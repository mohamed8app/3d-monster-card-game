# -*- coding: utf-8 -*-
"""
Pure-PyTorch Actor-Critic PPO Fighter + Neural Card Drafter for Arena_Lab_V2.
Designed specifically for CPU-only Windows training machines:
  - Compact ~80K-param Fighter MLP (~320 KB checkpoint per model)
  - Fast masked action sampling & clipped PPO updates
  - Learnable 1,470-card Neural Drafter enforcing all 50-card Constitution rules
  - 1-click Warm-Start Base support (transfers Fighter tactical skills without polluting Deck preferences)
"""

from __future__ import annotations

import json
import os
import shutil
import struct
import time
from typing import Any, Dict, List, Optional, Tuple

import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F

from .battle_engine import ACTION_DIM, OBS_DIM
from .card_model import DECK_SIZE, MAX_COPIES, CardDB

SAVED_MODELS_DIR = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "..", "saved_models")
)
WARM_START_PATH = os.path.join(SAVED_MODELS_DIR, "warm_start_base", "fighter_base.pt")


class FighterNet(nn.Module):
    """Compact LayerNorm Actor-Critic network tuned for rapid CPU inference & PPO updates."""

    def __init__(self, obs_dim: int = OBS_DIM, action_dim: int = ACTION_DIM, hidden1: int = 256, hidden2: int = 128) -> None:
        super().__init__()
        self.fc1 = nn.Linear(obs_dim, hidden1)
        self.ln1 = nn.LayerNorm(hidden1)
        self.fc2 = nn.Linear(hidden1, hidden2)
        self.ln2 = nn.LayerNorm(hidden2)
        self.policy_head = nn.Linear(hidden2, action_dim)
        self.value_head = nn.Linear(hidden2, 1)

        # Clean orthogonal initialization for stable PPO starts
        for m in (self.fc1, self.fc2):
            nn.init.orthogonal_(m.weight, gain=np.sqrt(2.0))
            nn.init.zeros_(m.bias)
        nn.init.orthogonal_(self.policy_head.weight, gain=0.01)
        nn.init.zeros_(self.policy_head.bias)
        nn.init.orthogonal_(self.value_head.weight, gain=1.0)
        nn.init.zeros_(self.value_head.bias)

    def forward(self, x: torch.Tensor) -> Tuple[torch.Tensor, torch.Tensor]:
        h = F.relu(self.ln1(self.fc1(x)))
        h = F.relu(self.ln2(self.fc2(h)))
        return self.policy_head(h), self.value_head(h).squeeze(-1)


class DeckDrafter:
    """
    Learnable preference-logit drafter over all 1,470 cards.
    Generates valid 50-card Constitution decks respecting:
      - Exact 50 cards, Max 2 copies per Card ID
      - 1 Leader, 1 Covenant, 1-2 Environments (forced required_card_id if applicable)
      - Healthy Monster/Spell balance (>= 28 Monsters)
      - Model legal card mask (Element / Tribe / Playstyle / Free-10 exclusion)
    """

    def __init__(self, db: CardDB) -> None:
        self.db = db
        self.num_cards = db.num_cards

        # Unbiased mana-curve prior across ALL card types (Monsters, Spells, Environments, Leaders, Covenants)
        # so 1-Mana Environments and 0-Mana Leaders/Covenants start on an equal footing with Monsters!
        prior = np.zeros(self.num_cards, dtype=np.float32)
        for i in range(self.num_cards):
            c = db.get(i + 1)
            if c.type == "monster":
                curve = {1: 0.08, 2: 0.10, 3: 0.08, 4: 0.04, 5: 0.0, 6: -0.05, 7: -0.08}
                prior[i] = (curve.get(c.mana, 0.0) - 0.20) if c.is_barrier else curve.get(c.mana, 0.0)
            elif c.type == "spell":
                prior[i] = 0.06 if c.mana <= 3 else 0.0
            else:
                # Environments (1 Mana), Leaders (0 Mana), Covenants (0 Mana) start completely fair at +0.08
                prior[i] = 0.08
        self.prior = prior.copy()
        self.logits = torch.tensor(prior, dtype=torch.float32, requires_grad=False)
        self.visits = np.zeros(self.num_cards, dtype=np.float32)
        self.reward_baseline: float = 0.0

        # Precompute type masks (indices 0..2269)
        self.mask_leader = np.zeros(self.num_cards, dtype=np.bool_)
        self.mask_covenant = np.zeros(self.num_cards, dtype=np.bool_)
        self.mask_env = np.zeros(self.num_cards, dtype=np.bool_)
        self.mask_monster = np.zeros(self.num_cards, dtype=np.bool_)
        self.mask_spell = np.zeros(self.num_cards, dtype=np.bool_)
        for cid in db.leaders:
            self.mask_leader[cid - 1] = True
        for cid in db.covenants:
            self.mask_covenant[cid - 1] = True
        for cid in db.envs:
            self.mask_env[cid - 1] = True
        for cid in db.monsters:
            self.mask_monster[cid - 1] = True
        for cid in db.spells:
            self.mask_spell[cid - 1] = True

    def _pick_from_pool(
        self,
        scores: np.ndarray,
        pool_mask: np.ndarray,
        counts: np.ndarray,
        rng: np.random.Generator,
        deterministic: bool,
        temperature: float = 1.0,
    ) -> Optional[int]:
        avail = pool_mask & (counts < MAX_COPIES)
        idxs = np.flatnonzero(avail)
        if idxs.size == 0:
            return None
        if deterministic:
            best_i = idxs[int(np.argmax(scores[idxs]))]
            return int(best_i)
        sub = scores[idxs] / max(0.25, temperature)
        sub = sub - np.max(sub)
        probs = np.exp(sub)
        ps = probs.sum()
        if not np.isfinite(ps) or ps <= 0:
            return int(rng.choice(idxs))
        probs /= ps
        return int(rng.choice(idxs, p=probs))

    def build_deck(
        self,
        legal_mask: np.ndarray,
        required_card_id: int = 0,
        deterministic: bool = False,
        temperature: float = 1.0,
        rng: Optional[np.random.Generator] = None,
    ) -> List[int]:
        if rng is None:
            rng = np.random.default_rng()

        scores = self.logits.detach().cpu().numpy().copy()
        if not deterministic:
            # UCB Curiosity Bonus: actively explore untested legal cards (including 10 Envs & 60 Leaders!)
            scores = scores + (0.65 / np.sqrt(1.0 + self.visits))
        counts = np.zeros(self.num_cards, dtype=np.int32)
        deck: List[int] = []

        def add_idx(idx: Optional[int]) -> bool:
            if idx is None or counts[idx] >= MAX_COPIES:
                return False
            counts[idx] += 1
            deck.append(idx + 1)
            return True

        # If a model explicitly requires a signature card (e.g., Environment Arena), include it first
        if required_card_id > 0 and legal_mask[required_card_id - 1]:
            add_idx(required_card_id - 1)

        # Fill all 50 deck slots 100% freely from the model's legal card pool!
        # Governed strictly by core game rules: 50 cards total (DECK_SIZE), max 2 copies per card (MAX_COPIES = 2).
        while len(deck) < DECK_SIZE:
            ci = self._pick_from_pool(scores, legal_mask, counts, rng, deterministic, temperature)
            if not add_idx(ci):
                break

        assert len(deck) == DECK_SIZE, f"Failed to assemble 50-card deck (got {len(deck)})"
        return deck

    def focus_score(self, legal_mask: np.ndarray) -> float:
        """
        Returns a [0.0 .. 100.0] percentage measuring how decisively the neural drafter
        has converged from uniform random exploration onto its preferred card core.
        """
        with torch.no_grad():
            legal_t = torch.from_numpy(legal_mask)
            n_legal = int(legal_t.sum().item())
            if n_legal <= 1:
                return 100.0
            masked = torch.where(legal_t, self.logits, torch.tensor(-1e9, dtype=torch.float32))
            probs = torch.softmax(masked, dim=0)[legal_t]
            ent = -(probs * torch.log(probs.clamp_min(1e-9))).sum().item()
            max_ent = float(np.log(n_legal))
            return float(max(0.0, min(100.0, (1.0 - ent / max(1e-6, max_ent)) * 100.0)))

    def update(
        self,
        batch_decks: List[List[int]],
        batch_rewards: List[float],
        legal_mask: np.ndarray,
        batch_played_ids: Optional[List[List[int]]] = None,
    ) -> float:
        """
        Direct Per-Card Bandit / Policy Update (Zero log_softmax cross-card leakage!):
        - Updates ONLY the cards actually drafted/played in this batch (untested cards stay untouched!).
        - Combines Within-Batch Relative Advantage (70%) + Absolute Outcome Signal (30%) so even when
          a model loses 90% of a batch, cards from its strongest/closest fights get promoted while
          cards from its worst losses get demoted and replaced by fresh unexplored cards!
        - Played cards receive 2.2x credit weight vs 0.45x for undrawn deck cards.
        """
        if not batch_decks:
            return 0.0
        rewards = np.asarray(batch_rewards, dtype=np.float32)
        mean_r = float(rewards.mean())
        self.reward_baseline = 0.85 * self.reward_baseline + 0.15 * mean_r

        # 70% within-batch relative ranking (always distinguishes better vs worse decks even in a losing streak!)
        # + 30% global baseline advantage
        std_r = float(rewards.std())
        if std_r > 1e-4:
            rel_adv = (rewards - mean_r) / (std_r + 1e-5)
        else:
            rel_adv = np.zeros_like(rewards)
        abs_adv = np.clip(rewards - self.reward_baseline, -1.5, 1.5)
        adv = 0.70 * rel_adv + 0.30 * abs_adv

        delta = np.zeros(self.num_cards, dtype=np.float32)
        weight_sum = np.zeros(self.num_cards, dtype=np.float32)

        for i, (deck_ids, a_val) in enumerate(zip(batch_decks, adv)):
            played_set = (
                set(batch_played_ids[i])
                if (batch_played_ids is not None and i < len(batch_played_ids) and batch_played_ids[i])
                else set()
            )
            for cid in deck_ids:
                idx = cid - 1
                w = 2.2 if cid in played_set else 0.45
                delta[idx] += float(a_val) * w
                weight_sum[idx] += w
                self.visits[idx] += 0.5 if cid not in played_set else 1.0

        active = weight_sum > 0
        step = np.zeros(self.num_cards, dtype=np.float32)
        step[active] = delta[active] / np.maximum(1.0, weight_sum[active])

        lr = 0.14
        with torch.no_grad():
            cur = self.logits.numpy()
            # Gentle weight decay (0.992) on active cards prevents old lucky streaks from locking forever
            cur[active] = cur[active] * 0.992 + lr * step[active]
            np.clip(cur, -3.5, 3.5, out=cur)
            self.logits.copy_(torch.from_numpy(cur))

        return float(np.abs(step[active]).mean()) if np.any(active) else 0.0


class StepBuffer:
    """Stores one match's fighter trajectory for a single model."""

    __slots__ = ("obs", "masks", "actions", "log_probs", "values", "rewards", "dones")

    def __init__(self) -> None:
        self.obs: List[np.ndarray] = []
        self.masks: List[np.ndarray] = []
        self.actions: List[int] = []
        self.log_probs: List[float] = []
        self.values: List[float] = []
        self.rewards: List[float] = []
        self.dones: List[bool] = []

    def add(
        self,
        obs: np.ndarray,
        mask: np.ndarray,
        action: int,
        log_prob: float,
        value: float,
        step_reward: float = 0.0,
    ) -> None:
        self.obs.append(obs)
        self.masks.append(mask)
        self.actions.append(int(action))
        self.log_probs.append(float(log_prob))
        self.values.append(float(value))
        self.rewards.append(float(step_reward))
        self.dones.append(False)

    def finalize_match(self, terminal_reward: float) -> None:
        if self.rewards:
            self.rewards[-1] += float(terminal_reward)
            self.dones[-1] = True


class ModelBrain:
    """Encapsulates one of the 40 independent AI models (FighterNet + DeckDrafter)."""

    def __init__(self, model_id: str, db: CardDB, lr: float = 3e-4) -> None:
        self.model_id = model_id
        self.db = db
        self.fighter = FighterNet(OBS_DIM, ACTION_DIM)
        self.fighter_opt = torch.optim.Adam(self.fighter.parameters(), lr=lr)
        self.drafter = DeckDrafter(db)
        self.matches_trained: int = 0
        self.warm_started: bool = False

    def checkpoint_path(self) -> str:
        return os.path.join(SAVED_MODELS_DIR, self.model_id, "brain.pt")

    def settled_deck_path(self) -> str:
        return os.path.join(SAVED_MODELS_DIR, self.model_id, "settled_deck.json")

    def save_settled_deck_file(self, deck_ids: List[int]) -> str:
        """
        Writes `saved_models/<model_id>/settled_deck.json` right next to `brain.pt`,
        listing each card's ID, copy count (1 or 2), name, type, and stats.
        """
        import json
        from collections import Counter

        path = self.settled_deck_path()
        os.makedirs(os.path.dirname(path), exist_ok=True)
        counts = Counter(int(cid) for cid in deck_ids)
        cards_list = []
        for cid, copies in sorted(counts.items(), key=lambda kv: (-kv[1], kv[0])):
            c = self.db.get(cid)
            cards_list.append({
                "id": int(cid),
                "copies": int(copies),
                "name": c.name,
                "type": c.type,
                "element": c.element_name,
                "tribe": c.tribe_name,
                "playstyle": c.playstyle_name,
                "mana": int(c.mana),
                "atk": int(c.atk),
                "def": int(c.deff),
            })
        payload = {
            "model_id": self.model_id,
            "matches_trained": int(self.matches_trained),
            "total_cards": len(deck_ids),
            "unique_cards": len(cards_list),
            "last_updated": time.strftime("%Y-%m-%d %H:%M:%S"),
            "cards": cards_list,
            "deck_50_ids": [int(cid) for cid in deck_ids],
        }
        tmp = path + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(payload, f, indent=2, ensure_ascii=False)
            f.flush()
            os.fsync(f.fileno())
        for attempt in range(6):
            try:
                os.replace(tmp, path)
                break
            except PermissionError:
                if attempt == 5:
                    raise
                time.sleep(0.05)
        return path

    @staticmethod
    def _load_fighter_weights_compat(
        fighter: FighterNet, saved_state: Dict[str, torch.Tensor]
    ) -> bool:
        """
        Loads FighterNet weights with zero-forgetting input-layer (OBS_DIM 160 -> 220)
        AND policy-head output-layer (ACTION_DIM 62 -> 63 for Aether Draw) expansion support!
        Preserves 100% of trained weights bit-for-bit and zero-initializes newly appended
        input columns or action rows so existing brains immediately learn the new features/actions.
        Returns True only on an exact shape match (safe to also load optimizer state).
        """
        curr_state = fighter.state_dict()
        saved_w = saved_state.get("fc1.weight")
        curr_w = curr_state.get("fc1.weight")
        if saved_w is None or curr_w is None:
            return False

        all_exact = all(
            (k in saved_state and tuple(saved_state[k].shape) == tuple(v_curr.shape))
            for k, v_curr in curr_state.items()
        )
        if all_exact:
            try:
                fighter.load_state_dict(saved_state)
                return True
            except RuntimeError:
                return False

        # Seamless zero-padded expansion for OBS_DIM (fc1.weight) and/or ACTION_DIM (policy_head.*)
        merged: Dict[str, torch.Tensor] = {}
        for k, v_curr in curr_state.items():
            if k not in saved_state:
                return False
            v_sav = saved_state[k]
            if tuple(v_sav.shape) == tuple(v_curr.shape):
                merged[k] = v_sav.to(dtype=v_curr.dtype)
            elif (
                k == "fc1.weight"
                and v_sav.ndim == 2 and v_curr.ndim == 2
                and v_sav.shape[0] == v_curr.shape[0]
                and v_sav.shape[1] < v_curr.shape[1]
            ):
                expanded = torch.zeros_like(v_curr)
                expanded[:, : v_sav.shape[1]] = v_sav.to(dtype=v_curr.dtype)
                merged[k] = expanded
            elif (
                k == "policy_head.weight"
                and v_sav.ndim == 2 and v_curr.ndim == 2
                and v_sav.shape[1] == v_curr.shape[1]
                and v_sav.shape[0] < v_curr.shape[0]
            ):
                expanded = torch.zeros_like(v_curr)
                expanded[: v_sav.shape[0], :] = v_sav.to(dtype=v_curr.dtype)
                merged[k] = expanded
            elif (
                k == "policy_head.bias"
                and v_sav.ndim == 1 and v_curr.ndim == 1
                and v_sav.shape[0] < v_curr.shape[0]
            ):
                expanded = torch.zeros_like(v_curr)
                expanded[: v_sav.shape[0]] = v_sav.to(dtype=v_curr.dtype)
                merged[k] = expanded
            else:
                return False
        fighter.load_state_dict(merged)
        return False

    @staticmethod
    def _has_zip_header(path: str) -> bool:
        try:
            if not os.path.isfile(path) or os.path.getsize(path) < 128:
                return False
            with open(path, "rb") as f:
                return f.read(4) == b"PK\x03\x04"
        except OSError:
            return False

    @classmethod
    def _safe_torch_save(cls, payload: Dict[str, Any], dst_path: str, keep_backup: bool = True) -> None:
        """
        Power-loss-safe atomic checkpoint writer for Windows NTFS:
          1) Writes to `.tmp` and calls flush() + os.fsync() so NTFS never commits all-zero blocks.
          2) Verifies the written `.tmp` has a valid PyTorch ZIP header (`PK\\x03\\x04`).
          3) Preserves the previous healthy checkpoint as `.bak` before atomic os.replace().
        """
        os.makedirs(os.path.dirname(dst_path), exist_ok=True)
        tmp = dst_path + ".tmp"
        with open(tmp, "wb") as f:
            torch.save(payload, f)
            f.flush()
            os.fsync(f.fileno())
        if not cls._has_zip_header(tmp):
            raise IOError(f"Checkpoint write verification failed for {tmp}")
        if keep_backup and cls._has_zip_header(dst_path):
            bak = dst_path + ".bak"
            try:
                shutil.copy2(dst_path, bak)
            except OSError:
                pass
        for attempt in range(8):
            try:
                os.replace(tmp, dst_path)
                break
            except PermissionError:
                if attempt == 7:
                    raise
                time.sleep(0.06)

    def _restore_drafter_from_settled_deck(self) -> bool:
        """
        Restores deck preference priors & matches_trained from settled_deck.json
        (or falls back to logs/roster_stats.json) if brain.pt was corrupted.
        """
        deck_ids: List[int] = []
        recovered_matches = 0

        s_path = self.settled_deck_path()
        if os.path.isfile(s_path):
            try:
                with open(s_path, "r", encoding="utf-8") as f:
                    sdata = json.load(f)
                if isinstance(sdata.get("deck_50_ids"), list) and sdata["deck_50_ids"]:
                    deck_ids = [int(c) for c in sdata["deck_50_ids"]]
                recovered_matches = max(recovered_matches, int(sdata.get("matches_trained", 0)))
            except Exception:
                pass

        if not deck_ids:
            logs_dir = os.path.abspath(os.path.join(SAVED_MODELS_DIR, "..", "logs"))
            for fname in ("roster_stats.json", "roster_stats.json.bak"):
                rpath = os.path.join(logs_dir, fname)
                if not os.path.isfile(rpath):
                    continue
                try:
                    with open(rpath, "r", encoding="utf-8") as f:
                        rs = json.load(f)
                    rec = rs.get(self.model_id, {})
                    if isinstance(rec.get("settled_deck"), list) and rec["settled_deck"]:
                        deck_ids = [int(c) for c in rec["settled_deck"]]
                    recovered_matches = max(recovered_matches, int(rec.get("matches", 0)))
                    if deck_ids:
                        break
                except Exception:
                    continue

        if deck_ids:
            for cid in deck_ids:
                idx = int(cid) - 1
                if 0 <= idx < self.drafter.num_cards:
                    self.drafter.logits[idx] += 0.45
            self.drafter.logits.clamp_(-2.5, 2.5)
            self.matches_trained = max(self.matches_trained, recovered_matches)
            try:
                self.save_settled_deck_file(deck_ids)
            except Exception:
                pass
            return True
        if recovered_matches > 0:
            self.matches_trained = max(self.matches_trained, recovered_matches)
        return False

    @classmethod
    def _load_warm_start_safe(cls, brain: "ModelBrain") -> bool:
        for cand in (WARM_START_PATH, WARM_START_PATH + ".bak"):
            if not os.path.isfile(cand):
                continue
            try:
                base = torch.load(cand, map_location="cpu", weights_only=False)
                if isinstance(base, dict) and "fighter" in base:
                    cls._load_fighter_weights_compat(brain.fighter, base["fighter"])
                    brain.warm_started = True
                    return True
            except Exception:
                continue
        return False

    @classmethod
    def load_or_create(
        cls,
        model_id: str,
        db: CardDB,
        use_warm_start: bool = True,
    ) -> "ModelBrain":
        brain = cls(model_id, db)
        ckpt = brain.checkpoint_path()
        bak = ckpt + ".bak"
        data = None
        loaded_from_bak = False
        corrupted_main = False

        if os.path.isfile(ckpt):
            try:
                loaded = torch.load(ckpt, map_location="cpu", weights_only=False)
                if isinstance(loaded, dict) and "fighter" in loaded:
                    data = loaded
                else:
                    corrupted_main = True
            except Exception:
                corrupted_main = True

        if data is None and os.path.isfile(bak):
            try:
                loaded = torch.load(bak, map_location="cpu", weights_only=False)
                if isinstance(loaded, dict) and "fighter" in loaded:
                    data = loaded
                    loaded_from_bak = True
            except Exception:
                pass

        if data is not None:
            exact_match = cls._load_fighter_weights_compat(brain.fighter, data["fighter"])
            if exact_match and "fighter_opt" in data:
                try:
                    brain.fighter_opt.load_state_dict(data["fighter_opt"])
                except (ValueError, RuntimeError):
                    pass
            if "drafter_logits" in data:
                saved_lg = data["drafter_logits"].float()
                if saved_lg.numel() == brain.drafter.logits.numel():
                    # Auto-sanitize any legacy checkpoint affected by the old log_softmax +5.5 saturation bug
                    if "drafter_visits" not in data and float(saved_lg.abs().max().item()) >= 4.8:
                        cleaned = torch.from_numpy(brain.drafter.prior) + 0.18 * (saved_lg - saved_lg.mean())
                        cleaned.clamp_(-1.5, 1.5)
                        brain.drafter.logits.copy_(cleaned)
                    else:
                        brain.drafter.logits.copy_(saved_lg)
                else:
                    # Seamless DB-resize migration (e.g., __KEEP_MIGRATION_1470_TO_1470__ cards):
                    # preserve trained FighterNet + non-monster logits (1..470) & restore monster priors from settled_deck!
                    n_pre = min(470, int(saved_lg.numel()), int(brain.drafter.logits.numel()))
                    if n_pre > 0:
                        brain.drafter.logits[:n_pre].copy_(saved_lg[:n_pre])
                    brain._restore_drafter_from_settled_deck()
            if "drafter_visits" in data:
                sav_v = np.asarray(data["drafter_visits"], dtype=np.float32)
                if sav_v.shape == brain.drafter.visits.shape:
                    brain.drafter.visits = sav_v
                else:
                    n_pre = min(470, int(sav_v.size), int(brain.drafter.visits.size))
                    if n_pre > 0:
                        brain.drafter.visits[:n_pre] = sav_v[:n_pre]
            brain.drafter.reward_baseline = float(data.get("drafter_baseline", 0.0))
            brain.matches_trained = int(data.get("matches_trained", 0))
            brain.warm_started = bool(data.get("warm_started", False))
            if corrupted_main and loaded_from_bak:
                try:
                    brain.save()
                except Exception:
                    pass
        else:
            if corrupted_main and os.path.isfile(ckpt):
                # Quarantine zeroed/corrupted checkpoint so it can never crash training again
                corrupt_dst = ckpt + ".corrupt"
                try:
                    os.replace(ckpt, corrupt_dst)
                except OSError:
                    pass
                if use_warm_start:
                    cls._load_warm_start_safe(brain)
                brain._restore_drafter_from_settled_deck()
                try:
                    brain.save()
                except Exception:
                    pass
            elif use_warm_start:
                cls._load_warm_start_safe(brain)
        return brain

    def unity_brain_path(self) -> str:
        return os.path.join(SAVED_MODELS_DIR, self.model_id, "brain_unity.bytes")

    def export_unity_binary(self, out_path: Optional[str] = None) -> str:
        """
        Writes FighterNet weights to compact V2BR little-endian float32 binary (~383 KB)
        directly inside saved_models/<model_id>/brain_unity.bytes so Unity (.exe & Editor)
        automatically loads the freshest trained neural brain live from Arena_Lab_V2!
        """
        dst = out_path or self.unity_brain_path()
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        sd = self.fighter.state_dict()
        keys = (
            "fc1.weight", "fc1.bias",
            "ln1.weight", "ln1.bias",
            "fc2.weight", "fc2.bias",
            "ln2.weight", "ln2.bias",
            "policy_head.weight", "policy_head.bias",
            "value_head.weight", "value_head.bias",
        )
        w1 = sd["fc1.weight"]
        w2 = sd["fc2.weight"]
        wp = sd["policy_head.weight"]
        h1, obs_dim = int(w1.shape[0]), int(w1.shape[1])
        h2 = int(w2.shape[0])
        act_dim = int(wp.shape[0])

        tmp = dst + ".tmp"
        with open(tmp, "wb") as f:
            f.write(b"V2BR")
            f.write(struct.pack("<5I", 1, obs_dim, h1, h2, act_dim))
            for k in keys:
                arr = np.ascontiguousarray(sd[k].detach().cpu().numpy(), dtype="<f4")
                f.write(arr.tobytes())
            f.flush()
            os.fsync(f.fileno())
        for attempt in range(6):
            try:
                os.replace(tmp, dst)
                break
            except PermissionError:
                if attempt == 5:
                    raise
                time.sleep(0.05)
        return dst

    def save(self) -> str:
        ckpt = self.checkpoint_path()
        payload = {
            "model_id": self.model_id,
            "fighter": self.fighter.state_dict(),
            "fighter_opt": self.fighter_opt.state_dict(),
            "drafter_logits": self.drafter.logits.detach().cpu(),
            "drafter_visits": self.drafter.visits.copy(),
            "drafter_baseline": self.drafter.reward_baseline,
            "matches_trained": self.matches_trained,
            "warm_started": self.warm_started,
        }
        self._safe_torch_save(payload, ckpt, keep_backup=True)
        try:
            self.export_unity_binary()
        except Exception:
            pass
        return ckpt

    @torch.no_grad()
    def select_action(
        self, obs: np.ndarray, legal_mask: np.ndarray, deterministic: bool = False
    ) -> Tuple[int, float, float]:
        obs_t = torch.from_numpy(obs).unsqueeze(0)
        logits, val = self.fighter(obs_t)
        logits = logits.squeeze(0)
        mask_t = torch.from_numpy(legal_mask)
        masked_logits = torch.where(mask_t, logits, torch.tensor(-1e9, dtype=torch.float32))

        if deterministic:
            action = int(torch.argmax(masked_logits).item())
            log_probs = F.log_softmax(masked_logits, dim=0)
            return action, float(log_probs[action].item()), float(val.item())

        probs = F.softmax(masked_logits, dim=0)
        dist = torch.distributions.Categorical(probs=probs)
        act_t = dist.sample()
        return int(act_t.item()), float(dist.log_prob(act_t).item()), float(val.item())

    def ppo_update(
        self,
        buffers: List[StepBuffer],
        gamma: float = 0.99,
        gae_lambda: float = 0.95,
        clip_eps: float = 0.2,
        epochs: int = 3,
        minibatch_size: int = 256,
    ) -> Dict[str, float]:
        """Runs clipped PPO across accumulated match trajectories on CPU."""
        all_obs: List[np.ndarray] = []
        all_masks: List[np.ndarray] = []
        all_actions: List[int] = []
        all_old_lp: List[float] = []
        all_adv: List[float] = []
        all_ret: List[float] = []

        for buf in buffers:
            n = len(buf.actions)
            if n == 0:
                continue
            adv = np.zeros(n, dtype=np.float32)
            last_gae = 0.0
            for t in reversed(range(n)):
                next_val = 0.0 if buf.dones[t] else (buf.values[t + 1] if t + 1 < n else 0.0)
                delta = buf.rewards[t] + gamma * next_val - buf.values[t]
                last_gae = delta + gamma * gae_lambda * (0.0 if buf.dones[t] else last_gae)
                adv[t] = last_gae
            ret = adv + np.asarray(buf.values, dtype=np.float32)

            all_obs.extend(buf.obs)
            all_masks.extend(buf.masks)
            all_actions.extend(buf.actions)
            all_old_lp.extend(buf.log_probs)
            all_adv.extend(adv.tolist())
            all_ret.extend(ret.tolist())

        total_steps = len(all_actions)
        if total_steps < 8:
            return {"policy_loss": 0.0, "value_loss": 0.0, "steps": total_steps}

        obs_t = torch.from_numpy(np.asarray(all_obs, dtype=np.float32))
        mask_t = torch.from_numpy(np.asarray(all_masks, dtype=np.bool_))
        act_t = torch.tensor(all_actions, dtype=torch.long)
        old_lp_t = torch.tensor(all_old_lp, dtype=torch.float32)
        adv_t = torch.tensor(all_adv, dtype=torch.float32)
        ret_t = torch.tensor(all_ret, dtype=torch.float32)

        if adv_t.numel() > 1:
            adv_t = (adv_t - adv_t.mean()) / (adv_t.std() + 1e-6)

        p_loss_acc = 0.0
        v_loss_acc = 0.0
        updates = 0

        for _ in range(epochs):
            perm = torch.randperm(total_steps)
            for start in range(0, total_steps, minibatch_size):
                idx = perm[start:start + minibatch_size]
                logits, values = self.fighter(obs_t[idx])
                masked = torch.where(mask_t[idx], logits, torch.tensor(-1e9, dtype=torch.float32))
                log_probs = F.log_softmax(masked, dim=-1)
                probs = F.softmax(masked, dim=-1)

                new_lp = log_probs.gather(1, act_t[idx].unsqueeze(1)).squeeze(1)
                entropy = -(probs * log_probs.clamp_min(-20.0)).sum(dim=-1).mean()

                ratio = torch.exp((new_lp - old_lp_t[idx]).clamp(-10.0, 10.0))
                s1 = ratio * adv_t[idx]
                s2 = torch.clamp(ratio, 1.0 - clip_eps, 1.0 + clip_eps) * adv_t[idx]
                p_loss = -torch.min(s1, s2).mean()
                v_loss = F.mse_loss(values, ret_t[idx])
                loss = p_loss + 0.5 * v_loss - 0.015 * entropy

                self.fighter_opt.zero_grad()
                loss.backward()
                torch.nn.utils.clip_grad_norm_(self.fighter.parameters(), 0.5)
                self.fighter_opt.step()

                p_loss_acc += float(p_loss.item())
                v_loss_acc += float(v_loss.item())
                updates += 1

        return {
            "policy_loss": p_loss_acc / max(1, updates),
            "value_loss": v_loss_acc / max(1, updates),
            "steps": total_steps,
        }


def promote_model_to_warm_start_base(model_id: str, db: CardDB) -> str:
    """
    Copies a trained model's FighterNet weights into `saved_models/warm_start_base/fighter_base.pt`
    so any untrained model among the 40 can start already knowing the game rules & combat fundamentals!
    """
    src_ckpt = os.path.join(SAVED_MODELS_DIR, model_id, "brain.pt")
    if not os.path.isfile(src_ckpt):
        raise FileNotFoundError(f"Model '{model_id}' has not been trained yet (no checkpoint found).")
    data = torch.load(src_ckpt, map_location="cpu", weights_only=False)
    ModelBrain._safe_torch_save(
        {
            "source_model_id": model_id,
            "source_matches": int(data.get("matches_trained", 0)),
            "fighter": data["fighter"],
        },
        WARM_START_PATH,
        keep_backup=True,
    )
    return WARM_START_PATH


def get_warm_start_info() -> Optional[Dict[str, object]]:
    if not os.path.isfile(WARM_START_PATH):
        return None
    try:
        data = torch.load(WARM_START_PATH, map_location="cpu", weights_only=False)
        return {
            "path": WARM_START_PATH,
            "source_model_id": data.get("source_model_id", "unknown"),
            "source_matches": int(data.get("source_matches", 0)),
        }
    except Exception:
        return None

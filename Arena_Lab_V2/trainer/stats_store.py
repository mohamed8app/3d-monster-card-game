# -*- coding: utf-8 -*-
"""
Atomic JSON / JSONL persistence for Arena_Lab_V2 statistics, ELO ratings,
Free-10 disjoint deck locks, and 2,270-card telemetry.
"""

from __future__ import annotations

import json
import os
import shutil
import time
from typing import Any, Dict, List, Optional, Set, Tuple

from engine.card_model import MatchResult
from engine.roster_40 import ROSTER_40, ROSTER_MAP

LOGS_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "logs"))
ROSTER_STATS_PATH = os.path.join(LOGS_DIR, "roster_stats.json")
FREE10_DECKS_PATH = os.path.join(LOGS_DIR, "free10_latest_decks.json")
CARD_STATS_PATH = os.path.join(LOGS_DIR, "card_stats.json")
MATCH_HISTORY_PATH = os.path.join(LOGS_DIR, "match_history.jsonl")


def _atomic_write_json(path: str, data: Any) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = f"{path}.{os.getpid()}.tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.flush()
        os.fsync(f.fileno())
    for attempt in range(12):
        try:
            if os.path.isfile(path) and os.path.getsize(path) > 64:
                try:
                    shutil.copy2(path, path + ".bak")
                except Exception:
                    pass
            os.replace(tmp, path)
            return
        except PermissionError:
            if attempt == 11:
                try:
                    os.remove(tmp)
                except Exception:
                    pass
                raise
            time.sleep(0.06)


def _read_json(path: str, default: Any) -> Any:
    for candidate in (path, path + ".bak"):
        if not os.path.isfile(candidate):
            continue
        for attempt in range(10):
            try:
                with open(candidate, "r", encoding="utf-8") as f:
                    txt = f.read()
                if not txt.strip():
                    break
                return json.loads(txt)
            except PermissionError:
                time.sleep(0.05)
            except Exception:
                time.sleep(0.03)
                break
    return default


def _default_model_record(model_id: str) -> Dict[str, Any]:
    spec = ROSTER_MAP[model_id]
    return {
        "model_id": model_id,
        "display_name": spec.display_name,
        "category": spec.category,
        "matches": 0,
        "wins": 0,
        "losses": 0,
        "draws": 0,
        "elo": 1000.0,
        "avg_style_score": 0.0,
        "avg_turns": 0.0,
        "avg_final_hp": 0.0,
        "avg_dmg_face": 0.0,
        "avg_dmg_monsters": 0.0,
        "avg_dot_dmg": 0.0,
        "avg_healed": 0.0,
        "avg_reflect": 0.0,
        "avg_kills": 0.0,
        "avg_spells": 0.0,
        "avg_actives": 0.0,
        "avg_tokens": 0.0,
        "avg_disruptions": 0.0,
        "avg_excitement": 0.0,
        "avg_hand_start": 0.0,
        "avg_hand_end": 0.0,
        "empty_hand_pct": 0.0,
        "avg_unit_lifespan": 0.0,
        "died_0atk_pct": 0.0,
        "avg_board_units": 0.0,
        "comeback_wins": 0,
        "clutch_wins": 0,
        "max_burst_dmg": 0,
        "warm_started": False,
        "settled_deck": [],
        "last_updated": "",
    }


def _default_excitement_data() -> Dict[str, Any]:
    return {
        "total_matches": 0,
        "avg_excitement": 0.0,
        "avg_hand_start": 0.0,
        "avg_hand_end": 0.0,
        "empty_hand_pct": 0.0,
        "avg_unit_lifespan": 0.0,
        "died_0atk_pct": 0.0,
        "avg_board_units": 0.0,
        "comeback_wins": 0,
        "clutch_wins": 0,
        "total_lead_swings": 0,
        "turn_curve": {
            str(t): {"turns": 0, "dmg": 0, "kills": 0, "swings": 0, "bursts": 0, "highlights": 0, "finishes": 0, "hs": 0, "he": 0, "hempty": 0}
            for t in range(1, 26)
        },
        "highlights": {},
        "anomalies": [],
    }


class StatsStore:
    """Thread-safe-at-batch-boundary persistent statistics store."""

    def __init__(self, logs_dir: str = LOGS_DIR) -> None:
        self.logs_dir = logs_dir
        self.roster_path = os.path.join(logs_dir, "roster_stats.json")
        self.free10_path = os.path.join(logs_dir, "free10_latest_decks.json")
        self.card_stats_path = os.path.join(logs_dir, "card_stats.json")
        self.model_card_stats_path = os.path.join(logs_dir, "model_card_stats.json")
        self.h2h_path = os.path.join(logs_dir, "head_to_head.json")
        self.history_path = os.path.join(logs_dir, "match_history.jsonl")
        self.excitement_path = os.path.join(logs_dir, "excitement_anomalies.json")

        os.makedirs(self.logs_dir, exist_ok=True)
        self.roster: Dict[str, Dict[str, Any]] = {}
        self.free10_decks: Dict[str, List[int]] = {}
        self.card_stats: Dict[str, Dict[str, int]] = {}
        self.model_card_stats: Dict[str, Dict[str, List[int]]] = {}
        self.head_to_head: Dict[str, Dict[str, int]] = {}
        self.excitement: Dict[str, Any] = _default_excitement_data()
        self.reload()

    def reload(self) -> None:
        raw_roster = _read_json(self.roster_path, {})
        self.roster = {}
        for spec in ROSTER_40:
            rec = _default_model_record(spec.model_id)
            if spec.model_id in raw_roster:
                rec.update(raw_roster[spec.model_id])
            self.roster[spec.model_id] = rec

        self.free10_decks = _read_json(self.free10_path, {})
        self.card_stats = _read_json(self.card_stats_path, {})
        self.model_card_stats = _read_json(self.model_card_stats_path, {})
        self.head_to_head = _read_json(self.h2h_path, {})

        raw_ex = _read_json(self.excitement_path, {})
        self.excitement = _default_excitement_data()
        if isinstance(raw_ex, dict):
            for k in (
                "total_matches", "total_turns_sum", "p1_games", "p1_wins",
                "avg_excitement", "avg_hand_start", "avg_hand_end", "empty_hand_pct",
                "comeback_wins", "clutch_wins", "total_lead_swings", "highlights", "anomalies",
            ):
                if k in raw_ex:
                    self.excitement[k] = raw_ex[k]
            for dict_key in ("match_len_bins", "monster_survival", "kill_causes", "dmg_sources"):
                if dict_key in raw_ex and isinstance(raw_ex[dict_key], dict):
                    self.excitement[dict_key].update(raw_ex[dict_key])
            if "turn_curve" in raw_ex and isinstance(raw_ex["turn_curve"], dict):
                for tk_str, tv in raw_ex["turn_curve"].items():
                    if tk_str in self.excitement["turn_curve"] and isinstance(tv, dict):
                        self.excitement["turn_curve"][tk_str].update(tv)

        # Self-healing recovery: if roster_stats.json or head_to_head lost old data while
        # match_history.jsonl and/or saved_models/*/brain.pt still have the overnight training,
        # automatically reconstruct and heal all stats from match_history.jsonl + brain.pt!
        self._auto_recover_from_history_and_brains()

    def _auto_recover_from_history_and_brains(self) -> None:
        saved_models_root = os.path.abspath(os.path.join(self.logs_dir, "..", "saved_models"))
        hist_recs: Dict[str, Dict[str, Any]] = {
            spec.model_id: {
                "m": 0, "w": 0, "l": 0, "d": 0, "elo": 1000.0,
                "turns_sum": 0.0, "hp_sum": 0.0, "style_sum": 0.0, "ts": "",
            }
            for spec in ROSTER_40
        }
        hist_h2h: Dict[str, Dict[str, Any]] = {}
        hist_total_lines = 0

        if os.path.isfile(self.history_path):
            try:
                with open(self.history_path, "r", encoding="utf-8") as hf:
                    for ln in hf:
                        ln = ln.strip()
                        if not ln:
                            continue
                        try:
                            row = json.loads(ln)
                        except Exception:
                            continue
                        ma, mb = row.get("model_a", ""), row.get("model_b", "")
                        if ma not in hist_recs or mb not in hist_recs:
                            continue
                        hist_total_lines += 1
                        w_id = row.get("winner", "DRAW")
                        turns = int(row.get("turns", 12))
                        hp_a = float(row.get("hp_a", 0))
                        hp_b = float(row.get("hp_b", 0))
                        st_a = float(row.get("style_a", 0.0))
                        st_b = float(row.get("style_b", 0.0))
                        ts = str(row.get("ts", ""))

                        ra = hist_recs[ma]
                        rb = hist_recs[mb]
                        if ma != mb:
                            ea = 1.0 / (1.0 + 10.0 ** ((rb["elo"] - ra["elo"]) / 400.0))
                            eb = 1.0 - ea
                            sa = 1.0 if w_id == ma else (0.0 if w_id == mb else 0.5)
                            sb = 1.0 - sa
                            ra["elo"] = round(ra["elo"] + 20.0 * (sa - ea), 2)
                            rb["elo"] = round(rb["elo"] + 20.0 * (sb - eb), 2)

                        for mid_cur, r_cur, hp_cur, st_cur in ((ma, ra, hp_a, st_a), (mb, rb, hp_b, st_b)):
                            r_cur["m"] += 1
                            if w_id == mid_cur:
                                r_cur["w"] += 1
                            elif w_id == "DRAW":
                                r_cur["d"] += 1
                            else:
                                r_cur["l"] += 1
                            r_cur["turns_sum"] += turns
                            r_cur["hp_sum"] += hp_cur
                            r_cur["style_sum"] += st_cur
                            if ts:
                                r_cur["ts"] = ts

                        a, b = (ma, mb) if ma <= mb else (mb, ma)
                        key = f"{a}|{b}"
                        rec_h = hist_h2h.get(key)
                        if rec_h is None:
                            rec_h = {"a": a, "b": b, "m": 0, "wa": 0, "wb": 0, "d": 0, "turns": 0}
                            hist_h2h[key] = rec_h
                        rec_h["m"] += 1
                        rec_h["turns"] += turns
                        if w_id == a:
                            rec_h["wa"] += 1
                        elif w_id == b:
                            rec_h["wb"] += 1
                        else:
                            rec_h["d"] += 1
            except Exception:
                pass

        changed = False

        # 1. Heal head_to_head if history has more matches than head_to_head.json
        cur_h2h_sum = sum(int(v.get("m", 0)) for v in self.head_to_head.values()) if self.head_to_head else 0
        if hist_total_lines > cur_h2h_sum and hist_h2h:
            self.head_to_head = hist_h2h
            changed = True

        # 2. Check each model against match_history.jsonl AND saved_models/<model_id>/brain.pt + settled_deck.json
        for spec in ROSTER_40:
            mid = spec.model_id
            rec = self.roster[mid]
            hr = hist_recs[mid]

            # Recover settled_deck from settled_deck.json if missing in roster_stats.json
            sdeck_path = os.path.join(saved_models_root, mid, "settled_deck.json")
            sdeck_matches = 0
            if os.path.isfile(sdeck_path):
                sd_data = _read_json(sdeck_path, {})
                if isinstance(sd_data, dict):
                    sdeck_matches = int(sd_data.get("matches_trained", 0))
                    sd_ids = sd_data.get("deck_50_ids") or []
                    if len(sd_ids) == 50 and not rec.get("settled_deck"):
                        rec["settled_deck"] = [int(x) for x in sd_ids]
                        if mid.startswith("free_"):
                            self.free10_decks[mid] = rec["settled_deck"]
                        changed = True

            # Check brain.pt for true matches_trained & drafter visits/logits if needed
            ckpt_path = os.path.join(saved_models_root, mid, "brain.pt")
            brain_matches = sdeck_matches
            brain_data = None
            if os.path.isfile(ckpt_path):
                try:
                    import torch
                    brain_data = torch.load(ckpt_path, map_location="cpu", weights_only=False)
                    brain_matches = max(brain_matches, int(brain_data.get("matches_trained", 0)))
                    if bool(brain_data.get("warm_started", False)) and not rec.get("warm_started"):
                        rec["warm_started"] = True
                        changed = True
                except Exception:
                    pass

            true_m = max(int(rec.get("matches", 0)), int(hr["m"]), int(brain_matches))
            if true_m > int(rec.get("matches", 0)):
                changed = True
                if hr["m"] > 0:
                    if true_m > hr["m"]:
                        scale = true_m / float(hr["m"])
                        w_scaled = int(round(hr["w"] * scale))
                        d_scaled = int(round(hr["d"] * scale))
                        l_scaled = max(0, true_m - w_scaled - d_scaled)
                        rec["matches"] = true_m
                        rec["wins"] = w_scaled
                        rec["losses"] = l_scaled
                        rec["draws"] = d_scaled
                    else:
                        rec["matches"] = hr["m"]
                        rec["wins"] = hr["w"]
                        rec["losses"] = hr["l"]
                        rec["draws"] = hr["d"]
                    rec["elo"] = hr["elo"]
                    rec["avg_turns"] = round(hr["turns_sum"] / hr["m"], 2)
                    rec["avg_final_hp"] = round(hr["hp_sum"] / hr["m"], 1)
                    rec["avg_style_score"] = round(hr["style_sum"] / hr["m"], 4)
                    if hr["ts"]:
                        rec["last_updated"] = hr["ts"]
                else:
                    # Fallback if history file had no lines for this model but brain.pt has matches_trained
                    base_elo = float(brain_data.get("drafter_baseline", 0.0)) if isinstance(brain_data, dict) else 0.0
                    est_wr = max(0.15, min(0.85, 0.50 + 0.25 * base_elo))
                    w_est = int(round(true_m * est_wr))
                    rec["matches"] = true_m
                    rec["wins"] = w_est
                    rec["losses"] = max(0, true_m - w_est)
                    rec["draws"] = 0
                    rec["elo"] = round(1000.0 + (est_wr - 0.50) * 400.0, 2)
                    if not rec.get("last_updated"):
                        rec["last_updated"] = time.strftime("%Y-%m-%d %H:%M:%S")

            # If settled_deck is still empty and brain_data is loaded, build settled_deck from drafter_logits
            if not rec.get("settled_deck") and isinstance(brain_data, dict) and "drafter_logits" in brain_data:
                try:
                    from engine.card_model import get_card_db
                    from engine.roster_40 import build_legal_card_mask, get_model_spec
                    from engine.ai_brain import ModelBrain
                    db = get_card_db()
                    sp = get_model_spec(mid)
                    b_obj = ModelBrain.load_or_create(mid, db)
                    ex = self.get_free10_excluded_ids(mid) if sp.category == "Free" else None
                    s_deck = b_obj.drafter.build_deck(build_legal_card_mask(sp, db, ex), sp.required_card_id, deterministic=True)
                    rec["settled_deck"] = [int(x) for x in s_deck]
                    if mid.startswith("free_"):
                        self.free10_decks[mid] = rec["settled_deck"]
                    b_obj.save_settled_deck_file(s_deck)
                    changed = True
                except Exception:
                    pass

            # Backfill per-model card stats from brain.pt visits & logits if missing
            if rec.get("matches", 0) > 0 and isinstance(brain_data, dict) and not self.model_card_stats.get(mid):
                try:
                    visits = brain_data.get("drafter_visits")
                    logits = brain_data.get("drafter_logits")
                    if visits is not None and logits is not None:
                        import numpy as np
                        v_arr = np.asarray(visits, dtype=np.float32)
                        l_arr = logits.detach().cpu().numpy() if hasattr(logits, "detach") else np.asarray(logits, dtype=np.float32)
                        m_wr = rec["wins"] / max(1, rec["matches"])
                        m_cdict: Dict[str, List[int]] = {}
                        for idx_c in np.where(v_arr > 0)[0]:
                            cid = int(idx_c) + 1
                            v_cnt = max(1, int(round(float(v_arr[idx_c]))))
                            card_wr = max(0.05, min(0.95, m_wr + 0.12 * float(np.tanh(l_arr[idx_c]))))
                            w_cnt = int(round(v_cnt * card_wr))
                            p_cnt = max(1, int(round(v_cnt * 0.65)))
                            pw_cnt = int(round(p_cnt * card_wr))
                            key = str(cid)
                            m_cdict[key] = [v_cnt, w_cnt, p_cnt, pw_cnt]
                            cs = self.card_stats.get(key)
                            if cs is None:
                                cs = {"drafted": 0, "wins": 0, "played": 0, "played_wins": 0}
                                self.card_stats[key] = cs
                            if cs["drafted"] < v_cnt:
                                cs["drafted"] += v_cnt
                                cs["wins"] += w_cnt
                                cs["played"] += p_cnt
                                cs["played_wins"] += pw_cnt
                        if m_cdict:
                            self.model_card_stats[mid] = m_cdict
                            changed = True
                except Exception:
                    pass

        # 3. Guarantee strict ZERO overlap across all 10 Free models (free_01 .. free_10)
        claimed_free: Set[int] = set()
        for i in range(1, 11):
            fmid = f"free_{i:02d}"
            frec = self.roster.get(fmid)
            if not frec:
                continue
            fdeck = frec.get("settled_deck") or self.free10_decks.get(fmid) or []
            if not fdeck:
                continue
            fset = set(int(x) for x in fdeck)
            if fset & claimed_free:
                # Rebuild this Free model's settled deck excluding all already-claimed Free cards
                try:
                    from engine.card_model import get_card_db
                    from engine.roster_40 import build_legal_card_mask, get_model_spec
                    from engine.ai_brain import ModelBrain
                    db = get_card_db()
                    sp = get_model_spec(fmid)
                    b_obj = ModelBrain.load_or_create(fmid, db)
                    clean_mask = build_legal_card_mask(sp, db, free_excluded_ids=claimed_free)
                    new_deck = [int(x) for x in b_obj.drafter.build_deck(clean_mask, 0, deterministic=True)]
                    frec["settled_deck"] = new_deck
                    self.free10_decks[fmid] = new_deck
                    b_obj.save_settled_deck_file(new_deck)
                    claimed_free.update(new_deck)
                    changed = True
                except Exception:
                    claimed_free.update(fset)
            else:
                if frec.get("settled_deck") != list(fdeck):
                    frec["settled_deck"] = [int(x) for x in fdeck]
                    changed = True
                if self.free10_decks.get(fmid) != frec["settled_deck"]:
                    self.free10_decks[fmid] = frec["settled_deck"]
                    changed = True
                claimed_free.update(fset)

        if changed:
            self.save_all()

    def save_all(self) -> None:
        _atomic_write_json(self.roster_path, self.roster)
        _atomic_write_json(self.free10_path, self.free10_decks)
        _atomic_write_json(self.card_stats_path, self.card_stats)
        _atomic_write_json(self.model_card_stats_path, self.model_card_stats)
        _atomic_write_json(self.h2h_path, self.head_to_head)
        _atomic_write_json(self.excitement_path, self.excitement)

    def get_anomaly_excluded_ids(self, top_k: int = 60) -> Set[int]:
        """
        Returns the Top-K most overused combat monsters & spells across the other 40 models
        so Glitch_41 (Anomaly Hunter) is forced to discover hidden combos in the remaining 2,210+ cards!
        """
        ranked: List[Tuple[int, int]] = []
        for k, st in self.card_stats.items():
            cid = int(k)
            # Only ban overused Monsters & Spells (never starve Envs/Leaders/Covenants)
            if cid > 170:
                ranked.append((int(st.get("played", 0)) * 3 + int(st.get("drafted", 0)), cid))
        ranked.sort(reverse=True)
        return {cid for _, cid in ranked[:top_k]}

    def get_free10_excluded_ids(self, model_id: str) -> Set[int]:
        """Returns the exact union of Card IDs used in the latest decks of the OTHER 9 Free models."""
        excluded: Set[int] = set()
        for i in range(1, 11):
            mid = f"free_{i:02d}"
            if mid == model_id:
                continue
            deck = self.free10_decks.get(mid) or (self.roster.get(mid, {}).get("settled_deck") or [])
            if deck:
                excluded.update(int(cid) for cid in deck)
        return excluded

    def update_free10_deck(self, model_id: str, deck_ids: List[int]) -> None:
        self.free10_decks[model_id] = [int(cid) for cid in deck_ids]
        if model_id in self.roster:
            self.roster[model_id]["settled_deck"] = [int(cid) for cid in deck_ids]

    def reset_free10_locks(self) -> None:
        self.free10_decks = {}
        for i in range(1, 11):
            mid = f"free_{i:02d}"
            if mid in self.roster:
                self.roster[mid]["settled_deck"] = []
        self.save_all()

    def set_settled_deck(self, model_id: str, deck_ids: List[int], warm_started: bool = False) -> None:
        if model_id in self.roster:
            self.roster[model_id]["settled_deck"] = [int(cid) for cid in deck_ids]
            self.roster[model_id]["warm_started"] = bool(warm_started)
            self.roster[model_id]["last_updated"] = time.strftime("%Y-%m-%d %H:%M:%S")
        if model_id.startswith("free_"):
            self.free10_decks[model_id] = [int(cid) for cid in deck_ids]

    def record_match(
        self,
        model_a_id: str,
        model_b_id: str,
        result: MatchResult,
        style_scores: Tuple[float, float],
        pending_history: List[str],
    ) -> None:
        ra = self.roster[model_a_id]
        rb = self.roster[model_b_id]

        # Standard Elo update (K=20)
        if model_a_id != model_b_id:
            ea = 1.0 / (1.0 + 10.0 ** ((rb["elo"] - ra["elo"]) / 400.0))
            eb = 1.0 - ea
            sa = 1.0 if result.winner == 0 else (0.0 if result.winner == 1 else 0.5)
            sb = 1.0 - sa
            ra["elo"] = round(ra["elo"] + 20.0 * (sa - ea), 2)
            rb["elo"] = round(rb["elo"] + 20.0 * (sb - eb), 2)

        now_str = time.strftime("%Y-%m-%d %H:%M:%S")
        for side, rec in ((0, ra), (1, rb)):
            m_prev = rec["matches"]
            rec["matches"] = m_prev + 1
            if result.winner == side:
                rec["wins"] += 1
            elif result.winner == (1 - side):
                rec["losses"] += 1
            else:
                rec["draws"] += 1
            rec["avg_style_score"] = round(
                (rec["avg_style_score"] * m_prev + style_scores[side]) / (m_prev + 1), 4
            )
            rec["avg_turns"] = round(
                (rec["avg_turns"] * m_prev + result.turns) / (m_prev + 1), 2
            )
            tel = result.telemetry[side]
            inv_n = 1.0 / (m_prev + 1)
            rec["avg_final_hp"] = round((rec.get("avg_final_hp", 0.0) * m_prev + result.final_hp[side]) * inv_n, 1)
            rec["avg_dmg_face"] = round((rec.get("avg_dmg_face", 0.0) * m_prev + tel.dmg_to_face) * inv_n, 1)
            rec["avg_dmg_monsters"] = round((rec.get("avg_dmg_monsters", 0.0) * m_prev + tel.dmg_to_monsters) * inv_n, 1)
            rec["avg_dot_dmg"] = round((rec.get("avg_dot_dmg", 0.0) * m_prev + tel.dot_dmg_dealt) * inv_n, 1)
            rec["avg_healed"] = round((rec.get("avg_healed", 0.0) * m_prev + tel.hp_healed + tel.def_healed) * inv_n, 1)
            rec["avg_reflect"] = round((rec.get("avg_reflect", 0.0) * m_prev + tel.reflect_dmg_dealt) * inv_n, 1)
            rec["avg_kills"] = round((rec.get("avg_kills", 0.0) * m_prev + tel.monsters_killed) * inv_n, 2)
            rec["avg_spells"] = round((rec.get("avg_spells", 0.0) * m_prev + tel.spells_cast) * inv_n, 2)
            rec["avg_actives"] = round((rec.get("avg_actives", 0.0) * m_prev + tel.actives_used) * inv_n, 2)
            rec["avg_tokens"] = round((rec.get("avg_tokens", 0.0) * m_prev + tel.tokens_spawned) * inv_n, 2)
            rec["avg_disruptions"] = round((rec.get("avg_disruptions", 0.0) * m_prev + tel.disruptions_applied) * inv_n, 2)
            rec["avg_excitement"] = round((rec.get("avg_excitement", 0.0) * m_prev + tel.excitement_score) * inv_n, 1)
            ht = max(1, tel.hand_turns)
            rec["avg_hand_start"] = round((rec.get("avg_hand_start", 0.0) * m_prev + tel.hand_sum_start / ht) * inv_n, 2)
            rec["avg_hand_end"] = round((rec.get("avg_hand_end", 0.0) * m_prev + tel.hand_sum_end / ht) * inv_n, 2)
            rec["empty_hand_pct"] = round((rec.get("empty_hand_pct", 0.0) * m_prev + 100.0 * tel.empty_hand_turns / ht) * inv_n, 1)
            rec["avg_board_units"] = round((rec.get("avg_board_units", 0.0) * m_prev + tel.board_units_turn_sum / ht) * inv_n, 2)
            if tel.units_died_count > 0:
                u_life = tel.units_died_rounds_sum / tel.units_died_count
                prev_life = float(rec.get("avg_unit_lifespan", 0.0))
                rec["avg_unit_lifespan"] = round(((prev_life * m_prev + u_life) * inv_n) if prev_life > 0 else u_life, 2)
            if tel.combat_died_count > 0:
                z_pct = 100.0 * tel.combat_died_0atk / tel.combat_died_count
                prev_z = float(rec.get("died_0atk_pct", 0.0))
                rec["died_0atk_pct"] = round(((prev_z * m_prev + z_pct) * inv_n) if prev_z > 0 else z_pct, 1)
            if tel.max_turn_dmg > int(rec.get("max_burst_dmg", 0)):
                rec["max_burst_dmg"] = int(tel.max_turn_dmg)
            if result.winner == side:
                if tel.comeback_deficit >= 100:
                    rec["comeback_wins"] = int(rec.get("comeback_wins", 0)) + 1
                if tel.clutch_win:
                    rec["clutch_wins"] = int(rec.get("clutch_wins", 0)) + 1
            rec["last_updated"] = now_str

            # Per-card telemetry (global + per-model personal card win rates!)
            won = 1 if result.winner == side else 0
            mid_cur = rec["model_id"]
            m_cdict = self.model_card_stats.get(mid_cur)
            if m_cdict is None:
                m_cdict = {}
                self.model_card_stats[mid_cur] = m_cdict
            deck_unique = set(result.decks[side])
            played_unique = set(result.telemetry[side].cards_played_ids)
            for cid in deck_unique:
                key = str(cid)
                cs = self.card_stats.get(key)
                if cs is None:
                    cs = {"drafted": 0, "wins": 0, "played": 0, "played_wins": 0}
                    self.card_stats[key] = cs
                cs["drafted"] += 1
                cs["wins"] += won
                mcs = m_cdict.get(key)
                if mcs is None:
                    mcs = [0, 0, 0, 0]  # [drafted, wins, played, played_wins]
                    m_cdict[key] = mcs
                mcs[0] += 1
                mcs[1] += won
            for cid in played_unique:
                key = str(cid)
                cs = self.card_stats.get(key)
                if cs is None:
                    cs = {"drafted": 0, "wins": 0, "played": 0, "played_wins": 0}
                    self.card_stats[key] = cs
                cs["played"] += 1
                cs["played_wins"] += won
                mcs = m_cdict.get(key)
                if mcs is None:
                    mcs = [0, 0, 0, 0]
                    m_cdict[key] = mcs
                mcs[2] += 1
                mcs[3] += won

        # Head-to-Head permanent pair matrix (never lost when match_history rotates!)
        a_id, b_id = (model_a_id, model_b_id) if model_a_id <= model_b_id else (model_b_id, model_a_id)
        h_key = f"{a_id}|{b_id}"
        h_rec = self.head_to_head.get(h_key)
        if h_rec is None:
            h_rec = {"a": a_id, "b": b_id, "m": 0, "wa": 0, "wb": 0, "d": 0, "turns": 0}
            self.head_to_head[h_key] = h_rec
        h_rec["m"] += 1
        h_rec["turns"] += int(result.turns)
        w_actual = model_a_id if result.winner == 0 else (model_b_id if result.winner == 1 else "DRAW")
        if w_actual == a_id:
            h_rec["wa"] += 1
        elif w_actual == b_id:
            h_rec["wb"] += 1
        else:
            h_rec["d"] += 1

        # Global Excitement, Turn-by-Turn Curve & Anomaly Log update
        ex = self.excitement
        tm = int(ex.get("total_matches", 0))
        t0, t1 = result.telemetry[0], result.telemetry[1]
        match_exc = max(t0.excitement_score, t1.excitement_score)
        ex["total_matches"] = tm + 1
        ex["avg_excitement"] = round((float(ex.get("avg_excitement", 0.0)) * tm + match_exc) / (tm + 1), 2)
        ex["total_lead_swings"] = int(ex.get("total_lead_swings", 0)) + max(t0.lead_swings, t1.lead_swings)

        if result.winner in (0, 1):
            wt = result.telemetry[result.winner]
            if wt.comeback_deficit >= 100:
                ex["comeback_wins"] = int(ex.get("comeback_wins", 0)) + 1
            if wt.clutch_win:
                ex["clutch_wins"] = int(ex.get("clutch_wins", 0)) + 1

        tc = ex["turn_curve"]
        for ev in result.turn_events:
            t_num, tdmg, tkills, tswing, thi = ev[:5]
            t_hs = ev[5] if len(ev) > 5 else 0
            t_he = ev[6] if len(ev) > 6 else 0
            tk_s = str(min(25, max(1, t_num)))
            b = tc.get(tk_s)
            if b is not None:
                b["turns"] += 1
                b["dmg"] += tdmg
                b["kills"] += tkills
                b["swings"] += tswing
                if tdmg >= 100:
                    b["bursts"] += 1
                b["highlights"] += thi
                b["hs"] = int(b.get("hs", 0)) + int(t_hs)
                b["he"] = int(b.get("he", 0)) + int(t_he)
                if t_he == 0 and len(ev) > 6:
                    b["hempty"] = int(b.get("hempty", 0)) + 1
        fin_key = str(min(25, max(1, result.turns)))
        if fin_key in tc:
            tc[fin_key]["finishes"] += 1

        hl_map = ex["highlights"]
        for s_tel in (t0, t1):
            for htag, hcnt in s_tel.highlight_tags.items():
                hl_map[htag] = int(hl_map.get(htag, 0)) + int(hcnt)

        # Detect Extreme Card / Combo Anomalies on Winning Side
        if result.winner in (0, 1):
            w_side = result.winner
            wt = result.telemetry[w_side]
            w_id = model_a_id if w_side == 0 else model_b_id
            l_id = model_b_id if w_side == 0 else model_a_id
            anom_type = ""
            anom_detail = ""
            severity = 0
            if wt.max_turn_dmg >= 150:
                anom_type = "OTK / Turn Burst"
                anom_detail = f"{wt.max_turn_dmg} dmg in Turn {wt.peak_turn_num}"
                severity = wt.max_turn_dmg
            elif wt.max_unit_atk >= 52 or wt.max_unit_def >= 88:
                anom_type = "Stat Freak Monster"
                anom_detail = f"Peak Unit {wt.max_unit_atk} ATK / {wt.max_unit_def} DEF"
                severity = wt.max_unit_atk * 2 + wt.max_unit_def
            elif result.turns <= 7 and result.win_reason == "CommanderHP":
                anom_type = "Speed Glitch Kill"
                anom_detail = f"Won in {result.turns} turns (Peak {wt.max_turn_dmg} dmg/turn)"
                severity = 220 - result.turns * 15
            elif wt.comeback_deficit >= 165:
                anom_type = "Miracle Comeback"
                anom_detail = f"Overcame -{wt.comeback_deficit} HP deficit -> won at {wt.final_hp} HP"
                severity = wt.comeback_deficit

            if anom_type:
                # Extract top 3 most-played cards in this match by the winner
                from collections import Counter
                top_cids = [cid for cid, _ in Counter(wt.cards_played_ids).most_common(3)]
                anom_entry = {
                    "ts": now_str,
                    "winner": w_id,
                    "loser": l_id,
                    "type": anom_type,
                    "turn": result.turns,
                    "detail": anom_detail,
                    "cards": top_cids,
                    "severity": int(severity),
                }
                anoms: List[Dict[str, Any]] = ex.get("anomalies", [])
                anoms.append(anom_entry)
                anoms.sort(key=lambda x: int(x.get("severity", 0)), reverse=True)
                ex["anomalies"] = anoms[:80]

        # Append compact history entry
        hist_row = {
            "ts": now_str,
            "model_a": model_a_id,
            "model_b": model_b_id,
            "winner": model_a_id if result.winner == 0 else (model_b_id if result.winner == 1 else "DRAW"),
            "turns": result.turns,
            "reason": result.win_reason,
            "hp_a": result
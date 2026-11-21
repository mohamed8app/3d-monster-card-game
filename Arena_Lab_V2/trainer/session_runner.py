# -*- coding: utf-8 -*-
"""
Headless CPU-safe match runner & Smart Matchup Advisor for Arena_Lab_V2.
Runs exact N matches between any two selected models from the 40-model roster,
performing PPO & Neural Drafter updates automatically in the background.
"""

from __future__ import annotations

import ctypes
import os
import sys
import threading
import time
from dataclasses import dataclass
from typing import Any, Callable, Dict, List, Optional, Tuple

import numpy as np
import torch

from engine.ai_brain import ModelBrain, StepBuffer
from engine.battle_engine import BattleEngine
from engine.card_model import CardDB, get_card_db
from engine.roster_40 import (
    ROSTER_40,
    build_legal_card_mask,
    compute_match_reward,
    get_model_spec,
)
from .stats_store import StatsStore

CPU_PRESETS: Dict[str, Dict[str, Any]] = {
    "Quiet (35% CPU)": {"frac": 0.25, "match_sleep": 0.012, "batch_sleep": 0.08},
    "Balanced (55% CPU)": {"frac": 0.50, "match_sleep": 0.004, "batch_sleep": 0.03},
    "Fast (75% CPU)": {"frac": 0.75, "match_sleep": 0.0, "batch_sleep": 0.005},
}


def apply_cpu_safety(preset_name: str = "Balanced (55% CPU)") -> Tuple[int, float, float]:
    """
    Configures PyTorch thread count and Windows BELOW_NORMAL process priority
    so long CPU-only training sessions never overheat or starve the OS.
    """
    cfg = CPU_PRESETS.get(preset_name, CPU_PRESETS["Balanced (55% CPU)"])
    total_cores = os.cpu_count() or 4
    threads = max(1, min(total_cores - 1 if total_cores > 2 else 1, int(round(total_cores * cfg["frac"]))))
    torch.set_num_threads(threads)
    try:
        torch.set_num_interop_threads(1)
    except RuntimeError:
        pass

    # Lower process priority gently on Windows (BELOW_NORMAL_PRIORITY_CLASS = 0x00004000)
    if sys.platform.startswith("win"):
        try:
            handle = ctypes.windll.kernel32.GetCurrentProcess()
            ctypes.windll.kernel32.SetPriorityClass(handle, 0x00004000)
        except Exception:
            pass

    return threads, float(cfg["match_sleep"]), float(cfg["batch_sleep"])


@dataclass
class MatchupSuggestion:
    model_a_id: str
    model_b_id: str
    tag: str
    reason: str


def suggest_matchups(store: StatsStore, limit: int = 6) -> List[MatchupSuggestion]:
    """
    Inspects all 40 models and suggests high-value matchups (advice only — user decides!).
    Prioritizes:
      1. Warm-Start Foundation check (if no Free models trained yet)
      2. Untrained / low-experience models (< 50 matches) paired cleanly
      3. Struggling models (< 40% WR) paired against close-ELO peers
      4. Champion stress-tests (top ELO vs runner-up / counter)
    """
    store.reload()
    recs = list(store.roster.values())
    suggestions: List[MatchupSuggestion] = []
    used_pairs: set = set()

    def add_sug(a: str, b: str, tag: str, reason: str) -> None:
        if a == b:
            return
        pair = tuple(sorted((a, b)))
        if pair in used_pairs:
            return
        used_pairs.add(pair)
        suggestions.append(MatchupSuggestion(a, b, tag, reason))

    # 1. Check if Free_01 / Free_02 need initial Warm-Start foundation
    f1 = store.roster["free_01"]
    f2 = store.roster["free_02"]
    if f1["matches"] < 200 and f2["matches"] < 200:
        add_sug(
            "free_01", "free_02", "WARM-START SEED",
            f"Train Free_01 ({f1['matches']}m) vs Free_02 ({f2['matches']}m) for 500 matches, "
            f"then click 'Set as Warm-Start Base' so all 38 other models start smart!",
        )

    # 2. Untrained or very low-experience models (< 50 matches)
    low_exp = [r for r in recs if r["matches"] < 50]
    low_exp.sort(key=lambda r: (r["matches"], r["category"], r["model_id"]))
    for i in range(0, len(low_exp) - 1, 2):
        if len(suggestions) >= limit - 2:
            break
        a, b = low_exp[i], low_exp[i + 1]
        add_sug(
            a["model_id"], b["model_id"], "LOW EXPERIENCE",
            f"Both need early training: {a['display_name']} ({a['matches']}m) vs {b['display_name']} ({b['matches']}m).",
        )

    # 3. Struggling trained models (< 42% WR with >= 30 matches)
    weak = [
        r for r in recs
        if r["matches"] >= 30 and (r["wins"] / max(1, r["matches"])) < 0.42
    ]
    weak.sort(key=lambda r: (r["wins"] / max(1, r["matches"]), r["elo"]))
    trained = [r for r in recs if r["matches"] >= 30]
    for w in weak:
        if len(suggestions) >= limit - 1:
            break
        # Find closest ELO sparring partner slightly above them
        peers = [p for p in trained if p["model_id"] != w["model_id"]]
        if not peers:
            continue
        peers.sort(key=lambda p: abs(p["elo"] - (w["elo"] + 25.0)))
        partner = peers[0]
        wr = 100.0 * w["wins"] / max(1, w["matches"])
        add_sug(
            w["model_id"], partner["model_id"], "RECOVERY SPARRING",
            f"{w['display_name']} has low win rate ({wr:.1f}%, ELO {w['elo']:.0f}); "
            f"spar against {partner['display_name']} (ELO {partner['elo']:.0f}).",
        )

    # 4. Top-ELO Champion showdown
    if len(trained) >= 2:
        by_elo = sorted(trained, key=lambda r: r["elo"], reverse=True)
        c1, c2 = by_elo[0], by_elo[1]
        add_sug(
            c1["model_id"], c2["model_id"], "CHAMPION CLASH",
            f"Top ELO test: {c1['display_name']} ({c1['elo']:.0f}) vs {c2['display_name']} ({c2['elo']:.0f}).",
        )

    # 5. Cross-category balance check fallback if still under limit
    if len(suggestions) < limit:
        by_m = sorted(recs, key=lambda r: r["matches"])
        for i in range(0, len(by_m) - 1, 2):
            if len(suggestions) >= limit:
                break
            a, b = by_m[i], by_m[i + 1]
            add_sug(
                a["model_id"], b["model_id"], "EXPERIENCE BOOST",
                f"Least trained pair: {a['display_name']} ({a['matches']}m) vs {b['display_name']} ({b['matches']}m).",
            )

    return suggestions[:limit]


def pick_next_auto_league_pair(
    store: StatsStore,
    scope: str = "All",
    last_pair: Optional[Tuple[str, str]] = None,
) -> Tuple[str, str]:
    """
    Selects the next optimal training pair for Perpetual Auto-League mode:
      1. Picks the least-experienced model (fewest matches) in the chosen pool as Model A.
      2. Picks Model B from the lowest-experience cohort (preferring close ELO + avoiding repeating `last_pair`)
         so all 40 models climb evenly together against diverse opponents up to 1,000,000+ matches!
    """
    store.reload()
    pool = [
        r for r in store.roster.values()
        if scope == "All" or r["category"] == scope
    ]
    if len(pool) < 2:
        pool = list(store.roster.values())

    # Sort primarily by fewest matches, with small tie-break noise so order rotates fairly
    rng = np.random.default_rng()
    pool.sort(key=lambda r: (r["matches"], float(rng.random())))
    rec_a = pool[0]

    # Candidate opponents: bottom 6 least-experienced peers (excluding A)
    others = [r for r in pool[1:]]
    candidates = others[: min(6, len(others))]

    # Prefer not repeating the exact same pair back-to-back, and favor close ELO for strongest RL gradient
    prev_set = set(last_pair) if last_pair else set()
    fresh_cands = [r for r in candidates if {rec_a["model_id"], r["model_id"]} != prev_set]
    pick_from = fresh_cands if fresh_cands else candidates
    pick_from.sort(key=lambda r: (r["matches"] - rec_a["matches"]) * 0.5 + abs(r["elo"] - rec_a["elo"]) * 0.05 + float(rng.random()) * 15.0)
    rec_b = pick_from[0]
    return rec_a["model_id"], rec_b["model_id"]


def run_match_session(
    model_a_id: str,
    model_b_id: str,
    num_matches: int,
    cpu_preset: str = "Balanced (55% CPU)",
    use_warm_start: bool = True,
    batch_size: int = 16,
    stop_event: Optional[threading.Event] = None,
    progress_cb: Optional[Callable[[int, int, Dict[str, Any]], None]] = None,
    db: Optional[CardDB] = None,
    store: Optional[StatsStore] = None,
) -> Dict[str, Any]:
    """
    Runs `num_matches` between Model A and Model B with automatic background PPO + Drafter updates.
    Zero live charts or heavy I/O during play; saves checkpoints and stats atomically per batch.
    """
    t0 = time.perf_counter()
    db = db if db is not None else get_card_db()
    store = store if store is not None else StatsStore()
    store.reload()

    threads, match_sleep, batch_sleep = apply_cpu_safety(cpu_preset)
    spec_a = get_model_spec(model_a_id)
    spec_b = get_model_spec(model_b_id)
    mirror = (model_a_id == model_b_id)

    brain_a = ModelBrain.load_or_create(model_a_id, db, use_warm_start=use_warm_start)
    brain_b = brain_a if mirror else ModelBrain.load_or_create(model_b_id, db, use_warm_start=use_warm_start)

    engine = BattleEngine(db=db)
    np_rng = np.random.default_rng()

    # Batch buffers (automatically updated every `batch_size` matches in the background)
    bufs_a: List[StepBuffer] = []
    bufs_b: List[StepBuffer] = []
    decks_a_batch: List[List[int]] = []
    decks_b_batch: List[List[int]] = []
    played_a_batch: List[List[int]] = []
    played_b_batch: List[List[int]] = []
    rews_a_batch: List[float] = []
    rews_b_batch: List[float] = []
    pending_history: List[str] = []

    wins_a = 0
    wins_b = 0
    draws = 0
    style_sum_a = 0.0
    style_sum_b = 0.0
    turns_sum = 0
    completed = 0

    def _calc_temp(matches_done: int) -> float:
        # Smooth exploration annealing across 0..5,000+ matches (1.15 -> 0.50)
        return float(max(0.50, 1.15 - min(0.65, matches_done / 4500.0)))

    temp_a = _calc_temp(brain_a.matches_trained)
    temp_b = _calc_temp(brain_b.matches_trained)
    last_vloss_a = 0.0
    last_vloss_b = 0.0
    last_focus_a = 0.0
    last_focus_b = 0.0

    def _get_excl(mid: str, cat: str) -> Optional[set]:
        if cat == "Free":
            return store.get_free10_excluded_ids(mid)
        return None

    def _refresh_masks() -> Tuple[np.ndarray, np.ndarray]:
        return (
            build_legal_card_mask(spec_a, db, free_excluded_ids=_get_excl(model_a_id, spec_a.category)),
            build_legal_card_mask(spec_b, db, free_excluded_ids=_get_excl(model_b_id, spec_b.category)),
        )

    mask_a, mask_b = _refresh_masks()
    last_focus_a = brain_a.drafter.focus_score(mask_a)
    last_focus_b = brain_b.drafter.focus_score(mask_b)

    def _flush_batch() -> None:
        nonlocal mask_a, mask_b, temp_a, temp_b, last_vloss_a, last_vloss_b, last_focus_a, last_focus_b
        if not decks_a_batch:
            return
        # Update Model A Fighter & Drafter (with Played-Card Credit Weighting)
        if mirror:
            st_a = brain_a.ppo_update(bufs_a + bufs_b)
            brain_a.drafter.update(
                decks_a_batch + decks_b_batch,
                rews_a_batch + rews_b_batch,
                mask_a,
                batch_played_ids=played_a_batch + played_b_batch,
            )
            brain_a.matches_trained += len(decks_a_batch)
            last_vloss_a = last_vloss_b = st_a["value_loss"]
            last_focus_a = last_focus_b = brain_a.drafter.focus_score(mask_a)
            settled_a = brain_a.drafter.build_deck(mask_a, spec_a.required_card_id, deterministic=True)
            store.set_settled_deck(model_a_id, settled_a, warm_started=brain_a.warm_started)
            brain_a.save()
            brain_a.save_settled_deck_file(settled_a)
        else:
            st_a = brain_a.ppo_update(bufs_a)
            brain_a.drafter.update(decks_a_batch, rews_a_batch, mask_a, batch_played_ids=played_a_batch)
            brain_a.matches_trained += len(decks_a_batch)
            last_vloss_a = st_a["value_loss"]
            last_focus_a = brain_a.drafter.focus_score(mask_a)
            # Export A's settled deck first so if both are Free-10, B excludes A's settled cards!
            settled_a = brain_a.drafter.build_deck(mask_a, spec_a.required_card_id, deterministic=True)
            store.set_settled_deck(model_a_id, settled_a, warm_started=brain_a.warm_started)
            brain_a.save()
            brain_a.save_settled_deck_file(settled_a)

            if spec_b.category == "Free":
                mask_b = build_legal_card_mask(
                    spec_b, db, free_excluded_ids=store.get_free10_excluded_ids(model_b_id)
                )
            st_b = brain_b.ppo_update(bufs_b)
            brain_b.drafter.update(decks_b_batch, rews_b_batch, mask_b, batch_played_ids=played_b_batch)
            brain_b.matches_trained += len(decks_b_batch)
            last_vloss_b = st_b["value_loss"]
            last_focus_b = brain_b.drafter.focus_score(mask_b)
            settled_b = brain_b.drafter.build_deck(mask_b, spec_b.required_card_id, deterministic=True)
            store.set_settled_deck(model_b_id, settled_b, warm_started=brain_b.warm_started)
            brain_b.save()
            brain_b.save_settled_deck_file(settled_b)

        store.save_all()
        store.flush_history(pending_history)

        bufs_a.clear()
        bufs_b.clear()
        decks_a_batch.clear()
        decks_b_batch.clear()
        played_a_batch.clear()
        played_b_batch.clear()
        rews_a_batch.clear()
        rews_b_batch.clear()
        mask_a, mask_b = _refresh_masks()
        temp_a = _calc_temp(brain_a.matches_trained)
        temp_b = _calc_temp(brain_b.matches_trained)

        if batch_sleep > 0.0:
            time.sleep(batch_sleep)

    for m_idx in range(1, num_matches + 1):
        if stop_event is not None and stop_event.is_set():
            break

        # Draft 50-card decks (guaranteeing zero overlap if both A and B are Free-10!)
        deck_a = brain_a.drafter.build_deck(
            mask_a, spec_a.required_card_id, deterministic=False, temperature=temp_a, rng=np_rng
        )
        if spec_a.category == "Free" and spec_b.category == "Free" and not mirror:
            # Also exclude deck_a's live cards during this match so A and B never share cards even on Match #1!
            live_ex_b = store.get_free10_excluded_ids(model_b_id) | set(deck_a)
            live_mask_b = build_legal_card_mask(spec_b, db, free_excluded_ids=live_ex_b)
        else:
            live_mask_b = mask_b

        deck_b = brain_b.drafter.build_deck(
            live_mask_b, spec_b.required_card_id, deterministic=False, temperature=temp_b, rng=np_rng
        )

        # Play 1 full battle with Dense Tactical Step Shaping (turn-by-turn error credit!)
        engine.reset(deck_a, deck_b)
        sb0 = StepBuffer()
        sb1 = StepBuffer()
        step_guard = 0

        while not engine.done and step_guard < 1600:
            side = engine.active
            opp_side = 1 - side
            obs = engine.get_obs(side)
            lmask = engine.get_legal_mask(side)
            brain = brain_a if side == 0 else brain_b
            act, lp, val = brain.select_action(obs, lmask, deterministic=False)

            pre_my_hp = engine.players[side].hp
            pre_op_hp = engine.players[opp_side].hp
            pre_kills = engine.players[side].telemetry.monsters_killed
            pre_my_b = engine.players[side].board_count()

            engine.step(act)

            # Small dense tactical shaping (always bounded well below +/-1.0 Win/Loss!)
            d_op_hp = pre_op_hp - engine.players[opp_side].hp
            d_my_hp = engine.players[side].hp - pre_my_hp
            d_kills = engine.players[side].telemetry.monsters_killed - pre_kills
            d_board = engine.players[side].board_count() - pre_my_b
            step_r = (0.0018 * d_op_hp) + (0.0012 * d_my_hp) + (0.012 * d_kills) + (0.004 * max(0, d_board))
            if step_r > 0.06:
                step_r = 0.06
            elif step_r < -0.06:
                step_r = -0.06

            (sb0 if side == 0 else sb1).add(obs, lmask, act, lp, val, step_reward=step_r)
            step_guard += 1

        if not engine.done:
            # Failsafe guard if ever reached
            w = 0 if engine.players[0].hp > engine.players[1].hp else (1 if engine.players[1].hp > engine.players[0].hp else -1)
            engine._finish(w, "StepGuard")

        res = engine.get_result()
        rew_a, st_a = compute_match_reward(spec_a, res, 0)
        rew_b, st_b = compute_match_reward(spec_b, res, 1)
        sb0.finalize_match(rew_a)
        sb1.finalize_match(rew_b)

        bufs_a.append(sb0)
        bufs_b.append(sb1)
        decks_a_batch.append(deck_a)
        decks_b_batch.append(deck_b)
        played_a_batch.append(list(res.telemetry[0].cards_played_ids))
        played_b_batch.append(list(res.telemetry[1].cards_played_ids))
        rews_a_batch.append(rew_a)
        rews_b_batch.append(rew_b)

        store.record_match(model_a_id, model_b_id, res, (st_a, st_b), pending_history)

        completed += 1
        turns_sum += res.turns
        style_sum_a += st_a
        style_sum_b += st_b
        if res.winner == 0:
            wins_a += 1
        elif res.winner == 1:
            wins_b += 1
        else:
            draws += 1

        if len(decks_a_batch) >= batch_size:
            _flush_batch()

        if progress_cb is not None and (completed % 2 == 0 or completed == num_matches):
            elapsed = max(0.01, time.perf_counter() - t0)
            progress_cb(
                completed,
                num_matches,
                {
                    "wins_a": wins_a,
                    "wins_b": wins_b,
                    "draws": draws,
                    "avg_turns": turns_sum / completed,
                    "avg_style_a": style_sum_a / completed,
                    "avg_style_b": style_sum_b / completed,
                    "matches_per_sec": completed / elapsed,
                    "elapsed_sec": elapsed,
                    "threads": threads,
                    "v_loss_a": last_vloss_a,
                    "v_loss_b": last_vloss_b,
                    "focus_a": last_focus_a,
                    "focus_b": last_focus_b,
                    "temp_a": temp_a,
                    "temp_b": temp_b,
                },
            )

        if match_sleep > 0.0:
            time.sleep(match_sleep)

    # Always flush any remaining partial batch cleanly!
    _flush_batch()

    elapsed = max(0.01, time.perf_counter() - t0)
    return {
        "model_a_id": model_a_id,
        "model_b_id": model_b_id,
        "completed": completed,
        "requested": num_matches,
        "stopped_early": bool(stop_event is not None and stop_event.is_set() and completed < num_matches),
        "wins_a": wins_a,
        "wins_b": wins_b,
        "draws": draws,
        "win_rate_a": (100.0 * wins_a / max(1, completed)),
        "win_rate_b": (100.0 * wins_b / max(1, completed)),
        "avg_turns": turns_sum / max(1, completed),
        "avg_style_a": style_sum_a / max(1, completed),
        "avg_style_b": style_sum_b / max(1, completed),
        "elapsed_sec": elapsed,
        "matches_per_sec": completed / elapsed,
        "threads": threads,
        "warm_started_a": brain_a.warm_started,
        "warm_started_b": brain_b.warm_started,
    }

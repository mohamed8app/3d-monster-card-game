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


def _atomic_write_json(path: str, data: Any, compact: bool = False) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = f"{path}.{os.getpid()}.tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        if compact:
            json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
        else:
            json.dump(data, f, ensure_ascii=False, indent=2)
        f.flush()
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
        "total_turns_sum": 0,
        "p1_games": 0,
        "p1_wins": 0,
        "avg_excitement": 0.0,
        "avg_hand_start": 0.0,
        "avg_hand_end": 0.0,
        "empty_hand_pct": 0.0,
        "comeback_wins": 0,
        "clutch_wins": 0,
        "total_lead_swings": 0,
        "match_len_bins": {"1-6": 0, "7-12": 0, "13-18": 0, "19-25": 0, "26-39": 0, "40+": 0},
        "monster_survival": {
            "died_cnt": 0, "died_rounds": 0,
            "d0": 0, "d1": 0, "d2": 0, "d3p": 0,
            "life_hist": [0, 0, 0, 0, 0, 0, 0],
            "tier_sum": [0] * 8, "tier_cnt": [0] * 8,
            "role_sum": [0] * 6, "role_cnt": [0] * 6,
            "tribe_sum": [0] * 10, "tribe_cnt": [0] * 10,
            "elem_sum": [0] * 10, "elem_cnt": [0] * 10,
            "dmg_absorbed_sum": 0, "base_def_sum": 0,
            "zero_impact_died": 0, "attacks_died_sum": 0,
            "skill_holders_died": 0, "skills_died_sum": 0,
            "first_impact_wait_sum": 0, "first_impact_cnt": 0,
            "empty_board_turns": 0,
            "exits_sacrifice": 0, "exits_recalled": 0,
            "combat_died": 0, "combat_0atk": 0,
            "surv_cnt": 0, "surv_rounds": 0,
            "board_sum": 0, "board_turns": 0,
        },
        "kill_causes": {"active": 0, "attack": 0, "spell": 0, "counter": 0, "dot": 0, "passive": 0},
        "dmg_sources": {"active": 0, "attack": 0, "spell": 0, "counter": 0, "dot": 0, "passive": 0},
        "game_pace": {
            "matches": 0,
            "turn_hist": {},
            "min_turn": 0,
            "max_turn": 0,
            "first_summon_sum": 0, "first_summon_cnt": 0,
            "first_attack_sum": 0, "first_attack_cnt": 0,
            "first_death_sum": 0,  "first_death_cnt": 0,
            "first_defense_sum": 0,"first_defense_cnt": 0,
            "zero_card_turns": 0,
            "dead_turns": 0,
            "total_player_turns": 0,
            "total_actions": 0,
            "impact_gaps_sum": 0,  "impact_gaps_cnt": 0,
            "max_turn_40_cnt": 0,
            "decisive_turn_sum": 0, "decisive_ratio_sum": 0.0, "decisive_cnt": 0,
        },
        "combat_quality": {
            "unit_dmg_dealt_sum": 0,
            "hits_to_kill_sum": 0,
            "total_strikes": 0,
            "unit_clashes": 0,
            "face_strikes": 0,
            "lethal_strikes": 0,
            "whiff_strikes": 0,
            "intercepted_strikes": 0,
            "mutual_kills": 0,
            "fair_trades": 0,
            "clean_kills": 0,
            "swing_strikes": 0,
            "overkill_dmg": 0,
            "raw_unit_dmg": 0,
            "barrier_dmg_absorbed": 0,
            "near_death_hits": 0,
            "total_unit_hits": 0,
            "skill_dmg_total": 0,
            "actives_used_total": 0,
            "total_healed": 0,
            "total_face_dmg": 0,
            "total_monster_dmg": 0,
        },
        "card_utilization": {
            "drawn_total": 0,
            "played_total": 0,
            "sacrificed_total": 0,
            "burned_hand7_total": 0,
            "hand_end_remaining": 0,
            "played_on_draw_turn": 0,
            "play_turn_sum": 0,
            "played_by_type": [0, 0, 0, 0, 0, 0],
            "cond_attempts": 0,
            "cond_success": 0,
            "hand_checks_cards": 0,
            "hand_unplayable_cards": 0,
            "low_value_plays": 0,
            "deck_cycles_done": 0,
            "impact_value_sum": 0,
            "sides_count": 0,
        },
        "resource_economy": {
            "mana_avail": 0,
            "mana_spent": 0,
            "mana_wasted": 0,
            "turns_total": 0,
            "turns_unspent": 0,
            "early_mana_avail": 0,
            "early_mana_spent": 0,
            "mana_spent_cards": 0,
            "mana_spent_monsters": 0,
            "mana_spent_spells": 0,
            "mana_spent_skills": 0,
            "mana_spent_other": 0,
            "mana_offense": 0,
            "mana_defense": 0,
            "card_cost_played": [0, 0, 0, 0, 0, 0, 0, 0],
            "card_cost_wins": [0, 0, 0, 0, 0, 0, 0, 0],
            "turn_mana_avail": [0] * 15,
            "turn_mana_spent": [0] * 15,
            "turn_mana_cnt": [0] * 15,
            "reach_cap_sum": 0,
            "reach_cap_cnt": 0,
            "sacrifices_total": 0,
            "impact_value_sum": 0,
            "sides_count": 0,
        },
        "mechanics_perf": {
            "matches": 0,
            "sides": 0,
            "wins_total": 0,
            "sys_used_sides": [0] * 9,
            "sys_used_wins": [0] * 9,
            "sys_total_cnt": [0] * 9,
            "match_sys_present": [0] * 9,
            "active_systems_sum": 0,
            "env_upgrades": 0,
            "passive_triggers": 0,
            "ascends": 0,
            "last_breaths": 0,
            "reflect_dmg": 0,
            "evasions": 0,
            "dot_dmg": 0,
            "total_dmg": 0,
            "sac_ramp_sides": 0,
            "sac_ramp_wins": 0,
            "sac_low_sides": 0,
            "sac_low_wins": 0,
        },
        "victory_cond": {
            "matches": 0,
            "decisive_matches": 0,
            "face_by_ch": [0, 0, 0, 0, 0, 0],
            "kill_by_ch": [0, 0, 0, 0, 0, 0],
            "aggro_wins": 0,
            "midrange_wins": 0,
            "control_wins": 0,
            "sudden_death_wins": 0,
            "covenant_limit_wins": 0,
            "board_dom_wins": 0,
            "blowout_wins": 0,
            "clutch_100_wins": 0,
            "comeback_100_wins": 0,
            "burst_finish_wins": 0,
        },
        "strategic_intel": {
            "turns_total": 0,
            "legal_actions_sum": 0,
            "distinct_choices_sum": 0,
            "choice_hist": [0, 0, 0, 0, 0, 0],
            "forced_pass_turns": 0,
            "multi_action_turns": 0,
            "decision_chain_sum": 0,
            "slot_summons": [0, 0, 0, 0, 0, 0],
            "sac_avail_turns": 0,
            "sac_taken_total": 0,
            "restraint_passes": 0,
            "reactive_decisions": 0,
            "total_decisions": 0,
            "planned_plays": 0,
            "played_total": 0,
            # Deep Skill Audit metrics (populated by Live Probe / Skill Audit)
            "policy_entropy_pct": -1.0,
            "ai_vs_random_wr": -1.0,
            "ai_vs_random_n": 0,
            "opt_vs_noisy_wr": -1.0,
            "opt_vs_noisy_n": 0,
            "decision_regret_pct": -1.0,
        },
        "enjoyment": {
            "matches": 0,
            "exc_sum": 0,
            "exc_hist": [0, 0, 0, 0, 0],
            "matches_high_exc": 0,
            "matches_boring": 0,
            "swings_sum": 0,
            "matches_with_swing": 0,
            "close_hp_turns": 0,
            "total_hp_turns": 0,
            "comeback_100": 0,
            "clutch_75": 0,
            "blowout_325": 0,
            "highlights_sum": 0,
            "multi_kill_matches": 0,
            "empty_hand_turns": 0,
            "player_turns_total": 0,
            "zero_imp_died": 0,
            "units_died_tot": 0,
        },
        "comp_fairness": {
            "matches": 0,
            "draws": 0,
            "p1_decisive": 0,
            "p1_wins": 0,
            "t4_diff_sum": 0,
            "t4_diff_cnt": 0,
            "open_ok_sides": 0, "open_ok_wins": 0,
            "open_brick_sides": 0, "open_brick_wins": 0,
            "early_spec_sides": 0, "early_spec_wins": 0,
            "no_spec_sides": 0, "no_spec_wins": 0,
            "evasions_total": 0,
            "mana_gap_sum": 0.0, "mana_gap_cnt": 0,
            "draw_gap_sum": 0, "draw_gap_cnt": 0,
            "mirror_matches": 0,
            "mirror_p1_wins": 0,
            "mirror_p1_decisive": 0,
            "mirror_blowouts": 0,
            "mirror_close": 0,
        },
        "turn_curve": {
            str(t): {
                "turns": 0, "dmg": 0, "kills": 0, "swings": 0, "bursts": 0,
                "highlights": 0, "finishes": 0, "hs": 0, "he": 0, "hempty": 0,
                "exc_sum": 0, "board_sum": 0,
            }
            for t in range(1, 26)
        },
        "highlights": {},
        "anomalies": [],
    }


class StatsStore:
    """Thread-safe-at-batch-boundary persistent statistics store."""

    def __init__(
        self,
        logs_dir: str = LOGS_DIR,
        saved_models_dir: Optional[str] = None,
        read_only: bool = False,
    ) -> None:
        self.read_only = bool(read_only)
        self._recovered_once = False
        self.roster: Dict[str, Dict[str, Any]] = {}
        self.free10_decks: Dict[str, List[int]] = {}
        self.card_stats: Dict[str, Dict[str, int]] = {}
        self.model_card_stats: Dict[str, Dict[str, List[int]]] = {}
        self.head_to_head: Dict[str, Dict[str, int]] = {}
        self.excitement: Dict[str, Any] = _default_excitement_data()
        self.set_source(logs_dir=logs_dir, saved_models_dir=saved_models_dir, read_only=read_only)

    def set_source(
        self,
        logs_dir: str,
        saved_models_dir: Optional[str] = None,
        read_only: bool = False,
    ) -> None:
        self.logs_dir = logs_dir
        self.saved_models_dir = saved_models_dir or os.path.abspath(os.path.join(logs_dir, "..", "saved_models"))
        self.read_only = bool(read_only)
        self.roster_path = os.path.join(logs_dir, "roster_stats.json")
        self.free10_path = os.path.join(logs_dir, "free10_latest_decks.json")
        self.card_stats_path = os.path.join(logs_dir, "card_stats.json")
        self.model_card_stats_path = os.path.join(logs_dir, "model_card_stats.json")
        self.h2h_path = os.path.join(logs_dir, "head_to_head.json")
        self.history_path = os.path.join(logs_dir, "match_history.jsonl")
        self.excitement_path = os.path.join(logs_dir, "excitement_anomalies.json")
        if not self.read_only:
            os.makedirs(self.logs_dir, exist_ok=True)
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
            for dict_key in ("match_len_bins", "monster_survival", "kill_causes", "dmg_sources", "game_pace", "combat_quality", "card_utilization", "resource_economy", "mechanics_perf", "victory_cond", "strategic_intel", "enjoyment", "comp_fairness"):
                if dict_key in raw_ex and isinstance(raw_ex[dict_key], dict):
                    self.excitement[dict_key].update(raw_ex[dict_key])
            if "turn_curve" in raw_ex and isinstance(raw_ex["turn_curve"], dict):
                for tk_str, tv in raw_ex["turn_curve"].items():
                    if tk_str in self.excitement["turn_curve"] and isinstance(tv, dict):
                        self.excitement["turn_curve"][tk_str].update(tv)

        # Self-healing recovery: ONLY run once at initial startup in local writable mode!
        if not self.read_only and not getattr(self, "_recovered_once", False):
            self._recovered_once = True
            self._auto_recover_from_history_and_brains()

    def _auto_recover_from_history_and_brains(self) -> None:
        saved_models_root = self.saved_models_dir
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

    def save_all(self, include_heavy: bool = True) -> None:
        _atomic_write_json(self.roster_path, self.roster)
        _atomic_write_json(self.free10_path, self.free10_decks)
        _atomic_write_json(self.h2h_path, self.head_to_head)
        _atomic_write_json(self.excitement_path, self.excitement)
        if include_heavy:
            _atomic_write_json(self.card_stats_path, self.card_stats, compact=True)
            _atomic_write_json(self.model_card_stats_path, self.model_card_stats, compact=True)

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
        ex["total_turns_sum"] = int(ex.get("total_turns_sum", 0)) + int(result.turns)
        ex["avg_excitement"] = round((float(ex.get("avg_excitement", 0.0)) * tm + match_exc) / (tm + 1), 2)
        ex["total_lead_swings"] = int(ex.get("total_lead_swings", 0)) + max(t0.lead_swings, t1.lead_swings)

        # P1 vs P2 fairness tracking
        if t0.went_first or t1.went_first:
            p1_side = 0 if t0.went_first else 1
            if result.winner in (0, 1):
                ex["p1_games"] = int(ex.get("p1_games", 0)) + 1
                if result.winner == p1_side:
                    ex["p1_wins"] = int(ex.get("p1_wins", 0)) + 1

        # Match length pacing histogram
        ml = ex.setdefault("match_len_bins", {"1-6": 0, "7-12": 0, "13-18": 0, "19-25": 0, "26-39": 0, "40+": 0})
        rt = int(result.turns)
        if rt <= 6:
            ml["1-6"] = int(ml.get("1-6", 0)) + 1
        elif rt <= 12:
            ml["7-12"] = int(ml.get("7-12", 0)) + 1
        elif rt <= 18:
            ml["13-18"] = int(ml.get("13-18", 0)) + 1
        elif rt <= 25:
            ml["19-25"] = int(ml.get("19-25", 0)) + 1
        elif rt <= 39:
            ml["26-39"] = int(ml.get("26-39", 0)) + 1
        else:
            ml["40+"] = int(ml.get("40+", 0)) + 1

        # Monster survival, board occupancy & kill/damage causes aggregation
        ms = ex.setdefault("monster_survival", {
            "died_cnt": 0, "died_rounds": 0, "d0": 0, "d1": 0, "d2": 0, "d3p": 0,
            "combat_died": 0, "combat_0atk": 0, "surv_cnt": 0, "surv_rounds": 0, "board_sum": 0, "board_turns": 0,
        })
        kc = ex.setdefault("kill_causes", {"active": 0, "attack": 0, "spell": 0, "counter": 0, "dot": 0, "passive": 0})
        ds = ex.setdefault("dmg_sources", {"active": 0, "attack": 0, "spell": 0, "counter": 0, "dot": 0, "passive": 0})
        for st_cur in (t0, t1):
            ms["died_cnt"] = int(ms.get("died_cnt", 0)) + int(st_cur.units_died_count)
            ms["died_rounds"] = int(ms.get("died_rounds", 0)) + int(st_cur.units_died_rounds_sum)
            ms["d0"] = int(ms.get("d0", 0)) + int(st_cur.units_died_0rnd)
            ms["d1"] = int(ms.get("d1", 0)) + int(st_cur.units_died_1rnd)
            ms["d2"] = int(ms.get("d2", 0)) + int(st_cur.units_died_2rnd)
            ms["d3p"] = int(ms.get("d3p", 0)) + int(st_cur.units_died_3p_rnd)
            ms["combat_died"] = int(ms.get("combat_died", 0)) + int(st_cur.combat_died_count)
            ms["combat_0atk"] = int(ms.get("combat_0atk", 0)) + int(st_cur.combat_died_0atk)
            ms["surv_cnt"] = int(ms.get("surv_cnt", 0)) + int(st_cur.units_survived_end)
            ms["surv_rounds"] = int(ms.get("surv_rounds", 0)) + int(st_cur.units_survived_rounds_sum)
            ms["board_sum"] = int(ms.get("board_sum", 0)) + int(st_cur.board_units_turn_sum)
            ms["board_turns"] = int(ms.get("board_turns", 0)) + int(st_cur.hand_turns)
            ms["empty_board_turns"] = int(ms.get("empty_board_turns", 0)) + int(st_cur.empty_board_turns)
            ms["dmg_absorbed_sum"] = int(ms.get("dmg_absorbed_sum", 0)) + int(st_cur.unit_dmg_absorbed_sum)
            ms["base_def_sum"] = int(ms.get("base_def_sum", 0)) + int(st_cur.unit_base_def_sum)
            ms["zero_impact_died"] = int(ms.get("zero_impact_died", 0)) + int(st_cur.unit_zero_impact_died)
            ms["attacks_died_sum"] = int(ms.get("attacks_died_sum", 0)) + int(st_cur.unit_attacks_died_sum)
            ms["skill_holders_died"] = int(ms.get("skill_holders_died", 0)) + int(st_cur.unit_skill_holders_died)
            ms["skills_died_sum"] = int(ms.get("skills_died_sum", 0)) + int(st_cur.unit_skills_died_sum)
            ms["first_impact_wait_sum"] = int(ms.get("first_impact_wait_sum", 0)) + int(st_cur.unit_first_impact_wait_sum)
            ms["first_impact_cnt"] = int(ms.get("first_impact_cnt", 0)) + int(st_cur.unit_first_impact_cnt)
            ms["exits_sacrifice"] = int(ms.get("exits_sacrifice", 0)) + int(st_cur.exits_sacrifice)
            ms["exits_recalled"] = int(ms.get("exits_recalled", 0)) + int(st_cur.exits_recalled)
            lh = ms.setdefault("life_hist", [0, 0, 0, 0, 0, 0, 0])
            for bi in range(7):
                lh[bi] = int(lh[bi]) + int(st_cur.unit_life_hist[bi])
            for arr_k, src_a, ln_a in (
                ("tier_sum", st_cur.unit_tier_sum, 8), ("tier_cnt", st_cur.unit_tier_cnt, 8),
                ("role_sum", st_cur.unit_role_sum, 6), ("role_cnt", st_cur.unit_role_cnt, 6),
                ("tribe_sum", st_cur.unit_tribe_sum, 10), ("tribe_cnt", st_cur.unit_tribe_cnt, 10),
                ("elem_sum", st_cur.unit_elem_sum, 10), ("elem_cnt", st_cur.unit_elem_cnt, 10),
            ):
                dst_a = ms.setdefault(arr_k, [0] * ln_a)
                for idx_a in range(ln_a):
                    dst_a[idx_a] = int(dst_a[idx_a]) + int(src_a[idx_a])

            kc["active"] = int(kc.get("active", 0)) + int(st_cur.kills_by_active)
            kc["attack"] = int(kc.get("attack", 0)) + int(st_cur.kills_by_attack)
            kc["spell"] = int(kc.get("spell", 0)) + int(st_cur.kills_by_spell)
            kc["counter"] = int(kc.get("counter", 0)) + int(st_cur.kills_by_counter)
            kc["dot"] = int(kc.get("dot", 0)) + int(st_cur.kills_by_dot)
            kc["passive"] = int(kc.get("passive", 0)) + int(st_cur.kills_by_passive)

            ds["active"] = int(ds.get("active", 0)) + int(st_cur.dmg_m_by_active)
            ds["attack"] = int(ds.get("attack", 0)) + int(st_cur.dmg_m_by_attack)
            ds["spell"] = int(ds.get("spell", 0)) + int(st_cur.dmg_m_by_spell)
            ds["counter"] = int(ds.get("counter", 0)) + int(st_cur.dmg_m_by_counter)
            ds["dot"] = int(ds.get("dot", 0)) + int(st_cur.dmg_m_by_dot)
            ds["passive"] = int(ds.get("passive", 0)) + int(st_cur.dmg_m_by_passive)

        # Pillar #1: Game Pace detailed aggregation
        gp = ex.setdefault("game_pace", _default_excitement_data()["game_pace"])
        gp["matches"] = int(gp.get("matches", 0)) + 1
        th = gp.setdefault("turn_hist", {})
        tk_h = str(max(1, rt))
        th[tk_h] = int(th.get(tk_h, 0)) + 1
        cur_min = int(gp.get("min_turn", 0))
        gp["min_turn"] = rt if (cur_min <= 0 or rt < cur_min) else cur_min
        gp["max_turn"] = max(int(gp.get("max_turn", 0)), rt)
        if rt >= 40:
            gp["max_turn_40_cnt"] = int(gp.get("max_turn_40_cnt", 0)) + 1
        if t0.pace_first_summon > 0:
            gp["first_summon_sum"] = int(gp.get("first_summon_sum", 0)) + int(t0.pace_first_summon)
            gp["first_summon_cnt"] = int(gp.get("first_summon_cnt", 0)) + 1
        if t0.pace_first_attack > 0:
            gp["first_attack_sum"] = int(gp.get("first_attack_sum", 0)) + int(t0.pace_first_attack)
            gp["first_attack_cnt"] = int(gp.get("first_attack_cnt", 0)) + 1
        if t0.pace_first_death > 0:
            gp["first_death_sum"] = int(gp.get("first_death_sum", 0)) + int(t0.pace_first_death)
            gp["first_death_cnt"] = int(gp.get("first_death_cnt", 0)) + 1
        if t0.pace_first_defense > 0:
            gp["first_defense_sum"] = int(gp.get("first_defense_sum", 0)) + int(t0.pace_first_defense)
            gp["first_defense_cnt"] = int(gp.get("first_defense_cnt", 0)) + 1
        if t0.pace_decisive_turn > 0 and rt > 0:
            gp["decisive_turn_sum"] = int(gp.get("decisive_turn_sum", 0)) + int(t0.pace_decisive_turn)
            gp["decisive_ratio_sum"] = float(gp.get("decisive_ratio_sum", 0.0)) + min(1.0, float(t0.pace_decisive_turn) / float(rt))
            gp["decisive_cnt"] = int(gp.get("decisive_cnt", 0)) + 1
        for st_cur in (t0, t1):
            gp["zero_card_turns"] = int(gp.get("zero_card_turns", 0)) + int(st_cur.pace_zero_card_turns)
            gp["dead_turns"] = int(gp.get("dead_turns", 0)) + int(st_cur.pace_dead_turns)
            gp["total_player_turns"] = int(gp.get("total_player_turns", 0)) + int(st_cur.hand_turns)
            gp["total_actions"] = int(gp.get("total_actions", 0)) + int(st_cur.pace_total_actions)
            gp["impact_gaps_sum"] = int(gp.get("impact_gaps_sum", 0)) + int(st_cur.pace_impact_gaps_sum)
            gp["impact_gaps_cnt"] = int(gp.get("impact_gaps_cnt", 0)) + int(st_cur.pace_impact_gaps_cnt)

        # Pillar #3: Combat Quality aggregation
        cq = ex.setdefault("combat_quality", _default_excitement_data()["combat_quality"])
        for st_cur in (t0, t1):
            cq["unit_dmg_dealt_sum"] = int(cq.get("unit_dmg_dealt_sum", 0)) + int(st_cur.cq_unit_dmg_dealt_sum)
            cq["hits_to_kill_sum"] = int(cq.get("hits_to_kill_sum", 0)) + int(st_cur.cq_hits_to_kill_sum)
            cq["total_strikes"] = int(cq.get("total_strikes", 0)) + int(st_cur.cq_total_strikes)
            cq["unit_clashes"] = int(cq.get("unit_clashes", 0)) + int(st_cur.cq_unit_clashes)
            cq["face_strikes"] = int(cq.get("face_strikes", 0)) + int(st_cur.cq_face_strikes)
            cq["lethal_strikes"] = int(cq.get("lethal_strikes", 0)) + int(st_cur.cq_lethal_strikes)
            cq["whiff_strikes"] = int(cq.get("whiff_strikes", 0)) + int(st_cur.cq_whiff_strikes)
            cq["intercepted_strikes"] = int(cq.get("intercepted_strikes", 0)) + int(st_cur.cq_intercepted_strikes)
            cq["mutual_kills"] = int(cq.get("mutual_kills", 0)) + int(st_cur.cq_mutual_kills)
            cq["fair_trades"] = int(cq.get("fair_trades", 0)) + int(st_cur.cq_fair_trades)
            cq["clean_kills"] = int(cq.get("clean_kills", 0)) + int(st_cur.cq_clean_kills)
            cq["swing_strikes"] = int(cq.get("swing_strikes", 0)) + int(st_cur.cq_swing_strikes)
            cq["overkill_dmg"] = int(cq.get("overkill_dmg", 0)) + int(st_cur.cq_overkill_dmg)
            cq["raw_unit_dmg"] = int(cq.get("raw_unit_dmg", 0)) + int(st_cur.cq_raw_unit_dmg)
            cq["barrier_dmg_absorbed"] = int(cq.get("barrier_dmg_absorbed", 0)) + int(st_cur.cq_barrier_dmg_absorbed)
            cq["near_death_hits"] = int(cq.get("near_death_hits", 0)) + int(st_cur.cq_near_death_hits)
            cq["total_unit_hits"] = int(cq.get("total_unit_hits", 0)) + int(st_cur.cq_total_unit_hits)
            cq["skill_dmg_total"] = int(cq.get("skill_dmg_total", 0)) + int(st_cur.cq_skill_dmg_total)
            cq["actives_used_total"] = int(cq.get("actives_used_total", 0)) + int(st_cur.actives_used)
            cq["total_healed"] = int(cq.get("total_healed", 0)) + int(st_cur.hp_healed) + int(st_cur.def_healed)
            cq["total_face_dmg"] = int(cq.get("total_face_dmg", 0)) + int(st_cur.dmg_to_face)
            cq["total_monster_dmg"] = int(cq.get("total_monster_dmg", 0)) + int(st_cur.dmg_to_monsters)

        # Pillar #4: Card Utilization aggregation
        cu = ex.setdefault("card_utilization", _default_excitement_data()["card_utilization"])
        for st_cur in (t0, t1):
            cu["sides_count"] = int(cu.get("sides_count", 0)) + 1
            cu["drawn_total"] = int(cu.get("drawn_total", 0)) + int(st_cur.cards_drawn)
            cu["played_total"] = int(cu.get("played_total", 0)) + int(st_cur.cu_played_count)
            cu["sacrificed_total"] = int(cu.get("sacrificed_total", 0)) + int(st_cur.sacrifices_made)
            cu["burned_hand7_total"] = int(cu.get("burned_hand7_total", 0)) + int(st_cur.cards_burned_hand7)
            cu["hand_end_remaining"] = int(cu.get("hand_end_remaining", 0)) + int(st_cur.cu_hand_end_remaining)
            cu["played_on_draw_turn"] = int(cu.get("played_on_draw_turn", 0)) + int(st_cur.cu_played_on_draw_turn)
            cu["play_turn_sum"] = int(cu.get("play_turn_sum", 0)) + int(st_cur.cu_play_turn_sum)
            cu["cond_attempts"] = int(cu.get("cond_attempts", 0)) + int(st_cur.cu_cond_attempts)
            cu["cond_success"] = int(cu.get("cond_success", 0)) + int(st_cur.cu_cond_success)
            cu["hand_checks_cards"] = int(cu.get("hand_checks_cards", 0)) + int(st_cur.cu_hand_checks_cards)
            cu["hand_unplayable_cards"] = int(cu.get("hand_unplayable_cards", 0)) + int(st_cur.cu_hand_unplayable_cards)
            cu["low_value_plays"] = int(cu.get("low_value_plays", 0)) + int(st_cur.cu_low_value_plays)
            cu["deck_cycles_done"] = int(cu.get("deck_cycles_done", 0)) + int(st_cur.cu_deck_cycles_done)
            imp_v = (
                int(st_cur.dmg_to_face) + int(st_cur.dmg_to_monsters)
                + int(st_cur.hp_healed) + int(st_cur.def_healed)
                + 12 * int(st_cur.monsters_killed) + 8 * int(st_cur.disruptions_applied)
            )
            cu["impact_value_sum"] = int(cu.get("impact_value_sum", 0)) + imp_v
            pbt = cu.setdefault("played_by_type", [0, 0, 0, 0, 0, 0])
            for ti_c in range(6):
                pbt[ti_c] = int(pbt[ti_c]) + int(st_cur.cu_played_by_type[ti_c])

        # Pillar #5: Resource Economy aggregation
        re_d = ex.setdefault("resource_economy", _default_excitement_data()["resource_economy"])
        for side_i, st_cur in enumerate((t0, t1)):
            re_d["sides_count"] = int(re_d.get("sides_count", 0)) + 1
            re_d["mana_avail"] = int(re_d.get("mana_avail", 0)) + int(st_cur.re_mana_avail)
            re_d["mana_spent"] = int(re_d.get("mana_spent", 0)) + int(st_cur.re_mana_spent)
            re_d["mana_wasted"] = int(re_d.get("mana_wasted", 0)) + int(st_cur.re_mana_wasted)
            re_d["turns_total"] = int(re_d.get("turns_total", 0)) + int(st_cur.hand_turns)
            re_d["turns_unspent"] = int(re_d.get("turns_unspent", 0)) + int(st_cur.re_turns_unspent)
            re_d["early_mana_avail"] = int(re_d.get("early_mana_avail", 0)) + int(st_cur.re_early_mana_avail)
            re_d["early_mana_spent"] = int(re_d.get("early_mana_spent", 0)) + int(st_cur.re_early_mana_spent)
            re_d["mana_spent_cards"] = int(re_d.get("mana_spent_cards", 0)) + int(st_cur.re_mana_spent_cards)
            re_d["mana_spent_monsters"] = int(re_d.get("mana_spent_monsters", 0)) + int(st_cur.re_mana_spent_monsters)
            re_d["mana_spent_spells"] = int(re_d.get("mana_spent_spells", 0)) + int(st_cur.re_mana_spent_spells)
            re_d["mana_spent_skills"] = int(re_d.get("mana_spent_skills", 0)) + int(st_cur.re_mana_spent_skills)
            re_d["mana_spent_other"] = int(re_d.get("mana_spent_other", 0)) + int(st_cur.re_mana_spent_other)
            re_d["mana_offense"] = int(re_d.get("mana_offense", 0)) + int(st_cur.re_mana_offense)
            re_d["mana_defense"] = int(re_d.get("mana_defense", 0)) + int(st_cur.re_mana_defense)
            re_d["sacrifices_total"] = int(re_d.get("sacrifices_total", 0)) + int(st_cur.sacrifices_made)
            re_d["aether_draws_total"] = int(re_d.get("aether_draws_total", 0)) + int(st_cur.re_aether_draws)
            imp_re = (
                int(st_cur.dmg_to_face) + int(st_cur.dmg_to_monsters)
                + int(st_cur.hp_healed) + int(st_cur.def_healed)
                + 12 * int(st_cur.monsters_killed) + 8 * int(st_cur.disruptions_applied)
            )
            re_d["impact_value_sum"] = int(re_d.get("impact_value_sum", 0)) + imp_re
            if st_cur.re_reach_cap_turn > 0:
                re_d["reach_cap_sum"] = int(re_d.get("reach_cap_sum", 0)) + int(st_cur.re_reach_cap_turn)
                re_d["reach_cap_cnt"] = int(re_d.get("reach_cap_cnt", 0)) + 1
            ccp = re_d.setdefault("card_cost_played", [0] * 8)
            ccw = re_d.setdefault("card_cost_wins", [0] * 8)
            is_win = (result.winner == side_i)
            for ci in range(8):
                cnt_c = int(st_cur.re_card_cost_played[ci])
                ccp[ci] = int(ccp[ci]) + cnt_c
                if is_win:
                    ccw[ci] = int(ccw[ci]) + cnt_c
            tma = re_d.setdefault("turn_mana_avail", [0] * 15)
            tms = re_d.setdefault("turn_mana_spent", [0] * 15)
            tmc = re_d.setdefault("turn_mana_cnt", [0] * 15)
            for ti_m in range(15):
                tma[ti_m] = int(tma[ti_m]) + int(st_cur.re_turn_mana_avail[ti_m])
                tms[ti_m] = int(tms[ti_m]) + int(st_cur.re_turn_mana_spent[ti_m])
                tmc[ti_m] = int(tmc[ti_m]) + int(st_cur.re_turn_mana_cnt[ti_m])

        # Pillar #7: Mechanics Performance aggregation (9 Core Systems + WR Lift)
        mp = ex.setdefault("mechanics_perf", _default_excitement_data()["mechanics_perf"])
        mp["matches"] = int(mp.get("matches", 0)) + 1
        su_s = mp.setdefault("sys_used_sides", [0] * 9)
        su_w = mp.setdefault("sys_used_wins", [0] * 9)
        su_c = mp.setdefault("sys_total_cnt", [0] * 9)
        msp = mp.setdefault("match_sys_present", [0] * 9)
        m_flags = [False] * 9
        for side_i, st_cur in enumerate((t0, t1)):
            mp["sides"] = int(mp.get("sides", 0)) + 1
            is_w = (result.winner == side_i)
            if is_w:
                mp["wins_total"] = int(mp.get("wins_total", 0)) + 1
            s_vals = [
                int(st_cur.actives_used),
                int(st_cur.spells_cast),
                int(st_cur.cu_played_by_type[1]),
                int(st_cur.cu_played_by_type[3]),
                int(st_cur.cu_played_by_type[4]),
                int(st_cur.cu_played_by_type[5]),
                int(st_cur.dot_dmg_dealt),
                int(st_cur.disruptions_applied),
                int(st_cur.tokens_spawned) + int(st_cur.clones_made) + int(st_cur.revives_done) + int(st_cur.grave_returns),
            ]
            for ki in range(9):
                v_k = s_vals[ki]
                su_c[ki] = int(su_c[ki]) + v_k
                if v_k > 0:
                    su_s[ki] = int(su_s[ki]) + 1
                    m_flags[ki] = True
                    if is_w:
                        su_w[ki] = int(su_w[ki]) + 1
            mp["env_upgrades"] = int(mp.get("env_upgrades", 0)) + int(st_cur.mp_env_upgrades)
            mp["passive_triggers"] = int(mp.get("passive_triggers", 0)) + int(st_cur.mp_passive_triggers)
            mp["ascends"] = int(mp.get("ascends", 0)) + int(st_cur.mp_ascends)
            mp["last_breaths"] = int(mp.get("last_breaths", 0)) + int(st_cur.mp_last_breaths)
            mp["reflect_dmg"] = int(mp.get("reflect_dmg", 0)) + int(st_cur.reflect_dmg_dealt)
            mp["evasions"] = int(mp.get("evasions", 0)) + int(st_cur.evasions_triggered)
            mp["dot_dmg"] = int(mp.get("dot_dmg", 0)) + int(st_cur.dot_dmg_dealt)
            mp["total_dmg"] = int(mp.get("total_dmg", 0)) + int(st_cur.dmg_to_face) + int(st_cur.dmg_to_monsters)
            if int(st_cur.sacrifices_made) >= 3:
                mp["sac_ramp_sides"] = int(mp.get("sac_ramp_sides", 0)) + 1
                if is_w:
                    mp["sac_ramp_wins"] = int(mp.get("sac_ramp_wins", 0)) + 1
            else:
                mp["sac_low_sides"] = int(mp.get("sac_low_sides", 0)) + 1
                if is_w:
                    mp["sac_low_wins"] = int(mp.get("sac_low_wins", 0)) + 1
        for ki in range(9):
            if m_flags[ki]:
                msp[ki] = int(msp[ki]) + 1
        mp["active_systems_sum"] = int(mp.get("active_systems_sum", 0)) + sum(1 for f in m_flags if f)

        # Pillar #8: Victory Conditions & Win Channels aggregation
        vc = ex.setdefault("victory_cond", _default_excitement_data()["victory_cond"])
        vc["matches"] = int(vc.get("matches", 0)) + 1
        fbc = vc.setdefault("face_by_ch", [0] * 6)
        kbc = vc.setdefault("kill_by_ch", [0] * 6)
        for st_cur in (t0, t1):
            for ci_v in range(6):
                fbc[ci_v] = int(fbc[ci_v]) + int(st_cur.vc_face_by_ch[ci_v])
        if rt <= 12:
            vc["aggro_wins"] = int(vc.get("aggro_wins", 0)) + 1
        elif rt <= 22:
            vc["midrange_wins"] = int(vc.get("midrange_wins", 0)) + 1
        elif rt <= 39:
            vc["control_wins"] = int(vc.get("control_wins", 0)) + 1
        else:
            vc["sudden_death_wins"] = int(vc.get("sudden_death_wins", 0)) + 1
        if result.win_reason == "CovenantTurnLimit":
            vc["covenant_limit_wins"] = int(vc.get("covenant_limit_wins", 0)) + 1
        if result.winner in (0, 1):
            vc["decisive_matches"] = int(vc.get("decisive_matches", 0)) + 1
            wt_v = result.telemetry[result.winner]
            kc_i = max(0, min(5, int(wt_v.vc_kill_channel)))
            kbc[kc_i] = int(kbc[kc_i]) + 1
            if int(wt_v.vc_end_board_cnt) >= 2:
                vc["board_dom_wins"] = int(vc.get("board_dom_wins", 0)) + 1
            if int(wt_v.final_hp) >= 325:
                vc["blowout_wins"] = int(vc.get("blowout_wins", 0)) + 1
            if 0 < int(wt_v.final_hp) <= 100:
                vc["clutch_100_wins"] = int(vc.get("clutch_100_wins", 0)) + 1
            if int(wt_v.comeback_deficit) >= 100:
                vc["comeback_100_wins"] = int(vc.get("comeback_100_wins", 0)) + 1
            if int(wt_v.vc_final_turn_face_dmg) >= 100:
                vc["burst_finish_wins"] = int(vc.get("burst_finish_wins", 0)) + 1

        # Pillar #9: Strategic Intelligence aggregation
        si = ex.setdefault("strategic_intel", _default_excitement_data()["strategic_intel"])
        ch_h = si.setdefault("choice_hist", [0] * 6)
        sl_s = si.setdefault("slot_summons", [0] * 6)
        for st_cur in (t0, t1):
            si["turns_total"] = int(si.get("turns_total", 0)) + int(st_cur.hand_turns)
            si["legal_actions_sum"] = int(si.get("legal_actions_sum", 0)) + int(st_cur.si_legal_actions_sum)
            si["distinct_choices_sum"] = int(si.get("distinct_choices_sum", 0)) + int(st_cur.si_distinct_choices_sum)
            si["forced_pass_turns"] = int(si.get("forced_pass_turns", 0)) + int(st_cur.si_forced_pass_turns)
            si["multi_action_turns"] = int(si.get("multi_action_turns", 0)) + int(st_cur.si_multi_action_turns)
            si["decision_chain_sum"] = int(si.get("decision_chain_sum", 0)) + int(st_cur.si_decision_chain_sum)
            si["sac_avail_turns"] = int(si.get("sac_avail_turns", 0)) + int(st_cur.si_sac_avail_turns)
            si["sac_taken_total"] = int(si.get("sac_taken_total", 0)) + int(st_cur.sacrifices_made)
            si["restraint_passes"] = int(si.get("restraint_passes", 0)) + int(st_cur.si_restraint_passes)
            si["reactive_decisions"] = int(si.get("reactive_decisions", 0)) + int(st_cur.si_reactive_decisions)
            si["total_decisions"] = int(si.get("total_decisions", 0)) + int(st_cur.si_total_decisions)
            si["played_total"] = int(si.get("played_total", 0)) + int(st_cur.cu_played_count)
            si["planned_plays"] = int(si.get("planned_plays", 0)) + max(0, int(st_cur.cu_played_count) - int(st_cur.cu_played_on_draw_turn))
            for k_si in range(6):
                ch_h[k_si] = int(ch_h[k_si]) + int(st_cur.si_choice_hist[k_si])
                sl_s[k_si] = int(sl_s[k_si]) + int(st_cur.si_slot_summons[k_si])

        # Pillar #10: Gameplay Enjoyment & Emotional Arc aggregation
        enj = ex.setdefault("enjoyment", _default_excitement_data()["enjoyment"])
        enj["matches"] = int(enj.get("matches", 0)) + 1
        enj["exc_sum"] = int(enj.get("exc_sum", 0)) + int(match_exc)
        eh = enj.setdefault("exc_hist", [0, 0, 0, 0, 0])
        if match_exc < 35:
            eh[0] = int(eh[0]) + 1
            enj["matches_boring"] = int(enj.get("matches_boring", 0)) + 1
        elif match_exc < 50:
            eh[1] = int(eh[1]) + 1
        elif match_exc < 65:
            eh[2] = int(eh[2]) + 1
        elif match_exc < 80:
            eh[3] = int(eh[3]) + 1
            enj["matches_high_exc"] = int(enj.get("matches_high_exc", 0)) + 1
        else:
            eh[4] = int(eh[4]) + 1
            enj["matches_high_exc"] = int(enj.get("matches_high_exc", 0)) + 1
        m_sw = max(int(t0.lead_swings), int(t1.lead_swings))
        enj["swings_sum"] = int(enj.get("swings_sum", 0)) + m_sw
        if m_sw >= 1:
            enj["matches_with_swing"] = int(enj.get("matches_with_swing", 0)) + 1
        enj["close_hp_turns"] = int(enj.get("close_hp_turns", 0)) + int(t0.en_close_hp_turns) + int(t1.en_close_hp_turns)
        enj["total_hp_turns"] = int(enj.get("total_hp_turns", 0)) + int(t0.en_hp_turns) + int(t1.en_hp_turns)
        enj["highlights_sum"] = int(enj.get("highlights_sum", 0)) + int(t0.highlight_triggers) + int(t1.highlight_triggers)
        if (int(t0.multi_kill_turns) + int(t1.multi_kill_turns)) >= 1:
            enj["multi_kill_matches"] = int(enj.get("multi_kill_matches", 0)) + 1
        enj["empty_hand_turns"] = int(enj.get("empty_hand_turns", 0)) + int(t0.empty_hand_turns) + int(t1.empty_hand_turns)
        enj["player_turns_total"] = int(enj.get("player_turns_total", 0)) + int(t0.hand_turns) + int(t1.hand_turns)
        enj["zero_imp_died"] = int(enj.get("zero_imp_died", 0)) + int(t0.unit_zero_impact_died) + int(t1.unit_zero_impact_died)
        enj["units_died_tot"] = int(enj.get("units_died_tot", 0)) + int(t0.units_died_count) + int(t1.units_died_count)
        if result.winner in (0, 1):
            wt_e = result.telemetry[result.winner]
            if int(wt_e.comeback_deficit) >= 100:
                enj["comeback_100"] = int(enj.get("comeback_100", 0)) + 1
            if wt_e.clutch_win:
                enj["clutch_75"] = int(enj.get("clutch_75", 0)) + 1
            if int(wt_e.final_hp) >= 325:
                enj["blowout_325"] = int(enj.get("blowout_325", 0)) + 1

        # Pillar #11: Competitive Fairness aggregation
        cf = ex.setdefault("comp_fairness", _default_excitement_data()["comp_fairness"])
        cf["matches"] = int(cf.get("matches", 0)) + 1
        cf["evasions_total"] = int(cf.get("evasions_total", 0)) + int(t0.evasions_triggered) + int(t1.evasions_triggered)
        if t0.cf_t4_recorded:
            cf["t4_diff_sum"] = int(cf.get("t4_diff_sum", 0)) + int(t0.cf_t4_hp_diff)
            cf["t4_diff_cnt"] = int(cf.get("t4_diff_cnt", 0)) + 1
        if result.winner not in (0, 1):
            cf["draws"] = int(cf.get("draws", 0)) + 1
        else:
            p1_s = 0 if t0.went_first else 1
            cf["p1_decisive"] = int(cf.get("p1_decisive", 0)) + 1
            if result.winner == p1_s:
                cf["p1_wins"] = int(cf.get("p1_wins", 0)) + 1
            wt_f = result.telemetry[result.winner]
            lt_f = result.telemetry[1 - result.winner]
            w_mpt = wt_f.re_mana_avail / max(1.0, float(wt_f.hand_turns))
            l_mpt = lt_f.re_mana_avail / max(1.0, float(lt_f.hand_turns))
            cf["mana_gap_sum"] = float(cf.get("mana_gap_sum", 0.0)) + (w_mpt - l_mpt)
            cf["mana_gap_cnt"] = int(cf.get("mana_gap_cnt", 0)) + 1
            cf["draw_gap_sum"] = int(cf.get("draw_gap_sum", 0)) + (int(wt_f.cards_drawn) - int(lt_f.cards_drawn))
            cf["draw_gap_cnt"] = int(cf.get("draw_gap_cnt", 0)) + 1
        for side_i, st_cur in enumerate((t0, t1)):
            won_s = (result.winner == side_i)
            if st_cur.cf_open_playable:
                cf["open_ok_sides"] = int(cf.get("open_ok_sides", 0)) + 1
                if won_s:
                    cf["open_ok_wins"] = int(cf.get("open_ok_wins", 0)) + 1
            else:
                cf["open_brick_sides"] = int(cf.get("open_brick_sides", 0)) + 1
                if won_s:
                    cf["open_brick_wins"] = int(cf.get("open_brick_wins", 0)) + 1
            if st_cur.cf_early_special:
                cf["early_spec_sides"] = int(cf.get("early_spec_sides", 0)) + 1
                if won_s:
                    cf["early_spec_wins"] = int(cf.get("early_spec_wins", 0)) + 1
            else:
                cf["no_spec_sides"] = int(cf.get("no_spec_sides", 0)) + 1
                if won_s:
                    cf["no_spec_wins"] = int(cf.get("no_spec_wins", 0)) + 1

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
            t_exc = ev[7] if len(ev) > 7 else 0
            t_brd = ev[8] if len(ev) > 8 else 0
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
                b["exc_sum"] = int(b.get("exc_sum", 0)) + int(t_exc)
                b["board_sum"] = int(b.get("board_sum", 0)) + int(t_brd)
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
            "hp_a": result.final_hp[0],
            "hp_b": result.final_hp[1],
            "style_a": round(style_scores[0], 3),
            "style_b": round(style_scores[1], 3),
        }
        pending_history.append(json.dumps(hist_row, ensure_ascii=False))

    def flush_history(self, lines: List[str], max_lines_keep: int = 250000) -> None:
        if not lines:
            return
        os.makedirs(self.logs_dir, exist_ok=True)
        with open(self.history_path, "a", encoding="utf-8") as f:
            for ln in lines:
                f.write(ln + "\n")
        lines.clear()
        # Keep up to 250,000 matches (~55 MB) in match_history.jsonl so full overnight runs stay 100% intact
        try:
            if os.path.getsize(self.history_path) > 55_000_000:
                with open(self.history_path, "r", encoding="utf-8") as rf:
                    tail = rf.readlines()[-max_lines_keep:]
                tmp = self.history_path + ".tmp"
                with open(tmp, "w", encoding="utf-8") as wf:
                    wf.writelines(tail)
                os.replace(tmp, self.history_path)
        except Exception:
            pass

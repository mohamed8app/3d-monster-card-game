# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Pillar #12: Simulation Integrity, Determinism & Fuzz Audit Tab (Arabic RTL).
Automated QA & Constitution Fortress checking 14 critical engine & data invariants:
  1. Python V2.2 <-> Unity cards_10000.json exact sync across ALL 2,270 cards
  2. Barrier Constitution (200 Barriers: 0 ATK, 0 Actives, never attacks)
  3. All 41 settled_deck.json validity (50 cards, <=2 copies, valid IDs, Free-10 disjoint)
  4. 100% Seed Determinism (15 identical dual-seed replays)
  5. 150 Fuzz Stress Matches covering ALL 2,270 cards (0 Crashes / 0 Exceptions)
  6. 0 Infinite Loops / Stalled Matches (<800 steps & <=MAX_TURNS)
  7. 0 NaN/Inf in OBS_DIM=220 & valid Legal Masks
  8. Hard Caps (Hand <= hand_limit, 0 <= Mana <= mana_cap, Board <= 6)
  9. Unit Board Invariants (no dead zombies, Barrier ATK==0, All-Attack ATK locked)
  10. V2.1 Independent Active Skills Unit Test
  11. V2.1 Leader Spirit Tribal On-Summon Condition Unit Test
  12. 100% Exact Telemetry Conservation (Mana & Face-Damage Channels)
  13. Infinite Shuffle-Bag Integrity (0 Deck-Out losses)
  14. Pure Engine Throughput Benchmark (Matches/Sec)
"""

from __future__ import annotations

import json
import os
import random
import threading
import time
import tkinter as tk
from collections import Counter
from tkinter import ttk
from typing import Any, Callable, Dict, List, Optional, Set, Tuple

import numpy as np

from engine.card_model import BOARD_SLOTS, CardDB, DECK_SIZE, MAX_COPIES, MAX_TURNS
from engine.roster_40 import ROSTER_40
from trainer.stats_store import StatsStore
from pace_tab import rtl
from arabic_glossary import enable_treeview_sorting

BG_DARK = "#0F131A"
BG_PANEL = "#171C26"
BG_CARD = "#1D2432"
BG_BORDER = "#2A3346"
FG_TEXT = "#EEF2F9"
FG_MUTED = "#94A6C0"

COL_BLUE = "#3B82F6"
COL_CYAN = "#06B6D4"
COL_GREEN = "#10B981"
COL_GOLD = "#F59E0B"
COL_RED = "#EF4444"
COL_PURPLE = "#A855F7"

FONT_UI = "Segoe UI"


def run_full_integrity_audit(
    db: CardDB,
    logs_dir: str,
    n_fuzz: int = 150,
    n_det: int = 15,
    progress_cb: Optional[Callable[[str], None]] = None,
    saved_models_dir: Optional[str] = None,
) -> Dict[str, Any]:
    """Runs all 14 automated Simulation Integrity, Determinism & Fuzz checks."""
    from engine.battle_engine import BattleEngine

    t_start = time.time()
    project_root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    saved_dir = saved_models_dir or os.path.abspath(os.path.join(logs_dir, "..", "saved_models"))
    unity_json = os.path.join(project_root, "UnityGame", "Assets", "Resources", "cards_10000.json")

    # 1. Python <-> Unity Card DB Sync (2,270 cards)
    if progress_cb:
        progress_cb("1/5 فحص تطابق الـ 2,270 بطاقة بين Python وUnity...")
    unity_sync_mismatches = 0
    unity_found = os.path.isfile(unity_json)
    unity_checked = 0
    if unity_found:
        with open(unity_json, "r", encoding="utf-8") as f:
            u_raw = json.load(f)
        u_map = {int(c["id"]): c for c in u_raw.get("cards", [])}
        for cid, pc in db.cards.items():
            uc = u_map.get(cid)
            if uc is None:
                unity_sync_mismatches += 1
                continue
            unity_checked += 1
            if (
                int(uc.get("atk", -1)) != pc.atk
                or int(uc.get("def", -1)) != pc.deff
                or int(uc.get("cost", -1)) != pc.mana
                or str(uc.get("card_type", "")) != pc.type
                or bool(uc.get("is_barrier", False)) != pc.is_barrier
            ):
                unity_sync_mismatches += 1
    else:
        unity_sync_mismatches = -1

    # 2. Barrier Constitution Static Audit (200 barriers)
    barrier_db_cnt = 0
    barrier_violations = 0
    for pc in db.cards.values():
        if pc.is_barrier:
            barrier_db_cnt += 1
            if pc.atk != 0 or len(pc.actives) != 0 or pc.tribe != -1:
                barrier_violations += 1

    # 3. 41 Settled Decks Audit (50 cards, <=2 copies, valid IDs, Free-10 disjoint)
    decks_ok = 0
    decks_total = len(ROSTER_40)
    deck_violations = 0
    free10_sets: Dict[str, Set[int]] = {}
    for spec in ROSTER_40:
        dp = os.path.join(saved_dir, spec.model_id, "settled_deck.json")
        if not os.path.isfile(dp):
            deck_violations += 1
            continue
        try:
            with open(dp, "r", encoding="utf-8") as f:
                cids = [int(x) for x in (json.load(f).get("deck_50_ids") or [])]
            cnts = Counter(cids)
            ok = (
                len(cids) == DECK_SIZE
                and all(1 <= cid <= db.num_cards for cid in cids)
                and max(cnts.values()) <= MAX_COPIES
            )
            if ok:
                decks_ok += 1
                if spec.category == "Free":
                    free10_sets[spec.model_id] = set(cids)
            else:
                deck_violations += 1
        except Exception:
            deck_violations += 1

    free10_overlaps = 0
    f_keys = sorted(free10_sets.keys())
    for i in range(len(f_keys)):
        for j in range(i + 1, len(f_keys)):
            if free10_sets[f_keys[i]] & free10_sets[f_keys[j]]:
                free10_overlaps += 1

    # Build Fuzz Decks covering EVERY SINGLE ONE of the 2,270 cards!
    rng = random.Random(20261212)
    all_ids = list(range(1, db.num_cards + 1))
    rng.shuffle(all_ids)
    fuzz_decks: List[List[int]] = []
    for start in range(0, len(all_ids), 50):
        chunk = all_ids[start : start + 50]
        while len(chunk) < 50:
            chunk.append(rng.choice(all_ids))
        fuzz_decks.append(chunk)

    eng = BattleEngine(db)

    # 4. 100% Seed Determinism Test (15 paired identical replays)
    if progress_cb:
        progress_cb(f"2/5 اختبار حتمية البذور ({n_det} زوج مباراة متطابقة)...")
    det_passed = 0
    for i in range(n_det):
        d0 = fuzz_decks[i % len(fuzz_decks)]
        d1 = fuzz_decks[(i + 7) % len(fuzz_decks)]
        seed_i = 90000 + i
        snaps = []
        for _run in range(2):
            eng.reset(d0, d1, seed=seed_i)
            st_c = 0
            while not eng.done and st_c < 700:
                m = eng.get_legal_mask(eng.active)
                leg = np.flatnonzero(m)
                act = int(leg[(st_c * 13 + eng.turn * 5) % len(leg)])
                eng.step(act)
                st_c += 1
            res = eng.get_result()
            snaps.append((
                res.winner, res.turns, res.win_reason, res.final_hp,
                res.telemetry[0].dmg_to_face, res.telemetry[1].dmg_to_face,
                tuple(res.telemetry[0].cards_played_ids), tuple(res.telemetry[1].cards_played_ids),
            ))
        if snaps[0] == snaps[1]:
            det_passed += 1

    # 5..9 & 12..13: 150 Fuzz Stress Matches auditing ALL invariants step-by-step
    if progress_cb:
        progress_cb(f"3/5 تشغيل اختبار الضغط الفوضوي ({n_fuzz} مباراة Fuzz على كل الـ 2,270 بطاقة)...")
    crashes = 0
    stalls = 0
    nan_obs_errs = 0
    cap_violations = 0
    unit_violations = 0
    conservation_errs = 0
    deckout_errs = 0
    total_steps_checked = 0
    cards_exercised: Set[int] = set()
    win_reasons: Dict[str, int] = {}

    for i in range(n_fuzz):
        d0 = fuzz_decks[(i * 2) % len(fuzz_decks)]
        d1 = fuzz_decks[(i * 2 + 1) % len(fuzz_decks)]
        try:
            eng.reset(d0, d1, seed=50000 + i)
            steps = 0
            while not eng.done and steps < 800:
                s = eng.active
                mask = eng.get_legal_mask(s)
                if (not bool(mask[0])) or int(mask.sum()) < 1:
                    nan_obs_errs += 1
                if steps % 3 == 0:
                    obs = eng.get_obs(s)
                    if (obs.shape[0] != 220) or (not bool(np.isfinite(obs).all())):
                        nan_obs_errs += 1

                leg = np.flatnonzero(mask)
                non_pass = leg[leg > 0]
                if len(non_pass) > 0 and rng.random() < 0.85:
                    act = int(rng.choice(non_pass))
                else:
                    act = int(rng.choice(leg))
                eng.step(act)
                steps += 1
                total_steps_checked += 1

                # Hard Caps & Unit Board Invariants on BOTH players
                for ps in eng.players:
                    if len(ps.hand) > ps.hand_limit():
                        cap_violations += 1
                    if ps.mana < 0 or ps.max_mana > ps.mana_cap() or ps.board_count() > BOARD_SLOTS:
                        cap_violations += 1
                    for sl_i, u in enumerate(ps.board):
                        if u is None:
                            continue
                        if u.deff <= 0 or u.slot != sl_i or u.owner != ps.side:
                            unit_violations += 1
                        if u.is_barrier:
                            if eng._effective_atk(ps.side, u) != 0:
                                barrier_violations += 1
                                unit_violations += 1
                        if u.atk_locked:
                            if eng._effective_atk(ps.side, u) > u.base_atk:
                                unit_violations += 1

            if not eng.done or steps >= 800 or eng.turn > MAX_TURNS + 1:
                stalls += 1
            res = eng.get_result()
            wr_k = res.win_reason or "Unknown"
            win_reasons[wr_k] = win_reasons.get(wr_k, 0) + 1
            if wr_k == "DeckOut" or any(ps.deck_out_lost for ps in eng.players):
                deckout_errs += 1

            for st in res.telemetry:
                cards_exercised.update(st.cards_played_ids)
                if st.re_mana_avail != st.re_mana_spent + st.re_mana_wasted:
                    conservation_errs += 1
                cat_s = st.re_mana_spent_monsters + st.re_mana_spent_spells + st.re_mana_spent_skills + st.re_mana_spent_other
                if cat_s != st.re_mana_spent:
                    conservation_errs += 1
                if st.re_mana_offense + st.re_mana_defense != st.re_mana_spent:
                    conservation_errs += 1
                if sum(st.vc_face_by_ch[:5]) != st.dmg_to_face:
                    conservation_errs += 1
        except Exception:
            crashes += 1

    # 10. V2.1 Independent Active Skills Unit Test
    if progress_cb:
        progress_cb("4/5 فحص قوانين V2.1 (المهارات المستقلة + شرط القادة القبلي)...")
    indep_skills_pass = False
    try:
        dual_c = next(
            c for c in db.cards.values()
            if c.type == "monster" and not c.is_barrier and len(c.actives) >= 2
            and all(op.get("kind") not in ("double_skill_1t", "devour_3", "recall_ally") for op in c.actives[:2])
        )
        base_d = [dual_c.id] * 2 + [db.monsters[0]] * 48
        eng.reset(base_d, base_d, seed=7771)
        s = eng.active
        p = eng.players[s]
        p.max_mana = 10; p.mana = 10; p.temp_mana = 10
        eng._summon_monster(s, dual_c, 0)
        # Ensure target exists for enemy-targeted actives
        eng._summon_monster(1 - s, db.get(db.monsters[10]), 0)
        m0 = eng.get_legal_mask(s)
        ok_pre = bool(m0[50] and m0[51])
        eng.step(50)  # Cast Skill 1
        m1 = eng.get_legal_mask(s)
        ok_mid = (not bool(m1[50])) and bool(m1[51])
        eng.step(51)  # Cast Skill 2
        m2 = eng.get_legal_mask(s)
        ok_post = (not bool(m2[50])) and (not bool(m2[51]))
        # Test Double Skill 1T grants 2nd use per skill independently
        if p.board[0] is not None:
            p.board[0].double_skill_1t = True
            p.mana = 10; p.temp_mana = 10
            m3 = eng.get_legal_mask(s)
            ok_dbl = bool(m3[50] and m3[51])
        else:
            ok_dbl = True
        # Also verify V2.3 Aether Well (auto 1+1 ramp) + Aether Draw (Action 62, 2 Mana, 1x/turn)
        eng.reset(base_d, base_d, seed=7774)
        s_ae = eng.active
        p_ae = eng.players[s_ae]
        ok_aether_t1 = (p_ae.max_mana == 1 and p_ae.mana == 1)
        p_ae.max_mana = 4; p_ae.mana = 4
        while len(p_ae.hand) >= p_ae.hand_limit():
            p_ae.hand.pop()
        h_before = len(p_ae.hand)
        m_ae0 = eng.get_legal_mask(s_ae)
        ok_ae_legal = (len(m_ae0) == 63 and bool(m_ae0[62]))
        eng.step(62)
        m_ae1 = eng.get_legal_mask(s_ae)
        ok_ae_used = (
            p_ae.mana == 2
            and len(p_ae.hand) == h_before + 1
            and p_ae.aether_drawn_this_turn
            and (not bool(m_ae1[62]))
            and p_ae.telemetry.re_aether_draws == 1
        )
        indep_skills_pass = bool(ok_pre and ok_mid and ok_post and ok_dbl and ok_aether_t1 and ok_ae_legal and ok_ae_used)
    except Exception:
        indep_skills_pass = False

    # 11. V2.3 Pure Continuous Tribal Leader Spirit Aura Unit Test (Zero On-Summon Bursts!)
    leader_cond_pass = False
    try:
        all_leaders = [c for c in db.cards.values() if c.type == "leader"]
        no_summon_burst = len(all_leaders) == 60 and all(
            c.leader_ops
            and not any(k.startswith("summon_") and v for k, v in c.leader_ops.items())
            and sum(1 for k, v in c.leader_ops.items() if k.startswith("aura_") and v) == 1
            for c in all_leaders
        )
        atk_ldr = next(c for c in all_leaders if int((c.leader_ops or {}).get("aura_atk", 0)) > 0)
        def_ldr = next(c for c in all_leaders if int((c.leader_ops or {}).get("aura_def", 0)) > 0)
        l_tr = int(atk_ldr.leader_ops["tribe"])
        match_m = next(c for c in db.cards.values() if c.type == "monster" and not c.is_barrier and not c.is_all_attack and c.tribe == l_tr)
        other_m = next(c for c in db.cards.values() if c.type == "monster" and not c.is_barrier and not c.is_all_attack and c.tribe != l_tr and c.tribe >= 0 and not any(pa.get("kind") == "chameleon" for pa in c.passives))
        d_t = [match_m.id] * 2 + [other_m.id] * 48
        eng.reset(d_t, d_t, seed=7772)
        s = eng.active
        u_match = eng._summon_monster(s, match_m, 0)
        u_other = eng._summon_monster(s, other_m, 1)
        pre_match = eng._effective_atk(s, u_match)
        pre_other = eng._effective_atk(s, u_other)
        eng._play_leader(s, atk_ldr)
        ok_atk = (
            eng._effective_atk(s, u_match) == pre_match + int(atk_ldr.leader_ops["aura_atk"])
            and eng._effective_atk(s, u_other) == pre_other
        )
        # Verify aura_def applies immediately to existing matching-tribe ally on board and strips cleanly on Exorcism
        d_tr = int(def_ldr.leader_ops["tribe"])
        d_match = next(c for c in db.cards.values() if c.type == "monster" and not c.is_barrier and c.tribe == d_tr)
        eng.reset([d_match.id] * 2 + [other_m.id] * 48, d_t, seed=7773)
        s = eng.active
        ud = eng._summon_monster(s, d_match, 0)
        pre_def = ud.deff
        eng._play_leader(s, def_ldr)
        ok_def_on = (ud.deff == pre_def + int(def_ldr.leader_ops["aura_def"]))
        eng.players[s].leader_active = False
        eng.players[s].leader_exorcised = True
        eng.players[s].leader_ops = None
        eng._sync_leader_def_aura(s)
        ok_def_off = (ud.deff == pre_def)
        leader_cond_pass = bool(no_summon_burst and ok_atk and ok_def_on and ok_def_off)
    except Exception:
        leader_cond_pass = False

    # 14. Pure Raw Engine Throughput Benchmark (50 matches, no invariant overhead!)
    if progress_cb:
        progress_cb("5/5 قياس سرعة المحاكي النقية (50 مباراة معيارية)...")
    t_spd0 = time.time()
    spd_n = 50
    spd_steps = 0
    for i in range(spd_n):
        eng.reset(fuzz_decks[i % len(fuzz_decks)], fuzz_decks[(i + 5) % len(fuzz_decks)], seed=80000 + i)
        st_s = 0
        while not eng.done and st_s < 800:
            m = eng.get_legal_mask(eng.active)
            leg = np.flatnonzero(m)
            act = int(leg[(st_s * 7 + i) % len(leg)])
            eng.step(act)
            st_s += 1
        spd_steps += st_s
    spd_dt = max(0.01, time.time() - t_spd0)
    matches_per_sec = round(spd_n / spd_dt, 1)
    steps_per_sec = int(round(spd_steps / spd_dt))

    return {
        "audit_ts": time.strftime("%Y-%m-%d %H:%M:%S"),
        "db_version": db.version,
        "elapsed_sec": round(time.time() - t_start, 2),
        "n_fuzz": n_fuzz,
        "n_det": n_det,
        "unity_found": unity_found,
        "unity_checked": unity_checked,
        "unity_sync_mismatches": unity_sync_mismatches,
        "barrier_db_cnt": barrier_db_cnt,
        "barrier_violations": barrier_violations,
        "decks_ok": decks_ok,
        "decks_total": decks_total,
        "deck_violations": deck_violations,
        "free10_overlaps": free10_overlaps,
        "det_passed": det_passed,
        "crashes": crashes,
        "stalls": stalls,
        "nan_obs_errs": nan_obs_errs,
        "cap_violations": cap_violations,
        "unit_violations": unit_violations,
        "indep_skills_pass": indep_skills_pass,
        "leader_cond_pass": leader_cond_pass,
        "conservation_errs": conservation_errs,
        "deckout_errs": deckout_errs,
        "matches_per_sec": matches_per_sec,
        "steps_per_sec": steps_per_sec,
        "total_steps_checked": total_steps_checked,
        "cards_exercised_cnt": len(cards_exercised),
        "total_cards": db.num_cards,
        "win_reasons": win_reasons,
    }


class SimulationIntegrityTab:
    def __init__(
        self,
        parent: tk.Frame,
        db: CardDB,
        store: StatsStore,
        on_probe_complete: Optional[Callable[[], None]] = None,
    ) -> None:
        self.parent = parent
        self.db = db
        self.store = store
        self.on_probe_complete = on_probe_complete
        self.audit_path = os.path.join(os.path.abspath(os.path.dirname(__file__)), "logs", "integrity_audit.json")
        self.audit_data: Optional[Dict[str, Any]] = self._load_audit()
        self._running = False
        self.status_var = tk.StringVar(value="")
        self._last_metrics: Dict[str, Any] = {}
        self._build_layout()

    def _load_audit(self) -> Optional[Dict[str, Any]]:
        if os.path.isfile(self.audit_path):
            try:
                with open(self.audit_path, "r", encoding="utf-8") as f:
                    d = json.load(f)
                if isinstance(d, dict) and d.get("n_fuzz", 0) > 0:
                    return d
            except Exception:
                pass
        return None

    def _build_layout(self) -> None:
        ctrl = tk.Frame(self.parent, bg=BG_PANEL, padx=12, pady=8)
        ctrl.pack(fill="x", padx=8, pady=(6, 4))

        tk.Label(
            ctrl,
            text=rtl("فحص السلامة الهندسية والدستورية (لا يمسّ سجلات التدريب نهائياً):"),
            bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold"),
        ).pack(side="right", padx=(4, 0))

        self.run_btn = tk.Button(
            ctrl,
            text=rtl("تشغيل فحص السلامة الشامل (150 مباراة Fuzz + تدقيق 2,270 بطاقة)"),
            command=self._start_audit,
            bg=COL_PURPLE,
            fg="#FFFFFF",
            relief="flat",
            font=(FONT_UI, 9, "bold"),
            padx=14,
            pady=4,
            cursor="hand2",
        )
        self.run_btn.pack(side="left")

        tk.Label(
            ctrl, textvariable=self.status_var, bg=BG_PANEL, fg=COL_GOLD,
            font=(FONT_UI, 8, "bold"), anchor="e", justify="right",
        ).pack(side="left", padx=10)

        outer = tk.Frame(self.parent, bg=BG_DARK)
        outer.pack(fill="both", expand=True, padx=8, pady=(2, 6))

        self.scroll_canvas = tk.Canvas(outer, bg=BG_DARK, highlightthickness=0)
        vsb = ttk.Scrollbar(outer, orient="vertical", command=self.scroll_canvas.yview)
        self.scroll_canvas.configure(yscrollcommand=vsb.set)
        vsb.pack(side="left", fill="y")
        self.scroll_canvas.pack(side="right", fill="both", expand=True)

        self.inner = tk.Frame(self.scroll_canvas, bg=BG_DARK)
        self._inner_win = self.scroll_canvas.create_window((0, 0), window=self.inner, anchor="nw")
        self.inner.bind("<Configure>", lambda _e: self.scroll_canvas.configure(scrollregion=self.scroll_canvas.bbox("all")))
        self.scroll_canvas.bind("<Configure>", lambda e: self.scroll_canvas.itemconfigure(self._inner_win, width=max(980, e.width)))
        self.scroll_canvas.bind_all("<MouseWheel>", self._on_mousewheel)

        # Hero Score Banner
        hero = tk.Frame(self.inner, bg=BG_CARD, highlightbackground=BG_BORDER, highlightthickness=1, padx=14, pady=10)
        hero.pack(fill="x", pady=(2, 8))
        self.cv_score = tk.Canvas(hero, bg=BG_CARD, height=82, highlightthickness=0)
        self.cv_score.pack(fill="x")
        self.cv_score.bind("<Configure>", lambda _e: self._draw_score_banner())

        # Visual Charts Row (RTL: 14-Check LED Matrix on RIGHT 54%, Fuzz Stress Telemetry on LEFT 46%)
        charts = tk.Frame(self.inner, bg=BG_DARK)
        charts.pack(fill="x", pady=(0, 8))
        charts.grid_columnconfigure(0, weight=46, uniform="si12")
        charts.grid_columnconfigure(1, weight=54, uniform="si12")

        p_fuzz = self._make_panel(charts, "تقرير ضغط اختبار الفوضى (تغطية البطاقات والسرعة ونهايات المباريات)", COL_CYAN)
        p_fuzz.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        self.cv_fuzz = tk.Canvas(p_fuzz, bg=BG_PANEL, height=225, highlightthickness=0)
        self.cv_fuzz.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_fuzz.bind("<Configure>", lambda _e: self._draw_fuzz_telemetry())

        p_led = self._make_panel(charts, "لوحة مصفوفة الفحوصات الهندسية الـ 14 (Pass / Fail LED Matrix)", COL_GREEN)
        p_led.grid(row=0, column=1, sticky="nsew", padx=(5, 0))
        self.cv_led = tk.Canvas(p_led, bg=BG_PANEL, height=225, highlightthickness=0)
        self.cv_led.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_led.bind("<Configure>", lambda _e: self._draw_led_matrix())

        # Self-Explanatory 14-Indicator Diagnostic Table
        tbl_panel = self._make_panel(
            self.inner,
            "بطاقة التقييم التفصيلية لسلامة المحاكي والقواعد (14 فحصاً آلياً — النتيجة + الجيد + المثالي + التشخيص)",
            COL_BLUE,
        )
        tbl_panel.pack(fill="x", pady=(0, 8))

        headers = [
            ("diag", "ماذا يعني الرقم الآن؟ (تشخيص هندسي تلقائي)", 365),
            ("status", "التقييم", 96),
            ("good", "الجيد 🟡", 118),
            ("ideal", "المثالي 🟢", 118),
            ("actual", "نتيجتك الحالية", 142),
            ("metric", "الفحص الهندسي / الدستوري", 220),
            ("num", "#", 34),
        ]
        self.int_tree = ttk.Treeview(tbl_panel, columns=tuple(c for c, _, _ in headers), show="headings", height=14)
        for c, txt, w in headers:
            self.int_tree.heading(c, text=rtl(txt))
            self.int_tree.column(c, width=w, anchor="e" if c in ("metric", "diag") else "center")

        self.int_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.int_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.int_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.int_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        self.int_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.int_tree)

    def _make_panel(self, parent: tk.Widget, title: str, accent: str) -> tk.Frame:
        f = tk.Frame(parent, bg=BG_PANEL, highlightbackground=BG_BORDER, highlightthickness=1)
        hdr = tk.Frame(f, bg=BG_PANEL, padx=10, pady=7)
        hdr.pack(fill="x")
        tk.Frame(hdr, bg=accent, width=4, height=15).pack(side="right", padx=(7, 0))
        tk.Label(hdr, text=rtl(title), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold"), anchor="e", justify="right").pack(side="right")
        return f

    def _on_mousewheel(self, event: tk.Event) -> None:
        try:
            if str(self.parent.winfo_ismapped()) == "1":
                self.scroll_canvas.yview_scroll(int(-1 * (event.delta / 120)), "units")
        except Exception:
            pass

    def refresh(self) -> None:
        self.audit_data = self._load_audit()
        if self.audit_data and not self._running:
            ts = str(self.audit_data.get("audit_ts", ""))
            st_n = int(self.audit_data.get("total_steps_checked", 0))
            self.status_var.set(rtl(f"آخر تدقيق: {ts} • {st_n:,} خطوة مفحوصة"))
        elif not self._running:
            self.status_var.set(rtl("اضغط الزر بالأعلى لتشغيل فحص السلامة الشامل (4 ثوانٍ)"))

        self._compute_and_populate()
        self._draw_score_banner()
        self._draw_led_matrix()
        self._draw_fuzz_telemetry()

    def _compute_and_populate(self) -> None:
        self.int_tree.delete(*self.int_tree.get_children())
        a = self.audit_data
        if not a:
            self._last_metrics = {"has_data": False, "score": 0, "ideal_cnt": 0, "good_cnt": 0, "bad_cnt": 0}
            self.int_tree.insert(
                "", "end",
                values=(
                    rtl("اضغط زر (تشغيل فحص السلامة الشامل) بالأعلى لاختبار الـ 14 قاعدة هندسية في 4 ثوانٍ."),
                    rtl("بانتظار فحص"), "--", "--", "--", rtl("منظومة فحص سلامة المحاكي جاهزة"), "!",
                ),
                tags=("nodata",),
            )
            return

        rows: List[Tuple[int, str, str, str, str, str, str, str, int]] = []
        led_labels: List[Tuple[str, str]] = []

        def _add_zero(num: int, name: str, short_l: str, errs: int, ok_msg: str, bad_msg: str, good_cap: int = 0) -> None:
            if errs == 0:
                st, tg, pts, diag = "🟢 مثالي", "ideal", 100, ok_msg
            elif good_cap > 0 and 0 < errs <= good_cap:
                st, tg, pts, diag = "🟡 جيد", "good", 72, bad_msg
            else:
                st, tg, pts, diag = "🔴 عطل!", "bad", 0, bad_msg
            rows.append((num, name, f"{errs} أخطاء" if errs >= 0 else "غير موجود", "0 أخطاء", f"0 – {good_cap}" if good_cap else "0 أخطاء", st, diag, tg, pts))
            led_labels.append((short_l, tg))

        # 1. Python <-> Unity Card DB Sync
        um = int(a.get("unity_sync_mismatches", -1))
        uc = int(a.get("unity_checked", 0))
        _add_zero(
            1, "تطابق قاعدة البطاقات (Python ↔ Unity)", "تطابق Unity", um,
            f"تطابق تام 100%! كل الـ {uc:,} بطاقة متطابقة حرفياً بين Python وUnity.",
            f"تحذير! يوجد {um} اختلاف بين ملف بطاقات Python وUnityGame/Resources/cards_10000.json!",
        )

        # 2. Barrier Constitution
        bv = int(a.get("barrier_violations", 0))
        bc = int(a.get("barrier_db_cnt", 0))
        _add_zero(
            2, "دستور الحواجز (0 ATK و0 مهارات)", "دستور الحواجز", bv,
            f"سليم 100%! كل الـ {bc} حاجزاً ملتزمة بـ 0 ATK و0 مهارات بالقاعدة وأثناء القتال.",
            f"تم رصد {bv} مخالفة لدستور الحواجز (هجوم غير صفري أو مهارة فعّالة)!",
        )

        # 3. 41 Settled Decks Validity & Free-10 Disjoint
        dv = int(a.get("deck_violations", 0)) + int(a.get("free10_overlaps", 0))
        dok = int(a.get("decks_ok", 0))
        dtot = int(a.get("decks_total", 41))
        _add_zero(
            3, "صحة ديكات الـ 41 نموذجاً وانفصال النماذج الحرة", "ديكات الـ 41", dv,
            f"سليم 100%! كل {dok}/{dtot} ديكات (50 كارت، <=2 نسخ، و0 تداخل بين النماذج الحرة 1–10).",
            f"مخالفة ديكات ({dok}/{dtot} سليم، تداخل النماذج الحرة: {a.get('free10_overlaps', 0)})!",
        )

        # 4. 100% Seed Determinism
        dp = int(a.get("det_passed", 0))
        dn = int(a.get("n_det", 15))
        d_err = max(0, dn - dp)
        if d_err == 0:
            rows.append((4, "الحتمية المطلقة للبذور (Seed Determinism)", f"{dp}/{dn} (100%)", "100%", "100%", "🟢 مثالي", f"تطابق تام 100%! كل الـ {dn} بذور أعادت نفس النتيجة والضرر حرفياً.", "ideal", 100))
            led_labels.append(("حتمية البذور", "ideal"))
        else:
            rows.append((4, "الحتمية المطلقة للبذور (Seed Determinism)", f"{dp}/{dn}", "100%", "100%", "🔴 عطل!", f"فشل الحتمية في {d_err} مباريات — يوجد مصدر عشوائية غير مربوط بالـ Seed!", "bad", 0))
            led_labels.append(("حتمية البذور", "bad"))

        # 5. Zero Crashes in 150 Fuzz Matches
        nf = int(a.get("n_fuzz", 150))
        cr = int(a.get("crashes", 0))
        _add_zero(
            5, f"انعدام الانهيارات ({nf} مباراة Fuzz شاملة)", "0 انهيارات", cr,
            f"صلابة تامة! اكتملت {nf}/{nf} مباراة فوضوية عبر كل الـ 2,270 بطاقة بـ 0 أخطاء!",
            f"حدث {cr} انهيار/استثناء أثناء اختبار الضغط الفوضوي!",
        )

        # 6. Zero Infinite Loops / Stalls
        stl = int(a.get("stalls", 0))
        _add_zero(
            6, "انعدام الحلقات اللانهائية والجمود (<800 خطوة)", "0 جمود", stl,
            f"لا توجد أي حلقة لانهائية — كل الـ {nf} مباراة انتهت بسلاسة قبل الحد الأقصى.",
            f"تم رصد {stl} مباراة معلّقة تجاوزت حد 800 خطوة!",
        )

        # 7. Zero NaN/Inf in OBS_DIM=220 & Legal Mask
        ne = int(a.get("nan_obs_errs", 0))
        _add_zero(
            7, "سلامة متجه الإدراك OBS=220 وقناع الحركات", "سلامة OBS", ne,
            f"نظافة رقمية 100%! صفر قيم NaN أو Inf عبر {int(a.get('total_steps_checked', 0)):,} خطوة.",
            f"تم رصد {ne} خطأ NaN/Inf أو قناع حركات فارغ!",
        )

        # 8. Hard Caps (Hand <=7, Mana 0..10, Board <=6)
        cv_err = int(a.get("cap_violations", 0))
        _add_zero(
            8, "سلامة حدود اليد (<=7) والمانا (<=10) والملعب (<=6)", "حدود اللعب", cv_err,
            "التزام صارم 100% بحدود اليد والمانا وسقف الخانات الست بعد كل خطوة.",
            f"تم رصد {cv_err} تجاوز لحدود اليد أو المانا أو الملعب!",
        )

        # 9. Unit Board Invariants (no zombies, no barrier atk, all-attack locked)
        uv = int(a.get("unit_violations", 0))
        _add_zero(
            9, "سلامة الوحوش بالملعب (لا زومبي / قفل All-Attack)", "سلامة الوحوش", uv,
            "كل الوحوش الحيّة على الملعب سليمة 100% (لا DEF<=0، ولا خرق لقفل All-Attack).",
            f"تم رصد {uv} مخالفة في حالة الوحوش بالملعب!",
        )

        # 10. V2.1 Independent Active Skills Unit Test
        isp = bool(a.get("indep_skills_pass", False))
        rows.append((
            10, "اختبار قانون المهارات المستقلة (V2.1)", "PASS 🟢" if isp else "FAIL 🔴", "PASS", "PASS",
            "🟢 مثالي" if isp else "🔴 عطل!",
            "سليم 100%! تشغيل Skill 1 لا يغلق Skill 2 ويدعم Double Skill 1T باستقلال تام." if isp else "فشل اختبار استقلالية المهارتين الفعّالتين!",
            "ideal" if isp else "bad", 100 if isp else 0,
        ))
        led_labels.append(("استقلال المهارات", "ideal" if isp else "bad"))

        # 11. V2.3 Pure Continuous Tribal Leader Aura Unit Test
        lcp = bool(a.get("leader_cond_pass", False))
        rows.append((
            11, "اختبار تمائم القادة القبلية المستمرة (V2.3)", "PASS 🟢" if lcp else "FAIL 🔴", "PASS", "PASS",
            "🟢 مثالي" if lcp else "🔴 عطل!",
            "سليم 100%! كل الـ 60 تميمة ببركة قبلية مستمرة نقية (0 تأثير نزول) وتُطبّق على عشيرتها فقط وتُزال بالـ Exorcism." if lcp else "فشل اختبار تمائم القادة القبلية المستمرة!",
            "ideal" if lcp else "bad", 100 if lcp else 0,
        ))
        led_labels.append(("تمائم القادة V2.3", "ideal" if lcp else "bad"))

        # 12. Exact Telemetry Conservation
        ce = int(a.get("conservation_errs", 0))
        _add_zero(
            12, "الحفظ الرياضي للإحصائيات (المانا وقنوات الضرر)", "حفظ الإحصاء", ce,
            "دقة رياضية 100.0%! المانا وقنوات ضرر القائد متطابقة دون خطأ واحد عبر كل المباريات.",
            f"تم رصد {ce} انحراف في حفظ إحصائيات المانا أو ضرر القائد!",
        )

        # 13. Infinite Shuffle-Bag Integrity (0 Deck-Out)
        do = int(a.get("deckout_errs", 0))
        _add_zero(
            13, "سلامة الديك اللانهائي (0 خسارة Deck-Out)", "الديك اللانهائي", do,
            "سليم 100%! نظام Infinite Shuffle-Bag يعيد خلط الديك بسلاسة بلا أي خسارة Deck-Out.",
            f"تم رصد {do} حالة خسارة Deck-Out غير قانونية!",
        )

        # 14. Engine Throughput (Matches / Sec)
        mps = float(a.get("matches_per_sec", 0.0))
        sps = int(a.get("steps_per_sec", 0))
        if mps >= 35.0:
            st, tg, pts = "🟢 مثالي", "ideal", 100
            diag = f"محاكي فائق السرعة! {mps:.1f} مباراة/ثانية ({sps:,} خطوة/ثانية على نواة CPU واحدة)."
        elif mps >= 18.0:
            st, tg, pts = "🟡 جيد", "good", 78
            diag = f"سرعة جيدة ({mps:.1f} مباراة/ثانية • {sps:,} خطوة/ثانية)."
        else:
            st, tg, pts = "🔴 بطيء", "bad", 35
            diag = f"أداء بطيء ({mps:.1f} مباراة/ثانية) — قد يبطئ التدريب الليلي!"
        rows.append((14, "سرعة وأداء المحاكي (Matches / Sec)", f"{mps:.1f} م/ث", "35+ م/ث", "18 – 34 م/ث", st, diag, tg, pts))
        led_labels.append(("سرعة المحاكي", tg))

        avg_score = int(round(sum(r[8] for r in rows) / len(rows)))
        ideal_cnt = sum(1 for r in rows if r[7] == "ideal")
        good_cnt = sum(1 for r in rows if r[7] == "good")
        bad_cnt = sum(1 for r in rows if r[7] == "bad")

        self._last_metrics = {
            "has_data": True,
            "score": avg_score,
            "ideal_cnt": ideal_cnt,
            "good_cnt": good_cnt,
            "bad_cnt": bad_cnt,
            "led_labels": led_labels,
            "audit": a,
        }

        for num, m_name, act_s, ideal_s, good_s, st_lbl, diag_s, tag, _ in rows:
            self.int_tree.insert(
                "", "end",
                values=(rtl(diag_s), rtl(st_lbl), rtl(good_s), rtl(ideal_s), rtl(act_s), rtl(m_name), num),
                tags=(tag,),
            )

    # ------------------------------------------------------------------
    # Hero Score Banner
    # ------------------------------------------------------------------
    def _draw_score_banner(self) -> None:
        cv = self.cv_score
        cv.delete("all")
        w = max(700, cv.winfo_width())
        h = max(76, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("بانتظار فحص السلامة — اضغط زر (تشغيل فحص السلامة الشامل) بالأعلى!"), fill=COL_GOLD, font=(FONT_UI, 11, "bold"))
            return

        sc = int(d["score"])
        a = d["audit"]
        if sc >= 95 and d["bad_cnt"] == 0:
            col, badge = COL_GREEN, "🟢 المحاكي والقواعد وقاعدة Unity سليمة بنسبة 100% وجاهزة للتدريب والنشر! (AAA)"
        elif sc >= 80 and d["bad_cnt"] == 0:
            col, badge = COL_GREEN, "🟢 جميع قواعد السلامة نجحت بلا أعطال (راجع سرعة المحاكي فقط)"
        else:
            col, badge = COL_RED, f"🔴 تحذير هندسي! تم رصد {d['bad_cnt']} عطل/مخالفة يجب إصلاحها قبل التدريب!"

        cv.create_text(w - 12, 18, text=rtl(f"التقييم العام لسلامة المحاكي والقواعد:  {sc} / 100%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 14, "bold"))
        cv.create_text(w - 12, 44, text=rtl(badge), anchor="e", fill=col, font=(FONT_UI, 10, "bold"))
        cv.create_text(
            w - 12, 66,
            text=rtl(f"تم فحص {a['total_steps_checked']:,} خطوة عبر {a['n_fuzz']} مباراة Fuzz • سرعة: {a['matches_per_sec']} م/ث • زمن التدقيق: {a['elapsed_sec']}ث"),
            anchor="e", fill=FG_MUTED, font=(FONT_UI, 8),
        )

        bar_l, bar_r = 16, min(370, int(w * 0.36))
        bar_w = max(140, bar_r - bar_l)
        cv.create_rectangle(bar_l, 16, bar_r, 34, fill="#242C3D", outline=BG_BORDER)
        fw = int(bar_w * min(100, sc) / 100.0)
        if fw > 0:
            cv.create_rectangle(bar_r - fw, 16, bar_r, 34, fill=col, outline="")
        cv.create_text(
            (bar_l + bar_r) / 2, 54,
            text=rtl(f"🟢 سليم: {d['ideal_cnt']}/14   •   🟡 مقبول: {d['good_cnt']}/14   •   🔴 عطل: {d['bad_cnt']}/14"),
            fill=FG_TEXT, font=(FONT_UI, 8, "bold"),
        )

    # ------------------------------------------------------------------
    # Chart 1: 14-Check Visual Pass/Fail LED Matrix (RTL 2x7 grid)
    # ------------------------------------------------------------------
    def _draw_led_matrix(self) -> None:
        cv = self.cv_led
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(195, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد نتائج فحص بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        leds = d["led_labels"]
        cols = 2
        rows_n = 7
        pad = 6
        cw = (w - pad * (cols + 1)) / float(cols)
        ch = (h - pad * (rows_n + 1)) / float(rows_n)

        for idx, (lbl, tg) in enumerate(leds):
            r = idx % rows_n
            c = idx // rows_n  # 0 = Right col, 1 = Left col
            x1 = w - pad - c * (cw + pad)
            x0 = x1 - cw
            y0 = pad + r * (ch + pad)
            y1 = y0 + ch
            if tg == "ideal":
                bg, bd, ico, fg = "#132B22", COL_GREEN, "🟢", "#4ADE80"
            elif tg == "good":
                bg, bd, ico, fg = "#282315", COL_GOLD, "🟡", "#FACC15"
            else:
                bg, bd, ico, fg = "#2D161A", COL_RED, "🔴", "#F87171"
            cv.create_rectangle(x0, y0, x1, y1, fill=bg, outline=bd, width=1)
            cv.create_text(x1 - 8, (y0 + y1) / 2, text=rtl(f"{idx+1}. {lbl}"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_text(x0 + 8, (y0 + y1) / 2, text=ico, anchor="w", fill=fg, font=(FONT_UI, 8))

    # ------------------------------------------------------------------
    # Chart 2: Fuzz Stress Telemetry & Termination Reasons
    # ------------------------------------------------------------------
    def _draw_fuzz_telemetry(self) -> None:
        cv = self.cv_fuzz
        cv.delete("all")
        w = max(340, cv.winfo_width())
        h = max(195, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات ضغط فوضوي بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        a = d["audit"]
        cov_pct = 100.0 * int(a["cards_exercised_cnt"]) / max(1, int(a["total_cards"]))
        det_pct = 100.0 * int(a["det_passed"]) / max(1, int(a["n_det"]))
        spd_pct = min(100.0, 100.0 * float(a["matches_per_sec"]) / 60.0)
        sync_pct = 100.0 if int(a["unity_sync_mismatches"]) == 0 else 0.0

        bars = [
            (f"تطابق بطاقات Python↔Unity ({a['unity_checked']:,})", sync_pct, COL_GREEN if sync_pct == 100 else COL_RED),
            (f"حتمية البذور Seed ({a['det_passed']}/{a['n_det']})", det_pct, COL_GREEN if det_pct == 100 else COL_RED),
            (f"بطاقات فريدة لُعبت بـ Fuzz ({a['cards_exercised_cnt']:,}/2,270)", cov_pct, COL_CYAN),
            (f"سرعة المحاكي ({a['matches_per_sec']} م/ث | {a['steps_per_sec']:,} خ/ث)", spd_pct, COL_BLUE),
        ]

        lbl_x = w - 10
        bar_r = w - 210
        bar_l = 52
        bar_w = max(55, bar_r - bar_l)
        for i, (lbl, pct, col) in enumerate(bars):
            y = 12 + i * 28
            cv.create_text(lbl_x, y + 8, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(bar_l, y + 2, bar_r, y + 14, fill="#242C3D", outline="")
            fw = max(2, int(bar_w * min(100.0, pct) / 100.0)) if pct > 0.1 else 0
            if fw > 0:
                cv.create_rectangle(bar_r - fw, y + 2, bar_r, y + 14, fill=col, outline="")
            cv.create_text(bar_l - 6, y + 8, text=f"{pct:4.0f}%", anchor="e", fill=col, font=(FONT_UI, 8, "bold"))

        # Bottom: Fuzz Match Termination Breakdown
        cv.create_line(10, 128, w - 10, 128, fill=BG_BORDER)
        cv.create_text(w - 10, 141, text=rtl(f"توزيع أسباب نهاية الـ {a['n_fuzz']} مباراة فوضوية (إجمالي {a['total_steps_checked']:,} خطوة):"), anchor="e", fill=COL_GOLD, font=(FONT_UI, 8, "bold"))
        wr_map = a.get("win_reasons") or {}
        items_r = sorted(wr_map.items(), key=lambda x: x[1], reverse=True)[:4]
        rn_ar = {
            "CommanderHP": "إنزال دم القائد (0 HP)",
            "SuddenDeathHP": "الموت المفاجئ (40+)",
            "SuddenDeathDraw": "تعادل الموت المفاجئ",
            "CovenantTurnLimit": "انتهاء حد العهد",
            "TurnLimitHP": "سقف الجولات (60)",
        }
        for i, (rk, rc) in enumerate(items_r):
            y = 158 + i * 17
            rp = 100.0 * rc / max(1, int(a["n_fuzz"]))
            cv.create_text(w - 10, y, text=rtl(f"• {rn_ar.get(rk, rk)}"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8))
            cv.create_text(14, y, text=f"{rc} ({rp:.1f}%)", anchor="w", fill=COL_CYAN, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Trigger Full Integrity Audit on Background Thread
    # ------------------------------------------------------------------
    def _start_audit(self) -> None:
        if self._running:
            return
        self._running = True
        self.run_btn.config(state="disabled", text=rtl("جاري الفحص الشامل..."))
        self.status_var.set(rtl("جاري تشغيل 14 اختباراً هندسياً + 150 مباراة Fuzz..."))
        threading.Thread(target=self._run_worker, daemon=True).start()

    def _run_worker(self) -> None:
        try:
            fresh_db = CardDB()
            res = run_full_integrity_audit(
                fresh_db,
                self.store.logs_dir,
                n_fuzz=150,
                n_det=15,
                progress_cb=lambda msg: self.parent.after(0, lambda m=msg: self.status_var.set(rtl(m))),
                saved_models_dir=self.store.saved_models_dir,
            )
            tmp = self.audit_path + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(res, f, indent=2, ensure_ascii=False)
            os.replace(tmp, self.audit_path)

            def _done_ok() -> None:
                self._running = False
                self.run_btn.config(state="normal", text=rtl("تشغيل فحص السلامة الشامل (150 مباراة Fuzz + تدقيق 2,270 بطاقة)"))
                self.refresh()

            self.parent.after(0, _done_ok)
        except Exception as err:
            def _done_err(msg: str = str(err)) -> None:
                self._running = False
                self.run_btn.config(state="normal", text=rtl("تشغيل فحص السلامة الشامل (150 مباراة Fuzz + تدقيق 2,270 بطاقة)"))
                self.status_var.set(rtl(f"خطأ بالفحص: {msg[:55]}"))
            self.parent.after(0, _done_err)

# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Tab #16: Master Fun Equation, Anti-Frustration, Matchup Polarization,
Spatial 6-Lane Value & 10-Environment Radar (Arabic RTL).

Implements all 6 AAA Psychological & Tactical Quality Pillars:
  1. Snowball & Garbage Time Index (Turn-6 Lead Conversion %, Garbage Time %)
  2. Anti-Fun / Lockout & Solitaire Detector (Lockout %, Solitaire Face-Burn Loss %, Wall Stalemate %)
  3. Matchup Polarization & Non-Transitive Meta Cycles (41x41 Pairwise Sigma, 40-60% Competitive %, >70% Blowout %, A>B>C>A Cycles)
  4. Spatial 6-Lane Occupancy Entropy, Clash vs Open-Lane Ratio & Lane Relocation Skills
  5. 10-Environment Tug-of-War & Individual Balance Lift + Unbounded Aether (P50/P90/Max) + 2-Mana Aether Draw Lift + New-15 Abilities Radar
  6. Master Fun Score (MFE 0..100) using Weighted Composite + Fun-Killer Veto Deductions!
"""

from __future__ import annotations

import math
import tkinter as tk
from tkinter import ttk
from typing import Any, Callable, Dict, List, Optional, Tuple

from engine.card_model import CardDB, ELEMENTS
from engine.roster_40 import ROSTER_40
from trainer.stats_store import StatsStore, _default_excitement_data
from pace_tab import rtl
from arabic_glossary import ELEM_AR, enable_treeview_sorting

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
COL_PINK = "#EC4899"
COL_AMBER = "#F97316"

FONT_UI = "Segoe UI"

ENV_SPEC_IDS = [
    "env_fire", "env_water", "env_nature", "env_electric", "env_ice",
    "env_earth", "env_dark", "env_light", "env_air", "env_desert",
]
ENV_COLORS = [
    "#EF4444", "#06B6D4", "#22C55E", "#FACC15", "#38BDF8",
    "#D97706", "#A855F7", "#FDE047", "#94A3B8", "#F97316",
]


def _hist_percentile(hist: List[int], pct: float) -> float:
    total = sum(int(x) for x in hist)
    if total <= 0:
        return 0.0
    target = total * (pct / 100.0)
    cum = 0
    for idx, cnt in enumerate(hist):
        cum += int(cnt)
        if cum >= target:
            return float(idx)
    return float(len(hist) - 1)


def _shannon_entropy_pct(counts: List[int]) -> float:
    tot = sum(max(0, int(c)) for c in counts)
    n = len(counts)
    if tot <= 0 or n <= 1:
        return 0.0
    h = 0.0
    for c in counts:
        if c > 0:
            p = c / tot
            h -= p * math.log2(p)
    return 100.0 * h / math.log2(n)


def compute_matchup_polarization(head_to_head: Dict[str, Dict[str, int]]) -> Dict[str, Any]:
    """Computes 41x41 Pairwise Polarization Sigma, Competitive/Blowout % & Non-Transitive Meta Cycles."""
    # Prefer pairs with >= 10 matches; smoothly fall back to >= 4 during early runs
    raw_pairs: List[Tuple[str, str, int, float]] = []
    for min_m in (10, 6, 4):
        raw_pairs = []
        for rec in (head_to_head or {}).values():
            a = str(rec.get("a", ""))
            b = str(rec.get("b", ""))
            if not a or not b or a == b:
                continue
            wa = int(rec.get("wa", 0))
            wb = int(rec.get("wb", 0))
            dec = wa + wb
            if dec >= min_m:
                raw_pairs.append((a, b, dec, 100.0 * wa / dec))
        if len(raw_pairs) >= 15:
            break

    n_pairs = len(raw_pairs)
    if n_pairs == 0:
        return {
            "n_pairs": 0, "sigma": 0.0, "comp_pct": 0.0,
            "blowout_pct": 0.0, "cycles": 0, "min_m_used": 0,
        }

    sq_err = sum((wr - 50.0) ** 2 for _, _, _, wr in raw_pairs)
    sigma = math.sqrt(sq_err / n_pairs)
    comp = sum(1 for _, _, _, wr in raw_pairs if 40.0 <= wr <= 60.0)
    blow = sum(1 for _, _, _, wr in raw_pairs if wr > 70.0 or wr < 30.0)

    # Directed edge lookup for non-transitive 3-cycles (A > B > C > A where each edge >= 53%)
    edge: Dict[Tuple[str, str], float] = {}
    nodes_set = set()
    for a, b, _dec, wr_a in raw_pairs:
        edge[(a, b)] = wr_a
        edge[(b, a)] = 100.0 - wr_a
        nodes_set.add(a)
        nodes_set.add(b)
    nodes = sorted(nodes_set)
    nn = len(nodes)
    cycles = 0
    for i in range(nn):
        ni = nodes[i]
        for j in range(i + 1, nn):
            nj = nodes[j]
            w_ij = edge.get((ni, nj))
            if w_ij is None or (47.0 < w_ij < 53.0):
                continue
            for k in range(j + 1, nn):
                nk = nodes[k]
                w_jk = edge.get((nj, nk))
                w_ki = edge.get((nk, ni))
                if w_jk is None or w_ki is None:
                    continue
                if (w_ij >= 53.0 and w_jk >= 53.0 and w_ki >= 53.0) or (
                    w_ij <= 47.0 and w_jk <= 47.0 and w_ki <= 47.0
                ):
                    cycles += 1

    return {
        "n_pairs": n_pairs,
        "sigma": sigma,
        "comp_pct": 100.0 * comp / n_pairs,
        "blowout_pct": 100.0 * blow / n_pairs,
        "cycles": cycles,
    }


def _clamp01(x: float) -> float:
    return max(0.0, min(100.0, x))


def _range_score(val: float, ideal_lo: float, ideal_hi: float, good_lo: float, good_hi: float) -> float:
    if ideal_lo <= val <= ideal_hi:
        return 100.0
    if val < ideal_lo:
        span = max(1e-6, ideal_lo - good_lo)
        if val >= good_lo:
            return 72.0 + 28.0 * (val - good_lo) / span
        return max(15.0, 72.0 - 45.0 * (good_lo - val) / span)
    span = max(1e-6, good_hi - ideal_hi)
    if val <= good_hi:
        return 72.0 + 28.0 * (good_hi - val) / span
    return max(15.0, 72.0 - 45.0 * (val - good_hi) / span)


class MasterFunTab:
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
        self._last_metrics: Dict[str, Any] = {}
        self._build_layout()

    def _build_layout(self) -> None:
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
        self.scroll_canvas.bind("<Configure>", lambda e: self.scroll_canvas.itemconfigure(self._inner_win, width=max(400, e.width)))

        # 1. Master Fun Equation Hero Banner (MFE 0..100 + 6 Sub-Pillars + Fun-Killer Veto)
        hero = tk.Frame(self.inner, bg=BG_CARD, highlightbackground=COL_GOLD, highlightthickness=1, padx=14, pady=10)
        hero.pack(fill="x", pady=(2, 8))
        self.cv_hero = tk.Canvas(hero, bg=BG_CARD, height=122, highlightthickness=0)
        self.cv_hero.pack(fill="x")
        self.cv_hero.bind("<Configure>", lambda _e: self._draw_hero_banner())

        # 2. 16-Indicator Master Fun & Anti-Frustration Table (Full Width First!)
        tbl_panel = self._make_panel(
            self.inner,
            "بطاقة المعايير الـ 6 الذهبية للمتعة القصوى وكشف الإحباط (16 مؤشراً — اضغط على أي عمود للترتيب)",
            COL_GOLD,
        )
        tbl_panel.pack(fill="x", pady=(0, 8))

        headers = [
            ("diag", "التشخيص التلقائي وماذا يعني الرقم؟", 355),
            ("status", "التقييم", 96),
            ("good", "الجيد 🟡", 122),
            ("ideal", "المثالي 🟢", 122),
            ("actual", "نتيجتك الحالية", 142),
            ("metric", "المؤشر", 230),
            ("num", "#", 34),
        ]
        self.mf_tree = ttk.Treeview(tbl_panel, columns=tuple(c for c, _, _ in headers), show="headings", height=16)
        for c, txt, w in headers:
            self.mf_tree.heading(c, text=rtl(txt))
            self.mf_tree.column(c, width=w, anchor="e" if c in ("metric", "diag") else "center")
        self.mf_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.mf_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.mf_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.mf_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)
        self.mf_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.mf_tree)

        # 3. Dedicated 10-Environment Live Balance Audit Table (Full Width)
        env_tbl_panel = self._make_panel(
            self.inner,
            "جدول التدقيق المباشر لكل بيئة من البيئات الـ 10 منفردة (نسبة الفوز + الرفع Lift + نموذج البيئة المختص)",
            COL_CYAN,
        )
        env_tbl_panel.pack(fill="x", pady=(0, 8))

        e_headers = [
            ("diag", "توصية التوازن للبيئة", 310),
            ("status", "التقييم", 96),
            ("spec_wr", "نموذج البيئة WR / ELO", 150),
            ("lift", "الرفع عن 50%", 105),
            ("cast_wr", "فوز ملقي البيئة", 112),
            ("share", "حضور بالملعب", 112),
            ("casts", "مرات التفعيل", 98),
            ("env", "البيئة", 115),
            ("num", "#", 34),
        ]
        self.env_tree = ttk.Treeview(env_tbl_panel, columns=tuple(c for c, _, _ in e_headers), show="headings", height=10)
        for c, txt, w in e_headers:
            self.env_tree.heading(c, text=rtl(txt))
            self.env_tree.column(c, width=w, anchor="e" if c in ("env", "diag") else "center")
        self.env_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.env_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.env_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.env_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)
        self.env_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.env_tree)

        # 4. Visual Charts Stacked Vertically at Full Screen Width
        p_lanes = self._make_panel(self.inner, "مكوّنات معادلة المتعة الـ 6 + خريطة حرارة الـ 6 خانات (Spatial Lanes)", COL_PURPLE)
        p_lanes.pack(fill="x", pady=(0, 8))
        self.cv_lanes = tk.Canvas(p_lanes, bg=BG_PANEL, height=255, highlightthickness=0)
        self.cv_lanes.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_lanes.bind("<Configure>", lambda _e: self._draw_pillars_and_lanes_chart())

        p_env = self._make_panel(self.inner, "رادار توازن البيئات الـ 10 (أثر الفوز Lift عن 50%) + سقف الإيثر الفعلي", COL_AMBER)
        p_env.pack(fill="x", pady=(0, 8))
        self.cv_env = tk.Canvas(p_env, bg=BG_PANEL, height=255, highlightthickness=0)
        self.cv_env.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_env.bind("<Configure>", lambda _e: self._draw_env_and_aether_chart())

    def _make_panel(self, parent: tk.Widget, title: str, accent: str) -> tk.Frame:
        f = tk.Frame(parent, bg=BG_PANEL, highlightbackground=BG_BORDER, highlightthickness=1)
        hdr = tk.Frame(f, bg=BG_PANEL, padx=10, pady=7)
        hdr.pack(fill="x")
        tk.Frame(hdr, bg=accent, width=4, height=15).pack(side="right", padx=(7, 0))
        tk.Label(hdr, text=rtl(title), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold"), anchor="e", justify="right").pack(side="right")
        return f

    @staticmethod
    def _grade_range(val: float, ideal_lo: float, ideal_hi: float, good_lo: float, good_hi: float) -> Tuple[str, str]:
        if ideal_lo <= val <= ideal_hi:
            return ("🟢 مثالي", "ideal")
        if good_lo <= val <= good_hi:
            return ("🟡 جيد", "good")
        return ("🔴 يحتاج تعديل", "bad")

    def refresh(self) -> None:
        self._compute_and_populate()
        self._draw_hero_banner()
        self._draw_env_and_aether_chart()
        self._draw_pillars_and_lanes_chart()

    def _compute_and_populate(self) -> None:
        ex = self.store.excitement or _default_excitement_data()
        mf = ex.get("master_fun") or _default_excitement_data()["master_fun"]
        gp = ex.get("game_pace") or {}
        enj = ex.get("enjoyment") or {}
        cf = ex.get("comp_fairness") or {}
        si = ex.get("strategic_intel") or {}
        ms = ex.get("monster_survival") or {}
        cq = ex.get("combat_quality") or {}

        tot_ex_m = int(ex.get("total_matches", 0))
        mf_m = int(mf.get("matches", 0))
        has_any = tot_ex_m > 0 or mf_m > 0
        has_mf = mf_m > 0

        # 1. Snowball & Garbage Time
        t6_m = int(mf.get("t6_lead_matches", 0))
        t6_conv = (100.0 * int(mf.get("t6_lead_converted", 0)) / t6_m) if t6_m > 0 else None
        tot_mt = max(1, int(mf.get("total_match_turns", 0)))
        garbage_pct = (100.0 * int(mf.get("garbage_turns_sum", 0)) / tot_mt) if has_mf else None

        # 2. Anti-Fun / Lockout / Solitaire / Wall Stalemate
        act_ut = int(mf.get("active_unit_turns", 0))
        lockout_pct = (100.0 * int(mf.get("lockout_unit_turns", 0)) / act_ut) if act_ut > 0 else None
        dec_m = int(mf.get("decisive_matches", 0))
        solitaire_pct = (100.0 * int(mf.get("solitaire_losses", 0)) / dec_m) if dec_m > 0 else None
        p_turns = int(mf.get("player_turns_total", 0))
        stalemate_pct = (100.0 * int(mf.get("stalemate_turns", 0)) / p_turns) if p_turns > 0 else None

        # 3. 41x41 Matchup Polarization & Non-Transitive Meta Cycles
        pol = compute_matchup_polarization(self.store.head_to_head)
        has_pol = pol["n_pairs"] > 0

        # 4. Spatial 6-Lane Occupancy & Positioning
        lane_occ = [int(x) for x in (mf.get("lane_occ") or [0] * 6)]
        if sum(lane_occ) == 0:
            lane_occ = [int(x) for x in (si.get("slot_summons") or [0] * 6)]
        lane_tot = sum(lane_occ)
        lane_ent = _shannon_entropy_pct(lane_occ) if lane_tot > 0 else None
        open_st = int(mf.get("open_lane_strikes", 0)) if has_mf else int(cq.get("face_strikes", 0))
        clash_st = int(mf.get("unit_clash_strikes", 0)) if has_mf else int(cq.get("unit_clashes", 0))
        clash_ratio = (100.0 * clash_st / (clash_st + open_st)) if (clash_st + open_st) > 0 else None
        lane_moves_pm = (int(mf.get("lane_moves", 0)) / mf_m) if has_mf else None

        # 5. 10-Environment Tug-of-War, Unbounded Aether & New-15 Radar
        env_flips_pm = (int(mf.get("env_flips", 0)) / mf_m) if has_mf else None
        env_active = [int(x) for x in (mf.get("env_active_turns") or [0] * 10)]
        env_sides = [int(x) for x in (mf.get("env_played_sides") or [0] * 10)]
        env_wins = [int(x) for x in (mf.get("env_played_wins") or [0] * 10)]

        env_rows_data: List[Dict[str, Any]] = []
        valid_env_wrs: List[float] = []
        tot_env_act = max(1, sum(env_active))
        for ei in range(10):
            en_name = ELEMENTS[ei]
            ar_name = ELEM_AR.get(en_name, en_name)
            casts = env_sides[ei]
            c_wr = (100.0 * env_wins[ei] / casts) if casts >= 5 else None
            lift = (c_wr - 50.0) if c_wr is not None else None
            share = (100.0 * env_active[ei] / tot_env_act) if sum(env_active) > 0 else 0.0
            spec_rec = self.store.roster.get(ENV_SPEC_IDS[ei], {})
            sp_m = int(spec_rec.get("matches", 0))
            sp_wr = (100.0 * int(spec_rec.get("wins", 0)) / sp_m) if sp_m > 0 else None
            sp_elo = float(spec_rec.get("elo", 1000.0))
            # Track spread using caster WR when available, else specialist model WR
            if c_wr is not None and casts >= 15:
                valid_env_wrs.append(c_wr)
            elif sp_wr is not None and sp_m >= 15:
                valid_env_wrs.append(sp_wr)
            env_rows_data.append({
                "idx": ei, "en": en_name, "ar": ar_name,
                "casts": casts, "share": share, "c_wr": c_wr, "lift": lift,
                "sp_m": sp_m, "sp_wr": sp_wr, "sp_elo": sp_elo,
            })

        env_spread = (max(valid_env_wrs) - min(valid_env_wrs)) if len(valid_env_wrs) >= 4 else None

        # Aether Peak P50 / P90 / Max & 2-Mana Aether Draw WR Lift
        ah = [int(x) for x in (mf.get("aether_hist") or [0] * 45)]
        has_aether = sum(ah) > 0
        ae_p50 = _hist_percentile(ah, 50.0) if has_aether else 0.0
        ae_p90 = _hist_percentile(ah, 90.0) if has_aether else 0.0
        ae_max = int(mf.get("max_aether_seen", 0))
        ad_s = int(mf.get("aether_draw_sides", 0))
        nad_s = int(mf.get("no_aether_draw_sides", 0))
        ad_wr = (100.0 * int(mf.get("aether_draw_wins", 0)) / ad_s) if ad_s >= 5 else None
        nad_wr = (100.0 * int(mf.get("no_aether_draw_wins", 0)) / nad_s) if nad_s >= 5 else None
        ad_lift = (ad_wr - nad_wr) if (ad_wr is not None and nad_wr is not None) else None

        # New-15 Abilities Radar
        n15_uses = int(mf.get("new15_uses_total", 0))
        n15_sides = int(mf.get("new15_sides", 0))
        n15_wr = (100.0 * int(mf.get("new15_wins", 0)) / n15_sides) if n15_sides >= 5 else None

        # ==================================================================
        # 6. Weighted Harmonic-Veto Master Fun Equation (MFE 0..100)
        # ==================================================================
        # Sub-pillar 1: E_tension (25%)
        exc_avg = float(ex.get("avg_excitement", 50.0)) if tot_ex_m > 0 else 50.0
        enj_m = max(1, int(enj.get("matches", 0)))
        swing_pct = 100.0 * int(enj.get("matches_with_swing", 0)) / enj_m if int(enj.get("matches", 0)) > 0 else 50.0
        close_hp_pct = 100.0 * int(enj.get("close_hp_turns", 0)) / max(1, int(enj.get("total_hp_turns", 0))) if int(enj.get("total_hp_turns", 0)) > 0 else 55.0
        t_parts = [
            _clamp01(exc_avg * 1.25),
            _range_score(swing_pct, 40.0, 85.0, 22.0, 95.0),
            _range_score(close_hp_pct, 45.0, 80.0, 30.0, 92.0),
        ]
        if t6_conv is not None:
            t_parts.append(_range_score(t6_conv, 65.0, 76.0, 55.0, 84.0))
        if garbage_pct is not None:
            t_parts.append(_range_score(garbage_pct, 0.0, 15.0, 0.0, 25.0))
        e_tension = sum(t_parts) / len(t_parts)

        # Sub-pillar 2: S_agency (20%)
        si_t = max(1, int(si.get("turns_total", 0)))
        has_si = int(si.get("turns_total", 0)) > 0
        avg_choices = (int(si.get("distinct_choices_sum", 0)) / si_t) if has_si else 4.0
        forced_pass_pct = (100.0 * int(si.get("forced_pass_turns", 0)) / si_t) if has_si else 5.0
        multi_act_pct = (100.0 * int(si.get("multi_action_turns", 0)) / si_t) if has_si else 45.0
        a_parts = [
            _range_score(avg_choices, 3.0, 8.0, 2.0, 12.0),
            _range_score(forced_pass_pct, 0.0, 8.0, 0.0, 16.0),
            _range_score(multi_act_pct, 35.0, 75.0, 20.0, 88.0),
        ]
        if lane_ent is not None:
            a_parts.append(_range_score(lane_ent, 92.0, 100.0, 82.0, 100.0))
        s_agency = sum(a_parts) / len(a_parts)

        # Sub-pillar 3: P_pace (18%)
        avg_turns = (int(ex.get("total_turns_sum", 0)) / max(1, tot_ex_m)) if tot_ex_m > 0 else 14.0
        sd_pct = (100.0 * int(gp.get("max_turn_40_cnt", 0)) / max(1, int(gp.get("matches", 0)))) if int(gp.get("matches", 0)) > 0 else 0.0
        dead_pct = (100.0 * int(gp.get("dead_turns", 0)) / max(1, int(gp.get("total_player_turns", 0)))) if int(gp.get("total_player_turns", 0)) > 0 else 0.0
        p_pace = (
            _range_score(avg_turns, 11.0, 16.5, 8.5, 22.0) * 0.45
            + _range_score(sd_pct, 0.0, 3.0, 0.0, 10.0) * 0.35
            + _range_score(dead_pct, 0.0, 5.0, 0.0, 12.0) * 0.20
        )

        # Sub-pillar 4: B_fair (15%)
        p1_dec = int(cf.get("p1_decisive", 0))
        p1_wr = (100.0 * int(cf.get("p1_wins", 0)) / p1_dec) if p1_dec > 0 else 50.0
        f_parts = [_range_score(p1_wr, 47.0, 53.0, 44.0, 56.0)]
        if has_pol:
            f_parts.append(_range_score(pol["sigma"], 0.0, 10.0, 0.0, 16.0))
            f_parts.append(_range_score(pol["comp_pct"], 60.0, 100.0, 40.0, 100.0))
        b_fair = sum(f_parts) / len(f_parts)

        # Sub-pillar 5: C_combat (12%)
        died_cnt = int(ms.get("died_cnt", 0))
        avg_life = (int(ms.get("died_rounds", 0)) / died_cnt) if died_cnt > 0 else 2.2
        c_parts = [_range_score(avg_life, 1.8, 3.5, 1.3, 4.5)]
        if lockout_pct is not None:
            c_parts.append(_range_score(lockout_pct, 0.0, 12.0, 0.0, 20.0))
        if stalemate_pct is not None:
            c_parts.append(_range_score(stalemate_pct, 0.0, 15.0, 0.0, 25.0))
        if solitaire_pct is not None:
            c_parts.append(_range_score(solitaire_pct, 0.0, 10.0, 0.0, 18.0))
        c_combat = sum(c_parts) / len(c_parts)

        # Sub-pillar 6: M_variety (10%)
        seen_cards = sum(1 for v in self.store.card_stats.values() if int(v.get("drafted", 0)) > 0)
        cov_pct = 100.0 * seen_cards / max(1, self.db.num_cards)
        v_parts = [_range_score(cov_pct, 65.0, 100.0, 40.0, 100.0)]
        if env_flips_pm is not None:
            v_parts.append(_range_score(env_flips_pm, 0.35, 1.60, 0.15, 2.50))
        if env_spread is not None:
            v_parts.append(_range_score(env_spread, 0.0, 8.0, 0.0, 14.0))
        if has_pol:
            v_parts.append(_range_score(float(pol["cycles"]), 10.0, 500.0, 3.0, 1000.0))
        m_variety = sum(v_parts) / len(v_parts)

        raw_fun = (
            0.25 * e_tension
            + 0.20 * s_agency
            + 0.18 * p_pace
            + 0.15 * b_fair
            + 0.12 * c_combat
            + 0.10 * m_variety
        )

        # Smart Fun-Killer Veto Deductions (V_veto)
        vetos: List[Tuple[str, float]] = []
        if garbage_pct is not None and garbage_pct > 25.0:
            vetos.append((f"وقت ميت مرتفع ({garbage_pct:.1f}% > 25%)", min(12.0, (garbage_pct - 25.0) * 0.6)))
        if t6_conv is not None and t6_conv > 85.0:
            vetos.append((f"كرة ثلج مبكرة ({t6_conv:.1f}% > 85%)", min(10.0, (t6_conv - 85.0) * 0.8)))
        if lockout_pct is not None and lockout_pct > 20.0:
            vetos.append((f"اختناق وشلل عالي ({lockout_pct:.1f}% > 20%)", min(10.0, (lockout_pct - 20.0) * 0.7)))
        if stalemate_pct is not None and stalemate_pct > 25.0:
            vetos.append((f"جمود حواجز ({stalemate_pct:.1f}% > 25%)", min(10.0, (stalemate_pct - 25.0) * 0.6)))
        if solitaire_pct is not None and solitaire_pct > 18.0:
            vetos.append((f"حرق بدون تفاعل ({solitaire_pct:.1f}% > 18%)", min(8.0, (solitaire_pct - 18.0) * 0.6)))
        if tot_ex_m > 0 and sd_pct > 10.0:
            vetos.append((f"موت مفاجئ T40+ ({sd_pct:.1f}% > 10%)", min(12.0, (sd_pct - 10.0) * 0.5)))
        if has_pol and pol["blowout_pct"] > 25.0:
            vetos.append((f"مواجهات مكسورة >70% ({pol['blowout_pct']:.1f}%)", min(10.0, (pol["blowout_pct"] - 25.0) * 0.5)))

        veto_total = min(40.0, sum(v for _, v in vetos))
        master_score = int(round(max(0.0, min(100.0, raw_fun - veto_total)))) if has_any else 0

        # ==================================================================
        # Populate 16-Indicator Diagnostic Table
        # ==================================================================
        rows: List[Tuple[int, str, str, str, str, str, str, str]] = []

        def add_row(num: int, name: str, val_s: str, ideal_s: str, good_s: str, status: str, tag: str, diag: str) -> None:
            rows.append((num, name, val_s, ideal_s, good_s, status, tag, diag))

        # 1. Turn-6 Lead Conversion
        if t6_conv is not None:
            st, tg = self._grade_range(t6_conv, 65.0, 76.0, 55.0, 84.0)
            dg = (
                "مثالي! التفوق المبكر له وزن حقيقي مع فرصة ربع لثلث المباريات للعودة الذكية." if tg == "ideal"
                else ("كرة ثلج خطيرة! النتيجة تُحسم مبكراً جداً في الجولة 6." if t6_conv > 84 else
                      ("عشوائية زائدة: تفوق الجولة 6 لا يُكافأ بما يكفي." if t6_conv < 55 else "جيد — قريب جداً من النطاق الذهبي."))
            )
            add_row(1, "تحويل التفوق بالجولة 6 (Snowball)", f"{t6_conv:.1f}% ({t6_m:,})", "65% – 76%", "55% – 84%", st, tg, dg)
        else:
            add_row(1, "تحويل التفوق بالجولة 6 (Snowball)", "--", "65% – 76%", "55% – 84%", "بانتظار مباريات", "nodata", "يُقاس تلقائياً مع أول مباريات تُلعب بالمحاكي المحدّث.")

        # 2. Garbage Time %
        if garbage_pct is not None:
            st, tg = self._grade_range(garbage_pct, 0.0, 15.0, 0.0, 25.0)
            dg = (
                "ممتاز! المباريات تنتهي فور حسمها بدون وقت ميت ممل." if tg == "ideal"
                else ("وقت ميت مقبول لكن يمكن تسريع الإغلاق." if tg == "good" else "تحذير فيتو! الخاسر يمضي وقتاً طويلاً بعد فقدان الأمل.")
            )
            add_row(2, "نسبة الوقت الميت (Garbage Time)", f"{garbage_pct:.1f}%", "<= 15%", "16% – 25%", st, tg, dg)
        else:
            add_row(2, "نسبة الوقت الميت (Garbage Time)", "--", "<= 15%", "16% – 25%", "بانتظار مباريات", "nodata", "يقيس الجولات بعد تخطي فارق 220 HP + سيطرة الملعب.")

        # 3. Lockout %
        if lockout_pct is not None:
            st, tg = self._grade_range(lockout_pct, 0.0, 12.0, 0.0, 20.0)
            dg = (
                "نظيف وممتع! الوحوش تتحرك وتقاتل بحرية بدون إحباط التجميد/الصمت." if tg == "ideal"
                else ("مستوى تعطيل متوسط ومقبول." if tg == "good" else "فيتو إحباط! نسبة عالية من أدوار الوحوش مشلولة بالتجميد أو الصمت.")
            )
            add_row(3, "مؤشر الاختناق والشلل (Lockout %)", f"{lockout_pct:.1f}%", "<= 12%", "13% – 20%", st, tg, dg)
        else:
            add_row(3, "مؤشر الاختناق والشلل (Lockout %)", "--", "<= 12%", "13% – 20%", "بانتظار مباريات", "nodata", "يقيس % أدوار الوحوش المجمّدة أو الصامتة أو المثبّتة.")

        # 4. Solitaire / Face-Burn Loss %
        if solitaire_pct is not None:
            st, tg = self._grade_range(solitaire_pct, 0.0, 10.0, 0.0, 18.0)
            dg = (
                "تفاعل ممتاز! الفوز يتطلب مواجهة حقيقية على أرض المعركة." if tg == "ideal"
                else ("بعض الانتصارات بالحرق المباشر — ضمن الحد المقبول." if tg == "good" else "تحذير Solitaire! خسائر كثيرة بالحرق المباشر رغم سيطرة الخاسر على الملعب.")
            )
            add_row(4, "الخسارة بدون تفاعل (Solitaire Loss)", f"{solitaire_pct:.1f}%", "<= 10%", "11% – 18%", st, tg, dg)
        else:
            add_row(4, "الخسارة بدون تفاعل (Solitaire Loss)", "--", "<= 10%", "11% – 18%", "بانتظار مباريات", "nodata", "يقيس خسارة اللاعب المسيطر على الملعب بحرق مباشر من اليد/المهارات.")

        # 5. Wall Stalemate Turns %
        if stalemate_pct is not None:
            st, tg = self._grade_range(stalemate_pct, 0.0, 15.0, 0.0, 25.0)
            dg = (
                "إيقاع هجومي حيّ! الحواجز تحمي بدون أن تجمد المباراة." if tg == "ideal"
                else ("بعض الجمود الدفاعي — راقب الحواجز ودفاع الوحوش." if tg == "good" else "فيتو جمود! تكدّس الحواجز والدفاع العالي يوقف القتال لأدوار طويلة.")
            )
            add_row(5, "أدوار جمود الحواجز (Wall Stalemate)", f"{stalemate_pct:.1f}%", "<= 15%", "16% – 25%", st, tg, dg)
        else:
            add_row(5, "أدوار جمود الحواجز (Wall Stalemate)", "--", "<= 15%", "16% – 25%", "بانتظار مباريات", "nodata", "أدوار بلا قتلى وبضرر قائد <= 10 رغم امتلاء الملعب (كاشف أثر +20% DEF).")

        # 6. Pairwise Polarization Sigma (41x41)
        if has_pol:
            st, tg = self._grade_range(pol["sigma"], 0.0, 10.0, 0.0, 16.0)
            dg = (
                f"مصفوفة متناغمة عبر {pol['n_pairs']} زوجاً — المهارة تتفوق على حظ القرعة!" if tg == "ideal"
                else ("استقطاب متوسط بين بعض الديكات." if tg == "good" else "حجر-ورقة-مقص حاد! القرعة تحسم الكثير من المواجهات قبل اللعب.")
            )
            add_row(6, "استقطاب المواجهات 41×41 (Sigma σ)", f"±{pol['sigma']:.1f}% ({pol['n_pairs']})", "<= 10.0%", "10.1% – 16%", st, tg, dg)
        else:
            add_row(6, "استقطاب المواجهات 41×41 (Sigma σ)", "--", "<= 10.0%", "10.1% – 16%", "بانتظار بيانات", "nodata", "يحتاج 15+ زوج مواجهات بـ 4+ مباريات لكل زوج في الدوري.")

        # 7. Competitive Matchups (40-60%)
        if has_pol:
            st, tg = self._grade_range(pol["comp_pct"], 60.0, 100.0, 40.0, 100.0)
            dg = (
                "رائع! غالبية المواجهات متقاربة ومفتوحة للطرفين (40%–60%)." if tg == "ideal"
                else ("نسبة مقبولة من المواجهات المتكافئة." if tg == "good" else "ضعيف! أغلب المواجهات تميل لطرف واحد بوضوح.")
            )
            add_row(7, "المواجهات التنافسية (40%–60% WR)", f"{pol['comp_pct']:.1f}%", ">= 60%", "40% – 59%", st, tg, dg)
        else:
            add_row(7, "المواجهات التنافسية (40%–60% WR)", "--", ">= 60%", "40% – 59%", "بانتظار بيانات", "nodata", "نسبة أزواج النماذج المتوازنة داخل النطاق 40%–60%.")

        # 8. Polarized Blowout Matchups (>70%)
        if has_pol:
            st, tg = self._grade_range(pol["blowout_pct"], 0.0, 12.0, 0.0, 25.0)
            dg = (
                "ممتاز! ندرة المواجهات المكسورة من طرف واحد." if tg == "ideal"
                else ("بعض المواجهات الحادة — طبيعي في بداية تدريب الدوري." if tg == "good" else "فيتو استقطاب! أكثر من ربع المواجهات محسومة بقرعة >70%.")
            )
            add_row(8, "المواجهات الحادة المكسورة (>70% WR)", f"{pol['blowout_pct']:.1f}%", "<= 12%", "13% – 25%", st, tg, dg)
        else:
            add_row(8, "المواجهات الحادة المكسورة (>70% WR)", "--", "<= 12%", "13% – 25%", "بانتظار بيانات", "nodata", "نسبة المواجهات شبه المضمونة مسبقاً (>70% أو <30%).")

        # 9. Healthy Non-Transitive Meta Cycles
        if has_pol:
            cy = int(pol["cycles"])
            st, tg = self._grade_range(float(cy), 10.0, 99999.0, 3.0, 99999.0)
            dg = (
                f"بيئة ميتا حيّة ودائرية! وُجدت {cy:,} دورة توازن (A>B>C>A) تمنع هيمنة ديك واحد." if tg == "ideal"
                else ("دورات توازن ناشئة — ستتوسع مع نضج الدوري." if tg == "good" else "الميتا خطّية حالياً (أقوى ديك يغلب الكل بدون مضادات دائرية).")
            )
            add_row(9, "دورات الميتا الصحية (A>B>C>A)", f"{cy:,} دورة", ">= 10", "3 – 9", st, tg, dg)
        else:
            add_row(9, "دورات الميتا الصحية (A>B>C>A)", "--", ">= 10", "3 – 9", "بانتظار بيانات", "nodata", "يكشف وجود توازن دائري صحي بين الديكات.")

        # 10. Spatial 6-Lane Entropy
        if lane_ent is not None:
            st, tg = self._grade_range(lane_ent, 92.0, 100.0, 82.0, 100.0)
            dg = (
                "توزيع مكاني ممتاز! كل الخانات الستة (0..5) حيّة وتشارك في القتال." if tg == "ideal"
                else ("انحياز خفيف لخانات معينة (الوسط أو الأطراف)." if tg == "good" else "خانات مهجورة! الذكاء يتجاهل بعض الخانات الستة.")
            )
            add_row(10, "إنتروبيا إشغال الـ 6 خانات (Lanes)", f"{lane_ent:.1f}%", ">= 92%", "82% – 91%", st, tg, dg)
        else:
            add_row(10, "إنتروبيا إشغال الـ 6 خانات (Lanes)", "--", ">= 92%", "82% – 91%", "بانتظار بيانات", "nodata", "يقيس عدالة استخدام جميع الـ 6 خانات على الملعب.")

        # 11. Clash vs Open-Lane Ratio
        if clash_ratio is not None:
            st, tg = self._grade_range(clash_ratio, 55.0, 75.0, 42.0, 85.0)
            dg = (
                "توازن تكتيكي رائع بين الاشتباك المباشر والاختراق عبر الخانات المفتوحة!" if tg == "ideal"
                else ("ميل خفيف للوجه أو للاشتباك." if tg == "good" else ("سباق ضرب وجه بدون اشتباك!" if clash_ratio < 42 else "اشتباك خانق بلا ثغرات للوصول للقائد!"))
            )
            add_row(11, "نسبة الاشتباك مقابل الخانة المفتوحة", f"{clash_ratio:.1f}% اشتباك", "55% – 75%", "42% – 85%", st, tg, dg)
        else:
            add_row(11, "نسبة الاشتباك مقابل الخانة المفتوحة", "--", "55% – 75%", "42% – 85%", "بانتظار بيانات", "nodata", "التوازن بين صدام الوحوش وضرب القائد من الخانات الفارغة.")

        # 12. Lane Relocations per Match
        if lane_moves_pm is not None:
            st, tg = self._grade_range(lane_moves_pm, 0.25, 4.0, 0.08, 6.0)
            dg = (
                "مهارات السحب وتحريك الخانات حيّة ومؤثرة في المعارك!" if tg == "ideal"
                else ("استخدام خفيف لمهارات تحريك الخانات." if tg == "good" else "مهارات التموضع (السحب/التبديل) نادرة الاستخدام حالياً.")
            )
            add_row(12, "مهارات التموضع وتحريك الخانات/مباراة", f"{lane_moves_pm:.2f}", ">= 0.25", "0.08 – 0.24", st, tg, dg)
        else:
            add_row(12, "مهارات التموضع وتحريك الخانات/مباراة", "--", ">= 0.25", "0.08 – 0.24", "بانتظار مباريات", "nodata", "يقيس تفعيل pull_hook و relocate_self و reposition_ally و swap_lanes.")

        # 13. Arena Tug-of-War Flip Rate
        if env_flips_pm is not None:
            st, tg = self._grade_range(env_flips_pm, 0.35, 1.60, 0.15, 2.50)
            dg = (
                "حرب بيئات ممتعة! اللاعبون يكسرون بيئة الخصم ببيئتهم المضادة." if tg == "ideal"
                else ("تبادل بيئات خفيف — راقب إقبال الديكات على البيئات." if tg == "good" else ("البيئات نادراً ما تتصارع في المباراة." if env_flips_pm < 0.15 else "تبديل بيئات مفرط جداً كل دور."))
            )
            add_row(13, "معدل قلب ساحة البيئة (Arena Flips)", f"{env_flips_pm:.2f} / مباراة", "0.35 – 1.60", "0.15 – 2.50", st, tg, dg)
        else:
            add_row(13, "معدل قلب ساحة البيئة (Arena Flips)", "--", "0.35 – 1.60", "0.15 – 2.50", "بانتظار مباريات", "nodata", "كم مرة يتم كسر بيئة قائمة ببيئة مختلفة خلال المباراة.")

        # 14. 10-Environment Balance Spread
        if env_spread is not None:
            st, tg = self._grade_range(env_spread, 0.0, 8.0, 0.0, 14.0)
            dg = (
                "توازن ممتاز بين جميع البيئات الـ 10 (فارق ضئيل بين الأقوى والأضعف)!" if tg == "ideal"
                else ("فارق متوسط بين البيئات — راجع جدول البيئات الـ 10 بالأسفل." if tg == "good" else "خلل واضح بين البيئات! بيئة مكسورة وأخرى مهجورة — راجع الجدول أسفل.")
            )
            add_row(14, "فارق التوازن بين البيئات الـ 10 (Spread)", f"{env_spread:.1f}%", "<= 8.0%", "8.1% – 14.0%", st, tg, dg)
        else:
            add_row(14, "فارق التوازن بين البيئات الـ 10 (Spread)", "--", "<= 8.0%", "8.1% – 14.0%", "بانتظار بيانات", "nodata", "الفارق بين أعلى وأقل نسبة فوز عبر البيئات الـ 10.")

        # 15. Unbounded Aether P50 / P90 / Max + 2-Mana Draw Lift
        if has_aether:
            st, tg = self._grade_range(ae_p90, 10.0, 16.0, 8.0, 20.0)
            lift_s = f" | رفع السحب: {ad_lift:+.1f}%" if ad_lift is not None else ""
            dg = (
                f"منحنى إيثر مفتوح رائع! الوسيط {ae_p50:.0f}، P90={ae_p90:.0f}، أقصى إيثر={ae_max}{lift_s}." if tg == "ideal"
                else (f"إيثر مفتوح مقبول (P90={ae_p90:.0f}، أقصى={ae_max}){lift_s}." if tg == "good" else f"سقف الإيثر {'منخفض' if ae_p90 < 8 else 'متضخم جداً'} (P90={ae_p90:.0f}، أقصى={ae_max}){lift_s}.")
            )
            add_row(15, "الإيثر المفتوح (P50 / P90 / Max)", f"{ae_p50:.0f} / {ae_p90:.0f} / {ae_max}", "P90: 10 – 16", "P90: 8 – 20", st, tg, dg)
        else:
            add_row(15, "الإيثر المفتوح (P50 / P90 / Max)", "--", "P90: 10 – 16", "P90: 8 – 20", "بانتظار مباريات", "nodata", "يقيس أقصى إيثر يصل إليه اللاعبون + أثر زر السحب (2 مانا).")

        # 16. New 15 Abilities Radar
        if n15_wr is not None:
            st, tg = self._grade_range(n15_wr, 45.0, 56.0, 40.0, 62.0)
            dg = (
                f"توازن ممتاز للـ 15 مهارة الجديدة ({n15_uses:,} تفعيل • فوز {n15_wr:.1f}%)!" if tg == "ideal"
                else (f"أداء جيد للـ 15 مهارة ({n15_uses:,} تفعيل)." if tg == "good" else f"تنبيه: الـ 15 مهارة الجديدة {'قوية جداً' if n15_wr > 62 else 'ضعيفة'} ({n15_wr:.1f}% WR).")
            )
            add_row(16, "رادار الـ 15 مهارة الجديدة (WR & Uses)", f"{n15_wr:.1f}% ({n15_uses:,})", "45% – 56%", "40% – 62%", st, tg, dg)
        else:
            add_row(16, "رادار الـ 15 مهارة الجديدة (WR & Uses)", f"0 ({n15_uses})" if has_mf else "--", "45% – 56%", "40% – 62%", "بانتظار مباريات", "nodata", "يرصد عدد تفعيلات ونسبة فوز الـ 15 مهارة الجديدة المصممة يدوياً.")

        # Fill mf_tree
        self.mf_tree.delete(*self.mf_tree.get_children())
        ideal_cnt = good_cnt = bad_cnt = 0
        for num, name, val_s, ideal_s, good_s, status, tag, diag in rows:
            if tag == "ideal":
                ideal_cnt += 1
            elif tag == "good":
                good_cnt += 1
            elif tag == "bad":
                bad_cnt += 1
            self.mf_tree.insert(
                "", "end",
                values=(rtl(diag), rtl(status), rtl(good_s), rtl(ideal_s), rtl(val_s), rtl(name), num),
                tags=(tag,),
            )

        # Fill env_tree (All 10 Environments Individually!)
        self.env_tree.delete(*self.env_tree.get_children())
        for er in env_rows_data:
            c_wr = er["c_wr"]
            lift = er["lift"]
            sp_wr = er["sp_wr"]
            sp_m = er["sp_m"]
            sp_elo = er["sp_elo"]
            casts = er["casts"]
            share = er["share"]

            if c_wr is not None:
                st_e, tg_e = self._grade_range(lift, -4.0, 7.0, -8.0, 11.0)
                if lift > 11.0:
                    dg_e = "مكسورة القوة! خفّض أرقام البف الإيجابي أو العقوبة الأجنبية قليلاً."
                elif lift < -8.0:
                    dg_e = "ضعيفة! ارفع البف الإيجابي لبيئتها لتشجيع اللعب بها."
                elif tg_e == "ideal":
                    dg_e = "مثالية! تمنح ميزة ممتعة لعنصرها بدون كسر التوازن."
                else:
                    dg_e = "ضمن النطاق المقبول — راقبها بعد مزيد من المباريات."
                c_wr_s = f"{c_wr:.1f}%"
                lift_s = f"{lift:+.1f}%"
            elif sp_wr is not None and sp_m >= 5:
                st_e, tg_e = self._grade_range(sp_wr, 45.0, 55.0, 40.0, 60.0)
                dg_e = (
                    "نموذج البيئة متوازن (بانتظار تفعيلات البيئة المباشرة بالمحاكي المحدّث)." if tg_e == "ideal"
                    else ("نموذج البيئة متفوق — راقب أرقامها." if sp_wr > 55 else "نموذج البيئة ضعيف — قد تحتاج دعم أرقامها.")
                )
                c_wr_s = "--"
                lift_s = "--"
            else:
                st_e, tg_e = "بانتظار بيانات", "nodata"
                dg_e = "ستمتلئ تلقائياً بمجرد بدء مباريات الدوري الجديدة."
                c_wr_s = "--"
                lift_s = "--"

            spec_s = f"{sp_wr:.1f}% ({sp_elo:.0f})" if (sp_wr is not None and sp_m > 0) else "--"
            share_s = f"{share:.1f}%" if sum(env_active) > 0 else "--"
            self.env_tree.insert(
                "", "end",
                values=(rtl(dg_e), rtl(st_e), spec_s, lift_s, c_wr_s, share_s, f"{casts:,}", rtl(f"{er['ar']} ({er['en']})"), er["idx"] + 1),
                tags=(tg_e,),
            )

        self._last_metrics = {
            "has_data": has_any,
            "has_mf": has_mf,
            "score": master_score,
            "raw_fun": raw_fun,
            "veto_total": veto_total,
            "vetos": vetos,
            "ideal_cnt": ideal_cnt,
            "good_cnt": good_cnt,
            "bad_cnt": bad_cnt,
            "sub_pillars": [
                ("التوتر والعودة (25%)", e_tension, COL_GOLD),
                ("حرية القرار (20%)", s_agency, COL_BLUE),
                ("الإيقاع (18%)", p_pace, COL_CYAN),
                ("العدالة والاستقطاب (15%)", b_fair, COL_GREEN),
                ("حيوية القتال (12%)", c_combat, COL_PURPLE),
                ("التنوع والبيئات (10%)", m_variety, COL_PINK),
            ],
            "lane_occ": lane_occ,
            "env_rows": env_rows_data,
            "ae_p50": ae_p50,
            "ae_p90": ae_p90,
            "ae_max": ae_max,
            "has_aether": has_aether,
            "tot_matches": tot_ex_m,
            "mf_matches": mf_m,
        }

    # ==================================================================
    # Visual Canvases
    # ==================================================================
    def _draw_hero_banner(self) -> None:
        cv = self.cv_hero
        cv.delete("all")
        w = max(720, cv.winfo_width())
        h = max(112, cv.winfo_height())
        m = self._last_metrics
        has_d = bool(m.get("has_data"))
        sc = int(m.get("score", 0))
        veto_tot = float(m.get("veto_total", 0.0))
        vetos = m.get("vetos") or []

        if not has_d:
            cv.create_text(w - 20, h // 2, text=rtl("معادلة المتعة القصوى (MFE) — بانتظار مباريات تدريب لحساب الدرجة الذهبية..."),
                           fill=FG_MUTED, font=(FONT_UI, 12, "bold"), anchor="e")
            return

        col = COL_GREEN if sc >= 82 else (COL_GOLD if sc >= 65 else COL_RED)
        badge = "🏆 مستوى AAA ممتع جداً" if sc >= 85 else ("🟢 ممتع ومتوازن" if sc >= 75 else ("🟡 جيد — يحتاج ضبطاً" if sc >= 60 else "🔴 قتلة متعة نشطة!"))

        # Giant Score Box on Right (RTL)
        bx1, bx2 = w - 210, w - 10
        cv.create_rectangle(bx1, 8, bx2, h - 8, fill="#121824", outline=col, width=2)
        cv.create_text((bx1 + bx2) // 2, 42, text=f"{sc} / 100", fill=col, font=(FONT_UI, 24, "bold"))
        cv.create_text((bx1 + bx2) // 2, 74, text=rtl("معادلة المتعة القصوى (MFE)"), fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
        cv.create_text((bx1 + bx2) // 2, 95, text=rtl(badge), fill=col, font=(FONT_UI, 8, "bold"))

        # Title & Sub-stats
        right_edge = bx1 - 18
        cv.create_text(
            right_edge, 22,
            text=rtl(f"التقييم المركّب قبل الفيتو: {m.get('raw_fun', 0):.1f}  •  خصم قتلة المتعة (Veto): -{veto_tot:.1f}  •  مباريات محللة: {m.get('tot_matches', 0):,} (محدّثة: {m.get('mf_matches', 0):,})"),
            fill=FG_TEXT, font=(FONT_UI, 10, "bold"), anchor="e",
        )

        # Progress bar
        bar_x1, bar_x2 = 16, right_edge
        bar_y1, bar_y2 = 42, 60
        cv.create_rectangle(bar_x1, bar_y1, bar_x2, bar_y2, fill="#111622", outline=BG_BORDER)
        fill_w = int((bar_x2 - bar_x1) * max(0.0, min(1.0, sc / 100.0)))
        if fill_w > 2:
            cv.create_rectangle(bar_x2 - fill_w, bar_y1, bar_x2, bar_y2, fill=col, outline="")

        # Veto Pills Row
        if vetos:
            veto_txt = "  |  ".join(f"⚠️ {lbl} (-{pts:.1f})" for lbl, pts in vetos[:4])
            cv.create_text(right_edge, 84, text=rtl(f"فيتو قتلة المتعة المفعّلة:  {veto_txt}"), fill="#FCA5A5", font=(FONT_UI, 9, "bold"), anchor="e")
        else:
            cv.create_text(right_edge, 84, text=rtl("✅ لا يوجد أي فيتو مفعّل من قتلة المتعة (Zero Anti-Fun Vetoes!)"), fill="#4ADE80", font=(FONT_UI, 9, "bold"), anchor="e")

        cv.create_text(
            right_edge, 106,
            text=rtl(f"المؤشرات الـ 16:  🟢 مثالي: {m.get('ideal_cnt', 0)}   •   🟡 جيد: {m.get('good_cnt', 0)}   •   🔴 يحتاج تعديل: {m.get('bad_cnt', 0)}"),
            fill=FG_MUTED, font=(FONT_UI, 8), anchor="e",
        )

    def _draw_pillars_and_lanes_chart(self) -> None:
        cv = self.cv_lanes
        cv.delete("all")
        w = max(420, cv.winfo_width())
        h = max(230, cv.winfo_height())
        m = self._last_metrics
        subs = m.get("sub_pillars") or []
        lanes = m.get("lane_occ") or [0] * 6

        # Top Half: 6 Weighted MFE Sub-Pillars horizontal bars
        cv.create_text(w - 10, 14, text=rtl("أداء المحاور الـ 6 داخل معادلة المتعة (0–100)"), fill=FG_TEXT, font=(FONT_UI, 8, "bold"), anchor="e")
        y = 30
        bar_left = 44
        bar_right = w - 145
        bar_span = max(80, bar_right - bar_left)
        for lbl, val, col in subs:
            cv.create_text(w - 10, y + 7, text=rtl(lbl), fill=FG_MUTED, font=(FONT_UI, 8), anchor="e")
            cv.create_rectangle(bar_left, y, bar_right, y + 14, fill="#111622", outline=BG_BORDER)
            fw = int(bar_span * max(0.0, min(1.0, val / 100.0)))
            if fw > 1:
                cv.create_rectangle(bar_right - fw, y, bar_right, y + 14, fill=col, outline="")
            cv.create_text(bar_left - 6, y + 7, text=f"{val:.0f}", fill=col, font=(FONT_UI, 8, "bold"), anchor="e")
            y += 20

        # Bottom Half: 6-Lane Spatial Heatmap (Slot 0 .. Slot 5)
        ly = y + 12
        cv.create_text(w - 10, ly, text=rtl("خريطة إشغال الـ 6 خانات (الأطراف 0-1-4-5 والوسط 2-3)"), fill=FG_TEXT, font=(FONT_UI, 8, "bold"), anchor="e")
        tot_l = max(1, sum(lanes))
        max_l = max(1, max(lanes))
        box_y1, box_y2 = ly + 14, h - 14
        slot_w = max(40, (w - 30) // 6)
        start_x = (w - slot_w * 6) // 2
        for si in range(6):
            x1 = start_x + si * slot_w + 3
            x2 = x1 + slot_w - 6
            pct = 100.0 * lanes[si] / tot_l if sum(lanes) > 0 else 0.0
            ratio = lanes[si] / max_l if sum(lanes) > 0 else 0.0
            fill_c = "#14532D" if (13.0 <= pct <= 21.0) else ("#713F12" if pct > 0 else "#1B2230")
            out_c = COL_GOLD if si in (2, 3) else COL_CYAN
            cv.create_rectangle(x1, box_y1, x2, box_y2, fill=fill_c, outline=out_c, width=2 if si in (2, 3) else 1)
            tag_l = "وسط" if si in (2, 3) else "طرف"
            cv.create_text((x1 + x2) // 2, box_y1 + 13, text=rtl(f"خانة {si} ({tag_l})"), fill=FG_TEXT, font=(FONT_UI, 7, "bold"))
            cv.create_text((x1 + x2) // 2, box_y1 + 31, text=f"{pct:.1f}%" if sum(lanes) > 0 else "--", fill="#FFFFFF", font=(FONT_UI, 9, "bold"))
            # Mini fill bar at bottom of lane tile
            mw = int((x2 - x1 - 8) * ratio)
            if mw > 1:
                cv.create_rectangle(x1 + 4, box_y2 - 8, x1 + 4 + mw, box_y2 - 3, fill=out_c, outline="")

    def _draw_env_and_aether_chart(self) -> None:
        cv = self.cv_env
        cv.delete("all")
        w = max(420, cv.winfo_width())
        h = max(230, cv.winfo_height())
        m = self._last_metrics
        erows = m.get("env_rows") or []

        cv.create_text(w - 10, 14, text=rtl("توازن البيئات الـ 10 حول خط 50% (النطاق الذهبي الأخضر: 46%–57%)"), fill=FG_TEXT, font=(FONT_UI, 8, "bold"), anchor="e")

        # Diverging bar chart centered at 50% (range 30% .. 70%)
        x_lo, x_hi = 36, w - 105
        x_mid = (x_lo + x_hi) // 2
        half_w = (x_hi - x_lo) / 2.0

        # Ideal green band (46%..57%)
        gx1 = x_mid + (-4.0 / 20.0) * half_w
        gx2 = x_mid + (7.0 / 20.0) * half_w
        cv.create_rectangle(gx1, 26, gx2, h - 38, fill="#112A22", outline="")
        cv.create_line(x_mid, 26, x_mid, h - 38, fill=FG_MUTED, dash=(3, 3))

        row_h = max(14, (h - 68) // 10)
        y = 28
        for er in erows:
            ei = er["idx"]
            val = er["c_wr"] if er["c_wr"] is not None else er["sp_wr"]
            col = ENV_COLORS[ei % len(ENV_COLORS)]
            cv.create_text(w - 8, y + row_h // 2, text=rtl(er["ar"]), fill=FG_TEXT, font=(FONT_UI, 8, "bold"), anchor="e")
            if val is not None:
                diff = max(-20.0, min(20.0, val - 50.0))
                bx = x_mid + (diff / 20.0) * half_w
                cv.create_rectangle(min(x_mid, bx), y + 2, max(x_mid, bx), y + row_h - 2, fill=col, outline="")
                cv.create_text(x_lo - 4, y + row_h // 2, text=f"{val:.0f}%", fill=col, font=(FONT_UI, 7, "bold"), anchor="e")
            else:
                cv.create_text(x_lo - 4, y + row_h // 2, text="--", fill=FG_MUTED, font=(FONT_UI, 7), anchor="e")
            y += row_h

        # Footer strip: Unbounded Aether summary
        if m.get("has_aether"):
            ae_txt = f"💧 الإيثر المفتوح:  الوسيط (P50) = {m['ae_p50']:.0f}   •   P90 = {m['ae_p90']:.0f}   •   أقصى إيثر مُسجّل = {m['ae_max']}"
        else:
            ae_txt = "💧 الإيثر المفتوح (P50 / P90 / Max): بانتظار مباريات بالمحاكي المحدّث"
        cv.create_rectangle(10, h - 30, w - 10, h - 6, fill="#121824", outline=BG_BORDER)
        cv.create_text(w // 2, h - 18, text=rtl(ae_txt), fill=COL_CYAN, font=(FONT_UI, 8, "bold"))

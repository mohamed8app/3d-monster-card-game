# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Executive Visual Analytics Dashboard Tab (Arabic RTL Edition).
Designed cleanly Right-to-Left (RTL) with crisp Segoe UI Arabic typography
and native Tkinter Canvas vector charts.
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
from typing import Any, Dict, List, Optional, Tuple

from engine.card_model import ROLES, CardDB
from engine.roster_40 import ROSTER_40
from trainer.stats_store import StatsStore, _default_excitement_data

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
COL_SLATE = "#64748B"

FONT_UI = "Segoe UI"

AR_ROLES = ["مهاجم", "قنّاص", "مدافع", "حاجز", "دعم", "معطّل"]


def rtl(text: str) -> str:
    """Wraps text with Unicode RTL Embedding so Windows Tk/Uniscribe renders Arabic + numbers cleanly RTL."""
    if not text:
        return ""
    return f"\u202B{text}\u202C"


class VisualDashboardTab:
    def __init__(self, parent: tk.Frame, db: CardDB, store: StatsStore) -> None:
        self.parent = parent
        self.db = db
        self.store = store
        self.probe_path = os.path.join(self.store.logs_dir, "live_probe_snapshot.json")
        self.probe_data: Optional[Dict[str, Any]] = self._load_probe_snapshot()
        self._probe_running = False

        # Auto-select Live Probe if it exists and main logs don't have kill-source data yet
        default_src = (
            rtl("عيّنة الفحص المباشر (قوانين V2.2 الحالية)")
            if (self.probe_data and sum(int(v) for v in self.store.excitement.get("kill_causes", {}).values()) == 0)
            else rtl("سجلات التدريب الكاملة (كل المباريات)")
        )
        self.source_var = tk.StringVar(value=default_src)
        self.deck_scope_var = tk.StringVar(value=rtl("كل النماذج (41)"))
        self.sec_per_rnd_var = tk.StringVar(value=rtl("40 ثانية / جولة (قياسي)"))
        self.probe_status_var = tk.StringVar(value="")

        self._kpi_widgets: Dict[str, Tuple[tk.Label, tk.Label, tk.Label]] = {}
        self._build_layout()

    def _load_probe_snapshot(self) -> Optional[Dict[str, Any]]:
        if os.path.isfile(self.probe_path):
            try:
                with open(self.probe_path, "r", encoding="utf-8") as f:
                    d = json.load(f)
                if isinstance(d, dict) and d.get("total_matches", 0) > 0:
                    return d
            except Exception:
                pass
        return None

    def _build_layout(self) -> None:
        # Top RTL control toolbar (packed right-to-left)
        ctrl = tk.Frame(self.parent, bg=BG_PANEL, padx=12, pady=8)
        ctrl.pack(fill="x", padx=8, pady=(6, 4))

        # Right-aligned filters (read naturally from Right -> Left)
        tk.Label(ctrl, text=rtl("مصدر البيانات:"), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold")).pack(side="right", padx=(4, 0))
        self.source_cb = ttk.Combobox(
            ctrl,
            textvariable=self.source_var,
            values=[
                rtl("سجلات التدريب الكاملة (كل المباريات)"),
                rtl("عيّنة الفحص المباشر (قوانين V2.2 الحالية)"),
            ],
            state="readonly",
            justify="right",
            width=28,
        )
        self.source_cb.pack(side="right", padx=(10, 6))
        self.source_cb.bind("<<ComboboxSelected>>", lambda _e: self.refresh())

        tk.Label(ctrl, text=rtl("نطاق الديكات:"), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold")).pack(side="right", padx=(4, 0))
        scope_cb = ttk.Combobox(
            ctrl,
            textvariable=self.deck_scope_var,
            values=[
                rtl("كل النماذج (41)"),
                rtl("نماذج الحر فقط (Free-10)"),
                rtl("نماذج الأساليب (10)"),
                rtl("نماذج البيئات (10)"),
                rtl("نماذج العشائر (10)"),
            ],
            state="readonly",
            justify="right",
            width=20,
        )
        scope_cb.pack(side="right", padx=(10, 6))
        scope_cb.bind("<<ComboboxSelected>>", lambda _e: self.refresh())

        tk.Label(ctrl, text=rtl("إيقاع اللعب:"), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold")).pack(side="right", padx=(4, 0))
        pace_cb = ttk.Combobox(
            ctrl,
            textvariable=self.sec_per_rnd_var,
            values=[
                rtl("30 ثانية / جولة (سريع)"),
                rtl("40 ثانية / جولة (قياسي)"),
                rtl("50 ثانية / جولة (عميق)"),
            ],
            state="readonly",
            justify="right",
            width=19,
        )
        pace_cb.pack(side="right", padx=(10, 6))
        pace_cb.bind("<<ComboboxSelected>>", lambda _e: self.refresh())

        # Left-aligned Action Button & Status
        self.probe_btn = tk.Button(
            ctrl,
            text=rtl("فحص مباشر فوري (80 مباراة)"),
            command=self._start_live_probe,
            bg=COL_PURPLE,
            fg="#FFFFFF",
            relief="flat",
            font=(FONT_UI, 9, "bold"),
            padx=12,
            pady=4,
            cursor="hand2",
        )
        self.probe_btn.pack(side="left")

        tk.Label(
            ctrl, textvariable=self.probe_status_var, bg=BG_PANEL, fg=COL_GOLD,
            font=(FONT_UI, 8, "bold"), anchor="e", justify="right",
        ).pack(side="left", padx=10)

        # Scrollable canvas body (scrollbar on the LEFT for clean RTL layout)
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
        self.scroll_canvas.bind("<Configure>", self._on_scroll_canvas_resize)
        self.scroll_canvas.bind_all("<MouseWheel>", self._on_mousewheel)

        # --- Row 1: 6 Executive KPI Cards ordered STRICTLY RIGHT-TO-LEFT (col 5 -> col 0) ---
        kpi_row = tk.Frame(self.inner, bg=BG_DARK)
        kpi_row.pack(fill="x", pady=(2, 8))
        kpi_specs_rtl = [
            ("rounds", "متوسط الجولات والوقت المتوقع", COL_BLUE),
            ("lifespan", "متوسط بقاء الوحوش في الميدان", COL_GREEN),
            ("excitement", "متوسط حماس اللعبة", COL_CYAN),
            ("economy", "اقتصاد أوراق اليد والملعب", COL_GOLD),
            ("fairness", "عدالة اللاعب الأول (P1 / P2)", COL_PURPLE),
            ("coverage", "حجم البيانات والتغطية", COL_PINK),
        ]
        n_kpi = len(kpi_specs_rtl)
        for idx, (key, title, accent) in enumerate(kpi_specs_rtl):
            col_pos = (n_kpi - 1) - idx  # Rightmost = col 5, Leftmost = col 0
            card = tk.Frame(kpi_row, bg=BG_CARD, highlightbackground=BG_BORDER, highlightthickness=1)
            card.grid(
                row=0, column=col_pos, sticky="nsew",
                padx=(0 if col_pos == 0 else 4, 0 if col_pos == n_kpi - 1 else 4),
            )
            kpi_row.grid_columnconfigure(col_pos, weight=1, uniform="kpi")
            tk.Frame(card, bg=accent, height=3).pack(fill="x")
            body = tk.Frame(card, bg=BG_CARD, padx=10, pady=8)
            body.pack(fill="both", expand=True)
            tk.Label(body, text=rtl(title), bg=BG_CARD, fg=FG_MUTED, font=(FONT_UI, 8, "bold"), anchor="e", justify="right").pack(fill="x")
            val_lbl = tk.Label(body, text="--", bg=BG_CARD, fg=FG_TEXT, font=(FONT_UI, 14, "bold"), anchor="e", justify="right")
            val_lbl.pack(fill="x", pady=(2, 1))
            sub1 = tk.Label(body, text="", bg=BG_CARD, fg=accent, font=(FONT_UI, 8, "bold"), anchor="e", justify="right")
            sub1.pack(fill="x")
            sub2 = tk.Label(body, text="", bg=BG_CARD, fg=FG_MUTED, font=(FONT_UI, 8), anchor="e", justify="right")
            sub2.pack(fill="x")
            self._kpi_widgets[key] = (val_lbl, sub1, sub2)

        # --- Row 2 (RTL): Excitement Curve on RIGHT (col 1, 58%) + Kill Causes on LEFT (col 0, 42%) ---
        row2 = tk.Frame(self.inner, bg=BG_DARK)
        row2.pack(fill="x", pady=(0, 8))
        row2.grid_columnconfigure(0, weight=42, uniform="r2")
        row2.grid_columnconfigure(1, weight=58, uniform="r2")

        p_kill = self._make_chart_panel(row2, "نسبة موت الوحوش والضرر حسب المصدر (مهارات / هجوم / سحر)", COL_PURPLE)
        p_kill.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        self.cv_kills = tk.Canvas(p_kill, bg=BG_PANEL, height=285, highlightthickness=0)
        self.cv_kills.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_kills.bind("<Configure>", lambda _e: self._draw_kill_causes())

        p_exc = self._make_chart_panel(row2, "منحنى حماس اللعبة من أول جولة حتى النهاية (رسم بياني)", COL_CYAN)
        p_exc.grid(row=0, column=1, sticky="nsew", padx=(5, 0))
        self.cv_excitement = tk.Canvas(p_exc, bg=BG_PANEL, height=285, highlightthickness=0)
        self.cv_excitement.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_excitement.bind("<Configure>", lambda _e: self._draw_excitement_curve())

        # --- Row 3 (RTL): Deck Pick Rates on RIGHT (col 1, 50%) + Monster Survival & Pacing on LEFT (col 0, 50%) ---
        row3 = tk.Frame(self.inner, bg=BG_DARK)
        row3.pack(fill="x", pady=(0, 8))
        row3.grid_columnconfigure(0, weight=1, uniform="r3")
        row3.grid_columnconfigure(1, weight=1, uniform="r3")

        p_surv = self._make_chart_panel(row3, "توزيع بقاء الوحوش في الميدان وتوزيع أطوال المباريات", COL_GREEN)
        p_surv.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        self.cv_survival = tk.Canvas(p_surv, bg=BG_PANEL, height=285, highlightthickness=0)
        self.cv_survival.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_survival.bind("<Configure>", lambda _e: self._draw_survival_and_pacing())

        p_deck = self._make_chart_panel(row3, "نسبة اختيار كل أنواع البطاقات في أحدث ديكات النماذج", COL_GOLD)
        p_deck.grid(row=0, column=1, sticky="nsew", padx=(5, 0))
        self.cv_decks = tk.Canvas(p_deck, bg=BG_PANEL, height=285, highlightthickness=0)
        self.cv_decks.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_decks.bind("<Configure>", lambda _e: self._draw_deck_pick_rates())

        # --- Row 4 (RTL): Hand Economy on RIGHT (col 1, 58%) + Highlights on LEFT (col 0, 42%) ---
        row4 = tk.Frame(self.inner, bg=BG_DARK)
        row4.pack(fill="x", pady=(0, 8))
        row4.grid_columnconfigure(0, weight=42, uniform="r4")
        row4.grid_columnconfigure(1, weight=58, uniform="r4")

        p_hl = self._make_chart_panel(row4, "تكرار اللحظات الحماسية والدرامية (لكل 100 مباراة)", COL_PINK)
        p_hl.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        self.cv_highlights = tk.Canvas(p_hl, bg=BG_PANEL, height=215, highlightthickness=0)
        self.cv_highlights.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_highlights.bind("<Configure>", lambda _e: self._draw_highlights())

        p_hand = self._make_chart_panel(row4, "منحنى اقتصاد اليد ووحوش الملعب عبر الجولات (بداية ونهاية الدور)", COL_BLUE)
        p_hand.grid(row=0, column=1, sticky="nsew", padx=(5, 0))
        self.cv_hand = tk.Canvas(p_hand, bg=BG_PANEL, height=215, highlightthickness=0)
        self.cv_hand.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_hand.bind("<Configure>", lambda _e: self._draw_hand_curve())

    def _make_chart_panel(self, parent: tk.Widget, title: str, accent: str) -> tk.Frame:
        f = tk.Frame(parent, bg=BG_PANEL, highlightbackground=BG_BORDER, highlightthickness=1)
        hdr = tk.Frame(f, bg=BG_PANEL, padx=10, pady=7)
        hdr.pack(fill="x")
        tk.Frame(hdr, bg=accent, width=4, height=15).pack(side="right", padx=(7, 0))
        tk.Label(hdr, text=rtl(title), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold"), anchor="e", justify="right").pack(side="right")
        return f

    def _on_scroll_canvas_resize(self, event: tk.Event) -> None:
        self.scroll_canvas.itemconfigure(self._inner_win, width=max(960, event.width))

    def _on_mousewheel(self, event: tk.Event) -> None:
        try:
            if str(self.parent.winfo_ismapped()) == "1":
                self.scroll_canvas.yview_scroll(int(-1 * (event.delta / 120)), "units")
        except Exception:
            pass

    def _is_probe_source(self) -> bool:
        return "عيّنة" in self.source_var.get()

    def _active_ex(self) -> Dict[str, Any]:
        if self._is_probe_source() and self.probe_data:
            return self.probe_data
        return self.store.excitement

    def _sec_per_round(self) -> int:
        s = self.sec_per_rnd_var.get()
        if "30" in s:
            return 30
        if "50" in s:
            return 50
        return 40

    def _compute_avg_rounds(self, ex: Dict[str, Any]) -> float:
        tm = int(ex.get("total_matches", 0))
        tsum = int(ex.get("total_turns_sum", 0))
        if tm > 0 and tsum > 0:
            return tsum / float(tm)
        tc = ex.get("turn_curve", {})
        fin_sum, fin_w = 0, 0
        for t in range(1, 26):
            fc = int(tc.get(str(t), {}).get("finishes", 0))
            fin_sum += fc
            fin_w += fc * t
        if fin_sum > 0:
            return fin_w / float(fin_sum)
        tot_m, tot_t = 0, 0.0
        for r in self.store.roster.values():
            m = int(r.get("matches", 0))
            if m > 0 and float(r.get("avg_turns", 0.0)) > 0:
                tot_m += m
                tot_t += m * float(r["avg_turns"])
        return (tot_t / tot_m) if tot_m > 0 else 0.0

    def _compute_turn_excitement_series(self, ex: Dict[str, Any]) -> List[Tuple[int, float, float, float, int]]:
        tc = ex.get("turn_curve", {})
        series: List[Tuple[int, float, float, float, int]] = []
        for t in range(1, 26):
            b = tc.get(str(t), {})
            cnt = int(b.get("turns", 0))
            if cnt <= 0:
                continue
            avg_dmg = float(b.get("dmg", 0)) / cnt
            avg_kills = float(b.get("kills", 0)) / cnt
            avg_swings = float(b.get("swings", 0)) / cnt
            avg_hi = float(b.get("highlights", 0)) / cnt
            avg_brd = float(b.get("board_sum", 0)) / cnt if int(b.get("board_sum", 0)) > 0 else 0.0
            exc_sum = int(b.get("exc_sum", 0))
            if exc_sum > 0:
                exc_val = min(100.0, max(0.0, exc_sum / float(cnt)))
            else:
                exc_val = min(
                    100.0,
                    max(
                        6.0,
                        8.0
                        + min(40.0, avg_dmg * 0.55)
                        + min(24.0, avg_kills * 11.0)
                        + min(20.0, avg_swings * 45.0)
                        + min(16.0, avg_hi * 12.0)
                        + min(14.0, t * 0.85),
                    ),
                )
            series.append((t, exc_val, avg_dmg, avg_brd, cnt))
        return series

    def refresh(self) -> None:
        self.probe_data = self._load_probe_snapshot()
        ex = self._active_ex()
        if self.probe_data and not self._probe_running:
            pm = int(self.probe_data.get("total_matches", 0))
            self.probe_status_var.set(rtl(f"عيّنة جاهزة: {pm} مباراة"))
        elif not self._probe_running:
            self.probe_status_var.set(rtl("اضغط فحص مباشر لمعاينة التعديلات"))

        self._update_kpis(ex)
        self._draw_excitement_curve()
        self._draw_kill_causes()
        self._draw_deck_pick_rates()
        self._draw_survival_and_pacing()
        self._draw_hand_curve()
        self._draw_highlights()

    def _update_kpis(self, ex: Dict[str, Any]) -> None:
        tm = int(ex.get("total_matches", 0))
        avg_r = self._compute_avg_rounds(ex)
        sec_r = self._sec_per_round()
        est_sec = int(round(avg_r * sec_r))
        est_min, est_rem = divmod(est_sec, 60)

        # 1. Rounds & Est. Time
        v, s1, s2 = self._kpi_widgets["rounds"]
        if avg_r > 0:
            v.config(text=rtl(f"{avg_r:.1f} جولة"))
            s1.config(text=rtl(f"الوقت المتوقع: {est_min}:{est_rem:02d} دقيقة"))
            verdict = "إيقاع مثالي (10-16 جولة)" if 10.0 <= avg_r <= 16.5 else ("إيقاع سريع" if avg_r < 10.0 else "معارك تكتيكية طويلة")
            s2.config(text=rtl(verdict))
        else:
            v.config(text="--")
            s1.config(text=rtl("لا توجد مباريات مسجلة"))
            s2.config(text="")

        # 2. Monster Board Lifespan
        v, s1, s2 = self._kpi_widgets["lifespan"]
        ms = ex.get("monster_survival", {})
        died_cnt = int(ms.get("died_cnt", 0))
        if died_cnt > 0:
            avg_life = float(ms.get("died_rounds", 0)) / died_cnt
            d0_pct = 100.0 * int(ms.get("d0", 0)) / died_cnt
            cd = max(1, int(ms.get("combat_died", 0)))
            z_pct = 100.0 * int(ms.get("combat_0atk", 0)) / cd
            v.config(text=rtl(f"{avg_life:.2f} جولة"))
            s1.config(text=rtl(f"موت فوري (0 جولة): {d0_pct:.1f}%"))
            s2.config(text=rtl(f"مات قبل أول هجمة: {z_pct:.1f}%"))
        else:
            v.config(text=rtl("قيد الانتظار"))
            s1.config(text=rtl("اختر عيّنة الفحص المباشر"))
            s2.config(text=rtl("أو شغّل جولة تدريب جديدة"))

        # 3. Excitement Score
        v, s1, s2 = self._kpi_widgets["excitement"]
        avg_exc = float(ex.get("avg_excitement", 0.0))
        cb = int(ex.get("comeback_wins", 0))
        cl = int(ex.get("clutch_wins", 0))
        if tm > 0:
            v.config(text=rtl(f"{avg_exc:.1f} / 100"))
            s1.config(text=rtl(f"عودة: {100.0*cb/tm:.1f}%  •  درامي: {100.0*cl/tm:.1f}%"))
            swings = int(ex.get("total_lead_swings", 0)) / float(tm)
            s2.config(text=rtl(f"متوسط تقلبات التقدّم: {swings:.2f} / مباراة"))
        else:
            v.config(text="--")
            s1.config(text=rtl("لا توجد بيانات حماس بعد"))
            s2.config(text="")

        # 4. Board & Hand Economy
        v, s1, s2 = self._kpi_widgets["economy"]
        tc = ex.get("turn_curve", {})
        hs_sum = sum(int(tc.get(str(t), {}).get("hs", 0)) for t in range(1, 26))
        he_sum = sum(int(tc.get(str(t), {}).get("he", 0)) for t in range(1, 26))
        hemp_sum = sum(int(tc.get(str(t), {}).get("hempty", 0)) for t in range(1, 26))
        t_sum = sum(int(tc.get(str(t), {}).get("turns", 0)) for t in range(1, 26))
        bt = int(ms.get("board_turns", 0))
        avg_brd = (float(ms.get("board_sum", 0)) / bt) if bt > 0 else 0.0
        if t_sum > 0 and (hs_sum > 0 or he_sum > 0):
            v.config(text=rtl(f"{hs_sum/t_sum:.1f} ← {he_sum/t_sum:.1f} ورقة"))
            s1.config(text=rtl(f"أدوار بيد فارغة: {100.0*hemp_sum/t_sum:.1f}%"))
            s2.config(text=rtl(f"متوسط وحوش الملعب: {avg_brd:.2f} / 6") if bt > 0 else rtl("متوسط أوراق اليد بداية ونهاية الدور"))
        else:
            v.config(text="--")
            s1.config(text=rtl("لا توجد بيانات يد بعد"))
            s2.config(text="")

        # 5. P1 vs P2 Fairness
        v, s1, s2 = self._kpi_widgets["fairness"]
        p1g = int(ex.get("p1_games", 0))
        p1w = int(ex.get("p1_wins", 0))
        if p1g > 0:
            p1_pct = 100.0 * p1w / p1g
            v.config(text=f"{p1_pct:.1f}% / {100.0-p1_pct:.1f}%")
            tag = "متوازن تماماً (47-53%)" if 47.0 <= p1_pct <= 53.0 else ("أفضلية للاعب الأول" if p1_pct > 53.0 else "أفضلية للاعب الثاني")
            s1.config(text=rtl(tag))
            s2.config(text=rtl(f"من أصل {p1g} مباراة محسومة"))
        else:
            v.config(text=rtl("قيد الانتظار"))
            s1.config(text=rtl("يُقاس في المباريات الجديدة"))
            s2.config(text=rtl("أو في عيّنة الفحص المباشر"))

        # 6. Coverage
        v, s1, s2 = self._kpi_widgets["coverage"]
        total_roster_m = sum(int(r.get("matches", 0)) for r in self.store.roster.values()) // 2
        trained_cnt = sum(1 for r in self.store.roster.values() if int(r.get("matches", 0)) > 0)
        cards_seen = sum(1 for st in self.store.card_stats.values() if int(st.get("drafted", 0)) > 0)
        shown_m = tm if self._is_probe_source() else max(tm, total_roster_m)
        v.config(text=rtl(f"{shown_m:,} مباراة"))
        s1.config(text=rtl(f"النماذج المدرّبة: {trained_cnt} / 41"))
        s2.config(text=rtl(f"البطاقات المختبرة: {cards_seen:,} / 1,470"))

    # ------------------------------------------------------------------
    # Chart 1: Match Excitement Curve Across Turns (Graphical Line + Area)
    # ------------------------------------------------------------------
    def _draw_excitement_curve(self) -> None:
        cv = self.cv_excitement
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(240, cv.winfo_height())

        ex = self._active_ex()
        series = self._compute_turn_excitement_series(ex)

        pad_l, pad_r, pad_t, pad_b = 22, 46, 34, 38
        pw = max(60, w - pad_l - pad_r)
        ph = max(60, h - pad_t - pad_b)

        if len(series) < 2:
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات منحنى جولات بعد — شغّل التدريب أو الفحص المباشر."), fill=FG_MUTED, font=(FONT_UI, 10))
            return

        max_t = max(s[0] for s in series)
        min_t = min(s[0] for s in series)
        t_span = max(1, max_t - min_t)
        max_dmg = max(40.0, max(s[2] for s in series))

        # RTL time axis: Turn 1 starts on the RIGHT, progressing LEFT to Endgame!
        def tx(turn: int) -> float:
            return (pad_l + pw) - pw * ((turn - min_t) / float(t_span))

        def ty_exc(val: float) -> float:
            return pad_t + ph * (1.0 - max(0.0, min(100.0, val)) / 100.0)

        def ty_dmg(val: float) -> float:
            return pad_t + ph * (1.0 - max(0.0, min(max_dmg, val)) / max_dmg)

        # Phase background bands (RTL: Early on Right, Mid in Center, Climax on Left)
        bands = [
            (min_t, min(4, max_t), "#151D2B", "البداية والتأسيس"),
            (min(4, max_t), min(10, max_t), "#182032", "اشتعال الوسط"),
            (min(10, max_t), max_t, "#1E1A2E", "الذروة والحسم"),
        ]
        for b0, b1, bcol, blbl in bands:
            if b1 > b0:
                x_right, x_left = tx(b0), tx(b1)
                cv.create_rectangle(x_left, pad_t, x_right, pad_t + ph, fill=bcol, outline="")
                if x_right - x_left > 65:
                    cv.create_text((x_left + x_right) / 2, pad_t + 11, text=rtl(blbl), fill="#65789A", font=(FONT_UI, 8, "bold"))

        # Horizontal grid lines (Y-axis labels on the RIGHT for RTL readability!)
        for gv in (0, 25, 50, 75, 100):
            gy = ty_exc(gv)
            cv.create_line(pad_l, gy, pad_l + pw, gy, fill="#242E40", dash=(2, 3))
            cv.create_text(pad_l + pw + 8, gy, text=str(gv), anchor="w", fill=FG_MUTED, font=(FONT_UI, 8))

        # RTL Legend at top-right
        rx = pad_l + pw
        cv.create_line(rx - 20, 12, rx, 12, fill=COL_CYAN, width=3)
        cv.create_text(rx - 26, 12, text=rtl("مؤشر الحماس (0-100)"), anchor="e", fill=COL_CYAN, font=(FONT_UI, 8, "bold"))
        cv.create_line(rx - 185, 12, rx - 165, 12, fill=COL_GOLD, width=2, dash=(4, 2))
        cv.create_text(rx - 191, 12, text=rtl(f"متوسط الضرر/جولة (قمة {max_dmg:.0f})"), anchor="e", fill=COL_GOLD, font=(FONT_UI, 8, "bold"))

        # Area polygon under Excitement curve
        pts_exc = [(tx(t), ty_exc(ev)) for t, ev, _, _, _ in series]
        poly = [pts_exc[0][0], pad_t + ph]
        for px, py in pts_exc:
            poly.extend([px, py])
        poly.extend([pts_exc[-1][0], pad_t + ph])
        cv.create_polygon(poly, fill="#11384B", outline="", stipple="gray50")

        # Secondary dashed line: Avg Turn Damage
        pts_dmg = []
        for t, _, dmg, _, _ in series:
            pts_dmg.extend([tx(t), ty_dmg(dmg)])
        if len(pts_dmg) >= 4:
            cv.create_line(pts_dmg, fill=COL_GOLD, width=2, smooth=True, dash=(4, 3))

        # Primary glowing Excitement line
        flat_exc = []
        for px, py in pts_exc:
            flat_exc.extend([px, py])
        cv.create_line(flat_exc, fill="#084E68", width=6, smooth=True)
        cv.create_line(flat_exc, fill=COL_CYAN, width=3, smooth=True)

        # Data points & X-axis labels
        peak_pt = max(series, key=lambda s: s[1])
        step_lbl = 1 if len(series) <= 14 else 2
        for idx, (t, ev, dmg, _, _) in enumerate(series):
            px, py = tx(t), ty_exc(ev)
            r = 4.5 if t == peak_pt[0] else 3.0
            dot_col = "#FDE047" if t == peak_pt[0] else COL_CYAN
            cv.create_oval(px - r, py - r, px + r, py + r, fill=dot_col, outline=BG_PANEL, width=1.5)
            if idx % step_lbl == 0 or t == max_t:
                cv.create_text(px, pad_t + ph + 14, text=f"ج{t}", fill=FG_MUTED, font=(FONT_UI, 8))

        # Peak Climax Badge
        pk_x, pk_y = tx(peak_pt[0]), ty_exc(peak_pt[1])
        badge_x = min(pad_l + pw - 78, max(pad_l + 78, pk_x))
        badge_y = max(pad_t + 28, pk_y - 22)
        cv.create_rectangle(badge_x - 74, badge_y - 11, badge_x + 74, badge_y + 11, fill="#0D2736", outline=COL_CYAN, width=1)
        cv.create_text(badge_x, badge_y, text=rtl(f"أعلى حماس (ج{peak_pt[0]}): {peak_pt[1]:.0f}/100"), fill="#FDE047", font=(FONT_UI, 8, "bold"))

        cv.create_text(pad_l + pw / 2, h - 8, text=rtl("تدرّج جولات المباراة (من الجولة 1 يميناً ← حتى النهاية يساراً)"), fill=FG_MUTED, font=(FONT_UI, 8))

    # ------------------------------------------------------------------
    # Chart 2: Monster Kill Causes & Damage Breakdown (Donut + RTL Bars)
    # ------------------------------------------------------------------
    def _draw_kill_causes(self) -> None:
        cv = self.cv_kills
        cv.delete("all")
        w = max(320, cv.winfo_width())
        h = max(240, cv.winfo_height())

        ex = self._active_ex()
        kc = ex.get("kill_causes", {})
        ds = ex.get("dmg_sources", {})

        items = [
            ("active", "المهارات النشطة", COL_PURPLE),
            ("attack", "الهجوم العادي", COL_GOLD),
            ("spell", "البطاقات السحرية", COL_CYAN),
            ("counter", "المرتد والأشواك", COL_RED),
            ("dot", "حرق / سم / نزيف", COL_GREEN),
            ("passive", "الباسفات والوصية", COL_SLATE),
        ]
        tot_k = sum(int(kc.get(k, 0)) for k, _, _ in items)
        tot_d = sum(int(ds.get(k, 0)) for k, _, _ in items)

        if tot_k <= 0:
            cv.create_text(w // 2, h // 2 - 12, text=rtl("تتبع مصادر موت الوحوش أصبح مفعّلاً!"), fill=COL_GOLD, font=(FONT_UI, 10, "bold"))
            cv.create_text(w // 2, h // 2 + 12, text=rtl("اختر (عيّنة الفحص المباشر) من الأعلى أو ابدأ تدريب جديد."), fill=FG_MUTED, font=(FONT_UI, 8))
            return

        # Left side: Vector Donut Chart
        donut_r = min(76, int(min(w * 0.22, h * 0.35)))
        cx = max(donut_r + 16, int(w * 0.21))
        cy = h // 2 + 6
        start_ang = 90.0
        for key, _, col in items:
            cnt = int(kc.get(key, 0))
            if cnt <= 0:
                continue
            ext = -360.0 * (cnt / float(tot_k))
            if abs(ext) >= 359.9:
                cv.create_oval(cx - donut_r, cy - donut_r, cx + donut_r, cy + donut_r, fill=col, outline=BG_PANEL, width=1.5)
            else:
                cv.create_arc(
                    cx - donut_r, cy - donut_r, cx + donut_r, cy + donut_r,
                    start=start_ang, extent=ext, fill=col, outline=BG_PANEL, width=1.5,
                )
            start_ang += ext

        hole_r = int(donut_r * 0.56)
        cv.create_oval(cx - hole_r, cy - hole_r, cx + hole_r, cy + hole_r, fill=BG_PANEL, outline=BG_BORDER)
        cv.create_text(cx, cy - 8, text=f"{tot_k:,}", fill=FG_TEXT, font=(FONT_UI, 11, "bold"))
        cv.create_text(cx, cy + 10, text=rtl("قتلة وحش"), fill=FG_MUTED, font=(FONT_UI, 8, "bold"))

        # Right side: Ranked Arabic RTL Bars (filling cleanly Right -> Left!)
        bar_l = cx + donut_r + 18
        bar_r = w - 12
        rw = max(110, bar_r - bar_l)
        ranked = sorted(items, key=lambda it: int(kc.get(it[0], 0)), reverse=True)
        row_h = max(34, (h - 32) // len(ranked))
        y0 = 18

        cv.create_text(bar_r, 8, text=rtl("مصدر القتل"), anchor="e", fill=FG_MUTED, font=(FONT_UI, 8, "bold"))
        cv.create_text(bar_l, 8, text=rtl("قتل% | ضرر%"), anchor="w", fill=FG_MUTED, font=(FONT_UI, 8, "bold"))

        for i, (key, lbl, col) in enumerate(ranked):
            y = y0 + i * row_h + 6
            k_pct = 100.0 * int(kc.get(key, 0)) / tot_k
            d_pct = (100.0 * int(ds.get(key, 0)) / tot_d) if tot_d > 0 else 0.0
            # Color dot & Arabic label on the RIGHT
            cv.create_rectangle(bar_r - 9, y + 2, bar_r, y + 11, fill=col, outline="")
            cv.create_text(bar_r - 14, y + 6, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            # Percentages on the LEFT
            cv.create_text(bar_l, y + 6, text=f"{k_pct:4.1f}% | {d_pct:4.1f}%", anchor="w", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))

            # Bar grows cleanly from RIGHT to LEFT!
            bar_y = y + 17
            cv.create_rectangle(bar_l, bar_y, bar_r, bar_y + 8, fill="#242C3D", outline="")
            bw = max(2, int(rw * min(100.0, k_pct) / 100.0)) if k_pct > 0 else 0
            if bw > 0:
                cv.create_rectangle(bar_r - bw, bar_y, bar_r, bar_y + 8, fill=col, outline="")

    # ------------------------------------------------------------------
    # Chart 3: Latest Settled Decks Card-Type Pick Rates (RTL Bars + Roles)
    # ------------------------------------------------------------------
    def _draw_deck_pick_rates(self) -> None:
        cv = self.cv_decks
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(240, cv.winfo_height())

        scope = self.deck_scope_var.get()
        decks: List[List[int]] = []
        for spec in ROSTER_40:
            if "Free-10" in scope and spec.category != "Free":
                continue
            elif "الأساليب" in scope and spec.category != "Playstyle":
                continue
            elif "البيئات" in scope and spec.category != "Environment":
                continue
            elif "العشائر" in scope and spec.category != "Tribe":
                continue
            rec = self.store.roster.get(spec.model_id, {})
            d = rec.get("settled_deck") or []
            if len(d) == 50:
                decks.append([int(x) for x in d])

        if not decks:
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد ديكات محفوظة لهذا النطاق بعد."), fill=FG_MUTED, font=(FONT_UI, 10))
            return

        n_decks = len(decks)
        tot_cards = n_decks * 50
        type_counts = Counter()
        role_counts = Counter()
        for d in decks:
            for cid in d:
                c = self.db.cards.get(cid)
                if not c:
                    continue
                if c.type == "monster":
                    if c.is_barrier:
                        type_counts["barrier"] += 1
                    else:
                        type_counts["monster"] += 1
                    role_counts[c.role] += 1
                else:
                    type_counts[c.type] += 1

        cv.create_text(
            w - 14, 11,
            text=rtl(f"النطاق: {n_decks} ديك (إجمالي {tot_cards:,} خانة بطاقة في أحدث الديكات)"),
            anchor="e", fill=COL_GOLD, font=(FONT_UI, 8, "bold"),
        )

        rows = [
            ("monster", "الوحوش القتالية", COL_BLUE),
            ("spell", "البطاقات السحرية", COL_CYAN),
            ("covenant", "العهود والقيود", COL_GOLD),
            ("barrier", "الحواجز (0 هجوم)", COL_GREEN),
            ("leader", "تمائم القادة", COL_PURPLE),
            ("environment", "بطاقات البيئة", COL_PINK),
        ]
        # RTL layout: Arabic labels on RIGHT -> Bar grows Right-to-Left -> % & Avg on LEFT!
        lbl_x = w - 14
        bar_r = w - 128
        bar_l = 120
        bar_w = max(80, bar_r - bar_l)
        y_start = 30
        step_y = 26

        for i, (key, lbl, col) in enumerate(rows):
            y = y_start + i * step_y
            cnt = type_counts[key]
            pct = 100.0 * cnt / tot_cards
            avg_c = cnt / float(n_decks)
            cv.create_text(lbl_x, y + 8, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(bar_l, y + 2, bar_r, y + 15, fill="#242C3D", outline="")
            fill_w = int(bar_w * min(100.0, pct) / 100.0)
            if fill_w > 0:
                cv.create_rectangle(bar_r - fill_w, y + 2, bar_r, y + 15, fill=col, outline="")
            cv.create_text(
                bar_l - 8, y + 8,
                text=f"{pct:4.1f}% ({avg_c:4.1f}/50)",
                anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"),
            )

        # Bottom Monster Role Composition Stacked Strip (RTL right-to-left)
        tot_m = sum(role_counts.values())
        ry = y_start + len(rows) * step_y + 14
        cv.create_text(w - 14, ry, text=rtl("توزيع أدوار الوحوش المختارة (القتالية والحواجز):"), anchor="e", fill=FG_MUTED, font=(FONT_UI, 8, "bold"))
        if tot_m > 0:
            role_cols = [COL_RED, COL_PINK, COL_BLUE, COL_GREEN, COL_CYAN, COL_PURPLE]
            sx_right = w - 14
            sw = w - 28
            sy = ry + 14
            for ri in range(len(ROLES)):
                rc = role_counts[ri]
                if rc <= 0:
                    continue
                seg_w = max(2, int(round(sw * rc / float(tot_m))))
                if sx_right - seg_w < 14:
                    seg_w = max(0, sx_right - 14)
                cv.create_rectangle(sx_right - seg_w, sy, sx_right, sy + 19, fill=role_cols[ri % len(role_cols)], outline=BG_PANEL)
                if seg_w >= 34:
                    cv.create_text(sx_right - seg_w / 2, sy + 9, text=f"{100.0*rc/tot_m:.0f}%", fill="#FFFFFF", font=(FONT_UI, 8, "bold"))
                sx_right -= seg_w

            # Role Legend row (RTL right-to-left)
            ly = sy + 30
            col_step = max(62, (w - 28) // len(ROLES))
            for ri, ar_name in enumerate(AR_ROLES):
                rx_i = (w - 14) - ri * col_step
                rp = 100.0 * role_counts[ri] / tot_m
                cv.create_rectangle(rx_i - 8, ly - 4, rx_i, ly + 4, fill=role_cols[ri % len(role_cols)], outline="")
                cv.create_text(rx_i - 12, ly, text=rtl(f"{ar_name} {rp:.0f}%"), anchor="e", fill=FG_MUTED, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Chart 4: Monster Board Survival Lifespan & Match Length Histogram
    # ------------------------------------------------------------------
    def _draw_survival_and_pacing(self) -> None:
        cv = self.cv_survival
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(240, cv.winfo_height())

        ex = self._active_ex()
        ms = ex.get("monster_survival", {})
        ml = ex.get("match_len_bins", {})

        # --- TOP HALF: Monster Survival Lifespan Distribution (RTL Right->Left) ---
        top_h = h // 2 - 4
        cv.create_text(w - 14, 11, text=rtl("عمر الوحوش في الميدان قبل الموت (بالجولات الكاملة)"), anchor="e", fill=COL_GREEN, font=(FONT_UI, 8, "bold"))

        died_cnt = int(ms.get("died_cnt", 0))
        if died_cnt > 0:
            avg_life = float(ms.get("died_rounds", 0)) / died_cnt
            cv.create_text(14, 11, text=rtl(f"المتوسط: {avg_life:.2f} جولة ({died_cnt:,} وحش)"), anchor="w", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            buckets = [
                ("فوري (0 جولة)", int(ms.get("d0", 0)), COL_RED),
                ("جولة واحدة", int(ms.get("d1", 0)), COL_GOLD),
                ("جولتان", int(ms.get("d2", 0)), COL_CYAN),
                ("+3 جولات", int(ms.get("d3p", 0)), COL_GREEN),
            ]
            max_b = max(1, max(b[1] for b in buckets))
            bx0, bx1 = 18, w - 18
            col_w = (bx1 - bx0) / len(buckets)
            base_y = top_h - 19
            bar_max_h = max(24, top_h - 54)
            # Place 0-Rnd on Rightmost, progressing Left to 3+ Rnds
            for i, (lbl, cnt, col) in enumerate(buckets):
                pct = 100.0 * cnt / died_cnt
                cx = bx1 - i * col_w - col_w * 0.5
                bw = min(56, col_w * 0.56)
                bh = max(3, int(bar_max_h * cnt / max_b)) if cnt > 0 else 0
                cv.create_rectangle(cx - bw / 2, base_y - bh, cx + bw / 2, base_y, fill=col, outline="")
                cv.create_text(cx, base_y - bh - 9, text=f"{pct:.1f}%", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
                cv.create_text(cx, base_y + 11, text=rtl(lbl), fill=FG_MUTED, font=(FONT_UI, 8, "bold"))
        else:
            cv.create_text(w // 2, top_h // 2 + 6, text=rtl("اختر (عيّنة الفحص المباشر) أو ابدأ تدريب لعرض عمر الوحوش!"), fill=FG_MUTED, font=(FONT_UI, 8))

        mid_y = top_h + 6
        cv.create_line(12, mid_y, w - 12, mid_y, fill=BG_BORDER)

        # --- BOTTOM HALF: Match Length Distribution (RTL Right->Left) ---
        cv.create_text(w - 14, mid_y + 13, text=rtl("توزيع أطوال المباريات (عدد الجولات حتى الحسم)"), anchor="e", fill=COL_BLUE, font=(FONT_UI, 8, "bold"))
        bins_def = [
            ("1-6 سريعة", "1-6", COL_RED),
            ("7-12 نشطة", "7-12", COL_CYAN),
            ("13-18 مثالية", "13-18", COL_GREEN),
            ("19-25 طويلة", "19-25", COL_GOLD),
            ("26-39 استنزاف", "26-39", COL_PURPLE),
            ("+40 عقوبة", "40+", COL_PINK),
        ]
        tot_ml = sum(int(ml.get(k, 0)) for _, k, _ in bins_def)
        if tot_ml <= 0:
            tc = ex.get("turn_curve", {})
            fb = {"1-6": 0, "7-12": 0, "13-18": 0, "19-25": 0, "26-39": 0, "40+": 0}
            for t in range(1, 26):
                fc = int(tc.get(str(t), {}).get("finishes", 0))
                if t <= 6: fb["1-6"] += fc
                elif t <= 12: fb["7-12"] += fc
                elif t <= 18: fb["13-18"] += fc
                else: fb["19-25"] += fc
            ml = fb
            tot_ml = sum(ml.values())

        if tot_ml > 0:
            max_m = max(1, max(int(ml.get(k, 0)) for _, k, _ in bins_def))
            bx0, bx1 = 18, w - 18
            col_w = (bx1 - bx0) / len(bins_def)
            base_y = h - 22
            bar_max_h = max(24, h - mid_y - 62)
            for i, (lbl, key, col) in enumerate(bins_def):
                cnt = int(ml.get(key, 0))
                pct = 100.0 * cnt / tot_ml
                cx = bx1 - i * col_w - col_w * 0.5
                bw = min(44, col_w * 0.60)
                bh = max(3, int(bar_max_h * cnt / max_m)) if cnt > 0 else 0
                cv.create_rectangle(cx - bw / 2, base_y - bh, cx + bw / 2, base_y, fill=col, outline="")
                if pct >= 0.5:
                    cv.create_text(cx, base_y - bh - 9, text=f"{pct:.0f}%", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
                cv.create_text(cx, base_y + 11, text=rtl(lbl), fill=FG_MUTED, font=(FONT_UI, 7, "bold"))
        else:
            cv.create_text(w // 2, (mid_y + h) // 2 + 6, text=rtl("لا يوجد توزيع لأطوال المباريات بعد."), fill=FG_MUTED, font=(FONT_UI, 8))

    # ------------------------------------------------------------------
    # Chart 5: Hand Economy & Board Occupancy Across Turns (RTL)
    # ------------------------------------------------------------------
    def _draw_hand_curve(self) -> None:
        cv = self.cv_hand
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(180, cv.winfo_height())

        ex = self._active_ex()
        tc = ex.get("turn_curve", {})
        pts = []
        for t in range(1, 21):
            b = tc.get(str(t), {})
            cnt = int(b.get("turns", 0))
            if cnt <= 0:
                continue
            hs = float(b.get("hs", 0)) / cnt
            he = float(b.get("he", 0)) / cnt
            brd = float(b.get("board_sum", 0)) / cnt
            if hs > 0 or he > 0 or brd > 0:
                pts.append((t, hs, he, brd))

        if len(pts) < 2:
            cv.create_text(w // 2, h // 2, text=rtl("منحنى أوراق اليد والملعب سيمتلئ بعد التدريب أو الفحص المباشر."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        pad_l, pad_r, pad_t, pad_b = 20, 40, 30, 32
        pw = max(60, w - pad_l - pad_r)
        ph = max(50, h - pad_t - pad_b)
        min_t, max_t = pts[0][0], pts[-1][0]
        span = max(1, max_t - min_t)

        def tx(t: int) -> float:
            return (pad_l + pw) - pw * ((t - min_t) / float(span))

        def ty(v: float) -> float:
            return pad_t + ph * (1.0 - max(0.0, min(7.0, v)) / 7.0)

        for gv in (0, 2, 4, 6, 7):
            gy = ty(gv)
            cv.create_line(pad_l, gy, pad_l + pw, gy, fill="#242E40", dash=(2, 3))
            cv.create_text(pad_l + pw + 8, gy, text=str(gv), anchor="w", fill=FG_MUTED, font=(FONT_UI, 8))

        # RTL Legend at top-right
        rx = pad_l + pw
        cv.create_line(rx - 18, 12, rx, 12, fill=COL_BLUE, width=2.5)
        cv.create_text(rx - 22, 12, text=rtl("اليد (البداية)"), anchor="e", fill=COL_BLUE, font=(FONT_UI, 8, "bold"))
        cv.create_line(rx - 118, 12, rx - 100, 12, fill=COL_GOLD, width=2.5)
        cv.create_text(rx - 122, 12, text=rtl("اليد (النهاية)"), anchor="e", fill=COL_GOLD, font=(FONT_UI, 8, "bold"))
        if any(p[3] > 0 for p in pts):
            cv.create_line(rx - 224, 12, rx - 206, 12, fill=COL_GREEN, width=2.5, dash=(4, 2))
            cv.create_text(rx - 228, 12, text=rtl("وحوش الملعب (/6)"), anchor="e", fill=COL_GREEN, font=(FONT_UI, 8, "bold"))

        s_line, e_line, b_line = [], [], []
        for t, hs, he, brd in pts:
            x = tx(t)
            s_line.extend([x, ty(hs)])
            e_line.extend([x, ty(he)])
            if brd > 0:
                b_line.extend([x, ty(brd)])
        cv.create_line(s_line, fill=COL_BLUE, width=2.5, smooth=True)
        cv.create_line(e_line, fill=COL_GOLD, width=2.5, smooth=True)
        if len(b_line) >= 4:
            cv.create_line(b_line, fill=COL_GREEN, width=2.5, smooth=True, dash=(4, 2))

        step = 1 if len(pts) <= 13 else 2
        for idx, (t, _, _, _) in enumerate(pts):
            if idx % step == 0 or t == max_t:
                cv.create_text(tx(t), pad_t + ph + 14, text=f"ج{t}", fill=FG_MUTED, font=(FONT_UI, 8))

    # ------------------------------------------------------------------
    # Chart 6: Combat Highlights & Drama Frequency (RTL Horizontal Bars)
    # ------------------------------------------------------------------
    def _draw_highlights(self) -> None:
        cv = self.cv_highlights
        cv.delete("all")
        w = max(320, cv.winfo_width())
        h = max(180, cv.winfo_height())

        ex = self._active_ex()
        tm = int(ex.get("total_matches", 0))
        hl = ex.get("highlights", {})
        if tm <= 0 or not hl:
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد لحظات حماسية مسجلة بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        names = {
            "Mega_Burst_Turn": ("انفجار ضرر (+120 بجولة)", COL_GOLD),
            "Multi_Kill_Sweep": ("مسح متعدد (+3 وحوش)", COL_RED),
            "Comeback_120HP": ("عودة أسطورية (-120 HP)", COL_CYAN),
            "Clutch_Finish": ("فوز درامي (<=75 HP)", COL_GREEN),
            "Undying_1DEF_Save": ("إنقاذ الخلود (1 DEF)", COL_PURPLE),
            "Hydra_Split": ("انقسام الهيدرا", COL_BLUE),
            "Ascend_Evolution": ("ارتقاء المعركة (Ascend)", COL_PINK),
            "Counter_Tech_Break": ("كسر بتعاويذ النقض", COL_GOLD),
            "Stat_Flip_Surprise": ("قلب الإحصائيات المفاجئ", COL_CYAN),
        }
        ranked = sorted(hl.items(), key=lambda kv: int(kv[1]), reverse=True)[:6]
        max_rate = max(1.0, max(100.0 * int(v) / tm for _, v in ranked))

        lbl_x = w - 12
        bar_r = w - 166
        bar_l = 58
        bar_w = max(60, bar_r - bar_l)
        row_h = max(25, (h - 18) // max(1, len(ranked)))
        for i, (k, v) in enumerate(ranked):
            y = 10 + i * row_h
            lbl, col = names.get(k, (k.replace("_", " "), COL_BLUE))
            rate = 100.0 * int(v) / tm
            cv.create_text(lbl_x, y + 8, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(bar_l, y + 3, bar_r, y + 14, fill="#242C3D", outline="")
            fw = max(2, int(bar_w * rate / max_rate))
            cv.create_rectangle(bar_r - fw, y + 3, bar_r, y + 14, fill=col, outline="")
            cv.create_text(bar_l - 6, y + 8, text=f"{rate:.1f}%", anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Non-Destructive Quick Live Probe (80 matches using current balance)
    # ------------------------------------------------------------------
    def _start_live_probe(self) -> None:
        if self._probe_running:
            return
        self._probe_running = True
        self.probe_btn.config(state="disabled", text=rtl("جاري تشغيل 80 مباراة..."))
        self.probe_status_var.set(rtl("جاري محاكاة 80 مباراة حية على القوانين الحالية..."))
        threading.Thread(target=self._run_probe_worker, daemon=True).start()

    def _run_probe_worker(self) -> None:
        try:
            from engine.ai_brain import ModelBrain
            from engine.battle_engine import BattleEngine

            candidates = [f"free_{i:02d}" for i in range(1, 11)] + ["style_alpha", "style_bastion", "style_pyro", "style_arcane"]
            agents: Dict[str, Any] = {}
            decks: Dict[str, List[int]] = {}
            saved_dir = os.path.abspath(os.path.join(self.store.logs_dir, "..", "saved_models"))
            for mid in candidates:
                dp = os.path.join(saved_dir, mid, "settled_deck.json")
                if os.path.isfile(dp):
                    with open(dp, "r", encoding="utf-8") as f:
                        d = json.load(f)
                    cids = d.get("deck_50_ids") or []
                    if len(cids) == 50:
                        decks[mid] = [int(x) for x in cids]
                        agents[mid] = ModelBrain.load_or_create(mid, self.db, use_warm_start=False)

            mids = list(agents.keys())
            if len(mids) < 2:
                raise RuntimeError("تحتاج نموذجين مدرّبين على الأقل بديك 50 بطاقة.")

            fresh_db = CardDB()
            eng = BattleEngine(fresh_db)
            rng = random.Random(int(time.time()) & 0xFFFF)

            ex = _default_excitement_data()
            n_matches = 80
            for i in range(n_matches):
                m0, m1 = rng.sample(mids, 2)
                eng.reset(decks[m0], decks[m1], seed=rng.randrange(1, 10_000_000))
                pair = [agents[m0], agents[m1]]
                steps = 0
                while not eng.done and steps < 800:
                    s = eng.active
                    act, _, _ = pair[s].select_action(eng.get_obs(s), eng.get_legal_mask(s), deterministic=False)
                    eng.step(act)
                    steps += 1
                res = eng.get_result()
                self._accumulate_probe_result(ex, res)
                if (i + 1) % 20 == 0:
                    self.parent.after(0, lambda k=i + 1: self.probe_status_var.set(rtl(f"الفحص المباشر: {k}/{n_matches} مباراة...")))

            ex["probe_ts"] = time.strftime("%Y-%m-%d %H:%M:%S")
            ex["probe_version"] = fresh_db.version
            tmp = self.probe_path + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(ex, f, indent=2, ensure_ascii=False)
            os.replace(tmp, self.probe_path)

            def _done_ok() -> None:
                self._probe_running = False
                self.probe_btn.config(state="normal", text=rtl("فحص مباشر فوري (80 مباراة)"))
                self.source_var.set(rtl("عيّنة الفحص المباشر (قوانين V2.2 الحالية)"))
                self.refresh()

            self.parent.after(0, _done_ok)
        except Exception as err:
            def _done_err(msg: str = str(err)) -> None:
                self._probe_running = False
                self.probe_btn.config(state="normal", text=rtl("فحص مباشر فوري (80 مباراة)"))
                self.probe_status_var.set(rtl(f"خطأ بالفحص: {msg[:50]}"))
            self.parent.after(0, _done_err)

    @staticmethod
    def _accumulate_probe_result(ex: Dict[str, Any], result: Any) -> None:
        tm = int(ex["total_matches"])
        t0, t1 = result.telemetry[0], result.telemetry[1]
        match_exc = max(t0.excitement_score, t1.excitement_score)
        ex["total_matches"] = tm + 1
        ex["total_turns_sum"] = int(ex["total_turns_sum"]) + int(result.turns)
        ex["avg_excitement"] = round((float(ex["avg_excitement"]) * tm + match_exc) / (tm + 1), 2)
        ex["total_lead_swings"] = int(ex["total_lead_swings"]) + max(t0.lead_swings, t1.lead_swings)

        if t0.went_first or t1.went_first:
            p1_side = 0 if t0.went_first else 1
            if result.winner in (0, 1):
                ex["p1_games"] += 1
                if result.winner == p1_side:
                    ex["p1_wins"] += 1

        if result.winner in (0, 1):
            wt = result.telemetry[result.winner]
            if wt.comeback_deficit >= 100:
                ex["comeback_wins"] += 1
            if wt.clutch_win:
                ex["clutch_wins"] += 1

        ml = ex["match_len_bins"]
        rt = int(result.turns)
        if rt <= 6: ml["1-6"] += 1
        elif rt <= 12: ml["7-12"] += 1
        elif rt <= 18: ml["13-18"] += 1
        elif rt <= 25: ml["19-25"] += 1
        elif rt <= 39: ml["26-39"] += 1
        else: ml["40+"] += 1

        ms, kc, ds = ex["monster_survival"], ex["kill_causes"], ex["dmg_sources"]
        for st in (t0, t1):
            ms["died_cnt"] += st.units_died_count
            ms["died_rounds"] += st.units_died_rounds_sum
            ms["d0"] += st.units_died_0rnd
            ms["d1"] += st.units_died_1rnd
            ms["d2"] += st.units_died_2rnd
            ms["d3p"] += st.units_died_3p_rnd
            ms["combat_died"] += st.combat_died_count
            ms["combat_0atk"] += st.combat_died_0atk
            ms["surv_cnt"] += st.units_survived_end
            ms["surv_rounds"] += st.units_survived_rounds_sum
            ms["board_sum"] += st.board_units_turn_sum
            ms["board_turns"] += st.hand_turns

            kc["active"] += st.kills_by_active
            kc["attack"] += st.kills_by_attack
            kc["spell"] += st.kills_by_spell
            kc["counter"] += st.kills_by_counter
            kc["dot"] += st.kills_by_dot
            kc["passive"] += st.kills_by_passive

            ds["active"] += st.dmg_m_by_active
            ds["attack"] += st.dmg_m_by_attack
            ds["spell"] += st.dmg_m_by_spell
            ds["counter"] += st.dmg_m_by_counter
            ds["dot"] += st.dmg_m_by_dot
            ds["passive"] += st.dmg_m_by_passive

            for htag, hcnt in st.highlight_tags.items():
                ex["highlights"][htag] = int(ex["highlights"].get(htag, 0)) + int(hcnt)

        tc = ex["turn_curve"]
        for ev in result.turn_events:
            t_num, tdmg, tkills, tswing, thi = ev[:5]
            t_hs = ev[5] if len(ev) > 5 else 0
            t_he = ev[6] if len(ev) > 6 else 0
            t_exc = ev[7] if len(ev) > 7 else 0
            t_brd = ev[8] if len(ev) > 8 else 0
            b = tc.get(str(min(25, max(1, t_num))))
            if b is not None:
                b["turns"] += 1
                b["dmg"] += tdmg
                b["kills"] += tkills
                b["swings"] += tswing
                if tdmg >= 100:
                    b["bursts"] += 1
                b["highlights"] += thi
                b["hs"] += t_hs
                b["he"] += t_he
                b["exc_sum"] += t_exc
                b["board_sum"] += t_brd
                if t_he == 0 and len(ev) > 6:
                    b["hempty"] += 1
        fin_b = tc.get(str(min(25, max(1, rt))))
        if fin_b is not None:
            fin_b["finishes"] += 1

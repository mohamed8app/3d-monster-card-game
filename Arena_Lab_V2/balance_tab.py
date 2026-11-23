# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Pillar #6: Archetype, Tribe, Element & Roster Balance Tab (Arabic RTL).
Evaluates balance across all 41 AI models (10 Tribes, 10 Elements, 10 Playstyles, 10 Free, Glitch_41)
and all 1,470 cards with:
  - Overall 0-100% Balance Score (>=85% Ideal, >=70% Good, <70% Warning)
  - Interactive 5-Way Chart 1: 10 Tribes | 10 Elements | 10 Playstyles | 10 Free | 6 Roles (centered on 50%!)
  - Chart 2: Top 5 Strongest (OP Watch) vs Bottom 5 Weakest (Buff Watch) + 4-Category Summary
  - Self-Explanatory 14-Row Diagnostic Table (Actual | Ideal | Good | Status | Auto-Diagnosis)
  - Non-Destructive Live V2.2 41-Model Tournament Probe (123 matches = 6 games/model)
"""

from __future__ import annotations

import json
import math
import os
import random
import threading
import time
import tkinter as tk
from tkinter import ttk
from typing import Any, Callable, Dict, List, Optional, Tuple

from engine.card_model import CardDB, ELEMENTS, ROLES, TRIBES
from engine.roster_40 import ROSTER_40, ROSTER_MAP
from trainer.stats_store import StatsStore
from pace_tab import rtl
from arabic_glossary import arabic_model_name, enable_treeview_sorting

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

FONT_UI = "Segoe UI"

TRIBE_AR = {
    "Celestials": "السماويون", "Demons": "الشياطين", "Ghosts": "الأشباح",
    "Mages": "السحرة", "Warriors": "المحاربون", "Dwarves": "الأقزام",
    "Giants": "العمالقة", "Elves": "الجان", "Beasts": "الوحوش", "Insects": "الحشرات",
}
ELEM_AR = {
    "Fire": "النار", "Water": "الماء", "Nature": "الطبيعة", "Electric": "الكهرباء",
    "Ice": "الجليد", "Earth": "الأرض", "Dark": "الظلام", "Light": "النور",
    "Air": "الهواء", "Desert": "الصحراء",
}
ROLE_AR = {
    "Attacker": "مهاجم", "Assassin": "قاتل", "Defender": "مدافع",
    "Barrier": "حاجز", "Support": "داعم", "Disruptor": "معطّل",
}


class ArchetypeBalanceTab:
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
        self.probe_path = os.path.join(self.store.logs_dir, "balance_probe_v22.json")
        self.probe_data: Optional[Dict[str, Any]] = self._load_probe_snapshot()
        self._probe_running = False

        self.source_var = tk.StringVar(value=rtl("سجلات التدريب الكاملة (كل الـ 41 نموذجاً)"))
        self.chart_mode_var = tk.StringVar(value="Tribe")
        self.probe_status_var = tk.StringVar(value="")
        self._last_metrics: Dict[str, Any] = {}
        self._mode_buttons: Dict[str, tk.Button] = {}
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

        # Visual Charts Row (RTL: Interactive 5-Way Breakdown on RIGHT 56%, Extremes & Categories on LEFT 44%)
        charts = tk.Frame(self.inner, bg=BG_DARK)
        charts.pack(fill="x", pady=(0, 8))
        charts.grid_columnconfigure(0, weight=44, uniform="bal")
        charts.grid_columnconfigure(1, weight=56, uniform="bal")

        p_ext = self._make_panel(charts, "الأقوى (مرشح إضعاف) مقابل الأضعف (مرشح تقوية) + المجموعات الأربع", COL_RED)
        p_ext.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        self.cv_extremes = tk.Canvas(p_ext, bg=BG_PANEL, height=275, highlightthickness=0)
        self.cv_extremes.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_extremes.bind("<Configure>", lambda _e: self._draw_extremes())

        p_grp = self._make_panel(charts, "نسبة الفوز التفصيلية حول خط التوازن 50% (اضغط للتبديل)", COL_GOLD)
        p_grp.grid(row=0, column=1, sticky="nsew", padx=(5, 0))

        # 5-Way Toggle Bar (RTL right-to-left)
        tbar = tk.Frame(p_grp, bg=BG_PANEL, padx=8, pady=2)
        tbar.pack(fill="x")
        modes = [
            ("Tribe", "العشائر (10)"),
            ("Environment", "العناصر (10)"),
            ("Playstyle", "الأساليب (10)"),
            ("Free", "الميتا الحرة (10)"),
            ("Roles", "الأدوار (6)"),
        ]
        for key, lbl in modes:
            btn = tk.Button(
                tbar,
                text=rtl(lbl),
                command=lambda k=key: self._set_chart_mode(k),
                bg=COL_BLUE if key == "Tribe" else BG_CARD,
                fg="#FFFFFF" if key == "Tribe" else FG_MUTED,
                relief="flat",
                font=(FONT_UI, 8, "bold"),
                padx=8,
                pady=2,
                cursor="hand2",
            )
            btn.pack(side="right", padx=2)
            self._mode_buttons[key] = btn

        self.cv_group = tk.Canvas(p_grp, bg=BG_PANEL, height=248, highlightthickness=0)
        self.cv_group.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_group.bind("<Configure>", lambda _e: self._draw_group_bars())

        # Self-Explanatory 14-Indicator Diagnostic Table
        tbl_panel = self._make_panel(
            self.inner,
            "بطاقة التقييم التفصيلية لمعيار توازن العشائر والعناصر والنماذج (14 مؤشراً — النتيجة + الجيد + المثالي + التشخيص)",
            COL_BLUE,
        )
        tbl_panel.pack(fill="x", pady=(0, 8))

        headers = [
            ("diag", "ماذا يعني الرقم الآن؟ (تشخيص تلقائي)", 375),
            ("status", "التقييم", 96),
            ("good", "الجيد 🟡", 118),
            ("ideal", "المثالي 🟢", 118),
            ("actual", "نتيجتك الحالية", 140),
            ("metric", "المؤشر", 220),
            ("num", "#", 34),
        ]
        self.bal_tree = ttk.Treeview(tbl_panel, columns=tuple(c for c, _, _ in headers), show="headings", height=14)
        for c, txt, w in headers:
            self.bal_tree.heading(c, text=rtl(txt))
            self.bal_tree.column(c, width=w, anchor="e" if c in ("metric", "diag") else "center")

        self.bal_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.bal_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.bal_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.bal_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        self.bal_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.bal_tree)

    def _make_panel(self, parent: tk.Widget, title: str, accent: str) -> tk.Frame:
        f = tk.Frame(parent, bg=BG_PANEL, highlightbackground=BG_BORDER, highlightthickness=1)
        hdr = tk.Frame(f, bg=BG_PANEL, padx=10, pady=7)
        hdr.pack(fill="x")
        tk.Frame(hdr, bg=accent, width=4, height=15).pack(side="right", padx=(7, 0))
        tk.Label(hdr, text=rtl(title), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold"), anchor="e", justify="right").pack(side="right")
        return f

    def _set_chart_mode(self, mode: str) -> None:
        self.chart_mode_var.set(mode)
        for k, b in self._mode_buttons.items():
            b.config(bg=COL_BLUE if k == mode else BG_CARD, fg="#FFFFFF" if k == mode else FG_MUTED)
        self._draw_group_bars()

    def _on_mousewheel(self, event: tk.Event) -> None:
        try:
            if str(self.parent.winfo_ismapped()) == "1":
                self.scroll_canvas.yview_scroll(int(-1 * (event.delta / 120)), "units")
        except Exception:
            pass

    def _is_probe(self) -> bool:
        return "بطولة" in self.source_var.get()

    def refresh(self) -> None:
        self._compute_and_populate()
        self._draw_score_banner()
        self._draw_group_bars()
        self._draw_extremes()

    @staticmethod
    def _grade_range(val: float, ideal_lo: float, ideal_hi: float, good_lo: float, good_hi: float) -> Tuple[str, str, int]:
        if ideal_lo <= val <= ideal_hi:
            return ("🟢 مثالي", "ideal", 100)
        if good_lo <= val <= good_hi:
            return ("🟡 جيد", "good", 72)
        return ("🔴 يحتاج تعديل", "bad", 28)

    @staticmethod
    def _short_model_label(mid: str) -> str:
        return arabic_model_name(mid)

    def _active_datasets(self) -> Tuple[Dict[str, Dict[str, Any]], Dict[str, Dict[str, Any]], Dict[str, Dict[str, int]], Dict[str, Any], int]:
        """Reads directly from active training store (local or live remote PC #2)."""
        return (
            self.store.roster,
            self.store.head_to_head,
            self.store.card_stats,
            self.store.model_card_stats,
            10,
        )

    def _compute_and_populate(self) -> None:
        self.bal_tree.delete(*self.bal_tree.get_children())
        roster, h2h, cstats, mcs, min_m = self._active_datasets()

        trained: List[Dict[str, Any]] = []
        by_cat: Dict[str, List[Dict[str, Any]]] = {"Tribe": [], "Environment": [], "Playstyle": [], "Free": [], "Anomaly": []}
        for spec in ROSTER_40:
            r = roster.get(spec.model_id, {})
            m = int(r.get("matches", 0))
            w = int(r.get("wins", 0))
            elo = float(r.get("elo", 1000.0))
            if m >= min_m:
                item = {
                    "id": spec.model_id,
                    "label": self._short_model_label(spec.model_id),
                    "cat": spec.category,
                    "matches": m,
                    "wins": w,
                    "wr": 100.0 * w / m,
                    "elo": elo,
                }
                trained.append(item)
                by_cat.setdefault(spec.category, []).append(item)

        if len(trained) < 4:
            self._last_metrics = {"has_data": False, "score": 0, "ideal_cnt": 0, "good_cnt": 0, "bad_cnt": 0}
            std_balance_indicators = [
                (1, "فجوة الفوز بين الـ 10 عشائر (أعلى − أدنى)", "12% فأقل", "12% – 20%"),
                (2, "فجوة الفوز بين الـ 10 عناصر / بيئات", "12% فأقل", "12% – 20%"),
                (3, "فجوة الفوز بين الـ 10 أساليب لعب", "12% فأقل", "12% – 20%"),
                (4, "فجوة الفوز بين النماذج الحرة (نموذج حر 1–10)", "12% فأقل", "12% – 18%"),
                (5, "نسبة النماذج داخل النطاق الصحي (45%–55%)", "70% فأعلى", "50% – 69%"),
                (6, "عدد النماذج المتطرفة (>60% خارق أو <40% ميت)", "0 – 2 نموذج", "3 – 6 نماذج"),
                (7, "الانحراف المعياري لفوز كل النماذج (WR σ)", "5.0% فأقل", "5.0% – 8.0%"),
                (8, "مدى الفارق التنافسي في تصنيف ELO", "220 فأقل", "220 – 380"),
                (9, "توازن الفئات الأربع (عشائر/عناصر/أساليب/حر)", "5.0% فأقل", "5.0% – 9.0%"),
                (10, "توازن قوة بطاقات العشائر الـ 10 (الكتالوج)", "4.0% فأقل", "4.0% – 7.5%"),
                (11, "توازن قوة بطاقات العناصر الـ 10 (الكتالوج)", "4.0% فأقل", "4.0% – 7.5%"),
                (12, "توازن الأدوار القتالية الـ 6 (مهاجم/مدافع/حاجز..)", "4.5% فأقل", "4.5% – 8.5%"),
                (13, "نسبة المواجهات القاتلة أحادية الطرف (>65%)", "15% فأقل", "15% – 30%"),
                (14, "الاعتماد على بطاقة واحدة خارقة (Carry Lift)", "+7.0% فأقل", "+7% – +12%"),
            ]
            for num, m_name, ideal_s, good_s in std_balance_indicators:
                self.bal_tree.insert(
                    "", "end",
                    values=(
                        rtl("بانتظار بيانات تدريب 4+ نماذج لتفعيل قياس التوازن الرياضي."),
                        rtl("بانتظار بيانات"),
                        rtl(good_s),
                        rtl(ideal_s),
                        "--",
                        rtl(m_name),
                        num,
                    ),
                    tags=("nodata",),
                )
            return

        def _spread_info(items: List[Dict[str, Any]]) -> Tuple[float, str, str, float, float]:
            if len(items) < 2:
                return (0.0, "--", "--", 50.0, 50.0)
            hi = max(items, key=lambda x: x["wr"])
            lo = min(items, key=lambda x: x["wr"])
            return (hi["wr"] - lo["wr"], hi["label"], lo["label"], hi["wr"], lo["wr"])

        tr_sp, tr_hi, tr_lo, tr_hw, tr_lw = _spread_info(by_cat["Tribe"])
        el_sp, el_hi, el_lo, el_hw, el_lw = _spread_info(by_cat["Environment"])
        ps_sp, ps_hi, ps_lo, ps_hw, ps_lw = _spread_info(by_cat["Playstyle"])
        fr_sp, fr_hi, fr_lo, fr_hw, fr_lw = _spread_info(by_cat["Free"])

        n_tr = len(trained)
        wrs_all = [x["wr"] for x in trained]
        healthy_cnt = sum(1 for w in wrs_all if 45.0 <= w <= 55.0)
        healthy_pct = 100.0 * healthy_cnt / n_tr
        op_models = [x for x in trained if x["wr"] > 60.0]
        weak_models = [x for x in trained if x["wr"] < 40.0]
        outlier_cnt = len(op_models) + len(weak_models)

        mean_wr = sum(wrs_all) / n_tr
        std_wr = math.sqrt(sum((w - mean_wr) ** 2 for w in wrs_all) / n_tr)

        elos = [x["elo"] for x in trained]
        elo_spread = max(elos) - min(elos)
        elo_top = max(trained, key=lambda x: x["elo"])
        elo_bot = min(trained, key=lambda x: x["elo"])

        cat_avgs: Dict[str, float] = {}
        for ck in ("Tribe", "Environment", "Playstyle", "Free"):
            arr = by_cat[ck]
            if arr:
                cat_avgs[ck] = sum(x["wr"] for x in arr) / len(arr)
        cat_spread = (max(cat_avgs.values()) - min(cat_avgs.values())) if len(cat_avgs) >= 2 else 0.0

        # Catalog-level Tribe / Element / Role Win-Rate balance from card_stats
        tr_c_p, tr_c_w = [0] * 10, [0] * 10
        el_c_p, el_c_w = [0] * 10, [0] * 10
        rl_c_p, rl_c_w = [0] * 6, [0] * 6
        for cid_s, st in cstats.items():
            pl = int(st.get("played", 0))
            if pl <= 0:
                continue
            pw = int(st.get("played_wins", 0))
            c = self.db.cards.get(int(cid_s))
            if not c or c.type != "monster":
                continue
            if 0 <= c.tribe < 10:
                tr_c_p[c.tribe] += pl
                tr_c_w[c.tribe] += pw
            if 0 <= c.element < 10:
                el_c_p[c.element] += pl
                el_c_w[c.element] += pw
            if 0 <= c.role < 6:
                rl_c_p[c.role] += pl
                rl_c_w[c.role] += pw

        tr_c_wrs = [(100.0 * tr_c_w[i] / tr_c_p[i], TRIBE_AR.get(TRIBES[i], TRIBES[i])) for i in range(10) if tr_c_p[i] >= 20]
        el_c_wrs = [(100.0 * el_c_w[i] / el_c_p[i], ELEM_AR.get(ELEMENTS[i], ELEMENTS[i])) for i in range(10) if el_c_p[i] >= 20]
        rl_c_wrs = [(100.0 * rl_c_w[i] / rl_c_p[i], ROLE_AR.get(ROLES[i], ROLES[i]), rl_c_p[i]) for i in range(6) if rl_c_p[i] >= 20]

        cat_tr_sp = (max(x[0] for x in tr_c_wrs) - min(x[0] for x in tr_c_wrs)) if len(tr_c_wrs) >= 2 else 0.0
        cat_el_sp = (max(x[0] for x in el_c_wrs) - min(x[0] for x in el_c_wrs)) if len(el_c_wrs) >= 2 else 0.0
        cat_rl_sp = (max(x[0] for x in rl_c_wrs) - min(x[0] for x in rl_c_wrs)) if len(rl_c_wrs) >= 2 else 0.0

        # Hard-counter matchups (>65% one-sided) from head_to_head
        h2h_min = 20
        h2h_valid = 0
        h2h_hard = 0
        worst_h2h = ""
        worst_h2h_wr = 50.0
        for rec_h in h2h.values():
            mh = int(rec_h.get("m", 0))
            if mh < h2h_min:
                continue
            h2h_valid += 1
            wa = int(rec_h.get("wa", 0))
            wb = int(rec_h.get("wb", 0))
            hi_wr = 100.0 * max(wa, wb) / mh
            if hi_wr > 65.0:
                h2h_hard += 1
            if hi_wr > worst_h2h_wr:
                worst_h2h_wr = hi_wr
                w_side = arabic_model_name(rec_h.get("a", "") if wa >= wb else rec_h.get("b", ""))
                l_side = arabic_model_name(rec_h.get("b", "") if wa >= wb else rec_h.get("a", ""))
                worst_h2h = f"{w_side} ضد {l_side} ({hi_wr:.0f}%)"
        hard_cnt_pct = (100.0 * h2h_hard / h2h_valid) if h2h_valid > 0 else 0.0

        # Single-card carry dependency lift from model_card_stats
        lifts: List[Tuple[float, str, str]] = []
        for item in trained:
            mc = mcs.get(item["id"], {})
            if not mc:
                continue
            best_lift = 0.0
            best_cid = 0
            for cid_s, arr in mc.items():
                if len(arr) >= 4 and int(arr[2]) >= 30:
                    c_wr = 100.0 * int(arr[3]) / int(arr[2])
                    lift = c_wr - item["wr"]
                    if lift > best_lift:
                        best_lift = lift
                        best_cid = int(cid_s)
            if best_cid > 0:
                cname = self.db.cards[best_cid].name if best_cid in self.db.cards else f"#{best_cid}"
                lifts.append((best_lift, item["label"], cname))
        avg_carry_lift = (sum(x[0] for x in lifts) / len(lifts)) if lifts else 0.0
        top_carry = max(lifts, key=lambda x: x[0]) if lifts else (0.0, "--", "--")

        rows: List[Tuple[int, str, str, str, str, str, str, str, int]] = []

        # 1. Tribes Spread
        st, tg, pts = self._grade_range(tr_sp, 0.0, 12.0, 0.0, 20.0)
        diag = f"أقوى عشيرة: {tr_hi} ({tr_hw:.1f}%) | أضعف عشيرة: {tr_lo} ({tr_lw:.1f}%)."
        rows.append((1, "فجوة الفوز بين الـ 10 عشائر (أعلى − أدنى)", f"{tr_sp:.1f}%", "12% فأقل", "12% – 20%", st, diag, tg, pts))

        # 2. Elements Spread
        st, tg, pts = self._grade_range(el_sp, 0.0, 12.0, 0.0, 20.0)
        diag = f"أقوى عنصر: {el_hi} ({el_hw:.1f}%) | أضعف عنصر: {el_lo} ({el_lw:.1f}%)."
        rows.append((2, "فجوة الفوز بين الـ 10 عناصر / بيئات", f"{el_sp:.1f}%", "12% فأقل", "12% – 20%", st, diag, tg, pts))

        # 3. Playstyles Spread
        st, tg, pts = self._grade_range(ps_sp, 0.0, 12.0, 0.0, 20.0)
        diag = f"أقوى أسلوب: {ps_hi} ({ps_hw:.1f}%) | أضعف أسلوب: {ps_lo} ({ps_lw:.1f}%)."
        rows.append((3, "فجوة الفوز بين الـ 10 أساليب لعب", f"{ps_sp:.1f}%", "12% فأقل", "12% – 20%", st, diag, tg, pts))

        # 4. Free-10 Meta Spread
        st, tg, pts = self._grade_range(fr_sp, 0.0, 12.0, 0.0, 18.0)
        diag = f"النماذج الحرة المنفصلة (حر 1–10): أعلى {fr_hi} ({fr_hw:.1f}%) وأدنى {fr_lo} ({fr_lw:.1f}%)."
        rows.append((4, "فجوة الفوز بين النماذج الحرة (نموذج حر 1–10)", f"{fr_sp:.1f}%", "12% فأقل", "12% – 18%", st, diag, tg, pts))

        # 5. Healthy Band (45%-55%) %
        st, tg, pts = self._grade_range(healthy_pct, 70.0, 100.0, 50.0, 100.0)
        diag = (
            f"توازن ممتاز! {healthy_cnt} من {n_tr} نموذجاً داخل النطاق الصحي (45%–55%)." if tg == "ideal"
            else f"{healthy_cnt} فقط من {n_tr} نموذجاً داخل 45%–55% — الباقي متطرف للأعلى أو الأسفل!"
        )
        rows.append((5, "نسبة النماذج داخل النطاق الصحي (45%–55%)", f"{healthy_pct:.1f}% ({healthy_cnt}/{n_tr})", "70% فأعلى", "50% – 69%", st, diag, tg, pts))

        # 6. Outlier Models (>60% OP or <40% Dead)
        st, tg, pts = self._grade_range(float(outlier_cnt), 0.0, 2.0, 0.0, 6.0)
        diag = (
            f"انضباط ممتاز! لا توجد نماذج خارقة (>60%) أو ميتة (<40%) مزعجة." if outlier_cnt <= 2
            else f"تحذير! يوجد {len(op_models)} نموذج خارق (>60% فوز) و{len(weak_models)} نموذج ضعيف (<40% فوز)!"
        )
        rows.append((6, "عدد النماذج المتطرفة (>60% خارق أو <40% ميت)", f"{outlier_cnt} (OP:{len(op_models)} | ضعيف:{len(weak_models)})", "0 – 2 نموذج", "3 – 6 نماذج", st, diag, tg, pts))

        # 7. Global WR StdDev
        st, tg, pts = self._grade_range(std_wr, 0.0, 5.0, 0.0, 8.0)
        diag = (
            f"تشتت الفوز منخفض جداً (σ = {std_wr:.2f}%) — بيئة تنافسية عادلة جداً." if tg == "ideal"
            else f"تشتت مرتفع بين قوة النماذج (الانحراف المعياري σ = {std_wr:.2f}%)."
        )
        rows.append((7, "الانحراف المعياري لفوز كل النماذج (WR σ)", f"{std_wr:.2f}%", "5.0% فأقل", "5.0% – 8.0%", st, diag, tg, pts))

        # 8. ELO Spread
        st, tg, pts = self._grade_range(elo_spread, 0.0, 220.0, 0.0, 380.0)
        diag = f"أعلى ELO: {elo_top['label']} ({elo_top['elo']:.0f}) | أدنى: {elo_bot['label']} ({elo_bot['elo']:.0f})."
        rows.append((8, "مدى الفارق التنافسي في تصنيف ELO", f"{elo_spread:.0f} نقطة", "220 فأقل", "220 – 380", st, diag, tg, pts))

        # 9. Macro Category Balance (Tribe vs Element vs Playstyle vs Free)
        st, tg, pts = self._grade_range(cat_spread, 0.0, 5.0, 0.0, 9.0)
        c_hi = max(cat_avgs.items(), key=lambda x: x[1]) if cat_avgs else ("--", 50.0)
        c_lo = min(cat_avgs.items(), key=lambda x: x[1]) if cat_avgs else ("--", 50.0)
        cat_tr_ar = {"Tribe": "العشائر", "Environment": "العناصر", "Playstyle": "الأساليب", "Free": "الميتا الحرة"}
        diag = f"متوسط المجموعات: الأعلى {cat_tr_ar.get(c_hi[0], c_hi[0])} ({c_hi[1]:.1f}%) والأدنى {cat_tr_ar.get(c_lo[0], c_lo[0])} ({c_lo[1]:.1f}%)."
        rows.append((9, "توازن الفئات الأربع (عشائر/عناصر/أساليب/حر)", f"فرق {cat_spread:.1f}%", "5.0% فأقل", "5.0% – 9.0%", st, diag, tg, pts))

        # 10. Catalog Tribe Cards WR Spread
        st, tg, pts = self._grade_range(cat_tr_sp, 0.0, 4.0, 0.0, 7.5)
        if tr_c_wrs:
            t_hi = max(tr_c_wrs, key=lambda x: x[0])
            t_lo = min(tr_c_wrs, key=lambda x: x[0])
            diag = f"بطاقات الكتالوج: أعلى عشيرة {t_hi[1]} ({t_hi[0]:.1f}%) وأدنى {t_lo[1]} ({t_lo[0]:.1f}%)."
        else:
            diag = "لا توجد بيانات كروت كافية بعد."
        rows.append((10, "توازن قوة بطاقات العشائر الـ 10 (الكتالوج)", f"فرق {cat_tr_sp:.1f}%", "4.0% فأقل", "4.0% – 7.5%", st, diag, tg, pts))

        # 11. Catalog Element Cards WR Spread
        st, tg, pts = self._grade_range(cat_el_sp, 0.0, 4.0, 0.0, 7.5)
        if el_c_wrs:
            e_hi = max(el_c_wrs, key=lambda x: x[0])
            e_lo = min(el_c_wrs, key=lambda x: x[0])
            diag = f"بطاقات الكتالوج: أعلى عنصر {e_hi[1]} ({e_hi[0]:.1f}%) وأدنى {e_lo[1]} ({e_lo[0]:.1f}%)."
        else:
            diag = "لا توجد بيانات كروت كافية بعد."
        rows.append((11, "توازن قوة بطاقات العناصر الـ 10 (الكتالوج)", f"فرق {cat_el_sp:.1f}%", "4.0% فأقل", "4.0% – 7.5%", st, diag, tg, pts))

        # 12. 6 Combat Roles Card WR Spread
        st, tg, pts = self._grade_range(cat_rl_sp, 0.0, 4.5, 0.0, 8.5)
        if rl_c_wrs:
            r_hi = max(rl_c_wrs, key=lambda x: x[0])
            r_lo = min(rl_c_wrs, key=lambda x: x[0])
            diag = f"الأدوار الستة: الأعلى {r_hi[1]} ({r_hi[0]:.1f}%) والأدنى {r_lo[1]} ({r_lo[0]:.1f}%)."
        else:
            diag = "لا توجد بيانات أدوار كافية بعد."
        rows.append((12, "توازن الأدوار القتالية الـ 6 (مهاجم/مدافع/حاجز..)", f"فرق {cat_rl_sp:.1f}%", "4.5% فأقل", "4.5% – 8.5%", st, diag, tg, pts))

        # 13. Hard-Counter Matchups (>65% WR)
        st, tg, pts = self._grade_range(hard_cnt_pct, 0.0, 15.0, 0.0, 30.0)
        diag = (
            f"تفاعلات عادلة! {h2h_hard} فقط من {h2h_valid} مواجهة مباشرة تجاوزت 65% (أشدها: {worst_h2h})." if h2h_valid > 0 and tg != "bad"
            else (f"تحذير حجر-ورقة-مقص! {hard_cnt_pct:.0f}% من المواجهات أحادية الاتجاه (>65% — أشدها: {worst_h2h})." if h2h_valid > 0 else "لا توجد مواجهات مباشرة مسجلة بعد.")
        )
        rows.append((13, "نسبة المواجهات القاتلة أحادية الطرف (>65%)", f"{hard_cnt_pct:.1f}% ({h2h_hard}/{h2h_valid})", "15% فأقل", "15% – 30%", st, diag, tg, pts))

        # 14. Single-Card Dependency Lift
        st, tg, pts = self._grade_range(avg_carry_lift, 0.0, 7.0, 0.0, 12.0)
        diag = (
            f"فوز جماعي صحي! أقوى ورقة بكل نموذج ترفع فوزه بـ +{avg_carry_lift:.1f}% فقط بالمتوسط." if tg == "ideal"
            else f"اعتماد مرتفع على ورقة واحدة (+{avg_carry_lift:.1f}% بالمتوسط — الأعلى: {top_carry[2]} ترفع {top_carry[1]} بـ +{top_carry[0]:.1f}%!)."
        )
        rows.append((14, "الاعتماد على بطاقة واحدة خارقة (Carry Lift)", f"+{avg_carry_lift:.1f}%", "+7.0% فأقل", "+7% – +12%", st, diag, tg, pts))

        avg_score = int(round(sum(r[8] for r in rows) / len(rows)))
        ideal_cnt = sum(1 for r in rows if r[7] == "ideal")
        good_cnt = sum(1 for r in rows if r[7] == "good")
        bad_cnt = sum(1 for r in rows if r[7] == "bad")

        sorted_all = sorted(trained, key=lambda x: x["wr"], reverse=True)
        role_items = [
            {"label": r_lbl, "wr": r_wr, "matches": r_cnt, "elo": 0.0}
            for (r_wr, r_lbl, r_cnt) in sorted(rl_c_wrs, key=lambda x: x[0], reverse=True)
        ]

        self._last_metrics = {
            "has_data": True,
            "score": avg_score,
            "ideal_cnt": ideal_cnt,
            "good_cnt": good_cnt,
            "bad_cnt": bad_cnt,
            "n_models": n_tr,
            "tot_matches": sum(x["matches"] for x in trained) // 2,
            "by_cat": {k: sorted(v, key=lambda x: x["wr"], reverse=True) for k, v in by_cat.items()},
            "roles": role_items,
            "top5": sorted_all[:5],
            "bot5": list(reversed(sorted_all[-5:])),
            "cat_avgs": cat_avgs,
        }

        for num, m_name, act_s, ideal_s, good_s, st_lbl, diag_s, tag, _ in rows:
            self.bal_tree.insert(
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
            cv.create_text(w // 2, h // 2, text=rtl("بانتظار بيانات التوازن من التدريب — ابدأ التدريب أو اتصل بالجهاز الثاني!"), fill=COL_GOLD, font=(FONT_UI, 11, "bold"))
            return

        sc = int(d["score"])
        if sc >= 85:
            col, badge = COL_GREEN, "🟢 توازن تنافسي مثالي بين العشائر والعناصر والنماذج (AAA)"
        elif sc >= 70:
            col, badge = COL_GOLD, "🟡 توازن جيد (يوجد بعض العشائر/العناصر تحتاج تعديلاً طفيفاً)"
        else:
            col, badge = COL_RED, "🔴 فجوة توازن واضحة بين النماذج (راجع قائمة الأقوى للإضعاف والأضعف للتقوية!)"

        cv.create_text(w - 12, 18, text=rtl(f"التقييم العام لمعيار توازن العشائر والعناصر والنماذج:  {sc} / 100%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 14, "bold"))
        cv.create_text(w - 12, 44, text=rtl(badge), anchor="e", fill=col, font=(FONT_UI, 10, "bold"))
        cv.create_text(
            w - 12, 66,
            text=rtl(f"المعيار: 🟢 >=85% مثالي | 🟡 70–84% جيد | 🔴 <70% خطر  •  (النماذج المقاسة: {d['n_models']} / 41 • إجمالي المباريات: {d['tot_matches']:,})"),
            anchor="e", fill=FG_MUTED, font=(FONT_UI, 8),
        )

        bar_l, bar_r = 16, min(370, int(w * 0.36))
        bar_w = max(140, bar_r - bar_l)
        cv.create_rectangle(bar_l, 16, bar_r, 34, fill="#242C3D", outline=BG_BORDER)
        fw = int(bar_w * min(100, sc) / 100.0)
        if fw > 0:
            cv.create_rectangle(bar_r - fw, 16, bar_r, 34, fill=col, outline="")
        x70 = bar_r - int(bar_w * 0.70)
        x85 = bar_r - int(bar_w * 0.85)
        cv.create_line(x70, 12, x70, 38, fill=COL_GOLD, width=2)
        cv.create_line(x85, 12, x85, 38, fill=COL_GREEN, width=2)
        cv.create_text(x70, 7, text="70%", fill=COL_GOLD, font=(FONT_UI, 7, "bold"))
        cv.create_text(x85, 7, text="85%", fill=COL_GREEN, font=(FONT_UI, 7, "bold"))

        cv.create_text(
            (bar_l + bar_r) / 2, 54,
            text=rtl(f"🟢 مثالي: {d['ideal_cnt']}/14   •   🟡 جيد: {d['good_cnt']}/14   •   🔴 تعديل: {d['bad_cnt']}/14"),
            fill=FG_TEXT, font=(FONT_UI, 8, "bold"),
        )

    # ------------------------------------------------------------------
    # Chart 1: Interactive 5-Way Breakdown Centered on 50% Balance Line
    # ------------------------------------------------------------------
    def _draw_group_bars(self) -> None:
        cv = self.cv_group
        cv.delete("all")
        w = max(380, cv.winfo_width())
        h = max(210, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        mode = self.chart_mode_var.get()
        items = d["roles"] if mode == "Roles" else d["by_cat"].get(mode, [])
        if not items:
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات لهذه المجموعة."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        lbl_x = w - 8
        bar_r = w - 138
        bar_l = 86
        bar_w = max(80, bar_r - bar_l)
        # Scale 20%..80% so 50% is dead center!
        x50 = bar_l + bar_w * 0.50
        x45 = bar_l + bar_w * (45.0 - 20.0) / 60.0
        x55 = bar_l + bar_w * (55.0 - 20.0) / 60.0

        # Healthy green 45-55% zone backdrop
        cv.create_rectangle(x45, 16, x55, h - 8, fill="#132620", outline="")
        cv.create_line(x50, 14, x50, h - 8, fill=COL_GOLD, width=1, dash=(3, 3))
        cv.create_text(x50, 7, text="50%", fill=COL_GOLD, font=(FONT_UI, 7, "bold"))
        cv.create_text(bar_l, 7, text="20%", fill=FG_MUTED, font=(FONT_UI, 7))
        cv.create_text(bar_r, 7, text="80%", fill=FG_MUTED, font=(FONT_UI, 7))

        row_h = max(20, (h - 24) // max(1, len(items)))
        for i, it in enumerate(items[:10]):
            y = 18 + i * row_h
            wr = float(it["wr"])
            if 45.0 <= wr <= 55.0:
                col = COL_GREEN
            elif 40.0 <= wr <= 60.0:
                col = COL_GOLD
            else:
                col = COL_RED

            cv.create_text(lbl_x, y + row_h * 0.42, text=rtl(str(it["label"])[:20]), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(bar_l, y + 3, bar_r, y + row_h - 6, fill="#1F2736", outline="")

            clamped = max(20.0, min(80.0, wr))
            px = bar_l + bar_w * (clamped - 20.0) / 60.0
            bx0, bx1 = (min(x50, px), max(x50, px))
            if bx1 - bx0 < 3:
                bx0, bx1 = x50 - 2, x50 + 2
            cv.create_rectangle(bx0, y + 3, bx1, y + row_h - 6, fill=col, outline="")
            cv.create_text(bar_l - 6, y + row_h * 0.42, text=f"{wr:5.1f}%", anchor="e", fill=col, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Chart 2: Top 5 Strongest (OP) vs Bottom 5 Weakest + 4-Category Bar
    # ------------------------------------------------------------------
    def _draw_extremes(self) -> None:
        cv = self.cv_extremes
        cv.delete("all")
        w = max(340, cv.winfo_width())
        h = max(230, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات ترتيب بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        mid_x = w // 2
        # Right half: Top 5 Strongest (OP Watch)
        cv.create_text(w - 10, 12, text=rtl("أقوى 5 نماذج (مرشحة للإضعاف)"), anchor="e", fill=COL_RED, font=(FONT_UI, 8, "bold"))
        for i, it in enumerate(d["top5"]):
            y = 30 + i * 24
            wr = float(it["wr"])
            col = COL_RED if wr > 60.0 else (COL_GOLD if wr > 55.0 else COL_GREEN)
            cv.create_text(w - 10, y, text=rtl(f"{i+1}. {it['label'][:17]}"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8))
            cv.create_text(mid_x + 8, y, text=f"{wr:.1f}%", anchor="w", fill=col, font=(FONT_UI, 8, "bold"))

        cv.create_line(mid_x, 8, mid_x, 150, fill=BG_BORDER)

        # Left half: Bottom 5 Weakest (Buff Watch)
        cv.create_text(mid_x - 10, 12, text=rtl("أضعف 5 نماذج (مرشحة للتقوية)"), anchor="e", fill=COL_CYAN, font=(FONT_UI, 8, "bold"))
        for i, it in enumerate(d["bot5"]):
            y = 30 + i * 24
            wr = float(it["wr"])
            col = COL_RED if wr < 40.0 else (COL_GOLD if wr < 45.0 else COL_GREEN)
            cv.create_text(mid_x - 10, y, text=rtl(f"{i+1}. {it['label'][:17]}"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8))
            cv.create_text(8, y, text=f"{wr:.1f}%", anchor="w", fill=col, font=(FONT_UI, 8, "bold"))

        # Bottom Section: 4 Macro-Category Average Win Rates
        cv.create_line(10, 158, w - 10, 158, fill=BG_BORDER)
        cv.create_text(w - 10, 171, text=rtl("متوسط فوز المجموعات الأربع الكبرى (الهدف 50%):"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))

        cat_defs = [
            ("Environment", "العناصر (10)", COL_CYAN),
            ("Tribe", "العشائر (10)", COL_GOLD),
            ("Playstyle", "الأساليب (10)", COL_PURPLE),
            ("Free", "الميتا الحرة (10)", COL_BLUE),
        ]
        bar_r = w - 110
        bar_l = 64
        bar_w = max(80, bar_r - bar_l)
        x50 = bar_l + bar_w * 0.50
        cv.create_line(x50, 182, x50, h - 6, fill=COL_GOLD, dash=(2, 2))

        for i, (ck, lbl, col) in enumerate(cat_defs):
            y = 186 + i * 21
            val = float(d["cat_avgs"].get(ck, 50.0))
            cv.create_text(w - 10, y + 6, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8))
            cv.create_rectangle(bar_l, y + 1, bar_r, y + 12, fill="#1F2736", outline="")
            px = bar_l + bar_w * (max(25.0, min(75.0, val)) - 25.0) / 50.0
            cv.create_rectangle(min(x50, px), y + 1, max(x50, px), y + 12, fill=col, outline="")
            cv.create_text(bar_l - 6, y + 6, text=f"{val:4.1f}%", anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Live V2.2 41-Model Non-Destructive Balance Tournament (123 Matches)
    # ------------------------------------------------------------------
    def _start_balance_probe(self) -> None:
        if self._probe_running:
            return
        self._probe_running = True
        self.probe_btn.config(state="disabled", text=rtl("جاري تشغيل بطولة 123 مباراة..."))
        self.probe_status_var.set(rtl("جاري اختبار كل الـ 41 نموذجاً على قوانين V2.2..."))
        threading.Thread(target=self._run_balance_probe_worker, daemon=True).start()

    def _run_balance_probe_worker(self) -> None:
        try:
            from engine.ai_brain import ModelBrain
            from engine.battle_engine import BattleEngine

            saved_dir = os.path.abspath(os.path.join(self.store.logs_dir, "..", "saved_models"))
            fresh_db = CardDB()
            agents: Dict[str, Any] = {}
            decks: Dict[str, List[int]] = {}
            for spec in ROSTER_40:
                mid = spec.model_id
                dp = os.path.join(saved_dir, mid, "settled_deck.json")
                if os.path.isfile(dp):
                    with open(dp, "r", encoding="utf-8") as f:
                        cids = json.load(f).get("deck_50_ids") or []
                    if len(cids) == 50:
                        decks[mid] = [int(x) for x in cids]
                        agents[mid] = ModelBrain.load_or_create(mid, fresh_db, use_warm_start=False)

            mids = list(agents.keys())
            if len(mids) < 2:
                raise RuntimeError("تحتاج نموذجين على الأقل.")

            rng = random.Random(int(time.time()) & 0xFFFF)
            eng = BattleEngine(fresh_db)

            # Balanced schedule: 3 pairings per model (~123 matches = 6 games each!)
            pairs: List[Tuple[str, str]] = []
            for _ in range(3):
                for m0 in mids:
                    m1 = rng.choice([x for x in mids if x != m0])
                    pairs.append((m0, m1))

            roster_out = {
                m: {"matches": 0, "wins": 0, "losses": 0, "draws": 0, "elo": 1000.0}
                for m in mids
            }
            h2h_out: Dict[str, Dict[str, Any]] = {}
            cstats_out: Dict[str, Dict[str, int]] = {}

            tot_m = len(pairs)
            for idx_m, (m0, m1) in enumerate(pairs):
                eng.reset(decks[m0], decks[m1], seed=rng.randrange(1, 10_000_000))
                pair_a = [agents[m0], agents[m1]]
                steps = 0
                while not eng.done and steps < 800:
                    s = eng.active
                    act, _, _ = pair_a[s].select_action(eng.get_obs(s), eng.get_legal_mask(s), deterministic=False)
                    eng.step(act)
                    steps += 1
                res = eng.get_result()

                ra, rb = roster_out[m0], roster_out[m1]
                ra["matches"] += 1
                rb["matches"] += 1
                ea = 1.0 / (1.0 + 10.0 ** ((rb["elo"] - ra["elo"]) / 400.0))
                sa = 1.0 if res.winner == 0 else (0.0 if res.winner == 1 else 0.5)
                ra["elo"] = round(ra["elo"] + 24.0 * (sa - ea), 1)
                rb["elo"] = round(rb["elo"] + 24.0 * ((1.0 - sa) - (1.0 - ea)), 1)
                if res.winner == 0:
                    ra["wins"] += 1; rb["losses"] += 1
                elif res.winner == 1:
                    rb["wins"] += 1; ra["losses"] += 1
                else:
                    ra["draws"] += 1; rb["draws"] += 1

                a, b = (m0, m1) if m0 <= m1 else (m1, m0)
                hk = f"{a}|{b}"
                hr = h2h_out.setdefault(hk, {"a": a, "b": b, "m": 0, "wa": 0, "wb": 0, "d": 0})
                hr["m"] += 1
                if (res.winner == 0 and m0 == a) or (res.winner == 1 and m1 == a):
                    hr["wa"] += 1
                elif res.winner in (0, 1):
                    hr["wb"] += 1
                else:
                    hr["d"] += 1

                for s_i in (0, 1):
                    won_s = (res.winner == s_i)
                    for cid in res.telemetry[s_i].cards_played_ids:
                        cs = cstats_out.setdefault(str(cid), {"played": 0, "played_wins": 0})
                        cs["played"] += 1
                        if won_s:
                            cs["played_wins"] += 1

                if (idx_m + 1) % 25 == 0:
                    self.parent.after(0, lambda k=idx_m + 1, t=tot_m: self.probe_status_var.set(rtl(f"بطولة التوازن V2.2: {k}/{t} مباراة...")))

            payload = {
                "probe_ts": time.strftime("%Y-%m-%d %H:%M:%S"),
                "probe_version": fresh_db.version,
                "total_matches": tot_m,
                "roster": roster_out,
                "head_to_head": h2h_out,
                "card_stats": cstats_out,
            }
            tmp = self.probe_path + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(payload, f, indent=2, ensure_ascii=False)
            os.replace(tmp, self.probe_path)

            def _done_ok() -> None:
                self._probe_running = False
                self.probe_btn.config(state="normal", text=rtl("فحص توازن V2.2 لكل الـ 41 نموذجاً (123 مباراة)"))
                self.source_var.set(rtl("بطولة الفحص المباشر V2.2 (كل الـ 41 نموذجاً)"))
                self.refresh()

            self.parent.after(0, _done_ok)
        except Exception as err:
            def _done_err(msg: str = str(err)) -> None:
                self._probe_running = False
                self.probe_btn.config(state="normal", text=rtl("فحص توازن V2.2 لكل الـ 41 نموذجاً (123 مباراة)"))
                self.probe_status_var.set(rtl(f"خطأ بالبطولة: {msg[:50]}"))
            self.parent.after(0, _done_err)

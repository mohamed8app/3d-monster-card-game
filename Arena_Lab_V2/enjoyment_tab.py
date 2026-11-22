# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Pillar #10: Gameplay Enjoyment & Emotional Arc Tab (Arabic RTL).
Tracks all 14 fun, excitement, drama, WOW-moment, and frustration indicators:
  - Overall 0-100% Enjoyment Score (>=85% Ideal, >=70% Good, <70% Warning)
  - Visual Chart 1: Turn-by-Turn Excitement Curve (T1 -> T20) showing the real Emotional Arc & Crescendo!
  - Visual Chart 2: Match Excitement Tier Distribution (5 Tiers) + Top 5 WOW Highlights!
  - Self-Explanatory 14-Row Diagnostic Table (Actual | Ideal | Good | Status | Auto-Diagnosis)
"""

from __future__ import annotations

import json
import os
import random
import threading
import time
import tkinter as tk
from tkinter import ttk
from typing import Any, Callable, Dict, List, Optional, Tuple

from engine.card_model import CardDB
from trainer.stats_store import StatsStore, _default_excitement_data
from pace_tab import GamePaceTab, rtl
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
COL_PINK = "#EC4899"

FONT_UI = "Segoe UI"

HL_AR = {
    "Ascend_Evolution": "ارتقاء وتطور وحش (Ascend)",
    "Undying_1DEF_Save": "إنقاذ صمود 1 DEF",
    "Mega_Burst_Turn": "دور انفجار ضخم (>=120)",
    "Multi_Kill_Sweep": "مسح جماعي (>=3 وحوش)",
    "Comeback_120HP": "عودة بطولية (+120 HP)",
    "Clutch_Finish": "حسم درامي (<=75 HP)",
    "Counter_Tech_Break": "كسر عهد أو قائد مضاد",
    "Time_Rewind_Miracle": "معجزة إرجاع الزمن",
    "Polymorph_Sheep": "تحويل لخروف (Polymorph)",
    "Stat_Flip_Surprise": "مفاجأة قلب الإحصائيات",
}


class GameplayEnjoymentTab:
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
        self.probe_path = os.path.join(self.store.logs_dir, "live_probe_snapshot.json")
        self.probe_data: Optional[Dict[str, Any]] = self._load_probe_snapshot()
        self._probe_running = False

        has_main = int(self.store.excitement.get("enjoyment", {}).get("matches", 0)) > 0
        has_probe = bool(self.probe_data and int(self.probe_data.get("enjoyment", {}).get("matches", 0)) > 0)
        default_src = (
            rtl("عيّنة الفحص المباشر (قوانين V2.2 الحالية)")
            if (has_probe and not has_main)
            else rtl("سجلات التدريب الكاملة (كل المباريات)")
        )
        self.source_var = tk.StringVar(value=default_src)
        self.probe_status_var = tk.StringVar(value="")
        self._last_metrics: Dict[str, Any] = {}
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

        # Visual Charts Row (RTL: Excitement Curve on RIGHT 54%, Tiers & WOW Highlights on LEFT 46%)
        charts = tk.Frame(self.inner, bg=BG_DARK)
        charts.pack(fill="x", pady=(0, 8))
        charts.grid_columnconfigure(0, weight=46, uniform="enj")
        charts.grid_columnconfigure(1, weight=54, uniform="enj")

        p_tiers = self._make_panel(charts, "توزيع مستويات متعة المباريات + أبرز لحظات الإبهار (WOW Highlights)", COL_CYAN)
        p_tiers.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        self.cv_tiers = tk.Canvas(p_tiers, bg=BG_PANEL, height=245, highlightthickness=0)
        self.cv_tiers.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_tiers.bind("<Configure>", lambda _e: self._draw_tiers_and_highlights())

        p_arc = self._make_panel(charts, "منحنى تصاعد الحماس والقوس الدرامي عبر الأدوار (T1 → T20)", COL_GOLD)
        p_arc.grid(row=0, column=1, sticky="nsew", padx=(5, 0))
        self.cv_arc = tk.Canvas(p_arc, bg=BG_PANEL, height=245, highlightthickness=0)
        self.cv_arc.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_arc.bind("<Configure>", lambda _e: self._draw_excitement_arc())

        # Self-Explanatory 14-Indicator Diagnostic Table
        tbl_panel = self._make_panel(
            self.inner,
            "بطاقة التقييم التفصيلية لمعيار المتعة والإثارة (14 مؤشراً — النتيجة + الجيد + المثالي + التشخيص التلقائي)",
            COL_BLUE,
        )
        tbl_panel.pack(fill="x", pady=(0, 8))

        headers = [
            ("diag", "ماذا يعني الرقم الآن؟ (تشخيص تلقائي)", 365),
            ("status", "التقييم", 96),
            ("good", "الجيد 🟡", 125),
            ("ideal", "المثالي 🟢", 125),
            ("actual", "نتيجتك الحالية", 136),
            ("metric", "المؤشر", 220),
            ("num", "#", 34),
        ]
        self.enj_tree = ttk.Treeview(tbl_panel, columns=tuple(c for c, _, _ in headers), show="headings", height=14)
        for c, txt, w in headers:
            self.enj_tree.heading(c, text=rtl(txt))
            self.enj_tree.column(c, width=w, anchor="e" if c in ("metric", "diag") else "center")

        self.enj_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.enj_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.enj_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.enj_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        self.enj_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.enj_tree)

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

    def _is_probe(self) -> bool:
        return "عيّنة" in self.source_var.get()

    def _active_ex(self) -> Dict[str, Any]:
        return self.store.excitement

    def refresh(self) -> None:
        self._compute_and_populate()
        self._draw_score_banner()
        self._draw_excitement_arc()
        self._draw_tiers_and_highlights()

    @staticmethod
    def _grade_range(val: float, ideal_lo: float, ideal_hi: float, good_lo: float, good_hi: float) -> Tuple[str, str, int]:
        if ideal_lo <= val <= ideal_hi:
            return ("🟢 مثالي", "ideal", 100)
        if good_lo <= val <= good_hi:
            return ("🟡 جيد", "good", 72)
        return ("🔴 يحتاج تعديل", "bad", 28)

    def _compute_and_populate(self) -> None:
        self.enj_tree.delete(*self.enj_tree.get_children())
        ex = self._active_ex()
        enj = ex.get("enjoyment", {})
        matches = int(enj.get("matches", 0))
        legacy_mode = False
        if matches <= 0 and int(ex.get("total_matches", 0)) > 0:
            matches = int(ex.get("total_matches", 0))
            legacy_mode = True

        if matches <= 0:
            self._last_metrics = {"has_data": False, "score": 0, "ideal_cnt": 0, "good_cnt": 0, "bad_cnt": 0}
            self.enj_tree.insert(
                "", "end",
                values=(
                    rtl("بانتظار بيانات التدريب — تظهر الأرقام تلقائياً فور بدء التدريب على المحاكي المحدّث أو الاتصال بالجهاز الثاني."),
                    rtl("بانتظار بيانات"), "--", "--", "--", rtl("تتبع المتعة والإثارة والقوس الدرامي جاهز ومفعّل"), "!",
                ),
                tags=("nodata",),
            )
            return

        if legacy_mode:
            avg_exc = float(ex.get("avg_excitement", 0.0))
            high_exc_pct = -1.0
            boring_pct = -1.0
            swings_pm = int(ex.get("total_lead_swings", 0)) / float(matches)
            with_swing_pct = -1.0
            tension_pct = -1.0
            comeback_pct = 100.0 * int(ex.get("comeback_wins", 0)) / matches
            clutch_pct = 100.0 * int(ex.get("clutch_wins", 0)) / matches
            blowout_pct = -1.0
            hl_map_l = ex.get("highlights", {})
            hl_pm = sum(int(v) for v in hl_map_l.values()) / float(matches)
            mk_pct = min(100.0, 100.0 * int(hl_map_l.get("Multi_Kill_Sweep", 0)) / float(matches))
        else:
            avg_exc = int(enj.get("exc_sum", 0)) / float(matches)
            high_exc_pct = 100.0 * int(enj.get("matches_high_exc", 0)) / matches
            boring_pct = 100.0 * int(enj.get("matches_boring", 0)) / matches
            swings_pm = int(enj.get("swings_sum", 0)) / float(matches)
            with_swing_pct = 100.0 * int(enj.get("matches_with_swing", 0)) / matches
            tension_pct = 100.0 * int(enj.get("close_hp_turns", 0)) / max(1, int(enj.get("total_hp_turns", 0)))

            comeback_pct = 100.0 * int(enj.get("comeback_100", 0)) / matches
            clutch_pct = 100.0 * int(enj.get("clutch_75", 0)) / matches
            blowout_pct = 100.0 * int(enj.get("blowout_325", 0)) / matches
            hl_pm = int(enj.get("highlights_sum", 0)) / float(matches)
            mk_pct = 100.0 * int(enj.get("multi_kill_matches", 0)) / matches

        # Compute Turn-by-Turn Excitement Arc (T1..T20) & Emotional Crescendo
        tc = ex.get("turn_curve", {})
        arc_pts: List[Tuple[int, float]] = []
        early_s, early_n = 0.0, 0
        late_s, late_n = 0.0, 0
        tc_turns_tot = 0
        tc_hempty_tot = 0
        for t in range(1, 21):
            b = tc.get(str(t), {})
            nt = int(b.get("turns", 0))
            if nt > 0:
                tc_turns_tot += nt
                tc_hempty_tot += int(b.get("hempty", 0))
                if int(b.get("exc_sum", 0)) > 0:
                    val = int(b.get("exc_sum", 0)) / float(nt)
                    es_v = float(b.get("exc_sum", 0))
                else:
                    # Legacy turn_curve proxy from real burst/swing/highlight density per turn
                    val = min(100.0, 15.0 + 18.0 * (int(b.get("kills", 0)) / float(nt)) + 65.0 * ((int(b.get("swings", 0)) + int(b.get("bursts", 0)) + int(b.get("highlights", 0))) / float(nt)))
                    es_v = val * nt
                arc_pts.append((t, val))
                if t <= 4:
                    early_s += es_v
                    early_n += nt
                elif t >= 12:
                    late_s += es_v
                    late_n += nt
        early_avg = (early_s / early_n) if early_n > 0 else 20.0
        late_avg = (late_s / late_n) if late_n > 0 else early_avg
        crescendo = late_avg - early_avg

        if legacy_mode:
            empty_h_pct = (100.0 * tc_hempty_tot / max(1, tc_turns_tot)) if tc_turns_tot > 0 else -1.0
            zero_imp_pct = -1.0
        else:
            empty_h_pct = 100.0 * int(enj.get("empty_hand_turns", 0)) / max(1, int(enj.get("player_turns_total", 0)))
            zero_imp_pct = 100.0 * int(enj.get("zero_imp_died", 0)) / max(1, int(enj.get("units_died_tot", 0)))

        rows: List[Tuple[int, str, str, str, str, str, str, str, int]] = []

        # 1. Avg Match Excitement (0-100)
        st, tg, pts = self._grade_range(avg_exc, 55.0, 88.0, 42.0, 95.0)
        diag = (
            f"مباريات مشوّقة جداً! متوسط الإثارة {avg_exc:.1f} / 100." if tg == "ideal"
            else f"الإثارة المتوسطة {avg_exc:.1f}/100 (أقل من المثالي 55 — تزيد برفع العودات والتقلّبات!)."
        )
        rows.append((1, "متوسط مؤشر الإثارة العام للمباراة (0–100)", f"{avg_exc:.1f} / 100", "55 – 88", "42 – 95", st, diag, tg, pts))

        # 2. High-Excitement Matches (>=65)
        if high_exc_pct >= 0:
            st, tg, pts = self._grade_range(high_exc_pct, 45.0, 100.0, 28.0, 100.0)
            diag = (
                f"{high_exc_pct:.1f}% من المباريات حماسية أو ملحمية (>=65 نقطة)!" if tg == "ideal"
                else f"{high_exc_pct:.1f}% فقط من المباريات تصل لمستوى حماس عالٍ (>=65)."
            )
            rows.append((2, "نسبة المباريات الحماسية والملحمية (>=65)", f"{high_exc_pct:.1f}%", "45% فأعلى", "28% – 44%", st, diag, tg, pts))
        else:
            rows.append((2, "نسبة المباريات الحماسية والملحمية (>=65)", "بانتظار تدريب محدّث", "45% فأعلى", "28% – 44%", "🟡 جزئي", "يُحسب تلقائياً عند التدريب على المحاكي المحدّث.", "nodata", 72))

        # 3. Boring Matches (<35)
        if boring_pct >= 0:
            st, tg, pts = self._grade_range(boring_pct, 0.0, 12.0, 0.0, 24.0)
            diag = (
                f"انضباط ممتع! المباريات الرتيبة (<35) لا تتجاوز {boring_pct:.1f}%." if tg == "ideal"
                else f"{boring_pct:.1f}% من المباريات باردة ورتيبة (<35 نقطة إثارة)!"
            )
            rows.append((3, "نسبة المباريات الرتيبة والمملة (<35)", f"{boring_pct:.1f}%", "12% فأقل", "12% – 24%", st, diag, tg, pts))
        else:
            rows.append((3, "نسبة المباريات الرتيبة والمملة (<35)", "بانتظار تدريب محدّث", "12% فأقل", "12% – 24%", "🟡 جزئي", "يُحسب تلقائياً عند التدريب على المحاكي المحدّث.", "nodata", 72))

        # 4. Lead Swings / Match
        st, tg, pts = self._grade_range(swings_pm, 1.2, 3.8, 0.6, 5.0)
        diag = (
            f"تقلّبات صدارة ممتعة ({swings_pm:.2f} قلب طاولة في كل مباراة بالمتوسط)." if tg == "ideal"
            else (f"قلّة تقلّبات الصدارة ({swings_pm:.2f}/مباراة — مَن يتقدم أولاً غالباً يظل متقدماً للنهاية!)." if swings_pm < 1.2 else "تقلّبات مفرطة وعشوائية.")
        )
        rows.append((4, "متوسط تقلّبات الصدارة بالمباراة (Lead Swings)", f"{swings_pm:.2f} / مباراة", "1.2 – 3.8", "0.6 – 5.0", st, diag, tg, pts))

        # 5. Matches with >=1 Lead Swing %
        if with_swing_pct >= 0:
            st, tg, pts = self._grade_range(with_swing_pct, 55.0, 92.0, 38.0, 100.0)
            diag = (
                f"ديناميكية رائعة! {with_swing_pct:.1f}% من المباريات تشهد انقلاباً حقيقياً بالنتيجة." if tg == "ideal"
                else f"{with_swing_pct:.1f}% فقط من المباريات تشهد تقلباً واحداً على الأقل بالصدارة."
            )
            rows.append((5, "نسبة المباريات ذات قلب الطاولة (>=1 تقلّب)", f"{with_swing_pct:.1f}%", "55% – 92%", "38% – 100%", st, diag, tg, pts))
        else:
            rows.append((5, "نسبة المباريات ذات قلب الطاولة (>=1 تقلّب)", "بانتظار تدريب محدّث", "55% – 92%", "38% – 100%", "🟡 جزئي", "يُحسب تلقائياً عند التدريب على المحاكي المحدّث.", "nodata", 72))

        # 6. Active Tension Time % (turns with |HP0 - HP1| <= 100)
        if tension_pct >= 0:
            st, tg, pts = self._grade_range(tension_pct, 45.0, 78.0, 32.0, 88.0)
            diag = (
                f"ندية مستمرة! في {tension_pct:.1f}% من كل أدوار المباراة يبقى الفارق <=100 HP والنتيجة معلّقة." if tg == "ideal"
                else f"الفارق يتّسع مبكراً (الندية المستمرة {tension_pct:.1f}% فقط من وقت المباراة)."
            )
            rows.append((6, "زمن النديّة والتوتر الفعلي (فارق <=100 HP)", f"{tension_pct:.1f}%", "45% – 78%", "32% – 88%", st, diag, tg, pts))
        else:
            rows.append((6, "زمن النديّة والتوتر الفعلي (فارق <=100 HP)", "بانتظار تدريب محدّث", "45% – 78%", "32% – 88%", "🟡 جزئي", "يُحسب تلقائياً عند التدريب على المحاكي المحدّث.", "nodata", 72))

        # 7. Comeback Wins (>=100 HP deficit overcome)
        st, tg, pts = self._grade_range(comeback_pct, 14.0, 36.0, 8.0, 48.0)
        diag = (
            f"الأمل حيّ دائماً! {comeback_pct:.1f}% من الانتصارات عودات بطولية بعد تأخر >=100 HP." if tg == "ideal"
            else (f"العودة من التأخر صعبة ({comeback_pct:.1f}% فقط)." if comeback_pct < 14.0 else "كثرة العودات المبالغ فيها قد تُضعف أهمية البداية.")
        )
        rows.append((7, "نسبة العودات البطولية (تعويض >=100 HP)", f"{comeback_pct:.1f}%", "14% – 36%", "8% – 48%", st, diag, tg, pts))

        # 8. Clutch Finishes (<=75 HP)
        st, tg, pts = self._grade_range(clutch_pct, 12.0, 32.0, 6.0, 44.0)
        diag = (
            f"نهايات تخطف الأنفاس ({clutch_pct:.1f}% تنتهي والمنتصر عند <=75 HP!)." if tg == "ideal"
            else f"النهايات المتقاربة جداً (<=75 HP) قليلة ({clutch_pct:.1f}% فقط)."
        )
        rows.append((8, "نسبة النهايات المشدودة للنفس الأخير (<=75 HP)", f"{clutch_pct:.1f}%", "12% – 32%", "6% – 44%", st, diag, tg, pts))

        # 9. Blowout Crushes (>=325 HP)
        if blowout_pct >= 0:
            st, tg, pts = self._grade_range(blowout_pct, 0.0, 18.0, 0.0, 30.0)
            diag = (
                f"انخفاض المباريات المحبطة أحادية الطرف ({blowout_pct:.1f}% فقط فوق 325 HP)." if tg == "ideal"
                else f"نصف المباريات تقريباً ({blowout_pct:.1f}%) هزائم ساحقة تحبط الطرف الخاسر!"
            )
            rows.append((9, "نسبة الهزائم الساحقة المحبطة (>=325 HP)", f"{blowout_pct:.1f}%", "18% فأقل", "18% – 30%", st, diag, tg, pts))
        else:
            rows.append((9, "نسبة الهزائم الساحقة المحبطة (>=325 HP)", "بانتظار تدريب محدّث", "18% فأقل", "18% – 30%", "🟡 جزئي", "يُحسب تلقائياً عند التدريب على المحاكي المحدّث.", "nodata", 72))

        # 10. WOW Highlights / Match
        st, tg, pts = self._grade_range(hl_pm, 1.5, 5.5, 0.8, 8.0)
        diag = (
            f"لحظات إبهار غنية ({hl_pm:.2f} لحظة WOW بالمباراة: ارتقاء، إنقاذ، مسح جماعي، كسر عهد)." if tg == "ideal"
            else ("لحظات الإبهار قليلة نسبياً." if hl_pm < 1.5 else "كثرة مفرطة للحظات الانفجار.")
        )
        rows.append((10, "معدل لحظات الإبهار (WOW Highlights / Match)", f"{hl_pm:.2f} / مباراة", "1.5 – 5.5", "0.8 – 8.0", st, diag, tg, pts))

        # 11. Multi-Kill Sweep Matches (>=3 kills in 1 turn)
        st, tg, pts = self._grade_range(mk_pct, 15.0, 50.0, 8.0, 68.0)
        diag = (
            f"لحظات مسح جماعي مُرضية جداً في {mk_pct:.1f}% من المباريات." if tg == "ideal"
            else ("نادراً ما يحدث دور مسح جماعي مثير." if mk_pct < 15.0 else "أدوار المسح الجماعي متكررة بزيادة.")
        )
        rows.append((11, "نسبة المباريات ذات المسح الجماعي (>=3 قتلى/دور)", f"{mk_pct:.1f}%", "15% – 50%", "8% – 68%", st, diag, tg, pts))

        # 12. Emotional Crescendo (Late Excitement - Early Excitement)
        st, tg, pts = self._grade_range(crescendo, 10.0, 38.0, 4.0, 50.0)
        diag = (
            f"قوس درامي مثالي! الإثارة تتصاعد من {early_avg:.0f} بالبداية إلى {late_avg:.0f} بالنهاية ({crescendo:+.1f})." if tg == "ideal"
            else f"تصاعد الإثارة ({crescendo:+.1f}: من {early_avg:.0f} إلى {late_avg:.0f}) يحتاج شحذاً أكبر في النهاية."
        )
        rows.append((12, "تصاعد الحماس الدرامي (النهاية − البداية)", f"{crescendo:+.1f} نقطة", "+10 إلى +38", "+4 إلى +50", st, diag, tg, pts))

        # 13. Empty-Hand Frustration Turns %
        if empty_h_pct >= 0:
            st, tg, pts = self._grade_range(empty_h_pct, 0.0, 8.0, 0.0, 16.0)
            diag = (
                f"انعدام إحباط فراغ اليد ({empty_h_pct:.1f}% فقط من الأدوار تنتهي بيد فارغة)." if tg == "ideal"
                else f"إحباط ملحوظ! في {empty_h_pct:.1f}% من الأدوار يجد اللاعب يده فارغة تماماً (0 أوراق)!"
            )
            rows.append((13, "كاشف الإحباط: أدوار فراغ اليد (0 أوراق)", f"{empty_h_pct:.1f}%", "8% فأقل", "8% – 16%", st, diag, tg, pts))
        else:
            rows.append((13, "كاشف الإحباط: أدوار فراغ اليد (0 أوراق)", "بانتظار تدريب محدّث", "8% فأقل", "8% – 16%", "🟡 جزئي", "يُحسب تلقائياً عند التدريب على المحاكي المحدّث.", "nodata", 72))

        # 14. Zero-Impact Instant-Death Frustration %
        if zero_imp_pct >= 0:
            st, tg, pts = self._grade_range(zero_imp_pct, 0.0, 15.0, 0.0, 24.0)
            diag = (
                f"تجربة استدعاء مُرضية! {100-zero_imp_pct:.1f}% من الوحوش تترك أثراً قبل موتها." if tg == "ideal"
                else f"إحباط! {zero_imp_pct:.1f}% من الوحوش تموت فوراً دون أن تترك أي أثر!"
            )
            rows.append((14, "كاشف الإحباط: موت الوحوش بلا أثر (0-Impact)", f"{zero_imp_pct:.1f}%", "15% فأقل", "15% – 24%", st, diag, tg, pts))
        else:
            rows.append((14, "كاشف الإحباط: موت الوحوش بلا أثر (0-Impact)", "بانتظار تدريب محدّث", "15% فأقل", "15% – 24%", "🟡 جزئي", "يُحسب تلقائياً عند التدريب على المحاكي المحدّث.", "nodata", 72))

        scored_rows = [r for r in rows if r[7] != "nodata"]
        avg_score = int(round(sum(r[8] for r in scored_rows) / max(1, len(scored_rows))))
        ideal_cnt = sum(1 for r in rows if r[7] == "ideal")
        good_cnt = sum(1 for r in rows if r[7] == "good")
        bad_cnt = sum(1 for r in rows if r[7] == "bad")

        hl_map = ex.get("highlights", {})
        top_hl = sorted(
            [(HL_AR.get(k, k), int(v)) for k, v in hl_map.items() if int(v) > 0],
            key=lambda x: x[1],
            reverse=True,
        )[:5]

        self._last_metrics = {
            "has_data": True,
            "score": avg_score,
            "ideal_cnt": ideal_cnt,
            "good_cnt": good_cnt,
            "bad_cnt": bad_cnt,
            "matches": matches,
            "arc_pts": arc_pts,
            "early_avg": early_avg,
            "late_avg": late_avg,
            "exc_hist": [int(x) for x in enj.get("exc_hist", [0] * 5)],
            "top_hl": top_hl,
        }

        for num, m_name, act_s, ideal_s, good_s, st_lbl, diag_s, tag, _ in rows:
            self.enj_tree.insert(
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
            cv.create_text(w // 2, h // 2, text=rtl("بانتظار بيانات التدريب الجديدة للمتعة والإثارة — ابدأ التدريب أو اتصل بالجهاز الثاني!"), fill=COL_GOLD, font=(FONT_UI, 11, "bold"))
            return

        sc = int(d["score"])
        if sc >= 85:
            col, badge = COL_GREEN, "🟢 متعة استثنائية وقوس درامي مشوّق يخطف الأنفاس (AAA)"
        elif sc >= 70:
            col, badge = COL_GOLD, "🟡 متعة جيدة (راجع المؤشرات الحمراء لتقليل المباريات الساحقة ورفع العودات)"
        else:
            col, badge = COL_RED, "🔴 برود درامي أو إحباط ملحوظ (راجع التقلّبات والهزائم الساحقة!)"

        cv.create_text(w - 12, 18, text=rtl(f"التقييم العام لمعيار المتعة والإثارة:  {sc} / 100%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 14, "bold"))
        cv.create_text(w - 12, 44, text=rtl(badge), anchor="e", fill=col, font=(FONT_UI, 10, "bold"))
        cv.create_text(
            w - 12, 66,
            text=rtl(f"المعيار: 🟢 >=85% مثالي | 🟡 70–84% جيد | 🔴 <70% خطر  •  (المباريات المحللة: {d['matches']:,})"),
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
    # Chart 1: Turn-by-Turn Excitement Curve (T1 -> T20)
    # ------------------------------------------------------------------
    def _draw_excitement_arc(self) -> None:
        cv = self.cv_arc
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(210, cv.winfo_height())

        d = self._last_metrics
        pts = d.get("arc_pts") or []
        if not d.get("has_data") or len(pts) < 2:
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات منحنى الإثارة بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        left_x, right_x = 38, w - 18
        top_y, bot_y = 26, h - 28
        plot_w = max(180, right_x - left_x)
        plot_h = max(100, bot_y - top_y)

        cv.create_text(
            w - 12, 11,
            text=rtl(f"البداية (T1–4): {d['early_avg']:.0f}  ←  النهاية (T12+): {d['late_avg']:.0f}  (تصاعد {d['late_avg']-d['early_avg']:+.1f})"),
            anchor="e", fill=COL_GOLD, font=(FONT_UI, 8, "bold"),
        )

        # Horizontal grid (0, 25, 50, 75, 100)
        for gv in (0, 25, 50, 75, 100):
            gy = bot_y - int(plot_h * gv / 100.0)
            cv.create_line(left_x, gy, right_x, gy, fill="#222A3A")
            cv.create_text(left_x - 6, gy, text=str(gv), anchor="e", fill=FG_MUTED, font=(FONT_UI, 7))

        # RTL x-mapping: Turn 1 on RIGHT, Turn 20 on LEFT
        max_t = max(20, pts[-1][0])
        coords: List[Tuple[float, float, int, float]] = []
        for t, val in pts:
            cx = right_x - plot_w * (t - 1) / float(max_t - 1)
            cy = bot_y - plot_h * min(100.0, max(0.0, val)) / 100.0
            coords.append((cx, cy, t, val))

        # Soft area polygon under curve
        poly = [coords[0][0], bot_y]
        for cx, cy, _, _ in coords:
            poly.extend([cx, cy])
        poly.extend([coords[-1][0], bot_y])
        cv.create_polygon(poly, fill="#1E293B", outline="")

        # Crisp glowing line + points
        for i in range(len(coords) - 1):
            x0, y0, _, _ = coords[i]
            x1, y1, _, v1 = coords[i + 1]
            lcol = COL_GREEN if v1 >= 50 else (COL_CYAN if v1 >= 30 else COL_BLUE)
            cv.create_line(x0, y0, x1, y1, fill=lcol, width=2.5)
        for idx_c, (cx, cy, t, val) in enumerate(coords):
            pcol = COL_GOLD if val >= 55 else COL_CYAN
            cv.create_oval(cx - 3, cy - 3, cx + 3, cy + 3, fill=pcol, outline="")
            if t in (1, 5, 10, 15, 20) or idx_c == len(coords) - 1:
                cv.create_text(cx, bot_y + 12, text=f"T{t}", fill=FG_MUTED, font=(FONT_UI, 7, "bold"))
            if t in (1, 6, 12, 18):
                cv.create_text(cx, cy - 10, text=f"{val:.0f}", fill=FG_TEXT, font=(FONT_UI, 7, "bold"))

    # ------------------------------------------------------------------
    # Chart 2: Excitement Tier Distribution + Top 5 WOW Highlights
    # ------------------------------------------------------------------
    def _draw_tiers_and_highlights(self) -> None:
        cv = self.cv_tiers
        cv.delete("all")
        w = max(340, cv.winfo_width())
        h = max(210, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات توزيع متعة بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        eh = d["exc_hist"]
        tot = max(1, sum(eh))
        tiers = [
            ("ملحمية (80+)", eh[4], COL_GREEN),
            ("حماسية (65–79)", eh[3], COL_CYAN),
            ("ممتعة (50–64)", eh[2], COL_BLUE),
            ("هادئة (35–49)", eh[1], COL_GOLD),
            ("مملة (<35)", eh[0], COL_RED),
        ]
        lbl_x = w - 10
        bar_r = w - 108
        bar_l = 58
        bar_w = max(60, bar_r - bar_l)
        for i, (lbl, cnt, col) in enumerate(tiers):
            y = 10 + i * 21
            pct = 100.0 * cnt / tot
            cv.create_text(lbl_x, y + 7, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8))
            cv.create_rectangle(bar_l, y + 2, bar_r, y + 12, fill="#242C3D", outline="")
            fw = max(2, int(bar_w * min(100.0, pct) / 100.0)) if cnt > 0 else 0
            if fw > 0:
                cv.create_rectangle(bar_r - fw, y + 2, bar_r, y + 12, fill=col, outline="")
            cv.create_text(bar_l - 6, y + 7, text=f"{pct:4.1f}%", anchor="e", fill=col, font=(FONT_UI, 8, "bold"))

        # Bottom: Top 5 WOW Highlights
        cv.create_line(10, 120, w - 10, 120, fill=BG_BORDER)
        cv.create_text(w - 10, 132, text=rtl("أكثر لحظات الإبهار (WOW Highlights) تكراراً:"), anchor="e", fill=COL_GOLD, font=(FONT_UI, 8, "bold"))
        top_hl = d.get("top_hl") or []
        if not top_hl:
            cv.create_text(w // 2, 175, text=rtl("لا توجد لحظات مسجلة بعد."), fill=FG_MUTED, font=(FONT_UI, 8))
            return
        max_h = max(1, top_hl[0][1])
        br2 = w - 156
        bl2 = 48
        bw2 = max(50, br2 - bl2)
        for i, (lbl, cnt) in enumerate(top_hl[:5]):
            y = 146 + i * 19
            cv.create_text(w - 10, y + 6, text=rtl(lbl[:22]), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8))
            cv.create_rectangle(bl2, y + 2, br2, y + 11, fill="#242C3D", outline="")
            fw = max(2, int(bw2 * cnt / float(max_h)))
            cv.create_rectangle(br2 - fw, y + 2, br2, y + 11, fill=COL_PURPLE, outline="")
            cv.create_text(bl2 - 6, y + 6, text=f"{cnt:,}", anchor="e", fill=COL_CYAN, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Shared 80-Match Non-Destructive Live Probe
    # ------------------------------------------------------------------
    def _start_live_probe(self) -> None:
        if self._probe_running:
            return
        self._probe_running = True
        self.probe_btn.config(state="disabled", text=rtl("جاري تشغيل 80 مباراة..."))
        self.probe_status_var.set(rtl("جاري قياس المتعة والإثارة والقوس الدرامي عبر 80 مباراة حية..."))
        threading.Thread(target=self._run_probe_worker, daemon=True).start()

    def _run_probe_worker(self) -> None:
        try:
            from engine.ai_brain import ModelBrain
            from engine.battle_engine import BattleEngine

            candidates = [f"free_{i:02d}" for i in range(1, 11)] + ["style_alpha", "style_bastion", "style_pyro", "style_arcane"]
            agents, decks = {}, {}
            saved_dir = os.path.abspath(os.path.join(self.store.logs_dir, "..", "saved_models"))
            for mid in candidates:
                dp = os.path.join(saved_dir, mid, "settled_deck.json")
                if os.path.isfile(dp):
                    with open(dp, "r", encoding="utf-8") as f:
                        cids = json.load(f).get("deck_50_ids") or []
                    if len(cids) == 50:
                        decks[mid] = [int(x) for x in cids]
                        agents[mid] = ModelBrain.load_or_create(mid, self.db, use_warm_start=False)

            mids = list(agents.keys())
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
                GamePaceTab.accumulate_probe_result(ex, eng.get_result())
                if (i + 1) % 20 == 0:
                    self.parent.after(0, lambda k=i + 1: self.probe_status_var.set(rtl(f"الفحص المباشر: {k}/{n_matches} مباراة...")))

            # Preserve existing skill audit if present in old snapshot
            if self.probe_data and self.probe_data.get("strategic_intel", {}).get("ai_vs_random_wr", -1) >= 0:
                for sk in ("policy_entropy_pct", "ai_vs_random_wr", "ai_vs_random_n", "opt_vs_noisy_wr", "opt_vs_noisy_n", "decision_regret_pct"):
                    ex["strategic_intel"][sk] = self.probe_data["strategic_intel"][sk]

            ex["probe_ts"] = time.strftime("%Y-%m-%d %H:%M:%S")
            ex["probe_version"] = fresh_db.version
            tmp = self.probe_path + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(ex, f, indent=2, ensure_ascii=False)
            os.replace(tmp, self.probe_path)

            def _done_ok() -> None:
                self._probe_running = False
                self.probe_btn.config(state="normal", text=rtl("تشغيل فحص مباشر (80 مباراة)"))
                self.source_var.set(rtl("عيّنة الفحص المباشر (قوانين V2.2 الحالية)"))
                if self.on_probe_complete:
                    self.on_probe_complete()
                else:
                    self.refresh()

            self.parent.after(0, _done_ok)
        except Exception as err:
            def _done_err(msg: str = str(err)) -> None:
                self._probe_running = False
                self.probe_btn.config(state="normal", text=rtl("تشغيل فحص مباشر (80 مباراة)"))
                self.probe_status_var.set(rtl(f"خطأ بالفحص: {msg[:50]}"))
            self.parent.after(0, _done_err)

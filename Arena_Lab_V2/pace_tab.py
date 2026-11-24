# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Pillar #1: Game Pace Scorecard Tab (Arabic RTL).
Every indicator displays:
  - Actual Measured Value
  - Ideal Range (🟢)
  - Good / Acceptable Range (🟡)
  - Color-Coded Status Verdict
  - Plain-Arabic Automatic Diagnosis explaining what the number means right now!
Plus an overall 0-100% Pace Score (>=85% Ideal, >=70% Good, <70% Warning)
and 2 crisp RTL vector charts (Match Length Histogram + Match Pace Timeline).
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


def rtl(text: str) -> str:
    if not text:
        return ""
    return f"\u202B{text}\u202C"


def _hist_percentile(hist: Dict[str, Any], q: float) -> float:
    pairs = sorted((int(k), int(v)) for k, v in hist.items() if int(v) > 0)
    if not pairs:
        return 0.0
    total = sum(v for _, v in pairs)
    target = max(1.0, total * q)
    acc = 0
    for val, cnt in pairs:
        acc += cnt
        if acc >= target:
            return float(val)
    return float(pairs[-1][0])


class GamePaceTab:
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

        # Auto-select probe if main logs don't have Pillar #1 detailed pace data yet
        has_main_pace = int(self.store.excitement.get("game_pace", {}).get("matches", 0)) > 0
        has_probe_pace = bool(self.probe_data and int(self.probe_data.get("game_pace", {}).get("matches", 0)) > 0)
        default_src = (
            rtl("عيّنة الفحص المباشر (قوانين V2.2 الحالية)")
            if (has_probe_pace and not has_main_pace)
            else rtl("سجلات التدريب الكاملة (كل المباريات)")
        )
        self.source_var = tk.StringVar(value=default_src)
        self.sec_per_rnd_var = tk.StringVar(value=rtl("40 ثانية / جولة (قياسي)"))
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
        # 1. Top RTL Control Bar (Direct Training Logs — No Sample Mode!)
        ctrl = tk.Frame(self.parent, bg=BG_PANEL, padx=12, pady=7)
        ctrl.pack(fill="x", padx=8, pady=(6, 4))

        tk.Label(ctrl, text=rtl("سرعة اللعب البشري لتقدير مدة المباراة:"), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold")).pack(side="right", padx=(4, 0))
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
            width=22,
        )
        pace_cb.pack(side="right", padx=(10, 6))
        pace_cb.bind("<<ComboboxSelected>>", lambda _e: self.refresh())

        tk.Label(
            ctrl, textvariable=self.probe_status_var, bg=BG_PANEL, fg=COL_CYAN,
            font=(FONT_UI, 8, "bold"), anchor="w", justify="left",
        ).pack(side="left", padx=6)

        # 2. Scrollable Body (scrollbar on LEFT for RTL)
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
        self.scroll_canvas.bind_all("<MouseWheel>", self._on_mousewheel)

        # --- Hero Score Banner (0 - 100% Pace Score + Clear Passing Standards!) ---
        hero = tk.Frame(self.inner, bg=BG_CARD, highlightbackground=BG_BORDER, highlightthickness=1, padx=14, pady=10)
        hero.pack(fill="x", pady=(2, 8))

        self.cv_score = tk.Canvas(hero, bg=BG_CARD, height=82, highlightthickness=0)
        self.cv_score.pack(fill="x")
        self.cv_score.bind("<Configure>", lambda _e: self._draw_score_banner())

        # --- Self-Explanatory 14-Indicator Diagnostic Table (Full Width First!) ---
        tbl_panel = self._make_panel(
            self.inner,
            "بطاقة التقييم التفصيلية لمعيار إيقاع المباراة (14 مؤشراً — النتيجة + الجيد + المثالي + التشخيص التلقائي)",
            COL_GREEN,
        )
        tbl_panel.pack(fill="x", pady=(0, 8))

        # RTL Columns: Rightmost = #, Indicator Name -> Result -> Ideal -> Good -> Status -> Auto Diagnosis (Leftmost wide)
        headers = [
            ("diag", "ماذا يعني الرقم الآن؟ (تشخيص تلقائي)", 365),
            ("status", "التقييم", 96),
            ("good", "الجيد 🟡", 125),
            ("ideal", "المثالي 🟢", 125),
            ("actual", "نتيجتك الحالية", 132),
            ("metric", "المؤشر", 220),
            ("num", "#", 34),
        ]
        cols = tuple(c for c, _, _ in headers)
        self.pace_tree = ttk.Treeview(tbl_panel, columns=cols, show="headings", height=14)
        for c, txt, w in headers:
            self.pace_tree.heading(c, text=rtl(txt))
            self.pace_tree.column(c, width=w, anchor="e" if c in ("metric", "diag") else "center")

        self.pace_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.pace_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.pace_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.pace_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        self.pace_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.pace_tree)

        # --- Visual Charts Stacked Vertically at Full Screen Width ---
        p_time = self._make_panel(self.inner, "الخط الزمني لإيقاع المباراة (متى تبدأ الأحداث ومتى يُحسم التفوق؟)", COL_CYAN)
        p_time.pack(fill="x", pady=(0, 8))
        self.cv_timeline = tk.Canvas(p_time, bg=BG_PANEL, height=240, highlightthickness=0)
        self.cv_timeline.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_timeline.bind("<Configure>", lambda _e: self._draw_pace_timeline())

        p_hist = self._make_panel(self.inner, "توزيع أطوال المباريات (مع تظليل النطاق المثالي 11–16 جولة والوسيط)", COL_BLUE)
        p_hist.pack(fill="x", pady=(0, 8))
        self.cv_hist = tk.Canvas(p_hist, bg=BG_PANEL, height=240, highlightthickness=0)
        self.cv_hist.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_hist.bind("<Configure>", lambda _e: self._draw_length_histogram())

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

    def _sec_per_round(self) -> int:
        s = self.sec_per_rnd_var.get()
        if "30" in s:
            return 30
        if "50" in s:
            return 50
        return 40

    def refresh(self) -> None:
        m = int(self.store.excitement.get("game_pace", {}).get("matches", 0))
        tot = int(self.store.excitement.get("total_matches", 0))
        self.probe_status_var.set(rtl(f"بيانات التدريب الحقيقية مباشرة • إيقاع مفصّل: {m:,} مباراة (إجمالي السجل: {tot:,}) • اضغط أي عنوان عمود للترتيب"))

        self._compute_and_populate()
        self._draw_score_banner()
        self._draw_length_histogram()
        self._draw_pace_timeline()

    @staticmethod
    def _grade_range(val: float, ideal_lo: float, ideal_hi: float, good_lo: float, good_hi: float) -> Tuple[str, str, int]:
        """Returns (status_label, tag, points_out_of_100)."""
        if ideal_lo <= val <= ideal_hi:
            return ("🟢 مثالي", "ideal", 100)
        if good_lo <= val <= good_hi:
            return ("🟡 جيد", "good", 72)
        return ("🔴 يحتاج تعديل", "bad", 28)

    def _compute_and_populate(self) -> None:
        self.pace_tree.delete(*self.pace_tree.get_children())
        ex = self._active_ex()
        gp = ex.get("game_pace", {})
        m = int(gp.get("matches", 0))
        th = gp.get("turn_hist", {})
        sec_r = self._sec_per_round()

        if m <= 0 or not th:
            self._last_metrics = {"has_data": False, "score": 0, "ideal_cnt": 0, "good_cnt": 0, "bad_cnt": 0, "matches": 0}
            self.pace_tree.insert(
                "", "end",
                values=(
                    rtl("بانتظار بيانات التدريب — تظهر الأرقام تلقائياً فور بدء التدريب على المحاكي المحدّث أو الاتصال بالجهاز الثاني."),
                    rtl("بانتظار بيانات"), "--", "--", "--", rtl("تتبع إيقاع المباراة جاهز ومفعّل"), "!",
                ),
                tags=("nodata",),
            )
            return

        # Exact statistics from turn_hist
        tot_hist_m = sum(int(v) for v in th.values())
        mean_r = sum(int(k) * int(v) for k, v in th.items()) / max(1, tot_hist_m)
        med_r = _hist_percentile(th, 0.50)
        p10_r = _hist_percentile(th, 0.10)
        p90_r = _hist_percentile(th, 0.90)
        min_r = int(gp.get("min_turn", min(int(k) for k in th.keys())))
        max_r = int(gp.get("max_turn", max(int(k) for k in th.keys())))
        in_8_18 = 100.0 * sum(int(v) for k, v in th.items() if 8 <= int(k) <= 18) / max(1, tot_hist_m)

        est_sec = int(round(mean_r * sec_r))
        est_m, est_s = divmod(est_sec, 60)

        f_sum = float(gp.get("first_summon_sum", 0)) / max(1, int(gp.get("first_summon_cnt", 0))) if int(gp.get("first_summon_cnt", 0)) > 0 else 0.0
        f_atk = float(gp.get("first_attack_sum", 0)) / max(1, int(gp.get("first_attack_cnt", 0))) if int(gp.get("first_attack_cnt", 0)) > 0 else 0.0
        f_die = float(gp.get("first_death_sum", 0)) / max(1, int(gp.get("first_death_cnt", 0))) if int(gp.get("first_death_cnt", 0)) > 0 else 0.0
        f_def = float(gp.get("first_defense_sum", 0)) / max(1, int(gp.get("first_defense_cnt", 0))) if int(gp.get("first_defense_cnt", 0)) > 0 else 0.0

        tot_pt = max(1, int(gp.get("total_player_turns", 0)))
        zero_card_pct = 100.0 * int(gp.get("zero_card_turns", 0)) / tot_pt
        dead_pct = 100.0 * int(gp.get("dead_turns", 0)) / tot_pt
        avg_acts = float(gp.get("total_actions", 0)) / tot_pt

        imp_cnt = int(gp.get("impact_gaps_cnt", 0))
        avg_gap = (float(gp.get("impact_gaps_sum", 0)) / imp_cnt) if imp_cnt > 0 else 0.0
        sd40_pct = 100.0 * int(gp.get("max_turn_40_cnt", 0)) / max(1, m)

        dec_cnt = int(gp.get("decisive_cnt", 0))
        dec_turn = (float(gp.get("decisive_turn_sum", 0)) / dec_cnt) if dec_cnt > 0 else 0.0
        dec_ratio_pct = (100.0 * float(gp.get("decisive_ratio_sum", 0.0)) / dec_cnt) if dec_cnt > 0 else 0.0

        # Build all 14 rows with Ideal, Good, Verdict, Points, and Dynamic Plain-Arabic Diagnosis!
        rows: List[Tuple[int, str, str, str, str, str, str, str, int]] = []

        # 1. Mean Rounds
        st, tg, pts = self._grade_range(mean_r, 11.0, 16.0, 9.0, 20.0)
        diag = (
            f"إيقاع ممتاز جداً (~{est_m}:{est_s:02d} دقيقة للعب البشري)." if tg == "ideal"
            else (f"المباريات أطول من المثالي ({mean_r:.1f} جولة ≈ {est_m}:{est_s:02d} د) — فكّر في تسريع الحسم." if mean_r > 16.0
                  else f"المباريات سريعة جداً ({mean_r:.1f} جولة) — قد لا تلحق الاستراتيجيات العميقة تتطور.")
        )
        rows.append((1, "متوسط أدوار المباراة والوقت", f"{mean_r:.1f} ج ({est_m}:{est_s:02d}د)", "11 – 16 جولة", "9 – 20 جولة", st, diag, tg, pts))

        # 2. Median Rounds
        st, tg, pts = self._grade_range(med_r, 11.0, 15.0, 9.0, 19.0)
        diag = (
            "الوسيط متطابق مع الإيقاع المثالي ومستقر بعيداً عن الشذوذ." if tg == "ideal"
            else ("معظم المباريات تميل للطول؛ الوسيط مرتفع فوق 15 جولة." if med_r > 15.0 else "معظم المباريات تنتهي بسرعة زائدة قبل نضوج المعركة.")
        )
        rows.append((2, "وسيط عدد الأدوار (Median)", f"{med_r:.0f} جولة", "11 – 15 جولة", "9 – 19 جولة", st, diag, tg, pts))

        # 3. Min / Max & P10-P90
        if min_r >= 7 and p90_r <= 22:
            st, tg, pts = "🟢 مثالي", "ideal", 100
            diag = f"حدود متماسكة جداً (80% من المباريات بين جولة {p10_r:.0f} و {p90_r:.0f})."
        elif min_r >= 5 and p90_r <= 28:
            st, tg, pts = "🟡 جيد", "good", 72
            diag = f"مقبول لكن يوجد بعض المباريات المتطرفة (أدنى {min_r}، P90 عند {p90_r:.0f}، أقصى {max_r})."
        else:
            st, tg, pts = "🔴 يحتاج تعديل", "bad", 28
            diag = f"تفاوت كبير في طول المباريات (أقصر {min_r} ← أطول {max_r}، وP90 = {p90_r:.0f})."
        rows.append((3, "أقصر وأطول مباراة (Min/Max • P90)", f"{min_r}–{max_r} (P90={p90_r:.0f})", "أدنى>=7 | P90<=22", "أدنى>=5 | P90<=28", st, diag, tg, pts))

        # 4. Distribution concentration (8-18 rounds)
        st, tg, pts = self._grade_range(in_8_18, 70.0, 100.0, 50.0, 100.0)
        diag = (
            f"تركّز ممتاز! {in_8_18:.0f}% من المباريات تنتهي في المنطقة الممتعة (8–18 جولة)." if tg == "ideal"
            else (f"نصف المباريات فقط في النطاق المثالي ({in_8_18:.0f}%) والباقي يمتد أطول أو أسرع." if tg == "good"
                  else f"معظم المباريات ({100-in_8_18:.0f}%) خارج النطاق الصحي 8–18 جولة!")
        )
        rows.append((4, "تركّز المباريات بالنطاق الصحي (8–18ج)", f"{in_8_18:.1f}%", "70% فأعلى", "50% – 69%", st, diag, tg, pts))

        # 5. First Monster Summon
        st, tg, pts = self._grade_range(f_sum, 1.0, 1.5, 1.0, 2.2)
        diag = (
            "دخول فوري ومثالي للمعركة منذ الجولة الأولى!" if tg == "ideal"
            else "تأخر في نزول أول وحش — راجع عدد وحوش Tier 1–2 في الديكات."
        )
        rows.append((5, "الدور الأول لأول استدعاء وحش", f"جولة {f_sum:.2f}", "1.0 – 1.5", "1.0 – 2.2", st, diag, tg, pts))

        # 6. First Attack
        st, tg, pts = self._grade_range(f_atk, 1.5, 2.5, 1.0, 3.2)
        diag = (
            "بداية اشتباك قتالي مثالية بعد جولة التأسيس مباشرة." if tg == "ideal"
            else ("الاشتباك يبدأ متأخراً قليلاً — البداية باردة." if f_atk > 2.5 else "الهجوم سريع جداً منذ الجولة 1 (انتشار Haste مفرط).")
        )
        rows.append((6, "الدور الأول لأول هجوم فعلي", f"جولة {f_atk:.2f}", "1.5 – 2.5", "1.0 – 3.2", st, diag, tg, pts))

        # 7. First Unit Death
        st, tg, pts = self._grade_range(f_die, 2.5, 4.2, 1.8, 5.5)
        diag = (
            "أول سقوط لوحش يحدث بتوقيت تكتيكي سليم (لا هو فوري ولا بطيء)." if tg == "ideal"
            else ("الوحوش تبدأ بالموت مبكراً جداً (قبل الجولة 2.5) — ضغط المهارات/الهجوم عالٍ." if f_die < 2.5 else "أول موت متأخر جداً — بداية المعركة بطيئة.")
        )
        rows.append((7, "الدور الأول لأول موت وحش", f"جولة {f_die:.2f}", "2.5 – 4.2", "1.8 – 5.5", st, diag, tg, pts))

        # 8. First Defensive Interaction
        st, tg, pts = self._grade_range(f_def, 1.5, 4.0, 1.0, 6.0)
        diag = (
            "تفاعل دفاعي ممتاز (استدراج / شفاء / تجميد / حاجز) منذ بداية المعركة." if tg == "ideal"
            else ("التفاعل الدفاعي متأخر — النماذج تلعب هجوم أعمى أكثر من الدفاع." if f_def > 4.0 else "تفاعل دفاعي فوري منذ الجولة الأولى.")
        )
        rows.append((8, "الدور الأول لأول تفاعل دفاعي", f"جولة {f_def:.2f}", "1.5 – 4.0", "1.0 – 6.0", st, diag, tg, pts))

        # 9. Zero-Card Turns %
        st, tg, pts = self._grade_range(zero_card_pct, 0.0, 8.0, 0.0, 15.0)
        diag = (
            "خيارات اليد نشطة؛ نادراً ما يمر دور دون لعب بطاقة." if tg == "ideal"
            else ("نسبة متوسطة من الأدوار بلا بطاقة (بسبب غلاء التكلفة أو نفاد اليد)." if tg == "good"
                  else f"تحذير! {zero_card_pct:.1f}% من الأدوار لا يُلعب فيها أي كارت — اللاعب يشعر بالتعطّل!")
        )
        rows.append((9, "نسبة الأدوار بلا لعب أي كارت", f"{zero_card_pct:.1f}%", "8% فأقل", "8% – 15%", st, diag, tg, pts))

        # 10. True Dead Turns % (0 actions at all!)
        st, tg, pts = self._grade_range(dead_pct, 0.0, 2.0, 0.0, 6.0)
        diag = (
            "ممتاز! شبه انعدام للأدوار الميتة تماماً (كل دور فيه بناء أو قتال)." if tg == "ideal"
            else ("بعض الأدوار الميتة البسيطة — مقبول حالياً." if tg == "good"
                  else f"خطر ملل! {dead_pct:.1f}% من الأدوار لا يحدث فيها أي فعل نهائياً (لا لعب ولا مهارة ولا هجوم)!")
        )
        rows.append((10, "نسبة الأدوار الميتة تماماً (بلا أي فعل)", f"{dead_pct:.1f}%", "2% فأقل", "2% – 6%", st, diag, tg, pts))

        # 11. Avg Actions per Turn
        st, tg, pts = self._grade_range(avg_acts, 2.5, 5.2, 1.8, 6.8)
        diag = (
            f"كثافة لعب ممتعة جداً ({avg_acts:.2f} أفعال لكل دور بين لعب ومهارات وهجوم)." if tg == "ideal"
            else ("الأدوار فقيرة بالأفعال — اللاعب يفعل حركة واحدة وينهي الدور." if avg_acts < 2.5 else "كثافة أفعال عالية جداً في الدور الواحد.")
        )
        rows.append((11, "متوسط الأفعال في الدور الواحد", f"{avg_acts:.2f} فعل/دور", "2.5 – 5.2", "1.8 – 6.8", st, diag, tg, pts))

        # 12. Avg Gap Between Impact Events
        st, tg, pts = self._grade_range(avg_gap, 0.8, 1.5, 0.5, 2.2)
        diag = (
            "إيقاع أحداث ناري! كل جولة تقريباً فيها حدث مؤثر (قتل أو ضرر كبير أو تقلب)." if tg == "ideal"
            else "توجد فجوات هدوء طويلة نسبياً بين الأحداث المؤثرة."
        )
        rows.append((12, "متوسط الفجوة بين حدثين مؤثرين", f"{avg_gap:.2f} جولة", "0.8 – 1.5 جولة", "0.5 – 2.2 جولة", st, diag, tg, pts))

        # 13. Reaching Turn 40+ Sudden Death %
        st, tg, pts = self._grade_range(sd40_pct, 0.0, 1.0, 0.0, 3.5)
        diag = (
            "ممتاز! المباريات تُحسم طبيعياً دون الوصول لركود عقوبة الجولة 40." if tg == "ideal"
            else ("ظهور حالات ركود نادرة تصل للجولة 40." if tg == "good"
                  else f"إنذار ركود! {sd40_pct:.1f}% من المباريات تفشل في الحسم وتصل لعقوبة الجولة 40!")
        )
        rows.append((13, "نسبة الوصول لعقوبة الجولة +40", f"{sd40_pct:.1f}%", "1% فأقل", "1% – 3.5%", st, diag, tg, pts))

        # 14. Decisive Lead Turn (% of Match)
        st, tg, pts = self._grade_range(dec_ratio_pct, 55.0, 82.0, 42.0, 92.0)
        diag = (
            f"توتر رائع! التفوق الحاسم لا يثبت إلا عند جولة {dec_turn:.1f} ({dec_ratio_pct:.0f}% من عمر المباراة)." if tg == "ideal"
            else (f"تحذير حسم مبكر! الفائز يضمن التقدم منذ {dec_ratio_pct:.0f}% فقط من المباراة (باقي الجولات وقت ضائع)." if dec_ratio_pct < 55.0
                  else f"حسم في اللحظات الأخيرة جداً ({dec_ratio_pct:.0f}% من عمر المباراة — إثارة عالية!).")
        )
        rows.append((14, "جولة بداية التفوق الحاسم (+60 HP)", f"ج{dec_turn:.1f} ({dec_ratio_pct:.0f}%)", "55% – 82%", "42% – 92%", st, diag, tg, pts))

        # Compute Overall Pace Score (0 - 100%)
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
            "matches": m,
            "turn_hist": th,
            "mean_r": mean_r,
            "med_r": med_r,
            "p10_r": p10_r,
            "p90_r": p90_r,
            "f_sum": f_sum,
            "f_atk": f_atk,
            "f_def": f_def,
            "f_die": f_die,
            "dec_turn": dec_turn,
        }

        for num, m_name, act_s, ideal_s, good_s, st_lbl, diag_s, tag, _ in rows:
            self.pace_tree.insert(
                "", "end",
                values=(rtl(diag_s), rtl(st_lbl), rtl(good_s), rtl(ideal_s), rtl(act_s), rtl(m_name), num),
                tags=(tag,),
            )

    # ------------------------------------------------------------------
    # Hero Score Banner (0 - 100% + Passing Grade Guide)
    # ------------------------------------------------------------------
    def _draw_score_banner(self) -> None:
        cv = self.cv_score
        cv.delete("all")
        w = max(700, cv.winfo_width())
        h = max(76, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("بانتظار بيانات التدريب الجديدة لإيقاع المباراة — ابدأ التدريب أو اتصل بالجهاز الثاني!"), fill=COL_GOLD, font=(FONT_UI, 11, "bold"))
            return

        sc = int(d["score"])
        if sc >= 85:
            col, badge = COL_GREEN, "🟢 إيقاع مثالي (جاهز لمستوى AAA)"
        elif sc >= 70:
            col, badge = COL_GOLD, "🟡 إيقاع جيد ومقبول (يحتاج ضبط المؤشرات الصفراء/الحمراء)"
        else:
            col, badge = COL_RED, "🔴 إيقاع يحتاج إعادة ضبط (راجع المؤشرات الحمراء بالجدول)"

        # Right side: Big Score & Verdict
        cv.create_text(w - 12, 18, text=rtl(f"التقييم العام لمعيار إيقاع المباراة:  {sc} / 100%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 14, "bold"))
        cv.create_text(w - 12, 44, text=rtl(badge), anchor="e", fill=col, font=(FONT_UI, 10, "bold"))
        cv.create_text(
            w - 12, 66,
            text=rtl(f"المعيار المعتمد للنجاح:  🟢 85% فأعلى = مثالي   |   🟡 70%–84% = جيد ومقبول   |   🔴 أقل من 70% = مشكلة إيقاع   •   (العينة: {d['matches']:,} مباراة)"),
            anchor="e", fill=FG_MUTED, font=(FONT_UI, 8),
        )

        # Left side: Visual Gauge Bar + Breakdown Pills
        bar_l, bar_r = 16, min(370, int(w * 0.36))
        bar_w = max(140, bar_r - bar_l)
        # Background track + 70% & 85% threshold ticks!
        cv.create_rectangle(bar_l, 16, bar_r, 34, fill="#242C3D", outline=BG_BORDER)
        fw = int(bar_w * min(100, sc) / 100.0)
        if fw > 0:
            cv.create_rectangle(bar_r - fw, 16, bar_r, 34, fill=col, outline="")
        # Threshold vertical markers (70% = Good, 85% = Ideal from right)
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
    # Chart 1: Match Length Distribution Histogram (with Ideal Band + Median)
    # ------------------------------------------------------------------
    def _draw_length_histogram(self) -> None:
        cv = self.cv_hist
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(195, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات توزيع بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        th = d["turn_hist"]
        max_bin = min(40, max(22, max(int(k) for k in th.keys())))
        counts = [int(th.get(str(t), 0)) for t in range(1, max_bin + 1)]
        tot = max(1, sum(counts))
        max_c = max(1, max(counts))

        pad_l, pad_r, pad_t, pad_b = 18, 36, 30, 34
        pw = max(60, w - pad_l - pad_r)
        ph = max(60, h - pad_t - pad_b)
        step = pw / float(max_bin)

        # RTL x: Turn 1 on RIGHT, Turn max_bin on LEFT
        def bx(turn: int) -> float:
            return (pad_l + pw) - (turn - 0.5) * step

        # Shaded Ideal Zone (Turns 11..16)
        x_i_right = (pad_l + pw) - 10 * step
        x_i_left = (pad_l + pw) - min(max_bin, 16) * step
        cv.create_rectangle(x_i_left, pad_t, x_i_right, pad_t + ph, fill="#122B22", outline="#1D4D3B", dash=(3, 2))
        cv.create_text((x_i_left + x_i_right) / 2, pad_t + 10, text=rtl("المثالي 11–16"), fill=COL_GREEN, font=(FONT_UI, 7, "bold"))

        # Bars
        for t in range(1, max_bin + 1):
            c = counts[t - 1]
            cx = bx(t)
            bw = max(4.0, step * 0.72)
            if c > 0:
                bh = max(3, int(ph * c / float(max_c)))
                col = COL_GREEN if (11 <= t <= 16) else (COL_RED if (t <= 6 or t >= 28) else COL_BLUE)
                cv.create_rectangle(cx - bw / 2, pad_t + ph - bh, cx + bw / 2, pad_t + ph, fill=col, outline="")
            if t == 1 or t % 3 == 0 or t == max_bin:
                cv.create_text(cx, pad_t + ph + 13, text=f"ج{t}", fill=FG_MUTED, font=(FONT_UI, 7))

        # Median vertical line
        med_r = d["med_r"]
        if 1 <= med_r <= max_bin:
            mx = bx(int(round(med_r)))
            cv.create_line(mx, pad_t, mx, pad_t + ph, fill=COL_GOLD, width=2, dash=(4, 2))
            cv.create_text(mx, pad_t - 9, text=rtl(f"الوسيط: ج{med_r:.0f}"), fill=COL_GOLD, font=(FONT_UI, 8, "bold"))

        cv.create_text(pad_l + pw, 10, text=rtl(f"المتوسط: {d['mean_r']:.1f} جولة  |  P10–P90: ج{d['p10_r']:.0f} – ج{d['p90_r']:.0f}"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Chart 2: Match Pace Timeline Milestones (RTL Right -> Left)
    # ------------------------------------------------------------------
    def _draw_pace_timeline(self) -> None:
        cv = self.cv_timeline
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(195, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات خط زمني بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        end_r = max(6.0, float(d["mean_r"]))
        pad_l, pad_r = 34, 34
        line_y = h // 2 + 4
        pw = max(100, w - pad_l - pad_r)

        def tx(r_val: float) -> float:
            frac = max(0.0, min(1.0, (r_val - 0.5) / max(1.0, end_r - 0.5)))
            return (pad_l + pw) - pw * frac

        # Base timeline bar
        cv.create_line(pad_l, line_y, pad_l + pw, line_y, fill="#334155", width=5)
        cv.create_text(pad_l + pw, line_y + 72, text=rtl("بداية المباراة (جولة 1)"), anchor="e", fill=FG_MUTED, font=(FONT_UI, 8))
        cv.create_text(pad_l, line_y + 72, text=rtl(f"متوسط النهاية (ج{end_r:.1f})"), anchor="w", fill=FG_MUTED, font=(FONT_UI, 8))

        # Highlight "Contested Phase" (Green) vs "Decisive Endgame Phase" (Gold)
        x_dec = tx(d["dec_turn"])
        cv.create_line(x_dec, line_y, pad_l + pw, line_y, fill=COL_CYAN, width=5)
        cv.create_line(pad_l, line_y, x_dec, line_y, fill=COL_GOLD, width=5)

        milestones = [
            (d["f_sum"], "أول استدعاء", COL_BLUE, -1, 46),
            (d["f_atk"], "أول هجوم", COL_CYAN, +1, 42),
            (d["f_def"], "أول دفاع", COL_GREEN, -1, 76),
            (d["f_die"], "أول موت وحش", COL_RED, +1, 72),
            (d["dec_turn"], "التفوق الحاسم", COL_GOLD, -1, 46),
            (end_r, "النهاية", COL_PURPLE, +1, 42),
        ]

        for r_val, lbl, col, dir_y, dy in milestones:
            if r_val <= 0:
                continue
            mx = tx(r_val)
            ly = line_y + dir_y * dy
            cv.create_line(mx, line_y, mx, ly - dir_y * 12, fill=col, width=1.5, dash=(2, 2))
            cv.create_oval(mx - 6, line_y - 6, mx + 6, line_y + 6, fill=col, outline=BG_PANEL, width=2)
            # Clamp badge horizontally so edges never clip
            bx = min(w - 58, max(58, mx))
            cv.create_rectangle(bx - 52, ly - 12, bx + 52, ly + 12, fill=BG_CARD, outline=col, width=1)
            cv.create_text(bx, ly, text=rtl(f"{lbl}: ج{r_val:.1f}"), fill=FG_TEXT, font=(FONT_UI, 7, "bold"))

    # ------------------------------------------------------------------
    # Live 80-Match Non-Destructive Probe (Records Full Pillar #1 Metrics!)
    # ------------------------------------------------------------------
    def _start_live_probe(self) -> None:
        if self._probe_running:
            return
        self._probe_running = True
        self.probe_btn.config(state="disabled", text=rtl("جاري تشغيل 80 مباراة..."))
        self.probe_status_var.set(rtl("جاري فحص إيقاع المباراة على القوانين الحالية..."))
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
                raise RuntimeError("تحتاج نموذجين مدرّبين على الأقل.")

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
                self.accumulate_probe_result(ex, res)
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

    @staticmethod
    def accumulate_probe_result(ex: Dict[str, Any], result: Any) -> None:
        tm = int(ex["total_matches"])
        t0, t1 = result.telemetry[0], result.telemetry[1]
        rt = int(result.turns)
        match_exc = max(t0.excitement_score, t1.excitement_score)
        ex["total_matches"] = tm + 1
        ex["total_turns_sum"] = int(ex["total_turns_sum"]) + rt
        ex["avg_excitement"] = round((float(ex["avg_excitement"]) * tm + match_exc) / (tm + 1), 2)
        ex["total_lead_swings"] = int(ex["total_lead_swings"]) + max(t0.lead_swings, t1.lead_swings)

        if t0.went_first or t1.went_first:
            p1_side = 0 if t0.went_first else 1
            if result.winner in (0, 1):
                ex["p1_games"] += 1
                if result.winner == p1_side:
                    ex["p1_wins"] += 1

        gp = ex["game_pace"]
        gp["matches"] += 1
        th = gp["turn_hist"]
        tk_h = str(max(1, rt))
        th[tk_h] = int(th.get(tk_h, 0)) + 1
        gp["min_turn"] = rt if (gp["min_turn"] <= 0 or rt < gp["min_turn"]) else gp["min_turn"]
        gp["max_turn"] = max(gp["max_turn"], rt)
        if rt >= 40:
            gp["max_turn_40_cnt"] += 1
        if t0.pace_first_summon > 0:
            gp["first_summon_sum"] += int(t0.pace_first_summon)
            gp["first_summon_cnt"] += 1
        if t0.pace_first_attack > 0:
            gp["first_attack_sum"] += int(t0.pace_first_attack)
            gp["first_attack_cnt"] += 1
        if t0.pace_first_death > 0:
            gp["first_death_sum"] += int(t0.pace_first_death)
            gp["first_death_cnt"] += 1
        if t0.pace_first_defense > 0:
            gp["first_defense_sum"] += int(t0.pace_first_defense)
            gp["first_defense_cnt"] += 1
        if t0.pace_decisive_turn > 0 and rt > 0:
            gp["decisive_turn_sum"] += int(t0.pace_decisive_turn)
            gp["decisive_ratio_sum"] += min(1.0, float(t0.pace_decisive_turn) / float(rt))
            gp["decisive_cnt"] += 1

        ms, kc, ds = ex["monster_survival"], ex["kill_causes"], ex["dmg_sources"]
        for st in (t0, t1):
            gp["zero_card_turns"] += int(st.pace_zero_card_turns)
            gp["dead_turns"] += int(st.pace_dead_turns)
            gp["total_player_turns"] += int(st.hand_turns)
            gp["total_actions"] += int(st.pace_total_actions)
            gp["impact_gaps_sum"] += int(st.pace_impact_gaps_sum)
            gp["impact_gaps_cnt"] += int(st.pace_impact_gaps_cnt)

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
            ms["empty_board_turns"] += st.empty_board_turns
            ms["dmg_absorbed_sum"] += st.unit_dmg_absorbed_sum
            ms["base_def_sum"] += st.unit_base_def_sum
            ms["zero_impact_died"] += st.unit_zero_impact_died
            ms["attacks_died_sum"] += st.unit_attacks_died_sum
            ms["skill_holders_died"] += st.unit_skill_holders_died
            ms["skills_died_sum"] += st.unit_skills_died_sum
            ms["first_impact_wait_sum"] += st.unit_first_impact_wait_sum
            ms["first_impact_cnt"] += st.unit_first_impact_cnt
            ms["exits_sacrifice"] += st.exits_sacrifice
            ms["exits_recalled"] += st.exits_recalled
            for bi in range(7):
                ms["life_hist"][bi] += st.unit_life_hist[bi]
            for idx_a in range(8):
                ms["tier_sum"][idx_a] += st.unit_tier_sum[idx_a]
                ms["tier_cnt"][idx_a] += st.unit_tier_cnt[idx_a]
            for idx_a in range(6):
                ms["role_sum"][idx_a] += st.unit_role_sum[idx_a]
                ms["role_cnt"][idx_a] += st.unit_role_cnt[idx_a]
            for idx_a in range(10):
                ms["tribe_sum"][idx_a] += st.unit_tribe_sum[idx_a]
                ms["tribe_cnt"][idx_a] += st.unit_tribe_cnt[idx_a]
                ms["elem_sum"][idx_a] += st.unit_elem_sum[idx_a]
                ms["elem_cnt"][idx_a] += st.unit_elem_cnt[idx_a]

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

            cq = ex["combat_quality"]
            cq["unit_dmg_dealt_sum"] += st.cq_unit_dmg_dealt_sum
            cq["hits_to_kill_sum"] += st.cq_hits_to_kill_sum
            cq["total_strikes"] += st.cq_total_strikes
            cq["unit_clashes"] += st.cq_unit_clashes
            cq["face_strikes"] += st.cq_face_strikes
            cq["lethal_strikes"] += st.cq_lethal_strikes
            cq["whiff_strikes"] += st.cq_whiff_strikes
            cq["intercepted_strikes"] += st.cq_intercepted_strikes
            cq["mutual_kills"] += st.cq_mutual_kills
            cq["fair_trades"] += st.cq_fair_trades
            cq["clean_kills"] += st.cq_clean_kills
            cq["swing_strikes"] += st.cq_swing_strikes
            cq["overkill_dmg"] += st.cq_overkill_dmg
            cq["raw_unit_dmg"] += st.cq_raw_unit_dmg
            cq["barrier_dmg_absorbed"] += st.cq_barrier_dmg_absorbed
            cq["near_death_hits"] += st.cq_near_death_hits
            cq["total_unit_hits"] += st.cq_total_unit_hits
            cq["skill_dmg_total"] += st.cq_skill_dmg_total
            cq["actives_used_total"] += st.actives_used
            cq["total_healed"] += st.hp_healed + st.def_healed
            cq["total_face_dmg"] += st.dmg_to_face
            cq["total_monster_dmg"] += st.dmg_to_monsters

            cu = ex["card_utilization"]
            cu["sides_count"] += 1
            cu["drawn_total"] += st.cards_drawn
            cu["played_total"] += st.cu_played_count
            cu["sacrificed_total"] += st.sacrifices_made
            cu["burned_hand7_total"] += st.cards_burned_hand7
            cu["hand_end_remaining"] += st.cu_hand_end_remaining
            cu["played_on_draw_turn"] += st.cu_played_on_draw_turn
            cu["play_turn_sum"] += st.cu_play_turn_sum
            cu["cond_attempts"] += st.cu_cond_attempts
            cu["cond_success"] += st.cu_cond_success
            cu["hand_checks_cards"] += st.cu_hand_checks_cards
            cu["hand_unplayable_cards"] += st.cu_hand_unplayable_cards
            cu["low_value_plays"] += st.cu_low_value_plays
            cu["deck_cycles_done"] += st.cu_deck_cycles_done
            cu["impact_value_sum"] += (
                st.dmg_to_face + st.dmg_to_monsters + st.hp_healed + st.def_healed
                + 12 * st.monsters_killed + 8 * st.disruptions_applied
            )
            for ti_c in range(6):
                cu["played_by_type"][ti_c] += st.cu_played_by_type[ti_c]

        re_d = ex["resource_economy"]
        for side_i, st in enumerate((t0, t1)):
            re_d["sides_count"] += 1
            re_d["mana_avail"] += st.re_mana_avail
            re_d["mana_spent"] += st.re_mana_spent
            re_d["mana_wasted"] += st.re_mana_wasted
            re_d["turns_total"] += st.hand_turns
            re_d["turns_unspent"] += st.re_turns_unspent
            re_d["early_mana_avail"] += st.re_early_mana_avail
            re_d["early_mana_spent"] += st.re_early_mana_spent
            re_d["mana_spent_cards"] += st.re_mana_spent_cards
            re_d["mana_spent_monsters"] += st.re_mana_spent_monsters
            re_d["mana_spent_spells"] += st.re_mana_spent_spells
            re_d["mana_spent_skills"] += st.re_mana_spent_skills
            re_d["mana_spent_other"] += st.re_mana_spent_other
            re_d["mana_offense"] += st.re_mana_offense
            re_d["mana_defense"] += st.re_mana_defense
            re_d["sacrifices_total"] += st.sacrifices_made
            re_d["impact_value_sum"] += (
                st.dmg_to_face + st.dmg_to_monsters + st.hp_healed + st.def_healed
                + 12 * st.monsters_killed + 8 * st.disruptions_applied
            )
            if st.re_reach_cap_turn > 0:
                re_d["reach_cap_sum"] += st.re_reach_cap_turn
                re_d["reach_cap_cnt"] += 1
            is_win = (result.winner == side_i)
            for ci in range(8):
                cnt_c = st.re_card_cost_played[ci]
                re_d["card_cost_played"][ci] += cnt_c
                if is_win:
                    re_d["card_cost_wins"][ci] += cnt_c
            for ti_m in range(15):
                re_d["turn_mana_avail"][ti_m] += st.re_turn_mana_avail[ti_m]
                re_d["turn_mana_spent"][ti_m] += st.re_turn_mana_spent[ti_m]
                re_d["turn_mana_cnt"][ti_m] += st.re_turn_mana_cnt[ti_m]

        mp = ex["mechanics_perf"]
        mp["matches"] += 1
        m_flags = [False] * 9
        for side_i, st in enumerate((t0, t1)):
            mp["sides"] += 1
            is_w = (result.winner == side_i)
            if is_w:
                mp["wins_total"] += 1
            s_vals = [
                st.actives_used,
                st.spells_cast,
                st.cu_played_by_type[1],
                st.cu_played_by_type[3],
                st.cu_played_by_type[4],
                st.cu_played_by_type[5],
                st.dot_dmg_dealt,
                st.disruptions_applied,
                st.tokens_spawned + st.clones_made + st.revives_done + st.grave_returns,
            ]
            for ki in range(9):
                v_k = s_vals[ki]
                mp["sys_total_cnt"][ki] += v_k
                if v_k > 0:
                    mp["sys_used_sides"][ki] += 1
                    m_flags[ki] = True
                    if is_w:
                        mp["sys_used_wins"][ki] += 1
            mp["env_upgrades"] += st.mp_env_upgrades
            mp["passive_triggers"] += st.mp_passive_triggers
            mp["ascends"] += st.mp_ascends
            mp["last_breaths"] += st.mp_last_breaths
            mp["reflect_dmg"] += st.reflect_dmg_dealt
            mp["evasions"] += st.evasions_triggered
            mp["dot_dmg"] += st.dot_dmg_dealt
            mp["total_dmg"] += st.dmg_to_face + st.dmg_to_monsters
            if st.sacrifices_made >= 3:
                mp["sac_ramp_sides"] += 1
                if is_w:
                    mp["sac_ramp_wins"] += 1
            else:
                mp["sac_low_sides"] += 1
                if is_w:
                    mp["sac_low_wins"] += 1
        for ki in range(9):
            if m_flags[ki]:
                mp["match_sys_present"][ki] += 1
        mp["active_systems_sum"] += sum(1 for f in m_flags if f)

        vc = ex["victory_cond"]
        vc["matches"] += 1
        for st in (t0, t1):
            for ci_v in range(6):
                vc["face_by_ch"][ci_v] += st.vc_face_by_ch[ci_v]
        if rt <= 12:
            vc["aggro_wins"] += 1
        elif rt <= 22:
            vc["midrange_wins"] += 1
        elif rt <= 39:
            vc["control_wins"] += 1
        else:
            vc["sudden_death_wins"] += 1
        if result.win_reason == "CovenantTurnLimit":
            vc["covenant_limit_wins"] += 1
        if result.winner in (0, 1):
            vc["decisive_matches"] += 1
            wt_v = result.telemetry[result.winner]
            kc_i = max(0, min(5, int(wt_v.vc_kill_channel)))
            vc["kill_by_ch"][kc_i] += 1
            if wt_v.vc_end_board_cnt >= 2:
                vc["board_dom_wins"] += 1
            if wt_v.final_hp >= 325:
                vc["blowout_wins"] += 1
            if 0 < wt_v.final_hp <= 100:
                vc["clutch_100_wins"] += 1
            if wt_v.comeback_deficit >= 100:
                vc["comeback_100_wins"] += 1
            if wt_v.vc_final_turn_face_dmg >= 100:
                vc["burst_finish_wins"] += 1

        si = ex["strategic_intel"]
        for st in (t0, t1):
            si["turns_total"] += st.hand_turns
            si["legal_actions_sum"] += st.si_legal_actions_sum
            si["distinct_choices_sum"] += st.si_distinct_choices_sum
            si["forced_pass_turns"] += st.si_forced_pass_turns
            si["multi_action_turns"] += st.si_multi_action_turns
            si["decision_chain_sum"] += st.si_decision_chain_sum
            si["sac_avail_turns"] += st.si_sac_avail_turns
            si["sac_taken_total"] += st.sacrifices_made
            si["restraint_passes"] += st.si_restraint_passes
            si["reactive_decisions"] += st.si_reactive_decisions
            si["total_decisions"] += st.si_total_decisions
            si["played_total"] += st.cu_played_count
            si["planned_plays"] += max(0, st.cu_played_count - st.cu_played_on_draw_turn)
            for k_si in range(6):
                si["choice_hist"][k_si] += st.si_choice_hist[k_si]
                si["slot_summons"][k_si] += st.si_slot_summons[k_si]

        # Pillar #10: Gameplay Enjoyment + Turn Excitement Curve + Highlights
        enj = ex["enjoyment"]
        enj["matches"] += 1
        enj["exc_sum"] += match_exc
        if match_exc < 35:
            enj["exc_hist"][0] += 1
            enj["matches_boring"] += 1
        elif match_exc < 50:
            enj["exc_hist"][1] += 1
        elif match_exc < 65:
            enj["exc_hist"][2] += 1
        elif match_exc < 80:
            enj["exc_hist"][3] += 1
            enj["matches_high_exc"] += 1
        else:
            enj["exc_hist"][4] += 1
            enj["matches_high_exc"] += 1
        m_sw = max(t0.lead_swings, t1.lead_swings)
        enj["swings_sum"] += m_sw
        if m_sw >= 1:
            enj["matches_with_swing"] += 1
        enj["close_hp_turns"] += t0.en_close_hp_turns + t1.en_close_hp_turns
        enj["total_hp_turns"] += t0.en_hp_turns + t1.en_hp_turns
        enj["highlights_sum"] += t0.highlight_triggers + t1.highlight_triggers
        if (t0.multi_kill_turns + t1.multi_kill_turns) >= 1:
            enj["multi_kill_matches"] += 1
        enj["empty_hand_turns"] += t0.empty_hand_turns + t1.empty_hand_turns
        enj["player_turns_total"] += t0.hand_turns + t1.hand_turns
        enj["zero_imp_died"] += t0.unit_zero_impact_died + t1.unit_zero_impact_died
        enj["units_died_tot"] += t0.units_died_count + t1.units_died_count
        if result.winner in (0, 1):
            wt_e = result.telemetry[result.winner]
            if wt_e.comeback_deficit >= 100:
                enj["comeback_100"] += 1
                ex["comeback_wins"] = int(ex.get("comeback_wins", 0)) + 1
            if wt_e.clutch_win:
                enj["clutch_75"] += 1
                ex["clutch_wins"] = int(ex.get("clutch_wins", 0)) + 1
            if wt_e.final_hp >= 325:
                enj["blowout_325"] += 1

        tc = ex["turn_curve"]
        for ev in getattr(result, "turn_events", []):
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
                b["hs"] += t_hs
                b["he"] += t_he
                b["exc_sum"] += t_exc
                b["board_sum"] += t_brd
                if t_he == 0 and len(ev) > 6:
                    b["hempty"] += 1
        fin_key = str(min(25, max(1, rt)))
        if fin_key in tc:
            tc[fin_key]["finishes"] += 1
        hl_map = ex["highlights"]
        for s_tel in (t0, t1):
            for htag, hcnt in s_tel.highlight_tags.items():
                hl_map[htag] = int(hl_map.get(htag, 0)) + int(hcnt)

        # Pillar #11: Competitive Fairness aggregation
        cf = ex["comp_fairness"]
        cf["matches"] += 1
        cf["evasions_total"] += t0.evasions_triggered + t1.evasions_triggered
        if t0.cf_t4_recorded:
            cf["t4_diff_sum"] += t0.cf_t4_hp_diff
            cf["t4_diff_cnt"] += 1
        if result.winner not in (0, 1):
            cf["draws"] += 1
        else:
            p1_s = 0 if t0.went_first else 1
            cf["p1_decisive"] += 1
            if result.winner == p1_s:
                cf["p1_wins"] += 1
            wt_f = result.telemetry[result.winner]
            lt_f = result.telemetry[1 - result.winner]
            w_mpt = wt_f.re_mana_avail / max(1.0, float(wt_f.hand_turns))
            l_mpt = lt_f.re_mana_avail / max(1.0, float(lt_f.hand_turns))
            cf["mana_gap_sum"] += (w_mpt - l_mpt)
            cf["mana_gap_cnt"] += 1
            cf["draw_gap_sum"] += (wt_f.cards_drawn - lt_f.cards_drawn)
            cf["draw_gap_cnt"] += 1
        for side_i, st_cur in enumerate((t0, t1)):
            won_s = (result.winner == side_i)
            if st_cur.cf_open_playable:
                cf["open_ok_sides"] += 1
                if won_s:
                    cf["open_ok_wins"] += 1
            else:
                cf["open_brick_sides"] += 1
                if won_s:
                    cf["open_brick_wins"] += 1
            if st_cur.cf_early_special:
                cf["early_spec_sides"] += 1
                if won_s:
                    cf["early_spec_wins"] += 1
            else:
                cf["no_spec_sides"] += 1
                if won_s:
                    cf["no_spec_wins"] += 1

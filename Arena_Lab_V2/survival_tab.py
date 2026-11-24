# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Pillar #2: Monster Survival & Battlefield Scorecard Tab (Arabic RTL).
Tracks all 16 survival & battlefield metrics with:
  - Overall 0-100% Survival Score (>=85% Ideal, >=70% Good, <70% Warning)
  - Strict separation of Killed Units vs Match-End Survivors + Exact Median
  - Interactive 4-Way Lifespan Breakdown Bar Chart (Cost Tier 1-7 | 6 Roles | 10 Tribes | 10 Elements)
  - 8-Source Exit Causes Donut & RTL Bars (Active Skills, Attacks, Spells, Counter, DoT, Passives, Sacrifice, Recalled)
  - Self-Explanatory 16-Row Diagnostic Table (Actual | Ideal | Good | Verdict | Auto-Diagnosis)
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
COL_SLATE = "#64748B"
COL_TEAL = "#14B8A6"
COL_ORANGE = "#F97316"

FONT_UI = "Segoe UI"

AR_ROLES = ["مهاجم", "قنّاص", "مدافع", "حاجز", "دعم", "معطّل"]
AR_ELEMENTS = ["النار", "الماء", "الطبيعة", "الكهرباء", "الجليد", "الأرض", "الظلام", "الضوء", "الهواء", "الصحراء"]
AR_TRIBES = ["السماويون", "الشياطين", "الأطياف", "السحرة", "المحاربون", "الأقزام", "العمالقة", "الإلف", "الوحوش", "الحشرات"]


def _hist7_median(hist: List[int]) -> float:
    tot = sum(int(x) for x in hist)
    if tot <= 0:
        return 0.0
    half = tot / 2.0
    acc = 0
    for idx, cnt in enumerate(hist):
        acc += int(cnt)
        if acc >= half:
            return float(idx)
    return float(len(hist) - 1)


class MonsterSurvivalTab:
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

        has_main = sum(int(x) for x in self.store.excitement.get("monster_survival", {}).get("life_hist", [])) > 0
        has_probe = bool(self.probe_data and sum(int(x) for x in self.probe_data.get("monster_survival", {}).get("life_hist", [])) > 0)
        default_src = (
            rtl("عيّنة الفحص المباشر (قوانين V2.2 الحالية)")
            if (has_probe and not has_main)
            else rtl("سجلات التدريب الكاملة (كل المباريات)")
        )
        self.source_var = tk.StringVar(value=default_src)
        self.breakdown_mode = tk.StringVar(value="tier")  # tier | role | tribe | elem
        self.probe_status_var = tk.StringVar(value="")

        self._last_metrics: Dict[str, Any] = {}
        self._mode_btns: Dict[str, tk.Button] = {}
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
        # 1. Scrollable Body (Direct Training Data — No Sample Bar!)
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

        # --- Hero Score Banner (0 - 100% Survival Score + Passing Guide) ---
        hero = tk.Frame(self.inner, bg=BG_CARD, highlightbackground=BG_BORDER, highlightthickness=1, padx=14, pady=10)
        hero.pack(fill="x", pady=(2, 8))

        self.cv_score = tk.Canvas(hero, bg=BG_CARD, height=82, highlightthickness=0)
        self.cv_score.pack(fill="x")
        self.cv_score.bind("<Configure>", lambda _e: self._draw_score_banner())

        # --- Self-Explanatory 16-Indicator Diagnostic Table (Full Width First!) ---
        tbl_panel = self._make_panel(
            self.inner,
            "بطاقة التقييم التفصيلية لمعيار بقاء الوحوش وساحة المعركة (16 مؤشراً — النتيجة + الجيد + المثالي + التشخيص التلقائي)",
            COL_GREEN,
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
        self.surv_tree = ttk.Treeview(tbl_panel, columns=tuple(c for c, _, _ in headers), show="headings", height=16)
        for c, txt, w in headers:
            self.surv_tree.heading(c, text=rtl(txt))
            self.surv_tree.column(c, width=w, anchor="e" if c in ("metric", "diag") else "center")

        self.surv_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.surv_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.surv_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.surv_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        self.surv_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.surv_tree)

        # --- Visual Charts Stacked Vertically at Full Screen Width ---
        p_brk = tk.Frame(self.inner, bg=BG_PANEL, highlightbackground=BG_BORDER, highlightthickness=1)
        p_brk.pack(fill="x", pady=(0, 8))
        hdr_b = tk.Frame(p_brk, bg=BG_PANEL, padx=10, pady=6)
        hdr_b.pack(fill="x")
        tk.Frame(hdr_b, bg=COL_GREEN, width=4, height=15).pack(side="right", padx=(7, 0))
        tk.Label(hdr_b, text=rtl("متوسط عمر الوحش المقتول حسب:"), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold")).pack(side="right", padx=(8, 0))

        for key, lbl in (("tier", "التكلفة T1-7"), ("role", "الأدوار 6"), ("tribe", "العشائر 10"), ("elem", "العناصر 10")):
            btn = tk.Button(
                hdr_b, text=rtl(lbl), command=lambda k=key: self._set_breakdown_mode(k),
                bg=COL_BLUE if key == "tier" else "#242C3D", fg="#FFFFFF",
                relief="flat", font=(FONT_UI, 8, "bold"), padx=8, pady=2, cursor="hand2",
            )
            btn.pack(side="right", padx=2)
            self._mode_btns[key] = btn

        self.cv_breakdown = tk.Canvas(p_brk, bg=BG_PANEL, height=260, highlightthickness=0)
        self.cv_breakdown.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_breakdown.bind("<Configure>", lambda _e: self._draw_breakdown_chart())

        p_exit = self._make_panel(self.inner, "أسباب خروج الوحوش من الساحة (موت / تضحية / إرجاع لليد)", COL_PURPLE)
        p_exit.pack(fill="x", pady=(0, 8))
        self.cv_exits = tk.Canvas(p_exit, bg=BG_PANEL, height=260, highlightthickness=0)
        self.cv_exits.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_exits.bind("<Configure>", lambda _e: self._draw_exit_causes())

    def _make_panel(self, parent: tk.Widget, title: str, accent: str) -> tk.Frame:
        f = tk.Frame(parent, bg=BG_PANEL, highlightbackground=BG_BORDER, highlightthickness=1)
        hdr = tk.Frame(f, bg=BG_PANEL, padx=10, pady=7)
        hdr.pack(fill="x")
        tk.Frame(hdr, bg=accent, width=4, height=15).pack(side="right", padx=(7, 0))
        tk.Label(hdr, text=rtl(title), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold"), anchor="e", justify="right").pack(side="right")
        return f

    def _set_breakdown_mode(self, mode: str) -> None:
        self.breakdown_mode.set(mode)
        for k, b in self._mode_btns.items():
            b.config(bg=COL_BLUE if k == mode else "#242C3D")
        self._draw_breakdown_chart()

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
        self._draw_breakdown_chart()
        self._draw_exit_causes()

    @staticmethod
    def _grade_range(val: float, ideal_lo: float, ideal_hi: float, good_lo: float, good_hi: float) -> Tuple[str, str, int]:
        if ideal_lo <= val <= ideal_hi:
            return ("🟢 مثالي", "ideal", 100)
        if good_lo <= val <= good_hi:
            return ("🟡 جيد", "good", 72)
        return ("🔴 يحتاج تعديل", "bad", 28)

    def _compute_and_populate(self) -> None:
        self.surv_tree.delete(*self.surv_tree.get_children())
        ex = self._active_ex()
        ms = ex.get("monster_survival", {})
        died_cnt = int(ms.get("died_cnt", 0))
        lh = ms.get("life_hist", [0] * 7)
        has_detailed = died_cnt > 0 and sum(int(x) for x in lh) > 0

        if not has_detailed:
            self._last_metrics = {"has_data": False, "score": 0, "ideal_cnt": 0, "good_cnt": 0, "bad_cnt": 0, "died_cnt": 0}
            self.surv_tree.insert(
                "", "end",
                values=(
                    rtl("بانتظار بيانات التدريب — تظهر الأرقام تلقائياً فور بدء التدريب على المحاكي المحدّث أو الاتصال بالجهاز الثاني."),
                    rtl("بانتظار بيانات"), "--", "--", "--", rtl("تتبع بقاء الوحوش المفصّل جاهز ومفعّل"), "!",
                ),
                tags=("nodata",),
            )
            return

        mean_life = float(ms.get("died_rounds", 0)) / died_cnt
        med_life = _hist7_median(lh)
        surv_1p = 100.0 * (died_cnt - int(lh[0])) / died_cnt
        surv_2p = 100.0 * sum(int(lh[i]) for i in range(2, 7)) / died_cnt
        surv_3p = 100.0 * sum(int(lh[i]) for i in range(3, 7)) / died_cnt

        # Tier progression: High (T5..7) vs Low (T1..2)
        t_sum, t_cnt = ms.get("tier_sum", [0] * 8), ms.get("tier_cnt", [0] * 8)
        low_c = int(t_cnt[1]) + int(t_cnt[2])
        high_c = int(t_cnt[5]) + int(t_cnt[6]) + int(t_cnt[7])
        low_avg = (int(t_sum[1]) + int(t_sum[2])) / max(1, low_c)
        high_avg = (int(t_sum[5]) + int(t_sum[6]) + int(t_sum[7])) / max(1, high_c)
        tier_diff = (high_avg - low_avg) if (low_c > 0 and high_c > 0) else 0.0

        # Role hierarchy: Tanks (Defender=2 + Barrier=3) vs Assassin (1)
        r_sum, r_cnt = ms.get("role_sum", [0] * 6), ms.get("role_cnt", [0] * 6)
        tank_c = int(r_cnt[2]) + int(r_cnt[3])
        ass_c = int(r_cnt[1])
        tank_avg = (int(r_sum[2]) + int(r_sum[3])) / max(1, tank_c)
        ass_avg = int(r_sum[1]) / max(1, ass_c)
        role_diff = (tank_avg - ass_avg) if (tank_c > 0 and ass_c > 0) else 0.0

        # Tribe & Element lifespan spread (max - min among those with >= 10 deaths)
        tr_sum, tr_cnt = ms.get("tribe_sum", [0] * 10), ms.get("tribe_cnt", [0] * 10)
        tr_avgs = [(int(tr_sum[i]) / int(tr_cnt[i]), AR_TRIBES[i]) for i in range(10) if int(tr_cnt[i]) >= 8]
        tr_spread = (max(x[0] for x in tr_avgs) - min(x[0] for x in tr_avgs)) if len(tr_avgs) >= 2 else 0.0
        tr_weak = min(tr_avgs, key=lambda x: x[0]) if tr_avgs else (0.0, "--")

        el_sum, el_cnt = ms.get("elem_sum", [0] * 10), ms.get("elem_cnt", [0] * 10)
        el_avgs = [(int(el_sum[i]) / int(el_cnt[i]), AR_ELEMENTS[i]) for i in range(10) if int(el_cnt[i]) >= 8]
        el_spread = (max(x[0] for x in el_avgs) - min(x[0] for x in el_avgs)) if len(el_avgs) >= 2 else 0.0
        el_weak = min(el_avgs, key=lambda x: x[0]) if el_avgs else (0.0, "--")

        # Absorbed Damage vs Base DEF
        abs_sum = int(ms.get("dmg_absorbed_sum", 0))
        bdef_sum = max(1, int(ms.get("base_def_sum", 0)))
        abs_pct = 100.0 * abs_sum / bdef_sum
        abs_avg = abs_sum / float(died_cnt)

        # Zero-impact death, 0-atk death, avg attacks, avg skills
        c_died = max(1, int(ms.get("combat_died", 0)))
        zi_pct = 100.0 * int(ms.get("zero_impact_died", 0)) / c_died
        z0_atk_pct = 100.0 * int(ms.get("combat_0atk", 0)) / c_died
        avg_atks = float(ms.get("attacks_died_sum", 0)) / c_died

        sk_holders = int(ms.get("skill_holders_died", 0))
        avg_sks = (float(ms.get("skills_died_sum", 0)) / sk_holders) if sk_holders > 0 else 0.0

        # Survivors separated, board density & empty board %
        surv_cnt = int(ms.get("surv_cnt", 0))
        surv_pct = 100.0 * surv_cnt / max(1, died_cnt + surv_cnt)
        surv_avg_r = (float(ms.get("surv_rounds", 0)) / surv_cnt) if surv_cnt > 0 else 0.0
        bt = max(1, int(ms.get("board_turns", 0)))
        avg_brd = float(ms.get("board_sum", 0)) / bt
        empty_brd_pct = 100.0 * int(ms.get("empty_board_turns", 0)) / bt

        # First impact wait
        fi_cnt = int(ms.get("first_impact_cnt", 0))
        avg_wait = (float(ms.get("first_impact_wait_sum", 0)) / fi_cnt) if fi_cnt > 0 else 0.0

        rows: List[Tuple[int, str, str, str, str, str, str, str, int]] = []

        # 1. Mean Lifespan (Killed only)
        st, tg, pts = self._grade_range(mean_life, 2.0, 3.2, 1.5, 4.0)
        diag = (
            "عمر مثالي! الوحوش تثبت بالملعب وتسمح بتكتيكات وبافات عميقة." if tg == "ideal"
            else ("الوحوش تموت أسرع من المطلوب (القتل فوري جداً — راجع ضرر المهارات النشطة!)." if mean_life < 2.0 else "الوحوش صلبة جداً وتطيل المعارك أكثر من اللازم.")
        )
        rows.append((1, "متوسط عمر الوحش المقتول (Mean)", f"{mean_life:.2f} جولة", "2.0 – 3.2 جولة", "1.5 – 4.0 جولة", st, diag, tg, pts))

        # 2. Median Lifespan
        st, tg, pts = self._grade_range(med_life, 2.0, 3.0, 1.0, 4.0)
        diag = (
            "نصف الوحوش على الأقل تعيش جولتين كاملتين أو أكثر — توازن ممتاز!" if tg == "ideal"
            else (f"الوسيط منخفض ({med_life:.0f} جولة) — أغلب الوحوش تسقط خلال جولة واحدة أو فوراً!" if med_life < 2.0 else "الوسيط مرتفع — الملعب مزدحم لفترة طويلة.")
        )
        rows.append((2, "وسيط عمر الوحش المقتول (Median)", f"{med_life:.0f} جولة", "2 – 3 جولات", "1 – 4 جولات", st, diag, tg, pts))

        # 3. Survive to next turn (>= 1 round)
        st, tg, pts = self._grade_range(surv_1p, 70.0, 100.0, 58.0, 100.0)
        diag = (
            f"ممتاز! {surv_1p:.1f}% من الوحوش تنجو من أول رد للخصم وتصل لدورك القادم." if tg == "ideal"
            else f"{100-surv_1p:.1f}% من الوحوش تموت فوراً قبل أن يرجع لك الدور — استثمار المانا محفوف بالمخاطرة!"
        )
        rows.append((3, "نسبة النجاة للدور التالي (>= 1 جولة)", f"{surv_1p:.1f}%", "70% فأعلى", "58% – 69%", st, diag, tg, pts))

        # 4. Survive 2+ rounds
        st, tg, pts = self._grade_range(surv_2p, 45.0, 72.0, 30.0, 82.0)
        diag = (
            "استدامة قوية في الميدان تجعل البافات والـ DoT مؤثرة جداً." if tg == "ideal"
            else ("نسبة ضعيفة فقط تصمد جولتين — البافات والهالات لا تلحق تعطي قيمتها." if surv_2p < 45.0 else "صمود مفرط — صعوبة في كسر التشكيلات.")
        )
        rows.append((4, "نسبة النجاة لدورين (>= 2 جولة)", f"{surv_2p:.1f}%", "45% – 72%", "30% – 44%", st, diag, tg, pts))

        # 5. Survive 3+ rounds
        st, tg, pts = self._grade_range(surv_3p, 20.0, 42.0, 12.0, 55.0)
        diag = (
            "توجد وحوش محورية تصمد 3+ جولات وتقود المعركة بنجاح." if tg == "ideal"
            else ("نادراً ما يصمد أي وحش لـ 3 جولات — الملعب يتبدل بالكامل كل جولتين." if surv_3p < 20.0 else "عدد كبير من الوحوش يعيش 3+ جولات.")
        )
        rows.append((5, "نسبة الصمود 3 جولات فأكثر", f"{surv_3p:.1f}%", "20% – 42%", "12% – 55%", st, diag, tg, pts))

        # 6. Cost progression (T5-7 vs T1-2)
        st, tg, pts = self._grade_range(tier_diff, 0.30, 3.0, -0.25, 4.0)
        diag = (
            f"تدرّج منطقي! الوحوش الغالية T5-7 ({high_avg:.2f}ج) تصمد أكثر من T1-2 ({low_avg:.2f}ج)." if tg == "ideal"
            else f"تحذير! وحوش T5-7 الغالية ({high_avg:.2f}ج) لا تصمد أكثر من T1-2 ({low_avg:.2f}ج) — الإزالة السريعة تظلم الكروت الغالية!"
        )
        rows.append((6, "تدرّج البقاء حسب التكلفة (T5-7 ضد T1-2)", f"{tier_diff:+.2f} جولة", "+0.30ج فأكثر", "-0.25ج فأكثر", st, diag, tg, pts))

        # 7. Role progression (Tanks vs Assassins)
        st, tg, pts = self._grade_range(role_diff, 0.55, 4.0, 0.20, 5.0)
        diag = (
            f"هوية الأدوار واضحة! المدافع/الحاجز ({tank_avg:.2f}ج) يصمد أطول بكثير من القنّاص ({ass_avg:.2f}ج)." if tg == "ideal"
            else f"الفارق بين المدافع/الحاجز ({tank_avg:.2f}ج) والقنّاص ({ass_avg:.2f}ج) صغير جداً — راجع دفاع المدافعين!"
        )
        rows.append((7, "تدرّج الأدوار (حاجز/مدافع ضد قنّاص)", f"{role_diff:+.2f} جولة", "+0.55ج فأكثر", "+0.20ج فأكثر", st, diag, tg, pts))

        # 8. Tribe spread
        st, tg, pts = self._grade_range(tr_spread, 0.0, 0.45, 0.0, 0.85)
        diag = (
            "توازن ممتاز بين جميع العشائر الـ 10 في صمود وحوشها." if tg == "ideal"
            else f"تفاوت بين العشائر (فارق {tr_spread:.2f}ج) — أضعف عشيرة صموداً: {tr_weak[1]} ({tr_weak[0]:.2f}ج)."
        )
        rows.append((8, "توازن بقاء العشائر الـ 10 (أعلى − أقل)", f"{tr_spread:.2f} جولة", "0.45ج فأقل", "0.85ج فأقل", st, diag, tg, pts))

        # 9. Element spread
        st, tg, pts = self._grade_range(el_spread, 0.0, 0.45, 0.0, 0.85)
        diag = (
            "توازن ممتاز بين العناصر الـ 10 في البقاء بالميدان." if tg == "ideal"
            else f"تفاوت بين العناصر (فارق {el_spread:.2f}ج) — أضعف عنصر صموداً: {el_weak[1]} ({el_weak[0]:.2f}ج)."
        )
        rows.append((9, "توازن بقاء العناصر الـ 10 (أعلى − أقل)", f"{el_spread:.2f} جولة", "0.45ج فأقل", "0.85ج فأقل", st, diag, tg, pts))

        # 10. Damage Absorbed before death (% of Base DEF)
        st, tg, pts = self._grade_range(abs_pct, 100.0, 135.0, 88.0, 155.0)
        diag = (
            f"الوحش يمتص {abs_avg:.1f} ضرر ({abs_pct:.0f}% من دفاعه الأساسي) — الشفاء والدفاع يعملان بكفاءة." if tg == "ideal"
            else (f"الضرر الممتص أقل من الدفاع الأساسي ({abs_pct:.0f}%) بسبب التدمير الفوري/الإعدام." if abs_pct < 100.0 else f"امتصاص ضخم ({abs_pct:.0f}%) بفضل الشفاء والتعزيزات.")
        )
        rows.append((10, "الضرر المُمتَص قبل الموت (% من DEF)", f"{abs_pct:.0f}% ({abs_avg:.1f})", "100% – 135%", "88% – 155%", st, diag, tg, pts))

        # 11. Zero-Impact Death %
        st, tg, pts = self._grade_range(zi_pct, 0.0, 18.0, 0.0, 30.0)
        diag = (
            f"رائع! {100-zi_pct:.1f}% من الوحوش تحقق قيمة فعلية (هجمة أو مهارة أو تأثير) قبل موتها." if tg == "ideal"
            else f"تنبيه: {zi_pct:.1f}% من الوحوش تموت بلا هجمة ولا مهارة ولا تأثير — شعور محبط للاعب!"
        )
        rows.append((11, "نسبة الموت بلا أي قيمة (Zero-Impact)", f"{zi_pct:.1f}%", "18% فأقل", "18% – 30%", st, diag, tg, pts))

        # 12. 0-Attack Death %
        st, tg, pts = self._grade_range(z0_atk_pct, 0.0, 25.0, 0.0, 36.0)
        diag = (
            "أغلب الوحوش القتالية تشارك في الهجوم فعلياً قبل سقوطها." if tg == "ideal"
            else f"{z0_atk_pct:.1f}% من الوحوش القتالية تموت قبل تنفيذ هجمة واحدة! (الضغط المضاد مرتفع)."
        )
        rows.append((12, "نسبة الموت قبل أول هجمة (0-Atk Death)", f"{z0_atk_pct:.1f}%", "25% فأقل", "25% – 36%", st, diag, tg, pts))

        # 13. Avg Attacks Before Death
        st, tg, pts = self._grade_range(avg_atks, 1.8, 3.2, 1.2, 4.0)
        diag = (
            f"مشاركة قتالية صحية جداً ({avg_atks:.2f} هجمة لكل وحش قبل موته)." if tg == "ideal"
            else ("مشاركة قتالية منخفضة — الوحش بالكاد يضرب مرة واحدة ويموت." if avg_atks < 1.8 else "الوحش يهاجم مرات كثيرة جداً قبل موته.")
        )
        rows.append((13, "متوسط هجمات الوحش قبل موته", f"{avg_atks:.2f} هجمة", "1.8 – 3.2", "1.2 – 4.0", st, diag, tg, pts))

        # 14. Avg Active Skill Uses Before Death
        st, tg, pts = self._grade_range(avg_sks, 1.2, 2.6, 0.8, 3.4)
        diag = (
            f"استفادة ممتازة من تصميم المهارات النشطة ({avg_sks:.2f} تفعيل قبل الموت)." if tg == "ideal"
            else ("استفادة قليلة من المهارات النشطة قبل موت الوحش." if avg_sks < 1.2 else f"تفعيل مفرط للمهارات النشطة ({avg_sks:.2f} مرة للوحش) — قد تهيمن على القتال!")
        )
        rows.append((14, "متوسط تفعيل المهارات النشطة قبل الموت", f"{avg_sks:.2f} تفعيل", "1.2 – 2.6", "0.8 – 3.4", st, diag, tg, pts))

        # 15. Board Density, Empty Board % & Survivors Separated
        if 2.4 <= avg_brd <= 4.2 and empty_brd_pct <= 10.0:
            st, tg, pts = "🟢 مثالي", "ideal", 100
            diag = f"ملعب حيوي ({avg_brd:.2f}/6 وحوش • فارغ {empty_brd_pct:.1f}%)، والناجون للنهاية {surv_pct:.0f}% (بعمر {surv_avg_r:.1f}ج)."
        elif 1.7 <= avg_brd <= 4.8 and empty_brd_pct <= 18.0:
            st, tg, pts = "🟡 جيد", "good", 72
            diag = f"كثافة مقبولة ({avg_brd:.2f}/6 • فارغ {empty_brd_pct:.1f}%)، والناجون للنهاية {surv_pct:.0f}% (عمر {surv_avg_r:.1f}ج)."
        else:
            st, tg, pts = "🔴 يحتاج تعديل", "bad", 28
            diag = f"مشكلة كثافة ساحة ({avg_brd:.2f}/6 • ساحة فارغة {empty_brd_pct:.1f}%) — صراع الساحة ضعيف أو مكتظ!"
        rows.append((15, "كثافة الساحة • الساحة الفارغة • الناجون", f"{avg_brd:.2f}/6 | فارغ {empty_brd_pct:.0f}%", "2.4–4.2 | فارغ<=10%", "1.7–4.8 | فارغ<=18%", st, diag, tg, pts))

        # 16. Avg Wait Before First Impact
        st, tg, pts = self._grade_range(avg_wait, 0.35, 0.95, 0.15, 1.35)
        diag = (
            f"سرعة تحقيق قيمة متوازنة ({avg_wait:.2f} جولة انتظار قبل أول هجمة/مهارة/تأثير)." if tg == "ideal"
            else ("الوحوش تفعل تأثيرها فورا بالصفر (انتشار مفرط لتأثيرات النزول/المهارات الفورية)." if avg_wait < 0.35 else "الوحوش تنتظر طويلاً قبل تحقيق أول قيمة.")
        )
        rows.append((16, "متوسط الانتظار قبل أول تأثير فعلي", f"{avg_wait:.2f} جولة", "0.35 – 0.95 ج", "0.15 – 1.35 ج", st, diag, tg, pts))

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
            "died_cnt": died_cnt,
            "surv_cnt": surv_cnt,
            "surv_avg_r": surv_avg_r,
            "ms": ms,
            "kc": ex.get("kill_causes", {}),
        }

        for num, m_name, act_s, ideal_s, good_s, st_lbl, diag_s, tag, _ in rows:
            self.surv_tree.insert(
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
            cv.create_text(w // 2, h // 2, text=rtl("بانتظار بيانات التدريب الجديدة لبقاء الوحوش — ابدأ التدريب أو اتصل بالجهاز الثاني!"), fill=COL_GOLD, font=(FONT_UI, 11, "bold"))
            return

        sc = int(d["score"])
        if sc >= 85:
            col, badge = COL_GREEN, "🟢 بقاء واستدامة مثالية في الميدان (AAA)"
        elif sc >= 70:
            col, badge = COL_GOLD, "🟡 بقاء جيد ومقبول (حسّن المؤشرات الصفراء/الحمراء)"
        else:
            col, badge = COL_RED, "🔴 خطر هشاشة الوحوش! (راجع المؤشرات الحمراء بالجدول فوراً)"

        cv.create_text(w - 12, 18, text=rtl(f"التقييم العام لمعيار بقاء الوحوش:  {sc} / 100%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 14, "bold"))
        cv.create_text(w - 12, 44, text=rtl(badge), anchor="e", fill=col, font=(FONT_UI, 10, "bold"))
        cv.create_text(
            w - 12, 66,
            text=rtl(f"المعيار: 🟢 >=85% مثالي | 🟡 70–84% جيد | 🔴 <70% خطر  •  (المقتول: {d['died_cnt']:,} وحش • الناجي للنهاية: {d['surv_cnt']:,} بعمر {d['surv_avg_r']:.1f}ج)"),
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
            text=rtl(f"🟢 مثالي: {d['ideal_cnt']}/16   •   🟡 جيد: {d['good_cnt']}/16   •   🔴 تعديل: {d['bad_cnt']}/16"),
            fill=FG_TEXT, font=(FONT_UI, 8, "bold"),
        )

    # ------------------------------------------------------------------
    # Chart 1: Interactive 4-Way Lifespan Breakdown (Tier / Role / Tribe / Elem)
    # ------------------------------------------------------------------
    def _draw_breakdown_chart(self) -> None:
        cv = self.cv_breakdown
        cv.delete("all")
        w = max(380, cv.winfo_width())
        h = max(220, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات تفصيلية بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        ms = d["ms"]
        mode = self.breakdown_mode.get()
        bars: List[Tuple[str, float, int]] = []
        if mode == "tier":
            s_a, c_a = ms.get("tier_sum", [0] * 8), ms.get("tier_cnt", [0] * 8)
            for t in range(1, 8):
                cnt = int(c_a[t])
                bars.append((f"T{t}", (int(s_a[t]) / cnt) if cnt > 0 else 0.0, cnt))
        elif mode == "role":
            s_a, c_a = ms.get("role_sum", [0] * 6), ms.get("role_cnt", [0] * 6)
            for i in range(6):
                cnt = int(c_a[i])
                bars.append((AR_ROLES[i], (int(s_a[i]) / cnt) if cnt > 0 else 0.0, cnt))
        elif mode == "tribe":
            s_a, c_a = ms.get("tribe_sum", [0] * 10), ms.get("tribe_cnt", [0] * 10)
            for i in range(10):
                cnt = int(c_a[i])
                bars.append((AR_TRIBES[i], (int(s_a[i]) / cnt) if cnt > 0 else 0.0, cnt))
        else:
            s_a, c_a = ms.get("elem_sum", [0] * 10), ms.get("elem_cnt", [0] * 10)
            for i in range(10):
                cnt = int(c_a[i])
                bars.append((AR_ELEMENTS[i], (int(s_a[i]) / cnt) if cnt > 0 else 0.0, cnt))

        max_v = max(3.2, max((b[1] for b in bars), default=2.5) * 1.15)
        pad_l, pad_r, pad_t, pad_b = 18, 36, 26, 44
        pw = max(80, w - pad_l - pad_r)
        ph = max(60, h - pad_t - pad_b)

        def vy(v: float) -> float:
            return pad_t + ph * (1.0 - max(0.0, min(max_v, v)) / max_v)

        # Ideal threshold line at 2.0 rounds!
        y2 = vy(2.0)
        cv.create_line(pad_l, y2, pad_l + pw, y2, fill=COL_GREEN, width=1.5, dash=(4, 3))
        cv.create_text(pad_l + pw + 6, y2, text="2.0", anchor="w", fill=COL_GREEN, font=(FONT_UI, 8, "bold"))
        cv.create_text(pad_l + pw, 10, text=rtl("الخط الأخضر = الحد المثالي لبقاء الوحش (2.0 جولة)"), anchor="e", fill=COL_GREEN, font=(FONT_UI, 8, "bold"))

        y1 = vy(1.0)
        cv.create_line(pad_l, y1, pad_l + pw, y1, fill="#2A3548", dash=(2, 3))
        cv.create_text(pad_l + pw + 6, y1, text="1.0", anchor="w", fill=FG_MUTED, font=(FONT_UI, 8))

        n = len(bars)
        col_w = pw / float(max(1, n))
        # RTL ordering: first item on the RIGHT!
        for i, (lbl, val, cnt) in enumerate(bars):
            cx = (pad_l + pw) - i * col_w - col_w * 0.5
            bw = min(46, col_w * 0.62)
            col = COL_GREEN if val >= 2.0 else (COL_GOLD if val >= 1.5 else COL_RED)
            if cnt > 0:
                top_y = vy(val)
                cv.create_rectangle(cx - bw / 2, top_y, cx + bw / 2, pad_t + ph, fill=col, outline="")
                cv.create_text(cx, top_y - 9, text=f"{val:.2f}", fill=FG_TEXT, font=(FONT_UI, 7, "bold"))
            else:
                cv.create_text(cx, pad_t + ph - 8, text="--", fill=FG_MUTED, font=(FONT_UI, 7))
            cv.create_text(cx, pad_t + ph + 13, text=rtl(lbl), fill=FG_TEXT, font=(FONT_UI, 7, "bold"))
            cv.create_text(cx, pad_t + ph + 28, text=f"({cnt})", fill=FG_MUTED, font=(FONT_UI, 7))

    # ------------------------------------------------------------------
    # Chart 2: All 8 Exit Causes (Donut + RTL Bars)
    # ------------------------------------------------------------------
    def _draw_exit_causes(self) -> None:
        cv = self.cv_exits
        cv.delete("all")
        w = max(320, cv.winfo_width())
        h = max(220, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات أسباب خروج بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        kc = d["kc"]
        ms = d["ms"]
        items = [
            ("active", "المهارات النشطة", COL_PURPLE, int(kc.get("active", 0))),
            ("attack", "الهجوم العادي", COL_GOLD, int(kc.get("attack", 0))),
            ("spell", "البطاقات السحرية", COL_CYAN, int(kc.get("spell", 0))),
            ("counter", "المرتد والأشواك", COL_RED, int(kc.get("counter", 0))),
            ("dot", "حرق / سم / نزيف", COL_GREEN, int(kc.get("dot", 0))),
            ("passive", "الباسفات والوصية", COL_SLATE, int(kc.get("passive", 0))),
            ("sac", "تضحية / التهام", COL_ORANGE, int(ms.get("exits_sacrifice", 0))),
            ("recall", "إرجاع لليد (Recall)", COL_TEAL, int(ms.get("exits_recalled", 0))),
        ]
        tot = sum(x[3] for x in items)
        if tot <= 0:
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد حالات خروج مسجلة."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        donut_r = min(72, int(min(w * 0.21, h * 0.34)))
        cx = max(donut_r + 14, int(w * 0.20))
        cy = h // 2 + 4
        start_ang = 90.0
        for _, _, col, cnt in items:
            if cnt <= 0:
                continue
            ext = -360.0 * (cnt / float(tot))
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
        cv.create_text(cx, cy - 8, text=f"{tot:,}", fill=FG_TEXT, font=(FONT_UI, 10, "bold"))
        cv.create_text(cx, cy + 9, text=rtl("خروج وحش"), fill=FG_MUTED, font=(FONT_UI, 7, "bold"))

        bar_l = cx + donut_r + 16
        bar_r = w - 10
        rw = max(100, bar_r - bar_l)
        ranked = sorted(items, key=lambda x: x[3], reverse=True)
        row_h = max(27, (h - 22) // len(ranked))

        for i, (_, lbl, col, cnt) in enumerate(ranked):
            y = 10 + i * row_h
            pct = 100.0 * cnt / tot
            cv.create_rectangle(bar_r - 8, y + 2, bar_r, y + 10, fill=col, outline="")
            cv.create_text(bar_r - 12, y + 6, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_text(bar_l, y + 6, text=f"{pct:4.1f}% ({cnt})", anchor="w", fill=FG_TEXT, font=(FONT_UI, 7, "bold"))
            by = y + 15
            cv.create_rectangle(bar_l, by, bar_r, by + 6, fill="#242C3D", outline="")
            bw = max(2, int(rw * min(100.0, pct) / 100.0)) if cnt > 0 else 0
            if bw > 0:
                cv.create_rectangle(bar_r - bw, by, bar_r, by + 6, fill=col, outline="")

    # ------------------------------------------------------------------
    # Live 80-Match Non-Destructive Probe (Shared snapshot with Tab 1!)
    # ------------------------------------------------------------------
    def _start_live_probe(self) -> None:
        if self._probe_running:
            return
        self._probe_running = True
        self.probe_btn.config(state="disabled", text=rtl("جاري تشغيل 80 مباراة..."))
        self.probe_status_var.set(rtl("جاري قياس بقاء الوحوش عبر 80 مباراة حية..."))
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

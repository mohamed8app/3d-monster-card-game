# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Pillar #8: Victory Conditions & Win Channels Tab (Arabic RTL).
Tracks all 14 real Deck Game V2.2 victory-channel indicators:
  - Overall 0-100% Victory Diversity Score (>=85% Ideal, >=70% Good, <70% Warning)
  - Visual Chart 1: Commander HP (500) Damage Sources vs Final Killing-Blow Channel (6 Paired Channels)
  - Visual Chart 2: Victory Tempo Profiles (Aggro / Midrange / Control / Sudden Death) + Drama Badges
  - Self-Explanatory 14-Row Diagnostic Table (Actual | Ideal | Good | Status | Auto-Diagnosis)
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


class VictoryConditionsTab:
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

        has_main = int(self.store.excitement.get("victory_cond", {}).get("matches", 0)) > 0
        has_probe = bool(self.probe_data and int(self.probe_data.get("victory_cond", {}).get("matches", 0)) > 0)
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

        # Visual Charts Row (RTL: Face Damage vs Killing Blow on RIGHT 52%, Victory Profiles & Drama on LEFT 48%)
        charts = tk.Frame(self.inner, bg=BG_DARK)
        charts.pack(fill="x", pady=(0, 8))
        charts.grid_columnconfigure(0, weight=48, uniform="vc")
        charts.grid_columnconfigure(1, weight=52, uniform="vc")

        p_prof = self._make_panel(charts, "أنماط الفوز (سريع / وسطي / تحكم / موت مفاجئ) + دراما الحسم", COL_CYAN)
        p_prof.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        self.cv_prof = tk.Canvas(p_prof, bg=BG_PANEL, height=240, highlightthickness=0)
        self.cv_prof.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_prof.bind("<Configure>", lambda _e: self._draw_profiles())

        p_ch = self._make_panel(charts, "مصادر إنزال دم القائد (500 HP) مقابل الضربة القاضية النهائية", COL_GOLD)
        p_ch.grid(row=0, column=1, sticky="nsew", padx=(5, 0))
        self.cv_ch = tk.Canvas(p_ch, bg=BG_PANEL, height=240, highlightthickness=0)
        self.cv_ch.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_ch.bind("<Configure>", lambda _e: self._draw_channels())

        # Self-Explanatory 14-Indicator Diagnostic Table
        tbl_panel = self._make_panel(
            self.inner,
            "بطاقة التقييم التفصيلية لمعيار طرق الفوز (14 مؤشراً — النتيجة + الجيد + المثالي + التشخيص التلقائي)",
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
        self.vc_tree = ttk.Treeview(tbl_panel, columns=tuple(c for c, _, _ in headers), show="headings", height=14)
        for c, txt, w in headers:
            self.vc_tree.heading(c, text=rtl(txt))
            self.vc_tree.column(c, width=w, anchor="e" if c in ("metric", "diag") else "center")

        self.vc_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.vc_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.vc_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.vc_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        self.vc_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.vc_tree)

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
        self._draw_channels()
        self._draw_profiles()

    @staticmethod
    def _grade_range(val: float, ideal_lo: float, ideal_hi: float, good_lo: float, good_hi: float) -> Tuple[str, str, int]:
        if ideal_lo <= val <= ideal_hi:
            return ("🟢 مثالي", "ideal", 100)
        if good_lo <= val <= good_hi:
            return ("🟡 جيد", "good", 72)
        return ("🔴 يحتاج تعديل", "bad", 28)

    def _compute_and_populate(self) -> None:
        self.vc_tree.delete(*self.vc_tree.get_children())
        ex = self._active_ex()
        vc = ex.get("victory_cond", {})
        matches = int(vc.get("matches", 0))

        if matches <= 0:
            self._last_metrics = {"has_data": False, "score": 0, "ideal_cnt": 0, "good_cnt": 0, "bad_cnt": 0}
            self.vc_tree.insert(
                "", "end",
                values=(
                    rtl("بانتظار بيانات التدريب — تظهر الأرقام تلقائياً فور بدء التدريب على المحاكي المحدّث أو الاتصال بالجهاز الثاني."),
                    rtl("بانتظار بيانات"), "--", "--", "--", rtl("تتبع طرق الفوز وقنوات الحسم جاهز ومفعّل"), "!",
                ),
                tags=("nodata",),
            )
            return

        dec = max(1, int(vc.get("decisive_matches", matches)))
        fbc = [int(x) for x in vc.get("face_by_ch", [0] * 6)]
        kbc = [int(x) for x in vc.get("kill_by_ch", [0] * 6)]
        f_tot = max(1, sum(fbc))
        k_tot = max(1, sum(kbc))

        f_pcts = [100.0 * x / f_tot for x in fbc]
        k_pcts = [100.0 * x / k_tot for x in kbc]

        atk_face = f_pcts[0]
        act_face = f_pcts[1]
        spl_face = f_pcts[2]
        att_face = f_pcts[3] + f_pcts[4]  # DoT + Passives/Envs/Covenants
        non_combat_ko = 100.0 * (k_tot - kbc[0]) / k_tot

        aggro_pct = 100.0 * int(vc.get("aggro_wins", 0)) / matches
        mid_pct = 100.0 * int(vc.get("midrange_wins", 0)) / matches
        ctrl_pct = 100.0 * int(vc.get("control_wins", 0)) / matches
        sd_cov_pct = 100.0 * (int(vc.get("sudden_death_wins", 0)) + int(vc.get("covenant_limit_wins", 0))) / matches

        board_dom_pct = 100.0 * int(vc.get("board_dom_wins", 0)) / dec
        blowout_pct = 100.0 * int(vc.get("blowout_wins", 0)) / dec
        clutch_pct = 100.0 * int(vc.get("clutch_100_wins", 0)) / dec
        comeback_pct = 100.0 * int(vc.get("comeback_100_wins", 0)) / dec
        burst_pct = 100.0 * int(vc.get("burst_finish_wins", 0)) / dec

        # Shannon Diversity Entropy across 5 main damage channels (attack, active, spell, dot+passive, self/sd)
        ch5 = [fbc[0], fbc[1], fbc[2], fbc[3] + fbc[4], fbc[5]]
        c5_tot = max(1, sum(ch5))
        ent = 0.0
        for v in ch5:
            if v > 0:
                p_v = v / float(c5_tot)
                ent -= p_v * math.log2(p_v)
        # Normalize against 4 major active channels (log2(4)=2.0) capped at 100%
        div_entropy = min(100.0, 100.0 * ent / math.log2(4.0))

        rows: List[Tuple[int, str, str, str, str, str, str, str, int]] = []

        # 1. Combat Attack Face %
        st, tg, pts = self._grade_range(atk_face, 40.0, 68.0, 28.0, 78.0)
        diag = (
            f"توازن قتالي رائع! هجمات الوحوش المباشرة تنزل {atk_face:.1f}% من دم القائد." if tg == "ideal"
            else ("هجمات الوحوش ضعيفة جداً في إنهاء القائد (المهارات/السحر تحسم المعركة بدلاً منها!)." if atk_face < 40.0 else "اعتماد شبه كلي على الضرب المباشر فقط.")
        )
        rows.append((1, "حصة هجمات الوحوش المباشرة من دم القائد", f"{atk_face:.1f}%", "40% – 68%", "28% – 78%", st, diag, tg, pts))

        # 2. Active Skill Face %
        st, tg, pts = self._grade_range(act_face, 12.0, 30.0, 6.0, 42.0)
        diag = (
            f"المهارات الفعّالة تساهم بنسبة صحية ({act_face:.1f}%) في حرق دم القائد." if tg == "ideal"
            else (f"المهارات الفعّالة تحرق القائد بكثافة عالية ({act_face:.1f}% من كل ضرر القائد!)." if act_face > 30.0 else "مساهمة المهارات الفعّالة في دم القائد ضعيفة.")
        )
        rows.append((2, "حصة المهارات الفعّالة من ضرر القائد", f"{act_face:.1f}%", "12% – 30%", "6% – 42%", st, diag, tg, pts))

        # 3. Direct Spell Face %
        st, tg, pts = self._grade_range(spl_face, 8.0, 24.0, 3.0, 34.0)
        diag = (
            f"سحر الحرق المباشر فعال ومتوازن ({spl_face:.1f}% من دم القائد)." if tg == "ideal"
            else ("بطاقات السحر شبه غائبة عن تهديد دم القائد." if spl_face < 8.0 else "سحر الحرق المباشر مفرط وقد يهمّش الملعب.")
        )
        rows.append((3, "حصة بطاقات السحر المباشر من ضرر القائد", f"{spl_face:.1f}%", "8% – 24%", "3% – 34%", st, diag, tg, pts))

        # 4. Attrition (DoTs + Passives + Envs + Covenants) Face %
        st, tg, pts = self._grade_range(att_face, 6.0, 22.0, 2.0, 32.0)
        diag = (
            f"قنوات الاستنزاف والهالات والبيئات فعّالة ({att_face:.1f}% من ضرر القائد)." if tg == "ideal"
            else ("قنوات الاستنزاف والسلبيات ضعيفة الأثر على القائد." if att_face < 6.0 else "ضرر الاستنزاف والهالات مرتفع جداً.")
        )
        rows.append((4, "حصة الاستنزاف والهالات والبيئات من دم القائد", f"{att_face:.1f}%", "6% – 22%", "2% – 32%", st, diag, tg, pts))

        # 5. Non-Combat Killing Blow %
        st, tg, pts = self._grade_range(non_combat_ko, 20.0, 48.0, 10.0, 62.0)
        diag = (
            f"تنوع ممتاز في لحظة الحسم ({100-non_combat_ko:.0f}% ضربة قتالية مقابل {non_combat_ko:.0f}% سحر/مهارة/استنزاف)." if tg == "ideal"
            else (f"أغلب المباريات ({non_combat_ko:.0f}%) تنتهي بحسم غير قتالي (مهارات/سحر) بدلاً من معركة الوحوش!" if non_combat_ko > 48.0 else "الضربة القاضية محصورة بالهجوم المباشر فقط.")
        )
        rows.append((5, "نسبة الضربة القاضية غير القتالية (سحر/مهارة)", f"{non_combat_ko:.1f}%", "20% – 48%", "10% – 62%", st, diag, tg, pts))

        # 6. Aggro / Rush Wins (<=12 rounds)
        st, tg, pts = self._grade_range(aggro_pct, 10.0, 26.0, 4.0, 36.0)
        diag = (
            f"استراتيجيات الهجوم السريع قابلة للنجاح دون هيمنة ({aggro_pct:.1f}% <= 12 جولة)." if tg == "ideal"
            else ("الفوز الهجومي السريع نادر جداً (المباريات بطيئة البداية)." if aggro_pct < 10.0 else "طغيان الهجوم السريع (Aggro Rush)!")
        )
        rows.append((6, "نسبة الفوز الهجومي السريع (Aggro <=12 جولة)", f"{aggro_pct:.1f}%", "10% – 26%", "4% – 36%", st, diag, tg, pts))

        # 7. Midrange / Tempo Wins (13-22 rounds)
        st, tg, pts = self._grade_range(mid_pct, 42.0, 65.0, 30.0, 78.0)
        diag = (
            f"العمود الفقري للمباريات سليم جداً ({mid_pct:.1f}% تُحسم بين الجولتين 13 و22)." if tg == "ideal"
            else "توزيع منتصف المباراة (13–22 جولة) خارج النطاق المثالي."
        )
        rows.append((7, "نسبة الفوز الوسطي المتوازن (13–22 جولة)", f"{mid_pct:.1f}%", "42% – 65%", "30% – 78%", st, diag, tg, pts))

        # 8. Control / Attrition Wins (23-39 rounds)
        st, tg, pts = self._grade_range(ctrl_pct, 15.0, 35.0, 8.0, 46.0)
        diag = (
            f"ديكات التحكم والنفس الطويل لها مساحة حقيقية ({ctrl_pct:.1f}% بين 23 و39 جولة)." if tg == "ideal"
            else ("المباريات الطويلة نادرة." if ctrl_pct < 15.0 else "نسبة عالية جداً من المباريات تمتد للاستنزاف الطويل!")
        )
        rows.append((8, "نسبة الفوز بالتحكم والنفس الطويل (23–39 جولة)", f"{ctrl_pct:.1f}%", "15% – 35%", "8% – 46%", st, diag, tg, pts))

        # 9. Sudden Death 40+ / Covenant Limit
        st, tg, pts = self._grade_range(sd_cov_pct, 0.0, 5.0, 0.0, 12.0)
        diag = (
            f"حسم طبيعي نظيف! الموت المفاجئ وحدود العهود لا تتجاوز {sd_cov_pct:.1f}% فقط." if tg == "ideal"
            else f"تحذير جمود! {sd_cov_pct:.1f}% من المباريات وصلت للموت المفاجئ (40+) أو انتهاء حد العهد!"
        )
        rows.append((9, "حسم الموت المفاجئ (40+) أو حد العهد", f"{sd_cov_pct:.1f}%", "5% فأقل", "5% – 12%", st, diag, tg, pts))

        # 10. Board Dominance Wins (>=2 surviving units)
        st, tg, pts = self._grade_range(board_dom_pct, 55.0, 82.0, 42.0, 92.0)
        diag = (
            f"السيطرة على الملعب تصنع الفوز فعلاً ({board_dom_pct:.1f}% من الفائزين ينهون بـ >=2 وحوش)." if tg == "ideal"
            else ("السيطرة على الملعب ضعيفة الأثر على الحسم." if board_dom_pct < 55.0 else "الفوز شبه مستحيل بدون ملعب مكدّس.")
        )
        rows.append((10, "الفوز بسيطرة الملعب (إنهاء بـ >=2 وحوش)", f"{board_dom_pct:.1f}%", "55% – 82%", "42% – 92%", st, diag, tg, pts))

        # 11. Blowout Wins (Winner HP >= 325/500)
        st, tg, pts = self._grade_range(blowout_pct, 0.0, 18.0, 0.0, 30.0)
        diag = (
            f"مباريات تنافسية مشدودة! الفوز الساحق (>=325 HP) لا يتجاوز {blowout_pct:.1f}%." if tg == "ideal"
            else f"كثرة المباريات الساحقة أحادية الطرف ({blowout_pct:.1f}% من الفائزين فوق 325/500 HP!)."
        )
        rows.append((11, "نسبة الفوز الساحق أحادي الطرف (>=325 HP)", f"{blowout_pct:.1f}%", "18% فأقل", "18% – 30%", st, diag, tg, pts))

        # 12. Clutch Finishes (Winner HP <= 100/500)
        st, tg, pts = self._grade_range(clutch_pct, 18.0, 38.0, 10.0, 50.0)
        diag = (
            f"نهايات حماسية ومثيرة! {clutch_pct:.1f}% من الانتصارات تُحسم بفارق <=100 HP (وعودات بطولية: {comeback_pct:.0f}%)." if tg == "ideal"
            else ("النهايات المتقاربة قليلة (الأفضلية المبكرة تحسم بسهولة)." if clutch_pct < 18.0 else "أغلب المباريات متقاربة للغاية حتى النفس الأخير.")
        )
        rows.append((12, "نسبة الفوز الدرامي المتقارب (<=100 HP)", f"{clutch_pct:.1f}% (عودة:{comeback_pct:.0f}%)", "18% – 38%", "10% – 50%", st, diag, tg, pts))

        # 13. Final-Turn Face Burst (>=100 Face Dmg)
        st, tg, pts = self._grade_range(burst_pct, 0.0, 10.0, 0.0, 20.0)
        diag = (
            f"حماية ممتازة من الموت المفاجئ! انفجارات >=100 ضرر قائد بالدور الأخير فقط {burst_pct:.1f}%." if tg == "ideal"
            else f"تحذير OTK! {burst_pct:.1f}% من المباريات حُسمت بانفجار >=100 ضرر مباشر في دور واحد!"
        )
        rows.append((13, "حسم بالانفجار المفاجئ (>=100 ضرر قائد/دور)", f"{burst_pct:.1f}%", "10% فأقل", "10% – 20%", st, diag, tg, pts))

        # 14. Victory Channel Diversity Entropy (0-100%)
        st, tg, pts = self._grade_range(div_entropy, 75.0, 100.0, 58.0, 100.0)
        diag = (
            f"ثراء استراتيجي عالٍ! تنوع قنوات الفوز {div_entropy:.0f}% (لا توجد طريقة فوز واحدة محتكرة)." if tg == "ideal"
            else f"احتكار واضح لطريقة فوز واحدة (مؤشر التنوع {div_entropy:.0f}% فقط — قنوات أخرى مهملة!)."
        )
        rows.append((14, "مؤشر تنوع طرق الفوز (Diversity Entropy)", f"{div_entropy:.1f}%", "75% فأعلى", "58% – 74%", st, diag, tg, pts))

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
            "matches": matches,
            "f_pcts": f_pcts,
            "k_pcts": k_pcts,
            "tempo": (aggro_pct, mid_pct, ctrl_pct, sd_cov_pct),
            "drama": (board_dom_pct, clutch_pct, comeback_pct, blowout_pct, burst_pct),
        }

        for num, m_name, act_s, ideal_s, good_s, st_lbl, diag_s, tag, _ in rows:
            self.vc_tree.insert(
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
            cv.create_text(w // 2, h // 2, text=rtl("بانتظار بيانات التدريب الجديدة لطرق الفوز — ابدأ التدريب أو اتصل بالجهاز الثاني!"), fill=COL_GOLD, font=(FONT_UI, 11, "bold"))
            return

        sc = int(d["score"])
        if sc >= 85:
            col, badge = COL_GREEN, "🟢 تنوع طرق فوز مثالي وحسم درامي ممتع (AAA)"
        elif sc >= 70:
            col, badge = COL_GOLD, "🟡 طرق فوز جيدة (راجع المؤشرات الصفراء/الحمراء لتنويع قنوات الحسم)"
        else:
            col, badge = COL_RED, "🔴 أحادية في طرق الفوز أو حسم غير متوازن (راجع المؤشرات الحمراء!)"

        cv.create_text(w - 12, 18, text=rtl(f"التقييم العام لمعيار طرق الفوز وقنوات الحسم:  {sc} / 100%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 14, "bold"))
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
    # Chart 1: Commander HP Damage Sources vs Final Killing Blow (Paired RTL)
    # ------------------------------------------------------------------
    def _draw_channels(self) -> None:
        cv = self.cv_ch
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(200, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات قنوات فوز بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        labels = [
            "هجمات الوحوش المباشرة",
            "المهارات الفعّالة",
            "بطاقات السحر المباشر",
            "انفجار الحرق/النزيف DoT",
            "الهالات/البيئات/العهود",
            "ضرر ذاتي / موت مفاجئ",
        ]
        fp = d["f_pcts"]
        kp = d["k_pcts"]

        cv.create_rectangle(w - 22, 8, w - 12, 16, fill=COL_CYAN, outline="")
        cv.create_text(w - 26, 12, text=rtl("حصة ضرر دم القائد %"), anchor="e", fill=COL_CYAN, font=(FONT_UI, 8, "bold"))
        cv.create_rectangle(w - 178, 8, w - 168, 16, fill=COL_PINK, outline="")
        cv.create_text(w - 182, 12, text=rtl("حصة الضربة القاضية النهائية %"), anchor="e", fill=COL_PINK, font=(FONT_UI, 8, "bold"))

        lbl_x = w - 10
        bar_r = w - 152
        bar_l = 104
        bar_w = max(60, bar_r - bar_l)
        row_h = max(31, (h - 32) // 6)

        for i, lbl in enumerate(labels):
            y = 26 + i * row_h
            fv, kv = fp[i], kp[i]
            cv.create_text(lbl_x, y + 10, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))

            cv.create_rectangle(bar_l, y + 2, bar_r, y + 10, fill="#242C3D", outline="")
            fw = max(2, int(bar_w * min(100.0, fv) / 100.0)) if fv > 0.1 else 0
            if fw > 0:
                cv.create_rectangle(bar_r - fw, y + 2, bar_r, y + 10, fill=COL_CYAN, outline="")

            cv.create_rectangle(bar_l, y + 12, bar_r, y + 20, fill="#242C3D", outline="")
            kw = max(2, int(bar_w * min(100.0, kv) / 100.0)) if kv > 0.1 else 0
            if kw > 0:
                cv.create_rectangle(bar_r - kw, y + 12, bar_r, y + 20, fill=COL_PINK, outline="")

            cv.create_text(bar_l - 6, y + 11, text=f"{fv:4.1f}% | {kv:4.1f}%", anchor="e", fill=FG_TEXT, font=(FONT_UI, 7, "bold"))

    # ------------------------------------------------------------------
    # Chart 2: Victory Tempo Stacked Bar + Drama/Finish Profile Bars
    # ------------------------------------------------------------------
    def _draw_profiles(self) -> None:
        cv = self.cv_prof
        cv.delete("all")
        w = max(340, cv.winfo_width())
        h = max(200, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات أنماط فوز بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        # Top: Stacked Tempo Bar (Aggro / Midrange / Control / Sudden Death)
        agg, mid, ctl, sd = d["tempo"]
        cv.create_text(w - 10, 12, text=rtl("توزيع سرعة الفوز (مجموع 100%):"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
        bar_l, bar_r = 14, w - 14
        bar_w = max(140, bar_r - bar_l)
        segs = [
            ("سريع <=12", agg, COL_RED),
            ("وسطي 13–22", mid, COL_GREEN),
            ("تحكم 23–39", ctl, COL_BLUE),
            ("موت مفاجئ", sd, COL_GOLD),
        ]
        cur_r = bar_r
        for idx_s, (lbl, pct, col) in enumerate(segs):
            sw = int(round(bar_w * pct / 100.0)) if idx_s < len(segs) - 1 else max(0, cur_r - bar_l)
            if sw > 0:
                cv.create_rectangle(cur_r - sw, 24, cur_r, 44, fill=col, outline="")
                if sw >= 32:
                    cv.create_text(cur_r - sw / 2, 34, text=f"{pct:.0f}%", fill="#FFFFFF", font=(FONT_UI, 8, "bold"))
                cur_r -= sw

        lx = bar_r
        for lbl, pct, col in segs:
            cv.create_rectangle(lx - 8, 52, lx, 60, fill=col, outline="")
            cv.create_text(lx - 11, 56, text=rtl(f"{lbl} {pct:.0f}%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 7))
            lx -= max(72, int(bar_w / 4.0))

        # Bottom: 5 Drama / Finish Condition Bars
        cv.create_line(10, 72, w - 10, 72, fill=BG_BORDER)
        b_dom, clu, cbk, blo, brs = d["drama"]
        d_rows = [
            ("فوز بسيطرة الملعب (>=2 وحوش)", b_dom, COL_BLUE),
            ("فوز درامي متقارب (<=100 HP)", clu, COL_GREEN),
            ("عودة بطولية (تعويض >=100 HP)", cbk, COL_CYAN),
            ("فوز ساحق أحادي (>=325 HP)", blo, COL_GOLD),
            ("انفجار الدور الأخير (>=100 ضرر)", brs, COL_RED),
        ]
        lbl_x = w - 10
        br2 = w - 186
        bl2 = 52
        bw2 = max(60, br2 - bl2)
        rh2 = max(28, (h - 84) // len(d_rows))
        for i, (lbl, val, col) in enumerate(d_rows):
            y = 82 + i * rh2
            cv.create_text(lbl_x, y + 8, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8))
            cv.create_rectangle(bl2, y + 2, br2, y + 14, fill="#242C3D", outline="")
            fw = max(2, int(bw2 * min(100.0, val) / 100.0)) if val > 0.1 else 0
            if fw > 0:
                cv.create_rectangle(br2 - fw, y + 2, br2, y + 14, fill=col, outline="")
            cv.create_text(bl2 - 6, y + 8, text=f"{val:4.1f}%", anchor="e", fill=col, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Shared 80-Match Non-Destructive Live Probe
    # ------------------------------------------------------------------
    def _start_live_probe(self) -> None:
        if self._probe_running:
            return
        self._probe_running = True
        self.probe_btn.config(state="disabled", text=rtl("جاري تشغيل 80 مباراة..."))
        self.probe_status_var.set(rtl("جاري قياس طرق الفوز وقنوات الحسم عبر 80 مباراة حية..."))
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

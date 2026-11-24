# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Pillar #3: Combat Quality Scorecard Tab (Arabic RTL).
Tracks all 18 combat quality metrics with:
  - Overall 0-100% Combat Quality Score (>=85% Ideal, >=70% Good, <70% Warning)
  - Visual Clash Outcome Breakdown Chart (Face Strikes, Fair Trades, Clean Kills, Mutual Double-KOs, Dodged/Whiffs)
  - Visual Combat Economy Balance Chart (Monster Dmg, Overkill Waste, Face Dmg, Healing Recovery, Skill Dmg, Barrier Absorbed)
  - Self-Explanatory 18-Row Diagnostic Table (Actual | Ideal | Good | Status | Auto-Diagnosis)
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
COL_ORANGE = "#F97316"
COL_TEAL = "#14B8A6"

FONT_UI = "Segoe UI"


class CombatQualityTab:
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

        has_main = int(self.store.excitement.get("combat_quality", {}).get("total_strikes", 0)) > 0
        has_probe = bool(self.probe_data and int(self.probe_data.get("combat_quality", {}).get("total_strikes", 0)) > 0)
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
        # Scrollable Body (Direct Training Logs — No Sample Bar!)
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

        # --- Hero Score Banner ---
        hero = tk.Frame(self.inner, bg=BG_CARD, highlightbackground=BG_BORDER, highlightthickness=1, padx=14, pady=10)
        hero.pack(fill="x", pady=(2, 8))
        self.cv_score = tk.Canvas(hero, bg=BG_CARD, height=82, highlightthickness=0)
        self.cv_score.pack(fill="x")
        self.cv_score.bind("<Configure>", lambda _e: self._draw_score_banner())

        # --- Self-Explanatory 18-Indicator Diagnostic Table (Full Width First!) ---
        tbl_panel = self._make_panel(
            self.inner,
            "بطاقة التقييم التفصيلية لمعيار جودة القتال (18 مؤشراً — النتيجة + الجيد + المثالي + التشخيص التلقائي)",
            COL_CYAN,
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
        self.cq_tree = ttk.Treeview(tbl_panel, columns=tuple(c for c, _, _ in headers), show="headings", height=18)
        for c, txt, w in headers:
            self.cq_tree.heading(c, text=rtl(txt))
            self.cq_tree.column(c, width=w, anchor="e" if c in ("metric", "diag") else "center")

        self.cq_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.cq_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.cq_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.cq_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        self.cq_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.cq_tree)

        # --- Visual Charts Stacked Vertically at Full Screen Width ---
        p_clash = self._make_panel(self.inner, "توزيع نتائج الاشتباك القتالي (تبادل / قتل نظيف / موت متبادل / مراوغة)", COL_RED)
        p_clash.pack(fill="x", pady=(0, 8))
        self.cv_clash = tk.Canvas(p_clash, bg=BG_PANEL, height=240, highlightthickness=0)
        self.cv_clash.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_clash.bind("<Configure>", lambda _e: self._draw_clash_outcomes())

        p_econ = self._make_panel(self.inner, "ميزان الضرر والشفاء والضرر الزائد (Overkill) والحواجز", COL_GOLD)
        p_econ.pack(fill="x", pady=(0, 8))
        self.cv_econ = tk.Canvas(p_econ, bg=BG_PANEL, height=240, highlightthickness=0)
        self.cv_econ.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_econ.bind("<Configure>", lambda _e: self._draw_damage_economy())

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
        self._draw_clash_outcomes()
        self._draw_damage_economy()

    @staticmethod
    def _grade_range(val: float, ideal_lo: float, ideal_hi: float, good_lo: float, good_hi: float) -> Tuple[str, str, int]:
        if ideal_lo <= val <= ideal_hi:
            return ("🟢 مثالي", "ideal", 100)
        if good_lo <= val <= good_hi:
            return ("🟡 جيد", "good", 72)
        return ("🔴 يحتاج تعديل", "bad", 28)

    def _compute_and_populate(self) -> None:
        self.cq_tree.delete(*self.cq_tree.get_children())
        ex = self._active_ex()
        cq = ex.get("combat_quality", {})
        ms = ex.get("monster_survival", {})
        kc = ex.get("kill_causes", {})
        gp = ex.get("game_pace", {})

        tot_strikes = int(cq.get("total_strikes", 0))
        clashes = int(cq.get("unit_clashes", 0))
        if tot_strikes <= 0:
            self._last_metrics = {"has_data": False, "score": 0, "ideal_cnt": 0, "good_cnt": 0, "bad_cnt": 0}
            self.cq_tree.insert(
                "", "end",
                values=(
                    rtl("بانتظار بيانات التدريب — تظهر الأرقام تلقائياً فور بدء التدريب على المحاكي المحدّث أو الاتصال بالجهاز الثاني."),
                    rtl("بانتظار بيانات"), "--", "--", "--", rtl("تتبع جودة القتال جاهز ومفعّل"), "!",
                ),
                tags=("nodata",),
            )
            return

        c_died = max(1, int(ms.get("combat_died", 0)))
        all_died = max(1, int(ms.get("died_cnt", 0)))
        cl = max(1, clashes)

        avg_dealt = float(cq.get("unit_dmg_dealt_sum", 0)) / c_died
        avg_recv = float(ms.get("dmg_absorbed_sum", 0)) / all_died
        lethal_pct = 100.0 * int(cq.get("lethal_strikes", 0)) / cl
        hits_to_kill = float(cq.get("hits_to_kill_sum", 0)) / all_died
        whiff_pct = 100.0 * int(cq.get("whiff_strikes", 0)) / cl

        raw_u = max(1, int(cq.get("raw_unit_dmg", 0)))
        overkill_pct = 100.0 * int(cq.get("overkill_dmg", 0)) / raw_u
        intercept_pct = 100.0 * int(cq.get("intercepted_strikes", 0)) / cl

        tot_m_dmg = max(1, int(cq.get("total_monster_dmg", 0)))
        tot_f_dmg = int(cq.get("total_face_dmg", 0))
        bar_abs = int(cq.get("barrier_dmg_absorbed", 0))
        bar_share = 100.0 * bar_abs / tot_m_dmg

        tot_healed = int(cq.get("total_healed", 0))
        heal_ratio = 100.0 * tot_healed / max(1, tot_m_dmg + tot_f_dmg)

        mutual_pct = 100.0 * int(cq.get("mutual_kills", 0)) / cl
        fair_pct = 100.0 * int(cq.get("fair_trades", 0)) / cl
        clean_pct = 100.0 * int(cq.get("clean_kills", 0)) / cl

        acts_u = max(1, int(cq.get("actives_used_total", 0)))
        avg_sk_dmg = float(cq.get("skill_dmg_total", 0)) / acts_u
        swing_pct = 100.0 * int(cq.get("swing_strikes", 0)) / max(1, tot_strikes)

        partic_pct = 100.0 * (c_died - int(ms.get("combat_0atk", 0))) / c_died
        tot_pt = max(1, int(gp.get("total_player_turns", 0)) or int(ms.get("board_turns", 0)))
        atks_per_turn = float(tot_strikes) / tot_pt

        tot_k = max(1, sum(int(kc.get(k, 0)) for k in ("active", "attack", "spell", "counter", "dot", "passive")))
        non_combat_k = int(kc.get("active", 0)) + int(kc.get("spell", 0)) + int(kc.get("dot", 0)) + int(kc.get("passive", 0))
        non_combat_pct = 100.0 * non_combat_k / tot_k

        tot_hits = max(1, int(cq.get("total_unit_hits", 0)))
        near_death_pct = 100.0 * int(cq.get("near_death_hits", 0)) / tot_hits

        rows: List[Tuple[int, str, str, str, str, str, str, str, int]] = []

        # 1. Avg Damage Dealt by Monster
        st, tg, pts = self._grade_range(avg_dealt, 22.0, 55.0, 14.0, 75.0)
        diag = (
            f"تأثير قتالي صحي! كل وحش قتالي يُلحق {avg_dealt:.1f} ضرر في المتوسط خلال حياته." if tg == "ideal"
            else ("إنتاجية الوحوش منخفضة (تموت قبل تحقيق ضرر كافٍ)." if avg_dealt < 22.0 else "إنتاجية ضرر مفرطة للوحش الواحد.")
        )
        rows.append((1, "متوسط الضرر الذي يسببه الوحش بحياته", f"{avg_dealt:.1f} ضرر", "22 – 55 ضرر", "14 – 75 ضرر", st, diag, tg, pts))

        # 2. Avg Damage Received
        st, tg, pts = self._grade_range(avg_recv, 22.0, 48.0, 16.0, 65.0)
        diag = (
            f"صلابة متوازنة؛ الوحش يتحمل {avg_recv:.1f} ضرر فعلي قبل سقوطه." if tg == "ideal"
            else ("الوحوش تسقط بعد ضرر قليل نسبياً." if avg_recv < 22.0 else "الوحوش تتحمل ضرراً ضخماً جداً قبل الموت.")
        )
        rows.append((2, "متوسط الضرر الذي يستقبله الوحش", f"{avg_recv:.1f} ضرر", "22 – 48 ضرر", "16 – 65 ضرر", st, diag, tg, pts))

        # 3. Lethal Attack Rate %
        st, tg, pts = self._grade_range(lethal_pct, 28.0, 48.0, 20.0, 58.0)
        diag = (
            "إيقاع اشتباك تكتيكي ممتاز (ضربة من كل 2–3 هجمات تقتل المدافع)." if tg == "ideal"
            else ("نسبة القتل بضربة واحدة مرتفعة جداً — قتال فوري بلا أخذ ورد!" if lethal_pct > 48.0 else "الهجمات العادية ضعيفة جداً ونادراً ما تحسم قتل وحش مقابل.")
        )
        rows.append((3, "نسبة الهجمات القاتلة للمدافع", f"{lethal_pct:.1f}%", "28% – 48%", "20% – 58%", st, diag, tg, pts))

        # 4. Avg Hits to Kill
        st, tg, pts = self._grade_range(hits_to_kill, 2.2, 3.6, 1.7, 4.5)
        diag = (
            f"رائع! الوحش يحتاج {hits_to_kill:.2f} ضربات لإسقاطه — معارك حقيقية وممتدة." if tg == "ideal"
            else ("الوحوش تسقط بسرعة زائدة بضربة ونصف تقريباً." if hits_to_kill < 2.2 else "الوحوش تتطلب ضربات كثيرة جداً لإسقاطها (إحساس بالإرهاق).")
        )
        rows.append((4, "متوسط الضربات اللازمة لقتل وحش", f"{hits_to_kill:.2f} ضربة", "2.2 – 3.6", "1.7 – 4.5", st, diag, tg, pts))

        # 5. Whiff / Dodged Attacks %
        st, tg, pts = self._grade_range(whiff_pct, 3.0, 14.0, 1.0, 22.0)
        diag = (
            "معدل مراوغة مثير ومتوازن دون إحباط اللاعب." if tg == "ideal"
            else ("المراوغة والصد الكامل نادرة جداً." if whiff_pct < 3.0 else f"نسبة فشل الهجمات ({whiff_pct:.1f}%) مرتفعة وتسبب إحباط حظ!")
        )
        rows.append((5, "نسبة الهجمات غير المؤثرة (مراوغة / 0 ضرر)", f"{whiff_pct:.1f}%", "3% – 14%", "1% – 22%", st, diag, tg, pts))

        # 6. Overkill %
        st, tg, pts = self._grade_range(overkill_pct, 8.0, 20.0, 4.0, 28.0)
        diag = (
            "ضرر محسوب ودقيق — لا يوجد هدر مبالغ فيه لأرقام الضرر." if tg == "ideal"
            else (f"تنبيه Overkill ({overkill_pct:.1f}%)! ضربات/مهارات تفيض كثيراً فوق دفاع الوحوش." if overkill_pct > 20.0 else "ضرر متقارب جداً مع دفاع الأهداف.")
        )
        rows.append((6, "نسبة الضرر الزائد المهدور (Overkill)", f"{overkill_pct:.1f}%", "8% – 20%", "4% – 28%", st, diag, tg, pts))

        # 7. Intercepted / Taunt-Blocked Strikes %
        st, tg, pts = self._grade_range(intercept_pct, 15.0, 38.0, 8.0, 48.0)
        diag = (
            "دور الاستدراج والحواجز ممتاز في حماية الوحوش والقائد." if tg == "ideal"
            else ("الاستدراج والحواجز شبه غائبة في اعتراض الهجمات!" if intercept_pct < 15.0 else "الاستدراج يسيطر على أغلب الاشتباكات.")
        )
        rows.append((7, "نسبة الهجمات المصدودة (استدراج / حاجز)", f"{intercept_pct:.1f}%", "15% – 38%", "8% – 48%", st, diag, tg, pts))

        # 8. Barrier Damage Absorbed Share %
        st, tg, pts = self._grade_range(bar_share, 8.0, 22.0, 3.0, 30.0)
        diag = (
            f"الحواجز تؤدي وظيفتها الدفاعية بقوة ({bar_share:.1f}% من ضرر الميدان)." if tg == "ideal"
            else (f"الحواجز مهمَلة عملياً ({bar_share:.1f}% فقط من الضرر الممتص — تحتاج تقوية أو إغراء أكبر بالديكات!)." if bar_share < 8.0 else "الحواجز تبتلع حصة مفرطة من القتال.")
        )
        rows.append((8, "نسبة الضرر الذي امتصته الحواجز (Barriers)", f"{bar_share:.1f}%", "8% – 22%", "3% – 30%", st, diag, tg, pts))

        # 9. Healing Recovery Ratio %
        st, tg, pts = self._grade_range(heal_ratio, 12.0, 30.0, 6.0, 42.0)
        diag = (
            f"توازن صحي للشفاء (يسترد {heal_ratio:.1f}% من إجمالي الضرر دون تجميد المباراة)." if tg == "ideal"
            else ("الشفاء ضعيف جداً مقارنة بالضرر." if heal_ratio < 12.0 else f"الشفاء قوي جداً ({heal_ratio:.1f}%) وقد يطيل المباريات للركود!")
        )
        rows.append((9, "نسبة الضرر المسترد بالعلاج (Heal Ratio)", f"{heal_ratio:.1f}%", "12% – 30%", "6% – 42%", st, diag, tg, pts))

        # 10. Mutual Kills (Double-KO %)
        st, tg, pts = self._grade_range(mutual_pct, 10.0, 24.0, 5.0, 32.0)
        diag = (
            "قتال متكافئ بروح التحدي — نسبة صحية من الموت المتبادل." if tg == "ideal"
            else ("نادراً ما يموت الوحشان معاً (لأن المهاجم إما لا يقتل أو لا يتضرر كفاية بالمرتد)." if mutual_pct < 10.0 else "قتال انتحاري مفرط (الوحشان يموتان معاً بكثرة).")
        )
        rows.append((10, "نسبة القتلات المتبادلة (يموت الوحشان معاً)", f"{mutual_pct:.1f}%", "10% – 24%", "5% – 32%", st, diag, tg, pts))

        # 11. Fair Trades %
        st, tg, pts = self._grade_range(fair_pct, 38.0, 65.0, 25.0, 75.0)
        diag = (
            "أغلب الاشتباكات فيها أخذ ورد وتبادل حقيقي للأضرار بين الوحشين!" if tg == "ideal"
            else ("التبادلات المتكافئة قليلة — القتال إما ضربات أحادية أو تجنّب." if fair_pct < 38.0 else "كل الاشتباكات تقريباً تبادلات مستمرة (هجوم منخفض جداً للقتل الفوري).")
        )
        rows.append((11, "نسبة التبادلات القتالية العادلة (أخذ ورد)", f"{fair_pct:.1f}%", "38% – 65%", "25% – 75%", st, diag, tg, pts))

        # 12. Unscathed Clean Kills %
        st, tg, pts = self._grade_range(clean_pct, 20.0, 45.0, 12.0, 58.0)
        diag = (
            "فرص جيدة للاستغلال التكتيكي (تجميد أو إضعاف لقتل الخصم دون ضرر مرتد)." if tg == "ideal"
            else ("نادراً ما ينجح مهاجم في قتل مدافع بلا خسارة (الهجوم العادي ضعيف للإنهاء)." if clean_pct < 20.0 else "أغلب القتلات من طرف واحد بلا أي رد فعل من المدافع!")
        )
        rows.append((12, "نسبة الاشتباكات النظيفة (قتل بلا ضرر مرتد)", f"{clean_pct:.1f}%", "20% – 45%", "12% – 58%", st, diag, tg, pts))

        # 13. Avg Active Skill Damage per Cast
        st, tg, pts = self._grade_range(avg_sk_dmg, 6.0, 14.0, 4.0, 18.0)
        diag = (
            f"قوة المهارات النشطة متزنة ({avg_sk_dmg:.1f} ضرر لكل تفعيل)." if tg == "ideal"
            else (f"المهارات النشطة تضرب بقوة مفرطة ({avg_sk_dmg:.1f} ضرر/تفعيل) مقارنة بالهجوم العادي!" if avg_sk_dmg > 14.0 else "ضرر المهارات النشطة خفيف جداً.")
        )
        rows.append((13, "متوسط ضرر المهارة النشطة للضربة الواحدة", f"{avg_sk_dmg:.1f} ضرر", "6 – 14 ضرر", "4 – 18 ضرر", st, diag, tg, pts))

        # 14. High-Impact / Swing Strikes %
        st, tg, pts = self._grade_range(swing_pct, 12.0, 30.0, 6.0, 40.0)
        diag = (
            "وجود ضربات مفصلية مؤثرة تغيّر مجرى المعركة وتصنع الإثارة." if tg == "ideal"
            else ("الضربات القتالية متشابهة وصغيرة ونادراً ما تُحدث نقلة مفاجئة." if swing_pct < 12.0 else "أغلب الضربات ضخمة ومفاجئة جداً.")
        )
        rows.append((14, "نسبة الهجمات الحاسمة (تغيّر المسار / تقتل T5+)", f"{swing_pct:.1f}%", "12% – 30%", "6% – 40%", st, diag, tg, pts))

        # 15. Combat Participation Rate (100 - 0-Atk Death)
        st, tg, pts = self._grade_range(partic_pct, 75.0, 100.0, 64.0, 100.0)
        diag = (
            f"رائع! {partic_pct:.1f}% من الوحوش القتالية تشارك في الهجوم فعلياً قبل موتها." if tg == "ideal"
            else f"{100-partic_pct:.1f}% من الوحوش القتالية تموت دون تنفيذ هجمة واحدة طوال حياتها!"
        )
        rows.append((15, "نسبة المشاركة القتالية للوحوش (>= 1 هجمة)", f"{partic_pct:.1f}%", "75% فأعلى", "64% – 74%", st, diag, tg, pts))

        # 16. Avg Attacks per Turn
        st, tg, pts = self._grade_range(atks_per_turn, 1.5, 3.5, 1.0, 4.5)
        diag = (
            f"طور قتال نشيط وممتع ({atks_per_turn:.2f} هجمة لكل دور)." if tg == "ideal"
            else ("عدد الهجمات بالدور قليل — اللعب يميل للمهارات أو التوقف." if atks_per_turn < 1.5 else "زخم هجمات مفرط في كل دور.")
        )
        rows.append((16, "متوسط الهجمات الفعلية في كل دور", f"{atks_per_turn:.2f} هجمة/دور", "1.5 – 3.5", "1.0 – 4.5", st, diag, tg, pts))

        # 17. Non-Combat Kills Share %
        st, tg, pts = self._grade_range(non_combat_pct, 35.0, 58.0, 25.0, 68.0)
        diag = (
            "مزيج ذهبي! القتال المباشر يسبب نصف القتلات والمهارات/السحر/DoTs تسبب النصف الآخر." if tg == "ideal"
            else (f"إنذار! {non_combat_pct:.1f}% من القتلات تأتي من خارج القتال (أغلبها المهارات النشطة) على حساب الاشتباك العادي!" if non_combat_pct > 58.0
                  else "القتال العادي يهيمن تماماً والسحر/المهارات شبه غائبة في الحسم.")
        )
        rows.append((17, "نسبة القتلات بتأثيرات غير قتالية (مهارات/سحر/DoT)", f"{non_combat_pct:.1f}%", "35% – 58%", "25% – 68%", st, diag, tg, pts))

        # 18. Near-Death Saves (1-2 DEF)
        st, tg, pts = self._grade_range(near_death_pct, 4.0, 14.0, 2.0, 20.0)
        diag = (
            "لحظات درامية رائعة — وحوش تنجو على الحافة عند 1–2 دفاع!" if tg == "ideal"
            else ("حالات النجاة على الحافة نادرة." if near_death_pct < 4.0 else "عدد كبير من الضربات يترك الوحوش عند 1–2 دفاع.")
        )
        rows.append((18, "نسبة النجاة على الحافة (تبقّي 1–2 DEF)", f"{near_death_pct:.1f}%", "4% – 14%", "2% – 20%", st, diag, tg, pts))

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
            "tot_strikes": tot_strikes,
            "clashes": clashes,
            "face_strikes": int(cq.get("face_strikes", 0)),
            "fair_live": max(0, int(cq.get("fair_trades", 0)) - int(cq.get("mutual_kills", 0))),
            "clean_kills": int(cq.get("clean_kills", 0)),
            "mutual_kills": int(cq.get("mutual_kills", 0)),
            "whiffs": int(cq.get("whiff_strikes", 0)),
            "tot_m_dmg": tot_m_dmg,
            "tot_f_dmg": tot_f_dmg,
            "overkill_dmg": int(cq.get("overkill_dmg", 0)),
            "tot_healed": tot_healed,
            "skill_dmg": int(cq.get("skill_dmg_total", 0)),
            "bar_abs": bar_abs,
        }

        for num, m_name, act_s, ideal_s, good_s, st_lbl, diag_s, tag, _ in rows:
            self.cq_tree.insert(
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
            cv.create_text(w // 2, h // 2, text=rtl("بانتظار بيانات التدريب الجديدة لجودة القتال — ابدأ التدريب أو اتصل بالجهاز الثاني!"), fill=COL_GOLD, font=(FONT_UI, 11, "bold"))
            return

        sc = int(d["score"])
        if sc >= 85:
            col, badge = COL_GREEN, "🟢 جودة قتال مثالية وممتعة (أخذ ورد وتكتيك AAA)"
        elif sc >= 70:
            col, badge = COL_GOLD, "🟡 جودة قتال جيدة (راجع المؤشرات الصفراء والحمراء لتحسين التبادل)"
        else:
            col, badge = COL_RED, "🔴 خلل واضح في جودة القتال (راجع المؤشرات الحمراء بالجدول فوراً)"

        cv.create_text(w - 12, 18, text=rtl(f"التقييم العام لمعيار جودة القتال:  {sc} / 100%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 14, "bold"))
        cv.create_text(w - 12, 44, text=rtl(badge), anchor="e", fill=col, font=(FONT_UI, 10, "bold"))
        cv.create_text(
            w - 12, 66,
            text=rtl(f"المعيار: 🟢 >=85% مثالي | 🟡 70–84% جيد | 🔴 <70% خطر  •  (إجمالي الضربات المقاسة: {d['tot_strikes']:,} ضربة • اشتباك وحوش: {d['clashes']:,})"),
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
            text=rtl(f"🟢 مثالي: {d['ideal_cnt']}/18   •   🟡 جيد: {d['good_cnt']}/18   •   🔴 تعديل: {d['bad_cnt']}/18"),
            fill=FG_TEXT, font=(FONT_UI, 8, "bold"),
        )

    # ------------------------------------------------------------------
    # Chart 1: Clash Outcome Breakdown (RTL Horizontal Bars)
    # ------------------------------------------------------------------
    def _draw_clash_outcomes(self) -> None:
        cv = self.cv_clash
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(195, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات اشتباك بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        tot = max(1, d["tot_strikes"])
        cl = max(1, d["clashes"])
        other_clash = max(0, cl - d["fair_live"] - d["clean_kills"] - d["mutual_kills"] - d["whiffs"])
        rows = [
            ("ضربة مباشرة لقائد الخصم (خانة فارغة)", d["face_strikes"], tot, COL_BLUE),
            ("تبادل حي بين وحشين (كلاهما تضرر)", d["fair_live"], tot, COL_GREEN),
            ("قتل نظيف (مات المدافع بلا ضرر مرتد)", d["clean_kills"], tot, COL_GOLD),
            ("قتل متبادل Double-KO (مات الوحشان معاً)", d["mutual_kills"], tot, COL_RED),
            ("مراوغة / صد كامل (0 ضرر)", d["whiffs"], tot, COL_PURPLE),
            ("ضربة خفيفة (تضرر المدافع دون موت/رد)", other_clash, tot, COL_CYAN),
        ]

        lbl_x = w - 12
        bar_r = w - 215
        bar_l = 96
        bar_w = max(60, bar_r - bar_l)
        row_h = max(30, (h - 22) // len(rows))

        for i, (lbl, cnt, denom, col) in enumerate(rows):
            y = 12 + i * row_h
            pct = 100.0 * cnt / denom
            cv.create_text(lbl_x, y + 9, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(bar_l, y + 3, bar_r, y + 16, fill="#242C3D", outline="")
            fw = max(2, int(bar_w * min(100.0, pct) / 100.0)) if cnt > 0 else 0
            if fw > 0:
                cv.create_rectangle(bar_r - fw, y + 3, bar_r, y + 16, fill=col, outline="")
            cv.create_text(bar_l - 8, y + 9, text=f"{pct:4.1f}% ({cnt})", anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Chart 2: Combat Damage, Healing, Overkill & Barrier Economy
    # ------------------------------------------------------------------
    def _draw_damage_economy(self) -> None:
        cv = self.cv_econ
        cv.delete("all")
        w = max(340, cv.winfo_width())
        h = max(195, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات ضرر وشفاء بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        rows = [
            ("الضرر الفعلي على الوحوش", d["tot_m_dmg"], COL_GOLD),
            ("الضرر المباشر على القادة", d["tot_f_dmg"], COL_RED),
            ("ضرر المهارات النشطة الخاصة", d["skill_dmg"], COL_PURPLE),
            ("الضرر المسترد بالعلاج (HP+DEF)", d["tot_healed"], COL_GREEN),
            ("الضرر الزائد المهدور (Overkill)", d["overkill_dmg"], COL_ORANGE),
            ("الضرر الممتص بالحواجز (Barriers)", d["bar_abs"], COL_TEAL),
        ]
        max_v = max(1, max(r[1] for r in rows))
        lbl_x = w - 12
        bar_r = w - 190
        bar_l = 74
        bar_w = max(60, bar_r - bar_l)
        row_h = max(30, (h - 22) // len(rows))

        for i, (lbl, val, col) in enumerate(rows):
            y = 12 + i * row_h
            cv.create_text(lbl_x, y + 9, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(bar_l, y + 3, bar_r, y + 16, fill="#242C3D", outline="")
            fw = max(2, int(bar_w * val / float(max_v))) if val > 0 else 0
            if fw > 0:
                cv.create_rectangle(bar_r - fw, y + 3, bar_r, y + 16, fill=col, outline="")
            cv.create_text(bar_l - 8, y + 9, text=f"{val:,}", anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Shared 80-Match Non-Destructive Live Probe
    # ------------------------------------------------------------------
    def _start_live_probe(self) -> None:
        if self._probe_running:
            return
        self._probe_running = True
        self.probe_btn.config(state="disabled", text=rtl("جاري تشغيل 80 مباراة..."))
        self.probe_status_var.set(rtl("جاري قياس جودة القتال عبر 80 مباراة حية..."))
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

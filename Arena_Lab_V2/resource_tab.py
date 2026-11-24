# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Pillar #5: Resource Economy Scorecard Tab (Arabic RTL).
Tracks all 14 mana & resource-economy indicators with:
  - Overall 0-100% Resource Economy Score (>=85% Ideal, >=70% Good, <70% Warning)
  - Visual Chart 1: Mana Cost Distribution (0..7) + Win Rate % per Cost Tier
  - Visual Chart 2: Turn-by-Turn Mana Available vs Spent Curve (T1..T14) + Mana Allocation Breakdown Bar
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


class ResourceEconomyTab:
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

        has_main = int(self.store.excitement.get("resource_economy", {}).get("mana_avail", 0)) > 0
        has_probe = bool(self.probe_data and int(self.probe_data.get("resource_economy", {}).get("mana_avail", 0)) > 0)
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
        self.scroll_canvas.bind("<Configure>", lambda e: self.scroll_canvas.itemconfigure(self._inner_win, width=max(400, e.width)))
        self.scroll_canvas.bind_all("<MouseWheel>", self._on_mousewheel)

        # Hero Score Banner
        hero = tk.Frame(self.inner, bg=BG_CARD, highlightbackground=BG_BORDER, highlightthickness=1, padx=14, pady=10)
        hero.pack(fill="x", pady=(2, 8))
        self.cv_score = tk.Canvas(hero, bg=BG_CARD, height=82, highlightthickness=0)
        self.cv_score.pack(fill="x")
        self.cv_score.bind("<Configure>", lambda _e: self._draw_score_banner())

        # Self-Explanatory 14-Indicator Diagnostic Table (Full Width First!)
        tbl_panel = self._make_panel(
            self.inner,
            "بطاقة التقييم التفصيلية لمعيار الموارد والتكلفة (14 مؤشراً — النتيجة + الجيد + المثالي + التشخيص التلقائي)",
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
        self.re_tree = ttk.Treeview(tbl_panel, columns=tuple(c for c, _, _ in headers), show="headings", height=14)
        for c, txt, w in headers:
            self.re_tree.heading(c, text=rtl(txt))
            self.re_tree.column(c, width=w, anchor="e" if c in ("metric", "diag") else "center")

        self.re_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.re_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.re_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.re_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        self.re_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.re_tree)

        # Visual Charts Stacked Vertically at Full Screen Width
        p_costs = self._make_panel(self.inner, "توزيع اللعب ونسبة الفوز حسب تكلفة البطاقة (0 – 7 مانا)", COL_GOLD)
        p_costs.pack(fill="x", pady=(0, 8))
        self.cv_costs = tk.Canvas(p_costs, bg=BG_PANEL, height=250, highlightthickness=0)
        self.cv_costs.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_costs.bind("<Configure>", lambda _e: self._draw_cost_tiers())

        p_curve = self._make_panel(self.inner, "منحنى المانا عبر الأدوار (المتاح مقابل المنفق) + توزيع صرف المانا", COL_CYAN)
        p_curve.pack(fill="x", pady=(0, 8))
        self.cv_curve = tk.Canvas(p_curve, bg=BG_PANEL, height=250, highlightthickness=0)
        self.cv_curve.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_curve.bind("<Configure>", lambda _e: self._draw_mana_curve())

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
        self._draw_cost_tiers()
        self._draw_mana_curve()

    @staticmethod
    def _grade_range(val: float, ideal_lo: float, ideal_hi: float, good_lo: float, good_hi: float) -> Tuple[str, str, int]:
        if ideal_lo <= val <= ideal_hi:
            return ("🟢 مثالي", "ideal", 100)
        if good_lo <= val <= good_hi:
            return ("🟡 جيد", "good", 72)
        return ("🔴 يحتاج تعديل", "bad", 28)

    def _compute_and_populate(self) -> None:
        self.re_tree.delete(*self.re_tree.get_children())
        ex = self._active_ex()
        re_d = ex.get("resource_economy", {})
        m_avail = int(re_d.get("mana_avail", 0))
        m_spent = int(re_d.get("mana_spent", 0))

        if m_avail <= 0:
            self._last_metrics = {"has_data": False, "score": 0, "ideal_cnt": 0, "good_cnt": 0, "bad_cnt": 0}
            self.re_tree.insert(
                "", "end",
                values=(
                    rtl("بانتظار بيانات التدريب — تظهر الأرقام تلقائياً فور بدء التدريب على المحاكي المحدّث أو الاتصال بالجهاز الثاني."),
                    rtl("بانتظار بيانات"), "--", "--", "--", rtl("تتبع اقتصاد المانا والموارد جاهز ومفعّل"), "!",
                ),
                tags=("nodata",),
            )
            return

        m_waste = int(re_d.get("mana_wasted", max(0, m_avail - m_spent)))
        turns_tot = max(1, int(re_d.get("turns_total", 0)))
        turns_unsp = int(re_d.get("turns_unspent", 0))
        sides = max(1, int(re_d.get("sides_count", 0)))

        eff_pct = 100.0 * m_spent / m_avail
        waste_pct = 100.0 * m_waste / m_avail
        unsp_turn_pct = 100.0 * turns_unsp / turns_tot
        avg_avail_t = m_avail / float(turns_tot)
        avg_spent_t = m_spent / float(turns_tot)
        avg_waste_t = m_waste / float(turns_tot)

        ccp = [int(x) for x in re_d.get("card_cost_played", [0] * 8)]
        ccw = [int(x) for x in re_d.get("card_cost_wins", [0] * 8)]
        cards_played = max(1, sum(ccp))
        m_cards = int(re_d.get("mana_spent_cards", 0))
        avg_card_cost = m_cards / float(cards_played)

        sac_per_side = int(re_d.get("sacrifices_total", 0)) / float(sides)
        aether_draw_per_side = int(re_d.get("aether_draws_total", 0)) / float(sides)
        cap_cnt = int(re_d.get("reach_cap_cnt", 0))
        avg_cap_turn = (int(re_d.get("reach_cap_sum", 0)) / float(cap_cnt)) if cap_cnt > 0 else 10.0

        m_spent_nz = max(1, m_spent)
        m_mon = int(re_d.get("mana_spent_monsters", 0))
        m_spl = int(re_d.get("mana_spent_spells", 0))
        m_skl = int(re_d.get("mana_spent_skills", 0))
        m_oth = int(re_d.get("mana_spent_other", 0))
        skill_mana_pct = 100.0 * m_skl / m_spent_nz

        low_cnt = ccp[0] + ccp[1] + ccp[2]
        high_cnt = ccp[5] + ccp[6] + ccp[7]
        low_share = 100.0 * low_cnt / cards_played
        high_share = 100.0 * high_cnt / cards_played

        low_wr = (100.0 * (ccw[0] + ccw[1] + ccw[2]) / low_cnt) if low_cnt > 0 else 50.0
        high_wr = (100.0 * (ccw[5] + ccw[6] + ccw[7]) / high_cnt) if high_cnt > 0 else 50.0
        cost_wr_gap = abs(high_wr - low_wr)

        m_off = int(re_d.get("mana_offense", 0))
        m_def = int(re_d.get("mana_defense", 0))
        def_mana_pct = 100.0 * m_def / max(1, m_off + m_def)

        val_per_mana = float(re_d.get("impact_value_sum", 0)) / m_spent_nz

        e_avail = max(1, int(re_d.get("early_mana_avail", 0)))
        e_spent = int(re_d.get("early_mana_spent", 0))
        early_eff_pct = 100.0 * e_spent / e_avail

        rows: List[Tuple[int, str, str, str, str, str, str, str, int]] = []

        # 1. Overall Mana Efficiency %
        st, tg, pts = self._grade_range(eff_pct, 75.0, 92.0, 62.0, 96.0)
        diag = (
            f"اقتصاد مانا ممتاز! {eff_pct:.1f}% من كل المانا المتاحة تُستثمر بفاعلية." if tg == "ideal"
            else ("استغلال المانا منخفض — بطاقات ثقيلة أو خيارات محدودة تترك مانا ضائعة!" if eff_pct < 75.0 else "استهلاك شبه كامل 100% للمانا (قد يقلل هامش التخطيط للأدوار القادمة).")
        )
        rows.append((1, "نسبة استغلال المانا المتاحة (Mana Efficiency)", f"{eff_pct:.1f}%", "75% – 92%", "62% – 96%", st, diag, tg, pts))

        # 2. Mana Waste %
        st, tg, pts = self._grade_range(waste_pct, 8.0, 25.0, 4.0, 38.0)
        diag = (
            f"هدر طبيعي وصحي ({waste_pct:.1f}% فقط) يترك هامش قرار تكتيكي." if tg == "ideal"
            else (f"هدر مرتفع للمانا ({waste_pct:.1f}%) — الديكات لا تجد ما تصرف عليه المانا!" if waste_pct > 25.0 else "هدر شبه معدوم — كل نقطة مانا تُصرف آلياً.")
        )
        rows.append((2, "نسبة المانا المهدورة بلا استخدام (Waste %)", f"{waste_pct:.1f}%", "8% – 25%", "4% – 38%", st, diag, tg, pts))

        # 3. Turns ending with unspent mana %
        st, tg, pts = self._grade_range(unsp_turn_pct, 15.0, 42.0, 8.0, 58.0)
        diag = (
            f"أغلب الأدوار منضبطة؛ {unsp_turn_pct:.1f}% فقط تنتهي بمانا متبقية." if tg == "ideal"
            else (f"في {unsp_turn_pct:.1f}% من الأدوار تبقى مانا غير مصروفة — راجع منحنى التكاليف!" if unsp_turn_pct > 42.0 else "نادر جداً بقاء مانا بنهاية الدور.")
        )
        rows.append((3, "نسبة الأدوار المنتهية بمانا غير مستغلة", f"{unsp_turn_pct:.1f}%", "15% – 42%", "8% – 58%", st, diag, tg, pts))

        # 4. Avg Available vs Spent per Turn (graded by avg wasted mana/turn)
        st, tg, pts = self._grade_range(avg_waste_t, 0.4, 1.5, 0.2, 2.5)
        diag = (
            f"تدفق مانا متوازن (يُصرف {avg_spent_t:.1f} من أصل {avg_avail_t:.1f} مانا بالدور — فائض {avg_waste_t:.1f})." if tg == "ideal"
            else (f"فائض مانا كبير في كل دور ({avg_waste_t:.1f} مانا تضيع بكل دور — اليد تفرغ أسرع من سقف المانا!)." if avg_waste_t > 1.5 else "صرف كامل تقريباً لكل نقطة مانا بالدور.")
        )
        rows.append((4, "متوسط المانا المنفقة مقابل المتاحة/دور", f"{avg_spent_t:.1f} / {avg_avail_t:.1f} مانا", "فائض 0.4–1.5", "فائض 0.2–2.5", st, diag, tg, pts))

        # 5. Avg Card Cost Played
        st, tg, pts = self._grade_range(avg_card_cost, 2.6, 4.2, 2.0, 5.0)
        diag = (
            f"منحنى تكلفة متوازن جداً (متوسط البطاقة الملعوبة {avg_card_cost:.2f} مانا)." if tg == "ideal"
            else ("الديكات تركز بشدة على الكروت الرخيصة جداً." if avg_card_cost < 2.6 else "الكروت الملعوبة ثقيلة جداً بالمتوسط.")
        )
        rows.append((5, "متوسط تكلفة البطاقة الملعوبة فعلياً", f"{avg_card_cost:.2f} مانا", "2.6 – 4.2 مانا", "2.0 – 5.0 مانا", st, diag, tg, pts))

        # 6. 1x/Turn Sacrifice (Unbounded Aether) & 1x/Turn Aether Draw (2 Mana) Rate / Match
        st, tg, pts = self._grade_range(sac_per_side, 1.5, 6.5, 0.5, 9.5)
        diag = (
            f"توازن ممتاز! التضحية (1×/دور بلا سقف: {sac_per_side:.1f}/مباراة) + سحب الأثير (1×/دور بـ 2 مانا: {aether_draw_per_side:.1f}/مباراة)." if tg == "ideal"
            else (f"تضحية قليلة ({sac_per_side:.1f}) • سحب الأثير بـ 2 مانا ({aether_draw_per_side:.1f}/مباراة)." if sac_per_side < 1.5 else f"تضحية مرتفعة ({sac_per_side:.1f}/مباراة) • سحب الأثير ({aether_draw_per_side:.1f}/مباراة).")
        )
        rows.append((6, "التضحية (1×/دور بلا سقف) وسحب الأثير (2 مانا)", f"حرق {sac_per_side:.1f} | سحب {aether_draw_per_side:.1f}", "1.5 – 6.5", "0.5 – 9.5", st, diag, tg, pts))

        # 7. Turn Reaching 10 Aether Milestone (Unbounded Aether!)
        st, tg, pts = self._grade_range(avg_cap_turn, 5.0, 8.5, 4.0, 10.5)
        diag = (
            f"تصاعد إيثر مفتوح متوازن! يصل اللاعب لـ 10 إيثر عند الجولة {avg_cap_turn:.1f} بالمتوسط (ويواصل النمو بلا سقف)." if tg == "ideal"
            else ("وصول سريع جداً إلى 10 إيثر (قبل الجولة 5.0)." if avg_cap_turn < 5.0 else "وصول بطيء إلى 10 إيثر.")
        )
        rows.append((7, "متوسط جولة بلوغ 10 إيثر (مفتوح بلا سقف)", f"جولة {avg_cap_turn:.1f}", "جولة 5.0–8.5", "جولة 4.0–10.5", st, diag, tg, pts))

        # 8. Active Skill Mana Share %
        st, tg, pts = self._grade_range(skill_mana_pct, 15.0, 34.0, 8.0, 45.0)
        diag = (
            f"توازن صحي! {skill_mana_pct:.1f}% من المانا تذهب للمهارات الفعّالة و{100-skill_mana_pct:.1f}% للبطاقات." if tg == "ideal"
            else (f"المهارات الفعّالة تبتلع {skill_mana_pct:.1f}% من إجمالي المانا (أكثر من اللازم على حساب لعب البطاقات!)." if skill_mana_pct > 34.0 else "استخدام المهارات الفعّالة ضعيف جداً.")
        )
        rows.append((8, "حصة المهارات الفعّالة من إجمالي إنفاق المانا", f"{skill_mana_pct:.1f}%", "15% – 34%", "8% – 45%", st, diag, tg, pts))

        # 9. Low-Cost (0-2) Share %
        st, tg, pts = self._grade_range(low_share, 25.0, 45.0, 15.0, 58.0)
        diag = (
            f"تغطية ممتازة لبداية المباراة (كروت 0–2 مانا تمثل {low_share:.1f}% من اللعب)." if tg == "ideal"
            else ("الكروت الرخيصة قليلة جداً — بطء محتمل بأول جولتين." if low_share < 25.0 else "هيمنة مفرطة للكروت الرخيصة (0–2 مانا)!")
        )
        rows.append((9, "حصة البطاقات الرخيصة (0–2 مانا) باللعب", f"{low_share:.1f}%", "25% – 45%", "15% – 58%", st, diag, tg, pts))

        # 10. High-Cost (5-7) Share %
        st, tg, pts = self._grade_range(high_share, 14.0, 32.0, 8.0, 42.0)
        diag = (
            f"الكروت الثقيلة والزعماء (5–7 مانا) حاضرة وممتعة ({high_share:.1f}% من اللعب)." if tg == "ideal"
            else (f"الكروت الثقيلة (5–7 مانا) نادرة ({high_share:.1f}% فقط) — غالباً أبطأ أو أغلى من قيمتها!" if high_share < 14.0 else "كثرة الكروت الثقيلة تبطئ إيقاع اليد.")
        )
        rows.append((10, "حصة البطاقات الثقيلة (5–7 مانا) باللعب", f"{high_share:.1f}%", "14% – 32%", "8% – 42%", st, diag, tg, pts))

        # 11. High-Cost vs Low-Cost Win Rate Balance
        st, tg, pts = self._grade_range(cost_wr_gap, 0.0, 5.0, 0.0, 9.0)
        fav = "الثقيلة" if high_wr >= low_wr else "الرخيصة"
        diag = (
            f"توازن تكلفة ممتاز! فوز الرخيصة {low_wr:.1f}% مقابل الثقيلة {high_wr:.1f}% (فرق {cost_wr_gap:.1f}% فقط)." if tg == "ideal"
            else f"انحياز لصالح الكروت {fav}! (الرخيصة {low_wr:.1f}% مقابل الثقيلة {high_wr:.1f}% — فرق {cost_wr_gap:.1f}%)."
        )
        rows.append((11, "توازن فوز الثقيلة (5–7) مقابل الرخيصة (0–2)", f"فرق {cost_wr_gap:.1f}% ({high_wr:.0f}%/{low_wr:.0f}%)", "فرق <= 5%", "فرق <= 9%", st, diag, tg, pts))

        # 12. Defense vs Offense Mana Spend %
        st, tg, pts = self._grade_range(def_mana_pct, 25.0, 48.0, 16.0, 58.0)
        diag = (
            f"توازن تكتيكي سليم ({def_mana_pct:.1f}% دفاع/دعم/تعطيل مقابل {100-def_mana_pct:.1f}% هجوم)." if tg == "ideal"
            else (f"طغيان هجومي بالمانا! ({100-def_mana_pct:.1f}% هجوم مقابل {def_mana_pct:.1f}% فقط دفاع/دعم!)." if def_mana_pct < 25.0 else "طغيان دفاعي قد يطيل المباريات بلا حسم.")
        )
        rows.append((12, "توزيع المانا: حصة الدفاع والدعم مقابل الهجوم", f"{def_mana_pct:.1f}% دفاع", "25% – 48%", "16% – 58%", st, diag, tg, pts))

        # 13. Impact Value per 1 Mana Spent
        st, tg, pts = self._grade_range(val_per_mana, 10.0, 22.0, 7.0, 28.0)
        diag = (
            f"مردود مانا متوازن ومُرضٍ ({val_per_mana:.1f} نقطة أثر لكل 1 مانا مُنفقة)." if tg == "ideal"
            else ("مردود المانا ضعيف — البطاقات تشعر أنها غالية مقابل تأثيرها." if val_per_mana < 10.0 else f"تضخم قيمة المانا ({val_per_mana:.1f} نقطة/مانا — غالباً من كروت 0–1 مانا أو مهارات رخيصة جداً!).")
        )
        rows.append((13, "متوسط القيمة المحققة لكل 1 مانا منفق", f"{val_per_mana:.1f} نقطة/مانا", "10 – 22 نقطة", "7 – 28 نقطة", st, diag, tg, pts))

        # 14. Early-Game (T1-T5) Mana Efficiency %
        st, tg, pts = self._grade_range(early_eff_pct, 68.0, 92.0, 55.0, 96.0)
        diag = (
            f"بداية مباراة سلسة! {early_eff_pct:.1f}% من مانا أول 5 أدوار تُستغل دون تعثر." if tg == "ideal"
            else (f"تعثر في أول 5 أدوار! ({early_eff_pct:.1f}% فقط من المانا المبكرة تُستخدم — نقص كروت 1–3 مانا)." if early_eff_pct < 68.0 else "استهلاك شبه تام لمانا البداية.")
        )
        rows.append((14, "كفاءة استغلال المانا المبكرة (أدوار 1–5)", f"{early_eff_pct:.1f}%", "68% – 92%", "55% – 96%", st, diag, tg, pts))

        avg_score = int(round(sum(r[8] for r in rows) / len(rows)))
        ideal_cnt = sum(1 for r in rows if r[7] == "ideal")
        good_cnt = sum(1 for r in rows if r[7] == "good")
        bad_cnt = sum(1 for r in rows if r[7] == "bad")

        tma = [int(x) for x in re_d.get("turn_mana_avail", [0] * 15)]
        tms = [int(x) for x in re_d.get("turn_mana_spent", [0] * 15)]
        tmc = [int(x) for x in re_d.get("turn_mana_cnt", [0] * 15)]

        self._last_metrics = {
            "has_data": True,
            "score": avg_score,
            "ideal_cnt": ideal_cnt,
            "good_cnt": good_cnt,
            "bad_cnt": bad_cnt,
            "mana_avail": m_avail,
            "mana_spent": m_spent,
            "mana_waste": m_waste,
            "ccp": ccp,
            "ccw": ccw,
            "tma": tma,
            "tms": tms,
            "tmc": tmc,
            "spend_split": (m_mon, m_skl, m_spl, m_oth, m_waste),
        }

        for num, m_name, act_s, ideal_s, good_s, st_lbl, diag_s, tag, _ in rows:
            self.re_tree.insert(
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
            cv.create_text(w // 2, h // 2, text=rtl("بانتظار بيانات التدريب الجديدة لاقتصاد الموارد — ابدأ التدريب أو اتصل بالجهاز الثاني!"), fill=COL_GOLD, font=(FONT_UI, 11, "bold"))
            return

        sc = int(d["score"])
        if sc >= 85:
            col, badge = COL_GREEN, "🟢 اقتصاد مانا وتكلفة مثالي ومتوازن (AAA)"
        elif sc >= 70:
            col, badge = COL_GOLD, "🟡 اقتصاد مانا جيد (راجع المؤشرات الصفراء/الحمراء لتحسين توزيع المانا)"
        else:
            col, badge = COL_RED, "🔴 خلل في اقتصاد المانا والتكلفة (هدر مفرط أو اختلال فئات — راجع الأحمر!)"

        cv.create_text(w - 12, 18, text=rtl(f"التقييم العام لمعيار الموارد والتكلفة:  {sc} / 100%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 14, "bold"))
        cv.create_text(w - 12, 44, text=rtl(badge), anchor="e", fill=col, font=(FONT_UI, 10, "bold"))
        cv.create_text(
            w - 12, 66,
            text=rtl(f"المعيار: 🟢 >=85% مثالي | 🟡 70–84% جيد | 🔴 <70% خطر  •  (إجمالي المانا المتاحة: {d['mana_avail']:,} • المنفقة: {d['mana_spent']:,})"),
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
    # Chart 1: Mana Cost Distribution (0..7) + Win Rate % per Tier
    # ------------------------------------------------------------------
    def _draw_cost_tiers(self) -> None:
        cv = self.cv_costs
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(210, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات تكاليف بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        ccp = d["ccp"]
        ccw = d["ccw"]
        tot = max(1, sum(ccp))
        max_pct = max(12.0, max(100.0 * c / tot for c in ccp))

        cv.create_text(
            w - 10, 11,
            text=rtl("الشريط = حصة اللعب %  |  اللون والرقم الأيسر = نسبة الفوز WR%"),
            anchor="e", fill=FG_MUTED, font=(FONT_UI, 8),
        )

        lbl_x = w - 10
        bar_r = w - 76
        bar_l = 122
        bar_w = max(60, bar_r - bar_l)
        row_h = max(24, (h - 30) // 8)

        for ci in range(8):
            y = 24 + ci * row_h
            cnt = ccp[ci]
            pct = 100.0 * cnt / tot
            wr = (100.0 * ccw[ci] / cnt) if cnt >= 10 else 50.0
            if cnt < 10:
                col = FG_MUTED
            elif 46.0 <= wr <= 54.0:
                col = COL_GREEN
            elif 42.0 <= wr <= 58.0:
                col = COL_GOLD
            else:
                col = COL_RED

            lbl = f"{ci}+ مانا" if ci == 7 else f"{ci} مانا"
            cv.create_text(lbl_x, y + 8, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(bar_l, y + 2, bar_r, y + 14, fill="#242C3D", outline="")
            fw = max(2, int(bar_w * min(1.0, pct / max_pct))) if cnt > 0 else 0
            if fw > 0:
                cv.create_rectangle(bar_r - fw, y + 2, bar_r, y + 14, fill=col, outline="")
            wr_s = f"WR {wr:4.1f}%" if cnt >= 10 else "WR --"
            cv.create_text(bar_l - 6, y + 8, text=f"{pct:4.1f}% | {wr_s}", anchor="e", fill=col, font=(FONT_UI, 7, "bold"))

    # ------------------------------------------------------------------
    # Chart 2: Turn-by-Turn Mana Available vs Spent + Allocation Bar
    # ------------------------------------------------------------------
    def _draw_mana_curve(self) -> None:
        cv = self.cv_curve
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(210, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات منحنى مانا بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        tma, tms, tmc = d["tma"], d["tms"], d["tmc"]
        # Top section: Turn 1..14 Paired Bars (Available vs Spent)
        top_y = 22
        bot_y = int(h * 0.64)
        plot_h = max(70, bot_y - top_y)
        left_x, right_x = 34, w - 14
        plot_w = max(180, right_x - left_x)

        # Legend
        cv.create_rectangle(w - 22, 6, w - 12, 14, fill="#3B475E", outline="")
        cv.create_text(w - 26, 10, text=rtl("المتاح"), anchor="e", fill=FG_MUTED, font=(FONT_UI, 7, "bold"))
        cv.create_rectangle(w - 86, 6, w - 76, 14, fill=COL_CYAN, outline="")
        cv.create_text(w - 90, 10, text=rtl("المنفق فعلياً"), anchor="e", fill=COL_CYAN, font=(FONT_UI, 7, "bold"))

        n_t = 14
        max_m = 11.0
        for gi in (0, 5, 10):
            gy = bot_y - int(plot_h * gi / max_m)
            cv.create_line(left_x, gy, right_x, gy, fill="#222A3A")
            cv.create_text(left_x - 5, gy, text=str(gi), anchor="e", fill=FG_MUTED, font=(FONT_UI, 7))

        step_x = plot_w / float(n_t)
        bw = max(6, int(step_x * 0.62))
        for i in range(n_t):
            c = tmc[i]
            av = (tma[i] / float(c)) if c > 0 else 0.0
            sp = (tms[i] / float(c)) if c > 0 else 0.0
            # RTL x-order: Turn 1 on RIGHT, Turn 14 on LEFT
            cx = right_x - (i + 0.5) * step_x
            x0, x1 = cx - bw / 2, cx + bw / 2
            ah = int(plot_h * min(max_m, av) / max_m)
            sh = int(plot_h * min(max_m, sp) / max_m)
            if ah > 0:
                cv.create_rectangle(x0, bot_y - ah, x1, bot_y, fill="#334155", outline="")
            if sh > 0:
                eff_t = (sp / av) if av > 0 else 0.0
                scol = COL_GREEN if eff_t >= 0.78 else (COL_CYAN if eff_t >= 0.62 else COL_GOLD)
                cv.create_rectangle(x0 + 1, bot_y - sh, x1 - 1, bot_y, fill=scol, outline="")
            if i % 2 == 0 or i == n_t - 1:
                cv.create_text(cx, bot_y + 9, text=f"T{i+1}", fill=FG_MUTED, font=(FONT_UI, 7))

        # Bottom section: Exact Stacked Mana Breakdown Bar
        sb_top = bot_y + 26
        cv.create_text(w - 12, sb_top, text=rtl("أين تذهب كل نقطة مانا متاحة في المباراة؟ (مجموع 100%)"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
        m_mon, m_skl, m_spl, m_oth, m_wst = d["spend_split"]
        tot_m = max(1, m_mon + m_skl + m_spl + m_oth + m_wst)
        segs = [
            ("وحوش", m_mon, COL_BLUE),
            ("مهارات", m_skl, COL_PINK),
            ("سحر", m_spl, COL_CYAN),
            ("حواجز/بيئة", m_oth, COL_GOLD),
            ("مهدور", m_wst, "#475569"),
        ]
        bar_y0, bar_y1 = sb_top + 12, sb_top + 30
        cur_r = right_x
        total_w = right_x - left_x
        for idx_s, (lbl, val, col) in enumerate(segs):
            frac = val / float(tot_m)
            sw = int(round(total_w * frac)) if idx_s < len(segs) - 1 else max(0, cur_r - left_x)
            if sw > 0:
                cv.create_rectangle(cur_r - sw, bar_y0, cur_r, bar_y1, fill=col, outline="")
                if sw >= 38:
                    cv.create_text(cur_r - sw / 2, (bar_y0 + bar_y1) / 2, text=f"{frac*100:.0f}%", fill="#FFFFFF", font=(FONT_UI, 7, "bold"))
                cur_r -= sw

        # Compact RTL legend under stacked bar
        lx = right_x
        ly = bar_y1 + 13
        for lbl, val, col in segs:
            pct = 100.0 * val / tot_m
            txt = rtl(f"{lbl} {pct:.0f}%")
            cv.create_rectangle(lx - 8, ly - 4, lx, ly + 4, fill=col, outline="")
            cv.create_text(lx - 11, ly, text=txt, anchor="e", fill=FG_TEXT, font=(FONT_UI, 7))
            lx -= max(64, int(total_w / 5.0))

    # ------------------------------------------------------------------
    # Shared 80-Match Non-Destructive Live Probe
    # ------------------------------------------------------------------
    def _start_live_probe(self) -> None:
        if self._probe_running:
            return
        self._probe_running = True
        self.probe_btn.config(state="disabled", text=rtl("جاري تشغيل 80 مباراة..."))
        self.probe_status_var.set(rtl("جاري قياس اقتصاد المانا والتكلفة عبر 80 مباراة حية..."))
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

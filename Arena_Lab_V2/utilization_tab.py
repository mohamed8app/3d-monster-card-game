# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Pillar #4: Card Utilization Scorecard Tab (Arabic RTL).
Tracks all 14 card-utilization metrics with:
  - Overall 0-100% Utilization Score (>=85% Ideal, >=70% Good, <70% Warning)
  - Visual Chart 1: Played in Matches vs Drafted in Latest Settled Decks (6 Card Types)
  - Visual Chart 2: Drawn Card Fate Funnel (Planned Play | Top-Deck Play | Sacrificed | Left in Hand | Burned Hand-7)
  - Self-Explanatory 14-Row Diagnostic Table (Actual | Ideal | Good | Status | Auto-Diagnosis)
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
from typing import Any, Callable, Dict, List, Optional, Tuple

from engine.card_model import CardDB
from engine.roster_40 import ROSTER_40
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


class CardUtilizationTab:
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

        has_main = int(self.store.excitement.get("card_utilization", {}).get("played_total", 0)) > 0
        has_probe = bool(self.probe_data and int(self.probe_data.get("card_utilization", {}).get("played_total", 0)) > 0)
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
            "بطاقة التقييم التفصيلية لمعيار استخدام البطاقات (14 مؤشراً — النتيجة + الجيد + المثالي + التشخيص التلقائي)",
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
        self.cu_tree = ttk.Treeview(tbl_panel, columns=tuple(c for c, _, _ in headers), show="headings", height=14)
        for c, txt, w in headers:
            self.cu_tree.heading(c, text=rtl(txt))
            self.cu_tree.column(c, width=w, anchor="e" if c in ("metric", "diag") else "center")

        self.cu_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.cu_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.cu_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.cu_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        self.cu_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.cu_tree)

        # Visual Charts Stacked Vertically at Full Screen Width
        p_types = self._make_panel(self.inner, "مقارنة الفئات: اللعب الفعلي بالمباريات مقابل الاختيار في أحدث الديكات", COL_GOLD)
        p_types.pack(fill="x", pady=(0, 8))
        self.cv_types = tk.Canvas(p_types, bg=BG_PANEL, height=240, highlightthickness=0)
        self.cv_types.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_types.bind("<Configure>", lambda _e: self._draw_type_comparison())

        p_fate = self._make_panel(self.inner, "مصير البطاقات المسحوبة (تخطيط / فوري / تضحية / متبقّي / محترق)", COL_CYAN)
        p_fate.pack(fill="x", pady=(0, 8))
        self.cv_fate = tk.Canvas(p_fate, bg=BG_PANEL, height=240, highlightthickness=0)
        self.cv_fate.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_fate.bind("<Configure>", lambda _e: self._draw_card_fate())

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
        self._draw_type_comparison()
        self._draw_card_fate()

    @staticmethod
    def _grade_range(val: float, ideal_lo: float, ideal_hi: float, good_lo: float, good_hi: float) -> Tuple[str, str, int]:
        if ideal_lo <= val <= ideal_hi:
            return ("🟢 مثالي", "ideal", 100)
        if good_lo <= val <= good_hi:
            return ("🟡 جيد", "good", 72)
        return ("🔴 يحتاج تعديل", "bad", 28)

    def _compute_settled_deck_type_shares(self) -> List[float]:
        """Returns % shares across all settled 50-card decks: [monster, barrier, spell, leader, covenant, env]."""
        cnts = [0, 0, 0, 0, 0, 0]
        tot = 0
        for spec in ROSTER_40:
            d = self.store.roster.get(spec.model_id, {}).get("settled_deck") or []
            if len(d) != 50:
                continue
            for cid in d:
                c = self.db.cards.get(int(cid))
                if not c:
                    continue
                tot += 1
                if c.type == "monster":
                    cnts[1 if c.is_barrier else 0] += 1
                elif c.type == "spell":
                    cnts[2] += 1
                elif c.type == "leader":
                    cnts[3] += 1
                elif c.type == "covenant":
                    cnts[4] += 1
                elif c.type == "environment":
                    cnts[5] += 1
        if tot <= 0:
            return [0.0] * 6
        return [100.0 * x / tot for x in cnts]

    def _compute_and_populate(self) -> None:
        self.cu_tree.delete(*self.cu_tree.get_children())
        ex = self._active_ex()
        cu = ex.get("card_utilization", {})
        played_tot = int(cu.get("played_total", 0))
        drawn_tot = int(cu.get("drawn_total", 0))

        if played_tot <= 0 or drawn_tot <= 0:
            self._last_metrics = {"has_data": False, "score": 0, "ideal_cnt": 0, "good_cnt": 0, "bad_cnt": 0}
            self.cu_tree.insert(
                "", "end",
                values=(
                    rtl("بانتظار بيانات التدريب — تظهر الأرقام تلقائياً فور بدء التدريب على المحاكي المحدّث أو الاتصال بالجهاز الثاني."),
                    rtl("بانتظار بيانات"), "--", "--", "--", rtl("تتبع استخدام البطاقات جاهز ومفعّل"), "!",
                ),
                tags=("nodata",),
            )
            return

        sides = max(1, int(cu.get("sides_count", 0)))
        sac_tot = int(cu.get("sacrificed_total", 0))
        burn7_tot = int(cu.get("burned_hand7_total", 0))
        rem_end = int(cu.get("hand_end_remaining", 0))

        # Total cards that entered hand (deck draws + recalls + grave returns + hand clones)
        hand_entries = max(drawn_tot, played_tot + rem_end + sac_tot + burn7_tot)
        played_of_drawn = 100.0 * played_tot / hand_entries
        unused_end_pct = 100.0 * rem_end / hand_entries
        avg_cards_match = played_tot / float(sides)

        cond_att = int(cu.get("cond_attempts", 0))
        cond_ok = int(cu.get("cond_success", 0))
        cond_pct = (100.0 * cond_ok / cond_att) if cond_att > 0 else 100.0

        # Global 1,470 catalog coverage & win-rate balance from store.card_stats
        tot_catalog = max(1, self.db.num_cards)
        seen_cards = sum(1 for st in self.store.card_stats.values() if int(st.get("drafted", 0)) > 0)
        coverage_pct = 100.0 * seen_cards / tot_catalog
        neglected_cnt = max(0, tot_catalog - seen_cards)

        tested_20 = []
        for st in self.store.card_stats.values():
            pl = int(st.get("played", 0))
            if pl >= 20:
                tested_20.append(100.0 * int(st.get("played_wins", 0)) / pl)
        bal_cards_pct = (100.0 * sum(1 for wr in tested_20 if 44.0 <= wr <= 56.0) / len(tested_20)) if tested_20 else 50.0
        op_cnt = sum(1 for wr in tested_20 if wr > 58.0)

        avg_play_turn = float(cu.get("play_turn_sum", 0)) / played_tot
        topdeck_pct = 100.0 * int(cu.get("played_on_draw_turn", 0)) / played_tot

        hc_tot = max(1, int(cu.get("hand_checks_cards", 0)))
        unplay_pct = 100.0 * int(cu.get("hand_unplayable_cards", 0)) / hc_tot

        low_val_pct = 100.0 * int(cu.get("low_value_plays", 0)) / played_tot

        tc = ex.get("turn_curve", {})
        t_cnts = sum(int(tc.get(str(t), {}).get("turns", 0)) for t in range(1, 26))
        hs_sum = sum(int(tc.get(str(t), {}).get("hs", 0)) for t in range(1, 26))
        he_sum = sum(int(tc.get(str(t), {}).get("he", 0)) for t in range(1, 26))
        if t_cnts > 0 and hs_sum > 0:
            avg_hand = 0.5 * (hs_sum + he_sum) / t_cnts
        else:
            avg_hand = hc_tot / max(1.0, float(ex.get("game_pace", {}).get("total_player_turns", 1)))
        burn7_pct = 100.0 * burn7_tot / drawn_tot

        avg_cycles = (drawn_tot / float(sides)) / 50.0
        full_cycles_pct = 100.0 * int(cu.get("deck_cycles_done", 0)) / sides

        pbt = cu.get("played_by_type", [0] * 6)
        pbt_sum = max(1, sum(int(x) for x in pbt))
        non_monster_pct = 100.0 * (int(pbt[2]) + int(pbt[3]) + int(pbt[4]) + int(pbt[5])) / pbt_sum

        avg_val_card = float(cu.get("impact_value_sum", 0)) / played_tot

        rows: List[Tuple[int, str, str, str, str, str, str, str, int]] = []

        # 1. Played / Drawn %
        st, tg, pts = self._grade_range(played_of_drawn, 55.0, 80.0, 42.0, 88.0)
        diag = (
            f"استغلال صحي! {played_of_drawn:.1f}% من الأوراق المسحوبة تُلعب فعلياً بالملعب." if tg == "ideal"
            else ("نسبة كبيرة من المسحوب لا تُلعب (بسبب التضحية أو غلاء المانا أو بقائها ميتة باليد)." if played_of_drawn < 55.0 else "استهلاك شبه كامل لكل ورقة مسحوبة.")
        )
        rows.append((1, "نسبة البطاقات المسحوبة التي تم لعبها", f"{played_of_drawn:.1f}%", "55% – 80%", "42% – 88%", st, diag, tg, pts))

        # 2. Unused in Hand at Finish %
        st, tg, pts = self._grade_range(unused_end_pct, 10.0, 26.0, 5.0, 36.0)
        diag = (
            "اللاعب ينهي المباراة بيد متوازنة (لديه خيارات دون تكدّس بطاقات ميتة)." if tg == "ideal"
            else ("بطاقات كثيرة تبقى عالقة بلا استخدام عند نهاية المباراة!" if unused_end_pct > 26.0 else "اللاعب يفرغ يده بالكامل تقريباً قبل نهاية المباراة.")
        )
        rows.append((2, "نسبة البطاقات المتبقية بلا استخدام بالنهاية", f"{unused_end_pct:.1f}%", "10% – 26%", "5% – 36%", st, diag, tg, pts))

        # 3. Avg Cards Played / Match per Side
        st, tg, pts = self._grade_range(avg_cards_match, 12.0, 24.0, 8.0, 30.0)
        diag = (
            f"تفاعل ممتاز مع ديك الـ 50 بطاقة ({avg_cards_match:.1f} بطاقة ملعوبة لكل لاعب بالمباراة)." if tg == "ideal"
            else ("عدد قليل جداً من كروت الديك يُلعب بالمباراة." if avg_cards_match < 12.0 else "مباراة طويلة يُلعب فيها أكثر من نصف الديك.")
        )
        rows.append((3, "متوسط البطاقات المستخدمة في المباراة/لاعب", f"{avg_cards_match:.1f} بطاقة", "12 – 24 بطاقة", "8 – 30 بطاقة", st, diag, tg, pts))

        # 4. Tribal Leader Aura Fit % (Leaders matching Deck/Board Tribe)
        st, tg, pts = self._grade_range(cond_pct, 80.0, 100.0, 60.0, 100.0)
        diag = (
            f"تآزر قبلي ممتاز! {cond_pct:.0f}% من تفعيلات التمائم ({cond_ok}/{cond_att}) تطابق عشيرة الديك وتفيد وحوشها باستمرار." if cond_att > 0 and tg == "ideal"
            else ("لا توجد تفعيلات تمائم في هذه العينة." if cond_att == 0 else f"تنبيه: {100-cond_pct:.0f}% من التمائم لُعبت في ديكات لا تضم وحوشاً من عشيرتها!")
        )
        rows.append((4, "تطابق تمائم القادة المستمرة مع عشيرة الديك", f"{cond_pct:.1f}% ({cond_ok}/{cond_att})", "80% فأعلى", "60% – 79%", st, diag, tg, pts))

        # 5. Catalog Coverage (1,470 cards)
        st, tg, pts = self._grade_range(coverage_pct, 65.0, 100.0, 40.0, 100.0)
        diag = (
            f"تغطية ضخمة! {seen_cards:,} بطاقة من الـ 1,470 تم اختبارها بالديكات (المهملة: {neglected_cnt})." if tg == "ideal"
            else f"{neglected_cnt:,} بطاقة لم تُسحب في أي ديك بعد (تغطية {coverage_pct:.1f}% فقط من الكتالوج)."
        )
        rows.append((5, "نسبة تغطية كتالوج الـ 1,470 بطاقة", f"{coverage_pct:.1f}% ({seen_cards:,})", "65% فأعلى", "40% – 64%", st, diag, tg, pts))

        # 6. Balanced Card Win-Rate Share (44-56%)
        st, tg, pts = self._grade_range(bal_cards_pct, 75.0, 100.0, 58.0, 100.0)
        diag = (
            f"توازن قوي! {bal_cards_pct:.0f}% من البطاقات المختبرة داخل النطاق الصحي 44–56% (المكسورة >58%: {op_cnt})." if tg == "ideal"
            else f"تفاوت ملحوظ في فوز البطاقات ({bal_cards_pct:.0f}% فقط داخل 44–56%، ويوجد {op_cnt} بطاقة فوق 58% فوز!)."
        )
        rows.append((6, "نسبة البطاقات المتوازنة (فوز 44%–56%)", f"{bal_cards_pct:.1f}% (OP:{op_cnt})", "75% فأعلى", "58% – 74%", st, diag, tg, pts))

        # 7. Avg Turn Card is Played
        st, tg, pts = self._grade_range(avg_play_turn, 5.5, 11.5, 4.0, 14.0)
        diag = (
            f"تدرّج مانا سليم (متوسط دور لعب البطاقة عند الجولة {avg_play_turn:.1f})." if tg == "ideal"
            else ("أغلب اللعب مكدّس في البداية فقط." if avg_play_turn < 5.5 else "اللعب متأخر جداً بسبب طول المباريات.")
        )
        rows.append((7, "متوسط الدور الذي تُستخدم فيه البطاقة", f"جولة {avg_play_turn:.1f}", "5.5 – 11.5", "4.0 – 14.0", st, diag, tg, pts))

        # 8. Played Immediately on Draw Turn (Top-Deck %)
        st, tg, pts = self._grade_range(topdeck_pct, 18.0, 42.0, 10.0, 55.0)
        diag = (
            f"قرار استراتيجي ممتاز! {100-topdeck_pct:.0f}% من البطاقات تُحتفظ بها للوقت المناسب و{topdeck_pct:.0f}% تُلعب فور سحبها." if tg == "ideal"
            else (f"تحذير لعب آلي (Top-Deck)! {topdeck_pct:.1f}% من البطاقات تُرمى فوراً لحظة سحبها بلا تخطيط." if topdeck_pct > 42.0 else "تكديس مفرط للبطاقات باليد دون لعبها.")
        )
        rows.append((8, "نسبة البطاقات الملعوبة فور سحبها (Top-Deck)", f"{topdeck_pct:.1f}%", "18% – 42%", "10% – 55%", st, diag, tg, pts))

        # 9. Unplayable-in-Hand Rate %
        st, tg, pts = self._grade_range(unplay_pct, 18.0, 42.0, 10.0, 55.0)
        diag = (
            "نسبة طبيعية من الأوراق الثقيلة باليد تنتظر دور مانا أعلى — قرارات مانا صحية." if tg == "ideal"
            else (f"انغلاق خيارات! {unplay_pct:.1f}% من أوراق اليد غير قابلة للعب في كل دور (تكاليف مرتفعة أو شروط صعبة)." if unplay_pct > 42.0 else "كل أوراق اليد رخيصة جداً وقابلة للعب دائماً.")
        )
        rows.append((9, "نسبة أوراق اليد غير القابلة للعب في الدور", f"{unplay_pct:.1f}%", "18% – 42%", "10% – 55%", st, diag, tg, pts))

        # 10. Low-Value / Zero-Impact Plays %
        st, tg, pts = self._grade_range(low_val_pct, 0.0, 14.0, 0.0, 24.0)
        diag = (
            f"كفاءة عالية! {100-low_val_pct:.1f}% من البطاقات الملعوبة حققت أثراً حقيقياً بالمعركة." if tg == "ideal"
            else f"هدر ملحوظ! {low_val_pct:.1f}% من البطاقات الملعوبة انتهت بلا أثر عملي (موت فوري أو سحر/قائد بلا نتيجة)."
        )
        rows.append((10, "نسبة البطاقات عديمة الأثر العملي (Wasted)", f"{low_val_pct:.1f}%", "14% فأقل", "14% – 24%", st, diag, tg, pts))

        # 11. Avg Hand Size & Hand-7 Burn %
        if 3.0 <= avg_hand <= 5.2 and burn7_pct <= 4.0:
            st, tg, pts = "🟢 مثالي", "ideal", 100
            diag = f"يد صحية جداً ({avg_hand:.1f} أوراق بالمتوسط، وحرق سقف اليد 7 فقط {burn7_pct:.1f}%)."
        elif 2.0 <= avg_hand <= 6.0 and burn7_pct <= 10.0:
            st, tg, pts = "🟡 جيد", "good", 72
            diag = f"اقتصاد يد مقبول ({avg_hand:.1f} أوراق بالمتوسط، حرق اليد-7 = {burn7_pct:.1f}%)."
        else:
            st, tg, pts = "🔴 يحتاج تعديل", "bad", 28
            diag = f"مشكلة ازدحام أو فراغ اليد ({avg_hand:.1f} أوراق • حرق سقف-7 = {burn7_pct:.1f}%)!"
        rows.append((11, "متوسط أوراق اليد وحرق سقف الـ 7", f"{avg_hand:.1f} ورقة | حرق {burn7_pct:.1f}%", "3.0–5.2 | حرق<=4%", "2.0–6.0 | حرق<=10%", st, diag, tg, pts))

        # 12. Infinite Shuffle-Bag Deck Cycles
        st, tg, pts = self._grade_range(avg_cycles, 0.35, 0.85, 0.20, 1.15)
        diag = (
            f"دوران ممتاز للديك (تُرى {avg_cycles*100:.0f}% من بطاقات الديك بالمباراة؛ إتمام دورة كاملة: {full_cycles_pct:.0f}%)." if tg == "ideal"
            else ("يُرى جزء صغير فقط من الديك في كل مباراة." if avg_cycles < 0.35 else f"المباريات تدور أكثر من دورة كاملة ({avg_cycles:.2f} دورة) — تكرار عالٍ لنفس البطاقات.")
        )
        rows.append((12, "معدل دورات الديك (Infinite Shuffle-Bag)", f"{avg_cycles:.2f} دورة ({full_cycles_pct:.0f}%)", "0.35 – 0.85 دورة", "0.20 – 1.15 دورة", st, diag, tg, pts))

        # 13. Non-Monster Card Play Diversity %
        st, tg, pts = self._grade_range(non_monster_pct, 22.0, 45.0, 14.0, 55.0)
        diag = (
            f"تنوع ممتاز! السحر والعهود والقادة والبيئات تمثل {non_monster_pct:.1f}% من اللعب الفعلي." if tg == "ideal"
            else (f"هيمنة ساحقة للوحوش! البطاقات غير الوحوش لا تمثل إلا {non_monster_pct:.1f}% فقط من اللعب الفعلي (السحر والبيئات شبه غائبة!)." if non_monster_pct < 22.0 else "هيمنة مفرطة للتعاويذ على حساب الوحوش.")
        )
        rows.append((13, "تنوع الفئات: حصة غير الوحوش باللعب الفعلي", f"{non_monster_pct:.1f}%", "22% – 45%", "14% – 55%", st, diag, tg, pts))

        # 14. Avg Impact Value per Card Played (calibrated to 350-HP Deck Game economy)
        st, tg, pts = self._grade_range(avg_val_card, 32.0, 64.0, 20.0, 78.0)
        diag = (
            f"كل بطاقة تُلعب تضيف قيمة ملموسة وممتعة ({avg_val_card:.1f} نقطة قيمة مركّبة/بطاقة)." if tg == "ideal"
            else ("قيمة البطاقة الواحدة ضعيفة نسبياً (الورق لا يصنع أثراً كافياً)." if avg_val_card < 32.0 else f"قيمة البطاقة الواحدة مرتفعة ({avg_val_card:.1f} نقطة — غالباً بسبب ارتفاع ضرر المهارات الفعّالة!).")
        )
        rows.append((14, "متوسط القيمة المحققة لكل بطاقة ملعوبة", f"{avg_val_card:.1f} نقطة/كارت", "32 – 64 نقطة", "20 – 78 نقطة", st, diag, tg, pts))

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
            "drawn_tot": drawn_tot,
            "played_tot": played_tot,
            "planned_plays": max(0, played_tot - int(cu.get("played_on_draw_turn", 0))),
            "topdeck_plays": int(cu.get("played_on_draw_turn", 0)),
            "sac_tot": sac_tot,
            "rem_end": rem_end,
            "burn7_tot": burn7_tot,
            "played_shares": [100.0 * int(x) / pbt_sum for x in pbt],
            "drafted_shares": self._compute_settled_deck_type_shares(),
        }

        for num, m_name, act_s, ideal_s, good_s, st_lbl, diag_s, tag, _ in rows:
            self.cu_tree.insert(
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
            cv.create_text(w // 2, h // 2, text=rtl("بانتظار بيانات التدريب الجديدة لاستخدام البطاقات — ابدأ التدريب أو اتصل بالجهاز الثاني!"), fill=COL_GOLD, font=(FONT_UI, 11, "bold"))
            return

        sc = int(d["score"])
        if sc >= 85:
            col, badge = COL_GREEN, "🟢 استغلال بطاقات مثالي ومتنوع (AAA)"
        elif sc >= 70:
            col, badge = COL_GOLD, "🟡 استغلال جيد للبطاقات (راجع المؤشرات الصفراء/الحمراء لرفع تنوع الفئات)"
        else:
            col, badge = COL_RED, "🔴 خلل في استخدام البطاقات (هيمنة فئة واحدة أو بطاقات ميتة — راجع الأحمر!)"

        cv.create_text(w - 12, 18, text=rtl(f"التقييم العام لمعيار استخدام البطاقات:  {sc} / 100%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 14, "bold"))
        cv.create_text(w - 12, 44, text=rtl(badge), anchor="e", fill=col, font=(FONT_UI, 10, "bold"))
        cv.create_text(
            w - 12, 66,
            text=rtl(f"المعيار: 🟢 >=85% مثالي | 🟡 70–84% جيد | 🔴 <70% خطر  •  (إجمالي المسحوب: {d['drawn_tot']:,} • الملعوب فعلياً: {d['played_tot']:,})"),
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
    # Chart 1: Played vs Drafted Shares across all 6 Card Types (RTL Paired Bars)
    # ------------------------------------------------------------------
    def _draw_type_comparison(self) -> None:
        cv = self.cv_types
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(200, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات فئات بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        labels = ["وحوش قتالية", "حواجز (0 هجوم)", "بطاقات السحر", "تمائم القادة", "العهود والقيود", "بطاقات البيئة"]
        p_sh = d["played_shares"]
        d_sh = d["drafted_shares"]

        # Legend at top-right
        cv.create_rectangle(w - 22, 8, w - 12, 16, fill=COL_CYAN, outline="")
        cv.create_text(w - 26, 12, text=rtl("نسبة اللعب الفعلي بالمباريات"), anchor="e", fill=COL_CYAN, font=(FONT_UI, 8, "bold"))
        cv.create_rectangle(w - 196, 8, w - 186, 16, fill=COL_GOLD, outline="")
        cv.create_text(w - 200, 12, text=rtl("نسبة الاختيار بأحدث الديكات (41)"), anchor="e", fill=COL_GOLD, font=(FONT_UI, 8, "bold"))

        lbl_x = w - 12
        bar_r = w - 118
        bar_l = 108
        bar_w = max(70, bar_r - bar_l)
        row_h = max(31, (h - 32) // 6)

        for i, lbl in enumerate(labels):
            y = 26 + i * row_h
            pv = p_sh[i]
            dv = d_sh[i]
            cv.create_text(lbl_x, y + 10, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))

            # Played bar (top, cyan)
            cv.create_rectangle(bar_l, y + 2, bar_r, y + 10, fill="#242C3D", outline="")
            pw = max(2, int(bar_w * min(100.0, pv) / 100.0)) if pv > 0.1 else 0
            if pw > 0:
                cv.create_rectangle(bar_r - pw, y + 2, bar_r, y + 10, fill=COL_CYAN, outline="")

            # Drafted bar (bottom, gold)
            cv.create_rectangle(bar_l, y + 12, bar_r, y + 20, fill="#242C3D", outline="")
            dw = max(2, int(bar_w * min(100.0, dv) / 100.0)) if dv > 0.1 else 0
            if dw > 0:
                cv.create_rectangle(bar_r - dw, y + 12, bar_r, y + 20, fill=COL_GOLD, outline="")

            cv.create_text(bar_l - 6, y + 11, text=f"{pv:4.1f}% | {dv:4.1f}%", anchor="e", fill=FG_TEXT, font=(FONT_UI, 7, "bold"))

    # ------------------------------------------------------------------
    # Chart 2: Drawn Card Fate Funnel (RTL Bars)
    # ------------------------------------------------------------------
    def _draw_card_fate(self) -> None:
        cv = self.cv_fate
        cv.delete("all")
        w = max(340, cv.winfo_width())
        h = max(200, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات مصير الأوراق بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        tot = max(1, d["planned_plays"] + d["topdeck_plays"] + d["rem_end"] + d["sac_tot"] + d["burn7_tot"])
        rows = [
            ("لُعبت بتخطيط (في دور لاحق)", d["planned_plays"], COL_BLUE),
            ("لُعبت فور سحبها (Top-Deck)", d["topdeck_plays"], COL_CYAN),
            ("بقيت باليد عند نهاية المباراة", d["rem_end"], COL_PURPLE),
            ("ضُحّي بها لزيادة المانا (+1)", d["sac_tot"], COL_GOLD),
            ("احترقت بسقف اليد 7 (Burned)", d["burn7_tot"], COL_RED),
        ]

        lbl_x = w - 12
        bar_r = w - 196
        bar_l = 88
        bar_w = max(60, bar_r - bar_l)
        row_h = max(36, (h - 24) // len(rows))

        for i, (lbl, cnt, col) in enumerate(rows):
            y = 14 + i * row_h
            pct = 100.0 * cnt / tot
            cv.create_text(lbl_x, y + 9, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(bar_l, y + 3, bar_r, y + 16, fill="#242C3D", outline="")
            fw = max(2, int(bar_w * min(100.0, pct) / 100.0)) if cnt > 0 else 0
            if fw > 0:
                cv.create_rectangle(bar_r - fw, y + 3, bar_r, y + 16, fill=col, outline="")
            cv.create_text(bar_l - 8, y + 9, text=f"{pct:4.1f}% ({cnt})", anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Shared 80-Match Non-Destructive Live Probe
    # ------------------------------------------------------------------
    def _start_live_probe(self) -> None:
        if self._probe_running:
            return
        self._probe_running = True
        self.probe_btn.config(state="disabled", text=rtl("جاري تشغيل 80 مباراة..."))
        self.probe_status_var.set(rtl("جاري قياس استخدام البطاقات عبر 80 مباراة حية..."))
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

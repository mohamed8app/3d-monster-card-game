# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Pillar #11: Competitive Fairness & Esports Readiness Tab (Arabic RTL).
Tracks all 14 competitive fairness indicators:
  - P1 vs P2 Turn-Order Fairness (General + Pure Identical Mirror-Match Audit!)
  - Opening Hand Brick Rate & WR Penalty + Early Special Card (Leader/Covenant) Draw Lottery Lift
  - Mirror-Match Pure-Luck Blowout Rate + Evasion RNG + Free-10 Draft Priority Bias + ELO Accuracy
  - Overall 0-100% Fairness Score (>=85% Ideal, >=70% Good, <70% Warning)
  - Visual Chart 1: Fairness Balance Scale around 50% (P1 General, Mirror P1, Good vs Brick Opener, Early Special)
  - Visual Chart 2: Free_01 -> Free_10 Draft Order WR Bars + Pure Mirror Match Outcome Breakdown
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


def run_mirror_audit(
    db: CardDB,
    agents: Dict[str, Any],
    decks: Dict[str, List[int]],
    seed: int = 20261201,
    n_matches: int = 40,
) -> Dict[str, int]:
    """
    Runs Pure Identical Mirror Matches (EXACT same 50-card deck + EXACT same AI brain on both sides!)
    to isolate pure First-Player Advantage (P1 4-card vs P2 5-card) and pure Draw-RNG Blowout Rate!
    """
    from engine.battle_engine import BattleEngine

    mids = list(agents.keys())
    rng = random.Random(seed)
    eng = BattleEngine(db)

    m_tot = 0
    p1_dec = 0
    p1_wins = 0
    blowouts = 0
    closes = 0

    for i in range(n_matches):
        mid = mids[i % len(mids)]
        eng.reset(decks[mid], decks[mid], seed=seed + 1000 + i)
        brain = agents[mid]
        steps = 0
        while not eng.done and steps < 800:
            s = eng.active
            act, _, _ = brain.select_action(eng.get_obs(s), eng.get_legal_mask(s), deterministic=False)
            eng.step(act)
            steps += 1
        res = eng.get_result()
        m_tot += 1
        if res.winner in (0, 1):
            p1_dec += 1
            p1_s = 0 if res.telemetry[0].went_first else 1
            if res.winner == p1_s:
                p1_wins += 1
            w_hp = res.final_hp[res.winner]
            if w_hp >= 325:
                blowouts += 1
            elif w_hp <= 120:
                closes += 1

    return {
        "mirror_matches": m_tot,
        "mirror_p1_decisive": p1_dec,
        "mirror_p1_wins": p1_wins,
        "mirror_blowouts": blowouts,
        "mirror_close": closes,
    }


class CompetitiveFairnessTab:
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

        has_main = int(self.store.excitement.get("comp_fairness", {}).get("matches", 0)) > 0
        has_probe = bool(self.probe_data and int(self.probe_data.get("comp_fairness", {}).get("matches", 0)) > 0)
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
            "بطاقة التقييم التفصيلية لمعيار العدالة التنافسية (14 مؤشراً — النتيجة + الجيد + المثالي + التشخيص التلقائي)",
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
        self.cf_tree = ttk.Treeview(tbl_panel, columns=tuple(c for c, _, _ in headers), show="headings", height=14)
        for c, txt, w in headers:
            self.cf_tree.heading(c, text=rtl(txt))
            self.cf_tree.column(c, width=w, anchor="e" if c in ("metric", "diag") else "center")

        self.cf_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.cf_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.cf_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.cf_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        self.cf_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.cf_tree)

        # Visual Charts Stacked Vertically at Full Screen Width
        p_scale = self._make_panel(self.inner, "ميزان العدالة التنافسية حول خط 50% (P1/P2 • Mirror • اليد • السحب)", COL_GOLD)
        p_scale.pack(fill="x", pady=(0, 8))
        self.cv_scale = tk.Canvas(p_scale, bg=BG_PANEL, height=245, highlightthickness=0)
        self.cv_scale.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_scale.bind("<Configure>", lambda _e: self._draw_fairness_scale())

        p_free = self._make_panel(self.inner, "عدالة ترتيب النماذج الحرة (حر 1 ← حر 10) + نتائج المواجهات المتطابقة", COL_CYAN)
        p_free.pack(fill="x", pady=(0, 8))
        self.cv_free = tk.Canvas(p_free, bg=BG_PANEL, height=245, highlightthickness=0)
        self.cv_free.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_free.bind("<Configure>", lambda _e: self._draw_free10_and_mirror())

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
        self._draw_fairness_scale()
        self._draw_free10_and_mirror()

    @staticmethod
    def _grade_range(val: float, ideal_lo: float, ideal_hi: float, good_lo: float, good_hi: float) -> Tuple[str, str, int]:
        if ideal_lo <= val <= ideal_hi:
            return ("🟢 مثالي", "ideal", 100)
        if good_lo <= val <= good_hi:
            return ("🟡 جيد", "good", 72)
        return ("🔴 يحتاج تعديل", "bad", 28)

    def _compute_elo_wr_corr(self) -> float:
        xs, ys = [], []
        for spec in ROSTER_40:
            r = self.store.roster.get(spec.model_id, {})
            m = int(r.get("matches", 0))
            if m >= 10:
                xs.append(float(r.get("elo", 1000.0)))
                ys.append(100.0 * int(r.get("wins", 0)) / m)
        n = len(xs)
        if n < 3:
            return 0.0
        mx, my = sum(xs) / n, sum(ys) / n
        num = sum((x - mx) * (y - my) for x, y in zip(xs, ys))
        dx = math.sqrt(sum((x - mx) ** 2 for x in xs))
        dy = math.sqrt(sum((y - my) ** 2 for y in ys))
        return (num / (dx * dy)) if (dx > 0 and dy > 0) else 0.0

    def _compute_free10_wrs(self) -> List[Tuple[str, float]]:
        out = []
        for i in range(1, 11):
            mid = f"free_{i:02d}"
            r = self.store.roster.get(mid, {})
            m = int(r.get("matches", 0))
            wr = (100.0 * int(r.get("wins", 0)) / m) if m > 0 else 50.0
            out.append((f"حر {i}", wr))
        return out

    def _compute_and_populate(self) -> None:
        self.cf_tree.delete(*self.cf_tree.get_children())
        ex = self._active_ex()
        cf = ex.get("comp_fairness", {})
        matches = int(cf.get("matches", 0))

        if matches <= 0:
            self._last_metrics = {"has_data": False, "score": 0, "ideal_cnt": 0, "good_cnt": 0, "bad_cnt": 0}
            self.cf_tree.insert(
                "", "end",
                values=(
                    rtl("بانتظار بيانات التدريب — تظهر الأرقام تلقائياً فور بدء التدريب على المحاكي المحدّث أو الاتصال بالجهاز الثاني."),
                    rtl("بانتظار بيانات"), "--", "--", "--", rtl("تتبع العدالة التنافسية وبطولة Mirror جاهز ومفعّل"), "!",
                ),
                tags=("nodata",),
            )
            return

        p1_dec = max(1, int(cf.get("p1_decisive", 0)))
        p1_wr = 100.0 * int(cf.get("p1_wins", 0)) / p1_dec
        draw_pct = 100.0 * int(cf.get("draws", 0)) / matches

        t4_cnt = int(cf.get("t4_diff_cnt", 0))
        t4_diff = (int(cf.get("t4_diff_sum", 0)) / float(t4_cnt)) if t4_cnt > 0 else 0.0

        ok_s = int(cf.get("open_ok_sides", 0))
        br_s = int(cf.get("open_brick_sides", 0))
        tot_s = max(1, ok_s + br_s)
        brick_rate = 100.0 * br_s / tot_s
        ok_wr = (100.0 * int(cf.get("open_ok_wins", 0)) / ok_s) if ok_s > 0 else 50.0
        br_wr = (100.0 * int(cf.get("open_brick_wins", 0)) / br_s) if br_s > 0 else 50.0
        brick_pen = (ok_wr - br_wr) if (ok_s >= 5 and br_s >= 3) else 0.0

        es_s = int(cf.get("early_spec_sides", 0))
        ns_s = int(cf.get("no_spec_sides", 0))
        es_wr = (100.0 * int(cf.get("early_spec_wins", 0)) / es_s) if es_s > 0 else 50.0
        ns_wr = (100.0 * int(cf.get("no_spec_wins", 0)) / ns_s) if ns_s > 0 else 50.0
        spec_lift = (es_wr - ns_wr) if (es_s >= 5 and ns_s >= 5) else 0.0

        ev_pm = int(cf.get("evasions_total", 0)) / float(matches)
        mana_gap = float(cf.get("mana_gap_sum", 0.0)) / max(1, int(cf.get("mana_gap_cnt", 0)))
        draw_gap = int(cf.get("draw_gap_sum", 0)) / float(max(1, int(cf.get("draw_gap_cnt", 0))))

        # Mirror Audit values directly from training logs
        mir_m = int(cf.get("mirror_matches", 0))
        mir_src = cf
        mir_dec = max(1, int(mir_src.get("mirror_p1_decisive", 0)))
        mir_p1_wr = (100.0 * int(mir_src.get("mirror_p1_wins", 0)) / mir_dec) if mir_m > 0 else -1.0
        mir_blow = (100.0 * int(mir_src.get("mirror_blowouts", 0)) / mir_dec) if mir_m > 0 else -1.0
        mir_close = (100.0 * int(mir_src.get("mirror_close", 0)) / mir_dec) if mir_m > 0 else 0.0

        # Roster-level Free-10 Draft Priority Bias & ELO Correlation
        f10 = self._compute_free10_wrs()
        early5 = sum(x[1] for x in f10[:5]) / 5.0
        late5 = sum(x[1] for x in f10[5:]) / 5.0
        draft_bias = abs(early5 - late5)
        elo_corr = self._compute_elo_wr_corr()

        # Composite Esports Fairness Index (0..100%)
        p1_dev = abs(p1_wr - 50.0)
        mir_dev = abs(mir_p1_wr - 50.0) if mir_p1_wr >= 0 else p1_dev
        esports_idx = max(0.0, min(100.0, 100.0 - (p1_dev * 2.5 + mir_dev * 2.0 + max(0.0, brick_pen) * 0.6 + abs(spec_lift) * 0.5)))

        rows: List[Tuple[int, str, str, str, str, str, str, str, int]] = []

        # 1. General P1 Win Rate %
        st, tg, pts = self._grade_range(p1_wr, 47.0, 53.0, 44.0, 56.0)
        diag = (
            f"عدالة ممتازة بين الأول والثاني! (P1 = {p1_wr:.1f}% مقابل P2 = {100-p1_wr:.1f}%)." if tg == "ideal"
            else f"تحيّز واضح لـ {'اللاعب الأول P1' if p1_wr > 50 else 'اللاعب الثاني P2'} ({p1_wr:.1f}% / {100-p1_wr:.1f}%)!"
        )
        rows.append((1, "نسبة فوز اللاعب الأول العام (P1 vs P2)", f"{p1_wr:.1f}%", "47% – 53%", "44% – 56%", st, diag, tg, pts))

        # 2. Pure Identical Mirror P1 Win Rate %
        if mir_p1_wr >= 0:
            st, tg, pts = self._grade_range(mir_p1_wr, 45.0, 55.0, 40.0, 60.0)
            diag = (
                f"برهان عدالة خالص! بنفس الديك تماماً، فوز P1 = {mir_p1_wr:.1f}% ({mir_m} مباراة متطابقة)." if tg == "ideal"
                else f"تحيّز قانون الدور الأول في المواجهات المتطابقة! (P1 = {mir_p1_wr:.1f}% بنفس الديك ونفس الذكاء!)."
            )
            act_2 = f"{mir_p1_wr:.1f}% (n={mir_m})"
        else:
            st, tg, pts, act_2 = "🟡 بانتظار تدريب", "good", 72, "بانتظار تدريب"
            diag = "تُسجّل تلقائياً أثناء مواجهات التدريب المتطابقة."
        rows.append((2, "عدالة P1 في المواجهات المتطابقة (Pure Mirror)", act_2, "45% – 55%", "40% – 60%", st, diag, tg, pts))

        # 3. Turn-4 Early Tempo Lead (P1 - P2 HP)
        st, tg, pts = self._grade_range(t4_diff, -35.0, 35.0, -65.0, 65.0)
        diag = (
            f"تعادل ممتاز في البداية! فارق الدم بالجولة 4 هو {t4_diff:+.1f} HP لصالح P1." if tg == "ideal"
            else f"تفوّق مبكر مفرط عند الجولة 4 ({t4_diff:+.1f} HP لصالح P1) — الورقة الخامسة لـ P2 لا تكفي وحدها!"
        )
        rows.append((3, "فارق دم القائدين عند الجولة 4 (P1 − P2)", f"{t4_diff:+.1f} HP", "-35 إلى +35", "-65 إلى +65", st, diag, tg, pts))

        # 4. Opening Hand Brick Rate % (0 cards <= 2 mana)
        st, tg, pts = self._grade_range(brick_rate, 0.0, 12.0, 0.0, 22.0)
        diag = (
            f"سحب افتتاحي سلس جداً (تعثر اليد الافتتاحية فقط {brick_rate:.1f}%)." if tg == "ideal"
            else f"في {brick_rate:.1f}% من المباريات يبدأ اللاعب بلا أي بطاقة رخيصة (<=2 مانا) في يده!"
        )
        rows.append((4, "نسبة تعثر اليد الافتتاحية (0 كروت <=2 مانا)", f"{brick_rate:.1f}%", "12% فأقل", "12% – 22%", st, diag, tg, pts))

        # 5. Brick Opening WR Penalty
        st, tg, pts = self._grade_range(brick_pen, -5.0, 12.0, -10.0, 20.0)
        diag = (
            f"التضحية للمانا تخفف أثر تعثر السحب (فوز البداية السلسة {ok_wr:.0f}% مقابل المتعثرة {br_wr:.0f}% — فرق {brick_pen:+.1f}%)." if tg == "ideal"
            else f"عقوبة قاسية لتعثر البداية! ({ok_wr:.0f}% للسلسة مقابل {br_wr:.0f}% للمتعثرة — فرق {brick_pen:+.1f}%!)."
        )
        rows.append((5, "عقوبة تعثر اليد الافتتاحية على الفوز (WR Gap)", f"{brick_pen:+.1f}% ({ok_wr:.0f}%/{br_wr:.0f}%)", "12% فأقل", "12% – 20%", st, diag, tg, pts))

        # 6. Early Special Card (Leader/Covenant in T1-T3) Lottery Lift
        st, tg, pts = self._grade_range(spec_lift, -6.0, 8.0, -11.0, 15.0)
        diag = (
            f"لا يوجد يانصيب سحب مبكر! سحب القائد/العهد بأول 3 أدوار يغيّر الفوز بـ {spec_lift:+.1f}% فقط." if tg == "ideal"
            else f"حظ السحب المبكر للقائد/العهد مؤثر ({es_wr:.0f}% مقابل {ns_wr:.0f}% — فرق {spec_lift:+.1f}%)."
        )
        rows.append((6, "حظ سحب القائد/العهد بأول 3 أدوار (WR Lift)", f"{spec_lift:+.1f}% ({es_wr:.0f}%/{ns_wr:.0f}%)", "-6% إلى +8%", "-11% إلى +15%", st, diag, tg, pts))

        # 7. Pure Mirror-Match Blowout Rate % (Pure Draw-RNG Luck!)
        if mir_blow >= 0:
            st, tg, pts = self._grade_range(mir_blow, 0.0, 28.0, 0.0, 44.0)
            diag = (
                f"توازن ممتاز ضد حظ السحب! الهزائم الساحقة في الديكات المتطابقة فقط {mir_blow:.1f}%." if tg == "ideal"
                else f"تحذير حظ وكرة ثلج! حتى بنفس الديك تماماً، {mir_blow:.1f}% من المباريات تنتهي بفوز ساحق (>=325 HP)!"
            )
            act_7 = f"{mir_blow:.1f}%"
        else:
            st, tg, pts, act_7 = "🟡 بانتظار تدريب", "good", 72, "بانتظار تدريب"
            diag = "تُسجّل تلقائياً عند توفر مواجهات متطابقة بالتدريب."
        rows.append((7, "هزائم حظ السحب الصافي في المواجهات المتطابقة (>=325 HP)", act_7, "28% فأقل", "28% – 44%", st, diag, tg, pts))

        # 8. Evasion RNG / Match
        st, tg, pts = self._grade_range(ev_pm, 0.0, 2.5, 0.0, 4.5)
        diag = (
            f"عشوائية قتالية منضبطة جداً ({ev_pm:.2f} تفادي/مباراة — لا تسرق المباريات بالحظ)." if tg == "ideal"
            else f"كثرة عشوائية التفادي ({ev_pm:.2f}/مباراة) قد تسبب إحباطاً تنافسياً!"
        )
        rows.append((8, "معدل عشوائية التفادي القتالي (Evasion RNG)", f"{ev_pm:.2f} / مباراة", "2.5 فأقل", "2.5 – 4.5", st, diag, tg, pts))

        # 9. Free-10 Draft Order Priority Bias (Free 1..5 vs 6..10)
        st, tg, pts = self._grade_range(draft_bias, 0.0, 5.0, 0.0, 9.0)
        diag = (
            f"ممتاز! ترتيب اختيار الميتا الحرة عادل (الأوائل 1–5 = {early5:.1f}% مقابل الأواخر 6–10 = {late5:.1f}%)." if tg == "ideal"
            else f"انحياز في ترتيب الاختيار! الأوائل (1–5: {early5:.1f}%) مقابل الأواخر (6–10: {late5:.1f}%) — فرق {draft_bias:.1f}%."
        )
        rows.append((9, "تأثير ترتيب النماذج الحرة (حر 1–5 ضد حر 6–10)", f"فرق {draft_bias:.1f}% ({early5:.0f}%/{late5:.0f}%)", "5% فأقل", "5% – 9%", st, diag, tg, pts))

        # 10. ELO <-> Win Rate Predictive Accuracy (Correlation r)
        st, tg, pts = self._grade_range(elo_corr, 0.65, 1.0, 0.40, 1.0)
        diag = (
            f"نظام تصنيف ELO دقيق وموثوق (ارتباط r = {elo_corr:.2f} مع نسب الفوز الفعلية عبر 41 نموذجاً)." if tg == "ideal"
            else f"ارتباط ELO بنسب الفوز متوسط (r = {elo_corr:.2f} — لأن بعض النماذج لعبت مباريات أقل أو ضد خصوم مختلفين)."
        )
        rows.append((10, "دقة ومصداقية تصنيف ELO (ارتباط r مع الفوز)", f"r = {elo_corr:.2f}", "0.65 فأعلى", "0.40 – 0.64", st, diag, tg, pts))

        # 11. Draw Rate %
        st, tg, pts = self._grade_range(draw_pct, 0.0, 3.0, 0.0, 7.0)
        diag = (
            f"مباريات حاسمة ونظيفة (نسبة التعادل {draw_pct:.1f}% فقط)." if tg == "ideal"
            else f"نسبة تعادل مرتفعة نسبياً ({draw_pct:.1f}%)."
        )
        rows.append((11, "نسبة التعادلات (Draw Rate %)", f"{draw_pct:.1f}%", "3% فأقل", "3% – 7%", st, diag, tg, pts))

        # 12. Winner vs Loser Mana Fairness Gap
        st, tg, pts = self._grade_range(abs(mana_gap), 0.0, 0.8, 0.0, 1.5)
        diag = (
            f"تكافؤ موارد عادل! فارق المانا المتاحة بين الفائز والخاسر {mana_gap:+.2f} مانا/دور فقط." if tg == "ideal"
            else f"فجوة مانا ملحوظة بين الفائز والخاسر ({mana_gap:+.2f} مانا/دور) — التسارع يخلق فجوة موارد!"
        )
        rows.append((12, "فجوة المانا المتاحة بين الفائز والخاسر/دور", f"{mana_gap:+.2f} مانا", "0.8 فأقل", "0.8 – 1.5", st, diag, tg, pts))

        # 13. Winner vs Loser Card Draw Fairness Gap
        st, tg, pts = self._grade_range(abs(draw_gap), 0.0, 2.5, 0.0, 4.5)
        diag = (
            f"تكافؤ سحب عادل ({draw_gap:+.1f} بطاقة فرق فقط بين الفائز والخاسر)." if tg == "ideal"
            else f"فارق سحب ملحوظ ({draw_gap:+.1f} بطاقة لصالح الفائز)."
        )
        rows.append((13, "فجوة عدد البطاقات المسحوبة (فائز − خاسر)", f"{draw_gap:+.1f} بطاقة", "2.5 فأقل", "2.5 – 4.5", st, diag, tg, pts))

        # 14. Composite Esports Fairness Index (0-100%)
        st, tg, pts = self._grade_range(esports_idx, 78.0, 100.0, 62.0, 100.0)
        diag = (
            f"لعبة جاهزة للتصنيف التنافسي! مؤشر العدالة المركّب {esports_idx:.1f} / 100%." if tg == "ideal"
            else f"مؤشر العدالة المركّب {esports_idx:.1f}% (يحتاج تقليص انحياز P1/Mirror أو عقوبة تعثر البداية)."
        )
        rows.append((14, "مؤشر الجاهزية التنافسية الشامل (Esports Index)", f"{esports_idx:.1f}%", "78% فأعلى", "62% – 77%", st, diag, tg, pts))

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
            "scale_items": [
                ("فوز اللاعب الأول (P1 العام)", p1_wr),
                ("فوز P1 بالمواجهات المتطابقة (Mirror)", mir_p1_wr if mir_p1_wr >= 0 else 50.0),
                ("بداية سلسة (<=2 مانا باليد)", ok_wr),
                ("بداية متعثرة (0 كروت <=2 مانا)", br_wr),
                ("سحب قائد/عهد بأول 3 أدوار", es_wr),
                ("بدون سحب قائد/عهد مبكر", ns_wr),
            ],
            "f10": f10,
            "mirror_stats": (mir_m, mir_close, max(0.0, 100.0 - mir_close - max(0.0, mir_blow)), max(0.0, mir_blow)),
        }

        for num, m_name, act_s, ideal_s, good_s, st_lbl, diag_s, tag, _ in rows:
            self.cf_tree.insert(
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
            cv.create_text(w // 2, h // 2, text=rtl("بانتظار بيانات التدريب الجديدة للعدالة التنافسية — ابدأ التدريب أو اتصل بالجهاز الثاني!"), fill=COL_GOLD, font=(FONT_UI, 11, "bold"))
            return

        sc = int(d["score"])
        if sc >= 85:
            col, badge = COL_GREEN, "🟢 عدالة تنافسية ممتازة وجاهزية عالية لنظام التصنيف والبطولات (AAA)"
        elif sc >= 70:
            col, badge = COL_GOLD, "🟡 عدالة جيدة (راجع المؤشرات الصفراء/الحمراء لضبط P1 وحظ السحب)"
        else:
            col, badge = COL_RED, "🔴 خلل في العدالة التنافسية (انحياز P1 أو حظ سحب مرتفع — راجع الأحمر!)"

        cv.create_text(w - 12, 18, text=rtl(f"التقييم العام لمعيار العدالة التنافسية:  {sc} / 100%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 14, "bold"))
        cv.create_text(w - 12, 44, text=rtl(badge), anchor="e", fill=col, font=(FONT_UI, 10, "bold"))
        cv.create_text(
            w - 12, 66,
            text=rtl(f"المعيار: 🟢 >=85% مثالي | 🟡 70–84% جيد | 🔴 <70% خطر  •  (المباريات: {d['matches']:,} • Mirror: {d['mirror_stats'][0]})"),
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
    # Chart 1: Fairness Balance Scale around 50% Centerline
    # ------------------------------------------------------------------
    def _draw_fairness_scale(self) -> None:
        cv = self.cv_scale
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(205, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات عدالة بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        items = d["scale_items"]
        lbl_x = w - 10
        bar_r = w - 196
        bar_l = 58
        bar_w = max(70, bar_r - bar_l)
        x50 = bar_l + bar_w * 0.50
        x45 = bar_l + bar_w * (45.0 - 20.0) / 60.0
        x55 = bar_l + bar_w * (55.0 - 20.0) / 60.0

        cv.create_rectangle(x45, 16, x55, h - 8, fill="#132620", outline="")
        cv.create_line(x50, 14, x50, h - 8, fill=COL_GOLD, dash=(3, 3))
        cv.create_text(x50, 7, text="50%", fill=COL_GOLD, font=(FONT_UI, 7, "bold"))
        cv.create_text(bar_l, 7, text="20%", fill=FG_MUTED, font=(FONT_UI, 7))
        cv.create_text(bar_r, 7, text="80%", fill=FG_MUTED, font=(FONT_UI, 7))

        row_h = max(32, (h - 24) // len(items))
        for i, (lbl, wr) in enumerate(items):
            y = 20 + i * row_h
            col = COL_GREEN if 45.0 <= wr <= 55.0 else (COL_GOLD if 40.0 <= wr <= 60.0 else COL_RED)
            cv.create_text(lbl_x, y + 10, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(bar_l, y + 3, bar_r, y + 17, fill="#1F2736", outline="")
            px = bar_l + bar_w * (max(20.0, min(80.0, wr)) - 20.0) / 60.0
            bx0, bx1 = min(x50, px), max(x50, px)
            if bx1 - bx0 < 3:
                bx0, bx1 = x50 - 2, x50 + 2
            cv.create_rectangle(bx0, y + 3, bx1, y + 17, fill=col, outline="")
            cv.create_text(bar_l - 6, y + 10, text=f"{wr:4.1f}%", anchor="e", fill=col, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Chart 2: Free_01..10 Draft Order Bars + Pure Mirror Outcome Bar
    # ------------------------------------------------------------------
    def _draw_free10_and_mirror(self) -> None:
        cv = self.cv_free
        cv.delete("all")
        w = max(340, cv.winfo_width())
        h = max(205, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        # Top 60%: Free_01 -> Free_10 WR bars around 50%
        cv.create_text(w - 10, 11, text=rtl("نسبة فوز النماذج الحرة (حر 1 ← حر 10 — كاشف انحياز ترتيب الاختيار):"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
        f10 = d["f10"]
        top_y, bot_y = 26, int(h * 0.58)
        plot_h = max(60, bot_y - top_y)
        lx, rx = 30, w - 12
        step = (rx - lx) / 10.0
        bw = max(12, int(step * 0.62))

        y50 = bot_y - int(plot_h * (50.0 - 25.0) / 50.0)
        cv.create_line(lx, y50, rx, y50, fill=COL_GOLD, dash=(3, 3))
        cv.create_text(lx - 4, y50, text="50%", anchor="e", fill=COL_GOLD, font=(FONT_UI, 7))

        for i, (tag, wr) in enumerate(f10):
            cx = rx - (i + 0.5) * step
            clamped = max(25.0, min(75.0, wr))
            bh = int(plot_h * (clamped - 25.0) / 50.0)
            col = COL_GREEN if 45.0 <= wr <= 55.0 else (COL_GOLD if 40.0 <= wr <= 60.0 else COL_RED)
            cv.create_rectangle(cx - bw / 2, bot_y - bh, cx + bw / 2, bot_y, fill=col, outline="")
            cv.create_text(cx, bot_y - bh - 7, text=f"{wr:.0f}", fill=FG_TEXT, font=(FONT_UI, 7))
            cv.create_text(cx, bot_y + 9, text=tag, fill=FG_MUTED, font=(FONT_UI, 7))

        # Bottom 40%: Pure Mirror-Match Outcome Distribution (Close vs Normal vs Blowout)
        my0 = bot_y + 26
        cv.create_line(10, my0 - 4, w - 10, my0 - 4, fill=BG_BORDER)
        mir_m, m_clo, m_mid, m_blo = d["mirror_stats"]
        cv.create_text(
            w - 10, my0 + 8,
            text=rtl(f"نتائج المواجهات المتطابقة (Pure Mirror — {mir_m} مباراة نفس الديك):"),
            anchor="e", fill=COL_CYAN, font=(FONT_UI, 8, "bold"),
        )
        if mir_m <= 0:
            cv.create_text(w // 2, my0 + 36, text=rtl("بانتظار تسجيل مواجهات متطابقة في بيانات التدريب."), fill=FG_MUTED, font=(FONT_UI, 8))
            return

        bl, br = 14, w - 14
        bw_m = max(140, br - bl)
        segs = [
            ("متقاربة <=120 HP", m_clo, COL_GREEN),
            ("متوازنة", m_mid, COL_BLUE),
            ("ساحقة (حظ سحب)", m_blo, COL_RED),
        ]
        cur_r = br
        for idx_s, (lbl, pct, col) in enumerate(segs):
            sw = int(round(bw_m * pct / 100.0)) if idx_s < 2 else max(0, cur_r - bl)
            if sw > 0:
                cv.create_rectangle(cur_r - sw, my0 + 20, cur_r, my0 + 38, fill=col, outline="")
                if sw >= 34:
                    cv.create_text(cur_r - sw / 2, my0 + 29, text=f"{pct:.0f}%", fill="#FFFFFF", font=(FONT_UI, 8, "bold"))
                cur_r -= sw
        lx2 = br
        for lbl, pct, col in segs:
            cv.create_rectangle(lx2 - 8, my0 + 46, lx2, my0 + 54, fill=col, outline="")
            cv.create_text(lx2 - 11, my0 + 50, text=rtl(f"{lbl} {pct:.0f}%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 7))
            lx2 -= max(90, int(bw_m / 3.0))

    # ------------------------------------------------------------------
    # Shared Live Probe (80 Matches) + Pure Mirror Audit (40 Matches)
    # ------------------------------------------------------------------
    def _start_live_probe(self) -> None:
        if self._probe_running:
            return
        self._probe_running = True
        self.probe_btn.config(state="disabled", text=rtl("جاري الفحص + بطولة Mirror..."))
        self.probe_status_var.set(rtl("جاري تشغيل 80 مباراة قياس + 40 مباراة Mirror متطابقة..."))
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
                    self.parent.after(0, lambda k=i + 1: self.probe_status_var.set(rtl(f"المباريات الحية: {k}/{n_matches}...")))

            self.parent.after(0, lambda: self.probe_status_var.set(rtl("جاري تشغيل 40 مباراة Mirror متطابقة...")))
            mir = run_mirror_audit(fresh_db, agents, decks, seed=rng.randrange(1, 100_000), n_matches=40)
            ex["comp_fairness"].update(mir)

            # Preserve existing skill audit if present
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
                self.probe_btn.config(state="normal", text=rtl("فحص مباشر + بطولة Mirror متطابقة (80 + 40 مباراة)"))
                self.source_var.set(rtl("عيّنة الفحص المباشر (قوانين V2.2 الحالية)"))
                if self.on_probe_complete:
                    self.on_probe_complete()
                else:
                    self.refresh()

            self.parent.after(0, _done_ok)
        except Exception as err:
            def _done_err(msg: str = str(err)) -> None:
                self._probe_running = False
                self.probe_btn.config(state="normal", text=rtl("فحص مباشر + بطولة Mirror متطابقة (80 + 40 مباراة)"))
                self.probe_status_var.set(rtl(f"خطأ بالفحص: {msg[:50]}"))
            self.parent.after(0, _done_err)

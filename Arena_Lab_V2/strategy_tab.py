# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Pillar #9: Strategic Intelligence & Decision Depth Tab (Arabic RTL).
Tracks all 14 strategic-depth & skill-expression indicators:
  - 10 live per-match decision metrics (Legal Actions, Distinct Choices, Forced Pass, Multi-Action Chains,
    6-Lane Placement, Sacrifice Balance, Reactive Share, Tactical Restraint, Planned Hold Plays)
  - 4 deep skill-expression benchmarks (Policy Entropy, Trained AI vs Random WR, Optimal vs Blunder WR,
    and 1-Step Counterfactual Value Regret!)
  - Overall 0-100% Strategic Intelligence Score (>=85% Ideal, >=70% Good, <70% Warning)
  - Visual Chart 1: Distinct Choices Depth Distribution + 6-Lane Placement Heatmap (Slot 0..5)
  - Visual Chart 2: Skill Expression Ladder (AI vs Random, Optimal vs Blunder, Decision Regret, Planning, Restraint)
"""

from __future__ import annotations

import copy
import json
import math
import os
import random
import threading
import time
import tkinter as tk
from tkinter import ttk
from typing import Any, Callable, Dict, List, Optional, Tuple

import numpy as np

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


def run_skill_audit(
    db: CardDB,
    agents: Dict[str, Any],
    decks: Dict[str, List[int]],
    seed: int = 20261129,
    n_rand: int = 30,
    n_noisy: int = 30,
) -> Dict[str, Any]:
    """
    Runs fast, scientifically rigorous Skill-Expression Audit on V2.2:
      1. Trained AI (deterministic) vs Random Legal Player (n_rand matches) -> Skill Ceiling WR%
      2. Trained AI (deterministic) vs 35%-Blunder AI (n_noisy matches) -> Mistake Punishment WR%
      3. Policy Normalized Shannon Entropy over multi-choice decisions (n_legal >= 3)
      4. 1-Step Counterfactual Value-Head Regret (delta win-prob between Best Action vs Random Other Action)
    """
    import torch
    from engine.battle_engine import BattleEngine

    mids = list(agents.keys())
    rng = random.Random(seed)
    eng = BattleEngine(db)

    # 1. Trained AI vs Random Legal Player
    rand_wins = 0
    for i in range(n_rand):
        m0, m1 = rng.sample(mids, 2)
        eng.reset(decks[m0], decks[m1], seed=seed + 100 + i)
        ai_side = i % 2
        ai_brain = agents[m0 if ai_side == 0 else m1]
        steps = 0
        while not eng.done and steps < 800:
            s = eng.active
            mask = eng.get_legal_mask(s)
            if s == ai_side:
                act, _, _ = ai_brain.select_action(eng.get_obs(s), mask, deterministic=True)
            else:
                legal_ids = np.flatnonzero(mask)
                act = int(rng.choice(legal_ids))
            eng.step(act)
            steps += 1
        if eng.winner == ai_side:
            rand_wins += 1

    # 2. Optimal (deterministic) vs Blunder AI (35% random legal move) + Entropy & Counterfactual Value Regret
    noisy_wins = 0
    ent_samples: List[float] = []
    regret_samples: List[float] = []

    for i in range(n_noisy):
        m0, m1 = rng.sample(mids, 2)
        eng.reset(decks[m0], decks[m1], seed=seed + 500 + i)
        opt_side = i % 2
        pair = [agents[m0], agents[m1]]
        cf_done_match = 0
        steps = 0
        while not eng.done and steps < 800:
            s = eng.active
            mask = eng.get_legal_mask(s)
            obs = eng.get_obs(s)
            legal_ids = np.flatnonzero(mask)
            n_leg = len(legal_ids)

            if s == opt_side:
                # Sample Policy Entropy & 1-Step Value Regret cleanly with torch.no_grad()
                if n_leg >= 3 and (steps % 4 == 0):
                    with torch.no_grad():
                        obs_t = torch.from_numpy(obs).unsqueeze(0)
                        logits, _ = pair[s].fighter(obs_t)
                        l_sub = logits[0, legal_ids]
                        probs = torch.softmax(l_sub, dim=0).cpu().numpy()
                        ent = float(-np.sum(probs * np.log(np.clip(probs, 1e-9, 1.0))))
                        ent_samples.append(100.0 * ent / math.log(n_leg))

                        best_act = int(legal_ids[int(np.argmax(probs))])
                        # 2 counterfactual regret checks per match (mid-game turns >= 3)
                        if cf_done_match < 2 and eng.turn >= 3 and n_leg >= 4:
                            others = [int(a) for a in legal_ids if int(a) != best_act]
                            alt_act = rng.choice(others)
                            e_best = copy.deepcopy(eng)
                            e_best.step(best_act)
                            e_alt = copy.deepcopy(eng)
                            e_alt.step(alt_act)
                            _, vb = pair[s].fighter(torch.from_numpy(e_best.get_obs(s)).unsqueeze(0))
                            _, va = pair[s].fighter(torch.from_numpy(e_alt.get_obs(s)).unsqueeze(0))
                            # Map value [-1, +1] to win-prob [0%, 100%] via tanh scaling
                            pb = 50.0 * (math.tanh(float(vb.item())) + 1.0)
                            pa = 50.0 * (math.tanh(float(va.item())) + 1.0)
                            regret_samples.append(abs(pb - pa))
                            cf_done_match += 1
                    act = best_act
                else:
                    act, _, _ = pair[s].select_action(obs, mask, deterministic=True)
            else:
                if n_leg > 1 and rng.random() < 0.35:
                    act = int(rng.choice(legal_ids))
                else:
                    act, _, _ = pair[s].select_action(obs, mask, deterministic=False)

            eng.step(act)
            steps += 1
        if eng.winner == opt_side:
            noisy_wins += 1

    return {
        "policy_entropy_pct": round(sum(ent_samples) / len(ent_samples), 2) if ent_samples else 35.0,
        "ai_vs_random_wr": round(100.0 * rand_wins / max(1, n_rand), 1),
        "ai_vs_random_n": int(n_rand),
        "opt_vs_noisy_wr": round(100.0 * noisy_wins / max(1, n_noisy), 1),
        "opt_vs_noisy_n": int(n_noisy),
        "decision_regret_pct": round(sum(regret_samples) / len(regret_samples), 2) if regret_samples else 0.0,
    }


class StrategicIntelligenceTab:
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

        has_main = int(self.store.excitement.get("strategic_intel", {}).get("turns_total", 0)) > 0
        has_probe = bool(self.probe_data and int(self.probe_data.get("strategic_intel", {}).get("turns_total", 0)) > 0)
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

        # Visual Charts Row (RTL: Choices + 6-Lane Heatmap on RIGHT 52%, Skill Expression Ladder on LEFT 48%)
        charts = tk.Frame(self.inner, bg=BG_DARK)
        charts.pack(fill="x", pady=(0, 8))
        charts.grid_columnconfigure(0, weight=48, uniform="si")
        charts.grid_columnconfigure(1, weight=52, uniform="si")

        p_skill = self._make_panel(charts, "سلّم سقف المهارة وعقاب الأخطاء (هل المهارة تغلب الحظ؟)", COL_CYAN)
        p_skill.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        self.cv_skill = tk.Canvas(p_skill, bg=BG_PANEL, height=245, highlightthickness=0)
        self.cv_skill.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_skill.bind("<Configure>", lambda _e: self._draw_skill_ladder())

        p_depth = self._make_panel(charts, "توزيع عمق الخيارات المميزة بالدور + خريطة استخدام الـ 6 خانات", COL_GOLD)
        p_depth.grid(row=0, column=1, sticky="nsew", padx=(5, 0))
        self.cv_depth = tk.Canvas(p_depth, bg=BG_PANEL, height=245, highlightthickness=0)
        self.cv_depth.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_depth.bind("<Configure>", lambda _e: self._draw_depth_and_lanes())

        # Self-Explanatory 14-Indicator Diagnostic Table
        tbl_panel = self._make_panel(
            self.inner,
            "بطاقة التقييم التفصيلية لمعيار الذكاء الاستراتيجي وعمق القرار (14 مؤشراً — النتيجة + الجيد + المثالي + التشخيص)",
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
        self.si_tree = ttk.Treeview(tbl_panel, columns=tuple(c for c, _, _ in headers), show="headings", height=14)
        for c, txt, w in headers:
            self.si_tree.heading(c, text=rtl(txt))
            self.si_tree.column(c, width=w, anchor="e" if c in ("metric", "diag") else "center")

        self.si_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.si_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.si_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.si_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        self.si_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.si_tree)

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
        self._draw_depth_and_lanes()
        self._draw_skill_ladder()

    @staticmethod
    def _grade_range(val: float, ideal_lo: float, ideal_hi: float, good_lo: float, good_hi: float) -> Tuple[str, str, int]:
        if ideal_lo <= val <= ideal_hi:
            return ("🟢 مثالي", "ideal", 100)
        if good_lo <= val <= good_hi:
            return ("🟡 جيد", "good", 72)
        return ("🔴 يحتاج تعديل", "bad", 28)

    def _compute_and_populate(self) -> None:
        self.si_tree.delete(*self.si_tree.get_children())
        ex = self._active_ex()
        si = ex.get("strategic_intel", {})
        turns = int(si.get("turns_total", 0))

        if turns <= 0:
            self._last_metrics = {"has_data": False, "score": 0, "ideal_cnt": 0, "good_cnt": 0, "bad_cnt": 0}
            self.si_tree.insert(
                "", "end",
                values=(
                    rtl("بانتظار بيانات التدريب — تظهر الأرقام تلقائياً فور بدء التدريب على المحاكي المحدّث أو الاتصال بالجهاز الثاني."),
                    rtl("بانتظار بيانات"), "--", "--", "--", rtl("تتبع الذكاء الاستراتيجي وسقف المهارة جاهز ومفعّل"), "!",
                ),
                tags=("nodata",),
            )
            return

        avg_legal = int(si.get("legal_actions_sum", 0)) / float(turns)
        avg_dist = int(si.get("distinct_choices_sum", 0)) / float(turns)
        forced_pct = 100.0 * int(si.get("forced_pass_turns", 0)) / turns
        multi_pct = 100.0 * int(si.get("multi_action_turns", 0)) / turns
        avg_chain = int(si.get("decision_chain_sum", 0)) / float(turns)

        slots = [int(x) for x in si.get("slot_summons", [0] * 6)]
        s_tot = max(1, sum(slots))
        center_pct = 100.0 * (slots[2] + slots[3]) / s_tot

        sac_avail = max(1, int(si.get("sac_avail_turns", 0)))
        sac_rate = 100.0 * int(si.get("sac_taken_total", 0)) / sac_avail

        tot_dec = max(1, int(si.get("total_decisions", 0)))
        react_pct = 100.0 * int(si.get("reactive_decisions", 0)) / tot_dec
        restraint_pct = 100.0 * int(si.get("restraint_passes", 0)) / turns
        planned_pct = 100.0 * int(si.get("planned_plays", 0)) / max(1, int(si.get("played_total", 0)))

        # Deep skill audit values directly from training logs
        pol_ent = float(si.get("policy_entropy_pct", -1.0))
        ai_rand_wr = float(si.get("ai_vs_random_wr", -1.0))
        opt_noisy_wr = float(si.get("opt_vs_noisy_wr", -1.0))
        regret_pct = float(si.get("decision_regret_pct", -1.0))

        rows: List[Tuple[int, str, str, str, str, str, str, str, int]] = []

        # 1. Avg Legal Actions / Turn
        st, tg, pts = self._grade_range(avg_legal, 6.0, 22.0, 3.5, 30.0)
        diag = (
            f"غنى تكتيكي ممتاز ({avg_legal:.1f} حركة قانونية متاحة بداية كل دور)." if tg == "ideal"
            else ("خيارات قانونية محدودة جداً." if avg_legal < 6.0 else "عدد ضخم جداً من الحركات المتاحة (احتمال تشتيت).")
        )
        rows.append((1, "متوسط الحركات القانونية المتاحة بالدور", f"{avg_legal:.1f} حركة", "6 – 22 حركة", "3.5 – 30", st, diag, tg, pts))

        # 2. Distinct Tactical Choices / Turn
        st, tg, pts = self._grade_range(avg_dist, 3.2, 7.5, 2.0, 10.0)
        diag = (
            f"عمق قرار مثالي! أمام اللاعب {avg_dist:.1f} قرارات مميزة مختلفة (أوراق/مهارات/تضحية) كل دور." if tg == "ideal"
            else ("خيارات مميزة قليلة بالدور (اللعب يميل للخطية)." if avg_dist < 3.2 else "وفرة خيارات مرتفعة جداً بالدور الواحد.")
        )
        rows.append((2, "متوسط القرارات التكتيكية المميزة/دور", f"{avg_dist:.1f} قرار", "3.2 – 7.5", "2.0 – 10.0", st, diag, tg, pts))

        # 3. Forced Pass Turns %
        st, tg, pts = self._grade_range(forced_pct, 0.0, 8.0, 0.0, 16.0)
        diag = (
            f"حرية قرار عالية! الأدوار الإجبارية (لا يوجد إلا إنهاء الدور) فقط {forced_pct:.1f}%." if tg == "ideal"
            else f"في {forced_pct:.1f}% من الأدوار يُجبر اللاعب على إنهاء الدور بلا أي خيار!"
        )
        rows.append((3, "نسبة الأدوار الإجبارية (Forced Pass)", f"{forced_pct:.1f}%", "8% فأقل", "8% – 16%", st, diag, tg, pts))

        # 4. Multi-Action Combo Turns %
        st, tg, pts = self._grade_range(multi_pct, 55.0, 85.0, 38.0, 94.0)
        diag = (
            f"سلاسل لعب ممتعة! في {multi_pct:.1f}% من الأدوار ينفّذ اللاعب قرارين متتاليين أو أكثر." if tg == "ideal"
            else ("أغلب الأدوار قرار واحد فقط (قلة كومبوهات)." if multi_pct < 55.0 else "كثافة سلاسل مرتفعة جداً بكل دور.")
        )
        rows.append((4, "نسبة أدوار السلاسل المتعددة (>=2 قرارات)", f"{multi_pct:.1f}%", "55% – 85%", "38% – 94%", st, diag, tg, pts))

        # 5. Avg Decision Chain Length / Turn
        st, tg, pts = self._grade_range(avg_chain, 2.0, 4.5, 1.3, 6.0)
        diag = (
            f"إيقاع تفكير سليم ({avg_chain:.2f} قرارات متسلسلة بالدور قبل الهجوم)." if tg == "ideal"
            else ("سلسلة قرارات قصيرة." if avg_chain < 2.0 else "سلاسل قرارات طويلة جداً بكل دور.")
        )
        rows.append((5, "متوسط عمق التسلسل التكتيكي بالدور", f"{avg_chain:.2f} قرار/دور", "2.0 – 4.5", "1.3 – 6.0", st, diag, tg, pts))

        # 6. Center Lanes (2-3) vs Flanks (0,1,4,5) Placement %
        st, tg, pts = self._grade_range(center_pct, 28.0, 52.0, 20.0, 62.0)
        diag = (
            f"توزيع تموضع صحي بين الوسط ({center_pct:.1f}%) والأطراف ({100-center_pct:.1f}%)." if tg == "ideal"
            else f"تحيّز واضح في اختيار الخانات (الوسط 2–3 = {center_pct:.1f}% مقابل الأطراف {100-center_pct:.1f}%)!"
        )
        rows.append((6, "توازن التموضع: الوسط (2–3) مقابل الأطراف", f"{center_pct:.1f}% وسط", "28% – 52%", "20% – 62%", st, diag, tg, pts))

        # 7. Sacrifice Choice Balance % (when available)
        st, tg, pts = self._grade_range(sac_rate, 25.0, 62.0, 15.0, 78.0)
        diag = (
            f"قرار تضحية حرّ ومدروس ({sac_rate:.1f}% تضحية مقابل {100-sac_rate:.1f}% احتفاظ بالورقة عند التوفر)." if tg == "ideal"
            else (f"تضحية شبه آلية ({sac_rate:.1f}% كلما توفرت — يفقد اللاعب متعة الاختيار!)." if sac_rate > 62.0 else "تضحية نادرة جداً حتى عند توفرها.")
        )
        rows.append((7, "توازن قرار التضحية للمانا عند توفرها", f"{sac_rate:.1f}%", "25% – 62%", "15% – 78%", st, diag, tg, pts))

        # 8. Reactive / Defensive Decision Share %
        st, tg, pts = self._grade_range(react_pct, 25.0, 48.0, 16.0, 58.0)
        diag = (
            f"توازن قرارات سليم ({react_pct:.1f}% دفاع/دعم/تعطيل مقابل {100-react_pct:.1f}% هجوم)." if tg == "ideal"
            else ("القرارات هجومية بالكامل تقريباً." if react_pct < 25.0 else "القرارات دفاعية/تعطيلية بنسبة مفرطة.")
        )
        rows.append((8, "حصة القرارات الدفاعية والتفاعلية من اللعب", f"{react_pct:.1f}%", "25% – 48%", "16% – 58%", st, diag, tg, pts))

        # 9. Tactical Restraint (Passing while holding legal plays)
        st, tg, pts = self._grade_range(restraint_pct, 8.0, 28.0, 3.0, 42.0)
        diag = (
            f"ضبط نفس تكتيكي ممتاز! يحتفظ اللاعب بأوراق/مهارات للوقت الأنسب في {restraint_pct:.1f}% من الأدوار." if tg == "ideal"
            else (f"لعب آلي متسرع! يفرغ اللاعب كل خياراته بلا تأنٍّ (ضبط النفس {restraint_pct:.1f}% فقط)." if restraint_pct < 8.0 else f"تحفظ مفرط! يمرر اللاعب {restraint_pct:.1f}% من الأدوار رغم وجود خيارات متاحة!")
        )
        rows.append((9, "نسبة ضبط النفس (الاحتفاظ بخيار رغم إمكانية لعبه)", f"{restraint_pct:.1f}%", "8% – 28%", "3% – 42%", st, diag, tg, pts))

        # 10. Planned Hold Plays % (opposite of Top-Deck)
        st, tg, pts = self._grade_range(planned_pct, 55.0, 82.0, 42.0, 90.0)
        diag = (
            f"تخطيط مسبق سليم ({planned_pct:.1f}% من الأوراق تُلعب بعد الاحتفاظ بها لدور لاحق)." if tg == "ideal"
            else f"اعتماد عالٍ على سحب اللحظة (التخطيط المسبق {planned_pct:.1f}% فقط)."
        )
        rows.append((10, "نسبة اللعب المخطط مسبقاً (عكس Top-Deck)", f"{planned_pct:.1f}%", "55% – 82%", "42% – 90%", st, diag, tg, pts))

        # 11. Policy Decision Entropy %
        if pol_ent >= 0:
            st, tg, pts = self._grade_range(pol_ent, 20.0, 55.0, 10.0, 72.0)
            diag = (
                f"ثقة استراتيجية متوازنة (إنتروبيا السياسة {pol_ent:.1f}% — قرارات واضحة دون عشوائية)." if tg == "ideal"
                else ("السياسة متصلّبة جداً (مسار واحد فقط)." if pol_ent < 20.0 else "تشتت عالٍ في تفضيلات الذكاء (يحتاج مزيداً من التدريب).")
            )
            act_11 = f"{pol_ent:.1f}%"
        else:
            st, tg, pts, act_11 = "🟡 بانتظار تدريب", "good", 72, "بانتظار تدريب"
            diag = "تُسجّل تلقائياً من تقييم الشبكة العصبية أثناء التدريب."
        rows.append((11, "إنتروبيا قرارات السياسة (Policy Entropy)", act_11, "20% – 55%", "10% – 72%", st, diag, tg, pts))

        # 12. Trained AI vs Random Legal Player WR % (Skill Ceiling!)
        if ai_rand_wr >= 0:
            st, tg, pts = self._grade_range(ai_rand_wr, 85.0, 100.0, 72.0, 100.0)
            diag = (
                f"سقف مهارة مذهل! الذكاء المدرّب يسحق اللاعب العشوائي بنسبة {ai_rand_wr:.1f}% — لعبتك لعبة مهارة حقيقية!" if tg == "ideal"
                else f"سقف المهارة يحتاج رفعاً (تفوق الذكاء على العشوائي {ai_rand_wr:.1f}% فقط — الحظ يؤثر بنسبة ملحوظة!)."
            )
            act_12 = f"{ai_rand_wr:.1f}%"
        else:
            st, tg, pts, act_12 = "🟡 بانتظار تدريب", "good", 72, "بانتظار تدريب"
            diag = "تُحسب تلقائياً أثناء جولات التدريب التقييمية ضد اللاعب العشوائي."
        rows.append((12, "سقف المهارة: الذكاء المدرّب ضد عشوائي (WR%)", act_12, "85% فأعلى", "72% – 84%", st, diag, tg, pts))

        # 13. Optimal vs 35%-Blunder AI WR % (Mistake Punishment!)
        if opt_noisy_wr >= 0:
            st, tg, pts = self._grade_range(opt_noisy_wr, 58.0, 92.0, 52.0, 98.0)
            diag = (
                f"الأخطاء تُعاقَب بوضوح! اللعب الأمثل يهزم اللعب المتذبذب بنسبة {opt_noisy_wr:.1f}%." if tg == "ideal"
                else f"عقاب الأخطاء ضعيف نسبياً ({opt_noisy_wr:.1f}% فقط فرق بين اللعب الأمثل والمتذبذب)."
            )
            act_13 = f"{opt_noisy_wr:.1f}%"
        else:
            st, tg, pts, act_13 = "🟡 بانتظار تدريب", "good", 72, "بانتظار تدريب"
            diag = "تُحسب تلقائياً أثناء جولات التقييم بين اللعب الدقيق والمتذبذب."
        rows.append((13, "عقاب الأخطاء: اللعب الأمثل ضد المتذبذب (WR%)", act_13, "58% – 92%", "52% – 98%", st, diag, tg, pts))

        # 14. 1-Step Counterfactual Decision Value Gap / Regret %
        if regret_pct >= 0:
            st, tg, pts = self._grade_range(regret_pct, 8.0, 35.0, 4.0, 48.0)
            diag = (
                f"كل قرار له وزن حقيقي! الخطأ الواحد يغيّر تقييم الفوز بـ {regret_pct:.1f}% بالمتوسط." if tg == "ideal"
                else ("فارق التقييم بين القرار الجيد والسيئ ضئيل جداً." if regret_pct < 8.0 else "قرار واحد خاطئ ينسف المباراة فوراً.")
            )
            act_14 = f"{regret_pct:.1f}%"
        else:
            st, tg, pts, act_14 = "🟡 بانتظار تدريب", "good", 72, "بانتظار تدريب"
            diag = "تُحسب تلقائياً أثناء تقييم ندم القرار التكتيكي بالتدريب."
        rows.append((14, "ثقل القرار: ندم الخطأ الواحد (Value Regret)", act_14, "8% – 35%", "4% – 48%", st, diag, tg, pts))

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
            "turns": turns,
            "choice_hist": [int(x) for x in si.get("choice_hist", [0] * 6)],
            "slots": slots,
            "skill_bars": [
                ("تفوّق الذكاء ضد عشوائي (سقف المهارة)", max(0.0, ai_rand_wr), COL_GREEN),
                ("تفوّق الأمثل ضد متذبذب (عقاب الخطأ)", max(0.0, opt_noisy_wr), COL_CYAN),
                ("اللعب المخطط مسبقاً (عكس Top-Deck)", planned_pct, COL_BLUE),
                ("توازن قرار التضحية للمانا", sac_rate, COL_GOLD),
                ("إنتروبيا قرارات السياسة", max(0.0, pol_ent), COL_PURPLE),
                ("ضبط النفس (الاحتفاظ بخيار للوقت الأنسب)", restraint_pct, COL_PINK),
                ("ثقل القرار الخاطئ (Value Regret)", max(0.0, regret_pct), COL_RED),
            ],
        }

        for num, m_name, act_s, ideal_s, good_s, st_lbl, diag_s, tag, _ in rows:
            self.si_tree.insert(
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
            cv.create_text(w // 2, h // 2, text=rtl("بانتظار بيانات التدريب الجديدة للذكاء الاستراتيجي — ابدأ التدريب أو اتصل بالجهاز الثاني!"), fill=COL_GOLD, font=(FONT_UI, 11, "bold"))
            return

        sc = int(d["score"])
        if sc >= 85:
            col, badge = COL_GREEN, "🟢 عمق استراتيجي رائع وسقف مهارة تنافسي حقيقي (AAA)"
        elif sc >= 70:
            col, badge = COL_GOLD, "🟡 عمق استراتيجي جيد (راجع المؤشرات الصفراء/الحمراء لتقليل اللعب الآلي)"
        else:
            col, badge = COL_RED, "🔴 ضعف في عمق القرار أو هيمنة الحظ (راجع المؤشرات الحمراء!)"

        cv.create_text(w - 12, 18, text=rtl(f"التقييم العام لمعيار الذكاء الاستراتيجي وعمق القرار:  {sc} / 100%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 14, "bold"))
        cv.create_text(w - 12, 44, text=rtl(badge), anchor="e", fill=col, font=(FONT_UI, 10, "bold"))
        cv.create_text(
            w - 12, 66,
            text=rtl(f"المعيار: 🟢 >=85% مثالي | 🟡 70–84% جيد | 🔴 <70% خطر  •  (إجمالي الأدوار المحللة: {d['turns']:,})"),
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
    # Chart 1: Choice Depth Histogram + 6-Lane Placement Heatmap
    # ------------------------------------------------------------------
    def _draw_depth_and_lanes(self) -> None:
        cv = self.cv_depth
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(210, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات خيارات بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        # Top 58%: Distinct Choices Histogram (6 buckets)
        cv.create_text(w - 10, 12, text=rtl("عدد القرارات المميزة بداية كل دور (% من الأدوار):"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
        ch = d["choice_hist"]
        tot_c = max(1, sum(ch))
        pcts = [100.0 * x / tot_c for x in ch]
        max_p = max(20.0, max(pcts))
        c_lbls = ["0 (إجباري)", "1 قرار", "2–3", "4–5", "6–7", "8+"]
        c_cols = [COL_RED, COL_GOLD, COL_CYAN, COL_GREEN, COL_BLUE, COL_PURPLE]

        top_y, bot_y = 26, int(h * 0.54)
        plot_h = max(55, bot_y - top_y)
        lx, rx = 16, w - 16
        step = (rx - lx) / 6.0
        bw = max(20, int(step * 0.62))
        for i in range(6):
            cx = rx - (i + 0.5) * step
            bh = int(plot_h * pcts[i] / max_p)
            cv.create_rectangle(cx - bw / 2, bot_y - bh, cx + bw / 2, bot_y, fill=c_cols[i], outline="")
            cv.create_text(cx, bot_y - bh - 7, text=f"{pcts[i]:.0f}%", fill=FG_TEXT, font=(FONT_UI, 7, "bold"))
            cv.create_text(cx, bot_y + 10, text=rtl(c_lbls[i]), fill=FG_MUTED, font=(FONT_UI, 7))

        # Bottom 42%: 6-Lane Battlefield Placement Heatmap (Slot 0..5)
        hy0 = bot_y + 26
        cv.create_line(10, hy0 - 4, w - 10, hy0 - 4, fill=BG_BORDER)
        cv.create_text(w - 10, hy0 + 8, text=rtl("خريطة تموضع استدعاء الوحوش في الـ 6 خانات (المثالي ~16.7% لكل خانة):"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
        slots = d["slots"]
        st_tot = max(1, sum(slots))
        s_pcts = [100.0 * x / st_tot for x in slots]
        box_y0, box_y1 = hy0 + 22, min(h - 14, hy0 + 68)
        for si_i in range(6):
            x0 = lx + si_i * step + 3
            x1 = lx + (si_i + 1) * step - 3
            sp = s_pcts[si_i]
            is_ctr = si_i in (2, 3)
            bg_c = "#14352B" if (11.0 <= sp <= 24.0) else ("#3B2618" if sp > 24.0 else "#252B3B")
            out_c = COL_GOLD if is_ctr else BG_BORDER
            cv.create_rectangle(x0, box_y0, x1, box_y1, fill=bg_c, outline=out_c, width=2 if is_ctr else 1)
            tag_l = f"وسط {si_i}" if is_ctr else f"طرف {si_i}"
            cv.create_text((x0 + x1) / 2, box_y0 + 13, text=rtl(tag_l), fill=COL_GOLD if is_ctr else FG_MUTED, font=(FONT_UI, 7, "bold"))
            cv.create_text((x0 + x1) / 2, box_y0 + 30, text=f"{sp:.1f}%", fill=FG_TEXT, font=(FONT_UI, 9, "bold"))

    # ------------------------------------------------------------------
    # Chart 2: Skill Expression Ladder
    # ------------------------------------------------------------------
    def _draw_skill_ladder(self) -> None:
        cv = self.cv_skill
        cv.delete("all")
        w = max(340, cv.winfo_width())
        h = max(210, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات مهارة بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        bars = d["skill_bars"]
        lbl_x = w - 10
        bar_r = w - 210
        bar_l = 52
        bar_w = max(60, bar_r - bar_l)
        row_h = max(29, (h - 16) // len(bars))

        for i, (lbl, val, col) in enumerate(bars):
            y = 10 + i * row_h
            cv.create_text(lbl_x, y + 9, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(bar_l, y + 3, bar_r, y + 15, fill="#242C3D", outline="")
            fw = max(2, int(bar_w * min(100.0, val) / 100.0)) if val > 0.1 else 0
            if fw > 0:
                cv.create_rectangle(bar_r - fw, y + 3, bar_r, y + 15, fill=col, outline="")
            cv.create_text(bar_l - 6, y + 9, text=f"{val:4.1f}%", anchor="e", fill=col, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Shared Live Probe (80 Matches) + Skill Expression Audit (60 Matches)
    # ------------------------------------------------------------------
    def _start_live_probe(self) -> None:
        if self._probe_running:
            return
        self._probe_running = True
        self.probe_btn.config(state="disabled", text=rtl("جاري الفحص واختبار المهارة..."))
        self.probe_status_var.set(rtl("جاري تشغيل 80 مباراة قياس + 60 مباراة اختبار مهارة..."))
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

            self.parent.after(0, lambda: self.probe_status_var.set(rtl("جاري تشغيل اختبار سقف المهارة (60 مباراة)...")))
            audit = run_skill_audit(fresh_db, agents, decks, seed=rng.randrange(1, 100_000), n_rand=30, n_noisy=30)
            ex["strategic_intel"].update(audit)

            ex["probe_ts"] = time.strftime("%Y-%m-%d %H:%M:%S")
            ex["probe_version"] = fresh_db.version
            tmp = self.probe_path + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(ex, f, indent=2, ensure_ascii=False)
            os.replace(tmp, self.probe_path)

            def _done_ok() -> None:
                self._probe_running = False
                self.probe_btn.config(state="normal", text=rtl("فحص مباشر + اختبار المهارة (80 + 60 مباراة)"))
                self.source_var.set(rtl("عيّنة الفحص المباشر (قوانين V2.2 الحالية)"))
                if self.on_probe_complete:
                    self.on_probe_complete()
                else:
                    self.refresh()

            self.parent.after(0, _done_ok)
        except Exception as err:
            def _done_err(msg: str = str(err)) -> None:
                self._probe_running = False
                self.probe_btn.config(state="normal", text=rtl("فحص مباشر + اختبار المهارة (80 + 60 مباراة)"))
                self.probe_status_var.set(rtl(f"خطأ بالفحص: {msg[:50]}"))
            self.parent.after(0, _done_err)

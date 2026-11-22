# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Pillar #7: Mechanics Performance Scorecard Tab (Arabic RTL).
Tracks all 14 real Deck Game V2.2 mechanics indicators across the 9 core systems:
  (Active Skills, Spells, Barriers, Leaders, Covenants, Environments, DoTs, Disruptions, Summon/Graveyard)
  + Passives (Last Breath, Ascend, Reflect, Evasion, Leech, Cleave) & Sacrifice Ramp WR Lift!
  - Overall 0-100% Mechanics Score (>=85% Ideal, >=70% Good, <70% Warning)
  - Visual Chart 1: Match Presence % across all 9 Core Systems (instant dead-mechanic detector!)
  - Visual Chart 2: Win-Rate Impact (WR% around 50% centerline) when each System is triggered!
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

SYSTEM_LABELS = [
    "المهارات الفعّالة (Actives)",
    "بطاقات السحر (Spells)",
    "الحواجز الدفاعية (Barriers)",
    "تمائم القادة (Leaders)",
    "العهود والقيود (Covenants)",
    "ساحات البيئة (Environments)",
    "ضرر الحالات (DoTs)",
    "التعطيل والتحكم (Disruptions)",
    "الاستدعاء والمقبرة (Tokens/Revive)",
]


class MechanicsPerformanceTab:
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

        has_main = int(self.store.excitement.get("mechanics_perf", {}).get("matches", 0)) > 0
        has_probe = bool(self.probe_data and int(self.probe_data.get("mechanics_perf", {}).get("matches", 0)) > 0)
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

        # Visual Charts Row (RTL: 9-System Match Presence on RIGHT 52%, WR Impact around 50% on LEFT 48%)
        charts = tk.Frame(self.inner, bg=BG_DARK)
        charts.pack(fill="x", pady=(0, 8))
        charts.grid_columnconfigure(0, weight=48, uniform="mp")
        charts.grid_columnconfigure(1, weight=52, uniform="mp")

        p_wr = self._make_panel(charts, "أثر كل ميكانيكية على نسبة الفوز عند تفعيلها (حول خط 50%)", COL_CYAN)
        p_wr.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        self.cv_wr = tk.Canvas(p_wr, bg=BG_PANEL, height=255, highlightthickness=0)
        self.cv_wr.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_wr.bind("<Configure>", lambda _e: self._draw_wr_impact())

        p_pres = self._make_panel(charts, "نسبة حضور الأنظمة الـ 9 في المباريات (كاشف الميكانيكيات الميتة)", COL_GOLD)
        p_pres.grid(row=0, column=1, sticky="nsew", padx=(5, 0))
        self.cv_pres = tk.Canvas(p_pres, bg=BG_PANEL, height=255, highlightthickness=0)
        self.cv_pres.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_pres.bind("<Configure>", lambda _e: self._draw_presence())

        # Self-Explanatory 14-Indicator Diagnostic Table
        tbl_panel = self._make_panel(
            self.inner,
            "بطاقة التقييم التفصيلية لمعيار أداء الميكانيكيات (14 مؤشراً — النتيجة + الجيد + المثالي + التشخيص التلقائي)",
            COL_BLUE,
        )
        tbl_panel.pack(fill="x", pady=(0, 8))

        headers = [
            ("diag", "ماذا يعني الرقم الآن؟ (تشخيص تلقائي)", 365),
            ("status", "التقييم", 96),
            ("good", "الجيد 🟡", 125),
            ("ideal", "المثالي 🟢", 125),
            ("actual", "نتيجتك الحالية", 138),
            ("metric", "المؤشر", 220),
            ("num", "#", 34),
        ]
        self.mp_tree = ttk.Treeview(tbl_panel, columns=tuple(c for c, _, _ in headers), show="headings", height=14)
        for c, txt, w in headers:
            self.mp_tree.heading(c, text=rtl(txt))
            self.mp_tree.column(c, width=w, anchor="e" if c in ("metric", "diag") else "center")

        self.mp_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.mp_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.mp_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.mp_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        self.mp_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.mp_tree)

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
        self._draw_presence()
        self._draw_wr_impact()

    @staticmethod
    def _grade_range(val: float, ideal_lo: float, ideal_hi: float, good_lo: float, good_hi: float) -> Tuple[str, str, int]:
        if ideal_lo <= val <= ideal_hi:
            return ("🟢 مثالي", "ideal", 100)
        if good_lo <= val <= good_hi:
            return ("🟡 جيد", "good", 72)
        return ("🔴 يحتاج تعديل", "bad", 28)

    def _compute_and_populate(self) -> None:
        self.mp_tree.delete(*self.mp_tree.get_children())
        ex = self._active_ex()
        mp = ex.get("mechanics_perf", {})
        matches = int(mp.get("matches", 0))
        sides = int(mp.get("sides", 0))

        if matches <= 0 or sides <= 0:
            self._last_metrics = {"has_data": False, "score": 0, "ideal_cnt": 0, "good_cnt": 0, "bad_cnt": 0}
            self.mp_tree.insert(
                "", "end",
                values=(
                    rtl("بانتظار بيانات التدريب — تظهر الأرقام تلقائياً فور بدء التدريب على المحاكي المحدّث أو الاتصال بالجهاز الثاني."),
                    rtl("بانتظار بيانات"), "--", "--", "--", rtl("تتبع أداء الـ 9 ميكانيكيات جاهز ومفعّل"), "!",
                ),
                tags=("nodata",),
            )
            return

        su_s = [int(x) for x in mp.get("sys_used_sides", [0] * 9)]
        su_w = [int(x) for x in mp.get("sys_used_wins", [0] * 9)]
        su_c = [int(x) for x in mp.get("sys_total_cnt", [0] * 9)]
        msp = [int(x) for x in mp.get("match_sys_present", [0] * 9)]
        tot_wins = int(mp.get("wins_total", sides // 2))

        def _wr_and_lift(idx: int) -> Tuple[float, float]:
            u_s = su_s[idx]
            u_w = su_w[idx]
            nu_s = max(0, sides - u_s)
            nu_w = max(0, tot_wins - u_w)
            wr_u = (100.0 * u_w / u_s) if u_s > 0 else 50.0
            wr_nu = (100.0 * nu_w / nu_s) if nu_s > 0 else 50.0
            return (wr_u, wr_u - wr_nu if (u_s > 0 and nu_s > 0) else (wr_u - 50.0))

        pres_pcts = [100.0 * msp[i] / matches for i in range(9)]
        wr_vals = [_wr_and_lift(i)[0] for i in range(9)]

        spells_pm = su_c[1] / float(matches)
        sp_wr, sp_lift = _wr_and_lift(1)
        barriers_pm = su_c[2] / float(matches)
        bar_wr, _ = _wr_and_lift(2)
        leader_act_pct = 100.0 * su_s[3] / sides
        ldr_wr, _ = _wr_and_lift(3)
        cov_act_pct = 100.0 * su_s[4] / sides
        cov_wr, cov_lift = _wr_and_lift(4)
        env_match_pct = pres_pcts[5]
        env_upg = int(mp.get("env_upgrades", 0))
        env_wr, _ = _wr_and_lift(5)

        actives_pm = su_c[0] / float(matches)
        dot_share = 100.0 * int(mp.get("dot_dmg", 0)) / max(1, int(mp.get("total_dmg", 0)))
        disrupt_pm = su_c[7] / float(matches)
        passive_pm = int(mp.get("passive_triggers", 0)) / float(matches)
        lb_cnt = int(mp.get("last_breaths", 0))
        asc_cnt = int(mp.get("ascends", 0))
        sumgrave_pm = su_c[8] / float(matches)

        sr_s = int(mp.get("sac_ramp_sides", 0))
        sr_w = int(mp.get("sac_ramp_wins", 0))
        sl_s = int(mp.get("sac_low_sides", 0))
        sl_w = int(mp.get("sac_low_wins", 0))
        sac_wr_hi = (100.0 * sr_w / sr_s) if sr_s > 0 else 50.0
        sac_wr_lo = (100.0 * sl_w / sl_s) if sl_s > 0 else 50.0
        sac_lift = sac_wr_hi - sac_wr_lo

        avg_sys_match = int(mp.get("active_systems_sum", 0)) / float(matches)
        dead_sys = [SYSTEM_LABELS[i].split(" ")[0] for i in range(9) if pres_pcts[i] < 10.0]
        dead_pct = 100.0 * len(dead_sys) / 9.0

        rows: List[Tuple[int, str, str, str, str, str, str, str, int]] = []

        # 1. Spells per Match
        st, tg, pts = self._grade_range(spells_pm, 3.0, 7.5, 1.8, 10.0)
        diag = (
            f"حضور صحي للسحر ({spells_pm:.1f} تعويذة/مباراة — ظهور في {pres_pcts[1]:.0f}% من المباريات)." if tg == "ideal"
            else (f"لعب السحر ضعيف ({spells_pm:.1f}/مباراة) — النماذج تفضل الوحوش وتضحي بالسحر." if spells_pm < 3.0 else "كثرة مفرطة للسحر على حساب صراع الوحوش.")
        )
        rows.append((1, "معدل تفعيل بطاقات السحر (Spells / Match)", f"{spells_pm:.1f} / مباراة", "3.0 – 7.5", "1.8 – 10.0", st, diag, tg, pts))

        # 2. Spell WR Impact
        st, tg, pts = self._grade_range(sp_lift, -3.0, 7.0, -7.0, 12.0)
        diag = (
            f"أثر السحر متوازن ومجزي (فوز مستخدم السحر {sp_wr:.1f}%، فرق {sp_lift:+.1f}%)." if tg == "ideal"
            else f"اختلال أثر السحر! (فوز {sp_wr:.1f}%، فرق {sp_lift:+.1f}% مقارنة بعدم استخدامه)."
        )
        rows.append((2, "أثر استخدام السحر على نسبة الفوز (WR Lift)", f"{sp_lift:+.1f}% (WR {sp_wr:.0f}%)", "-3% إلى +7%", "-7% إلى +12%", st, diag, tg, pts))

        # 3. Barriers per Match
        st, tg, pts = self._grade_range(barriers_pm, 1.0, 4.0, 0.4, 5.5)
        diag = (
            f"الحواجز الدفاعية حاضرة ومؤثرة ({barriers_pm:.2f} حاجز/مباراة • فوز {bar_wr:.0f}%)." if tg == "ideal"
            else f"الحواجز الدفاعية شبه مهجورة! ({barriers_pm:.2f} فقط/مباراة — ظهور {pres_pcts[2]:.0f}%، فوز {bar_wr:.0f}%)."
        )
        rows.append((3, "معدل استدعاء الحواجز الدفاعية (0 ATK)", f"{barriers_pm:.2f} / مباراة", "1.0 – 4.0", "0.4 – 5.5", st, diag, tg, pts))

        # 4. Leader Spirit Activation %
        st, tg, pts = self._grade_range(leader_act_pct, 35.0, 75.0, 20.0, 88.0)
        diag = (
            f"تفعيل ممتاز لتمائم القادة ({leader_act_pct:.1f}% من اللاعبين • فوز {ldr_wr:.0f}%)." if tg == "ideal"
            else f"تفعيل القادة منخفض ({leader_act_pct:.1f}% فقط من اللاعبين • فوز {ldr_wr:.0f}% — الديكات لا تضمهم أو تضحّي بهم!)."
        )
        rows.append((4, "نسبة تفعيل تمائم القادة (Leader Spirits)", f"{leader_act_pct:.1f}% (WR {ldr_wr:.0f}%)", "35% – 75%", "20% – 88%", st, diag, tg, pts))

        # 5. Covenant Activation % & WR Balance
        if 25.0 <= cov_act_pct <= 65.0 and -5.0 <= cov_lift <= 8.0:
            st, tg, pts = "🟢 مثالي", "ideal", 100
            diag = f"العهود متوازنة ومثيرة! (تُفعّل في {cov_act_pct:.1f}% من الجوانب، فوز {cov_wr:.0f}%، فرق {cov_lift:+.1f}%)."
        elif 15.0 <= cov_act_pct <= 78.0 and -10.0 <= cov_lift <= 14.0:
            st, tg, pts = "🟡 جيد", "good", 72
            diag = f"مشاركة العهود مقبولة ({cov_act_pct:.1f}% • فوز {cov_wr:.0f}%، فرق {cov_lift:+.1f}%)."
        else:
            st, tg, pts = "🔴 يحتاج تعديل", "bad", 28
            diag = f"العهود تحتاج مراجعة! (تفعيل {cov_act_pct:.1f}% • فوز {cov_wr:.0f}%، فرق {cov_lift:+.1f}% — إما نادرة أو عقوبتها قاسية!)."
        rows.append((5, "أداء العهود والقيود (مذبح الجناح الأيمن)", f"{cov_act_pct:.1f}% | WR {cov_wr:.0f}%", "25–65% | متوازن", "15–78%", st, diag, tg, pts))

        # 6. Environment Activation % & Upgrades
        st, tg, pts = self._grade_range(env_match_pct, 25.0, 70.0, 12.0, 85.0)
        diag = (
            f"ساحات البيئة حاضرة ({env_match_pct:.0f}% من المباريات • ترقيات Lv2/3: {env_upg} • فوز {env_wr:.0f}%)." if tg == "ideal"
            else f"بطاقات البيئة نادرة ({env_match_pct:.0f}% من المباريات فقط • ترقيات: {env_upg}) — معظم الديكات تتجاهلها!"
        )
        rows.append((6, "حضور ساحات البيئة المشتركة وترقيتها", f"{env_match_pct:.1f}% (ترقية:{env_upg})", "25% – 70%", "12% – 85%", st, diag, tg, pts))

        # 7. Active Skills Intensity / Match
        st, tg, pts = self._grade_range(actives_pm, 6.0, 16.0, 3.0, 22.0)
        diag = (
            f"كثافة مهارات ممتعة ومتوازنة ({actives_pm:.1f} مهارة/مباراة)." if tg == "ideal"
            else (f"إفراط في تشغيل المهارات الفعّالة ({actives_pm:.1f}/مباراة — تبتلع المانا وتقتل الوحوش بسرعة!)." if actives_pm > 16.0 else "تفعيل المهارات الفعّالة قليل.")
        )
        rows.append((7, "كثافة تشغيل المهارات الفعّالة (Actives)", f"{actives_pm:.1f} / مباراة", "6.0 – 16.0", "3.0 – 22.0", st, diag, tg, pts))

        # 8. DoT Damage Share % (Burn / Poison / Bleed)
        st, tg, pts = self._grade_range(dot_share, 5.0, 18.0, 2.0, 26.0)
        diag = (
            f"الحرق والسم والنزيف تمثل {dot_share:.1f}% من إجمالي الضرر (دور تكتيكي ممتاز)." if tg == "ideal"
            else (f"تأثير DoTs ضعيف جداً ({dot_share:.1f}% فقط من الضرر)." if dot_share < 5.0 else f"ضرر الحالات المستمرة مبالغ فيه ({dot_share:.1f}%!).")
        )
        rows.append((8, "حصة ضرر الحالات المستمرة (حرق/سم/نزيف)", f"{dot_share:.1f}%", "5% – 18%", "2% – 26%", st, diag, tg, pts))

        # 9. Disruptions & Control / Match
        st, tg, pts = self._grade_range(disrupt_pm, 4.0, 14.0, 2.0, 20.0)
        diag = (
            f"تفاعل تحكم وتعطيل ممتع ({disrupt_pm:.1f} تعطيل/مباراة: تجميد، صمت، جذور، كسر)." if tg == "ideal"
            else ("أدوات التعطيل قليلة بالمباريات." if disrupt_pm < 4.0 else f"التعطيل مفرط جداً ({disrupt_pm:.1f}/مباراة) وقد يسبب إحباطاً!")
        )
        rows.append((9, "كثافة التعطيل والتحكم (تجميد/صمت/كسر)", f"{disrupt_pm:.1f} / مباراة", "4.0 – 14.0", "2.0 – 20.0", st, diag, tg, pts))

        # 10. Passive Triggers / Match (Reflect, Evasion, LB, Ascend, Leech, Cleave)
        st, tg, pts = self._grade_range(passive_pm, 5.0, 18.0, 2.5, 26.0)
        diag = (
            f"القدرات السلبية نشطة وحيوية ({passive_pm:.1f}/مباراة • نفس أخير:{lb_cnt} • ارتقاء:{asc_cnt})." if tg == "ideal"
            else f"تفعيل القدرات السلبية ({passive_pm:.1f}/مباراة • نفس أخير:{lb_cnt} • ارتقاء:{asc_cnt})."
        )
        rows.append((10, "نشاط القدرات السلبية (نفس أخير/ارتقاء/عكس..)", f"{passive_pm:.1f} / مباراة", "5.0 – 18.0", "2.5 – 26.0", st, diag, tg, pts))

        # 11. Tokens + Clones + Revives / Match
        st, tg, pts = self._grade_range(sumgrave_pm, 2.0, 8.0, 0.8, 12.0)
        diag = (
            f"ميكانيكيات التوكنز والنسخ وإحياء المقبرة حيّة ({sumgrave_pm:.1f}/مباراة)." if tg == "ideal"
            else ("ميكانيكيات الاستدعاء والمقبرة شبه غائبة." if sumgrave_pm < 2.0 else "تضخم كبير في التوكنز والإحياء.")
        )
        rows.append((11, "ميكانيكيات التوكنز والنسخ والمقبرة", f"{sumgrave_pm:.1f} / مباراة", "2.0 – 8.0", "0.8 – 12.0", st, diag, tg, pts))

        # 12. Sacrifice Ramp WR Lift
        st, tg, pts = self._grade_range(sac_lift, -4.0, 8.0, -9.0, 14.0)
        diag = (
            f"تضحية المانا اختيار تكتيكي عادل! (فوز المضحّي >=3: {sac_wr_hi:.0f}% مقابل <=2: {sac_wr_lo:.0f}% — فرق {sac_lift:+.1f}%)." if tg == "ideal"
            else f"تضحية المانا منحازة بوضوح ({sac_wr_hi:.0f}% للمضحّي بكثرة مقابل {sac_wr_lo:.0f}% للمحتفظ بالورق — فرق {sac_lift:+.1f}%)."
        )
        rows.append((12, "أثر التضحية للمانا (>=3 تضحيات) على الفوز", f"{sac_lift:+.1f}% ({sac_wr_hi:.0f}%/{sac_wr_lo:.0f}%)", "-4% إلى +8%", "-9% إلى +14%", st, diag, tg, pts))

        # 13. Avg Active Core Systems / Match (out of 9)
        st, tg, pts = self._grade_range(avg_sys_match, 5.5, 8.2, 4.2, 9.0)
        diag = (
            f"ثراء وتنوع رائع! تتفاعل {avg_sys_match:.1f} أنظمة مختلفة من أصل 9 في كل مباراة." if tg == "ideal"
            else f"تنوع الأنظمة محدود ({avg_sys_match:.1f} فقط من 9 أنظمة تظهر بالمباراة — عدة أنظمة غائبة!)."
        )
        rows.append((13, "متوسط الأنظمة المتفاعلة بالمباراة (من 9)", f"{avg_sys_match:.1f} / 9 أنظمة", "5.5 – 8.2", "4.2 – 9.0", st, diag, tg, pts))

        # 14. Dead / Unused Mechanics % (<10% match presence)
        st, tg, pts = self._grade_range(dead_pct, 0.0, 12.0, 0.0, 25.0)
        diag = (
            "ممتاز! كل الأنظمة الـ 9 الأساسية حيّة وتظهر في أكثر من 10% من المباريات." if not dead_sys
            else f"تحذير! يوجد {len(dead_sys)} ميكانيكيات شبه ميتة (<10% ظهور): {', '.join(dead_sys)}!"
        )
        rows.append((14, "نسبة الميكانيكيات المهملة/الميتة (<10% ظهور)", f"{dead_pct:.1f}% ({len(dead_sys)}/9)", "0% – 12%", "12% – 25%", st, diag, tg, pts))

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
            "pres_pcts": pres_pcts,
            "wr_vals": wr_vals,
            "su_s": su_s,
        }

        for num, m_name, act_s, ideal_s, good_s, st_lbl, diag_s, tag, _ in rows:
            self.mp_tree.insert(
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
            cv.create_text(w // 2, h // 2, text=rtl("بانتظار بيانات التدريب الجديدة للميكانيكيات — ابدأ التدريب أو اتصل بالجهاز الثاني!"), fill=COL_GOLD, font=(FONT_UI, 11, "bold"))
            return

        sc = int(d["score"])
        if sc >= 85:
            col, badge = COL_GREEN, "🟢 كل ميكانيكيات اللعبة حيّة ومتناغمة وتصنع عمقاً رائعاً (AAA)"
        elif sc >= 70:
            col, badge = COL_GOLD, "🟡 أداء جيد للميكانيكيات (بعض الأنظمة مثل الحواجز/البيئات/القادة تحتاج تنشيطاً)"
        else:
            col, badge = COL_RED, "🔴 ميكانيكيات عديدة مهملة أو غير متوازنة (راجع المؤشرات الحمراء لإنقاذها!)"

        cv.create_text(w - 12, 18, text=rtl(f"التقييم العام لمعيار أداء الميكانيكيات:  {sc} / 100%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 14, "bold"))
        cv.create_text(w - 12, 44, text=rtl(badge), anchor="e", fill=col, font=(FONT_UI, 10, "bold"))
        cv.create_text(
            w - 12, 66,
            text=rtl(f"المعيار: 🟢 >=85% مثالي | 🟡 70–84% جيد | 🔴 <70% خطر  •  (الأنظمة المقاسة: 9 أنظمة أساسية • المباريات: {d['matches']:,})"),
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
    # Chart 1: 9-System Match Presence % (Dead Mechanic Detector)
    # ------------------------------------------------------------------
    def _draw_presence(self) -> None:
        cv = self.cv_pres
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(220, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات حضور أنظمة بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        pres = d["pres_pcts"]
        lbl_x = w - 10
        bar_r = w - 182
        bar_l = 52
        bar_w = max(60, bar_r - bar_l)
        row_h = max(24, (h - 16) // 9)

        # 10% dead-mechanic warning dashed line
        x10 = bar_r - bar_w * 0.10
        cv.create_line(x10, 8, x10, h - 6, fill=COL_RED, dash=(2, 2))

        for i, lbl in enumerate(SYSTEM_LABELS):
            y = 10 + i * row_h
            pv = pres[i]
            col = COL_RED if pv < 15.0 else (COL_GOLD if pv < 35.0 else COL_GREEN)
            cv.create_text(lbl_x, y + 9, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(bar_l, y + 3, bar_r, y + 15, fill="#242C3D", outline="")
            fw = max(2, int(bar_w * min(100.0, pv) / 100.0)) if pv > 0.1 else 0
            if fw > 0:
                cv.create_rectangle(bar_r - fw, y + 3, bar_r, y + 15, fill=col, outline="")
            cv.create_text(bar_l - 6, y + 9, text=f"{pv:4.1f}%", anchor="e", fill=col, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Chart 2: Win-Rate Impact around 50% Centerline for all 9 Systems
    # ------------------------------------------------------------------
    def _draw_wr_impact(self) -> None:
        cv = self.cv_wr
        cv.delete("all")
        w = max(340, cv.winfo_width())
        h = max(220, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات أثر فوز بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        wrs = d["wr_vals"]
        su_s = d["su_s"]
        lbl_x = w - 10
        bar_r = w - 150
        bar_l = 66
        bar_w = max(70, bar_r - bar_l)
        x50 = bar_l + bar_w * 0.50

        cv.create_line(x50, 14, x50, h - 6, fill=COL_GOLD, dash=(3, 3))
        cv.create_text(x50, 7, text="50%", fill=COL_GOLD, font=(FONT_UI, 7, "bold"))
        cv.create_text(bar_l, 7, text="20%", fill=FG_MUTED, font=(FONT_UI, 7))
        cv.create_text(bar_r, 7, text="80%", fill=FG_MUTED, font=(FONT_UI, 7))

        short_names = [
            "المهارات الفعّالة", "بطاقات السحر", "الحواجز الدفاعية",
            "تمائم القادة", "العهود والقيود", "ساحات البيئة",
            "ضرر الحالات DoT", "التعطيل والتحكم", "الاستدعاء والمقبرة",
        ]
        row_h = max(24, (h - 22) // 9)
        for i, lbl in enumerate(short_names):
            y = 18 + i * row_h
            cnt = su_s[i]
            wr = wrs[i]
            if cnt < 5:
                col = FG_MUTED
            elif 45.0 <= wr <= 55.0:
                col = COL_GREEN
            elif 40.0 <= wr <= 60.0:
                col = COL_GOLD
            else:
                col = COL_RED
            cv.create_text(lbl_x, y + 8, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8))
            cv.create_rectangle(bar_l, y + 2, bar_r, y + 14, fill="#1F2736", outline="")
            if cnt >= 5:
                px = bar_l + bar_w * (max(20.0, min(80.0, wr)) - 20.0) / 60.0
                cv.create_rectangle(min(x50, px), y + 2, max(x50, px), y + 14, fill=col, outline="")
                cv.create_text(bar_l - 6, y + 8, text=f"{wr:4.1f}%", anchor="e", fill=col, font=(FONT_UI, 8, "bold"))
            else:
                cv.create_text(bar_l - 6, y + 8, text=f"n={cnt}", anchor="e", fill=FG_MUTED, font=(FONT_UI, 7))

    # ------------------------------------------------------------------
    # Shared 80-Match Non-Destructive Live Probe
    # ------------------------------------------------------------------
    def _start_live_probe(self) -> None:
        if self._probe_running:
            return
        self._probe_running = True
        self.probe_btn.config(state="disabled", text=rtl("جاري تشغيل 80 مباراة..."))
        self.probe_status_var.set(rtl("جاري قياس أداء الـ 9 ميكانيكيات عبر 80 مباراة حية..."))
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

# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Tab #17: Active Skill & Spell Mana-Cost Pricing Advisor (`skill_pricing_tab.py`).

Evaluates EVERY Active Skill (all 38 kinds & per-cost tiers) AND all 300 Spells using live
per-skill telemetry from `BattleEngine` (`skill_pricing` in `excitement_anomalies.json`) with
automatic card-level fallback for legacy logs:
  - Tracks exact Activations/Match, Mana Spent, Monster Damage, Face Damage, Healing/Buff Value,
    Direct Kills, Disruptions, Utility/Aether Gain, Win Rate %, and Value Per 1 Mana (VPM).
  - Computes a clear, mathematically grounded Mana-Cost Recommendation for every skill:
      * 🔴 ارفع +2 مانا   (Severely underpriced / spammed / high WR & VPM)
      * 🟠 ارفع +1 مانا   (Overperforming or spammed — raise cost by +1 Mana)
      * 🟢 مثالي          (Fair cost — keep as is)
      * 🔵 خفّض -1 مانا   (Overpriced / weak / neglected — lower cost by -1 Mana)
      * 🟣 قوّي الأثر     (Weak & already 1 Mana — buff effect +20% instead of going to 0)
  - Includes 2 visual charts, filter/search controls, sortable columns, and click-to-inspect
    drill-down listing the exact Monster/Spell cards carrying the selected skill!
"""

from __future__ import annotations

import math
import statistics
import tkinter as tk
from collections import defaultdict
from tkinter import ttk
from typing import Any, Callable, Dict, List, Optional, Tuple

from engine.card_model import CardDB, CardDef
from trainer.stats_store import StatsStore
from pace_tab import rtl
from arabic_glossary import (
    ACTIVE_AR,
    ELEM_AR,
    ROLE_AR,
    SPELL_AR,
    TRIBE_AR,
    enable_treeview_sorting,
    explain_card_arabic,
)

BG_DARK = "#0F131A"
BG_PANEL = "#171C26"
BG_CARD = "#1D2432"
BG_BORDER = "#2A3346"
BG_INPUT = "#121721"
FG_TEXT = "#EEF2F9"
FG_MUTED = "#94A6C0"

COL_BLUE = "#3B82F6"
COL_CYAN = "#06B6D4"
COL_GREEN = "#10B981"
COL_GOLD = "#F59E0B"
COL_RED = "#EF4444"
COL_ORANGE = "#F97316"
COL_PURPLE = "#A855F7"
COL_PINK = "#EC4899"

FONT_UI = "Segoe UI"


def compute_skill_impact_value(
    dmg_mon: float,
    dmg_face: float,
    heal_buff: float,
    kills: float,
    disrupt: float,
    util: float,
) -> float:
    """Weighted composite gameplay value produced per single activation of an Active Skill."""
    return (
        1.00 * dmg_mon
        + 1.35 * dmg_face
        + 0.75 * heal_buff
        + 18.0 * kills
        + 10.0 * disrupt
        + 14.0 * util
    )


def recommend_mana_pricing(
    *,
    is_spell: bool,
    cur_cost: int,
    wr: Optional[float],
    uses: int,
    tot_matches: int,
    uses_per_match: float,
    uses_per_card_pm: float,
    median_upcm: float,
    vpm: Optional[float],
    median_vpm: Optional[float],
    kills_pu: float,
    dmg_face_pu: float,
    heal_buff_pu: float,
    card_count: int,
    ar_name: str,
) -> Tuple[str, str, int, str]:
    """
    Returns (verdict_badge, row_tag, suggested_cost, arabic_reason).
    Uses 4 orthogonal signals: Win Rate, Value-Per-1-Mana (VPM), Usage/Spam Rate, and Abuse/Lethality.
    """
    max_cap = 7 if is_spell else 6
    min_uses = 30 if not is_spell else 25

    if tot_matches < 50:
        return (
            "⚪ بانتظار بيانات",
            "nodata",
            cur_cost,
            f"عدد المباريات المسجلة حالياً ({tot_matches}) أقل من 50 — ابدأ التدريب لتظهر توصيات التسعير الدقيقة.",
        )

    if uses < min_uses or wr is None:
        if uses == 0 and card_count >= 3 and tot_matches >= 200:
            sug = max(1, cur_cost - 1) if cur_cost >= 2 else cur_cost
            if cur_cost >= 2:
                return (
                    "🔵 خفّض -1 مانا",
                    "drop",
                    sug,
                    f"مهارة مهجورة تماماً (0 تفعيل رغم وجودها على {card_count} بطاقات) — خفّض تكلفتها من {cur_cost} إلى {sug} مانا لتنشيطها.",
                )
            return (
                "🟣 قوّي الأثر",
                "buff",
                cur_cost,
                f"مهارة مهجورة (0 تفعيل) وسعرها بالفعل {cur_cost} مانا — قوِّ تأثيرها بـ +25% لجذب اللاعبين.",
            )
        return (
            "⚪ عيّنة صغيرة",
            "nodata",
            cur_cost,
            f"عدد التفعيلات قليل ({uses}) — بانتظار مزيد من المباريات لإصدار توصية تسعير دقيقة.",
        )

    score = 0
    reasons: List[str] = []

    # 1. Win Rate Signal
    if wr >= 57.0:
        score += 2
        reasons.append(f"فوز مرتفع جداً ({wr:.1f}%)")
    elif wr >= 53.5:
        score += 1
        reasons.append(f"فوز فوق المتوسط ({wr:.1f}%)")
    elif wr <= 43.5:
        score -= 2
        reasons.append(f"فوز منخفض جداً ({wr:.1f}%)")
    elif wr <= 46.5:
        score -= 1
        reasons.append(f"فوز أضعف من المتوسط ({wr:.1f}%)")

    # 2. Value-Per-1-Mana (VPM) Signal (when live per-skill telemetry is available)
    vpm_ratio: Optional[float] = None
    if vpm is not None and median_vpm is not None and median_vpm > 0.1:
        vpm_ratio = vpm / median_vpm
        if vpm_ratio >= 1.90:
            score += 2
            reasons.append(f"عائد هائل لكل مانا ({vpm:.1f} = {vpm_ratio:.1f}× المتوسط)")
        elif vpm_ratio >= 1.35:
            score += 1
            reasons.append(f"عائد مرتفع لكل مانا ({vpm:.1f} = {vpm_ratio:.1f}× المتوسط)")
        elif vpm_ratio <= 0.50:
            score -= 2
            reasons.append(f"عائد ضعيف جداً لكل مانا ({vpm:.1f} = {vpm_ratio:.1f}× المتوسط)")
        elif vpm_ratio <= 0.72:
            score -= 1
            reasons.append(f"عائد أقل من المتوسط ({vpm:.1f} = {vpm_ratio:.1f}× المتوسط)")

    # 3. Spam / Overuse vs Neglect Frequency Signal
    if median_upcm > 1e-6:
        spam_ratio = uses_per_card_pm / median_upcm
        if spam_ratio >= 2.2 and uses_per_match >= (1.2 if not is_spell else 0.12):
            score += 1
            reasons.append(f"تكرار مفرط ({uses_per_match:.2f}/مباراة = {spam_ratio:.1f}× المتوسط)")
        elif spam_ratio <= 0.25 and card_count >= 4 and uses_per_match < 0.25:
            score -= 1
            reasons.append(f"استخدام نادر ({uses_per_match:.2f}/مباراة)")

    # 4. Direct Lethality / Healing / Face-Burst Abuse Signal (Active Skills)
    if not is_spell:
        if kills_pu >= 0.40 and cur_cost <= 3:
            score += 1
            reasons.append(f"معدل قتل مباشر مرتفع ({kills_pu:.2f} قتلة/تفعيل)")
        if dmg_face_pu >= 18.0 and cur_cost <= 2:
            score += 1
            reasons.append(f"حرق مباشر مرتفع للقائد ({dmg_face_pu:.1f} ضرر/تفعيل)")
        if heal_buff_pu >= 36.0 and cur_cost <= 2:
            score += 1
            reasons.append(f"شفاء/تعزيز ضخم مقابل {cur_cost} مانا ({heal_buff_pu:.1f}/تفعيل)")

    # Convert composite score into explicit Mana Cost verdict
    if score >= 3 and cur_cost < max_cap:
        delta = min(2, max_cap - cur_cost)
        sug = cur_cost + delta
        badge = f"🔴 ارفع +{delta} مانا"
        tag = "raise2"
        why = " + ".join(reasons) if reasons else "أداء وتكرار مفرطان"
        return (badge, tag, sug, f"ارفع السعر من {cur_cost} ← {sug} مانا: {why}.")

    if (score >= 1 or (wr >= 55.0 and score >= 0)) and cur_cost < max_cap:
        sug = cur_cost + 1
        badge = "🟠 ارفع +1 مانا"
        tag = "raise1"
        why = " + ".join(reasons) if reasons else f"أداء فوق المتوسط ({wr:.1f}%)"
        return (badge, tag, sug, f"ارفع السعر من {cur_cost} ← {sug} مانا: {why}.")

    if score >= 1 and cur_cost >= max_cap:
        why = " + ".join(reasons) if reasons else f"فوز مرتفع ({wr:.1f}%)"
        return (
            "🔴 خفّض الضرر (سقف مانا)",
            "raise2",
            cur_cost,
            f"وصلت لسقف المانا ({cur_cost}) — اخفض قيمة التأثير بـ 15–20% بدل رفع المانا: {why}.",
        )

    if score <= -2 or (wr <= 44.5 and score <= -1):
        why = " + ".join(reasons) if reasons else f"أداء ضعيف ({wr:.1f}%)"
        if cur_cost >= 2:
            sug = cur_cost - 1
            return ("🔵 خفّض -1 مانا", "drop", sug, f"خفّض السعر من {cur_cost} ← {sug} مانا: {why}.")
        return (
            "🟣 قوّي الأثر (+20%)",
            "buff",
            cur_cost,
            f"سعرها بالفعل {cur_cost} مانا (الحد الأدنى) — قوِّ تأثير [{ar_name}] بـ +20–25%: {why}.",
        )

    why = " + ".join(reasons) if reasons else f"توازن صحي ({wr:.1f}%)"
    return ("🟢 تكلفة مثالية", "ideal", cur_cost, f"تسعير عادل عند {cur_cost} مانا — أبقِها كما هي ({why}).")


class SkillPricingAdvisorTab:
    """Tab #17: Dedicated Active-Skill & Spell Mana-Cost Pricing Evaluator (Arabic RTL)."""

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
        self._mode_var = tk.StringVar(value="skill_tier")  # skill_tier | skill_kind | spells
        self._filter_var = tk.StringVar(value="all")       # all | raise | drop | ideal
        self._search_var = tk.StringVar(value="")
        self._all_rows: List[Dict[str, Any]] = []
        self._filtered_rows: List[Dict[str, Any]] = []
        self._last_metrics: Dict[str, Any] = {}
        self._build_layout()

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

        # 1. Hero Banner (Pricing Health Score + 5 Verdict KPIs + Live/Fallback Telemetry Status)
        hero = tk.Frame(self.inner, bg=BG_CARD, highlightbackground=COL_GOLD, highlightthickness=1, padx=14, pady=8)
        hero.pack(fill="x", pady=(2, 8))
        self.cv_hero = tk.Canvas(hero, bg=BG_CARD, height=112, highlightthickness=0)
        self.cv_hero.pack(fill="x")
        self.cv_hero.bind("<Configure>", lambda _e: self._draw_hero_banner())

        # 2. Visual Charts Row (2 Live Charts)
        charts = tk.Frame(self.inner, bg=BG_DARK)
        charts.pack(fill="x", pady=(0, 8))
        charts.grid_columnconfigure(0, weight=50, uniform="sp")
        charts.grid_columnconfigure(1, weight=50, uniform="sp")

        p_left = self._make_panel(charts, "أعلى المهارات تكراراً في المباراة مقابل نسبة الفوز (كاشف الإسبام)", COL_ORANGE)
        p_left.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        self.cv_spam = tk.Canvas(p_left, bg=BG_PANEL, height=235, highlightthickness=0)
        self.cv_spam.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_spam.bind("<Configure>", lambda _e: self._draw_spam_chart())

        p_right = self._make_panel(charts, "أعلى المهارات قيمةً لكل 1 مانا (VPM — مرشحة لرفع السعر) مقابل الأقل", COL_CYAN)
        p_right.grid(row=0, column=1, sticky="nsew", padx=(5, 0))
        self.cv_vpm = tk.Canvas(p_right, bg=BG_PANEL, height=235, highlightthickness=0)
        self.cv_vpm.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_vpm.bind("<Configure>", lambda _e: self._draw_vpm_chart())

        # 3. Mode & Filter Toolbar
        ctrl = tk.Frame(self.inner, bg=BG_PANEL, highlightbackground=BG_BORDER, highlightthickness=1, padx=10, pady=7)
        ctrl.pack(fill="x", pady=(0, 8))

        # Right: Mode radios
        m_box = tk.Frame(ctrl, bg=BG_PANEL)
        m_box.pack(side="right")
        tk.Label(m_box, text=rtl("نطاق التقييم:"), bg=BG_PANEL, fg=FG_MUTED, font=(FONT_UI, 8, "bold")).pack(side="right", padx=(6, 0))
        for val, lbl in (
            ("skill_tier", "⚡ المهارات × التكلفة (دقيق)"),
            ("skill_kind", "⚡ 38 مهارة مجمّعة"),
            ("spells", "📜 بطاقات السحر (300)"),
        ):
            tk.Radiobutton(
                m_box, text=rtl(lbl), variable=self._mode_var, value=val,
                command=self.refresh,
                bg=BG_PANEL, fg=FG_TEXT, selectcolor=BG_CARD,
                activebackground=BG_PANEL, activeforeground=COL_GOLD,
                font=(FONT_UI, 8, "bold"), cursor="hand2",
            ).pack(side="right", padx=4)

        # Middle: Verdict Filter buttons
        f_box = tk.Frame(ctrl, bg=BG_PANEL)
        f_box.pack(side="right", padx=(14, 0))
        for val, lbl, fg in (
            ("all", "الكل", FG_TEXT),
            ("raise", "🔴 ارفع السعر", "#FCA5A5"),
            ("drop", "🔵 خفّض / قوّي", "#93C5FD"),
            ("ideal", "🟢 مثالي", "#86EFAC"),
        ):
            tk.Radiobutton(
                f_box, text=rtl(lbl), variable=self._filter_var, value=val,
                command=self._apply_filter_and_fill,
                bg=BG_PANEL, fg=fg, selectcolor=BG_CARD,
                activebackground=BG_PANEL, activeforeground=fg,
                font=(FONT_UI, 8, "bold"), cursor="hand2",
            ).pack(side="right", padx=3)

        # Left: Search entry
        s_box = tk.Frame(ctrl, bg=BG_PANEL)
        s_box.pack(side="left")
        tk.Label(s_box, text=rtl("🔍 بحث:"), bg=BG_PANEL, fg=FG_MUTED, font=(FONT_UI, 8, "bold")).pack(side="right", padx=(4, 0))
        ent = tk.Entry(
            s_box, textvariable=self._search_var, width=20,
            bg=BG_INPUT, fg=FG_TEXT, insertbackground=FG_TEXT, relief="flat",
            font=(FONT_UI, 9), justify="right",
        )
        ent.pack(side="right", ipady=2)
        self._search_var.trace_add("write", lambda *_: self._apply_filter_and_fill())

        # 4. Main Sortable Skill Mana Pricing Table
        tbl_panel = self._make_panel(
            self.inner,
            "جدول التوصية المباشرة لتسعير المانا لكل مهارة (اضغط على أي عمود للترتيب • اضغط على أي صف لعرض بطاقاته بالأسفل)",
            COL_GOLD,
        )
        tbl_panel.pack(fill="x", pady=(0, 8))

        headers = [
            ("reason", "النصيحة التفصيلية والسبب الرياضي", 340),
            ("verdict", "قرار التسعير", 122),
            ("vpm", "القيمة/1مانا", 96),
            ("impact", "متوسط الأثر/تفعيل", 132),
            ("wr", "نسبة الفوز", 78),
            ("upm", "تفعيل/مباراة", 88),
            ("sug", "المقترح", 92),
            ("cur", "الحالي", 66),
            ("cards", "البطاقات", 58),
            ("skill", "المهارة الفعّالة / السحر", 215),
            ("num", "#", 32),
        ]
        self.sp_tree = ttk.Treeview(tbl_panel, columns=tuple(c for c, _, _ in headers), show="headings", height=16)
        for c, txt, w in headers:
            self.sp_tree.heading(c, text=rtl(txt))
            self.sp_tree.column(c, width=w, anchor="e" if c in ("skill", "reason") else "center")
        self.sp_tree.tag_configure("raise2", foreground="#FCA5A5", background="#2E1619")
        self.sp_tree.tag_configure("raise1", foreground="#FDBA74", background="#2B1F15")
        self.sp_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.sp_tree.tag_configure("drop", foreground="#93C5FD", background="#152033")
        self.sp_tree.tag_configure("buff", foreground="#D8B4FE", background="#221730")
        self.sp_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)
        self.sp_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.sp_tree)
        self.sp_tree.bind("<<TreeviewSelect>>", self._on_row_selected)

        # 5. Drill-Down Inspector (Lists exact cards carrying selected skill)
        drill_panel = self._make_panel(
            self.inner,
            "البطاقات الحاملة للمهارة المحددة (اضغط على أي مهارة بالأعلى لمعرفة أرقام البطاقات المطلوب تعديلها)",
            COL_PURPLE,
        )
        drill_panel.pack(fill="x", pady=(0, 8))

        self.drill_lbl = tk.Label(
            drill_panel,
            text=rtl("اختر أي صف من الجدول بالأعلى لعرض قائمة البطاقات الحاملة للمهارة ونسبة فوز كل بطاقة."),
            bg=BG_PANEL, fg=FG_MUTED, font=(FONT_UI, 8), anchor="e", justify="right",
        )
        self.drill_lbl.pack(fill="x", padx=10, pady=(0, 4))

        d_headers = [
            ("desc", "تفاصيل القدرة والمهارات", 370),
            ("wr", "فوز البطاقة", 84),
            ("plays", "مرات اللعب", 80),
            ("stats", "الإحصائيات", 105),
            ("scost", "تكلفة المهارة", 86),
            ("mcost", "تكلفة الكارت", 82),
            ("name", "اسم البطاقة", 215),
            ("cid", "ID", 52),
        ]
        self.drill_tree = ttk.Treeview(drill_panel, columns=tuple(c for c, _, _ in d_headers), show="headings", height=7)
        for c, txt, w in d_headers:
            self.drill_tree.heading(c, text=rtl(txt))
            self.drill_tree.column(c, width=w, anchor="e" if c in ("name", "desc") else "center")
        self.drill_tree.pack(fill="x", padx=8, pady=(0, 8))
        enable_treeview_sorting(self.drill_tree)

    def _make_panel(self, parent: tk.Widget, title: str, accent: str) -> tk.Frame:
        f = tk.Frame(parent, bg=BG_PANEL, highlightbackground=BG_BORDER, highlightthickness=1)
        hdr = tk.Frame(f, bg=BG_PANEL, padx=10, pady=7)
        hdr.pack(fill="x")
        tk.Frame(hdr, bg=accent, width=4, height=15).pack(side="right", padx=(7, 0))
        tk.Label(hdr, text=rtl(title), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold"), anchor="e", justify="right").pack(side="right")
        return f

    def refresh(self) -> None:
        self._compute_rows()
        self._apply_filter_and_fill()
        self._draw_hero_banner()
        self._draw_spam_chart()
        self._draw_vpm_chart()

    def _compute_rows(self) -> None:
        mode = self._mode_var.get()
        ex = self.store.excitement or {}
        sp_map: Dict[str, List[int]] = ex.get("skill_pricing") or {}
        raw_tot_matches = int(ex.get("total_matches", 0))
        tot_matches = max(1, raw_tot_matches)
        has_live_sp = any(int(v[0]) > 0 for v in sp_map.values() if isinstance(v, list) and len(v) >= 8)

        rows: List[Dict[str, Any]] = []

        if mode == "spells":
            # Evaluate all 300 Spell cards individually for Mana-Cost Pricing!
            spell_cards = [c for c in self.db.cards.values() if c.type == "spell"]
            plays_list = []
            raw_items = []
            for c in spell_cards:
                st = self.store.card_stats.get(str(c.id), {})
                pl = int(st.get("played", 0))
                pw = int(st.get("played_wins", 0))
                wr = (100.0 * pw / pl) if pl >= 10 else None
                upm = pl / tot_matches if raw_tot_matches > 0 else 0.0
                if pl >= 25:
                    plays_list.append(upm)
                sk = str((c.spell_ops or {}).get("kind", ""))
                ar_k = SPELL_AR.get(sk, (sk, ""))[0]
                raw_items.append((c, pl, wr, upm, ar_k))
            med_upcm = statistics.median(plays_list) if plays_list else 0.05

            for c, pl, wr, upm, ar_k in raw_items:
                verdict, tag, sug, reason = recommend_mana_pricing(
                    is_spell=True,
                    cur_cost=int(c.mana),
                    wr=wr,
                    uses=pl,
                    tot_matches=raw_tot_matches,
                    uses_per_match=upm,
                    uses_per_card_pm=upm,
                    median_upcm=med_upcm,
                    vpm=None,
                    median_vpm=None,
                    kills_pu=0.0,
                    dmg_face_pu=0.0,
                    heal_buff_pu=0.0,
                    card_count=1,
                    ar_name=c.name,
                )
                rows.append({
                    "key": f"spell|{c.id}",
                    "skill_label": f"#{c.id} {c.name} ({ar_k})",
                    "short_label": f"#{c.id} {c.name[:18]}",
                    "cards": [c],
                    "card_count": 1,
                    "cur_cost": int(c.mana),
                    "sug_cost": sug,
                    "uses": pl,
                    "upm": upm,
                    "wr": wr,
                    "impact_str": ar_k[:26],
                    "vpm": None,
                    "vpm_str": "--",
                    "verdict": verdict,
                    "tag": tag,
                    "reason": reason,
                    "live_telemetry": True,
                })
        else:
            # Active Skills (either per (kind, cost) tier or aggregated by kind)
            groups: Dict[str, List[Tuple[CardDef, Dict[str, Any]]]] = defaultdict(list)
            for c in self.db.cards.values():
                if c.type != "monster":
                    continue
                for a in (c.actives or []):
                    kind = str(a.get("kind", "dmg_single"))
                    bcost = int(a.get("cost", 1))
                    gkey = f"{kind}|{bcost}" if mode == "skill_tier" else kind
                    groups[gkey].append((c, a))

            # Build raw stats for every group
            pre_rows: List[Dict[str, Any]] = []
            vpm_pool: List[float] = []
            upcm_pool: List[float] = []

            for gkey, pairs in groups.items():
                if mode == "skill_tier":
                    kind, bcost_s = gkey.split("|", 1)
                    cur_cost = int(bcost_s)
                    matched_keys = [gkey]
                else:
                    kind = gkey
                    costs_all = [int(a.get("cost", 1)) for _, a in pairs]
                    cur_cost = int(round(sum(costs_all) / max(1, len(costs_all))))
                    matched_keys = [sk for sk in sp_map.keys() if sk.startswith(kind + "|")]

                ar_nm, ar_desc = ACTIVE_AR.get(kind, (kind, ""))
                cards_unique = list({c.id: c for c, _ in pairs}.values())
                n_cards = len(cards_unique)

                if has_live_sp:
                    acc = [0] * 10
                    for mk in matched_keys:
                        v = sp_map.get(mk)
                        if isinstance(v, list):
                            for i in range(min(10, len(v))):
                                acc[i] += int(v[i])
                    uses, mana_sum, d_mon, d_face, d_hb, kills, disr, util, sides, wins = acc
                    avg_eff_cost = (mana_sum / uses) if uses > 0 else float(cur_cost)
                    wr = (100.0 * wins / sides) if sides >= 10 else None
                    upm = uses / tot_matches
                    upcm = upm / max(1, n_cards)
                    if uses > 0:
                        dm_pu = d_mon / uses
                        df_pu = d_face / uses
                        hb_pu = d_hb / uses
                        kl_pu = kills / uses
                        ds_pu = disr / uses
                        ut_pu = util / uses
                        imp = compute_skill_impact_value(dm_pu, df_pu, hb_pu, kl_pu, ds_pu, ut_pu)
                        vpm = imp / max(0.5, avg_eff_cost if avg_eff_cost > 0 else float(cur_cost))
                        parts = []
                        if dm_pu >= 0.5:
                            parts.append(f"وحش:{dm_pu:.1f}")
                        if df_pu >= 0.5:
                            parts.append(f"قائد:{df_pu:.1f}")
                        if hb_pu >= 0.5:
                            parts.append(f"شفاء/بف:{hb_pu:.1f}")
                        if kl_pu >= 0.05:
                            parts.append(f"قتل:{kl_pu:.2f}")
                        if ds_pu >= 0.1:
                            parts.append(f"تعطيل:{ds_pu:.1f}")
                        if not parts and ut_pu >= 0.1:
                            parts.append(f"فائدة:{ut_pu:.1f}")
                        imp_str = " | ".join(parts[:3]) if parts else f"قيمة {imp:.1f}"
                    else:
                        dm_pu = df_pu = hb_pu = kl_pu = ds_pu = ut_pu = 0.0
                        imp = 0.0
                        vpm = None
                        imp_str = "0 تفعيل"
                else:
                    # Automatic fallback from card_stats.json for older logs predating live skill_pricing
                    tot_pl = tot_w = 0
                    for c in cards_unique:
                        st = self.store.card_stats.get(str(c.id), {})
                        tot_pl += int(st.get("played", 0))
                        tot_w += int(st.get("played_wins", 0))
                    uses = tot_pl
                    wr = (100.0 * tot_w / tot_pl) if tot_pl >= 15 else None
                    upm = tot_pl / tot_matches if int(ex.get("total_matches", 0)) > 0 else 0.0
                    upcm = upm / max(1, n_cards)
                    # Estimate nominal power per mana from static opcode numbers
                    vals = []
                    for _, a in pairs:
                        v = float(a.get("v1", 0) or a.get("atk", 0) or a.get("deff", 0) or a.get("hp", 0) or 0)
                        if kind in ("aoe_enemy", "aoe_ally"):
                            v *= 2.8
                        elif kind == "cleave_enemy":
                            v *= 1.9
                        elif kind in ("discard_hand_2aether", "mirror_enemy_exact", "mind_control_temp", "revive_board", "betray_commander"):
                            v = max(v, 22.0)
                        elif kind == "dmg_face_aether":
                            v = max(v, 18.0)
                        vals.append(v)
                    est_imp = sum(vals) / max(1, len(vals))
                    vpm = (est_imp / max(1.0, float(cur_cost))) if est_imp > 0 else None
                    df_pu = hb_pu = kl_pu = 0.0
                    imp_str = f"تقديري (لعب الكروت: {tot_pl:,})"

                if uses >= 30:
                    upcm_pool.append(upcm)
                    if vpm is not None and vpm > 0:
                        vpm_pool.append(vpm)

                pre_rows.append({
                    "key": gkey,
                    "kind": kind,
                    "ar_name": ar_nm,
                    "ar_desc": ar_desc,
                    "skill_label": f"{ar_nm} ({cur_cost} مانا)" if mode == "skill_tier" else f"{ar_nm} [{kind}]",
                    "short_label": f"{ar_nm[:18]} ({cur_cost}م)" if mode == "skill_tier" else ar_nm[:20],
                    "cards": cards_unique,
                    "card_count": n_cards,
                    "cur_cost": cur_cost,
                    "uses": uses,
                    "upm": upm,
                    "upcm": upcm,
                    "wr": wr,
                    "vpm": vpm,
                    "impact_str": imp_str,
                    "kills_pu": kl_pu,
                    "dmg_face_pu": df_pu,
                    "heal_buff_pu": hb_pu,
                })

            med_vpm = statistics.median(vpm_pool) if vpm_pool else None
            med_upcm = statistics.median(upcm_pool) if upcm_pool else 0.05

            for pr in pre_rows:
                verdict, tag, sug, reason = recommend_mana_pricing(
                    is_spell=False,
                    cur_cost=pr["cur_cost"],
                    wr=pr["wr"],
                    uses=pr["uses"],
                    tot_matches=raw_tot_matches,
                    uses_per_match=pr["upm"],
                    uses_per_card_pm=pr["upcm"],
                    median_upcm=med_upcm,
                    vpm=pr["vpm"],
                    median_vpm=med_vpm,
                    kills_pu=pr["kills_pu"],
                    dmg_face_pu=pr["dmg_face_pu"],
                    heal_buff_pu=pr["heal_buff_pu"],
                    card_count=pr["card_count"],
                    ar_name=pr["ar_name"],
                )
                vpm_v = pr["vpm"]
                if vpm_v is not None and med_vpm and med_vpm > 0.1:
                    vpm_str = f"{vpm_v:.1f} ({vpm_v / med_vpm:.1f}×)"
                elif vpm_v is not None:
                    vpm_str = f"{vpm_v:.1f}"
                else:
                    vpm_str = "--"
                pr["sug_cost"] = sug
                pr["verdict"] = verdict
                pr["tag"] = tag
                pr["reason"] = reason
                pr["vpm_str"] = vpm_str
                pr["live_telemetry"] = has_live_sp
                rows.append(pr)

        # Sort by priority: raise2 -> raise1 -> drop -> buff -> ideal -> nodata, then by WR desc
        prio = {"raise2": 0, "raise1": 1, "drop": 2, "buff": 3, "ideal": 4, "nodata": 5}
        rows.sort(key=lambda r: (prio.get(r["tag"], 9), -(r["wr"] or 0.0), -r["uses"]))
        self._all_rows = rows

        # Aggregate KPI counts & Pricing Fairness Score (0..100)
        c_r2 = sum(1 for r in rows if r["tag"] == "raise2")
        c_r1 = sum(1 for r in rows if r["tag"] == "raise1")
        c_ok = sum(1 for r in rows if r["tag"] == "ideal")
        c_dr = sum(1 for r in rows if r["tag"] == "drop")
        c_bf = sum(1 for r in rows if r["tag"] == "buff")
        c_nd = sum(1 for r in rows if r["tag"] == "nodata")
        evaluated = max(0, len(rows) - c_nd)
        if evaluated > 0:
            # Ideal = 1.0, raise1/drop/buff = 0.45, raise2 = 0.0
            fair_score = int(round(100.0 * (c_ok * 1.0 + (c_r1 + c_dr + c_bf) * 0.45) / evaluated))
        else:
            fair_score = 0

        self._last_metrics = {
            "has_data": evaluated > 0,
            "score": fair_score,
            "ideal_cnt": c_ok,
            "good_cnt": c_r1 + c_dr + c_bf,
            "bad_cnt": c_r2,
            "raise2": c_r2,
            "raise1": c_r1,
            "drop": c_dr,
            "buff": c_bf,
            "nodata": c_nd,
            "total": len(rows),
            "evaluated": evaluated,
            "has_live_sp": has_live_sp if mode != "spells" else True,
            "mode": mode,
            "tot_matches": int(ex.get("total_matches", 0)),
        }

    def _apply_filter_and_fill(self) -> None:
        flt = self._filter_var.get()
        q = self._search_var.get().strip().casefold()
        out = []
        for r in self._all_rows:
            if flt == "raise" and r["tag"] not in ("raise2", "raise1"):
                continue
            if flt == "drop" and r["tag"] not in ("drop", "buff"):
                continue
            if flt == "ideal" and r["tag"] != "ideal":
                continue
            if q:
                hay = f"{r['skill_label']} {r['reason']} {r['key']}".casefold()
                if q not in hay:
                    continue
            out.append(r)
        self._filtered_rows = out

        self.sp_tree.delete(*self.sp_tree.get_children())
        for idx, r in enumerate(out, 1):
            cur = r["cur_cost"]
            sug = r["sug_cost"]
            diff = sug - cur
            sug_str = f"{sug} مانا ({diff:+d})" if diff != 0 else f"{sug} مانا (=)"
            wr_s = f"{r['wr']:.1f}%" if r["wr"] is not None else "--"
            upm_s = f"{r['upm']:.2f}" if r["uses"] > 0 else "0.00"
            self.sp_tree.insert(
                "",
                "end",
                iid=str(idx - 1),
                values=(
                    rtl(r["reason"]),
                    rtl(r["verdict"]),
                    rtl(r["vpm_str"]),
                    rtl(r["impact_str"]),
                    wr_s,
                    upm_s,
                    rtl(sug_str),
                    rtl(f"{cur} مانا"),
                    str(r["card_count"]),
                    rtl(r["skill_label"]),
                    str(idx),
                ),
                tags=(r["tag"],),
            )

    def _on_row_selected(self, _evt: Any = None) -> None:
        sel = self.sp_tree.selection()
        if not sel:
            return
        try:
            idx = int(sel[0])
            row = self._filtered_rows[idx]
        except Exception:
            return
        self.drill_tree.delete(*self.drill_tree.get_children())
        cards: List[CardDef] = row.get("cards") or []
        self.drill_lbl.config(
            text=rtl(
                f"المهارة المحددة: {row['skill_label']}  |  الحكم: {row['verdict']}  |  عدد البطاقات الحاملة لها: {len(cards)}"
            ),
            fg=COL_GOLD,
        )
        items = []
        for c in cards:
            st = self.store.card_stats.get(str(c.id), {})
            pl = int(st.get("played", 0))
            wr = (100.0 * int(st.get("played_wins", 0)) / pl) if pl >= 10 else None
            scost_list = [int(a.get("cost", 0)) for a in (c.actives or [])] if c.type == "monster" else [int(c.mana)]
            scost_s = "/".join(str(x) for x in scost_list) + " م" if scost_list else "--"
            stats_s = f"{c.atk} هـ / {c.deff} د" if c.type == "monster" else "سحر"
            items.append((wr or -1.0, pl, c, scost_s, stats_s))
        items.sort(key=lambda x: (x[0], x[1]), reverse=True)
        for wr, pl, c, scost_s, stats_s in items:
            wr_s = f"{wr:.1f}%" if wr >= 0 else "--"
            self.drill_tree.insert(
                "",
                "end",
                values=(
                    rtl(explain_card_arabic(c)[:135]),
                    wr_s,
                    f"{pl:,}",
                    rtl(stats_s),
                    rtl(scost_s),
                    rtl(f"{c.mana} مانا"),
                    c.name,
                    str(c.id),
                ),
            )

    def _draw_hero_banner(self) -> None:
        cv = self.cv_hero
        cv.delete("all")
        w = max(780, cv.winfo_width())
        h = max(108, cv.winfo_height())
        m = self._last_metrics
        if not m:
            return

        sc = int(m.get("score", 0))
        has_data = bool(m.get("has_data"))
        sc_col = COL_GREEN if sc >= 78 else (COL_GOLD if sc >= 58 else COL_RED)
        if not has_data:
            sc_col = FG_MUTED

        # Right side: Big Pricing Health Score Box
        bx2 = w - 8
        bx1 = w - 210
        cv.create_rectangle(bx1, 8, bx2, h - 8, fill="#141A24", outline=sc_col, width=2)
        cv.create_text((bx1 + bx2) / 2, 26, text=rtl("عدالة تسعير المانا"), fill=FG_MUTED, font=(FONT_UI, 9, "bold"))
        cv.create_text((bx1 + bx2) / 2, 62, text=f"{sc} / 100" if has_data else "--", fill=sc_col, font=(FONT_UI, 20, "bold"))
        mode_lbl = {
            "skill_tier": "مهارات الوحوش × التكلفة",
            "skill_kind": "38 مهارة مجمّعة",
            "spells": "300 بطاقة سحر",
        }.get(m.get("mode", "skill_tier"), "")
        cv.create_text((bx1 + bx2) / 2, h - 18, text=rtl(mode_lbl), fill=FG_TEXT, font=(FONT_UI, 8))

        # Status Subtitle (Live per-skill telemetry vs fallback)
        if m.get("has_live_sp"):
            src_txt = f"🟢 قياس مباشر لكل مهارة (من {m.get('tot_matches', 0):,} مباراة — الضرر والقتل والشفاء والتكرار بدقة 100%)"
            src_col = "#4ADE80"
        else:
            src_txt = f"🟡 وضع التقدير الأولي من بيانات الـ {m.get('tot_matches', 0):,} مباراة السابقة — سيتحول تلقائياً للقياس المباشر الدقيق مع أول تشغيل على القانون الجديد!"
            src_col = COL_GOLD
        cv.create_text(bx1 - 14, 18, text=rtl(src_txt), fill=src_col, font=(FONT_UI, 8, "bold"), anchor="e")

        # 5 Verdict KPI cards across remaining width
        kpis = [
            ("🔴 ارفع (+2 مانا)", m.get("raise2", 0), COL_RED),
            ("🟠 ارفع (+1 مانا)", m.get("raise1", 0), COL_ORANGE),
            ("🟢 تسعير مثالي", m.get("ideal_cnt", 0), COL_GREEN),
            ("🔵 خفّض (-1 مانا)", m.get("drop", 0), COL_BLUE),
            ("🟣 قوّي الأثر", m.get("buff", 0), COL_PURPLE),
        ]
        left_w = max(460, bx1 - 24)
        gap = 8
        cw = (left_w - gap * (len(kpis) - 1)) / len(kpis)
        for idx, (lbl, cnt, col) in enumerate(kpis):
            # Place right-to-left
            x2 = bx1 - 14 - idx * (cw + gap)
            x1 = x2 - cw
            cv.create_rectangle(x1, 34, x2, h - 10, fill=BG_PANEL, outline=col, width=1)
            cv.create_text((x1 + x2) / 2, 52, text=rtl(lbl), fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_text((x1 + x2) / 2, 80, text=f"{cnt}", fill=col, font=(FONT_UI, 16, "bold"))

    def _draw_vpm_chart(self) -> None:
        cv = self.cv_vpm
        cv.delete("all")
        w = max(420, cv.winfo_width())
        h = max(210, cv.winfo_height())
        valid = [r for r in self._all_rows if r.get("vpm") is not None and r["uses"] >= 20]
        if not valid:
            # In spell mode (no VPM), draw Top 8 highest WR vs Lowest WR spells
            valid_wr = [r for r in self._all_rows if r.get("wr") is not None and r["uses"] >= 25]
            if not valid_wr:
                cv.create_text(w / 2, h / 2, text=rtl("بانتظار بيانات كافية للرسم البياني..."), fill=FG_MUTED, font=(FONT_UI, 9))
                return
            valid_wr.sort(key=lambda r: r["wr"], reverse=True)
            pick = valid_wr[:5] + list(reversed(valid_wr[-4:]))
            row_h = (h - 30) / max(1, len(pick))
            mid_x = 135
            bar_w = max(60, w - 265)
            cv.create_line(mid_x + bar_w * 0.5, 10, mid_x + bar_w * 0.5, h - 18, fill="#475569", dash=(3, 3))
            for i, r in enumerate(pick):
                y = 14 + i * row_h
                wr = r["wr"]
                col = COL_RED if wr >= 55 else (COL_BLUE if wr <= 45 else COL_GREEN)
                x_end = mid_x + bar_w * max(0.05, min(1.0, (wr - 25.0) / 50.0))
                cv.create_rectangle(mid_x, y + 3, x_end, y + row_h - 4, fill=col, outline="")
                cv.create_text(w - 6, y + row_h / 2, text=rtl(r["short_label"]), fill=FG_TEXT, font=(FONT_UI, 8), anchor="e")
                cv.create_text(mid_x - 6, y + row_h / 2, text=f"{wr:.1f}% ({r['cur_cost']}م)", fill=col, font=(FONT_UI, 8, "bold"), anchor="e")
            return

        valid.sort(key=lambda r: r["vpm"], reverse=True)
        pick = valid[:5] + (valid[-4:] if len(valid) >= 9 else valid[5:])
        max_v = max(r["vpm"] for r in pick) or 1.0
        med_v = statistics.median(r["vpm"] for r in valid)
        row_h = (h - 32) / max(1, len(pick))
        x_left = 78
        x_right = w - 165
        bw = max(80, x_right - x_left)
        med_x = x_left + bw * min(1.0, med_v / max_v)
        cv.create_line(med_x, 12, med_x, h - 18, fill=COL_GOLD, dash=(3, 3))
        cv.create_text(med_x, h - 9, text=rtl(f"المتوسط {med_v:.1f}"), fill=COL_GOLD, font=(FONT_UI, 7, "bold"))

        tag_col = {"raise2": COL_RED, "raise1": COL_ORANGE, "ideal": COL_GREEN, "drop": COL_BLUE, "buff": COL_PURPLE}
        for i, r in enumerate(pick):
            y = 14 + i * row_h
            col = tag_col.get(r["tag"], COL_CYAN)
            bx = x_left + bw * min(1.0, r["vpm"] / max_v)
            cv.create_rectangle(x_left, y + 3, max(x_left + 4, bx), y + row_h - 4, fill=col, outline="")
            cv.create_text(w - 6, y + row_h / 2, text=rtl(r["short_label"]), fill=FG_TEXT, font=(FONT_UI, 8), anchor="e")
            cv.create_text(x_left - 6, y + row_h / 2, text=f"{r['vpm']:.1f}", fill=col, font=(FONT_UI, 8, "bold"), anchor="e")

    def _draw_spam_chart(self) -> None:
        cv = self.cv_spam
        cv.delete("all")
        w = max(420, cv.winfo_width())
        h = max(210, cv.winfo_height())
        valid = [r for r in self._all_rows if r["uses"] > 0]
        if not valid:
            cv.create_text(w / 2, h / 2, text=rtl("بانتظار بيانات تدريب لعرض تكرار المهارات..."), fill=FG_MUTED, font=(FONT_UI, 9))
            return
        valid.sort(key=lambda r: r["upm"], reverse=True)
        top = valid[:9]
        max_u = max(r["upm"] for r in top) or 1.0
        row_h = (h - 24) / max(1, len(top))
        x_left = 96
        x_right = w - 165
        bw = max(80, x_right - x_left)
        tag_col = {"raise2": COL_RED, "raise1": COL_ORANGE, "ideal": COL_GREEN, "drop": COL_BLUE, "buff": COL_PURPLE, "nodata": FG_MUTED}
        for i, r in enumerate(top):
            y = 12 + i * row_h
            col = tag_col.get(r["tag"], COL_CYAN)
            bx = x_left + bw * min(1.0, r["upm"] / max_u)
            cv.create_rectangle(x_left, y + 3, max(x_left + 4, bx), y + row_h - 4, fill=col, outline="")
            cv.create_text(w - 6, y + row_h / 2, text=rtl(r["short_label"]), fill=FG_TEXT, font=(FONT_UI, 8), anchor="e")
            wr_s = f"{r['wr']:.0f}%" if r["wr"] is not None else "--"
            cv.create_text(x_left - 6, y + row_h / 2, text=f"{r['upm']:.2f} | {wr_s}", fill=col, font=(FONT_UI, 8, "bold"), anchor="e")

# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Tab #15: Passives, Active Abilities & Spell Cards Balance Evaluator (`abilities_eval_tab.py`).
Evaluates all 70 Passives, 25 Active Skills, 45 Spell Effect Types, and all 260 Individual Spell Cards:
  - Computes real Win Rate % (when played & when drafted), Total Plays, 41-Deck Inclusion %,
    and Average / Min..Max Damage or Effect Value & Mana Cost directly from training data & card DB.
  - Automatically flags Overpowered (🔴 يحتاج إضعاف), Balanced (🟢 متوازن), and Weak (🔵 يحتاج تقوية)
    abilities/spells and generates a concrete Arabic numerical balance recommendation for each!
  - Visual Chart 1: Top 7 Highest WR% (Nerf Watch) vs Bottom 7 Lowest WR% (Buff Watch) around 50% line.
  - Visual Chart 2: Top 8 Most Used in 41 Final Decks & Matches + Category Balance Health Breakdown.
  - Interactive Drill-Down Inspector: Clicking any Passive/Active/Spell lists the exact cards carrying it
    and their individual Win Rates!
"""

from __future__ import annotations

import json
import os
import tkinter as tk
from collections import Counter
from tkinter import ttk
from typing import Any, Callable, Dict, List, Optional, Tuple

from engine.card_model import CardDB, CardDef
from engine.roster_40 import ROSTER_40
from trainer.stats_store import StatsStore
from pace_tab import rtl
from arabic_glossary import (
    ACTIVE_AR,
    ELEM_AR,
    PASSIVE_AR,
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


def _extract_op_value(op: Dict[str, Any]) -> float:
    """Extracts the primary numerical magnitude (damage / heal / buff / pct / count) from an opcode dict."""
    for k in ("v1", "pct", "dmg", "atk", "def", "v2", "turns", "count"):
        v = op.get(k)
        if isinstance(v, (int, float)) and not isinstance(v, bool) and v != 0:
            return float(v)
    return 0.0


def build_auto_balance_recommendation(
    category_mode: str,
    ar_name: str,
    wr: float,
    played: int,
    deck_copies: int,
    avg_val: float,
    avg_cost: float,
) -> Tuple[str, str, str]:
    """
    Returns (status_badge, row_tag, arabic_recommendation_text) based on real Win Rate % and usage.
    """
    if played < 15:
        if deck_copies == 0:
            return (
                "⚪ لم يُستخدم",
                "nodata",
                f"لم يظهر في الديكات بعد (0 لعب) — إن استمر مهجوراً بعد التدريب، قوِّ قيمة [{ar_name}] بـ +20% أو خفّض تكلفة بطاقاته.",
            )
        return (
            "⚪ عيّنة صغيرة",
            "nodata",
            f"لُعب {played} مرة فقط — بانتظار مزيد من مباريات التدريب للحكم الدقيق على [{ar_name}].",
        )

    diff = wr - 50.0
    # 1. Overpowered (WR > 54.5%)
    if wr > 54.5:
        sev = "قوي جداً (OP)" if wr >= 57.5 else "قوي فوق المتوسط"
        pct_cut = min(35, max(12, int(round(diff * 2.2))))
        if avg_val >= 2.0:
            new_val = max(1, int(round(avg_val * (1.0 - pct_cut / 100.0))))
            if new_val == int(round(avg_val)):
                new_val = max(1, new_val - 1)
            val_hint = f"خفض قيمة التأثير/الضرر من متوسط {int(round(avg_val))} إلى {new_val} (-{pct_cut}%)"
        else:
            val_hint = "تقييد شرط التفعيل أو خفض إحصائيات الوحش الحامل لها بـ -15%"
        cost_hint = f" أو رفع التكلفة من {int(round(avg_cost))} إلى {int(round(avg_cost))+1} مانا" if category_mode in ("actives", "spells_kind", "spells_cards") and avg_cost < 7 else ""
        return (
            "🔴 يحتاج إضعاف",
            "op",
            f"{sev} (فوز {wr:.1f}% | +{diff:.1f}%): يُقترح {val_hint}{cost_hint}.",
        )

    # 2. Underpowered / Weak (WR < 45.5%)
    if wr < 45.5:
        sev = "ضعيف جداً" if wr <= 42.5 else "أضعف من المتوسط"
        pct_buff = min(40, max(15, int(round(abs(diff) * 2.4))))
        if avg_val >= 1.0:
            new_val = max(int(round(avg_val)) + 1, int(round(avg_val * (1.0 + pct_buff / 100.0))))
            val_hint = f"رفع قيمة التأثير/الضرر من متوسط {int(round(avg_val))} إلى {new_val} (+{pct_buff}%)"
        else:
            val_hint = "تعزيز إحصائيات (هجوم/دفاع) البطاقات الحاملة لها بـ +15%"
        cost_hint = f" أو خفض التكلفة من {int(round(avg_cost))} إلى {max(0, int(round(avg_cost))-1)} مانا" if category_mode in ("actives", "spells_kind", "spells_cards") and avg_cost >= 2 else ""
        return (
            "🔵 يحتاج تقوية",
            "weak",
            f"{sev} (فوز {wr:.1f}% | {diff:.1f}%): يُقترح {val_hint}{cost_hint}.",
        )

    # 3. Slightly warm / cool (45.5..47.0 or 53.0..54.5)
    if wr > 53.0:
        return (
            "🟡 قوي نسبياً",
            "good",
            f"أداء قوي ومستقر ({wr:.1f}%) — راقبه؛ إن تجاوز 55% اخفض قيمته بمقدار 1 إلى 2 نقطة فقط.",
        )
    if wr < 47.0:
        return (
            "🟡 مقبول (يميل للضعف)",
            "good",
            f"أداء مقبول ({wr:.1f}%) — يمكن منحه دفعة طفيفة (+1 أو +2 في القيمة/الدفاع) لزيادة جاذبيته.",
        )

    # 4. Ideal Balanced Band (47.0% .. 53.0%)
    return (
        "🟢 متوازن مثالي",
        "ideal",
        f"متوازن بامتياز (فوز {wr:.1f}% حول خط 50%) — القيمة الحالية مثالية ولا تحتاج أي تعديل.",
    )


class AbilitiesAndSpellsEvalTab:
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

        self.mode_var = tk.StringVar(value="passives")  # "passives" | "actives" | "spells_kind" | "spells_cards"
        self.search_var = tk.StringVar(value="")
        self.hero_title_var = tk.StringVar(value="")
        self.hero_sub_var = tk.StringVar(value="")
        self.drill_var = tk.StringVar(
            value=rtl("اضغط على أي قدرة سلبية أو مهارة أو سحر في الجدول لعرض كل البطاقات التي تحملها وأرقامها التفصيلية هنا...")
        )

        self._mode_buttons: Dict[str, tk.Button] = {}
        self._evaluated_items: List[Dict[str, Any]] = []
        self._item_by_iid: Dict[str, Dict[str, Any]] = {}

        self._build_layout()

    def _build_layout(self) -> None:
        # Top Mode Bar & Search
        ctrl = tk.Frame(self.parent, bg=BG_PANEL, padx=12, pady=7)
        ctrl.pack(fill="x", padx=8, pady=(6, 3))

        tk.Label(
            ctrl, text=rtl("اختر قسم التقييم:"), bg=BG_PANEL, fg=COL_GOLD, font=(FONT_UI, 9, "bold"),
        ).pack(side="right", padx=(4, 4))

        modes = [
            ("passives", "1. القدرات السلبية (70 Passive)"),
            ("actives", "2. المهارات الفعّالة (25 Active)"),
            ("spells_kind", "3. أنواع تأثيرات السحر (45 نوع)"),
            ("spells_cards", "4. بطاقات السحر الفردية (300 بطاقة)"),
        ]
        for key, lbl in modes:
            btn = tk.Button(
                ctrl,
                text=rtl(lbl),
                command=lambda k=key: self._set_mode(k),
                bg=COL_BLUE if key == "passives" else BG_CARD,
                fg="#FFFFFF" if key == "passives" else FG_MUTED,
                relief="flat",
                font=(FONT_UI, 8, "bold"),
                padx=9,
                pady=4,
                cursor="hand2",
            )
            btn.pack(side="right", padx=2)
            self._mode_buttons[key] = btn

        tk.Label(
            ctrl, text=rtl("بحث سريع:"), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 8, "bold"),
        ).pack(side="right", padx=(10, 3))

        ent = tk.Entry(
            ctrl, textvariable=self.search_var, bg=BG_CARD, fg=FG_TEXT,
            insertbackground="#FFFFFF", justify="right", width=22, font=(FONT_UI, 9), relief="flat",
        )
        ent.pack(side="right", padx=3, ipady=2)
        self.search_var.trace_add("write", lambda *_a: self._populate_tree_and_charts())

        tk.Button(
            ctrl,
            text=rtl("نسخ مقترحات التعديل لـ ChatGPT"),
            command=self._copy_recommendations_to_clipboard,
            bg=COL_GREEN,
            fg="#FFFFFF",
            relief="flat",
            font=(FONT_UI, 8, "bold"),
            padx=10,
            pady=4,
            cursor="hand2",
        ).pack(side="left", padx=(0, 6))

        # Main Scrollable Container
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
        self.scroll_canvas.bind("<Configure>", lambda e: self.scroll_canvas.itemconfigure(self._inner_win, width=max(1020, e.width)))

        # Hero Summary Banner
        hero = tk.Frame(self.inner, bg=BG_CARD, highlightbackground=BG_BORDER, highlightthickness=1, padx=14, pady=8)
        hero.pack(fill="x", pady=(2, 6))
        tk.Label(
            hero, textvariable=self.hero_title_var, bg=BG_CARD, fg=FG_TEXT,
            font=(FONT_UI, 12, "bold"), anchor="e", justify="right",
        ).pack(fill="x")
        tk.Label(
            hero, textvariable=self.hero_sub_var, bg=BG_CARD, fg=COL_CYAN,
            font=(FONT_UI, 9, "bold"), anchor="e", justify="right",
        ).pack(fill="x", pady=(2, 0))

        # ==================================================================
        # Visual Charts Row (Right = Extremes around 50% WR | Left = Most Used & Health Breakdown)
        # ==================================================================
        charts = tk.Frame(self.inner, bg=BG_DARK)
        charts.pack(fill="x", pady=(0, 8))
        charts.grid_columnconfigure(0, weight=46, uniform="ab_ch")
        charts.grid_columnconfigure(1, weight=54, uniform="ab_ch")

        p_use = self._make_panel(
            charts, "الأكثر اعتماداً في الديكات النهائية الـ 41 والمباريات + ملخص توازن القسم", COL_CYAN
        )
        p_use.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        self.cv_usage = tk.Canvas(p_use, bg=BG_PANEL, height=230, highlightthickness=0)
        self.cv_usage.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_usage.bind("<Configure>", lambda _e: self._draw_usage_chart())

        p_wr = self._make_panel(
            charts, "أعلى نسبة فوز (مرشح إضعاف 🔴) مقابل أدنى نسبة فوز (مرشح تقوية 🔵) حول خط 50%", COL_GOLD
        )
        p_wr.grid(row=0, column=1, sticky="nsew", padx=(5, 0))
        self.cv_wr = tk.Canvas(p_wr, bg=BG_PANEL, height=230, highlightthickness=0)
        self.cv_wr.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_wr.bind("<Configure>", lambda _e: self._draw_wr_extremes_chart())

        # ==================================================================
        # Main Sortable Evaluation Table
        # ==================================================================
        p_tbl = self._make_panel(
            self.inner,
            "جدول التقييم الشامل (الاسم + الشرح العربي + القيمة/الضرر + مرات اللعب + نسبة الفوز + اقتراح التعديل التلقائي)",
            COL_BLUE,
        )
        p_tbl.pack(fill="both", expand=True, pady=(0, 6))

        t_fr = tk.Frame(p_tbl, bg=BG_PANEL)
        t_fr.pack(fill="both", expand=True, padx=6, pady=(2, 4))

        cols = [
            ("rec", "كيف تعدّلها؟ (اقتراح التوازن التلقائي بالأرقام)", 320),
            ("status", "التقييم", 96),
            ("wr", "نسبة الفوز %", 82),
            ("decks", "بالديكات الـ41", 82),
            ("played", "مرات اللعب", 78),
            ("power", "القيمة/الضرر والتكلفة", 132),
            ("cards", "البطاقات", 64),
            ("desc", "الشرح العربي الواضح للقدرة / السحر", 265),
            ("name", "الاسم العربي (والرمز)", 180),
            ("num", "#", 36),
        ]
        self.eval_tree = ttk.Treeview(t_fr, columns=tuple(c for c, _, _ in cols), show="headings", height=14)
        for cid, txt, w in cols:
            self.eval_tree.heading(cid, text=rtl(txt))
            self.eval_tree.column(cid, width=w, anchor="e" if cid in ("rec", "desc", "name") else "center")

        self.eval_tree.tag_configure("op", foreground="#F87171", background="#2A161B")
        self.eval_tree.tag_configure("weak", foreground="#38BDF8", background="#132332")
        self.eval_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.eval_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.eval_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        vsb_t = ttk.Scrollbar(t_fr, orient="vertical", command=self.eval_tree.yview)
        self.eval_tree.configure(yscrollcommand=vsb_t.set)
        vsb_t.pack(side="left", fill="y")
        self.eval_tree.pack(side="right", fill="both", expand=True)
        self.eval_tree.bind("<<TreeviewSelect>>", self._on_row_selected)
        enable_treeview_sorting(self.eval_tree)

        # Drill-down card inspector box at bottom
        tk.Label(
            p_tbl,
            textvariable=self.drill_var,
            bg=BG_CARD,
            fg=COL_GOLD,
            font=(FONT_UI, 9, "bold"),
            anchor="e",
            justify="right",
            wraplength=1120,
            padx=12,
            pady=8,
        ).pack(fill="x", padx=6, pady=(0, 6))

    def _make_panel(self, parent: tk.Widget, title: str, accent: str) -> tk.Frame:
        f = tk.Frame(parent, bg=BG_PANEL, highlightbackground=BG_BORDER, highlightthickness=1)
        hdr = tk.Frame(f, bg=BG_PANEL, padx=10, pady=6)
        hdr.pack(fill="x")
        tk.Frame(hdr, bg=accent, width=4, height=15).pack(side="right", padx=(7, 0))
        tk.Label(
            hdr, text=rtl(title), bg=BG_PANEL, fg=FG_TEXT,
            font=(FONT_UI, 9, "bold"), anchor="e", justify="right",
        ).pack(side="right")
        return f

    def _set_mode(self, mode: str) -> None:
        self.mode_var.set(mode)
        for k, b in self._mode_buttons.items():
            b.config(bg=COL_BLUE if k == mode else BG_CARD, fg="#FFFFFF" if k == mode else FG_MUTED)
        self._compute_mode_items()
        self._populate_tree_and_charts()

    def refresh(self) -> None:
        self._compute_mode_items()
        self._populate_tree_and_charts()

    # ------------------------------------------------------------------
    # Core Aggregation Engine across 70 Passives / 25 Actives / 45 Spells / 260 Spell Cards
    # ------------------------------------------------------------------
    def _load_settled_deck_card_counts(self) -> Counter:
        saved_dir = getattr(
            self.store,
            "saved_models_dir",
            os.path.abspath(os.path.join(self.store.logs_dir, "..", "saved_models")),
        )
        deck_card_cnt = Counter()
        for spec in ROSTER_40:
            mid = spec.model_id
            cids: List[int] = []
            dp = os.path.join(saved_dir, mid, "settled_deck.json")
            if os.path.isfile(dp):
                try:
                    with open(dp, "r", encoding="utf-8") as f:
                        cids = [int(x) for x in (json.load(f).get("deck_50_ids") or [])]
                except Exception:
                    cids = []
            if len(cids) != 50:
                rd = self.store.roster.get(mid, {}).get("settled_deck") or self.store.free10_decks.get(mid) or []
                if len(rd) == 50:
                    cids = [int(x) for x in rd]
            for cid in cids:
                deck_card_cnt[cid] += 1
        return deck_card_cnt

    def _compute_mode_items(self) -> None:
        mode = self.mode_var.get()
        deck_cnts = self._load_settled_deck_card_counts()
        cstats = self.store.card_stats

        items: List[Dict[str, Any]] = []

        if mode in ("passives", "actives", "spells_kind"):
            if mode == "passives":
                ref_map = PASSIVE_AR
            elif mode == "actives":
                ref_map = ACTIVE_AR
            else:
                ref_map = SPELL_AR

            buckets: Dict[str, Dict[str, Any]] = {
                k: {
                    "code": k,
                    "ar_name": nm,
                    "desc": desc,
                    "cids": [],
                    "vals": [],
                    "costs": [],
                    "played": 0,
                    "played_wins": 0,
                    "drafted": 0,
                    "draft_wins": 0,
                    "deck_copies": 0,
                }
                for k, (nm, desc) in ref_map.items()
            }

            for cid, c in self.db.cards.items():
                st = cstats.get(str(cid), {})
                pl = int(st.get("played", 0))
                pw = int(st.get("played_wins", 0))
                dr = int(st.get("drafted", 0))
                dw = int(st.get("wins", 0))
                dc = int(deck_cnts.get(cid, 0))

                if mode == "passives":
                    seen_k = set()
                    for pa in c.passives:
                        k = str(pa.get("kind", ""))
                        if k in buckets and k not in seen_k:
                            seen_k.add(k)
                            b = buckets[k]
                            b["cids"].append(cid)
                            v = _extract_op_value(pa)
                            if v > 0:
                                b["vals"].append(v)
                            b["costs"].append(float(c.mana))
                            b["played"] += pl
                            b["played_wins"] += pw
                            b["drafted"] += dr
                            b["draft_wins"] += dw
                            b["deck_copies"] += dc

                elif mode == "actives":
                    seen_k = set()
                    for ac in c.actives:
                        k = str(ac.get("kind", ""))
                        if k in buckets and k not in seen_k:
                            seen_k.add(k)
                            b = buckets[k]
                            b["cids"].append(cid)
                            v = _extract_op_value(ac)
                            if v > 0:
                                b["vals"].append(v)
                            b["costs"].append(float(ac.get("cost", 1)))
                            b["played"] += pl
                            b["played_wins"] += pw
                            b["drafted"] += dr
                            b["draft_wins"] += dw
                            b["deck_copies"] += dc

                elif mode == "spells_kind" and c.type == "spell" and c.spell_ops:
                    k = str(c.spell_ops.get("kind", ""))
                    if k in buckets:
                        b = buckets[k]
                        b["cids"].append(cid)
                        v = _extract_op_value(c.spell_ops)
                        if v > 0:
                            b["vals"].append(v)
                        b["costs"].append(float(c.mana))
                        b["played"] += pl
                        b["played_wins"] += pw
                        b["drafted"] += dr
                        b["draft_wins"] += dw
                        b["deck_copies"] += dc

            for k, b in buckets.items():
                pl = b["played"]
                dr = b["drafted"]
                wr = (100.0 * b["played_wins"] / pl) if pl > 0 else ((100.0 * b["draft_wins"] / dr) if dr > 0 else 50.0)
                avg_val = (sum(b["vals"]) / len(b["vals"])) if b["vals"] else 0.0
                min_v = int(min(b["vals"])) if b["vals"] else 0
                max_v = int(max(b["vals"])) if b["vals"] else 0
                avg_cost = (sum(b["costs"]) / len(b["costs"])) if b["costs"] else 0.0

                if avg_val > 0:
                    val_s = f"قيمة: {avg_val:.0f}" if min_v == max_v else f"قيمة: {avg_val:.1f} ({min_v}..{max_v})"
                else:
                    val_s = "تأثير مباشر"
                if mode == "actives":
                    power_str = f"{val_s} | {avg_cost:.1f}م مهارة"
                elif mode == "spells_kind":
                    power_str = f"{val_s} | {avg_cost:.1f}م سحر"
                else:
                    power_str = f"{val_s} | وحش ~{avg_cost:.1f}م"

                st_badge, tag, rec_txt = build_auto_balance_recommendation(
                    mode, b["ar_name"], wr, pl, b["deck_copies"], avg_val, avg_cost
                )
                items.append({
                    "iid": k,
                    "name": f"{b['ar_name']} ({k})",
                    "short_name": b["ar_name"],
                    "code": k,
                    "desc": b["desc"],
                    "cards_cnt": len(b["cids"]),
                    "cids": b["cids"],
                    "power_str": power_str,
                    "avg_val": avg_val,
                    "avg_cost": avg_cost,
                    "played": pl,
                    "deck_copies": b["deck_copies"],
                    "wr": wr,
                    "has_plays": pl >= 15,
                    "status": st_badge,
                    "tag": tag,
                    "rec": rec_txt,
                })

        else:
            # Mode 4: Individual 260 Spell Cards (#211 .. #470)
            for cid in sorted(self.db.spells):
                c = self.db.cards[cid]
                op = c.spell_ops or {}
                k = str(op.get("kind", "dmg_single"))
                sp_ar, sp_desc = SPELL_AR.get(k, (k, "تأثير سحري."))
                val = _extract_op_value(op)
                el_ar = ELEM_AR.get(c.element_name, "عام") if c.element_name != "None" else "عام"

                st = cstats.get(str(cid), {})
                pl = int(st.get("played", 0))
                pw = int(st.get("played_wins", 0))
                dr = int(st.get("drafted", 0))
                dw = int(st.get("wins", 0))
                dc = int(deck_cnts.get(cid, 0))
                wr = (100.0 * pw / pl) if pl > 0 else ((100.0 * dw / dr) if dr > 0 else 50.0)

                val_s = f"قوة/ضرر: {int(val)}" if val > 0 else "تكتيكي"
                power_str = f"{c.mana} مانا | {val_s}"
                full_desc = f"[{el_ar} • {sp_ar}] {sp_desc}"

                st_badge, tag, rec_txt = build_auto_balance_recommendation(
                    "spells_cards", f"{c.name} (#{cid})", wr, pl, dc, val, float(c.mana)
                )
                items.append({
                    "iid": f"spell_{cid}",
                    "name": f"#{cid} {c.name}",
                    "short_name": f"#{cid} {c.name[:16]}",
                    "code": str(cid),
                    "desc": full_desc,
                    "cards_cnt": 1,
                    "cids": [cid],
                    "power_str": power_str,
                    "avg_val": val,
                    "avg_cost": float(c.mana),
                    "played": pl,
                    "deck_copies": dc,
                    "wr": wr,
                    "has_plays": pl >= 15,
                    "status": st_badge,
                    "tag": tag,
                    "rec": rec_txt,
                })

        # Sort by Win Rate descending for tested items, then by deck_copies/cards_cnt
        items.sort(key=lambda x: (1 if x["has_plays"] else 0, x["wr"], x["deck_copies"], x["played"]), reverse=True)
        self._evaluated_items = items

    def _populate_tree_and_charts(self) -> None:
        q = self.search_var.get().strip().casefold()
        self.eval_tree.delete(*self.eval_tree.get_children())
        self._item_by_iid.clear()

        mode = self.mode_var.get()
        mode_titles = {
            "passives": "تقييم القدرات السلبية للوحوش والحواجز (70 Passive)",
            "actives": "تقييم المهارات الفعّالة للوحوش (25 Active Ability)",
            "spells_kind": "تقييم أنواع تأثيرات بطاقات السحر (45 Spell Type)",
            "spells_cards": "تقييم جميع بطاقات السحر الفردية (300 Spell Card)",
        }

        op_cnt = sum(1 for x in self._evaluated_items if x["tag"] == "op")
        weak_cnt = sum(1 for x in self._evaluated_items if x["tag"] == "weak")
        ideal_cnt = sum(1 for x in self._evaluated_items if x["tag"] in ("ideal", "good"))
        untested_cnt = sum(1 for x in self._evaluated_items if x["tag"] == "nodata")
        tot_plays = sum(x["played"] for x in self._evaluated_items)

        self.hero_title_var.set(
            rtl(
                f"{mode_titles.get(mode, '')}  —  "
                f"🟢 متوازن: {ideal_cnt}   |   🔴 قوي يحتاج إضعاف: {op_cnt}   |   "
                f"🔵 ضعيف يحتاج تقوية: {weak_cnt}   |   ⚪ بانتظار تدريب كافٍ: {untested_cnt}"
            )
        )
        self.hero_sub_var.set(
            rtl(
                f"إجمالي تفعيلات/لعب هذا القسم في التدريب: {tot_plays:,} مرة  •  "
                f"(اضغط على أي عنوان عمود للترتيب، أو اضغط على أي صف لعرض البطاقات الحاملة له بالأسفل)"
            )
        )

        shown = 0
        for idx, it in enumerate(self._evaluated_items, 1):
            if q:
                hay = f"{it['name']} {it['desc']} {it['status']} {it['rec']}".casefold()
                if q not in hay:
                    continue
            shown += 1
            iid = it["iid"]
            self._item_by_iid[iid] = it
            wr_s = f"{it['wr']:.1f}%" if it["played"] > 0 else "--"
            self.eval_tree.insert(
                "",
                "end",
                iid=iid,
                values=(
                    rtl(it["rec"]),
                    rtl(it["status"]),
                    wr_s,
                    f"{it['deck_copies']} نسخة",
                    f"{it['played']:,}",
                    rtl(it["power_str"]),
                    f"{it['cards_cnt']} بطاقة",
                    rtl(it["desc"][:110]),
                    rtl(it["name"]),
                    idx,
                ),
                tags=(it["tag"],),
            )

        self._draw_wr_extremes_chart()
        self._draw_usage_chart()

    # ------------------------------------------------------------------
    # Drill-Down Inspector: Click any Passive/Active/Spell -> See Exact Cards & Stats!
    # ------------------------------------------------------------------
    def _on_row_selected(self, _e: Any = None) -> None:
        sel = self.eval_tree.selection()
        if not sel:
            return
        it = self._item_by_iid.get(str(sel[0]))
        if not it:
            return

        cids = it["cids"]
        cstats = self.store.card_stats
        card_summaries: List[Tuple[int, float, int, str]] = []
        for cid in cids:
            c = self.db.cards.get(cid)
            if not c:
                continue
            st = cstats.get(str(cid), {})
            pl = int(st.get("played", 0))
            pw = int(st.get("played_wins", 0))
            wr = (100.0 * pw / pl) if pl > 0 else 50.0
            if c.type == "monster":
                s_txt = f"#{cid} {c.name} ({c.mana}م {c.atk}/{c.deff}" + (f" | فوز {wr:.0f}% بـ{pl}لعب)" if pl > 0 else ")")
            else:
                s_txt = f"#{cid} {c.name} ({c.mana}م" + (f" | فوز {wr:.0f}% بـ{pl}لعب)" if pl > 0 else ")")
            card_summaries.append((pl, wr, cid, s_txt))

        card_summaries.sort(key=lambda x: (x[0] > 0, x[1], x[0]), reverse=True)
        top_samples = "  ،  ".join(x[3] for x in card_summaries[:8])
        more_s = f" ... (+{len(card_summaries) - 8} بطاقة أخرى)" if len(card_summaries) > 8 else ""

        self.drill_var.set(
            rtl(
                f"🔍 [{it['name']}] — {it['desc']}\n"
                f"• التقييم والاقتراح: {it['rec']}\n"
                f"• أبرز البطاقات الحاملة لها ({len(cids)} بطاقة):  {top_samples}{more_s}"
            )
        )

    def _copy_recommendations_to_clipboard(self) -> None:
        mode = self.mode_var.get()
        lines = [
            f"# تقرير تقييم ومقترحات تعديل القدرات والسحر ({mode})",
            "",
            "| # | القدرة / البطاقة | القيمة والتكلفة | مرات اللعب | بالديكات الـ41 | الفوز % | التقييم | اقتراح التعديل التلقائي |",
            "|---:|---|---|---:|---:|:---:|:---:|---|",
        ]
        for idx, it in enumerate(self._evaluated_items, 1):
            wr_s = f"{it['wr']:.1f}%" if it["played"] > 0 else "--"
            lines.append(
                f"| {idx} | {it['name']} | {it['power_str']} | {it['played']:,} | {it['deck_copies']} | **{wr_s}** | {it['status']} | {it['rec']} |"
            )
        txt = "\n".join(lines)
        try:
            self.parent.clipboard_clear()
            self.parent.clipboard_append(txt)
            self.parent.update()
            self.drill_var.set(
                rtl("✅ تم نسخ جدول تقييم القدرات والسحر ومقترحات التعديل الكامل (Markdown) إلى الحافظة — الصقه مباشرة في ChatGPT!")
            )
        except Exception:
            pass

    # ------------------------------------------------------------------
    # Chart 1: Highest WR% (Nerf Watch) vs Lowest WR% (Buff Watch) around 50%
    # ------------------------------------------------------------------
    def _draw_wr_extremes_chart(self) -> None:
        cv = self.cv_wr
        cv.delete("all")
        w = max(380, cv.winfo_width())
        h = max(210, cv.winfo_height())

        tested = [x for x in self._evaluated_items if x["played"] >= 5]
        if not tested:
            # Show top items by catalog/deck count when training is still at 0 matches
            cv.create_text(
                w // 2, h // 2 - 10,
                text=rtl("بانتظار مباريات التدريب لرسم منحنى الفوز (50%) للقدرات والسحر."),
                fill=COL_GOLD, font=(FONT_UI, 9, "bold"),
            )
            cv.create_text(
                w // 2, h // 2 + 14,
                text=rtl("الجدول بالأسفل يعرض حالياً كل الـ 70 باسف والـ 25 مهارة والـ 260 تعويذة بقيمها وشرحها العربي وتواجدها في الديكات!"),
                fill=FG_MUTED, font=(FONT_UI, 8),
            )
            return

        by_wr = sorted(tested, key=lambda x: x["wr"], reverse=True)
        top_n = by_wr[:4]
        bot_n = list(reversed(by_wr[-4:])) if len(by_wr) > 4 else []
        chart_rows = top_n + bot_n

        lbl_x = w - 10
        bar_r = w - 155
        bar_l = 66
        bar_w = max(80, bar_r - bar_l)
        x50 = bar_l + bar_w * 0.50
        x46 = bar_l + bar_w * (46.0 - 20.0) / 60.0
        x54 = bar_l + bar_w * (54.0 - 20.0) / 60.0

        cv.create_rectangle(x46, 16, x54, h - 8, fill="#132620", outline="")
        cv.create_line(x50, 14, x50, h - 8, fill=COL_GOLD, dash=(3, 3))
        cv.create_text(x50, 7, text="50%", fill=COL_GOLD, font=(FONT_UI, 7, "bold"))
        cv.create_text(bar_l, 7, text="20%", fill=FG_MUTED, font=(FONT_UI, 7))
        cv.create_text(bar_r, 7, text="80%", fill=FG_MUTED, font=(FONT_UI, 7))

        row_h = max(21, (h - 24) // max(1, len(chart_rows)))
        for i, it in enumerate(chart_rows):
            y = 18 + i * row_h
            wr = float(it["wr"])
            col = COL_RED if wr > 54.5 else (COL_CYAN if wr < 45.5 else COL_GREEN)
            cv.create_text(lbl_x, y + row_h * 0.42, text=rtl(it["short_name"][:20]), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(bar_l, y + 3, bar_r, y + row_h - 5, fill="#1F2736", outline="")
            px = bar_l + bar_w * (max(20.0, min(80.0, wr)) - 20.0) / 60.0
            bx0, bx1 = min(x50, px), max(x50, px)
            if bx1 - bx0 < 3:
                bx0, bx1 = x50 - 2, x50 + 2
            cv.create_rectangle(bx0, y + 3, bx1, y + row_h - 5, fill=col, outline="")
            cv.create_text(bar_l - 6, y + row_h * 0.42, text=f"{wr:5.1f}%", anchor="e", fill=col, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Chart 2: Top 8 Most Included in the 41 Final Decks / Most Played
    # ------------------------------------------------------------------
    def _draw_usage_chart(self) -> None:
        cv = self.cv_usage
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(210, cv.winfo_height())

        if not self._evaluated_items:
            return

        by_use = sorted(
            self._evaluated_items,
            key=lambda x: (x["deck_copies"], x["played"], x["cards_cnt"]),
            reverse=True,
        )[:8]

        max_dc = max(1, max(x["deck_copies"] for x in by_use))
        use_plays = sum(x["played"] for x in by_use) > 0 and max(x["deck_copies"] for x in by_use) == 0

        title_s = "الأكثر تواجداً في الديكات النهائية الـ 41 (عدد النسخ):" if not use_plays else "الأكثر لعباً في المباريات:"
        cv.create_text(w - 10, 11, text=rtl(title_s), anchor="e", fill=COL_CYAN, font=(FONT_UI, 8, "bold"))

        lbl_x = w - 10
        bar_r = w - 150
        bar_l = 64
        bar_w = max(60, bar_r - bar_l)
        row_h = max(22, (h - 24) // max(1, len(by_use)))

        for i, it in enumerate(by_use):
            y = 20 + i * row_h
            val = it["played"] if use_plays else (it["deck_copies"] if max_dc > 1 else it["cards_cnt"])
            denom = max(1, max(x["played"] if use_plays else (x["deck_copies"] if max_dc > 1 else x["cards_cnt"]) for x in by_use))
            unit_s = f"{val:,} لعب" if use_plays else (f"{val} بالديكات" if max_dc > 1 else f"{val} بطاقة")
            col = COL_PURPLE if i < 3 else COL_BLUE

            cv.create_text(lbl_x, y + row_h * 0.42, text=rtl(it["short_name"][:20]), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(bar_l, y + 3, bar_r, y + row_h - 5, fill="#222B3B", outline="")
            fw = max(3, int(bar_w * (val / float(denom)))) if val > 0 else 0
            if fw > 0:
                cv.create_rectangle(bar_r - fw, y + 3, bar_r, y + row_h - 5, fill=col, outline="")
            cv.create_text(bar_l - 6, y + row_h * 0.42, text=rtl(unit_s), anchor="e", fill=COL_CYAN, font=(FONT_UI, 8, "bold"))

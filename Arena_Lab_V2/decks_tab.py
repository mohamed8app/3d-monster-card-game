# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Tab #14: Final Settled Decks Explorer & Visual Card-Type Breakdown (`decks_tab.py`).
Arabic RTL interactive viewer for all 41 AI models' 50-card decks:
  - Category filter bar + clickable 41-Model table (Arabic names, matches, WR%, ELO, quick type counts)
  - Instant Visual Chart 1 on Model Select: Card Types Breakdown (Combat Monsters, Barriers, Spells,
    Leader, Covenant, Environments) + 6 Combat Roles distribution!
  - Instant Visual Chart 2 on Model Select: Mana Cost Curve (0..7 Mana) + Top Tribes & Elements in Deck!
  - Full Sortable 50-Card Deck Table with copies (x1/x2), model-specific card WR%, stats, and full Arabic
    explanation of every card's passives, active skills, spell effects, leader aura, or covenant rule!
"""

from __future__ import annotations

import json
import os
import tkinter as tk
from collections import Counter
from tkinter import ttk
from typing import Any, Callable, Dict, List, Optional, Tuple

from engine.ai_brain import DeckDrafter
from engine.card_model import CardDB, CardDef
from engine.roster_40 import ROSTER_40, ROSTER_MAP, build_legal_card_mask
from trainer.stats_store import StatsStore
from pace_tab import rtl
from arabic_glossary import (
    CATEGORY_AR,
    ELEM_AR,
    ROLE_AR,
    TRIBE_AR,
    TYPE_AR,
    arabic_model_name,
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
COL_TEAL = "#14B8A6"

FONT_UI = "Segoe UI"


class FinalDecksTab:
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

        self.cat_filter_var = tk.StringVar(value="All")
        self.selected_model_id: str = "free_01"
        self.model_combo_var = tk.StringVar(value="")
        self.hero_title_var = tk.StringVar(value="")
        self.hero_sub_var = tk.StringVar(value="")
        self.card_detail_var = tk.StringVar(
            value=rtl("اضغط على أي بطاقة في جدول الديك لعرض شرح قدراتها ومهاراتها الكامل بالعربي هنا...")
        )

        self._cat_buttons: Dict[str, tk.Button] = {}
        self._combo_label_to_id: Dict[str, str] = {}
        self._decks_cache: Dict[str, Tuple[List[int], bool]] = {}  # model_id -> (50 card IDs, is_settled_from_training)
        self._fallback_drafter: Optional[DeckDrafter] = None
        self._current_deck_stats: Dict[str, Any] = {}
        self._suppress_tree_select = False

        self._build_layout()

    def _build_layout(self) -> None:
        # Top Filter & Model Selector Bar
        ctrl = tk.Frame(self.parent, bg=BG_PANEL, padx=12, pady=7)
        ctrl.pack(fill="x", padx=8, pady=(6, 3))

        tk.Label(
            ctrl, text=rtl("تصفية النماذج:"), bg=BG_PANEL, fg=COL_GOLD, font=(FONT_UI, 9, "bold"),
        ).pack(side="right", padx=(4, 4))

        cats = [
            ("All", "الكل (41)"),
            ("Free", "الأحرار (10)"),
            ("Playstyle", "الأساليب (10)"),
            ("Tribe", "العشائر (10)"),
            ("Environment", "العناصر (10)"),
            ("Anomaly", "صائد الثغرات (1)"),
        ]
        for key, lbl in cats:
            btn = tk.Button(
                ctrl,
                text=rtl(lbl),
                command=lambda k=key: self._set_category_filter(k),
                bg=COL_BLUE if key == "All" else BG_CARD,
                fg="#FFFFFF" if key == "All" else FG_MUTED,
                relief="flat",
                font=(FONT_UI, 8, "bold"),
                padx=8,
                pady=3,
                cursor="hand2",
            )
            btn.pack(side="right", padx=2)
            self._cat_buttons[key] = btn

        tk.Label(
            ctrl, text=rtl("اختر النموذج:"), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold"),
        ).pack(side="right", padx=(12, 4))

        self.model_cb = ttk.Combobox(
            ctrl, textvariable=self.model_combo_var, state="readonly", width=34, font=(FONT_UI, 9),
        )
        self.model_cb.pack(side="right", padx=4)
        self.model_cb.bind("<<ComboboxSelected>>", self._on_combo_selected)

        tk.Button(
            ctrl,
            text=rtl("نسخ بطاقات الديك للحافظة"),
            command=self._copy_current_deck_to_clipboard,
            bg=COL_GREEN,
            fg="#FFFFFF",
            relief="flat",
            font=(FONT_UI, 8, "bold"),
            padx=10,
            pady=4,
            cursor="hand2",
        ).pack(side="left", padx=(0, 6))

        # Main Scrollable Area
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

        # Hero Summary Strip for Selected Model
        hero = tk.Frame(self.inner, bg=BG_CARD, highlightbackground=BG_BORDER, highlightthickness=1, padx=14, pady=8)
        hero.pack(fill="x", pady=(2, 8))
        tk.Label(
            hero, textvariable=self.hero_title_var, bg=BG_CARD, fg=FG_TEXT,
            font=(FONT_UI, 12, "bold"), anchor="e", justify="right",
        ).pack(fill="x")
        tk.Label(
            hero, textvariable=self.hero_sub_var, bg=BG_CARD, fg=COL_CYAN,
            font=(FONT_UI, 9, "bold"), anchor="e", justify="right",
        ).pack(fill="x", pady=(2, 0))

        # ==================================================================
        # Table 1 (Full Width): All 41 Models Table (Click to inspect any model's deck!)
        # ==================================================================
        p_models = self._make_panel(
            self.inner,
            "قائمة النماذج الـ 41 (اضغط على أي نموذج لعرض ديكه النهائي ورسمه البياني بالأسفل)",
            COL_BLUE,
        )
        p_models.pack(fill="x", pady=(0, 8))

        mod_Fr = tk.Frame(p_models, bg=BG_PANEL)
        mod_Fr.pack(fill="both", expand=True, padx=8, pady=(2, 8))

        mod_cols = [
            ("state", "حالة الديك", 110),
            ("mix", "وحش/حاجز/سحر/خاص", 165),
            ("wr", "الفوز %", 85),
            ("matches", "المباريات", 90),
            ("model", "اسم النموذج (عربي)", 320),
            ("num", "#", 40),
        ]
        self.models_tree = ttk.Treeview(mod_Fr, columns=tuple(c for c, _, _ in mod_cols), show="headings", height=10)
        for cid, txt, w in mod_cols:
            self.models_tree.heading(cid, text=rtl(txt))
            self.models_tree.column(cid, width=w, minwidth=w, stretch=False, anchor="e" if cid == "model" else "center")

        self.models_tree.tag_configure("settled", foreground="#4ADE80", background="#142620")
        self.models_tree.tag_configure("initial", foreground="#FACC15", background=BG_PANEL)

        m_vsb = ttk.Scrollbar(mod_Fr, orient="vertical", command=self.models_tree.yview)
        m_hsb = ttk.Scrollbar(mod_Fr, orient="horizontal", command=self.models_tree.xview)
        self.models_tree.configure(yscrollcommand=m_vsb.set, xscrollcommand=m_hsb.set)
        self.models_tree._has_hsb = True
        self.models_tree._hsb_widget = m_hsb

        m_hsb.pack(side="bottom", fill="x")
        m_vsb.pack(side="left", fill="y")
        self.models_tree.pack(side="right", fill="both", expand=True)
        self.models_tree.bind("<<TreeviewSelect>>", self._on_model_tree_selected)
        enable_treeview_sorting(self.models_tree)

        # ==================================================================
        # Table 2 (Full Width): Selected Model's 50-Card Final Deck Table
        # ==================================================================
        p_deck = self._make_panel(
            self.inner,
            "بطاقات الديك النهائي للنموذج المختار (50 بطاقة) — اضغط أي عنوان عمود للترتيب أو أي بطاقة لشرحها",
            COL_GREEN,
        )
        p_deck.pack(fill="x", pady=(0, 8))

        deck_Fr = tk.Frame(p_deck, bg=BG_PANEL)
        deck_Fr.pack(fill="both", expand=True, padx=8, pady=(2, 4))

        deck_cols = [
            ("explain", "الشرح العربي المختصر للقدرات", 340),
            ("wr", "فوز البطاقة", 85),
            ("played", "لُعبت", 68),
            ("stats", "مانا | هـ/د", 100),
            ("role", "العنصر / العشيرة / الدور", 170),
            ("type", "نوع البطاقة", 95),
            ("qty", "النسخ", 54),
            ("name", "اسم البطاقة", 195),
            ("cid", "ID", 50),
        ]
        self.deck_tree = ttk.Treeview(deck_Fr, columns=tuple(c for c, _, _ in deck_cols), show="headings", height=16)
        for cid, txt, w in deck_cols:
            self.deck_tree.heading(cid, text=rtl(txt))
            self.deck_tree.column(cid, width=w, minwidth=w, stretch=False, anchor="e" if cid in ("explain", "name", "role") else "center")

        self.deck_tree.tag_configure("monster", foreground="#EEF2F9", background=BG_PANEL)
        self.deck_tree.tag_configure("barrier", foreground="#38BDF8", background="#132230")
        self.deck_tree.tag_configure("spell", foreground="#FACC15", background="#242014")
        self.deck_tree.tag_configure("special", foreground="#C084FC", background="#211730")

        d_vsb = ttk.Scrollbar(deck_Fr, orient="vertical", command=self.deck_tree.yview)
        d_hsb = ttk.Scrollbar(deck_Fr, orient="horizontal", command=self.deck_tree.xview)
        self.deck_tree.configure(yscrollcommand=d_vsb.set, xscrollcommand=d_hsb.set)
        self.deck_tree._has_hsb = True
        self.deck_tree._hsb_widget = d_hsb

        d_hsb.pack(side="bottom", fill="x")
        d_vsb.pack(side="left", fill="y")
        self.deck_tree.pack(side="right", fill="both", expand=True)
        self.deck_tree.bind("<<TreeviewSelect>>", self._on_deck_card_selected)
        enable_treeview_sorting(self.deck_tree)

        card_detail_lbl = tk.Label(
            p_deck,
            textvariable=self.card_detail_var,
            bg=BG_CARD,
            fg=COL_GOLD,
            font=(FONT_UI, 9, "bold"),
            anchor="e",
            justify="right",
            wraplength=1100,
            padx=12,
            pady=8,
        )
        card_detail_lbl.pack(fill="x", padx=8, pady=(0, 8))
        card_detail_lbl.bind("<Configure>", lambda e: card_detail_lbl.configure(wraplength=max(400, e.width - 28)))

        # ==================================================================
        # Visual Charts Stacked Vertically at Full Screen Width
        # ==================================================================
        p_types = self._make_panel(
            self.inner, "الشكل البياني السريع لأنواع البطاقات والأدوار القتالية في الديك (50 بطاقة)", COL_GOLD
        )
        p_types.pack(fill="x", pady=(0, 8))
        self.cv_types = tk.Canvas(p_types, bg=BG_PANEL, height=240, highlightthickness=0)
        self.cv_types.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_types.bind("<Configure>", lambda _e: self._draw_card_types_chart())

        p_curve = self._make_panel(
            self.inner, "منحنى تكلفة المانا (0 → 7 مانا) + أبرز العشائر والعناصر داخل الديك", COL_CYAN
        )
        p_curve.pack(fill="x", pady=(0, 8))
        self.cv_curve = tk.Canvas(p_curve, bg=BG_PANEL, height=240, highlightthickness=0)
        self.cv_curve.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_curve.bind("<Configure>", lambda _e: self._draw_mana_and_affinities_chart())

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

    # ------------------------------------------------------------------
    # Data Loading: Settled Deck (or Deterministic Initial Constitution Deck)
    # ------------------------------------------------------------------
    def _load_all_decks(self) -> None:
        saved_dir = getattr(
            self.store,
            "saved_models_dir",
            os.path.abspath(os.path.join(self.store.logs_dir, "..", "saved_models")),
        )
        if self._fallback_drafter is None:
            self._fallback_drafter = DeckDrafter(self.db)

        self._decks_cache.clear()
        claimed_free: set = set()

        for spec in ROSTER_40:
            mid = spec.model_id
            cids: List[int] = []
            is_settled = False

            dp = os.path.join(saved_dir, mid, "settled_deck.json")
            if os.path.isfile(dp):
                try:
                    with open(dp, "r", encoding="utf-8") as f:
                        raw_ids = json.load(f).get("deck_50_ids") or []
                    if len(raw_ids) == 50:
                        cids = [int(x) for x in raw_ids]
                        is_settled = True
                except Exception:
                    cids = []

            if len(cids) != 50:
                rd = self.store.roster.get(mid, {}).get("settled_deck") or self.store.free10_decks.get(mid) or []
                if len(rd) == 50:
                    cids = [int(x) for x in rd]
                    is_settled = int(self.store.roster.get(mid, {}).get("matches", 0)) > 0

            if len(cids) != 50:
                # Build deterministic initial 50-card Constitution deck so tab is 100% functional even at 0 matches!
                excl = claimed_free if spec.category == "Free" else None
                lmask = build_legal_card_mask(spec, self.db, free_excluded_ids=excl)
                cids = [int(x) for x in self._fallback_drafter.build_deck(lmask, spec.required_card_id, deterministic=True)]
                is_settled = False

            if spec.category == "Free":
                claimed_free.update(cids)
            self._decks_cache[mid] = (cids, is_settled)

    def _summarize_deck_types(self, cids: List[int]) -> Dict[str, Any]:
        counts_by_type = {
            "monster": 0,   # Combat monsters (ATK > 0)
            "barrier": 0,   # Defensive barriers (ATK == 0)
            "spell": 0,
            "leader": 0,
            "covenant": 0,
            "environment": 0,
        }
        roles_cnt = Counter()
        tribes_cnt = Counter()
        elems_cnt = Counter()
        mana_hist = [0] * 8  # 0..7 mana
        mana_sum = 0
        atk_sum = 0
        def_sum = 0
        leader_name = "—"
        cov_name = "—"
        env_names: List[str] = []

        for cid in cids:
            c = self.db.cards.get(cid)
            if not c:
                continue
            m_clamped = max(0, min(7, int(c.mana)))
            mana_hist[m_clamped] += 1
            mana_sum += int(c.mana)
            if c.type == "monster":
                if c.is_barrier:
                    counts_by_type["barrier"] += 1
                else:
                    counts_by_type["monster"] += 1
                    atk_sum += int(c.atk)
                def_sum += int(c.deff)
                roles_cnt[ROLE_AR.get(c.role_name, c.role_name)] += 1
                if c.tribe_name and c.tribe_name != "None":
                    tribes_cnt[TRIBE_AR.get(c.tribe_name, c.tribe_name)] += 1
                if c.element_name and c.element_name != "None":
                    elems_cnt[ELEM_AR.get(c.element_name, c.element_name)] += 1
            elif c.type == "spell":
                counts_by_type["spell"] += 1
            elif c.type == "leader":
                counts_by_type["leader"] += 1
                leader_name = c.name
            elif c.type == "covenant":
                counts_by_type["covenant"] += 1
                cov_name = c.name
            elif c.type == "environment":
                counts_by_type["environment"] += 1
                if c.name not in env_names:
                    env_names.append(c.name)

        n_tot = max(1, len(cids))
        return {
            "total": len(cids),
            "unique": len(set(cids)),
            "counts": counts_by_type,
            "roles": roles_cnt,
            "tribes": tribes_cnt.most_common(4),
            "elems": elems_cnt.most_common(4),
            "mana_hist": mana_hist,
            "avg_mana": mana_sum / float(n_tot),
            "atk_sum": atk_sum,
            "def_sum": def_sum,
            "leader_name": leader_name,
            "cov_name": cov_name,
            "env_names": env_names,
        }

    # ------------------------------------------------------------------
    # Filtering, Selection & Refresh
    # ------------------------------------------------------------------
    def _set_category_filter(self, cat: str) -> None:
        self.cat_filter_var.set(cat)
        for k, b in self._cat_buttons.items():
            b.config(bg=COL_BLUE if k == cat else BG_CARD, fg="#FFFFFF" if k == cat else FG_MUTED)
        self._populate_models_list(keep_current=False)

    def _on_combo_selected(self, _e: Any = None) -> None:
        mid = self._combo_label_to_id.get(self.model_combo_var.get())
        if mid:
            self.selected_model_id = mid
            self._sync_tree_selection_to_model(mid)
            self._render_selected_model()

    def _on_model_tree_selected(self, _e: Any = None) -> None:
        if self._suppress_tree_select:
            return
        sel = self.models_tree.selection()
        if not sel:
            return
        mid = str(sel[0])
        if mid in ROSTER_MAP and mid != self.selected_model_id:
            self.selected_model_id = mid
            for lbl, m in self._combo_label_to_id.items():
                if m == mid:
                    self.model_combo_var.set(lbl)
                    break
            self._render_selected_model()

    def _sync_tree_selection_to_model(self, mid: str) -> None:
        if mid in self.models_tree.get_children(""):
            self._suppress_tree_select = True
            try:
                self.models_tree.selection_set(mid)
                self.models_tree.see(mid)
            finally:
                self._suppress_tree_select = False

    def refresh(self) -> None:
        self._load_all_decks()
        self._populate_models_list(keep_current=True)

    def _populate_models_list(self, keep_current: bool = True) -> None:
        cat = self.cat_filter_var.get()
        self.models_tree.delete(*self.models_tree.get_children())
        self._combo_label_to_id.clear()
        combo_vals: List[str] = []
        filtered_ids: List[str] = []

        for idx, spec in enumerate(ROSTER_40, 1):
            if cat != "All" and spec.category != cat:
                continue
            mid = spec.model_id
            filtered_ids.append(mid)
            ar_name = arabic_model_name(mid)
            rec = self.store.roster.get(mid, {})
            m = int(rec.get("matches", 0))
            w = int(rec.get("wins", 0))
            wr_s = f"{100.0 * w / m:.1f}%" if m > 0 else "--"
            cids, is_settled = self._decks_cache.get(mid, ([], False))
            tsum = self._summarize_deck_types(cids)
            cnts = tsum["counts"]
            sp_cnt = cnts["leader"] + cnts["covenant"] + cnts["environment"]
            mix_s = f"{cnts['monster']}و / {cnts['barrier']}ح / {cnts['spell']}س / {sp_cnt}خ"
            st_lbl = "🟢 نهائي" if is_settled else "🟡 مبدئي"
            tag = "settled" if is_settled else "initial"

            self.models_tree.insert(
                "",
                "end",
                iid=mid,
                values=(rtl(st_lbl), rtl(mix_s), wr_s, f"{m:,}", rtl(ar_name), idx),
                tags=(tag,),
            )
            c_lbl = rtl(f"{idx:02d}. {ar_name}  ({m:,} مباراة | {wr_s})")
            self._combo_label_to_id[c_lbl] = mid
            combo_vals.append(c_lbl)

        self.model_cb["values"] = combo_vals
        if not keep_current or self.selected_model_id not in filtered_ids:
            if filtered_ids:
                self.selected_model_id = filtered_ids[0]

        for lbl, m in self._combo_label_to_id.items():
            if m == self.selected_model_id:
                self.model_combo_var.set(lbl)
                break

        self._sync_tree_selection_to_model(self.selected_model_id)
        self._render_selected_model()
        try:
            self.models_tree.xview_moveto(1.0)
        except Exception:
            pass

    # ------------------------------------------------------------------
    # Populate Selected Model's Hero Strip, Charts & 50-Card Table
    # ------------------------------------------------------------------
    def _render_selected_model(self) -> None:
        mid = self.selected_model_id
        spec = ROSTER_MAP.get(mid, ROSTER_40[0])
        ar_name = arabic_model_name(mid)
        cat_ar = CATEGORY_AR.get(spec.category, spec.category)
        rec = self.store.roster.get(mid, {})
        m = int(rec.get("matches", 0))
        w = int(rec.get("wins", 0))
        wr = (100.0 * w / m) if m > 0 else 0.0
        elo = float(rec.get("elo", 1000.0))

        cids, is_settled = self._decks_cache.get(mid, ([], False))
        dstats = self._summarize_deck_types(cids)
        self._current_deck_stats = dstats
        cnts = dstats["counts"]

        st_badge = "🟢 ديك نهائي مستقر من التدريب" if is_settled else "🟡 ديك مبدئي للنموذج (يستقر تلقائياً فور تدريب النموذج)"
        self.hero_title_var.set(
            rtl(
                f"الديك النهائي لـ [{ar_name}] ({cat_ar})  —  {st_badge}  |  "
                f"المباريات: {m:,}  •  الفوز: {wr:.1f}%  •  تصنيف ELO: {elo:.0f}"
            )
        )
        envs_str = " + ".join(dstats["env_names"]) if dstats["env_names"] else "—"
        self.hero_sub_var.set(
            rtl(
                f"إجمالي: {dstats['total']} بطاقة ({dstats['unique']} بطاقة فريدة)  |  "
                f"وحوش قتالية: {cnts['monster']}  •  حواجز: {cnts['barrier']}  •  سحر: {cnts['spell']}  •  "
                f"القائد: {dstats['leader_name']}  •  العهد: {dstats['cov_name']}  •  البيئة: {envs_str}  |  "
                f"متوسط المانا: {dstats['avg_mana']:.2f}"
            )
        )

        # Populate 50-Card Table (grouped by unique Card ID with copy count x1 / x2)
        self.deck_tree.delete(*self.deck_tree.get_children())
        id_counts = Counter(cids)
        mcs = self.store.model_card_stats.get(mid, {})

        type_order = {"leader": 0, "covenant": 1, "environment": 2, "monster": 3, "spell": 5}

        def _card_sort_key(cid_k: int) -> Tuple[int, int, int]:
            c_obj = self.db.cards[cid_k]
            t_rank = 4 if c_obj.is_barrier else type_order.get(c_obj.type, 9)
            return (t_rank, c_obj.mana, cid_k)

        for cid in sorted(id_counts.keys(), key=_card_sort_key):
            qty = id_counts[cid]
            c = self.db.cards[cid]
            t_key = "barrier" if c.is_barrier else c.type
            t_lbl = TYPE_AR.get(t_key, c.type)

            if c.type == "monster":
                el_s = ELEM_AR.get(c.element_name, c.element_name)
                tr_s = TRIBE_AR.get(c.tribe_name, "—") if not c.is_barrier else "حاجز"
                rl_s = ROLE_AR.get(c.role_name, c.role_name)
                role_col = f"{el_s} • {tr_s} • {rl_s}"
                stat_col = f"{c.mana}م | {c.atk}/{c.deff}"
                row_tag = "barrier" if c.is_barrier else "monster"
            elif c.type == "spell":
                el_s = ELEM_AR.get(c.element_name, "عام") if c.element_name != "None" else "سحر عام"
                role_col = f"{el_s} • تعويذة"
                stat_col = f"{c.mana} مانا"
                row_tag = "spell"
            else:
                role_col = "بطاقة خاصة (قائد/عهد/بيئة)"
                stat_col = f"{c.mana} مانا"
                row_tag = "special"

            # Model-specific card win rate (fallback to global card_stats)
            m_arr = mcs.get(str(cid))
            if isinstance(m_arr, list) and len(m_arr) >= 4 and int(m_arr[2]) > 0:
                pl_cnt = int(m_arr[2])
                wr_c = f"{100.0 * int(m_arr[3]) / pl_cnt:.1f}%"
            else:
                gst = self.store.card_stats.get(str(cid), {})
                pl_cnt = int(gst.get("played", 0))
                wr_c = f"{100.0 * int(gst.get('played_wins', 0)) / pl_cnt:.1f}%" if pl_cnt >= 5 else "--"

            exp = explain_card_arabic(c)
            self.deck_tree.insert(
                "",
                "end",
                iid=str(cid),
                values=(
                    rtl(exp[:115]),
                    wr_c,
                    f"{pl_cnt:,}",
                    rtl(stat_col),
                    rtl(role_col),
                    rtl(t_lbl),
                    f"×{qty}",
                    c.name,
                    cid,
                ),
                tags=(row_tag,),
            )

        self._draw_card_types_chart()
        self._draw_mana_and_affinities_chart()
        try:
            self.deck_tree.xview_moveto(1.0)
        except Exception:
            pass

    def _on_deck_card_selected(self, _e: Any = None) -> None:
        sel = self.deck_tree.selection()
        if not sel:
            return
        try:
            cid = int(sel[0])
            c = self.db.cards.get(cid)
            if c:
                self.card_detail_var.set(rtl(f"بطاقة #{cid} ({c.name}):  {explain_card_arabic(c)}"))
        except Exception:
            pass

    def _copy_current_deck_to_clipboard(self) -> None:
        mid = self.selected_model_id
        ar_name = arabic_model_name(mid)
        cids, is_settled = self._decks_cache.get(mid, ([], False))
        id_counts = Counter(cids)
        lines = [
            f"# الديك النهائي لنموذج: {ar_name} ({mid})",
            f"# الإجمالي: {len(cids)} بطاقة ({len(id_counts)} بطاقة فريدة)",
            "",
        ]
        for cid, qty in sorted(id_counts.items()):
            c = self.db.cards[cid]
            lines.append(f"- [x{qty}] #{cid} {c.name} — {explain_card_arabic(c)}")
        txt = "\n".join(lines)
        try:
            self.parent.clipboard_clear()
            self.parent.clipboard_append(txt)
            self.parent.update()
            self.card_detail_var.set(rtl(f"✅ تم نسخ الديك الكامل لـ [{ar_name}] (50 بطاقة بشرحها العربي) إلى الحافظة!"))
        except Exception:
            pass

    # ------------------------------------------------------------------
    # Chart 1: Quick Card Types Breakdown + 6 Combat Roles
    # ------------------------------------------------------------------
    def _draw_card_types_chart(self) -> None:
        cv = self.cv_types
        cv.delete("all")
        w = max(380, cv.winfo_width())
        h = max(210, cv.winfo_height())

        d = self._current_deck_stats
        if not d or d.get("total", 0) <= 0:
            cv.create_text(w // 2, h // 2, text=rtl("اختر نموذجاً لعرض توزيع أنواع البطاقات."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        tot = max(1, int(d["total"]))
        cnts = d["counts"]
        type_items = [
            ("وحوش قتالية (Monsters)", cnts["monster"], COL_BLUE),
            ("حواجز دفاعية (Barriers)", cnts["barrier"], COL_CYAN),
            ("تعاويذ سحرية (Spells)", cnts["spell"], COL_GOLD),
            ("تميمة قائد (Leader)", cnts["leader"], COL_PURPLE),
            ("عهد وقيد (Covenant)", cnts["covenant"], COL_PINK),
            ("ساحات بيئة (Environments)", cnts["environment"], COL_GREEN),
        ]

        # 1. Top Full-Width Stacked Composition Bar (50 Cards = 100%)
        bar_l, bar_r = 16, w - 16
        bar_w = max(160, bar_r - bar_l)
        cv.create_text(
            w - 14, 11,
            text=rtl(f"التوزيع الكلي للـ {tot} بطاقة في الديك حسب نوع البطاقة:"),
            anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"),
        )
        cur_r = bar_r
        for lbl, c_val, col in type_items:
            if c_val <= 0:
                continue
            seg_w = max(4, int(round(bar_w * (c_val / float(tot)))))
            x0 = max(bar_l, cur_r - seg_w)
            cv.create_rectangle(x0, 22, cur_r, 40, fill=col, outline=BG_PANEL, width=1)
            if cur_r - x0 >= 28:
                cv.create_text((x0 + cur_r) / 2, 31, text=f"{c_val}", fill="#FFFFFF", font=(FONT_UI, 8, "bold"))
            cur_r = x0

        # 2. Right Column: 6 Card Types Bars
        mid_x = int(w * 0.48)
        r_lbl_x = w - 12
        r_bar_r = w - 145
        r_bar_l = mid_x + 48
        r_bar_w = max(45, r_bar_r - r_bar_l)

        cv.create_text(w - 12, 54, text=rtl("تفصيل أنواع البطاقات (العدد والنسبة):"), anchor="e", fill=COL_GOLD, font=(FONT_UI, 8, "bold"))
        for i, (lbl, c_val, col) in enumerate(type_items):
            y = 68 + i * 26
            pct = 100.0 * c_val / float(tot)
            cv.create_text(r_lbl_x, y + 7, text=rtl(lbl.split(" (")[0]), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(r_bar_l, y + 1, r_bar_r, y + 13, fill="#222B3B", outline="")
            fw = max(2, int(r_bar_w * min(1.0, c_val / 40.0))) if c_val > 0 else 0
            if fw > 0:
                cv.create_rectangle(r_bar_r - fw, y + 1, r_bar_r, y + 13, fill=col, outline="")
            cv.create_text(r_bar_l - 5, y + 7, text=f"{c_val} ({pct:.0f}%)", anchor="e", fill=col, font=(FONT_UI, 8, "bold"))

        cv.create_line(mid_x, 50, mid_x, h - 8, fill=BG_BORDER)

        # 3. Left Column: 6 Combat Roles Breakdown inside Deck's Monsters
        roles_cnt: Counter = d["roles"]
        role_order = [
            ("مهاجم", COL_RED),
            ("قاتل", COL_PURPLE),
            ("مدافع", COL_BLUE),
            ("حاجز", COL_CYAN),
            ("داعم", COL_GREEN),
            ("معطّل", COL_GOLD),
        ]
        l_lbl_x = mid_x - 10
        l_bar_r = mid_x - 68
        l_bar_l = 44
        l_bar_w = max(40, l_bar_r - l_bar_l)
        m_tot = max(1, cnts["monster"] + cnts["barrier"])

        cv.create_text(l_lbl_x, 54, text=rtl("توزيع الأدوار القتالية الـ 6 بالملعب:"), anchor="e", fill=COL_CYAN, font=(FONT_UI, 8, "bold"))
        for i, (r_ar, col) in enumerate(role_order):
            y = 68 + i * 26
            rc = int(roles_cnt.get(r_ar, 0))
            cv.create_text(l_lbl_x, y + 7, text=rtl(r_ar), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8))
            cv.create_rectangle(l_bar_l, y + 1, l_bar_r, y + 13, fill="#222B3B", outline="")
            fw = max(2, int(l_bar_w * min(1.0, rc / float(max(12, m_tot * 0.5))))) if rc > 0 else 0
            if fw > 0:
                cv.create_rectangle(l_bar_r - fw, y + 1, l_bar_r, y + 13, fill=col, outline="")
            cv.create_text(l_bar_l - 5, y + 7, text=f"{rc}", anchor="e", fill=col, font=(FONT_UI, 8, "bold"))

    # ------------------------------------------------------------------
    # Chart 2: Mana Curve (0..7 Mana) + Top Tribes & Elements in Deck
    # ------------------------------------------------------------------
    def _draw_mana_and_affinities_chart(self) -> None:
        cv = self.cv_curve
        cv.delete("all")
        w = max(360, cv.winfo_width())
        h = max(210, cv.winfo_height())

        d = self._current_deck_stats
        if not d or d.get("total", 0) <= 0:
            cv.create_text(w // 2, h // 2, text=rtl("اختر نموذجاً لعرض منحنى المانا."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        mh = d["mana_hist"]
        max_m = max(8, max(mh))
        cv.create_text(
            w - 12, 11,
            text=rtl(
                f"منحنى تكلفة المانا (متوسط التكلفة: {d['avg_mana']:.2f} مانا  |  إجمالي هجوم الوحوش: {d['atk_sum']}  |  إجمالي الدفاع: {d['def_sum']}):"
            ),
            anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"),
        )

        # Top 55%: Vertical Mana Curve Bars (RTL: 0 Mana on Right -> 7 Mana on Left)
        top_y, bot_y = 26, int(h * 0.54)
        plot_h = max(55, bot_y - top_y)
        lx, rx = 18, w - 18
        step = (rx - lx) / 8.0
        bw = max(18, int(step * 0.58))
        m_cols = [COL_PURPLE, COL_CYAN, COL_GREEN, COL_BLUE, COL_TEAL, COL_GOLD, COL_PINK, COL_RED]

        for m_cost in range(8):
            cnt = mh[m_cost]
            cx = rx - (m_cost + 0.5) * step
            bh = int(plot_h * (cnt / float(max_m))) if cnt > 0 else 0
            cv.create_rectangle(cx - bw / 2, top_y, cx + bw / 2, bot_y, fill="#1E2534", outline="")
            if bh > 0:
                cv.create_rectangle(cx - bw / 2, bot_y - bh, cx + bw / 2, bot_y, fill=m_cols[m_cost], outline="")
            cv.create_text(cx, bot_y - bh - 7, text=str(cnt), fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_text(cx, bot_y + 10, text=rtl(f"{m_cost} مانا"), fill=FG_MUTED, font=(FONT_UI, 7, "bold"))

        # Bottom 45%: Top Tribes (Right half) & Top Elements (Left half) in Deck
        by0 = bot_y + 24
        cv.create_line(12, by0 - 3, w - 12, by0 - 3, fill=BG_BORDER)
        mid_x = w // 2
        cv.create_line(mid_x, by0, mid_x, h - 6, fill=BG_BORDER)

        cv.create_text(w - 12, by0 + 9, text=rtl("أبرز العشائر في الديك:"), anchor="e", fill=COL_GOLD, font=(FONT_UI, 8, "bold"))
        for i, (tr_nm, cnt) in enumerate(d["tribes"][:4]):
            y = by0 + 24 + i * 19
            cv.create_text(w - 14, y, text=rtl(f"• {tr_nm}"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8))
            cv.create_text(mid_x + 12, y, text=rtl(f"{cnt} بطاقة"), anchor="w", fill=COL_GOLD, font=(FONT_UI, 8, "bold"))

        cv.create_text(mid_x - 12, by0 + 9, text=rtl("أبرز العناصر في الديك:"), anchor="e", fill=COL_CYAN, font=(FONT_UI, 8, "bold"))
        for i, (el_nm, cnt) in enumerate(d["elems"][:4]):
            y = by0 + 24 + i * 19
            cv.create_text(mid_x - 14, y, text=rtl(f"• {el_nm}"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8))
            cv.create_text(12, y, text=rtl(f"{cnt} بطاقة"), anchor="w", fill=COL_CYAN, font=(FONT_UI, 8, "bold"))

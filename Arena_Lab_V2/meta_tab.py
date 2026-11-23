# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Pillar #13: Meta Discovery, Strategic Entropy & Synergy Explorer Tab (Arabic RTL).
Analyzes all 41 settled 50-card decks, 1,470 card win rates, Glitch_41 anomaly hunter, and pairwise synergies:
  - Overall 0-100% Meta Discovery & Longevity Score (>=85% Ideal, >=70% Good, <70% Warning)
  - Interactive 3-Way Chart 1: Top 8 Staples (Most Included) | Top 8 Hidden Gems | Top 8 Winning Synergy Pairs
  - Chart 2: Meta Vitality & Longevity Bars + Top 4 Most-Similar Deck Pairs (Clone Watch)
  - Self-Explanatory 14-Row Diagnostic Table (Actual | Ideal | Good | Status | Auto-Diagnosis)
"""

from __future__ import annotations

import json
import math
import os
import tkinter as tk
from collections import Counter
from tkinter import ttk
from typing import Any, Callable, Dict, List, Optional, Set, Tuple

from engine.card_model import CardDB
from engine.roster_40 import ROSTER_40, ROSTER_MAP
from trainer.stats_store import StatsStore
from pace_tab import rtl
from arabic_glossary import arabic_model_name, enable_treeview_sorting

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


class MetaDiscoveryTab:
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
        self.mode_var = tk.StringVar(value="staples")
        self.status_var = tk.StringVar(value="")
        self._mode_buttons: Dict[str, tk.Button] = {}
        self._last_metrics: Dict[str, Any] = {}
        self._build_layout()

    def _build_layout(self) -> None:
        ctrl = tk.Frame(self.parent, bg=BG_PANEL, padx=12, pady=8)
        ctrl.pack(fill="x", padx=8, pady=(6, 4))

        tk.Label(
            ctrl,
            text=rtl("مستكشف الميتا والتآزر الاستراتيجي عبر كل الـ 41 ديكاً والـ 1,470 بطاقة:"),
            bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold"),
        ).pack(side="right", padx=(4, 0))

        tk.Button(
            ctrl,
            text=rtl("إعادة تحليل ديكات الميتا (41 ديك)"),
            command=self.refresh,
            bg=COL_PURPLE,
            fg="#FFFFFF",
            relief="flat",
            font=(FONT_UI, 9, "bold"),
            padx=12,
            pady=4,
            cursor="hand2",
        ).pack(side="left")

        tk.Label(
            ctrl, textvariable=self.status_var, bg=BG_PANEL, fg=COL_GOLD,
            font=(FONT_UI, 8, "bold"), anchor="e", justify="right",
        ).pack(side="left", padx=10)

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

        # Visual Charts Row (RTL: Interactive 3-Way Meta Explorer on RIGHT 54%, Vitality & Clone Watch on LEFT 46%)
        charts = tk.Frame(self.inner, bg=BG_DARK)
        charts.pack(fill="x", pady=(0, 8))
        charts.grid_columnconfigure(0, weight=46, uniform="meta")
        charts.grid_columnconfigure(1, weight=54, uniform="meta")

        p_vit = self._make_panel(charts, "مؤشرات حيوية وطول عمر الميتا + أكثر الديكات تشابهاً (Clone Watch)", COL_CYAN)
        p_vit.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        self.cv_vit = tk.Canvas(p_vit, bg=BG_PANEL, height=255, highlightthickness=0)
        self.cv_vit.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_vit.bind("<Configure>", lambda _e: self._draw_vitality())

        p_exp = self._make_panel(charts, "مستكشف البطاقات والأزواج التفاعلي (اضغط للتبديل)", COL_GOLD)
        p_exp.grid(row=0, column=1, sticky="nsew", padx=(5, 0))

        tbar = tk.Frame(p_exp, bg=BG_PANEL, padx=8, pady=2)
        tbar.pack(fill="x")
        modes = [
            ("staples", "أكثر 8 بطاقات احتكاراً (Staples)"),
            ("gems", "أقوى 8 جواهر مخفية (Gems)"),
            ("pairs", "أقوى 8 أزواج تآزر (Pairs)"),
        ]
        for key, lbl in modes:
            btn = tk.Button(
                tbar,
                text=rtl(lbl),
                command=lambda k=key: self._set_mode(k),
                bg=COL_BLUE if key == "staples" else BG_CARD,
                fg="#FFFFFF" if key == "staples" else FG_MUTED,
                relief="flat",
                font=(FONT_UI, 8, "bold"),
                padx=8,
                pady=2,
                cursor="hand2",
            )
            btn.pack(side="right", padx=2)
            self._mode_buttons[key] = btn

        self.cv_exp = tk.Canvas(p_exp, bg=BG_PANEL, height=228, highlightthickness=0)
        self.cv_exp.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_exp.bind("<Configure>", lambda _e: self._draw_explorer())

        # Self-Explanatory 14-Indicator Diagnostic Table
        tbl_panel = self._make_panel(
            self.inner,
            "بطاقة التقييم التفصيلية لمعيار اكتشاف الميتا والتنوع الاستراتيجي (14 مؤشراً — النتيجة + الجيد + المثالي + التشخيص)",
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
        self.meta_tree = ttk.Treeview(tbl_panel, columns=tuple(c for c, _, _ in headers), show="headings", height=14)
        for c, txt, w in headers:
            self.meta_tree.heading(c, text=rtl(txt))
            self.meta_tree.column(c, width=w, anchor="e" if c in ("metric", "diag") else "center")

        self.meta_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.meta_tree.tag_configure("good", foreground="#FACC15", background="#242116")
        self.meta_tree.tag_configure("bad", foreground="#F87171", background="#29181B")
        self.meta_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        self.meta_tree.pack(fill="x", padx=8, pady=(2, 8))
        enable_treeview_sorting(self.meta_tree)

    def _make_panel(self, parent: tk.Widget, title: str, accent: str) -> tk.Frame:
        f = tk.Frame(parent, bg=BG_PANEL, highlightbackground=BG_BORDER, highlightthickness=1)
        hdr = tk.Frame(f, bg=BG_PANEL, padx=10, pady=7)
        hdr.pack(fill="x")
        tk.Frame(hdr, bg=accent, width=4, height=15).pack(side="right", padx=(7, 0))
        tk.Label(hdr, text=rtl(title), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold"), anchor="e", justify="right").pack(side="right")
        return f

    def _set_mode(self, mode: str) -> None:
        self.mode_var.set(mode)
        for k, b in self._mode_buttons.items():
            b.config(bg=COL_BLUE if k == mode else BG_CARD, fg="#FFFFFF" if k == mode else FG_MUTED)
        self._draw_explorer()

    def _on_mousewheel(self, event: tk.Event) -> None:
        try:
            if str(self.parent.winfo_ismapped()) == "1":
                self.scroll_canvas.yview_scroll(int(-1 * (event.delta / 120)), "units")
        except Exception:
            pass

    def refresh(self) -> None:
        self._compute_and_populate()
        self._draw_score_banner()
        self._draw_explorer()
        self._draw_vitality()

    @staticmethod
    def _grade_range(val: float, ideal_lo: float, ideal_hi: float, good_lo: float, good_hi: float) -> Tuple[str, str, int]:
        if ideal_lo <= val <= ideal_hi:
            return ("🟢 مثالي", "ideal", 100)
        if good_lo <= val <= good_hi:
            return ("🟡 جيد", "good", 72)
        return ("🔴 يحتاج تعديل", "bad", 28)

    def _load_all_settled_decks(self) -> Dict[str, List[int]]:
        saved_dir = getattr(self.store, "saved_models_dir", os.path.abspath(os.path.join(self.store.logs_dir, "..", "saved_models")))
        decks: Dict[str, List[int]] = {}
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
                rd = self.store.roster.get(mid, {}).get("settled_deck") or []
                if len(rd) == 50:
                    cids = [int(x) for x in rd]
            if len(cids) == 50:
                decks[mid] = cids
        return decks

    def _compute_and_populate(self) -> None:
        self.meta_tree.delete(*self.meta_tree.get_children())
        decks = self._load_all_settled_decks()
        n_decks = len(decks)

        if n_decks < 4:
            self._last_metrics = {"has_data": False, "score": 0, "ideal_cnt": 0, "good_cnt": 0, "bad_cnt": 0}
            self.meta_tree.insert(
                "", "end",
                values=(rtl("لا توجد ديكات مستقرة كافية (settled_deck.json) لتحليل الميتا."), rtl("بانتظار بيانات"), "--", "--", "--", rtl("تحليل الميتا جاهز"), "!"),
                tags=("nodata",),
            )
            return

        # 1 & 2. Strategic Meta Entropy & Active Card Pool across all 41 decks
        card_slot_freq: Counter = Counter()
        card_deck_cnt: Counter = Counter()
        deck_sets: Dict[str, Set[int]] = {}
        for mid, cids in decks.items():
            card_slot_freq.update(cids)
            uniq = set(cids)
            deck_sets[mid] = uniq
            for cid in uniq:
                card_deck_cnt[cid] += 1

        tot_slots = sum(card_slot_freq.values())
        active_unique = len(card_slot_freq)
        ent = 0.0
        for cnt in card_slot_freq.values():
            p = cnt / float(tot_slots)
            ent -= p * math.log2(p)
        # Max theoretical unique cards in n_decks of 50 (with 2x copies each -> 25*n_decks capped at 1,470)
        max_ent = math.log2(min(self.db.num_cards, max(2, 25 * n_decks)))
        meta_entropy = min(100.0, 100.0 * ent / max_ent)

        # 3 & 4. Pairwise Jaccard Similarity across all N*(N-1)/2 pairs
        mids = sorted(decks.keys())
        jac_vals: List[float] = []
        top_clones: List[Tuple[float, str, str]] = []
        for i in range(len(mids)):
            si = deck_sets[mids[i]]
            for j in range(i + 1, len(mids)):
                sj = deck_sets[mids[j]]
                inter = len(si & sj)
                union = max(1, len(si | sj))
                jac = 100.0 * inter / union
                jac_vals.append(jac)
                if jac >= 10.0:
                    top_clones.append((jac, arabic_model_name(mids[i]), arabic_model_name(mids[j])))
        avg_jaccard = sum(jac_vals) / max(1, len(jac_vals))
        top_clones.sort(key=lambda x: x[0], reverse=True)
        max_clone = top_clones[0] if top_clones else (0.0, "--", "--")

        # 5 & 6. Auto-Include Staples (>=25% of all decks) & Top Single Staple
        staple_thresh = max(3, int(math.ceil(0.25 * n_decks)))
        staples_all = [(cid, cnt) for cid, cnt in card_deck_cnt.most_common() if cnt >= staple_thresh]
        top_cid, top_cnt = card_deck_cnt.most_common(1)[0]
        top_incl_pct = 100.0 * top_cnt / n_decks
        top_cname = self.db.cards[top_cid].name if top_cid in self.db.cards else f"#{top_cid}"

        # 7. Deck Synergy Coherence (dominant Tribe or Element concentration among monsters)
        coh_scores: List[float] = []
        for mid, cids in decks.items():
            tr_c: Counter = Counter()
            el_c: Counter = Counter()
            mon_n = 0
            for cid in cids:
                c = self.db.cards.get(cid)
                if c and c.type == "monster" and not c.is_barrier:
                    mon_n += 1
                    if c.tribe >= 0:
                        tr_c[c.tribe] += 1
                    if c.element >= 0:
                        el_c[c.element] += 1
            if mon_n > 0:
                best_tr = tr_c.most_common(1)[0][1] if tr_c else 0
                best_el = el_c.most_common(1)[0][1] if el_c else 0
                coh_scores.append(100.0 * max(best_tr, best_el) / mon_n)
        avg_coherence = sum(coh_scores) / max(1, len(coh_scores))

        # Model Win Rates from store.roster
        model_wr: Dict[str, float] = {}
        viable_cnt = 0
        for spec in ROSTER_40:
            r = self.store.roster.get(spec.model_id, {})
            m = int(r.get("matches", 0))
            wr = (100.0 * int(r.get("wins", 0)) / m) if m > 0 else 50.0
            model_wr[spec.model_id] = wr
            if m >= 10 and wr >= 50.0:
                viable_cnt += 1

        # 8. Winning Synergy Card Pairs (co-occurring in >=3 decks with WR >= 52%)
        pair_cnt: Counter = Counter()
        pair_wr_sum: Dict[Tuple[int, int], float] = {}
        win_decks = [m for m in mids if model_wr.get(m, 50.0) >= 52.0]
        for mid in win_decks:
            u_list = sorted(deck_sets[mid])
            w_m = model_wr[mid]
            for a_i in range(len(u_list)):
                ca = u_list[a_i]
                for b_i in range(a_i + 1, len(u_list)):
                    key = (ca, u_list[b_i])
                    pair_cnt[key] += 1
                    pair_wr_sum[key] = pair_wr_sum.get(key, 0.0) + w_m

        good_pairs = [
            (cnt, pair_wr_sum[k] / cnt, k[0], k[1])
            for k, cnt in pair_cnt.items()
            if cnt >= 3
        ]
        good_pairs.sort(key=lambda x: (x[0], x[1]), reverse=True)

        # 9 & 10. Hidden Gems (WR >= 57%, played >= 30, <= 2 decks) & Trap Cards (>= 4 decks, WR < 45%)
        gems_list: List[Tuple[float, int, int, str]] = []
        traps_list: List[Tuple[float, int, int, str]] = []
        for cid_s, st in self.store.card_stats.items():
            pl = int(st.get("played", 0))
            if pl < 30:
                continue
            cid = int(cid_s)
            wr = 100.0 * int(st.get("played_wins", 0)) / pl
            d_cnt = int(card_deck_cnt.get(cid, 0))
            cname = self.db.cards[cid].name if cid in self.db.cards else f"#{cid}"
            if wr >= 57.0 and d_cnt <= 2:
                gems_list.append((wr, d_cnt, pl, cname))
            elif wr < 45.0 and d_cnt >= 4:
                traps_list.append((wr, d_cnt, pl, cname))
        gems_list.sort(key=lambda x: (x[0], x[2]), reverse=True)
        traps_list.sort(key=lambda x: (x[0], -x[1]))

        # 11. Glitch_41 Off-Meta Anomaly Hunter Performance
        g_rec = self.store.roster.get("anomaly_glitch", {})
        g_m = int(g_rec.get("matches", 0))
        g_wr = (100.0 * int(g_rec.get("wins", 0)) / g_m) if g_m > 0 else 50.0

        # 12. Severe Exploits in anomalies log
        anoms = self.store.excitement.get("anomalies", [])
        sev_anoms = [a for a in anoms if a.get("type") in ("Speed Glitch Kill", "Stat Freak Monster", "OTK / Turn Burst")]
        sev_cnt = len(sev_anoms)

        # 14. Meta Longevity Index (0-100%)
        distinct_sc = max(0.0, min(100.0, 100.0 - avg_jaccard * 3.0))
        viable_sc = min(100.0, 100.0 * viable_cnt / 22.0)
        gems_sc = min(100.0, 100.0 * min(len(gems_list), 80) / 50.0)
        longevity = 0.35 * meta_entropy + 0.25 * distinct_sc + 0.25 * viable_sc + 0.15 * gems_sc

        rows: List[Tuple[int, str, str, str, str, str, str, str, int]] = []

        # 1. Strategic Meta Entropy
        st, tg, pts = self._grade_range(meta_entropy, 82.0, 100.0, 68.0, 100.0)
        diag = (
            f"تنوع استراتيجي ضخم! إنتروبيا الميتا {meta_entropy:.1f}% عبر الـ {n_decks} ديكاً." if tg == "ideal"
            else f"تجمّع الميتا حول نفس الكروت (إنتروبيا {meta_entropy:.1f}%)."
        )
        rows.append((1, "إنتروبيا التنوع الاستراتيجي (Meta Entropy)", f"{meta_entropy:.1f}%", "82% فأعلى", "68% – 81%", st, diag, tg, pts))

        # 2. Active Meta Pool (Unique cards in latest 41 decks)
        st, tg, pts = self._grade_range(float(active_unique), 450.0, float(self.db.num_cards), 320.0, float(self.db.num_cards))
        diag = f"{active_unique:,} بطاقة فريدة حيّة داخل أحدث الـ {n_decks} ديكاً ({100*active_unique/self.db.num_cards:.1f}% من الـ {self.db.num_cards:,})."
        rows.append((2, "حجم حوض البطاقات النشط في الـ 41 ديكاً", f"{active_unique:,} بطاقة", "450+ بطاقة", "320 – 449", st, diag, tg, pts))

        # 3. Avg Pairwise Deck Similarity (Jaccard %)
        st, tg, pts = self._grade_range(avg_jaccard, 0.0, 8.0, 0.0, 15.0)
        diag = (
            f"هويات مستقلة تماماً! متوسط تشابه أي ديكين هو {avg_jaccard:.1f}% فقط عبر 820 زوجاً." if tg == "ideal"
            else f"تشابه ملحوظ بين الديكات (متوسط Jaccard = {avg_jaccard:.1f}%)."
        )
        rows.append((3, "متوسط تشابه الديكات المتبادل (Jaccard %)", f"{avg_jaccard:.1f}%", "8% فأقل", "8% – 15%", st, diag, tg, pts))

        # 4. Max Deck Clone Similarity %
        st, tg, pts = self._grade_range(max_clone[0], 0.0, 35.0, 0.0, 55.0)
        diag = f"أكثر ديكين تشابهاً: {max_clone[1]} و {max_clone[2]} بنسبة {max_clone[0]:.1f}%."
        rows.append((4, "أعلى نسبة تشابه بين ديكين (Clone Watch)", f"{max_clone[0]:.1f}%", "35% فأقل", "35% – 55%", st, diag, tg, pts))

        # 5. Auto-Include Staples (>=25% of decks)
        st, tg, pts = self._grade_range(float(len(staples_all)), 0.0, 5.0, 0.0, 14.0)
        diag = (
            f"صفر احتكار خانق! فقط {len(staples_all)} بطاقات موجودة في >=25% من كل الديكات." if tg == "ideal"
            else f"تحذير! يوجد {len(staples_all)} بطاقة إجبارية (Staples) مكررة في ربع الديكات أو أكثر!"
        )
        rows.append((5, "عدد البطاقات المحتكرة للميتا (>=25% ديكات)", f"{len(staples_all)} بطاقة", "0 – 5 بطاقات", "6 – 14 بطاقة", st, diag, tg, pts))

        # 6. Top Single Card Inclusion Rate %
        st, tg, pts = self._grade_range(top_incl_pct, 0.0, 35.0, 0.0, 55.0)
        diag = f"الأكثر انتشاراً: {top_cname} (موجودة في {top_cnt}/{n_decks} ديكاً = {top_incl_pct:.1f}%)."
        rows.append((6, "أعلى نسبة انتشار لبطاقة واحدة (Top Staple)", f"{top_incl_pct:.1f}% ({top_cnt}/{n_decks})", "35% فأقل", "35% – 55%", st, diag, tg, pts))

        # 7. Deck Synergy Coherence %
        st, tg, pts = self._grade_range(avg_coherence, 55.0, 100.0, 40.0, 100.0)
        diag = (
            f"بناء ديكات متماسك! متوسط التركيز القبلي/العنصري للوحوش هو {avg_coherence:.1f}%." if tg == "ideal"
            else f"الديكات مبعثرة نسبياً (تماسك التآزر {avg_coherence:.1f}% فقط)."
        )
        rows.append((7, "تماسك التآزر القبلي والعنصري داخل الديكات", f"{avg_coherence:.1f}%", "55% فأعلى", "40% – 54%", st, diag, tg, pts))

        # 8. Winning Synergy Card Pairs
        st, tg, pts = self._grade_range(float(len(good_pairs)), 15.0, 5000.0, 5.0, 5000.0)
        if good_pairs:
            p0 = good_pairs[0]
            n_a = self.db.cards[p0[2]].name[:16] if p0[2] in self.db.cards else f"#{p0[2]}"
            n_b = self.db.cards[p0[3]].name[:16] if p0[3] in self.db.cards else f"#{p0[3]}"
            diag = f"تم رصد {len(good_pairs)} زوجاً متآزراً رابحاً (الأقوى: {n_a} + {n_b} في {p0[0]} ديكات)."
        else:
            diag = "لم يتم رصد أزواج تآزر متكررة في 3+ ديكات رابحة (تنوع فردي عالٍ جداً!)."
        rows.append((8, "أزواج الكومبو والتآزر الرابحة (>=3 ديكات)", f"{len(good_pairs)} زوج", "15+ زوجاً", "5 – 14", st, diag, tg, pts))

        # 9. Hidden Gems Count
        st, tg, pts = self._grade_range(float(len(gems_list)), 20.0, 220.0, 5.0, 400.0)
        top_g = f" — أبرزها: {gems_list[0][3]} ({gems_list[0][0]:.1f}%)" if gems_list else ""
        diag = f"يوجد {len(gems_list)} جوهرة مخفية (فوز >=57% وفي <=2 ديكات){top_g}."
        rows.append((9, "عدد الجواهر المخفية القابلة للاكتشاف (Gems)", f"{len(gems_list)} جوهرة", "20 – 220", "5 – 400", st, diag, tg, pts))

        # 10. Trap Cards Count
        st, tg, pts = self._grade_range(float(len(traps_list)), 0.0, 8.0, 0.0, 20.0)
        top_t = f" — أبرزها: {traps_list[0][3]} ({traps_list[0][0]:.1f}%)" if traps_list else ""
        diag = (
            f"قلة الفخاخ الخادعة ({len(traps_list)} بطاقات فقط شائعة بفوز <45%{top_t})." if tg == "ideal"
            else f"يوجد {len(traps_list)} بطاقة فخ منتشرة لكنها تخسر كثيراً{top_t}."
        )
        rows.append((10, "عدد البطاقات الفخاخ (شائعة >=4 ديكات وفوز <45%)", f"{len(traps_list)} بطاقة", "0 – 8", "9 – 20", st, diag, tg, pts))

        # 11. Glitch_41 Anomaly Hunter WR %
        st, tg, pts = self._grade_range(g_wr, 45.0, 58.0, 40.0, 63.0)
        diag = (
            f"إبداع متوازن خارج الميتا! صائد الثغرات Glitch_41 يحقق {g_wr:.1f}% فوز ({g_m:,} مباراة) بلا ثغرات مكسورة." if tg == "ideal"
            else (f"تحذير! Glitch_41 اكتشف استراتيجية خارقة خارج الميتا ({g_wr:.1f}% فوز)!" if g_wr > 58.0 else f"اللعب خارج الميتا ضعيف جداً (Glitch_41 = {g_wr:.1f}%).")
        )
        rows.append((11, "أداء صائد الثغرات خارج الميتا (Glitch_41)", f"{g_wr:.1f}% ({g_m:,}م)", "45% – 58%", "40% – 63%", st, diag, tg, pts))

        # 12. Severe Tracked Anomalies / Exploits
        st, tg, pts = self._grade_range(float(sev_cnt), 0.0, 12.0, 0.0, 35.0)
        diag = (
            f"ميتا نظيفة من الشذوذات القاتلة ({sev_cnt} حالة مسجلة فقط)." if tg == "ideal"
            else f"تم رصد {sev_cnt} حالة شذوذ شديدة (OTK / Stat Freak / Speed Glitch) في السجل التاريخي!"
        )
        rows.append((12, "حالات الشذوذ والانفجارات الخطيرة المسجلة", f"{sev_cnt} حالة", "12 فأقل", "13 – 35", st, diag, tg, pts))

        # 13. Viable Winning Archetypes (>=50% WR out of 41)
        st, tg, pts = self._grade_range(float(viable_cnt), 18.0, 41.0, 12.0, 41.0)
        diag = (
            f"ميتا غنية ومفتوحة! {viable_cnt} من أصل 41 نموذجاً مختلفاً تحقق >=50% فوز تنافسي." if tg == "ideal"
            else f"{viable_cnt} فقط من 41 نموذجاً تتجاوز حاجز 50% فوز."
        )
        rows.append((13, "عدد الاستراتيجيات التنافسية الرابحة (>=50% WR)", f"{viable_cnt} / 41 نموذج", "18+ نموذجاً", "12 – 17", st, diag, tg, pts))

        # 14. Meta Longevity Index (0-100%)
        st, tg, pts = self._grade_range(longevity, 80.0, 100.0, 65.0, 100.0)
        diag = (
            f"عمر ميتا طويل ومستدام! مؤشر قابلية الاستكشاف والتجدد = {longevity:.1f} / 100%." if tg == "ideal"
            else f"مؤشر طول عمر الميتا {longevity:.1f}% (يرتفع بتقليص تشابه الديكات وتقوية النماذج الضعيفة)."
        )
        rows.append((14, "مؤشر طول عمر الميتا وقابلية الاستكشاف", f"{longevity:.1f}%", "80% فأعلى", "65% – 79%", st, diag, tg, pts))

        avg_score = int(round(sum(r[8] for r in rows) / len(rows)))
        ideal_cnt = sum(1 for r in rows if r[7] == "ideal")
        good_cnt = sum(1 for r in rows if r[7] == "good")
        bad_cnt = sum(1 for r in rows if r[7] == "bad")

        top8_staples = []
        for cid, cnt in card_deck_cnt.most_common(8):
            c = self.db.cards.get(cid)
            nm = c.name if c else f"#{cid}"
            cs = self.store.card_stats.get(str(cid), {})
            pl = int(cs.get("played", 0))
            wr_c = (100.0 * int(cs.get("played_wins", 0)) / pl) if pl > 0 else 50.0
            top8_staples.append((nm, 100.0 * cnt / n_decks, f"{cnt}/{n_decks} (WR {wr_c:.0f}%)"))

        top8_gems = [(nm, wr, f"{wr:.1f}% (n={pl})") for (wr, _dc, pl, nm) in gems_list[:8]]
        top8_pairs = []
        for cnt, awr, ca, cb in good_pairs[:8]:
            na = self.db.cards[ca].name[:14] if ca in self.db.cards else f"#{ca}"
            nb = self.db.cards[cb].name[:14] if cb in self.db.cards else f"#{cb}"
            top8_pairs.append((f"{na} + {nb}", awr, f"{cnt} ديك | WR {awr:.0f}%"))

        self.status_var.set(rtl(f"تم تحليل {n_decks} ديكاً • {active_unique:,} بطاقة فريدة • {len(gems_list)} جوهرة مخفية"))

        self._last_metrics = {
            "has_data": True,
            "score": avg_score,
            "ideal_cnt": ideal_cnt,
            "good_cnt": good_cnt,
            "bad_cnt": bad_cnt,
            "n_decks": n_decks,
            "active_unique": active_unique,
            "staples": top8_staples,
            "gems": top8_gems,
            "pairs": top8_pairs,
            "vit_bars": [
                ("إنتروبيا التنوع الاستراتيجي", meta_entropy, COL_GREEN),
                ("تمايز الديكات (100 − Jaccard)", 100.0 - avg_jaccard, COL_CYAN),
                ("تماسك التآزر القبلي/العنصري", avg_coherence, COL_BLUE),
                ("نسبة الاستراتيجيات الرابحة (>=50%)", 100.0 * viable_cnt / 41.0, COL_GOLD),
                ("أداء Glitch_41 خارج الميتا", g_wr, COL_PURPLE),
                ("مؤشر طول عمر الميتا الإجمالي", longevity, COL_PINK),
            ],
            "top_clones": top_clones[:4],
        }

        for num, m_name, act_s, ideal_s, good_s, st_lbl, diag_s, tag, _ in rows:
            self.meta_tree.insert(
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
            cv.create_text(w // 2, h // 2, text=rtl("بانتظار ديكات الميتا لتحليل التنوع الاستراتيجي."), fill=COL_GOLD, font=(FONT_UI, 11, "bold"))
            return

        sc = int(d["score"])
        if sc >= 85:
            col, badge = COL_GREEN, "🟢 ميتا عميقة ومفتوحة غنية بالاكتشافات والجواهر المخفية (AAA)"
        elif sc >= 70:
            col, badge = COL_GOLD, "🟡 ميتا جيدة التنوع (راجع البطاقات المحتكرة أو الشذوذات لتمديد عمر اللعبة)"
        else:
            col, badge = COL_RED, "🔴 ميتا مغلقة أو شديدة التشابه (تحتاج تنويعاً وإضعاف البطاقات الإجبارية!)"

        cv.create_text(w - 12, 18, text=rtl(f"التقييم العام لمعيار اكتشاف الميتا والتنوع الاستراتيجي:  {sc} / 100%"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 14, "bold"))
        cv.create_text(w - 12, 44, text=rtl(badge), anchor="e", fill=col, font=(FONT_UI, 10, "bold"))
        cv.create_text(
            w - 12, 66,
            text=rtl(f"المعيار: 🟢 >=85% مثالي | 🟡 70–84% جيد | 🔴 <70% خطر  •  (ديكات محللة: {d['n_decks']}/41 • بطاقات فريدة: {d['active_unique']:,}/1,470)"),
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
    # Chart 1: Interactive 3-Way Meta Explorer (Staples / Gems / Synergy Pairs)
    # ------------------------------------------------------------------
    def _draw_explorer(self) -> None:
        cv = self.cv_exp
        cv.delete("all")
        w = max(380, cv.winfo_width())
        h = max(195, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        mode = self.mode_var.get()
        items = d.get(mode, [])
        if not items:
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد عناصر مطابقة لهذا التصنيف حالياً (تنوع فردي عالٍ!)."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        col = COL_GOLD if mode == "staples" else (COL_GREEN if mode == "gems" else COL_CYAN)
        lbl_x = w - 10
        bar_r = w - 188
        bar_l = 112
        bar_w = max(60, bar_r - bar_l)
        row_h = max(24, (h - 14) // max(1, len(items)))

        for i, (nm, pct, tag_txt) in enumerate(items[:8]):
            y = 10 + i * row_h
            cv.create_text(lbl_x, y + 8, text=rtl(f"{i+1}. {nm[:24]}"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_rectangle(bar_l, y + 2, bar_r, y + 14, fill="#242C3D", outline="")
            fw = max(3, int(bar_w * min(100.0, max(0.0, pct)) / 100.0))
            cv.create_rectangle(bar_r - fw, y + 2, bar_r, y + 14, fill=col, outline="")
            cv.create_text(bar_l - 6, y + 8, text=rtl(tag_txt), anchor="e", fill=col, font=(FONT_UI, 7, "bold"))

    # ------------------------------------------------------------------
    # Chart 2: Meta Vitality Bars + Top 4 Similar Deck Pairs (Clone Watch)
    # ------------------------------------------------------------------
    def _draw_vitality(self) -> None:
        cv = self.cv_vit
        cv.delete("all")
        w = max(340, cv.winfo_width())
        h = max(215, cv.winfo_height())

        d = self._last_metrics
        if not d.get("has_data"):
            cv.create_text(w // 2, h // 2, text=rtl("لا توجد بيانات حيوية بعد."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        vbars = d["vit_bars"]
        lbl_x = w - 10
        bar_r = w - 186
        bar_l = 52
        bar_w = max(55, bar_r - bar_l)
        for i, (lbl, val, col) in enumerate(vbars):
            y = 10 + i * 22
            cv.create_text(lbl_x, y + 7, text=rtl(lbl), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8))
            cv.create_rectangle(bar_l, y + 2, bar_r, y + 12, fill="#242C3D", outline="")
            fw = max(2, int(bar_w * min(100.0, max(0.0, val)) / 100.0))
            cv.create_rectangle(bar_r - fw, y + 2, bar_r, y + 12, fill=col, outline="")
            cv.create_text(bar_l - 6, y + 7, text=f"{val:4.1f}%", anchor="e", fill=col, font=(FONT_UI, 8, "bold"))

        # Bottom: Top 4 Clone Watch (Highest Jaccard Deck Pairs)
        cv.create_line(10, 146, w - 10, 146, fill=BG_BORDER)
        cv.create_text(w - 10, 158, text=rtl("أكثر أزواج الديكات تشابهاً (Clone Watch — المثالي <=35%):"), anchor="e", fill=COL_GOLD, font=(FONT_UI, 8, "bold"))
        clones = d.get("top_clones") or []
        if not clones:
            cv.create_text(w // 2, 198, text=rtl("🟢 كل الـ 41 ديكاً فريدة تماماً (<10% تشابه)!"), fill=COL_GREEN, font=(FONT_UI, 8, "bold"))
            return
        for i, (jac, ma, mb) in enumerate(clones[:4]):
            y = 175 + i * 19
            ccol = COL_RED if jac > 55.0 else (COL_GOLD if jac > 35.0 else COL_GREEN)
            cv.create_text(w - 10, y, text=rtl(f"{i+1}. {ma}  ↔  {mb}"), anchor="e", fill=FG_TEXT, font=(FONT_UI, 8))
            cv.create_text(14, y, text=f"{jac:.1f}%", anchor="w", fill=ccol, font=(FONT_UI, 8, "bold"))

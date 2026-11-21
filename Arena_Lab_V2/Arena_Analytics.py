# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Standalone Offline Analytics & Balance Viewer (Tkinter Desktop App).
Reads saved logs/models independently without impacting training speed.
All UI text is strictly English.
"""

from __future__ import annotations

import csv
import json
import os
import sys
import tkinter as tk
from collections import Counter, defaultdict
from tkinter import filedialog, messagebox, ttk
from typing import Any, Dict, List, Tuple

ROOT_DIR = os.path.abspath(os.path.dirname(__file__))
if ROOT_DIR not in sys.path:
    sys.path.insert(0, ROOT_DIR)

from engine.venv_bootstrap import ensure_venv

ensure_venv(__file__, require_torch=False)

from engine.card_model import ELEMENTS, PLAYSTYLES, ROLES, TRIBES, get_card_db
from engine.roster_40 import ROSTER_40, get_model_spec
from trainer.stats_store import StatsStore

BG_DARK = "#12151C"
BG_PANEL = "#1B202B"
BG_INPUT = "#252C3A"
FG_TEXT = "#EAEFF7"
FG_MUTED = "#98A4B8"
ACCENT_BLUE = "#3B82F6"
ACCENT_GREEN = "#10B981"
ACCENT_GOLD = "#F59E0B"
ACCENT_RED = "#EF4444"


class ArenaAnalyticsApp:
    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title("Arena Lab V2 — Offline Analytics & Balance Viewer")
        self.root.geometry("1180x760")
        self.root.minsize(1020, 660)
        self.root.configure(bg=BG_DARK)

        self.db = get_card_db()
        self.store = StatsStore()
        self._current_card_rows: List[Tuple[Any, ...]] = []
        self._tree_sort_state: Dict[str, bool] = {}

        self._setup_styles()
        self._build_ui()
        self.refresh_all()

    @staticmethod
    def _sort_key_val(val: Any) -> Tuple[int, Any]:
        """Smart parser for Treeview column sorting (handles numbers, %, T1..T7, x2, ATK/DEF, --)."""
        s = str(val).strip()
        if not s or s == "--":
            return (1, -1e12)
        clean = s.rstrip("%").replace(",", "")
        if clean.startswith(("T", "x", "#")) and len(clean) > 1 and clean[1:].replace(".", "", 1).lstrip("-").isdigit():
            clean = clean[1:]
        if " / " in clean:
            parts = clean.split(" / ")
            if parts[0].strip().lstrip("-").isdigit():
                return (0, float(parts[0].strip()))
        try:
            return (0, float(clean))
        except ValueError:
            return (0, s.lower())

    def _enable_heading_sort(self, tree: ttk.Treeview, headers: List[Tuple[str, str, int]]) -> None:
        """Makes every column header in `tree` 1-click sortable (toggling Descending ▼ / Ascending ▲)."""
        base_titles = {col: txt for col, txt, _ in headers}

        def _sort_by(col: str) -> None:
            key_id = f"{id(tree)}:{col}"
            desc = self._tree_sort_state.get(key_id, True)
            self._tree_sort_state[key_id] = not desc

            items = [(tree.set(k, col), k) for k in tree.get_children("")]
            # Always keep empty '--' rows at the bottom regardless of asc/desc
            valid = [(ArenaAnalyticsApp._sort_key_val(v)[1], k) for v, k in items if str(v).strip() not in ("", "--")]
            empty = [k for v, k in items if str(v).strip() in ("", "--")]
            try:
                valid.sort(key=lambda x: x[0], reverse=desc)
            except TypeError:
                valid.sort(key=lambda x: str(x[0]), reverse=desc)

            ordered = [k for _, k in valid] + empty
            for idx, k in enumerate(ordered):
                tree.move(k, "", idx)

            arrow = " ▼" if desc else " ▲"
            for c_id, orig_txt in base_titles.items():
                tree.heading(c_id, text=(orig_txt + arrow) if c_id == col else orig_txt)

        for col, txt, w in headers:
            tree.heading(col, text=txt, command=lambda c=col: _sort_by(c))

    def _setup_styles(self) -> None:
        style = ttk.Style()
        try:
            style.theme_use("clam")
        except Exception:
            pass
        # High-contrast dark Combobox styling (fixes clam readonly light-gray background!)
        style.configure(
            "TCombobox",
            fieldbackground="#1E2636",
            background="#2B3548",
            foreground="#FFFFFF",
            arrowcolor="#F59E0B",
            bordercolor="#3B475E",
            padding=4,
        )
        style.map(
            "TCombobox",
            fieldbackground=[("readonly", "#1E2636"), ("focus", "#243044")],
            foreground=[("readonly", "#FFFFFF"), ("focus", "#FFFFFF")],
            selectbackground=[("readonly", "#1E2636")],
            selectforeground=[("readonly", "#FDE047")],
            background=[("active", "#36445C")],
        )
        self.root.option_add("*TCombobox*Listbox*Background", "#1B2230")
        self.root.option_add("*TCombobox*Listbox*Foreground", "#FFFFFF")
        self.root.option_add("*TCombobox*Listbox*selectBackground", "#3B82F6")
        self.root.option_add("*TCombobox*Listbox*selectForeground", "#FFFFFF")
        self.root.option_add("*TCombobox*Listbox*Font", ("Segoe UI", 9))
        style.configure("TNotebook", background=BG_DARK, borderwidth=0)
        style.configure("TNotebook.Tab", background=BG_PANEL, foreground=FG_MUTED, padding=(14, 7), font=("Segoe UI", 9, "bold"))
        style.map("TNotebook.Tab", background=[("selected", ACCENT_BLUE)], foreground=[("selected", "#FFFFFF")])
        style.configure(
            "Treeview",
            background=BG_PANEL,
            foreground=FG_TEXT,
            fieldbackground=BG_PANEL,
            rowheight=24,
            font=("Segoe UI", 9),
        )
        style.configure("Treeview.Heading", background=BG_INPUT, foreground=ACCENT_GOLD, font=("Segoe UI", 9, "bold"))
        style.map("Treeview", background=[("selected", "#2E405E")])

    def _build_ui(self) -> None:
        # Top Header
        hdr = tk.Frame(self.root, bg=BG_PANEL, padx=16, pady=10)
        hdr.pack(fill="x", padx=10, pady=(10, 4))

        left = tk.Frame(hdr, bg=BG_PANEL)
        left.pack(side="left", fill="x", expand=True)
        tk.Label(
            left, text="ARENA LAB V2 — OFFLINE ANALYTICS & BALANCE REPORT",
            bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 14, "bold"),
        ).pack(anchor="w")
        self.summary_var = tk.StringVar(value="Loading statistics...")
        tk.Label(left, textvariable=self.summary_var, bg=BG_PANEL, fg=FG_MUTED, font=("Segoe UI", 9)).pack(anchor="w")

        tk.Button(
            hdr, text="Refresh Data", command=self.refresh_all,
            bg=ACCENT_BLUE, fg="#FFFFFF", relief="flat",
            font=("Segoe UI", 10, "bold"), padx=14, pady=6, cursor="hand2",
        ).pack(side="right")

        # Notebook Tabs
        self.nb = ttk.Notebook(self.root)
        self.nb.pack(fill="both", expand=True, padx=10, pady=(4, 10))

        self._build_tab_leaderboard()
        self._build_tab_deck_inspector()
        self._build_tab_card_balance()
        self._build_tab_archetype_balance()
        self._build_tab_excitement_anomalies()
        self._build_tab_head_to_head()
        self._build_tab_passive_skill_meta()

    # ------------------------------------------------------------------
    # Tab 1: 41-Model Leaderboard
    # ------------------------------------------------------------------
    def _build_tab_leaderboard(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text="1. 41-Model Leaderboard")

        bar = tk.Frame(tab, bg=BG_PANEL, padx=12, pady=8)
        bar.pack(fill="x", padx=8, pady=6)
        tk.Label(bar, text="Filter Category:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 9, "bold")).pack(side="left")
        self.lb_cat_var = tk.StringVar(value="All")
        cb = ttk.Combobox(
            bar, textvariable=self.lb_cat_var,
            values=["All", "Playstyle", "Environment", "Tribe", "Free", "Anomaly"], state="readonly", width=15,
        )
        cb.pack(side="left", padx=8)
        cb.bind("<<ComboboxSelected>>", lambda _e: self._populate_leaderboard())

        tk.Label(bar, text="Sort By:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 9, "bold")).pack(side="left", padx=(14, 0))
        self.lb_sort_var = tk.StringVar(value="ELO (High -> Low)")
        scb = ttk.Combobox(
            bar, textvariable=self.lb_sort_var,
            values=[
                "ELO (High -> Low)", "Win % (High -> Low)", "Matches (High -> Low)",
                "Excitement (High -> Low)", "Style Score (High -> Low)", "Model Name",
            ],
            state="readonly", width=24,
        )
        scb.pack(side="left", padx=8)
        scb.bind("<<ComboboxSelected>>", lambda _e: self._populate_leaderboard())

        cols = ("rank", "model", "category", "matches", "wr", "elo", "excite", "style", "turns", "hand_se", "empty_h", "face_dmg", "healed", "kills", "spells", "actives", "ws")
        self.lb_tree = ttk.Treeview(tab, columns=cols, show="headings")
        headers = [
            ("rank", "#", 30), ("model", "Model", 195), ("category", "Category", 74),
            ("matches", "Matches", 60), ("wr", "Win %", 58), ("elo", "ELO", 60),
            ("excite", "Excite%", 58), ("style", "Style", 50), ("turns", "Turns", 50),
            ("hand_se", "Hand S/E", 68), ("empty_h", "Empty%", 58),
            ("face_dmg", "Face Dmg", 64), ("healed", "Healed", 58), ("kills", "Kills", 48),
            ("spells", "Spells", 50), ("actives", "Actives", 54), ("ws", "WarmStart", 66),
        ]
        for c, txt, w in headers:
            self.lb_tree.heading(c, text=txt)
            self.lb_tree.column(c, width=w, anchor="w" if c in ("model", "category") else "center")
        self._enable_heading_sort(self.lb_tree, headers)

        vsb = ttk.Scrollbar(tab, orient="vertical", command=self.lb_tree.yview)
        self.lb_tree.configure(yscrollcommand=vsb.set)
        vsb.pack(side="right", fill="y", pady=6)
        self.lb_tree.pack(fill="both", expand=True, padx=(8, 0), pady=6)

    def _populate_leaderboard(self) -> None:
        self.lb_tree.delete(*self.lb_tree.get_children())
        cat = self.lb_cat_var.get()
        rows = [r for r in self.store.roster.values() if cat == "All" or r["category"] == cat]

        sort_key = self.lb_sort_var.get()
        if sort_key.startswith("ELO"):
            rows.sort(key=lambda r: (r["elo"], r["matches"]), reverse=True)
        elif sort_key.startswith("Win"):
            rows.sort(key=lambda r: (r["wins"] / max(1, r["matches"]), r["matches"]), reverse=True)
        elif sort_key.startswith("Matches"):
            rows.sort(key=lambda r: (r["matches"], r["elo"]), reverse=True)
        elif sort_key.startswith("Excitement"):
            rows.sort(key=lambda r: (r.get("avg_excitement", 0.0), r["matches"]), reverse=True)
        elif sort_key.startswith("Style"):
            rows.sort(key=lambda r: (r["avg_style_score"], r["elo"]), reverse=True)
        else:
            rows.sort(key=lambda r: r["display_name"])

        for rank, r in enumerate(rows, 1):
            m = r["matches"]
            wr = f"{100.0 * r['wins'] / m:.1f}%" if m > 0 else "--"
            exc = f"{r.get('avg_excitement', 0.0):.0f}%" if m > 0 else "--"
            hand_se = f"{r.get('avg_hand_start', 0.0):.1f}/{r.get('avg_hand_end', 0.0):.1f}" if m > 0 else "--"
            empty_h = f"{r.get('empty_hand_pct', 0.0):.0f}%" if m > 0 else "--"
            self.lb_tree.insert(
                "", "end",
                values=(
                    rank, r["display_name"], r["category"], m, wr, f"{r['elo']:.1f}",
                    exc, f"{r['avg_style_score']:.2f}", f"{r['avg_turns']:.1f}",
                    hand_se, empty_h,
                    f"{r.get('avg_dmg_face', 0.0):.0f}", f"{r.get('avg_healed', 0.0):.0f}",
                    f"{r.get('avg_kills', 0.0):.1f}", f"{r.get('avg_spells', 0.0):.1f}",
                    f"{r.get('avg_actives', 0.0):.1f}",
                    "Yes" if r.get("warm_started") else "No",
                ),
            )

    # ------------------------------------------------------------------
    # Tab 2: Settled 50-Card Deck Inspector & Free-10 Disjoint Check
    # ------------------------------------------------------------------
    def _build_tab_deck_inspector(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text="2. Settled Deck Inspector")

        top = tk.Frame(tab, bg=BG_PANEL, padx=12, pady=10)
        top.pack(fill="x", padx=8, pady=6)
        tk.Label(top, text="Select Model:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 9, "bold")).pack(side="left")
        self.deck_model_var = tk.StringVar(value=ROSTER_40[0].display_name)
        self._deck_name_to_id = {m.display_name: m.model_id for m in ROSTER_40}
        cb = ttk.Combobox(
            top, textvariable=self.deck_model_var, values=list(self._deck_name_to_id.keys()),
            state="readonly", width=34,
        )
        cb.pack(side="left", padx=8)
        cb.bind("<<ComboboxSelected>>", lambda _e: self._populate_deck_inspector())

        self.deck_meta_var = tk.StringVar(value="")
        tk.Label(top, textvariable=self.deck_meta_var, bg=BG_PANEL, fg=ACCENT_GOLD, font=("Segoe UI", 9, "bold")).pack(side="left", padx=12)

        self.free10_check_var = tk.StringVar(value="Free-10 Disjointness: Checking...")
        self.free10_label = tk.Label(
            tab, textvariable=self.free10_check_var, bg=BG_INPUT, fg=ACCENT_GREEN,
            font=("Segoe UI", 9, "bold"), padx=12, pady=6, anchor="w",
        )
        self.free10_label.pack(fill="x", padx=8, pady=(0, 6))

        cols = ("cid", "copies", "name", "type", "elem", "tribe", "style", "role", "tier", "mana", "stats", "played", "play_wr", "draft_wr")
        self.deck_tree = ttk.Treeview(tab, columns=cols, show="headings")
        d_headers = [
            ("cid", "ID", 48), ("copies", "Qty", 42), ("name", "Card Name", 215), ("type", "Type", 78),
            ("elem", "Element", 72), ("tribe", "Tribe", 78), ("style", "Style", 74),
            ("role", "Role", 76), ("tier", "Tier", 44), ("mana", "Mana", 46), ("stats", "ATK / DEF", 76),
            ("played", "Played", 64), ("play_wr", "Play WR%", 78), ("draft_wr", "Draft WR%", 80),
        ]
        for c, txt, w in d_headers:
            self.deck_tree.heading(c, text=txt)
            self.deck_tree.column(c, width=w, anchor="w" if c == "name" else "center")
        self._enable_heading_sort(self.deck_tree, d_headers)

        vsb = ttk.Scrollbar(tab, orient="vertical", command=self.deck_tree.yview)
        self.deck_tree.configure(yscrollcommand=vsb.set)
        vsb.pack(side="right", fill="y", pady=6)
        self.deck_tree.pack(fill="both", expand=True, padx=(8, 0), pady=6)

    def _populate_deck_inspector(self) -> None:
        self.deck_tree.delete(*self.deck_tree.get_children())
        # Verify Free-10 disjointness globally
        seen: Dict[int, str] = {}
        overlaps = 0
        settled_free = 0
        for mid, d_ids in self.store.free10_decks.items():
            if not d_ids:
                continue
            settled_free += 1
            for cid in set(d_ids):
                if cid in seen:
                    overlaps += 1
                else:
                    seen[cid] = mid
        if settled_free == 0:
            self.free10_check_var.set("Free-10 Overlap Check: No Free models have settled decks yet.")
            self.free10_label.configure(fg=FG_MUTED)
        elif overlaps == 0:
            self.free10_check_var.set(
                f"Free-10 Overlap Check: PASS — 0 shared cards across {settled_free} settled Free decks ({len(seen)} unique cards claimed)."
            )
            self.free10_label.configure(fg=ACCENT_GREEN)
        else:
            self.free10_check_var.set(f"Free-10 Overlap Check: WARNING — {overlaps} overlapping card IDs detected!")
            self.free10_label.configure(fg=ACCENT_RED)

        mid = self._deck_name_to_id.get(self.deck_model_var.get(), ROSTER_40[0].model_id)
        rec = self.store.roster[mid]
        deck = rec.get("settled_deck") or []
        if not deck:
            self.deck_meta_var.set("No settled deck saved yet — train this model first.")
            return

        cnts = Counter(deck)
        type_cnts = Counter(self.db.get(cid).type for cid in deck)
        avg_mana = sum(self.db.get(cid).mana for cid in deck) / len(deck)
        self.deck_meta_var.set(
            f"Total: {len(deck)} cards ({len(cnts)} unique) | Monsters: {type_cnts['monster']} | "
            f"Spells: {type_cnts['spell']} | Avg Mana: {avg_mana:.2f}  (Click any column header to sort!)"
        )

        m_cstats = self.store.model_card_stats.get(mid, {})
        deck_rows: List[Tuple[float, Tuple[Any, ...]]] = []
        for cid, qty in cnts.items():
            c = self.db.get(cid)
            stats = f"{c.atk} / {c.deff}" if c.type == "monster" else "--"
            k_str = str(cid)
            if k_str in m_cstats and m_cstats[k_str][0] > 0:
                dr, dw, pl, pw = m_cstats[k_str]
            else:
                gst = self.store.card_stats.get(k_str, {})
                dr = int(gst.get("drafted", 0))
                dw = int(gst.get("wins", 0))
                pl = int(gst.get("played", 0))
                pw = int(gst.get("played_wins", 0))
            pwr = (100.0 * pw / pl) if pl > 0 else -1.0
            dwr = (100.0 * dw / dr) if dr > 0 else -1.0
            row_vals = (
                c.id, f"x{qty}", c.name, c.type.title(), c.element_name,
                c.tribe_name, c.playstyle_name, c.role_name, f"T{c.tier}" if c.tier else "--",
                c.mana, stats, pl,
                f"{pwr:.1f}%" if pwr >= 0 else "--",
                f"{dwr:.1f}%" if dwr >= 0 else "--",
            )
            sort_score = pwr if pwr >= 0 else dwr
            deck_rows.append((sort_score, row_vals))

        # Sort by highest winning card in deck first so top MVP card is immediately visible!
        deck_rows.sort(key=lambda x: x[0], reverse=True)
        for _, rv in deck_rows:
            self.deck_tree.insert("", "end", values=rv)

    # ------------------------------------------------------------------
    # Tab 3: Card Balance & OP / Weak Report (All 2,270 Cards)
    # ------------------------------------------------------------------
    def _build_tab_card_balance(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text="3. Card Balance (2,270 Cards)")

        bar = tk.Frame(tab, bg=BG_PANEL, padx=10, pady=8)
        bar.pack(fill="x", padx=8, pady=6)

        tk.Label(bar, text="View:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 9, "bold")).pack(side="left")
        self.cb_mode_var = tk.StringVar(value="Top OP (Played Win %)")
        m_cb = ttk.Combobox(
            bar, textvariable=self.cb_mode_var,
            values=["Top OP (Played Win %)", "Weak / Underperforming", "Most Drafted", "Never Drafted", "All 2,270 Cards"],
            state="readonly", width=22,
        )
        m_cb.pack(side="left", padx=6)
        m_cb.bind("<<ComboboxSelected>>", lambda _e: self._populate_card_balance())

        tk.Label(bar, text="Type:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 9)).pack(side="left", padx=(8, 2))
        self.cb_type_var = tk.StringVar(value="All")
        t_cb = ttk.Combobox(
            bar, textvariable=self.cb_type_var,
            values=["All", "monster", "spell", "covenant", "leader", "environment"],
            state="readonly", width=11,
        )
        t_cb.pack(side="left", padx=4)
        t_cb.bind("<<ComboboxSelected>>", lambda _e: self._populate_card_balance())

        tk.Label(bar, text="Min Drafts:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 9)).pack(side="left", padx=(8, 2))
        self.cb_min_var = tk.StringVar(value="5")
        tk.Entry(bar, textvariable=self.cb_min_var, width=5, bg=BG_INPUT, fg=FG_TEXT, relief="flat", justify="center").pack(side="left", padx=4)

        tk.Label(bar, text="Search:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 9)).pack(side="left", padx=(8, 2))
        self.cb_search_var = tk.StringVar(value="")
        tk.Entry(bar, textvariable=self.cb_search_var, width=18, bg=BG_INPUT, fg=FG_TEXT, relief="flat").pack(side="left", padx=4)

        tk.Button(
            bar, text="Apply", command=self._populate_card_balance,
            bg=ACCENT_BLUE, fg="#FFFFFF", relief="flat", font=("Segoe UI", 8, "bold"), padx=10, pady=3, cursor="hand2",
        ).pack(side="left", padx=6)

        tk.Button(
            bar, text="Export CSV", command=self._export_cards_csv,
            bg=ACCENT_GREEN, fg="#0B1215", relief="flat", font=("Segoe UI", 8, "bold"), padx=10, pady=3, cursor="hand2",
        ).pack(side="right")

        cols = ("cid", "name", "type", "elem", "tribe", "style", "tier", "mana", "drafted", "draft_wr", "played", "play_wr")
        self.card_tree = ttk.Treeview(tab, columns=cols, show="headings")
        c_headers = [
            ("cid", "ID", 52), ("name", "Card Name", 230), ("type", "Type", 82),
            ("elem", "Element", 75), ("tribe", "Tribe", 82), ("style", "Style", 80),
            ("tier", "Tier", 45), ("mana", "Mana", 48), ("drafted", "Drafted", 70),
            ("draft_wr", "Draft WR%", 82), ("played", "Played", 68), ("play_wr", "Played WR%", 86),
        ]
        for c, txt, w in c_headers:
            self.card_tree.heading(c, text=txt)
            self.card_tree.column(c, width=w, anchor="w" if c == "name" else "center")
        self._enable_heading_sort(self.card_tree, c_headers)

        vsb = ttk.Scrollbar(tab, orient="vertical", command=self.card_tree.yview)
        self.card_tree.configure(yscrollcommand=vsb.set)
        vsb.pack(side="right", fill="y", pady=6)
        self.card_tree.pack(fill="both", expand=True, padx=(8, 0), pady=6)

    def _populate_card_balance(self) -> None:
        self.card_tree.delete(*self.card_tree.get_children())
        mode = self.cb_mode_var.get()
        ftype = self.cb_type_var.get()
        q = self.cb_search_var.get().strip().lower()
        try:
            min_d = max(0, int(self.cb_min_var.get().strip()))
        except ValueError:
            min_d = 0

        items: List[Tuple[float, Tuple[Any, ...]]] = []
        for cid in self.db.all_ids:
            c = self.db.get(cid)
            if ftype != "All" and c.type != ftype:
                continue
            if q and (q not in c.name.lower() and q != str(c.id) and q not in c.element_name.lower() and q not in c.tribe_name.lower()):
                continue
            st = self.store.card_stats.get(str(cid), {})
            drafted = int(st.get("drafted", 0))
            wins = int(st.get("wins", 0))
            played = int(st.get("played", 0))
            pwins = int(st.get("played_wins", 0))

            if mode == "Never Drafted" and drafted > 0:
                continue
            if mode in ("Top OP (Played Win %)", "Weak / Underperforming") and drafted < min_d:
                continue

            dwr = (100.0 * wins / drafted) if drafted > 0 else 0.0
            pwr = (100.0 * pwins / played) if played > 0 else 0.0
            row = (
                c.id, c.name, c.type.title(), c.element_name, c.tribe_name, c.playstyle_name,
                f"T{c.tier}" if c.tier else "--", c.mana, drafted,
                f"{dwr:.1f}%" if drafted > 0 else "--",
                played, f"{pwr:.1f}%" if played > 0 else "--",
            )
            if mode == "Top OP (Played Win %)":
                s_val = pwr if played >= max(1, min_d // 2) else dwr
            elif mode == "Weak / Underperforming":
                s_val = -(pwr if played >= max(1, min_d // 2) else dwr)
            elif mode == "Most Drafted":
                s_val = float(drafted)
            else:
                s_val = -float(c.id)
            items.append((s_val, row))

        items.sort(key=lambda x: x[0], reverse=True)
        self._current_card_rows = [r for _, r in items]
        for row in self._current_card_rows[:600]:
            self.card_tree.insert("", "end", values=row)

    def _export_cards_csv(self) -> None:
        if not self._current_card_rows:
            messagebox.showinfo("Export CSV", "No rows to export.")
            return
        path = filedialog.asksaveasfilename(
            title="Export Card Balance CSV",
            defaultextension=".csv",
            filetypes=[("CSV Files", "*.csv")],
            initialfile="arena_lab_v2_card_balance.csv",
        )
        if not path:
            return
        with open(path, "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            w.writerow(["CardID", "Name", "Type", "Element", "Tribe", "Playstyle", "Tier", "Mana", "Drafted", "DraftWR", "Played", "PlayedWR"])
            w.writerows(self._current_card_rows)
        messagebox.showinfo("Exported", f"Saved {len(self._current_card_rows)} rows to:\n{path}")

    # ------------------------------------------------------------------
    # Tab 4: Archetype Balance & Recent Match Log
    # ------------------------------------------------------------------
    def _build_tab_archetype_balance(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text="4. Category Balance & Recent Matches")

        top = tk.Frame(tab, bg=BG_DARK)
        top.pack(fill="both", expand=True, padx=8, pady=6)

        tk.Label(top, text="CATEGORY AGGREGATE BALANCE", bg=BG_DARK, fg=ACCENT_GOLD, font=("Segoe UI", 10, "bold")).pack(anchor="w")
        c_cols = ("cat", "models_trained", "matches", "avg_wr", "avg_elo", "top_model")
        self.cat_tree = ttk.Treeview(top, columns=c_cols, show="headings", height=6)
        cat_headers = [
            ("cat", "Category", 130), ("models_trained", "Trained Models", 120),
            ("matches", "Total Matches", 120), ("avg_wr", "Avg Win %", 110),
            ("avg_elo", "Avg ELO", 110), ("top_model", "Top Performing Model", 320),
        ]
        for c, txt, w in cat_headers:
            self.cat_tree.heading(c, text=txt)
            self.cat_tree.column(c, width=w, anchor="w" if c in ("cat", "top_model") else "center")
        self._enable_heading_sort(self.cat_tree, cat_headers)
        self.cat_tree.pack(fill="x", pady=(4, 12))

        tk.Label(top, text="RECENT MATCHES (LATEST 100)", bg=BG_DARK, fg=ACCENT_GOLD, font=("Segoe UI", 10, "bold")).pack(anchor="w")
        h_cols = ("ts", "ma", "mb", "winner", "turns", "hp", "reason")
        self.hist_tree = ttk.Treeview(top, columns=h_cols, show="headings")
        h_headers = [
            ("ts", "Timestamp", 145), ("ma", "Model A", 160), ("mb", "Model B", 160),
            ("winner", "Winner", 150), ("turns", "Turns", 65), ("hp", "Final HP (A vs B)", 130), ("reason", "Win Reason", 140),
        ]
        for c, txt, w in h_headers:
            self.hist_tree.heading(c, text=txt)
            self.hist_tree.column(c, width=w, anchor="center")
        self._enable_heading_sort(self.hist_tree, h_headers)
        vsb = ttk.Scrollbar(top, orient="vertical", command=self.hist_tree.yview)
        self.hist_tree.configure(yscrollcommand=vsb.set)
        vsb.pack(side="right", fill="y", pady=4)
        self.hist_tree.pack(fill="both", expand=True, pady=4)

    def _populate_archetype_balance(self) -> None:
        self.cat_tree.delete(*self.cat_tree.get_children())
        groups: Dict[str, List[Dict[str, Any]]] = defaultdict(list)
        for r in self.store.roster.values():
            groups[r["category"]].append(r)

        for cat in ("Playstyle", "Environment", "Tribe", "Free", "Anomaly"):
            recs = groups[cat]
            if not recs:
                continue
            trained = [r for r in recs if r["matches"] > 0]
            tot_m = sum(r["matches"] for r in recs)
            tot_w = sum(r["wins"] for r in recs)
            avg_wr = f"{100.0 * tot_w / tot_m:.1f}%" if tot_m > 0 else "--"
            avg_elo = f"{sum(r['elo'] for r in recs) / len(recs):.1f}"
            best = max(recs, key=lambda r: (r["elo"], r["matches"]))
            best_str = f"{best['display_name']} (ELO {best['elo']:.0f}, {best['matches']}m)" if best["matches"] > 0 else "--"
            self.cat_tree.insert("", "end", values=(cat, f"{len(trained)} / {len(recs)}", tot_m, avg_wr, avg_elo, best_str))

        # Recent match history
        self.hist_tree.delete(*self.hist_tree.get_children())
        if os.path.isfile(self.store.history_path):
            try:
                with open(self.store.history_path, "r", encoding="utf-8") as f:
                    lines = f.readlines()[-100:]
                for ln in reversed(lines):
                    row = json.loads(ln)
                    self.hist_tree.insert(
                        "", "end",
                        values=(
                            row.get("ts", ""), row.get("model_a", ""), row.get("model_b", ""),
                            row.get("winner", ""), row.get("turns", ""),
                            f"{row.get('hp_a', 0)} vs {row.get('hp_b', 0)}", row.get("reason", ""),
                        ),
                    )
            except Exception:
                pass

    # ------------------------------------------------------------------
    # Tab 5: Excitement & Anomaly Lab
    # ------------------------------------------------------------------
    def _build_tab_excitement_anomalies(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text="5. Excitement & Anomaly Lab")

        self.exc_banner_var = tk.StringVar(value="Excitement Telemetry: Waiting for matches...")
        tk.Label(
            tab, textvariable=self.exc_banner_var, bg=BG_INPUT, fg=ACCENT_GOLD,
            font=("Segoe UI", 10, "bold"), padx=14, pady=8, anchor="w",
        ).pack(fill="x", padx=8, pady=(8, 6))

        body = tk.Frame(tab, bg=BG_DARK)
        body.pack(fill="both", expand=True, padx=8, pady=4)
        body.columnconfigure(0, weight=5)
        body.columnconfigure(1, weight=6)
        body.rowconfigure(0, weight=3)
        body.rowconfigure(1, weight=2)

        # Top-Left: Turn-by-Turn Excitement Heatmap (Turn 1..25)
        tl = tk.Frame(body, bg=BG_DARK)
        tl.grid(row=0, column=0, sticky="nsew", padx=(0, 6), pady=(0, 6))
        tk.Label(
            tl, text="TURN-BY-TURN EXCITEMENT CURVE (WHEN DOES THE ACTION PEAK?)",
            bg=BG_DARK, fg="#22D3EE", font=("Segoe UI", 9, "bold"),
        ).pack(anchor="w")

        t_cols = ("turn", "active", "avg_dmg", "hand_se", "empty_h", "kills", "swings", "bursts", "finishes")
        self.turn_tree = ttk.Treeview(tl, columns=t_cols, show="headings", height=12)
        t_headers = [
            ("turn", "Turn", 44), ("active", "Matches", 60), ("avg_dmg", "Avg Dmg", 64),
            ("hand_se", "Hand S/E", 68), ("empty_h", "Empty%", 58),
            ("kills", "Kills", 52), ("swings", "Swings", 54), ("bursts", "100+ Burst", 70),
            ("finishes", "Finishes", 60),
        ]
        for c, txt, w in t_headers:
            self.turn_tree.heading(c, text=txt)
            self.turn_tree.column(c, width=w, anchor="center")
        self._enable_heading_sort(self.turn_tree, t_headers)
        tsb = ttk.Scrollbar(tl, orient="vertical", command=self.turn_tree.yview)
        self.turn_tree.configure(yscrollcommand=tsb.set)
        tsb.pack(side="right", fill="y")
        self.turn_tree.pack(fill="both", expand=True)

        # Bottom-Left: In-Game Highlight Moments Breakdown
        bl = tk.Frame(body, bg=BG_DARK)
        bl.grid(row=1, column=0, sticky="nsew", padx=(0, 6))
        tk.Label(
            bl, text="IN-GAME HIGHLIGHT & DRAMA MOMENTS",
            bg=BG_DARK, fg=ACCENT_GREEN, font=("Segoe UI", 9, "bold"),
        ).pack(anchor="w")

        hl_cols = ("event", "count", "rate")
        self.hl_tree = ttk.Treeview(bl, columns=hl_cols, show="headings", height=7)
        hl_headers = [("event", "Highlight / Thrill Event", 260), ("count", "Total Triggers", 110), ("rate", "Per 100 Matches", 120)]
        for c, txt, w in hl_headers:
            self.hl_tree.heading(c, text=txt)
            self.hl_tree.column(c, width=w, anchor="w" if c == "event" else "center")
        self._enable_heading_sort(self.hl_tree, hl_headers)
        self.hl_tree.pack(fill="both", expand=True)

        # Right Column (Full Height): Discovered Card Anomalies & Exploit Combos
        rt = tk.Frame(body, bg=BG_DARK)
        rt.grid(row=0, column=1, rowspan=2, sticky="nsew", padx=(6, 0))
        tk.Label(
            rt, text="DISCOVERED CARD ANOMALIES & EXPLOIT COMBOS (HUNTED BY GLITCH_41 & ALL MODELS)",
            bg=BG_DARK, fg=ACCENT_GOLD, font=("Segoe UI", 9, "bold"),
        ).pack(anchor="w")

        a_cols = ("type", "winner", "turn", "detail", "combo")
        self.anom_tree = ttk.Treeview(rt, columns=a_cols, show="headings")
        a_headers = [
            ("type", "Anomaly Type", 125), ("winner", "Winner vs Loser", 145),
            ("turn", "Turn", 46), ("detail", "Peak Stat / Burst", 175),
            ("combo", "Top 3 Combo Cards Played", 230),
        ]
        for c, txt, w in a_headers:
            self.anom_tree.heading(c, text=txt)
            self.anom_tree.column(c, width=w, anchor="w" if c in ("detail", "combo") else "center")
        self._enable_heading_sort(self.anom_tree, a_headers)
        asb = ttk.Scrollbar(rt, orient="vertical", command=self.anom_tree.yview)
        self.anom_tree.configure(yscrollcommand=asb.set)
        asb.pack(side="right", fill="y")
        self.anom_tree.pack(fill="both", expand=True)

    def _populate_excitement_anomalies(self) -> None:
        self.turn_tree.delete(*self.turn_tree.get_children())
        self.hl_tree.delete(*self.hl_tree.get_children())
        self.anom_tree.delete(*self.anom_tree.get_children())

        ex = self.store.excitement
        tm = int(ex.get("total_matches", 0))
        cb_w = int(ex.get("comeback_wins", 0))
        cl_w = int(ex.get("clutch_wins", 0))
        swings = int(ex.get("total_lead_swings", 0))
        avg_exc = float(ex.get("avg_excitement", 0.0))
        anoms = ex.get("anomalies", [])

        if tm > 0:
            self.exc_banner_var.set(
                f"Global Excitement Index: {avg_exc:.1f} / 100  |  "
                f"Comeback Wins (-100+ HP): {cb_w} ({100.0 * cb_w / tm:.1f}%)  |  "
                f"Clutch Finishes (<=75 HP): {cl_w} ({100.0 * cl_w / tm:.1f}%)  |  "
                f"Avg Lead Reversals: {swings / tm:.2f}/match  |  "
                f"Anomalies Logged: {len(anoms)}"
            )
        else:
            self.exc_banner_var.set(
                "Excitement & Anomaly Lab Ready — Run new matches (or Glitch_41) to populate turn curves, comebacks & combo anomalies!"
            )

        tc = ex.get("turn_curve", {})
        for t in range(1, 26):
            b = tc.get(str(t), {})
            t_cnt = int(b.get("turns", 0))
            dmg = int(b.get("dmg", 0))
            avg_d = f"{dmg / t_cnt:.1f}" if t_cnt > 0 else "--"
            hs = int(b.get("hs", 0))
            he = int(b.get("he", 0))
            hempty = int(b.get("hempty", 0))
            hand_se = f"{hs / t_cnt:.1f}/{he / t_cnt:.1f}" if (t_cnt > 0 and (hs > 0 or he > 0)) else "--"
            emp_s = f"{100.0 * hempty / t_cnt:.0f}%" if (t_cnt > 0 and (hs > 0 or he > 0)) else "--"
            self.turn_tree.insert(
                "", "end",
                values=(
                    f"T{t}", t_cnt, avg_d, hand_se, emp_s, int(b.get("kills", 0)),
                    int(b.get("swings", 0)), int(b.get("bursts", 0)),
                    int(b.get("finishes", 0)),
                ),
            )

        hl_map = ex.get("highlights", {})
        friendly_names = {
            "Comeback_120HP": "Miracle Comeback Win (-120+ HP Overcome)",
            "Clutch_Finish": "Clutch Nail-Biter Win (<= 75 HP Remaining)",
            "Mega_Burst_Turn": "Mega Damage Burst Turn (120+ Dmg in 1 Turn)",
            "Multi_Kill_Sweep": "Board Wipe / Multi-Kill Turn (3+ Kills)",
            "Undying_1DEF_Save": "Undying 1-DEF Lethal Save",
            "Hydra_Split": "Hydra Split Clone Trigger",
            "Ascend_Evolution": "Ascend In-Battle Evolution",
            "Counter_Tech_Break": "Counter Tech Break (Betrayal/Oblivion/Exorcism)",
            "Time_Rewind_Miracle": "Time Rewind Miracle Reset",
            "Polymorph_Sheep": "Polymorph Boss-to-Sheep Transform",
            "Stat_Flip_Surprise": "Stat Flip Surprise Swing",
        }
        all_keys = list(friendly_names.keys()) + [k for k in hl_map if k not in friendly_names]
        for k in all_keys:
            cnt = int(hl_map.get(k, 0))
            rate = f"{100.0 * cnt / tm:.1f}" if tm > 0 else "--"
            self.hl_tree.insert("", "end", values=(friendly_names.get(k, k), cnt, rate))

        for a in anoms:
            cids = a.get("cards", [])
            c_str = " + ".join(f"#{cid} {self.db.get(cid).name.split('#')[0].strip()}" for cid in cids if cid in self.db.cards)
            self.anom_tree.insert(
                "", "end",
                values=(
                    a.get("type", ""),
                    f"{a.get('winner', '')} > {a.get('loser', '')}",
                    f"T{a.get('turn', '')}",
                    a.get("detail", ""),
                    c_str or "--",
                ),
            )

    # ------------------------------------------------------------------
    # Tab 6: Head-to-Head Matchup Matrix (Model A vs Model B Win Rates)
    # ------------------------------------------------------------------
    def _build_tab_head_to_head(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text="6. Head-to-Head Matchups")

        bar = tk.Frame(tab, bg=BG_PANEL, padx=12, pady=8)
        bar.pack(fill="x", padx=8, pady=6)

        tk.Label(bar, text="Filter Model:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 9, "bold")).pack(side="left")
        self.h2h_model_var = tk.StringVar(value="All Models")
        model_opts = ["All Models"] + [m.display_name for m in ROSTER_40]
        m_cb = ttk.Combobox(bar, textvariable=self.h2h_model_var, values=model_opts, state="readonly", width=30)
        m_cb.pack(side="left", padx=8)
        m_cb.bind("<<ComboboxSelected>>", lambda _e: self._populate_head_to_head())

        tk.Label(bar, text="Filter Category:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 9, "bold")).pack(side="left", padx=(12, 0))
        self.h2h_cat_var = tk.StringVar(value="All Pairs")
        c_cb = ttk.Combobox(
            bar, textvariable=self.h2h_cat_var,
            values=["All Pairs", "Environment vs Environment", "Tribe vs Tribe", "Playstyle vs Playstyle", "Free vs Free", "Cross-Category Only"],
            state="readonly", width=26,
        )
        c_cb.pack(side="left", padx=8)
        c_cb.bind("<<ComboboxSelected>>", lambda _e: self._populate_head_to_head())

        self.h2h_meta_var = tk.StringVar(value="")
        tk.Label(bar, textvariable=self.h2h_meta_var, bg=BG_PANEL, fg=ACCENT_GOLD, font=("Segoe UI", 9, "bold")).pack(side="right")

        cols = ("ma", "mb", "matches", "wa", "wb", "draws", "wra", "wrb", "turns", "verdict")
        self.h2h_tree = ttk.Treeview(tab, columns=cols, show="headings")
        h2h_headers = [
            ("ma", "Model A (Focus)", 220), ("mb", "Model B (Opponent)", 220),
            ("matches", "Matches", 75), ("wa", "A Wins", 68), ("wb", "B Wins", 68),
            ("draws", "Draws", 58), ("wra", "A Win %", 78), ("wrb", "B Win %", 78),
            ("turns", "Avg Turns", 76), ("verdict", "Matchup Verdict", 160),
        ]
        for c, txt, w in h2h_headers:
            self.h2h_tree.heading(c, text=txt)
            self.h2h_tree.column(c, width=w, anchor="w" if c in ("ma", "mb", "verdict") else "center")
        self._enable_heading_sort(self.h2h_tree, h2h_headers)

        vsb = ttk.Scrollbar(tab, orient="vertical", command=self.h2h_tree.yview)
        self.h2h_tree.configure(yscrollcommand=vsb.set)
        vsb.pack(side="right", fill="y", pady=6)
        self.h2h_tree.pack(fill="both", expand=True, padx=(8, 0), pady=6)

    def _populate_head_to_head(self) -> None:
        self.h2h_tree.delete(*self.h2h_tree.get_children())
        sel_model_disp = self.h2h_model_var.get()
        sel_mid = self._deck_name_to_id.get(sel_model_disp, "")
        cat_filter = self.h2h_cat_var.get()

        rows_out: List[Tuple[int, Tuple[Any, ...]]] = []
        for h_rec in self.store.head_to_head.values():
            a_id = h_rec.get("a", "")
            b_id = h_rec.get("b", "")
            m = int(h_rec.get("m", 0))
            if m <= 0 or a_id not in self.store.roster or b_id not in self.store.roster:
                continue
            wa = int(h_rec.get("wa", 0))
            wb = int(h_rec.get("wb", 0))
            dr = int(h_rec.get("d", 0))
            turns = int(h_rec.get("turns", 0))

            # If user selected a specific model (e.g. Water Domain), orient that model as Model A!
            if sel_mid:
                if sel_mid != a_id and sel_mid != b_id:
                    continue
                if sel_mid == b_id:
                    a_id, b_id = b_id, a_id
                    wa, wb = wb, wa

            ra = self.store.roster[a_id]
            rb = self.store.roster[b_id]
            ca, cb = ra["category"], rb["category"]

            if cat_filter == "Environment vs Environment" and not (ca == "Environment" and cb == "Environment"):
                continue
            if cat_filter == "Tribe vs Tribe" and not (ca == "Tribe" and cb == "Tribe"):
                continue
            if cat_filter == "Playstyle vs Playstyle" and not (ca == "Playstyle" and cb == "Playstyle"):
                continue
            if cat_filter == "Free vs Free" and not (ca == "Free" and cb == "Free"):
                continue
            if cat_filter == "Cross-Category Only" and ca == cb:
                continue

            wra = 100.0 * wa / m
            wrb = 100.0 * wb / m
            avg_t = turns / m
            if abs(wra - wrb) <= 6.0:
                verdict = "Balanced (Even)"
            elif wra > wrb:
                verdict = f"{ra['display_name'].split(']')[-1].strip()} Favored"
            else:
                verdict = f"{rb['display_name'].split(']')[-1].strip()} Favored"

            rows_out.append(
                (
                    m,
                    (
                        ra["display_name"], rb["display_name"], m, wa, wb, dr,
                        f"{wra:.1f}%", f"{wrb:.1f}%", f"{avg_t:.1f}", verdict,
                    ),
                )
            )

        rows_out.sort(key=lambda x: x[0], reverse=True)
        self.h2h_meta_var.set(f"Pairs Shown: {len(rows_out)}  (Click any column header to sort)")
        for _, rv in rows_out:
            self.h2h_tree.insert("", "end", values=rv)

    # ------------------------------------------------------------------
    # Tab 7: Passive, Active Skill & Spell Mechanic Meta Win Rates
    # ------------------------------------------------------------------
    @staticmethod
    def _format_mechanic_label(category: str, key: str) -> str:
        """Converts an internal mechanic key (e.g. 'double_strike', 'lb_burn_boom') into a clean English title."""
        overrides = {
            "all_attack": "All-Attack (Hits All Enemies)",
            "double_strike": "Double Strike (2x Attack)",
            "taunt_reflect_pct": "Taunt + % Damage Reflect",
            "taunt_evasion": "Taunt + Evasion",
            "rooted_immune_regen": "Status Immune + Flat Armor",
            "undying_1def": "Undying (Survive at 1 DEF)",
            "lb_heal_leader": "Last Breath: Heal Leader",
            "lb_dmg_leader": "Last Breath: Damage Enemy Leader",
            "lb_burn_boom": "Last Breath: AoE Burn Explosion",
            "lb_freeze": "Last Breath: Freeze Enemy",
            "lb_poison": "Last Breath: Poison Enemy",
            "lb_token": "Last Breath: Spawn Token",
            "lb_draw": "Last Breath: Draw Card",
            "lb_grave_return": "Last Breath: Return From Grave",
            "lb_clone_hand": "Last Breath: Clone To Hand",
            "lb_mana": "Last Breath: Gain Mana",
            "on_spell_grow": "Scaling: Grow On Spell Cast",
            "on_skill_grow": "Scaling: Grow On Active Skill",
            "on_death_grow": "Scaling: Grow On Unit Death",
            "on_death_regen": "On Death: Heal Allies / Leader",
            "on_hit_burn": "On-Hit: Inflict Burn",
            "on_hit_poison": "On-Hit: Inflict Poison",
            "on_hit_bleed": "On-Hit: Inflict Bleed",
            "aura_weaken_opp": "Aura: Weaken Enemy ATK",
            "turn_heal_aura": "Turn Start: Heal Allies / Leader",
            "cleanse_heal_aura": "Cleanse + Heal Aura",
            "spell_power_aura": "Aura: Bonus Spell Power",
            "pack_aura": "Aura: Pack Synergy Buff",
            "dmg_single": "Single-Target Damage",
            "aoe_enemy": "AoE Enemy Board Damage",
            "aoe_ally": "AoE Ally Board Buff / Heal",
            "cleave_enemy": "Cleave Adjacent Enemies",
            "buff_ally": "Buff Single Ally",
            "dmg_leader": "Direct Leader Burn Damage",
            "heal_leader": "Heal Own Leader HP",
            "double_skill_1t": "Double Skill Usage (1 Turn)",
            "detonate_dots": "Detonate All Burn/Poison/Bleed",
            "mind_control_temp": "Temporary Mind Control",
            "devour_3": "Devour Ally / Enemy Stats",
            "pull_hook": "Pull Enemy To Front Slot",
            "pull_hook_spell": "Pull Enemy To Front Slot",
            "perm_mana": "Permanent Max Mana Ramp",
            "perm_mana_summon": "On Summon: Permanent Max Mana",
            "temp_mana": "Temporary Mana Surge",
            "temp_mana_summon": "On Summon: Temporary Mana",
            "pure_draw": "Pure Card Draw",
            "revive_board": "Resurrect From Graveyard",
            "grave_to_hand": "Recover Card From Graveyard",
            "counter_betrayal": "Counter-Tech: Betrayal",
            "counter_stand_alone": "Counter-Tech: Stand Alone",
            "counter_oblivion": "Counter-Tech: Oblivion",
            "counter_exorcism": "Counter-Tech: Exorcism",
        }
        if key in overrides:
            return overrides[key]
        return key.replace("_", " ").title()

    def _build_tab_passive_skill_meta(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text="7. Passive & Skill Meta")

        bar = tk.Frame(tab, bg=BG_PANEL, padx=10, pady=8)
        bar.pack(fill="x", padx=8, pady=6)

        tk.Label(bar, text="Category:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 9, "bold")).pack(side="left")
        self.pm_cat_var = tk.StringVar(value="All Mechanics")
        cat_cb = ttk.Combobox(
            bar,
            textvariable=self.pm_cat_var,
            values=["All Mechanics", "Monster Passives", "Monster Active Skills", "Spell Mechanics"],
            state="readonly",
            width=22,
        )
        cat_cb.pack(side="left", padx=6)
        cat_cb.bind("<<ComboboxSelected>>", lambda _e: self._populate_passive_skill_meta())

        tk.Label(bar, text="Min Plays:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 9)).pack(side="left", padx=(8, 2))
        self.pm_min_var = tk.StringVar(value="20")
        min_cb = ttk.Combobox(
            bar,
            textvariable=self.pm_min_var,
            values=["0", "20", "50", "200", "1000"],
            state="readonly",
            width=7,
        )
        min_cb.pack(side="left", padx=4)
        min_cb.bind("<<ComboboxSelected>>", lambda _e: self._populate_passive_skill_meta())

        tk.Label(bar, text="Search:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 9)).pack(side="left", padx=(8, 2))
        self.pm_search_var = tk.StringVar(value="")
        search_ent = tk.Entry(bar, textvariable=self.pm_search_var, width=18, bg=BG_INPUT, fg=FG_TEXT, relief="flat")
        search_ent.pack(side="left", padx=4)
        search_ent.bind("<KeyRelease>", lambda _e: self._populate_passive_skill_meta())

        tk.Button(
            bar, text="Export CSV", command=self._export_mechanics_csv,
            bg=ACCENT_GREEN, fg="#0B1215", relief="flat", font=("Segoe UI", 8, "bold"), padx=10, pady=3, cursor="hand2",
        ).pack(side="right", padx=(8, 0))

        self.pm_meta_var = tk.StringVar(value="")
        tk.Label(bar, textvariable=self.pm_meta_var, bg=BG_PANEL, fg=ACCENT_GOLD, font=("Segoe UI", 9, "bold")).pack(side="right", padx=6)

        cols = (
            "cat", "name", "key", "cards", "avg_tier", "drafted",
            "played", "play_wr", "draft_wr", "delta", "top_card", "verdict",
        )
        self.pm_tree = ttk.Treeview(tab, columns=cols, show="headings")
        pm_headers = [
            ("cat", "Category", 92),
            ("name", "Mechanic", 220),
            ("key", "Internal Key", 140),
            ("cards", "Cards", 52),
            ("avg_tier", "Avg Tier", 64),
            ("drafted", "Total Drafts", 82),
            ("played", "Total Plays", 82),
            ("play_wr", "Play WR%", 74),
            ("draft_wr", "Draft WR%", 76),
            ("delta", "Play-Draft", 76),
            ("top_card", "Most Played Card", 215),
            ("verdict", "Balance Verdict", 112),
        ]
        for c, txt, w in pm_headers:
            self.pm_tree.heading(c, text=txt)
            self.pm_tree.column(c, width=w, anchor="w" if c in ("name", "key", "top_card") else "center")
        self._enable_heading_sort(self.pm_tree, pm_headers)

        self.pm_tree.tag_configure("op", foreground="#F87171")
        self.pm_tree.tag_configure("strong", foreground="#4ADE80")
        self.pm_tree.tag_configure("below", foreground="#FACC15")
        self.pm_tree.tag_configure("weak", foreground="#60A5FA")
        self.pm_tree.tag_configure("low", foreground="#94A3B8")

        vsb = ttk.Scrollbar(tab, orient="vertical", command=self.pm_tree.yview)
        self.pm_tree.configure(yscrollcommand=vsb.set)
        vsb.pack(side="right", fill="y", pady=6)
        self.pm_tree.pack(fill="both", expand=True, padx=(8, 0), pady=6)

    def _compute_passive_skill_rows(self) -> List[Tuple[float, str, Tuple[Any, ...]]]:
        """Aggregates Play WR% and Draft WR% for every Monster Passive, Active Skill, and Spell Mechanic."""
        buckets: Dict[Tuple[str, str], Dict[str, Any]] = {}

        def _add_entry(cat: str, key: str, card_obj: Any, st: Dict[str, Any]) -> None:
            bk = (cat, key)
            rec = buckets.get(bk)
            if rec is None:
                rec = {
                    "card_ids": set(),
                    "tier_sum": 0,
                    "drafted": 0,
                    "draft_wins": 0,
                    "played": 0,
                    "play_wins": 0,
                    "top_card_plays": -1,
                    "top_card_label": "--",
                }
                buckets[bk] = rec
            if card_obj.id in rec["card_ids"]:
                return
            rec["card_ids"].add(card_obj.id)
            rec["tier_sum"] += int(card_obj.tier)
            d = int(st.get("drafted", 0))
            dw = int(st.get("wins", 0))
            pl = int(st.get("played", 0))
            pw = int(st.get("played_wins", 0))
            rec["drafted"] += d
            rec["draft_wins"] += dw
            rec["played"] += pl
            rec["play_wins"] += pw
            if pl > rec["top_card_plays"]:
                rec["top_card_plays"] = pl
                rec["top_card_label"] = f"#{card_obj.id} {card_obj.name}"

        for cid in self.db.all_ids:
            c = self.db.get(cid)
            st = self.store.card_stats.get(str(cid), {})
            if c.type == "monster":
                for p in c.passives:
                    pk = str(p.get("kind", "")).strip()
                    if pk:
                        _add_entry("Passive", pk, c, st)
                for a in c.actives:
                    ak = str(a.get("kind", "")).strip()
                    if ak:
                        _add_entry("Active Skill", ak, c, st)
            elif c.type == "spell" and c.spell_ops:
                sk = str(c.spell_ops.get("kind", "")).strip()
                if sk:
                    _add_entry("Spell", sk, c, st)

        cat_filter = self.pm_cat_var.get()
        q = self.pm_search_var.get().strip().lower()
        try:
            min_plays = max(0, int(self.pm_min_var.get().strip()))
        except ValueError:
            min_plays = 0

        rows: List[Tuple[float, str, Tuple[Any, ...]]] = []
        for (cat, key), rec in buckets.items():
            if cat_filter == "Monster Passives" and cat != "Passive":
                continue
            if cat_filter == "Monster Active Skills" and cat != "Active Skill":
                continue
            if cat_filter == "Spell Mechanics" and cat != "Spell":
                continue

            played = rec["played"]
            drafted = rec["drafted"]
            if played < min_plays:
                continue

            label = self._format_mechanic_label(cat, key)
            if q and (q not in label.lower() and q not in key.lower() and q not in cat.lower() and q not in rec["top_card_label"].lower()):
                continue

            n_cards = len(rec["card_ids"])
            avg_tier = rec["tier_sum"] / max(1, n_cards)
            pwr = (100.0 * rec["play_wins"] / played) if played > 0 else -1.0
            dwr = (100.0 * rec["draft_wins"] / drafted) if drafted > 0 else -1.0
            delta = (pwr - dwr) if (pwr >= 0 and dwr >= 0) else 0.0

            if played < 20:
                verdict, tag = "Low Data", "low"
            elif pwr >= 56.0:
                verdict, tag = "OP (>56%)", "op"
            elif pwr >= 53.0:
                verdict, tag = "Strong", "strong"
            elif pwr <= 44.0:
                verdict, tag = "Weak (<44%)", "weak"
            elif pwr <= 47.0:
                verdict, tag = "Below Avg", "below"
            else:
                verdict, tag = "Balanced", ""

            row_vals = (
                cat,
                label,
                key,
                n_cards,
                f"T{avg_tier:.1f}",
                drafted,
                played,
                f"{pwr:.1f}%" if pwr >= 0 else "--",
                f"{dwr:.1f}%" if dwr >= 0 else "--",
                f"{delta:+.1f}%" if (pwr >= 0 and dwr >= 0) else "--",
                rec["top_card_label"],
                verdict,
            )
            sort_key = pwr if pwr >= 0 else -999.0
            rows.append((sort_key, tag, row_vals))

        rows.sort(key=lambda x: x[0], reverse=True)
        return rows

    def _populate_passive_skill_meta(self) -> None:
        self.pm_tree.delete(*self.pm_tree.get_children())
        rows = self._compute_passive_skill_rows()
        op_cnt = sum(1 for _, tag, _ in rows if tag == "op")
        weak_cnt = sum(1 for _, tag, _ in rows if tag == "weak")
        self.pm_meta_var.set(f"Mechanics: {len(rows)}  |  OP (>56%): {op_cnt}  |  Weak (<44%): {weak_cnt}")
        for _, tag, rv in rows:
            if tag:
                self.pm_tree.insert("", "end", values=rv, tags=(tag,))
            else:
                self.pm_tree.insert("", "end", values=rv)

    def _export_mechanics_csv(self) -> None:
        path = filedialog.asksaveasfilename(
            title="Export Passive & Skill Meta Report (CSV)",
            defaultextension=".csv",
            initialfile="arena_v2_passive_skill_meta.csv",
            filetypes=[("CSV Files", "*.csv")],
        )
        if not path:
            return
        rows = self._compute_passive_skill_rows()
        with open(path, "w", newline="", encoding="utf-8-sig") as f:
            w = csv.writer(f)
            w.writerow([
                "Category", "Mechanic", "Internal Key", "Cards", "Avg Tier",
                "Total Drafts", "Total Plays", "Play WR%", "Draft WR%",
                "Play-Draft Delta", "Most Played Card", "Balance Verdict",
            ])
            for _, _, rv in rows:
                w.writerow(rv)
        messagebox.showinfo("Export Complete", f"Exported {len(rows)} mechanics to:\n{path}")

    def refresh_all(self) -> None:
        self.store.reload()
        total_entries = sum(r["matches"] for r in self.store.roster.values()) // 2
        trained_cnt = sum(1 for r in self.store.roster.values() if r["matches"] > 0)
        cards_seen = sum(1 for v in self.store.card_stats.values() if v.get("drafted", 0) > 0)
        self.summary_var.set(
            f"Total Matches Logged: {total_entries}  |  Trained Models: {trained_cnt} / 41  |  "
            f"Unique Cards Tested: {cards_seen} / 2,270  |  (Click any table column header to sort)"
        )
        self._populate_leaderboard()
        self._populate_deck_inspector()
        self._populate_card_balance()
        self._populate_archetype_balance()
        self._populate_excitement_anomalies()
        self._populate_head_to_head()
        self._populate_passive_skill_meta()


def main() -> None:
    root = tk.Tk()
    ArenaAnalyticsApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()

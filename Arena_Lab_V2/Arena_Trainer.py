# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Lightweight CPU-Safe Training Launcher (Tkinter Desktop App).
Zero live charts during training for maximum speed & CPU longevity.
All UI text is strictly English.
"""

from __future__ import annotations

import gc
import os
import subprocess
import sys
import threading
import time
import tkinter as tk
from tkinter import messagebox, ttk
from typing import Any, Dict, List, Optional, Tuple

# Ensure Arena_Lab_V2 root is on sys.path and auto-relaunch inside local venv if double-clicked directly
ROOT_DIR = os.path.abspath(os.path.dirname(__file__))
if ROOT_DIR not in sys.path:
    sys.path.insert(0, ROOT_DIR)

from engine.venv_bootstrap import ensure_venv

ensure_venv(__file__, require_torch=True)

from engine.ai_brain import get_warm_start_info, promote_model_to_warm_start_base
from engine.card_model import get_card_db
from engine.roster_40 import ROSTER_40, get_model_spec
from pc2_headless_worker import (
    perform_smart_v22_reset,
    play_training_complete_sound_10s,
    resolve_pc2_remote_logs_dir,
)
from trainer.session_runner import (
    CPU_PRESETS,
    pick_next_auto_league_pair,
    run_match_session,
    suggest_matchups,
)
from trainer.stats_store import StatsStore, _atomic_write_json, _read_json

# === Dark Theme Palette ===
BG_DARK = "#12151C"
BG_PANEL = "#1B202B"
BG_INPUT = "#252C3A"
FG_TEXT = "#EAEFF7"
FG_MUTED = "#98A4B8"
ACCENT_BLUE = "#3B82F6"
ACCENT_GREEN = "#10B981"
ACCENT_RED = "#EF4444"
ACCENT_GOLD = "#F59E0B"
ACCENT_PURPLE = "#8B5CF6"


class ArenaTrainerApp:
    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title("Arena Lab V2 — 40-Model CPU Training Suite")
        self.root.geometry("1020x835")
        self.root.minsize(920, 740)
        self.root.configure(bg=BG_DARK)

        self.db = get_card_db()
        self.store = StatsStore()
        self.stop_event = threading.Event()
        self.worker_thread: Optional[threading.Thread] = None
        self.is_running = False

        # Filter, model, and queue state
        self.cat_a_var = tk.StringVar(value="All (41)")
        self.cat_b_var = tk.StringVar(value="All (41)")
        self.model_a_var = tk.StringVar()
        self.model_b_var = tk.StringVar()
        self.matches_var = tk.StringVar(value="500")
        self.cpu_var = tk.StringVar(value="Balanced (55% CPU)")
        self.warm_start_var = tk.BooleanVar(value=True)
        self.queue_items: List[tuple] = []
        self.queue_cycles_var = tk.StringVar(value="1")
        self.queue_preset_var = tk.StringVar(value="Free 10 (5 Pairs)")
        self.league_block_var = tk.StringVar(value="1000")
        self.league_target_var = tk.StringVar(value="1000000")
        self.league_pool_var = tk.StringVar(value="All (41)")
        self.exec_target_var = tk.StringVar(value="pc2")
        self.pc2_badge_var = tk.StringVar(value="PC #2 Status: Checking...")
        self._remote_monitoring = False
        self._seen_remote_logs: List[str] = []

        self._label_to_id_a: Dict[str, str] = {}
        self._label_to_id_b: Dict[str, str] = {}

        self._setup_styles()
        self._build_ui()
        self._refresh_model_lists(keep_selection=False)
        self._update_warm_start_badge()
        self._poll_pc2_heartbeat()
        self._log("Arena Lab V2 ready. Loaded 1,470 V2.2 cards and 41 independent AI models.")
        self._log("Remote PC #2 Control ready: Run Z:\\Start_PC2_Headless_Worker.bat once on PC #2, turn off its screen, and control everything from here!")

        self.root.protocol("WM_DELETE_WINDOW", self._on_close)

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
            lightcolor="#3B475E",
            darkcolor="#1E2636",
            padding=4,
        )
        style.map(
            "TCombobox",
            fieldbackground=[("readonly", "#1E2636"), ("disabled", "#171C26"), ("focus", "#243044")],
            foreground=[("readonly", "#FFFFFF"), ("disabled", "#7A869A"), ("focus", "#FFFFFF")],
            selectbackground=[("readonly", "#1E2636")],
            selectforeground=[("readonly", "#FDE047")],
            background=[("active", "#36445C")],
        )
        self.root.option_add("*TCombobox*Listbox*Background", "#1B2230")
        self.root.option_add("*TCombobox*Listbox*Foreground", "#FFFFFF")
        self.root.option_add("*TCombobox*Listbox*selectBackground", "#3B82F6")
        self.root.option_add("*TCombobox*Listbox*selectForeground", "#FFFFFF")
        self.root.option_add("*TCombobox*Listbox*Font", ("Segoe UI", 10))
        style.configure(
            "Green.Horizontal.TProgressbar",
            troughcolor=BG_INPUT,
            background=ACCENT_GREEN,
            bordercolor=BG_DARK,
            lightcolor=ACCENT_GREEN,
            darkcolor=ACCENT_GREEN,
        )

    def _build_ui(self) -> None:
        # --- Header ---
        hdr = tk.Frame(self.root, bg=BG_PANEL, padx=16, pady=12)
        hdr.pack(fill="x", padx=12, pady=(12, 6))

        left_h = tk.Frame(hdr, bg=BG_PANEL)
        left_h.pack(side="left", fill="x", expand=True)
        tk.Label(
            left_h, text="ARENA LAB V2 — 40-MODEL TRAINING LAUNCHER",
            bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 14, "bold"),
        ).pack(anchor="w")
        tk.Label(
            left_h,
            text="Pure-Python CPU RL Simulator | 1,470 Cards | Strict Integer Rules | Zero Live-Chart Overhead",
            bg=BG_PANEL, fg=FG_MUTED, font=("Segoe UI", 9),
        ).pack(anchor="w")

        right_h = tk.Frame(hdr, bg=BG_PANEL)
        right_h.pack(side="right")

        tk.Button(
            right_h, text="Open Analytics", command=self._open_analytics,
            bg=ACCENT_BLUE, fg="#FFFFFF", activebackground="#2563EB",
            font=("Segoe UI", 9, "bold"), relief="flat", padx=10, pady=6, cursor="hand2",
        ).pack(side="right", padx=(8, 0))

        self.ws_badge = tk.Label(
            right_h, text="Warm-Start Base: Checking...", bg=BG_INPUT, fg=ACCENT_GOLD,
            font=("Segoe UI", 9, "bold"), padx=10, pady=6,
        )
        self.ws_badge.pack(side="right")

        # --- Remote PC #2 Headless Control & Clean/Reset Bar ---
        rem_bar = tk.Frame(self.root, bg="#161D2B", highlightbackground="#2E3B52", highlightthickness=1, padx=14, pady=7)
        rem_bar.pack(fill="x", padx=12, pady=(0, 4))

        tk.Label(
            rem_bar, text="EXECUTION TARGET:", bg="#161D2B", fg=ACCENT_GOLD, font=("Segoe UI", 9, "bold"),
        ).pack(side="left", padx=(0, 8))

        tk.Radiobutton(
            rem_bar, text="Run on PC #2 (Headless Remote over Z:\\ — Screen OFF)",
            variable=self.exec_target_var, value="pc2",
            bg="#161D2B", fg="#38BDF8", selectcolor=BG_INPUT, activebackground="#161D2B", activeforeground="#38BDF8",
            font=("Segoe UI", 9, "bold"), cursor="hand2",
        ).pack(side="left", padx=(0, 12))

        tk.Radiobutton(
            rem_bar, text="Run on THIS PC (Local CPU)",
            variable=self.exec_target_var, value="local",
            bg="#161D2B", fg=FG_TEXT, selectcolor=BG_INPUT, activebackground="#161D2B", activeforeground=FG_TEXT,
            font=("Segoe UI", 9, "bold"), cursor="hand2",
        ).pack(side="left", padx=(0, 14))

        self.pc2_badge_lbl = tk.Label(
            rem_bar, textvariable=self.pc2_badge_var, bg=BG_INPUT, fg=ACCENT_GOLD,
            font=("Segoe UI", 8, "bold"), padx=10, pady=4,
        )
        self.pc2_badge_lbl.pack(side="left")

        tk.Button(
            rem_bar, text="Smart Reset V2.2 (Clean Both PCs)", command=self._on_smart_reset_v22,
            bg="#7F1D1D", fg="#FCA5A5", activebackground=ACCENT_RED, activeforeground="#FFFFFF",
            font=("Segoe UI", 8, "bold"), relief="flat", padx=10, pady=4, cursor="hand2",
        ).pack(side="right")

        # --- Matchup Selection ---
        pick_frame = tk.Frame(self.root, bg=BG_DARK)
        pick_frame.pack(fill="x", padx=12, pady=6)
        pick_frame.columnconfigure(0, weight=1)
        pick_frame.columnconfigure(1, weight=0)
        pick_frame.columnconfigure(2, weight=1)

        self.card_a_desc = tk.StringVar(value="")
        self.card_b_desc = tk.StringVar(value="")

        self.combo_a = self._build_model_panel(
            pick_frame, "MODEL A (SIDE 0)", self.cat_a_var, self.model_a_var, self.card_a_desc, col=0
        )

        mid_col = tk.Frame(pick_frame, bg=BG_DARK, padx=8)
        mid_col.grid(row=0, column=1, sticky="ns")
        tk.Label(mid_col, text="VS", bg=BG_DARK, fg=ACCENT_GOLD, font=("Segoe UI", 16, "bold")).pack(pady=(38, 8))
        tk.Button(
            mid_col, text="Swap", command=self._swap_models,
            bg=BG_INPUT, fg=FG_TEXT, activebackground=ACCENT_BLUE, relief="flat",
            font=("Segoe UI", 9, "bold"), padx=10, pady=4, cursor="hand2",
        ).pack()

        self.combo_b = self._build_model_panel(
            pick_frame, "MODEL B (SIDE 1)", self.cat_b_var, self.model_b_var, self.card_b_desc, col=2
        )

        # --- Session Settings ---
        cfg = tk.Frame(self.root, bg=BG_PANEL, padx=16, pady=12)
        cfg.pack(fill="x", padx=12, pady=6)

        # Matches row
        r1 = tk.Frame(cfg, bg=BG_PANEL)
        r1.pack(fill="x", pady=(0, 8))
        tk.Label(
            r1, text="Number of Matches:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 10, "bold")
        ).pack(side="left")
        tk.Entry(
            r1, textvariable=self.matches_var, width=8, bg=BG_INPUT, fg=FG_TEXT,
            insertbackground=FG_TEXT, font=("Segoe UI", 11, "bold"), relief="flat", justify="center",
        ).pack(side="left", padx=(8, 10))

        for preset_n in (200, 500, 2000, 5000, 10000, 20000):
            tk.Button(
                r1, text=str(preset_n), command=lambda n=preset_n: self.matches_var.set(str(n)),
                bg=BG_INPUT, fg=FG_TEXT, activebackground=ACCENT_BLUE, relief="flat",
                font=("Segoe UI", 9), padx=7, pady=2, cursor="hand2",
            ).pack(side="left", padx=2)

        tk.Label(
            r1, text="CPU Safety Mode:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 10, "bold")
        ).pack(side="left", padx=(24, 8))
        cpu_cb = ttk.Combobox(
            r1, textvariable=self.cpu_var, values=list(CPU_PRESETS.keys()),
            state="readonly", width=20, font=("Segoe UI", 9),
        )
        cpu_cb.pack(side="left")

        # Options row
        r2 = tk.Frame(cfg, bg=BG_PANEL)
        r2.pack(fill="x")
        tk.Checkbutton(
            r2,
            text="Use Warm-Start Base for Untrained Models (Transfers combat basics only; each model builds its own deck)",
            variable=self.warm_start_var,
            bg=BG_PANEL, fg=FG_TEXT, selectcolor=BG_INPUT,
            activebackground=BG_PANEL, activeforeground=FG_TEXT, font=("Segoe UI", 9),
        ).pack(side="left")

        # --- Action Buttons ---
        btns = tk.Frame(self.root, bg=BG_DARK)
        btns.pack(fill="x", padx=12, pady=6)

        self.btn_start = tk.Button(
            btns, text="START SINGLE PAIR", command=self._start_training,
            bg=ACCENT_GREEN, fg="#0B1215", activebackground="#059669",
            font=("Segoe UI", 10, "bold"), relief="flat", padx=16, pady=8, cursor="hand2",
        )
        self.btn_start.pack(side="left", padx=(0, 8))

        self.btn_run_queue = tk.Button(
            btns, text="RUN QUEUE PLAYLIST", command=self._start_queue,
            bg="#06B6D4", fg="#082F49", activebackground="#0891B2",
            font=("Segoe UI", 10, "bold"), relief="flat", padx=16, pady=8, cursor="hand2",
        )
        self.btn_run_queue.pack(side="left", padx=(0, 8))

        self.btn_stop = tk.Button(
            btns, text="STOP & SAVE", command=self._stop_training,
            bg=ACCENT_RED, fg=FG_TEXT, activebackground="#DC2626",
            font=("Segoe UI", 10, "bold"), relief="flat", padx=14, pady=8, state="disabled", cursor="hand2",
        )
        self.btn_stop.pack(side="left", padx=(0, 12))

        self.btn_advisor = tk.Button(
            btns, text="Smart Matchup Advisor", command=self._open_advisor,
            bg=ACCENT_PURPLE, fg=FG_TEXT, activebackground="#7C3AED",
            font=("Segoe UI", 9, "bold"), relief="flat", padx=12, pady=8, cursor="hand2",
        )
        self.btn_advisor.pack(side="left", padx=(0, 8))

        self.btn_promote = tk.Button(
            btns, text="Set Model A as Warm-Start Base", command=self._promote_model_a,
            bg=ACCENT_GOLD, fg="#111827", activebackground="#D97706",
            font=("Segoe UI", 9, "bold"), relief="flat", padx=10, pady=8, cursor="hand2",
        )
        self.btn_promote.pack(side="left", padx=(0, 8))

        self.btn_reset_free = tk.Button(
            btns, text="Reset Free-10 Locks", command=self._reset_free10,
            bg=BG_INPUT, fg=FG_MUTED, activebackground=ACCENT_RED,
            font=("Segoe UI", 8), relief="flat", padx=8, pady=8, cursor="hand2",
        )
        self.btn_reset_free.pack(side="right")

        # --- Multi-Matchup Queue / Rounds Playlist ---
        q_box = tk.Frame(self.root, bg=BG_PANEL, padx=14, pady=10)
        q_box.pack(fill="x", padx=12, pady=4)

        q_top = tk.Frame(q_box, bg=BG_PANEL)
        q_top.pack(fill="x", pady=(0, 6))
        tk.Label(
            q_top, text="MATCHUP QUEUE / ROUNDS PLAYLIST",
            bg=BG_PANEL, fg="#22D3EE", font=("Segoe UI", 9, "bold"),
        ).pack(side="left")

        self.btn_q_add = tk.Button(
            q_top, text="+ Add Current Pair", command=self._queue_add_current,
            bg=ACCENT_BLUE, fg="#FFFFFF", relief="flat", font=("Segoe UI", 8, "bold"),
            padx=9, pady=3, cursor="hand2",
        )
        self.btn_q_add.pack(side="left", padx=(12, 6))

        presets = [
            "Free 10 (5 Pairs)",
            "Environments 10 (5 Pairs)",
            "Tribes 10 (5 Pairs)",
            "Playstyles 10 (5 Pairs)",
            "Anomaly Glitch_41 Gauntlet (4 Pairs)",
            "ALL 41 Models (21 Pairs)",
        ]
        ttk.Combobox(
            q_top, textvariable=self.queue_preset_var, values=presets,
            state="readonly", width=23, font=("Segoe UI", 8),
        ).pack(side="left", padx=(6, 4))

        self.btn_q_preset = tk.Button(
            q_top, text="+ Add Preset", command=self._queue_add_preset,
            bg=BG_INPUT, fg=FG_TEXT, relief="flat", font=("Segoe UI", 8, "bold"),
            padx=8, pady=3, cursor="hand2",
        )
        self.btn_q_preset.pack(side="left", padx=(0, 8))

        tk.Label(q_top, text="Cycles:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 8, "bold")).pack(side="left", padx=(4, 4))
        tk.Entry(
            q_top, textvariable=self.queue_cycles_var, width=4,
            bg=BG_INPUT, fg=FG_TEXT, insertbackground=FG_TEXT,
            font=("Segoe UI", 9, "bold"), relief="flat", justify="center",
        ).pack(side="left")

        self.btn_q_clear = tk.Button(
            q_top, text="Clear", command=self._queue_clear,
            bg=BG_INPUT, fg=FG_MUTED, relief="flat", font=("Segoe UI", 8), padx=8, pady=3, cursor="hand2",
        )
        self.btn_q_clear.pack(side="right", padx=(4, 0))

        self.btn_q_rem = tk.Button(
            q_top, text="Remove", command=self._queue_remove_selected,
            bg=BG_INPUT, fg=FG_TEXT, relief="flat", font=("Segoe UI", 8), padx=8, pady=3, cursor="hand2",
        )
        self.btn_q_rem.pack(side="right")

        q_list_row = tk.Frame(q_box, bg=BG_PANEL)
        q_list_row.pack(fill="x")
        self.queue_listbox = tk.Listbox(
            q_list_row, bg=BG_DARK, fg="#E2E8F0", selectbackground=ACCENT_BLUE,
            selectforeground="#FFFFFF", font=("Consolas", 9), height=3, relief="flat",
        )
        q_sb = tk.Scrollbar(q_list_row, command=self.queue_listbox.yview)
        self.queue_listbox.configure(yscrollcommand=q_sb.set)
        q_sb.pack(side="right", fill="y")
        self.queue_listbox.pack(fill="x", expand=True)

        # --- Perpetual Auto-League Bar (Least-Experienced Auto-Pairing up to 1,000,000+ Matches!) ---
        al_box = tk.Frame(self.root, bg="#1E2333", padx=14, pady=8)
        al_box.pack(fill="x", padx=12, pady=(2, 4))

        tk.Label(
            al_box, text="AUTO-LEAGUE (PERPETUAL):", bg="#1E2333", fg=ACCENT_GOLD,
            font=("Segoe UI", 9, "bold"),
        ).pack(side="left")

        tk.Label(al_box, text="Pool:", bg="#1E2333", fg=FG_TEXT, font=("Segoe UI", 8)).pack(side="left", padx=(10, 4))
        ttk.Combobox(
            al_box, textvariable=self.league_pool_var,
            values=["All (41)", "Free (10)", "Playstyle (10)", "Environment (10)", "Tribe (10)", "Anomaly (1)"],
            state="readonly", width=15, font=("Segoe UI", 8),
        ).pack(side="left")

        tk.Label(al_box, text="Matches/Pair:", bg="#1E2333", fg=FG_TEXT, font=("Segoe UI", 8)).pack(side="left", padx=(10, 4))
        tk.Entry(
            al_box, textvariable=self.league_block_var, width=6,
            bg=BG_INPUT, fg=FG_TEXT, insertbackground=FG_TEXT,
            font=("Segoe UI", 9, "bold"), relief="flat", justify="center",
        ).pack(side="left")

        tk.Label(al_box, text="Target Total:", bg="#1E2333", fg=FG_TEXT, font=("Segoe UI", 8)).pack(side="left", padx=(10, 4))
        tk.Entry(
            al_box, textvariable=self.league_target_var, width=9,
            bg=BG_INPUT, fg=FG_TEXT, insertbackground=FG_TEXT,
            font=("Segoe UI", 9, "bold"), relief="flat", justify="center",
        ).pack(side="left")

        self.btn_auto_league = tk.Button(
            al_box, text="START AUTO-LEAGUE (NONSTOP)", command=self._start_auto_league,
            bg="#F97316", fg="#111827", activebackground="#EA580C",
            font=("Segoe UI", 9, "bold"), relief="flat", padx=14, pady=5, cursor="hand2",
        )
        self.btn_auto_league.pack(side="right")

        # --- Minimal Progress Bar & Status ---
        prog_box = tk.Frame(self.root, bg=BG_PANEL, padx=16, pady=10)
        prog_box.pack(fill="x", padx=12, pady=4)

        self.prog_title_var = tk.StringVar(value="Idle — Select two models and press START TRAINING.")
        tk.Label(
            prog_box, textvariable=self.prog_title_var,
            bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 10, "bold"),
        ).pack(anchor="w")

        self.progress = ttk.Progressbar(
            prog_box, style="Green.Horizontal.TProgressbar",
            orient="horizontal", mode="determinate", maximum=100,
        )
        self.progress.pack(fill="x", pady=(6, 6))

        self.prog_stats_var = tk.StringVar(value="Match 0 / 0  |  Score: 0 - 0  |  Speed: --  |  ETA: --")
        tk.Label(
            prog_box, textvariable=self.prog_stats_var,
            bg=BG_PANEL, fg=FG_MUTED, font=("Consolas", 9),
        ).pack(anchor="w")

        # --- Lightweight Activity Log ---
        log_frame = tk.Frame(self.root, bg=BG_PANEL, padx=12, pady=10)
        log_frame.pack(fill="both", expand=True, padx=12, pady=(4, 12))
        tk.Label(
            log_frame, text="SESSION LOG (Open Arena_Analytics.py anytime for deep deck & card reports)",
            bg=BG_PANEL, fg=FG_MUTED, font=("Segoe UI", 8, "bold"),
        ).pack(anchor="w", pady=(0, 4))

        self.log_text = tk.Text(
            log_frame, bg=BG_DARK, fg="#CBD5E1", font=("Consolas", 9),
            relief="flat", height=10, state="disabled", wrap="word",
        )
        sb = tk.Scrollbar(log_frame, command=self.log_text.yview)
        self.log_text.configure(yscrollcommand=sb.set)
        sb.pack(side="right", fill="y")
        self.log_text.pack(fill="both", expand=True)

    def _build_model_panel(
        self,
        parent: tk.Frame,
        title: str,
        cat_var: tk.StringVar,
        model_var: tk.StringVar,
        desc_var: tk.StringVar,
        col: int,
    ) -> ttk.Combobox:
        box = tk.Frame(parent, bg=BG_PANEL, padx=14, pady=12)
        box.grid(row=0, column=col, sticky="nsew")

        top = tk.Frame(box, bg=BG_PANEL)
        top.pack(fill="x", pady=(0, 6))
        tk.Label(top, text=title, bg=BG_PANEL, fg=ACCENT_BLUE, font=("Segoe UI", 10, "bold")).pack(side="left")

        cats = ["All (41)", "Playstyle (10)", "Environment (10)", "Tribe (10)", "Free (10)", "Anomaly (1)"]
        cat_cb = ttk.Combobox(top, textvariable=cat_var, values=cats, state="readonly", width=15, font=("Segoe UI", 8))
        cat_cb.pack(side="right")
        cat_cb.bind("<<ComboboxSelected>>", lambda _e: self._refresh_model_lists(keep_selection=True))

        m_cb = ttk.Combobox(box, textvariable=model_var, state="readonly", font=("Segoe UI", 10))
        m_cb.pack(fill="x", pady=(0, 6))
        m_cb.bind("<<ComboboxSelected>>", lambda _e: self._update_descriptions())

        tk.Label(
            box, textvariable=desc_var, bg=BG_PANEL, fg=FG_MUTED,
            font=("Segoe UI", 8), wraplength=390, justify="left", height=2, anchor="nw",
        ).pack(fill="x")
        return m_cb

    def _filtered_models(self, cat_label: str) -> List[str]:
        cat_key = cat_label.split(" ")[0]
        out: List[str] = []
        for spec in ROSTER_40:
            if cat_key != "All" and spec.category != cat_key:
                continue
            out.append(spec.model_id)
        return out

    def _format_model_label(self, model_id: str) -> str:
        rec = self.store.roster[model_id]
        spec = get_model_spec(model_id)
        m = rec["matches"]
        wr = (100.0 * rec["wins"] / m) if m > 0 else 0.0
        return f"{spec.display_name}  ({m}m | {wr:.0f}% WR | ELO {rec['elo']:.0f})"

    def _refresh_model_lists(self, keep_selection: bool = True) -> None:
        self.store.reload()
        old_a = self._label_to_id_a.get(self.model_a_var.get(), "free_01")
        old_b = self._label_to_id_b.get(self.model_b_var.get(), "free_02")

        ids_a = self._filtered_models(self.cat_a_var.get())
        ids_b = self._filtered_models(self.cat_b_var.get())

        self._label_to_id_a = {self._format_model_label(mid): mid for mid in ids_a}
        self._label_to_id_b = {self._format_model_label(mid): mid for mid in ids_b}

        labels_a = list(self._label_to_id_a.keys())
        labels_b = list(self._label_to_id_b.keys())
        self.combo_a["values"] = labels_a
        self.combo_b["values"] = labels_b

        if keep_selection and old_a in ids_a:
            self.model_a_var.set(self._format_model_label(old_a))
        elif labels_a:
            self.model_a_var.set(self._format_model_label("free_01") if "free_01" in ids_a else labels_a[0])

        if keep_selection and old_b in ids_b:
            self.model_b_var.set(self._format_model_label(old_b))
        elif labels_b:
            self.model_b_var.set(self._format_model_label("free_02") if "free_02" in ids_b else labels_b[-1])

        self._update_descriptions()

    def _select_pair(self, id_a: str, id_b: str) -> None:
        self.cat_a_var.set("All (41)")
        self.cat_b_var.set("All (41)")
        self._refresh_model_lists(keep_selection=False)
        self.model_a_var.set(self._format_model_label(id_a))
        self.model_b_var.set(self._format_model_label(id_b))
        self._update_descriptions()

    def _swap_models(self) -> None:
        a = self._label_to_id_a.get(self.model_a_var.get())
        b = self._label_to_id_b.get(self.model_b_var.get())
        if a and b:
            self._select_pair(b, a)

    def _update_descriptions(self) -> None:
        id_a = self._label_to_id_a.get(self.model_a_var.get())
        id_b = self._label_to_id_b.get(self.model_b_var.get())
        if id_a:
            self.card_a_desc.set(get_model_spec(id_a).short_desc)
        if id_b:
            self.card_b_desc.set(get_model_spec(id_b).short_desc)

    def _update_warm_start_badge(self) -> None:
        info = get_warm_start_info()
        if info:
            self.ws_badge.configure(
                text=f"Warm-Start Base: READY ({info['source_model_id']}, {info['source_matches']}m)",
                fg=ACCENT_GREEN,
            )
        else:
            self.ws_badge.configure(
                text="Warm-Start Base: NOT SET YET",
                fg=ACCENT_GOLD,
            )

    def _log(self, msg: str) -> None:
        ts = time.strftime("%H:%M:%S")
        self.log_text.configure(state="normal")
        self.log_text.insert("end", f"[{ts}] {msg}\n")
        self.log_text.see("end")
        self.log_text.configure(state="disabled")

    def _set_controls_state(self, running: bool) -> None:
        self.is_running = running
        st = "disabled" if running else "normal"
        for btn in (
            self.btn_start, self.btn_run_queue, self.btn_auto_league, self.btn_advisor, self.btn_promote,
            self.btn_reset_free, self.btn_q_add, self.btn_q_preset, self.btn_q_rem, self.btn_q_clear,
        ):
            btn.configure(state=st)
        self.btn_stop.configure(state="normal" if running else "disabled")

    def _parse_matches_input(self) -> Optional[int]:
        try:
            n = int(self.matches_var.get().strip())
            if n < 1 or n > 500000:
                raise ValueError
            return n
        except ValueError:
            messagebox.showerror("Invalid Match Count", "Enter a whole number of matches between 1 and 500,000.")
            return None

    def _refresh_queue_listbox(self) -> None:
        self.queue_listbox.delete(0, "end")
        for idx, (a, b, n) in enumerate(self.queue_items, 1):
            sa = get_model_spec(a).display_name
            sb = get_model_spec(b).display_name
            self.queue_listbox.insert("end", f" {idx:2d}.  {sa:30s}  VS  {sb:30s}  |  {n:,} matches")

    def _queue_add_current(self) -> None:
        id_a = self._label_to_id_a.get(self.model_a_var.get())
        id_b = self._label_to_id_b.get(self.model_b_var.get())
        n = self._parse_matches_input()
        if not id_a or not id_b or n is None:
            return
        self.queue_items.append((id_a, id_b, n))
        self._refresh_queue_listbox()
        self._log(f"Queued: {id_a} vs {id_b} ({n:,} matches). Queue size: {len(self.queue_items)}")

    def _queue_add_preset(self) -> None:
        n = self._parse_matches_input()
        if n is None:
            return
        p = self.queue_preset_var.get()
        free_pairs = [("free_01", "free_02"), ("free_03", "free_04"), ("free_05", "free_06"), ("free_07", "free_08"), ("free_09", "free_10")]
        env_pairs = [("env_fire", "env_water"), ("env_nature", "env_electric"), ("env_ice", "env_earth"), ("env_dark", "env_light"), ("env_air", "env_desert")]
        tribe_pairs = [("tribe_celestials", "tribe_demons"), ("tribe_ghosts", "tribe_mages"), ("tribe_warriors", "tribe_dwarves"), ("tribe_giants", "tribe_elves"), ("tribe_beasts", "tribe_insects")]
        style_pairs = [("style_alpha", "style_bastion"), ("style_sparta", "style_arcane"), ("style_pyro", "style_viper"), ("style_mirage", "style_hive"), ("style_reaper", "style_tactician")]
        anom_pairs = [("anomaly_glitch", "free_01"), ("anomaly_glitch", "style_bastion"), ("anomaly_glitch", "env_fire"), ("anomaly_glitch", "tribe_mages")]

        if p.startswith("Free"):
            pairs = free_pairs
        elif p.startswith("Environments"):
            pairs = env_pairs
        elif p.startswith("Tribes"):
            pairs = tribe_pairs
        elif p.startswith("Playstyles"):
            pairs = style_pairs
        elif p.startswith("Anomaly"):
            pairs = anom_pairs
        else:
            pairs = free_pairs + env_pairs + tribe_pairs + style_pairs + [("anomaly_glitch", "free_01")]

        for a, b in pairs:
            self.queue_items.append((a, b, n))
        self._refresh_queue_listbox()
        self._log(f"Added preset '{p}' ({len(pairs)} pairs x {n:,} matches). Total queue: {len(self.queue_items)} pairs.")

    def _queue_remove_selected(self) -> None:
        sel = self.queue_listbox.curselection()
        if not sel:
            return
        for idx in reversed(sel):
            self.queue_items.pop(idx)
        self._refresh_queue_listbox()

    def _queue_clear(self) -> None:
        self.queue_items.clear()
        self._refresh_queue_listbox()

    def _start_training(self) -> None:
        if self.is_running:
            return
        id_a = self._label_to_id_a.get(self.model_a_var.get())
        id_b = self._label_to_id_b.get(self.model_b_var.get())
        if not id_a or not id_b:
            messagebox.showerror("Invalid Selection", "Please select both Model A and Model B.")
            return
        n_matches = self._parse_matches_input()
        if n_matches is None:
            return
        self._launch_playlist([(id_a, id_b, n_matches)], cycles=1)

    def _start_queue(self) -> None:
        if self.is_running:
            return
        if not self.queue_items:
            messagebox.showwarning("Empty Queue", "Add at least one matchup (or a Quick Preset) to the queue first!")
            return
        try:
            cycles = int(self.queue_cycles_var.get().strip())
            if cycles < 1 or cycles > 50:
                raise ValueError
        except ValueError:
            messagebox.showerror("Invalid Cycles", "Enter Cycles between 1 and 50.")
            return
        self._launch_playlist(list(self.queue_items), cycles=cycles)

    def _start_auto_league(self) -> None:
        if self.is_running:
            return
        try:
            block_n = int(self.league_block_var.get().strip())
            target_n = int(self.league_target_var.get().strip())
            if block_n < 16 or target_n < block_n or target_n > 50_000_000:
                raise ValueError
        except ValueError:
            messagebox.showerror(
                "Invalid Auto-League Settings",
                "Matches/Pair must be >= 16 and Target Total must be between Matches/Pair and 50,000,000.",
            )
            return

        pool_scope = self.league_pool_var.get().split(" ")[0]
        cpu_preset = self.cpu_var.get()
        use_ws = bool(self.warm_start_var.get())

        if self.exec_target_var.get() == "pc2":
            self._send_remote_command(
                {
                    "action": "start_auto_league",
                    "pool_scope": pool_scope,
                    "block_n": block_n,
                    "target_n": target_n,
                    "cpu_preset": cpu_preset,
                    "use_warm_start": use_ws,
                },
                f"Dispatched PERPETUAL AUTO-LEAGUE to PC #2: Pool={pool_scope} | {block_n:,}/pair | Target={target_n:,} | CPU={cpu_preset}",
            )
            return

        self.stop_event.clear()
        self._set_controls_state(True)

        self._log(
            f"Started PERPETUAL AUTO-LEAGUE: Pool={pool_scope} | {block_n:,} matches/pair | "
            f"Target={target_n:,} matches | CPU={cpu_preset} (Press STOP & SAVE anytime)"
        )

        def _worker() -> None:
            league_total = 0
            round_num = 0
            last_pair: Optional[Tuple[str, str]] = None
            try:
                while league_total < target_n and not self.stop_event.is_set():
                    round_num += 1
                    cur_block = min(block_n, target_n - league_total)
                    id_a, id_b = pick_next_auto_league_pair(self.store, scope=pool_scope, last_pair=last_pair)
                    last_pair = (id_a, id_b)
                    spec_a = get_model_spec(id_a)
                    spec_b = get_model_spec(id_b)
                    ma = self.store.roster[id_a]["matches"]
                    mb = self.store.roster[id_b]["matches"]

                    title_str = (
                        f"[Auto-League Round #{round_num} | Total {league_total:,}/{target_n:,}]  "
                        f"{spec_a.display_name} ({ma:,}m)  VS  {spec_b.display_name} ({mb:,}m)"
                    )
                    self.root.after(
                        0,
                        lambda t=title_str, nm=cur_block, rn=round_num, a=id_a, b=id_b: self._on_job_start(
                            t, nm, rn, max(1, target_n // block_n), a, b
                        ),
                    )

                    last_log_milestone = [0]

                    def _on_progress(done: int, total: int, info: Dict[str, Any]) -> None:
                        mps = info["matches_per_sec"]
                        rem_block = (total - done) / max(0.01, mps)
                        eta_str = f"{int(rem_block // 60)}m {int(rem_block % 60):02d}s" if rem_block >= 60 else f"{int(rem_block)}s"
                        stat_line = (
                            f"Round Match {done:,}/{total:,}  |  League Total: {league_total + done:,}/{target_n:,}  |  "
                            f"A:{info['wins_a']}W B:{info['wins_b']}W  |  {mps:.1f} m/s  |  Pair ETA: {eta_str}"
                        )
                        log_line: Optional[str] = None
                        if done - last_log_milestone[0] >= 500:
                            last_log_milestone[0] = done
                            wr_a = 100.0 * info["wins_a"] / max(1, done)
                            log_line = (
                                f"[Auto-League #{round_num} @ {done}/{total}] A WR={wr_a:.1f}% | "
                                f"V-Loss A={info.get('v_loss_a', 0.0):.3f} B={info.get('v_loss_b', 0.0):.3f} | "
                                f"Focus A={info.get('focus_a', 0.0):.1f}% B={info.get('focus_b', 0.0):.1f}%"
                            )
                        self.root.after(0, lambda d=done, s=stat_line, ll=log_line: self._apply_progress_ui(d, s, ll))

                    summary = run_match_session(
                        model_a_id=id_a,
                        model_b_id=id_b,
                        num_matches=cur_block,
                        cpu_preset=cpu_preset,
                        use_warm_start=use_ws,
                        stop_event=self.stop_event,
                        progress_cb=_on_progress,
                        db=self.db,
                        store=self.store,
                    )
                    league_total += summary["completed"]
                    self.root.after(0, lambda res=summary: self._on_job_finished(res))
                    # Keep RAM completely flat over 1,000,000+ matches!
                    gc.collect()

                self.root.after(
                    0,
                    lambda lt=league_total, rn=round_num: self._on_playlist_done(rn, rn, None, None),
                )
            except Exception as exc:
                self.root.after(0, lambda e=exc, rn=round_num: self._on_playlist_done(rn, rn, None, e))

        self.worker_thread = threading.Thread(target=_worker, daemon=True)
        self.worker_thread.start()

    def _launch_playlist(self, playlist: List[tuple], cycles: int = 1) -> None:
        cpu_preset = self.cpu_var.get()
        use_ws = bool(self.warm_start_var.get())

        if self.exec_target_var.get() == "pc2":
            self._send_remote_command(
                {
                    "action": "start_playlist",
                    "playlist": [[a, b, n] for (a, b, n) in playlist],
                    "cycles": cycles,
                    "cpu_preset": cpu_preset,
                    "use_warm_start": use_ws,
                },
                f"Dispatched playlist to PC #2: {len(playlist)} pair(s) x {cycles} cycle(s) | CPU={cpu_preset}",
            )
            return

        self.stop_event.clear()
        self._set_controls_state(True)

        total_jobs = len(playlist) * cycles
        self._log(f"Started playlist: {len(playlist)} pair(s) x {cycles} cycle(s) = {total_jobs} job(s) | CPU={cpu_preset}")

        def _worker() -> None:
            jobs_done = 0
            last_summary: Optional[Dict[str, Any]] = None
            try:
                for cyc in range(1, cycles + 1):
                    for idx, (id_a, id_b, n_matches) in enumerate(playlist, 1):
                        if self.stop_event.is_set():
                            break
                        job_num = (cyc - 1) * len(playlist) + idx
                        spec_a = get_model_spec(id_a)
                        spec_b = get_model_spec(id_b)
                        title_str = (
                            f"[Job {job_num}/{total_jobs} | Cycle {cyc}/{cycles}]  "
                            f"{spec_a.display_name}  VS  {spec_b.display_name}  ({cpu_preset})"
                        )
                        self.root.after(
                            0,
                            lambda t=title_str, nm=n_matches, jn=job_num, a=id_a, b=id_b: self._on_job_start(t, nm, jn, total_jobs, a, b),
                        )

                        last_log_milestone = [0]

                        def _on_progress(done: int, total: int, info: Dict[str, Any]) -> None:
                            mps = info["matches_per_sec"]
                            eta = (total - done) / max(0.01, mps)
                            if eta >= 3600:
                                eta_str = f"{int(eta // 3600)}h {int((eta % 3600) // 60):02d}m"
                            elif eta >= 60:
                                eta_str = f"{int(eta // 60)}m {int(eta % 60):02d}s"
                            else:
                                eta_str = f"{int(eta)}s"
                            stat_line = (
                                f"Match {done} / {total}  |  A: {info['wins_a']}W  B: {info['wins_b']}W  D: {info['draws']}  |  "
                                f"Avg Turns: {info['avg_turns']:.1f}  |  Speed: {mps:.2f} m/s ({info['threads']}T)  |  ETA: {eta_str}"
                            )
                            log_line: Optional[str] = None
                            milestone_step = 200 if total >= 400 else 50
                            if done - last_log_milestone[0] >= milestone_step:
                                last_log_milestone[0] = done
                                wr_a = 100.0 * info["wins_a"] / max(1, done)
                                log_line = (
                                    f"[Learning Check @ {done}/{total}] A WR={wr_a:.1f}% | "
                                    f"V-Loss A={info.get('v_loss_a', 0.0):.3f} B={info.get('v_loss_b', 0.0):.3f} | "
                                    f"Deck Focus A={info.get('focus_a', 0.0):.1f}% B={info.get('focus_b', 0.0):.1f}% | "
                                    f"Temp={info.get('temp_a', 1.0):.2f}"
                                )
                            self.root.after(0, lambda d=done, s=stat_line, ll=log_line: self._apply_progress_ui(d, s, ll))

                        summary = run_match_session(
                            model_a_id=id_a,
                            model_b_id=id_b,
                            num_matches=n_matches,
                            cpu_preset=cpu_preset,
                            use_warm_start=use_ws,
                            stop_event=self.stop_event,
                            progress_cb=_on_progress,
                            db=self.db,
                            store=self.store,
                        )
                        jobs_done += 1
                        last_summary = summary
                        self.root.after(0, lambda res=summary: self._on_job_finished(res))
                    if self.stop_event.is_set():
                        break
                self.root.after(0, lambda jd=jobs_done, tj=total_jobs, ls=last_summary: self._on_playlist_done(jd, tj, ls, None))
            except Exception as exc:
                self.root.after(0, lambda e=exc: self._on_playlist_done(jobs_done, total_jobs, None, e))

        self.worker_thread = threading.Thread(target=_worker, daemon=True)
        self.worker_thread.start()

    def _on_job_start(self, title: str, n_matches: int, job_num: int, total_jobs: int, id_a: str, id_b: str) -> None:
        self.prog_title_var.set(title)
        self.progress["value"] = 0
        self.progress["maximum"] = n_matches
        self._log(f"--- Job {job_num}/{total_jobs}: {id_a} vs {id_b} ({n_matches:,} matches) ---")

    def _on_job_finished(self, summary: Dict[str, Any]) -> None:
        self._refresh_model_lists(keep_selection=True)
        status = "Stopped & Saved" if summary["stopped_early"] else "Completed"
        self._log(
            f"{status} ({summary['elapsed_sec']:.1f}s, {summary['matches_per_sec']:.2f} m/s): "
            f"{summary['model_a_id']} {summary['wins_a']}W ({summary['win_rate_a']:.1f}%) vs "
            f"{summary['model_b_id']} {summary['wins_b']}W ({summary['win_rate_b']:.1f}%) | "
            f"Draws: {summary['draws']} | Avg Turns: {summary['avg_turns']:.1f} | "
            f"Style A={summary['avg_style_a']:.2f}, B={summary['avg_style_b']:.2f}"
        )

    def _on_playlist_done(
        self, jobs_done: int, total_jobs: int, last_summary: Optional[Dict[str, Any]], err: Optional[Exception]
    ) -> None:
        self._set_controls_state(False)
        self._refresh_model_lists(keep_selection=True)
        if err is not None:
            self.prog_title_var.set("Training Error — Check log below.")
            self._log(f"ERROR: {err}")
            messagebox.showerror("Training Error", str(err))
            return
        if self.stop_event.is_set():
            self.prog_title_var.set(f"Stopped & Saved cleanly ({jobs_done} / {total_jobs} jobs completed).")
        else:
            self.prog_title_var.set(f"ALL DONE — Completed {jobs_done} / {total_jobs} queued matchup job(s)!")
            play_training_complete_sound_10s()

    def _apply_progress_ui(self, done: int, stat_line: str, log_line: Optional[str] = None) -> None:
        self.progress["value"] = done
        self.prog_stats_var.set(stat_line)
        if log_line:
            self._log(log_line)

    def _open_analytics(self) -> None:
        analytics_py = os.path.join(ROOT_DIR, "Arena_Analytics.py")
        subprocess.Popen([sys.executable, analytics_py])

    def _stop_training(self) -> None:
        if self._remote_monitoring:
            self._send_remote_command({"action": "stop"}, "Sent remote STOP & SAVE signal to PC #2...")
            self.prog_title_var.set("Sent STOP & SAVE to PC #2 — waiting for current match to finish...")
            return
        if self.is_running:
            self.stop_event.set()
            self.prog_title_var.set("Stopping cleanly after current match & saving checkpoints...")
            self._log("Stop requested — finishing current match and flushing checkpoints cleanly...")

    def _send_remote_command(self, payload: Dict[str, Any], log_msg: str) -> None:
        rem_logs = resolve_pc2_remote_logs_dir(ROOT_DIR)
        cmd_file = os.path.join(rem_logs, "pc2_remote_cmd.json")
        cmd = dict(payload)
        cmd["cmd_id"] = f"cmd_{int(time.time() * 1000)}"
        cmd["status"] = "pending"
        cmd["sent_ts"] = time.strftime("%Y-%m-%d %H:%M:%S")
        _atomic_write_json(cmd_file, cmd)
        self._log(log_msg)
        if payload.get("action") in ("start_playlist", "start_auto_league"):
            self._remote_monitoring = True
            self._set_controls_state(True)
            self.prog_title_var.set("Command sent to PC #2 — waiting for PC #2 worker to pick it up...")

    def _poll_pc2_heartbeat(self) -> None:
        try:
            rem_logs = resolve_pc2_remote_logs_dir(ROOT_DIR)
            status_file = os.path.join(rem_logs, "pc2_status.json")
            cmd_file = os.path.join(rem_logs, "pc2_remote_cmd.json")
            st = _read_json(status_file, {})
            if isinstance(st, dict) and st.get("hostname"):
                age = time.time() - float(st.get("epoch", 0.0))
                host = st.get("hostname", "PC2")
                ip_s = st.get("ip", "")
                r_state = str(st.get("state", "idle"))
                if age <= 8.0:
                    self.pc2_badge_var.set(f"PC #2 ONLINE ({host} {ip_s} — {r_state.upper()})")
                    self.pc2_badge_lbl.configure(fg=ACCENT_GREEN if r_state == "idle" else "#38BDF8")
                else:
                    self.pc2_badge_var.set(f"PC #2 Worker Offline ({host} last seen {int(age)}s ago)")
                    self.pc2_badge_lbl.configure(fg=ACCENT_GOLD)

                # Stream live PC #2 progress & logs into Trainer UI!
                for line in st.get("recent_logs", []):
                    if line not in self._seen_remote_logs:
                        self._seen_remote_logs.append(line)
                        if len(self._seen_remote_logs) > 150:
                            self._seen_remote_logs = self._seen_remote_logs[-150:]
                        self.log_text.configure(state="normal")
                        self.log_text.insert("end", f"{line}\n")
                        self.log_text.see("end")
                        self.log_text.configure(state="disabled")

                if r_state in ("running", "stopping"):
                    if not self.is_running:
                        self._remote_monitoring = True
                        self._set_controls_state(True)
                    self.prog_title_var.set(str(st.get("prog_title", "PC #2 Running...")))
                    self.progress["maximum"] = max(1, int(st.get("prog_total", 100)))
                    self.progress["value"] = int(st.get("prog_done", 0))
                    self.prog_stats_var.set(str(st.get("prog_stats", "")))
                elif self._remote_monitoring and r_state == "idle":
                    cmd_now = _read_json(cmd_file, {})
                    if not isinstance(cmd_now, dict) or cmd_now.get("status") != "pending":
                        self._remote_monitoring = False
                        self._set_controls_state(False)
                        self.prog_title_var.set(str(st.get("prog_title", "PC #2 Idle.")))
                        self.prog_stats_var.set(str(st.get("prog_stats", "")))
                        self._refresh_model_lists(keep_selection=True)
                        play_training_complete_sound_10s()
            else:
                self.pc2_badge_var.set("PC #2 Worker: Waiting (Run Setup_And_Clean_PC2_Once.bat once on PC #2)")
                self.pc2_badge_lbl.configure(fg=ACCENT_GOLD)
        except Exception:
            pass
        self.root.after(1500, self._poll_pc2_heartbeat)

    def _on_smart_reset_v22(self) -> None:
        if self.is_running:
            messagebox.showwarning("Training Active", "Please click STOP & SAVE before performing Smart Reset.")
            return
        ok = messagebox.askyesno(
            "Smart Reset V2.2 (Both PCs)",
            "Archive all current logs & 41 model decks/brains into Archive_Pre_V22,\n"
            "keep Warm-Start Base (fighter_base.pt), and start 100% clean for V2.2?\n\n"
            "(Because PC #2 shares Z:\\ with this folder, this cleans BOTH PCs immediately!)",
        )
        if not ok:
            return
        res = perform_smart_v22_reset(ROOT_DIR)
        self._send_remote_command(
            {"action": "clean_reset"},
            "Sent remote Smart V2.2 Clean & Reset command to PC #2's local D: drive as well!",
        )
        self._refresh_model_lists(keep_selection=False)
        self._update_warm_start_badge()
        self._log(
            f"Smart V2.2 Reset complete! Archived {res['archived_logs']} logs & {res['archived_models']} models -> {res['archive_dir']}"
        )
        messagebox.showinfo(
            "Smart Reset Complete",
            f"Both PCs are now 100% clean for V2.2!\n"
            f"- Archived logs: {res['archived_logs']}\n"
            f"- Archived models: {res['archived_models']}\n"
            f"- Warm-Start Base (fighter_base.pt): PRESERVED",
        )

    def _promote_model_a(self) -> None:
        id_a = self._label_to_id_a.get(self.model_a_var.get())
        if not id_a:
            return
        rec = self.store.roster[id_a]
        if rec["matches"] <= 0:
            messagebox.showwarning(
                "Model Untrained",
                f"{id_a} has 0 trained matches. Train it first (recommended: 500 matches) before setting it as Warm-Start Base.",
            )
            return
        ok = messagebox.askyesno(
            "Set Warm-Start Base",
            f"Copy Fighter weights from '{rec['display_name']}' ({rec['matches']} matches, ELO {rec['elo']:.0f})\n"
            f"into the global Warm-Start Base?\n\n"
            f"All future untrained models will start already knowing combat & rules while still building their own unique 50-card decks.",
        )
        if not ok:
            return
        try:
            path = promote_model_to_warm_start_base(id_a, self.db)
            self._update_warm_start_badge()
            self._log(f"Warm-Start Base updated from '{id_a}' ({rec['matches']}m) -> {path}")
            messagebox.showinfo("Warm-Start Ready", f"Warm-Start Base saved successfully from {id_a}!")
        except Exception as exc:
            messagebox.showerror("Warm-Start Error", str(exc))

    def _reset_free10(self) -> None:
        ok = messagebox.askyesno(
            "Reset Free-10 Deck Locks",
            "Clear all saved Free-10 latest decks so Free_01..Free_10 can draft fresh disjoint decks from scratch?\n"
            "(Their neural network weights and match stats will NOT be deleted.)",
        )
        if ok:
            self.store.reset_free10_locks()
            self._log("Reset Free-10 disjoint deck locks.")

    def _open_advisor(self) -> None:
        sugs = suggest_matchups(self.store, limit=6)
        win = tk.Toplevel(self.root)
        win.title("Smart Matchup Advisor — Recommendations")
        win.geometry("760x420")
        win.configure(bg=BG_DARK)
        win.transient(self.root)

        tk.Label(
            win, text="SMART MATCHUP ADVISOR (Advice Only — Click Any Row to Load)",
            bg=BG_DARK, fg=ACCENT_PURPLE, font=("Segoe UI", 11, "bold"),
        ).pack(anchor="w", padx=14, pady=(12, 8))

        for s in sugs:
            row = tk.Frame(win, bg=BG_PANEL, padx=12, pady=8)
            row.pack(fill="x", padx=14, pady=4)
            top = tk.Frame(row, bg=BG_PANEL)
            top.pack(fill="x")
            sa = get_model_spec(s.model_a_id).display_name
            sb = get_model_spec(s.model_b_id).display_name
            tk.Label(
                top, text=f"[{s.tag}]  {sa}  VS  {sb}",
                bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 9, "bold"),
            ).pack(side="left")

            def _apply(a=s.model_a_id, b=s.model_b_id) -> None:
                self._select_pair(a, b)
                self._log(f"Advisor loaded matchup: {a} vs {b}")
                win.destroy()

            tk.Button(
                top, text="Load Pair", command=_apply,
                bg=ACCENT_BLUE, fg=FG_TEXT, relief="flat",
                font=("Segoe UI", 8, "bold"), padx=10, pady=2, cursor="hand2",
            ).pack(side="right")

            tk.Label(
                row, text=s.reason, bg=BG_PANEL, fg=FG_MUTED,
                font=("Segoe UI", 8), wraplength=690, justify="left",
            ).pack(anchor="w", pady=(3, 0))

    def _on_close(self) -> None:
        if self.is_running:
            if not messagebox.askyesno("Training Running", "Stop training cleanly and exit?"):
                return
            self.stop_event.set()
            if self.worker_thread and self.worker_thread.is_alive():
                self.worker_thread.join(timeout=5.0)
        self.root.destroy()


def main() -> None:
    root = tk.Tk()
    ArenaTrainerApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()

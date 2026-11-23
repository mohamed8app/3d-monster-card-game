# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Unified Direct Python Launcher Hub (Tkinter GUI).
Double-click this .py file directly in Windows File Explorer (no .bat needed!):
  - Automatically switches into the local .\venv Python environment
  - Launches Arena_Trainer.py (40-Model CPU Trainer)
  - Launches Arena_Analytics.py (Offline Balance & Deck Viewer)
  - Syncs trained models & logs to Main PC over LAN in pure Python
"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
import threading
import tkinter as tk
from tkinter import messagebox

ROOT_DIR = os.path.abspath(os.path.dirname(__file__))
if ROOT_DIR not in sys.path:
    sys.path.insert(0, ROOT_DIR)

from engine.venv_bootstrap import ensure_venv, find_venv_python

ensure_venv(__file__, require_torch=True)

from engine.ai_brain import get_warm_start_info
from trainer.stats_store import StatsStore

BG_DARK = "#12151C"
BG_PANEL = "#1B202B"
BG_INPUT = "#252C3A"
FG_TEXT = "#EAEFF7"
FG_MUTED = "#98A4B8"
ACCENT_BLUE = "#3B82F6"
ACCENT_GREEN = "#10B981"
ACCENT_GOLD = "#F59E0B"
ACCENT_PURPLE = "#8B5CF6"


class ArenaLabHubApp:
    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title("Arena Lab V2 — Main Control Hub")
        self.root.geometry("620x470")
        self.root.resizable(False, False)
        self.root.configure(bg=BG_DARK)

        self.store = StatsStore()
        self.host_ip_var = tk.StringVar(value="192.168.1.2")
        self.status_var = tk.StringVar(value="Loading status...")
        self.ws_var = tk.StringVar(value="Warm-Start Base: Checking...")
        self.sync_var = tk.StringVar(value="Ready.")

        self._build_ui()
        self._refresh_status()

    def _build_ui(self) -> None:
        # Banner
        hdr = tk.Frame(self.root, bg=BG_PANEL, padx=20, pady=16)
        hdr.pack(fill="x", padx=14, pady=(14, 8))
        tk.Label(
            hdr, text="ARENA LAB V2 — CONTROL HUB",
            bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 16, "bold"),
        ).pack(anchor="w")
        tk.Label(
            hdr, text="40 Independent AI Models | 1,470 Cards | Pure-Python CPU RL Suite",
            bg=BG_PANEL, fg=FG_MUTED, font=("Segoe UI", 9),
        ).pack(anchor="w", pady=(2, 8))
        tk.Label(
            hdr, textvariable=self.status_var,
            bg=BG_INPUT, fg=FG_TEXT, font=("Consolas", 9), padx=10, pady=5, anchor="w",
        ).pack(fill="x")
        tk.Label(
            hdr, textvariable=self.ws_var,
            bg=BG_PANEL, fg=ACCENT_GOLD, font=("Segoe UI", 9, "bold"), anchor="w",
        ).pack(fill="x", pady=(6, 0))

        # Main Launch Buttons
        body = tk.Frame(self.root, bg=BG_DARK, padx=14, pady=6)
        body.pack(fill="x")

        tk.Button(
            body,
            text="OPEN AI TRAINER  (40 Models • Up to 20,000+ Matches)",
            command=self._launch_trainer,
            bg=ACCENT_GREEN, fg="#0B1215", activebackground="#059669",
            font=("Segoe UI", 11, "bold"), relief="flat", pady=14, cursor="hand2",
        ).pack(fill="x", pady=(4, 8))

        tk.Button(
            body,
            text="OPEN ANALYTICS & BALANCE VIEWER  (Decks • ELO • OP/Weak Cards)",
            command=self._launch_analytics,
            bg=ACCENT_BLUE, fg="#FFFFFF", activebackground="#2563EB",
            font=("Segoe UI", 11, "bold"), relief="flat", pady=14, cursor="hand2",
        ).pack(fill="x", pady=(0, 8))

        # LAN Sync Panel
        sync_box = tk.Frame(self.root, bg=BG_PANEL, padx=16, pady=12)
        sync_box.pack(fill="x", padx=14, pady=6)

        row1 = tk.Frame(sync_box, bg=BG_PANEL)
        row1.pack(fill="x")
        tk.Label(
            row1, text="LAN Sync Host IP:", bg=BG_PANEL, fg=FG_TEXT, font=("Segoe UI", 9, "bold")
        ).pack(side="left")
        tk.Entry(
            row1, textvariable=self.host_ip_var, width=15,
            bg=BG_INPUT, fg=FG_TEXT, insertbackground=FG_TEXT,
            font=("Consolas", 10), relief="flat", justify="center",
        ).pack(side="left", padx=8)

        self.btn_sync = tk.Button(
            row1, text="Sync Models & Logs to Main PC", command=self._sync_lan,
            bg=ACCENT_PURPLE, fg="#FFFFFF", activebackground="#7C3AED",
            font=("Segoe UI", 9, "bold"), relief="flat", padx=12, pady=5, cursor="hand2",
        )
        self.btn_sync.pack(side="right")

        tk.Label(
            sync_box, textvariable=self.sync_var, bg=BG_PANEL, fg=FG_MUTED,
            font=("Segoe UI", 8), anchor="w",
        ).pack(fill="x", pady=(6, 0))

        # Footer
        foot = tk.Frame(self.root, bg=BG_DARK, padx=14, pady=4)
        foot.pack(fill="x")
        tk.Button(
            foot, text="Refresh Status", command=self._refresh_status,
            bg=BG_INPUT, fg=FG_TEXT, relief="flat", font=("Segoe UI", 9), padx=10, pady=4, cursor="hand2",
        ).pack(side="left")
        tk.Label(
            foot, text=f"Python: {os.path.basename(sys.executable)}",
            bg=BG_DARK, fg=FG_MUTED, font=("Consolas", 8),
        ).pack(side="right")

    def _python_exe(self) -> str:
        return find_venv_python(ROOT_DIR, prefer_windowless=False) or sys.executable

    def _launch_trainer(self) -> None:
        script = os.path.join(ROOT_DIR, "Arena_Trainer.py")
        subprocess.Popen([self._python_exe(), script])
        self.sync_var.set("Launched Arena_Trainer.py")

    def _launch_analytics(self) -> None:
        script = os.path.join(ROOT_DIR, "Arena_Analytics.py")
        subprocess.Popen([self._python_exe(), script])
        self.sync_var.set("Launched Arena_Analytics.py")

    def _refresh_status(self) -> None:
        self.store.reload()
        total_m = sum(r["matches"] for r in self.store.roster.values()) // 2
        trained = sum(1 for r in self.store.roster.values() if r["matches"] > 0)
        cards_seen = sum(1 for v in self.store.card_stats.values() if v.get("drafted", 0) > 0)
        self.status_var.set(f"Matches: {total_m}  |  Trained Models: {trained}/41  |  Cards Tested: {cards_seen}/1470")
        info = get_warm_start_info()
        if info:
            self.ws_var.set(f"Warm-Start Base: READY (from {info['source_model_id']}, {info['source_matches']} matches)")
        else:
            self.ws_var.set("Warm-Start Base: Not Set Yet (Train Free_01 vs Free_02 first)")

    def _sync_lan(self) -> None:
        ip = self.host_ip_var.get().strip()
        if not ip:
            return
        dest_root = rf"\\{ip}\Arena_Lab_V2"
        if not os.path.isdir(dest_root):
            messagebox.showerror(
                "LAN Share Unreachable",
                f"Cannot access {dest_root}\n\nOpen {dest_root} in File Explorer once first (User: ArenaLAN / Pass: Arena2026).",
            )
            return
        if os.path.normcase(os.path.abspath(dest_root)) == os.path.normcase(os.path.abspath(ROOT_DIR)):
            messagebox.showinfo("Already on Host", "You are already running directly inside the shared Host folder!")
            return

        self.btn_sync.configure(state="disabled")
        self.sync_var.set(f"Syncing saved_models & logs to {dest_root} ...")

        def _worker() -> None:
            copied = 0
            try:
                for sub in ("saved_models", "logs"):
                    src_dir = os.path.join(ROOT_DIR, sub)
                    if not os.path.isdir(src_dir):
                        continue
                    for dirpath, _dirnames, filenames in os.walk(src_dir):
                        rel = os.path.relpath(dirpath, ROOT_DIR)
                        target_dir = os.path.join(dest_root, rel)
                        os.makedirs(target_dir, exist_ok=True)
                        for fn in filenames:
                            if fn.endswith(".tmp"):
                                continue
                            s_file = os.path.join(dirpath, fn)
                            d_file = os.path.join(target_dir, fn)
                            if (not os.path.exists(d_file)) or (os.path.getmtime(s_file) > os.path.getmtime(d_file) + 0.5):
                                shutil.copy2(s_file, d_file)
                                copied += 1
                self.root.after(0, lambda c=copied: self._on_sync_done(f"Synced {c} updated files to {dest_root}!", None))
            except Exception as exc:
                self.root.after(0, lambda e=exc: self._on_sync_done("", e))

        threading.Thread(target=_worker, daemon=True).start()

    def _on_sync_done(self, msg: str, err: Exception | None) -> None:
        self.btn_sync.configure(state="normal")
        if err is not None:
            self.sync_var.set(f"Sync error: {err}")
            messagebox.showerror("LAN Sync Error", str(err))
        else:
            self.sync_var.set(msg)
            messagebox.showinfo("LAN Sync Complete", msg)


def main() -> None:
    root = tk.Tk()
    ArenaLabHubApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()

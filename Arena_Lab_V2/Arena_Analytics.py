# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — منظومة التحليلات والمعايير الـ 13 المعتمدة (واجهة عربية RTL نظيفة وكاملة).
تقرأ مباشرة من بيانات التدريب الحقيقية (سواء على الجهاز الحالي أو مباشرةً من الجهاز الثاني أثناء التدريب بأمان تام):
  1. الإيقاع (Game Pace — 14 مؤشراً)
  2. البقاء (Monster Survival — 16 مؤشراً)
  3. القتال (Combat Quality — 18 مؤشراً)
  4. البطاقات (Card Utilization — 14 مؤشراً)
  5. الموارد (Resource Economy — 14 مؤشراً)
  6. التوازن (Archetype & Roster Balance — 14 مؤشراً)
  7. الميكانيكيات (Mechanics Performance — 14 مؤشراً)
  8. طرق الفوز (Victory Conditions & Win Channels — 14 مؤشراً)
  9. الذكاء (Strategic Intelligence & Skill Ceiling — 14 مؤشراً)
  10. المتعة (Gameplay Enjoyment & Emotional Arc — 14 مؤشراً)
  11. العدالة (Competitive Fairness & Mirror Audit — 14 مؤشراً)
  12. السلامة (Simulation Integrity, Determinism & Fuzz Audit — 14 فحصاً)
  13. الميتا (Meta Discovery, Strategic Entropy & Synergies — 14 مؤشراً)
"""

from __future__ import annotations

import json
import os
import shutil
import sys
import threading
import time
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
from typing import Any, Callable, Dict, List, Optional, Tuple

ROOT_DIR = os.path.abspath(os.path.dirname(__file__))
if ROOT_DIR not in sys.path:
    sys.path.insert(0, ROOT_DIR)

from engine.venv_bootstrap import ensure_venv

ensure_venv(__file__, require_torch=False)

from engine.card_model import get_card_db
from trainer.stats_store import StatsStore
from arabic_glossary import auto_expand_treeview_columns, export_full_analytics_report, open_arabic_skills_guide
from pace_tab import GamePaceTab, rtl
from survival_tab import MonsterSurvivalTab
from combat_tab import CombatQualityTab
from utilization_tab import CardUtilizationTab
from resource_tab import ResourceEconomyTab
from balance_tab import ArchetypeBalanceTab
from mechanics_tab import MechanicsPerformanceTab
from victory_tab import VictoryConditionsTab
from strategy_tab import StrategicIntelligenceTab
from enjoyment_tab import GameplayEnjoymentTab
from fairness_tab import CompetitiveFairnessTab
from integrity_tab import SimulationIntegrityTab
from meta_tab import MetaDiscoveryTab
from decks_tab import FinalDecksTab
from abilities_eval_tab import AbilitiesAndSpellsEvalTab
from master_fun_tab import MasterFunTab
from skill_pricing_tab import SkillPricingAdvisorTab

BG_DARK = "#12151C"
BG_PANEL = "#1B202B"
BG_SUB = "#161B26"
BG_INPUT = "#252C3A"
FG_TEXT = "#EAEFF7"
FG_MUTED = "#98A4B8"
ACCENT_BLUE = "#3B82F6"
ACCENT_GOLD = "#F59E0B"
ACCENT_GREEN = "#059669"
ACCENT_PURPLE = "#7C3AED"
ACCENT_AMBER = "#D97706"
ACCENT_RED = "#EF4444"
FONT_UI = "Segoe UI"

CONFIG_PATH = os.path.join(ROOT_DIR, "logs", "analytics_config.json")
LOCAL_LOGS_DIR = os.path.join(ROOT_DIR, "logs")
LOCAL_MODELS_DIR = os.path.join(ROOT_DIR, "saved_models")
EXPORTS_DIR = os.path.join(ROOT_DIR, "exports")


def resolve_remote_dirs(raw_path: str) -> Tuple[str, str, str]:
    """
    Resolves any user-provided PC #2 folder path (e.g. Z:\\, \\\\192.168.1.3\\Arena_Lab_V2,
    or directly ...\\Arena_Lab_V2\\logs) into (remote_root, remote_logs_dir, remote_saved_models_dir).
    """
    p = (raw_path or "").strip().strip('"').strip("'")
    if not p:
        return ("", "", "")
    p_norm = os.path.normpath(p)
    base = os.path.basename(p_norm).lower()
    if base in ("logs", "saved_models"):
        parent = os.path.dirname(p_norm)
        return (parent, os.path.join(parent, "logs"), os.path.join(parent, "saved_models"))
    return (p_norm, os.path.join(p_norm, "logs"), os.path.join(p_norm, "saved_models"))


def _safe_copy_atomic(src: str, dst: str, allow_bak_fallback: bool = True) -> bool:
    """
    Lock-safe, retry-enabled copy from a live training PC #2 into a local atomic temp file.
    Never modifies or locks the remote source file!
    """
    candidates = [src]
    if allow_bak_fallback and src.lower().endswith(".json") and os.path.isfile(src + ".bak"):
        candidates.append(src + ".bak")

    os.makedirs(os.path.dirname(dst), exist_ok=True)
    tmp_dst = f"{dst}.{os.getpid()}.pull.tmp"

    for cand in candidates:
        if not os.path.isfile(cand):
            continue
        for attempt in range(8):
            try:
                sz = os.path.getsize(cand)
                if sz == 0 and cand.lower().endswith(".json"):
                    time.sleep(0.06)
                    continue
                shutil.copy2(cand, tmp_dst)
                # Validate JSON integrity before replacing local file so we never pull a half-written JSON!
                if cand.lower().endswith(".json"):
                    with open(tmp_dst, "r", encoding="utf-8") as vf:
                        json.load(vf)
                os.replace(tmp_dst, dst)
                return True
            except PermissionError:
                time.sleep(0.08)
            except Exception:
                try:
                    if os.path.isfile(tmp_dst):
                        os.remove(tmp_dst)
                except Exception:
                    pass
                time.sleep(0.05)
                break
    return False


def pull_training_results_from_remote(
    remote_path: str,
    local_root: str = ROOT_DIR,
    progress_cb: Optional[Callable[[str], None]] = None,
) -> Dict[str, Any]:
    """
    Safely pulls ALL training logs (logs/*.json, *.jsonl) AND ALL trained AI models
    (saved_models/<model_id>/* including brain.pt & settled_deck.json) from PC #2 to Main PC.
    Works safely even while PC #2 is actively training (read-only on remote + atomic replace locally).
    """
    rem_root, rem_logs, rem_saved = resolve_remote_dirs(remote_path)
    if not rem_root or (not os.path.isdir(rem_logs) and not os.path.isdir(rem_saved)):
        raise FileNotFoundError(f"المسار غير متاح أو لا يحتوي على logs / saved_models: {remote_path}")

    loc_logs = os.path.join(local_root, "logs")
    loc_saved = os.path.join(local_root, "saved_models")
    os.makedirs(loc_logs, exist_ok=True)
    os.makedirs(loc_saved, exist_ok=True)

    skip_log_names = {
        "analytics_config.json",
        "live_probe_snapshot.json",
        "balance_probe_v22.json",
        "integrity_audit.json",
    }

    logs_copied = 0
    models_copied = 0
    model_dirs = 0
    skipped = 0
    errors: List[str] = []

    # 1. Pull all training logs
    if os.path.isdir(rem_logs):
        if progress_cb:
            progress_cb("1/2 جاري سحب ملفات السجلات والإحصائيات من الجهاز الثاني...")
        for fn in sorted(os.listdir(rem_logs)):
            low = fn.lower()
            if low in skip_log_names or low.endswith(".tmp") or low.endswith(".bak"):
                continue
            if not (low.endswith(".json") or low.endswith(".jsonl")):
                continue
            src = os.path.join(rem_logs, fn)
            if not os.path.isfile(src):
                continue
            dst = os.path.join(loc_logs, fn)
            if _safe_copy_atomic(src, dst, allow_bak_fallback=low.endswith(".json")):
                logs_copied += 1
            else:
                skipped += 1
                errors.append(f"logs/{fn}")

    # 2. Pull all saved_models folders (brains + settled_deck.json)
    if os.path.isdir(rem_saved):
        entries = sorted(os.listdir(rem_saved))
        total_dirs = sum(1 for e in entries if os.path.isdir(os.path.join(rem_saved, e)))
        done_dirs = 0
        for entry in entries:
            s_dir = os.path.join(rem_saved, entry)
            if not os.path.isdir(s_dir):
                continue
            done_dirs += 1
            if progress_cb and (done_dirs == 1 or done_dirs % 5 == 0 or done_dirs == total_dirs):
                progress_cb(f"2/2 جاري سحب النماذج والديكات ({done_dirs}/{total_dirs}): {entry}...")
            d_dir = os.path.join(loc_saved, entry)
            os.makedirs(d_dir, exist_ok=True)
            dir_had_files = False
            for fn in sorted(os.listdir(s_dir)):
                low = fn.lower()
                if low.endswith(".tmp") or low.endswith(".bak"):
                    continue
                src = os.path.join(s_dir, fn)
                if not os.path.isfile(src):
                    continue
                dst = os.path.join(d_dir, fn)
                if _safe_copy_atomic(src, dst, allow_bak_fallback=low.endswith(".json")):
                    models_copied += 1
                    dir_had_files = True
                else:
                    skipped += 1
                    errors.append(f"saved_models/{entry}/{fn}")
            if dir_had_files:
                model_dirs += 1

    return {
        "logs_copied": logs_copied,
        "models_copied": models_copied,
        "model_dirs": model_dirs,
        "skipped": skipped,
        "errors": errors,
        "remote_root": rem_root,
    }


def _default_remote_path() -> str:
    if os.path.isdir(r"Y:\logs"):
        return r"Y:\\"
    info_p = os.path.join(LOCAL_LOGS_DIR, "pc2_host_info.json")
    if os.path.isfile(info_p):
        try:
            with open(info_p, "r", encoding="utf-8") as f:
                sh = str(json.load(f).get("share", "")).strip()
            if sh:
                return sh
        except Exception:
            pass
    if os.path.isdir(r"Z:\logs"):
        return r"Z:\\"
    return r"\\192.168.1.6\Arena_Lab_V2"


def _load_config() -> Dict[str, Any]:
    cfg = {
        "mode": "local",
        "remote_dir": _default_remote_path(),
        "auto_refresh": False,
    }
    if os.path.isfile(CONFIG_PATH):
        try:
            with open(CONFIG_PATH, "r", encoding="utf-8") as f:
                raw = json.load(f)
            if isinstance(raw, dict):
                cfg.update(raw)
        except Exception:
            pass
    return cfg


def _save_config(cfg: Dict[str, Any]) -> None:
    try:
        os.makedirs(os.path.dirname(CONFIG_PATH), exist_ok=True)
        tmp = CONFIG_PATH + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(cfg, f, indent=2, ensure_ascii=False)
        os.replace(tmp, CONFIG_PATH)
    except Exception:
        pass


class ArenaAnalyticsApp:
    def __init__(
        self,
        root: tk.Tk,
        cli_remote: Optional[str] = None,
        cli_auto_refresh: Optional[bool] = None,
    ) -> None:
        self.root = root
        self.root.title(rtl("مختبر Arena Lab V2 — منظومة التحليل والمعايير الـ 13 (بيانات التدريب المباشرة)"))
        self.root.geometry("1440x880")
        self.root.minsize(1000, 680)
        try:
            self.root.state("zoomed")
        except Exception:
            pass
        self.root.configure(bg=BG_DARK)

        self.cfg = _load_config()
        if cli_remote:
            self.cfg["mode"] = "remote"
            self.cfg["remote_dir"] = cli_remote
        if cli_auto_refresh is not None:
            self.cfg["auto_refresh"] = bool(cli_auto_refresh)
        self.db = get_card_db()
        # Analytics ALWAYS opens StatsStore in read_only=True so it NEVER contends with an active trainer on EITHER PC!
        self.store = StatsStore(logs_dir=LOCAL_LOGS_DIR, saved_models_dir=LOCAL_MODELS_DIR, read_only=True)
        self.active_source_label = "الجهاز الحالي (محلي)"

        self.mode_var = tk.StringVar(value=str(self.cfg.get("mode", "local")))
        self.remote_path_var = tk.StringVar(value=str(self.cfg.get("remote_dir", _default_remote_path())))
        self.auto_refresh_var = tk.BooleanVar(value=bool(self.cfg.get("auto_refresh", False)))
        self.sync_status_var = tk.StringVar(value=rtl("🟢 متصل ببيانات التدريب المحلية (قراءة آمنة)"))

        self._pull_running = False
        self._auto_job: Optional[str] = None

        self._setup_styles()
        self._build_ui()
        self._apply_data_source(silent=True)
        self._schedule_auto_refresh()

    def _setup_styles(self) -> None:
        style = ttk.Style()
        try:
            style.theme_use("clam")
        except Exception:
            pass
        style.configure(
            "TCombobox",
            fieldbackground="#1E2636",
            background="#2B3548",
            foreground="#FFFFFF",
            arrowcolor="#F59E0B",
            bordercolor="#3B475E",
            padding=4,
            font=(FONT_UI, 9),
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
        self.root.option_add("*TCombobox*Listbox*Font", (FONT_UI, 9))
        style.configure("TNotebook", background=BG_DARK, borderwidth=0, tabposition="ne")
        style.configure("TNotebook.Tab", background=BG_PANEL, foreground=FG_MUTED, padding=(4, 7), font=(FONT_UI, 8, "bold"))
        style.map("TNotebook.Tab", background=[("selected", ACCENT_BLUE)], foreground=[("selected", "#FFFFFF")])
        style.configure(
            "Treeview",
            background=BG_PANEL,
            foreground=FG_TEXT,
            fieldbackground=BG_PANEL,
            rowheight=26,
            font=(FONT_UI, 9),
        )
        style.configure("Treeview.Heading", background=BG_INPUT, foreground=ACCENT_GOLD, font=(FONT_UI, 9, "bold"))
        style.map("Treeview", background=[("selected", "#2E405E")])
        style.configure(
            "Horizontal.TScrollbar",
            background="#2E3C54",
            troughcolor="#141822",
            bordercolor="#1B202B",
            arrowcolor="#F59E0B",
            lightcolor="#3B82F6",
            darkcolor="#1D2432",
        )
        style.map(
            "Horizontal.TScrollbar",
            background=[("active", "#3B82F6"), ("pressed", "#1D4ED8")],
            arrowcolor=[("active", "#FFFFFF")],
        )
        style.configure(
            "Vertical.TScrollbar",
            background="#2E3C54",
            troughcolor="#141822",
            bordercolor="#1B202B",
            arrowcolor="#F59E0B",
            lightcolor="#3B82F6",
            darkcolor="#1D2432",
        )
        style.map(
            "Vertical.TScrollbar",
            background=[("active", "#3B82F6"), ("pressed", "#1D4ED8")],
            arrowcolor=[("active", "#FFFFFF")],
        )

    def _build_ui(self) -> None:
        # ==============================================================
        # Row 1: Main RTL Header + Guide & ChatGPT Export Buttons
        # ==============================================================
        hdr = tk.Frame(self.root, bg=BG_PANEL, padx=14, pady=8)
        hdr.pack(fill="x", padx=10, pady=(8, 2))

        right_box = tk.Frame(hdr, bg=BG_PANEL)
        right_box.pack(side="right", fill="x", expand=True)
        tk.Label(
            right_box,
            text=rtl("مختبر ARENA LAB V2 — منظومة التحليل الـ 13 (بيانات التدريب المباشرة • اضغط أي عنوان عمود للترتيب)"),
            bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 12, "bold"), anchor="e", justify="right",
        ).pack(anchor="e")
        self.summary_var = tk.StringVar(value=rtl("جاري تحميل إحصائيات التدريب..."))
        tk.Label(
            right_box, textvariable=self.summary_var,
            bg=BG_PANEL, fg=FG_MUTED, font=(FONT_UI, 9), anchor="e", justify="right",
        ).pack(anchor="e")

        btn_box = tk.Frame(hdr, bg=BG_PANEL)
        btn_box.pack(side="left")

        tk.Button(
            btn_box, text=rtl("تحديث الآن"), command=self.refresh_all,
            bg=ACCENT_BLUE, fg="#FFFFFF", relief="flat",
            font=(FONT_UI, 9, "bold"), padx=11, pady=5, cursor="hand2",
        ).pack(side="left", padx=(0, 5))

        tk.Button(
            btn_box, text=rtl("تصدير لـ ChatGPT (MD + JSON + CSV)"), command=self._on_export_report,
            bg=ACCENT_GREEN, fg="#FFFFFF", relief="flat",
            font=(FONT_UI, 9, "bold"), padx=11, pady=5, cursor="hand2",
        ).pack(side="left", padx=(0, 5))

        tk.Button(
            btn_box, text=rtl("دليل القدرات والمهارات (عربي)"), command=self._on_open_arabic_guide,
            bg=ACCENT_PURPLE, fg="#FFFFFF", relief="flat",
            font=(FONT_UI, 9, "bold"), padx=11, pady=5, cursor="hand2",
        ).pack(side="left")

        # ==============================================================
        # Row 2: Live PC #2 Remote Reader + Pull All Training Results Bar
        # ==============================================================
        net_bar = tk.Frame(self.root, bg=BG_SUB, highlightbackground="#2A3346", highlightthickness=1, padx=12, pady=6)
        net_bar.pack(fill="x", padx=10, pady=(2, 4))

        # Right side: Mode Radios + Remote Path Entry + Browse + Apply + Auto-Refresh
        tk.Label(
            net_bar, text=rtl("مصدر البيانات:"), bg=BG_SUB, fg=ACCENT_GOLD, font=(FONT_UI, 9, "bold"),
        ).pack(side="right", padx=(6, 0))

        tk.Radiobutton(
            net_bar, text=rtl("الجهاز الحالي (محلي)"), variable=self.mode_var, value="local",
            command=lambda: self._apply_data_source(silent=False),
            bg=BG_SUB, fg=FG_TEXT, selectcolor=BG_INPUT, activebackground=BG_SUB, activeforeground=FG_TEXT,
            font=(FONT_UI, 8, "bold"), cursor="hand2",
        ).pack(side="right", padx=(4, 2))

        tk.Radiobutton(
            net_bar, text=rtl("مباشر من الجهاز الثاني (أثناء التدريب)"), variable=self.mode_var, value="remote",
            command=lambda: self._apply_data_source(silent=False),
            bg=BG_SUB, fg="#38BDF8", selectcolor=BG_INPUT, activebackground=BG_SUB, activeforeground="#38BDF8",
            font=(FONT_UI, 8, "bold"), cursor="hand2",
        ).pack(side="right", padx=(8, 2))

        tk.Label(
            net_bar, text=rtl("مجلد الجهاز الثاني:"), bg=BG_SUB, fg=FG_MUTED, font=(FONT_UI, 8, "bold"),
        ).pack(side="right", padx=(6, 2))

        self.remote_entry = tk.Entry(
            net_bar, textvariable=self.remote_path_var, bg=BG_INPUT, fg="#FFFFFF",
            insertbackground="#FFFFFF", relief="flat", width=30, font=(FONT_UI, 8),
        )
        self.remote_entry.pack(side="right", padx=3, ipady=3)
        self.remote_entry.bind("<Return>", lambda _e: self._on_connect_remote())

        tk.Button(
            net_bar, text=rtl("استعراض..."), command=self._on_browse_remote,
            bg="#2B3548", fg=FG_TEXT, relief="flat", font=(FONT_UI, 8, "bold"), padx=7, pady=3, cursor="hand2",
        ).pack(side="right", padx=2)

        tk.Button(
            net_bar, text=rtl("اتصال"), command=self._on_connect_remote,
            bg="#0284C7", fg="#FFFFFF", relief="flat", font=(FONT_UI, 8, "bold"), padx=8, pady=3, cursor="hand2",
        ).pack(side="right", padx=(2, 8))

        tk.Checkbutton(
            net_bar, text=rtl("تحديث تلقائي (15ث)"), variable=self.auto_refresh_var,
            command=self._on_toggle_auto_refresh,
            bg=BG_SUB, fg=FG_TEXT, selectcolor=BG_INPUT, activebackground=BG_SUB, activeforeground=FG_TEXT,
            font=(FONT_UI, 8), cursor="hand2",
        ).pack(side="right", padx=4)

        # Left side: Pull All Training Results Button + Live Status
        self.pull_btn = tk.Button(
            net_bar,
            text=rtl("سحب كل نتائج التدريب من الجهاز التاني"),
            command=self._on_pull_from_pc2,
            bg=ACCENT_AMBER, fg="#111827", relief="flat",
            font=(FONT_UI, 9, "bold"), padx=12, pady=4, cursor="hand2",
        )
        self.pull_btn.pack(side="left")

        # Status micro-strip under Row 2
        st_bar = tk.Frame(self.root, bg=BG_DARK, padx=12)
        st_bar.pack(fill="x")
        self.sync_status_lbl = tk.Label(
            st_bar, textvariable=self.sync_status_var,
            bg=BG_DARK, fg="#4ADE80", font=(FONT_UI, 8, "bold"), anchor="e", justify="right",
        )
        self.sync_status_lbl.pack(side="right")

        # ==============================================================
        # Notebook Tabs — All 13 Independent Arabic RTL Pillars
        # ==============================================================
        self.nb = ttk.Notebook(self.root)
        self.nb.pack(fill="both", expand=True, padx=10, pady=(2, 8))

        self._build_tab_game_pace()
        self._build_tab_monster_survival()
        self._build_tab_combat_quality()
        self._build_tab_card_utilization()
        self._build_tab_resource_economy()
        self._build_tab_archetype_balance()
        self._build_tab_mechanics_perf()
        self._build_tab_victory_cond()
        self._build_tab_strategic_intel()
        self._build_tab_enjoyment()
        self._build_tab_fairness()
        self._build_tab_integrity()
        self._build_tab_meta()
        self._build_tab_decks()
        self._build_tab_abilities_eval()
        self._build_tab_master_fun()
        self._build_tab_skill_pricing()

        self._all_tabs = [
            self.pace_tab,
            self.survival_tab,
            self.combat_tab,
            self.utilization_tab,
            self.resource_tab,
            self.balance_tab,
            self.mechanics_tab,
            self.victory_tab,
            self.strategy_tab,
            self.enjoyment_tab,
            self.fairness_tab,
            self.integrity_tab,
            self.meta_tab,
            self.decks_tab,
            self.abilities_eval_tab,
            self.master_fun_tab,
            self.skill_pricing_tab,
        ]
        # Central global mousewheel handlers (replaces the overwritten per-tab bind_all so ALL tabs scroll smoothly!)
        self.root.bind_all("<MouseWheel>", self._on_global_mousewheel)
        self.root.bind_all("<Shift-MouseWheel>", self._on_global_shift_mousewheel)
        self.nb.bind("<<NotebookTabChanged>>", self._on_notebook_tab_changed)

    def _on_notebook_tab_changed(self, event: Optional[tk.Event] = None) -> None:
        """Auto-expands all Treeviews in the newly selected tab and lazily refreshes dirty tabs."""
        try:
            sel = self.nb.select()
            if not sel:
                return
            idx = int(self.nb.index(sel))
            if 0 <= idx < len(self._all_tabs):
                tab_obj = self._all_tabs[idx]
                if getattr(tab_obj, "_is_dirty", False):
                    tab_obj._is_dirty = False
                    if hasattr(tab_obj, "refresh"):
                        tab_obj.refresh()

                def _fit_tab_trees(target_tab: Any = tab_obj) -> None:
                    try:
                        sc = getattr(target_tab, "scroll_canvas", None)
                        inner_win = getattr(target_tab, "_inner_win", None)
                        if sc is not None and inner_win is not None:
                            sw = int(sc.winfo_width())
                            if sw > 200:
                                sc.itemconfigure(inner_win, width=sw)
                        for attr_name in dir(target_tab):
                            if attr_name.endswith("_tree") or attr_name == "tree":
                                t = getattr(target_tab, attr_name, None)
                                if isinstance(t, ttk.Treeview) and t.winfo_exists():
                                    auto_expand_treeview_columns(t)
                    except Exception:
                        pass

                self.root.after(25, _fit_tab_trees)
        except Exception:
            pass

    def _on_global_shift_mousewheel(self, event: tk.Event) -> None:
        """Enables smooth horizontal scrolling across ANY Treeview when holding Shift."""
        try:
            delta_units = int(-1 * (event.delta / 120)) if event.delta else 0
            if delta_units == 0:
                return
            cur = event.widget
            while cur is not None:
                if isinstance(cur, ttk.Treeview):
                    try:
                        cur.xview_scroll(delta_units, "units")
                        return
                    except Exception:
                        pass
                    break
                cur = getattr(cur, "master", None)
        except Exception:
            pass

    def _on_global_mousewheel(self, event: tk.Event) -> None:
        try:
            delta_units = int(-1 * (event.delta / 120)) if event.delta else 0
            if delta_units == 0:
                return
            w = event.widget
            # 1. If cursor is over a scrollable Treeview/Text/Listbox (e.g. inside Arabic Skills Guide popup), scroll it
            cur = w
            while cur is not None:
                if isinstance(cur, (ttk.Treeview, tk.Text, tk.Listbox)):
                    try:
                        yv = cur.yview()
                        if yv != (0.0, 1.0):
                            cur.yview_scroll(delta_units, "units")
                            return
                    except Exception:
                        pass
                    break
                cur = getattr(cur, "master", None)

            # 2. Otherwise scroll the currently active Pillar Tab's scroll_canvas!
            idx = int(self.nb.index(self.nb.select()))
            if 0 <= idx < len(self._all_tabs):
                sc = getattr(self._all_tabs[idx], "scroll_canvas", None)
                if sc is not None:
                    sc.yview_scroll(delta_units, "units")
        except Exception:
            pass

    def _save_current_config(self) -> None:
        self.cfg["mode"] = self.mode_var.get()
        self.cfg["remote_dir"] = self.remote_path_var.get().strip()
        self.cfg["auto_refresh"] = bool(self.auto_refresh_var.get())
        _save_config(self.cfg)

    def _on_browse_remote(self) -> None:
        cur = self.remote_path_var.get().strip()
        init_dir = cur if os.path.isdir(cur) else ROOT_DIR
        chosen = filedialog.askdirectory(title="اختر مجلد Arena_Lab_V2 المشترك على الجهاز الثاني", initialdir=init_dir)
        if chosen:
            self.remote_path_var.set(chosen)
            self.mode_var.set("remote")
            self._apply_data_source(silent=False)

    def _on_connect_remote(self) -> None:
        self.mode_var.set("remote")
        self._apply_data_source(silent=False)

    def _on_toggle_auto_refresh(self) -> None:
        self._save_current_config()
        self._schedule_auto_refresh()

    def _schedule_auto_refresh(self) -> None:
        if self._auto_job is not None:
            try:
                self.root.after_cancel(self._auto_job)
            except Exception:
                pass
            self._auto_job = None
        if self.auto_refresh_var.get():
            self._auto_job = self.root.after(15000, self._auto_refresh_tick)

    def _auto_refresh_tick(self) -> None:
        self._auto_job = None
        if self.auto_refresh_var.get() and not self._pull_running:
            try:
                self.refresh_all()
                # Also detect when PC #2 transitions from running -> idle while Analytics is watching!
                st_p = os.path.join(self.store.logs_dir, "pc2_status.json")
                if os.path.isfile(st_p):
                    with open(st_p, "r", encoding="utf-8") as sf:
                        st_d = json.load(sf)
                    cur_st = str(st_d.get("state", "idle")) if isinstance(st_d, dict) else "idle"
                    prev_st = getattr(self, "_prev_pc2_state", "idle")
                    self._prev_pc2_state = cur_st
                    if prev_st in ("running", "stopping") and cur_st == "idle":
                        from pc2_headless_worker import play_training_complete_sound_10s
                        play_training_complete_sound_10s()
            except Exception:
                pass
            self._schedule_auto_refresh()

    def _apply_data_source(self, silent: bool = False) -> None:
        mode = self.mode_var.get()
        self._save_current_config()
        if mode == "remote":
            raw_p = self.remote_path_var.get().strip()
            rem_root, rem_logs, rem_saved = resolve_remote_dirs(raw_p)
            if rem_root and os.path.isdir(rem_logs):
                self.store.set_source(logs_dir=rem_logs, saved_models_dir=rem_saved, read_only=True)
                self.active_source_label = f"مباشر من الجهاز الثاني ({rem_root})"
                self.sync_status_lbl.config(fg="#38BDF8")
                self.sync_status_var.set(
                    rtl(f"🟢 متصل مباشرةً بالجهاز الثاني (قراءة آمنة بدون مقاطعة التدريب):  {rem_root}")
                )
            else:
                # Fall back safely to local if remote path is unreachable right now
                self.store.set_source(logs_dir=LOCAL_LOGS_DIR, saved_models_dir=LOCAL_MODELS_DIR, read_only=True)
                self.active_source_label = "الجهاز الحالي (محلي — تعذّر الوصول للجهاز الثاني)"
                self.sync_status_lbl.config(fg=ACCENT_RED)
                self.sync_status_var.set(
                    rtl(f"🔴 تعذّر الوصول لمجلد الجهاز الثاني ({raw_p}) — تأكد من المشاركة الشبكية أو اضغط استعراض...")
                )
                if not silent:
                    messagebox.showwarning(
                        "تعذّر الاتصال بالجهاز الثاني",
                        f"المسار غير متاح حالياً:\n{raw_p}\n\nتأكد أن الجهاز الثاني يعمل ومجلد Arena_Lab_V2 مشارك على الشبكة (مثل Z:\\ أو \\\\IP\\Arena_Lab_V2).",
                    )
        else:
            self.store.set_source(logs_dir=LOCAL_LOGS_DIR, saved_models_dir=LOCAL_MODELS_DIR, read_only=True)
            self.active_source_label = "الجهاز الحالي (محلي)"
            self.sync_status_lbl.config(fg="#4ADE80")
            self.sync_status_var.set(rtl(f"🟢 يعرض مباشرة من بيانات التدريب المحلية:  {LOCAL_LOGS_DIR}"))

        self.refresh_all()

    def _on_pull_from_pc2(self) -> None:
        if self._pull_running:
            return
        raw_p = self.remote_path_var.get().strip()
        rem_root, rem_logs, rem_saved = resolve_remote_dirs(raw_p)
        if not rem_root or (not os.path.isdir(rem_logs) and not os.path.isdir(rem_saved)):
            messagebox.showwarning(
                "مسار الجهاز الثاني غير متاح",
                f"لم يتم العثور على مجلد logs أو saved_models في:\n{raw_p}\n\nاضغط (استعراض...) واختر مجلد Arena_Lab_V2 على الجهاز الثاني أولاً.",
            )
            return

        self._pull_running = True
        self._save_current_config()
        self.pull_btn.config(state="disabled", text=rtl("جاري سحب نتائج التدريب..."))
        self.sync_status_lbl.config(fg=ACCENT_GOLD)
        self.sync_status_var.set(rtl("⏳ جاري سحب كل السجلات والنماذج والديكات من الجهاز الثاني بأمان..."))

        def _worker() -> None:
            try:
                res = pull_training_results_from_remote(
                    raw_p,
                    local_root=ROOT_DIR,
                    progress_cb=lambda msg: self.root.after(0, lambda m=msg: self.sync_status_var.set(rtl(f"⏳ {m}"))),
                )

                def _done_ok() -> None:
                    self._pull_running = False
                    self.pull_btn.config(state="normal", text=rtl("سحب كل نتائج التدريب من الجهاز التاني"))
                    self.sync_status_lbl.config(fg="#4ADE80")
                    summary = (
                        f"تم سحب نتائج التدريب بنجاح! "
                        f"({res['logs_copied']} ملف سجلات • {res['models_copied']} ملف عبر {res['model_dirs']} نموذجاً)"
                    )
                    if res["skipped"] > 0:
                        summary += f" — تم تخطي {res['skipped']} ملف مشغول لحظياً"
                    self.sync_status_var.set(rtl(f"🟢 {summary}"))
                    # Switch to local or reload active store so the newly pulled results are live right away
                    if self.mode_var.get() != "remote":
                        self.store.set_source(logs_dir=LOCAL_LOGS_DIR, saved_models_dir=LOCAL_MODELS_DIR, read_only=True)
                    self.refresh_all()
                    messagebox.showinfo("اكتمل سحب نتائج التدريب", summary)

                self.root.after(0, _done_ok)
            except Exception as err:
                def _done_err(msg: str = str(err)) -> None:
                    self._pull_running = False
                    self.pull_btn.config(state="normal", text=rtl("سحب كل نتائج التدريب من الجهاز التاني"))
                    self.sync_status_lbl.config(fg=ACCENT_RED)
                    self.sync_status_var.set(rtl(f"🔴 خطأ أثناء السحب: {msg[:80]}"))
                    messagebox.showerror("خطأ أثناء السحب", msg)

                self.root.after(0, _done_err)

        threading.Thread(target=_worker, daemon=True).start()

    def _on_open_arabic_guide(self) -> None:
        open_arabic_skills_guide(self.root, self.db, self.store)

    def _on_export_report(self) -> None:
        try:
            md_path, json_path, csv_path, md_text = export_full_analytics_report(self, EXPORTS_DIR)
            copied = False
            try:
                self.root.clipboard_clear()
                self.root.clipboard_append(md_text)
                self.root.update()
                copied = True
            except Exception:
                copied = False

            self.sync_status_lbl.config(fg="#4ADE80")
            self.sync_status_var.set(
                rtl(f"🟢 تم تصدير التقرير الشامل لـ ChatGPT ({os.path.basename(md_path)})" + (" ونسخه للحافظة!" if copied else "!"))
            )

            win = tk.Toplevel(self.root)
            win.title(rtl("تم تصدير جميع النتائج بنجاح — جاهز لـ ChatGPT"))
            win.geometry("680x310")
            win.configure(bg=BG_DARK)
            tk.Label(
                win,
                text=rtl("✅ تم تصدير كل الـ 13 معياراً (188 مؤشراً) + كل الـ 41 نموذجاً بالعربي + البطاقات بـ 3 صيغ:"),
                bg=BG_DARK, fg="#4ADE80", font=(FONT_UI, 10, "bold"), anchor="e", justify="right",
            ).pack(fill="x", padx=14, pady=(14, 8))

            info = (
                f"1) ملف Markdown جاهز للّصق أو الرفع مباشرة لـ ChatGPT:\n   {md_path}\n\n"
                f"2) ملف JSON منظم للتحليل البرمجي أو GPT Code Interpreter:\n   {json_path}\n\n"
                f"3) ملف CSV عربي متوافق مع Excel:\n   {csv_path}\n\n"
                + ("📋 تم أيضاً نسخ التقرير الكامل (Markdown) إلى الحافظة تلقائياً — يمكنك لصقه بـ Ctrl+V في ChatGPT فوراً!" if copied else "")
            )
            txt = tk.Text(win, bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9), height=9, relief="flat", padx=10, pady=8)
            txt.insert("1.0", info)
            txt.config(state="disabled")
            txt.pack(fill="both", expand=True, padx=14, pady=(0, 10))

            brow = tk.Frame(win, bg=BG_DARK)
            brow.pack(fill="x", padx=14, pady=(0, 12))
            tk.Button(
                brow, text=rtl("فتح مجلد التصدير (exports)"),
                command=lambda: os.startfile(EXPORTS_DIR) if hasattr(os, "startfile") else None,
                bg=ACCENT_GREEN, fg="#FFFFFF", relief="flat", font=(FONT_UI, 9, "bold"), padx=14, pady=6, cursor="hand2",
            ).pack(side="right", padx=(0, 8))
            tk.Button(
                brow, text=rtl("إغلاق"), command=win.destroy,
                bg=BG_INPUT, fg=FG_TEXT, relief="flat", font=(FONT_UI, 9, "bold"), padx=14, pady=6, cursor="hand2",
            ).pack(side="left")
        except Exception as err:
            messagebox.showerror("خطأ في التصدير", str(err))

    def _on_probe_updated(self) -> None:
        self.refresh_all()

    def _build_tab_game_pace(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("1. الإيقاع"))
        self.pace_tab = GamePaceTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_monster_survival(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("2. البقاء"))
        self.survival_tab = MonsterSurvivalTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_combat_quality(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("3. القتال"))
        self.combat_tab = CombatQualityTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_card_utilization(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("4. البطاقات"))
        self.utilization_tab = CardUtilizationTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_resource_economy(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("5. الموارد"))
        self.resource_tab = ResourceEconomyTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_archetype_balance(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("6. التوازن"))
        self.balance_tab = ArchetypeBalanceTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_mechanics_perf(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("7. الميكانيكيات"))
        self.mechanics_tab = MechanicsPerformanceTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_victory_cond(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("8. طرق الفوز"))
        self.victory_tab = VictoryConditionsTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_strategic_intel(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("9. الذكاء"))
        self.strategy_tab = StrategicIntelligenceTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_enjoyment(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("10. المتعة"))
        self.enjoyment_tab = GameplayEnjoymentTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_fairness(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("11. العدالة"))
        self.fairness_tab = CompetitiveFairnessTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_integrity(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("12. السلامة"))
        self.integrity_tab = SimulationIntegrityTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_meta(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("13. الميتا"))
        self.meta_tab = MetaDiscoveryTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_decks(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("14. الديكات"))
        self.decks_tab = FinalDecksTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_abilities_eval(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("15. القدرات والسحر"))
        self.abilities_eval_tab = AbilitiesAndSpellsEvalTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_master_fun(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("16. المتعة القصوى ★"))
        self.master_fun_tab = MasterFunTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def _build_tab_skill_pricing(self) -> None:
        tab = tk.Frame(self.nb, bg=BG_DARK)
        self.nb.add(tab, text=rtl("17. تسعير المهارات ⚡"))
        self.skill_pricing_tab = SkillPricingAdvisorTab(tab, self.db, self.store, on_probe_complete=self._on_probe_updated)

    def refresh_all(self, force_all: bool = False) -> None:
        self.store.reload()
        total_entries = sum(r["matches"] for r in self.store.roster.values()) // 2
        trained_cnt = sum(1 for r in self.store.roster.values() if r["matches"] > 0)
        cards_seen = sum(1 for v in self.store.card_stats.values() if v.get("drafted", 0) > 0)
        now_s = time.strftime("%H:%M:%S")
        self.summary_var.set(
            rtl(
                f"إجمالي المباريات المسجلة: {total_entries:,}  |  النماذج المدرّبة: {trained_cnt} / 41  |  "
                f"البطاقات المختبرة: {cards_seen:,} / 1,470  |  آخر تحديث: {now_s}"
            )
        )
        cur_idx = -1
        try:
            sel = self.nb.select()
            if sel:
                cur_idx = int(self.nb.index(sel))
        except Exception:
            pass

        for idx, tab in enumerate(self._all_tabs):
            if force_all or idx == cur_idx:
                try:
                    tab.refresh()
                    tab._is_dirty = False
                except Exception:
                    pass
            else:
                tab._is_dirty = True


def main() -> None:
    cli_remote: Optional[str] = None
    cli_auto: Optional[bool] = None
    args = sys.argv[1:]
    i = 0
    while i < len(args):
        if args[i] == "--remote" and i + 1 < len(args):
            cli_remote = args[i + 1]
            i += 2
        elif args[i] == "--auto-refresh":
            cli_auto = True
            i += 1
        else:
            i += 1
    root = tk.Tk()
    ArenaAnalyticsApp(root, cli_remote=cli_remote, cli_auto_refresh=cli_auto)
    root.mainloop()


if __name__ == "__main__":
    main()

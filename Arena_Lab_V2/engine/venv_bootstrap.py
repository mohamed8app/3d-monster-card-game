# -*- coding: utf-8 -*-
"""
Allows double-clicking Arena_Trainer.py, Arena_Analytics.py, or Arena_Lab_Hub.py
directly in Windows File Explorer without any .bat wrapper.
If the system Python lacks PyTorch/NumPy, this seamlessly re-launches the script
using the local Arena_Lab_V2 virtual environment (`.\venv`).
"""

from __future__ import annotations

import os
import subprocess
import sys
from typing import List, Optional


def find_venv_python(root_dir: str, prefer_windowless: bool = False) -> Optional[str]:
    """Searches standard local venv paths for a Python executable with PyTorch installed."""
    exe_names = ("pythonw.exe", "python.exe") if prefer_windowless else ("python.exe", "pythonw.exe")
    candidate_roots = [
        os.path.join(root_dir, "venv", "Scripts"),
        r"C:\Arena_Lab_V2_Venv\Scripts",
        os.path.abspath(os.path.join(root_dir, "..", "_Project_Archive_And_Offline_Tools", "Offline_Tools", "venv", "Scripts")),
    ]
    for folder in candidate_roots:
        for name in exe_names:
            path = os.path.join(folder, name)
            if os.path.isfile(path):
                return path
    return None


def ensure_venv(script_file: str, require_torch: bool = True) -> None:
    """
    Checks whether required libraries are available in the active Python interpreter.
    If not, transparently relaunches `script_file` under the local `venv` Python.
    """
    if os.environ.get("ARENA_LAB_BOOTSTRAPPED") == "1":
        return

    needed = ("numpy", "torch") if require_torch else ("numpy",)
    missing: List[str] = []
    for mod in needed:
        try:
            __import__(mod)
        except ImportError:
            missing.append(mod)

    if not missing:
        return

    root_dir = os.path.abspath(os.path.dirname(script_file))
    venv_py = find_venv_python(root_dir, prefer_windowless=False)
    if venv_py and os.path.normcase(venv_py) != os.path.normcase(sys.executable):
        env = os.environ.copy()
        env["ARENA_LAB_BOOTSTRAPPED"] = "1"
        creationflags = 0
        if sys.platform.startswith("win") and venv_py.lower().endswith("pythonw.exe"):
            creationflags = 0x08000000  # CREATE_NO_WINDOW
        subprocess.Popen([venv_py, os.path.abspath(script_file)] + sys.argv[1:], env=env, creationflags=creationflags)
        sys.exit(0)

    # Fallback: show a clean Tkinter dialog if venv has not been installed yet
    try:
        import tkinter as tk
        from tkinter import messagebox

        root = tk.Tk()
        root.withdraw()
        messagebox.showerror(
            "Arena Lab V2 — Setup Required",
            f"Missing required Python packages: {', '.join(missing)}\n\n"
            f"Please run Install_Python_Packages.bat once inside:\n{root_dir}\n\n"
            f"After that, you can double-click any .py file directly!",
        )
        root.destroy()
    except Exception:
        print(f"ERROR: Missing packages {missing}. Run Install_Python_Packages.bat once.")
    sys.exit(1)

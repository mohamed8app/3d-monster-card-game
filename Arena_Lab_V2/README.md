# Arena Lab V2 — Portable 40-Model Pure-Python CPU RL Simulator

Standalone, self-contained Pure-Python RL simulator & training suite for all **2,270 cards** (`IDs 1..2270`), **37 Constitution Rules** (`50-card` deck, **Max 2 Copies** per card, Infinite Shuffle-Bag, Turn 40+ `-100 HP` Sudden Death, strict integer rounding), **25 Locked Effects**, and **40 Independent AI Models**.

## Folder Structure
- `data/cards_v2_2270.json` — Pre-compiled numeric database of all 2,270 cards (strict integers, zero Excel/Unity dependency at runtime).
- `engine/` — `card_model.py`, `battle_engine.py`, `roster_40.py`, `ai_brain.py`, `venv_bootstrap.py`, `export_from_excel.py`.
- `trainer/` — `stats_store.py`, `session_runner.py` (CPU Safety controller, background PPO updates, Perpetual Auto-League, Smart Matchup Advisor).
- `Arena_Lab_Hub.py` — Unified direct-Python Tkinter Control Hub (launch Trainer, Analytics, or LAN Sync without `.bat` files).
- `Arena_Trainer.py` — Lightweight Tkinter Training Launcher (Single Pair, Multi-Matchup Queue Playlist, and Perpetual Auto-League up to `1,000,000+` matches).
- `Arena_Analytics.py` — Standalone Offline Analytics Viewer (40-Model Leaderboard with combat stats, Settled 50-Card Decks, Free-10 Disjoint Check, OP/Weak Cards, CSV export).
- `saved_models/` — Independent checkpoints for each of the 40 models + `warm_start_base/fighter_base.pt`.
- `logs/` — Atomic JSON/JSONL stats, ELO, Free-10 deck locks, and per-card win rates (auto-rotated for 1M+ runs).

## Quick Setup on a Dedicated CPU-Only Windows PC
1. Copy the entire `Arena_Lab_V2` folder to the training PC.
2. Double-click **`Setup_Dedicated_PC.bat`** once (creates `venv` with lightweight CPU-only PyTorch + NumPy + OpenPyXL).
3. Double-click **`Start_Trainer.bat`** to train, or **`Start_Analytics.bat`** to inspect results anytime.

## Recommended Warm-Start Workflow (Save Hours of Training!)
1. Open **`Start_Trainer.bat`**.
2. Select **`[Free] Free_01 (Vanguard)`** as Model A and **`[Free] Free_02 (Apex)`** as Model B.
3. Set **Number of Matches = `500`**, choose **`Balanced (55% CPU)`** (or `Quiet (35% CPU)`), and click **`START TRAINING`**.
4. When finished, select the stronger winner as **Model A** and click **`Set Model A as Warm-Start Base`**.
5. Keep **"Use Warm-Start Base for Untrained Models"** checked — all remaining 38 models will now start already knowing combat rules while learning their own unique 50-card decks!

## Re-compiling from Updated Excel (Optional)
If you ever edit `التصنيف الشامل.xlsx` later, simply run:
```bat
python engine\export_from_excel.py "C:\path\to\التصنيف الشامل.xlsx"
```

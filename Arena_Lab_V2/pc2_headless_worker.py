# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — PC #2 Headless Background Training Daemon (`pc2_headless_worker.py`).
100% Pure Local D: Drive Speed on PC #2:
  - Trains 100% locally on PC #2's D: drive with ZERO background file copying during training!
  - Main PC views live results directly over the LAN share (`\\\\<PC2_IP>\\Arena_Lab_V2` / `Y:\\`) in read-only mode
    without copying anything, and pulls results ONLY when the user clicks the Pull button!
  - Main PC controls Start / Stop / Auto-League / Smart Reset remotely while PC #2's screen is OFF.
"""

from __future__ import annotations

import gc
import json
import math
import os
import shutil
import socket
import struct
import sys
import threading
import time
import wave
from typing import Any, Dict, List, Optional, Tuple

try:
    import winsound
except ImportError:
    winsound = None  # type: ignore

ROOT_DIR = os.path.abspath(os.path.dirname(__file__))
if ROOT_DIR not in sys.path:
    sys.path.insert(0, ROOT_DIR)

from engine.venv_bootstrap import ensure_venv

ensure_venv(__file__, require_torch=True)

from engine.card_model import get_card_db
from engine.roster_40 import ROSTER_40, get_model_spec
from trainer.session_runner import pick_next_auto_league_pair, run_match_session
from trainer.stats_store import StatsStore, _atomic_write_json, _read_json

LOGS_DIR = os.path.join(ROOT_DIR, "logs")
SAVED_MODELS_DIR = os.path.join(ROOT_DIR, "saved_models")
CMD_PATH = os.path.join(LOGS_DIR, "pc2_remote_cmd.json")
STATUS_PATH = os.path.join(LOGS_DIR, "pc2_status.json")
HOST_INFO_NAME = "pc2_host_info.json"
_LAST_SOUND_EPOCH: float = 0.0


def play_training_complete_sound_10s() -> None:
    """
    Synthesizes (once) and plays a clear, pleasant 10.0-second triumphant multi-chime
    WAV alert asynchronously through the default Windows audio output when training finishes.
    Includes a 12s debounce so Trainer + Analytics open together never double-trigger.
    """
    global _LAST_SOUND_EPOCH
    now = time.time()
    if now - _LAST_SOUND_EPOCH < 12.0:
        return
    _LAST_SOUND_EPOCH = now

    def _runner() -> None:
        try:
            os.makedirs(LOGS_DIR, exist_ok=True)
            stamp_p = os.path.join(LOGS_DIR, ".last_chime_ts")
            try:
                if os.path.isfile(stamp_p) and (time.time() - os.path.getmtime(stamp_p)) < 12.0:
                    return
                with open(stamp_p, "w", encoding="utf-8") as sf:
                    sf.write(str(time.time()))
            except Exception:
                pass

            wav_path = os.path.join(LOGS_DIR, "training_complete_10s.wav")
            if not os.path.isfile(wav_path) or os.path.getsize(wav_path) < 400000:
                sr = 22050
                dur = 10.0
                n_samples = int(sr * dur)
                notes = [
                    (0.00, 0.45, 523.25), (0.45, 0.90, 659.25), (0.90, 1.35, 783.99), (1.35, 2.35, 1046.50),
                    (2.50, 2.95, 587.33), (2.95, 3.40, 739.99), (3.40, 3.85, 880.00), (3.85, 4.85, 1174.66),
                    (5.00, 5.45, 523.25), (5.45, 5.90, 659.25), (5.90, 6.35, 783.99), (6.35, 7.35, 1046.50),
                    (7.50, 7.90, 783.99), (7.90, 8.30, 987.77), (8.30, 10.00, 1046.50),
                ]
                buf = bytearray(n_samples * 2)
                two_pi = 2.0 * math.pi
                for s_t, e_t, freq in notes:
                    i0 = max(0, int(s_t * sr))
                    i1 = min(n_samples, int(e_t * sr))
                    span = max(1, i1 - i0)
                    for i in range(i0, i1):
                        rel = (i - i0) / sr
                        prog = (i - i0) / span
                        attack = min(1.0, (i - i0) / max(1.0, 0.018 * sr))
                        decay = math.exp(-2.3 * prog)
                        env = attack * decay
                        val = (
                            0.65 * math.sin(two_pi * freq * rel)
                            + 0.25 * math.sin(two_pi * (freq * 2.0) * rel)
                            + 0.10 * math.sin(two_pi * (freq * 3.0) * rel)
                        )
                        sample = int(max(-32767, min(32767, val * env * 23000)))
                        struct.pack_into("<h", buf, i * 2, sample)
                with wave.open(wav_path, "wb") as wf:
                    wf.setnchannels(1)
                    wf.setsampwidth(2)
                    wf.setframerate(sr)
                    wf.writeframes(buf)

            if winsound is not None:
                winsound.PlaySound(wav_path, winsound.SND_FILENAME | winsound.SND_ASYNC)
        except Exception:
            if winsound is not None:
                try:
                    for freq, ms in ((523, 550), (659, 550), (784, 550), (1046, 850)) * 4:
                        winsound.Beep(freq, ms)
                except Exception:
                    pass

    threading.Thread(target=_runner, daemon=True).start()


def _get_lan_ipv4() -> str:
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("192.168.1.1", 80))
        ip = s.getsockname()[0]
        s.close()
        return ip
    except Exception:
        return "127.0.0.1"


def resolve_pc2_remote_logs_dir(local_root: str = ROOT_DIR) -> str:
    """
    Used by Main PC (`Arena_Trainer.py`) to locate PC #2's shared `logs/` folder
    (`Y:\\logs` or `\\\\<PC2_IP>\\Arena_Lab_V2\\logs` from `pc2_host_info.json`).
    """
    if os.path.isdir(r"Y:\logs"):
        return r"Y:\logs"
    info_p = os.path.join(local_root, "logs", HOST_INFO_NAME)
    info = _read_json(info_p, {})
    if isinstance(info, dict):
        sh = str(info.get("share", "")).strip()
        if sh and os.path.isdir(os.path.join(sh, "logs")):
            return os.path.join(sh, "logs")
    cfg_p = os.path.join(local_root, "logs", "analytics_config.json")
    cfg = _read_json(cfg_p, {})
    if isinstance(cfg, dict):
        rd = str(cfg.get("remote_dir", "")).strip()
        if rd:
            cand = os.path.join(rd, "logs") if not rd.lower().endswith("logs") else rd
            if os.path.isdir(cand):
                return cand
    return os.path.join(local_root, "logs")


def perform_smart_v22_reset(root_dir: str = ROOT_DIR) -> Dict[str, Any]:
    """
    Archives old pre-V2.2 logs & 41 model checkpoints into `Archive_Pre_V22_<timestamp>/`,
    preserves `saved_models/warm_start_base/fighter_base.pt`, and initializes fresh V2.2 logs.
    """
    ts = time.strftime("%Y%m%d_%H%M%S")
    arch_dir = os.path.join(root_dir, f"Archive_Pre_V22_{ts}")
    arch_logs = os.path.join(arch_dir, "logs")
    arch_models = os.path.join(arch_dir, "saved_models")

    logs_dir = os.path.join(root_dir, "logs")
    models_dir = os.path.join(root_dir, "saved_models")
    os.makedirs(logs_dir, exist_ok=True)
    os.makedirs(models_dir, exist_ok=True)

    keep_logs = {"analytics_config.json", "pc2_remote_cmd.json", "pc2_status.json", HOST_INFO_NAME}
    archived_logs = 0
    archived_models = 0

    for fn in list(os.listdir(logs_dir)):
        if fn in keep_logs:
            continue
        src = os.path.join(logs_dir, fn)
        if os.path.isfile(src):
            os.makedirs(arch_logs, exist_ok=True)
            shutil.move(src, os.path.join(arch_logs, fn))
            archived_logs += 1

    for entry in list(os.listdir(models_dir)):
        if entry.lower() == "warm_start_base":
            continue
        src_d = os.path.join(models_dir, entry)
        if os.path.isdir(src_d):
            files = [f for f in os.listdir(src_d) if os.path.isfile(os.path.join(src_d, f))]
            if files:
                os.makedirs(arch_models, exist_ok=True)
                shutil.move(src_d, os.path.join(arch_models, entry))
                archived_models += 1
            os.makedirs(src_d, exist_ok=True)

    store = StatsStore(logs_dir=logs_dir, saved_models_dir=models_dir, read_only=False)
    _atomic_write_json(store.roster_path, store.roster)
    _atomic_write_json(store.free10_path, store.free10_decks)
    _atomic_write_json(store.card_stats_path, store.card_stats)
    _atomic_write_json(store.model_card_stats_path, store.model_card_stats)
    _atomic_write_json(store.h2h_path, store.head_to_head)
    _atomic_write_json(store.excitement_path, store.excitement)
    return {
        "archive_dir": arch_dir if (archived_logs > 0 or archived_models > 0) else "",
        "archived_logs": archived_logs,
        "archived_models": archived_models,
    }


class PC2HeadlessWorker:
    def __init__(self) -> None:
        self.hostname = socket.gethostname()
        self.ip_addr = _get_lan_ipv4()
        self.pid = os.getpid()
        self.db = get_card_db()
        self.store = StatsStore()
        self.stop_event = threading.Event()
        self.worker_thread: Optional[threading.Thread] = None

        self.state = "idle"
        self.last_cmd_id = ""
        self.prog_title = f"PC #2 ({self.hostname} @ {self.ip_addr}) Ready — 100% Pure Local D: Speed (Zero Copy Overhead)"
        self.prog_done = 0
        self.prog_total = 100
        self.prog_stats = "Idle — Screen can be turned OFF safely."
        self.recent_logs: List[str] = []
        self._lock = threading.Lock()

        existing_cmd = _read_json(CMD_PATH, {})
        if isinstance(existing_cmd, dict) and existing_cmd.get("status") != "pending":
            self.last_cmd_id = str(existing_cmd.get("cmd_id", ""))

        self._announce_ip_to_main_pc()
        self._append_log(
            f"PC #2 Headless Worker ONLINE [{self.hostname} @ {self.ip_addr}] | Local D:={ROOT_DIR} | Zero Copy Mode"
        )
        self._write_status()

    def _announce_ip_to_main_pc(self) -> None:
        """Writes a tiny 120-byte host-discovery file once on startup so Main PC knows PC #2's exact IP."""
        info = {
            "hostname": self.hostname,
            "ip": self.ip_addr,
            "share": f"\\\\{self.ip_addr}\\Arena_Lab_V2",
            "local_dir": ROOT_DIR,
            "ts": time.strftime("%Y-%m-%d %H:%M:%S"),
        }
        try:
            _atomic_write_json(os.path.join(LOGS_DIR, HOST_INFO_NAME), info)
        except Exception:
            pass
        for cand in (r"Z:\logs", r"\\192.168.1.2\Arena_Lab_V2\logs"):
            try:
                if os.path.isdir(cand):
                    _atomic_write_json(os.path.join(cand, HOST_INFO_NAME), info)
                    break
            except Exception:
                pass

    def _append_log(self, msg: str) -> None:
        ts = time.strftime("%H:%M:%S")
        line = f"[{ts}] [PC2:{self.hostname}] {msg}"
        print(line, flush=True)
        with self._lock:
            self.recent_logs.append(line)
            if len(self.recent_logs) > 80:
                self.recent_logs = self.recent_logs[-80:]

    def _write_status(self) -> None:
        with self._lock:
            payload = {
                "hostname": self.hostname,
                "ip": self.ip_addr,
                "share": f"\\\\{self.ip_addr}\\Arena_Lab_V2",
                "local_dir": ROOT_DIR,
                "pid": self.pid,
                "epoch": time.time(),
                "ts": time.strftime("%Y-%m-%d %H:%M:%S"),
                "state": self.state,
                "last_cmd_id": self.last_cmd_id,
                "prog_title": self.prog_title,
                "prog_done": self.prog_done,
                "prog_total": max(1, self.prog_total),
                "prog_stats": self.prog_stats,
                "recent_logs": list(self.recent_logs[-35:]),
            }
        try:
            _atomic_write_json(STATUS_PATH, payload)
        except Exception:
            pass

    def _mark_cmd(self, cmd: Dict[str, Any], status: str) -> None:
        try:
            cmd_copy = dict(cmd)
            cmd_copy["status"] = status
            cmd_copy["handled_by"] = self.hostname
            cmd_copy["handled_ts"] = time.strftime("%Y-%m-%d %H:%M:%S")
            _atomic_write_json(CMD_PATH, cmd_copy)
        except Exception:
            pass

    def run_forever(self) -> None:
        print("=" * 76, flush=True)
        print(f" ARENA LAB V2 — PC #2 HEADLESS WORKER (100% LOCAL SPEED, ZERO BACKGROUND COPY)", flush=True)
        print(f" Host & IP     : {self.hostname}  ({self.ip_addr})", flush=True)
        print(f" Local D: Path : {ROOT_DIR}", flush=True)
        print(f" Network Share : \\\\{self.ip_addr}\\Arena_Lab_V2", flush=True)
        print(" You can now TURN OFF PC #2's screen! Main PC reads live without copying.", flush=True)
        print("=" * 76, flush=True)

        while True:
            try:
                self._poll_command()
                self._write_status()
            except KeyboardInterrupt:
                print("\nStopping PC #2 Headless Worker...", flush=True)
                self.stop_event.set()
                break
            except Exception as err:
                self._append_log(f"Worker poll warning: {err}")
            time.sleep(1.5)

    def _poll_command(self) -> None:
        cmd = _read_json(CMD_PATH, {})
        if not isinstance(cmd, dict):
            return
        cmd_id = str(cmd.get("cmd_id", ""))
        status = str(cmd.get("status", ""))
        action = str(cmd.get("action", ""))
        if not cmd_id or status != "pending":
            return

        if action == "stop":
            self.last_cmd_id = cmd_id
            self._mark_cmd(cmd, "acknowledged")
            if self.worker_thread and self.worker_thread.is_alive():
                self.state = "stopping"
                self.prog_title = "Stopping cleanly after current match & flushing checkpoints..."
                self._append_log("Remote STOP received from Main PC — finishing current match cleanly...")
                self.stop_event.set()
            else:
                self.state = "idle"
                self._append_log("Remote STOP received (already idle).")
            self._write_status()
            return

        if action == "clean_reset":
            self.last_cmd_id = cmd_id
            self._mark_cmd(cmd, "acknowledged")
            if self.worker_thread and self.worker_thread.is_alive():
                self.stop_event.set()
                self.worker_thread.join(timeout=15.0)
            res_local = perform_smart_v22_reset(ROOT_DIR)
            self.store.reload()
            self.state = "idle"
            self.prog_done = 0
            self.prog_total = 100
            self.prog_title = "Smart V2.2 Clean & Reset Complete on PC #2 D: — Ready for fresh V2.2 training!"
            self._append_log(
                f"Smart V2.2 Reset complete on PC #2 ({ROOT_DIR}): archived {res_local['archived_logs']} logs & {res_local['archived_models']} models."
            )
            self._mark_cmd(cmd, "completed")
            self._write_status()
            return

        if self.worker_thread and self.worker_thread.is_alive():
            self._append_log("Stopping previous job before starting new remote command...")
            self.stop_event.set()
            self.worker_thread.join(timeout=15.0)

        self.last_cmd_id = cmd_id
        self._mark_cmd(cmd, "running")

        if action == "start_playlist":
            playlist = cmd.get("playlist", [])
            cycles = int(cmd.get("cycles", 1))
            cpu_preset = str(cmd.get("cpu_preset", "Balanced (55% CPU)"))
            use_ws = bool(cmd.get("use_warm_start", True))
            self.stop_event.clear()
            self.state = "running"
            self.worker_thread = threading.Thread(
                target=self._run_playlist_worker,
                args=(cmd, playlist, cycles, cpu_preset, use_ws),
                daemon=True,
            )
            self.worker_thread.start()

        elif action == "start_auto_league":
            pool_scope = str(cmd.get("pool_scope", "All"))
            block_n = int(cmd.get("block_n", 1000))
            target_n = int(cmd.get("target_n", 1000000))
            cpu_preset = str(cmd.get("cpu_preset", "Balanced (55% CPU)"))
            use_ws = bool(cmd.get("use_warm_start", True))
            self.stop_event.clear()
            self.state = "running"
            self.worker_thread = threading.Thread(
                target=self._run_auto_league_worker,
                args=(cmd, pool_scope, block_n, target_n, cpu_preset, use_ws),
                daemon=True,
            )
            self.worker_thread.start()

    def _run_playlist_worker(
        self,
        cmd: Dict[str, Any],
        playlist: List[List[Any]],
        cycles: int,
        cpu_preset: str,
        use_ws: bool,
    ) -> None:
        total_jobs = len(playlist) * cycles
        self._append_log(f"Started Playlist on PC #2 D: (Zero Copy): {len(playlist)} pair(s) x {cycles} cycle(s) | CPU={cpu_preset}")
        jobs_done = 0
        try:
            for cyc in range(1, cycles + 1):
                for idx, item in enumerate(playlist, 1):
                    if self.stop_event.is_set():
                        break
                    id_a, id_b, n_matches = str(item[0]), str(item[1]), int(item[2])
                    job_num = (cyc - 1) * len(playlist) + idx
                    spec_a = get_model_spec(id_a)
                    spec_b = get_model_spec(id_b)
                    with self._lock:
                        self.prog_title = (
                            f"[PC #2 Job {job_num}/{total_jobs} | Cycle {cyc}/{cycles}]  "
                            f"{spec_a.display_name}  VS  {spec_b.display_name}  ({cpu_preset})"
                        )
                        self.prog_done = 0
                        self.prog_total = n_matches
                    self._append_log(f"--- Job {job_num}/{total_jobs}: {id_a} vs {id_b} ({n_matches:,} matches) ---")
                    self._write_status()

                    last_ms = [0]
                    last_stat_t = [0.0]

                    def _on_prog(done: int, total: int, info: Dict[str, Any]) -> None:
                        mps = info["matches_per_sec"]
                        eta = (total - done) / max(0.01, mps)
                        eta_s = f"{int(eta // 60)}m {int(eta % 60):02d}s" if eta >= 60 else f"{int(eta)}s"
                        s_line = (
                            f"[PC #2 D:] Match {done:,}/{total:,}  |  A:{info['wins_a']}W B:{info['wins_b']}W D:{info['draws']}  |  "
                            f"Turns:{info['avg_turns']:.1f}  |  Speed:{mps:.2f} m/s ({info['threads']}T)  |  ETA:{eta_s}"
                        )
                        with self._lock:
                            self.prog_done = done
                            self.prog_total = total
                            self.prog_stats = s_line
                        step = 200 if total >= 400 else 50
                        if done - last_ms[0] >= step:
                            last_ms[0] = done
                            wr_a = 100.0 * info["wins_a"] / max(1, done)
                            self._append_log(
                                f"[{done}/{total}] A WR={wr_a:.1f}% | V-Loss A={info.get('v_loss_a', 0.0):.3f} B={info.get('v_loss_b', 0.0):.3f} | "
                                f"Focus A={info.get('focus_a', 0.0):.1f}% B={info.get('focus_b', 0.0):.1f}%"
                            )
                        now = time.time()
                        if now - last_stat_t[0] >= 1.5 or done == total:
                            last_stat_t[0] = now
                            self._write_status()

                    summary = run_match_session(
                        model_a_id=id_a,
                        model_b_id=id_b,
                        num_matches=n_matches,
                        cpu_preset=cpu_preset,
                        use_warm_start=use_ws,
                        stop_event=self.stop_event,
                        progress_cb=_on_prog,
                        db=self.db,
                        store=self.store,
                    )
                    jobs_done += 1
                    st_word = "Stopped & Saved" if summary["stopped_early"] else "Completed"
                    self._append_log(
                        f"{st_word} ({summary['elapsed_sec']:.1f}s, {summary['matches_per_sec']:.2f} m/s): "
                        f"{id_a} {summary['wins_a']}W ({summary['win_rate_a']:.1f}%) vs {id_b} {summary['wins_b']}W ({summary['win_rate_b']:.1f}%)"
                    )
                    gc.collect()
                if self.stop_event.is_set():
                    break

            with self._lock:
                self.state = "idle"
                self.prog_title = (
                    f"PC #2 Stopped & Saved cleanly ({jobs_done}/{total_jobs} jobs)."
                    if self.stop_event.is_set()
                    else f"PC #2 ALL DONE — Completed {jobs_done}/{total_jobs} job(s)!"
                )
            self._mark_cmd(cmd, "completed")
            self._write_status()
            if not self.stop_event.is_set():
                play_training_complete_sound_10s()
        except Exception as err:
            with self._lock:
                self.state = "idle"
                self.prog_title = f"PC #2 Error: {err}"
            self._append_log(f"ERROR: {err}")
            self._mark_cmd(cmd, "error")
            self._write_status()

    def _run_auto_league_worker(
        self,
        cmd: Dict[str, Any],
        pool_scope: str,
        block_n: int,
        target_n: int,
        cpu_preset: str,
        use_ws: bool,
    ) -> None:
        self._append_log(
            f"Started AUTO-LEAGUE on PC #2 D: (Zero Copy): Pool={pool_scope} | {block_n:,}/pair | Target={target_n:,} | CPU={cpu_preset}"
        )
        league_total = 0
        round_num = 0
        last_pair: Optional[Tuple[str, str]] = None
        try:
            while league_total < target_n and not self.stop_event.is_set():
                round_num += 1
                cur_block = min(block_n, target_n - league_total)
                self.store.reload()
                id_a, id_b = pick_next_auto_league_pair(self.store, scope=pool_scope, last_pair=last_pair)
                last_pair = (id_a, id_b)
                spec_a = get_model_spec(id_a)
                spec_b = get_model_spec(id_b)
                ma = self.store.roster[id_a]["matches"]
                mb = self.store.roster[id_b]["matches"]

                with self._lock:
                    self.prog_title = (
                        f"[PC #2 Auto-League #{round_num} | Total {league_total:,}/{target_n:,}]  "
                        f"{spec_a.display_name} ({ma:,}m)  VS  {spec_b.display_name} ({mb:,}m)"
                    )
                    self.prog_done = 0
                    self.prog_total = cur_block
                self._append_log(f"--- Auto-League Round #{round_num}: {id_a} vs {id_b} ({cur_block:,} matches) ---")
                self._write_status()

                last_ms = [0]
                last_stat_t = [0.0]

                def _on_prog(done: int, total: int, info: Dict[str, Any]) -> None:
                    mps = info["matches_per_sec"]
                    rem = (total - done) / max(0.01, mps)
                    eta_s = f"{int(rem // 60)}m {int(rem % 60):02d}s" if rem >= 60 else f"{int(rem)}s"
                    s_line = (
                        f"[PC #2 D:] Round {done:,}/{total:,}  |  League Total: {league_total + done:,}/{target_n:,}  |  "
                        f"A:{info['wins_a']}W B:{info['wins_b']}W  |  {mps:.1f} m/s ({info['threads']}T)  |  ETA:{eta_s}"
                    )
                    with self._lock:
                        self.prog_done = done
                        self.prog_total = total
                        self.prog_stats = s_line
                    if done - last_ms[0] >= 500:
                        last_ms[0] = done
                        wr_a = 100.0 * info["wins_a"] / max(1, done)
                        self._append_log(
                            f"[Auto-League #{round_num} @ {done}/{total}] A WR={wr_a:.1f}% | "
                            f"Focus A={info.get('focus_a', 0.0):.1f}% B={info.get('focus_b', 0.0):.1f}%"
                        )
                    now = time.time()
                    if now - last_stat_t[0] >= 1.5 or done == total:
                        last_stat_t[0] = now
                        self._write_status()

                summary = run_match_session(
                    model_a_id=id_a,
                    model_b_id=id_b,
                    num_matches=cur_block,
                    cpu_preset=cpu_preset,
                    use_warm_start=use_ws,
                    stop_event=self.stop_event,
                    progress_cb=_on_prog,
                    db=self.db,
                    store=self.store,
                )
                league_total += summary["completed"]
                self._append_log(
                    f"Round #{round_num} Done ({summary['matches_per_sec']:.1f} m/s): "
                    f"{id_a} {summary['wins_a']}W ({summary['win_rate_a']:.1f}%) vs {id_b} {summary['wins_b']}W ({summary['win_rate_b']:.1f}%)"
                )
                gc.collect()

            with self._lock:
                self.state = "idle"
                self.prog_title = (
                    f"PC #2 Auto-League Stopped & Saved ({league_total:,} matches)."
                    if self.stop_event.is_set()
                    else f"PC #2 Auto-League Target Reached ({league_total:,} matches)!"
                )
            self._mark_cmd(cmd, "completed")
            self._write_status()
            if not self.stop_event.is_set():
                play_training_complete_sound_10s()
        except Exception as err:
            with self._lock:
                self.state = "idle"
                self.prog_title = f"PC #2 Auto-League Error: {err}"
            self._append_log(f"ERROR: {err}")
            self._mark_cmd(cmd, "error")
            self._write_status()


if __name__ == "__main__":
    PC2HeadlessWorker().run_forever()

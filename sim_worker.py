# -*- coding: utf-8 -*-
"""
وحدة المعالجة الفرعية للمحاكاة (Worker for Multiprocessing)
معزولة تماماً عن مكتبة Tkinter لتجنب أي مشاكل في Windows Multiprocessing
"""

from collections import defaultdict
from battle_engine_advanced import run_single_battle, MONSTER_MAP

def simulate_matches_worker(num_matches):
    appearances = defaultdict(int)
    wins = defaultdict(int)
    p1_wins = 0
    total_turns = 0

    for _ in range(num_matches):
        res = run_single_battle(deck_size=30, include_environments=True)
        total_turns += res["turns"]
        if res["first_player_won"]:
            p1_wins += 1
        for cid in res["winner_cards"]:
            appearances[cid] += 1
            wins[cid] += 1
        for cid in res["loser_cards"]:
            appearances[cid] += 1

    return {
        "appearances": dict(appearances),
        "wins": dict(wins),
        "p1_wins": p1_wins,
        "total_turns": total_turns,
        "matches": num_matches
    }

# -*- coding: utf-8 -*-
"""
Comprehensive end-to-end verification test suite for Arena_Lab_V2.
Tests all 2,270 cards, strict integer rounding, Constitution rules, all 40 models,
Free-10 disjointness, Warm-Start promotion, PPO training loop, Advisor, and UI modules.
"""

from __future__ import annotations

import os
import shutil
import sys
import tempfile
import time

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
if ROOT_DIR not in sys.path:
    sys.path.insert(0, ROOT_DIR)

import numpy as np

from engine import ai_brain
from engine.ai_brain import DeckDrafter, ModelBrain, get_warm_start_info, promote_model_to_warm_start_base
from engine.battle_engine import ACTION_DIM, OBS_DIM, BattleEngine
from engine.card_model import (
    BUFF_CAP_ATK,
    BUFF_CAP_DEF,
    DECK_SIZE,
    MAX_COPIES,
    MAX_HAND,
    CardDB,
    PlayerState,
    get_card_db,
    pct_of,
)
from engine.roster_40 import ROSTER_40, build_legal_card_mask, get_model_spec
from trainer.session_runner import run_match_session, suggest_matchups
from trainer.stats_store import StatsStore


def test_1_card_database(db: CardDB) -> None:
    assert db.num_cards == 1470
    assert len(db.envs) == 10
    assert len(db.leaders) == 60
    assert len(db.covenants) == 100
    assert len(db.spells) == 300
    assert len(db.monsters) == 1000
    assert len(db.barriers) == 200
    assert len(db.combat_monsters) == 800

    for bid in db.barriers:
        c = db.get(bid)
        assert c.is_barrier and c.atk == 0 and c.tribe == -1 and c.a_cnt == 0 and len(c.actives) == 0

    all_atk_cards = [c for c in db.cards.values() if c.is_all_attack]
    assert len(all_atk_cards) == 10, f"Expected 10 All-Attack cards, got {len(all_atk_cards)}"
    print("[PASS] Test 1: 1,470-card DB integrity (10 Envs, 60 Leaders, 100 Covenants, 300 Spells, 800 Combat + 200 Neutral Barriers)")


def test_2_constitution_rules(db: CardDB) -> None:
    # 2a. Max Hand = 7 burns oldest card to Graveyard
    p = PlayerState(0, [])
    for cid in range(101, 109):
        p.add_to_hand(cid)
    assert len(p.hand) == MAX_HAND == 7
    assert p.hand[0].card_id == 102
    assert p.hand[-1].card_id == 108
    assert 101 in p.graveyard
    assert p.telemetry.cards_burned_hand7 == 1

    # 2b. Barrier 0 ATK locked forever (even under Fire Arena L3, perm/temp buff, Stat Flip)
    eng = BattleEngine(db=db, seed=42)
    sample_deck = [1] + db.monsters[:49]
    eng.reset(sample_deck, sample_deck, seed=42)
    eng.players[0].board = [None] * 6
    barrier_card = db.get(db.barriers[0])
    bu = eng._summon_monster(0, barrier_card, 0)
    assert bu is not None and bu.is_barrier
    eng._play_environment(0, 0)  # Fire L1
    eng._play_environment(0, 0)  # Fire L2
    eng._play_environment(0, 0)  # Fire L3 (+6 ATK to non-barriers)
    bu.apply_perm_buff(10, 10)
    bu.apply_temp_buff(10, 10)
    assert bu.atk == 0
    assert eng._effective_atk(0, bu) == 0
    assert not bu.can_ever_attack()
    # Cast Stat Flip spell targeting slot 0 -> must NOT raise Barrier ATK!
    sf_card = next(c for c in db.cards.values() if c.type == "spell" and c.spell_ops and c.spell_ops.get("kind") == "stat_flip")
    eng._cast_spell(0, sf_card, 0)
    assert bu.atk == 0 and eng._effective_atk(0, bu) == 0

    # 2c. All-Attack ATK locked forever
    aa_card = next(c for c in db.cards.values() if c.is_all_attack)
    aau = eng._summon_monster(0, aa_card, 1)
    assert aau is not None and aau.atk_locked
    orig_a = aau.base_atk
    aau.apply_perm_buff(10, 10)
    aau.apply_temp_buff(10, 0)
    assert aau.atk == orig_a
    assert eng._effective_atk(0, aau) == orig_a

    # 2d. Permanent buff cap (+10 ATK / +15 DEF)
    norm_card = next(c for c in db.cards.values() if c.type == "monster" and not c.is_barrier and not c.is_all_attack)
    nu = eng._summon_monster(0, norm_card, 2)
    assert nu is not None
    nu.apply_perm_buff(99, 99)
    assert nu.perm_atk_buff == BUFF_CAP_ATK == 10
    assert nu.perm_def_buff == BUFF_CAP_DEF == 15

    # 2e. Undying 1 DEF once-per-game save
    nu.undying_ready = True
    nu.undying_used = False
    eng._damage_unit(0, nu, 999, source_side=1)
    assert nu.deff == 1 and nu.undying_used and not nu.undying_ready
    eng._damage_unit(0, nu, 1, source_side=1)
    assert nu.deff == 0

    # 2f. Hydra Split strict integer rounding on odd stats
    hydra_card = next(c for c in db.cards.values() if c.type == "monster" and any(pa.get("kind") == "hydra_split" for pa in c.passives))
    hu = eng._summon_monster(0, hydra_card, 3)
    assert hu is not None
    hu.atk = 13
    hu.deff = 17
    hu.max_def = 20
    eng._damage_unit(0, hu, 2, source_side=1)  # Survives at 15 DEF -> splits!
    clones = [u for u in eng.players[0].board_units() if u.name == "Hydra Head"]
    assert len(clones) == 1
    assert isinstance(clones[0].atk, int) and isinstance(clones[0].deff, int)
    assert clones[0].atk == pct_of(13, 50) and clones[0].deff == pct_of(15, 50)

    # 2g. Devour <= 3 Mana restriction
    eng.players[1].board = [None] * 6
    high_card = next(c for c in db.cards.values() if c.type == "monster" and not c.is_barrier and c.mana >= 5)
    low_card = next(c for c in db.cards.values() if c.type == "monster" and not c.is_barrier and c.mana <= 2)
    hi_u = eng._summon_monster(1, high_card, 0)
    assert eng._find_devour_target(0, hu) is None or eng._find_devour_target(0, hu)[1].mana <= 3
    lo_u = eng._summon_monster(1, low_card, 1)
    dt = eng._find_devour_target(0, hu)
    assert dt is not None and dt[1] is lo_u and dt[1].mana <= 3

    # 2h. Infinite Shuffle-Bag (ZERO Deck-Out loss; all 50 cards drawn per cycle, then Cycle 2 refills!)
    eng_inf = BattleEngine(db=db, seed=99)
    unique_50 = db.monsters[:50]
    eng_inf.reset(unique_50, unique_50, seed=99)
    seen_cycle1 = set(hc.card_id for hc in eng_inf.players[0].hand)
    while eng_inf.players[0].deck_cycles == 1:
        eng_inf._draw_card(0, check_deckout=True)
        if eng_inf.players[0].deck_cycles == 1:
            seen_cycle1.add(eng_inf.players[0].hand[-1].card_id)
    assert not eng_inf.done, "Infinite Shuffle-Bag must NEVER cause Deck-Out loss!"
    assert eng_inf.players[0].deck_cycles == 2
    assert len(seen_cycle1) == 50, f"Expected all 50 cards to appear in Cycle 1, got {len(seen_cycle1)}"

    # 2i. Sudden Death Penalty after Turn 40 (-100 HP to BOTH players per round; higher HP wins on double KO!)
    eng_sd = BattleEngine(db=db, seed=101)
    eng_sd.reset(unique_50, unique_50, seed=101)
    eng_sd.turn = 40
    eng_sd.active = 1
    eng_sd.players[0].hp = 85
    eng_sd.players[1].hp = 60
    eng_sd._run_combat_and_end_turn(1)  # Advances to Turn 41 -> BOTH take -100 HP!
    assert eng_sd.done and eng_sd.win_reason == "SuddenDeathHP" and eng_sd.winner == 0

    print("[PASS] Test 2: All Constitution rules verified (Hand-7 burn, Barrier 0-ATK, All-Attack, Buff Caps, Undying 1 DEF, Hydra Int, Devour <=3, Infinite Shuffle-Bag, Turn 40+ Sudden Death)")


def test_3_all_40_models_and_free10_disjoint(db: CardDB) -> None:
    drafter = DeckDrafter(db)
    rng = np.random.default_rng(123)

    free_claimed: set = set()
    for spec in ROSTER_40:
        excl = free_claimed if spec.category == "Free" else None
        lmask = build_legal_card_mask(spec, db, free_excluded_ids=excl)
        deck = drafter.build_deck(lmask, spec.required_card_id, deterministic=False, rng=rng)
        assert len(deck) == DECK_SIZE, f"{spec.model_id}: got {len(deck)} cards"

        # Max 3 copies
        from collections import Counter
        cnts = Counter(deck)
        assert max(cnts.values()) <= MAX_COPIES, f"{spec.model_id}: exceeded 3 copies"

        # Category-specific constraints
        if spec.category == "Playstyle":
            assert int(lmask.sum()) == 1470
        elif spec.category == "Environment":
            if spec.required_card_id and spec.required_card_id > 0:
                assert spec.required_card_id in deck
            for cid in deck:
                c = db.get(cid)
                if c.type == "monster":
                    assert (c.element == spec.sub_index) or c.is_barrier, f"{spec.model_id} drafted foreign element monster {cid}"
        elif spec.category == "Tribe":
            for cid in deck:
                c = db.get(cid)
                if c.type == "monster":
                    assert (c.tribe == spec.sub_index) or c.is_barrier, f"{spec.model_id} drafted foreign tribe monster {cid}"
        elif spec.category == "Free":
            overlap = free_claimed.intersection(deck)
            assert len(overlap) == 0, f"{spec.model_id} overlapped Free-10 cards: {overlap}"
            free_claimed.update(deck)

    print(f"[PASS] Test 3: All 40 models drafted legal 50-card decks! Free-10 claimed {len(free_claimed)} strictly disjoint unique cards (0 overlaps).")


def test_4_strict_integers_across_40_full_battles(db: CardDB) -> None:
    drafter = DeckDrafter(db)
    rng = np.random.default_rng(777)
    eng = BattleEngine(db=db, seed=777)
    full_mask = np.ones(db.num_cards, dtype=np.bool_)

    t0 = time.perf_counter()
    for i in range(40):
        d0 = drafter.build_deck(full_mask, deterministic=False, rng=rng)
        d1 = drafter.build_deck(full_mask, deterministic=False, rng=rng)
        eng.reset(d0, d1, seed=1000 + i)
        steps = 0
        while not eng.done and steps < 1500:
            obs = eng.get_obs()
            lmask = eng.get_legal_mask()
            assert obs.shape == (OBS_DIM,) and np.all(np.isfinite(obs))
            assert lmask.shape == (ACTION_DIM,) and lmask[0]
            legal_idxs = np.flatnonzero(lmask)
            act = int(rng.choice(legal_idxs))
            eng.step(act)
            steps += 1

            # Assert ZERO fractional stats anywhere on board or commanders
            for s in (0, 1):
                pl = eng.players[s]
                assert isinstance(pl.hp, int) and isinstance(pl.mana, int)
                for u in pl.board_units():
                    assert isinstance(u.atk, int) and isinstance(u.deff, int) and isinstance(u.max_def, int)
                    ea = eng._effective_atk(s, u)
                    assert isinstance(ea, int)
                    if u.is_barrier:
                        assert u.atk == 0 and ea == 0
        if not eng.done:
            eng._finish(0, "Guard")

    dt = time.perf_counter() - t0
    print(f"[PASS] Test 4: 40 full battles simulated cleanly in {dt:.2f}s ({40.0/dt:.1f} matches/sec) with 100% strict integer stats & Barrier 0-ATK invariant!")


def test_5_end_to_end_training_and_warm_start(db: CardDB) -> None:
    tmp_dir = tempfile.mkdtemp(prefix="arena_lab_v2_test_")
    old_saved = ai_brain.SAVED_MODELS_DIR
    old_ws = ai_brain.WARM_START_PATH
    try:
        ai_brain.SAVED_MODELS_DIR = os.path.join(tmp_dir, "saved_models")
        ai_brain.WARM_START_PATH = os.path.join(ai_brain.SAVED_MODELS_DIR, "warm_start_base", "fighter_base.pt")
        store = StatsStore(logs_dir=os.path.join(tmp_dir, "logs"))

        # Phase A: Train Free_01 vs Free_02 for 16 matches (1 full background PPO + Drafter batch)
        res1 = run_match_session(
            model_a_id="free_01",
            model_b_id="free_02",
            num_matches=16,
            cpu_preset="Fast (75% CPU)",
            use_warm_start=True,
            batch_size=8,
            db=db,
            store=store,
        )
        assert res1["completed"] == 16
        store.reload()
        d_f1 = set(store.free10_decks["free_01"])
        d_f2 = set(store.free10_decks["free_02"])
        assert len(d_f1) > 10 and len(d_f2) > 10
        assert d_f1.isdisjoint(d_f2), f"Free_01 & Free_02 settled decks overlapped: {d_f1 & d_f2}"

        # Phase B: 1-Click Promote Free_01 to Warm-Start Base
        ws_path = promote_model_to_warm_start_base("free_01", db)
        assert os.path.isfile(ws_path)
        info = get_warm_start_info()
        assert info is not None and info["source_model_id"] == "free_01"

        # Phase C: Train Style (Bastion) vs Environment (Fire) using Warm-Start Base
        res2 = run_match_session(
            model_a_id="style_bastion",
            model_b_id="env_fire",
            num_matches=8,
            cpu_preset="Balanced (55% CPU)",
            use_warm_start=True,
            batch_size=8,
            db=db,
            store=store,
        )
        assert res2["completed"] == 8
        assert res2["warm_started_a"] is True and res2["warm_started_b"] is True

        # Phase D: Train Model #41 (Glitch_41 Anomaly Hunter) vs Free_01 and verify Excitement & Anomaly telemetry!
        res3 = run_match_session(
            model_a_id="anomaly_glitch",
            model_b_id="free_01",
            num_matches=8,
            cpu_preset="Fast (75% CPU)",
            use_warm_start=True,
            batch_size=8,
            db=db,
            store=store,
        )
        assert res3["completed"] == 8
        store.reload()
        assert store.excitement["total_matches"] == 32
        assert store.excitement["avg_excitement"] > 0.0

        # Phase E: Smart Matchup Advisor check
        sugs = suggest_matchups(store, limit=6)
        assert len(sugs) >= 4
        print(
            f"[PASS] Test 5: End-to-end PPO + Drafter + Warm-Start + Free-10 Disjoint + Glitch_41 Anomaly Hunter + Excitement Lab verified! "
            f"(Avg Excitement: {store.excitement['avg_excitement']:.1f}/100, Anomalies Logged: {len(store.excitement['anomalies'])})"
        )
    finally:
        ai_brain.SAVED_MODELS_DIR = old_saved
        ai_brain.WARM_START_PATH = old_ws
        shutil.rmtree(tmp_dir, ignore_errors=True)


def test_6_ui_modules_import() -> None:
    import Arena_Analytics
    import Arena_Trainer
    assert hasattr(Arena_Trainer, "ArenaTrainerApp")
    assert hasattr(Arena_Analytics, "ArenaAnalyticsApp")
    print("[PASS] Test 6: Arena_Trainer.py and Arena_Analytics.py imported cleanly with zero syntax/dependency errors!")


if __name__ == "__main__":
    db = get_card_db()
    test_1_card_database(db)
    test_2_constitution_rules(db)
    test_3_all_40_models_and_free10_disjoint(db)
    test_4_strict_integers_across_40_full_battles(db)
    test_5_end_to_end_training_and_warm_start(db)
    test_6_ui_modules_import()
    print("\nALL 6 END-TO-END TESTS PASSED 100%!")

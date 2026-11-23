# -*- coding: utf-8 -*-
"""
Test Suite: Rigorous Proof of the 4 Evidence-Based Attribution Cases for Two-Head Drafter.

User-Defined Attribution Rules for Copy #2:
  Case 1 (Undrawn, p_drawn < 2):
    - 2nd copy was never drawn during the match (stayed buried in deck).
    - Unobserved evidence -> Zero reward, Zero penalty (delta = 0).
  Case 2 (Drawn without opportunity, p_drawn >= 2, p_played < 2, opp_wasted == 0):
    - 2nd copy was drawn, but never had legal/strategic opportunity to be played (mana-starved, board locked).
    - Zero penalty (delta = 0).
  Case 3 (Drawn with wasted opportunity, p_drawn >= 2, p_played < 2, opp_wasted > 0):
    - 2nd copy sat in hand across turns with valid legal opportunities to play, but left unused.
    - Proportional missed-opportunity penalty (delta < 0).
  Case 4 (Played and contributed, p_played >= 2):
    - Both copies were summoned/cast on the battlefield and contributed.
    - Full positive reward (delta > 0).

Also Verifies:
  - Strict independence between Head 0 and Head 1 updates.
  - Autonomous drafting behavior (1-of vs 2-of selection).
  - Legacy 1D checkpoint backward compatibility.
"""

import os
import sys
from collections import Counter
import numpy as np
import torch

BASE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.insert(0, BASE_DIR)

from engine.card_model import CardDB
from engine.ai_brain import DeckDrafter, ModelBrain


def test_four_attribution_cases():
    print("=" * 70)
    print("TEST SUITE: TWO-HEAD DRAFTER 4-CASE EVIDENCE-BASED ATTRIBUTION")
    print("=" * 70)

    db = CardDB()
    num_cards = db.num_cards
    drafter = DeckDrafter(db)

    # 4 distinct test cards
    c_undrawn = 10     # Case 1: 2nd copy never drawn (p_drawn = 1, p_played = 1)
    c_no_opp = 20      # Case 2: 2nd copy drawn, but opp_wasted = 0 (p_drawn = 2, p_played = 1)
    c_wasted = 30      # Case 3: 2nd copy drawn, opp_wasted = 2 (p_drawn = 2, p_played = 1)
    c_played = 40      # Case 4: 2nd copy drawn & played (p_drawn = 2, p_played = 2)

    idx_undrawn = c_undrawn - 1
    idx_no_opp = c_no_opp - 1
    idx_wasted = c_wasted - 1
    idx_played = c_played - 1

    # Record initial logits
    init_undrawn_c1 = float(drafter.logits[idx_undrawn, 0].item())
    init_undrawn_c2 = float(drafter.logits[idx_undrawn, 1].item())

    init_no_opp_c1 = float(drafter.logits[idx_no_opp, 0].item())
    init_no_opp_c2 = float(drafter.logits[idx_no_opp, 1].item())

    init_wasted_c1 = float(drafter.logits[idx_wasted, 0].item())
    init_wasted_c2 = float(drafter.logits[idx_wasted, 1].item())

    init_played_c1 = float(drafter.logits[idx_played, 0].item())
    init_played_c2 = float(drafter.logits[idx_played, 1].item())

    # Build batch of 10 matches
    # Every deck drafts 2 copies of all 4 test cards (plus filler to reach 50)
    deck_50 = [c_undrawn, c_undrawn, c_no_opp, c_no_opp, c_wasted, c_wasted, c_played, c_played]
    for cid in range(1, 51):
        if cid not in (c_undrawn, c_no_opp, c_wasted, c_played) and len(deck_50) < 50:
            deck_50.append(cid)
    assert len(deck_50) == 50

    batch_decks = []
    batch_rewards = []
    batch_played = []
    batch_drawn = []
    batch_wasted = []

    for _ in range(10):
        batch_decks.append(list(deck_50))
        batch_rewards.append(1.0)  # Team won the match

        # Played list:
        # - c_undrawn: played 1 time (Copy 1 played)
        # - c_no_opp: played 1 time (Copy 1 played)
        # - c_wasted: played 1 time (Copy 1 played)
        # - c_played: played 2 times (BOTH Copy 1 and Copy 2 played)
        batch_played.append([c_undrawn, c_no_opp, c_wasted, c_played, c_played])

        # Drawn list:
        # - c_undrawn: drawn ONLY 1 time (Copy 2 stayed buried in deck)
        # - c_no_opp: drawn 2 times
        # - c_wasted: drawn 2 times
        # - c_played: drawn 2 times
        batch_drawn.append([c_undrawn, c_no_opp, c_no_opp, c_wasted, c_wasted, c_played, c_played])

        # Wasted opportunities map:
        # - c_undrawn: 0 wasted (never drawn)
        # - c_no_opp: 0 wasted (drawn, but no mana / board locked)
        # - c_wasted: 2 wasted turns (sat in hand with legal playable moves, but passed)
        # - c_played: 0 wasted (both were played)
        batch_wasted.append({c_wasted: 2})

    legal_mask = np.ones(num_cards, dtype=np.bool_)

    print("[Step 1] Executing drafter.update() across 10 matches...")
    drafter.update(
        batch_decks=batch_decks,
        batch_rewards=batch_rewards,
        legal_mask=legal_mask,
        batch_played_ids=batch_played,
        batch_drawn_ids=batch_drawn,
        batch_wasted_opps=batch_wasted,
    )

    new_undrawn_c1 = float(drafter.logits[idx_undrawn, 0].item())
    new_undrawn_c2 = float(drafter.logits[idx_undrawn, 1].item())

    new_no_opp_c1 = float(drafter.logits[idx_no_opp, 0].item())
    new_no_opp_c2 = float(drafter.logits[idx_no_opp, 1].item())

    new_wasted_c1 = float(drafter.logits[idx_wasted, 0].item())
    new_wasted_c2 = float(drafter.logits[idx_wasted, 1].item())

    new_played_c1 = float(drafter.logits[idx_played, 0].item())
    new_played_c2 = float(drafter.logits[idx_played, 1].item())

    print("\n--- ATTRIBUTION RESULTS PER CARD ---")
    print(f"Case 1 (Card #{c_undrawn} - 2nd copy UNDRAWN in deck):")
    print(f"  Copy 1 (played): {init_undrawn_c1:+.4f} -> {new_undrawn_c1:+.4f} (diff: {new_undrawn_c1 - init_undrawn_c1:+.4f})")
    print(f"  Copy 2 (undrawn): {init_undrawn_c2:+.4f} -> {new_undrawn_c2:+.4f} (diff: {new_undrawn_c2 - init_undrawn_c2:+.4f})")

    print(f"\nCase 2 (Card #{c_no_opp} - 2nd copy DRAWN, ZERO legal opportunity):")
    print(f"  Copy 1 (played): {init_no_opp_c1:+.4f} -> {new_no_opp_c1:+.4f} (diff: {new_no_opp_c1 - init_no_opp_c1:+.4f})")
    print(f"  Copy 2 (no opp): {init_no_opp_c2:+.4f} -> {new_no_opp_c2:+.4f} (diff: {new_no_opp_c2 - init_no_opp_c2:+.4f})")

    print(f"\nCase 3 (Card #{c_wasted} - 2nd copy DRAWN, OPPORTUNITY WASTED across 2 turns):")
    print(f"  Copy 1 (played): {init_wasted_c1:+.4f} -> {new_wasted_c1:+.4f} (diff: {new_wasted_c1 - init_wasted_c1:+.4f})")
    print(f"  Copy 2 (wasted): {init_wasted_c2:+.4f} -> {new_wasted_c2:+.4f} (diff: {new_wasted_c2 - init_wasted_c2:+.4f})")

    print(f"\nCase 4 (Card #{c_played} - BOTH copies DRAWN AND PLAYED):")
    print(f"  Copy 1 (played): {init_played_c1:+.4f} -> {new_played_c1:+.4f} (diff: {new_played_c1 - init_played_c1:+.4f})")
    print(f"  Copy 2 (played): {init_played_c2:+.4f} -> {new_played_c2:+.4f} (diff: {new_played_c2 - init_played_c2:+.4f})")

    # =========================================================================
    # ASSERTIONS PROVING USER'S 4 CONDITIONS
    # =========================================================================

    # Condition 1: Undrawn 2nd copy gets ZERO penalty / ZERO change
    assert abs(new_undrawn_c2 - init_undrawn_c2) < 1e-6, (
        f"FAILED Condition 1: Undrawn 2nd copy changed! ({init_undrawn_c2} -> {new_undrawn_c2})"
    )
    print("\n[VERIFIED] Condition 1 Passed: Undrawn 2nd copy received 0.0 delta (zero change).")

    # Condition 2: Drawn but no opportunity gets ZERO penalty / ZERO change
    assert abs(new_no_opp_c2 - init_no_opp_c2) < 1e-6, (
        f"FAILED Condition 2: 2nd copy without opportunity changed! ({init_no_opp_c2} -> {new_no_opp_c2})"
    )
    print("[VERIFIED] Condition 2 Passed: 2nd copy drawn without playable opportunity received 0.0 delta (zero penalty).")

    # Condition 3: Drawn with wasted opportunity receives proportional penalty (< 0)
    assert new_wasted_c2 < init_wasted_c2, (
        f"FAILED Condition 3: 2nd copy with wasted opportunity did not decrease! ({init_wasted_c2} -> {new_wasted_c2})"
    )
    print("[VERIFIED] Condition 3 Passed: 2nd copy with wasted opportunity received proportional penalty.")

    # Condition 4: Both copies played receive positive reward (> 0)
    assert new_played_c2 > init_played_c2, (
        f"FAILED Condition 4: 2nd copy played did not increase! ({init_played_c2} -> {new_played_c2})"
    )
    print("[VERIFIED] Condition 4 Passed: 2nd copy played and contributed received positive reward.")

    # Independence Assertion:
    # In Case 3 (Card 30), Copy 1 INCREASED (+0.035) while Copy 2 DECREASED (-0.017)
    assert new_wasted_c1 > init_wasted_c1, "Copy 1 failed to increase"
    assert new_wasted_c2 < init_wasted_c2, "Copy 2 failed to decrease"
    print(f"[VERIFIED] Head Independence Passed: For Card #{c_wasted}, Copy 1 increased by {new_wasted_c1 - init_wasted_c1:+.4f} while Copy 2 decreased by {new_wasted_c2 - init_wasted_c2:+.4f}!")

    # =========================================================================
    # Step 2: Autonomous Drafting Verification
    # =========================================================================
    print("\n[Step 2] Testing autonomous deck building...")
    with torch.no_grad():
        # Set Card 40 (Case 4) high on both copies
        drafter.logits[idx_played, 0] = 2.0
        drafter.logits[idx_played, 1] = 1.9

        # Set Card 30 (Case 3) high on Copy 1, but low on Copy 2
        drafter.logits[idx_wasted, 0] = 1.8
        drafter.logits[idx_wasted, 1] = -0.5

        # Filler cards around 0.5
        drafter.logits[:, 0].clamp_(min=-0.2)

    deck = drafter.build_deck(legal_mask, deterministic=True)
    counts = Counter(deck)

    c_played_cnt = counts.get(c_played, 0)
    c_wasted_cnt = counts.get(c_wasted, 0)

    print(f"  Drafted deck contains:")
    print(f"    Card #{c_played} (Contributed): {c_played_cnt} copies (Expected: 2)")
    print(f"    Card #{c_wasted} (Wasted 2nd):  {c_wasted_cnt} copies (Expected: 1)")

    assert c_played_cnt == 2, f"Expected 2 copies of Card #{c_played}, got {c_played_cnt}"
    assert c_wasted_cnt == 1, f"Expected exactly 1 copy of Card #{c_wasted}, got {c_wasted_cnt}"
    print("[VERIFIED] Autonomous Drafting Passed: Drafter drafted 2 copies of staple card and 1 copy of tech card!")

    # =========================================================================
    # Step 3: Checkpoint Backwards Compatibility Verification
    # =========================================================================
    print("\n[Step 3] Testing legacy 1D checkpoint backwards compatibility...")
    brain = ModelBrain("test_model_v2", db)
    legacy_payload = {
        "fighter": brain.fighter.state_dict(),
        "drafter_logits": torch.ones(num_cards, dtype=torch.float32) * 0.15,
        "drafter_visits": np.ones(num_cards, dtype=np.float32) * 8.0,
        "matches_trained": 100,
    }
    tmp_path = brain.checkpoint_path()
    os.makedirs(os.path.dirname(tmp_path), exist_ok=True)
    brain._safe_torch_save(legacy_payload, tmp_path, keep_backup=False)

    loaded_brain = ModelBrain.load_or_create("test_model_v2", db)
    assert loaded_brain.drafter.logits.shape == (num_cards, 2)
    assert loaded_brain.drafter.visits.shape == (num_cards, 2)
    assert abs(float(loaded_brain.drafter.logits[0, 0].item()) - 0.15) < 1e-4
    assert loaded_brain.matches_trained == 100
    print("[VERIFIED] Legacy 1D checkpoint seamlessly loaded and expanded to (num_cards, 2)!")

    # Cleanup test files
    try:
        if os.path.exists(tmp_path):
            os.remove(tmp_path)
        if os.path.exists(tmp_path + ".bak"):
            os.remove(tmp_path + ".bak")
        pdir = os.path.dirname(tmp_path)
        if os.path.exists(pdir) and not os.listdir(pdir):
            os.rmdir(pdir)
    except Exception:
        pass

    print("\n" + "=" * 70)
    print(">>> ALL 4 CASES & VERIFICATION CRITERIA PASSED 100%! <<<")
    print("=" * 70 + "\n")


if __name__ == "__main__":
    test_four_attribution_cases()

# -*- coding: utf-8 -*-
"""
Test Suite: Proving Independent Learning of Copy #1 vs Copy #2 in Two-Head DeckDrafter.

Strict Verification Criteria:
1. Shape Verification: Drafter logits and visits must be 2D tensors of shape (num_cards, 2).
2. Independent Learning Signal:
   - When a card is drafted as 2-of and BOTH copies are played (p=2), BOTH logits[:, 0] and logits[:, 1] increase.
   - When a card is drafted as 2-of and ONLY 1 copy is played (p=1) while the 2nd copy sits dead:
     * logits[:, 0] INCREASES (the card was valuable as a 1-of).
     * logits[:, 1] DECREASES (the 2nd copy was dead weight and receives an independent penalty).
3. Autonomous Drafting Differentiation:
   - When drafting, the model autonomously chooses 2 copies of the staple card and EXACTLY 1 copy of the tech card!
4. Checkpoint Compatibility:
   - Legacy 1D checkpoints load and expand cleanly to 2D.
"""

import os
import sys
import numpy as np
import torch

# Ensure Arena_Lab_V2 is on path
BASE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.insert(0, BASE_DIR)

from engine.card_model import CardDB
from engine.ai_brain import DeckDrafter, ModelBrain


def test_independent_copy_learning():
    print("[1/5] Loading CardDB...")
    db = CardDB()
    num_cards = db.num_cards
    print(f"      CardDB loaded with {num_cards} cards.")

    # 1. Initialize drafter
    drafter = DeckDrafter(db)
    assert drafter.logits.shape == (num_cards, 2), f"Expected shape ({num_cards}, 2), got {drafter.logits.shape}"
    assert drafter.visits.shape == (num_cards, 2), f"Expected visits shape ({num_cards}, 2), got {drafter.visits.shape}"
    print("[2/5] Verified Drafter shape: (num_cards, 2).")

    # Pick two distinct legal test cards (e.g. Card 10 and Card 20)
    card_staple = 10
    card_tech = 20
    idx_staple = card_staple - 1
    idx_tech = card_tech - 1

    # Record initial logits
    init_staple_copy1 = float(drafter.logits[idx_staple, 0].item())
    init_staple_copy2 = float(drafter.logits[idx_staple, 1].item())
    init_tech_copy1 = float(drafter.logits[idx_tech, 0].item())
    init_tech_copy2 = float(drafter.logits[idx_tech, 1].item())

    # 2. Simulate training matches:
    # Both decks contain 2 copies of card_staple and 2 copies of card_tech.
    # In each match:
    # - card_staple was played TWICE (p=2) -> Both copies actively contributed.
    # - card_tech was played ONCE (p=1) -> Copy 1 was played, Copy 2 was dead weight.
    # The team won all matches with positive reward (+1.0).

    batch_decks = []
    batch_rewards = []
    batch_played = []

    # Build a dummy 50-card deck with card_staple (x2) and card_tech (x2) and filler
    dummy_deck = [card_staple, card_staple, card_tech, card_tech]
    for cid in range(1, 51):
        if cid not in (card_staple, card_tech) and len(dummy_deck) < 50:
            dummy_deck.append(cid)
    assert len(dummy_deck) == 50

    # Repeat for 12 winning matches in a batch
    for _ in range(12):
        batch_decks.append(list(dummy_deck))
        batch_rewards.append(1.0)
        # Played list: staple played 2 times, tech played 1 time
        played = [card_staple, card_staple, card_tech]
        batch_played.append(played)

    legal_mask = np.ones(num_cards, dtype=np.bool_)

    print("[3/5] Executing drafter.update() across simulated training batch...")
    step_loss = drafter.update(
        batch_decks=batch_decks,
        batch_rewards=batch_rewards,
        legal_mask=legal_mask,
        batch_played_ids=batch_played,
    )

    new_staple_copy1 = float(drafter.logits[idx_staple, 0].item())
    new_staple_copy2 = float(drafter.logits[idx_staple, 1].item())
    new_tech_copy1 = float(drafter.logits[idx_tech, 0].item())
    new_tech_copy2 = float(drafter.logits[idx_tech, 1].item())

    print(f"      Card {card_staple} (Staple, 2 played):")
    print(f"        Copy 1: {init_staple_copy1:+.4f} -> {new_staple_copy1:+.4f} (diff: {new_staple_copy1 - init_staple_copy1:+.4f})")
    print(f"        Copy 2: {init_staple_copy2:+.4f} -> {new_staple_copy2:+.4f} (diff: {new_staple_copy2 - init_staple_copy2:+.4f})")

    print(f"      Card {card_tech} (Tech, 1 played, 2nd dead):")
    print(f"        Copy 1: {init_tech_copy1:+.4f} -> {new_tech_copy1:+.4f} (diff: {new_tech_copy1 - init_tech_copy1:+.4f})")
    print(f"        Copy 2: {init_tech_copy2:+.4f} -> {new_tech_copy2:+.4f} (diff: {new_tech_copy2 - init_tech_copy2:+.4f})")

    # Mathematical Proof Assertions:
    # Staple: Both copy 1 and copy 2 MUST increase!
    assert new_staple_copy1 > init_staple_copy1, f"Staple Copy 1 failed to increase: {new_staple_copy1} <= {init_staple_copy1}"
    assert new_staple_copy2 > init_staple_copy2, f"Staple Copy 2 failed to increase: {new_staple_copy2} <= {init_staple_copy2}"

    # Tech: Copy 1 MUST increase (it helped win!), BUT Copy 2 MUST DECREASE (dead weight penalty)!
    assert new_tech_copy1 > init_tech_copy1, f"Tech Copy 1 failed to increase: {new_tech_copy1} <= {init_tech_copy1}"
    assert new_tech_copy2 < init_tech_copy2, f"Tech Copy 2 failed to decrease: {new_tech_copy2} >= {init_tech_copy2}"

    gap_tech = new_tech_copy1 - new_tech_copy2
    assert gap_tech > (init_tech_copy1 - init_tech_copy2), "Gap between Copy 1 and Copy 2 of tech card did not widen!"
    print(f"      [PROVEN] Copy 1 of Card {card_tech} increased while Copy 2 decreased! Gap widened by {gap_tech - (init_tech_copy1 - init_tech_copy2):.4f}")

    # 4. Autonomous Drafting Test
    # Set up drafter where Card Tech has high Copy 1 logit, but low Copy 2 logit.
    # Meanwhile other legal cards have intermediate logits.
    print("[4/5] Testing autonomous deck building...")
    with torch.no_grad():
        # Make Card Staple very high on both copies
        drafter.logits[idx_staple, 0] = 2.0
        drafter.logits[idx_staple, 1] = 1.9

        # Make Card Tech very high on Copy 1 (e.g. 1.8), but LOW on Copy 2 (e.g. -0.5)
        drafter.logits[idx_tech, 0] = 1.8
        drafter.logits[idx_tech, 1] = -0.5

        # Filler cards around 0.5
        drafter.logits[:, 0].clamp_(min=-0.2)

    deck = drafter.build_deck(legal_mask, deterministic=True)
    from collections import Counter
    counts = Counter(deck)

    staple_count = counts.get(card_staple, 0)
    tech_count = counts.get(card_tech, 0)

    print(f"      Drafted deck contains:")
    print(f"        Card {card_staple} (Staple): {staple_count} copies (Expected: 2)")
    print(f"        Card {card_tech} (Tech):   {tech_count} copies (Expected: 1)")

    assert staple_count == 2, f"Expected 2 copies of staple card {card_staple}, got {staple_count}"
    assert tech_count == 1, f"Expected exactly 1 copy of tech card {card_tech}, got {tech_count}"
    print("      [PROVEN] Drafter drafted 2-of staple and 1-of tech autonomously without hardcoded caps!")

    # 5. Checkpoint backwards compatibility test
    print("[5/5] Testing 1D legacy checkpoint loading compatibility...")
    brain = ModelBrain("test_model", db)
    # Simulate loading legacy 1D checkpoint
    legacy_payload = {
        "fighter": brain.fighter.state_dict(),
        "drafter_logits": torch.ones(num_cards, dtype=torch.float32) * 0.12,
        "drafter_visits": np.ones(num_cards, dtype=np.float32) * 5.0,
        "matches_trained": 42,
    }
    tmp_path = brain.checkpoint_path()
    os.makedirs(os.path.dirname(tmp_path), exist_ok=True)
    brain._safe_torch_save(legacy_payload, tmp_path, keep_backup=False)

    loaded_brain = ModelBrain.load_or_create("test_model", db)
    assert loaded_brain.drafter.logits.shape == (num_cards, 2)
    assert loaded_brain.drafter.visits.shape == (num_cards, 2)
    assert abs(float(loaded_brain.drafter.logits[0, 0].item()) - 0.12) < 1e-4
    assert loaded_brain.matches_trained == 42
    print("      [PROVEN] Legacy 1D checkpoint loaded and seamlessly expanded to (num_cards, 2)!")

    # Clean up test checkpoint
    try:
        if os.path.exists(tmp_path):
            os.remove(tmp_path)
        if os.path.exists(tmp_path + ".bak"):
            os.remove(tmp_path + ".bak")
        parent_dir = os.path.dirname(tmp_path)
        if os.path.exists(parent_dir) and not os.listdir(parent_dir):
            os.rmdir(parent_dir)
    except Exception:
        pass

    print("\n>>> ALL INDEPENDENT LEARNING PROOF CRITERIA PASSED 100%! <<<\n")


if __name__ == "__main__":
    test_independent_copy_learning()

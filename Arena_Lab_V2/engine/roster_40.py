# -*- coding: utf-8 -*-
"""
Defines the 40 Independent AI Models for Arena_Lab_V2:
  - 10 Playstyle Models (full 1,470 card pool + behavioral style rewards)
  - 10 Environment Models (own Element monsters + own Environment + all Spells/Covenants/Leaders)
  - 10 Tribe Models (own Tribe monsters + all 200 Neutral Barriers + own Tribe Leaders + all Spells/Covenants/Envs)
  - 10 Free Models (full 1,470 pool minus cards claimed in the latest decks of the other 9 Free models)
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, List, Optional, Set, Tuple

import numpy as np

from .card_model import (
    ELEMENTS,
    PLAYSTYLES,
    TRIBES,
    CardDB,
    MatchResult,
    MatchTelemetry,
)


@dataclass(frozen=True)
class ModelSpec:
    model_id: str
    display_name: str
    category: str          # "Playstyle" | "Environment" | "Tribe" | "Free"
    sub_index: int         # 0..9
    short_desc: str
    required_card_id: int = 0  # Non-zero for Environment models (must include own Environment)


FREE_CODENAMES = (
    "Vanguard", "Apex", "Titan", "Phantom", "Sovereign",
    "Catalyst", "Oracle", "Warden", "Nomad", "Eclipse",
)

STYLE_DESCRIPTIONS = {
    "Alpha": "Healer & Sustain — Win at high HP through relentless commander & unit healing.",
    "Sparta": "Resource Siege — Starve & lock the enemy via mana drain, discard, freeze, silence & counters.",
    "Pyro": "Burn & Ignition — Win through fire burn DoT stacks and explosive burn damage.",
    "Viper": "Bleed & Poison — Melt enemy monsters and commander via poison and kinetic bleed.",
    "Bastion": "Solid Fortress — Crush attackers against massive DEF walls, barriers, and thorn reflection.",
    "Mirage": "Clone & Steal — Win by copying, stealing, and turning the opponent's cards against them.",
    "Hive": "Swarm & Pack — Flood all 6 lanes with tokens and overwhelm with pack synergy.",
    "Reaper": "Graveyard & Necro — Resurrect fallen monsters, harvest the graveyard, and thrive on death.",
    "Tactician": "Monster Active Skills — Dominate the board by chaining monster active abilities.",
    "Arcane": "Spell Rain — Overwhelm the enemy with a relentless barrage of magic spells.",
}


def build_roster_40() -> List[ModelSpec]:
    roster: List[ModelSpec] = []

    # 1. 10 Playstyle Models
    for idx, st in enumerate(PLAYSTYLES):
        roster.append(
            ModelSpec(
                model_id=f"style_{st.lower()}",
                display_name=f"[Style] {st}",
                category="Playstyle",
                sub_index=idx,
                short_desc=STYLE_DESCRIPTIONS[st],
            )
        )

    # 2. 10 Environment Models (Monsters restricted strictly to own Element; all Envs/Spells/Leaders/Covenants free)
    for idx, el in enumerate(ELEMENTS):
        roster.append(
            ModelSpec(
                model_id=f"env_{el.lower()}",
                display_name=f"[Env] {el} Domain",
                category="Environment",
                sub_index=idx,
                short_desc=f"{el} Monsters ONLY + free choice of any Environment, Leader, Covenant, or Spell.",
                required_card_id=0,
            )
        )

    # 3. 10 Tribe Models
    for idx, tr in enumerate(TRIBES):
        roster.append(
            ModelSpec(
                model_id=f"tribe_{tr.lower()}",
                display_name=f"[Tribe] {tr}",
                category="Tribe",
                sub_index=idx,
                short_desc=f"{tr} Monsters (+ Neutral Barriers) ONLY + free choice of any Leader, Env, Covenant, or Spell.",
            )
        )

    # 4. 10 Free Models (Mutually Disjoint Latest Decks)
    for idx, cname in enumerate(FREE_CODENAMES):
        roster.append(
            ModelSpec(
                model_id=f"free_{idx + 1:02d}",
                display_name=f"[Free] Free_{idx + 1:02d} ({cname})",
                category="Free",
                sub_index=idx,
                short_desc="Open 1,470 pool — strictly banned from picking cards in other 9 Free decks.",
            )
        )

    # 5. Model #41: The Anomaly / Exploit Hunter
    roster.append(
        ModelSpec(
            model_id="anomaly_glitch",
            display_name="[Anomaly] Glitch_41 (Exploit Hunter)",
            category="Anomaly",
            sub_index=0,
            short_desc="Exploit Hunter — Avoids top overused meta cards & gets rewarded for extreme bursts, stat breaks, speed glitches & comebacks.",
        )
    )

    assert len(roster) == 41
    return roster


ROSTER_40: List[ModelSpec] = build_roster_40()
ROSTER_MAP: Dict[str, ModelSpec] = {m.model_id: m for m in ROSTER_40}


def get_model_spec(model_id: str) -> ModelSpec:
    return ROSTER_MAP[model_id]


def build_legal_card_mask(
    spec: ModelSpec,
    db: CardDB,
    free_excluded_ids: Optional[Set[int]] = None,
) -> np.ndarray:
    """
    Returns a boolean mask of length `db.num_cards` (index i corresponds to card_id = i + 1)
    specifying which of the 1,470 cards this model is legally allowed to draft.
    """
    mask = np.zeros(db.num_cards, dtype=np.bool_)
    excl = free_excluded_ids if (spec.category in ("Free", "Anomaly") and free_excluded_ids) else set()

    for idx in range(db.num_cards):
        cid = idx + 1
        if cid in excl:
            continue
        c = db.get(cid)

        if spec.category in ("Playstyle", "Free", "Anomaly"):
            # Playstyle: full 1,470 pool
            # Free: 1,470 minus cards in other 9 Free decks
            # Anomaly: 1,470 minus top overused meta cards across the 40 models
            mask[idx] = True

        elif spec.category == "Environment":
            el = spec.sub_index
            if c.type == "monster":
                # MANDATORY: ONLY monsters of own Element (e.g. Fire model picks ONLY Fire monsters)
                mask[idx] = (c.element == el)
            else:
                # All Environments, Leaders, Covenants, and Spells are 100% open & free to choose!
                mask[idx] = True

        elif spec.category == "Tribe":
            tr = spec.sub_index
            if c.type == "monster":
                # MANDATORY: Own Tribe combat monsters + Neutral Barriers (tribe == -1)
                mask[idx] = (c.tribe == tr) or c.is_barrier
            else:
                # All 60 Leaders, 10 Environments, 100 Covenants, and 300 Spells are 100% open & free to choose!
                mask[idx] = True

    return mask


# ----------------------------------------------------------------------
# Behavioral Style Score (0.0 .. 1.0) & Final RL Reward Computation
# ----------------------------------------------------------------------
def compute_style_score(spec: ModelSpec, result: MatchResult, side: int) -> float:
    """
    Returns a clean [0.0, 1.0] behavioral mastery score measuring how strongly the
    agent actually executed its playstyle during battle (independent of card labels!).
    """
    t: MatchTelemetry = result.telemetry[side]
    turns = max(1, result.turns)

    if spec.category == "Anomaly":
        # Anomaly / Exploit Score [0.0 .. 1.0]: rewards extreme 1-turn burst (OTK), broken stat-freak monsters,
        # miracle comebacks, speed glitches (<=7 turns), non-combat wins, or bizarre deck compositions!
        burst_s = min(1.0, max(0.0, (t.max_turn_dmg - 75.0) / 115.0))
        stat_s = min(1.0, max(0.0, max((t.max_unit_atk - 34.0) / 30.0, (t.max_unit_def - 55.0) / 40.0)))
        comeback_s = min(1.0, t.comeback_deficit / 170.0)
        speed_s = 1.0 if turns <= 7 else max(0.0, (11.0 - turns) / 4.0)
        non_combat_s = min(1.0, (t.dot_dmg_dealt + t.reflect_dmg_dealt + t.direct_face_spells * 22.0) / 210.0)
        combo_s = min(1.0, (t.clones_made * 2 + t.revives_done * 2 + t.actives_used) / 12.0)
        peak_exploit = max(burst_s, stat_s, comeback_s, speed_s, non_combat_s, combo_s)
        avg_exploit = (burst_s + stat_s + comeback_s + speed_s + non_combat_s + combo_s) / 6.0
        return float(min(1.0, 0.70 * peak_exploit + 0.30 * avg_exploit))

    if spec.category != "Playstyle":
        # Non-style models: mild HP preservation quality indicator for analytics
        return float(max(0.0, min(1.0, t.final_hp / max(1, t.max_hp_end))))

    st = PLAYSTYLES[spec.sub_index]

    if st == "Alpha":
        # 1. Healer & Sustain: Win at maximum possible HP + heavy commander & unit healing
        hp_ratio = max(0.0, min(1.0, t.final_hp / max(1, t.max_hp_end)))
        heal_s = min(1.0, (t.hp_healed + t.def_healed) / 160.0)
        return 0.55 * hp_ratio + 0.45 * heal_s

    if st == "Sparta":
        # 2. Resource Siege: Heavy disruption (mana drain, discard, deck burn, freeze, silence, root, counter tech)
        return min(1.0, t.disruptions_applied / 12.0)

    if st == "Pyro":
        # 3. Burn & Ignition: Burn DoT damage + explosive fire face damage
        dot_s = min(1.0, t.dot_dmg_dealt / 90.0)
        face_s = min(1.0, t.direct_face_spells / 5.0)
        return 0.65 * dot_s + 0.35 * face_s

    if st == "Viper":
        # 4. Bleed & Poison: Poison/bleed erosion + monster attrition kills
        dot_s = min(1.0, t.dot_dmg_dealt / 100.0)
        kill_s = min(1.0, t.monsters_killed / 10.0)
        return 0.65 * dot_s + 0.35 * kill_s

    if st == "Bastion":
        # 5. Solid Fortress: Thorn reflection damage + massive unit DEF walls + commander survival
        ref_s = min(1.0, t.reflect_dmg_dealt / 75.0)
        def_s = min(1.0, t.max_unit_def / 55.0)
        hp_ratio = max(0.0, min(1.0, t.final_hp / max(1, t.max_hp_end)))
        return 0.45 * ref_s + 0.35 * def_s + 0.20 * hp_ratio

    if st == "Mirage":
        # 6. Clone & Steal: Copying, cloning, stealing enemy monsters & hand cards + evasion
        clone_steal_s = min(1.0, t.clones_made / 4.0)
        ev_s = min(1.0, t.evasions_triggered / 4.0)
        return 0.70 * clone_steal_s + 0.30 * ev_s

    if st == "Hive":
        # 7. Swarm & Pack: Token swarm + maintaining a full 5–6 lane board
        tok_s = min(1.0, t.tokens_spawned / 8.0)
        wide_s = min(1.0, t.turns_with_full_board / 6.0)
        max_b = min(1.0, t.max_board_units / 6.0)
        return 0.50 * tok_s + 0.30 * wide_s + 0.20 * max_b

    if st == "Reaper":
        # 8. Graveyard & Necro: Graveyard revives, grave returns & sacrifices
        rev_s = min(1.0, (t.revives_done * 2 + t.grave_returns) / 6.0)
        sac_s = min(1.0, t.sacrifices_made / 6.0)
        return 0.75 * rev_s + 0.25 * sac_s

    if st == "Tactician":
        # 9. Monster Active Skills: Winning via monster active abilities & card tempo
        act_s = min(1.0, t.actives_used / 9.0)
        draw_s = min(1.0, t.cards_drawn / 24.0)
        return 0.80 * act_s + 0.20 * draw_s

    if st == "Arcane":
        # 10. Spell Rain: Winning via heavy spell casting & magic barrage
        sp_s = min(1.0, t.spells_cast / 12.0)
        face_sp = min(1.0, t.direct_face_spells / 5.0)
        return 0.75 * sp_s + 0.25 * face_sp

    return 0.0


def compute_match_reward(spec: ModelSpec, result: MatchResult, side: int) -> Tuple[float, float]:
    """
    Returns `(total_reward, style_score)`.
    Anti-reward-hacking rule: Winning is ALWAYS the dominant signal (+1.0 vs -1.0).
    For Playstyle models, style mastery adds up to +0.45 on a WIN, and only +0.10 on a LOSS,
    so a model can NEVER prefer losing with high style over winning!
    """
    style_score = compute_style_score(spec, result, side)
    if result.winner == side:
        base = 1.0
    elif result.winner == (1 - side):
        base = -1.0
    else:
        base = 0.0

    # Small smooth HP differential shaping (+/- 0.06) to help early learning converge faster
    hp_diff = (result.final_hp[side] - result.final_hp[1 - side]) / 500.0
    hp_shaping = 0.06 * hp_diff

    if spec.category in ("Playstyle", "Anomaly"):
        w_bonus = 0.55 if spec.category == "Anomaly" else 0.45
        style_bonus = (w_bonus * style_score) if result.winner == side else (0.10 * style_score)
        return float(base + hp_shaping + style_bonus), float(style_score)

    return float(base + hp_shaping), float(style_score)

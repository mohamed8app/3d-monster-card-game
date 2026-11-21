# -*- coding: utf-8 -*-
"""
Core data structures and Constitution constants for Arena_Lab_V2.
All stats, percentages, and damage values are strictly rounded to integers.
"""

from __future__ import annotations

import json
import os
from typing import Any, Dict, List, Optional, Tuple

# === Constitution Constants ===
LEADER_HP = 500
DECK_SIZE = 50
MAX_COPIES = 2
MAX_HAND = 7
BOARD_SLOTS = 6
CENTER_SLOTS = (2, 3)
FLANK_SLOTS = (0, 1, 4, 5)
BUFF_CAP_ATK = 10
BUFF_CAP_DEF = 15
MAX_MANA_CAP = 10
MAX_TURNS = 60

ELEMENTS = ("Fire", "Water", "Nature", "Electric", "Ice", "Earth", "Dark", "Light", "Air", "Desert")
TRIBES = ("Celestials", "Demons", "Ghosts", "Mages", "Warriors", "Dwarves", "Giants", "Elves", "Beasts", "Insects")
PLAYSTYLES = ("Alpha", "Sparta", "Pyro", "Viper", "Bastion", "Mirage", "Hive", "Reaper", "Tactician", "Arcane")
ROLES = ("Attacker", "Assassin", "Defender", "Barrier", "Support", "Disruptor")

DEFAULT_CARDS_JSON = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "..", "data", "cards_v2_2270.json")
)


def ri(value: float) -> int:
    """Strictly round any calculated numeric game value to the nearest integer."""
    return int(round(value))


def pct_of(base: int, pct: int) -> int:
    """Strictly integer-rounded percentage of an integer base stat or damage."""
    return int(round(base * pct / 100.0))


class CardDef:
    __slots__ = (
        "id", "type", "name", "element", "element_name", "tribe", "tribe_name",
        "playstyle", "playstyle_name", "role", "role_name", "tier", "mana",
        "atk", "deff", "is_barrier", "is_all_attack", "p_cnt", "a_cnt",
        "env_id", "leader_ops", "covenant_ops", "spell_ops", "passives", "actives",
    )

    def __init__(self, raw: Dict[str, Any]) -> None:
        self.id: int = int(raw["id"])
        self.type: str = str(raw["type"])
        self.name: str = str(raw["name"])
        self.element: int = int(raw["element"])
        self.element_name: str = str(raw["element_name"])
        self.tribe: int = int(raw["tribe"])
        self.tribe_name: str = str(raw["tribe_name"])
        self.playstyle: int = int(raw["playstyle"])
        self.playstyle_name: str = str(raw["playstyle_name"])
        self.role: int = int(raw["role"])
        self.role_name: str = str(raw["role_name"])
        self.tier: int = int(raw["tier"])
        self.mana: int = int(raw["mana"])
        self.is_barrier: bool = bool(raw.get("is_barrier", False))
        # Hard Constitution rule: Barriers ALWAYS have 0 ATK and Neutral tribe (-1)
        self.atk: int = 0 if self.is_barrier else int(raw["atk"])
        self.deff: int = int(raw["def"])
        self.p_cnt: int = int(raw.get("p_cnt", 0))
        self.a_cnt: int = 0 if self.is_barrier else int(raw.get("a_cnt", 0))
        self.env_id: int = int(raw.get("env_id", -1))
        self.leader_ops: Optional[Dict[str, Any]] = raw.get("leader_ops")
        self.covenant_ops: Optional[Dict[str, Any]] = raw.get("covenant_ops")
        self.spell_ops: Optional[Dict[str, Any]] = raw.get("spell_ops")
        self.passives: List[Dict[str, Any]] = raw.get("passives", [])
        self.actives: List[Dict[str, Any]] = [] if self.is_barrier else raw.get("actives", [])
        self.is_all_attack: bool = any(p.get("kind") == "all_attack" for p in self.passives)


class CardDB:
    """Fast in-memory database of all 2,270 compiled cards (IDs 1..2270)."""

    def __init__(self, json_path: str = DEFAULT_CARDS_JSON) -> None:
        with open(json_path, "r", encoding="utf-8") as f:
            payload = json.load(f)
        self.version: str = payload.get("version", "2.0")
        self.cards: Dict[int, CardDef] = {}
        self.all_ids: List[int] = []
        self.envs: List[int] = []
        self.leaders: List[int] = []
        self.covenants: List[int] = []
        self.spells: List[int] = []
        self.monsters: List[int] = []
        self.barriers: List[int] = []
        self.combat_monsters: List[int] = []
        self.by_element: Dict[int, List[int]] = {i: [] for i in range(10)}
        self.by_tribe: Dict[int, List[int]] = {i: [] for i in range(10)}
        self.by_style: Dict[int, List[int]] = {i: [] for i in range(10)}
        self.leaders_by_tribe: Dict[int, List[int]] = {i: [] for i in range(10)}

        for raw in payload["cards"]:
            c = CardDef(raw)
            self.cards[c.id] = c
            self.all_ids.append(c.id)
            if c.type == "environment":
                self.envs.append(c.id)
            elif c.type == "leader":
                self.leaders.append(c.id)
                if 0 <= c.tribe < 10:
                    self.leaders_by_tribe[c.tribe].append(c.id)
            elif c.type == "covenant":
                self.covenants.append(c.id)
            elif c.type == "spell":
                self.spells.append(c.id)
            elif c.type == "monster":
                self.monsters.append(c.id)
                if c.is_barrier:
                    self.barriers.append(c.id)
                else:
                    self.combat_monsters.append(c.id)
                if 0 <= c.element < 10:
                    self.by_element[c.element].append(c.id)
                if 0 <= c.tribe < 10:
                    self.by_tribe[c.tribe].append(c.id)
            if 0 <= c.playstyle < 10:
                self.by_style[c.playstyle].append(c.id)

        self.all_ids.sort()
        self.num_cards: int = len(self.all_ids)
        assert self.num_cards == 2270, f"Expected 2270 cards, found {self.num_cards}"

    def get(self, card_id: int) -> CardDef:
        return self.cards[card_id]


_SINGLETON_DB: Optional[CardDB] = None


def get_card_db(json_path: str = DEFAULT_CARDS_JSON) -> CardDB:
    global _SINGLETON_DB
    if _SINGLETON_DB is None:
        _SINGLETON_DB = CardDB(json_path)
    return _SINGLETON_DB


class HandCard:
    __slots__ = ("card_id", "cost_mod")

    def __init__(self, card_id: int, cost_mod: int = 0) -> None:
        self.card_id: int = int(card_id)
        self.cost_mod: int = int(cost_mod)


class MonsterUnit:
    __slots__ = (
        "card_id", "name", "owner", "slot", "element", "tribe", "playstyle", "role",
        "tier", "mana", "base_atk", "base_def", "atk", "deff", "max_def",
        "perm_atk_buff", "perm_def_buff", "temp_atk_buff", "temp_def_buff",
        "is_barrier", "atk_locked", "is_all_attack", "is_spy", "spy_dmg", "spy_poison",
        "is_token", "is_clone", "is_resurrected", "summoned_turn",
        "can_attack", "attacked_this_turn", "skills_used_this_turn",
        "double_skill_1t", "double_strike_temp",
        "burn", "poison", "bleed", "frozen", "silenced", "rooted", "weakened",
        "stealth", "taunt", "haste", "double_strike", "evasion_pct", "life_leech_pct",
        "reflect_flat", "reflect_pct", "cleave_pct", "bypass_pct",
        "spell_immune", "status_immune", "enrage_atk", "enrage_reflect", "enrage_active",
        "undying_ready", "undying_used", "ascend_turns", "ascend_kills", "ascended",
        "hydra_split_turn", "shifting_curse_dmg", "shifting_curse_enemy_only",
        "prev_turn_def", "temp_mc_orig_owner",
        "passives", "actives", "chameleon", "lone_wolf_atk", "lone_wolf_def",
        "open_lane_atk", "flank_atk", "pack_atk", "pack_def", "skill_conduit",
        "spell_power", "cost_discount_mode", "grave_scale_atk", "grave_scale_def",
        "aura_weaken_opp", "on_spell_atk", "on_spell_def", "on_skill_atk", "on_skill_def",
        "on_death_atk", "on_death_def", "on_death_dmg", "on_death_regen_def", "on_death_regen_hp",
        "on_hit_burn", "on_hit_poison", "on_hit_bleed", "strip_buffs_bonus",
        "turn_heal_def", "turn_heal_hp", "cleanse_aura_def", "dmg_reduce_flat",
        "spell_ping_dmg",
    )

    def __init__(
        self,
        card_id: int,
        name: str,
        owner: int,
        slot: int,
        element: int,
        tribe: int,
        playstyle: int,
        role: int,
        tier: int,
        mana: int,
        atk: int,
        deff: int,
        is_barrier: bool = False,
        is_all_attack: bool = False,
        is_token: bool = False,
        is_clone: bool = False,
        is_resurrected: bool = False,
        is_spy: bool = False,
        passives: Optional[List[Dict[str, Any]]] = None,
        actives: Optional[List[Dict[str, Any]]] = None,
        summoned_turn: int = 1,
    ) -> None:
        self.card_id: int = int(card_id)
        self.name: str = name
        self.owner: int = int(owner)
        self.slot: int = int(slot)
        self.element: int = int(element)
        self.tribe: int = -1 if is_barrier else int(tribe)
        self.playstyle: int = int(playstyle)
        self.role: int = int(role)
        self.tier: int = int(tier)
        self.mana: int = int(mana)

        self.is_barrier: bool = bool(is_barrier)
        self.is_all_attack: bool = bool(is_all_attack)
        self.atk_locked: bool = bool(is_barrier or is_all_attack or is_spy)
        self.is_spy: bool = bool(is_spy)
        self.spy_dmg: int = 0
        self.spy_poison: bool = False
        self.is_token: bool = bool(is_token)
        self.is_clone: bool = bool(is_clone)
        self.is_resurrected: bool = bool(is_resurrected)

        base_a = 0 if (self.is_barrier or self.is_spy) else max(0, int(atk))
        base_d = max(1, int(deff))
        self.base_atk: int = base_a
        self.base_def: int = base_d
        self.atk: int = base_a
        self.deff: int = base_d
        self.max_def: int = base_d
        self.prev_turn_def: int = base_d

        self.perm_atk_buff: int = 0
        self.perm_def_buff: int = 0
        self.temp_atk_buff: int = 0
        self.temp_def_buff: int = 0

        self.summoned_turn: int = int(summoned_turn)
        self.can_attack: bool = False
        self.attacked_this_turn: bool = False
        self.skills_used_this_turn: int = 0
        self.double_skill_1t: bool = False
        self.double_strike_temp: bool = False

        # Status effects (all integers)
        self.burn: int = 0
        self.poison: int = 0
        self.bleed: int = 0
        self.frozen: int = 0
        self.silenced: int = 0
        self.rooted: int = 0
        self.weakened: int = 0

        # Keywords / passive caches (all integers / bools)
        self.stealth: bool = False
        self.taunt: bool = False
        self.haste: bool = False
        self.double_strike: bool = False
        self.evasion_pct: int = 0
        self.life_leech_pct: int = 0
        self.reflect_flat: int = 0
        self.reflect_pct: int = 0
        self.cleave_pct: int = 0
        self.bypass_pct: int = 0
        self.spell_immune: bool = False
        self.status_immune: bool = False
        self.enrage_atk: int = 0
        self.enrage_reflect: int = 0
        self.enrage_active: bool = False

        self.undying_ready: bool = False
        self.undying_used: bool = False
        self.ascend_turns: int = 0
        self.ascend_kills: int = 0
        self.ascended: bool = False
        self.hydra_split_turn: int = -1
        self.shifting_curse_dmg: int = 0
        self.shifting_curse_enemy_only: bool = False
        self.temp_mc_orig_owner: int = -1

        self.passives: List[Dict[str, Any]] = list(passives) if passives else []
        self.actives: List[Dict[str, Any]] = [] if (self.is_barrier or self.is_spy) else (list(actives) if actives else [])

        self.chameleon: bool = False
        self.lone_wolf_atk: int = 0
        self.lone_wolf_def: int = 0
        self.open_lane_atk: int = 0
        self.flank_atk: int = 0
        self.pack_atk: int = 0
        self.pack_def: int = 0
        self.skill_conduit: int = 0
        self.spell_power: int = 0
        self.cost_discount_mode: int = -1
        self.grave_scale_atk: int = 0
        self.grave_scale_def: int = 0
        self.aura_weaken_opp: int = 0
        self.on_spell_atk: int = 0
        self.on_spell_def: int = 0
        self.on_skill_atk: int = 0
        self.on_skill_def: int = 0
        self.on_death_atk: int = 0
        self.on_death_def: int = 0
        self.on_death_dmg: int = 0
        self.on_death_regen_def: int = 0
        self.on_death_regen_hp: int = 0
        self.on_hit_burn: int = 0
        self.on_hit_poison: int = 0
        self.on_hit_bleed: int = 0
        self.strip_buffs_bonus: int = 0
        self.turn_heal_def: int = 0
        self.turn_heal_hp: int = 0
        self.cleanse_aura_def: int = 0
        self.dmg_reduce_flat: int = 0
        self.spell_ping_dmg: int = 0

    def apply_perm_buff(self, d_atk: int = 0, d_def: int = 0) -> Tuple[int, int]:
        """Applies permanent buff respecting Constitution caps (+10 ATK / +15 DEF) & Barrier/All-Attack ATK Lock."""
        d_atk = int(d_atk)
        d_def = int(d_def)
        if self.is_barrier or self.atk_locked or self.is_spy:
            d_atk = 0
        if d_atk > 0:
            allowed_a = max(0, BUFF_CAP_ATK - self.perm_atk_buff)
            d_atk = min(d_atk, allowed_a)
            self.perm_atk_buff += d_atk
            self.atk += d_atk
        if d_def > 0:
            allowed_d = max(0, BUFF_CAP_DEF - self.perm_def_buff)
            d_def = min(d_def, allowed_d)
            self.perm_def_buff += d_def
            self.deff += d_def
            self.max_def += d_def
        return d_atk, d_def

    def apply_temp_buff(self, d_atk: int = 0, d_def: int = 0) -> None:
        """Applies 1-turn temporary buff; Barriers and ATK-locked units NEVER gain ATK."""
        d_atk = int(d_atk)
        d_def = int(d_def)
        if self.is_barrier or self.atk_locked or self.is_spy:
            d_atk = 0
        if d_atk != 0:
            self.temp_atk_buff += d_atk
        if d_def > 0:
            self.temp_def_buff += d_def
            self.deff += d_def
            self.max_def += d_def

    def heal_def(self, amount: int) -> int:
        amt = max(0, int(amount))
        if amt <= 0 or self.deff >= self.max_def:
            return 0
        healed = min(amt, self.max_def - self.deff)
        self.deff += healed
        return healed

    def cleanse(self) -> bool:
        had = (self.burn > 0 or self.poison > 0 or self.bleed > 0
               or self.frozen > 0 or self.silenced > 0 or self.rooted > 0 or self.weakened > 0)
        self.burn = 0
        self.poison = 0
        self.bleed = 0
        self.frozen = 0
        self.silenced = 0
        self.rooted = 0
        self.weakened = 0
        return had

    def is_silenced(self) -> bool:
        return self.silenced > 0

    def can_ever_attack(self) -> bool:
        return (not self.is_barrier) and (not self.is_spy)


class MatchTelemetry:
    __slots__ = (
        "dmg_to_face", "dmg_to_monsters", "dot_dmg_dealt", "hp_healed", "def_healed",
        "reflect_dmg_dealt", "monsters_killed", "spells_cast", "actives_used",
        "tokens_spawned", "clones_made", "revives_done", "grave_returns",
        "disruptions_applied", "evasions_triggered", "cards_drawn", "cards_burned_hand7",
        "max_board_units", "cards_played_ids", "final_hp", "max_hp_end",
        "turns_with_full_board", "direct_face_spells", "sacrifices_made",
        "max_turn_dmg", "peak_turn_num", "max_unit_atk", "max_unit_def",
        "multi_kill_turns", "highlight_triggers", "comeback_deficit",
        "lead_swings", "clutch_win", "excitement_score", "highlight_tags",
        "hand_sum_start", "hand_sum_end", "hand_turns", "empty_hand_turns",
    )

    def __init__(self) -> None:
        self.dmg_to_face: int = 0
        self.dmg_to_monsters: int = 0
        self.dot_dmg_dealt: int = 0
        self.hp_healed: int = 0
        self.def_healed: int = 0
        self.reflect_dmg_dealt: int = 0
        self.monsters_killed: int = 0
        self.spells_cast: int = 0
        self.actives_used: int = 0
        self.tokens_spawned: int = 0
        self.clones_made: int = 0
        self.revives_done: int = 0
        self.grave_returns: int = 0
        self.disruptions_applied: int = 0
        self.evasions_triggered: int = 0
        self.cards_drawn: int = 0
        self.cards_burned_hand7: int = 0
        self.max_board_units: int = 0
        self.cards_played_ids: List[int] = []
        self.final_hp: int = LEADER_HP
        self.max_hp_end: int = LEADER_HP
        self.turns_with_full_board: int = 0
        self.direct_face_spells: int = 0
        self.sacrifices_made: int = 0
        # Excitement & Anomaly metrics (all integers / fast counters)
        self.max_turn_dmg: int = 0
        self.peak_turn_num: int = 1
        self.max_unit_atk: int = 0
        self.max_unit_def: int = 0
        self.multi_kill_turns: int = 0
        self.highlight_triggers: int = 0
        self.comeback_deficit: int = 0
        self.lead_swings: int = 0
        self.clutch_win: bool = False
        self.excitement_score: int = 0
        self.highlight_tags: Dict[str, int] = {}
        self.hand_sum_start: int = 0
        self.hand_sum_end: int = 0
        self.hand_turns: int = 0
        self.empty_hand_turns: int = 0


class PlayerState:
    __slots__ = (
        "side", "hp", "max_hp", "mana", "max_mana", "temp_mana", "locked_mana_next",
        "sacrificed_this_turn", "base_deck", "deck", "deck_cycles", "hand", "graveyard", "board",
        "leader_card_id", "leader_active", "leader_muted_turns", "leader_exorcised",
        "leader_ops", "covenant_card_id", "covenant_active", "covenant_destroyed",
        "covenant_ops", "echo_next_spell", "spell_power_temp", "skill_bonus_temp",
        "skill_discount_temp", "no_draw_turns", "deck_out_lost", "cards_played_turn",
        "summons_turn", "telemetry",
    )

    def __init__(self, side: int, deck_ids: List[int]) -> None:
        self.side: int = int(side)
        self.hp: int = LEADER_HP
        self.max_hp: int = LEADER_HP
        self.mana: int = 1
        self.max_mana: int = 1
        self.temp_mana: int = 0
        self.locked_mana_next: int = 0
        self.sacrificed_this_turn: bool = False
        self.base_deck: List[int] = list(deck_ids)
        self.deck: List[int] = list(deck_ids)
        self.deck_cycles: int = 1
        self.hand: List[HandCard] = []
        self.graveyard: List[int] = []
        self.board: List[Optional[MonsterUnit]] = [None] * BOARD_SLOTS

        self.leader_card_id: int = 0
        self.leader_active: bool = False
        self.leader_muted_turns: int = 0
        self.leader_exorcised: bool = False
        self.leader_ops: Optional[Dict[str, Any]] = None

        self.covenant_card_id: int = 0
        self.covenant_active: bool = False
        self.covenant_destroyed: bool = False
        self.covenant_ops: Optional[Dict[str, Any]] = None

        self.echo_next_spell: bool = False
        self.spell_power_temp: int = 0
        self.skill_bonus_temp: int = 0
        self.skill_discount_temp: int = 0
        self.no_draw_turns: int = 0
        self.deck_out_lost: bool = False
        self.cards_played_turn: int = 0
        self.summons_turn: int = 0
        self.telemetry: MatchTelemetry = MatchTelemetry()

    def hand_limit(self) -> int:
        if self.covenant_active and self.covenant_ops:
            return min(MAX_HAND, int(self.covenant_ops.get("max_hand_self", MAX_HAND)))
        return MAX_HAND

    def board_limit(self) -> int:
        if self.covenant_active and self.covenant_ops:
            return min(BOARD_SLOTS, int(self.covenant_ops.get("max_board_self", BOARD_SLOTS)))
        return BOARD_SLOTS

    def mana_cap(self) -> int:
        if self.covenant_active and self.covenant_ops:
            return min(MAX_MANA_CAP, int(self.covenant_ops.get("max_mana_self", MAX_MANA_CAP)))
        return MAX_MANA_CAP

    def add_to_hand(self, card_id: int, cost_mod: int = 0) -> Optional[int]:
        """Adds card to hand. If hand is already at limit (Max 7), burns OLDEST card to graveyard."""
        burned: Optional[int] = None
        cap = self.hand_limit()
        while len(self.hand) >= cap and self.hand:
            oldest = self.hand.pop(0)
            self.graveyard.append(oldest.card_id)
            self.telemetry.cards_burned_hand7 += 1
            burned = oldest.card_id
        self.hand.append(HandCard(card_id, cost_mod))
        return burned

    def board_units(self) -> List[MonsterUnit]:
        return [u for u in self.board if u is not None]

    def board_count(self) -> int:
        return sum(1 for u in self.board if u is not None)

    def empty_slots(self) -> List[int]:
        return [i for i in range(BOARD_SLOTS) if self.board[i] is None]

    def heal_hp(self, amount: int) -> int:
        if self.covenant_active and self.covenant_ops and self.covenant_ops.get("ban_self_heal"):
            return 0
        amt = max(0, int(amount))
        if amt <= 0 or self.hp <= 0:
            return 0
        healed = min(amt, self.max_hp - self.hp)
        if healed > 0:
            self.hp += healed
            self.telemetry.hp_healed += healed
        return healed


class MatchResult:
    __slots__ = ("winner", "turns", "win_reason", "telemetry", "final_hp", "decks", "turn_events")

    def __init__(
        self,
        winner: int,
        turns: int,
        win_reason: str,
        telemetry: Tuple[MatchTelemetry, MatchTelemetry],
        final_hp: Tuple[int, int],
        decks: Tuple[List[int], List[int]],
        turn_events: Optional[List[Tuple[int, int, int, int, int]]] = None,
    ) -> None:
        self.winner: int = int(winner)  # 0 or 1 (-1 draw)
        self.turns: int = int(turns)
        self.win_reason: str = win_reason
        self.telemetry: Tuple[MatchTelemetry, MatchTelemetry] = telemetry
        self.final_hp: Tuple[int, int] = final_hp
        self.decks: Tuple[List[int], List[int]] = decks
        # Per-turn tuple: (turn_num, dmg_dealt, kills, lead_swing, highlights)
        self.turn_events: List[Tuple[int, int, int, int, int]] = turn_events or []

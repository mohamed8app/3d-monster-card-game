"""
Full-Rules Endless TCG Battle Simulator (Tkinter GUI + Multiprocessing up to 1,000,000 Battles)
==================================================================================================
Mirrors UnityGame combat rules cleanly:
  - 452-card database (240 Monsters + 200 Spells across 20 templates + 12 Environments)
  - 50-card unique opening deck, 6 opening hand cards, 1 card drawn per turn
  - Endless Waves after deck depletion (~80% unseen escalating-pity + ~20% fallen veterans comeback)
  - Full 6-slot tactical board, all Monster Passives, Active Skills, Spells, Environments, & Sacrifice AI
  - Per-card Win Rate evaluation (Monster + its Passives + Active Skill as one unified card!)
"""

from __future__ import annotations

import csv
import json
import math
import multiprocessing as mp
import os
import queue
import random
import threading
import time
import tkinter as tk
from dataclasses import dataclass, field
from tkinter import filedialog, messagebox, scrolledtext, ttk
from typing import Dict, List, Optional, Tuple

try:
    import openpyxl
    from openpyxl.styles import Alignment, Font, PatternFill
    HAS_OPENPYXL = True
except ImportError:
    HAS_OPENPYXL = False

DEFAULT_JSON_PATH = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "..", "UnityGame", "Assets", "Resources", "cards_10000.json")
)
DEFAULT_EXPORT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "Data"))

ELEMENTS = [
    "Fire", "Water", "Ice", "Earth", "Electric", "Air",
    "Forest", "Light", "Dark", "Mecha", "Ghosts", "Arcane",
]

TYPE_AR: Dict[str, str] = {
    "monster": "وحش",
    "spell": "تعويذة",
    "env": "بيئة",
}
TYPE_FROM_AR: Dict[str, str] = {v: k for k, v in TYPE_AR.items()}

ELEM_AR: Dict[str, str] = {
    "Fire": "نار",
    "Water": "ماء",
    "Ice": "جليد",
    "Earth": "أرض",
    "Electric": "كهرباء",
    "Air": "هواء",
    "Forest": "غابة",
    "Light": "نور",
    "Dark": "ظلام",
    "Mecha": "ميكا",
    "Ghosts": "أشباح",
    "Arcane": "غامض",
}
ELEM_FROM_AR: Dict[str, str] = {v: k for k, v in ELEM_AR.items()}

ROLE_AR: Dict[str, str] = {
    "Attacker": "مهاجم",
    "Pure Shield": "درع صافي",
    "Balanced": "متوازن",
}

ENV_MATRIX: Dict[str, Tuple[str, set]] = {
    "Fire": ("Fire", {"Forest", "Ice", "Mecha"}),
    "Water": ("Water", {"Fire", "Earth", "Mecha"}),
    "Ice": ("Ice", {"Water", "Forest", "Air"}),
    "Earth": ("Earth", {"Electric", "Fire", "Ice"}),
    "Electric": ("Electric", {"Mecha", "Water", "Air"}),
    "Air": ("Air", {"Ghosts", "Earth", "Dark"}),
    "Forest": ("Forest", {"Earth", "Water", "Air"}),
    "Light": ("Light", {"Dark", "Ghosts", "Arcane"}),
    "Dark": ("Dark", {"Light", "Arcane", "Electric"}),
    "Mecha": ("Mecha", {"Forest", "Ghosts", "Light"}),
    "Ghosts": ("Ghosts", {"Fire", "Electric", "Arcane"}),
    "Arcane": ("Arcane", {"Ice", "Light", "Dark"}),
}

# Slot ordering matching AIOpponent.cs
SHIELD_SLOTS = (2, 3, 0, 5, 1, 4)
ATTACK_SLOTS = (0, 1, 4, 5, 2, 3)
BALANCED_SLOTS = (2, 3, 1, 4, 0, 5)
FLANK_SLOTS = (0, 1, 4, 5)
CENTER_SLOTS = (2, 3)

# SpellType integer codes matching CardData.cs + SpellDatabase.cs
ST_LINK_WINGS_STRIKE = 10
ST_COPY_SUPPORT_PASSIVE = 11
ST_LINK_CENTER_TWIN = 12
ST_ELEMENTAL_SYNERGY_SHIELD = 13
ST_SWAP_WING_CENTER = 14
ST_PULL_CENTER_WING = 15
ST_PUSH_CENTER_OPEN = 16
ST_TELEPORT_WEAKEST = 17
ST_CONVERT_DEF_ATK = 18
ST_DIST_SHIELD_WINGS = 19
ST_INVERT_ENEMY_STATS = 20
ST_SIPHON_DEF_ATK = 21
ST_ZERO_SKILL_BOOST = 22
ST_DOUBLE_STRIKE_CENTER = 23
ST_CLEANSE_FORTIFY = 24
ST_SUPPORT_BATTERY_HEAL = 25
ST_ARCANE_PIERCING_SHOT = 26
ST_DRAW_TWO_STRIKE = 27
ST_REVIVE_GRAVEYARD = 28
ST_MANA_STEAL_AOE = 29

# Passive names tracked cleanly via frozenset on CardProto
PASSIVE_KEYS = (
    "Light of Truth", "Granite Armor", "Ice Structure", "Pure Radiance",
    "Flank Strike", "Swift Speed", "Quick Charge", "Fire Blaze", "High Tide",
    "Ocean Protection", "War Fusion", "Nourishing Roots", "Psychic Drain",
    "Black Nightmare", "Tidal Bounce", "Earth Steadfastness", "Moisture Shield",
    "Total Immunity to Fire", "Immunity to Poison and Burn", "Immunity to Poison",
    "Immunity to Dark and Poison", "Freeze Resistance", "Total Immunity to Ice",
    "Frost", "Self-Ignition", "Blazing Fire", "Slow Poison", "Vital Regeneration",
    "Soothing Current", "Holy Aura", "Gushing Waterfall", "Supercharge",
    "Booster Engine", "Tailwind Gust", "Shield of Salvation", "Thermal Explosion",
    "Resurrection Ashes", "Inner Quake", "Scrap Recycling", "Curse of Doom",
    "Immunity to Dark", "Total Immunity to Ghosts and Dark", "Spirit Possession",
    "Electromagnetic Shield", "Night Stealth", "Mana Shield", "Thicket Cover",
    "Mana Recovery", "Arcane Echo", "Arcane Overflow", "Spell Multiplier",
    "Frost Aura", "Spectral Presence", "Dread of the Dead", "Polar Winds",
    "Light Chill", "Aura of Dread", "Water Submersion", "Fierce Gale",
    "Majestic Brightness", "Ice Blade", "Ethereal Body", "Phase Shift",
    "Targeting System", "Light Evasion", "Mystic Intuition", "Nimble Agility",
    "Aerial Pierce", "Water Resilience", "Protective Flame", "Rock Hardness",
    "Steel Armor", "Eternal Flame", "Static Discharge", "Life Leech",
    "Chain Lightning", "Poison Thorns", "Backlash Spark", "Flint Skin",
    "Burning Embers", "Auto-Freeze Attacker", "Mana Steal",
)

ACTIVE_REQUIRES_ENEMY_EXEMPT = (
    "Restore", "Heal", "Nectar", "Sun Judgment", "Shield", "Fortification",
    "Blessing", "Resurrection", "Wrath of Nature", "Summoning", "Reboot",
    "Wall", "Bubble", "Purging Flame", "Energy Surge", "Hastening Breeze",
    "Arcane Time Warp", "Total Eclipse", "Destructive Ion Cannon",
    "Grand Mana Blast", "Grand Thunderstorm", "Hell of Annihilation",
)


def infer_spell_type(active_skill: str, raw_type: int) -> int:
    sk = (active_skill or "").lower()
    if not sk:
        return raw_type if raw_type >= 10 else ST_ARCANE_PIERCING_SHOT
    if "combine left and right" in sk:
        return ST_LINK_WINGS_STRIKE
    if "copy your support" in sk:
        return ST_COPY_SUPPORT_PASSIVE
    if "link both center" in sk:
        return ST_LINK_CENTER_TWIN
    if "elemental synergy" in sk:
        return ST_ELEMENTAL_SYNERGY_SHIELD
    if "swap a wounded flank" in sk:
        return ST_SWAP_WING_CENTER
    if "forcefully pull" in sk:
        return ST_PULL_CENTER_WING
    if "displace an enemy" in sk:
        return ST_PUSH_CENTER_OPEN
    if "teleport your attacker" in sk:
        return ST_TELEPORT_WEAKEST
    if "convert" in sk and "def from your defender" in sk:
        return ST_CONVERT_DEF_ATK
    if "distribute shield energy" in sk:
        return ST_DIST_SHIELD_WINGS
    if "invert the opposite enemy" in sk:
        return ST_INVERT_ENEMY_STATS
    if "siphon" in sk and "def" in sk:
        return ST_SIPHON_DEF_ATK
    if "reduce your monster's active skill cost" in sk:
        return ST_ZERO_SKILL_BOOST
    if "strike twice" in sk:
        return ST_DOUBLE_STRIKE_CENTER
    if "cleanse all curses" in sk:
        return ST_CLEANSE_FORTIFY
    if "overcharge your support" in sk:
        return ST_SUPPORT_BATTERY_HEAL
    if "arcane piercing projectile" in sk:
        return ST_ARCANE_PIERCING_SHOT
    if "draw" in sk and "extra cards" in sk:
        return ST_DRAW_TWO_STRIKE
    if "revive the strongest" in sk:
        return ST_REVIVE_GRAVEYARD
    if "steal" in sk and "mana cap" in sk:
        return ST_MANA_STEAL_AOE
    return raw_type if raw_type >= 10 else ST_ARCANE_PIERCING_SHOT


@dataclass(frozen=True)
class CardProto:
    cid: int
    card_type: str  # "monster", "spell", "env"
    element: str
    role: str
    tier_num: int
    cost: int
    atk: int
    def_stat: int
    passives: Tuple[str, ...]
    passive_tags: frozenset
    actives: Tuple[str, ...]
    active_cost: int
    active_req_enemy: bool
    spell_type: int
    spell_power: int
    summary_text: str


def extract_passive_tags(passives: List[str]) -> frozenset:
    tags = set()
    for p in passives:
        for key in PASSIVE_KEYS:
            if key in p:
                tags.add(key)
        # Note: C# checks "Frost" substring via p.Contains("Frost")
        if "Frost" in p:
            tags.add("Frost")
    return frozenset(tags)


def load_database(json_path: str) -> Tuple[Dict[int, CardProto], List[CardProto], List[CardProto], List[CardProto]]:
    with open(json_path, "r", encoding="utf-8") as f:
        raw = json.load(f)
    raw_cards = raw.get("cards", raw) if isinstance(raw, dict) else raw

    by_id: Dict[int, CardProto] = {}
    monsters: List[CardProto] = []
    spells: List[CardProto] = []
    envs: List[CardProto] = []

    for c in raw_cards:
        cid = int(c["id"])
        ctype = c.get("card_type", "monster")
        elem = c.get("element", "Fire")
        role = c.get("role", "")
        tier_num = int(c.get("tier_num", 1))
        cost = int(c.get("cost", 1))
        atk = int(c.get("atk", 0))
        def_stat = int(c.get("def", 0))

        if ctype == "monster":
            plist = tuple(c.get("passives") or ([] if not c.get("passives_text") else [c["passives_text"]]))
            alist = tuple(c.get("active_skills") or ([] if not c.get("active_skill") else [c["active_skill"]]))
            ptags = extract_passive_tags(list(plist))
            act0 = alist[0] if alist else ""
            act_cost = 3 if "(Cost 3 Power)" in act0 else (2 if "(Cost 2 Power)" in act0 else (1 if act0 else 0))
            req_enemy = bool(act0) and not any(k in act0 for k in ACTIVE_REQUIRES_ENEMY_EXEMPT)
            p_short = plist[0].split(":")[0].strip() if plist else "-"
            a_short = act0.split(":")[0].strip() if act0 else "-"
            summary = f"{p_short} | {a_short}"
            proto = CardProto(
                cid, "monster", elem, role, tier_num, cost, atk, def_stat,
                plist, ptags, alist, act_cost, req_enemy, 0, 0, summary
            )
            by_id[cid] = proto
            monsters.append(proto)
        elif ctype == "spell":
            st = infer_spell_type(c.get("active_skill", ""), int(c.get("spellType", 0)))
            sp = max(10, int(c.get("spellPower", 12)))
            summary = (c.get("active_skill") or c.get("description") or "")[:68]
            proto = CardProto(
                cid, "spell", elem, role, tier_num, cost, 0, 0,
                (), frozenset(), (), 0, False, st, sp, summary
            )
            by_id[cid] = proto
            spells.append(proto)
        else:
            # Environment card (normalize element from name/element)
            env_elem = elem
            if env_elem not in ENV_MATRIX:
                for el in ELEMENTS:
                    if el.lower() in (c.get("name", "") + " " + elem).lower():
                        env_elem = el
                        break
                else:
                    env_elem = ELEMENTS[(cid - 441) % len(ELEMENTS)]
            env_cost = int(c.get("cost", 1))
            proto = CardProto(
                cid, "env", env_elem, "Environment", 3, env_cost, 0, 0,
                (), frozenset(), (), 0, False, 0, 0, f"{env_elem} (+30% Ally / -20% Counter | Draw +1)"
            )
            by_id[cid] = proto
            envs.append(proto)

    # Ensure all 12 environments exist even if JSON trimmed
    if len(envs) < 12:
        existing_elems = {e.element for e in envs}
        for idx, el in enumerate(ELEMENTS, start=1):
            if el not in existing_elems:
                cid = 440 + idx
                proto = CardProto(cid, "env", el, "Environment", 3, 1, 0, 0, (), frozenset(), (), 0, False, 0, 0, f"{el} Environment")
                by_id[cid] = proto
                envs.append(proto)

    return by_id, monsters, spells, envs


# Synthetic token prototypes for Wrath of Nature / Graveyard Summoning
TOKEN_TREANT = CardProto(
    9998, "monster", "Forest", "Defender", 2, 2, 6, 30,
    ("Poison Thorns: Deal 3 damage to attacking monster",),
    frozenset({"Poison Thorns"}), (), 0, False, 0, 0, "Token Treant"
)
TOKEN_GHOST = CardProto(
    9999, "monster", "Ghosts", "Attacker", 2, 2, 12, 14,
    ("Ethereal Body: 35% immune to normal monster strikes",),
    frozenset({"Ethereal Body"}), (), 0, False, 0, 0, "Token Ghost"
)


class SimMonster:
    __slots__ = (
        "proto", "slot", "base_atk", "bonus_atk", "base_def", "max_def", "cur_def",
        "summon_turn", "can_attack", "double_strike", "frozen_turns", "silenced_turns",
        "weakened_turns", "burn_dmg", "poison_dmg", "salvation_used", "bubble_shield",
        "used_active_turn", "has_steadfast", "extra_passives", "damage_dealt", "kills"
    )

    def __init__(self, proto: CardProto, slot: int, turn_num: int):
        self.proto = proto
        self.slot = slot
        self.base_atk = proto.atk
        self.bonus_atk = 0
        self.base_def = proto.def_stat
        self.max_def = proto.def_stat
        self.cur_def = proto.def_stat
        self.summon_turn = turn_num
        self.can_attack = False
        self.double_strike = False
        self.frozen_turns = 0
        self.silenced_turns = 0
        self.weakened_turns = 0
        self.burn_dmg = 0
        self.poison_dmg = 0
        self.salvation_used = False
        self.bubble_shield = False
        self.used_active_turn = False
        self.has_steadfast = "Earth Steadfastness" in proto.passive_tags
        self.extra_passives: Optional[set] = None
        self.damage_dealt = 0
        self.kills = 0

    @property
    def is_alive(self) -> bool:
        return self.cur_def > 0

    def has_p(self, tag: str) -> bool:
        if self.silenced_turns > 0:
            return False
        if tag in self.proto.passive_tags:
            return True
        return self.extra_passives is not None and tag in self.extra_passives

    def has_p_raw(self, tag: str) -> bool:
        # Salvation & some checks match MonsterInstance.HasPassiveSubstr (which respects silence only where checked)
        if tag in self.proto.passive_tags:
            return True
        return self.extra_passives is not None and tag in self.extra_passives

    def add_bonus_def(self, amount: int) -> None:
        if amount <= 0:
            return
        self.max_def += amount
        self.cur_def += amount

    def heal_def(self, amount: int) -> None:
        if amount <= 0:
            return
        self.cur_def = min(self.max_def, self.cur_def + amount)

    def reset_turn(self, turn_num: int) -> None:
        self.used_active_turn = False
        self.double_strike = False
        self.can_attack = (self.frozen_turns <= 0) and (turn_num > self.summon_turn or self.has_p_raw("Quick Charge"))

    def effective_atk(self, active_env: Optional[str]) -> int:
        val = self.base_atk + self.bonus_atk
        if self.weakened_turns > 0:
            val = max(1, val // 2)
        if active_env is not None:
            buffed, debuffed = ENV_MATRIX[active_env]
            if self.proto.element == buffed:
                val = int(round(val * 1.30))
            elif self.proto.element in debuffed:
                val = max(1, int(round(val * 0.80)))
        return max(0, val)


class SimPlayer:
    __slots__ = (
        "name", "hp", "max_hp", "shield", "mana_cap", "power", "sacrificed_turn",
        "deny_turns", "team_immune_turns", "free_skill_bonus",
        "deck", "hand", "graveyard", "slots",
        "drawn_ids", "fallen_deaths", "fallen_turn", "revived_counts",
        "turns_waiting", "non_monster_streak", "monster_streak", "last_elem", "wave_num",
        "played_ids", "drawn_ever_ids", "card_dmg", "card_kills", "comebacks",
        "salvations", "multipliers", "turn_deaths", "turn_clutch", "card_excite", "card_stall"
    )

    def __init__(self, name: str):
        self.name = name
        self.hp = 500
        self.max_hp = 500
        self.shield = 0
        self.mana_cap = 0
        self.power = 0
        self.sacrificed_turn = False
        self.deny_turns = 0
        self.team_immune_turns = 0
        self.free_skill_bonus = 0
        self.deck: List[CardProto] = []
        self.hand: List[CardProto] = []
        self.graveyard: List[CardProto] = []
        self.slots: List[Optional[SimMonster]] = [None] * 6

        self.drawn_ids: set = set()
        self.fallen_deaths: Dict[int, int] = {}
        self.fallen_turn: Dict[int, int] = {}
        self.revived_counts: Dict[int, int] = {}
        self.turns_waiting: Dict[int, int] = {}
        self.non_monster_streak = 0
        self.monster_streak = 0
        self.last_elem = ""
        self.wave_num = 1

        # Telemetry for per-card win rates & pacing (only real catalog IDs <= 452)
        self.played_ids: set = set()
        self.drawn_ever_ids: set = set()
        self.card_dmg: Dict[int, int] = {}
        self.card_kills: Dict[int, int] = {}
        self.comebacks = 0
        self.salvations = 0
        self.multipliers = 0
        self.turn_deaths = 0
        self.turn_clutch = False
        self.card_excite: Dict[int, int] = {}
        self.card_stall: Dict[int, int] = {}

    def take_leader_damage(self, dmg: int) -> None:
        if dmg <= 0 or self.team_immune_turns > 0:
            return
        if self.shield > 0:
            absorbed = min(self.shield, dmg)
            self.shield -= absorbed
            dmg -= absorbed
        if dmg > 0:
            self.hp = max(0, self.hp - dmg)

    def heal_leader(self, amount: int, opp: "SimPlayer") -> None:
        if amount <= 0:
            return
        for s in opp.slots:
            if s is not None and s.is_alive and s.has_p_raw("Spectral Presence"):
                return
        self.hp = min(self.max_hp, self.hp + amount)

    def record_death(self, cid: int, turn_num: int) -> None:
        if cid > 452:
            return
        self.fallen_deaths[cid] = self.fallen_deaths.get(cid, 0) + 1
        self.fallen_turn[cid] = turn_num

    def fallen_comeback_weight(self, cid: int, turn_num: int) -> float:
        deaths = self.fallen_deaths.get(cid, 0)
        if deaths <= 0:
            return 0.0
        if turn_num - self.fallen_turn.get(cid, 0) < 2:
            return 0.03
        rev = self.revived_counts.get(cid, 0)
        return max(0.25, (0.95 + min(deaths, 3) * 0.20) / (1.0 + rev * 0.85))


class BattleEngine:
    """Self-contained simulation engine runnable both in GUI sample inspector and worker processes."""

    def __init__(
        self,
        by_id: Dict[int, CardProto],
        monsters: List[CardProto],
        spells: List[CardProto],
        envs: List[CardProto],
        endless_waves: bool = True,
        deck_size: int = 50,
    ):
        self.by_id = by_id
        self.monsters = monsters
        self.early_m = [m for m in monsters if m.cost <= 3]
        self.mid_m = [m for m in monsters if 4 <= m.cost <= 5]
        self.late_m = [m for m in monsters if m.cost >= 6]
        self.spells = spells
        self.envs = envs
        self.endless_waves = endless_waves
        self.deck_size = deck_size

    def _pick_uniform_unique(self, pool: List[CardProto], target: int, used: set, out: List[CardProto], rng: random.Random) -> int:
        if not pool or target <= 0:
            return 0
        avail = [c for c in pool if c.cid not in used]
        if not avail:
            return 0
        if len(avail) <= target:
            rng.shuffle(avail)
            for c in avail:
                used.add(c.cid)
                out.append(c)
            return len(avail)
        chosen = rng.sample(avail, target)
        for c in chosen:
            used.add(c.cid)
            out.append(c)
        return target

    def _pick_weighted_unique(self, pool: List[CardProto], target: int, used: set, out: List[CardProto], weight_fn, rng: random.Random) -> int:
        if not pool or target <= 0:
            return 0
        cands = [c for c in pool if c.cid not in used]
        added = 0
        while added < target and cands:
            weights = [max(0.01, weight_fn(c.cid)) for c in cands]
            total = sum(weights)
            roll = rng.random() * total
            acc = 0.0
            pick_idx = len(cands) - 1
            for i, w in enumerate(weights):
                acc += w
                if roll <= acc:
                    pick_idx = i
                    break
            ch = cands.pop(pick_idx)
            if ch.cid not in used:
                used.add(ch.cid)
                out.append(ch)
                added += 1
        return added

    def generate_deck(self, count: int, exclude_ids: Optional[set], rng: random.Random, weight_fn=None) -> List[CardProto]:
        used = set(exclude_ids) if exclude_ids else set()
        deck: List[CardProto] = []
        m_target = int(round(count * 0.60))
        s_target = int(round(count * 0.34))
        e_target = max(0, count - m_target - s_target)

        early_t = int(round(m_target * 0.35))
        mid_t = int(round(m_target * 0.40))
        late_t = max(0, m_target - early_t - mid_t)

        picker = self._pick_weighted_unique if weight_fn else self._pick_uniform_unique
        kwargs = {"weight_fn": weight_fn} if weight_fn else {}

        pm = 0
        if weight_fn:
            pm += self._pick_weighted_unique(self.early_m, early_t, used, deck, weight_fn, rng)
            pm += self._pick_weighted_unique(self.mid_m, mid_t, used, deck, weight_fn, rng)
            pm += self._pick_weighted_unique(self.late_m, late_t, used, deck, weight_fn, rng)
            if pm < m_target:
                self._pick_weighted_unique(self.monsters, m_target - pm, used, deck, weight_fn, rng)
            self._pick_weighted_unique(self.spells, s_target, used, deck, weight_fn, rng)
            self._pick_weighted_unique(self.envs, e_target, used, deck, weight_fn, rng)
        else:
            pm += self._pick_uniform_unique(self.early_m, early_t, used, deck, rng)
            pm += self._pick_uniform_unique(self.mid_m, mid_t, used, deck, rng)
            pm += self._pick_uniform_unique(self.late_m, late_t, used, deck, rng)
            if pm < m_target:
                self._pick_uniform_unique(self.monsters, m_target - pm, used, deck, rng)
            self._pick_uniform_unique(self.spells, s_target, used, deck, rng)
            self._pick_uniform_unique(self.envs, e_target, used, deck, rng)

        if len(deck) < count:
            self._pick_uniform_unique(self.monsters, count - len(deck), used, deck, rng)
        if len(deck) < count:
            self._pick_uniform_unique(self.spells, count - len(deck), used, deck, rng)

        rng.shuffle(deck)
        return deck

    def refill_endless_wave(self, player: SimPlayer, turn_num: int, rng: random.Random) -> List[CardProto]:
        player.wave_num += 1
        active_ids = {c.cid for c in player.hand}
        for s in player.slots:
            if s is not None and s.is_alive:
                active_ids.add(s.proto.cid)

        wave_used = set(active_ids)
        wave_cards: List[CardProto] = []

        # ~20% Veteran Fallen Monsters Comeback
        m_quota = int(round(self.deck_size * 0.60))
        max_fallen = max(1, int(round(m_quota * 0.22)))
        fallen_pool = [
            m for m in self.monsters
            if m.cid not in wave_used and player.fallen_deaths.get(m.cid, 0) > 0
        ]
        if fallen_pool:
            target_cb = min(max_fallen, len(fallen_pool))
            self._pick_weighted_unique(
                fallen_pool,
                target_cb,
                wave_used,
                wave_cards,
                lambda cid: player.fallen_comeback_weight(cid, turn_num) * rng.uniform(0.55, 1.65),
                rng,
            )

        remaining = max(0, self.deck_size - len(wave_cards))
        excl = player.drawn_ids | wave_used
        fresh = self.generate_deck(remaining, excl, rng)
        if len(fresh) < remaining:
            player.drawn_ids.clear()
            for c in wave_cards:
                player.drawn_ids.add(c.cid)
            cycle_excl = wave_used | {c.cid for c in fresh}
            fresh.extend(self.generate_deck(remaining - len(fresh), cycle_excl, rng))

        wave_cards.extend(fresh)
        rng.shuffle(wave_cards)
        return wave_cards

    def draw_card(self, player: SimPlayer, turn_num: int, rng: random.Random, log: Optional[List[str]] = None) -> bool:
        if not player.deck:
            if self.endless_waves:
                refill = self.refill_endless_wave(player, turn_num, rng)
                for c in refill:
                    player.deck.append(c)
                    player.turns_waiting[c.cid] = player.turns_waiting.get(c.cid, 0)
                if log is not None:
                    log.append(f"  [موجة #{player.wave_num}] {player.name} أعاد تعبئة +{len(refill)} كارت لا نهائي")
            else:
                return False

        if not player.deck:
            return False

        # Weighted Stochastic Roulette Selection matching PlayerState.SelectWeightedNextCardIndex
        n = len(player.deck)
        if n == 1:
            idx = 0
        else:
            is_open = len(player.hand) < 6
            open_m = sum(1 for h in player.hand if h.card_type == "monster") if is_open else 0
            open_low = sum(1 for h in player.hand if h.cost <= 3) if is_open else 0

            weights = [0.0] * n
            total = 0.0
            for i, c in enumerate(player.deck):
                waited = player.turns_waiting.get(c.cid, 0)
                w_wait = 1.0 + waited * 0.45
                w_pat = 1.0
                if is_open:
                    if open_m < 3 and c.card_type == "monster":
                        w_pat *= 3.2
                    if open_low < 2 and c.cost <= 3:
                        w_pat *= 3.0
                else:
                    if c.card_type == "monster" and player.non_monster_streak >= 2:
                        w_pat *= 3.5 + (player.non_monster_streak - 2) * 2.5
                    elif c.card_type != "monster" and player.monster_streak >= 4:
                        w_pat *= 1.8
                    if player.last_elem and c.element == player.last_elem:
                        w_pat *= 0.55
                    if c.card_type == "monster" and c.tier_num >= 4 and player.mana_cap >= 5:
                        w_pat *= 1.0 + min(0.75, (player.mana_cap - 4) * 0.12)

                if c.cid not in player.drawn_ids:
                    w_state = 2.8 + min(3.0, turn_num * 0.06)
                elif c.card_type == "monster" and player.fallen_deaths.get(c.cid, 0) > 0:
                    w_state = 0.65 * player.fallen_comeback_weight(c.cid, turn_num)
                else:
                    w_state = 0.08

                w_val = max(0.01, w_wait * w_pat * w_state * rng.uniform(0.55, 1.65))
                weights[i] = w_val
                total += w_val

            roll = rng.random() * total
            acc = 0.0
            idx = n - 1
            for i, w in enumerate(weights):
                acc += w
                if roll <= acc:
                    idx = i
                    break

        card = player.deck.pop(idx)
        player.turns_waiting.pop(card.cid, None)
        for rem in player.deck:
            player.turns_waiting[rem.cid] = player.turns_waiting.get(rem.cid, 0) + 1

        was_fallen = (
            card.card_type == "monster"
            and card.cid in player.drawn_ids
            and player.fallen_deaths.get(card.cid, 0) > 0
        )
        if was_fallen:
            player.revived_counts[card.cid] = player.revived_counts.get(card.cid, 0) + 1
            player.comebacks += 1

        player.drawn_ids.add(card.cid)
        if card.cid <= 452:
            player.drawn_ever_ids.add(card.cid)
        player.last_elem = card.element
        if card.card_type == "monster":
            player.non_monster_streak = 0
            player.monster_streak += 1
        else:
            player.non_monster_streak += 1
            player.monster_streak = 0

        if len(player.hand) < 10:
            player.hand.append(card)
            if log is not None:
                tag = " [عودة ملحمية!]" if was_fallen else ""
                elem_lbl = ELEM_AR.get(card.element, card.element)
                type_lbl = TYPE_AR.get(card.card_type, card.card_type)
                log.append(f"  + {player.name} سحب #{card.cid} ({elem_lbl} - {type_lbl}، تكلفة {card.cost}){tag}")
            return True
        else:
            player.graveyard.append(card)
            return False

    def try_salvation(self, owner: SimPlayer, slot: int, m: SimMonster) -> bool:
        if not m.salvation_used and m.has_p_raw("Shield of Salvation"):
            m.salvation_used = True
            m.cur_def = 10
            owner.salvations += 1
            owner.turn_clutch = True
            return True
        for adj in (slot - 1, slot + 1):
            if 0 <= adj < 6:
                ally = owner.slots[adj]
                if ally is not None and not ally.salvation_used and ally.has_p_raw("Shield of Salvation"):
                    ally.salvation_used = True
                    m.cur_def = 5
                    owner.salvations += 1
                    owner.turn_clutch = True
                    return True
        return False

    def handle_death(
        self,
        owner: SimPlayer,
        killer_owner: SimPlayer,
        slot: int,
        dead_m: SimMonster,
        killer_m: Optional[SimMonster],
        turn_num: int,
    ) -> bool:
        if self.try_salvation(owner, slot, dead_m):
            return False

        owner.turn_deaths += 1

        if 0 <= slot < 6 and owner.slots[slot] is dead_m:
            owner.slots[slot] = None

        if dead_m.proto.cid <= 452:
            owner.graveyard.append(dead_m.proto)
            owner.record_death(dead_m.proto.cid, turn_num)

        if killer_m is not None and killer_m.proto.cid <= 452:
            killer_owner.card_kills[killer_m.proto.cid] = killer_owner.card_kills.get(killer_m.proto.cid, 0) + 1

        if dead_m.silenced_turns <= 0:
            pt = dead_m.proto.passive_tags
            ep = dead_m.extra_passives
            has_t = lambda k: (k in pt) or (ep is not None and k in ep)
            if has_t("Thermal Explosion"):
                opp = killer_owner.slots[slot] if 0 <= slot < 6 else None
                if opp is not None:
                    opp.cur_def -= 10
                    if not opp.is_alive and not self.try_salvation(killer_owner, slot, opp):
                        if opp.proto.cid <= 452:
                            killer_owner.graveyard.append(opp.proto)
                            killer_owner.record_death(opp.proto.cid, turn_num)
                        killer_owner.slots[slot] = None
                else:
                    killer_owner.take_leader_damage(10)
            if has_t("Resurrection Ashes"):
                owner.heal_leader(20, killer_owner)
            if has_t("Inner Quake"):
                killer_owner.take_leader_damage(10)
            if has_t("Scrap Recycling"):
                owner.power += 2
                owner.mana_cap += 1
            if has_t("Curse of Doom") and killer_m is not None and killer_m.is_alive:
                if not killer_m.has_p_raw("Immunity to Dark") and not killer_m.has_p_raw("Total Immunity to Ghosts and Dark"):
                    killer_m.cur_def = 0
                    for ks in range(6):
                        if killer_owner.slots[ks] is killer_m:
                            if not self.try_salvation(killer_owner, ks, killer_m):
                                if killer_m.proto.cid <= 452:
                                    killer_owner.graveyard.append(killer_m.proto)
                                    killer_owner.record_death(killer_m.proto.cid, turn_num)
                                killer_owner.slots[ks] = None
                            break
            if has_t("Spirit Possession") and 0 <= slot < 6 and killer_owner.slots[slot] is not None:
                t = killer_owner.slots[slot]
                t.frozen_turns = max(t.frozen_turns, 2)
                t.silenced_turns = max(t.silenced_turns, 2)
                t.can_attack = False
        return True

    def deal_spell_skill_dmg(
        self,
        slot: int,
        target_m: SimMonster,
        dmg: int,
        src_m: Optional[SimMonster],
        opponent: SimPlayer,
        player: SimPlayer,
        turn_num: int,
        ignore_immunity: bool = False,
        spell_cid: int = 0,
    ) -> None:
        if target_m is None or not target_m.is_alive:
            return
        if target_m.bubble_shield and not ignore_immunity:
            target_m.bubble_shield = False
            return
        if not ignore_immunity:
            if target_m.has_p("Electromagnetic Shield") or target_m.has_p("Night Stealth"):
                return
            if target_m.has_p("Mana Shield"):
                dmg = max(0, dmg - 6)
            for s in opponent.slots:
                if s is not None and s.has_p("Thicket Cover"):
                    dmg -= int(round(dmg * 0.25))
                    break

        applied = max(0, dmg)
        target_m.cur_def -= applied
        if applied > 0:
            if src_m is not None and src_m.proto.cid <= 452:
                player.card_dmg[src_m.proto.cid] = player.card_dmg.get(src_m.proto.cid, 0) + applied
            elif spell_cid > 0:
                player.card_dmg[spell_cid] = player.card_dmg.get(spell_cid, 0) + applied
        if not target_m.is_alive:
            self.handle_death(opponent, player, slot, target_m, src_m, turn_num)

    def summon_monster(self, player: SimPlayer, opp: SimPlayer, card: CardProto, slot: int, turn_num: int) -> bool:
        if player.slots[slot] is not None or card.cost > player.power:
            return False
        player.power -= card.cost
        player.hand.remove(card)
        if card.cid <= 452:
            player.played_ids.add(card.cid)

        m = SimMonster(card, slot, turn_num)
        player.slots[slot] = m

        # Apply On-Summon Passives
        opp_m = opp.slots[slot]
        if opp_m is not None and opp_m.has_p("Light of Truth"):
            m.silenced_turns = max(m.silenced_turns, 2)
            return True

        pt = card.passive_tags
        if "Light of Truth" in pt and opp_m is not None:
            opp_m.silenced_turns = max(opp_m.silenced_turns, 3)
        if "Granite Armor" in pt and slot in CENTER_SLOTS:
            m.add_bonus_def(m.base_def)
        if "Ice Structure" in pt and slot in CENTER_SLOTS:
            m.add_bonus_def(5)
        if "Pure Radiance" in pt and slot in CENTER_SLOTS:
            m.bonus_atk += 3
        if "Flank Strike" in pt and slot in FLANK_SLOTS:
            m.bonus_atk += 4
        if "Swift Speed" in pt and slot in FLANK_SLOTS:
            m.bonus_atk += 5
        if "Quick Charge" in pt:
            m.bonus_atk += 4
            m.can_attack = True
        if "Fire Blaze" in pt:
            boosted = 0
            for adj in (slot - 1, slot + 1):
                if 0 <= adj < 6 and player.slots[adj] is not None:
                    player.slots[adj].bonus_atk += 3
                    boosted += 1
            if boosted == 0:
                m.bonus_atk += 3
        if "High Tide" in pt:
            for s in player.slots:
                if s is not None and s.proto.element == "Water":
                    s.add_bonus_def(4)
        if "Ocean Protection" in pt:
            best_t = None
            min_d = 10**9
            for i in range(6):
                si = player.slots[i]
                if i != slot and si is not None and si.cur_def < min_d:
                    min_d = si.cur_def
                    best_t = si
            if best_t is not None:
                best_t.add_bonus_def(15)
            else:
                m.add_bonus_def(10)
        if "War Fusion" in pt:
            mechas = sum(1 for s in player.slots if s is not None and s.proto.element == "Mecha")
            m.add_bonus_def(max(1, mechas) * 8)
        if "Nourishing Roots" in pt:
            player.mana_cap += 1
            player.power += 1
        if "Psychic Drain" in pt:
            drained = min(2, max(0, opp.mana_cap - 1))
            opp.mana_cap -= drained
        if "Black Nightmare" in pt:
            if opp.hand:
                sac = opp.hand.pop()
                opp.graveyard.append(sac)
            else:
                opp.take_leader_damage(12)
        if "Tidal Bounce" in pt and opp_m is not None and (opp_m.base_atk + opp_m.bonus_atk) < 14:
            if not opp_m.has_steadfast:
                if len(opp.hand) < 10:
                    opp.hand.append(opp_m.proto)
                opp.slots[slot] = None
        return True

    def start_of_turn_passives(self, player: SimPlayer, opp: SimPlayer, turn_num: int) -> None:
        for slot in range(6):
            m = player.slots[slot]
            if m is None:
                continue
            if m.burn_dmg > 0:
                if m.has_p_raw("Moisture Shield") or m.has_p_raw("Total Immunity to Fire") or m.has_p_raw("Immunity to Poison and Burn"):
                    m.burn_dmg = 0
                else:
                    m.cur_def -= m.burn_dmg
            if m.poison_dmg > 0:
                if m.has_p_raw("Immunity to Poison") or m.has_p_raw("Immunity to Dark and Poison"):
                    m.poison_dmg = 0
                else:
                    m.cur_def -= m.poison_dmg
            if not m.is_alive:
                if not self.try_salvation(player, slot, m):
                    if m.proto.cid <= 452:
                        player.graveyard.append(m.proto)
                        player.record_death(m.proto.cid, turn_num)
                    player.slots[slot] = None
                    continue

            if m.frozen_turns > 0:
                if m.has_p_raw("Freeze Resistance") or m.has_p_raw("Total Immunity to Ice") or m.has_p_raw("Frost"):
                    m.frozen_turns = 0
                else:
                    m.frozen_turns -= 1
                    m.can_attack = False

            if m.silenced_turns > 0:
                m.silenced_turns -= 1
            if m.weakened_turns > 0:
                m.weakened_turns -= 1

            opp_m = opp.slots[slot]
            if m.silenced_turns <= 0:
                if m.has_p("Self-Ignition") and opp_m is not None:
                    opp_m.burn_dmg = max(opp_m.burn_dmg, 3)
                    opp_m.cur_def -= 3
                if m.has_p("Blazing Fire") and opp_m is not None:
                    opp_m.burn_dmg = max(opp_m.burn_dmg, 8)
                    opp_m.cur_def -= 8
                if m.has_p("Slow Poison") and opp_m is not None:
                    opp_m.poison_dmg = max(opp_m.poison_dmg, 6)
                    opp_m.cur_def -= 6
                if m.has_p("Vital Regeneration"):
                    m.heal_def(4)
                if m.has_p("Soothing Current"):
                    player.heal_leader(5, opp)
                if m.has_p("Holy Aura"):
                    player.heal_leader(4, opp)
                if m.has_p("Gushing Waterfall"):
                    for s in player.slots:
                        if s is not None:
                            s.burn_dmg = s.poison_dmg = s.frozen_turns = s.weakened_turns = 0
                if m.has_p("Supercharge"):
                    player.power += 1
                if m.has_p("Booster Engine"):
                    m.bonus_atk += 2
                if m.has_p("Tailwind Gust"):
                    for ws in FLANK_SLOTS:
                        if player.slots[ws] is not None:
                            player.slots[ws].double_strike = True

            if opp_m is not None and not opp_m.is_alive:
                self.handle_death(opp, player, slot, opp_m, m, turn_num)

    def _get_enemy_target(self, pref: int, enemies: List[SimMonster], opp: SimPlayer, max_atk: bool = False) -> Optional[SimMonster]:
        if not enemies:
            return None
        if 0 <= pref < 6 and opp.slots[pref] is not None:
            return opp.slots[pref]
        if max_atk:
            return max(enemies, key=lambda e: e.base_atk + e.bonus_atk)
        return enemies[0]

    def resolve_active_skill(self, act: str, slot: int, m: SimMonster, player: SimPlayer, opp: SimPlayer, bonus: int, turn_num: int, rng: random.Random) -> None:
        enemies = [s for s in opp.slots if s is not None and s.is_alive]
        allies = [s for s in player.slots if s is not None and s.is_alive]

        if "Hell of Annihilation" in act:
            dmg = 25 + bonus
            if enemies:
                for es in list(enemies):
                    self.deal_spell_skill_dmg(es.slot, es, dmg, m, opp, player, turn_num)
            else:
                opp.take_leader_damage(dmg)
                player.card_dmg[m.proto.cid] = player.card_dmg.get(m.proto.cid, 0) + dmg
        elif "Volcanic Eruption" in act:
            t = self._get_enemy_target(slot, enemies, opp)
            if t is not None:
                if t.cur_def < 30 + bonus:
                    t.cur_def = 0
                    self.handle_death(opp, player, t.slot, t, m, turn_num)
                else:
                    self.deal_spell_skill_dmg(t.slot, t, 20 + bonus, m, opp, player, turn_num)
        elif "Fireball" in act:
            t = self._get_enemy_target(slot, enemies, opp, True)
            if t:
                self.deal_spell_skill_dmg(t.slot, t, 12 + bonus, m, opp, player, turn_num)
        elif "Ember Blast" in act:
            t = self._get_enemy_target(slot, enemies, opp)
            if t:
                idx = t.slot
                self.deal_spell_skill_dmg(idx, t, 8 + bonus, m, opp, player, turn_num)
                for adj in (idx - 1, idx + 1):
                    if 0 <= adj < 6 and opp.slots[adj] is not None:
                        self.deal_spell_skill_dmg(adj, opp.slots[adj], 8, m, opp, player, turn_num)
        elif "Lava Torrent" in act:
            t = self._get_enemy_target(slot, enemies, opp)
            if t:
                self.deal_spell_skill_dmg(t.slot, t, 15 + bonus, m, opp, player, turn_num)
        elif "Purging Flame" in act:
            ally = m
            for al in allies:
                if al.burn_dmg or al.poison_dmg or al.frozen_turns or al.weakened_turns:
                    ally = al
                    break
            ally.burn_dmg = ally.poison_dmg = ally.frozen_turns = ally.weakened_turns = ally.silenced_turns = 0
            ally.bonus_atk += 5 + bonus
        elif "Sweeping Tsunami" in act:
            for es in list(enemies):
                es.bonus_atk = 0
                self.deal_spell_skill_dmg(es.slot, es, 20 + bonus, m, opp, player, turn_num)
        elif "Abyssal Submersion" in act:
            fc = 0
            for v in CENTER_SLOTS:
                if opp.slots[v] is not None:
                    opp.slots[v].frozen_turns = max(opp.slots[v].frozen_turns, 1)
                    opp.slots[v].can_attack = False
                    fc += 1
            if fc == 0:
                for es in enemies[:2]:
                    es.frozen_turns = max(es.frozen_turns, 1)
                    es.can_attack = False
        elif "Water Vortex" in act:
            t = self._get_enemy_target(slot, enemies, opp)
            if t:
                t.can_attack = False
                if not t.has_steadfast:
                    for adj in (t.slot - 1, t.slot + 1):
                        if 0 <= adj < 6 and opp.slots[adj] is None:
                            opp.slots[t.slot] = None
                            opp.slots[adj] = t
                            t.slot = adj
                            break
        elif "Healing Stream" in act:
            tgt = min(allies, key=lambda a: a.cur_def) if allies else m
            tgt.heal_def(20 + bonus)
        elif "Safety Bubble" in act:
            tgt = next((a for a in allies if not a.bubble_shield), m)
            tgt.bubble_shield = True
            tgt.add_bonus_def(10)
        elif "Ice Age" in act:
            for es in enemies:
                es.frozen_turns = max(es.frozen_turns, 1)
                es.can_attack = False
        elif "Frost Shatter" in act:
            fslot = next((es for es in enemies if es.frozen_turns > 0), None)
            if fslot:
                fslot.cur_def = 0
                self.handle_death(opp, player, fslot.slot, fslot, m, turn_num)
            else:
                t = self._get_enemy_target(slot, enemies, opp)
                if t:
                    t.frozen_turns = max(t.frozen_turns, 1)
                    t.can_attack = False
                    self.deal_spell_skill_dmg(t.slot, t, 18 + bonus, m, opp, player, turn_num)
        elif "Ice Spear" in act:
            t = self._get_enemy_target(slot, enemies, opp, True)
            if t:
                t.frozen_turns = max(t.frozen_turns, 1)
                t.can_attack = False
                self.deal_spell_skill_dmg(t.slot, t, 10 + bonus, m, opp, player, turn_num)
        elif "Snow Wall" in act:
            tgt = min(allies, key=lambda a: a.cur_def) if allies else m
            tgt.add_bonus_def(20 + bonus)
        elif "Grand Thunderstorm" in act:
            per_bolt = 10 + bonus // 2
            for _ in range(4):
                alive = [s for s in opp.slots if s is not None and s.is_alive]
                if alive:
                    pk = rng.choice(alive)
                    self.deal_spell_skill_dmg(pk.slot, pk, per_bolt, m, opp, player, turn_num)
                else:
                    opp.take_leader_damage(per_bolt)
                    player.card_dmg[m.proto.cid] = player.card_dmg.get(m.proto.cid, 0) + per_bolt
        elif "Total Neural Paralysis" in act:
            for es in enemies:
                es.silenced_turns = max(es.silenced_turns, 2)
        elif "Sudden Shock" in act:
            t = self._get_enemy_target(slot, enemies, opp)
            if t:
                self.deal_spell_skill_dmg(t.slot, t, 14 + bonus, m, opp, player, turn_num)
        elif "Energy Surge" in act:
            tgt = max(allies, key=lambda a: a.base_atk + a.bonus_atk) if allies else m
            tgt.bonus_atk += 8 + bonus
        elif "Sweeping Cyclone" in act:
            if enemies:
                best = max(enemies, key=lambda e: e.proto.cost)
                if not best.has_steadfast:
                    if len(opp.hand) < 10 and best.proto.cid <= 452:
                        opp.hand.append(best.proto)
                    opp.slots[best.slot] = None
                else:
                    self.deal_spell_skill_dmg(best.slot, best, 14 + bonus, m, opp, player, turn_num)
        elif "Winds of Annihilation" in act:
            for es in list(enemies):
                self.deal_spell_skill_dmg(es.slot, es, 22 + bonus, m, opp, player, turn_num, ignore_immunity=True)
        elif "Cutting Gale" in act:
            t = self._get_enemy_target(slot, enemies, opp)
            if t:
                self.deal_spell_skill_dmg(t.slot, t, 12 + bonus, m, opp, player, turn_num)
                rem = [s for s in opp.slots if s is not None and s.is_alive]
                if len(rem) >= 2 and not rem[0].has_steadfast and not rem[-1].has_steadfast:
                    s1, s2 = rem[0].slot, rem[-1].slot
                    m1, m2 = rem[0], rem[-1]
                    opp.slots[s1], opp.slots[s2] = m2, m1
                    m2.slot, m1.slot = s1, s2
        elif "Hastening Breeze" in act:
            tgt = m if not m.can_attack else (max(allies, key=lambda a: a.base_atk + a.bonus_atk) if allies else m)
            tgt.can_attack = True
            tgt.bonus_atk += 5 + bonus
        elif "Great Earth Fissure" in act:
            t = self._get_enemy_target(slot, enemies, opp)
            if t is None or t.slot not in CENTER_SLOTS:
                for v in CENTER_SLOTS:
                    if opp.slots[v] is not None:
                        t = opp.slots[v]
                        break
            if t:
                t.cur_def = 0
                self.handle_death(opp, player, t.slot, t, m, turn_num)
        elif "Immortal Mountain Shield" in act or "Iron Dome Shield" in act:
            player.team_immune_turns = max(player.team_immune_turns, 1)
        elif "Earthquake Strike" in act:
            t = self._get_enemy_target(slot, enemies, opp)
            if t:
                idx = t.slot
                self.deal_spell_skill_dmg(idx, t, 15 + bonus, m, opp, player, turn_num)
                for adj in (idx - 1, idx + 1):
                    if 0 <= adj < 6 and opp.slots[adj] is not None:
                        self.deal_spell_skill_dmg(adj, opp.slots[adj], 5, m, opp, player, turn_num)
        elif "Stone Fortification" in act:
            tgt = min(allies, key=lambda a: a.cur_def) if allies else m
            tgt.add_bonus_def(15 + bonus)
            tgt.has_steadfast = True
        elif "Wrath of Nature" in act:
            for ws in FLANK_SLOTS:
                if player.slots[ws] is None:
                    player.slots[ws] = SimMonster(TOKEN_TREANT, ws, turn_num)
                    break
        elif "Organic Blight Poison" in act:
            for es in list(enemies):
                es.poison_dmg = max(es.poison_dmg, 10)
                self.deal_spell_skill_dmg(es.slot, es, 10 + bonus, m, opp, player, turn_num)
        elif "Strangling Roots" in act:
            t = self._get_enemy_target(slot, enemies, opp, True)
            if t:
                t.frozen_turns = max(t.frozen_turns, 2)
                t.can_attack = False
        elif "Nectar of Life" in act:
            player.heal_leader(25 + bonus, opp)
            m.heal_def(10)
        elif "Grand Light Resurrection" in act:
            fallen = [g for g in player.graveyard if g.card_type == "monster"]
            emp = next((i for i in range(6) if player.slots[i] is None), -1)
            if fallen and emp != -1:
                best = max(fallen, key=lambda g: g.cost)
                player.graveyard.remove(best)
                rm = SimMonster(best, emp, turn_num)
                rm.can_attack = True
                player.slots[emp] = rm
            else:
                player.heal_leader(40 + bonus, opp)
        elif "Absolute Sun Judgment" in act:
            player.heal_leader(80 + bonus, opp)
            for al in allies:
                al.add_bonus_def(15)
        elif "Dawn Ray" in act:
            t = self._get_enemy_target(slot, enemies, opp)
            if t:
                self.deal_spell_skill_dmg(t.slot, t, 14 + bonus, m, opp, player, turn_num, ignore_immunity=True)
        elif "Blessing of Light" in act:
            tgt = max(allies, key=lambda a: a.base_atk + a.bonus_atk) if allies else m
            tgt.bonus_atk += max(8, tgt.base_atk) + bonus
        elif "Total Eclipse" in act:
            opp.deny_turns = max(opp.deny_turns, 1)
        elif "Soul Reap" in act:
            for es in sorted(enemies, key=lambda e: e.cur_def)[:2]:
                es.cur_def = 0
                self.handle_death(opp, player, es.slot, es, m, turn_num)
        elif "Shadow Strike" in act:
            t = self._get_enemy_target(slot, enemies, opp)
            if t:
                player.heal_leader(5, opp)
                self.deal_spell_skill_dmg(t.slot, t, 14 + bonus, m, opp, player, turn_num)
        elif "Soul Lock" in act:
            t = self._get_enemy_target(slot, enemies, opp, True)
            if t:
                t.silenced_turns = max(t.silenced_turns, 2)
        elif "Destructive Ion Cannon" in act:
            cdmg = 50 + bonus
            t = self._get_enemy_target(slot, enemies, opp)
            if t:
                rem = max(0, cdmg - t.cur_def)
                self.deal_spell_skill_dmg(t.slot, t, cdmg, m, opp, player, turn_num, ignore_immunity=True)
                if rem > 0:
                    opp.take_leader_damage(rem)
            else:
                opp.take_leader_damage(cdmg)
                player.card_dmg[m.proto.cid] = player.card_dmg.get(m.proto.cid, 0) + cdmg
        elif "Guided Missile" in act:
            t = self._get_enemy_target(slot, enemies, opp, True)
            if t:
                self.deal_spell_skill_dmg(t.slot, t, 15 + bonus, m, opp, player, turn_num)
        elif "System Reboot" in act:
            m.burn_dmg = m.poison_dmg = m.frozen_turns = m.silenced_turns = m.weakened_turns = 0
            m.heal_def(20 + bonus)
        elif "Graveyard Summoning" in act:
            for ws in FLANK_SLOTS:
                if player.slots[ws] is None:
                    tg = SimMonster(TOKEN_GHOST, ws, turn_num)
                    tg.can_attack = True
                    player.slots[ws] = tg
                    break
        elif "Soul Drain" in act:
            for es in enemies:
                es.base_atk = max(1, es.base_atk // 2)
                es.cur_def = max(1, es.cur_def // 2)
                es.weakened_turns = max(es.weakened_turns, 1)
        elif "Touch of the Beyond" in act:
            t = self._get_enemy_target(slot, enemies, opp)
            if t:
                self.deal_spell_skill_dmg(t.slot, t, 10 + bonus, m, opp, player, turn_num, ignore_immunity=True)
        elif "Spectral Terror" in act:
            t = self._get_enemy_target(slot, enemies, opp, True)
            if t:
                t.frozen_turns = max(t.frozen_turns, 1)
                t.can_attack = False
        elif "Grand Mana Blast" in act:
            for es in list(enemies):
                self.deal_spell_skill_dmg(es.slot, es, 20 + bonus, m, opp, player, turn_num)
            opp.take_leader_damage(15)
            player.card_dmg[m.proto.cid] = player.card_dmg.get(m.proto.cid, 0) + 15
        elif "Arcane Time Warp" in act:
            for al in allies:
                al.can_attack = True
                al.bonus_atk += 5 + bonus
        elif "Arcane Arrow" in act:
            t = self._get_enemy_target(slot, enemies, opp, True)
            if t:
                self.deal_spell_skill_dmg(t.slot, t, 15 + bonus, m, opp, player, turn_num)
        elif "Elemental Shift" in act:
            t = self._get_enemy_target(slot, enemies, opp, True)
            if t:
                t.weakened_turns = max(t.weakened_turns, 2)
        else:
            m.add_bonus_def(15 + bonus)
            m.bonus_atk += 5

    _SYNERGY_PASSIVES = frozenset({
        "Arcane Echo", "Arcane Overflow", "Spell Multiplier",
        "Mana Recovery", "Supercharge", "Nourishing Roots",
    })
    _POST_BUFF_SPELLS = frozenset({
        ST_CLEANSE_FORTIFY, ST_DIST_SHIELD_WINGS, ST_COPY_SUPPORT_PASSIVE,
        ST_LINK_CENTER_TWIN, ST_ELEMENTAL_SYNERGY_SHIELD, ST_SWAP_WING_CENTER,
        ST_TELEPORT_WEAKEST, ST_CONVERT_DEF_ATK, ST_DOUBLE_STRIKE_CENTER,
    })

    def _score_summon_slot(self, player: SimPlayer, opp: SimPlayer, card: CardProto, slot: int) -> float:
        pt = card.passive_tags
        score = 0.0
        # Role baseline preference
        if card.role == "Pure Shield":
            score += 4.0 if slot in CENTER_SLOTS else (1.5 if slot in (0, 5) else 0.0)
        elif card.role == "Attacker":
            score += 4.0 if slot in FLANK_SLOTS else 0.0
        else:
            score += 3.0 if slot in CENTER_SLOTS else 1.5

        # Slot-specific passive bonuses
        if "Granite Armor" in pt and slot in CENTER_SLOTS:
            score += card.def_stat * 0.8 + 18.0
        if "Ice Structure" in pt and slot in CENTER_SLOTS:
            score += 12.0
        if "Pure Radiance" in pt and slot in CENTER_SLOTS:
            score += 10.0
        if ("Flank Strike" in pt or "Swift Speed" in pt) and slot in FLANK_SLOTS:
            score += 14.0
        if "Fire Blaze" in pt:
            for adj in (slot - 1, slot + 1):
                if 0 <= adj < 6 and player.slots[adj] is not None:
                    score += 8.0

        opp_m = opp.slots[slot]
        if opp_m is None:
            # Open lane: favor immediate burst & attackers
            if "Quick Charge" in pt:
                score += 32.0 + card.atk
            elif card.role == "Attacker" or card.atk >= 14:
                score += 11.0
        else:
            opp_atk = opp_m.base_atk + opp_m.bonus_atk
            if "Light of Truth" in pt and opp_m.silenced_turns == 0:
                score += 22.0 + opp_atk + len(opp_m.proto.passive_tags) * 6.0
            if "Tidal Bounce" in pt and opp_atk < 14 and not opp_m.has_steadfast:
                score += 28.0 + opp_m.proto.cost * 3.0
            if (
                card.role == "Pure Shield"
                or "Flint Skin" in pt
                or "Auto-Freeze Attacker" in pt
                or "Poison Thorns" in pt
                or "Burning Embers" in pt
            ):
                score += opp_atk * 1.35
            elif card.def_stat <= opp_atk and "Quick Charge" not in pt:
                score -= 9.0
        return score

    def _choose_best_slot(self, player: SimPlayer, opp: SimPlayer, card: CardProto) -> int:
        best_slot = -1
        best_score = -1e9
        for s in range(6):
            if player.slots[s] is None:
                sc = self._score_summon_slot(player, opp, card, s)
                if sc > best_score:
                    best_score = sc
                    best_slot = s
        return best_slot

    def _should_play_env(self, active: SimPlayer, other: SimPlayer, env_card: CardProto, active_env: Optional[str], env_turns: int) -> bool:
        if active_env == env_card.element and env_turns >= 2:
            return False
        buffed, debuffed = ENV_MATRIX[env_card.element]
        score = 0.0
        for s in active.slots:
            if s is not None:
                if s.proto.element == buffed:
                    score += 2.2
                elif s.proto.element in debuffed:
                    score -= 2.0
        for hc in active.hand:
            if hc.card_type == "monster" and hc.cost <= active.power:
                if hc.element == buffed:
                    score += 1.4
                elif hc.element in debuffed:
                    score -= 1.0
        for es in other.slots:
            if es is not None:
                if es.proto.element in debuffed:
                    score += 1.8
                elif es.proto.element == buffed:
                    score -= 1.8
        return score >= (0.6 if active_env is None else 1.2)

    def _should_cast_spell(self, caster: SimPlayer, opp: SimPlayer, sp: CardProto) -> bool:
        st = sp.spell_type
        if st == ST_SUPPORT_BATTERY_HEAL:
            wounded = any(s is not None and s.cur_def < s.max_def - 4 for s in caster.slots)
            needs_battery = caster.power >= sp.cost and any(
                c is not sp and c.cost > caster.power - sp.cost and c.cost <= caster.power - sp.cost + 2
                for c in caster.hand
            )
            if caster.hp > caster.max_hp * 0.82 and not wounded and not needs_battery:
                return False
        elif st == ST_REVIVE_GRAVEYARD:
            has_dead = any(g.card_type == "monster" for g in caster.graveyard)
            if not has_dead and caster.hp > caster.max_hp * 0.85:
                return False
        elif st in (ST_CLEANSE_FORTIFY, ST_DIST_SHIELD_WINGS, ST_LINK_CENTER_TWIN):
            if not any(s is not None and s.is_alive for s in caster.slots):
                return False
        elif st == ST_COPY_SUPPORT_PASSIVE:
            allies = [s for s in caster.slots if s is not None and s.is_alive]
            if len(allies) < 2 and caster.hp > caster.max_hp * 0.85:
                return False
        elif st in (ST_SWAP_WING_CENTER, ST_DOUBLE_STRIKE_CENTER, ST_CONVERT_DEF_ATK):
            if not any(s is not None and s.is_alive for s in caster.slots):
                return False
        return True

    def _score_active_skill(self, player: SimPlayer, opp: SimPlayer, m: SimMonster, act0: str) -> int:
        # Higher score runs first when mana is shared across multiple monsters
        if opp.hp <= 50 and ("Ion Cannon" in act0 or "Mana Blast" in act0 or "Thermal" in act0):
            return 100
        if "Resurrection" in act0 and any(g.card_type == "monster" for g in player.graveyard):
            return 90
        if ("Mountain Shield" in act0 or "Iron Dome" in act0) and player.hp < 220:
            return 85
        if "Total Eclipse" in act0:
            return 80
        if "Soul Reap" in act0 or "Fissure" in act0 or "Annihilation" in act0 or "Grand Mana Blast" in act0 or "Ion Cannon" in act0:
            return 75
        if "Time Warp" in act0 or "Double" in act0 or "Hastening Breeze" in act0:
            return 70
        if "Absolute Sun" in act0 or "Nectar" in act0:
            return 65 if player.hp < player.max_hp * 0.80 else 25
        return 40

    def execute_active_skills(self, player: SimPlayer, opp: SimPlayer, turn_num: int, rng: random.Random, acted: Optional[List[int]] = None) -> None:
        if player.power <= 0:
            return
        candidates = []
        for slot in range(6):
            m = player.slots[slot]
            if m is None or not m.is_alive or m.silenced_turns > 0 or m.frozen_turns > 0 or m.used_active_turn or not m.proto.actives:
                continue
            candidates.append((self._score_active_skill(player, opp, m, m.proto.actives[0]), slot, m))
        candidates.sort(key=lambda x: x[0], reverse=True)

        for _, slot, m in candidates:
            if not m.is_alive:
                continue
            act0 = m.proto.actives[0]
            opp_m = opp.slots[slot]
            extra_cost = 1 if (opp_m is not None and opp_m.has_p("Frost Aura")) else 0
            discount = 1 if player.free_skill_bonus > 0 else 0
            cost = max(0, m.proto.active_cost + extra_cost - discount)
            if player.power < cost:
                continue
            has_enemies = any(s is not None and s.is_alive for s in opp.slots)
            if m.proto.active_req_enemy and not has_enemies:
                continue
            if ("Wrath of Nature" in act0 or "Graveyard Summoning" in act0) and not any(player.slots[ws] is None for ws in FLANK_SLOTS):
                continue

            m.used_active_turn = True
            player.power -= cost
            bonus = player.free_skill_bonus
            player.free_skill_bonus = 0
            if m.proto.cid <= 452 and acted is not None:
                acted.append(m.proto.cid)
            if m.has_p("Mana Recovery"):
                player.power += 1
            for s in player.slots:
                if s is not None and s.has_p("Arcane Echo"):
                    s.bonus_atk += 3
            reps = 2 if (m.has_p("Spell Multiplier") and rng.random() < 0.50) else 1
            if reps == 2:
                player.multipliers += 1
                player.turn_clutch = True
            for _ in range(reps):
                self.resolve_active_skill(act0, slot, m, player, opp, bonus, turn_num, rng)
            if player.power <= 0:
                break

    def cast_spell(self, caster: SimPlayer, opp: SimPlayer, sp: CardProto, turn_num: int, rng: random.Random) -> None:
        caster.power -= sp.cost
        caster.hand.remove(sp)
        caster.graveyard.append(sp)
        caster.played_ids.add(sp.cid)

        val = max(10, sp.spell_power)
        reps = 1
        for s in caster.slots:
            if s is not None:
                if s.has_p("Arcane Echo"):
                    s.bonus_atk += 3
                if s.has_p("Arcane Overflow"):
                    val += 8
                if s.has_p("Spell Multiplier") and reps == 1 and rng.random() < 0.50:
                    reps = 2
                    caster.multipliers += 1
                    caster.turn_clutch = True
                if s.has_p("Mana Recovery"):
                    caster.power += 1

        for _ in range(reps):
            allies = [s for s in caster.slots if s is not None and s.is_alive]
            enemies = [s for s in opp.slots if s is not None and s.is_alive]
            st = sp.spell_type

            if st == ST_ARCANE_PIERCING_SHOT:
                if enemies:
                    # Snipe enemy blocking strongest ready ally, or highest ATK threat
                    tgt = max(enemies, key=lambda e: (1 if (caster.slots[e.slot] is not None and caster.slots[e.slot].can_attack) else 0, e.base_atk + e.bonus_atk))
                    self.deal_spell_skill_dmg(tgt.slot, tgt, val, None, opp, caster, turn_num, ignore_immunity=True, spell_cid=sp.cid)
                else:
                    opp.take_leader_damage(val)
                    caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + val
            elif st == ST_SUPPORT_BATTERY_HEAL:
                caster.power += 2
                caster.heal_leader(val, opp)
                for al in allies:
                    al.heal_def(12)
            elif st in (ST_CLEANSE_FORTIFY, ST_DIST_SHIELD_WINGS):
                per = max(6, val // max(1, len(allies)))
                for al in allies:
                    al.frozen_turns = al.silenced_turns = al.burn_dmg = al.poison_dmg = al.weakened_turns = 0
                    al.add_bonus_def(per)
            elif st == ST_DRAW_TWO_STRIKE:
                self.draw_card(caster, turn_num, rng)
                self.draw_card(caster, turn_num, rng)
                opp.take_leader_damage(max(6, val // 2))
                caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + max(6, val // 2)
            elif st == ST_MANA_STEAL_AOE:
                if opp.mana_cap > 1:
                    opp.mana_cap = max(1, opp.mana_cap - 1)
                caster.mana_cap += 1
                caster.power += 1
                aoe = max(6, val // 3)
                for es in list(enemies):
                    self.deal_spell_skill_dmg(es.slot, es, aoe, None, opp, caster, turn_num, spell_cid=sp.cid)
            elif st == ST_LINK_WINGS_STRIKE:
                wing_atk = sum(al.base_atk + al.bonus_atk for al in allies if al.slot in FLANK_SLOTS)
                dmg = val + int(round(wing_atk * 0.45))
                if enemies:
                    tgt = max(enemies, key=lambda e: e.base_atk + e.bonus_atk)
                    self.deal_spell_skill_dmg(tgt.slot, tgt, dmg, None, opp, caster, turn_num, spell_cid=sp.cid)
                else:
                    opp.take_leader_damage(dmg)
                    caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + dmg
            elif st == ST_COPY_SUPPORT_PASSIVE:
                if allies:
                    donor = max(allies, key=lambda a: len(a.proto.passive_tags))
                    others = [a for a in allies if a is not donor]
                    rec = max(others, key=lambda a: a.base_atk + a.cur_def) if others else donor
                    if rec.extra_passives is None:
                        rec.extra_passives = set()
                    rec.extra_passives.update(donor.proto.passive_tags)
                    rec.has_steadfast = rec.has_steadfast or donor.has_steadfast
                    rec.add_bonus_def(val)
                else:
                    caster.heal_leader(val, opp)
            elif st in (ST_LINK_CENTER_TWIN, ST_ELEMENTAL_SYNERGY_SHIELD):
                if allies:
                    for al in allies:
                        al.bonus_atk += max(3, val // 4)
                        al.add_bonus_def(max(6, val // 2))
                else:
                    caster.shield += val
            elif st == ST_SWAP_WING_CENTER:
                if len(allies) >= 2:
                    # Smart swap: move high-ATK ready ally to open enemy lane, or protect weakest ally
                    open_lane_ally = max(
                        allies,
                        key=lambda a: (1 if opp.slots[a.slot] is None else 0, a.base_atk + a.bonus_atk),
                    )
                    wounded = min(allies, key=lambda a: a.cur_def)
                    if opp.slots[wounded.slot] is None and opp.slots[open_lane_ally.slot] is not None and open_lane_ally is not wounded:
                        m1, m2 = open_lane_ally, wounded
                    else:
                        m1 = max(allies, key=lambda a: a.cur_def)
                        m2 = wounded if wounded is not m1 else allies[-1]
                    if m1 is not m2:
                        s1, s2 = m1.slot, m2.slot
                        caster.slots[s1], caster.slots[s2] = m2, m1
                        m2.slot, m1.slot = s1, s2
                        m1.add_bonus_def(val)
                        m2.add_bonus_def(val // 2)
                    else:
                        m1.add_bonus_def(val)
                elif len(allies) == 1:
                    allies[0].add_bonus_def(val)
                else:
                    caster.heal_leader(val, opp)
            elif st in (ST_PULL_CENTER_WING, ST_PUSH_CENTER_OPEN):
                if enemies:
                    # Prioritize displacing enemy blocking our strongest ready attacker
                    es = max(
                        enemies,
                        key=lambda e: (
                            1 if (caster.slots[e.slot] is not None and caster.slots[e.slot].can_attack) else 0,
                            0 if e.has_steadfast else 1,
                            e.base_atk + e.bonus_atk,
                        ),
                    )
                    if not es.has_steadfast:
                        for ws in FLANK_SLOTS:
                            if opp.slots[ws] is None:
                                opp.slots[es.slot] = None
                                opp.slots[ws] = es
                                es.slot = ws
                                break
                    self.deal_spell_skill_dmg(es.slot, es, val, None, opp, caster, turn_num, spell_cid=sp.cid)
                else:
                    opp.take_leader_damage(val)
                    caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + val
            elif st == ST_TELEPORT_WEAKEST:
                if allies:
                    am = max(allies, key=lambda a: a.base_atk + a.bonus_atk)
                    am.bonus_atk += max(4, val // 2)
                    am.can_attack = True
                    if enemies:
                        tgt = min(enemies, key=lambda e: e.cur_def)
                        self.deal_spell_skill_dmg(tgt.slot, tgt, val // 2, am, opp, caster, turn_num)
                else:
                    opp.take_leader_damage(val)
                    caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + val
            elif st == ST_CONVERT_DEF_ATK:
                if allies:
                    am = max(allies, key=lambda a: (1 if opp.slots[a.slot] is None else 0, a.cur_def))
                    conv = min(max(0, am.cur_def - 6), val // 2)
                    am.cur_def -= conv
                    am.bonus_atk += val
                    am.can_attack = True
                else:
                    opp.take_leader_damage(val)
                    caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + val
            elif st in (ST_INVERT_ENEMY_STATS, ST_SIPHON_DEF_ATK):
                if enemies:
                    es = max(enemies, key=lambda e: e.cur_def)
                    self.deal_spell_skill_dmg(es.slot, es, val, None, opp, caster, turn_num, spell_cid=sp.cid)
                    if allies:
                        al = max(allies, key=lambda a: a.base_atk + a.bonus_atk)
                        al.bonus_atk += val // 2
                        al.add_bonus_def(val // 3)
                else:
                    opp.take_leader_damage(val)
                    caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + val
            elif st == ST_ZERO_SKILL_BOOST:
                caster.power += 1
                caster.free_skill_bonus += val // 2
                if allies:
                    al = max(allies, key=lambda a: 1 if a.proto.actives else 0)
                    al.bonus_atk += val // 2
                    al.add_bonus_def(val // 2)
            elif st == ST_DOUBLE_STRIKE_CENTER:
                if allies:
                    am = max(
                        allies,
                        key=lambda a: (1 if opp.slots[a.slot] is None else 0, 1 if a.can_attack else 0, a.base_atk + a.bonus_atk),
                    )
                    am.can_attack = True
                    am.double_strike = True
                    am.bonus_atk += val // 2
                else:
                    opp.take_leader_damage(val)
                    caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + val
            elif st == ST_REVIVE_GRAVEYARD:
                fallen = [g for g in caster.graveyard if g.card_type == "monster"]
                if fallen and len(caster.hand) < 10:
                    best = max(fallen, key=lambda g: g.cost)
                    caster.graveyard.remove(best)
                    caster.hand.append(best)
                else:
                    self.draw_card(caster, turn_num, rng)
                caster.heal_leader(val, opp)

    def execute_combat_phase(
        self,
        attacker: SimPlayer,
        defender: SimPlayer,
        active_env: Optional[str],
        turn_num: int,
        rng: random.Random,
    ) -> None:
        for slot_idx in range(6):
            att = attacker.slots[slot_idx]
            if att is None or not att.can_attack or att.frozen_turns > 0:
                continue
            strikes = 2 if att.double_strike else 1
            att.double_strike = False
            att.can_attack = False

            for _ in range(strikes):
                if not att.is_alive or defender.hp <= 0 or attacker.hp <= 0:
                    break
                opp_m = defender.slots[slot_idx]
                atk_power = att.effective_atk(active_env)
                for es in defender.slots:
                    if es is not None:
                        if es.has_p("Dread of the Dead"):
                            atk_power = max(0, atk_power - 3)
                        if slot_idx in FLANK_SLOTS and es.has_p("Polar Winds"):
                            atk_power = max(0, atk_power - 4)
                if opp_m is not None and opp_m.is_alive:
                    if opp_m.has_p("Light Chill"):
                        atk_power = max(0, atk_power - 3)
                    if opp_m.has_p("Aura of Dread"):
                        atk_power = max(0, atk_power - 3)
                    if opp_m.has_p("Water Submersion"):
                        atk_power = max(0, atk_power - 4)
                    if opp_m.has_p("Fierce Gale"):
                        atk_power = int(round(atk_power * 0.75))
                    if opp_m.has_p("Majestic Brightness") and rng.random() < 0.30:
                        atk_power = int(round(atk_power * 0.55))
                    if att.has_p("Ice Blade") and opp_m.frozen_turns > 0:
                        atk_power += 5

                if atk_power <= 0 or defender.team_immune_turns > 0:
                    continue

                if opp_m is not None:
                    if opp_m.bubble_shield:
                        opp_m.bubble_shield = False
                        continue
                    if opp_m.has_p("Ethereal Body"):
                        if att.proto.element not in ("Light", "Arcane", "Ghosts") and not att.has_p("Phase Shift"):
                            atk_power = max(1, int(round(atk_power * 0.65)))
                    if not att.has_p("Targeting System"):
                        if opp_m.has_p("Light Evasion") and rng.random() < 0.20:
                            continue
                        if opp_m.has_p("Mystic Intuition") and rng.random() < 0.25:
                            continue
                        if opp_m.has_p("Nimble Agility") and rng.random() < 0.15:
                            continue

                    if not (att.has_p("Phase Shift") or att.has_p("Aerial Pierce")):
                        if opp_m.has_p("Water Resilience"):
                            atk_power = max(1, atk_power - 3)
                        if opp_m.has_p("Protective Flame"):
                            atk_power = max(1, atk_power - 4)
                        if opp_m.has_p("Rock Hardness"):
                            atk_power = max(1, atk_power - 4)
                        if opp_m.has_p("Steel Armor"):
                            atk_power = max(1, atk_power - 5)

                    opp_m.cur_def -= atk_power
                    if att.proto.cid <= 452:
                        attacker.card_dmg[att.proto.cid] = attacker.card_dmg.get(att.proto.cid, 0) + atk_power

                    if att.has_p("Eternal Flame"):
                        att.bonus_atk += 2
                    if att.has_p("Static Discharge"):
                        opp_m.silenced_turns = max(opp_m.silenced_turns, 1)
                    if att.has_p("Life Leech"):
                        att.heal_def(max(2, int(round(atk_power * 0.20))))
                    if att.has_p("Slow Poison"):
                        opp_m.poison_dmg = max(opp_m.poison_dmg, 6)
                    if att.has_p("Chain Lightning"):
                        splash = max(3, int(round(atk_power * 0.30)))
                        for adj in (slot_idx - 1, slot_idx + 1):
                            if 0 <= adj < 6 and defender.slots[adj] is not None:
                                adj_m = defender.slots[adj]
                                adj_m.cur_def -= splash
                                if not adj_m.is_alive:
                                    self.handle_death(defender, attacker, adj, adj_m, att, turn_num)
                                break

                    if opp_m.has_p("Poison Thorns") or opp_m.has_p("Backlash Spark"):
                        att.cur_def -= 3
                    if opp_m.has_p("Flint Skin"):
                        att.cur_def -= 5
                    if opp_m.has_p("Burning Embers"):
                        att.cur_def -= max(2, int(round(atk_power * 0.25)))
                    if opp_m.has_p("Auto-Freeze Attacker") and att.is_alive:
                        att.frozen_turns = max(att.frozen_turns, 1)

                    if not opp_m.is_alive:
                        self.handle_death(defender, attacker, slot_idx, opp_m, att, turn_num)
                    if not att.is_alive:
                        self.handle_death(attacker, defender, slot_idx, att, opp_m, turn_num)
                else:
                    defender.take_leader_damage(atk_power)
                    if att.proto.cid <= 452:
                        attacker.card_dmg[att.proto.cid] = attacker.card_dmg.get(att.proto.cid, 0) + atk_power
                    if att.has_p("Mana Steal"):
                        att.bonus_atk += 0  # +1 current power
                        attacker.power += 1

    def play_match(self, rng: random.Random, max_turns: int = 80, verbose: bool = False) -> Tuple[int, int, SimPlayer, SimPlayer, List[str], dict]:
        log: Optional[List[str]] = [] if verbose else None
        p1 = SimPlayer("اللاعب أ")
        p2 = SimPlayer("اللاعب ب")

        p1.deck = self.generate_deck(self.deck_size, None, rng)
        p2.deck = self.generate_deck(self.deck_size, None, rng)

        for _ in range(5):
            self.draw_card(p1, 1, rng, log)
            self.draw_card(p2, 1, rng, log)

        active_env: Optional[str] = None
        env_turns = 0
        turn_num = 1
        is_p1 = True

        # Excitement & Boredom Pacing Telemetry
        half_turns = 0
        dead_turns = 0
        stall_turns = 0
        explosive_turns = 0
        lead_changes = 0
        prev_leader = 0
        p1_trailed_deep = False
        p2_trailed_deep = False

        while turn_num <= max_turns and p1.hp > 0 and p2.hp > 0:
            half_turns += 1
            active = p1 if is_p1 else p2
            other = p2 if is_p1 else p1

            p1.turn_deaths = 0
            p2.turn_deaths = 0
            p1.turn_clutch = False
            p2.turn_clutch = False
            hp1_start = p1.hp
            hp2_start = p2.hp
            actions_taken = 0
            acted_cids: List[int] = []

            if other.team_immune_turns > 0:
                other.team_immune_turns -= 1

            active.power = active.mana_cap
            if turn_num == 2 and not is_p1:
                active.power += 1  # تعويض +1 لمرة واحدة فقط في أول جولة للاعب الثاني دون زيادة دائمة للمانا
                if log is not None:
                    log.append(f"  [دور {turn_num}] {active.name} حصل على تعويض الجولة الأولى (+1 طاقة مؤقتة)")
            active.sacrificed_turn = False
            if active.deny_turns > 0:
                active.deny_turns -= 1
                active.sacrificed_turn = True
            else:
                if not (turn_num == 1 and is_p1):
                    self.draw_card(active, turn_num, rng, log)
                elif log is not None:
                    log.append(f"  [دور {turn_num}] {active.name} يتخطى سحب الدور الأول (قاعدة التوازن)")

            for s in active.slots:
                if s is not None:
                    s.reset_turn(turn_num)

            self.start_of_turn_passives(active, other, turn_num)
            if p1.hp <= 0 or p2.hp <= 0:
                break

            # 1. Smart Sacrifice (guarantees ramp from 0 mana & smart mana growth)
            if len(active.hand) >= 2 and not active.sacrificed_turn and active.mana_cap < 10:
                aff_m = sum(1 for c in active.hand if c.card_type == "monster" and c.cost <= active.power)
                has_hi = any(c.card_type == "monster" and c.cost > active.power for c in active.hand)
                needs_mana = (active.mana_cap == 0) or (has_hi and aff_m == 0) or (active.mana_cap < 7 and len(active.hand) >= 5)
                if needs_mana or len(active.hand) >= 8:
                    hi_card = max((c for c in active.hand if c.cost > active.power + 2), key=lambda x: x.cost, default=None)
                    if hi_card is None:
                        hi_card = next((c for c in active.hand if c.card_type == "env" and not self._should_play_env(active, other, c, active_env, env_turns)), None)
                    if hi_card is None:
                        hi_card = next((c for c in active.hand if c.card_type != "monster" and c.cost > active.power + 1), None)
                    if hi_card is None and (needs_mana or len(active.hand) >= 6):
                        hi_card = max(active.hand, key=lambda x: x.cost)
                    if hi_card is not None:
                        active.hand.remove(hi_card)
                        active.graveyard.append(hi_card)
                        active.mana_cap += 1
                        active.power += 1
                        active.sacrificed_turn = True
                        if log is not None:
                            log.append(f"  [دور {turn_num}] {active.name} ضحّى بـ #{hi_card.cid} -> المانا: {active.mana_cap} | نقاط الاستدعاء: {active.power}")

            # 2. Smart Environment Activation (Cantrip: draws +1 card & shifts weather when net beneficial)
            for c in list(active.hand):
                if c.card_type == "env" and c.cost <= active.power and self._should_play_env(active, other, c, active_env, env_turns):
                    active.power -= c.cost
                    active.hand.remove(c)
                    active.played_ids.add(c.cid)
                    acted_cids.append(c.cid)
                    actions_taken += 1
                    active_env = c.element
                    env_turns = 4
                    self.draw_card(active, turn_num, rng, log)
                    if log is not None:
                        log.append(f"  [دور {turn_num}] {active.name} غيّر البيئة تكتيكياً (+سحب ورقة) -> {ELEM_AR.get(active_env, active_env)}")
                    break

            # 3A. Combo Ordering: Summon Spell-Synergy / Mana-Gen Enablers BEFORE casting spells!
            has_playable_spells = any(c.card_type == "spell" and c.cost <= active.power for c in active.hand)
            if has_playable_spells:
                synergy_hand = [
                    c for c in active.hand
                    if c.card_type == "monster" and c.cost <= active.power and (c.passive_tags & self._SYNERGY_PASSIVES)
                ]
                synergy_hand.sort(key=lambda c: c.cost)
                for sm in synergy_hand:
                    if sm.cost <= active.power:
                        slot = self._choose_best_slot(active, other, sm)
                        if slot != -1:
                            self.summon_monster(active, other, sm, slot, turn_num)
                            actions_taken += 1
                            acted_cids.append(sm.cid)
                            if log is not None:
                                log.append(f"  [دور {turn_num}] {active.name} استدعى ممهّد كومبو #{sm.cid} @خانة {slot}")

            # 3B. Pre-Summon / Offensive / Draw / Lane-Clearing Spells
            pre_spells = [
                c for c in active.hand
                if c.card_type == "spell" and c.cost <= active.power and c.spell_type not in self._POST_BUFF_SPELLS
            ]
            for sp in pre_spells:
                if sp.cost <= active.power and sp in active.hand and self._should_cast_spell(active, other, sp):
                    self.cast_spell(active, other, sp, turn_num, rng)
                    actions_taken += 1
                    acted_cids.append(sp.cid)
                    if log is not None:
                        log.append(f"  [دور {turn_num}] {active.name} ألقى تعويذة #{sp.cid} ({sp.role})")
                    if p1.hp <= 0 or p2.hp <= 0:
                        break
            if p1.hp <= 0 or p2.hp <= 0:
                break

            # 4. Smart Tactical Monster Summons (Counter-Lane + Quick Charge Sniping)
            playable_monsters = [c for c in active.hand if c.card_type == "monster"]
            playable_monsters.sort(
                key=lambda c: (1 if "Quick Charge" in c.passive_tags else 0, c.cost + c.atk + c.def_stat),
                reverse=True,
            )
            for mcard in playable_monsters:
                if mcard.cost <= active.power:
                    slot = self._choose_best_slot(active, other, mcard)
                    if slot != -1:
                        self.summon_monster(active, other, mcard, slot, turn_num)
                        actions_taken += 1
                        acted_cids.append(mcard.cid)
                        if log is not None:
                            log.append(
                                f"  [دور {turn_num}] {active.name} استدعى #{mcard.cid} "
                                f"({ELEM_AR.get(mcard.element, mcard.element)}، تكلفة {mcard.cost}، {mcard.atk}/{mcard.def_stat}) @خانة {slot}"
                            )

            # 4B. Post-Summon Ally-Buff Spells (now all summoned allies get buffed!)
            post_spells = [c for c in active.hand if c.card_type == "spell" and c.cost <= active.power]
            for sp in post_spells:
                if sp.cost <= active.power and sp in active.hand and self._should_cast_spell(active, other, sp):
                    self.cast_spell(active, other, sp, turn_num, rng)
                    actions_taken += 1
                    acted_cids.append(sp.cid)
                    if log is not None:
                        log.append(f"  [دور {turn_num}] {active.name} ألقى تعويذة تعزيز #{sp.cid} ({sp.role})")
                    if p1.hp <= 0 or p2.hp <= 0:
                        break
            if p1.hp <= 0 or p2.hp <= 0:
                break

            # 5. Prioritized Active Skills
            pwr_before_sk = active.power
            self.execute_active_skills(active, other, turn_num, rng, acted_cids)
            if active.power < pwr_before_sk or any(s is not None and s.used_active_turn for s in active.slots):
                actions_taken += 1
            if p1.hp <= 0 or p2.hp <= 0:
                break

            # 6. Execute Combat Phase
            self.execute_combat_phase(active, other, active_env, turn_num, rng)

            # Evaluate Excitement & Boredom for this Turn
            hp_swing = abs(p1.hp - hp1_start) + abs(p2.hp - hp2_start)
            total_deaths = p1.turn_deaths + p2.turn_deaths
            had_clutch = p1.turn_clutch or p2.turn_clutch
            board_cnt = sum(1 for s in p1.slots if s is not None) + sum(1 for s in p2.slots if s is not None)

            is_explosive = (total_deaths >= 2) or (hp_swing >= 45) or had_clutch
            is_dead = (actions_taken == 0) and (hp_swing == 0) and (total_deaths == 0)
            is_stall = (not is_dead) and (board_cnt >= 2) and (total_deaths == 0) and (hp_swing < 10)

            if is_explosive:
                explosive_turns += 1
                for cid in set(acted_cids):
                    active.card_excite[cid] = active.card_excite.get(cid, 0) + 1
                if log is not None:
                    log.append(f"    🔥 [لحظة إثارة!] ضرر/تغير: {hp_swing} | وفيات: {total_deaths}")
            elif is_dead:
                dead_turns += 1
                for hc in active.hand:
                    if hc.cid <= 452 and hc.cost > active.power:
                        active.card_stall[hc.cid] = active.card_stall.get(hc.cid, 0) + 1
                if log is not None:
                    log.append(f"    💤 [دور ميت] لا لعب متاح (مانا {active.power})")
            elif is_stall:
                stall_turns += 1
                for s in active.slots:
                    if s is not None and s.proto.cid <= 452:
                        active.card_stall[s.proto.cid] = active.card_stall.get(s.proto.cid, 0) + 1
                if log is not None:
                    log.append(f"    🧊 [جمود لوحة] تبادل ضعيف بدون سقوط وحوش")

            # Lead Swing / Comeback Tracking
            if turn_num >= 3:
                if p2.hp - p1.hp >= 75:
                    p1_trailed_deep = True
                if p1.hp - p2.hp >= 75:
                    p2_trailed_deep = True
                cur_leader = 1 if p1.hp > p2.hp + 15 else (2 if p2.hp > p1.hp + 15 else prev_leader)
                if prev_leader != 0 and cur_leader != 0 and cur_leader != prev_leader:
                    lead_changes += 1
                    if log is not None:
                        log.append(f"    🔄 [انقلاب الصدارة!] ({p1.hp} ضد {p2.hp})")
                if cur_leader != 0:
                    prev_leader = cur_leader

            if p1.hp <= 0 or p2.hp <= 0:
                break

            if active_env is not None:
                env_turns -= 1
                if env_turns <= 0:
                    active_env = None

            is_p1 = not is_p1
            turn_num += 1

        if p1.hp > 0 and p2.hp <= 0:
            winner = 1
        elif p2.hp > 0 and p1.hp <= 0:
            winner = 2
        elif p1.hp > p2.hp:
            winner = 1
        elif p2.hp > p1.hp:
            winner = 2
        else:
            winner = 0

        turns_taken = min(turn_num, max_turns)
        comeback_win = 1 if ((winner == 1 and p1_trailed_deep) or (winner == 2 and p2_trailed_deep)) else 0
        winner_hp = p1.hp if winner == 1 else (p2.hp if winner == 2 else 0)
        nail_biter = 1 if (winner != 0 and winner_hp <= 90) else 0
        blowout = 1 if (winner != 0 and turns_taken <= 7) else 0

        if winner == 1 and (comeback_win or nail_biter):
            bonus_e = 2 if comeback_win else 1
            for cid in p1.played_ids:
                p1.card_excite[cid] = p1.card_excite.get(cid, 0) + bonus_e
        elif winner == 2 and (comeback_win or nail_biter):
            bonus_e = 2 if comeback_win else 1
            for cid in p2.played_ids:
                p2.card_excite[cid] = p2.card_excite.get(cid, 0) + bonus_e

        mstats = {
            "half_turns": max(1, half_turns),
            "dead_turns": dead_turns,
            "stall_turns": stall_turns,
            "explosive_turns": explosive_turns,
            "lead_changes": lead_changes,
            "comeback_win": comeback_win,
            "nail_biter": nail_biter,
            "blowout": blowout,
            "salvations": p1.salvations + p2.salvations,
            "multipliers": p1.multipliers + p2.multipliers,
        }

        if log is not None:
            winner_lbl = "اللاعب أ" if winner == 1 else ("اللاعب ب" if winner == 2 else "تعادل")
            log.append(
                f"=== نهاية المعركة (الأدوار: {turns_taken}) | الفائز: {winner_lbl} | الصحة: {p1.hp} ضد {p2.hp} "
                f"| متفجرة: {explosive_turns} • ميتة: {dead_turns} • جمود: {stall_turns} • انقلابات: {lead_changes} ==="
            )

        return winner, turns_taken, p1, p2, (log or []), mstats


# ==============================================================================
# Multiprocessing Worker Globals & Batch Entrypoint
# ==============================================================================
_WORKER_ENGINE: Optional[BattleEngine] = None
_WORKER_SEED_BASE: int = 0


def _init_worker(json_path: str, endless_waves: bool, deck_size: int, seed_base: int) -> None:
    global _WORKER_ENGINE, _WORKER_SEED_BASE
    by_id, monsters, spells, envs = load_database(json_path)
    _WORKER_ENGINE = BattleEngine(by_id, monsters, spells, envs, endless_waves=endless_waves, deck_size=deck_size)
    _WORKER_SEED_BASE = seed_base


def _run_chunk(task_args: Tuple[int, int]) -> dict:
    chunk_id, count = task_args
    rng = random.Random(_WORKER_SEED_BASE + chunk_id * 1000003)
    eng = _WORKER_ENGINE
    assert eng is not None

    # Local sparse dictionaries for ultra-low pickle overhead
    p_matches: Dict[int, int] = {}
    p_wins: Dict[int, int] = {}
    nm_matches: Dict[int, int] = {}
    nm_wins: Dict[int, int] = {}
    d_matches: Dict[int, int] = {}
    d_wins: Dict[int, int] = {}
    dmg_sum: Dict[int, int] = {}
    kill_sum: Dict[int, int] = {}
    excite_sum: Dict[int, int] = {}
    stall_sum: Dict[int, int] = {}

    w1 = w2 = draws = 0
    total_turns = 0
    total_waves = 0
    total_comebacks = 0
    half_turns = 0
    dead_turns = 0
    stall_turns = 0
    explosive_turns = 0
    lead_changes = 0
    comeback_wins = 0
    nail_biters = 0
    blowouts = 0
    salvations = 0
    multipliers = 0

    for _ in range(count):
        winner, turns, p1, p2, _, ms = eng.play_match(rng)
        total_turns += turns
        total_waves += max(0, p1.wave_num - 1) + max(0, p2.wave_num - 1)
        total_comebacks += p1.comebacks + p2.comebacks
        half_turns += ms["half_turns"]
        dead_turns += ms["dead_turns"]
        stall_turns += ms["stall_turns"]
        explosive_turns += ms["explosive_turns"]
        lead_changes += ms["lead_changes"]
        comeback_wins += ms["comeback_win"]
        nail_biters += ms["nail_biter"]
        blowouts += ms["blowout"]
        salvations += ms["salvations"]
        multipliers += ms["multipliers"]

        if winner == 1:
            w1 += 1
        elif winner == 2:
            w2 += 1
        else:
            draws += 1

        # Played cards attribution (non-mirror + total)
        p1_win = 1 if winner == 1 else 0
        p2_win = 1 if winner == 2 else 0
        if winner != 0:
            both_played = p1.played_ids & p2.played_ids
            for cid in p1.played_ids:
                p_matches[cid] = p_matches.get(cid, 0) + 1
                p_wins[cid] = p_wins.get(cid, 0) + p1_win
                if cid not in both_played:
                    nm_matches[cid] = nm_matches.get(cid, 0) + 1
                    nm_wins[cid] = nm_wins.get(cid, 0) + p1_win
            for cid in p2.played_ids:
                p_matches[cid] = p_matches.get(cid, 0) + 1
                p_wins[cid] = p_wins.get(cid, 0) + p2_win
                if cid not in both_played:
                    nm_matches[cid] = nm_matches.get(cid, 0) + 1
                    nm_wins[cid] = nm_wins.get(cid, 0) + p2_win

            for cid in p1.drawn_ever_ids:
                d_matches[cid] = d_matches.get(cid, 0) + 1
                d_wins[cid] = d_wins.get(cid, 0) + p1_win
            for cid in p2.drawn_ever_ids:
                d_matches[cid] = d_matches.get(cid, 0) + 1
                d_wins[cid] = d_wins.get(cid, 0) + p2_win

        for cid, v in p1.card_dmg.items():
            dmg_sum[cid] = dmg_sum.get(cid, 0) + v
        for cid, v in p2.card_dmg.items():
            dmg_sum[cid] = dmg_sum.get(cid, 0) + v
        for cid, v in p1.card_kills.items():
            kill_sum[cid] = kill_sum.get(cid, 0) + v
        for cid, v in p2.card_kills.items():
            kill_sum[cid] = kill_sum.get(cid, 0) + v
        for cid, v in p1.card_excite.items():
            excite_sum[cid] = excite_sum.get(cid, 0) + v
        for cid, v in p2.card_excite.items():
            excite_sum[cid] = excite_sum.get(cid, 0) + v
        for cid, v in p1.card_stall.items():
            stall_sum[cid] = stall_sum.get(cid, 0) + v
        for cid, v in p2.card_stall.items():
            stall_sum[cid] = stall_sum.get(cid, 0) + v

    return {
        "n": count, "w1": w1, "w2": w2, "draws": draws,
        "turns": total_turns, "waves": total_waves, "comebacks": total_comebacks,
        "half_turns": half_turns, "dead_turns": dead_turns, "stall_turns": stall_turns,
        "explosive_turns": explosive_turns, "lead_changes": lead_changes,
        "comeback_wins": comeback_wins, "nail_biters": nail_biters, "blowouts": blowouts,
        "salvations": salvations, "multipliers": multipliers,
        "pm": p_matches, "pw": p_wins, "nmm": nm_matches, "nmw": nm_wins,
        "dm": d_matches, "dw": d_wins, "dmg": dmg_sum, "kills": kill_sum,
        "cexcite": excite_sum, "cstall": stall_sum,
    }


# ==============================================================================
# Tkinter GUI Application
# ==============================================================================
class SimulatorApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("⚔️ محاكي معارك الكروت الكامل (حتى 1,000,000 معركة)")
        self.geometry("1420x860")
        self.configure(bg="#0F172A")

        self.json_path = DEFAULT_JSON_PATH
        self.by_id, self.monsters, self.spells, self.envs = load_database(self.json_path)

        # Aggregated runtime statistics
        self.reset_stats()
        self.is_running = False
        self.stop_requested = False
        self.sim_start_time = 0.0
        self.target_matches = 10000
        self.ui_queue: queue.Queue = queue.Queue()

        self._setup_styles()
        self._build_ui()
        self.refresh_table()
        self.after(120, self._poll_queue)

    def reset_stats(self) -> None:
        self.done_matches = 0
        self.w1 = 0
        self.w2 = 0
        self.draws = 0
        self.total_turns = 0
        self.total_waves = 0
        self.total_comebacks = 0
        self.half_turns = 0
        self.dead_turns = 0
        self.stall_turns = 0
        self.explosive_turns = 0
        self.lead_changes = 0
        self.comeback_wins = 0
        self.nail_biters = 0
        self.blowouts = 0
        self.salvations = 0
        self.multipliers = 0
        self.pm: Dict[int, int] = {}
        self.pw: Dict[int, int] = {}
        self.nmm: Dict[int, int] = {}
        self.nmw: Dict[int, int] = {}
        self.dm: Dict[int, int] = {}
        self.dw: Dict[int, int] = {}
        self.dmg: Dict[int, int] = {}
        self.kills: Dict[int, int] = {}
        self.cexcite: Dict[int, int] = {}
        self.cstall: Dict[int, int] = {}

    def _setup_styles(self) -> None:
        style = ttk.Style(self)
        style.theme_use("clam")
        style.configure("Treeview", background="#1E293B", foreground="#E2E8F0", fieldbackground="#1E293B", rowheight=26, font=("Segoe UI", 9))
        style.configure("Treeview.Heading", background="#0B1220", foreground="#38BDF8", font=("Segoe UI", 9, "bold"), relief="flat")
        style.map("Treeview", background=[("selected", "#2563EB")])
        style.configure("TProgressbar", troughcolor="#1E293B", background="#22C55E", thickness=18)

    def _build_ui(self) -> None:
        # Header & Controls
        top = tk.Frame(self, bg="#1E293B", pady=10, padx=14)
        top.pack(fill="x", padx=10, pady=(10, 6))

        tk.Label(
            top, text="⚔️ محاكي المعارك بالقواعد الكاملة (452 بطاقة • موجات لا نهائية • المهارات الكامنة والنشطة)",
            bg="#1E293B", fg="#F8FAFC", font=("Segoe UI", 13, "bold")
        ).grid(row=0, column=0, columnspan=10, sticky="w", pady=(0, 8))

        tk.Label(top, text="عدد المعارك:", bg="#1E293B", fg="#CBD5E1", font=("Segoe UI", 10)).grid(row=1, column=0, sticky="w")
        self.battles_var = tk.StringVar(value="10000")
        battles_entry = tk.Entry(top, textvariable=self.battles_var, width=12, bg="#0F172A", fg="#38BDF8", insertbackground="#38BDF8", font=("Segoe UI", 10, "bold"))
        battles_entry.grid(row=1, column=1, padx=(4, 8))

        for col_i, (lbl, val) in enumerate([("1 ألف", "1000"), ("10 آلاف", "10000"), ("100 ألف", "100000"), ("500 ألف", "500000"), ("1 مليون", "1000000")], start=2):
            tk.Button(
                top, text=lbl, bg="#334155", fg="#F1F5F9", relief="flat", padx=8,
                command=lambda v=val: self.battles_var.set(v)
            ).grid(row=1, column=col_i, padx=2)

        tk.Label(top, text="الأنوية (CPU):", bg="#1E293B", fg="#CBD5E1", font=("Segoe UI", 10)).grid(row=1, column=7, padx=(14, 4))
        max_cpu = max(1, (os.cpu_count() or 4))
        self.workers_var = tk.IntVar(value=max(1, max_cpu - 1))
        tk.Spinbox(top, from_=1, to=max_cpu, textvariable=self.workers_var, width=4, bg="#0F172A", fg="#F8FAFC").grid(row=1, column=8, padx=4)

        self.endless_var = tk.BooleanVar(value=True)
        tk.Checkbutton(
            top, text="موجات لا نهائية (>50 + عودة الأبطال)", variable=self.endless_var,
            bg="#1E293B", fg="#A7F3D0", selectcolor="#0F172A", activebackground="#1E293B", font=("Segoe UI", 9, "bold")
        ).grid(row=1, column=9, padx=(12, 4))

        btn_frame = tk.Frame(top, bg="#1E293B")
        btn_frame.grid(row=2, column=0, columnspan=10, sticky="w", pady=(10, 0))

        self.start_btn = tk.Button(btn_frame, text="▶ بدء المحاكاة", bg="#16A34A", fg="#FFFFFF", font=("Segoe UI", 10, "bold"), relief="flat", padx=16, pady=5, command=self.start_sim)
        self.start_btn.pack(side="left", padx=(0, 8))

        self.stop_btn = tk.Button(btn_frame, text="⏹ إيقاف", bg="#DC2626", fg="#FFFFFF", font=("Segoe UI", 10, "bold"), relief="flat", padx=14, pady=5, state="disabled", command=self.stop_sim)
        self.stop_btn.pack(side="left", padx=(0, 8))

        tk.Button(btn_frame, text="🔍 تجربة معركة واحدة (سجل مفصل)", bg="#0284C7", fg="#FFFFFF", font=("Segoe UI", 9, "bold"), relief="flat", padx=12, pady=5, command=self.run_sample_match).pack(side="left", padx=(0, 8))
        tk.Button(btn_frame, text="📊 تصدير Excel (.xlsx)", bg="#7C3AED", fg="#FFFFFF", font=("Segoe UI", 9, "bold"), relief="flat", padx=12, pady=5, command=self.export_excel).pack(side="left", padx=(0, 8))
        tk.Button(btn_frame, text="📄 تصدير CSV", bg="#475569", fg="#FFFFFF", font=("Segoe UI", 9, "bold"), relief="flat", padx=12, pady=5, command=self.export_csv).pack(side="left")

        # Progress & Telemetry
        prog_frame = tk.Frame(self, bg="#1E293B", pady=8, padx=14)
        prog_frame.pack(fill="x", padx=10, pady=(0, 6))

        self.progress = ttk.Progressbar(prog_frame, mode="determinate", maximum=100)
        self.progress.pack(fill="x", pady=(0, 6))

        self.status_lbl = tk.Label(
            prog_frame,
            text=f"جاهز • تم تحميل {len(self.by_id)} بطاقة ({len(self.monsters)} وحش + {len(self.spells)} تعويذة + {len(self.envs)} بيئة)",
            bg="#1E293B", fg="#38BDF8", font=("Segoe UI", 10, "bold"), anchor="w"
        )
        self.status_lbl.pack(fill="x")

        self.summary_lbl = tk.Label(
            prog_frame, text="ملخص التوازن: شغّل المحاكاة لحساب نسبة الفوز الحقيقية لكل بطاقة.",
            bg="#1E293B", fg="#CBD5E1", font=("Segoe UI", 9), anchor="w"
        )
        self.summary_lbl.pack(fill="x", pady=(2, 0))

        self.pacing_lbl = tk.Label(
            prog_frame, text="إيقاع المتعة والملل: سيظهر هنا تحليل الأدوار المتفجرة، الانقلابات، النهايات المتقاربة، الأدوار الميتة والجمود.",
            bg="#1E293B", fg="#FDE68A", font=("Segoe UI", 9, "bold"), anchor="w"
        )
        self.pacing_lbl.pack(fill="x", pady=(2, 0))

        # Filters
        flt = tk.Frame(self, bg="#0F172A", pady=4, padx=10)
        flt.pack(fill="x")

        tk.Label(flt, text="النوع:", bg="#0F172A", fg="#CBD5E1").pack(side="left")
        self.type_filter = ttk.Combobox(flt, values=["الكل", "وحش", "تعويذة", "بيئة"], width=10, state="readonly")
        self.type_filter.set("الكل")
        self.type_filter.pack(side="left", padx=(4, 12))
        self.type_filter.bind("<<ComboboxSelected>>", lambda _e: self.refresh_table())

        tk.Label(flt, text="العنصر:", bg="#0F172A", fg="#CBD5E1").pack(side="left")
        self.elem_filter = ttk.Combobox(flt, values=["الكل"] + [ELEM_AR[e] for e in ELEMENTS], width=12, state="readonly")
        self.elem_filter.set("الكل")
        self.elem_filter.pack(side="left", padx=(4, 12))
        self.elem_filter.bind("<<ComboboxSelected>>", lambda _e: self.refresh_table())

        tk.Label(flt, text="التوازن:", bg="#0F172A", fg="#CBD5E1").pack(side="left")
        self.status_filter = ttk.Combobox(flt, values=["الكل", "قوي جداً (>55%)", "متوازن (45-55%)", "ضعيف (<45%)"], width=18, state="readonly")
        self.status_filter.set("الكل")
        self.status_filter.pack(side="left", padx=(4, 12))
        self.status_filter.bind("<<ComboboxSelected>>", lambda _e: self.refresh_table())

        tk.Label(flt, text="بحث:", bg="#0F172A", fg="#CBD5E1").pack(side="left")
        self.search_var = tk.StringVar()
        self.search_var.trace_add("write", lambda *_: self.refresh_table())
        tk.Entry(flt, textvariable=self.search_var, width=22, bg="#1E293B", fg="#F8FAFC", insertbackground="#38BDF8").pack(side="left", padx=4)

        self.sort_col = "winrate"
        self.sort_desc = True

        # Treeview Table
        tbl_wrap = tk.Frame(self, bg="#0F172A", padx=10, pady=6)
        tbl_wrap.pack(fill="both", expand=True)

        cols = (
            "id", "type", "element", "role", "tier", "cost", "stats", "skills",
            "plays", "drawn", "winrate", "all_wr", "avg_dmg", "kills", "excite", "boredom", "status"
        )
        self.tree = ttk.Treeview(tbl_wrap, columns=cols, show="headings")

        headers = {
            "id": ("المعرّف", 54), "type": ("النوع", 64), "element": ("العنصر", 74),
            "role": ("الدور", 120), "tier": ("الفئة", 44), "cost": ("التكلفة", 50),
            "stats": ("هجوم/دفاع", 74), "skills": ("الكامنة + النشطة / التأثير", 280),
            "plays": ("المشاركات", 74), "drawn": ("السحب", 68),
            "winrate": ("نسبة الفوز %", 86), "all_wr": ("الكل %", 74),
            "avg_dmg": ("متوسط الضرر", 80), "kills": ("القتلى", 60),
            "excite": ("زخم الحماس", 82), "boredom": ("جمود/تعطل", 82), "status": ("حالة التوازن", 114),
        }
        for c, (txt, w) in headers.items():
            self.tree.heading(c, text=txt, command=lambda col=c: self._sort_by(col))
            self.tree.column(c, width=w, anchor="center" if c not in ("role", "skills") else "w")

        vsb = ttk.Scrollbar(tbl_wrap, orient="vertical", command=self.tree.yview)
        hsb = ttk.Scrollbar(tbl_wrap, orient="horizontal", command=self.tree.xview)
        self.tree.configure(yscrollcommand=vsb.set, xscrollcommand=hsb.set)
        self.tree.grid(row=0, column=0, sticky="nsew")
        vsb.grid(row=0, column=1, sticky="ns")
        hsb.grid(row=1, column=0, sticky="ew")
        tbl_wrap.grid_rowconfigure(0, weight=1)
        tbl_wrap.grid_columnconfigure(0, weight=1)

        self.tree.tag_configure("op", background="#451A1A", foreground="#FCA5A5")
        self.tree.tag_configure("strong", background="#3B2F13", foreground="#FDE68A")
        self.tree.tag_configure("balanced", background="#143325", foreground="#86EFAC")
        self.tree.tag_configure("weak", background="#1E293B", foreground="#93C5FD")
        self.tree.tag_configure("pending", background="#1E293B", foreground="#94A3B8")

    def _sort_by(self, col: str) -> None:
        if self.sort_col == col:
            self.sort_desc = not self.sort_desc
        else:
            self.sort_col = col
            self.sort_desc = True
        self.refresh_table()

    def _classify(self, plays: int, wr: float) -> Tuple[str, str]:
        if plays < 10:
            return "قيد الانتظار", "pending"
        if wr > 55.0:
            return f"قوي جداً (+{wr-50:.1f}%)", "op"
        if wr > 52.5:
            return f"قوي (+{wr-50:.1f}%)", "strong"
        if wr >= 47.5:
            return "متوازن", "balanced"
        if wr >= 45.0:
            return "منخفض قليلاً", "weak"
        return f"ضعيف ({wr-50:.1f}%)", "weak"

    def refresh_table(self) -> None:
        tf_raw = self.type_filter.get()
        ef_raw = self.elem_filter.get()
        tf = TYPE_FROM_AR.get(tf_raw, tf_raw)
        ef = ELEM_FROM_AR.get(ef_raw, ef_raw)
        sf = self.status_filter.get()
        q = self.search_var.get().strip().lower()

        rows = []
        op_c = bal_c = weak_c = 0

        for cid, proto in self.by_id.items():
            plays = self.nmm.get(cid, 0)
            wins = self.nmw.get(cid, 0)
            all_p = self.pm.get(cid, 0)
            all_w = self.pw.get(cid, 0)
            dr = self.dm.get(cid, 0)
            tot_dmg = self.dmg.get(cid, 0)
            tot_k = self.kills.get(cid, 0)

            wr = (wins / plays * 100.0) if plays > 0 else 50.0
            all_wr = (all_w / all_p * 100.0) if all_p > 0 else 50.0
            avg_d = (tot_dmg / all_p) if all_p > 0 else 0.0
            st_lbl, tag = self._classify(plays, wr)

            if plays >= 10:
                if wr > 55.0:
                    op_c += 1
                elif wr < 45.0:
                    weak_c += 1
                else:
                    bal_c += 1

            if tf not in ("الكل", "All") and proto.card_type != tf:
                continue
            if ef not in ("الكل", "All") and proto.element != ef:
                continue
            if sf in ("قوي جداً (>55%)", "OP (>55%)") and not (plays >= 10 and wr > 55.0):
                continue
            if sf in ("متوازن (45-55%)", "Balanced (45-55%)") and not (plays >= 10 and 45.0 <= wr <= 55.0):
                continue
            if sf in ("ضعيف (<45%)", "Weak (<45%)") and not (plays >= 10 and wr < 45.0):
                continue

            type_disp = TYPE_AR.get(proto.card_type, proto.card_type)
            elem_disp = ELEM_AR.get(proto.element, proto.element)
            role_disp = ROLE_AR.get(proto.role, proto.role)

            if q:
                haystack = " ".join((
                    str(cid), proto.card_type, type_disp, proto.element, elem_disp,
                    proto.role, role_disp, proto.summary_text
                )).lower()
                if q not in haystack:
                    continue

            stats_str = f"{proto.atk}/{proto.def_stat}" if proto.card_type == "monster" else "-"
            ex_rate = (self.cexcite.get(cid, 0) / max(1, all_p)) * 100.0 if all_p > 0 else 0.0
            st_rate = (self.cstall.get(cid, 0) / max(1, max(all_p, dr))) * 100.0 if (all_p > 0 or dr > 0) else 0.0
            rows.append((
                cid, type_disp, elem_disp, role_disp, proto.tier_num, proto.cost,
                stats_str, proto.summary_text, all_p, dr, wr, all_wr, avg_d, tot_k, ex_rate, st_rate, st_lbl, tag
            ))

        key_idx = {
            "id": 0, "type": 1, "element": 2, "role": 3, "tier": 4, "cost": 5,
            "plays": 8, "drawn": 9, "winrate": 10, "all_wr": 11, "avg_dmg": 12, "kills": 13,
            "excite": 14, "boredom": 15, "status": 10,
        }.get(self.sort_col, 10)
        rows.sort(key=lambda r: r[key_idx], reverse=self.sort_desc)

        self.tree.delete(*self.tree.get_children())
        for r in rows:
            self.tree.insert(
                "", "end",
                values=(
                    r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7],
                    f"{r[8]:,}", f"{r[9]:,}", f"{r[10]:.2f}%", f"{r[11]:.2f}%",
                    f"{r[12]:.1f}", f"{r[13]:,}", f"{r[14]:.1f}%", f"{r[15]:.1f}%", r[16]
                ),
                tags=(r[17],)
            )

        if self.done_matches > 0:
            avg_t = self.total_turns / self.done_matches
            p1_pct = self.w1 / self.done_matches * 100.0
            p2_pct = self.w2 / self.done_matches * 100.0
            self.summary_lbl.config(
                text=(
                    f"البطاقات المحلّلة (≥10 مباريات بدون مرآة): متوازن={bal_c} | قوي جداً(>55%)={op_c} | ضعيف(<45%)={weak_c}   •   "
                    f"متوسط الأدوار: {avg_t:.1f}   •   نسبة أ/ب: {p1_pct:.1f}% / {p2_pct:.1f}%   •   "
                    f"الموجات اللانهائية: {self.total_waves:,}   •   عودات الأبطال: {self.total_comebacks:,}"
                )
            )
            ht = max(1, self.half_turns)
            dm = max(1, self.done_matches)
            exp_pct = self.explosive_turns / ht * 100.0
            dead_pct = self.dead_turns / ht * 100.0
            stall_pct = self.stall_turns / ht * 100.0
            cb_pct = self.comeback_wins / dm * 100.0
            nb_pct = self.nail_biters / dm * 100.0
            bo_pct = self.blowouts / dm * 100.0
            avg_lc = self.lead_changes / dm
            self.pacing_lbl.config(
                text=(
                    f"🔥 أدوار متفجرة: {exp_pct:.1f}%   •   🔄 انقلابات الصدارة: {avg_lc:.2f}/مباراة ({cb_pct:.1f}% فوز بعد تأخر)   •   "
                    f"🏆 نهايات متقاربة (≤90HP): {nb_pct:.1f}%   •   💤 أدوار ميتة: {dead_pct:.1f}%   •   "
                    f"🧊 جمود لوحة: {stall_pct:.1f}%   •   ⚡ حسم سريع (≤7 أدوار): {bo_pct:.1f}%   •   "
                    f"🛡️ إنقاذ: {self.salvations:,} / تضاعف: {self.multipliers:,}"
                )
            )

    def start_sim(self) -> None:
        if self.is_running:
            return
        try:
            n = int(self.battles_var.get().replace(",", "").strip())
            if n < 1 or n > 1_000_000:
                raise ValueError()
        except ValueError:
            messagebox.showerror("عدد غير صالح", "يرجى إدخال عدد معارك بين 1 و 1,000,000.")
            return

        self.reset_stats()
        self.target_matches = n
        self.is_running = True
        self.stop_requested = False
        self.sim_start_time = time.perf_counter()
        self.progress["value"] = 0
        self.start_btn.config(state="disabled")
        self.stop_btn.config(state="normal")

        workers = max(1, int(self.workers_var.get()))
        endless = bool(self.endless_var.get())
        seed_base = int(time.time()) & 0xFFFFFF

        t = threading.Thread(
            target=self._run_pool_thread,
            args=(n, workers, endless, seed_base),
            daemon=True,
        )
        t.start()

    def stop_sim(self) -> None:
        self.stop_requested = True
        self.stop_btn.config(state="disabled")

    def _run_pool_thread(self, total: int, workers: int, endless: bool, seed_base: int) -> None:
        chunk_sz = max(50, min(1500, total // max(1, workers * 12)))
        tasks = []
        rem = total
        cid = 0
        while rem > 0:
            cur = min(chunk_sz, rem)
            tasks.append((cid, cur))
            rem -= cur
            cid += 1

        try:
            with mp.Pool(
                processes=workers,
                initializer=_init_worker,
                initargs=(self.json_path, endless, 50, seed_base),
            ) as pool:
                for res in pool.imap_unordered(_run_chunk, tasks):
                    self.ui_queue.put(("chunk", res))
                    if self.stop_requested:
                        pool.terminate()
                        break
        except Exception as exc:
            self.ui_queue.put(("error", str(exc)))
        finally:
            self.ui_queue.put(("done", None))

    def _merge_dict(self, dst: Dict[int, int], src: Dict[int, int]) -> None:
        for k, v in src.items():
            dst[k] = dst.get(k, 0) + v

    def _poll_queue(self) -> None:
        dirty = False
        try:
            while True:
                kind, payload = self.ui_queue.get_nowait()
                if kind == "chunk":
                    self.done_matches += payload["n"]
                    self.w1 += payload["w1"]
                    self.w2 += payload["w2"]
                    self.draws += payload["draws"]
                    self.total_turns += payload["turns"]
                    self.total_waves += payload["waves"]
                    self.total_comebacks += payload["comebacks"]
                    self.half_turns += payload["half_turns"]
                    self.dead_turns += payload["dead_turns"]
                    self.stall_turns += payload["stall_turns"]
                    self.explosive_turns += payload["explosive_turns"]
                    self.lead_changes += payload["lead_changes"]
                    self.comeback_wins += payload["comeback_wins"]
                    self.nail_biters += payload["nail_biters"]
                    self.blowouts += payload["blowouts"]
                    self.salvations += payload["salvations"]
                    self.multipliers += payload["multipliers"]
                    self._merge_dict(self.pm, payload["pm"])
                    self._merge_dict(self.pw, payload["pw"])
                    self._merge_dict(self.nmm, payload["nmm"])
                    self._merge_dict(self.nmw, payload["nmw"])
                    self._merge_dict(self.dm, payload["dm"])
                    self._merge_dict(self.dw, payload["dw"])
                    self._merge_dict(self.dmg, payload["dmg"])
                    self._merge_dict(self.kills, payload["kills"])
                    self._merge_dict(self.cexcite, payload["cexcite"])
                    self._merge_dict(self.cstall, payload["cstall"])
                    dirty = True
                elif kind == "error":
                    messagebox.showerror("خطأ في المحاكاة", str(payload))
                elif kind == "done":
                    self.is_running = False
                    self.start_btn.config(state="normal")
                    self.stop_btn.config(state="disabled")
                    dirty = True
        except queue.Empty:
            pass

        if dirty:
            pct = (self.done_matches / max(1, self.target_matches)) * 100.0
            self.progress["value"] = min(100.0, pct)
            elapsed = max(0.01, time.perf_counter() - self.sim_start_time) if self.sim_start_time else 0.01
            rate = self.done_matches / elapsed
            rem_sec = max(0.0, (self.target_matches - self.done_matches) / max(1.0, rate))
            state_txt = "مكتمل" if not self.is_running else "جاري التشغيل"
            self.status_lbl.config(
                text=f"{state_txt}: {self.done_matches:,} / {self.target_matches:,} ({pct:.1f}%)  •  السرعة: {rate:,.0f} معركة/ث  •  المتبقي: {rem_sec:.1f} ث"
            )
            self.refresh_table()

        self.after(150, self._poll_queue)

    def run_sample_match(self) -> None:
        eng = BattleEngine(self.by_id, self.monsters, self.spells, self.envs, endless_waves=bool(self.endless_var.get()))
        rng = random.Random()
        _, _, _, _, log, _ = eng.play_match(rng, max_turns=80, verbose=True)

        win = tk.Toplevel(self)
        win.title("🔍 سجل معركة تجريبية مباشر بالقواعد الكاملة")
        win.geometry("820x580")
        win.configure(bg="#0F172A")
        txt = scrolledtext.ScrolledText(win, bg="#0B1220", fg="#E2E8F0", font=("Consolas", 10))
        txt.pack(fill="both", expand=True, padx=10, pady=10)
        txt.insert("1.0", "\n".join(log))
        txt.config(state="disabled")

    _EXPORT_HEADERS = [
        "المعرّف", "النوع", "العنصر", "الدور", "الفئة", "التكلفة", "الهجوم", "الدفاع",
        "المهارات الكامنة", "المهارات النشطة", "إجمالي المشاركات", "مباريات بدون مرآة", "انتصارات بدون مرآة",
        "نسبة الفوز %", "نسبة الفوز الكلية %", "مرات السحب", "نسبة الفوز عند السحب %",
        "متوسط الضرر", "إجمالي القتلى", "زخم_الحماس_%", "جمود_وتعطل_%", "حالة التوازن"
    ]

    def _collect_export_rows(self) -> List[tuple]:
        out = []
        for cid in sorted(self.by_id.keys()):
            proto = self.by_id[cid]
            plays = self.nmm.get(cid, 0)
            wins = self.nmw.get(cid, 0)
            all_p = self.pm.get(cid, 0)
            all_w = self.pw.get(cid, 0)
            dr = self.dm.get(cid, 0)
            dr_w = self.dw.get(cid, 0)
            wr = (wins / plays * 100.0) if plays > 0 else 50.0
            all_wr = (all_w / all_p * 100.0) if all_p > 0 else 50.0
            dr_wr = (dr_w / dr * 100.0) if dr > 0 else 50.0
            avg_d = (self.dmg.get(cid, 0) / all_p) if all_p > 0 else 0.0
            kills = self.kills.get(cid, 0)
            ex_rate = (self.cexcite.get(cid, 0) / max(1, all_p)) * 100.0 if all_p > 0 else 0.0
            st_rate = (self.cstall.get(cid, 0) / max(1, max(all_p, dr))) * 100.0 if (all_p > 0 or dr > 0) else 0.0
            st_lbl, _ = self._classify(plays, wr)
            out.append((
                cid, TYPE_AR.get(proto.card_type, proto.card_type), ELEM_AR.get(proto.element, proto.element),
                ROLE_AR.get(proto.role, proto.role), proto.tier_num, proto.cost,
                proto.atk, proto.def_stat, " ; ".join(proto.passives), " ; ".join(proto.actives),
                all_p, plays, wins, round(wr, 2), round(all_wr, 2), dr, round(dr_wr, 2),
                round(avg_d, 2), kills, round(ex_rate, 2), round(st_rate, 2), st_lbl
            ))
        return out

    def export_csv(self) -> None:
        os.makedirs(DEFAULT_EXPORT_DIR, exist_ok=True)
        path = filedialog.asksaveasfilename(
            initialdir=DEFAULT_EXPORT_DIR,
            initialfile=f"card_winrates_{self.done_matches}.csv",
            defaultextension=".csv",
            filetypes=[("ملفات CSV", "*.csv")],
        )
        if not path:
            return
        with open(path, "w", newline="", encoding="utf-8-sig") as f:
            w = csv.writer(f)
            w.writerow(self._EXPORT_HEADERS)
            w.writerows(self._collect_export_rows())
        messagebox.showinfo("تم التصدير", f"تم حفظ تقرير CSV في:\n{path}")

    def export_excel(self) -> None:
        if not HAS_OPENPYXL:
            messagebox.showwarning("مكتبة openpyxl غير موجودة", "مكتبة openpyxl غير مثبّتة. استخدم تصدير CSV بدلاً منها.")
            return
        os.makedirs(DEFAULT_EXPORT_DIR, exist_ok=True)
        path = filedialog.asksaveasfilename(
            initialdir=DEFAULT_EXPORT_DIR,
            initialfile=f"card_winrates_{self.done_matches}.xlsx",
            defaultextension=".xlsx",
            filetypes=[("ملفات Excel", "*.xlsx")],
        )
        if not path:
            return
        wb = openpyxl.Workbook()
        ws = wb.active
        ws.title = "نسب فوز البطاقات"
        ws.append(self._EXPORT_HEADERS)
        fill_hdr = PatternFill("solid", fgColor="0F172A")
        font_hdr = Font(bold=True, color="38BDF8")
        for col_idx in range(1, len(self._EXPORT_HEADERS) + 1):
            cell = ws.cell(1, col_idx)
            cell.fill = fill_hdr
            cell.font = font_hdr
            cell.alignment = Alignment(horizontal="center")

        for row in self._collect_export_rows():
            ws.append(list(row))

        ws.freeze_panes = "A2"
        wb.save(path)
        messagebox.showinfo("تم التصدير", f"تم حفظ تقرير Excel في:\n{path}")


if __name__ == "__main__":
    mp.freeze_support()
    app = SimulatorApp()
    app.mainloop()


"""
Genius Full-Rules Unity-Exact TCG Battle Simulator & Balance Analytics Suite
===========================================================================
100% faithful replication of UnityGame/Assets/Scripts/Core/*:
  - Full 470-card catalog (240 Monsters + 200 Spells + 10 Environments + 20 Commander Spirits [451-470])
  - Exact 50-card deck ratios: 3 Commander Spirits + 27 Monsters (35% early / 40% mid / 25% late) + 17 Spells + 3 Environments
  - Exact PlayerState weighted stochastic draw + Endless Wave (~22% Fallen Veterans comeback + fresh unseen pity)
  - Exact Hand Cap = 7 with burn-oldest (`hand.pop(0) -> graveyard`) on Draw, Revive, Tidal Bounce, Sweeping Cyclone
  - Exact Environment Levels 1 / 2 / 3 (Dur 4/2/1, +30%/+40%/+70% buff, -20%/-30%/-50% counter, Cantrip +1 draw)
  - Exact all 20 Commander Spirits (IDs 451-470) across all 12 C# hooks (clan-strict ally buffs & rival debuffs)
  - Exact 2-Active Skill loop on boss monsters, exact Weakened (*0.70), Targeting System (+15% before Spirit aura),
    HealDefense (cap base_def+20), TrySalvationSave (>12/-10->10), TakeDamage bubble_shield on thorns, HasPassiveSubstr silence check
  - Selectable Start Modes (fair_standard / sym_spirit_real / sym_spirit_free / unity_debug_exact) & AI Policies (smart_tactical / unity_ai_exact)
  - Genius Diagnostics: Wilson 95% CIs, Z-Scores, Cost-Tier Residual, Cosmetic-Passive Detection, Environment & Spirit Lift,
    Excitement vs Boredom (Brick/Stall/Dead/Burn) Turn Curves, Optimal Turn Window Solver, 4-Tab GUI + Excel (.xlsx) + Interactive HTML + CLI
"""

from __future__ import annotations

import argparse
import csv
import datetime as dt
import html as html_lib
import json
import math
import multiprocessing as mp
import os
import queue
import random
import threading
import time
import tkinter as tk
from dataclasses import dataclass
from tkinter import filedialog, messagebox, scrolledtext, ttk
from typing import Any, Callable, Dict, List, Optional, Tuple

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

MAX_HAND_SIZE = 7
MAX_TURN_BIN = 30
MAX_MATCH_TURNS = 80

ELEMENTS: Tuple[str, ...] = (
    "Fire", "Water", "Ice", "Earth", "Electric",
    "Air", "Nature", "Light", "Dark", "Arcane",
)

TYPE_AR: Dict[str, str] = {
    "monster": "وحش",
    "spell": "تعويذة",
    "env": "بيئة",
    "leader": "روح قائد",
}
TYPE_FROM_AR: Dict[str, str] = {v: k for k, v in TYPE_AR.items()}

ELEM_AR: Dict[str, str] = {
    "Fire": "نار", "Water": "ماء", "Ice": "جليد", "Earth": "أرض",
    "Electric": "كهرباء", "Air": "هواء", "Nature": "طبيعة",
    "Light": "نور", "Dark": "ظلام", "Arcane": "غامض",
}
ELEM_FROM_AR: Dict[str, str] = {v: k for k, v in ELEM_AR.items()}

TRIBES: Tuple[str, ...] = (
    "Beasts", "Ghosts", "Dwarves", "Elves", "Warriors",
    "Giants", "Insects", "Mages", "Demons", "Celestials",
)
TRIBE_AR: Dict[str, str] = {
    "Beasts": "وحوش برية", "Ghosts": "أشباح", "Dwarves": "أقزام",
    "Elves": "إلف", "Warriors": "محاربون", "Giants": "عمالقة",
    "Insects": "حشرات", "Mages": "سحرة", "Demons": "شياطين",
    "Celestials": "سماويون",
}
TRIBE_FROM_AR: Dict[str, str] = {v: k for k, v in TRIBE_AR.items()}

ROLE_AR: Dict[str, str] = {
    "Attacker": "مهاجم",
    "Defender": "مدافع",
    "Support": "دعم",
    "Pure Shield": "درع صافي",
    "Balanced": "متوازن",
    "Environment": "بيئة",
    "Leader": "قائد عشيرة",
}

START_MODES: Dict[str, str] = {
    "fair_standard": "القواعد العادلة الحالية (5 مقابل 6 + تعويض T2 + الأرواح في الدك)",
    "sym_spirit_real": "توازن مع ضمان روح قائد في الافتتاحية للطرفين (بتكلفتها الطبيعية)",
    "sym_spirit_free": "بداية مع روح قائد مجانية (Cost 0) للطرفين في اليد الافتتاحية",
    "unity_debug_exact": "وضع الاختبار الحالي داخل GameManager.cs (P1: باور 10+روح0+3 جليد | P2: باور 5)",
}

AI_POLICIES: Dict[str, str] = {
    "smart_tactical": "الذكاء التكتيكي المحسّن (أفضل قرار متاح)",
    "unity_ai_exact": "ذكاء Unity الحالي حرفياً (AIOpponent.cs: التعاويذ قبل الاستدعاء)",
}

# Exact matrix from EnvironmentData.cs:69-130
ENV_MATRIX: Dict[str, Tuple[str, frozenset]] = {
    "Fire": ("Fire", frozenset({"Nature", "Ice", "Dark"})),
    "Water": ("Water", frozenset({"Fire", "Earth", "Light"})),
    "Ice": ("Ice", frozenset({"Water", "Nature", "Air"})),
    "Earth": ("Earth", frozenset({"Fire", "Electric", "Ice"})),
    "Electric": ("Electric", frozenset({"Water", "Air", "Arcane"})),
    "Air": ("Air", frozenset({"Earth", "Fire", "Dark"})),
    "Nature": ("Nature", frozenset({"Earth", "Water", "Electric"})),
    "Light": ("Light", frozenset({"Dark", "Ice", "Arcane"})),
    "Dark": ("Dark", frozenset({"Light", "Nature", "Arcane"})),
    "Arcane": ("Arcane", frozenset({"Light", "Electric", "Air"})),
}

# EnvironmentCardData.SetLevelStats (dur, buff_pct, debuff_pct)
ENV_LEVEL_STATS: Dict[int, Tuple[int, float, float]] = {
    1: (4, 0.30, 0.20),
    2: (2, 0.40, 0.30),
    3: (1, 0.70, 0.50),
}

SHIELD_SLOTS = (2, 3, 0, 5, 1, 4)
ATTACK_SLOTS = (0, 1, 4, 5, 2, 3)
BALANCED_SLOTS = (2, 3, 1, 4, 0, 5)
FLANK_SLOTS = (0, 1, 4, 5)
CENTER_SLOTS = (2, 3)

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

SPELL_TYPE_NAMES: Dict[int, str] = {
    10: "هجوم رابط الأجنحة", 11: "نسخ كوامن الحليف", 12: "ربط وتعزيز المركز",
    13: "درع التناغم العنصري", 14: "تبديل الجناح بالمركز", 15: "سحب العدو للجناح",
    16: "إزاحة العدو لفتح مسار", 17: "انتقال خاطف للأضعف", 18: "تحويل الدفاع لهجوم",
    19: "توزيع طاقة الدرع", 20: "عكس/تفكيك دفاع العدو", 21: "امتصاص دفاع العدو",
    22: "تصفير تكلفة المهارة + تعزيز", 23: "ضربة مزدوجة للمركز", 24: "تطهير وتحصين اللوحة",
    25: "شحن المانا وعلاج القائد", 26: "قذيفة خارقة للدروع", 27: "سحب ورقتين وضربة",
    28: "إحياء من المقبرة وعلاج", 29: "سرقة الباور والمانا",
}

PASSIVE_KEYS: Tuple[str, ...] = (
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

# Passives existing in JSON cards that currently have NO code handler inside Unity CombatSystem.cs
COSMETIC_UNIMPLEMENTED_PASSIVES = frozenset({
    "Dark Resistance", "Earth Resistance", "Electric Resistance", "Wind Resistance",
    "Stone Roots", "Immunity to Air", "Immunity to Earth", "Immunity to Light",
    "Immunity to Water",
})

ACTIVE_REQUIRES_ENEMY_EXEMPT: Tuple[str, ...] = (
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
    mapping = (
        ("combine left and right", ST_LINK_WINGS_STRIKE),
        ("copy your support", ST_COPY_SUPPORT_PASSIVE),
        ("link both center", ST_LINK_CENTER_TWIN),
        ("elemental synergy", ST_ELEMENTAL_SYNERGY_SHIELD),
        ("swap a wounded flank", ST_SWAP_WING_CENTER),
        ("forcefully pull", ST_PULL_CENTER_WING),
        ("displace an enemy", ST_PUSH_CENTER_OPEN),
        ("teleport your attacker", ST_TELEPORT_WEAKEST),
        ("distribute shield energy", ST_DIST_SHIELD_WINGS),
        ("invert the opposite enemy", ST_INVERT_ENEMY_STATS),
        ("reduce your monster's active skill cost", ST_ZERO_SKILL_BOOST),
        ("strike twice", ST_DOUBLE_STRIKE_CENTER),
        ("cleanse all curses", ST_CLEANSE_FORTIFY),
        ("overcharge your support", ST_SUPPORT_BATTERY_HEAL),
        ("arcane piercing projectile", ST_ARCANE_PIERCING_SHOT),
        ("revive the strongest", ST_REVIVE_GRAVEYARD),
    )
    for k, val in mapping:
        if k in sk:
            return val
    if "convert" in sk and "def from your defender" in sk:
        return ST_CONVERT_DEF_ATK
    if "siphon" in sk and "def" in sk:
        return ST_SIPHON_DEF_ATK
    if "draw" in sk and "extra cards" in sk:
        return ST_DRAW_TWO_STRIKE
    if "steal" in sk and ("mana cap" in sk or "power" in sk):
        return ST_MANA_STEAL_AOE
    return raw_type if raw_type >= 10 else ST_ARCANE_PIERCING_SHOT


def parse_active_costs(act: str, card_pwr_cost: int = 0) -> Tuple[int, int, bool]:
    """Mirrors CombatSystem.GetActiveSkillCost base cost, GetActiveSkillPowerCost, and SkillRequiresEnemyMonster."""
    if not act:
        return 0, 0, False
    pwr = card_pwr_cost
    if pwr == 0:
        if any(k in act for k in ("(Requires 3 Power)", "(Cost 3 Permanent Power)", "(Spend 3 Power)", "(Drain 3 Power)", "يتطلب 3 باور", "يستهلك 3 باور")):
            pwr = 3
        elif any(k in act for k in ("(Requires 2 Power)", "(Cost 2 Permanent Power)", "(Spend 2 Power)", "(Drain 2 Power)", "يتطلب 2 باور", "يستهلك 2 باور")):
            pwr = 2
        elif any(k in act for k in ("(Requires 1 Power)", "(Cost 1 Permanent Power)", "(Spend 1 Power)", "(Drain 1 Power)", "يتطلب 1 باور", "يستهلك 1 باور")):
            pwr = 1
    has_pwr_only = (pwr > 0 and "mana" not in act.lower())
    if has_pwr_only:
        base_mana = 0
    elif "(Cost 3 Mana)" in act or "(Cost 3 Power)" in act:
        base_mana = 3
    elif "(Cost 2 Mana)" in act or "(Cost 2 Power)" in act:
        base_mana = 2
    else:
        base_mana = 1
    req_enemy = not any(k in act for k in ACTIVE_REQUIRES_ENEMY_EXEMPT)
    return base_mana, pwr, req_enemy


def extract_passive_tags(passives: List[str]) -> Tuple[frozenset, bool]:
    tags = set()
    has_cosmetic = False
    for p in passives:
        for key in PASSIVE_KEYS:
            if key in p:
                tags.add(key)
        if "Frost" in p:
            tags.add("Frost")
        pname = p.split(":")[0].strip()
        if pname in COSMETIC_UNIMPLEMENTED_PASSIVES:
            has_cosmetic = True
    return frozenset(tags), has_cosmetic


@dataclass(frozen=True)
class CardProto:
    cid: int
    name: str
    card_type: str  # "monster", "spell", "env", "leader"
    element: str
    tribe: str
    role: str
    tier_num: int
    cost: int
    atk: int
    def_stat: int
    passives: Tuple[str, ...]
    passive_tags: frozenset
    has_cosmetic_passive: bool
    actives: Tuple[str, ...]
    active_meta: Tuple[Tuple[int, int, bool], ...]  # per-active (base_mana_cost, power_cost, req_enemy)
    spell_type: int
    spell_power: int
    summary_text: str
    env_level: int = 1


def load_database(json_path: str) -> Tuple[Dict[int, CardProto], List[CardProto], List[CardProto], List[CardProto], List[CardProto]]:
    with open(json_path, "r", encoding="utf-8") as f:
        raw = json.load(f)
    raw_cards = raw.get("cards", raw) if isinstance(raw, dict) else raw

    by_id: Dict[int, CardProto] = {}
    monsters: List[CardProto] = []
    spells: List[CardProto] = []
    envs: List[CardProto] = []
    leaders: List[CardProto] = []

    for c in raw_cards:
        cid = int(c["id"])
        ctype = (c.get("card_type") or "monster").lower()
        name = str(c.get("name") or cid)
        elem = c.get("element", "Fire")
        if elem == "Forest":
            elem = "Nature"
        tribe = c.get("tribe", "") or ""
        role = c.get("role", "") or ("Leader" if ctype == "leader" else "Environment" if ctype == "env" else "")
        tier_num = int(c.get("tier_num", 1))
        cost = int(c.get("cost", 1))
        atk = int(c.get("atk", 0))
        def_stat = int(c.get("def", 0))

        if ctype == "monster":
            plist = tuple(c.get("passives") or ([] if not c.get("passives_text") else [c["passives_text"]]))
            alist = tuple(c.get("active_skills") or ([] if not c.get("active_skill") else [c["active_skill"]]))
            ptags, has_cosm = extract_passive_tags(list(plist))
            raw_pwr_cost = int(c.get("powerCost", 0) or 0)
            ameta = tuple(parse_active_costs(a, raw_pwr_cost) for a in alist)
            p_short = " + ".join(p.split(":")[0].strip() for p in plist) if plist else "-"
            a_short = " + ".join(a.split(":")[0].strip() for a in alist) if alist else "-"
            summary = f"{p_short} | {a_short}"
            proto = CardProto(
                cid, f"#{cid}", "monster", elem, tribe, role, tier_num, cost, atk, def_stat,
                plist, ptags, has_cosm, alist, ameta, 0, 0, summary, 1
            )
            by_id[cid] = proto
            monsters.append(proto)
        elif ctype == "spell":
            st = infer_spell_type(c.get("active_skill", ""), int(c.get("spellType", 0)))
            sp = max(10, int(c.get("spellPower", 12)))
            summary = (c.get("active_skill") or c.get("description") or "")[:72]
            proto = CardProto(
                cid, f"#{cid}", "spell", elem, "", role, tier_num, cost, 0, 0,
                (), frozenset(), False, (), (), st, sp, summary, 1
            )
            by_id[cid] = proto
            spells.append(proto)
        elif ctype == "leader":
            desc = (c.get("active_skill") or c.get("description") or "")[:82]
            proto = CardProto(
                cid, name, "leader", elem, tribe, "Leader", tier_num, cost, 0, 0,
                (), frozenset(), False, (), (), 0, 0, desc, 1
            )
            by_id[cid] = proto
            leaders.append(proto)
        else:
            env_elem = elem
            if env_elem not in ENV_MATRIX:
                for el in ELEMENTS:
                    if el.lower() in (name + " " + elem).lower():
                        env_elem = el
                        break
                else:
                    env_elem = ELEMENTS[(cid - 441) % len(ELEMENTS)]
            proto = CardProto(
                cid, name, "env", env_elem, "", "Environment", 3, cost, 0, 0,
                (), frozenset(), False, (), (), 0, 0,
                f"بيئة {ELEM_AR.get(env_elem, env_elem)} (L1/L2/L3 تصاعدي + سحب ورقة)", 1
            )
            by_id[cid] = proto
            envs.append(proto)

    return by_id, monsters, spells, envs, leaders


TOKEN_TREANT = CardProto(
    9998, "Treant", "monster", "Nature", "Giants", "Defender", 2, 2, 6, 30,
    ("Poison Thorns: Deal 3 damage to attacking monster",),
    frozenset({"Poison Thorns"}), False, (), (), 0, 0, "Token Treant", 1
)
TOKEN_GHOST = CardProto(
    9999, "Ghost", "monster", "Dark", "Ghosts", "Attacker", 2, 2, 12, 14,
    ("Ethereal Body: 35% immune to normal monster strikes",),
    frozenset({"Ethereal Body"}), False, (), (), 0, 0, "Token Ghost", 1
)
ICE_DEBUG_ENVS: Tuple[CardProto, ...] = (
    CardProto(4431, "Ice Env L1", "env", "Ice", "", "Environment", 3, 0, 0, 0, (), frozenset(), False, (), (), 0, 0, "Ice L1 Test", 1),
    CardProto(4432, "Ice Env L2", "env", "Ice", "", "Environment", 3, 0, 0, 0, (), frozenset(), False, (), (), 0, 0, "Ice L2 Test", 2),
    CardProto(4433, "Ice Env L3", "env", "Ice", "", "Environment", 3, 0, 0, 0, (), frozenset(), False, (), (), 0, 0, "Ice L3 Test", 3),
)


class SimMonster:
    __slots__ = (
        "proto", "slot", "base_atk", "bonus_atk", "base_def", "cur_def",
        "summon_turn", "can_attack", "double_strike", "frozen_turns", "silenced_turns",
        "weakened_turns", "burn_dmg", "poison_dmg", "salvation_used", "bubble_shield",
        "used_active_turn", "extra_passives", "damage_dealt", "kills"
    )

    def __init__(self, proto: CardProto, slot: int, turn_num: int):
        self.proto = proto
        self.slot = slot
        self.base_atk = proto.atk
        self.bonus_atk = 0
        self.base_def = proto.def_stat
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
        self.extra_passives: Optional[set] = None
        self.damage_dealt = 0
        self.kills = 0

    @property
    def is_alive(self) -> bool:
        return self.cur_def > 0

    def has_p(self, tag: str) -> bool:
        """Mirrors MonsterInstance.HasPassiveSubstr: returns False if silencedTurns > 0."""
        if self.silenced_turns > 0:
            return False
        if tag in self.proto.passive_tags:
            return True
        return self.extra_passives is not None and tag in self.extra_passives

    @property
    def has_steadfast(self) -> bool:
        return self.has_p("Earth Steadfastness")

    def add_bonus_def(self, amount: int) -> None:
        if amount <= 0:
            return
        self.base_def += amount
        self.cur_def += amount

    def heal_def(self, amount: int) -> None:
        """Mirrors MonsterInstance.HealDefense: caps at baseDef + 20."""
        if amount <= 0:
            return
        self.cur_def = min(self.base_def + 20, self.cur_def + amount)

    def take_damage(self, dmg: int) -> int:
        """Mirrors MonsterInstance.TakeDamage: Bubble Shield absorbs hit cleanly. Returns raw dmg applied (0 if absorbed)."""
        if dmg <= 0:
            return 0
        if self.bubble_shield:
            self.bubble_shield = False
            return 0
        self.cur_def = max(0, self.cur_def - dmg)
        return dmg

    def reset_turn(self, turn_num: int) -> None:
        """Mirrors MonsterInstance.ResetTurnReadiness."""
        if self.is_alive:
            self.used_active_turn = False
            self.double_strike = False
            if turn_num > self.summon_turn and self.frozen_turns <= 0:
                self.can_attack = True

    def effective_atk(self, active_env: Optional[str], env_level: int, owner: "SimPlayer") -> int:
        """Mirrors MonsterInstance.GetEffectiveAtk exact order."""
        total = self.base_atk + self.bonus_atk
        if active_env is not None:
            buffed, debuffed = ENV_MATRIX[active_env]
            _, buff_pct, debuff_pct = ENV_LEVEL_STATS.get(env_level, (4, 0.30, 0.20))
            if self.proto.element == buffed:
                total = max(0, int(round(total * (1.0 + buff_pct))))
            elif self.proto.element in debuffed:
                mod = max(0.2, 1.0 - debuff_pct)
                total = max(0, int(round(total * mod)))
        if self.weakened_turns > 0:
            total = int(round(total * 0.70))
        if self.has_p("Targeting System"):
            total += max(2, int(round(total * 0.15)))
        total += CommanderSpiritEffects.get_continuous_atk_bonus(owner, self)
        return max(0, total)


class SimPlayer:
    __slots__ = (
        "name", "hp", "max_hp", "shield", "power", "mana", "sacrificed_turn",
        "deny_turns", "team_immune_turns", "free_skill_bonus", "active_leader",
        "deck", "hand", "graveyard", "slots",
        "drawn_ids", "fallen_deaths", "fallen_turn", "revived_counts",
        "turns_waiting", "non_monster_streak", "monster_streak", "last_elem", "wave_num",
        "played_ids", "drawn_ever_ids", "card_dmg", "card_kills", "comebacks",
        "salvations", "multipliers", "turn_deaths", "turn_clutch", "card_excite", "card_stall",
        "sacrificed_cnt", "burned_cnt", "turn_burns", "hand_burns_total", "fatigue_cnt",
        "env_played_cnt", "env_played_elems", "spirit_summons", "spirit_summon_allies",
        "spirit_ge2_allies", "total_dmg_dealt", "turn12_dmg", "t2_bonus_used", "turn_kills"
    )

    def __init__(self, name: str):
        self.name = name
        self.hp = 500
        self.max_hp = 500
        self.shield = 0
        self.power = 0
        self.mana = 0
        self.sacrificed_turn = False
        self.deny_turns = 0
        self.team_immune_turns = 0
        self.free_skill_bonus = 0
        self.active_leader: Optional[CardProto] = None
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

        # Telemetry for all 470 catalog IDs (< 9000 skips synthetic tokens/test cards >= 4000 unless catalog)
        self.played_ids: set = set()
        self.drawn_ever_ids: set = set()
        self.card_dmg: Dict[int, int] = {}
        self.card_kills: Dict[int, int] = {}
        self.comebacks = 0
        self.salvations = 0
        self.multipliers = 0
        self.turn_deaths = 0
        self.turn_kills = 0
        self.turn_clutch = False
        self.card_excite: Dict[int, int] = {}
        self.card_stall: Dict[int, int] = {}
        self.sacrificed_cnt: Dict[int, int] = {}
        self.burned_cnt: Dict[int, int] = {}
        self.turn_burns = 0
        self.hand_burns_total = 0
        self.fatigue_cnt = 0
        self.env_played_cnt = 0
        self.env_played_elems: List[str] = []
        self.spirit_summons: List[Tuple[int, int, int, int]] = []  # (cid, turn, allies_same_tribe, enemies_rival_tribe)
        self.spirit_summon_allies = 0
        self.spirit_ge2_allies = False
        self.total_dmg_dealt = 0
        self.turn12_dmg = 0
        self.t2_bonus_used = False

    def add_card_to_hand(self, card: CardProto, is_catalog_fn) -> None:
        """Mirrors PlayerState.AddCardToHandWithBurnLimit (maxHandSize = 7, burns hand[0] oldest)."""
        while len(self.hand) >= MAX_HAND_SIZE and self.hand:
            burned = self.hand.pop(0)
            if is_catalog_fn(burned.cid):
                self.graveyard.append(burned)
                self.burned_cnt[burned.cid] = self.burned_cnt.get(burned.cid, 0) + 1
            self.turn_burns += 1
            self.hand_burns_total += 1
        self.hand.append(card)

    def take_leader_damage(self, dmg: int, attacker_player: Optional["SimPlayer"] = None) -> int:
        if dmg <= 0 or self.team_immune_turns > 0:
            return 0
        raw_in = dmg
        if self.shield > 0:
            absorbed = min(self.shield, dmg)
            self.shield -= absorbed
            dmg -= absorbed
        if dmg > 0:
            dealt = min(self.hp, dmg)
            self.hp = max(0, self.hp - dmg)
            if attacker_player is not None:
                attacker_player.total_dmg_dealt += dealt
            return dealt
        return 0 if raw_in > 0 else 0

    def heal_leader(self, amount: int, opp: Optional["SimPlayer"]) -> None:
        if amount <= 0:
            return
        if opp is not None:
            for s in opp.slots:
                if s is not None and s.is_alive and s.has_p("Spectral Presence"):
                    return
        self.hp = min(self.max_hp, self.hp + amount)

    def record_death(self, cid: int, turn_num: int) -> None:
        if cid >= 9000:
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


# ==============================================================================
# Commander Spirit Engine (100% Parity with CommanderSpiritEffects.cs)
# ==============================================================================
_CONT_ATK_AURA: Dict[int, Tuple[str, int]] = {
    451: ("Ghosts", 5), 453: ("Demons", 5), 455: ("Celestials", 4),
    457: ("Mages", 5), 458: ("Mages", 4), 459: ("Warriors", 4),
    461: ("Beasts", 4), 464: ("Elves", 4), 465: ("Dwarves", 4),
    466: ("Dwarves", 4), 467: ("Giants", 6), 468: ("Giants", 5),
    469: ("Insects", 4), 470: ("Insects", 4),
}
_ENEMY_DREAD_PENALTY: Dict[int, Tuple[str, int]] = {
    452: ("Ghosts", 3), 456: ("Celestials", 2), 463: ("Elves", 2), 468: ("Giants", 3),
}
_COMBAT_EVADE: Dict[int, Tuple[str, float]] = {
    452: ("Ghosts", 0.22), 458: ("Mages", 0.20), 464: ("Elves", 0.20),
}
_COMBAT_REDUCE: Dict[int, Tuple[str, int]] = {
    455: ("Celestials", 4), 460: ("Warriors", 4), 465: ("Dwarves", 4),
}


class CommanderSpiritEffects:
    @staticmethod
    def _rand_pick(pool: List[SimMonster], count: int, rng: random.Random) -> List[SimMonster]:
        if not pool or count <= 0:
            return []
        if len(pool) <= count:
            return list(pool)
        return rng.sample(pool, count)

    @staticmethod
    def _strongest(slots: List[SimMonster]) -> Optional[SimMonster]:
        return max(slots, key=lambda m: m.base_atk + m.bonus_atk + m.cur_def, default=None)

    @staticmethod
    def _weakest(slots: List[SimMonster]) -> Optional[SimMonster]:
        return min(slots, key=lambda m: m.cur_def, default=None)

    @classmethod
    def apply_on_summoned(
        cls, owner: SimPlayer, opp: SimPlayer, spirit: CardProto,
        turn_num: int, rng: random.Random, engine: "BattleEngine"
    ) -> None:
        sid = spirit.cid
        allies = [s for s in owner.slots if s is not None and s.is_alive]
        enemies = [s for s in opp.slots if s is not None and s.is_alive]

        if sid == 451:  # Valkor (Ghosts)
            dealt = opp.take_leader_damage(12, owner)
            owner.card_dmg[sid] = owner.card_dmg.get(sid, 0) + dealt
            owner.heal_leader(12, opp)
            for es in cls._rand_pick([e for e in enemies if e.proto.tribe != "Ghosts"], 1, rng):
                es.weakened_turns = max(es.weakened_turns, 2)
        elif sid == 452:  # Morghana (Ghosts)
            for es in cls._rand_pick([e for e in enemies if e.proto.tribe != "Ghosts"], 2, rng):
                es.weakened_turns = max(es.weakened_turns, 2)
        elif sid == 453:  # Malakor (Demons)
            if enemies:
                for es in list(enemies):
                    dmg = 10 if es.proto.tribe != "Demons" else 6
                    engine.deal_spell_skill_dmg(es.slot, es, dmg, None, opp, owner, turn_num, spell_cid=sid)
                rem_rivals = [s for s in opp.slots if s is not None and s.is_alive and s.proto.tribe != "Demons"]
                for es in cls._rand_pick(rem_rivals, 2, rng):
                    es.burn_dmg = max(es.burn_dmg, 5)
            else:
                dealt = opp.take_leader_damage(8, owner)
                owner.card_dmg[sid] = owner.card_dmg.get(sid, 0) + dealt
        elif sid == 454:  # Lilitha (Demons)
            owner.shield += 10
            st = cls._strongest([a for a in allies if a.proto.tribe == "Demons"])
            if st is not None:
                st.bonus_atk += 5
                st.add_bonus_def(6)
        elif sid == 455:  # Orion (Celestials)
            owner.shield += 18
            for al in allies:
                if al.proto.tribe == "Celestials":
                    al.add_bonus_def(8)
        elif sid == 456:  # Celestia (Celestials)
            owner.heal_leader(22, opp)
            for al in allies:
                if al.proto.tribe == "Celestials":
                    al.burn_dmg = al.poison_dmg = al.frozen_turns = al.weakened_turns = al.silenced_turns = 0
        elif sid == 457:  # Zarathos (Mages)
            owner.mana += 1
            owner.free_skill_bonus += 4
        elif sid == 458:  # Astrea (Mages)
            top = cls._strongest(enemies)
            if top is not None:
                top.silenced_turns = max(top.silenced_turns, 2)
                if top.proto.tribe != "Mages":
                    top.weakened_turns = max(top.weakened_turns, 1)
        elif sid == 459:  # Garik (Warriors)
            owner.shield += 6
            for al in allies:
                if al.proto.tribe == "Warriors":
                    al.bonus_atk += 5
                    al.add_bonus_def(5)
        elif sid == 460:  # Valeria (Warriors)
            owner.shield += 15
            wk = cls._weakest([a for a in allies if a.proto.tribe == "Warriors"])
            if wk is not None:
                wk.bubble_shield = True
        elif sid == 461:  # Fenrir (Beasts)
            for al in allies:
                if al.proto.tribe == "Beasts":
                    al.bonus_atk += 6
        elif sid == 462:  # Ravika (Beasts)
            for al in allies:
                if al.proto.tribe == "Beasts":
                    al.bonus_atk += 4
                    al.add_bonus_def(4)
            for es in cls._rand_pick([e for e in enemies if e.proto.tribe != "Beasts"], 1, rng):
                es.weakened_turns = max(es.weakened_turns, 2)
        elif sid == 463:  # Sylvandor (Elves)
            owner.mana += 1
            for al in allies:
                if al.proto.tribe == "Elves":
                    al.add_bonus_def(7)
        elif sid == 464:  # Elara (Elves)
            rivals = [e for e in enemies if e.proto.tribe != "Elves"] or list(enemies)
            if rivals:
                for es in cls._rand_pick(rivals, 2, rng):
                    dmg = 9 if es.proto.tribe != "Elves" else 6
                    engine.deal_spell_skill_dmg(es.slot, es, dmg, None, opp, owner, turn_num, spell_cid=sid)
            else:
                dealt = opp.take_leader_damage(8, owner)
                owner.card_dmg[sid] = owner.card_dmg.get(sid, 0) + dealt
        elif sid == 465:  # Thorin (Dwarves)
            owner.shield += 12
            for al in allies:
                if al.proto.tribe == "Dwarves":
                    al.add_bonus_def(7)
        elif sid == 466:  # Brunda (Dwarves)
            for al in allies:
                if al.proto.tribe == "Dwarves":
                    al.frozen_turns = 0
                    al.weakened_turns = 0
                    al.add_bonus_def(7)
        elif sid == 467:  # Kronos (Giants)
            if enemies:
                for es in list(enemies):
                    dmg = 12 if es.proto.tribe != "Giants" else 8
                    engine.deal_spell_skill_dmg(es.slot, es, dmg, None, opp, owner, turn_num, spell_cid=sid)
            else:
                dealt = opp.take_leader_damage(10, owner)
                owner.card_dmg[sid] = owner.card_dmg.get(sid, 0) + dealt
        elif sid == 468:  # Skaya (Giants)
            ft = cls._strongest(enemies)
            if ft is not None:
                turns = 2 if ft.proto.tribe != "Giants" else 1
                ft.frozen_turns = max(ft.frozen_turns, turns)
            for al in allies:
                if al.proto.tribe == "Giants":
                    al.add_bonus_def(7)
        elif sid == 469:  # Chitinus (Insects)
            for es in cls._rand_pick([e for e in enemies if e.proto.tribe != "Insects"], 2, rng):
                es.poison_dmg = max(es.poison_dmg, 5)
            for al in allies:
                if al.proto.tribe == "Insects":
                    al.add_bonus_def(6)
        elif sid == 470:  # Zerilla (Insects)
            for al in allies:
                if al.proto.tribe == "Insects":
                    al.bonus_atk += 6
            for es in cls._rand_pick([e for e in enemies if e.proto.tribe != "Insects"], 1, rng):
                es.poison_dmg = max(es.poison_dmg, 4)

    @staticmethod
    def on_monster_summoned(owner: SimPlayer, m: SimMonster) -> None:
        if owner.active_leader is None:
            return
        sid = owner.active_leader.cid
        t = m.proto.tribe
        if sid == 456 and t == "Celestials":
            m.add_bonus_def(8)
        elif sid == 460 and t == "Warriors":
            m.add_bonus_def(8)
        elif sid == 462 and t == "Beasts":
            m.bonus_atk += 4
            m.add_bonus_def(4)
        elif sid == 465 and t == "Dwarves":
            owner.shield += 4

    @staticmethod
    def on_card_sacrificed(owner: SimPlayer, opp: SimPlayer) -> None:
        if owner.active_leader is None or owner.active_leader.cid != 454:
            return
        for s in owner.slots:
            if s is not None and s.is_alive and s.proto.tribe == "Demons":
                s.bonus_atk += 4
        dealt = opp.take_leader_damage(8, owner)
        owner.card_dmg[454] = owner.card_dmg.get(454, 0) + dealt

    @staticmethod
    def on_spell_cast(owner: SimPlayer, opp: SimPlayer) -> None:
        if owner.active_leader is None or owner.active_leader.cid != 457:
            return
        for s in owner.slots:
            if s is not None and s.is_alive and s.proto.tribe == "Mages":
                s.bonus_atk += 3
        dealt = opp.take_leader_damage(6, owner)
        owner.card_dmg[457] = owner.card_dmg.get(457, 0) + dealt

    @classmethod
    def on_start_of_turn(cls, active: SimPlayer, other: SimPlayer, rng: random.Random) -> None:
        if active.active_leader is None:
            return
        sid = active.active_leader.cid
        if sid == 452:
            active.heal_leader(6, other)
            rivals = [s for s in other.slots if s is not None and s.is_alive and s.proto.tribe != "Ghosts"]
            for es in cls._rand_pick(rivals, 1, rng):
                es.weakened_turns = max(es.weakened_turns, 1)
        elif sid == 455:
            active.heal_leader(8, other)
        elif sid == 456:
            for s in active.slots:
                if s is not None and s.is_alive and s.proto.tribe == "Celestials":
                    s.heal_def(4)
        elif sid == 463:
            active.heal_leader(5, other)
            for s in active.slots:
                if s is not None and s.is_alive and s.proto.tribe == "Elves":
                    s.heal_def(4)

    @staticmethod
    def get_continuous_atk_bonus(owner: SimPlayer, m: SimMonster) -> int:
        if owner.active_leader is None:
            return 0
        rule = _CONT_ATK_AURA.get(owner.active_leader.cid)
        return rule[1] if (rule and m.proto.tribe == rule[0]) else 0

    @staticmethod
    def get_enemy_dread_atk_penalty(defender: SimPlayer, att: SimMonster) -> int:
        if defender.active_leader is None:
            return 0
        rule = _ENEMY_DREAD_PENALTY.get(defender.active_leader.cid)
        return rule[1] if (rule and att.proto.tribe != rule[0]) else 0

    @staticmethod
    def get_skill_mana_discount(owner: SimPlayer, m: SimMonster) -> int:
        if owner.active_leader is None:
            return 0
        return 1 if (owner.active_leader.cid == 458 and m.proto.tribe == "Mages") else 0

    @staticmethod
    def get_combat_evasion_chance(defender: SimPlayer, def_m: SimMonster) -> float:
        if defender.active_leader is None:
            return 0.0
        rule = _COMBAT_EVADE.get(defender.active_leader.cid)
        return rule[1] if (rule and def_m.proto.tribe == rule[0]) else 0.0

    @staticmethod
    def get_combat_incoming_reduction(defender: SimPlayer, def_m: SimMonster) -> int:
        if defender.active_leader is None:
            return 0
        rule = _COMBAT_REDUCE.get(defender.active_leader.cid)
        return rule[1] if (rule and def_m.proto.tribe == rule[0]) else 0

    @staticmethod
    def modify_outgoing_combat_attack(
        att_owner: SimPlayer, att_m: SimMonster, def_m: Optional[SimMonster], atk_power: int, rng: random.Random
    ) -> int:
        if att_owner.active_leader is None:
            return atk_power
        sid = att_owner.active_leader.cid
        t = att_m.proto.tribe
        if sid == 459 and t == "Warriors" and def_m is not None:
            atk_power += 4
        elif sid == 462 and t == "Beasts" and rng.random() < 0.20:
            atk_power = int(round(atk_power * 1.40))
        elif sid == 470 and t == "Insects" and def_m is not None and def_m.poison_dmg > 0:
            atk_power += 4
        return max(0, atk_power)

    @staticmethod
    def on_combat_hit_dealt(
        att_owner: SimPlayer, att_m: SimMonster, def_owner: SimPlayer, def_m: SimMonster, dmg_dealt: int
    ) -> None:
        if att_owner.active_leader is not None:
            asid = att_owner.active_leader.cid
            t = att_m.proto.tribe
            if asid == 453 and t == "Demons":
                def_m.burn_dmg = max(def_m.burn_dmg, 4)
            elif asid == 454 and t == "Demons":
                att_m.heal_def(max(2, int(round(dmg_dealt * 0.20))))
            elif asid == 469 and t == "Insects":
                def_m.poison_dmg = max(def_m.poison_dmg, 4)
            elif asid == 470 and t == "Insects":
                def_m.poison_dmg += 2

        if def_owner.active_leader is not None and att_m.is_alive:
            dsid = def_owner.active_leader.cid
            dt = def_m.proto.tribe
            if dsid == 460 and dt == "Warriors" and att_m.proto.tribe != "Warriors":
                att_m.take_damage(3)
            elif (dsid == 466 and dt == "Dwarves") or (dsid == 468 and dt == "Giants"):
                att_m.take_damage(4)

    @staticmethod
    def on_monster_died(
        dead_owner: SimPlayer, killer_owner: SimPlayer, dead_m: SimMonster, killer_m: Optional[SimMonster],
        p1: SimPlayer, p2: SimPlayer
    ) -> None:
        # Bilateral Valkor (451): CommanderSpiritEffects.cs lines 747-748
        for possessor, enemy in ((p1, p2), (p2, p1)):
            if possessor.active_leader is not None and possessor.active_leader.cid == 451:
                dealt = enemy.take_leader_damage(6, possessor)
                possessor.card_dmg[451] = possessor.card_dmg.get(451, 0) + dealt
                ghosts = [s for s in possessor.slots if s is not None and s.is_alive and s.proto.tribe == "Ghosts"]
                if ghosts:
                    min(ghosts, key=lambda g: g.cur_def).heal_def(5)

        if dead_owner.active_leader is not None and dead_owner.active_leader.cid == 467 and dead_m.proto.tribe == "Giants":
            opp = p2 if dead_owner is p1 else p1
            dealt = opp.take_leader_damage(12, dead_owner)
            dead_owner.card_dmg[467] = dead_owner.card_dmg.get(467, 0) + dealt

        if killer_owner.active_leader is not None and killer_owner.active_leader.cid == 461:
            if killer_m is not None and killer_m.is_alive and killer_m.proto.tribe == "Beasts":
                killer_m.bonus_atk += 4
                killer_m.heal_def(6)


# ==============================================================================
# Core BattleEngine
# ==============================================================================
class BattleEngine:
    def __init__(
        self,
        by_id: Dict[int, CardProto],
        monsters: List[CardProto],
        spells: List[CardProto],
        envs: List[CardProto],
        leaders: List[CardProto],
        endless_waves: bool = True,
        deck_size: int = 50,
        start_mode: str = "fair_standard",
        ai_policy: str = "smart_tactical",
    ):
        self.by_id = by_id
        self.catalog_ids = frozenset(by_id.keys())
        self.monsters = monsters
        self.early_m = [m for m in monsters if m.cost <= 3]
        self.mid_m = [m for m in monsters if 4 <= m.cost <= 5]
        self.late_m = [m for m in monsters if m.cost >= 6]
        self.spells = spells
        self.envs = envs
        self.leaders = leaders
        self.free_leaders = tuple(
            CardProto(
                l.cid, l.name, l.card_type, l.element, l.tribe, l.role, l.tier_num,
                0, 0, 0, (), frozenset(), False, (), (), 0, 0, l.summary_text, 1
            )
            for l in leaders
        )
        self.endless_waves = endless_waves
        self.deck_size = deck_size
        self.start_mode = start_mode
        self.ai_policy = ai_policy
        self._p1: Optional[SimPlayer] = None
        self._p2: Optional[SimPlayer] = None

    def _is_catalog(self, cid: int) -> bool:
        return cid in self.catalog_ids

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
        for c in rng.sample(avail, target):
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

    def generate_deck(self, count: int, exclude_ids: Optional[set], rng: random.Random) -> List[CardProto]:
        """Mirrors GameManager.GenerateDeck (27M + 17S + 3E + 3L for 50)."""
        used = set(exclude_ids) if exclude_ids else set()
        deck: List[CardProto] = []
        leader_t = min(len(self.leaders), 3 if count >= 40 else (2 if count >= 25 else 1)) if (self.leaders and count >= 10) else 0
        m_target = max(0, int(round(count * 0.60)) - leader_t)
        s_target = int(round(count * 0.34))
        e_target = max(0, count - m_target - s_target - leader_t)

        early_t = int(round(m_target * 0.35))
        mid_t = int(round(m_target * 0.40))
        late_t = max(0, m_target - early_t - mid_t)

        pm = self._pick_uniform_unique(self.early_m, early_t, used, deck, rng)
        pm += self._pick_uniform_unique(self.mid_m, mid_t, used, deck, rng)
        pm += self._pick_uniform_unique(self.late_m, late_t, used, deck, rng)
        if pm < m_target:
            self._pick_uniform_unique(self.monsters, m_target - pm, used, deck, rng)
        self._pick_uniform_unique(self.spells, s_target, used, deck, rng)
        self._pick_uniform_unique(self.envs, e_target, used, deck, rng)
        if leader_t > 0:
            self._pick_uniform_unique(self.leaders, leader_t, used, deck, rng)

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

        m_quota = int(round(self.deck_size * 0.60))
        max_fallen = max(1, int(round(m_quota * 0.22)))
        fallen_pool = [m for m in self.monsters if m.cid not in wave_used and player.fallen_deaths.get(m.cid, 0) > 0]
        if fallen_pool:
            self._pick_weighted_unique(
                fallen_pool, min(max_fallen, len(fallen_pool)), wave_used, wave_cards,
                lambda cid: player.fallen_comeback_weight(cid, turn_num) * rng.uniform(0.55, 1.65), rng,
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
            else:
                player.fatigue_cnt += 1
                player.take_leader_damage(player.fatigue_cnt * 30, None)
                return False

        if not player.deck:
            return False

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
                w_wait = 1.0 + player.turns_waiting.get(c.cid, 0) * 0.45
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
                    if c.card_type == "monster" and c.tier_num >= 4 and player.power >= 5:
                        w_pat *= 1.0 + min(0.75, (player.power - 4) * 0.12)

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

        was_fallen = card.card_type == "monster" and card.cid in player.drawn_ids and player.fallen_deaths.get(card.cid, 0) > 0
        if was_fallen:
            player.revived_counts[card.cid] = player.revived_counts.get(card.cid, 0) + 1
            player.comebacks += 1

        player.drawn_ids.add(card.cid)
        if self._is_catalog(card.cid):
            player.drawn_ever_ids.add(card.cid)
        player.last_elem = card.element
        if card.card_type == "monster":
            player.non_monster_streak = 0
            player.monster_streak += 1
        else:
            player.non_monster_streak += 1
            player.monster_streak = 0

        player.add_card_to_hand(card, self._is_catalog)
        if log is not None:
            tag = " [عودة مخضرم!]" if was_fallen else ""
            log.append(f"  + {player.name} سحب {card.name} ({TYPE_AR.get(card.card_type, card.card_type)}، تكلفة {card.cost}){tag}")
        return True

    def try_salvation(self, owner: SimPlayer, slot: int, m: SimMonster) -> bool:
        """Mirrors CombatSystem.TrySalvationSave: self -> 10 DEF; adj > 12 loses 10 DEF, ally -> 10 DEF."""
        if not m.salvation_used and m.has_p("Shield of Salvation"):
            m.salvation_used = True
            m.cur_def = 10
            owner.salvations += 1
            owner.turn_clutch = True
            return True
        for adj in (slot - 1, slot + 1):
            if 0 <= adj < 6:
                ally = owner.slots[adj]
                if ally is not None and not ally.salvation_used and ally.has_p("Shield of Salvation") and ally.cur_def > 12:
                    ally.salvation_used = True
                    ally.cur_def -= 10
                    m.cur_def = 10
                    owner.salvations += 1
                    owner.turn_clutch = True
                    return True
        return False

    def handle_death(
        self, owner: SimPlayer, killer_owner: SimPlayer, slot: int,
        dead_m: SimMonster, killer_m: Optional[SimMonster], turn_num: int
    ) -> bool:
        if self.try_salvation(owner, slot, dead_m):
            return False

        owner.turn_deaths += 1
        killer_owner.turn_kills += 1
        if 0 <= slot < 6 and owner.slots[slot] is dead_m:
            owner.slots[slot] = None

        if self._is_catalog(dead_m.proto.cid):
            owner.graveyard.append(dead_m.proto)
            owner.record_death(dead_m.proto.cid, turn_num)

        if killer_m is not None and self._is_catalog(killer_m.proto.cid):
            killer_owner.card_kills[killer_m.proto.cid] = killer_owner.card_kills.get(killer_m.proto.cid, 0) + 1

        if dead_m.silenced_turns <= 0:
            has_t = lambda k: (k in dead_m.proto.passive_tags) or (dead_m.extra_passives is not None and k in dead_m.extra_passives)
            if has_t("Thermal Explosion"):
                opp = killer_owner.slots[slot] if 0 <= slot < 6 else None
                if opp is not None:
                    opp.take_damage(10)
                    if not opp.is_alive and not self.try_salvation(killer_owner, slot, opp):
                        if self._is_catalog(opp.proto.cid):
                            killer_owner.graveyard.append(opp.proto)
                            killer_owner.record_death(opp.proto.cid, turn_num)
                        killer_owner.slots[slot] = None
                else:
                    dealt = killer_owner.take_leader_damage(10, owner)
                    if self._is_catalog(dead_m.proto.cid):
                        owner.card_dmg[dead_m.proto.cid] = owner.card_dmg.get(dead_m.proto.cid, 0) + dealt
            if has_t("Resurrection Ashes"):
                owner.heal_leader(20, killer_owner)
            if has_t("Inner Quake"):
                dealt = killer_owner.take_leader_damage(10, owner)
                if self._is_catalog(dead_m.proto.cid):
                    owner.card_dmg[dead_m.proto.cid] = owner.card_dmg.get(dead_m.proto.cid, 0) + dealt
            if has_t("Scrap Recycling"):
                owner.mana += 2
                owner.power += 1
            if has_t("Curse of Doom") and killer_m is not None and killer_m.is_alive:
                if not killer_m.has_p("Immunity to Dark") and not killer_m.has_p("Total Immunity to Ghosts and Dark"):
                    killer_m.cur_def = 0
                    for ks in range(6):
                        if killer_owner.slots[ks] is killer_m:
                            if not self.try_salvation(killer_owner, ks, killer_m):
                                if self._is_catalog(killer_m.proto.cid):
                                    killer_owner.graveyard.append(killer_m.proto)
                                    killer_owner.record_death(killer_m.proto.cid, turn_num)
                                killer_owner.slots[ks] = None
                            break
            if has_t("Spirit Possession") and 0 <= slot < 6 and killer_owner.slots[slot] is not None:
                t = killer_owner.slots[slot]
                t.frozen_turns = max(t.frozen_turns, 2)
                t.silenced_turns = max(t.silenced_turns, 2)
                t.can_attack = False

        if self._p1 is not None and self._p2 is not None:
            CommanderSpiritEffects.on_monster_died(owner, killer_owner, dead_m, killer_m, self._p1, self._p2)
        return True

    def deal_spell_skill_dmg(
        self, slot: int, target_m: Optional[SimMonster], dmg: int, src_m: Optional[SimMonster],
        opponent: SimPlayer, player: SimPlayer, turn_num: int, ignore_immunity: bool = False, spell_cid: int = 0
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
                if s is not None and s.is_alive and s.has_p("Thicket Cover"):
                    dmg -= int(round(dmg * 0.25))
                    break

        applied = max(0, dmg)
        target_m.cur_def -= applied
        if applied > 0:
            player.total_dmg_dealt += applied
            if src_m is not None and self._is_catalog(src_m.proto.cid):
                player.card_dmg[src_m.proto.cid] = player.card_dmg.get(src_m.proto.cid, 0) + applied
            elif spell_cid > 0 and self._is_catalog(spell_cid):
                player.card_dmg[spell_cid] = player.card_dmg.get(spell_cid, 0) + applied
        if not target_m.is_alive:
            self.handle_death(opponent, player, slot, target_m, src_m, turn_num)

    def summon_monster(self, player: SimPlayer, opp: SimPlayer, card: CardProto, slot: int, turn_num: int) -> bool:
        if player.slots[slot] is not None or card.cost > player.mana:
            return False
        player.mana -= card.cost
        player.hand.remove(card)
        if self._is_catalog(card.cid):
            player.played_ids.add(card.cid)

        m = SimMonster(card, slot, turn_num)
        player.slots[slot] = m

        # CombatSystem.ApplyOnSummonPassives
        opp_m = opp.slots[slot]
        if opp_m is not None and opp_m.has_p("Light of Truth"):
            m.silenced_turns = max(m.silenced_turns, 2)
        else:
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
                best_t = min((si for i, si in enumerate(player.slots) if i != slot and si is not None), key=lambda x: x.cur_def, default=None)
                if best_t is not None:
                    best_t.add_bonus_def(15)
                else:
                    m.add_bonus_def(10)
            if "War Fusion" in pt:
                dwarves = sum(1 for s in player.slots if s is not None and (s.proto.tribe == "Dwarves" or s.proto.element == "Mecha"))
                m.add_bonus_def(max(1, dwarves) * 8)
            if "Nourishing Roots" in pt:
                player.power += 1
                player.mana += 1
            if "Psychic Drain" in pt:
                opp.power -= min(2, max(0, opp.power - 1))
            if "Black Nightmare" in pt:
                if opp.hand:
                    sac = opp.hand.pop()
                    if self._is_catalog(sac.cid):
                        opp.graveyard.append(sac)
                else:
                    opp.take_leader_damage(12, player)
            if "Tidal Bounce" in pt and opp_m is not None and (opp_m.base_atk + opp_m.bonus_atk) < 14:
                if not opp_m.has_steadfast:
                    if self._is_catalog(opp_m.proto.cid):
                        opp.add_card_to_hand(opp_m.proto, self._is_catalog)
                    opp.slots[slot] = None

        # PlayerState.SummonMonster line 456: CommanderSpiritEffects.OnMonsterSummoned
        CommanderSpiritEffects.on_monster_summoned(player, m)
        return True

    def play_leader_card(
        self, player: SimPlayer, opp: SimPlayer, leader: CardProto, turn_num: int,
        rng: random.Random, log: Optional[List[str]] = None
    ) -> bool:
        if leader.cost > player.mana or leader not in player.hand:
            return False
        player.mana -= leader.cost
        player.hand.remove(leader)
        if player.active_leader is not None and self._is_catalog(player.active_leader.cid):
            player.graveyard.append(player.active_leader)
        player.active_leader = leader
        if self._is_catalog(leader.cid):
            player.played_ids.add(leader.cid)

        allies_same = sum(1 for s in player.slots if s is not None and s.is_alive and s.proto.tribe == leader.tribe)
        enemies_rival = sum(1 for s in opp.slots if s is not None and s.is_alive and s.proto.tribe != leader.tribe)
        player.spirit_summons.append((leader.cid, turn_num, allies_same, enemies_rival))
        player.spirit_summon_allies += allies_same
        if allies_same >= 2:
            player.spirit_ge2_allies = True

        CommanderSpiritEffects.apply_on_summoned(player, opp, leader, turn_num, rng, self)
        if log is not None:
            log.append(f"  [دور {turn_num}] {player.name} استدعى روح القائد [{leader.name}] ({TRIBE_AR.get(leader.tribe, leader.tribe)}) | حلفاء نفس العشيرة باللوحة: {allies_same}")
        return True

    def start_of_turn_passives(self, player: SimPlayer, opp: SimPlayer, turn_num: int, rng: random.Random) -> None:
        for slot in range(6):
            m = player.slots[slot]
            if m is None:
                continue
            if m.burn_dmg > 0:
                if m.has_p("Moisture Shield") or m.has_p("Total Immunity to Fire") or m.has_p("Immunity to Poison and Burn"):
                    m.burn_dmg = 0
                else:
                    m.cur_def -= m.burn_dmg
            if m.poison_dmg > 0:
                if m.has_p("Immunity to Poison") or m.has_p("Immunity to Dark and Poison"):
                    m.poison_dmg = 0
                else:
                    m.cur_def -= m.poison_dmg
            if not m.is_alive:
                if not self.try_salvation(player, slot, m):
                    player.turn_deaths += 1
                    opp.turn_kills += 1
                    if self._is_catalog(m.proto.cid):
                        player.graveyard.append(m.proto)
                        player.record_death(m.proto.cid, turn_num)
                    player.slots[slot] = None
                    continue

            if m.frozen_turns > 0:
                if m.has_p("Freeze Resistance") or m.has_p("Total Immunity to Ice") or m.has_p("Frost"):
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
                    player.mana += 1
                if m.has_p("Booster Engine"):
                    m.bonus_atk += 2
                if m.has_p("Tailwind Gust"):
                    for ws in FLANK_SLOTS:
                        if player.slots[ws] is not None:
                            player.slots[ws].double_strike = True

            if opp_m is not None and not opp_m.is_alive:
                self.handle_death(opp, player, slot, opp_m, m, turn_num)

        CommanderSpiritEffects.on_start_of_turn(player, opp, rng)

    def _get_enemy_target(self, pref: int, enemies: List[SimMonster], opp: SimPlayer, max_atk: bool = False) -> Optional[SimMonster]:
        if not enemies:
            return None
        if 0 <= pref < 6 and opp.slots[pref] is not None:
            return opp.slots[pref]
        return max(enemies, key=lambda e: e.base_atk + e.bonus_atk) if max_atk else enemies[0]

    def resolve_active_skill(
        self, act: str, slot: int, m: SimMonster, player: SimPlayer, opp: SimPlayer,
        bonus: int, turn_num: int, rng: random.Random
    ) -> None:
        enemies = [s for s in opp.slots if s is not None and s.is_alive]
        allies = [s for s in player.slots if s is not None and s.is_alive]

        if "Hell of Annihilation" in act:
            dmg = 25 + bonus
            if enemies:
                for es in list(enemies):
                    self.deal_spell_skill_dmg(es.slot, es, dmg, m, opp, player, turn_num)
            else:
                dealt = opp.take_leader_damage(dmg, player)
                player.card_dmg[m.proto.cid] = player.card_dmg.get(m.proto.cid, 0) + dealt
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
            ally = next((al for al in allies if al.burn_dmg or al.poison_dmg or al.frozen_turns or al.weakened_turns), m)
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
                    dealt = opp.take_leader_damage(per_bolt, player)
                    player.card_dmg[m.proto.cid] = player.card_dmg.get(m.proto.cid, 0) + dealt
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
                    if self._is_catalog(best.proto.cid):
                        opp.add_card_to_hand(best.proto, self._is_catalog)
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
            tgt.extra_passives = (tgt.extra_passives or set()) | {"Earth Steadfastness"}
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
                    dealt = opp.take_leader_damage(rem, player)
                    player.card_dmg[m.proto.cid] = player.card_dmg.get(m.proto.cid, 0) + dealt
            else:
                dealt = opp.take_leader_damage(cdmg, player)
                player.card_dmg[m.proto.cid] = player.card_dmg.get(m.proto.cid, 0) + dealt
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
            dealt = opp.take_leader_damage(15, player)
            player.card_dmg[m.proto.cid] = player.card_dmg.get(m.proto.cid, 0) + dealt
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

    def _choose_slot_unity(self, player: SimPlayer, card: CardProto) -> int:
        order = SHIELD_SLOTS if card.role == "Pure Shield" else (ATTACK_SLOTS if card.role == "Attacker" else BALANCED_SLOTS)
        for s in order:
            if player.slots[s] is None:
                return s
        return -1

    def _score_summon_slot(self, player: SimPlayer, opp: SimPlayer, card: CardProto, slot: int) -> float:
        pt = card.passive_tags
        score = 0.0
        if card.role == "Pure Shield":
            score += 4.0 if slot in CENTER_SLOTS else (1.5 if slot in (0, 5) else 0.0)
        elif card.role == "Attacker":
            score += 4.0 if slot in FLANK_SLOTS else 0.0
        else:
            score += 3.0 if slot in CENTER_SLOTS else 1.5

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
            if card.role == "Pure Shield" or (pt & {"Flint Skin", "Auto-Freeze Attacker", "Poison Thorns", "Burning Embers"}):
                score += opp_atk * 1.35
            elif card.def_stat <= opp_atk and "Quick Charge" not in pt:
                score -= 9.0
        return score

    def _choose_best_slot(self, player: SimPlayer, opp: SimPlayer, card: CardProto) -> int:
        if self.ai_policy == "unity_ai_exact":
            return self._choose_slot_unity(player, card)
        best_slot = -1
        best_score = -1e9
        for s in range(6):
            if player.slots[s] is None:
                sc = self._score_summon_slot(player, opp, card, s)
                if sc > best_score:
                    best_score = sc
                    best_slot = s
        return best_slot

    def _should_play_env(
        self, active: SimPlayer, other: SimPlayer, env_card: CardProto,
        active_env: Optional[str], env_level: int, env_turns: int
    ) -> bool:
        if active_env == env_card.element:
            # Escalate Level 1->2->3 only when ready attackers benefit immediately (Lvl3 lasts 1 turn!)
            ready_buffed = sum(1 for s in active.slots if s is not None and s.proto.element == active_env and (s.can_attack or "Quick Charge" in s.proto.passive_tags))
            if env_level >= 3 and env_turns >= 1:
                return False
            if env_level == 1 and env_turns >= 3 and ready_buffed == 0:
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
            if hc.card_type == "monster" and hc.cost <= active.mana:
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
        return score >= (0.5 if active_env is None else 1.2)

    def _pick_spirit_unity(self, active: SimPlayer) -> Optional[CardProto]:
        """Exact AIOpponent.DecideCommanderSpiritCoroutine."""
        cur_tier = active.active_leader.cost if active.active_leader is not None else -1
        chosen: Optional[CardProto] = None
        for c in active.hand:
            if c.card_type == "leader" and c.cost <= active.mana:
                if active.active_leader is None or (c.cid != active.active_leader.cid and c.cost >= cur_tier):
                    if chosen is None or c.cost > chosen.cost:
                        chosen = c
        return chosen

    def _pick_spirit_smart(self, active: SimPlayer, other: SimPlayer) -> Optional[CardProto]:
        """Smart spirit scorer valuing board tribe synergy + tempo."""
        best: Optional[CardProto] = None
        best_val = -1e9
        enemies = [s for s in other.slots if s is not None and s.is_alive]
        allies = [s for s in active.slots if s is not None and s.is_alive]
        for c in active.hand:
            if c.card_type != "leader" or c.cost > active.mana:
                continue
            if active.active_leader is not None and active.active_leader.cid == c.cid:
                continue
            clan_board = sum(1 for al in allies if al.proto.tribe == c.tribe)
            clan_hand = sum(1 for hc in active.hand if hc.card_type == "monster" and hc.tribe == c.tribe)
            val = 10.0 + clan_board * 9.0 + clan_hand * 3.0 + len(enemies) * 1.8 + c.cost * 1.5
            # Direct tempo spirits usable even on low tribe board
            if c.cid in (451, 453, 455, 456, 457, 458, 464, 467, 468):
                val += 6.0
            # Avoid overwriting existing active Spirit unless strict upgrade or empty altar
            if active.active_leader is not None:
                cur_clan = sum(1 for al in allies if al.proto.tribe == active.active_leader.tribe)
                val -= (14.0 + cur_clan * 8.0)
            if val > 8.0 and val > best_val:
                best_val = val
                best = c
        return best

    def _should_cast_spell(self, caster: SimPlayer, opp: SimPlayer, sp: CardProto) -> bool:
        if self.ai_policy == "unity_ai_exact":
            return not (sp.spell_type == 25 and caster.hp > caster.max_hp * 0.80)
        st = sp.spell_type
        if st == ST_SUPPORT_BATTERY_HEAL:
            wounded = any(s is not None and s.cur_def < s.base_def - 4 for s in caster.slots)
            needs_battery = any(c is not sp and c.cost > caster.mana - sp.cost and c.cost <= caster.mana - sp.cost + 2 for c in caster.hand)
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

    def _score_active_skill(self, player: SimPlayer, opp: SimPlayer, act0: str) -> int:
        if opp.hp <= 50 and ("Ion Cannon" in act0 or "Mana Blast" in act0 or "Thermal" in act0):
            return 100
        if "Resurrection" in act0 and any(g.card_type == "monster" for g in player.graveyard):
            return 90
        if ("Mountain Shield" in act0 or "Iron Dome" in act0) and player.hp < 220:
            return 85
        if "Total Eclipse" in act0:
            return 80
        if any(k in act0 for k in ("Soul Reap", "Fissure", "Annihilation", "Grand Mana Blast", "Ion Cannon")):
            return 75
        if any(k in act0 for k in ("Time Warp", "Double", "Hastening Breeze")):
            return 70
        if "Absolute Sun" in act0 or "Nectar" in act0:
            return 65 if player.hp < player.max_hp * 0.80 else 25
        return 40

    def execute_active_skills(
        self, player: SimPlayer, opp: SimPlayer, turn_num: int,
        rng: random.Random, acted: Optional[List[int]] = None
    ) -> None:
        if player.mana <= 0 and player.power <= 0:
            return

        if self.ai_policy == "unity_ai_exact":
            slots_order = [(0, s, player.slots[s]) for s in range(6) if player.slots[s] is not None]
        else:
            cands = []
            for s in range(6):
                m = player.slots[s]
                if m is not None and m.is_alive and m.silenced_turns <= 0 and m.frozen_turns <= 0 and not m.used_active_turn and m.proto.actives:
                    cands.append((self._score_active_skill(player, opp, m.proto.actives[0]), s, m))
            cands.sort(key=lambda x: x[0], reverse=True)
            slots_order = cands

        for _, slot, m in slots_order:
            if not m.is_alive or m.silenced_turns > 0 or m.frozen_turns > 0 or m.used_active_turn or not m.proto.actives:
                continue

            # Check primary active readiness (GetAbilityState gates on actives[0])
            base0, pwr0, req_en0 = m.proto.active_meta[0]
            opp_m = opp.slots[slot]
            extra = 1 if (opp_m is not None and opp_m.has_p("Frost Aura")) else 0
            disc = (1 if player.free_skill_bonus > 0 else 0) + CommanderSpiritEffects.get_skill_mana_discount(player, m)
            cost0 = max(0, base0 + extra - disc)
            if player.mana < cost0 or player.power < pwr0:
                continue
            has_enemies = any(s is not None and s.is_alive for s in opp.slots)
            if req_en0 and not has_enemies:
                continue

            # Execute ALL actives on m (CombatSystem.ActivateMonsterAbility foreach loop!)
            activated_any = False
            for idx_a, act in enumerate(m.proto.actives):
                base_c, pwr_c, req_en = m.proto.active_meta[idx_a]
                opp_m = opp.slots[slot]
                extra = 1 if (opp_m is not None and opp_m.has_p("Frost Aura")) else 0
                disc = (1 if player.free_skill_bonus > 0 else 0) + CommanderSpiritEffects.get_skill_mana_discount(player, m)
                cost = max(0, base_c + extra - disc)
                if player.mana < cost or player.power < pwr_c:
                    continue
                has_enemies = any(s is not None and s.is_alive for s in opp.slots)
                if req_en and not has_enemies:
                    continue
                if ("Wrath of Nature" in act or "Graveyard Summoning" in act) and not any(player.slots[ws] is None for ws in FLANK_SLOTS):
                    continue

                m.used_active_turn = True
                activated_any = True
                player.mana -= cost
                if pwr_c > 0:
                    player.power -= pwr_c
                bonus = player.free_skill_bonus
                player.free_skill_bonus = 0
                if acted is not None and self._is_catalog(m.proto.cid):
                    acted.append(m.proto.cid)
                if m.has_p("Mana Recovery"):
                    player.mana += 1
                for s in player.slots:
                    if s is not None and s.has_p("Arcane Echo"):
                        s.bonus_atk += 3
                reps = 2 if (m.has_p("Spell Multiplier") and rng.random() < 0.50) else 1
                if reps == 2:
                    player.multipliers += 1
                    player.turn_clutch = True
                for _ in range(reps):
                    self.resolve_active_skill(act, slot, m, player, opp, bonus, turn_num, rng)
                if player.mana <= 0 and player.power <= 0:
                    break

            if not activated_any:
                m.used_active_turn = False
            if player.mana <= 0 and player.power <= 0:
                break

    def cast_spell(self, caster: SimPlayer, opp: SimPlayer, sp: CardProto, turn_num: int, rng: random.Random) -> None:
        caster.mana -= sp.cost
        caster.hand.remove(sp)
        if self._is_catalog(sp.cid):
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
                    caster.mana += 1

        for _ in range(reps):
            allies = [s for s in caster.slots if s is not None and s.is_alive]
            enemies = [s for s in opp.slots if s is not None and s.is_alive]
            st = sp.spell_type

            if st == ST_ARCANE_PIERCING_SHOT:
                if enemies:
                    tgt = max(enemies, key=lambda e: (1 if (caster.slots[e.slot] is not None and caster.slots[e.slot].can_attack) else 0, e.base_atk + e.bonus_atk))
                    self.deal_spell_skill_dmg(tgt.slot, tgt, val, None, opp, caster, turn_num, ignore_immunity=True, spell_cid=sp.cid)
                else:
                    dealt = opp.take_leader_damage(val, caster)
                    caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + dealt
            elif st == ST_SUPPORT_BATTERY_HEAL:
                caster.mana += 2
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
                dealt = opp.take_leader_damage(max(6, val // 2), caster)
                caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + dealt
            elif st == ST_MANA_STEAL_AOE:
                if opp.power > 1:
                    opp.power = max(1, opp.power - 1)
                caster.power += 1
                caster.mana += 1
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
                    dealt = opp.take_leader_damage(dmg, caster)
                    caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + dealt
            elif st == ST_COPY_SUPPORT_PASSIVE:
                if allies:
                    donor = max(allies, key=lambda a: len(a.proto.passive_tags))
                    others = [a for a in allies if a is not donor]
                    rec = max(others, key=lambda a: a.base_atk + a.cur_def) if others else donor
                    rec.extra_passives = (rec.extra_passives or set()) | donor.proto.passive_tags
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
                    m1 = max(allies, key=lambda a: (1 if opp.slots[a.slot] is None else 0, a.base_atk + a.bonus_atk))
                    wounded = min(allies, key=lambda a: a.cur_def)
                    if m1 is wounded:
                        m2 = max(allies, key=lambda a: a.cur_def)
                    else:
                        m2 = wounded
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
                    es = max(
                        enemies,
                        key=lambda e: (1 if (caster.slots[e.slot] is not None and caster.slots[e.slot].can_attack) else 0, 0 if e.has_steadfast else 1, e.base_atk + e.bonus_atk),
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
                    dealt = opp.take_leader_damage(val, caster)
                    caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + dealt
            elif st == ST_TELEPORT_WEAKEST:
                if allies:
                    am = max(allies, key=lambda a: a.base_atk + a.bonus_atk)
                    am.bonus_atk += max(4, val // 2)
                    am.can_attack = True
                    if enemies:
                        tgt = min(enemies, key=lambda e: e.cur_def)
                        self.deal_spell_skill_dmg(tgt.slot, tgt, val // 2, am, opp, caster, turn_num)
                else:
                    dealt = opp.take_leader_damage(val, caster)
                    caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + dealt
            elif st == ST_CONVERT_DEF_ATK:
                if allies:
                    am = max(allies, key=lambda a: (1 if opp.slots[a.slot] is None else 0, a.cur_def))
                    conv = min(max(0, am.cur_def - 6), val // 2)
                    am.cur_def -= conv
                    am.bonus_atk += val
                    am.can_attack = True
                else:
                    dealt = opp.take_leader_damage(val, caster)
                    caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + dealt
            elif st in (ST_INVERT_ENEMY_STATS, ST_SIPHON_DEF_ATK):
                if enemies:
                    es = max(enemies, key=lambda e: e.cur_def)
                    self.deal_spell_skill_dmg(es.slot, es, val, None, opp, caster, turn_num, spell_cid=sp.cid)
                    if allies:
                        al = max(allies, key=lambda a: a.base_atk + a.bonus_atk)
                        al.bonus_atk += val // 2
                        al.add_bonus_def(val // 3)
                else:
                    dealt = opp.take_leader_damage(val, caster)
                    caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + dealt
            elif st == ST_ZERO_SKILL_BOOST:
                caster.mana += 1
                caster.free_skill_bonus += val // 2
                if allies:
                    al = max(allies, key=lambda a: 1 if a.proto.actives else 0)
                    al.bonus_atk += val // 2
                    al.add_bonus_def(val // 2)
            elif st == ST_DOUBLE_STRIKE_CENTER:
                if allies:
                    am = max(allies, key=lambda a: (1 if opp.slots[a.slot] is None else 0, 1 if a.can_attack else 0, a.base_atk + a.bonus_atk))
                    am.can_attack = True
                    am.double_strike = True
                    am.bonus_atk += val // 2
                else:
                    dealt = opp.take_leader_damage(val, caster)
                    caster.card_dmg[sp.cid] = caster.card_dmg.get(sp.cid, 0) + dealt
            elif st == ST_REVIVE_GRAVEYARD:
                fallen = [g for g in caster.graveyard if g.card_type == "monster"]
                if fallen:
                    best = max(fallen, key=lambda g: g.cost)
                    caster.graveyard.remove(best)
                    caster.add_card_to_hand(best, self._is_catalog)
                else:
                    self.draw_card(caster, turn_num, rng)
                caster.heal_leader(val, opp)

        CommanderSpiritEffects.on_spell_cast(caster, opp)

    def execute_combat_phase(
        self, attacker: SimPlayer, defender: SimPlayer, active_env: Optional[str],
        env_level: int, turn_num: int, rng: random.Random
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
                atk_power = att.effective_atk(active_env, env_level, attacker)

                for es in defender.slots:
                    if es is not None and es.is_alive:
                        if es.has_p("Dread of the Dead"):
                            atk_power = max(0, atk_power - 3)
                        if slot_idx in FLANK_SLOTS and es.has_p("Polar Winds"):
                            atk_power = max(0, atk_power - 4)

                atk_power = max(0, atk_power - CommanderSpiritEffects.get_enemy_dread_atk_penalty(defender, att))

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

                opp_target_m = opp_m if (opp_m is not None and opp_m.is_alive) else None
                atk_power = CommanderSpiritEffects.modify_outgoing_combat_attack(attacker, att, opp_target_m, atk_power, rng)

                if opp_target_m is not None:
                    if opp_target_m.bubble_shield:
                        opp_target_m.bubble_shield = False
                        continue
                    if opp_target_m.has_p("Ethereal Body"):
                        if att.proto.element not in ("Light", "Arcane") and att.proto.tribe != "Ghosts" and att.proto.element != "Ghosts" and not att.has_p("Phase Shift"):
                            atk_power = max(1, int(round(atk_power * 0.65)))
                    if not att.has_p("Targeting System"):
                        if opp_target_m.has_p("Light Evasion") and rng.random() < 0.20:
                            continue
                        if opp_target_m.has_p("Mystic Intuition") and rng.random() < 0.25:
                            continue
                        if opp_target_m.has_p("Nimble Agility") and rng.random() < 0.15:
                            continue
                        sp_evade = CommanderSpiritEffects.get_combat_evasion_chance(defender, opp_target_m)
                        if sp_evade > 0.0 and rng.random() < sp_evade:
                            continue

                    if not (att.has_p("Phase Shift") or att.has_p("Aerial Pierce")):
                        if opp_target_m.has_p("Water Resilience"):
                            atk_power = max(1, atk_power - 3)
                        if opp_target_m.has_p("Protective Flame"):
                            atk_power = max(1, atk_power - 4)
                        if opp_target_m.has_p("Rock Hardness"):
                            atk_power = max(1, atk_power - 4)
                        if opp_target_m.has_p("Steel Armor"):
                            atk_power = max(1, atk_power - 5)
                        sp_red = CommanderSpiritEffects.get_combat_incoming_reduction(defender, opp_target_m)
                        if sp_red > 0:
                            atk_power = max(1, atk_power - sp_red)

                    dealt_m = opp_target_m.take_damage(atk_power)
                    attacker.total_dmg_dealt += dealt_m
                    if dealt_m > 0 and self._is_catalog(att.proto.cid):
                        attacker.card_dmg[att.proto.cid] = attacker.card_dmg.get(att.proto.cid, 0) + dealt_m

                    if att.has_p("Eternal Flame"):
                        att.bonus_atk += 2
                    if att.has_p("Static Discharge"):
                        opp_target_m.silenced_turns = max(opp_target_m.silenced_turns, 1)
                    if att.has_p("Life Leech"):
                        att.heal_def(max(2, int(round(atk_power * 0.20))))
                    if att.has_p("Slow Poison"):
                        opp_target_m.poison_dmg = max(opp_target_m.poison_dmg, 6)

                    CommanderSpiritEffects.on_combat_hit_dealt(attacker, att, defender, opp_target_m, atk_power)

                    if att.has_p("Chain Lightning"):
                        splash = max(3, int(round(atk_power * 0.30)))
                        for adj in (slot_idx - 1, slot_idx + 1):
                            if 0 <= adj < 6 and defender.slots[adj] is not None:
                                adj_m = defender.slots[adj]
                                dealt_adj = adj_m.take_damage(splash)
                                attacker.total_dmg_dealt += dealt_adj
                                if not adj_m.is_alive:
                                    self.handle_death(defender, attacker, adj, adj_m, att, turn_num)
                                break

                    if opp_target_m.has_p("Poison Thorns") or opp_target_m.has_p("Backlash Spark"):
                        att.take_damage(3)
                    if opp_target_m.has_p("Flint Skin"):
                        att.take_damage(5)
                    if opp_target_m.has_p("Burning Embers"):
                        att.take_damage(max(2, int(round(atk_power * 0.25))))
                    if opp_target_m.has_p("Auto-Freeze Attacker") and att.is_alive:
                        att.frozen_turns = max(att.frozen_turns, 1)

                    if not opp_target_m.is_alive:
                        self.handle_death(defender, attacker, slot_idx, opp_target_m, att, turn_num)
                    if not att.is_alive:
                        self.handle_death(attacker, defender, slot_idx, att, opp_target_m, turn_num)
                else:
                    dealt = defender.take_leader_damage(atk_power, attacker)
                    if self._is_catalog(att.proto.cid):
                        attacker.card_dmg[att.proto.cid] = attacker.card_dmg.get(att.proto.cid, 0) + dealt
                    if att.has_p("Mana Steal"):
                        attacker.mana += 1

    def _setup_opening_hands(self, p1: SimPlayer, p2: SimPlayer, rng: random.Random, log: Optional[List[str]]) -> None:
        p1.deck = self.generate_deck(self.deck_size, None, rng)
        p2.deck = self.generate_deck(self.deck_size, None, rng)

        if self.start_mode == "fair_standard":
            for _ in range(5):
                self.draw_card(p1, 1, rng, log)
            for _ in range(6):
                self.draw_card(p2, 1, rng, log)
        elif self.start_mode in ("sym_spirit_real", "sym_spirit_free"):
            pool = self.free_leaders if self.start_mode == "sym_spirit_free" else self.leaders
            for pl, n_cards in ((p1, 5), (p2, 6)):
                sp = next((c for c in pl.deck if c.card_type == "leader"), None)
                if sp is not None:
                    pl.deck.remove(sp)
                    if self.start_mode == "sym_spirit_free":
                        sp = next((fl for fl in self.free_leaders if fl.cid == sp.cid), sp)
                elif pool:
                    sp = rng.choice(pool)
                if sp is not None:
                    pl.drawn_ids.add(sp.cid)
                    if self._is_catalog(sp.cid):
                        pl.drawn_ever_ids.add(sp.cid)
                    pl.hand.append(sp)
                for _ in range(max(0, n_cards - len(pl.hand))):
                    self.draw_card(pl, 1, rng, log)
        elif self.start_mode == "unity_debug_exact":
            sp = next((c for c in p1.deck if c.card_type == "leader"), None)
            if sp is not None:
                p1.deck.remove(sp)
                fl = next((x for x in self.free_leaders if x.cid == sp.cid), sp)
                p1.drawn_ids.add(fl.cid)
                p1.drawn_ever_ids.add(fl.cid)
                p1.hand.append(fl)
            for env_test in ICE_DEBUG_ENVS:
                p1.hand.append(env_test)
            for _ in range(2):
                self.draw_card(p1, 1, rng, log)
            for _ in range(6):
                self.draw_card(p2, 1, rng, log)
            p1.power = 10
            p1.mana = 10
            p2.power = 5
            p2.mana = 5

    def play_match(
        self, rng: random.Random, max_turns: int = MAX_MATCH_TURNS, verbose: bool = False
    ) -> Tuple[int, int, SimPlayer, SimPlayer, List[str], dict]:
        log: Optional[List[str]] = [] if verbose else None
        p1 = SimPlayer("اللاعب الأول (أ)")
        p2 = SimPlayer("اللاعب الثاني (ب)")
        self._p1 = p1
        self._p2 = p2
        self._setup_opening_hands(p1, p2, rng, log)

        active_env: Optional[str] = None
        env_level = 1
        env_turns = 0
        env_caster_is_p1: Optional[bool] = None
        turn_num = 1
        is_p1 = True

        # Deep Telemetry Accumulators
        half_turns = 0
        dead_turns = 0
        brick_dead_turns = 0
        stall_turns = 0
        explosive_turns = 0
        lead_changes = 0
        prev_leader = 0
        p1_trailed_75 = p2_trailed_75 = False
        p1_trailed_120 = p2_trailed_120 = False
        total_sacrifices = 0
        env_active_turns = 0
        env_benefit_turns = 0
        env_backfire_turns = 0
        env_lvl_plays = [0, 0, 0, 0]
        env_elem_l2: Dict[str, int] = {}
        env_elem_l3: Dict[str, int] = {}

        # Per-turn fixed profile bins [1..30]
        b_n = MAX_TURN_BIN + 1
        tv = [0] * b_n
        thp1 = [0] * b_n
        thp2 = [0] * b_n
        tm_av = [0] * b_n
        tm_sp = [0] * b_n
        thnd = [0] * b_n
        tbrd = [0] * b_n
        tdmg = [0] * b_n
        tdead = [0] * b_n
        tbrick = [0] * b_n
        tstall = [0] * b_n
        texc = [0] * b_n
        tburn = [0] * b_n

        while turn_num <= max_turns and p1.hp > 0 and p2.hp > 0:
            half_turns += 1
            active = p1 if is_p1 else p2
            other = p2 if is_p1 else p1

            p1.turn_deaths = p2.turn_deaths = 0
            p1.turn_kills = p2.turn_kills = 0
            p1.turn_clutch = p2.turn_clutch = False
            p1.turn_burns = p2.turn_burns = 0
            hp1_start, hp2_start = p1.hp, p2.hp
            dmg_start = active.total_dmg_dealt
            actions_taken = 0
            acted_cids: List[int] = []

            if other.team_immune_turns > 0:
                other.team_immune_turns -= 1

            active.mana = active.power
            got_t2_bonus = False
            if turn_num == 2 and not is_p1:
                active.mana += 1
                got_t2_bonus = True
                if log is not None:
                    log.append(f"  [دور {turn_num}] {active.name} استلم +1 مانا مؤقتة للتعويض")

            active.sacrificed_turn = False
            if active.deny_turns > 0:
                active.deny_turns -= 1
                active.sacrificed_turn = True
            else:
                if not ((turn_num == 1 and is_p1) or (turn_num == 2 and not is_p1)):
                    self.draw_card(active, turn_num, rng, log)

            for s in active.slots:
                if s is not None:
                    s.reset_turn(turn_num)

            self.start_of_turn_passives(active, other, turn_num, rng)
            if p1.hp <= 0 or p2.hp <= 0:
                break

            avail_mana = active.mana + (1 if (len(active.hand) >= 2 and not active.sacrificed_turn) else 0)

            # 1. Sacrifice (+1 Power & +1 Mana + Trigger Lilitha 454 if active)
            if len(active.hand) >= 2 and not active.sacrificed_turn:
                aff_m = sum(1 for c in active.hand if c.card_type == "monster" and c.cost <= active.mana)
                has_hi = any(c.card_type == "monster" and c.cost > active.mana for c in active.hand)
                needs_mana = (active.power == 0) or (has_hi and aff_m == 0) or (active.power < 8 and len(active.hand) >= 5)
                if self.ai_policy == "unity_ai_exact":
                    should_sac = needs_mana or len(active.hand) >= 7
                else:
                    should_sac = (active.power < 10 and (needs_mana or len(active.hand) >= 7)) or len(active.hand) >= 7
                if should_sac:
                    sac = max((c for c in active.hand if c.cost > active.mana + 2), key=lambda x: x.cost, default=None)
                    if sac is None:
                        sac = next((c for c in active.hand if c.card_type == "env" and not self._should_play_env(active, other, c, active_env, env_level, env_turns)), None)
                    if sac is None:
                        sac = next((c for c in active.hand if c.card_type != "monster" and c.cost > active.mana + 1), None)
                    if sac is None and (needs_mana or len(active.hand) >= 5):
                        sac = max(active.hand, key=lambda x: x.cost) if self.ai_policy != "unity_ai_exact" else active.hand[-1]
                    if sac is not None and sac in active.hand:
                        active.hand.remove(sac)
                        if self._is_catalog(sac.cid):
                            active.graveyard.append(sac)
                            active.sacrificed_cnt[sac.cid] = active.sacrificed_cnt.get(sac.cid, 0) + 1
                        active.power += 1
                        active.mana += 1
                        active.sacrificed_turn = True
                        total_sacrifices += 1
                        CommanderSpiritEffects.on_card_sacrificed(active, other)
                        if log is not None:
                            log.append(f"  [دور {turn_num}] {active.name} ضحّى بـ {sac.name} -> باور {active.power} | مانا {active.mana}")

            mana_start_budget = active.mana

            # 2. Environment Card (with Lvl 1 -> 2 -> 3 escalation & Cantrip Draw +1)
            for c in list(active.hand):
                if c.card_type == "env" and c.cost <= active.mana:
                    play_ok = (self.ai_policy == "unity_ai_exact") or self._should_play_env(active, other, c, active_env, env_level, env_turns)
                    if play_ok:
                        active.mana -= c.cost
                        active.hand.remove(c)
                        if self._is_catalog(c.cid):
                            active.graveyard.append(c)
                            active.played_ids.add(c.cid)
                            acted_cids.append(c.cid)
                        actions_taken += 1
                        active.env_played_cnt += 1
                        active.env_played_elems.append(c.element)
                        if active_env == c.element:
                            env_level = c.env_level if c.env_level > env_level else min(3, env_level + 1)
                        else:
                            active_env = c.element
                            env_level = max(1, c.env_level)
                        env_turns = ENV_LEVEL_STATS[env_level][0]
                        env_caster_is_p1 = is_p1
                        env_lvl_plays[env_level] += 1
                        if env_level == 2:
                            env_elem_l2[active_env] = env_elem_l2.get(active_env, 0) + 1
                        elif env_level == 3:
                            env_elem_l3[active_env] = env_elem_l3.get(active_env, 0) + 1
                        self.draw_card(active, turn_num, rng, log)
                        if log is not None:
                            log.append(f"  [دور {turn_num}] {active.name} فعّل بيئة {ELEM_AR.get(active_env, active_env)} Lvl {env_level} ({env_turns} أدوار) (+سحب)")
                        break

            # 2.5 Commander Spirit Summon
            sp_card = self._pick_spirit_unity(active) if self.ai_policy == "unity_ai_exact" else self._pick_spirit_smart(active, other)
            if sp_card is not None:
                if self.play_leader_card(active, other, sp_card, turn_num, rng, log):
                    actions_taken += 1
                    acted_cids.append(sp_card.cid)
                    if p1.hp <= 0 or p2.hp <= 0:
                        break

            if self.ai_policy == "unity_ai_exact":
                # Literal AIOpponent.cs order: 3. All Spells -> 4. Summons
                for sp in [c for c in list(active.hand) if c.card_type == "spell" and c.cost <= active.mana]:
                    if sp.cost <= active.mana and sp in active.hand and self._should_cast_spell(active, other, sp):
                        self.cast_spell(active, other, sp, turn_num, rng)
                        actions_taken += 1
                        acted_cids.append(sp.cid)
                        if p1.hp <= 0 or p2.hp <= 0:
                            break
                if p1.hp <= 0 or p2.hp <= 0:
                    break
                mon_cands = [c for c in active.hand if c.card_type == "monster"]
                mon_cands.sort(key=lambda c: c.cost + c.atk + c.def_stat, reverse=True)
                for mcard in mon_cands:
                    if mcard.cost <= active.mana:
                        slot = self._choose_best_slot(active, other, mcard)
                        if slot != -1:
                            self.summon_monster(active, other, mcard, slot, turn_num)
                            actions_taken += 1
                            acted_cids.append(mcard.cid)
            else:
                # Smart Tactical Order: 3A Synergy Enablers -> 3B Pre-Spells -> 4 Summons -> 4B Post-Buff Spells
                if any(c.card_type == "spell" and c.cost <= active.mana for c in active.hand):
                    syn = [c for c in active.hand if c.card_type == "monster" and c.cost <= active.mana and (c.passive_tags & self._SYNERGY_PASSIVES)]
                    syn.sort(key=lambda c: c.cost)
                    for sm in syn:
                        if sm.cost <= active.mana:
                            slot = self._choose_best_slot(active, other, sm)
                            if slot != -1:
                                self.summon_monster(active, other, sm, slot, turn_num)
                                actions_taken += 1
                                acted_cids.append(sm.cid)

                for sp in [c for c in list(active.hand) if c.card_type == "spell" and c.cost <= active.mana and c.spell_type not in self._POST_BUFF_SPELLS]:
                    if sp.cost <= active.mana and sp in active.hand and self._should_cast_spell(active, other, sp):
                        self.cast_spell(active, other, sp, turn_num, rng)
                        actions_taken += 1
                        acted_cids.append(sp.cid)
                        if p1.hp <= 0 or p2.hp <= 0:
                            break
                if p1.hp <= 0 or p2.hp <= 0:
                    break

                mon_cands = [c for c in active.hand if c.card_type == "monster"]
                mon_cands.sort(key=lambda c: (1 if "Quick Charge" in c.passive_tags else 0, c.cost + c.atk + c.def_stat), reverse=True)
                for mcard in mon_cands:
                    if mcard.cost <= active.mana:
                        slot = self._choose_best_slot(active, other, mcard)
                        if slot != -1:
                            self.summon_monster(active, other, mcard, slot, turn_num)
                            actions_taken += 1
                            acted_cids.append(mcard.cid)

                for sp in [c for c in list(active.hand) if c.card_type == "spell" and c.cost <= active.mana]:
                    if sp.cost <= active.mana and sp in active.hand and self._should_cast_spell(active, other, sp):
                        self.cast_spell(active, other, sp, turn_num, rng)
                        actions_taken += 1
                        acted_cids.append(sp.cid)
                        if p1.hp <= 0 or p2.hp <= 0:
                            break
                if p1.hp <= 0 or p2.hp <= 0:
                    break

            # 5. Active Skills (all 2 skills on boss cards)
            mana_b_sk, pwr_b_sk = active.mana, active.power
            self.execute_active_skills(active, other, turn_num, rng, acted_cids)
            if active.mana < mana_b_sk or active.power < pwr_b_sk or any(s is not None and s.used_active_turn for s in active.slots):
                actions_taken += 1
            if p1.hp <= 0 or p2.hp <= 0:
                break

            mana_spent = max(0, mana_start_budget - max(0, active.mana))
            if got_t2_bonus and mana_spent >= mana_start_budget:
                active.t2_bonus_used = True

            # Environment turn-end check metrics (before combat & tick)
            if active_env is not None:
                env_active_turns += 1
                if env_caster_is_p1 is not None:
                    caster_p = p1 if env_caster_is_p1 else p2
                    opp_p = p2 if env_caster_is_p1 else p1
                    b_el, deb_set = ENV_MATRIX[active_env]
                    c_score = sum((1 if s.proto.element == b_el else (-1 if s.proto.element in deb_set else 0)) for s in caster_p.slots if s is not None)
                    o_score = sum((-1 if s.proto.element == b_el else (1 if s.proto.element in deb_set else 0)) for s in opp_p.slots if s is not None)
                    if c_score + o_score > 0:
                        env_benefit_turns += 1
                    elif c_score + o_score < 0:
                        env_backfire_turns += 1

            # 6. Combat Phase
            self.execute_combat_phase(active, other, active_env, env_level, turn_num, rng)

            hp_swing = abs(p1.hp - hp1_start) + abs(p2.hp - hp2_start)
            dmg_delta = max(0, active.total_dmg_dealt - dmg_start)
            turn_dmg_total = max(dmg_delta, hp_swing)
            total_deaths = p1.turn_deaths + p2.turn_deaths
            had_clutch = p1.turn_clutch or p2.turn_clutch
            board_cnt = sum(1 for s in p1.slots if s is not None) + sum(1 for s in p2.slots if s is not None)
            if turn_num <= 2:
                opp_loss_t12 = max(0, (hp2_start - p2.hp) if is_p1 else (hp1_start - p1.hp))
                active.turn12_dmg += max(dmg_delta, opp_loss_t12)

            is_explosive = (total_deaths >= 2) or (hp_swing >= 45) or had_clutch
            is_dead = (actions_taken == 0) and (hp_swing == 0) and (total_deaths == 0)
            is_brick_dead = is_dead and (mana_start_budget >= 2)
            is_stall = (not is_dead) and (board_cnt >= 2) and (total_deaths == 0) and (hp_swing < 10)

            if is_explosive:
                explosive_turns += 1
                for cid in set(acted_cids):
                    active.card_excite[cid] = active.card_excite.get(cid, 0) + 1
            elif is_dead:
                dead_turns += 1
                if is_brick_dead:
                    brick_dead_turns += 1
                for hc in active.hand:
                    if self._is_catalog(hc.cid) and hc.cost > active.mana:
                        active.card_stall[hc.cid] = active.card_stall.get(hc.cid, 0) + 1
            elif is_stall:
                stall_turns += 1
                for s in active.slots:
                    if s is not None and self._is_catalog(s.proto.cid):
                        active.card_stall[s.proto.cid] = active.card_stall.get(s.proto.cid, 0) + 1

            b = min(turn_num, MAX_TURN_BIN)
            tv[b] += 1
            thp1[b] += p1.hp
            thp2[b] += p2.hp
            tm_av[b] += max(0, avail_mana)
            tm_sp[b] += mana_spent
            thnd[b] += len(active.hand)
            tbrd[b] += board_cnt
            tdmg[b] += turn_dmg_total
            if is_dead:
                tdead[b] += 1
            if is_brick_dead:
                tbrick[b] += 1
            if is_stall:
                tstall[b] += 1
            if is_explosive:
                texc[b] += 1
            tburn[b] += p1.turn_burns + p2.turn_burns

            if turn_num >= 3:
                if p2.hp - p1.hp >= 75:
                    p1_trailed_75 = True
                if p1.hp - p2.hp >= 75:
                    p2_trailed_75 = True
                if p2.hp - p1.hp >= 120:
                    p1_trailed_120 = True
                if p1.hp - p2.hp >= 120:
                    p2_trailed_120 = True
                cur_leader = 1 if p1.hp > p2.hp + 15 else (2 if p2.hp > p1.hp + 15 else prev_leader)
                if prev_leader != 0 and cur_leader != 0 and cur_leader != prev_leader:
                    lead_changes += 1
                if cur_leader != 0:
                    prev_leader = cur_leader

            if p1.hp <= 0 or p2.hp <= 0:
                break

            # GameManager.EndTurn: activeEnvironment.TickTurn()
            if active_env is not None:
                env_turns -= 1
                if env_turns <= 0:
                    active_env = None
                    env_level = 1
                    env_caster_is_p1 = None

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
        comeback_win = 1 if ((winner == 1 and p1_trailed_75) or (winner == 2 and p2_trailed_75)) else 0
        big_comeback = 1 if ((winner == 1 and p1_trailed_120) or (winner == 2 and p2_trailed_120)) else 0
        winner_hp = p1.hp if winner == 1 else (p2.hp if winner == 2 else 0)
        hp_diff = abs(p1.hp - p2.hp)
        nail_biter = 1 if (winner != 0 and (winner_hp <= 90 or hp_diff <= 50)) else 0
        blowout = 1 if (winner != 0 and turns_taken <= 8) else 0

        if winner in (1, 2) and (comeback_win or nail_biter):
            win_p = p1 if winner == 1 else p2
            bonus_e = 2 if comeback_win else 1
            for cid in win_p.played_ids:
                win_p.card_excite[cid] = win_p.card_excite.get(cid, 0) + bonus_e

        mstats = {
            "half_turns": max(1, half_turns),
            "dead_turns": dead_turns, "brick_dead_turns": brick_dead_turns,
            "stall_turns": stall_turns, "explosive_turns": explosive_turns,
            "lead_changes": lead_changes, "comeback_win": comeback_win, "big_comeback": big_comeback,
            "nail_biter": nail_biter, "blowout": blowout,
            "salvations": p1.salvations + p2.salvations, "multipliers": p1.multipliers + p2.multipliers,
            "sacrifices": total_sacrifices, "hand_burns": p1.hand_burns_total + p2.hand_burns_total,
            "env_active_turns": env_active_turns, "env_benefit_turns": env_benefit_turns, "env_backfire_turns": env_backfire_turns,
            "env_lvl_plays": env_lvl_plays, "env_elem_l2": env_elem_l2, "env_elem_l3": env_elem_l3,
            "tv": tv, "thp1": thp1, "thp2": thp2, "tm_av": tm_av, "tm_sp": tm_sp,
            "thnd": thnd, "tbrd": tbrd, "tdmg": tdmg, "tdead": tdead, "tbrick": tbrick,
            "tstall": tstall, "texc": texc, "tburn": tburn,
        }
        if log is not None:
            w_lbl = p1.name if winner == 1 else (p2.name if winner == 2 else "تعادل")
            rounds_taken = (turns_taken + 1) // 2
            log.append(
                f"=== النهاية | الدور {turns_taken} ({rounds_taken} جولات كاملة) | الفائز: {w_lbl} | الصحة {p1.hp} ضد {p2.hp} "
                f"| متفجرة: {explosive_turns} • ميتة: {dead_turns} • جمود: {stall_turns} • انقلابات: {lead_changes} ==="
            )
        return winner, turns_taken, p1, p2, (log or []), mstats


# ==============================================================================
# Multiprocessing Worker & Stats Container
# ==============================================================================
_WORKER_ENGINE: Optional[BattleEngine] = None
_WORKER_SEED_BASE: int = 0


def new_agg_stats() -> dict:
    b_n = MAX_TURN_BIN + 1
    return {
        "n": 0, "w1": 0, "w2": 0, "draws": 0, "turns": 0, "waves": 0, "comebacks": 0,
        "half_turns": 0, "dead_turns": 0, "brick_dead_turns": 0, "stall_turns": 0,
        "explosive_turns": 0, "lead_changes": 0, "comeback_wins": 0, "big_comebacks": 0,
        "nail_biters": 0, "blowouts": 0, "salvations": 0, "multipliers": 0,
        "sacrifices": 0, "hand_burns": 0, "p1_hp_end": 0, "p2_hp_end": 0,
        "p1_t12_dmg": 0, "p2_t12_dmg": 0, "t2_bonus_used": 0,
        "env_active_turns": 0, "env_benefit_turns": 0, "env_backfire_turns": 0,
        "env_l1": 0, "env_l2": 0, "env_l3": 0,
        "env_caster_nm": 0, "env_caster_wins": 0,
        "sp_any_p1_m": 0, "sp_any_p1_w": 0, "sp_any_p2_m": 0, "sp_any_p2_w": 0,
        "sp_vs_none_m": 0, "sp_vs_none_w": 0,
        "sp_ge2_m": 0, "sp_ge2_w": 0, "sp_lt2_m": 0, "sp_lt2_w": 0,
        "sp_summons": 0, "sp_turn_sum": 0, "sp_allies_sum": 0, "sp_rivals_sum": 0,
        "sp_0_ally_cnt": 0, "sp_ge1_ally_cnt": 0, "sp_ge2_ally_cnt": 0,
        "turn_hist": {},
        "pm": {}, "pw": {}, "nmm": {}, "nmw": {}, "dm": {}, "dw": {},
        "dmg": {}, "kills": {}, "cexcite": {}, "cstall": {}, "sac": {}, "burn": {},
        "sp_cnt": {}, "sp_turn": {}, "sp_allies": {}, "sp_rivals": {}, "sp_ge1": {},
        "env_el_l2": {}, "env_el_l3": {},
        "tv": [0] * b_n, "thp1": [0] * b_n, "thp2": [0] * b_n, "tm_av": [0] * b_n,
        "tm_sp": [0] * b_n, "thnd": [0] * b_n, "tbrd": [0] * b_n, "tdmg": [0] * b_n,
        "tdead": [0] * b_n, "tbrick": [0] * b_n, "tstall": [0] * b_n, "texc": [0] * b_n,
        "tburn": [0] * b_n,
    }


def merge_agg_stats(dst: dict, src: dict) -> None:
    scalar_keys = (
        "n", "w1", "w2", "draws", "turns", "waves", "comebacks", "half_turns",
        "dead_turns", "brick_dead_turns", "stall_turns", "explosive_turns",
        "lead_changes", "comeback_wins", "big_comebacks", "nail_biters", "blowouts",
        "salvations", "multipliers", "sacrifices", "hand_burns", "p1_hp_end", "p2_hp_end",
        "p1_t12_dmg", "p2_t12_dmg", "t2_bonus_used", "env_active_turns", "env_benefit_turns",
        "env_backfire_turns", "env_l1", "env_l2", "env_l3", "env_caster_nm", "env_caster_wins",
        "sp_any_p1_m", "sp_any_p1_w", "sp_any_p2_m", "sp_any_p2_w", "sp_vs_none_m", "sp_vs_none_w",
        "sp_ge2_m", "sp_ge2_w", "sp_lt2_m", "sp_lt2_w", "sp_summons", "sp_turn_sum",
        "sp_allies_sum", "sp_rivals_sum", "sp_0_ally_cnt", "sp_ge1_ally_cnt", "sp_ge2_ally_cnt",
    )
    for k in scalar_keys:
        dst[k] += src[k]
    for k in (
        "turn_hist", "pm", "pw", "nmm", "nmw", "dm", "dw", "dmg", "kills",
        "cexcite", "cstall", "sac", "burn", "sp_cnt", "sp_turn", "sp_allies",
        "sp_rivals", "sp_ge1", "env_el_l2", "env_el_l3"
    ):
        dd, sd = dst[k], src[k]
        for ik, iv in sd.items():
            dd[ik] = dd.get(ik, 0) + iv
    for k in ("tv", "thp1", "thp2", "tm_av", "tm_sp", "thnd", "tbrd", "tdmg", "tdead", "tbrick", "tstall", "texc", "tburn"):
        da, sa = dst[k], src[k]
        for i in range(len(da)):
            da[i] += sa[i]


def _init_worker(json_path: str, endless_waves: bool, deck_size: int, seed_base: int, start_mode: str, ai_policy: str) -> None:
    global _WORKER_ENGINE, _WORKER_SEED_BASE
    by_id, monsters, spells, envs, leaders = load_database(json_path)
    _WORKER_ENGINE = BattleEngine(
        by_id, monsters, spells, envs, leaders,
        endless_waves=endless_waves, deck_size=deck_size,
        start_mode=start_mode, ai_policy=ai_policy
    )
    _WORKER_SEED_BASE = seed_base


def _run_chunk(task_args: Tuple[int, int]) -> dict:
    chunk_id, count = task_args
    rng = random.Random(_WORKER_SEED_BASE + chunk_id * 1000003)
    eng = _WORKER_ENGINE
    assert eng is not None

    st = new_agg_stats()
    for _ in range(count):
        winner, turns, p1, p2, _, ms = eng.play_match(rng)
        st["n"] += 1
        st["turns"] += turns
        st["turn_hist"][turns] = st["turn_hist"].get(turns, 0) + 1
        st["waves"] += max(0, p1.wave_num - 1) + max(0, p2.wave_num - 1)
        st["comebacks"] += p1.comebacks + p2.comebacks
        st["half_turns"] += ms["half_turns"]
        st["dead_turns"] += ms["dead_turns"]
        st["brick_dead_turns"] += ms["brick_dead_turns"]
        st["stall_turns"] += ms["stall_turns"]
        st["explosive_turns"] += ms["explosive_turns"]
        st["lead_changes"] += ms["lead_changes"]
        st["comeback_wins"] += ms["comeback_win"]
        st["big_comebacks"] += ms["big_comeback"]
        st["nail_biters"] += ms["nail_biter"]
        st["blowouts"] += ms["blowout"]
        st["salvations"] += ms["salvations"]
        st["multipliers"] += ms["multipliers"]
        st["sacrifices"] += ms["sacrifices"]
        st["hand_burns"] += ms["hand_burns"]
        st["p1_hp_end"] += p1.hp
        st["p2_hp_end"] += p2.hp
        st["p1_t12_dmg"] += p1.turn12_dmg
        st["p2_t12_dmg"] += p2.turn12_dmg
        if p2.t2_bonus_used:
            st["t2_bonus_used"] += 1

        st["env_active_turns"] += ms["env_active_turns"]
        st["env_benefit_turns"] += ms["env_benefit_turns"]
        st["env_backfire_turns"] += ms["env_backfire_turns"]
        st["env_l1"] += ms["env_lvl_plays"][1]
        st["env_l2"] += ms["env_lvl_plays"][2]
        st["env_l3"] += ms["env_lvl_plays"][3]
        for ek, ev in ms["env_elem_l2"].items():
            st["env_el_l2"][ek] = st["env_el_l2"].get(ek, 0) + ev
        for ek, ev in ms["env_elem_l3"].items():
            st["env_el_l3"][ek] = st["env_el_l3"].get(ek, 0) + ev

        for k in ("tv", "thp1", "thp2", "tm_av", "tm_sp", "thnd", "tbrd", "tdmg", "tdead", "tbrick", "tstall", "texc", "tburn"):
            da, sa = st[k], ms[k]
            for i in range(len(da)):
                da[i] += sa[i]

        if winner == 1:
            st["w1"] += 1
        elif winner == 2:
            st["w2"] += 1
        else:
            st["draws"] += 1

        p1_win = 1 if winner == 1 else 0
        p2_win = 1 if winner == 2 else 0

        if winner != 0:
            # Asymmetric Environment Caster Advantage
            if (p1.env_played_cnt > 0) != (p2.env_played_cnt > 0):
                st["env_caster_nm"] += 1
                if (p1.env_played_cnt > 0 and p1_win) or (p2.env_played_cnt > 0 and p2_win):
                    st["env_caster_wins"] += 1

            # Spirit presence & asymmetric Spirit vs No-Spirit Lift
            p1_has_sp = bool(p1.spirit_summons)
            p2_has_sp = bool(p2.spirit_summons)
            if p1_has_sp:
                st["sp_any_p1_m"] += 1
                st["sp_any_p1_w"] += p1_win
            if p2_has_sp:
                st["sp_any_p2_m"] += 1
                st["sp_any_p2_w"] += p2_win
            if p1_has_sp != p2_has_sp:
                st["sp_vs_none_m"] += 1
                if (p1_has_sp and p1_win) or (p2_has_sp and p2_win):
                    st["sp_vs_none_w"] += 1
            for pl, pw in ((p1, p1_win), (p2, p2_win)):
                if pl.spirit_summons:
                    if pl.spirit_ge2_allies:
                        st["sp_ge2_m"] += 1
                        st["sp_ge2_w"] += pw
                    else:
                        st["sp_lt2_m"] += 1
                        st["sp_lt2_w"] += pw

            both_played = p1.played_ids & p2.played_ids
            for pl, wflag in ((p1, p1_win), (p2, p2_win)):
                for cid in pl.played_ids:
                    st["pm"][cid] = st["pm"].get(cid, 0) + 1
                    st["pw"][cid] = st["pw"].get(cid, 0) + wflag
                    if cid not in both_played:
                        st["nmm"][cid] = st["nmm"].get(cid, 0) + 1
                        st["nmw"][cid] = st["nmw"].get(cid, 0) + wflag
                for cid in pl.drawn_ever_ids:
                    st["dm"][cid] = st["dm"].get(cid, 0) + 1
                    st["dw"][cid] = st["dw"].get(cid, 0) + wflag

        for pl in (p1, p2):
            for cid, tn, al, rv in pl.spirit_summons:
                st["sp_summons"] += 1
                st["sp_turn_sum"] += tn
                st["sp_allies_sum"] += al
                st["sp_rivals_sum"] += rv
                if al == 0:
                    st["sp_0_ally_cnt"] += 1
                else:
                    st["sp_ge1_ally_cnt"] += 1
                if al >= 2:
                    st["sp_ge2_ally_cnt"] += 1
                st["sp_cnt"][cid] = st["sp_cnt"].get(cid, 0) + 1
                st["sp_turn"][cid] = st["sp_turn"].get(cid, 0) + tn
                st["sp_allies"][cid] = st["sp_allies"].get(cid, 0) + al
                st["sp_rivals"][cid] = st["sp_rivals"].get(cid, 0) + rv
                if al >= 1:
                    st["sp_ge1"][cid] = st["sp_ge1"].get(cid, 0) + 1

            for dname, field_key in (
                (pl.card_dmg, "dmg"), (pl.card_kills, "kills"), (pl.card_excite, "cexcite"),
                (pl.card_stall, "cstall"), (pl.sacrificed_cnt, "sac"), (pl.burned_cnt, "burn"),
            ):
                dst_d = st[field_key]
                for cid, val in dname.items():
                    dst_d[cid] = dst_d.get(cid, 0) + val

    return st


# ==============================================================================
# Statistical Genius Diagnostics Engine
# ==============================================================================
def wilson_ci(wins: int, n: int, z: float = 1.96) -> Tuple[float, float]:
    if n <= 0:
        return 0.0, 100.0
    p = wins / n
    denom = 1.0 + z * z / n
    center = (p + z * z / (2.0 * n)) / denom
    half = (z * math.sqrt((p * (1.0 - p) + z * z / (4.0 * n)) / n)) / denom
    return max(0.0, (center - half) * 100.0), min(100.0, (center + half) * 100.0)


def binomial_z(wins: int, n: int, p0: float = 0.5) -> float:
    if n <= 0:
        return 0.0
    sd = math.sqrt(p0 * (1.0 - p0) / n)
    return ((wins / n) - p0) / sd if sd > 0 else 0.0


def _pct(a: float, b: float) -> float:
    return (a / b * 100.0) if b > 0 else 0.0


def _diagnose_card(
    proto: CardProto, wr: float, z: float, nmm: int, drawn: int, plays: int,
    avg_dmg: float, kills_per_play: float, sac_rate: float, exc_rate: float,
    stall_rate: float, cost_delta: float, avg_allies: float, cost_avg_stat: float
) -> Tuple[str, str, str]:
    """Produces (status_label, tag, root_cause_ar) from Unity mechanics & observed telemetry."""
    if nmm < 15:
        return "عينة غير كافية", "pending", "مشاركات أقل من 15 بدون مرآة"

    sig = abs(z) >= 2.58
    strong_sig = abs(z) >= 3.29
    reasons: List[str] = []

    if wr >= 55.0:
        lbl = f"مكسور جداً (+{wr-50:.1f}%)" if (wr >= 57.5 and sig) else f"قوي جداً (+{wr-50:.1f}%)"
        tag = "op"
    elif wr >= 52.5:
        lbl = f"قوي (+{wr-50:.1f}%)"
        tag = "strong"
    elif wr >= 47.5:
        lbl = "متوازن"
        tag = "balanced"
    elif wr >= 45.0:
        lbl = "منخفض قليلاً"
        tag = "weak"
    else:
        lbl = f"ضعيف جداً ({wr-50:.1f}%)" if (wr <= 42.5 and sig) else f"ضعيف ({wr-50:.1f}%)"
        tag = "weak"

    if proto.card_type == "monster":
        stat_sum = proto.atk + proto.def_stat
        if wr >= 54.0:
            if stat_sum > cost_avg_stat + 6:
                reasons.append(f"إحصائيات أعلى من متوسط تكلفته ({stat_sum} ضد {cost_avg_stat:.0f})")
            if len(proto.actives) >= 2:
                reasons.append("يمتلك مهارتين نشطتين تُنفذان معاً في دور واحد")
            if "Quick Charge" in proto.passive_tags:
                reasons.append("هجوم فوري Quick Charge")
            if proto.passive_tags & {"Light of Truth", "Tidal Bounce", "Total Eclipse", "Frost Aura", "Spectral Presence", "Shield of Salvation", "Mana Recovery", "Nourishing Roots", "Arcane Overflow", "Spell Multiplier"}:
                reasons.append("كامنة تحكم/قيمة فورية عالية")
            if avg_dmg >= 38:
                reasons.append(f"متوسط ضرر مرتفع ({avg_dmg:.0f}/لعب)")
        elif wr <= 46.5:
            if proto.has_cosmetic_passive:
                reasons.append("كامنة غير مفعّلة في كود Unity (تجميلية فقط)")
            if stat_sum < cost_avg_stat - 5:
                reasons.append(f"هجوم/دفاع أقل من متوسط تكلفة {proto.cost} ({stat_sum} ضد {cost_avg_stat:.0f})")
            if sac_rate >= 28.0:
                reasons.append(f"يُضحّى به كثيراً في اليد ({sac_rate:.0f}%)")
            if proto.cost >= 6 and plays < max(1, drawn * 0.45):
                reasons.append("ثقيل التكلفة (Brick) بطيء النزول")
            if stall_rate >= 28.0:
                reasons.append(f"معدل جمود عالٍ ({stall_rate:.0f}%)")
    elif proto.card_type == "spell":
        if wr >= 54.0:
            reasons.append(f"تأثير تعويذة عالٍ ({SPELL_TYPE_NAMES.get(proto.spell_type, '')}) بتكلفة {proto.cost}")
        elif wr <= 46.5:
            if sac_rate >= 25.0:
                reasons.append(f"تبقى ميتة في اليد وتُضحى ({sac_rate:.0f}%)")
            else:
                reasons.append(f"أضعف من بدائلها بتكلفة {proto.cost} ({cost_delta:+.1f}%)")
    elif proto.card_type == "leader":
        if wr >= 54.0:
            reasons.append("قائد ذو أثر عالمي قوي لا يشترط كثافة عشيرة كبيرة")
        elif wr <= 46.5:
            reasons.append(f"معتمد على عشيرة {TRIBE_AR.get(proto.tribe, proto.tribe)} (متوسط الحلفاء عند الاستدعاء {avg_allies:.2f} فقط في دك 50 عشوائي)")
    else:
        if wr >= 53.0:
            reasons.append(f"عنصر {ELEM_AR.get(proto.element, proto.element)} يستفيد من تغطية وحوشه ودفع الهجوم")
        elif wr <= 47.0:
            reasons.append(f"بيئة {ELEM_AR.get(proto.element, proto.element)} تنعكس أحياناً على الخصم أو تغيب أهدافها")

    if not reasons:
        if strong_sig and wr > 52.5:
            reasons.append("تفوّق إحصائي واضح ومستقر")
        elif strong_sig and wr < 47.5:
            reasons.append("تأثير ميداني أقل من متوسط تكلفة الورقة")
        else:
            reasons.append("ضمن النطاق الطبيعي للتوازن")
    return lbl, tag, " • ".join(reasons)


def analyze_simulation(
    st: dict, by_id: Dict[int, CardProto], monsters: List[CardProto],
    spells: List[CardProto], envs: List[CardProto], leaders: List[CardProto]
) -> dict:
    n = max(1, st["n"])
    decided = max(1, st["w1"] + st["w2"])
    ht = max(1, st["half_turns"])

    # Cost-tier average baseline ATK+DEF and card WR baselines
    cost_stat_avgs: Dict[int, float] = {}
    for cost in range(1, 9):
        cm = [m for m in monsters if m.cost == cost]
        cost_stat_avgs[cost] = (sum(m.atk + m.def_stat for m in cm) / len(cm)) if cm else 0.0

    cost_wr_sums: Dict[Tuple[str, int], List[int]] = {}
    for cid, proto in by_id.items():
        k = (proto.card_type, proto.cost)
        if k not in cost_wr_sums:
            cost_wr_sums[k] = [0, 0]
        cost_wr_sums[k][0] += st["nmw"].get(cid, 0)
        cost_wr_sums[k][1] += st["nmm"].get(cid, 0)

    card_rows: List[dict] = []
    for cid in sorted(by_id.keys()):
        p = by_id[cid]
        nmm = st["nmm"].get(cid, 0)
        nmw = st["nmw"].get(cid, 0)
        pm = st["pm"].get(cid, 0)
        pw = st["pw"].get(cid, 0)
        dm = st["dm"].get(cid, 0)
        dw = st["dw"].get(cid, 0)
        tot_dmg = st["dmg"].get(cid, 0)
        kills = st["kills"].get(cid, 0)
        sac = st["sac"].get(cid, 0)
        burn = st["burn"].get(cid, 0)

        wr = (nmw / nmm * 100.0) if nmm > 0 else 50.0
        all_wr = (pw / pm * 100.0) if pm > 0 else 50.0
        dr_wr = (dw / dm * 100.0) if dm > 0 else 50.0
        ci_lo, ci_hi = wilson_ci(nmw, nmm)
        z = binomial_z(nmw, nmm)
        avg_dmg = (tot_dmg / pm) if pm > 0 else 0.0
        kills_pp = (kills / pm) if pm > 0 else 0.0
        exc_rate = (st["cexcite"].get(cid, 0) / max(1, pm)) * 100.0 if pm > 0 else 0.0
        stall_rate = (st["cstall"].get(cid, 0) / max(1, max(pm, dm))) * 100.0 if (pm > 0 or dm > 0) else 0.0
        sac_rate = (sac / max(1, dm)) * 100.0 if dm > 0 else 0.0
        burn_rate = (burn / max(1, dm)) * 100.0 if dm > 0 else 0.0

        cw, cn = cost_wr_sums.get((p.card_type, p.cost), (0, 0))
        cost_base_wr = (cw / cn * 100.0) if cn > 0 else 50.0
        cost_delta = wr - cost_base_wr
        sp_c = st["sp_cnt"].get(cid, 0)
        avg_sp_allies = (st["sp_allies"].get(cid, 0) / sp_c) if sp_c > 0 else 0.0
        avg_sp_turn = (st["sp_turn"].get(cid, 0) / sp_c) if sp_c > 0 else 0.0

        st_lbl, tag, diag = _diagnose_card(
            p, wr, z, nmm, dm, pm, avg_dmg, kills_pp, sac_rate, exc_rate,
            stall_rate, cost_delta, avg_sp_allies, cost_stat_avgs.get(p.cost, 0.0)
        )
        card_rows.append({
            "cid": cid, "name": p.name, "card_type": p.card_type, "type_ar": TYPE_AR.get(p.card_type, p.card_type),
            "element": p.element, "elem_ar": ELEM_AR.get(p.element, p.element),
            "tribe": p.tribe, "tribe_ar": TRIBE_AR.get(p.tribe, p.tribe) if p.tribe else "—",
            "role": p.role, "role_ar": ROLE_AR.get(p.role, p.role),
            "tier": p.tier_num, "cost": p.cost, "atk": p.atk, "def": p.def_stat,
            "stats": f"{p.atk}/{p.def_stat}" if p.card_type == "monster" else "-",
            "skills": p.summary_text, "has_cosmetic": p.has_cosmetic_passive, "actives_cnt": len(p.actives),
            "plays": pm, "nmm": nmm, "nmw": nmw, "drawn": dm,
            "wr": wr, "all_wr": all_wr, "dr_wr": dr_wr, "ci_lo": ci_lo, "ci_hi": ci_hi, "z": z,
            "cost_delta": cost_delta, "avg_dmg": avg_dmg, "kills": kills,
            "exc_rate": exc_rate, "stall_rate": stall_rate, "sac_rate": sac_rate, "burn_rate": burn_rate,
            "sp_allies": avg_sp_allies, "sp_turn": avg_sp_turn,
            "status_ar": st_lbl, "tag": tag, "diagnosis": diag,
            "passives_full": " ; ".join(p.passives), "actives_full": " ; ".join(p.actives),
        })

    # Turn Distribution & Percentiles
    th = st["turn_hist"]
    sorted_turns = sorted(th.keys())
    cum = 0
    p10 = p25 = p50 = p75 = p90 = 0
    for tval in sorted_turns:
        cum += th[tval]
        frac = cum / n
        if not p10 and frac >= 0.10:
            p10 = tval
        if not p25 and frac >= 0.25:
            p25 = tval
        if not p50 and frac >= 0.50:
            p50 = tval
        if not p75 and frac >= 0.75:
            p75 = tval
        if not p90 and frac >= 0.90:
            p90 = tval
    t_min = sorted_turns[0] if sorted_turns else 0
    t_max = sorted_turns[-1] if sorted_turns else 0
    t_avg = st["turns"] / n
    r_avg = t_avg / 2.0
    r_med = (p50 + 1) // 2

    blitz_cnt = sum(v for k, v in th.items() if k <= 8)
    short_cnt = sum(v for k, v in th.items() if 9 <= k <= 14)
    ideal_cnt = sum(v for k, v in th.items() if 15 <= k <= 26)
    long_cnt = sum(v for k, v in th.items() if 27 <= k <= 38)
    mara_cnt = sum(v for k, v in th.items() if k >= 39)
    draw_rate = st["draws"] / n * 100.0

    # P1 vs P2
    p1_pct = st["w1"] / n * 100.0
    p2_pct = st["w2"] / n * 100.0
    p1_ci_lo, p1_ci_hi = wilson_ci(st["w1"], decided)
    p1_z = binomial_z(st["w1"], decided)
    p1_avg_hp = st["p1_hp_end"] / n
    p2_avg_hp = st["p2_hp_end"] / n

    # Group Aggregations
    def group_table(items: List[dict], key_fn, label_fn) -> List[dict]:
        buckets: Dict[Any, List[dict]] = {}
        for r in items:
            k = key_fn(r)
            buckets.setdefault(k, []).append(r)
        res = []
        for k, lst in buckets.items():
            nmm_s = sum(x["nmm"] for x in lst)
            nmw_s = sum(x["nmw"] for x in lst)
            pm_s = sum(x["plays"] for x in lst)
            dm_s = sum(x["drawn"] for x in lst)
            wr_s = (nmw_s / nmm_s * 100.0) if nmm_s > 0 else 50.0
            ci_l, ci_h = wilson_ci(nmw_s, nmm_s)
            z_s = binomial_z(nmw_s, nmm_s)
            dmg_s = sum(x["avg_dmg"] * x["plays"] for x in lst) / max(1, pm_s)
            res.append({
                "key": k, "label": label_fn(k), "count": len(lst), "plays": pm_s,
                "drawn": dm_s, "nmm": nmm_s, "wr": wr_s, "ci_lo": ci_l, "ci_hi": ci_h,
                "z": z_s, "avg_dmg": dmg_s,
                "op_cnt": sum(1 for x in lst if x["nmm"] >= 15 and x["wr"] > 55.0),
                "weak_cnt": sum(1 for x in lst if x["nmm"] >= 15 and x["wr"] < 45.0),
            })
        return res

    mon_rows = [r for r in card_rows if r["card_type"] == "monster"]
    sp_rows = [r for r in card_rows if r["card_type"] == "spell"]
    env_rows = [r for r in card_rows if r["card_type"] == "env"]
    ldr_rows = [r for r in card_rows if r["card_type"] == "leader"]

    tribes_tbl = sorted(group_table(mon_rows, lambda r: r["tribe"], lambda k: TRIBE_AR.get(k, k)), key=lambda x: x["wr"], reverse=True)
    elems_tbl = sorted(group_table(mon_rows, lambda r: r["element"], lambda k: ELEM_AR.get(k, k)), key=lambda x: x["wr"], reverse=True)
    cost_tbl = sorted(group_table(mon_rows, lambda r: r["cost"], lambda k: f"تكلفة {k}"), key=lambda x: x["key"])
    roles_tbl = sorted(group_table(mon_rows, lambda r: r["role"], lambda k: ROLE_AR.get(k, k)), key=lambda x: x["wr"], reverse=True)

    spell_type_tbl = []
    for sp_proto in spells:
        pass
    by_sp_t: Dict[int, List[dict]] = {}
    for r in sp_rows:
        st_code = by_id[r["cid"]].spell_type
        by_sp_t.setdefault(st_code, []).append(r)
    for st_code, lst in by_sp_t.items():
        nmm_s = sum(x["nmm"] for x in lst)
        nmw_s = sum(x["nmw"] for x in lst)
        wr_s = (nmw_s / nmm_s * 100.0) if nmm_s > 0 else 50.0
        spell_type_tbl.append({
            "type_id": st_code, "name": SPELL_TYPE_NAMES.get(st_code, f"Type {st_code}"),
            "count": len(lst), "plays": sum(x["plays"] for x in lst), "nmm": nmm_s,
            "wr": wr_s, "z": binomial_z(nmw_s, nmm_s),
        })
    spell_type_tbl.sort(key=lambda x: x["wr"], reverse=True)

    # Environment Summary
    env_tot_plays = st["env_l1"] + st["env_l2"] + st["env_l3"]
    env_caster_wr = _pct(st["env_caster_wins"], st["env_caster_nm"]) if st["env_caster_nm"] else 50.0
    env_ben_share = _pct(st["env_benefit_turns"], st["env_benefit_turns"] + st["env_backfire_turns"])

    # Spirit Summary
    sp_lift_wr = _pct(st["sp_vs_none_w"], st["sp_vs_none_m"]) if st["sp_vs_none_m"] else 50.0
    sp_lift_z = binomial_z(st["sp_vs_none_w"], st["sp_vs_none_m"])
    sp_ge2_wr = _pct(st["sp_ge2_w"], st["sp_ge2_m"]) if st["sp_ge2_m"] else 50.0
    sp_lt2_wr = _pct(st["sp_lt2_w"], st["sp_lt2_m"]) if st["sp_lt2_m"] else 50.0

    # Turn-by-turn profile rows (1..MAX_TURN_BIN)
    turn_curve: List[dict] = []
    for b in range(1, MAX_TURN_BIN + 1):
        v = st["tv"][b]
        if v == 0:
            continue
        turn_curve.append({
            "turn": b, "round": (b + 1) // 2, "visits": v, "share_pct": v / n * 100.0,
            "p1_hp": st["thp1"][b] / v, "p2_hp": st["thp2"][b] / v,
            "mana_av": st["tm_av"][b] / v, "mana_sp": st["tm_sp"][b] / v,
            "mana_eff": _pct(st["tm_sp"][b], st["tm_av"][b]),
            "hand": st["thnd"][b] / v, "board": st["tbrd"][b] / v,
            "dmg": st["tdmg"][b] / v,
            "dead_pct": st["tdead"][b] / v * 100.0,
            "brick_pct": st["tbrick"][b] / v * 100.0,
            "stall_pct": st["tstall"][b] / v * 100.0,
            "excite_pct": st["texc"][b] / v * 100.0,
            "burn_rate": st["tburn"][b] / v,
        })

    # Global Health / Excitement / Boredom Scores
    analyzed_cards = [r for r in card_rows if r["nmm"] >= 20]
    analyzed_m = [r for r in mon_rows if r["nmm"] >= 20]
    bal_m = sum(1 for r in analyzed_m if 45.0 <= r["wr"] <= 55.0)
    op_m = sorted([r for r in analyzed_m if r["wr"] > 55.0], key=lambda x: x["wr"], reverse=True)
    weak_m = sorted([r for r in analyzed_m if r["wr"] < 45.0], key=lambda x: x["wr"])
    op_all = sorted([r for r in analyzed_cards if r["wr"] > 55.0], key=lambda x: x["wr"], reverse=True)
    weak_all = sorted([r for r in analyzed_cards if r["wr"] < 45.0], key=lambda x: x["wr"])

    exp_pct = st["explosive_turns"] / ht * 100.0
    dead_pct = st["dead_turns"] / ht * 100.0
    brick_pct = st["brick_dead_turns"] / ht * 100.0
    stall_pct = st["stall_turns"] / ht * 100.0
    boredom_pct = dead_pct + stall_pct
    cb_pct = st["comeback_wins"] / n * 100.0
    big_cb_pct = st["big_comebacks"] / n * 100.0
    nb_pct = st["nail_biters"] / n * 100.0
    bo_pct = st["blowouts"] / n * 100.0
    lc_avg = st["lead_changes"] / n

    # Composite Scores (/100)
    p1_gap = abs(p1_pct - p2_pct)
    bal_ratio = (len([r for r in analyzed_cards if 45.0 <= r["wr"] <= 55.0]) / max(1, len(analyzed_cards))) * 100.0
    balance_score = max(0.0, min(100.0, bal_ratio - p1_gap * 1.4 - draw_rate * 1.5))
    excite_score = max(0.0, min(100.0, exp_pct * 1.3 + cb_pct * 0.9 + nb_pct * 0.5 + lc_avg * 14.0 - bo_pct * 0.5))

    # Peak excitement & boredom phases
    peak_excite = max(turn_curve, key=lambda x: x["excite_pct"]) if turn_curve else None
    peak_dead = max(turn_curve, key=lambda x: x["dead_pct"]) if turn_curve else None
    peak_stall = max(turn_curve, key=lambda x: x["stall_pct"]) if turn_curve else None

    # Auto-generated Arabic Executive Recommendations
    recs: List[str] = []
    if p1_gap > 4.0:
        fav = "اللاعب الأول" if p1_pct > p2_pct else "اللاعب الثاني"
        recs.append(f"⚠️ فجوة أسبقية الدور: {fav} يتقدم بـ {p1_gap:.1f}% ({p1_pct:.1f}% ضد {p2_pct:.1f}%). إذا بقي الفرق >3% بعد ضبط الافتتاحية، عدّل تعويض المانا أو حجم اليد الافتتاحي.")
    else:
        recs.append(f"✅ توازن اللاعب الأول والثاني ممتاز: {p1_pct:.2f}% ضد {p2_pct:.2f}% (فارق {p1_gap:.2f}% فقط) — قاعدة 5 مقابل 6 + مانا T2 تعمل بدقة عالية.")

    if sp_lt2_wr < 51.0 and st["sp_summons"] > 0:
        zero_ally_pct = st["sp_0_ally_cnt"] / max(1, st["sp_summons"]) * 100.0
        recs.append(
            f"👑 مشكلة جوهرية في أرواح القادة: عند النزول في دك عشوائي من 50 بطاقة، {zero_ally_pct:.1f}% من استدعاءات القائد تحدث مع 0 حليف من عشيرته باللوحة! "
            f"(نسبة الفوز مع ≥2 حليف عشيرة = {sp_ge2_wr:.1f}% مقابل {sp_lt2_wr:.1f}% مع 0-1 حليف). الحل المقترح: السماح ببناء الدك حسب العشائر (Tribe-Focused Deck Builder) أو إضافة ميزة 'استدعاء/سحب وحش من عشيرة القائد' عند نزول الروح."
        )

    cosm_count = sum(1 for r in mon_rows if r["has_cosmetic"] and r["wr"] < 47.0)
    if cosm_count > 0:
        recs.append(f"🧩 تم اكتشاف وحوش تحمل كوامن غير مبرمجة فعلياً في CombatSystem.cs (مثل Stone Roots / Earth Resistance / Wind Resistance) مما تسبب في هبوط {cosm_count} وحش منها تحت 47% WR — إما تفعيل كوامنها في C# أو استبدالها بكوامن مفعّلة.")

    if op_m:
        top3 = ", ".join(f"#{r['cid']} ({r['wr']:.1f}%)" for r in op_m[:5])
        recs.append(f"🔥 وحوش مكسورة تحتاج Nerf: {top3} — غالباً بسبب تزامن مهارتين نشطتين أو إحصائيات/كوامن فوق ميزانية التكلفة.")
    if weak_m:
        bot3 = ", ".join(f"#{r['cid']} ({r['wr']:.1f}%)" for r in weak_m[:5])
        recs.append(f"❄️ وحوش ضعيفة تحتاج Buff: {bot3} — معظمها يُضحى به في اليد أو يعاني من كوامن غير منفّذة أو عجز هجوم/دفاع لمستواه.")

    env_act_pct = _pct(st["env_active_turns"], ht)
    recs.append(
        f"🌍 البيئات نشطة في {env_act_pct:.1f}% من الأدوار (L1={st['env_l1']:,} | L2={st['env_l2']:,} | L3={st['env_l3']:,})؛ "
        f"نسبة فوز مُفعّل البيئة {env_caster_wr:.1f}% — الارتقاء إلى L2/L3 نادر في الدك العشوائي لأن وجود 3 بيئات مختلفة في 50 بطاقة يقلل تكرار نفس العنصر (يُفضّل اختيار بيئات العناصر عند بناء الدك)."
    )

    if st["hand_burns"] / n > 0.4:
        recs.append(f"🔥 حد اليد 7 يحرق {st['hand_burns']/n:.2f} كارت/مباراة — يرفع الحماس ضد التكديس لكنه يزيد حاجة توازن الكروت الغالية.")
    if dead_pct > 15.0 or stall_pct > 25.0:
        recs.append(f"💤 أوقات الملل ({boredom_pct:.1f}%: {dead_pct:.1f}% ميت + {stall_pct:.1f}% جمود) أعلى من المستهدف؛ فكّر في تخفيض تكلفة 1 من كروت التكلفة 6-8 أو زيادة سحب الورقة في المراحل المتأخرة.")
    else:
        recs.append(f"⚡ إيقاع ممتع ومنخفض الملل: أدوار متفجرة {exp_pct:.1f}% | انقلابات صدارة {lc_avg:.2f}/مباراة | فوز بعد تأخر ≥75 HP: {cb_pct:.1f}% | جمود وميت: {boredom_pct:.1f}%.")

    return {
        "n": n, "decided": decided, "ht": ht,
        "w1": st["w1"], "w2": st["w2"], "draws": st["draws"], "draw_rate": draw_rate,
        "p1_pct": p1_pct, "p2_pct": p2_pct, "p1_ci": (p1_ci_lo, p1_ci_hi), "p1_z": p1_z,
        "p1_avg_hp": p1_avg_hp, "p2_avg_hp": p2_avg_hp,
        "p1_t12_dmg": st["p1_t12_dmg"] / n, "p2_t12_dmg": st["p2_t12_dmg"] / n,
        "t2_bonus_used_pct": st["t2_bonus_used"] / n * 100.0,
        "t_min": t_min, "t_avg": t_avg, "t_p10": p10, "t_p25": p25, "t_p50": p50, "t_p75": p75, "t_p90": p90, "t_max": t_max,
        "r_avg": r_avg, "r_med": r_med,
        "blitz_pct": blitz_cnt / n * 100.0, "short_pct": short_cnt / n * 100.0,
        "ideal_pct": ideal_cnt / n * 100.0, "long_pct": long_cnt / n * 100.0, "mara_pct": mara_cnt / n * 100.0,
        "turn_hist": {k: th[k] for k in sorted_turns},
        "exp_pct": exp_pct, "dead_pct": dead_pct, "brick_pct": brick_pct, "stall_pct": stall_pct, "boredom_pct": boredom_pct,
        "cb_pct": cb_pct, "big_cb_pct": big_cb_pct, "nb_pct": nb_pct, "bo_pct": bo_pct, "lc_avg": lc_avg,
        "salvations_pm": st["salvations"] / n, "multipliers_pm": st["multipliers"] / n,
        "sac_pm": st["sacrifices"] / n, "burn_pm": st["hand_burns"] / n, "waves_pm": st["waves"] / n,
        "env_act_pct": env_act_pct, "env_tot_plays": env_tot_plays,
        "env_l1": st["env_l1"], "env_l2": st["env_l2"], "env_l3": st["env_l3"],
        "env_caster_nm": st["env_caster_nm"], "env_caster_wr": env_caster_wr, "env_ben_share": env_ben_share,
        "env_el_l2": st["env_el_l2"], "env_el_l3": st["env_el_l3"],
        "sp_summons_pm": st["sp_summons"] / n,
        "sp_avg_turn": st["sp_turn_sum"] / max(1, st["sp_summons"]),
        "sp_avg_allies": st["sp_allies_sum"] / max(1, st["sp_summons"]),
        "sp_avg_rivals": st["sp_rivals_sum"] / max(1, st["sp_summons"]),
        "sp_0_ally_pct": _pct(st["sp_0_ally_cnt"], st["sp_summons"]),
        "sp_ge1_ally_pct": _pct(st["sp_ge1_ally_cnt"], st["sp_summons"]),
        "sp_ge2_ally_pct": _pct(st["sp_ge2_ally_cnt"], st["sp_summons"]),
        "sp_vs_none_m": st["sp_vs_none_m"], "sp_vs_none_wr": sp_lift_wr, "sp_vs_none_z": sp_lift_z,
        "sp_ge2_wr": sp_ge2_wr, "sp_ge2_m": st["sp_ge2_m"],
        "sp_lt2_wr": sp_lt2_wr, "sp_lt2_m": st["sp_lt2_m"],
        "sp_any_p1_wr": _pct(st["sp_any_p1_w"], st["sp_any_p1_m"]),
        "sp_any_p2_wr": _pct(st["sp_any_p2_w"], st["sp_any_p2_m"]),
        "balance_score": balance_score, "excite_score": excite_score, "bal_ratio": bal_ratio,
        "card_rows": card_rows, "mon_rows": mon_rows, "sp_rows": sp_rows, "env_rows": env_rows, "ldr_rows": ldr_rows,
        "op_m": op_m, "weak_m": weak_m, "op_all": op_all, "weak_all": weak_all,
        "bal_m_cnt": bal_m, "analyzed_m_cnt": len(analyzed_m), "analyzed_cnt": len(analyzed_cards),
        "tribes_tbl": tribes_tbl, "elems_tbl": elems_tbl, "cost_tbl": cost_tbl, "roles_tbl": roles_tbl,
        "spell_type_tbl": spell_type_tbl, "turn_curve": turn_curve,
        "peak_excite": peak_excite, "peak_dead": peak_dead, "peak_stall": peak_stall,
        "recs": recs,
    }


# ==============================================================================
# Multi-Sheet Excel & Interactive Dark-Mode HTML Dashboard Exporters
# ==============================================================================
def export_excel_report(path: str, diag: dict, meta: dict) -> None:
    if not HAS_OPENPYXL:
        raise RuntimeError("openpyxl is not installed")
    wb = openpyxl.Workbook()
    fill_h = PatternFill("solid", fgColor="0F172A")
    font_h = Font(bold=True, color="38BDF8")
    fill_sec = PatternFill("solid", fgColor="1E293B")
    font_sec = Font(bold=True, color="FDE68A")

    def style_hdr(ws, ncols: int, row_i: int = 1) -> None:
        for ci in range(1, ncols + 1):
            c = ws.cell(row_i, ci)
            c.fill = fill_h
            c.font = font_h
            c.alignment = Alignment(horizontal="center")

    # Sheet 1: Executive Summary
    ws1 = wb.active
    ws1.title = "الملخص التشخيصي"
    ws1.append(["مؤشر الأداء / التحليل", "القيمة", "التقييم / الدلالة"])
    style_hdr(ws1, 3)
    kpis = [
        ("وضع البداية", START_MODES.get(meta["start_mode"], meta["start_mode"]), ""),
        ("سياسة الذكاء الاصطناعي", AI_POLICIES.get(meta["ai_policy"], meta["ai_policy"]), ""),
        ("عدد المعارك المحاكاة", f"{diag['n']:,}", f"تاريخ: {dt.datetime.now().strftime('%Y-%m-%d %H:%M')}"),
        ("مؤشر صحة التوازن الكلي", f"{diag['balance_score']:.1f} / 100", f"متوازن: {diag['bal_ratio']:.1f}% من {diag['analyzed_cnt']} بطاقة"),
        ("مؤشر الحماس والمتعة", f"{diag['excite_score']:.1f} / 100", f"أدوار متفجرة {diag['exp_pct']:.1f}% • انقلابات {diag['lc_avg']:.2f}/مباراة"),
        ("فوز اللاعب الأول مقابل الثاني", f"{diag['p1_pct']:.2f}% / {diag['p2_pct']:.2f}%", f"تعادل {diag['draw_rate']:.2f}% | CI95% أ: [{diag['p1_ci'][0]:.2f}%, {diag['p1_ci'][1]:.2f}%] | Z={diag['p1_z']:.2f}"),
        ("متوسط الجولات (Turns Unity / Rounds)", f"{diag['t_avg']:.2f} دور ({diag['r_avg']:.2f} جولة كاملة)", f"الوسيط P50: {diag['t_p50']} ({diag['r_med']} جولات) | P25-P75: {diag['t_p25']}-{diag['t_p75']} | المدى: {diag['t_min']}-{diag['t_max']}"),
        ("توزيع أطوال المباريات", f"سريعة(≤8): {diag['blitz_pct']:.1f}% | المثالية(15-26): {diag['ideal_pct']:.1f}%", f"قصيرة(9-14): {diag['short_pct']:.1f}% | طويلة(27-38): {diag['long_pct']:.1f}% | ماراثون(≥39): {diag['mara_pct']:.1f}%"),
        ("أوقات الملل والجمود", f"{diag['boredom_pct']:.2f}%", f"أدوار ميتة {diag['dead_pct']:.2f}% (منها Brick مع مانا {diag['brick_pct']:.2f}%) | جمود لوحة {diag['stall_pct']:.2f}%"),
        ("لحظات الحماس والقلب", f"فوز بعد تأخر≥75HP: {diag['cb_pct']:.1f}% | تقارب≤90HP: {diag['nb_pct']:.1f}%", f"تضاعف/مباراة {diag['multipliers_pm']:.2f} | إنقاذ/مباراة {diag['salvations_pm']:.2f} | حرق يد7/مباراة {diag['burn_pm']:.2f}"),
        ("البيئات (Coverage & Lvl1/2/3)", f"نشطة في {diag['env_act_pct']:.1f}% من الأدوار", f"L1={diag['env_l1']:,} • L2={diag['env_l2']:,} • L3={diag['env_l3']:,} | WR مُفعّل البيئة={diag['env_caster_wr']:.2f}% | صافي فائدة اللوحة {diag['env_ben_share']:.1f}%"),
        ("أرواح القادة (Lift & Synergy)", f"تأثير الروح ضد بدون روح: {diag['sp_vs_none_wr']:.2f}% (Z={diag['sp_vs_none_z']:.2f})", f"متوسط الحلفاء عند النزول={diag['sp_avg_allies']:.2f} | 0 حليف={diag['sp_0_ally_pct']:.1f}% | WR ≥2 حلفاء={diag['sp_ge2_wr']:.1f}% مقابل 0-1={diag['sp_lt2_wr']:.1f}%"),
    ]
    for row in kpis:
        ws1.append(list(row))
    ws1.append([])
    ws1.append(["التوصيات التلقائية العاجلة", "", ""])
    for ci in range(1, 4):
        ws1.cell(ws1.max_row, ci).fill = fill_sec
        ws1.cell(ws1.max_row, ci).font = font_sec
    for rec in diag["recs"]:
        ws1.append([rec, "", ""])

    # Sheet 2: All 470 Cards
    ws2 = wb.create_sheet("تحليل البطاقات الكامل (470)")
    hdr2 = [
        "المعرّف", "الاسم", "النوع", "العنصر", "الفئة", "الدور", "المستوى", "التكلفة",
        "الهجوم", "الدفاع", "الكوامن", "النشطة / التأثير", "مشاركات الكل", "بدون مرآة",
        "انتصارات بدون مرآة", "نسبة الفوز %", "الحد الأدنى CI%", "الحد الأعلى CI%", "Z-Score",
        "فارق عن متوسط تكلفته %", "نسبة فوز الكل %", "مرات السحب", "نسبة التضحية %", "نسبة الحرق %",
        "متوسط الضرر", "القتلى", "الحماس %", "الجمود %", "حالة التوازن", "تشخيص السبب الجذري"
    ]
    ws2.append(hdr2)
    style_hdr(ws2, len(hdr2))
    for r in diag["card_rows"]:
        ws2.append([
            r["cid"], r["name"], r["type_ar"], r["elem_ar"], r["tribe_ar"], r["role_ar"],
            r["tier"], r["cost"], r["atk"], r["def"], r["passives_full"], r["actives_full"],
            r["plays"], r["nmm"], r["nmw"], round(r["wr"], 2), round(r["ci_lo"], 2), round(r["ci_hi"], 2),
            round(r["z"], 2), round(r["cost_delta"], 2), round(r["all_wr"], 2), r["drawn"],
            round(r["sac_rate"], 2), round(r["burn_rate"], 2), round(r["avg_dmg"], 2), r["kills"],
            round(r["exc_rate"], 2), round(r["stall_rate"], 2), r["status_ar"], r["diagnosis"]
        ])
    ws2.freeze_panes = "A2"

    # Sheet 3: 20 Commander Spirits + 10 Clans
    ws3 = wb.create_sheet("القادة والعشائر")
    hdr3 = ["المعرّف", "القائد", "العنصر", "العشيرة", "التكلفة", "الاستدعاءات", "متوسط دور النزول", "متوسط حلفاء العشيرة", "نسبة الفوز بدون مرآة %", "Z-Score", "التضحية %", "الحالة", "التشخيص"]
    ws3.append(hdr3)
    style_hdr(ws3, len(hdr3))
    for r in sorted(diag["ldr_rows"], key=lambda x: x["wr"], reverse=True):
        ws3.append([
            r["cid"], r["name"], r["elem_ar"], r["tribe_ar"], r["cost"], r["plays"],
            round(r["sp_turn"], 2), round(r["sp_allies"], 2), round(r["wr"], 2),
            round(r["z"], 2), round(r["sac_rate"], 2), r["status_ar"], r["diagnosis"]
        ])
    ws3.append([])
    ws3.append(["ملخص العشائر (الوحوش)", "عدد البطاقات", "مشاركات", "بدون مرآة", "نسبة الفوز %", "Z-Score", "قوية جداً (>55%)", "ضعيفة (<45%)", "متوسط الضرر", "", "", "", ""])
    style_hdr(ws3, 9, ws3.max_row)
    for t in diag["tribes_tbl"]:
        ws3.append([t["label"], t["count"], t["plays"], t["nmm"], round(t["wr"], 2), round(t["z"], 2), t["op_cnt"], t["weak_cnt"], round(t["avg_dmg"], 1)])

    # Sheet 4: Environments + Elements + Costs + Spells
    ws4 = wb.create_sheet("البيئات والعناصر والتكلفة")
    hdr4 = ["المعرّف", "البيئة", "العنصر", "التكلفة", "مرات التفعيل", "ترقيات L2", "ترقيات L3", "نسبة الفوز بدون مرآة %", "Z-Score", "التضحية %", "التشخيص"]
    ws4.append(hdr4)
    style_hdr(ws4, len(hdr4))
    for r in sorted(diag["env_rows"], key=lambda x: x["wr"], reverse=True):
        ws4.append([
            r["cid"], r["name"], r["elem_ar"], r["cost"], r["plays"],
            diag["env_el_l2"].get(r["element"], 0), diag["env_el_l3"].get(r["element"], 0),
            round(r["wr"], 2), round(r["z"], 2), round(r["sac_rate"], 2), r["diagnosis"]
        ])
    ws4.append([])
    ws4.append(["الفئة / المنحنى", "البند", "عدد الكروت", "مشاركات", "نسبة الفوز %", "Z-Score", "قوي جداً", "ضعيف", "متوسط الضرر", "", ""])
    style_hdr(ws4, 9, ws4.max_row)
    for e in diag["elems_tbl"]:
        ws4.append(["عنصر", e["label"], e["count"], e["plays"], round(e["wr"], 2), round(e["z"], 2), e["op_cnt"], e["weak_cnt"], round(e["avg_dmg"], 1)])
    for c in diag["cost_tbl"]:
        ws4.append(["تكلفة", c["label"], c["count"], c["plays"], round(c["wr"], 2), round(c["z"], 2), c["op_cnt"], c["weak_cnt"], round(c["avg_dmg"], 1)])
    for spt in diag["spell_type_tbl"]:
        ws4.append(["قالب تعويذة", spt["name"], spt["count"], spt["plays"], round(spt["wr"], 2), round(spt["z"], 2), "-", "-", "-"])

    # Sheet 5: Turn-by-Turn Profile & Histogram
    ws5 = wb.create_sheet("إيقاع الجولات والحماس والملل")
    hdr5 = ["الدور (Turn)", "الجولة الكاملة", "نسبة الوصول %", "صحة الأول", "صحة الثاني", "المانا المتاحة", "المانا المنفقة", "كفاءة المانا %", "اللوحة", "اليد", "الضرر/دور", "حماس %", "ميت %", "Brick مع مانا %", "جمود %", "حرق يد7"]
    ws5.append(hdr5)
    style_hdr(ws5, len(hdr5))
    for tc in diag["turn_curve"]:
        ws5.append([
            tc["turn"], tc["round"], round(tc["share_pct"], 2), round(tc["p1_hp"], 1), round(tc["p2_hp"], 1),
            round(tc["mana_av"], 2), round(tc["mana_sp"], 2), round(tc["mana_eff"], 1),
            round(tc["board"], 2), round(tc["hand"], 2), round(tc["dmg"], 1),
            round(tc["excite_pct"], 1), round(tc["dead_pct"], 1), round(tc["brick_pct"], 1),
            round(tc["stall_pct"], 1), round(tc["burn_rate"], 3)
        ])

    # Sheet 6: Top 30 OP & Bottom 30 Weak Monsters
    ws6 = wb.create_sheet("المكسور والضعيف وأسبابه")
    hdr6 = ["التصنيف", "المعرّف", "العنصر", "الفئة", "التكلفة", "هجوم/دفاع", "نسبة الفوز %", "CI95%", "Z-Score", "فارق التكلفة %", "تشخيص السبب", "المهارات"]
    ws6.append(hdr6)
    style_hdr(ws6, len(hdr6))
    for r in diag["op_all"][:30]:
        ws6.append(["🔥 مكسور", r["cid"], r["elem_ar"], r["tribe_ar"], r["cost"], r["stats"], round(r["wr"], 2), f"[{r['ci_lo']:.1f},{r['ci_hi']:.1f}]", round(r["z"], 2), round(r["cost_delta"], 2), r["diagnosis"], r["skills"]])
    for r in diag["weak_all"][:30]:
        ws6.append(["❄️ ضعيف", r["cid"], r["elem_ar"], r["tribe_ar"], r["cost"], r["stats"], round(r["wr"], 2), f"[{r['ci_lo']:.1f},{r['ci_hi']:.1f}]", round(r["z"], 2), round(r["cost_delta"], 2), r["diagnosis"], r["skills"]])

    wb.save(path)


def export_html_report(path: str, diag: dict, meta: dict) -> None:
    """Generates a self-contained sleek RTL Arabic interactive HTML visual dashboard."""
    hist = diag["turn_hist"]
    max_h = max(hist.values()) if hist else 1
    bars_html = []
    for t in range(1, min(61, (max(hist.keys()) if hist else 30) + 1)):
        cnt = hist.get(t, 0)
        h_pct = cnt / max(1, max_h) * 100.0
        col = "#38BDF8" if t < 15 else ("#22C55E" if t <= 26 else ("#F59E0B" if t <= 38 else "#EF4444"))
        bars_html.append(
            f'<div class="hbar" style="height:{max(2, h_pct):.1f}%;background:{col}" '
            f'title="دور {t} (جولة {(t+1)//2}): {cnt:,} معركة ({cnt/diag["n"]*100:.2f}%)"></div>'
        )

    curve_rows = []
    for tc in diag["turn_curve"][:28]:
        curve_rows.append(
            f"<tr><td>{tc['turn']} (ج{tc['round']})</td><td>{tc['share_pct']:.1f}%</td>"
            f"<td>{tc['p1_hp']:.0f} / {tc['p2_hp']:.0f}</td><td>{tc['mana_eff']:.0f}%</td>"
            f"<td>{tc['board']:.1f}</td><td>{tc['dmg']:.1f}</td>"
            f"<td style='color:#4ADE80'>{tc['excite_pct']:.1f}%</td>"
            f"<td style='color:#F87171'>{tc['dead_pct']:.1f}%</td>"
            f"<td style='color:#FBBF24'>{tc['stall_pct']:.1f}%</td></tr>"
        )

    def _tbl_cards(lst: List[dict]) -> str:
        out = []
        for r in lst:
            cls = "op" if r["wr"] >= 55 else ("weak" if r["wr"] < 45 else "ok")
            out.append(
                f"<tr class='{cls}'><td>{r['cid']}</td><td>{html_lib.escape(r['name'])}</td><td>{r['type_ar']}</td>"
                f"<td>{r['elem_ar']}</td><td>{r['tribe_ar']}</td><td>{r['cost']}</td><td>{r['stats']}</td>"
                f"<td><b>{r['wr']:.2f}%</b></td><td>{r['z']:+.2f}</td><td>{r['sac_rate']:.1f}%</td>"
                f"<td>{r['avg_dmg']:.1f}</td><td>{html_lib.escape(r['diagnosis'])}</td></tr>"
            )
        return "\n".join(out)

    ldr_sorted = sorted(diag["ldr_rows"], key=lambda x: x["wr"], reverse=True)
    env_sorted = sorted(diag["env_rows"], key=lambda x: x["wr"], reverse=True)
    recs_html = "".join(f"<li>{html_lib.escape(r)}</li>" for r in diag["recs"])

    html_doc = f"""<!doctype html>
<html lang="ar" dir="rtl"><head><meta charset="utf-8">
<title>تقرير التحليل العبقري لمحاكي المعارك ({diag['n']:,} معركة)</title>
<style>
body{{font-family:'Segoe UI',Tahoma,sans-serif;background:#0B1120;color:#E2E8F0;margin:0;padding:24px;line-height:1.55}}
.wrap{{max-width:1380px;margin:auto}}
h1,h2{{color:#38BDF8;margin:0 0 12px}}
.meta{{color:#94A3B8;margin-bottom:20px;font-size:14px}}
.grid{{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:14px;margin-bottom:22px}}
.card{{background:#1E293B;border:1px solid #334155;border-radius:12px;padding:16px}}
.card h3{{margin:0 0 6px;font-size:13px;color:#94A3B8}}
.big{{font-size:26px;font-weight:700;color:#F8FAFC}}
.sub{{font-size:12px;color:#CBD5E1;margin-top:4px}}
.panel{{background:#131C31;border:1px solid #293548;border-radius:12px;padding:18px;margin-bottom:22px}}
.hist{{display:flex;align-items:flex-end;gap:2px;height:150px;background:#0F172A;padding:10px;border-radius:8px;overflow:hidden}}
.hbar{{flex:1;min-width:4px;border-radius:3px 3px 0 0;opacity:.9}}.hbar:hover{{opacity:1;outline:1px solid #FFF}}
table{{width:100%;border-collapse:collapse;font-size:13px}}
th,td{{padding:7px 9px;border-bottom:1px solid #1E293B;text-align:right}}
th{{background:#0F172A;color:#38BDF8;position:sticky;top:0}}
tr.op{{background:#3B1518;color:#FCA5A5}}tr.weak{{background:#16243E;color:#93C5FD}}
input,select{{background:#0F172A;color:#F8FAFC;border:1px solid #334155;padding:7px 10px;border-radius:8px;margin:0 6px 12px 0}}
ul.recs li{{margin-bottom:8px;background:#1E293B;padding:10px 14px;border-radius:8px;border-right:4px solid #38BDF8;list-style:none}}
</style></head><body><div class="wrap">
<h1>⚔️ لوحة التحليل العبقري لمحاكي معارك الكروت ({diag['n']:,} معركة)</h1>
<div class="meta">الوضع: {html_lib.escape(START_MODES.get(meta['start_mode'], meta['start_mode']))} • الذكاء: {html_lib.escape(AI_POLICIES.get(meta['ai_policy'], meta['ai_policy']))} • القواعد الكاملة: 470 بطاقة / حد اليد 7 / بيئات L1-L3 / 20 قائد عشيرة / مهارتان نشطتان</div>
<div class="grid">
  <div class="card"><h3>صحة التوازن الكلي</h3><div class="big">{diag['balance_score']:.1f}/100</div><div class="sub">متوازن {diag['bal_ratio']:.1f}% | قوي جداً {len(diag['op_all'])} | ضعيف {len(diag['weak_all'])}</div></div>
  <div class="card"><h3>توازن اللاعب أ ضد ب</h3><div class="big">{diag['p1_pct']:.2f}% / {diag['p2_pct']:.2f}%</div><div class="sub">تعادل {diag['draw_rate']:.2f}% | CI95% أ [{diag['p1_ci'][0]:.1f}–{diag['p1_ci'][1]:.1f}%] | Z={diag['p1_z']:.2f}</div></div>
  <div class="card"><h3>عدد الأدوار والجولات</h3><div class="big">{diag['t_avg']:.1f} دور ({diag['r_avg']:.1f} جولة)</div><div class="sub">الوسيط {diag['t_p50']} ({diag['r_med']}ج) • P25-P75: {diag['t_p25']}–{diag['t_p75']} • النطاق المثالي 15–26: {diag['ideal_pct']:.1f}%</div></div>
  <div class="card"><h3>الحماس مقابل الملل</h3><div class="big">{diag['excite_score']:.1f}/100</div><div class="sub">متفجرة {diag['exp_pct']:.1f}% • انقلابات {diag['lc_avg']:.2f}/مباراة • ميت {diag['dead_pct']:.1f}% • جمود {diag['stall_pct']:.1f}%</div></div>
  <div class="card"><h3>أثر كروت البيئة (10)</h3><div class="big">{diag['env_act_pct']:.1f}% تغطية</div><div class="sub">L1:{diag['env_l1']:,} • L2:{diag['env_l2']:,} • L3:{diag['env_l3']:,} | WR مُفعّلها: {diag['env_caster_wr']:.2f}%</div></div>
  <div class="card"><h3>أثر أرواح القادة (20)</h3><div class="big">{diag['sp_vs_none_wr']:.2f}% WR</div><div class="sub">متوسط حلفاء العشيرة {diag['sp_avg_allies']:.2f} | ≥2 حلفاء={diag['sp_ge2_wr']:.1f}% مقابل 0-1={diag['sp_lt2_wr']:.1f}%</div></div>
</div>
<div class="panel"><h2>🧠 التشخيص والتوصيات الاستراتيجية</h2><ul class="recs">{recs_html}</ul></div>
<div class="panel"><h2>⏱️ توزيع أطوال المعارك (الأدوار 1 إلى 60 • الأخضر = النطاق المثالي 15–26 دور = 8–13 جولة)</h2><div class="hist">{''.join(bars_html)}</div></div>
<div class="panel"><h2>👑 أداء أرواح القادة العشرين (451–470)</h2><table><thead><tr><th>#</th><th>القائد</th><th>النوع</th><th>العنصر</th><th>العشيرة</th><th>التكلفة</th><th>-</th><th>بدون مرآة %</th><th>Z</th><th>التضحية%</th><th>ضرر</th><th>التشخيص</th></tr></thead><tbody>{_tbl_cards(ldr_sorted)}</tbody></table></div>
<div class="panel"><h2>🌍 أداء بطاقات البيئة العشر (441–450)</h2><table><thead><tr><th>#</th><th>البيئة</th><th>النوع</th><th>العنصر</th><th>-</th><th>التكلفة</th><th>-</th><th>بدون مرآة %</th><th>Z</th><th>التضحية%</th><th>ضرر</th><th>التشخيص</th></tr></thead><tbody>{_tbl_cards(env_sorted)}</tbody></table></div>
<div class="panel"><h2>📈 منحنى الإيقاع دور بدور (أول 28 دوراً)</h2><table><thead><tr><th>الدور (الجولة)</th><th>نسبة الوصول</th><th>صحة أ / ب</th><th>كفاءة المانا</th><th>وحوش اللوحة</th><th>ضرر الدور</th><th>حماس %</th><th>دور ميت %</th><th>جمود %</th></tr></thead><tbody>{''.join(curve_rows)}</tbody></table></div>
<div class="panel"><h2>🔥 أقوى 25 بطاقة مكسورة + ❄️ أضعف 25 بطاقة</h2><table><thead><tr><th>#</th><th>البطاقة</th><th>النوع</th><th>العنصر</th><th>الفئة</th><th>التكلفة</th><th>إحصائيات</th><th>بدون مرآة %</th><th>Z</th><th>التضحية%</th><th>ضرر</th><th>السبب الجذري</th></tr></thead><tbody>{_tbl_cards(diag['op_all'][:25] + diag['weak_all'][:25])}</tbody></table></div>
</div></body></html>"""
    with open(path, "w", encoding="utf-8") as f:
        f.write(html_doc)


# ==============================================================================
# Tkinter 4-Tab Genius GUI Application
# ==============================================================================
class SimulatorApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("⚔️ محاكي واستوديو التحليل العبقري — قواعد Unity الكاملة (470 بطاقة)")
        self.geometry("1480x900")
        self.configure(bg="#0F172A")

        self.json_path = DEFAULT_JSON_PATH
        self.by_id, self.monsters, self.spells, self.envs, self.leaders = load_database(self.json_path)

        self.reset_stats()
        self.is_running = False
        self.stop_requested = False
        self.sim_start_time = 0.0
        self.target_matches = 10000
        self.last_diag: Optional[dict] = None
        self.ui_queue: queue.Queue = queue.Queue()

        self._setup_styles()
        self._build_ui()
        self.refresh_all_views()
        self.after(140, self._poll_queue)

    def reset_stats(self) -> None:
        self.stats = new_agg_stats()
        self.last_diag = None

    def _setup_styles(self) -> None:
        style = ttk.Style(self)
        style.theme_use("clam")
        style.configure("Treeview", background="#1E293B", foreground="#E2E8F0", fieldbackground="#1E293B", rowheight=24, font=("Segoe UI", 9))
        style.configure("Treeview.Heading", background="#0B1220", foreground="#38BDF8", font=("Segoe UI", 9, "bold"), relief="flat")
        style.map("Treeview", background=[("selected", "#2563EB")])
        style.configure("TProgressbar", troughcolor="#1E293B", background="#22C55E", thickness=16)
        style.configure("TNotebook", background="#0F172A", borderwidth=0)
        style.configure("TNotebook.Tab", background="#1E293B", foreground="#CBD5E1", padding=[14, 6], font=("Segoe UI", 10, "bold"))
        style.map("TNotebook.Tab", background=[("selected", "#2563EB")], foreground=[("selected", "#FFFFFF")])

    def _build_ui(self) -> None:
        top = tk.Frame(self, bg="#1E293B", pady=8, padx=12)
        top.pack(fill="x", padx=8, pady=(8, 4))

        tk.Label(
            top, text="⚔️ أداة التحليل العبقري لمحاكي المعارك (470 بطاقة • 20 قائد عشيرة • بيئات L1–L3 • حد اليد 7 • مهارتان نشطتان)",
            bg="#1E293B", fg="#F8FAFC", font=("Segoe UI", 12, "bold")
        ).grid(row=0, column=0, columnspan=12, sticky="w", pady=(0, 6))

        tk.Label(top, text="المعارك:", bg="#1E293B", fg="#CBD5E1").grid(row=1, column=0, sticky="w")
        self.battles_var = tk.StringVar(value="20000")
        tk.Entry(top, textvariable=self.battles_var, width=10, bg="#0F172A", fg="#38BDF8", font=("Segoe UI", 10, "bold")).grid(row=1, column=1, padx=4)
        for ci, (lbl, v) in enumerate([("5K", "5000"), ("20K", "20000"), ("50K", "50000"), ("100K", "100000"), ("500K", "500000")], start=2):
            tk.Button(top, text=lbl, bg="#334155", fg="#F1F5F9", relief="flat", padx=7, command=lambda x=v: self.battles_var.set(x)).grid(row=1, column=ci, padx=2)

        tk.Label(top, text="CPU:", bg="#1E293B", fg="#CBD5E1").grid(row=1, column=7, padx=(10, 2))
        max_cpu = max(1, os.cpu_count() or 4)
        self.workers_var = tk.IntVar(value=max(1, max_cpu - 1))
        tk.Spinbox(top, from_=1, to=max_cpu, textvariable=self.workers_var, width=4, bg="#0F172A", fg="#F8FAFC").grid(row=1, column=8)

        tk.Label(top, text="وضع البداية:", bg="#1E293B", fg="#CBD5E1").grid(row=1, column=9, padx=(10, 2))
        self.start_mode_cb = ttk.Combobox(top, values=list(START_MODES.keys()), width=17, state="readonly")
        self.start_mode_cb.set("fair_standard")
        self.start_mode_cb.grid(row=1, column=10, padx=3)

        tk.Label(top, text="الذكاء:", bg="#1E293B", fg="#CBD5E1").grid(row=1, column=11, padx=(8, 2))
        self.ai_policy_cb = ttk.Combobox(top, values=list(AI_POLICIES.keys()), width=15, state="readonly")
        self.ai_policy_cb.set("smart_tactical")
        self.ai_policy_cb.grid(row=1, column=12, padx=3)

        btn_f = tk.Frame(top, bg="#1E293B")
        btn_f.grid(row=2, column=0, columnspan=13, sticky="w", pady=(8, 0))
        self.start_btn = tk.Button(btn_f, text="▶ بدء المحاكاة والتحليل", bg="#16A34A", fg="#FFF", font=("Segoe UI", 9, "bold"), relief="flat", padx=14, pady=4, command=self.start_sim)
        self.start_btn.pack(side="left", padx=(0, 6))
        self.stop_btn = tk.Button(btn_f, text="⏹ إيقاف", bg="#DC2626", fg="#FFF", font=("Segoe UI", 9, "bold"), relief="flat", padx=12, pady=4, state="disabled", command=self.stop_sim)
        self.stop_btn.pack(side="left", padx=(0, 6))
        tk.Button(btn_f, text="🔍 معركة تجريبية (سجل)", bg="#0284C7", fg="#FFF", font=("Segoe UI", 9, "bold"), relief="flat", padx=10, pady=4, command=self.run_sample_match).pack(side="left", padx=(0, 6))
        tk.Button(btn_f, text="📊 تصدير Excel الشامل", bg="#7C3AED", fg="#FFF", font=("Segoe UI", 9, "bold"), relief="flat", padx=10, pady=4, command=self.export_excel).pack(side="left", padx=(0, 6))
        tk.Button(btn_f, text="🌐 لوحة HTML مرئية", bg="#0D9488", fg="#FFF", font=("Segoe UI", 9, "bold"), relief="flat", padx=10, pady=4, command=self.export_html).pack(side="left", padx=(0, 6))
        tk.Button(btn_f, text="📄 CSV", bg="#475569", fg="#FFF", font=("Segoe UI", 9, "bold"), relief="flat", padx=10, pady=4, command=self.export_csv).pack(side="left", padx=(0, 10))
        self.endless_var = tk.BooleanVar(value=True)
        tk.Checkbutton(btn_f, text="موجات لا نهائية (>50 + عودة المخضرمين)", variable=self.endless_var, bg="#1E293B", fg="#A7F3D0", selectcolor="#0F172A", activebackground="#1E293B").pack(side="left")

        prog = tk.Frame(self, bg="#1E293B", pady=6, padx=12)
        prog.pack(fill="x", padx=8, pady=(0, 4))
        self.progress = ttk.Progressbar(prog, mode="determinate", maximum=100)
        self.progress.pack(fill="x", pady=(0, 4))
        self.status_lbl = tk.Label(prog, text=f"جاهز • {len(self.by_id)} بطاقة ({len(self.monsters)} وحش + {len(self.spells)} تعويذة + {len(self.envs)} بيئة + {len(self.leaders)} قائد)", bg="#1E293B", fg="#38BDF8", font=("Segoe UI", 9, "bold"), anchor="w")
        self.status_lbl.pack(fill="x")

        self.nb = ttk.Notebook(self)
        self.nb.pack(fill="both", expand=True, padx=8, pady=(0, 8))

        self.tab_dash = tk.Frame(self.nb, bg="#0F172A")
        self.tab_cards = tk.Frame(self.nb, bg="#0F172A")
        self.tab_meta = tk.Frame(self.nb, bg="#0F172A")
        self.tab_pace = tk.Frame(self.nb, bg="#0F172A")
        self.nb.add(self.tab_dash, text="📊 لوحة القيادة والملخص العبقري")
        self.nb.add(self.tab_cards, text="🃏 المكسورة والضعيفة (470 بطاقة)")
        self.nb.add(self.tab_meta, text="👑 القادة والبيئات والعشائر والتكلفة")
        self.nb.add(self.tab_pace, text="⏱️ الجولات والحماس والملل + الأول ضد الثاني")

        self._build_dash_tab()
        self._build_cards_tab()
        self._build_meta_tab()
        self._build_pace_tab()

    def _build_dash_tab(self) -> None:
        kpi_row = tk.Frame(self.tab_dash, bg="#0F172A")
        kpi_row.pack(fill="x", padx=8, pady=6)
        self.kpi_labels: Dict[str, tk.Label] = {}
        cards_spec = [
            ("balance", "صحة التوازن", "#38BDF8"),
            ("p12", "الأول ضد الثاني", "#A78BFA"),
            ("turns", "الجولات والمثالي", "#4ADE80"),
            ("excite", "مؤشر الحماس", "#FBBF24"),
            ("boredom", "الميت والجمود", "#F87171"),
            ("env_sp", "أثر البيئة والقادة", "#2DD4BF"),
        ]
        for idx, (k, title, col) in enumerate(cards_spec):
            f = tk.Frame(kpi_row, bg="#1E293B", padx=10, pady=8, highlightbackground="#334155", highlightthickness=1)
            f.grid(row=0, column=idx, sticky="nsew", padx=4)
            kpi_row.grid_columnconfigure(idx, weight=1)
            tk.Label(f, text=title, bg="#1E293B", fg="#94A3B8", font=("Segoe UI", 9)).pack(anchor="w")
            lbl = tk.Label(f, text="—", bg="#1E293B", fg=col, font=("Segoe UI", 11, "bold"), justify="left")
            lbl.pack(anchor="w", pady=(3, 0))
            self.kpi_labels[k] = lbl

        mid = tk.Frame(self.tab_dash, bg="#0F172A")
        mid.pack(fill="both", expand=True, padx=8, pady=(0, 6))
        mid.grid_rowconfigure(0, weight=1)
        mid.grid_columnconfigure(0, weight=3)
        mid.grid_columnconfigure(1, weight=2)

        self.dash_canvas = tk.Canvas(mid, bg="#0B1220", highlightthickness=1, highlightbackground="#334155")
        self.dash_canvas.grid(row=0, column=0, sticky="nsew", padx=(0, 6))

        rec_f = tk.Frame(mid, bg="#1E293B", padx=8, pady=8)
        rec_f.grid(row=0, column=1, sticky="nsew")
        tk.Label(rec_f, text="🧠 التشخيص التلقائي والتوصيات الاستراتيجية", bg="#1E293B", fg="#FDE68A", font=("Segoe UI", 10, "bold")).pack(anchor="w", pady=(0, 4))
        self.recs_txt = scrolledtext.ScrolledText(rec_f, bg="#0B1220", fg="#E2E8F0", font=("Segoe UI", 10), wrap="word")
        self.recs_txt.pack(fill="both", expand=True)

    def _build_cards_tab(self) -> None:
        flt = tk.Frame(self.tab_cards, bg="#0F172A", pady=4, padx=8)
        flt.pack(fill="x")
        tk.Label(flt, text="النوع:", bg="#0F172A", fg="#CBD5E1").pack(side="left")
        self.type_filter = ttk.Combobox(flt, values=["الكل", "وحش", "تعويذة", "بيئة", "روح قائد"], width=10, state="readonly")
        self.type_filter.set("الكل")
        self.type_filter.pack(side="left", padx=(4, 10))
        self.type_filter.bind("<<ComboboxSelected>>", lambda _e: self._refresh_cards_table())

        tk.Label(flt, text="العنصر:", bg="#0F172A", fg="#CBD5E1").pack(side="left")
        self.elem_filter = ttk.Combobox(flt, values=["الكل"] + [ELEM_AR[e] for e in ELEMENTS], width=10, state="readonly")
        self.elem_filter.set("الكل")
        self.elem_filter.pack(side="left", padx=(4, 10))
        self.elem_filter.bind("<<ComboboxSelected>>", lambda _e: self._refresh_cards_table())

        tk.Label(flt, text="الفئة:", bg="#0F172A", fg="#CBD5E1").pack(side="left")
        self.tribe_filter = ttk.Combobox(flt, values=["الكل"] + [TRIBE_AR[t] for t in TRIBES], width=11, state="readonly")
        self.tribe_filter.set("الكل")
        self.tribe_filter.pack(side="left", padx=(4, 10))
        self.tribe_filter.bind("<<ComboboxSelected>>", lambda _e: self._refresh_cards_table())

        tk.Label(flt, text="التوازن:", bg="#0F172A", fg="#CBD5E1").pack(side="left")
        self.status_filter = ttk.Combobox(flt, values=["الكل", "مكسور (>55%)", "متوازن (45-55%)", "ضعيف (<45%)", "كامنة غير مفعلة"], width=18, state="readonly")
        self.status_filter.set("الكل")
        self.status_filter.pack(side="left", padx=(4, 10))
        self.status_filter.bind("<<ComboboxSelected>>", lambda _e: self._refresh_cards_table())

        tk.Label(flt, text="بحث:", bg="#0F172A", fg="#CBD5E1").pack(side="left")
        self.search_var = tk.StringVar()
        self.search_var.trace_add("write", lambda *_: self._refresh_cards_table())
        tk.Entry(flt, textvariable=self.search_var, width=20, bg="#1E293B", fg="#F8FAFC").pack(side="left", padx=4)

        self.sort_col = "wr"
        self.sort_desc = True
        wrap = tk.Frame(self.tab_cards, bg="#0F172A", padx=8, pady=4)
        wrap.pack(fill="both", expand=True)
        cols = ("cid", "name", "type", "elem", "tribe", "cost", "stats", "plays", "wr", "z", "cdelta", "sac", "dmg", "excite", "status", "diag")
        self.tree = ttk.Treeview(wrap, columns=cols, show="headings")
        hdrs = {
            "cid": ("#", 44), "name": ("البطاقة", 110), "type": ("النوع", 68), "elem": ("العنصر", 64),
            "tribe": ("العشيرة", 74), "cost": ("ت", 32), "stats": ("هـ/د", 58), "plays": ("لعب", 62),
            "wr": ("فوز %", 68), "z": ("Z", 52), "cdelta": ("فارق التكلفة", 74), "sac": ("تضحية%", 60),
            "dmg": ("ضرر", 56), "excite": ("حماس%", 58), "status": ("التصنيف", 115), "diag": ("تشخيص السبب", 380),
        }
        for c, (txt, w) in hdrs.items():
            self.tree.heading(c, text=txt, command=lambda col=c: self._sort_by(col))
            self.tree.column(c, width=w, anchor="w" if c in ("name", "diag") else "center")
        vsb = ttk.Scrollbar(wrap, orient="vertical", command=self.tree.yview)
        hsb = ttk.Scrollbar(wrap, orient="horizontal", command=self.tree.xview)
        self.tree.configure(yscrollcommand=vsb.set, xscrollcommand=hsb.set)
        self.tree.grid(row=0, column=0, sticky="nsew")
        vsb.grid(row=0, column=1, sticky="ns")
        hsb.grid(row=1, column=0, sticky="ew")
        wrap.grid_rowconfigure(0, weight=1)
        wrap.grid_columnconfigure(0, weight=1)
        self.tree.tag_configure("op", background="#451A1A", foreground="#FCA5A5")
        self.tree.tag_configure("strong", background="#3B2F13", foreground="#FDE68A")
        self.tree.tag_configure("balanced", background="#143325", foreground="#86EFAC")
        self.tree.tag_configure("weak", background="#1E293B", foreground="#93C5FD")
        self.tree.tag_configure("pending", background="#1E293B", foreground="#94A3B8")

    def _build_meta_tab(self) -> None:
        pw = tk.PanedWindow(self.tab_meta, orient="vertical", bg="#0F172A", sashwidth=5)
        pw.pack(fill="both", expand=True, padx=8, pady=6)

        f1 = tk.Frame(pw, bg="#0F172A")
        tk.Label(f1, text="👑 أداء أرواح القادة العشرين (451–470)", bg="#0F172A", fg="#FDE68A", font=("Segoe UI", 10, "bold")).pack(anchor="w")
        lcols = ("cid", "name", "tribe", "cost", "plays", "turn", "allies", "wr", "z", "sac", "diag")
        self.ldr_tree = ttk.Treeview(f1, columns=lcols, show="headings", height=9)
        for c, (t, w) in zip(lcols, [("#", 45), ("القائد", 170), ("العشيرة", 82), ("ت", 34), ("نزول", 62), ("متوسط الدور", 74), ("حلفاء/نزول", 76), ("بدون مرآة%", 82), ("Z", 55), ("تضحية%", 65), ("التشخيص", 430)]):
            self.ldr_tree.heading(c, text=t)
            self.ldr_tree.column(c, width=w, anchor="w" if c in ("name", "diag") else "center")
        self.ldr_tree.pack(fill="both", expand=True)
        pw.add(f1)

        f2 = tk.Frame(pw, bg="#0F172A")
        f2.grid_columnconfigure(0, weight=1)
        f2.grid_columnconfigure(1, weight=1)
        f2.grid_rowconfigure(1, weight=1)
        tk.Label(f2, text="🌍 بطاقات البيئة (441–450)", bg="#0F172A", fg="#38BDF8", font=("Segoe UI", 10, "bold")).grid(row=0, column=0, sticky="w")
        tk.Label(f2, text="🧬 العشائر والتكلفة والتموضعات", bg="#0F172A", fg="#4ADE80", font=("Segoe UI", 10, "bold")).grid(row=0, column=1, sticky="w", padx=(10, 0))
        ecols = ("cid", "elem", "plays", "l2", "l3", "wr", "z", "sac")
        self.env_tree = ttk.Treeview(f2, columns=ecols, show="headings", height=9)
        for c, (t, w) in zip(ecols, [("#", 42), ("العنصر", 72), ("تفعيل", 62), ("L2", 46), ("L3", 46), ("فوز%", 68), ("Z", 52), ("تضحية%", 62)]):
            self.env_tree.heading(c, text=t)
            self.env_tree.column(c, width=w, anchor="center")
        self.env_tree.grid(row=1, column=0, sticky="nsew", padx=(0, 6))

        gcols = ("grp", "lbl", "cnt", "plays", "wr", "z", "op", "wk")
        self.grp_tree = ttk.Treeview(f2, columns=gcols, show="headings", height=9)
        for c, (t, w) in zip(gcols, [("محور", 66), ("الفئة/التكلفة", 110), ("كروت", 48), ("لعب", 66), ("فوز%", 68), ("Z", 54), ("مكسور", 52), ("ضعيف", 52)]):
            self.grp_tree.heading(c, text=t)
            self.grp_tree.column(c, width=w, anchor="center")
        self.grp_tree.grid(row=1, column=1, sticky="nsew")
        pw.add(f2)

    def _build_pace_tab(self) -> None:
        self.pace_hdr = tk.Label(self.tab_pace, text="شغّل المحاكاة لعرض منحنى الجولات والحماس والملل...", bg="#1E293B", fg="#FDE68A", font=("Segoe UI", 10, "bold"), pady=8, padx=10, anchor="w", justify="left")
        self.pace_hdr.pack(fill="x", padx=8, pady=6)
        wrap = tk.Frame(self.tab_pace, bg="#0F172A", padx=8, pady=4)
        wrap.pack(fill="both", expand=True)
        pcols = ("turn", "rnd", "reach", "hp", "meff", "brd", "hnd", "dmg", "exc", "dead", "brk", "stl", "burn")
        self.pace_tree = ttk.Treeview(wrap, columns=pcols, show="headings")
        for c, (t, w) in zip(pcols, [("الدور", 52), ("جولة", 52), ("الوصول%", 70), ("صحة أ / ب", 95), ("كفاءة مانا%", 76), ("اللوحة", 64), ("اليد", 56), ("ضرر/دور", 72), ("حماس%", 68), ("ميت%", 68), ("Brick%", 68), ("جمود%", 68), ("حرق يد7", 68)]):
            self.pace_tree.heading(c, text=t)
            self.pace_tree.column(c, width=w, anchor="center")
        vsb = ttk.Scrollbar(wrap, orient="vertical", command=self.pace_tree.yview)
        self.pace_tree.configure(yscrollcommand=vsb.set)
        self.pace_tree.pack(side="left", fill="both", expand=True)
        vsb.pack(side="right", fill="y")

    def _sort_by(self, col: str) -> None:
        if self.sort_col == col:
            self.sort_desc = not self.sort_desc
        else:
            self.sort_col = col
            self.sort_desc = True
        self._refresh_cards_table()

    def _draw_dash_charts(self, diag: dict) -> None:
        cv = self.dash_canvas
        cv.delete("all")
        w = max(500, cv.winfo_width())
        h = max(320, cv.winfo_height())
        if diag["n"] == 0:
            cv.create_text(w // 2, h // 2, text="اضغط ▶ بدء المحاكاة لرسم المنحنيات التحليلية", fill="#64748B", font=("Segoe UI", 12))
            return

        # Top Half: Turn Length Histogram (1..55 turns)
        pad_l, pad_r, pad_t, mid_y = 48, 20, 28, int(h * 0.48)
        cv.create_text(pad_l, 12, text="توزيع طول المباريات بالأدوار (الأخضر = المثالي 15–26 دور = 8–13 جولة كاملة)", fill="#38BDF8", anchor="w", font=("Segoe UI", 9, "bold"))
        hist = diag["turn_hist"]
        max_cnt = max(hist.values()) if hist else 1
        max_t = 55
        bw = max(3, (w - pad_l - pad_r) / max_t)
        for t in range(1, max_t + 1):
            cnt = hist.get(t, 0)
            bh = (cnt / max(1, max_cnt)) * max(20, mid_y - pad_t - 18)
            x0 = pad_l + (t - 1) * bw
            col = "#38BDF8" if t < 15 else ("#22C55E" if t <= 26 else ("#F59E0B" if t <= 38 else "#EF4444"))
            cv.create_rectangle(x0, mid_y - 16 - bh, x0 + max(2, bw - 1), mid_y - 16, fill=col, outline="")
            if t in (1, 8, 15, 21, 26, 35, 45, 55):
                cv.create_text(x0 + bw / 2, mid_y - 6, text=str(t), fill="#94A3B8", font=("Segoe UI", 7))

        # Bottom Half: Excitement vs Dead vs Stall Curve across turns 1..28
        bot_t = mid_y + 22
        bot_b = h - 22
        cv.create_text(pad_l, bot_t - 10, text="منحنى الإثارة (أخضر) ضد الأدوار الميتة (أحمر) والجمود (أصفر) عبر الأدوار 1..28", fill="#FDE68A", anchor="w", font=("Segoe UI", 9, "bold"))
        tc = [x for x in diag["turn_curve"] if x["turn"] <= 28]
        if len(tc) >= 2:
            max_y = max(40.0, max(max(x["excite_pct"], x["dead_pct"], x["stall_pct"]) for x in tc))
            for key, col in (("excite_pct", "#22C55E"), ("dead_pct", "#EF4444"), ("stall_pct", "#FBBF24")):
                pts = []
                for idx, row in enumerate(tc):
                    px = pad_l + idx * ((w - pad_l - pad_r) / max(1, len(tc) - 1))
                    py = bot_b - (row[key] / max_y) * max(20, bot_b - bot_t - 8)
                    pts.extend((px, py))
                cv.create_line(*pts, fill=col, width=2, smooth=True)

    def _refresh_cards_table(self) -> None:
        if not hasattr(self, "tree"):
            return
        diag = self.last_diag
        rows_src = diag["card_rows"] if diag else []
        tf = TYPE_FROM_AR.get(self.type_filter.get(), self.type_filter.get())
        ef = ELEM_FROM_AR.get(self.elem_filter.get(), self.elem_filter.get())
        trf = TRIBE_FROM_AR.get(self.tribe_filter.get(), self.tribe_filter.get())
        sf = self.status_filter.get()
        q = self.search_var.get().strip().lower()

        filtered = []
        for r in rows_src:
            if tf != "الكل" and r["card_type"] != tf:
                continue
            if ef != "الكل" and r["element"] != ef:
                continue
            if trf != "الكل" and r["tribe"] != trf:
                continue
            if sf == "مكسور (>55%)" and not (r["nmm"] >= 15 and r["wr"] > 55.0):
                continue
            if sf == "متوازن (45-55%)" and not (r["nmm"] >= 15 and 45.0 <= r["wr"] <= 55.0):
                continue
            if sf == "ضعيف (<45%)" and not (r["nmm"] >= 15 and r["wr"] < 45.0):
                continue
            if sf == "كامنة غير مفعلة" and not r["has_cosmetic"]:
                continue
            if q:
                hs = f"{r['cid']} {r['name']} {r['type_ar']} {r['elem_ar']} {r['tribe_ar']} {r['skills']} {r['diagnosis']}".lower()
                if q not in hs:
                    continue
            filtered.append(r)

        kmap = {
            "cid": "cid", "name": "name", "type": "type_ar", "elem": "elem_ar", "tribe": "tribe_ar",
            "cost": "cost", "plays": "plays", "wr": "wr", "z": "z", "cdelta": "cost_delta",
            "sac": "sac_rate", "dmg": "avg_dmg", "excite": "exc_rate", "status": "wr"
        }
        sk = kmap.get(self.sort_col, "wr")
        filtered.sort(key=lambda r: r[sk], reverse=self.sort_desc)
        self.tree.delete(*self.tree.get_children())
        for r in filtered:
            self.tree.insert("", "end", values=(
                r["cid"], r["name"], r["type_ar"], r["elem_ar"], r["tribe_ar"], r["cost"], r["stats"],
                f"{r['plays']:,}", f"{r['wr']:.2f}%", f"{r['z']:+.2f}", f"{r['cost_delta']:+.1f}%",
                f"{r['sac_rate']:.1f}%", f"{r['avg_dmg']:.1f}", f"{r['exc_rate']:.1f}%", r["status_ar"], r["diagnosis"]
            ), tags=(r["tag"],))

    def refresh_all_views(self) -> None:
        diag = analyze_simulation(self.stats, self.by_id, self.monsters, self.spells, self.envs, self.leaders)
        self.last_diag = diag
        if diag["n"] > 0:
            self.kpi_labels["balance"].config(text=f"{diag['balance_score']:.1f}/100 ({diag['bal_ratio']:.1f}% متوازن)\nOP:{len(diag['op_all'])} | Weak:{len(diag['weak_all'])}")
            self.kpi_labels["p12"].config(text=f"{diag['p1_pct']:.2f}% / {diag['p2_pct']:.2f}%\nتعادل {diag['draw_rate']:.2f}% | Z={diag['p1_z']:+.2f}")
            self.kpi_labels["turns"].config(text=f"{diag['t_avg']:.1f} دور ({diag['r_avg']:.1f} جولات)\nوسيط {diag['t_p50']} | مثالي 15-26: {diag['ideal_pct']:.1f}%")
            self.kpi_labels["excite"].config(text=f"{diag['excite_score']:.1f}/100 • متفجر {diag['exp_pct']:.1f}%\nانقلابات {diag['lc_avg']:.2f} | تأخر {diag['cb_pct']:.1f}%")
            self.kpi_labels["boredom"].config(text=f"{diag['boredom_pct']:.1f}% إجمالي\nميت {diag['dead_pct']:.1f}% | جمود {diag['stall_pct']:.1f}%")
            self.kpi_labels["env_sp"].config(text=f"بيئة {diag['env_act_pct']:.1f}% (WR {diag['env_caster_wr']:.1f}%)\nقائد WR {diag['sp_vs_none_wr']:.1f}% (≥2: {diag['sp_ge2_wr']:.1f}%)")
            self.recs_txt.config(state="normal")
            self.recs_txt.delete("1.0", "end")
            self.recs_txt.insert("1.0", "\n\n".join(diag["recs"]))
            self.recs_txt.config(state="disabled")

        self._draw_dash_charts(diag)
        self._refresh_cards_table()

        self.ldr_tree.delete(*self.ldr_tree.get_children())
        for r in sorted(diag["ldr_rows"], key=lambda x: x["wr"], reverse=True):
            self.ldr_tree.insert("", "end", values=(
                r["cid"], r["name"], r["tribe_ar"], r["cost"], f"{r['plays']:,}", f"{r['sp_turn']:.1f}",
                f"{r['sp_allies']:.2f}", f"{r['wr']:.2f}%", f"{r['z']:+.2f}", f"{r['sac_rate']:.1f}%", r["diagnosis"]
            ))

        self.env_tree.delete(*self.env_tree.get_children())
        for r in sorted(diag["env_rows"], key=lambda x: x["wr"], reverse=True):
            self.env_tree.insert("", "end", values=(
                r["cid"], r["elem_ar"], f"{r['plays']:,}", diag["env_el_l2"].get(r["element"], 0),
                diag["env_el_l3"].get(r["element"], 0), f"{r['wr']:.2f}%", f"{r['z']:+.2f}", f"{r['sac_rate']:.1f}%"
            ))

        self.grp_tree.delete(*self.grp_tree.get_children())
        for ax, lst in (("عشيرة", diag["tribes_tbl"]), ("عنصر", diag["elems_tbl"]), ("تكلفة", diag["cost_tbl"]), ("دور", diag["roles_tbl"])):
            for x in lst:
                self.grp_tree.insert("", "end", values=(ax, x["label"], x["count"], f"{x['plays']:,}", f"{x['wr']:.2f}%", f"{x['z']:+.2f}", x["op_cnt"], x["weak_cnt"]))

        self.pace_hdr.config(
            text=f"متوسط الأدوار {diag['t_avg']:.2f} ({diag['r_avg']:.2f} جولة) | الوسيط P50={diag['t_p50']} ({diag['r_med']}ج) | P25={diag['t_p25']} – P75={diag['t_p75']} – P90={diag['t_p90']} "
                 f"| الأول: صحة متبقية {diag['p1_avg_hp']:.1f} (ضرر T1-2: {diag['p1_t12_dmg']:.1f}) ضد الثاني: {diag['p2_avg_hp']:.1f} (ضرر T1-2: {diag['p2_t12_dmg']:.1f}، استفادة +1 مانا T2: {diag['t2_bonus_used_pct']:.1f}%)"
        )
        self.pace_tree.delete(*self.pace_tree.get_children())
        for tc in diag["turn_curve"]:
            self.pace_tree.insert("", "end", values=(
                tc["turn"], tc["round"], f"{tc['share_pct']:.1f}%", f"{tc['p1_hp']:.0f} / {tc['p2_hp']:.0f}",
                f"{tc['mana_eff']:.1f}%", f"{tc['board']:.2f}", f"{tc['hand']:.2f}", f"{tc['dmg']:.1f}",
                f"{tc['excite_pct']:.1f}%", f"{tc['dead_pct']:.1f}%", f"{tc['brick_pct']:.1f}%", f"{tc['stall_pct']:.1f}%", f"{tc['burn_rate']:.3f}"
            ))

    def start_sim(self) -> None:
        if self.is_running:
            return
        try:
            n = int(self.battles_var.get().replace(",", "").strip())
            if not (1 <= n <= 1_000_000):
                raise ValueError()
        except ValueError:
            messagebox.showerror("خطأ", "أدخل عدداً بين 1 و 1,000,000")
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
        sm = self.start_mode_cb.get()
        ap = self.ai_policy_cb.get()
        seed_base = int(time.time()) & 0xFFFFFF
        threading.Thread(target=self._run_pool_thread, args=(n, workers, endless, seed_base, sm, ap), daemon=True).start()

    def stop_sim(self) -> None:
        self.stop_requested = True
        self.stop_btn.config(state="disabled")

    def _run_pool_thread(self, total: int, workers: int, endless: bool, seed_base: int, sm: str, ap: str) -> None:
        chunk_sz = max(50, min(1200, total // max(1, workers * 10)))
        tasks, rem, cid = [], total, 0
        while rem > 0:
            cur = min(chunk_sz, rem)
            tasks.append((cid, cur))
            rem -= cur
            cid += 1
        try:
            with mp.Pool(processes=workers, initializer=_init_worker, initargs=(self.json_path, endless, 50, seed_base, sm, ap)) as pool:
                for res in pool.imap_unordered(_run_chunk, tasks):
                    self.ui_queue.put(("chunk", res))
                    if self.stop_requested:
                        pool.terminate()
                        break
        except Exception as exc:
            self.ui_queue.put(("error", str(exc)))
        finally:
            self.ui_queue.put(("done", None))

    def _poll_queue(self) -> None:
        dirty = False
        try:
            while True:
                kind, payload = self.ui_queue.get_nowait()
                if kind == "chunk":
                    merge_agg_stats(self.stats, payload)
                    dirty = True
                elif kind == "error":
                    messagebox.showerror("خطأ", str(payload))
                elif kind == "done":
                    self.is_running = False
                    self.start_btn.config(state="normal")
                    self.stop_btn.config(state="disabled")
                    dirty = True
        except queue.Empty:
            pass
        if dirty:
            done = self.stats["n"]
            pct = (done / max(1, self.target_matches)) * 100.0
            self.progress["value"] = min(100.0, pct)
            el = max(0.01, time.perf_counter() - self.sim_start_time) if self.sim_start_time else 0.01
            rate = done / el
            self.status_lbl.config(text=f"{'مكتمل' if not self.is_running else 'جاري'}: {done:,} / {self.target_matches:,} ({pct:.1f}%) • السرعة: {rate:,.0f} معركة/ث")
            self.refresh_all_views()
        self.after(150, self._poll_queue)

    def run_sample_match(self) -> None:
        eng = BattleEngine(
            self.by_id, self.monsters, self.spells, self.envs, self.leaders,
            endless_waves=bool(self.endless_var.get()), start_mode=self.start_mode_cb.get(), ai_policy=self.ai_policy_cb.get()
        )
        _, _, _, _, log, _ = eng.play_match(random.Random(), verbose=True)
        win = tk.Toplevel(self)
        win.title("🔍 سجل معركة تجريبية بالقواعد الكاملة")
        win.geometry("880x600")
        txt = scrolledtext.ScrolledText(win, bg="#0B1220", fg="#E2E8F0", font=("Consolas", 10))
        txt.pack(fill="both", expand=True)
        txt.insert("1.0", "\n".join(log))
        txt.config(state="disabled")

    def export_excel(self) -> None:
        if not HAS_OPENPYXL or not self.last_diag:
            return
        os.makedirs(DEFAULT_EXPORT_DIR, exist_ok=True)
        path = filedialog.asksaveasfilename(initialdir=DEFAULT_EXPORT_DIR, initialfile=f"genius_balance_{self.stats['n']}.xlsx", defaultextension=".xlsx")
        if path:
            export_excel_report(path, self.last_diag, {"start_mode": self.start_mode_cb.get(), "ai_policy": self.ai_policy_cb.get()})
            messagebox.showinfo("تم", f"تم حفظ التقرير الشامل في:\n{path}")

    def export_html(self) -> None:
        if not self.last_diag:
            return
        os.makedirs(DEFAULT_EXPORT_DIR, exist_ok=True)
        path = filedialog.asksaveasfilename(initialdir=DEFAULT_EXPORT_DIR, initialfile=f"genius_dashboard_{self.stats['n']}.html", defaultextension=".html")
        if path:
            export_html_report(path, self.last_diag, {"start_mode": self.start_mode_cb.get(), "ai_policy": self.ai_policy_cb.get()})
            messagebox.showinfo("تم", f"تم حفظ اللوحة المرئية في:\n{path}")

    def export_csv(self) -> None:
        if not self.last_diag:
            return
        os.makedirs(DEFAULT_EXPORT_DIR, exist_ok=True)
        path = filedialog.asksaveasfilename(initialdir=DEFAULT_EXPORT_DIR, initialfile=f"card_diagnostics_{self.stats['n']}.csv", defaultextension=".csv")
        if path:
            with open(path, "w", newline="", encoding="utf-8-sig") as f:
                w = csv.writer(f)
                w.writerow(["CID", "Name", "Type", "Element", "Tribe", "Cost", "Stats", "Plays", "NonMirror", "WR%", "Z", "CostDelta%", "Sac%", "AvgDmg", "Status", "Diagnosis"])
                for r in self.last_diag["card_rows"]:
                    w.writerow([r["cid"], r["name"], r["type_ar"], r["elem_ar"], r["tribe_ar"], r["cost"], r["stats"], r["plays"], r["nmm"], round(r["wr"], 2), round(r["z"], 2), round(r["cost_delta"], 2), round(r["sac_rate"], 2), round(r["avg_dmg"], 2), r["status_ar"], r["diagnosis"]])
            messagebox.showinfo("تم", f"تم حفظ CSV في:\n{path}")


# ==============================================================================
# Headless Batch Runner & CLI Entrypoint
# ==============================================================================
def run_headless(
    matches: int, workers: int, json_path: str = DEFAULT_JSON_PATH,
    endless: bool = True, start_mode: str = "fair_standard", ai_policy: str = "smart_tactical",
    seed: int = 20261114
) -> Tuple[dict, dict]:
    by_id, monsters, spells, envs, leaders = load_database(json_path)
    st = new_agg_stats()
    chunk_sz = max(50, min(1500, matches // max(1, workers * 8)))
    tasks, rem, cid = [], matches, 0
    while rem > 0:
        cur = min(chunk_sz, rem)
        tasks.append((cid, cur))
        rem -= cur
        cid += 1
    with mp.Pool(processes=workers, initializer=_init_worker, initargs=(json_path, endless, 50, seed, start_mode, ai_policy)) as pool:
        for res in pool.imap_unordered(_run_chunk, tasks):
            merge_agg_stats(st, res)
    diag = analyze_simulation(st, by_id, monsters, spells, envs, leaders)
    return st, diag


def main() -> None:
    parser = argparse.ArgumentParser(description="Unity TCG Battle Simulator & Genius Analytics")
    parser.add_argument("--cli", action="store_true", help="Run headless simulation & export reports")
    parser.add_argument("--matches", type=int, default=50000, help="Matches per run")
    parser.add_argument("--workers", type=int, default=max(1, (os.cpu_count() or 4) - 1))
    parser.add_argument("--start-mode", choices=list(START_MODES.keys()), default="fair_standard")
    parser.add_argument("--ai-policy", choices=list(AI_POLICIES.keys()), default="smart_tactical")
    parser.add_argument("--no-endless", action="store_true")
    parser.add_argument("--json-path", default=DEFAULT_JSON_PATH)
    parser.add_argument("--xlsx-out", default="")
    parser.add_argument("--html-out", default="")
    parser.add_argument("--json-out", default="")
    args = parser.parse_args()

    if not args.cli:
        app = SimulatorApp()
        app.mainloop()
        return

    t0 = time.perf_counter()
    _, diag = run_headless(
        args.matches, args.workers, json_path=args.json_path, endless=not args.no_endless,
        start_mode=args.start_mode, ai_policy=args.ai_policy
    )
    dt_sec = time.perf_counter() - t0
    meta = {"start_mode": args.start_mode, "ai_policy": args.ai_policy, "elapsed_sec": round(dt_sec, 2)}

    if args.xlsx_out and HAS_OPENPYXL:
        os.makedirs(os.path.dirname(os.path.abspath(args.xlsx_out)), exist_ok=True)
        export_excel_report(args.xlsx_out, diag, meta)
    if args.html_out:
        os.makedirs(os.path.dirname(os.path.abspath(args.html_out)), exist_ok=True)
        export_html_report(args.html_out, diag, meta)
    if args.json_out:
        os.makedirs(os.path.dirname(os.path.abspath(args.json_out)), exist_ok=True)
        with open(args.json_out, "w", encoding="utf-8") as f:
            json.dump({"meta": meta, "diag": diag}, f, ensure_ascii=False, indent=2)

    print(
        f"DONE {diag['n']:,} matches in {dt_sec:.2f}s ({diag['n']/max(0.01,dt_sec):,.0f}/s) "
        f"| mode={args.start_mode} ai={args.ai_policy} | P1={diag['p1_pct']:.2f}% P2={diag['p2_pct']:.2f}% Draw={diag['draw_rate']:.2f}% "
        f"| AvgTurns={diag['t_avg']:.2f} ({diag['r_avg']:.2f} rnds) | Ideal(15-26)={diag['ideal_pct']:.1f}% "
        f"| Excite={diag['exp_pct']:.1f}% Dead={diag['dead_pct']:.1f}% Stall={diag['stall_pct']:.1f}% "
        f"| EnvAct={diag['env_act_pct']:.1f}% EnvWR={diag['env_caster_wr']:.2f}% | SpiritWR={diag['sp_vs_none_wr']:.2f}%"
    )


if __name__ == "__main__":
    mp.freeze_support()
    main()

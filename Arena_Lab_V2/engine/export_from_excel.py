# -*- coding: utf-8 -*-
"""
Compiles the master Excel workbook ('التصنيف الشامل.xlsx', 2,270 cards, IDs 1..2270)
into a blazing-fast, pre-parsed English JSON database (`data/cards_v2_2270.json`)
for Arena_Lab_V2. Every numeric value is rounded strictly to integers.
"""

from __future__ import annotations

import json
import os
import re
import sys
from collections import Counter
from typing import Any, Dict, List, Optional, Tuple

import openpyxl

DEFAULT_EXCEL_PATH = os.path.expanduser(r"~/Desktop/التصنيف الشامل.xlsx")
OUT_JSON_PATH = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "..", "data", "cards_v2_2270.json")
)

ELEMENT_MAP = {
    "Fire": 0, "Water": 1, "Nature": 2, "Electric": 3, "Ice": 4,
    "Earth": 5, "Dark": 6, "Light": 7, "Air": 8, "Desert": 9,
}
TRIBE_MAP = {
    "Celestials": 0, "Demons": 1, "Ghosts": 2, "Mages": 3, "Warriors": 4,
    "Dwarves": 5, "Giants": 6, "Elves": 7, "Beasts": 8, "Insects": 9,
    "Neutral": -1,
}
PLAYSTYLE_MAP = {
    "Alpha": 0, "Sparta": 1, "Pyro": 2, "Viper": 3, "Bastion": 4,
    "Mirage": 5, "Hive": 6, "Reaper": 7, "Tactician": 8, "Arcane": 9,
}
ROLE_MAP = {
    "Attacker": 0, "Assassin": 1, "Defender": 2, "Barrier": 3,
    "Support": 4, "Disruptor": 5,
}
TYPE_MAP = {
    "بيئة": "environment",
    "تميمة قائد": "leader",
    "عهد وقيد": "covenant",
    "سحر": "spell",
    "وحش": "monster",
}


def _extract_paren_key(val: Any) -> str:
    if not val or val == "—":
        return ""
    m = re.search(r"\(([A-Za-z0-9_\- ]+)\)", str(val))
    return m.group(1).strip() if m else ""


def _extract_english_title(desc: str, fallback: str) -> str:
    head = desc.split(" | ")[0]
    m = re.search(r"\(([A-Za-z][A-Za-z0-9' \-]+)", head)
    if m:
        return m.group(1).strip()
    return fallback


def _first_int(text: str, pattern: str, default: int = 0) -> int:
    m = re.search(pattern, text)
    if not m:
        return default
    for g in m.groups():
        if g is not None:
            return int(g)
    return default


def _all_ints(text: str) -> List[int]:
    return [int(x) for x in re.findall(r"(?<![A-Za-z])\d+(?![A-Za-z])", text)]


def _parse_token_stats(text: str, default_a: int = 5, default_d: int = 5) -> Tuple[int, int]:
    m = re.search(r"(\d+)\s*/\s*(\d+)", text)
    if m:
        return int(m.group(1)), int(m.group(2))
    return default_a, default_d


def _parse_passive_clause(text: str, tier: int) -> Dict[str, Any]:
    """Converts a single passive clause into a clean pre-compiled mechanical op dict."""
    t = text.strip()
    op: Dict[str, Any] = {"kind": "generic_stat", "v1": 0, "v2": 0, "pct": 0, "dur": 1}

    # --- Rare Signature Passives ---
    if "All-Attack" in t:
        bonus_def = _first_int(t, r"\+(\d+)\s*DEF", 0)
        op = {"kind": "all_attack", "v1": bonus_def, "cleanse": "Cleanse" in t, "spell_imm": "Spell Immune" in t}
    elif "زرع الطفيل الجاسوس" in t or "زرع الجاسوس" in t:
        ta, td = _parse_token_stats(t, 0, 14)
        dmg = _first_int(t, r"كل دور\s*(\d+)", 5)
        op = {"kind": "spy_plant", "v1": td, "v2": dmg, "poison": "سم" in t}
    elif "Deck Burn" in t:
        n = _first_int(t, r"أعلى\s*(\d+)", 2)
        op = {"kind": "deck_burn_summon", "v1": n}
    elif "Hand Discard" in t:
        dmg = _first_int(t, r"يلحق\s*(\d+)", 8)
        op = {"kind": "hand_discard_summon", "v1": 1, "v2": dmg}
    elif "مانا دائمة" in t:
        heal = _first_int(t, r"\+(\d+)\s*HP", 0)
        bdef = _first_int(t, r"\+(\d+)\s*DEF", 0)
        op = {"kind": "perm_mana_summon", "v1": 1, "v2": heal, "bdef": bdef, "temp": 1 if "مؤقتة" in t else 0}
    elif "Temp Mana" in t:
        tm = _first_int(t, r"\+(\d+)\s*مانا مؤقتة", 1)
        op = {"kind": "temp_mana_summon", "v1": tm, "v2": _first_int(t, r"\+(\d+)\s*DEF", 0)}
    elif "تكاثر الخلية المجاور" in t:
        ta, td = _parse_token_stats(t, 4, 5)
        op = {"kind": "summon_token", "v1": ta, "v2": td, "count": 1}
    elif "فقس الوصية الأخيرة" in t:
        ta, td = _parse_token_stats(t, 4, 4)
        op = {"kind": "lb_token", "v1": ta, "v2": td, "count": 2}
    elif "Lone Wolf" in t:
        op = {"kind": "lone_wolf", "v1": _first_int(t, r"\+(\d+)\s*ATK", 4), "v2": _first_int(t, r"\+(\d+)\s*DEF", 4)}
    elif "Open Lane" in t:
        op = {"kind": "open_lane", "v1": _first_int(t, r"\+(\d+)\s*ATK", 6)}
    elif "Ascend" in t:
        op = {"kind": "ascend", "v1": _first_int(t, r"\+(\d+)\s*ATK", 7), "v2": _first_int(t, r"\+(\d+)\s*DEF", 9),
              "pct": _first_int(t, r"(\d+)%", 35)}
    elif "Hydra Split" in t:
        op = {"kind": "hydra_split", "pct": 50}
    elif "Chameleon" in t:
        op = {"kind": "chameleon", "v1": _first_int(t, r"\+(\d+)\s*DEF", 5)}
    elif "Doppelganger" in t:
        cap = 3 if "3 مانا" in t else 99
        op = {"kind": "doppelganger", "v1": cap, "v2": _first_int(t, r"\+(\d+)\s*DEF", 0)}
    elif "Cost Discount" in t:
        mode = 0 if "تعويذ" in t else (1 if "ماتا" in t else 2)
        op = {"kind": "cost_discount", "v1": mode}
    elif "Mana Lock" in t:
        op = {"kind": "mana_lock_summon", "v1": _first_int(t, r"يقفل\s*(\d+)", 1)}
    elif "Undying 1 DEF" in t:
        op = {"kind": "undying_1def", "v1": _first_int(t, r"\+(\d+)\s*DEF", 0)}
    elif "Arena Upgrade" in t:
        op = {"kind": "arena_upgrade_once", "v1": _first_int(t, r"\+(\d+)\s*أدوار", 2)}
    elif "Grave Scaling" in t:
        op = {"kind": "grave_scaling", "v1": _first_int(t, r"\+(\d+)\s*ATK", 2), "v2": _first_int(t, r"\+(\d+)\s*DEF", 2)}
    elif "بصيرة الاستدعاء" in t:
        op = {"kind": "summon_draw", "v1": 1, "v2": _first_int(t, r"\+(\d+)\s*DEF", 3)}
    elif "إلهام (Last Breath)" in t:
        op = {"kind": "lb_draw", "v1": 1, "v2": _first_int(t, r"\+(\d+)\s*HP", 5)}
    elif "استنساخ المرايا" in t:
        op = {"kind": "summon_clone_self", "pct": _first_int(t, r"(\d+)%", 60)}
    elif "الوصية الأخيرة — نسخة" in t:
        op = {"kind": "lb_clone_hand", "v1": _first_int(t, r"-(\d+)\s*مانا", 1), "v2": _first_int(t, r"\+(\d+)\s*DEF", 3)}
    # --- Core 25-Effect Passives ---
    elif "Taunt" in t and "Reflect" in t:
        op = {"kind": "taunt_reflect_pct", "pct": _first_int(t, r"(\d+)%", 30), "v1": _first_int(t, r"\+(\d+)\s*DEF", 0)}
    elif "Taunt" in t and "Evasion" in t:
        op = {"kind": "taunt_evasion", "pct": _first_int(t, r"(\d+)%", 18)}
    elif "Taunt" in t:
        op = {"kind": "taunt", "v1": _first_int(t, r"\+(\d+)\s*DEF", 4)}
    elif "Double Strike" in t:
        op = {"kind": "double_strike", "v1": _first_int(t, r"\+(\d+)\s*ATK", 0), "v2": _first_int(t, r"\+(\d+)\s*DEF", 0)}
    elif "Haste" in t:
        op = {"kind": "haste", "v1": _first_int(t, r"\+(\d+)\s*ATK", 0), "v2": _first_int(t, r"Burn\s*(\d+)", 0),
              "tok_atk": _first_int(t, r"\(Tokens\)\s*\+(\d+)", 0)}
    elif "Cleave" in t:
        op = {"kind": "cleave", "pct": _first_int(t, r"(\d+)%", 35), "v1": _first_int(t, r"\+\s*(\d+)\s*حرق", 0)}
    elif "Bypass" in t and "تعويذة" in t:
        op = {"kind": "spell_ping", "v1": _first_int(t, r"تلحق\s*(\d+)", 3)}
    elif "Bypass" in t:
        op = {"kind": "bypass", "pct": _first_int(t, r"(\d+)%", 70), "v1": _first_int(t, r"يلحق\s*(\d+)", 0)}
    elif "Stealth" in t:
        op = {"kind": "stealth", "v1": _first_int(t, r"\+(\d+)\s*ATK", 4) or _first_int(t, r"\+(\d+)\s*ضرر", 4)}
    elif "Evasion" in t:
        op = {"kind": "evasion", "pct": _first_int(t, r"(\d+)%", 18), "v1": _first_int(t, r"\+(\d+)\s*ATK", 0)}
    elif "Enrage" in t:
        op = {"kind": "enrage", "v1": _first_int(t, r"\+(\d+)\s*ATK", 5), "v2": _first_int(t, r"\+(\d+)\s*أشواك", 0)}
    elif "Spell Immune" in t:
        op = {"kind": "spell_immune", "v1": _first_int(t, r"\+(\d+)\s*DEF", 4), "v2": _first_int(t, r"تستعيد\s*\+(\d+)|يستعيد\s*\+(\d+)", 0)}
    elif "Last Breath" in t:
        if "Token" in t:
            ta, td = _parse_token_stats(t, 4, 5)
            cnt = 2 if "تابعيْن" in t or "حشرتين" in t else 1
            op = {"kind": "lb_token", "v1": ta, "v2": td, "count": cnt}
        elif "Frozen" in t:
            op = {"kind": "lb_freeze", "v1": _first_int(t, r"به\s*(\d+)\s*ضرر", 5), "dur": 2 if "2T" in t else 1}
        elif "Resurrected" in t or "مقبرتك" in t:
            op = {"kind": "lb_grave_return", "v1": _first_int(t, r"يلحق\s*(\d+)", 0), "v2": _first_int(t, r"-(\d+)\s*مانا", 1)}
        elif "حرق" in t or "ينفجر" in t:
            op = {"kind": "lb_burn_boom", "v1": _first_int(t, r"بـ\s*(\d+)", 8)}
        elif "Poison" in t:
            op = {"kind": "lb_poison", "v1": _first_int(t, r"ينثر\s*(\d+)", 3), "v2": _first_int(t, r"و(\d+)\s*ضرر", 0)}
        elif "HP" in t:
            op = {"kind": "lb_heal_leader", "v1": _first_int(t, r"\+(\d+)\s*HP", 10), "cleanse": "Cleanse" in t}
        elif "مانا مؤقتة" in t:
            op = {"kind": "lb_mana", "v1": _first_int(t, r"\+(\d+)\s*مانا", 2), "v2": _first_int(t, r"\+(\d+)\s*DEF", 4)}
        elif "ينسخ بطاقة" in t:
            op = {"kind": "lb_copy_enemy", "v1": _first_int(t, r"\+(\d+)\s*HP", 6)}
        else:
            op = {"kind": "lb_dmg_leader", "v1": _first_int(t, r"يلحق\s*(\d+)", 10)}
    elif "Reflect" in t:
        dot_k = "burn" if "Burn" in t else ("poison" if "Poison" in t else "")
        dot_v = _first_int(t, r"(?:Burn|Poison)\s*(\d+)|يتلقى\s*(\d+)|بـ\s*(\d+)\s*سم", 0)
        refl = _first_int(t, r"يعكس\s*(\d+)|يرد\s*(\d+)|و(\d+)\s*ضرر شوكي", 5)
        op = {"kind": "reflect", "v1": refl, "dot_k": dot_k, "dot_v": dot_v}
    elif "Burn" in t:
        op = {"kind": "on_hit_burn", "v1": _first_int(t, r"Burn\s*(\d+)", 2)}
    elif "Poison" in t:
        op = {"kind": "on_hit_poison", "v1": _first_int(t, r"تضع\s*(\d+)", 2)}
    elif "Bleed" in t:
        op = {"kind": "on_hit_bleed", "v1": _first_int(t, r"تضع\s*(\d+)", 2)}
    elif "Life Leech" in t:
        if "كلما مات" in t:
            op = {"kind": "on_death_regen", "v1": _first_int(t, r"\+(\d+)\s*DEF", 5), "v2": _first_int(t, r"\+(\d+)\s*HP", 3)}
        else:
            op = {"kind": "life_leech", "pct": _first_int(t, r"(\d+)%", 25), "v1": _first_int(t, r"\+(\d+)\s*ATK", 0)}
    elif "Frozen" in t:
        op = {"kind": "summon_freeze", "v1": _first_int(t, r"بتكلفة\s*(\d+)", tier + 1), "v2": _first_int(t, r"ويلحق\s*(\d+)", 4)}
    elif "Silenced" in t and "Immune" not in t:
        op = {"kind": "summon_silence", "dur": 2 if "2T" in t else 1, "v1": _first_int(t, r"يلحق\s*(\d+)", 3)}
    elif "Rooted مناعة" in t:
        op = {"kind": "rooted_immune_regen", "v1": _first_int(t, r"\+(\d+)\s*DEF", 0), "v2": _first_int(t, r"يمتص\s*-(\d+)", 0)}
    elif "Rooted" in t:
        op = {"kind": "root_lock", "dur": 2 if "2T" in t else 1, "v1": _first_int(t, r"يلحق\s*(\d+)|تلحق\s*\+(\d+)", 3),
              "silence": "Silenced" in t}
    elif "Recall" in t:
        op = {"kind": "summon_recall_enemy", "v1": _first_int(t, r"بتكلفة\s*(\d+)", tier + 1), "v2": _first_int(t, r"\+(\d+)\s*DEF", 3)}
    elif "Weakened" in t:
        if "يمتص" in t:
            op = {"kind": "summon_steal_atk", "v1": _first_int(t, r"-(\d+)\s*ATK", 3)}
        elif "تزيل تعزيزات" in t:
            op = {"kind": "strip_buffs_hit", "v1": _first_int(t, r"\+(\d+)\s*ضرر", 5)}
        else:
            op = {"kind": "aura_weaken_opp", "v1": _first_int(t, r"-(\d+)\s*ATK", 3)}
    elif "Disrupt" in t:
        op = {"kind": "summon_tax_mana", "v1": _first_int(t, r"-(\d+)\s*ATK", 3), "v2": _first_int(t, r"ويلحق\s*(\d+)", 4)}
    elif "Cleanse" in t:
        op = {"kind": "cleanse_heal_aura", "v1": _first_int(t, r"\+(\d+)\s*DEF", 4), "summon": "عند النزول" in t}
    elif "Heal" in t:
        op = {"kind": "turn_heal_aura", "v1": _first_int(t, r"\+(\d+)\s*DEF", 4), "v2": _first_int(t, r"\+(\d+)\s*HP", 4)}
    elif "Immune" in t:
        op = {"kind": "status_immune", "v1": _first_int(t, r"\+(\d+)\s*DEF", 4), "v2": _first_int(t, r"\+(\d+)\s*ضرر", 0)}
    elif "Token" in t:
        ta, td = _parse_token_stats(t, 4, 5)
        op = {"kind": "summon_token", "v1": ta, "v2": td, "count": 1}
    elif "Skill Conduit" in t or "موصل المهارات" in t:
        op = {"kind": "skill_conduit", "v1": 1, "v2": _first_int(t, r"\+(\d+)", 3)}
    elif "تعويذة سحرية" in t and "+ATK" in t:
        op = {"kind": "on_spell_grow", "v1": _first_int(t, r"\+(\d+)\s*ATK", 2), "v2": _first_int(t, r"\+(\d+)\s*DEF", 2)}
    elif "مهارة نشطة" in t and "+ATK" in t:
        op = {"kind": "on_skill_grow", "v1": _first_int(t, r"\+(\d+)\s*ATK", 2), "v2": _first_int(t, r"\+(\d+)\s*DEF", 2)}
    elif "كلما مات" in t:
        op = {"kind": "on_death_grow", "v1": _first_int(t, r"\+(\d+)\s*ATK", 2), "v2": _first_int(t, r"\+(\d+)\s*DEF", 2),
              "dmg": _first_int(t, r"يلحق\s*(\d+)", 2)}
    elif "الأجنحة" in t:
        op = {"kind": "flank_bonus", "v1": _first_int(t, r"\+(\d+)\s*ATK", 5)}
    elif "عن كل وحش حليف" in t:
        op = {"kind": "pack_aura", "v1": _first_int(t, r"\+(\d+)\s*ATK", 2), "v2": _first_int(t, r"\+(\d+)\s*DEF", 2)}
    elif "المجاورين" in t:
        op = {"kind": "adj_buff_summon", "v1": _first_int(t, r"\+(\d+)\s*ATK", 2), "v2": _first_int(t, r"\+(\d+)\s*DEF", 4)}
    elif "تعاويذك السحرية" in t:
        op = {"kind": "spell_power_aura", "v1": _first_int(t, r"\+(\d+)", 2)}
    else:
        op = {"kind": "bonus_stats_summon", "v1": _first_int(t, r"\+(\d+)\s*ATK", 0), "v2": _first_int(t, r"\+(\d+)\s*DEF", 5)}

    # Check appended Elemental / Tribal signature rider after "—"
    if " — " in t:
        rider = t.split(" — ", 1)[1]
        r_op: Dict[str, Any] = {}
        if "Burn" in rider:
            r_op = {"r": "burn", "v": _first_int(rider, r"(\d+)\s*حرق", 2)}
        elif "Poison" in rider:
            r_op = {"r": "poison", "v": _first_int(rider, r"(\d+)\s*سم", 2)}
        elif "Cleanse" in rider:
            r_op = {"r": "cleanse_def", "v": _first_int(rider, r"\+(\d+)\s*DEF", 3)}
        elif "HP" in rider:
            r_op = {"r": "heal_hp", "v": _first_int(rider, r"\+(\d+)\s*HP", 4)}
        elif "صاعقة" in rider:
            r_op = {"r": "ping_opp", "v": _first_int(rider, r"\+(\d+)\s*ضرر", 3)}
        elif "Weakened" in rider:
            r_op = {"r": "weaken_opp", "v": _first_int(rider, r"-(\d+)\s*ATK", 2)}
        elif "Reflect" in rider:
            r_op = {"r": "def_reflect", "v": _first_int(rider, r"\+(\d+)\s*DEF", 3), "v2": _first_int(rider, r"(\d+)\s*أشواك|يرد\s*\+(\d+)", 2)}
        elif "Life Leech" in rider:
            r_op = {"r": "leech_hp", "v": _first_int(rider, r"\+(\d+)\s*HP", 4)}
        elif "Evasion" in rider:
            r_op = {"r": "evasion", "v": _first_int(rider, r"(\d+)%", 12)}
        elif "Rooted" in rider:
            r_op = {"r": "root_opp", "v": 1}
        elif "DEF" in rider:
            r_op = {"r": "def_bonus", "v": _first_int(rider, r"\+(\d+)\s*DEF", 4)}
        if r_op:
            op["rider"] = r_op
    return op


def _parse_active_clause(text: str, tier: int) -> Dict[str, Any]:
    """Converts a single active skill clause into a structured op dict."""
    t = text.strip()
    cost = _first_int(t, r"\((\d+)\s*مانا", 1)
    op: Dict[str, Any] = {"cost": cost, "kind": "dmg_single", "v1": 10, "v2": 0, "dur": 1}

    # --- Rare Signature Actives ---
    if "نداء عشيرة المحاربين" in t:
        to_board = "مباشرة من ديكك" in t
        disc = _first_int(t, r"-(\d+)\s*مانا", 0)
        cap = _first_int(t, r"بتكلفة\s*(\d+)", tier)
        buff = _first_int(t, r"\+(\d+)/\+", 0)
        op = {"cost": cost, "kind": "warrior_call", "to_board": to_board, "disc": disc, "cap": cap, "buff": buff}
    elif "التهام الوحش" in t:
        op = {"cost": cost, "kind": "devour_3", "pct": _first_int(t, r"(\d+)%", 50), "max_cost": 3}
    elif "جذب المبارزة" in t:
        op = {"cost": cost, "kind": "pull_hook", "v1": _first_int(t, r"إلحاق\s*(\d+)", 8)}
    elif "اللعنة المتنقلة الطائشة" in t:
        op = {"cost": cost, "kind": "shifting_curse", "v1": 5, "enemy_only": False}
    elif "اللعنة المتنقلة الموجهة" in t:
        op = {"cost": cost, "kind": "shifting_curse", "v1": 10, "enemy_only": True}
    elif "Mind Control" in t:
        op = {"cost": cost, "kind": "mind_control_temp", "cap": _first_int(t, r"بتكلفة\s*(\d+)", tier + 1),
              "v1": _first_int(t, r"\+(\d+)\s*ATK", 0)}
    elif "Double Skill" in t:
        op = {"cost": cost, "kind": "double_skill_1t", "v1": _first_int(t, r"\+(\d+)", 3)}
    elif "Double Strike" in t:
        op = {"cost": cost, "kind": "grant_double_strike", "v1": _first_int(t, r"\+(\d+)\s*ATK", 2)}
    elif "Recall" in t and "حليف" in t:
        op = {"cost": cost, "kind": "recall_ally", "v1": _first_int(t, r"\+(\d+)\s*HP", 0),
              "mana": _first_int(t, r"\+(\d+)\s*مانا مؤقتة", 0)}
    elif "Recall" in t:
        op = {"cost": cost, "kind": "recall_enemy", "v1": _first_int(t, r"إلحاق\s*(\d+)", 6),
              "cap": _first_int(t, r"بتكلفة\s*(\d+)", tier + 1)}
    elif "بكل وحوش الخصم" in t or "جميع وحوش الخصم" in t:
        dmg = _first_int(t, r"إلحاق\s*(\d+)|مع\s*(\d+)\s*ضرر", 10)
        st_fx = ("burn" if "Burn" in t else "poison" if "Poison" in t else "bleed" if "Bleed" in t
                 else "freeze" if "Frozen" in t else "silence" if "Silenced" in t
                 else "root" if "Rooted" in t else "weaken" if "Weakened" in t else "")
        st_v = _first_int(t, r"(?:Burn|Poison|Bleed)\s*(\d+)|وضع\s*(\d+)|-(\d+)\s*ATK", 3)
        op = {"cost": cost, "kind": "aoe_enemy", "v1": dmg, "fx": st_fx, "v2": st_v,
              "leech": _first_int(t, r"Life Leech\)\s*(\d+)", 0), "cleanse": "Cleanse" in t}
    elif "متجاورة" in t or "والمجاورين" in t or "ومجاوره" in t:
        dmg = _first_int(t, r"إلحاق\s*(\d+)|و(\d+)\s*ضرر", 9)
        st_fx = ("burn" if "Burn" in t else "poison" if "Poison" in t else "bleed" if "Bleed" in t
                 else "freeze" if "Frozen" in t else "silence" if "Silenced" in t
                 else "root" if "Rooted" in t else "weaken" if "Weakened" in t else "")
        st_v = _first_int(t, r"(?:Burn|Poison|Bleed)\s*(\d+)|وضع\s*(\d+)|-(\d+)\s*ATK", 3)
        op = {"cost": cost, "kind": "cleave_enemy", "v1": dmg, "fx": st_fx, "v2": st_v,
              "heal_hp": _first_int(t, r"\+(\d+)\s*HP", 0), "self_def": _first_int(t, r"نفسك\s*\+(\d+)\s*DEF", 0)}
    elif "كل حلفائك" in t or "جميع حلفائك" in t:
        op = {"cost": cost, "kind": "aoe_ally",
              "atk": _first_int(t, r"\+(\d+)\s*ATK", 0),
              "deff": _first_int(t, r"\+(\d+)\s*DEF", 0),
              "cleanse": "Cleanse" in t, "haste": "Haste" in t,
              "reflect": _first_int(t, r"(\d+)\s*أشواك", 0),
              "ev": _first_int(t, r"(\d+)%", 0) if "Evasion" in t else 0,
              "leech": _first_int(t, r"(\d+)%", 0) if "Life Leech" in t else 0,
              "imm": "Immune" in t, "hp": _first_int(t, r"\+(\d+)\s*HP", 0)}
    elif "وحش حليف" in t or "حلفائك" in t or "حليف مجاور" in t:
        op = {"cost": cost, "kind": "buff_ally",
              "atk": _first_int(t, r"\+(\d+)\s*ATK", 0),
              "deff": _first_int(t, r"\+(\d+)\s*DEF", 0),
              "cleanse": "Cleanse" in t, "haste": "Haste" in t, "taunt": "Taunt" in t,
              "stealth": "Stealth" in t, "imm": "Immune" in t,
              "reflect": _first_int(t, r"(\d+)\s*أشواك", 0),
              "ev": _first_int(t, r"(\d+)%", 0) if "Evasion" in t else 0,
              "leech": _first_int(t, r"(\d+)%", 0) if "Life Leech" in t else 0,
              "self_dmg": _first_int(t, r"إلحاق\s*(\d+)\s*ضرر ذاتي", 0)}
    elif "مقبرتك" in t:
        to_board = "Resurrected" in t
        op = {"cost": cost, "kind": "revive_board" if to_board else "grave_to_hand",
              "cap": _first_int(t, r"بتكلفة\s*(\d+)", tier + 1),
              "v1": _first_int(t, r"\+(\d+)\s*ATK|\+(\d+)\s*HP", 4)}
    elif "استدعاء تابع" in t:
        ta, td = _parse_token_stats(t, 5, 5)
        cnt = 2 if ("تابعيْن" in t and tier >= 4) else 1
        op = {"cost": cost, "kind": "spawn_token", "v1": ta, "v2": td, "count": cnt, "haste": "Haste" in t}
    elif "مانا مؤقتة" in t and "يفقد الخصم" not in t:
        op = {"cost": cost, "kind": "gain_temp_mana", "v1": _first_int(t, r"\+(\d+)\s*مانا", 2), "v2": _first_int(t, r"\+(\d+)", 3)}
    elif "يفقد الخصم" in t:
        op = {"cost": cost, "kind": "drain_enemy_mana", "v1": _first_int(t, r"الخصم\s*(\d+)", 1), "v2": _first_int(t, r"يلحق\s*(\d+)", 8)}
    elif "تفعيل ضرر كل تراكمات" in t:
        op = {"cost": cost, "kind": "detonate_dots", "v1": _first_int(t, r"إلحاق\s*(\d+)", 5)}
    elif "نسخ بطاقة" in t:
        op = {"cost": cost, "kind": "copy_enemy_hand", "v1": _first_int(t, r"\+(\d+)\s*DEF", 2)}
    elif "شفاء (Heal) قائدك" in t:
        op = {"cost": cost, "kind": "heal_leader", "v1": _first_int(t, r"\+(\d+)\s*HP", 15),
              "v2": _first_int(t, r"\+(\d+)\s*DEF", 0), "cleanse": "Cleanse" in t}
    elif "بقائد الخصم" in t and "وحش معادٍ" not in t:
        op = {"cost": cost, "kind": "dmg_leader", "v1": _first_int(t, r"إلحاق\s*(\d+)", 14),
              "heal": _first_int(t, r"\+(\d+)\s*HP", 0), "double_burn": "مضاعفة الحرق" in t}
    elif "تقليل تكلفة" in t:
        op = {"cost": cost, "kind": "discount_hand_spells", "v1": 1, "v2": _first_int(t, r"\+(\d+)\s*HP", 5)}
    elif "خانتي الوسط" in t:
        op = {"cost": cost, "kind": "buff_center", "v1": _first_int(t, r"\+(\d+)\s*DEF", 8), "v2": _first_int(t, r"\+(\d+)\s*HP", 8)}
    else:
        dmg = _first_int(t, r"إلحاق\s*(\d+)", 10)
        fx = ("burn" if "Burn" in t else "poison" if "Poison" in t else "bleed" if "Bleed" in t
              else "freeze" if "Frozen" in t else "silence" if "Silenced" in t
              else "root" if "Rooted" in t else "weaken" if "Weakened" in t else "")
        fx_v = _first_int(t, r"(?:Burn|Poison|Bleed)\s*(\d+)|وضع\s*(\d+)|-(\d+)\s*ATK", 3)
        op = {"cost": cost, "kind": "dmg_single", "v1": dmg, "fx": fx, "v2": fx_v,
              "dur": 2 if "2T" in t else 1,
              "exec_below": _first_int(t, r"أقل من\s*(\d+)", 0),
              "leech": _first_int(t, r"Life Leech\)\s*(\d+)", 0),
              "heal_hp": _first_int(t, r"\+(\d+)\s*HP", 0),
              "self_def": _first_int(t, r"\+(\d+)\s*DEF", 0),
              "spawn_tok": _parse_token_stats(t, 4, 4) if "Token" in t else None}
    return op


def _parse_spell(cid: int, tier: int, style_idx: int, desc: str) -> Dict[str, Any]:
    """Compiles a Spell card (IDs 171..470) into a clean executable spell_op dict."""
    body = desc.split(" | ", 1)[1] if " | " in desc else desc
    op: Dict[str, Any] = {"kind": "dmg_single", "v1": 10, "v2": 0}

    # --- 4 Universal Counter Tech Spells ---
    if "Betrayal —" in desc:
        return {"kind": "counter_betrayal", "self_dmg": 40}
    if "Stand Alone —" in desc:
        return {"kind": "counter_stand_alone", "dur": 3}
    if "Oblivion —" in desc:
        return {"kind": "counter_oblivion"}
    if "Exorcism —" in desc:
        return {"kind": "counter_exorcism"}

    # --- Signature Special Spells ---
    if "Time Rewind" in desc:
        return {"kind": "time_rewind", "hp": 35}
    if "Grand Polymorph" in desc:
        return {"kind": "polymorph", "cap": 99}
    if "Polymorph" in desc:
        return {"kind": "polymorph", "cap": 5}
    if "Twin Stat Flip" in desc:
        return {"kind": "stat_flip", "twin": True, "bonus_def": 4}
    if "Stat Flip" in desc or "Fortress Swap" in desc:
        return {"kind": "stat_flip", "twin": False, "bonus_def": _first_int(body, r"\+(\d+)\s*DEF", 0),
                "hp": _first_int(body, r"\+(\d+)\s*HP", 0)}
    if "Supreme Doppelganger" in desc:
        return {"kind": "doppelganger_spell", "cap": 99, "haste": True}
    if "Minor Doppelganger" in desc:
        return {"kind": "doppelganger_spell", "cap": 4, "haste": False}
    if "Wild Shifting Curse" in desc:
        return {"kind": "shifting_curse", "v1": 5, "enemy_only": False}
    if "Focused Shifting Curse" in desc:
        return {"kind": "shifting_curse", "v1": 10, "enemy_only": True}
    if "Plant Spy" in desc or "Parasite Spy" in desc:
        _, td = _parse_token_stats(body, 0, 15)
        return {"kind": "spy_plant_spell", "v1": td, "v2": _first_int(body, r"كل دور\s*(\d+)", 5),
                "poison": "سم" in body, "fallback_dmg": _first_int(body, r"(\d+)\s*ضرر لوحش", 14)}
    if "Spartan Hook" in desc or "Infernal Pull" in desc:
        return {"kind": "pull_hook_spell", "burn": _first_int(body, r"Burn\s*(\d+)", 0),
                "root": "Rooted" in body, "draw": 1 if "سحب بطاقة" in body else 0}
    if "Supply Blockade" in desc or "Ash Library Burn" in desc:
        return {"kind": "deck_burn_spell", "v1": _first_int(body, r"أعلى\s*(\d+)", 4),
                "burn": _first_int(body, r"Burn\s*(\d+)", 0), "weaken": _first_int(body, r"-(\d+)\s*ATK", 0)}
    if "Hand Strip" in desc:
        return {"kind": "hand_discard_spell", "v1": 1, "dmg": _first_int(body, r"إلحاق\s*(\d+)", 8)}

    # --- General Spells ---
    draw_n = 0
    if "سحب 4" in body: draw_n = 4
    elif "سحب 3" in body or "لسحب 3" in body: draw_n = 3
    elif "بطاقتين" in body and ("سحب" in body or "يسحب" in body or "لسحب" in body): draw_n = 2
    elif "سحب بطاقة" in body or "تسحب بطاقة" in body or "سحب تعويذة" in body or "سحب وحش" in body or "سحب أغلى" in body: draw_n = 1

    if "Mind Control" in body:
        cnt = 2 if "وحشين" in body else 1
        perm = "دائم" in body or "الدائمة" in body
        cap = _first_int(body, r"بتكلفة\s*(\d+)", 99)
        return {"kind": "mind_control", "cnt": cnt, "perm": perm, "cap": cap, "dbl": "Double Strike" in body}
    if "Clone" in body:
        cnt = 2 if ("نسختين" in body or "مرتين" in body or "وحشين" in body) else 1
        pct = _first_int(body, r"(\d+)%", 100)
        cap = _first_int(body, r"بتكلفة\s*(\d+)", 99)
        from_enemy = "معادٍ" in body or "الخصم" in body
        return {"kind": "clone_board", "cnt": cnt, "pct": pct, "cap": cap, "from_enemy": from_enemy,
                "haste": "Haste" in body, "stealth": "Stealth" in body}
    if "Resurrected" in body:
        cnt = 6 if "جميع خاناتك" in body else (3 if "3 وحوش" in body else (2 if "وحشين" in body else 1))
        cap = _first_int(body, r"بتكلفة\s*(\d+)", 99)
        return {"kind": "revive_board", "cnt": cnt, "cap": cap, "haste": "Haste" in body,
                "atk": _first_int(body, r"\+(\d+)\s*ATK", 0), "hp": _first_int(body, r"\+(\d+)\s*HP", 0),
                "from_enemy": "مقبرة الخصم" in body}
    if "Double Skill" in body:
        cnt = 6 if "جميع وحوشك" in body else (2 if "وحشين" in body else 1)
        return {"kind": "double_skill_1t", "cnt": cnt, "mana": _first_int(body, r"\+(\d+)\s*مانا", 0),
                "dbl_atk": "Double Strike" in body, "free": "0 مانا" in body}
    if "Double Strike" in body:
        cnt = 6 if "كل وحوشك" in body or "جميع وحوشك" in body else 1
        return {"kind": "grant_double_strike", "cnt": cnt, "atk": _first_int(body, r"\+(\d+)\s*ATK", 3),
                "mana": _first_int(body, r"\+(\d+)\s*مانا", 0)}
    if "Recall" in body:
        return {"kind": "recall_spell", "enemy": "معادٍ" in body or "للخصم" in body,
                "cnt": 2 if "وحشين" in body else 1, "cap": _first_int(body, r"بتكلفة\s*(\d+)", 99),
                "hp": _first_int(body, r"\+(\d+)\s*HP", 0), "mana": _first_int(body, r"\+(\d+)\s*مانا", 0),
                "spawn": _parse_token_stats(body, 3, 4) if "Token" in body else None}
    if "مانا دائمة" in body:
        return {"kind": "perm_mana", "v1": _first_int(body, r"\+(\d+)\s*مانا دائمة", 1),
                "hp_cost": _first_int(body, r"دفع\s*(\d+)\s*HP", 0), "sac": "التضحية بوحش" in body,
                "hp_heal": _first_int(body, r"\+(\d+)\s*HP", 0), "draw": draw_n}
    if "خفض المانا القصوى" in body:
        return {"kind": "reduce_enemy_max_mana", "v1": _first_int(body, r"-(\d+)", 1), "dmg": _first_int(body, r"إلحاق\s*(\d+)", 8),
                "silence_all": "Silenced" in body}
    if "يفقد الخصم" in body and "مانا" in body:
        return {"kind": "drain_enemy_mana", "v1": _first_int(body, r"الخصم\s*(\d+)", 2),
                "gain": _first_int(body, r"\+(\d+)\s*مانا", 0), "freeze": "Frozen" in body}
    if "مانا مؤقتة" in body and "ضرر" not in body and "تدمير" not in body and "Token" not in body:
        return {"kind": "temp_mana", "v1": _first_int(body, r"\+(\d+)\s*مانا مؤقتة", 2),
                "hp_cost": _first_int(body, r"دفع\s*(\d+)\s*HP", 0), "draw": draw_n,
                "sp": _first_int(body, r"\+(\d+)\s*هذا الدور", 0)}
    if "Token" in body and ("استدعاء" in body or "ملء" in body):
        ta, td = _parse_token_stats(body, 5, 5)
        cnt = 6 if "ملء كل" in body else (4 if ("4 توابع" in body or "الأجنحة" in body) else (3 if "3 توابع" in body else (2 if "تابعيْن" in body else 1)))
        return {"kind": "spawn_tokens", "v1": ta, "v2": td, "cnt": cnt, "haste": "Haste" in body,
                "atk": _first_int(body, r"\+(\d+)\s*ATK", 0), "sac": "التضحية بوحش" in body}
    if "تفعيل" in body and ("السم" in body or "السموم" in body or "الحرق" in body):
        return {"kind": "detonate_dots", "mult": 3 if "3 مرات" in body else (2 if "مرتين" in body or "مضاعفة" in body else 1),
                "draw": draw_n, "leech": "Life Leech" in body, "dmg": _first_int(body, r"إلحاق\s*(\d+)", 0)}
    if "تنصيف الدفاع" in body or "إذابة 75%" in body:
        pct = 75 if "75%" in body else 50
        return {"kind": "melt_enemy_def", "pct": pct, "aoe": "جميع وحوش الخصم" in body,
                "poison": _first_int(body, r"(\d+)\s*سم", 0), "bleed": _first_int(body, r"(\d+)\s*نزيف", 0),
                "dmg": _first_int(body, r"إلحاق\s*(\d+)", 0)}
    if "تدمير وحش معادٍ" in body:
        return {"kind": "execute_enemy", "cap": _first_int(body, r"(\d+)\s*أو أقل", 999),
                "fallback": _first_int(body, r"إلحاق\s*(\d+)", 20), "splash": _first_int(body, r"بباقي وحوش الخصم.*?(\d+)|إلحاق\s*(\d+)\s*ضرر ناري بباقي", 0) or (14 if "بباقي وحوش الخصم" in body else 0),
                "poison_all": 6 if "جميع وحوش الخصم المتبقية" in body else 0, "sac": "تدمير وحش حليف" in body}
    if "بجميع وحوش الخصم" in body or "جميع وحوش الخصم" in body or "كل وحوش الخصم" in body:
        dmg = _first_int(body, r"إلحاق\s*(\d+)", 14)
        fx = ("burn" if "Burn" in body else "poison" if "Poison" in body else "bleed" if "Bleed" in body
              else "freeze" if "Frozen" in body else "silence" if "Silenced" in body
              else "root" if "Rooted" in body else "weaken" if "Weakened" in body else "")
        fx_v = _first_int(body, r"(?:Burn|Poison|Bleed)\s*(\d+)|بـ\s*(\d+)\s*(?:سم|نزيف)|-(\d+)\s*ATK", 4)
        return {"kind": "aoe_enemy", "v1": dmg, "fx": fx, "v2": fx_v, "dur": 2 if "2T" in body else 1,
                "face": _first_int(body, r"و(\d+)\s*ضرر.*?قائد|و(\d+)\s*ضرر لقائد", 0),
                "leech": "Life Leech" in body, "heal": _first_int(body, r"\+(\d+)\s*HP", 0), "draw": draw_n}
    if "متجاورة" in body or "المجاورتين" in body:
        dmg = _first_int(body, r"إلحاق\s*(\d+)", 12)
        fx = ("burn" if "Burn" in body else "poison" if "Poison" in body else "bleed" if "Bleed" in body
              else "silence" if "Silenced" in body else "root" if "Rooted" in body else "")
        fx_v = _first_int(body, r"(?:Burn|Poison|Bleed)\s*(\d+)|و(\d+)\s*(?:سم|نزيف)", 3)
        return {"kind": "cleave_enemy", "v1": dmg, "fx": fx, "v2": fx_v, "draw": draw_n}
    if ("جميع وحوشك" in body or "كل وحوشك" in body or "كل حلفائك" in body) and ("منح" in body or "شفاء" in body or "تطهير" in body):
        return {"kind": "aoe_ally", "atk": _first_int(body, r"\+(\d+)\s*ATK", 0),
                "deff": _first_int(body, r"\+(\d+)\s*DEF", 0), "full_heal": "Full DEF" in body,
                "hp": _first_int(body, r"\+(\d+)\s*HP", 0), "cleanse": "Cleanse" in body,
                "haste": "Haste" in body, "reflect": _first_int(body, r"(\d+)\s*أشواك", 0),
                "reflect_pct": _first_int(body, r"عكس\s*(\d+)%", 0), "imm": "Immune" in body, "draw": draw_n}
    if ("بقائد الخصم" in body or "لقائد الخصم" in body) and "بوحش معادٍ" not in body and "وحش معادٍ" not in body:
        return {"kind": "dmg_leader", "v1": _first_int(body, r"إلحاق\s*(\d+)", 18),
                "self_dmg": _first_int(body, r"قائدك\s*(\d+)\s*ضرر", 0),
                "grave_mult": 5 if "عن كل وحش في مقبرتك" in body else 0, "draw": draw_n}
    if "شفاء" in body and ("قائدك" in body or "الشفاء" in body) and "بوحش معادٍ" not in body:
        return {"kind": "heal_leader", "v1": _first_int(body, r"\+(\d+)\s*HP", 25),
                "max_hp": _first_int(body, r"الصحة القصوى.*?(\d+)", 0),
                "cleanse": "Cleanse" in body, "draw": draw_n}
    if draw_n > 0 and "بوحش معادٍ" not in body and "منح وحش" not in body:
        return {"kind": "pure_draw", "draw": draw_n, "hp": _first_int(body, r"\+(\d+)\s*HP", 0),
                "mana": _first_int(body, r"\+(\d+)\s*مانا", 0), "disc": 1 if "-1 مانا" in body else 0}
    if "منح وحش حليف" in body or "شفاء (Heal) +" in body or "تطهير (Cleanse)" in body or "منح وحشين" in body or "خانتي الوسط" in body:
        return {"kind": "buff_ally", "cnt": 2 if ("وحشين" in body or "المجاور" in body or "الوسط" in body) else 1,
                "atk": _first_int(body, r"\+(\d+)\s*ATK", 0), "deff": _first_int(body, r"\+(\d+)\s*DEF", 0),
                "taunt": "Taunt" in body, "haste": "Haste" in body, "stealth": "Stealth" in body,
                "cleanse": "Cleanse" in body, "imm": "Immune" in body, "bypass": "Bypass" in body,
                "reflect": _first_int(body, r"(\d+)\s*أشواك", 0), "reflect_pct": _first_int(body, r"عكس\s*(\d+)%", 0),
                "ev": _first_int(body, r"(\d+)%", 0) if "Evasion" in body else 0,
                "leech": _first_int(body, r"(\d+)%", 0) if "Life Leech" in body else 0,
                "draw": draw_n}
    if "مقبرتك" in body and "إلى يدك" in body:
        return {"kind": "grave_to_hand", "cnt": 2 if "وحشين" in body else 1, "disc": _first_int(body, r"-(\d+)\s*مانا", 1),
                "hp": _first_int(body, r"\+(\d+)\s*HP", 0), "hp_cost": _first_int(body, r"دفع\s*(\d+)\s*HP", 0)}
    if "نسخ" in body or "سرقة" in body:
        return {"kind": "copy_enemy_hand", "cnt": 2 if "بطاقتين" in body else 1, "disc": _first_int(body, r"-(\d+)\s*مانا", 1), "draw": draw_n}
    if "الوصية الأخيرة" in body:
        return {"kind": "trigger_lb", "cnt": 2 if "لوحشين" in body else 1, "dmg": _first_int(body, r"إلحاق\s*(\d+)", 0)}
    if "تبديل خانة" in body:
        return {"kind": "swap_lanes", "atk": _first_int(body, r"\+(\d+)\s*ATK", 4), "deff": _first_int(body, r"\+(\d+)\s*DEF", 0)}
    if "المهارات النشطة" in body or "المهارة النشطة" in body:
        return {"kind": "boost_skills", "v1": _first_int(body, r"\+(\d+)", 6), "disc": 1 if "-1 مانا" in body else 0,
                "refresh": "إنعاش" in body, "mana": _first_int(body, r"\+(\d+)\s*مانا", 0)}
    if "التعويذة" in body and "مرتين" in body:
        return {"kind": "echo_next_spell", "v1": _first_int(body, r"\+(\d+)", 0), "draw": draw_n}
    if "إطلاق" in body and "صواعق" in body:
        return {"kind": "random_bolts", "cnt": _first_int(body, r"إطلاق\s*(\d+)|إلقاء\s*(\d+)", 4),
                "dmg": _first_int(body, r"(\d+)\s*ضرر", 8)}

    if "نقل الحياة" in desc or "امتصاص (Life Leech)" in body:
        amt = _first_int(body, r"(\d+)\s*ضرر", 24)
        return {"kind": "dmg_single", "v1": amt, "fx": "", "v2": 0, "dur": 1, "leech": amt, "draw": draw_n}
    if "تضحية الشمس" in desc or "يساوي 150%" in body:
        return {"kind": "solar_sacrifice", "pct": _first_int(body, r"(\d+)%", 150)}
    if "من دفاع" in body and ("40%" in body or "60%" in body):
        return {"kind": "def_slam", "pct": _first_int(body, r"(\d+)%", 50)}
    if "رفع دفاع وحش حليف إلى الضعف" in body:
        return {"kind": "buff_ally", "cnt": 1, "atk": 0, "deff": 25, "draw": draw_n}
    if "تبديل نقاط الهجوم" in body:
        return {"kind": "swap_atk_enemy"}
    if "يلحق كل وحش حليف" in body:
        return {"kind": "swarm_bite", "v1": _first_int(body, r"(\d+)\s*ضرر", 4)}
    if "الموجة الحية" in desc or "4+ وحوش" in body:
        return {"kind": "aoe_ally", "atk": 4, "deff": 4, "draw": draw_n}
    if "عقاب الاعتداء" in desc:
        return {"kind": "aoe_ally", "atk": 0, "deff": 4, "reflect": _first_int(body, r"(\d+)", 10), "draw": draw_n}
    if "تثبيت وحش حليف" in body:
        return {"kind": "buff_ally", "cnt": 1, "atk": 0, "deff": _first_int(body, r"\+(\d+)\s*DEF", 8), "cleanse": True, "draw": draw_n}

    # Default single-target damage / status spell
    dmg = _first_int(body, r"إلحاق\s*(\d+)|خصم\s*(\d+)", 10)
    fx = ("burn" if "Burn" in body else "poison" if "Poison" in body else "bleed" if "Bleed" in body
          else "freeze" if "Frozen" in body else "silence" if "Silenced" in body
          else "root" if "Rooted" in body else "weaken" if "Weakened" in body else "")
    fx_v = _first_int(body, r"(?:Burn|Poison|Bleed)\s*(\d+)|وضع\s*(\d+)|و(\d+)\s*(?:سم|نزيف)|-(\d+)\s*ATK", 3)
    return {"kind": "dmg_single", "v1": dmg, "fx": fx, "v2": fx_v, "dur": 2 if "2T" in body else 1,
            "leech": _first_int(body, r"\+(\d+)\s*HP", 0) if "Life Leech" in body else 0,
            "overkill": "فائض" in body, "draw": draw_n,
            "bonus_if_dmg": _first_int(body, r"أو\s*(\d+)\s*ضرر", 0),
            "mana_on_kill": 1 if "تكسب +1 مانا" in body else 0}


def _parse_leader(cid: int, tribe_idx: int, style_idx: int, desc: str) -> Dict[str, Any]:
    """Compiles a Leader Spirit Talisman (IDs 11..70) into Summon + Continuous Tribal Aura."""
    parts = desc.split(" • ")
    s_txt = parts[0] if parts else ""
    a_txt = parts[1] if len(parts) > 1 else ""
    return {
        "tribe": tribe_idx,
        "style": style_idx,
        "summon_heal_hp": _first_int(s_txt, r"\+(\d+)\s*HP", 0),
        "summon_dmg_face": _first_int(s_txt, r"(\d+)\s*ضرر لقائد|سحب\s*(\d+)\s*HP|امتصاص\s*(\d+)\s*HP", 0),
        "summon_dmg_enemy": _first_int(s_txt, r"إلحاق\s*(\d+)\s*ضرر|(\d+)\s*ضرر ناري|(\d+)\s*ضرر زلزالي|(\d+)\s*ضرر روني|قصف.*?(\d+)", 0),
        "summon_buff_atk": _first_int(s_txt, r"\+(\d+)\s*ATK", 0),
        "summon_buff_def": _first_int(s_txt, r"\+(\d+)\s*DEF", 0),
        "summon_draw": 2 if "بطاقتين" in s_txt else (1 if "سحب بطاقة" in s_txt else 0),
        "summon_mana": _first_int(s_txt, r"\+(\d+)\s*مانا", 0),
        "summon_cleanse": "Cleanse" in s_txt,
        "summon_freeze": "Frozen" in s_txt,
        "summon_silence": "Silenced" in s_txt,
        "summon_root": "Rooted" in s_txt,
        "summon_weaken": _first_int(s_txt, r"-(\d+)\s*ATK", 0),
        "summon_burn": _first_int(s_txt, r"Burn\s*(\d+)|(\d+)\s*حرق", 0),
        "summon_poison": _first_int(s_txt, r"Poison\s*(\d+)|(\d+)\s*سم", 0),
        "summon_bleed": _first_int(s_txt, r"Bleed\s*(\d+)|(\d+)\s*نزيف", 0),
        "summon_clone": "Clone" in s_txt or "استنساخ" in s_txt or "نسخ وحش" in s_txt,
        "summon_copy_hand": "سرقة نسخة" in s_txt or "نسخ بطاقة" in s_txt,
        "summon_mind_control": "Mind Control" in s_txt,
        "summon_recall": "Recall" in s_txt,
        "summon_tokens": 3 if "3 دبابير" in s_txt else (2 if "تابعيْن" in s_txt or "ذئبين" in s_txt else (1 if "Token" in s_txt else 0)),
        "summon_revive": "Resurrected" in s_txt,
        "summon_double_skill": "Double Skill" in s_txt,
        "aura_atk": _first_int(a_txt, r"\+(\d+)\s*ATK", 0),
        "aura_def": _first_int(a_txt, r"\+(\d+)\s*DEF", 0),
        "aura_heal_hp": _first_int(a_txt, r"\+(\d+)\s*HP", 0),
        "aura_reflect": _first_int(a_txt, r"يعكس.*?(\d+)|(\d+)\s*أشواك", 0),
        "aura_evasion": _first_int(a_txt, r"(\d+)%\s*مراوغة", 0),
        "aura_leech": _first_int(a_txt, r"(\d+)%\s*امتصاص", 0),
        "aura_burn": _first_int(a_txt, r"Burn\s*(\d+)", 0),
        "aura_poison": _first_int(a_txt, r"Poison\s*(\d+)|\+(\d+)\s*سم", 0),
        "aura_bleed": _first_int(a_txt, r"(\d+)\s*نزيف", 0),
        "aura_skill_disc": 1 if "-1 مانا" in a_txt and "مهار" in a_txt else 0,
        "aura_spell_ping": _first_int(a_txt, r"تلحق\s*(\d+)\s*ضرر|يلحق\s*(\d+)\s*ضرر", 0) if "تعويذة" in a_txt else 0,
        "aura_death_ping": _first_int(a_txt, r"يلحق\s*(\d+)\s*ضرر|يسحب\s*(\d+)\s*HP|بـ\s*(\d+)\s*ضرر", 0) if "مات" in a_txt or "موت" in a_txt else 0,
        "aura_weaken_opp": _first_int(a_txt, r"-(\d+)\s*ATK", 0),
    }


def _parse_covenant(cid: int, style_idx: int, tier: int, desc: str) -> Dict[str, Any]:
    """Compiles a Covenant & Binding Restriction (IDs 71..170) into complete self-restriction + reward flags."""
    parts = desc.split(" • المكافأة: ")
    cost_txt = parts[0] if parts else ""
    rew_txt = parts[1] if len(parts) > 1 else ""
    return {
        "style": style_idx,
        "tier": tier,
        # Self-only restrictions (all 100 Covenants faithfully covered!)
        "self_hp_turn": _first_int(cost_txt, r"(\d+)\s*HP\s*في بداية|يتلقى قائدك\s*(\d+)", 0),
        "self_hp_cap": _first_int(cost_txt, r"عند\s*(\d+)\s*HP|إلى\s*(\d+)\s*HP", 0),
        "self_hp_init_loss": _first_int(cost_txt, r"بـ\s*(\d+)\s*HP أقل|-(\d+)\s*HP", 0),
        "self_hp_on_attack": _first_int(cost_txt, r"(\d+)\s*HP مع كل هجوم", 0),
        "self_atk_pen": _first_int(cost_txt, r"-(\d+)\s*ATK|-(\d+)\s*ضرر(?!\s*قتالي)", 0),
        "self_def_pen": _first_int(cost_txt, r"-(\d+)\s*DEF", 0),
        "self_atk_pct_pen": _first_int(cost_txt, r"(\d+)%\s*ضرراً أقل|-(\d+)%\s*ضرر قتالي", 0),
        "self_def_pct_pen": _first_int(cost_txt, r"-(\d+)%\s*من دفاعها", 0),
        "ally_dmg_eot": _first_int(cost_txt, r"لـ\s*(\d+)\s*ضرر ذاتي|(\d+)\s*ضرر سم ذاتي", 0),
        "ban_self_spells": "أي بطاقات سحر" in cost_txt or "أي تعويذة سحرية" in cost_txt,
        "ban_face_attack": "مهاجمة قائد الخصم" in cost_txt or "شن هجمات مباشرة ضد قائد" in cost_txt,
        "ban_face_spell": "تعاويذ ضرر مباشر على قائد" in cost_txt or "ضرر سحري مباشر بقائد" in cost_txt,
        "ban_normal_attack": "شن هجمات عادية" in cost_txt,
        "ban_self_heal": "يُمنع عليك علاج قائدك" in cost_txt or "لا يمكن علاج قائدك" in cost_txt,
        "ban_skill_gt1": "تكلف أكثر من 1 مانا" in cost_txt,
        "monster_tax_high": 1 if ("5–7" in cost_txt and "+1 مانا" in cost_txt) else 0,
        "monster_tax_all": 1 if ("تكلفة استدعاء الوحوش" in cost_txt and "+1 مانا" in cost_txt) else 0,
        "spell_tax_all": 1 if (("تعاويذك السحرية" in cost_txt or "تعاويذ الهجوم" in cost_txt or "أول تعويذة" in cost_txt) and "+1 مانا" in cost_txt) else 0,
        "discard_hand_eot": ("كل البطاقات المتبقية في يدك" in cost_txt or "تبقى في يدك بنهاية الجولة" in cost_txt),
        "discard_cheapest_2t": "أرخص بطاقة في يدك" in cost_txt,
        "sac_every_2nd": "التضحية بوحش حليف اختياري" in cost_txt,
        "skip_every_3rd": "تتخطى دورك بالكامل كل 3 جولات" in cost_txt,
        "single_attacker_turn": "وحش حليف واحد فقط شن هجوم" in cost_txt,
        "max_plays_turn": 2 if "أكثر من بطاقتين" in cost_txt else 99,
        "max_summons_turn": 1 if "أكثر من وحش واحد في كل جولة" in cost_txt else 99,
        "ban_hand_monster_ge": 4 if ("4 مانا أو أكثر" in cost_txt) else (5 if ("5 مانا أو أكثر" in cost_txt) else (6 if ("6 أو 7 مانا" in cost_txt) else 99)),
        "max_board_self": 1 if "بوحش واحد فقط" in cost_txt else (2 if "بأكثر من وحشين" in cost_txt or "خانتي الوسط" in cost_txt else (3 if "3 وحوش فقط" in cost_txt else 6)),
        "max_hand_self": 3 if "3 بطاقات فقط" in cost_txt else (5 if "5 بطاقات" in cost_txt else 7),
        "max_mana_self": 7 if "عند 7 مانا" in cost_txt else 10,
        "turn_limit_lose": 10 if "الجولة 10" in cost_txt else 0,
        "self_def_cap": 1 if "عند 1 DEF" in cost_txt else (12 if "عند 12 DEF" in cost_txt else (20 if "عند 20 DEF" in cost_txt else 0)),
        "ban_self_dots": "حرق أو سم أو نزيف" in cost_txt,
        "ban_self_extra_draw": "سحب بطاقات إضافية" in cost_txt or "تكلف قائدك 6 HP" in cost_txt,
        "self_hp_extra_spell_dmg": 25 if "30% ضرراً إضافياً" in cost_txt else 0,
        "self_mana_tax_if_lt2": 1 if "أقل من وحشين" in cost_txt else 0,
        "ban_self_revive": "تُنفى ولا يمكن إحياؤها" in cost_txt,
        "delay_leader_turn5": "حتى الجولة الخامسة" in cost_txt,
        "self_minor_restriction": True,  # Ensures every single covenant carries at least its light thematic condition
        # Rewards (comprehensive coverage across all 100 Covenants!)
        "rew_atk": _first_int(rew_txt, r"\+(\d+)\s*ATK", 0) or (2 if "فائض فوق الحد الأقصى" in rew_txt else 0),
        "rew_def": _first_int(rew_txt, r"\+(\d+)\s*DEF", 0) or (3 if "تكتسب وحوش الوسط" in rew_txt else 0),
        "rew_hp_turn": _first_int(rew_txt, r"\+(\d+)\s*HP", 0),
        "rew_burn": _first_int(rew_txt, r"(\d+)\s*(?:ضرر\s*)?حرق|بمقدار\s*\+(\d+)\s*ضرر إضافي", 0) or (4 if "تتضاعف (×2)" in rew_txt else 0),
        "rew_poison": _first_int(rew_txt, r"(\d+)\s*تراكم|(\d+)\s*سم", 0) or (3 if "السم يضرب مرتين" in rew_txt or "ينقل كل وحش معادٍ مسموم" in rew_txt else 0),
        "rew_bleed": _first_int(rew_txt, r"(\d+)\s*نزيف|\+(\d+)\s*ضرر نزيف", 0) or (3 if "تتضاعف تراكمات النزيف" in rew_txt else 0),
        "rew_reflect": _first_int(rew_txt, r"(\d+)\s*(?:ضرر شوكي|أشواك)", 0) or (5 if "ضربات مرتدة" in rew_txt else 0),
        "rew_reflect_pct": _first_int(rew_txt, r"(\d+)%.*?ينعكس|يعكس.*?(\d+)%", 0),
        "rew_leech_pct": _first_int(rew_txt, r"(\d+)%.*?علاج", 0),
        "rew_haste": "سرعة خاطفة" in rew_txt,
        "rew_taunt": "الاستدراج" in rew_txt or "Taunt" in rew_txt,
        "rew_cleanse": "يتطهّر" in rew_txt or "تطهيراً" in rew_txt,
        "rew_extra_draw": 1 if "تسحب بطاقتين" in rew_txt or "تسحب بطاقة إضافية" in rew_txt or "بطاقة إضافية" in rew_txt else 0,
        "rew_token_turn": 2 if "تابعين" in rew_txt else (1 if "Token" in rew_txt else 0),
        "rew_skill_disc": 1 if ("المهارات النشطة" in rew_txt or "مهارة نشطة" in rew_txt or "روح قائدك" in rew_txt) and ("-1 مانا" in rew_txt or "1 مانا" in rew_txt) else 0,
        "rew_spell_disc": _first_int(rew_txt, r"تعاويذك\s*(\d+)\s*مانا|تكلفتها\s*(\d+)\s*مانا|التعاويذ تقل\s*(\d+)", 0) or (1 if "كل بطاقاتك 1 مانا" in rew_txt or "أول بطاقة كل جولة تقل 1 مانا" in rew_txt or "تقل تكلفتها بمقدار 1" in rew_txt else 0),
        "rew_spell_power": _first_int(rew_txt, r"\+(\d+)\s*ضرر إضافي|بمقدار\s*\+(\d+)|يلحق\s*(\d+)\s*ضرر", 0) if ("تعويذ" in rew_txt or "التعاويذ" in rew_txt) else 0,
        "rew_skill_power": _first_int(rew_txt, r"بمقدار\s*\+(\d+)", 0) if "المهارات النشطة" in rew_txt else (4 if "تلك الضربة تسبب" in rew_txt else 0),
        "rew_temp_mana": 1 if "+1 مانا مؤقتة" in rew_txt else 0,
        "rew_dmg_reduce": _first_int(rew_txt, r"أول\s*(\d+)\s*نقاط ضرر", 0) or (4 if "يتلقى قائدك 40%" in rew_txt else 0),
        "rew_disrupt_eot": 1 if ("رمي بطاقة" in rew_txt or "مُسكَتاً" in rew_txt or "أغلى بطاقة في يده" in rew_txt or "تكلفه 5 HP" in rew_txt) else 0,
        "rew_steal_copy": 1 if ("تنسخ" in rew_txt or "نسخة" in rew_txt or "تستولي" in rew_txt or "ديك الخصم" in rew_txt) else 0,
        "rew_double_skill": "Double Skill" in rew_txt or "مرتين متتاليتين" in rew_txt or "تُلقى مرتين" in rew_txt or "قدرة نزولها مرتين" in rew_txt,
        "rew_stat_pct": _first_int(rew_txt, r"\+(\d+)%\s*إحصائيات|\+(\d+)%\s*ATK", 0) or (15 if "قدرات الموت" in rew_txt or "ضربة قاتلة" in rew_txt else 0),
        "rew_enemy_mana_loss": 1 if "يفقد الخصم 1 مانا" in rew_txt or "عند 7 مانا أيضاً" in rew_txt or "يفقد 1 مانا مؤقتة" in rew_txt else 0,
        "rew_enemy_face": _first_int(rew_txt, r"قائده\s*(\d+)|بقائده\s*(\d+)|(\d+)\s*ضرر مباشر|(\d+)\s*ضرر ناري", 0) or (6 if "وحوش الخصم المقتولة تنفجر" in rew_txt else 0),
        "rew_revive_disc": 2 if "بمقدار 2 مانا" in rew_txt or "بنصف تكلفته" in rew_txt else (1 if "المقبرة تقل" in rew_txt else 0),
    }


def export_excel_to_json(excel_path: str = DEFAULT_EXCEL_PATH, out_path: str = OUT_JSON_PATH) -> Dict[str, Any]:
    wb = openpyxl.load_workbook(excel_path, data_only=True)
    ws = wb["التصنيف الشامل"]

    cards: List[Dict[str, Any]] = []
    for r in range(2, ws.max_row + 1):
        cid = int(ws.cell(r, 1).value)
        ctype = TYPE_MAP[str(ws.cell(r, 2).value).strip()]
        elem_key = _extract_paren_key(ws.cell(r, 3).value)
        tribe_key = _extract_paren_key(ws.cell(r, 4).value)
        style_key = _extract_paren_key(ws.cell(r, 5).value)
        role_key = _extract_paren_key(ws.cell(r, 6).value)
        tier_val = str(ws.cell(r, 7).value or "")
        tier = int(tier_val.split()[1]) if tier_val.startswith("Tier ") else 0
        p_cnt = int(ws.cell(r, 9).value or 0)
        a_cnt = int(ws.cell(r, 10).value or 0)
        mana = int(ws.cell(r, 11).value or 0)
        atk_raw = ws.cell(r, 12).value
        def_raw = ws.cell(r, 13).value
        atk = int(atk_raw) if isinstance(atk_raw, (int, float)) else 0
        deff = int(def_raw) if isinstance(def_raw, (int, float)) else 0
        desc = str(ws.cell(r, 14).value or "")

        elem_idx = ELEMENT_MAP.get(elem_key, -1)
        tribe_idx = TRIBE_MAP.get(tribe_key, -1)
        style_idx = PLAYSTYLE_MAP.get(style_key, -1)
        role_idx = ROLE_MAP.get(role_key, -1)

        if ctype == "monster":
            name_en = f"{elem_key} {role_key} #{cid}" if role_idx == 3 else f"{elem_key} {tribe_key} {role_key} #{cid}"
        else:
            name_en = _extract_english_title(desc, f"{ctype.title()} #{cid}")

        entry: Dict[str, Any] = {
            "id": cid,
            "type": ctype,
            "name": name_en,
            "element": elem_idx,
            "element_name": elem_key or "None",
            "tribe": tribe_idx,
            "tribe_name": tribe_key or "None",
            "playstyle": style_idx,
            "playstyle_name": style_key or "Free",
            "role": role_idx,
            "role_name": role_key or "None",
            "tier": tier,
            "mana": mana,
            "atk": atk,
            "def": deff,
            "is_barrier": (ctype == "monster" and role_idx == 3),
            "p_cnt": p_cnt,
            "a_cnt": a_cnt,
        }

        if ctype == "environment":
            entry["env_id"] = elem_idx  # 0..9 exact symmetric arena rule!
        elif ctype == "leader":
            entry["leader_ops"] = _parse_leader(cid, tribe_idx, style_idx, desc)
        elif ctype == "covenant":
            entry["covenant_ops"] = _parse_covenant(cid, style_idx, tier, desc)
        elif ctype == "spell":
            entry["spell_ops"] = _parse_spell(cid, tier, style_idx, desc)
        elif ctype == "monster":
            mech = desc.split(" | ", 1)[1] if " | " in desc else desc
            p_raw = re.findall(r"\[باسف \d+\]: ([^•|]+)", mech)
            a_raw = re.findall(r"\[أبيلتي \d+\]: ([^•|]+)", mech)
            entry["passives"] = [_parse_passive_clause(p, tier) for p in p_raw]
            entry["actives"] = [_parse_active_clause(a, tier) for a in a_raw]
            assert len(entry["passives"]) == p_cnt, f"Passive parse mismatch on ID {cid}"
            assert len(entry["actives"]) == a_cnt, f"Active parse mismatch on ID {cid}"
            if entry["is_barrier"]:
                assert entry["atk"] == 0 and entry["tribe"] == -1 and a_cnt == 0

        cards.append(entry)

    assert len(cards) == 2270, f"Expected 2270 cards, got {len(cards)}"
    payload = {
        "version": "2.0",
        "total_cards": len(cards),
        "rules": {
            "leader_hp": 500,
            "deck_size": 50,
            "max_copies": 2,
            "max_hand": 7,
            "board_slots": 6,
            "buff_cap_atk": 10,
            "buff_cap_def": 15,
            "barrier_atk_locked_zero": True,
        },
        "cards": cards,
    }
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, separators=(",", ":"))
    return payload


if __name__ == "__main__":
    src = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_EXCEL_PATH
    data = export_excel_to_json(src, OUT_JSON_PATH)
    cnts = Counter(c["type"] for c in data["cards"])
    p_kinds = Counter(p["kind"] for c in data["cards"] if c["type"] == "monster" for p in c["passives"])
    a_kinds = Counter(a["kind"] for c in data["cards"] if c["type"] == "monster" for a in c["actives"])
    s_kinds = Counter(c["spell_ops"]["kind"] for c in data["cards"] if c["type"] == "spell")
    print(f"EXPORTED {data['total_cards']} cards -> {OUT_JSON_PATH}")
    print("Types:", dict(cnts))
    print("Unique Monster Passive Kinds:", len(p_kinds), "| Active Kinds:", len(a_kinds), "| Spell Kinds:", len(s_kinds))

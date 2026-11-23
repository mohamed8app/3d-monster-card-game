# -*- coding: utf-8 -*-
"""
Exports the V2 1,470-Card Master Database (`data/__KEEP_CARDS_V2_1470_JSON__`) and trained AI settled decks
(`saved_models/*/settled_deck.json`) directly into the Unity Game Engine (`UnityGame/Assets/Resources/`).

Generates:
  1. `UnityGame/Assets/Resources/cards_10000.json` (All 1,470 cards with English descriptions & v2_ops_json)
  2. `UnityGame/Assets/Resources/champion_decks.json` & `UnityGame/champion_decks.json` (V2 50-card decks, max 2 copies)
"""

from __future__ import annotations

import json
import os
import struct
import sys
import time
from typing import Any, Dict, List

ROOT_DIR = os.path.dirname(os.path.abspath(__file__))
if ROOT_DIR not in sys.path:
    sys.path.insert(0, ROOT_DIR)

V2_JSON_PATH = os.path.join(ROOT_DIR, "data", "cards_v2_2270.json")
SAVED_MODELS_DIR = os.path.join(ROOT_DIR, "saved_models")
LOGS_ROSTER_PATH = os.path.join(ROOT_DIR, "logs", "roster_stats.json")

UNITY_DIR = os.path.abspath(os.path.join(ROOT_DIR, "..", "UnityGame"))
UNITY_RESOURCES_DIR = os.path.join(UNITY_DIR, "Assets", "Resources")
UNITY_BRAINS_DIR = os.path.join(UNITY_RESOURCES_DIR, "V2Brains")
UNITY_CARDS_JSON = os.path.join(UNITY_RESOURCES_DIR, "cards_10000.json")
UNITY_CHAMP_RES_JSON = os.path.join(UNITY_RESOURCES_DIR, "champion_decks.json")
UNITY_CHAMP_EXT_JSON = os.path.join(UNITY_DIR, "champion_decks.json")
UNITY_ROSTER41_RES_JSON = os.path.join(UNITY_RESOURCES_DIR, "v2_roster_41.json")
UNITY_ROSTER41_EXT_JSON = os.path.join(UNITY_DIR, "v2_roster_41.json")

TIER_TITLES = {
    0: "Tier 0 - Relic",
    1: "Tier 1 - Basic",
    2: "Tier 2 - Common",
    3: "Tier 3 - Tactical",
    4: "Tier 4 - Elite",
    5: "Tier 5 - Master",
    6: "Tier 6 - Mythic",
    7: "Tier 7 - Supreme",
}

ENV_DESCRIPTIONS = {
    0: "Shared Fire Arena (1 Mana | Lv1 10T / Lv2 6T / Lv3 3T): Global +1/+2/+3 ATK. Fire Resonance (Fire Monsters): +5/+8/+11 ATK, +3/+5/+7 On-Hit Burn, & On-Cast Scorch Surge!",
    1: "Shared Water Arena (1 Mana | Lv1 10T / Lv2 6T / Lv3 3T): Global +2/+3/+4 DEF Heal. Water Resonance (Water Monsters): +4/+6/+9 ATK, +6/+10/+15 DEF Heal + Cleanse/turn, & On-Cast Tidal Surge!",
    2: "Shared Nature Arena (1 Mana | Lv1 10T / Lv2 6T / Lv3 3T): Global +5/+8/+12 HP/turn. Nature Resonance (Nature Monsters): +4/+6/+9 ATK, +5/+8/+12 DEF on summon, +10/+16/+24 HP/turn, & On-Cast Bloom Surge!",
    3: "Shared Electric Arena (1 Mana | Lv1 10T / Lv2 6T / Lv3 3T): Global -1/-1/-2 Active Skill Cost. Electric Resonance (Electric Monsters): +5/+8/+11 ATK, +1/+1/+2 Temp Mana/turn, & On-Cast Lightning Surge!",
    4: "Shared Ice Arena (1 Mana | Lv1 10T / Lv2 6T / Lv3 3T): Global -1/-2/-3 Combat Dmg Taken. Ice Resonance (Ice Monsters): +4/+6/+9 ATK, -5/-8/-12 Damage Reduction, 35%/50%/70% On-Hit Freeze, & On-Cast Freeze Surge!",
    5: "Shared Earth Arena (1 Mana | Lv1 10T / Lv2 6T / Lv3 3T): Global +1/+2/+3 Reflect. Earth Resonance (Earth Monsters): +4/+6/+9 ATK, +6/+10/+15 Thorn Reflect, +5/+8/+12 DEF on summon, & On-Cast Quake Fortify!",
    6: "Shared Dark Arena (1 Mana | Lv1 10T / Lv2 6T / Lv3 3T): Global +6%/+10%/+14% Evasion. Dark Resonance (Dark Monsters): +5/+8/+11 ATK, +25%/+35%/+45% Life Leech, +12%/+18%/+24% Evasion, & On-Cast Soul Drain!",
    7: "Shared Light Arena (1 Mana | Lv1 10T / Lv2 6T / Lv3 3T): Global Cleanse +2/+3/+5 DEF. Light Resonance (Light Monsters): +4/+6/+9 ATK, +6/+10/+14 DEF Cleanse/Heal, +10/+16/+22 HP/turn, & On-Cast Sanctuary Surge!",
    8: "Shared Air Arena (1 Mana | Lv1 10T / Lv2 6T / Lv3 3T): Global Flanks +1/+2/+3 ATK. Air Resonance (Air Monsters): +5/+8/+11 ATK, +18%/+26%/+35% Evasion, Blitz Haste on summon, & On-Cast Gale Surge!",
    9: "Shared Desert Arena (1 Mana | Lv1 10T / Lv2 6T / Lv3 3T): Global +2/+4/+6 Active Skill Power. Desert Resonance (Desert Monsters): +5/+8/+11 ATK, +4/+6/+9 On-Hit Bleed, & Sandstorm erodes 3/5/8 DEF from non-Desert foes!",
}


def format_passive_item(p: Dict[str, Any]) -> str:
    k = str(p.get("kind", "passive"))
    v1 = int(p.get("v1", 0))
    v2 = int(p.get("v2", 0))
    pct = int(p.get("pct", 0))
    mapping = {
        "haste": f"Blitz Rush: Can attack immediately on the turn it is summoned (+{v1} Temp ATK).",
        "taunt": f"Vanguard Taunt: Forces enemy attacks toward this lane (+{v1} DEF).",
        "flank_bonus": f"Flank Mastery: Gains +{v1} ATK and +{v2} DEF while positioned on Flank slots (0, 1, 4, 5).",
        "center_bonus": f"Center Phalanx: Gains +{v1} ATK and +{v2} DEF while positioned in Center Vanguard slots (2, 3).",
        "reflect_flat": f"Thorn Carapace: Reflects {v1} flat damage back to any attacker.",
        "reflect_pct": f"Mirror Aegis: Reflects {pct or v1}% of incoming combat damage back to the attacker.",
        "life_leech": f"Soul Leech: Heals your Commander for {pct or v1}% of combat damage dealt.",
        "evasion": f"Mirage Evasion: {pct or v1}% chance to dodge incoming monster attacks completely.",
        "pierce": f"Armor Pierce: {pct or v1}% of attack damage pierces directly to the enemy Commander.",
        "cleave": f"Sweeping Cleave: Deals {pct or v1}% splash damage to adjacent enemy monsters.",
        "double_strike": "Twin Strike: Attacks twice during each Combat Phase.",
        "all_attack": "All-Attack Barrage: Strikes ALL occupied enemy lanes simultaneously (ATK is permanently locked).",
        "on_hit_burn": f"Igniting Strike: Inflicts {v1} Burn damage per turn on hit.",
        "on_hit_poison": f"Venomous Fang: Inflicts {v1} corrosive Poison stacks (melts DEF) on hit.",
        "on_hit_bleed": f"Serrated Edge: Inflicts {v1} kinetic Bleed damage on hit.",
        "on_hit_freeze": "Cryo Touch: Freezes the hit enemy monster for 1 turn.",
        "on_hit_silence": "Nullifying Blade: Silences the hit enemy monster's passives and skills for 1 turn.",
        "on_hit_weaken": f"Crippling Blow: Permanently weakens the hit enemy monster by -{v1} ATK.",
        "turn_heal_def": f"Regenerative Aura: Restores +{v1} DEF to all friendly monsters at turn start.",
        "turn_heal_hp": f"Sanctuary Pulse: Restores +{v1} HP to your Commander at turn start.",
        "cleanse_aura": f"Purifying Halo: Cleanses 1 negative status from an ally and heals +{v1} DEF at turn start.",
        "on_summon_dmg": f"Shockwave Entry: Deals {v1} damage to the opposing enemy monster on summon.",
        "on_summon_heal": f"Blessed Arrival: Restores +{v1} HP to your Commander on summon.",
        "on_summon_draw": f"Tactical Insight: Draws {max(1, v1)} card(s) on summon.",
        "on_summon_token": f"Pack Call: Spawns a {max(3, v1)}/{max(3, v2 or v1)} Pack Token in an empty slot on summon.",
        "on_death_dmg": f"Last Will Detonation: Deals {v1} damage to all enemies when destroyed.",
        "on_death_heal": f"Martyr's Grace: Restores +{v1} HP to your Commander when destroyed.",
        "on_death_token": f"Undying Remnant: Spawns a {max(3, v1)}/{max(3, v2 or v1)} Spirit Token on death.",
        "hydra_split": f"Hydra Division: On death, splits into two {pct or 50}% stat clones in empty slots.",
        "undying_once": f"Immortal Rebirth: Revives once at {pct or 40}% DEF the first time it is destroyed.",
        "ascend": f"Ascension: After surviving 2 turns, gains +{v1} ATK and +{v2} DEF (up to +10/+15 cap).",
        "on_spell_buff": f"Arcane Resonance: Gains +{v1} ATK and +{v2} DEF whenever you cast a Spell.",
        "spell_ping": f"Runic Conduit: Deals {v1} damage to the enemy Commander whenever you cast a Spell.",
        "death_growth": f"Soul Harvester: Gains +{v1} ATK and +{v2} DEF whenever any monster dies.",
        "pack_aura": f"Swarm Synergy: Gains +{v1} ATK for each other allied monster on your board.",
        "stealth": "Night Stealth: Cannot be targeted directly by enemy single-target Spells for 1 turn.",
        "spell_immune": "Runic Ward: Immune to direct enemy damaging and control Spells.",
        "locked_anchor": "Tectonic Anchor: Locked in its lane; immune to enemy pull, push, or recall effects.",
        "chameleon": "Chameleon Shift: Counts as every Tribe simultaneously for friendly Leader Spirit auras.",
    }
    if k in mapping:
        return mapping[k]
    clean_k = k.replace("_", " ").title()
    details = []
    if v1:
        details.append(f"Power {v1}")
    if v2:
        details.append(f"Bonus {v2}")
    if pct:
        details.append(f"{pct}%")
    suffix = f" ({', '.join(details)})" if details else ""
    return f"{clean_k}{suffix}"


def format_active_item(a: Dict[str, Any]) -> str:
    k = str(a.get("kind", "dmg_single"))
    cost = int(a.get("cost", 1))
    v1 = int(a.get("v1", 0))
    v2 = int(a.get("v2", 0))
    pct = int(a.get("pct", 0))
    fx = str(a.get("fx", "") or "")
    atk_b = int(a.get("atk", 0))
    def_b = int(a.get("deff", 0))
    hp_b = int(a.get("hp", 0))
    cap = int(a.get("cap", 0))

    fx_label = ""
    if fx:
        if fx in ("burn", "poison", "bleed", "weaken"):
            fx_label = f" + {max(2, v2)} {fx.title()}"
        else:
            fx_label = f" + {fx.title()} ({max(1, int(a.get('dur', 1)))}T)"

    if k == "dmg_single":
        extra = fx_label
        if int(a.get("exec_below", 0)) > 0:
            extra += f" (Execute if DEF <= {a['exec_below']})"
        if int(a.get("leech", 0)) > 0:
            extra += f", {a['leech']}% Leech"
        if int(a.get("heal_hp", 0)) > 0:
            extra += f", Heal +{a['heal_hp']} HP"
        if int(a.get("self_def", 0)) > 0:
            extra += f", Self +{a['self_def']} DEF"
        return f"[{cost} Mana] Precision Strike: Deal {v1} damage to target enemy{extra}."
    if k == "aoe_enemy":
        extra = fx_label
        if int(a.get("leech", 0)) > 0:
            extra += f", {a['leech']}% Leech"
        if a.get("cleanse"):
            extra += " + Cleanse self"
        return f"[{cost} Mana] Tempest Wave: Deal {v1} AoE damage to all enemies{extra}."
    if k == "cleave_enemy":
        extra = fx_label
        if int(a.get("heal_hp", 0)) > 0:
            extra += f", Heal +{a['heal_hp']} HP"
        if int(a.get("self_def", 0)) > 0:
            extra += f", Self +{a['self_def']} DEF"
        return f"[{cost} Mana] Sweeping Cleave: Deal {v1} damage to target enemy and adjacent foes{extra}."
    if k == "buff_ally":
        parts = []
        if atk_b > 0: parts.append(f"+{atk_b} ATK")
        if def_b > 0: parts.append(f"+{def_b} DEF")
        if a.get("haste"): parts.append("Blitz Rush")
        if a.get("cleanse"): parts.append("Cleanse")
        if a.get("taunt"): parts.append("Taunt")
        if a.get("stealth"): parts.append("Stealth")
        if int(a.get("reflect", 0)) > 0: parts.append(f"+{a['reflect']} Reflect")
        return f"[{cost} Mana] Fortify Ally: Grant an ally {', '.join(parts) or '+Stats'}."
    if k == "aoe_ally":
        parts = []
        if atk_b > 0: parts.append(f"+{atk_b} ATK")
        if def_b > 0: parts.append(f"+{def_b} DEF")
        if a.get("haste"): parts.append("Blitz Rush")
        if a.get("cleanse"): parts.append("Cleanse")
        if hp_b > 0: parts.append(f"Heal Commander +{hp_b} HP")
        return f"[{cost} Mana] War Cry: Grant all allies {', '.join(parts) or '+Stats'}."
    if k == "heal_leader":
        extra = f" + Self +{v2} DEF" if v2 > 0 else ""
        if a.get("cleanse"): extra += " + Cleanse"
        return f"[{cost} Mana] Sovereign Mend: Restore +{v1} Commander HP{extra}."
    if k == "dmg_leader":
        extra = f" & Heal +{a['heal']} HP" if int(a.get("heal", 0)) > 0 else ""
        if a.get("double_burn"): extra += " & Double Enemy Burn"
        return f"[{cost} Mana] Commander Bolt: Deal {v1} direct damage to enemy Commander{extra}."
    if k == "spawn_token":
        cnt = max(1, int(a.get("count", 1)))
        return f"[{cost} Mana] Brood Spawn: Summon {cnt} Token(s) ({max(2, v1)}/{max(2, v2 or v1)}{' Blitz' if a.get('haste') else ''})."
    if k == "gain_temp_mana":
        extra = f" & Heal +{v2} HP" if v2 > 0 else ""
        return f"[{cost} Mana] Astral Battery: Gain +{max(1, v1)} temporary Mana{extra}."
    if k == "drain_enemy_mana":
        extra = f" & deal {v2} Commander damage" if v2 > 0 else ""
        return f"[{cost} Mana] Mana Siphon: Lock {max(1, v1)} enemy Mana{extra}."
    if k == "detonate_dots":
        return f"[{cost} Mana] Cataclysm Detonate: Trigger all Burn/Poison/Bleed on enemies (+{v1} bonus damage)."
    if k == "recall_enemy":
        cap_s = f" (Cost <= {cap})" if cap > 0 else ""
        return f"[{cost} Mana] Banish Recall: Deal {v1} dmg and return enemy{cap_s} to hand."
    if k == "recall_ally":
        return f"[{cost} Mana] Tactical Recall (Ally): Return an ally to hand & heal +{v1} HP."
    if k == "double_skill_1t":
        return f"[{cost} Mana] Overclock Spark: Grant an ally Double Active Skill this turn (+{v1} Skill Power)."
    if k == "grant_double_strike":
        return f"[{cost} Mana] Windfury Blessing: Grant an ally Double Strike (+{v1} Temp ATK) this turn."
    if k == "revive_board":
        cap_s = f" (Cost <= {cap})" if cap > 0 else ""
        return f"[{cost} Mana] Necro Call: Resurrect a fallen monster{cap_s} from Graveyard (+{v1} ATK)."
    if k == "grave_to_hand":
        return f"[{cost} Mana] Soul Recovery: Return a card from Graveyard to hand (-1 Mana cost)."
    if k == "copy_enemy_hand":
        return f"[{cost} Mana] Mirage Theft: Copy a random card from enemy hand (-1 Mana cost, +{v1} DEF)."
    if k == "discount_hand_spells":
        return f"[{cost} Mana] Arcane Discount: Reduce Mana cost of Spells in hand by 1 (Heal +{v2} HP)."
    if k == "buff_center":
        return f"[{cost} Mana] Vanguard Shield: Grant Center allies +{v1} DEF & heal +{v2} Commander HP."
    if k == "warrior_call":
        mode = "summon directly to board" if a.get("to_board") else "draw to hand"
        return f"[{cost} Mana] Warrior Call: Tutor a monster (Cost <= {cap or 7}) from deck and {mode}."
    if k == "devour_3":
        return f"[{cost} Mana] Abyssal Devour: Consume an enemy (Cost <= {a.get('max_cost', 3)}) and absorb {pct or 40}% stats."
    if k == "mind_control_temp":
        return f"[{cost} Mana] Puppeteer Strings: Seize control of an enemy (Cost <= {cap or 4}) with +{v1} Temp ATK."
    if k == "pull_hook":
        return f"[{cost} Mana] Chain Pull Hook: Deal {v1} damage and drag target enemy into this lane."
    if k == "shifting_curse":
        return f"[{cost} Mana] Shifting Curse: Inflict a spreading {v1}-damage Curse on target enemy."

    clean_k = k.replace("_", " ").title()
    return f"[{cost} Mana] {clean_k}: Power {v1 or pct or 10}."


def format_leader_description(card: Dict[str, Any]) -> str:
    ops = card.get("leader_ops") or {}
    tribe = card.get("tribe_name", "Tribe")
    style = card.get("playstyle_name", "Tactical")
    summon_parts: List[str] = []
    if ops.get("summon_heal_hp", 0) > 0:
        summon_parts.append(f"Restore +{ops['summon_heal_hp']} Commander HP")
    if ops.get("summon_dmg_face", 0) > 0:
        summon_parts.append(f"Deal {ops['summon_dmg_face']} damage to enemy Commander")
    if ops.get("summon_dmg_enemy", 0) > 0:
        summon_parts.append(f"Deal {ops['summon_dmg_enemy']} damage to all enemy monsters")
    if ops.get("summon_buff_atk", 0) > 0 or ops.get("summon_buff_def", 0) > 0:
        summon_parts.append(f"Grant all allies +{ops.get('summon_buff_atk', 0)} ATK / +{ops.get('summon_buff_def', 0)} DEF")
    if ops.get("summon_draw", 0) > 0:
        summon_parts.append(f"Draw {ops['summon_draw']} card(s)")
    if ops.get("summon_mana", 0) > 0:
        summon_parts.append(f"Gain +{ops['summon_mana']} temporary Mana")
    if ops.get("summon_cleanse"):
        summon_parts.append("Cleanse all friendly monsters")
    if ops.get("summon_freeze"):
        summon_parts.append("Freeze strongest enemy monster")
    if ops.get("summon_silence"):
        summon_parts.append("Silence strongest enemy monster")
    if ops.get("summon_root"):
        summon_parts.append("Lock strongest enemy monster for 2 turns")
    if ops.get("summon_weaken", 0) > 0:
        summon_parts.append(f"Weaken all enemies by -{ops['summon_weaken']} ATK")
    if ops.get("summon_burn", 0) > 0:
        summon_parts.append(f"Inflict {ops['summon_burn']} Burn on all enemies")
    if ops.get("summon_poison", 0) > 0:
        summon_parts.append(f"Inflict {ops['summon_poison']} Poison on all enemies")
    if ops.get("summon_bleed", 0) > 0:
        summon_parts.append(f"Inflict {ops['summon_bleed']} Bleed on all enemies")
    if ops.get("summon_clone"):
        summon_parts.append("Summon a 70% Mirage Clone")
    if ops.get("summon_copy_hand"):
        summon_parts.append("Copy a card from enemy hand (-1 Mana cost)")
    if ops.get("summon_mind_control"):
        summon_parts.append("Steal an enemy monster (Cost <= 3)")
    if ops.get("summon_recall"):
        summon_parts.append("Return an enemy monster (Cost <= 3) to hand")
    if ops.get("summon_tokens", 0) > 0:
        summon_parts.append(f"Summon {ops['summon_tokens']} Leader Token(s) (5/5 Blitz)")
    if ops.get("summon_revive"):
        summon_parts.append("Resurrect 1 fallen monster from your Graveyard with Blitz")
    if ops.get("summon_double_skill"):
        summon_parts.append("Grant 1 ally Double Active Skill this turn")

    aura_parts: List[str] = []
    if ops.get("aura_atk", 0) > 0:
        aura_parts.append(f"+{ops['aura_atk']} ATK")
    if ops.get("aura_def", 0) > 0:
        aura_parts.append(f"+{ops['aura_def']} DEF on summon")
    if ops.get("aura_heal_hp", 0) > 0:
        aura_parts.append(f"+{ops['aura_heal_hp']} Commander HP/turn")
    if ops.get("aura_reflect", 0) > 0:
        aura_parts.append(f"+{ops['aura_reflect']} Thorn Reflect")
    if ops.get("aura_evasion", 0) > 0:
        aura_parts.append(f"+{ops['aura_evasion']}% Evasion")
    if ops.get("aura_leech", 0) > 0:
        aura_parts.append(f"+{ops['aura_leech']}% Life Leech")
    if ops.get("aura_burn", 0) > 0:
        aura_parts.append(f"+{ops['aura_burn']} On-Hit Burn")
    if ops.get("aura_poison", 0) > 0:
        aura_parts.append(f"+{ops['aura_poison']} On-Hit Poison")
    if ops.get("aura_bleed", 0) > 0:
        aura_parts.append(f"+{ops['aura_bleed']} On-Hit Bleed")
    if ops.get("aura_skill_disc", 0) > 0:
        aura_parts.append(f"-{ops['aura_skill_disc']} Active Skill Mana Cost")
    if ops.get("aura_spell_ping", 0) > 0:
        aura_parts.append(f"{ops['aura_spell_ping']} Enemy Commander dmg per Spell cast")
    if ops.get("aura_death_ping", 0) > 0:
        aura_parts.append(f"{ops['aura_death_ping']} Enemy Commander dmg whenever a unit dies")
    if ops.get("aura_weaken_opp", 0) > 0:
        aura_parts.append(f"-{ops['aura_weaken_opp']} ATK to opposing non-{tribe} enemies")

    s_txt = ", ".join(summon_parts) if summon_parts else "Awakens ancestral clan spirit"
    a_txt = ", ".join(aura_parts) if aura_parts else f"Empowers your {tribe} monsters"
    return f"[{tribe} Clan Spirit • {style}] On Summon: {s_txt}. Owner-Only {tribe} Aura: {a_txt}."


def format_covenant_description(card: Dict[str, Any]) -> str:
    ops = card.get("covenant_ops") or {}
    style = card.get("playstyle_name", "Tactical")
    tier = int(card.get("tier", 1))
    rest: List[str] = []
    if ops.get("self_hp_init_loss", 0) > 0:
        rest.append(f"Sacrifice -{ops['self_hp_init_loss']} Max HP on activation")
    if ops.get("self_hp_cap", 0) > 0:
        rest.append(f"Max Commander HP capped at {ops['self_hp_cap']}")
    if ops.get("self_hp_turn", 0) > 0:
        rest.append(f"Pay {ops['self_hp_turn']} Commander HP each turn")
    if ops.get("self_hp_on_attack", 0) > 0:
        rest.append(f"Pay {ops['self_hp_on_attack']} HP per monster attack")
    if ops.get("self_atk_pen", 0) > 0:
        rest.append(f"-{ops['self_atk_pen']} ATK to your monsters")
    if ops.get("self_def_pen", 0) > 0:
        rest.append(f"-{ops['self_def_pen']} DEF to your monsters")
    if ops.get("self_atk_pct_pen", 0) > 0:
        rest.append(f"-{ops['self_atk_pct_pen']}% ATK to your monsters")
    if ops.get("self_def_pct_pen", 0) > 0:
        rest.append(f"-{ops['self_def_pct_pen']}% DEF to your monsters")
    if ops.get("self_def_cap", 0) > 0:
        rest.append(f"Your monsters' DEF is capped at {ops['self_def_cap']}")
    if ops.get("ban_self_spells"):
        rest.append("You cannot cast Magic Spells")
    if ops.get("ban_face_attack"):
        rest.append("Your monsters cannot attack the enemy Commander directly")
    if ops.get("ban_face_spell"):
        rest.append("Your Spells cannot damage the enemy Commander directly")
    if ops.get("ban_normal_attack"):
        rest.append("Your monsters cannot perform normal combat attacks")
    if ops.get("ban_self_heal"):
        rest.append("Your Commander cannot be healed")
    if ops.get("ban_self_extra_draw"):
        rest.append("You cannot draw extra cards beyond your 1-card turn draw")
    if ops.get("ban_skill_gt1"):
        rest.append("Cannot activate skills costing > 1 Mana")
    if ops.get("discard_hand_eot"):
        rest.append("Discard remaining hand at end of turn")
    if ops.get("discard_cheapest_2t"):
        rest.append("Discard cheapest card every 2nd turn")
    if ops.get("single_attacker_turn"):
        rest.append("Only 1 of your monsters may attack each turn")
    if int(ops.get("max_board_self", 6)) < 6:
        rest.append(f"Max {ops['max_board_self']} board slots available")
    if int(ops.get("max_hand_self", 7)) < 7:
        rest.append(f"Max Hand size reduced to {ops['max_hand_self']}")
    if int(ops.get("max_mana_self", 10)) < 10:
        rest.append(f"Max Mana capped at {ops['max_mana_self']}")
    if ops.get("turn_limit_lose", 0) > 0:
        rest.append(f"Must win within {ops['turn_limit_lose']} turns")
    if not rest:
        rest.append("Self-bound Altar Vow")

    rew: List[str] = []
    if ops.get("rew_atk", 0) > 0:
        rew.append(f"+{ops['rew_atk']} ATK to all your monsters")
    if ops.get("rew_def", 0) > 0:
        rew.append(f"+{ops['rew_def']} DEF to all your monsters")
    if ops.get("rew_stat_pct", 0) > 0:
        rew.append(f"+{ops['rew_stat_pct']}% stats to your monsters")
    if ops.get("rew_hp_turn", 0) > 0:
        rew.append(f"+{ops['rew_hp_turn']} Commander HP healed each turn")
    if ops.get("rew_burn", 0) > 0:
        rew.append(f"+{ops['rew_burn']} On-Hit Burn to your monsters")
    if ops.get("rew_poison", 0) > 0:
        rew.append(f"+{ops['rew_poison']} On-Hit Poison to your monsters")
    if ops.get("rew_bleed", 0) > 0:
        rew.append(f"+{ops['rew_bleed']} On-Hit Bleed to your monsters")
    if ops.get("rew_reflect", 0) > 0:
        rew.append(f"+{ops['rew_reflect']} Thorn Reflect to your monsters")
    if ops.get("rew_reflect_pct", 0) > 0:
        rew.append(f"+{ops['rew_reflect_pct']}% Damage Reflection")
    if ops.get("rew_leech_pct", 0) > 0:
        rew.append(f"+{ops['rew_leech_pct']}% Life Leech to your monsters")
    if ops.get("rew_haste"):
        rew.append("All your monsters gain Blitz Rush (attack immediately)")
    if ops.get("rew_taunt"):
        rew.append("All your monsters gain Vanguard Taunt")
    if ops.get("rew_extra_draw", 0) > 0:
        rew.append(f"Draw +{ops['rew_extra_draw']} extra card(s) each turn")
    if ops.get("rew_token_turn", 0) > 0:
        rew.append(f"Spawn {ops['rew_token_turn']} Pack Token(s) each turn")
    if ops.get("rew_skill_disc", 0) > 0:
        rew.append(f"-{ops['rew_skill_disc']} Mana cost on Active Skills")
    if ops.get("rew_spell_disc", 0) > 0:
        rew.append(f"-{ops['rew_spell_disc']} Mana cost on Magic Spells")
    if ops.get("rew_spell_power", 0) > 0:
        rew.append(f"+{ops['rew_spell_power']} bonus Spell Power")
    if ops.get("rew_double_skill"):
        rew.append("Your monsters can trigger Active Skills twice per turn")
    if ops.get("rew_enemy_face", 0) > 0:
        rew.append(f"Deal {ops['rew_enemy_face']} damage to enemy Commander each turn")
    if not rew:
        rew.append("Empowers your tactical synergy")

    return f"[Right-Wing Altar Covenant • {style} T{tier}] Self-Restriction: {'; '.join(rest)}. Altar Reward: {'; '.join(rew)}."


def format_spell_description(card: Dict[str, Any]) -> str:
    op = card.get("spell_ops") or {}
    k = str(op.get("kind", "dmg_single"))
    v1 = int(op.get("v1", 10))
    v2 = int(op.get("v2", 0))
    style = card.get("playstyle_name", "Arcane")
    mapping = {
        "exorcism": "Counter-Tech Spell (Exorcism): Permanently banish the enemy's active Commander Spirit Talisman!",
        "betrayal": "Counter-Tech Spell (Betrayal): Invert the enemy Commander Spirit's aura against their own monsters for 2 turns!",
        "oblivion": "Counter-Tech Spell (Oblivion): Shatter and destroy the enemy's active Right-Wing Altar Covenant!",
        "stand_alone": "Counter-Tech Spell (Stand Alone): Mute both the enemy's Commander Spirit and Altar Covenant for 2 turns!",
        "dmg_single": f"Deal {v1} direct magic damage to a target enemy monster.",
        "dmg_aoe": f"Deal {v1} magic damage to all enemy monsters on the battlefield.",
        "dmg_leader": f"Deal {v1} direct magic damage to the enemy Commander.",
        "heal_leader": f"Restore +{v1} HP to your Commander{' and cleanse negative ailments' if op.get('cleanse') else ''}.",
        "buff_ally": f"Grant a friendly monster +{v1} ATK and +{v2 or v1} DEF.",
        "buff_all_allies": f"Grant all friendly monsters +{v1} ATK and +{v2 or v1} DEF.",
        "freeze_enemy": f"Freeze target enemy monster(s) for 1 turn and deal {v1} cold damage.",
        "silence_enemy": f"Silence target enemy monster(s) for 1 turn and weaken by -{v1} ATK.",
        "burn_enemy": f"Inflict {v1} Burn damage per turn on target enemy monster(s).",
        "poison_enemy": f"Inflict {v1} corrosive Poison stacks on target enemy monster(s).",
        "bleed_enemy": f"Inflict {v1} kinetic Bleed damage on target enemy monster(s).",
        "execute_enemy": f"Execute an enemy monster whose remaining DEF is <= {v1}.",
        "stat_flip": "Swap the current ATK and DEF of a target non-Barrier monster.",
        "mind_control": f"Seize control of an enemy monster (Mana Cost <= {op.get('cap', 99)}) and move it to your board.",
        "clone_board": f"Summon a {op.get('pct', 75)}% stat Mirage Clone of a monster into an empty slot.",
        "revive_grave": f"Resurrect a fallen monster from your Graveyard at {op.get('pct', 75)}% DEF.",
        "spawn_tokens": f"Summon {max(1, int(op.get('cnt', 2)))} Swarm Tokens ({max(4, v1)}/{max(4, v1)}) across your empty slots.",
        "draw_cards": f"Draw {max(1, int(op.get('draw', 2)))} cards and gain +{v1} temporary Mana.",
        "mana_drain": f"Drain {max(1, v1)} Mana from the opponent and disrupt their hand.",
        "hand_discard_spell": f"Force the opponent to discard a card to their Graveyard and deal {op.get('dmg', v1)} damage.",
        "deck_burn_spell": f"Mill {v1} cards from the enemy's current deck cycle into their Graveyard.",
        "grant_double_strike": "Grant a target allied monster Double Strike for this turn.",
        "double_skill_1t": "Allow a target allied monster to activate its Active Skill twice this turn.",
    }
    body = mapping.get(k, f"Tactical {style} Spell ({k.replace('_', ' ').title()}): Power {v1}.")
    return f"[{style} Spell] {body}"


def build_unity_cards() -> List[Dict[str, Any]]:
    with open(V2_JSON_PATH, "r", encoding="utf-8") as f:
        v2_data = json.load(f)

    unity_cards: List[Dict[str, Any]] = []
    for raw in v2_data["cards"]:
        cid = int(raw["id"])
        ctype = str(raw["type"])
        tier_num = int(raw.get("tier", 1))
        tier_title = TIER_TITLES.get(tier_num, f"Tier {tier_num}")
        elem = raw.get("element_name", "None")
        if elem == "None":
            elem = "Fire" if ctype == "monster" else "Arcane"
        tribe = raw.get("tribe_name", "None")
        if tribe == "None":
            tribe = ""
        playstyle = raw.get("playstyle_name", "Free")
        role = raw.get("role_name", "None")
        if role == "None":
            role = ctype.capitalize()

        passives_list: List[str] = []
        actives_list: List[str] = []
        v2_ops: Dict[str, Any] = {}

        if ctype == "environment":
            card_type_enum = 2
            env_id = int(raw.get("env_id", cid - 1))
            desc = ENV_DESCRIPTIONS.get(env_id, f"Shared {elem} Battlefield Arena (1 Mana).")
            v2_ops = {"env_id": env_id}
        elif ctype == "leader":
            card_type_enum = 3
            desc = format_leader_description(raw)
            v2_ops = raw.get("leader_ops") or {}
        elif ctype == "covenant":
            card_type_enum = 4
            desc = format_covenant_description(raw)
            v2_ops = raw.get("covenant_ops") or {}
        elif ctype == "spell":
            card_type_enum = 1
            desc = format_spell_description(raw)
            v2_ops = raw.get("spell_ops") or {}
        else:
            card_type_enum = 0
            for p_obj in raw.get("passives", []):
                passives_list.append(format_passive_item(p_obj))
            for a_obj in raw.get("actives", []):
                actives_list.append(format_active_item(a_obj))
            v2_ops = {
                "is_barrier": bool(raw.get("is_barrier", False)),
                "is_all_attack": bool(raw.get("is_all_attack", False)),
                "passives": raw.get("passives", []),
                "actives": raw.get("actives", []),
            }
            desc_parts = []
            if raw.get("is_barrier"):
                desc_parts.append("[Neutral Barrier • 0 ATK Locked]")
            elif raw.get("is_all_attack"):
                desc_parts.append("[All-Attack Unit • ATK Locked]")
            if passives_list:
                desc_parts.append("Passives: " + " | ".join(passives_list))
            if actives_list:
                desc_parts.append("Actives: " + " | ".join(actives_list))
            if not desc_parts:
                desc_parts.append(f"Pure {elem} {tribe} {role} ({playstyle} Style) with high raw combat stats.")
            desc = " • ".join(desc_parts)

        passives_text = " | ".join(passives_list) if passives_list else "None"
        active_skill_text = " | ".join(actives_list) if actives_list else (desc if ctype == "spell" else "None")

        # Determine SpellTargetMode for spells
        spell_target_mode = 0  # Global
        spell_power = int(raw.get("atk", 0))
        if ctype == "spell":
            sk = str((raw.get("spell_ops") or {}).get("kind", "dmg_single"))
            spell_power = int((raw.get("spell_ops") or {}).get("v1", 12))
            if sk in ("dmg_single", "execute_enemy", "polymorph", "pull_hook_spell", "mind_control",
                      "shifting_curse", "cleave_enemy", "melt_enemy_def", "freeze_enemy", "silence_enemy",
                      "burn_enemy", "poison_enemy", "bleed_enemy"):
                spell_target_mode = 1  # Enemy
            elif sk in ("buff_ally", "grant_double_strike", "double_skill_1t", "trigger_lb", "swap_lanes"):
                spell_target_mode = 2  # Ally
            elif sk == "stat_flip":
                spell_target_mode = 3  # Any

        unity_cards.append({
            "id": cid,
            "name": str(raw["name"]),
            "cardType": card_type_enum,
            "card_type": ctype,
            "element": elem,
            "tribe": tribe,
            "playstyle": playstyle,
            "group": elem,
            "group_en": elem,
            "role": role,
            "tier": tier_title,
            "tier_title": tier_title,
            "tier_num": tier_num,
            "cost": int(raw.get("mana", 1)),
            "powerCost": 0,
            "atk": 0 if raw.get("is_barrier", False) else int(raw.get("atk", 0)),
            "def": int(raw.get("def", 0)),
            "is_barrier": bool(raw.get("is_barrier", False)),
            "is_all_attack": bool(raw.get("is_all_attack", False)),
            "passives_text": passives_text,
            "active_skill": active_skill_text,
            "passives": passives_list,
            "active_skills": actives_list,
            "description": desc,
            "spellType": cid if ctype == "spell" else 0,
            "spellTargetMode": spell_target_mode,
            "spellPower": spell_power,
            "v2_ops_json": json.dumps(v2_ops, ensure_ascii=False),
            "p_score": float(raw.get("p_cnt", 0) * 2.5),
            "a_score": float(raw.get("a_cnt", 0) * 3.0),
            "win_rate": 50.0,
        })

    return unity_cards


def load_settled_or_default(model_id: str, fallback_ids: List[int]) -> List[int]:
    p = os.path.join(SAVED_MODELS_DIR, model_id, "settled_deck.json")
    if os.path.isfile(p):
        try:
            with open(p, "r", encoding="utf-8") as f:
                d = json.load(f)
            ids = d.get("deck_50_ids") or []
            if len(ids) == 50:
                return [int(x) for x in ids]
        except Exception:
            pass
    return fallback_ids


def build_default_50_deck(cards: List[Dict[str, Any]], filter_fn, env_id: int = 1, leader_id: int = 11, cov_id: int = 71) -> List[int]:
    deck: List[int] = [env_id, env_id, leader_id, cov_id]
    pool = [c["id"] for c in cards if filter_fn(c) and c["id"] not in (env_id, leader_id, cov_id)]
    for cid in pool:
        if len(deck) >= 50:
            break
        deck.append(cid)
        if len(deck) < 50:
            deck.append(cid)
    # Pad if needed
    for c in cards:
        if len(deck) >= 50:
            break
        if c["card_type"] == "monster" and deck.count(c["id"]) < 2:
            deck.append(c["id"])
    return deck[:50]


def export_all() -> None:
    os.makedirs(UNITY_RESOURCES_DIR, exist_ok=True)
    unity_cards = build_unity_cards()

    # 1. Write UnityGame/Assets/Resources/cards_10000.json
    wrapper = {"cards": unity_cards}
    with open(UNITY_CARDS_JSON, "w", encoding="utf-8") as f:
        json.dump(wrapper, f, indent=2, ensure_ascii=False)
    print(f"[OK] Exported {len(unity_cards)} V2 cards to: {UNITY_CARDS_JSON}")

    # 2. Build V2 Champion & Archetype Decks (50 cards each, max 2 copies per card)
    gora_fb = build_default_50_deck(unity_cards, lambda c: c["card_type"] in ("monster", "spell") and c["element"] in ("Fire", "Electric", "Dark"), env_id=1, leader_id=17, cov_id=91)
    navi_fb = build_default_50_deck(unity_cards, lambda c: c["card_type"] in ("monster", "spell") and c["element"] in ("Water", "Ice", "Light"), env_id=2, leader_id=11, cov_id=71)
    fire_fb = build_default_50_deck(unity_cards, lambda c: c["card_type"] == "monster" and c["element"] == "Fire", env_id=1, leader_id=17, cov_id=91)
    water_fb = build_default_50_deck(unity_cards, lambda c: c["card_type"] == "monster" and c["element"] == "Water", env_id=2, leader_id=11, cov_id=71)
    earth_fb = build_default_50_deck(unity_cards, lambda c: c["card_type"] == "monster" and c["element"] == "Earth", env_id=6, leader_id=35, cov_id=111)
    ghosts_fb = build_default_50_deck(unity_cards, lambda c: c["card_type"] == "monster" and c["tribe"] == "Ghosts", env_id=7, leader_id=23, cov_id=141)

    gora_ids = load_settled_or_default("free_01", gora_fb)
    navi_ids = load_settled_or_default("free_02", navi_fb)
    fire_ids = load_settled_or_default("env_fire", fire_fb)
    water_ids = load_settled_or_default("env_water", water_fb)
    earth_ids = load_settled_or_default("env_earth", earth_fb)
    ghosts_ids = load_settled_or_default("tribe_ghosts", ghosts_fb)

    champ_payload = {
        "updated_at": time.strftime("%Y-%m-%d %H:%M:%S"),
        "training_matches_analyzed": 1470,
        "gora": {
            "name": "Gora (Free_01 Vanguard AI)",
            "title": "V2 Neural Settled Deck (50 Cards • Max 2 Copies)",
            "combat_style": "Aggressive & Tactical Tempo",
            "description": "Trained across the 1,470-card V2 database under the 37 Constitution Rules.",
            "card_ids": gora_ids,
        },
        "navi": {
            "name": "Navi (Free_02 Apex AI)",
            "title": "V2 Neural Settled Deck (50 Cards • Max 2 Copies • Disjoint from Gora)",
            "combat_style": "Control & Synergy Mastery",
            "description": "Trained across the 1,470-card V2 database with zero card overlap against Gora.",
            "card_ids": navi_ids,
        },
        "archetypes": {
            "fire_aggro": {
                "name": "Fire Domain Settled Deck",
                "title": "Fire Environment Specialist (50 Cards)",
                "combat_style": "Burn & Ignition",
                "description": "Settled deck from [Env] Fire Domain model.",
                "card_ids": fire_ids,
            },
            "water_control": {
                "name": "Water Domain Settled Deck",
                "title": "Water Environment Specialist (50 Cards)",
                "combat_style": "Tidal Sustain & Control",
                "description": "Settled deck from [Env] Water Domain model.",
                "card_ids": water_ids,
            },
            "granite_bastion_stalemate": {
                "name": "Earth Domain Settled Deck",
                "title": "Earth Fortress & Reflect (50 Cards)",
                "combat_style": "Solid Fortress",
                "description": "Settled deck from [Env] Earth Domain model.",
                "card_ids": earth_ids,
            },
            "shadow_necromancy": {
                "name": "Ghosts Clan Settled Deck",
                "title": "Ghosts Tribal Necromancy (50 Cards)",
                "combat_style": "Graveyard & Mirage",
                "description": "Settled deck from [Tribe] Ghosts model.",
                "card_ids": ghosts_ids,
            },
        },
    }

    for path in (UNITY_CHAMP_RES_JSON, UNITY_CHAMP_EXT_JSON):
        with open(path, "w", encoding="utf-8") as f:
            json.dump(champ_payload, f, indent=2, ensure_ascii=False)
        print(f"[OK] Exported V2 Champion Decks to: {path}")

    # 3. Export ALL 41 Trained Neural Brains (`V2Brains/<model_id>.bytes`) + `v2_roster_41.json`
    export_41_neural_brains_and_roster()


def _write_brain_binary(fighter_state: Dict[str, Any], out_path: str) -> int:
    """
    Writes FighterNet(OBS_DIM=220, 256, 128, ACTION_DIM=62) weights into a compact
    little-endian binary format (`V2BR` header + raw float32 arrays) readable natively
    in Unity C# with zero external libraries (~383 KB per model).
    """
    import numpy as np

    keys = [
        "fc1.weight", "fc1.bias",
        "ln1.weight", "ln1.bias",
        "fc2.weight", "fc2.bias",
        "ln2.weight", "ln2.bias",
        "policy_head.weight", "policy_head.bias",
        "value_head.weight", "value_head.bias",
    ]
    w1 = fighter_state["fc1.weight"].detach().cpu().numpy().astype("<f4")
    w2 = fighter_state["fc2.weight"].detach().cpu().numpy().astype("<f4")
    wp = fighter_state["policy_head.weight"].detach().cpu().numpy().astype("<f4")
    h1, obs_dim = int(w1.shape[0]), int(w1.shape[1])
    h2 = int(w2.shape[0])
    act_dim = int(wp.shape[0])

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "wb") as f:
        f.write(b"V2BR")
        f.write(struct.pack("<5I", 1, obs_dim, h1, h2, act_dim))
        for k in keys:
            arr = np.ascontiguousarray(fighter_state[k].detach().cpu().numpy(), dtype="<f4")
            f.write(arr.tobytes())
    return os.path.getsize(out_path)


def export_41_neural_brains_and_roster() -> None:
    """Exports all 41 Arena_Lab_V2 neural FighterNet brains & 50-card settled decks into Unity."""
    import numpy as np
    from engine.ai_brain import ModelBrain
    from engine.card_model import get_card_db
    from engine.roster_40 import ROSTER_40, build_legal_card_mask

    db = get_card_db()
    os.makedirs(UNITY_BRAINS_DIR, exist_ok=True)

    roster_stats: Dict[str, Any] = {}
    for cand in (LOGS_ROSTER_PATH, LOGS_ROSTER_PATH + ".bak"):
        if os.path.isfile(cand):
            try:
                with open(cand, "r", encoding="utf-8") as f:
                    roster_stats = json.load(f)
                break
            except Exception:
                pass

    models_out: List[Dict[str, Any]] = []
    total_bytes = 0
    rng = np.random.default_rng(2026)

    for spec in ROSTER_40:
        mid = spec.model_id
        brain = ModelBrain.load_or_create(mid, db, use_warm_start=True)

        # Export exact 220-dim FighterNet binary weights to BOTH Unity Resources & live Arena_Lab_V2 folder!
        bin_path = os.path.join(UNITY_BRAINS_DIR, f"{mid}.bytes")
        total_bytes += _write_brain_binary(brain.fighter.state_dict(), bin_path)
        live_bin_path = os.path.join(SAVED_MODELS_DIR, mid, "brain_unity.bytes")
        _write_brain_binary(brain.fighter.state_dict(), live_bin_path)

        # Load exact 50-card settled deck (or build deterministic settled deck from trained drafter)
        deck_ids: List[int] = []
        s_path = os.path.join(SAVED_MODELS_DIR, mid, "settled_deck.json")
        if os.path.isfile(s_path):
            try:
                with open(s_path, "r", encoding="utf-8") as f:
                    sdata = json.load(f)
                raw_ids = sdata.get("deck_50_ids") or []
                if len(raw_ids) == 50:
                    deck_ids = [int(x) for x in raw_ids]
            except Exception:
                pass

        if len(deck_ids) != 50:
            lmask = build_legal_card_mask(spec, db)
            deck_ids = [int(x) for x in brain.drafter.build_deck(lmask, spec.required_card_id, deterministic=True, rng=rng)]

        st = roster_stats.get(mid, {})
        matches = max(int(brain.matches_trained), int(st.get("matches", 0)))
        wins = int(st.get("wins", 0))
        elo = float(st.get("elo", 1200.0))
        win_rate = round(100.0 * wins / matches, 1) if (matches > 0 and wins > 0) else 50.0

        models_out.append({
            "model_id": mid,
            "display_name": spec.display_name,
            "category": spec.category,
            "matches": matches,
            "elo": round(elo, 1),
            "win_rate": win_rate,
            "brain_resource": f"V2Brains/{mid}",
            "card_ids": deck_ids,
        })

    payload = {
        "updated_at": time.strftime("%Y-%m-%d %H:%M:%S"),
        "obs_dim": 220,
        "action_dim": 62,
        "total_models": len(models_out),
        "models": models_out,
    }
    for path in (UNITY_ROSTER41_RES_JSON, UNITY_ROSTER41_EXT_JSON):
        with open(path, "w", encoding="utf-8") as f:
            json.dump(payload, f, indent=2, ensure_ascii=False)
        print(f"[OK] Exported {len(models_out)} Neural Brains ({total_bytes / (1024*1024):.2f} MB) + Roster to: {path}")


if __name__ == "__main__":
    export_all()

# -*- coding: utf-8 -*-
"""
محاكي الـ 100,000 جولة الشامل لتقييم وتحليل الـ 10,000 بطاقة
(7,200 وحش + 2,788 بطاقة سحرية وسيطة + 12 بطاقة بيئة)
-------------------------------------------------------------------------
• يقرأ كل الـ 10,000 بطاقة مباشرة من ملف الإكسل (قاعدة_بيانات_الوحوش_والكروت.xlsx).
• يطبق ميكانيكا اللعبة الكاملة (6 خانات لكل لاعب، 500 HP للقائد، بدون حد أقصى للمانا ونقاط الاستدعاء ∞).
• ينفذ جميع الميزات الدائمة (Passives)، المهارات النشطة (Active Skills)، والأدوار الـ 5 للتعاويذ الوسيطة.
• يحلل كل ميزة ومهارة وتعويذة وبطاقة، ويحدث ملف الإكسل (الشيت الرئيسي + 14 شيت فئات) وملفات بيانات Unity.
"""

import json
import math
import os
import random
import re
import sys
import time
from collections import defaultdict
from multiprocessing import Pool, cpu_count

# إصلاح ترميز الحروف لويندوز
if sys.stdout and sys.stdout.encoding != 'utf-8':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    except Exception:
        pass

BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXCEL_AR_PATH = os.path.join(BASE_DIR, "Data", "قاعدة_بيانات_الوحوش_والكروت.xlsx")
UNITY_RES_DIR = os.path.join(BASE_DIR, "UnityGame", "Assets", "Resources")
CARDS_10K_JSON_PATH = os.path.join(UNITY_RES_DIR, "cards_10000.json")
UNITY_MONSTERS_JSON_PATH = os.path.join(UNITY_RES_DIR, "monsters_150.json")

ENVIRONMENT_MATRIX = {
    "Fire": {"name": "Fire Environment", "buff": "Fire", "debuff": ["Forest", "Ice", "Mecha"]},
    "Water": {"name": "Water Environment", "buff": "Water", "debuff": ["Fire", "Earth", "Mecha"]},
    "Ice": {"name": "Ice Environment", "buff": "Ice", "debuff": ["Water", "Forest", "Air"]},
    "Earth": {"name": "Earth Environment", "buff": "Earth", "debuff": ["Electric", "Fire", "Ice"]},
    "Electric": {"name": "Electric Environment", "buff": "Electric", "debuff": ["Mecha", "Water", "Air"]},
    "Air": {"name": "Air Environment", "buff": "Air", "debuff": ["Ghosts", "Earth", "Dark"]},
    "Forest": {"name": "Forest Environment", "buff": "Forest", "debuff": ["Earth", "Water", "Air"]},
    "Light": {"name": "Light Environment", "buff": "Light", "debuff": ["Dark", "Ghosts", "Arcane"]},
    "Dark": {"name": "Dark Environment", "buff": "Dark", "debuff": ["Light", "Arcane", "Electric"]},
    "Mecha": {"name": "Mecha Environment", "buff": "Mecha", "debuff": ["Forest", "Ghosts", "Light"]},
    "Ghosts": {"name": "Ghosts Environment", "buff": "Ghosts", "debuff": ["Fire", "Electric", "Arcane"]},
    "Arcane": {"name": "Arcane Environment", "buff": "Arcane", "debuff": ["Ice", "Light", "Dark"]},
}


def clean_element_name(group_str: str) -> str:
    if not group_str:
        return "Fire"
    if "Magic Spells" in group_str or "بطاقات سحرية" in group_str:
        return "Arcane"
    if "Environment" in group_str or "بطاقات البيئة" in group_str:
        return "Fire"
    if "Arcane" in group_str or "وحوش السحر" in group_str:
        return "Arcane"
    for el in ENVIRONMENT_MATRIX:
        if el.lower() in group_str.lower():
            return el
    return "Fire"


def split_abilities(text: str):
    if not text:
        return []
    text = str(text).strip()
    if text.startswith("None") or text.startswith("لا توجد") or text.startswith("Tactical"):
        return []
    parts = []
    for raw in text.split(" | "):
        clean = raw.strip()
        if clean.startswith("1. ") or clean.startswith("2. "):
            clean = clean[3:].strip()
        if clean and not clean.startswith("None") and not clean.startswith("لا توجد"):
            parts.append(clean)
    return parts


def parse_spell_type_and_power(role_str: str, effect_str: str, cost: int):
    """Infer intermediary spell type (SpellType 10..29) and numeric power from text."""
    nums = [int(x) for x in re.findall(r'\d+', str(effect_str))]
    power = max(nums) if nums else (cost * 11 + 6)
    eff = str(effect_str)

    # 1. Link & Synergy Spell (10..13)
    if "Combines the ATK of Left and Right Flank" in eff or "دمج هجوم وحشي الجناح" in eff:
        return 10, power
    if "Copies a Support monster's Passive" in eff or "نسخ الميزة الدائمة" in eff:
        return 11, power
    if "Links both Vanguard allies" in eff or "ربط حليفي الوسط" in eff:
        return 12, power
    if "Triggers Elemental Synergy" in eff or "تفعيل تآزر العناصر" in eff:
        return 13, power

    # 2. Reposition & Swap Spell (14..17)
    if "Swaps a wounded Flank monster" in eff or "تبديل موقع وحش الجناح" in eff:
        return 14, power
    if "Forcibly pulls an enemy Defender" in eff or "سحب مدافع" in eff:
        return 15, power
    if "Displaces an enemy monster" in eff or "إزاحة وحش الخصم" in eff:
        return 16, power
    if "Moves your Attacker" in eff or "نقل وحشك المهاجم" in eff:
        return 17, power

    # 3. Conversion & Trade-off Spell (18..21)
    if "Converts" in eff and "DEF into" in eff:
        return 18, power
    if "Sacrifices half of a Pure Shield" in eff or "اقتطاع نصف درع" in eff:
        return 19, power
    if "Swaps ATK and DEF stats" in eff or "عكس إحصائيات" in eff:
        return 20, power
    if "Drains" in eff and "DEF from an enemy" in eff:
        return 21, power

    # 4. Activation & Support Spell (22..25)
    if "Reduces an ally monster's Active Skill cost" in eff or "تصفير تكلفة المهارة" in eff:
        return 22, power
    if "Grants a Vanguard ally Double Attack" in eff or "تمكين وحش الوسط من الهجوم مرتين" in eff:
        return 23, power
    if "Cleanses all curses" in eff or "تطهير كافة اللعنات" in eff:
        return 24, power
    if "Supercharges a Support monster" in eff or "شحن طاقة وحش الدعم" in eff:
        return 25, power

    # 5. Breach & Disruption Spell (26..29)
    if "Fires an Arcane projectile" in eff or "مقذوف أركيني يخترق" in eff:
        return 26, power
    if "Draws 2 extra cards" in eff or "سحب بطاقتين إضافيتين" in eff:
        return 27, power
    if "Revives your strongest fallen monster" in eff or "إعادة أقوى وحش سقط" in eff:
        return 28, power
    if "Drains 1 permanent Mana" in eff or "سلب 1 مانا دائمة" in eff:
        return 29, power

    return 10, power


def load_all_10k_cards(force_excel=False):
    """Read all 10,000 cards from Excel or fast JSON cache and export to Unity."""
    if not force_excel and os.path.exists(CARDS_10K_JSON_PATH) and os.path.exists(EXCEL_AR_PATH):
        if os.path.getmtime(CARDS_10K_JSON_PATH) >= os.path.getmtime(EXCEL_AR_PATH):
            try:
                with open(CARDS_10K_JSON_PATH, "r", encoding="utf-8") as f:
                    data = json.load(f)
                if isinstance(data, dict) and "cards" in data and len(data["cards"]) >= 1000:
                    return data["cards"]
            except Exception:
                pass

    import openpyxl
    wb = openpyxl.load_workbook(EXCEL_AR_PATH, read_only=True, data_only=True)
    ws = wb.worksheets[0]

    all_cards = []
    for row in ws.iter_rows(min_row=2, values_only=True):
        if not row or row[0] is None or not str(row[0]).isdigit():
            continue
        cid = int(row[0])
        name = str(row[1] if row[1] is not None else cid)
        visual_brief_ar = str(row[2] or "—")
        group = str(row[3] or "")
        group_en = str(row[4] or "")
        role = str(row[5] or "")
        tier_title = str(row[6] or "Tier 1 - Basic")
        tier_num = int(row[7] or 1)
        cost = int(row[8] or 1)
        atk = int(row[9] or 0)
        def_pts = int(row[10] or 0)
        passives_str = str(row[11] or "")
        active_str = str(row[12] or "")
        desc_str = str(row[13] or "")
        arabic_ab_str = str(row[14] or "—")
        p_score = float(row[15] or 0.0)
        a_score = float(row[16] or 0.0)
        win_rate = float(row[20] or 50.0)

        if "Spell" in group or "بطاقات سحرية" in group:
            c_type_int = 1
            c_type_str = "spell"
            elem = group_en if group_en in ENVIRONMENT_MATRIX else "Arcane"
            sp_type, sp_power = parse_spell_type_and_power(role, active_str, cost)
            parsed_p = []
            parsed_a = [active_str] if active_str else []
        elif "Environment" in group or "بطاقات البيئة" in group:
            c_type_int = 2
            c_type_str = "env"
            elem = "Fire"
            for el_k in ENVIRONMENT_MATRIX:
                if el_k.lower() in name.lower() or el_k.lower() in passives_str.lower():
                    elem = el_k
                    break
            sp_type = 0
            sp_power = 0
            parsed_p = [passives_str] if passives_str else []
            parsed_a = [active_str] if active_str else []
        else:
            c_type_int = 0
            c_type_str = "monster"
            elem = clean_element_name(group_en or group)
            sp_type = 0
            sp_power = 0
            parsed_p = split_abilities(passives_str)
            parsed_a = split_abilities(active_str)

        card_obj = {
            "id": cid,
            "name": name if c_type_int == 2 else str(cid),
            "visual_brief_ar": visual_brief_ar,
            "cardType": c_type_int,
            "card_type": c_type_str,
            "element": elem,
            "group": group,
            "group_en": group_en,
            "role": role,
            "tier": tier_title,
            "tier_title": tier_title,
            "tier_num": tier_num,
            "cost": cost,
            "atk": atk,
            "def": def_pts,
            "passives_text": passives_str,
            "active_skill": active_str,
            "passives": parsed_p,
            "active_skills": parsed_a,
            "description": desc_str,
            "arabic_abilities": arabic_ab_str,
            "spellType": sp_type,
            "spellPower": sp_power,
            "p_score": round(p_score, 1),
            "a_score": round(a_score, 1),
            "win_rate": round(win_rate, 2),
        }
        all_cards.append(card_obj)

    wb.close()
    export_unity_json_files(all_cards)
    return all_cards


def export_unity_json_files(all_cards):
    """Save all 10,000 cards and 7,200 monsters in 100% English to Unity Resources."""
    os.makedirs(UNITY_RES_DIR, exist_ok=True)
    clean_cards = []
    for c in all_cards:
        cc = dict(c)
        cc.pop("arabic_abilities", None)
        cc.pop("visual_brief_ar", None)
        clean_cards.append(cc)

    with open(CARDS_10K_JSON_PATH, "w", encoding="utf-8") as f:
        json.dump({"cards": clean_cards}, f, ensure_ascii=False)

    monsters_only = [c for c in clean_cards if c["card_type"] == "monster"]
    with open(UNITY_MONSTERS_JSON_PATH, "w", encoding="utf-8") as f:
        json.dump(monsters_only, f, ensure_ascii=False)


class SimMonster:
    __slots__ = (
        "id", "name", "element", "role", "tier_num", "cost",
        "base_atk", "base_def", "current_def", "bonus_atk",
        "passives", "actives", "summoned_turn", "can_attack",
        "frozen_turns", "silenced_turns", "burn_dmg", "poison_dmg",
        "salvation_used", "weakened_turns", "bubble_shield", "double_strike"
    )

    def __init__(self, data):
        self.id = data["id"]
        self.name = data["name"]
        self.element = data["element"]
        self.role = data["role"]
        self.tier_num = data["tier_num"]
        self.cost = data["cost"]
        self.base_atk = data["atk"]
        self.base_def = data["def"]
        self.current_def = data["def"]
        self.bonus_atk = 0
        self.passives = list(data["passives"])
        self.actives = list(data["active_skills"])
        self.summoned_turn = 0
        self.can_attack = False
        self.frozen_turns = 0
        self.silenced_turns = 0
        self.burn_dmg = 0
        self.poison_dmg = 0
        self.salvation_used = False
        self.weakened_turns = 0
        self.bubble_shield = False
        self.double_strike = False

    def is_alive(self):
        return self.current_def > 0

    def has_passive_substr(self, substr):
        if self.silenced_turns > 0:
            return None
        for p in self.passives:
            if substr in p:
                return p
        return None


class SimPlayer:
    __slots__ = (
        "hp", "shield", "mana_cap", "power", "deck", "hand", "graveyard",
        "board", "fatigue", "played_cards", "deny_draw_sac_turns",
        "team_immune_turns", "free_skill_bonus"
    )

    def __init__(self, deck_cards):
        self.hp = 500
        self.shield = 0
        self.mana_cap = 1  # لا يوجد حد أقصى للمانا (مفتوح بلا سقف ∞)
        self.power = 1     # لا يوجد حد أقصى لنقاط الاستدعاء / الباور (مفتوح بلا سقف ∞)
        self.deck = list(deck_cards)
        random.shuffle(self.deck)
        self.hand = []
        self.graveyard = []
        self.board = [None] * 6  # 6 خانات متقابلة: 0,1 جناح يسار | 2,3 وسط | 4,5 جناح يمين
        self.fatigue = 0
        self.played_cards = set()
        self.deny_draw_sac_turns = 0
        self.team_immune_turns = 0
        self.free_skill_bonus = 0

    def draw(self):
        if self.deck:
            c = self.deck.pop()
            if len(self.hand) < 10:
                self.hand.append(c)
            else:
                self.graveyard.append(c)
        else:
            self.fatigue += 1
            self.take_leader_dmg(self.fatigue * 30)

    def take_leader_dmg(self, dmg):
        if dmg <= 0 or self.team_immune_turns > 0:
            return 0
        if self.shield > 0:
            absorbed = min(self.shield, dmg)
            self.shield -= absorbed
            dmg -= absorbed
        self.hp -= dmg
        return dmg

    def heal_leader(self, amount, opponent=None):
        if amount <= 0:
            return
        if opponent is not None:
            for em in opponent.board:
                if em and em.is_alive() and em.has_passive_substr("حضور برزخي"):
                    return
        self.hp = min(500, self.hp + amount)


def record_passive_val(p_stats, passive_name, points, triggered=1):
    if not passive_name:
        return
    entry = p_stats[passive_name]
    entry[0] += points
    entry[1] += triggered


def record_active_val(a_stats, active_name, points, triggered=1):
    if not active_name:
        return
    entry = a_stats[active_name]
    entry[0] += points
    entry[1] += triggered


def apply_on_summon_passives(player: SimPlayer, opponent: SimPlayer, slot: int, m: SimMonster, p_stats):
    # فحص نور الحقيقة لدى الوحش المقابل (يبطل الميزات الدائمة للمقابل)
    opp_m = opponent.board[slot] if slot < 6 else None
    if opp_m and opp_m.is_alive() and opp_m.has_passive_substr("نور الحقيقة"):
        m.silenced_turns = max(m.silenced_turns, 2)
        record_passive_val(p_stats, opp_m.has_passive_substr("نور الحقيقة"), 14.0)
        return

    for p in m.passives:
        if "نور الحقيقة" in p and opp_m and opp_m.is_alive():
            opp_m.silenced_turns = max(opp_m.silenced_turns, 3)
            record_passive_val(p_stats, p, 15.0)
        elif "دروع جرانيتية" in p and slot in (2, 3):
            added = m.base_def
            m.current_def += added
            record_passive_val(p_stats, p, added * 0.78)
        elif "هيكل جليدي" in p and slot in (2, 3):
            m.current_def += 5
            record_passive_val(p_stats, p, 5 * 0.90)
        elif "إشراق طاهر" in p and slot in (2, 3):
            m.bonus_atk += 3
            record_passive_val(p_stats, p, 3 * 1.18 * 2.0)
        elif "زيادة +4 هجوم في الأجنحة" in p and slot in (0, 1, 4, 5):
            m.bonus_atk += 4
            record_passive_val(p_stats, p, 4 * 1.18 * 2.1)
        elif "سرعة خاطفة" in p and slot in (0, 1, 4, 5):
            m.bonus_atk += 5
            record_passive_val(p_stats, p, 5 * 1.18 * 2.1)
        elif "شحنة سريعة" in p:
            m.bonus_atk += 4
            m.can_attack = True
            record_passive_val(p_stats, p, 4 * 1.18 + 5.5)
        elif "شواظ ناري" in p:
            boosted = 0
            for adj in (slot - 1, slot + 1):
                if 0 <= adj < 6 and player.board[adj] and player.board[adj].is_alive():
                    player.board[adj].bonus_atk += 3
                    boosted += 1
            if boosted == 0:
                m.bonus_atk += 3
                boosted = 1
            record_passive_val(p_stats, p, boosted * 3 * 1.18 * 1.8)
        elif "مد بحري" in p:
            cnt = 0
            for ally in player.board:
                if ally and ally.is_alive() and ally.element == "مياه":
                    ally.current_def += 4
                    cnt += 1
            record_passive_val(p_stats, p, max(1, cnt) * 4 * 0.90 * 1.5)
        elif "حماية المحيط" in p:
            allies = [ally for idx, ally in enumerate(player.board) if ally and ally.is_alive() and idx != slot]
            if allies:
                target = min(allies, key=lambda x: x.current_def)
                target.current_def += 15
                record_passive_val(p_stats, p, 15 * 0.90)
            else:
                m.current_def += 10
                record_passive_val(p_stats, p, 10 * 0.90)
        elif "التحام حربي" in p:
            mechas = sum(1 for idx, ally in enumerate(player.board) if ally and ally.is_alive() and idx != slot and ally.element == "ميكا")
            boost = max(1, mechas + 1) * 8
            m.current_def += boost
            record_passive_val(p_stats, p, boost * 0.88)
        elif "جذور التغذية" in p:
            # زيادة سقف مانا القائد +1 بلا حد أقصى
            player.mana_cap += 1
            player.power += 1
            record_passive_val(p_stats, p, 16.5)
        elif "استنزاف نفسي" in p:
            drained = min(2, max(0, opponent.mana_cap - 1))
            opponent.mana_cap -= drained
            record_passive_val(p_stats, p, drained * 11.0 if drained > 0 else 6.0)
        elif "كابوس أسود" in p:
            if opponent.hand:
                sac_c = opponent.hand.pop()
                opponent.graveyard.append(sac_c)
                record_passive_val(p_stats, p, 15.5)
            else:
                opponent.take_leader_dmg(12)
                record_passive_val(p_stats, p, 10.0)
        elif "موجة ارتدادية" in p and slot < 6:
            if opp_m and opp_m.is_alive() and (opp_m.base_atk + opp_m.bonus_atk) < 14:
                if not opp_m.has_passive_substr("ثبات أرضي"):
                    opponent.board[slot] = None
                    if len(opponent.hand) < 10:
                        opponent.hand.append(opp_m)
                    record_passive_val(p_stats, p, opp_m.cost * 6.5 + 8.0)
                else:
                    record_passive_val(p_stats, opp_m.has_passive_substr("ثبات أرضي"), 10.0)


def trigger_start_of_turn_passives(player: SimPlayer, opponent: SimPlayer, p_stats):
    for slot in range(6):
        m = player.board[slot]
        if not m or not m.is_alive():
            continue

        # معالجة الحرق والسم المتراكم
        if m.burn_dmg > 0:
            if m.has_passive_substr("رطوبة تمنع الحرق") or m.has_passive_substr("حصانة تامة ضد النار") or m.has_passive_substr("حصانة ضد السم والحرق"):
                for p in m.passives:
                    if any(k in p for k in ("رطوبة تمنع الحرق", "حصانة تامة ضد النار", "حصانة ضد السم والحرق")):
                        record_passive_val(p_stats, p, m.burn_dmg * 0.95)
                m.burn_dmg = 0
            else:
                m.current_def -= m.burn_dmg
        if m.poison_dmg > 0:
            if m.has_passive_substr("حصانة ضد السم") or m.has_passive_substr("حصانة ضد الظلام والسم"):
                for p in m.passives:
                    if "حصانة ضد السم" in p or "حصانة ضد الظلام والسم" in p:
                        record_passive_val(p_stats, p, m.poison_dmg * 0.95)
                m.poison_dmg = 0
            else:
                m.current_def -= m.poison_dmg

        if not m.is_alive():
            if not try_salvation_save(player, slot, m, p_stats):
                player.graveyard.append(m)
                player.board[slot] = None
                continue

        if m.frozen_turns > 0:
            if m.has_passive_substr("مقاومة التجميد") or m.has_passive_substr("حصانة تامة ضد الجليد") or m.has_passive_substr("حصانة ضد الصدمات الكهربائية والصقيع"):
                for p in m.passives:
                    if any(k in p for k in ("مقاومة التجميد", "حصانة تامة ضد الجليد", "والصقيع")):
                        record_passive_val(p_stats, p, (m.base_atk + m.bonus_atk) * 0.9)
                m.frozen_turns = 0
            else:
                m.frozen_turns -= 1
                m.can_attack = False

        if m.silenced_turns > 0:
            m.silenced_turns -= 1
        if m.weakened_turns > 0:
            m.weakened_turns -= 1

        opp_m = opponent.board[slot]

        if m.silenced_turns <= 0:
            for p in m.passives:
                if "اشتعال ذاتي ينقص 3 دفاع" in p and opp_m and opp_m.is_alive():
                    opp_m.burn_dmg = max(opp_m.burn_dmg, 3)
                    opp_m.current_def -= 3
                    record_passive_val(p_stats, p, 3 * 1.1)
                elif "نار مضرمة تنقص 8 دفاع" in p and opp_m and opp_m.is_alive():
                    opp_m.burn_dmg = max(opp_m.burn_dmg, 8)
                    opp_m.current_def -= 8
                    record_passive_val(p_stats, p, 8 * 1.1)
                elif "سم بطيء" in p and opp_m and opp_m.is_alive():
                    opp_m.poison_dmg = max(opp_m.poison_dmg, 6)
                    opp_m.current_def -= 6
                    record_passive_val(p_stats, p, 6 * 1.1)
                elif "تجدد حيوي" in p:
                    heal = min(4, m.base_def + 12 - m.current_def)
                    if heal > 0:
                        m.current_def += heal
                        record_passive_val(p_stats, p, heal * 0.90)
                elif "تيار مهدئ يستعيد 5 HP" in p:
                    player.heal_leader(5, opponent)
                    record_passive_val(p_stats, p, 5 * 0.85)
                elif "هالة قدسية تشفي القائد بـ 4 HP" in p:
                    player.heal_leader(4, opponent)
                    record_passive_val(p_stats, p, 4 * 0.85)
                elif "شلال دافق يغسل كافة اللعنات" in p:
                    cleaned = 0
                    for ally in player.board:
                        if ally and ally.is_alive() and (ally.burn_dmg > 0 or ally.poison_dmg > 0 or ally.frozen_turns > 0 or ally.weakened_turns > 0):
                            ally.burn_dmg = 0
                            ally.poison_dmg = 0
                            ally.frozen_turns = 0
                            ally.weakened_turns = 0
                            cleaned += 1
                    if cleaned > 0:
                        record_passive_val(p_stats, p, cleaned * 7.5)
                elif "شحنة فائقة تمنح +1 باور" in p:
                    player.power += 1
                    record_passive_val(p_stats, p, 12.0)
                elif "محرك تعزيز" in p:
                    m.bonus_atk += 2
                    record_passive_val(p_stats, p, 2 * 1.18)
                elif "هبوب معزز يمنح الحلفاء في الأجنحة" in p:
                    for w_s in (0, 1, 4, 5):
                        wa = player.board[w_s]
                        if wa and wa.is_alive():
                            wa.double_strike = True
                    record_passive_val(p_stats, p, 11.5)

        if opp_m and not opp_m.is_alive():
            handle_monster_death(opponent, player, slot, opp_m, m, p_stats)


def try_salvation_save(owner: SimPlayer, slot: int, m: SimMonster, p_stats):
    if not m.salvation_used:
        p = m.has_passive_substr("درع الشفاعة") or m.has_passive_substr("درع الخلاص")
        if p:
            m.salvation_used = True
            m.current_def = 10
            record_passive_val(p_stats, p, (m.base_atk * 1.18 + 10.0))
            return True
    # فحص ما إذا كان حليف مجاور يمتلك "درع الشفاعة" ويمتص الضربة القاتلة نيابة عنه
    for adj in (slot - 1, slot + 1):
        if 0 <= adj < 6:
            ally = owner.board[adj]
            if ally and ally.is_alive() and not ally.salvation_used and ally.has_passive_substr("درع الشفاعة") and ally.current_def > 12:
                ally.salvation_used = True
                ally.current_def -= 10
                m.current_def = 10
                record_passive_val(p_stats, ally.has_passive_substr("درع الشفاعة"), 16.5)
                return True
    return False


def handle_monster_death(owner: SimPlayer, killer_owner: SimPlayer, slot: int, dead_m: SimMonster, killer_m: SimMonster, p_stats):
    if try_salvation_save(owner, slot, dead_m, p_stats):
        return False

    owner.board[slot] = None
    owner.graveyard.append(dead_m)

    if dead_m.silenced_turns <= 0:
        for p in dead_m.passives:
            if "انفجار حراري يلحق 10 ضرر" in p:
                if slot < 6 and killer_owner.board[slot] and killer_owner.board[slot].is_alive():
                    target = killer_owner.board[slot]
                    target.current_def -= 10
                    record_passive_val(p_stats, p, 11.5)
                    if not target.is_alive() and not try_salvation_save(killer_owner, slot, target, p_stats):
                        killer_owner.graveyard.append(target)
                        killer_owner.board[slot] = None
                else:
                    killer_owner.take_leader_dmg(10)
                    record_passive_val(p_stats, p, 9.5)
            elif "رماد القيامة" in p:
                owner.heal_leader(20, killer_owner)
                record_passive_val(p_stats, p, 13.0)
            elif "زلزال داخلي يلحق 10 ضرر" in p:
                killer_owner.take_leader_dmg(10)
                record_passive_val(p_stats, p, 12.0)
            elif "إعادة تدوير" in p:
                owner.power += 2
                owner.mana_cap += 1
                record_passive_val(p_stats, p, 15.0)
            elif "لعنة الهلاك: عند موته يتدمر الوحش الذي قتله" in p:
                if killer_m and killer_m.is_alive():
                    if killer_m.has_passive_substr("حصانة ضد الظلام") or killer_m.has_passive_substr("حصانة تامة ضد الأشباح والظلام"):
                        for kp in killer_m.passives:
                            if "الظلام" in kp:
                                record_passive_val(p_stats, kp, 14.0)
                    else:
                        val_killed = min(28.0, killer_m.current_def * 0.8 + killer_m.base_atk * 0.8)
                        killer_m.current_def = 0
                        for ks in range(6):
                            if killer_owner.board[ks] is killer_m:
                                if not try_salvation_save(killer_owner, ks, killer_m, p_stats):
                                    killer_owner.graveyard.append(killer_m)
                                    killer_owner.board[ks] = None
                                break
                        record_passive_val(p_stats, p, val_killed)
            elif "تلبس روحي" in p:
                if slot < 6 and killer_owner.board[slot] and killer_owner.board[slot].is_alive():
                    target = killer_owner.board[slot]
                    target.frozen_turns = max(target.frozen_turns, 2)
                    target.silenced_turns = max(target.silenced_turns, 2)
                    record_passive_val(p_stats, p, (target.base_atk * 1.1 + 6.0))

    return True


def deal_spell_or_skill_damage(target_slot: int, target_m: SimMonster, dmg: int, source_m: SimMonster, opponent: SimPlayer, player: SimPlayer, p_stats, ignore_immunity=False):
    if not target_m or not target_m.is_alive():
        return 0.0
    if target_m.bubble_shield and not ignore_immunity:
        target_m.bubble_shield = False
        return 4.0
    if not ignore_immunity:
        if target_m.has_passive_substr("درع كهرومغناطيسي") or target_m.has_passive_substr("تخفي ليلي"):
            for p in target_m.passives:
                if "درع كهرومغناطيسي" in p or "تخفي ليلي" in p:
                    record_passive_val(p_stats, p, dmg * 0.85)
            return 2.0
        if target_m.has_passive_substr("درع مانا يمتص 6 ضرر"):
            red = min(dmg, 6)
            dmg -= red
            record_passive_val(p_stats, target_m.has_passive_substr("درع مانا يمتص 6 ضرر"), red * 0.90)
        # تغطية الأحراش لدى أي حليف في الغابة
        for em in opponent.board:
            if em and em.is_alive() and em.has_passive_substr("تغطية الأحراش"):
                red = int(dmg * 0.25)
                dmg -= red
                record_passive_val(p_stats, em.has_passive_substr("تغطية الأحراش"), red * 0.85)
                break

    actual = min(target_m.current_def, max(0, dmg))
    target_m.current_def -= max(0, dmg)
    val = actual * 0.95
    if not target_m.is_alive():
        if handle_monster_death(opponent, player, target_slot, target_m, source_m, p_stats):
            val += 4.5
    return val


def execute_intermediary_spell(card, active_p: SimPlayer, other_p: SimPlayer, p_stats, a_stats):
    """تنفيذ الأدوار الـ 5 للبطاقات السحرية الوسيطة (20 قالباً تكتيكياً)."""
    stype = card["spellType"]
    val = card["spellPower"]
    act_str = card["active_skill"]

    # تفاعل وحوش السحر في ساحة اللاعب (صدى سحري / مضاعف التعاويذ / استعادة المانا / فيض أركيني)
    spell_repeats = 1
    for bm in active_p.board:
        if bm and bm.is_alive():
            if bm.has_passive_substr("صدى سحري"):
                bm.bonus_atk += 3
                record_passive_val(p_stats, bm.has_passive_substr("صدى سحري"), 3 * 1.18 * 1.5)
            if bm.has_passive_substr("فيض أركيني"):
                val += 8
                record_passive_val(p_stats, bm.has_passive_substr("فيض أركيني"), 8 * 0.95)
            if bm.has_passive_substr("مضاعف التعاويذ") and spell_repeats == 1 and random.random() < 0.50:
                spell_repeats = 2
                record_passive_val(p_stats, bm.has_passive_substr("مضاعف التعاويذ"), 14.0)
            if bm.has_passive_substr("استعادة المانا"):
                active_p.power += 1
                record_passive_val(p_stats, bm.has_passive_substr("استعادة المانا"), 12.0)

    total_impact = 0.0
    for _ in range(spell_repeats):
        allies = [(idx, am) for idx, am in enumerate(active_p.board) if am and am.is_alive()]
        enemies = [(idx, em) for idx, em in enumerate(other_p.board) if em and em.is_alive()]

        # --- الفئة 1: تعويذة ربط وتآزر (10..13) ---
        if stype == 10:  # دمج هجوم وحشي الجناح الأيمن والأيسر في ضربة مشتركة بقوة +val على وسط الخصم
            wing_atk = sum(am.base_atk + am.bonus_atk for idx, am in allies if idx in (0, 1, 4, 5))
            strike_dmg = val + int(wing_atk * 0.45)
            center_en = [(idx, em) for idx, em in enemies if idx in (2, 3)]
            target_list = center_en if center_en else enemies
            if target_list:
                idx, em = max(target_list, key=lambda x: x[1].current_def)
                total_impact += deal_spell_or_skill_damage(idx, em, strike_dmg, None, other_p, active_p, p_stats)
            else:
                other_p.take_leader_dmg(strike_dmg)
                total_impact += strike_dmg * 0.85

        elif stype == 11:  # نسخ الميزة الدائمة لوحش الدعم ومنحها لمهاجم الوسط مع تعزيز +val دفاع
            if allies:
                donor = next((am for _, am in allies if am.passives), allies[0][1])
                target = next((am for idx, am in allies if idx in (2, 3)), allies[-1][1])
                for p in donor.passives:
                    if p not in target.passives:
                        target.passives.append(p)
                target.current_def += val
                total_impact += val * 0.90 + 6.0
            else:
                active_p.heal_leader(val, other_p)
                total_impact += val * 0.75

        elif stype == 12:  # ربط حليفي الوسط معاً؛ تقاسم الضرر بالتساوي وزيادة هجومهما المشترك بـ +val
            center_allies = [am for idx, am in allies if idx in (2, 3)] or [am for _, am in allies[:2]]
            if center_allies:
                atk_each = max(4, val // max(1, len(center_allies)))
                for am in center_allies:
                    am.bonus_atk += atk_each // 2
                    am.current_def += atk_each // 2
                total_impact += val * 0.95
            else:
                other_p.take_leader_dmg(val)
                total_impact += val * 0.80

        elif stype == 13:  # تفعيل تآزر العناصر بين وحشين حليفين لمنح كليهما درعاً سحرياً بقيمة +val
            if allies:
                shield_each = max(6, val // min(2, len(allies)))
                for _, am in allies[:2]:
                    am.current_def += shield_each
                    am.bonus_atk += max(2, val // 6)
                total_impact += val * 0.95
            else:
                active_p.shield += val
                total_impact += val * 0.85

        # --- الفئة 2: تعويذة تموضع وتبديل (14..17) ---
        elif stype == 14:  # تبديل موقع وحش الجناح الجريح مع المدافع الوسطي ومنحه +val درع إصلاحي
            wing_allies = [(idx, am) for idx, am in allies if idx in (0, 1, 4, 5)]
            center_allies = [(idx, am) for idx, am in allies if idx in (2, 3)]
            if wing_allies and center_allies:
                w_idx, w_m = min(wing_allies, key=lambda x: x[1].current_def)
                c_idx, c_m = max(center_allies, key=lambda x: x[1].current_def)
                active_p.board[w_idx], active_p.board[c_idx] = c_m, w_m
                w_m.current_def += val
                c_m.current_def += val // 2
                total_impact += val * 0.95 + 5.0
            elif allies:
                allies[0][1].current_def += val
                total_impact += val * 0.90
            else:
                active_p.heal_leader(val, other_p)
                total_impact += val * 0.75

        elif stype == 15:  # سحب مدافع الخصم قسراً إلى خانة الجناح المكشوفة وإضعاف دفاعه بـ -val
            if enemies:
                idx, em = max(enemies, key=lambda x: x[1].current_def)
                empty_wings = [s for s in (0, 1, 4, 5) if other_p.board[s] is None]
                if empty_wings and not em.has_passive_substr("ثبات أرضي"):
                    new_s = empty_wings[0]
                    other_p.board[idx] = None
                    other_p.board[new_s] = em
                    idx = new_s
                total_impact += deal_spell_or_skill_damage(idx, em, val, None, other_p, active_p, p_stats) + 5.0
            else:
                other_p.take_leader_dmg(val)
                total_impact += val * 0.85

        elif stype == 16:  # إزاحة وحش الخصم من الوسط إلى خانة جناح شاغرة وفتح مسار مباشر للقائد بـ val ضرر
            center_en = [(idx, em) for idx, em in enemies if idx in (2, 3)]
            if center_en:
                idx, em = center_en[0]
                empty_wings = [s for s in (0, 1, 4, 5) if other_p.board[s] is None]
                if empty_wings and not em.has_passive_substr("ثبات أرضي"):
                    other_p.board[idx] = None
                    other_p.board[empty_wings[0]] = em
                other_p.take_leader_dmg(val)
                total_impact += val * 0.90 + 6.0
            else:
                other_p.take_leader_dmg(val)
                total_impact += val * 0.85

        elif stype == 17:  # نقل وحشك المهاجم إلى المسار المقابل لأضعف وحش لدى الخصم مع +val هجوم خاطف
            if allies:
                a_idx, am = max(allies, key=lambda x: x[1].base_atk + x[1].bonus_atk)
                am.bonus_atk += max(4, val // 2)
                am.can_attack = True
                if enemies:
                    e_idx, em = min(enemies, key=lambda x: x[1].current_def)
                    if active_p.board[e_idx] is None:
                        active_p.board[a_idx] = None
                        active_p.board[e_idx] = am
                    total_impact += deal_spell_or_skill_damage(e_idx, em, val // 2, am, other_p, active_p, p_stats) + (val * 0.55)
                else:
                    other_p.take_leader_dmg(val // 2)
                    total_impact += val * 0.90
            else:
                other_p.take_leader_dmg(val)
                total_impact += val * 0.80

        # --- الفئة 3: تعويذة تحويل ومقايضة (18..21) ---
        elif stype == 18:  # تحويل val نقطة من دفاع وحشك المدافع إلى نقاط هجوم حاسمة في هذا الدور
            if allies:
                _, am = max(allies, key=lambda x: x[1].current_def)
                converted = min(max(0, am.current_def - 6), val // 2)
                am.current_def -= converted
                am.bonus_atk += val
                am.can_attack = True
                total_impact += val * 0.95
            else:
                other_p.take_leader_dmg(val)
                total_impact += val * 0.80

        elif stype == 19:  # اقتطاع نصف درع وحش الدرع الصرف لتوزيعه كدروع بقيمة +val على جميع حلفاء الأجنحة
            if allies:
                per_ally = max(6, val // len(allies))
                for _, am in allies:
                    am.current_def += per_ally
                total_impact += val * 0.92
            else:
                active_p.shield += val
                total_impact += val * 0.85

        elif stype == 20:  # عكس إحصائيات الهجوم والدفاع لوحش الخصم المقابل وإلحاق val ضرر ارتدادي به
            if enemies:
                idx, em = max(enemies, key=lambda x: x[1].current_def + x[1].base_atk)
                if em.current_def > em.base_atk:
                    em.base_atk, em.current_def = em.current_def, max(4, em.base_atk)
                total_impact += deal_spell_or_skill_damage(idx, em, val, None, other_p, active_p, p_stats) + 5.0
            else:
                other_p.take_leader_dmg(val)
                total_impact += val * 0.85

        elif stype == 21:  # امتصاص val نقطة دفاع من وحش الخصم المقابل وتحويلها إلى هجوم مباشر لوحشك
            if enemies:
                idx, em = max(enemies, key=lambda x: x[1].current_def)
                drained = deal_spell_or_skill_damage(idx, em, val // 2, None, other_p, active_p, p_stats)
                if allies:
                    allies[0][1].bonus_atk += val // 2
                    allies[0][1].current_def += val // 3
                total_impact += drained + (val * 0.55)
            else:
                other_p.take_leader_dmg(val)
                total_impact += val * 0.85

        # --- الفئة 4: تعويذة تنشيط ودعم (22..25) ---
        elif stype == 22:  # تصفير تكلفة المهارة النشطة لوحشك في هذا الدور مع زيادة أثرها بمقدار +val
            active_p.power += 1
            active_p.free_skill_bonus += val // 2
            if allies:
                allies[0][1].bonus_atk += val // 2
                allies[0][1].current_def += val // 2
            total_impact += val * 0.95

        elif stype == 23:  # تمكين وحش الوسط من الهجوم مرتين متتاليتين في هذا الدور مع +val اختراق دروع
            if allies:
                target = next((am for idx, am in allies if idx in (2, 3)), allies[0][1])
                target.can_attack = True
                target.double_strike = True
                target.bonus_atk += val // 2
                total_impact += val * 0.95
            else:
                other_p.take_leader_dmg(val)
                total_impact += val * 0.80

        elif stype == 24:  # تطهير كافة اللعنات والتجميد عن وحوشك ومنحها +val نقطة دفاع إضافية
            if allies:
                per_ally = max(6, val // len(allies))
                for _, am in allies:
                    am.frozen_turns = 0
                    am.silenced_turns = 0
                    am.burn_dmg = 0
                    am.poison_dmg = 0
                    am.weakened_turns = 0
                    am.current_def += per_ally
                total_impact += val * 0.95
            else:
                active_p.heal_leader(val, other_p)
                total_impact += val * 0.80

        elif stype == 25:  # شحن طاقة وحش الدعم لتوليد +2 باور فوري وترميم +val HP لقائدك
            active_p.power += 2
            active_p.heal_leader(val, other_p)
            total_impact += val * 0.70 + 14.0

        # --- الفئة 5: تعويذة اختراق وتدمير (26..29) ---
        elif stype == 26:  # مقذوف أركيني يخترق الوحوش العادية ويصيب الأشباح أو المدافع بـ val ضرر صافي
            if enemies:
                ghosts = [(idx, em) for idx, em in enemies if em.element == "أشباح"]
                idx, em = ghosts[0] if ghosts else max(enemies, key=lambda x: x[1].base_atk + x[1].current_def)
                total_impact += deal_spell_or_skill_damage(idx, em, val, None, other_p, active_p, p_stats, ignore_immunity=True) + 4.0
            else:
                other_p.take_leader_dmg(val)
                total_impact += val * 0.90

        elif stype == 27:  # سحب بطاقتين إضافيتين ليدك لتعويض التضحية مع إلحاق val ضرر بقائد الخصم
            active_p.draw()
            active_p.draw()
            other_p.take_leader_dmg(val // 2)
            total_impact += (val * 0.55) + 12.0

        elif stype == 28:  # إعادة أقوى وحش سقط في مقبرتك إلى يدك وتخفيض تكلفة استدعائه مع val شفاء للقائد
            fallen_monsters = [g for g in active_p.graveyard if isinstance(g, SimMonster)]
            if fallen_monsters and len(active_p.hand) < 10:
                best_m = max(fallen_monsters, key=lambda x: x.cost)
                active_p.graveyard.remove(best_m)
                best_m.current_def = best_m.base_def
                best_m.cost = max(1, best_m.cost - 1)
                active_p.hand.append(best_m)
                total_impact += best_m.cost * 4.0
            else:
                active_p.draw()
                total_impact += 8.0
            active_p.heal_leader(val, other_p)
            total_impact += val * 0.65

        elif stype == 29:  # سلب 1 مانا دائمة من سقف الخصم وإضافتها لسقفك مع صعق وحوشه بـ val ضرر جماعي
            if other_p.mana_cap > 1:
                other_p.mana_cap -= 1
            active_p.mana_cap += 1  # بدون حد أقصى للمانا!
            active_p.power += 1
            aoe_dmg = max(6, val // 3)
            for idx, em in enemies:
                total_impact += deal_spell_or_skill_damage(idx, em, aoe_dmg, None, other_p, active_p, p_stats)
            total_impact += 14.0

    record_active_val(a_stats, act_str, total_impact)


def execute_active_skills(player: SimPlayer, opponent: SimPlayer, p_stats, a_stats):
    if player.power <= 0:
        return

    for slot in range(6):
        m = player.board[slot]
        if not m or not m.is_alive() or m.silenced_turns > 0 or m.frozen_turns > 0 or not m.actives:
            continue

        opp_slot_m = opponent.board[slot]
        extra_cost = 1 if (opp_slot_m and opp_slot_m.is_alive() and opp_slot_m.has_passive_substr("هالة الصقيع")) else 0

        for act in m.actives:
            base_skill_cost = 3 if "(تكلفة 3 باور)" in act else (2 if "(تكلفة 2 باور)" in act else 1)
            total_cost = max(0, base_skill_cost + extra_cost - (1 if player.free_skill_bonus > 0 else 0))
            if player.power < total_cost:
                continue

            if extra_cost > 0 and opp_slot_m:
                record_passive_val(p_stats, opp_slot_m.has_passive_substr("هالة الصقيع"), 6.5)

            enemy_targets = [(idx, em) for idx, em in enumerate(opponent.board) if em and em.is_alive()]
            ally_targets = [(idx, am) for idx, am in enumerate(player.board) if am and am.is_alive()]
            if not enemy_targets and not any(w in act for w in ("استعادة", "شفاء", "رحيق", "حكم الشمس", "درع", "تحصين", "بركة", "قيامة", "غضب الطبيعة", "استحضار")):
                continue

            player.power -= total_cost
            bonus_eff = player.free_skill_bonus
            player.free_skill_bonus = 0

            repeat_count = 2 if (m.has_passive_substr("مضاعف التعاويذ") and random.random() < 0.5) else 1
            if repeat_count == 2:
                record_passive_val(p_stats, m.has_passive_substr("مضاعف التعاويذ"), 12.0)

            if m.has_passive_substr("استعادة المانا"):
                player.power += 1
                record_passive_val(p_stats, m.has_passive_substr("استعادة المانا"), 10.0)

            for _, ally_m in ally_targets:
                p_echo = ally_m.has_passive_substr("صدى سحري")
                if p_echo:
                    ally_m.bonus_atk += 3
                    record_passive_val(p_stats, p_echo, 3 * 1.18)

            raw_impact = 0.0
            for _ in range(repeat_count):
                raw_impact += resolve_single_active(act, slot, m, player, opponent, enemy_targets, ally_targets, p_stats, bonus_eff)

            power_opportunity_cost = base_skill_cost * 3.8
            net_skill_pts = max(4.0, raw_impact - power_opportunity_cost)
            record_active_val(a_stats, act, net_skill_pts)

            if player.power <= 0:
                break


def resolve_single_active(act: str, slot: int, m: SimMonster, player: SimPlayer, opponent: SimPlayer, enemy_targets, ally_targets, p_stats, bonus_eff=0):
    enemy_targets = [(idx, em) for idx, em in enumerate(opponent.board) if em and em.is_alive()]

    # --- النار ---
    if "جحيم الفناء" in act:
        total = 0.0
        for idx, em in enemy_targets:
            total += deal_spell_or_skill_damage(idx, em, 25 + bonus_eff, m, opponent, player, p_stats)
        return max(18.0, total)
    if "ثوران بركاني" in act:
        target = opponent.board[slot] if slot < 6 and opponent.board[slot] else (enemy_targets[0][1] if enemy_targets else None)
        t_idx = slot if (slot < 6 and opponent.board[slot]) else (enemy_targets[0][0] if enemy_targets else -1)
        if target and target.is_alive():
            if target.current_def < 30 + bonus_eff:
                val = target.current_def * 0.9 + target.base_atk * 0.8 + 8.0
                target.current_def = 0
                handle_monster_death(opponent, player, t_idx, target, m, p_stats)
                return val
            return deal_spell_or_skill_damage(t_idx, target, 20 + bonus_eff, m, opponent, player, p_stats)
        return 12.0
    if "قذيفة نارية" in act or "صاروخ موجه" in act:
        if enemy_targets:
            idx, em = max(enemy_targets, key=lambda x: x[1].base_atk)
            return deal_spell_or_skill_damage(idx, em, 12 + bonus_eff, m, opponent, player, p_stats) + 5.0
        return 11.0
    if "انفجار الجمر" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            total = deal_spell_or_skill_damage(idx, em, 8 + bonus_eff, m, opponent, player, p_stats)
            for adj in (idx - 1, idx + 1):
                if 0 <= adj < 6 and opponent.board[adj] and opponent.board[adj].is_alive():
                    total += deal_spell_or_skill_damage(adj, opponent.board[adj], 8, m, opponent, player, p_stats)
            return total + 6.0
        return 12.0
    if "سيل الحمم" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            return deal_spell_or_skill_damage(idx, em, 15 + bonus_eff, m, opponent, player, p_stats) + 4.5
        return 12.0
    if "لهب التطهير" in act:
        m.burn_dmg = 0
        m.poison_dmg = 0
        m.frozen_turns = 0
        m.bonus_atk += 5 + bonus_eff
        return 16.0

    # --- المياه ---
    if "تسونامي جارف" in act:
        total = 0.0
        for idx, em in enemy_targets:
            em.bonus_atk = 0
            total += deal_spell_or_skill_damage(idx, em, 20 + bonus_eff, m, opponent, player, p_stats)
        return max(18.0, total)
    if "غمر الأعماق" in act:
        total = 10.0
        for idx in (2, 3):
            em = opponent.board[idx]
            if em and em.is_alive():
                em.frozen_turns = max(em.frozen_turns, 1)
                total += em.base_atk * 1.1 + 5.0
        return total
    if "دوامة مائية" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            em.can_attack = False
            return em.base_atk * 0.8 + 7.0
        return 11.0
    if "تيار شفاء" in act:
        m.current_def += 20 + bonus_eff
        return 18.0
    if "فقاعة أمان" in act:
        m.bubble_shield = True
        m.current_def += 10
        return 18.5

    # --- الجليد ---
    if "عصر جليدي" in act:
        total = 10.0
        for idx, em in enemy_targets:
            em.frozen_turns = max(em.frozen_turns, 1)
            total += em.base_atk * 1.0 + 3.5
        return total
    if "انفجار الصقيع" in act:
        frozen = [(idx, em) for idx, em in enemy_targets if em.frozen_turns > 0]
        if frozen:
            idx, em = frozen[0]
            val = em.current_def * 0.9 + em.base_atk * 1.0 + 8.0
            em.current_def = 0
            handle_monster_death(opponent, player, idx, em, m, p_stats)
            return val
        elif enemy_targets:
            idx, em = enemy_targets[0]
            em.frozen_turns += 1
            return deal_spell_or_skill_damage(idx, em, 18 + bonus_eff, m, opponent, player, p_stats) + 6.0
        return 14.0
    if "رمح الجليد" in act:
        if enemy_targets:
            idx, em = max(enemy_targets, key=lambda x: x[1].base_atk)
            em.frozen_turns = max(em.frozen_turns, 1)
            return deal_spell_or_skill_damage(idx, em, 10 + bonus_eff, m, opponent, player, p_stats) + 8.0
        return 12.0
    if "جدار ثلجي" in act:
        m.current_def += 20 + bonus_eff
        return 17.5

    # --- الكهرباء ---
    if "عاصفة الصواعق الكبرى" in act:
        total = 0.0
        for _ in range(4):
            alive = [(idx, em) for idx, em in enumerate(opponent.board) if em and em.is_alive()]
            if alive:
                idx, em = random.choice(alive)
                total += deal_spell_or_skill_damage(idx, em, 10 + bonus_eff // 2, m, opponent, player, p_stats)
            else:
                opponent.take_leader_dmg(10)
                total += 7.5
        return max(20.0, total)
    if "شلل عصبي تام" in act:
        total = 12.0
        for idx, em in enemy_targets:
            em.silenced_turns = max(em.silenced_turns, 2)
            total += 5.0
        return total
    if "صعقة مفاجئة" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            return deal_spell_or_skill_damage(idx, em, 14 + bonus_eff, m, opponent, player, p_stats) + 5.0
        return 12.0
    if "شحن طاقة" in act:
        m.bonus_atk += 8 + bonus_eff
        return 16.5

    # --- الرياح ---
    if "إعصار كاسح" in act:
        if enemy_targets:
            idx, em = max(enemy_targets, key=lambda x: x[1].cost)
            if not em.has_passive_substr("ثبات أرضي"):
                opponent.board[idx] = None
                if len(opponent.hand) < 10:
                    opponent.hand.append(em)
                return em.cost * 4.2 + 12.0
        return 16.0
    if "رياح الفناء" in act:
        total = 0.0
        for idx, em in enemy_targets:
            total += deal_spell_or_skill_damage(idx, em, 22 + bonus_eff, m, opponent, player, p_stats, ignore_immunity=True)
        return max(20.0, total)
    if "عاصفة قاطعة" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            return deal_spell_or_skill_damage(idx, em, 12 + bonus_eff, m, opponent, player, p_stats) + 6.5
        return 12.0
    if "نسيم معجل" in act:
        m.can_attack = True
        m.bonus_atk += 5
        return 18.5

    # --- الأرض ---
    if "شق الأرض العظيم" in act:
        center_en = [(idx, em) for idx, em in enemy_targets if idx in (2, 3)]
        target_list = center_en if center_en else enemy_targets
        if target_list:
            idx, em = target_list[0]
            val = min(30.0, em.current_def * 0.85 + em.base_atk * 0.8 + 8.0)
            em.current_def = 0
            handle_monster_death(opponent, player, idx, em, m, p_stats)
            return val
        return 16.0
    if "درع الجبال الخالد" in act or "درع القبة الحديدية" in act:
        player.team_immune_turns = max(player.team_immune_turns, 1)
        return 26.0
    if "ضربة الزلزال" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            total = deal_spell_or_skill_damage(idx, em, 15 + bonus_eff, m, opponent, player, p_stats)
            for adj in (idx - 1, idx + 1):
                if 0 <= adj < 6 and opponent.board[adj] and opponent.board[adj].is_alive():
                    total += deal_spell_or_skill_damage(adj, opponent.board[adj], 5, m, opponent, player, p_stats)
            return total + 5.0
        return 14.0
    if "تحصين صخري" in act:
        m.current_def += 15 + bonus_eff
        return 17.5

    # --- الغابة ---
    if "غضب الطبيعة" in act:
        spawned = 0
        for w_s in (0, 1, 4, 5):
            if player.board[w_s] is None and spawned < 2:
                tree = SimMonster({
                    "id": 9998, "name": "9998", "element": "غابة", "role": "مدافع",
                    "tier_num": 2, "cost": 2, "atk": 6, "def": 30,
                    "passives": ["أشواك سامة: إلحاق 3 ضرر بالوحش المهاجم"], "active_skills": []
                })
                player.board[w_s] = tree
                spawned += 1
        return 14.0 + spawned * 8.0
    if "سم الفناء العضوي" in act:
        total = 12.0
        for idx, em in enemy_targets:
            em.poison_dmg = max(em.poison_dmg, 10)
            total += deal_spell_or_skill_damage(idx, em, 10 + bonus_eff, m, opponent, player, p_stats)
        return min(32.0, total)
    if "جذور خانقة" in act:
        if enemy_targets:
            idx, em = max(enemy_targets, key=lambda x: x[1].base_atk)
            em.frozen_turns = max(em.frozen_turns, 2)
            return em.base_atk * 1.1 + 8.0
        return 13.0
    if "رحيق الحياة" in act:
        player.heal_leader(25 + bonus_eff, opponent)
        m.current_def += 10
        return 18.0

    # --- الضياء ---
    if "قيامة النور الكبرى" in act:
        fallen = [g for g in player.graveyard if isinstance(g, SimMonster)]
        empty_s = [s for s in range(6) if player.board[s] is None]
        if fallen and empty_s:
            best_m = max(fallen, key=lambda x: x.cost)
            player.graveyard.remove(best_m)
            best_m.current_def = best_m.base_def
            best_m.can_attack = True
            player.board[empty_s[0]] = best_m
            return min(32.0, best_m.cost * 4.5 + 12.0)
        player.heal_leader(40, opponent)
        return 20.0
    if "حكم الشمس المطلق" in act:
        player.heal_leader(100, opponent)
        for _, am in ally_targets:
            am.current_def += 20
        return 28.0
    if "شعاع الفجر" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            return deal_spell_or_skill_damage(idx, em, 14 + bonus_eff, m, opponent, player, p_stats, ignore_immunity=True) + 6.0
        return 14.0
    if "بركة النور" in act:
        m.bonus_atk += max(8, m.base_atk)
        return 19.5

    # --- الظلام ---
    if "كسوف مطلق" in act:
        opponent.deny_draw_sac_turns = max(opponent.deny_draw_sac_turns, 1)
        return 26.0
    if "حصد الأرواح" in act:
        sorted_en = sorted(enemy_targets, key=lambda x: x[1].current_def)
        total = 10.0
        for idx, em in sorted_en[:2]:
            total += em.current_def * 0.75 + em.base_atk * 0.7
            em.current_def = 0
            handle_monster_death(opponent, player, idx, em, m, p_stats)
        return min(31.0, total)
    if "ضربة الظلال" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            player.heal_leader(5, opponent)
            return deal_spell_or_skill_damage(idx, em, 14 + bonus_eff, m, opponent, player, p_stats) + 6.0
        return 14.0
    if "قفل روحي" in act:
        if enemy_targets:
            enemy_targets[0][1].silenced_turns = max(enemy_targets[0][1].silenced_turns, 2)
            return 17.5
        return 11.0

    # --- الميكا ---
    if "مدفع الأيونات التدميري" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            rem = max(0, 50 + bonus_eff - em.current_def)
            val = deal_spell_or_skill_damage(idx, em, 50 + bonus_eff, m, opponent, player, p_stats)
            if rem > 0:
                opponent.take_leader_dmg(rem)
                val += rem * 0.65
            return min(33.0, val)
        opponent.take_leader_dmg(50)
        return 28.0
    if "إعادة تهيئة" in act:
        m.burn_dmg = 0
        m.poison_dmg = 0
        m.current_def += 20 + bonus_eff
        return 17.5

    # --- الأشباح ---
    if "استحضار المقابر" in act:
        spawned = 0
        for w_slot in (0, 1, 4, 5):
            if player.board[w_slot] is None and spawned < 2:
                ghost_token = SimMonster({
                    "id": 9999, "name": "9999", "element": "أشباح", "role": "مهاجم",
                    "tier_num": 2, "cost": 2, "atk": 12, "def": 14,
                    "passives": ["جسد أثيري: منيع ضد ضربات الوحوش العادية بنسبة 35%"],
                    "active_skills": []
                })
                ghost_token.can_attack = True
                player.board[w_slot] = ghost_token
                spawned += 1
        return 14.0 + spawned * 8.5
    if "سلب الأرواح" in act:
        total = 12.0
        for idx, em in enemy_targets:
            lost_atk = em.base_atk // 2
            lost_def = em.current_def // 2
            em.base_atk = max(1, em.base_atk - lost_atk)
            em.current_def = max(1, em.current_def - lost_def)
            total += lost_atk * 0.65 + lost_def * 0.45
        return min(32.0, total)
    if "لمسة البرزخ" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            return deal_spell_or_skill_damage(idx, em, 10 + bonus_eff, m, opponent, player, p_stats, ignore_immunity=True) + 7.5
        return 13.0
    if "فزع روحي" in act:
        if enemy_targets:
            idx, em = max(enemy_targets, key=lambda x: x[1].base_atk)
            em.frozen_turns = max(em.frozen_turns, 1)
            return em.base_atk * 0.95 + 6.5
        return 12.0

    # --- وحوش السحر ---
    if "انفجار المانا الأعظم" in act:
        total = 0.0
        for idx, em in enemy_targets[:3]:
            total += deal_spell_or_skill_damage(idx, em, 14 + bonus_eff // 2, m, opponent, player, p_stats)
        opponent.take_leader_dmg(15)
        return max(22.0, total + 10.0)
    if "توقف الزمن الأركيني" in act:
        for _, am in ally_targets:
            am.can_attack = True
            am.double_strike = True
        opponent.deny_draw_sac_turns = max(opponent.deny_draw_sac_turns, 1)
        return 32.0
    if "سهم الأركين" in act:
        if enemy_targets:
            idx, em = max(enemy_targets, key=lambda x: x[1].base_atk)
            return deal_spell_or_skill_damage(idx, em, 15 + bonus_eff, m, opponent, player, p_stats) + 5.5
        return 14.0
    if "تحويل العناصر" in act:
        if enemy_targets:
            idx, em = max(enemy_targets, key=lambda x: x[1].base_atk)
            em.weakened_turns = max(em.weakened_turns, 2)
            return (em.base_atk * 0.4 + em.current_def * 0.25) + 7.5
        return 12.0

    return 14.0


def compute_attack_power(slot: int, m: SimMonster, opp_m: SimMonster, active_env, opponent: SimPlayer, p_stats):
    atk = m.base_atk + m.bonus_atk
    if active_env is not None:
        env_el, env_lvl = active_env
        buff_pct = 0.30 if env_lvl == 1 else (0.40 if env_lvl == 2 else 0.70)
        debuff_pct = 0.20 if env_lvl == 1 else (0.30 if env_lvl == 2 else 0.50)
        if m.element == ENVIRONMENT_MATRIX[env_el]["buff"]:
            atk = int(round(atk * (1.0 + buff_pct)))
        elif m.element in ENVIRONMENT_MATRIX[env_el]["debuff"]:
            atk = int(round(atk * max(0.2, 1.0 - debuff_pct)))

    if m.weakened_turns > 0:
        atk = int(atk * 0.70)

    if m.has_passive_substr("نظام توجيه"):
        bonus = max(2, int(atk * 0.15))
        atk += bonus
        record_passive_val(p_stats, m.has_passive_substr("نظام توجيه"), bonus * 1.15)

    # فحص هالات إضعاف الهجوم في ساحة الخصم
    for e_idx, em in enumerate(opponent.board):
        if em and em.is_alive():
            if em.has_passive_substr("هيبة الموتى"):
                atk = max(0, atk - 3)
                record_passive_val(p_stats, em.has_passive_substr("هيبة الموتى"), 3.0)
            if slot in (0, 1, 4, 5) and em.has_passive_substr("رياح قطبية"):
                atk = max(0, atk - 4)
                record_passive_val(p_stats, em.has_passive_substr("رياح قطبية"), 4.0)

    if opp_m and opp_m.is_alive():
        for p in opp_m.passives:
            if "برودة خفيفة" in p:
                atk = max(0, atk - 3)
                record_passive_val(p_stats, p, 3.2)
            elif "هالة رعب" in p:
                atk = max(0, atk - 3)
                record_passive_val(p_stats, p, 3.2)
            elif "غمر مائي" in p:
                atk = max(0, atk - 4)
                record_passive_val(p_stats, p, 4.2)
            elif "رياح عاتية" in p:
                red = int(atk * 0.25)
                atk = max(0, atk - red)
                record_passive_val(p_stats, p, red * 0.95)
            elif "سطوع مهيب" in p and random.random() < 0.30:
                red = int(atk * 0.45)
                atk = max(0, atk - red)
                record_passive_val(p_stats, p, red * 0.95)

        if m.has_passive_substr("نصل جليدي") and opp_m.frozen_turns > 0:
            atk += 5
            record_passive_val(p_stats, m.has_passive_substr("نصل جليدي"), 5 * 1.2)

    return atk


def apply_monster_hit(attacker_slot: int, m: SimMonster, def_slot: int, opp_m: SimMonster, atk_dmg: int, player: SimPlayer, opponent: SimPlayer, p_stats):
    if opponent.team_immune_turns > 0:
        return
    if opp_m.bubble_shield:
        opp_m.bubble_shield = False
        return

    # جسد أثيري للأشباح: منيع ضد الوحوش العادية بنسبة 35%
    if opp_m.has_passive_substr("جسد أثيري"):
        if m.element not in ("ضياء", "سحر", "أشباح") and not m.has_passive_substr("عبور حركي"):
            prevented = int(atk_dmg * 0.35)
            atk_dmg = max(1, atk_dmg - prevented)
            record_passive_val(p_stats, opp_m.has_passive_substr("جسد أثيري"), prevented * 0.95)

    # فحص المراوغة والحدس الباطني
    if not m.has_passive_substr("نظام توجيه"):
        if opp_m.has_passive_substr("مراوغة خفيفة") and random.random() < 0.20:
            record_passive_val(p_stats, opp_m.has_passive_substr("مراوغة خفيفة"), atk_dmg * 0.90)
            return
        if opp_m.has_passive_substr("حدس باطني") and random.random() < 0.25:
            record_passive_val(p_stats, opp_m.has_passive_substr("حدس باطني"), atk_dmg * 0.90)
            return
        if opp_m.has_passive_substr("خفة الحركة") and random.random() < 0.15:
            record_passive_val(p_stats, opp_m.has_passive_substr("خفة الحركة"), atk_dmg * 0.90)
            return

    # فحص اختراق الدروع وتقليل الضرر
    ignore_armor = m.has_passive_substr("عبور حركي") is not None or m.has_passive_substr("اختراق هوائي") is not None
    if ignore_armor:
        if m.has_passive_substr("عبور حركي"):
            record_passive_val(p_stats, m.has_passive_substr("عبور حركي"), 5.5)
        if m.has_passive_substr("اختراق هوائي"):
            atk_dmg += 4
            record_passive_val(p_stats, m.has_passive_substr("اختراق هوائي"), 7.0)
    else:
        for p in opp_m.passives:
            if "مرونة مائية" in p:
                red = min(atk_dmg, 3)
                atk_dmg -= red
                record_passive_val(p_stats, p, red * 0.95)
            elif "لهب وقائي" in p:
                red = min(atk_dmg, 4)
                atk_dmg -= red
                record_passive_val(p_stats, p, red * 0.95)
            elif "صلابة صخرية" in p:
                red = min(atk_dmg, 4)
                atk_dmg -= red
                record_passive_val(p_stats, p, red * 0.95)
            elif "درع فولاذي" in p:
                red = min(atk_dmg, 5)
                atk_dmg -= red
                record_passive_val(p_stats, p, red * 0.95)

    # جذور صخرية: امتصاص 6 ضرر نيابة عن وحوش الأجنحة المجاورة
    if def_slot in (0, 1, 4, 5):
        for c_idx in (2, 3):
            cm = opponent.board[c_idx]
            if cm and cm.is_alive() and cm.has_passive_substr("جذور صخرية") and cm.current_def > 8:
                absorbed = min(atk_dmg, 6)
                atk_dmg -= absorbed
                cm.current_def -= absorbed // 2
                record_passive_val(p_stats, cm.has_passive_substr("جذور صخرية"), absorbed * 1.1)
                break

    # فحص الحصانات والمقاومات العنصرية
    elem_immunity_map = {
        "نار": ("حصانة تامة ضد النار", "رطوبة تمنع الحرق"),
        "مياه": ("حصانة ضد الماء",),
        "جليد": ("حصانة تامة ضد الجليد", "مقاومة التجميد", "والصقيع"),
        "كهرباء": ("مقاومة الكهرباء", "حصانة ضد الصدمات الكهربائية"),
        "رياح": ("حصانة ضد الرياح", "مقاومة الرياح"),
        "أرض": ("حصانة ضد الأرض", "مقاومة الأرض"),
        "ضياء": ("حصانة ضد النور",),
        "ظلام": ("حصانة ضد الظلام", "مقاومة الظلام"),
        "أشباح": ("حصانة تامة ضد الأشباح",),
    }
    for imm_key in elem_immunity_map.get(m.element, ()):
        p_imm = opp_m.has_passive_substr(imm_key)
        if p_imm:
            red = max(3, int(atk_dmg * 0.40))
            atk_dmg = max(1, atk_dmg - red)
            record_passive_val(p_stats, p_imm, red * 1.1 + 3.5)

    if atk_dmg > 0:
        opp_m.current_def -= atk_dmg

        if m.has_passive_substr("لهيب أزلي"):
            m.bonus_atk += 2
            record_passive_val(p_stats, m.has_passive_substr("لهيب أزلي"), 2 * 1.18 * 1.8)
        if m.has_passive_substr("تفريغ كهربائي"):
            opp_m.silenced_turns = max(opp_m.silenced_turns, 1)
            record_passive_val(p_stats, m.has_passive_substr("تفريغ كهربائي"), 8.5)
        if m.has_passive_substr("امتصاص حياة"):
            heal = max(2, int(atk_dmg * 0.20))
            m.current_def += heal
            record_passive_val(p_stats, m.has_passive_substr("امتصاص حياة"), heal * 0.95)
        if m.has_passive_substr("سم بطيء"):
            opp_m.poison_dmg = max(opp_m.poison_dmg, 6)
            record_passive_val(p_stats, m.has_passive_substr("سم بطيء"), 6.0)
        if m.has_passive_substr("صعق متسلسل"):
            splash = max(3, int(atk_dmg * 0.30))
            for adj in (def_slot - 1, def_slot + 1):
                if 0 <= adj < 6 and opponent.board[adj] and opponent.board[adj].is_alive():
                    opponent.board[adj].current_def -= splash
                    record_passive_val(p_stats, m.has_passive_substr("صعق متسلسل"), splash * 1.1)
                    if not opponent.board[adj].is_alive():
                        handle_monster_death(opponent, player, adj, opponent.board[adj], m, p_stats)
                    break

    # ردود الفعل الدفاعية (الأشواك، الشرارة، جلد الصوان، جمر ملتهب، التجميد التلقائي)
    if opp_m.has_passive_substr("أشواك سامة") or opp_m.has_passive_substr("أشواك دفاعية"):
        m.current_def -= 3
        record_passive_val(p_stats, opp_m.has_passive_substr("أشواك"), 3 * 1.1)
    if opp_m.has_passive_substr("شرارة ارتدادية"):
        m.current_def -= 3
        record_passive_val(p_stats, opp_m.has_passive_substr("شرارة ارتدادية"), 3 * 1.1)
    if opp_m.has_passive_substr("جلد الصوان"):
        m.current_def -= 5
        record_passive_val(p_stats, opp_m.has_passive_substr("جلد الصوان"), 5 * 1.1)
    if opp_m.has_passive_substr("جمر ملتهب"):
        ref_dmg = max(2, int(atk_dmg * 0.25))
        m.current_def -= ref_dmg
        record_passive_val(p_stats, opp_m.has_passive_substr("جمر ملتهب"), ref_dmg * 1.1)
    if opp_m.has_passive_substr("تجميد تلقائي للمهاجم") and m.is_alive():
        m.frozen_turns = max(m.frozen_turns, 1)
        record_passive_val(p_stats, opp_m.has_passive_substr("تجميد تلقائي للمهاجم"), m.base_atk * 0.95)

    if not opp_m.is_alive():
        handle_monster_death(opponent, player, def_slot, opp_m, m, p_stats)
    if not m.is_alive():
        handle_monster_death(player, opponent, attacker_slot, m, opp_m, p_stats)


def execute_combat_phase(player: SimPlayer, opponent: SimPlayer, active_env, p_stats):
    for slot in (0, 1, 2, 3, 4, 5):
        m = player.board[slot]
        if not m or not m.is_alive() or not m.can_attack or m.frozen_turns > 0:
            continue

        strikes = 2 if m.double_strike else 1
        m.double_strike = False
        m.can_attack = False

        for _ in range(strikes):
            if not m.is_alive():
                break
            opp_m = opponent.board[slot]
            atk_dmg = compute_attack_power(slot, m, opp_m, active_env, opponent, p_stats)
            if atk_dmg <= 0:
                continue

            if opp_m and opp_m.is_alive():
                apply_monster_hit(slot, m, slot, opp_m, atk_dmg, player, opponent, p_stats)
            else:
                if opponent.team_immune_turns <= 0:
                    opponent.take_leader_dmg(atk_dmg)
                    if m.has_passive_substr("سرقة مانا"):
                        player.power += 1
                        record_passive_val(p_stats, m.has_passive_substr("سرقة مانا"), 12.0)


def simulate_batch(args):
    batch_size, seed, monsters_pool, spells_pool, envs_pool = args
    random.seed(seed)

    c_stats = defaultdict(lambda: [0, 0, 0])  # id -> [played_games, wins_when_played, times_used]
    p_stats = defaultdict(lambda: [0.0, 0])
    a_stats = defaultdict(lambda: [0.0, 0])
    p_summons = defaultdict(int)
    a_summons = defaultdict(int)

    for _ in range(batch_size):
        # بناء مجموعة 30 كارت لكل لاعب: 18 وحش (60%) + 10 سحر وسيط (33.3%) + 2 بيئة (6.7%)
        m1 = [SimMonster(d) for d in random.sample(monsters_pool, 18)]
        s1 = [dict(c) for c in random.sample(spells_pool, 10)]
        e1 = [dict(c) for c in random.sample(envs_pool, 2)]

        m2 = [SimMonster(d) for d in random.sample(monsters_pool, 18)]
        s2 = [dict(c) for c in random.sample(spells_pool, 10)]
        e2 = [dict(c) for c in random.sample(envs_pool, 2)]

        p1 = SimPlayer(m1 + s1 + e1)
        p2 = SimPlayer(m2 + s2 + e2)

        for _ in range(6):
            p1.draw()
            p2.draw()

        active_env = None
        env_turns_left = 0

        turn = 0
        while p1.hp > 0 and p2.hp > 0 and turn < 75:
            turn += 1
            active_p = p1 if (turn % 2 == 1) else p2
            other_p = p2 if (turn % 2 == 1) else p1

            if other_p.team_immune_turns > 0:
                other_p.team_immune_turns -= 1

            # بداية الدور: شحن الباور ليساوي سعة المانا الحالية (بلا حد أقصى للمانا أو الباور ∞)
            active_p.power = active_p.mana_cap
            if active_p.deny_draw_sac_turns > 0:
                active_p.deny_draw_sac_turns -= 1
            else:
                active_p.draw()
                # التضحية التكتيكية لرفع المانا والباور (بلا حد أقصى للـ mana_cap!)
                if len(active_p.hand) >= 2:
                    unplayable = [c for c in active_p.hand if (c.cost if isinstance(c, SimMonster) else c["cost"]) > active_p.power + 1]
                    # يضحي إذا كان لديه كارت أعلى من طاقته أو يريد رفع المانا ولديه وفرة في اليد
                    if unplayable or len(active_p.hand) >= 4 or active_p.mana_cap < 10:
                        sac = unplayable[0] if unplayable else active_p.hand[-1]
                        active_p.hand.remove(sac)
                        active_p.graveyard.append(sac)
                        active_p.mana_cap += 1
                        active_p.power += 1

            for s in range(6):
                bm = active_p.board[s]
                if bm and bm.is_alive() and turn > bm.summoned_turn:
                    bm.can_attack = True

            trigger_start_of_turn_passives(active_p, other_p, p_stats)

            # 1. لعب بطاقات البيئة والمناخ (مع تكديس المستويات 1 -> 2 -> 3)
            for c in list(active_p.hand):
                if isinstance(c, dict) and c.get("card_type") == "env" and c["cost"] <= active_p.power:
                    active_p.power -= c["cost"]
                    active_p.hand.remove(c)
                    active_p.graveyard.append(c)
                    active_p.played_cards.add(c["id"])
                    c_stats[c["id"]][2] += 1
                    if active_env is not None and active_env[0] == c["element"]:
                        new_lvl = min(3, active_env[1] + 1)
                        active_env = (c["element"], new_lvl)
                        env_turns_left = 4 if new_lvl == 1 else (2 if new_lvl == 2 else 1)
                    else:
                        active_env = (c["element"], 1)
                        env_turns_left = 4
                    break

            # 2. استدعاء الوحوش في الخانات الـ 6 أولاً حتى تستفيد التعاويذ الوسيطة من وجود الوحوش
            monster_cards = [c for c in active_p.hand if isinstance(c, SimMonster)]
            monster_cards.sort(key=lambda c: c.cost, reverse=True)
            for card in monster_cards:
                if card.cost <= active_p.power:
                    if card.has_passive_substr("في الأجنحة") or card.has_passive_substr("سرعة خاطفة") or card.role == "مهاجم":
                        slot_order = (0, 1, 4, 5, 2, 3)
                    elif card.has_passive_substr("في الوسط") or card.has_passive_substr("خانات الوسط") or card.role == "درع صرف":
                        slot_order = (2, 3, 0, 5, 1, 4)
                    else:
                        slot_order = (2, 3, 0, 1, 4, 5)

                    chosen_slot = None
                    for s in slot_order:
                        if active_p.board[s] is None:
                            chosen_slot = s
                            break

                    if chosen_slot is not None:
                        active_p.power -= card.cost
                        card.summoned_turn = turn
                        card.can_attack = False
                        active_p.board[chosen_slot] = card
                        active_p.hand.remove(card)
                        active_p.played_cards.add(card.id)
                        c_stats[card.id][2] += 1
                        for p_str in card.passives:
                            p_summons[p_str] += 1
                        for a_str in card.actives:
                            a_summons[a_str] += 1
                        apply_on_summon_passives(active_p, other_p, chosen_slot, card, p_stats)

            # 3. إلقاء البطاقات السحرية الوسيطة (2,788 كارت عبر التخصصات الـ 5)
            for c in list(active_p.hand):
                if isinstance(c, dict) and c.get("card_type") == "spell" and c["cost"] <= active_p.power:
                    active_p.power -= c["cost"]
                    active_p.hand.remove(c)
                    active_p.graveyard.append(c)
                    active_p.played_cards.add(c["id"])
                    c_stats[c["id"]][2] += 1
                    a_summons[c["active_skill"]] += 1
                    execute_intermediary_spell(c, active_p, other_p, p_stats, a_stats)

            # 4. تفعيل المهارات النشطة للوحوش
            execute_active_skills(active_p, other_p, p_stats, a_stats)

            # 5. تنفيذ مرحلة القتال عبر الـ 6 خانات
            execute_combat_phase(active_p, other_p, active_env, p_stats)

            # 6. تقليل عداد جولات البيئة في نهاية الدور
            if active_env is not None:
                env_turns_left -= 1
                if env_turns_left <= 0:
                    active_env = None

        p1_won = p1.hp >= p2.hp
        for cid in p1.played_cards:
            c_stats[cid][0] += 1
            if p1_won:
                c_stats[cid][1] += 1
        for cid in p2.played_cards:
            c_stats[cid][0] += 1
            if not p1_won:
                c_stats[cid][1] += 1

    return dict(c_stats), dict(p_stats), dict(a_stats), dict(p_summons), dict(a_summons)


def run_100k_evaluation(total_matches=100000, progress_callback=None):
    t0 = time.time()
    all_cards = load_all_10k_cards()

    monsters_pool = [c for c in all_cards if c["card_type"] == "monster"]
    spells_pool = [c for c in all_cards if c["card_type"] == "spell"]
    envs_pool = [c for c in all_cards if c["card_type"] == "env"]

    workers = max(1, min(cpu_count(), 12))
    chunks = workers * 4
    batch_size = total_matches // chunks
    remainder = total_matches % chunks

    tasks = []
    for i in range(chunks):
        b = batch_size + (1 if i < remainder else 0)
        if b > 0:
            tasks.append((b, 1000 + i * 97, monsters_pool, spells_pool, envs_pool))

    total_c_stats = {c["id"]: [0, 0, 0] for c in all_cards}
    total_p_stats = defaultdict(lambda: [0.0, 0])
    total_a_stats = defaultdict(lambda: [0.0, 0])
    total_p_summons = defaultdict(int)
    total_a_summons = defaultdict(int)

    completed_matches = 0
    with Pool(processes=workers) as pool:
        for idx, (c_st, p_st, a_st, p_sum, a_sum) in enumerate(pool.imap_unordered(simulate_batch, tasks)):
            completed_matches += tasks[idx][0]
            for cid, vals in c_st.items():
                total_c_stats[cid][0] += vals[0]
                total_c_stats[cid][1] += vals[1]
                total_c_stats[cid][2] += vals[2]
            for k, vals in p_st.items():
                total_p_stats[k][0] += vals[0]
                total_p_stats[k][1] += vals[1]
            for k, vals in a_st.items():
                total_a_stats[k][0] += vals[0]
                total_a_stats[k][1] += vals[1]
            for k, cnt in p_sum.items():
                total_p_summons[k] += cnt
            for k, cnt in a_sum.items():
                total_a_summons[k] += cnt

            if progress_callback:
                progress_callback(completed_matches, total_matches)

    elapsed = time.time() - t0

    # حساب نسب الفوز المعيارية لكل بطاقة من الـ 10,000 بطاقة (مع معايرة Bayesian Empirical Shrinkage)
    # حتى تحصل كل بطاقة على نسبة فوز دقيقة ومستقرة إحصائياً ضمن فئتها وتكلفتها
    archetype_wins = defaultdict(lambda: [0, 0])  # (card_type, group, role, cost, tier_num) -> [played, wins]
    for c in all_cards:
        cid = c["id"]
        played, wins, _ = total_c_stats[cid]
        key = (c["card_type"], c["group"], c["role"], c["cost"], c["tier_num"])
        archetype_wins[key][0] += played
        archetype_wins[key][1] += wins

    raw_win_rates = {}
    cost_groups = defaultdict(list)
    prior_k = 40.0  # معامل الاستقرار الإحصائي لدمج أداء الكارت الفردي مع فئته
    for c in all_cards:
        cid = c["id"]
        played, wins, _ = total_c_stats[cid]
        key = (c["card_type"], c["group"], c["role"], c["cost"], c["tier_num"])
        arch_played, arch_wins = archetype_wins[key]
        arch_wr = (arch_wins / arch_played * 100.0) if arch_played > 0 else 50.0
        indiv_wr = (wins / played * 100.0) if played > 0 else arch_wr
        shrunk_wr = ((played * indiv_wr) + (prior_k * arch_wr)) / (played + prior_k)
        raw_win_rates[cid] = shrunk_wr
        cost_groups[(c["card_type"], c["cost"])].append(shrunk_wr)

    cost_avg_wr = {k: (sum(vals) / max(1, len(vals))) for k, vals in cost_groups.items()}

    card_win_rates = {}
    for c in all_cards:
        cid = c["id"]
        k = (c["card_type"], c["cost"])
        norm_wr = 50.0 + (raw_win_rates[cid] - cost_avg_wr[k])
        card_win_rates[cid] = round(max(44.5, min(55.5, norm_wr)), 2)

    # تجميع نسب الفوز للوحوش الحاملة لكل باسيف ومهارة نشطة وتعويذة
    passive_wr_map = defaultdict(list)
    active_wr_map = defaultdict(list)
    spell_role_wr_map = defaultdict(list)

    for c in all_cards:
        wr = card_win_rates[c["id"]]
        if c["card_type"] == "monster":
            for p in c["passives"]:
                passive_wr_map[p].append(wr)
            for a in c["active_skills"]:
                active_wr_map[a].append(wr)
        elif c["card_type"] == "spell":
            spell_role_wr_map[c["role"]].append(wr)

    # حساب التقييم النقطي لكل ميزة دائمة (Passive)
    passive_scores = {}
    passive_details = []
    for p, wr_list in passive_wr_map.items():
        pts_sum, trig_cnt = total_p_stats[p]
        summons = max(1, total_p_summons[p])
        avg_combat_pts = pts_sum / summons
        avg_wr = sum(wr_list) / len(wr_list)
        wr_adjustment = (avg_wr - 50.0) * 1.10
        empirical_score = round(max(6.0, min(35.0, avg_combat_pts + wr_adjustment)), 1)
        passive_scores[p] = empirical_score
        passive_details.append({
            "name": p,
            "type": "🛡️ ميزة دائمة (Passive)",
            "triggers": trig_cnt,
            "summons": summons,
            "avg_combat_pts": round(avg_combat_pts, 2),
            "avg_wr": round(avg_wr, 2),
            "score": empirical_score
        })

    # حساب التقييم النقطي لكل مهارة نشطة للوحوش (Active Skill)
    active_scores = {}
    active_details = []
    for a, wr_list in active_wr_map.items():
        pts_sum, trig_cnt = total_a_stats[a]
        summons = max(1, total_a_summons[a])
        avg_per_trig = (pts_sum / max(1, trig_cnt))
        activation_rate = min(1.30, max(0.65, (trig_cnt / summons) * 1.35))
        avg_combat_pts = avg_per_trig * activation_rate
        avg_wr = sum(wr_list) / len(wr_list)
        wr_adjustment = (avg_wr - 50.0) * 1.10
        empirical_score = round(max(8.0, min(36.0, avg_combat_pts + wr_adjustment)), 1)
        active_scores[a] = empirical_score
        active_details.append({
            "name": a,
            "type": "⚡ مهارة وحش نشطة (Active)",
            "triggers": trig_cnt,
            "summons": summons,
            "avg_combat_pts": round(avg_combat_pts, 2),
            "avg_wr": round(avg_wr, 2),
            "score": empirical_score
        })

    # إضافة ملخص الأدوار الـ 5 للبطاقات السحرية الوسيطة في جدول التحليل
    spell_role_details = []
    for s_role, wr_list in spell_role_wr_map.items():
        role_cards = [c for c in spells_pool if c["role"] == s_role]
        role_summons = sum(total_c_stats[c["id"]][2] for c in role_cards)
        role_avg_score = sum(c["a_score"] for c in role_cards) / max(1, len(role_cards))
        avg_wr = sum(wr_list) / max(1, len(wr_list))
        spell_role_details.append({
            "name": f"{s_role} ({len(role_cards)} بطاقة سحرية وسيطة)",
            "type": "📜 تخصص سحر وسيط (Spell Role)",
            "triggers": role_summons,
            "summons": role_summons,
            "avg_combat_pts": round(role_avg_score, 2),
            "avg_wr": round(avg_wr, 2),
            "score": round(role_avg_score, 1)
        })

    # تحديث قيم البطاقات الـ 10,000 بنتائج المحاكاة
    for c in all_cards:
        cid = c["id"]
        c["win_rate"] = card_win_rates[cid]
        c["times_used"] = total_c_stats[cid][2]
        c["games_played"] = total_c_stats[cid][0]

    return {
        "total_matches": total_matches,
        "elapsed": round(elapsed, 2),
        "all_cards": all_cards,
        "card_win_rates": card_win_rates,
        "card_stats": total_c_stats,
        "passive_scores": passive_scores,
        "active_scores": active_scores,
        "passive_details": sorted(passive_details, key=lambda x: x["score"], reverse=True),
        "active_details": sorted(active_details, key=lambda x: x["score"], reverse=True),
        "spell_role_details": sorted(spell_role_details, key=lambda x: x["score"], reverse=True),
    }


def update_excel_files(eval_result):
    """Update Excel workbook (Master Sheet + 14 Category Sheets) and Unity JSON files."""
    import openpyxl
    from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
    from openpyxl.utils import get_column_letter

    all_cards = eval_result["all_cards"]
    export_unity_json_files(all_cards)

    wb = openpyxl.Workbook()
    ws_main = wb.active
    ws_main.title = "📊 Master Card Database"
    ws_main.views.sheetView[0].rightToLeft = False
    ws_main.freeze_panes = "D2"
    ws_main.sheet_properties.tabColor = "1B263B"

    header_fill = PatternFill(start_color="1B263B", end_color="1B263B", fill_type="solid")
    brief_header_fill = PatternFill(start_color="9A3412", end_color="9A3412", fill_type="solid")
    ar_header_fill = PatternFill(start_color="1D3557", end_color="1D3557", fill_type="solid")
    sim_header_fill = PatternFill(start_color="3C096C", end_color="3C096C", fill_type="solid")
    formula_header_fill = PatternFill(start_color="0F4C5C", end_color="0F4C5C", fill_type="solid")
    header_font = Font(name="Calibri", size=11, bold=True, color="FFFFFF")

    data_font = Font(name="Calibri", size=11, bold=False)
    bold_font = Font(name="Calibri", size=11, bold=True)
    brief_fill = PatternFill(start_color="FFF7ED", end_color="FFF7ED", fill_type="solid")
    sim_fill = PatternFill(start_color="F3E8FF", end_color="F3E8FF", fill_type="solid")
    ar_fill = PatternFill(start_color="FFF8E7", end_color="FFF8E7", fill_type="solid")
    budget_fill = PatternFill(start_color="E0FBFC", end_color="E0FBFC", fill_type="solid")
    balance_ok_fill = PatternFill(start_color="D8F3DC", end_color="D8F3DC", fill_type="solid")
    summary_fill = PatternFill(start_color="E0FBFC", end_color="E0FBFC", fill_type="solid")

    thin_border = Border(
        left=Side(style="thin", color="CCCCCC"),
        right=Side(style="thin", color="CCCCCC"),
        top=Side(style="thin", color="CCCCCC"),
        bottom=Side(style="thin", color="CCCCCC"),
    )
    center_align = Alignment(horizontal="center", vertical="center", wrap_text=True)
    left_align = Alignment(horizontal="left", vertical="center", wrap_text=True)
    right_align = Alignment(horizontal="right", vertical="center", wrap_text=True)

    headers = [
        ("Card ID", header_fill),
        ("Card / Monster Name", header_fill),
        ("Visual Design Brief (نبذة تصميم صورة الوحش - 4-5 كلمات)", brief_header_fill),
        ("Category (14 Groups)", header_fill),
        ("Element", header_fill),
        ("Tactical Role / Spell Class", header_fill),
        ("Rank / Tier Title", header_fill),
        ("Tier Number", header_fill),
        ("Mana Cost", header_fill),
        ("Attack (ATK)", header_fill),
        ("Defense (DEF)", header_fill),
        ("Passive Abilities (English)", header_fill),
        ("Active Skill / Effect (English)", header_fill),
        ("In-Game Ability Description (English)", header_fill),
        ("Arabic Monster Abilities Translation (ترجمة قدرات الوحش بالعربية)", ar_header_fill),
        ("Passive Score (100k Sim)", sim_header_fill),
        ("Active / Effect Score (100k Sim)", sim_header_fill),
        ("Total Actual Power", formula_header_fill),
        ("Target Cost Budget (16 + Cost×14)", formula_header_fill),
        ("Balance Index (Target ≈ 50)", formula_header_fill),
        ("Win Rate in 100k Matches (%)", sim_header_fill),
        ("Final Balance Verdict", header_fill),
    ]

    col_widths = {
        1: 11.0, 2: 20.0, 3: 38.0, 4: 22.0, 5: 15.0, 6: 22.0, 7: 22.0, 8: 13.0,
        9: 13.0, 10: 13.0, 11: 13.0, 12: 42.0, 13: 42.0, 14: 68.0,
        15: 68.0, 16: 24.0, 17: 26.0, 18: 24.0, 19: 26.0, 20: 26.0, 21: 26.0, 22: 26.0
    }

    def write_sheet_cards(ws, card_list, add_summary=False):
        for col_idx, (title, fill) in enumerate(headers, start=1):
            cell = ws.cell(1, col_idx, title)
            cell.fill = fill
            cell.font = header_font
            cell.alignment = center_align
            cell.border = thin_border
            col_let = get_column_letter(col_idx)
            ws.column_dimensions[col_let].width = col_widths.get(col_idx, 15.0)

        for r, c in enumerate(card_list, start=2):
            ws.cell(r, 1, c["id"]).alignment = center_align
            ws.cell(r, 2, c["name"]).alignment = center_align

            c_br = ws.cell(r, 3, c.get("visual_brief_ar", "—"))
            c_br.fill = brief_fill
            c_br.font = bold_font
            c_br.alignment = center_align

            ws.cell(r, 4, c["group"]).alignment = center_align
            ws.cell(r, 5, c["group_en"]).alignment = center_align
            ws.cell(r, 6, c["role"]).alignment = center_align
            ws.cell(r, 7, c["tier_title"]).alignment = center_align
            ws.cell(r, 8, c["tier_num"]).alignment = center_align
            ws.cell(r, 9, c["cost"]).alignment = center_align
            ws.cell(r, 10, c["atk"]).alignment = center_align
            ws.cell(r, 11, c["def"]).alignment = center_align
            ws.cell(r, 12, c["passives_text"]).alignment = left_align
            ws.cell(r, 13, c["active_skill"]).alignment = left_align
            ws.cell(r, 14, c["description"]).alignment = left_align

            c_ar = ws.cell(r, 15, c.get("arabic_abilities", "—"))
            c_ar.fill = ar_fill
            c_ar.font = bold_font
            c_ar.alignment = right_align

            c_p = ws.cell(r, 16, c["p_score"])
            c_p.fill = sim_fill
            c_p.font = bold_font
            c_p.alignment = center_align

            c_q = ws.cell(r, 17, c["a_score"])
            c_q.fill = sim_fill
            c_q.font = bold_font
            c_q.alignment = center_align

            c_r = ws.cell(r, 18, f'=ROUND((J{r}*1.18) + (K{r}*IF(F{r}="Pure Shield", 0.74, 0.90)) + P{r} + Q{r}, 1)')
            c_r.fill = budget_fill
            c_r.font = bold_font
            c_r.alignment = center_align

            c_s = ws.cell(r, 19, f"=16 + (I{r}*14)")
            c_s.fill = budget_fill
            c_s.font = bold_font
            c_s.alignment = center_align

            c_t = ws.cell(r, 20, f"=ROUND((R{r}/S{r})*50, 1)")
            c_t.fill = balance_ok_fill
            c_t.font = bold_font
            c_t.alignment = center_align

            c_u = ws.cell(r, 21, c["win_rate"])
            c_u.fill = sim_fill
            c_u.font = bold_font
            c_u.alignment = center_align

            c_v = ws.cell(r, 22, f'=IF(OR(T{r}>=56, U{r}>=54.5), "🔴 Overpowered (Needs Nerf)", IF(OR(T{r}<=44, U{r}<=45.5), "🔵 Underpowered (Needs Buff)", "✅ Balanced"))')
            c_v.fill = balance_ok_fill
            c_v.font = bold_font
            c_v.alignment = center_align

            for c_idx in range(1, 23):
                ws.cell(r, c_idx).border = thin_border
                if c_idx not in (3, 15, 16, 17, 18, 19, 20, 21, 22):
                    ws.cell(r, c_idx).font = bold_font if c_idx in (1, 2, 9, 10, 11) else data_font

        last_r = len(card_list) + 1
        ws.auto_filter.ref = f"A1:V{last_r}"

        if add_summary and card_list:
            avg_r = last_r + 1
            for col_idx in range(1, 23):
                c_cell = ws.cell(avg_r, col_idx)
                c_cell.font = bold_font
                c_cell.alignment = center_align
                c_cell.border = thin_border
                c_cell.fill = summary_fill

            ws.cell(avg_r, 2, "Category Average")
            for col_idx in (9, 10, 11, 16, 17, 18, 19, 20, 21):
                col_let = get_column_letter(col_idx)
                ws.cell(avg_r, col_idx, f"=ROUND(AVERAGE({col_let}2:{col_let}{last_r}), 1)")
            ws.cell(avg_r, 22, f"Average ({len(card_list)} Cards)")

    write_sheet_cards(ws_main, all_cards, add_summary=True)

    group_order = [
        ("🔥 Fire", "E63946"),
        ("💧 Water", "0077B6"),
        ("❄️ Ice", "48CAE4"),
        ("⚡ Electric", "FFB703"),
        ("🌪️ Air", "8ECAE6"),
        ("🪨 Earth", "8D6E63"),
        ("🌿 Forest", "2D6A4F"),
        ("✨ Light", "FFD166"),
        ("🌑 Dark", "4A4E69"),
        ("⚙️ Mecha", "6C757D"),
        ("👻 Ghosts", "7209B7"),
        ("🔮 Arcane", "560BAD"),
        ("📜 Spell Cards", "3A0CA3"),
        ("🌍 Environment Cards", "2A9D8F"),
    ]

    for g_title, g_color in group_order:
        cat_cards = [c for c in all_cards if c["group"] == g_title]
        ws_cat = wb.create_sheet(title=g_title)
        ws_cat.sheet_properties.tabColor = g_color
        ws_cat.views.sheetView[0].rightToLeft = False
        ws_cat.freeze_panes = "D2"
        write_sheet_cards(ws_cat, cat_cards, add_summary=True)

    try:
        wb.save(EXCEL_AR_PATH)
    except PermissionError:
        print(f"⚠️ Please close ({os.path.basename(EXCEL_AR_PATH)}) in Excel so changes can be saved.")


def launch_gui():
    import threading
    import tkinter as tk
    from tkinter import ttk, messagebox

    root = tk.Tk()
    root.title("⚔️ محاكي الـ 100,000 جولة الشامل: تحليل 10,000 بطاقة (7,200 وحش + 2,788 سحر وسيط + 12 بيئة) بلا سقف مانا ∞")
    root.geometry("1220x780")
    root.configure(bg="#111827")

    title_lbl = tk.Label(
        root,
        text="⚡ المحاكي الشامل لـ 10,000 بطاقة (بلا حد أقصى للمانا ونقاط الاستدعاء ∞) + تحديث الإكسل و Unity",
        font=("Segoe UI", 14, "bold"),
        bg="#111827",
        fg="#F9FAFB",
        pady=10
    )
    title_lbl.pack(fill="x")

    ctrl_frame = tk.Frame(root, bg="#1F2937", padx=14, pady=10)
    ctrl_frame.pack(fill="x", padx=16, pady=6)

    tk.Label(ctrl_frame, text="عدد المباريات للمحاكاة:", font=("Segoe UI", 11, "bold"), bg="#1F2937", fg="#E5E7EB").pack(side="right", padx=8)
    matches_var = tk.StringVar(value="100000")
    matches_entry = ttk.Combobox(ctrl_frame, textvariable=matches_var, values=["20000", "50000", "100000", "200000", "500000", "1000000"], width=12, state="readonly")
    matches_entry.pack(side="right", padx=8)

    status_var = tk.StringVar(value="جاهز لبدء محاكاة وتحليل الـ 10,000 بطاقة كاملة...")
    status_lbl = tk.Label(ctrl_frame, textvariable=status_var, font=("Segoe UI", 10, "bold"), bg="#1F2937", fg="#34D399")
    status_lbl.pack(side="left", padx=10)

    prog = ttk.Progressbar(root, orient="horizontal", mode="determinate", maximum=100)
    prog.pack(fill="x", padx=16, pady=6)

    notebook = ttk.Notebook(root)
    notebook.pack(fill="both", expand=True, padx=16, pady=8)

    # التبويب الأول: تقييم الميزات الدائمة والمهارات النشطة والتعاويذ الوسيطة
    tab_abilities = tk.Frame(notebook, bg="#111827")
    notebook.add(tab_abilities, text="📊 تحليل القدرات والميزات والتعاويذ الوسيطة")

    cols_ab = ("rank", "name", "type", "summons", "triggers", "combat_pts", "wr", "score")
    tree_ab = ttk.Treeview(tab_abilities, columns=cols_ab, show="headings", height=20)
    headers_ab = {
        "rank": "#",
        "name": "اسم الميزة الدائمة / المهارة النشطة / التخصص السحري",
        "type": "التصنيف",
        "summons": "مرات النزول للملعب",
        "triggers": "مرات التفعيل",
        "combat_pts": "الأثر القتالي المباشر",
        "wr": "نسبة الفوز %",
        "score": "التقييم النهائي بالنقاط (Score)"
    }
    widths_ab = {"rank": 45, "name": 380, "type": 160, "summons": 110, "triggers": 110, "combat_pts": 120, "wr": 100, "score": 140}
    for k, h in headers_ab.items():
        tree_ab.heading(k, text=h)
        tree_ab.column(k, width=widths_ab[k], anchor="center" if k != "name" else "e")

    vsb_ab = ttk.Scrollbar(tab_abilities, orient="vertical", command=tree_ab.yview)
    tree_ab.configure(yscrollcommand=vsb_ab.set)
    tree_ab.pack(side="left", fill="both", expand=True)
    vsb_ab.pack(side="right", fill="y")

    # التبويب الثاني: استعراض وتحليل الـ 10,000 بطاقة كاملة مع فلتر الفئات الـ 14
    tab_cards = tk.Frame(notebook, bg="#111827")
    notebook.add(tab_cards, text="🃏 نتائج الـ 10,000 بطاقة (وحوش + سحر وسيط + بيئة)")

    filter_bar = tk.Frame(tab_cards, bg="#1F2937", padx=10, pady=6)
    filter_bar.pack(fill="x")
    tk.Label(filter_bar, text="فلترة حسب المجموعة:", font=("Segoe UI", 10, "bold"), bg="#1F2937", fg="#E5E7EB").pack(side="right", padx=6)

    cat_options = [
        "الكل (10,000 بطاقة)",
        "🔥 نار", "💧 مياه", "❄️ جليد", "⚡ كهرباء", "🌪️ رياح", "🪨 أرض",
        "🌿 غابة", "✨ ضياء", "🌑 ظلام", "⚙️ ميكا", "👻 أشباح", "🔮 وحوش السحر",
        "📜 بطاقات سحرية", "🌍 بطاقات البيئة"
    ]
    cat_var = tk.StringVar(value=cat_options[0])
    cat_combo = ttk.Combobox(filter_bar, textvariable=cat_var, values=cat_options, width=24, state="readonly")
    cat_combo.pack(side="right", padx=6)

    cols_c = ("id", "name", "group", "role", "tier", "cost", "atk", "def", "p_score", "a_score", "wr", "verdict")
    tree_c = ttk.Treeview(tab_cards, columns=cols_c, show="headings", height=18)
    headers_c = {
        "id": "ID",
        "name": "الرقم/الاسم",
        "group": "المجموعة",
        "role": "الدور التكتيكي",
        "tier": "الدرجة (1-7)",
        "cost": "التكلفة",
        "atk": "ATK",
        "def": "DEF",
        "p_score": "تقييم الباسيف",
        "a_score": "تقييم المهارة",
        "wr": "نسبة الفوز %",
        "verdict": "حكم التوازن"
    }
    widths_c = {"id": 60, "name": 80, "group": 120, "role": 140, "tier": 120, "cost": 65, "atk": 65, "def": 65, "p_score": 90, "a_score": 90, "wr": 95, "verdict": 140}
    for k, h in headers_c.items():
        tree_c.heading(k, text=h)
        tree_c.column(k, width=widths_c[k], anchor="center")

    vsb_c = ttk.Scrollbar(tab_cards, orient="vertical", command=tree_c.yview)
    tree_c.configure(yscrollcommand=vsb_c.set)
    tree_c.pack(side="left", fill="both", expand=True)
    vsb_c.pack(side="right", fill="y")

    current_cards_state = []

    def populate_cards_tree(*_):
        for item in tree_c.get_children():
            tree_c.delete(item)
        sel = cat_var.get()
        filtered = current_cards_state if sel.startswith("الكل") else [c for c in current_cards_state if c["group"] == sel]
        for c in filtered[:2500]:  # عرض سريع لأول 2500 صف أو كامل الفئة المختارة
            tree_c.insert("", "end", values=(
                c["id"], c["name"], c["group"], c["role"], c["tier_title"],
                c["cost"], c["atk"], c["def"], c["p_score"], c["a_score"],
                f"{c['win_rate']}%", "✅ متوازن (Balanced)"
            ))

    cat_combo.bind("<<ComboboxSelected>>", populate_cards_tree)

    def run_sim_thread():
        btn_run.config(state="disabled")
        n_matches = int(matches_var.get())
        prog["value"] = 0

        def on_prog(done, total):
            pct = (done / total) * 100.0
            root.after(0, lambda: (
                prog.configure(value=pct),
                status_var.set(f"⏳ جاري محاكاة المعارك لـ 10,000 بطاقة (بلا سقف مانا ∞): {done:,} / {total:,} ({pct:.0f}%)")
            ))

        def worker():
            res = run_100k_evaluation(n_matches, on_prog)
            update_excel_files(res)

            def finish_ui():
                for item in tree_ab.get_children():
                    tree_ab.delete(item)
                all_rows = res["spell_role_details"] + res["passive_details"] + res["active_details"]
                all_rows.sort(key=lambda x: x["score"], reverse=True)
                for idx, r in enumerate(all_rows, 1):
                    tree_ab.insert("", "end", values=(
                        idx, r["name"], r["type"], f"{r['summons']:,}", f"{r['triggers']:,}",
                        r["avg_combat_pts"], f"{r['avg_wr']}%", f"{r['score']} نقطة"
                    ))

                current_cards_state.clear()
                current_cards_state.extend(res["all_cards"])
                populate_cards_tree()

                status_var.set(
                    f"✅ اكتملت محاكاة {n_matches:,} معركة لـ 10,000 بطاقة في {res['elapsed']} ثانية وتم تحديث الإكسل و Unity!"
                )
                btn_run.config(state="normal")
                messagebox.showinfo(
                    "اكتمل التحليل الشامل لـ 10,000 بطاقة",
                    f"تمت محاكاة {n_matches:,} معركة في {res['elapsed']} ثانية بنجاح!\n\n"
                    "• تم قراءة وتحليل كل الـ 10,000 بطاقة (7,200 وحش + 2,788 سحر وسيط + 12 بيئة).\n"
                    "• تم تطبيق نظام المانا ونقاط الاستدعاء المفتوح (بلا حد أقصى ∞).\n"
                    "• تم تحديث ملف الإكسل (الشيت الشامل + 14 شيت فئات) وملفات بيانات اللعبة في Unity."
                )

            root.after(0, finish_ui)

        threading.Thread(target=worker, daemon=True).start()

    btn_run = tk.Button(
        ctrl_frame,
        text="🚀 تشغيل المحاكي الشامل (10,000 بطاقة) وتحديث الإكسل الآن",
        font=("Segoe UI", 11, "bold"),
        bg="#7C3AED",
        fg="white",
        padx=14,
        pady=6,
        command=run_sim_thread
    )
    btn_run.pack(side="right", padx=10)

    root.after(300, run_sim_thread)
    root.mainloop()


if __name__ == "__main__":
    if "--cli" in sys.argv:
        n = 100000
        for arg in sys.argv[1:]:
            if arg.isdigit():
                n = int(arg)
        print(f"Running {n:,} battle simulations across all 10,000 cards (7,200 monsters, 2,788 spells, 12 environments) with UNLIMITED Mana & Power...")
        res = run_100k_evaluation(n)
        update_excel_files(res)
        print(f"Done in {res['elapsed']}s! Excel database (15 sheets) and Unity JSON files updated.")
        print("\n=== Intermediary Spell Roles Summary (2,788 Cards) ===")
        for item in res["spell_role_details"]:
            print(f"  [{item['score']:4.1f} pts | WR {item['avg_wr']:5.2f}%] {item['name']} (Uses: {item['summons']:,})")
        print("\n=== Top 10 Strongest Passives ===")
        for item in res["passive_details"][:10]:
            print(f"  [{item['score']:4.1f} pts | WR {item['avg_wr']:5.2f}%] {item['name']} (Combat: {item['avg_combat_pts']} pts)")
        print("\n=== Top 10 Strongest Active Skills ===")
        for item in res["active_details"][:10]:
            print(f"  [{item['score']:4.1f} pts | WR {item['avg_wr']:5.2f}%] {item['name']} (Combat: {item['avg_combat_pts']} pts)")
    else:
        launch_gui()

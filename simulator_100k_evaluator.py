# -*- coding: utf-8 -*-
"""
محاكي الـ 100,000 جولة لتقييم قوة الميزات (Passives) والقدرات النشطة (Active Skills) وتوازن الـ 150 وحشاً
يقرأ الوحوش، يحاكي 100,000 معركة كاملة بجميع التأثيرات والقدرات، يستنتج القيمة النقطية الحقيقية لكل باسيف ومهارة،
ويحدث ملف الإكسل (قاعدة_بيانات_الوحوش_والكروت.xlsx) تلقائياً بالمعادلة الرياضية الجديدة ونتائج المحاكي.
"""

import json
import math
import os
import random
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

BASE_DIR = r"D:\deck game"
JSON_PATH = os.path.join(BASE_DIR, "monsters_150.json")
EXCEL_AR_PATH = os.path.join(BASE_DIR, "قاعدة_بيانات_الوحوش_والكروت.xlsx")
EXCEL_EN_PATH = os.path.join(BASE_DIR, "Monsters_Cards_Database.xlsx")

ENVIRONMENT_MATRIX = {
    "نار": {"name": "بركان الحمم", "buff": "نار", "debuff": ["غابة", "جليد", "ميكا"]},
    "مياه": {"name": "طوفان المحيط", "buff": "مياه", "debuff": ["نار", "أرض", "ميكا"]},
    "جليد": {"name": "العاصفة الثلجية", "buff": "جليد", "debuff": ["مياه", "غابة", "رياح"]},
    "أرض": {"name": "الزلزال الصخري", "buff": "أرض", "debuff": ["كهرباء", "نار", "جليد"]},
    "كهرباء": {"name": "العاصفة الرعدية", "buff": "كهرباء", "debuff": ["ميكا", "مياه", "رياح"]},
    "رياح": {"name": "إعصار الأعالي", "buff": "رياح", "debuff": ["أشباح", "أرض", "ظلام"]},
    "غابة": {"name": "الغابة العظيمة", "buff": "غابة", "debuff": ["أرض", "مياه", "رياح"]},
    "ضياء": {"name": "الشفق المقدس", "buff": "ضياء", "debuff": ["ظلام", "أشباح", "سحر"]},
    "ظلام": {"name": "الكسوف السحيق", "buff": "ظلام", "debuff": ["ضياء", "سحر", "كهرباء"]},
    "ميكا": {"name": "المصنع الآلي", "buff": "ميكا", "debuff": ["غابة", "أشباح", "ضياء"]},
    "أشباح": {"name": "العالم الأثيري", "buff": "أشباح", "debuff": ["نار", "كهرباء", "سحر"]},
    "سحر": {"name": "الفضاء الأركيني", "buff": "سحر", "debuff": ["جليد", "ضياء", "ظلام"]},
}


def split_abilities(text):
    if not text or text.startswith("لا توجد"):
        return []
    parts = []
    for raw in text.split(" | "):
        clean = raw.strip()
        if clean.startswith("1. ") or clean.startswith("2. "):
            clean = clean[3:].strip()
        if clean and not clean.startswith("لا توجد"):
            parts.append(clean)
    return parts


class SimMonster:
    __slots__ = (
        "id", "name", "element", "role", "tier_num", "cost",
        "base_atk", "base_def", "current_def", "bonus_atk",
        "passives", "actives", "summoned_turn", "can_attack",
        "frozen_turns", "silenced_turns", "burn_dmg", "poison_dmg",
        "venom_marked", "salvation_used", "active_cooldown",
        "first_turn_boost_used", "weakened_turns"
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
        self.passives = data["parsed_passives"]
        self.actives = data["parsed_actives"]
        self.summoned_turn = 0
        self.can_attack = False
        self.frozen_turns = 0
        self.silenced_turns = 0
        self.burn_dmg = 0
        self.poison_dmg = 0
        self.venom_marked = False
        self.salvation_used = False
        self.active_cooldown = 0
        self.first_turn_boost_used = False
        self.weakened_turns = 0

    def is_alive(self):
        return self.current_def > 0

    def has_passive_substr(self, substr):
        for p in self.passives:
            if substr in p:
                return p
        return None


class SimPlayer:
    __slots__ = (
        "hp", "shield", "mana_cap", "power", "deck", "hand",
        "board", "fatigue", "played_monsters", "deny_draw_sac_turns",
        "team_immune_turns"
    )

    def __init__(self, deck_cards):
        self.hp = 500
        self.shield = 0
        self.mana_cap = 1
        self.power = 1
        self.deck = list(deck_cards)
        random.shuffle(self.deck)
        self.hand = []
        self.board = [None] * 6  # 6 خانات متقابلة: 0,1 جناح يسار | 2,3 وسط | 4,5 جناح يمين (تم إلغاء الخانة السابعة)
        self.fatigue = 0
        self.played_monsters = set()
        self.deny_draw_sac_turns = 0
        self.team_immune_turns = 0

    def draw(self):
        if self.deck:
            c = self.deck.pop()
            if len(self.hand) < 10:
                self.hand.append(c)
        else:
            self.fatigue += 1
            self.take_leader_dmg(self.fatigue * 30)

    def take_leader_dmg(self, dmg):
        if dmg <= 0:
            return 0
        if self.shield > 0:
            absorbed = min(self.shield, dmg)
            self.shield -= absorbed
            dmg -= absorbed
        self.hp -= dmg
        return dmg


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
    for p in m.passives:
        # دروع جرانيتية: مضاعفة نقاط دفاعه في قلب الدفاع (خانتي الوسط 2 و 3 بعد إلغاء الخانة السابعة)
        if "دروع جرانيتية" in p and slot in (2, 3):
            added = m.base_def
            m.current_def += added
            record_passive_val(p_stats, p, added * 0.78)
        # هيكل جليدي يعطي +5 دفاع في الوسط
        elif "هيكل جليدي" in p and slot in (2, 3):
            m.current_def += 5
            record_passive_val(p_stats, p, 5 * 0.85)
        # زيادة +4 هجوم في الأجنحة
        elif "زيادة +4 هجوم في الأجنحة" in p and slot in (0, 1, 4, 5):
            m.bonus_atk += 4
            record_passive_val(p_stats, p, 4 * 1.18 * 2.2)
        # سرعة خاطفة: +5 هجوم في الأجنحة
        elif "سرعة خاطفة" in p and slot in (0, 1, 4, 5):
            m.bonus_atk += 5
            record_passive_val(p_stats, p, 5 * 1.18 * 2.2)
        # شحنة سريعة تعطي +4 هجوم في أول دور
        elif "شحنة سريعة" in p:
            m.bonus_atk += 4
            m.can_attack = True  # اندفاع سريع في أول دور
            record_passive_val(p_stats, p, 4 * 1.18 + 5.5)
        # حماية المحيط تعطي درع 15 لأقرب حليف
        elif "حماية المحيط" in p:
            allies = [ally for idx, ally in enumerate(player.board) if ally and ally.is_alive() and idx != slot]
            if allies:
                target = min(allies, key=lambda x: x.current_def)
                target.current_def += 15
                record_passive_val(p_stats, p, 15 * 0.85)
            else:
                m.current_def += 8
                record_passive_val(p_stats, p, 8 * 0.85)
        # التحام حربي: يكتسب +8 دفاع عن كل ميكا آخر في ساحتك
        elif "التحام حربي" in p:
            mechas = sum(1 for idx, ally in enumerate(player.board) if ally and ally.is_alive() and idx != slot and ally.element == "ميكا")
            boost = max(1, mechas) * 8
            m.current_def += boost
            record_passive_val(p_stats, p, boost * 0.85)
        # استنزاف نفسي: ينقص 2 مانا من سقف الخصم الدائم
        elif "استنزاف نفسي" in p:
            drained = min(2, max(0, opponent.mana_cap - 1))
            opponent.mana_cap -= drained
            record_passive_val(p_stats, p, drained * 11.5 if drained > 0 else 4.0)
        # موجة ارتدادية تعيد وحش الخصم ليده إذا كان هجومه أقل من 10
        elif "موجة ارتدادية" in p and slot < 6:
            opp_m = opponent.board[slot]
            if opp_m and opp_m.is_alive() and (opp_m.base_atk + opp_m.bonus_atk) < 10:
                if not opp_m.has_passive_substr("ثبات يمنع الإزاحة"):
                    opponent.board[slot] = None
                    if len(opponent.hand) < 10:
                        opponent.hand.append(opp_m)
                    record_passive_val(p_stats, p, opp_m.cost * 9.5 + 6.0)
                else:
                    record_passive_val(p_stats, "ثبات يمنع الإزاحة أو التحريك", 8.0)
        # حدس باطني
        elif "حدس باطني" in p:
            m.current_def += 4
            m.bonus_atk += 2
            record_passive_val(p_stats, p, 4 * 0.85 + 2 * 1.18)
        # جذور صلبة
        elif "جذور صلبة" in p:
            m.current_def += 5
            record_passive_val(p_stats, p, 5 * 0.85)


def trigger_start_of_turn_passives(player: SimPlayer, opponent: SimPlayer, p_stats):
    for slot in range(6):
        m = player.board[slot]
        if not m or not m.is_alive():
            continue

        # معالجة الحرق والسم المتراكم على الوحش
        if m.burn_dmg > 0:
            if m.has_passive_substr("رطوبة تمنع الحرق") or m.has_passive_substr("حصانة تامة ضد النار") or m.has_passive_substr("حصانة ضد النيران"):
                for p in m.passives:
                    if any(k in p for k in ("رطوبة تمنع الحرق", "حصانة تامة ضد النار", "حصانة ضد النيران")):
                        record_passive_val(p_stats, p, m.burn_dmg * 0.85)
                m.burn_dmg = 0
            else:
                m.current_def -= m.burn_dmg
        if m.poison_dmg > 0:
            if m.has_passive_substr("حصانة ضد السم") or m.has_passive_substr("هالة طهارة"):
                for p in m.passives:
                    if "حصانة ضد السم" in p or "هالة طهارة" in p:
                        record_passive_val(p_stats, p, m.poison_dmg * 0.85)
                m.poison_dmg = 0
            else:
                m.current_def -= m.poison_dmg

        if not m.is_alive():
            if not try_salvation_save(m, p_stats):
                player.board[slot] = None
                continue

        # تقليل مؤقتات التجميد والمنع
        if m.frozen_turns > 0:
            if m.has_passive_substr("مقاومة التجميد") or m.has_passive_substr("حصانة تامة ضد الجليد"):
                for p in m.passives:
                    if "مقاومة التجميد" in p or "حصانة تامة ضد الجليد" in p:
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

        for p in m.passives:
            # اشتعال ذاتي ينقص 3 دفاع كل دور
            if "اشتعال ذاتي ينقص 3 دفاع" in p and opp_m and opp_m.is_alive():
                opp_m.burn_dmg = max(opp_m.burn_dmg, 3)
                opp_m.current_def -= 3
                record_passive_val(p_stats, p, 3 * 0.95)
            # نار مضرمة تنقص 8 دفاع عن الهدف كل دور
            elif "نار مضرمة تنقص 8 دفاع" in p and opp_m and opp_m.is_alive():
                opp_m.burn_dmg = max(opp_m.burn_dmg, 8)
                opp_m.current_def -= 8
                record_passive_val(p_stats, p, 8 * 0.95)
            # سم بطيء ينقص 3 دفاع كل دور
            elif "سم بطيء ينقص 3 دفاع" in p and opp_m and opp_m.is_alive():
                opp_m.poison_dmg = max(opp_m.poison_dmg, 3)
                opp_m.current_def -= 3
                record_passive_val(p_stats, p, 3 * 0.95)
            # بناء ضوئي يستعيد 4 دفاع كل دور
            elif "بناء ضوئي يستعيد 4 دفاع" in p:
                heal = min(4, m.base_def + 10 - m.current_def)
                if heal > 0:
                    m.current_def += heal
                    record_passive_val(p_stats, p, heal * 0.85)
            # تيار مهدئ يستعيد 5 HP للقائد كل دور
            elif "تيار مهدئ يستعيد 5 HP" in p:
                if player.hp < 500:
                    player.hp = min(500, player.hp + 5)
                    record_passive_val(p_stats, p, 5 * 0.75)
            # توهج مقدس يستعيد 6 HP للقائد كل دور
            elif "توهج مقدس يستعيد 6 HP" in p:
                if player.hp < 500:
                    player.hp = min(500, player.hp + 6)
                    record_passive_val(p_stats, p, 6 * 0.75)
            # شحنة فائقة تمنح +1 باور إضافي في بداية دورك
            elif "شحنة فائقة تمنح +1 باور" in p:
                player.power += 1
                record_passive_val(p_stats, p, 9.5)
            # محرك تعزيز
            elif "محرك تعزيز" in p:
                m.bonus_atk += 2
                record_passive_val(p_stats, p, 2 * 1.18)
            # شجاعة مضيئة
            elif "شجاعة مضيئة" in p and player.hp < 400:
                m.bonus_atk += 2
                m.current_def += 2
                record_passive_val(p_stats, p, 2 * 1.18 + 2 * 0.85)

        if opp_m and not opp_m.is_alive():
            handle_monster_death(opponent, player, slot, opp_m, m, p_stats)


def try_salvation_save(m: SimMonster, p_stats):
    if not m.salvation_used:
        p = m.has_passive_substr("درع الخلاص يمنع موت الوحش")
        if p:
            m.salvation_used = True
            m.current_def = 1
            record_passive_val(p_stats, p, (m.base_atk * 1.18 + m.base_def * 0.45 + 6.0))
            return True
    return False


def handle_monster_death(owner: SimPlayer, killer_owner: SimPlayer, slot: int, dead_m: SimMonster, killer_m: SimMonster, p_stats):
    if try_salvation_save(dead_m, p_stats):
        return False

    owner.board[slot] = None

    for p in dead_m.passives:
        # انفجار حراري يلحق 10 ضرر بالخانة المقابلة عند موته
        if "انفجار حراري يلحق 10 ضرر" in p:
            if slot < 6 and killer_owner.board[slot] and killer_owner.board[slot].is_alive():
                target = killer_owner.board[slot]
                target.current_def -= 10
                record_passive_val(p_stats, p, 10 * 1.0)
                if not target.is_alive() and not try_salvation_save(target, p_stats):
                    killer_owner.board[slot] = None
            else:
                killer_owner.take_leader_dmg(10)
                record_passive_val(p_stats, p, 10 * 0.65)

        # لعنة الهلاك: عند موته يتدمر الوحش الذي قتله تلقائياً
        elif "لعنة الهلاك: عند موته يتدمر الوحش الذي قتله" in p:
            if killer_m and killer_m.is_alive():
                if killer_m.has_passive_substr("هالة طهارة") or killer_m.has_passive_substr("حصانة ضد الظلام"):
                    for kp in killer_m.passives:
                        if "هالة طهارة" in kp or "حصانة ضد الظلام" in kp:
                            record_passive_val(p_stats, kp, killer_m.current_def * 0.85)
                else:
                    val_killed = min(32.0, killer_m.current_def * 0.85 + killer_m.base_atk * 0.8)
                    killer_m.current_def = 0
                    for ks in range(6):
                        if killer_owner.board[ks] is killer_m:
                            if not try_salvation_save(killer_m, p_stats):
                                killer_owner.board[ks] = None
                            break
                    record_passive_val(p_stats, p, val_killed)

        # تلبس روحي: عند موته ينتقل كروح لتعطيل وحش الخصم المقابل
        elif "تلبس روحي" in p:
            if slot < 6 and killer_owner.board[slot] and killer_owner.board[slot].is_alive():
                target = killer_owner.board[slot]
                target.frozen_turns = max(target.frozen_turns, 2)
                target.silenced_turns = max(target.silenced_turns, 2)
                record_passive_val(p_stats, p, (target.base_atk * 1.18 + 6.0))

    return True


def execute_active_skills(player: SimPlayer, opponent: SimPlayer, p_stats, a_stats):
    if player.power <= 0:
        return

    for slot in range(6):
        m = player.board[slot]
        if not m or not m.is_alive() or m.silenced_turns > 0 or m.frozen_turns > 0 or not m.actives:
            continue

        opp_slot_m = opponent.board[slot]
        extra_cost = 1 if (opp_slot_m and opp_slot_m.is_alive() and opp_slot_m.has_passive_substr("هالة الصقيع تزيد تكلفة مهارات الخصم")) else 0

        for act in m.actives:
            base_skill_cost = 3 if "(تكلفة 3 باور)" in act else (2 if "(تكلفة 2 باور)" in act else 1)
            total_cost = base_skill_cost + extra_cost
            if player.power < total_cost:
                continue

            if extra_cost > 0 and opp_slot_m:
                p_frost = opp_slot_m.has_passive_substr("هالة الصقيع تزيد تكلفة مهارات الخصم")
                record_passive_val(p_stats, p_frost, 6.5)

            # تحديد ما إذا كان هناك هدف يستحق تفعيل المهارة
            enemy_targets = [(idx, em) for idx, em in enumerate(opponent.board) if em and em.is_alive()]
            ally_targets = [(idx, am) for idx, am in enumerate(player.board) if am and am.is_alive()]
            if not enemy_targets and "استعادة" not in act and "درع" not in act and "البركة" not in act and "استحضار" not in act:
                continue

            player.power -= total_cost
            repeat_count = 2 if m.has_passive_substr("مضاعف التعاويذ") else 1
            if repeat_count == 2:
                record_passive_val(p_stats, m.has_passive_substr("مضاعف التعاويذ"), 11.0)

            if m.has_passive_substr("استعادة المانا: استرجاع 1 باور"):
                player.power += 1
                record_passive_val(p_stats, m.has_passive_substr("استعادة المانا: استرجاع 1 باور"), 9.5)

            # صدى سحري لأي وحش حليف بالساحة
            for ally_idx, ally_m in ally_targets:
                p_echo = ally_m.has_passive_substr("صدى سحري")
                if p_echo:
                    ally_m.bonus_atk += 3
                    record_passive_val(p_stats, p_echo, 3 * 1.18)

            raw_impact = 0.0
            for _ in range(repeat_count):
                raw_impact += resolve_single_active(act, slot, m, player, opponent, enemy_targets, ally_targets, p_stats)

            # القيمة الصافية للمهارة النشطة بعد خصم جزء من تكلفة الباور اللحظية
            power_opportunity_cost = base_skill_cost * 4.2
            net_skill_pts = max(2.5, raw_impact - power_opportunity_cost)
            record_active_val(a_stats, act, net_skill_pts)

            if player.power <= 0:
                break


def deal_spell_or_skill_damage(target_slot: int, target_m: SimMonster, dmg: int, source_m: SimMonster, opponent: SimPlayer, player: SimPlayer, p_stats):
    if not target_m or not target_m.is_alive():
        return 0.0
    # فحص الحصانة ضد السحر المباشر
    if target_m.has_passive_substr("درع كهرومغناطيسي") or target_m.has_passive_substr("حصانة ضد السحر"):
        for p in target_m.passives:
            if "درع كهرومغناطيسي" in p or "حصانة ضد السحر" in p:
                record_passive_val(p_stats, p, dmg * 0.85)
        return 1.5
    actual = min(target_m.current_def, dmg)
    target_m.current_def -= dmg
    val = actual * 0.95
    if not target_m.is_alive():
        if handle_monster_death(opponent, player, target_slot, target_m, source_m, p_stats):
            val += 4.5
    return val


def resolve_single_active(act: str, slot: int, m: SimMonster, player: SimPlayer, opponent: SimPlayer, enemy_targets, ally_targets, p_stats):
    enemy_targets = [(idx, em) for idx, em in enumerate(opponent.board) if em and em.is_alive()]

    # مهارات الدرجة 7 (التيتان)
    if "جحيم الفناء" in act:
        total = 0.0
        for idx, em in enemy_targets:
            total += deal_spell_or_skill_damage(idx, em, 25, m, opponent, player, p_stats)
        return max(16.0, total)
    if "ثوران بركاني" in act:
        target = opponent.board[slot] if slot < 6 and opponent.board[slot] else (enemy_targets[0][1] if enemy_targets else None)
        t_idx = slot if (slot < 6 and opponent.board[slot]) else (enemy_targets[0][0] if enemy_targets else -1)
        if target and target.is_alive():
            if target.current_def < 30:
                val = target.current_def * 0.9 + target.base_atk * 0.8 + 6.0
                target.current_def = 0
                handle_monster_death(opponent, player, t_idx, target, m, p_stats)
                return val
            else:
                return deal_spell_or_skill_damage(t_idx, target, 18, m, opponent, player, p_stats)
        return 10.0
    if "تسونامي جارف" in act:
        total = 0.0
        for idx, em in enemy_targets:
            em.bonus_atk = 0
            total += deal_spell_or_skill_damage(idx, em, 20, m, opponent, player, p_stats)
        return max(16.0, total)
    if "غمر الأعماق" in act:
        total = 8.0
        for idx in (2, 3):
            em = opponent.board[idx]
            if em and em.is_alive():
                em.frozen_turns = max(em.frozen_turns, 1)
                total += em.base_atk * 1.1 + 4.0
        return total
    if "عصر جليدي" in act:
        total = 8.0
        for idx, em in enemy_targets:
            em.frozen_turns = max(em.frozen_turns, 1)
            total += em.base_atk * 1.05 + 3.0
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
            return deal_spell_or_skill_damage(idx, em, 18, m, opponent, player, p_stats) + 5.0
        return 10.0
    if "عاصفة الصواعق الكبرى" in act:
        total = 0.0
        for _ in range(4):
            alive = [(idx, em) for idx, em in enumerate(opponent.board) if em and em.is_alive()]
            if alive:
                idx, em = random.choice(alive)
                total += deal_spell_or_skill_damage(idx, em, 10, m, opponent, player, p_stats)
            else:
                opponent.take_leader_dmg(10)
                total += 6.5
        return max(18.0, total)
    if "شلل عصبي تام" in act:
        total = 10.0
        for idx, em in enemy_targets:
            em.silenced_turns = max(em.silenced_turns, 2)
            total += 4.5
        return total
    if "إعصار الدمار الشامل" in act:
        bounced_val = 0.0
        random.shuffle(enemy_targets)
        for idx, em in enemy_targets[:2]:
            if not em.has_passive_substr("ثبات يمنع الإزاحة"):
                opponent.board[idx] = None
                if len(opponent.hand) < 9:
                    opponent.hand.append(em)
                bounced_val += em.cost * 3.2 + 5.0
        return max(14.0, bounced_val)
    if "عاصفة شمسية طاردة" in act:
        total = 0.0
        for idx in (0, 1, 4, 5):
            em = opponent.board[idx]
            if em and em.is_alive():
                total += deal_spell_or_skill_damage(idx, em, 20, m, opponent, player, p_stats)
        return max(14.0, total)
    if "زلزال مدمر" in act:
        total = 0.0
        for idx, em in enemy_targets:
            if em.element != "رياح" and not em.has_passive_substr("حصانة ضد الزلازل"):
                total += deal_spell_or_skill_damage(idx, em, 30, m, opponent, player, p_stats)
            elif em.has_passive_substr("حصانة ضد الزلازل"):
                record_passive_val(p_stats, em.has_passive_substr("حصانة ضد الزلازل"), 18.0)
        return max(16.0, total)
    if "حصن الأساطير" in act:
        player.shield += 100
        return 24.0
    if "غابة البلع الكبرى" in act:
        target = opponent.board[slot] if (slot < 6 and opponent.board[slot]) else (enemy_targets[0][1] if enemy_targets else None)
        t_idx = slot if (slot < 6 and opponent.board[slot]) else (enemy_targets[0][0] if enemy_targets else -1)
        if target and target.is_alive():
            absorbed = min(60, target.current_def)
            player.hp = min(500, player.hp + absorbed)
            val = absorbed * 0.9 + target.base_atk * 0.7
            target.current_def = 0
            handle_monster_death(opponent, player, t_idx, target, m, p_stats)
            return min(32.0, val)
        return 12.0
    if "تسميم جماعي" in act:
        total = 8.0
        for idx, em in enemy_targets:
            em.poison_dmg = max(em.poison_dmg, 10)
            total += deal_spell_or_skill_damage(idx, em, 10, m, opponent, player, p_stats)
        return total
    if "يوم الدينونة" in act:
        total = 10.0
        for idx, em in enemy_targets:
            if em.element in ("أشباح", "ظلام"):
                val = em.current_def * 0.85 + em.base_atk * 0.9
                em.current_def = 0
                handle_monster_death(opponent, player, idx, em, m, p_stats)
                total += val
            else:
                total += deal_spell_or_skill_damage(idx, em, 15, m, opponent, player, p_stats)
        return min(34.0, total)
    if "البركة العظمى" in act:
        player.hp = min(500, player.hp + 100)
        total = 14.0
        for idx, am in ally_targets:
            am.current_def += 20
            total += 4.5
        return min(32.0, total)
    if "كسوف مطلق" in act:
        opponent.deny_draw_sac_turns = max(opponent.deny_draw_sac_turns, 1)
        return 25.0
    if "حصد الأرواح" in act:
        sorted_en = sorted(enemy_targets, key=lambda x: x[1].current_def)
        total = 8.0
        for idx, em in sorted_en[:2]:
            total += em.current_def * 0.8 + em.base_atk * 0.7
            em.current_def = 0
            handle_monster_death(opponent, player, idx, em, m, p_stats)
        return min(32.0, total)
    if "مدفع الأيونات التدميري" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            rem = max(0, 50 - em.current_def)
            val = deal_spell_or_skill_damage(idx, em, 50, m, opponent, player, p_stats)
            if rem > 0:
                opponent.take_leader_dmg(rem)
                val += rem * 0.65
            return min(34.0, val)
        else:
            opponent.take_leader_dmg(50)
            return 28.0
    if "درع القبة الحديدية" in act:
        player.team_immune_turns = max(player.team_immune_turns, 1)
        return 26.0
    if "استحضار المقابر" in act:
        spawned = 0
        for w_slot in (0, 1, 4, 5):
            if player.board[w_slot] is None and spawned < 2:
                ghost_token = SimMonster({
                    "id": 999, "name": "طيف مستحضر", "element": "أشباح", "role": "مهاجم",
                    "tier_num": 2, "cost": 2, "atk": 12, "def": 14,
                    "parsed_passives": ["جسد أثيري: منيع 100% ضد ضربات الوحوش العادية (يتأثر بالسحر والضياء فقط)"],
                    "parsed_actives": []
                })
                ghost_token.can_attack = True
                player.board[w_slot] = ghost_token
                spawned += 1
        return 12.0 + spawned * 10.0
    if "سلب الأرواح" in act:
        total = 10.0
        for idx, em in enemy_targets:
            lost_atk = em.base_atk // 2
            lost_def = em.current_def // 2
            em.base_atk = max(1, em.base_atk - lost_atk)
            em.current_def = max(1, em.current_def - lost_def)
            total += lost_atk * 0.7 + lost_def * 0.5
        return min(34.0, total)
    if "انفجار المانا الأعظم" in act:
        total = 0.0
        for idx, em in enemy_targets[:3]:
            total += deal_spell_or_skill_damage(idx, em, 14, m, opponent, player, p_stats)
        opponent.take_leader_dmg(15)
        return max(20.0, total + 9.0)
    if "توقف الزمن الأركيني" in act:
        for idx, am in ally_targets:
            am.can_attack = True
        opponent.deny_draw_sac_turns = max(opponent.deny_draw_sac_turns, 1)
        return 32.0

    # المهارات العادية (تكلفة 1 باور)
    if "قذيفة نارية" in act or "صاروخ موجه" in act:
        if enemy_targets:
            idx, em = max(enemy_targets, key=lambda x: x[1].base_atk)
            return deal_spell_or_skill_damage(idx, em, 12, m, opponent, player, p_stats)
        return 8.0
    if "انفجار الجمر" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            total = deal_spell_or_skill_damage(idx, em, 8, m, opponent, player, p_stats)
            for adj in (idx - 1, idx + 1):
                if 0 <= adj < 6 and opponent.board[adj] and opponent.board[adj].is_alive():
                    total += deal_spell_or_skill_damage(adj, opponent.board[adj], 8, m, opponent, player, p_stats)
            return total
        return 8.0
    if "دوامة مائية" in act or "عصفة هوائية" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            em.can_attack = False
            return em.base_atk * 0.75 + 4.5
        return 7.0
    if "تيار شفاء" in act:
        damaged = [am for _, am in ally_targets if am.current_def < am.base_def]
        if damaged:
            t = min(damaged, key=lambda x: x.current_def)
            healed = min(20, t.base_def + 8 - t.current_def)
            t.current_def += healed
            return healed * 0.85
        player.hp = min(500, player.hp + 15)
        return 10.0
    if "رمح الجليد" in act:
        if enemy_targets:
            idx, em = max(enemy_targets, key=lambda x: x[1].base_atk)
            em.frozen_turns = max(em.frozen_turns, 1)
            return deal_spell_or_skill_damage(idx, em, 10, m, opponent, player, p_stats) + em.base_atk * 0.55
        return 10.0
    if "جدار ثلجي" in act:
        m.current_def += 20
        return 20 * 0.75
    if "صعقة مفاجئة" in act:
        target = opponent.board[slot] if (slot < 6 and opponent.board[slot]) else (enemy_targets[0][1] if enemy_targets else None)
        t_idx = slot if (slot < 6 and opponent.board[slot]) else (enemy_targets[0][0] if enemy_targets else -1)
        if target and target.is_alive():
            return deal_spell_or_skill_damage(t_idx, target, 14, m, opponent, player, p_stats)
        return 9.0
    if "شحن طاقة" in act:
        attackers = [am for _, am in ally_targets if am.can_attack]
        target = attackers[0] if attackers else m
        target.bonus_atk += 8
        return 8 * 1.18
    if "شفرة الرياح" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            em.current_def -= 11
            if not em.is_alive():
                handle_monster_death(opponent, player, idx, em, m, p_stats)
            return 11 * 1.12
        return 9.0
    if "انهيار صخري" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            return deal_spell_or_skill_damage(idx, em, 10, m, opponent, player, p_stats) + 3.5
        return 8.5
    if "درع الحصن" in act:
        target = player.board[2] if (player.board[2] and player.board[2].is_alive()) else m
        target.current_def += 25
        return 25 * 0.78
    if "بصقة حمضية" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            em.base_def = max(1, em.base_def - 6)
            return deal_spell_or_skill_damage(idx, em, 12, m, opponent, player, p_stats)
        return 9.5
    if "تضميد الجراح" in act:
        m.current_def += 18
        return 18 * 0.80
    if "إشعاع الشفاء" in act:
        player.hp = min(500, player.hp + 30)
        m.current_def += 8
        return 30 * 0.55 + 8 * 0.75
    if "سهم النور" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            em.bonus_atk = 0
            return deal_spell_or_skill_damage(idx, em, 12, m, opponent, player, p_stats) + 3.5
        return 9.5
    if "ضربة الظلال" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            player.hp = min(500, player.hp + 5)
            return deal_spell_or_skill_damage(idx, em, 14, m, opponent, player, p_stats) + 3.5
        return 10.0
    if "قفل روحي" in act:
        with_act = [(idx, em) for idx, em in enemy_targets if em.actives]
        target = with_act[0][1] if with_act else (enemy_targets[0][1] if enemy_targets else None)
        if target:
            target.silenced_turns = max(target.silenced_turns, 2)
            return 13.5
        return 7.5
    if "إعادة تهيئة" in act:
        m.burn_dmg = 0
        m.poison_dmg = 0
        m.current_def += 20
        return 20 * 0.82
    if "لمسة البرزخ" in act:
        if enemy_targets:
            idx, em = enemy_targets[0]
            em.current_def -= 8
            if not em.is_alive():
                handle_monster_death(opponent, player, idx, em, m, p_stats)
            return 8 * 1.25
        return 8.5
    if "فزع روحي" in act:
        if enemy_targets:
            idx, em = max(enemy_targets, key=lambda x: x[1].base_atk)
            em.frozen_turns = max(em.frozen_turns, 1)
            return em.base_atk * 0.95 + 4.0
        return 9.0
    if "سهم الأركين" in act:
        if enemy_targets:
            idx, em = max(enemy_targets, key=lambda x: x[1].base_atk)
            return deal_spell_or_skill_damage(idx, em, 15, m, opponent, player, p_stats)
        return 11.0
    if "تحويل العناصر" in act:
        if enemy_targets:
            idx, em = max(enemy_targets, key=lambda x: x[1].base_atk)
            em.weakened_turns = max(em.weakened_turns, 2)
            return (em.base_atk * 0.35 + em.current_def * 0.25) + 4.5
        return 8.5

    return 9.0


def compute_attack_power(m: SimMonster, opp_m: SimMonster, active_env, p_stats):
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
        atk = int(atk * 0.7)

    if opp_m and opp_m.is_alive():
        for p in opp_m.passives:
            if "برودة خفيفة تقلل هجوم المقابل بـ 3" in p:
                atk = max(0, atk - 3)
                record_passive_val(p_stats, p, 3 * 1.0)
            elif "هالة رعب تنقص 3 هجوم من المقابل" in p:
                atk = max(0, atk - 3)
                record_passive_val(p_stats, p, 3 * 1.0)
            elif "هيبة الموتى" in p:
                atk = max(0, atk - 2)
                record_passive_val(p_stats, p, 2 * 1.0)
            elif "رياح عاتية تشتت هجمات الخصم وتقللها بـ 8" in p:
                atk = max(0, atk - 8)
                record_passive_val(p_stats, p, 8 * 1.0)

        # نور مبدد للأشباح: يلحق ضرراً مضاعفاً بالأشباح والظلام
        if m.has_passive_substr("نور مبدد للأشباح") and opp_m.element in ("أشباح", "ظلام"):
            record_passive_val(p_stats, m.has_passive_substr("نور مبدد للأشباح"), atk * 1.1)
            atk *= 2

    if m.has_passive_substr("نظام توجيه يزيد الدقة"):
        bonus = max(2, int(atk * 0.12))
        atk += bonus
        record_passive_val(p_stats, m.has_passive_substr("نظام توجيه يزيد الدقة"), bonus * 1.15)

    return atk


def apply_monster_hit(attacker_slot: int, m: SimMonster, def_slot: int, opp_m: SimMonster, atk_dmg: int, player: SimPlayer, opponent: SimPlayer, p_stats):
    if opponent.team_immune_turns > 0:
        return

    # فحص جسد أثيري للأشباح: منيع ضد الوحوش العادية (يتأثر بالسحر والضياء والأشباح فقط)
    if opp_m.has_passive_substr("جسد أثيري"):
        if m.element not in ("ضياء", "سحر", "أشباح") and not m.has_passive_substr("عبور حركي"):
            # يمتص 80% من الضربة الفيزيائية العادية بسبب جسده الأثيري!
            prevented = int(atk_dmg * 0.80)
            atk_dmg = max(1, atk_dmg - prevented)
            record_passive_val(p_stats, opp_m.has_passive_substr("جسد أثيري"), prevented * 0.85)

    # فحص المراوغة
    if not m.has_passive_substr("نظام توجيه"):
        if opp_m.has_passive_substr("مراوغة خفيفة") and random.random() < 0.20:
            record_passive_val(p_stats, opp_m.has_passive_substr("مراوغة خفيفة"), atk_dmg * 0.85)
            return
        if opp_m.has_passive_substr("خفة الحركة") and random.random() < 0.14:
            record_passive_val(p_stats, opp_m.has_passive_substr("خفة الحركة"), atk_dmg * 0.85)
            return
        if opp_m.has_passive_substr("تخفي ليلي") and random.random() < 0.16:
            record_passive_val(p_stats, opp_m.has_passive_substr("تخفي ليلي"), atk_dmg * 0.85)
            return

    # فحص تقليل الضرر والدروع
    ignore_armor = m.has_passive_substr("عبور حركي يتجاهل الدروع") is not None
    if ignore_armor:
        record_passive_val(p_stats, m.has_passive_substr("عبور حركي يتجاهل الدروع"), 4.5)
    else:
        for p in opp_m.passives:
            if "مرونة مائية تقلل الضرر الوارد بـ 3" in p:
                red = min(atk_dmg, 3)
                atk_dmg -= red
                record_passive_val(p_stats, p, red * 0.85)
            elif "لهب وقائي يمتص 4 ضرر" in p:
                red = min(atk_dmg, 4)
                atk_dmg -= red
                record_passive_val(p_stats, p, red * 0.85)
            elif "صلابة صخرية تقلل الضرر بـ 4" in p:
                red = min(atk_dmg, 4)
                atk_dmg -= red
                record_passive_val(p_stats, p, red * 0.85)
            elif "درع فولاذي يقلل الضرر بـ 5" in p:
                red = min(atk_dmg, 5)
                atk_dmg -= red
                record_passive_val(p_stats, p, red * 0.85)
            elif "درع مانا يمتص 6 ضرر" in p:
                red = min(atk_dmg, 6)
                atk_dmg -= red
                record_passive_val(p_stats, p, red * 0.85)
            elif "حصانة ضد الصدمات" in p:
                red = min(atk_dmg, 5)
                atk_dmg -= red
                record_passive_val(p_stats, p, red * 0.85 + 1.5)

    # فحص الحصانات العنصرية ضد الهجوم المباشر
    elem_immunity_map = {
        "نار": ("حصانة تامة ضد النار", "حصانة ضد النيران"),
        "جليد": ("حصانة تامة ضد الجليد",),
        "كهرباء": ("تأريض تام",),
        "رياح": ("مقاومة الرياح",),
        "أرض": ("حصانة ضد الزلازل",),
        "ضياء": ("حصانة ضد النور",),
        "ظلام": ("حصانة ضد الظلام",),
        "سحر": ("حصانة ضد السحر", "درع كهرومغناطيسي"),
    }
    for imm_key in elem_immunity_map.get(m.element, ()):
        p_imm = opp_m.has_passive_substr(imm_key)
        if p_imm:
            red = int(atk_dmg * 0.45)
            atk_dmg -= red
            record_passive_val(p_stats, p_imm, red * 0.85 + 2.5)

    if atk_dmg > 0:
        opp_m.current_def -= atk_dmg

        # امتصاص حياة / جذور ممتصة
        if m.has_passive_substr("امتصاص حياة"):
            heal = max(1, int(atk_dmg * 0.20))
            m.current_def += heal
            record_passive_val(p_stats, m.has_passive_substr("امتصاص حياة"), heal * 0.85)
        if m.has_passive_substr("جذور ممتصة تمتص 50%"):
            gain = max(2, int(atk_dmg * 0.50))
            m.current_def += gain
            record_passive_val(p_stats, m.has_passive_substr("جذور ممتصة تمتص 50%"), gain * 0.85)

        # صعق متسلسل: 30% من ضرر هجومه يقفز للخانة المجاورة
        if m.has_passive_substr("صعق متسلسل"):
            splash = max(2, int(atk_dmg * 0.30))
            for adj in (def_slot - 1, def_slot + 1):
                if 0 <= adj < 6 and opponent.board[adj] and opponent.board[adj].is_alive():
                    opponent.board[adj].current_def -= splash
                    record_passive_val(p_stats, m.has_passive_substr("صعق متسلسل"), splash * 0.95)
                    if not opponent.board[adj].is_alive():
                        handle_monster_death(opponent, player, adj, opponent.board[adj], m, p_stats)
                    break

        # اللمسة القاتلة (Venom)
        if m.has_passive_substr("اللمسة القاتلة (Venom)") and opp_m.is_alive():
            if not opp_m.has_passive_substr("حصانة ضد السم") and not opp_m.has_passive_substr("هالة طهارة"):
                ven_val = min(30.0, opp_m.current_def * 0.85 + opp_m.base_atk * 0.5)
                opp_m.current_def = 0
                record_passive_val(p_stats, m.has_passive_substr("اللمسة القاتلة (Venom)"), ven_val)
            else:
                for kp in opp_m.passives:
                    if "حصانة ضد السم" in kp or "هالة طهارة" in kp:
                        record_passive_val(p_stats, kp, opp_m.current_def * 0.6)

    # ردود الفعل الدفاعية (الأشواك، الشرارة، التجميد التلقائي)
    if opp_m.has_passive_substr("أشواك دفاعية تلحق 3 ضرر"):
        m.current_def -= 3
        record_passive_val(p_stats, opp_m.has_passive_substr("أشواك دفاعية تلحق 3 ضرر"), 3 * 0.95)
    if opp_m.has_passive_substr("شرارة ارتدادية تلحق 3 ضرر"):
        m.current_def -= 3
        record_passive_val(p_stats, opp_m.has_passive_substr("شرارة ارتدادية تلحق 3 ضرر"), 3 * 0.95)
    if opp_m.has_passive_substr("تجميد تلقائي للمهاجم لدور واحد") and m.is_alive():
        m.frozen_turns = max(m.frozen_turns, 1)
        record_passive_val(p_stats, opp_m.has_passive_substr("تجميد تلقائي للمهاجم لدور واحد"), m.base_atk * 0.95)

    if not opp_m.is_alive():
        handle_monster_death(opponent, player, def_slot, opp_m, m, p_stats)
    if not m.is_alive():
        handle_monster_death(player, opponent, attacker_slot, m, opp_m, p_stats)


def execute_combat_phase(player: SimPlayer, opponent: SimPlayer, active_env, p_stats):
    # الهجوم عبر الخانات الـ 6 المتقابلة (0..5): إذا كانت الخانة المقابلة فارغة يضرب القائد (500 HP) مباشرة
    for slot in (0, 1, 2, 3, 4, 5):
        m = player.board[slot]
        if not m or not m.is_alive() or not m.can_attack or m.frozen_turns > 0:
            continue

        opp_m = opponent.board[slot]
        atk_dmg = compute_attack_power(m, opp_m, active_env, p_stats)
        if atk_dmg <= 0:
            continue

        if opp_m and opp_m.is_alive():
            apply_monster_hit(slot, m, slot, opp_m, atk_dmg, player, opponent, p_stats)
        else:
            # فحص إعصار دفاعي في ساحة الخصم يحجب الهجوم المباشر عن القائد
            blocker_slot = None
            for b_idx, bm in enumerate(opponent.board):
                if bm and bm.is_alive() and bm.has_passive_substr("إعصار دفاعي يحجب هجوم الخصم المباشر"):
                    blocker_slot = b_idx
                    break
            if blocker_slot is not None:
                bm = opponent.board[blocker_slot]
                record_passive_val(p_stats, bm.has_passive_substr("إعصار دفاعي يحجب هجوم الخصم المباشر"), min(atk_dmg, bm.current_def) * 0.75)
                apply_monster_hit(slot, m, blocker_slot, bm, atk_dmg, player, opponent, p_stats)
            else:
                if opponent.team_immune_turns <= 0:
                    opponent.take_leader_dmg(atk_dmg)
                    # سرقة مانا عند توجيه ضربة مباشرة للقائد
                    if m.has_passive_substr("سرقة مانا: سلب 1 باور"):
                        player.power += 1
                        record_passive_val(p_stats, m.has_passive_substr("سرقة مانا: سلب 1 باور"), 11.0)


def simulate_batch(args):
    batch_size, seed, all_monsters = args
    random.seed(seed)

    # إحصائيات الوحوش: id -> [played_games, wins_when_played, times_summoned]
    m_stats = {m["id"]: [0, 0, 0] for m in all_monsters}
    # إحصائيات الباسيف: passive_str -> [total_points, trigger_or_summon_count]
    p_stats = defaultdict(lambda: [0.0, 0])
    # إحصائيات المهارات النشطة: active_str -> [total_points, trigger_count]
    a_stats = defaultdict(lambda: [0.0, 0])
    # عدد مرات استدعاء كل باسيف وكل مهارة لحساب المتوسط لكل استدعاء
    p_summons = defaultdict(int)
    a_summons = defaultdict(int)

    all_elements = list(ENVIRONMENT_MATRIX.keys())
    spell_prototypes = [
        {"card_type": "spell", "cost": 2, "stype": "direct", "power": 22},
        {"card_type": "spell", "cost": 3, "stype": "aoe", "power": 14},
        {"card_type": "spell", "cost": 2, "stype": "heal", "power": 70},
        {"card_type": "spell", "cost": 2, "stype": "freeze", "power": 10},
        {"card_type": "spell", "cost": 2, "stype": "draw_dmg", "power": 12},
        {"card_type": "spell", "cost": 2, "stype": "buff_def", "power": 20},
        {"card_type": "spell", "cost": 1, "stype": "energy", "power": 2},
        {"card_type": "spell", "cost": 3, "stype": "drain", "power": 25},
        {"card_type": "spell", "cost": 4, "stype": "execute", "power": 999},
        {"card_type": "spell", "cost": 3, "stype": "direct", "power": 28},
        {"card_type": "spell", "cost": 3, "stype": "mana_theft", "power": 1},
        {"card_type": "spell", "cost": 5, "stype": "arcane_blast", "power": 35},
    ]

    for _ in range(batch_size):
        # بناء مجموعة 30 كارت مطابقة لـ Unity تماماً: 20 وحش + 6 بطاقات سحر + 4 بطاقات بيئة
        m1 = [SimMonster(d) for d in random.sample(all_monsters, 20)]
        s1 = [dict( random.choice(spell_prototypes) ) for _ in range(6)]
        e1 = [{"card_type": "env", "cost": 2, "element": random.choice(all_elements)} for _ in range(4)]

        m2 = [SimMonster(d) for d in random.sample(all_monsters, 20)]
        s2 = [dict( random.choice(spell_prototypes) ) for _ in range(6)]
        e2 = [{"card_type": "env", "cost": 2, "element": random.choice(all_elements)} for _ in range(4)]

        p1 = SimPlayer(m1 + s1 + e1)
        p2 = SimPlayer(m2 + s2 + e2)

        for _ in range(6):
            p1.draw()
            p2.draw()

        active_env = None  # (element, level)
        env_turns_left = 0

        turn = 0
        while p1.hp > 0 and p2.hp > 0 and turn < 75:
            turn += 1
            active_p = p1 if (turn % 2 == 1) else p2
            other_p = p2 if (turn % 2 == 1) else p1

            if other_p.team_immune_turns > 0:
                other_p.team_immune_turns -= 1

            # بداية الدور
            active_p.power = active_p.mana_cap
            if active_p.deny_draw_sac_turns > 0:
                active_p.deny_draw_sac_turns -= 1
            else:
                active_p.draw()
                # التضحية التكتيكية لرفع المانا
                if active_p.mana_cap < 8 and len(active_p.hand) >= 2:
                    # يفضل التضحية بكارت غير قابل للعب حالياً مع الاحتفاظ بوحش حسم إن أمكن
                    unplayable = [c for c in active_p.hand if (c.cost if isinstance(c, SimMonster) else c["cost"]) > active_p.power + 1]
                    sac = unplayable[0] if unplayable else active_p.hand[-1]
                    active_p.hand.remove(sac)
                    active_p.mana_cap += 1
                    active_p.power += 1

            # تجهيز الوحوش للهجوم وتفعيل باسيف بداية الدور
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
                    if active_env is not None and active_env[0] == c["element"]:
                        new_lvl = min(3, active_env[1] + 1)
                        active_env = (c["element"], new_lvl)
                        env_turns_left = 4 if new_lvl == 1 else (2 if new_lvl == 2 else 1)
                    else:
                        active_env = (c["element"], 1)
                        env_turns_left = 4
                    break

            # 2. إلقاء بطاقات السحر الـ 12 وتفعيل تفاعلات وحوش السحر والحصانات
            for c in list(active_p.hand):
                if isinstance(c, dict) and c.get("card_type") == "spell" and c["cost"] <= active_p.power:
                    stype = c["stype"]
                    if stype == "heal" and active_p.hp > 400:
                        continue
                    active_p.power -= c["cost"]
                    active_p.hand.remove(c)

                    # تفاعل وحوش السحر (صدى سحري / مضاعف التعاويذ / استعادة المانا)
                    spell_repeats = 1
                    for bm in active_p.board:
                        if bm and bm.is_alive():
                            if bm.has_passive_substr("صدى سحري"):
                                bm.bonus_atk += 3
                                record_passive_val(p_stats, bm.has_passive_substr("صدى سحري"), 3 * 1.18)
                            if bm.has_passive_substr("مضاعف التعاويذ") and spell_repeats == 1:
                                spell_repeats = 2
                                record_passive_val(p_stats, bm.has_passive_substr("مضاعف التعاويذ"), 12.0)
                            if bm.has_passive_substr("استعادة المانا"):
                                active_p.power += 1
                                record_passive_val(p_stats, bm.has_passive_substr("استعادة المانا"), 10.0)

                    for _ in range(spell_repeats):
                        en_alive = [(idx, em) for idx, em in enumerate(other_p.board) if em and em.is_alive()]
                        if stype == "direct":
                            if en_alive:
                                idx, em = max(en_alive, key=lambda x: x[1].base_atk)
                                deal_spell_or_skill_damage(idx, em, c["power"], None, other_p, active_p, p_stats)
                            else:
                                other_p.take_leader_dmg(c["power"])
                        elif stype == "aoe":
                            if en_alive:
                                for idx, em in en_alive:
                                    deal_spell_or_skill_damage(idx, em, c["power"], None, other_p, active_p, p_stats)
                            else:
                                other_p.take_leader_dmg(c["power"])
                        elif stype == "heal":
                            active_p.hp = min(500, active_p.hp + c["power"])
                            for bm in active_p.board:
                                if bm and bm.is_alive():
                                    bm.current_def = min(bm.base_def + 15, bm.current_def + 12)
                        elif stype == "freeze":
                            for idx, em in en_alive:
                                em.frozen_turns = max(em.frozen_turns, 1)
                                deal_spell_or_skill_damage(idx, em, c["power"], None, other_p, active_p, p_stats)
                        elif stype == "draw_dmg":
                            active_p.draw()
                            active_p.draw()
                            other_p.take_leader_dmg(c["power"])
                        elif stype == "buff_def":
                            for bm in active_p.board:
                                if bm and bm.is_alive():
                                    bm.current_def += c["power"]
                        elif stype == "energy":
                            active_p.power += 2
                            active_p.draw()
                        elif stype == "drain":
                            other_p.take_leader_dmg(c["power"])
                            active_p.hp = min(500, active_p.hp + c["power"])
                        elif stype == "execute":
                            if en_alive:
                                idx, em = max(en_alive, key=lambda x: x[1].base_atk * 2 + x[1].current_def)
                                if em.has_passive_substr("درع كهرومغناطيسي") or em.has_passive_substr("حصانة ضد السحر"):
                                    for kp in em.passives:
                                        if "درع كهرومغناطيسي" in kp or "حصانة ضد السحر" in kp:
                                            record_passive_val(p_stats, kp, em.current_def * 0.85 + 10.0)
                                else:
                                    em.current_def = 0
                                    handle_monster_death(other_p, active_p, idx, em, None, p_stats)
                            else:
                                other_p.take_leader_dmg(30)
                        elif stype == "mana_theft":
                            if other_p.mana_cap > 1:
                                other_p.mana_cap -= 1
                            if active_p.mana_cap < 8:
                                active_p.mana_cap += 1
                                active_p.power += 1
                        elif stype == "arcane_blast":
                            if en_alive:
                                dmg_each = max(8, c["power"] // (len(en_alive) + 1))
                                for idx, em in en_alive:
                                    deal_spell_or_skill_damage(idx, em, dmg_each, None, other_p, active_p, p_stats)
                                other_p.take_leader_dmg(dmg_each)
                            else:
                                other_p.take_leader_dmg(c["power"])

            # 3. استدعاء الوحوش في الخانات الـ 6
            monster_cards = [c for c in active_p.hand if isinstance(c, SimMonster)]
            monster_cards.sort(key=lambda c: c.cost, reverse=True)
            for card in monster_cards:
                if card.cost <= active_p.power:
                    # اختيار الخانة الأنسب من الـ 6 خانات (0,1 جناح يسار | 2,3 وسط | 4,5 جناح يمين)
                    if card.has_passive_substr("في الأجنحة") or card.role == "مهاجم":
                        slot_order = (0, 1, 4, 5, 2, 3)
                    elif card.has_passive_substr("في الوسط") or card.has_passive_substr("دروع جرانيتية") or card.role == "درع صرف":
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
                        active_p.played_monsters.add(card.id)
                        m_stats[card.id][2] += 1
                        for p_str in card.passives:
                            p_summons[p_str] += 1
                        for a_str in card.actives:
                            a_summons[a_str] += 1
                        apply_on_summon_passives(active_p, other_p, chosen_slot, card, p_stats)

            # 4. تفعيل المهارات النشطة للوحوش
            execute_active_skills(active_p, other_p, p_stats, a_stats)

            # 5. تنفيذ مرحلة القتال عبر الـ 6 خانات مع تأثير البيئة النشطة
            execute_combat_phase(active_p, other_p, active_env, p_stats)

            # 6. تقليل عداد جولات البيئة في نهاية الدور
            if active_env is not None:
                env_turns_left -= 1
                if env_turns_left <= 0:
                    active_env = None

        p1_won = p1.hp >= p2.hp
        for mid in p1.played_monsters:
            m_stats[mid][0] += 1
            if p1_won:
                m_stats[mid][1] += 1
        for mid in p2.played_monsters:
            m_stats[mid][0] += 1
            if not p1_won:
                m_stats[mid][1] += 1

    return m_stats, dict(p_stats), dict(a_stats), dict(p_summons), dict(a_summons)


def run_100k_evaluation(total_matches=100000, progress_callback=None):
    t0 = time.time()
    with open(JSON_PATH, "r", encoding="utf-8") as f:
        all_monsters = json.load(f)

    for m in all_monsters:
        m["parsed_passives"] = split_abilities(m["passives"])
        m["parsed_actives"] = split_abilities(m["active_skill"])

    workers = max(1, min(cpu_count(), 12))
    chunks = workers * 4
    batch_size = total_matches // chunks
    remainder = total_matches % chunks

    tasks = []
    for i in range(chunks):
        b = batch_size + (1 if i < remainder else 0)
        if b > 0:
            tasks.append((b, 1000 + i * 97, all_monsters))

    total_m_stats = {m["id"]: [0, 0, 0] for m in all_monsters}
    total_p_stats = defaultdict(lambda: [0.0, 0])
    total_a_stats = defaultdict(lambda: [0.0, 0])
    total_p_summons = defaultdict(int)
    total_a_summons = defaultdict(int)

    completed_matches = 0
    with Pool(processes=workers) as pool:
        for idx, (m_st, p_st, a_st, p_sum, a_sum) in enumerate(pool.imap_unordered(simulate_batch, tasks)):
            completed_matches += tasks[idx][0]
            for mid, vals in m_st.items():
                total_m_stats[mid][0] += vals[0]
                total_m_stats[mid][1] += vals[1]
                total_m_stats[mid][2] += vals[2]
            for k, vals in p_st.items():
                total_p_stats[k][0] += vals[0]
                total_p_stats[k][1] += vals[1]
            for k, vals in a_st.items():
                total_a_stats[k][0] += vals[0]
                total_a_stats[k][1] += vals[1]
            for k, c in p_sum.items():
                total_p_summons[k] += c
            for k, c in a_sum.items():
                total_a_summons[k] += c

            if progress_callback:
                progress_callback(completed_matches, total_matches)

    elapsed = time.time() - t0

    # حساب نسبة الفوز الخام لكل وحش ثم معايرتها ضمن نفس فئة التكلفة (Cost-Normalized Win Rate)
    # لمنع انحياز نهاية المباراة (حيث الوحوش بتكلفة 8 لا تنزل إلا بعد بقاء اللاعب حياً للـ Late Game)
    raw_win_rates = {}
    cost_groups = defaultdict(list)
    for m in all_monsters:
        mid = m["id"]
        played, wins, _ = total_m_stats[mid]
        wr = (wins / played * 100.0) if played > 0 else 50.0
        raw_win_rates[mid] = wr
        cost_groups[m["cost"]].append(wr)

    cost_avg_wr = {c: (sum(vals) / len(vals)) for c, vals in cost_groups.items()}

    monster_win_rates = {}
    for m in all_monsters:
        mid = m["id"]
        c = m["cost"]
        # معايرة نسبة الفوز حول 50% مقارنة بمتوسط الوحوش من نفس تكلفة الباور
        norm_wr = 50.0 + (raw_win_rates[mid] - cost_avg_wr[c])
        monster_win_rates[mid] = round(max(30.0, min(75.0, norm_wr)), 2)

    # حساب متوسط نسبة الفوز المعيارية للوحوش الحاملة لكل باسيف ومهارة نشطة
    passive_wr_map = defaultdict(list)
    active_wr_map = defaultdict(list)
    for m in all_monsters:
        wr = monster_win_rates[m["id"]]
        for p in m["parsed_passives"]:
            passive_wr_map[p].append(wr)
        for a in m["parsed_actives"]:
            active_wr_map[a].append(wr)

    # حساب التقييم النقطي النهائي لكل باسيف (Passive Score) ولكل مهارة (Active Score)
    passive_scores = {}
    passive_details = []
    for p, wr_list in passive_wr_map.items():
        pts_sum, trig_cnt = total_p_stats[p]
        summons = max(1, total_p_summons[p])
        avg_combat_pts = pts_sum / summons
        avg_wr = sum(wr_list) / len(wr_list)
        # دمج الأثر القتالي المقاس في المعركة مع انحراف نسبة الفوز المعيارية عن 50%
        wr_adjustment = (avg_wr - 50.0) * 1.10
        empirical_score = round(max(4.0, min(35.0, avg_combat_pts + wr_adjustment)), 1)
        passive_scores[p] = empirical_score
        passive_details.append({
            "name": p,
            "type": "ميزة دائمة (Passive)",
            "triggers": trig_cnt,
            "summons": summons,
            "avg_combat_pts": round(avg_combat_pts, 2),
            "avg_wr": round(avg_wr, 2),
            "score": empirical_score
        })

    active_scores = {}
    active_details = []
    for a, wr_list in active_wr_map.items():
        pts_sum, trig_cnt = total_a_stats[a]
        summons = max(1, total_a_summons[a])
        # المهارة النشطة تقاس بمتوسط أثرها عند التفعيل مضروباً في معدل تفعيلها الفعلي بالمباراة
        avg_per_trig = (pts_sum / max(1, trig_cnt))
        activation_rate = min(1.30, max(0.55, (trig_cnt / summons) * 1.35))
        avg_combat_pts = avg_per_trig * activation_rate
        avg_wr = sum(wr_list) / len(wr_list)
        wr_adjustment = (avg_wr - 50.0) * 1.10
        empirical_score = round(max(6.0, min(36.0, avg_combat_pts + wr_adjustment)), 1)
        active_scores[a] = empirical_score
        active_details.append({
            "name": a,
            "type": "مهارة نشطة (Active Skill)",
            "triggers": trig_cnt,
            "summons": summons,
            "avg_combat_pts": round(avg_combat_pts, 2),
            "avg_wr": round(avg_wr, 2),
            "score": empirical_score
        })

    return {
        "total_matches": total_matches,
        "elapsed": round(elapsed, 2),
        "all_monsters": all_monsters,
        "monster_win_rates": monster_win_rates,
        "monster_stats": total_m_stats,
        "passive_scores": passive_scores,
        "active_scores": active_scores,
        "passive_details": sorted(passive_details, key=lambda x: x["score"], reverse=True),
        "active_details": sorted(active_details, key=lambda x: x["score"], reverse=True),
    }


def update_excel_files(eval_result):
    import openpyxl
    from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
    from openpyxl.utils import get_column_letter

    passive_scores = eval_result["passive_scores"]
    active_scores = eval_result["active_scores"]
    monster_win_rates = eval_result["monster_win_rates"]

    for excel_path in (EXCEL_AR_PATH, EXCEL_EN_PATH):
        if not os.path.exists(excel_path):
            continue

        wb = openpyxl.load_workbook(excel_path)
        ws = wb["🐉 الوحوش (150 وحش)"]

        # تنسيقات احترافية
        header_fill = PatternFill(start_color="1B263B", end_color="1B263B", fill_type="solid")
        sim_header_fill = PatternFill(start_color="3C096C", end_color="3C096C", fill_type="solid")
        formula_header_fill = PatternFill(start_color="0F4C5C", end_color="0F4C5C", fill_type="solid")
        header_font = Font(name="Calibri", size=11, bold=True, color="FFFFFF")

        data_font = Font(name="Calibri", size=11, bold=False)
        bold_font = Font(name="Calibri", size=11, bold=True)
        sim_fill = PatternFill(start_color="F3E8FF", end_color="F3E8FF", fill_type="solid")
        budget_fill = PatternFill(start_color="E0FBFC", end_color="E0FBFC", fill_type="solid")
        balance_ok_fill = PatternFill(start_color="D8F3DC", end_color="D8F3DC", fill_type="solid")
        balance_op_fill = PatternFill(start_color="FFD6D6", end_color="FFD6D6", fill_type="solid")
        balance_up_fill = PatternFill(start_color="FFF3B0", end_color="FFF3B0", fill_type="solid")

        thin_border = Border(
            left=Side(style="thin", color="CCCCCC"),
            right=Side(style="thin", color="CCCCCC"),
            top=Side(style="thin", color="CCCCCC"),
            bottom=Side(style="thin", color="CCCCCC"),
        )
        center_align = Alignment(horizontal="center", vertical="center", wrap_text=True)

        # تحديث رؤوس الأعمدة من العمود 14 إلى 20
        new_headers = {
            14: ("تقييم الباسيف (محاكي 100 ألف)", sim_header_fill),
            15: ("تقييم المهارة النشطة (محاكي 100 ألف)", sim_header_fill),
            16: ("قوة الوحش الفعلية الكلية (Actual Power)", formula_header_fill),
            17: ("الميزانية المستحقة للتكلفة (16 + Cost×14)", formula_header_fill),
            18: ("مؤشر توازن البطاقة (الهدف ≈ 50)", formula_header_fill),
            19: ("نسبة الفوز في 100 ألف جولة (الهدف ≈ 50%)", sim_header_fill),
            20: ("حكم التوازن النهائي (Balance Verdict)", header_fill),
        }

        for col_idx, (title, fill) in new_headers.items():
            cell = ws.cell(1, col_idx, title)
            cell.fill = fill
            cell.font = header_font
            cell.alignment = center_align
            cell.border = thin_border

        # تحديث صفوف الوحوش الـ 150 (الصفوف 2 إلى 151)
        for r in range(2, 152):
            mid = ws.cell(r, 1).value
            # توحيد اسم الوحش ليكون رقمه فقط (1، 2، 3... 150) بناءً على طلب المصمم
            ws.cell(r, 2, str(mid))
            passives_str = str(ws.cell(r, 11).value or "")
            active_str = str(ws.cell(r, 12).value or "")

            p_list = split_abilities(passives_str)
            a_list = split_abilities(active_str)

            p_score = round(sum(passive_scores.get(p, 0.0) for p in p_list), 1)
            a_score = round(sum(active_scores.get(a, 0.0) for a in a_list), 1)
            wr = monster_win_rates.get(mid, 50.0)

            # العمود N (14): تقييم الباسيف المحسوب من محاكي 100 ألف جولة
            c_n = ws.cell(r, 14, p_score)
            c_n.fill = sim_fill
            c_n.font = bold_font
            c_n.alignment = center_align
            c_n.border = thin_border

            # العمود O (15): تقييم المهارة النشطة المحسوب من محاكي 100 ألف جولة
            c_o = ws.cell(r, 15, a_score)
            c_o.fill = sim_fill
            c_o.font = bold_font
            c_o.alignment = center_align
            c_o.border = thin_border

            # العمود P (16): قوة الوحش الفعلية الكلية = الهجوم بوزنه + الدفاع بوزنه حسب الدور + تقييم الباسيف + تقييم المهارة
            c_p = ws.cell(
                r, 16,
                f'=ROUND((I{r}*1.18) + (J{r}*IF(E{r}="درع صرف", 0.74, 0.90)) + N{r} + O{r}, 1)'
            )
            c_p.fill = budget_fill
            c_p.font = bold_font
            c_p.alignment = center_align
            c_p.border = thin_border

            # العمود Q (17): الميزانية المستحقة حسب التكلفة = 16 (ثمن الكارت والخانة) + (نقاط الاستدعاء × 14)
            c_q = ws.cell(r, 17, f"=16 + (H{r}*14)")
            c_q.fill = budget_fill
            c_q.font = bold_font
            c_q.alignment = center_align
            c_q.border = thin_border

            # العمود R (18): مؤشر توازن البطاقة على أساس 50 (ليتطابق بصرياً مع نسبة الفوز 50%)
            c_r = ws.cell(r, 18, f"=ROUND((P{r}/Q{r})*50, 1)")
            c_r.font = bold_font
            c_r.alignment = center_align
            c_r.border = thin_border

            # تلوين الخلية بناءً على التوازن المحسوب (المرجع = 50)
            cost_val = float(ws.cell(r, 8).value or 1)
            atk_val = float(ws.cell(r, 9).value or 0)
            def_val = float(ws.cell(r, 10).value or 0)
            role_val = str(ws.cell(r, 5).value or "")
            actual_val = (atk_val * 1.18) + (def_val * (0.74 if role_val == "درع صرف" else 0.90)) + p_score + a_score
            expected_val = 16.0 + (cost_val * 14.0)
            bal_50 = (actual_val / expected_val) * 50.0

            if bal_50 >= 56.0 or wr >= 54.5:
                status_fill = balance_op_fill
            elif bal_50 <= 44.0 or wr <= 45.5:
                status_fill = balance_up_fill
            else:
                status_fill = balance_ok_fill

            c_r.fill = status_fill

            # العمود S (19): نسبة الفوز في 100 ألف جولة
            c_s = ws.cell(r, 19, wr)
            c_s.fill = sim_fill
            c_s.font = bold_font
            c_s.alignment = center_align
            c_s.border = thin_border

            # العمود T (20): حكم التوازن النهائي (مقارنة على أساس 50 لكلا العمودين)
            c_t = ws.cell(
                r, 20,
                f'=IF(OR(R{r}>=56, S{r}>=54.5), "🔴 قوي جداً (يحتاج Nerf)", IF(OR(R{r}<=44, S{r}<=45.5), "🔵 ضعيف (يحتاج Buff)", "✅ متوازن (Balanced)"))'
            )
            c_t.font = bold_font
            c_t.alignment = center_align
            c_t.border = thin_border
            c_t.fill = status_fill

        # تحديث صف المتوسط العام (الصف 152)
        for col_idx in range(14, 20):
            col_let = get_column_letter(col_idx)
            c_avg = ws.cell(152, col_idx, f"=ROUND(AVERAGE({col_let}2:{col_let}151), 1)")
            c_avg.font = bold_font
            c_avg.alignment = center_align
            c_avg.border = thin_border
        ws.cell(152, 20, "متوسط الـ 100,000 معركة").alignment = center_align

        # ضبط عرض الأعمدة الجديدة
        col_widths = {14: 26, 15: 28, 16: 26, 17: 26, 18: 28, 19: 26, 20: 26}
        for col_idx, width in col_widths.items():
            ws.column_dimensions[get_column_letter(col_idx)].width = width

        # إنشاء أو تحديث شيت تقييم القدرات التفصيلي من المحاكي
        sheet_title = "⚡ تقييم القدرات (100 ألف جولة)"
        if sheet_title in wb.sheetnames:
            del wb[sheet_title]
        ws_ab = wb.create_sheet(title=sheet_title, index=1)
        ws_ab.views.sheetView[0].rightToLeft = True

        ab_headers = [
            "الترتيب",
            "اسم الميزة الدائمة / المهارة النشطة",
            "التصنيف",
            "مرات الاستدعاء في 100,000 جولة",
            "مرات التفعيل الفعلي بالمعركة",
            "متوسط الأثر القتالي المباشر (نقاط)",
            "متوسط نسبة الفوز للوحوش الحاملة لها %",
            "التقييم النقطي النهائي للميزة (Simulator Score)",
            "توصية المحاكي"
        ]
        for c_idx, h_text in enumerate(ab_headers, 1):
            c = ws_ab.cell(1, c_idx, h_text)
            c.fill = sim_header_fill
            c.font = header_font
            c.alignment = center_align
            c.border = thin_border

        all_ab_rows = eval_result["passive_details"] + eval_result["active_details"]
        all_ab_rows.sort(key=lambda x: x["score"], reverse=True)

        for idx, item in enumerate(all_ab_rows, 1):
            r_idx = idx + 1
            score = item["score"]
            is_passive = "Passive" in item["type"]
            if (is_passive and score >= 20.0) or (not is_passive and score >= 24.0):
                verdict = "🔥 قدرة خارقة عالية التأثير (S-Tier)"
                row_fill = balance_op_fill
            elif score >= 11.0:
                verdict = "⚡ قدرة قوية ومؤثرة (A-Tier)"
                row_fill = balance_ok_fill
            else:
                verdict = "🛡️ ميزة مساندة متوازنة (B-Tier)"
                row_fill = budget_fill

            vals = [
                idx,
                item["name"],
                item["type"],
                item["summons"],
                item["triggers"],
                item["avg_combat_pts"],
                item["avg_wr"],
                score,
                verdict
            ]
            for c_idx, v in enumerate(vals, 1):
                c = ws_ab.cell(r_idx, c_idx, v)
                c.font = bold_font if c_idx in (1, 2, 8, 9) else data_font
                c.alignment = center_align if c_idx != 2 else Alignment(horizontal="right", vertical="center", wrap_text=True)
                c.border = thin_border
                if c_idx in (8, 9):
                    c.fill = row_fill

        ab_widths = {1: 10, 2: 52, 3: 24, 4: 26, 5: 26, 6: 28, 7: 28, 8: 30, 9: 32}
        for c_idx, w in ab_widths.items():
            ws_ab.column_dimensions[get_column_letter(c_idx)].width = w

        # تحديث شيت دليل التعديل والإرشادات بالمعادلة الجديدة
        if "📖 دليل التعديل والإرشادات" in wb.sheetnames:
            ws_guide = wb["📖 دليل التعديل والإرشادات"]
            ws_guide.cell(5, 1, "4. معادلة الميزانية المستحقة للتكلفة (Expected Budget):")
            ws_guide.cell(
                5, 2,
                "المعادلة: =16 + (Cost * 14)\n"
                "• كل بطاقة تدخل الملعب لها تكلفة أساسية ثابتة = 16 نقطة (ثمن استهلاك بطاقة من اليد وحجز خانة من الـ 7 خانات).\n"
                "• كل 1 نقطة استدعاء (Cost) تساوي 14 نقطة ميزانية صافية تضاف للبطاقة."
            )
            ws_guide.cell(6, 1, "5. تقييم الميزات والقدرات عبر محاكي الـ 100,000 جولة:")
            ws_guide.cell(
                6, 2,
                "• يقوم محاكي الـ 100,000 معركة باختبار جميع الميزات الدائمة (70 ميزة) والمهارات النشطة (37 مهارة) عملياً داخل المعارك.\n"
                "• يحسب المحاكي قيمة كل باسيف ومهارة بالنقاط بناءً على الضرر الفعلي، الحماية، التجميد، القتل الفوري، وتوليد الباور، بالإضافة إلى تأثيرها على نسبة الفوز (Win Rate %).\n"
                "• معادلة مؤشر التوازن (على أساس 50) = (قوة الوحش الفعلية شاملة تقييم المحاكي للقدرات ÷ الميزانية المستحقة للتكلفة) × 50. الهدف أن تكون كل البطاقات قريبة من 50 لتتطابق بصرياً مع نسبة الفوز (Win Rate ≈ 50%)."
            )

        try:
            wb.save(excel_path)
        except PermissionError:
            alt_path = excel_path.replace(".xlsx", "_محدث.xlsx")
            wb.save(alt_path)
            print(f"⚠️ ملاحظة: الملف {os.path.basename(excel_path)} مفتوح حالياً في برنامج الإكسل، لذا تم حفظ النسخة المحدثة أيضاً في: {os.path.basename(alt_path)}")


def launch_gui():
    import threading
    import tkinter as tk
    from tkinter import ttk, messagebox

    root = tk.Tk()
    root.title("⚔️ محاكي الـ 100,000 جولة لتقييم قوة الميزات (Passives) والقدرات النشطة وتحديث الإكسل")
    root.geometry("1080x720")
    root.configure(bg="#111827")

    title_lbl = tk.Label(
        root,
        text="⚡ محاكي 100,000 جولة: التقييم الذكي للباسيف والقدرات النشطة + تحديث الإكسل تلقائياً",
        font=("Segoe UI", 15, "bold"),
        bg="#111827",
        fg="#F9FAFB",
        pady=12
    )
    title_lbl.pack(fill="x")

    ctrl_frame = tk.Frame(root, bg="#1F2937", padx=14, pady=10)
    ctrl_frame.pack(fill="x", padx=16, pady=6)

    tk.Label(ctrl_frame, text="عدد المباريات للمحاكاة:", font=("Segoe UI", 11, "bold"), bg="#1F2937", fg="#E5E7EB").pack(side="right", padx=8)
    matches_var = tk.StringVar(value="100000")
    matches_entry = ttk.Combobox(ctrl_frame, textvariable=matches_var, values=["20000", "50000", "100000", "200000"], width=12, state="readonly")
    matches_entry.pack(side="right", padx=8)

    status_var = tk.StringVar(value="جاهز لبدء محاكاة الـ 100,000 معركة واستخراج تقييم كل باسيف وقدرة...")
    status_lbl = tk.Label(ctrl_frame, textvariable=status_var, font=("Segoe UI", 10, "bold"), bg="#1F2937", fg="#34D399")
    status_lbl.pack(side="left", padx=10)

    prog = ttk.Progressbar(root, orient="horizontal", mode="determinate", maximum=100)
    prog.pack(fill="x", padx=16, pady=6)

    # جدول عرض تقييم القدرات والباسيف
    tree_frame = tk.Frame(root, bg="#111827")
    tree_frame.pack(fill="both", expand=True, padx=16, pady=8)

    cols = ("rank", "name", "type", "summons", "triggers", "combat_pts", "wr", "score")
    tree = ttk.Treeview(tree_frame, columns=cols, show="headings", height=20)
    headers = {
        "rank": "#",
        "name": "اسم الميزة الدائمة / المهارة النشطة",
        "type": "النوع",
        "summons": "مرات النزول للملعب",
        "triggers": "مرات التفعيل",
        "combat_pts": "الأثر القتالي المباشر",
        "wr": "نسبة الفوز %",
        "score": "التقييم النهائي بالنقاط (Score)"
    }
    widths = {"rank": 45, "name": 340, "type": 140, "summons": 110, "triggers": 110, "combat_pts": 120, "wr": 100, "score": 150}
    for k, h in headers.items():
        tree.heading(k, text=h)
        tree.column(k, width=widths[k], anchor="center" if k != "name" else "e")

    vsb = ttk.Scrollbar(tree_frame, orient="vertical", command=tree.yview)
    tree.configure(yscrollcommand=vsb.set)
    tree.pack(side="left", fill="both", expand=True)
    vsb.pack(side="right", fill="y")

    def run_sim_thread():
        btn_run.config(state="disabled")
        n_matches = int(matches_var.get())
        prog["value"] = 0

        def on_prog(done, total):
            pct = (done / total) * 100.0
            root.after(0, lambda: (
                prog.configure(value=pct),
                status_var.set(f"⏳ جاري محاكاة المعارك وتقييم القدرات: {done:,} / {total:,} ({pct:.0f}%)")
            ))

        def worker():
            res = run_100k_evaluation(n_matches, on_prog)
            update_excel_files(res)

            def finish_ui():
                for item in tree.get_children():
                    tree.delete(item)
                all_rows = res["passive_details"] + res["active_details"]
                all_rows.sort(key=lambda x: x["score"], reverse=True)
                for idx, r in enumerate(all_rows, 1):
                    tree.insert("", "end", values=(
                        idx, r["name"], r["type"], f"{r['summons']:,}", f"{r['triggers']:,}",
                        r["avg_combat_pts"], f"{r['avg_wr']}%", f"{r['score']} نقطة"
                    ))
                status_var.set(
                    f"✅ اكتملت محاكاة {n_matches:,} معركة في {res['elapsed']} ثانية وتم تحديث ملف الإكسل تلقائياً!"
                )
                btn_run.config(state="normal")
                messagebox.showinfo(
                    "تم تحديث الإكسل بنجاح",
                    f"تمت محاكاة {n_matches:,} معركة في {res['elapsed']} ثانية!\n\n"
                    "• تم حساب القيمة النقطية الدقيقة لكل ميزة دائمة (Passive) وكل مهارة نشطة (Active Skill).\n"
                    "• تم تحديث ملف الإكسل (قاعدة_بيانات_الوحوش_والكروت.xlsx) بالمعادلة الجديدة ونتائج المحاكي."
                )

            root.after(0, finish_ui)

        threading.Thread(target=worker, daemon=True).start()

    btn_run = tk.Button(
        ctrl_frame,
        text="🚀 تشغيل محاكي 100,000 جولة وتحديث الإكسل الآن",
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
        print(f"Running {n:,} battle simulations to evaluate all Passives, Active Skills, and Monsters...")
        res = run_100k_evaluation(n)
        update_excel_files(res)
        print(f"Done in {res['elapsed']}s! Excel files updated.")
        print("\n=== Top 12 Strongest Passives (Discovered by 100k Simulator) ===")
        for item in res["passive_details"][:12]:
            print(f"  [{item['score']:4.1f} pts | WR {item['avg_wr']:5.2f}%] {item['name']} (Combat: {item['avg_combat_pts']} pts)")
        print("\n=== Bottom 6 Weakest Passives (Discovered by 100k Simulator) ===")
        for item in res["passive_details"][-6:]:
            print(f"  [{item['score']:4.1f} pts | WR {item['avg_wr']:5.2f}%] {item['name']} (Combat: {item['avg_combat_pts']} pts)")
        print("\n=== Top 12 Strongest Active Skills (Discovered by 100k Simulator) ===")
        for item in res["active_details"][:12]:
            print(f"  [{item['score']:4.1f} pts | WR {item['avg_wr']:5.2f}%] {item['name']} (Combat: {item['avg_combat_pts']} pts)")
    else:
        launch_gui()

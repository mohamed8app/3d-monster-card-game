# -*- coding: utf-8 -*-
"""
محرك المحاكاة التكتيكي المتقدم (Advanced Tactical Battle Engine)
يدعم:
1. 10,000+ جولة بسرعة فائقة
2. كروت البيئة الـ 12 بمستوياتها الـ 3 ومصفوفتها المتناظرة العادلة
3. رصد منحنى الاشتعال ودور الذروة (Climax Turn) وحمام الدم
4. حساب زمن الجولات والمباراة الواقعي بدقة ديناميكية
5. تشخيص نواقص ميكانزم اللعبة (الجمود، الإرهاق، العودة من الخلف، فاعلية المدافع والأجنحة)
"""

import json
import random
import time
import sys
from collections import defaultdict

# إصلاح ترميز الحروف لويندوز
if sys.stdout.encoding != 'utf-8':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    except Exception:
        pass

# تحميل قاعدة بيانات الـ 150 وحش
with open(r"d:\deck game\monsters_150.json", "r", encoding="utf-8") as f:
    ALL_MONSTERS = json.load(f)

MONSTER_MAP = {m["id"]: m for m in ALL_MONSTERS}
ALL_ELEMENTS = ["نار", "مياه", "جليد", "أرض", "كهرباء", "رياح", "غابة", "ضياء", "ظلام", "ميكا", "أشباح", "سحر"]

# مصفوفة كروت البيئة الـ 12 المعتمدة (تفيد 1 وتضر 3 ومحايدة مع 8)
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

# مستويات كروت البيئة
ENV_LEVELS = {
    1: {"cost": 2, "duration": 4, "buff_pct": 0.30, "debuff_pct": 0.20},
    2: {"cost": 4, "duration": 2, "buff_pct": 0.40, "debuff_pct": 0.30},
    3: {"cost": 6, "duration": 1, "buff_pct": 0.70, "debuff_pct": 0.50},
}

class EnvironmentCard:
    def __init__(self, element, level=1):
        self.element = element
        self.level = level
        data = ENV_LEVELS[level]
        self.cost = data["cost"]
        self.duration = data["duration"]
        self.buff_pct = data["buff_pct"]
        self.debuff_pct = data["debuff_pct"]
        self.name = f"{ENVIRONMENT_MATRIX[element]['name']} (مستوى {level})"

class ActiveEnvironment:
    def __init__(self, card: EnvironmentCard):
        self.element = card.element
        self.level = card.level
        self.turns_left = card.duration
        self.buff_pct = card.buff_pct
        self.debuff_pct = card.debuff_pct
        self.buffed_elem = ENVIRONMENT_MATRIX[card.element]["buff"]
        self.debuffed_elems = set(ENVIRONMENT_MATRIX[card.element]["debuff"])

    def get_modifier(self, monster_elem):
        if monster_elem == self.buffed_elem:
            return 1.0 + self.buff_pct
        elif monster_elem in self.debuffed_elems:
            return max(0.2, 1.0 - self.debuff_pct)
        return 1.0

class MonsterInstance:
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
        self.summoned_turn = 0
        self.can_attack = False

    def is_alive(self):
        return self.current_def > 0

    def get_effective_atk(self, active_env: ActiveEnvironment):
        if not active_env:
            return self.base_atk
        mod = active_env.get_modifier(self.element)
        return max(0, int(round(self.base_atk * mod)))

    def get_effective_max_def(self, active_env: ActiveEnvironment):
        if not active_env:
            return self.base_def
        mod = active_env.get_modifier(self.element)
        return max(1, int(round(self.base_def * mod)))

class TacticalBot:
    def __init__(self, name, deck_cards):
        self.name = name
        self.hp = 1000
        self.mana_cap = 1
        self.power = 1
        self.deck = list(deck_cards)
        random.shuffle(self.deck)
        self.hand = []
        # 7 خانات:
        # 0, 1: أجنحة يسار
        # 2, 3: وسط أمامي
        # 4, 5: أجنحة يمين
        # 6: مدافع خلفي
        self.board = [None] * 7
        self.fatigue_count = 0
        self.played_card_ids = set()
        self.min_hp_seen = 1000

    def draw_card(self):
        if self.deck:
            card = self.deck.pop(0)
            if len(self.hand) < 10:
                self.hand.append(card)
            return True
        else:
            self.fatigue_count += 1
            fatigue_damage = self.fatigue_count * 30
            self.hp -= fatigue_damage
            return False

    def perform_turn_start(self, turn_num):
        self.power = self.mana_cap
        self.draw_card()
        for slot in range(7):
            m = self.board[slot]
            if m and m.is_alive():
                if turn_num > m.summoned_turn:
                    m.can_attack = True

    def perform_sacrifice(self):
        # تضحية تكتيكية إذا كان سقف المانا أقل من 8 ومعه أكثر من كارت
        if self.mana_cap < 8 and len(self.hand) >= 2:
            monsters_in_hand = [c for c in self.hand if isinstance(c, MonsterInstance)]
            if monsters_in_hand:
                unplayable = [c for c in monsters_in_hand if c.cost > self.power + 1]
                sac_card = max(unplayable, key=lambda c: c.cost) if unplayable else monsters_in_hand[-1]
                self.hand.remove(sac_card)
                self.mana_cap += 1
                self.power += 1

    def play_environment_logic(self, current_active_env: ActiveEnvironment):
        # ذكاء لعب كروت البيئة
        env_cards = [c for c in self.hand if isinstance(c, EnvironmentCard) and c.cost <= self.power]
        if not env_cards:
            return None
        
        # حساب كم وحش في ساحتي يستفيد من كل كارت بيئة باليد
        my_monsters = [m for m in self.board if m and m.is_alive()]
        best_env = None
        best_score = -999

        for env_card in env_cards:
            score = 0
            # تقييم الفائدة لجيشي
            for m in my_monsters:
                if m.element == env_card.element:
                    score += 3
                elif m.element in ENVIRONMENT_MATRIX[env_card.element]["debuff"]:
                    score -= 2
            
            # تفضيل لعب البيئة إذا كانت تفيد وحوشي أكثر
            if score > best_score:
                best_score = score
                best_env = env_card

        if best_env and (best_score >= 1 or current_active_env is None or current_active_env.turns_left <= 1):
            self.power -= best_env.cost
            self.hand.remove(best_env)
            return ActiveEnvironment(best_env)
        return None

    def summon_ai_logic(self, turn_num):
        monsters = [c for c in self.hand if isinstance(c, MonsterInstance)]
        monsters.sort(key=lambda c: (c.cost, c.base_atk + c.base_def), reverse=True)
        
        for card in list(monsters):
            if card.cost <= self.power:
                best_slot = self.find_best_slot(card)
                if best_slot is not None:
                    self.power -= card.cost
                    card.summoned_turn = turn_num
                    card.can_attack = False
                    self.board[best_slot] = card
                    self.hand.remove(card)
                    self.played_card_ids.add(card.id)

    def find_best_slot(self, card: MonsterInstance):
        if card.role == "درع صرف":
            if self.board[6] is None:
                return 6
            for s in [0, 5, 1, 4]:
                if self.board[s] is None:
                    return s
        elif card.role == "مهاجم":
            for s in [0, 1, 4, 5, 2, 3]:
                if self.board[s] is None:
                    return s
        else:
            for s in [2, 3, 6, 0, 1, 4, 5]:
                if self.board[s] is None:
                    return s
        return None

def calculate_realistic_turn_seconds(turn_num):
    # حساب واقعي لزمن التفكير والحركة لكل دور
    if turn_num <= 5:
        return 12.0  # أدوار أولى سريعة
    elif turn_num <= 18:
        return 28.0  # ذروة التفكير والمواجهات في المنتصف
    else:
        return 22.0  # أدوار الحسم المباشرة

def execute_turn_combat(attacker: TacticalBot, defender: TacticalBot, active_env: ActiveEnvironment):
    # مرحلة القتال وحساب الضرر والوفيات لهذا الدور
    stats = {
        "damage_dealt": 0,
        "monsters_killed": 0,
        "guardian_hits": 0,
        "guardian_damage_absorbed": 0,
        "wings_direct_hits": 0,
        "wings_direct_damage": 0,
        "center_direct_damage": 0,
    }

    # 1. هجوم الأجنحة (0, 1, 4, 5)
    for slot in [0, 1, 4, 5]:
        m = attacker.board[slot]
        if m and m.is_alive() and m.can_attack:
            atk_power = m.get_effective_atk(active_env)
            if atk_power <= 0:
                continue
            
            enemy_m = defender.board[slot]
            if enemy_m and enemy_m.is_alive():
                enemy_m.current_def -= atk_power
                stats["damage_dealt"] += atk_power
                if not enemy_m.is_alive():
                    defender.board[slot] = None
                    stats["monsters_killed"] += 1
            else:
                # الخانة المقابلة فارغة: ضرب القائد مباشرة من الجناح!
                defender.hp -= atk_power
                stats["damage_dealt"] += atk_power
                stats["wings_direct_hits"] += 1
                stats["wings_direct_damage"] += atk_power

    # 2. هجوم الوسط (2, 3)
    for slot in [2, 3]:
        m = attacker.board[slot]
        if m and m.is_alive() and m.can_attack:
            atk_power = m.get_effective_atk(active_env)
            if atk_power <= 0:
                continue

            enemy_m = defender.board[slot]
            if enemy_m and enemy_m.is_alive():
                enemy_m.current_def -= atk_power
                stats["damage_dealt"] += atk_power
                if not enemy_m.is_alive():
                    defender.board[slot] = None
                    stats["monsters_killed"] += 1
            else:
                # الوسط فارغ: فحص المدافع الخلفي بالوسط (خانة 6)
                guardian = defender.board[6]
                if guardian and guardian.is_alive():
                    stats["guardian_hits"] += 1
                    guardian.current_def -= atk_power
                    stats["damage_dealt"] += atk_power
                    stats["guardian_damage_absorbed"] += atk_power
                    if not guardian.is_alive():
                        defender.board[6] = None
                        stats["monsters_killed"] += 1
                else:
                    # الوسط فارغ والمدافع الخلفي ميت: ضرب القائد مباشرة!
                    defender.hp -= atk_power
                    stats["damage_dealt"] += atk_power
                    stats["center_direct_damage"] += atk_power

    return stats

def build_custom_deck(deck_size=30, include_environments=True):
    cards = []
    all_monster_ids = list(MONSTER_MAP.keys())
    random.shuffle(all_monster_ids)
    
    num_envs = 4 if include_environments and deck_size >= 30 else 0
    num_monsters = deck_size - num_envs

    # إضافة الوحوش مع مراعاة قيد الدرجة 7 (كارت واحد فقط)
    has_tier7 = False
    for mid in all_monster_ids:
        if len(cards) >= num_monsters:
            break
        m = MONSTER_MAP[mid]
        if m["tier_num"] == 7:
            if has_tier7:
                continue
            has_tier7 = True
        cards.append(MonsterInstance(m))

    while len(cards) < num_monsters:
        mid = random.choice(all_monster_ids)
        cards.append(MonsterInstance(MONSTER_MAP[mid]))

    # إضافة كروت البيئة (مزيج من مستويات 1 و 2 و 3)
    if num_envs > 0:
        # اختيار عناصر تتوافق جزئياً مع وحوش المجموعة
        deck_elems = [c.element for c in cards if isinstance(c, MonsterInstance)]
        for _ in range(num_envs):
            elem = random.choice(deck_elems) if deck_elems else random.choice(ALL_ELEMENTS)
            lvl = random.choices([1, 2, 3], weights=[0.55, 0.35, 0.10])[0]
            cards.append(EnvironmentCard(elem, lvl))

    return cards

def run_single_battle(deck_size=30, include_environments=True, max_turns=80):
    deck_a = build_custom_deck(deck_size, include_environments)
    deck_b = build_custom_deck(deck_size, include_environments)

    p1 = TacticalBot("اللاعب الأول", deck_a)
    p2 = TacticalBot("اللاعب الثاني", deck_b)

    # قرعة البداية
    coin_flip = random.random() < 0.5
    active, passive = (p1, p2) if coin_flip else (p2, p1)

    # سحب يد البداية (6 كروت لكل لاعب)
    for _ in range(6):
        p1.draw_card()
        p2.draw_card()

    active_env = None
    turn = 1
    
    match_turn_stats = []
    consecutive_stagnant_turns = 0
    stagnation_detected = False
    max_hp_deficit_for_winner = 0

    total_guardian_hits = 0
    total_guardian_absorbed = 0
    total_wings_direct = 0
    total_center_direct = 0
    env_plays_count = 0

    while p1.hp > 0 and p2.hp > 0 and turn <= max_turns:
        # إدارة مؤقت البيئة الحالية
        if active_env:
            active_env.turns_left -= 1
            if active_env.turns_left <= 0:
                active_env = None

        # 1. بداية الدور
        active.perform_turn_start(turn)
        
        # 2. التضحية
        active.perform_sacrifice()
        
        # 3. لعب كارت البيئة إن وجد
        if include_environments:
            new_env = active.play_environment_logic(active_env)
            if new_env:
                active_env = new_env
                env_plays_count += 1

        # 4. استدعاء الوحوش
        active.summon_ai_logic(turn)

        # 5. مرحلة القتال
        combat_res = execute_turn_combat(active, passive, active_env)
        
        total_guardian_hits += combat_res["guardian_hits"]
        total_guardian_absorbed += combat_res["guardian_damage_absorbed"]
        total_wings_direct += combat_res["wings_direct_damage"]
        total_center_direct += combat_res["center_direct_damage"]

        # تسجيل إحصائيات هذا الدور لتقييم الحماس والاشتعال
        turn_seconds = calculate_realistic_turn_seconds(turn)
        turn_intensity = combat_res["damage_dealt"] + (combat_res["monsters_killed"] * 30)
        
        match_turn_stats.append({
            "turn": turn,
            "damage": combat_res["damage_dealt"],
            "kills": combat_res["monsters_killed"],
            "intensity": turn_intensity,
            "seconds": turn_seconds,
            "p1_hp": p1.hp,
            "p2_hp": p2.hp,
        })

        # فحص الجمود التكتيكي (Stagnation): دور كامل بدون دمج أو موت وحوش مع وجود وحوش
        act_monsters = sum(1 for m in active.board if m and m.is_alive())
        pas_monsters = sum(1 for m in passive.board if m and m.is_alive())
        if combat_res["damage_dealt"] == 0 and combat_res["monsters_killed"] == 0 and (act_monsters + pas_monsters >= 6):
            consecutive_stagnant_turns += 1
            if consecutive_stagnant_turns >= 4:
                stagnation_detected = True
        else:
            consecutive_stagnant_turns = 0

        # تتبع أقل HP لكل لاعب لرصد العودة من الخلف (Comeback)
        p1.min_hp_seen = min(p1.min_hp_seen, p1.hp)
        p2.min_hp_seen = min(p2.min_hp_seen, p2.hp)

        if passive.hp <= 0:
            break

        # تبديل الأدوار
        active, passive = passive, active
        turn += 1

    # تحديد الفائز وسبب الفوز
    winner = p1 if p1.hp > p2.hp else p2
    loser = p2 if winner == p1 else p1
    death_by_fatigue = (loser.hp <= 0 and loser.fatigue_count > 0)
    
    # حساب دور الذروة والاشتعال (Climax Turn)
    climax_turn = 1
    if match_turn_stats:
        climax_turn = max(match_turn_stats, key=lambda s: s["intensity"])["turn"]

    # فحص العودة من الخلف (Comeback): الفائز كان متأخراً بـ 300+ HP
    is_comeback = False
    if winner == p1 and (p2.hp - p1.min_hp_seen) >= 300:
        is_comeback = True
    elif winner == p2 and (p1.hp - p2.min_hp_seen) >= 300:
        is_comeback = True

    total_match_seconds = sum(s["seconds"] for s in match_turn_stats)

    return {
        "winner_player": 1 if winner == p1 else 2,
        "first_player_won": (winner == p1 if coin_flip else winner == p2),
        "turns": min(turn, max_turns),
        "total_seconds": total_match_seconds,
        "climax_turn": climax_turn,
        "winner_cards": winner.played_card_ids,
        "loser_cards": loser.played_card_ids,
        "death_by_fatigue": death_by_fatigue,
        "stagnation": stagnation_detected,
        "is_comeback": is_comeback,
        "guardian_absorbed": total_guardian_absorbed,
        "wings_direct": total_wings_direct,
        "center_direct": total_center_direct,
        "turn_stats": match_turn_stats,
        "env_plays": env_plays_count,
    }

def run_large_scale_simulation(num_matches=10000, deck_size=30, include_environments=True, progress_callback=None):
    """
    محاكاة ضخمة (10,000+ جولة) مع تجميع كامل القياسات
    """
    start_time = time.time()
    
    card_appearances = defaultdict(int)
    card_wins = defaultdict(int)
    
    first_player_wins = 0
    total_turns = 0
    total_match_seconds = 0
    fatigue_deaths = 0
    stagnations = 0
    comebacks = 0
    
    climax_turn_histogram = defaultdict(int)
    turn_intensity_accumulator = defaultdict(lambda: {"damage": 0, "kills": 0, "count": 0})
    
    guardian_total_absorbed = 0
    wings_total_direct = 0
    center_total_direct = 0
    total_env_plays = 0
    
    for i in range(1, num_matches + 1):
        res = run_single_battle(deck_size, include_environments)
        
        total_turns += res["turns"]
        total_match_seconds += res["total_seconds"]
        if res["first_player_won"]:
            first_player_wins += 1
        if res["death_by_fatigue"]:
            fatigue_deaths += 1
        if res["stagnation"]:
            stagnations += 1
        if res["is_comeback"]:
            comebacks += 1
            
        climax_turn_histogram[res["climax_turn"]] += 1
        guardian_total_absorbed += res["guardian_absorbed"]
        wings_total_direct += res["wings_direct"]
        center_total_direct += res["center_direct"]
        total_env_plays += res["env_plays"]

        # تجميع كروت الفوز والظهور
        for cid in res["winner_cards"]:
            card_appearances[cid] += 1
            card_wins[cid] += 1
        for cid in res["loser_cards"]:
            card_appearances[cid] += 1

        # تجميع منحنى الاشتعال لكل دور
        for t_stat in res["turn_stats"]:
            t = t_stat["turn"]
            turn_intensity_accumulator[t]["damage"] += t_stat["damage"]
            turn_intensity_accumulator[t]["kills"] += t_stat["kills"]
            turn_intensity_accumulator[t]["count"] += 1

        if progress_callback and (i % 250 == 0 or i == num_matches):
            progress_callback(i, num_matches)

    elapsed_seconds = time.time() - start_time
    avg_turns = total_turns / num_matches
    avg_match_minutes = (total_match_seconds / num_matches) / 60.0
    avg_turn_seconds = total_match_seconds / total_turns if total_turns > 0 else 20.0
    
    # حساب أكثر دور حدث فيه اشتعال الساحة عبر كل المباريات
    most_frequent_climax_turn = max(climax_turn_histogram.items(), key=lambda x: x[1])[0] if climax_turn_histogram else 15

    # تجميع جدول نسب فوز الوحوش
    monsters_stats = []
    for mid, m in MONSTER_MAP.items():
        apps = card_appearances[mid]
        w = card_wins[mid]
        wr = (w / apps * 100.0) if apps > 0 else 50.0
        monsters_stats.append({
            "id": mid,
            "name": m["name"],
            "element": m["element"],
            "role": m["role"],
            "tier": m["tier_num"],
            "cost": m["cost"],
            "atk": m["atk"],
            "def": m["def"],
            "appearances": apps,
            "wins": w,
            "win_rate": round(wr, 1),
        })

    monsters_stats.sort(key=lambda x: x["win_rate"], reverse=True)

    # منحنى الاشتعال لكل دور
    intensity_curve = []
    for t in sorted(turn_intensity_accumulator.keys()):
        cnt = turn_intensity_accumulator[t]["count"]
        if cnt > 0:
            intensity_curve.append({
                "turn": t,
                "avg_damage": round(turn_intensity_accumulator[t]["damage"] / cnt, 1),
                "avg_kills": round(turn_intensity_accumulator[t]["kills"] / cnt, 2),
            })

    # تشخيص الميكانزم وتوليد توصيات تلقائية
    recommendations = []
    if (fatigue_deaths / num_matches) > 0.25:
        recommendations.append("⚠️ نسبة الموت بالإرهاق مرتفعة (>25%): يُنصح بزيادة حجم المجموعة (Deck Size) في الرتب المتقدمة أو تسريع حسم المباريات بزيادة ضرر الأجنحة المباشر.")
    else:
        recommendations.append("✅ معدل الإرهاق ممتاز ومتوازن (<25%): معظم المباريات تُحسم بالقتال الميداني المباشر.")

    if (stagnations / num_matches) > 0.08:
        recommendations.append("⚠️ رصد حالات جمود تكتيكي في الساحة: يُوصى بإضافة ميزة الاختراق (Piercing Damage) أو مهارات مساحية (AoE) لفض التكدس.")
    else:
        recommendations.append("✅ الساحة ديناميكية وحركة المعركة مستمرة بدون جمود أو تكدس دفاعي ميت.")

    if comebacks / num_matches < 0.12:
        recommendations.append("💡 إمكانية العودة من الخلف منخفضة: اللاعب المتأخر نادراً ما يعود. يُنصح بإضافة ميكانيكا العودة (Comeback Mechanics) مثل بطاقة خارقة تُفعل عندما يقل الـ HP عن 300.")
    else:
        recommendations.append(f"🔥 مؤشر الإثارة رائع! {round(comebacks / num_matches * 100, 1)}% من المباريات شهدت عودة ملحمية (Comeback) بعد تأخر بأكثر من 300 HP.")

    total_damage_pool = (guardian_total_absorbed + wings_total_direct + center_total_direct) or 1
    guardian_pct = round(guardian_total_absorbed / total_damage_pool * 100, 1)
    wings_pct = round(wings_total_direct / total_damage_pool * 100, 1)
    center_pct = round(center_total_direct / total_damage_pool * 100, 1)

    return {
        "num_matches": num_matches,
        "elapsed_seconds": round(elapsed_seconds, 2),
        "avg_turns": round(avg_turns, 1),
        "avg_match_minutes": round(avg_match_minutes, 1),
        "avg_turn_seconds": round(avg_turn_seconds, 1),
        "first_player_win_rate": round(first_player_wins / num_matches * 100, 1),
        "fatigue_death_rate": round(fatigue_deaths / num_matches * 100, 1),
        "stagnation_rate": round(stagnations / num_matches * 100, 1),
        "comeback_rate": round(comebacks / num_matches * 100, 1),
        "most_frequent_climax_turn": most_frequent_climax_turn,
        "climax_histogram": dict(climax_turn_histogram),
        "intensity_curve": intensity_curve,
        "damage_distribution": {
            "guardian_absorbed_pct": guardian_pct,
            "wings_direct_pct": wings_pct,
            "center_direct_pct": center_pct,
        },
        "total_env_plays": total_env_plays,
        "recommendations": recommendations,
        "monsters_stats": monsters_stats,
    }

if __name__ == "__main__":
    print("🚀 اختبار المحرك المتقدم لـ 1,000 جولة...")
    res = run_large_scale_simulation(1000, 30, True)
    print(f"✅ تم في {res['elapsed_seconds']} ثانية! متوسط الأدوار: {res['avg_turns']}، دور الذروة: {res['most_frequent_climax_turn']}")

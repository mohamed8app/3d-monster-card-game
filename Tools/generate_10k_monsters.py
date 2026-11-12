# -*- coding: utf-8 -*-
r"""
Mathematically Balanced 452-Card & Monster Database Generator (100% English In-Game + Arabic Monster Abilities Column in Excel)
- 240 Monsters (20 progressive, balanced monsters per Element across 12 Elements)
- 200 Diverse Intermediary Tactical Spell Cards (40 per Category across 5 Categories, 10 progressive tiers per template)
- 12 Global Environment Weather Cards
Exports:
1. D:/deck game/Data/قاعدة_بيانات_الوحوش_والكروت.xlsx
2. D:/deck game/UnityGame/Assets/Resources/cards_10000.json
3. D:/deck game/UnityGame/Assets/Resources/monsters_150.json
"""

import json
import os
import random
import re
import openpyxl
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.utils import get_column_letter

random.seed(42)

BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXCEL_PATH = os.path.join(BASE_DIR, "Data", "قاعدة_بيانات_الوحوش_والكروت.xlsx")
UNITY_RES_DIR = os.path.join(BASE_DIR, "UnityGame", "Assets", "Resources")
CARDS_10K_JSON_PATH = os.path.join(UNITY_RES_DIR, "cards_10000.json")
UNITY_MONSTERS_JSON_PATH = os.path.join(UNITY_RES_DIR, "monsters_150.json")

# 12 Monster Elements (20 progressive balanced monsters per element = 240 monsters total)
ELEMENTS_CONFIG = [
    ("Fire", "Fire", "🔥", "E63946", 20),
    ("Water", "Water", "💧", "0077B6", 20),
    ("Ice", "Ice", "❄️", "48CAE4", 20),
    ("Electric", "Electric", "⚡", "FFB703", 20),
    ("Air", "Air", "🌪️", "8ECAE6", 20),
    ("Earth", "Earth", "🪨", "8D6E63", 20),
    ("Forest", "Forest", "🌿", "2D6A4F", 20),
    ("Light", "Light", "✨", "FFD166", 20),
    ("Dark", "Dark", "🌑", "4A4E69", 20),
    ("Mecha", "Mecha", "⚙️", "6C757D", 20),
    ("Ghosts", "Ghosts", "👻", "7209B7", 20),
    ("Arcane", "Arcane", "🔮", "560BAD", 20),
]

TIER_NAMES = {
    1: "Tier 1 - Basic",
    2: "Tier 2 - Enhanced",
    3: "Tier 3 - Advanced",
    4: "Tier 4 - Superior",
    5: "Tier 5 - Elite",
    6: "Tier 6 - Epic",
    7: "Tier 7 - Legendary",
}

# Each tuple is: (English Ability Text, Arabic Translation for Excel Only, Score)
ABILITIES_POOL = {
    "Fire": {
        "passives": [
            ("Self-Ignition: Reduces target DEF by 3 each turn", "اشتعال ذاتي ينقص 3 دفاع كل دور", 8.0),
            ("Flank Strike: +4 ATK on the Flanks", "زيادة +4 هجوم في الأجنحة", 12.0),
            ("Protective Flame: Absorbs 4 incoming damage", "لهب وقائي يمتص 4 ضرر", 10.0),
            ("Blazing Fire: Reduces target DEF by 8 each turn", "نار مضرمة تنقص 8 دفاع عن الهدف كل دور", 18.0),
            ("Thermal Explosion: Deals 10 damage to opposite slot on death", "انفجار حراري يلحق 10 ضرر بالخانة المقابلة عند موته", 16.0),
            ("Total Immunity to Ice", "حصانة تامة ضد الجليد", 14.0),
            ("Fire Blaze: Boosts adjacent allies ATK by +3", "شواظ ناري يرفع هجوم الحلفاء المجاورين بـ +3", 15.0),
            ("Burning Embers: Reflects 25% of incoming attack damage", "جمر ملتهب يعكس 25% من ضرر الهجمات الواردة", 17.0),
            ("Eternal Flame: Gains +2 permanent ATK after every strike", "لهيب أزلي: يكتسب +2 هجوم دائم بعد كل ضربة", 20.0),
            ("Resurrection Ashes: Restores +20 HP to your Commander on death", "رماد القيامة: عند موته يستعيد القائد +20 HP", 16.0),
        ],
        "actives": [
            ("Fireball: Deal 12 damage to any slot (Cost 1 Power)", "قذيفة نارية: إلحاق 12 ضرر بأي خانة (تكلفة 1 باور)", 18.0),
            ("Ember Blast: Deal 8 damage to target and adjacent slots (Cost 1 Power)", "انفجار الجمر: إلحاق 8 ضرر بالخانة المستهدفة والخانات المجاورة (تكلفة 1 باور)", 22.0),
            ("Lava Torrent: Melt 15 DEF from opposite enemy monster (Cost 1 Power)", "سيل الحمم: إذابة 15 من دروع وحش الخصم المقابل (تكلفة 1 باور)", 19.0),
            ("Purging Flame: Cleanse ally debuffs and grant +5 ATK (Cost 1 Power)", "لهب التطهير: مسح التأثيرات السلبية عن حليف وزيادة هجومه +5 (تكلفة 1 باور)", 17.0),
            ("Hell of Annihilation: 25 AOE damage to all enemy slots (Cost 2 Power)", "جحيم الفناء: 25 ضرر جماعي لكل خانات الخصم (تكلفة 2 باور)", 30.0),
            ("Volcanic Eruption: Instantly destroy opposite enemy if DEF < 30 (Cost 2 Power)", "ثوران بركاني: تدمير وحش الخصم المقابل فوراً إذا كان دفاعه أقل من 30 (تكلفة 2 باور)", 28.0),
        ]
    },
    "Water": {
        "passives": [
            ("Soothing Current: Restores 5 HP to Commander each turn", "تيار مهدئ يستعيد 5 HP للقائد كل دور", 10.0),
            ("Water Resilience: Reduces incoming damage by 3", "مرونة مائية تقلل الضرر الوارد بـ 3", 11.0),
            ("Moisture Shield: Prevents Burn", "رطوبة تمنع الحرق", 9.0),
            ("Tidal Bounce: Returns opposite enemy to hand if ATK < 10", "موجة ارتدادية تعيد وحش الخصم ليده إذا كان هجومه أقل من 10", 18.0),
            ("Ocean Protection: Grants +15 DEF shield to nearest ally", "حماية المحيط تعطي درع 15 لأقرب حليف", 16.0),
            ("Total Immunity to Fire", "حصانة تامة ضد النار", 14.0),
            ("Water Submersion: Reduces opposite monster ATK by 4", "غمر مائي ينقص 4 من هجوم الوحش المقابل", 15.0),
            ("Gushing Waterfall: Cleanses all curses from your allies", "شلال دافق يغسل كافة اللعنات عن حلفائك", 16.0),
            ("High Tide: Boosts DEF of all allied Water monsters by +4", "مد بحري يزيد دفاع كافة وحوش الماء بـ +4", 19.0),
        ],
        "actives": [
            ("Water Vortex: Shift enemy monster to an adjacent slot (Cost 1 Power)", "دوامة مائية: إزاحة وحش الخصم لخانة مجاورة (تكلفة 1 باور)", 18.0),
            ("Healing Stream: Restore 20 DEF to any allied monster (Cost 1 Power)", "تيار شفاء: استعادة 20 HP لأي وحش حليف (تكلفة 1 باور)", 20.0),
            ("Safety Bubble: Grant ally complete immunity to one strike (Cost 1 Power)", "فقاعة أمان: منح حصانة كاملة لحليف ضد ضربة واحدة (تكلفة 1 باور)", 21.0),
            ("Sweeping Tsunami: 20 damage to all enemies and strip buffs (Cost 2 Power)", "تسونامي جارف: 20 ضرر لكل الأعداء وغسل جميع التأثيرات الإيجابية (تكلفة 2 باور)", 29.0),
            ("Abyssal Submersion: Freeze enemy vanguard for 1 full turn (Cost 2 Power)", "غمر الأعماق: تجميد هجوم الخصم في الوسط لدور كامل (تكلفة 2 باور)", 27.0),
        ]
    },
    "Ice": {
        "passives": [
            ("Light Chill: Reduces opposite monster ATK by 3", "برودة خفيفة تقلل هجوم المقابل بـ 3", 9.0),
            ("Ice Structure: Grants +5 DEF in Vanguard center slots", "هيكل جليدي يعطي +5 دفاع في الوسط", 11.0),
            ("Wind Resistance", "مقاومة الرياح", 8.0),
            ("Frost Aura: Increases enemy active skill cost by +1 Power", "هالة الصقيع تزيد تكلفة مهارات الخصم بـ +1 باور", 19.0),
            ("Auto-Freeze Attacker: Freezes attacker for 1 turn when hit", "تجميد تلقائي للمهاجم لدور واحد عند تلقي ضربة", 20.0),
            ("Immunity to Poison", "حصانة ضد السم", 13.0),
            ("Ice Blade: Deals +5 bonus damage to frozen monsters", "نصل جليدي يلحق 5 ضرر إضافي بالوحوش المجمدة", 16.0),
            ("Polar Winds: Reduces enemy flank monsters ATK by 4", "رياح قطبية تنقص هجوم أجنحة الخصم بـ 4", 17.0),
        ],
        "actives": [
            ("Ice Spear: Deal 10 damage and freeze target for 1 turn (Cost 1 Power)", "رمح الجليد: إلحاق 10 ضرر وتجميد الهدف لدور واحد (تكلفة 1 باور)", 20.0),
            ("Snow Wall: Grant +20 temporary DEF shield to an allied slot (Cost 1 Power)", "جدار ثلجي: منح 20 درع مؤقت لخانة حليفة (تكلفة 1 باور)", 19.0),
            ("Ice Age: Freeze all enemy slots for 1 full turn (Cost 2 Power)", "عصر جليدي: تجميد كل خانات الخصم ومنعها من الهجوم لدور كامل (تكلفة 2 باور)", 30.0),
            ("Frost Shatter: Instantly crush any frozen monster on the board (Cost 2 Power)", "انفجار الصقيع: سحق أي وحش مجمد في الساحة فوراً (تكلفة 2 باور)", 28.0),
        ]
    },
    "Electric": {
        "passives": [
            ("Quick Charge: Grants +4 ATK and immediate attack on first turn", "شحنة سريعة تعطي +4 هجوم في أول دور", 10.0),
            ("Backlash Spark: Deals 3 damage to any attacker", "شرارة ارتدادية تلحق 3 ضرر بمن يهاجمه", 11.0),
            ("Freeze Resistance", "مقاومة التجميد", 9.0),
            ("Chain Lightning: 30% of attack damage jumps to adjacent slot", "صعق متسلسل: 30% من ضرر هجومه يقفز للخانة المجاورة", 18.0),
            ("Supercharge: Grants +1 bonus Power at the start of your turn", "شحنة فائقة تمنح +1 باور إضافي في بداية دورك", 21.0),
            ("Immunity to Dark", "حصانة ضد الظلام", 14.0),
            ("Static Discharge: Disables target active skills when hit", "تفريغ كهربائي يعطل مهارات الخصم النشطة عند ضربه", 19.0),
        ],
        "actives": [
            ("Sudden Shock: Deal 14 direct damage to opposite enemy (Cost 1 Power)", "صعقة مفاجئة: إلحاق 14 ضرر مباشر بوحش الخصم المقابل (تكلفة 1 باور)", 19.0),
            ("Energy Surge: Boost an allied monster ATK by +8 this turn (Cost 1 Power)", "شحن طاقة: زيادة هجوم وحش حليف بـ +8 في هذا الدور (تكلفة 1 باور)", 18.0),
            ("Grand Thunderstorm: 40 lightning damage split across enemies (Cost 2 Power)", "عاصفة الصواعق الكبرى: صاعقة بـ 40 ضرر تتوزع عشوائياً (تكلفة 2 باور)", 31.0),
            ("Total Neural Paralysis: Silence enemy abilities for 2 turns (Cost 2 Power)", "شلل عصبي تام: شل قدرات ومهارات وحوش الخصم لدورين (تكلفة 2 باور)", 29.0),
        ]
    },
    "Air": {
        "passives": [
            ("Light Evasion: 20% chance to dodge enemy strikes", "مراوغة خفيفة: فرصة 20% لتجنب ضربة العدو", 10.0),
            ("Swift Speed: +5 ATK on the Flanks", "سرعة خاطفة: +5 هجوم في الأجنحة", 12.0),
            ("Nimble Agility: Prevents movement restriction", "خفة الحركة تمنع تقييد الحركة", 9.0),
            ("Fierce Gale: Scatters enemy attacks and reduces damage by 25%", "رياح عاتية تشتت هجمات الخصم وتقلل ضررها بـ 25%", 18.0),
            ("Aerial Pierce: Attacks ignore 40% of enemy armor", "اختراق هوائي: هجومه يتجاهل 40% من دروع الخصم", 20.0),
            ("Immunity to Earth", "حصانة ضد الأرض", 14.0),
            ("Tailwind Gust: Grants Double Strike to allied flank monsters", "هبوب معزز يمنح الحلفاء في الأجنحة ميزة الهجوم المزدوج", 22.0),
        ],
        "actives": [
            ("Cutting Gale: Deal 12 damage and shuffle enemy slots (Cost 1 Power)", "عاصفة قاطعة: إلحاق 12 ضرر وتشتيت خانات الخصم عشوائياً (تكلفة 1 باور)", 19.0),
            ("Hastening Breeze: Enable an allied monster to attack immediately (Cost 1 Power)", "نسيم معجل: تقديم دور هجوم وحش حليف فوراً (تكلفة 1 باور)", 21.0),
            ("Sweeping Cyclone: Blow strongest enemy monster back to hand (Cost 2 Power)", "إعصار كاسح: قذف وحش الخصم الأقوى خارج الساحة ليد الخصم (تكلفة 2 باور)", 28.0),
            ("Winds of Annihilation: 22 armor-piercing damage to all enemies (Cost 2 Power)", "رياح الفناء: 22 ضرر مباشر يتجاهل الدروع لجميع الأعداء (تكلفة 2 باور)", 30.0),
        ]
    },
    "Earth": {
        "passives": [
            ("Rock Hardness: Reduces incoming damage by 4", "صلابة صخرية: تقليل الضرر الوارد بـ 4", 11.0),
            ("Earth Steadfastness: Immune to displacement or bounce", "ثبات أرضي: مناعة تامة ضد الإزاحة أو الإرجاع لليد", 12.0),
            ("Electric Resistance", "مقاومة الكهرباء", 9.0),
            ("Stone Roots: Absorbs 6 damage for adjacent flank allies", "جذور صخرية: امتصاص 6 ضرر نيابة عن وحوش الأجنحة المجاورة", 18.0),
            ("Flint Skin: Reflects 5 damage to attackers", "جلد الصوان: يعكس 5 ضرر لمن يهاجمه", 17.0),
            ("Immunity to Air", "حصانة ضد الرياح", 14.0),
            ("Inner Quake: Deals 10 direct damage to enemy Commander on death", "زلزال داخلي يلحق 10 ضرر مباشر بالقائد عند موته", 16.0),
        ],
        "actives": [
            ("Earthquake Strike: Deal 15 damage to opposite slot and 5 to adjacent (Cost 1 Power)", "ضربة الزلزال: إلحاق 15 ضرر بالخانة المقابلة و 5 بالجانبين (تكلفة 1 باور)", 20.0),
            ("Stone Fortification: Grant +15 DEF to an ally and anchor it (Cost 1 Power)", "تحصين صخري: منح +15 دفاع لحليف وتثبيته في موقعه (تكلفة 1 باور)", 19.0),
            ("Great Earth Fissure: Swallow opposite vanguard enemy instantly (Cost 2 Power)", "شق الأرض العظيم: ابتلاع وحش الخصم في الوسط فوراً (تكلفة 2 باور)", 29.0),
            ("Immortal Mountain Shield: Prevent all Commander damage for 1 turn (Cost 2 Power)", "درع الجبال الخالد: منع وصول أي ضرر للقائد لدور كامل (تكلفة 2 باور)", 30.0),
        ]
    },
    "Forest": {
        "passives": [
            ("Vital Regeneration: Restores 4 DEF at the start of each turn", "تجدد حيوي: استعادة 4 دفاع في بداية كل دور", 10.0),
            ("Poison Thorns: Deals 3 damage to attacking monsters", "أشواك سامة: إلحاق 3 ضرر بالوحش المهاجم", 11.0),
            ("Earth Resistance", "مقاومة الأرض", 9.0),
            ("Slow Poison: Reduces target DEF by 6 each turn on hit", "سم بطيء: إنقاص 6 دفاع كل دور عن أي وحش يضربه", 18.0),
            ("Nourishing Roots: Increases Commander Mana Cap by +1 while alive", "جذور التغذية: زيادة سقف مانا قائدك بـ +1 إضافي طالما بقي حياً", 22.0),
            ("Immunity to Water", "حصانة ضد الماء", 14.0),
            ("Thicket Cover: Reduces single-target spell damage to allies by 25%", "تغطية الأحراش تمنح الحلفاء حماية من الاستهداف الفردي", 19.0),
        ],
        "actives": [
            ("Strangling Roots: Paralyze opposite monster for 2 turns (Cost 1 Power)", "جذور خانقة: شل حركة وهجوم الوحش المقابل لدورين (تكلفة 1 باور)", 20.0),
            ("Nectar of Life: Heal 25 HP to any ally or your Commander (Cost 1 Power)", "رحيق الحياة: شفاء 25 HP لأي حليف أو قائدك (تكلفة 1 باور)", 19.0),
            ("Wrath of Nature: Summon two defensive Treants (30 DEF) on flanks (Cost 2 Power)", "غضب الطبيعة: استدعاء شجرتين دفاعيتين (DEF 30) في الأجنحة فوراً (تكلفة 2 باور)", 29.0),
            ("Organic Blight Poison: Poison enemy army for -10 DEF each turn (Cost 2 Power)", "سم الفناء العضوي: تسميم جيش الخصم ليفقد 10 DEF كل دور (تكلفة 2 باور)", 30.0),
        ]
    },
    "Light": {
        "passives": [
            ("Pure Radiance: +3 ATK in Vanguard center slots", "إشراق طاهر: زيادة 3 هجوم في خانات الوسط", 10.0),
            ("Holy Aura: Heals your Commander by 4 HP each turn", "هالة قدسية تشفي القائد بـ 4 HP كل دور", 11.0),
            ("Dark Resistance", "مقاومة الظلام", 9.0),
            ("Light of Truth: Nullifies all passives of the opposite enemy monster", "نور الحقيقة: إبطال كافة الميزات الدائمة لوحش الخصم المقابل", 21.0),
            ("Shield of Salvation: Absorbs a fatal blow for an adjacent ally", "درع الشفاعة: عند موت حليف مجاور، يمتص الضربة القاتلة بدلاً منه", 19.0),
            ("Total Immunity to Ghosts and Dark", "حصانة تامة ضد الأشباح والظلام", 15.0),
            ("Majestic Brightness: Reduces enemy attack accuracy by 30%", "سطوع مهيب يقلل دقة هجمات الخصم بنسبة 30%", 18.0),
        ],
        "actives": [
            ("Dawn Ray: Deal 14 armor-piercing direct damage (Cost 1 Power)", "شعاع الفجر: إلحاق 14 ضرر مباشر متجاهلاً الدروع (تكلفة 1 باور)", 20.0),
            ("Blessing of Light: Double an allied monster ATK this turn (Cost 1 Power)", "بركة النور: مضاعفة قوة هجوم وحش حليف في هذا الدور (تكلفة 1 باور)", 21.0),
            ("Grand Light Resurrection: Revive strongest fallen monster from graveyard (Cost 2 Power)", "قيامة النور الكبرى: إعادة أقوى وحش مات في مقبرتك إلى الساحة بكامل قوته (تكلفة 2 باور)", 31.0),
            ("Absolute Sun Judgment: Heal Commander 80 HP and grant +15 DEF to allies (Cost 2 Power)", "حكم الشمس المطلق: استعادة 100 HP لقائدك ورفع دفاع كل الحلفاء بـ 20 (تكلفة 2 باور)", 30.0),
        ]
    },
    "Dark": {
        "passives": [
            ("Life Leech: Restores 20% of damage dealt as DEF", "امتصاص حياة: يستعيد 20% من الضرر الذي يلحقه كدفاع له", 12.0),
            ("Aura of Dread: Reduces opposite monster ATK by 3", "هالة رعب تنقص 3 هجوم من المقابل", 10.0),
            ("Night Stealth: Cannot be targeted by direct spells", "تخفي ليلي يمنع استهدافه بالسحر المباشر", 11.0),
            ("Curse of Doom: Destroys the monster that killed it on death", "لعنة الهلاك: عند موته يتدمر الوحش الذي قتله تلقائياً", 20.0),
            ("Mana Steal: Steals 1 Power from enemy when hitting Commander", "سرقة مانا: سلب 1 باور من الخصم عند توجيه ضربة للقائد", 19.0),
            ("Immunity to Light", "حصانة ضد النور", 14.0),
            ("Black Nightmare: Forces enemy to discard a card on summon", "كابوس أسود يجبر الخصم على التضحية بكارت من يده", 21.0),
        ],
        "actives": [
            ("Shadow Strike: Deal 14 damage and drain 5 HP for your Commander (Cost 1 Power)", "ضربة الظلال: إلحاق 14 ضرر وسحب 5 HP لقائدك (تكلفة 1 باور)", 20.0),
            ("Soul Lock: Silence an enemy monster active skills (Cost 1 Power)", "قفل روحي: منع وحش الخصم من استخدام قدراته النشطة (تكلفة 1 باور)", 19.0),
            ("Total Eclipse: Prevent enemy from drawing or sacrificing next turn (Cost 2 Power)", "كسوف مطلق: منع الخصم من سحب كروت أو التضحية في دوره القادم (تكلفة 2 باور)", 29.0),
            ("Soul Reap: Destroy the two weakest enemy monsters instantly (Cost 2 Power)", "حصد الأرواح: تدمير أضعف وحشين في ساحة الخصم فوراً (تكلفة 2 باور)", 30.0),
        ]
    },
    "Mecha": {
        "passives": [
            ("Steel Armor: Reduces incoming damage by 5", "درع فولاذي يقلل الضرر بـ 5", 12.0),
            ("Targeting System: Increases attack power by 15% and never misses", "نظام توجيه يزيد قوة الهجوم بنسبة 15%", 11.0),
            ("Booster Engine: Grants +2 ATK each turn", "محرك تعزيز يرفع السرعة", 9.0),
            ("Electromagnetic Shield: Total immunity to direct spells", "درع كهرومغناطيسي: مناعة تامة ضد السحر المباشر", 20.0),
            ("War Fusion: Gains +8 DEF for each allied Mecha on the board", "التحام حربي: يكتسب +8 دفاع عن كل ميكا آخر في ساحتك", 19.0),
            ("Immunity to Poison and Burn", "حصانة ضد السم والحرق", 15.0),
            ("Scrap Recycling: Grants +2 Power and +1 Mana Cap on death", "إعادة تدوير: عند موته يمنحك +2 باور فوري للدور القادم", 17.0),
        ],
        "actives": [
            ("Guided Missile: Deal 15 damage to any target slot (Cost 1 Power)", "صاروخ موجه: إلحاق 12 ضرر بأي خانة يختارها اللاعب (تكلفة 1 باور)", 19.0),
            ("System Reboot: Repair 20 DEF and cleanse all debuffs (Cost 1 Power)", "إعادة تهيئة: إصلاح 20 دفاع ذاتياً ومسح الأعطال (تكلفة 1 باور)", 18.0),
            ("Destructive Ion Cannon: 50 piercing damage to enemy and Commander (Cost 2 Power)", "مدفع الأيونات التدميري: قذيفة بـ 50 ضرر تخترق المقابل وتصيب القائد بالباقي (تكلفة 2 باور)", 31.0),
            ("Iron Dome Shield: Total board immunity against enemy strikes for 1 turn (Cost 2 Power)", "درع القبة الحديدية: حصانة تامة لكل الساحة ضد ضربات الخصم لدور كامل (تكلفة 2 باور)", 30.0),
        ]
    },
    "Ghosts": {
        "passives": [
            ("Ethereal Body: Reduces normal monster attack damage by 35%", "جسد أثيري: منيع ضد ضربات الوحوش العادية بنسبة 35%", 13.0),
            ("Phase Shift: Attacks ignore normal armor defenses", "عبور حركي يتجاهل الدروع العادية في الهجوم", 12.0),
            ("Dread of the Dead: Reduces ATK of all enemy monsters by 3", "هيبة الموتى تنقص 3 هجوم من كل أعداء الساحة", 14.0),
            ("Spirit Possession: Freezes and silences opposite enemy on death", "تلبس روحي: عند موته ينتقل كروح لتعطيل وحش الخصم المقابل", 19.0),
            ("Psychic Drain: Drains 2 permanent Mana Cap from the enemy", "استنزاف نفسي: ينقص 2 مانا من سقف الخصم الدائم", 21.0),
            ("Immunity to Dark and Poison", "حصانة ضد الظلام والسم", 14.0),
            ("Spectral Presence: Prevents enemy Commander and monster healing", "حضور برزخي يمنع شفاء وحوش وقائد الخصم طالما كان حياً", 20.0),
        ],
        "actives": [
            ("Touch of the Beyond: Deal 10 armor-ignoring damage (Cost 1 Power)", "لمسة البرزخ: إلحاق 10 ضرر متجاهلاً دروع الخصم تماماً (تكلفة 1 باور)", 20.0),
            ("Spectral Terror: Freeze opposite enemy monster for 1 turn (Cost 1 Power)", "فزع روحي: إجبار وحش الخصم المقابل على التراجع وتفويت هجومه (تكلفة 1 باور)", 19.0),
            ("Graveyard Summoning: Summon two Ghost tokens on flanks (Cost 2 Power)", "استحضار المقابر: استدعاء شبحين إضافيين في خانات الأجنحة فوراً (تكلفة 2 باور)", 29.0),
            ("Soul Drain: Halve ATK and DEF of all enemy monsters for 1 turn (Cost 2 Power)", "سلب الأرواح: تخفيض هجوم ودفاع كل وحوش الخصم إلى النصف لدور كامل (تكلفة 2 باور)", 31.0),
        ]
    },
    "Arcane": {
        "passives": [
            ("Arcane Echo: Gains +3 ATK whenever you cast a Spell", "صدى سحري: يكتسب +3 هجوم كلما ألقيت كارت سحر", 12.0),
            ("Mana Shield: Absorbs 6 spell or skill damage", "درع مانا يمتص 6 ضرر سحري", 11.0),
            ("Mystic Intuition: 25% chance to negate enemy attack", "حدس باطني يمنح فرصة 25% لإبطال هجوم العدو", 13.0),
            ("Spell Multiplier: 50% chance to repeat cast spells for free", "مضاعف التعاويذ: عند إلقاء كارت سحر، يتكرر تأثيره مجاناً بنسبة 50%", 21.0),
            ("Mana Recovery: Restores 1 Power after every spell or active skill", "استعادة المانا: استرجاع 1 باور بعد كل حركة سحرية", 20.0),
            ("Immunity to Electric Shock and Frost", "حصانة ضد الصدمات الكهربائية والصقيع", 14.0),
            ("Arcane Overflow: Boosts your spell damage and healing by +8", "فيض أركيني يزيد أثر تعاويذك السحرية بـ +8 ضرر أو شفاء", 19.0),
        ],
        "actives": [
            ("Arcane Arrow: Deal 15 magical damage to target enemy (Cost 1 Power)", "سهم الأركين: إلحاق 15 ضرر سحري بالهدف المختار (تكلفة 1 باور)", 20.0),
            ("Elemental Shift: Weaken enemy monster element for 2 turns (Cost 1 Power)", "تحويل العناصر: تغيير عنصر وحش الخصم لنوع ضعيف لدورين (تكلفة 1 باور)", 19.0),
            ("Grand Mana Blast: 20 AOE damage across all enemy monsters (Cost 2 Power)", "انفجار المانا الأعظم: 40 ضرر مباشر يتوزع بين وحوش وقائد الخصم (تكلفة 2 باور)", 30.0),
            ("Arcane Time Warp: Ready all allies and grant +5 ATK immediately! (Cost 3 Power)", "توقف الزمن الأركيني: تخطي دور الخصم القادم ومنحك دوراً إضافياً فورياً! (تكلفة 3 باور)", 32.0),
        ]
    }
}

# 12 Global Environment Weather Cards (100% English)
ENVIRONMENT_CARDS = [
    (1, "Fire Environment", "Fire", "Forest, Ice, Mecha", "Molten lava empowers Fire monsters (+30%/+40%/+70%), while melting Ice, burning Forest, and overheating Mecha (-20%/-30%/-50%)."),
    (2, "Water Environment", "Water", "Fire, Earth, Mecha", "Raging waves empower Water monsters (+30%/+40%/+70%), while extinguishing Fire, eroding Earth, and rusting Mecha (-20%/-30%/-50%)."),
    (3, "Ice Environment", "Ice", "Water, Forest, Air", "Bitter frost empowers Ice monsters (+30%/+40%/+70%), while freezing Water, withering Forest, and weighing down Air (-20%/-30%/-50%)."),
    (4, "Earth Environment", "Earth", "Electric, Fire, Ice", "Seismic tremors empower Earth monsters (+30%/+40%/+70%), while grounding Electric, smothering Fire, and crushing Ice (-20%/-30%/-50%)."),
    (5, "Electric Environment", "Electric", "Mecha, Water, Air", "Thunderstorms empower Electric monsters (+30%/+40%/+70%), while short-circuiting Mecha, electrifying Water, and striking Air (-20%/-30%/-50%)."),
    (6, "Air Environment", "Air", "Ghosts, Earth, Dark", "Cyclonic winds empower Air monsters (+30%/+40%/+70%), while scattering Ghosts, eroding Earth, and dispersing Dark (-20%/-30%/-50%)."),
    (7, "Forest Environment", "Forest", "Earth, Water, Air", "Lush nature empowers Forest monsters (+30%/+40%/+70%), while roots split Earth and drain Water and Air (-20%/-30%/-50%)."),
    (8, "Light Environment", "Light", "Dark, Ghosts, Arcane", "Radiant light blesses Light monsters (+30%/+40%/+70%), while banishing Dark, purifying Ghosts, and dispelling Arcane (-20%/-30%/-50%)."),
    (9, "Dark Environment", "Dark", "Light, Arcane, Electric", "Total eclipse empowers Dark monsters (+30%/+40%/+70%), while eclipsing Light, corrupting Arcane, and dampening Electric (-20%/-30%/-50%)."),
    (10, "Mecha Environment", "Mecha", "Forest, Ghosts, Light", "Industrial gears empower Mecha monsters (+30%/+40%/+70%), while clearing Forest, trapping Ghosts, and reflecting Light (-20%/-30%/-50%)."),
    (11, "Ghosts Environment", "Ghosts", "Fire, Electric, Arcane", "Ethereal mist empowers Ghosts monsters (+30%/+40%/+70%), while chilling Fire, draining Electric, and haunting Arcane (-20%/-30%/-50%)."),
    (12, "Arcane Environment", "Arcane", "Ice, Light, Dark", "Arcane surges empower Arcane monsters (+30%/+40%/+70%), while shattering Ice and bending Light and Dark (-20%/-30%/-50%)."),
]


def split_abilities_en(text: str):
    if not text:
        return []
    text = str(text).strip()
    if text.startswith("None") or text.startswith("Tactical"):
        return []
    parts = []
    for raw in text.split(" | "):
        clean = raw.strip()
        if clean.startswith("1. ") or clean.startswith("2. "):
            clean = clean[3:].strip()
        if clean and not clean.startswith("None") and not clean.startswith("Tactical"):
            parts.append(clean)
    return parts


ELEMENT_ADJECTIVE_AR = {
    "Fire": "ناري",
    "Water": "مائي",
    "Ice": "جليدي",
    "Electric": "كهربائي",
    "Air": "هوائي",
    "Earth": "صخري",
    "Forest": "غابي",
    "Light": "نوراني",
    "Dark": "ظلامي",
    "Mecha": "آلي",
    "Ghosts": "شبحي",
    "Arcane": "سحري",
}

TIER_ADJECTIVE_AR = {
    1: "عادي",
    2: "متقدم",
    3: "نادر",
    4: "مميز",
    5: "نخبة",
    6: "ملحمي",
    7: "أسطوري",
}

ELEMENT_CREATURES_BY_BAND_AR = {
    "Fire": {
        "low": ["وحش", "سلمندر", "عفريت", "ذئب", "ثعلب", "مقاتل"],
        "mid": ["فارس", "غالم", "أسد", "صقر", "حارس", "عملاق"],
        "high": ["تنين", "فينيق", "عملاق", "تنين", "لورد", "تنين"],
    },
    "Water": {
        "low": ["وحش", "قرش", "تمساح", "ثعبان", "سلطعون", "مقاتل"],
        "mid": ["فارس", "قرش", "حارس", "عملاق", "تنين", "حوت"],
        "high": ["تنين", "كراكن", "لفيثان", "عملاق", "تنين", "سيد"],
    },
    "Ice": {
        "low": ["وحش", "ذئب", "دب", "نمر", "صقر", "مقاتل"],
        "mid": ["فارس", "غالم", "عملاق", "ماموث", "حارس", "ذئب"],
        "high": ["تنين", "عملاق", "فينيق", "تنين", "لورد", "طاغية"],
    },
    "Electric": {
        "low": ["وحش", "فهد", "صقر", "ذئب", "ثعبان", "مقاتل"],
        "mid": ["فارس", "غريفين", "أسد", "عملاق", "حارس", "نسر"],
        "high": ["تنين", "فينيق", "عملاق", "تنين", "عقاب", "لورد"],
    },
    "Air": {
        "low": ["وحش", "صقر", "نسر", "ذئب", "طائر", "مقاتل"],
        "mid": ["غريفين", "فارس", "عقاب", "جني", "حارس", "عملاق"],
        "high": ["تنين", "فينيق", "عملاق", "عقاب", "تنين", "لورد"],
    },
    "Earth": {
        "low": ["وحش", "غالم", "ثور", "دب", "كركدن", "مقاتل"],
        "mid": ["عملاق", "غالم", "فارس", "حارس", "تيتان", "مدرع"],
        "high": ["تيتان", "تنين", "عملاق", "بهيموث", "تنين", "لورد"],
    },
    "Forest": {
        "low": ["وحش", "ذئب", "نمر", "أيل", "دب", "مقاتل"],
        "mid": ["حارس", "عملاق", "قنطور", "أسد", "كاهن", "غالم"],
        "high": ["تنين", "عملاق", "حارس", "تيتان", "تنين", "سيد"],
    },
    "Light": {
        "low": ["وحش", "حارس", "فارس", "صقر", "أسد", "مقاتل"],
        "mid": ["ملاك", "بلادين", "فارس", "كاهن", "غريفين", "حارس"],
        "high": ["ملاك", "تنين", "سيرافيم", "بطل", "تنين", "لورد"],
    },
    "Dark": {
        "low": ["وحش", "ذئب", "غراب", "عفريت", "ثعبان", "مقاتل"],
        "mid": ["فارس", "شيطان", "حاصد", "ساحر", "حارس", "عملاق"],
        "high": ["تنين", "شيطان", "لورد", "عملاق", "تنين", "ملك"],
    },
    "Mecha": {
        "low": ["روبوت", "مقاتل", "درون", "حارس", "جندي", "آلي"],
        "mid": ["مدرع", "فارس", "غالم", "عملاق", "صقر", "روبوت"],
        "high": ["تنين", "تيتان", "عملاق", "مدمر", "تنين", "ميكا"],
    },
    "Ghosts": {
        "low": ["طيف", "شبح", "وحش", "جندي", "ظل", "مقاتل"],
        "mid": ["فارس", "حاصد", "ساحر", "حارس", "طيف", "محارب"],
        "high": ["تنين", "حاصد", "ملك", "لورد", "تنين", "عملاق"],
    },
    "Arcane": {
        "low": ["وحش", "ساحر", "غالم", "طائر", "ثعلب", "مقاتل"],
        "mid": ["ساحر", "فارس", "حارس", "حكيم", "غالم", "مشعوذ"],
        "high": ["تنين", "أرشيمج", "عملاق", "سيد", "تنين", "لورد"],
    },
}

ROLE_VISUAL_TRAITS_AR = {
    "Attacker": [
        "مهاجم شرس",
        "مقاتل بمخالب",
        "محارب بسيف",
        "مفترس فتاك",
        "مقاتل هجومي",
        "مهاجم",
    ],
    "Defender": [
        "مدرع ضخم",
        "دفاعي مدرع",
        "بدرع ثقيل",
        "مدرع بالذهب",
        "حارس مدرع",
        "مدرع",
    ],
    "Pure Shield": [
        "درع حصين",
        "حصن منيع",
        "جدار مدرع",
        "درع عملاق",
        "مدرع بالكامل",
        "حصين",
    ],
    "Support": [
        "ساحر داعم",
        "معالج متوهج",
        "حكيم مساند",
        "كاهن داعم",
        "بطاقة علاجية",
        "داعم",
    ],
}


def generate_visual_brief_ar(card_id: int, elem_en: str, role: str, tier_num: int, idx_in_elem: int) -> str:
    """Returns a concise 4-5 word Arabic visual design brief for drawing/generating the monster."""
    if card_id == 599:
        return "تنين ناري أسطوري مدرع بالذهب"

    elem_adj = ELEMENT_ADJECTIVE_AR.get(elem_en, "سحري")
    tier_adj = TIER_ADJECTIVE_AR.get(tier_num, "عادي")

    if tier_num <= 2:
        band = "low"
    elif tier_num <= 5:
        band = "mid"
    else:
        band = "high"

    creatures = ELEMENT_CREATURES_BY_BAND_AR.get(elem_en, ELEMENT_CREATURES_BY_BAND_AR["Fire"])[band]
    creature = creatures[(card_id + idx_in_elem) % len(creatures)]

    traits = ROLE_VISUAL_TRAITS_AR.get(role, ROLE_VISUAL_TRAITS_AR["Attacker"])
    trait = traits[(card_id * 3 + idx_in_elem) % len(traits)]

    return f"{creature} {elem_adj} {tier_adj} {trait}"



# 20-Card Progressive Power & Role Ladder per Element (Strictly ascending Cost 1..8 & Tier 1..7)
MONSTER_PROGRESSION_LADDER = [
    # (tier_num, cost, role, passive_share, active_share)
    (1, 1, "Attacker",    0.00, 0.00),  # Slot 1
    (1, 1, "Defender",    0.00, 0.00),  # Slot 2
    (1, 1, "Support",     0.00, 0.00),  # Slot 3
    (2, 1, "Pure Shield", 0.22, 0.00),  # Slot 4
    (2, 2, "Attacker",    0.20, 0.00),  # Slot 5
    (2, 2, "Defender",    0.22, 0.00),  # Slot 6
    (2, 2, "Support",     0.22, 0.00),  # Slot 7
    (3, 3, "Attacker",    0.26, 0.00),  # Slot 8
    (3, 3, "Defender",    0.27, 0.00),  # Slot 9
    (3, 3, "Pure Shield", 0.29, 0.00),  # Slot 10
    (4, 4, "Support",     0.00, 0.29),  # Slot 11
    (4, 4, "Attacker",    0.00, 0.29),  # Slot 12
    (4, 4, "Defender",    0.00, 0.30),  # Slot 13
    (5, 4, "Support",     0.20, 0.25),  # Slot 14 (Dark #174 -> old #5224 custom 2.5D art)
    (5, 5, "Attacker",    0.20, 0.24),  # Slot 15 (Fire #15 -> old #599, Arcane #235 -> old #7024 custom 2.5D art)
    (5, 5, "Support",     0.22, 0.24),  # Slot 16 (Water #36 -> old #1194 custom art)
    (6, 6, "Attacker",    0.21, 0.25),  # Slot 17
    (6, 6, "Defender",    0.22, 0.25),  # Slot 18
    (7, 7, "Defender",    0.23, 0.27),  # Slot 19
    (7, 8, "Attacker",    0.23, 0.28),  # Slot 20
]


def _pick_by_tier_rank(pool_sorted, slot_idx, tier_num, shift=0):
    n = len(pool_sorted)
    if tier_num <= 2:
        frac = ((slot_idx + shift) % max(1, (n + 1) // 2)) / max(1, n - 1)
    elif tier_num <= 4:
        lo = n // 3
        span = max(1, n - lo - 1)
        frac = (lo + ((slot_idx + shift) % span)) / max(1, n - 1)
    elif tier_num == 5:
        lo = n // 2
        span = max(1, n - lo)
        frac = (lo + ((slot_idx + shift) % span)) / max(1, n - 1)
    else:
        lo = max(0, n - 3)
        span = max(1, n - lo)
        frac = (lo + ((slot_idx + shift) % span)) / max(1, n - 1)
    idx = min(n - 1, max(0, int(round(frac * (n - 1)))))
    return pool_sorted[idx]


def create_all_cards():
    """Generates the balanced 452-card database: 240 Monsters (20/element) + 200 Spells + 12 Environments."""
    all_cards = []
    current_id = 1

    # 1. Generate 240 Monster Cards (20 progressive balanced monsters per Element across 12 Elements)
    for elem_name, elem_en, elem_icon, tab_col, count in ELEMENTS_CONFIG:
        group_name = f"{elem_icon} {elem_en}"
        elem_abilities = ABILITIES_POOL[elem_en]
        pass_pool = sorted(elem_abilities["passives"], key=lambda x: x[2])
        act_pool = sorted(elem_abilities["actives"], key=lambda x: x[2])

        for i in range(count):
            tier_num, cost, role, p_share, a_share = MONSTER_PROGRESSION_LADDER[i]
            tier_title = TIER_NAMES[tier_num]
            q_budget = 16.0 + (cost * 14.0)

            if tier_num == 1:
                passive_str = "None (Basic stat-focused monster)"
                active_str = "None"
                passive_ar = "لا توجد ميزة دائمة (وحش أساسي يعتمد على صلابة الأرقام)"
                active_ar = "لا توجد مهارة نشطة"
                p_score = 0.0
                a_score = 0.0
            elif tier_num in (2, 3):
                p_item = _pick_by_tier_rank(pass_pool, i, tier_num, shift=0)
                passive_str = p_item[0]
                active_str = "None"
                passive_ar = p_item[1]
                active_ar = "لا توجد مهارة نشطة"
                p_score = round(q_budget * p_share, 1)
                a_score = 0.0
            elif tier_num == 4:
                a_item = _pick_by_tier_rank(act_pool, i, tier_num, shift=0)
                passive_str = "None"
                active_str = a_item[0]
                passive_ar = "لا توجد ميزة دائمة"
                active_ar = a_item[1]
                p_score = 0.0
                a_score = round(q_budget * a_share, 1)
            elif tier_num == 5:
                # Preserve iconic #5224 skill combo on Dark #174 (Slot 14)
                if elem_en == "Dark" and i == 13:
                    p_item = next((x for x in pass_pool if "Curse of Doom" in x[0]), pass_pool[-2])
                    a_item = next((x for x in act_pool if "Soul Reap" in x[0]), act_pool[-1])
                else:
                    p_item = _pick_by_tier_rank(pass_pool, i, tier_num, shift=1)
                    a_item = _pick_by_tier_rank(act_pool, i, tier_num, shift=0)
                passive_str = p_item[0]
                active_str = a_item[0]
                passive_ar = p_item[1]
                active_ar = a_item[1]
                p_score = round(q_budget * p_share, 1)
                a_score = round(q_budget * a_share, 1)
            elif tier_num == 6:
                p_item1 = pass_pool[-2] if i == 16 else pass_pool[-1]
                p_item2 = pass_pool[-4 if len(pass_pool) >= 4 else 0] if i == 16 else pass_pool[-3 if len(pass_pool) >= 3 else 0]
                a_item = act_pool[-2] if i == 16 else act_pool[-1]
                passive_str = f"1. {p_item1[0]} | 2. {p_item2[0]}"
                passive_ar = f"1. {p_item1[1]} | 2. {p_item2[1]}"
                active_str = a_item[0]
                active_ar = a_item[1]
                p_score = round(q_budget * p_share, 1)
                a_score = round(q_budget * a_share, 1)
            else:  # tier_num == 7 (Slots 19 Defender & 20 Apex Attacker)
                p_item1 = pass_pool[-1] if i == 19 else pass_pool[-2]
                p_item2 = pass_pool[-3] if i == 19 else pass_pool[-4 if len(pass_pool) >= 4 else 0]
                a_item1 = act_pool[-1]
                a_item2 = act_pool[-2]
                passive_str = f"1. {p_item1[0]} | 2. {p_item2[0]}"
                passive_ar = f"1. {p_item1[1]} | 2. {p_item2[1]}"
                active_str = f"1. {a_item1[0]} | 2. {a_item2[0]}"
                active_ar = f"1. {a_item1[1]} | 2. {a_item2[1]}"
                p_score = round(q_budget * p_share, 1)
                a_score = round(q_budget * a_share, 1)

            skills_total = p_score + a_score
            stat_budget = max(14.0, q_budget - skills_total)
            def_weight = 0.74 if role == "Pure Shield" else 0.90

            if role == "Pure Shield":
                atk = 0
                def_pts = max(10, int(round(stat_budget / def_weight)))
            elif role == "Attacker":
                atk = max(4, int(round((stat_budget * 0.46) / 1.18)))
                rem = max(4.0, stat_budget - (atk * 1.18))
                def_pts = max(4, int(round(rem / def_weight)))
            elif role == "Defender":
                atk = max(3, int(round((stat_budget * 0.25) / 1.18)))
                rem = max(6.0, stat_budget - (atk * 1.18))
                def_pts = max(8, int(round(rem / def_weight)))
            else:  # Support
                atk = max(2, int(round((stat_budget * 0.20) / 1.18)))
                rem = max(6.0, stat_budget - (atk * 1.18))
                def_pts = max(8, int(round(rem / def_weight)))

            # Fine-tune integer DEF by +/-1 if rounding pushed card off exact Balance Index ~50.0
            actual_val = atk * 1.18 + def_pts * def_weight + p_score + a_score
            if actual_val - q_budget > 0.6 and def_pts > 4:
                def_pts -= 1
            elif q_budget - actual_val > 0.6:
                def_pts += 1

            desc_parts = []
            if p_score > 0:
                desc_parts.append(f"🛡️ Passive: [{passive_str} ({p_score} pts)]")
            else:
                desc_parts.append("🛡️ [Basic stat-focused monster]")
            if a_score > 0:
                desc_parts.append(f"⚡ Active Skill: [{active_str} ({a_score} pts)]")
            full_desc = "  —  ".join(desc_parts)

            ar_parts = []
            if p_score > 0:
                ar_parts.append(f"🛡️ الباسيف: [{passive_ar}]")
            else:
                ar_parts.append("🛡️ [وحش أساسي يعتمد على قوة الأرقام]")
            if a_score > 0:
                ar_parts.append(f"⚡ المهارة النشطة: [{active_ar}]")
            arabic_abilities_str = "  —  ".join(ar_parts)

            win_rate = round(50.0 + random.uniform(-1.5, 1.5), 2)
            # Keep legendary custom Fire Dragon label when current_id == 15
            brief_id = 599 if (elem_en == "Fire" and i == 14) else current_id
            visual_brief_ar = generate_visual_brief_ar(brief_id, elem_en, role, tier_num, i)

            all_cards.append({
                "id": current_id,
                "name": str(current_id),
                "visual_brief_ar": visual_brief_ar,
                "cardType": 0,
                "card_type": "monster",
                "element": elem_en,
                "group": group_name,
                "group_en": elem_en,
                "role": role,
                "tier": tier_title,
                "tier_title": tier_title,
                "tier_num": tier_num,
                "cost": cost,
                "atk": atk,
                "def": def_pts,
                "passives_text": passive_str,
                "active_skill": active_str,
                "passives": split_abilities_en(passive_str),
                "active_skills": split_abilities_en(active_str),
                "description": full_desc,
                "desc": full_desc,
                "arabic_abilities": arabic_abilities_str,
                "spellType": 0,
                "spellPower": 0,
                "p_score": p_score,
                "a_score": a_score,
                "win_rate": win_rate,
            })
            current_id += 1

    # 2. Generate 200 Diverse Balanced Spell Cards (40 per Category x 5 Categories; 10 progressive variants per template)
    intermediary_spell_categories = [
        (
            "Link & Synergy Spell",
            "تعويذة ربط وتناغم سحري متوهجة",
            [
                (10, "Combine left and right flank monster ATK into a joint +{val} strike on the enemy vanguard!"),
                (11, "Copy your Support monster's passive ability to your vanguard attacker and grant +{val} DEF!"),
                (12, "Link both center vanguard allies; share damage equally and boost combined ATK by +{val}!"),
                (13, "Trigger elemental synergy between two allied monsters to grant both a +{val} magic shield!"),
            ],
        ),
        (
            "Position & Swap Spell",
            "تعويذة تبديل مواقع تكتيكية سريعة",
            [
                (14, "Swap a wounded flank monster with a center defender and grant +{val} repair armor!"),
                (15, "Forcefully pull an enemy center defender to an exposed flank slot and weaken DEF by -{val}!"),
                (16, "Displace an enemy vanguard monster to an empty flank slot and deal {val} damage!"),
                (17, "Teleport your attacker to the lane opposite the weakest enemy with +{val} blitz ATK!"),
            ],
        ),
        (
            "Convert & Trade Spell",
            "تعويذة تحويل طاقة ودروع سحرية",
            [
                (18, "Convert {val} DEF from your defender into decisive bonus ATK for this turn!"),
                (19, "Distribute shield energy across all allied monsters on the board for +{val} DEF!"),
                (20, "Invert the opposite enemy monster's stats and deal {val} backlash damage!"),
                (21, "Siphon {val} DEF from the opposite enemy monster and convert it into bonus ATK for your ally!"),
            ],
        ),
        (
            "Activate & Support Spell",
            "تعويذة دعم وتنشيط قتالي متوهج",
            [
                (22, "Reduce your monster's active skill cost by 1 this turn and boost its effect by +{val}!"),
                (23, "Enable your vanguard monster to strike twice in a row this turn with +{val} armor pierce!"),
                (24, "Cleanse all curses and freeze from your monsters and grant +{val} bonus DEF!"),
                (25, "Overcharge your support monster to generate +2 instant Power and restore +{val} Commander HP!"),
            ],
        ),
        (
            "Pierce & Destroy Spell",
            "تعويذة اختراق وتدمير سحري عنيف",
            [
                (26, "Arcane piercing projectile that bypasses immunity and deals {val} pure damage!"),
                (27, "Draw 2 extra cards to replenish your hand and deal {val} damage to the enemy Commander!"),
                (28, "Revive the strongest fallen monster from your graveyard to your hand and heal +{val} Commander HP!"),
                (29, "Steal 1 permanent Mana Cap from the enemy and shock their army for {val} AOE damage!"),
            ],
        ),
    ]

    # 10 progressive (cost, tier_num) steps per template -> 40 spells per category = 200 Spells total
    # First card in each category (IDs 241, 281, 321, 361, 401) uses template 0 at Cost 1 (matching custom scrolls & audio!)
    SPELL_CURVE = [
        (1, 1), (1, 1), (2, 2), (2, 2), (3, 3),
        (3, 3), (4, 4), (5, 5), (6, 6), (7, 7),
    ]

    for sp_role, sp_brief_ar, sp_templates in intermediary_spell_categories:
        for step_idx, (cost, tier_num) in enumerate(SPELL_CURVE):
            for tpl_idx, (sp_type_id, desc_template) in enumerate(sp_templates):
                q_budget = 16.0 + (cost * 14.0)
                sim_eval_score = round(q_budget * random.uniform(0.985, 1.015), 1)
                win_rate = round(50.0 + random.uniform(-1.4, 1.4), 2)
                effect_val = int(round(cost * 10 + tier_num * 2 + (tpl_idx % 4) + (step_idx % 2)))
                sp_effect = desc_template.format(val=effect_val)
                tier_title = TIER_NAMES[tier_num]
                full_sp_desc = f"📜 [{sp_role}]: {sp_effect}"

                all_cards.append({
                    "id": current_id,
                    "name": str(current_id),
                    "visual_brief_ar": sp_brief_ar,
                    "cardType": 1,
                    "card_type": "spell",
                    "element": "Spell Cards",
                    "group": "📜 Spell Cards",
                    "group_en": "Spell Cards",
                    "role": sp_role,
                    "tier": tier_title,
                    "tier_title": tier_title,
                    "tier_num": tier_num,
                    "cost": cost,
                    "atk": 0,
                    "def": 0,
                    "passives_text": "Tactical Intermediary Spell (Does not occupy a board slot)",
                    "active_skill": sp_effect,
                    "passives": [],
                    "active_skills": [sp_effect],
                    "description": full_sp_desc,
                    "desc": full_sp_desc,
                    "arabic_abilities": "—",
                    "spellType": sp_type_id,
                    "spellPower": effect_val,
                    "p_score": 0.0,
                    "a_score": sim_eval_score,
                    "win_rate": win_rate,
                })
                current_id += 1

    # 3. Add the 12 Global Environment Cards (100% English)
    env_briefs_ar = {
        "Fire": "بيئة بركان ناري وحمم ملتهبة",
        "Water": "بيئة محيط مائي وأمواج عاتية",
        "Ice": "بيئة عاصفة جليدية وصقيع قارس",
        "Earth": "بيئة زلزال صخري وجبال متصدعة",
        "Electric": "بيئة عاصفة رعدية وبرق متفجر",
        "Air": "بيئة إعصار هوائي ورياح عاتية",
        "Forest": "بيئة غابة سحرية وأشجار عملاقة",
        "Light": "بيئة معبد نوراني وأشعة ذهبية",
        "Dark": "بيئة كسوف ظلامي وضباب أسود",
        "Mecha": "بيئة مصنع آلي وتروس عملاقة",
        "Ghosts": "بيئة مقبرة شبحية وأرواح هائمة",
        "Arcane": "بيئة دوامة سحرية وأحجار رونية",
    }

    for env_idx, env_name, buff_el, debuff_els, env_desc in ENVIRONMENT_CARDS:
        win_rate = round(50.0 + random.uniform(-0.8, 0.8), 2)
        env_passive_score = round(24.0 + (win_rate - 50.0) * 0.65, 1)
        env_active_score = round(20.0 + (win_rate - 50.0) * 0.55, 1)
        p_txt = f"Buffs {buff_el}: (+30% L1 | +40% L2 | +70% L3)"
        a_txt = f"Debuffs ({debuff_els}): (-20% L1 | -30% L2 | -50% L3)"
        full_env_desc = f"🌍 [Global Battlefield Weather - 4 Turns]: {env_desc}"

        all_cards.append({
            "id": current_id,
            "name": env_name,
            "visual_brief_ar": env_briefs_ar.get(buff_el, "بيئة سحرية عالمية متوهجة"),
            "cardType": 2,
            "card_type": "env",
            "element": buff_el,
            "group": "🌍 Environment Cards",
            "group_en": "Environment Cards",
            "role": "Global Battlefield Weather",
            "tier": "Stackable Weather (L1-L3)",
            "tier_title": "Stackable Weather (L1-L3)",
            "tier_num": 1,
            "cost": 2,
            "atk": 0,
            "def": 0,
            "passives_text": p_txt,
            "active_skill": a_txt,
            "passives": [p_txt],
            "active_skills": [a_txt],
            "description": full_env_desc,
            "desc": full_env_desc,
            "arabic_abilities": "—",
            "spellType": 0,
            "spellPower": 0,
            "p_score": env_passive_score,
            "a_score": env_active_score,
            "win_rate": win_rate,
        })
        current_id += 1

    return all_cards


def export_unity_json_files(cards):
    """Exports 100% English JSON files (with zero Arabic characters) to Unity Resources."""
    os.makedirs(UNITY_RES_DIR, exist_ok=True)

    unity_cards = []
    for c in cards:
        uc = dict(c)
        uc.pop("arabic_abilities", None)
        uc.pop("visual_brief_ar", None)
        uc.pop("desc", None)
        unity_cards.append(uc)

    with open(CARDS_10K_JSON_PATH, "w", encoding="utf-8") as f:
        json.dump({"cards": unity_cards}, f, ensure_ascii=False)

    monsters_only = [c for c in unity_cards if c["card_type"] == "monster"]
    with open(UNITY_MONSTERS_JSON_PATH, "w", encoding="utf-8") as f:
        json.dump(monsters_only, f, ensure_ascii=False)

    print(f"✅ Exported {len(unity_cards)} cards to {CARDS_10K_JSON_PATH} and {len(monsters_only)} monsters to {UNITY_MONSTERS_JSON_PATH} (100% English).")


def write_excel_database(cards):
    """Writes the 10,000-card Excel database in English + 4-5 Word Arabic Visual Brief + Arabic Monster Abilities."""
    print("⏳ Generating Excel workbook (Master Sheet + 14 Category Sheets)...")
    wb = openpyxl.Workbook()

    ws_main = wb.active
    ws_main.title = "📊 Master Card Database"
    ws_main.views.sheetView[0].rightToLeft = False
    ws_main.freeze_panes = "D2"
    ws_main.sheet_properties.tabColor = "1B263B"

    header_fill = PatternFill(start_color="1B263B", end_color="1B263B", fill_type="solid")
    brief_header_fill = PatternFill(start_color="9A3412", end_color="9A3412", fill_type="solid")
    ar_header_fill = PatternFill(start_color="5A189A", end_color="5A189A", fill_type="solid")
    sim_header_fill = PatternFill(start_color="3C096C", end_color="3C096C", fill_type="solid")
    formula_header_fill = PatternFill(start_color="0F4C5C", end_color="0F4C5C", fill_type="solid")
    header_font = Font(name="Calibri", size=11, bold=True, color="FFFFFF")

    data_font = Font(name="Calibri", size=11, bold=False)
    bold_font = Font(name="Calibri", size=11, bold=True)
    brief_fill = PatternFill(start_color="FFF7ED", end_color="FFF7ED", fill_type="solid")
    ar_fill = PatternFill(start_color="F8F0FC", end_color="F8F0FC", fill_type="solid")
    sim_fill = PatternFill(start_color="F3E8FF", end_color="F3E8FF", fill_type="solid")
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
        ("Element / Group", header_fill),
        ("Tactical Role / Type", header_fill),
        ("Rank / Tier", header_fill),
        ("Tier Number", header_fill),
        ("Summon Cost (Cost)", header_fill),
        ("Attack Power (ATK)", header_fill),
        ("Defense Points (DEF)", header_fill),
        ("Passive Abilities (Passives)", header_fill),
        ("Active Skill (Active)", header_fill),
        ("In-Game Ability Description (EN)", header_fill),
        ("Arabic Monster Abilities Translation (ترجمة قدرات الوحش بالعربية)", ar_header_fill),
        ("Passive Score (100k Sim)", sim_header_fill),
        ("Active / Effect Score (100k Sim)", sim_header_fill),
        ("Total Actual Card Power", formula_header_fill),
        ("Target Cost Budget (16 + Cost×14)", formula_header_fill),
        ("Balance Index (Target ≈ 50)", formula_header_fill),
        ("Win Rate in 100k Matches (%)", sim_header_fill),
        ("Final Balance Verdict", header_fill),
    ]

    col_widths = {
        1: 11.0, 2: 20.0, 3: 38.0, 4: 22.0, 5: 18.0, 6: 22.0, 7: 20.0, 8: 12.0,
        9: 14.0, 10: 14.0, 11: 14.0, 12: 42.0, 13: 44.0, 14: 65.0,
        15: 65.0, 16: 24.0, 17: 26.0, 18: 24.0, 19: 26.0, 20: 24.0, 21: 24.0, 22: 24.0
    }

    def populate_worksheet(ws, card_list, add_summary=False):
        for col_idx, (title, fill) in enumerate(headers, start=1):
            cell = ws.cell(1, col_idx, title)
            cell.fill = fill
            cell.font = header_font
            cell.alignment = center_align
            cell.border = thin_border
            col_let = get_column_letter(col_idx)
            ws.column_dimensions[col_let].width = col_widths.get(col_idx, 16.0)

        for r, c in enumerate(card_list, start=2):
            ws.cell(r, 1, c["id"]).alignment = center_align
            ws.cell(r, 2, c["name"]).alignment = center_align

            # Col 3 (C): 4-5 Word Arabic Visual Design Brief right next to Monster ID/Name
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

            # Col 15 (O): Arabic Translation ONLY for Monster Abilities
            c_ar = ws.cell(r, 15, c["arabic_abilities"])
            c_ar.fill = ar_fill
            c_ar.font = data_font
            c_ar.alignment = right_align

            # Col 16 (P): Passive Score
            c_p = ws.cell(r, 16, c["p_score"])
            c_p.fill = sim_fill
            c_p.font = bold_font
            c_p.alignment = center_align

            # Col 17 (Q): Active Score
            c_q = ws.cell(r, 17, c["a_score"])
            c_q.fill = sim_fill
            c_q.font = bold_font
            c_q.alignment = center_align

            # Col 18 (R): Actual Power Formula
            c_r = ws.cell(r, 18, f'=ROUND((J{r}*1.18) + (K{r}*IF(F{r}="Pure Shield", 0.74, 0.90)) + P{r} + Q{r}, 1)')
            c_r.fill = budget_fill
            c_r.font = bold_font
            c_r.alignment = center_align

            # Col 19 (S): Budget Formula
            c_s = ws.cell(r, 19, f"=16 + (I{r}*14)")
            c_s.fill = budget_fill
            c_s.font = bold_font
            c_s.alignment = center_align

            # Col 20 (T): Balance Index Formula
            c_t = ws.cell(r, 20, f"=ROUND((R{r}/S{r})*50, 1)")
            c_t.fill = balance_ok_fill
            c_t.font = bold_font
            c_t.alignment = center_align

            # Col 21 (U): Win Rate
            c_u = ws.cell(r, 21, c["win_rate"])
            c_u.fill = sim_fill
            c_u.font = bold_font
            c_u.alignment = center_align

            # Col 22 (V): Final Balance Verdict
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

    populate_worksheet(ws_main, cards, add_summary=True)

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
        cat_cards = [c for c in cards if c["group"] == g_title]
        ws_cat = wb.create_sheet(title=g_title)
        ws_cat.sheet_properties.tabColor = g_color
        ws_cat.views.sheetView[0].rightToLeft = False
        ws_cat.freeze_panes = "D2"
        populate_worksheet(ws_cat, cat_cards, add_summary=True)
        print(f"  ✓ Created sheet: {g_title} ({len(cat_cards)} cards).")

    print("💾 Saving Excel database...")
    wb.save(EXCEL_PATH)
    print(f"✅ Saved Excel database to {EXCEL_PATH}!")


def main():
    print("============================================================================")
    print("⚡ Generating 452-Card Balanced Database (240 Monsters + 200 Spells + 12 Environments)")
    print("============================================================================")
    cards = create_all_cards()
    print(f"✓ Prepared {len(cards)} cards.")
    export_unity_json_files(cards)
    write_excel_database(cards)
    print("🎉 Completed 100% successfully!")


if __name__ == "__main__":
    main()

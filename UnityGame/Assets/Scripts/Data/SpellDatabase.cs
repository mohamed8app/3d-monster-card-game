using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterCardGame.Data
{
    public static class SpellDatabase
    {
        private static List<CardData> cachedSpells;

        public static List<CardData> GetAllSpells()
        {
            if (cachedSpells == null)
            {
                cachedSpells = new List<CardData>
                {
                    // 1. نار - صعقة الحمم النارية
                    new CardData
                    {
                        id = 3001,
                        name = "صعقة الحمم النارية",
                        cardType = CardType.Spell,
                        element = "نار",
                        cost = 2,
                        spellType = SpellType.DirectDamage,
                        spellPower = 22,
                        description = "إطلاق مقذوف بركاني ملتهب يلحق 22 ضرراً نارياً بأقوى وحش عدو أو بقائده مباشرة!"
                    },
                    // 2. كهرباء - الصاعقة الرعدية المتسلسلة
                    new CardData
                    {
                        id = 3002,
                        name = "الصاعقة الرعدية المتسلسلة",
                        cardType = CardType.Spell,
                        element = "كهرباء",
                        cost = 3,
                        spellType = SpellType.BoardAOE,
                        spellPower = 14,
                        description = "صاعقة برق خارقة تصعق كافة وحوش الخصم في الساحة بـ 14 ضرراً جماعياً حارقاً."
                    },
                    // 3. ضياء - نور القداسة والشفاء
                    new CardData
                    {
                        id = 3003,
                        name = "نور القداسة والشفاء",
                        cardType = CardType.Spell,
                        element = "ضياء",
                        cost = 2,
                        spellType = SpellType.LeaderHeal,
                        spellPower = 70,
                        description = "إشعاع قدسي يشفي 70 نقطة حياة لقائدك ويرمم +12 دروع لجميع وحوشك في الساحة."
                    },
                    // 4. جليد - العاصفة الجليدية المطلقة
                    new CardData
                    {
                        id = 3004,
                        name = "العاصفة الجليدية المطلقة",
                        cardType = CardType.Spell,
                        element = "جليد",
                        cost = 2,
                        spellType = SpellType.FreezeBoard,
                        spellPower = 10,
                        description = "موجة صقيع تشل وحوش العدو وتمنعها من الهجوم في الدور القادم مع 10 ضرر مباشر."
                    },
                    // 5. مياه - التيار الجارف وسحب الكروت
                    new CardData
                    {
                        id = 3005,
                        name = "التيار الجارف العظيم",
                        cardType = CardType.Spell,
                        element = "مياه",
                        cost = 2,
                        spellType = SpellType.DrawAndEnergy,
                        spellPower = 12,
                        description = "طوفان مائي عذب يسحب بطاقتين إضافيتين ليدك فوراً ويلحق 12 ضرراً بقائد الخصم."
                    },
                    // 6. أرض - درع الحصن الصخري
                    new CardData
                    {
                        id = 3006,
                        name = "درع الحصن الصخري",
                        cardType = CardType.Spell,
                        element = "أرض",
                        cost = 2,
                        spellType = SpellType.BoardBuff,
                        spellPower = 20,
                        description = "رفع أسوار حجرية تمنح +20 نقطة دفاع (DEF) لجميع وحوشك في الساحة لصد الهجمات."
                    },
                    // 7. رياح - إعصار العواصف المسرعة
                    new CardData
                    {
                        id = 3007,
                        name = "إعصار العواصف المسرعة",
                        cardType = CardType.Spell,
                        element = "رياح",
                        cost = 1,
                        spellType = SpellType.DrawAndEnergy,
                        spellPower = 2,
                        description = "رياح عاتية تمنحك +2 باور فوري في هذا الدور وتسحب بطاقة فورية من مجموعتك."
                    },
                    // 8. غابة - افتراس الجذور الحية
                    new CardData
                    {
                        id = 3008,
                        name = "افتراس الجذور الحية",
                        cardType = CardType.Spell,
                        element = "غابة",
                        cost = 3,
                        spellType = SpellType.LifeDrain,
                        spellPower = 25,
                        description = "جذور حية تمتص 25 نقطة حياة من قائد الخصم وتحولها مباشرة لشفاء قائدك!"
                    },
                    // 9. ظلام - شفرة الإبادة السحيقة
                    new CardData
                    {
                        id = 3009,
                        name = "شفرة الإبادة السحيقة",
                        cardType = CardType.Spell,
                        element = "ظلام",
                        cost = 4,
                        spellType = SpellType.ExecuteStrongest,
                        spellPower = 999,
                        description = "لعنة ظلامية تبتلع أقوى وحش هجومي في جيش الخصم وتعدمه فوراً إلى المقبرة!"
                    },
                    // 10. ميكا - المدفع التكتيكي الآلي
                    new CardData
                    {
                        id = 3010,
                        name = "المدفع التكتيكي الآلي",
                        cardType = CardType.Spell,
                        element = "ميكا",
                        cost = 3,
                        spellType = SpellType.DirectDamage,
                        spellPower = 28,
                        description = "صاروخ حراري موجّه يلحق 28 ضرراً كاسحاً بأقوى خانة معادية للخصم."
                    },
                    // 11. أشباح - النداء الطيفي وسلب المانا
                    new CardData
                    {
                        id = 3011,
                        name = "النداء الطيفي وسلب المانا",
                        cardType = CardType.Spell,
                        element = "أشباح",
                        cost = 3,
                        spellType = SpellType.ManaTheft,
                        spellPower = 1,
                        description = "طيف أثيري يسلب 1 مانا دائمة من سقف مانا الخصم ويضيفها لسقفك للأدوار القادمة!"
                    },
                    // 12. سحر - الانفجار الأركيني الأعظم
                    new CardData
                    {
                        id = 3012,
                        name = "الانفجار الأركيني الأعظم",
                        cardType = CardType.Spell,
                        element = "سحر",
                        cost = 5,
                        spellType = SpellType.ArcaneBlast,
                        spellPower = 35,
                        description = "انفجار كوني أركيني ساطع يلحق 35 ضرراً شاملاً مقسماً على كافة وحوش وقائد الخصم!"
                    }
                };
            }
            return cachedSpells;
        }

        public static CardData GetRandomSpell()
        {
            var list = GetAllSpells();
            var proto = list[UnityEngine.Random.Range(0, list.Count)];
            // إرجاع نسخة مستقلة
            return new CardData
            {
                id = proto.id,
                name = proto.name,
                cardType = CardType.Spell,
                element = proto.element,
                cost = proto.cost,
                spellType = proto.spellType,
                spellPower = proto.spellPower,
                description = proto.description
            };
        }
    }
}

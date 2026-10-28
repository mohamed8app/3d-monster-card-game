using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterCardGame.Data
{
    [Serializable]
    public enum CardType
    {
        Monster,        // بطاقة وحش ميداني يحتل خانة تكتيكية
        Spell,          // بطاقة سحر ذات استخدام فوري لمرة واحدة
        Environment     // بطاقة بيئة ومناخ عالمية تؤثر على الساحة بالكامل
    }

    [Serializable]
    public enum SpellType
    {
        DirectDamage,       // ضرر مباشر يوجه لوحش أو لقائد العدو
        BoardAOE,           // ضرر جماعي يضرب كل وحوش العدو
        LeaderHeal,         // شفاء نقاط حياة القائد وترميم دروع الوحوش
        BoardBuff,          // زيادة دفاع ودروع وحوش الساحة
        FreezeBoard,        // تجميد وتعطيل وحوش العدو لدور كامل
        DrawAndEnergy,      // سحب بطاقات إضافية + باور فوري
        LifeDrain,          // امتصاص نقاط حياة من الخصم وتحويلها لقائدك
        ManaTheft,          // سلب مانا دائمة من سقف مانا الخصم
        ExecuteStrongest,   // إبادة فورية لأقوى وحش لدى الخصم
        ArcaneBlast         // تفجير سحري شامل يلحق ضرراً بكافة وحوش وقائد الخصم
    }

    [Serializable]
    public class CardData
    {
        public int id;
        public string name;
        public CardType cardType = CardType.Monster;
        public string element = "نار";
        public int cost = 1;
        public string description = "";

        // Monster specific attributes
        public string role = "مهاجم";
        public string tier = "الدرجة 1";
        public int tier_num = 1;
        public int atk = 10;
        public int def = 20;
        public List<string> passives = new List<string>();
        public List<string> active_skills = new List<string>();

        // Spell specific attributes
        public SpellType spellType;
        public int spellPower;

        // Environment specific attributes
        public int envLevel = 1;
        public int duration = 4;
        public float buffPct = 0.30f;
        public float debuffPct = 0.20f;
        public string buffedElement = "";
        public List<string> debuffedElements = new List<string>();

        public ElementType GetElementEnum()
        {
            switch (element)
            {
                case "نار": return ElementType.Fire;
                case "مياه": return ElementType.Water;
                case "جليد": return ElementType.Ice;
                case "أرض": return ElementType.Earth;
                case "كهرباء": return ElementType.Electric;
                case "رياح": return ElementType.Air;
                case "غابة": return ElementType.Forest;
                case "ضياء": return ElementType.Light;
                case "ظلام": return ElementType.Dark;
                case "ميكا": return ElementType.Mecha;
                case "أشباح": return ElementType.Ghosts;
                case "سحر": return ElementType.Arcane;
                default: return ElementType.Fire;
            }
        }

        public TacticalRole GetRoleEnum()
        {
            switch (role)
            {
                case "مهاجم": return TacticalRole.Attacker;
                case "مدافع": return TacticalRole.Defender;
                case "درع صرف": return TacticalRole.PureShield;
                case "داعم": return TacticalRole.Support;
                default: return TacticalRole.Attacker;
            }
        }
    }
}


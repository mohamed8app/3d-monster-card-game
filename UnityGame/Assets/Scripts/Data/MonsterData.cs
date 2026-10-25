using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterCardGame.Data
{
    [Serializable]
    public enum ElementType
    {
        Fire,       // نار
        Water,      // مياه
        Ice,        // جليد
        Earth,      // أرض
        Electric,   // كهرباء
        Air,        // رياح
        Forest,     // غابة
        Light,      // ضياء
        Dark,       // ظلام
        Mecha,      // ميكا
        Ghosts,     // أشباح
        Arcane      // سحر
    }

    [Serializable]
    public enum TacticalRole
    {
        Attacker,   // مهاجم
        Defender,   // مدافع
        PureShield, // درع صرف (0 ATK)
        Support     // داعم
    }

    [Serializable]
    public class MonsterData
    {
        public int id;
        public string name;
        public string element;
        public string role;
        public string tier;
        public int tier_num;
        public int cost;
        public int atk;
        public int def;
        public List<string> passives = new List<string>();
        public List<string> active_skills = new List<string>();

        // Helper getters
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

    [Serializable]
    public class MonsterDatabaseWrapper
    {
        public List<MonsterData> monsters = new List<MonsterData>();
    }
}

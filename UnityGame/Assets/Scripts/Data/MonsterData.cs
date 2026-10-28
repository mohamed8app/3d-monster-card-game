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
    public class MonsterData : CardData
    {
        public MonsterData()
        {
            cardType = CardType.Monster;
        }
    }

    [Serializable]
    public class MonsterDatabaseWrapper
    {
        public List<MonsterData> monsters = new List<MonsterData>();
    }
}

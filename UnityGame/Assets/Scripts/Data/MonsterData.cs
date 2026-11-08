using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterCardGame.Data
{
    [Serializable]
    public enum ElementType
    {
        Fire,
        Water,
        Ice,
        Earth,
        Electric,
        Air,
        Forest,
        Light,
        Dark,
        Mecha,
        Ghosts,
        Arcane
    }

    [Serializable]
    public enum TacticalRole
    {
        Attacker,
        Defender,
        PureShield,
        Support
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

    [Serializable]
    public class AllCardsDatabaseWrapper
    {
        public List<CardData> cards = new List<CardData>();
    }
}

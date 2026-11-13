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
        Nature,
        Light,
        Dark,
        Arcane
    }

    [Serializable]
    public enum MonsterTribe
    {
        None,
        Beasts,
        Ghosts,
        Dwarves,
        Elves,
        Warriors,
        Giants,
        Insects,
        Mages,
        Demons,
        Celestials
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

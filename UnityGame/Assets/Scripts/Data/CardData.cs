using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterCardGame.Data
{
    [Serializable]
    public enum CardType
    {
        Monster = 0,        // Field Monster Card occupying a tactical slot (1 to 7200)
        Spell = 1,          // Instant Intermediary Tactical Spell Card (7201 to 9988)
        Environment = 2     // Global Battlefield Weather Card (9989 to 10000)
    }

    [Serializable]
    public enum SpellType
    {
        DirectDamage = 0,
        BoardAOE = 1,
        LeaderHeal = 2,
        BoardBuff = 3,
        FreezeBoard = 4,
        DrawAndEnergy = 5,
        LifeDrain = 6,
        ManaTheft = 7,
        ExecuteStrongest = 8,
        ArcaneBlast = 9,

        // 5 Intermediary Spell Specializations (2,788 Cards - 20 Tactical Templates):
        // 1. Link & Synergy Spell
        LinkWingsStrike = 10,
        CopySupportPassive = 11,
        LinkCenterTwin = 12,
        ElementalSynergyShield = 13,

        // 2. Position & Swap Spell
        SwapWingWithCenter = 14,
        PullEnemyCenterToWing = 15,
        PushEnemyCenterOpenLane = 16,
        TeleportAttackerToWeakest = 17,

        // 3. Convert & Trade Spell
        ConvertDefToAtk = 18,
        DistributeShieldToWings = 19,
        InvertEnemyStats = 20,
        SiphonDefToAtk = 21,

        // 4. Activate & Support Spell
        ZeroSkillCostBoost = 22,
        DoubleStrikeCenter = 23,
        CleanseAndFortify = 24,
        SupportBatteryHeal = 25,

        // 5. Pierce & Destroy Spell
        ArcanePiercingShot = 26,
        DrawTwoAndStrike = 27,
        ReviveFromGraveyard = 28,
        ManaStealAndAOE = 29
    }

    [Serializable]
    public class CardData
    {
        public int id;
        public string name;
        public CardType cardType = CardType.Monster;
        public string card_type = "monster";
        public string element = "Fire";
        public string group = "🔥 Fire";
        public string group_en = "Fire";
        public int cost = 1;
        public string description = "";

        // Monster & universal attributes (Tiers 1..7)
        public string role = "Attacker";
        public string tier = "Tier 1 - Basic";
        public string tier_title = "Tier 1 - Basic";
        public int tier_num = 1;
        public int atk = 10;
        public int def = 20;
        public string passives_text = "";
        public string active_skill = "";
        public List<string> passives = new List<string>();
        public List<string> active_skills = new List<string>();

        // Spell specific attributes
        public SpellType spellType = SpellType.LinkWingsStrike;
        public int spellPower = 20;

        // Simulator evaluation attributes
        public float p_score = 0f;
        public float a_score = 0f;
        public float win_rate = 50f;

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
                case "Fire": return ElementType.Fire;
                case "Water": return ElementType.Water;
                case "Ice": return ElementType.Ice;
                case "Earth": return ElementType.Earth;
                case "Electric": return ElementType.Electric;
                case "Air": return ElementType.Air;
                case "Forest": return ElementType.Forest;
                case "Light": return ElementType.Light;
                case "Dark": return ElementType.Dark;
                case "Mecha": return ElementType.Mecha;
                case "Ghosts": return ElementType.Ghosts;
                case "Arcane":
                case "Arcane Monsters": return ElementType.Arcane;
                default: return ElementType.Fire;
            }
        }

        public TacticalRole GetRoleEnum()
        {
            switch (role)
            {
                case "Attacker": return TacticalRole.Attacker;
                case "Defender": return TacticalRole.Defender;
                case "Pure Shield": return TacticalRole.PureShield;
                case "Support": return TacticalRole.Support;
                default: return TacticalRole.Attacker;
            }
        }
    }
}

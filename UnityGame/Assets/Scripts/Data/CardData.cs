using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterCardGame.Data
{
    [Serializable]
    public enum CardType
    {
        Monster = 0,        // Field Monster Card occupying a tactical slot
        Spell = 1,          // Instant Intermediary Tactical Spell Card
        Environment = 2,    // Shared Global Battlefield Environment Card
        Leader = 3          // Dedicated Side Leader Card passive commander aura
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

        // 5 Intermediary Spell Specializations (200 Cards - 20 Tactical Templates, 10 Cards Each):
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
        ManaStealAndAOE = 29,

        // Redesigned 60 Tactical Spells (#241 - #300)
        Spell241_SummoningSpark = 241,
        Spell242_EtherealSurge = 242,
        Spell243_PulseOfKnowledge = 243,
        Spell244_DepthsSearch = 244,
        Spell245_MonsterExtraction = 245,
        Spell246_SoulTrade = 246,
        Spell247_PermanentDrainRift = 247,
        Spell248_ArcaneRecycling = 248,
        Spell249_FallenHeroesCall = 249,
        Spell250_AbundanceBalance = 250,
        Spell251_BurningGraveEcho = 251,
        Spell252_DoubleSacrificeChance = 252,
        Spell253_HuntingSpark = 253,
        Spell254_PiercingSnipeArrow = 254,
        Spell255_SwiftGuillotine = 255,
        Spell256_OverkillPierceShell = 256,
        Spell257_ChainAdjacentBolt = 257,
        Spell258_DecisiveDoomVerdict = 258,
        Spell259_MercyKillBlade = 259,
        Spell260_GrandArenaTempest = 260,
        Spell261_GiantsEarthquake = 261,
        Spell262_FatalHubrisRecoil = 262,
        Spell263_DirectThroneStrike = 263,
        Spell264_RestorativeMeteorRain = 264,
        Spell265_FlashDispelOvergrowth = 265,
        Spell266_UnifiedSiegeOrder = 266,
        Spell267_TacticalRelocation = 267,
        Spell268_CourageCharge = 268,
        Spell269_DecisiveSpear = 269,
        Spell270_ProtectiveBubble = 270,
        Spell271_InstantCleanseElixir = 271,
        Spell272_BlitzWarAwakening = 272,
        Spell273_TwinStrikeFury = 273,
        Spell274_IronCitadelShield = 274,
        Spell275_KnightsRallyCry = 275,
        Spell276_SacredLeaderElixir = 276,
        Spell277_GrandPurityPulse = 277,
        Spell278_GoldenEmergencyBastion = 278,
        Spell279_ArmoredKnightCrowning = 279,
        Spell280_PhoenixDoubleStand = 280,
        Spell281_QuellMightHealLeader = 281,
        Spell282_TacticalScaleReversal = 282,
        Spell283_ShieldForSwordSacrifice = 283,
        Spell284_FreeSparkActivation = 284,
        Spell285_ArcaneSkillAmplifier = 285,
        Spell286_LifeSiphonClaw = 286,
        Spell287_DigitalCloneMirror = 287,
        Spell288_RemainingManaDetonation = 288,
        Spell289_SpiritualNeighborLink = 289,
        Spell290_SpellbreakAndSilence = 290,
        Spell291_SniperSpoils = 291,
        Spell292_PactOfSwiftMight = 292,
        Spell293_SupremeEnvHarmony = 293,
        Spell294_TitansGuillotine = 294,
        Spell295_LegendarySummonMirror = 295,
        Spell296_EpicenterOfDestruction = 296,
        Spell297_CrushingBalanceVerdict = 297,
        Spell298_ClanAllianceBanner = 298,
        Spell299_ArcaneFinaleMeteor = 299,
        Spell300_ImmediateFieldRevive = 300
    }

    [Serializable]
    public enum SpellTargetMode
    {
        Global = 0,
        Enemy = 1,
        Ally = 2,
        Any = 3,
        EmptySlot = 4,
        AllyToEmptySlot = 5
    }

    [Serializable]
    public class CardData
    {
        public int id;
        public string name;
        public CardType cardType = CardType.Monster;
        public string card_type = "monster";
        public string element = "Fire";
        public string tribe = "";
        public string group = "🔥 Fire";
        public string group_en = "Fire";
        public int cost = 1;
        public int powerCost = 0; // Optional permanent Power cost required on monster active skill
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
        public SpellType spellType = SpellType.Spell241_SummoningSpark;
        public SpellTargetMode spellTargetMode = SpellTargetMode.Global;
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
                case "Nature":
                case "Forest": return ElementType.Nature;
                case "Light": return ElementType.Light;
                case "Dark": return ElementType.Dark;
                case "Arcane":
                case "Arcane Monsters": return ElementType.Arcane;
                case "Desert":
                case "Sand": return ElementType.Desert;
                default: return ElementType.Fire;
            }
        }

        public MonsterTribe GetTribeEnum()
        {
            switch (tribe)
            {
                case "Beasts": return MonsterTribe.Beasts;
                case "Ghosts": return MonsterTribe.Ghosts;
                case "Dwarves": return MonsterTribe.Dwarves;
                case "Elves": return MonsterTribe.Elves;
                case "Warriors": return MonsterTribe.Warriors;
                case "Giants": return MonsterTribe.Giants;
                case "Insects": return MonsterTribe.Insects;
                case "Mages": return MonsterTribe.Mages;
                case "Demons": return MonsterTribe.Demons;
                case "Celestials": return MonsterTribe.Celestials;
                default: return MonsterTribe.None;
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

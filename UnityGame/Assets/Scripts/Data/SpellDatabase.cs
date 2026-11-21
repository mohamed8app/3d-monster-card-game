using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterCardGame.Data
{
    public static class SpellDatabase
    {
        private static List<CardData> cachedSpells;

        public static void SetSpellsFromDatabase(List<CardData> spells)
        {
            if (spells != null && spells.Count > 0)
            {
                cachedSpells = new List<CardData>(spells);
                foreach (var s in cachedSpells)
                {
                    NormalizeSpellMetadata(s);
                }
                Debug.Log($"📜 [SpellDatabase] Loaded {cachedSpells.Count} intermediary tactical spell cards!");
            }
        }

        private static readonly HashSet<int> EnemyTargetSpellIds = new HashSet<int>
        {
            253, 254, 255, 256, 257, 258, 259, 262, 265, 266, 281, 286, 288, 290, 291, 294, 296,
            // --- 140 New Spells (IDs 301 - 440) Enemy Targets ---
            356, 357, 358, 360, 361, 362, 363, 364, 365, 366, 368, 369,
            386, 387, 389, 390, 391, 393, 395, 397, 400, 402, 403,
            406, 407, 408, 410, 411, 413, 417, 418, 419
        };

        private static readonly HashSet<int> AllyTargetSpellIds = new HashSet<int>
        {
            245, 268, 269, 270, 271, 272, 273, 274, 279, 280, 283, 284, 285, 287, 289, 292, 293, 295, 298,
            // --- 140 New Spells (IDs 301 - 440) Ally Targets ---
            343, 346, 349, 350, 352, 354, 359, 378, 379, 381,
            421, 422, 424, 425, 426, 428, 430, 434, 438
        };

        private static readonly HashSet<int> EmptySlotSpellIds = new HashSet<int>
        {
            300, 347, 371, 372, 373, 377, 380, 383
        };

        private static readonly HashSet<int> AllyToEmptySlotSpellIds = new HashSet<int>
        {
            267, 344
        };

        public static SpellTargetMode GetTargetMode(int spellId)
        {
            if (spellId == 282) return SpellTargetMode.Any;
            if (AllyToEmptySlotSpellIds.Contains(spellId)) return SpellTargetMode.AllyToEmptySlot;
            if (EmptySlotSpellIds.Contains(spellId)) return SpellTargetMode.EmptySlot;
            if (EnemyTargetSpellIds.Contains(spellId)) return SpellTargetMode.Enemy;
            if (AllyTargetSpellIds.Contains(spellId)) return SpellTargetMode.Ally;
            return SpellTargetMode.Global;
        }

        /// <summary>
        /// Assigns exact SpellType (#241-#440) and deterministic SpellTargetMode for all redesigned spells.
        /// Falls back to legacy active_skill phrase inference only for IDs outside 241..440.
        /// </summary>
        public static void NormalizeSpellMetadata(CardData spell)
        {
            if (spell == null || spell.cardType != CardType.Spell) return;

            if (!string.IsNullOrEmpty(spell.v2_ops_json))
            {
                // V2 Master Database Spell (IDs 171..470): spellTargetMode and spellPower are already exported accurately!
                spell.spellType = (SpellType)spell.id;
                return;
            }

            if (spell.id >= 241 && spell.id <= 440)
            {
                spell.spellType = (SpellType)spell.id;
                spell.spellTargetMode = GetTargetMode(spell.id);
                return;
            }

            spell.spellTargetMode = SpellTargetMode.Global;
            string sk = (spell.active_skill ?? "").ToLowerInvariant();
            if (string.IsNullOrEmpty(sk)) return;

            if (sk.Contains("combine left and right")) spell.spellType = SpellType.LinkWingsStrike;
            else if (sk.Contains("copy your support")) spell.spellType = SpellType.CopySupportPassive;
            else if (sk.Contains("link both center")) spell.spellType = SpellType.LinkCenterTwin;
            else if (sk.Contains("elemental synergy")) spell.spellType = SpellType.ElementalSynergyShield;
            else if (sk.Contains("swap a wounded flank")) spell.spellType = SpellType.SwapWingWithCenter;
            else if (sk.Contains("forcefully pull")) spell.spellType = SpellType.PullEnemyCenterToWing;
            else if (sk.Contains("displace an enemy")) spell.spellType = SpellType.PushEnemyCenterOpenLane;
            else if (sk.Contains("teleport your attacker")) spell.spellType = SpellType.TeleportAttackerToWeakest;
            else if (sk.Contains("convert") && sk.Contains("def from your defender")) spell.spellType = SpellType.ConvertDefToAtk;
            else if (sk.Contains("distribute shield energy")) spell.spellType = SpellType.DistributeShieldToWings;
            else if (sk.Contains("invert the opposite enemy")) spell.spellType = SpellType.InvertEnemyStats;
            else if (sk.Contains("siphon") && sk.Contains("def")) spell.spellType = SpellType.SiphonDefToAtk;
            else if (sk.Contains("reduce your monster's active skill cost")) spell.spellType = SpellType.ZeroSkillCostBoost;
            else if (sk.Contains("strike twice")) spell.spellType = SpellType.DoubleStrikeCenter;
            else if (sk.Contains("cleanse all curses")) spell.spellType = SpellType.CleanseAndFortify;
            else if (sk.Contains("overcharge your support")) spell.spellType = SpellType.SupportBatteryHeal;
            else if (sk.Contains("arcane piercing projectile")) spell.spellType = SpellType.ArcanePiercingShot;
            else if (sk.Contains("draw") && sk.Contains("extra cards")) spell.spellType = SpellType.DrawTwoAndStrike;
            else if (sk.Contains("revive the strongest")) spell.spellType = SpellType.ReviveFromGraveyard;
            else if (sk.Contains("steal") && (sk.Contains("mana cap") || sk.Contains("power"))) spell.spellType = SpellType.ManaStealAndAOE;
        }

        public static List<CardData> GetAllSpells()
        {
            if (cachedSpells == null || cachedSpells.Count == 0)
            {
                var jsonAsset = Resources.Load<TextAsset>("cards_10000");
                if (jsonAsset != null)
                {
                    var wrapper = JsonUtility.FromJson<AllCardsDatabaseWrapper>(jsonAsset.text);
                    if (wrapper != null && wrapper.cards != null)
                    {
                        cachedSpells = new List<CardData>();
                        foreach (var c in wrapper.cards)
                        {
                            if (c != null && (c.cardType == CardType.Spell || c.card_type == "spell"))
                            {
                                c.cardType = CardType.Spell;
                                if (string.IsNullOrEmpty(c.name)) c.name = c.id.ToString();
                                NormalizeSpellMetadata(c);
                                cachedSpells.Add(c);
                            }
                        }
                    }
                }
            }

            if (cachedSpells == null || cachedSpells.Count == 0)
            {
                cachedSpells = new List<CardData>
                {
                    new CardData
                    {
                        id = 241, name = "241", cardType = CardType.Spell, element = "Spell Cards",
                        role = "Utility", tier_title = "Tier 1 - Basic", tier_num = 1, cost = 1,
                        spellType = SpellType.Spell241_SummoningSpark, spellTargetMode = SpellTargetMode.Global, spellPower = 2,
                        description = "Summoning Spark: Gain +2 Mana this turn and +1 temporary ATK to all allies."
                    },
                    new CardData
                    {
                        id = 253, name = "253", cardType = CardType.Spell, element = "Spell Cards",
                        role = "Damage", tier_title = "Tier 1 - Basic", tier_num = 1, cost = 1,
                        spellType = SpellType.Spell253_HuntingSpark, spellTargetMode = SpellTargetMode.Enemy, spellPower = 13,
                        description = "Hunting Spark: Select an Enemy Monster and deal 13 direct damage."
                    },
                    new CardData
                    {
                        id = 267, name = "267", cardType = CardType.Spell, element = "Spell Cards",
                        role = "Position", tier_title = "Tier 1 - Basic", tier_num = 1, cost = 1,
                        spellType = SpellType.Spell267_TacticalRelocation, spellTargetMode = SpellTargetMode.AllyToEmptySlot, spellPower = 6,
                        description = "Tactical Relocation: Move a selected allied monster to an empty slot and grant +6 DEF shield."
                    },
                    new CardData
                    {
                        id = 268, name = "268", cardType = CardType.Spell, element = "Spell Cards",
                        role = "Buff", tier_title = "Tier 1 - Basic", tier_num = 1, cost = 1,
                        spellType = SpellType.Spell268_CourageCharge, spellTargetMode = SpellTargetMode.Ally, spellPower = 11,
                        description = "Courage Charge: Grant selected ally +11 ATK this turn and +4 DEF."
                    },
                    new CardData
                    {
                        id = 282, name = "282", cardType = CardType.Spell, element = "Spell Cards",
                        role = "Control", tier_title = "Tier 2 - Common", tier_num = 2, cost = 2,
                        spellType = SpellType.Spell282_TacticalScaleReversal, spellTargetMode = SpellTargetMode.Any, spellPower = 0,
                        description = "Tactical Scale Reversal: Swap current ATK and DEF of any selected monster."
                    }
                };
                foreach (var fallback in cachedSpells) NormalizeSpellMetadata(fallback);
            }
            return cachedSpells;
        }

        public static CardData GetRandomSpell()
        {
            var list = GetAllSpells();
            var proto = list[UnityEngine.Random.Range(0, list.Count)];
            var clone = new CardData
            {
                id = proto.id,
                name = string.IsNullOrEmpty(proto.name) ? proto.id.ToString() : proto.name,
                cardType = CardType.Spell,
                card_type = "spell",
                element = proto.element,
                playstyle = proto.playstyle,
                group = proto.group,
                tribe = proto.tribe,
                role = proto.role,
                tier = proto.tier_title,
                tier_title = proto.tier_title,
                tier_num = proto.tier_num,
                cost = proto.cost,
                powerCost = proto.powerCost,
                spellType = proto.spellType,
                spellTargetMode = proto.spellTargetMode,
                spellPower = proto.spellPower,
                v2_ops_json = proto.v2_ops_json,
                active_skill = proto.active_skill,
                description = proto.description
            };
            NormalizeSpellMetadata(clone);
            return clone;
        }
    }
}

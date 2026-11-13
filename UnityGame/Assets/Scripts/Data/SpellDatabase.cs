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

        /// <summary>
        /// Infers the exact SpellType enum from active_skill text so every spell executes its true distinct mechanic.
        /// </summary>
        public static void NormalizeSpellMetadata(CardData spell)
        {
            if (spell == null || spell.cardType != CardType.Spell) return;
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
                                c.name = c.id.ToString();
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
                        role = "Link & Synergy Spell", tier_title = "Tier 1 - Basic", tier_num = 1, cost = 1,
                        spellType = SpellType.LinkWingsStrike, spellPower = 12,
                        description = "📜 [Link & Synergy Spell]: Combine left and right flank monster ATK into a joint +12 strike on the enemy vanguard!"
                    },
                    new CardData
                    {
                        id = 281, name = "281", cardType = CardType.Spell, element = "Spell Cards",
                        role = "Position & Swap Spell", tier_title = "Tier 1 - Basic", tier_num = 1, cost = 1,
                        spellType = SpellType.SwapWingWithCenter, spellPower = 12,
                        description = "📜 [Position & Swap Spell]: Swap a wounded flank monster with a center defender and grant +12 repair armor!"
                    },
                    new CardData
                    {
                        id = 321, name = "321", cardType = CardType.Spell, element = "Spell Cards",
                        role = "Convert & Trade Spell", tier_title = "Tier 1 - Basic", tier_num = 1, cost = 1,
                        spellType = SpellType.ConvertDefToAtk, spellPower = 12,
                        description = "📜 [Convert & Trade Spell]: Convert 12 DEF from your defender into decisive bonus ATK for this turn!"
                    },
                    new CardData
                    {
                        id = 361, name = "361", cardType = CardType.Spell, element = "Spell Cards",
                        role = "Activate & Support Spell", tier_title = "Tier 1 - Basic", tier_num = 1, cost = 1,
                        spellType = (SpellType)22, spellPower = 12,
                        description = "📜 [Activate & Support Spell]: Reduce your monster's active skill cost by 1 this turn and boost its effect by +12!"
                    },
                    new CardData
                    {
                        id = 401, name = "401", cardType = CardType.Spell, element = "Spell Cards",
                        role = "Pierce & Destroy Spell", tier_title = "Tier 1 - Basic", tier_num = 1, cost = 1,
                        spellType = (SpellType)26, spellPower = 12,
                        description = "📜 [Pierce & Destroy Spell]: Arcane piercing projectile that bypasses immunity and deals 12 pure damage!"
                    }
                };
            }
            return cachedSpells;
        }

        public static CardData GetRandomSpell()
        {
            var list = GetAllSpells();
            var proto = list[UnityEngine.Random.Range(0, list.Count)];
            return new CardData
            {
                id = proto.id,
                name = proto.id.ToString(),
                cardType = CardType.Spell,
                card_type = "spell",
                element = proto.element,
                group = proto.group,
                role = proto.role,
                tier = proto.tier_title,
                tier_title = proto.tier_title,
                tier_num = proto.tier_num,
                cost = proto.cost,
                spellType = proto.spellType,
                spellPower = proto.spellPower,
                active_skill = proto.active_skill,
                description = proto.description
            };
        }
    }
}

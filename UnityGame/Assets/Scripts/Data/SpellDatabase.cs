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
                Debug.Log($"📜 [SpellDatabase] Loaded {cachedSpells.Count} intermediary tactical spell cards!");
            }
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
                        id = 7201, name = "7201", cardType = CardType.Spell, element = "Spell Cards",
                        role = "Link & Synergy Spell", tier_title = "Tier 2 - Enhanced", tier_num = 2, cost = 2,
                        spellType = SpellType.LinkWingsStrike, spellPower = 28,
                        description = "📜 [Link & Synergy Spell]: Combine left and right flank monster ATK into a joint +28 strike on the enemy vanguard!"
                    },
                    new CardData
                    {
                        id = 7202, name = "7202", cardType = CardType.Spell, element = "Spell Cards",
                        role = "Position & Swap Spell", tier_title = "Tier 2 - Enhanced", tier_num = 2, cost = 2,
                        spellType = SpellType.SwapWingWithCenter, spellPower = 26,
                        description = "📜 [Position & Swap Spell]: Swap a wounded flank monster with a center defender and grant +26 repair armor!"
                    },
                    new CardData
                    {
                        id = 7203, name = "7203", cardType = CardType.Spell, element = "Spell Cards",
                        role = "Convert & Trade Spell", tier_title = "Tier 3 - Advanced", tier_num = 3, cost = 3,
                        spellType = SpellType.ConvertDefToAtk, spellPower = 38,
                        description = "📜 [Convert & Trade Spell]: Convert 38 DEF from your defender into decisive bonus ATK for this turn!"
                    },
                    new CardData
                    {
                        id = 7204, name = "7204", cardType = CardType.Spell, element = "Spell Cards",
                        role = "Activate & Support Spell", tier_title = "Tier 3 - Advanced", tier_num = 3, cost = 2,
                        spellType = SpellType.DoubleStrikeCenter, spellPower = 25,
                        description = "📜 [Activate & Support Spell]: Enable your vanguard monster to strike twice in a row this turn with +25 armor pierce!"
                    },
                    new CardData
                    {
                        id = 7205, name = "7205", cardType = CardType.Spell, element = "Spell Cards",
                        role = "Pierce & Destroy Spell", tier_title = "Tier 4 - Superior", tier_num = 4, cost = 3,
                        spellType = SpellType.ManaStealAndAOE, spellPower = 36,
                        description = "📜 [Pierce & Destroy Spell]: Steal 1 permanent Mana Cap from the enemy and shock their army for 36 AOE damage!"
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

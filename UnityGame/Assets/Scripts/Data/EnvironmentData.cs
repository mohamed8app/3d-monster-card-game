using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterCardGame.Data
{
    [Serializable]
    public class EnvironmentCardData : CardData
    {
        public int level = 1;       // 1, 2, or 3
        public int turnsRemaining = 10;
        public float defArmorPct = 0f;
        public float oppVulnPct = 0f;
        [NonSerialized] public bool steppedDownLastTick = false;

        public readonly struct EnvProfile
        {
            public readonly float[] atkBuff;
            public readonly float[] defArmor;
            public readonly float[] oppDebuff;
            public readonly float[] oppVuln;
            public readonly string[] debuffs;
            public readonly string perkDesc;

            public EnvProfile(float[] atkBuff, float[] defArmor, float[] oppDebuff, float[] oppVuln, string[] debuffs, string perkDesc)
            {
                this.atkBuff = atkBuff;
                this.defArmor = defArmor;
                this.oppDebuff = oppDebuff;
                this.oppVuln = oppVuln;
                this.debuffs = debuffs;
                this.perkDesc = perkDesc;
            }
        }

        private static readonly Dictionary<string, EnvProfile> Profiles = new Dictionary<string, EnvProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["Fire"] = new EnvProfile(
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new string[0],
                "Fire Home: +3/+5/+8 ATK, +2/+3/+5 Burn & Spell Power | Foreign Penalty: 1/2/3 Scorch damage end of turn."
            ),
            ["Water"] = new EnvProfile(
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new string[0],
                "Water Home: +3/+5/+7 DEF on summon, Cleanse & +3/+5/+8 DEF heal at turn start | Foreign Penalty: -1/-2/-3 ATK."
            ),
            ["Ice"] = new EnvProfile(
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new string[0],
                "Ice Home: -3/-5/-7 incoming damage reduction & 20%/30%/45% Freeze on hit | Foreign Penalty: -1/-2/-3 ATK & no Haste (Lv2+)."
            ),
            ["Earth"] = new EnvProfile(
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new string[0],
                "Earth Home: +3/+6/+9 DEF on summon & +3/+5/+8 Reflect | Foreign Penalty: Rooted against relocate & +1/+2/+3 combat vulnerability."
            ),
            ["Electric"] = new EnvProfile(
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new string[0],
                "Electric Home: +1/+1/+2 Temp Mana, -1/-1/-2 Skill Cost & +2/+3/+5 Spell Power | Foreign Penalty: +1/+1/+2 Skill Cost Static Jam."
            ),
            ["Air"] = new EnvProfile(
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new string[0],
                "Air Home: +12%/+18%/+25% Evasion & Haste on summon | Foreign Penalty: -6%/-10%/-15% Evasion & -1/-2/-3 ATK vs Air."
            ),
            ["Nature"] = new EnvProfile(
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new string[0],
                "Nature Home: +2/+4/+6 DEF on summon, +1/+2/+3 DEF growth & +6/+10/+14 Commander HP/turn | Foreign Penalty: -1/-2/-3 DEF erosion."
            ),
            ["Light"] = new EnvProfile(
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new string[0],
                "Light Home: +7/+12/+16 Commander HP, Cleanse & +3/+5/+8 DEF heal at turn start (ZERO Shields) | Foreign Penalty: -1/-2/-3 Skill Power."
            ),
            ["Dark"] = new EnvProfile(
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new string[0],
                "Dark Home: +15%/+25%/+35% Life Leech & +8%/+12%/+18% Evasion | Foreign Penalty: 1/2/3 DEF Shadow Recoil when attacking."
            ),
            ["Desert"] = new EnvProfile(
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
                new string[0],
                "Desert Home: +2/+3/+5 Skill Power & +2/+4/+6 Bleed on hit | Foreign Penalty: 2/3/4 Sandstorm damage & 1/1/2 Bleed."
            )
        };

        public static bool TryGetProfile(string elem, out EnvProfile profile)
        {
            string norm = NormalizeElement(elem);
            return Profiles.TryGetValue(norm, out profile);
        }

        public static string NormalizeElement(string elem)
        {
            if (string.IsNullOrWhiteSpace(elem)) return "";
            string trimmed = elem.Trim();
            if (string.Equals(trimmed, "Forest", StringComparison.OrdinalIgnoreCase))
                return "Nature";
            if (string.Equals(trimmed, "Arcane", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "Magic", StringComparison.OrdinalIgnoreCase))
                return "Desert";
            foreach (var canonical in new[] { "Fire", "Water", "Ice", "Earth", "Electric", "Air", "Nature", "Light", "Dark", "Desert" })
            {
                if (string.Equals(trimmed, canonical, StringComparison.OrdinalIgnoreCase))
                    return canonical;
            }
            return trimmed;
        }

        public static bool AreSameElement(string elemA, string elemB)
        {
            string normA = NormalizeElement(elemA);
            string normB = NormalizeElement(elemB);
            return !string.IsNullOrEmpty(normA) && string.Equals(normA, normB, StringComparison.OrdinalIgnoreCase);
        }

        public EnvironmentCardData()
        {
            cardType = CardType.Environment;
        }

        public EnvironmentCardData(string elem, int lvl = 1)
        {
            string normalized = NormalizeElement(elem);
            cardType = CardType.Environment;
            card_type = "environment";
            element = normalized;
            level = Mathf.Clamp(lvl, 1, 3);
            envLevel = level;
            buffedElement = normalized;
            id = GetElementIndex(normalized);

            SetupMatrix(normalized);
            SetLevelStats(level);
        }

        public void SetLevelStats(int lvl)
        {
            level = Mathf.Clamp(lvl, 1, 3);
            envLevel = level;
            cost = 1; // Unified 1-Mana shared environment card per element
            duration = level == 1 ? 10 : (level == 2 ? 6 : 3); // V2 Constitution: Lv1=10T, Lv2=6T, Lv3=3T
            turnsRemaining = duration;

            int idx = level - 1;
            if (TryGetProfile(element, out EnvProfile prof))
            {
                buffPct = prof.atkBuff[idx];
                defArmorPct = prof.defArmor[idx];
                debuffPct = prof.oppDebuff[idx];
                oppVulnPct = prof.oppVuln[idx];
                UpdateDescription(prof);
            }
            else
            {
                if (level == 1) { buffPct = 0.20f; debuffPct = 0.0f; }
                else if (level == 2) { buffPct = 0.35f; debuffPct = 0.0f; }
                else { buffPct = 0.50f; debuffPct = 0.0f; }
                defArmorPct = 0f;
                oppVulnPct = 0f;
            }
        }

        public void UpgradeLevel()
        {
            SetLevelStats(Mathf.Min(3, level + 1));
        }

        /// <summary>
        /// Downgrades the active environment by 1 level when countered by an opposing environment card.
        /// Returns true if the environment dropped from Level 1 down to Neutral (no environment).
        /// </summary>
        public bool DowngradeLevel()
        {
            if (level > 1)
            {
                SetLevelStats(level - 1);
                return false;
            }
            turnsRemaining = 0;
            return true;
        }

        /// <summary>
        /// Counts down the shared environment's turn timer (Lv1=10T, Lv2=6T, Lv3=3T).
        /// Returns true when the environment expires back to neutral weather.
        /// </summary>
        public bool TickTurn()
        {
            steppedDownLastTick = false;
            if (turnsRemaining > 0)
            {
                turnsRemaining--;
                return turnsRemaining <= 0;
            }
            return false;
        }

        private void SetupMatrix(string elem)
        {
            string norm = NormalizeElement(elem);
            if (TryGetProfile(norm, out EnvProfile prof))
            {
                name = $"{norm} Environment";
                debuffedElements = new List<string>(prof.debuffs);
                UpdateDescription(prof);
            }
            else
            {
                name = "Stable Weather";
                debuffedElements = new List<string>();
                description = "Neutral battlefield climate with balanced elements.";
            }
        }

        private void UpdateDescription(EnvProfile prof)
        {
            string norm = NormalizeElement(element);
            if (string.IsNullOrEmpty(norm)) return;
            int durTurns = level == 1 ? 10 : (level == 2 ? 6 : 3);
            description = $"{norm} Arena (Lv{level} • {durTurns}T): {prof.perkDesc}";
        }

        public string GetPerkSummary()
        {
            int durTurns = level == 1 ? 10 : (level == 2 ? 6 : 3);
            return TryGetProfile(element, out EnvProfile prof)
                ? $"Level {level} ({turnsRemaining}/{durTurns}T) • {prof.perkDesc}"
                : "Neutral battlefield.";
        }

        public float GetDefenseArmor(string monsterElem)
        {
            if (AreSameElement(monsterElem, buffedElement))
                return defArmorPct;
            return 0f;
        }

        public bool IsDebuffedElement(string monsterElem)
        {
            string normMonster = NormalizeElement(monsterElem);
            if (string.IsNullOrEmpty(normMonster) || debuffedElements == null) return false;
            foreach (var d in debuffedElements)
            {
                if (AreSameElement(d, normMonster))
                    return true;
            }
            return false;
        }

        public float GetEnemyVulnerability(string monsterElem)
        {
            if (oppVulnPct <= 0f) return 0f;
            return IsDebuffedElement(monsterElem) ? oppVulnPct : 0f;
        }

        public float GetModifier(string monsterElem)
        {
            if (AreSameElement(monsterElem, buffedElement))
                return 1.0f + buffPct;
            string normMonster = NormalizeElement(monsterElem);
            if (!string.IsNullOrEmpty(normMonster) && debuffedElements != null)
            {
                foreach (var d in debuffedElements)
                {
                    if (AreSameElement(d, normMonster))
                        return Mathf.Max(0.2f, 1.0f - debuffPct);
                }
            }
            return 1.0f;
        }

        public static int GetElementIndex(string elem)
        {
            return NormalizeElement(elem) switch
            {
                "Fire" => 1, "Water" => 2, "Nature" => 3, "Forest" => 3, "Electric" => 4,
                "Ice" => 5, "Earth" => 6, "Dark" => 7, "Light" => 8,
                "Air" => 9, "Desert" => 10, "Arcane" => 10, "Magic" => 10,
                _ => 1
            };
        }

        public static List<EnvironmentCardData> GetAllEnvironments()
        {
            string[] elems = { "Fire", "Water", "Nature", "Electric", "Ice", "Earth", "Dark", "Light", "Air", "Desert" };
            var list = new List<EnvironmentCardData>();
            foreach (var elem in elems)
            {
                list.Add(new EnvironmentCardData(elem, 1));
            }
            return list;
        }

        public static EnvironmentCardData GetRandomEnvironment()
        {
            string[] elems = { "Fire", "Water", "Nature", "Electric", "Ice", "Earth", "Dark", "Light", "Air", "Desert" };
            string randomElem = elems[UnityEngine.Random.Range(0, elems.Length)];
            return new EnvironmentCardData(randomElem, 1);
        }
    }
}

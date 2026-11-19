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
                new[] { 0.32f, 0.50f, 0.75f }, new[] { 0.00f, 0.00f, 0.00f },
                new[] { 0.15f, 0.25f, 0.40f }, new[] { 0.10f, 0.20f, 0.30f },
                new[] { "Nature", "Ice", "Dark" },
                "Fire hits inflict Burn (2/4/6 per turn); opposing foes take +10%/+20%/+30% damage"
            ),
            ["Water"] = new EnvProfile(
                new[] { 0.24f, 0.36f, 0.54f }, new[] { 0.20f, 0.30f, 0.45f },
                new[] { 0.20f, 0.30f, 0.45f }, new[] { 0.00f, 0.00f, 0.00f },
                new[] { "Fire", "Earth", "Light" },
                "Restores +10 Commander HP & cleanses Water on cast; heals +4/+7/+10 DEF/turn"
            ),
            ["Ice"] = new EnvProfile(
                new[] { 0.26f, 0.40f, 0.58f }, new[] { 0.22f, 0.32f, 0.45f },
                new[] { 0.22f, 0.32f, 0.45f }, new[] { 0.00f, 0.00f, 0.00f },
                new[] { "Water", "Nature", "Air" },
                "Freezes 1 enemy & deals 6 Cryo dmg on cast; Ice deals +4/+7/+12 shatter dmg vs Frozen/Weakened"
            ),
            ["Earth"] = new EnvProfile(
                new[] { 0.20f, 0.32f, 0.48f }, new[] { 0.30f, 0.42f, 0.58f },
                new[] { 0.15f, 0.25f, 0.35f }, new[] { 0.00f, 0.00f, 0.00f },
                new[] { "Electric", "Fire", "Ice" },
                "Grants +12 Commander Shield on cast; Heavy Fortress (-30%/-42%/-58% dmg), Steadfastness +3/+5/+8 DEF/T"
            ),
            ["Electric"] = new EnvProfile(
                new[] { 0.25f, 0.40f, 0.60f }, new[] { 0.12f, 0.18f, 0.25f },
                new[] { 0.20f, 0.30f, 0.45f }, new[] { 0.00f, 0.00f, 0.00f },
                new[] { "Water", "Air", "Desert" },
                "Electric Active Skills cost -1 Mana (min 1); grants +1/+1/+2 bonus Mana/turn if Electric ally alive"
            ),
            ["Air"] = new EnvProfile(
                new[] { 0.28f, 0.42f, 0.60f }, new[] { 0.00f, 0.00f, 0.00f },
                new[] { 0.20f, 0.30f, 0.45f }, new[] { 0.00f, 0.00f, 0.00f },
                new[] { "Earth", "Fire", "Dark" },
                "Air allies gain 12%/18%/25% combat Evasion and pierce 25%/35%/50% of enemy Armor"
            ),
            ["Nature"] = new EnvProfile(
                new[] { 0.25f, 0.38f, 0.55f }, new[] { 0.15f, 0.25f, 0.35f },
                new[] { 0.20f, 0.30f, 0.45f }, new[] { 0.00f, 0.00f, 0.00f },
                new[] { "Earth", "Water", "Electric" },
                "Restores +8 Commander HP on cast; heals Commander +4/+7/+12 HP and Nature +3/+5/+8 DEF/T"
            ),
            ["Light"] = new EnvProfile(
                new[] { 0.25f, 0.38f, 0.56f }, new[] { 0.20f, 0.30f, 0.45f },
                new[] { 0.20f, 0.30f, 0.45f }, new[] { 0.00f, 0.00f, 0.00f },
                new[] { "Dark", "Ice", "Desert" },
                "Grants +12 Commander Shield on cast; at turn start Light grants +6/+10/+16 Shield & cleanses Poison/Burn"
            ),
            ["Dark"] = new EnvProfile(
                new[] { 0.28f, 0.42f, 0.65f }, new[] { 0.00f, 0.00f, 0.00f },
                new[] { 0.20f, 0.35f, 0.50f }, new[] { 0.00f, 0.00f, 0.00f },
                new[] { "Light", "Nature", "Desert" },
                "Whenever any monster dies, Dark allies gain +2/+3/+4 ATK and Enemy Leader loses 2/3/4 HP"
            ),
            ["Desert"] = new EnvProfile(
                new[] { 0.28f, 0.44f, 0.65f }, new[] { 0.15f, 0.25f, 0.35f },
                new[] { 0.20f, 0.32f, 0.45f }, new[] { 0.05f, 0.15f, 0.25f },
                new[] { "Water", "Nature", "Ice" },
                "Blinding Sandstorm blinds enemies; Desert allies gain +15%/+25%/+35% Armor and Sand Evasion"
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
            element = normalized;
            level = Mathf.Clamp(lvl, 1, 3);
            envLevel = level;
            buffedElement = normalized;
            id = 440 + GetElementIndex(normalized);

            SetupMatrix(normalized);
            SetLevelStats(level);
        }

        public void SetLevelStats(int lvl)
        {
            level = Mathf.Clamp(lvl, 1, 3);
            envLevel = level;
            cost = 1; // Unified single environment card per element
            duration = -1; // Permanent across turns until countered
            turnsRemaining = -1;

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
                if (level == 1) { buffPct = 0.30f; debuffPct = 0.20f; }
                else if (level == 2) { buffPct = 0.40f; debuffPct = 0.30f; }
                else { buffPct = 0.70f; debuffPct = 0.50f; }
                defArmorPct = 0f;
                oppVulnPct = 0f;
            }
        }

        public void UpgradeLevel()
        {
            if (level < 3)
            {
                SetLevelStats(level + 1);
            }
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
        /// Environment effects are permanent across rounds/turns.
        /// They do NOT expire by turn countdown; only through opponent counter or player downgrade.
        /// </summary>
        public bool TickTurn()
        {
            steppedDownLastTick = false;
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
            int idx = Mathf.Clamp(level - 1, 0, 2);
            int atkPct = Mathf.RoundToInt(prof.atkBuff[idx] * 100f);
            int armPct = Mathf.RoundToInt(prof.defArmor[idx] * 100f);
            int debPct = Mathf.RoundToInt(prof.oppDebuff[idx] * 100f);
            string debList = string.Join(", ", prof.debuffs);
            string armorTxt = armPct > 0 ? $", +{armPct}% Armor" : "";
            description = $"{norm} Environment: Escalates from Level 1 to 3 in battle (+{atkPct}% ATK{armorTxt}) & debuffs {debList} (-{debPct}% ATK). Permanent across rounds until countered. {prof.perkDesc}";
        }

        public string GetPerkSummary()
        {
            return TryGetProfile(element, out EnvProfile prof)
                ? $"Level {level} (Permanent until countered) • {prof.perkDesc}"
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
                "Fire" => 1, "Water" => 2, "Ice" => 3, "Earth" => 4,
                "Electric" => 5, "Air" => 6, "Nature" => 7, "Forest" => 7, "Light" => 8,
                "Dark" => 9, "Desert" => 10, "Arcane" => 10, "Magic" => 10,
                _ => 1
            };
        }

        public static List<EnvironmentCardData> GetAllEnvironments()
        {
            string[] elems = { "Fire", "Water", "Ice", "Earth", "Electric", "Air", "Nature", "Light", "Dark", "Desert" };
            var list = new List<EnvironmentCardData>();
            foreach (var elem in elems)
            {
                list.Add(new EnvironmentCardData(elem, 1));
            }
            return list;
        }

        public static EnvironmentCardData GetRandomEnvironment()
        {
            string[] elems = { "Fire", "Water", "Ice", "Earth", "Electric", "Air", "Nature", "Light", "Dark", "Desert" };
            string randomElem = elems[UnityEngine.Random.Range(0, elems.Length)];
            return new EnvironmentCardData(randomElem, 1);
        }
    }
}

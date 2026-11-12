using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterCardGame.Data
{
    [Serializable]
    public class EnvironmentCardData : CardData
    {
        public int level = 1;       // 1, 2, or 3
        public int turnsRemaining = 4;

        public EnvironmentCardData()
        {
            cardType = CardType.Environment;
        }

        public EnvironmentCardData(string elem, int lvl = 1)
        {
            cardType = CardType.Environment;
            element = elem;
            level = Mathf.Clamp(lvl, 1, 3);
            envLevel = level;
            buffedElement = elem;
            id = 440 + GetElementIndex(elem);

            SetLevelStats(level);
            SetupMatrix(elem);
        }

        public void SetLevelStats(int lvl)
        {
            level = Mathf.Clamp(lvl, 1, 3);
            envLevel = level;

            if (level == 1)
            {
                cost = 1; duration = 4; buffPct = 0.30f; debuffPct = 0.20f;
            }
            else if (level == 2)
            {
                cost = 4; duration = 2; buffPct = 0.40f; debuffPct = 0.30f;
            }
            else
            {
                cost = 6; duration = 1; buffPct = 0.70f; debuffPct = 0.50f;
            }
            turnsRemaining = duration;
        }

        public void UpgradeLevel()
        {
            if (level < 3)
            {
                SetLevelStats(level + 1);
            }
            else
            {
                turnsRemaining = duration;
            }
        }

        public bool TickTurn()
        {
            turnsRemaining--;
            return turnsRemaining <= 0;
        }

        private void SetupMatrix(string elem)
        {
            switch (elem)
            {
                case "Fire":
                    name = "Fire Environment";
                    debuffedElements = new List<string> { "Forest", "Ice", "Mecha" };
                    description = "Molten lava empowers Fire monsters (+30%), while melting Ice, burning Forest, and overheating Mecha (-20%).";
                    break;
                case "Water":
                    name = "Water Environment";
                    debuffedElements = new List<string> { "Fire", "Earth", "Mecha" };
                    description = "Raging waves empower Water monsters (+30%), while extinguishing Fire, eroding Earth, and rusting Mecha (-20%).";
                    break;
                case "Ice":
                    name = "Ice Environment";
                    debuffedElements = new List<string> { "Water", "Forest", "Air" };
                    description = "Bitter frost empowers Ice monsters (+30%), while freezing Water, withering Forest, and weighing down Air (-20%).";
                    break;
                case "Earth":
                    name = "Earth Environment";
                    debuffedElements = new List<string> { "Electric", "Fire", "Ice" };
                    description = "Seismic tremors empower Earth monsters (+30%), while grounding Electric, smothering Fire, and crushing Ice (-20%).";
                    break;
                case "Electric":
                    name = "Electric Environment";
                    debuffedElements = new List<string> { "Mecha", "Water", "Air" };
                    description = "Thunderstorms empower Electric monsters (+30%), while short-circuiting Mecha, electrifying Water, and striking Air (-20%).";
                    break;
                case "Air":
                    name = "Air Environment";
                    debuffedElements = new List<string> { "Ghosts", "Earth", "Dark" };
                    description = "Cyclonic winds empower Air monsters (+30%), while scattering Ghosts, eroding Earth, and dispersing Dark (-20%).";
                    break;
                case "Forest":
                    name = "Forest Environment";
                    debuffedElements = new List<string> { "Earth", "Water", "Air" };
                    description = "Lush nature empowers Forest monsters (+30%), while roots split Earth and drain Water and Air (-20%).";
                    break;
                case "Light":
                    name = "Light Environment";
                    debuffedElements = new List<string> { "Dark", "Ghosts", "Arcane" };
                    description = "Radiant light blesses Light monsters (+30%), while banishing Dark, purifying Ghosts, and dispelling Arcane (-20%).";
                    break;
                case "Dark":
                    name = "Dark Environment";
                    debuffedElements = new List<string> { "Light", "Arcane", "Electric" };
                    description = "Total eclipse empowers Dark monsters (+30%), while eclipsing Light, corrupting Arcane, and dampening Electric (-20%).";
                    break;
                case "Mecha":
                    name = "Mecha Environment";
                    debuffedElements = new List<string> { "Forest", "Ghosts", "Light" };
                    description = "Industrial gears empower Mecha monsters (+30%), while clearing Forest, trapping Ghosts, and reflecting Light (-20%).";
                    break;
                case "Ghosts":
                    name = "Ghosts Environment";
                    debuffedElements = new List<string> { "Fire", "Electric", "Arcane" };
                    description = "Ethereal mist empowers Ghosts monsters (+30%), while chilling Fire, draining Electric, and haunting Arcane (-20%).";
                    break;
                case "Arcane":
                    name = "Arcane Environment";
                    debuffedElements = new List<string> { "Ice", "Light", "Dark" };
                    description = "Arcane surges empower Arcane monsters (+30%), while shattering Ice and bending Light and Dark (-20%).";
                    break;
                default:
                    name = "Stable Weather";
                    debuffedElements = new List<string>();
                    description = "Neutral battlefield climate with balanced elements.";
                    break;
            }
        }

        public float GetModifier(string monsterElem)
        {
            if (monsterElem == buffedElement)
                return 1.0f + buffPct;
            if (debuffedElements.Contains(monsterElem))
                return Mathf.Max(0.2f, 1.0f - debuffPct);
            return 1.0f;
        }

        public static int GetElementIndex(string elem)
        {
            return elem switch
            {
                "Fire" => 1, "Water" => 2, "Ice" => 3, "Earth" => 4,
                "Electric" => 5, "Air" => 6, "Forest" => 7, "Light" => 8,
                "Dark" => 9, "Mecha" => 10, "Ghosts" => 11, "Arcane" => 12,
                _ => 1
            };
        }

        public static List<EnvironmentCardData> GetAllEnvironments()
        {
            string[] elems = { "Fire", "Water", "Ice", "Earth", "Electric", "Air", "Forest", "Light", "Dark", "Mecha", "Ghosts", "Arcane" };
            var list = new List<EnvironmentCardData>();
            foreach (var elem in elems)
            {
                list.Add(new EnvironmentCardData(elem, 1));
            }
            return list;
        }

        public static EnvironmentCardData GetRandomEnvironment()
        {
            string[] elems = { "Fire", "Water", "Ice", "Earth", "Electric", "Air", "Forest", "Light", "Dark", "Mecha", "Ghosts", "Arcane" };
            string randomElem = elems[UnityEngine.Random.Range(0, elems.Length)];
            return new EnvironmentCardData(randomElem, 1);
        }
    }
}

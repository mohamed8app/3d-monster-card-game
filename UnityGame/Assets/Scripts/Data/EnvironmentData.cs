using System;
using System.Collections.Generic;

namespace MonsterCardGame.Data
{
    [Serializable]
    public class EnvironmentCardData
    {
        public string element;
        public string cardName;
        public int level;       // 1, 2, or 3
        public int cost;        // 2, 4, or 6
        public int duration;    // 4, 2, or 1 turn
        public float buffPct;   // +30%, +40%, +70%
        public float debuffPct; // -20%, -30%, -50%

        public string buffedElement;
        public List<string> debuffedElements = new List<string>();

        public EnvironmentCardData(string elem, int lvl)
        {
            element = elem;
            level = lvl;
            buffedElement = elem;

            if (lvl == 1)
            {
                cost = 2; duration = 4; buffPct = 0.30f; debuffPct = 0.20f;
            }
            else if (lvl == 2)
            {
                cost = 4; duration = 2; buffPct = 0.40f; debuffPct = 0.30f;
            }
            else
            {
                cost = 6; duration = 1; buffPct = 0.70f; debuffPct = 0.50f;
            }

            SetupMatrix(elem);
        }

        private void SetupMatrix(string elem)
        {
            switch (elem)
            {
                case "نار":
                    cardName = "بركان الحمم";
                    debuffedElements = new List<string> { "غابة", "جليد", "ميكا" };
                    break;
                case "مياه":
                    cardName = "طوفان المحيط";
                    debuffedElements = new List<string> { "نار", "أرض", "ميكا" };
                    break;
                case "جليد":
                    cardName = "العاصفة الثلجية";
                    debuffedElements = new List<string> { "مياه", "غابة", "رياح" };
                    break;
                case "أرض":
                    cardName = "الزلزال الصخري";
                    debuffedElements = new List<string> { "كهرباء", "نار", "جليد" };
                    break;
                case "كهرباء":
                    cardName = "العاصفة الرعدية";
                    debuffedElements = new List<string> { "ميكا", "مياه", "رياح" };
                    break;
                case "رياح":
                    cardName = "إعصار الأعالي";
                    debuffedElements = new List<string> { "أشباح", "أرض", "ظلام" };
                    break;
                case "غابة":
                    cardName = "الغابة العظيمة";
                    debuffedElements = new List<string> { "أرض", "مياه", "رياح" };
                    break;
                case "ضياء":
                    cardName = "الشفق المقدس";
                    debuffedElements = new List<string> { "ظلام", "أشباح", "سحر" };
                    break;
                case "ظلام":
                    cardName = "الكسوف السحيق";
                    debuffedElements = new List<string> { "ضياء", "سحر", "كهرباء" };
                    break;
                case "ميكا":
                    cardName = "المصنع الآلي";
                    debuffedElements = new List<string> { "غابة", "أشباح", "ضياء" };
                    break;
                case "أشباح":
                    cardName = "العالم الأثيري";
                    debuffedElements = new List<string> { "نار", "كهرباء", "سحر" };
                    break;
                case "سحر":
                    cardName = "الفضاء الأركيني";
                    debuffedElements = new List<string> { "جليد", "ضياء", "ظلام" };
                    break;
            }
        }

        public float GetModifier(string monsterElem)
        {
            if (monsterElem == buffedElement)
                return 1.0f + buffPct;
            if (debuffedElements.Contains(monsterElem))
                return Math.Max(0.2f, 1.0f - debuffPct);
            return 1.0f;
        }
    }
}

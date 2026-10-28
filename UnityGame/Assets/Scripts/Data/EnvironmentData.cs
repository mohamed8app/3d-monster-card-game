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
            id = 4000 + GetElementIndex(elem);

            SetLevelStats(level);
            SetupMatrix(elem);
        }

        public void SetLevelStats(int lvl)
        {
            level = Mathf.Clamp(lvl, 1, 3);
            envLevel = level;

            if (level == 1)
            {
                cost = 2; duration = 4; buffPct = 0.30f; debuffPct = 0.20f;
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
                turnsRemaining = duration; // تجديد مدة المستوى الثالث
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
                case "نار":
                    name = "بركان الحمم";
                    debuffedElements = new List<string> { "غابة", "جليد", "ميكا" };
                    description = "حمم منصهرة تسعر وحوش النار (+30%)، وتذيب الجليد وتحرق الغابات وتعطل الميكا (-20%).";
                    break;
                case "مياه":
                    name = "طوفان المحيط";
                    debuffedElements = new List<string> { "نار", "أرض", "ميكا" };
                    description = "أمواج عاتية تقوي وحوش المياه (+30%)، وتخمد النيران وتجرف الصخور وتصدئ الآلات (-20%).";
                    break;
                case "جليد":
                    name = "العاصفة الثلجية";
                    debuffedElements = new List<string> { "مياه", "غابة", "رياح" };
                    description = "صقيع قارس يعزز وحوش الجليد (+30%)، ويجمد المياه ويميت النبات ويثقل الرياح (-20%).";
                    break;
                case "أرض":
                    name = "الزلزال الصخري";
                    debuffedElements = new List<string> { "كهرباء", "نار", "جليد" };
                    description = "زلزال صخري يرسخ وحوش الأرض (+30%)، ويفرغ الشحنات ويخنق النار ويسحق الجليد (-20%).";
                    break;
                case "كهرباء":
                    name = "العاصفة الرعدية";
                    debuffedElements = new List<string> { "ميكا", "مياه", "رياح" };
                    description = "صواعق رعدية تغذي وحوش الكهرباء (+30%)، وتكهرب الماء وتشوش الآلات وتصطاد الطائر (-20%).";
                    break;
                case "رياح":
                    name = "إعصار الأعالي";
                    debuffedElements = new List<string> { "أشباح", "أرض", "ظلام" };
                    description = "رياح إعصارية تزيد سرعة وحوش الرياح (+30%)، وتشتت الأطياف وتنحت الصخور وتطرد الظلام (-20%).";
                    break;
                case "غابة":
                    name = "الغابة العظيمة";
                    debuffedElements = new List<string> { "أرض", "مياه", "رياح" };
                    description = "طبيعة خصبة تنعش وحوش الغابة (+30%)، وجذور تشق الصخور وتستنزف المياه والرياح (-20%).";
                    break;
                case "ضياء":
                    name = "الشفق المقدس";
                    debuffedElements = new List<string> { "ظلام", "أشباح", "سحر" };
                    description = "نور ساطع يبارك وحوش الضياء (+30%)، ويبدد العتمة ويطهر الأشباح ويبطل الطلاسم (-20%).";
                    break;
                case "ظلام":
                    name = "الكسوف السحيق";
                    debuffedElements = new List<string> { "ضياء", "سحر", "كهرباء" };
                    description = "عتمة مطلقة تقوي وحوش الظلام (+30%)، وتحجب الضياء وتلوث السحر وتخنق الكهرباء (-20%).";
                    break;
                case "ميكا":
                    name = "المصنع الآلي";
                    debuffedElements = new List<string> { "غابة", "أشباح", "ضياء" };
                    description = "محركات وتروس تشحذ وحوش الميكا (+30%)، وتبيد الغابات وتصطاد الأطياف وتعكس النور (-20%).";
                    break;
                case "أشباح":
                    name = "العالم الأثيري";
                    debuffedElements = new List<string> { "نار", "كهرباء", "سحر" };
                    description = "ضباب أثيري يمكّن وحوش الأشباح (+30%)، ويخمد اللهب ويسحب الشحنات ويستنزف السحرة (-20%).";
                    break;
                case "سحر":
                    name = "الفضاء الأركيني";
                    debuffedElements = new List<string> { "جليد", "ضياء", "ظلام" };
                    description = "موجات طاقة أركينية تضاعف وحوش السحر (+30%)، وتفكك الجليد وتحرف النور والعتمة (-20%).";
                    break;
                default:
                    name = "طقس مستقر";
                    debuffedElements = new List<string>();
                    description = "مناخ معتدل لا يغير موازين العناصر.";
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
                "نار" => 1, "مياه" => 2, "جليد" => 3, "أرض" => 4,
                "كهرباء" => 5, "رياح" => 6, "غابة" => 7, "ضياء" => 8,
                "ظلام" => 9, "ميكا" => 10, "أشباح" => 11, "سحر" => 12,
                _ => 1
            };
        }

        public static List<EnvironmentCardData> GetAllEnvironments()
        {
            string[] elems = { "نار", "مياه", "جليد", "أرض", "كهرباء", "رياح", "غابة", "ضياء", "ظلام", "ميكا", "أشباح", "سحر" };
            var list = new List<EnvironmentCardData>();
            foreach (var elem in elems)
            {
                list.Add(new EnvironmentCardData(elem, 1));
            }
            return list;
        }

        public static EnvironmentCardData GetRandomEnvironment()
        {
            string[] elems = { "نار", "مياه", "جليد", "أرض", "كهرباء", "رياح", "غابة", "ضياء", "ظلام", "ميكا", "أشباح", "سحر" };
            string randomElem = elems[UnityEngine.Random.Range(0, elems.Length)];
            return new EnvironmentCardData(randomElem, 1);
        }
    }
}

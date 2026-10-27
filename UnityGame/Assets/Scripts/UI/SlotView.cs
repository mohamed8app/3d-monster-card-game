using System;
using UnityEngine;
using UnityEngine.UI;
using MonsterCardGame.Core;
using MonsterCardGame.Data;
using MonsterCardGame.Utils;

namespace MonsterCardGame.UI
{
    public class SlotView : MonoBehaviour
    {
        public int slotIndex;
        public bool isPlayerSide = true;
        public BoardSlot logicSlot;

        [Header("UI References")]
        public Text slotNameText;
        public Image slotBorder;
        public Transform cardHolder;
        public CardView placedCardView;

        public void Setup(int index, bool playerSide, BoardSlot slot)
        {
            slotIndex = index;
            isPlayerSide = playerSide;
            logicSlot = slot;

            if (slotNameText != null)
            {
                slotNameText.text = ArabicFixer.Fix(GetArabicSlotName(index));
            }

            if (slotBorder != null)
            {
                if (index == 2 || index == 3)
                {
                    // الوسط بلون برتقالي/هجومي
                    slotBorder.color = new Color(1.0f, 0.45f, 0.2f, 0.7f);
                }
                else
                {
                    // الأجنحة بلون أزرق/سماوي
                    slotBorder.color = new Color(0.25f, 0.7f, 1.0f, 0.7f);
                }
            }
        }

        private string GetArabicSlotName(int idx)
        {
            switch (idx)
            {
                case 0: return "جناح أيسر 1";
                case 1: return "جناح أيسر 2";
                case 2: return "قلب الهجوم 1";
                case 3: return "قلب الهجوم 2";
                case 4: return "جناح أيمن 1";
                case 5: return "جناح أيمن 2";
                default: return $"خانة {idx}";
            }
        }

        public void DisplayMonster(MonsterData data, MonsterInstance instance)
        {
            if (placedCardView != null)
            {
                placedCardView.Setup(data, instance);
                placedCardView.gameObject.SetActive(true);
                return;
            }

            // عرض تلقائي لاسم الوحش وأرقامه داخل الخانة
            var dynamicInfo = transform.Find("DynamicMonsterInfo");
            if (dynamicInfo == null)
            {
                var infoObj = new GameObject("DynamicMonsterInfo");
                infoObj.transform.SetParent(transform, false);
                var rect = infoObj.AddComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(0, 10);
                rect.sizeDelta = new Vector2(150, 50);

                var txt = infoObj.AddComponent<Text>();
                txt.font = GameBootstrapper.GetFont();
                txt.fontSize = 14;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = Color.white;
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
                txt.text = ArabicFixer.Fix($"{data.name}\n{instance.baseAtk}⚔️  {instance.currentDef}🛡️");
            }
            else
            {
                var txt = dynamicInfo.GetComponent<Text>();
                if (txt != null)
                {
                    txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                    txt.verticalOverflow = VerticalWrapMode.Overflow;
                    txt.text = ArabicFixer.Fix($"{data.name}\n{instance.baseAtk}⚔️  {instance.currentDef}🛡️");
                }
                dynamicInfo.gameObject.SetActive(true);
            }
        }

        public void ClearDisplay()
        {
            if (placedCardView != null)
            {
                placedCardView.gameObject.SetActive(false);
            }
            var dynamicInfo = transform.Find("DynamicMonsterInfo");
            if (dynamicInfo != null)
            {
                dynamicInfo.gameObject.SetActive(false);
            }
        }
    }
}

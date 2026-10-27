using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using MonsterCardGame.Data;
using MonsterCardGame.Core;

namespace MonsterCardGame.UI
{
    /// <summary>
    /// يتيح سحب الكارت بالفأرة (Drag & Drop) من يد اللاعب وإفلاته مباشرة في خانات المعركة.
    /// Provides smooth Drag & Drop for cards from hand to board slots.
    /// </summary>
    public class DraggableCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public MonsterData cardData;
        private Transform originalParent;
        private int originalSiblingIndex;
        private Canvas rootCanvas;
        private RectTransform rectTransform;
        private CanvasGroup canvasGroup;

        public void Setup(MonsterData data, Canvas canvas)
        {
            cardData = data;
            rootCanvas = canvas;
            rectTransform = GetComponent<RectTransform>();
            canvasGroup = gameObject.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (transform.parent == originalParent || originalParent == null)
            {
                transform.localScale = Vector3.one * 1.06f;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (transform.parent == originalParent || originalParent == null)
            {
                transform.localScale = Vector3.one;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (BattleUIManager.Instance != null && cardData != null)
            {
                BattleUIManager.Instance.OnCardSelectedInHand(cardData);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (cardData == null) return;

            originalParent = transform.parent;
            originalSiblingIndex = transform.GetSiblingIndex();

            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = false;
                canvasGroup.alpha = 0.85f;
            }

            transform.localScale = Vector3.one * 1.15f;

            // رفع الكارت إلى الكانفاس الرئيسي ليظهر فوق كل العناصر أثناء السحب
            if (rootCanvas == null)
            {
                rootCanvas = GetComponentInParent<Canvas>();
            }

            if (rootCanvas != null)
            {
                transform.SetParent(rootCanvas.transform, true);
                transform.SetAsLastSibling();
            }

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.OnCardSelectedInHand(cardData);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            // تحريك الكارت مع مؤشر الفأرة بسلاسة
            transform.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = true;
                canvasGroup.alpha = 1.0f;
            }
            transform.localScale = Vector3.one;

            bool placed = false;

            // 1. فحص العناصر الواقعة تحت مؤشر الفأرة عبر Raycast
            var results = new List<RaycastResult>();
            if (EventSystem.current != null)
            {
                EventSystem.current.RaycastAll(eventData, results);
            }

            foreach (var hit in results)
            {
                // إذا تم إفلات الكارت فوق خانة تكتيكية للاعب
                var slotView = hit.gameObject.GetComponentInParent<SlotView>();
                if (slotView != null && slotView.isPlayerSide)
                {
                    placed = TrySummonToSlot(slotView.slotIndex);
                    if (placed) break;
                }

                // إذا تم إفلات الكارت فوق زر التضحية
                if (hit.gameObject.name.Contains("Sacrifice") || hit.gameObject.name.Contains("تضحية"))
                {
                    if (BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.OnCardSelectedInHand(cardData);
                        BattleUIManager.Instance.SendMessage("OnSacrificeClicked");
                        Destroy(gameObject);
                        return;
                    }
                }
            }

            // 2. إذا لم يتم الالتقاط المباشر، نفحص المسافة الفيزيائية لأقرب خانة للاعب
            if (!placed && BattleUIManager.Instance != null)
            {
                for (int i = 0; i < BattleUIManager.Instance.playerSlotViews.Length; i++)
                {
                    var sv = BattleUIManager.Instance.playerSlotViews[i];
                    if (sv != null)
                    {
                        float dist = Vector2.Distance(eventData.position, sv.transform.position);
                        if (dist < 130f) // مسافة كافية لامتصاص الإفلات
                        {
                            placed = TrySummonToSlot(i);
                            if (placed) break;
                        }
                    }
                }
            }

            // 3. إذا لم ينجح الإنزال، إعادة الكارت إلى مكانه الأصلي في اليد
            if (!placed)
            {
                if (originalParent != null)
                {
                    transform.SetParent(originalParent, false);
                    transform.SetSiblingIndex(originalSiblingIndex);
                }
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private bool TrySummonToSlot(int slotIndex)
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn) return false;

            if (gm.humanPlayer.SummonMonster(cardData, slotIndex, gm.currentTurn))
            {
                BattleUIManager.Instance.LogCombatMessage($"✅ تم سحب وإنزال [{cardData.name}] في الخانة {slotIndex}.");
                BattleUIManager.Instance.UpdateUI();
                return true;
            }
            else
            {
                BattleUIManager.Instance.LogCombatMessage($"❌ لا يمكن إنزال الكارت في الخانة {slotIndex} (تحقق من الباور المتاح).");
                return false;
            }
        }
    }
}

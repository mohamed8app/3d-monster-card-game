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
    /// يتيح سحب الكروت بأنواعها (وحوش، سحر، بيئة) بالفأرة من يد اللاعب وإفلاتها مباشرة في ساحة المعركة.
    /// Provides smooth Drag & Drop for Monsters, Spells and Environment cards.
    /// </summary>
    public class DraggableCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public CardData cardData;
        private Transform originalParent;
        private int originalSiblingIndex;
        private Canvas rootCanvas;
        private RectTransform rectTransform;
        private CanvasGroup canvasGroup;

        public void Setup(CardData data, Canvas canvas)
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
                transform.localScale = Vector3.one * 1.15f;
                transform.SetAsLastSibling();
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

            bool handled = false;

            // 1. فحص العناصر الواقعة تحت مؤشر الفأرة
            var results = new List<RaycastResult>();
            if (EventSystem.current != null)
            {
                EventSystem.current.RaycastAll(eventData, results);
            }

            foreach (var hit in results)
            {
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

                // إذا كانت بطاقة وحش وأُفلتت فوق خانة للاعب
                if (cardData.cardType == CardType.Monster)
                {
                    var slotView = hit.gameObject.GetComponentInParent<SlotView>();
                    if (slotView != null && slotView.isPlayerSide)
                    {
                        handled = TrySummonToSlot(slotView.slotIndex);
                        if (handled) break;
                    }
                }
            }

            // 2. إذا كانت بطاقة سحر أو بيئة وسُحبت لمنتصف الساحة (الأعلى من منطقة اليد)
            if (!handled)
            {
                if (cardData.cardType == CardType.Spell && eventData.position.y > 220f)
                {
                    handled = TryCastSpell();
                }
                else if (cardData.cardType == CardType.Environment && eventData.position.y > 220f)
                {
                    handled = TryActivateEnvironment();
                }
            }

            // 3. مسافة امتصاص الأمان للوحوش عند الإفلات قرب الخانات
            if (!handled && cardData.cardType == CardType.Monster && BattleUIManager.Instance != null)
            {
                for (int i = 0; i < BattleUIManager.Instance.playerSlotViews.Length; i++)
                {
                    var sv = BattleUIManager.Instance.playerSlotViews[i];
                    if (sv != null)
                    {
                        float dist = Vector2.Distance(eventData.position, sv.transform.position);
                        if (dist < 160f)
                        {
                            handled = TrySummonToSlot(i);
                            if (handled) break;
                        }
                    }
                }
            }

            // 4. إعادة الكارت لليد إن لم يتم تصريفه
            if (!handled)
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
                BattleUIManager.Instance.LogCombatMessage($"✅ تم استدعاء [{cardData.name}] في الخانة {slotIndex}.");
                BattleUIManager.Instance.UpdateUI();
                var sv = BattleUIManager.Instance.GetSlotView(slotIndex, true);
                if (sv != null)
                {
                    BattleUIManager.Instance.StartCoroutine(BattleAnimations.AnimateCardSummon(sv.transform));
                }
                return true;
            }
            else
            {
                BattleUIManager.Instance.LogCombatMessage($"❌ تعذر إنزال الوحش في الخانة {slotIndex} (تأكد من الباور والشواغر).");
                return false;
            }
        }

        private bool TryCastSpell()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn) return false;

            return gm.CastSpell(gm.humanPlayer, gm.aiPlayer, cardData);
        }

        private bool TryActivateEnvironment()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn) return false;

            return gm.PlayEnvironmentCard(gm.humanPlayer, cardData);
        }
    }
}

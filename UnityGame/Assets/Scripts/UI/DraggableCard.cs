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
    /// يتيح سحب واختيار الكروت بأنواعها (وحوش، سحر، بيئة) بسلاسة فائقة وانسيابية تامة دون أي رمش أو اهتزاز.
    /// Provides ultra-smooth Hover, Selection, and Drag & Drop without any layout flickering or sibling jumping.
    /// </summary>
    public class DraggableCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public CardData cardData;
        private Transform originalParent;
        private int originalSiblingIndex;
        private Canvas rootCanvas;
        private RectTransform rectTransform;
        private CanvasGroup canvasGroup;
        private Canvas localCanvas;
        private GraphicRaycaster localRaycaster;
        private Outline cardOutline;
        private Color defaultOutlineColor = Color.gray;

        private bool isHovered = false;
        private bool isSelected = false;
        private bool isDragging = false;

        private float targetY = 0f;
        private float targetScale = 1.0f;

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

            // كانفاس فرعي محلي لرفع ترتيب الرسم (Z-Order / Sorting Order) فوق البطاقات المجاورة
            // دون الحاجة لتغيير ترتيب الأبناء (Sibling Index) الذي كان يسبب قفز ورمش الكروت في الـ LayoutGroup!
            localCanvas = gameObject.GetComponent<Canvas>();
            if (localCanvas == null)
            {
                localCanvas = gameObject.AddComponent<Canvas>();
            }
            localRaycaster = gameObject.GetComponent<GraphicRaycaster>();
            if (localRaycaster == null)
            {
                localRaycaster = gameObject.AddComponent<GraphicRaycaster>();
            }

            cardOutline = GetComponent<Outline>();
            if (cardOutline != null)
            {
                defaultOutlineColor = cardOutline.effectColor;
            }

            localCanvas.overrideSorting = false;
            localCanvas.sortingOrder = 1;
        }

        private void Update()
        {
            if (isDragging) return;

            // تحديد الارتفاع والتكبير المستهدف بسلاسة
            if (isSelected)
            {
                targetY = 28f;
                targetScale = 1.08f;
            }
            else if (isHovered)
            {
                targetY = 16f;
                targetScale = 1.05f;
            }
            else
            {
                targetY = 0f;
                targetScale = 1.0f;
            }

            // حركة تنعيم انسيابية مستمرة (Smooth Lerp)
            Vector3 curScale = transform.localScale;
            float newScale = Mathf.Lerp(curScale.x, targetScale, Time.deltaTime * 20f);
            transform.localScale = new Vector3(newScale, newScale, 1f);

            Vector2 curPos = rectTransform.anchoredPosition;
            float newY = Mathf.Lerp(curPos.y, targetY, Time.deltaTime * 20f);
            rectTransform.anchoredPosition = new Vector2(curPos.x, newY);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (isDragging) return;
            isHovered = true;
            UpdateVisualState();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (isDragging) return;
            isHovered = false;
            UpdateVisualState();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (isDragging) return;

            if (BattleUIManager.Instance != null && cardData != null)
            {
                BattleUIManager.Instance.OnCardSelectedInHand(cardData);
            }
        }

        public void SetSelected(bool selected)
        {
            isSelected = selected;
            UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            // تحديث طبقة الرسم لتظهر البطاقة المختارة أو الممرر عليها الماوس فوق البطاقات المجاورة
            if (localCanvas != null)
            {
                if (isSelected)
                {
                    localCanvas.overrideSorting = true;
                    localCanvas.sortingOrder = 25;
                }
                else if (isHovered)
                {
                    localCanvas.overrideSorting = true;
                    localCanvas.sortingOrder = 15;
                }
                else
                {
                    localCanvas.overrideSorting = false;
                    localCanvas.sortingOrder = 1;
                }
            }

            // تحديث لون وتوهج الإطار
            if (cardOutline != null)
            {
                if (isSelected)
                {
                    // توهج ذهبي ملكي عريض ومشع للبطاقة المحددة
                    cardOutline.effectColor = new Color(1f, 0.92f, 0.25f, 1f);
                    cardOutline.effectDistance = new Vector2(3f, 3f);
                }
                else if (isHovered)
                {
                    // وميض أبيض ناصع عند مرور مؤشر الفأرة
                    cardOutline.effectColor = new Color(1f, 1f, 1f, 0.95f);
                    cardOutline.effectDistance = new Vector2(2.5f, 2.5f);
                }
                else
                {
                    // اللون الأصلي لعنصر البطاقة
                    cardOutline.effectColor = defaultOutlineColor;
                    cardOutline.effectDistance = new Vector2(2f, 2f);
                }
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (cardData == null) return;

            isDragging = true;
            originalParent = transform.parent;
            originalSiblingIndex = transform.GetSiblingIndex();

            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = false;
                canvasGroup.alpha = 0.88f;
            }

            transform.localScale = Vector3.one * 1.12f;

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
            isDragging = false;

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

            // 4. إعادة الكارت لليد إن لم يتم تصريفه مع الحفاظ على ترتيبه الأصلي
            if (!handled)
            {
                if (originalParent != null)
                {
                    transform.SetParent(originalParent, false);
                    transform.SetSiblingIndex(originalSiblingIndex);
                    rectTransform.anchoredPosition = new Vector2(rectTransform.anchoredPosition.x, 0f);
                }
                UpdateVisualState();
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
                BattleUIManager.Instance.DeselectHandCards();
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

            bool success = gm.CastSpell(gm.humanPlayer, gm.aiPlayer, cardData);
            if (success && BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.DeselectHandCards();
            }
            return success;
        }

        private bool TryActivateEnvironment()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn) return false;

            bool success = gm.PlayEnvironmentCard(gm.humanPlayer, cardData);
            if (success && BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.DeselectHandCards();
            }
            return success;
        }
    }
}

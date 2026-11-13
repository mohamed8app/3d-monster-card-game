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
    /// Provides flicker-free Hover, Selection, and Drag & Drop by keeping the root raycast/layout RectTransform
    /// completely stationary inside the HorizontalLayoutGroup while animating only the non-raycast child VisualRoot.
    /// </summary>
    public class DraggableCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public CardData cardData;
        private Transform originalParent;
        private int originalSiblingIndex;
        private Canvas rootCanvas;
        private RectTransform rectTransform;
        private RectTransform visualRoot;
        private CanvasGroup canvasGroup;
        private Outline cardOutline;
        private Color defaultOutlineColor = Color.gray;

        private bool isHovered = false;
        private bool isSelected = false;
        private bool isDragging = false;

        private float targetVisualY = 0f;
        private float targetVisualScale = 1.0f;

        public void Setup(CardData data, Canvas canvas)
        {
            cardData = data;
            rootCanvas = canvas;
            rectTransform = GetComponent<RectTransform>();

            Transform visTr = transform.Find("VisualRoot");
            if (visTr != null)
            {
                visualRoot = visTr.GetComponent<RectTransform>();
                cardOutline = visTr.GetComponent<Outline>();
            }
            else
            {
                visualRoot = rectTransform;
                cardOutline = GetComponent<Outline>();
            }

            if (cardOutline != null)
            {
                defaultOutlineColor = cardOutline.effectColor;
            }

            canvasGroup = gameObject.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            // Ensure all child graphics inside VisualRoot do not intercept pointer raycasts
            DisableChildRaycasts();
            UpdateVisualState();
        }

        private void DisableChildRaycasts()
        {
            var rootImg = GetComponent<Image>();
            var allGraphics = GetComponentsInChildren<Graphic>(true);
            foreach (var g in allGraphics)
            {
                if (g != null && g != rootImg)
                {
                    g.raycastTarget = false;
                }
            }
            if (rootImg != null)
            {
                rootImg.raycastTarget = true;
            }
        }

        private void Update()
        {
            if (isDragging || visualRoot == null || visualRoot == rectTransform) return;

            if (isSelected)
            {
                targetVisualY = 16f;
                targetVisualScale = 1.06f;
            }
            else if (isHovered)
            {
                targetVisualY = 10f;
                targetVisualScale = 1.035f;
            }
            else
            {
                targetVisualY = 0f;
                targetVisualScale = 1.0f;
            }

            Vector3 curScale = visualRoot.localScale;
            if (Mathf.Abs(curScale.x - targetVisualScale) > 0.001f)
            {
                float newScale = Mathf.Lerp(curScale.x, targetVisualScale, Time.unscaledDeltaTime * 18f);
                visualRoot.localScale = new Vector3(newScale, newScale, 1f);
            }

            Vector2 curPos = visualRoot.anchoredPosition;
            if (Mathf.Abs(curPos.y - targetVisualY) > 0.05f)
            {
                float newY = Mathf.Lerp(curPos.y, targetVisualY, Time.unscaledDeltaTime * 18f);
                visualRoot.anchoredPosition = new Vector2(0f, newY);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (isDragging || isHovered) return;
            isHovered = true;
            UpdateVisualState();
            if (BattleUIManager.Instance != null && cardData != null)
            {
                BattleUIManager.Instance.ShowCardDetails(cardData);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (isDragging || !isHovered) return;
            isHovered = false;
            UpdateVisualState();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (isDragging) return;

            // Right Click: Direct card sacrifice for +1 Max Mana & +1 Power
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                if (BattleUIManager.Instance != null && cardData != null)
                {
                    BattleUIManager.Instance.SacrificeCardDirectly(cardData);
                }
                return;
            }

            // Left Click: Select card in hand and display details in Inspector
            if (BattleUIManager.Instance != null && cardData != null)
            {
                BattleUIManager.Instance.OnCardSelectedInHand(cardData);
            }
        }

        public void SetSelected(bool selected)
        {
            if (isSelected == selected) return;
            isSelected = selected;
            UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            if (cardOutline != null)
            {
                if (isSelected)
                {
                    cardOutline.effectColor = new Color(0.45f, 0.76f, 0.92f, 0.95f);
                }
                else if (isHovered)
                {
                    cardOutline.effectColor = new Color(0.76f, 0.82f, 0.90f, 0.90f);
                }
                else
                {
                    cardOutline.effectColor = defaultOutlineColor;
                }
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (cardData == null) return;

            isDragging = true;
            originalParent = transform.parent;
            originalSiblingIndex = transform.GetSiblingIndex();

            if (visualRoot != null && visualRoot != rectTransform)
            {
                visualRoot.anchoredPosition = Vector2.zero;
                visualRoot.localScale = Vector3.one;
            }

            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = false;
                canvasGroup.alpha = 0.65f;
            }

            transform.localScale = Vector3.one * 0.85f;

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
                if (!isSelected)
                {
                    BattleUIManager.Instance.OnCardSelectedInHand(cardData);
                }
                else if (cardData != null && cardData.cardType == CardType.Monster)
                {
                    BattleUIManager.Instance.HighlightAvailableSlots(true);
                }
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            transform.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            isDragging = false;

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.HighlightAvailableSlots(false);
            }

            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = true;
                canvasGroup.alpha = 1.0f;
            }
            transform.localScale = Vector3.one;

            bool handled = false;

            // 1. Raycast elements under cursor
            var results = new List<RaycastResult>();
            if (EventSystem.current != null)
            {
                EventSystem.current.RaycastAll(eventData, results);
            }

            foreach (var hit in results)
            {
                if (hit.gameObject.name.Contains("Sacrifice"))
                {
                    if (BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.OnCardSelectedInHand(cardData);
                        BattleUIManager.Instance.SendMessage("OnSacrificeClicked");
                        Destroy(gameObject);
                        return;
                    }
                }

                if (cardData.cardType == CardType.Monster)
                {
                    var slotView = hit.gameObject.GetComponentInParent<SlotView>();
                    if (slotView != null && slotView.isPlayerSide)
                    {
                        handled = TrySummonToSlot(slotView.slotIndex);
                        if (handled) break;
                    }
                }
                else if (cardData.cardType == CardType.Environment)
                {
                    if (hit.gameObject.name.Contains("SharedEnvSlot") || hit.gameObject.name.Contains("SharedEnvCardSlot"))
                    {
                        handled = TryActivateEnvironment();
                        if (handled) break;
                    }
                }
                else if (cardData.cardType == CardType.Leader)
                {
                    if (hit.gameObject.name.Contains("PlayerLeaderSlot") || hit.gameObject.name.Contains("PlayerLeaderCardSlot"))
                    {
                        handled = TrySummonLeader();
                        if (handled) break;
                    }
                }
            }

            // 2. If Spell, Environment, or Leader card is dragged to the arena above the hand dock
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
                else if (cardData.cardType == CardType.Leader && eventData.position.y > 220f)
                {
                    handled = TrySummonLeader();
                }
            }

            // 3. Proximity snap distance for monster drops near player slots
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

            // 4. Return card to hand if not played
            if (!handled)
            {
                if (originalParent != null)
                {
                    transform.SetParent(originalParent, false);
                    transform.SetSiblingIndex(originalSiblingIndex);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(originalParent as RectTransform);
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
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.OnPlayerMonsterSummoned(cardData, slotIndex);
                }
                return true;
            }
            else
            {
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.LogCombatMessage($"❌ Cannot summon monster to Slot {slotIndex} (Check Mana & slot availability).");
                }
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

        private bool TrySummonLeader()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn) return false;

            bool success = gm.PlayLeaderCard(gm.humanPlayer, cardData);
            if (success && BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.DeselectHandCards();
            }
            return success;
        }
    }
}

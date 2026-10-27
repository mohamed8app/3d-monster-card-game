using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MonsterCardGame.Core;
using MonsterCardGame.Data;

namespace MonsterCardGame.UI
{
    public class BattleUIManager : MonoBehaviour
    {
        public static BattleUIManager Instance { get; private set; }

        [Header("Leader HP & Resources")]
        public Text playerHpText;
        public Slider playerHpSlider;
        public Text enemyHpText;
        public Slider enemyHpSlider;

        public Text playerPowerText;
        public Text turnInfoText;

        [Header("Controls")]
        public Button attackButton;
        public Button endTurnButton;
        public Button sacrificeButton;

        [Header("Containers")]
        public Transform playerHandContainer;
        public SlotView[] playerSlotViews = new SlotView[7];
        public SlotView[] enemySlotViews = new SlotView[7];

        [Header("Prefabs")]
        public GameObject cardPrefab;

        [Header("Combat Log")]
        public Text combatLogText;
        private List<string> logEntries = new List<string>();

        private MonsterData selectedCardForAction = null;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            if (attackButton != null) attackButton.onClick.AddListener(OnAttackClicked);
            if (endTurnButton != null) endTurnButton.onClick.AddListener(OnEndTurnClicked);
            if (sacrificeButton != null) sacrificeButton.onClick.AddListener(OnSacrificeClicked);

            UpdateUI();
        }

        public void UpdateUI()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            // 1. تحديث صحة القائدين
            if (playerHpText != null) playerHpText.text = $"{gm.humanPlayer.currentHp} / 1000 HP";
            if (playerHpSlider != null) playerHpSlider.value = gm.humanPlayer.currentHp / 1000f;

            if (enemyHpText != null) enemyHpText.text = $"{gm.aiPlayer.currentHp} / 1000 HP";
            if (enemyHpSlider != null) enemyHpSlider.value = gm.aiPlayer.currentHp / 1000f;

            // 2. تحديث المانا والباور
            if (playerPowerText != null)
            {
                playerPowerText.text = $"⚡ الباور: {gm.humanPlayer.currentPower} / المانا: {gm.humanPlayer.manaCapacity}";
            }

            // 3. مؤشر الدور
            if (turnInfoText != null)
            {
                turnInfoText.text = gm.isPlayerTurn ? "🟢 دورك الآن (اختر كارت أو هاجم)" : "🔴 دور الخصم (يفكر...)";
            }

            // 4. أزرار التحكم
            if (attackButton != null) attackButton.interactable = gm.isPlayerTurn;
            if (endTurnButton != null) endTurnButton.interactable = gm.isPlayerTurn;
            if (sacrificeButton != null)
            {
                sacrificeButton.interactable = gm.isPlayerTurn && !gm.humanPlayer.hasSacrificedThisTurn;
            }

            // 5. تحديث خانات الساحة
            UpdateSlots();

            // 6. تحديث كروت اليد
            RefreshPlayerHand();
        }

        private void UpdateSlots()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            for (int i = 0; i < 7; i++)
            {
                var pSlot = gm.humanPlayer.slots[i];
                if (pSlot != null && pSlot.IsOccupied && i < playerSlotViews.Length && playerSlotViews[i] != null)
                {
                    playerSlotViews[i].DisplayMonster(pSlot.currentMonster.data, pSlot.currentMonster);
                }
                else if (i < playerSlotViews.Length && playerSlotViews[i] != null)
                {
                    playerSlotViews[i].ClearDisplay();
                }

                var eSlot = gm.aiPlayer.slots[i];
                if (eSlot != null && eSlot.IsOccupied && i < enemySlotViews.Length && enemySlotViews[i] != null)
                {
                    enemySlotViews[i].DisplayMonster(eSlot.currentMonster.data, eSlot.currentMonster);
                }
                else if (i < enemySlotViews.Length && enemySlotViews[i] != null)
                {
                    enemySlotViews[i].ClearDisplay();
                }
            }
        }

        private void RefreshPlayerHand()
        {
            var gm = GameManager.Instance;
            if (gm == null || playerHandContainer == null) return;

            // مسح الكروت القديمة في اليد
            foreach (Transform child in playerHandContainer)
            {
                Destroy(child.gameObject);
            }

            // رسم الكروت الحالية
            foreach (var card in gm.humanPlayer.hand)
            {
                if (cardPrefab != null)
                {
                    var cardObj = Instantiate(cardPrefab, playerHandContainer);
                    var cardView = cardObj.GetComponent<CardView>();
                    if (cardView != null)
                    {
                        cardView.Setup(card);
                        var btn = cardObj.GetComponent<Button>();
                        if (btn != null)
                        {
                            var targetCard = card;
                            btn.onClick.AddListener(() => OnCardSelectedInHand(targetCard));
                        }
                    }
                }
            }
        }

        public void OnCardSelectedInHand(MonsterData card)
        {
            selectedCardForAction = card;
            LogCombatMessage($"تم تحديد الكارت: [{card.name}] (تكلفة: {card.cost}⚡) - اضغط على خانة شاغرة لإنزاله أو اضغط 'تضحية'.");
        }

        public void OnSlotClicked(int slotIndex, bool isPlayerSide)
        {
            if (!isPlayerSide || selectedCardForAction == null) return;

            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn) return;

            if (gm.humanPlayer.SummonMonster(selectedCardForAction, slotIndex, gm.currentTurn))
            {
                LogCombatMessage($"✅ تم إنزال [{selectedCardForAction.name}] في الخانة {slotIndex}.");
                selectedCardForAction = null;
                UpdateUI();
            }
            else
            {
                LogCombatMessage($"❌ لا يمكنك إنزال الكارت في الخانة {slotIndex} (تأكد من الباور والشواغر).");
            }
        }

        private void OnSacrificeClicked()
        {
            if (selectedCardForAction == null)
            {
                LogCombatMessage("⚠️ يرجى تحديد كارت من يدك أولاً للقيام بالتضحية به!");
                return;
            }

            var gm = GameManager.Instance;
            if (gm.humanPlayer.SacrificeCard(selectedCardForAction))
            {
                LogCombatMessage($"🔥 تمت التضحية بـ [{selectedCardForAction.name}]! كسبت +1 سقف مانا و +1 باور.");
                selectedCardForAction = null;
                UpdateUI();
            }
        }

        private void OnAttackClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            LogCombatMessage("⚔️ إطلاق مرحلة الهجوم! تندفع وحوشك نحو خطوط العدو...");
            gm.OnPlayerExecuteAttackButton();
            UpdateUI();
        }

        private void OnEndTurnClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            LogCombatMessage("🔁 أنهيت دورك. الآن دور الخصم...");
            selectedCardForAction = null;
            gm.EndTurn();
            UpdateUI();
        }

        public void LogCombatMessage(string msg)
        {
            logEntries.Add(msg);
            if (logEntries.Count > 8) logEntries.RemoveAt(0);

            if (combatLogText != null)
            {
                combatLogText.text = string.Join("\n", logEntries);
            }
            Debug.Log($"[LOG]: {msg}");
        }
    }
}

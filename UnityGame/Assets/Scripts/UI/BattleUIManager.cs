using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MonsterCardGame.Core;
using MonsterCardGame.Data;
using MonsterCardGame.Utils;

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

        [Header("Opponent Hand Display (Face-Down Cards)")]
        public Transform enemyHandContainer;
        public Text enemyHandCountText;

        [Header("Controls")]
        public Button attackButton;
        public Button endTurnButton;
        public Button sacrificeButton;

        [Header("Containers")]
        public Transform playerHandContainer;
        public SlotView[] playerSlotViews = new SlotView[6];
        public SlotView[] enemySlotViews = new SlotView[6];

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
            StartCoroutine(DelayedInitialUpdate());
        }

        private System.Collections.IEnumerator DelayedInitialUpdate()
        {
            // ننتظر فريم واحد حتى ينتهي GameManager من إعداد وتجهيز كروت البداية
            yield return null;
            UpdateUI();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
                #else
                Application.Quit();
                #endif
            }

            // فحص أمان تلقائي: لو يد اللاعب بها كروت ولكن لم ترسم بعد، يتم التحديث فوراً
            var gm = GameManager.Instance;
            if (gm != null && gm.humanPlayer != null && gm.humanPlayer.hand != null && gm.humanPlayer.hand.Count > 0)
            {
                if (playerHandContainer != null && playerHandContainer.childCount == 0)
                {
                    UpdateUI();
                }
            }
        }

        public void UpdateUI()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            // 1. تحديث صحة القائدين
            if (playerHpText != null) playerHpText.text = ArabicFixer.Fix($"قائدك: {gm.humanPlayer.currentHp} / 1000 HP");
            if (playerHpSlider != null) playerHpSlider.value = gm.humanPlayer.currentHp / 1000f;

            if (enemyHpText != null) enemyHpText.text = ArabicFixer.Fix($"قائد الخصم: {gm.aiPlayer.currentHp} / 1000 HP");
            if (enemyHpSlider != null) enemyHpSlider.value = gm.aiPlayer.currentHp / 1000f;

            // 2. تحديث المانا والباور
            if (playerPowerText != null)
            {
                playerPowerText.text = ArabicFixer.Fix($"⚡ الباور: {gm.humanPlayer.currentPower} / المانا: {gm.humanPlayer.manaCapacity}");
            }

            // 3. مؤشر الدور
            if (turnInfoText != null)
            {
                turnInfoText.text = ArabicFixer.Fix(gm.isPlayerTurn ? "🟢 دورك الآن (اختر كارت أو هاجم)" : "🔴 دور الخصم (يفكر...)");
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

            // 6. تحديث كروت يد اللاعب
            RefreshPlayerHand();

            // 7. تحديث كروت يد الخصم (تظهر مقلوبة من ظهرها فقط)
            RefreshEnemyHand();
        }

        private void UpdateSlots()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            for (int i = 0; i < 6; i++)
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

            // رسم الكروت الحالية مع خاصية السحب والإفلات (Drag & Drop)
            var mainCanvas = GetComponentInParent<Canvas>();
            foreach (var card in gm.humanPlayer.hand)
            {
                var cardObj = CreateDynamicCardUI(playerHandContainer, card);
                var draggable = cardObj.AddComponent<DraggableCard>();
                draggable.Setup(card, mainCanvas);
            }
        }

        private void RefreshEnemyHand()
        {
            var gm = GameManager.Instance;
            if (gm == null || enemyHandContainer == null) return;

            // مسح الكروت القديمة في يد الخصم
            foreach (Transform child in enemyHandContainer)
            {
                Destroy(child.gameObject);
            }

            int handCount = gm.aiPlayer.hand.Count;
            if (enemyHandCountText != null)
            {
                enemyHandCountText.text = ArabicFixer.Fix($"🎴 كروت يد الخصم: {handCount} بطاقات مستترة");
            }

            // رسم كل بطاقة من ظهرها فقط بأناقة وفخامة
            for (int i = 0; i < handCount; i++)
            {
                CreateFaceDownCardUI(enemyHandContainer, i);
            }
        }

        private GameObject CreateFaceDownCardUI(Transform parent, int index)
        {
            // الغلاف الخارجي لظهر البطاقة
            var cardObj = GameBootstrapper.CreatePanel(parent, $"EnemyCardBack_{index}", new Color(0.12f, 0.09f, 0.20f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(58, 52));

            var layoutElem = cardObj.AddComponent<LayoutElement>();
            layoutElem.preferredWidth = 58;
            layoutElem.preferredHeight = 52;
            layoutElem.minWidth = 58;
            layoutElem.minHeight = 52;

            var outline = cardObj.AddComponent<Outline>();
            outline.effectColor = new Color(0.92f, 0.76f, 0.28f, 0.95f); // إطار ذهبي ملكي متوهج
            outline.effectDistance = new Vector2(2, 2);

            // بطانة داخلية مزخرفة
            var innerPanel = GameBootstrapper.CreatePanel(cardObj.transform, "InnerRuneBorder", new Color(0.08f, 0.06f, 0.14f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46, 40));

            var innerOutline = innerPanel.AddComponent<Outline>();
            innerOutline.effectColor = new Color(0.55f, 0.35f, 0.8f, 0.7f); // وهج أرجواني سحري
            innerOutline.effectDistance = new Vector2(1, 1);

            // الرمز السحري لظهر البطاقة في المنتصف
            GameBootstrapper.CreateText(innerPanel.transform, "RuneSymbol", "🔮", 16,
                new Color(1f, 0.85f, 0.35f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(42, 36));

            return cardObj;
        }

        private GameObject CreateDynamicCardUI(Transform parent, MonsterData card)
        {
            var cardObj = GameBootstrapper.CreatePanel(parent, $"Card_{card.name}", new Color(0.16f, 0.18f, 0.25f),
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(170, 95));

            var layoutElem = cardObj.AddComponent<LayoutElement>();
            layoutElem.preferredWidth = 170;
            layoutElem.preferredHeight = 95;
            layoutElem.minWidth = 170;
            layoutElem.minHeight = 95;

            var outline = cardObj.AddComponent<Outline>();
            Color elemColor = card.element switch
            {
                "نار" => new Color(1f, 0.3f, 0.2f),
                "مياه" => new Color(0.2f, 0.6f, 1f),
                "جليد" => new Color(0.4f, 0.9f, 1f),
                "أرض" => new Color(0.7f, 0.5f, 0.3f),
                "كهرباء" => new Color(1f, 0.9f, 0.2f),
                "رياح" => new Color(0.4f, 0.95f, 0.7f),
                "غابة" => new Color(0.3f, 0.8f, 0.3f),
                "ضياء" => new Color(1f, 0.95f, 0.6f),
                "ظلام" => new Color(0.45f, 0.2f, 0.65f),
                "ميكا" => new Color(0.7f, 0.75f, 0.85f),
                "أشباح" => new Color(0.7f, 0.5f, 0.9f),
                "سحر" => new Color(0.9f, 0.4f, 0.9f),
                _ => Color.white
            };
            outline.effectColor = elemColor;
            outline.effectDistance = new Vector2(2, 2);

            var btn = cardObj.AddComponent<Button>();
            btn.targetGraphic = cardObj.GetComponent<Image>();

            // سطر علوي: العنصر والتكلفة
            GameBootstrapper.CreateText(cardObj.transform, "TopInfo", $"{card.element} | {card.cost}⚡", 13,
                elemColor, TextAnchor.UpperCenter, new Vector2(0, 32), new Vector2(160, 22));

            // اسم الوحش في المنتصف
            GameBootstrapper.CreateText(cardObj.transform, "Name", card.name, 14,
                Color.white, TextAnchor.MiddleCenter, new Vector2(0, 5), new Vector2(160, 26));

            // سطر سفلي: الهجوم والدفاع
            GameBootstrapper.CreateText(cardObj.transform, "Stats", $"{card.atk}⚔️   {card.def}🛡️", 14,
                new Color(0.95f, 0.85f, 0.4f), TextAnchor.LowerCenter, new Vector2(0, -30), new Vector2(160, 22));

            return cardObj;
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
                if (slotIndex >= 0 && slotIndex < playerSlotViews.Length && playerSlotViews[slotIndex] != null)
                {
                    StartCoroutine(BattleAnimations.AnimateCardSummon(playerSlotViews[slotIndex].transform));
                }
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
                combatLogText.text = ArabicFixer.Fix(string.Join("\n", logEntries));
            }
            Debug.Log($"[LOG]: {msg}");
        }

        public void ShowBanner(string text, Color color)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                StartCoroutine(BattleAnimations.AnimateTurnBanner(canvas, text, color));
            }
        }

        public void SpawnDamageNumber(Vector3 worldPos, int damage, bool isLeaderHit)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                StartCoroutine(BattleAnimations.AnimateFloatingDamage(canvas.transform, worldPos, damage, isLeaderHit));
            }
        }

        public void ShakeScreen()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                StartCoroutine(BattleAnimations.AnimateScreenShake(canvas.transform));
            }
        }

        public void TriggerHitShake(Transform target, Color color)
        {
            if (target != null)
            {
                StartCoroutine(BattleAnimations.AnimateHitShake(target, color));
            }
        }

        public SlotView GetSlotView(int index, bool isPlayer)
        {
            var arr = isPlayer ? playerSlotViews : enemySlotViews;
            if (arr != null && index >= 0 && index < arr.Length)
            {
                return arr[index];
            }
            return null;
        }

        public Transform GetLeaderTransform(bool isPlayer)
        {
            return isPlayer 
                ? (playerHpText != null ? playerHpText.transform.parent : null) 
                : (enemyHpText != null ? enemyHpText.transform.parent : null);
        }
    }
}

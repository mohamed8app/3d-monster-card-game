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
        public Button playCardButton;
        public Button quitButton;

        [Header("Containers")]
        public Transform playerHandContainer;
        public SlotView[] playerSlotViews = new SlotView[6];
        public SlotView[] enemySlotViews = new SlotView[6];

        [Header("Environment Altar (محراب المناخ والبيئة)")]
        public GameObject envAltarPanel;
        public Text envIconText;
        public Text envNameText;
        public Text envLevelText;
        public Text envDurationText;
        public Text envBuffText;
        public Text envDebuffText;
        public Image envAtmosphereOverlay;
        public Button testCycleEnvButton;
        public Button testCycleLevelButton;

        private static readonly string[] AllTestElements = {
            "نار", "مياه", "جليد", "أرض", "كهرباء", "رياح",
            "غابة", "ضياء", "ظلام", "ميكا", "أشباح", "سحر"
        };
        private int currentTestEnvIndex = -1;

        [Header("Combat Log")]
        public Text combatLogText;
        private List<string> logEntries = new List<string>();

        [Header("Game Over Modal (خاتمة المعركة)")]
        public GameObject gameOverPanel;
        public Text gameOverTitleText;
        public Text gameOverSubtitleText;
        public Text gameOverStatsText;
        public Outline gameOverModalOutline;
        public Button restartGameButton;
        public Button gameOverQuitButton;

        private CardData selectedCardForAction = null;

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
            if (playCardButton != null) playCardButton.onClick.AddListener(OnPlayCardClicked);

            if (testCycleEnvButton == null)
            {
                var teb = GameObject.Find("TestCycleEnvBtn");
                if (teb != null) testCycleEnvButton = teb.GetComponent<Button>();
            }
            if (testCycleEnvButton != null)
            {
                testCycleEnvButton.onClick.RemoveAllListeners();
                testCycleEnvButton.onClick.AddListener(OnTestCycleEnvironmentClicked);
            }

            if (testCycleLevelButton == null)
            {
                var tlb = GameObject.Find("TestCycleLevelBtn");
                if (tlb != null) testCycleLevelButton = tlb.GetComponent<Button>();
            }
            if (testCycleLevelButton != null)
            {
                testCycleLevelButton.onClick.RemoveAllListeners();
                testCycleLevelButton.onClick.AddListener(OnTestCycleEnvLevelClicked);
            }

            if (quitButton == null)
            {
                var qb = GameObject.Find("QuitGameBtn");
                if (qb != null) quitButton = qb.GetComponent<Button>();
            }
            if (quitButton != null)
            {
                quitButton.onClick.RemoveAllListeners();
                quitButton.onClick.AddListener(OnQuitClicked);
            }

            UpdateUI();
            StartCoroutine(DelayedInitialUpdate());
        }

        private System.Collections.IEnumerator DelayedInitialUpdate()
        {
            yield return null;
            EnsureAtmosphereEngine();
            UpdateUI();
            UpdateEnvironmentVisuals();
        }

        private void EnsureAtmosphereEngine()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                var bgTr = canvas.transform.Find("ArenaBackground");
                var bgImg = bgTr != null ? bgTr.GetComponent<Image>() : null;
                BattlefieldAtmosphereEngine.EnsureInstance(canvas.transform, bgImg, envAtmosphereOverlay);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                OnQuitClicked();
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                OnTestCycleEnvironmentClicked();
            }
            else if (Input.GetKeyDown(KeyCode.L))
            {
                OnTestCycleEnvLevelClicked();
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

        /// <summary>
        /// زر تجربة وتبديل البيئات الـ 12 + الوضع الحربي الافتراضي فورياً بدون استهلاك مانا
        /// </summary>
        public void OnTestCycleEnvironmentClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            currentTestEnvIndex++;
            if (currentTestEnvIndex >= AllTestElements.Length)
            {
                // العودة إلى الوضع الحربي الافتراضي (بدون بيئة عنصرية)
                currentTestEnvIndex = -1;
                gm.activeEnvironment = null;
                LogCombatMessage("⚔️ [تجربة المناخ]: العودة إلى طقس المعركة الحربي الافتراضي (متكافئ).");
            }
            else
            {
                string elem = AllTestElements[currentTestEnvIndex];
                int currentLvl = (gm.activeEnvironment != null) ? gm.activeEnvironment.level : 1;
                gm.activeEnvironment = new EnvironmentCardData(elem, currentLvl);
                if (BattlefieldAtmosphereEngine.Instance != null)
                {
                    Color c = MonsterArtLoader.GetElementColor(elem);
                    BattlefieldAtmosphereEngine.Instance.TriggerActionPulse(c, 1.4f);
                }
                LogCombatMessage($"🌍 [تجربة المناخ {currentTestEnvIndex + 1}/12]: تم تفعيل [{gm.activeEnvironment.name}] ({elem} - المستوى {currentLvl})!");
            }

            UpdateEnvironmentVisuals();
            UpdateUI();
        }

        /// <summary>
        /// زر تجربة ترقية مستوى البيئة الحالية (المستوى 1 ★ -> 2 ★★ -> 3 ★★★)
        /// </summary>
        public void OnTestCycleEnvLevelClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            if (gm.activeEnvironment == null)
            {
                // إذا لم تكن هناك بيئة نشطة، نفعل أول بيئة (بركان الحمم) في المستوى 1
                currentTestEnvIndex = 0;
                gm.activeEnvironment = new EnvironmentCardData(AllTestElements[0], 1);
            }
            else
            {
                int nextLevel = (gm.activeEnvironment.level % 3) + 1;
                gm.activeEnvironment.SetLevelStats(nextLevel);
            }

            if (BattlefieldAtmosphereEngine.Instance != null)
            {
                Color c = MonsterArtLoader.GetElementColor(gm.activeEnvironment.element);
                BattlefieldAtmosphereEngine.Instance.TriggerActionPulse(c, 1.5f);
            }
            LogCombatMessage($"⭐ [تجربة المستوى]: بيئة [{gm.activeEnvironment.name}] الآن في المستوى {gm.activeEnvironment.level} (تعزيز +{Mathf.RoundToInt(gm.activeEnvironment.buffPct * 100)}%)!");

            UpdateEnvironmentVisuals();
            UpdateUI();
        }

        public void OnQuitClicked()
        {
            Debug.Log("🚪 تم الضغط على زر الخروج - إغلاق اللعبة الآن...");
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #else
            Application.Quit();
            try
            {
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            catch { }
            #endif
        }

        public void UpdateUI()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            // 1. تحديث صحة القائدين
            if (playerHpText != null) playerHpText.text = ArabicFixer.Fix($"❤️ {gm.humanPlayer.currentHp} / 1000 HP");
            if (playerHpSlider != null) playerHpSlider.value = gm.humanPlayer.currentHp / 1000f;

            if (enemyHpText != null) enemyHpText.text = ArabicFixer.Fix($"❤️ {gm.aiPlayer.currentHp} / 1000 HP");
            if (enemyHpSlider != null) enemyHpSlider.value = gm.aiPlayer.currentHp / 1000f;

            // 2. تحديث المانا والباور مع رسم جواهر المانا المتوهجة (Mana Crystals)
            if (playerPowerText != null)
            {
                string crystals = "";
                int power = gm.humanPlayer.currentPower;
                int maxCap = gm.humanPlayer.manaCapacity;

                for (int i = 0; i < maxCap; i++)
                {
                    crystals += (i < power) ? "💎" : "◇";
                }

                playerPowerText.text = ArabicFixer.Fix($"[ {crystals} ]  الباور: {power}/{maxCap}  |  المانا: {maxCap}/8");
            }

            // 3. مؤشر الدور
            if (turnInfoText != null)
            {
                turnInfoText.text = ArabicFixer.Fix(gm.isPlayerTurn ? "⚔️ دورك الآن (استدعِ، ألقِ السحر، أو هاجم)" : "⏳ دور الخصم (يفكر...)");
            }

            // 4. أزرار التحكم
            if (attackButton != null) attackButton.interactable = gm.isPlayerTurn;
            if (endTurnButton != null) endTurnButton.interactable = gm.isPlayerTurn;
            if (sacrificeButton != null)
            {
                sacrificeButton.interactable = gm.isPlayerTurn && !gm.humanPlayer.hasSacrificedThisTurn && selectedCardForAction != null;
            }

            // تحديث زر تفعيل السحر أو البيئة عند تحديد بطاقة
            if (playCardButton != null)
            {
                bool canPlaySelected = gm.isPlayerTurn && selectedCardForAction != null && 
                    (selectedCardForAction.cardType == CardType.Spell || selectedCardForAction.cardType == CardType.Environment) &&
                    selectedCardForAction.cost <= gm.humanPlayer.currentPower;

                playCardButton.interactable = canPlaySelected;
                var txt = playCardButton.GetComponentInChildren<Text>();
                if (txt != null)
                {
                    if (selectedCardForAction != null && selectedCardForAction.cardType == CardType.Spell)
                        txt.text = ArabicFixer.Fix($"🔮 إطلاق التعويذة ({selectedCardForAction.cost}⚡)");
                    else if (selectedCardForAction != null && selectedCardForAction.cardType == CardType.Environment)
                        txt.text = ArabicFixer.Fix($"🌍 تنشيط المناخ ({selectedCardForAction.cost}⚡)");
                    else
                        txt.text = ArabicFixer.Fix("✨ تفعيل البطاقة");
                }
            }

            // 5. تحديث خانات الساحة
            UpdateSlots();

            // 6. تحديث كروت يد اللاعب
            RefreshPlayerHand();

            // 7. تحديث كروت يد الخصم
            RefreshEnemyHand();

            // 8. تحديث محراب البيئة والمناخ
            UpdateEnvironmentVisuals();
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

        public void UpdateEnvironmentVisuals()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            var env = gm.activeEnvironment;
            if (env != null)
            {
                string envEmoji = MonsterArtLoader.GetEnvironmentEmoji(env.element);
                Color elemColor = MonsterArtLoader.GetElementColor(env.element);

                if (envIconText != null) envIconText.text = envEmoji;
                if (envNameText != null)
                {
                    envNameText.text = ArabicFixer.Fix($"{env.name}");
                    envNameText.color = elemColor;
                }
                if (envLevelText != null)
                {
                    string stars = new string('★', env.level);
                    envLevelText.text = ArabicFixer.Fix($"{stars} المستوى {env.level}");
                    envLevelText.color = new Color(1f, 0.9f, 0.4f);
                }
                if (envDurationText != null)
                {
                    envDurationText.text = ArabicFixer.Fix($"⏳ متبقي: {env.turnsRemaining} جولات");
                }
                if (envBuffText != null)
                {
                    int buffPercent = Mathf.RoundToInt(env.buffPct * 100);
                    envBuffText.text = ArabicFixer.Fix($"⬆️ {env.buffedElement}: +{buffPercent}% هجوم");
                    envBuffText.color = new Color(0.2f, 1f, 0.45f);
                }
                if (envDebuffText != null)
                {
                    int debuffPercent = Mathf.RoundToInt(env.debuffPct * 100);
                    string debuffedStr = string.Join("، ", env.debuffedElements);
                    envDebuffText.text = ArabicFixer.Fix($"⬇️ {debuffedStr}: -{debuffPercent}%");
                    envDebuffText.color = new Color(1f, 0.45f, 0.45f);
                }

                // تحديث محرك الغلاف الجوي والطقس الحربي المتحرك (Living Battlefield & Weather Engine)
                EnsureAtmosphereEngine();
                if (BattlefieldAtmosphereEngine.Instance != null)
                {
                    BattlefieldAtmosphereEngine.Instance.ApplyEnvironmentProfile(env);
                }
            }
            else
            {
                if (envIconText != null) envIconText.text = "☀️";
                if (envNameText != null)
                {
                    envNameText.text = ArabicFixer.Fix("المناخ: مستقر");
                    envNameText.color = new Color(0.85f, 0.9f, 1f);
                }
                if (envLevelText != null)
                {
                    envLevelText.text = ArabicFixer.Fix("طبيعي (محايد)");
                    envLevelText.color = new Color(0.65f, 0.75f, 0.85f);
                }
                if (envDurationText != null) envDurationText.text = ArabicFixer.Fix("⏳ دائم");
                if (envBuffText != null)
                {
                    envBuffText.text = ArabicFixer.Fix("• لا توجد تعزيزات نشطة");
                    envBuffText.color = new Color(0.7f, 0.75f, 0.8f);
                }
                if (envDebuffText != null)
                {
                    envDebuffText.text = ArabicFixer.Fix("• كافة العناصر متكافئة");
                    envDebuffText.color = new Color(0.7f, 0.75f, 0.8f);
                }

                EnsureAtmosphereEngine();
                if (BattlefieldAtmosphereEngine.Instance != null)
                {
                    BattlefieldAtmosphereEngine.Instance.ApplyEnvironmentProfile(null);
                }
            }
        }

        private void RefreshPlayerHand()
        {
            var gm = GameManager.Instance;
            if (gm == null || playerHandContainer == null) return;

            foreach (Transform child in playerHandContainer)
            {
                Destroy(child.gameObject);
            }

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

            foreach (Transform child in enemyHandContainer)
            {
                Destroy(child.gameObject);
            }

            int handCount = gm.aiPlayer.hand.Count;
            if (enemyHandCountText != null)
            {
                enemyHandCountText.text = ArabicFixer.Fix($"🎴 يد الخصم: {handCount} بطاقات مستترة");
            }

            for (int i = 0; i < handCount; i++)
            {
                CreateFaceDownCardUI(enemyHandContainer, i);
            }
        }

        private GameObject CreateFaceDownCardUI(Transform parent, int index)
        {
            var cardObj = GameBootstrapper.CreatePanel(parent, $"EnemyCardBack_{index}", new Color(0.11f, 0.08f, 0.18f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(52, 72));

            var layoutElem = cardObj.AddComponent<LayoutElement>();
            layoutElem.preferredWidth = 52;
            layoutElem.preferredHeight = 72;
            layoutElem.minWidth = 52;
            layoutElem.minHeight = 72;

            var outline = cardObj.AddComponent<Outline>();
            outline.effectColor = new Color(0.92f, 0.76f, 0.28f, 0.95f);
            outline.effectDistance = new Vector2(1.5f, 1.5f);

            var innerPanel = GameBootstrapper.CreatePanel(cardObj.transform, "InnerRuneBorder", new Color(0.07f, 0.05f, 0.12f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(42, 60));

            var innerOutline = innerPanel.AddComponent<Outline>();
            innerOutline.effectColor = new Color(0.55f, 0.35f, 0.8f, 0.7f);
            innerOutline.effectDistance = new Vector2(1, 1);

            GameBootstrapper.CreateText(innerPanel.transform, "RuneSymbol", "🔮", 16,
                new Color(1f, 0.85f, 0.35f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(38, 48));

            return cardObj;
        }

        private GameObject CreateDynamicCardUI(Transform parent, CardData card)
        {
            Color elemColor = MonsterArtLoader.GetElementColor(card.element);
            string elemEmoji = MonsterArtLoader.GetElementEmoji(card.element);

            // الغلاف الخارجي للبطاقة العمودية الفاخرة (Portrait 135x195)
            Color cardBgColor = card.cardType switch
            {
                CardType.Spell => new Color(0.10f, 0.06f, 0.16f, 0.98f),       // أرجواني سحري غامق
                CardType.Environment => new Color(0.06f, 0.11f, 0.14f, 0.98f), // زمردي/سيان مناخي
                _ => new Color(0.08f, 0.09f, 0.14f, 0.98f)                     // رمادي فضي للوحوش
            };

            var cardObj = GameBootstrapper.CreatePanel(parent, $"Card_{card.name}", cardBgColor,
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(135, 195));

            var layoutElem = cardObj.AddComponent<LayoutElement>();
            layoutElem.preferredWidth = 135;
            layoutElem.preferredHeight = 195;
            layoutElem.minWidth = 135;
            layoutElem.minHeight = 195;

            var outline = cardObj.AddComponent<Outline>();
            outline.effectColor = (card.cardType == CardType.Spell) 
                ? new Color(0.95f, 0.75f, 0.25f, 0.95f) // إطار ذهبي سحري
                : elemColor;
            outline.effectDistance = new Vector2(2, 2);

            // 1. شارة الباور / المانا (أعلى اليسار)
            var manaBadge = GameBootstrapper.CreatePanel(cardObj.transform, "ManaBadge", new Color(0.85f, 0.65f, 0.15f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-40, 80), new Vector2(40, 22));
            var manaOutline = manaBadge.AddComponent<Outline>();
            manaOutline.effectColor = new Color(1f, 0.9f, 0.4f, 0.8f);
            manaOutline.effectDistance = new Vector2(1, 1);
            var txtMana = GameBootstrapper.CreateText(manaBadge.transform, "ManaText", $"{card.cost}⚡", 12,
                new Color(0.1f, 0.08f, 0.05f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(38, 20));
            txtMana.raycastTarget = false;

            // 2. شارة نوع البطاقة والعنصر (أعلى اليمين)
            string typeLabel = card.cardType switch
            {
                CardType.Spell => "🔮 سحر",
                CardType.Environment => "🌍 بيئة",
                _ => $"{elemEmoji} {card.element}"
            };

            var elemBadge = GameBootstrapper.CreatePanel(cardObj.transform, "ElemBadge", new Color(0.06f, 0.07f, 0.10f, 0.90f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(28, 80), new Vector2(66, 22));
            var elemBadgeOutline = elemBadge.AddComponent<Outline>();
            elemBadgeOutline.effectColor = elemColor;
            elemBadgeOutline.effectDistance = new Vector2(1, 1);
            var txtElem = GameBootstrapper.CreateText(elemBadge.transform, "ElemText", typeLabel, 11,
                elemColor, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(64, 20));
            txtElem.raycastTarget = false;

            if (card.cardType == CardType.Monster)
            {
                // تفاصيل بطاقة الوحش:
                // صورة الوحش الفنية الملحمية
                var artObj = new GameObject("CardArt");
                artObj.transform.SetParent(cardObj.transform, false);
                var rectArt = artObj.AddComponent<RectTransform>();
                rectArt.anchoredPosition = new Vector2(0, 24);
                rectArt.sizeDelta = new Vector2(125, 82);
                var artImg = artObj.AddComponent<Image>();
                var sprite = MonsterArtLoader.GetMonsterSprite(card);
                if (sprite != null) artImg.sprite = sprite;
                artImg.raycastTarget = false;

                // اسم الوحش في شريط زجاجي
                var namePanel = GameBootstrapper.CreatePanel(cardObj.transform, "NamePlate", new Color(0.05f, 0.06f, 0.09f, 0.92f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -28), new Vector2(125, 20));
                var txtName = GameBootstrapper.CreateText(namePanel.transform, "NameText", card.name, 12,
                    Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120, 20));
                txtName.raycastTarget = false;

                // شارة الهجوم (يسار)
                var atkBadge = GameBootstrapper.CreatePanel(cardObj.transform, "AtkBadge", new Color(0.65f, 0.16f, 0.16f, 0.95f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-34, -68), new Vector2(54, 24));
                var atkOutline = atkBadge.AddComponent<Outline>();
                atkOutline.effectColor = new Color(1f, 0.4f, 0.4f, 0.8f);
                atkOutline.effectDistance = new Vector2(1, 1);
                var txtAtk = GameBootstrapper.CreateText(atkBadge.transform, "AtkText", $"⚔️ {card.atk}", 12,
                    new Color(1f, 0.95f, 0.45f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(52, 22));
                txtAtk.raycastTarget = false;

                // شارة الدفاع (يمين)
                var defBadge = GameBootstrapper.CreatePanel(cardObj.transform, "DefBadge", new Color(0.14f, 0.34f, 0.70f, 0.95f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(34, -68), new Vector2(54, 24));
                var defOutline = defBadge.AddComponent<Outline>();
                defOutline.effectColor = new Color(0.35f, 0.75f, 1f, 0.8f);
                defOutline.effectDistance = new Vector2(1, 1);
                var txtDef = GameBootstrapper.CreateText(defBadge.transform, "DefText", $"🛡️ {card.def}", 12,
                    new Color(0.45f, 0.95f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(52, 22));
                txtDef.raycastTarget = false;
            }
            else if (card.cardType == CardType.Spell)
            {
                // تفاصيل بطاقة السحر:
                // أيقونة السحر المتوهجة
                string spellIcon = MonsterArtLoader.GetSpellIcon(card.spellType);
                var iconObj = GameBootstrapper.CreateText(cardObj.transform, "SpellIcon", spellIcon, 36,
                    new Color(1f, 0.9f, 0.35f), TextAnchor.MiddleCenter, new Vector2(0, 32), new Vector2(120, 50));
                iconObj.raycastTarget = false;

                // اسم التعويذة
                var namePanel = GameBootstrapper.CreatePanel(cardObj.transform, "NamePlate", new Color(0.18f, 0.12f, 0.28f, 0.92f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(125, 22));
                var txtName = GameBootstrapper.CreateText(namePanel.transform, "NameText", card.name, 12,
                    new Color(1f, 0.9f, 0.4f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120, 20));
                txtName.raycastTarget = false;

                // مربع وصف مفعول التعويذة
                var descPanel = GameBootstrapper.CreatePanel(cardObj.transform, "DescPanel", new Color(0.06f, 0.05f, 0.10f, 0.85f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -42), new Vector2(125, 52));
                var txtDesc = GameBootstrapper.CreateText(descPanel.transform, "DescText", card.description, 10,
                    new Color(0.92f, 0.92f, 0.96f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120, 48));
                txtDesc.raycastTarget = false;

                // شارة الاستخدام الفوري بالأسفل
                var actionBadge = GameBootstrapper.CreatePanel(cardObj.transform, "ActionBadge", new Color(0.45f, 0.20f, 0.65f, 0.95f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -78), new Vector2(122, 22));
                var txtAction = GameBootstrapper.CreateText(actionBadge.transform, "ActionText", "✨ إلقاء فوري", 11,
                    Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120, 20));
                txtAction.raycastTarget = false;
            }
            else if (card.cardType == CardType.Environment)
            {
                // تفاصيل بطاقة البيئة والمناخ:
                string envEmoji = MonsterArtLoader.GetEnvironmentEmoji(card.element);
                var iconObj = GameBootstrapper.CreateText(cardObj.transform, "EnvIcon", envEmoji, 36,
                    elemColor, TextAnchor.MiddleCenter, new Vector2(0, 32), new Vector2(120, 50));
                iconObj.raycastTarget = false;

                // اسم البيئة
                var namePanel = GameBootstrapper.CreatePanel(cardObj.transform, "NamePlate", new Color(0.08f, 0.14f, 0.18f, 0.92f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(125, 22));
                var txtName = GameBootstrapper.CreateText(namePanel.transform, "NameText", card.name, 12,
                    elemColor, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120, 20));
                txtName.raycastTarget = false;

                // وصف تأثير البيئة
                var descPanel = GameBootstrapper.CreatePanel(cardObj.transform, "DescPanel", new Color(0.05f, 0.08f, 0.11f, 0.85f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -42), new Vector2(125, 52));
                var txtDesc = GameBootstrapper.CreateText(descPanel.transform, "DescText", card.description, 10,
                    new Color(0.9f, 0.95f, 0.95f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120, 48));
                txtDesc.raycastTarget = false;

                // شارة مدة البيئة
                var actionBadge = GameBootstrapper.CreatePanel(cardObj.transform, "ActionBadge", new Color(0.15f, 0.40f, 0.55f, 0.95f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -78), new Vector2(122, 22));
                var txtAction = GameBootstrapper.CreateText(actionBadge.transform, "ActionText", $"⏳ تدوم {card.duration} جولات", 11,
                    new Color(0.9f, 0.98f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120, 20));
                txtAction.raycastTarget = false;
            }

            return cardObj;
        }

        public void OnCardSelectedInHand(CardData card)
        {
            // إذا ضغط اللاعب على نفس الكارت المحدد حالياً، يتم إلغاء التحديد بسلاسة
            if (selectedCardForAction == card)
            {
                DeselectHandCards();
                LogCombatMessage("تم إلغاء تحديد البطاقة.");
                return;
            }

            selectedCardForAction = card;

            // تحديث حالة التحديد البصرية لكافة البطاقات في اليد فوراً وبدون أي وميض أو إعادة ترتيب
            if (playerHandContainer != null)
            {
                foreach (Transform child in playerHandContainer)
                {
                    var drag = child.GetComponent<DraggableCard>();
                    if (drag != null)
                    {
                        drag.SetSelected(drag.cardData == card);
                    }
                }
            }

            if (card.cardType == CardType.Monster)
            {
                LogCombatMessage($"تم تحديد الوحش: [{card.name}] (تكلفة: {card.cost}⚡) - اضغط على خانة شاغرة لإنزاله أو اضغط 'تضحية'.");
            }
            else if (card.cardType == CardType.Spell)
            {
                LogCombatMessage($"تم تحديد التعويذة: [{card.name}] (تكلفة: {card.cost}⚡) - اضغط زر 'إطلاق التعويذة' أو اسحبها للأعلى لإلقائها فوراً!");
            }
            else if (card.cardType == CardType.Environment)
            {
                LogCombatMessage($"تم تحديد بطاقة المناخ: [{card.name}] (تكلفة: {card.cost}⚡) - اضغط زر 'تنشيط المناخ' أو اسحبها للأعلى لتبديل بيئة المعركة!");
            }

            if (playCardButton != null)
            {
                var gm = GameManager.Instance;
                bool canPlay = gm != null && gm.isPlayerTurn && card.cost <= gm.humanPlayer.currentPower &&
                    (card.cardType == CardType.Spell || card.cardType == CardType.Environment);
                playCardButton.interactable = canPlay;

                var txt = playCardButton.GetComponentInChildren<Text>();
                if (txt != null)
                {
                    if (card.cardType == CardType.Spell)
                        txt.text = ArabicFixer.Fix($"🔮 إطلاق التعويذة ({card.cost}⚡)");
                    else if (card.cardType == CardType.Environment)
                        txt.text = ArabicFixer.Fix($"🌍 تنشيط المناخ ({card.cost}⚡)");
                }
            }

            if (sacrificeButton != null)
            {
                var gm = GameManager.Instance;
                sacrificeButton.interactable = gm != null && gm.isPlayerTurn && !gm.humanPlayer.hasSacrificedThisTurn;
            }
        }

        public void DeselectHandCards()
        {
            selectedCardForAction = null;

            if (playerHandContainer != null)
            {
                foreach (Transform child in playerHandContainer)
                {
                    var drag = child.GetComponent<DraggableCard>();
                    if (drag != null)
                    {
                        drag.SetSelected(false);
                    }
                }
            }

            if (playCardButton != null)
            {
                playCardButton.interactable = false;
                var txt = playCardButton.GetComponentInChildren<Text>();
                if (txt != null) txt.text = ArabicFixer.Fix("✨ تفعيل البطاقة");
            }

            if (sacrificeButton != null)
            {
                sacrificeButton.interactable = false;
            }
        }

        public void OnSlotClicked(int slotIndex, bool isPlayerSide)
        {
            if (!isPlayerSide || selectedCardForAction == null) return;

            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn) return;

            if (selectedCardForAction.cardType != CardType.Monster)
            {
                LogCombatMessage("⚠️ هذه البطاقة ليست وحشاً! بطاقات السحر والبيئة لا تحتل خانات الوحوش.");
                return;
            }

            var chosenCard = selectedCardForAction;
            if (gm.humanPlayer.SummonMonster(chosenCard, slotIndex, gm.currentTurn))
            {
                LogCombatMessage($"✅ تم إنزال [{chosenCard.name}] في الخانة {slotIndex}.");
                DeselectHandCards();
                UpdateUI();
                if (slotIndex >= 0 && slotIndex < playerSlotViews.Length && playerSlotViews[slotIndex] != null)
                {
                    StartCoroutine(BattleAnimations.AnimateCardSummon(playerSlotViews[slotIndex].transform));
                }
            }
            else
            {
                LogCombatMessage($"❌ لا يمكنك إنزال الوحش في الخانة {slotIndex} (تأكد من الباور والشواغر).");
            }
        }

        private void OnPlayCardClicked()
        {
            if (selectedCardForAction == null) return;
            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn) return;

            var chosenCard = selectedCardForAction;
            if (chosenCard.cardType == CardType.Spell)
            {
                if (gm.CastSpell(gm.humanPlayer, gm.aiPlayer, chosenCard))
                {
                    DeselectHandCards();
                    UpdateUI();
                }
            }
            else if (chosenCard.cardType == CardType.Environment)
            {
                if (gm.PlayEnvironmentCard(gm.humanPlayer, chosenCard))
                {
                    DeselectHandCards();
                    UpdateUI();
                }
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
            var chosenCard = selectedCardForAction;
            if (gm.humanPlayer.SacrificeCard(chosenCard))
            {
                LogCombatMessage($"🔥 تمت التضحية بـ [{chosenCard.name}]! كسبت +1 سقف مانا و +1 باور.");
                DeselectHandCards();
                UpdateUI();
            }
        }

        private void OnAttackClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            DeselectHandCards();
            LogCombatMessage("⚔️ إطلاق مرحلة الهجوم! تندفع وحوشك نحو خطوط العدو...");
            gm.OnPlayerExecuteAttackButton();
            UpdateUI();
        }

        private void OnEndTurnClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn) return;

            DeselectHandCards();
            gm.OnPlayerEndTurn();
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

        public void ShowSpellBanner(CardData spellCard, string casterName)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                StartCoroutine(BattleAnimations.AnimateSpellCast(canvas, spellCard, casterName));
            }
            if (BattlefieldAtmosphereEngine.Instance != null && spellCard != null)
            {
                Color c = MonsterArtLoader.GetElementColor(spellCard.element);
                BattlefieldAtmosphereEngine.Instance.TriggerActionPulse(c, 1.35f);
            }
        }

        public void ShowEnvironmentBanner(EnvironmentCardData envData, string activatorName)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                StartCoroutine(BattleAnimations.AnimateEnvironmentShift(canvas, envData, activatorName));
            }
            if (BattlefieldAtmosphereEngine.Instance != null && envData != null)
            {
                Color c = MonsterArtLoader.GetElementColor(envData.element);
                BattlefieldAtmosphereEngine.Instance.TriggerActionPulse(c, 1.6f);
            }
        }

        public void SpawnDamageNumber(Vector3 worldPos, int damage, bool isLeaderHit)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                StartCoroutine(BattleAnimations.AnimateFloatingDamage(canvas.transform, worldPos, damage, isLeaderHit));
            }
            if (BattlefieldAtmosphereEngine.Instance != null)
            {
                Color pulseCol = isLeaderHit ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.65f, 0.25f);
                BattlefieldAtmosphereEngine.Instance.TriggerActionPulse(pulseCol, isLeaderHit ? 1.4f : 0.85f);
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

        #region Game Over & Conclusion

        public void ShowGameOverScreen(bool isVictory, int turns, int playerHp, int enemyHp, int monstersSummoned, int spellsCast, string envName, int manaCap)
        {
            if (gameOverPanel == null) return;

            gameOverPanel.SetActive(true);

            if (isVictory)
            {
                if (gameOverTitleText != null)
                {
                    gameOverTitleText.text = ArabicFixer.Fix("🏆 نصر أسطوري مؤزر!");
                    gameOverTitleText.color = new Color(1f, 0.88f, 0.35f);
                }
                if (gameOverSubtitleText != null)
                {
                    gameOverSubtitleText.text = ArabicFixer.Fix("لقد سحقت قائد العدو وفرضت سيطرتك المطلقة على الساحة!");
                    gameOverSubtitleText.color = new Color(0.85f, 1f, 0.9f);
                }
                if (gameOverModalOutline != null)
                {
                    gameOverModalOutline.effectColor = new Color(1f, 0.85f, 0.25f, 0.95f);
                }
            }
            else
            {
                if (gameOverTitleText != null)
                {
                    gameOverTitleText.text = ArabicFixer.Fix("💀 سقط قائدك في الميدان!");
                    gameOverTitleText.color = new Color(0.95f, 0.3f, 0.3f);
                }
                if (gameOverSubtitleText != null)
                {
                    gameOverSubtitleText.text = ArabicFixer.Fix("استبسل جيشك حتى الرمق الأخير.. حظاً أوفر في الجولة القادمة!");
                    gameOverSubtitleText.color = new Color(1f, 0.8f, 0.8f);
                }
                if (gameOverModalOutline != null)
                {
                    gameOverModalOutline.effectColor = new Color(0.9f, 0.25f, 0.3f, 0.95f);
                }
            }

            if (gameOverStatsText != null)
            {
                string stats = 
                    $"⏱️ الجولات المستغرقة: {turns} جولات تكتيكية\n" +
                    $"❤️ صحة بطلك المتبقية: {playerHp} / 1000 HP\n" +
                    $"💀 صحة قائد الخصم: {enemyHp} / 1000 HP\n" +
                    $"⚔️ الوحوش التي استدعيتها: {monstersSummoned} وحشاً\n" +
                    $"🔮 التعاويذ التي أطلقتها: {spellsCast} تعويذة\n" +
                    $"🌍 مناخ نهاية المعركة: {envName}\n" +
                    $"💎 سقف المانا النهائي: {manaCap} / 8";

                gameOverStatsText.text = ArabicFixer.Fix(stats);
            }

            var modalBox = gameOverPanel.transform.Find("ModalBox");
            if (modalBox != null)
            {
                StartCoroutine(BattleAnimations.AnimateModalPopIn(modalBox));
            }
        }

        public void OnRestartClicked()
        {
            if (gameOverPanel != null) gameOverPanel.SetActive(false);
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.RestartGame();
            }
        }

        #endregion
    }
}

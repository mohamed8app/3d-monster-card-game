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

        [Header("Monster & Card Details Inspector (لوحة تفاصيل الكروت والوحوش)")]
        public GameObject cardDetailsPanel;
        public Image detailCardArt;
        public Text detailCostText;
        public Text detailAtkText;
        public Text detailDefText;
        public Text detailNameText;
        public Text detailMetaText;
        public Text detailStatsText;
        public Text detailDescText;
        public Text detailStatusText;

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
            if (playerHpText != null) playerHpText.text = ArabicFixer.Fix($"❤️ {gm.humanPlayer.currentHp} / {gm.humanPlayer.maxHp} HP");
            if (playerHpSlider != null) playerHpSlider.value = (float)gm.humanPlayer.currentHp / gm.humanPlayer.maxHp;

            if (enemyHpText != null) enemyHpText.text = ArabicFixer.Fix($"❤️ {gm.aiPlayer.currentHp} / {gm.aiPlayer.maxHp} HP");
            if (enemyHpSlider != null) enemyHpSlider.value = (float)gm.aiPlayer.currentHp / gm.aiPlayer.maxHp;

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

            // الغلاف الخارجي للبطاقة العمودية الواضحة بحجم كبير ومقروء
            Color cardBgColor = card.cardType switch
            {
                CardType.Spell => new Color(0.12f, 0.08f, 0.20f, 0.98f),       // أرجواني سحري غامق
                CardType.Environment => new Color(0.06f, 0.12f, 0.16f, 0.98f), // زمردي/سيان مناخي
                _ => new Color(0.09f, 0.10f, 0.15f, 0.98f)                     // رمادي فحمي للوحوش
            };

            var cardObj = GameBootstrapper.CreatePanel(parent, $"Card_{card.name}", cardBgColor,
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(138, 182));

            var layoutElem = cardObj.AddComponent<LayoutElement>();
            layoutElem.preferredWidth = 138;
            layoutElem.preferredHeight = 182;
            layoutElem.minWidth = 138;
            layoutElem.minHeight = 182;

            var outline = cardObj.AddComponent<Outline>();
            outline.effectColor = (card.cardType == CardType.Spell) 
                ? new Color(0.95f, 0.75f, 0.25f, 0.95f) // إطار ذهبي سحري
                : elemColor;
            outline.effectDistance = new Vector2(2, 2);

            if (card.cardType == CardType.Monster)
            {
                // تفاصيل بطاقة الوحش بنمط Astral Masters (رسمة كاملة بدون فريم يضيقها + 3 دوائر في الأركان):
                // 1. صورة الوحش الفنية تمتد على كامل الكارت
                var artObj = new GameObject("CardArt");
                artObj.transform.SetParent(cardObj.transform, false);
                var rectArt = artObj.AddComponent<RectTransform>();
                rectArt.anchorMin = Vector2.zero;
                rectArt.anchorMax = Vector2.one;
                rectArt.sizeDelta = Vector2.zero;
                rectArt.anchoredPosition = Vector2.zero;
                var artImg = artObj.AddComponent<Image>();
                var sprite = MonsterArtLoader.GetMonsterSprite(card);
                if (sprite != null) artImg.sprite = sprite;
                artImg.preserveAspect = false;
                artImg.raycastTarget = false;

                // 2. دائرة نقاط الاستدعاء أعلى اليمين (Top-Right Blue Circle)
                var costCircle = GameBootstrapper.CreateCirclePanel(cardObj.transform, "CostCircle", new Color(0.10f, 0.28f, 0.65f, 0.96f),
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18, -18), new Vector2(32, 32));
                var costOutline = costCircle.AddComponent<Outline>();
                costOutline.effectColor = new Color(0.40f, 0.90f, 1f, 1f);
                costOutline.effectDistance = new Vector2(1.5f, 1.5f);
                var txtCost = GameBootstrapper.CreateText(costCircle.transform, "CostText", card.cost.ToString(), 16,
                    new Color(1f, 0.95f, 0.70f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(32, 32));
                txtCost.fontStyle = FontStyle.Bold;
                txtCost.raycastTarget = false;

                // 3. شارة الهجوم أسفل اليسار (Bottom-Left Red Circle)
                var atkBadge = GameBootstrapper.CreateCirclePanel(cardObj.transform, "AtkBadge", new Color(0.70f, 0.12f, 0.16f, 0.96f),
                    new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18, 18), new Vector2(32, 32));
                var atkOutline = atkBadge.AddComponent<Outline>();
                atkOutline.effectColor = new Color(1f, 0.45f, 0.35f, 1f);
                atkOutline.effectDistance = new Vector2(1.5f, 1.5f);
                var txtAtk = GameBootstrapper.CreateText(atkBadge.transform, "AtkText", card.atk.ToString(), 16,
                    Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(32, 32));
                txtAtk.fontStyle = FontStyle.Bold;
                txtAtk.raycastTarget = false;

                // 4. شارة الدفاع أسفل اليمين (Bottom-Right Green Circle)
                var defBadge = GameBootstrapper.CreateCirclePanel(cardObj.transform, "DefBadge", new Color(0.10f, 0.52f, 0.22f, 0.96f),
                    new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18, 18), new Vector2(32, 32));
                var defOutline = defBadge.AddComponent<Outline>();
                defOutline.effectColor = new Color(0.35f, 1f, 0.45f, 1f);
                defOutline.effectDistance = new Vector2(1.5f, 1.5f);
                var txtDef = GameBootstrapper.CreateText(defBadge.transform, "DefText", card.def.ToString(), 16,
                    Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(32, 32));
                txtDef.fontStyle = FontStyle.Bold;
                txtDef.raycastTarget = false;

                // 5. شريط اسم الوحش في منتصف الأسفل بين الهجوم والدفاع
                var namePanel = GameBootstrapper.CreatePanel(cardObj.transform, "NamePlate", new Color(0.04f, 0.05f, 0.08f, 0.85f),
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 16), new Vector2(66, 20));
                var txtName = GameBootstrapper.CreateText(namePanel.transform, "NameText", card.name, 10,
                    Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(64, 20));
                txtName.fontStyle = FontStyle.Bold;
                txtName.raycastTarget = false;
            }
            else if (card.cardType == CardType.Spell)
            {
                // دائرة نقاط الاستدعاء أعلى اليمين
                var costCircle = GameBootstrapper.CreateCirclePanel(cardObj.transform, "CostCircle", new Color(0.10f, 0.28f, 0.65f, 0.96f),
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18, -18), new Vector2(32, 32));
                var costOutline = costCircle.AddComponent<Outline>();
                costOutline.effectColor = new Color(0.95f, 0.75f, 0.35f, 1f);
                costOutline.effectDistance = new Vector2(1.5f, 1.5f);
                var txtCost = GameBootstrapper.CreateText(costCircle.transform, "CostText", card.cost.ToString(), 16,
                    new Color(1f, 0.95f, 0.70f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(32, 32));
                txtCost.fontStyle = FontStyle.Bold;
                txtCost.raycastTarget = false;

                // تفاصيل بطاقة السحر:
                string spellIcon = MonsterArtLoader.GetSpellIcon(card.spellType);
                var iconObj = GameBootstrapper.CreateText(cardObj.transform, "SpellIcon", spellIcon, 38,
                    new Color(1f, 0.9f, 0.35f), TextAnchor.MiddleCenter, new Vector2(0, 22), new Vector2(110, 46));
                iconObj.raycastTarget = false;

                // اسم التعويذة
                var namePanel = GameBootstrapper.CreatePanel(cardObj.transform, "NamePlate", new Color(0.18f, 0.12f, 0.28f, 0.92f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -16), new Vector2(130, 22));
                var txtName = GameBootstrapper.CreateText(namePanel.transform, "NameText", card.name, 13,
                    new Color(1f, 0.9f, 0.4f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(126, 22));
                txtName.fontStyle = FontStyle.Bold;
                txtName.raycastTarget = false;

                // وصف مختصر
                var descPanel = GameBootstrapper.CreatePanel(cardObj.transform, "DescPanel", new Color(0.06f, 0.05f, 0.10f, 0.85f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -46), new Vector2(130, 40));
                var txtDesc = GameBootstrapper.CreateText(descPanel.transform, "DescText", card.description, 12,
                    new Color(0.92f, 0.92f, 0.96f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(126, 38));
                txtDesc.fontStyle = FontStyle.Bold;
                txtDesc.raycastTarget = false;

                // شارة سحر
                var actionBadge = GameBootstrapper.CreatePanel(cardObj.transform, "ActionBadge", new Color(0.45f, 0.20f, 0.65f, 0.95f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -72), new Vector2(124, 22));
                var txtAction = GameBootstrapper.CreateText(actionBadge.transform, "ActionText", "🔮 سحر فوري", 12,
                    Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120, 20));
                txtAction.fontStyle = FontStyle.Bold;
                txtAction.raycastTarget = false;
            }
            else if (card.cardType == CardType.Environment)
            {
                // دائرة نقاط الاستدعاء أعلى اليمين
                var costCircle = GameBootstrapper.CreateCirclePanel(cardObj.transform, "CostCircle", new Color(0.10f, 0.28f, 0.65f, 0.96f),
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18, -18), new Vector2(32, 32));
                var costOutline = costCircle.AddComponent<Outline>();
                costOutline.effectColor = elemColor;
                costOutline.effectDistance = new Vector2(1.5f, 1.5f);
                var txtCost = GameBootstrapper.CreateText(costCircle.transform, "CostText", card.cost.ToString(), 16,
                    new Color(1f, 0.95f, 0.70f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(32, 32));
                txtCost.fontStyle = FontStyle.Bold;
                txtCost.raycastTarget = false;

                // تفاصيل بطاقة البيئة والمناخ:
                string envEmoji = MonsterArtLoader.GetEnvironmentEmoji(card.element);
                var iconObj = GameBootstrapper.CreateText(cardObj.transform, "EnvIcon", envEmoji, 38,
                    elemColor, TextAnchor.MiddleCenter, new Vector2(0, 22), new Vector2(110, 46));
                iconObj.raycastTarget = false;

                // اسم البيئة
                var namePanel = GameBootstrapper.CreatePanel(cardObj.transform, "NamePlate", new Color(0.08f, 0.14f, 0.18f, 0.92f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -16), new Vector2(130, 22));
                var txtName = GameBootstrapper.CreateText(namePanel.transform, "NameText", card.name, 13,
                    elemColor, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(126, 22));
                txtName.fontStyle = FontStyle.Bold;
                txtName.raycastTarget = false;

                // وصف مختصر
                var descPanel = GameBootstrapper.CreatePanel(cardObj.transform, "DescPanel", new Color(0.05f, 0.08f, 0.11f, 0.85f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -46), new Vector2(130, 40));
                var txtDesc = GameBootstrapper.CreateText(descPanel.transform, "DescText", card.description, 12,
                    new Color(0.9f, 0.95f, 0.95f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(126, 38));
                txtDesc.fontStyle = FontStyle.Bold;
                txtDesc.raycastTarget = false;

                // شارة مدة البيئة
                var actionBadge = GameBootstrapper.CreatePanel(cardObj.transform, "ActionBadge", new Color(0.15f, 0.40f, 0.55f, 0.95f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -72), new Vector2(124, 22));
                var txtAction = GameBootstrapper.CreateText(actionBadge.transform, "ActionText", $"🌍 {card.duration} جولات", 12,
                    new Color(0.9f, 0.98f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120, 20));
                txtAction.fontStyle = FontStyle.Bold;
                txtAction.raycastTarget = false;
            }

            return cardObj;
        }

        public void ShowMonsterDetails(CardData data, MonsterInstance instance, bool isPlayer)
        {
            if (data == null) return;
            if (cardDetailsPanel != null) cardDetailsPanel.SetActive(true);

            if (detailCardArt != null)
            {
                var sprite = MonsterArtLoader.GetMonsterSprite(data);
                if (sprite != null)
                {
                    detailCardArt.sprite = sprite;
                    detailCardArt.color = Color.white;
                    detailCardArt.gameObject.SetActive(true);
                }
            }

            if (detailCostText != null)
            {
                detailCostText.text = data.cost.ToString();
            }

            if (detailNameText != null)
            {
                string sideEmoji = isPlayer ? "👑" : "💀";
                detailNameText.text = ArabicFixer.Fix($"{sideEmoji} {data.name}");
                detailNameText.color = isPlayer ? new Color(1f, 0.90f, 0.45f) : new Color(1f, 0.55f, 0.55f);
            }

            if (detailMetaText != null)
            {
                string elemEmoji = MonsterArtLoader.GetElementEmoji(data.element);
                string sideStr = isPlayer ? "حليفك" : "معادٍ";
                detailMetaText.text = ArabicFixer.Fix($"{elemEmoji} عنصر {data.element} • {data.role} ({sideStr}) • استدعاء: {data.cost} مانا");
                detailMetaText.color = MonsterArtLoader.GetElementColor(data.element);
            }

            var gm = GameManager.Instance;
            var activeEnv = gm != null ? gm.activeEnvironment : null;
            int effAtk = (instance != null) ? instance.GetEffectiveAtk(activeEnv) : data.atk;
            int curDef = (instance != null) ? instance.currentDef : data.def;
            int maxDef = (instance != null && instance.baseDef > 0) ? instance.baseDef : data.def;
            float envMod = (instance != null) ? instance.GetEnvironmentModifier(activeEnv) : 1f;

            if (detailAtkText != null) detailAtkText.text = effAtk.ToString();
            if (detailDefText != null) detailDefText.text = curDef.ToString();

            if (detailStatsText != null)
            {
                string buffTag = "";
                if (envMod > 1.01f) buffTag = $" (معزز +{Mathf.RoundToInt((envMod - 1f) * 100)}% ⬆️)";
                else if (envMod < 0.99f) buffTag = $" (مخفض -{Mathf.RoundToInt((1f - envMod) * 100)}% ⬇️)";

                detailStatsText.text = ArabicFixer.Fix($"الهجوم: {effAtk}{buffTag}  |  الدفاع: {curDef} / {maxDef}");
            }

            if (detailDescText != null)
            {
                string desc = string.IsNullOrEmpty(data.description) 
                    ? $"وحش تكتيكي من عنصر {data.element} يمتلك قدرات قتالية عالية في الميدان." 
                    : data.description;
                detailDescText.text = ArabicFixer.Fix($"📜 القدرة: {desc}");
            }

            if (detailStatusText != null)
            {
                if (instance != null)
                {
                    if (isPlayer)
                    {
                        if (instance.canAttack)
                        {
                            detailStatusText.text = ArabicFixer.Fix("⚡ جاهز للهجوم والاشتباك هذا الدور!");
                            detailStatusText.color = new Color(0.2f, 1f, 0.5f);
                        }
                        else if (gm != null && instance.summonedTurn == gm.currentTurn)
                        {
                            detailStatusText.text = ArabicFixer.Fix("💤 نائم (استُدعي هذا الدور - يهاجم في الجولة القادمة).");
                            detailStatusText.color = new Color(0.5f, 0.8f, 1f);
                        }
                        else
                        {
                            detailStatusText.text = ArabicFixer.Fix("⏳ نفذ هجومه بالفعل هذا الدور.");
                            detailStatusText.color = new Color(0.9f, 0.7f, 0.3f);
                        }
                    }
                    else
                    {
                        if (!instance.canAttack && gm != null && instance.summonedTurn == gm.currentTurn)
                        {
                            detailStatusText.text = ArabicFixer.Fix("💤 وحش معادٍ استُدعي حديثاً (نائم هذا الدور).");
                            detailStatusText.color = new Color(0.7f, 0.7f, 0.9f);
                        }
                        else
                        {
                            detailStatusText.text = ArabicFixer.Fix("🎯 هدف معادٍ في ميدان المعركة.");
                            detailStatusText.color = new Color(1f, 0.45f, 0.45f);
                        }
                    }
                }
                else
                {
                    detailStatusText.text = ArabicFixer.Fix("🎴 في يدك - جاهز للاستدعاء.");
                    detailStatusText.color = new Color(0.4f, 0.85f, 1f);
                }
            }
        }

        public void ShowCardDetails(CardData data)
        {
            if (data == null) return;
            if (cardDetailsPanel != null) cardDetailsPanel.SetActive(true);

            if (detailCardArt != null)
            {
                var sprite = MonsterArtLoader.GetMonsterSprite(data);
                if (sprite != null)
                {
                    detailCardArt.sprite = sprite;
                    detailCardArt.color = Color.white;
                    detailCardArt.gameObject.SetActive(true);
                }
            }

            if (detailCostText != null) detailCostText.text = data.cost.ToString();
            if (detailAtkText != null) detailAtkText.text = data.atk.ToString();
            if (detailDefText != null) detailDefText.text = data.def.ToString();

            string typeName = data.cardType switch
            {
                CardType.Spell => "تعويذة سحرية 🔮",
                CardType.Environment => "بطاقة بيئة ومناخ 🌍",
                _ => "بطاقة وحش 🐉"
            };

            if (detailNameText != null)
            {
                detailNameText.text = ArabicFixer.Fix($"{data.name}");
                detailNameText.color = new Color(1f, 0.90f, 0.45f);
            }

            if (detailMetaText != null)
            {
                string elemEmoji = MonsterArtLoader.GetElementEmoji(data.element);
                detailMetaText.text = ArabicFixer.Fix($"{elemEmoji} {typeName} • تكلفة: {data.cost} مانا");
                detailMetaText.color = MonsterArtLoader.GetElementColor(data.element);
            }

            if (detailStatsText != null)
            {
                if (data.cardType == CardType.Monster)
                {
                    detailStatsText.text = ArabicFixer.Fix($"الهجوم الأساسي: {data.atk}  |  الدفاع الأساسي: {data.def}");
                }
                else if (data.cardType == CardType.Spell)
                {
                    detailStatsText.text = ArabicFixer.Fix($"نوع التعويذة: {data.spellType} (تأثير فوري)");
                }
                else
                {
                    detailStatsText.text = ArabicFixer.Fix($"مدة التأثير: {data.duration} جولات مناخية");
                }
            }

            if (detailDescText != null)
            {
                string desc = string.IsNullOrEmpty(data.description)
                    ? $"بطاقة استراتيجية قوية من نوع {typeName}."
                    : data.description;
                detailDescText.text = ArabicFixer.Fix($"📜 التأثير: {desc}");
            }

            if (detailStatusText != null)
            {
                var gm = GameManager.Instance;
                bool canAfford = (gm != null && gm.humanPlayer.currentPower >= data.cost);
                detailStatusText.text = ArabicFixer.Fix(canAfford ? "✨ يمكنك استخدام هذه البطاقة الآن (الباور كافٍ)." : "⚠️ الباور المتاح غير كافٍ لتفعيلها.");
                detailStatusText.color = canAfford ? new Color(0.2f, 1f, 0.5f) : new Color(1f, 0.45f, 0.45f);
            }
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

            // عرض تفاصيل البطاقة كاملة فوراً بنص واضح في لوحة التفاصيل
            ShowCardDetails(card);

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
                LogCombatMessage($"تم تحديد الوحش: [{card.name}] (تكلفة: {card.cost}) - اضغط على خانة لإنزاله، أو كليك يمين للتضحية به (+1 مانا).");
            }
            else if (card.cardType == CardType.Spell)
            {
                LogCombatMessage($"تم تحديد التعويذة: [{card.name}] (تكلفة: {card.cost}) - اضغط 'إطلاق التعويذة' أو اسحبها للأعلى، أو كليك يمين للتضحية.");
            }
            else if (card.cardType == CardType.Environment)
            {
                LogCombatMessage($"تم تحديد بطاقة المناخ: [{card.name}] (تكلفة: {card.cost}) - اضغط 'تنشيط المناخ' أو اسحبها للأعلى، أو كليك يمين للتضحية.");
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
            var gm = GameManager.Instance;
            if (gm == null) return;

            var targetPlayer = isPlayerSide ? gm.humanPlayer : gm.aiPlayer;
            var slot = (slotIndex >= 0 && slotIndex < targetPlayer.slots.Length) ? targetPlayer.slots[slotIndex] : null;

            // 1. إذا كانت الخانة بها وحش، اعرض بياناته كاملة بنص واضح فوراً!
            if (slot != null && slot.IsOccupied && slot.currentMonster != null)
            {
                ShowMonsterDetails(slot.currentMonster.data, slot.currentMonster, isPlayerSide);
                return;
            }

            // 2. إذا كانت الخانة فارغة، واللاعب محدد بطاقة وحش في يده وهو دوره:
            if (!isPlayerSide || selectedCardForAction == null || !gm.isPlayerTurn) return;

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
                    var summonedSlot = gm.humanPlayer.slots[slotIndex];
                    if (summonedSlot != null && summonedSlot.currentMonster != null)
                    {
                        ShowMonsterDetails(summonedSlot.currentMonster.data, summonedSlot.currentMonster, true);
                    }
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

        /// <summary>
        /// التضحية المباشرة بأي بطاقة في اليد عبر النقر عليها بزر الفأرة الأيمن (+1 سقف مانا و +1 باور)
        /// </summary>
        public void SacrificeCardDirectly(CardData card)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            if (!gm.isPlayerTurn)
            {
                LogCombatMessage("⚠️ لا يمكنك التضحية ببطاقة في دور الخصم!");
                return;
            }

            if (gm.humanPlayer.hasSacrificedThisTurn)
            {
                LogCombatMessage("⚠️ لقد قمت بالتضحية ببطاقة بالفعل هذا الدور (مسموح بتضحية واحدة فقط في كل دور).");
                return;
            }

            if (card == null) return;

            if (gm.humanPlayer.SacrificeCard(card))
            {
                LogCombatMessage($"🔥 تمت التضحية بـ [{card.name}] بنقرة زر الفأرة الأيمن! كسبت +1 سقف مانا و +1 باور.");
                DeselectHandCards();
                UpdateUI();
            }
        }

        private void OnSacrificeClicked()
        {
            if (selectedCardForAction != null)
            {
                SacrificeCardDirectly(selectedCardForAction);
            }
            else
            {
                LogCombatMessage("⚠️ اضغط كليك يمين على أي كارت في يدك للتضحية به!");
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
                    $"❤️ صحة بطلك المتبقية: {playerHp} / 200 HP\n" +
                    $"💀 صحة قائد الخصم: {enemyHp} / 200 HP\n" +
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

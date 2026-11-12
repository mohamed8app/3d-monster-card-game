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
        public Image playerAvatarImage;
        public Text enemyHpText;
        public Slider enemyHpSlider;
        public Image enemyAvatarImage;

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

        [Header("Environment Altar")]
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
            "Fire", "Water", "Ice", "Earth", "Electric", "Air",
            "Forest", "Light", "Dark", "Mecha", "Ghosts", "Arcane"
        };
        private int currentTestEnvIndex = -1;

        [Header("Combat Log")]
        public Text combatLogText;
        private List<string> logEntries = new List<string>();

        [Header("Game Over Modal")]
        public GameObject gameOverPanel;
        public Text gameOverTitleText;
        public Text gameOverSubtitleText;
        public Text gameOverStatsText;
        public Outline gameOverModalOutline;
        public Button restartGameButton;
        public Button gameOverQuitButton;

        [Header("Monster & Card Details Inspector")]
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
        private int selectedBoardSlotIndex = -1;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
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

            var gm = GameManager.Instance;
            if (gm != null && gm.humanPlayer != null && gm.humanPlayer.hand != null && gm.humanPlayer.hand.Count > 0)
            {
                ShowCardDetails(gm.humanPlayer.hand[0]);
            }
        }

        private void EnsureAtmosphereEngine()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                var boardTr = canvas.transform.Find("CenterBattleBoard");
                Transform targetRoot = boardTr != null ? boardTr : canvas.transform;
                var bgTr = targetRoot.Find("ArenaBackground");
                if (bgTr == null) bgTr = canvas.transform.Find("ArenaBackground");
                var bgImg = bgTr != null ? bgTr.GetComponent<Image>() : null;
                BattlefieldAtmosphereEngine.EnsureInstance(targetRoot, bgImg, envAtmosphereOverlay);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                OnQuitClicked();
            }
            else if (Input.GetKeyDown(KeyCode.F11))
            {
                ToggleFullScreen();
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                OnTestCycleEnvironmentClicked();
            }
            else if (Input.GetKeyDown(KeyCode.L))
            {
                OnTestCycleEnvLevelClicked();
            }

            // Safety check: if player hand has cards but UI container is empty, refresh immediately
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
        /// Cycles through all 12 elemental environments + default neutral war weather without consuming mana.
        /// </summary>
        public void OnTestCycleEnvironmentClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            currentTestEnvIndex++;
            if (currentTestEnvIndex >= AllTestElements.Length)
            {
                currentTestEnvIndex = -1;
                gm.activeEnvironment = null;
                LogCombatMessage("⚔️ [Weather Test]: Returned to Default War Weather (Neutral).");
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
                LogCombatMessage($"🌍 [Weather Test {currentTestEnvIndex + 1}/12]: Activated [{gm.activeEnvironment.name}] ({elem} - Level {currentLvl})!");
            }

            UpdateEnvironmentVisuals();
            UpdateUI();
        }

        /// <summary>
        /// Cycles the active environment level (Level 1 ★ -> 2 ★★ -> 3 ★★★).
        /// </summary>
        public void OnTestCycleEnvLevelClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            if (gm.activeEnvironment == null)
            {
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
            LogCombatMessage($"⭐ [Level Test]: [{gm.activeEnvironment.name}] is now Level {gm.activeEnvironment.level} (+{Mathf.RoundToInt(gm.activeEnvironment.buffPct * 100)}% ATK Buff)!");

            UpdateEnvironmentVisuals();
            UpdateUI();
        }

        public void ToggleFullScreen()
        {
            if (Screen.fullScreen)
            {
                Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            }
            else
            {
                GameBootstrapper.EnsureFullScreenDisplay();
            }
        }

        public void OnQuitClicked()
        {
            Debug.Log("🚪 Quit button pressed - closing game...");
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

            // 1. Update Leader HP
            if (playerHpText != null) playerHpText.text = $"❤️ {gm.humanPlayer.currentHp} / {gm.humanPlayer.maxHp} HP";
            if (playerHpSlider != null) playerHpSlider.value = (float)gm.humanPlayer.currentHp / gm.humanPlayer.maxHp;

            if (enemyHpText != null) enemyHpText.text = $"❤️ {gm.aiPlayer.currentHp} / {gm.aiPlayer.maxHp} HP";
            if (enemyHpSlider != null) enemyHpSlider.value = (float)gm.aiPlayer.currentHp / gm.aiPlayer.maxHp;

            // 2. Update Mana & Power (Unlimited Mana Cap ∞)
            if (playerPowerText != null)
            {
                string crystals = "";
                int power = gm.humanPlayer.currentPower;
                int maxCap = gm.humanPlayer.manaCapacity;
                int displayIcons = Mathf.Min(Mathf.Max(maxCap, power), 8);

                for (int i = 0; i < displayIcons; i++)
                {
                    crystals += (i < power) ? "💎" : "◇";
                }
                if (Mathf.Max(maxCap, power) > 8) crystals += "+";

                playerPowerText.text = $"[ {crystals} ]  Power: {power}  |  Mana: {maxCap} (∞)";
            }

            // 3. Turn Indicator
            if (turnInfoText != null)
            {
                turnInfoText.text = gm.isPlayerTurn
                    ? "⚔️ YOUR TURN (Summon, Cast Spells, or Attack)"
                    : "⏳ ENEMY TURN (Thinking...)";
            }

            // 4. Control Buttons
            if (attackButton != null) attackButton.interactable = gm.isPlayerTurn;
            if (endTurnButton != null) endTurnButton.interactable = gm.isPlayerTurn;
            if (sacrificeButton != null)
            {
                sacrificeButton.interactable = gm.isPlayerTurn && !gm.humanPlayer.hasSacrificedThisTurn && selectedCardForAction != null;
            }

            UpdatePlayCardButtonState();

            // 5. Update Board Slots
            UpdateSlots();

            // 6. Refresh Player Hand
            RefreshPlayerHand();

            // 7. Refresh Enemy Hand
            RefreshEnemyHand();

            // 8. Update Environment Altar
            UpdateEnvironmentVisuals();
        }

        private void UpdateSlots()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            if (selectedBoardSlotIndex >= 0 && selectedBoardSlotIndex < 6)
            {
                var selSlot = gm.humanPlayer.slots[selectedBoardSlotIndex];
                if (selSlot == null || !selSlot.IsOccupied)
                {
                    selectedBoardSlotIndex = -1;
                }
            }

            for (int i = 0; i < 6; i++)
            {
                var pSlot = gm.humanPlayer.slots[i];
                if (pSlot != null && pSlot.IsOccupied && i < playerSlotViews.Length && playerSlotViews[i] != null)
                {
                    playerSlotViews[i].DisplayMonster(pSlot.currentMonster.data, pSlot.currentMonster);
                    playerSlotViews[i].SetBoardSelected(i == selectedBoardSlotIndex);
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
                    envNameText.text = $"{env.name}";
                    envNameText.color = elemColor;
                }
                if (envLevelText != null)
                {
                    string stars = new string('★', env.level);
                    envLevelText.text = $"{stars} Level {env.level}";
                    envLevelText.color = new Color(1f, 0.9f, 0.4f);
                }
                if (envDurationText != null)
                {
                    envDurationText.text = $"⏳ Remaining: {env.turnsRemaining} Turns";
                }
                if (envBuffText != null)
                {
                    int buffPercent = Mathf.RoundToInt(env.buffPct * 100);
                    envBuffText.text = $"⬆️ {env.buffedElement}: +{buffPercent}% ATK";
                    envBuffText.color = new Color(0.2f, 1f, 0.45f);
                }
                if (envDebuffText != null)
                {
                    int debuffPercent = Mathf.RoundToInt(env.debuffPct * 100);
                    string debuffedStr = string.Join(", ", env.debuffedElements);
                    envDebuffText.text = $"⬇️ {debuffedStr}: -{debuffPercent}%";
                    envDebuffText.color = new Color(1f, 0.45f, 0.45f);
                }

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
                    envNameText.text = "Weather: Stable";
                    envNameText.color = new Color(0.85f, 0.9f, 1f);
                }
                if (envLevelText != null)
                {
                    envLevelText.text = "Natural (Neutral)";
                    envLevelText.color = new Color(0.65f, 0.75f, 0.85f);
                }
                if (envDurationText != null) envDurationText.text = "⏳ Permanent";
                if (envBuffText != null)
                {
                    envBuffText.text = "• No Active Buffs";
                    envBuffText.color = new Color(0.7f, 0.75f, 0.8f);
                }
                if (envDebuffText != null)
                {
                    envDebuffText.text = "• All Elements Balanced";
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
                if (selectedCardForAction != null && selectedCardForAction == card)
                {
                    draggable.SetSelected(true);
                }
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
                enemyHandCountText.text = $"🎴 Enemy Hand: {handCount} Hidden Cards";
            }

            for (int i = 0; i < handCount; i++)
            {
                CreateFaceDownCardUI(enemyHandContainer, i);
            }
        }

        private GameObject CreateFaceDownCardUI(Transform parent, int index)
        {
            var cardRootObj = GameBootstrapper.CreatePanel(parent, $"EnemyCardBack_{index}", new Color(0f, 0f, 0f, 0.005f),
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(138, 182));

            var layoutElem = cardRootObj.AddComponent<LayoutElement>();
            layoutElem.preferredWidth = 138;
            layoutElem.preferredHeight = 182;
            layoutElem.minWidth = 138;
            layoutElem.minHeight = 182;

            var cardObj = GameBootstrapper.CreatePanel(cardRootObj.transform, "VisualRoot", new Color(0.08f, 0.06f, 0.10f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(138, 182));
            var cardImg = cardObj.GetComponent<Image>();
            cardImg.raycastTarget = false;

            Sprite backSprite = MonsterArtLoader.GetCardBackSprite();
            if (backSprite != null)
            {
                cardImg.sprite = backSprite;
                cardImg.color = Color.white;
                cardImg.preserveAspect = false;
            }
            else
            {
                cardImg.color = new Color(0.12f, 0.08f, 0.16f, 1f);
                var glyph = GameBootstrapper.CreateText(cardObj.transform, "BackGlyph", "❖", 32,
                    new Color(0.95f, 0.80f, 0.35f, 0.6f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(80, 80));
                glyph.raycastTarget = false;
            }

            return cardRootObj;
        }

        private GameObject CreateDynamicCardUI(Transform parent, CardData card)
        {
            Color elemColor = MonsterArtLoader.GetElementColor(card.element);

            Color cardBgColor = card.cardType switch
            {
                CardType.Spell => new Color(0.12f, 0.08f, 0.20f, 0.98f),
                CardType.Environment => new Color(0.06f, 0.12f, 0.16f, 0.98f),
                _ => new Color(0.09f, 0.10f, 0.15f, 0.98f)
            };

            var cardRootObj = GameBootstrapper.CreatePanel(parent, $"Card_{card.name}", new Color(0f, 0f, 0f, 0.005f),
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(138, 182));

            var layoutElem = cardRootObj.AddComponent<LayoutElement>();
            layoutElem.preferredWidth = 138;
            layoutElem.preferredHeight = 182;
            layoutElem.minWidth = 138;
            layoutElem.minHeight = 182;

            var cardObj = GameBootstrapper.CreatePanel(cardRootObj.transform, "VisualRoot", cardBgColor,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(138, 182));
            cardObj.GetComponent<Image>().raycastTarget = false;

            var outline = cardObj.AddComponent<Outline>();
            outline.effectColor = (card.cardType == CardType.Spell)
                ? new Color(0.95f, 0.75f, 0.25f, 0.95f)
                : elemColor;
            outline.effectDistance = new Vector2(2, 2);
            if (card.cardType == CardType.Monster)
            {
                outline.enabled = false;
            }

            Sprite customArt = MonsterArtLoader.GetCardSprite(card);

            if (card.cardType == CardType.Monster)
            {
                var artObj = new GameObject("CardArt");
                artObj.transform.SetParent(cardObj.transform, false);
                var rectArt = artObj.AddComponent<RectTransform>();
                rectArt.anchorMin = Vector2.zero;
                rectArt.anchorMax = Vector2.one;
                rectArt.sizeDelta = Vector2.zero;
                rectArt.anchoredPosition = Vector2.zero;
                var artImg = artObj.AddComponent<Image>();
                if (customArt != null)
                {
                    artImg.sprite = customArt;
                    artImg.color = Color.white;
                }
                else
                {
                    artImg.sprite = null;
                    artImg.color = MonsterArtLoader.GetEnvironmentAtmosphereColor(card.element);
                }
                artImg.raycastTarget = false;

                if (customArt == null)
                {
                    var idCenterTxt = GameBootstrapper.CreateText(cardObj.transform, "MonsterIdCenter", card.name, 30,
                        elemColor, TextAnchor.MiddleCenter, new Vector2(0, 12), new Vector2(126, 60));
                    idCenterTxt.fontStyle = FontStyle.Bold;
                    idCenterTxt.raycastTarget = false;

                    string elemEmoji = MonsterArtLoader.GetElementEmoji(card.element);
                    var tierBadgeTxt = GameBootstrapper.CreateText(cardObj.transform, "TierBadge", $"{elemEmoji} T{Mathf.Max(1, card.tier_num)}", 13,
                        new Color(0.9f, 0.92f, 0.96f), TextAnchor.MiddleCenter, new Vector2(0, -24), new Vector2(120, 24));
                    tierBadgeTxt.fontStyle = FontStyle.Bold;
                    tierBadgeTxt.raycastTarget = false;
                }

                // Classic Hand-Crafted Frame Overlay
                var frameObj = new GameObject("FrameOverlay");
                frameObj.transform.SetParent(cardObj.transform, false);
                var rectFrame = frameObj.AddComponent<RectTransform>();
                rectFrame.anchorMin = Vector2.zero;
                rectFrame.anchorMax = Vector2.one;
                rectFrame.sizeDelta = Vector2.zero;
                rectFrame.anchoredPosition = Vector2.zero;
                var frameImg = frameObj.AddComponent<Image>();
                Sprite frameSprite = MonsterArtLoader.GetCardFrameOverlaySprite(card.element);
                if (frameSprite != null)
                {
                    frameImg.sprite = frameSprite;
                    frameImg.color = Color.white;
                    frameImg.preserveAspect = false;
                }
                frameImg.raycastTarget = false;

                // Top-Center Summon Cost Number (Inscribed inside top medallion socket)
                var txtCost = GameBootstrapper.CreateText(cardObj.transform, "CostText", card.cost.ToString(), 14,
                    new Color(1f, 0.96f, 0.72f), TextAnchor.MiddleCenter, new Vector2(0, -11f), new Vector2(28, 22));
                txtCost.rectTransform.anchorMin = new Vector2(0.5f, 1f);
                txtCost.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                txtCost.rectTransform.anchoredPosition = new Vector2(0, -11f);
                txtCost.fontStyle = FontStyle.Bold;
                txtCost.raycastTarget = false;
                GameBootstrapper.AddTextShadow(txtCost);

                // Bottom-Left ATK Number (Inscribed inside bottom-left socket)
                var txtAtk = GameBootstrapper.CreateText(cardObj.transform, "AtkText", card.atk.ToString(), 15,
                    Color.white, TextAnchor.MiddleCenter, new Vector2(17.5f, 20.5f), new Vector2(26, 22));
                txtAtk.rectTransform.anchorMin = new Vector2(0f, 0f);
                txtAtk.rectTransform.anchorMax = new Vector2(0f, 0f);
                txtAtk.rectTransform.anchoredPosition = new Vector2(17.5f, 20.5f);
                txtAtk.fontStyle = FontStyle.Bold;
                txtAtk.raycastTarget = false;
                GameBootstrapper.AddTextShadow(txtAtk);

                // Bottom-Right DEF Number (Inscribed inside bottom-right socket)
                var txtDef = GameBootstrapper.CreateText(cardObj.transform, "DefText", card.def.ToString(), 15,
                    Color.white, TextAnchor.MiddleCenter, new Vector2(-17.5f, 20.5f), new Vector2(26, 22));
                txtDef.rectTransform.anchorMin = new Vector2(1f, 0f);
                txtDef.rectTransform.anchorMax = new Vector2(1f, 0f);
                txtDef.rectTransform.anchoredPosition = new Vector2(-17.5f, 20.5f);
                txtDef.fontStyle = FontStyle.Bold;
                txtDef.raycastTarget = false;
                GameBootstrapper.AddTextShadow(txtDef);
            }
            else if (card.cardType == CardType.Spell)
            {
                if (customArt != null)
                {
                    var artObj = new GameObject("CardArt");
                    artObj.transform.SetParent(cardObj.transform, false);
                    var rectArt = artObj.AddComponent<RectTransform>();
                    rectArt.anchorMin = Vector2.zero;
                    rectArt.anchorMax = Vector2.one;
                    rectArt.sizeDelta = Vector2.zero;
                    rectArt.anchoredPosition = Vector2.zero;
                    var artImg = artObj.AddComponent<Image>();
                    artImg.sprite = customArt;
                    artImg.color = Color.white;
                    artImg.raycastTarget = false;
                }

                var costCircle = GameBootstrapper.CreateCirclePanel(cardObj.transform, "CostCircle", new Color(0.10f, 0.28f, 0.65f, 0.96f),
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18, -18), new Vector2(32, 32));
                var costOutline = costCircle.AddComponent<Outline>();
                costOutline.effectColor = new Color(0.95f, 0.75f, 0.35f, 1f);
                costOutline.effectDistance = new Vector2(1.5f, 1.5f);
                var txtCost = GameBootstrapper.CreateText(costCircle.transform, "CostText", card.cost.ToString(), 16,
                    new Color(1f, 0.95f, 0.70f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(32, 32));
                txtCost.fontStyle = FontStyle.Bold;
                txtCost.raycastTarget = false;

                if (customArt == null)
                {
                    var iconObj = GameBootstrapper.CreateText(cardObj.transform, "SpellIdCenter", card.name, 26,
                        new Color(1f, 0.9f, 0.35f), TextAnchor.MiddleCenter, new Vector2(0, 26), new Vector2(124, 42));
                    iconObj.fontStyle = FontStyle.Bold;
                    iconObj.raycastTarget = false;
                }

                var namePanel = GameBootstrapper.CreatePanel(cardObj.transform, "NamePlate", new Color(0.18f, 0.12f, 0.28f, 0.92f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(130, 22));
                var txtName = GameBootstrapper.CreateText(namePanel.transform, "NameText", $"{card.name} • T{Mathf.Max(1, card.tier_num)}", 12,
                    new Color(1f, 0.9f, 0.4f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(126, 22));
                txtName.fontStyle = FontStyle.Bold;
                txtName.raycastTarget = false;

                var descPanel = GameBootstrapper.CreatePanel(cardObj.transform, "DescPanel", new Color(0.06f, 0.05f, 0.10f, 0.85f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -44), new Vector2(130, 42));
                var txtDesc = GameBootstrapper.CreateText(descPanel.transform, "DescText", card.description, 11,
                    new Color(0.92f, 0.92f, 0.96f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(126, 40));
                txtDesc.fontStyle = FontStyle.Bold;
                txtDesc.raycastTarget = false;

                var actionBadge = GameBootstrapper.CreatePanel(cardObj.transform, "ActionBadge", new Color(0.45f, 0.20f, 0.65f, 0.95f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -74), new Vector2(124, 20));
                var txtAction = GameBootstrapper.CreateText(actionBadge.transform, "ActionText", string.IsNullOrEmpty(card.role) ? "🔮 Tactical Spell" : card.role, 10,
                    Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120, 18));
                txtAction.fontStyle = FontStyle.Bold;
                txtAction.raycastTarget = false;
            }
            else if (card.cardType == CardType.Environment)
            {
                if (customArt != null)
                {
                    var artObj = new GameObject("CardArt");
                    artObj.transform.SetParent(cardObj.transform, false);
                    var rectArt = artObj.AddComponent<RectTransform>();
                    rectArt.anchorMin = Vector2.zero;
                    rectArt.anchorMax = Vector2.one;
                    rectArt.sizeDelta = Vector2.zero;
                    rectArt.anchoredPosition = Vector2.zero;
                    var artImg = artObj.AddComponent<Image>();
                    artImg.sprite = customArt;
                    artImg.color = Color.white;
                    artImg.raycastTarget = false;
                }

                var costCircle = GameBootstrapper.CreateCirclePanel(cardObj.transform, "CostCircle", new Color(0.10f, 0.28f, 0.65f, 0.96f),
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18, -18), new Vector2(32, 32));
                var costOutline = costCircle.AddComponent<Outline>();
                costOutline.effectColor = elemColor;
                costOutline.effectDistance = new Vector2(1.5f, 1.5f);
                var txtCost = GameBootstrapper.CreateText(costCircle.transform, "CostText", card.cost.ToString(), 16,
                    new Color(1f, 0.95f, 0.70f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(32, 32));
                txtCost.fontStyle = FontStyle.Bold;
                txtCost.raycastTarget = false;

                if (customArt == null)
                {
                    string envEmoji = MonsterArtLoader.GetEnvironmentEmoji(card.element);
                    var iconObj = GameBootstrapper.CreateText(cardObj.transform, "EnvIcon", envEmoji, 38,
                        elemColor, TextAnchor.MiddleCenter, new Vector2(0, 22), new Vector2(110, 46));
                    iconObj.raycastTarget = false;
                }

                var namePanel = GameBootstrapper.CreatePanel(cardObj.transform, "NamePlate", new Color(0.08f, 0.14f, 0.18f, 0.92f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -16), new Vector2(130, 22));
                var txtName = GameBootstrapper.CreateText(namePanel.transform, "NameText", card.name, 13,
                    elemColor, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(126, 22));
                txtName.fontStyle = FontStyle.Bold;
                txtName.raycastTarget = false;

                var descPanel = GameBootstrapper.CreatePanel(cardObj.transform, "DescPanel", new Color(0.05f, 0.08f, 0.11f, 0.85f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -46), new Vector2(130, 40));
                var txtDesc = GameBootstrapper.CreateText(descPanel.transform, "DescText", card.description, 12,
                    new Color(0.9f, 0.95f, 0.95f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(126, 38));
                txtDesc.fontStyle = FontStyle.Bold;
                txtDesc.raycastTarget = false;

                var actionBadge = GameBootstrapper.CreatePanel(cardObj.transform, "ActionBadge", new Color(0.15f, 0.40f, 0.55f, 0.95f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -72), new Vector2(124, 22));
                var txtAction = GameBootstrapper.CreateText(actionBadge.transform, "ActionText", $"🌍 {card.duration} Turns", 12,
                    new Color(0.9f, 0.98f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120, 20));
                txtAction.fontStyle = FontStyle.Bold;
                txtAction.raycastTarget = false;
            }

            return cardRootObj;
        }

        private void UpdateDetailArtNumber(CardData data)
        {
            if (detailCardArt == null || data == null) return;
            Sprite customArt = MonsterArtLoader.GetInspectorSprite(data);
            detailCardArt.gameObject.SetActive(true);

            var numTr = detailCardArt.transform.Find("DetailBigNumber");
            if (customArt != null)
            {
                detailCardArt.sprite = customArt;
                detailCardArt.color = Color.white;
                if (numTr != null) numTr.gameObject.SetActive(false);
                return;
            }

            detailCardArt.sprite = null;
            detailCardArt.color = MonsterArtLoader.GetEnvironmentAtmosphereColor(data.element);

            Text numTxt;
            if (numTr == null)
            {
                numTxt = GameBootstrapper.CreateText(detailCardArt.transform, "DetailBigNumber", data.name, 46,
                    MonsterArtLoader.GetElementColor(data.element), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(240, 120));
                numTxt.fontStyle = FontStyle.Bold;
                numTxt.raycastTarget = false;
            }
            else
            {
                numTr.gameObject.SetActive(true);
                numTxt = numTr.GetComponent<Text>();
                if (numTxt != null)
                {
                    numTxt.text = data.name;
                    numTxt.color = MonsterArtLoader.GetElementColor(data.element);
                }
            }
        }

        public void ShowMonsterDetails(CardData data, MonsterInstance instance, bool isPlayer)
        {
            if (data == null) return;
            if (cardDetailsPanel != null) cardDetailsPanel.SetActive(true);

            UpdateDetailArtNumber(data);

            if (detailCostText != null)
            {
                detailCostText.text = data.cost.ToString();
            }

            if (detailNameText != null)
            {
                string sideEmoji = isPlayer ? "👑" : "💀";
                detailNameText.text = $"{sideEmoji} Card #{data.name}";
                detailNameText.color = isPlayer ? new Color(1f, 0.90f, 0.45f) : new Color(1f, 0.55f, 0.55f);
            }

            if (detailMetaText != null)
            {
                string elemEmoji = MonsterArtLoader.GetElementEmoji(data.element);
                string sideStr = isPlayer ? "Allied" : "Enemy";
                detailMetaText.text = $"{elemEmoji} {data.element} • {data.role} ({sideStr}) • Cost: {data.cost} Mana";
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
                if (envMod > 1.01f) buffTag = $" (Buffed +{Mathf.RoundToInt((envMod - 1f) * 100)}% ⬆️)";
                else if (envMod < 0.99f) buffTag = $" (Debuffed -{Mathf.RoundToInt((1f - envMod) * 100)}% ⬇️)";

                detailStatsText.text = $"ATK: {effAtk}{buffTag}  |  DEF: {curDef} / {maxDef}";
            }

            if (detailDescText != null)
            {
                string desc = string.IsNullOrEmpty(data.description)
                    ? $"Tactical {data.element} monster with high battlefield combat capabilities."
                    : data.description;
                detailDescText.text = $"📜 Ability: {desc}";
            }

            if (detailStatusText != null)
            {
                if (instance != null)
                {
                    if (isPlayer)
                    {
                        string atkStatus;
                        Color statusCol;
                        if (instance.frozenTurns > 0)
                        {
                            atkStatus = $"❄️ Frozen ({instance.frozenTurns}T)";
                            statusCol = new Color(0.55f, 0.90f, 1f);
                        }
                        else if (instance.canAttack)
                        {
                            atkStatus = "⚔️ Ready to Attack!";
                            statusCol = new Color(0.2f, 1f, 0.5f);
                        }
                        else if (gm != null && instance.summonedTurn == gm.currentTurn)
                        {
                            atkStatus = "💤 Summoned this turn";
                            statusCol = new Color(0.5f, 0.8f, 1f);
                        }
                        else
                        {
                            atkStatus = "⏳ Attacked this turn";
                            statusCol = new Color(0.9f, 0.7f, 0.3f);
                        }

                        if (instance.HasActiveSkill && CombatSystem.Instance != null && instance.currentSlot != null)
                        {
                            var abState = CombatSystem.Instance.GetAbilityState(gm.humanPlayer, gm.aiPlayer, instance.currentSlot.slotIndex, out int cost, out string _);
                            string abTag = abState switch
                            {
                                CombatSystem.AbilityReadyState.Ready => $"⚡ Aura Ready ({cost}⚡) — Click ⚡ Flash / 2x Click",
                                CombatSystem.AbilityReadyState.AlreadyUsed => "✓ Skill Used",
                                CombatSystem.AbilityReadyState.NotEnoughPower => $"⚡ Need {cost}⚡",
                                CombatSystem.AbilityReadyState.NoValidTarget => "🎯 No Target",
                                CombatSystem.AbilityReadyState.Silenced => "🔒 Silenced",
                                CombatSystem.AbilityReadyState.Frozen => "❄️ Frozen",
                                _ => $"⏳ Skill ({cost}⚡)"
                            };
                            detailStatusText.text = $"{atkStatus}\n{abTag}";
                            detailStatusText.color = (abState == CombatSystem.AbilityReadyState.Ready) ? new Color(0.35f, 1f, 0.75f) : statusCol;
                        }
                        else
                        {
                            detailStatusText.text = atkStatus;
                            detailStatusText.color = statusCol;
                        }
                    }
                    else
                    {
                        if (!instance.canAttack && gm != null && instance.summonedTurn == gm.currentTurn)
                        {
                            detailStatusText.text = "💤 Newly summoned enemy monster (Sleeping this turn).";
                            detailStatusText.color = new Color(0.7f, 0.7f, 0.9f);
                        }
                        else
                        {
                            detailStatusText.text = "🎯 Hostile target on the battlefield.";
                            detailStatusText.color = new Color(1f, 0.45f, 0.45f);
                        }
                    }
                }
                else
                {
                    detailStatusText.text = "🎴 In your hand — Ready to summon.";
                    detailStatusText.color = new Color(0.4f, 0.85f, 1f);
                }
            }
        }

        public void ShowCardDetails(CardData data)
        {
            if (data == null) return;
            if (cardDetailsPanel != null) cardDetailsPanel.SetActive(true);

            UpdateDetailArtNumber(data);

            if (detailCostText != null) detailCostText.text = data.cost.ToString();
            if (detailAtkText != null) detailAtkText.text = data.atk.ToString();
            if (detailDefText != null) detailDefText.text = data.def.ToString();

            string typeName = data.cardType switch
            {
                CardType.Spell => "Magic Spell 🔮",
                CardType.Environment => "Weather & Environment 🌍",
                _ => "Monster Card 🐉"
            };

            if (detailNameText != null)
            {
                detailNameText.text = $"Card #{data.name}";
                detailNameText.color = new Color(1f, 0.90f, 0.45f);
            }

            if (detailMetaText != null)
            {
                string elemEmoji = MonsterArtLoader.GetElementEmoji(data.element);
                detailMetaText.text = $"{elemEmoji} {data.element} • {typeName} • Cost: {data.cost} Mana";
                detailMetaText.color = MonsterArtLoader.GetElementColor(data.element);
            }

            if (detailStatsText != null)
            {
                if (data.cardType == CardType.Monster)
                {
                    detailStatsText.text = $"Base ATK: {data.atk}  |  Base DEF: {data.def}";
                }
                else if (data.cardType == CardType.Spell)
                {
                    detailStatsText.text = $"Spell Type: {data.spellType} (Instant Effect)";
                }
                else
                {
                    detailStatsText.text = $"Duration: {data.duration} Weather Turns";
                }
            }

            if (detailDescText != null)
            {
                string desc = string.IsNullOrEmpty(data.description)
                    ? $"Strategic {typeName} card."
                    : data.description;
                detailDescText.text = $"📜 Effect: {desc}";
            }

            if (detailStatusText != null)
            {
                var gm = GameManager.Instance;
                bool canAfford = (gm != null && gm.humanPlayer.currentPower >= data.cost);
                detailStatusText.text = canAfford
                    ? "✨ Ready to play (Enough Power available)."
                    : "⚠️ Insufficient Power to play this card.";
                detailStatusText.color = canAfford ? new Color(0.2f, 1f, 0.5f) : new Color(1f, 0.45f, 0.45f);
            }
        }

        public void OnCardSelectedInHand(CardData card)
        {
            if (selectedCardForAction == card)
            {
                DeselectHandCards();
                LogCombatMessage("Card deselected.");
                return;
            }

            ClearBoardSelection();
            selectedCardForAction = card;
            ShowCardDetails(card);

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
                HighlightAvailableSlots(true);
                LogCombatMessage($"Selected Monster: [{card.name}] (Cost: {card.cost}) — Click a slot to summon, or Right-Click to sacrifice (+1 Mana).");
            }
            else
            {
                HighlightAvailableSlots(false);
                if (card.cardType == CardType.Spell)
                {
                    LogCombatMessage($"Selected Spell: [{card.name}] (Cost: {card.cost}) — Click 'Cast Spell' or drag upward, or Right-Click to sacrifice.");
                }
                else if (card.cardType == CardType.Environment)
                {
                    LogCombatMessage($"Selected Weather: [{card.name}] (Cost: {card.cost}) — Click 'Activate Weather' or drag upward, or Right-Click to sacrifice.");
                }
            }

            UpdatePlayCardButtonState();

            if (sacrificeButton != null)
            {
                var gm = GameManager.Instance;
                sacrificeButton.interactable = gm != null && gm.isPlayerTurn && !gm.humanPlayer.hasSacrificedThisTurn;
            }
        }

        public void DeselectHandCards()
        {
            selectedCardForAction = null;
            HighlightAvailableSlots(false);

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

            UpdatePlayCardButtonState();

            if (sacrificeButton != null)
            {
                sacrificeButton.interactable = false;
            }
        }

        public void ClearBoardSelection()
        {
            selectedBoardSlotIndex = -1;
            if (playerSlotViews != null)
            {
                for (int i = 0; i < playerSlotViews.Length; i++)
                {
                    if (playerSlotViews[i] != null) playerSlotViews[i].SetBoardSelected(false);
                }
            }
            UpdatePlayCardButtonState();
        }

        private void UpdatePlayCardButtonState()
        {
            if (playCardButton == null) return;
            var gm = GameManager.Instance;
            var txt = playCardButton.GetComponentInChildren<Text>();

            if (selectedCardForAction != null)
            {
                bool canPlaySelected = gm != null && gm.isPlayerTurn &&
                    (selectedCardForAction.cardType == CardType.Spell || selectedCardForAction.cardType == CardType.Environment) &&
                    selectedCardForAction.cost <= gm.humanPlayer.currentPower;

                playCardButton.interactable = canPlaySelected;
                if (txt != null)
                {
                    if (selectedCardForAction.cardType == CardType.Spell)
                        txt.text = $"🔮 Cast Spell ({selectedCardForAction.cost}⚡)";
                    else if (selectedCardForAction.cardType == CardType.Environment)
                        txt.text = $"🌍 Activate Weather ({selectedCardForAction.cost}⚡)";
                    else
                        txt.text = "✨ Select Slot to Summon";
                }
                return;
            }

            if (selectedBoardSlotIndex >= 0 && gm != null && CombatSystem.Instance != null)
            {
                var state = CombatSystem.Instance.GetAbilityState(gm.humanPlayer, gm.aiPlayer, selectedBoardSlotIndex, out int cost, out string _);
                if (state != CombatSystem.AbilityReadyState.NoAbility)
                {
                    playCardButton.interactable = (state == CombatSystem.AbilityReadyState.Ready);
                    if (txt != null)
                    {
                        txt.text = state switch
                        {
                            CombatSystem.AbilityReadyState.Ready => $"⚡ Activate Ability ({cost}⚡)",
                            CombatSystem.AbilityReadyState.AlreadyUsed => "✓ Ability Used This Turn",
                            CombatSystem.AbilityReadyState.NotEnoughPower => $"⚡ Need {cost}⚡ Power",
                            CombatSystem.AbilityReadyState.NoValidTarget => "🎯 Needs Enemy Target",
                            CombatSystem.AbilityReadyState.Silenced => "🔒 Ability Silenced",
                            CombatSystem.AbilityReadyState.Frozen => "❄️ Monster Frozen",
                            _ => $"⏳ Ability ({cost}⚡)"
                        };
                    }
                    return;
                }
            }

            playCardButton.interactable = false;
            if (txt != null) txt.text = "✨ Activate Card / Spell";
        }

        public void HighlightAvailableSlots(bool highlight)
        {
            if (playerSlotViews == null) return;
            for (int i = 0; i < playerSlotViews.Length; i++)
            {
                var sv = playerSlotViews[i];
                if (sv != null)
                {
                    sv.SetSummonHighlight(highlight);
                }
            }
        }

        public void OnSlotClicked(int slotIndex, bool isPlayerSide)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            var targetPlayer = isPlayerSide ? gm.humanPlayer : gm.aiPlayer;
            var slot = (slotIndex >= 0 && slotIndex < targetPlayer.slots.Length) ? targetPlayer.slots[slotIndex] : null;

            // 1. If the slot is occupied by a monster, select & inspect it immediately
            if (slot != null && slot.IsOccupied && slot.currentMonster != null)
            {
                DeselectHandCards();
                selectedBoardSlotIndex = isPlayerSide ? slotIndex : -1;
                if (playerSlotViews != null)
                {
                    for (int i = 0; i < playerSlotViews.Length; i++)
                    {
                        if (playerSlotViews[i] != null) playerSlotViews[i].SetBoardSelected(i == selectedBoardSlotIndex);
                    }
                }
                ShowMonsterDetails(slot.currentMonster.data, slot.currentMonster, isPlayerSide);
                UpdatePlayCardButtonState();
                return;
            }

            // 2. If the slot is empty and the player has selected a monster card during their turn:
            if (!isPlayerSide || selectedCardForAction == null || !gm.isPlayerTurn) return;

            if (selectedCardForAction.cardType != CardType.Monster)
            {
                LogCombatMessage("⚠️ That card is not a monster! Spell and Weather cards do not occupy monster slots.");
                return;
            }

            var chosenCard = selectedCardForAction;
            if (gm.humanPlayer.SummonMonster(chosenCard, slotIndex, gm.currentTurn))
            {
                OnPlayerMonsterSummoned(chosenCard, slotIndex);
            }
            else
            {
                LogCombatMessage($"❌ Cannot summon monster to Slot {slotIndex} (Check Power & slot availability).");
            }
        }

        public void OnPlayerMonsterSummoned(CardData chosenCard, int slotIndex)
        {
            var gm = GameManager.Instance;
            if (gm == null || chosenCard == null) return;

            if (MonsterCardGame.Audio.SoundManager.Instance != null)
                MonsterCardGame.Audio.SoundManager.Instance.PlayCardPlay();

            LogCombatMessage($"✅ Summoned [{chosenCard.name}] to Slot {slotIndex}.");
            DeselectHandCards();

            var summonedSlot = (slotIndex >= 0 && slotIndex < gm.humanPlayer.slots.Length) ? gm.humanPlayer.slots[slotIndex] : null;
            var summonedMonster = (summonedSlot != null) ? summonedSlot.currentMonster : null;

            if (summonedMonster != null && summonedMonster.HasActiveSkill)
            {
                selectedBoardSlotIndex = slotIndex;
                if (CombatSystem.Instance != null)
                {
                    var abState = CombatSystem.Instance.GetAbilityState(gm.humanPlayer, gm.aiPlayer, slotIndex, out int cost, out string reason);
                    if (abState == CombatSystem.AbilityReadyState.Ready)
                    {
                        LogCombatMessage($"⚡ [{chosenCard.name}] Aura Awakened! Click its glowing ⚡ Flash Sigil (or Double-Click card) to unleash ({cost}⚡)!");
                    }
                    else
                    {
                        LogCombatMessage($"✨ [{chosenCard.name}] has Special Ability ({cost}⚡): {reason}");
                    }
                }
            }
            else
            {
                selectedBoardSlotIndex = -1;
            }

            UpdateUI();

            if (slotIndex >= 0 && slotIndex < playerSlotViews.Length && playerSlotViews[slotIndex] != null)
            {
                StartCoroutine(BattleAnimations.AnimateCardSummon(playerSlotViews[slotIndex].transform));
            }

            if (summonedMonster != null)
            {
                ShowMonsterDetails(summonedMonster.data, summonedMonster, true);
            }
        }

        public void OnSlotAbilityButtonClicked(int slotIndex)
        {
            DeselectHandCards();
            selectedBoardSlotIndex = slotIndex;
            if (playerSlotViews != null)
            {
                for (int i = 0; i < playerSlotViews.Length; i++)
                {
                    if (playerSlotViews[i] != null) playerSlotViews[i].SetBoardSelected(i == selectedBoardSlotIndex);
                }
            }
            TryActivateBoardAbility(slotIndex);
        }

        public void TryActivateBoardAbility(int slotIndex)
        {
            var gm = GameManager.Instance;
            if (gm == null || CombatSystem.Instance == null) return;

            var slot = (slotIndex >= 0 && slotIndex < gm.humanPlayer.slots.Length) ? gm.humanPlayer.slots[slotIndex] : null;
            var monster = (slot != null && slot.IsOccupied) ? slot.currentMonster : null;
            string actText = (monster != null && monster.HasActiveSkill) ? monster.actives[0] : "";

            if (!CombatSystem.Instance.ActivateMonsterAbility(gm.humanPlayer, gm.aiPlayer, slotIndex, out string msg))
            {
                LogCombatMessage(msg);
                if (monster != null)
                {
                    ShowMonsterDetails(monster.data, monster, true);
                }
                UpdatePlayCardButtonState();
                return;
            }

            if (MonsterCardGame.Audio.SoundManager.Instance != null)
                MonsterCardGame.Audio.SoundManager.Instance.PlayCardAttack();

            LogCombatMessage(msg);
            if (monster != null)
            {
                var canvas = GetComponentInParent<Canvas>();
                var sv = GetSlotView(slotIndex, true);
                if (canvas != null && sv != null)
                {
                    StartCoroutine(BattleAnimations.AnimateAbilityShockwave(canvas.transform, sv.transform, MonsterArtLoader.GetElementColor(monster.data.element)));
                }
                ShowAbilityBanner(monster.data, actText);
            }

            gm.CheckGameOver();
            UpdateUI();

            var refreshedSlot = (slotIndex >= 0 && slotIndex < gm.humanPlayer.slots.Length) ? gm.humanPlayer.slots[slotIndex] : null;
            if (refreshedSlot != null && refreshedSlot.IsOccupied && refreshedSlot.currentMonster != null)
            {
                ShowMonsterDetails(refreshedSlot.currentMonster.data, refreshedSlot.currentMonster, true);
            }
            else
            {
                ClearBoardSelection();
                UpdateUI();
            }
        }

        private void OnPlayCardClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn) return;

            if (selectedCardForAction != null)
            {
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
            else if (selectedBoardSlotIndex >= 0)
            {
                TryActivateBoardAbility(selectedBoardSlotIndex);
            }
        }

        /// <summary>
        /// Direct sacrifice of any card in hand via Right-Click (+1 Max Mana & +1 Power).
        /// </summary>
        public void SacrificeCardDirectly(CardData card)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            if (!gm.isPlayerTurn)
            {
                LogCombatMessage("⚠️ You cannot sacrifice cards during the enemy turn!");
                return;
            }

            if (gm.humanPlayer.hasSacrificedThisTurn)
            {
                LogCombatMessage("⚠️ You already sacrificed a card this turn (only 1 sacrifice allowed per turn).");
                return;
            }

            if (card == null) return;

            if (gm.humanPlayer.SacrificeCard(card))
            {
                MonsterCardGame.Audio.SoundManager.Instance.PlayCardSacrifice();
                LogCombatMessage($"🔥 Sacrificed [{card.name}] via Right-Click! Gained +1 Max Mana & +1 Power.");
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
                LogCombatMessage("⚠️ Right-Click any card in your hand to sacrifice it!");
            }
        }

        private void OnAttackClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            DeselectHandCards();
            ClearBoardSelection();
            LogCombatMessage("⚔️ Launching Combat Phase! Your monsters charge toward enemy lines...");
            gm.OnPlayerExecuteAttackButton();
            UpdateUI();
        }

        private void OnEndTurnClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn) return;

            MonsterCardGame.Audio.SoundManager.Instance.PlayTurnEnd();
            DeselectHandCards();
            ClearBoardSelection();
            gm.OnPlayerEndTurn();
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

        public void ShowAbilityBanner(CardData monsterData, string abilityText)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && monsterData != null)
            {
                StartCoroutine(BattleAnimations.AnimateAbilityCast(canvas, monsterData, abilityText));
            }
            if (BattlefieldAtmosphereEngine.Instance != null && monsterData != null)
            {
                Color c = MonsterArtLoader.GetElementColor(monsterData.element);
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
                    gameOverTitleText.text = "🏆 LEGENDARY VICTORY!";
                    gameOverTitleText.color = new Color(1f, 0.88f, 0.35f);
                }
                if (gameOverSubtitleText != null)
                {
                    gameOverSubtitleText.text = "You crushed the enemy warlord and claimed supreme dominion over the arena!";
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
                    gameOverTitleText.text = "💀 DEFEAT IN BATTLE!";
                    gameOverTitleText.color = new Color(0.95f, 0.3f, 0.3f);
                }
                if (gameOverSubtitleText != null)
                {
                    gameOverSubtitleText.text = "Your army fought bravely to the last breath... Regroup for the next battle!";
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
                    $"⏱️ Turns Elapsed: {turns} Tactical Turns\n" +
                    $"❤️ Your Remaining HP: {playerHp} / 500 HP\n" +
                    $"💀 Enemy Leader HP: {enemyHp} / 500 HP\n" +
                    $"⚔️ Monsters Summoned: {monstersSummoned}\n" +
                    $"🔮 Spells Cast: {spellsCast}\n" +
                    $"🌍 Final Weather: {envName}\n" +
                    $"💎 Final Mana Capacity: {manaCap} (Unlimited ∞)";

                gameOverStatsText.text = stats;
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

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
        public Text enemyPowerText;
        public Text turnInfoText;

        [Header("Opponent Hand Display (Face-Down Cards)")]
        public Transform enemyHandContainer;
        public Text enemyHandCountText;

        [Header("Controls")]
        public Button attackButton;
        public Button endTurnButton;
        public AstralHourglassControl hourglassControl;
        public Button sacrificeButton;
        public Button playCardButton;
        public Button quitButton;

        [Header("Containers")]
        public Transform playerHandContainer;
        public SlotView[] playerSlotViews = new SlotView[6];
        public SlotView[] enemySlotViews = new SlotView[6];

        [Header("Shared Environment Slot")]
        public GameObject envAltarPanel;
        public Button envSlotButton;
        public Image envCardArtImage;
        public Text envCardTitleText;
        public Text envCardCostText;
        public Text envIconText;
        public Text envNameText;
        public Text envOwnerText;
        public Text envLevelText;
        public Text envDurationText;
        public Text envBuffText;
        public Text envDebuffText;
        public Image envAtmosphereOverlay;
        public Button testCycleEnvButton;
        public Button testCycleLevelButton;

        [Header("Side Leader Slots")]
        public Button playerLeaderSlotButton;
        public Image playerLeaderCardArt;
        public GameObject playerLeaderEmptyGroup;
        public Text playerLeaderCostText;
        public Text playerLeaderNameText;
        public Text playerLeaderTribeText;
        public Text playerLeaderDescText;

        public Button enemyLeaderSlotButton;
        public Image enemyLeaderCardArt;
        public GameObject enemyLeaderEmptyGroup;
        public Text enemyLeaderCostText;
        public Text enemyLeaderNameText;
        public Text enemyLeaderDescText;

        private static readonly string[] AllTestElements = {
            "Fire", "Water", "Ice", "Earth", "Electric", "Air",
            "Nature", "Light", "Dark", "Arcane"
        };
        private int currentTestEnvIndex = -1;

        [Header("Combat Log")]
        public Text combatLogText;
        private List<string> logEntries = new List<string>();

        [Header("Card Flight Motion & Anchors")]
        public RectTransform cardFlightLayer;
        public RectTransform centerBoardRect;
        public RectTransform playerDeckAnchor;
        public RectTransform enemyDeckAnchor;
        public RectTransform playerPowerAnchor;
        public RectTransform enemyPowerAnchor;

        private readonly List<CardData> pendingPlayerDrawnCards = new List<CardData>();
        private readonly HashSet<CardData> inFlightPlayerDrawCards = new HashSet<CardData>();
        private int pendingEnemyDrawnCount = 0;
        private int inFlightEnemyDrawCount = 0;
        private readonly List<GameObject> activeFlightVisuals = new List<GameObject>();
        public int HandRenderGeneration { get; private set; } = 0;
        private DraggableCard activeDraggingCard = null;

        public void SetDraggingCard(DraggableCard card)
        {
            activeDraggingCard = card;
        }

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

            if (envSlotButton != null)
            {
                envSlotButton.onClick.RemoveAllListeners();
                envSlotButton.onClick.AddListener(OnSharedEnvSlotClicked);
            }
            if (playerLeaderSlotButton != null)
            {
                playerLeaderSlotButton.onClick.RemoveAllListeners();
                playerLeaderSlotButton.onClick.AddListener(OnPlayerLeaderSlotClicked);
            }
            if (enemyLeaderSlotButton != null)
            {
                enemyLeaderSlotButton.onClick.RemoveAllListeners();
                enemyLeaderSlotButton.onClick.AddListener(OnEnemyLeaderSlotClicked);
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
            UpdateLeaderSlotsUI();

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

            // 2. Update Turn Mana & Permanent Power (Unlimited Power ∞) for both Player & Enemy
            if (playerPowerText != null)
            {
                playerPowerText.text = $"{FormatManaPowerText(gm.humanPlayer.currentMana, gm.humanPlayer.power)}  |  Deck: {gm.humanPlayer.deck.Count}";
            }

            if (enemyPowerText != null && gm.aiPlayer != null)
            {
                enemyPowerText.text = FormatManaPowerText(gm.aiPlayer.currentMana, gm.aiPlayer.power);
            }

            // 3. Turn Indicator
            if (turnInfoText != null)
            {
                turnInfoText.text = gm.isPlayerTurn
                    ? $"TURN {gm.currentTurn} • YOUR MOVE"
                    : $"TURN {gm.currentTurn} • ENEMY TURN";
                turnInfoText.color = gm.isPlayerTurn
                    ? new Color(0.84f, 0.90f, 0.98f)
                    : new Color(0.92f, 0.68f, 0.68f);
            }

            // 4. Control Buttons & Astral Hourglass Turn Control
            if (attackButton != null) attackButton.interactable = gm.isPlayerTurn;
            if (endTurnButton != null) endTurnButton.interactable = gm.isPlayerTurn;
            if (hourglassControl != null) hourglassControl.SyncStateImmediate(gm.isPlayerTurn && gm.currentPhase == GamePhase.ActionPhase);
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

            // 8. Update Shared Environment & Side Leader Slots
            UpdateEnvironmentVisuals();
            UpdateLeaderSlotsUI();
        }

        private static string FormatManaPowerText(int mana, int permPower)
        {
            string crystals = "";
            int displayIcons = Mathf.Min(Mathf.Max(permPower, mana), 8);
            for (int i = 0; i < displayIcons; i++)
            {
                crystals += (i < mana) ? "💎" : "◇";
            }
            if (Mathf.Max(permPower, mana) > 8) crystals += "+";
            if (string.IsNullOrEmpty(crystals)) crystals = "◇";

            return $"[ {crystals} ]  Mana: {mana}  |  Power: {permPower} (∞)";
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

                if (envCardArtImage != null)
                {
                    Sprite cardArt = (gm.activeEnvironmentCard != null ? MonsterArtLoader.GetCardSprite(gm.activeEnvironmentCard) : null)
                                  ?? BattlefieldAtmosphereEngine.GetOrLoadGroundSprite(env.element, env.level);
                    envCardArtImage.sprite = cardArt;
                    envCardArtImage.color = (cardArt != null) ? Color.white : MonsterArtLoader.GetEnvironmentAtmosphereColor(env.element);
                }
                if (envCardTitleText != null)
                {
                    envCardTitleText.text = env.element.ToUpperInvariant();
                }
                if (envCardCostText != null)
                {
                    int costVal = gm.activeEnvironmentCard != null ? gm.activeEnvironmentCard.cost : env.cost;
                    envCardCostText.text = costVal > 0 ? costVal.ToString() : "E";
                }
                if (envOwnerText != null)
                {
                    string ownerStr = string.IsNullOrEmpty(gm.activeEnvironmentOwner) ? "Arena" : gm.activeEnvironmentOwner;
                    envOwnerText.text = $"Controlled by: {ownerStr}";
                    envOwnerText.color = ownerStr == "Enemy"
                        ? new Color(0.92f, 0.64f, 0.64f)
                        : new Color(0.74f, 0.84f, 0.94f);
                }

                if (envIconText != null) envIconText.text = envEmoji;
                if (envNameText != null)
                {
                    envNameText.text = $"{env.name}";
                    envNameText.color = Color.Lerp(elemColor, Color.white, 0.35f);
                }
                if (envLevelText != null)
                {
                    string stars = new string('★', env.level);
                    envLevelText.text = $"{stars} Level {env.level}";
                    envLevelText.color = new Color(0.78f, 0.86f, 0.96f);
                }
                if (envDurationText != null)
                {
                    envDurationText.text = $"Remaining: {env.turnsRemaining} Turns";
                }
                if (envBuffText != null)
                {
                    int buffPercent = Mathf.RoundToInt(env.buffPct * 100);
                    envBuffText.text = $"+{buffPercent}% {env.buffedElement}";
                    envBuffText.color = new Color(0.45f, 0.94f, 0.62f);
                }
                if (envDebuffText != null)
                {
                    int debuffPercent = Mathf.RoundToInt(env.debuffPct * 100);
                    string debuffedStr = string.Join(", ", env.debuffedElements);
                    envDebuffText.text = $"-{debuffPercent}% {debuffedStr}";
                    envDebuffText.color = new Color(0.96f, 0.56f, 0.56f);
                }

                EnsureAtmosphereEngine();
                if (BattlefieldAtmosphereEngine.Instance != null)
                {
                    BattlefieldAtmosphereEngine.Instance.ApplyEnvironmentProfile(env);
                }
            }
            else
            {
                if (envCardArtImage != null)
                {
                    Sprite neutralSprite = BattlefieldAtmosphereEngine.GetOrLoadGroundSprite("", 0);
                    envCardArtImage.sprite = neutralSprite;
                    envCardArtImage.color = neutralSprite != null
                        ? new Color(0.62f, 0.66f, 0.74f, 0.92f)
                        : new Color(0.10f, 0.12f, 0.16f, 1f);
                }
                if (envCardTitleText != null) envCardTitleText.text = "NEUTRAL ARENA";
                if (envCardCostText != null) envCardCostText.text = "-";
                if (envOwnerText != null)
                {
                    envOwnerText.text = "Controlled by: Arena (Neutral)";
                    envOwnerText.color = new Color(0.66f, 0.72f, 0.80f);
                }

                if (envIconText != null) envIconText.text = "☀️";
                if (envNameText != null)
                {
                    envNameText.text = "Stable Arena";
                    envNameText.color = new Color(0.84f, 0.88f, 0.95f);
                }
                if (envLevelText != null)
                {
                    envLevelText.text = "No Active Weather";
                    envLevelText.color = new Color(0.62f, 0.70f, 0.78f);
                }
                if (envDurationText != null) envDurationText.text = "Either player can override";
                if (envBuffText != null)
                {
                    envBuffText.text = "No Active Buffs";
                    envBuffText.color = new Color(0.68f, 0.74f, 0.80f);
                }
                if (envDebuffText != null)
                {
                    envDebuffText.text = "All Elements Balanced";
                    envDebuffText.color = new Color(0.68f, 0.74f, 0.80f);
                }

                EnsureAtmosphereEngine();
                if (BattlefieldAtmosphereEngine.Instance != null)
                {
                    BattlefieldAtmosphereEngine.Instance.ApplyEnvironmentProfile(null);
                }
            }
        }

        public void UpdateLeaderSlotsUI()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            UpdateSingleLeaderSlot(
                gm.humanPlayer != null ? gm.humanPlayer.activeLeader : null,
                true,
                playerLeaderCardArt,
                playerLeaderEmptyGroup,
                playerLeaderCostText,
                playerLeaderNameText,
                playerLeaderTribeText,
                playerLeaderDescText);

            UpdateSingleLeaderSlot(
                gm.aiPlayer != null ? gm.aiPlayer.activeLeader : null,
                false,
                enemyLeaderCardArt,
                enemyLeaderEmptyGroup,
                enemyLeaderCostText,
                enemyLeaderNameText,
                null,
                enemyLeaderDescText);
        }

        private void UpdateSingleLeaderSlot(
            CardData leader,
            bool isPlayer,
            Image artImg,
            GameObject emptyGroup,
            Text costTxt,
            Text nameTxt,
            Text tribeTxt,
            Text descTxt)
        {
            if (leader != null)
            {
                if (emptyGroup != null) emptyGroup.SetActive(false);
                if (artImg != null)
                {
                    artImg.gameObject.SetActive(true);
                    Sprite sp = MonsterArtLoader.GetCardSprite(leader);
                    artImg.sprite = sp;
                    artImg.color = sp != null ? Color.white : MonsterArtLoader.GetEnvironmentAtmosphereColor(leader.element);
                }
                if (costTxt != null)
                {
                    if (costTxt.transform.parent != null) costTxt.transform.parent.gameObject.SetActive(true);
                    costTxt.gameObject.SetActive(true);
                    costTxt.text = leader.cost.ToString();
                }
                if (nameTxt != null)
                {
                    nameTxt.text = $"Leader #{leader.name}";
                    nameTxt.color = isPlayer ? new Color(0.84f, 0.92f, 1f) : new Color(0.95f, 0.74f, 0.76f);
                }
                if (tribeTxt != null)
                {
                    string tPart = string.IsNullOrEmpty(leader.tribe) ? leader.element : $"{leader.element} • {leader.tribe}";
                    tribeTxt.text = tPart;
                }
                if (descTxt != null)
                {
                    descTxt.text = string.IsNullOrEmpty(leader.description)
                        ? "Active side commander passive aura."
                        : leader.description;
                }
            }
            else
            {
                if (emptyGroup != null) emptyGroup.SetActive(true);
                if (artImg != null)
                {
                    artImg.sprite = null;
                    artImg.gameObject.SetActive(false);
                }
                if (costTxt != null)
                {
                    costTxt.gameObject.SetActive(false);
                    if (costTxt.transform.parent != null) costTxt.transform.parent.gameObject.SetActive(false);
                }
                if (nameTxt != null)
                {
                    nameTxt.text = isPlayer ? "No Leader Summoned" : "No Enemy Leader";
                    nameTxt.color = isPlayer ? new Color(0.76f, 0.84f, 0.92f) : new Color(0.84f, 0.66f, 0.68f);
                }
                if (tribeTxt != null)
                {
                    tribeTxt.text = "Passive Side Aura Slot";
                }
                if (descTxt != null)
                {
                    descTxt.text = isPlayer
                        ? "Drag or play a Leader card into this side slot."
                        : "Side passive zone (Not targetable by lane attacks).";
                }
            }
        }

        private void OnSharedEnvSlotClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            if (selectedCardForAction != null && selectedCardForAction.cardType == CardType.Environment && gm.isPlayerTurn)
            {
                if (gm.PlayEnvironmentCard(gm.humanPlayer, selectedCardForAction))
                {
                    DeselectHandCards();
                    UpdateUI();
                }
                return;
            }

            CardData envToInspect = gm.activeEnvironmentCard ?? gm.activeEnvironment;
            if (envToInspect != null)
            {
                ShowCardDetails(envToInspect);
            }
        }

        private void OnPlayerLeaderSlotClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.humanPlayer == null) return;

            if (selectedCardForAction != null &&
                (selectedCardForAction.cardType == CardType.Leader || string.Equals(selectedCardForAction.card_type, "leader", StringComparison.OrdinalIgnoreCase)) &&
                gm.isPlayerTurn)
            {
                if (gm.PlayLeaderCard(gm.humanPlayer, selectedCardForAction))
                {
                    DeselectHandCards();
                    UpdateUI();
                }
                return;
            }

            if (gm.humanPlayer.activeLeader != null)
            {
                ShowCardDetails(gm.humanPlayer.activeLeader);
            }
        }

        private void OnEnemyLeaderSlotClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.aiPlayer == null) return;

            if (gm.aiPlayer.activeLeader != null)
            {
                ShowCardDetails(gm.aiPlayer.activeLeader);
            }
        }

        public void ClearFlightAnimations()
        {
            pendingPlayerDrawnCards.Clear();
            inFlightPlayerDrawCards.Clear();
            pendingEnemyDrawnCount = 0;
            inFlightEnemyDrawCount = 0;

            for (int i = 0; i < activeFlightVisuals.Count; i++)
            {
                if (activeFlightVisuals[i] != null) Destroy(activeFlightVisuals[i]);
            }
            activeFlightVisuals.Clear();

            if (playerSlotViews != null)
            {
                for (int i = 0; i < playerSlotViews.Length; i++) playerSlotViews[i]?.SetFlightHidden(false);
            }
            if (enemySlotViews != null)
            {
                for (int i = 0; i < enemySlotViews.Length; i++) enemySlotViews[i]?.SetFlightHidden(false);
            }
        }

        public void NotifyCardDrawn(bool isPlayerSide, CardData card)
        {
            if (isPlayerSide)
            {
                if (card != null)
                {
                    pendingPlayerDrawnCards.Add(card);
                    inFlightPlayerDrawCards.Add(card);
                }
            }
            else
            {
                pendingEnemyDrawnCount++;
                inFlightEnemyDrawCount++;
            }
        }

        private Vector3 GetDeckOriginWorldPos(bool isPlayerSide)
        {
            if (isPlayerSide)
            {
                if (playerDeckAnchor != null) return playerDeckAnchor.position;
                if (playerHandContainer != null) return playerHandContainer.position + new Vector3(-560f, 0f, 0f);
                return new Vector3(320f, 110f, 0f);
            }
            else
            {
                if (enemyDeckAnchor != null) return enemyDeckAnchor.position;
                if (enemyHandContainer != null) return enemyHandContainer.position + new Vector3(-560f, 0f, 0f);
                return new Vector3(320f, 970f, 0f);
            }
        }

        public Vector3 GetCenterArenaWorldPos()
        {
            if (centerBoardRect != null) return centerBoardRect.position;
            return new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        }

        public Vector3 GetEnvironmentTargetWorldPos()
        {
            if (envSlotButton != null) return envSlotButton.transform.position;
            if (envAltarPanel != null) return envAltarPanel.transform.position;
            return GetCenterArenaWorldPos() + new Vector3(680f, 0f, 0f);
        }

        public Vector3 GetPlayerLeaderTargetWorldPos()
        {
            if (playerLeaderSlotButton != null) return playerLeaderSlotButton.transform.position;
            return GetCenterArenaWorldPos() + new Vector3(680f, -340f, 0f);
        }

        public Vector3 GetPlayerHandCardWorldPos(CardData card)
        {
            if (playerHandContainer != null && card != null)
            {
                for (int i = 0; i < playerHandContainer.childCount; i++)
                {
                    var child = playerHandContainer.GetChild(i);
                    var drag = child.GetComponent<DraggableCard>();
                    if (drag != null && drag.cardData == card)
                    {
                        return child.position;
                    }
                }
            }
            return playerHandContainer != null ? playerHandContainer.position : GetCenterArenaWorldPos() + new Vector3(0f, -380f, 0f);
        }

        private Vector3 ConsumeTopEnemyHandCardPos()
        {
            if (enemyHandContainer != null)
            {
                for (int i = enemyHandContainer.childCount - 1; i >= 0; i--)
                {
                    var child = enemyHandContainer.GetChild(i);
                    var cg = child.GetComponent<CanvasGroup>();
                    if (cg == null || cg.alpha > 0.5f)
                    {
                        Vector3 pos = child.position;
                        if (cg == null) cg = child.gameObject.AddComponent<CanvasGroup>();
                        cg.alpha = 0f;
                        return pos;
                    }
                }
            }
            return GetDeckOriginWorldPos(false);
        }

        private GameObject CreateFlyingCardVisual(CardData card, bool includeCardBackCover, out GameObject backCoverObj)
        {
            backCoverObj = null;
            Transform layer = (cardFlightLayer != null) ? cardFlightLayer : transform;
            var cardGo = CreateDynamicCardUI(layer, card);
            var rect = cardGo.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(138f, 182f);
            }
            var cg = cardGo.GetComponent<CanvasGroup>() ?? cardGo.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;
            foreach (var g in cardGo.GetComponentsInChildren<Graphic>(true))
            {
                if (g != null) g.raycastTarget = false;
            }

            if (includeCardBackCover)
            {
                backCoverObj = CreateFaceDownCardUI(cardGo.transform, -1);
                backCoverObj.name = "CardBackCover";
                var bRect = backCoverObj.GetComponent<RectTransform>();
                if (bRect != null)
                {
                    bRect.anchorMin = new Vector2(0.5f, 0.5f);
                    bRect.anchorMax = new Vector2(0.5f, 0.5f);
                    bRect.pivot = new Vector2(0.5f, 0.5f);
                    bRect.anchoredPosition = Vector2.zero;
                    bRect.sizeDelta = new Vector2(138f, 182f);
                }
                foreach (var g in backCoverObj.GetComponentsInChildren<Graphic>(true))
                {
                    if (g != null) g.raycastTarget = false;
                }
            }

            activeFlightVisuals.Add(cardGo);
            return cardGo;
        }

        private void RefreshPlayerHand()
        {
            var gm = GameManager.Instance;
            if (gm == null || playerHandContainer == null) return;

            HandRenderGeneration++;

            if (activeDraggingCard != null)
            {
                if (!gm.isPlayerTurn || gm.currentPhase != GamePhase.ActionPhase
                    || gm.humanPlayer.hand == null || !gm.humanPlayer.hand.Contains(activeDraggingCard.cardData))
                {
                    Destroy(activeDraggingCard.gameObject);
                    activeDraggingCard = null;
                }
            }

            for (int c = playerHandContainer.childCount - 1; c >= 0; c--)
            {
                var child = playerHandContainer.GetChild(c);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            var layout = playerHandContainer.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                int handCount = gm.humanPlayer.hand != null ? gm.humanPlayer.hand.Count : 0;
                layout.spacing = handCount >= 8 ? 10f : (handCount >= 7 ? 18f : 30f);
            }

            var mainCanvas = GetComponentInParent<Canvas>();
            var createdItems = new List<GameObject>();
            var createdCards = new List<CardData>();

            foreach (var card in gm.humanPlayer.hand)
            {
                var cardObj = CreateDynamicCardUI(playerHandContainer, card);
                var cg = cardObj.GetComponent<CanvasGroup>() ?? cardObj.AddComponent<CanvasGroup>();
                if (inFlightPlayerDrawCards.Contains(card))
                {
                    cg.alpha = 0f;
                }
                var draggable = cardObj.AddComponent<DraggableCard>();
                draggable.Setup(card, mainCanvas);
                if (selectedCardForAction != null && selectedCardForAction == card)
                {
                    draggable.SetSelected(true);
                }
                createdItems.Add(cardObj);
                createdCards.Add(card);
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(playerHandContainer as RectTransform);

            if (pendingPlayerDrawnCards.Count > 0)
            {
                var claimedIndices = new HashSet<int>();
                for (int d = 0; d < pendingPlayerDrawnCards.Count; d++)
                {
                    var drawnCard = pendingPlayerDrawnCards[d];
                    GameObject targetSeat = null;
                    for (int k = createdCards.Count - 1; k >= 0; k--)
                    {
                        if (!claimedIndices.Contains(k) && createdCards[k] == drawnCard)
                        {
                            claimedIndices.Add(k);
                            targetSeat = createdItems[k];
                            break;
                        }
                    }

                    Vector3 fallbackPos = targetSeat != null ? targetSeat.transform.position : playerHandContainer.position;
                    StartCoroutine(AnimatePlayerDrawCardRoutine(drawnCard, targetSeat, fallbackPos, d * 0.08f));
                }
                pendingPlayerDrawnCards.Clear();
            }
        }

        private System.Collections.IEnumerator AnimatePlayerDrawCardRoutine(CardData card, GameObject targetSeat, Vector3 fallbackPos, float delay)
        {
            var flyingVisual = CreateFlyingCardVisual(card, false, out _);
            Vector3 startPos = GetDeckOriginWorldPos(true);

            yield return BattleAnimations.AnimateDrawFlight(
                flyingVisual,
                startPos,
                targetSeat != null ? targetSeat.transform : null,
                fallbackPos,
                0.42f,
                delay,
                -8f,
                () =>
                {
                    inFlightPlayerDrawCards.Remove(card);
                    if (targetSeat != null)
                    {
                        var cg = targetSeat.GetComponent<CanvasGroup>();
                        if (cg != null) cg.alpha = 1f;
                    }
                    else if (playerHandContainer != null)
                    {
                        for (int i = 0; i < playerHandContainer.childCount; i++)
                        {
                            var child = playerHandContainer.GetChild(i);
                            var drag = child.GetComponent<DraggableCard>();
                            if (drag != null && drag.cardData == card)
                            {
                                var cg = child.GetComponent<CanvasGroup>();
                                if (cg != null) cg.alpha = 1f;
                            }
                        }
                    }
                });
        }

        private void RefreshEnemyHand()
        {
            var gm = GameManager.Instance;
            if (gm == null || enemyHandContainer == null) return;

            for (int c = enemyHandContainer.childCount - 1; c >= 0; c--)
            {
                var child = enemyHandContainer.GetChild(c);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            int handCount = gm.aiPlayer.hand.Count;
            if (enemyHandCountText != null)
            {
                enemyHandCountText.text = $"🎴 Hand: {handCount}  |  Deck: {gm.aiPlayer.deck.Count}";
            }

            var createdBacks = new List<GameObject>();
            for (int i = 0; i < handCount; i++)
            {
                var backGo = CreateFaceDownCardUI(enemyHandContainer, i);
                backGo.GetComponent<CanvasGroup>(); // Ensure ready if added below
                createdBacks.Add(backGo);
            }

            inFlightEnemyDrawCount = Mathf.Clamp(inFlightEnemyDrawCount, 0, handCount);
            int firstHiddenIdx = handCount - inFlightEnemyDrawCount;
            for (int i = firstHiddenIdx; i < handCount; i++)
            {
                var cg = createdBacks[i].GetComponent<CanvasGroup>() ?? createdBacks[i].AddComponent<CanvasGroup>();
                cg.alpha = 0f;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(enemyHandContainer as RectTransform);

            if (pendingEnemyDrawnCount > 0)
            {
                int toAnimate = Mathf.Min(pendingEnemyDrawnCount, handCount);
                pendingEnemyDrawnCount = 0;
                int startIdx = handCount - toAnimate;
                for (int k = 0; k < toAnimate; k++)
                {
                    var seat = createdBacks[startIdx + k];
                    Vector3 fallbackPos = seat.transform.position;
                    StartCoroutine(AnimateEnemyDrawCardRoutine(seat, fallbackPos, k * 0.08f));
                }
            }
        }

        private System.Collections.IEnumerator AnimateEnemyDrawCardRoutine(GameObject targetSeat, Vector3 fallbackPos, float delay)
        {
            Transform layer = (cardFlightLayer != null) ? cardFlightLayer : transform;
            var flyingVisual = CreateFaceDownCardUI(layer, -1);
            var rect = flyingVisual.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(138f, 182f);
            }
            foreach (var g in flyingVisual.GetComponentsInChildren<Graphic>(true))
            {
                if (g != null) g.raycastTarget = false;
            }
            activeFlightVisuals.Add(flyingVisual);

            Vector3 startPos = GetDeckOriginWorldPos(false);

            yield return BattleAnimations.AnimateDrawFlight(
                flyingVisual,
                startPos,
                targetSeat != null ? targetSeat.transform : null,
                fallbackPos,
                0.42f,
                delay,
                8f,
                () =>
                {
                    inFlightEnemyDrawCount = Mathf.Max(0, inFlightEnemyDrawCount - 1);
                    if (targetSeat != null)
                    {
                        var cg = targetSeat.GetComponent<CanvasGroup>();
                        if (cg != null) cg.alpha = 1f;
                    }
                    else if (enemyHandContainer != null)
                    {
                        int visibleTargetCount = Mathf.Max(0, enemyHandContainer.childCount - inFlightEnemyDrawCount);
                        for (int i = 0; i < visibleTargetCount; i++)
                        {
                            var cg = enemyHandContainer.GetChild(i).GetComponent<CanvasGroup>();
                            if (cg != null) cg.alpha = 1f;
                        }
                    }
                });
        }

        public System.Collections.IEnumerator AnimateEnemyCardSacrificeRoutine()
        {
            Vector3 startPos = ConsumeTopEnemyHandCardPos();
            Vector3 targetPos = (enemyPowerAnchor != null) ? enemyPowerAnchor.position : GetDeckOriginWorldPos(false);
            Transform layer = (cardFlightLayer != null) ? cardFlightLayer : transform;

            var visual = CreateFaceDownCardUI(layer, -1);
            var rect = visual.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(138f, 182f);
            }
            foreach (var g in visual.GetComponentsInChildren<Graphic>(true))
            {
                if (g != null) g.raycastTarget = false;
            }
            activeFlightVisuals.Add(visual);

            yield return BattleAnimations.AnimateBoardCardFlight(
                visual, startPos, targetPos, 0.38f, 0.94f, 0.45f, -20f, null, 0.12f, null);
        }

        public System.Collections.IEnumerator AnimateEnemyCardPlayRoutine(CardData card, Vector3 targetWorldPos, float duration = 0.50f, float fadeOutAfter = 0.08f, float endScale = 0.98f)
        {
            if (card == null) yield break;

            Vector3 startPos = ConsumeTopEnemyHandCardPos();
            var visual = CreateFlyingCardVisual(card, true, out GameObject backCover);
            yield return BattleAnimations.AnimateBoardCardFlight(
                visual, startPos, targetWorldPos, duration, 0.90f, endScale, -32f, backCover, fadeOutAfter, null);
        }

        public void LaunchPlayerUtilityCardFlight(CardData card, Vector3 startWorldPos)
        {
            if (card == null) return;

            Vector3 targetPos;
            float endScale = 1.0f;
            float fadeAfter = 0.14f;
            float dur = 0.40f;

            if (card.cardType == CardType.Spell)
            {
                targetPos = GetCenterArenaWorldPos();
                endScale = 1.10f;
                fadeAfter = 0.20f;
            }
            else if (card.cardType == CardType.Environment)
            {
                targetPos = GetEnvironmentTargetWorldPos();
                endScale = 0.85f;
                fadeAfter = 0.12f;
                dur = 0.42f;
            }
            else if (card.cardType == CardType.Leader)
            {
                targetPos = GetPlayerLeaderTargetWorldPos();
                endScale = 0.85f;
                fadeAfter = 0.12f;
                dur = 0.42f;
            }
            else
            {
                return;
            }

            var visual = CreateFlyingCardVisual(card, false, out _);
            StartCoroutine(BattleAnimations.AnimateBoardCardFlight(
                visual, startWorldPos, targetPos, dur, 1.0f, endScale, 36f, null, fadeAfter, null));
        }

        public void LaunchSacrificeFlight(CardData card, Vector3 startWorldPos)
        {
            if (card == null) return;
            Vector3 targetPos = (playerPowerAnchor != null) ? playerPowerAnchor.position : GetDeckOriginWorldPos(true);
            var visual = CreateFlyingCardVisual(card, false, out _);
            StartCoroutine(BattleAnimations.AnimateBoardCardFlight(
                visual, startWorldPos, targetPos, 0.36f, 0.96f, 0.45f, 24f, null, 0.12f, null));
        }

        private System.Collections.IEnumerator LaunchMonsterLandingFlight(CardData card, int slotIndex, Vector3 startWorldPos, float duration)
        {
            var slotView = (playerSlotViews != null && slotIndex >= 0 && slotIndex < playerSlotViews.Length)
                ? playerSlotViews[slotIndex]
                : null;
            Vector3 targetPos = (slotView != null) ? slotView.transform.position : GetCenterArenaWorldPos();

            var visual = CreateFlyingCardVisual(card, false, out _);
            yield return BattleAnimations.AnimateBoardCardFlight(
                visual,
                startWorldPos,
                targetPos,
                duration,
                0.98f,
                1.02f,
                32f,
                null,
                0f,
                () =>
                {
                    if (slotView != null)
                    {
                        slotView.SetFlightHidden(false);
                        StartCoroutine(BattleAnimations.AnimateCardSettle(slotView.transform, 0.16f));
                    }
                });
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
                    new Color(0.65f, 0.70f, 0.78f, 0.5f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(80, 80));
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
                CardType.Leader => new Color(0.10f, 0.12f, 0.18f, 0.98f),
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
                ? new Color(0.45f, 0.36f, 0.62f, 0.90f)
                : (card.cardType == CardType.Leader ? new Color(0.48f, 0.62f, 0.78f, 0.92f) : elemColor);
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
                    if (MonsterArtLoader.HasStandby25DAssets(card))
                    {
                        var handStandby = artObj.AddComponent<MonsterStandbyAnimator>();
                        handStandby.Configure(card, false);
                    }
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
                    string tribeEmoji = MonsterArtLoader.GetTribeEmoji(card.tribe);
                    var tierBadgeTxt = GameBootstrapper.CreateText(cardObj.transform, "TierBadge", $"{elemEmoji}{tribeEmoji} T{Mathf.Max(1, card.tier_num)}", 13,
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
                    new Color(0.92f, 0.95f, 1f), TextAnchor.MiddleCenter, new Vector2(0, -11f), new Vector2(28, 22));
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
                Sprite scrollSprite = MonsterArtLoader.GetSpellScrollSprite();
                var cardImg = cardObj.GetComponent<Image>();

                if (scrollSprite != null && cardImg != null)
                {
                    cardImg.sprite = scrollSprite;
                    cardImg.color = Color.white;
                    cardImg.preserveAspect = false;
                    outline.enabled = false;

                    // Top-Center Cost Number inside the Sapphire Scroll Medallion
                    var txtCost = GameBootstrapper.CreateText(cardObj.transform, "CostText", card.cost.ToString(), 14,
                        new Color(0.92f, 0.95f, 1f), TextAnchor.MiddleCenter, new Vector2(0f, -9.5f), new Vector2(28f, 22f));
                    txtCost.rectTransform.anchorMin = new Vector2(0.5f, 1f);
                    txtCost.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                    txtCost.rectTransform.anchoredPosition = new Vector2(0f, -9.5f);
                    txtCost.fontStyle = FontStyle.Bold;
                    txtCost.raycastTarget = false;
                    GameBootstrapper.AddTextShadow(txtCost);

                    // Full-parchment antique grimoire spell manuscript filling the open scroll
                    if (customArt != null)
                    {
                        var artObj = new GameObject("CardArt");
                        artObj.transform.SetParent(cardObj.transform, false);
                        var rectArt = artObj.AddComponent<RectTransform>();
                        rectArt.anchorMin = new Vector2(0.5f, 0.5f);
                        rectArt.anchorMax = new Vector2(0.5f, 0.5f);
                        rectArt.sizeDelta = new Vector2(94f, 114f);
                        rectArt.anchoredPosition = new Vector2(0f, 4f);
                        var artImg = artObj.AddComponent<Image>();
                        artImg.sprite = customArt;
                        artImg.preserveAspect = true;
                        artImg.color = Color.white;
                        artImg.raycastTarget = false;
                    }
                    else
                    {
                        var runeTxt = GameBootstrapper.CreateText(cardObj.transform, "SpellIdCenter", $"🔮\n#{card.name}", 20,
                            new Color(0.24f, 0.14f, 0.34f), TextAnchor.MiddleCenter, new Vector2(0f, 10f), new Vector2(94f, 64f));
                        runeTxt.fontStyle = FontStyle.Bold;
                        runeTxt.raycastTarget = false;
                    }

                    // Clean antique sepia ink inscription tucked cleanly above lower wood roller
                    var txtName = GameBootstrapper.CreateText(cardObj.transform, "NameText", $"#{card.name} • T{Mathf.Max(1, card.tier_num)}", 10,
                        new Color(0.20f, 0.10f, 0.04f), TextAnchor.MiddleCenter, new Vector2(0f, -57f), new Vector2(94f, 13f));
                    txtName.fontStyle = FontStyle.Bold;
                    txtName.raycastTarget = false;

                    string shortRole = string.IsNullOrEmpty(card.role)
                        ? "Tactical Spell"
                        : card.role.Replace(" Spell", "").Trim();
                    var txtAction = GameBootstrapper.CreateText(cardObj.transform, "ActionText", shortRole, 9,
                        new Color(0.32f, 0.16f, 0.07f), TextAnchor.MiddleCenter, new Vector2(0f, -65.5f), new Vector2(94f, 12f));
                    txtAction.fontStyle = FontStyle.Bold;
                    txtAction.raycastTarget = false;
                }
                else
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
                    costOutline.effectColor = new Color(0.45f, 0.56f, 0.72f, 1f);
                    costOutline.effectDistance = new Vector2(1.5f, 1.5f);
                    var txtCost = GameBootstrapper.CreateText(costCircle.transform, "CostText", card.cost.ToString(), 16,
                        new Color(0.92f, 0.95f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(32, 32));
                    txtCost.fontStyle = FontStyle.Bold;
                    txtCost.raycastTarget = false;

                    if (customArt == null)
                    {
                        var iconObj = GameBootstrapper.CreateText(cardObj.transform, "SpellIdCenter", card.name, 26,
                            new Color(0.84f, 0.88f, 0.96f), TextAnchor.MiddleCenter, new Vector2(0, 26), new Vector2(124, 42));
                        iconObj.fontStyle = FontStyle.Bold;
                        iconObj.raycastTarget = false;
                    }

                    var namePanel = GameBootstrapper.CreatePanel(cardObj.transform, "NamePlate", new Color(0.18f, 0.12f, 0.28f, 0.92f),
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(130, 22));
                    var txtName = GameBootstrapper.CreateText(namePanel.transform, "NameText", $"{card.name} • T{Mathf.Max(1, card.tier_num)}", 12,
                        new Color(0.88f, 0.92f, 0.98f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(126, 22));
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
            }
            else if (card.cardType == CardType.Environment)
            {
                Sprite envArt = customArt ?? BattlefieldAtmosphereEngine.GetOrLoadGroundSprite(card.element, 1);
                if (envArt != null)
                {
                    var artObj = new GameObject("CardArt");
                    artObj.transform.SetParent(cardObj.transform, false);
                    var rectArt = artObj.AddComponent<RectTransform>();
                    rectArt.anchorMin = Vector2.zero;
                    rectArt.anchorMax = Vector2.one;
                    rectArt.sizeDelta = Vector2.zero;
                    rectArt.anchoredPosition = Vector2.zero;
                    var artImg = artObj.AddComponent<Image>();
                    artImg.sprite = envArt;
                    artImg.color = Color.white;
                    artImg.raycastTarget = false;
                }

                var costCircle = GameBootstrapper.CreateCirclePanel(cardObj.transform, "CostCircle", new Color(0.10f, 0.28f, 0.65f, 0.96f),
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18, -18), new Vector2(32, 32));
                var costOutline = costCircle.AddComponent<Outline>();
                costOutline.effectColor = elemColor;
                costOutline.effectDistance = new Vector2(1.5f, 1.5f);
                var txtCost = GameBootstrapper.CreateText(costCircle.transform, "CostText", card.cost.ToString(), 16,
                    new Color(0.92f, 0.95f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(32, 32));
                txtCost.fontStyle = FontStyle.Bold;
                txtCost.raycastTarget = false;

                if (envArt == null)
                {
                    string envEmoji = MonsterArtLoader.GetEnvironmentEmoji(card.element);
                    var iconObj = GameBootstrapper.CreateText(cardObj.transform, "EnvIcon", envEmoji, 38,
                        elemColor, TextAnchor.MiddleCenter, new Vector2(0, 22), new Vector2(110, 46));
                    iconObj.raycastTarget = false;
                }

                var namePanel = GameBootstrapper.CreatePanel(cardObj.transform, "NamePlate", new Color(0.08f, 0.14f, 0.18f, 0.92f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -16), new Vector2(130, 22));
                var txtName = GameBootstrapper.CreateText(namePanel.transform, "NameText", card.name, 13,
                    Color.Lerp(elemColor, Color.white, 0.35f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(126, 22));
                txtName.fontStyle = FontStyle.Bold;
                txtName.raycastTarget = false;

                var descPanel = GameBootstrapper.CreatePanel(cardObj.transform, "DescPanel", new Color(0.05f, 0.08f, 0.11f, 0.85f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -46), new Vector2(130, 40));
                var txtDesc = GameBootstrapper.CreateText(descPanel.transform, "DescText", card.description, 12,
                    new Color(0.9f, 0.95f, 0.95f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(126, 38));
                txtDesc.fontStyle = FontStyle.Bold;
                txtDesc.raycastTarget = false;

                var actionBadge = GameBootstrapper.CreatePanel(cardObj.transform, "ActionBadge", new Color(0.15f, 0.34f, 0.48f, 0.95f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -72), new Vector2(124, 22));
                var txtAction = GameBootstrapper.CreateText(actionBadge.transform, "ActionText", $"Shared Arena • {card.duration}T", 11,
                    new Color(0.9f, 0.98f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120, 20));
                txtAction.fontStyle = FontStyle.Bold;
                txtAction.raycastTarget = false;
            }
            else if (card.cardType == CardType.Leader)
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

                var tagBadge = GameBootstrapper.CreatePanel(cardObj.transform, "LeaderBadge", new Color(0.14f, 0.22f, 0.34f, 0.95f),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36, -16), new Vector2(58, 18));
                var txtTag = GameBootstrapper.CreateText(tagBadge.transform, "TagText", "LEADER", 9,
                    new Color(0.86f, 0.94f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(54, 16));
                txtTag.fontStyle = FontStyle.Bold;
                txtTag.raycastTarget = false;

                var costCircle = GameBootstrapper.CreateCirclePanel(cardObj.transform, "CostCircle", new Color(0.12f, 0.28f, 0.58f, 0.96f),
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18, -18), new Vector2(30, 30));
                var costOutline = costCircle.AddComponent<Outline>();
                costOutline.effectColor = new Color(0.55f, 0.72f, 0.90f, 1f);
                costOutline.effectDistance = new Vector2(1.5f, 1.5f);
                var txtCost = GameBootstrapper.CreateText(costCircle.transform, "CostText", card.cost.ToString(), 15,
                    new Color(0.92f, 0.96f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(30, 30));
                txtCost.fontStyle = FontStyle.Bold;
                txtCost.raycastTarget = false;

                var namePanel = GameBootstrapper.CreatePanel(cardObj.transform, "NamePlate", new Color(0.08f, 0.11f, 0.17f, 0.94f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -18), new Vector2(130, 22));
                var txtName = GameBootstrapper.CreateText(namePanel.transform, "NameText", $"Leader #{card.name}", 12,
                    new Color(0.86f, 0.94f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(126, 20));
                txtName.fontStyle = FontStyle.Bold;
                txtName.raycastTarget = false;

                var descPanel = GameBootstrapper.CreatePanel(cardObj.transform, "DescPanel", new Color(0.05f, 0.07f, 0.11f, 0.88f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -48), new Vector2(130, 38));
                var txtDesc = GameBootstrapper.CreateText(descPanel.transform, "DescText", card.description, 10,
                    new Color(0.88f, 0.92f, 0.96f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(126, 36));
                txtDesc.fontStyle = FontStyle.Bold;
                txtDesc.raycastTarget = false;

                var actionBadge = GameBootstrapper.CreatePanel(cardObj.transform, "ActionBadge", new Color(0.18f, 0.28f, 0.42f, 0.95f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -74), new Vector2(124, 18));
                var txtAction = GameBootstrapper.CreateText(actionBadge.transform, "ActionText",
                    string.IsNullOrEmpty(card.tribe) ? "Side Slot Aura" : $"{card.tribe} Leader", 10,
                    new Color(0.88f, 0.95f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120, 16));
                txtAction.fontStyle = FontStyle.Bold;
                txtAction.raycastTarget = false;
            }

            return cardRootObj;
        }

        private void UpdateDetailArtNumber(CardData data)
        {
            if (detailCardArt == null || data == null) return;
            Sprite customArt = MonsterArtLoader.GetInspectorSprite(data);
            if (customArt == null && data.cardType == CardType.Environment)
            {
                customArt = BattlefieldAtmosphereEngine.GetOrLoadGroundSprite(data.element, Mathf.Max(1, data.envLevel));
            }
            detailCardArt.gameObject.SetActive(true);

            var numTr = detailCardArt.transform.Find("DetailBigNumber");
            var spellSymTr = detailCardArt.transform.Find("InspectorSpellSymbol");
            var detailStandby = detailCardArt.GetComponent<MonsterStandbyAnimator>();

            if (data.cardType == CardType.Spell)
            {
                if (detailStandby != null) detailStandby.ClearStandby();
                Sprite scrollSprite = MonsterArtLoader.GetSpellScrollSprite();
                if (scrollSprite != null)
                {
                    detailCardArt.sprite = scrollSprite;
                    detailCardArt.color = Color.white;
                    detailCardArt.preserveAspect = true;

                    if (customArt != null)
                    {
                        Image symImg;
                        RectTransform symRect;
                        if (spellSymTr == null)
                        {
                            var symObj = new GameObject("InspectorSpellSymbol");
                            symObj.transform.SetParent(detailCardArt.transform, false);
                            symRect = symObj.AddComponent<RectTransform>();
                            symImg = symObj.AddComponent<Image>();
                            symImg.raycastTarget = false;
                        }
                        else
                        {
                            spellSymTr.gameObject.SetActive(true);
                            symRect = spellSymTr.GetComponent<RectTransform>();
                            symImg = spellSymTr.GetComponent<Image>();
                        }
                        if (symRect != null)
                        {
                            symRect.anchorMin = new Vector2(0.5f, 0.5f);
                            symRect.anchorMax = new Vector2(0.5f, 0.5f);
                            symRect.sizeDelta = new Vector2(126f, 168f);
                            symRect.anchoredPosition = new Vector2(0f, -3f);
                        }
                        if (symImg != null)
                        {
                            symImg.sprite = customArt;
                            symImg.preserveAspect = true;
                            symImg.color = Color.white;
                        }
                        if (numTr != null) numTr.gameObject.SetActive(false);
                        return;
                    }
                    else
                    {
                        if (spellSymTr != null) spellSymTr.gameObject.SetActive(false);
                    }
                }
            }
            else
            {
                if (spellSymTr != null) spellSymTr.gameObject.SetActive(false);
                detailCardArt.preserveAspect = false;
            }

            if (customArt != null && data.cardType != CardType.Spell)
            {
                detailCardArt.sprite = customArt;
                detailCardArt.color = Color.white;
                if (MonsterArtLoader.HasStandby25DAssets(data))
                {
                    if (detailStandby == null) detailStandby = detailCardArt.gameObject.AddComponent<MonsterStandbyAnimator>();
                    detailStandby.Configure(data, true);
                }
                else if (detailStandby != null)
                {
                    detailStandby.ClearStandby();
                }
                if (numTr != null) numTr.gameObject.SetActive(false);
                return;
            }
            if (detailStandby != null) detailStandby.ClearStandby();

            bool hasScrollShown = (data.cardType == CardType.Spell && detailCardArt.sprite != null);
            if (!hasScrollShown)
            {
                detailCardArt.sprite = null;
                detailCardArt.color = MonsterArtLoader.GetEnvironmentAtmosphereColor(data.element);
            }

            Color textCol = hasScrollShown
                ? new Color(0.22f, 0.12f, 0.05f)
                : MonsterArtLoader.GetElementColor(data.element);

            Text numTxt;
            if (numTr == null)
            {
                numTxt = GameBootstrapper.CreateText(detailCardArt.transform, "DetailBigNumber", data.name, 46,
                    textCol, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(240, 120));
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
                    numTxt.color = textCol;
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
                detailNameText.color = isPlayer ? new Color(0.88f, 0.94f, 1f) : new Color(0.95f, 0.62f, 0.64f);
            }

            if (detailMetaText != null)
            {
                string elemEmoji = MonsterArtLoader.GetElementEmoji(data.element);
                string tribePart = string.IsNullOrEmpty(data.tribe) ? "" : $" • {MonsterArtLoader.GetTribeEmoji(data.tribe)} {data.tribe}";
                string sideStr = isPlayer ? "Allied" : "Enemy";
                detailMetaText.text = $"{elemEmoji} {data.element}{tribePart} • {data.role} ({sideStr}) • Cost: {data.cost} Mana";
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
                CardType.Environment => "Shared Environment 🌍",
                CardType.Leader => "Side Leader 👑",
                _ => "Monster Card 🐉"
            };

            if (detailNameText != null)
            {
                detailNameText.text = data.cardType == CardType.Environment
                    ? data.name
                    : $"Card #{data.name}";
                detailNameText.color = new Color(0.90f, 0.95f, 1f);
            }

            if (detailMetaText != null)
            {
                string elemEmoji = MonsterArtLoader.GetElementEmoji(data.element);
                string tribePart = string.IsNullOrEmpty(data.tribe) ? "" : $" • {MonsterArtLoader.GetTribeEmoji(data.tribe)} {data.tribe}";
                detailMetaText.text = $"{elemEmoji} {data.element}{tribePart} • {typeName} • Cost: {data.cost} Mana";
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
                else if (data.cardType == CardType.Leader)
                {
                    detailStatsText.text = "Side Leader Slot • Passive Aura";
                }
                else
                {
                    detailStatsText.text = $"Shared Slot • {data.duration} Turns";
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
                bool canAfford = (gm != null && gm.humanPlayer.currentMana >= data.cost);
                detailStatusText.text = canAfford
                    ? "✨ Ready to play (Enough Mana available)."
                    : "⚠️ Insufficient Mana to play this card.";
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

            MonsterCardGame.Audio.SoundManager.Instance?.PlayCardSelect();

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
                    LogCombatMessage($"Selected Environment: [{card.name}] (Cost: {card.cost}) — Drag to Shared Environment Slot or click 'Activate Environment'.");
                }
                else if (card.cardType == CardType.Leader)
                {
                    LogCombatMessage($"Selected Leader: [{card.name}] (Cost: {card.cost}) — Drag to Your Leader Slot, click it, or click 'Deploy Leader'.");
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
                bool isInstantPlayType = selectedCardForAction.cardType == CardType.Spell ||
                                         selectedCardForAction.cardType == CardType.Environment ||
                                         selectedCardForAction.cardType == CardType.Leader;
                bool canPlaySelected = gm != null && gm.isPlayerTurn && isInstantPlayType &&
                    selectedCardForAction.cost <= gm.humanPlayer.currentMana;

                playCardButton.interactable = canPlaySelected;
                if (txt != null)
                {
                    if (selectedCardForAction.cardType == CardType.Spell)
                        txt.text = $"Cast Spell ({selectedCardForAction.cost}💧)";
                    else if (selectedCardForAction.cardType == CardType.Environment)
                        txt.text = $"Activate Environment ({selectedCardForAction.cost}💧)";
                    else if (selectedCardForAction.cardType == CardType.Leader)
                        txt.text = $"Deploy Leader ({selectedCardForAction.cost}💧)";
                    else
                        txt.text = "Select Slot to Summon";
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
                            CombatSystem.AbilityReadyState.Ready => $"⚡ Activate Ability ({cost})",
                            CombatSystem.AbilityReadyState.AlreadyUsed => "✓ Ability Used This Turn",
                            CombatSystem.AbilityReadyState.NotEnoughMana => $"💧 Need {cost}💧 Mana",
                            CombatSystem.AbilityReadyState.NotEnoughPower => $"⚡ Need {cost}⚡ Power",
                            CombatSystem.AbilityReadyState.NoValidTarget => "🎯 Needs Enemy Target",
                            CombatSystem.AbilityReadyState.Silenced => "🔒 Ability Silenced",
                            CombatSystem.AbilityReadyState.Frozen => "❄️ Monster Frozen",
                            _ => $"⏳ Ability ({cost})"
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
            Vector3 startHandPos = GetPlayerHandCardWorldPos(chosenCard);
            if (gm.humanPlayer.SummonMonster(chosenCard, slotIndex, gm.currentTurn))
            {
                OnPlayerMonsterSummoned(chosenCard, slotIndex, startHandPos, false);
            }
            else
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                LogCombatMessage($"❌ Cannot summon monster to Slot {slotIndex} (Check Mana & slot availability).");
            }
        }

        public void OnPlayerMonsterSummoned(CardData chosenCard, int slotIndex, Vector3? explicitStartPos = null, bool isQuickDrop = false)
        {
            var gm = GameManager.Instance;
            if (gm == null || chosenCard == null) return;

            Vector3 startPos = explicitStartPos ?? GetPlayerHandCardWorldPos(chosenCard);

            if (MonsterCardGame.Audio.SoundManager.Instance != null)
                MonsterCardGame.Audio.SoundManager.Instance.PlayMonsterSummon(chosenCard);

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
                        LogCombatMessage($"⚡ [{chosenCard.name}] Aura Awakened! Click its glowing ⚡ Flash Sigil (or Double-Click card) to unleash ({cost})!");
                    }
                    else
                    {
                        LogCombatMessage($"✨ [{chosenCard.name}] has Special Ability ({cost}): {reason}");
                    }
                }
            }
            else
            {
                selectedBoardSlotIndex = -1;
            }

            float flightDuration = 0f;
            if (slotIndex >= 0 && slotIndex < playerSlotViews.Length && playerSlotViews[slotIndex] != null)
            {
                flightDuration = isQuickDrop ? 0.24f : 0.40f;
                playerSlotViews[slotIndex].SetFlightHidden(true);
            }

            UpdateUI();

            if (flightDuration > 0f)
            {
                StartCoroutine(LaunchMonsterLandingFlight(chosenCard, slotIndex, startPos, flightDuration));
            }

            if (summonedMonster != null)
            {
                ShowMonsterDetails(summonedMonster.data, summonedMonster, true);
            }

            gm.EvaluateAutoEndTurn(0.65f + flightDuration);
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
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                LogCombatMessage(msg);
                if (monster != null)
                {
                    ShowMonsterDetails(monster.data, monster, true);
                }
                UpdatePlayCardButtonState();
                return;
            }

            if (MonsterCardGame.Audio.SoundManager.Instance != null)
                MonsterCardGame.Audio.SoundManager.Instance.PlayMonsterSkill(monster != null ? monster.data : null);

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
            gm.EvaluateAutoEndTurn();
        }

        private void OnPlayCardClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn) return;

            if (selectedCardForAction != null)
            {
                var chosenCard = selectedCardForAction;
                Vector3 startPos = GetPlayerHandCardWorldPos(chosenCard);
                if (chosenCard.cardType == CardType.Spell)
                {
                    if (gm.CastSpell(gm.humanPlayer, gm.aiPlayer, chosenCard))
                    {
                        LaunchPlayerUtilityCardFlight(chosenCard, startPos);
                        DeselectHandCards();
                        UpdateUI();
                    }
                }
                else if (chosenCard.cardType == CardType.Environment)
                {
                    if (gm.PlayEnvironmentCard(gm.humanPlayer, chosenCard))
                    {
                        LaunchPlayerUtilityCardFlight(chosenCard, startPos);
                        DeselectHandCards();
                        UpdateUI();
                    }
                }
                else if (chosenCard.cardType == CardType.Leader)
                {
                    if (gm.PlayLeaderCard(gm.humanPlayer, chosenCard))
                    {
                        LaunchPlayerUtilityCardFlight(chosenCard, startPos);
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
        /// Direct sacrifice of any card in hand via Right-Click (+1 Power & +1 Mana).
        /// </summary>
        public void SacrificeCardDirectly(CardData card, Vector3? explicitStartPos = null)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            if (!gm.isPlayerTurn)
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                LogCombatMessage("⚠️ You cannot sacrifice cards during the enemy turn!");
                return;
            }

            if (gm.humanPlayer.hasSacrificedThisTurn)
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                LogCombatMessage("⚠️ You already sacrificed a card this turn (only 1 sacrifice allowed per turn).");
                return;
            }

            if (card == null) return;

            Vector3 startPos = explicitStartPos ?? GetPlayerHandCardWorldPos(card);

            if (gm.humanPlayer.SacrificeCard(card))
            {
                MonsterCardGame.Audio.SoundManager.Instance.PlayCardSacrifice();
                LogCombatMessage($"🔥 Sacrificed [{card.name}] via Right-Click! Gained +1 Power & +1 Mana.");
                LaunchSacrificeFlight(card, startPos);
                DeselectHandCards();
                UpdateUI();
                gm.EvaluateAutoEndTurn();
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

        public void OnHourglassEndTurnClicked()
        {
            OnEndTurnClicked();
        }

        private void OnEndTurnClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn || gm.currentPhase != GamePhase.ActionPhase) return;

            MonsterCardGame.Audio.SoundManager.Instance?.PlayTurnEnd();
            DeselectHandCards();
            ClearBoardSelection();
            LogCombatMessage("⏳ Astral Hourglass activated — passing turn...");
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
            PlayBattlefieldSpellMirage(spellCard);
            if (BattlefieldAtmosphereEngine.Instance != null && spellCard != null)
            {
                Color c = MonsterArtLoader.GetElementColor(spellCard.element);
                BattlefieldAtmosphereEngine.Instance.TriggerActionPulse(c, 1.35f);
            }
        }

        public void PlayBattlefieldSpellMirage(CardData spellCard)
        {
            if (centerBoardRect == null) return;
            Color elemColor = spellCard != null ? MonsterArtLoader.GetElementColor(spellCard.element) : new Color(0.55f, 0.78f, 1.0f);
            StartCoroutine(BattleAnimations.AnimateBattlefieldMirage(centerBoardRect, elemColor, 1.55f));
        }

        /// <summary>
        /// Detaches a clean visual snapshot of a dying monster at its board slot and plays the &lt;2s death dissolution VFX,
        /// allowing the real board slot to clear cleanly right away without leaving any red or stale boxes behind.
        /// </summary>
        public void PlayMonsterDeathEffect(PlayerState owner, int slotIndex, MonsterInstance deadMonster)
        {
            if (deadMonster == null || deadMonster.data == null) return;

            var gm = GameManager.Instance;
            bool isPlayerSide = (gm != null && owner == gm.humanPlayer);
            var sv = GetSlotView(slotIndex, isPlayerSide);
            if (sv == null) return;

            Transform layer = (cardFlightLayer != null) ? cardFlightLayer : transform;
            var visual = CreateFlyingCardVisual(deadMonster.data, false, out _);
            if (visual != null)
            {
                var rt = visual.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.sizeDelta = new Vector2(148f, 184f);
                }
                activeFlightVisuals.Add(visual);
            }

            Color elemColor = MonsterArtLoader.GetElementColor(deadMonster.data.element);
            StartCoroutine(BattleAnimations.AnimateMonsterDeath(
                visual,
                layer,
                sv.transform.position,
                elemColor,
                1.25f,
                () =>
                {
                    if (visual != null) activeFlightVisuals.Remove(visual);
                }));
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
            if (target == null) return;

            var sv = target.GetComponent<SlotView>();
            if (sv != null)
            {
                sv.PlayHitFlashAndShake(color);
                return;
            }

            StartCoroutine(BattleAnimations.AnimateHitShake(target, color));
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

        public void ShowGameOverScreen(bool isVictory, int turns, int playerHp, int enemyHp, int monstersSummoned, int spellsCast, string envName, int finalPower)
        {
            if (gameOverPanel == null) return;

            gameOverPanel.SetActive(true);

            if (isVictory)
            {
                if (gameOverTitleText != null)
                {
                    gameOverTitleText.text = "🏆 LEGENDARY VICTORY!";
                    gameOverTitleText.color = new Color(0.88f, 0.94f, 1f);
                }
                if (gameOverSubtitleText != null)
                {
                    gameOverSubtitleText.text = "You crushed the enemy warlord and claimed supreme dominion over the arena!";
                    gameOverSubtitleText.color = new Color(0.85f, 1f, 0.9f);
                }
                if (gameOverModalOutline != null)
                {
                    gameOverModalOutline.effectColor = new Color(0.42f, 0.56f, 0.72f, 0.95f);
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
                    $"⚡ Final Power Capacity: {finalPower} (Unlimited ∞)";

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
            MonsterCardGame.Audio.SoundManager.Instance?.PlayUiClick();
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

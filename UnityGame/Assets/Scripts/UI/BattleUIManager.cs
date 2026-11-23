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

        [Header("Leader Title Badges")]
        public Text playerTitleText;
        public Text enemyTitleText;

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
        public Button aetherDrawButton;
        public Text aetherDrawButtonText;
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
        public Image envCardFrameImage;
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
        public Button testCyclePlayerSpiritButton;
        public Button testCycleEnemySpiritButton;

        [Header("Commander Spirit Circular Altars")]
        public Button playerLeaderSlotButton;
        public Image playerLeaderCardArt;
        public GameObject playerLeaderEmptyGroup;
        public Text playerLeaderCostText;
        public Text playerLeaderNameText;
        public Text playerLeaderTribeText;
        public Text playerLeaderDescText;
        public CommanderSpiritSlotController playerSpiritController;

        public Button enemyLeaderSlotButton;
        public Image enemyLeaderCardArt;
        public GameObject enemyLeaderEmptyGroup;
        public Text enemyLeaderCostText;
        public Text enemyLeaderNameText;
        public Text enemyLeaderDescText;
        public CommanderSpiritSlotController enemySpiritController;

        private static readonly string[] AllTestElements = {
            "Fire", "Water", "Ice", "Earth", "Electric", "Air",
            "Nature", "Light", "Dark", "Desert"
        };
        private int currentTestEnvIndex = -1;

        private static readonly (string tribe, string auraDesc)[] SpiritTribePresets = {
            ("Ghosts", "Spectral Veil: Allied Ghosts ONLY gain evasion & non-Ghost enemies suffer dread."),
            ("Demons", "Abyssal Pact: Allied Demons ONLY surge with blazing fury & life leech."),
            ("Celestials", "Solar Sanctuary: Allied Celestials ONLY gain armor, healing & shield blessing."),
            ("Mages", "Astral Resonance: Allied Mages ONLY amplify attack, evasion & skill cost discount."),
            ("Warriors", "Vanguard Valor: Allied Warriors ONLY gain attack, armor & counter-bash."),
            ("Beasts", "Primal Hunt: Allied Beasts ONLY gain savage crits, ferocity & kill feast."),
            ("Elves", "Sylvan Wind: Allied Elves ONLY gain agility, healing & root enemy clans."),
            ("Dwarves", "Runeforge Bastion: Allied Dwarves ONLY fortify armor & molten rune thorns."),
            ("Giants", "Colossus Might: Allied Giants ONLY crush enemies & chill rival clans."),
            ("Insects", "Swarm Chitin: Allied Insects ONLY overwhelm rival clans with deadly poison.")
        };
        private int currentTestSpiritIndex = -1;

        [Header("Combat Log")]
        public Text combatLogText;
        public ScrollRect combatLogScrollRect;
        public RectTransform combatLogContent;
        public Scrollbar combatLogScrollbar;
        private readonly List<string> logEntries = new List<string>();
        private readonly List<GameObject> logEntryObjects = new List<GameObject>();
        private const int MaxCombatLogEntries = 120;

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
        private int pendingEnemyBurnCursor = 0;
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
        [NonSerialized] private int pendingAbilitySourceSlot = -1;
        [NonSerialized] private int pendingAbilitySkillIndex = 0;
        [NonSerialized] private CombatSystem.AbilityTargetMode pendingAbilityTargetMode = CombatSystem.AbilityTargetMode.None;
        public bool IsSelectingAbilityTarget => pendingAbilitySourceSlot >= 0 && pendingAbilityTargetMode != CombatSystem.AbilityTargetMode.None;

        [NonSerialized] private CardData pendingSpellCard = null;
        [NonSerialized] private SpellTargetMode pendingSpellTargetMode = SpellTargetMode.Global;
        [NonSerialized] private int pendingSpellPrimarySlot = -1;
        [NonSerialized] private bool pendingSpellPrimarySelected = false;
        [NonSerialized] private Vector3 pendingSpellStartPos = Vector3.zero;
        public bool IsSelectingSpellTarget => pendingSpellCard != null;
        public bool IsSelectingAnyTarget => IsSelectingAbilityTarget || IsSelectingSpellTarget;

        public void CancelPendingAbilityTargeting(bool silent = true)
        {
            bool wasTargeting = IsSelectingAbilityTarget;
            pendingAbilitySourceSlot = -1;
            pendingAbilitySkillIndex = 0;
            pendingAbilityTargetMode = CombatSystem.AbilityTargetMode.None;
            RefreshAbilityTargetHighlights();
            if (wasTargeting && !silent)
            {
                LogCombatMessage("❎ Ability target selection cancelled.");
            }
        }

        public void CancelPendingSpellTargeting(bool silent = true)
        {
            bool wasTargeting = IsSelectingSpellTarget;
            pendingSpellCard = null;
            pendingSpellTargetMode = SpellTargetMode.Global;
            pendingSpellPrimarySlot = -1;
            pendingSpellPrimarySelected = false;
            pendingSpellStartPos = Vector3.zero;
            RefreshAbilityTargetHighlights();
            if (wasTargeting && !silent)
            {
                LogCombatMessage("❎ Spell target selection cancelled.");
            }
        }

        public void RefreshAbilityTargetHighlights()
        {
            var gm = GameManager.Instance;

            if (enemySlotViews != null)
            {
                for (int i = 0; i < enemySlotViews.Length; i++)
                {
                    if (enemySlotViews[i] != null)
                    {
                        enemySlotViews[i].SetEmptySlotTargetHighlight(false);
                        enemySlotViews[i].SetAbilityTargetHighlight(false, true);
                    }
                }
            }
            if (playerSlotViews != null)
            {
                for (int i = 0; i < playerSlotViews.Length; i++)
                {
                    if (playerSlotViews[i] != null)
                    {
                        playerSlotViews[i].SetEmptySlotTargetHighlight(false);
                        playerSlotViews[i].SetAbilityTargetHighlight(false, false);
                    }
                }
            }

            if (gm == null) return;

            if (IsSelectingAbilityTarget)
            {
                if (pendingAbilityTargetMode == CombatSystem.AbilityTargetMode.SingleEnemy && CombatSystem.Instance != null)
                {
                    string actText = "";
                    var srcSlot = (pendingAbilitySourceSlot >= 0 && gm.humanPlayer.slots != null && pendingAbilitySourceSlot < gm.humanPlayer.slots.Length)
                        ? gm.humanPlayer.slots[pendingAbilitySourceSlot] : null;
                    if (srcSlot != null && srcSlot.IsOccupied && srcSlot.currentMonster != null &&
                        pendingAbilitySkillIndex >= 0 && pendingAbilitySkillIndex < srcSlot.currentMonster.actives.Count)
                    {
                        actText = srcSlot.currentMonster.actives[pendingAbilitySkillIndex];
                    }
                    bool canHitStealth = CombatSystem.Instance.SkillTargetsCanHitStealth(actText);

                    if (enemySlotViews != null && gm.aiPlayer != null && gm.aiPlayer.slots != null)
                    {
                        for (int i = 0; i < enemySlotViews.Length && i < gm.aiPlayer.slots.Length; i++)
                        {
                            var es = gm.aiPlayer.slots[i];
                            bool valid = es != null && es.IsOccupied && es.currentMonster != null && es.currentMonster.IsAlive &&
                                         (canHitStealth || es.currentMonster.HasPassiveSubstr("Night Stealth") == null);
                            if (enemySlotViews[i] != null) enemySlotViews[i].SetAbilityTargetHighlight(valid, true);
                        }
                    }
                }
                else if (pendingAbilityTargetMode == CombatSystem.AbilityTargetMode.SingleAlly)
                {
                    if (playerSlotViews != null && gm.humanPlayer != null && gm.humanPlayer.slots != null)
                    {
                        for (int i = 0; i < playerSlotViews.Length && i < gm.humanPlayer.slots.Length; i++)
                        {
                            var ps = gm.humanPlayer.slots[i];
                            bool valid = ps != null && ps.IsOccupied && ps.currentMonster != null && ps.currentMonster.IsAlive;
                            if (playerSlotViews[i] != null) playerSlotViews[i].SetAbilityTargetHighlight(valid, false);
                        }
                    }
                }
                return;
            }

            if (IsSelectingSpellTarget)
            {
                if (pendingSpellTargetMode == SpellTargetMode.Enemy || pendingSpellTargetMode == SpellTargetMode.Any)
                {
                    if (enemySlotViews != null && gm.aiPlayer != null && gm.aiPlayer.slots != null)
                    {
                        for (int i = 0; i < enemySlotViews.Length && i < gm.aiPlayer.slots.Length; i++)
                        {
                            var es = gm.aiPlayer.slots[i];
                            bool valid = es != null && es.IsOccupied && es.currentMonster != null && es.currentMonster.IsAlive &&
                                         es.currentMonster.HasPassiveSubstr("Night Stealth") == null;
                            if (enemySlotViews[i] != null) enemySlotViews[i].SetAbilityTargetHighlight(valid, true);
                        }
                    }
                }

                if (pendingSpellTargetMode == SpellTargetMode.Ally || pendingSpellTargetMode == SpellTargetMode.Any ||
                    (pendingSpellTargetMode == SpellTargetMode.AllyToEmptySlot && !pendingSpellPrimarySelected))
                {
                    if (playerSlotViews != null && gm.humanPlayer != null && gm.humanPlayer.slots != null)
                    {
                        for (int i = 0; i < playerSlotViews.Length && i < gm.humanPlayer.slots.Length; i++)
                        {
                            var ps = gm.humanPlayer.slots[i];
                            bool valid = ps != null && ps.IsOccupied && ps.currentMonster != null && ps.currentMonster.IsAlive;
                            if (playerSlotViews[i] != null) playerSlotViews[i].SetAbilityTargetHighlight(valid, false);
                        }
                    }
                }
                else if (pendingSpellTargetMode == SpellTargetMode.EmptySlot ||
                         (pendingSpellTargetMode == SpellTargetMode.AllyToEmptySlot && pendingSpellPrimarySelected))
                {
                    string lbl = pendingSpellTargetMode == SpellTargetMode.EmptySlot ? "📍 REVIVE HERE" : "📍 MOVE HERE";
                    if (playerSlotViews != null && gm.humanPlayer != null && gm.humanPlayer.slots != null)
                    {
                        for (int i = 0; i < playerSlotViews.Length && i < gm.humanPlayer.slots.Length; i++)
                        {
                            var ps = gm.humanPlayer.slots[i];
                            bool isEmpty = ps != null && !ps.IsOccupied;
                            if (playerSlotViews[i] != null)
                            {
                                playerSlotViews[i].SetEmptySlotTargetHighlight(isEmpty, lbl);
                                if (pendingSpellTargetMode == SpellTargetMode.AllyToEmptySlot && i == pendingSpellPrimarySlot)
                                    playerSlotViews[i].SetBoardSelected(true);
                            }
                        }
                    }
                }
            }
        }

        public static string SanitizeAbilityText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string clean = text.Trim();
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s*\(P[_-]?Score:[^)]*\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s*\(A[_-]?Score:[^)]*\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s*\(Score:[^)]*\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s*\(Eval:[^)]*\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s*\(Win\s*Rate:[^)]*\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s*\(Rating:[^)]*\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s*Rating\s*\d+.*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"[\u0600-\u06FF]+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"[ \t]{2,}", " ").Trim();
            return clean;
        }

        public static string FormatMonsterAbilityRichText(CardData data, MonsterInstance instance = null)
        {
            if (data == null) return "";

            List<string> passives = new List<string>();
            List<string> actives = new List<string>();

            if (instance != null)
            {
                if (instance.passives != null) passives.AddRange(instance.passives);
                if (instance.actives != null) actives.AddRange(instance.actives);
            }
            else
            {
                if (data.passives != null && data.passives.Count > 0)
                {
                    passives.AddRange(data.passives);
                }
                else if (!string.IsNullOrEmpty(data.passives_text) && !data.passives_text.StartsWith("None"))
                {
                    foreach (var p in data.passives_text.Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string clean = p.Trim();
                        if (clean.StartsWith("1. ") || clean.StartsWith("2. ")) clean = clean.Substring(3).Trim();
                        if (!string.IsNullOrEmpty(clean) && !clean.StartsWith("None")) passives.Add(clean);
                    }
                }

                if (data.active_skills != null && data.active_skills.Count > 0)
                {
                    actives.AddRange(data.active_skills);
                }
                else if (!string.IsNullOrEmpty(data.active_skill) && !data.active_skill.StartsWith("None"))
                {
                    foreach (var a in data.active_skill.Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string clean = a.Trim();
                        if (clean.StartsWith("1. ") || clean.StartsWith("2. ")) clean = clean.Substring(3).Trim();
                        if (!string.IsNullOrEmpty(clean) && !clean.StartsWith("None")) actives.Add(clean);
                    }
                }
            }

            if (passives.Count == 0 && actives.Count == 0)
            {
                string fallback = string.IsNullOrEmpty(data.description)
                    ? $"Tactical {data.element} monster with high battlefield combat capabilities."
                    : data.description;
                return $"<color=#E0E8F0>📜 <b>Basic Ability:</b> {SanitizeAbilityText(fallback)}</color>";
            }

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < passives.Count; i++)
            {
                string pText = SanitizeAbilityText(passives[i]);
                if (string.IsNullOrEmpty(pText)) continue;
                if (sb.Length > 0) sb.Append("\n\n");
                string pLabel = passives.Count > 1 ? $"PASSIVE {i + 1}" : "PASSIVE";
                sb.Append($"<color=#FFD866><b>🛡️ {pLabel}:</b></color> <color=#FFFFFF>{pText}</color>");
            }

            for (int i = 0; i < actives.Count; i++)
            {
                string aText = SanitizeAbilityText(actives[i]);
                if (string.IsNullOrEmpty(aText)) continue;
                if (sb.Length > 0) sb.Append("\n\n");
                if (i == 0)
                {
                    string aLabel = actives.Count > 1 ? "ACTIVE SKILL 1" : "ACTIVE SKILL";
                    sb.Append($"<color=#38E1FF><b>⚡ {aLabel}:</b></color> <color=#E2F6FF>{aText}</color>");
                }
                else
                {
                    sb.Append($"<color=#FF6B81><b>⚡ ACTIVE SKILL 2:</b></color> <color=#FFEBEF>{aText}</color>");
                }
            }

            return sb.ToString();
        }

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

            if (testCyclePlayerSpiritButton == null)
            {
                var psb = GameObject.Find("TestCyclePlayerSpiritBtn");
                if (psb != null) testCyclePlayerSpiritButton = psb.GetComponent<Button>();
            }
            if (testCyclePlayerSpiritButton != null)
            {
                testCyclePlayerSpiritButton.onClick.RemoveAllListeners();
                testCyclePlayerSpiritButton.onClick.AddListener(OnTestCycleSpiritClicked);
            }

            if (testCycleEnemySpiritButton == null)
            {
                var esb = GameObject.Find("TestCycleEnemySpiritBtn");
                if (esb != null) testCycleEnemySpiritButton = esb.GetComponent<Button>();
            }
            if (testCycleEnemySpiritButton != null)
            {
                testCycleEnemySpiritButton.onClick.RemoveAllListeners();
                testCycleEnemySpiritButton.onClick.AddListener(OnTestCycleEnemySpiritClicked);
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

            if (GameManager.Instance != null && GameManager.Instance.isMatchStarted)
            {
                StartBattleArenaVisuals();
            }
        }

        public void StartBattleArenaVisuals()
        {
            EnsureAtmosphereEngine();
            UpdateUI();
            UpdateEnvironmentVisuals();
            UpdateLeaderSlotsUI();

            var gm = GameManager.Instance;
            if (gm != null && gm.humanPlayer != null && gm.humanPlayer.hand != null && gm.humanPlayer.hand.Count > 0)
            {
                ShowCardDetails(gm.humanPlayer.hand[0]);
            }

            if (logEntries.Count == 0)
            {
                LogCombatMessage("⚔️ Match started — scroll anytime to review earlier cards & actions, or click a logged card to inspect it.");
            }
        }

        private void EnsureAtmosphereEngine()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                var boardTr = canvas.transform.Find("ScreenRootContainer/CenterBattleBoard");
                if (boardTr == null) boardTr = canvas.transform.Find("CenterBattleBoard");
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
                if (IsSelectingAbilityTarget)
                {
                    CancelPendingAbilityTargeting(silent: false);
                    UpdatePlayCardButtonState();
                    return;
                }
                if (IsSelectingSpellTarget)
                {
                    CancelPendingSpellTargeting(silent: false);
                    UpdatePlayCardButtonState();
                    return;
                }
                OnQuitClicked();
            }
            else if (Input.GetMouseButtonDown(1) && IsSelectingAnyTarget)
            {
                if (IsSelectingAbilityTarget) CancelPendingAbilityTargeting(silent: false);
                if (IsSelectingSpellTarget) CancelPendingSpellTargeting(silent: false);
                UpdatePlayCardButtonState();
                return;
            }
            else if (Input.GetKeyDown(KeyCode.F11))
            {
                ToggleFullScreen();
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

        /// <summary>
        /// Cycles Commander Spirits on the circular Spirit Altar so each faction's medallion portrait & aura can be previewed cleanly.
        /// </summary>
        public void OnTestCycleSpiritClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.humanPlayer == null) return;

            var loadedLeaders = (gm.allLeaders != null)
                ? gm.allLeaders.FindAll(l => l != null)
                : new List<CardData>();

            int totalCount = loadedLeaders.Count > 0 ? loadedLeaders.Count : SpiritTribePresets.Length;
            currentTestSpiritIndex++;
            if (currentTestSpiritIndex >= totalCount)
            {
                currentTestSpiritIndex = -1;
                gm.humanPlayer.activeLeader = null;
                LogCombatMessage("✧ [Commander Spirit]: Cleared circular Spirit Altar back to seal.");
            }
            else if (loadedLeaders.Count > 0)
            {
                var spirit = loadedLeaders[currentTestSpiritIndex];
                gm.humanPlayer.activeLeader = spirit;
                playerSpiritController?.TriggerSummonFlare();
                MonsterCardGame.Audio.SoundManager.Instance?.PlayLeaderDeploy(spirit);
                ShowCardDetails(spirit);
                LogCombatMessage($"👑 [Commander Spirit]: Previewing [{MonsterArtLoader.FormatSpiritTitle(spirit)}] ({MonsterArtLoader.GetTribeEmoji(spirit.tribe)} {spirit.tribe} Clan)!", spirit);
            }
            else
            {
                var preset = SpiritTribePresets[currentTestSpiritIndex];
                var spirit = new CardData
                {
                    id = 8000 + currentTestSpiritIndex + 1,
                    name = $"Commander Spirit: {preset.tribe}",
                    cardType = CardType.Leader,
                    card_type = "leader",
                    element = "",
                    tribe = preset.tribe,
                    cost = 3,
                    atk = 0,
                    def = 0,
                    description = preset.auraDesc
                };
                gm.humanPlayer.activeLeader = spirit;
                playerSpiritController?.TriggerSummonFlare();
                MonsterCardGame.Audio.SoundManager.Instance?.PlayLeaderDeploy(spirit);
                ShowCardDetails(spirit);
                LogCombatMessage($"👑 [Commander Spirit]: Summoned [{spirit.name}] ({MonsterArtLoader.GetTribeEmoji(preset.tribe)} {preset.tribe} Clan)!", spirit);
            }

            UpdateLeaderSlotsUI();
            UpdateUI();
        }

        private int currentEnemySpiritIndex = 0;

        /// <summary>
        /// Cycles Commander Spirits on the Enemy circular Spirit Altar to preview every faction's warlord.
        /// </summary>
        public void OnTestCycleEnemySpiritClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.aiPlayer == null) return;

            var loadedLeaders = (gm.allLeaders != null)
                ? gm.allLeaders.FindAll(l => l != null)
                : new List<CardData>();

            int totalCount = loadedLeaders.Count > 0 ? loadedLeaders.Count : SpiritTribePresets.Length;
            currentEnemySpiritIndex++;
            if (currentEnemySpiritIndex >= totalCount)
            {
                currentEnemySpiritIndex = -1;
                gm.aiPlayer.activeLeader = null;
                LogCombatMessage("💀 [Enemy Spirit]: Cleared enemy Spirit Altar back to seal.");
            }
            else if (loadedLeaders.Count > 0)
            {
                var spirit = loadedLeaders[currentEnemySpiritIndex];
                gm.aiPlayer.activeLeader = spirit;
                enemySpiritController?.TriggerSummonFlare();
                MonsterCardGame.Audio.SoundManager.Instance?.PlayLeaderDeploy(spirit);
                ShowCardDetails(spirit);
                LogCombatMessage($"💀 [Enemy Spirit]: Previewing [{MonsterArtLoader.FormatSpiritTitle(spirit)}] ({MonsterArtLoader.GetTribeEmoji(spirit.tribe)} {spirit.tribe} Clan)!", spirit);
            }
            else
            {
                var preset = SpiritTribePresets[currentEnemySpiritIndex];
                var spirit = new CardData
                {
                    id = 8000 + currentEnemySpiritIndex + 1,
                    name = $"Enemy Spirit: {preset.tribe}",
                    cardType = CardType.Leader,
                    card_type = "leader",
                    element = "",
                    tribe = preset.tribe,
                    cost = 3,
                    atk = 0,
                    def = 0,
                    description = preset.auraDesc
                };
                gm.aiPlayer.activeLeader = spirit;
                enemySpiritController?.TriggerSummonFlare();
                MonsterCardGame.Audio.SoundManager.Instance?.PlayLeaderDeploy(spirit);
                ShowCardDetails(spirit);
                LogCombatMessage($"💀 [Enemy Spirit]: Summoned [{spirit.name}] ({MonsterArtLoader.GetTribeEmoji(preset.tribe)} {preset.tribe} Clan)!", spirit);
            }

            UpdateLeaderSlotsUI();
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
            Application.Quit();
            try
            {
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            catch { }
        }

        public void UpdateUI()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            // 1. Update Leader HP & Title Badges
            if (playerHpText != null) playerHpText.text = $"❤️ {gm.humanPlayer.currentHp} / {gm.humanPlayer.maxHp} HP";
            if (playerHpSlider != null) playerHpSlider.value = (float)gm.humanPlayer.currentHp / gm.humanPlayer.maxHp;

            if (enemyHpText != null) enemyHpText.text = $"❤️ {gm.aiPlayer.currentHp} / {gm.aiPlayer.maxHp} HP";
            if (enemyHpSlider != null) enemyHpSlider.value = (float)gm.aiPlayer.currentHp / gm.aiPlayer.maxHp;

            if (enemyTitleText != null && gm.aiPlayer != null) enemyTitleText.text = gm.aiPlayer.playerName;
            if (playerTitleText != null && gm.humanPlayer != null) playerTitleText.text = gm.humanPlayer.playerName;

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
                sacrificeButton.interactable = gm.isPlayerTurn && !gm.humanPlayer.hasSacrificedThisTurn && gm.humanPlayer.denyDrawSacTurns <= 0 && selectedCardForAction != null;
            }
            if (aetherDrawButton != null)
            {
                bool canDraw = gm.isPlayerTurn && gm.currentPhase == GamePhase.ActionPhase && gm.humanPlayer.CanAetherDraw(out _);
                aetherDrawButton.interactable = canDraw;
                if (aetherDrawButtonText != null)
                {
                    aetherDrawButtonText.text = gm.humanPlayer.hasAetherDrawnThisTurn
                        ? "✓ DRAWN (1/1)"
                        : $"✨ DRAW ({PlayerState.AetherDrawCost}💧)";
                    aetherDrawButtonText.color = canDraw
                        ? new Color(0.86f, 0.98f, 1.0f)
                        : new Color(0.54f, 0.62f, 0.72f);
                }
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

            return $"[ {crystals} ]  Mana: {mana}  |  Aether: {permPower} (∞)";
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
                    if (string.Equals(env.element, "Desert", StringComparison.OrdinalIgnoreCase))
                    {
                        envCardArtImage.color = Color.white;
                    }
                    else
                    {
                        Sprite cardArt = BattlefieldAtmosphereEngine.GetOrLoadGroundSprite(env.element, env.level)
                                      ?? (gm.activeEnvironmentCard != null ? MonsterArtLoader.GetCardSprite(gm.activeEnvironmentCard) : null);
                        envCardArtImage.sprite = cardArt;
                        envCardArtImage.color = (cardArt != null) ? Color.white : MonsterArtLoader.GetEnvironmentAtmosphereColor(env.element);
                    }
                }
                if (envCardFrameImage != null)
                {
                    Sprite frameSp = MonsterArtLoader.GetCardFrameOverlaySprite(env.element);
                    envCardFrameImage.sprite = frameSp;
                    envCardFrameImage.color = Color.white;
                    envCardFrameImage.enabled = (frameSp != null);
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
                    envOwnerText.text = "Shared Arena • Neutral Ground";
                    envOwnerText.color = new Color(0.74f, 0.86f, 0.96f);
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
                    envDurationText.text = "Permanent • Active Until Countered";
                }
                if (envBuffText != null)
                {
                    int buffPercent = Mathf.RoundToInt(env.buffPct * 100);
                    int armorPercent = Mathf.RoundToInt(env.defArmorPct * 100);
                    envBuffText.text = armorPercent > 0
                        ? $"+{buffPercent}% {env.buffedElement} ATK • +{armorPercent}% DEF"
                        : $"+{buffPercent}% {env.buffedElement}";
                    envBuffText.color = new Color(0.45f, 0.94f, 0.62f);
                }
                if (envDebuffText != null)
                {
                    int debuffPercent = Mathf.RoundToInt(env.debuffPct * 100);
                    int vulnPercent = Mathf.RoundToInt(env.oppVulnPct * 100);
                    string debuffedStr = env.debuffedElements != null && env.debuffedElements.Count > 0
                        ? string.Join(", ", env.debuffedElements)
                        : "Rivals";
                    string debPart = debuffPercent > 0 ? $"-{debuffPercent}% {debuffedStr}" : "Balanced ATK";
                    if (vulnPercent > 0) debPart += $" • +{vulnPercent}% Vuln";
                    envDebuffText.text = debPart;
                    envDebuffText.color = new Color(0.96f, 0.56f, 0.56f);
                }

                EnsureAtmosphereEngine();
                EnvironmentCardAnimator.Instance?.SetActiveEnvironment(env.element);
                if (BattlefieldAtmosphereEngine.Instance != null)
                {
                    BattlefieldAtmosphereEngine.Instance.ApplyEnvironmentProfile(env);
                }
            }
            else
            {
                EnvironmentCardAnimator.Instance?.SetActiveEnvironment(null);
                if (envCardArtImage != null)
                {
                    Sprite neutralSprite = BattlefieldAtmosphereEngine.GetOrLoadGroundSprite("", 0);
                    envCardArtImage.sprite = neutralSprite;
                    envCardArtImage.color = neutralSprite != null
                        ? new Color(0.62f, 0.66f, 0.74f, 0.92f)
                        : new Color(0.10f, 0.12f, 0.16f, 1f);
                }
                if (envCardFrameImage != null) envCardFrameImage.enabled = false;
                if (envCardTitleText != null) envCardTitleText.text = "NEUTRAL ARENA";
                if (envCardCostText != null) envCardCostText.text = "-";
                if (envOwnerText != null)
                {
                    envOwnerText.text = "Shared Arena (Neutral)";
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
                gm.humanPlayer != null ? gm.humanPlayer.activeCovenant : null,
                true,
                playerLeaderCardArt,
                playerLeaderEmptyGroup,
                playerLeaderCostText,
                playerLeaderNameText,
                playerLeaderTribeText,
                playerLeaderDescText,
                playerSpiritController);

            UpdateSingleLeaderSlot(
                gm.aiPlayer != null ? gm.aiPlayer.activeLeader : null,
                gm.aiPlayer != null ? gm.aiPlayer.activeCovenant : null,
                false,
                enemyLeaderCardArt,
                enemyLeaderEmptyGroup,
                enemyLeaderCostText,
                enemyLeaderNameText,
                null,
                enemyLeaderDescText,
                enemySpiritController);
        }

        private void UpdateSingleLeaderSlot(
            CardData leader,
            CardData covenant,
            bool isPlayer,
            Image artImg,
            GameObject emptyGroup,
            Text costTxt,
            Text nameTxt,
            Text tribeTxt,
            Text descTxt,
            CommanderSpiritSlotController spiritCtrl)
        {
            Color catColor = MonsterArtLoader.GetSpiritCategoryColor(leader, isPlayer);
            spiritCtrl?.SetCategoryTheme(leader);

            if (leader != null)
            {
                if (emptyGroup != null) emptyGroup.SetActive(false);
                if (spiritCtrl != null && spiritCtrl.circularRim != null)
                {
                    spiritCtrl.circularRim.gameObject.SetActive(false);
                }
                if (artImg != null)
                {
                    artImg.gameObject.SetActive(true);
                    Sprite sp = MonsterArtLoader.GetCardSprite(leader);
                    if (sp != null)
                    {
                        artImg.sprite = sp;
                        artImg.color = Color.white;
                        artImg.preserveAspect = true;
                    }
                    else
                    {
                        artImg.sprite = GameBootstrapper.GetSpiritCrestSprite();
                        artImg.color = Color.Lerp(catColor, Color.white, 0.30f);
                        artImg.preserveAspect = true;
                    }
                }
                if (costTxt != null)
                {
                    if (costTxt.transform.parent != null) costTxt.transform.parent.gameObject.SetActive(true);
                    costTxt.gameObject.SetActive(true);
                    costTxt.text = leader.cost.ToString();
                }
                if (nameTxt != null)
                {
                    string titleStr = MonsterArtLoader.FormatSpiritTitle(leader);
                    if (covenant != null) titleStr += $"\n⚖️ {covenant.name}";
                    nameTxt.text = titleStr;
                    nameTxt.color = Color.Lerp(catColor, Color.white, 0.48f);
                }
                if (tribeTxt != null && spiritCtrl == null)
                {
                    string catIcon = MonsterArtLoader.GetTribeEmoji(leader.tribe);
                    string tribeDisplay = MonsterArtLoader.GetTribeDisplayName(leader.tribe);
                    tribeTxt.text = $"{catIcon} {tribeDisplay.ToUpper()} CLAN";
                }
                if (descTxt != null)
                {
                    string dStr = string.IsNullOrEmpty(leader.description)
                        ? "Commander Spirit passive aura is active and empowering allied clan monsters."
                        : leader.description;
                    if (covenant != null) dStr += $"\n⚖️ COVENANT: {covenant.description}";
                    descTxt.text = dStr;
                }
            }
            else if (covenant != null)
            {
                if (emptyGroup != null) emptyGroup.SetActive(false);
                if (spiritCtrl != null && spiritCtrl.circularRim != null)
                {
                    spiritCtrl.circularRim.gameObject.SetActive(true);
                }
                if (artImg != null)
                {
                    artImg.gameObject.SetActive(true);
                    Sprite sp = MonsterArtLoader.GetCardSprite(covenant) ?? MonsterArtLoader.GetCovenantFrameSprite();
                    artImg.sprite = sp != null ? sp : GameBootstrapper.GetSpiritCrestSprite();
                    artImg.color = sp != null ? Color.white : new Color(0.96f, 0.74f, 0.28f);
                    artImg.preserveAspect = true;
                }
                if (costTxt != null)
                {
                    costTxt.gameObject.SetActive(false);
                    if (costTxt.transform.parent != null) costTxt.transform.parent.gameObject.SetActive(false);
                }
                if (nameTxt != null)
                {
                    nameTxt.text = $"⚖️ {covenant.name}";
                    nameTxt.color = new Color(0.98f, 0.80f, 0.34f);
                }
                if (tribeTxt != null && spiritCtrl == null)
                {
                    tribeTxt.text = "⚖️ ALTAR COVENANT ACTIVE";
                }
                if (descTxt != null)
                {
                    descTxt.text = covenant.description;
                }
            }
            else
            {
                if (emptyGroup != null) emptyGroup.SetActive(true);
                if (spiritCtrl != null && spiritCtrl.circularRim != null)
                {
                    spiritCtrl.circularRim.gameObject.SetActive(true);
                }
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
                    nameTxt.text = isPlayer ? "ALTAR VACANT" : "NO ENEMY SPIRIT";
                    nameTxt.color = isPlayer ? new Color(0.76f, 0.86f, 0.96f) : new Color(0.88f, 0.68f, 0.70f);
                }
                if (tribeTxt != null && spiritCtrl == null)
                {
                    tribeTxt.text = isPlayer ? "✧ SPIRIT / COVENANT ALTAR" : "✧ ENEMY ALTAR (EMPTY)";
                }
                if (descTxt != null)
                {
                    descTxt.text = isPlayer
                        ? "Drag a Commander Spirit or Altar Covenant card here to activate it."
                        : "Enemy Spirit & Altar Covenant sanctuary.";
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

            if (selectedCardForAction != null && gm.isPlayerTurn)
            {
                if (selectedCardForAction.cardType == CardType.Leader || string.Equals(selectedCardForAction.card_type, "leader", StringComparison.OrdinalIgnoreCase))
                {
                    if (gm.PlayLeaderCard(gm.humanPlayer, selectedCardForAction))
                    {
                        playerSpiritController?.TriggerSummonFlare();
                        DeselectHandCards();
                        UpdateUI();
                    }
                    return;
                }
                if (selectedCardForAction.cardType == CardType.Covenant || string.Equals(selectedCardForAction.card_type, "covenant", StringComparison.OrdinalIgnoreCase))
                {
                    if (gm.PlayCovenantCard(gm.humanPlayer, selectedCardForAction))
                    {
                        DeselectHandCards();
                        UpdateUI();
                    }
                    return;
                }
            }

            if (gm.humanPlayer.activeLeader != null)
            {
                ShowCardDetails(gm.humanPlayer.activeLeader);
            }
            else if (gm.humanPlayer.activeCovenant != null)
            {
                ShowCardDetails(gm.humanPlayer.activeCovenant);
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
            pendingEnemyBurnCursor = 0;

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

        public void NotifyOldestCardBurned(bool isPlayerSide, CardData burnedCard, CardData incomingCard)
        {
            var gm = GameManager.Instance;
            int maxCap = isPlayerSide
                ? ((gm != null && gm.humanPlayer != null) ? gm.humanPlayer.maxHandSize : PlayerState.DefaultMaxHandSize)
                : ((gm != null && gm.aiPlayer != null) ? gm.aiPlayer.maxHandSize : PlayerState.DefaultMaxHandSize);

            if (isPlayerSide && burnedCard != null)
            {
                if (selectedCardForAction == burnedCard)
                {
                    DeselectHandCards();
                }

                string incomingName = incomingCard != null ? incomingCard.name : "New Card";
                LogCombatMessage($"🔥 Hand full (Max {maxCap})! Burned oldest card [{burnedCard.name}] to draw [{incomingName}].", burnedCard);
            }
            else if (!isPlayerSide)
            {
                LogCombatMessage($"🔥 Enemy hand full (Max {maxCap})! Enemy burned their oldest card.");
            }

            if (!gameObject.activeInHierarchy) return;

            Transform layer = (cardFlightLayer != null) ? cardFlightLayer : transform;
            Vector3 burnWorldPos;

            if (isPlayerSide)
            {
                burnWorldPos = playerHandContainer != null
                    ? playerHandContainer.TransformPoint(new Vector3(-474f, 0f, 0f))
                    : GetCenterArenaWorldPos() + new Vector3(-474f, -380f, 0f);

                if (activeDraggingCard != null && activeDraggingCard.cardData == burnedCard)
                {
                    burnWorldPos = activeDraggingCard.transform.position;
                }
                else if (playerHandContainer != null && playerHandContainer.childCount > 0)
                {
                    bool matched = false;
                    for (int i = 0; i < playerHandContainer.childCount; i++)
                    {
                        var child = playerHandContainer.GetChild(i);
                        var drag = child.GetComponent<DraggableCard>();
                        if (drag != null && drag.cardData == burnedCard)
                        {
                            burnWorldPos = child.position;
                            matched = true;
                            break;
                        }
                    }
                    if (!matched)
                    {
                        burnWorldPos = playerHandContainer.GetChild(0).position;
                    }
                }
            }
            else
            {
                if (enemyHandContainer != null && enemyHandContainer.childCount > 0)
                {
                    int idx = Mathf.Clamp(pendingEnemyBurnCursor, 0, enemyHandContainer.childCount - 1);
                    pendingEnemyBurnCursor++;
                    burnWorldPos = enemyHandContainer.GetChild(idx).position;
                }
                else if (enemyHandContainer != null)
                {
                    burnWorldPos = enemyHandContainer.TransformPoint(new Vector3(-474f, 0f, 0f));
                }
                else
                {
                    burnWorldPos = GetCenterArenaWorldPos() + new Vector3(-474f, 380f, 0f);
                }
            }

            GameObject burnVisual = null;
            if (isPlayerSide && burnedCard != null)
            {
                burnVisual = CreateFlyingCardVisual(burnedCard, false, out _);
            }
            else if (!isPlayerSide)
            {
                burnVisual = CreateFaceDownCardUI(layer, -1);
                var rect = burnVisual.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = new Vector2(138f, 182f);
                }
                var cg = EnsureCanvasGroup(burnVisual);
                cg.blocksRaycasts = false;
                cg.interactable = false;
                foreach (var g in burnVisual.GetComponentsInChildren<Graphic>(true))
                {
                    if (g != null) g.raycastTarget = false;
                }
                activeFlightVisuals.Add(burnVisual);
            }

            MonsterCardGame.Audio.SoundManager.Instance?.PlayCardSacrifice();

            if (burnVisual != null)
            {
                StartCoroutine(BattleAnimations.AnimateMonsterDeath(
                    burnVisual,
                    layer,
                    burnWorldPos,
                    new Color(1f, 0.42f, 0.12f),
                    0.78f,
                    () => activeFlightVisuals.Remove(burnVisual)));
            }

            string floatTag = (isPlayerSide && burnedCard != null)
                ? $"🔥 BURNED #{burnedCard.name}"
                : "🔥 OVERFLOW BURN";
            StartCoroutine(BattleAnimations.AnimateFloatingStatus(
                layer,
                burnWorldPos + new Vector3(0f, isPlayerSide ? 36f : -86f, 0f),
                floatTag,
                new Color(1f, 0.48f, 0.16f)));
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

        public Vector3 GetEnemyLeaderTargetWorldPos()
        {
            if (enemyLeaderSlotButton != null) return enemyLeaderSlotButton.transform.position;
            return GetCenterArenaWorldPos() + new Vector3(680f, 340f, 0f);
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

        private static CanvasGroup EnsureCanvasGroup(GameObject go)
        {
            if (go == null) return null;
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            return cg;
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
            var cg = EnsureCanvasGroup(cardGo);
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

        private static void ConfigureHandLayout(Transform handContainer)
        {
            if (handContainer == null) return;
            var layout = handContainer.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = new RectOffset(27, 27, 0, 0);
                layout.spacing = 20f;
                layout.childAlignment = TextAnchor.MiddleLeft;
                layout.childControlWidth = false;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
            }
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

            ConfigureHandLayout(playerHandContainer);

            var mainCanvas = GetComponentInParent<Canvas>();
            var createdItems = new List<GameObject>();
            var createdCards = new List<CardData>();

            foreach (var card in gm.humanPlayer.hand)
            {
                var cardObj = CreateDynamicCardUI(playerHandContainer, card);
                var cg = EnsureCanvasGroup(cardObj);
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

            pendingEnemyBurnCursor = 0;

            for (int c = enemyHandContainer.childCount - 1; c >= 0; c--)
            {
                var child = enemyHandContainer.GetChild(c);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            ConfigureHandLayout(enemyHandContainer);

            int handCount = gm.aiPlayer.hand.Count;
            if (enemyHandCountText != null)
            {
                enemyHandCountText.text = $"🎴 Hand: {handCount}/{gm.aiPlayer.maxHandSize}  |  Deck: {gm.aiPlayer.deck.Count}";
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
                var cg = EnsureCanvasGroup(createdBacks[i]);
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

        public GameObject CreateDynamicCardUI(Transform parent, CardData card)
        {
            Color elemColor = MonsterArtLoader.GetElementColor(card.element);

            Color cardBgColor = card.cardType switch
            {
                CardType.Spell => new Color(0f, 0f, 0f, 0f),
                CardType.Environment => new Color(0.06f, 0.12f, 0.16f, 0.98f),
                CardType.Leader => new Color(0f, 0f, 0f, 0f),
                CardType.Covenant => new Color(0f, 0f, 0f, 0f),
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
            if (card.cardType == CardType.Monster || card.cardType == CardType.Leader)
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
                    var nameBg = GameBootstrapper.CreatePanel(cardObj.transform, "MonsterNameBg", new Color(0.04f, 0.05f, 0.09f, 0.72f),
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(106f, 54f));
                    nameBg.GetComponent<Image>().raycastTarget = false;

                    var idCenterTxt = GameBootstrapper.CreateText(nameBg.transform, "MonsterIdCenter", card.name, 12,
                        Color.Lerp(elemColor, Color.white, 0.45f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(98f, 48f));
                    idCenterTxt.fontStyle = FontStyle.Bold;
                    idCenterTxt.resizeTextForBestFit = true;
                    idCenterTxt.resizeTextMinSize = 8;
                    idCenterTxt.resizeTextMaxSize = 12;
                    idCenterTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
                    idCenterTxt.verticalOverflow = VerticalWrapMode.Truncate;
                    idCenterTxt.raycastTarget = false;
                    GameBootstrapper.AddTextShadow(idCenterTxt);

                    string elemEmoji = MonsterArtLoader.GetElementEmoji(card.element);
                    string tribeEmoji = MonsterArtLoader.GetTribeEmoji(card.tribe);
                    var tierBadgeTxt = GameBootstrapper.CreateText(cardObj.transform, "TierBadge", $"{elemEmoji}{tribeEmoji} T{Mathf.Max(1, card.tier_num)}", 10,
                        new Color(0.92f, 0.94f, 0.98f), TextAnchor.MiddleCenter, new Vector2(0, -30), new Vector2(100, 18));
                    tierBadgeTxt.fontStyle = FontStyle.Bold;
                    tierBadgeTxt.resizeTextForBestFit = true;
                    tierBadgeTxt.resizeTextMinSize = 7;
                    tierBadgeTxt.resizeTextMaxSize = 10;
                    tierBadgeTxt.verticalOverflow = VerticalWrapMode.Truncate;
                    tierBadgeTxt.raycastTarget = false;
                    GameBootstrapper.AddTextShadow(tierBadgeTxt);
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

                // Top-Left Clan / Tribe Emblem Badge (enlarged & inset cleanly away from frame edges)
                Sprite clanSprite = MonsterArtLoader.GetClanEmblemSprite(card.tribe);
                if (clanSprite != null)
                {
                    var clanObj = new GameObject("ClanEmblemBadge");
                    clanObj.transform.SetParent(cardObj.transform, false);
                    var rectClan = clanObj.AddComponent<RectTransform>();
                    rectClan.anchorMin = new Vector2(0f, 1f);
                    rectClan.anchorMax = new Vector2(0f, 1f);
                    rectClan.sizeDelta = new Vector2(35f, 35f);
                    rectClan.anchoredPosition = new Vector2(21.5f, -21.5f);
                    var clanImg = clanObj.AddComponent<Image>();
                    clanImg.sprite = clanSprite;
                    clanImg.color = Color.white;
                    clanImg.preserveAspect = true;
                    clanImg.raycastTarget = false;
                }

                // Top-Center Summon Cost Number (Inscribed inside top medallion socket; shows discounted cost in green)
                var gmHand = GameManager.Instance;
                int effHandCost = (gmHand != null && gmHand.humanPlayer != null) ? gmHand.humanPlayer.GetEffectiveCardCost(card) : card.cost;
                Color costNumCol = effHandCost < card.cost ? new Color(0.45f, 1f, 0.55f) : new Color(0.92f, 0.95f, 1f);
                var txtCost = GameBootstrapper.CreateText(cardObj.transform, "CostText", effHandCost.ToString(), 14,
                    costNumCol, TextAnchor.MiddleCenter, new Vector2(0, -11f), new Vector2(28, 22));
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

                    // Top-Center Cost Number inside the Sapphire Scroll Medallion (Accurately Centered)
                    var txtCost = GameBootstrapper.CreateText(cardObj.transform, "CostText", card.cost.ToString(), 14,
                        new Color(0.92f, 0.95f, 1f), TextAnchor.MiddleCenter, new Vector2(0f, -14f), new Vector2(28f, 22f));
                    txtCost.rectTransform.anchorMin = new Vector2(0.5f, 1f);
                    txtCost.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                    txtCost.rectTransform.anchoredPosition = new Vector2(0f, -14f);
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
                        var runeTxt = GameBootstrapper.CreateText(cardObj.transform, "SpellIdCenter", $"🔮\n#{card.id}", 15,
                            new Color(0.24f, 0.14f, 0.34f), TextAnchor.MiddleCenter, new Vector2(0f, 16f), new Vector2(88f, 48f));
                        runeTxt.fontStyle = FontStyle.Bold;
                        runeTxt.resizeTextForBestFit = true;
                        runeTxt.resizeTextMinSize = 10;
                        runeTxt.resizeTextMaxSize = 16;
                        runeTxt.verticalOverflow = VerticalWrapMode.Truncate;
                        runeTxt.raycastTarget = false;

                        // Fallback text only when no art is assigned (Localization-safe: hidden when art exists)
                        string spellDisplayName = string.IsNullOrEmpty(card.name) || card.name == card.id.ToString() ? $"Spell #{card.id}" : card.name;
                        var txtName = GameBootstrapper.CreateText(cardObj.transform, "NameText", spellDisplayName, 9,
                            new Color(0.20f, 0.10f, 0.04f), TextAnchor.MiddleCenter, new Vector2(0f, -34f), new Vector2(90f, 26f));
                        txtName.fontStyle = FontStyle.Bold;
                        txtName.resizeTextForBestFit = true;
                        txtName.resizeTextMinSize = 7;
                        txtName.resizeTextMaxSize = 9;
                        txtName.horizontalOverflow = HorizontalWrapMode.Wrap;
                        txtName.verticalOverflow = VerticalWrapMode.Truncate;
                        txtName.raycastTarget = false;

                        string shortRole = string.IsNullOrEmpty(card.role)
                            ? $"T{Mathf.Max(1, card.tier_num)} Spell"
                            : $"{card.role.Replace(" Spell", "").Trim()} • T{Mathf.Max(1, card.tier_num)}";
                        var txtAction = GameBootstrapper.CreateText(cardObj.transform, "ActionText", shortRole, 8,
                            new Color(0.32f, 0.16f, 0.07f), TextAnchor.MiddleCenter, new Vector2(0f, -58f), new Vector2(90f, 14f));
                        txtAction.fontStyle = FontStyle.Bold;
                        txtAction.resizeTextForBestFit = true;
                        txtAction.resizeTextMinSize = 6;
                        txtAction.resizeTextMaxSize = 8;
                        txtAction.verticalOverflow = VerticalWrapMode.Truncate;
                        txtAction.raycastTarget = false;
                    }
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
                    var txtCost = GameBootstrapper.CreateText(costCircle.transform, "CostText", card.cost.ToString(), 15,
                        new Color(0.92f, 0.95f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(32, 32));
                    txtCost.fontStyle = FontStyle.Bold;
                    txtCost.raycastTarget = false;

                    if (customArt == null)
                    {
                        var iconObj = GameBootstrapper.CreateText(cardObj.transform, "SpellIdCenter", $"🔮 #{card.id}", 14,
                            new Color(0.84f, 0.88f, 0.96f), TextAnchor.MiddleCenter, new Vector2(0, 32), new Vector2(116, 32));
                        iconObj.fontStyle = FontStyle.Bold;
                        iconObj.resizeTextForBestFit = true;
                        iconObj.resizeTextMinSize = 9;
                        iconObj.resizeTextMaxSize = 14;
                        iconObj.verticalOverflow = VerticalWrapMode.Truncate;
                        iconObj.raycastTarget = false;
                    }

                    var namePanel = GameBootstrapper.CreatePanel(cardObj.transform, "NamePlate", new Color(0.18f, 0.12f, 0.28f, 0.92f),
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(130, 24));
                    var txtName = GameBootstrapper.CreateText(namePanel.transform, "NameText", $"{card.name} • T{Mathf.Max(1, card.tier_num)}", 10,
                        new Color(0.88f, 0.92f, 0.98f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(124, 22));
                    txtName.fontStyle = FontStyle.Bold;
                    txtName.resizeTextForBestFit = true;
                    txtName.resizeTextMinSize = 7;
                    txtName.resizeTextMaxSize = 10;
                    txtName.horizontalOverflow = HorizontalWrapMode.Wrap;
                    txtName.verticalOverflow = VerticalWrapMode.Truncate;
                    txtName.raycastTarget = false;

                    var descPanel = GameBootstrapper.CreatePanel(cardObj.transform, "DescPanel", new Color(0.06f, 0.05f, 0.10f, 0.85f),
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -44), new Vector2(130, 42));
                    string compactDesc = (!string.IsNullOrEmpty(card.description) && card.description.Length > 72)
                        ? card.description.Substring(0, 69) + "..."
                        : card.description;
                    var txtDesc = GameBootstrapper.CreateText(descPanel.transform, "DescText", compactDesc, 9,
                        new Color(0.92f, 0.92f, 0.96f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(124, 38));
                    txtDesc.resizeTextForBestFit = true;
                    txtDesc.resizeTextMinSize = 6;
                    txtDesc.resizeTextMaxSize = 9;
                    txtDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
                    txtDesc.verticalOverflow = VerticalWrapMode.Truncate;
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
                Sprite envArt = customArt ?? BattlefieldAtmosphereEngine.GetOrLoadGroundSprite(card.element, Mathf.Max(1, card.envLevel));
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

                // Classic Hand-Crafted Elemental Frame Overlay for Environment Card in Hand
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

                // Top-Center Summon Cost Number (Inscribed inside top medallion socket; shows discounted cost in green)
                var gmHand = GameManager.Instance;
                int effHandCost = (gmHand != null && gmHand.humanPlayer != null) ? gmHand.humanPlayer.GetEffectiveCardCost(card) : card.cost;
                Color costNumCol = effHandCost < card.cost ? new Color(0.45f, 1f, 0.55f) : new Color(0.92f, 0.95f, 1f);
                var txtCost = GameBootstrapper.CreateText(cardObj.transform, "CostText", effHandCost.ToString(), 14,
                    costNumCol, TextAnchor.MiddleCenter, new Vector2(0, -11f), new Vector2(28, 22));
                txtCost.rectTransform.anchorMin = new Vector2(0.5f, 1f);
                txtCost.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                txtCost.rectTransform.anchoredPosition = new Vector2(0, -11f);
                txtCost.fontStyle = FontStyle.Bold;
                txtCost.raycastTarget = false;
                GameBootstrapper.AddTextShadow(txtCost);

                // Bottom-Left Socket: Infinity symbol ∞ representing non-combat / boundless atmospheric presence
                var txtAtk = GameBootstrapper.CreateText(cardObj.transform, "AtkText", "∞", 17,
                    new Color(0.92f, 0.96f, 1f), TextAnchor.MiddleCenter, new Vector2(17.5f, 20.5f), new Vector2(26, 22));
                txtAtk.rectTransform.anchorMin = new Vector2(0f, 0f);
                txtAtk.rectTransform.anchorMax = new Vector2(0f, 0f);
                txtAtk.rectTransform.anchoredPosition = new Vector2(17.5f, 20.5f);
                txtAtk.fontStyle = FontStyle.Bold;
                txtAtk.raycastTarget = false;
                GameBootstrapper.AddTextShadow(txtAtk);

                // Bottom-Right Socket: Infinity symbol ∞ representing permanent weather duration
                var txtDef = GameBootstrapper.CreateText(cardObj.transform, "DefText", "∞", 17,
                    new Color(0.92f, 0.96f, 1f), TextAnchor.MiddleCenter, new Vector2(-17.5f, 20.5f), new Vector2(26, 22));
                txtDef.rectTransform.anchorMin = new Vector2(1f, 0f);
                txtDef.rectTransform.anchorMax = new Vector2(1f, 0f);
                txtDef.rectTransform.anchoredPosition = new Vector2(-17.5f, 20.5f);
                txtDef.fontStyle = FontStyle.Bold;
                txtDef.raycastTarget = false;
                GameBootstrapper.AddTextShadow(txtDef);

                // Center Fallback icon only if artwork is missing (100% clean card when art exists)
                if (envArt == null)
                {
                    string envEmoji = MonsterArtLoader.GetEnvironmentEmoji(card.element);
                    var iconObj = GameBootstrapper.CreateText(cardObj.transform, "EnvIcon", envEmoji, 38,
                        elemColor, TextAnchor.MiddleCenter, new Vector2(0, 22), new Vector2(110, 46));
                    iconObj.raycastTarget = false;
                }
            }
            else if (card.cardType == CardType.Leader)
            {
                Color spiritCatCol = MonsterArtLoader.GetSpiritCategoryColor(card, true);

                // Medallion Halo for hover / selection highlight
                var haloObj = GameBootstrapper.CreateCirclePanel(cardObj.transform, "LeaderMedallionHalo", Color.clear,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 16f), new Vector2(134f, 134f));
                haloObj.GetComponent<Image>().raycastTarget = false;
                var haloOutline = haloObj.AddComponent<Outline>();
                haloOutline.effectColor = Color.Lerp(spiritCatCol, Color.white, 0.55f);
                haloOutline.effectDistance = new Vector2(2f, 2f);
                haloOutline.enabled = false;

                // Circular Medallion Root - displays the user's full unhindered circular frame and art with 100% transparent background
                var discObj = new GameObject("LeaderMedallionDisc");
                discObj.transform.SetParent(cardObj.transform, false);
                var discRt = discObj.AddComponent<RectTransform>();
                discRt.anchorMin = new Vector2(0.5f, 0.5f);
                discRt.anchorMax = new Vector2(0.5f, 0.5f);
                discRt.anchoredPosition = new Vector2(0f, 16f);
                discRt.sizeDelta = new Vector2(134f, 134f);

                if (customArt != null)
                {
                    var artObj = new GameObject("CardArt");
                    artObj.transform.SetParent(discObj.transform, false);
                    var rectArt = artObj.AddComponent<RectTransform>();
                    rectArt.anchorMin = Vector2.zero;
                    rectArt.anchorMax = Vector2.one;
                    rectArt.sizeDelta = Vector2.zero; // Full edge-to-edge fit showing the full circular frame!
                    rectArt.anchoredPosition = Vector2.zero;
                    var artImg = artObj.AddComponent<Image>();
                    artImg.sprite = customArt;
                    artImg.preserveAspect = true;
                    artImg.color = Color.white;
                    artImg.raycastTarget = false;
                }
                else
                {
                    var crestObj = GameBootstrapper.CreateCirclePanel(discObj.transform, "SpiritCardCrest",
                        Color.Lerp(new Color(0.08f, 0.12f, 0.20f, 0.95f), spiritCatCol, 0.35f),
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(126f, 126f));
                    crestObj.GetComponent<Image>().raycastTarget = false;
                    var crestArt = new GameObject("CrestSymbol");
                    crestArt.transform.SetParent(crestObj.transform, false);
                    var crestArtRt = crestArt.AddComponent<RectTransform>();
                    crestArtRt.anchorMin = new Vector2(0.5f, 0.5f);
                    crestArtRt.anchorMax = new Vector2(0.5f, 0.5f);
                    crestArtRt.sizeDelta = new Vector2(88f, 88f);
                    crestArtRt.anchoredPosition = Vector2.zero;
                    var crestArtImg = crestArt.AddComponent<Image>();
                    crestArtImg.sprite = GameBootstrapper.GetSpiritCrestSprite();
                    crestArtImg.color = Color.Lerp(spiritCatCol, Color.white, 0.25f);
                    crestArtImg.raycastTarget = false;

                    var rimObj = GameBootstrapper.CreatePanel(discObj.transform, "CircularTalismanRim",
                        Color.Lerp(spiritCatCol, Color.white, 0.35f),
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(130f, 130f));
                    var rimImg = rimObj.GetComponent<Image>();
                    rimImg.sprite = GameBootstrapper.GetCircularSpiritRimSprite();
                    rimImg.raycastTarget = false;
                }

                var tagBadge = GameBootstrapper.CreatePanel(cardObj.transform, "LeaderBadge", Color.Lerp(new Color(0.08f, 0.12f, 0.20f), spiritCatCol, 0.35f),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -12f), new Vector2(56f, 16f));
                tagBadge.GetComponent<Image>().raycastTarget = false;
                var txtTag = GameBootstrapper.CreateText(tagBadge.transform, "TagText", "SPIRIT", 8,
                    Color.Lerp(spiritCatCol, Color.white, 0.55f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(54f, 14f));
                txtTag.fontStyle = FontStyle.Bold;
                txtTag.raycastTarget = false;

                var costCircle = GameBootstrapper.CreateCirclePanel(cardObj.transform, "CostCircle", new Color(0.12f, 0.28f, 0.58f, 0.96f),
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -12f), new Vector2(28f, 28f));
                costCircle.GetComponent<Image>().raycastTarget = false;
                var costOutline = costCircle.AddComponent<Outline>();
                costOutline.effectColor = Color.Lerp(spiritCatCol, Color.white, 0.35f);
                costOutline.effectDistance = new Vector2(1.5f, 1.5f);
                var txtCost = GameBootstrapper.CreateText(costCircle.transform, "CostText", card.cost.ToString(), 14,
                    new Color(0.92f, 0.96f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(28f, 28f));
                txtCost.fontStyle = FontStyle.Bold;
                txtCost.raycastTarget = false;

                var namePanel = GameBootstrapper.CreatePanel(cardObj.transform, "NamePlate", new Color(0.06f, 0.08f, 0.14f, 0.95f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -54f), new Vector2(134f, 22f));
                namePanel.GetComponent<Image>().raycastTarget = false;
                var nameOutline = namePanel.AddComponent<Outline>();
                nameOutline.effectColor = Color.Lerp(spiritCatCol, Color.black, 0.30f);
                nameOutline.effectDistance = new Vector2(1f, 1f);
                var txtName = GameBootstrapper.CreateText(namePanel.transform, "NameText", MonsterArtLoader.FormatSpiritTitle(card), 10,
                    Color.Lerp(spiritCatCol, Color.white, 0.5f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(128f, 20f));
                txtName.fontStyle = FontStyle.Bold;
                txtName.resizeTextForBestFit = true;
                txtName.resizeTextMinSize = 7;
                txtName.resizeTextMaxSize = 10;
                txtName.raycastTarget = false;

                string tribeDisplay = MonsterArtLoader.GetTribeDisplayName(card.tribe);
                string tribeText = string.IsNullOrEmpty(card.tribe)
                    ? "Commander Spirit"
                    : $"{MonsterArtLoader.GetTribeEmoji(card.tribe)} {tribeDisplay} Clan";
                var actionBadge = GameBootstrapper.CreatePanel(cardObj.transform, "ActionBadge", Color.Lerp(new Color(0.08f, 0.12f, 0.20f), spiritCatCol, 0.32f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -77f), new Vector2(128f, 20f));
                actionBadge.GetComponent<Image>().raycastTarget = false;
                var txtAction = GameBootstrapper.CreateText(actionBadge.transform, "ActionText", tribeText, 10,
                    new Color(0.92f, 0.97f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(124f, 18f));
                txtAction.fontStyle = FontStyle.Bold;
                txtAction.raycastTarget = false;
            }
            else if (card.cardType == CardType.Covenant)
            {
                // Right-Wing Altar Covenant Card (Octagonal Chained Blood Contract Frame)
                Sprite covFrame = MonsterArtLoader.GetCovenantFrameSprite();
                var cardImg = cardObj.GetComponent<Image>();
                Color covGold = new Color(0.96f, 0.78f, 0.32f, 1f);

                if (covFrame != null && cardImg != null)
                {
                    cardImg.sprite = covFrame;
                    cardImg.color = Color.white;
                    cardImg.preserveAspect = false;
                    outline.enabled = false;

                    // 1. Top Gray Octagonal Window (y = +33f): Card Emblem / Illustration area
                    if (customArt != null)
                    {
                        var artObj = new GameObject("CardArt");
                        artObj.transform.SetParent(cardObj.transform, false);
                        var rectArt = artObj.AddComponent<RectTransform>();
                        rectArt.anchorMin = new Vector2(0.5f, 0.5f);
                        rectArt.anchorMax = new Vector2(0.5f, 0.5f);
                        rectArt.sizeDelta = new Vector2(74f, 64f);
                        rectArt.anchoredPosition = new Vector2(0f, 33f);
                        var artImg = artObj.AddComponent<Image>();
                        artImg.sprite = customArt;
                        artImg.preserveAspect = true;
                        artImg.color = Color.white;
                        artImg.raycastTarget = false;
                    }
                    else
                    {
                        var emblem = GameBootstrapper.CreateText(cardObj.transform, "CovEmblem", $"⚖️\n#{card.id}", 16,
                            new Color(0.95f, 0.82f, 0.48f), TextAnchor.MiddleCenter, new Vector2(0f, 38f), new Vector2(78f, 44f));
                        emblem.fontStyle = FontStyle.Bold;
                        emblem.resizeTextForBestFit = true;
                        emblem.resizeTextMinSize = 11;
                        emblem.resizeTextMaxSize = 16;
                        emblem.raycastTarget = false;
                        GameBootstrapper.AddTextShadow(emblem);

                        string shortVowCat = string.IsNullOrEmpty(card.playstyle) ? "Altar Covenant" : card.playstyle;
                        var catTxt = GameBootstrapper.CreateText(cardObj.transform, "CovCatText", $"{shortVowCat} • T{Mathf.Max(1, card.tier_num)}", 8,
                            new Color(0.86f, 0.78f, 0.64f), TextAnchor.MiddleCenter, new Vector2(0f, 12f), new Vector2(76f, 16f));
                        catTxt.fontStyle = FontStyle.Bold;
                        catTxt.resizeTextForBestFit = true;
                        catTxt.resizeTextMinSize = 6;
                        catTxt.resizeTextMaxSize = 8;
                        catTxt.raycastTarget = false;
                        GameBootstrapper.AddTextShadow(catTxt);
                    }

                    // 2. Parchment Box directly above the Red Seal (y = -34f to -54f): Card Name & Pact Summary
                    string covDisplayName = string.IsNullOrEmpty(card.name) ? $"Covenant #{card.id}" : card.name;
                    var txtName = GameBootstrapper.CreateText(cardObj.transform, "NameText", covDisplayName, 9,
                        new Color(0.18f, 0.08f, 0.04f), TextAnchor.MiddleCenter, new Vector2(0f, -35f), new Vector2(78f, 22f));
                    txtName.fontStyle = FontStyle.Bold;
                    txtName.resizeTextForBestFit = true;
                    txtName.resizeTextMinSize = 6;
                    txtName.resizeTextMaxSize = 9;
                    txtName.horizontalOverflow = HorizontalWrapMode.Wrap;
                    txtName.verticalOverflow = VerticalWrapMode.Truncate;
                    txtName.raycastTarget = false;

                    string covDesc = card.description ?? "";
                    if (covDesc.Length > 52) covDesc = covDesc.Substring(0, 49) + "...";
                    var txtDesc = GameBootstrapper.CreateText(cardObj.transform, "DescText", covDesc, 7,
                        new Color(0.32f, 0.14f, 0.08f), TextAnchor.MiddleCenter, new Vector2(0f, -53f), new Vector2(76f, 16f));
                    txtDesc.fontStyle = FontStyle.Bold;
                    txtDesc.resizeTextForBestFit = true;
                    txtDesc.resizeTextMinSize = 5;
                    txtDesc.resizeTextMaxSize = 7;
                    txtDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
                    txtDesc.verticalOverflow = VerticalWrapMode.Truncate;
                    txtDesc.raycastTarget = false;

                    // 3. Bottom Red Runic Circle (Exact center at y = 14.5f from bottom): Summon Cost Number
                    var txtCost = GameBootstrapper.CreateText(cardObj.transform, "CostText", card.cost.ToString(), 15,
                        new Color(1f, 0.95f, 0.85f), TextAnchor.MiddleCenter, new Vector2(0f, 14.5f), new Vector2(28f, 24f));
                    txtCost.rectTransform.anchorMin = new Vector2(0.5f, 0f);
                    txtCost.rectTransform.anchorMax = new Vector2(0.5f, 0f);
                    txtCost.rectTransform.anchoredPosition = new Vector2(0f, 14.5f);
                    txtCost.fontStyle = FontStyle.Bold;
                    txtCost.raycastTarget = false;
                    GameBootstrapper.AddTextShadow(txtCost);
                }
                else
                {
                    // Fallback when frame sprite is missing
                    Color covBg = new Color(0.18f, 0.06f, 0.08f, 0.98f);
                    var bgImg = cardObj.GetComponent<Image>();
                    if (bgImg != null) bgImg.color = covBg;

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
                        artImg.color = new Color(1f, 1f, 1f, 0.78f);
                        artImg.raycastTarget = false;
                    }

                    var costCircle = GameBootstrapper.CreateCirclePanel(cardObj.transform, "CostCircle", new Color(0.50f, 0.14f, 0.16f, 0.98f),
                        new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(28f, 28f));
                    costCircle.GetComponent<Image>().raycastTarget = false;
                    var txtCost = GameBootstrapper.CreateText(costCircle.transform, "CostText", card.cost.ToString(), 14,
                        covGold, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(28f, 28f));
                    txtCost.fontStyle = FontStyle.Bold;
                    txtCost.raycastTarget = false;
                }
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
            var clanBadgeTr = detailCardArt.transform.Find("InspectorClanEmblem");
            var detailStandby = detailCardArt.GetComponent<MonsterStandbyAnimator>();

            Sprite inspectorClanSprite = (data.cardType == CardType.Monster)
                ? MonsterArtLoader.GetClanEmblemSprite(data.tribe)
                : null;
            if (inspectorClanSprite != null)
            {
                Image clanImg;
                RectTransform clanRect;
                if (clanBadgeTr == null)
                {
                    var clanObj = new GameObject("InspectorClanEmblem");
                    clanObj.transform.SetParent(detailCardArt.transform, false);
                    clanRect = clanObj.AddComponent<RectTransform>();
                    clanImg = clanObj.AddComponent<Image>();
                    clanImg.raycastTarget = false;
                }
                else
                {
                    clanBadgeTr.gameObject.SetActive(true);
                    clanRect = clanBadgeTr.GetComponent<RectTransform>();
                    clanImg = clanBadgeTr.GetComponent<Image>();
                }
                if (clanRect != null)
                {
                    clanRect.anchorMin = new Vector2(0f, 1f);
                    clanRect.anchorMax = new Vector2(0f, 1f);
                    clanRect.sizeDelta = new Vector2(44f, 44f);
                    clanRect.anchoredPosition = new Vector2(27f, -27f);
                }
                if (clanImg != null)
                {
                    clanImg.sprite = inspectorClanSprite;
                    clanImg.preserveAspect = true;
                    clanImg.color = Color.white;
                }
            }
            else if (clanBadgeTr != null)
            {
                clanBadgeTr.gameObject.SetActive(false);
            }

            if (data.cardType == CardType.Environment)
            {
                if (detailStandby != null) detailStandby.ClearStandby();
                if (spellSymTr != null) spellSymTr.gameObject.SetActive(false);
                if (clanBadgeTr != null) clanBadgeTr.gameObject.SetActive(false);
                if (numTr != null) numTr.gameObject.SetActive(false);

                detailCardArt.preserveAspect = true;
                detailCardArt.color = Color.white;
                Sprite envFramed = MonsterArtLoader.GetEnvironmentFramedSprite(data) ?? customArt;
                if (envFramed != null)
                {
                    detailCardArt.sprite = envFramed;
                }
                return;
            }

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
                            symRect.sizeDelta = new Vector2(96f, 128f);
                            symRect.anchoredPosition = new Vector2(0f, -2f);
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
                detailCardArt.preserveAspect = (data.cardType == CardType.Leader);
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

            if (data.cardType == CardType.Leader)
            {
                detailCardArt.sprite = GameBootstrapper.GetSpiritCrestSprite();
                detailCardArt.color = MonsterArtLoader.GetSpiritCategoryColor(data, true);
                detailCardArt.preserveAspect = true;
                if (numTr != null) numTr.gameObject.SetActive(false);
                return;
            }

            if (data.cardType == CardType.Covenant)
            {
                Sprite covFrame = MonsterArtLoader.GetCovenantFrameSprite();
                if (covFrame != null)
                {
                    detailCardArt.sprite = covFrame;
                    detailCardArt.color = Color.white;
                    detailCardArt.preserveAspect = true;
                }
            }

            bool hasScrollShown = (data.cardType == CardType.Spell && detailCardArt.sprite != null);
            bool hasCovenantShown = (data.cardType == CardType.Covenant && detailCardArt.sprite != null);
            if (!hasScrollShown && !hasCovenantShown)
            {
                Sprite elemFrame = MonsterArtLoader.GetCardFrameOverlaySprite(data.element);
                if (elemFrame != null)
                {
                    detailCardArt.sprite = elemFrame;
                    detailCardArt.color = Color.white;
                    detailCardArt.preserveAspect = true;
                }
                else
                {
                    detailCardArt.sprite = null;
                    detailCardArt.color = MonsterArtLoader.GetEnvironmentAtmosphereColor(data.element);
                }
            }

            Color textCol = hasScrollShown
                ? new Color(0.22f, 0.12f, 0.05f)
                : (hasCovenantShown ? new Color(0.96f, 0.80f, 0.38f) : MonsterArtLoader.GetElementColor(data.element));

            string detailPlaceholderText = $"#{data.id}\n{data.name}";
            Text numTxt;
            if (numTr == null)
            {
                numTxt = GameBootstrapper.CreateText(detailCardArt.transform, "DetailBigNumber", detailPlaceholderText, 16,
                    Color.Lerp(textCol, Color.white, 0.35f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(180, 100));
                numTxt.fontStyle = FontStyle.Bold;
                numTxt.resizeTextForBestFit = true;
                numTxt.resizeTextMinSize = 10;
                numTxt.resizeTextMaxSize = 16;
                numTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
                numTxt.verticalOverflow = VerticalWrapMode.Truncate;
                numTxt.raycastTarget = false;
                GameBootstrapper.AddTextShadow(numTxt);
            }
            else
            {
                numTr.gameObject.SetActive(true);
                numTxt = numTr.GetComponent<Text>();
                if (numTxt != null)
                {
                    numTxt.text = detailPlaceholderText;
                    numTxt.color = Color.Lerp(textCol, Color.white, 0.35f);
                    numTxt.fontSize = 16;
                    numTxt.resizeTextForBestFit = true;
                    numTxt.resizeTextMinSize = 10;
                    numTxt.resizeTextMaxSize = 16;
                    numTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
                    numTxt.verticalOverflow = VerticalWrapMode.Truncate;
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
                detailDescText.supportRichText = true;
                detailDescText.text = FormatMonsterAbilityRichText(data, instance);
            }

            if (detailStatusText != null)
            {
                detailStatusText.supportRichText = true;
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
                            var st1 = CombatSystem.Instance.GetAbilityState(gm.humanPlayer, gm.aiPlayer, instance.currentSlot.slotIndex, 0, out int c1, out string _);
                            string tag1 = st1 == CombatSystem.AbilityReadyState.Ready
                                ? $"<color=#2EE6FF>🔵 Skill 1 Ready ({c1}💧)</color>"
                                : (st1 == CombatSystem.AbilityReadyState.AlreadyUsed ? "<color=#78B890>🔵 Skill 1 Used ✓</color>" : $"<color=#8CA8C8>🔵 Skill 1 ({c1}💧)</color>");

                            if (instance.ActiveSkillCount >= 2)
                            {
                                var st2 = CombatSystem.Instance.GetAbilityState(gm.humanPlayer, gm.aiPlayer, instance.currentSlot.slotIndex, 1, out int c2, out string _);
                                string tag2 = st2 == CombatSystem.AbilityReadyState.Ready
                                    ? $"<color=#FF5263>🔴 Skill 2 Ready ({c2}💧)</color>"
                                    : (st2 == CombatSystem.AbilityReadyState.AlreadyUsed ? "<color=#78B890>🔴 Skill 2 Used ✓</color>" : $"<color=#C88C94>🔴 Skill 2 ({c2}💧)</color>");
                                detailStatusText.text = $"{atkStatus}\n{tag1}   |   {tag2}";
                                detailStatusText.color = (st1 == CombatSystem.AbilityReadyState.Ready || st2 == CombatSystem.AbilityReadyState.Ready)
                                    ? new Color(0.88f, 0.96f, 1f)
                                    : statusCol;
                            }
                            else
                            {
                                detailStatusText.text = $"{atkStatus}\n{tag1}";
                                detailStatusText.color = (st1 == CombatSystem.AbilityReadyState.Ready) ? new Color(0.35f, 1f, 0.75f) : statusCol;
                            }
                        }
                        else
                        {
                            detailStatusText.text = atkStatus;
                            detailStatusText.color = statusCol;
                        }
                    }
                    else
                    {
                        string enemyBaseStatus;
                        Color enemyStatusCol;
                        if (instance.frozenTurns > 0)
                        {
                            enemyBaseStatus = $"❄️ Frozen Enemy ({instance.frozenTurns}T)";
                            enemyStatusCol = new Color(0.55f, 0.90f, 1f);
                        }
                        else if (!instance.canAttack && gm != null && instance.summonedTurn == gm.currentTurn)
                        {
                            enemyBaseStatus = "💤 Newly summoned enemy (Sleeping this turn)";
                            enemyStatusCol = new Color(0.75f, 0.78f, 0.95f);
                        }
                        else
                        {
                            enemyBaseStatus = "🎯 Hostile enemy on the battlefield";
                            enemyStatusCol = new Color(1f, 0.50f, 0.50f);
                        }

                        if (instance.HasActiveSkill && CombatSystem.Instance != null && instance.currentSlot != null && gm != null)
                        {
                            var st1 = CombatSystem.Instance.GetAbilityState(gm.aiPlayer, gm.humanPlayer, instance.currentSlot.slotIndex, 0, out int c1, out string _);
                            string sName1 = CombatSystem.GetShortSkillName(instance.actives[0]);
                            string tag1 = st1 == CombatSystem.AbilityReadyState.AlreadyUsed
                                ? $"<color=#65FF9A>🔵 {sName1}: USED ✓</color>"
                                : $"<color=#2EE6FF>🔵 {sName1} ({c1}💧)</color>";

                            if (instance.ActiveSkillCount >= 2)
                            {
                                var st2 = CombatSystem.Instance.GetAbilityState(gm.aiPlayer, gm.humanPlayer, instance.currentSlot.slotIndex, 1, out int c2, out string _);
                                string sName2 = CombatSystem.GetShortSkillName(instance.actives[1]);
                                string tag2 = st2 == CombatSystem.AbilityReadyState.AlreadyUsed
                                    ? $"<color=#65FF9A>🔴 {sName2}: USED ✓</color>"
                                    : $"<color=#FF5263>🔴 {sName2} ({c2}💧)</color>";
                                detailStatusText.text = $"{enemyBaseStatus}\n{tag1}   |   {tag2}";
                            }
                            else
                            {
                                detailStatusText.text = $"{enemyBaseStatus}\n{tag1}";
                            }
                            detailStatusText.color = enemyStatusCol;
                        }
                        else
                        {
                            detailStatusText.text = enemyBaseStatus;
                            detailStatusText.color = enemyStatusCol;
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
            if (detailAtkText != null) detailAtkText.text = (data.cardType == CardType.Environment) ? "∞" : data.atk.ToString();
            if (detailDefText != null) detailDefText.text = (data.cardType == CardType.Environment) ? "∞" : data.def.ToString();

            string typeName = data.cardType switch
            {
                CardType.Spell => "Magic Spell 🔮",
                CardType.Environment => "Shared Environment 🌍",
                CardType.Leader => "Commander Spirit 👑",
                _ => "Monster Card 🐉"
            };

            if (detailNameText != null)
            {
                if (data.cardType == CardType.Leader)
                {
                    detailNameText.text = MonsterArtLoader.FormatSpiritTitle(data);
                    detailNameText.color = Color.Lerp(MonsterArtLoader.GetSpiritCategoryColor(data, true), Color.white, 0.48f);
                }
                else
                {
                    detailNameText.text = data.cardType == CardType.Environment
                        ? data.name
                        : $"Card #{data.name}";
                    detailNameText.color = new Color(0.90f, 0.95f, 1f);
                }
            }

            var gmDetail = GameManager.Instance;
            int effDetailCost = (gmDetail != null && gmDetail.humanPlayer != null) ? gmDetail.humanPlayer.GetEffectiveCardCost(data) : data.cost;
            if (detailCostText != null) detailCostText.text = effDetailCost.ToString();

            if (detailMetaText != null)
            {
                if (data.cardType == CardType.Leader)
                {
                    string tribePart = string.IsNullOrEmpty(data.tribe) ? "Universal Clan" : $"{MonsterArtLoader.GetTribeEmoji(data.tribe)} {data.tribe} Clan";
                    string costSuffix = effDetailCost < data.cost ? $"{effDetailCost} Mana (Base: {data.cost})" : $"{data.cost} Mana";
                    detailMetaText.text = $"👑 {tribePart} • Commander Spirit • Cost: {costSuffix}";
                    detailMetaText.color = MonsterArtLoader.GetSpiritCategoryColor(data, true);
                }
                else
                {
                    string elemEmoji = MonsterArtLoader.GetElementEmoji(data.element);
                    string tribePart = string.IsNullOrEmpty(data.tribe) ? "" : $" • {MonsterArtLoader.GetTribeEmoji(data.tribe)} {data.tribe}";
                    string costSuffix = effDetailCost < data.cost ? $"{effDetailCost} Mana (Base: {data.cost})" : $"{data.cost} Mana";
                    detailMetaText.text = $"{elemEmoji} {data.element}{tribePart} • {typeName} • Cost: {costSuffix}";
                    detailMetaText.color = MonsterArtLoader.GetElementColor(data.element);
                }
            }

            if (detailStatsText != null)
            {
                if (data.cardType == CardType.Monster)
                {
                    detailStatsText.text = $"Base ATK: {data.atk}  |  Base DEF: {data.def}";
                }
                else if (data.cardType == CardType.Spell)
                {
                    SpellDatabase.NormalizeSpellMetadata(data);
                    string targetDesc = data.spellTargetMode switch
                    {
                        SpellTargetMode.Enemy => "Targeted Enemy",
                        SpellTargetMode.Ally => "Targeted Ally",
                        SpellTargetMode.Any => "Any Monster",
                        SpellTargetMode.EmptySlot => "Empty Allied Slot",
                        SpellTargetMode.AllyToEmptySlot => "Ally -> Empty Slot",
                        _ => "Instant Global"
                    };
                    detailStatsText.text = $"Spell #{data.id} ({targetDesc})";
                }
                else if (data.cardType == CardType.Leader)
                {
                    detailStatsText.text = "Circular Spirit Altar • Passive Aura";
                }
                else
                {
                    if (data is EnvironmentCardData ecd && GameManager.Instance != null && GameManager.Instance.activeEnvironment == ecd)
                    {
                        detailStatsText.text = $"Active Level {ecd.level} • Permanent Climate (∞) • Level 1-3 Escalation";
                    }
                    else
                    {
                        detailStatsText.text = "Permanent Battlefield Climate (∞) • Boundless Stats (ATK: ∞ / DEF: ∞)";
                    }
                }
            }

            if (detailDescText != null)
            {
                detailDescText.supportRichText = true;
                if (data.cardType == CardType.Monster)
                {
                    detailDescText.text = FormatMonsterAbilityRichText(data, null);
                }
                else if (data.cardType == CardType.Spell)
                {
                    string desc = string.IsNullOrEmpty(data.description)
                        ? "Tactical magic spell with immediate battlefield effects."
                        : SanitizeAbilityText(data.description);
                    detailDescText.text = $"<color=#D29BFF><b>🔮 SPELL EFFECT:</b></color>\n<color=#FFFFFF>{desc}</color>";
                }
                else if (data.cardType == CardType.Leader)
                {
                    string desc = string.IsNullOrEmpty(data.description)
                        ? "Empowers allied clan monsters on the battlefield with passive aura bonuses."
                        : SanitizeAbilityText(data.description);
                    detailDescText.text = $"<color=#FFD866><b>👑 COMMANDER AURA:</b></color>\n<color=#FFFFFF>{desc}</color>";
                }
                else if (data.cardType == CardType.Environment)
                {
                    string elemName = EnvironmentCardData.NormalizeElement(data.element);
                    int lvl = 1;
                    if (data is EnvironmentCardData ed) lvl = Mathf.Clamp(ed.level, 1, 3);
                    else if (GameManager.Instance != null && GameManager.Instance.activeEnvironment != null && GameManager.Instance.activeEnvironment.element == data.element)
                        lvl = GameManager.Instance.activeEnvironment.level;

                    if (EnvironmentCardData.TryGetProfile(elemName, out var prof))
                    {
                        int durTurns = lvl == 1 ? 10 : (lvl == 2 ? 6 : 3);
                        detailDescText.text = $"<color=#58E085><b>🌍 [SHARED {elemName.ToUpper()} ARENA — LEVEL {lvl} ({durTurns}T)]:</b></color>\n" +
                            $"<color=#FFD866>• Bespoke Arena Rules:</color> <color=#FFFFFF>{prof.perkDesc}</color>\n" +
                            $"<color=#8B949E><i>Duration: Lv1=10T, Lv2=6T, Lv3=3T. Replay same element to upgrade, or play another element to overwrite!</i></color>";
                    }
                    else
                    {
                        string desc = string.IsNullOrEmpty(data.description)
                            ? "Alters the battlefield climate, buffing aligned elements."
                            : SanitizeAbilityText(data.description);
                        detailDescText.text = $"<color=#58E085><b>🌍 ENVIRONMENT EFFECT:</b></color>\n<color=#FFFFFF>{desc}</color>";
                    }
                }
                else
                {
                    string desc = string.IsNullOrEmpty(data.description)
                        ? $"Strategic {typeName} card."
                        : SanitizeAbilityText(data.description);
                    detailDescText.text = $"<color=#E0E8F0><b>📜 EFFECT:</b></color>\n<color=#FFFFFF>{desc}</color>";
                }
            }

            if (detailStatusText != null)
            {
                var gm = GameManager.Instance;
                bool canAfford = (gm != null && gm.humanPlayer.currentMana >= effDetailCost);
                if (data.cardType == CardType.Spell && gm != null)
                {
                    bool spellReady = gm.IsSpellPlayable(gm.humanPlayer, gm.aiPlayer, data, out string spellReason);
                    detailStatusText.text = spellReady
                        ? "✨ Ready to cast (Auto-targets sole target or select on board)."
                        : $"⚠️ {spellReason}";
                    detailStatusText.color = spellReady ? new Color(0.2f, 1f, 0.5f) : new Color(1f, 0.45f, 0.45f);
                }
                else
                {
                    detailStatusText.text = canAfford
                        ? "✨ Ready to play (Enough Mana available)."
                        : "⚠️ Insufficient Mana to play this card.";
                    detailStatusText.color = canAfford ? new Color(0.2f, 1f, 0.5f) : new Color(1f, 0.45f, 0.45f);
                }
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
                    LogCombatMessage($"👑 Selected Commander Spirit: [{MonsterArtLoader.FormatSpiritTitle(card)}] (Cost: {card.cost}) — Drag to Circular Spirit Altar or click 'Summon Spirit'.");
                }
            }

            UpdatePlayCardButtonState();

            if (sacrificeButton != null)
            {
                var gm = GameManager.Instance;
                sacrificeButton.interactable = gm != null && gm.isPlayerTurn && !gm.humanPlayer.hasSacrificedThisTurn && gm.humanPlayer.denyDrawSacTurns <= 0;
            }
        }

        public void DeselectHandCards()
        {
            CancelPendingSpellTargeting();
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
            CancelPendingAbilityTargeting();
            CancelPendingSpellTargeting();
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

            if (IsSelectingSpellTarget)
            {
                playCardButton.interactable = true;
                if (txt != null)
                {
                    txt.text = pendingSpellTargetMode switch
                    {
                        SpellTargetMode.Enemy => "🎯 Click Enemy Target (Cancel)",
                        SpellTargetMode.Ally => "✨ Click Ally Target (Cancel)",
                        SpellTargetMode.Any => "🔄 Click Any Monster (Cancel)",
                        SpellTargetMode.EmptySlot => "💀 Click Empty Slot (Cancel)",
                        SpellTargetMode.AllyToEmptySlot => pendingSpellPrimarySelected
                            ? "📍 Click Empty Slot (Cancel)"
                            : "🔄 Click Ally to Move (Cancel)",
                        _ => "Cancel Targeting"
                    };
                }
                return;
            }

            if (IsSelectingAbilityTarget)
            {
                playCardButton.interactable = true;
                if (txt != null)
                {
                    txt.text = pendingAbilityTargetMode == CombatSystem.AbilityTargetMode.SingleEnemy
                        ? "🎯 Click Enemy Target (Cancel)"
                        : "✨ Click Ally Target (Cancel)";
                }
                return;
            }

            if (selectedCardForAction != null)
            {
                bool isInstantPlayType = selectedCardForAction.cardType == CardType.Spell ||
                                         selectedCardForAction.cardType == CardType.Environment ||
                                         selectedCardForAction.cardType == CardType.Leader ||
                                         selectedCardForAction.cardType == CardType.Covenant;
                int effCost = gm != null && gm.humanPlayer != null ? gm.humanPlayer.GetEffectiveCardCost(selectedCardForAction) : selectedCardForAction.cost;
                bool canPlaySelected = gm != null && gm.isPlayerTurn && isInstantPlayType && effCost <= gm.humanPlayer.currentMana;
                string spellBlock = null;
                if (canPlaySelected && selectedCardForAction.cardType == CardType.Spell)
                {
                    canPlaySelected = gm.IsSpellPlayable(gm.humanPlayer, gm.aiPlayer, selectedCardForAction, out spellBlock);
                }

                playCardButton.interactable = canPlaySelected;
                if (txt != null)
                {
                    if (selectedCardForAction.cardType == CardType.Spell)
                        txt.text = !string.IsNullOrEmpty(spellBlock) ? "⚠️ No Valid Target" : $"Cast Spell ({effCost}💧)";
                    else if (selectedCardForAction.cardType == CardType.Environment)
                        txt.text = $"Activate Environment ({effCost}💧)";
                    else if (selectedCardForAction.cardType == CardType.Leader)
                        txt.text = $"Summon Spirit ({effCost}💧)";
                    else if (selectedCardForAction.cardType == CardType.Covenant)
                        txt.text = $"📜 Activate Covenant ({effCost}💧)";
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
                            CombatSystem.AbilityReadyState.NotEnoughPower => $"⚡ Need {cost}⚡ Aether",
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

            // 0A. If currently in Spell Target Selection Mode, validate & execute or advance step!
            if (IsSelectingSpellTarget)
            {
                HandleSpellTargetSlotClick(slotIndex, isPlayerSide, slot);
                return;
            }

            // 0B. If currently in Ability Target Selection Mode, clicking a valid monster slot fires the ability on that slot!
            if (IsSelectingAbilityTarget)
            {
                if (pendingAbilityTargetMode == CombatSystem.AbilityTargetMode.SingleEnemy)
                {
                    if (!isPlayerSide && slot != null && slot.IsOccupied && slot.currentMonster != null && slot.currentMonster.IsAlive)
                    {
                        string actText = "";
                        var srcSlot = (pendingAbilitySourceSlot >= 0 && gm.humanPlayer.slots != null && pendingAbilitySourceSlot < gm.humanPlayer.slots.Length)
                            ? gm.humanPlayer.slots[pendingAbilitySourceSlot] : null;
                        if (srcSlot != null && srcSlot.IsOccupied && srcSlot.currentMonster != null &&
                            pendingAbilitySkillIndex >= 0 && pendingAbilitySkillIndex < srcSlot.currentMonster.actives.Count)
                        {
                            actText = srcSlot.currentMonster.actives[pendingAbilitySkillIndex];
                        }
                        if (CombatSystem.Instance != null && !CombatSystem.Instance.SkillTargetsCanHitStealth(actText) &&
                            slot.currentMonster.HasPassiveSubstr("Night Stealth") != null)
                        {
                            MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                            LogCombatMessage("🌑 That enemy monster is hidden in Night Stealth and cannot be targeted by this skill!");
                            return;
                        }
                        int srcSlotIdx = pendingAbilitySourceSlot;
                        int sIdx = pendingAbilitySkillIndex;
                        CancelPendingAbilityTargeting();
                        TryActivateBoardAbility(srcSlotIdx, sIdx, slotIndex);
                        return;
                    }
                    else
                    {
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                        LogCombatMessage("🎯 Please click a highlighted ENEMY monster slot to hit with your ability, or right-click / press Cancel.");
                        return;
                    }
                }
                else if (pendingAbilityTargetMode == CombatSystem.AbilityTargetMode.SingleAlly)
                {
                    if (isPlayerSide && slot != null && slot.IsOccupied && slot.currentMonster != null && slot.currentMonster.IsAlive)
                    {
                        int srcSlotIdx = pendingAbilitySourceSlot;
                        int sIdx = pendingAbilitySkillIndex;
                        CancelPendingAbilityTargeting();
                        TryActivateBoardAbility(srcSlotIdx, sIdx, slotIndex);
                        return;
                    }
                    else
                    {
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                        LogCombatMessage("✨ Please click a highlighted ALLIED monster slot to buff with your ability, or right-click / press Cancel.");
                        return;
                    }
                }
            }

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
                string reason = !string.IsNullOrEmpty(gm.humanPlayer.lastSummonFailReason)
                    ? gm.humanPlayer.lastSummonFailReason
                    : $"Cannot summon monster to Slot {slotIndex + 1} (Check Mana & slot availability).";
                LogCombatMessage($"❌ {reason}");
                ShowBanner($"⚠️ {reason}", new Color(0.95f, 0.35f, 0.35f));
            }
        }

        public void OnPlayerMonsterSummoned(CardData chosenCard, int slotIndex, Vector3? explicitStartPos = null, bool isQuickDrop = false)
        {
            var gm = GameManager.Instance;
            if (gm == null || chosenCard == null) return;

            Vector3 startPos = explicitStartPos ?? GetPlayerHandCardWorldPos(chosenCard);

            if (MonsterCardGame.Audio.SoundManager.Instance != null)
                MonsterCardGame.Audio.SoundManager.Instance.PlayMonsterSummon(chosenCard);

            LogCombatMessage($"✅ Summoned [{chosenCard.name}] to Slot {slotIndex}.", chosenCard);
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
                        LogCombatMessage($"⚡ [{chosenCard.name}] Aura Awakened! Click its glowing ⚡ Flash Sigil (or Double-Click card) to unleash ({cost})!", chosenCard);
                    }
                    else
                    {
                        LogCombatMessage($"✨ [{chosenCard.name}] has Special Ability ({cost}): {reason}", chosenCard);
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

        public void OnSlotAbilityButtonClicked(int slotIndex, int skillIndex = 0)
        {
            // If clicking the exact same ability button while already selecting a target, toggle/cancel targeting
            if (IsSelectingAbilityTarget && pendingAbilitySourceSlot == slotIndex && pendingAbilitySkillIndex == skillIndex)
            {
                CancelPendingAbilityTargeting(silent: false);
                UpdatePlayCardButtonState();
                return;
            }

            CancelPendingAbilityTargeting();
            CancelPendingSpellTargeting();
            DeselectHandCards();
            selectedBoardSlotIndex = slotIndex;
            if (playerSlotViews != null)
            {
                for (int i = 0; i < playerSlotViews.Length; i++)
                {
                    if (playerSlotViews[i] != null) playerSlotViews[i].SetBoardSelected(i == selectedBoardSlotIndex);
                }
            }
            TryActivateBoardAbility(slotIndex, skillIndex, -1);
        }

        public void OnSlotSigilClickedNonActivate(int slotIndex, bool isPlayerSide, int skillIndex = 0)
        {
            // If player is currently choosing a target for an ability or spell, clicking a sigil on a target monster selects that monster!
            if (IsSelectingAnyTarget)
            {
                OnSlotClicked(slotIndex, isPlayerSide);
                return;
            }

            OnSlotClicked(slotIndex, isPlayerSide);

            var gm = GameManager.Instance;
            if (gm == null || CombatSystem.Instance == null) return;
            var owner = isPlayerSide ? gm.humanPlayer : gm.aiPlayer;
            var opp = isPlayerSide ? gm.aiPlayer : gm.humanPlayer;
            var slot = (slotIndex >= 0 && slotIndex < owner.slots.Length) ? owner.slots[slotIndex] : null;
            var m = (slot != null && slot.IsOccupied) ? slot.currentMonster : null;
            if (m != null && m.HasActiveSkillAt(skillIndex))
            {
                string actText = m.actives[skillIndex];
                string badge = skillIndex == 0 ? "🔵 [BLUE SIGIL]" : "🔴 [RED SIGIL]";
                var st = CombatSystem.Instance.GetAbilityState(owner, opp, slotIndex, skillIndex, out int cost, out string _);
                string statusTag = st == CombatSystem.AbilityReadyState.AlreadyUsed ? "USED THIS TURN ✓" : $"Cost: {cost}💧";
                LogCombatMessage($"💀 Enemy Monster #{m.data.name} — {badge} ({statusTag}): {actText}", m.data);
            }
        }

        public void NotifyAIMonsterSkillActivated(PlayerState caster, int slotIndex, MonsterInstance monster, int skillIndex, string actText, string logMsg)
        {
            if (monster == null || monster.data == null) return;

            var gm = GameManager.Instance;
            bool isPlayerSide = (gm != null && caster == gm.humanPlayer);
            string colorBadge = skillIndex == 0 ? "🔵 [BLUE SIGIL]" : "🔴 [RED SIGIL]";
            string shortSkillName = CombatSystem.GetShortSkillName(actText);

            LogCombatMessage(logMsg, monster.data);

            var canvas = GetComponentInParent<Canvas>();
            var sv = GetSlotView(slotIndex, isPlayerSide);
            if (sv != null)
            {
                sv.PlaySkillCastHighlight(skillIndex, shortSkillName);
                if (canvas != null)
                {
                    Color waveCol = skillIndex == 0 ? SlotView.Skill1BlueColor : SlotView.Skill2RedColor;
                    StartCoroutine(BattleAnimations.AnimateAbilityShockwave(canvas.transform, sv.transform, waveCol));
                }
            }

            string sidePrefix = isPlayerSide ? "" : "💀 ENEMY ";
            ShowAbilityBanner(monster.data, $"{sidePrefix}#{monster.data.name} {colorBadge} {actText}");
            ShowMonsterDetails(monster.data, monster, isPlayerSide);
        }

        public void TryActivateBoardAbility(int slotIndex, int skillIndex = 0, int explicitTargetSlot = -1)
        {
            var gm = GameManager.Instance;
            if (gm == null || CombatSystem.Instance == null) return;

            var slot = (slotIndex >= 0 && slotIndex < gm.humanPlayer.slots.Length) ? gm.humanPlayer.slots[slotIndex] : null;
            var monster = (slot != null && slot.IsOccupied) ? slot.currentMonster : null;
            if (monster == null || !monster.HasActiveSkill) return;

            if (skillIndex < 0 || skillIndex >= monster.ActiveSkillCount)
            {
                skillIndex = 0;
            }
            string actText = monster.actives[skillIndex];
            string shortSkillName = CombatSystem.GetShortSkillName(actText);
            string colorBadge = skillIndex == 0 ? "🔵 [BLUE SIGIL]" : "🔴 [RED SIGIL]";

            // 1. Check if the chosen skill is ready before entering target selection or executing
            var readyState = CombatSystem.Instance.GetAbilityState(gm.humanPlayer, gm.aiPlayer, slotIndex, skillIndex, out int skillCost, out string notReadyReason);
            if (readyState != CombatSystem.AbilityReadyState.Ready)
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                LogCombatMessage(notReadyReason);
                ShowMonsterDetails(monster.data, monster, true);
                UpdatePlayCardButtonState();
                return;
            }

            // 2. If this skill targets a specific monster (SingleEnemy or SingleAlly), auto-target if exactly 1 valid targetable monster exists (excluding Night Stealth unless ignore-stealth); otherwise enter Target Selection Mode!
            var targetMode = CombatSystem.Instance.GetSkillTargetMode(actText, gm.humanPlayer, gm.aiPlayer);
            if (explicitTargetSlot < 0 && targetMode != CombatSystem.AbilityTargetMode.None)
            {
                int validTargetCount = 0;
                int soleValidSlot = -1;

                if (targetMode == CombatSystem.AbilityTargetMode.SingleEnemy)
                {
                    var validList = CombatSystem.Instance.GetTargetableEnemySlots(gm.aiPlayer, actText);
                    validTargetCount = validList.Count;
                    if (validTargetCount == 1) soleValidSlot = validList[0].slotIndex;
                }
                else
                {
                    if (gm.humanPlayer != null && gm.humanPlayer.slots != null)
                    {
                        for (int i = 0; i < gm.humanPlayer.slots.Length; i++)
                        {
                            var ts = gm.humanPlayer.slots[i];
                            if (ts != null && ts.IsOccupied && ts.currentMonster != null && ts.currentMonster.IsAlive)
                            {
                                validTargetCount++;
                                soleValidSlot = i;
                            }
                        }
                    }
                }

                if (validTargetCount == 0)
                {
                    MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                    string sideWord = targetMode == CombatSystem.AbilityTargetMode.SingleEnemy
                        ? "targetable enemy (Night Stealth blocks targeting)"
                        : "allied";
                    LogCombatMessage($"🎯 No valid {sideWord} monster on the board for [{shortSkillName}]!");
                    ShowMonsterDetails(monster.data, monster, true);
                    UpdatePlayCardButtonState();
                    return;
                }
                else if (validTargetCount == 1)
                {
                    // Auto-target when only 1 valid target exists on the board
                    explicitTargetSlot = soleValidSlot;
                }
                else
                {
                    pendingAbilitySourceSlot = slotIndex;
                    pendingAbilitySkillIndex = skillIndex;
                    pendingAbilityTargetMode = targetMode;
                    RefreshAbilityTargetHighlights();
                    ShowMonsterDetails(monster.data, monster, true);
                    UpdatePlayCardButtonState();

                    if (targetMode == CombatSystem.AbilityTargetMode.SingleEnemy)
                    {
                        LogCombatMessage($"🎯 {colorBadge} [{shortSkillName}] Armed! Click any highlighted ENEMY monster on the board to strike it (Right-click/Esc to cancel).", monster.data);
                    }
                    else
                    {
                        LogCombatMessage($"✨ {colorBadge} [{shortSkillName}] Armed! Click any highlighted ALLIED monster on the board to buff it (Right-click/Esc to cancel).", monster.data);
                    }
                    return;
                }
            }

            // 3. Execute the ability on the chosen target (or immediately for AOE/Global skills)
            if (!CombatSystem.Instance.ActivateMonsterAbility(gm.humanPlayer, gm.aiPlayer, slotIndex, skillIndex, explicitTargetSlot, out string msg))
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                LogCombatMessage(msg);
                ShowMonsterDetails(monster.data, monster, true);
                UpdatePlayCardButtonState();
                return;
            }

            CancelPendingAbilityTargeting();

            if (MonsterCardGame.Audio.SoundManager.Instance != null)
                MonsterCardGame.Audio.SoundManager.Instance.PlayMonsterSkill(monster.data);

            LogCombatMessage(msg, monster.data);
            var canvas = GetComponentInParent<Canvas>();
            var sv = GetSlotView(slotIndex, true);
            if (sv != null)
            {
                sv.PlaySkillCastHighlight(skillIndex, shortSkillName);
                if (canvas != null)
                {
                    Color waveCol = skillIndex == 0 ? SlotView.Skill1BlueColor : SlotView.Skill2RedColor;
                    StartCoroutine(BattleAnimations.AnimateAbilityShockwave(canvas.transform, sv.transform, waveCol));
                }
            }
            ShowAbilityBanner(monster.data, $"{colorBadge} {actText}");

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

        private List<int> GetAliveAllySlotIndices(PlayerState p)
        {
            var res = new List<int>();
            if (p == null || p.slots == null) return res;
            for (int i = 0; i < p.slots.Length; i++)
            {
                if (p.slots[i] != null && p.slots[i].IsOccupied && p.slots[i].currentMonster != null && p.slots[i].currentMonster.IsAlive)
                    res.Add(i);
            }
            return res;
        }

        private List<int> GetEmptySlotIndices(PlayerState p)
        {
            var res = new List<int>();
            if (p == null || p.slots == null) return res;
            for (int i = 0; i < p.slots.Length; i++)
            {
                if (p.slots[i] != null && !p.slots[i].IsOccupied) res.Add(i);
            }
            return res;
        }

        private bool ExecuteSpellAndFinalize(CardData spellCard, Vector3 startPos, int primarySlot, int secondarySlot, bool primaryIsAlly)
        {
            var gm = GameManager.Instance;
            if (gm == null || spellCard == null) return false;
            if (!gm.CastSpell(gm.humanPlayer, gm.aiPlayer, spellCard, primarySlot, secondarySlot, primaryIsAlly))
                return false;

            CancelPendingSpellTargeting();
            LaunchPlayerUtilityCardFlight(spellCard, startPos);
            DeselectHandCards();
            UpdateUI();
            return true;
        }

        /// <summary>
        /// Initiates deterministic targeting for a player Spell:
        /// - Returns true ONLY if the spell immediately cast (Global or 1 sole valid target or valid explicit slot drop).
        /// - Returns false if unplayable OR if waiting for multi-target click on board (IsSelectingSpellTarget == true).
        /// </summary>
        public bool TryStartSpellCast(CardData spellCard, Vector3? explicitStartPos = null, int explicitSlot = -1, bool explicitIsAlly = false)
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn || spellCard == null || spellCard.cardType != CardType.Spell) return false;

            if (!gm.IsSpellPlayable(gm.humanPlayer, gm.aiPlayer, spellCard, out string reason))
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                LogCombatMessage($"❌ {reason}");
                return false;
            }

            CancelPendingAbilityTargeting();
            CancelPendingSpellTargeting();

            SpellDatabase.NormalizeSpellMetadata(spellCard);
            SpellTargetMode tmode = SpellDatabase.GetTargetMode(spellCard.id);
            Vector3 startPos = explicitStartPos ?? GetPlayerHandCardWorldPos(spellCard);

            var validEnemies = gm.GetValidSpellEnemies(gm.aiPlayer);
            var aliveAllies = GetAliveAllySlotIndices(gm.humanPlayer);
            var emptyAllies = GetEmptySlotIndices(gm.humanPlayer);

            if (explicitSlot >= 0 && explicitSlot < 6)
            {
                if (tmode == SpellTargetMode.Global)
                {
                    return ExecuteSpellAndFinalize(spellCard, startPos, -1, -1, false);
                }
                if (tmode == SpellTargetMode.Enemy)
                {
                    if (!explicitIsAlly && validEnemies.Exists(e => e.slotIndex == explicitSlot))
                        return ExecuteSpellAndFinalize(spellCard, startPos, explicitSlot, -1, false);
                    MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                    LogCombatMessage(!explicitIsAlly
                        ? "🌑 That enemy cannot be targeted (check Night Stealth or empty slot)!"
                        : "🎯 Drop onto a valid ENEMY monster to cast this spell!");
                    return false;
                }
                if (tmode == SpellTargetMode.Ally)
                {
                    if (explicitIsAlly && aliveAllies.Contains(explicitSlot))
                        return ExecuteSpellAndFinalize(spellCard, startPos, explicitSlot, -1, true);
                    MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                    LogCombatMessage("✨ Drop onto one of your ALLIED monsters to cast this spell!");
                    return false;
                }
                if (tmode == SpellTargetMode.Any)
                {
                    if (explicitIsAlly && aliveAllies.Contains(explicitSlot))
                        return ExecuteSpellAndFinalize(spellCard, startPos, explicitSlot, -1, true);
                    if (!explicitIsAlly && validEnemies.Exists(e => e.slotIndex == explicitSlot))
                        return ExecuteSpellAndFinalize(spellCard, startPos, explicitSlot, -1, false);
                    MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                    LogCombatMessage("🔄 Drop onto any valid Ally or non-Stealthed Enemy monster!");
                    return false;
                }
                if (tmode == SpellTargetMode.EmptySlot)
                {
                    if (explicitIsAlly && emptyAllies.Contains(explicitSlot))
                        return ExecuteSpellAndFinalize(spellCard, startPos, explicitSlot, -1, true);
                    MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                    LogCombatMessage("💀 Drop onto an EMPTY allied slot to revive there!");
                    return false;
                }
                if (tmode == SpellTargetMode.AllyToEmptySlot)
                {
                    if (explicitIsAlly && aliveAllies.Contains(explicitSlot))
                    {
                        if (emptyAllies.Count == 1)
                            return ExecuteSpellAndFinalize(spellCard, startPos, explicitSlot, emptyAllies[0], true);
                        pendingSpellCard = spellCard;
                        pendingSpellTargetMode = tmode;
                        pendingSpellPrimarySlot = explicitSlot;
                        pendingSpellPrimarySelected = true;
                        pendingSpellStartPos = startPos;
                        RefreshAbilityTargetHighlights();
                        UpdatePlayCardButtonState();
                        LogCombatMessage($"📍 [{spellCard.name}] Step 2/2: Click any highlighted EMPTY allied slot to relocate there!");
                        return false;
                    }
                    MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                    LogCombatMessage("🔄 Drop onto an ALLIED monster to relocate!");
                    return false;
                }
            }

            if (tmode == SpellTargetMode.Global)
            {
                return ExecuteSpellAndFinalize(spellCard, startPos, -1, -1, false);
            }

            if (tmode == SpellTargetMode.Enemy)
            {
                if (validEnemies.Count == 1)
                    return ExecuteSpellAndFinalize(spellCard, startPos, validEnemies[0].slotIndex, -1, false);

                pendingSpellCard = spellCard;
                pendingSpellTargetMode = tmode;
                pendingSpellPrimarySlot = -1;
                pendingSpellPrimarySelected = false;
                pendingSpellStartPos = startPos;
                RefreshAbilityTargetHighlights();
                UpdatePlayCardButtonState();
                LogCombatMessage($"🎯 [{spellCard.name}] Armed! Click any highlighted ENEMY monster to strike (Right-click/Esc to cancel).", spellCard);
                return false;
            }

            if (tmode == SpellTargetMode.Ally)
            {
                if (aliveAllies.Count == 1)
                    return ExecuteSpellAndFinalize(spellCard, startPos, aliveAllies[0], -1, true);

                pendingSpellCard = spellCard;
                pendingSpellTargetMode = tmode;
                pendingSpellPrimarySlot = -1;
                pendingSpellPrimarySelected = false;
                pendingSpellStartPos = startPos;
                RefreshAbilityTargetHighlights();
                UpdatePlayCardButtonState();
                LogCombatMessage($"✨ [{spellCard.name}] Armed! Click any highlighted ALLIED monster to buff (Right-click/Esc to cancel).", spellCard);
                return false;
            }

            if (tmode == SpellTargetMode.Any)
            {
                if (aliveAllies.Count + validEnemies.Count == 1)
                {
                    if (aliveAllies.Count == 1) return ExecuteSpellAndFinalize(spellCard, startPos, aliveAllies[0], -1, true);
                    return ExecuteSpellAndFinalize(spellCard, startPos, validEnemies[0].slotIndex, -1, false);
                }

                pendingSpellCard = spellCard;
                pendingSpellTargetMode = tmode;
                pendingSpellPrimarySlot = -1;
                pendingSpellPrimarySelected = false;
                pendingSpellStartPos = startPos;
                RefreshAbilityTargetHighlights();
                UpdatePlayCardButtonState();
                LogCombatMessage($"🔄 [{spellCard.name}] Armed! Click ANY highlighted Ally or Enemy monster to invert its ATK/DEF.", spellCard);
                return false;
            }

            if (tmode == SpellTargetMode.EmptySlot)
            {
                if (emptyAllies.Count == 1)
                    return ExecuteSpellAndFinalize(spellCard, startPos, emptyAllies[0], -1, true);

                pendingSpellCard = spellCard;
                pendingSpellTargetMode = tmode;
                pendingSpellPrimarySlot = -1;
                pendingSpellPrimarySelected = false;
                pendingSpellStartPos = startPos;
                RefreshAbilityTargetHighlights();
                UpdatePlayCardButtonState();
                LogCombatMessage($"💀 [{spellCard.name}] Armed! Click any highlighted EMPTY allied slot to revive into.", spellCard);
                return false;
            }

            // AllyToEmptySlot (#267)
            if (aliveAllies.Count == 1 && emptyAllies.Count == 1)
            {
                return ExecuteSpellAndFinalize(spellCard, startPos, aliveAllies[0], emptyAllies[0], true);
            }
            if (aliveAllies.Count == 1)
            {
                pendingSpellCard = spellCard;
                pendingSpellTargetMode = tmode;
                pendingSpellPrimarySlot = aliveAllies[0];
                pendingSpellPrimarySelected = true;
                pendingSpellStartPos = startPos;
                RefreshAbilityTargetHighlights();
                UpdatePlayCardButtonState();
                LogCombatMessage($"📍 [{spellCard.name}] Step 2/2: Click any highlighted EMPTY allied slot to relocate there.", spellCard);
                return false;
            }

            pendingSpellCard = spellCard;
            pendingSpellTargetMode = tmode;
            pendingSpellPrimarySlot = -1;
            pendingSpellPrimarySelected = false;
            pendingSpellStartPos = startPos;
            RefreshAbilityTargetHighlights();
            UpdatePlayCardButtonState();
            LogCombatMessage($"🔄 [{spellCard.name}] Step 1/2: Click the ALLIED monster you want to relocate.", spellCard);
            return false;
        }

        private void HandleSpellTargetSlotClick(int slotIndex, bool isPlayerSide, BoardSlot slot)
        {
            var gm = GameManager.Instance;
            if (gm == null || pendingSpellCard == null)
            {
                CancelPendingSpellTargeting();
                return;
            }

            var card = pendingSpellCard;
            var mode = pendingSpellTargetMode;
            Vector3 startPos = pendingSpellStartPos != Vector3.zero ? pendingSpellStartPos : GetPlayerHandCardWorldPos(card);

            if (mode == SpellTargetMode.Enemy)
            {
                if (!isPlayerSide && slot != null && slot.IsOccupied && slot.currentMonster != null && slot.currentMonster.IsAlive)
                {
                    if (slot.currentMonster.HasPassiveSubstr("Night Stealth") != null)
                    {
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                        LogCombatMessage("🌑 That enemy monster is hidden in Night Stealth and cannot be targeted!");
                        return;
                    }
                    ExecuteSpellAndFinalize(card, startPos, slotIndex, -1, false);
                    return;
                }
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                LogCombatMessage("🎯 Please click a highlighted ENEMY monster (or right-click / press Cancel).");
                return;
            }

            if (mode == SpellTargetMode.Ally)
            {
                if (isPlayerSide && slot != null && slot.IsOccupied && slot.currentMonster != null && slot.currentMonster.IsAlive)
                {
                    ExecuteSpellAndFinalize(card, startPos, slotIndex, -1, true);
                    return;
                }
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                LogCombatMessage("✨ Please click a highlighted ALLIED monster (or right-click / press Cancel).");
                return;
            }

            if (mode == SpellTargetMode.Any)
            {
                if (slot != null && slot.IsOccupied && slot.currentMonster != null && slot.currentMonster.IsAlive)
                {
                    if (!isPlayerSide && slot.currentMonster.HasPassiveSubstr("Night Stealth") != null)
                    {
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                        LogCombatMessage("🌑 That enemy monster is hidden in Night Stealth and cannot be targeted!");
                        return;
                    }
                    ExecuteSpellAndFinalize(card, startPos, slotIndex, -1, isPlayerSide);
                    return;
                }
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                LogCombatMessage("🔄 Please click any highlighted Ally or Enemy monster (or right-click / press Cancel).");
                return;
            }

            if (mode == SpellTargetMode.EmptySlot)
            {
                if (isPlayerSide && slot != null && !slot.IsOccupied)
                {
                    ExecuteSpellAndFinalize(card, startPos, slotIndex, -1, true);
                    return;
                }
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                LogCombatMessage("💀 Please click a highlighted EMPTY allied slot to revive into (or right-click / press Cancel).");
                return;
            }

            if (mode == SpellTargetMode.AllyToEmptySlot)
            {
                if (!pendingSpellPrimarySelected)
                {
                    if (isPlayerSide && slot != null && slot.IsOccupied && slot.currentMonster != null && slot.currentMonster.IsAlive)
                    {
                        var emptyAllies = GetEmptySlotIndices(gm.humanPlayer);
                        if (emptyAllies.Count == 1)
                        {
                            ExecuteSpellAndFinalize(card, startPos, slotIndex, emptyAllies[0], true);
                            return;
                        }
                        pendingSpellPrimarySlot = slotIndex;
                        pendingSpellPrimarySelected = true;
                        RefreshAbilityTargetHighlights();
                        UpdatePlayCardButtonState();
                        LogCombatMessage($"📍 [{card.name}] Step 2/2: Click any highlighted EMPTY allied slot to relocate [{slot.currentMonster.data.name}] there!");
                        return;
                    }
                    MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                    LogCombatMessage("🔄 Step 1/2: Please click one of your ALLIED monsters to relocate.");
                    return;
                }
                else
                {
                    if (isPlayerSide && slot != null && !slot.IsOccupied)
                    {
                        ExecuteSpellAndFinalize(card, startPos, pendingSpellPrimarySlot, slotIndex, true);
                        return;
                    }
                    MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                    LogCombatMessage("📍 Step 2/2: Please click a highlighted EMPTY allied slot to complete relocation (or right-click to cancel).");
                    return;
                }
            }
        }

        private void OnPlayCardClicked()
        {
            if (IsSelectingSpellTarget)
            {
                CancelPendingSpellTargeting(silent: false);
                UpdatePlayCardButtonState();
                return;
            }
            if (IsSelectingAbilityTarget)
            {
                CancelPendingAbilityTargeting(silent: false);
                UpdatePlayCardButtonState();
                return;
            }

            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn) return;

            if (selectedCardForAction != null)
            {
                var chosenCard = selectedCardForAction;
                Vector3 startPos = GetPlayerHandCardWorldPos(chosenCard);
                if (chosenCard.cardType == CardType.Spell)
                {
                    TryStartSpellCast(chosenCard, startPos);
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
                        if (playerSpiritController != null) playerSpiritController.TriggerSummonFlare();
                        LaunchPlayerUtilityCardFlight(chosenCard, startPos);
                        DeselectHandCards();
                        UpdateUI();
                    }
                }
                else if (chosenCard.cardType == CardType.Covenant)
                {
                    if (gm.PlayCovenantCard(gm.humanPlayer, chosenCard))
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
        public bool SacrificeCardDirectly(CardData card, Vector3? explicitStartPos = null)
        {
            var gm = GameManager.Instance;
            if (gm == null) return false;

            if (!gm.isPlayerTurn || gm.currentPhase != GamePhase.ActionPhase)
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                LogCombatMessage("⚠️ You can only sacrifice cards during your Action Phase!");
                return false;
            }

            if (gm.humanPlayer.hasSacrificedThisTurn)
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                LogCombatMessage("⚠️ You already sacrificed a card this turn (1 sacrifice per turn, unbounded Aether).");
                return false;
            }
            if (gm.humanPlayer.denyDrawSacTurns > 0)
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                LogCombatMessage("⚠️ Card sacrifice is currently blocked this turn.");
                return false;
            }

            if (card == null) return false;

            Vector3 startPos = explicitStartPos ?? GetPlayerHandCardWorldPos(card);

            if (gm.humanPlayer.SacrificeCard(card))
            {
                MonsterCardGame.Audio.SoundManager.Instance.PlayCardSacrifice();
                LogCombatMessage($"🔥 Sacrificed [{card.name}] via Right-Click! Gained +1 Aether & +1 Mana (Total Aether: {gm.humanPlayer.power}).", card);
                LaunchSacrificeFlight(card, startPos);
                DeselectHandCards();
                UpdateUI();
                gm.EvaluateAutoEndTurn();
                return true;
            }
            else
            {
                int cap = V2CardMechanicsEngine.GetPlayerManaCap(gm.humanPlayer);
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                if (gm.humanPlayer.power >= cap)
                    LogCombatMessage($"⚠️ Cannot sacrifice: Active Covenant [{gm.humanPlayer.activeCovenant?.name}] caps your Aether at {cap}!");
                else
                    LogCombatMessage("⚠️ Cannot sacrifice this card right now.");
                return false;
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

        public void ClearCombatLog()
        {
            logEntries.Clear();
            for (int i = 0; i < logEntryObjects.Count; i++)
            {
                if (logEntryObjects[i] != null) Destroy(logEntryObjects[i]);
            }
            logEntryObjects.Clear();

            if (combatLogText != null)
            {
                combatLogText.text = string.Empty;
            }
            if (combatLogScrollRect != null)
            {
                combatLogScrollRect.verticalNormalizedPosition = 1f;
            }
        }

        public void LogCombatMessage(string msg, CardData relatedCard = null)
        {
            if (string.IsNullOrEmpty(msg)) return;

            var gm = GameManager.Instance;
            string formattedMsg = msg;
            if (gm != null && gm.currentTurn > 0 && !msg.StartsWith("[T", StringComparison.Ordinal))
            {
                formattedMsg = $"[T{gm.currentTurn}] {msg}";
            }

            bool shouldAutoScroll = (combatLogScrollRect == null || logEntryObjects.Count <= 5 || combatLogScrollRect.verticalNormalizedPosition <= 0.18f);

            logEntries.Add(formattedMsg);
            while (logEntries.Count > MaxCombatLogEntries)
            {
                logEntries.RemoveAt(0);
            }

            if (combatLogContent != null)
            {
                if (combatLogText != null)
                {
                    combatLogText.text = string.Empty;
                    if (combatLogText.gameObject.activeSelf) combatLogText.gameObject.SetActive(false);
                }

                VerticalLayoutGroup vlg = combatLogContent.GetComponent<VerticalLayoutGroup>();
                if (vlg != null)
                {
                    vlg.childControlWidth = true;
                    vlg.childControlHeight = true;
                    vlg.childForceExpandWidth = true;
                    vlg.childForceExpandHeight = false;
                }

                while (logEntryObjects.Count >= MaxCombatLogEntries)
                {
                    GameObject oldest = logEntryObjects[0];
                    logEntryObjects.RemoveAt(0);
                    if (oldest != null) Destroy(oldest);
                }

                Font rowFont = (combatLogText != null && combatLogText.font != null)
                    ? combatLogText.font
                    : (detailDescText != null ? detailDescText.font : null);

                GameObject rowObj = new GameObject($"LogEntry_{logEntries.Count}", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
                rowObj.transform.SetParent(combatLogContent, false);

                Image rowBg = rowObj.GetComponent<Image>();
                bool isClickable = (relatedCard != null);
                rowBg.color = isClickable
                    ? new Color(0.11f, 0.14f, 0.21f, 0.90f)
                    : new Color(0.08f, 0.10f, 0.15f, 0.62f);

                HorizontalLayoutGroup hlg = rowObj.GetComponent<HorizontalLayoutGroup>();
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = true;
                hlg.childForceExpandHeight = false;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.spacing = 6f;
                hlg.padding = new RectOffset(6, 6, 4, 4);

                Color accentColor = Color.white;
                if (relatedCard != null)
                {
                    bool isPlayerCard = !msg.Contains("Enemy");
                    accentColor = (relatedCard.cardType == CardType.Leader)
                        ? MonsterArtLoader.GetSpiritCategoryColor(relatedCard, isPlayerCard)
                        : MonsterArtLoader.GetElementColor(relatedCard.element);

                    GameObject barObj = new GameObject("AccentBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                    barObj.transform.SetParent(rowObj.transform, false);
                    barObj.GetComponent<Image>().color = accentColor;
                    LayoutElement barLe = barObj.GetComponent<LayoutElement>();
                    barLe.minWidth = 4f;
                    barLe.preferredWidth = 4f;
                    barLe.flexibleWidth = 0f;
                    barLe.minHeight = 14f;
                    barLe.flexibleHeight = 1f;
                }

                GameObject textObj = new GameObject("LogText", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
                textObj.transform.SetParent(rowObj.transform, false);
                Text entryText = textObj.GetComponent<Text>();
                if (rowFont != null) entryText.font = rowFont;
                entryText.fontSize = 11;
                entryText.lineSpacing = 1.08f;
                entryText.alignment = TextAnchor.MiddleLeft;
                entryText.horizontalOverflow = HorizontalWrapMode.Wrap;
                entryText.verticalOverflow = VerticalWrapMode.Overflow;
                entryText.supportRichText = true;
                entryText.color = isClickable
                    ? new Color(0.92f, 0.95f, 1f, 1f)
                    : new Color(0.76f, 0.82f, 0.90f, 0.94f);
                entryText.text = formattedMsg;
                entryText.raycastTarget = false;

                LayoutElement textLe = textObj.GetComponent<LayoutElement>();
                textLe.flexibleWidth = 1f;
                textLe.minWidth = 40f;

                if (relatedCard != null)
                {
                    GameObject iconObj = new GameObject("InspectHint", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
                    iconObj.transform.SetParent(rowObj.transform, false);
                    Text hintTxt = iconObj.GetComponent<Text>();
                    if (rowFont != null) hintTxt.font = rowFont;
                    hintTxt.fontSize = 11;
                    hintTxt.alignment = TextAnchor.MiddleCenter;
                    hintTxt.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.9f);
                    hintTxt.text = "🔍";
                    hintTxt.raycastTarget = false;

                    LayoutElement iconLe = iconObj.GetComponent<LayoutElement>();
                    iconLe.minWidth = 18f;
                    iconLe.preferredWidth = 18f;
                    iconLe.flexibleWidth = 0f;

                    Button btn = rowObj.AddComponent<Button>();
                    btn.targetGraphic = rowBg;
                    ColorBlock cb = btn.colors;
                    cb.normalColor = Color.white;
                    cb.highlightedColor = new Color(1.18f, 1.24f, 1.38f, 1f);
                    cb.pressedColor = new Color(0.85f, 0.92f, 1.05f, 1f);
                    btn.colors = cb;

                    CardData capturedCard = relatedCard;
                    btn.onClick.AddListener(() =>
                    {
                        ShowCardDetails(capturedCard);
                        if (MonsterCardGame.Audio.SoundManager.Instance != null)
                            MonsterCardGame.Audio.SoundManager.Instance.PlayCardSelect();
                    });
                }

                logEntryObjects.Add(rowObj);

                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(combatLogContent);
                Canvas.ForceUpdateCanvases();

                if (shouldAutoScroll && combatLogScrollRect != null)
                {
                    combatLogScrollRect.verticalNormalizedPosition = 0f;
                    if (gameObject.activeInHierarchy)
                    {
                        StartCoroutine(ScrollCombatLogToBottomNextFrame());
                    }
                }
            }
            else if (combatLogText != null)
            {
                combatLogText.text = string.Join("\n", logEntries);
            }
            Debug.Log($"[LOG]: {formattedMsg}");
        }

        private System.Collections.IEnumerator ScrollCombatLogToBottomNextFrame()
        {
            yield return null;
            if (combatLogContent != null)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(combatLogContent);
                Canvas.ForceUpdateCanvases();
            }
            if (combatLogScrollRect != null)
            {
                combatLogScrollRect.verticalNormalizedPosition = 0f;
            }
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

        public void ShowEnvironmentBanner(EnvironmentCardData envData, string activatorName, string actionVerb = "activated")
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                StartCoroutine(BattleAnimations.AnimateEnvironmentShift(canvas, envData, activatorName, actionVerb));
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

            var gm = GameManager.Instance;
            string currentOppName = (gm != null && gm.aiPlayer != null) ? gm.aiPlayer.playerName : "Opponent";
            string nextOppName = (gm != null) ? gm.GetOpponentDisplayName() : "AI Opponent";

            if (isVictory)
            {
                if (gameOverTitleText != null)
                {
                    gameOverTitleText.text = "🏆 LEGENDARY VICTORY!";
                    gameOverTitleText.color = new Color(0.88f, 0.94f, 1f);
                }
                if (gameOverSubtitleText != null)
                {
                    gameOverSubtitleText.text = $"You crushed {currentOppName}!\n🔁 Next Opponent: {nextOppName}";
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
                    gameOverSubtitleText.text = $"{currentOppName} overpowered your forces... Regroup for the next battle!\n🔁 Next Opponent: {nextOppName}";
                    gameOverSubtitleText.color = new Color(1f, 0.8f, 0.8f);
                }
                if (gameOverModalOutline != null)
                {
                    gameOverModalOutline.effectColor = new Color(0.9f, 0.25f, 0.3f, 0.95f);
                }
            }

            if (restartGameButton != null)
            {
                var btnTxt = restartGameButton.GetComponentInChildren<Text>();
                if (btnTxt != null) btnTxt.text = $"🔁 NEXT BATTLE VS {nextOppName}";
            }

            if (gameOverStatsText != null)
            {
                string stats =
                    $"⏱️ Tactical Turns Elapsed: {turns}\n" +
                    $"❤️ Your Commander Remaining HP: {playerHp} / 500 HP\n" +
                    $"💀 Enemy Commander HP: {enemyHp} / 500 HP\n" +
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

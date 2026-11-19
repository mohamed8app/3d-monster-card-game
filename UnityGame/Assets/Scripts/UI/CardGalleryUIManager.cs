using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MonsterCardGame.Core;
using MonsterCardGame.Data;
using MonsterCardGame.Utils;

namespace MonsterCardGame.UI
{
    /// <summary>
    /// Full-Screen Main Menu & Comprehensive Card Gallery / Asset Inspector (All-English UI).
    /// Displays all cards (Monsters, Spells, Environments Lv1-3, Commander Spirits)
    /// with category/element/clan/completion filters and side-by-side Hand + Battlefield previews.
    /// Waits for player to click START GAME before starting the battle match.
    /// </summary>
    public class CardGalleryUIManager : MonoBehaviour
    {
        public static CardGalleryUIManager Instance { get; private set; }

        public enum TypeFilter
        {
            All,
            Monsters,
            Spells,
            Environments,
            Leaders
        }

        public enum ArtStatusFilter
        {
            All,
            HasArt,
            MissingArt,
            Has25D
        }

        public enum GalleryViewMode
        {
            BothHandAndField,
            HandOnly,
            FieldOnly
        }

        private GameObject battleScreenRoot;
        private GameObject mainMenuOverlay;
        private GameObject galleryOverlay;
        private Text mainMenuStatsText;
        private Text startBtnLabelText;
        private Text opponentStatusText;
        private Text playerDeckStatusText;

        // Filter state
        private TypeFilter currentTypeFilter = TypeFilter.All;
        private ArtStatusFilter currentStatusFilter = ArtStatusFilter.All;
        private GalleryViewMode currentViewMode = GalleryViewMode.BothHandAndField;
        private string currentElementFilter = "All";
        private string currentTribeFilter = "All";
        private int currentTierFilter = 0; // 0 = All, 1..7 = Tier
        private int currentPage = 0;

        // Cached master catalog & filtered list
        private readonly List<CardData> masterCatalog = new List<CardData>();
        private readonly List<CardData> filteredCards = new List<CardData>();
        private CardData selectedCard = null;

        // UI references in Gallery
        private Transform gridContainer;
        private Text headerStatsText;
        private Text pageIndicatorText;
        private Transform inspectorHandHolder;
        private Transform inspectorFieldHolder;
        private Text inspectorTitleText;
        private Text inspectorMetaText;
        private Text inspectorChecklistText;
        private Text inspectorAbilitiesText;

        private readonly List<ButtonHighlightPair> typeButtons = new List<ButtonHighlightPair>();
        private readonly List<ButtonHighlightPair> statusButtons = new List<ButtonHighlightPair>();
        private readonly List<ButtonHighlightPair> viewModeButtons = new List<ButtonHighlightPair>();
        private readonly List<ButtonHighlightPair> elementButtons = new List<ButtonHighlightPair>();
        private readonly List<ButtonHighlightPair> tribeButtons = new List<ButtonHighlightPair>();
        private readonly List<ButtonHighlightPair> tierButtons = new List<ButtonHighlightPair>();

        // Custom Deck Builder UI references & state
        private class DeckBuilderCardControl
        {
            public Text badgeText;
            public Button plusButton;
            public Button minusButton;
        }

        private GameObject deckBuilderOverlay;
        private readonly List<int> workingCustomDeckIds = new List<int>();
        private readonly Dictionary<int, DeckBuilderCardControl> deckBuilderControls = new Dictionary<int, DeckBuilderCardControl>();
        private Transform deckBuilderCatalogGrid;
        private Transform deckBuilderTrayList;
        private Text deckBuilderStatusText;
        private Text deckBuilderBreakdownText;
        private Text deckBuilderAvgCostText;
        private Text deckBuilderFilterCountText;
        private Text deckBuilderNoticeText;
        private Coroutine deckBuilderNoticeCoroutine;

        private TypeFilter deckBuilderTypeFilter = TypeFilter.Monsters;
        private string deckBuilderElementFilter = "All";
        private string deckBuilderTribeFilter = "All";
        private readonly List<ButtonHighlightPair> deckBuilderTypeButtons = new List<ButtonHighlightPair>();
        private readonly List<ButtonHighlightPair> deckBuilderElementButtons = new List<ButtonHighlightPair>();
        private readonly List<ButtonHighlightPair> deckBuilderTribeButtons = new List<ButtonHighlightPair>();

        private struct ButtonHighlightPair
        {
            public string key;
            public Image bg;
            public Outline outline;
            public Color baseColor;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            // Refresh catalog after GameManager has loaded the card database in Start()
            Invoke(nameof(DelayedInitialize), 0.05f);
        }

        private void DelayedInitialize()
        {
            RebuildMasterCatalog();
            RefreshMainMenuSummary();
            ApplyFiltersAndRefresh(0);
        }

        public static CardGalleryUIManager AttachToCanvas(Transform canvasTransform, Transform rightWingTopBar, GameObject screenRootObj = null)
        {
            var rootObj = new GameObject("CardGalleryUIManager");
            rootObj.transform.SetParent(canvasTransform, false);
            var rect = rootObj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            var mgr = rootObj.AddComponent<CardGalleryUIManager>();
            mgr.battleScreenRoot = screenRootObj;
            if (mgr.battleScreenRoot != null)
            {
                mgr.battleScreenRoot.SetActive(false);
            }

            mgr.BuildBattleTopBarButtons(rightWingTopBar);
            mgr.BuildGalleryOverlay(rootObj.transform);
            mgr.BuildMainMenuOverlay(rootObj.transform);
            mgr.BuildDeckBuilderOverlay(rootObj.transform);

            // Start on standalone Start Screen without any battle running in the background
            mgr.ShowMainMenu();
            return mgr;
        }

        private void BuildBattleTopBarButtons(Transform rightWingTopBar)
        {
            if (rightWingTopBar == null) return;

            GameBootstrapper.CreateBeveledButton(rightWingTopBar, "OpenGalleryTopBtn", "🎴 GALLERY",
                new Color(0.14f, 0.28f, 0.46f, 0.96f), new Color(0.92f, 0.97f, 1f),
                new Vector2(-118f, 0f), new Vector2(76f, 26f), 10,
                () => OpenGallery(TypeFilter.All, ArtStatusFilter.All));

            GameBootstrapper.CreateBeveledButton(rightWingTopBar, "OpenDeckBuilderTopBtn", "🛠️ DECK",
                new Color(0.42f, 0.24f, 0.12f, 0.96f), new Color(1f, 0.94f, 0.85f),
                new Vector2(0f, 0f), new Vector2(72f, 26f), 10,
                () => OpenDeckBuilder());

            GameBootstrapper.CreateBeveledButton(rightWingTopBar, "OpenMainMenuTopBtn", "🏠 MENU",
                new Color(0.22f, 0.18f, 0.34f, 0.96f), new Color(0.95f, 0.92f, 1f),
                new Vector2(118f, 0f), new Vector2(72f, 26f), 10,
                () => ShowMainMenu());
        }

        public void ShowMainMenu()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.CancelAutoEndTurn();
            }

            if (battleScreenRoot != null)
            {
                battleScreenRoot.SetActive(false);
            }

            RebuildMasterCatalog();
            RefreshMainMenuSummary();
            if (galleryOverlay != null) galleryOverlay.SetActive(false);
            if (deckBuilderOverlay != null) deckBuilderOverlay.SetActive(false);
            if (mainMenuOverlay != null)
            {
                mainMenuOverlay.SetActive(true);
                mainMenuOverlay.transform.SetAsLastSibling();
            }
        }

        public void StartOrResumeMatch()
        {
            if (mainMenuOverlay != null) mainMenuOverlay.SetActive(false);
            if (galleryOverlay != null) galleryOverlay.SetActive(false);
            if (deckBuilderOverlay != null) deckBuilderOverlay.SetActive(false);

            if (battleScreenRoot != null)
            {
                battleScreenRoot.SetActive(true);
            }

            var gm = GameManager.Instance;
            if (gm != null && !gm.isMatchStarted)
            {
                gm.StartGame();
            }

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.StartBattleArenaVisuals();
            }
        }

        public void OpenGallery(TypeFilter typeFilter = TypeFilter.All, ArtStatusFilter statusFilter = ArtStatusFilter.All)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.CancelAutoEndTurn();
            }

            if (battleScreenRoot != null)
            {
                battleScreenRoot.SetActive(false);
            }

            RebuildMasterCatalog();
            if (mainMenuOverlay != null) mainMenuOverlay.SetActive(false);
            if (deckBuilderOverlay != null) deckBuilderOverlay.SetActive(false);
            if (galleryOverlay != null)
            {
                galleryOverlay.SetActive(true);
                galleryOverlay.transform.SetAsLastSibling();
            }

            currentTypeFilter = typeFilter;
            currentStatusFilter = statusFilter;
            ApplyFiltersAndRefresh(0);
        }

        // =========================================================================
        // 1. MAIN MENU OVERLAY (Start Game + Card Gallery + Live Asset Report)
        // =========================================================================
        private void BuildMainMenuOverlay(Transform parent)
        {
            mainMenuOverlay = GameBootstrapper.CreatePanel(parent, "MainMenuOverlay",
                new Color(0.02f, 0.03f, 0.06f, 1f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Full-Screen Standalone Title Card (1840 x 1020)
            var centerCard = GameBootstrapper.CreateOrnatePanel(mainMenuOverlay.transform, "MainMenuCenterCard",
                new Color(0.04f, 0.06f, 0.11f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1840f, 1020f));
            var cardOutline = centerCard.AddComponent<Outline>();
            cardOutline.effectColor = new Color(0.38f, 0.64f, 0.92f, 0.85f);
            cardOutline.effectDistance = new Vector2(2f, 2f);

            // Title Banner
            var titleBanner = GameBootstrapper.CreateOrnatePanel(centerCard.transform, "TitleBanner",
                new Color(0.08f, 0.12f, 0.22f, 0.98f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(1680f, 96f));
            var titleTxt = GameBootstrapper.CreateText(titleBanner.transform, "TitleText",
                "⚔️ ASTRAL COMMANDERS • TACTICAL CARD ARENA ⚔️", 28,
                new Color(0.96f, 0.88f, 0.62f), TextAnchor.MiddleCenter, new Vector2(0f, 12f), new Vector2(1600f, 44f));
            titleTxt.fontStyle = FontStyle.Bold;
            GameBootstrapper.AddTextShadow(titleTxt);

            var subTitleTxt = GameBootstrapper.CreateText(titleBanner.transform, "SubTitleText",
                "10 Elements • 10 Clans • Tiered Environments • Commander Spirits", 15,
                new Color(0.76f, 0.88f, 0.98f), TextAnchor.MiddleCenter, new Vector2(0f, -22f), new Vector2(1600f, 28f));
            GameBootstrapper.AddTextShadow(subTitleTxt);

            // Primary Action Buttons Row
            var startBtn = GameBootstrapper.CreateBeveledButton(centerCard.transform, "BtnStartMatch",
                "⚔️ START BATTLE",
                new Color(0.14f, 0.44f, 0.24f, 0.98f), new Color(0.94f, 1f, 0.95f),
                new Vector2(-540f, 275f), new Vector2(490f, 64f), 19,
                () => StartOrResumeMatch());
            startBtnLabelText = startBtn.GetComponentInChildren<Text>();

            GameBootstrapper.CreateBeveledButton(centerCard.transform, "BtnOpenDeckBuilder",
                "🛠️ CUSTOM DECK BUILDER",
                new Color(0.48f, 0.28f, 0.12f, 0.98f), new Color(1f, 0.95f, 0.84f),
                new Vector2(0f, 275f), new Vector2(490f, 64f), 18,
                () => OpenDeckBuilder());

            GameBootstrapper.CreateBeveledButton(centerCard.transform, "BtnOpenGallery",
                "🎴 CARD GALLERY",
                new Color(0.14f, 0.30f, 0.56f, 0.98f), new Color(0.92f, 0.97f, 1f),
                new Vector2(540f, 275f), new Vector2(490f, 64f), 18,
                () => OpenGallery(TypeFilter.All, ArtStatusFilter.All));

            // Opponent Selection Row (Gora vs Navi)
            var oppBox = GameBootstrapper.CreateOrnatePanel(centerCard.transform, "OpponentSelectionPanel",
                new Color(0.06f, 0.09f, 0.16f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 202f), new Vector2(1180f, 48f));
            var oppBoxOutline = oppBox.AddComponent<Outline>();
            oppBoxOutline.effectColor = new Color(0.32f, 0.52f, 0.78f, 0.85f);

            GameBootstrapper.CreateBeveledButton(oppBox.transform, "BtnOppGora",
                "🏆 CHALLENGE GORA (Aggressive)",
                new Color(0.18f, 0.32f, 0.54f, 0.98f), Color.white,
                new Vector2(-360f, 0f), new Vector2(280f, 36f), 12,
                () => SetOpponentInMenu(GameManager.OpponentModel.Gora));

            GameBootstrapper.CreateBeveledButton(oppBox.transform, "BtnOppNavi",
                "🟣 CHALLENGE NAVI (Tactical)",
                new Color(0.42f, 0.18f, 0.44f, 0.98f), Color.white,
                new Vector2(360f, 0f), new Vector2(280f, 36f), 12,
                () => SetOpponentInMenu(GameManager.OpponentModel.Navi));

            opponentStatusText = GameBootstrapper.CreateText(oppBox.transform, "OpponentStatusText",
                "Active Opponent: Gora | Auto-alternation enabled per match", 12,
                new Color(0.96f, 0.90f, 0.72f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(420f, 36f));
            opponentStatusText.fontStyle = FontStyle.Bold;
            GameBootstrapper.AddTextShadow(opponentStatusText);

            // Player Deck Selection Panel
            var deckBox = GameBootstrapper.CreateOrnatePanel(centerCard.transform, "PlayerDeckSelectionPanel",
                new Color(0.05f, 0.07f, 0.12f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1180f, 82f));
            var deckBoxOutline = deckBox.AddComponent<Outline>();
            deckBoxOutline.effectColor = new Color(0.24f, 0.42f, 0.62f, 0.85f);

            var deckHeader = GameBootstrapper.CreateText(deckBox.transform, "DeckHeader",
                "🎮 SELECT YOUR 50-CARD BATTLE DECK:", 12,
                new Color(0.78f, 0.90f, 1f), TextAnchor.MiddleCenter, new Vector2(0f, 24f), new Vector2(1120f, 20f));
            deckHeader.fontStyle = FontStyle.Bold;

            float[] xOffsets = { -480f, -288f, -96f, 96f, 288f, 480f };
            float btnW = 180f;

            GameBootstrapper.CreateBeveledButton(deckBox.transform, "BtnDeckGora",
                "🏆 GORA CORE (50)", new Color(0.16f, 0.32f, 0.48f, 0.95f), Color.white,
                new Vector2(xOffsets[0], -8f), new Vector2(btnW, 32f), 11,
                () => SetPlayerDeckInMenu(GameManager.PlayerDeckSelection.GoraChampion));

            GameBootstrapper.CreateBeveledButton(deckBox.transform, "BtnDeckNavi",
                "🟣 NAVI CORE (50)", new Color(0.38f, 0.18f, 0.44f, 0.95f), Color.white,
                new Vector2(xOffsets[1], -8f), new Vector2(btnW, 32f), 11,
                () => SetPlayerDeckInMenu(GameManager.PlayerDeckSelection.NaviChampion));

            GameBootstrapper.CreateBeveledButton(deckBox.transform, "BtnDeckFire",
                "🔥 FIRE AGGRO (50)", new Color(0.52f, 0.22f, 0.14f, 0.95f), Color.white,
                new Vector2(xOffsets[2], -8f), new Vector2(btnW, 32f), 11,
                () => SetPlayerDeckInMenu(GameManager.PlayerDeckSelection.FireAggro));

            GameBootstrapper.CreateBeveledButton(deckBox.transform, "BtnDeckWater",
                "💧 WATER CTRL (50)", new Color(0.14f, 0.36f, 0.48f, 0.95f), Color.white,
                new Vector2(xOffsets[3], -8f), new Vector2(btnW, 32f), 11,
                () => SetPlayerDeckInMenu(GameManager.PlayerDeckSelection.WaterControl));

            GameBootstrapper.CreateBeveledButton(deckBox.transform, "BtnDeckDraft",
                "🎲 BALANCED DRAFT", new Color(0.18f, 0.38f, 0.28f, 0.95f), Color.white,
                new Vector2(xOffsets[4], -8f), new Vector2(btnW, 32f), 11,
                () => SetPlayerDeckInMenu(GameManager.PlayerDeckSelection.BalancedDraft));

            GameBootstrapper.CreateBeveledButton(deckBox.transform, "BtnDeckCustom",
                "🛠️ CUSTOM BUILDER", new Color(0.44f, 0.26f, 0.14f, 0.95f), new Color(1f, 0.95f, 0.85f),
                new Vector2(xOffsets[5], -8f), new Vector2(btnW, 32f), 11,
                () => OpenDeckBuilder());

            playerDeckStatusText = GameBootstrapper.CreateText(deckBox.transform, "PlayerDeckStatus",
                "Selected Deck: Balanced Draft Deck (50 Cards)", 11,
                new Color(0.85f, 1f, 0.90f), TextAnchor.MiddleCenter, new Vector2(0f, -28f), new Vector2(1120f, 18f));
            GameBootstrapper.AddTextShadow(playerDeckStatusText);

            // Quick Category Shortcuts Row
            GameBootstrapper.CreateBeveledButton(centerCard.transform, "BtnQuickMissing",
                "⚠️ MISSING ART ONLY",
                new Color(0.52f, 0.24f, 0.12f, 0.96f), new Color(1f, 0.92f, 0.84f),
                new Vector2(-405f, 45f), new Vector2(256f, 38f), 12,
                () => OpenGallery(TypeFilter.All, ArtStatusFilter.MissingArt));

            GameBootstrapper.CreateBeveledButton(centerCard.transform, "BtnQuickMonsters",
                "🐺 MONSTERS GALLERY",
                new Color(0.16f, 0.22f, 0.34f, 0.96f), new Color(0.90f, 0.95f, 1f),
                new Vector2(-135f, 45f), new Vector2(256f, 38f), 12,
                () => OpenGallery(TypeFilter.Monsters, ArtStatusFilter.All));

            GameBootstrapper.CreateBeveledButton(centerCard.transform, "BtnQuickSpells",
                "📜 SPELLS GALLERY",
                new Color(0.26f, 0.16f, 0.38f, 0.96f), new Color(0.94f, 0.90f, 1f),
                new Vector2(135f, 45f), new Vector2(256f, 38f), 12,
                () => OpenGallery(TypeFilter.Spells, ArtStatusFilter.All));

            GameBootstrapper.CreateBeveledButton(centerCard.transform, "BtnQuickEnvsAndSpirits",
                "🌋 ENVIRONMENTS & SPIRITS",
                new Color(0.12f, 0.28f, 0.32f, 0.96f), new Color(0.88f, 0.98f, 1f),
                new Vector2(405f, 45f), new Vector2(256f, 38f), 12,
                () => OpenGallery(TypeFilter.Environments, ArtStatusFilter.All));

            // Asset Completion Audit Panel
            var auditPanel = GameBootstrapper.CreateOrnatePanel(centerCard.transform, "AssetAuditPanel",
                new Color(0.03f, 0.05f, 0.09f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -145f), new Vector2(1180f, 300f));
            var auditOutline = auditPanel.AddComponent<Outline>();
            auditOutline.effectColor = new Color(0.24f, 0.36f, 0.52f, 0.85f);

            var auditHeader = GameBootstrapper.CreateText(auditPanel.transform, "AuditHeader",
                "📊 PROJECT CARD & ARTWORK COMPLETION STATUS", 17,
                new Color(0.96f, 0.86f, 0.55f), TextAnchor.MiddleCenter, new Vector2(0f, 120f), new Vector2(1120f, 28f));
            auditHeader.fontStyle = FontStyle.Bold;
            GameBootstrapper.AddTextShadow(auditHeader);

            mainMenuStatsText = GameBootstrapper.CreateText(auditPanel.transform, "MainMenuStatsText",
                "Scanning card database and artwork assets...", 14,
                new Color(0.88f, 0.94f, 1f), TextAnchor.MiddleCenter, new Vector2(0f, -10f), new Vector2(1120f, 220f));
            mainMenuStatsText.lineSpacing = 1.25f;
            GameBootstrapper.AddTextShadow(mainMenuStatsText);

            // Bottom Row: Fresh Match & Quit Game
            GameBootstrapper.CreateBeveledButton(centerCard.transform, "BtnRestartFreshMatch",
                "🔄 START FRESH NEW MATCH (RESET BOARD & DECKS)",
                new Color(0.18f, 0.24f, 0.32f, 0.95f), new Color(0.86f, 0.92f, 1f),
                new Vector2(-140f, -380f), new Vector2(440f, 44f), 13,
                () =>
                {
                    if (GameManager.Instance != null) GameManager.Instance.RestartGame();
                    StartOrResumeMatch();
                });

            GameBootstrapper.CreateBeveledButton(centerCard.transform, "BtnQuitApp",
                "🚪 QUIT GAME",
                new Color(0.42f, 0.14f, 0.16f, 0.95f), new Color(1f, 0.88f, 0.88f),
                new Vector2(220f, -380f), new Vector2(220f, 44f), 13,
                () => Application.Quit());
        }

        private void SetOpponentInMenu(GameManager.OpponentModel opp)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.SetOpponent(opp);
            }
            RefreshOpponentAndDeckBadges();
        }

        private void SetPlayerDeckInMenu(GameManager.PlayerDeckSelection deck)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.SetPlayerDeck(deck);
            }
            RefreshOpponentAndDeckBadges();
        }

        private void RefreshOpponentAndDeckBadges()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            if (opponentStatusText != null)
            {
                string oppName = (gm.currentOpponent == GameManager.OpponentModel.Gora)
                    ? "🏆 Gora (Aggressive Combat AI)"
                    : "🟣 Navi (Tactical Control AI)";
                opponentStatusText.text = $"Active Opponent: {oppName} | Auto-alternates after each match";
            }

            if (playerDeckStatusText != null)
            {
                playerDeckStatusText.text = $"Selected Deck: {gm.GetSelectedDeckDisplayName()}";
            }

            if (startBtnLabelText != null)
            {
                string oppShort = (gm.currentOpponent == GameManager.OpponentModel.Gora) ? "Gora" : "Navi";
                startBtnLabelText.text = gm.isMatchStarted
                    ? $"⚔️ RESUME BATTLE VS {oppShort.ToUpper()}"
                    : $"⚔️ START BATTLE VS {oppShort.ToUpper()}";
            }
        }

        private void RefreshMainMenuSummary()
        {
            RefreshOpponentAndDeckBadges();

            if (mainMenuStatsText == null) return;

            int mTotal = 0, mArt = 0, m25D = 0;
            int sTotal = 0, sArt = 0;
            int eTotal = 0, eArt = 0;
            int lTotal = 0, lArt = 0;

            foreach (var c in masterCatalog)
            {
                if (c == null) continue;
                bool hasArt = HasCardArtwork(c);
                if (c.cardType == CardType.Monster)
                {
                    mTotal++;
                    if (hasArt) mArt++;
                    if (MonsterArtLoader.HasStandby25DAssets(c)) m25D++;
                }
                else if (c.cardType == CardType.Spell)
                {
                    sTotal++;
                    if (hasArt) sArt++;
                }
                else if (c.cardType == CardType.Environment)
                {
                    eTotal++;
                    if (hasArt) eArt++;
                }
                else if (c.cardType == CardType.Leader)
                {
                    lTotal++;
                    if (hasArt) lArt++;
                }
            }

            string[] elems = { "Fire", "Water", "Ice", "Electric", "Earth", "Nature", "Air", "Light", "Dark", "Desert" };
            int elemFramesReady = 0;
            foreach (var el in elems)
            {
                if (MonsterArtLoader.GetCardFrameOverlaySprite(el) != null) elemFramesReady++;
            }

            string[] tribes = { "Beasts", "Ghosts", "Dwarves", "Elves", "Warriors", "Giants", "Insects", "Mages", "Demons", "Celestials" };
            int clanEmblemsReady = 0;
            foreach (var tr in tribes)
            {
                if (MonsterArtLoader.GetClanEmblemSprite(tr) != null) clanEmblemsReady++;
            }

            int totalCards = mTotal + sTotal + eTotal + lTotal;
            int totalArt = mArt + sArt + eArt + lArt;
            int totalMissing = Mathf.Max(0, totalCards - totalArt);

            mainMenuStatsText.text =
                $"TOTAL CARDS IN CATALOG: {totalCards}   |   ✅ COMPLETED ART: {totalArt}   |   ⚠️ MISSING ART: {totalMissing}\n\n" +
                $"• 🐺 Monsters: Total {mTotal}   |   ✅ Has Art: {mArt}   |   ⚠️ Missing Art: {mTotal - mArt}   |   🎬 2.5D Standby Ready: {m25D}\n" +
                $"• 📜 Spells: Total {sTotal}   |   ✅ Has Art: {sArt}   |   ⚠️ Missing Art: {sTotal - sArt}\n" +
                $"• 🌋 Environments (10 Elements): Total {eTotal}   |   ✅ Ground Art Ready: {eArt}   |   ⚠️ Missing: {eTotal - eArt}\n" +
                $"• 👑 Commander Spirits: Total {lTotal}   |   ✅ Has Art: {lTotal} / {lArt}   |   ⚠️ Missing Art: {lTotal - lArt}\n\n" +
                $"• 🖼️ Elemental Card Frames: {elemFramesReady} / 10 Ready ✅     |     🛡️ Top-Left Clan Emblems: {clanEmblemsReady} / 10 Ready ✅";
        }

        // =========================================================================
        // 2. FULL-SCREEN CARD GALLERY OVERLAY
        // =========================================================================
        private void BuildGalleryOverlay(Transform parent)
        {
            galleryOverlay = GameBootstrapper.CreatePanel(parent, "CardGalleryOverlay",
                new Color(0.02f, 0.03f, 0.06f, 0.99f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // 2.1 Top Header Bar (y = 514, height = 44)
            var topBar = GameBootstrapper.CreateOrnatePanel(galleryOverlay.transform, "GalleryTopBar",
                new Color(0.06f, 0.09f, 0.16f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 514f), new Vector2(1896f, 44f));

            GameBootstrapper.CreateBeveledButton(topBar.transform, "BtnBackToMenu", "🏠 MAIN MENU",
                new Color(0.22f, 0.16f, 0.34f, 0.96f), new Color(0.95f, 0.92f, 1f),
                new Vector2(-860f, 0f), new Vector2(150f, 32f), 11,
                () => ShowMainMenu());

            GameBootstrapper.CreateBeveledButton(topBar.transform, "BtnGoToBattle", "⚔️ START / BATTLE",
                new Color(0.14f, 0.42f, 0.24f, 0.96f), new Color(0.92f, 1f, 0.94f),
                new Vector2(-695f, 0f), new Vector2(150f, 32f), 11,
                () => StartOrResumeMatch());

            headerStatsText = GameBootstrapper.CreateText(topBar.transform, "GalleryHeaderStats",
                "🎴 CARD GALLERY — HAND VIEW + BATTLEFIELD VIEW", 14,
                new Color(0.96f, 0.88f, 0.62f), TextAnchor.MiddleCenter, new Vector2(-40f, 0f), new Vector2(880f, 34f));
            headerStatsText.fontStyle = FontStyle.Bold;
            GameBootstrapper.AddTextShadow(headerStatsText);

            // View Mode Toggle Buttons on Right of Top Bar
            AddViewModeButton(topBar.transform, "Both", "✋+🏟️ HAND + FIELD", new Vector2(535f, 0f), new Vector2(170f, 30f), GalleryViewMode.BothHandAndField);
            AddViewModeButton(topBar.transform, "Hand", "✋ HAND ONLY", new Vector2(705f, 0f), new Vector2(126f, 30f), GalleryViewMode.HandOnly);
            AddViewModeButton(topBar.transform, "Field", "🏟️ FIELD ONLY", new Vector2(842f, 0f), new Vector2(138f, 30f), GalleryViewMode.FieldOnly);

            // 2.2 Filter Row 1: Card Types + Art Completion Status + Tier (y = 468, height = 38)
            var filterRow1 = GameBootstrapper.CreatePanel(galleryOverlay.transform, "FilterRow1_TypeAndStatus",
                new Color(0.05f, 0.07f, 0.12f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 468f), new Vector2(1896f, 36f));

            AddTypeFilterButton(filterRow1.transform, "All", "ALL TYPES", new Vector2(-890f, 0f), new Vector2(88f, 28f), TypeFilter.All);
            AddTypeFilterButton(filterRow1.transform, "Monsters", "🐺 MONSTERS", new Vector2(-790f, 0f), new Vector2(102f, 28f), TypeFilter.Monsters);
            AddTypeFilterButton(filterRow1.transform, "Spells", "📜 SPELLS", new Vector2(-680f, 0f), new Vector2(96f, 28f), TypeFilter.Spells);
            AddTypeFilterButton(filterRow1.transform, "Environments", "🌋 ENVIRONMENTS", new Vector2(-566f, 0f), new Vector2(116f, 28f), TypeFilter.Environments);
            AddTypeFilterButton(filterRow1.transform, "Leaders", "👑 SPIRITS", new Vector2(-446f, 0f), new Vector2(96f, 28f), TypeFilter.Leaders);

            // Status filter buttons (to see what's done vs what's missing!)
            AddStatusFilterButton(filterRow1.transform, "All", "ALL STATUS", new Vector2(-318f, 0f), new Vector2(98f, 28f), ArtStatusFilter.All, new Color(0.14f, 0.18f, 0.26f));
            AddStatusFilterButton(filterRow1.transform, "HasArt", "✅ HAS ART", new Vector2(-202f, 0f), new Vector2(118f, 28f), ArtStatusFilter.HasArt, new Color(0.12f, 0.32f, 0.20f));
            AddStatusFilterButton(filterRow1.transform, "MissingArt", "⚠️ MISSING ART", new Vector2(-66f, 0f), new Vector2(128f, 28f), ArtStatusFilter.MissingArt, new Color(0.42f, 0.20f, 0.12f));
            AddStatusFilterButton(filterRow1.transform, "Has25D", "🎬 2.5D READY", new Vector2(68f, 0f), new Vector2(118f, 28f), ArtStatusFilter.Has25D, new Color(0.20f, 0.26f, 0.44f));

            // Tier filter buttons (All, T1..T7)
            AddTierFilterButton(filterRow1.transform, 0, "ALL T", new Vector2(226f, 0f), new Vector2(62f, 28f));
            for (int t = 1; t <= 7; t++)
            {
                AddTierFilterButton(filterRow1.transform, t, $"T{t}", new Vector2(226f + t * 56f, 0f), new Vector2(48f, 28f));
            }

            // 2.3 Filter Row 2: 10 Elements (y = 428, height = 34)
            var filterRow2 = GameBootstrapper.CreatePanel(galleryOverlay.transform, "FilterRow2_Elements",
                new Color(0.04f, 0.06f, 0.10f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 428f), new Vector2(1896f, 34f));

            string[] elemKeys = { "All", "Fire", "Water", "Ice", "Electric", "Earth", "Nature", "Air", "Light", "Dark", "Desert" };
            string[] elemLabels = {
                "ALL ELEMENTS", "🔥 Fire", "💧 Water", "❄️ Ice", "⚡ Electric",
                "🪨 Earth", "🌿 Nature", "🌪️ Air", "✨ Light", "🌑 Dark", "🏜️ Desert"
            };
            for (int i = 0; i < elemKeys.Length; i++)
            {
                float x = -800f + i * 160f;
                AddElementFilterButton(filterRow2.transform, elemKeys[i], elemLabels[i], new Vector2(x, 0f), new Vector2(150f, 26f));
            }

            // 2.4 Filter Row 3: 10 Clans / Tribes (y = 390, height = 34)
            var filterRow3 = GameBootstrapper.CreatePanel(galleryOverlay.transform, "FilterRow3_Tribes",
                new Color(0.04f, 0.06f, 0.10f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 390f), new Vector2(1896f, 34f));

            string[] tribeKeys = { "All", "Beasts", "Ghosts", "Dwarves", "Elves", "Warriors", "Giants", "Insects", "Mages", "Demons", "Celestials" };
            string[] tribeLabels = {
                "ALL CLANS", "🐺 Beasts", "👻 Ghosts", "⛏️ Dwarves", "🏹 Elves",
                "⚔️ Warriors", "🗿 Giants", "🕷️ Insects", "🧙 Mages", "👹 Demons", "👼 Celestials"
            };
            for (int i = 0; i < tribeKeys.Length; i++)
            {
                float x = -840f + i * 168f;
                AddTribeFilterButton(filterRow3.transform, tribeKeys[i], tribeLabels[i], new Vector2(x, 0f), new Vector2(158f, 26f));
            }

            // 2.5 Left/Center Paginated Grid Area (1330 x 824, centered at x = -280, y = -48)
            var gridPanel = GameBootstrapper.CreateOrnatePanel(galleryOverlay.transform, "GalleryGridPanel",
                new Color(0.03f, 0.04f, 0.08f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-280f, -48f), new Vector2(1330f, 824f));

            var gridObj = new GameObject("CardsGridContainer");
            gridObj.transform.SetParent(gridPanel.transform, false);
            var gridRt = gridObj.AddComponent<RectTransform>();
            gridRt.anchorMin = new Vector2(0.5f, 0.5f);
            gridRt.anchorMax = new Vector2(0.5f, 0.5f);
            gridRt.sizeDelta = new Vector2(1306f, 752f);
            gridRt.anchoredPosition = new Vector2(0f, 24f);
            gridContainer = gridObj.transform;

            // Bottom Pagination Controls inside Grid Panel
            var pageBar = GameBootstrapper.CreatePanel(gridPanel.transform, "PaginationBar",
                new Color(0.06f, 0.09f, 0.15f, 0.98f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(1306f, 42f));

            GameBootstrapper.CreateBeveledButton(pageBar.transform, "BtnFirstPage", "⏮️ FIRST",
                new Color(0.14f, 0.20f, 0.30f), Color.white, new Vector2(-560f, 0f), new Vector2(110f, 30f), 11,
                () => ChangePage(-99999));

            GameBootstrapper.CreateBeveledButton(pageBar.transform, "BtnPrev10Page", "⏪ -10",
                new Color(0.14f, 0.20f, 0.30f), Color.white, new Vector2(-436f, 0f), new Vector2(96f, 30f), 11,
                () => ChangePage(-10));

            GameBootstrapper.CreateBeveledButton(pageBar.transform, "BtnPrevPage", "◀️ PREV",
                new Color(0.16f, 0.28f, 0.44f), Color.white, new Vector2(-310f, 0f), new Vector2(130f, 30f), 12,
                () => ChangePage(-1));

            pageIndicatorText = GameBootstrapper.CreateText(pageBar.transform, "PageIndicatorText",
                "Page 1 / 1", 13, new Color(0.92f, 0.96f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(440f, 32f));
            pageIndicatorText.fontStyle = FontStyle.Bold;

            GameBootstrapper.CreateBeveledButton(pageBar.transform, "BtnNextPage", "NEXT ▶️",
                new Color(0.16f, 0.28f, 0.44f), Color.white, new Vector2(310f, 0f), new Vector2(130f, 30f), 12,
                () => ChangePage(1));

            GameBootstrapper.CreateBeveledButton(pageBar.transform, "BtnNext10Page", "+10 ⏩",
                new Color(0.14f, 0.20f, 0.30f), Color.white, new Vector2(436f, 0f), new Vector2(96f, 30f), 11,
                () => ChangePage(10));

            GameBootstrapper.CreateBeveledButton(pageBar.transform, "BtnLastPage", "LAST ⏭️",
                new Color(0.14f, 0.20f, 0.30f), Color.white, new Vector2(560f, 0f), new Vector2(110f, 30f), 11,
                () => ChangePage(99999));

            // 2.6 Right Side Panel: Live Side-by-Side (Hand + Field) Showcase & Missing Asset Checklist (550 x 824 at x = 670, y = -48)
            var inspectorPanel = GameBootstrapper.CreateOrnatePanel(galleryOverlay.transform, "GalleryInspectorPanel",
                new Color(0.04f, 0.06f, 0.11f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(670f, -48f), new Vector2(550f, 824f));
            var inspOut = inspectorPanel.AddComponent<Outline>();
            inspOut.effectColor = new Color(0.32f, 0.54f, 0.82f, 0.85f);

            inspectorTitleText = GameBootstrapper.CreateText(inspectorPanel.transform, "InspectorTitle",
                "CARD PREVIEW (HAND + BATTLEFIELD)", 16,
                new Color(0.98f, 0.88f, 0.58f), TextAnchor.MiddleCenter, new Vector2(0f, 386f), new Vector2(520f, 28f));
            inspectorTitleText.fontStyle = FontStyle.Bold;
            GameBootstrapper.AddTextShadow(inspectorTitleText);

            inspectorMetaText = GameBootstrapper.CreateText(inspectorPanel.transform, "InspectorMeta",
                "", 12, new Color(0.78f, 0.90f, 1f), TextAnchor.MiddleCenter, new Vector2(0f, 358f), new Vector2(520f, 22f));

            // Dual Preview Stage (524 x 294 at y = 190)
            var dualStage = GameBootstrapper.CreateOrnatePanel(inspectorPanel.transform, "DualPreviewStage",
                new Color(0.02f, 0.03f, 0.06f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 190f), new Vector2(524f, 294f));

            var lblHand = GameBootstrapper.CreateText(dualStage.transform, "LblHand",
                "✋ PLAYER HAND VIEW", 12, new Color(0.84f, 0.92f, 1f),
                TextAnchor.MiddleCenter, new Vector2(-130f, 128f), new Vector2(230f, 22f));
            lblHand.fontStyle = FontStyle.Bold;

            var lblField = GameBootstrapper.CreateText(dualStage.transform, "LblField",
                "🏟️ BATTLEFIELD SLOT VIEW", 12, new Color(0.84f, 0.98f, 0.88f),
                TextAnchor.MiddleCenter, new Vector2(130f, 128f), new Vector2(230f, 22f));
            lblField.fontStyle = FontStyle.Bold;

            var handHolderObj = new GameObject("InspectorHandHolder");
            handHolderObj.transform.SetParent(dualStage.transform, false);
            var hhRt = handHolderObj.AddComponent<RectTransform>();
            hhRt.anchorMin = new Vector2(0.5f, 0.5f);
            hhRt.anchorMax = new Vector2(0.5f, 0.5f);
            hhRt.sizeDelta = new Vector2(220f, 240f);
            hhRt.anchoredPosition = new Vector2(-130f, -10f);
            inspectorHandHolder = handHolderObj.transform;

            var fieldHolderObj = new GameObject("InspectorFieldHolder");
            fieldHolderObj.transform.SetParent(dualStage.transform, false);
            var fhRt = fieldHolderObj.AddComponent<RectTransform>();
            fhRt.anchorMin = new Vector2(0.5f, 0.5f);
            fhRt.anchorMax = new Vector2(0.5f, 0.5f);
            fhRt.sizeDelta = new Vector2(220f, 240f);
            fhRt.anchoredPosition = new Vector2(130f, -10f);
            inspectorFieldHolder = fieldHolderObj.transform;

            // Asset Checklist Box
            var checklistPanel = GameBootstrapper.CreateOrnatePanel(inspectorPanel.transform, "AssetChecklistPanel",
                new Color(0.05f, 0.08f, 0.14f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -55f), new Vector2(524f, 168f));

            var chkHeader = GameBootstrapper.CreateText(checklistPanel.transform, "ChecklistHeader",
                "🔍 ASSET & ARTWORK CHECKLIST:", 13,
                new Color(0.96f, 0.86f, 0.55f), TextAnchor.MiddleLeft, new Vector2(0f, 64f), new Vector2(496f, 22f));
            chkHeader.fontStyle = FontStyle.Bold;

            inspectorChecklistText = GameBootstrapper.CreateText(checklistPanel.transform, "ChecklistBody",
                "", 12, new Color(0.90f, 0.95f, 1f), TextAnchor.UpperLeft, new Vector2(0f, -12f), new Vector2(496f, 124f));
            inspectorChecklistText.lineSpacing = 1.2f;

            // Card Abilities & Description Box
            var abilitiesPanel = GameBootstrapper.CreateOrnatePanel(inspectorPanel.transform, "AbilitiesPanel",
                new Color(0.03f, 0.05f, 0.09f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -272f), new Vector2(524f, 242f));

            inspectorAbilitiesText = GameBootstrapper.CreateText(abilitiesPanel.transform, "AbilitiesBody",
                "", 11, new Color(0.88f, 0.92f, 0.98f), TextAnchor.UpperLeft, new Vector2(0f, -4f), new Vector2(496f, 218f));
            inspectorAbilitiesText.lineSpacing = 1.18f;
            inspectorAbilitiesText.resizeTextForBestFit = true;
            inspectorAbilitiesText.resizeTextMinSize = 9;
            inspectorAbilitiesText.resizeTextMaxSize = 12;
        }

        // =========================================================================
        // 2.5 CUSTOM DECK BUILDER OVERLAY (Scrollable Monster/Card Library + Deck Tray)
        // =========================================================================
        private void BuildDeckBuilderOverlay(Transform parent)
        {
            deckBuilderOverlay = GameBootstrapper.CreatePanel(parent, "CustomDeckBuilderOverlay",
                new Color(0.02f, 0.03f, 0.05f, 0.99f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Center Modal Window (1860 x 1030)
            var modal = GameBootstrapper.CreateOrnatePanel(deckBuilderOverlay.transform, "DeckBuilderModal",
                new Color(0.04f, 0.06f, 0.10f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1860f, 1030f));
            var modalOutline = modal.AddComponent<Outline>();
            modalOutline.effectColor = new Color(0.35f, 0.60f, 0.92f, 0.85f);
            modalOutline.effectDistance = new Vector2(2f, 2f);

            // 1. Top Header Bar (1820 x 64 at y = 472)
            var topBar = GameBootstrapper.CreateOrnatePanel(modal.transform, "DeckBuilderTopBar",
                new Color(0.06f, 0.09f, 0.16f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 472f), new Vector2(1820f, 64f));

            var titleTxt = GameBootstrapper.CreateText(topBar.transform, "Title",
                "🛠️ CUSTOM DECK BUILDER • CRAFT YOUR 50-CARD BATTLE DECK", 20,
                new Color(0.96f, 0.88f, 0.62f), TextAnchor.MiddleLeft, new Vector2(-180f, 10f), new Vector2(1200f, 28f));
            titleTxt.fontStyle = FontStyle.Bold;
            GameBootstrapper.AddTextShadow(titleTxt);

            var subTxt = GameBootstrapper.CreateText(topBar.transform, "Subtitle",
                "Scroll through monsters and cards • Click (+) to add, (−) to remove • Max 2 copies per card • Exactly 50 cards required", 12,
                new Color(0.76f, 0.88f, 0.98f), TextAnchor.MiddleLeft, new Vector2(-180f, -14f), new Vector2(1200f, 20f));
            GameBootstrapper.AddTextShadow(subTxt);

            GameBootstrapper.CreateBeveledButton(topBar.transform, "BtnCloseDeckBuilder",
                "✖️ CLOSE / MENU",
                new Color(0.36f, 0.16f, 0.18f, 0.98f), new Color(1f, 0.90f, 0.90f),
                new Vector2(790f, 0f), new Vector2(160f, 38f), 12,
                () => CloseDeckBuilder());

            // 2. Left Column: Monster & Card Library (Width 1260, Height 868, at x = -275, y = -36)
            var libPanel = GameBootstrapper.CreateOrnatePanel(modal.transform, "CardLibraryPanel",
                new Color(0.03f, 0.04f, 0.08f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-275f, -36f), new Vector2(1260f, 868f));
            var libOutline = libPanel.AddComponent<Outline>();
            libOutline.effectColor = new Color(0.22f, 0.36f, 0.54f, 0.85f);

            // Filter Bar 1: Type Tabs (y = 408 inside libPanel, size 1240 x 32)
            var typeBar = GameBootstrapper.CreatePanel(libPanel.transform, "TypeFilterBar",
                new Color(0.05f, 0.08f, 0.14f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 408f), new Vector2(1240f, 32f));

            AddDeckBuilderTypeButton(typeBar.transform, TypeFilter.Monsters, "🐺 MONSTERS (240)", new Vector2(-460f, 0f), new Vector2(190f, 26f));
            AddDeckBuilderTypeButton(typeBar.transform, TypeFilter.Spells, "📜 SPELLS (200)", new Vector2(-255f, 0f), new Vector2(170f, 26f));
            AddDeckBuilderTypeButton(typeBar.transform, TypeFilter.Environments, "🌋 ENVIRONMENTS (10)", new Vector2(-65f, 0f), new Vector2(180f, 26f));
            AddDeckBuilderTypeButton(typeBar.transform, TypeFilter.Leaders, "👑 SPIRITS (10)", new Vector2(120f, 0f), new Vector2(150f, 26f));
            AddDeckBuilderTypeButton(typeBar.transform, TypeFilter.All, "🎴 ALL CARDS (460)", new Vector2(300f, 0f), new Vector2(170f, 26f));

            deckBuilderFilterCountText = GameBootstrapper.CreateText(typeBar.transform, "FilterCountText",
                "Showing 240 Monsters", 11,
                new Color(0.92f, 0.96f, 1f), TextAnchor.MiddleRight, new Vector2(510f, 0f), new Vector2(200f, 24f));
            deckBuilderFilterCountText.fontStyle = FontStyle.Bold;

            // Filter Bar 2: Element Tabs (y = 374 inside libPanel, size 1240 x 28)
            var elemBar = GameBootstrapper.CreatePanel(libPanel.transform, "ElementFilterBar",
                new Color(0.04f, 0.06f, 0.10f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 374f), new Vector2(1240f, 28f));

            string[] elemKeys = { "All", "Fire", "Water", "Ice", "Electric", "Earth", "Nature", "Air", "Light", "Dark", "Desert" };
            string[] elemLabels = { "ALL ELEM", "🔥 Fire", "💧 Water", "❄️ Ice", "⚡ Elec", "🪨 Earth", "🌿 Nature", "🌪️ Air", "✨ Light", "🌑 Dark", "🏜️ Desert" };
            for (int i = 0; i < elemKeys.Length; i++)
            {
                float x = -550f + i * 110f;
                AddDeckBuilderElementButton(elemBar.transform, elemKeys[i], elemLabels[i], new Vector2(x, 0f), new Vector2(104f, 22f));
            }

            // Filter Bar 3: Clan / Tribe Tabs (y = 342 inside libPanel, size 1240 x 28)
            var tribeBar = GameBootstrapper.CreatePanel(libPanel.transform, "TribeFilterBar",
                new Color(0.04f, 0.06f, 0.10f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 342f), new Vector2(1240f, 28f));

            string[] tribeKeys = { "All", "Beasts", "Ghosts", "Dwarves", "Elves", "Warriors", "Giants", "Insects", "Mages", "Demons", "Celestials" };
            string[] tribeLabels = { "ALL CLANS", "🐺 Beasts", "👻 Ghosts", "⛏️ Dwarves", "🏹 Elves", "⚔️ Warriors", "🗿 Giants", "🕷️ Insects", "🧙 Mages", "👹 Demons", "👼 Celestials" };
            for (int i = 0; i < tribeKeys.Length; i++)
            {
                float x = -555f + i * 111f;
                AddDeckBuilderTribeButton(tribeBar.transform, tribeKeys[i], tribeLabels[i], new Vector2(x, 0f), new Vector2(104f, 22f));
            }

            // Scrollable Catalog Grid Viewport (size 1240 x 714 at y = -48)
            var scrollObj = new GameObject("CatalogScrollView");
            scrollObj.transform.SetParent(libPanel.transform, false);
            var scrollRt = scrollObj.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.5f, 0.5f);
            scrollRt.anchorMax = new Vector2(0.5f, 0.5f);
            scrollRt.sizeDelta = new Vector2(1240f, 714f);
            scrollRt.anchoredPosition = new Vector2(0f, -48f);

            var scrollRect = scrollObj.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 45f;
            scrollRect.inertia = true;

            var viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(scrollObj.transform, false);
            var vpRt = viewportObj.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.sizeDelta = Vector2.zero;
            var vpImg = viewportObj.AddComponent<Image>();
            vpImg.color = new Color(0.02f, 0.03f, 0.05f, 0.25f);
            viewportObj.AddComponent<RectMask2D>();
            scrollRect.viewport = vpRt;

            var contentObj = new GameObject("CatalogContent");
            contentObj.transform.SetParent(viewportObj.transform, false);
            var contentRt = contentObj.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;

            var glg = contentObj.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(186f, 236f);
            glg.spacing = new Vector2(14f, 14f);
            glg.padding = new RectOffset(12, 12, 12, 12);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 6;

            var csf = contentObj.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = contentRt;
            deckBuilderCatalogGrid = contentObj.transform;

            // 3. Right Column: Selected Deck Tray (Width 530, Height 868, at x = 635, y = -36)
            var trayPanel = GameBootstrapper.CreateOrnatePanel(modal.transform, "DeckTrayPanel",
                new Color(0.03f, 0.05f, 0.09f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(635f, -36f), new Vector2(530f, 868f));
            var trayOutline = trayPanel.AddComponent<Outline>();
            trayOutline.effectColor = new Color(0.28f, 0.44f, 0.64f, 0.85f);

            // Tray Header Box (size 510 x 96 at y = 370)
            var trayHdr = GameBootstrapper.CreateOrnatePanel(trayPanel.transform, "TrayHeaderBox",
                new Color(0.06f, 0.09f, 0.16f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 370f), new Vector2(510f, 96f));

            deckBuilderStatusText = GameBootstrapper.CreateText(trayHdr.transform, "TrayStatus",
                "⚠️ 0 / 50 CARDS (50 NEEDED)", 16,
                new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleCenter, new Vector2(0f, 24f), new Vector2(490f, 26f));
            deckBuilderStatusText.fontStyle = FontStyle.Bold;
            GameBootstrapper.AddTextShadow(deckBuilderStatusText);

            deckBuilderBreakdownText = GameBootstrapper.CreateText(trayHdr.transform, "TrayBreakdown",
                "Monsters: 0 • Spells: 0 • Envs: 0 • Spirits: 0", 11,
                new Color(0.78f, 0.90f, 1f), TextAnchor.MiddleCenter, new Vector2(0f, -2f), new Vector2(490f, 20f));

            deckBuilderAvgCostText = GameBootstrapper.CreateText(trayHdr.transform, "TrayAvgCost",
                "Average Mana Cost: 0.0 💎", 11,
                new Color(0.96f, 0.88f, 0.62f), TextAnchor.MiddleCenter, new Vector2(0f, -24f), new Vector2(490f, 18f));

            // Scrollable Tray List Viewport (size 510 x 620 at y = 0)
            var trayScrollObj = new GameObject("TrayScrollView");
            trayScrollObj.transform.SetParent(trayPanel.transform, false);
            var trayScrollRt = trayScrollObj.AddComponent<RectTransform>();
            trayScrollRt.anchorMin = new Vector2(0.5f, 0.5f);
            trayScrollRt.anchorMax = new Vector2(0.5f, 0.5f);
            trayScrollRt.sizeDelta = new Vector2(510f, 620f);
            trayScrollRt.anchoredPosition = new Vector2(0f, 0f);

            var trayScrollRect = trayScrollObj.AddComponent<ScrollRect>();
            trayScrollRect.horizontal = false;
            trayScrollRect.vertical = true;
            trayScrollRect.movementType = ScrollRect.MovementType.Clamped;
            trayScrollRect.scrollSensitivity = 30f;
            trayScrollRect.inertia = true;

            var trayVpObj = new GameObject("Viewport");
            trayVpObj.transform.SetParent(trayScrollObj.transform, false);
            var trayVpRt = trayVpObj.AddComponent<RectTransform>();
            trayVpRt.anchorMin = Vector2.zero;
            trayVpRt.anchorMax = Vector2.one;
            trayVpRt.sizeDelta = Vector2.zero;
            var trayVpImg = trayVpObj.AddComponent<Image>();
            trayVpImg.color = new Color(0.02f, 0.03f, 0.05f, 0.35f);
            trayVpObj.AddComponent<RectMask2D>();
            trayScrollRect.viewport = trayVpRt;

            var trayContentObj = new GameObject("TrayContent");
            trayContentObj.transform.SetParent(trayVpObj.transform, false);
            var trayContentRt = trayContentObj.AddComponent<RectTransform>();
            trayContentRt.anchorMin = new Vector2(0f, 1f);
            trayContentRt.anchorMax = new Vector2(1f, 1f);
            trayContentRt.pivot = new Vector2(0.5f, 1f);
            trayContentRt.anchoredPosition = Vector2.zero;
            trayContentRt.sizeDelta = Vector2.zero;

            var vlg = trayContentObj.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 4f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var trayCsf = trayContentObj.AddComponent<ContentSizeFitter>();
            trayCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            trayScrollRect.content = trayContentRt;
            deckBuilderTrayList = trayContentObj.transform;

            // Tray Bottom Controls Box (size 510 x 112 at y = -368)
            var trayBottom = GameBootstrapper.CreateOrnatePanel(trayPanel.transform, "TrayBottomControls",
                new Color(0.06f, 0.09f, 0.16f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -368f), new Vector2(510f, 112f));

            GameBootstrapper.CreateBeveledButton(trayBottom.transform, "BtnAutoFill",
                "🎲 AUTO-FILL TO 50",
                new Color(0.16f, 0.36f, 0.48f, 0.96f), Color.white,
                new Vector2(-128f, 24f), new Vector2(236f, 36f), 12,
                () => AutoFillCustomDeck());

            GameBootstrapper.CreateBeveledButton(trayBottom.transform, "BtnClearDeck",
                "🗑️ CLEAR ALL",
                new Color(0.48f, 0.16f, 0.16f, 0.96f), Color.white,
                new Vector2(128f, 24f), new Vector2(236f, 36f), 12,
                () => ClearCustomDeck());

            GameBootstrapper.CreateBeveledButton(trayBottom.transform, "BtnSaveEquipDeck",
                "💾 SAVE & EQUIP CUSTOM DECK (50)",
                new Color(0.14f, 0.50f, 0.26f, 0.98f), new Color(0.95f, 1f, 0.95f),
                new Vector2(0f, -22f), new Vector2(492f, 42f), 14,
                () => SaveAndEquipCustomDeck());

            // Floating Toast Notice Banner (at y = -494)
            deckBuilderNoticeText = GameBootstrapper.CreateText(modal.transform, "DeckNoticeText",
                "", 13,
                new Color(1f, 0.90f, 0.55f), TextAnchor.MiddleCenter, new Vector2(0f, -494f), new Vector2(1200f, 26f));
            deckBuilderNoticeText.fontStyle = FontStyle.Bold;
            GameBootstrapper.AddTextShadow(deckBuilderNoticeText);

            deckBuilderOverlay.SetActive(false);
        }

        private void AddDeckBuilderTypeButton(Transform parent, TypeFilter filter, string label, Vector2 pos, Vector2 size)
        {
            var btn = GameBootstrapper.CreateBeveledButton(parent, $"BtnType_{filter}", label,
                new Color(0.14f, 0.20f, 0.30f), Color.white, pos, size, 11,
                () => SetDeckBuilderTypeFilter(filter));
            var outline = btn.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.clear;
            deckBuilderTypeButtons.Add(new ButtonHighlightPair
            {
                key = filter.ToString(),
                bg = btn.GetComponent<Image>(),
                outline = outline,
                baseColor = new Color(0.14f, 0.20f, 0.30f)
            });
        }

        private void AddDeckBuilderElementButton(Transform parent, string elemKey, string label, Vector2 pos, Vector2 size)
        {
            var btn = GameBootstrapper.CreateBeveledButton(parent, $"BtnElem_{elemKey}", label,
                new Color(0.12f, 0.16f, 0.24f), Color.white, pos, size, 10,
                () => SetDeckBuilderElementFilter(elemKey));
            var outline = btn.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.clear;
            deckBuilderElementButtons.Add(new ButtonHighlightPair
            {
                key = elemKey,
                bg = btn.GetComponent<Image>(),
                outline = outline,
                baseColor = new Color(0.12f, 0.16f, 0.24f)
            });
        }

        private void AddDeckBuilderTribeButton(Transform parent, string tribeKey, string label, Vector2 pos, Vector2 size)
        {
            var btn = GameBootstrapper.CreateBeveledButton(parent, $"BtnTribe_{tribeKey}", label,
                new Color(0.12f, 0.16f, 0.24f), Color.white, pos, size, 10,
                () => SetDeckBuilderTribeFilter(tribeKey));
            var outline = btn.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.clear;
            deckBuilderTribeButtons.Add(new ButtonHighlightPair
            {
                key = tribeKey,
                bg = btn.GetComponent<Image>(),
                outline = outline,
                baseColor = new Color(0.12f, 0.16f, 0.24f)
            });
        }

        private void SetDeckBuilderTypeFilter(TypeFilter filter)
        {
            deckBuilderTypeFilter = filter;
            UpdateDeckBuilderFilterHighlights();
            PopulateDeckBuilderCatalog();
        }

        private void SetDeckBuilderElementFilter(string elem)
        {
            deckBuilderElementFilter = elem;
            UpdateDeckBuilderFilterHighlights();
            PopulateDeckBuilderCatalog();
        }

        private void SetDeckBuilderTribeFilter(string tribe)
        {
            deckBuilderTribeFilter = tribe;
            UpdateDeckBuilderFilterHighlights();
            PopulateDeckBuilderCatalog();
        }

        private void UpdateDeckBuilderFilterHighlights()
        {
            foreach (var b in deckBuilderTypeButtons)
            {
                bool active = b.key.Equals(deckBuilderTypeFilter.ToString(), StringComparison.OrdinalIgnoreCase);
                if (b.bg != null) b.bg.color = active ? new Color(0.24f, 0.46f, 0.72f, 0.98f) : b.baseColor;
                if (b.outline != null) b.outline.effectColor = active ? new Color(0.6f, 0.9f, 1f) : Color.clear;
            }

            foreach (var b in deckBuilderElementButtons)
            {
                bool active = b.key.Equals(deckBuilderElementFilter, StringComparison.OrdinalIgnoreCase);
                if (b.bg != null) b.bg.color = active ? new Color(0.36f, 0.44f, 0.62f, 0.98f) : b.baseColor;
                if (b.outline != null) b.outline.effectColor = active ? new Color(0.8f, 0.95f, 1f) : Color.clear;
            }

            foreach (var b in deckBuilderTribeButtons)
            {
                bool active = b.key.Equals(deckBuilderTribeFilter, StringComparison.OrdinalIgnoreCase);
                if (b.bg != null) b.bg.color = active ? new Color(0.52f, 0.32f, 0.20f, 0.98f) : b.baseColor;
                if (b.outline != null) b.outline.effectColor = active ? new Color(1f, 0.88f, 0.45f) : Color.clear;
            }
        }

        public void OpenDeckBuilder()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.CancelAutoEndTurn();
            }

            RebuildMasterCatalog();

            // Load existing custom deck IDs
            var currentIds = (GameManager.Instance != null)
                ? GameManager.Instance.GetCustomDeckCardIds()
                : new List<int>();

            workingCustomDeckIds.Clear();
            if (currentIds != null)
            {
                workingCustomDeckIds.AddRange(currentIds);
            }

            // Always open directly on the Monsters tab by default!
            deckBuilderTypeFilter = TypeFilter.Monsters;
            deckBuilderElementFilter = "All";
            deckBuilderTribeFilter = "All";

            if (mainMenuOverlay != null) mainMenuOverlay.SetActive(false);
            if (galleryOverlay != null) galleryOverlay.SetActive(false);
            if (battleScreenRoot != null) battleScreenRoot.SetActive(false);

            if (deckBuilderOverlay != null)
            {
                deckBuilderOverlay.SetActive(true);
                deckBuilderOverlay.transform.SetAsLastSibling();
            }

            UpdateDeckBuilderFilterHighlights();
            PopulateDeckBuilderCatalog();
            RefreshDeckBuilderTray();
        }

        public void CloseDeckBuilder()
        {
            if (deckBuilderOverlay != null) deckBuilderOverlay.SetActive(false);
            ShowMainMenu();
        }

        private void PopulateDeckBuilderCatalog()
        {
            if (deckBuilderCatalogGrid == null) return;

            for (int i = deckBuilderCatalogGrid.childCount - 1; i >= 0; i--)
            {
                Destroy(deckBuilderCatalogGrid.GetChild(i).gameObject);
            }
            deckBuilderControls.Clear();

            List<CardData> matched = new List<CardData>();
            foreach (var c in masterCatalog)
            {
                if (c == null) continue;
                if (deckBuilderTypeFilter == TypeFilter.Monsters && c.cardType != CardType.Monster) continue;
                if (deckBuilderTypeFilter == TypeFilter.Spells && c.cardType != CardType.Spell) continue;
                if (deckBuilderTypeFilter == TypeFilter.Environments && c.cardType != CardType.Environment) continue;
                if (deckBuilderTypeFilter == TypeFilter.Leaders && c.cardType != CardType.Leader) continue;

                if (deckBuilderElementFilter != "All" && deckBuilderTypeFilter != TypeFilter.Leaders)
                {
                    if (string.IsNullOrEmpty(c.element) || !c.element.Equals(deckBuilderElementFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (deckBuilderTribeFilter != "All")
                {
                    if (string.IsNullOrEmpty(c.tribe) || !c.tribe.Equals(deckBuilderTribeFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                matched.Add(c);
            }

            if (deckBuilderFilterCountText != null)
            {
                string typeName = deckBuilderTypeFilter switch
                {
                    TypeFilter.Monsters => "Monsters",
                    TypeFilter.Spells => "Spells",
                    TypeFilter.Environments => "Environments",
                    TypeFilter.Leaders => "Commander Spirits",
                    _ => "Total Cards"
                };
                string elemStr = (deckBuilderElementFilter == "All") ? "All Elements" : $"{deckBuilderElementFilter}";
                string tribeStr = (deckBuilderTribeFilter == "All") ? "All Clans" : $"{deckBuilderTribeFilter}";
                deckBuilderFilterCountText.text = $"Showing {matched.Count} {typeName} | {elemStr} • {tribeStr}";
            }

            foreach (var card in matched)
            {
                CreateDeckBuilderCatalogTile(card);
            }

            UpdateDeckBuilderCatalogBadges();
        }

        private void CreateDeckBuilderCatalogTile(CardData card)
        {
            if (card == null || deckBuilderCatalogGrid == null) return;

            Color elemColor = MonsterArtLoader.GetElementColor(card.element);
            string elemEmoji = MonsterArtLoader.GetElementEmoji(card.element);
            string tribeEmoji = MonsterArtLoader.GetTribeEmoji(card.tribe);

            var cellObj = GameBootstrapper.CreateOrnatePanel(deckBuilderCatalogGrid, $"BuilderCard_{card.id}",
                new Color(0.04f, 0.06f, 0.10f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(186f, 236f));
            var cellOut = cellObj.AddComponent<Outline>();
            cellOut.effectColor = new Color(elemColor.r, elemColor.g, elemColor.b, 0.75f);
            cellOut.effectDistance = new Vector2(1.5f, 1.5f);

            // Header (Mana pill + Name)
            var hdr = GameBootstrapper.CreatePanel(cellObj.transform, "Hdr",
                new Color(0.07f, 0.10f, 0.16f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 103f), new Vector2(178f, 24f));

            var manaPill = GameBootstrapper.CreatePanel(hdr.transform, "ManaPill",
                new Color(0.12f, 0.32f, 0.56f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-66f, 0f), new Vector2(38f, 20f));
            var manaTxt = GameBootstrapper.CreateText(manaPill.transform, "Txt", $"💎{card.cost}", 10,
                Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(38f, 20f));
            manaTxt.fontStyle = FontStyle.Bold;

            var nameTxt = GameBootstrapper.CreateText(hdr.transform, "Name", card.name, 10,
                new Color(0.95f, 0.96f, 1f), TextAnchor.MiddleLeft, new Vector2(20f, 0f), new Vector2(126f, 20f));
            nameTxt.fontStyle = FontStyle.Bold;

            // Artwork Box
            var artBox = GameBootstrapper.CreatePanel(cellObj.transform, "ArtBox",
                new Color(0.02f, 0.03f, 0.06f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 24f), new Vector2(176f, 124f));
            var artImg = artBox.GetComponent<Image>();
            artImg.preserveAspect = true;
            Sprite customArt = MonsterArtLoader.GetCardSprite(card);
            if (customArt != null)
            {
                artImg.sprite = customArt;
                artImg.color = Color.white;
            }
            else
            {
                artImg.color = new Color(elemColor.r * 0.35f, elemColor.g * 0.35f, elemColor.b * 0.35f, 0.8f);
            }

            // Clicking art adds 1 copy to deck
            var artBtn = artBox.AddComponent<Button>();
            artBtn.targetGraphic = artImg;
            artBtn.onClick.AddListener(() => AddCardToCustomDeck(card.id));

            // Tier badge
            var tierPill = GameBootstrapper.CreatePanel(artBox.transform, "TierPill",
                new Color(0.08f, 0.12f, 0.20f, 0.88f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(68f, 48f), new Vector2(32f, 18f));
            var tierTxt = GameBootstrapper.CreateText(tierPill.transform, "Txt", $"T{Mathf.Max(1, card.tier_num)}", 9,
                new Color(1f, 0.88f, 0.45f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(32f, 18f));
            tierTxt.fontStyle = FontStyle.Bold;

            // Tag badge
            string tagLabel = card.cardType switch
            {
                CardType.Monster => $"{elemEmoji}{tribeEmoji} {card.tribe}",
                CardType.Spell => $"🔮 {card.element} Spell",
                CardType.Environment => $"🌋 {card.element} Env",
                CardType.Leader => $"👑 {card.tribe}",
                _ => $"{card.element}"
            };
            var tagPill = GameBootstrapper.CreatePanel(artBox.transform, "TagPill",
                new Color(0.06f, 0.08f, 0.14f, 0.88f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-28f, -48f), new Vector2(110f, 18f));
            GameBootstrapper.CreateText(tagPill.transform, "Txt", tagLabel, 9,
                new Color(0.85f, 0.94f, 1f), TextAnchor.MiddleLeft, new Vector2(4f, 0f), new Vector2(102f, 16f));

            // Stats / Effect Row
            string statStr = card.cardType switch
            {
                CardType.Monster => $"⚔️ {card.atk}   🛡️ {card.def}",
                CardType.Spell => $"🔮 Spell • Tier {Mathf.Max(1, card.tier_num)}",
                CardType.Environment => "🌋 Environment",
                CardType.Leader => $"👑 Commander Spirit",
                _ => $"#{card.id}"
            };
            var statTxt = GameBootstrapper.CreateText(cellObj.transform, "Stat", statStr, 11,
                (card.cardType == CardType.Monster) ? new Color(0.96f, 0.88f, 0.58f) : new Color(0.78f, 0.90f, 1f),
                TextAnchor.MiddleCenter, new Vector2(0f, -48f), new Vector2(176f, 20f));
            statTxt.fontStyle = FontStyle.Bold;

            // Quantity Control Bar
            var ctrlBar = GameBootstrapper.CreatePanel(cellObj.transform, "CtrlBar",
                new Color(0.06f, 0.08f, 0.14f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -86f), new Vector2(176f, 34f));

            var minusBtn = GameBootstrapper.CreateBeveledButton(ctrlBar.transform, "Minus", "−",
                new Color(0.44f, 0.16f, 0.16f, 0.96f), Color.white,
                new Vector2(-62f, 0f), new Vector2(36f, 26f), 14,
                () => RemoveCardFromCustomDeck(card.id));

            var badgeTxt = GameBootstrapper.CreateText(ctrlBar.transform, "Badge", "0 / 2", 11,
                Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(80f, 26f));
            badgeTxt.fontStyle = FontStyle.Bold;

            var plusBtn = GameBootstrapper.CreateBeveledButton(ctrlBar.transform, "Plus", "+",
                new Color(0.14f, 0.44f, 0.24f, 0.96f), Color.white,
                new Vector2(62f, 0f), new Vector2(36f, 26f), 14,
                () => AddCardToCustomDeck(card.id));

            deckBuilderControls[card.id] = new DeckBuilderCardControl
            {
                badgeText = badgeTxt,
                plusButton = plusBtn,
                minusButton = minusBtn
            };
        }

        private void UpdateDeckBuilderCatalogBadges()
        {
            Dictionary<int, int> counts = new Dictionary<int, int>();
            foreach (var cid in workingCustomDeckIds)
            {
                counts[cid] = counts.TryGetValue(cid, out int c) ? c + 1 : 1;
            }

            int totalInDeck = workingCustomDeckIds.Count;

            foreach (var kvp in deckBuilderControls)
            {
                int cid = kvp.Key;
                var ctrl = kvp.Value;
                if (ctrl == null) continue;

                int count = counts.TryGetValue(cid, out int c) ? c : 0;

                if (ctrl.badgeText != null)
                {
                    if (count == 0)
                    {
                        ctrl.badgeText.text = "0 / 2";
                        ctrl.badgeText.color = new Color(0.6f, 0.65f, 0.75f);
                    }
                    else if (count == 1)
                    {
                        ctrl.badgeText.text = "1 / 2";
                        ctrl.badgeText.color = new Color(0.35f, 0.85f, 1f);
                    }
                    else
                    {
                        ctrl.badgeText.text = "2 / 2 (MAX)";
                        ctrl.badgeText.color = new Color(0.4f, 1f, 0.5f);
                    }
                }

                if (ctrl.plusButton != null)
                {
                    ctrl.plusButton.interactable = (count < 2 && totalInDeck < 50);
                }
                if (ctrl.minusButton != null)
                {
                    ctrl.minusButton.interactable = (count > 0);
                }
            }
        }

        private void AddCardToCustomDeck(int cardId)
        {
            if (workingCustomDeckIds.Count >= 50)
            {
                ShowDeckBuilderNotification("⚠️ Deck is full (50/50)! Remove a card or click Save.");
                return;
            }

            int count = 0;
            for (int i = 0; i < workingCustomDeckIds.Count; i++)
            {
                if (workingCustomDeckIds[i] == cardId) count++;
            }

            if (count >= 2)
            {
                ShowDeckBuilderNotification("⚠️ Maximum 2 copies allowed per card!");
                return;
            }

            workingCustomDeckIds.Add(cardId);
            UpdateDeckBuilderCatalogBadges();
            RefreshDeckBuilderTray();

            var card = masterCatalog.Find(c => c != null && c.id == cardId);
            string name = (card != null) ? card.name : $"Card #{cardId}";
            ShowDeckBuilderNotification($"Added {name} ({count + 1}/2). Total: {workingCustomDeckIds.Count}/50");
        }

        private void RemoveCardFromCustomDeck(int cardId)
        {
            int idx = workingCustomDeckIds.IndexOf(cardId);
            if (idx >= 0)
            {
                workingCustomDeckIds.RemoveAt(idx);
                UpdateDeckBuilderCatalogBadges();
                RefreshDeckBuilderTray();

                var card = masterCatalog.Find(c => c != null && c.id == cardId);
                string name = (card != null) ? card.name : $"Card #{cardId}";
                ShowDeckBuilderNotification($"Removed 1 copy of {name}. Total: {workingCustomDeckIds.Count}/50");
            }
        }

        private void ClearCustomDeck()
        {
            workingCustomDeckIds.Clear();
            UpdateDeckBuilderCatalogBadges();
            RefreshDeckBuilderTray();
            ShowDeckBuilderNotification("🗑️ Custom deck cleared (0/50 cards).");
        }

        private void RefreshDeckBuilderTray()
        {
            if (deckBuilderTrayList == null) return;

            for (int i = deckBuilderTrayList.childCount - 1; i >= 0; i--)
            {
                Destroy(deckBuilderTrayList.GetChild(i).gameObject);
            }

            int total = workingCustomDeckIds.Count;
            int mCount = 0, sCount = 0, eCount = 0, lCount = 0;
            int totalCost = 0;

            Dictionary<int, int> counts = new Dictionary<int, int>();
            Dictionary<int, CardData> cardsById = new Dictionary<int, CardData>();

            foreach (var cid in workingCustomDeckIds)
            {
                counts[cid] = counts.TryGetValue(cid, out int c) ? c + 1 : 1;
                if (!cardsById.ContainsKey(cid))
                {
                    var card = masterCatalog.Find(x => x != null && x.id == cid);
                    if (card != null) cardsById[cid] = card;
                }
            }

            List<CardData> uniqueList = new List<CardData>(cardsById.Values);
            uniqueList.Sort((a, b) =>
            {
                int typeOrderA = (int)a.cardType;
                int typeOrderB = (int)b.cardType;
                if (typeOrderA != typeOrderB) return typeOrderA.CompareTo(typeOrderB);
                if (a.cost != b.cost) return a.cost.CompareTo(b.cost);
                return string.Compare(a.name, b.name, StringComparison.Ordinal);
            });

            foreach (var card in uniqueList)
            {
                int cp = counts[card.id];
                totalCost += card.cost * cp;
                if (card.cardType == CardType.Monster) mCount += cp;
                else if (card.cardType == CardType.Spell) sCount += cp;
                else if (card.cardType == CardType.Environment) eCount += cp;
                else if (card.cardType == CardType.Leader) lCount += cp;

                CreateDeckBuilderTrayRow(card, cp);
            }

            float avgCost = (total > 0) ? (totalCost / (float)total) : 0f;

            if (deckBuilderStatusText != null)
            {
                if (total == 50)
                {
                    deckBuilderStatusText.text = "✨ 50 / 50 CARDS (READY TO BATTLE)";
                    deckBuilderStatusText.color = new Color(0.42f, 1f, 0.58f);
                }
                else if (total < 50)
                {
                    deckBuilderStatusText.text = $"⚠️ {total} / 50 CARDS ({50 - total} MORE NEEDED)";
                    deckBuilderStatusText.color = (total == 0) ? new Color(1f, 0.45f, 0.45f) : new Color(1f, 0.85f, 0.35f);
                }
                else
                {
                    deckBuilderStatusText.text = $"⚠️ {total} / 50 CARDS ({total - 50} EXCEEDED)";
                    deckBuilderStatusText.color = new Color(1f, 0.4f, 0.4f);
                }
            }

            if (deckBuilderBreakdownText != null)
            {
                deckBuilderBreakdownText.text = $"Monsters: {mCount}  •  Spells: {sCount}  •  Envs: {eCount}  •  Spirits: {lCount}";
            }

            if (deckBuilderAvgCostText != null)
            {
                deckBuilderAvgCostText.text = $"Average Mana Cost: {avgCost:F1} 💎";
            }

            if (uniqueList.Count == 0)
            {
                var emptyTxt = GameBootstrapper.CreateText(deckBuilderTrayList, "EmptyTrayText",
                    "Your custom deck is currently empty.\n\nBrowse the library on the left and click (+) on monsters or cards to add them to your deck!", 13,
                    new Color(0.70f, 0.80f, 0.92f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(480f, 160f));
                emptyTxt.lineSpacing = 1.3f;
            }
        }

        private void CreateDeckBuilderTrayRow(CardData card, int copies)
        {
            Color elemColor = MonsterArtLoader.GetElementColor(card.element);
            string elemEmoji = MonsterArtLoader.GetElementEmoji(card.element);
            string typeStr = card.cardType switch
            {
                CardType.Monster => "Monster",
                CardType.Spell => "Spell",
                CardType.Environment => "Env",
                CardType.Leader => "Spirit",
                _ => "Card"
            };

            var rowObj = GameBootstrapper.CreatePanel(deckBuilderTrayList, $"TrayRow_{card.id}",
                new Color(0.06f, 0.08f, 0.14f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(494f, 36f));
            var rowOut = rowObj.AddComponent<Outline>();
            rowOut.effectColor = new Color(elemColor.r, elemColor.g, elemColor.b, 0.45f);
            rowOut.effectDistance = new Vector2(1f, 1f);

            var le = rowObj.AddComponent<LayoutElement>();
            le.minHeight = 36f;
            le.preferredHeight = 36f;
            le.flexibleWidth = 1f;

            // Cost Pill
            var costPill = GameBootstrapper.CreatePanel(rowObj.transform, "CostPill",
                new Color(0.12f, 0.28f, 0.48f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-228f, 0f), new Vector2(30f, 24f));
            var cTxt = GameBootstrapper.CreateText(costPill.transform, "Txt", $"💎{card.cost}", 9,
                Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(30f, 24f));
            cTxt.fontStyle = FontStyle.Bold;

            // Card Name
            var nameTxt = GameBootstrapper.CreateText(rowObj.transform, "Name", card.name, 11,
                Color.white, TextAnchor.MiddleLeft, new Vector2(-80f, 0f), new Vector2(240f, 24f));
            nameTxt.fontStyle = FontStyle.Bold;

            // Tag (Element + Type)
            var tagTxt = GameBootstrapper.CreateText(rowObj.transform, "Tag", $"{elemEmoji} {typeStr}", 10,
                new Color(0.75f, 0.88f, 1f), TextAnchor.MiddleLeft, new Vector2(95f, 0f), new Vector2(90f, 22f));

            // Copies Badge
            Color copyCol = (copies == 2) ? new Color(1f, 0.88f, 0.4f) : Color.white;
            var copyTxt = GameBootstrapper.CreateText(rowObj.transform, "Copies", $"x{copies}", 12,
                copyCol, TextAnchor.MiddleCenter, new Vector2(165f, 0f), new Vector2(34f, 24f));
            copyTxt.fontStyle = FontStyle.Bold;

            // Minus Button
            GameBootstrapper.CreateBeveledButton(rowObj.transform, "BtnMinus", "−",
                new Color(0.44f, 0.16f, 0.16f, 0.95f), Color.white,
                new Vector2(202f, 0f), new Vector2(28f, 26f), 14,
                () => RemoveCardFromCustomDeck(card.id));

            // Plus Button
            var plusBtn = GameBootstrapper.CreateBeveledButton(rowObj.transform, "BtnPlus", "+",
                new Color(0.14f, 0.44f, 0.24f, 0.95f), Color.white,
                new Vector2(234f, 0f), new Vector2(28f, 26f), 14,
                () => AddCardToCustomDeck(card.id));
            plusBtn.interactable = (copies < 2 && workingCustomDeckIds.Count < 50);
        }

        private void AutoFillCustomDeck()
        {
            int needed = 50 - workingCustomDeckIds.Count;
            if (needed <= 0)
            {
                ShowDeckBuilderNotification("Deck already has 50 cards!");
                return;
            }

            Dictionary<int, int> counts = new Dictionary<int, int>();
            foreach (var cid in workingCustomDeckIds)
            {
                counts[cid] = counts.TryGetValue(cid, out int c) ? c + 1 : 1;
            }

            List<CardData> pool = new List<CardData>(masterCatalog);
            for (int i = 0; i < pool.Count; i++)
            {
                int r = UnityEngine.Random.Range(i, pool.Count);
                var temp = pool[i];
                pool[i] = pool[r];
                pool[r] = temp;
            }

            int added = 0;
            // 1. First add monsters
            foreach (var c in pool)
            {
                if (c == null || c.cardType != CardType.Monster) continue;
                int currentCopies = counts.TryGetValue(c.id, out int cp) ? cp : 0;
                while (currentCopies < 2 && workingCustomDeckIds.Count < 50)
                {
                    workingCustomDeckIds.Add(c.id);
                    currentCopies++;
                    counts[c.id] = currentCopies;
                    added++;
                    if (workingCustomDeckIds.Count >= 50) break;
                }
                if (workingCustomDeckIds.Count >= 50) break;
            }

            // 2. If still needed, add spells
            if (workingCustomDeckIds.Count < 50)
            {
                foreach (var c in pool)
                {
                    if (c == null || c.cardType != CardType.Spell) continue;
                    int currentCopies = counts.TryGetValue(c.id, out int cp) ? cp : 0;
                    while (currentCopies < 2 && workingCustomDeckIds.Count < 50)
                    {
                        workingCustomDeckIds.Add(c.id);
                        currentCopies++;
                        counts[c.id] = currentCopies;
                        added++;
                        if (workingCustomDeckIds.Count >= 50) break;
                    }
                    if (workingCustomDeckIds.Count >= 50) break;
                }
            }

            UpdateDeckBuilderCatalogBadges();
            RefreshDeckBuilderTray();
            ShowDeckBuilderNotification($"✨ Auto-filled {added} cards to complete 50/50 deck!");
        }

        private void SaveAndEquipCustomDeck()
        {
            if (workingCustomDeckIds.Count != 50)
            {
                int remaining = 50 - workingCustomDeckIds.Count;
                if (remaining > 0)
                {
                    ShowDeckBuilderNotification($"⚠️ Please complete all 50 cards before saving! ({remaining} more needed, or click AUTO-FILL).");
                }
                else
                {
                    ShowDeckBuilderNotification($"⚠️ Deck has {workingCustomDeckIds.Count} cards! Maximum is exactly 50 cards.");
                }
                return;
            }

            if (GameManager.Instance != null)
            {
                GameManager.Instance.SaveCustomDeck(workingCustomDeckIds, "Player Custom Deck");
                GameManager.Instance.SetPlayerDeck(GameManager.PlayerDeckSelection.CustomJson);
            }

            CloseDeckBuilder();
            RefreshOpponentAndDeckBadges();
        }

        private void ShowDeckBuilderNotification(string msg)
        {
            if (deckBuilderNoticeText == null) return;
            deckBuilderNoticeText.text = msg;
            if (deckBuilderNoticeCoroutine != null) StopCoroutine(deckBuilderNoticeCoroutine);
            deckBuilderNoticeCoroutine = StartCoroutine(ClearNoticeAfterDelay());
        }

        private System.Collections.IEnumerator ClearNoticeAfterDelay()
        {
            yield return new WaitForSeconds(3.5f);
            if (deckBuilderNoticeText != null) deckBuilderNoticeText.text = "";
        }

        // =========================================================================
        // 3. CATALOG BUILDING & FILTERING
        // =========================================================================
        private void RebuildMasterCatalog()
        {
            masterCatalog.Clear();
            var gm = GameManager.Instance;

            // 1. Monsters (sorted by ID)
            if (gm != null && gm.allMonsters != null && gm.allMonsters.Count > 0)
            {
                var monstersCopy = new List<MonsterData>(gm.allMonsters);
                monstersCopy.Sort((a, b) => (a?.id ?? 0).CompareTo(b?.id ?? 0));
                foreach (var m in monstersCopy)
                {
                    if (m != null) masterCatalog.Add(m);
                }
            }

            // 2. Spells (sorted by ID)
            var spells = SpellDatabase.GetAllSpells();
            if (spells != null && spells.Count > 0)
            {
                var spellsCopy = new List<CardData>(spells);
                spellsCopy.Sort((a, b) => (a?.id ?? 0).CompareTo(b?.id ?? 0));
                foreach (var s in spellsCopy)
                {
                    if (s != null) masterCatalog.Add(s);
                }
            }

            // 3. Environments (All 10 Elements = 10 unified Environment Cards)
            string[] envElems = { "Fire", "Water", "Ice", "Electric", "Earth", "Nature", "Air", "Light", "Dark", "Desert" };
            foreach (var el in envElems)
            {
                var envCard = new EnvironmentCardData(el, 1);
                envCard.name = $"{el} Environment";
                envCard.tier_num = 1;
                masterCatalog.Add(envCard);
            }

            // 4. Commander Spirits / Leaders (sorted by ID)
            if (gm != null && gm.allLeaders != null && gm.allLeaders.Count > 0)
            {
                var leadersCopy = new List<CardData>(gm.allLeaders);
                leadersCopy.Sort((a, b) => (a?.id ?? 0).CompareTo(b?.id ?? 0));
                foreach (var l in leadersCopy)
                {
                    if (l != null) masterCatalog.Add(l);
                }
            }
        }

        public static bool HasCardArtwork(CardData card)
        {
            if (card == null) return false;
            if (card.cardType == CardType.Environment)
            {
                int lv = Mathf.Max(1, card.envLevel);
                return BattlefieldAtmosphereEngine.GetOrLoadGroundSprite(card.element, lv) != null
                    || MonsterArtLoader.GetCardSprite(card) != null;
            }
            return MonsterArtLoader.GetCardSprite(card) != null;
        }

        private void ApplyFiltersAndRefresh(int targetPage)
        {
            filteredCards.Clear();
            int hasArtCount = 0;
            int missingArtCount = 0;

            foreach (var c in masterCatalog)
            {
                if (c == null) continue;

                // Type filter
                if (currentTypeFilter == TypeFilter.Monsters && c.cardType != CardType.Monster) continue;
                if (currentTypeFilter == TypeFilter.Spells && c.cardType != CardType.Spell) continue;
                if (currentTypeFilter == TypeFilter.Environments && c.cardType != CardType.Environment) continue;
                if (currentTypeFilter == TypeFilter.Leaders && c.cardType != CardType.Leader) continue;

                // Element filter (Leaders have NO element association; ignore element filter if browsing Leaders tab)
                if (currentElementFilter != "All" && currentTypeFilter != TypeFilter.Leaders)
                {
                    if (string.IsNullOrEmpty(c.element) || !c.element.Equals(currentElementFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Tribe filter
                if (currentTribeFilter != "All")
                {
                    if (string.IsNullOrEmpty(c.tribe) || !c.tribe.Equals(currentTribeFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Tier filter
                if (currentTierFilter > 0 && Mathf.Max(1, c.tier_num) != currentTierFilter)
                {
                    continue;
                }

                bool hasArt = HasCardArtwork(c);
                bool has25D = (c.cardType == CardType.Monster) && MonsterArtLoader.HasStandby25DAssets(c);

                // Art Status filter
                if (currentStatusFilter == ArtStatusFilter.HasArt && !hasArt) continue;
                if (currentStatusFilter == ArtStatusFilter.MissingArt && hasArt) continue;
                if (currentStatusFilter == ArtStatusFilter.Has25D && !has25D) continue;

                filteredCards.Add(c);
                if (hasArt) hasArtCount++;
                else missingArtCount++;
            }

            UpdateFilterButtonHighlights();

            int pageSize = (currentViewMode == GalleryViewMode.BothHandAndField) ? 8 : 12;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(filteredCards.Count / (float)pageSize));
            currentPage = Mathf.Clamp(targetPage, 0, totalPages - 1);

            if (headerStatsText != null)
            {
                headerStatsText.text =
                    $"🎴 FILTER RESULTS: {filteredCards.Count} Cards   |   ✅ HAS ART: {hasArtCount}   |   ⚠️ MISSING ART: {missingArtCount}";
            }

            if (pageIndicatorText != null)
            {
                pageIndicatorText.text = $"Page {currentPage + 1} / {totalPages}  (Showing {filteredCards.Count} Cards)";
            }

            PopulateGridPage();

            if (filteredCards.Count > 0)
            {
                int firstIdx = currentPage * pageSize;
                if (selectedCard == null || !filteredCards.Contains(selectedCard))
                {
                    SelectCardForInspector(filteredCards[Mathf.Clamp(firstIdx, 0, filteredCards.Count - 1)]);
                }
                else
                {
                    SelectCardForInspector(selectedCard);
                }
            }
            else
            {
                SelectCardForInspector(null);
            }
        }

        private void ChangePage(int delta)
        {
            ApplyFiltersAndRefresh(currentPage + delta);
        }

        // =========================================================================
        // 4. GRID RENDERING (Hand View + Battlefield Slot View)
        // =========================================================================
        private void PopulateGridPage()
        {
            if (gridContainer == null) return;

            for (int i = gridContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(gridContainer.GetChild(i).gameObject);
            }

            if (filteredCards.Count == 0)
            {
                var emptyTxt = GameBootstrapper.CreateText(gridContainer, "EmptyFilterText",
                    "No cards match the selected filters.", 18,
                    new Color(0.75f, 0.82f, 0.92f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(800f, 60f));
                emptyTxt.fontStyle = FontStyle.Bold;
                return;
            }

            bool isBoth = (currentViewMode == GalleryViewMode.BothHandAndField);
            int cols = isBoth ? 4 : 6;
            int rows = 2;
            int pageSize = cols * rows;
            int startIdx = currentPage * pageSize;
            int endIdx = Mathf.Min(filteredCards.Count, startIdx + pageSize);

            float cellW = isBoth ? 316f : 210f;
            float cellH = 364f;
            float gapX = isBoth ? 10f : 8f;
            float gapY = 12f;

            float totalW = cols * cellW + (cols - 1) * gapX;
            float totalH = rows * cellH + (rows - 1) * gapY;
            float startX = -totalW * 0.5f + cellW * 0.5f;
            float startY = totalH * 0.5f - cellH * 0.5f;

            for (int idx = startIdx; idx < endIdx; idx++)
            {
                int local = idx - startIdx;
                int r = local / cols;
                int c = local % cols;

                float x = startX + c * (cellW + gapX);
                float y = startY - r * (cellH + gapY);

                CardData card = filteredCards[idx];
                BuildGridCell(gridContainer, card, new Vector2(x, y), new Vector2(cellW, cellH), isBoth);
            }
        }

        private void BuildGridCell(Transform parent, CardData card, Vector2 pos, Vector2 size, bool isBoth)
        {
            bool hasArt = HasCardArtwork(card);
            Color borderCol = hasArt
                ? new Color(0.22f, 0.62f, 0.36f, 0.85f)
                : new Color(0.78f, 0.36f, 0.18f, 0.90f);

            var cellObj = GameBootstrapper.CreateOrnatePanel(parent, $"GalleryCell_{card.id}",
                new Color(0.05f, 0.07f, 0.12f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            var cellOut = cellObj.AddComponent<Outline>();
            cellOut.effectColor = borderCol;
            cellOut.effectDistance = new Vector2(1.5f, 1.5f);

            // Header bar inside cell
            var hdr = GameBootstrapper.CreatePanel(cellObj.transform, "CellHeader",
                new Color(0.08f, 0.11f, 0.18f, 0.96f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -15f), new Vector2(size.x - 8f, 24f));

            string elemEmoji = MonsterArtLoader.GetElementEmoji(card.element);
            string tribeEmoji = MonsterArtLoader.GetTribeEmoji(card.tribe);
            string typeTag = card.cardType switch
            {
                CardType.Monster => $"#{card.id} {elemEmoji}{tribeEmoji} T{Mathf.Max(1, card.tier_num)}",
                CardType.Spell => $"🔮 #{card.id} T{Mathf.Max(1, card.tier_num)}",
                CardType.Environment => $"🌋 {card.element} Environment",
                CardType.Leader => $"👑 #{card.id} {tribeEmoji} {card.tribe}",
                _ => $"#{card.id}"
            };

            var hdrTxt = GameBootstrapper.CreateText(hdr.transform, "HdrText", typeTag, 11,
                new Color(0.92f, 0.96f, 1f), TextAnchor.MiddleLeft, new Vector2(-32f, 0f), new Vector2(size.x - 84f, 22f));
            hdrTxt.fontStyle = FontStyle.Bold;
            hdrTxt.raycastTarget = false;

            string statusLabel = hasArt ? "✅ READY" : "⚠️ MISSING";
            Color statusCol = hasArt ? new Color(0.42f, 0.98f, 0.58f) : new Color(1f, 0.62f, 0.36f);
            var stTxt = GameBootstrapper.CreateText(hdr.transform, "StatusText", statusLabel, 10,
                statusCol, TextAnchor.MiddleRight, new Vector2(size.x * 0.5f - 44f, 0f), new Vector2(78f, 22f));
            stTxt.fontStyle = FontStyle.Bold;
            stTxt.raycastTarget = false;

            if (isBoth)
            {
                var subH = GameBootstrapper.CreateText(cellObj.transform, "SubHand", "✋ HAND", 10,
                    new Color(0.75f, 0.86f, 0.98f), TextAnchor.MiddleCenter, new Vector2(-76f, 140f), new Vector2(140f, 18f));
                subH.raycastTarget = false;

                var subF = GameBootstrapper.CreateText(cellObj.transform, "SubField", "🏟️ FIELD", 10,
                    new Color(0.78f, 0.95f, 0.84f), TextAnchor.MiddleCenter, new Vector2(76f, 140f), new Vector2(140f, 18f));
                subF.raycastTarget = false;

                // Left: Player Hand Card (scaled 0.92)
                var handWrap = new GameObject("HandWrap");
                handWrap.transform.SetParent(cellObj.transform, false);
                var hwRt = handWrap.AddComponent<RectTransform>();
                hwRt.anchorMin = new Vector2(0.5f, 0.5f);
                hwRt.anchorMax = new Vector2(0.5f, 0.5f);
                hwRt.anchoredPosition = new Vector2(-77f, -12f);
                hwRt.localScale = new Vector3(0.92f, 0.92f, 1f);
                BuildHandPreviewInto(handWrap.transform, card);

                // Right: Battlefield Slot Token (scaled 0.88)
                var fieldWrap = new GameObject("FieldWrap");
                fieldWrap.transform.SetParent(cellObj.transform, false);
                var fwRt = fieldWrap.AddComponent<RectTransform>();
                fwRt.anchorMin = new Vector2(0.5f, 0.5f);
                fwRt.anchorMax = new Vector2(0.5f, 0.5f);
                fwRt.anchoredPosition = new Vector2(77f, -12f);
                fwRt.localScale = new Vector3(0.88f, 0.88f, 1f);
                BuildFieldPreviewInto(fieldWrap.transform, card);
            }
            else if (currentViewMode == GalleryViewMode.HandOnly)
            {
                var handWrap = new GameObject("HandWrapSingle");
                handWrap.transform.SetParent(cellObj.transform, false);
                var hwRt = handWrap.AddComponent<RectTransform>();
                hwRt.anchorMin = new Vector2(0.5f, 0.5f);
                hwRt.anchorMax = new Vector2(0.5f, 0.5f);
                hwRt.anchoredPosition = new Vector2(0f, -12f);
                hwRt.localScale = new Vector3(1.18f, 1.18f, 1f);
                BuildHandPreviewInto(handWrap.transform, card);
            }
            else
            {
                var fieldWrap = new GameObject("FieldWrapSingle");
                fieldWrap.transform.SetParent(cellObj.transform, false);
                var fwRt = fieldWrap.AddComponent<RectTransform>();
                fwRt.anchorMin = new Vector2(0.5f, 0.5f);
                fwRt.anchorMax = new Vector2(0.5f, 0.5f);
                fwRt.anchoredPosition = new Vector2(0f, -12f);
                fwRt.localScale = new Vector3(1.12f, 1.12f, 1f);
                BuildFieldPreviewInto(fieldWrap.transform, card);
            }

            // Footer info strip inside cell
            string footerText = card.cardType switch
            {
                CardType.Monster => $"💎{card.cost}   ⚔️{card.atk}   🛡️{card.def}   • {card.role}",
                CardType.Spell => $"💎{card.cost}   • {card.role}",
                CardType.Environment => $"💎{card.cost}   • Permanent Arena Weather (Levels 1-3)",
                CardType.Leader => $"💎{card.cost}   • {MonsterArtLoader.FormatSpiritTitle(card)}",
                _ => ""
            };
            var ftr = GameBootstrapper.CreateText(cellObj.transform, "CellFooter", footerText, 10,
                new Color(0.84f, 0.90f, 0.96f), TextAnchor.MiddleCenter, new Vector2(0f, -size.y * 0.5f + 14f), new Vector2(size.x - 10f, 20f));
            ftr.raycastTarget = false;

            // Invisible full-cell click button to inspect card in the Right Stage
            var clickBtnObj = GameBootstrapper.CreatePanel(cellObj.transform, "CellClickBtn", new Color(0f, 0f, 0f, 0.001f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var btn = clickBtnObj.AddComponent<Button>();
            CardData captured = card;
            btn.onClick.AddListener(() => SelectCardForInspector(captured));
        }

        private void BuildHandPreviewInto(Transform parent, CardData card)
        {
            if (BattleUIManager.Instance != null && card != null)
            {
                var handCardObj = BattleUIManager.Instance.CreateDynamicCardUI(parent, card);
                if (handCardObj != null)
                {
                    var rt = handCardObj.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.anchorMin = new Vector2(0.5f, 0.5f);
                        rt.anchorMax = new Vector2(0.5f, 0.5f);
                        rt.anchoredPosition = Vector2.zero;
                    }
                    foreach (var g in handCardObj.GetComponentsInChildren<Graphic>(true))
                    {
                        if (g != null) g.raycastTarget = false;
                    }
                }
            }
        }

        /// <summary>
        /// Builds the authentic Battlefield (Field Slot) representation for any card type
        /// so the user can see how the card looks when deployed on the board.
        /// </summary>
        private void BuildFieldPreviewInto(Transform parent, CardData card)
        {
            if (card == null) return;
            Color elemColor = MonsterArtLoader.GetElementColor(card.element);

            var root = GameBootstrapper.CreatePanel(parent, "FieldSlotPreviewRoot", new Color(0.03f, 0.04f, 0.07f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(156f, 196f));
            root.GetComponent<Image>().raycastTarget = false;

            if (card.cardType == CardType.Monster)
            {
                // 1. Stone Pedestal & Glowing Summoning Circle
                var pedObj = GameBootstrapper.CreatePanel(root.transform, "Pedestal", Color.white,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(152f, 188f));
                var pedImg = pedObj.GetComponent<Image>();
                pedImg.sprite = GameBootstrapper.GetSlotPedestalSprite(false);
                pedImg.color = new Color(0.75f, 0.82f, 0.92f, 0.85f);
                pedImg.raycastTarget = false;

                var circleObj = GameBootstrapper.CreatePanel(root.transform, "SummoningCircle", Color.white,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(144f, 144f));
                var circleImg = circleObj.GetComponent<Image>();
                circleImg.sprite = GameBootstrapper.GetSummoningCircleSprite();
                circleImg.color = new Color(elemColor.r, elemColor.g, elemColor.b, 0.72f);
                circleImg.raycastTarget = false;

                // 2. Field Card Token (148 x 184)
                var token = GameBootstrapper.CreatePanel(root.transform, "FieldCardToken", new Color(0.04f, 0.05f, 0.08f, 1f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(148f, 184f));
                token.GetComponent<Image>().raycastTarget = false;

                Sprite customArt = MonsterArtLoader.GetCardSprite(card);
                var artObj = new GameObject("MonsterArt");
                artObj.transform.SetParent(token.transform, false);
                var rectArt = artObj.AddComponent<RectTransform>();
                rectArt.anchorMin = Vector2.zero;
                rectArt.anchorMax = Vector2.one;
                rectArt.sizeDelta = Vector2.zero;
                var artImg = artObj.AddComponent<Image>();
                artImg.raycastTarget = false;

                if (customArt != null)
                {
                    artImg.sprite = customArt;
                    artImg.color = Color.white;
                    if (MonsterArtLoader.HasStandby25DAssets(card))
                    {
                        var anim = artObj.AddComponent<MonsterStandbyAnimator>();
                        anim.Configure(card, false);
                    }
                }
                else
                {
                    artImg.color = MonsterArtLoader.GetEnvironmentAtmosphereColor(card.element);
                    var idTxt = GameBootstrapper.CreateText(token.transform, "MissingIdText", $"#{card.id}\n⚠️ NO ART", 16,
                        elemColor, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(130f, 64f));
                    idTxt.fontStyle = FontStyle.Bold;
                    idTxt.raycastTarget = false;
                }

                // Elemental Frame Overlay
                var frameObj = new GameObject("FrameOverlay");
                frameObj.transform.SetParent(token.transform, false);
                var rectFrame = frameObj.AddComponent<RectTransform>();
                rectFrame.anchorMin = Vector2.zero;
                rectFrame.anchorMax = Vector2.one;
                rectFrame.sizeDelta = Vector2.zero;
                var frameImg = frameObj.AddComponent<Image>();
                frameImg.sprite = MonsterArtLoader.GetCardFrameOverlaySprite(card.element);
                frameImg.color = Color.white;
                frameImg.preserveAspect = false;
                frameImg.raycastTarget = false;

                // Top-Left Clan Emblem Badge (enlarged & inset cleanly away from frame edges)
                Sprite clanSprite = MonsterArtLoader.GetClanEmblemSprite(card.tribe);
                if (clanSprite != null)
                {
                    var clanObj = new GameObject("ClanEmblemBadge");
                    clanObj.transform.SetParent(token.transform, false);
                    var rectClan = clanObj.AddComponent<RectTransform>();
                    rectClan.anchorMin = new Vector2(0f, 1f);
                    rectClan.anchorMax = new Vector2(0f, 1f);
                    rectClan.sizeDelta = new Vector2(37f, 37f);
                    rectClan.anchoredPosition = new Vector2(22.5f, -22.5f);
                    var clanImg = clanObj.AddComponent<Image>();
                    clanImg.sprite = clanSprite;
                    clanImg.color = Color.white;
                    clanImg.preserveAspect = true;
                    clanImg.raycastTarget = false;
                }

                // Cost, ATK, DEF numbers
                var txtCost = GameBootstrapper.CreateText(token.transform, "CostText", card.cost.ToString(), 14,
                    new Color(0.92f, 0.95f, 1f), TextAnchor.MiddleCenter, new Vector2(0f, -11f), new Vector2(28f, 22f));
                txtCost.rectTransform.anchorMin = new Vector2(0.5f, 1f);
                txtCost.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                txtCost.rectTransform.anchoredPosition = new Vector2(0f, -11f);
                txtCost.fontStyle = FontStyle.Bold;
                txtCost.raycastTarget = false;
                GameBootstrapper.AddTextShadow(txtCost);

                string atkVal = (card.cardType == CardType.Environment) ? "∞" : card.atk.ToString();
                int atkSize = (card.cardType == CardType.Environment) ? 17 : 15;
                var txtAtk = GameBootstrapper.CreateText(token.transform, "AtkText", atkVal, atkSize,
                    Color.white, TextAnchor.MiddleCenter, new Vector2(17.5f, 20.5f), new Vector2(26f, 22f));
                txtAtk.rectTransform.anchorMin = new Vector2(0f, 0f);
                txtAtk.rectTransform.anchorMax = new Vector2(0f, 0f);
                txtAtk.rectTransform.anchoredPosition = new Vector2(17.5f, 20.5f);
                txtAtk.fontStyle = FontStyle.Bold;
                txtAtk.raycastTarget = false;
                GameBootstrapper.AddTextShadow(txtAtk);

                string defVal = (card.cardType == CardType.Environment) ? "∞" : card.def.ToString();
                int defSize = (card.cardType == CardType.Environment) ? 17 : 15;
                var txtDef = GameBootstrapper.CreateText(token.transform, "DefText", defVal, defSize,
                    Color.white, TextAnchor.MiddleCenter, new Vector2(-17.5f, 20.5f), new Vector2(26f, 22f));
                txtDef.rectTransform.anchorMin = new Vector2(1f, 0f);
                txtDef.rectTransform.anchorMax = new Vector2(1f, 0f);
                txtDef.rectTransform.anchoredPosition = new Vector2(-17.5f, 20.5f);
                txtDef.fontStyle = FontStyle.Bold;
                txtDef.raycastTarget = false;
                GameBootstrapper.AddTextShadow(txtDef);

                // 3. Active Ability Sigils on Battlefield Preview (🔵 Blue for Skill 1, 🔴 Red for Skill 2)
                List<string> previewActives = new List<string>();
                if (card.active_skills != null && card.active_skills.Count > 0)
                {
                    previewActives.AddRange(card.active_skills);
                }
                else if (!string.IsNullOrEmpty(card.active_skill) && !card.active_skill.StartsWith("None"))
                {
                    foreach (var a in card.active_skill.Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string clean = a.Trim();
                        if (!string.IsNullOrEmpty(clean) && !clean.StartsWith("None")) previewActives.Add(clean);
                    }
                }

                if (previewActives.Count >= 1)
                {
                    bool twoSkills = previewActives.Count >= 2;
                    CreateFieldPreviewSigil(root.transform, "PreviewSigil1",
                        twoSkills ? new Vector2(-22f, -68f) : new Vector2(0f, -68f),
                        SlotView.Skill1BlueColor, new Color(0.06f, 0.34f, 0.68f, 0.98f),
                        GetPreviewSkillCost(previewActives[0]));

                    if (twoSkills)
                    {
                        CreateFieldPreviewSigil(root.transform, "PreviewSigil2",
                            new Vector2(22f, -68f),
                            SlotView.Skill2RedColor, new Color(0.68f, 0.10f, 0.18f, 0.98f),
                            GetPreviewSkillCost(previewActives[1]));
                    }
                }
            }
            else if (card.cardType == CardType.Environment)
            {
                int lv = Mathf.Max(1, card.envLevel);
                Sprite groundArt = BattlefieldAtmosphereEngine.GetOrLoadGroundSprite(card.element, lv)
                                ?? MonsterArtLoader.GetCardSprite(card);

                var groundObj = GameBootstrapper.CreatePanel(root.transform, "ArenaGroundPreview", Color.white,
                    Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-8f, -8f));
                var gImg = groundObj.GetComponent<Image>();
                gImg.raycastTarget = false;
                if (groundArt != null)
                {
                    gImg.sprite = groundArt;
                    gImg.color = Color.white;
                }
                else
                {
                    gImg.color = MonsterArtLoader.GetEnvironmentAtmosphereColor(card.element);
                }
            }
            else if (card.cardType == CardType.Leader)
            {
                Color catCol = MonsterArtLoader.GetSpiritCategoryColor(card, true);
                var halo = GameBootstrapper.CreateCirclePanel(root.transform, "AltarHalo",
                    new Color(catCol.r, catCol.g, catCol.b, 0.28f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(136f, 136f));
                halo.GetComponent<Image>().raycastTarget = false;

                var disc = GameBootstrapper.CreateCirclePanel(halo.transform, "AltarDisc",
                    new Color(0.04f, 0.05f, 0.09f, 1f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(130f, 130f));
                disc.GetComponent<Image>().raycastTarget = false;
                var mask = disc.AddComponent<Mask>();
                mask.showMaskGraphic = false;

                Sprite spArt = MonsterArtLoader.GetCardSprite(card);
                if (spArt != null)
                {
                    var artObj = GameBootstrapper.CreatePanel(disc.transform, "SpiritArt", Color.white,
                        Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    var aImg = artObj.GetComponent<Image>();
                    aImg.sprite = spArt;
                    aImg.preserveAspect = true;
                    aImg.raycastTarget = false;
                }
                else
                {
                    var crestObj = GameBootstrapper.CreatePanel(disc.transform, "SpiritCrest", Color.Lerp(catCol, Color.white, 0.3f),
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84f, 84f));
                    var cImg = crestObj.GetComponent<Image>();
                    cImg.sprite = GameBootstrapper.GetSpiritCrestSprite();
                    cImg.raycastTarget = false;

                    var rimObj = GameBootstrapper.CreatePanel(halo.transform, "AltarRim", Color.Lerp(catCol, Color.white, 0.35f),
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(128f, 128f));
                    var rImg = rimObj.GetComponent<Image>();
                    rImg.sprite = GameBootstrapper.GetCircularSpiritRimSprite();
                    rImg.raycastTarget = false;
                }

                var lbl = GameBootstrapper.CreateText(root.transform, "AltarLbl", "SPIRIT ALTAR ON BOARD", 10,
                    Color.Lerp(catCol, Color.white, 0.5f), TextAnchor.MiddleCenter, new Vector2(0f, -74f), new Vector2(144f, 20f));
                lbl.fontStyle = FontStyle.Bold;
                lbl.raycastTarget = false;
            }
            else if (card.cardType == CardType.Spell)
            {
                var circleObj = GameBootstrapper.CreatePanel(root.transform, "SpellCastCircle",
                    new Color(0.58f, 0.36f, 0.95f, 0.72f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(138f, 138f));
                var cImg = circleObj.GetComponent<Image>();
                cImg.sprite = GameBootstrapper.GetSummoningCircleSprite();
                cImg.raycastTarget = false;

                Sprite spArt = MonsterArtLoader.GetCardSprite(card);
                if (spArt != null)
                {
                    var artObj = GameBootstrapper.CreatePanel(root.transform, "SpellArtField", Color.white,
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(104f, 126f));
                    var aImg = artObj.GetComponent<Image>();
                    aImg.sprite = spArt;
                    aImg.preserveAspect = true;
                    aImg.raycastTarget = false;
                }
                else
                {
                    var iconTxt = GameBootstrapper.CreateText(root.transform, "SpellIconTxt",
                        $"🔮\n#{card.id}", 24, new Color(0.88f, 0.78f, 1f),
                        TextAnchor.MiddleCenter, new Vector2(0f, 12f), new Vector2(110f, 80f));
                    iconTxt.fontStyle = FontStyle.Bold;
                    iconTxt.raycastTarget = false;
                }

                var lbl = GameBootstrapper.CreateText(root.transform, "SpellFieldLbl", "BATTLEFIELD SPELL CAST", 10,
                    new Color(0.86f, 0.78f, 1f), TextAnchor.MiddleCenter, new Vector2(0f, -74f), new Vector2(144f, 20f));
                lbl.fontStyle = FontStyle.Bold;
                lbl.raycastTarget = false;
            }
        }

        // =========================================================================
        // 5. RIGHT-SIDE INSPECTOR & MISSING ASSET CHECKLIST
        // =========================================================================
        private void SelectCardForInspector(CardData card)
        {
            selectedCard = card;

            if (inspectorHandHolder != null)
            {
                for (int i = inspectorHandHolder.childCount - 1; i >= 0; i--)
                    Destroy(inspectorHandHolder.GetChild(i).gameObject);
            }
            if (inspectorFieldHolder != null)
            {
                for (int i = inspectorFieldHolder.childCount - 1; i >= 0; i--)
                    Destroy(inspectorFieldHolder.GetChild(i).gameObject);
            }

            if (card == null)
            {
                if (inspectorTitleText != null) inspectorTitleText.text = "NO CARD SELECTED";
                if (inspectorMetaText != null) inspectorMetaText.text = "";
                if (inspectorChecklistText != null) inspectorChecklistText.text = "";
                if (inspectorAbilitiesText != null) inspectorAbilitiesText.text = "";
                return;
            }

            // Build Large Hand & Field Previews
            if (inspectorHandHolder != null)
            {
                var wrapH = new GameObject("LargeHandPreview");
                wrapH.transform.SetParent(inspectorHandHolder, false);
                var rtH = wrapH.AddComponent<RectTransform>();
                rtH.anchoredPosition = Vector2.zero;
                rtH.localScale = new Vector3(1.18f, 1.18f, 1f);
                BuildHandPreviewInto(wrapH.transform, card);
            }

            if (inspectorFieldHolder != null)
            {
                var wrapF = new GameObject("LargeFieldPreview");
                wrapF.transform.SetParent(inspectorFieldHolder, false);
                var rtF = wrapF.AddComponent<RectTransform>();
                rtF.anchoredPosition = Vector2.zero;
                rtF.localScale = new Vector3(1.12f, 1.12f, 1f);
                BuildFieldPreviewInto(wrapF.transform, card);
            }

            string elemEmoji = MonsterArtLoader.GetElementEmoji(card.element);
            string tribeEmoji = MonsterArtLoader.GetTribeEmoji(card.tribe);

            if (inspectorTitleText != null)
            {
                inspectorTitleText.text = $"CARD #{card.id} — {card.name} ({card.cardType})";
            }

            if (inspectorMetaText != null)
            {
                string tribePart = string.IsNullOrEmpty(card.tribe) ? "Universal" : $"{tribeEmoji} {card.tribe}";
                if (card.cardType == CardType.Leader)
                {
                    inspectorMetaText.text = $"Clan: {tribePart}   |   Role: Commander Spirit (Neutral)   |   Cost: {card.cost} Mana";
                }
                else
                {
                    inspectorMetaText.text = $"Element: {elemEmoji} {card.element}   |   Clan: {tribePart}   |   Tier: T{Mathf.Max(1, card.tier_num)}";
                }
            }

            // Build Asset Completion Checklist
            bool hasArt = HasCardArtwork(card);
            bool hasElemFrame = MonsterArtLoader.GetCardFrameOverlaySprite(card.element) != null;
            bool hasClanEmblem = string.IsNullOrEmpty(card.tribe) || MonsterArtLoader.GetClanEmblemSprite(card.tribe) != null;
            bool has25D = (card.cardType == CardType.Monster) && MonsterArtLoader.HasStandby25DAssets(card);

            string expectedArtFile = card.cardType switch
            {
                CardType.Monster => $"CardsArt/Monsters/{card.id}.jpg (or {card.id}.png)",
                CardType.Spell => $"CardsArt/Spells/{card.id}.jpg (or {card.id}.png)",
                CardType.Environment => $"env_{card.element.ToLowerInvariant()}_lv1.png",
                CardType.Leader => $"CardsArt/Leaders/{card.id}.png (or {card.id}.jpg)",
                _ => $"{card.id}.png"
            };

            if (inspectorChecklistText != null)
            {
                string line1 = hasArt
                    ? $"✅ Primary Card Artwork: READY ({expectedArtFile})"
                    : $"❌ Primary Card Artwork: MISSING! (Expected: {expectedArtFile})";

                string line2 = (card.cardType == CardType.Monster)
                    ? (hasElemFrame ? $"✅ Elemental Frame ({card.element}): READY (card_frame_{card.element.ToLowerInvariant()}.png)" : $"❌ Elemental Frame ({card.element}): MISSING!")
                    : (card.cardType == CardType.Leader ? "👑 Commander Spirit Medallion: READY (Neutral Clan Aura)" : $"✅ Special Frame ({card.cardType}): READY");

                string line3 = (card.cardType == CardType.Monster && !string.IsNullOrEmpty(card.tribe))
                    ? (hasClanEmblem ? $"✅ Top-Left Clan Emblem ({card.tribe}): READY (clan_emblem_{card.tribe.ToLowerInvariant()}.png)" : $"❌ Clan Emblem ({card.tribe}): MISSING!")
                    : "➖ Top-Left Clan Emblem: N/A for this card type";

                string line4 = (card.cardType == CardType.Monster)
                    ? (has25D ? $"🎬 2.5D Standby / Depth Animation: READY" : $"⚪ 2.5D Standby Animation: Static 2D Art Only")
                    : "";

                inspectorChecklistText.text = $"{line1}\n{line2}\n{line3}\n{line4}";
            }

            if (inspectorAbilitiesText != null)
            {
                inspectorAbilitiesText.supportRichText = true;
                if (card.cardType == CardType.Monster)
                {
                    inspectorAbilitiesText.text =
                        $"💎 Cost: {card.cost} Mana   |   ⚔️ ATK: {card.atk}   |   🛡️ DEF: {card.def}   |   Role: {card.role}\n\n" +
                        BattleUIManager.FormatMonsterAbilityRichText(card, null);
                }
                else if (card.cardType == CardType.Environment)
                {
                    string elemName = EnvironmentCardData.NormalizeElement(card.element);
                    int lvl = Mathf.Max(1, card.envLevel);
                    if (EnvironmentCardData.TryGetProfile(elemName, out var prof))
                    {
                        int atkPct = Mathf.RoundToInt(prof.atkBuff[lvl - 1] * 100f);
                        int armPct = Mathf.RoundToInt(prof.defArmor[lvl - 1] * 100f);
                        int debPct = Mathf.RoundToInt(prof.oppDebuff[lvl - 1] * 100f);
                        string debList = string.Join(", ", prof.debuffs);
                        string armStr = armPct > 0 ? $" & +{armPct}% Armor" : "";

                        inspectorAbilitiesText.text =
                            $"💎 Cost: {card.cost} Mana   |   ⚔️ ATK: ∞   |   🛡️ DEF: ∞   |   Role: Shared Environment\n\n" +
                            $"<color=#58E085><b>🌍 [ENVIRONMENT EFFECT - LEVEL {lvl}]:</b></color>\n" +
                            $"<color=#FFD866>• Aligned Allies ({elemName}):</color> +{atkPct}% ATK{armStr}\n" +
                            $"<color=#FF7B72>• Opposing Elements ({debList}):</color> -{debPct}% ATK\n" +
                            $"<color=#79C0FF>• Unique Weather Perk:</color> {prof.perkDesc}\n\n" +
                            $"<color=#8B949E><i>Permanent climate across rounds until countered by an opponent environment card.</i></color>";
                    }
                    else
                    {
                        inspectorAbilitiesText.text =
                            $"💎 Cost: {card.cost} Mana   |   ⚔️ ATK: ∞   |   🛡️ DEF: ∞   |   Role: Shared Environment\n\n" +
                            $"• Effect Description:\n{card.description}";
                    }
                }
                else
                {
                    inspectorAbilitiesText.text =
                        $"💎 Cost: {card.cost} Mana   |   Role: {card.role}\n\n" +
                        $"• Effect Description:\n{card.description}";
                }
            }
        }

        private static string GetPreviewSkillCost(string act)
        {
            if (string.IsNullOrEmpty(act)) return "1";
            if (act.Contains("Cost 3") || act.Contains("3 Mana") || act.Contains("3 Power")) return "3";
            if (act.Contains("Cost 2") || act.Contains("2 Mana") || act.Contains("2 Power")) return "2";
            return "1";
        }

        private static void CreateFieldPreviewSigil(Transform parent, string name, Vector2 pos, Color accentCol, Color pipBgCol, string costStr)
        {
            var sigilObj = new GameObject(name);
            sigilObj.transform.SetParent(parent, false);
            var rt = sigilObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(38f, 38f);
            rt.anchoredPosition = pos;

            var bgImg = sigilObj.AddComponent<Image>();
            bgImg.sprite = GameBootstrapper.GetCircleSprite();
            bgImg.color = new Color(0.05f, 0.08f, 0.14f, 0.80f);
            bgImg.raycastTarget = false;

            var ringObj = new GameObject("Ring");
            ringObj.transform.SetParent(sigilObj.transform, false);
            var rRect = ringObj.AddComponent<RectTransform>();
            rRect.anchorMin = new Vector2(0.5f, 0.5f);
            rRect.anchorMax = new Vector2(0.5f, 0.5f);
            rRect.sizeDelta = new Vector2(46f, 46f);
            var rImg = ringObj.AddComponent<Image>();
            rImg.sprite = GameBootstrapper.GetSummoningCircleSprite();
            rImg.color = accentCol;
            rImg.raycastTarget = false;

            var flashObj = new GameObject("Flash");
            flashObj.transform.SetParent(sigilObj.transform, false);
            var fRect = flashObj.AddComponent<RectTransform>();
            fRect.anchorMin = new Vector2(0.5f, 0.5f);
            fRect.anchorMax = new Vector2(0.5f, 0.5f);
            fRect.sizeDelta = new Vector2(38f, 38f);
            var fImg = flashObj.AddComponent<Image>();
            fImg.sprite = GameBootstrapper.GetAbilityFlashCoreSprite();
            fImg.color = Color.Lerp(accentCol, Color.white, 0.25f);
            fImg.raycastTarget = false;

            var swObj = new GameObject("Swords");
            swObj.transform.SetParent(sigilObj.transform, false);
            var swRect = swObj.AddComponent<RectTransform>();
            swRect.anchorMin = new Vector2(0.5f, 0.5f);
            swRect.anchorMax = new Vector2(0.5f, 0.5f);
            swRect.sizeDelta = new Vector2(24f, 24f);
            var swImg = swObj.AddComponent<Image>();
            swImg.sprite = GameBootstrapper.GetCrossedSwordsSprite();
            swImg.color = Color.Lerp(accentCol, Color.white, 0.65f);
            swImg.raycastTarget = false;

            var pipObj = GameBootstrapper.CreateCirclePanel(sigilObj.transform, "CostPip",
                pipBgCol, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(13f, -12f), new Vector2(16f, 16f));
            pipObj.GetComponent<Image>().raycastTarget = false;
            var pipOutline = pipObj.AddComponent<Outline>();
            pipOutline.effectColor = Color.Lerp(accentCol, Color.white, 0.35f);
            pipOutline.effectDistance = new Vector2(1f, 1f);

            var txt = GameBootstrapper.CreateText(pipObj.transform, "CostTxt", costStr, 9,
                Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(16f, 16f));
            txt.fontStyle = FontStyle.Bold;
            txt.raycastTarget = false;
            GameBootstrapper.AddTextShadow(txt);
        }

        // =========================================================================
        // 6. FILTER BUTTON HELPERS
        // =========================================================================
        private void AddTypeFilterButton(Transform parent, string key, string label, Vector2 pos, Vector2 size, TypeFilter filter)
        {
            Color baseCol = new Color(0.12f, 0.18f, 0.28f, 0.96f);
            var btn = GameBootstrapper.CreateBeveledButton(parent, $"BtnType_{key}", label,
                baseCol, Color.white, pos, size, 11,
                () =>
                {
                    currentTypeFilter = filter;
                    ApplyFiltersAndRefresh(0);
                });
            typeButtons.Add(new ButtonHighlightPair
            {
                key = filter.ToString(),
                bg = btn.GetComponent<Image>(),
                outline = btn.GetComponent<Outline>(),
                baseColor = baseCol
            });
        }

        private void AddStatusFilterButton(Transform parent, string key, string label, Vector2 pos, Vector2 size, ArtStatusFilter filter, Color baseCol)
        {
            var btn = GameBootstrapper.CreateBeveledButton(parent, $"BtnStatus_{key}", label,
                baseCol, Color.white, pos, size, 11,
                () =>
                {
                    currentStatusFilter = filter;
                    ApplyFiltersAndRefresh(0);
                });
            statusButtons.Add(new ButtonHighlightPair
            {
                key = filter.ToString(),
                bg = btn.GetComponent<Image>(),
                outline = btn.GetComponent<Outline>(),
                baseColor = baseCol
            });
        }

        private void AddViewModeButton(Transform parent, string key, string label, Vector2 pos, Vector2 size, GalleryViewMode mode)
        {
            Color baseCol = new Color(0.14f, 0.22f, 0.34f, 0.96f);
            var btn = GameBootstrapper.CreateBeveledButton(parent, $"BtnViewMode_{key}", label,
                baseCol, Color.white, pos, size, 10,
                () =>
                {
                    currentViewMode = mode;
                    ApplyFiltersAndRefresh(0);
                });
            viewModeButtons.Add(new ButtonHighlightPair
            {
                key = mode.ToString(),
                bg = btn.GetComponent<Image>(),
                outline = btn.GetComponent<Outline>(),
                baseColor = baseCol
            });
        }

        private void AddElementFilterButton(Transform parent, string elemKey, string label, Vector2 pos, Vector2 size)
        {
            Color baseCol = elemKey == "All"
                ? new Color(0.14f, 0.18f, 0.26f, 0.96f)
                : Color.Lerp(new Color(0.08f, 0.10f, 0.16f, 0.96f), MonsterArtLoader.GetElementColor(elemKey), 0.32f);

            var btn = GameBootstrapper.CreateBeveledButton(parent, $"BtnElem_{elemKey}", label,
                baseCol, Color.white, pos, size, 10,
                () =>
                {
                    currentElementFilter = elemKey;
                    ApplyFiltersAndRefresh(0);
                });
            elementButtons.Add(new ButtonHighlightPair
            {
                key = elemKey,
                bg = btn.GetComponent<Image>(),
                outline = btn.GetComponent<Outline>(),
                baseColor = baseCol
            });
        }

        private void AddTribeFilterButton(Transform parent, string tribeKey, string label, Vector2 pos, Vector2 size)
        {
            Color baseCol = new Color(0.12f, 0.16f, 0.24f, 0.96f);
            var btn = GameBootstrapper.CreateBeveledButton(parent, $"BtnTribe_{tribeKey}", label,
                baseCol, Color.white, pos, size, 10,
                () =>
                {
                    currentTribeFilter = tribeKey;
                    ApplyFiltersAndRefresh(0);
                });
            tribeButtons.Add(new ButtonHighlightPair
            {
                key = tribeKey,
                bg = btn.GetComponent<Image>(),
                outline = btn.GetComponent<Outline>(),
                baseColor = baseCol
            });
        }

        private void AddTierFilterButton(Transform parent, int tierNum, string label, Vector2 pos, Vector2 size)
        {
            Color baseCol = new Color(0.12f, 0.16f, 0.24f, 0.96f);
            var btn = GameBootstrapper.CreateBeveledButton(parent, $"BtnTier_{tierNum}", label,
                baseCol, Color.white, pos, size, 10,
                () =>
                {
                    currentTierFilter = tierNum;
                    ApplyFiltersAndRefresh(0);
                });
            tierButtons.Add(new ButtonHighlightPair
            {
                key = tierNum.ToString(),
                bg = btn.GetComponent<Image>(),
                outline = btn.GetComponent<Outline>(),
                baseColor = baseCol
            });
        }

        private void UpdateFilterButtonHighlights()
        {
            HighlightGroup(typeButtons, currentTypeFilter.ToString());
            HighlightGroup(statusButtons, currentStatusFilter.ToString());
            HighlightGroup(viewModeButtons, currentViewMode.ToString());
            HighlightGroup(elementButtons, currentElementFilter);
            HighlightGroup(tribeButtons, currentTribeFilter);
            HighlightGroup(tierButtons, currentTierFilter.ToString());
        }

        private void HighlightGroup(List<ButtonHighlightPair> list, string activeKey)
        {
            foreach (var item in list)
            {
                bool active = string.Equals(item.key, activeKey, StringComparison.OrdinalIgnoreCase);
                if (item.bg != null)
                {
                    item.bg.color = active
                        ? Color.Lerp(item.baseColor, new Color(0.28f, 0.62f, 0.98f, 1f), 0.55f)
                        : item.baseColor;
                }
                if (item.outline != null)
                {
                    item.outline.effectColor = active
                        ? new Color(1f, 0.88f, 0.42f, 1f)
                        : new Color(0.32f, 0.40f, 0.54f, 0.75f);
                }
            }
        }
    }
}

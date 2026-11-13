using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using MonsterCardGame.Core;
using MonsterCardGame.Data;
using MonsterCardGame.Utils;

namespace MonsterCardGame.UI
{
    public class GameBootstrapper : MonoBehaviour
    {
        private static Font defaultFont;
        private static readonly Dictionary<string, Sprite> resourceSpriteCache = new Dictionary<string, Sprite>();
        private static Sprite _circleSprite;
        private static Sprite _ornateFrameSprite;
        private static Sprite _hollowOrnateBorderSprite;
        private static Sprite _beveledButtonSprite;
        private static Sprite _medallionRingSprite;
        private static Sprite _playerSlotPedestalSprite;
        private static Sprite _enemySlotPedestalSprite;
        private static Sprite _summoningCircleSprite;
        private static Sprite _handTraySprite;
        private static Sprite _elementalAuraHaloSprite;
        private static Sprite _circularBlueFlareSprite;
        private static Sprite _mirageRippleSprite;
        private static Sprite _mirageWaveSweepSprite;
        private static Sprite _abilityFlashCoreSprite;
        private static Sprite _abilityShockwaveRingSprite;
        private static Sprite _crossedSwordsSprite;

        public static Font GetFont()
        {
            if (defaultFont == null)
            {
                try
                {
                    defaultFont = Resources.Load<Font>("Fonts/ArabicFont");
                }
                catch { }

                if (defaultFont == null)
                {
                    try
                    {
                        defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    }
                    catch { }
                }

                if (defaultFont == null)
                {
                    try
                    {
                        defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    }
                    catch { }
                }
            }
            return defaultFont;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyStartupFullscreen()
        {
            EnsureFullScreenDisplay();
        }

        public static void EnsureFullScreenDisplay()
        {
            #if !UNITY_EDITOR
            int nativeW = (Display.main != null && Display.main.systemWidth > 0)
                ? Display.main.systemWidth
                : Screen.currentResolution.width;
            int nativeH = (Display.main != null && Display.main.systemHeight > 0)
                ? Display.main.systemHeight
                : Screen.currentResolution.height;

            if (nativeW <= 0 || nativeH <= 0)
            {
                nativeW = 1920;
                nativeH = 1080;
            }

            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            Screen.fullScreen = true;
            Screen.SetResolution(nativeW, nativeH, FullScreenMode.FullScreenWindow);
            #endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void AutoInitializeGame()
        {
            if (Application.isPlaying)
            {
                EnsureFullScreenDisplay();

                // Remove any pre-baked scene objects to ensure all frames, sprites, and UI elements are constructed freshly in memory
                var oldGms = FindObjectsOfType<GameManager>();
                for (int i = 0; i < oldGms.Length; i++)
                {
                    if (oldGms[i] != null) DestroyImmediate(oldGms[i].gameObject);
                }

                var oldUis = FindObjectsOfType<BattleUIManager>();
                for (int i = 0; i < oldUis.Length; i++)
                {
                    if (oldUis[i] != null) DestroyImmediate(oldUis[i].gameObject);
                }

                var oldCanvas = GameObject.Find("MainCanvas");
                if (oldCanvas != null) DestroyImmediate(oldCanvas);
            }
            else if (FindObjectOfType<GameManager>() != null)
            {
                return;
            }

            Debug.Log("🚀 Building AAA Fantasy Tactical Battlefield & Zonal UI...");

            // 1. Camera & Audio
            EnsureCamera();
            EnsureSoundManager();

            // 2. EventSystem & Input
            EnsureEventSystem();

            // 3. Main Canvas
            var canvas = CreateMainCanvas();

            // 4. Core Game Objects
            var gameManagerObj = new GameObject("GameManager");
            var gm = gameManagerObj.AddComponent<GameManager>();
            var combatSystem = gameManagerObj.AddComponent<CombatSystem>();

            var playerObj = new GameObject("HumanPlayer");
            playerObj.transform.SetParent(gameManagerObj.transform);
            var humanPlayer = playerObj.AddComponent<PlayerState>();
            humanPlayer.playerName = "Supreme Commander (You)";

            var aiObj = new GameObject("AIOpponent");
            aiObj.transform.SetParent(gameManagerObj.transform);
            var aiPlayer = aiObj.AddComponent<PlayerState>();
            aiPlayer.playerName = "Shadow Warlord (Enemy)";
            var aiBrain = aiObj.AddComponent<AIOpponent>();
            aiBrain.aiPlayer = aiPlayer;
            aiBrain.humanPlayer = humanPlayer;

            gm.humanPlayer = humanPlayer;
            gm.aiPlayer = aiPlayer;
            gm.aiOpponent = aiBrain;

            // 5. Build 6 Board Slots per Player
            for (int i = 0; i < 6; i++)
            {
                var pSlotObj = new GameObject($"Player_Slot_{i}");
                pSlotObj.transform.SetParent(playerObj.transform);
                var pSlot = pSlotObj.AddComponent<BoardSlot>();
                pSlot.Initialize(i, true);
                humanPlayer.slots[i] = pSlot;

                var eSlotObj = new GameObject($"Enemy_Slot_{i}");
                eSlotObj.transform.SetParent(aiObj.transform);
                var eSlot = eSlotObj.AddComponent<BoardSlot>();
                eSlot.Initialize(i, false);
                aiPlayer.slots[i] = eSlot;
            }

            // 6. Build Interactive Visual Arena on Canvas
            var uiManagerObj = new GameObject("BattleUIManager");
            uiManagerObj.transform.SetParent(canvas.transform);
            var ui = uiManagerObj.AddComponent<BattleUIManager>();

            BuildVisualArena(canvas.transform, ui, humanPlayer, aiPlayer);

            // 7. Link Database Asset & Start Match
            var jsonAsset = Resources.Load<TextAsset>("monsters_150");
            gm.monstersJsonFile = jsonAsset;

            Debug.Log("🎉 AAA Fantasy Tactical Battlefield built! Ready for battle.");
        }

        private static void EnsureCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camObj = new GameObject("Main Camera");
                cam = camObj.AddComponent<Camera>();
                camObj.tag = "MainCamera";
                camObj.transform.position = new Vector3(0, 0, -10);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.04f, 0.05f, 0.08f);
            }

            if (cam.GetComponent<AudioListener>() == null)
            {
                cam.gameObject.AddComponent<AudioListener>();
            }
        }

        private static void EnsureSoundManager()
        {
            if (UnityEngine.Object.FindAnyObjectByType<MonsterCardGame.Audio.SoundManager>() == null)
            {
                var smObj = new GameObject("SoundManager");
                smObj.AddComponent<MonsterCardGame.Audio.SoundManager>();
            }
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() == null)
            {
                var esObj = new GameObject("EventSystem");
                esObj.AddComponent<EventSystem>();
                esObj.AddComponent<StandaloneInputModule>();
            }
        }

        private static Canvas CreateMainCanvas()
        {
            var canvasObj = new GameObject("MainCanvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.dynamicPixelsPerUnit = 2.5f;

            canvasObj.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static Sprite LoadResourceSprite(string resPath)
        {
            if (resourceSpriteCache.TryGetValue(resPath, out Sprite cached) && cached != null)
            {
                return cached;
            }
            var tex = Resources.Load<Texture2D>(resPath);
            if (tex != null)
            {
                var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                resourceSpriteCache[resPath] = sp;
                return sp;
            }
            return null;
        }

        private static void BuildVisualArena(Transform canvas, BattleUIManager ui, PlayerState human, PlayerState ai)
        {
            // 0.0 Master Screen Container for whole-screen tremor & unified visuals
            var screenRoot = CreatePanel(canvas, "ScreenRootContainer", Color.clear,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var screenRootImg = screenRoot.GetComponent<Image>();
            if (screenRootImg != null) screenRootImg.raycastTarget = false;

            // Unified Dark Obsidian War Room Background
            var mainBg = CreatePanel(screenRoot.transform, "MainCanvasBackground", new Color(0.03f, 0.04f, 0.07f, 1f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            mainBg.transform.SetAsFirstSibling();

            Sprite sidebarArtSprite = LoadResourceSprite("ui_sidebar_frame");
            Sprite handTrayArtSprite = GetHandTraySprite();
            Sprite arenaGroundSprite = LoadResourceSprite("arena_ground");

            // =========================================================================
            // ZONE 1: Center Battlefield Board (1200 x 688)
            // =========================================================================
            var centerBoard = CreatePanel(screenRoot.transform, "CenterBattleBoard", new Color(0.04f, 0.05f, 0.08f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200, 688));
            ui.centerBoardRect = centerBoard.GetComponent<RectTransform>();

            // 1.1 Open Battle Plain Ground (arena_ground)
            var bgObj = new GameObject("ArenaBackground");
            bgObj.transform.SetParent(centerBoard.transform, false);
            bgObj.transform.SetAsFirstSibling();
            var bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImg = bgObj.AddComponent<Image>();
            if (arenaGroundSprite != null)
            {
                bgImg.sprite = arenaGroundSprite;
                bgImg.color = Color.white;
            }
            else
            {
                bgImg.color = new Color(0.12f, 0.16f, 0.14f, 1f);
            }

            // 1.2 Dynamic Weather & Atmosphere Overlay inside CenterBattleBoard
            var atmosObj = new GameObject("EnvironmentAtmosphereOverlay");
            atmosObj.transform.SetParent(centerBoard.transform, false);
            atmosObj.transform.SetSiblingIndex(1);
            var atmosRect = atmosObj.AddComponent<RectTransform>();
            atmosRect.anchorMin = Vector2.zero;
            atmosRect.anchorMax = Vector2.one;
            atmosRect.sizeDelta = Vector2.zero;
            var atmosImg = atmosObj.AddComponent<Image>();
            atmosImg.color = new Color(0.08f, 0.09f, 0.13f, 0.18f);
            atmosImg.raycastTarget = false;
            ui.envAtmosphereOverlay = atmosImg;

            BattlefieldAtmosphereEngine.EnsureInstance(centerBoard.transform, bgImg, atmosImg);

            // Turn info text disabled to keep center battlefield clean
            ui.turnInfoText = null;

            // 1.4 12 Tactical Battlefield Slots (Clean - No outer territory boxes)
            BuildSlotsRow(centerBoard.transform, ui, ai, false);
            BuildSlotsRow(centerBoard.transform, ui, human, true);

            // Subtle matte gunmetal hairline dividers framing the untouched center battlefield
            var topSeam = CreatePanel(screenRoot.transform, "TopArenaSeamDivider", new Color(0.20f, 0.23f, 0.29f, 0.92f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 344), new Vector2(1200, 3));
            topSeam.GetComponent<Image>().raycastTarget = false;
            var bottomSeam = CreatePanel(screenRoot.transform, "BottomArenaSeamDivider", new Color(0.20f, 0.23f, 0.29f, 0.92f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -344), new Vector2(1200, 3));
            bottomSeam.GetComponent<Image>().raycastTarget = false;

            // =========================================================================
            // ZONE 2: Top Dock — Pure Opponent Hidden Card Hand Tray (1200 x 196)
            // =========================================================================
            BuildHandDock(screenRoot.transform, ui, false, handTrayArtSprite);

            // =========================================================================
            // ZONE 3: Bottom Dock — Pure Player Card Hand Tray (1200 x 196)
            // =========================================================================
            BuildHandDock(screenRoot.transform, ui, true, handTrayArtSprite);

            // =========================================================================
            // ZONE 4: Left Wing — Compact Citadels & Card Inspector (360 x 1080)
            // =========================================================================
            var leftWing = CreatePanel(screenRoot.transform, "LeftWingPanel", Color.white,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(180, 0), new Vector2(360, 1080));
            if (sidebarArtSprite != null)
            {
                var lwImg = leftWing.GetComponent<Image>();
                lwImg.sprite = sidebarArtSprite;
                lwImg.color = new Color(0.46f, 0.49f, 0.56f, 1f);
            }
            else
            {
                leftWing.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.98f);
            }
            var leftEdgeDiv = CreatePanel(leftWing.transform, "LeftWingEdgeDivider", new Color(0.18f, 0.21f, 0.27f, 0.92f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-1, 0), new Vector2(2, 1080));
            leftEdgeDiv.GetComponent<Image>().raycastTarget = false;

            // Top-Left: Compact Enemy Commander HUD (320 x 126, y = -18 from top)
            BuildCommanderCitadel(leftWing.transform, ui, false, null);

            // Middle-Left: Matte Dark Slate Card Inspector (Header + 724px Inspector)
            var reconHeader = CreateOrnatePanel(leftWing.transform, "ReconHeaderBanner",
                new Color(0.07f, 0.09f, 0.14f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 369), new Vector2(320, 28));
            var rhText = CreateText(reconHeader.transform, "ReconHeaderText", "CARD INSPECTOR", 11,
                new Color(0.76f, 0.82f, 0.90f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(304, 24));
            AddTextShadow(rhText);

            var detailsPanel = CreateOrnatePanel(leftWing.transform, "CardDetailsInspectorPanel",
                new Color(0.05f, 0.06f, 0.09f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -16), new Vector2(320, 724));
            ui.cardDetailsPanel = detailsPanel;

            // 1. Card Name Bar & Sapphire Mana Cost Medallion
            var detailHeaderBar = CreateOrnatePanel(detailsPanel.transform, "DetailHeaderBar",
                new Color(0.08f, 0.10f, 0.15f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 334), new Vector2(300, 40));

            ui.detailNameText = CreateText(detailHeaderBar.transform, "DetailName", "Select a Monster or Card", 14,
                new Color(0.90f, 0.94f, 0.98f), TextAnchor.MiddleLeft, new Vector2(-18, 0), new Vector2(224, 32));
            AddTextShadow(ui.detailNameText);

            var fullCostCircle = CreateMedallion(detailHeaderBar.transform, "FullCostCircle",
                new Color(0.14f, 0.34f, 0.64f, 1f), new Vector2(124, 0), new Vector2(34, 34), "-", 15);
            ui.detailCostText = fullCostCircle.transform.Find("MedallionText").GetComponent<Text>();

            // 2. Full-Width Enlarged Artwork Window (272 x 310, clean dark slate bevel — no gold frame)
            var artWindow = CreatePanel(detailsPanel.transform, "ArtWindow", new Color(0.03f, 0.04f, 0.06f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(272, 310));
            var artWinOutline = artWindow.AddComponent<Outline>();
            artWinOutline.effectColor = new Color(0.28f, 0.32f, 0.40f, 0.95f);
            artWinOutline.effectDistance = new Vector2(1.5f, 1.5f);

            var artObj = new GameObject("FullCardArtImage");
            artObj.transform.SetParent(artWindow.transform, false);
            var rectArt = artObj.AddComponent<RectTransform>();
            rectArt.anchorMin = Vector2.zero;
            rectArt.anchorMax = Vector2.one;
            rectArt.sizeDelta = new Vector2(-6, -6);
            ui.detailCardArt = artObj.AddComponent<Image>();
            ui.detailCardArt.preserveAspect = false;

            // 3. Element, Tribe, Role & Cost Meta Bar
            var metaBar = CreateOrnatePanel(detailsPanel.transform, "MetaBar",
                new Color(0.08f, 0.11f, 0.16f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -28), new Vector2(300, 30));
            ui.detailMetaText = CreateText(metaBar.transform, "DetailMeta", "Click any card on board, hand, or side slots", 11,
                new Color(0.70f, 0.84f, 0.96f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(288, 24));
            AddTextShadow(ui.detailMetaText);

            // 4. Ability & Effect Scroll Box (216 height)
            var descBox = CreateOrnatePanel(detailsPanel.transform, "DescBox",
                new Color(0.04f, 0.05f, 0.08f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -160), new Vector2(300, 216));

            var descHeader = CreateText(descBox.transform, "DescBoxHeader", "SIDE ABILITY & EFFECT", 10,
                new Color(0.68f, 0.74f, 0.82f, 0.92f), TextAnchor.MiddleCenter, new Vector2(0, -14), new Vector2(280, 18));
            descHeader.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            descHeader.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            descHeader.rectTransform.anchoredPosition = new Vector2(0, -14);

            ui.detailDescText = CreateText(descBox.transform, "DetailDesc", "Full monster abilities, leader passive auras, and spell/environment effects will appear here.", 12,
                new Color(0.90f, 0.93f, 0.96f), TextAnchor.UpperLeft, Vector2.zero, Vector2.zero);
            ui.detailDescText.rectTransform.anchorMin = Vector2.zero;
            ui.detailDescText.rectTransform.anchorMax = Vector2.one;
            ui.detailDescText.rectTransform.offsetMin = new Vector2(12, 10);
            ui.detailDescText.rectTransform.offsetMax = new Vector2(-12, -26);
            ui.detailDescText.horizontalOverflow = HorizontalWrapMode.Wrap;
            ui.detailDescText.verticalOverflow = VerticalWrapMode.Truncate;

            // 5. Bottom ATK / Status / DEF Plaques (matte dark slate)
            var atkBadge = CreateOrnatePanel(detailsPanel.transform, "FullAtkBadge",
                new Color(0.28f, 0.11f, 0.13f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-104, -318), new Vector2(88, 44));
            ui.detailAtkText = CreateText(atkBadge.transform, "AtkText", "⚔️ -", 15,
                new Color(0.96f, 0.88f, 0.88f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(82, 38));
            AddTextShadow(ui.detailAtkText);

            var statusBadge = CreateOrnatePanel(detailsPanel.transform, "DetailStatusBadge",
                new Color(0.08f, 0.11f, 0.16f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -318), new Vector2(114, 44));
            ui.detailStatusText = CreateText(statusBadge.transform, "DetailStatus", "Ready", 11,
                new Color(0.52f, 0.88f, 0.68f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(106, 38));
            ui.detailStatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            ui.detailStatusText.verticalOverflow = VerticalWrapMode.Truncate;
            ui.detailStatusText.resizeTextForBestFit = true;
            ui.detailStatusText.resizeTextMinSize = 8;
            ui.detailStatusText.resizeTextMaxSize = 11;
            ui.detailStatsText = ui.detailStatusText;

            var defBadge = CreateOrnatePanel(detailsPanel.transform, "FullDefBadge",
                new Color(0.11f, 0.25f, 0.16f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(104, -318), new Vector2(88, 44));
            ui.detailDefText = CreateText(defBadge.transform, "DefText", "🛡️ -", 15,
                new Color(0.88f, 0.96f, 0.90f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(82, 38));
            AddTextShadow(ui.detailDefText);

            // Bottom-Left: Compact Player Commander HUD (320 x 126, y = +18 from bottom)
            BuildCommanderCitadel(leftWing.transform, ui, true, null);

            // =========================================================================
            // ZONE 5: Right Wing — Leaders Side Slots, Chronicle, Shared Environment & Orders
            // =========================================================================
            var rightWing = CreatePanel(screenRoot.transform, "RightWingPanel", Color.white,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-180, 0), new Vector2(360, 1080));
            if (sidebarArtSprite != null)
            {
                var rwImg = rightWing.GetComponent<Image>();
                rwImg.sprite = sidebarArtSprite;
                rwImg.color = new Color(0.46f, 0.49f, 0.56f, 1f);
            }
            else
            {
                rightWing.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.98f);
            }
            var rightEdgeDiv = CreatePanel(rightWing.transform, "RightWingEdgeDivider", new Color(0.18f, 0.21f, 0.27f, 0.92f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(1, 0), new Vector2(2, 1080));
            rightEdgeDiv.GetComponent<Image>().raycastTarget = false;

            // Top Utility Row (Full-Width Centered Turn Badge at y = +506 — clean symmetrical top-right header)
            var turnBadgePanel = CreateOrnatePanel(rightWing.transform, "TopTurnBar",
                new Color(0.07f, 0.09f, 0.14f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 506), new Vector2(320, 32));
            ui.turnInfoText = CreateText(turnBadgePanel.transform, "TurnInfoText", "TURN 1 • YOUR MOVE", 11,
                new Color(0.84f, 0.90f, 0.98f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(300, 28));
            AddTextShadow(ui.turnInfoText);

            ui.endTurnButton = null;
            ui.quitButton = null;

            // Zone A: Enemy Leader Side Slot (y = +382, 320 x 196)
            BuildSideLeaderSlot(rightWing.transform, ui, false, 382f, 196f);

            // Zone B: Expanded Battle Chronicle Log (y = +148, 320 x 228)
            var logPanel = CreateOrnatePanel(rightWing.transform, "CombatLogPanel",
                new Color(0.05f, 0.06f, 0.09f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 148), new Vector2(320, 228));

            var logHeaderBar = CreatePanel(logPanel.transform, "LogHeaderBar",
                new Color(0.08f, 0.10f, 0.15f, 0.96f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -14), new Vector2(304, 22));
            var lhTxt = CreateText(logHeaderBar.transform, "LogHeader", "BATTLE CHRONICLE", 10,
                new Color(0.72f, 0.78f, 0.86f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(290, 20));
            AddTextShadow(lhTxt);

            ui.combatLogText = CreateText(logPanel.transform, "CombatLogText", "Match started — drag cards or right-click to sacrifice. Turns auto-complete when no actions remain.", 10,
                new Color(0.86f, 0.90f, 0.95f), TextAnchor.UpperLeft, new Vector2(0, -12), new Vector2(296, 188));
            ui.combatLogText.horizontalOverflow = HorizontalWrapMode.Wrap;
            ui.combatLogText.verticalOverflow = VerticalWrapMode.Truncate;

            // Zone C: Shared Environment Card Slot centered cleanly on Right Wing (y = -114, 320 x 256)
            BuildSharedEnvironmentSlot(rightWing.transform, ui, -114f, 256f);

            // Zone D: Player Leader Side Slot (y = -384, 320 x 236)
            BuildSideLeaderSlot(rightWing.transform, ui, true, -384f, 236f);

            // 10.2 Center Seam Astral Hourglass Medallion (integrated on BottomArenaSeamDivider at x = 0, y = -310)
            BuildAstralHourglassMedallion(screenRoot.transform, ui);

            // 10.5 Top-Level Overlay for Card Flight Animations (Deck -> Hand, Hand -> Board/Env/Leader)
            var flightLayerObj = new GameObject("CardFlightLayer");
            flightLayerObj.transform.SetParent(screenRoot.transform, false);
            var flightLayerRect = flightLayerObj.AddComponent<RectTransform>();
            flightLayerRect.anchorMin = Vector2.zero;
            flightLayerRect.anchorMax = Vector2.one;
            flightLayerRect.sizeDelta = Vector2.zero;
            flightLayerRect.anchoredPosition = Vector2.zero;
            ui.cardFlightLayer = flightLayerRect;

            // 11. Epic Game Over Modal
            BuildGameOverModal(canvas, ui);
        }

        private static void BuildAstralHourglassMedallion(Transform screenRoot, BattleUIManager ui)
        {
            var anchor = new GameObject("AstralHourglassMedallion");
            anchor.transform.SetParent(screenRoot, false);
            var anchorRect = anchor.AddComponent<RectTransform>();
            anchorRect.anchorMin = new Vector2(0.5f, 0.5f);
            anchorRect.anchorMax = new Vector2(0.5f, 0.5f);
            anchorRect.sizeDelta = new Vector2(124f, 96f);
            anchorRect.anchoredPosition = new Vector2(0f, -310f);

            // 1. Scale pivot centered at the circular medallion (x=0, y=-310 -> base touches seam line y=-344)
            var scalePivotObj = new GameObject("MedallionScalePivot");
            scalePivotObj.transform.SetParent(anchor.transform, false);
            var scaleRect = scalePivotObj.AddComponent<RectTransform>();
            scaleRect.anchorMin = new Vector2(0.5f, 0.5f);
            scaleRect.anchorMax = new Vector2(0.5f, 0.5f);
            scaleRect.sizeDelta = new Vector2(86f, 86f);
            scaleRect.anchoredPosition = Vector2.zero;

            // Invisible circular hit catcher across the full medallion & ring perimeter
            var hitCatcher = scalePivotObj.AddComponent<Image>();
            hitCatcher.sprite = GetCircleSprite();
            hitCatcher.color = new Color(0f, 0f, 0f, 0f);
            hitCatcher.raycastTarget = true;

            // Circular Blue Flare Halo behind medallion (pure radial glow + soft corona, zero square corners)
            var haloObj = new GameObject("HourglassAuraHalo");
            haloObj.transform.SetParent(scalePivotObj.transform, false);
            var haloRect = haloObj.AddComponent<RectTransform>();
            haloRect.anchorMin = new Vector2(0.5f, 0.5f);
            haloRect.anchorMax = new Vector2(0.5f, 0.5f);
            haloRect.sizeDelta = new Vector2(152f, 152f);
            var haloImg = haloObj.AddComponent<Image>();
            haloImg.sprite = GetCircularBlueFlareSprite();
            haloImg.color = new Color(0.24f, 0.72f, 1.0f, 0.56f);
            haloImg.raycastTarget = false;

            // Shimmering astral cyan-blue star lens flare core
            var flareObj = new GameObject("HourglassFlareCore");
            flareObj.transform.SetParent(scalePivotObj.transform, false);
            var flareRect = flareObj.AddComponent<RectTransform>();
            flareRect.anchorMin = new Vector2(0.5f, 0.5f);
            flareRect.anchorMax = new Vector2(0.5f, 0.5f);
            flareRect.sizeDelta = new Vector2(118f, 118f);
            var flareImg = flareObj.AddComponent<Image>();
            flareImg.sprite = GetAbilityFlashCoreSprite();
            flareImg.color = new Color(0.52f, 0.90f, 1.0f, 0.45f);
            flareImg.raycastTarget = false;

            // Spinning subtle astrolabe rune ring behind medallion
            var ringObj = new GameObject("HourglassRuneRing");
            ringObj.transform.SetParent(scalePivotObj.transform, false);
            var ringRect = ringObj.AddComponent<RectTransform>();
            ringRect.anchorMin = new Vector2(0.5f, 0.5f);
            ringRect.anchorMax = new Vector2(0.5f, 0.5f);
            ringRect.sizeDelta = new Vector2(88f, 88f);
            var ringImg = ringObj.AddComponent<Image>();
            ringImg.sprite = GetSummoningCircleSprite();
            ringImg.color = new Color(0.32f, 0.80f, 1.0f, 0.62f);
            ringImg.raycastTarget = false;

            // Flip pivot (rotates 0 <-> 180 deg cleanly on turn transition)
            var flipPivotObj = new GameObject("HourglassFlipPivot");
            flipPivotObj.transform.SetParent(scalePivotObj.transform, false);
            var flipRect = flipPivotObj.AddComponent<RectTransform>();
            flipRect.anchorMin = new Vector2(0.5f, 0.5f);
            flipRect.anchorMax = new Vector2(0.5f, 0.5f);
            flipRect.sizeDelta = new Vector2(72f, 72f);

            var medallionImg = flipPivotObj.AddComponent<Image>();
            Sprite hgSprite = LoadResourceSprite("astral_hourglass");
            if (hgSprite != null)
            {
                medallionImg.sprite = hgSprite;
                medallionImg.color = Color.white;
                medallionImg.preserveAspect = true;
            }
            else
            {
                medallionImg.sprite = GetCircleSprite();
                medallionImg.color = new Color(0.28f, 0.42f, 0.58f, 0.96f);
            }
            medallionImg.raycastTarget = true;

            // 2. Compact sapphire-bronze nameplate integrated cleanly on the seam divider beneath the medallion
            var plaque = CreatePanel(anchor.transform, "HourglassNameplate",
                new Color(0.05f, 0.08f, 0.14f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -38f), new Vector2(114f, 18f));
            var plaqueImg = plaque.GetComponent<Image>();
            plaqueImg.raycastTarget = true;
            var plaqueOut = plaque.AddComponent<Outline>();
            plaqueOut.effectColor = new Color(0.26f, 0.52f, 0.84f, 0.90f);
            plaqueOut.effectDistance = new Vector2(1.0f, 1.0f);

            var captionTxt = CreateText(plaque.transform, "HourglassCaption", "⏳ PASS TURN", 9,
                new Color(0.76f, 0.90f, 1.0f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(108f, 16f));
            captionTxt.fontStyle = FontStyle.Bold;
            captionTxt.raycastTarget = false;
            AddTextShadow(captionTxt);

            // Bind Button to root anchor so clicking either the circular medallion OR its nameplate triggers Pass/Attack
            var btn = anchor.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.targetGraphic = medallionImg;

            var ctrl = anchor.AddComponent<AstralHourglassControl>();
            ctrl.hourglassButton = btn;
            ctrl.scalePivot = scaleRect;
            ctrl.flipPivot = flipRect;
            ctrl.medallionImage = medallionImg;
            ctrl.outerRuneRing = ringImg;
            ctrl.glowHalo = haloImg;
            ctrl.flareCore = flareImg;
            ctrl.captionText = captionTxt;
            ctrl.captionBgImage = plaqueImg;
            ctrl.BindButton();

            ui.hourglassControl = ctrl;
        }

        private static void BuildSideLeaderSlot(Transform parent, BattleUIManager ui, bool isPlayerSide, float centerY, float height)
        {
            string panelName = isPlayerSide ? "PlayerLeaderSlotPanel" : "EnemyLeaderSlotPanel";
            string cardBtnName = isPlayerSide ? "PlayerLeaderCardSlot" : "EnemyLeaderCardSlot";
            Color bgCol = isPlayerSide ? new Color(0.07f, 0.10f, 0.14f, 0.96f) : new Color(0.11f, 0.06f, 0.08f, 0.96f);
            Color outlineCol = isPlayerSide ? new Color(0.28f, 0.38f, 0.48f, 0.92f) : new Color(0.38f, 0.20f, 0.23f, 0.92f);

            var panel = CreateOrnatePanel(parent, panelName, bgCol,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, centerY), new Vector2(320, height));
            var panelOut = panel.AddComponent<Outline>();
            panelOut.effectColor = outlineCol;
            panelOut.effectDistance = new Vector2(1.2f, 1.2f);

            string hdrStr = isPlayerSide ? "YOUR LEADER SLOT (SIDE)" : "ENEMY LEADER SLOT (SIDE)";
            Color hdrCol = isPlayerSide ? new Color(0.78f, 0.88f, 0.98f) : new Color(0.88f, 0.68f, 0.70f);
            float hdrY = (height * 0.5f) - 16f;
            var hdr = CreateText(panel.transform, "LeaderSlotHeader", hdrStr, 10,
                hdrCol, TextAnchor.MiddleCenter, new Vector2(0, hdrY), new Vector2(300, 20));
            AddTextShadow(hdr);

            float cardH = isPlayerSide ? 154f : 140f;
            var cardObj = CreatePanel(panel.transform, cardBtnName, new Color(0.04f, 0.05f, 0.08f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-88, -11), new Vector2(114, cardH));
            var cardOut = cardObj.AddComponent<Outline>();
            cardOut.effectColor = outlineCol;
            cardOut.effectDistance = new Vector2(1.5f, 1.5f);
            var cardBtn = cardObj.AddComponent<Button>();
            cardBtn.targetGraphic = cardObj.GetComponent<Image>();

            // Empty placeholder inset
            var emptyGrp = CreatePanel(cardObj.transform, "EmptyGroup", new Color(0.06f, 0.07f, 0.10f, 0.95f),
                Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-8, -8));
            emptyGrp.GetComponent<Image>().raycastTarget = false;
            var emptyTxt = CreateText(emptyGrp.transform, "EmptyLabel",
                isPlayerSide ? "EMPTY\nLEADER\n(Drag / Play)" : "EMPTY\nLEADER", 10,
                isPlayerSide ? new Color(0.56f, 0.64f, 0.74f) : new Color(0.58f, 0.44f, 0.48f),
                TextAnchor.MiddleCenter, Vector2.zero, new Vector2(100, 80));
            emptyTxt.raycastTarget = false;

            // Summoned Leader Art Layer
            var artObj = new GameObject("LeaderCardArt");
            artObj.transform.SetParent(cardObj.transform, false);
            var artRt = artObj.AddComponent<RectTransform>();
            artRt.anchorMin = Vector2.zero;
            artRt.anchorMax = Vector2.one;
            artRt.sizeDelta = new Vector2(-6, -6);
            var artImg = artObj.AddComponent<Image>();
            artImg.preserveAspect = false;
            artImg.raycastTarget = false;
            artObj.SetActive(false);

            // Top-right Cost Badge
            var costOrb = CreateCirclePanel(cardObj.transform, "LeaderCostBadge", new Color(0.12f, 0.30f, 0.58f, 0.96f),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16, -16), new Vector2(24, 24));
            var costTxt = CreateText(costOrb.transform, "CostText", "0", 12,
                new Color(0.92f, 0.96f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(24, 24));
            costTxt.raycastTarget = false;
            costTxt.gameObject.SetActive(false);
            costOrb.SetActive(false);

            // Right Info Column
            var nameTxt = CreateText(panel.transform, "LeaderNameText", isPlayerSide ? "No Leader Summoned" : "No Enemy Leader", 12,
                isPlayerSide ? new Color(0.84f, 0.92f, 1f) : new Color(0.88f, 0.68f, 0.70f),
                TextAnchor.MiddleLeft, new Vector2(64, 42), new Vector2(170, 24));
            nameTxt.fontStyle = FontStyle.Bold;
            AddTextShadow(nameTxt);

            Text tribeTxt = null;
            if (isPlayerSide)
            {
                tribeTxt = CreateText(panel.transform, "LeaderTribeText", "Passive Side Aura Slot", 10,
                    new Color(0.64f, 0.76f, 0.88f), TextAnchor.MiddleLeft, new Vector2(64, 20), new Vector2(170, 18));
            }

            var descTxt = CreateText(panel.transform, "LeaderDescText",
                isPlayerSide ? "Drag or play a Leader card into this side slot." : "Side passive zone (Not targetable by lane attacks).", 10,
                new Color(0.80f, 0.84f, 0.90f), TextAnchor.UpperLeft, new Vector2(64, isPlayerSide ? -32f : -16f), new Vector2(170, isPlayerSide ? 78f : 72f));
            descTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
            descTxt.verticalOverflow = VerticalWrapMode.Truncate;

            if (!isPlayerSide)
            {
                var passiveBadge = CreatePanel(panel.transform, "PassiveZoneBadge", new Color(0.16f, 0.08f, 0.10f, 0.95f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(64, -66), new Vector2(170, 22));
                var pTxt = CreateText(passiveBadge.transform, "PassiveText", "PASSIVE • SIDE ZONE", 9,
                    new Color(0.82f, 0.64f, 0.66f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(162, 18));
                pTxt.raycastTarget = false;
            }

            if (isPlayerSide)
            {
                ui.playerLeaderSlotButton = cardBtn;
                ui.playerLeaderCardArt = artImg;
                ui.playerLeaderEmptyGroup = emptyGrp;
                ui.playerLeaderCostText = costTxt;
                ui.playerLeaderNameText = nameTxt;
                ui.playerLeaderTribeText = tribeTxt;
                ui.playerLeaderDescText = descTxt;
            }
            else
            {
                ui.enemyLeaderSlotButton = cardBtn;
                ui.enemyLeaderCardArt = artImg;
                ui.enemyLeaderEmptyGroup = emptyGrp;
                ui.enemyLeaderCostText = costTxt;
                ui.enemyLeaderNameText = nameTxt;
                ui.enemyLeaderDescText = descTxt;
            }
        }

        private static void BuildSharedEnvironmentSlot(Transform parent, BattleUIManager ui, float centerY, float height)
        {
            var envAltar = CreateOrnatePanel(parent, "SharedEnvSlotPanel",
                new Color(0.06f, 0.08f, 0.12f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, centerY), new Vector2(320, height));
            var envOut = envAltar.AddComponent<Outline>();
            envOut.effectColor = new Color(0.28f, 0.36f, 0.46f, 0.95f);
            envOut.effectDistance = new Vector2(1.2f, 1.2f);
            ui.envAltarPanel = envAltar;

            var altarHeader = CreatePanel(envAltar.transform, "AltarHeaderBar",
                new Color(0.08f, 0.12f, 0.18f, 0.96f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -15), new Vector2(304, 24));
            var altTitle = CreateText(altarHeader.transform, "AltarTitle", "SHARED ENVIRONMENT (BOTH)", 10,
                new Color(0.80f, 0.88f, 0.96f), TextAnchor.MiddleLeft, new Vector2(-24, 0), new Vector2(236, 20));
            AddTextShadow(altTitle);

            ui.testCycleEnvButton = CreateBeveledButton(altarHeader.transform, "TestCycleEnvBtn", "⟳",
                new Color(0.14f, 0.24f, 0.36f, 0.95f), new Color(0.88f, 0.94f, 1f),
                new Vector2(114, 0), new Vector2(24, 20), 11,
                () => { if (BattleUIManager.Instance != null) BattleUIManager.Instance.OnTestCycleEnvironmentClicked(); });

            ui.testCycleLevelButton = CreateBeveledButton(altarHeader.transform, "TestCycleLevelBtn", "★",
                new Color(0.18f, 0.22f, 0.30f, 0.95f), new Color(0.88f, 0.94f, 1f),
                new Vector2(140, 0), new Vector2(24, 20), 11,
                () => { if (BattleUIManager.Instance != null) BattleUIManager.Instance.OnTestCycleEnvLevelClicked(); });

            // Left 3:4 Active Environment Card Frame (114 x 168)
            var envCardObj = CreatePanel(envAltar.transform, "SharedEnvCardSlot",
                new Color(0.05f, 0.07f, 0.10f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-88, -12), new Vector2(114, 168));
            var envCardOut = envCardObj.AddComponent<Outline>();
            envCardOut.effectColor = new Color(0.32f, 0.42f, 0.54f, 0.95f);
            envCardOut.effectDistance = new Vector2(1.5f, 1.5f);
            var envSlotBtn = envCardObj.AddComponent<Button>();
            envSlotBtn.targetGraphic = envCardObj.GetComponent<Image>();
            ui.envSlotButton = envSlotBtn;

            var artObj = new GameObject("SharedEnvCardArt");
            artObj.transform.SetParent(envCardObj.transform, false);
            var artRt = artObj.AddComponent<RectTransform>();
            artRt.anchorMin = Vector2.zero;
            artRt.anchorMax = Vector2.one;
            artRt.sizeDelta = new Vector2(-6, -6);
            var artImg = artObj.AddComponent<Image>();
            artImg.preserveAspect = false;
            artImg.raycastTarget = false;
            ui.envCardArtImage = artImg;

            var tagPill = CreatePanel(envCardObj.transform, "EnvTagPill", new Color(0.09f, 0.18f, 0.28f, 0.92f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24, -13), new Vector2(36, 16));
            tagPill.GetComponent<Image>().raycastTarget = false;
            var tagTxt = CreateText(tagPill.transform, "EnvTagTxt", "ENV", 9,
                new Color(0.82f, 0.92f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(34, 14));
            tagTxt.raycastTarget = false;

            var costOrb = CreateCirclePanel(envCardObj.transform, "EnvCostBadge", new Color(0.12f, 0.28f, 0.54f, 0.95f),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-15, -15), new Vector2(22, 22));
            costOrb.GetComponent<Image>().raycastTarget = false;
            ui.envCardCostText = CreateText(costOrb.transform, "EnvCostTxt", "-", 11,
                new Color(0.92f, 0.96f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(22, 22));
            ui.envCardCostText.raycastTarget = false;

            var titleBar = CreatePanel(envCardObj.transform, "EnvCardTitleBar", new Color(0.03f, 0.05f, 0.08f, 0.86f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 14), new Vector2(108, 22));
            titleBar.GetComponent<Image>().raycastTarget = false;
            ui.envCardTitleText = CreateText(titleBar.transform, "EnvCardTitleTxt", "NEUTRAL ARENA", 9,
                new Color(0.90f, 0.95f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(102, 18));
            ui.envCardTitleText.raycastTarget = false;

            // Right Info Stack (x = +64, width 170)
            ui.envIconText = CreateText(envAltar.transform, "EnvIcon", "☀️", 16,
                new Color(0.84f, 0.90f, 0.98f), TextAnchor.MiddleLeft, new Vector2(-12, 68), new Vector2(24, 24));
            ui.envNameText = CreateText(envAltar.transform, "EnvName", "Stable Arena", 12,
                new Color(0.86f, 0.92f, 1f), TextAnchor.MiddleLeft, new Vector2(74, 68), new Vector2(148, 22));
            AddTextShadow(ui.envNameText);

            ui.envOwnerText = CreateText(envAltar.transform, "EnvOwner", "Controlled by: Arena", 10,
                new Color(0.68f, 0.76f, 0.86f), TextAnchor.MiddleLeft, new Vector2(64, 46), new Vector2(170, 18));

            ui.envLevelText = CreateText(envAltar.transform, "EnvLevel", "No Active Weather", 10,
                new Color(0.76f, 0.84f, 0.94f), TextAnchor.MiddleLeft, new Vector2(64, 26), new Vector2(170, 18));

            var buffPlaque = CreateOrnatePanel(envAltar.transform, "EnvBuffPlaque",
                new Color(0.07f, 0.17f, 0.12f, 0.94f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(64, -6), new Vector2(170, 32));
            ui.envBuffText = CreateText(buffPlaque.transform, "EnvBuff", "No Active Buffs", 10,
                new Color(0.45f, 0.94f, 0.62f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(160, 28));
            ui.envBuffText.horizontalOverflow = HorizontalWrapMode.Wrap;

            var debuffPlaque = CreateOrnatePanel(envAltar.transform, "EnvDebuffPlaque",
                new Color(0.18f, 0.08f, 0.10f, 0.94f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(64, -46), new Vector2(170, 36));
            ui.envDebuffText = CreateText(debuffPlaque.transform, "EnvDebuff", "All Elements Balanced", 10,
                new Color(0.96f, 0.56f, 0.56f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(160, 32));
            ui.envDebuffText.horizontalOverflow = HorizontalWrapMode.Wrap;

            ui.envDurationText = CreateText(envAltar.transform, "EnvDuration", "Either player can override", 10,
                new Color(0.76f, 0.82f, 0.90f), TextAnchor.MiddleLeft, new Vector2(64, -84), new Vector2(170, 22));
        }

        private static void BuildGameOverModal(Transform canvas, BattleUIManager ui)
        {
            var overlay = CreatePanel(canvas, "GameOverModalOverlay", new Color(0.02f, 0.03f, 0.06f, 0.92f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var modalBox = CreateOrnatePanel(overlay.transform, "ModalBox", new Color(0.08f, 0.10f, 0.16f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680, 560));

            var modalOutline = modalBox.GetComponent<Outline>();
            if (modalOutline == null) modalOutline = modalBox.AddComponent<Outline>();
            modalOutline.effectColor = new Color(0.42f, 0.56f, 0.72f, 0.95f);
            modalOutline.effectDistance = new Vector2(2.5f, 2.5f);
            ui.gameOverModalOutline = modalOutline;

            ui.gameOverTitleText = CreateText(modalBox.transform, "TitleText", "🏆 LEGENDARY VICTORY!", 28,
                new Color(0.88f, 0.94f, 1f), TextAnchor.UpperCenter, new Vector2(0, 220), new Vector2(640, 42));
            AddTextShadow(ui.gameOverTitleText);

            ui.gameOverSubtitleText = CreateText(modalBox.transform, "SubtitleText", "You crushed the enemy warlord and claimed supreme dominion over the arena!", 15,
                new Color(0.9f, 0.95f, 1f), TextAnchor.UpperCenter, new Vector2(0, 175), new Vector2(640, 30));

            var statsBox = CreateOrnatePanel(modalBox.transform, "StatsBox", new Color(0.05f, 0.06f, 0.11f, 0.94f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(600, 240));

            CreateText(statsBox.transform, "StatsHeader", "BATTLE SUMMARY & STATISTICS", 15,
                new Color(0.78f, 0.86f, 0.95f), TextAnchor.UpperCenter, new Vector2(0, 95), new Vector2(560, 24));

            ui.gameOverStatsText = CreateText(statsBox.transform, "StatsContent", "Calculating battle statistics...", 15,
                Color.white, TextAnchor.MiddleCenter, new Vector2(0, -12), new Vector2(560, 180));

            ui.restartGameButton = CreateBeveledButton(modalBox.transform, "RestartBtn", "🔁 Play Again",
                new Color(0.18f, 0.65f, 0.35f, 0.98f), Color.white,
                new Vector2(-155, -220), new Vector2(270, 52), 16,
                () => ui.SendMessage("OnRestartClicked"));

            ui.gameOverQuitButton = CreateBeveledButton(modalBox.transform, "QuitBtn", "🚪 Quit Game",
                new Color(0.75f, 0.18f, 0.22f, 0.98f), Color.white,
                new Vector2(155, -220), new Vector2(240, 52), 16,
                () => ui.SendMessage("OnQuitClicked"));

            overlay.SetActive(false);
            ui.gameOverPanel = overlay;
        }

        private static void BuildCommanderCitadel(Transform leftWingParent, BattleUIManager ui, bool isPlayerSide, Sprite dockArtSprite)
        {
            string prefix = isPlayerSide ? "Player" : "Enemy";
            Vector2 anchor = isPlayerSide ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 1f);
            Vector2 pivot = isPlayerSide ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 1f);
            Vector2 pos = isPlayerSide ? new Vector2(0f, 18f) : new Vector2(0f, -18f);

            var citadel = CreateOrnatePanel(leftWingParent, $"{prefix}LeaderCrest",
                isPlayerSide ? new Color(0.06f, 0.09f, 0.14f, 0.95f) : new Color(0.11f, 0.06f, 0.08f, 0.95f),
                anchor, anchor, pos, new Vector2(320, 126));
            var cRect = citadel.GetComponent<RectTransform>();
            if (cRect != null)
            {
                cRect.pivot = pivot;
                cRect.anchoredPosition = pos;
            }

            if (dockArtSprite != null)
            {
                var cImg = citadel.GetComponent<Image>();
                cImg.sprite = dockArtSprite;
                cImg.type = Image.Type.Simple;
                cImg.color = isPlayerSide
                    ? new Color(0.62f, 0.68f, 0.76f, 0.95f)
                    : new Color(0.70f, 0.58f, 0.62f, 0.95f);
            }

            Color rimColor = isPlayerSide
                ? new Color(0.28f, 0.38f, 0.48f, 0.92f)
                : new Color(0.40f, 0.21f, 0.24f, 0.92f);
            var cOut = citadel.AddComponent<Outline>();
            cOut.effectColor = rimColor;
            cOut.effectDistance = new Vector2(1.2f, 1.2f);

            // 1. Compact Portrait Frame (Left inside Citadel: 78 x 78)
            var portraitFrame = CreatePanel(citadel.transform, $"{prefix}PortraitFrame",
                new Color(0.04f, 0.05f, 0.08f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-108, 0), new Vector2(78, 78));
            var pfOut = portraitFrame.AddComponent<Outline>();
            pfOut.effectColor = rimColor;
            pfOut.effectDistance = new Vector2(1.5f, 1.5f);

            var avatarObj = new GameObject($"{prefix}AvatarImage");
            avatarObj.transform.SetParent(portraitFrame.transform, false);
            var rectAv = avatarObj.AddComponent<RectTransform>();
            rectAv.anchorMin = Vector2.zero;
            rectAv.anchorMax = Vector2.one;
            rectAv.sizeDelta = new Vector2(-6, -6);
            rectAv.anchoredPosition = Vector2.zero;
            var avImg = avatarObj.AddComponent<Image>();
            avImg.preserveAspect = false;

            Sprite avSprite = isPlayerSide ? MonsterArtLoader.GetPlayerAvatarSprite() : MonsterArtLoader.GetEnemyAvatarSprite();
            if (avSprite != null)
            {
                avImg.sprite = avSprite;
                avImg.color = Color.white;
            }
            else
            {
                avImg.color = isPlayerSide ? new Color(0.20f, 0.35f, 0.58f, 1f) : new Color(0.55f, 0.20f, 0.24f, 1f);
            }

            if (isPlayerSide) ui.playerAvatarImage = avImg;
            else ui.enemyAvatarImage = avImg;

            // 2. Identity, Health Bar & Resource Column (Right inside Citadel: width 200)
            string titleStr = isPlayerSide ? "PLAYER COMMANDER" : "ENEMY WARLORD";
            Color titleColor = isPlayerSide ? new Color(0.78f, 0.88f, 0.98f) : new Color(0.90f, 0.72f, 0.74f);
            var titleTxt = CreateText(citadel.transform, $"{prefix}LeaderTitle", titleStr, 12,
                titleColor, TextAnchor.MiddleLeft, new Vector2(44, 39), new Vector2(202, 22));
            titleTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
            titleTxt.resizeTextForBestFit = true;
            titleTxt.resizeTextMinSize = 10;
            titleTxt.resizeTextMaxSize = 12;
            AddTextShadow(titleTxt);

            Color hpFillColor = isPlayerSide ? new Color(0.16f, 0.72f, 0.34f) : new Color(0.78f, 0.18f, 0.24f);
            var hpSlider = CreateSlider(citadel.transform, $"{prefix}HpBar",
                new Color(0.05f, 0.06f, 0.09f, 0.98f), hpFillColor,
                new Vector2(44, 8), new Vector2(202, 22));

            var hpTxt = CreateText(citadel.transform, $"{prefix}HpText", "500 / 500 HP", 12,
                Color.white, TextAnchor.MiddleCenter, new Vector2(44, 8), new Vector2(202, 22));
            AddTextShadow(hpTxt);

            if (isPlayerSide)
            {
                ui.playerHpSlider = hpSlider;
                ui.playerHpText = hpTxt;
            }
            else
            {
                ui.enemyHpSlider = hpSlider;
                ui.enemyHpText = hpTxt;
            }

            var statusBox = CreateOrnatePanel(citadel.transform, $"{prefix}StatusChamber",
                isPlayerSide ? new Color(0.05f, 0.09f, 0.16f, 0.95f) : new Color(0.14f, 0.07f, 0.10f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                isPlayerSide ? new Vector2(44, -30) : new Vector2(44, -32),
                isPlayerSide ? new Vector2(202, 34) : new Vector2(202, 44));

            var statusRect = statusBox.GetComponent<RectTransform>();
            var deckOriginObj = new GameObject($"{prefix}DeckOrigin");
            deckOriginObj.transform.SetParent(citadel.transform, false);
            var deckOriginRect = deckOriginObj.AddComponent<RectTransform>();
            deckOriginRect.anchorMin = new Vector2(0.5f, 0.5f);
            deckOriginRect.anchorMax = new Vector2(0.5f, 0.5f);
            deckOriginRect.sizeDelta = new Vector2(16, 16);
            deckOriginRect.anchoredPosition = isPlayerSide ? new Vector2(158f, -10f) : new Vector2(158f, 10f);

            if (isPlayerSide)
            {
                ui.playerPowerAnchor = statusRect;
                ui.playerDeckAnchor = deckOriginRect;
            }
            else
            {
                ui.enemyPowerAnchor = statusRect;
                ui.enemyDeckAnchor = deckOriginRect;
            }

            if (isPlayerSide)
            {
                ui.playerPowerText = CreateText(statusBox.transform, "PlayerPowerText", "[ ◇ ] Mana: 0  |  Power: 0 (∞)", 11,
                    new Color(0.74f, 0.88f, 0.98f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(194, 30));
                ui.playerPowerText.horizontalOverflow = HorizontalWrapMode.Wrap;
                ui.playerPowerText.resizeTextForBestFit = true;
                ui.playerPowerText.resizeTextMinSize = 8;
                ui.playerPowerText.resizeTextMaxSize = 11;
                AddTextShadow(ui.playerPowerText);
            }
            else
            {
                ui.enemyPowerText = CreateText(statusBox.transform, "EnemyPowerText", "[ ◇ ]  Mana: 0  |  Power: 0 (∞)", 10,
                    new Color(0.98f, 0.78f, 0.82f), TextAnchor.MiddleCenter, new Vector2(0, 9), new Vector2(196, 22));
                ui.enemyPowerText.horizontalOverflow = HorizontalWrapMode.Wrap;
                ui.enemyPowerText.resizeTextForBestFit = true;
                ui.enemyPowerText.resizeTextMinSize = 7;
                ui.enemyPowerText.resizeTextMaxSize = 11;
                AddTextShadow(ui.enemyPowerText);

                ui.enemyHandCountText = CreateText(statusBox.transform, "EnemyHandLabel", "🎴 Hand: 6  |  Deck: 44", 10,
                    new Color(0.84f, 0.85f, 0.92f), TextAnchor.MiddleCenter, new Vector2(0, -11), new Vector2(196, 18));
                ui.enemyHandCountText.horizontalOverflow = HorizontalWrapMode.Wrap;
                ui.enemyHandCountText.resizeTextForBestFit = true;
                ui.enemyHandCountText.resizeTextMinSize = 7;
                ui.enemyHandCountText.resizeTextMaxSize = 10;
                AddTextShadow(ui.enemyHandCountText);
            }
        }

        private static void BuildHandDock(Transform parent, BattleUIManager ui, bool isPlayerSide, Sprite dockArtSprite)
        {
            string prefix = isPlayerSide ? "Player" : "Enemy";
            Vector2 anchor = isPlayerSide ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 1f);
            Vector2 pivot = isPlayerSide ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 1f);

            // 1. Root Hand Dock Panel (Pure 1200 x 196 Oil-Painted Hand Tray)
            var dock = CreatePanel(parent, isPlayerSide ? "HandPanel" : "TopHeaderZone", Color.white,
                anchor, anchor, Vector2.zero, new Vector2(1200, 196));
            var rectDock = dock.GetComponent<RectTransform>();
            if (rectDock != null)
            {
                rectDock.pivot = pivot;
                rectDock.anchoredPosition = Vector2.zero;
            }

            var dockImg = dock.GetComponent<Image>();
            if (dockArtSprite != null)
            {
                dockImg.sprite = dockArtSprite;
                dockImg.type = Image.Type.Simple;
                dockImg.preserveAspect = false;
                dockImg.color = new Color(0.76f, 0.78f, 0.84f, 1f);
            }
            else
            {
                dockImg.color = isPlayerSide
                    ? new Color(0.05f, 0.06f, 0.09f, 0.98f)
                    : new Color(0.08f, 0.05f, 0.07f, 0.98f);

                Color borderRimColor = new Color(0.24f, 0.27f, 0.34f, 0.90f);
                CreateHollowOrnatePanel(dock.transform, $"{prefix}DockOrnateBorder", borderRimColor,
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }

            // 2. 6 Subtle Recessed Card Bays across the matte hand dock
            float[] dockX = { -420f, -252f, -84f, 84f, 252f, 420f };
            for (int k = 0; k < 6; k++)
            {
                var bay = CreatePanel(dock.transform, $"{prefix}CardBay_{k}",
                    new Color(0.02f, 0.02f, 0.04f, 0.30f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(dockX[k], 0), new Vector2(138, 168));
                bay.GetComponent<Image>().raycastTarget = false;

                var bayOutline = bay.AddComponent<Outline>();
                bayOutline.effectColor = new Color(0.24f, 0.28f, 0.35f, 0.36f);
                bayOutline.effectDistance = new Vector2(1.0f, 1.0f);
            }

            // 3. Dynamic Cards Container with HorizontalLayoutGroup (Identical card sizes 138x182)
            var cardsContainer = new GameObject(isPlayerSide ? "CardsContainer" : "EnemyCardsContainer");
            cardsContainer.transform.SetParent(dock.transform, false);
            var rectCC = cardsContainer.AddComponent<RectTransform>();
            rectCC.anchorMin = new Vector2(0.5f, 0.5f);
            rectCC.anchorMax = new Vector2(0.5f, 0.5f);
            rectCC.pivot = new Vector2(0.5f, 0.5f);
            rectCC.sizeDelta = new Vector2(1140, 186);
            rectCC.anchoredPosition = Vector2.zero;

            var layout = cardsContainer.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 30;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            if (isPlayerSide)
            {
                ui.playerHandContainer = cardsContainer.transform;
            }
            else
            {
                ui.enemyHandContainer = cardsContainer.transform;
            }
        }

        private static void BuildSlotsRow(Transform parent, BattleUIManager ui, PlayerState player, bool isPlayerSide)
        {
            float posY = isPlayerSide ? -135f : 135f;
            float[] xCoords = { -426f, -255f, -86f, 86f, 255f, 426f };

            for (int i = 0; i < 6; i++)
            {
                float posX = xCoords[i];

                Color runeColor;
                if (isPlayerSide)
                {
                    runeColor = (i == 2 || i == 3)
                        ? new Color(1f, 0.80f, 0.32f, 0.95f)
                        : new Color(0.38f, 0.88f, 1f, 0.92f);
                }
                else
                {
                    runeColor = (i == 2 || i == 3)
                        ? new Color(1f, 0.42f, 0.38f, 0.95f)
                        : new Color(0.85f, 0.50f, 1f, 0.92f);
                }

                var slotObj = CreatePanel(parent, $"{(isPlayerSide ? "P" : "E")}_SlotView_{i}",
                    Color.clear,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(posX, posY), new Vector2(148, 184));

                var slotImg = slotObj.GetComponent<Image>();
                slotImg.color = Color.clear;

                // Subtle Monochrome Summoning Circle
                var circleObj = new GameObject("SummoningCircle");
                circleObj.transform.SetParent(slotObj.transform, false);
                var circleRect = circleObj.AddComponent<RectTransform>();
                circleRect.anchorMin = new Vector2(0.5f, 0.5f);
                circleRect.anchorMax = new Vector2(0.5f, 0.5f);
                circleRect.sizeDelta = new Vector2(130, 130);
                circleRect.anchoredPosition = Vector2.zero;
                var circleImg = circleObj.AddComponent<Image>();
                circleImg.sprite = GetSummoningCircleSprite();
                circleImg.color = new Color(1f, 1f, 1f, 0.60f);
                circleImg.raycastTarget = false;

                var slotView = slotObj.AddComponent<SlotView>();
                slotView.Setup(i, isPlayerSide, player.slots[i]);
                slotView.summoningCircleImage = circleImg;
                slotView.slotNameText = null;

                if (isPlayerSide)
                {
                    ui.playerSlotViews[i] = slotView;
                }
                else
                {
                    ui.enemySlotViews[i] = slotView;
                }
            }
        }

        // =========================================================================
        // Procedural AAA Fantasy UI Frame, Medallion & Button Generators
        // =========================================================================

        public static Sprite GetOrnateFrameSprite()
        {
            if (_ornateFrameSprite != null) return _ornateFrameSprite;
            int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int dx = Mathf.Min(x, size - 1 - x);
                    int dy = Mathf.Min(y, size - 1 - y);
                    int dEdge = Mathf.Min(dx, dy);

                    bool isCornerMedallion = (dx < 20 && dy < 20);
                    Color px;

                    if (dEdge == 0)
                    {
                        px = new Color(0.03f, 0.04f, 0.06f, 0.98f);
                    }
                    else if (isCornerMedallion)
                    {
                        float cDist = Mathf.Max(Mathf.Abs(dx - 10), Mathf.Abs(dy - 10));
                        if (cDist <= 3.5f)
                        {
                            px = new Color(0.56f, 0.62f, 0.72f, 1f);
                        }
                        else if (cDist <= 7.5f)
                        {
                            float bevel = (y > size / 2 || x < size / 2) ? 1.12f : 0.82f;
                            px = new Color(Mathf.Clamp01(0.34f * bevel), Mathf.Clamp01(0.38f * bevel), Mathf.Clamp01(0.46f * bevel), 1f);
                        }
                        else
                        {
                            px = new Color(0.16f, 0.18f, 0.23f, 1f);
                        }
                    }
                    else if (dEdge >= 1 && dEdge <= 4)
                    {
                        bool topOrLeft = (y > size - 6) || (x < 6);
                        px = topOrLeft
                            ? new Color(0.34f, 0.38f, 0.46f, 1f)
                            : new Color(0.16f, 0.19f, 0.24f, 1f);
                    }
                    else if (dEdge >= 5 && dEdge <= 8)
                    {
                        px = new Color(0.09f, 0.11f, 0.15f, 0.98f);
                    }
                    else if (dEdge >= 9 && dEdge <= 10)
                    {
                        px = new Color(0.25f, 0.29f, 0.36f, 0.95f);
                    }
                    else
                    {
                        float vig = Mathf.Clamp01((dEdge - 10f) / 26f);
                        float pattern = ((x + y) % 8 == 0 || (x - y + 256) % 12 == 0) ? 0.04f : 0f;
                        float baseVal = Mathf.Lerp(0.58f, 0.92f, vig) + pattern;
                        px = new Color(baseVal, baseVal, baseVal, 1f);
                    }

                    tex.SetPixel(x, y, px);
                }
            }
            tex.Apply();
            _ornateFrameSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(22, 22, 22, 22));
            return _ornateFrameSprite;
        }

        public static Sprite GetHollowOrnateBorderSprite()
        {
            if (_hollowOrnateBorderSprite != null) return _hollowOrnateBorderSprite;
            int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int dx = Mathf.Min(x, size - 1 - x);
                    int dy = Mathf.Min(y, size - 1 - y);
                    int dEdge = Mathf.Min(dx, dy);
                    bool isCorner = (dx < 18 && dy < 18);

                    Color px = Color.clear;
                    if (dEdge == 0)
                    {
                        px = new Color(0.04f, 0.05f, 0.07f, 0.95f);
                    }
                    else if (isCorner)
                    {
                        float cDist = Mathf.Max(Mathf.Abs(dx - 9), Mathf.Abs(dy - 9));
                        if (cDist <= 3f)
                            px = new Color(0.56f, 0.62f, 0.72f, 1f);
                        else if (cDist <= 7f)
                            px = new Color(0.36f, 0.41f, 0.50f, 1f);
                        else
                            px = new Color(0.16f, 0.19f, 0.24f, 0.95f);
                    }
                    else if (dEdge >= 1 && dEdge <= 4)
                    {
                        bool hi = (y > size - 6) || (x < 6);
                        px = hi ? new Color(0.42f, 0.47f, 0.56f, 1f) : new Color(0.22f, 0.25f, 0.31f, 1f);
                    }
                    else if (dEdge >= 5 && dEdge <= 7)
                    {
                        px = new Color(0.09f, 0.11f, 0.14f, 0.92f);
                    }
                    else if (dEdge == 8 || dEdge == 9)
                    {
                        px = new Color(0.32f, 0.37f, 0.45f, 0.90f);
                    }
                    else if (dEdge >= 10 && dEdge <= 16)
                    {
                        float shadowAlpha = Mathf.Lerp(0.45f, 0f, (dEdge - 10f) / 6f);
                        px = new Color(0f, 0f, 0f, shadowAlpha);
                    }

                    tex.SetPixel(x, y, px);
                }
            }
            tex.Apply();
            _hollowOrnateBorderSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(20, 20, 20, 20));
            return _hollowOrnateBorderSprite;
        }

        public static Sprite GetBeveledButtonSprite()
        {
            if (_beveledButtonSprite != null) return _beveledButtonSprite;
            int w = 128, h = 64;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < h; y++)
            {
                float ny = (float)y / (h - 1);
                for (int x = 0; x < w; x++)
                {
                    int dx = Mathf.Min(x, w - 1 - x);
                    int dy = Mathf.Min(y, h - 1 - y);
                    int dEdge = Mathf.Min(dx, dy);
                    bool isCorner = (dx < 12 && dy < 12);

                    Color px;
                    if (dEdge == 0)
                    {
                        px = new Color(0.03f, 0.04f, 0.06f, 0.98f);
                    }
                    else if (isCorner)
                    {
                        px = new Color(0.32f, 0.36f, 0.44f, 1f);
                    }
                    else if (dEdge >= 1 && dEdge <= 3)
                    {
                        px = (ny > 0.5f)
                            ? new Color(0.42f, 0.46f, 0.54f, 1f)
                            : new Color(0.18f, 0.21f, 0.26f, 1f);
                    }
                    else if (dEdge == 4 || dEdge == 5)
                    {
                        px = new Color(0.12f, 0.14f, 0.19f, 0.98f);
                    }
                    else
                    {
                        float grad = Mathf.Lerp(0.68f, 1.08f, ny);
                        if (ny > 0.72f && ny < 0.88f) grad += 0.14f;
                        grad = Mathf.Clamp01(grad);
                        px = new Color(grad, grad, grad, 1f);
                    }
                    tex.SetPixel(x, y, px);
                }
            }
            tex.Apply();
            _beveledButtonSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16, 14, 16, 14));
            return _beveledButtonSprite;
        }

        public static Sprite GetMedallionRingSprite()
        {
            if (_medallionRingSprite != null) return _medallionRingSprite;
            int size = 96;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float maxR = size * 0.48f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    if (dist > maxR + 1f)
                    {
                        tex.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float alpha = Mathf.Clamp01(maxR - dist + 1f);
                    float nr = dist / maxR;
                    Color px;
                    if (nr > 0.82f)
                    {
                        px = new Color(0.54f, 0.60f, 0.70f, alpha);
                    }
                    else if (nr > 0.72f)
                    {
                        px = new Color(0.13f, 0.16f, 0.21f, alpha);
                    }
                    else if (nr > 0.65f)
                    {
                        px = new Color(0.38f, 0.44f, 0.52f, alpha);
                    }
                    else
                    {
                        float glow = Mathf.Lerp(1f, 0.65f, nr / 0.65f);
                        px = new Color(glow, glow, glow, alpha);
                    }
                    tex.SetPixel(x, y, px);
                }
            }
            tex.Apply();
            _medallionRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _medallionRingSprite;
        }

        public static Sprite GetSlotPedestalSprite(bool isEnemy)
        {
            if (isEnemy && _enemySlotPedestalSprite != null) return _enemySlotPedestalSprite;
            if (!isPlayerPedestalNull(isEnemy) && !isEnemy) return _playerSlotPedestalSprite;

            int w = 148, h = 184;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            Color primaryRim = isEnemy ? new Color(0.68f, 0.34f, 0.34f, 0.85f) : new Color(0.45f, 0.62f, 0.76f, 0.85f);
            Color goldAccent = isEnemy ? new Color(0.56f, 0.32f, 0.34f, 0.80f) : new Color(0.42f, 0.50f, 0.60f, 0.80f);
            Color plateFill = isEnemy ? new Color(0.10f, 0.06f, 0.08f, 0.18f) : new Color(0.06f, 0.09f, 0.14f, 0.18f);

            Vector2 circleCenter = new Vector2(w * 0.5f, h * 0.5f);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int dx = Mathf.Min(x, w - 1 - x);
                    int dy = Mathf.Min(y, h - 1 - y);
                    int dEdge = Mathf.Min(dx, dy);

                    bool isCornerBracket = (dx < 22 && dy < 22) && (dx < 5 || dy < 5);

                    Color px = plateFill;

                    if (dEdge <= 1)
                    {
                        px = new Color(goldAccent.r, goldAccent.g, goldAccent.b, 0.75f);
                    }
                    else if (isCornerBracket)
                    {
                        px = goldAccent;
                    }
                    else if (dEdge >= 2 && dEdge <= 3)
                    {
                        px = new Color(primaryRim.r, primaryRim.g, primaryRim.b, 0.55f);
                    }
                    else
                    {
                        float cDist = Vector2.Distance(new Vector2(x, y), circleCenter);
                        if (Mathf.Abs(cDist - 52f) < 2.2f || Mathf.Abs(cDist - 38f) < 1.5f)
                        {
                            px = new Color(primaryRim.r, primaryRim.g, primaryRim.b, 0.65f);
                        }
                        else if (cDist < 52f)
                        {
                            px = new Color(plateFill.r * 1.2f, plateFill.g * 1.2f, plateFill.b * 1.2f, 0.22f);
                        }
                    }

                    tex.SetPixel(x, y, px);
                }
            }
            tex.Apply();
            Sprite sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            if (isEnemy) _enemySlotPedestalSprite = sp;
            else _playerSlotPedestalSprite = sp;
            return sp;
        }

        private static bool isPlayerPedestalNull(bool isEnemy)
        {
            return isEnemy ? (_enemySlotPedestalSprite == null) : (_playerSlotPedestalSprite == null);
        }

        private static Sprite LoadStreamingOrResourceSprite(ref Sprite cache, string fileName, string resourceName, float mipBias = -0.35f)
        {
            if (cache != null) return cache;

            if (Application.isPlaying)
            {
                string streamPath = Path.Combine(Application.streamingAssetsPath, fileName);
                if (File.Exists(streamPath))
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(streamPath);
                        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                        if (tex.LoadImage(bytes))
                        {
                            tex.filterMode = FilterMode.Trilinear;
                            tex.anisoLevel = 16;
                            tex.mipMapBias = mipBias;
                            cache = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                            return cache;
                        }
                    }
                    catch { }
                }
            }

            var resTex = Resources.Load<Texture2D>(resourceName);
            if (resTex != null)
            {
                resTex.filterMode = FilterMode.Trilinear;
                cache = Sprite.Create(resTex, new Rect(0, 0, resTex.width, resTex.height), new Vector2(0.5f, 0.5f), 100f);
                return cache;
            }

            return null;
        }

        public static Sprite GetSummoningCircleSprite()
        {
            return LoadStreamingOrResourceSprite(ref _summoningCircleSprite, "summoning_circle.png", "summoning_circle", -0.5f)
                   ?? GetCircleSprite();
        }

        public static Sprite GetHandTraySprite()
        {
            return LoadStreamingOrResourceSprite(ref _handTraySprite, "hand_tray_frame.jpg", "hand_tray_frame", -0.35f);
        }

        public static GameObject CreateOrnatePanel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
        {
            var obj = CreatePanel(parent, name, color, anchorMin, anchorMax, pos, size);
            var img = obj.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = GetOrnateFrameSprite();
                img.type = Image.Type.Sliced;
            }
            return obj;
        }

        public static GameObject CreateHollowOrnatePanel(Transform parent, string name, Color borderTint, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
        {
            var obj = CreatePanel(parent, name, borderTint, anchorMin, anchorMax, pos, size);
            var img = obj.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = GetHollowOrnateBorderSprite();
                img.type = Image.Type.Sliced;
                img.raycastTarget = false;
            }
            return obj;
        }

        public static GameObject CreateMedallion(Transform parent, string name, Color color, Vector2 pos, Vector2 size, string symbol, int fontSize)
        {
            var obj = CreatePanel(parent, name, color, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            var img = obj.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = GetMedallionRingSprite();
                img.type = Image.Type.Simple;
            }
            var txt = CreateText(obj.transform, "MedallionText", symbol, fontSize, Color.white, TextAnchor.MiddleCenter, Vector2.zero, size);
            txt.raycastTarget = false;
            AddTextShadow(txt);
            return obj;
        }

        public static Button CreateBeveledButton(Transform parent, string name, string label, Color bgColor, Color textColor, Vector2 pos, Vector2 size, int fontSize, UnityEngine.Events.UnityAction onClick)
        {
            var btnObj = CreatePanel(parent, name, bgColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            var img = btnObj.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = GetBeveledButtonSprite();
                img.type = Image.Type.Sliced;
            }

            var btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var txt = CreateText(btnObj.transform, "BtnText", label, fontSize, textColor, TextAnchor.MiddleCenter, Vector2.zero, size);
            txt.raycastTarget = false;
            AddTextShadow(txt);
            return btn;
        }

        public static void AddTextShadow(Text txt)
        {
            if (txt == null) return;
            var sh = txt.gameObject.GetComponent<Shadow>();
            if (sh == null) sh = txt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.88f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
        }

        public static Slider CreateSlider(Transform parent, string name, Color bgColor, Color fillColor, Vector2 pos, Vector2 size)
        {
            var sliderObj = CreateOrnatePanel(parent, name, bgColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            var slider = sliderObj.AddComponent<Slider>();
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderObj.transform, false);
            var rectFA = fillArea.AddComponent<RectTransform>();
            rectFA.anchorMin = Vector2.zero;
            rectFA.anchorMax = Vector2.one;
            rectFA.sizeDelta = new Vector2(-8, -6);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var rectF = fill.AddComponent<RectTransform>();
            rectF.anchorMin = Vector2.zero;
            rectF.anchorMax = Vector2.one;
            rectF.sizeDelta = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = fillColor;

            slider.targetGraphic = fillImg;
            slider.fillRect = rectF;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;

            return slider;
        }

        public static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float radius = size * 0.48f;
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _circleSprite;
        }

        public static Sprite GetElementalAuraHaloSprite()
        {
            if (_elementalAuraHaloSprite != null) return _elementalAuraHaloSprite;

            int w = 192, h = 232;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float cx = (w - 1) * 0.5f;
            float cy = (h - 1) * 0.5f;
            float halfCardW = 72f;
            float halfCardH = 90f;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x - cx);
                    float dy = Mathf.Abs(y - cy);

                    // Signed distance outside the card frame rectangle
                    float qx = Mathf.Max(dx - halfCardW, 0f);
                    float qy = Mathf.Max(dy - halfCardH, 0f);
                    float outDist = Mathf.Sqrt(qx * qx + qy * qy);

                    if (outDist > 24f)
                    {
                        tex.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float angle = Mathf.Atan2(y - cy, x - cx);
                    float wisp = 0.82f + 0.18f * Mathf.Sin(angle * 10f + outDist * 0.25f);
                    float glow = outDist <= 1.5f
                        ? 0.92f
                        : Mathf.Exp(-(outDist - 1.5f) * 0.145f) * wisp;

                    float edgeFade = Mathf.Clamp01((24f - outDist) / 5f);
                    float alpha = Mathf.Clamp01(glow * edgeFade);
                    float whiteness = Mathf.Lerp(0.75f, 1.0f, Mathf.Exp(-outDist * 0.25f));
                    tex.SetPixel(x, y, new Color(whiteness, whiteness, whiteness, alpha));
                }
            }

            tex.Apply();
            _elementalAuraHaloSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            return _elementalAuraHaloSprite;
        }

        public static Sprite GetCircularBlueFlareSprite()
        {
            if (_circularBlueFlareSprite != null) return _circularBlueFlareSprite;

            int size = 192;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float c = (size - 1) * 0.5f;
            float maxR = size * 0.49f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - c) / maxR;
                    float ny = (y - c) / maxR;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    if (r >= 0.99f)
                    {
                        tex.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    // Smooth radial bloom + luminous circular corona peaking right outside the 72px medallion rim
                    float bloom = Mathf.Exp(-r * r * 4.4f) * 0.72f;
                    float coronaRim = Mathf.Exp(-Mathf.Pow((r - 0.54f) * 7.5f, 2f)) * 0.88f;

                    float angle = Mathf.Atan2(ny, nx);
                    float rayPrimary = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2f)), 10f);
                    float rayDiag = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2f + Mathf.PI * 0.5f)), 8f);
                    float radialEnvelope = Mathf.Pow(Mathf.Clamp01(1f - r), 1.55f);
                    float flareRays = (rayPrimary * 0.42f + rayDiag * 0.22f) * radialEnvelope;

                    float edgeFade = Mathf.Clamp01((0.99f - r) / 0.22f);
                    edgeFade = edgeFade * edgeFade * (3f - 2f * edgeFade);

                    float alpha = Mathf.Clamp01((bloom + coronaRim + flareRays) * edgeFade);
                    float heat = Mathf.Clamp01(coronaRim * 0.65f + bloom * 0.5f + flareRays * 0.7f);
                    float redCh = Mathf.Lerp(0.45f, 0.92f, heat);
                    float greenCh = Mathf.Lerp(0.80f, 0.98f, heat);

                    tex.SetPixel(x, y, new Color(redCh, greenCh, 1.0f, alpha));
                }
            }

            tex.Apply();
            _circularBlueFlareSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _circularBlueFlareSprite;
        }

        public static Sprite GetMirageRippleSprite()
        {
            if (_mirageRippleSprite != null) return _mirageRippleSprite;

            int w = 256, h = 128;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < h; y++)
            {
                float ny = (y / (float)(h - 1)) * 2f - 1f;
                float edgeY = Mathf.Clamp01((1f - Mathf.Abs(ny)) / 0.25f);
                edgeY = edgeY * edgeY * (3f - 2f * edgeY);

                for (int x = 0; x < w; x++)
                {
                    float nx = (x / (float)(w - 1)) * 2f - 1f;
                    float edgeX = Mathf.Clamp01((1f - Mathf.Abs(nx)) / 0.18f);
                    edgeX = edgeX * edgeX * (3f - 2f * edgeX);

                    // Overlapping liquid heat/arcane mirage caustic bands
                    float w1 = 0.5f + 0.5f * Mathf.Sin(y * 0.34f + Mathf.Sin(x * 0.055f) * 2.4f);
                    float w2 = 0.5f + 0.5f * Mathf.Sin(y * 0.22f - Mathf.Cos(x * 0.042f) * 2.1f);
                    float caustic = Mathf.Pow(w1 * 0.58f + w2 * 0.42f, 2.2f);
                    float shimmer = Mathf.Pow(w1 * w2, 1.8f);

                    float alpha = Mathf.Clamp01((caustic * 0.72f + shimmer * 0.48f) * edgeX * edgeY);
                    float lum = Mathf.Lerp(0.76f, 1.0f, shimmer);
                    tex.SetPixel(x, y, new Color(lum, lum * 0.98f, 1f, alpha));
                }
            }

            tex.Apply();
            _mirageRippleSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            return _mirageRippleSprite;
        }

        public static Sprite GetMirageWaveSweepSprite()
        {
            if (_mirageWaveSweepSprite != null) return _mirageWaveSweepSprite;

            int w = 160, h = 160;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < h; y++)
            {
                float ny = (y / (float)(h - 1)) * 2f - 1f;
                float envY = Mathf.Clamp01(1f - ny * ny);
                float curveOffset = Mathf.Sin(ny * 2.6f) * 0.14f;

                for (int x = 0; x < w; x++)
                {
                    float nx = (x / (float)(w - 1)) * 2f - 1f - curveOffset;
                    float core = Mathf.Exp(-nx * nx * 16f);
                    float trailingHaze = Mathf.Exp(-(nx + 0.28f) * (nx + 0.28f) * 5.2f) * 0.58f;
                    float ripple = 0.78f + 0.22f * Mathf.Sin(y * 0.40f + nx * 10f);

                    float alpha = Mathf.Clamp01((core + trailingHaze) * envY * ripple);
                    float peak = Mathf.Clamp01(core * 0.85f);
                    tex.SetPixel(x, y, new Color(Mathf.Lerp(0.75f, 1f, peak), Mathf.Lerp(0.92f, 1f, peak), 1f, alpha));
                }
            }

            tex.Apply();
            _mirageWaveSweepSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            return _mirageWaveSweepSprite;
        }

        public static Sprite GetAbilityFlashCoreSprite()
        {
            if (_abilityFlashCoreSprite != null) return _abilityFlashCoreSprite;

            int size = 96;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float c = (size - 1) * 0.5f;
            float maxR = size * 0.48f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - c) / maxR;
                    float ny = (y - c) / maxR;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    if (r > 1.05f)
                    {
                        tex.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    // Soft circular halo + sharp 4-point star flare + subtle diagonal rays
                    float orb = Mathf.Exp(-r * r * 5.5f);
                    float crossFlare = (Mathf.Exp(-Mathf.Abs(nx) * 14f) * Mathf.Exp(-r * 2.2f))
                                     + (Mathf.Exp(-Mathf.Abs(ny) * 14f) * Mathf.Exp(-r * 2.2f));
                    float diag1 = (nx + ny) * 0.7071f;
                    float diag2 = (nx - ny) * 0.7071f;
                    float diagFlare = 0.5f * ((Mathf.Exp(-Mathf.Abs(diag1) * 18f) * Mathf.Exp(-r * 3.0f))
                                            + (Mathf.Exp(-Mathf.Abs(diag2) * 18f) * Mathf.Exp(-r * 3.0f)));

                    float coreCircle = Mathf.Clamp01((0.46f - r) * 8f) * 0.75f;
                    float intensity = Mathf.Clamp01(orb * 0.85f + crossFlare * 0.65f + diagFlare * 0.5f + coreCircle);
                    float edgeFade = Mathf.Clamp01((1.02f - r) * 5f);
                    float alpha = Mathf.Clamp01(intensity * edgeFade);
                    float bright = Mathf.Lerp(0.85f, 1.0f, intensity);

                    tex.SetPixel(x, y, new Color(bright, bright, bright, alpha));
                }
            }

            tex.Apply();
            _abilityFlashCoreSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _abilityFlashCoreSprite;
        }

        public static Sprite GetAbilityShockwaveRingSprite()
        {
            if (_abilityShockwaveRingSprite != null) return _abilityShockwaveRingSprite;

            int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float c = (size - 1) * 0.5f;
            float maxR = size * 0.48f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - c) / maxR;
                    float ny = (y - c) / maxR;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    if (r > 1.0f)
                    {
                        tex.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float ring1 = Mathf.Exp(-Mathf.Pow((r - 0.82f) * 9.5f, 2f));
                    float ring2 = 0.45f * Mathf.Exp(-Mathf.Pow((r - 0.62f) * 12f, 2f));
                    float alpha = Mathf.Clamp01(ring1 + ring2) * Mathf.Clamp01((1.0f - r) * 8f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            _abilityShockwaveRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _abilityShockwaveRingSprite;
        }

        private static void EvalSwordShape(float u, float v, out float alpha, out float shade)
        {
            const float aa = 0.035f;
            alpha = 0f;
            shade = 0.9f;

            // Blade (-0.26 to 0.84)
            if (u >= -0.28f && u <= 0.88f)
            {
                float halfW = (u <= 0.56f) ? 0.088f : Mathf.Max(0f, 0.088f * (0.84f - u) / 0.28f);
                float dBlade = halfW - v;
                float a = Mathf.Clamp01((dBlade + aa * 0.5f) / aa);
                if (a > alpha)
                {
                    alpha = a;
                    shade = (v < 0.028f) ? 1.0f : 0.84f;
                }
            }

            // Crossguard (-0.37 to -0.25)
            float dGuard = Mathf.Min(Mathf.Min(u + 0.37f, -0.25f - u), 0.27f - v);
            float ga = Mathf.Clamp01((dGuard + aa * 0.5f) / aa);
            if (ga > alpha)
            {
                alpha = ga;
                shade = 0.96f;
            }

            // Grip (-0.68 to -0.37)
            float dGrip = Mathf.Min(Mathf.Min(u + 0.68f, -0.37f - u), 0.052f - v);
            float ha = Mathf.Clamp01((dGrip + aa * 0.5f) / aa);
            if (ha > alpha)
            {
                alpha = ha;
                shade = 0.72f;
            }

            // Pommel at (-0.74, 0)
            float dPommel = 0.092f - Mathf.Sqrt((u + 0.74f) * (u + 0.74f) + v * v);
            float pa = Mathf.Clamp01((dPommel + aa * 0.5f) / aa);
            if (pa > alpha)
            {
                alpha = pa;
                shade = 0.95f;
            }
        }

        public static Sprite GetCrossedSwordsSprite()
        {
            if (_crossedSwordsSprite != null) return _crossedSwordsSprite;

            int size = 96;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float c = (size - 1) * 0.5f;
            float maxR = size * 0.46f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - c) / maxR;
                    float ny = (y - c) / maxR;

                    // Sword 1: bottom-left -> top-right (+45 deg)
                    float u1 = (nx + ny) * 0.7071f;
                    float v1 = Mathf.Abs((-nx + ny) * 0.7071f);
                    EvalSwordShape(u1, v1, out float a1, out float s1);

                    // Sword 2: bottom-right -> top-left (-45 deg)
                    float u2 = (-nx + ny) * 0.7071f;
                    float v2 = Mathf.Abs((nx + ny) * 0.7071f);
                    EvalSwordShape(u2, v2, out float a2, out float s2);

                    // Subtle bevel overlap shadow where Sword 2 crosses Sword 1
                    EvalSwordShape(u2, Mathf.Max(0f, v2 - 0.045f), out float a2Shadow, out float _);

                    float outA = Mathf.Clamp01(a1 + a2 * (1f - a1));
                    if (outA <= 0.005f)
                    {
                        tex.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float s1Shadowed = s1 * Mathf.Lerp(1f, 0.45f, a2Shadow * (1f - a2));
                    float outShade = Mathf.Lerp(s1Shadowed, s2, a2);
                    tex.SetPixel(x, y, new Color(outShade, outShade * 0.98f, outShade * 0.92f, outA));
                }
            }

            tex.Apply();
            _crossedSwordsSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _crossedSwordsSprite;
        }

        public static GameObject CreateCirclePanel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
        {
            var obj = CreatePanel(parent, name, color, anchorMin, anchorMax, pos, size);
            var img = obj.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = GetCircleSprite();
                img.type = Image.Type.Simple;
            }
            return obj;
        }

        public static GameObject CreatePanel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var img = obj.AddComponent<Image>();
            img.color = color;
            return obj;
        }

        public static Text CreateText(Transform parent, string name, string content, int fontSize, Color color, TextAnchor align, Vector2 pos, Vector2 size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var txt = obj.AddComponent<Text>();
            txt.font = GetFont();
            txt.fontStyle = FontStyle.Bold;
            txt.text = content;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = align;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            return txt;
        }

        public static Button CreateButton(Transform parent, string name, string label, Color bgColor, Color textColor, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            return CreateBeveledButton(parent, name, label, bgColor, textColor, pos, size, 15, onClick);
        }

        public static Button CreateAnchorButton(Transform parent, string name, string label, Color bgColor, Color textColor, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var btn = CreateBeveledButton(parent, name, label, bgColor, textColor, pos, size, 15, onClick);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = pos;
            return btn;
        }
    }

    public class QuitButtonHandler : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
        {
            Debug.Log("🚪 Quit button clicked via PointerClick!");
            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.OnQuitClicked();
            }
            else
            {
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
        }
    }
}

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void AutoInitializeGame()
        {
            if (Application.isPlaying)
            {
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

            // 1.7 Sculpted Gold Outer Frame & 4 Corner Bastion Medallions
            var boardOuterFrame = CreateHollowOrnatePanel(centerBoard.transform, "BoardSculptedFrame",
                new Color(1f, 0.85f, 0.42f, 0.98f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            boardOuterFrame.transform.SetAsLastSibling();

            BuildCornerBastionMedallion(boardOuterFrame.transform, "BastionTL", new Vector2(0f, 1f), new Vector2(22, -22), "⚔️");
            BuildCornerBastionMedallion(boardOuterFrame.transform, "BastionTR", new Vector2(1f, 1f), new Vector2(-22, -22), "⚔️");
            BuildCornerBastionMedallion(boardOuterFrame.transform, "BastionBL", new Vector2(0f, 0f), new Vector2(22, 22), "🛡️");
            BuildCornerBastionMedallion(boardOuterFrame.transform, "BastionBR", new Vector2(1f, 0f), new Vector2(-22, 22), "🛡️");

            // =========================================================================
            // ZONE 2: Top Dock — Pure Opponent Hidden Card Hand Tray (1200 x 196)
            // =========================================================================
            BuildHandDock(screenRoot.transform, ui, false, handTrayArtSprite);

            // =========================================================================
            // ZONE 3: Bottom Dock — Pure Player Card Hand Tray (1200 x 196)
            // =========================================================================
            BuildHandDock(screenRoot.transform, ui, true, handTrayArtSprite);

            // =========================================================================
            // ZONE 4: Left Wing — Isolated Top/Bottom Command Citadels & Inspector (360 x 1080)
            // =========================================================================
            var leftWing = CreatePanel(screenRoot.transform, "LeftWingPanel", Color.white,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(180, 0), new Vector2(360, 1080));
            if (sidebarArtSprite != null)
            {
                var lwImg = leftWing.GetComponent<Image>();
                lwImg.sprite = sidebarArtSprite;
                lwImg.color = Color.white;
            }
            else
            {
                leftWing.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.11f, 0.98f);
            }
            CreateHollowOrnatePanel(leftWing.transform, "LeftWingSculptedRim",
                new Color(0.95f, 0.78f, 0.35f, 0.92f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Top-Left: Isolated Enemy Commander Citadel
            BuildCommanderCitadel(leftWing.transform, ui, false, null);

            var topLeftDivider = CreateText(leftWing.transform, "TopLeftWingDivider", "━━━◆ 💀 ◆━━━", 13,
                new Color(0.95f, 0.45f, 0.42f, 0.88f), TextAnchor.MiddleCenter, new Vector2(0, 344), new Vector2(320, 22));
            topLeftDivider.raycastTarget = false;

            // Middle-Left: Reconnaissance & Card Inspector
            var reconHeader = CreateOrnatePanel(leftWing.transform, "ReconHeaderBanner",
                new Color(0.08f, 0.12f, 0.22f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 316), new Vector2(336, 32));
            var rhText = CreateText(reconHeader.transform, "ReconHeaderText", "⚜️ RECONNAISSANCE & INSPECTOR ⚜️", 12,
                new Color(1f, 0.92f, 0.62f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(320, 28));
            AddTextShadow(rhText);

            var detailsPanel = CreateOrnatePanel(leftWing.transform, "CardDetailsInspectorPanel",
                new Color(0.05f, 0.07f, 0.12f, 0.94f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -18), new Vector2(336, 630));
            ui.cardDetailsPanel = detailsPanel;

            // 1. Card Name Bar & Sapphire Mana Cost Medallion
            var detailHeaderBar = CreateOrnatePanel(detailsPanel.transform, "DetailHeaderBar",
                new Color(0.10f, 0.13f, 0.22f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 284), new Vector2(314, 40));

            ui.detailNameText = CreateText(detailHeaderBar.transform, "DetailName", "Select a Monster or Card", 15,
                new Color(1f, 0.92f, 0.48f), TextAnchor.MiddleLeft, new Vector2(-18, 0), new Vector2(236, 32));
            AddTextShadow(ui.detailNameText);

            var fullCostCircle = CreateMedallion(detailHeaderBar.transform, "FullCostCircle",
                new Color(0.12f, 0.42f, 0.88f, 1f), new Vector2(128, 0), new Vector2(38, 38), "-", 16);
            ui.detailCostText = fullCostCircle.transform.Find("MedallionText").GetComponent<Text>();

            // 2. Full-Width Enlarged Artwork Window (314 x 256) with Double Gold Frame
            var artWindow = CreatePanel(detailsPanel.transform, "ArtWindow", new Color(0.03f, 0.04f, 0.08f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 126), new Vector2(314, 256));

            var artObj = new GameObject("FullCardArtImage");
            artObj.transform.SetParent(artWindow.transform, false);
            var rectArt = artObj.AddComponent<RectTransform>();
            rectArt.anchorMin = Vector2.zero;
            rectArt.anchorMax = Vector2.one;
            rectArt.sizeDelta = new Vector2(-8, -8);
            ui.detailCardArt = artObj.AddComponent<Image>();
            ui.detailCardArt.preserveAspect = false;

            CreateHollowOrnatePanel(artWindow.transform, "ArtWindowGoldFrame",
                new Color(0.98f, 0.82f, 0.38f, 0.98f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // 3. Element, Role & Cost Meta Bar
            var metaBar = CreateOrnatePanel(detailsPanel.transform, "MetaBar",
                new Color(0.08f, 0.15f, 0.26f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -24), new Vector2(314, 28));
            ui.detailMetaText = CreateText(metaBar.transform, "DetailMeta", "Click any monster on the board or card in hand", 11,
                new Color(0.48f, 0.90f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(300, 22));
            AddTextShadow(ui.detailMetaText);

            // 4. Tactical Ability & Spell Effect Scroll Box (192 height)
            var descBox = CreateOrnatePanel(detailsPanel.transform, "DescBox",
                new Color(0.04f, 0.05f, 0.09f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -142), new Vector2(314, 192));

            var descHeader = CreateText(descBox.transform, "DescBoxHeader", "─── ❖ TACTICAL ABILITY & EFFECT ❖ ───", 11,
                new Color(0.95f, 0.80f, 0.40f, 0.90f), TextAnchor.UpperCenter, new Vector2(0, 82), new Vector2(290, 18));

            ui.detailDescText = CreateText(descBox.transform, "DetailDesc", "Full monster abilities, tactical passives, and spell effects will appear here.", 12,
                new Color(0.92f, 0.95f, 0.98f), TextAnchor.UpperLeft, new Vector2(0, -10), new Vector2(290, 156));
            ui.detailDescText.horizontalOverflow = HorizontalWrapMode.Wrap;
            ui.detailDescText.verticalOverflow = VerticalWrapMode.Truncate;

            // 5. Bottom ATK / DEF Shields & Unit Status Badge
            var atkBadge = CreateOrnatePanel(detailsPanel.transform, "FullAtkBadge",
                new Color(0.68f, 0.12f, 0.16f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-104, -274), new Vector2(90, 42));
            ui.detailAtkText = CreateText(atkBadge.transform, "AtkText", "⚔️ -", 16,
                Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(84, 36));
            AddTextShadow(ui.detailAtkText);

            var statusBadge = CreateOrnatePanel(detailsPanel.transform, "DetailStatusBadge",
                new Color(0.07f, 0.11f, 0.18f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -274), new Vector2(114, 42));
            ui.detailStatusText = CreateText(statusBadge.transform, "DetailStatus", "⚔️ Ready", 11,
                new Color(0.38f, 1f, 0.58f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(108, 36));
            ui.detailStatsText = ui.detailStatusText;

            var defBadge = CreateOrnatePanel(detailsPanel.transform, "FullDefBadge",
                new Color(0.10f, 0.52f, 0.24f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(104, -274), new Vector2(90, 42));
            ui.detailDefText = CreateText(defBadge.transform, "DefText", "🛡️ -", 16,
                Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(84, 36));
            AddTextShadow(ui.detailDefText);

            var bottomLeftDivider = CreateText(leftWing.transform, "BottomLeftWingDivider", "━━━◆ 👑 ◆━━━", 13,
                new Color(0.95f, 0.80f, 0.38f, 0.88f), TextAnchor.MiddleCenter, new Vector2(0, -344), new Vector2(320, 22));
            bottomLeftDivider.raycastTarget = false;

            // Bottom-Left: Isolated Player Commander Citadel
            BuildCommanderCitadel(leftWing.transform, ui, true, null);

            // =========================================================================
            // ZONE 5: Right Wing — War Operations & Elemental Altar (360 x 1080)
            // =========================================================================
            var rightWing = CreatePanel(screenRoot.transform, "RightWingPanel", Color.white,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-180, 0), new Vector2(360, 1080));
            if (sidebarArtSprite != null)
            {
                var rwImg = rightWing.GetComponent<Image>();
                rwImg.sprite = sidebarArtSprite;
                rwImg.color = Color.white;
            }
            else
            {
                rightWing.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.11f, 0.98f);
            }
            CreateHollowOrnatePanel(rightWing.transform, "RightWingSculptedRim",
                new Color(0.95f, 0.78f, 0.35f, 0.92f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Exit Button at Top of Right Wing
            var quitBtn = CreateBeveledButton(rightWing.transform, "QuitGameBtn", "❌ Exit",
                new Color(0.78f, 0.16f, 0.22f, 0.98f), Color.white,
                new Vector2(0, 492), new Vector2(136, 34), 14,
                () => { if (BattleUIManager.Instance != null) BattleUIManager.Instance.OnQuitClicked(); });
            quitBtn.gameObject.AddComponent<QuitButtonHandler>();
            ui.quitButton = quitBtn;

            // Right Wing Top Header Banner
            var opsHeader = CreateOrnatePanel(rightWing.transform, "OpsHeaderBanner",
                new Color(0.14f, 0.09f, 0.20f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 368), new Vector2(312, 34));
            var ohText = CreateText(opsHeader.transform, "OpsHeaderText", "🚩 WAR OPERATIONS & COMMAND CENTER 🚩", 12,
                new Color(1f, 0.92f, 0.62f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(300, 28));
            AddTextShadow(ohText);

            // 1. Battle & Spell Chronicle Log
            var logPanel = CreateOrnatePanel(rightWing.transform, "CombatLogPanel",
                new Color(0.05f, 0.06f, 0.11f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 234), new Vector2(312, 214));

            var logHeaderBar = CreateOrnatePanel(logPanel.transform, "LogHeaderBar",
                new Color(0.14f, 0.11f, 0.20f, 0.96f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -18), new Vector2(290, 28));
            var lhTxt = CreateText(logHeaderBar.transform, "LogHeader", "📜 BATTLE & SPELL CHRONICLE", 12,
                new Color(1f, 0.88f, 0.48f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(276, 24));
            AddTextShadow(lhTxt);

            ui.combatLogText = CreateText(logPanel.transform, "CombatLogText", "Match started with 500 HP and 10,000 tactical cards...", 11,
                new Color(0.90f, 0.94f, 0.98f), TextAnchor.UpperLeft, new Vector2(0, -14), new Vector2(282, 160));
            ui.combatLogText.horizontalOverflow = HorizontalWrapMode.Wrap;
            ui.combatLogText.verticalOverflow = VerticalWrapMode.Truncate;

            // 2. Field Command Arsenal (3 Action Buttons)
            var arsenalPanel = CreateOrnatePanel(rightWing.transform, "CommandArsenalPanel",
                new Color(0.06f, 0.08f, 0.14f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 26), new Vector2(312, 182));

            var arsTitle = CreateText(arsenalPanel.transform, "ArsenalHeader", "⚔️ FIELD COMMAND ORDERS ⚔️", 12,
                new Color(1f, 0.88f, 0.52f), TextAnchor.UpperCenter, new Vector2(0, 70), new Vector2(280, 20));
            AddTextShadow(arsTitle);

            ui.attackButton = CreateBeveledButton(arsenalPanel.transform, "AttackBtn", "⚔️ All-Out Monster Assault",
                new Color(0.82f, 0.18f, 0.22f, 0.98f), Color.white,
                new Vector2(0, 26), new Vector2(286, 40), 14,
                () => ui.SendMessage("OnAttackClicked"));

            ui.playCardButton = CreateBeveledButton(arsenalPanel.transform, "PlayCardBtn", "✨ Activate Card / Spell",
                new Color(0.56f, 0.20f, 0.82f, 0.98f), Color.white,
                new Vector2(0, -18), new Vector2(286, 40), 14,
                () => ui.SendMessage("OnPlayCardClicked"));

            ui.endTurnButton = CreateBeveledButton(arsenalPanel.transform, "EndTurnBtn", "🔁 End Tactical Turn",
                new Color(0.16f, 0.48f, 0.86f, 0.98f), new Color(1f, 0.96f, 0.72f),
                new Vector2(0, -64), new Vector2(286, 44), 15,
                () => ui.SendMessage("OnEndTurnClicked"));

            // 3. Elemental Weather Altar at Bottom of Right Wing
            var envAltar = CreateOrnatePanel(rightWing.transform, "EnvironmentAltarPanel",
                new Color(0.05f, 0.09f, 0.16f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -226), new Vector2(312, 296));
            ui.envAltarPanel = envAltar;

            var altarHeader = CreateOrnatePanel(envAltar.transform, "AltarHeaderBar",
                new Color(0.08f, 0.18f, 0.30f, 0.96f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -18), new Vector2(290, 28));
            var altTitle = CreateText(altarHeader.transform, "AltarTitle", "🏛️ ELEMENTAL WEATHER ALTAR (12)", 12,
                new Color(0.85f, 0.95f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(276, 24));
            AddTextShadow(altTitle);

            var envOrb = CreateMedallion(envAltar.transform, "EnvOrbMedallion",
                new Color(0.14f, 0.38f, 0.62f, 0.98f), new Vector2(0, 74), new Vector2(62, 62), "☀️", 28);
            ui.envIconText = envOrb.transform.Find("MedallionText").GetComponent<Text>();

            ui.envNameText = CreateText(envAltar.transform, "EnvName", "Weather: Stable", 14,
                new Color(1f, 0.90f, 0.45f), TextAnchor.MiddleCenter, new Vector2(0, 28), new Vector2(288, 20));
            AddTextShadow(ui.envNameText);

            ui.envLevelText = CreateText(envAltar.transform, "EnvLevel", "Natural (Neutral)", 11,
                new Color(0.68f, 0.88f, 1f), TextAnchor.MiddleCenter, new Vector2(0, 8), new Vector2(288, 18));

            ui.envDurationText = CreateText(envAltar.transform, "EnvDuration", "⏳ Permanent", 11,
                new Color(0.92f, 0.92f, 0.96f), TextAnchor.MiddleCenter, new Vector2(0, -10), new Vector2(288, 18));

            var buffPlaque = CreateOrnatePanel(envAltar.transform, "EnvBuffPlaque",
                new Color(0.05f, 0.18f, 0.11f, 0.92f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -38), new Vector2(288, 24));
            ui.envBuffText = CreateText(buffPlaque.transform, "EnvBuff", "• No Active Buffs", 10,
                new Color(0.32f, 0.98f, 0.52f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(276, 20));

            var debuffPlaque = CreateOrnatePanel(envAltar.transform, "EnvDebuffPlaque",
                new Color(0.20f, 0.06f, 0.09f, 0.92f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -66), new Vector2(288, 24));
            ui.envDebuffText = CreateText(debuffPlaque.transform, "EnvDebuff", "• All Elements Balanced", 10,
                new Color(1f, 0.48f, 0.48f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(276, 20));

            ui.testCycleEnvButton = CreateBeveledButton(envAltar.transform, "TestCycleEnvBtn", "🔄 Weather (12)",
                new Color(0.14f, 0.46f, 0.72f, 0.98f), Color.white,
                new Vector2(-72, -112), new Vector2(138, 34), 12,
                () => { if (BattleUIManager.Instance != null) BattleUIManager.Instance.OnTestCycleEnvironmentClicked(); });

            ui.testCycleLevelButton = CreateBeveledButton(envAltar.transform, "TestCycleLevelBtn", "⭐ Upgrade (1-3)",
                new Color(0.62f, 0.40f, 0.14f, 0.98f), new Color(1f, 0.95f, 0.65f),
                new Vector2(72, -112), new Vector2(138, 34), 12,
                () => { if (BattleUIManager.Instance != null) BattleUIManager.Instance.OnTestCycleEnvLevelClicked(); });

            // 11. Epic Game Over Modal
            BuildGameOverModal(canvas, ui);
        }

        private static void BuildCornerBastionMedallion(Transform parent, string name, Vector2 anchor, Vector2 pos, string symbol)
        {
            var med = CreateMedallion(parent, name, new Color(0.16f, 0.12f, 0.08f, 0.98f), pos, new Vector2(36, 36), symbol, 15);
            var rt = med.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
            med.GetComponent<Image>().raycastTarget = false;
        }

        private static void BuildGameOverModal(Transform canvas, BattleUIManager ui)
        {
            var overlay = CreatePanel(canvas, "GameOverModalOverlay", new Color(0.02f, 0.03f, 0.06f, 0.92f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var modalBox = CreateOrnatePanel(overlay.transform, "ModalBox", new Color(0.08f, 0.10f, 0.16f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680, 560));

            var modalOutline = modalBox.GetComponent<Outline>();
            if (modalOutline == null) modalOutline = modalBox.AddComponent<Outline>();
            modalOutline.effectColor = new Color(1f, 0.85f, 0.25f, 0.95f);
            modalOutline.effectDistance = new Vector2(3, 3);
            ui.gameOverModalOutline = modalOutline;

            ui.gameOverTitleText = CreateText(modalBox.transform, "TitleText", "🏆 LEGENDARY VICTORY!", 28,
                new Color(1f, 0.88f, 0.35f), TextAnchor.UpperCenter, new Vector2(0, 220), new Vector2(640, 42));
            AddTextShadow(ui.gameOverTitleText);

            ui.gameOverSubtitleText = CreateText(modalBox.transform, "SubtitleText", "You crushed the enemy warlord and claimed supreme dominion over the arena!", 15,
                new Color(0.9f, 0.95f, 1f), TextAnchor.UpperCenter, new Vector2(0, 175), new Vector2(640, 30));

            var statsBox = CreateOrnatePanel(modalBox.transform, "StatsBox", new Color(0.05f, 0.06f, 0.11f, 0.94f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(600, 240));

            CreateText(statsBox.transform, "StatsHeader", "📊 BATTLE SUMMARY & STATISTICS", 15,
                new Color(0.95f, 0.85f, 0.5f), TextAnchor.UpperCenter, new Vector2(0, 95), new Vector2(560, 24));

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
            Vector2 pos = isPlayerSide ? new Vector2(0f, 10f) : new Vector2(0f, -10f);

            var citadel = CreateOrnatePanel(leftWingParent, $"{prefix}LeaderCrest",
                isPlayerSide ? new Color(0.05f, 0.08f, 0.15f, 0.96f) : new Color(0.14f, 0.06f, 0.09f, 0.96f),
                anchor, anchor, pos, new Vector2(336, 176));
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
                    ? new Color(0.82f, 0.88f, 0.96f, 0.96f)
                    : new Color(0.88f, 0.72f, 0.76f, 0.96f);
            }

            Color rimColor = isPlayerSide
                ? new Color(1f, 0.84f, 0.36f, 0.95f)
                : new Color(0.95f, 0.40f, 0.38f, 0.95f);
            CreateHollowOrnatePanel(citadel.transform, $"{prefix}CitadelOrnateRim",
                rimColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // 1. Portrait Frame (Left inside Citadel: 100 x 100)
            var portraitFrame = CreateOrnatePanel(citadel.transform, $"{prefix}PortraitFrame",
                new Color(0.03f, 0.04f, 0.06f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-104, 0), new Vector2(100, 100));

            var portraitBorder = CreateHollowOrnatePanel(portraitFrame.transform, $"{prefix}PortraitBorder",
                rimColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            portraitBorder.GetComponent<Image>().raycastTarget = false;

            var avatarObj = new GameObject($"{prefix}AvatarImage");
            avatarObj.transform.SetParent(portraitFrame.transform, false);
            var rectAv = avatarObj.AddComponent<RectTransform>();
            rectAv.anchorMin = Vector2.zero;
            rectAv.anchorMax = Vector2.one;
            rectAv.sizeDelta = new Vector2(-8, -8);
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
                avImg.color = isPlayerSide ? new Color(0.2f, 0.4f, 0.8f, 1f) : new Color(0.8f, 0.2f, 0.2f, 1f);
            }

            if (isPlayerSide) ui.playerAvatarImage = avImg;
            else ui.enemyAvatarImage = avImg;

            // Corner Crest Badge on Avatar
            CreateMedallion(portraitFrame.transform, $"{prefix}CrestBadge",
                isPlayerSide ? new Color(0.95f, 0.78f, 0.22f, 1f) : new Color(0.85f, 0.22f, 0.26f, 1f),
                new Vector2(36, -36), new Vector2(28, 28), isPlayerSide ? "👑" : "💀", 14);

            // 2. Identity, Health Bar & Resource Column (Right inside Citadel: width 196)
            string titleStr = isPlayerSide ? "👑 SUPREME COMMANDER" : "💀 SHADOW WARLORD";
            Color titleColor = isPlayerSide ? new Color(1f, 0.92f, 0.48f) : new Color(1f, 0.84f, 0.84f);
            var titleTxt = CreateText(citadel.transform, $"{prefix}LeaderTitle", titleStr, 13,
                titleColor, TextAnchor.MiddleCenter, new Vector2(56, 48), new Vector2(194, 26));
            titleTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
            titleTxt.resizeTextForBestFit = true;
            titleTxt.resizeTextMinSize = 10;
            titleTxt.resizeTextMaxSize = 13;
            AddTextShadow(titleTxt);

            Color hpFillColor = isPlayerSide ? new Color(0.18f, 0.88f, 0.36f) : new Color(0.92f, 0.18f, 0.24f);
            var hpSlider = CreateSlider(citadel.transform, $"{prefix}HpBar",
                new Color(0.06f, 0.08f, 0.08f, 0.98f), hpFillColor,
                new Vector2(56, 10), new Vector2(194, 24));

            var hpTxt = CreateText(citadel.transform, $"{prefix}HpText", "❤️ 500 / 500 HP", 13,
                Color.white, TextAnchor.MiddleCenter, new Vector2(56, 10), new Vector2(194, 24));
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
                isPlayerSide ? new Color(0.04f, 0.11f, 0.22f, 0.95f) : new Color(0.18f, 0.08f, 0.14f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(56, -36), new Vector2(194, 38));

            if (isPlayerSide)
            {
                ui.playerPowerText = CreateText(statusBox.transform, "PlayerPowerText", "[ 💎 ]  Power: 1  |  Mana: 1 (∞)", 12,
                    new Color(0.42f, 0.94f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(186, 34));
                ui.playerPowerText.horizontalOverflow = HorizontalWrapMode.Wrap;
                ui.playerPowerText.resizeTextForBestFit = true;
                ui.playerPowerText.resizeTextMinSize = 9;
                ui.playerPowerText.resizeTextMaxSize = 12;
                AddTextShadow(ui.playerPowerText);
            }
            else
            {
                ui.enemyHandCountText = CreateText(statusBox.transform, "EnemyHandLabel", "🎴 Enemy Hand: 6 Hidden Cards", 12,
                    new Color(1f, 0.88f, 0.55f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(186, 34));
                ui.enemyHandCountText.horizontalOverflow = HorizontalWrapMode.Wrap;
                ui.enemyHandCountText.resizeTextForBestFit = true;
                ui.enemyHandCountText.resizeTextMinSize = 9;
                ui.enemyHandCountText.resizeTextMaxSize = 12;
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
                dockImg.color = Color.white;
            }
            else
            {
                dockImg.color = isPlayerSide
                    ? new Color(0.06f, 0.08f, 0.13f, 0.98f)
                    : new Color(0.12f, 0.06f, 0.09f, 0.98f);

                Color borderRimColor = isPlayerSide
                    ? new Color(1f, 0.84f, 0.36f, 0.95f)
                    : new Color(0.95f, 0.40f, 0.38f, 0.92f);
                CreateHollowOrnatePanel(dock.transform, $"{prefix}DockOrnateBorder", borderRimColor,
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }

            // 2. 6 Subtle Recessed Card Bays across the oil-painted leather center (aligned directly with card pitch 168px)
            float[] dockX = { -420f, -252f, -84f, 84f, 252f, 420f };
            for (int k = 0; k < 6; k++)
            {
                var bay = CreatePanel(dock.transform, $"{prefix}CardBay_{k}",
                    new Color(0.02f, 0.02f, 0.03f, 0.24f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(dockX[k], 0), new Vector2(138, 168));
                bay.GetComponent<Image>().raycastTarget = false;

                var bayOutline = bay.AddComponent<Outline>();
                bayOutline.effectColor = new Color(0.70f, 0.56f, 0.28f, 0.26f);
                bayOutline.effectDistance = new Vector2(1.2f, 1.2f);

                var glyph = CreateText(bay.transform, "BayGlyph", "❖", 24,
                    new Color(0.78f, 0.66f, 0.38f, 0.14f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(80, 80));
                glyph.raycastTarget = false;
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

                var btn = slotObj.AddComponent<Button>();
                int capturedIndex = i;
                bool capturedSide = isPlayerSide;
                btn.onClick.AddListener(() => ui.OnSlotClicked(capturedIndex, capturedSide));

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
                        px = new Color(0.05f, 0.04f, 0.03f, 0.98f);
                    }
                    else if (isCornerMedallion)
                    {
                        float cDist = Mathf.Max(Mathf.Abs(dx - 10), Mathf.Abs(dy - 10));
                        if (cDist <= 3.5f)
                        {
                            px = new Color(0.55f, 0.92f, 1f, 1f);
                        }
                        else if (cDist <= 7.5f)
                        {
                            float bevel = (y > size / 2 || x < size / 2) ? 1.15f : 0.85f;
                            px = new Color(Mathf.Clamp01(0.95f * bevel), Mathf.Clamp01(0.78f * bevel), Mathf.Clamp01(0.32f * bevel), 1f);
                        }
                        else
                        {
                            px = new Color(0.38f, 0.28f, 0.12f, 1f);
                        }
                    }
                    else if (dEdge >= 1 && dEdge <= 4)
                    {
                        bool topOrLeft = (y > size - 6) || (x < 6);
                        px = topOrLeft
                            ? new Color(1f, 0.88f, 0.45f, 1f)
                            : new Color(0.68f, 0.50f, 0.18f, 1f);
                    }
                    else if (dEdge >= 5 && dEdge <= 8)
                    {
                        px = new Color(0.14f, 0.13f, 0.18f, 0.98f);
                    }
                    else if (dEdge >= 9 && dEdge <= 10)
                    {
                        px = new Color(0.88f, 0.72f, 0.30f, 0.95f);
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
                        px = new Color(0.08f, 0.06f, 0.04f, 0.95f);
                    }
                    else if (isCorner)
                    {
                        float cDist = Mathf.Max(Mathf.Abs(dx - 9), Mathf.Abs(dy - 9));
                        if (cDist <= 3f)
                            px = new Color(1f, 0.94f, 0.65f, 1f);
                        else if (cDist <= 7f)
                            px = new Color(0.95f, 0.78f, 0.34f, 1f);
                        else
                            px = new Color(0.35f, 0.25f, 0.10f, 0.95f);
                    }
                    else if (dEdge >= 1 && dEdge <= 4)
                    {
                        bool hi = (y > size - 6) || (x < 6);
                        px = hi ? new Color(1f, 0.90f, 0.50f, 1f) : new Color(0.72f, 0.54f, 0.22f, 1f);
                    }
                    else if (dEdge >= 5 && dEdge <= 7)
                    {
                        px = new Color(0.12f, 0.10f, 0.14f, 0.92f);
                    }
                    else if (dEdge == 8 || dEdge == 9)
                    {
                        px = new Color(0.92f, 0.76f, 0.35f, 0.90f);
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
                        px = new Color(0.06f, 0.05f, 0.04f, 0.98f);
                    }
                    else if (isCorner)
                    {
                        px = new Color(1f, 0.86f, 0.42f, 1f);
                    }
                    else if (dEdge >= 1 && dEdge <= 3)
                    {
                        px = (ny > 0.5f)
                            ? new Color(1f, 0.90f, 0.48f, 1f)
                            : new Color(0.75f, 0.56f, 0.22f, 1f);
                    }
                    else if (dEdge == 4 || dEdge == 5)
                    {
                        px = new Color(0.18f, 0.16f, 0.22f, 0.98f);
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
                        px = new Color(1f, 0.86f, 0.42f, alpha);
                    }
                    else if (nr > 0.72f)
                    {
                        px = new Color(0.16f, 0.14f, 0.22f, alpha);
                    }
                    else if (nr > 0.65f)
                    {
                        px = new Color(0.92f, 0.76f, 0.34f, alpha);
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

            Color primaryRim = isEnemy ? new Color(0.95f, 0.32f, 0.30f, 0.92f) : new Color(0.32f, 0.85f, 1f, 0.92f);
            Color goldAccent = isEnemy ? new Color(0.88f, 0.52f, 0.28f, 0.95f) : new Color(1f, 0.84f, 0.36f, 0.95f);
            Color plateFill = isEnemy ? new Color(0.12f, 0.05f, 0.08f, 0.18f) : new Color(0.05f, 0.10f, 0.18f, 0.18f);

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

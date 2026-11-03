using System;
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
            if (FindObjectOfType<GameManager>() != null)
            {
                Debug.Log("GameManager موجود بالفعل في المشهد.");
                return;
            }

            Debug.Log("🚀 جاري بناء ساحة المعركة التكتيكية 3D المحدثة بالكامل مع محراب البيئة وبطاقات السحر...");

            // 1. الكاميرا
            EnsureCamera();

            // 2. نظام الإدخال والماوس
            EnsureEventSystem();

            // 3. الكانفاس والواجهة الرسومية
            var canvas = CreateMainCanvas();

            // 4. بناء كائنات اللعبة المنطقية
            var gameManagerObj = new GameObject("GameManager");
            var gm = gameManagerObj.AddComponent<GameManager>();
            var combatSystem = gameManagerObj.AddComponent<CombatSystem>();

            var playerObj = new GameObject("HumanPlayer");
            playerObj.transform.SetParent(gameManagerObj.transform);
            var humanPlayer = playerObj.AddComponent<PlayerState>();
            humanPlayer.playerName = "أنت (البطل)";

            var aiObj = new GameObject("AIOpponent");
            aiObj.transform.SetParent(gameManagerObj.transform);
            var aiPlayer = aiObj.AddComponent<PlayerState>();
            aiPlayer.playerName = "الخصم (الذكاء الاصطناعي)";
            var aiBrain = aiObj.AddComponent<AIOpponent>();
            aiBrain.aiPlayer = aiPlayer;
            aiBrain.humanPlayer = humanPlayer;

            gm.humanPlayer = humanPlayer;
            gm.aiPlayer = aiPlayer;
            gm.aiOpponent = aiBrain;

            // 5. بناء الخانات الـ 6 لكل لاعب
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

            // 6. بناء الواجهة الرسومية التفاعلية على الكانفاس
            var uiManagerObj = new GameObject("BattleUIManager");
            uiManagerObj.transform.SetParent(canvas.transform);
            var ui = uiManagerObj.AddComponent<BattleUIManager>();

            BuildVisualArena(canvas.transform, ui, humanPlayer, aiPlayer);

            // 7. ربط قاعدة البيانات وبدء المباراة
            var jsonAsset = Resources.Load<TextAsset>("monsters_150");
            gm.monstersJsonFile = jsonAsset;

            Debug.Log("🎉 اكتمل بناء ساحة المعركة التكتيكية الفاخرة بنجاح! اللعبة جاهزة للقتال.");
        }

        private static void EnsureCamera()
        {
            if (Camera.main == null)
            {
                var camObj = new GameObject("Main Camera");
                var cam = camObj.AddComponent<Camera>();
                camObj.tag = "MainCamera";
                camObj.transform.position = new Vector3(0, 0, -10);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.06f, 0.07f, 0.11f);
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
            var canvasObj = new GameObject("BattleCanvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            scaler.dynamicPixelsPerUnit = 2.5f;

            canvasObj.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static void BuildVisualArena(Transform canvas, BattleUIManager ui, PlayerState human, PlayerState ai)
        {
            Font font = GetFont();

            // 0. خلفية أرضية الملعب الملحمية (Arena Ground Texture)
            var bgObj = new GameObject("ArenaBackground");
            bgObj.transform.SetParent(canvas, false);
            bgObj.transform.SetAsFirstSibling();
            var bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImg = bgObj.AddComponent<Image>();
            var arenaTex = Resources.Load<Texture2D>("arena_ground");
            if (arenaTex != null)
            {
                bgImg.sprite = Sprite.Create(arenaTex, new Rect(0, 0, arenaTex.width, arenaTex.height), new Vector2(0.5f, 0.5f));
                bgImg.color = Color.white;
            }
            else
            {
                bgImg.color = new Color(0.08f, 0.09f, 0.13f);
            }

            // 0.1 طبقة الغلاف الجوي المتغير حسب المناخ والبيئة (Environment Atmosphere Overlay)
            var atmosObj = new GameObject("EnvironmentAtmosphereOverlay");
            atmosObj.transform.SetParent(canvas, false);
            atmosObj.transform.SetSiblingIndex(1);
            var atmosRect = atmosObj.AddComponent<RectTransform>();
            atmosRect.anchorMin = Vector2.zero;
            atmosRect.anchorMax = Vector2.one;
            atmosRect.sizeDelta = Vector2.zero;
            var atmosImg = atmosObj.AddComponent<Image>();
            atmosImg.color = new Color(0.08f, 0.09f, 0.13f, 0.25f);
            atmosImg.raycastTarget = false;
            ui.envAtmosphereOverlay = atmosImg;

            // 0.2 تأسيس محرك الغلاف الجوي الحربي والمناخي المتحرك (Living Battlefield & Weather Engine)
            BattlefieldAtmosphereEngine.EnsureInstance(canvas, bgImg, atmosImg);

            // 1. وسام قائد الخصم (Top-Left Leader Crest)
            var enemyCrest = CreatePanel(canvas, "EnemyLeaderCrest", new Color(0.12f, 0.10f, 0.16f, 0.94f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(195, -75), new Vector2(330, 95));
            var eCrestOutline = enemyCrest.AddComponent<Outline>();
            eCrestOutline.effectColor = new Color(0.85f, 0.25f, 0.3f, 0.85f);
            eCrestOutline.effectDistance = new Vector2(2, 2);

            CreateText(enemyCrest.transform, "EnemyLeaderTitle", "💀 قائد الظلال (الخصم)", 19,
                new Color(0.95f, 0.75f, 0.75f), TextAnchor.UpperCenter, new Vector2(0, 22), new Vector2(310, 28));

            ui.enemyHpSlider = CreateSlider(enemyCrest.transform, "EnemyHpBar",
                new Color(0.18f, 0.14f, 0.2f, 0.95f), new Color(0.9f, 0.2f, 0.25f),
                new Vector2(0, -10), new Vector2(290, 16));

            ui.enemyHpText = CreateText(enemyCrest.transform, "EnemyHpText", "200 / 200 HP", 15,
                Color.white, TextAnchor.MiddleCenter, new Vector2(0, -10), new Vector2(290, 20));

            // 2. يد الخصم المستترة (Top-Center Enemy Hand Panel)
            var enemyHandPanel = CreatePanel(canvas, "EnemyHandPanel", new Color(0.09f, 0.08f, 0.14f, 0.85f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -52), new Vector2(740, 85));
            var ehOutline = enemyHandPanel.AddComponent<Outline>();
            ehOutline.effectColor = new Color(0.65f, 0.48f, 0.22f, 0.8f);
            ehOutline.effectDistance = new Vector2(1.5f, 1.5f);

            ui.enemyHandCountText = CreateText(enemyHandPanel.transform, "EnemyHandLabel", "🎴 كروت يد الخصم: 6 بطاقات مستترة", 15,
                new Color(0.92f, 0.82f, 0.55f), TextAnchor.UpperCenter, new Vector2(0, 26), new Vector2(500, 24));

            var enemyCardsContainer = new GameObject("EnemyCardsContainer");
            enemyCardsContainer.transform.SetParent(enemyHandPanel.transform, false);
            var rectEC = enemyCardsContainer.AddComponent<RectTransform>();
            rectEC.sizeDelta = new Vector2(690, 52);
            rectEC.anchoredPosition = new Vector2(0, -12);

            var ecLayout = enemyCardsContainer.AddComponent<HorizontalLayoutGroup>();
            ecLayout.spacing = 10;
            ecLayout.childAlignment = TextAnchor.MiddleCenter;
            ecLayout.childControlWidth = false;
            ecLayout.childControlHeight = false;

            ui.enemyHandContainer = enemyCardsContainer.transform;

            // 3. زر الخروج وإنهاء اللعبة في أعلى يمين الشاشة (Top-Right Exit Button)
            var quitBtn = CreateAnchorButton(canvas, "QuitGameBtn", "❌ خروج",
                new Color(0.75f, 0.15f, 0.2f, 0.95f), Color.white,
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-70, -45), new Vector2(105, 42),
                () => {
                    if (BattleUIManager.Instance != null) BattleUIManager.Instance.OnQuitClicked();
                });
            var qOutline = quitBtn.gameObject.AddComponent<Outline>();
            qOutline.effectColor = new Color(1f, 0.4f, 0.4f, 0.8f);
            qOutline.effectDistance = new Vector2(1.5f, 1.5f);
            quitBtn.gameObject.AddComponent<QuitButtonHandler>();
            ui.quitButton = quitBtn;

            // 4. محراب المناخ والبيئة في أسفل يمين الشاشة (Bottom-Right Environment Altar)
            var envAltar = CreatePanel(canvas, "EnvironmentAltarPanel", new Color(0.08f, 0.09f, 0.15f, 0.92f),
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-160, 112), new Vector2(240, 210));
            var eaOutline = envAltar.AddComponent<Outline>();
            eaOutline.effectColor = new Color(0.35f, 0.75f, 1f, 0.85f);
            eaOutline.effectDistance = new Vector2(2, 2);
            ui.envAltarPanel = envAltar;

            CreateText(envAltar.transform, "AltarTitle", "🏛️ محراب المناخ", 14,
                new Color(0.85f, 0.92f, 1f), TextAnchor.UpperCenter, new Vector2(0, 88), new Vector2(220, 20));

            ui.envIconText = CreateText(envAltar.transform, "EnvIcon", "☀️", 28,
                Color.white, TextAnchor.MiddleCenter, new Vector2(0, 56), new Vector2(160, 36));

            ui.envNameText = CreateText(envAltar.transform, "EnvName", "المناخ: مستقر", 13,
                new Color(1f, 0.9f, 0.45f), TextAnchor.MiddleCenter, new Vector2(0, 28), new Vector2(220, 18));

            ui.envLevelText = CreateText(envAltar.transform, "EnvLevel", "طبيعي (محايد)", 11,
                new Color(0.7f, 0.8f, 0.9f), TextAnchor.MiddleCenter, new Vector2(0, 10), new Vector2(220, 16));

            ui.envDurationText = CreateText(envAltar.transform, "EnvDuration", "⏳ دائم", 11,
                new Color(0.9f, 0.9f, 0.95f), TextAnchor.MiddleCenter, new Vector2(0, -6), new Vector2(220, 16));

            ui.envBuffText = CreateText(envAltar.transform, "EnvBuff", "• لا توجد تعزيزات", 10,
                new Color(0.2f, 0.95f, 0.45f), TextAnchor.MiddleCenter, new Vector2(0, -22), new Vector2(220, 16));

            ui.envDebuffText = CreateText(envAltar.transform, "EnvDebuff", "• كافة العناصر متكافئة", 10,
                new Color(1f, 0.45f, 0.45f), TextAnchor.MiddleCenter, new Vector2(0, -38), new Vector2(220, 16));

            // أزرار تجربة واستعراض البيئات الـ 12 ومستوياتها الـ 3 داخل المحراب
            ui.testCycleEnvButton = CreateButton(envAltar.transform, "TestCycleEnvBtn", "🔄 المناخ (12)",
                new Color(0.16f, 0.45f, 0.68f, 0.95f), Color.white, new Vector2(-58, -74), new Vector2(108, 28),
                () => { if (BattleUIManager.Instance != null) BattleUIManager.Instance.OnTestCycleEnvironmentClicked(); });
            var tceTxt = ui.testCycleEnvButton.GetComponentInChildren<Text>();
            if (tceTxt != null) tceTxt.fontSize = 11;

            ui.testCycleLevelButton = CreateButton(envAltar.transform, "TestCycleLevelBtn", "⭐ ترقية (1-3)",
                new Color(0.55f, 0.36f, 0.14f, 0.95f), new Color(1f, 0.95f, 0.65f), new Vector2(58, -74), new Vector2(108, 28),
                () => { if (BattleUIManager.Instance != null) BattleUIManager.Instance.OnTestCycleEnvLevelClicked(); });
            var tclTxt = ui.testCycleLevelButton.GetComponentInChildren<Text>();
            if (tclTxt != null) tclTxt.fontSize = 11;

            // 5. مؤشر الدور العائم في منتصف الساحة (Central Floating Status Pill)
            var statusPill = CreatePanel(canvas, "StatusPill", new Color(0.08f, 0.10f, 0.16f, 0.85f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(440, 46));
            var spOutline = statusPill.AddComponent<Outline>();
            spOutline.effectColor = new Color(0.92f, 0.75f, 0.25f, 0.85f);
            spOutline.effectDistance = new Vector2(1.5f, 1.5f);

            ui.turnInfoText = CreateText(statusPill.transform, "TurnInfoText", "⚔️ دورك الآن (انشر أو هاجم)", 20,
                new Color(1f, 0.92f, 0.45f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(430, 42));

            // 6. خانات العدو الـ 6 (Runic Summoning Circles - Top Row Y: +145)
            BuildSlotsRow(canvas, ui, ai, false);

            // 7. خانات اللاعب الـ 6 (Runic Summoning Circles - Bottom Row Y: -140)
            BuildSlotsRow(canvas, ui, human, true);

            // 8. وسام قائدك والباور والمانا (Bottom-Left Leader Crest & Mana Crystals)
            var playerCrest = CreatePanel(canvas, "PlayerLeaderCrest", new Color(0.09f, 0.12f, 0.18f, 0.94f),
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(195, 140), new Vector2(330, 115));
            var pCrestOutline = playerCrest.AddComponent<Outline>();
            pCrestOutline.effectColor = new Color(0.95f, 0.8f, 0.3f, 0.9f);
            pCrestOutline.effectDistance = new Vector2(2, 2);

            CreateText(playerCrest.transform, "PlayerLeaderTitle", "👑 البطل (أنت)", 20,
                new Color(1f, 0.92f, 0.5f), TextAnchor.UpperCenter, new Vector2(0, 34), new Vector2(310, 28));

            ui.playerHpSlider = CreateSlider(playerCrest.transform, "PlayerHpBar",
                new Color(0.12f, 0.18f, 0.14f, 0.95f), new Color(0.2f, 0.9f, 0.35f),
                new Vector2(0, 6), new Vector2(290, 16));

            ui.playerHpText = CreateText(playerCrest.transform, "PlayerHpText", "200 / 200 HP", 15,
                Color.white, TextAnchor.MiddleCenter, new Vector2(0, 6), new Vector2(290, 20));

            ui.playerPowerText = CreateText(playerCrest.transform, "PlayerPowerText", "[ 💎 ]  الباور: 1/1  |  المانا: 1/8", 17,
                new Color(0.35f, 0.9f, 1f), TextAnchor.MiddleCenter, new Vector2(0, -28), new Vector2(310, 26));

            // 9. لوحة الأوامر وسجل المعركة على اليمين (Right Side Command Center & Combat Log)
            // 9.1 سجل المعركة العائم
            var logPanel = CreatePanel(canvas, "CombatLogPanel", new Color(0.08f, 0.09f, 0.14f, 0.85f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-175, 140), new Vector2(310, 185));
            var logOutline = logPanel.AddComponent<Outline>();
            logOutline.effectColor = new Color(0.65f, 0.5f, 0.25f, 0.75f);
            logOutline.effectDistance = new Vector2(1.5f, 1.5f);

            CreateText(logPanel.transform, "LogHeader", "📜 سجل المعركة والتعاويذ", 16,
                new Color(0.95f, 0.85f, 0.5f), TextAnchor.UpperCenter, new Vector2(0, 72), new Vector2(290, 24));

            ui.combatLogText = CreateText(logPanel.transform, "CombatLogText", "تبدأ المباراة بـ 1,000 HP وبطاقات سحر وبيئة...", 14,
                new Color(0.9f, 0.92f, 0.96f), TextAnchor.UpperLeft, new Vector2(0, -12), new Vector2(290, 135));

            // 9.2 أزرار الأوامر
            // زر هجوم الوحوش
            ui.attackButton = CreateAnchorButton(canvas, "AttackBtn", "⚔️ هجوم الوحوش",
                new Color(0.80f, 0.20f, 0.20f, 0.95f), Color.white,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-175, -35), new Vector2(210, 44),
                () => ui.SendMessage("OnAttackClicked"));
            var abOutline = ui.attackButton.gameObject.AddComponent<Outline>();
            abOutline.effectColor = new Color(1f, 0.45f, 0.45f, 0.8f);
            abOutline.effectDistance = new Vector2(1.5f, 1.5f);

            // زر تفعيل البطاقة المحددة (سحر أو بيئة)
            ui.playCardButton = CreateAnchorButton(canvas, "PlayCardBtn", "✨ تفعيل البطاقة",
                new Color(0.55f, 0.20f, 0.80f, 0.95f), Color.white,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-175, -92), new Vector2(210, 44),
                () => ui.SendMessage("OnPlayCardClicked"));
            var pcbOutline = ui.playCardButton.gameObject.AddComponent<Outline>();
            pcbOutline.effectColor = new Color(0.95f, 0.75f, 0.35f, 0.85f);
            pcbOutline.effectDistance = new Vector2(1.5f, 1.5f);

            // زر إنهاء الدور الفخم
            ui.endTurnButton = CreateAnchorButton(canvas, "EndTurnBtn", "🔁 إنهاء الدور",
                new Color(0.18f, 0.50f, 0.85f, 0.95f), Color.white,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-175, -165), new Vector2(210, 58),
                () => ui.SendMessage("OnEndTurnClicked"));
            var etOutline = ui.endTurnButton.gameObject.AddComponent<Outline>();
            etOutline.effectColor = new Color(0.95f, 0.85f, 0.3f, 0.95f);
            etOutline.effectDistance = new Vector2(2.5f, 2.5f);

            // 10. منطقة يد الكروت بالأسفل بتدرج زجاجي مفتوح (Bottom Player Hand)
            var handPanel = CreatePanel(canvas, "HandPanel", new Color(0.06f, 0.07f, 0.11f, 0.75f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 102), new Vector2(1160, 202));
            var hpOutline = handPanel.AddComponent<Outline>();
            hpOutline.effectColor = new Color(0.45f, 0.40f, 0.60f, 0.45f);
            hpOutline.effectDistance = new Vector2(1, 1);

            CreateText(handPanel.transform, "HandLabel", "🎴 يدك (اسحب الوحوش للساحة، وكليك يمين على أي كارت للتضحية به +1 مانا):", 14,
                new Color(0.85f, 0.88f, 0.95f), TextAnchor.UpperCenter, new Vector2(0, 88), new Vector2(850, 22));

            var handCardsContainer = new GameObject("CardsContainer");
            handCardsContainer.transform.SetParent(handPanel.transform, false);
            var rectH = handCardsContainer.AddComponent<RectTransform>();
            rectH.sizeDelta = new Vector2(1140, 186);
            rectH.anchoredPosition = new Vector2(0, -6);

            var hLayout = handCardsContainer.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 14;
            hLayout.childAlignment = TextAnchor.MiddleCenter;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;

            ui.playerHandContainer = handCardsContainer.transform;

            // 10.1 بطاقة الوحش الكاملة التفصيلية بالفريم على يسار الشاشة (Left-Side Full Detailed Monster Card)
            var detailsPanel = CreatePanel(canvas, "CardDetailsInspectorPanel", new Color(0.06f, 0.07f, 0.11f, 0.98f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(175, 12), new Vector2(275, 415));
            var dpOutline = detailsPanel.AddComponent<Outline>();
            dpOutline.effectColor = new Color(0.95f, 0.82f, 0.35f, 0.95f);
            dpOutline.effectDistance = new Vector2(2.5f, 2.5f);

            ui.cardDetailsPanel = detailsPanel;

            // 1. الشريط العلوي: اسم الوحش
            ui.detailNameText = CreateText(detailsPanel.transform, "DetailName", "اختر وحشاً أو بطاقة", 17,
                new Color(1f, 0.90f, 0.45f), TextAnchor.MiddleRight, new Vector2(-22, 185), new Vector2(210, 26));

            // دائرة المانا أعلى اليمين داخل الكارت الكامل
            var fullCostCircle = CreateCirclePanel(detailsPanel.transform, "FullCostCircle", new Color(0.10f, 0.28f, 0.65f, 0.96f),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22, -22), new Vector2(34, 34));
            var fccOutline = fullCostCircle.AddComponent<Outline>();
            fccOutline.effectColor = new Color(0.40f, 0.90f, 1f, 1f);
            fccOutline.effectDistance = new Vector2(1.5f, 1.5f);
            ui.detailCostText = CreateText(fullCostCircle.transform, "CostText", "-", 17,
                new Color(1f, 0.95f, 0.70f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(34, 34));

            // 2. نافذة رسمة الوحش الفنية الكبيرة
            var artWindow = CreatePanel(detailsPanel.transform, "ArtWindow", new Color(0.02f, 0.03f, 0.05f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 72), new Vector2(255, 175));
            var awOutline = artWindow.AddComponent<Outline>();
            awOutline.effectColor = new Color(0.35f, 0.40f, 0.55f, 0.65f);
            awOutline.effectDistance = new Vector2(1.5f, 1.5f);

            var artObj = new GameObject("FullCardArtImage");
            artObj.transform.SetParent(artWindow.transform, false);
            var rectArt = artObj.AddComponent<RectTransform>();
            rectArt.anchorMin = Vector2.zero;
            rectArt.anchorMax = Vector2.one;
            rectArt.sizeDelta = Vector2.zero;
            ui.detailCardArt = artObj.AddComponent<Image>();
            ui.detailCardArt.preserveAspect = false;

            // 3. شريط الرتبة والتصنيف والعنصر
            var metaBar = CreatePanel(detailsPanel.transform, "MetaBar", new Color(0.09f, 0.12f, 0.18f, 0.92f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -28), new Vector2(255, 24));
            ui.detailMetaText = CreateText(metaBar.transform, "DetailMeta", "اضغط على أي وحش في الساحة أو بطاقة في يدك", 12,
                new Color(0.40f, 0.85f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(250, 24));

            // 4. صندوق وصف القدرات والمهارات التكتيكية
            var descBox = CreatePanel(detailsPanel.transform, "DescBox", new Color(0.04f, 0.05f, 0.08f, 0.90f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(255, 85));
            var dbOutline = descBox.AddComponent<Outline>();
            dbOutline.effectColor = new Color(0.35f, 0.30f, 0.45f, 0.45f);
            dbOutline.effectDistance = new Vector2(1, 1);

            ui.detailDescText = CreateText(descBox.transform, "DetailDesc", "ستظهر هنا كافة بيانات الوحش أو التعويذة وقدراته التكتيكية بنص عربي واضح وكبير.", 13,
                new Color(0.90f, 0.92f, 0.96f), TextAnchor.UpperRight, new Vector2(-6, -6), new Vector2(243, 73));
            ui.detailDescText.horizontalOverflow = HorizontalWrapMode.Wrap;
            ui.detailDescText.verticalOverflow = VerticalWrapMode.Truncate;

            // 5. شارات الهجوم والدفاع السفلية
            // شارة الهجوم (أحمر)
            var atkBadge = CreatePanel(detailsPanel.transform, "FullAtkBadge", new Color(0.70f, 0.12f, 0.16f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-95, -165), new Vector2(58, 30));
            var fabOutline = atkBadge.AddComponent<Outline>();
            fabOutline.effectColor = new Color(1f, 0.45f, 0.35f, 1f);
            fabOutline.effectDistance = new Vector2(1.5f, 1.5f);
            ui.detailAtkText = CreateText(atkBadge.transform, "AtkText", "⚔️ -", 15,
                Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(58, 30));

            // شارة الدفاع (أخضر)
            var defBadge = CreatePanel(detailsPanel.transform, "FullDefBadge", new Color(0.10f, 0.52f, 0.22f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(95, -165), new Vector2(58, 30));
            var fdbOutline = defBadge.AddComponent<Outline>();
            fdbOutline.effectColor = new Color(0.35f, 1f, 0.45f, 1f);
            fdbOutline.effectDistance = new Vector2(1.5f, 1.5f);
            ui.detailDefText = CreateText(defBadge.transform, "DefText", "🛡️ -", 15,
                Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(58, 30));

            // شارة الحالة بالوسط
            ui.detailStatusText = CreateText(detailsPanel.transform, "DetailStatus", "⚔️ المعركة بانتظار أوامرك.", 12,
                new Color(0.3f, 1f, 0.5f), TextAnchor.MiddleCenter, new Vector2(0, -165), new Vector2(120, 26));

            // إبقاء detailStatsText لأي كود منطقي يعتمد عليه
            ui.detailStatsText = ui.detailStatusText;

            // 11. نافذة خاتمة ونهاية المعركة الملحمية (Game Over Modal)
            BuildGameOverModal(canvas, ui);
        }

        private static void BuildGameOverModal(Transform canvas, BattleUIManager ui)
        {
            // غطاء معتم يغطي الشاشة بالكامل ويعزل المعركة
            var overlay = CreatePanel(canvas, "GameOverModalOverlay", new Color(0.03f, 0.04f, 0.07f, 0.90f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // الصندوق المركزي لخاتمة المعركة
            var modalBox = CreatePanel(overlay.transform, "ModalBox", new Color(0.09f, 0.10f, 0.16f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680, 560));

            var modalOutline = modalBox.AddComponent<Outline>();
            modalOutline.effectColor = new Color(1f, 0.85f, 0.25f, 0.95f);
            modalOutline.effectDistance = new Vector2(3, 3);
            ui.gameOverModalOutline = modalOutline;

            // العنوان الرئيسي
            ui.gameOverTitleText = CreateText(modalBox.transform, "TitleText", "🏆 نصر أسطوري مؤزر!", 28,
                new Color(1f, 0.88f, 0.35f), TextAnchor.UpperCenter, new Vector2(0, 220), new Vector2(640, 42));

            // العنوان الفرعي
            ui.gameOverSubtitleText = CreateText(modalBox.transform, "SubtitleText", "لقد سحقت قائد العدو وفرضت سيطرتك المطلقة على الساحة!", 15,
                new Color(0.9f, 0.95f, 1f), TextAnchor.UpperCenter, new Vector2(0, 175), new Vector2(640, 30));

            // حاوية عرض الإحصائيات
            var statsBox = CreatePanel(modalBox.transform, "StatsBox", new Color(0.06f, 0.07f, 0.11f, 0.90f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(600, 240));

            var statsOutline = statsBox.AddComponent<Outline>();
            statsOutline.effectColor = new Color(0.45f, 0.40f, 0.65f, 0.65f);
            statsOutline.effectDistance = new Vector2(1.5f, 1.5f);

            CreateText(statsBox.transform, "StatsHeader", "📊 تفاصيل وسجل المعركة", 15,
                new Color(0.95f, 0.85f, 0.5f), TextAnchor.UpperCenter, new Vector2(0, 95), new Vector2(560, 24));

            ui.gameOverStatsText = CreateText(statsBox.transform, "StatsContent", "جاري رصد إحصائيات المعركة...", 15,
                Color.white, TextAnchor.MiddleCenter, new Vector2(0, -12), new Vector2(560, 180));

            // أزرار التحكم بالأسفل
            // 1. زر معركة جديدة
            ui.restartGameButton = CreateButton(modalBox.transform, "RestartBtn", "🔁 معركة جديدة (Play Again)",
                new Color(0.18f, 0.65f, 0.35f, 0.98f), Color.white,
                new Vector2(-155, -220), new Vector2(270, 52),
                () => ui.SendMessage("OnRestartClicked"));
            var rbOutline = ui.restartGameButton.gameObject.AddComponent<Outline>();
            rbOutline.effectColor = new Color(0.4f, 1f, 0.6f, 0.9f);
            rbOutline.effectDistance = new Vector2(2, 2);

            // 2. زر الخروج
            ui.gameOverQuitButton = CreateButton(modalBox.transform, "QuitBtn", "🚪 خروج من اللعبة",
                new Color(0.75f, 0.18f, 0.22f, 0.98f), Color.white,
                new Vector2(155, -220), new Vector2(240, 52),
                () => ui.SendMessage("OnQuitClicked"));
            var qbOutline = ui.gameOverQuitButton.gameObject.AddComponent<Outline>();
            qbOutline.effectColor = new Color(1f, 0.4f, 0.4f, 0.9f);
            qbOutline.effectDistance = new Vector2(2, 2);

            overlay.SetActive(false);
            ui.gameOverPanel = overlay;
        }

        private static void BuildSlotsRow(Transform canvas, BattleUIManager ui, PlayerState player, bool isPlayerSide)
        {
            // إحداثيات متطابقة بدقة مع الدوائر الحجرية مع تقريب المسافة الرأسية لإعطاء شعور بالاشتباك القريب والوضوح
            float posY = isPlayerSide ? -145f : 165f;
            float[] xCoords = { -426f, -255f, -86f, 86f, 255f, 426f };

            for (int i = 0; i < 6; i++)
            {
                float posX = xCoords[i];

                string slotLabel = i switch
                {
                    0 => "جناح أيسر 1",
                    1 => "جناح أيسر 2",
                    2 => "قلب الهجوم 1",
                    3 => "قلب الهجوم 2",
                    4 => "جناح أيمن 1",
                    5 => "جناح أيمن 2",
                    _ => $"خانة {i}"
                };

                Color runeColor;
                if (isPlayerSide)
                {
                    runeColor = (i == 2 || i == 3)
                        ? new Color(1f, 0.72f, 0.25f, 0.85f)
                        : new Color(0.30f, 0.85f, 1f, 0.80f);
                }
                else
                {
                    runeColor = (i == 2 || i == 3)
                        ? new Color(1f, 0.38f, 0.35f, 0.85f)
                        : new Color(0.75f, 0.45f, 1f, 0.80f);
                }

                var slotObj = CreatePanel(canvas, $"{(isPlayerSide ? "P" : "E")}_SlotView_{i}",
                    new Color(0.06f, 0.08f, 0.14f, 0.22f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(posX, posY), new Vector2(156, 198));

                var outline = slotObj.AddComponent<Outline>();
                outline.effectColor = runeColor;
                outline.effectDistance = new Vector2(2, 2);

                var slotView = slotObj.AddComponent<SlotView>();
                slotView.Setup(i, isPlayerSide, player.slots[i]);

                var runeSymbol = CreateText(slotObj.transform, "RuneGlyph", "✧", 32,
                    new Color(runeColor.r, runeColor.g, runeColor.b, 0.35f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120, 120));
                runeSymbol.raycastTarget = false;

                var lbl = CreateText(slotObj.transform, "SlotName", slotLabel, 13,
                    runeColor, TextAnchor.LowerCenter, new Vector2(0, -82), new Vector2(150, 22));
                lbl.raycastTarget = false;
                slotView.slotNameText = lbl;

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

        public static Slider CreateSlider(Transform parent, string name, Color bgColor, Color fillColor, Vector2 pos, Vector2 size)
        {
            var sliderObj = CreatePanel(parent, name, bgColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            var slider = sliderObj.AddComponent<Slider>();
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderObj.transform, false);
            var rectFA = fillArea.AddComponent<RectTransform>();
            rectFA.anchorMin = Vector2.zero;
            rectFA.anchorMax = Vector2.one;
            rectFA.sizeDelta = Vector2.zero;

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

        private static Sprite _circleSprite;
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
            txt.text = MonsterCardGame.Utils.ArabicFixer.Fix(content);
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = align;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            return txt;
        }

        public static Button CreateButton(Transform parent, string name, string label, Color bgColor, Color textColor, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var btnObj = CreatePanel(parent, name, bgColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            var btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = btnObj.GetComponent<Image>();
            if (onClick != null) btn.onClick.AddListener(onClick);

            var txt = CreateText(btnObj.transform, "BtnText", label, 16, textColor, TextAnchor.MiddleCenter, Vector2.zero, size);
            txt.raycastTarget = false;
            return btn;
        }

        public static Button CreateAnchorButton(Transform parent, string name, string label, Color bgColor, Color textColor, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var btnObj = CreatePanel(parent, name, bgColor, anchorMin, anchorMax, pos, size);
            var btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = btnObj.GetComponent<Image>();
            if (onClick != null) btn.onClick.AddListener(onClick);

            var txt = CreateText(btnObj.transform, "BtnText", label, 16, textColor, TextAnchor.MiddleCenter, Vector2.zero, size);
            txt.raycastTarget = false;
            return btn;
        }
    }

    public class QuitButtonHandler : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
        {
            Debug.Log("🚪 تم النقر على زر الخروج عبر PointerClick!");
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

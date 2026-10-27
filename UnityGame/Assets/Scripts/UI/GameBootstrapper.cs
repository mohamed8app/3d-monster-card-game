using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using MonsterCardGame.Core;
using MonsterCardGame.Data;

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
                    defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                catch { }

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

        // يتم تشغيل هذه الدالة تلقائياً بمجرد الضغط على Play في أي مشهد
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void AutoInitializeGame()
        {
            if (FindObjectOfType<GameManager>() != null)
            {
                Debug.Log("GameManager موجود بالفعل في المشهد.");
                return;
            }

            Debug.Log("🚀 جاري بناء ساحة المعركة التكتيكية 3D تلقائياً...");

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

            // 5. بناء الخانات الـ 6 لكل لاعب (تم إلغاء المدافع الخلفي)
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

            Debug.Log("🎉 اكتمل بناء ساحة المعركة التكتيكية بنجاح! اللعبة تعمل الآن.");
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
                cam.backgroundColor = new Color(0.08f, 0.09f, 0.13f); // كحلي داكن عصري
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
                bgImg.color = new Color(0.9f, 0.9f, 1f, 1f);
            }
            else
            {
                bgImg.color = new Color(0.08f, 0.09f, 0.13f);
            }

            // 1. شريط القائد الخصم (أعلى الشاشة Y: +460)
            var enemyBar = CreatePanel(canvas, "EnemyBar", new Color(0.18f, 0.12f, 0.15f, 0.9f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -42), new Vector2(1650, 55));
            
            ui.enemyHpText = CreateText(enemyBar.transform, "EnemyHpText", "🔴 قائد العدو: 1000 / 1000 HP", 22,
                new Color(1f, 0.4f, 0.4f), TextAnchor.MiddleLeft, new Vector2(-550, 0), new Vector2(450, 50));
            
            var eHandCount = CreateText(enemyBar.transform, "EnemyHandCount", "🎴 كروت يد الخصم: 6 بطاقات مستترة", 18,
                new Color(0.85f, 0.85f, 0.95f), TextAnchor.MiddleRight, new Vector2(500, 0), new Vector2(400, 50));
            ui.enemyHandCountText = eHandCount;

            // 1.1 حاوية كروت يد الخصم المقلوبة (تظهر من الظهر فقط Face-Down)
            var enemyHandPanel = CreatePanel(canvas, "EnemyHandPanel", new Color(0.10f, 0.08f, 0.15f, 0.88f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -104), new Vector2(1050, 62));

            var eOutline = enemyHandPanel.AddComponent<Outline>();
            eOutline.effectColor = new Color(0.6f, 0.45f, 0.25f, 0.7f);
            eOutline.effectDistance = new Vector2(1.5f, 1.5f);

            CreateText(enemyHandPanel.transform, "EnemyHandLabel", "يد الخصم (مستترة):", 13,
                new Color(0.85f, 0.75f, 0.95f), TextAnchor.MiddleLeft, new Vector2(-440, 0), new Vector2(140, 30));

            var enemyCardsContainer = new GameObject("EnemyCardsContainer");
            enemyCardsContainer.transform.SetParent(enemyHandPanel.transform, false);
            var rectEC = enemyCardsContainer.AddComponent<RectTransform>();
            rectEC.sizeDelta = new Vector2(860, 56);
            rectEC.anchoredPosition = new Vector2(60, 0);

            var ecLayout = enemyCardsContainer.AddComponent<HorizontalLayoutGroup>();
            ecLayout.spacing = 10;
            ecLayout.childAlignment = TextAnchor.MiddleCenter;
            ecLayout.childControlWidth = false;
            ecLayout.childControlHeight = false;

            ui.enemyHandContainer = enemyCardsContainer.transform;

            // زر إغلاق وإنهاء اللعبة في أعلى يمين الشاشة (Exit Game Button)
            CreateAnchorButton(canvas, "QuitGameBtn", "❌ خروج",
                new Color(0.85f, 0.2f, 0.25f, 0.95f), Color.white,
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-60, -42), new Vector2(95, 45),
                () => {
                    Debug.Log("🚪 الخروج من اللعبة...");
                    #if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
                    #else
                    Application.Quit();
                    #endif
                });

            // 2. خانات العدو الـ 6 (الخط العلوي)
            BuildSlotsRow(canvas, ui, ai, false);

            // 3. الخط الفاصل الأوسط وسجل المعركة (Y: 0)
            var midArena = CreatePanel(canvas, "MidArena", new Color(0.12f, 0.13f, 0.18f, 0.6f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(1800, 100));

            ui.turnInfoText = CreateText(midArena.transform, "TurnInfoText", "⚔️ ساحة المعركة التكتيكية (1,000 HP • 6 خانات)", 24,
                new Color(0.95f, 0.85f, 0.4f), TextAnchor.MiddleCenter, new Vector2(-200, 0), new Vector2(700, 60));

            var logPanel = CreatePanel(midArena.transform, "LogPanel", new Color(0.08f, 0.08f, 0.12f, 0.85f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-300, 0), new Vector2(550, 90));

            ui.combatLogText = CreateText(logPanel.transform, "CombatLogText", "سجل المعركة: تبدأ المباراة بـ 1,000 HP و 7 خانات...", 14,
                Color.white, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(530, 80));

            // 4. خانات اللاعب الـ 7 (الخط السفلي)
            BuildSlotsRow(canvas, ui, human, true);

            // 5. شريط القائد والتحكم الخاص بك (Y: -320)
            var playerControlBar = CreatePanel(canvas, "PlayerControlBar", new Color(0.12f, 0.16f, 0.22f, 0.95f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 190), new Vector2(1800, 80));

            ui.playerHpText = CreateText(playerControlBar.transform, "PlayerHpText", "🟢 قائدك (أنت): 1000 / 1000 HP", 22,
                new Color(0.4f, 1f, 0.5f), TextAnchor.MiddleLeft, new Vector2(-600, 0), new Vector2(400, 50));

            ui.playerPowerText = CreateText(playerControlBar.transform, "PlayerPowerText", "⚡ الباور: 1 / 1 (المانا: 1)", 20,
                new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleLeft, new Vector2(-220, 0), new Vector2(350, 50));

            // أزرار التحكم
            ui.attackButton = CreateButton(playerControlBar.transform, "AttackBtn", "⚔️ هجوم الوحوش",
                new Color(0.85f, 0.25f, 0.25f), Color.white, new Vector2(250, 0), new Vector2(180, 50), () => ui.SendMessage("OnAttackClicked"));

            ui.sacrificeButton = CreateButton(playerControlBar.transform, "SacrificeBtn", "🔥 تضحية بكارت (+1 مانا)",
                new Color(0.85f, 0.65f, 0.15f), new Color(0.1f, 0.1f, 0.1f), new Vector2(460, 0), new Vector2(200, 50), () => ui.SendMessage("OnSacrificeClicked"));

            ui.endTurnButton = CreateButton(playerControlBar.transform, "EndTurnBtn", "🔁 إنهاء الدور",
                new Color(0.25f, 0.5f, 0.85f), Color.white, new Vector2(680, 0), new Vector2(160, 50), () => ui.SendMessage("OnEndTurnClicked"));

            // 6. منطقة يد الكروت بالأسفل (Y: -450)
            var handPanel = CreatePanel(canvas, "HandPanel", new Color(0.09f, 0.1f, 0.14f, 0.98f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 70), new Vector2(1800, 130));

            var handLabel = CreateText(handPanel.transform, "HandLabel", "كروت يدك (اضغط على كارت ثم اضغط على خانة بالساحة لإنزاله):", 14,
                new Color(0.7f, 0.75f, 0.85f), TextAnchor.UpperLeft, new Vector2(-600, 45), new Vector2(550, 25));

            var handCardsContainer = new GameObject("CardsContainer");
            handCardsContainer.transform.SetParent(handPanel.transform, false);
            var rectH = handCardsContainer.AddComponent<RectTransform>();
            rectH.sizeDelta = new Vector2(1700, 100);
            rectH.anchoredPosition = new Vector2(0, -10);

            var hLayout = handCardsContainer.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 15;
            hLayout.childAlignment = TextAnchor.MiddleCenter;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;

            ui.playerHandContainer = handCardsContainer.transform;
        }

        private static void BuildSlotsRow(Transform canvas, BattleUIManager ui, PlayerState player, bool isPlayerSide)
        {
            float posY = isPlayerSide ? -150f : 150f;

            // توزيع الخانات الـ 6 بشكل متناسق ومستوٍ في خط معركة موحد:
            // 0, 1: أجنحة يسار
            // 2, 3: قلب الهجوم
            // 4, 5: أجنحة يمين
            float[] xCoords = { -520f, -310f, -105f, 105f, 310f, 520f };

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

                Color borderColor = (i == 2 || i == 3)
                    ? new Color(1f, 0.45f, 0.2f, 0.8f) // برتقالي لقلب الهجوم
                    : new Color(0.25f, 0.7f, 1f, 0.8f); // سماوي للأجنحة

                var slotObj = CreatePanel(canvas, $"{(isPlayerSide ? "P" : "E")}_SlotView_{i}", new Color(0.14f, 0.16f, 0.22f, 0.85f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(posX, posY), new Vector2(175, 115));

                var outline = slotObj.AddComponent<Outline>();
                outline.effectColor = borderColor;
                outline.effectDistance = new Vector2(3, 3);

                var slotView = slotObj.AddComponent<SlotView>();
                slotView.Setup(i, isPlayerSide, player.slots[i]);

                var lbl = CreateText(slotObj.transform, "SlotName", slotLabel, 14,
                    borderColor, TextAnchor.LowerCenter, new Vector2(0, -40), new Vector2(165, 25));

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

            CreateText(btnObj.transform, "BtnText", label, 16, textColor, TextAnchor.MiddleCenter, Vector2.zero, size);
            return btn;
        }

        public static Button CreateAnchorButton(Transform parent, string name, string label, Color bgColor, Color textColor, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var btnObj = CreatePanel(parent, name, bgColor, anchorMin, anchorMax, pos, size);
            var btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = btnObj.GetComponent<Image>();
            if (onClick != null) btn.onClick.AddListener(onClick);

            CreateText(btnObj.transform, "BtnText", label, 16, textColor, TextAnchor.MiddleCenter, Vector2.zero, size);
            return btn;
        }
    }
}

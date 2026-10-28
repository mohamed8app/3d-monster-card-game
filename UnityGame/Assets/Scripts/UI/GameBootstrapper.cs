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

            Debug.Log("🚀 جاري بناء ساحة المعركة التكتيكية 3D المحدثة تلقائياً...");

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

            Debug.Log("🎉 اكتمل بناء ساحة المعركة التكتيكية الفاخرة بنجاح! اللعبة تعمل الآن.");
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
                cam.backgroundColor = new Color(0.06f, 0.07f, 0.11f); // كحلي غامق سينمائي
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
                bgImg.color = Color.white;
            }
            else
            {
                bgImg.color = new Color(0.08f, 0.09f, 0.13f);
            }

            // 1. وسام قائد الخصم (Top-Left Leader Crest)
            var enemyCrest = CreatePanel(canvas, "EnemyLeaderCrest", new Color(0.12f, 0.10f, 0.15f, 0.92f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(195, -75), new Vector2(330, 95));
            var eCrestOutline = enemyCrest.AddComponent<Outline>();
            eCrestOutline.effectColor = new Color(0.85f, 0.25f, 0.3f, 0.85f);
            eCrestOutline.effectDistance = new Vector2(2, 2);

            CreateText(enemyCrest.transform, "EnemyLeaderTitle", "💀 قائد الظلال (الخصم)", 17,
                new Color(0.95f, 0.75f, 0.75f), TextAnchor.UpperCenter, new Vector2(0, 22), new Vector2(310, 26));

            // شريط صحة الخصم
            ui.enemyHpSlider = CreateSlider(enemyCrest.transform, "EnemyHpBar",
                new Color(0.18f, 0.14f, 0.2f, 0.95f), new Color(0.9f, 0.2f, 0.25f),
                new Vector2(0, -10), new Vector2(290, 16));

            ui.enemyHpText = CreateText(enemyCrest.transform, "EnemyHpText", "1000 / 1000 HP", 14,
                Color.white, TextAnchor.MiddleCenter, new Vector2(0, -10), new Vector2(290, 20));

            // 2. يد الخصم المستترة (Top-Center Enemy Hand Panel)
            var enemyHandPanel = CreatePanel(canvas, "EnemyHandPanel", new Color(0.09f, 0.08f, 0.14f, 0.85f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -52), new Vector2(720, 85));
            var ehOutline = enemyHandPanel.AddComponent<Outline>();
            ehOutline.effectColor = new Color(0.65f, 0.48f, 0.22f, 0.8f);
            ehOutline.effectDistance = new Vector2(1.5f, 1.5f);

            ui.enemyHandCountText = CreateText(enemyHandPanel.transform, "EnemyHandLabel", "🎴 كروت يد الخصم: 6 بطاقات مستترة", 14,
                new Color(0.92f, 0.82f, 0.55f), TextAnchor.UpperCenter, new Vector2(0, 26), new Vector2(500, 22));

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

            // 4. مؤشر الدور العائم في منتصف الساحة (Central Floating Status Pill - Rift is fully visible!)
            var statusPill = CreatePanel(canvas, "StatusPill", new Color(0.08f, 0.10f, 0.16f, 0.75f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(380, 38));
            var spOutline = statusPill.AddComponent<Outline>();
            spOutline.effectColor = new Color(0.92f, 0.75f, 0.25f, 0.85f);
            spOutline.effectDistance = new Vector2(1.5f, 1.5f);

            ui.turnInfoText = CreateText(statusPill.transform, "TurnInfoText", "⚔️ دورك الآن (اختر كارت أو هاجم)", 17,
                new Color(1f, 0.92f, 0.45f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(370, 36));

            // 5. خانات العدو الـ 6 (Runic Summoning Circles - Top Row Y: +145)
            BuildSlotsRow(canvas, ui, ai, false);

            // 6. خانات اللاعب الـ 6 (Runic Summoning Circles - Bottom Row Y: -140)
            BuildSlotsRow(canvas, ui, human, true);

            // 7. وسام قائدك والباور (Bottom-Left Leader Crest & Mana)
            var playerCrest = CreatePanel(canvas, "PlayerLeaderCrest", new Color(0.09f, 0.12f, 0.18f, 0.92f),
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(195, 140), new Vector2(330, 115));
            var pCrestOutline = playerCrest.AddComponent<Outline>();
            pCrestOutline.effectColor = new Color(0.95f, 0.8f, 0.3f, 0.9f);
            pCrestOutline.effectDistance = new Vector2(2, 2);

            CreateText(playerCrest.transform, "PlayerLeaderTitle", "👑 البطل (أنت)", 18,
                new Color(1f, 0.92f, 0.5f), TextAnchor.UpperCenter, new Vector2(0, 34), new Vector2(310, 26));

            // شريط صحة اللاعب
            ui.playerHpSlider = CreateSlider(playerCrest.transform, "PlayerHpBar",
                new Color(0.12f, 0.18f, 0.14f, 0.95f), new Color(0.2f, 0.9f, 0.35f),
                new Vector2(0, 6), new Vector2(290, 16));

            ui.playerHpText = CreateText(playerCrest.transform, "PlayerHpText", "1000 / 1000 HP", 14,
                Color.white, TextAnchor.MiddleCenter, new Vector2(0, 6), new Vector2(290, 20));

            // عداد الباور والمانا
            ui.playerPowerText = CreateText(playerCrest.transform, "PlayerPowerText", "💎 الباور: 1 / 1  |  المانا: 1", 16,
                new Color(0.35f, 0.9f, 1f), TextAnchor.MiddleCenter, new Vector2(0, -28), new Vector2(300, 26));

            // 8. لوحة الأوامر وسجل المعركة على اليمين (Right Side Command Center & Combat Log)
            // 8.1 سجل المعركة العائم
            var logPanel = CreatePanel(canvas, "CombatLogPanel", new Color(0.08f, 0.09f, 0.14f, 0.82f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-175, 135), new Vector2(300, 180));
            var logOutline = logPanel.AddComponent<Outline>();
            logOutline.effectColor = new Color(0.65f, 0.5f, 0.25f, 0.75f);
            logOutline.effectDistance = new Vector2(1.5f, 1.5f);

            CreateText(logPanel.transform, "LogHeader", "📜 سجل المعركة", 14,
                new Color(0.95f, 0.85f, 0.5f), TextAnchor.UpperCenter, new Vector2(0, 72), new Vector2(280, 24));

            ui.combatLogText = CreateText(logPanel.transform, "CombatLogText", "تبدأ المباراة بـ 1,000 HP و 6 خانات تكتيكية...", 13,
                new Color(0.9f, 0.92f, 0.96f), TextAnchor.UpperLeft, new Vector2(0, -12), new Vector2(280, 135));

            // 8.2 أزرار الأوامر
            // زر هجوم الوحوش
            ui.attackButton = CreateAnchorButton(canvas, "AttackBtn", "⚔️ هجوم الوحوش",
                new Color(0.80f, 0.20f, 0.20f, 0.95f), Color.white,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-175, -50), new Vector2(210, 46),
                () => ui.SendMessage("OnAttackClicked"));
            var abOutline = ui.attackButton.gameObject.AddComponent<Outline>();
            abOutline.effectColor = new Color(1f, 0.45f, 0.45f, 0.8f);
            abOutline.effectDistance = new Vector2(1.5f, 1.5f);

            // زر التضحية
            ui.sacrificeButton = CreateAnchorButton(canvas, "SacrificeBtn", "🔥 تضحية بكارت (+1 مانا)",
                new Color(0.85f, 0.60f, 0.12f, 0.95f), new Color(0.08f, 0.08f, 0.1f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-175, -105), new Vector2(210, 46),
                () => ui.SendMessage("OnSacrificeClicked"));
            var sbOutline = ui.sacrificeButton.gameObject.AddComponent<Outline>();
            sbOutline.effectColor = new Color(1f, 0.85f, 0.35f, 0.8f);
            sbOutline.effectDistance = new Vector2(1.5f, 1.5f);

            // زر إنهاء الدور الفخم (Hearthstone-style Turn Medallion)
            ui.endTurnButton = CreateAnchorButton(canvas, "EndTurnBtn", "🔁 إنهاء الدور",
                new Color(0.18f, 0.50f, 0.85f, 0.95f), Color.white,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-175, -175), new Vector2(210, 65),
                () => ui.SendMessage("OnEndTurnClicked"));
            var etOutline = ui.endTurnButton.gameObject.AddComponent<Outline>();
            etOutline.effectColor = new Color(0.95f, 0.85f, 0.3f, 0.95f);
            etOutline.effectDistance = new Vector2(2.5f, 2.5f);

            // 9. منطقة يد الكروت بالأسفل بتدرج زجاجي مفتوح (Bottom Player Hand - Portrait Cards)
            var handPanel = CreatePanel(canvas, "HandPanel", new Color(0.06f, 0.07f, 0.11f, 0.72f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 110), new Vector2(1150, 215));
            var hpOutline = handPanel.AddComponent<Outline>();
            hpOutline.effectColor = new Color(0.45f, 0.40f, 0.60f, 0.45f);
            hpOutline.effectDistance = new Vector2(1, 1);

            CreateText(handPanel.transform, "HandLabel", "🎴 كروت يدك (اسحب الكارت وأفلته في خانتك الميدانية لإنزاله):", 13,
                new Color(0.85f, 0.88f, 0.95f), TextAnchor.UpperCenter, new Vector2(0, 96), new Vector2(650, 20));

            var handCardsContainer = new GameObject("CardsContainer");
            handCardsContainer.transform.SetParent(handPanel.transform, false);
            var rectH = handCardsContainer.AddComponent<RectTransform>();
            rectH.sizeDelta = new Vector2(1130, 198);
            rectH.anchoredPosition = new Vector2(0, -6);

            var hLayout = handCardsContainer.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 12;
            hLayout.childAlignment = TextAnchor.MiddleCenter;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;

            ui.playerHandContainer = handCardsContainer.transform;
        }

        private static void BuildSlotsRow(Transform canvas, BattleUIManager ui, PlayerState player, bool isPlayerSide)
        {
            float posY = isPlayerSide ? -140f : 145f;

            // توزيع الخانات الـ 6 بشكل متناسق ومستوٍ في خط معركة موحد:
            // 0, 1: أجنحة يسار
            // 2, 3: قلب الهجوم
            // 4, 5: أجنحة يمين
            float[] xCoords = { -480f, -288f, -96f, 96f, 288f, 480f };

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

                // لون إطار دائرة الاستدعاء الرونية
                Color runeColor;
                if (isPlayerSide)
                {
                    runeColor = (i == 2 || i == 3)
                        ? new Color(1f, 0.65f, 0.25f, 0.85f) // ذهبي ناري لقلب الهجوم
                        : new Color(0.25f, 0.80f, 1f, 0.80f); // سماوي كوني للأجنحة
                }
                else
                {
                    runeColor = (i == 2 || i == 3)
                        ? new Color(1f, 0.35f, 0.35f, 0.85f) // قرمزي ناري للعدو
                        : new Color(0.70f, 0.40f, 1f, 0.80f); // أرجواني شبحي للأجنحة
                }

                // قاعدة الرون الحجرية الشفافة
                var slotObj = CreatePanel(canvas, $"{(isPlayerSide ? "P" : "E")}_SlotView_{i}",
                    new Color(0.08f, 0.10f, 0.16f, 0.50f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(posX, posY), new Vector2(135, 175));

                var outline = slotObj.AddComponent<Outline>();
                outline.effectColor = runeColor;
                outline.effectDistance = new Vector2(2, 2);

                var slotView = slotObj.AddComponent<SlotView>();
                slotView.Setup(i, isPlayerSide, player.slots[i]);

                // رمز رون سحري في قلب الخانة الفارغة
                var runeSymbol = CreateText(slotObj.transform, "RuneGlyph", "✧", 26,
                    new Color(runeColor.r, runeColor.g, runeColor.b, 0.35f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(100, 100));
                runeSymbol.raycastTarget = false;

                // مسمى الخانة التكتيكي بالأسفل
                var lbl = CreateText(slotObj.transform, "SlotName", slotLabel, 13,
                    runeColor, TextAnchor.LowerCenter, new Vector2(0, -68), new Vector2(130, 22));
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

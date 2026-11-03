using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using MonsterCardGame.Core;
using MonsterCardGame.Data;
using MonsterCardGame.Utils;

namespace MonsterCardGame.UI
{
    public class SlotView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public int slotIndex;
        public bool isPlayerSide = true;
        public BoardSlot logicSlot;

        [Header("UI References")]
        public Text slotNameText;
        public Image slotBorder;
        public Transform cardHolder;
        public CardView placedCardView;

        // كائنات بطاقة الوحش الميدانية العمودية (Portrait Field Card Token Standee)
        public CardData currentCardData;
        public MonsterInstance currentMonsterInstance;
        private GameObject fieldCardToken;
        private Image tokenArtImage;
        private Outline tokenOutline;
        private Text tokenNameText;
        private Text tokenCostText;
        private Text tokenAtkText;
        private Text tokenDefText;
        private RectTransform hpBarFillRect;
        private Image hpBarFillImage;
        private CardPulseEffect pulseEffect;
        private bool isHovered = false;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.OnSlotClicked(slotIndex, isPlayerSide);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHovered = true;
            if (currentCardData != null && BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.ShowMonsterDetails(currentCardData, currentMonsterInstance, isPlayerSide);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
        }

        private void Update()
        {
            float targetScale = isHovered ? 1.07f : 1.0f;
            Vector3 curScale = transform.localScale;
            if (Mathf.Abs(curScale.x - targetScale) > 0.001f)
            {
                transform.localScale = Vector3.Lerp(curScale, Vector3.one * targetScale, Time.deltaTime * 15f);
            }
        }

        public void Setup(int index, bool playerSide, BoardSlot slot)
        {
            slotIndex = index;
            isPlayerSide = playerSide;
            logicSlot = slot;

            if (slotNameText != null)
            {
                slotNameText.text = ArabicFixer.Fix(GetArabicSlotName(index));
            }
        }

        private string GetArabicSlotName(int idx)
        {
            switch (idx)
            {
                case 0: return "جناح أيسر 1";
                case 1: return "جناح أيسر 2";
                case 2: return "قلب الهجوم 1";
                case 3: return "قلب الهجوم 2";
                case 4: return "جناح أيمن 1";
                case 5: return "جناح أيمن 2";
                default: return $"خانة {idx}";
            }
        }

        public void DisplayMonster(CardData data, MonsterInstance instance)
        {
            if (data == null || instance == null)
            {
                ClearDisplay();
                return;
            }

            currentCardData = data;
            currentMonsterInstance = instance;

            EnsureFieldTokenCreated();

            if (slotNameText != null)
            {
                slotNameText.gameObject.SetActive(false);
            }
            var runeGlyph = transform.Find("RuneGlyph");
            if (runeGlyph != null) runeGlyph.gameObject.SetActive(false);

            fieldCardToken.SetActive(true);

            // 1. رسمة الوحش الفنية الملحمية تملأ كامل الكارت بنسبة 100%
            if (tokenArtImage != null)
            {
                var sprite = MonsterArtLoader.GetMonsterSprite(data);
                if (sprite != null)
                {
                    tokenArtImage.sprite = sprite;
                    tokenArtImage.color = Color.white;
                }
            }

            // 2. لون العنصر والإطار الخارجي النحيف
            Color elemColor = MonsterArtLoader.GetElementColor(data.element);

            // 3. دائرة نقاط الاستدعاء أعلى اليمين (Astral Masters: Top-Right Blue Circle)
            if (tokenCostText != null)
            {
                tokenCostText.text = data.cost.ToString();
            }

            // 4. أرقام الهجوم والدفاع في الركنين السفليين (Astral Masters: Bottom-Left ATK, Bottom-Right DEF)
            var gm = GameManager.Instance;
            var activeEnv = gm != null ? gm.activeEnvironment : null;
            int effectiveAtk = instance.GetEffectiveAtk(activeEnv);
            float envMod = instance.GetEnvironmentModifier(activeEnv);

            if (tokenAtkText != null)
            {
                tokenAtkText.text = effectiveAtk.ToString();
                if (envMod > 1.01f)
                {
                    tokenAtkText.color = new Color(0.40f, 1f, 0.55f);
                }
                else if (envMod < 0.99f)
                {
                    tokenAtkText.color = new Color(1f, 0.45f, 0.45f);
                }
                else
                {
                    tokenAtkText.color = Color.white;
                }
            }

            if (tokenDefText != null)
            {
                tokenDefText.text = instance.currentDef.ToString();
                if (instance.baseDef > 0 && instance.currentDef < instance.baseDef)
                {
                    tokenDefText.color = new Color(1f, 0.45f, 0.45f); // تلوين بالأحمر عند تلقي ضرر
                }
                else
                {
                    tokenDefText.color = Color.white;
                }
            }

            // 5. توهج ونبض الجاهزية للوحش (Breathing Pulse for Ready Attackers)
            bool isReadyToStrike = instance.canAttack && isPlayerSide;
            if (pulseEffect != null)
            {
                Color glow = isReadyToStrike ? new Color(1f, 0.88f, 0.3f, 0.95f) : elemColor;
                pulseEffect.SetPulsing(isReadyToStrike, glow);
            }
            else if (tokenOutline != null)
            {
                tokenOutline.effectColor = isReadyToStrike ? new Color(1f, 0.88f, 0.3f, 0.95f) : elemColor;
            }
        }

        private void EnsureFieldTokenCreated()
        {
            if (fieldCardToken != null) return;

            // كارت الوحش الميداني بدون فريم بنمط Astral Masters (Full-Art 154x196)
            fieldCardToken = GameBootstrapper.CreatePanel(transform, "FieldCardToken", new Color(0.04f, 0.05f, 0.08f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(154, 196));

            tokenOutline = fieldCardToken.AddComponent<Outline>();
            tokenOutline.effectColor = new Color(0.95f, 0.85f, 0.3f, 0.95f);
            tokenOutline.effectDistance = new Vector2(2f, 2f);

            pulseEffect = fieldCardToken.AddComponent<CardPulseEffect>();

            // 1. رسمة الوحش الفنية تمتد على 100% من مساحة الكارت بدون أي فريم يضيقها
            var artObj = new GameObject("MonsterArt");
            artObj.transform.SetParent(fieldCardToken.transform, false);
            var rectArt = artObj.AddComponent<RectTransform>();
            rectArt.anchorMin = Vector2.zero;
            rectArt.anchorMax = Vector2.one;
            rectArt.sizeDelta = Vector2.zero;
            rectArt.anchoredPosition = Vector2.zero;
            tokenArtImage = artObj.AddComponent<Image>();
            tokenArtImage.preserveAspect = false;
            tokenArtImage.raycastTarget = false;

            // 2. دائرة نقاط الاستدعاء أعلى اليمين (Top-Right Blue Circle - Astral Masters Style)
            var costCircle = GameBootstrapper.CreateCirclePanel(fieldCardToken.transform, "CostCircle",
                new Color(0.10f, 0.28f, 0.65f, 0.96f),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20, -20), new Vector2(34, 34));
            var costOutline = costCircle.AddComponent<Outline>();
            costOutline.effectColor = new Color(0.40f, 0.90f, 1f, 1f);
            costOutline.effectDistance = new Vector2(1.5f, 1.5f);
            tokenCostText = GameBootstrapper.CreateText(costCircle.transform, "CostText", "", 17,
                new Color(1f, 0.95f, 0.70f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(34, 34));
            tokenCostText.fontStyle = FontStyle.Bold;
            tokenCostText.raycastTarget = false;

            // 3. دائرة الهجوم أسفل اليسار (Bottom-Left Red Circle - Astral Masters Style)
            var atkCircle = GameBootstrapper.CreateCirclePanel(fieldCardToken.transform, "AtkCircle",
                new Color(0.70f, 0.12f, 0.16f, 0.96f),
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20, 20), new Vector2(34, 34));
            var atkOutline = atkCircle.AddComponent<Outline>();
            atkOutline.effectColor = new Color(1f, 0.45f, 0.35f, 1f);
            atkOutline.effectDistance = new Vector2(1.5f, 1.5f);
            tokenAtkText = GameBootstrapper.CreateText(atkCircle.transform, "AtkText", "0", 17,
                Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(34, 34));
            tokenAtkText.fontStyle = FontStyle.Bold;
            tokenAtkText.raycastTarget = false;

            // 4. دائرة الدفاع / الصحة أسفل اليمين (Bottom-Right Green Circle - Astral Masters Style)
            var defCircle = GameBootstrapper.CreateCirclePanel(fieldCardToken.transform, "DefCircle",
                new Color(0.10f, 0.52f, 0.22f, 0.96f),
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20, 20), new Vector2(34, 34));
            var defOutline = defCircle.AddComponent<Outline>();
            defOutline.effectColor = new Color(0.35f, 1f, 0.45f, 1f);
            defOutline.effectDistance = new Vector2(1.5f, 1.5f);
            tokenDefText = GameBootstrapper.CreateText(defCircle.transform, "DefText", "0", 17,
                Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(34, 34));
            tokenDefText.fontStyle = FontStyle.Bold;
            tokenDefText.raycastTarget = false;
        }

        public void ClearDisplay()
        {
            currentCardData = null;
            currentMonsterInstance = null;
            if (fieldCardToken != null)
            {
                fieldCardToken.SetActive(false);
            }
            if (slotNameText != null)
            {
                slotNameText.gameObject.SetActive(true);
            }
            var runeGlyph = transform.Find("RuneGlyph");
            if (runeGlyph != null) runeGlyph.gameObject.SetActive(true);
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.UI;
using MonsterCardGame.Core;
using MonsterCardGame.Data;
using MonsterCardGame.Utils;

namespace MonsterCardGame.UI
{
    public class SlotView : MonoBehaviour
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
        private GameObject fieldCardToken;
        private Image tokenArtImage;
        private Outline tokenOutline;
        private Text tokenNameText;
        private Text tokenTopText;
        private Text tokenAtkText;
        private Text tokenDefText;
        private RectTransform hpBarFillRect;
        private Image hpBarFillImage;
        private CardPulseEffect pulseEffect;

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

        public void DisplayMonster(MonsterData data, MonsterInstance instance)
        {
            if (data == null || instance == null)
            {
                ClearDisplay();
                return;
            }

            EnsureFieldTokenCreated();

            if (slotNameText != null)
            {
                slotNameText.gameObject.SetActive(false);
            }
            var runeGlyph = transform.Find("RuneGlyph");
            if (runeGlyph != null) runeGlyph.gameObject.SetActive(false);

            fieldCardToken.SetActive(true);

            // 1. صورة الوحش الفنية الملحمية
            if (tokenArtImage != null)
            {
                var sprite = MonsterArtLoader.GetMonsterSprite(data);
                if (sprite != null)
                {
                    tokenArtImage.sprite = sprite;
                    tokenArtImage.color = Color.white;
                }
            }

            // 2. لون العنصر والإطار
            Color elemColor = MonsterArtLoader.GetElementColor(data.element);
            string elemEmoji = MonsterArtLoader.GetElementEmoji(data.element);

            // 3. السطر العلوي: العنصر والتكلفة
            if (tokenTopText != null)
            {
                tokenTopText.text = ArabicFixer.Fix($"{elemEmoji} {data.element} | {data.cost}⚡");
                tokenTopText.color = elemColor;
            }

            // 4. اسم الوحش
            if (tokenNameText != null)
            {
                tokenNameText.text = ArabicFixer.Fix(data.name);
            }

            // 5. أرقام الهجوم والدفاع
            if (tokenAtkText != null)
            {
                tokenAtkText.text = ArabicFixer.Fix($"⚔️ {instance.baseAtk}");
            }
            if (tokenDefText != null)
            {
                tokenDefText.text = ArabicFixer.Fix($"🛡️ {instance.currentDef}");
            }

            // 6. شريط الصحة والدفاع الحي (Dynamic Defense / HP Bar)
            if (hpBarFillRect != null && hpBarFillImage != null)
            {
                float ratio = instance.baseDef > 0 ? Mathf.Clamp01((float)instance.currentDef / instance.baseDef) : 0f;
                hpBarFillRect.anchorMax = new Vector2(ratio, 1f);

                if (ratio > 0.6f)
                    hpBarFillImage.color = new Color(0.2f, 0.9f, 0.35f); // أخضر صحي
                else if (ratio > 0.3f)
                    hpBarFillImage.color = new Color(1.0f, 0.75f, 0.2f); // برتقالي متضرر
                else
                    hpBarFillImage.color = new Color(0.95f, 0.25f, 0.25f); // أحمر حرج
            }

            // 7. توهج ونبض الجاهزية (Breathing Pulse for Ready Attackers)
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

            // الحاوية الكلية لبطاقة الوحش العمودية داخل الخانة (Portrait Standee 125x165)
            fieldCardToken = GameBootstrapper.CreatePanel(transform, "FieldCardToken", new Color(0.08f, 0.09f, 0.13f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(125, 165));

            tokenOutline = fieldCardToken.AddComponent<Outline>();
            tokenOutline.effectColor = new Color(0.95f, 0.85f, 0.3f, 0.95f);
            tokenOutline.effectDistance = new Vector2(2, 2);

            pulseEffect = fieldCardToken.AddComponent<CardPulseEffect>();

            // 1. شريط علوي للعنصر والتكلفة
            var topBar = GameBootstrapper.CreatePanel(fieldCardToken.transform, "TopBar", new Color(0.06f, 0.07f, 0.10f, 0.90f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 66), new Vector2(118, 20));
            tokenTopText = GameBootstrapper.CreateText(topBar.transform, "TopText", "", 11,
                Color.yellow, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(115, 20));
            tokenTopText.raycastTarget = false;

            // 2. إطار وصورة الوحش
            var artObj = new GameObject("MonsterArt");
            artObj.transform.SetParent(fieldCardToken.transform, false);
            var rectArt = artObj.AddComponent<RectTransform>();
            rectArt.anchoredPosition = new Vector2(0, 16);
            rectArt.sizeDelta = new Vector2(118, 74);
            tokenArtImage = artObj.AddComponent<Image>();
            tokenArtImage.preserveAspect = false;

            // 3. شريط الدفاع والصحة الحي (Dynamic Defense Bar)
            var hpBg = GameBootstrapper.CreatePanel(fieldCardToken.transform, "HpBg", new Color(0.12f, 0.12f, 0.15f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -26), new Vector2(116, 7));

            var hpFill = new GameObject("HpFill");
            hpFill.transform.SetParent(hpBg.transform, false);
            hpBarFillRect = hpFill.AddComponent<RectTransform>();
            hpBarFillRect.anchorMin = Vector2.zero;
            hpBarFillRect.anchorMax = Vector2.one;
            hpBarFillRect.sizeDelta = Vector2.zero;
            hpBarFillImage = hpFill.AddComponent<Image>();
            hpBarFillImage.color = new Color(0.2f, 0.9f, 0.35f);

            // 4. شريط اسم الوحش
            var nameBar = GameBootstrapper.CreatePanel(fieldCardToken.transform, "NameBar", new Color(0.05f, 0.06f, 0.09f, 0.92f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(118, 18));
            tokenNameText = GameBootstrapper.CreateText(nameBar.transform, "NameText", "", 11,
                Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(115, 18));
            tokenNameText.raycastTarget = false;

            // 5. شارات الهجوم والدفاع القتالية السفلية
            // شارة الهجوم (يسار)
            var atkBadge = GameBootstrapper.CreatePanel(fieldCardToken.transform, "AtkBadge", new Color(0.65f, 0.16f, 0.16f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-30, -63), new Vector2(52, 22));
            var atkOutline = atkBadge.AddComponent<Outline>();
            atkOutline.effectColor = new Color(1f, 0.4f, 0.4f, 0.8f);
            atkOutline.effectDistance = new Vector2(1, 1);
            tokenAtkText = GameBootstrapper.CreateText(atkBadge.transform, "AtkText", "⚔️ 0", 11,
                new Color(1f, 0.95f, 0.45f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(50, 22));
            tokenAtkText.raycastTarget = false;

            // شارة الدفاع (يمين)
            var defBadge = GameBootstrapper.CreatePanel(fieldCardToken.transform, "DefBadge", new Color(0.14f, 0.34f, 0.70f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, -63), new Vector2(52, 22));
            var defOutline = defBadge.AddComponent<Outline>();
            defOutline.effectColor = new Color(0.35f, 0.75f, 1f, 0.8f);
            defOutline.effectDistance = new Vector2(1, 1);
            tokenDefText = GameBootstrapper.CreateText(defBadge.transform, "DefText", "🛡️ 0", 11,
                new Color(0.45f, 0.95f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(50, 22));
            tokenDefText.raycastTarget = false;
        }

        public void ClearDisplay()
        {
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

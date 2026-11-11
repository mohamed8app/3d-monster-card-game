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

        // Portrait Field Card Token Standee
        public CardData currentCardData;
        public MonsterInstance currentMonsterInstance;
        public Image summoningCircleImage;
        private GameObject fieldCardToken;
        private Image tokenArtImage;
        private Image tokenFrameImage;
        private Outline tokenOutline;
        private Text tokenNameText;
        private Text tokenCostText;
        private Text tokenAtkText;
        private Text tokenDefText;
        private RectTransform hpBarFillRect;
        private Image hpBarFillImage;
        private CardPulseEffect pulseEffect;
        private Button abilityButton;
        private Image abilityButtonImage;
        private Outline abilityButtonOutline;
        private Text abilityButtonText;
        private bool isAbilityReady = false;
        private bool isSelectedOnBoard = false;
        private bool isHovered = false;
        private bool isHighlightedForSummon = false;
        private float circleRotation = 0f;

        public void SetSummonHighlight(bool active)
        {
            isHighlightedForSummon = active && (currentCardData == null) && isPlayerSide;
        }

        public void SetBoardSelected(bool selected)
        {
            isSelectedOnBoard = selected && (currentCardData != null);
            RefreshTokenOutline();
        }

        private void RefreshTokenOutline()
        {
            if (tokenOutline == null) return;
            bool isReadyToStrike = currentMonsterInstance != null && currentMonsterInstance.canAttack && isPlayerSide;
            if (isReadyToStrike)
            {
                tokenOutline.enabled = true;
                tokenOutline.effectColor = new Color(1f, 0.88f, 0.3f, 0.95f);
            }
            else if (isSelectedOnBoard)
            {
                tokenOutline.enabled = true;
                tokenOutline.effectColor = new Color(0.32f, 0.92f, 1f, 0.98f);
            }
            else
            {
                tokenOutline.enabled = false;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.OnSlotClicked(slotIndex, isPlayerSide);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (isHovered) return;
            isHovered = true;
            if (currentCardData != null && BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.ShowMonsterDetails(currentCardData, currentMonsterInstance, isPlayerSide);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!isHovered) return;
            isHovered = false;
        }

        private void Update()
        {
            // 1. Occupied Slot: Standee Card Token Breathing / Hover
            if (fieldCardToken != null && fieldCardToken.activeSelf)
            {
                if (abilityButton != null && abilityButton.gameObject.activeSelf)
                {
                    if (isAbilityReady)
                    {
                        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.5f);
                        float btnScale = Mathf.Lerp(1.0f, 1.055f, pulse);
                        abilityButton.transform.localScale = new Vector3(btnScale, btnScale, 1f);
                        if (abilityButtonOutline != null)
                        {
                            abilityButtonOutline.effectColor = Color.Lerp(
                                new Color(0.40f, 0.92f, 1f, 0.92f),
                                new Color(1f, 0.92f, 0.38f, 1f),
                                pulse);
                        }
                    }
                    else
                    {
                        abilityButton.transform.localScale = Vector3.one;
                    }
                }

                if (pulseEffect != null && pulseEffect.isPulsing && !isHovered)
                {
                    return;
                }
                float targetScale = isHovered ? 1.05f : 1.0f;
                Vector3 curScale = fieldCardToken.transform.localScale;
                if (Mathf.Abs(curScale.x - targetScale) > 0.001f)
                {
                    float s = Mathf.Lerp(curScale.x, targetScale, Time.unscaledDeltaTime * 16f);
                    fieldCardToken.transform.localScale = new Vector3(s, s, 1f);
                }
            }
            // 2. Empty Slot: Summoning Circle (Static Size, Light Blink & Active Summon Rotation)
            else if (summoningCircleImage != null && summoningCircleImage.gameObject.activeSelf && currentCardData == null)
            {
                // Permanent fixed size: no scaling in or out
                summoningCircleImage.transform.localScale = Vector3.one;

                if (isHighlightedForSummon)
                {
                    // Active Summoning Mode: Spins continuously and light increases brightly!
                    float spinSpeed = isHovered ? 160f : 85f;
                    circleRotation = (circleRotation - spinSpeed * Time.unscaledDeltaTime) % 360f;
                    summoningCircleImage.transform.localRotation = Quaternion.Euler(0, 0, circleRotation);

                    // High radiant light ready for summon
                    float targetAlpha = isHovered ? 1.0f : 0.90f;
                    summoningCircleImage.color = new Color(1f, 1f, 1f, targetAlpha);
                }
                else if (isHovered)
                {
                    // Direct mouse hover
                    summoningCircleImage.color = new Color(1f, 1f, 1f, 0.85f);
                    summoningCircleImage.transform.localRotation = Quaternion.Euler(0, 0, circleRotation);
                }
                else
                {
                    // Idle Breathing Glow: Static position & size, gentle light pulsing (تنور وتطفي ببطء)
                    float breatheSine = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.0f + slotIndex * 0.45f);
                    float ambientAlpha = Mathf.Lerp(0.25f, 0.65f, breatheSine);

                    summoningCircleImage.color = new Color(1f, 1f, 1f, ambientAlpha);
                    summoningCircleImage.transform.localRotation = Quaternion.Euler(0, 0, circleRotation);
                }
            }
        }

        public void Setup(int index, bool playerSide, BoardSlot slot)
        {
            slotIndex = index;
            isPlayerSide = playerSide;
            logicSlot = slot;

            if (slotNameText != null)
            {
                slotNameText.text = GetSlotName(index);
            }
        }

        private string GetSlotName(int idx)
        {
            switch (idx)
            {
                case 0: return "Left Flank 1";
                case 1: return "Left Flank 2";
                case 2: return "Vanguard 1";
                case 3: return "Vanguard 2";
                case 4: return "Right Flank 1";
                case 5: return "Right Flank 2";
                default: return $"Slot {idx}";
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
            if (summoningCircleImage != null)
            {
                summoningCircleImage.gameObject.SetActive(false);
            }
            var runeGlyph = transform.Find("RuneGlyph");
            if (runeGlyph != null) runeGlyph.gameObject.SetActive(false);

            fieldCardToken.SetActive(true);

            // 1. Load custom monster art by ID if available, otherwise display clean element backdrop
            Color elemColor = MonsterArtLoader.GetElementColor(data.element);
            Color bgCol = MonsterArtLoader.GetEnvironmentAtmosphereColor(data.element);
            Sprite customArt = MonsterArtLoader.GetCardSprite(data);

            if (tokenArtImage != null)
            {
                if (customArt != null)
                {
                    tokenArtImage.sprite = customArt;
                    tokenArtImage.color = Color.white;
                    tokenArtImage.preserveAspect = false;
                }
                else
                {
                    tokenArtImage.sprite = null;
                    tokenArtImage.color = bgCol;
                }
            }

            // 1.5. Dynamic Elemental Frame Overlay
            if (tokenFrameImage != null && data != null)
            {
                tokenFrameImage.sprite = MonsterArtLoader.GetCardFrameOverlaySprite(data.element);
            }

            // 2. Central Monster ID label (only shown when custom art is not present)
            if (tokenNameText != null)
            {
                tokenNameText.gameObject.SetActive(customArt == null);
                tokenNameText.text = data.name;
                tokenNameText.color = elemColor;
            }

            // 3. Top-Right Summon Cost Circle
            if (tokenCostText != null)
            {
                tokenCostText.text = data.cost.ToString();
            }

            // 4. Bottom-Left ATK & Bottom-Right DEF Badges (Astral Masters Style)
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
                    tokenDefText.color = new Color(1f, 0.45f, 0.45f);
                }
                else
                {
                    tokenDefText.color = Color.white;
                }
            }

            // 5. Breathing Pulse for Ready Attackers
            bool isReadyToStrike = instance.canAttack && isPlayerSide;
            if (pulseEffect != null)
            {
                Color glow = isReadyToStrike ? new Color(1f, 0.88f, 0.3f, 0.95f) : elemColor;
                pulseEffect.SetPulsing(isReadyToStrike, glow);
            }
            RefreshTokenOutline();

            // 6. Active Ability Pill Button on Player Board Monsters
            UpdateAbilityButton(instance);
        }

        private void EnsureAbilityButtonCreated()
        {
            if (abilityButton != null) return;

            abilityButton = GameBootstrapper.CreateBeveledButton(
                transform,
                $"SlotAbilityBtn_{slotIndex}",
                "⚡ ABILITY",
                new Color(0.08f, 0.58f, 0.90f, 0.98f),
                new Color(1f, 0.96f, 0.72f),
                new Vector2(0f, -104f),
                new Vector2(128f, 24f),
                11,
                () =>
                {
                    if (BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.OnSlotAbilityButtonClicked(slotIndex);
                    }
                });

            abilityButtonImage = abilityButton.GetComponent<Image>();
            abilityButtonOutline = abilityButton.gameObject.GetComponent<Outline>();
            if (abilityButtonOutline == null)
            {
                abilityButtonOutline = abilityButton.gameObject.AddComponent<Outline>();
            }
            abilityButtonOutline.effectDistance = new Vector2(1.5f, 1.5f);
            abilityButtonText = abilityButton.GetComponentInChildren<Text>();
            abilityButton.gameObject.SetActive(false);
        }

        private void UpdateAbilityButton(MonsterInstance instance)
        {
            if (!isPlayerSide || instance == null || !instance.IsAlive || !instance.HasActiveSkill)
            {
                isAbilityReady = false;
                if (abilityButton != null) abilityButton.gameObject.SetActive(false);
                return;
            }

            EnsureAbilityButtonCreated();
            abilityButton.gameObject.SetActive(true);
            abilityButton.transform.SetAsLastSibling();

            var gm = GameManager.Instance;
            if (gm == null || CombatSystem.Instance == null)
            {
                isAbilityReady = false;
                if (abilityButtonText != null) abilityButtonText.text = "⚡ ABILITY";
                return;
            }

            var state = CombatSystem.Instance.GetAbilityState(gm.humanPlayer, gm.aiPlayer, slotIndex, out int cost, out string _);
            isAbilityReady = (state == CombatSystem.AbilityReadyState.Ready);

            Color bg;
            Color txtCol;
            Color rimCol;
            string label;

            switch (state)
            {
                case CombatSystem.AbilityReadyState.Ready:
                    bg = new Color(0.08f, 0.58f, 0.90f, 0.98f);
                    txtCol = new Color(1f, 0.96f, 0.72f);
                    rimCol = new Color(1f, 0.88f, 0.35f, 1f);
                    label = $"⚡ SKILL ({cost}⚡)";
                    break;
                case CombatSystem.AbilityReadyState.AlreadyUsed:
                    bg = new Color(0.14f, 0.24f, 0.18f, 0.94f);
                    txtCol = new Color(0.65f, 0.90f, 0.72f);
                    rimCol = new Color(0.35f, 0.65f, 0.45f, 0.75f);
                    label = "✓ USED";
                    break;
                case CombatSystem.AbilityReadyState.NotEnoughPower:
                    bg = new Color(0.16f, 0.18f, 0.26f, 0.94f);
                    txtCol = new Color(1f, 0.80f, 0.45f);
                    rimCol = new Color(0.85f, 0.58f, 0.22f, 0.80f);
                    label = $"⚡ NEED {cost}⚡";
                    break;
                case CombatSystem.AbilityReadyState.NoValidTarget:
                    bg = new Color(0.22f, 0.17f, 0.14f, 0.94f);
                    txtCol = new Color(1f, 0.75f, 0.50f);
                    rimCol = new Color(0.80f, 0.45f, 0.25f, 0.75f);
                    label = "🎯 NO TARGET";
                    break;
                case CombatSystem.AbilityReadyState.Silenced:
                    bg = new Color(0.24f, 0.14f, 0.30f, 0.94f);
                    txtCol = new Color(0.85f, 0.70f, 1f);
                    rimCol = new Color(0.65f, 0.35f, 0.85f, 0.80f);
                    label = "🔒 SILENCED";
                    break;
                case CombatSystem.AbilityReadyState.Frozen:
                    bg = new Color(0.14f, 0.24f, 0.32f, 0.94f);
                    txtCol = new Color(0.70f, 0.92f, 1f);
                    rimCol = new Color(0.45f, 0.80f, 0.95f, 0.80f);
                    label = "❄️ FROZEN";
                    break;
                default:
                    bg = new Color(0.15f, 0.16f, 0.22f, 0.92f);
                    txtCol = new Color(0.75f, 0.80f, 0.90f);
                    rimCol = new Color(0.45f, 0.50f, 0.65f, 0.70f);
                    label = $"⏳ SKILL ({cost}⚡)";
                    break;
            }

            if (abilityButtonImage != null) abilityButtonImage.color = bg;
            if (abilityButtonOutline != null) abilityButtonOutline.effectColor = rimCol;
            if (abilityButtonText != null)
            {
                abilityButtonText.text = label;
                abilityButtonText.color = txtCol;
            }
        }

        private void EnsureFieldTokenCreated()
        {
            if (fieldCardToken != null) return;

            // Astral Masters Style Full-Art Field Token (148x184)
            fieldCardToken = GameBootstrapper.CreatePanel(transform, "FieldCardToken", new Color(0.04f, 0.05f, 0.08f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(148, 184));

            tokenOutline = fieldCardToken.AddComponent<Outline>();
            tokenOutline.effectColor = new Color(0.95f, 0.85f, 0.3f, 0.95f);
            tokenOutline.effectDistance = new Vector2(2f, 2f);
            tokenOutline.enabled = false;

            pulseEffect = fieldCardToken.AddComponent<CardPulseEffect>();

            // 1. Card Art Image Layer
            var artObj = new GameObject("MonsterArt");
            artObj.transform.SetParent(fieldCardToken.transform, false);
            var rectArt = artObj.AddComponent<RectTransform>();
            rectArt.anchorMin = Vector2.zero;
            rectArt.anchorMax = Vector2.one;
            rectArt.sizeDelta = Vector2.zero;
            rectArt.anchoredPosition = Vector2.zero;
            tokenArtImage = artObj.AddComponent<Image>();
            tokenArtImage.color = new Color(0.08f, 0.10f, 0.16f, 0.96f);
            tokenArtImage.raycastTarget = false;

            // Central Monster ID Text (shown when no art image exists)
            tokenNameText = GameBootstrapper.CreateText(fieldCardToken.transform, "MonsterIdText", "0", 34,
                Color.white, TextAnchor.MiddleCenter, new Vector2(0, 6), new Vector2(140, 80));
            tokenNameText.fontStyle = FontStyle.Bold;
            tokenNameText.raycastTarget = false;

            // 2. Classic Hand-Crafted Frame Overlay
            var frameOverlayObj = new GameObject("FrameOverlay");
            frameOverlayObj.transform.SetParent(fieldCardToken.transform, false);
            var rectFrame = frameOverlayObj.AddComponent<RectTransform>();
            rectFrame.anchorMin = Vector2.zero;
            rectFrame.anchorMax = Vector2.one;
            rectFrame.sizeDelta = Vector2.zero;
            rectFrame.anchoredPosition = Vector2.zero;
            var frameImg = frameOverlayObj.AddComponent<Image>();
            tokenFrameImage = frameImg;
            Sprite frameSprite = MonsterArtLoader.GetCardFrameOverlaySprite();
            if (frameSprite != null)
            {
                frameImg.sprite = frameSprite;
                frameImg.color = Color.white;
                frameImg.preserveAspect = false;
            }
            frameImg.raycastTarget = false;

            // 3. Top-Center Summon Cost Number (Inscribed inside the top medallion socket)
            tokenCostText = GameBootstrapper.CreateText(fieldCardToken.transform, "CostText", "", 14,
                new Color(1f, 0.96f, 0.72f), TextAnchor.MiddleCenter, new Vector2(0, -11f), new Vector2(28, 22));
            tokenCostText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            tokenCostText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            tokenCostText.rectTransform.anchoredPosition = new Vector2(0, -11f);
            tokenCostText.fontStyle = FontStyle.Bold;
            tokenCostText.raycastTarget = false;
            GameBootstrapper.AddTextShadow(tokenCostText);

            // 4. Bottom-Left ATK Number (Inscribed inside the bottom-left riveted socket)
            tokenAtkText = GameBootstrapper.CreateText(fieldCardToken.transform, "AtkText", "0", 15,
                Color.white, TextAnchor.MiddleCenter, new Vector2(17.5f, 20.5f), new Vector2(26, 22));
            tokenAtkText.rectTransform.anchorMin = new Vector2(0f, 0f);
            tokenAtkText.rectTransform.anchorMax = new Vector2(0f, 0f);
            tokenAtkText.rectTransform.anchoredPosition = new Vector2(17.5f, 20.5f);
            tokenAtkText.fontStyle = FontStyle.Bold;
            tokenAtkText.raycastTarget = false;
            GameBootstrapper.AddTextShadow(tokenAtkText);

            // 5. Bottom-Right DEF Number (Inscribed inside the bottom-right riveted socket)
            tokenDefText = GameBootstrapper.CreateText(fieldCardToken.transform, "DefText", "0", 15,
                Color.white, TextAnchor.MiddleCenter, new Vector2(-17.5f, 20.5f), new Vector2(26, 22));
            tokenDefText.rectTransform.anchorMin = new Vector2(1f, 0f);
            tokenDefText.rectTransform.anchorMax = new Vector2(1f, 0f);
            tokenDefText.rectTransform.anchoredPosition = new Vector2(-17.5f, 20.5f);
            tokenDefText.fontStyle = FontStyle.Bold;
            tokenDefText.raycastTarget = false;
            GameBootstrapper.AddTextShadow(tokenDefText);

            // Ensure all child graphics inside fieldCardToken never block or split pointer raycasts on the slot
            foreach (var g in fieldCardToken.GetComponentsInChildren<Graphic>(true))
            {
                if (g != null) g.raycastTarget = false;
            }
        }

        public void ClearDisplay()
        {
            currentCardData = null;
            currentMonsterInstance = null;
            isHighlightedForSummon = false;
            isAbilityReady = false;
            isSelectedOnBoard = false;
            if (abilityButton != null)
            {
                abilityButton.gameObject.SetActive(false);
            }
            if (fieldCardToken != null)
            {
                fieldCardToken.SetActive(false);
            }
            if (summoningCircleImage != null)
            {
                summoningCircleImage.gameObject.SetActive(true);
                summoningCircleImage.color = new Color(1f, 1f, 1f, 0.60f);
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

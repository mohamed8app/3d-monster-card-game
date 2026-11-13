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
        private MonsterStandbyAnimator standbyAnimator;
        private GameObject auraRoot;
        private Image auraGlowImage;
        private Image auraRuneRingImage;
        private GameObject abilitySigilObj;
        private Button abilitySigilButton;
        private Image sigilRingImage;
        private Image sigilFlashImage;
        private Image sigilSwordsImage;
        private Image sigilPipBgImage;
        private Text sigilText;
        private Color activeAuraColor = new Color(0.35f, 0.85f, 1f);
        private bool isAbilityReady = false;
        private bool isSigilHovered = false;
        private bool isSelectedOnBoard = false;
        private bool isHovered = false;
        private bool isHighlightedForSummon = false;
        private bool isFlightHidden = false;
        private CanvasGroup tokenCanvasGroup;
        private Coroutine hitFlashCoroutine;
        private float circleRotation = 0f;
        private float auraRotation = 0f;

        public void SetSummonHighlight(bool active)
        {
            isHighlightedForSummon = active && (currentCardData == null) && isPlayerSide;
        }

        /// <summary>
        /// Temporarily hides the landed token & aura while the card is flying from hand/deck into this slot.
        /// Keeps the summoning circle visible until touch-down so no sudden flash occurs.
        /// </summary>
        public void SetFlightHidden(bool hidden)
        {
            isFlightHidden = hidden;
            if (fieldCardToken != null && tokenCanvasGroup == null)
            {
                tokenCanvasGroup = fieldCardToken.GetComponent<CanvasGroup>() ?? fieldCardToken.AddComponent<CanvasGroup>();
            }

            if (hidden)
            {
                if (tokenCanvasGroup != null) tokenCanvasGroup.alpha = 0f;
                if (auraRoot != null) auraRoot.SetActive(false);
                if (abilitySigilObj != null) abilitySigilObj.SetActive(false);
                if (summoningCircleImage != null) summoningCircleImage.gameObject.SetActive(true);
            }
            else
            {
                if (tokenCanvasGroup != null) tokenCanvasGroup.alpha = 1f;
                if (currentCardData != null && summoningCircleImage != null)
                {
                    summoningCircleImage.gameObject.SetActive(false);
                }
                if (currentMonsterInstance != null && currentCardData != null)
                {
                    UpdateAbilityAuraAndSigil(currentMonsterInstance, MonsterArtLoader.GetElementColor(currentCardData.element));
                }
            }
        }

        public void SetBoardSelected(bool selected)
        {
            isSelectedOnBoard = selected && (currentCardData != null);
            RefreshTokenOutline();
        }

        private void RefreshTokenOutline()
        {
            if (tokenOutline == null) return;
            if (isSelectedOnBoard)
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
            if (BattleUIManager.Instance == null) return;

            if (eventData != null && eventData.clickCount >= 2 && isPlayerSide &&
                currentMonsterInstance != null && currentMonsterInstance.HasActiveSkill)
            {
                BattleUIManager.Instance.OnSlotAbilityButtonClicked(slotIndex);
                return;
            }

            BattleUIManager.Instance.OnSlotClicked(slotIndex, isPlayerSide);
        }

        public void SetSigilHovered(bool hovered)
        {
            isSigilHovered = hovered;
            if (hovered && currentCardData != null && BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.ShowMonsterDetails(currentCardData, currentMonsterInstance, isPlayerSide);
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
            // 1. Occupied Slot: Static Card Scale (Rock-solid for Ability Sigil) + Steady Aura & Flash Glow
            if (fieldCardToken != null && fieldCardToken.activeSelf)
            {
                fieldCardToken.transform.localScale = Vector3.one;

                if (isAbilityReady)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.2f);
                    float flashPulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6.5f);
                    float speedMult = (isHovered || isSigilHovered) ? 1.8f : 1.0f;
                    auraRotation = (auraRotation + 28f * speedMult * Time.unscaledDeltaTime) % 360f;

                    if (auraRoot != null && auraRoot.activeSelf)
                    {
                        if (auraGlowImage != null)
                        {
                            auraGlowImage.transform.localScale = Vector3.one;
                            Color gc = Color.Lerp(activeAuraColor, Color.white, 0.15f + 0.20f * pulse);
                            gc.a = (isHovered || isSigilHovered) ? 0.96f : Mathf.Lerp(0.58f, 0.90f, pulse);
                            auraGlowImage.color = gc;
                        }
                        if (auraRuneRingImage != null)
                        {
                            auraRuneRingImage.transform.localRotation = Quaternion.Euler(0f, 0f, auraRotation);
                            Color rc = Color.Lerp(activeAuraColor, new Color(0.84f, 0.92f, 1f), 0.35f);
                            rc.a = (isHovered || isSigilHovered) ? 0.85f : Mathf.Lerp(0.38f, 0.68f, pulse);
                            auraRuneRingImage.color = rc;
                        }
                    }

                    if (abilitySigilObj != null && abilitySigilObj.activeSelf)
                    {
                        abilitySigilObj.transform.localScale = Vector3.one;

                        if (sigilRingImage != null)
                        {
                            sigilRingImage.transform.localRotation = Quaternion.Euler(0f, 0f, -auraRotation * 3.0f);
                            Color src = Color.Lerp(activeAuraColor, Color.white, isSigilHovered ? 0.70f : 0.45f);
                            src.a = isSigilHovered ? 1.0f : Mathf.Lerp(0.75f, 1.0f, flashPulse);
                            sigilRingImage.color = src;
                        }
                        if (sigilFlashImage != null)
                        {
                            Color fc = Color.Lerp(activeAuraColor, Color.white, isSigilHovered ? 0.65f : 0.30f);
                            fc.a = isSigilHovered ? 0.72f : Mathf.Lerp(0.32f, 0.58f, flashPulse);
                            sigilFlashImage.color = fc;
                        }
                        if (sigilSwordsImage != null)
                        {
                            Color sc = Color.Lerp(new Color(0.82f, 0.90f, 0.98f), Color.white, isSigilHovered ? 0.95f : (0.35f + 0.55f * flashPulse));
                            sc.a = 1.0f;
                            sigilSwordsImage.color = sc;
                        }
                    }
                }
                else if (abilitySigilObj != null && abilitySigilObj.activeSelf)
                {
                    abilitySigilObj.transform.localScale = Vector3.one;
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
                    if (standbyAnimator == null)
                    {
                        standbyAnimator = tokenArtImage.gameObject.GetComponent<MonsterStandbyAnimator>()
                            ?? tokenArtImage.gameObject.AddComponent<MonsterStandbyAnimator>();
                    }
                    standbyAnimator.Configure(data, false);
                }
                else
                {
                    tokenArtImage.sprite = null;
                    tokenArtImage.color = bgCol;
                    standbyAnimator?.ClearStandby();
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

            // 5. Ready-to-attack breathing pulse disabled (cards remain completely steady)
            if (pulseEffect != null)
            {
                pulseEffect.SetPulsing(false, elemColor);
            }
            if (fieldCardToken != null)
            {
                fieldCardToken.transform.localScale = Vector3.one;
            }
            RefreshTokenOutline();

            // 6. Living Elemental Aura & Clickable Star-Flash Sigil on Player Board Monsters
            UpdateAbilityAuraAndSigil(instance, elemColor);

            if (isFlightHidden)
            {
                SetFlightHidden(true);
            }
        }

        private void EnsureAbilityAuraAndSigilCreated()
        {
            if (auraRoot == null)
            {
                auraRoot = new GameObject("AbilityElementalAura");
                auraRoot.transform.SetParent(transform, false);
                auraRoot.transform.SetAsFirstSibling();

                var auraRect = auraRoot.AddComponent<RectTransform>();
                auraRect.anchorMin = new Vector2(0.5f, 0.5f);
                auraRect.anchorMax = new Vector2(0.5f, 0.5f);
                auraRect.sizeDelta = new Vector2(194f, 232f);
                auraRect.anchoredPosition = Vector2.zero;

                var glowObj = new GameObject("AuraBackGlow");
                glowObj.transform.SetParent(auraRoot.transform, false);
                var glowRect = glowObj.AddComponent<RectTransform>();
                glowRect.anchorMin = Vector2.zero;
                glowRect.anchorMax = Vector2.one;
                glowRect.sizeDelta = Vector2.zero;
                auraGlowImage = glowObj.AddComponent<Image>();
                auraGlowImage.sprite = GameBootstrapper.GetElementalAuraHaloSprite();
                auraGlowImage.raycastTarget = false;

                var ringObj = new GameObject("AuraRuneRing");
                ringObj.transform.SetParent(auraRoot.transform, false);
                var ringRect = ringObj.AddComponent<RectTransform>();
                ringRect.anchorMin = new Vector2(0.5f, 0.5f);
                ringRect.anchorMax = new Vector2(0.5f, 0.5f);
                ringRect.sizeDelta = new Vector2(192f, 192f);
                auraRuneRingImage = ringObj.AddComponent<Image>();
                auraRuneRingImage.sprite = GameBootstrapper.GetSummoningCircleSprite();
                auraRuneRingImage.raycastTarget = false;

                auraRoot.SetActive(false);
            }

            if (abilitySigilObj == null)
            {
                abilitySigilObj = new GameObject($"AbilitySigilFlash_{slotIndex}");
                abilitySigilObj.transform.SetParent(transform, false);

                var sigilRect = abilitySigilObj.AddComponent<RectTransform>();
                sigilRect.anchorMin = new Vector2(0.5f, 0.5f);
                sigilRect.anchorMax = new Vector2(0.5f, 0.5f);
                sigilRect.sizeDelta = new Vector2(44f, 44f);
                sigilRect.anchoredPosition = new Vector2(0f, -72f);

                var hitImg = abilitySigilObj.AddComponent<Image>();
                hitImg.sprite = GameBootstrapper.GetCircleSprite();
                hitImg.color = new Color(0.05f, 0.08f, 0.14f, 0.72f);
                hitImg.raycastTarget = true;

                abilitySigilButton = abilitySigilObj.AddComponent<Button>();
                abilitySigilButton.transition = Selectable.Transition.None;
                abilitySigilButton.onClick.AddListener(() =>
                {
                    if (BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.OnSlotAbilityButtonClicked(slotIndex);
                    }
                });

                var hoverRelay = abilitySigilObj.AddComponent<AbilitySigilHoverRelay>();
                hoverRelay.ownerSlot = this;

                var ringObj = new GameObject("SigilSpinRing");
                ringObj.transform.SetParent(abilitySigilObj.transform, false);
                var rRect = ringObj.AddComponent<RectTransform>();
                rRect.anchorMin = new Vector2(0.5f, 0.5f);
                rRect.anchorMax = new Vector2(0.5f, 0.5f);
                rRect.sizeDelta = new Vector2(54f, 54f);
                sigilRingImage = ringObj.AddComponent<Image>();
                sigilRingImage.sprite = GameBootstrapper.GetSummoningCircleSprite();
                sigilRingImage.raycastTarget = false;

                var flashObj = new GameObject("SigilFlashCore");
                flashObj.transform.SetParent(abilitySigilObj.transform, false);
                var fRect = flashObj.AddComponent<RectTransform>();
                fRect.anchorMin = new Vector2(0.5f, 0.5f);
                fRect.anchorMax = new Vector2(0.5f, 0.5f);
                fRect.sizeDelta = new Vector2(46f, 46f);
                sigilFlashImage = flashObj.AddComponent<Image>();
                sigilFlashImage.sprite = GameBootstrapper.GetAbilityFlashCoreSprite();
                sigilFlashImage.raycastTarget = false;

                var swordsObj = new GameObject("SigilCrossedSwords");
                swordsObj.transform.SetParent(abilitySigilObj.transform, false);
                var swRect = swordsObj.AddComponent<RectTransform>();
                swRect.anchorMin = new Vector2(0.5f, 0.5f);
                swRect.anchorMax = new Vector2(0.5f, 0.5f);
                swRect.sizeDelta = new Vector2(30f, 30f);
                sigilSwordsImage = swordsObj.AddComponent<Image>();
                sigilSwordsImage.sprite = GameBootstrapper.GetCrossedSwordsSprite();
                sigilSwordsImage.raycastTarget = false;

                var pipObj = GameBootstrapper.CreateCirclePanel(abilitySigilObj.transform, "SigilCostPip",
                    new Color(0.06f, 0.10f, 0.18f, 0.96f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(16f, -14f), new Vector2(18f, 18f));
                sigilPipBgImage = pipObj.GetComponent<Image>();
                sigilPipBgImage.raycastTarget = false;
                var pipOutline = pipObj.AddComponent<Outline>();
                pipOutline.effectColor = new Color(0.55f, 0.65f, 0.76f, 0.85f);
                pipOutline.effectDistance = new Vector2(1f, 1f);

                sigilText = GameBootstrapper.CreateText(pipObj.transform, "SigilCostText", "1", 10,
                    Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(18f, 18f));
                sigilText.fontStyle = FontStyle.Bold;
                sigilText.raycastTarget = false;
                GameBootstrapper.AddTextShadow(sigilText);

                abilitySigilObj.SetActive(false);
            }
        }

        private void UpdateAbilityAuraAndSigil(MonsterInstance instance, Color elemColor)
        {
            if (!isPlayerSide || instance == null || !instance.IsAlive || !instance.HasActiveSkill)
            {
                isAbilityReady = false;
                if (auraRoot != null) auraRoot.SetActive(false);
                if (abilitySigilObj != null) abilitySigilObj.SetActive(false);
                return;
            }

            EnsureAbilityAuraAndSigilCreated();

            // Guarantee correct layering: Aura behind card -> FieldCardToken -> Flash Sigil on front rim
            if (auraRoot != null && fieldCardToken != null)
            {
                auraRoot.transform.SetAsFirstSibling();
                fieldCardToken.transform.SetSiblingIndex(auraRoot.transform.GetSiblingIndex() + 1);
            }
            if (abilitySigilObj != null)
            {
                abilitySigilObj.transform.SetAsLastSibling();
            }

            activeAuraColor = elemColor;

            var gm = GameManager.Instance;
            if (gm == null || CombatSystem.Instance == null)
            {
                isAbilityReady = false;
                if (auraRoot != null) auraRoot.SetActive(false);
                if (abilitySigilObj != null) abilitySigilObj.SetActive(false);
                return;
            }

            var state = CombatSystem.Instance.GetAbilityState(gm.humanPlayer, gm.aiPlayer, slotIndex, out int cost, out string _);
            isAbilityReady = (state == CombatSystem.AbilityReadyState.Ready);

            if (auraRoot != null)
            {
                auraRoot.SetActive(isAbilityReady);
            }

            if (abilitySigilObj != null)
            {
                abilitySigilObj.SetActive(true);
            }

            if (isAbilityReady)
            {
                if (sigilText != null)
                {
                    sigilText.text = cost.ToString();
                    sigilText.color = new Color(0.90f, 0.95f, 1f);
                }
                if (sigilPipBgImage != null)
                {
                    sigilPipBgImage.color = new Color(0.10f, 0.28f, 0.52f, 0.98f);
                }
                if (sigilFlashImage != null)
                {
                    sigilFlashImage.color = Color.Lerp(elemColor, Color.white, 0.35f);
                }
                if (sigilSwordsImage != null)
                {
                    sigilSwordsImage.color = new Color(0.86f, 0.94f, 1f, 1f);
                }
                if (sigilRingImage != null)
                {
                    sigilRingImage.color = new Color(0.52f, 0.80f, 0.96f, 0.88f);
                }
            }
            else
            {
                abilitySigilObj.transform.localScale = Vector3.one;
                Color dimCol;
                string glyph;

                switch (state)
                {
                    case CombatSystem.AbilityReadyState.AlreadyUsed:
                        dimCol = new Color(0.28f, 0.55f, 0.40f, 0.40f);
                        glyph = "✓";
                        break;
                    case CombatSystem.AbilityReadyState.NotEnoughMana:
                        dimCol = new Color(0.28f, 0.56f, 0.88f, 0.48f);
                        glyph = cost.ToString();
                        break;
                    case CombatSystem.AbilityReadyState.NotEnoughPower:
                        dimCol = new Color(0.75f, 0.55f, 0.25f, 0.48f);
                        glyph = cost.ToString();
                        break;
                    case CombatSystem.AbilityReadyState.NoValidTarget:
                        dimCol = new Color(0.85f, 0.45f, 0.25f, 0.48f);
                        glyph = "🎯";
                        break;
                    case CombatSystem.AbilityReadyState.Silenced:
                        dimCol = new Color(0.55f, 0.30f, 0.75f, 0.48f);
                        glyph = "🔒";
                        break;
                    case CombatSystem.AbilityReadyState.Frozen:
                        dimCol = new Color(0.35f, 0.75f, 0.95f, 0.48f);
                        glyph = "❄️";
                        break;
                    default:
                        dimCol = new Color(0.45f, 0.50f, 0.65f, 0.38f);
                        glyph = "⏳";
                        break;
                }

                if (sigilFlashImage != null) sigilFlashImage.color = new Color(dimCol.r, dimCol.g, dimCol.b, 0.18f);
                if (sigilSwordsImage != null) sigilSwordsImage.color = new Color(0.72f, 0.75f, 0.82f, 0.62f);
                if (sigilRingImage != null) sigilRingImage.color = new Color(dimCol.r, dimCol.g, dimCol.b, 0.35f);
                if (sigilPipBgImage != null) sigilPipBgImage.color = new Color(0.08f, 0.09f, 0.14f, 0.92f);
                if (sigilText != null)
                {
                    sigilText.text = glyph;
                    sigilText.color = new Color(0.92f, 0.92f, 0.96f, 0.85f);
                }
            }
        }

        private void EnsureFieldTokenCreated()
        {
            if (fieldCardToken != null) return;

            // Astral Masters Style Full-Art Field Token (148x184)
            fieldCardToken = GameBootstrapper.CreatePanel(transform, "FieldCardToken", new Color(0.04f, 0.05f, 0.08f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(148, 184));
            tokenCanvasGroup = fieldCardToken.AddComponent<CanvasGroup>();

            tokenOutline = fieldCardToken.AddComponent<Outline>();
            tokenOutline.effectColor = new Color(0.48f, 0.68f, 0.82f, 0.90f);
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
            standbyAnimator = artObj.AddComponent<MonsterStandbyAnimator>();

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
                new Color(0.92f, 0.95f, 1f), TextAnchor.MiddleCenter, new Vector2(0, -11f), new Vector2(28, 22));
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

        public void SetStandbyPaused(bool paused)
        {
            if (standbyAnimator != null)
            {
                standbyAnimator.SetStandbyPaused(paused);
            }
        }

        /// <summary>
        /// Shakes and flashes only the live monster token artwork/frame (never tints the empty slot backdrop red),
        /// and cancels cleanly on monster death in ClearDisplay().
        /// </summary>
        public void PlayHitFlashAndShake(Color flashColor, float intensity = 10f)
        {
            if (hitFlashCoroutine != null)
            {
                StopCoroutine(hitFlashCoroutine);
                hitFlashCoroutine = null;
                ResetHitFlashVisuals();
            }

            if (fieldCardToken == null || !fieldCardToken.activeSelf || !gameObject.activeInHierarchy)
            {
                ResetHitFlashVisuals();
                return;
            }

            hitFlashCoroutine = StartCoroutine(HitFlashAndShakeRoutine(flashColor, intensity));
        }

        private System.Collections.IEnumerator HitFlashAndShakeRoutine(Color flashColor, float intensity)
        {
            float duration = 0.28f;
            float t = 0f;

            while (t < duration && fieldCardToken != null && fieldCardToken.activeSelf && currentMonsterInstance != null)
            {
                t += Mathf.Min(Time.deltaTime, 0.05f);
                float p = Mathf.Clamp01(1f - (t / duration));
                float offsetX = Mathf.Sin(t * 50f) * intensity * p;
                fieldCardToken.transform.localPosition = new Vector3(offsetX, 0f, 0f);

                if (tokenArtImage != null)
                {
                    tokenArtImage.color = Color.Lerp(Color.white, flashColor, p * 0.65f);
                }
                if (tokenFrameImage != null)
                {
                    tokenFrameImage.color = Color.Lerp(Color.white, flashColor, p * 0.70f);
                }
                yield return null;
            }

            ResetHitFlashVisuals();
            hitFlashCoroutine = null;
        }

        private void ResetHitFlashVisuals()
        {
            var slotBg = GetComponent<Image>();
            if (slotBg != null) slotBg.color = Color.clear;

            if (fieldCardToken != null)
            {
                fieldCardToken.transform.localPosition = Vector3.zero;
            }
            if (tokenArtImage != null)
            {
                tokenArtImage.color = (tokenArtImage.sprite != null)
                    ? Color.white
                    : (currentCardData != null ? MonsterArtLoader.GetEnvironmentAtmosphereColor(currentCardData.element) : Color.white);
            }
            if (tokenFrameImage != null)
            {
                tokenFrameImage.color = Color.white;
            }
        }

        public void ClearDisplay()
        {
            if (hitFlashCoroutine != null)
            {
                StopCoroutine(hitFlashCoroutine);
                hitFlashCoroutine = null;
            }
            currentCardData = null;
            currentMonsterInstance = null;
            isHighlightedForSummon = false;
            isFlightHidden = false;
            if (tokenCanvasGroup != null) tokenCanvasGroup.alpha = 1f;
            isAbilityReady = false;
            isSigilHovered = false;
            isSelectedOnBoard = false;
            standbyAnimator?.ClearStandby();
            ResetHitFlashVisuals();
            if (auraRoot != null)
            {
                auraRoot.SetActive(false);
            }
            if (abilitySigilObj != null)
            {
                abilitySigilObj.SetActive(false);
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

    public class AbilitySigilHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public SlotView ownerSlot;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (ownerSlot != null) ownerSlot.SetSigilHovered(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (ownerSlot != null) ownerSlot.SetSigilHovered(false);
        }
    }
}

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
        public Image innerRuneRingImage;
        public Image slotAuraFlare;
        public Image slotFlareCore;
        private float innerCircleRotation = 0f;
        private GameObject fieldCardToken;
        private Image tokenArtImage;
        private Image tokenFrameImage;
        [NonSerialized] private Image tokenClanEmblemImage;
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
        [NonSerialized] private GameObject abilitySigil2Obj;
        [NonSerialized] private Button abilitySigil2Button;
        [NonSerialized] private Image sigil2RingImage;
        [NonSerialized] private Image sigil2FlashImage;
        [NonSerialized] private Image sigil2SwordsImage;
        [NonSerialized] private Image sigil2PipBgImage;
        [NonSerialized] private Text sigil2Text;
        [NonSerialized] private bool isAbility2Ready = false;
        [NonSerialized] private bool isSigil2Hovered = false;
        [NonSerialized] private GameObject targetReticleBadge;
        [NonSerialized] private Image targetReticleBg;
        [NonSerialized] private Text targetReticleText;
        [NonSerialized] private bool isTargetedForAbility = false;
        [NonSerialized] private bool isEnemyAbilityTarget = true;
        [NonSerialized] private bool isTargetedForEmptySlot = false;
        [NonSerialized] private GameObject skillCastBadgeObj;
        [NonSerialized] private Image skillCastBadgeBg;
        [NonSerialized] private Outline skillCastBadgeOutline;
        [NonSerialized] private Text skillCastBadgeText;
        [NonSerialized] private Coroutine skillCastHighlightCoroutine;
        [NonSerialized] private bool isFlashingSkillCast = false;
        [NonSerialized] private Color flashingSkillCastColor = new Color(0.15f, 0.84f, 1.0f, 1f);
        public static readonly Color Skill1BlueColor = new Color(0.15f, 0.84f, 1.0f, 1f);
        public static readonly Color Skill2RedColor = new Color(1.0f, 0.24f, 0.34f, 1f);
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
                tokenCanvasGroup = fieldCardToken.GetComponent<CanvasGroup>();
                if (tokenCanvasGroup == null) tokenCanvasGroup = fieldCardToken.AddComponent<CanvasGroup>();
            }

            if (hidden)
            {
                if (tokenCanvasGroup != null) tokenCanvasGroup.alpha = 0f;
                if (auraRoot != null) auraRoot.SetActive(false);
                if (abilitySigilObj != null) abilitySigilObj.SetActive(false);
                if (abilitySigil2Obj != null) abilitySigil2Obj.SetActive(false);
                if (skillCastBadgeObj != null) skillCastBadgeObj.SetActive(false);
                if (summoningCircleImage != null) summoningCircleImage.gameObject.SetActive(true);
                if (innerRuneRingImage != null) innerRuneRingImage.gameObject.SetActive(true);
                if (slotAuraFlare != null) slotAuraFlare.gameObject.SetActive(true);
                if (slotFlareCore != null) slotFlareCore.gameObject.SetActive(true);
            }
            else
            {
                if (tokenCanvasGroup != null) tokenCanvasGroup.alpha = 1f;
                if (currentCardData != null)
                {
                    if (summoningCircleImage != null) summoningCircleImage.gameObject.SetActive(false);
                    if (innerRuneRingImage != null) innerRuneRingImage.gameObject.SetActive(false);
                    if (slotFlareCore != null) slotFlareCore.gameObject.SetActive(false);
                    if (slotAuraFlare != null) slotAuraFlare.color = new Color(1f, 1f, 1f, 0.12f);
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

        public void SetAbilityTargetHighlight(bool active, bool enemyTarget = true)
        {
            isTargetedForAbility = active && (currentCardData != null) && (currentMonsterInstance != null) && currentMonsterInstance.IsAlive;
            if (isTargetedForAbility) isTargetedForEmptySlot = false;
            isEnemyAbilityTarget = enemyTarget;
            EnsureTargetReticleCreated();
            if (targetReticleBadge != null)
            {
                bool showBadge = isTargetedForAbility || isTargetedForEmptySlot;
                targetReticleBadge.SetActive(showBadge);
                if (isTargetedForAbility)
                {
                    targetReticleBadge.transform.SetAsLastSibling();
                    if (targetReticleBg != null)
                    {
                        targetReticleBg.color = enemyTarget
                            ? new Color(0.78f, 0.12f, 0.18f, 0.95f)
                            : new Color(0.10f, 0.56f, 0.34f, 0.95f);
                    }
                    if (targetReticleText != null)
                    {
                        targetReticleText.text = enemyTarget ? "🎯 CLICK TARGET" : "✨ CLICK ALLY";
                        targetReticleText.color = Color.white;
                    }
                }
            }
            UpdatePersistentSkillCastBadge();
            RefreshTokenOutline();
        }

        public void SetEmptySlotTargetHighlight(bool active, string badgeLabel = "📍 PLACE HERE")
        {
            isTargetedForEmptySlot = active && (currentCardData == null) && isPlayerSide;
            if (isTargetedForEmptySlot) isTargetedForAbility = false;
            EnsureTargetReticleCreated();
            if (targetReticleBadge != null)
            {
                bool showBadge = isTargetedForEmptySlot || isTargetedForAbility;
                targetReticleBadge.SetActive(showBadge);
                if (isTargetedForEmptySlot)
                {
                    targetReticleBadge.transform.SetAsLastSibling();
                    if (targetReticleBg != null)
                    {
                        targetReticleBg.color = new Color(0.08f, 0.56f, 0.46f, 0.96f);
                    }
                    if (targetReticleText != null)
                    {
                        targetReticleText.text = string.IsNullOrEmpty(badgeLabel) ? "📍 PLACE HERE" : badgeLabel;
                        targetReticleText.color = Color.white;
                    }
                }
            }
            RefreshTokenOutline();
        }

        private void EnsureTargetReticleCreated()
        {
            if (targetReticleBadge != null) return;
            targetReticleBadge = GameBootstrapper.CreatePanel(transform, "TargetReticleBadge",
                new Color(0.78f, 0.12f, 0.18f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 76f), new Vector2(132f, 24f));
            targetReticleBg = targetReticleBadge.GetComponent<Image>();
            targetReticleBg.raycastTarget = false;
            var outline = targetReticleBadge.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.88f, 0.35f, 1f);
            outline.effectDistance = new Vector2(1.5f, 1.5f);

            targetReticleText = GameBootstrapper.CreateText(targetReticleBadge.transform, "TargetReticleText",
                "🎯 CLICK TARGET", 11, Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(128f, 22f));
            targetReticleText.fontStyle = FontStyle.Bold;
            targetReticleText.raycastTarget = false;
            GameBootstrapper.AddTextShadow(targetReticleText);
            targetReticleBadge.SetActive(false);
        }

        private void EnsureSkillCastBadgeCreated()
        {
            if (skillCastBadgeObj != null) return;
            skillCastBadgeObj = GameBootstrapper.CreatePanel(transform, "SkillCastBadge",
                new Color(0.06f, 0.36f, 0.68f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 76f), new Vector2(142f, 24f));
            skillCastBadgeBg = skillCastBadgeObj.GetComponent<Image>();
            skillCastBadgeBg.raycastTarget = false;
            skillCastBadgeOutline = skillCastBadgeObj.AddComponent<Outline>();
            skillCastBadgeOutline.effectColor = new Color(0.35f, 0.92f, 1f, 1f);
            skillCastBadgeOutline.effectDistance = new Vector2(1.5f, 1.5f);

            skillCastBadgeText = GameBootstrapper.CreateText(skillCastBadgeObj.transform, "SkillCastText",
                "⚡🔵 SKILL USED", 10, Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(138f, 22f));
            skillCastBadgeText.fontStyle = FontStyle.Bold;
            skillCastBadgeText.raycastTarget = false;
            GameBootstrapper.AddTextShadow(skillCastBadgeText);
            skillCastBadgeObj.SetActive(false);
        }

        public void PlaySkillCastHighlight(int skillIndex, string shortSkillName)
        {
            if (!gameObject.activeInHierarchy || currentMonsterInstance == null || !currentMonsterInstance.IsAlive)
                return;

            EnsureSkillCastBadgeCreated();
            if (skillCastHighlightCoroutine != null)
            {
                StopCoroutine(skillCastHighlightCoroutine);
                skillCastHighlightCoroutine = null;
            }
            skillCastHighlightCoroutine = StartCoroutine(SkillCastHighlightRoutine(skillIndex, shortSkillName));
        }

        private System.Collections.IEnumerator SkillCastHighlightRoutine(int skillIndex, string shortSkillName)
        {
            Color accent = skillIndex == 0 ? Skill1BlueColor : Skill2RedColor;
            Color bgCol = skillIndex == 0
                ? new Color(0.05f, 0.34f, 0.66f, 0.98f)
                : new Color(0.68f, 0.10f, 0.18f, 0.98f);
            string emoji = skillIndex == 0 ? "🔵" : "🔴";

            isFlashingSkillCast = true;
            flashingSkillCastColor = accent;
            RefreshTokenOutline();

            if (skillCastBadgeObj != null)
            {
                skillCastBadgeObj.SetActive(!isTargetedForAbility);
                skillCastBadgeObj.transform.SetAsLastSibling();
                if (skillCastBadgeBg != null) skillCastBadgeBg.color = bgCol;
                if (skillCastBadgeOutline != null) skillCastBadgeOutline.effectColor = Color.Lerp(accent, Color.white, 0.35f);
                if (skillCastBadgeText != null)
                {
                    skillCastBadgeText.text = $"⚡{emoji} {shortSkillName}";
                    skillCastBadgeText.color = Color.white;
                }
            }

            GameObject activeSigil = (skillIndex == 1 && abilitySigil2Obj != null && abilitySigil2Obj.activeSelf)
                ? abilitySigil2Obj
                : abilitySigilObj;

            float duration = 1.6f;
            float elapsed = 0f;
            while (elapsed < duration && currentMonsterInstance != null && currentMonsterInstance.IsAlive)
            {
                elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                float wave = 0.5f + 0.5f * Mathf.Sin(elapsed * 14f);
                if (skillCastBadgeObj != null && !isTargetedForAbility)
                {
                    float s = 1f + 0.08f * Mathf.Sin(Mathf.Min(1f, elapsed / 0.35f) * Mathf.PI);
                    skillCastBadgeObj.transform.localScale = new Vector3(s, s, 1f);
                    if (skillCastBadgeOutline != null)
                    {
                        skillCastBadgeOutline.effectColor = Color.Lerp(accent, Color.white, 0.25f + 0.65f * wave);
                    }
                }
                if (activeSigil != null && activeSigil.activeSelf)
                {
                    float sigScale = 1f + 0.28f * Mathf.Sin(Mathf.Clamp01(elapsed / 0.55f) * Mathf.PI);
                    activeSigil.transform.localScale = new Vector3(sigScale, sigScale, 1f);
                }
                yield return null;
            }

            if (activeSigil != null) activeSigil.transform.localScale = Vector3.one;
            if (skillCastBadgeObj != null) skillCastBadgeObj.transform.localScale = Vector3.one;

            isFlashingSkillCast = false;
            skillCastHighlightCoroutine = null;
            UpdatePersistentSkillCastBadge();
            RefreshTokenOutline();
        }

        private void UpdatePersistentSkillCastBadge()
        {
            if (skillCastHighlightCoroutine != null) return;

            if (isTargetedForAbility || currentMonsterInstance == null || !currentMonsterInstance.IsAlive || !currentMonsterInstance.hasUsedActiveThisTurn)
            {
                if (skillCastBadgeObj != null) skillCastBadgeObj.SetActive(false);
                return;
            }

            EnsureSkillCastBadgeCreated();
            if (skillCastBadgeObj == null) return;

            bool used0 = currentMonsterInstance.IsActiveSkillUsed(0);
            bool used1 = currentMonsterInstance.ActiveSkillCount >= 2 && currentMonsterInstance.IsActiveSkillUsed(1);
            if (!used0 && !used1)
            {
                skillCastBadgeObj.SetActive(false);
                return;
            }

            string label;
            Color bgCol;
            Color borderCol;
            if (used0 && used1)
            {
                label = "✓ USED 🔵+🔴 SKILLS";
                bgCol = new Color(0.24f, 0.14f, 0.42f, 0.94f);
                borderCol = new Color(0.45f, 0.95f, 0.65f, 0.95f);
            }
            else if (used1)
            {
                string sName = CombatSystem.GetShortSkillName(currentMonsterInstance.actives[1]);
                label = $"✓🔴 {sName}";
                bgCol = new Color(0.48f, 0.10f, 0.16f, 0.92f);
                borderCol = new Color(1f, 0.42f, 0.50f, 0.92f);
            }
            else
            {
                string sName = CombatSystem.GetShortSkillName(currentMonsterInstance.actives[0]);
                label = $"✓🔵 {sName}";
                bgCol = new Color(0.06f, 0.28f, 0.52f, 0.92f);
                borderCol = new Color(0.35f, 0.88f, 1f, 0.92f);
            }

            skillCastBadgeObj.SetActive(true);
            skillCastBadgeObj.transform.localScale = Vector3.one;
            skillCastBadgeObj.transform.SetAsLastSibling();
            if (skillCastBadgeBg != null) skillCastBadgeBg.color = bgCol;
            if (skillCastBadgeOutline != null) skillCastBadgeOutline.effectColor = borderCol;
            if (skillCastBadgeText != null)
            {
                skillCastBadgeText.text = label;
                skillCastBadgeText.color = new Color(0.92f, 1f, 0.95f, 1f);
            }
        }

        private void RefreshTokenOutline()
        {
            if (tokenOutline == null) return;
            if (isTargetedForAbility)
            {
                tokenOutline.enabled = true;
                tokenOutline.effectColor = isEnemyAbilityTarget
                    ? new Color(1f, 0.25f, 0.30f, 1f)
                    : new Color(0.25f, 1f, 0.55f, 1f);
                tokenOutline.effectDistance = new Vector2(3f, 3f);
            }
            else if (isFlashingSkillCast)
            {
                tokenOutline.enabled = true;
                tokenOutline.effectColor = flashingSkillCastColor;
                tokenOutline.effectDistance = new Vector2(3f, 3f);
            }
            else if (isSelectedOnBoard)
            {
                tokenOutline.enabled = true;
                tokenOutline.effectColor = new Color(0.32f, 0.92f, 1f, 0.98f);
                tokenOutline.effectDistance = new Vector2(2f, 2f);
            }
            else
            {
                tokenOutline.enabled = false;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (BattleUIManager.Instance == null) return;

            // If we are currently selecting a target for an active ability or spell, single-click immediately selects this slot as the target!
            if (BattleUIManager.Instance.IsSelectingAnyTarget)
            {
                BattleUIManager.Instance.OnSlotClicked(slotIndex, isPlayerSide);
                return;
            }

            if (eventData != null && eventData.clickCount >= 2 && isPlayerSide &&
                currentMonsterInstance != null && currentMonsterInstance.HasActiveSkill)
            {
                BattleUIManager.Instance.OnSlotAbilityButtonClicked(slotIndex, 0);
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

        public void SetSigil2Hovered(bool hovered)
        {
            isSigil2Hovered = hovered;
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

                if (isAbilityReady || isAbility2Ready)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.2f);
                    float flashPulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6.5f);
                    float speedMult = (isHovered || isSigilHovered || isSigil2Hovered) ? 1.8f : 1.0f;
                    auraRotation = (auraRotation + 28f * speedMult * Time.unscaledDeltaTime) % 360f;

                    if (auraRoot != null && auraRoot.activeSelf)
                    {
                        if (auraGlowImage != null)
                        {
                            auraGlowImage.transform.localScale = Vector3.one;
                            Color gc = Color.Lerp(activeAuraColor, Color.white, 0.15f + 0.20f * pulse);
                            gc.a = (isHovered || isSigilHovered || isSigil2Hovered) ? 0.96f : Mathf.Lerp(0.58f, 0.90f, pulse);
                            auraGlowImage.color = gc;
                        }
                        if (auraRuneRingImage != null)
                        {
                            auraRuneRingImage.transform.localRotation = Quaternion.Euler(0f, 0f, auraRotation);
                            Color rc = Color.Lerp(activeAuraColor, new Color(0.84f, 0.92f, 1f), 0.35f);
                            rc.a = (isHovered || isSigilHovered || isSigil2Hovered) ? 0.85f : Mathf.Lerp(0.38f, 0.68f, pulse);
                            auraRuneRingImage.color = rc;
                        }
                    }

                    // Skill 1 Sigil (🔵 BLUE)
                    if (abilitySigilObj != null && abilitySigilObj.activeSelf && isAbilityReady)
                    {
                        abilitySigilObj.transform.localScale = Vector3.one;

                        if (sigilRingImage != null)
                        {
                            sigilRingImage.transform.localRotation = Quaternion.Euler(0f, 0f, -auraRotation * 3.0f);
                            Color src = Color.Lerp(Skill1BlueColor, Color.white, isSigilHovered ? 0.65f : 0.30f);
                            src.a = isSigilHovered ? 1.0f : Mathf.Lerp(0.78f, 1.0f, flashPulse);
                            sigilRingImage.color = src;
                        }
                        if (sigilFlashImage != null)
                        {
                            Color fc = Color.Lerp(Skill1BlueColor, Color.white, isSigilHovered ? 0.55f : 0.22f);
                            fc.a = isSigilHovered ? 0.82f : Mathf.Lerp(0.42f, 0.72f, flashPulse);
                            sigilFlashImage.color = fc;
                        }
                        if (sigilSwordsImage != null)
                        {
                            Color sc = Color.Lerp(new Color(0.75f, 0.95f, 1f), Color.white, isSigilHovered ? 0.95f : (0.35f + 0.55f * flashPulse));
                            sc.a = 1.0f;
                            sigilSwordsImage.color = sc;
                        }
                    }

                    // Skill 2 Sigil (🔴 RED)
                    if (abilitySigil2Obj != null && abilitySigil2Obj.activeSelf && isAbility2Ready)
                    {
                        abilitySigil2Obj.transform.localScale = Vector3.one;

                        if (sigil2RingImage != null)
                        {
                            sigil2RingImage.transform.localRotation = Quaternion.Euler(0f, 0f, auraRotation * 3.0f);
                            Color src = Color.Lerp(Skill2RedColor, Color.white, isSigil2Hovered ? 0.65f : 0.30f);
                            src.a = isSigil2Hovered ? 1.0f : Mathf.Lerp(0.78f, 1.0f, flashPulse);
                            sigil2RingImage.color = src;
                        }
                        if (sigil2FlashImage != null)
                        {
                            Color fc = Color.Lerp(Skill2RedColor, Color.white, isSigil2Hovered ? 0.55f : 0.22f);
                            fc.a = isSigil2Hovered ? 0.82f : Mathf.Lerp(0.42f, 0.72f, flashPulse);
                            sigil2FlashImage.color = fc;
                        }
                        if (sigil2SwordsImage != null)
                        {
                            Color sc = Color.Lerp(new Color(1f, 0.80f, 0.82f), Color.white, isSigil2Hovered ? 0.95f : (0.35f + 0.55f * flashPulse));
                            sc.a = 1.0f;
                            sigil2SwordsImage.color = sc;
                        }
                    }
                }
                else
                {
                    if (abilitySigilObj != null && abilitySigilObj.activeSelf)
                        abilitySigilObj.transform.localScale = Vector3.one;
                    if (abilitySigil2Obj != null && abilitySigil2Obj.activeSelf)
                        abilitySigil2Obj.transform.localScale = Vector3.one;
                }
            }
            // 2. Empty Slot: Clean Summoning Dais with Calm Ambient Drift & Distinct Summoning Rotation
            else if (summoningCircleImage != null && summoningCircleImage.gameObject.activeSelf && currentCardData == null)
            {
                summoningCircleImage.transform.localScale = Vector3.one;

                float spinSpeed;
                float flareAlpha;
                float ringAlpha;
                Color activeAuraColor;

                if (isTargetedForEmptySlot)
                {
                    spinSpeed = isHovered ? 170f : 120f;
                    flareAlpha = isHovered ? 0.65f : 0.50f;
                    ringAlpha = isHovered ? 1.0f : 0.90f;
                    activeAuraColor = new Color(0.35f, 1.0f, 0.70f);
                }
                else if (isHighlightedForSummon)
                {
                    // Active Summoning Mode: Clean, distinct single-ring rotation
                    spinSpeed = isHovered ? 140f : 95f;
                    flareAlpha = isHovered ? 0.60f : 0.45f;
                    ringAlpha = isHovered ? 1.0f : 0.88f;
                    activeAuraColor = isPlayerSide
                        ? new Color(0.35f, 0.90f, 1.0f) // Celestial cyan
                        : new Color(1.0f, 0.40f, 0.45f); // Fiery ruby
                }
                else if (isHovered)
                {
                    // Direct mouse hover over slot
                    spinSpeed = 80f;
                    flareAlpha = 0.38f;
                    ringAlpha = 0.85f;
                    activeAuraColor = isPlayerSide
                        ? new Color(0.30f, 0.82f, 1.0f)
                        : new Color(0.95f, 0.35f, 0.45f);
                }
                else
                {
                    // Idle Breathing Glow: Calm, smooth ambient rotation (never dead/static, zero visual clutter)
                    float breatheSine = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.0f + slotIndex * 0.5f);
                    spinSpeed = 16f; // Calm 16 deg/sec ambient drift
                    flareAlpha = Mathf.Lerp(0.14f, 0.26f, breatheSine);
                    ringAlpha = Mathf.Lerp(0.50f, 0.72f, breatheSine);
                    activeAuraColor = isPlayerSide
                        ? new Color(0.24f, 0.68f, 1.0f)
                        : new Color(0.85f, 0.28f, 0.45f);
                }

                // Clean Single Runic Ring Rotation
                circleRotation = (circleRotation - spinSpeed * Time.unscaledDeltaTime) % 360f;
                summoningCircleImage.transform.localRotation = Quaternion.Euler(0, 0, circleRotation);
                summoningCircleImage.color = new Color(activeAuraColor.r, activeAuraColor.g, activeAuraColor.b, ringAlpha);

                // Subtle Atmospheric Background Lens Flare
                if (slotAuraFlare != null)
                {
                    slotAuraFlare.color = new Color(activeAuraColor.r, activeAuraColor.g, activeAuraColor.b, flareAlpha);
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

            Color elemColor = MonsterArtLoader.GetElementColor(data.element);
            Color bgCol = MonsterArtLoader.GetEnvironmentAtmosphereColor(data.element);

            if (slotNameText != null)
            {
                slotNameText.gameObject.SetActive(false);
            }
            if (summoningCircleImage != null)
            {
                summoningCircleImage.gameObject.SetActive(false);
            }
            if (innerRuneRingImage != null)
            {
                innerRuneRingImage.gameObject.SetActive(false);
            }
            if (slotFlareCore != null)
            {
                slotFlareCore.gameObject.SetActive(false);
            }
            if (slotAuraFlare != null)
            {
                slotAuraFlare.color = new Color(elemColor.r, elemColor.g, elemColor.b, 0.16f);
            }
            var runeGlyph = transform.Find("RuneGlyph");
            if (runeGlyph != null) runeGlyph.gameObject.SetActive(false);

            fieldCardToken.SetActive(true);

            // 1. Load custom monster art by ID if available, otherwise display clean element backdrop
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
                        standbyAnimator = tokenArtImage.gameObject.GetComponent<MonsterStandbyAnimator>();
                        if (standbyAnimator == null)
                            standbyAnimator = tokenArtImage.gameObject.AddComponent<MonsterStandbyAnimator>();
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

            // 1.6. Top-Left Clan / Tribe Emblem Badge
            if (tokenClanEmblemImage != null && data != null)
            {
                Sprite clanSprite = MonsterArtLoader.GetClanEmblemSprite(data.tribe);
                tokenClanEmblemImage.sprite = clanSprite;
                tokenClanEmblemImage.gameObject.SetActive(clanSprite != null);
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
            var slotOwner = gm != null ? (isPlayerSide ? gm.humanPlayer : gm.aiPlayer) : null;
            int effectiveAtk = instance.GetEffectiveAtk(activeEnv, slotOwner);
            float envMod = instance.GetEnvironmentModifier(activeEnv);

            if (tokenAtkText != null)
            {
                tokenAtkText.text = effectiveAtk.ToString();
                if (effectiveAtk > instance.data.atk || envMod > 1.01f)
                {
                    tokenAtkText.color = new Color(0.40f, 1f, 0.55f);
                }
                else if (effectiveAtk < instance.data.atk || envMod < 0.99f)
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
                else if (instance.baseDef > 0 && instance.currentDef > instance.baseDef)
                {
                    tokenDefText.color = new Color(0.45f, 0.98f, 1f);
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
                sigilRect.anchoredPosition = new Vector2(0f, -68f);

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
                        if (isPlayerSide && !BattleUIManager.Instance.IsSelectingAbilityTarget)
                        {
                            BattleUIManager.Instance.OnSlotAbilityButtonClicked(slotIndex, 0);
                        }
                        else
                        {
                            BattleUIManager.Instance.OnSlotSigilClickedNonActivate(slotIndex, isPlayerSide, 0);
                        }
                    }
                });

                var hoverRelay = abilitySigilObj.AddComponent<AbilitySigilHoverRelay>();
                hoverRelay.ownerSlot = this;
                hoverRelay.skillIndex = 0;

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
                    new Color(0.06f, 0.34f, 0.68f, 0.98f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(16f, -14f), new Vector2(18f, 18f));
                sigilPipBgImage = pipObj.GetComponent<Image>();
                sigilPipBgImage.raycastTarget = false;
                var pipOutline = pipObj.AddComponent<Outline>();
                pipOutline.effectColor = new Color(0.35f, 0.88f, 1f, 0.95f);
                pipOutline.effectDistance = new Vector2(1f, 1f);

                sigilText = GameBootstrapper.CreateText(pipObj.transform, "SigilCostText", "1", 10,
                    Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(18f, 18f));
                sigilText.fontStyle = FontStyle.Bold;
                sigilText.raycastTarget = false;
                GameBootstrapper.AddTextShadow(sigilText);

                abilitySigilObj.SetActive(false);
            }

            if (abilitySigil2Obj == null)
            {
                abilitySigil2Obj = new GameObject($"AbilitySigil2Flash_{slotIndex}");
                abilitySigil2Obj.transform.SetParent(transform, false);

                var sigil2Rect = abilitySigil2Obj.AddComponent<RectTransform>();
                sigil2Rect.anchorMin = new Vector2(0.5f, 0.5f);
                sigil2Rect.anchorMax = new Vector2(0.5f, 0.5f);
                sigil2Rect.sizeDelta = new Vector2(44f, 44f);
                sigil2Rect.anchoredPosition = new Vector2(22f, -68f);

                var hit2Img = abilitySigil2Obj.AddComponent<Image>();
                hit2Img.sprite = GameBootstrapper.GetCircleSprite();
                hit2Img.color = new Color(0.18f, 0.05f, 0.08f, 0.78f);
                hit2Img.raycastTarget = true;

                abilitySigil2Button = abilitySigil2Obj.AddComponent<Button>();
                abilitySigil2Button.transition = Selectable.Transition.None;
                abilitySigil2Button.onClick.AddListener(() =>
                {
                    if (BattleUIManager.Instance != null)
                    {
                        if (isPlayerSide && !BattleUIManager.Instance.IsSelectingAbilityTarget)
                        {
                            BattleUIManager.Instance.OnSlotAbilityButtonClicked(slotIndex, 1);
                        }
                        else
                        {
                            BattleUIManager.Instance.OnSlotSigilClickedNonActivate(slotIndex, isPlayerSide, 1);
                        }
                    }
                });

                var hoverRelay2 = abilitySigil2Obj.AddComponent<AbilitySigilHoverRelay>();
                hoverRelay2.ownerSlot = this;
                hoverRelay2.skillIndex = 1;

                var ring2Obj = new GameObject("Sigil2SpinRing");
                ring2Obj.transform.SetParent(abilitySigil2Obj.transform, false);
                var r2Rect = ring2Obj.AddComponent<RectTransform>();
                r2Rect.anchorMin = new Vector2(0.5f, 0.5f);
                r2Rect.anchorMax = new Vector2(0.5f, 0.5f);
                r2Rect.sizeDelta = new Vector2(54f, 54f);
                sigil2RingImage = ring2Obj.AddComponent<Image>();
                sigil2RingImage.sprite = GameBootstrapper.GetSummoningCircleSprite();
                sigil2RingImage.raycastTarget = false;

                var flash2Obj = new GameObject("Sigil2FlashCore");
                flash2Obj.transform.SetParent(abilitySigil2Obj.transform, false);
                var f2Rect = flash2Obj.AddComponent<RectTransform>();
                f2Rect.anchorMin = new Vector2(0.5f, 0.5f);
                f2Rect.anchorMax = new Vector2(0.5f, 0.5f);
                f2Rect.sizeDelta = new Vector2(46f, 46f);
                sigil2FlashImage = flash2Obj.AddComponent<Image>();
                sigil2FlashImage.sprite = GameBootstrapper.GetAbilityFlashCoreSprite();
                sigil2FlashImage.raycastTarget = false;

                var swords2Obj = new GameObject("Sigil2CrossedSwords");
                swords2Obj.transform.SetParent(abilitySigil2Obj.transform, false);
                var sw2Rect = swords2Obj.AddComponent<RectTransform>();
                sw2Rect.anchorMin = new Vector2(0.5f, 0.5f);
                sw2Rect.anchorMax = new Vector2(0.5f, 0.5f);
                sw2Rect.sizeDelta = new Vector2(30f, 30f);
                sigil2SwordsImage = swords2Obj.AddComponent<Image>();
                sigil2SwordsImage.sprite = GameBootstrapper.GetCrossedSwordsSprite();
                sigil2SwordsImage.raycastTarget = false;

                var pip2Obj = GameBootstrapper.CreateCirclePanel(abilitySigil2Obj.transform, "Sigil2CostPip",
                    new Color(0.68f, 0.10f, 0.18f, 0.98f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(16f, -14f), new Vector2(18f, 18f));
                sigil2PipBgImage = pip2Obj.GetComponent<Image>();
                sigil2PipBgImage.raycastTarget = false;
                var pip2Outline = pip2Obj.AddComponent<Outline>();
                pip2Outline.effectColor = new Color(1f, 0.45f, 0.50f, 0.95f);
                pip2Outline.effectDistance = new Vector2(1f, 1f);

                sigil2Text = GameBootstrapper.CreateText(pip2Obj.transform, "Sigil2CostText", "2", 10,
                    Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(18f, 18f));
                sigil2Text.fontStyle = FontStyle.Bold;
                sigil2Text.raycastTarget = false;
                GameBootstrapper.AddTextShadow(sigil2Text);

                abilitySigil2Obj.SetActive(false);
            }
        }

        private void UpdateAbilityAuraAndSigil(MonsterInstance instance, Color elemColor)
        {
            if (instance == null || !instance.IsAlive || !instance.HasActiveSkill)
            {
                isAbilityReady = false;
                isAbility2Ready = false;
                if (auraRoot != null) auraRoot.SetActive(false);
                if (abilitySigilObj != null) abilitySigilObj.SetActive(false);
                if (abilitySigil2Obj != null) abilitySigil2Obj.SetActive(false);
                if (skillCastBadgeObj != null && skillCastHighlightCoroutine == null) skillCastBadgeObj.SetActive(false);
                return;
            }

            EnsureAbilityAuraAndSigilCreated();

            // Guarantee correct layering: Aura behind card -> FieldCardToken -> Flash Sigils on front rim
            if (auraRoot != null && fieldCardToken != null)
            {
                auraRoot.transform.SetAsFirstSibling();
                fieldCardToken.transform.SetSiblingIndex(auraRoot.transform.GetSiblingIndex() + 1);
            }
            if (abilitySigilObj != null) abilitySigilObj.transform.SetAsLastSibling();
            if (abilitySigil2Obj != null) abilitySigil2Obj.transform.SetAsLastSibling();
            if (skillCastBadgeObj != null && skillCastBadgeObj.activeSelf) skillCastBadgeObj.transform.SetAsLastSibling();
            if (targetReticleBadge != null && targetReticleBadge.activeSelf) targetReticleBadge.transform.SetAsLastSibling();

            activeAuraColor = elemColor;

            var gm = GameManager.Instance;
            if (gm == null || CombatSystem.Instance == null)
            {
                isAbilityReady = false;
                isAbility2Ready = false;
                if (auraRoot != null) auraRoot.SetActive(false);
                if (abilitySigilObj != null) abilitySigilObj.SetActive(false);
                if (abilitySigil2Obj != null) abilitySigil2Obj.SetActive(false);
                return;
            }

            bool hasTwoSkills = instance.ActiveSkillCount >= 2;

            // Position Skill 1 Sigil (centered if 1 skill, left if 2 skills)
            if (abilitySigilObj != null)
            {
                var rt1 = abilitySigilObj.GetComponent<RectTransform>();
                if (rt1 != null)
                {
                    rt1.anchoredPosition = hasTwoSkills ? new Vector2(-22f, -68f) : new Vector2(0f, -68f);
                }
                abilitySigilObj.SetActive(true);
            }

            if (abilitySigil2Obj != null)
            {
                var rt2 = abilitySigil2Obj.GetComponent<RectTransform>();
                if (rt2 != null)
                {
                    rt2.anchoredPosition = new Vector2(22f, -68f);
                }
                abilitySigil2Obj.SetActive(hasTwoSkills);
            }

            var ownerPlayer = isPlayerSide ? gm.humanPlayer : gm.aiPlayer;
            var opponentPlayer = isPlayerSide ? gm.aiPlayer : gm.humanPlayer;

            // Update Skill 1 Sigil (🔵 BLUE)
            var state1 = CombatSystem.Instance.GetAbilityState(ownerPlayer, opponentPlayer, slotIndex, 0, out int cost1, out string _);
            isAbilityReady = (state1 == CombatSystem.AbilityReadyState.Ready);
            ApplySingleSigilVisualState(
                abilitySigilObj, sigilRingImage, sigilFlashImage, sigilSwordsImage, sigilPipBgImage, sigilText,
                state1, cost1, Skill1BlueColor, new Color(0.06f, 0.34f, 0.68f, 0.98f));

            // Update Skill 2 Sigil (🔴 RED) if monster has 2 active skills
            if (hasTwoSkills)
            {
                var state2 = CombatSystem.Instance.GetAbilityState(ownerPlayer, opponentPlayer, slotIndex, 1, out int cost2, out string _);
                isAbility2Ready = (state2 == CombatSystem.AbilityReadyState.Ready);
                ApplySingleSigilVisualState(
                    abilitySigil2Obj, sigil2RingImage, sigil2FlashImage, sigil2SwordsImage, sigil2PipBgImage, sigil2Text,
                    state2, cost2, Skill2RedColor, new Color(0.68f, 0.10f, 0.18f, 0.98f));
            }
            else
            {
                isAbility2Ready = false;
            }

            if (auraRoot != null)
            {
                auraRoot.SetActive(isAbilityReady || isAbility2Ready);
            }

            UpdatePersistentSkillCastBadge();
        }

        private void ApplySingleSigilVisualState(
            GameObject sigilObj, Image ringImg, Image flashImg, Image swordsImg, Image pipBgImg, Text costTxt,
            CombatSystem.AbilityReadyState state, int cost, Color sigilAccentColor, Color readyPipColor)
        {
            if (sigilObj == null) return;

            if (state == CombatSystem.AbilityReadyState.Ready)
            {
                if (costTxt != null)
                {
                    costTxt.text = cost.ToString();
                    costTxt.color = Color.white;
                }
                if (pipBgImg != null)
                {
                    pipBgImg.color = readyPipColor;
                }
                if (flashImg != null)
                {
                    flashImg.color = Color.Lerp(sigilAccentColor, Color.white, 0.25f);
                }
                if (swordsImg != null)
                {
                    swordsImg.color = Color.Lerp(sigilAccentColor, Color.white, 0.55f);
                }
                if (ringImg != null)
                {
                    ringImg.color = sigilAccentColor;
                }
            }
            else if (state == CombatSystem.AbilityReadyState.AlreadyUsed)
            {
                sigilObj.transform.localScale = Vector3.one;
                if (flashImg != null) flashImg.color = new Color(sigilAccentColor.r, sigilAccentColor.g, sigilAccentColor.b, 0.38f);
                if (swordsImg != null) swordsImg.color = new Color(sigilAccentColor.r, sigilAccentColor.g, sigilAccentColor.b, 0.88f);
                if (ringImg != null) ringImg.color = new Color(sigilAccentColor.r, sigilAccentColor.g, sigilAccentColor.b, 0.78f);
                if (pipBgImg != null) pipBgImg.color = new Color(0.08f, 0.56f, 0.28f, 0.98f);
                if (costTxt != null)
                {
                    costTxt.text = "✓";
                    costTxt.color = new Color(0.55f, 1f, 0.72f, 1f);
                }
            }
            else
            {
                sigilObj.transform.localScale = Vector3.one;
                string glyph;

                switch (state)
                {
                    case CombatSystem.AbilityReadyState.NotEnoughMana:
                    case CombatSystem.AbilityReadyState.NotEnoughPower:
                        glyph = cost.ToString();
                        break;
                    case CombatSystem.AbilityReadyState.NoValidTarget:
                        glyph = "🎯";
                        break;
                    case CombatSystem.AbilityReadyState.Silenced:
                        glyph = "🔒";
                        break;
                    case CombatSystem.AbilityReadyState.Frozen:
                        glyph = "❄️";
                        break;
                    default:
                        glyph = cost.ToString();
                        break;
                }

                if (flashImg != null) flashImg.color = new Color(sigilAccentColor.r, sigilAccentColor.g, sigilAccentColor.b, 0.30f);
                if (swordsImg != null) swordsImg.color = new Color(sigilAccentColor.r, sigilAccentColor.g, sigilAccentColor.b, 0.76f);
                if (ringImg != null) ringImg.color = new Color(sigilAccentColor.r, sigilAccentColor.g, sigilAccentColor.b, 0.62f);
                if (pipBgImg != null) pipBgImg.color = new Color(readyPipColor.r * 0.72f, readyPipColor.g * 0.72f, readyPipColor.b * 0.72f, 0.95f);
                if (costTxt != null)
                {
                    costTxt.text = glyph;
                    costTxt.color = new Color(0.95f, 0.96f, 1f, 0.95f);
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

            // 2.5. Top-Left Clan / Tribe Emblem Badge (enlarged & inset cleanly away from frame edges)
            var clanBadgeObj = new GameObject("ClanEmblemBadge");
            clanBadgeObj.transform.SetParent(fieldCardToken.transform, false);
            var rectClan = clanBadgeObj.AddComponent<RectTransform>();
            rectClan.anchorMin = new Vector2(0f, 1f);
            rectClan.anchorMax = new Vector2(0f, 1f);
            rectClan.sizeDelta = new Vector2(37f, 37f);
            rectClan.anchoredPosition = new Vector2(22.5f, -22.5f);
            tokenClanEmblemImage = clanBadgeObj.AddComponent<Image>();
            tokenClanEmblemImage.color = Color.white;
            tokenClanEmblemImage.preserveAspect = true;
            tokenClanEmblemImage.raycastTarget = false;
            clanBadgeObj.SetActive(false);

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
            if (skillCastHighlightCoroutine != null)
            {
                StopCoroutine(skillCastHighlightCoroutine);
                skillCastHighlightCoroutine = null;
            }
            isFlashingSkillCast = false;
            currentCardData = null;
            currentMonsterInstance = null;
            isHighlightedForSummon = false;
            isFlightHidden = false;
            if (tokenCanvasGroup != null) tokenCanvasGroup.alpha = 1f;
            isAbilityReady = false;
            isAbility2Ready = false;
            isSigilHovered = false;
            isSigil2Hovered = false;
            isTargetedForAbility = false;
            isTargetedForEmptySlot = false;
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
            if (abilitySigil2Obj != null)
            {
                abilitySigil2Obj.SetActive(false);
            }
            if (skillCastBadgeObj != null)
            {
                skillCastBadgeObj.SetActive(false);
            }
            if (targetReticleBadge != null)
            {
                targetReticleBadge.SetActive(false);
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
            if (innerRuneRingImage != null)
            {
                innerRuneRingImage.gameObject.SetActive(true);
            }
            if (slotAuraFlare != null)
            {
                slotAuraFlare.gameObject.SetActive(true);
            }
            if (slotFlareCore != null)
            {
                slotFlareCore.gameObject.SetActive(true);
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
        [NonSerialized] public int skillIndex = 0;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (ownerSlot != null)
            {
                if (skillIndex == 1) ownerSlot.SetSigil2Hovered(true);
                else ownerSlot.SetSigilHovered(true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (ownerSlot != null)
            {
                if (skillIndex == 1) ownerSlot.SetSigil2Hovered(false);
                else ownerSlot.SetSigilHovered(false);
            }
        }
    }
}

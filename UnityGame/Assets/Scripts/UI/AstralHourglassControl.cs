using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using MonsterCardGame.Core;

namespace MonsterCardGame.UI
{
    /// <summary>
    /// Tactile Astral Hourglass Medallion mounted on the center divider between the player's hand and battlefield.
    /// Acts as both a living turn indicator and an intuitive click/Spacebar end-turn pass control
    /// (with a 180-degree sand-flip animation and zero sidebar button clutter).
    /// </summary>
    public class AstralHourglassControl : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Button hourglassButton;
        public RectTransform scalePivot;
        public RectTransform flipPivot;
        public Image medallionImage;
        public Image outerRuneRing;
        public Image glowHalo;
        public Image flareCore;
        public Text captionText;
        public Image captionBgImage;

        private bool isHovered = false;
        private bool lastKnownPlayerActionReady = true;
        private bool hasInitializedState = false;
        private float currentFlipAngle = 0f;
        private float ringAngle = 0f;
        private float flareAngle = 0f;
        private Coroutine flipCoroutine = null;

        private void Awake()
        {
            BindButton();
        }

        private void Start()
        {
            BindButton();
        }

        public void BindButton()
        {
            if (hourglassButton == null)
            {
                hourglassButton = GetComponent<Button>();
            }
            if (hourglassButton != null)
            {
                hourglassButton.onClick.RemoveListener(OnHourglassPressed);
                hourglassButton.onClick.AddListener(OnHourglassPressed);
            }
        }

        public void SyncStateImmediate(bool playerActionReady)
        {
            if (!hasInitializedState)
            {
                hasInitializedState = true;
                lastKnownPlayerActionReady = playerActionReady;
                currentFlipAngle = playerActionReady ? 0f : 180f;
                if (flipPivot != null)
                {
                    flipPivot.localRotation = Quaternion.Euler(0f, 0f, -currentFlipAngle);
                }
                if (hourglassButton != null)
                {
                    hourglassButton.interactable = playerActionReady;
                }
            }
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            bool isPlayerTurn = gm != null && gm.isPlayerTurn;
            bool isActionPhase = gm != null && gm.currentPhase == GamePhase.ActionPhase;
            bool playerActionReady = isPlayerTurn && isActionPhase;

            if (!hasInitializedState)
            {
                SyncStateImmediate(playerActionReady);
            }
            else if (lastKnownPlayerActionReady != playerActionReady)
            {
                lastKnownPlayerActionReady = playerActionReady;
                float targetAngle = playerActionReady ? 360f : 180f;
                StartFlipTransition(targetAngle);
            }

            if (hourglassButton != null)
            {
                hourglassButton.interactable = playerActionReady;
            }

            // Keyboard Spacebar quick pass when player holds cards intentionally
            if (playerActionReady && Input.GetKeyDown(KeyCode.Space))
            {
                OnHourglassPressed();
                return;
            }

            // 1. Scale hover responsiveness (smooth & quiet)
            if (scalePivot != null)
            {
                float targetScale = (playerActionReady && isHovered) ? 1.09f : 1.0f;
                float curScale = scalePivot.localScale.x;
                if (Mathf.Abs(curScale - targetScale) > 0.002f)
                {
                    float s = Mathf.Lerp(curScale, targetScale, Time.unscaledDeltaTime * 14f);
                    scalePivot.localScale = new Vector3(s, s, 1f);
                }
            }

            // 2. Living astrolabe ring rotation & blue flare aura
            float breath = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (isHovered ? 5.2f : 2.4f));

            if (outerRuneRing != null)
            {
                float speed = playerActionReady ? (isHovered ? -58f : -16f) : -5f;
                ringAngle = (ringAngle + speed * Time.unscaledDeltaTime) % 360f;
                outerRuneRing.transform.localRotation = Quaternion.Euler(0f, 0f, ringAngle);

                if (playerActionReady)
                {
                    outerRuneRing.color = isHovered
                        ? new Color(0.66f, 0.94f, 1.0f, 0.95f)
                        : Color.Lerp(new Color(0.28f, 0.74f, 1.0f, 0.50f), new Color(0.46f, 0.88f, 1.0f, 0.80f), breath);
                }
                else
                {
                    outerRuneRing.color = new Color(0.22f, 0.52f, 0.88f, 0.34f);
                }
            }

            // 3. Circular astral blue lens-flare halo & core star glint
            if (glowHalo != null)
            {
                float haloScale = playerActionReady
                    ? (isHovered ? Mathf.Lerp(1.08f, 1.16f, breath) : Mathf.Lerp(0.98f, 1.05f, breath))
                    : Mathf.Lerp(0.92f, 0.96f, breath);
                glowHalo.transform.localScale = new Vector3(haloScale, haloScale, 1f);

                if (playerActionReady)
                {
                    glowHalo.color = isHovered
                        ? new Color(0.42f, 0.88f, 1.0f, Mathf.Lerp(0.76f, 0.96f, breath))
                        : new Color(0.24f, 0.72f, 1.0f, Mathf.Lerp(0.46f, 0.68f, breath));
                }
                else
                {
                    glowHalo.color = new Color(0.14f, 0.42f, 0.88f, Mathf.Lerp(0.22f, 0.36f, breath));
                }
            }

            if (flareCore != null)
            {
                float flareSpeed = playerActionReady ? (isHovered ? 24f : 11f) : 4f;
                flareAngle = (flareAngle + flareSpeed * Time.unscaledDeltaTime) % 360f;
                flareCore.transform.localRotation = Quaternion.Euler(0f, 0f, flareAngle);

                float fScale = playerActionReady
                    ? (isHovered ? Mathf.Lerp(1.08f, 1.18f, breath) : Mathf.Lerp(0.96f, 1.06f, breath))
                    : 0.88f;
                flareCore.transform.localScale = new Vector3(fScale, fScale, 1f);

                flareCore.color = playerActionReady
                    ? new Color(0.55f, 0.92f, 1.0f, isHovered ? Mathf.Lerp(0.72f, 0.92f, breath) : Mathf.Lerp(0.36f, 0.54f, breath))
                    : new Color(0.22f, 0.56f, 0.96f, 0.20f);
            }

            if (medallionImage != null)
            {
                Color targetTint = playerActionReady
                    ? (isHovered ? new Color(1.0f, 1.0f, 1.0f, 1.0f) : new Color(0.94f, 0.98f, 1.0f, 0.99f))
                    : new Color(0.76f, 0.84f, 0.96f, 0.92f);
                medallionImage.color = Color.Lerp(medallionImage.color, targetTint, Time.unscaledDeltaTime * 10f);
            }

            // 4. Compact inscribed caption plaque integrated into the seam line
            if (captionText != null)
            {
                if (playerActionReady)
                {
                    captionText.text = isHovered ? "⚔ ATTACK / PASS" : "⏳ PASS TURN";
                    captionText.color = isHovered ? new Color(0.72f, 0.95f, 1.0f) : new Color(0.76f, 0.90f, 1.0f);
                }
                else if (isPlayerTurn)
                {
                    captionText.text = "⚔ COMBAT";
                    captionText.color = new Color(0.64f, 0.88f, 1.0f);
                }
                else
                {
                    captionText.text = "ENEMY TURN";
                    captionText.color = new Color(0.56f, 0.76f, 0.96f);
                }
            }
        }

        private void StartFlipTransition(float targetAngle)
        {
            if (flipPivot == null) return;
            if (flipCoroutine != null) StopCoroutine(flipCoroutine);
            flipCoroutine = StartCoroutine(AnimateFlipRoutine(targetAngle));
        }

        private IEnumerator AnimateFlipRoutine(float targetAngle)
        {
            float startAngle = currentFlipAngle;
            if (Mathf.Approximately(startAngle, 360f) && targetAngle == 180f)
            {
                startAngle = 0f;
            }

            float duration = 0.42f;
            float t = 0f;
            while (t < duration && flipPivot != null)
            {
                t += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                float p = Mathf.Clamp01(t / duration);
                float ease = 1f - Mathf.Pow(1f - p, 3f);
                currentFlipAngle = Mathf.Lerp(startAngle, targetAngle, ease);
                flipPivot.localRotation = Quaternion.Euler(0f, 0f, -currentFlipAngle);
                yield return null;
            }

            currentFlipAngle = targetAngle % 360f;
            if (flipPivot != null)
            {
                flipPivot.localRotation = Quaternion.Euler(0f, 0f, -currentFlipAngle);
            }
            flipCoroutine = null;
        }

        public void OnHourglassPressed()
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            var gm = GameManager.Instance;
            if (gm == null || !gm.isPlayerTurn || gm.currentPhase != GamePhase.ActionPhase) return;

            isHovered = false;

            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && scalePivot != null)
            {
                StartCoroutine(BattleAnimations.AnimateAbilityShockwave(
                    canvas.transform, scalePivot, new Color(0.36f, 0.84f, 1.0f)));
            }

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.OnHourglassEndTurnClicked();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHovered = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
        }
    }
}


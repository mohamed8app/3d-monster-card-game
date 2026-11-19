using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using MonsterCardGame.Data;
using MonsterCardGame.Utils;

namespace MonsterCardGame.UI
{
    /// <summary>
    /// Controls the circular Commander Spirit medallion altar:
    /// applies category-specific luminous colors, orbits the outer rune ring,
    /// and animates a living directional flame aura + drifting embers streaming rightward.
    /// </summary>
    public class CommanderSpiritSlotController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public bool isPlayerSide = true;
        public Button slotButton;
        public RectTransform circleRoot;
        public Image flameCoronaGlow;
        public Image flamePlumePrimary;
        public Image flamePlumeSecondary;
        public RectTransform[] flameSparks;
        public Image[] flameSparkImages;
        public Image outerRuneRing;
        public Image circularRim;
        public Image categoryPillBg;
        public Text categoryPillText;
        public Image altarBorderGlow;
        public Image panelTintOverlay;

        private Color primaryColor = new Color(0.24f, 0.72f, 1.0f);
        private Color secondaryColor = new Color(0.45f, 0.48f, 1.0f);
        private bool hasActiveSpirit = false;
        private bool isHovered = false;
        private float summonBurstTimer = 0f;
        private float runeAngle = 0f;

        private float[] sparkPhase;
        private float[] sparkSpeed;
        private float[] sparkAmpY;
        private Vector2[] sparkBasePos;

        private void Awake()
        {
            InitSparkData();
            SetCategoryTheme(null);
        }

        private void InitSparkData()
        {
            if (flameSparks == null || flameSparks.Length == 0) return;
            int n = flameSparks.Length;
            sparkPhase = new float[n];
            sparkSpeed = new float[n];
            sparkAmpY = new float[n];
            sparkBasePos = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                sparkPhase[i] = (i * 0.17f + (isPlayerSide ? 0.1f : 0.45f)) % 1f;
                sparkSpeed[i] = 0.48f + (i % 3) * 0.16f;
                sparkAmpY[i] = 6f + (i % 4) * 4.5f;
                float yOff = Mathf.Lerp(-38f, 38f, (i + 0.5f) / n);
                sparkBasePos[i] = new Vector2(-46f, yOff - 10f);
            }
        }

        public void SetCategoryTheme(CardData spirit)
        {
            hasActiveSpirit = (spirit != null);
            primaryColor = MonsterArtLoader.GetSpiritCategoryColor(spirit, isPlayerSide);
            secondaryColor = MonsterArtLoader.GetSpiritCategorySecondaryColor(spirit, isPlayerSide);

            if (categoryPillBg != null)
            {
                Color pillFill = Color.Lerp(new Color(0.05f, 0.07f, 0.11f), primaryColor, hasActiveSpirit ? 0.28f : 0.14f);
                pillFill.a = 0.94f;
                categoryPillBg.color = pillFill;

                var outline = categoryPillBg.GetComponent<Outline>();
                if (outline != null)
                {
                    Color rim = primaryColor;
                    rim.a = hasActiveSpirit ? 0.85f : 0.45f;
                    outline.effectColor = rim;
                }
            }

            if (categoryPillText != null)
            {
                if (hasActiveSpirit)
                {
                    string catIcon = MonsterArtLoader.GetTribeEmoji(spirit.tribe);
                    string tribeDisplay = MonsterArtLoader.GetTribeDisplayName(spirit.tribe);
                    categoryPillText.text = $"{catIcon} {tribeDisplay.ToUpper()} CLAN".Trim();
                    categoryPillText.color = Color.Lerp(primaryColor, Color.white, 0.35f);
                }
                else
                {
                    categoryPillText.text = isPlayerSide ? "✧ SPIRIT SEAL (EMPTY)" : "✧ ENEMY SEAL (EMPTY)";
                    categoryPillText.color = Color.Lerp(primaryColor, new Color(0.75f, 0.82f, 0.92f), 0.45f);
                }
            }

            if (circularRim != null)
            {
                // When an active spirit is summoned, hide the procedural rim so the user's designed circular frame shines completely!
                circularRim.gameObject.SetActive(!hasActiveSpirit);
                if (!hasActiveSpirit)
                {
                    circularRim.color = Color.Lerp(primaryColor, Color.white, 0.10f);
                }
            }

            if (altarBorderGlow != null)
            {
                Color bc = Color.Lerp(new Color(0.24f, 0.28f, 0.36f), primaryColor, hasActiveSpirit ? 0.65f : 0.25f);
                bc.a = hasActiveSpirit ? 0.92f : 0.72f;
                altarBorderGlow.color = bc;
            }
        }

        public void TriggerSummonFlare()
        {
            summonBurstTimer = 0.85f;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHovered = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
        }

        private void Update()
        {
            float t = Time.unscaledTime;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            if (summonBurstTimer > 0f)
            {
                summonBurstTimer = Mathf.Max(0f, summonBurstTimer - dt);
            }
            float burst = (summonBurstTimer > 0f) ? Mathf.Sin((summonBurstTimer / 0.85f) * Mathf.PI) * 0.55f : 0f;

            float baseIntensity = hasActiveSpirit ? 1.0f : 0.42f;
            if (isHovered) baseIntensity += 0.28f;
            baseIntensity += burst;

            // Gentle circle pulse on hover / burst
            if (circleRoot != null)
            {
                float targetScale = (isHovered ? 1.055f : 1.0f) + burst * 0.12f;
                float cur = circleRoot.localScale.x;
                if (Mathf.Abs(cur - targetScale) > 0.002f)
                {
                    float s = Mathf.Lerp(cur, targetScale, dt * 12f);
                    circleRoot.localScale = new Vector3(s, s, 1f);
                }
            }
        }
    }
}


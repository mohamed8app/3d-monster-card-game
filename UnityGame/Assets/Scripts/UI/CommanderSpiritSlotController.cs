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
                    string catIcon = !string.IsNullOrEmpty(spirit.tribe)
                        ? MonsterArtLoader.GetTribeEmoji(spirit.tribe)
                        : MonsterArtLoader.GetElementEmoji(spirit.element);
                    string catLabel = !string.IsNullOrEmpty(spirit.tribe)
                        ? spirit.tribe.ToUpperInvariant()
                        : (!string.IsNullOrEmpty(spirit.element) ? spirit.element.ToUpperInvariant() : "SPIRIT");
                    string elemLabel = (!string.IsNullOrEmpty(spirit.tribe) && !string.IsNullOrEmpty(spirit.element) &&
                                        !string.Equals(spirit.tribe, spirit.element, System.StringComparison.OrdinalIgnoreCase))
                        ? $" • {spirit.element.ToUpperInvariant()}"
                        : "";
                    categoryPillText.text = $"{catIcon} {catLabel}{elemLabel}".Trim();
                    categoryPillText.color = Color.Lerp(primaryColor, Color.white, 0.35f);
                }
                else
                {
                    categoryPillText.text = isPlayerSide ? "✧ COMMANDER SEAL" : "✧ HOSTILE SEAL";
                    categoryPillText.color = Color.Lerp(primaryColor, new Color(0.75f, 0.82f, 0.92f), 0.45f);
                }
            }

            if (circularRim != null)
            {
                circularRim.color = Color.Lerp(primaryColor, Color.white, hasActiveSpirit ? 0.24f : 0.10f);
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

            // Rotating arcane ring around circular Commander Spirit card
            if (outerRuneRing != null)
            {
                float speed = (hasActiveSpirit ? 18f : 7f) + (isHovered ? 24f : 0f) + burst * 45f;
                runeAngle = (runeAngle + speed * dt * (isPlayerSide ? 1f : -1f)) % 360f;
                outerRuneRing.transform.localRotation = Quaternion.Euler(0f, 0f, runeAngle);

                float ringPulse = 0.5f + 0.5f * Mathf.Sin(t * 2.6f);
                Color rc = Color.Lerp(primaryColor, secondaryColor, ringPulse * 0.45f);
                rc.a = Mathf.Clamp01((hasActiveSpirit ? 0.68f : 0.30f) + ringPulse * 0.18f + (isHovered ? 0.18f : 0f) + burst * 0.25f);
                outerRuneRing.color = rc;
            }

            // Rightward directional fire aura (corona + dual undulating flame plumes)
            float waveA = 0.5f + 0.5f * Mathf.Sin(t * 5.2f + (isPlayerSide ? 0f : 1.7f));
            float waveB = 0.5f + 0.5f * Mathf.Sin(t * 7.8f + (isPlayerSide ? 2.1f : 0.4f));

            if (flameCoronaGlow != null)
            {
                float cScale = Mathf.Lerp(0.96f, 1.08f, waveA) + burst * 0.22f;
                flameCoronaGlow.transform.localScale = new Vector3(cScale, cScale, 1f);
                Color cc = Color.Lerp(primaryColor, Color.white, 0.22f);
                cc.a = Mathf.Clamp01((0.36f + waveA * 0.14f) * baseIntensity);
                flameCoronaGlow.color = cc;
            }

            if (flamePlumePrimary != null)
            {
                float sx = 1.0f + (waveA - 0.5f) * 0.10f + burst * 0.16f;
                float sy = 1.0f + (waveB - 0.5f) * 0.12f + burst * 0.10f;
                flamePlumePrimary.transform.localScale = new Vector3(sx, sy, 1f);
                Color fp = Color.Lerp(primaryColor, Color.white, 0.16f * waveA);
                fp.a = Mathf.Clamp01((0.52f + waveA * 0.18f) * baseIntensity);
                flamePlumePrimary.color = fp;
            }

            if (flamePlumeSecondary != null)
            {
                float sx2 = 0.98f + (waveB - 0.5f) * 0.14f + burst * 0.18f;
                float sy2 = 0.94f + (waveA - 0.5f) * 0.14f;
                flamePlumeSecondary.transform.localScale = new Vector3(sx2, sy2, 1f);
                Color fs = Color.Lerp(secondaryColor, primaryColor, 0.35f + 0.3f * waveB);
                fs.a = Mathf.Clamp01((0.40f + waveB * 0.20f) * baseIntensity);
                flamePlumeSecondary.color = fs;
            }

            if (panelTintOverlay != null)
            {
                Color pt = primaryColor;
                pt.a = Mathf.Clamp01((hasActiveSpirit ? 0.10f : 0.04f) + waveA * 0.03f);
                panelTintOverlay.color = pt;
            }

            // Animate 6 rightward flame embers streaming cleanly off the right arc of the circular card
            if (flameSparks != null && sparkPhase != null)
            {
                for (int i = 0; i < flameSparks.Length; i++)
                {
                    if (flameSparks[i] == null) continue;
                    sparkPhase[i] = (sparkPhase[i] + dt * sparkSpeed[i] * (hasActiveSpirit ? 1.05f : 0.55f)) % 1f;
                    float p = sparkPhase[i];
                    float travelX = Mathf.Lerp(0f, 188f, p);
                    float curlY = Mathf.Sin(p * 6.28318f + i * 1.1f) * sparkAmpY[i] * p;
                    flameSparks[i].anchoredPosition = sparkBasePos[i] + new Vector2(travelX, curlY);

                    float sc = Mathf.Lerp(1.05f, 0.22f, p);
                    flameSparks[i].localScale = new Vector3(sc, sc, 1f);

                    if (flameSparkImages != null && i < flameSparkImages.Length && flameSparkImages[i] != null)
                    {
                        Color ec = Color.Lerp(Color.white, (i % 2 == 0) ? primaryColor : secondaryColor, Mathf.Clamp01(0.35f + p * 0.65f));
                        float alphaEnv = Mathf.Clamp01(p * 5f) * Mathf.Pow(1f - p, 1.3f);
                        ec.a = Mathf.Clamp01(alphaEnv * 0.85f * baseIntensity);
                        flameSparkImages[i].color = ec;
                    }
                }
            }
        }
    }
}


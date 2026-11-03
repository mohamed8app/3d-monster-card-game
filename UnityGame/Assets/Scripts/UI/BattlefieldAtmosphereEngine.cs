using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MonsterCardGame.Core;
using MonsterCardGame.Data;
using MonsterCardGame.Utils;

namespace MonsterCardGame.UI
{
    /// <summary>
    /// محرك الخلفية الحية والطقس التفاعلي لساحة المعركة (Living War & Weather Battlefield Engine).
    /// يحول أرض المعركة إلى مشهد سينمائي نابض بالحياة يتفاعل مع الحرب والـ 12 بيئة ومستوياتها الـ 3.
    /// </summary>
    public class BattlefieldAtmosphereEngine : MonoBehaviour
    {
        public static BattlefieldAtmosphereEngine Instance { get; private set; }

        [Header("Layer References")]
        public Image arenaGroundImage;
        public Image ambientTintOverlay;
        public Image vignetteOverlay;
        public Image centralRiftGlow;
        public Image centralRiftCore;
        public RectTransform outerRuneWheel;
        public Image outerRuneImage;
        public RectTransform innerRuneWheel;
        public Image innerRuneImage;
        public Image shockwaveRing;
        public Image lightningFlashOverlay;

        // Procedural Sprites Cache
        private static Sprite softOrbSprite;
        private static Sprite streakSprite;
        private static Sprite ringSprite;
        private static Sprite runeWheelSprite;
        private static Sprite gearWheelSprite;
        private static Sprite riftBeamSprite;
        private static Sprite vignetteSprite;

        // Particle Pool
        private const int MaxParticles = 64;
        private readonly List<AtmosphereParticle> particles = new List<AtmosphereParticle>(MaxParticles);
        private RectTransform particleContainer;

        // Lightning Bolt Segments
        private readonly List<Image> lightningSegments = new List<Image>();
        private readonly List<Image> pedestalRuneRings = new List<Image>(12);
        private float nextLightningTimer = 3.5f;
        private float lightningFade = 0f;

        // State & Smooth Transitions
        private string currentElement = "";
        private int currentLevel = 0;
        private Color targetGroundTint = new Color(0.85f, 0.82f, 0.88f, 1f);
        private Color targetAmbientColor = new Color(0.12f, 0.08f, 0.06f, 0.25f);
        private Color targetRiftColor = new Color(1f, 0.45f, 0.15f, 0.75f);
        private Color targetVignetteColor = new Color(0.15f, 0.05f, 0.05f, 0.55f);

        private float actionPulseBoost = 0f;
        private float shockwaveProgress = 1.5f;
        private Color shockwaveColor = Color.white;
        private Vector2 baseGroundPos = Vector2.zero;

        private enum ParticleBehavior
        {
            WarEmbers,      // الوضع الحربي الافتراضي: جمر متطاير ودخان معركة
            VolcanoFire,    // بركان الحمم: حمم صاعدة كثيفة وشرارات نارية
            OceanFlood,     // طوفان المحيط: دوائر أمواج متسعة وفقاعات مائية
            BlizzardIce,    // العاصفة الثلجية: عاصفة ثلج مائلة سريعة وصقيع
            Earthquake,     // الزلزال الصخري: غبار متطاير وحصى مهتز
            Thunderstorm,   // العاصفة الرعدية: شرارات برق متذبذبة وصواعق خاطفة
            CycloneWind,    // إعصار الأعالي: تيارات رياح حلزونية تدور حول المركز
            GreatForest,    // الغابة العظيمة: يراعات مضيئة وأوراق شجر طائرة
            SacredLight,    // الشفق المقدس: أشعة نور سماوية وذرات ذهبية صاعدة
            DeepEclipse,    // الكسوف السحيق: أطياف ظلام تنجذب نحو مركز الكسوف
            MechaFactory,   // المصنع الآلي: تروس دوارة وشرارات لحام وبخار
            GhostRealm,     // العالم الأثيري: نيران شبحية متمايلة وضباب كثيف
            ArcaneSpace     // الفضاء الأركيني: طلاسم سحرية ونجوم كونية
        }

        private ParticleBehavior activeBehavior = ParticleBehavior.WarEmbers;

        private class AtmosphereParticle
        {
            public RectTransform rect;
            public Image img;
            public Vector2 pos;
            public Vector2 vel;
            public float size;
            public float baseAlpha;
            public float rotation;
            public float rotSpeed;
            public float phase;
            public float life;
            public float maxLife;
            public int layerType; // 0 = Primary, 1 = Secondary/Streak, 2 = Mist/Ripple
            public Color baseColor;
        }

        public static BattlefieldAtmosphereEngine EnsureInstance(Transform canvasTransform, Image groundImg = null, Image overlayImg = null)
        {
            if (Instance != null)
            {
                if (Instance.particles.Count == 0 || softOrbSprite == null)
                {
                    Instance.InitializeLayers(
                        canvasTransform,
                        groundImg != null ? groundImg : Instance.arenaGroundImage,
                        overlayImg != null ? overlayImg : Instance.ambientTintOverlay);
                }
                return Instance;
            }

            var existing = FindObjectOfType<BattlefieldAtmosphereEngine>();
            if (existing != null)
            {
                Instance = existing;
                if (existing.particles.Count == 0 || softOrbSprite == null)
                {
                    existing.InitializeLayers(
                        canvasTransform,
                        groundImg != null ? groundImg : existing.arenaGroundImage,
                        overlayImg != null ? overlayImg : existing.ambientTintOverlay);
                }
                return existing;
            }

            if (canvasTransform == null) return null;

            GameObject rootObj;
            if (overlayImg != null)
            {
                rootObj = overlayImg.gameObject;
            }
            else
            {
                rootObj = new GameObject("EnvironmentAtmosphereOverlay");
                rootObj.transform.SetParent(canvasTransform, false);
                rootObj.transform.SetSiblingIndex(1);
                var rt = rootObj.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.sizeDelta = Vector2.zero;
                overlayImg = rootObj.AddComponent<Image>();
                overlayImg.raycastTarget = false;
            }

            var engine = rootObj.AddComponent<BattlefieldAtmosphereEngine>();
            engine.InitializeLayers(canvasTransform, groundImg, overlayImg);
            return engine;
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this)
            {
                if (Application.isPlaying) Destroy(this);
                else DestroyImmediate(this);
            }
        }

        private void Start()
        {
            if (particles.Count == 0 || softOrbSprite == null || particleContainer == null)
            {
                var canvas = GetComponentInParent<Canvas>();
                Image ground = arenaGroundImage;
                if (ground == null && canvas != null)
                {
                    var bgTr = canvas.transform.Find("ArenaBackground");
                    if (bgTr != null) ground = bgTr.GetComponent<Image>();
                }
                InitializeLayers(
                    canvas != null ? canvas.transform : transform.parent,
                    ground,
                    ambientTintOverlay != null ? ambientTintOverlay : GetComponent<Image>());
            }
        }

        public void InitializeLayers(Transform canvasTransform, Image groundImg, Image overlayImg)
        {
            GenerateProceduralSprites();

            arenaGroundImage = groundImg;
            if (arenaGroundImage != null && arenaGroundImage.sprite == null)
            {
                var arenaTex = Resources.Load<Texture2D>("arena_ground");
                if (arenaTex != null)
                {
                    arenaGroundImage.sprite = Sprite.Create(arenaTex, new Rect(0, 0, arenaTex.width, arenaTex.height), new Vector2(0.5f, 0.5f));
                }
            }
            ambientTintOverlay = overlayImg;

            if (ambientTintOverlay != null)
            {
                ambientTintOverlay.raycastTarget = false;
                ambientTintOverlay.color = new Color(0.10f, 0.07f, 0.05f, 0.25f);
            }

            // ضمان عدم اعتراض أي نقرات ماوس نهائياً
            var cg = gameObject.GetComponent<CanvasGroup>();
            if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            // تنظيف أي أبناء سابقين عند إعادة البناء
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(transform.GetChild(i).gameObject);
                else
                    DestroyImmediate(transform.GetChild(i).gameObject);
            }
            particles.Clear();
            lightningSegments.Clear();
            pedestalRuneRings.Clear();

            // 0. حلقات الرون المتوهجة فوق المنصات الحجرية الـ 12 للوحوش (12 Monster Pedestal Runic Auras)
            float[] slotX = { -426f, -255f, -86f, 86f, 255f, 426f };
            float[] slotY = { 204f, -198f };
            for (int r = 0; r < 2; r++)
            {
                for (int c = 0; c < 6; c++)
                {
                    var pRingRect = CreateLayerImage($"PedestalAura_{r}_{c}", transform, runeWheelSprite, new Vector2(142, 142), new Color(1f, 0.65f, 0.25f, 0.22f), out Image pRingImg);
                    pRingRect.anchoredPosition = new Vector2(slotX[c], slotY[r]);
                    pedestalRuneRings.Add(pRingImg);
                }
            }

            // 1. دوائر الرون القتالية الدوارة في قلب الساحة (Rotating Arcane/War Battle Seals)
            outerRuneWheel = CreateLayerImage("OuterBattleSeal", transform, runeWheelSprite, new Vector2(360, 360), new Color(1f, 0.55f, 0.2f, 0.16f), out outerRuneImage);
            innerRuneWheel = CreateLayerImage("InnerBattleSeal", transform, runeWheelSprite, new Vector2(220, 220), new Color(1f, 0.75f, 0.3f, 0.20f), out innerRuneImage);

            // 2. صدع الاشتباك الوسطي النابض داخل الأخدود البركاني بين الجيشين (Central Chasm Rift)
            var riftGlowRect = CreateLayerImage("CentralRiftGlow", transform, riftBeamSprite, new Vector2(1140, 115), new Color(1f, 0.4f, 0.1f, 0.45f), out centralRiftGlow);
            riftGlowRect.anchoredPosition = new Vector2(0, 2f);

            var riftCoreRect = CreateLayerImage("CentralRiftCore", transform, riftBeamSprite, new Vector2(1060, 32), new Color(1f, 0.85f, 0.4f, 0.65f), out centralRiftCore);
            riftCoreRect.anchoredPosition = new Vector2(0, 2f);

            // 3. موجة الصدمة التفاعلية عند الضربات والتعاويذ (Combat Shockwave Ring)
            CreateLayerImage("CombatShockwaveRing", transform, ringSprite, new Vector2(100, 100), new Color(1f, 1f, 1f, 0f), out shockwaveRing);

            // 4. حاوية الجزيئات الجوية والحربية (64 جزيئاً حياً)
            var pContainerObj = new GameObject("WeatherParticlesContainer");
            pContainerObj.transform.SetParent(transform, false);
            particleContainer = pContainerObj.AddComponent<RectTransform>();
            particleContainer.anchorMin = Vector2.zero;
            particleContainer.anchorMax = Vector2.one;
            particleContainer.sizeDelta = Vector2.zero;

            for (int i = 0; i < MaxParticles; i++)
            {
                CreatePooledParticle(i);
            }

            // 5. شرائح البرق والصواعق (Lightning Bolt Segments)
            var lightningContainer = new GameObject("LightningContainer");
            lightningContainer.transform.SetParent(transform, false);
            var lcRect = lightningContainer.AddComponent<RectTransform>();
            lcRect.anchorMin = Vector2.zero;
            lcRect.anchorMax = Vector2.one;
            lcRect.sizeDelta = Vector2.zero;

            for (int i = 0; i < 10; i++)
            {
                CreateLayerImage($"BoltSeg_{i}", lcRect, streakSprite, new Vector2(140, 14), new Color(0.8f, 0.95f, 1f, 0f), out Image segImg);
                lightningSegments.Add(segImg);
            }

            // 6. وميض السماء عند الصواعق والانفجارات (Sky Flash Overlay)
            var flashRect = CreateLayerImage("SkyLightningFlash", transform, null, Vector2.zero, new Color(1f, 1f, 1f, 0f), out lightningFlashOverlay);
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;

            // 7. إطار الحواف السينمائي النابض (Screen-Edge War Vignette)
            var vigRect = CreateLayerImage("ScreenEdgeVignette", transform, vignetteSprite, Vector2.zero, new Color(0.15f, 0.04f, 0.02f, 0.55f), out vignetteOverlay);
            vigRect.anchorMin = Vector2.zero;
            vigRect.anchorMax = Vector2.one;

            // ضبط الحالة الابتدائية (أجواء الحرب الافتراضية)
            ApplyEnvironmentProfile(null);
        }

        private RectTransform CreateLayerImage(string name, Transform parent, Sprite sprite, Vector2 size, Color color, out Image img)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;

            img = obj.AddComponent<Image>();
            if (sprite != null) img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        private void CreatePooledParticle(int index)
        {
            var obj = new GameObject($"AtmosParticle_{index}");
            obj.transform.SetParent(particleContainer, false);
            var rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);

            var img = obj.AddComponent<Image>();
            img.raycastTarget = false;

            int layerType = (index < 38) ? 0 : (index < 54 ? 1 : 2);
            var p = new AtmosphereParticle
            {
                rect = rt,
                img = img,
                layerType = layerType,
                phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f)
            };

            particles.Add(p);
            ResetParticle(p, true);
        }

        /// <summary>
        /// تحديث مظهر وسلوك الخلفية الحية فور تغير البيئة أو مستواها
        /// </summary>
        public void ApplyEnvironmentProfile(EnvironmentCardData env)
        {
            string elem = env != null ? env.element : "";
            int lvl = env != null ? env.level : 0;

            bool changedElement = (elem != currentElement) || (lvl != currentLevel);
            currentElement = elem;
            currentLevel = lvl;

            float lvlAlphaBoost = (lvl == 2) ? 0.08f : (lvl == 3 ? 0.18f : 0f);

            switch (elem)
            {
                case "نار":
                    activeBehavior = ParticleBehavior.VolcanoFire;
                    targetGroundTint = new Color(1.0f, 0.72f, 0.62f, 1f);
                    targetAmbientColor = new Color(0.35f, 0.08f, 0.02f, 0.30f + lvlAlphaBoost);
                    targetRiftColor = new Color(1.0f, 0.35f, 0.05f, 0.85f);
                    targetVignetteColor = new Color(0.55f, 0.12f, 0.02f, 0.68f);
                    SetSealSprites(runeWheelSprite);
                    break;

                case "مياه":
                    activeBehavior = ParticleBehavior.OceanFlood;
                    targetGroundTint = new Color(0.65f, 0.88f, 1.0f, 1f);
                    targetAmbientColor = new Color(0.02f, 0.18f, 0.38f, 0.32f + lvlAlphaBoost);
                    targetRiftColor = new Color(0.15f, 0.75f, 1.0f, 0.80f);
                    targetVignetteColor = new Color(0.02f, 0.25f, 0.50f, 0.65f);
                    SetSealSprites(ringSprite);
                    break;

                case "جليد":
                    activeBehavior = ParticleBehavior.BlizzardIce;
                    targetGroundTint = new Color(0.78f, 0.94f, 1.0f, 1f);
                    targetAmbientColor = new Color(0.12f, 0.32f, 0.48f, 0.30f + lvlAlphaBoost);
                    targetRiftColor = new Color(0.45f, 0.92f, 1.0f, 0.85f);
                    targetVignetteColor = new Color(0.25f, 0.65f, 0.85f, 0.65f);
                    SetSealSprites(runeWheelSprite);
                    break;

                case "أرض":
                    activeBehavior = ParticleBehavior.Earthquake;
                    targetGroundTint = new Color(0.92f, 0.80f, 0.62f, 1f);
                    targetAmbientColor = new Color(0.28f, 0.18f, 0.06f, 0.30f + lvlAlphaBoost);
                    targetRiftColor = new Color(0.85f, 0.60f, 0.25f, 0.80f);
                    targetVignetteColor = new Color(0.35f, 0.22f, 0.08f, 0.65f);
                    SetSealSprites(runeWheelSprite);
                    break;

                case "كهرباء":
                    activeBehavior = ParticleBehavior.Thunderstorm;
                    targetGroundTint = new Color(0.92f, 0.92f, 0.72f, 1f);
                    targetAmbientColor = new Color(0.22f, 0.20f, 0.04f, 0.28f + lvlAlphaBoost);
                    targetRiftColor = new Color(1.0f, 0.92f, 0.20f, 0.90f);
                    targetVignetteColor = new Color(0.35f, 0.30f, 0.05f, 0.62f);
                    SetSealSprites(runeWheelSprite);
                    nextLightningTimer = 0.5f;
                    break;

                case "رياح":
                    activeBehavior = ParticleBehavior.CycloneWind;
                    targetGroundTint = new Color(0.75f, 0.98f, 0.88f, 1f);
                    targetAmbientColor = new Color(0.05f, 0.26f, 0.20f, 0.28f + lvlAlphaBoost);
                    targetRiftColor = new Color(0.30f, 0.95f, 0.75f, 0.80f);
                    targetVignetteColor = new Color(0.08f, 0.38f, 0.30f, 0.60f);
                    SetSealSprites(ringSprite);
                    break;

                case "غابة":
                    activeBehavior = ParticleBehavior.GreatForest;
                    targetGroundTint = new Color(0.72f, 0.96f, 0.72f, 1f);
                    targetAmbientColor = new Color(0.04f, 0.26f, 0.08f, 0.30f + lvlAlphaBoost);
                    targetRiftColor = new Color(0.35f, 0.95f, 0.35f, 0.80f);
                    targetVignetteColor = new Color(0.06f, 0.35f, 0.10f, 0.65f);
                    SetSealSprites(runeWheelSprite);
                    break;

                case "ضياء":
                    activeBehavior = ParticleBehavior.SacredLight;
                    targetGroundTint = new Color(1.0f, 0.98f, 0.82f, 1f);
                    targetAmbientColor = new Color(0.32f, 0.28f, 0.08f, 0.26f + lvlAlphaBoost);
                    targetRiftColor = new Color(1.0f, 0.95f, 0.55f, 0.88f);
                    targetVignetteColor = new Color(0.50f, 0.42f, 0.12f, 0.58f);
                    SetSealSprites(runeWheelSprite);
                    break;

                case "ظلام":
                    activeBehavior = ParticleBehavior.DeepEclipse;
                    targetGroundTint = new Color(0.68f, 0.55f, 0.85f, 1f);
                    targetAmbientColor = new Color(0.14f, 0.03f, 0.24f, 0.42f + lvlAlphaBoost);
                    targetRiftColor = new Color(0.70f, 0.20f, 0.95f, 0.88f);
                    targetVignetteColor = new Color(0.18f, 0.02f, 0.28f, 0.78f);
                    SetSealSprites(ringSprite);
                    break;

                case "ميكا":
                    activeBehavior = ParticleBehavior.MechaFactory;
                    targetGroundTint = new Color(0.82f, 0.88f, 0.95f, 1f);
                    targetAmbientColor = new Color(0.12f, 0.16f, 0.22f, 0.30f + lvlAlphaBoost);
                    targetRiftColor = new Color(0.45f, 0.82f, 1.0f, 0.80f);
                    targetVignetteColor = new Color(0.15f, 0.22f, 0.30f, 0.65f);
                    SetSealSprites(gearWheelSprite);
                    break;

                case "أشباح":
                    activeBehavior = ParticleBehavior.GhostRealm;
                    targetGroundTint = new Color(0.78f, 0.70f, 0.95f, 1f);
                    targetAmbientColor = new Color(0.16f, 0.06f, 0.28f, 0.34f + lvlAlphaBoost);
                    targetRiftColor = new Color(0.65f, 0.40f, 1.0f, 0.82f);
                    targetVignetteColor = new Color(0.22f, 0.08f, 0.38f, 0.70f);
                    SetSealSprites(runeWheelSprite);
                    break;

                case "سحر":
                    activeBehavior = ParticleBehavior.ArcaneSpace;
                    targetGroundTint = new Color(0.95f, 0.72f, 0.98f, 1f);
                    targetAmbientColor = new Color(0.26f, 0.05f, 0.30f, 0.32f + lvlAlphaBoost);
                    targetRiftColor = new Color(1.0f, 0.35f, 0.92f, 0.88f);
                    targetVignetteColor = new Color(0.38f, 0.06f, 0.42f, 0.68f);
                    SetSealSprites(runeWheelSprite);
                    break;

                default:
                    // الوضع الحربي المشتعل الافتراضي (Default War Battlefield)
                    activeBehavior = ParticleBehavior.WarEmbers;
                    targetGroundTint = new Color(0.92f, 0.86f, 0.84f, 1f);
                    targetAmbientColor = new Color(0.12f, 0.07f, 0.05f, 0.22f);
                    targetRiftColor = new Color(1.0f, 0.48f, 0.16f, 0.65f);
                    targetVignetteColor = new Color(0.18f, 0.06f, 0.04f, 0.55f);
                    SetSealSprites(runeWheelSprite);
                    break;
            }

            if (changedElement)
            {
                for (int i = 0; i < particles.Count; i++)
                {
                    ResetParticle(particles[i], true);
                }
                TriggerActionPulse(targetRiftColor, 1.2f);
            }
        }

        private void SetSealSprites(Sprite s)
        {
            if (outerRuneImage != null && s != null) outerRuneImage.sprite = s;
            if (innerRuneImage != null && s != null) innerRuneImage.sprite = s;
        }

        /// <summary>
        /// إطلاق موجة صدمة بصرية ونبضة طاقة في الخلفية عند الهجوم أو التعاويذ
        /// </summary>
        public void TriggerActionPulse(Color pulseColor, float intensity = 1.0f)
        {
            actionPulseBoost = Mathf.Clamp(actionPulseBoost + intensity, 0f, 2.5f);
            shockwaveProgress = 0f;
            shockwaveColor = pulseColor;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float time = Time.time;

            // 1. حساب معامل احتدام الحرب (War Escalation Factor)
            float warMultiplier = CalculateWarIntensity();

            if (actionPulseBoost > 0f)
            {
                actionPulseBoost = Mathf.Max(0f, actionPulseBoost - dt * 1.8f);
            }

            float totalSpeedScale = warMultiplier + actionPulseBoost * 0.8f;

            // 2. التدرج اللوني الناعم للأرضية والغلاف الجوي (Smooth Crossfade)
            if (arenaGroundImage != null)
            {
                float groundPulse = 1f + Mathf.Sin(time * 2.2f) * 0.04f * warMultiplier;
                Color desiredGround = new Color(
                    Mathf.Clamp01(targetGroundTint.r * groundPulse),
                    Mathf.Clamp01(targetGroundTint.g * groundPulse),
                    Mathf.Clamp01(targetGroundTint.b * groundPulse),
                    1f);
                arenaGroundImage.color = Color.Lerp(arenaGroundImage.color, desiredGround, dt * 3.5f);

                // اهتزاز أرضي خفيف في بيئة الزلزال أو المستوى 3 (كارثة عظمى)
                if (activeBehavior == ParticleBehavior.Earthquake || currentLevel == 3)
                {
                    float tremorAmp = (currentLevel == 3) ? 2.8f : 1.5f;
                    float tx = Mathf.Sin(time * 34f) * tremorAmp * Mathf.Max(0f, Mathf.Sin(time * 1.8f));
                    float ty = Mathf.Cos(time * 29f) * tremorAmp * Mathf.Max(0f, Mathf.Sin(time * 1.8f));
                    arenaGroundImage.rectTransform.anchoredPosition = baseGroundPos + new Vector2(tx, ty);
                }
                else
                {
                    arenaGroundImage.rectTransform.anchoredPosition = Vector2.Lerp(arenaGroundImage.rectTransform.anchoredPosition, baseGroundPos, dt * 8f);
                }
            }

            if (ambientTintOverlay != null)
            {
                ambientTintOverlay.color = Color.Lerp(ambientTintOverlay.color, targetAmbientColor, dt * 3.5f);
            }

            // 3. تحريك صدع الاشتباك الوسطي ودوائر الرون/التروس
            UpdateRiftAndSeals(dt, time, totalSpeedScale);

            // 4. تحديث موجة الصدمة التفاعلية
            UpdateShockwave(dt);

            // 5. تحديث البرق والصواعق في العاصفة الرعدية أو المستوى 3
            UpdateLightningEffects(dt);

            // 6. تحديث إطار الحواف السينمائي (Vignette + Danger Heartbeat)
            UpdateVignette(dt, time);

            // 7. تحديث جميع الجزيئات الجوية والحربية الـ 64
            for (int i = 0; i < particles.Count; i++)
            {
                UpdateParticle(particles[i], dt, time, totalSpeedScale);
            }
        }

        private float CalculateWarIntensity()
        {
            float intensity = 1.0f;

            // تأثير مستوى البيئة (1 -> 2 -> 3)
            if (currentLevel == 1) intensity = 1.15f;
            else if (currentLevel == 2) intensity = 1.65f;
            else if (currentLevel == 3) intensity = 2.35f;

            // تأثير خطر نقاط حياة القادة (احتدام الحرب عند انخفاض الـ HP)
            var gm = GameManager.Instance;
            if (gm != null && gm.humanPlayer != null && gm.aiPlayer != null)
            {
                int minHp = Mathf.Min(gm.humanPlayer.currentHp, gm.aiPlayer.currentHp);
                if (minHp < 60) intensity += 0.65f;
                else if (minHp < 120) intensity += 0.30f;
            }

            return intensity;
        }

        private void UpdateRiftAndSeals(float dt, float time, float speedScale)
        {
            if (outerRuneWheel != null)
            {
                outerRuneWheel.Rotate(0f, 0f, 10f * speedScale * dt);
                float s = 1f + Mathf.Sin(time * 1.6f) * 0.04f;
                outerRuneWheel.localScale = new Vector3(s, s, 1f);

                if (outerRuneImage != null)
                {
                    Color c = targetRiftColor;
                    c.a = Mathf.Clamp01((0.18f + (currentLevel * 0.06f)) + Mathf.Sin(time * 2.5f) * 0.05f);
                    outerRuneImage.color = Color.Lerp(outerRuneImage.color, c, dt * 4f);
                }
            }

            if (innerRuneWheel != null)
            {
                innerRuneWheel.Rotate(0f, 0f, -16f * speedScale * dt);
                float s = 1f + Mathf.Cos(time * 2.1f) * 0.05f;
                innerRuneWheel.localScale = new Vector3(s, s, 1f);

                if (innerRuneImage != null)
                {
                    Color c = Color.Lerp(targetRiftColor, Color.white, 0.35f);
                    c.a = Mathf.Clamp01((0.22f + (currentLevel * 0.07f)) + Mathf.Cos(time * 3.0f) * 0.06f);
                    innerRuneImage.color = Color.Lerp(innerRuneImage.color, c, dt * 4f);
                }
            }

            if (centralRiftGlow != null)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(time * 3.8f * Mathf.Sqrt(speedScale));
                Color c = targetRiftColor;
                c.a = Mathf.Clamp01(targetRiftColor.a * 0.55f * pulse + actionPulseBoost * 0.2f);
                centralRiftGlow.color = Color.Lerp(centralRiftGlow.color, c, dt * 6f);

                float heightPulse = 105f + Mathf.Sin(time * 3.2f) * 20f + (currentLevel * 16f) + (actionPulseBoost * 32f);
                centralRiftGlow.rectTransform.sizeDelta = new Vector2(1140f, heightPulse);
            }

            if (centralRiftCore != null)
            {
                Color coreColor = Color.Lerp(targetRiftColor, Color.white, 0.55f);
                coreColor.a = Mathf.Clamp01(0.55f + Mathf.Sin(time * 6.5f) * 0.20f + actionPulseBoost * 0.25f);
                centralRiftCore.color = coreColor;
            }

            // تدوير ونبض حلقات المنصات الحجرية الـ 12 تحت أماكن الوحوش
            for (int i = 0; i < pedestalRuneRings.Count; i++)
            {
                var ring = pedestalRuneRings[i];
                if (ring == null) continue;
                float dir = (i % 2 == 0) ? 1f : -1f;
                ring.rectTransform.Rotate(0f, 0f, dir * 12f * speedScale * dt);

                bool isCoreSlot = (i % 6 == 2 || i % 6 == 3);
                Color ringCol = isCoreSlot ? Color.Lerp(targetRiftColor, Color.white, 0.28f) : targetRiftColor;
                ringCol.a = Mathf.Clamp01((isCoreSlot ? 0.30f : 0.20f) + Mathf.Sin(time * 2.6f + i * 0.5f) * 0.06f + (currentLevel * 0.05f));
                ring.color = Color.Lerp(ring.color, ringCol, dt * 4f);
            }
        }

        private void UpdateShockwave(float dt)
        {
            if (shockwaveRing == null) return;

            if (shockwaveProgress < 1f)
            {
                shockwaveProgress += dt * 1.85f;
                float p = Mathf.Clamp01(shockwaveProgress);
                float size = Mathf.Lerp(120f, 1550f, 1f - (1f - p) * (1f - p));
                shockwaveRing.rectTransform.sizeDelta = new Vector2(size, size);

                Color c = shockwaveColor;
                c.a = (1f - p) * 0.55f;
                shockwaveRing.color = c;
            }
            else if (shockwaveRing.color.a > 0f)
            {
                shockwaveRing.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        private void UpdateLightningEffects(float dt)
        {
            bool allowLightning = (activeBehavior == ParticleBehavior.Thunderstorm) || (currentLevel == 3);
            if (allowLightning)
            {
                nextLightningTimer -= dt;
                if (nextLightningTimer <= 0f)
                {
                    TriggerLightningBolt();
                    nextLightningTimer = (currentLevel == 3)
                        ? UnityEngine.Random.Range(1.2f, 2.6f)
                        : UnityEngine.Random.Range(2.4f, 4.8f);
                }
            }

            if (lightningFade > 0f)
            {
                lightningFade = Mathf.Max(0f, lightningFade - dt * 4.2f);
                Color boltColor = (activeBehavior == ParticleBehavior.Thunderstorm)
                    ? new Color(0.85f, 0.96f, 1f, lightningFade)
                    : new Color(targetRiftColor.r, targetRiftColor.g, targetRiftColor.b, lightningFade * 0.85f);

                for (int i = 0; i < lightningSegments.Count; i++)
                {
                    if (lightningSegments[i] != null)
                        lightningSegments[i].color = boltColor;
                }

                if (lightningFlashOverlay != null)
                {
                    lightningFlashOverlay.color = new Color(boltColor.r, boltColor.g, boltColor.b, lightningFade * 0.16f);
                }
            }
        }

        private void TriggerLightningBolt()
        {
            lightningFade = 0.95f;
            Vector2 currentPt = new Vector2(UnityEngine.Random.Range(-720f, 720f), 540f);

            for (int i = 0; i < lightningSegments.Count; i++)
            {
                var seg = lightningSegments[i];
                if (seg == null) continue;

                Vector2 nextPt = currentPt + new Vector2(UnityEngine.Random.Range(-115f, 115f), UnityEngine.Random.Range(-95f, -130f));
                Vector2 mid = (currentPt + nextPt) * 0.5f;
                Vector2 diff = nextPt - currentPt;
                float len = diff.magnitude;
                float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

                seg.rectTransform.anchoredPosition = mid;
                seg.rectTransform.sizeDelta = new Vector2(len + 12f, UnityEngine.Random.Range(10f, 20f));
                seg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);

                currentPt = nextPt;
            }
        }

        private void UpdateVignette(float dt, float time)
        {
            if (vignetteOverlay == null) return;

            Color desiredVig = targetVignetteColor;

            // نبض أحمر حربي عند انخفاض صحة أحد القادة عن 70 HP
            var gm = GameManager.Instance;
            if (gm != null && gm.humanPlayer != null && gm.aiPlayer != null)
            {
                int minHp = Mathf.Min(gm.humanPlayer.currentHp, gm.aiPlayer.currentHp);
                if (minHp < 70)
                {
                    float dangerPulse = 0.5f + 0.5f * Mathf.Sin(time * 6.0f);
                    desiredVig = Color.Lerp(desiredVig, new Color(0.75f, 0.05f, 0.05f, 0.78f), dangerPulse * 0.6f);
                }
            }

            vignetteOverlay.color = Color.Lerp(vignetteOverlay.color, desiredVig, dt * 3.5f);
        }

        private void ResetParticle(AtmosphereParticle p, bool randomInitialPosition)
        {
            p.life = 0f;
            p.maxLife = UnityEngine.Random.Range(3.2f, 7.5f);
            if (randomInitialPosition)
            {
                p.life = UnityEngine.Random.Range(0f, p.maxLife);
            }

            float randX = UnityEngine.Random.Range(-960f, 960f);
            float randY = UnityEngine.Random.Range(-540f, 540f);

            switch (activeBehavior)
            {
                case ParticleBehavior.VolcanoFire:
                    p.img.sprite = softOrbSprite;
                    p.pos = new Vector2(randX, randomInitialPosition ? randY : UnityEngine.Random.Range(-560f, -120f));
                    p.vel = new Vector2(UnityEngine.Random.Range(-30f, 30f), UnityEngine.Random.Range(75f, 175f));
                    p.size = (p.layerType == 2) ? UnityEngine.Random.Range(140f, 240f) : UnityEngine.Random.Range(14f, 36f);
                    p.baseAlpha = (p.layerType == 2) ? 0.14f : UnityEngine.Random.Range(0.55f, 0.92f);
                    p.baseColor = (p.layerType == 0)
                        ? new Color(1f, UnityEngine.Random.Range(0.35f, 0.75f), 0.1f)
                        : new Color(1f, 0.2f, 0.05f);
                    p.rotSpeed = UnityEngine.Random.Range(-45f, 45f);
                    break;

                case ParticleBehavior.OceanFlood:
                    if (p.layerType == 2)
                    {
                        // دوائر أمواج متسعة (Water Ripples)
                        p.img.sprite = ringSprite;
                        p.pos = new Vector2(UnityEngine.Random.Range(-780f, 780f), UnityEngine.Random.Range(-380f, 380f));
                        p.vel = Vector2.zero;
                        p.size = UnityEngine.Random.Range(40f, 90f);
                        p.baseAlpha = 0.42f;
                        p.baseColor = new Color(0.35f, 0.85f, 1f);
                        p.maxLife = UnityEngine.Random.Range(2.4f, 4.2f);
                    }
                    else if (p.layerType == 1)
                    {
                        // تيارات مائية أفقية
                        p.img.sprite = streakSprite;
                        p.pos = new Vector2(randomInitialPosition ? randX : -1020f, randY);
                        p.vel = new Vector2(UnityEngine.Random.Range(110f, 210f), UnityEngine.Random.Range(-12f, 12f));
                        p.size = UnityEngine.Random.Range(95f, 170f);
                        p.baseAlpha = 0.25f;
                        p.baseColor = new Color(0.2f, 0.7f, 1f);
                        p.rotation = 0f;
                    }
                    else
                    {
                        // فقاعات مائية طافية
                        p.img.sprite = softOrbSprite;
                        p.pos = new Vector2(randX, randomInitialPosition ? randY : -550f);
                        p.vel = new Vector2(UnityEngine.Random.Range(-18f, 18f), UnityEngine.Random.Range(45f, 95f));
                        p.size = UnityEngine.Random.Range(14f, 32f);
                        p.baseAlpha = UnityEngine.Random.Range(0.45f, 0.80f);
                        p.baseColor = new Color(0.45f, 0.92f, 1f);
                    }
                    break;

                case ParticleBehavior.BlizzardIce:
                    if (p.layerType == 1)
                    {
                        // رياح جليدية مائلة سريعة
                        p.img.sprite = streakSprite;
                        p.pos = new Vector2(randomInitialPosition ? randX : UnityEngine.Random.Range(400f, 1050f), randomInitialPosition ? randY : UnityEngine.Random.Range(200f, 580f));
                        p.vel = new Vector2(UnityEngine.Random.Range(-280f, -180f), UnityEngine.Random.Range(-140f, -85f));
                        p.size = UnityEngine.Random.Range(90f, 160f);
                        p.baseAlpha = 0.38f;
                        p.baseColor = new Color(0.75f, 0.96f, 1f);
                        p.rotation = 26f;
                    }
                    else
                    {
                        // ندبات ثلج وصقيع
                        p.img.sprite = softOrbSprite;
                        p.pos = new Vector2(randX, randomInitialPosition ? randY : 560f);
                        p.vel = new Vector2(UnityEngine.Random.Range(-110f, -55f), UnityEngine.Random.Range(-130f, -65f));
                        p.size = (p.layerType == 2) ? UnityEngine.Random.Range(150f, 250f) : UnityEngine.Random.Range(12f, 28f);
                        p.baseAlpha = (p.layerType == 2) ? 0.14f : UnityEngine.Random.Range(0.55f, 0.90f);
                        p.baseColor = new Color(0.82f, 0.97f, 1f);
                    }
                    break;

                case ParticleBehavior.Earthquake:
                    p.img.sprite = (p.layerType == 1) ? streakSprite : softOrbSprite;
                    p.pos = new Vector2(randX, randomInitialPosition ? randY : UnityEngine.Random.Range(-520f, 100f));
                    p.vel = new Vector2(UnityEngine.Random.Range(45f, 120f), UnityEngine.Random.Range(15f, 55f));
                    p.size = (p.layerType == 2) ? UnityEngine.Random.Range(160f, 280f) : UnityEngine.Random.Range(16f, 38f);
                    p.baseAlpha = (p.layerType == 2) ? 0.18f : UnityEngine.Random.Range(0.45f, 0.80f);
                    p.baseColor = new Color(0.82f, 0.64f, 0.38f);
                    p.rotSpeed = UnityEngine.Random.Range(-90f, 90f);
                    break;

                case ParticleBehavior.Thunderstorm:
                    p.img.sprite = (p.layerType == 1) ? streakSprite : softOrbSprite;
                    p.pos = new Vector2(randX, randY);
                    p.vel = new Vector2(UnityEngine.Random.Range(-140f, 140f), UnityEngine.Random.Range(-140f, 140f));
                    p.size = (p.layerType == 1) ? UnityEngine.Random.Range(45f, 95f) : UnityEngine.Random.Range(14f, 30f);
                    p.baseAlpha = UnityEngine.Random.Range(0.55f, 0.95f);
                    p.baseColor = (p.layerType == 0) ? new Color(1f, 0.95f, 0.3f) : new Color(0.45f, 0.88f, 1f);
                    p.maxLife = UnityEngine.Random.Range(0.9f, 2.4f);
                    break;

                case ParticleBehavior.CycloneWind:
                    p.img.sprite = (p.layerType == 0) ? streakSprite : softOrbSprite;
                    float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    float radius = UnityEngine.Random.Range(80f, 820f);
                    p.pos = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.55f);
                    p.vel = new Vector2(radius, angle); // نخزن نصف القطر والزاوية للحركة الحلزونية
                    p.size = (p.layerType == 0) ? UnityEngine.Random.Range(80f, 150f) : UnityEngine.Random.Range(16f, 34f);
                    p.baseAlpha = UnityEngine.Random.Range(0.35f, 0.75f);
                    p.baseColor = new Color(0.45f, 0.98f, 0.80f);
                    break;

                case ParticleBehavior.GreatForest:
                    p.img.sprite = (p.layerType == 1) ? streakSprite : softOrbSprite;
                    p.pos = new Vector2(randX, randY);
                    p.vel = new Vector2(UnityEngine.Random.Range(-35f, 45f), UnityEngine.Random.Range(20f, 60f));
                    p.size = (p.layerType == 2) ? UnityEngine.Random.Range(130f, 220f) : UnityEngine.Random.Range(14f, 30f);
                    p.baseAlpha = (p.layerType == 2) ? 0.14f : UnityEngine.Random.Range(0.55f, 0.92f);
                    p.baseColor = (p.layerType == 0) ? new Color(0.55f, 1f, 0.35f) : new Color(0.25f, 0.90f, 0.45f);
                    p.rotSpeed = UnityEngine.Random.Range(-60f, 60f);
                    break;

                case ParticleBehavior.SacredLight:
                    if (p.layerType == 1)
                    {
                        // أشعة نور سماوية قطرية (God Rays)
                        p.img.sprite = streakSprite;
                        p.pos = new Vector2(randX, UnityEngine.Random.Range(80f, 480f));
                        p.vel = new Vector2(UnityEngine.Random.Range(8f, 22f), UnityEngine.Random.Range(-15f, -5f));
                        p.size = UnityEngine.Random.Range(220f, 360f);
                        p.baseAlpha = 0.22f;
                        p.baseColor = new Color(1f, 0.96f, 0.65f);
                        p.rotation = -52f;
                    }
                    else
                    {
                        p.img.sprite = softOrbSprite;
                        p.pos = new Vector2(randX, randomInitialPosition ? randY : -520f);
                        p.vel = new Vector2(UnityEngine.Random.Range(-15f, 15f), UnityEngine.Random.Range(35f, 80f));
                        p.size = UnityEngine.Random.Range(14f, 34f);
                        p.baseAlpha = UnityEngine.Random.Range(0.50f, 0.90f);
                        p.baseColor = new Color(1f, 0.95f, 0.55f);
                    }
                    break;

                case ParticleBehavior.DeepEclipse:
                    p.img.sprite = softOrbSprite;
                    float ecAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    float ecDist = randomInitialPosition ? UnityEngine.Random.Range(120f, 920f) : UnityEngine.Random.Range(650f, 980f);
                    p.pos = new Vector2(Mathf.Cos(ecAngle) * ecDist, Mathf.Sin(ecAngle) * ecDist * 0.6f);
                    // تتجه نحو مركز الكسوف في منتصف الساحة
                    p.vel = (-p.pos.normalized) * UnityEngine.Random.Range(55f, 125f);
                    p.size = (p.layerType == 2) ? UnityEngine.Random.Range(150f, 260f) : UnityEngine.Random.Range(18f, 42f);
                    p.baseAlpha = (p.layerType == 2) ? 0.22f : UnityEngine.Random.Range(0.50f, 0.88f);
                    p.baseColor = (p.layerType == 0) ? new Color(0.68f, 0.22f, 0.98f) : new Color(0.28f, 0.06f, 0.45f);
                    break;

                case ParticleBehavior.MechaFactory:
                    if (p.layerType == 2)
                    {
                        // تروس ميكانيكية دوارة في الخلفية
                        p.img.sprite = gearWheelSprite;
                        p.pos = new Vector2(UnityEngine.Random.Range(-820f, 820f), UnityEngine.Random.Range(-420f, 420f));
                        p.vel = Vector2.zero;
                        p.size = UnityEngine.Random.Range(85f, 165f);
                        p.baseAlpha = 0.20f;
                        p.baseColor = new Color(0.65f, 0.80f, 0.95f);
                        p.rotSpeed = UnityEngine.Random.Range(-35f, 35f);
                        p.maxLife = UnityEngine.Random.Range(5f, 9f);
                    }
                    else
                    {
                        // شرارات لحام متطايرة
                        p.img.sprite = (p.layerType == 1) ? streakSprite : softOrbSprite;
                        p.pos = new Vector2(randX, randomInitialPosition ? randY : UnityEngine.Random.Range(-250f, 250f));
                        p.vel = new Vector2(UnityEngine.Random.Range(-95f, 95f), UnityEngine.Random.Range(40f, 130f));
                        p.size = UnityEngine.Random.Range(12f, 28f);
                        p.baseAlpha = UnityEngine.Random.Range(0.60f, 0.95f);
                        p.baseColor = (p.layerType == 0) ? new Color(1f, 0.72f, 0.25f) : new Color(0.35f, 0.88f, 1f);
                    }
                    break;

                case ParticleBehavior.GhostRealm:
                    p.img.sprite = softOrbSprite;
                    p.pos = new Vector2(randX, randY);
                    p.vel = new Vector2(UnityEngine.Random.Range(-28f, 28f), UnityEngine.Random.Range(22f, 55f));
                    p.size = (p.layerType == 2) ? UnityEngine.Random.Range(160f, 280f) : UnityEngine.Random.Range(20f, 44f);
                    p.baseAlpha = (p.layerType == 2) ? 0.18f : UnityEngine.Random.Range(0.45f, 0.85f);
                    p.baseColor = (p.layerType == 0) ? new Color(0.45f, 0.95f, 0.88f) : new Color(0.75f, 0.42f, 1f);
                    break;

                case ParticleBehavior.ArcaneSpace:
                    p.img.sprite = (p.layerType == 2) ? runeWheelSprite : softOrbSprite;
                    p.pos = new Vector2(randX, randY);
                    p.vel = new Vector2(UnityEngine.Random.Range(-25f, 25f), UnityEngine.Random.Range(25f, 65f));
                    p.size = (p.layerType == 2) ? UnityEngine.Random.Range(75f, 135f) : UnityEngine.Random.Range(14f, 32f);
                    p.baseAlpha = (p.layerType == 2) ? 0.25f : UnityEngine.Random.Range(0.55f, 0.95f);
                    p.baseColor = (p.layerType == 0) ? new Color(1f, 0.42f, 0.95f) : new Color(0.45f, 0.85f, 1f);
                    p.rotSpeed = UnityEngine.Random.Range(-50f, 50f);
                    break;

                default: // WarEmbers (أجواء الحرب الافتراضية)
                    p.img.sprite = softOrbSprite;
                    p.pos = new Vector2(randX, randomInitialPosition ? randY : UnityEngine.Random.Range(-560f, -150f));
                    p.vel = new Vector2(UnityEngine.Random.Range(-22f, 28f), UnityEngine.Random.Range(38f, 95f));
                    p.size = (p.layerType == 2) ? UnityEngine.Random.Range(150f, 250f) : UnityEngine.Random.Range(10f, 24f);
                    p.baseAlpha = (p.layerType == 2) ? 0.11f : UnityEngine.Random.Range(0.45f, 0.85f);
                    p.baseColor = (p.layerType == 2)
                        ? new Color(0.28f, 0.18f, 0.14f)
                        : new Color(1f, UnityEngine.Random.Range(0.45f, 0.78f), 0.18f);
                    break;
            }

            p.rect.anchoredPosition = p.pos;
            p.rect.sizeDelta = new Vector2(p.size, (p.img.sprite == streakSprite) ? p.size * 0.22f : p.size);
            p.rect.localRotation = Quaternion.Euler(0f, 0f, p.rotation);
        }

        private void UpdateParticle(AtmosphereParticle p, float dt, float time, float speedScale)
        {
            p.life += dt * (0.7f + 0.3f * speedScale);
            if (p.life >= p.maxLife)
            {
                ResetParticle(p, false);
                return;
            }

            float lifeNorm = p.life / p.maxLife;
            // منحنى ظهور واختفاء ناعم (Fade-in & Fade-out)
            float alphaEnvelope = Mathf.Sin(lifeNorm * Mathf.PI);

            if (activeBehavior == ParticleBehavior.CycloneWind)
            {
                // حركة إعصار حلزونية تدور حول مركز الساحة
                float radius = p.vel.x;
                float angle = p.vel.y + dt * (1.25f * speedScale) * (420f / Mathf.Max(120f, radius));
                p.vel = new Vector2(radius, angle);
                p.pos = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.55f);

                float tangentAngle = (angle * Mathf.Rad2Deg) + 90f;
                p.rect.localRotation = Quaternion.Euler(0f, 0f, tangentAngle);
            }
            else if (activeBehavior == ParticleBehavior.OceanFlood && p.layerType == 2)
            {
                // توسع دوائر الأمواج المائية
                float currentSize = Mathf.Lerp(28f, 195f, lifeNorm);
                p.rect.sizeDelta = new Vector2(currentSize, currentSize);
            }
            else if (activeBehavior == ParticleBehavior.Thunderstorm)
            {
                // قفزات كهربائية سريعة
                p.pos += p.vel * (dt * speedScale);
                if (UnityEngine.Random.value < 0.08f)
                {
                    p.vel = new Vector2(UnityEngine.Random.Range(-180f, 180f), UnityEngine.Random.Range(-180f, 180f));
                    p.rotation = Mathf.Atan2(p.vel.y, p.vel.x) * Mathf.Rad2Deg;
                    p.rect.localRotation = Quaternion.Euler(0f, 0f, p.rotation);
                }
            }
            else
            {
                // حركة فيزيائية مع تموج جيبي طبيعي (Sinusoidal Sway)
                float swayX = Mathf.Sin(time * 2.2f + p.phase) * 24f;
                float swayY = (activeBehavior == ParticleBehavior.GhostRealm || activeBehavior == ParticleBehavior.GreatForest)
                    ? Mathf.Cos(time * 1.8f + p.phase) * 18f
                    : 0f;

                p.pos += (p.vel + new Vector2(swayX, swayY)) * (dt * speedScale);

                if (Mathf.Abs(p.rotSpeed) > 0.1f)
                {
                    p.rotation += p.rotSpeed * dt * speedScale;
                    p.rect.localRotation = Quaternion.Euler(0f, 0f, p.rotation);
                }
            }

            p.rect.anchoredPosition = p.pos;

            floatlevelBoost = (currentLevel == 3) ? 1.35f : (currentLevel == 2 ? 1.15f : 1.0f);
            float finalAlpha = Mathf.Clamp01(p.baseAlpha * alphaEnvelope * floatlevelBoost);
            p.img.color = new Color(p.baseColor.r, p.baseColor.g, p.baseColor.b, finalAlpha);
        }

        private float floatlevelBoost = 1f;

        #region Procedural Texture Generators (Zero External Dependencies)

        private static void GenerateProceduralSprites()
        {
            if (softOrbSprite != null) return;

            // 1. Soft Radial Glow Orb (64x64)
            int orbSize = 64;
            var orbTex = new Texture2D(orbSize, orbSize, TextureFormat.RGBA32, false);
            orbTex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < orbSize; y++)
            {
                for (int x = 0; x < orbSize; x++)
                {
                    float dx = (x - orbSize * 0.5f) / (orbSize * 0.5f);
                    float dy = (y - orbSize * 0.5f) / (orbSize * 0.5f);
                    float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    float a = Mathf.Pow(1f - d, 2.2f);
                    orbTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            orbTex.Apply();
            softOrbSprite = Sprite.Create(orbTex, new Rect(0, 0, orbSize, orbSize), new Vector2(0.5f, 0.5f));

            // 2. Tapered Wind / Blizzard / Lightning Streak (128x32)
            int sw = 128, sh = 32;
            var streakTex = new Texture2D(sw, sh, TextureFormat.RGBA32, false);
            streakTex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < sh; y++)
            {
                for (int x = 0; x < sw; x++)
                {
                    float nx = Mathf.Abs((x - sw * 0.5f) / (sw * 0.5f));
                    float ny = Mathf.Abs((y - sh * 0.5f) / (sh * 0.5f));
                    float a = Mathf.Clamp01((1f - nx * nx) * Mathf.Pow(1f - ny, 2.4f));
                    streakTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            streakTex.Apply();
            streakSprite = Sprite.Create(streakTex, new Rect(0, 0, sw, sh), new Vector2(0.5f, 0.5f));

            // 3. Expanding Ripple / Shockwave Ring (128x128)
            int rSize = 128;
            var ringTex = new Texture2D(rSize, rSize, TextureFormat.RGBA32, false);
            ringTex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < rSize; y++)
            {
                for (int x = 0; x < rSize; x++)
                {
                    float dx = (x - rSize * 0.5f) / (rSize * 0.5f);
                    float dy = (y - rSize * 0.5f) / (rSize * 0.5f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(dist - 0.78f) / 0.12f);
                    ringTex.SetPixel(x, y, new Color(1f, 1f, 1f, ring * ring));
                }
            }
            ringTex.Apply();
            ringSprite = Sprite.Create(ringTex, new Rect(0, 0, rSize, rSize), new Vector2(0.5f, 0.5f));

            // 4. Ornate Runic Battle Circle (192x192)
            int rwSize = 192;
            var runeTex = new Texture2D(rwSize, rwSize, TextureFormat.RGBA32, false);
            runeTex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < rwSize; y++)
            {
                for (int x = 0; x < rwSize; x++)
                {
                    float dx = (x - rwSize * 0.5f) / (rwSize * 0.5f);
                    float dy = (y - rwSize * 0.5f) / (rwSize * 0.5f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);

                    float outerRing = Mathf.Clamp01(1f - Mathf.Abs(dist - 0.90f) / 0.04f);
                    float midRing = Mathf.Clamp01(1f - Mathf.Abs(dist - 0.72f) / 0.03f);
                    float innerRing = Mathf.Clamp01(1f - Mathf.Abs(dist - 0.42f) / 0.03f);

                    float spokes = 0f;
                    if (dist > 0.42f && dist < 0.90f)
                    {
                        float spokeWave = Mathf.Abs(Mathf.Sin(angle * 6f));
                        if (spokeWave > 0.96f) spokes = 0.75f;
                    }

                    float runeMarks = 0f;
                    if (dist > 0.75f && dist < 0.87f)
                    {
                        float mark = Mathf.Sin(angle * 24f);
                        if (mark > 0.35f) runeMarks = 0.65f;
                    }

                    float a = Mathf.Clamp01(outerRing + midRing + innerRing + spokes + runeMarks);
                    runeTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            runeTex.Apply();
            runeWheelSprite = Sprite.Create(runeTex, new Rect(0, 0, rwSize, rwSize), new Vector2(0.5f, 0.5f));

            // 5. Mechanical Gear Wheel for Mecha Environment (160x160)
            int gSize = 160;
            var gearTex = new Texture2D(gSize, gSize, TextureFormat.RGBA32, false);
            gearTex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < gSize; y++)
            {
                for (int x = 0; x < gSize; x++)
                {
                    float dx = (x - gSize * 0.5f) / (gSize * 0.5f);
                    float dy = (y - gSize * 0.5f) / (gSize * 0.5f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);

                    float teethRadius = 0.82f + (Mathf.Sin(angle * 12f) > 0f ? 0.10f : 0f);
                    float body = (dist < teethRadius && dist > 0.32f) ? 0.65f : 0f;
                    if (dist > 0.46f && dist < 0.68f && Mathf.Abs(Mathf.Sin(angle * 4f)) < 0.75f)
                        body = 0f; // فتحات الترس الداخلية

                    gearTex.SetPixel(x, y, new Color(1f, 1f, 1f, body));
                }
            }
            gearTex.Apply();
            gearWheelSprite = Sprite.Create(gearTex, new Rect(0, 0, gSize, gSize), new Vector2(0.5f, 0.5f));

            // 6. Central Clash Rift Beam (256x64)
            int bw = 256, bh = 64;
            var beamTex = new Texture2D(bw, bh, TextureFormat.RGBA32, false);
            beamTex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < bh; y++)
            {
                for (int x = 0; x < bw; x++)
                {
                    float ny = (y - bh * 0.5f) / (bh * 0.5f);
                    float nx = (x - bw * 0.5f) / (bw * 0.5f);
                    float wave = Mathf.Sin(nx * Mathf.PI * 5f) * 0.14f;
                    float distY = Mathf.Abs(ny - wave);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - distY), 2.8f) * Mathf.Clamp01(1f - nx * nx * 0.4f);
                    beamTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            beamTex.Apply();
            riftBeamSprite = Sprite.Create(beamTex, new Rect(0, 0, bw, bh), new Vector2(0.5f, 0.5f));

            // 7. Screen-Edge Vignette (192x192)
            int vSize = 192;
            var vigTex = new Texture2D(vSize, vSize, TextureFormat.RGBA32, false);
            vigTex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < vSize; y++)
            {
                for (int x = 0; x < vSize; x++)
                {
                    float dx = (x - vSize * 0.5f) / (vSize * 0.5f);
                    float dy = (y - vSize * 0.5f) / (vSize * 0.5f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((dist - 0.58f) / 0.55f);
                    vigTex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            }
            vigTex.Apply();
            vignetteSprite = Sprite.Create(vigTex, new Rect(0, 0, vSize, vSize), new Vector2(0.5f, 0.5f));
        }

        #endregion
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterCardGame.Audio
{
    /// <summary>
    /// مدير الصوتيات الشامل للعبة كروت الوحوش
    /// يتحكم في مؤثرات تفاعل الكروت والقتال (SFX) والتبديل المتلاشي (Cross-Fade) بين أصوات البيئات الـ 12
    /// يتم إنشاؤه تلقائياً أو عبر المشهد مع قراءة الملفات من Resources/Audio
    /// </summary>
    public class SoundManager : MonoBehaviour
    {
        private static SoundManager _instance;
        public static SoundManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("SoundManager");
                    _instance = go.AddComponent<SoundManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        [Header("قنوات الصوت")]
        private AudioSource sfxSource;
        private AudioSource ambienceSourceA;
        private AudioSource ambienceSourceB;
        private bool isUsingSourceA = true;

        [Header("إعدادات الصوت")]
        [Range(0f, 1f)] public float sfxVolume = 0.85f;
        [Range(0f, 1f)] public float ambienceVolume = 0.40f;

        // قواميس الملفات الصوتية المحملة من Resources
        private Dictionary<string, AudioClip> sfxClips = new Dictionary<string, AudioClip>();
        private Dictionary<string, AudioClip> ambienceClips = new Dictionary<string, AudioClip>();

        private Coroutine crossFadeCoroutine;
        private string currentAmbienceKey = "";

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeAudioSources();
            LoadAllAudioClips();
        }

        private void InitializeAudioSources()
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            ambienceSourceA = gameObject.AddComponent<AudioSource>();
            ambienceSourceA.loop = true;
            ambienceSourceA.playOnAwake = false;
            ambienceSourceA.spatialBlend = 0f;

            ambienceSourceB = gameObject.AddComponent<AudioSource>();
            ambienceSourceB.loop = true;
            ambienceSourceB.playOnAwake = false;
            ambienceSourceB.spatialBlend = 0f;
        }

        private void Start()
        {
            // بدء صوت بيئة المعركة الأساسية تلقائياً بمجرد فتح اللعبة
            if (string.IsNullOrEmpty(currentAmbienceKey))
            {
                SetEnvironmentAmbience("ambience_wind", 1.0f);
            }
        }

        private void LoadAllAudioClips()
        {
            // تحميل مؤثرات الكروت (SFX)
            string[] sfxNames = {
                "card_draw", "card_play", "card_sacrifice",
                "card_attack", "card_hit", "card_death",
                "hp_damage", "turn_end"
            };

            foreach (var name in sfxNames)
            {
                var clip = Resources.Load<AudioClip>("Audio/SFX/" + name);
                if (clip != null) sfxClips[name] = clip;
            }

            // تحميل أصوات البيئات الـ 12 المستمرة (Ambience)
            string[] ambNames = {
                "ambience_fire", "ambience_water", "ambience_ice",
                "ambience_earth", "ambience_electric", "ambience_wind",
                "ambience_forest", "ambience_light", "ambience_dark",
                "ambience_mecha", "ambience_ghost", "ambience_arcane"
            };

            foreach (var name in ambNames)
            {
                var clip = Resources.Load<AudioClip>("Audio/Ambience/" + name);
                if (clip != null) ambienceClips[name] = clip;
            }

            Debug.Log($"🔊 [SoundManager] Loaded {sfxClips.Count} SFX and {ambienceClips.Count} Ambience clips.");
        }

        #region تشغيل مؤثرات الكروت (SFX) مع Pitch Shifting طبيعي

        public void PlaySFX(string clipName, float pitchVariance = 0.05f)
        {
            if (sfxClips.TryGetValue(clipName, out AudioClip clip))
            {
                // تغيير عشوائي طفيف جداً في طبقة الصوت حتى لا يمل اللاعب من التكرار
                float randomPitch = 1.0f + Random.Range(-pitchVariance, pitchVariance);
                sfxSource.pitch = randomPitch;
                sfxSource.PlayOneShot(clip, sfxVolume);
            }
        }

        public void PlayCardDraw() => PlaySFX("card_draw", 0.04f);
        public void PlayCardPlay() => PlaySFX("card_play", 0.05f);
        public void PlayCardSacrifice() => PlaySFX("card_sacrifice", 0.03f);
        public void PlayCardAttack() => PlaySFX("card_attack", 0.06f);
        public void PlayCardHit() => PlaySFX("card_hit", 0.08f);
        public void PlayCardDeath() => PlaySFX("card_death", 0.05f);
        public void PlayHpDamage() => PlaySFX("hp_damage", 0.04f);
        public void PlayTurnEnd() => PlaySFX("turn_end", 0.02f);

        #endregion

        #region أصوات البيئات الـ 12 مع التلاشي الناعم (Cross-Fade)

        /// <summary>
        /// تشغيل صوت البيئة المناسب بحسب عنصر كارت البيئة أو اسم البيئة
        /// </summary>
        public void SetEnvironmentAmbience(string elementOrKey, float fadeDuration = 1.5f)
        {
            string key = MapToAmbienceKey(elementOrKey);
            if (string.IsNullOrEmpty(key) || key == currentAmbienceKey) return;

            if (ambienceClips.TryGetValue(key, out AudioClip newClip))
            {
                currentAmbienceKey = key;
                if (crossFadeCoroutine != null) StopCoroutine(crossFadeCoroutine);
                crossFadeCoroutine = StartCoroutine(CrossFadeAmbience(newClip, fadeDuration));
            }
        }

        private IEnumerator CrossFadeAmbience(AudioClip newClip, float duration)
        {
            AudioSource currentSource = isUsingSourceA ? ambienceSourceA : ambienceSourceB;
            AudioSource newSource = isUsingSourceA ? ambienceSourceB : ambienceSourceA;

            newSource.clip = newClip;
            newSource.volume = 0f;
            newSource.Play();

            float timer = 0f;
            float startVol = currentSource.volume;

            while (timer < duration)
            {
                timer += Time.deltaTime;
                float progress = timer / duration;

                if (currentSource.isPlaying)
                    currentSource.volume = Mathf.Lerp(startVol, 0f, progress);

                newSource.volume = Mathf.Lerp(0f, ambienceVolume, progress);
                yield return null;
            }

            if (currentSource.isPlaying)
            {
                currentSource.Stop();
                currentSource.volume = 0f;
            }
            newSource.volume = ambienceVolume;

            isUsingSourceA = !isUsingSourceA;
        }

        private string MapToAmbienceKey(string input)
        {
            if (string.IsNullOrEmpty(input)) return "ambience_fire";
            string lower = input.Trim().ToLower();

            if (lower.Contains("نار") || lower.Contains("بركان") || lower.Contains("fire")) return "ambience_fire";
            if (lower.Contains("مياه") || lower.Contains("طوفان") || lower.Contains("water")) return "ambience_water";
            if (lower.Contains("جليد") || lower.Contains("صقيع") || lower.Contains("ice")) return "ambience_ice";
            if (lower.Contains("أرض") || lower.Contains("زلزال") || lower.Contains("earth")) return "ambience_earth";
            if (lower.Contains("كهرباء") || lower.Contains("رعد") || lower.Contains("electric")) return "ambience_electric";
            if (lower.Contains("رياح") || lower.Contains("إعصار") || lower.Contains("wind")) return "ambience_wind";
            if (lower.Contains("غابة") || lower.Contains("نبات") || lower.Contains("forest")) return "ambience_forest";
            if (lower.Contains("ضياء") || lower.Contains("نور") || lower.Contains("light")) return "ambience_light";
            if (lower.Contains("ظلام") || lower.Contains("كسوف") || lower.Contains("dark")) return "ambience_dark";
            if (lower.Contains("ميكا") || lower.Contains("آلي") || lower.Contains("mecha")) return "ambience_mecha";
            if (lower.Contains("شبح") || lower.Contains("أثير") || lower.Contains("ghost")) return "ambience_ghost";
            if (lower.Contains("سحر") || lower.Contains("أركين") || lower.Contains("arcane")) return "ambience_arcane";

            return "ambience_fire";
        }

        #endregion
    }
}


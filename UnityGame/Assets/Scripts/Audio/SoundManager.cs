using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterCardGame.Audio
{
    /// <summary>
    /// Comprehensive Sound & Ambience Manager for the Monster Card Game.
    /// Controls SFX and cross-fading between the 12 elemental environment ambiences.
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

        [Header("Audio Channels")]
        private AudioSource sfxSource;
        private AudioSource ambienceSourceA;
        private AudioSource ambienceSourceB;
        private bool isUsingSourceA = true;

        [Header("Audio Settings")]
        [Range(0f, 1f)] public float sfxVolume = 0.85f;
        [Range(0f, 1f)] public float ambienceVolume = 0f; // Disabled per user request: only video audio and card SFX play!

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
            ambienceSourceA.volume = 0f;

            ambienceSourceB = gameObject.AddComponent<AudioSource>();
            ambienceSourceB.loop = true;
            ambienceSourceB.playOnAwake = false;
            ambienceSourceB.spatialBlend = 0f;
            ambienceSourceB.volume = 0f;
        }

        private void Start()
        {
            // Background synthetic ambience/music completely disabled per user request
            StopAllAmbience();
        }

        public void StopAllAmbience()
        {
            if (ambienceSourceA != null) { ambienceSourceA.volume = 0f; ambienceSourceA.Stop(); }
            if (ambienceSourceB != null) { ambienceSourceB.volume = 0f; ambienceSourceB.Stop(); }
        }

        private void LoadAllAudioClips()
        {
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

        #region Card SFX Playback with Natural Pitch Shifting

        public void PlaySFX(string clipName, float pitchVariance = 0.05f)
        {
            if (sfxClips.TryGetValue(clipName, out AudioClip clip))
            {
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

        #region 12 Environment Ambiences with Smooth Cross-Fade

        public void SetEnvironmentAmbience(string elementOrKey, float fadeDuration = 1.5f)
        {
            // All synthetic background ambience/music disabled per user request: "الغى الموسيقى اللى انت عاملها"
            StopAllAmbience();
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

            if (lower.Contains("fire")) return "ambience_fire";
            if (lower.Contains("water")) return "ambience_water";
            if (lower.Contains("ice")) return "ambience_ice";
            if (lower.Contains("earth")) return "ambience_earth";
            if (lower.Contains("electric")) return "ambience_electric";
            if (lower.Contains("air") || lower.Contains("wind")) return "ambience_wind";
            if (lower.Contains("forest")) return "ambience_forest";
            if (lower.Contains("light")) return "ambience_light";
            if (lower.Contains("dark")) return "ambience_dark";
            if (lower.Contains("mecha")) return "ambience_mecha";
            if (lower.Contains("ghost")) return "ambience_ghost";
            if (lower.Contains("arcane")) return "ambience_arcane";

            return "ambience_fire";
        }

        #endregion
    }
}

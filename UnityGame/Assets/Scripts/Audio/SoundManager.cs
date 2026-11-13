using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Audio
{
    /// <summary>
    /// Pure Asset Sound Effects Manager.
    /// Plays only authentic audio assets loaded from Resources/Audio/SFX (zero synthetic/procedural tones).
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
        private AudioSource signatureSfxSource;

        [Header("Audio Settings")]
        [Range(0f, 1f)] public float sfxVolume = 0.88f;
        [Range(0f, 1f)] public float signatureSfxVolume = 0.95f;

        private readonly Dictionary<string, AudioClip> sfxClips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, AudioClip> spellClips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, AudioClip> monsterClips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, AudioClip> envClips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, AudioClip> leaderClips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> lastPlayTimes = new Dictionary<string, float>();

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
            sfxSource.pitch = 1f;

            // Dedicated pure unpitched source for Spells, Signature Monsters, Environments, and Leaders
            signatureSfxSource = gameObject.AddComponent<AudioSource>();
            signatureSfxSource.playOnAwake = false;
            signatureSfxSource.spatialBlend = 0f;
            signatureSfxSource.pitch = 1f;
        }

        public void ReloadAudioClips()
        {
            LoadAllAudioClips();
        }

        private void LoadAllAudioClips()
        {
            sfxClips.Clear();
            spellClips.Clear();
            monsterClips.Clear();
            envClips.Clear();
            leaderClips.Clear();

            // Load every pure audio asset anywhere under Resources/Audio/SFX
            LoadClipsInto("Audio/SFX", sfxClips);
            LoadClipsInto("Audio/SFX/Spells", spellClips);
            LoadClipsInto("Audio/SFX/Monsters", monsterClips);
            LoadClipsInto("Audio/SFX/Environments", envClips);
            LoadClipsInto("Audio/SFX/Leaders", leaderClips);

            Debug.Log($"🔊 [SoundManager] Pure Audio Loaded: {sfxClips.Count} Total SFX | {spellClips.Count} Spells | {monsterClips.Count} Monsters | {envClips.Count} Environments | {leaderClips.Count} Leaders");
        }

        private static void LoadClipsInto(string resourceFolder, Dictionary<string, AudioClip> target)
        {
            AudioClip[] clips = Resources.LoadAll<AudioClip>(resourceFolder);
            if (clips == null) return;

            foreach (var clip in clips)
            {
                if (clip == null || string.IsNullOrEmpty(clip.name)) continue;
                string key = clip.name.Trim().ToLowerInvariant();
                target[key] = clip;
            }
        }

        private bool CheckCooldown(string key, float minInterval)
        {
            if (minInterval <= 0f) return true;
            float now = Time.unscaledTime;
            if (lastPlayTimes.TryGetValue(key, out float prev) && (now - prev) < minInterval)
            {
                return false;
            }
            lastPlayTimes[key] = now;
            return true;
        }

        private bool PlayClip(AudioClip clip, AudioSource channel, float volume, float pitchVariance = 0f, float minInterval = 0f, string cooldownKey = null)
        {
            if (clip == null || channel == null) return false;

            string key = cooldownKey ?? clip.name;
            if (!CheckCooldown(key, minInterval)) return false;

            channel.pitch = (pitchVariance > 0.001f) ? (1.0f + Random.Range(-pitchVariance, pitchVariance)) : 1.0f;
            channel.PlayOneShot(clip, volume);
            return true;
        }

        public bool PlaySFX(string clipName, float pitchVariance = 0.02f, float volumeScale = 1.0f, float minInterval = 0f)
        {
            if (string.IsNullOrEmpty(clipName) || sfxSource == null) return false;
            string key = clipName.Trim().ToLowerInvariant();
            if (!sfxClips.TryGetValue(key, out AudioClip clip) || clip == null) return false;

            return PlayClip(clip, sfxSource, sfxVolume * Mathf.Clamp01(volumeScale), pitchVariance, minInterval, key);
        }

        public bool PlayFirstAvailable(float pitchVariance, float volumeScale, float minInterval, params string[] keys)
        {
            if (keys == null) return false;
            foreach (var k in keys)
            {
                if (string.IsNullOrEmpty(k)) continue;
                string normalized = k.Trim().ToLowerInvariant();
                if (sfxClips.TryGetValue(normalized, out AudioClip clip) && clip != null)
                {
                    return PlayClip(clip, sfxSource, sfxVolume * Mathf.Clamp01(volumeScale), pitchVariance, minInterval, normalized);
                }
            }
            return false;
        }

        #region Core Gameplay & UI SFX

        public void PlayMatchStart() => PlaySFX("match_start", 0f, 1f, 0.25f);
        public void PlayTurnStart(bool isPlayerTurn)
        {
            if (isPlayerTurn) PlayFirstAvailable(0f, 0.95f, 0.15f, "turn_start_player", "turn_start");
            else PlayFirstAvailable(0f, 0.90f, 0.15f, "turn_start_enemy", "turn_start");
        }
        public void PlayTurnEnd() => PlaySFX("turn_end", 0f, 1f, 0.12f);
        public void PlayCombatStart() => PlaySFX("combat_start", 0f, 1f, 0.15f);

        public void PlayCardDraw() => PlaySFX("card_draw", 0.03f, 0.9f, 0.06f);
        public void PlayCardHover() => PlaySFX("card_hover", 0.02f, 0.45f, 0.09f);
        public void PlayCardSelect() => PlaySFX("card_select", 0.02f, 0.75f, 0.05f);
        public void PlayCardSacrifice() => PlaySFX("card_sacrifice", 0.02f, 1f, 0.08f);
        public void PlayCardPlay() => PlayFirstAvailable(0.02f, 1f, 0.05f, "card_summon", "card_play");

        public void PlayCardAttack() => PlaySFX("card_attack", 0.03f, 1f, 0.05f);
        public void PlayCardHit() => PlaySFX("card_hit", 0.03f, 0.95f, 0.05f);
        public void PlayCardShieldBlock() => PlaySFX("card_shield_block", 0.02f, 0.95f, 0.08f);
        public void PlayCardDeath() => PlaySFX("card_death", 0.02f, 1f, 0.07f);

        public void PlayHpDamage() => PlaySFX("hp_damage", 0.02f, 1f, 0.08f);
        public void PlayHpHeal() => PlaySFX("hp_heal", 0.02f, 0.95f, 0.08f);

        public void PlayUiClick() => PlaySFX("ui_click", 0.02f, 0.75f, 0.04f);
        public void PlayInvalidAction() => PlaySFX("error_invalid", 0.01f, 0.85f, 0.12f);
        public void PlayVictory() => PlaySFX("victory", 0f, 1f, 0.5f);
        public void PlayDefeat() => PlaySFX("defeat", 0f, 1f, 0.5f);

        #endregion

        #region Signature Spells, Monsters, Environments & Leaders

        public void PlaySpellCast(CardData spellCard)
        {
            if (spellCard == null)
            {
                PlaySFX("spell_cast", 0f, 1f, 0.08f);
                return;
            }

            AudioClip clip = null;
            string idKey = $"spell_{spellCard.id}";
            if (!spellClips.TryGetValue(idKey, out clip) && !string.IsNullOrEmpty(spellCard.name))
            {
                spellClips.TryGetValue($"spell_{spellCard.name.Trim().ToLowerInvariant()}", out clip);
            }

            if (clip == null)
            {
                string role = (spellCard.role ?? "").ToLowerInvariant();
                if (role.Contains("link")) spellClips.TryGetValue("spell_241", out clip);
                else if (role.Contains("position") || role.Contains("swap")) spellClips.TryGetValue("spell_281", out clip);
                else if (role.Contains("convert") || role.Contains("trade")) spellClips.TryGetValue("spell_321", out clip);
                else if (role.Contains("activate") || role.Contains("support")) spellClips.TryGetValue("spell_361", out clip);
                else if (role.Contains("pierce") || role.Contains("destroy")) spellClips.TryGetValue("spell_401", out clip);
            }

            if (clip == null)
            {
                sfxClips.TryGetValue("spell_cast", out clip);
            }

            if (clip != null)
            {
                PlayClip(clip, signatureSfxSource, signatureSfxVolume, 0f, 0.05f, idKey);
            }
        }

        public bool TryPlayMonsterSignatureClip(CardData monsterCard, string suffix)
        {
            if (monsterCard == null || string.IsNullOrEmpty(suffix)) return false;

            AudioClip clip = null;
            string cleanSuffix = suffix.Trim().ToLowerInvariant();
            string idKey = $"monster_{monsterCard.id}_{cleanSuffix}";

            if (!monsterClips.TryGetValue(idKey, out clip) && !string.IsNullOrEmpty(monsterCard.name))
            {
                monsterClips.TryGetValue($"monster_{monsterCard.name.Trim().ToLowerInvariant()}_{cleanSuffix}", out clip);
            }

            if (clip != null)
            {
                return PlayClip(clip, signatureSfxSource, signatureSfxVolume, 0f, 0.05f, idKey);
            }
            return false;
        }

        public void PlayMonsterSummon(CardData monsterCard)
        {
            if (!TryPlayMonsterSignatureClip(monsterCard, "summon"))
            {
                PlayFirstAvailable(0.02f, 1f, 0.05f, "card_summon", "card_play");
            }
        }

        public void PlayMonsterSkill(CardData monsterCard)
        {
            if (!TryPlayMonsterSignatureClip(monsterCard, "skill"))
            {
                PlayFirstAvailable(0.02f, 1f, 0.05f, "monster_skill", "card_attack");
            }
        }

        public void PlayEnvironmentActivate(EnvironmentCardData env)
        {
            AudioClip clip = null;
            string key = "env_activate";
            if (env != null && !string.IsNullOrEmpty(env.element))
            {
                string elemKey = $"env_{env.element.Trim().ToLowerInvariant()}";
                if (envClips.TryGetValue(elemKey, out clip) || sfxClips.TryGetValue(elemKey, out clip))
                {
                    key = elemKey;
                }
            }

            if (clip == null && !envClips.TryGetValue("env_activate", out clip))
            {
                sfxClips.TryGetValue("env_activate", out clip);
            }

            if (clip != null)
            {
                PlayClip(clip, signatureSfxSource, signatureSfxVolume, 0f, 0.12f, key);
            }
        }

        public void PlayLeaderDeploy(CardData leaderCard)
        {
            AudioClip clip = null;
            string key = "leader_deploy";
            if (leaderCard != null)
            {
                string idKey = $"leader_{leaderCard.id}";
                if (leaderClips.TryGetValue(idKey, out clip) || sfxClips.TryGetValue(idKey, out clip))
                {
                    key = idKey;
                }
            }

            if (clip == null && !leaderClips.TryGetValue("leader_deploy", out clip))
            {
                sfxClips.TryGetValue("leader_deploy", out clip);
            }

            if (clip != null)
            {
                PlayClip(clip, signatureSfxSource, signatureSfxVolume, 0f, 0.12f, key);
            }
        }

        public void SetEnvironmentAmbience(string elementOrKey, float fadeDuration = 1.5f)
        {
            // Legacy procedural ambience removed completely — pure video audio + ElevenLabs SFX only.
        }

        public void StopAllAmbience()
        {
            // No procedural ambience sources exist.
        }

        #endregion
    }
}

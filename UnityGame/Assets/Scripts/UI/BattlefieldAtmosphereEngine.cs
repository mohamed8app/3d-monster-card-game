using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using MonsterCardGame.Core;
using MonsterCardGame.Data;

namespace MonsterCardGame.UI
{
    /// <summary>
    /// Pure Artwork & Cinematic Video Battlefield Engine with Dual-Channel Seamless Crossfade,
    /// Video Audio Playback, and Level 3 Continuous Screen Shake.
    /// </summary>
    public class BattlefieldAtmosphereEngine : MonoBehaviour
    {
        public static BattlefieldAtmosphereEngine Instance { get; private set; }

        [Header("Layer References")]
        public Image arenaGroundImage;
        public Image transitionGroundImage;
        public Image ambientTintOverlay;
        public RectTransform battleBoardRect;
        public RectTransform wholeScreenRect;

        private bool isInitialized = false;
        private static readonly Dictionary<string, Sprite> envSpriteCache = new Dictionary<string, Sprite>();

        // Dual Video Channel Buffer for Butter-Smooth Crossfading
        private class VideoChannel
        {
            public GameObject obj;
            public RawImage rawImage;
            public VideoPlayer player;
            public AudioSource audio;
            public RenderTexture renderTexture;
            public string activePath = "";
            public float currentAlpha = 0f;
            public float targetAlpha = 0f;
            public bool isPreparing = false;
        }

        private VideoChannel channelA;
        private VideoChannel channelB;
        private VideoChannel activeChannel;
        private VideoChannel inactiveChannel;

        private const float CrossfadeDuration = 1.25f; // Seamless transition time
        private const float MaxVideoVolume = 0.95f;   // Video audio playback volume

        // State & Tremor
        private string currentElement = null;
        private int currentLevel = -1;
        private Vector2 baseGroundPos = Vector2.zero;
        private Vector2 baseBoardPos = new Vector2(0f, 58f);
        private float shakeIntensity = 0f;

        public static BattlefieldAtmosphereEngine EnsureInstance(Transform canvasTransform, Image groundImg = null, Image overlayImg = null)
        {
            if (Instance != null)
            {
                if (!Instance.isInitialized || (groundImg != null && Instance.arenaGroundImage == null))
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
                if (!existing.isInitialized || (groundImg != null && existing.arenaGroundImage == null))
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

        private void OnDestroy()
        {
            ReleaseChannel(channelA);
            ReleaseChannel(channelB);
            if (Instance == this) Instance = null;
        }

        private void ReleaseChannel(VideoChannel ch)
        {
            if (ch == null) return;
            if (ch.player != null && ch.player.isPlaying) ch.player.Stop();
            if (ch.renderTexture != null)
            {
                ch.renderTexture.Release();
                Destroy(ch.renderTexture);
                ch.renderTexture = null;
            }
        }

        private void Start()
        {
            if (!isInitialized || arenaGroundImage == null || arenaGroundImage.sprite == null)
            {
                var canvas = GetComponentInParent<Canvas>();
                Image ground = arenaGroundImage;
                if (ground == null && transform.parent != null)
                {
                    var bgTr = transform.parent.Find("ArenaBackground");
                    if (bgTr != null) ground = bgTr.GetComponent<Image>();
                }
                if (ground == null && canvas != null)
                {
                    var bgTr = canvas.transform.Find("CenterBattleBoard/ArenaBackground");
                    if (bgTr == null) bgTr = canvas.transform.Find("ArenaBackground");
                    if (bgTr != null) ground = bgTr.GetComponent<Image>();
                }
                InitializeLayers(
                    transform.parent != null ? transform.parent : (canvas != null ? canvas.transform : transform),
                    ground,
                    ambientTintOverlay != null ? ambientTintOverlay : GetComponent<Image>());
            }
        }

        public void InitializeLayers(Transform canvasTransform, Image groundImg, Image overlayImg)
        {
            isInitialized = true;
            arenaGroundImage = groundImg;

            // Locate ScreenRootContainer (whole screen) and CenterBattleBoard for continuous Level 3 Screen Shake
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                var srTr = canvas.transform.Find("ScreenRootContainer");
                if (srTr != null)
                {
                    wholeScreenRect = srTr.GetComponent<RectTransform>();
                }

                var bbTr = canvas.transform.Find("ScreenRootContainer/CenterBattleBoard");
                if (bbTr == null) bbTr = canvas.transform.Find("CenterBattleBoard");
                if (bbTr != null)
                {
                    battleBoardRect = bbTr.GetComponent<RectTransform>();
                    baseBoardPos = battleBoardRect.anchoredPosition;
                }
            }

            if (arenaGroundImage != null)
            {
                baseGroundPos = arenaGroundImage.rectTransform.anchoredPosition;
                arenaGroundImage.color = Color.white;
                var initialSprite = GetOrLoadGroundSprite("", 0);
                if (initialSprite != null)
                {
                    arenaGroundImage.sprite = initialSprite;
                }

                // 1. Static Ground Crossfade Overlay
                var transTr = arenaGroundImage.transform.Find("GroundCrossfadeOverlay");
                if (transTr == null)
                {
                    var transObj = new GameObject("GroundCrossfadeOverlay");
                    transObj.transform.SetParent(arenaGroundImage.transform, false);
                    var rt = transObj.AddComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    transitionGroundImage = transObj.AddComponent<Image>();
                    transitionGroundImage.raycastTarget = false;
                    transitionGroundImage.color = new Color(1f, 1f, 1f, 0f);
                }
                else
                {
                    transitionGroundImage = transTr.GetComponent<Image>();
                    if (transitionGroundImage != null) transitionGroundImage.color = Color.clear;
                }

                // 2. Dual Video Channel System for 100% Smooth Crossfading & Audio
                channelA = CreateVideoChannel("GroundVideoChannel_A", arenaGroundImage.transform);
                channelB = CreateVideoChannel("GroundVideoChannel_B", arenaGroundImage.transform);
                activeChannel = channelA;
                inactiveChannel = channelB;
            }

            ambientTintOverlay = overlayImg;
            if (ambientTintOverlay != null)
            {
                ambientTintOverlay.raycastTarget = false;
                ambientTintOverlay.color = Color.clear;
            }

            // Ensure no mouse raycasts are blocked
            var cg = gameObject.GetComponent<CanvasGroup>();
            if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            // Remove all obsolete procedural effects/particles/runes/rifts/vignettes
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(transform.GetChild(i).gameObject);
                else
                    DestroyImmediate(transform.GetChild(i).gameObject);
            }

            // Apply current environment or default (00.jpg)
            var activeEnv = (GameManager.Instance != null) ? GameManager.Instance.activeEnvironment : null;
            currentElement = null;
            currentLevel = -1;
            ApplyEnvironmentProfile(activeEnv);
        }

        private VideoChannel CreateVideoChannel(string name, Transform parent)
        {
            var oldChild = parent.Find(name);
            if (oldChild != null)
            {
                if (Application.isPlaying) Destroy(oldChild.gameObject);
                else DestroyImmediate(oldChild.gameObject);
            }

            var vidObj = new GameObject(name);
            vidObj.transform.SetParent(parent, false);
            var vrt = vidObj.AddComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;

            var rawImg = vidObj.AddComponent<RawImage>();
            rawImg.raycastTarget = false;
            rawImg.color = Color.clear;

            var audioSrc = vidObj.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false;
            audioSrc.loop = true;
            audioSrc.spatialBlend = 0f;
            audioSrc.volume = 0f;

            var rt = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
            rt.Create();
            rawImg.texture = rt;

            var player = vidObj.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;
            player.waitForFirstFrame = true;
            player.skipOnDrop = true;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = rt;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            player.controlledAudioTrackCount = 1;
            player.EnableAudioTrack(0, true);
            player.SetTargetAudioSource(0, audioSrc);

            vidObj.SetActive(false);

            var ch = new VideoChannel
            {
                obj = vidObj,
                rawImage = rawImg,
                player = player,
                audio = audioSrc,
                renderTexture = rt,
                currentAlpha = 0f,
                targetAlpha = 0f,
                activePath = "",
                isPreparing = false
            };

            player.prepareCompleted += (source) =>
            {
                try
                {
                    if (source.audioTrackCount > 0)
                    {
                        source.EnableAudioTrack(0, true);
                        source.SetTargetAudioSource(0, ch.audio);
                    }
                }
                catch { }
            };

            return ch;
        }

        /// <summary>
        /// Updates the battlefield image and looping video when the environment or level changes.
        /// </summary>
        public void ApplyEnvironmentProfile(EnvironmentCardData env)
        {
            string elem = env != null ? env.element : "";
            int lvl = env != null ? Mathf.Clamp(env.level, 1, 3) : 0;

            bool changed = (elem != currentElement) || (lvl != currentLevel);
            currentElement = elem;
            currentLevel = lvl;

            if (changed)
            {
                StartGroundCrossfade(elem);
            }
        }

        private static string[] GetEnvironmentSearchFolders()
        {
            string dataPath = Application.dataPath;
            return new string[]
            {
                Path.Combine(Application.streamingAssetsPath, "Environments"),
                Path.GetFullPath(Path.Combine(dataPath, "../Environments")),
                Path.GetFullPath(Path.Combine(dataPath, "../../Environments")),
                Path.GetFullPath(Path.Combine(dataPath, "Resources/Environments"))
            };
        }

        private static string[] GetCandidateNamesForElement(string elem, string key, int lvl = 1)
        {
            if (string.IsNullOrEmpty(elem) || lvl <= 0)
            {
                return new[] { "00", "battlefield", "arena_ground", "ground", "default", "0" };
            }

            string[] baseNames = elem switch
            {
                "Fire" => new[] { "env_fire", "fire", "9989" },
                "Water" => new[] { "env_water", "water", "9990" },
                "Ice" => new[] { "env_ice", "ice", "9991" },
                "Earth" => new[] { "env_earth", "earth", "9992" },
                "Electric" => new[] { "env_electric", "electric", "9993" },
                "Air" => new[] { "env_wind", "env_air", "air", "wind", "9994" },
                "Forest" => new[] { "env_forest", "forest", "9995" },
                "Light" => new[] { "env_light", "light", "9996" },
                "Dark" => new[] { "env_dark", "dark", "9997" },
                "Mecha" => new[] { "env_mecha", "mecha", "9998" },
                "Ghosts" => new[] { "env_ghost", "env_ghosts", "ghosts", "ghost", "9999" },
                "Arcane" => new[] { "env_magic", "env_arcane", "arcane", "magic", "10000" },
                _ => new[] { "00", "battlefield", "arena_ground", "ground", "default", "0" }
            };

            var list = new List<string>();

            // For Fire environment, prioritize 01, 02, 03 corresponding to Levels 1, 2, 3
            if (elem == "Fire")
            {
                list.Add($"0{lvl}");
            }

            // Check level-specific names (e.g., env_fire_1, fire_2, env_fire_lvl3, etc.)
            foreach (string b in baseNames)
            {
                list.Add($"{b}_{lvl}");
                list.Add($"{b}_0{lvl}");
                list.Add($"{b}_lvl{lvl}");
                list.Add($"{b}_level{lvl}");
            }

            // Fallback to base element names
            if (elem == "Fire")
            {
                list.Add("01");
            }
            foreach (string b in baseNames)
            {
                list.Add(b);
            }

            return list.ToArray();
        }

        private static Texture2D TryLoadDiskEnvironmentTexture(string elem, string key, int lvl = 1)
        {
            string[] folders = GetEnvironmentSearchFolders();
            string[] names = GetCandidateNamesForElement(elem, key, lvl);
            string[] exts = { ".jpg", ".png", ".jpeg", ".webp" };

            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                foreach (string name in names)
                {
                    foreach (string ext in exts)
                    {
                        string fullPath = Path.Combine(folder, name + ext);
                        if (File.Exists(fullPath))
                        {
                            try
                            {
                                byte[] bytes = File.ReadAllBytes(fullPath);
                                if (bytes != null && bytes.Length > 0)
                                {
                                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                                    if (tex.LoadImage(bytes))
                                    {
                                        tex.wrapMode = TextureWrapMode.Clamp;
                                        tex.filterMode = FilterMode.Trilinear;
                                        tex.anisoLevel = 16;
                                        tex.mipMapBias = -0.5f;
                                        tex.Apply(true, false);
                                        return tex;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            return null;
        }

        private static string TryFindDiskEnvironmentVideo(string elem, string key, int lvl = 1)
        {
            string[] folders = GetEnvironmentSearchFolders();
            string[] names = GetCandidateNamesForElement(elem, key, lvl);
            string[] videoExts = { ".mp4", ".webm", ".mov" };

            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                foreach (string name in names)
                {
                    foreach (string ext in videoExts)
                    {
                        string fullPath = Path.Combine(folder, name + ext);
                        if (File.Exists(fullPath))
                        {
                            return fullPath;
                        }
                    }
                }
            }

            return null;
        }

        public static Sprite GetOrLoadGroundSprite(string elem, int lvl = 1)
        {
            string baseKey = elem switch
            {
                "Fire" => "fire",
                "Water" => "water",
                "Ice" => "ice",
                "Earth" => "earth",
                "Electric" => "electric",
                "Air" => "wind",
                "Forest" => "forest",
                "Light" => "light",
                "Dark" => "dark",
                "Mecha" => "mecha",
                "Ghosts" => "ghost",
                "Arcane" => "magic",
                _ => "default"
            };

            string cacheKey = (baseKey == "default" || lvl <= 0) ? "default" : $"{baseKey}_{Mathf.Clamp(lvl, 1, 3)}";

            if (envSpriteCache.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                return cached;
            }

            // 1. At runtime, allow external disk/StreamingAssets overrides first.
            // In Editor/scene-baking mode, prefer Resources GUID references to keep scene files lightweight.
            if (Application.isPlaying)
            {
                Texture2D diskTex = TryLoadDiskEnvironmentTexture(elem, baseKey, lvl);
                if (diskTex == null && baseKey != "default")
                {
                    diskTex = TryLoadDiskEnvironmentTexture("", "default", 0);
                }
                if (diskTex != null)
                {
                    var diskSp = Sprite.Create(diskTex, new Rect(0, 0, diskTex.width, diskTex.height), new Vector2(0.5f, 0.5f));
                    envSpriteCache[cacheKey] = diskSp;
                    return diskSp;
                }
            }

            // 2. Built-in Resources (supports level-specific Fire textures 01/02/03 as well as env_<element>)
            Texture2D tex = null;
            if (baseKey == "default" || lvl <= 0)
            {
                tex = Resources.Load<Texture2D>("arena_ground") ?? Resources.Load<Texture2D>("Environments/00");
            }
            else
            {
                if (baseKey == "fire")
                {
                    tex = Resources.Load<Texture2D>($"Environments/0{Mathf.Clamp(lvl, 1, 3)}")
                          ?? Resources.Load<Texture2D>("Environments/01");
                }
                if (tex == null)
                {
                    tex = Resources.Load<Texture2D>($"Environments/env_{baseKey}_{Mathf.Clamp(lvl, 1, 3)}")
                          ?? Resources.Load<Texture2D>($"Environments/env_{baseKey}")
                          ?? Resources.Load<Texture2D>("arena_ground");
                }
            }

            if (tex != null)
            {
                var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                envSpriteCache[cacheKey] = sp;
                return sp;
            }
            return null;
        }

        public void StartGroundCrossfade(string elem)
        {
            if (arenaGroundImage == null) return;

            int lvl = string.IsNullOrEmpty(elem) ? 0 : Mathf.Clamp(currentLevel, 1, 3);
            string key = string.IsNullOrEmpty(elem) ? "default" : elem.ToLowerInvariant();

            var newSprite = GetOrLoadGroundSprite(elem, lvl);
            if (newSprite != null)
            {
                arenaGroundImage.sprite = newSprite;
                arenaGroundImage.color = Color.white;
            }

            string videoPath = TryFindDiskEnvironmentVideo(elem, key, lvl);
            TransitionToVideo(videoPath);
        }

        private void TransitionToVideo(string videoPath)
        {
            if (channelA == null || channelB == null) return;

            // 1. If transitioning to empty video (e.g. neutral weather 00.jpg)
            if (string.IsNullOrEmpty(videoPath))
            {
                channelA.targetAlpha = 0f;
                channelB.targetAlpha = 0f;
                return;
            }

            // 2. If same video is already actively playing, ensure it stays at full volume & opacity
            if (activeChannel != null && activeChannel.activePath == videoPath && activeChannel.player.isPlaying)
            {
                activeChannel.targetAlpha = 1f;
                if (inactiveChannel != null) inactiveChannel.targetAlpha = 0f;
                return;
            }

            // 3. Prepare the inactive channel as the incoming smooth video
            var incoming = inactiveChannel;
            if (incoming == null) return;

            try
            {
                incoming.activePath = videoPath;
                incoming.currentAlpha = 0f;
                incoming.targetAlpha = 0f; // Will become 1f once prepared and first frame starts
                incoming.rawImage.color = new Color(1f, 1f, 1f, 0f);
                incoming.audio.volume = 0f;
                incoming.obj.SetActive(true);

                incoming.player.Stop();
                incoming.player.source = VideoSource.Url;
                incoming.player.url = videoPath;
                incoming.player.isLooping = true;
                incoming.player.targetTexture = incoming.renderTexture;
                incoming.rawImage.texture = incoming.renderTexture;
                incoming.isPreparing = true;

                incoming.player.Prepare();
                incoming.player.Play();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BattlefieldAtmosphereEngine] Video crossfade error: {ex.Message}");
                incoming.targetAlpha = 0f;
                incoming.obj.SetActive(false);
            }
        }

        private void UpdateChannelFade(VideoChannel ch, float dt)
        {
            if (ch == null || !ch.obj.activeSelf) return;

            // Check if freshly prepared video is ready to start cross-fading in
            if (ch.isPreparing)
            {
                if (ch.player.isPrepared && ch.player.frame > 0)
                {
                    ch.isPreparing = false;
                    ch.targetAlpha = 1f;

                    // Swap active channel so the newly playing video becomes active!
                    if (ch == inactiveChannel)
                    {
                        var temp = activeChannel;
                        activeChannel = inactiveChannel;
                        inactiveChannel = temp;
                        inactiveChannel.targetAlpha = 0f; // Fade out the old video
                    }
                }
            }

            // Smooth linear crossfade
            if (Mathf.Abs(ch.currentAlpha - ch.targetAlpha) > 0.001f)
            {
                ch.currentAlpha = Mathf.MoveTowards(ch.currentAlpha, ch.targetAlpha, dt * (1f / CrossfadeDuration));
                ch.rawImage.color = new Color(1f, 1f, 1f, ch.currentAlpha);
                ch.audio.volume = ch.currentAlpha * MaxVideoVolume;

                // When faded out completely, stop and hide
                if (ch.currentAlpha <= 0f && ch.targetAlpha <= 0f && !ch.isPreparing)
                {
                    if (ch.player.isPlaying) ch.player.Stop();
                    ch.audio.Stop();
                    ch.obj.SetActive(false);
                    ch.activePath = "";
                }
            }
        }

        public void TriggerActionPulse(Color pulseColor, float intensity = 1.0f)
        {
            // Procedural overlays remain disabled so the painted artwork and video loop are 100% pure.
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (arenaGroundImage != null)
            {
                arenaGroundImage.color = Color.white;
            }

            if (ambientTintOverlay != null && ambientTintOverlay.color.a > 0f)
            {
                ambientTintOverlay.color = Color.clear;
            }

            // 1. Butter-Smooth Dual-Channel Video & Audio Crossfade
            UpdateChannelFade(channelA, dt);
            UpdateChannelFade(channelB, dt);

            // 2. Level 3 Cataclysm Continuous Screen Tremor (Whole Screen Shake, Reduced Intensity)
            if (currentLevel == 3)
            {
                shakeIntensity = Mathf.MoveTowards(shakeIntensity, 1f, dt * 2.2f);
            }
            else
            {
                shakeIntensity = Mathf.MoveTowards(shakeIntensity, 0f, dt * 3.5f);
            }

            if (shakeIntensity > 0.001f)
            {
                float t = Time.time;
                // High-frequency volcanic rumble + deep seismic oscillation (calibrated to be pleasant)
                float rumbleX = (Mathf.Sin(t * 26f) * 0.55f + Mathf.Sin(t * 13.5f) * 0.45f);
                float rumbleY = (Mathf.Cos(t * 22f) * 0.55f + Mathf.Cos(t * 11.2f) * 0.45f);
                float perlinX = (Mathf.PerlinNoise(t * 18f, 0f) - 0.5f) * 2f;
                float perlinY = (Mathf.PerlinNoise(0f, t * 18f) - 0.5f) * 2f;

                float shakeOffsetX = (rumbleX * 1.9f + perlinX * 1.4f) * shakeIntensity;
                float shakeOffsetY = (rumbleY * 1.6f + perlinY * 1.2f) * shakeIntensity;
                float shakeRot = (Mathf.Sin(t * 15f) * 0.10f + (Mathf.PerlinNoise(t * 13f, t * 13f) - 0.5f) * 0.12f) * shakeIntensity;

                if (wholeScreenRect != null)
                {
                    wholeScreenRect.anchoredPosition = new Vector2(shakeOffsetX, shakeOffsetY);
                    wholeScreenRect.localRotation = Quaternion.Euler(0f, 0f, shakeRot);
                }
                else if (battleBoardRect != null)
                {
                    battleBoardRect.anchoredPosition = baseBoardPos + new Vector2(shakeOffsetX, shakeOffsetY);
                    battleBoardRect.localRotation = Quaternion.Euler(0f, 0f, shakeRot);
                }

                if (arenaGroundImage != null)
                {
                    arenaGroundImage.rectTransform.anchoredPosition = baseGroundPos + new Vector2(shakeOffsetX * 0.25f, shakeOffsetY * 0.25f);
                }
            }
            else
            {
                if (wholeScreenRect != null)
                {
                    wholeScreenRect.anchoredPosition = Vector2.zero;
                    wholeScreenRect.localRotation = Quaternion.identity;
                }
                if (battleBoardRect != null)
                {
                    battleBoardRect.anchoredPosition = baseBoardPos;
                    battleBoardRect.localRotation = Quaternion.identity;
                }
                if (arenaGroundImage != null)
                {
                    arenaGroundImage.rectTransform.anchoredPosition = baseGroundPos;
                }
            }
        }
    }
}

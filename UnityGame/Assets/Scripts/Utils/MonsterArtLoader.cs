using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Utils
{
    /// <summary>
    /// Smart ID-based artwork loader for Monsters, Spells, and Environment cards.
    /// Automatically loads images by Card ID (e.g. 599.jpg) from disk or Resources/CardsArt.
    /// </summary>
    public static class MonsterArtLoader
    {
        private static readonly Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> inspectorSpriteCache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, List<Texture2D>> standbyFramesCache = new Dictionary<string, List<Texture2D>>();
        private static readonly Dictionary<string, Texture2D> depthTextureCache = new Dictionary<string, Texture2D>();
        private static readonly HashSet<string> missingTextureKeys = new HashSet<string>();
        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".webp" };
        private static readonly string[] SubfolderCandidates = { "", "Monsters", "Spells", "Environments" };

        public static string ResolveCardIdKey(CardData data)
        {
            if (data == null) return string.Empty;
            if (data.id > 0) return data.id.ToString();
            if (!string.IsNullOrEmpty(data.name)) return data.name.Trim();
            return string.Empty;
        }

        public static Sprite GetCardSprite(CardData data)
        {
            string key = ResolveCardIdKey(data);
            if (string.IsNullOrEmpty(key)) return null;

            if (spriteCache.TryGetValue(key, out Sprite cached) && cached != null)
            {
                return cached;
            }

            Texture2D tex = LoadCardTexture(key);
            if (tex != null)
            {
                Sprite closeUp = (data != null && data.cardType == CardType.Spell)
                    ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f)
                    : CreateFocusedSpriteFromTexture(tex, 0.02f, 0.06f, 0.96f, 0.92f);
                if (closeUp != null)
                {
                    spriteCache[key] = closeUp;
                    return closeUp;
                }
            }

            return null;
        }

        public static Sprite GetInspectorSprite(CardData data)
        {
            string key = ResolveCardIdKey(data);
            if (string.IsNullOrEmpty(key)) return null;

            if (inspectorSpriteCache.TryGetValue(key, out Sprite cached) && cached != null)
            {
                return cached;
            }

            Texture2D tex = LoadCardTexture(key);
            if (tex != null)
            {
                Sprite showcase = (data != null && data.cardType == CardType.Spell)
                    ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f)
                    : CreateFocusedSpriteFromTexture(tex, 0.02f, 0.22f, 0.96f, 0.75f);
                if (showcase != null)
                {
                    inspectorSpriteCache[key] = showcase;
                    return showcase;
                }
            }

            return GetCardSprite(data);
        }

        private static readonly Dictionary<string, Sprite> _elementalFrameCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        public static Sprite GetCardFrameOverlaySprite(string element = "Fire")
        {
            if (string.IsNullOrWhiteSpace(element)) element = "Fire";
            string cleanElem = element.Trim().ToLowerInvariant();

            if (_elementalFrameCache.TryGetValue(cleanElem, out Sprite cached) && cached != null)
            {
                return cached;
            }

            string[] nameVariants = {
                $"card_frame_{cleanElem}",
                $"card_frame_{cleanElem}.png",
                "card_frame_overlay",
                "card_frame_overlay.png",
                "card_frame_fire",
                "card_frame_fire.png"
            };

            Texture2D tex = null;

            // 1. Check StreamingAssets at runtime for external overrides
            if (Application.isPlaying)
            {
                foreach (var variant in nameVariants)
                {
                    string filename = variant.EndsWith(".png") ? variant : variant + ".png";
                    string streamPath = Path.Combine(Application.streamingAssetsPath, filename);
                    if (File.Exists(streamPath))
                    {
                        tex = LoadTextureFromDisk(streamPath);
                        if (tex != null) break;
                    }
                }
            }

            // 2. Check Resources (primary source; keeps baked Editor scenes lightweight)
            if (tex == null)
            {
                foreach (var variant in nameVariants)
                {
                    string resName = variant.EndsWith(".png") ? variant.Substring(0, variant.Length - 4) : variant;
                    tex = Resources.Load<Texture2D>(resName);
                    if (tex != null) break;
                }
            }

            // 3. Check external disk search directories at runtime
            if (tex == null && Application.isPlaying)
            {
                string[] baseDirs = GetSearchDirectories();
                foreach (string baseDir in baseDirs)
                {
                    if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) continue;
                    foreach (var variant in nameVariants)
                    {
                        string filename = variant.EndsWith(".png") ? variant : variant + ".png";
                        string candidate = Path.Combine(baseDir, filename);
                        if (File.Exists(candidate))
                        {
                            tex = LoadTextureFromDisk(candidate);
                            if (tex != null) break;
                        }
                    }
                    if (tex != null) break;
                }
            }

            if (tex != null)
            {
                tex.filterMode = FilterMode.Bilinear;
                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                _elementalFrameCache[cleanElem] = sprite;
                return sprite;
            }

            return null;
        }

        private static Texture2D LoadCardTexture(string key)
        {
            return LoadTextureByVariantKey(key, true);
        }

        private static Texture2D LoadTextureByVariantKey(string key, bool allowPrefixVariants)
        {
            if (string.IsNullOrEmpty(key)) return null;

            if (textureCache.TryGetValue(key, out Texture2D cachedTex) && cachedTex != null)
            {
                return cachedTex;
            }
            if (missingTextureKeys.Contains(key))
            {
                return null;
            }

            string[] baseDirs = GetSearchDirectories();
            string[] fileVariants = allowPrefixVariants
                ? new[] { key, $"monster_{key}", $"spell_{key}", $"card_{key}" }
                : new[] { key };

            foreach (string baseDir in baseDirs)
            {
                if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) continue;

                foreach (string sub in SubfolderCandidates)
                {
                    string targetDir = string.IsNullOrEmpty(sub) ? baseDir : Path.Combine(baseDir, sub);
                    if (!Directory.Exists(targetDir)) continue;

                    foreach (string nameVar in fileVariants)
                    {
                        foreach (string ext in SupportedExtensions)
                        {
                            string fullPath = Path.Combine(targetDir, nameVar + ext);
                            if (File.Exists(fullPath))
                            {
                                Texture2D diskTex = LoadTextureFromDisk(fullPath);
                                if (diskTex != null)
                                {
                                    textureCache[key] = diskTex;
                                    return diskTex;
                                }
                            }
                        }
                    }
                }
            }

            string[] resourcePaths = {
                $"CardsArt/{key}",
                $"CardsArt/Monsters/{key}",
                $"CardsArt/Spells/{key}",
                $"Monsters/{key}",
                $"Spells/{key}",
                key
            };

            foreach (string resPath in resourcePaths)
            {
                Texture2D resTex = Resources.Load<Texture2D>(resPath);
                if (resTex != null)
                {
                    resTex.wrapMode = TextureWrapMode.Clamp;
                    resTex.filterMode = FilterMode.Trilinear;
                    resTex.anisoLevel = 16;
                    resTex.mipMapBias = -0.65f;
                    textureCache[key] = resTex;
                    return resTex;
                }
            }

            missingTextureKeys.Add(key);
            return null;
        }

        /// <summary>
        /// Loads standby idle frame sequence ([key, key_idle_1, key_idle_2, ...]) if available.
        /// </summary>
        public static List<Texture2D> GetStandbyFrameTextures(CardData data)
        {
            string key = ResolveCardIdKey(data);
            if (string.IsNullOrEmpty(key)) return null;

            if (standbyFramesCache.TryGetValue(key, out var cachedList) && cachedList != null)
            {
                return cachedList;
            }

            var frames = new List<Texture2D>();
            Texture2D baseTex = LoadCardTexture(key);
            if (baseTex != null)
            {
                frames.Add(baseTex);
                for (int idx = 1; idx <= 12; idx++)
                {
                    Texture2D idleTex = LoadTextureByVariantKey($"{key}_idle_{idx}", false);
                    if (idleTex == null) break;
                    frames.Add(idleTex);
                }
            }

            standbyFramesCache[key] = frames;
            return frames;
        }

        /// <summary>
        /// Loads optional grayscale depth map (key_depth) for 2.5D parallax standby motion.
        /// </summary>
        public static Texture2D GetMonsterDepthTexture(CardData data)
        {
            string key = ResolveCardIdKey(data);
            if (string.IsNullOrEmpty(key)) return null;

            if (depthTextureCache.TryGetValue(key, out Texture2D cached))
            {
                return cached;
            }

            Texture2D depthTex = LoadTextureByVariantKey($"{key}_depth", false);
            depthTextureCache[key] = depthTex;
            return depthTex;
        }

        public static bool HasStandby25DAssets(CardData data)
        {
            if (data == null || data.cardType != CardType.Monster) return false;
            if (GetMonsterDepthTexture(data) != null) return true;
            var frames = GetStandbyFrameTextures(data);
            return frames != null && frames.Count > 1;
        }

        public static Sprite GetMonsterSprite(CardData data)
        {
            return GetCardSprite(data);
        }

        /// <summary>
        /// Scans all custom art folders on disk and Resources/CardsArt to return all Card IDs that currently have custom artwork.
        /// </summary>
        public static HashSet<int> GetAvailableCustomArtCardIds()
        {
            var foundIds = new HashSet<int>();
            string[] baseDirs = GetSearchDirectories();

            foreach (string baseDir in baseDirs)
            {
                if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) continue;

                foreach (string sub in SubfolderCandidates)
                {
                    string targetDir = string.IsNullOrEmpty(sub) ? baseDir : Path.Combine(baseDir, sub);
                    if (!Directory.Exists(targetDir)) continue;

                    try
                    {
                        string[] files = Directory.GetFiles(targetDir);
                        foreach (string file in files)
                        {
                            string ext = Path.GetExtension(file).ToLowerInvariant();
                            bool validExt = false;
                            for (int i = 0; i < SupportedExtensions.Length; i++)
                            {
                                if (SupportedExtensions[i] == ext)
                                {
                                    validExt = true;
                                    break;
                                }
                            }
                            if (!validExt) continue;

                            string rawName = Path.GetFileNameWithoutExtension(file).Trim();
                            if (TryParseCardIdFromFileName(rawName, out int parsedId))
                            {
                                foundIds.Add(parsedId);
                            }
                        }
                    }
                    catch { }
                }
            }

            try
            {
                var resTextures = Resources.LoadAll<Texture2D>("CardsArt");
                if (resTextures != null)
                {
                    foreach (var tex in resTextures)
                    {
                        if (tex != null && TryParseCardIdFromFileName(tex.name, out int parsedId))
                        {
                            foundIds.Add(parsedId);
                        }
                    }
                }
            }
            catch { }

            return foundIds;
        }

        private static bool TryParseCardIdFromFileName(string rawName, out int cardId)
        {
            cardId = 0;
            if (string.IsNullOrEmpty(rawName)) return false;

            string cleaned = rawName.ToLowerInvariant();
            string[] prefixes = { "monster_", "spell_", "card_" };
            foreach (string p in prefixes)
            {
                if (cleaned.StartsWith(p))
                {
                    cleaned = cleaned.Substring(p.Length).Trim();
                    break;
                }
            }

            return int.TryParse(cleaned, out cardId) && cardId > 0;
        }

        private static string[] GetSearchDirectories()
        {
            string dataPath = Application.dataPath;
            return new string[]
            {
                Path.Combine(Application.streamingAssetsPath, "CardsArt"),
                Path.GetFullPath(Path.Combine(dataPath, "../CardsArt")),
                Path.GetFullPath(Path.Combine(dataPath, "Resources/CardsArt"))
            };
        }

        private static Texture2D LoadTextureFromDisk(string filePath)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                if (bytes == null || bytes.Length == 0) return null;

                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (tex.LoadImage(bytes))
                {
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Trilinear;
                    tex.anisoLevel = 16;
                    tex.mipMapBias = -0.65f;
                    tex.Apply(true, false);
                    return tex;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MonsterArtLoader] Failed to load texture from {filePath}: {ex.Message}");
            }
            return null;
        }

        private static Sprite _cardBackSprite;
        private static Sprite _spellScrollSprite;
        private static Sprite _playerAvatarSprite;
        private static Sprite _enemyAvatarSprite;

        public static Sprite GetCardBackSprite()
        {
            if (_cardBackSprite != null) return _cardBackSprite;
            _cardBackSprite = LoadSpriteByKey("card_back");
            return _cardBackSprite;
        }

        public static Sprite GetSpellScrollSprite()
        {
            if (_spellScrollSprite != null) return _spellScrollSprite;
            _spellScrollSprite = LoadSpriteByKey("card_scroll_spell");
            return _spellScrollSprite;
        }

        public static Sprite GetPlayerAvatarSprite()
        {
            if (_playerAvatarSprite != null) return _playerAvatarSprite;
            _playerAvatarSprite = LoadSpriteByKey("player_avatar");
            return _playerAvatarSprite;
        }

        public static Sprite GetEnemyAvatarSprite()
        {
            if (_enemyAvatarSprite != null) return _enemyAvatarSprite;
            _enemyAvatarSprite = LoadSpriteByKey("enemy_avatar");
            return _enemyAvatarSprite;
        }

        private static Sprite LoadSpriteByKey(string key)
        {
            Texture2D tex = null;
            string[] extensions = { ".jpg", ".png", ".jpeg" };

            // 1. StreamingAssets override at runtime
            if (Application.isPlaying)
            {
                foreach (var ext in extensions)
                {
                    string p = Path.Combine(Application.streamingAssetsPath, key + ext);
                    if (File.Exists(p))
                    {
                        tex = LoadTextureFromDisk(p);
                        if (tex != null) break;
                    }
                }
            }

            // 2. Resources
            if (tex == null)
            {
                tex = Resources.Load<Texture2D>(key);
            }

            // 3. External search directories at runtime
            if (tex == null && Application.isPlaying)
            {
                string[] baseDirs = GetSearchDirectories();
                foreach (string baseDir in baseDirs)
                {
                    if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) continue;
                    foreach (var ext in extensions)
                    {
                        string p = Path.Combine(baseDir, key + ext);
                        if (File.Exists(p))
                        {
                            tex = LoadTextureFromDisk(p);
                            if (tex != null) break;
                        }
                    }
                    if (tex != null) break;
                }
            }

            if (tex != null)
            {
                tex.filterMode = FilterMode.Trilinear;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            return null;
        }

        private static Sprite CreateFocusedSpriteFromTexture(Texture2D tex, float normX, float normY, float normW, float normH)
        {
            if (tex == null) return null;
            float x = Mathf.Clamp01(normX) * tex.width;
            float y = Mathf.Clamp01(normY) * tex.height;
            float w = Mathf.Clamp(normW, 0.1f, 1f - normX) * tex.width;
            float h = Mathf.Clamp(normH, 0.1f, 1f - normY) * tex.height;
            return Sprite.Create(tex, new Rect(x, y, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        public static Color GetElementColor(string element)
        {
            return element switch
            {
                "Fire" => new Color(1f, 0.35f, 0.15f),
                "Water" => new Color(0.2f, 0.65f, 1f),
                "Ice" => new Color(0.4f, 0.9f, 1f),
                "Electric" => new Color(1f, 0.9f, 0.2f),
                "Dark" => new Color(0.6f, 0.25f, 0.85f),
                "Ghosts" => new Color(0.7f, 0.45f, 0.95f),
                "Mecha" => new Color(0.75f, 0.8f, 0.9f),
                "Arcane" or "Arcane Monsters" => new Color(0.95f, 0.45f, 0.95f),
                "Light" => new Color(1f, 0.95f, 0.55f),
                "Earth" => new Color(0.75f, 0.55f, 0.35f),
                "Air" => new Color(0.4f, 0.95f, 0.75f),
                "Forest" => new Color(0.3f, 0.85f, 0.35f),
                _ => new Color(0.85f, 0.85f, 0.9f)
            };
        }

        public static Color GetEnvironmentAtmosphereColor(string element)
        {
            return element switch
            {
                "Fire" => new Color(0.18f, 0.08f, 0.08f, 0.95f),
                "Water" => new Color(0.06f, 0.12f, 0.22f, 0.95f),
                "Ice" => new Color(0.07f, 0.15f, 0.24f, 0.95f),
                "Earth" => new Color(0.15f, 0.11f, 0.07f, 0.95f),
                "Electric" => new Color(0.14f, 0.13f, 0.06f, 0.95f),
                "Air" => new Color(0.07f, 0.15f, 0.14f, 0.95f),
                "Forest" => new Color(0.06f, 0.15f, 0.08f, 0.95f),
                "Light" => new Color(0.17f, 0.16f, 0.09f, 0.95f),
                "Dark" => new Color(0.09f, 0.05f, 0.15f, 0.95f),
                "Mecha" => new Color(0.11f, 0.13f, 0.16f, 0.95f),
                "Ghosts" => new Color(0.12f, 0.08f, 0.18f, 0.95f),
                "Arcane" or "Arcane Monsters" => new Color(0.16f, 0.06f, 0.18f, 0.95f),
                _ => new Color(0.08f, 0.09f, 0.13f, 1f)
            };
        }

        public static string GetElementEmoji(string element)
        {
            return element switch
            {
                "Fire" => "🔥",
                "Water" => "💧",
                "Ice" => "❄️",
                "Earth" => "🪨",
                "Electric" => "⚡",
                "Air" => "🌪️",
                "Forest" => "🌿",
                "Light" => "✨",
                "Dark" => "🌑",
                "Mecha" => "⚙️",
                "Ghosts" => "👻",
                "Arcane" or "Arcane Monsters" => "🔮",
                _ => "⭐"
            };
        }

        public static string GetSpellIcon(SpellType type)
        {
            return type switch
            {
                SpellType.LinkWingsStrike => "🔗",
                SpellType.CopySupportPassive => "✨",
                SpellType.LinkCenterTwin => "⛓️",
                SpellType.ElementalSynergyShield => "🛡️",
                SpellType.SwapWingWithCenter => "🔄",
                SpellType.PullEnemyCenterToWing => "🧲",
                SpellType.PushEnemyCenterOpenLane => "🌪️",
                SpellType.TeleportAttackerToWeakest => "⚡",
                SpellType.ConvertDefToAtk => "⚔️",
                SpellType.DistributeShieldToWings => "🛡️",
                SpellType.InvertEnemyStats => "🔄",
                SpellType.SiphonDefToAtk => "🩸",
                SpellType.ZeroSkillCostBoost => "⚡",
                SpellType.DoubleStrikeCenter => "🗡️",
                SpellType.CleanseAndFortify => "✨",
                SpellType.SupportBatteryHeal => "💚",
                SpellType.ArcanePiercingShot => "🏹",
                SpellType.DrawTwoAndStrike => "📜",
                SpellType.ReviveFromGraveyard => "💀",
                SpellType.ManaStealAndAOE => "💥",
                SpellType.DirectDamage => "☄️",
                SpellType.BoardAOE => "⚡",
                SpellType.LeaderHeal => "💚",
                SpellType.BoardBuff => "🛡️",
                SpellType.FreezeBoard => "❄️",
                SpellType.DrawAndEnergy => "📜",
                SpellType.LifeDrain => "🩸",
                SpellType.ManaTheft => "🔮",
                SpellType.ExecuteStrongest => "💀",
                SpellType.ArcaneBlast => "💥",
                _ => "✨"
            };
        }

        public static string GetEnvironmentEmoji(string element)
        {
            return element switch
            {
                "Fire" => "🌋",
                "Water" => "🌊",
                "Ice" => "❄️",
                "Earth" => "🪨",
                "Electric" => "⚡",
                "Air" => "🌪️",
                "Forest" => "🌿",
                "Light" => "✨",
                "Dark" => "🌑",
                "Mecha" => "⚙️",
                "Ghosts" => "👻",
                "Arcane" or "Arcane Monsters" => "🔮",
                _ => "☀️"
            };
        }
    }
}

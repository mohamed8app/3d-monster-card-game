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
        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".webp" };
        private static readonly string[] SubfolderCandidates = { "", "Monsters", "Spells", "Environments" };
        private const string LegacyArtFolderName = "\u0635\u0648\u0631_\u0627\u0644\u0643\u0631\u0648\u062A_\u0648\u0627\u0644\u0648\u062D\u0648\u0634";

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
                Sprite closeUp = CreateFocusedSpriteFromTexture(tex, 0.02f, 0.06f, 0.96f, 0.92f);
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
                Sprite showcase = CreateFocusedSpriteFromTexture(tex, 0.02f, 0.22f, 0.96f, 0.75f);
                if (showcase != null)
                {
                    inspectorSpriteCache[key] = showcase;
                    return showcase;
                }
            }

            return GetCardSprite(data);
        }

        private static Texture2D LoadCardTexture(string key)
        {
            if (textureCache.TryGetValue(key, out Texture2D cachedTex) && cachedTex != null)
            {
                return cachedTex;
            }

            string[] baseDirs = GetSearchDirectories();
            string[] fileVariants = { key, $"monster_{key}", $"spell_{key}", $"card_{key}" };

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
                    resTex.filterMode = FilterMode.Trilinear;
                    resTex.anisoLevel = 16;
                    resTex.mipMapBias = -0.65f;
                    textureCache[key] = resTex;
                    return resTex;
                }
            }

            return null;
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
                Path.Combine(@"D:\deck game", LegacyArtFolderName),
                @"D:\deck game\CardsArt",
                @"D:\deck game\UnityGame\Assets\Resources\CardsArt",
                @"D:\deck game\Game_Windows_Exe\CardsArt",
                Path.GetFullPath(Path.Combine(dataPath, "../../" + LegacyArtFolderName)),
                Path.GetFullPath(Path.Combine(dataPath, "../" + LegacyArtFolderName)),
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

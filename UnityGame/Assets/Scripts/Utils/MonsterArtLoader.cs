using System;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Utils
{
    /// <summary>
    /// مدير تحميل وتوزيع رسومات وصور الوحوش الفنية وعناصرها بذكاء.
    /// Manages high-resolution monster illustrations, elemental colors and visual sprites.
    /// </summary>
    public static class MonsterArtLoader
    {
        private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        public static Sprite GetMonsterSprite(MonsterData data)
        {
            if (data == null) return null;

            string key = data.id.ToString();

            // 1. فحص الكاش أولاً
            if (spriteCache.TryGetValue(key, out var cachedSprite) && cachedSprite != null)
            {
                return cachedSprite;
            }

            // 2. البحث عن صورة خاصة برقم الوحش في Resources
            if (Resources.Load<Texture2D>(key) != null)
            {
                var customTex = Resources.Load<Texture2D>(key);
                var customSprite = CreateSpriteFromTexture(customTex);
                spriteCache[key] = customSprite;
                return customSprite;
            }

            // 3. التوزيع الذكي حسب عائلة العنصر (Elemental Archetypes)
            string resourceName = data.element switch
            {
                "نار" => "fire_dragon",
                "ظلام" => "shadow_knight",
                "أشباح" => "shadow_knight",
                "كهرباء" => "lightning_beast",
                "ميكا" => "lightning_beast",
                "مياه" => "frost_leviathan",
                "جليد" => "frost_leviathan",
                "رياح" => "arcane_mage",
                "غابة" => "arcane_mage",
                "سحر" => "arcane_mage",
                "ضياء" => "arcane_mage",
                "أرض" => "lightning_beast",
                _ => "arcane_mage"
            };

            if (spriteCache.TryGetValue(resourceName, out var archetypeSprite) && archetypeSprite != null)
            {
                spriteCache[key] = archetypeSprite;
                return archetypeSprite;
            }

            var tex = Resources.Load<Texture2D>(resourceName);
            if (tex != null)
            {
                var s = CreateSpriteFromTexture(tex);
                spriteCache[resourceName] = s;
                spriteCache[key] = s;
                return s;
            }

            return null;
        }

        private static Sprite CreateSpriteFromTexture(Texture2D tex)
        {
            if (tex == null) return null;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        public static Color GetElementColor(string element)
        {
            return element switch
            {
                "نار" => new Color(1f, 0.35f, 0.15f),
                "مياه" => new Color(0.2f, 0.65f, 1f),
                "جليد" => new Color(0.4f, 0.9f, 1f),
                "كهرباء" => new Color(1f, 0.9f, 0.2f),
                "ظلام" => new Color(0.6f, 0.25f, 0.85f),
                "أشباح" => new Color(0.7f, 0.45f, 0.95f),
                "ميكا" => new Color(0.75f, 0.8f, 0.9f),
                "سحر" => new Color(0.95f, 0.45f, 0.95f),
                "ضياء" => new Color(1f, 0.95f, 0.55f),
                "أرض" => new Color(0.75f, 0.55f, 0.35f),
                "رياح" => new Color(0.4f, 0.95f, 0.75f),
                "غابة" => new Color(0.3f, 0.85f, 0.35f),
                _ => new Color(0.85f, 0.85f, 0.9f)
            };
        }

        public static string GetElementEmoji(string element)
        {
            return element switch
            {
                "نار" => "🔥",
                "مياه" => "💧",
                "جليد" => "❄️",
                "كهرباء" => "⚡",
                "ظلام" => "🌑",
                "أشباح" => "👻",
                "ميكا" => "⚙️",
                "سحر" => "🔮",
                "ضياء" => "✨",
                "أرض" => "⛰️",
                "رياح" => "🌪️",
                "غابة" => "🌿",
                _ => "⚔️"
            };
        }
    }
}

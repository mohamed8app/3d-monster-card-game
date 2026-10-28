using System;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Utils
{
    /// <summary>
    /// مدير تحميل وتوزيع رسومات وصور الوحوش والتعاويذ وكروت البيئة وعناصرها بذكاء.
    /// Manages high-resolution illustrations, elemental colors, spells and weather visuals.
    /// </summary>
    public static class MonsterArtLoader
    {
        private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        public static Sprite GetCardSprite(CardData data)
        {
            if (data == null) return null;

            string key = data.id.ToString();

            // 1. فحص الكاش أولاً
            if (spriteCache.TryGetValue(key, out var cachedSprite) && cachedSprite != null)
            {
                return cachedSprite;
            }

            // 2. البحث عن صورة خاصة برقم البطاقة في Resources
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

        public static Sprite GetMonsterSprite(CardData data)
        {
            return GetCardSprite(data);
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

        public static Color GetEnvironmentAtmosphereColor(string element)
        {
            return element switch
            {
                "نار" => new Color(0.18f, 0.08f, 0.08f, 0.95f),       // وهج بركاني ناري
                "مياه" => new Color(0.06f, 0.12f, 0.22f, 0.95f),      // زرقة المحيط السحيق
                "جليد" => new Color(0.07f, 0.15f, 0.24f, 0.95f),      // صقيع العاصفة الثلجية
                "أرض" => new Color(0.15f, 0.11f, 0.07f, 0.95f),       // تراب وزلازل صخرية
                "كهرباء" => new Color(0.14f, 0.13f, 0.06f, 0.95f),    // عاصفة برق مشحونة
                "رياح" => new Color(0.07f, 0.15f, 0.14f, 0.95f),      // إعصار هوائي عاتٍ
                "غابة" => new Color(0.06f, 0.15f, 0.08f, 0.95f),      // غابة استوائية عميقة
                "ضياء" => new Color(0.17f, 0.16f, 0.09f, 0.95f),      // شفق مقدس ساطع
                "ظلام" => new Color(0.09f, 0.05f, 0.15f, 0.95f),      // كسوف كوني داكن
                "ميكا" => new Color(0.11f, 0.13f, 0.16f, 0.95f),      // مصنع آلي رمادي
                "أشباح" => new Color(0.12f, 0.08f, 0.18f, 0.95f),     // عالم أثيري شبحي
                "سحر" => new Color(0.16f, 0.06f, 0.18f, 0.95f),      // فضاء كوني أركيني
                _ => new Color(0.08f, 0.09f, 0.13f, 1f)
            };
        }

        public static string GetElementEmoji(string element)
        {
            return element switch
            {
                "نار" => "🔥",
                "مياه" => "💧",
                "جليد" => "❄️",
                "أرض" => "🪨",
                "كهرباء" => "⚡",
                "رياح" => "🌪️",
                "غابة" => "🌿",
                "ضياء" => "✨",
                "ظلام" => "🌑",
                "ميكا" => "⚙️",
                "أشباح" => "👻",
                "سحر" => "🔮",
                _ => "⭐"
            };
        }

        public static string GetSpellIcon(SpellType type)
        {
            return type switch
            {
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
                "نار" => "🌋",
                "مياه" => "🌊",
                "جليد" => "❄️",
                "أرض" => "🪨",
                "كهرباء" => "⚡",
                "رياح" => "🌪️",
                "غابة" => "🌿",
                "ضياء" => "✨",
                "ظلام" => "🌑",
                "ميكا" => "⚙️",
                "أشباح" => "👻",
                "سحر" => "🔮",
                _ => "☀️"
            };
        }
    }
}

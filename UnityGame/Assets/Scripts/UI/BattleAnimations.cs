using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MonsterCardGame.Utils;

namespace MonsterCardGame.UI
{
    /// <summary>
    /// محرك مؤثرات الحركة والانتقالات التفاعلية بين الأدوار وحركات الهجوم والإنزال والضرر.
    /// Motion, Transition and Combat Tweening Animations System.
    /// </summary>
    public static class BattleAnimations
    {
        /// <summary>
        /// شريط إعلان مرحلة / انتقال الدور المتحرك (Turn Transition Banner)
        /// </summary>
        public static IEnumerator AnimateTurnBanner(Canvas canvas, string text, Color color, float duration = 1.1f)
        {
            if (canvas == null) yield break;

            var bannerObj = GameBootstrapper.CreatePanel(canvas.transform, "TurnBanner",
                new Color(0.08f, 0.1f, 0.16f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(750, 90));

            var outline = bannerObj.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(3, 3);

            var bannerText = GameBootstrapper.CreateText(bannerObj.transform, "BannerText", text, 28,
                color, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(730, 80));

            var rect = bannerObj.GetComponent<RectTransform>();
            var group = bannerObj.AddComponent<CanvasGroup>();

            rect.localScale = Vector3.one * 0.4f;
            group.alpha = 0f;

            // دخول سريع مع ارتداد (Zoom in + EaseOutBack)
            float t = 0;
            float inDuration = 0.25f;
            while (t < inDuration)
            {
                t += Time.deltaTime;
                float progress = t / inDuration;
                float s = EaseOutBack(progress);
                rect.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.05f, s);
                group.alpha = Mathf.Lerp(0f, 1f, progress);
                yield return null;
            }
            rect.localScale = Vector3.one;
            group.alpha = 1f;

            // بقاء قصير
            yield return new WaitForSeconds(duration - 0.45f);

            // خروج انسيابي (Fade out + Slide up)
            t = 0;
            float outDuration = 0.2f;
            Vector2 startPos = rect.anchoredPosition;
            while (t < outDuration)
            {
                t += Time.deltaTime;
                float progress = t / outDuration;
                rect.anchoredPosition = startPos + new Vector2(0, progress * 40f);
                group.alpha = 1f - progress;
                yield return null;
            }

            GameObject.Destroy(bannerObj);
        }

        /// <summary>
        /// حركة إنزال واستدعاء الكارت (Card Summon Pop-in & Bounce)
        /// </summary>
        public static IEnumerator AnimateCardSummon(Transform target)
        {
            if (target == null) yield break;

            Vector3 originalScale = target.localScale;
            target.localScale = Vector3.one * 0.3f;

            float t = 0;
            float duration = 0.28f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = t / duration;
                float scaleFactor = EaseOutBack(p);
                target.localScale = Vector3.Lerp(Vector3.one * 0.3f, originalScale, scaleFactor);
                yield return null;
            }
            target.localScale = originalScale;
        }

        /// <summary>
        /// حركة اندفاع الهجوم (Lunge Forward & Strike & Spring Back)
        /// </summary>
        public static IEnumerator AnimateCardAttack(Transform attacker, Vector3 targetPos, Action onImpact)
        {
            if (attacker == null)
            {
                onImpact?.Invoke();
                yield break;
            }

            Vector3 startPos = attacker.position;
            Vector3 dir = (targetPos - startPos).normalized;
            Vector3 strikePos = startPos + dir * 85f; // اندفاع سريع نحو الهدف

            // 1. استعداد للخلف (Anticipation)
            float t = 0;
            float pullDuration = 0.12f;
            while (t < pullDuration)
            {
                t += Time.deltaTime;
                float p = t / pullDuration;
                attacker.position = Vector3.Lerp(startPos, startPos - dir * 18f, p);
                yield return null;
            }

            // 2. ضربة سريعة خاطفة (Lunge Forward)
            t = 0;
            float strikeDuration = 0.12f;
            while (t < strikeDuration)
            {
                t += Time.deltaTime;
                float p = t / strikeDuration;
                attacker.position = Vector3.Lerp(startPos - dir * 18f, strikePos, p * p);
                yield return null;
            }

            // 3. لحظة الاصطدام (Impact!)
            attacker.position = strikePos;
            onImpact?.Invoke();
            yield return new WaitForSeconds(0.08f);

            // 4. ارتداد لمكانه الأصلي (Spring Back)
            t = 0;
            float returnDuration = 0.18f;
            while (t < returnDuration)
            {
                t += Time.deltaTime;
                float p = t / returnDuration;
                attacker.position = Vector3.Lerp(strikePos, startPos, EaseOutQuad(p));
                yield return null;
            }
            attacker.position = startPos;
        }

        /// <summary>
        /// اهتزاز تلقي الضرر والتوهج الأحمر (Hit Shake & Red Flash)
        /// </summary>
        public static IEnumerator AnimateHitShake(Transform target, Color flashColor, float intensity = 12f)
        {
            if (target == null) yield break;

            Vector3 originalPos = target.position;
            var img = target.GetComponent<Image>();
            Color originalColor = img != null ? img.color : Color.white;

            float duration = 0.28f;
            float t = 0;

            while (t < duration)
            {
                t += Time.deltaTime;
                float p = 1f - (t / duration); // يتناقص مع الوقت
                float offsetX = Mathf.Sin(t * 50f) * intensity * p;
                target.position = originalPos + new Vector3(offsetX, 0, 0);

                if (img != null)
                {
                    img.color = Color.Lerp(originalColor, flashColor, p * 0.7f);
                }
                yield return null;
            }

            target.position = originalPos;
            if (img != null) img.color = originalColor;
        }

        /// <summary>
        /// أرقام الضرر المتطايرة لأعلى مع التلاشي (Floating Damage Text)
        /// </summary>
        public static IEnumerator AnimateFloatingDamage(Transform canvasTransform, Vector3 worldPos, int damage, bool isLeaderHit)
        {
            if (canvasTransform == null) yield break;

            string content = isLeaderHit ? $"-{damage} HP!" : $"-{damage}";
            Color textColor = isLeaderHit ? new Color(1f, 0.2f, 0.35f) : new Color(1f, 0.5f, 0.2f);
            int fontSize = isLeaderHit ? 30 : 22;

            var obj = new GameObject("FloatingDamage");
            obj.transform.SetParent(canvasTransform, false);
            obj.transform.position = worldPos + new Vector3(0, 20f, 0);

            var rect = obj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(180, 50);

            var outline = obj.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(2, 2);

            var txt = obj.AddComponent<Text>();
            txt.font = GameBootstrapper.GetFont();
            txt.text = ArabicFixer.Fix(content);
            txt.fontSize = fontSize;
            txt.color = textColor;
            txt.alignment = TextAnchor.MiddleCenter;

            var group = obj.AddComponent<CanvasGroup>();

            float duration = 0.85f;
            float t = 0;
            Vector3 startP = obj.transform.position;
            Vector3 targetP = startP + new Vector3(0, 55f, 0);

            while (t < duration)
            {
                t += Time.deltaTime;
                float p = t / duration;
                obj.transform.position = Vector3.Lerp(startP, targetP, EaseOutQuad(p));
                group.alpha = 1f - (p * p);
                yield return null;
            }

            GameObject.Destroy(obj);
        }

        /// <summary>
        /// اهتزاز الشاشة عند إصابة القائد بضربة مباشرة (Screen Shake)
        /// </summary>
        public static IEnumerator AnimateScreenShake(Transform target, float intensity = 8f, float duration = 0.22f)
        {
            if (target == null) yield break;

            Vector3 origin = target.localPosition;
            float t = 0;

            while (t < duration)
            {
                t += Time.deltaTime;
                float p = 1f - (t / duration);
                float x = (UnityEngine.Random.value * 2f - 1f) * intensity * p;
                float y = (UnityEngine.Random.value * 2f - 1f) * intensity * p;
                target.localPosition = origin + new Vector3(x, y, 0);
                yield return null;
            }

            target.localPosition = origin;
        }

        // معادلات التنعيم الرياضية (Easing Functions)
        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3) + c1 * Mathf.Pow(x - 1f, 2);
        }

        private static float EaseOutQuad(float x)
        {
            return 1f - (1f - x) * (1f - x);
        }
    }
}

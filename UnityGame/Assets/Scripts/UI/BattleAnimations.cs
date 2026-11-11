using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MonsterCardGame.Utils;

namespace MonsterCardGame.UI
{
    /// <summary>
    /// Motion, Transition and Combat Tweening Animations System.
    /// </summary>
    public static class BattleAnimations
    {
        /// <summary>
        /// Turn Transition Banner Animation
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

            // Zoom in + EaseOutBack
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

            yield return new WaitForSeconds(duration - 0.45f);

            // Fade out + Slide up
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
        /// Card Summon Pop-in & Bounce
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
        /// Lunge Forward, Strike & Spring Back
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
            Vector3 strikePos = startPos + dir * 115f;

            // 1. Anticipation pullback
            float t = 0;
            float pullDuration = 0.12f;
            while (t < pullDuration)
            {
                t += Time.deltaTime;
                float p = t / pullDuration;
                attacker.position = Vector3.Lerp(startPos, startPos - dir * 22f, p);
                yield return null;
            }

            // 2. Lunge Forward
            t = 0;
            float strikeDuration = 0.12f;
            while (t < strikeDuration)
            {
                t += Time.deltaTime;
                float p = t / strikeDuration;
                attacker.position = Vector3.Lerp(startPos - dir * 18f, strikePos, p * p);
                yield return null;
            }

            // 3. Impact!
            attacker.position = strikePos;
            onImpact?.Invoke();
            yield return new WaitForSeconds(0.08f);

            // 4. Spring Back
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
        /// Hit Shake & Flash
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
                float p = 1f - (t / duration);
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
        /// Floating Damage Text
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
            txt.text = content;
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
        /// Modal Pop-in & Bounce
        /// </summary>
        public static IEnumerator AnimateModalPopIn(Transform target)
        {
            if (target == null) yield break;

            Vector3 origScale = Vector3.one;
            target.localScale = Vector3.one * 0.35f;

            float t = 0;
            float dur = 0.35f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = t / dur;
                target.localScale = Vector3.one * Mathf.Lerp(0.35f, 1f, EaseOutBack(p));
                yield return null;
            }
            target.localScale = origScale;
        }

        /// <summary>
        /// Spell Cast Banner Animation
        /// </summary>
        public static IEnumerator AnimateSpellCast(Canvas canvas, MonsterCardGame.Data.CardData spellCard, string casterName)
        {
            if (canvas == null || spellCard == null) yield break;

            Color elemColor = MonsterArtLoader.GetElementColor(spellCard.element);
            string spellIcon = MonsterArtLoader.GetSpellIcon(spellCard.spellType);

            var bannerObj = GameBootstrapper.CreatePanel(canvas.transform, "SpellCastBanner",
                new Color(0.06f, 0.05f, 0.12f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(620, 85));

            var outline = bannerObj.AddComponent<Outline>();
            outline.effectColor = elemColor;
            outline.effectDistance = new Vector2(3, 3);

            string title = $"{spellIcon} {spellCard.name}";
            var titleTxt = GameBootstrapper.CreateText(bannerObj.transform, "SpellTitle", title, 24,
                Color.white, TextAnchor.UpperCenter, new Vector2(0, 16), new Vector2(600, 32));

            string sub = $"{casterName} cast Spell ({spellCard.element}): {spellCard.description}";
            var subTxt = GameBootstrapper.CreateText(bannerObj.transform, "SpellDesc", sub, 13,
                elemColor, TextAnchor.LowerCenter, new Vector2(0, -18), new Vector2(600, 26));

            var rect = bannerObj.GetComponent<RectTransform>();
            var group = bannerObj.AddComponent<CanvasGroup>();

            rect.localScale = Vector3.one * 0.4f;
            group.alpha = 0f;

            float t = 0;
            float inDur = 0.22f;
            while (t < inDur)
            {
                t += Time.deltaTime;
                float p = t / inDur;
                rect.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.08f, EaseOutBack(p));
                group.alpha = Mathf.Lerp(0f, 1f, p);
                yield return null;
            }
            rect.localScale = Vector3.one;
            group.alpha = 1f;

            yield return new WaitForSeconds(0.9f);

            t = 0;
            float outDur = 0.2f;
            Vector2 startPos = rect.anchoredPosition;
            while (t < outDur)
            {
                t += Time.deltaTime;
                float p = t / outDur;
                rect.anchoredPosition = startPos + new Vector2(0, p * 35f);
                group.alpha = 1f - p;
                yield return null;
            }

            GameObject.Destroy(bannerObj);
        }

        /// <summary>
        /// Environment Shift Banner Animation
        /// </summary>
        public static IEnumerator AnimateEnvironmentShift(Canvas canvas, MonsterCardGame.Data.EnvironmentCardData envData, string activatorName)
        {
            if (canvas == null || envData == null) yield break;

            Color elemColor = MonsterArtLoader.GetElementColor(envData.element);
            string envEmoji = MonsterArtLoader.GetEnvironmentEmoji(envData.element);

            var bannerObj = GameBootstrapper.CreatePanel(canvas.transform, "EnvShiftBanner",
                new Color(0.08f, 0.06f, 0.12f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(720, 95));

            var outline = bannerObj.AddComponent<Outline>();
            outline.effectColor = elemColor;
            outline.effectDistance = new Vector2(3.5f, 3.5f);

            string stars = new string('★', envData.level);
            string title = $"{envEmoji} Weather Shift: {envData.name} ({stars} Level {envData.level})";
            var titleTxt = GameBootstrapper.CreateText(bannerObj.transform, "EnvTitle", title, 24,
                Color.white, TextAnchor.UpperCenter, new Vector2(0, 18), new Vector2(700, 32));

            string sub = $"{activatorName} activated ({envData.element}) weather: {envData.description}";
            var subTxt = GameBootstrapper.CreateText(bannerObj.transform, "EnvDesc", sub, 13,
                elemColor, TextAnchor.LowerCenter, new Vector2(0, -18), new Vector2(700, 26));

            yield return AnimatePopupBanner(bannerObj, 1.1f, 0.25f, 0.22f, 40f);
        }

        /// <summary>
        /// Monster Active Ability Cast Banner Animation
        /// </summary>
        public static IEnumerator AnimateAbilityCast(Canvas canvas, MonsterCardGame.Data.CardData monsterData, string abilityText)
        {
            if (canvas == null || monsterData == null) yield break;

            Color elemColor = MonsterArtLoader.GetElementColor(monsterData.element);
            string elemEmoji = MonsterArtLoader.GetElementEmoji(monsterData.element);

            var bannerObj = GameBootstrapper.CreatePanel(canvas.transform, "AbilityCastBanner",
                new Color(0.05f, 0.07f, 0.14f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 48), new Vector2(660, 88));

            var outline = bannerObj.AddComponent<Outline>();
            outline.effectColor = elemColor;
            outline.effectDistance = new Vector2(3f, 3f);

            string shortName = abilityText ?? "Special Ability";
            int colonIdx = shortName.IndexOf(':');
            string abilityTitle = colonIdx > 0 ? shortName.Substring(0, colonIdx).Trim() : shortName;
            string abilityDesc = colonIdx > 0 ? shortName.Substring(colonIdx + 1).Trim() : shortName;

            GameBootstrapper.CreateText(bannerObj.transform, "AbilityTitle",
                $"⚡ {elemEmoji} Monster #{monsterData.name}: {abilityTitle}", 22,
                Color.white, TextAnchor.UpperCenter, new Vector2(0, 16), new Vector2(636, 30));

            GameBootstrapper.CreateText(bannerObj.transform, "AbilityDesc", abilityDesc, 13,
                elemColor, TextAnchor.LowerCenter, new Vector2(0, -18), new Vector2(636, 26));

            yield return AnimatePopupBanner(bannerObj, 0.95f, 0.22f, 0.20f, 35f);
        }

        private static IEnumerator AnimatePopupBanner(GameObject bannerObj, float holdDuration, float inDur, float outDur, float slideY)
        {
            var rect = bannerObj.GetComponent<RectTransform>();
            var group = bannerObj.AddComponent<CanvasGroup>();

            rect.localScale = Vector3.one * 0.4f;
            group.alpha = 0f;

            float t = 0f;
            while (t < inDur)
            {
                t += Time.deltaTime;
                float p = t / inDur;
                rect.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.05f, EaseOutBack(p));
                group.alpha = Mathf.Lerp(0f, 1f, p);
                yield return null;
            }
            rect.localScale = Vector3.one;
            group.alpha = 1f;

            yield return new WaitForSeconds(holdDuration);

            t = 0f;
            Vector2 startPos = rect.anchoredPosition;
            while (t < outDur)
            {
                t += Time.deltaTime;
                float p = t / outDur;
                rect.anchoredPosition = startPos + new Vector2(0, p * slideY);
                group.alpha = 1f - p;
                yield return null;
            }

            GameObject.Destroy(bannerObj);
        }

        /// <summary>
        /// Floating status / buff label above a board slot or commander
        /// </summary>
        public static IEnumerator AnimateFloatingStatus(Transform canvasTransform, Vector3 worldPos, string label, Color textColor)
        {
            if (canvasTransform == null || string.IsNullOrEmpty(label)) yield break;

            var obj = new GameObject("FloatingStatus");
            obj.transform.SetParent(canvasTransform, false);
            obj.transform.position = worldPos + new Vector3(0, 28f, 0);

            var rect = obj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220, 44);

            var outline = obj.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(2, 2);

            var txt = obj.AddComponent<Text>();
            txt.font = GameBootstrapper.GetFont();
            txt.fontStyle = FontStyle.Bold;
            txt.text = label;
            txt.fontSize = 19;
            txt.color = textColor;
            txt.alignment = TextAnchor.MiddleCenter;

            var group = obj.AddComponent<CanvasGroup>();

            float duration = 0.9f;
            float t = 0f;
            Vector3 startP = obj.transform.position;
            Vector3 targetP = startP + new Vector3(0, 52f, 0);

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

        public static IEnumerator AnimateFloatingHeal(Transform canvasTransform, Vector3 worldPos, int healAmount)
        {
            if (canvasTransform == null) yield break;

            var obj = new GameObject("FloatingHealText");
            obj.transform.SetParent(canvasTransform, false);
            obj.transform.position = worldPos + new Vector3(0, 30f, 0);

            var rect = obj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(180, 50);

            var outline = obj.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0.3f, 0.1f);
            outline.effectDistance = new Vector2(2, 2);

            var txt = obj.AddComponent<Text>();
            txt.font = GameBootstrapper.GetFont();
            txt.text = $"+{healAmount} HP 💚";
            txt.fontSize = 26;
            txt.color = new Color(0.2f, 1f, 0.45f);
            txt.alignment = TextAnchor.MiddleCenter;

            var group = obj.AddComponent<CanvasGroup>();

            float duration = 0.9f;
            float t = 0;
            Vector3 startP = obj.transform.position;
            Vector3 targetP = startP + new Vector3(0, 60f, 0);

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
        /// Radiant expanding shockwave ring + star flash burst when a monster unleashes its Special Ability.
        /// </summary>
        public static IEnumerator AnimateAbilityShockwave(Transform canvasTransform, Transform slotTransform, Color elemColor)
        {
            if (canvasTransform == null || slotTransform == null) yield break;

            Vector3 originPos = slotTransform.position;

            var ringObj = new GameObject("AbilityShockwaveRing");
            ringObj.transform.SetParent(canvasTransform, false);
            ringObj.transform.position = originPos;
            var ringRect = ringObj.AddComponent<RectTransform>();
            ringRect.sizeDelta = new Vector2(180f, 180f);
            var ringImg = ringObj.AddComponent<Image>();
            ringImg.sprite = GameBootstrapper.GetAbilityShockwaveRingSprite();
            ringImg.color = Color.Lerp(elemColor, Color.white, 0.35f);
            ringImg.raycastTarget = false;

            var flashObj = new GameObject("AbilityBurstFlash");
            flashObj.transform.SetParent(canvasTransform, false);
            flashObj.transform.position = originPos;
            var flashRect = flashObj.AddComponent<RectTransform>();
            flashRect.sizeDelta = new Vector2(160f, 160f);
            var flashImg = flashObj.AddComponent<Image>();
            flashImg.sprite = GameBootstrapper.GetAbilityFlashCoreSprite();
            flashImg.color = Color.white;
            flashImg.raycastTarget = false;

            float duration = 0.42f;
            float t = 0f;

            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                float ease = EaseOutQuad(p);

                float ringScale = Mathf.Lerp(0.45f, 2.35f, ease);
                ringRect.localScale = new Vector3(ringScale, ringScale, 1f);
                ringRect.localRotation = Quaternion.Euler(0f, 0f, -p * 65f);
                Color rc = Color.Lerp(elemColor, Color.white, 0.3f);
                rc.a = (1f - p) * 0.95f;
                ringImg.color = rc;

                float flashScale = Mathf.Lerp(0.6f, 1.85f, ease);
                flashRect.localScale = new Vector3(flashScale, flashScale, 1f);
                Color fc = Color.Lerp(Color.white, elemColor, p * 0.6f);
                fc.a = Mathf.Pow(1f - p, 1.5f);
                flashImg.color = fc;

                yield return null;
            }

            GameObject.Destroy(ringObj);
            GameObject.Destroy(flashObj);
        }

        /// <summary>
        /// Screen Shake on Direct Leader Hit
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

        // Easing Functions
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

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
        /// Smooth post-landing settle (replaces sudden 0.3x flash pop)
        /// </summary>
        public static IEnumerator AnimateCardSummon(Transform target)
        {
            yield return AnimateCardSettle(target, 0.16f);
        }

        public static IEnumerator AnimateCardSettle(Transform target, float duration = 0.16f)
        {
            if (target == null) yield break;

            Vector3 originalScale = Vector3.one;
            float t = 0f;
            while (t < duration && target != null)
            {
                t += Mathf.Min(Time.deltaTime, 0.05f);
                float p = Mathf.Clamp01(t / duration);
                // Smooth gentle 1.05 -> 1.00 cushion upon landing (no flash jump)
                float s = Mathf.Lerp(1.05f, 1.0f, EaseOutQuad(p));
                target.localScale = originalScale * s;
                yield return null;
            }
            if (target != null) target.localScale = originalScale;
        }

        /// <summary>
        /// Smooth card draw slide from the Deck origin into the live seat inside Player/Enemy hand.
        /// </summary>
        public static IEnumerator AnimateDrawFlight(
            GameObject flyingCardVisual,
            Vector3 startWorldPos,
            Transform liveTargetHandSlot,
            Vector3 fallbackTargetPos,
            float duration,
            float delay,
            float startTiltDeg,
            Action onArrived)
        {
            if (flyingCardVisual == null)
            {
                onArrived?.Invoke();
                yield break;
            }

            flyingCardVisual.SetActive(false);

            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            if (flyingCardVisual == null)
            {
                onArrived?.Invoke();
                yield break;
            }

            flyingCardVisual.SetActive(true);
            Transform tr = flyingCardVisual.transform;
            tr.position = startWorldPos;
            tr.localScale = Vector3.one * 0.56f;
            tr.localRotation = Quaternion.Euler(0f, 0f, startTiltDeg);

            var cg = flyingCardVisual.GetComponent<CanvasGroup>();
            if (cg == null) cg = flyingCardVisual.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;
            cg.alpha = 0.2f;

            Vector3 lastValidTargetPos = (liveTargetHandSlot != null) ? liveTargetHandSlot.position : fallbackTargetPos;
            float t = 0f;

            while (t < duration && flyingCardVisual != null)
            {
                t += Mathf.Min(Time.deltaTime, 0.05f);
                float p = Mathf.Clamp01(t / duration);
                float ease = EaseOutCubic(p);

                if (liveTargetHandSlot != null)
                {
                    lastValidTargetPos = liveTargetHandSlot.position;
                }

                Vector3 basePos = Vector3.Lerp(startWorldPos, lastValidTargetPos, ease);
                float arc = Mathf.Sin(p * Mathf.PI) * 26f;
                tr.position = basePos + new Vector3(0f, arc, 0f);

                float scale = Mathf.Lerp(0.56f, 1.0f, ease);
                tr.localScale = new Vector3(scale, scale, 1f);
                tr.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startTiltDeg, 0f, ease));
                cg.alpha = Mathf.Clamp01(p * 3.5f);

                yield return null;
            }

            onArrived?.Invoke();

            if (flyingCardVisual != null)
            {
                GameObject.Destroy(flyingCardVisual);
            }
        }

        /// <summary>
        /// Smooth card play / summon / spell / environment / sacrifice flight from hand to target on the board.
        /// Supports an optional mid-air reveal flip for opponent cards leaving a face-down hand.
        /// </summary>
        public static IEnumerator AnimateBoardCardFlight(
            GameObject flyingCardVisual,
            Vector3 startWorldPos,
            Vector3 targetWorldPos,
            float duration,
            float startScale,
            float endScale,
            float arcHeight,
            GameObject revealBackVisual,
            float endFadeOutDuration,
            Action onArrived)
        {
            if (flyingCardVisual == null)
            {
                onArrived?.Invoke();
                yield break;
            }

            Transform tr = flyingCardVisual.transform;
            tr.position = startWorldPos;
            tr.localScale = Vector3.one * startScale;

            var cg = flyingCardVisual.GetComponent<CanvasGroup>();
            if (cg == null) cg = flyingCardVisual.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;
            cg.alpha = 1f;

            bool hasRevealFlip = (revealBackVisual != null);
            if (hasRevealFlip)
            {
                revealBackVisual.SetActive(true);
            }

            float t = 0f;
            while (t < duration && flyingCardVisual != null)
            {
                t += Mathf.Min(Time.deltaTime, 0.05f);
                float p = Mathf.Clamp01(t / duration);
                float ease = EaseInOutCubic(p);

                Vector3 pos = Vector3.Lerp(startWorldPos, targetWorldPos, ease);
                float arc = Mathf.Sin(p * Mathf.PI) * arcHeight;
                tr.position = pos + new Vector3(0f, arc, 0f);

                float baseScale = Mathf.Lerp(startScale, endScale, ease);

                if (hasRevealFlip)
                {
                    if (p < 0.22f)
                    {
                        float fp = p / 0.22f;
                        float flipX = Mathf.Lerp(1f, 0.05f, fp);
                        tr.localScale = new Vector3(baseScale * flipX, baseScale, 1f);
                    }
                    else if (p < 0.44f)
                    {
                        if (revealBackVisual != null && revealBackVisual.activeSelf)
                        {
                            revealBackVisual.SetActive(false);
                        }
                        float fp = (p - 0.22f) / 0.22f;
                        float flipX = Mathf.Lerp(0.05f, 1f, fp);
                        tr.localScale = new Vector3(baseScale * flipX, baseScale, 1f);
                    }
                    else
                    {
                        if (revealBackVisual != null && revealBackVisual.activeSelf)
                        {
                            revealBackVisual.SetActive(false);
                        }
                        tr.localScale = new Vector3(baseScale, baseScale, 1f);
                    }
                }
                else
                {
                    tr.localScale = new Vector3(baseScale, baseScale, 1f);
                }

                // Subtle bank tilt while traveling laterally
                float deltaX = targetWorldPos.x - startWorldPos.x;
                float tilt = Mathf.Sin(p * Mathf.PI) * Mathf.Clamp(-deltaX * 0.012f, -6f, 6f);
                tr.localRotation = Quaternion.Euler(0f, 0f, tilt);

                yield return null;
            }

            if (revealBackVisual != null)
            {
                revealBackVisual.SetActive(false);
            }
            if (tr != null)
            {
                tr.position = targetWorldPos;
                tr.localRotation = Quaternion.identity;
            }

            onArrived?.Invoke();

            if (endFadeOutDuration > 0f && flyingCardVisual != null && cg != null)
            {
                float ft = 0f;
                Vector3 holdScale = tr.localScale;
                while (ft < endFadeOutDuration && flyingCardVisual != null)
                {
                    ft += Mathf.Min(Time.deltaTime, 0.05f);
                    float fp = Mathf.Clamp01(ft / endFadeOutDuration);
                    cg.alpha = 1f - fp;
                    tr.localScale = holdScale * Mathf.Lerp(1f, 0.88f, fp);
                    yield return null;
                }
            }

            if (flyingCardVisual != null)
            {
                GameObject.Destroy(flyingCardVisual);
            }
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

        private static readonly Dictionary<Transform, Color> activeShakeBaseColors = new Dictionary<Transform, Color>();
        private static readonly Dictionary<Transform, Vector3> activeShakeBasePositions = new Dictionary<Transform, Vector3>();
        private static readonly Dictionary<Transform, int> activeShakeTokens = new Dictionary<Transform, int>();

        /// <summary>
        /// Hit Shake & Flash (re-entrant safe: never locks flash tint as permanent base color on overlapping hits)
        /// </summary>
        public static IEnumerator AnimateHitShake(Transform target, Color flashColor, float intensity = 12f)
        {
            if (target == null) yield break;

            var img = target.GetComponent<Image>();

            if (!activeShakeBasePositions.ContainsKey(target))
            {
                activeShakeBasePositions[target] = target.localPosition;
            }
            if (img != null && !activeShakeBaseColors.ContainsKey(target))
            {
                activeShakeBaseColors[target] = img.color;
            }

            Vector3 originalLocalPos = activeShakeBasePositions[target];
            Color originalColor = (img != null && activeShakeBaseColors.TryGetValue(target, out Color bc)) ? bc : Color.white;

            int myToken = (activeShakeTokens.TryGetValue(target, out int prev) ? prev : 0) + 1;
            activeShakeTokens[target] = myToken;

            float duration = 0.28f;
            float t = 0f;

            while (t < duration && target != null)
            {
                if (!activeShakeTokens.TryGetValue(target, out int cur) || cur != myToken)
                {
                    yield break;
                }

                t += Mathf.Min(Time.deltaTime, 0.05f);
                float p = Mathf.Clamp01(1f - (t / duration));
                float offsetX = Mathf.Sin(t * 50f) * intensity * p;
                target.localPosition = originalLocalPos + new Vector3(offsetX, 0f, 0f);

                if (img != null)
                {
                    img.color = Color.Lerp(originalColor, flashColor, p * 0.7f);
                }
                yield return null;
            }

            if (target != null && activeShakeTokens.TryGetValue(target, out int endTok) && endTok == myToken)
            {
                target.localPosition = originalLocalPos;
                if (img != null) img.color = originalColor;
                activeShakeTokens.Remove(target);
                activeShakeBasePositions.Remove(target);
                activeShakeBaseColors.Remove(target);
            }
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
        /// Monster Death Effect (strictly <= 2s, default 1.25s):
        /// Brief fracture shake & spectral soul shockwave followed by ember-ash dissolution and rising soul sparks.
        /// Uses a detached card ghost so the underlying board slot clears immediately without stuck highlights.
        /// </summary>
        public static IEnumerator AnimateMonsterDeath(
            GameObject deathCardVisual,
            Transform vfxParent,
            Vector3 worldPos,
            Color elemColor,
            float duration = 1.25f,
            Action onCleanup = null)
        {
            duration = Mathf.Clamp(duration, 0.55f, 1.85f);
            Transform layer = (deathCardVisual != null && deathCardVisual.transform.parent != null)
                ? deathCardVisual.transform.parent
                : vfxParent;
            if (layer == null)
            {
                if (deathCardVisual != null) GameObject.Destroy(deathCardVisual);
                onCleanup?.Invoke();
                yield break;
            }

            // Force zero raycasts on detached ghost
            if (deathCardVisual != null)
            {
                deathCardVisual.transform.position = worldPos;
                var cgGhost = deathCardVisual.GetComponent<CanvasGroup>();
                if (cgGhost == null) cgGhost = deathCardVisual.AddComponent<CanvasGroup>();
                cgGhost.blocksRaycasts = false;
                cgGhost.interactable = false;
                cgGhost.alpha = 1f;
                foreach (var g in deathCardVisual.GetComponentsInChildren<Graphic>(true))
                {
                    if (g != null) g.raycastTarget = false;
                }
            }

            // 1. Expanding Spectral Soul Shockwave Ring & Core Flare
            var ringObj = new GameObject("DeathShockwaveRing");
            ringObj.transform.SetParent(layer, false);
            ringObj.transform.position = worldPos;
            var ringRect = ringObj.AddComponent<RectTransform>();
            ringRect.sizeDelta = new Vector2(180f, 180f);
            var ringImg = ringObj.AddComponent<Image>();
            ringImg.sprite = GameBootstrapper.GetAbilityShockwaveRingSprite();
            Color crimsonSoul = Color.Lerp(elemColor, new Color(0.95f, 0.24f, 0.30f), 0.58f);
            ringImg.color = crimsonSoul;
            ringImg.raycastTarget = false;

            var coreObj = new GameObject("DeathCoreFlare");
            coreObj.transform.SetParent(layer, false);
            coreObj.transform.position = worldPos;
            var coreRect = coreObj.AddComponent<RectTransform>();
            coreRect.sizeDelta = new Vector2(165f, 165f);
            var coreImg = coreObj.AddComponent<Image>();
            coreImg.sprite = GameBootstrapper.GetCircularBlueFlareSprite();
            coreImg.color = Color.Lerp(crimsonSoul, Color.white, 0.35f);
            coreImg.raycastTarget = false;

            // 2. Rising Ember & Soul Dust Motes (10 particles)
            const int moteCount = 10;
            var motes = new GameObject[moteCount];
            var moteImgs = new Image[moteCount];
            var moteStart = new Vector3[moteCount];
            var moteVel = new Vector3[moteCount];
            var moteSizes = new float[moteCount];
            Sprite circleSp = GameBootstrapper.GetCircleSprite();
            Sprite flareSp = GameBootstrapper.GetAbilityFlashCoreSprite();

            for (int i = 0; i < moteCount; i++)
            {
                var mObj = new GameObject($"DeathSoulMote_{i}");
                mObj.transform.SetParent(layer, false);
                var mRect = mObj.AddComponent<RectTransform>();
                float sz = UnityEngine.Random.Range(10f, 22f);
                mRect.sizeDelta = new Vector2(sz, sz);
                var mImg = mObj.AddComponent<Image>();
                mImg.sprite = (i % 3 == 0) ? flareSp : circleSp;
                mImg.color = (i % 2 == 0)
                    ? Color.Lerp(crimsonSoul, new Color(1f, 0.65f, 0.25f), 0.45f)
                    : Color.Lerp(elemColor, Color.white, 0.35f);
                mImg.raycastTarget = false;

                Vector3 offset = new Vector3(
                    UnityEngine.Random.Range(-56f, 56f),
                    UnityEngine.Random.Range(-70f, 55f),
                    0f);
                mObj.transform.position = worldPos + offset;

                motes[i] = mObj;
                moteImgs[i] = mImg;
                moteStart[i] = worldPos + offset;
                moteVel[i] = new Vector3(
                    offset.x * UnityEngine.Random.Range(0.75f, 1.45f) + UnityEngine.Random.Range(-22f, 22f),
                    UnityEngine.Random.Range(55f, 125f),
                    0f);
                moteSizes[i] = sz;
            }

            // Collect all child images of the dying card so they smoothly char & dissolve
            Graphic[] ghostGraphics = deathCardVisual != null
                ? deathCardVisual.GetComponentsInChildren<Graphic>(true)
                : new Graphic[0];
            Color[] baseColors = new Color[ghostGraphics.Length];
            for (int i = 0; i < ghostGraphics.Length; i++)
            {
                baseColors[i] = ghostGraphics[i] != null ? ghostGraphics[i].color : Color.white;
            }
            CanvasGroup cardCg = deathCardVisual != null ? deathCardVisual.GetComponent<CanvasGroup>() : null;
            float tiltTarget = (UnityEngine.Random.value < 0.5f ? -1f : 1f) * UnityEngine.Random.Range(5f, 9f);

            float t = 0f;
            float shatterCut = 0.16f;
            while (t < duration)
            {
                t += Mathf.Min(Time.deltaTime, 0.05f);
                float p = Mathf.Clamp01(t / duration);

                // Shockwave ring rapid expansion (0 -> 0.50s)
                if (ringObj != null)
                {
                    float rp = Mathf.Clamp01(t / 0.50f);
                    float rEase = EaseOutCubic(rp);
                    float rScale = Mathf.Lerp(0.45f, 2.05f, rEase);
                    ringRect.localScale = new Vector3(rScale, rScale, 1f);
                    Color rc = crimsonSoul;
                    rc.a = (1f - rp) * 0.90f;
                    ringImg.color = rc;
                }

                // Core flare peak then dissolve (0 -> 0.65s)
                if (coreObj != null)
                {
                    float cp = Mathf.Clamp01(t / 0.65f);
                    float cScale = Mathf.Lerp(0.55f, 1.55f, EaseOutQuad(cp));
                    coreRect.localScale = new Vector3(cScale, cScale, 1f);
                    Color cc = Color.Lerp(Color.white, crimsonSoul, Mathf.Clamp01(cp * 1.3f));
                    cc.a = (1f - cp * cp) * 0.85f;
                    coreImg.color = cc;
                }

                // Dying Card Ghost: Brief violent tremor -> scorch & shrink & float dissolve
                if (deathCardVisual != null)
                {
                    if (t < shatterCut)
                    {
                        float sp = t / shatterCut;
                        float shakeX = Mathf.Sin(t * 78f) * 7.5f * (1f - sp * 0.4f);
                        deathCardVisual.transform.position = worldPos + new Vector3(shakeX, 0f, 0f);
                        deathCardVisual.transform.localScale = Vector3.one * Mathf.Lerp(1.06f, 0.98f, sp);
                    }
                    else
                    {
                        float dp = Mathf.Clamp01((t - shatterCut) / (duration - shatterCut));
                        float dEase = EaseOutQuad(dp);
                        deathCardVisual.transform.position = worldPos + new Vector3(0f, dEase * 36f, 0f);
                        float sc = Mathf.Lerp(0.98f, 0.68f, dEase);
                        deathCardVisual.transform.localScale = new Vector3(sc, sc, 1f);
                        deathCardVisual.transform.localRotation = Quaternion.Euler(0f, 0f, tiltTarget * dEase);
                        if (cardCg != null)
                        {
                            cardCg.alpha = Mathf.Pow(1f - dp, 1.35f);
                        }
                    }

                    // Scorch tint transition (flash bright -> ember crimson/ash)
                    Color ashTint = new Color(0.28f, 0.16f, 0.22f, 1f);
                    float tintP = Mathf.Clamp01(t / 0.35f);
                    Color flashMix = (t < 0.11f)
                        ? Color.Lerp(Color.white, crimsonSoul, t / 0.11f)
                        : Color.Lerp(crimsonSoul, ashTint, tintP);
                    for (int i = 0; i < ghostGraphics.Length; i++)
                    {
                        if (ghostGraphics[i] != null)
                        {
                            Color bc = baseColors[i];
                            Color tinted = Color.Lerp(bc, flashMix * bc, 0.72f);
                            tinted.a = bc.a;
                            ghostGraphics[i].color = tinted;
                        }
                    }
                }

                // Rising drifting ember & soul dust motes
                for (int i = 0; i < moteCount; i++)
                {
                    if (motes[i] == null) continue;
                    float mp = Mathf.Clamp01((p - i * 0.025f) / 0.85f);
                    float mEase = EaseOutQuad(mp);
                    float sway = Mathf.Sin(t * 7f + i * 1.4f) * 10f * mp;
                    motes[i].transform.position = moteStart[i] + moteVel[i] * mEase + new Vector3(sway, 0f, 0f);
                    float sc = Mathf.Lerp(1.15f, 0.15f, mp);
                    motes[i].transform.localScale = new Vector3(sc, sc, 1f);
                    if (moteImgs[i] != null)
                    {
                        Color mc = moteImgs[i].color;
                        mc.a = Mathf.Clamp01(mp * 5f) * Mathf.Pow(1f - mp, 1.25f) * 0.92f;
                        moteImgs[i].color = mc;
                    }
                }

                yield return null;
            }

            if (deathCardVisual != null) GameObject.Destroy(deathCardVisual);
            if (ringObj != null) GameObject.Destroy(ringObj);
            if (coreObj != null) GameObject.Destroy(coreObj);
            for (int i = 0; i < moteCount; i++)
            {
                if (motes[i] != null) GameObject.Destroy(motes[i]);
            }

            onCleanup?.Invoke();
        }

        /// <summary>
        /// Magical Battlefield Mirage Effect ("سراب سحري") clipped cleanly inside CenterBattleBoard (1200x688)
        /// when activating a Spell card. Transforms the arena with flowing iridescent caustic ripples,
        /// refractive prism sweep waves, and rising shimmering haze wisps without resizing arena_ground.
        /// </summary>
        public static IEnumerator AnimateBattlefieldMirage(RectTransform battleBoardRect, Color elemColor, float duration = 1.55f)
        {
            if (battleBoardRect == null) yield break;

            // Lower Floor Mirage Layer (sits right above ArenaBackground / Video and beneath battle slots)
            var floorOverlay = new GameObject("SpellMirageFloorOverlay");
            floorOverlay.transform.SetParent(battleBoardRect, false);
            int targetIdx = Mathf.Min(2, battleBoardRect.childCount - 1);
            floorOverlay.transform.SetSiblingIndex(targetIdx);
            var fRect = floorOverlay.AddComponent<RectTransform>();
            fRect.anchorMin = Vector2.zero;
            fRect.anchorMax = Vector2.one;
            fRect.offsetMin = Vector2.zero;
            fRect.offsetMax = Vector2.zero;
            var fMask = floorOverlay.AddComponent<RectMask2D>();
            var fCg = floorOverlay.AddComponent<CanvasGroup>();
            fCg.blocksRaycasts = false;
            fCg.interactable = false;

            // Upper Air Refraction Mirage Glint (subtle translucent shimmer over arena air)
            var airOverlay = new GameObject("SpellMirageAirOverlay");
            airOverlay.transform.SetParent(battleBoardRect, false);
            airOverlay.transform.SetAsLastSibling();
            var aRect = airOverlay.AddComponent<RectTransform>();
            aRect.anchorMin = Vector2.zero;
            aRect.anchorMax = Vector2.one;
            aRect.offsetMin = Vector2.zero;
            aRect.offsetMax = Vector2.zero;
            airOverlay.AddComponent<RectMask2D>();
            var aCg = airOverlay.AddComponent<CanvasGroup>();
            aCg.blocksRaycasts = false;
            aCg.interactable = false;

            Color mirageCyan = new Color(0.42f, 0.88f, 1.0f);
            Color mirageViolet = new Color(0.72f, 0.50f, 1.0f);
            Color primaryMirage = Color.Lerp(mirageCyan, elemColor, 0.44f);
            Color secondaryMirage = Color.Lerp(mirageViolet, elemColor, 0.36f);

            // 1. Ethereal base atmosphere haze tint on floor
            var washObj = new GameObject("MirageBaseWash");
            washObj.transform.SetParent(floorOverlay.transform, false);
            var washRect = washObj.AddComponent<RectTransform>();
            washRect.anchorMin = Vector2.zero;
            washRect.anchorMax = Vector2.one;
            washRect.offsetMin = Vector2.zero;
            washRect.offsetMax = Vector2.zero;
            var washImg = washObj.AddComponent<Image>();
            washImg.color = new Color(primaryMirage.r, primaryMirage.g, primaryMirage.b, 0f);
            washImg.raycastTarget = false;

            Sprite rippleSprite = GameBootstrapper.GetMirageRippleSprite();
            Sprite sweepSprite = GameBootstrapper.GetMirageWaveSweepSprite();
            Sprite flareSprite = GameBootstrapper.GetAbilityFlashCoreSprite();
            Sprite glowSprite = GameBootstrapper.GetCircularBlueFlareSprite();

            // 2. Twin undulating caustic mirage layers gliding in opposing directions
            var veilAObj = new GameObject("MirageRippleVeilA");
            veilAObj.transform.SetParent(floorOverlay.transform, false);
            var veilARect = veilAObj.AddComponent<RectTransform>();
            veilARect.anchorMin = new Vector2(0.5f, 0.5f);
            veilARect.anchorMax = new Vector2(0.5f, 0.5f);
            veilARect.sizeDelta = new Vector2(1680f, 820f);
            var veilAImg = veilAObj.AddComponent<Image>();
            veilAImg.sprite = rippleSprite;
            veilAImg.color = primaryMirage;
            veilAImg.raycastTarget = false;

            var veilBObj = new GameObject("MirageRippleVeilB");
            veilBObj.transform.SetParent(floorOverlay.transform, false);
            var veilBRect = veilBObj.AddComponent<RectTransform>();
            veilBRect.anchorMin = new Vector2(0.5f, 0.5f);
            veilBRect.anchorMax = new Vector2(0.5f, 0.5f);
            veilBRect.sizeDelta = new Vector2(1680f, 820f);
            var veilBImg = veilBObj.AddComponent<Image>();
            veilBImg.sprite = rippleSprite;
            veilBImg.color = secondaryMirage;
            veilBImg.raycastTarget = false;

            // 3. Diagonal Luminous Refractive Mirage Wave sweeping smoothly across air
            var sweepObj = new GameObject("MirageRefractionSweep");
            sweepObj.transform.SetParent(airOverlay.transform, false);
            var sweepRect = sweepObj.AddComponent<RectTransform>();
            sweepRect.anchorMin = new Vector2(0.5f, 0.5f);
            sweepRect.anchorMax = new Vector2(0.5f, 0.5f);
            sweepRect.sizeDelta = new Vector2(460f, 920f);
            sweepRect.localRotation = Quaternion.Euler(0f, 0f, -12f);
            var sweepImg = sweepObj.AddComponent<Image>();
            sweepImg.sprite = sweepSprite;
            sweepImg.color = Color.Lerp(primaryMirage, Color.white, 0.45f);
            sweepImg.raycastTarget = false;

            // 4. 14 Floating Mirage Heat/Mana Wisps ascending over the battlefield
            const int wispCount = 14;
            var wisps = new RectTransform[wispCount];
            var wispImgs = new Image[wispCount];
            var wispBase = new Vector2[wispCount];
            var wispPhase = new float[wispCount];
            for (int i = 0; i < wispCount; i++)
            {
                var wObj = new GameObject($"MirageWisp_{i}");
                wObj.transform.SetParent((i % 2 == 0) ? floorOverlay.transform : airOverlay.transform, false);
                var wRect = wObj.AddComponent<RectTransform>();
                wRect.anchorMin = new Vector2(0.5f, 0.5f);
                wRect.anchorMax = new Vector2(0.5f, 0.5f);
                float size = UnityEngine.Random.Range(32f, 82f);
                wRect.sizeDelta = new Vector2(size, size);
                var wImg = wObj.AddComponent<Image>();
                wImg.sprite = (i % 3 == 0) ? flareSprite : glowSprite;
                wImg.color = (i % 2 == 0) ? primaryMirage : secondaryMirage;
                wImg.raycastTarget = false;

                wispBase[i] = new Vector2(
                    UnityEngine.Random.Range(-540f, 540f),
                    UnityEngine.Random.Range(-290f, 220f));
                wispPhase[i] = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                wisps[i] = wRect;
                wispImgs[i] = wImg;
            }

            float t = 0f;
            while (t < duration && battleBoardRect != null)
            {
                t += Mathf.Min(Time.deltaTime, 0.05f);
                float p = Mathf.Clamp01(t / duration);

                // Smooth envelope: quick shimmering entrance -> dreamy wave sustain -> soft fade out
                float envelope = Mathf.Sin(p * Mathf.PI);
                float sustainEnv = Mathf.Clamp01(p / 0.18f) * Mathf.Clamp01((1f - p) / 0.34f);

                if (washImg != null)
                {
                    Color wc = Color.Lerp(primaryMirage, secondaryMirage, 0.5f + 0.5f * Mathf.Sin(t * 5f));
                    wc.a = sustainEnv * 0.22f;
                    washImg.color = wc;
                }

                // Counter-flowing liquid caustic mirage sheets (vertical shimmer + horizontal drift)
                if (veilARect != null)
                {
                    float driftX = Mathf.Lerp(-165f, 165f, p);
                    float undulateY = Mathf.Sin(t * 7.2f) * 18f;
                    veilARect.anchoredPosition = new Vector2(driftX, undulateY);
                    float sy = 1.0f + Mathf.Sin(t * 9.0f) * 0.045f;
                    veilARect.localScale = new Vector3(1.04f, sy, 1f);
                    Color vcA = primaryMirage;
                    vcA.a = sustainEnv * (0.48f + 0.14f * Mathf.Sin(t * 11f));
                    veilAImg.color = vcA;
                }

                if (veilBRect != null)
                {
                    float driftX = Mathf.Lerp(175f, -175f, p);
                    float undulateY = Mathf.Cos(t * 5.8f) * 16f;
                    veilBRect.anchoredPosition = new Vector2(driftX, undulateY);
                    float sy = 1.0f + Mathf.Cos(t * 8.2f) * 0.05f;
                    veilBRect.localScale = new Vector3(1.06f, sy, 1f);
                    Color vcB = secondaryMirage;
                    vcB.a = sustainEnv * (0.42f + 0.12f * Mathf.Cos(t * 9.5f));
                    veilBImg.color = vcB;
                }

                // Luminous refraction beam sweeping softly across the battlefield air
                if (sweepRect != null)
                {
                    float sweepP = Mathf.Clamp01(p / 0.85f);
                    float sweepX = Mathf.Lerp(-820f, 820f, EaseInOutCubic(sweepP));
                    sweepRect.anchoredPosition = new Vector2(sweepX, Mathf.Sin(p * Mathf.PI * 2f) * 20f);
                    Color sc = Color.Lerp(primaryMirage, Color.white, 0.42f);
                    sc.a = Mathf.Sin(sweepP * Mathf.PI) * 0.34f;
                    sweepImg.color = sc;
                }

                // Undulating Mirage Wisps floating upward across the field
                for (int i = 0; i < wispCount; i++)
                {
                    if (wisps[i] == null) continue;
                    float phase = wispPhase[i];
                    float wx = wispBase[i].x + Mathf.Sin(t * 4.5f + phase) * 24f;
                    float wy = wispBase[i].y + p * 115f + Mathf.Cos(t * 3.8f + phase) * 10f;
                    wisps[i].anchoredPosition = new Vector2(wx, wy);

                    float pulse = 0.82f + 0.26f * Mathf.Sin(t * 6.5f + phase);
                    wisps[i].localScale = new Vector3(pulse, pulse, 1f);

                    Color wc = (i % 2 == 0) ? primaryMirage : secondaryMirage;
                    wc.a = envelope * ((i % 2 == 0) ? 0.38f : 0.24f) * (0.7f + 0.3f * Mathf.Sin(t * 8f + phase));
                    wispImgs[i].color = wc;
                }

                yield return null;
            }

            if (floorOverlay != null) GameObject.Destroy(floorOverlay);
            if (airOverlay != null) GameObject.Destroy(airOverlay);
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

        private static float EaseOutCubic(float x)
        {
            float inv = 1f - x;
            return 1f - inv * inv * inv;
        }

        private static float EaseInOutCubic(float x)
        {
            return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f;
        }
    }
}

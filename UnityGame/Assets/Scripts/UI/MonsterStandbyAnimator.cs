using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MonsterCardGame.Data;
using MonsterCardGame.Utils;

namespace MonsterCardGame.UI
{
    /// <summary>
    /// Drives smooth 60 FPS 2.5D Standby motion (depth parallax, organic breathing,
    /// volumetric rim sheen, and optional multi-frame crossfading) on a monster Image.
    /// </summary>
    public class MonsterStandbyAnimator : MonoBehaviour
    {
        private static Shader _cachedShader;

        private static readonly int CurrTexId = Shader.PropertyToID("_CurrTex");
        private static readonly int NextTexId = Shader.PropertyToID("_NextTex");
        private static readonly int DepthTexId = Shader.PropertyToID("_DepthTex");
        private static readonly int FrameBlendId = Shader.PropertyToID("_FrameBlend");
        private static readonly int UvRectId = Shader.PropertyToID("_UvRect");
        private static readonly int ParallaxOffsetId = Shader.PropertyToID("_ParallaxOffset");
        private static readonly int BreathPhaseId = Shader.PropertyToID("_BreathPhase");
        private static readonly int BreathStrengthId = Shader.PropertyToID("_BreathStrength");
        private static readonly int ParallaxStrengthId = Shader.PropertyToID("_ParallaxStrength");
        private static readonly int PerspectiveSkewId = Shader.PropertyToID("_PerspectiveSkew");
        private static readonly int RimLightDirId = Shader.PropertyToID("_RimLightDir");
        private static readonly int EmissiveBoostId = Shader.PropertyToID("_EmissiveBoost");
        private static readonly int EnableStandbyFxId = Shader.PropertyToID("_EnableStandbyFX");

        private Image targetImage;
        private RectTransform rectTransform;
        private Material runtimeMat;

        private int activeCardId = -1;
        private bool isInspectorMode = false;
        private bool isStandbyActive = false;
        private bool isPaused = false;

        private List<Texture2D> frames = new List<Texture2D>();
        private Texture2D depthTexture;
        private int[] pingPongIndices = { 0 };
        private float phaseOffset = 0f;
        private float animClock = 0f;
        private Vector2 smoothedHoverOffset = Vector2.zero;

        public void Configure(CardData data, bool inspectorMode = false)
        {
            if (targetImage == null) targetImage = GetComponent<Image>();
            if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
            if (targetImage == null) return;

            if (data == null || !MonsterArtLoader.HasStandby25DAssets(data) || targetImage.sprite == null)
            {
                ClearStandby();
                return;
            }

            if (activeCardId == data.id && isInspectorMode == inspectorMode && isStandbyActive)
            {
                UpdateUvRect();
                return;
            }

            activeCardId = data.id;
            isInspectorMode = inspectorMode;

            var loadedFrames = MonsterArtLoader.GetStandbyFrameTextures(data);
            frames.Clear();
            if (loadedFrames != null) frames.AddRange(loadedFrames);

            if (frames.Count == 0 && targetImage.sprite.texture != null)
            {
                frames.Add(targetImage.sprite.texture);
            }

            depthTexture = MonsterArtLoader.GetMonsterDepthTexture(data);
            BuildPingPongSchedule(frames.Count);

            if (!EnsureMaterial())
            {
                ClearStandby();
                return;
            }

            // Use deterministic offset per instance so identical monsters don't breathe in unison
            phaseOffset = ((data.id * 0.37f) + transform.GetSiblingIndex() * 1.13f + (inspectorMode ? 0.5f : 0f)) % 6.283f;
            animClock = 0f;
            isStandbyActive = true;
            isPaused = false;

            targetImage.material = runtimeMat;
            ApplyInitialMaterialState();
        }

        public void ClearStandby()
        {
            isStandbyActive = false;
            activeCardId = -1;
            frames.Clear();
            depthTexture = null;
            if (targetImage != null && targetImage.material == runtimeMat)
            {
                targetImage.material = null;
            }
        }

        public void SetStandbyPaused(bool paused)
        {
            isPaused = paused;
            if (runtimeMat != null && isStandbyActive)
            {
                runtimeMat.SetFloat(EnableStandbyFxId, paused ? 0f : 1f);
            }
        }

        private void BuildPingPongSchedule(int count)
        {
            if (count <= 1)
            {
                pingPongIndices = new[] { 0 };
                return;
            }

            // Full closed breathing loops (>= 6 keyframes: inhale -> peak -> exhale -> rest) advance sequentially
            if (count >= 6)
            {
                pingPongIndices = new int[count];
                for (int i = 0; i < count; i++) pingPongIndices[i] = i;
                return;
            }

            var list = new List<int>(count * 2 - 2);
            for (int i = 0; i < count; i++) list.Add(i);
            for (int i = count - 2; i >= 1; i--) list.Add(i);
            pingPongIndices = list.ToArray();
        }

        private bool EnsureMaterial()
        {
            if (runtimeMat != null) return true;

            if (_cachedShader == null)
            {
                _cachedShader = Resources.Load<Shader>("Shaders/MonsterStandby25D");
                if (_cachedShader == null)
                {
                    _cachedShader = Shader.Find("UI/MonsterStandby25D");
                }
            }

            if (_cachedShader == null || !_cachedShader.isSupported)
            {
                return false;
            }

            runtimeMat = new Material(_cachedShader);
            runtimeMat.hideFlags = HideFlags.DontSave;
            return true;
        }

        private void ApplyInitialMaterialState()
        {
            if (runtimeMat == null || frames.Count == 0) return;

            Texture2D firstTex = frames[0];
            runtimeMat.SetTexture(CurrTexId, firstTex);
            runtimeMat.SetTexture(NextTexId, firstTex);
            runtimeMat.SetTexture(DepthTexId, depthTexture != null ? depthTexture : Texture2D.grayTexture);
            runtimeMat.SetFloat(FrameBlendId, 0f);
            runtimeMat.SetFloat(EnableStandbyFxId, isPaused ? 0f : 1f);

            bool hasDepth = depthTexture != null;
            runtimeMat.SetFloat(BreathStrengthId, isInspectorMode ? 0.025f : 0.022f);
            runtimeMat.SetFloat(ParallaxStrengthId, hasDepth ? (isInspectorMode ? 0.030f : 0.025f) : 0f);
            runtimeMat.SetFloat(PerspectiveSkewId, hasDepth ? (isInspectorMode ? 0.018f : 0.014f) : 0f);
            runtimeMat.SetFloat(EmissiveBoostId, isInspectorMode ? 0.10f : 0.08f);

            UpdateUvRect();
        }

        private void UpdateUvRect()
        {
            if (runtimeMat == null || targetImage == null || targetImage.sprite == null) return;
            Sprite sp = targetImage.sprite;
            Texture tex = sp.texture;
            if (tex == null || tex.width <= 0 || tex.height <= 0) return;

            Rect tr = sp.textureRect;
            runtimeMat.SetVector(UvRectId, new Vector4(
                tr.x / tex.width,
                tr.y / tex.height,
                tr.width / tex.width,
                tr.height / tex.height));
        }

        private void Update()
        {
            if (!isStandbyActive || isPaused || runtimeMat == null || targetImage == null || frames.Count == 0)
                return;

            animClock += Time.unscaledDeltaTime;
            float t = animClock + phaseOffset;

            // 1. Organic deep breathing curve (inhale -> subtle hold -> exhale)
            float breath = 0.5f - 0.5f * Mathf.Cos(t * 1.95f);
            runtimeMat.SetFloat(BreathPhaseId, breath);

            // 2. Smooth 3D figure-8 camera sway + interactive pointer hover parallax
            Vector2 autoSway = new Vector2(
                Mathf.Sin(t * 1.05f) * 0.72f,
                Mathf.Sin(t * 0.72f + 0.55f) * 0.42f);

            Vector2 pointerTarget = Vector2.zero;
            if (rectTransform != null && RectTransformUtility.RectangleContainsScreenPoint(rectTransform, Input.mousePosition, null))
            {
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, Input.mousePosition, null, out Vector2 localPt))
                {
                    Rect r = rectTransform.rect;
                    float nx = r.width > 1f ? Mathf.Clamp(localPt.x / (r.width * 0.5f), -1f, 1f) : 0f;
                    float ny = r.height > 1f ? Mathf.Clamp(localPt.y / (r.height * 0.5f), -1f, 1f) : 0f;
                    float hoverGain = isInspectorMode ? 0.85f : 0.65f;
                    pointerTarget = new Vector2(nx * hoverGain, ny * hoverGain * 0.75f);
                }
            }

            smoothedHoverOffset = Vector2.Lerp(smoothedHoverOffset, pointerTarget, Time.unscaledDeltaTime * 7f);
            Vector2 combinedParallax = autoSway + smoothedHoverOffset;
            runtimeMat.SetVector(ParallaxOffsetId, new Vector4(combinedParallax.x, combinedParallax.y, 0f, 0f));

            // 3. Subtle moving 3D rim light aligned with sway
            runtimeMat.SetVector(RimLightDirId, new Vector4(
                0.38f + combinedParallax.x * 0.45f,
                0.72f + combinedParallax.y * 0.25f,
                0f, 0f));

            // 4. Smooth ping-pong crossfade across standby frames when multiple frames exist
            if (frames.Count > 1 && pingPongIndices.Length > 1)
            {
                // Maintain a steady ~2.8s breathing loop regardless of whether 4, 6, or 8 keyframes are provided
                float totalCycleSeconds = 2.8f;
                float stepDuration = Mathf.Clamp(totalCycleSeconds / pingPongIndices.Length, 0.16f, 0.45f);
                float cycleTime = animClock / stepDuration;
                int seqLen = pingPongIndices.Length;
                int stepIdx = Mathf.FloorToInt(cycleTime) % seqLen;
                int nextStepIdx = (stepIdx + 1) % seqLen;
                float rawFrac = cycleTime - Mathf.Floor(cycleTime);
                float smoothBlend = Mathf.SmoothStep(0f, 1f, rawFrac);

                Texture2D currTex = frames[pingPongIndices[stepIdx]];
                Texture2D nextTex = frames[pingPongIndices[nextStepIdx]];

                runtimeMat.SetTexture(CurrTexId, currTex);
                runtimeMat.SetTexture(NextTexId, nextTex);
                runtimeMat.SetFloat(FrameBlendId, smoothBlend);
            }
        }

        private void OnDestroy()
        {
            if (runtimeMat != null)
            {
                Destroy(runtimeMat);
                runtimeMat = null;
            }
        }
    }
}

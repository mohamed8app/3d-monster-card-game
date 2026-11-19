using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace MonsterCardGame.UI
{
    /// <summary>
    /// Animates multi-frame environment card artwork (Desert 10.gif frames)
    /// smoothly at 10 FPS directly on the side altar UI Image.
    /// Preserves original 16:9 aspect ratio and never impacts the battlefield ground.
    /// </summary>
    public class EnvironmentCardAnimator : MonoBehaviour
    {
        public static EnvironmentCardAnimator Instance { get; private set; }

        private Image targetImage;
        private List<Sprite> desertFrames = new List<Sprite>();
        private float frameDuration = 0.10f; // 10 FPS (100ms per frame from 10.gif)
        private float timer = 0f;
        private int currentFrame = 0;
        private bool isAnimating = false;
        private string activeElement = "";

        private void Awake()
        {
            Instance = this;
            targetImage = GetComponent<Image>();
            if (targetImage != null)
            {
                targetImage.preserveAspect = false;
            }
            LoadDesertFrames();
        }

        private void LoadDesertFrames()
        {
            if (desertFrames.Count > 0) return;

            string[] candidatePaths = {
                Path.Combine(Application.streamingAssetsPath, "Environments", "desert_card_frames"),
                Path.Combine(Application.streamingAssetsPath, "Environments/desert_card_frames"),
                @"D:\deck game\Game_Windows_Exe\MonsterCardGame_Data\StreamingAssets\Environments\desert_card_frames",
                @"D:\deck game\UnityGame\Assets\StreamingAssets\Environments\desert_card_frames"
            };

            string foundDir = null;
            foreach (var p in candidatePaths)
            {
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                {
                    foundDir = p;
                    break;
                }
            }

            if (foundDir != null)
            {
                var files = Directory.GetFiles(foundDir, "frame_*.jpg");
                Array.Sort(files);
                foreach (var file in files)
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(file);
                        Texture2D tex = new Texture2D(640, 360, TextureFormat.RGB24, false);
                        if (tex.LoadImage(bytes))
                        {
                            Sprite sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                            desertFrames.Add(sp);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[EnvironmentCardAnimator] Error loading {file}: {ex.Message}");
                    }
                }
            }

            Debug.Log($"[EnvironmentCardAnimator] Successfully loaded {desertFrames.Count} animated frames from: {foundDir}");
        }

        public void SetActiveEnvironment(string element)
        {
            activeElement = element ?? "";
            bool isDesert = activeElement.Equals("Desert", StringComparison.OrdinalIgnoreCase);

            if (isDesert)
            {
                if (desertFrames.Count == 0) LoadDesertFrames();
                isAnimating = (desertFrames.Count > 0);
                timer = 0f;
                currentFrame = 0;

                if (targetImage == null) targetImage = GetComponent<Image>();
                if (targetImage != null)
                {
                    targetImage.preserveAspect = false;
                    if (isAnimating)
                    {
                        targetImage.sprite = desertFrames[0];
                        targetImage.color = Color.white;
                    }
                }
                Debug.Log($"[EnvironmentCardAnimator] Desert active — animation started ({desertFrames.Count} frames).");
            }
            else
            {
                isAnimating = false;
                if (targetImage != null)
                {
                    targetImage.preserveAspect = false;
                }
            }
        }

        private void Update()
        {
            if (!isAnimating || desertFrames == null || desertFrames.Count == 0 || targetImage == null)
                return;

            timer += Time.unscaledDeltaTime;
            if (timer >= frameDuration)
            {
                timer -= frameDuration;
                currentFrame = (currentFrame + 1) % desertFrames.Count;
                targetImage.sprite = desertFrames[currentFrame];
            }
        }
    }
}

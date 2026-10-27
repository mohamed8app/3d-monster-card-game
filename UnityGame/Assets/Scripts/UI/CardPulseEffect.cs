using System;
using UnityEngine;
using UnityEngine.UI;

namespace MonsterCardGame.UI
{
    /// <summary>
    /// يمنح الوحوش الجاهزة للهجوم تأثيراً نابضاً بالحياة (Breathing & Glow Pulse)
    /// Gives ready-to-attack cards an organic breathing animation and glowing aura.
    /// </summary>
    public class CardPulseEffect : MonoBehaviour
    {
        public bool isPulsing = false;
        private Outline outline;
        private Color baseOutlineColor = Color.white;
        private float timer = 0f;

        private void Awake()
        {
            outline = GetComponent<Outline>();
            if (outline != null)
            {
                baseOutlineColor = outline.effectColor;
            }
        }

        public void SetPulsing(bool pulse, Color glowColor)
        {
            isPulsing = pulse;
            baseOutlineColor = glowColor;
            if (outline != null)
            {
                outline.effectColor = glowColor;
            }
            if (!pulse)
            {
                transform.localScale = Vector3.one;
            }
        }

        private void Update()
        {
            if (!isPulsing) return;

            timer += Time.deltaTime * 3.5f;
            float scaleFactor = 1.0f + Mathf.Sin(timer) * 0.025f;
            transform.localScale = new Vector3(scaleFactor, scaleFactor, 1f);

            if (outline != null)
            {
                float alpha = 0.7f + Mathf.PingPong(timer * 0.5f, 0.3f);
                var c = baseOutlineColor;
                c.a = alpha;
                outline.effectColor = c;
            }
        }

        private void OnDisable()
        {
            transform.localScale = Vector3.one;
        }
    }
}

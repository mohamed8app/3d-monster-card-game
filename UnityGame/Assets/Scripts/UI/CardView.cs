using System;
using UnityEngine;
using UnityEngine.UI;
using MonsterCardGame.Data;
using MonsterCardGame.Core;
using MonsterCardGame.Utils;

namespace MonsterCardGame.UI
{
    public class CardView : MonoBehaviour
    {
        [Header("Data")]
        public MonsterData monsterData;
        public MonsterInstance monsterInstance;

        [Header("UI Elements")]
        public Text nameText;
        public Text costText;
        public Text atkText;
        public Text defText;
        public Text elementText;
        public Text roleText;
        public Image cardFrame;
        public Image elementIcon;

        public bool isDraggable = true;
        private Vector3 originalPosition;
        private Transform originalParent;

        public void Setup(MonsterData data, MonsterInstance instance = null)
        {
            monsterData = data;
            monsterInstance = instance;

            if (nameText != null) nameText.text = ArabicFixer.Fix(data.name);
            if (costText != null) costText.text = $"{data.cost}⚡";
            if (atkText != null) atkText.text = $"{data.atk}⚔️";
            if (defText != null) defText.text = $"{data.def}🛡️";
            if (elementText != null) elementText.text = ArabicFixer.Fix(data.element);
            if (roleText != null) roleText.text = ArabicFixer.Fix(data.role);

            ApplyElementColor(data.element);
        }

        public void UpdateStats()
        {
            if (monsterInstance != null)
            {
                if (defText != null) defText.text = $"{monsterInstance.currentDef}🛡️";
                if (atkText != null) atkText.text = $"{monsterInstance.baseAtk}⚔️";
            }
        }

        private void ApplyElementColor(string element)
        {
            if (cardFrame == null) return;

            Color elemColor = Color.gray;
            switch (element)
            {
                case "نار": elemColor = new Color(0.9f, 0.25f, 0.2f); break;
                case "مياه": elemColor = new Color(0.2f, 0.5f, 0.95f); break;
                case "جليد": elemColor = new Color(0.4f, 0.85f, 0.95f); break;
                case "أرض": elemColor = new Color(0.6f, 0.45f, 0.25f); break;
                case "كهرباء": elemColor = new Color(0.95f, 0.85f, 0.15f); break;
                case "رياح": elemColor = new Color(0.4f, 0.9f, 0.7f); break;
                case "غابة": elemColor = new Color(0.25f, 0.75f, 0.3f); break;
                case "ضياء": elemColor = new Color(1.0f, 0.95f, 0.6f); break;
                case "ظلام": elemColor = new Color(0.35f, 0.15f, 0.5f); break;
                case "ميكا": elemColor = new Color(0.7f, 0.75f, 0.8f); break;
                case "أشباح": elemColor = new Color(0.65f, 0.5f, 0.85f); break;
                case "سحر": elemColor = new Color(0.85f, 0.35f, 0.85f); break;
            }
            cardFrame.color = elemColor;
        }
    }
}

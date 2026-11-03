using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Core
{
    public class AIOpponent : MonoBehaviour
    {
        public PlayerState aiPlayer;
        public PlayerState humanPlayer;

        public void PlayTurn(int currentTurn, EnvironmentCardData activeEnv, Action onTurnFinished)
        {
            if (!aiPlayer.IsAlive) return;
            StartCoroutine(PlayTurnCoroutine(currentTurn, activeEnv, onTurnFinished));
        }

        private IEnumerator PlayTurnCoroutine(int currentTurn, EnvironmentCardData activeEnv, Action onTurnFinished)
        {
            Debug.Log("🤖 يبدأ ذكاء البوت الاصطناعي بالتفكير التكتيكي...");
            yield return new WaitForSeconds(0.6f);

            // 1. التضحية التكتيكية
            DecideSacrifice();
            if (MonsterCardGame.UI.BattleUIManager.Instance != null) MonsterCardGame.UI.BattleUIManager.Instance.UpdateUI();
            yield return new WaitForSeconds(0.4f);

            // 2. استخدام بطاقات البيئة إن وجدت وكانت مفيدة
            DecideEnvironment();
            if (MonsterCardGame.UI.BattleUIManager.Instance != null) MonsterCardGame.UI.BattleUIManager.Instance.UpdateUI();
            yield return new WaitForSeconds(0.4f);

            // 3. إلقاء التعاويذ السحرية التكتيكية (شفاء / ضرر / صعق)
            DecideSpells();
            if (MonsterCardGame.UI.BattleUIManager.Instance != null) MonsterCardGame.UI.BattleUIManager.Instance.UpdateUI();
            yield return new WaitForSeconds(0.5f);

            // 4. الاستدعاء الميداني في أفضل الخانات
            DecideSummons(currentTurn);
            if (MonsterCardGame.UI.BattleUIManager.Instance != null) MonsterCardGame.UI.BattleUIManager.Instance.UpdateUI();
            yield return new WaitForSeconds(0.5f);

            // 5. تنفيذ مرحلة الهجوم بالرسوم المتحركة
            yield return CombatSystem.Instance.ExecuteCombatPhaseAnimated(aiPlayer, humanPlayer, GameManager.Instance.activeEnvironment, onTurnFinished);
        }

        private void DecideSacrifice()
        {
            if (aiPlayer.manaCapacity >= PlayerState.MaxManaCap || aiPlayer.hand.Count < 2)
                return;

            // التضحية بأعلى كارت تكلفة لا يمكن لعبه حالياً
            CardData bestSacrifice = null;
            int highestCost = -1;

            foreach (var card in aiPlayer.hand)
            {
                if (card.cost > aiPlayer.currentPower + 1 && card.cost > highestCost)
                {
                    highestCost = card.cost;
                    bestSacrifice = card;
                }
            }

            if (bestSacrifice == null)
            {
                bestSacrifice = aiPlayer.hand[aiPlayer.hand.Count - 1];
            }

            aiPlayer.SacrificeCard(bestSacrifice);
        }

        private void DecideEnvironment()
        {
            if (GameManager.Instance == null) return;

            foreach (var card in new List<CardData>(aiPlayer.hand))
            {
                if (card.cardType == CardType.Environment && card.cost <= aiPlayer.currentPower)
                {
                    // يفعل البيئة إذا كانت تخدم وحوشه أو تعطل وحوش اللاعب
                    GameManager.Instance.PlayEnvironmentCard(aiPlayer, card);
                    break;
                }
            }
        }

        private void DecideSpells()
        {
            if (GameManager.Instance == null) return;

            var spellCards = new List<CardData>();
            foreach (var c in aiPlayer.hand)
            {
                if (c.cardType == CardType.Spell && c.cost <= aiPlayer.currentPower)
                {
                    spellCards.Add(c);
                }
            }

            foreach (var spell in spellCards)
            {
                if (spell.cost <= aiPlayer.currentPower)
                {
                    // فحص ذكي: لو تعويذة شفاء ونقاط حياة الخصم شبه كاملة، يؤجلها
                    if (spell.spellType == SpellType.LeaderHeal && aiPlayer.currentHp > (aiPlayer.maxHp * 0.8f))
                    {
                        continue;
                    }

                    GameManager.Instance.CastSpell(aiPlayer, humanPlayer, spell);
                }
            }
        }

        private void DecideSummons(int currentTurn)
        {
            var monsterCards = new List<CardData>();
            foreach (var c in aiPlayer.hand)
            {
                if (c.cardType == CardType.Monster) monsterCards.Add(c);
            }

            monsterCards.Sort((a, b) => (b.cost + b.atk + b.def).CompareTo(a.cost + a.atk + a.def));

            foreach (var card in monsterCards)
            {
                if (card.cost <= aiPlayer.currentPower)
                {
                    int bestSlot = FindBestSlotForCard(card);
                    if (bestSlot != -1)
                    {
                        aiPlayer.SummonMonster(card, bestSlot, currentTurn);
                    }
                }
            }
        }

        private int FindBestSlotForCard(CardData card)
        {
            if (card.GetRoleEnum() == TacticalRole.PureShield)
            {
                // الدرع الصرف: يفضل ملء الوسط ثم الأجنحة لصد الضربات
                int[] shieldSlots = { 2, 3, 0, 5, 1, 4 };
                foreach (int s in shieldSlots)
                {
                    if (!aiPlayer.slots[s].IsOccupied) return s;
                }
            }
            else if (card.GetRoleEnum() == TacticalRole.Attacker)
            {
                // المهاجم: يفضل الأجنحة المفتوحة لضرب قائد الخصم مباشرة
                int[] attackSlots = { 0, 1, 4, 5, 2, 3 };
                foreach (int s in attackSlots)
                {
                    if (!aiPlayer.slots[s].IsOccupied) return s;
                }
            }
            else
            {
                // مدافع أو داعم: أي خانة شاغرة بتوازن
                int[] balancedSlots = { 2, 3, 1, 4, 0, 5 };
                foreach (int s in balancedSlots)
                {
                    if (!aiPlayer.slots[s].IsOccupied) return s;
                }
            }
            return -1;
        }
    }
}

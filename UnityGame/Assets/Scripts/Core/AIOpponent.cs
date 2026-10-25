using System;
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

            Debug.Log("🤖 يبدأ ذكاء البوت الاصطناعي بالتفكير التكتيكي...");

            // 1. التضحية
            DecideSacrifice();

            // 2. الاستدعاء في أفضل الخانات
            DecideSummons(currentTurn);

            // 3. تنفيذ مرحلة الهجوم
            CombatSystem.Instance.ExecuteCombatPhase(aiPlayer, humanPlayer, activeEnv);

            // 4. إنهاء الدور
            onTurnFinished?.Invoke();
        }

        private void DecideSacrifice()
        {
            if (aiPlayer.manaCapacity >= PlayerState.MaxManaCap || aiPlayer.hand.Count < 2)
                return;

            // التضحية بأعلى كارت تكلفة لا يمكن لعبه حالياً
            MonsterData bestSacrifice = null;
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

        private void DecideSummons(int currentTurn)
        {
            var playableCards = new List<MonsterData>(aiPlayer.hand);
            playableCards.Sort((a, b) => (b.cost + b.atk + b.def).CompareTo(a.cost + a.atk + a.def));

            foreach (var card in playableCards)
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

        private int FindBestSlotForCard(MonsterData card)
        {
            if (card.GetRoleEnum() == TacticalRole.PureShield)
            {
                // الدرع الصرف: يفضل خانة المدافع الخلفي (6) أولاً
                if (!aiPlayer.slots[6].IsOccupied) return 6;
                // أو الأجنحة لصد الضربات
                int[] wings = { 0, 5, 1, 4 };
                foreach (int s in wings)
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
                // المدافع أو الداعم: ملء الوسط أو الشواغر
                int[] balancedSlots = { 2, 3, 6, 0, 1, 4, 5 };
                foreach (int s in balancedSlots)
                {
                    if (!aiPlayer.slots[s].IsOccupied) return s;
                }
            }

            return -1;
        }
    }
}

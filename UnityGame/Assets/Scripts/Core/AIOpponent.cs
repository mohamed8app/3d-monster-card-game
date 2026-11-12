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
            Debug.Log("🤖 AI Warlord is calculating tactical moves...");
            yield return new WaitForSeconds(0.6f);

            // 1. Tactical Sacrifice
            DecideSacrifice();
            if (MonsterCardGame.UI.BattleUIManager.Instance != null) MonsterCardGame.UI.BattleUIManager.Instance.UpdateUI();
            yield return new WaitForSeconds(0.4f);

            // 2. Activate Environment Weather if available
            DecideEnvironment();
            if (MonsterCardGame.UI.BattleUIManager.Instance != null) MonsterCardGame.UI.BattleUIManager.Instance.UpdateUI();
            yield return new WaitForSeconds(0.4f);

            // 3. Cast Tactical Spells
            DecideSpells();
            if (MonsterCardGame.UI.BattleUIManager.Instance != null) MonsterCardGame.UI.BattleUIManager.Instance.UpdateUI();
            yield return new WaitForSeconds(0.5f);

            // 4. Field Summons in optimal slots
            DecideSummons(currentTurn);
            if (MonsterCardGame.UI.BattleUIManager.Instance != null) MonsterCardGame.UI.BattleUIManager.Instance.UpdateUI();
            yield return new WaitForSeconds(0.4f);

            // 5. Execute Active Skills
            if (CombatSystem.Instance != null)
            {
                CombatSystem.Instance.ExecuteActiveSkills(aiPlayer, humanPlayer);
            }
            if (MonsterCardGame.UI.BattleUIManager.Instance != null) MonsterCardGame.UI.BattleUIManager.Instance.UpdateUI();
            yield return new WaitForSeconds(0.4f);

            // 6. Execute Animated Combat Phase
            yield return CombatSystem.Instance.ExecuteCombatPhaseAnimated(aiPlayer, humanPlayer, GameManager.Instance.activeEnvironment, onTurnFinished);
        }

        private void DecideSacrifice()
        {
            if (aiPlayer.hand.Count < 2 || aiPlayer.denyDrawSacTurns > 0)
                return;

            // Count affordable monsters & check whether AI actually needs mana or is overflowing hand
            int affordableMonsters = 0;
            bool hasHighCostMonster = false;
            int emptySlots = 0;
            foreach (var s in aiPlayer.slots)
            {
                if (s != null && !s.IsOccupied) emptySlots++;
            }

            CardData unplayableHighCost = null;
            CardData extraNonMonster = null;
            int highestUnplayableCost = -1;

            foreach (var card in aiPlayer.hand)
            {
                if (card == null) continue;
                if (card.cardType == CardType.Monster)
                {
                    if (card.cost <= aiPlayer.currentPower) affordableMonsters++;
                    else hasHighCostMonster = true;
                }
                else if (card.cost > aiPlayer.currentPower + 1)
                {
                    extraNonMonster = card;
                }

                if (card.cost > aiPlayer.currentPower + 2 && card.cost > highestUnplayableCost)
                {
                    highestUnplayableCost = card.cost;
                    unplayableHighCost = card;
                }
            }

            // Only sacrifice when AI needs mana to play its hand or has plenty of cards (>= 6), instead of throwing away monsters every turn
            bool needsMana = (hasHighCostMonster && affordableMonsters == 0) || (aiPlayer.manaCapacity < 8 && aiPlayer.hand.Count >= 5);
            if (!needsMana && aiPlayer.hand.Count < 7) return;

            CardData bestSacrifice = unplayableHighCost ?? extraNonMonster;
            if (bestSacrifice == null && aiPlayer.hand.Count >= 5)
            {
                bestSacrifice = aiPlayer.hand[aiPlayer.hand.Count - 1];
            }

            if (bestSacrifice != null)
            {
                aiPlayer.SacrificeCard(bestSacrifice);
            }
        }

        private void DecideEnvironment()
        {
            if (GameManager.Instance == null) return;

            foreach (var card in new List<CardData>(aiPlayer.hand))
            {
                if (card.cardType == CardType.Environment && card.cost <= aiPlayer.currentPower)
                {
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
                int[] shieldSlots = { 2, 3, 0, 5, 1, 4 };
                foreach (int s in shieldSlots)
                {
                    if (!aiPlayer.slots[s].IsOccupied) return s;
                }
            }
            else if (card.GetRoleEnum() == TacticalRole.Attacker)
            {
                int[] attackSlots = { 0, 1, 4, 5, 2, 3 };
                foreach (int s in attackSlots)
                {
                    if (!aiPlayer.slots[s].IsOccupied) return s;
                }
            }
            else
            {
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

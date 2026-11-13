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
            yield return new WaitForSeconds(0.55f);

            // 1. Tactical Sacrifice (+1 Power & +1 Mana) with smooth card flight from hand to Enemy Power bar
            yield return DecideSacrificeCoroutine();

            // 2. Activate Environment Weather with visible flip & flight to Shared Environment Slot
            yield return DecideEnvironmentCoroutine();

            // 3. Cast Tactical Spells step-by-step with flip & flight into the arena
            yield return DecideSpellsCoroutine();
            if (!aiPlayer.IsAlive || !humanPlayer.IsAlive) yield break;

            // 4. Field Summons step-by-step: each card flies visibly from Enemy Hand to its Battle Slot
            yield return DecideSummonsCoroutine(currentTurn);
            if (!aiPlayer.IsAlive || !humanPlayer.IsAlive) yield break;

            // 5. Execute Active Skills step-by-step with live Mana/Power UI refresh
            if (CombatSystem.Instance != null)
            {
                yield return CombatSystem.Instance.ExecuteActiveSkillsAnimated(aiPlayer, humanPlayer);
            }
            if (!aiPlayer.IsAlive || !humanPlayer.IsAlive) yield break;
            if (MonsterCardGame.UI.BattleUIManager.Instance != null) MonsterCardGame.UI.BattleUIManager.Instance.UpdateUI();
            yield return new WaitForSeconds(0.35f);

            // 6. Execute Animated Combat Phase
            yield return CombatSystem.Instance.ExecuteCombatPhaseAnimated(aiPlayer, humanPlayer, GameManager.Instance.activeEnvironment, onTurnFinished);
        }

        private IEnumerator DecideSacrificeCoroutine()
        {
            if (aiPlayer.hand.Count < 2 || aiPlayer.denyDrawSacTurns > 0)
                yield break;

            // Count affordable monsters & check whether AI actually needs mana or is overflowing hand
            int affordableMonsters = 0;
            bool hasHighCostMonster = false;

            CardData unplayableHighCost = null;
            CardData extraNonMonster = null;
            int highestUnplayableCost = -1;

            foreach (var card in aiPlayer.hand)
            {
                if (card == null) continue;
                if (card.cardType == CardType.Monster)
                {
                    if (card.cost <= aiPlayer.currentMana) affordableMonsters++;
                    else hasHighCostMonster = true;
                }
                else if (card.cost > aiPlayer.currentMana + 1)
                {
                    extraNonMonster = card;
                }

                if (card.cost > aiPlayer.currentMana + 2 && card.cost > highestUnplayableCost)
                {
                    highestUnplayableCost = card.cost;
                    unplayableHighCost = card;
                }
            }

            // Always sacrifice at 0 Power; otherwise sacrifice when AI needs Mana/Power to play its hand or has plenty of cards
            bool needsMana = (aiPlayer.power == 0)
                || (hasHighCostMonster && affordableMonsters == 0)
                || (aiPlayer.power < 8 && aiPlayer.hand.Count >= 5);
            if (!needsMana && aiPlayer.hand.Count < 7) yield break;

            CardData bestSacrifice = unplayableHighCost ?? extraNonMonster;
            if (bestSacrifice == null && (needsMana || aiPlayer.hand.Count >= 5) && aiPlayer.hand.Count > 0)
            {
                bestSacrifice = aiPlayer.hand[aiPlayer.hand.Count - 1];
            }

            if (bestSacrifice != null && aiPlayer.hand.Contains(bestSacrifice) && !aiPlayer.hasSacrificedThisTurn)
            {
                var ui = MonsterCardGame.UI.BattleUIManager.Instance;
                if (ui != null)
                {
                    yield return ui.AnimateEnemyCardSacrificeRoutine();
                }

                if (aiPlayer.SacrificeCard(bestSacrifice))
                {
                    MonsterCardGame.Audio.SoundManager.Instance?.PlayCardSacrifice();
                    if (ui != null)
                    {
                        ui.LogCombatMessage(
                            $"🔥 Enemy sacrificed a card (+1 Power / +1 Mana -> Mana: {aiPlayer.currentMana}, Power: {aiPlayer.power}).");
                        ui.UpdateUI();
                    }
                }
                yield return new WaitForSeconds(0.35f);
            }
        }

        private IEnumerator DecideEnvironmentCoroutine()
        {
            if (GameManager.Instance == null) yield break;

            CardData chosenEnv = null;
            foreach (var card in new List<CardData>(aiPlayer.hand))
            {
                if (card.cardType == CardType.Environment && card.cost <= aiPlayer.currentMana)
                {
                    chosenEnv = card;
                    break;
                }
            }

            if (chosenEnv != null)
            {
                var ui = MonsterCardGame.UI.BattleUIManager.Instance;
                if (ui != null)
                {
                    yield return ui.AnimateEnemyCardPlayRoutine(chosenEnv, ui.GetEnvironmentTargetWorldPos(), 0.50f, 0.08f, 0.85f);
                }

                GameManager.Instance.PlayEnvironmentCard(aiPlayer, chosenEnv);
                if (ui != null) ui.UpdateUI();
                yield return new WaitForSeconds(0.35f);
            }
        }

        private IEnumerator DecideSpellsCoroutine()
        {
            if (GameManager.Instance == null) yield break;

            var spellCards = new List<CardData>();
            foreach (var c in aiPlayer.hand)
            {
                if (c.cardType == CardType.Spell && c.cost <= aiPlayer.currentMana)
                {
                    spellCards.Add(c);
                }
            }

            foreach (var spell in spellCards)
            {
                if (!aiPlayer.IsAlive || !humanPlayer.IsAlive) yield break;
                if (spell.cost <= aiPlayer.currentMana)
                {
                    if (spell.spellType == SpellType.LeaderHeal && aiPlayer.currentHp > (aiPlayer.maxHp * 0.8f))
                    {
                        continue;
                    }

                    var ui = MonsterCardGame.UI.BattleUIManager.Instance;
                    if (ui != null)
                    {
                        yield return ui.AnimateEnemyCardPlayRoutine(spell, ui.GetCenterArenaWorldPos(), 0.48f, 0.18f, 1.12f);
                    }

                    if (GameManager.Instance.CastSpell(aiPlayer, humanPlayer, spell))
                    {
                        if (ui != null) ui.UpdateUI();
                        yield return new WaitForSeconds(0.35f);
                    }
                }
            }
        }

        private IEnumerator DecideSummonsCoroutine(int currentTurn)
        {
            var monsterCards = new List<CardData>();
            foreach (var c in aiPlayer.hand)
            {
                if (c.cardType == CardType.Monster) monsterCards.Add(c);
            }

            monsterCards.Sort((a, b) => (b.cost + b.atk + b.def).CompareTo(a.cost + a.atk + a.def));

            foreach (var card in monsterCards)
            {
                if (!aiPlayer.IsAlive || !humanPlayer.IsAlive) yield break;
                if (card.cost <= aiPlayer.currentMana)
                {
                    int bestSlot = FindBestSlotForCard(card);
                    if (bestSlot == -1) continue;

                    var ui = MonsterCardGame.UI.BattleUIManager.Instance;
                    if (ui != null && ui.enemySlotViews != null && bestSlot < ui.enemySlotViews.Length && ui.enemySlotViews[bestSlot] != null)
                    {
                        yield return ui.AnimateEnemyCardPlayRoutine(card, ui.enemySlotViews[bestSlot].transform.position, 0.52f, 0f, 1.02f);
                    }

                    if (aiPlayer.SummonMonster(card, bestSlot, currentTurn))
                    {
                        if (MonsterCardGame.Audio.SoundManager.Instance != null)
                        {
                            MonsterCardGame.Audio.SoundManager.Instance.PlayMonsterSummon(card);
                        }

                        if (ui != null)
                        {
                            ui.LogCombatMessage($"👹 Enemy summoned [{card.name}] (-{card.cost} Mana | Left: {aiPlayer.currentMana}).", card);
                            ui.UpdateUI();
                            if (ui.enemySlotViews != null && bestSlot < ui.enemySlotViews.Length && ui.enemySlotViews[bestSlot] != null)
                            {
                                ui.StartCoroutine(MonsterCardGame.UI.BattleAnimations.AnimateCardSettle(ui.enemySlotViews[bestSlot].transform, 0.16f));
                            }
                        }

                        yield return new WaitForSeconds(0.30f);
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

using System;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Core
{
    public class PlayerState : MonoBehaviour
    {
        [Header("Leader Stats")]
        public string playerName = "Commander";
        public int currentHp = 500;
        public int maxHp = 500;
        public int shield = 0;

        [Header("Resources (Unlimited Mana & Power ∞)")]
        public int manaCapacity = 1;
        public int currentPower = 1;
        public bool hasSacrificedThisTurn = false;
        public int fatigueCount = 0;
        public int monstersSummonedTotal = 0;

        [Header("Tactical Status Effects")]
        public int denyDrawSacTurns = 0;
        public int teamImmuneTurns = 0;
        public int freeSkillBonus = 0;

        [Header("Card Collections")]
        public List<CardData> deck = new List<CardData>();
        public List<CardData> hand = new List<CardData>();
        public List<CardData> graveyard = new List<CardData>();

        [Header("6 Board Slots")]
        public BoardSlot[] slots = new BoardSlot[6];

        public bool IsAlive => currentHp > 0;

        public void InitializeDeck(List<CardData> initialDeck)
        {
            deck = new List<CardData>(initialDeck);
            ShuffleDeck();
            hand.Clear();
            graveyard.Clear();
            currentHp = maxHp;
            shield = 0;
            manaCapacity = 1;
            currentPower = 1;
            fatigueCount = 0;
            monstersSummonedTotal = 0;
            hasSacrificedThisTurn = false;
            denyDrawSacTurns = 0;
            teamImmuneTurns = 0;
            freeSkillBonus = 0;
        }

        public void ShuffleDeck()
        {
            for (int i = 0; i < deck.Count; i++)
            {
                int rnd = UnityEngine.Random.Range(i, deck.Count);
                var temp = deck[i];
                deck[i] = deck[rnd];
                deck[rnd] = temp;
            }
        }

        public void StartTurn(int turnNumber)
        {
            // 1. Recharge Power equal to current Mana Capacity (Unlimited ∞)
            currentPower = manaCapacity;
            hasSacrificedThisTurn = false;

            // 2. Check Total Eclipse effect (denies draw & sacrifice for 1 turn)
            if (denyDrawSacTurns > 0)
            {
                denyDrawSacTurns--;
                hasSacrificedThisTurn = true;
                Debug.Log($"🌑 [{playerName}] is under Total Eclipse—cannot draw or sacrifice this turn!");
            }
            else
            {
                DrawCard();
            }

            // 3. Refresh monster attack readiness
            foreach (var slot in slots)
            {
                if (slot != null && slot.IsOccupied)
                {
                    slot.currentMonster.ResetTurnReadiness(turnNumber);
                }
            }
        }

        public bool DrawCard()
        {
            if (deck.Count > 0)
            {
                var card = deck[0];
                deck.RemoveAt(0);

                if (hand.Count < 10)
                {
                    hand.Add(card);
                    if (MonsterCardGame.Audio.SoundManager.Instance != null)
                    {
                        MonsterCardGame.Audio.SoundManager.Instance.PlayCardDraw();
                    }
                    Debug.Log($"[{playerName}] drew card #{card.name} ({card.cardType})");
                    return true;
                }
                else
                {
                    graveyard.Add(card);
                    Debug.Log($"[{playerName}] Hand is full! Burned card: {card.name}");
                    return false;
                }
            }
            else
            {
                fatigueCount++;
                int fatigueDmg = fatigueCount * 30;
                TakeLeaderDamage(fatigueDmg);
                Debug.LogWarning($"[{playerName}] Out of cards! Took {fatigueDmg} HP fatigue damage.");
                return false;
            }
        }

        public bool SacrificeCard(CardData cardToSacrifice)
        {
            if (hasSacrificedThisTurn)
            {
                Debug.LogWarning("You can only sacrifice one card per turn!");
                return false;
            }

            if (!hand.Contains(cardToSacrifice))
                return false;

            hand.Remove(cardToSacrifice);
            graveyard.Add(cardToSacrifice);

            manaCapacity++;
            currentPower++;
            hasSacrificedThisTurn = true;

            Debug.Log($"[{playerName}] sacrificed [{cardToSacrifice.name}]. Mana Cap: {manaCapacity}, Current Power: {currentPower}");
            return true;
        }

        public bool SummonMonster(CardData card, int targetSlotIndex, int currentTurn)
        {
            if (targetSlotIndex < 0 || targetSlotIndex >= slots.Length)
                return false;

            if (slots[targetSlotIndex].IsOccupied)
            {
                Debug.LogWarning($"Slot {targetSlotIndex} is already occupied!");
                return false;
            }

            if (card.cost > currentPower)
            {
                Debug.LogWarning($"Not enough Power! Required: {card.cost}, Available: {currentPower}");
                return false;
            }

            currentPower -= card.cost;
            hand.Remove(card);
            monstersSummonedTotal++;

            var instance = new MonsterInstance(card, currentTurn);
            slots[targetSlotIndex].PlaceMonster(instance);

            if (CombatSystem.Instance != null && GameManager.Instance != null)
            {
                var opponent = (this == GameManager.Instance.humanPlayer) ? GameManager.Instance.aiPlayer : GameManager.Instance.humanPlayer;
                CombatSystem.Instance.ApplyOnSummonPassives(this, opponent, targetSlotIndex, instance);
            }

            Debug.Log($"[{playerName}] summoned Monster [{card.name}] in Slot {targetSlotIndex}");
            return true;
        }

        public void TakeLeaderDamage(int damage)
        {
            if (damage <= 0 || teamImmuneTurns > 0) return;

            if (shield > 0)
            {
                int absorbed = Mathf.Min(shield, damage);
                shield -= absorbed;
                damage -= absorbed;
            }

            if (damage > 0)
            {
                currentHp = Mathf.Max(0, currentHp - damage);
                Debug.Log($"🚨 [{playerName}] took {damage} direct Commander damage. Remaining HP: {currentHp}/{maxHp}");
            }
        }

        public void HealLeader(int healAmount, PlayerState opponent = null)
        {
            if (healAmount <= 0) return;

            if (opponent != null)
            {
                foreach (var s in opponent.slots)
                {
                    if (s != null && s.IsOccupied && s.currentMonster.HasPassiveSubstr("Spectral Presence") != null)
                    {
                        Debug.Log($"👻 [Spectral Presence] prevented healing for [{playerName}]!");
                        return;
                    }
                }
            }

            currentHp = Mathf.Min(maxHp, currentHp + healAmount);
            Debug.Log($"💚 [{playerName}] healed Commander for {healAmount} HP. Current HP: {currentHp}/{maxHp}");
        }
    }
}

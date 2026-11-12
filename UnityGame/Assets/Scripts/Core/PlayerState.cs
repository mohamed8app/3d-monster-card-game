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
        public int maxHandSize = 10;
        public List<CardData> deck = new List<CardData>();
        public List<CardData> hand = new List<CardData>();
        public List<CardData> graveyard = new List<CardData>();

        [Header("6 Board Slots")]
        public BoardSlot[] slots = new BoardSlot[6];

        // Non-repeating, escalating pity & fallen-comeback endless draw state
        public readonly HashSet<int> drawnCardIdsThisMatch = new HashSet<int>();
        public readonly Dictionary<int, int> fallenMonsterDeathCount = new Dictionary<int, int>();
        public readonly Dictionary<int, int> fallenMonsterDeathTurn = new Dictionary<int, int>();
        public readonly Dictionary<int, int> revivedFromDeathCount = new Dictionary<int, int>();
        private readonly Dictionary<int, int> cardTurnsWaiting = new Dictionary<int, int>();
        private int consecutiveNonMonsterDraws = 0;
        private int consecutiveMonsterDraws = 0;
        private string lastDrawnElement = "";
        public int waveNumber = 1;

        public Func<PlayerState, List<CardData>> onDeckExhaustedRefill;
        public Func<int, float> getCardDrawWeightMultiplier;
        public Action<int> onCardDrawnCallback;

        public bool IsAlive => currentHp > 0;

        public void RecordMonsterDeath(int monsterId, int turnNumber)
        {
            fallenMonsterDeathCount[monsterId] = (fallenMonsterDeathCount.TryGetValue(monsterId, out int c) ? c : 0) + 1;
            fallenMonsterDeathTurn[monsterId] = turnNumber;
        }

        public float GetFallenComebackWeight(int monsterId, int turnNumber)
        {
            if (!fallenMonsterDeathCount.TryGetValue(monsterId, out int deaths) || deaths <= 0)
                return 0f;

            int deathTurn = fallenMonsterDeathTurn.TryGetValue(monsterId, out int dt) ? dt : 0;
            if (turnNumber - deathTurn < 2)
                return 0.03f; // Brief cooldown right after dying

            int revives = revivedFromDeathCount.TryGetValue(monsterId, out int rc) ? rc : 0;
            return Mathf.Max(0.25f, (0.95f + Mathf.Min(deaths, 3) * 0.20f) / (1.0f + revives * 0.85f));
        }

        public void InitializeDeck(List<CardData> initialDeck)
        {
            deck = new List<CardData>();
            cardTurnsWaiting.Clear();
            drawnCardIdsThisMatch.Clear();
            fallenMonsterDeathCount.Clear();
            fallenMonsterDeathTurn.Clear();
            revivedFromDeathCount.Clear();
            consecutiveNonMonsterDraws = 0;
            consecutiveMonsterDraws = 0;
            lastDrawnElement = "";
            waveNumber = 1;

            var uniqueIds = new HashSet<int>();
            if (initialDeck != null)
            {
                foreach (var c in initialDeck)
                {
                    if (c != null && uniqueIds.Add(c.id))
                    {
                        deck.Add(c);
                        cardTurnsWaiting[c.id] = 0;
                    }
                }
            }

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

        public void AddRefillCardsToDeck(List<CardData> refillCards)
        {
            if (refillCards == null || refillCards.Count == 0) return;

            var existingInDeck = new HashSet<int>();
            foreach (var c in deck)
            {
                if (c != null) existingInDeck.Add(c.id);
            }

            foreach (var c in refillCards)
            {
                if (c != null && existingInDeck.Add(c.id))
                {
                    deck.Add(c);
                    if (!cardTurnsWaiting.ContainsKey(c.id))
                    {
                        cardTurnsWaiting[c.id] = 0;
                    }
                }
            }
            ShuffleDeck();
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
            if (deck.Count == 0 && onDeckExhaustedRefill != null)
            {
                var refill = onDeckExhaustedRefill.Invoke(this);
                AddRefillCardsToDeck(refill);
            }

            if (deck.Count > 0)
            {
                int chosenIndex = SelectWeightedNextCardIndex();
                var card = deck[chosenIndex];
                deck.RemoveAt(chosenIndex);
                cardTurnsWaiting.Remove(card.id);

                // Escalate wait counters for all remaining cards in deck so unseen cards keep rising in chance
                for (int i = 0; i < deck.Count; i++)
                {
                    var rem = deck[i];
                    if (rem != null)
                    {
                        cardTurnsWaiting[rem.id] = cardTurnsWaiting.TryGetValue(rem.id, out int cur) ? cur + 1 : 1;
                    }
                }

                bool wasFallenVeteran = card.cardType == CardType.Monster
                    && drawnCardIdsThisMatch.Contains(card.id)
                    && fallenMonsterDeathCount.TryGetValue(card.id, out int dc) && dc > 0;

                if (wasFallenVeteran)
                {
                    revivedFromDeathCount[card.id] = (revivedFromDeathCount.TryGetValue(card.id, out int rc) ? rc : 0) + 1;
                }

                drawnCardIdsThisMatch.Add(card.id);
                lastDrawnElement = card.element ?? "";
                if (card.cardType == CardType.Monster)
                {
                    consecutiveNonMonsterDraws = 0;
                    consecutiveMonsterDraws++;
                }
                else
                {
                    consecutiveNonMonsterDraws++;
                    consecutiveMonsterDraws = 0;
                }

                onCardDrawnCallback?.Invoke(card.id);

                if (hand.Count < maxHandSize)
                {
                    hand.Add(card);
                    if (MonsterCardGame.Audio.SoundManager.Instance != null)
                    {
                        MonsterCardGame.Audio.SoundManager.Instance.PlayCardDraw();
                    }
                    if (wasFallenVeteran && MonsterCardGame.UI.BattleUIManager.Instance != null)
                    {
                        MonsterCardGame.UI.BattleUIManager.Instance.LogCombatMessage($"🔥💀 [{playerName}] Comeback Draw! Fallen Monster #{card.name} returned for a rematch!");
                    }
                    Debug.Log($"[{playerName}] drew card #{card.name} ({card.cardType}) | Deck left: {deck.Count}");
                    return true;
                }
                else
                {
                    graveyard.Add(card);
                    Debug.Log($"[{playerName}] Hand is full ({maxHandSize})! Moved card {card.name} to graveyard.");
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

        private int SelectWeightedNextCardIndex()
        {
            if (deck.Count <= 1) return 0;

            bool isOpeningDraw = hand.Count < 6;
            int openingMonsters = 0;
            int openingLowCost = 0;
            if (isOpeningDraw)
            {
                foreach (var hc in hand)
                {
                    if (hc == null) continue;
                    if (hc.cardType == CardType.Monster) openingMonsters++;
                    if (hc.cost <= 3) openingLowCost++;
                }
            }

            int turnNow = GameManager.Instance != null ? GameManager.Instance.currentTurn : 1;
            float[] weights = new float[deck.Count];
            float totalWeight = 0f;

            for (int i = 0; i < deck.Count; i++)
            {
                var c = deck[i];
                if (c == null)
                {
                    weights[i] = 0f;
                    continue;
                }

                int waited = cardTurnsWaiting.TryGetValue(c.id, out int w) ? w : 0;
                float waitBoost = 1.0f + waited * 0.45f;
                float pityBoost = getCardDrawWeightMultiplier != null ? Mathf.Max(0.1f, getCardDrawWeightMultiplier(c.id)) : 1.0f;
                float patternBoost = 1.0f;

                if (isOpeningDraw)
                {
                    if (openingMonsters < 3 && c.cardType == CardType.Monster)
                        patternBoost *= 3.2f;
                    if (openingLowCost < 2 && c.cost <= 3)
                        patternBoost *= 3.0f;
                }
                else
                {
                    if (c.cardType == CardType.Monster && consecutiveNonMonsterDraws >= 2)
                        patternBoost *= 3.5f + (consecutiveNonMonsterDraws - 2) * 2.5f;
                    else if (c.cardType != CardType.Monster && consecutiveMonsterDraws >= 4)
                        patternBoost *= 1.8f;

                    // Soft element diversity so players don't pull monotonous elemental streaks
                    if (!string.IsNullOrEmpty(lastDrawnElement) && c.element == lastDrawnElement)
                        patternBoost *= 0.55f;

                    // Dynamic excitement curve: as Mana scales, high-tier bosses gain dramatic presence while mixed tiers stay in play
                    if (c.cardType == CardType.Monster && c.tier_num >= 4 && manaCapacity >= 5)
                        patternBoost *= 1.0f + Mathf.Min(0.75f, (manaCapacity - 4) * 0.12f);
                }

                // Match novelty vs Fallen Veteran comeback mix:
                // Never-drawn-yet cards surge; dead veterans retain ~15-22% thrilling comeback weight; other repeats stay suppressed
                float stateFactor;
                if (!drawnCardIdsThisMatch.Contains(c.id))
                {
                    stateFactor = 2.8f + Mathf.Min(3.0f, turnNow * 0.06f);
                }
                else if (c.cardType == CardType.Monster && fallenMonsterDeathCount.TryGetValue(c.id, out int dCount) && dCount > 0)
                {
                    stateFactor = 0.65f * GetFallenComebackWeight(c.id, turnNow);
                }
                else
                {
                    stateFactor = 0.08f;
                }

                // Stochastic chaos jitter prevents deterministic sorted order while honoring probability ratios
                float chaosJitter = UnityEngine.Random.Range(0.55f, 1.65f);

                float wVal = Mathf.Max(0.01f, waitBoost * pityBoost * patternBoost * stateFactor * chaosJitter);
                weights[i] = wVal;
                totalWeight += wVal;
            }

            if (totalWeight <= 0f) return UnityEngine.Random.Range(0, deck.Count);

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;
            for (int i = 0; i < deck.Count; i++)
            {
                cumulative += weights[i];
                if (roll <= cumulative) return i;
            }

            return deck.Count - 1;
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

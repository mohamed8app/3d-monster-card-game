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

        [Header("Resources (Permanent Aether -> Generates Turn Mana)")]
        public const int AetherDrawCost = 2;
        public int power = 0;
        public int Aether { get => power; set => power = value; }
        public int currentMana = 0;
        public int lockedManaNext = 0;
        public bool hasSacrificedThisTurn = false;
        public bool hasAetherDrawnThisTurn = false;
        public int fatigueCount = 0;
        public int monstersSummonedTotal = 0;

        [Header("Tactical Status Effects")]
        public int denyDrawSacTurns = 0;
        public int teamImmuneTurns = 0;
        public int freeSkillBonus = 0;
        public bool freeSkillManaZero = false;
        public int nextSpellRepeats = 0;
        public readonly Dictionary<int, int> discountedCardIds = new Dictionary<int, int>();

        public const int DefaultMaxHandSize = 7;
        public const int MaxCopiesPerCard = 2;

        [Header("Card Collections")]
        public int maxHandSize = DefaultMaxHandSize;
        public List<CardData> baseDeck = new List<CardData>();
        public List<CardData> deck = new List<CardData>();
        public List<CardData> hand = new List<CardData>();
        public List<CardData> graveyard = new List<CardData>();
        public int deckCycleCount = 1;

        [Header("Side Leader & Right-Wing Covenant Altar Slots")]
        public CardData activeLeader = null;
        public CardData activeCovenant = null;
        public bool leaderExorcised = false;
        public int leaderBetrayedTurns = 0;
        public int leaderMutedTurns = 0;
        public bool covenantDestroyed = false;
        public int covenantMutedTurns = 0;
        public int covPermHpLost = 0;
        public int cardsPlayedThisTurn = 0;
        public int monstersSummonedThisTurn = 0;
        [NonSerialized] public string lastSummonFailReason = "";

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
        public Action<PlayerState, CardData> onCardDrawnToHand;
        public Action<PlayerState, CardData, CardData> onOldestCardBurned;

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
            baseDeck = new List<CardData>();
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
            deckCycleCount = 1;

            var copyCounts = new Dictionary<int, int>();
            if (initialDeck != null)
            {
                foreach (var c in initialDeck)
                {
                    if (c == null) continue;
                    int curCopies = copyCounts.TryGetValue(c.id, out int cnt) ? cnt : 0;
                    // V2 Constitution Rule #3: Max 2 Copies of any card in the 50-card deck
                    if (curCopies < MaxCopiesPerCard)
                    {
                        copyCounts[c.id] = curCopies + 1;
                        baseDeck.Add(c);
                        deck.Add(c);
                        cardTurnsWaiting[c.id] = 0;
                    }
                }
            }

            ShuffleDeckAntiClump();
            hand.Clear();
            graveyard.Clear();
            activeLeader = null;
            activeCovenant = null;
            leaderExorcised = false;
            leaderBetrayedTurns = 0;
            leaderMutedTurns = 0;
            covenantDestroyed = false;
            covenantMutedTurns = 0;
            cardsPlayedThisTurn = 0;
            monstersSummonedThisTurn = 0;
            maxHp = 500;
            currentHp = maxHp;
            shield = 0;
            power = 0;
            currentMana = 0;
            lockedManaNext = 0;
            fatigueCount = 0;
            monstersSummonedTotal = 0;
            hasSacrificedThisTurn = false;
            denyDrawSacTurns = 0;
            teamImmuneTurns = 0;
            freeSkillBonus = 0;
            freeSkillManaZero = false;
            discountedCardIds.Clear();
        }

        public void AddRefillCardsToDeck(List<CardData> refillCards)
        {
            if (refillCards == null || refillCards.Count == 0) return;

            var copyCounts = new Dictionary<int, int>();
            foreach (var c in deck)
            {
                if (c != null)
                    copyCounts[c.id] = (copyCounts.TryGetValue(c.id, out int ec) ? ec : 0) + 1;
            }

            foreach (var c in refillCards)
            {
                if (c == null) continue;
                // Omit already-active Leader or Covenant on Cycle 2+ refills
                if (activeLeader != null && c.id == activeLeader.id) continue;
                if (activeCovenant != null && c.id == activeCovenant.id) continue;
                int cur = copyCounts.TryGetValue(c.id, out int cc) ? cc : 0;
                if (cur < MaxCopiesPerCard)
                {
                    copyCounts[c.id] = cur + 1;
                    deck.Add(c);
                    if (!cardTurnsWaiting.ContainsKey(c.id))
                    {
                        cardTurnsWaiting[c.id] = 0;
                    }
                }
            }
            ShuffleDeckAntiClump();
        }

        public void ShuffleDeck()
        {
            ShuffleDeckAntiClump();
        }

        public void ShuffleDeckAntiClump()
        {
            for (int i = 0; i < deck.Count; i++)
            {
                int rnd = UnityEngine.Random.Range(i, deck.Count);
                var temp = deck[i];
                deck[i] = deck[rnd];
                deck[rnd] = temp;
            }
            // V2 Anti-Clump Golden Spacing: separate identical 2x copies by at least 4 cards
            for (int i = 0; i < deck.Count - 1; i++)
            {
                if (deck[i] != null && deck[i + 1] != null && deck[i].id == deck[i + 1].id)
                {
                    int swapIdx = (i + 5) % deck.Count;
                    var tmp = deck[i + 1];
                    deck[i + 1] = deck[swapIdx];
                    deck[swapIdx] = tmp;
                }
            }
        }

        public void StartTurn(int turnNumber, bool skipDraw = false)
        {
            // 1. Pure-Sacrifice Aether Economy: NO automatic +1 permanent Aether!
            // Every turn grants +1 baseline Mana on top of permanent Aether (Turn Mana = min(cap, Aether + 1) - locked).
            // Permanent Aether grows ONLY through Sacrifice (+1 Aether & +1 Mana) or Aether cards/skills.
            int manaCap = V2CardMechanicsEngine.GetPlayerManaCap(this);
            if (power > manaCap) power = manaCap;
            int locked = lockedManaNext;
            lockedManaNext = 0;
            int baseTurnMana = Mathf.Min(manaCap, power + 1);
            currentMana = Mathf.Max(0, baseTurnMana - locked);
            hasSacrificedThisTurn = false;
            hasAetherDrawnThisTurn = false;
            cardsPlayedThisTurn = 0;
            monstersSummonedThisTurn = 0;

            if (leaderBetrayedTurns > 0) leaderBetrayedTurns--;
            if (leaderMutedTurns > 0) leaderMutedTurns--;
            if (covenantMutedTurns > 0) covenantMutedTurns--;

            // 2. Check Total Eclipse effect (denies draw & sacrifice for 1 turn) or explicit Turn-1 skip
            if (denyDrawSacTurns > 0)
            {
                denyDrawSacTurns--;
                hasSacrificedThisTurn = true;
                Debug.Log($"🌑 [{playerName}] is under Total Eclipse—cannot draw or sacrifice this turn!");
            }
            else if (!skipDraw)
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

        /// <summary>
        /// Adds an incoming card to the end (newest/rightmost) of hand while enforcing maxHandSize.
        /// If hand is already at capacity, burns the oldest card (index 0, leftmost) to graveyard first.
        /// </summary>
        public CardData AddCardToHandWithBurnLimit(CardData incomingCard)
        {
            if (incomingCard == null) return null;

            int cap = maxHandSize > 0 ? maxHandSize : DefaultMaxHandSize;
            CardData lastBurned = null;

            while (hand.Count >= cap && hand.Count > 0)
            {
                lastBurned = hand[0];
                hand.RemoveAt(0);
                if (lastBurned != null)
                {
                    graveyard.Add(lastBurned);
                    onOldestCardBurned?.Invoke(this, lastBurned, incomingCard);
                    Debug.Log($"🔥 [{playerName}] Hand limit ({cap}) exceeded! Burned oldest card [{lastBurned.name}] to graveyard for [{incomingCard.name}].");
                }
            }

            hand.Add(incomingCard);
            return lastBurned;
        }

        public bool DrawCard()
        {
            if (deck.Count == 0)
            {
                deckCycleCount++;
                if (baseDeck != null && baseDeck.Count > 0)
                {
                    AddRefillCardsToDeck(baseDeck);
                }
                else if (onDeckExhaustedRefill != null)
                {
                    var refill = onDeckExhaustedRefill.Invoke(this);
                    AddRefillCardsToDeck(refill);
                }
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

                AddCardToHandWithBurnLimit(card);
                onCardDrawnToHand?.Invoke(this, card);
                if (MonsterCardGame.Audio.SoundManager.Instance != null)
                {
                    MonsterCardGame.Audio.SoundManager.Instance.PlayCardDraw();
                }
                if (wasFallenVeteran && MonsterCardGame.UI.BattleUIManager.Instance != null)
                {
                    MonsterCardGame.UI.BattleUIManager.Instance.LogCombatMessage($"🔥💀 [{playerName}] Comeback Draw! Fallen Monster #{card.name} returned for a rematch!", card);
                }
                Debug.Log($"[{playerName}] drew card #{card.name} ({card.cardType}) | Hand: {hand.Count}/{maxHandSize} | Deck left: {deck.Count} (Cycle #{deckCycleCount})");
                return true;
            }

            // V2 Constitution Rule #8 & #9: Infinite Shuffle-Bag — ZERO Deck-Out or Fatigue Damage!
            return false;
        }

        public CardData DrawSpecificFromDeck(Func<CardData, bool> predicate, Action<CardData> adjustBeforeAdd = null)
        {
            if (deck == null || predicate == null) return null;
            int idx = deck.FindIndex(c => c != null && predicate(c));
            if (idx < 0) return null;

            var card = deck[idx];
            deck.RemoveAt(idx);
            cardTurnsWaiting.Remove(card.id);
            drawnCardIdsThisMatch.Add(card.id);
            adjustBeforeAdd?.Invoke(card);

            onCardDrawnCallback?.Invoke(card.id);
            AddCardToHandWithBurnLimit(card);
            onCardDrawnToHand?.Invoke(this, card);
            return card;
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

                    // Dynamic excitement curve: as Power scales, high-tier bosses gain dramatic presence while mixed tiers stay in play
                    if (c.cardType == CardType.Monster && c.tier_num >= 4 && power >= 5)
                        patternBoost *= 1.0f + Mathf.Min(0.75f, (power - 4) * 0.12f);
                }

                // Match novelty vs Fallen Veteran comeback mix:
                // Never-drawn-yet cards surge; dead veterans retain ~15-22% thrilling comeback weight; other repeats stay suppressed
                float stateFactor;
                if (c.cardType == CardType.Environment || !drawnCardIdsThisMatch.Contains(c.id))
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

            if (denyDrawSacTurns > 0)
            {
                Debug.LogWarning("Card sacrifice is blocked this turn!");
                return false;
            }

            int manaCap = V2CardMechanicsEngine.GetPlayerManaCap(this);
            if (power >= manaCap)
            {
                Debug.LogWarning($"Aether is capped by active Covenant ({manaCap})!");
                return false;
            }

            if (!hand.Contains(cardToSacrifice))
                return false;

            hand.Remove(cardToSacrifice);
            graveyard.Add(cardToSacrifice);

            power = Mathf.Min(manaCap, power + 1);
            currentMana++;
            hasSacrificedThisTurn = true;

            if (GameManager.Instance != null)
            {
                var opponent = (this == GameManager.Instance.humanPlayer) ? GameManager.Instance.aiPlayer : GameManager.Instance.humanPlayer;
                CommanderSpiritEffects.OnCardSacrificed(this, opponent);
            }

            Debug.Log($"[{playerName}] sacrificed [{cardToSacrifice.name}]. +1 Aether (Total Aether: {power}), +1 Mana (Current Mana: {currentMana})");
            return true;
        }

        public bool CanAetherDraw(out string reason)
        {
            reason = string.Empty;
            if (hasAetherDrawnThisTurn)
            {
                reason = "You already used Aether Draw this turn (1 per turn).";
                return false;
            }
            if (denyDrawSacTurns > 0)
            {
                reason = "Card drawing is blocked this turn.";
                return false;
            }
            if (V2CardMechanicsEngine.IsExtraDrawBanned(this))
            {
                reason = "Your active Covenant prevents extra card draws.";
                return false;
            }
            if (currentMana < AetherDrawCost)
            {
                reason = $"Need {AetherDrawCost} Mana to draw an extra card (Available: {currentMana}).";
                return false;
            }
            if (hand != null && hand.Count >= maxHandSize)
            {
                reason = $"Your hand is already full ({hand.Count}/{maxHandSize}).";
                return false;
            }
            return true;
        }

        public bool TryAetherDraw(out string reason)
        {
            if (!CanAetherDraw(out reason))
                return false;

            currentMana -= AetherDrawCost;
            hasAetherDrawnThisTurn = true;
            DrawCard();
            Debug.Log($"✨ [{playerName}] used Aether Draw (-{AetherDrawCost} Mana -> +1 Card | Mana: {currentMana}/{power}).");
            return true;
        }

        public int GetEffectiveCardCost(CardData card)
        {
            if (card == null) return 0;
            int disc = (card.cardType == CardType.Monster && discountedCardIds.TryGetValue(card.id, out int charges) && charges > 0) ? 1 : 0;
            int covDelta = V2CardMechanicsEngine.GetCardCostDelta(this, card);
            return Mathf.Max(0, card.cost - disc + covDelta);
        }

        public bool SummonMonster(CardData card, int targetSlotIndex, int currentTurn)
        {
            lastSummonFailReason = "";
            if (targetSlotIndex < 0 || targetSlotIndex >= slots.Length)
            {
                lastSummonFailReason = "Invalid board slot.";
                return false;
            }

            if (slots[targetSlotIndex].IsOccupied)
            {
                lastSummonFailReason = $"Slot {targetSlotIndex + 1} is already occupied!";
                Debug.LogWarning(lastSummonFailReason);
                return false;
            }

            if (!V2CardMechanicsEngine.CanPlayerSummonMonster(this, card, out string covReason))
            {
                lastSummonFailReason = covReason;
                Debug.LogWarning(covReason);
                return false;
            }

            int effCost = GetEffectiveCardCost(card);
            if (effCost > currentMana)
            {
                lastSummonFailReason = $"Not enough Mana for [{card.name}]! Required: {effCost}💧, Available: {currentMana}💧";
                Debug.LogWarning(lastSummonFailReason);
                return false;
            }

            currentMana -= effCost;
            if (discountedCardIds.TryGetValue(card.id, out int rem) && rem > 0)
            {
                if (rem <= 1) discountedCardIds.Remove(card.id);
                else discountedCardIds[card.id] = rem - 1;
            }
            hand.Remove(card);
            monstersSummonedTotal++;
            monstersSummonedThisTurn++;
            cardsPlayedThisTurn++;

            var instance = new MonsterInstance(card, currentTurn);
            slots[targetSlotIndex].PlaceMonster(instance);

            if (GameManager.Instance != null)
            {
                var opponent = (this == GameManager.Instance.humanPlayer) ? GameManager.Instance.aiPlayer : GameManager.Instance.humanPlayer;
                V2CardMechanicsEngine.ApplyV2OnMonsterSummoned(this, opponent, targetSlotIndex, instance);
                if (CombatSystem.Instance != null)
                {
                    CombatSystem.Instance.ApplyOnSummonPassives(this, opponent, targetSlotIndex, instance);
                }
                CommanderSpiritEffects.OnMonsterSummoned(this, opponent, targetSlotIndex, instance);
            }

            Debug.Log($"[{playerName}] summoned Monster [{card.name}] in Slot {targetSlotIndex}");
            return true;
        }

        public void TakeLeaderDamage(int damage)
        {
            if (damage <= 0 || teamImmuneTurns > 0) return;

            if (slots != null)
            {
                foreach (var s in slots)
                {
                    if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive &&
                        s.currentMonster.HasPassiveSubstr("Tectonic Guard") != null)
                    {
                        damage = Mathf.Max(0, damage - 5);
                        break;
                    }
                }
            }
            if (damage <= 0) return;

            // V2 Constitution Rule #20: Shield/Bubble/Armor are eliminated; all Commander damage applies directly to HP
            shield = 0;

            if (damage > 0)
            {
                currentHp = Mathf.Max(0, currentHp - damage);
                Debug.Log($"🚨 [{playerName}] took {damage} direct Commander damage. Remaining HP: {currentHp}/{maxHp}");
                MonsterCardGame.Audio.SoundManager.Instance?.PlayHpDamage();

                if (currentHp <= 0)
                {
                    GameManager.Instance?.CheckGameOver();
                }
            }
        }

        public void HealLeader(int healAmount, PlayerState opponent = null)
        {
            if (healAmount <= 0) return;
            if (V2CardMechanicsEngine.IsSelfHealBanned(this)) return;

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

            int previousHp = currentHp;
            currentHp = Mathf.Min(maxHp, currentHp + healAmount);
            Debug.Log($"💚 [{playerName}] healed Commander for {healAmount} HP. Current HP: {currentHp}/{maxHp}");
            if (currentHp > previousHp)
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayHpHeal();
            }
        }
    }
}

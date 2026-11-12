using System;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;
using MonsterCardGame.UI;
using MonsterCardGame.Utils;

namespace MonsterCardGame.Core
{
    public enum GamePhase
    {
        StartingCoinToss,
        TurnStart,
        ActionPhase,
        CombatPhase,
        TurnEnd,
        GameOver
    }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Players")]
        public PlayerState humanPlayer;
        public PlayerState aiPlayer;
        public AIOpponent aiOpponent;

        [Header("State")]
        public int currentTurn = 1;
        public bool isPlayerTurn = true;
        public GamePhase currentPhase = GamePhase.StartingCoinToss;
        public EnvironmentCardData activeEnvironment = null;

        [Header("Match Statistics")]
        public int playerSpellsCastCount = 0;

        [Header("Deck & Pity Draw Rules")]
        public int defaultDeckSize = 50;
        public int openingHandCount = 6;

        [Header("Database (452 Cards: 240 Monsters + 200 Spells + 12 Environments)")]
        public TextAsset monstersJsonFile;
        public List<MonsterData> allMonsters = new List<MonsterData>();

        // Persistent Pity Tracker so cards not drawn yet climb in probability across draws & matches
        private const string PityPrefsKey = "DeckGame_CardPityStats_v1";
        private readonly Dictionary<int, int> cardSeenCount = new Dictionary<int, int>();
        private readonly Dictionary<int, int> cardMissedDraws = new Dictionary<int, int>();
        private readonly List<int> allCatalogIds = new List<int>();
        private int pityDirtyCounter = 0;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void OnDestroy()
        {
            SaveCardPityStats();
            if (Instance == this) Instance = null;
        }

        private void OnApplicationQuit()
        {
            SaveCardPityStats();
        }

        private void Start()
        {
            LoadMonsterDatabase();
            BuildCatalogIdIndex();
            LoadCardPityStats();
            StartGame();
        }

        private void LoadMonsterDatabase()
        {
            var fullDbAsset = Resources.Load<TextAsset>("cards_10000");
            if (fullDbAsset != null)
            {
                var wrapper = JsonUtility.FromJson<AllCardsDatabaseWrapper>(fullDbAsset.text);
                if (wrapper != null && wrapper.cards != null && wrapper.cards.Count > 0)
                {
                    allMonsters = new List<MonsterData>();
                    var allSpells = new List<CardData>();

                    foreach (var c in wrapper.cards)
                    {
                        if (c == null) continue;
                        if (c.cardType == CardType.Monster || c.card_type == "monster")
                        {
                            var m = new MonsterData
                            {
                                id = c.id,
                                name = c.id.ToString(),
                                cardType = CardType.Monster,
                                card_type = "monster",
                                element = c.element,
                                group = c.group,
                                group_en = c.group_en,
                                role = c.role,
                                tier = c.tier_title,
                                tier_title = c.tier_title,
                                tier_num = c.tier_num,
                                cost = c.cost,
                                atk = c.atk,
                                def = c.def,
                                passives_text = c.passives_text,
                                active_skill = c.active_skill,
                                passives = c.passives ?? new List<string>(),
                                active_skills = c.active_skills ?? new List<string>(),
                                description = c.description,
                                p_score = c.p_score,
                                a_score = c.a_score,
                                win_rate = c.win_rate
                            };
                            allMonsters.Add(m);
                        }
                        else if (c.cardType == CardType.Spell || c.card_type == "spell")
                        {
                            c.cardType = CardType.Spell;
                            c.name = c.id.ToString();
                            SpellDatabase.NormalizeSpellMetadata(c);
                            allSpells.Add(c);
                        }
                    }

                    SpellDatabase.SetSpellsFromDatabase(allSpells);
                    Debug.Log($"✅ Loaded 452-Card Balanced Database: {allMonsters.Count} Monsters + {allSpells.Count} Tactical Spells!");
                    return;
                }
            }

            if (monstersJsonFile == null)
            {
                monstersJsonFile = Resources.Load<TextAsset>("monsters_150");
            }

            if (monstersJsonFile != null)
            {
                string json = monstersJsonFile.text;
                string wrappedJson = "{\"monsters\":" + json + "}";
                var wrapper = JsonUtility.FromJson<MonsterDatabaseWrapper>(wrappedJson);
                allMonsters = wrapper.monsters;
                if (allMonsters != null)
                {
                    foreach (var m in allMonsters)
                    {
                        if (m != null) m.name = m.id.ToString();
                    }
                }
                Debug.Log($"✅ Loaded {allMonsters.Count} Monsters from fallback database!");
            }
        }

        public void StartGame()
        {
            var deckA = GenerateDeck(defaultDeckSize);
            var deckB = GenerateDeck(defaultDeckSize);

            humanPlayer.InitializeDeck(deckA);
            aiPlayer.InitializeDeck(deckB);

            humanPlayer.onDeckExhaustedRefill = RefillPlayerDeck;
            humanPlayer.getCardDrawWeightMultiplier = GetCardPityWeight;
            humanPlayer.onCardDrawnCallback = RecordCardDrawnForPity;

            aiPlayer.onDeckExhaustedRefill = RefillPlayerDeck;
            aiPlayer.getCardDrawWeightMultiplier = GetCardPityWeight;
            aiPlayer.onCardDrawnCallback = null;

            // Draw 5 pre-turn cards; StartTurn() draws the 6th card on Turn 1 -> 6 random opening cards
            int preTurnDraws = Mathf.Max(0, openingHandCount - 1);
            for (int i = 0; i < preTurnDraws; i++)
            {
                humanPlayer.DrawCard();
                aiPlayer.DrawCard();
            }

            isPlayerTurn = true;
            currentTurn = 1;
            activeEnvironment = null;

            // Fair starting Mana & Power = 7 so Card #15 (Cost 5) can be summoned + its Active Skill (Cost 2) tested immediately on Turn 1
            humanPlayer.manaCapacity = 7;
            humanPlayer.currentPower = 7;
            aiPlayer.manaCapacity = 3;
            aiPlayer.currentPower = 3;

            Debug.Log($"🎲 Epic Battle Started! Decks: {defaultDeckSize} unique cards each | Opening Hand: {openingHandCount} | Unlimited Scaling & Unseen Pity Active.");

            StartTurn();

            // Place Monster Card #15 (Dragon) directly into opening hand slot 0 for immediate battle & skill audio testing without duplicating IDs
            if (allMonsters != null)
            {
                var dragonProto = allMonsters.Find(m => m != null && m.id == 15);
                if (dragonProto != null && !humanPlayer.hand.Exists(c => c != null && c.id == 15))
                {
                    humanPlayer.deck.RemoveAll(c => c != null && c.id == 15);
                    if (humanPlayer.hand.Count > 0)
                    {
                        var displaced = humanPlayer.hand[0];
                        if (displaced != null)
                        {
                            humanPlayer.drawnCardIdsThisMatch.Remove(displaced.id);
                            humanPlayer.deck.Add(displaced);
                            humanPlayer.ShuffleDeck();
                        }
                        humanPlayer.hand[0] = CloneMonsterCard(dragonProto);
                    }
                    else
                    {
                        humanPlayer.hand.Add(CloneMonsterCard(dragonProto));
                    }
                    humanPlayer.drawnCardIdsThisMatch.Add(15);
                }
            }

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.UpdateUI();
                if (humanPlayer.hand != null && humanPlayer.hand.Count > 0)
                {
                    BattleUIManager.Instance.ShowCardDetails(humanPlayer.hand[0]);
                }
            }
        }

        private List<CardData> RefillPlayerDeck(PlayerState player)
        {
            if (player == null) return new List<CardData>();

            player.waveNumber++;
            var waveCards = new List<CardData>();
            var activeHandAndBoard = new HashSet<int>();
            foreach (var h in player.hand) if (h != null) activeHandAndBoard.Add(h.id);
            foreach (var s in player.slots) if (s != null && s.IsOccupied) activeHandAndBoard.Add(s.currentMonster.data.id);

            var waveUsedIds = new HashSet<int>(activeHandAndBoard);

            // 1. Exciting ~20% Veteran Comeback quota from monsters that actually fought and died earlier in this match
            int monsterQuotaTotal = Mathf.RoundToInt(defaultDeckSize * 0.60f);
            int maxFallenQuota = Mathf.Max(1, Mathf.RoundToInt(monsterQuotaTotal * 0.22f)); // up to ~6 comeback veterans per 50-card wave
            var fallenCandidates = new List<MonsterData>();
            if (allMonsters != null && player.fallenMonsterDeathCount.Count > 0)
            {
                foreach (var m in allMonsters)
                {
                    if (m != null && !waveUsedIds.Contains(m.id)
                        && player.fallenMonsterDeathCount.TryGetValue(m.id, out int dc) && dc > 0)
                    {
                        fallenCandidates.Add(m);
                    }
                }
            }

            int comebackCount = 0;
            if (fallenCandidates.Count > 0)
            {
                int targetComeback = Mathf.Min(maxFallenQuota, fallenCandidates.Count);
                comebackCount = PickWeightedWithoutReplacement(
                    fallenCandidates,
                    targetComeback,
                    waveUsedIds,
                    m => m.id,
                    CloneMonsterCard,
                    waveCards,
                    id => player.GetFallenComebackWeight(id, currentTurn) * UnityEngine.Random.Range(0.55f, 1.65f));
            }

            // 2. Remaining ~80%+ slots prioritize strictly NEVER-DRAWN-YET cards in this match (with escalating global pity)
            int remainingSlots = Mathf.Max(0, defaultDeckSize - waveCards.Count);
            var excludeForFresh = new HashSet<int>(player.drawnCardIdsThisMatch);
            foreach (int id in waveUsedIds) excludeForFresh.Add(id);

            var freshBatch = GenerateDeck(remainingSlots, excludeForFresh);
            if (freshBatch.Count < remainingSlots)
            {
                // All cards in the 452-card catalog have appeared at least once this match -> cycle unseen frontier cleanly
                player.drawnCardIdsThisMatch.Clear();
                foreach (var c in waveCards) if (c != null) player.drawnCardIdsThisMatch.Add(c.id);
                var cycleExclude = new HashSet<int>(waveUsedIds);
                foreach (var c in freshBatch) if (c != null) cycleExclude.Add(c.id);
                var extra = GenerateDeck(remainingSlots - freshBatch.Count, cycleExclude);
                freshBatch.AddRange(extra);
            }

            waveCards.AddRange(freshBatch);

            // Final Fisher-Yates shuffle so comeback veterans & fresh unseen cards interleave unpredictably
            for (int i = 0; i < waveCards.Count; i++)
            {
                int r = UnityEngine.Random.Range(i, waveCards.Count);
                var temp = waveCards[i];
                waveCards[i] = waveCards[r];
                waveCards[r] = temp;
            }

            if (BattleUIManager.Instance != null && waveCards.Count > 0)
            {
                BattleUIManager.Instance.LogCombatMessage(
                    $"🌊 [{player.playerName}] Endless Wave #{player.waveNumber}: +{waveCards.Count} shuffled cards ({comebackCount} fallen veterans + {waveCards.Count - comebackCount} fresh unseen)!");
            }
            return waveCards;
        }

        public List<CardData> GenerateDeck(int count, HashSet<int> excludeIds = null)
        {
            var deck = new List<CardData>();
            if (allMonsters == null || allMonsters.Count == 0 || count <= 0) return deck;

            var usedIds = excludeIds != null ? new HashSet<int>(excludeIds) : new HashSet<int>();

            int monsterTarget = Mathf.RoundToInt(count * 0.60f); // 30 Monsters out of 50
            int spellTarget = Mathf.RoundToInt(count * 0.34f);   // 17 Spells out of 50
            int envTarget = Mathf.Max(0, count - monsterTarget - spellTarget); // 3 Environments out of 50

            // 1. Stratified Monster selection without replacement (balanced early/mid/late costs + escalating unseen pity)
            int earlyCount = Mathf.RoundToInt(monsterTarget * 0.35f);
            int midCount = Mathf.RoundToInt(monsterTarget * 0.40f);
            int lateCount = Mathf.Max(0, monsterTarget - earlyCount - midCount);

            var earlyPool = allMonsters.FindAll(m => m != null && m.cost <= 3);
            var midPool = allMonsters.FindAll(m => m != null && m.cost >= 4 && m.cost <= 5);
            var latePool = allMonsters.FindAll(m => m != null && m.cost >= 6);

            int pickedMonsters = 0;
            pickedMonsters += PickWeightedWithoutReplacement(earlyPool, earlyCount, usedIds, m => m.id, CloneMonsterCard, deck);
            pickedMonsters += PickWeightedWithoutReplacement(midPool, midCount, usedIds, m => m.id, CloneMonsterCard, deck);
            pickedMonsters += PickWeightedWithoutReplacement(latePool, lateCount, usedIds, m => m.id, CloneMonsterCard, deck);

            if (pickedMonsters < monsterTarget)
            {
                PickWeightedWithoutReplacement(allMonsters, monsterTarget - pickedMonsters, usedIds, m => m.id, CloneMonsterCard, deck);
            }

            // 2. Diverse Spells without replacement
            var spellPool = SpellDatabase.GetAllSpells();
            if (spellPool != null && spellPool.Count > 0)
            {
                PickWeightedWithoutReplacement(spellPool, spellTarget, usedIds, s => s.id, CloneSpellCard, deck);
            }

            // 3. Global Environment Cards without replacement
            var envPool = EnvironmentCardData.GetAllEnvironments();
            if (envPool != null && envPool.Count > 0)
            {
                PickWeightedWithoutReplacement(envPool, envTarget, usedIds, e => e.id, e => new EnvironmentCardData(e.element, 1), deck);
            }

            // Top up to requested count from any remaining unseen monsters/spells if a pool ran dry during marathon refills
            if (deck.Count < count)
            {
                PickWeightedWithoutReplacement(allMonsters, count - deck.Count, usedIds, m => m.id, CloneMonsterCard, deck);
            }
            if (deck.Count < count && spellPool != null)
            {
                PickWeightedWithoutReplacement(spellPool, count - deck.Count, usedIds, s => s.id, CloneSpellCard, deck);
            }

            // Final Fisher-Yates shuffle
            for (int i = 0; i < deck.Count; i++)
            {
                int r = UnityEngine.Random.Range(i, deck.Count);
                var temp = deck[i];
                deck[i] = deck[r];
                deck[r] = temp;
            }

            return deck;
        }

        private int PickWeightedWithoutReplacement<T>(
            List<T> sourcePool,
            int targetCount,
            HashSet<int> usedIds,
            Func<T, int> idSelector,
            Func<T, CardData> cloneFactory,
            List<CardData> destination,
            Func<int, float> customWeightSelector = null)
        {
            if (sourcePool == null || sourcePool.Count == 0 || targetCount <= 0) return 0;

            var candidates = new List<T>();
            foreach (var item in sourcePool)
            {
                if (item == null) continue;
                int id = idSelector(item);
                if (!usedIds.Contains(id)) candidates.Add(item);
            }

            Func<int, float> weightFn = customWeightSelector ?? GetCardPityWeight;

            int added = 0;
            while (added < targetCount && candidates.Count > 0)
            {
                float totalWeight = 0f;
                for (int i = 0; i < candidates.Count; i++)
                {
                    totalWeight += Mathf.Max(0.01f, weightFn(idSelector(candidates[i])));
                }

                int pickedIdx = candidates.Count - 1;
                if (totalWeight > 0f)
                {
                    float roll = UnityEngine.Random.Range(0f, totalWeight);
                    float acc = 0f;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        acc += Mathf.Max(0.01f, weightFn(idSelector(candidates[i])));
                        if (roll <= acc)
                        {
                            pickedIdx = i;
                            break;
                        }
                    }
                }
                else
                {
                    pickedIdx = UnityEngine.Random.Range(0, candidates.Count);
                }

                var chosen = candidates[pickedIdx];
                candidates.RemoveAt(pickedIdx);
                int chosenId = idSelector(chosen);
                if (usedIds.Add(chosenId))
                {
                    var cloned = cloneFactory(chosen);
                    if (cloned != null)
                    {
                        destination.Add(cloned);
                        added++;
                    }
                }
            }
            return added;
        }

        private static MonsterData CloneMonsterCard(MonsterData src)
        {
            return new MonsterData
            {
                id = src.id,
                name = src.id.ToString(),
                cardType = CardType.Monster,
                card_type = "monster",
                element = src.element,
                group = src.group,
                group_en = src.group_en,
                role = src.role,
                tier = src.tier,
                tier_title = src.tier_title,
                tier_num = src.tier_num,
                cost = src.cost,
                atk = src.atk,
                def = src.def,
                passives_text = src.passives_text,
                active_skill = src.active_skill,
                passives = src.passives != null ? new List<string>(src.passives) : new List<string>(),
                active_skills = src.active_skills != null ? new List<string>(src.active_skills) : new List<string>(),
                description = src.description,
                p_score = src.p_score,
                a_score = src.a_score,
                win_rate = src.win_rate
            };
        }

        private static CardData CloneSpellCard(CardData proto)
        {
            return new CardData
            {
                id = proto.id,
                name = proto.id.ToString(),
                cardType = CardType.Spell,
                card_type = "spell",
                element = proto.element,
                group = proto.group,
                role = proto.role,
                tier = proto.tier_title,
                tier_title = proto.tier_title,
                tier_num = proto.tier_num,
                cost = proto.cost,
                spellType = proto.spellType,
                spellPower = proto.spellPower,
                active_skill = proto.active_skill,
                description = proto.description
            };
        }

        private void BuildCatalogIdIndex()
        {
            allCatalogIds.Clear();
            var seen = new HashSet<int>();
            if (allMonsters != null)
            {
                foreach (var m in allMonsters)
                {
                    if (m != null && seen.Add(m.id)) allCatalogIds.Add(m.id);
                }
            }
            var spells = SpellDatabase.GetAllSpells();
            if (spells != null)
            {
                foreach (var s in spells)
                {
                    if (s != null && seen.Add(s.id)) allCatalogIds.Add(s.id);
                }
            }
            var envs = EnvironmentCardData.GetAllEnvironments();
            foreach (var e in envs)
            {
                if (e != null && seen.Add(e.id)) allCatalogIds.Add(e.id);
            }
        }

        public float GetCardPityWeight(int cardId)
        {
            int seen = cardSeenCount.TryGetValue(cardId, out int sc) ? sc : 0;
            int missed = cardMissedDraws.TryGetValue(cardId, out int md) ? md : 0;

            if (seen == 0)
            {
                // Unseen cards start at 5x baseline weight and climb every single missed draw
                return 5.0f + missed * 1.0f;
            }

            return Mathf.Max(0.12f, (1.0f / (1.0f + seen * 1.5f)) + missed * 0.12f);
        }

        private void RecordCardDrawnForPity(int drawnId)
        {
            cardSeenCount[drawnId] = (cardSeenCount.TryGetValue(drawnId, out int curSeen) ? curSeen : 0) + 1;
            cardMissedDraws[drawnId] = 0;

            for (int i = 0; i < allCatalogIds.Count; i++)
            {
                int id = allCatalogIds[i];
                if (id == drawnId) continue;
                int prev = cardMissedDraws.TryGetValue(id, out int m) ? m : 0;
                if (prev < 300) cardMissedDraws[id] = prev + 1;
            }

            pityDirtyCounter++;
            if (pityDirtyCounter >= 6)
            {
                pityDirtyCounter = 0;
                SaveCardPityStats();
            }
        }

        private void LoadCardPityStats()
        {
            cardSeenCount.Clear();
            cardMissedDraws.Clear();

            string raw = PlayerPrefs.GetString(PityPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return;

            string[] entries = raw.Split(';');
            foreach (string entry in entries)
            {
                if (string.IsNullOrEmpty(entry)) continue;
                string[] parts = entry.Split(':');
                if (parts.Length >= 3
                    && int.TryParse(parts[0], out int id)
                    && int.TryParse(parts[1], out int seen)
                    && int.TryParse(parts[2], out int missed))
                {
                    cardSeenCount[id] = Mathf.Max(0, seen);
                    cardMissedDraws[id] = Mathf.Clamp(missed, 0, 300);
                }
            }
        }

        private void SaveCardPityStats()
        {
            if (allCatalogIds.Count == 0 && cardSeenCount.Count == 0 && cardMissedDraws.Count == 0) return;

            var sb = new System.Text.StringBuilder(2048);
            var keys = new HashSet<int>(cardSeenCount.Keys);
            foreach (int k in cardMissedDraws.Keys) keys.Add(k);

            foreach (int id in keys)
            {
                int seen = cardSeenCount.TryGetValue(id, out int sc) ? sc : 0;
                int missed = cardMissedDraws.TryGetValue(id, out int md) ? md : 0;
                if (seen == 0 && missed == 0) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(id).Append(':').Append(seen).Append(':').Append(missed);
            }

            PlayerPrefs.SetString(PityPrefsKey, sb.ToString());
            PlayerPrefs.Save();
        }

        public void StartTurn()
        {
            currentPhase = GamePhase.TurnStart;
            var activePlayer = isPlayerTurn ? humanPlayer : aiPlayer;
            var otherPlayer = isPlayerTurn ? aiPlayer : humanPlayer;

            if (otherPlayer.teamImmuneTurns > 0)
            {
                otherPlayer.teamImmuneTurns--;
            }

            activePlayer.StartTurn(currentTurn);

            if (CombatSystem.Instance != null)
            {
                CombatSystem.Instance.TriggerStartOfTurnPassives(activePlayer, otherPlayer);
            }

            currentPhase = GamePhase.ActionPhase;

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.UpdateUI();
            }

            if (isPlayerTurn)
            {
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowBanner($"🟢 YOUR TURN (Round {currentTurn} - Unlimited Mana ∞)", new Color(0.2f, 0.95f, 0.5f));
                }
            }
            else
            {
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowBanner("🔴 ENEMY TURN (Thinking...)", new Color(1f, 0.35f, 0.35f));
                }
                aiOpponent.PlayTurn(currentTurn, activeEnvironment, EndTurn);
            }
        }

        #region Spell & Environment Mechanisms (5 Intermediary Roles + Classic)

        public bool CastSpell(PlayerState caster, PlayerState target, CardData spellCard)
        {
            if (spellCard == null || spellCard.cardType != CardType.Spell) return false;
            if (spellCard.cost > caster.currentPower)
            {
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.LogCombatMessage($"❌ Not enough Power to cast Spell [{spellCard.name}]! (Req: {spellCard.cost}⚡, Avail: {caster.currentPower}⚡)");
                }
                return false;
            }

            caster.currentPower -= spellCard.cost;
            caster.hand.Remove(spellCard);
            caster.graveyard.Add(spellCard);

            if (caster == humanPlayer)
            {
                playerSpellsCastCount++;
            }

            if (MonsterCardGame.Audio.SoundManager.Instance != null)
            {
                MonsterCardGame.Audio.SoundManager.Instance.PlaySpellCast(spellCard);
            }

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.ShowSpellBanner(spellCard, caster.playerName);
                BattleUIManager.Instance.LogCombatMessage($"🔮 [{caster.playerName}] cast Spell #{spellCard.name} ({spellCard.cost}⚡): {spellCard.description}");
            }

            int val = Mathf.Max(10, spellCard.spellPower);
            int repeats = 1;
            foreach (var s in caster.slots)
            {
                if (s != null && s.IsOccupied)
                {
                    var bm = s.currentMonster;
                    if (bm.HasPassiveSubstr("Arcane Echo") != null) bm.bonusAtk += 3;
                    if (bm.HasPassiveSubstr("Arcane Overflow") != null) val += 8;
                    if (bm.HasPassiveSubstr("Spell Multiplier") != null && repeats == 1 && UnityEngine.Random.value < 0.50f) repeats = 2;
                    if (bm.HasPassiveSubstr("Mana Recovery") != null) caster.currentPower += 1;
                }
            }

            for (int rep = 0; rep < repeats; rep++)
            {
                ExecuteSingleSpellEffect(caster, target, spellCard.spellType, val);
            }

            CheckGameOver();
            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.UpdateUI();
            }
            return true;
        }

        private List<BoardSlot> GetAliveSlots(PlayerState p)
        {
            var list = new List<BoardSlot>();
            foreach (var s in p.slots)
            {
                if (s != null && s.IsOccupied) list.Add(s);
            }
            return list;
        }

        private void ExecuteSingleSpellEffect(PlayerState caster, PlayerState target, SpellType stype, int val)
        {
            var allies = GetAliveSlots(caster);
            var enemies = GetAliveSlots(target);

            switch (stype)
            {
                case SpellType.DirectDamage:
                case SpellType.ArcanePiercingShot:
                    ExecuteDirectDamageSpell(caster, target, val, stype == SpellType.ArcanePiercingShot);
                    break;

                case SpellType.BoardAOE:
                    ExecuteBoardAOESpell(target, val);
                    break;

                case SpellType.LeaderHeal:
                case SpellType.SupportBatteryHeal:
                    if (stype == SpellType.SupportBatteryHeal) caster.currentPower += 2;
                    caster.HealLeader(val, target);
                    foreach (var slot in allies) slot.currentMonster.HealDefense(12);
                    break;

                case SpellType.BoardBuff:
                case SpellType.CleanseAndFortify:
                case SpellType.DistributeShieldToWings:
                    foreach (var slot in allies)
                    {
                        slot.currentMonster.frozenTurns = 0;
                        slot.currentMonster.silencedTurns = 0;
                        slot.currentMonster.burnDmg = 0;
                        slot.currentMonster.poisonDmg = 0;
                        slot.currentMonster.weakenedTurns = 0;
                        slot.currentMonster.AddBonusDef(Mathf.Max(6, val / Mathf.Max(1, allies.Count)));
                    }
                    break;

                case SpellType.FreezeBoard:
                    foreach (var slot in enemies)
                    {
                        slot.currentMonster.frozenTurns = Mathf.Max(slot.currentMonster.frozenTurns, 1);
                        slot.currentMonster.canAttack = false;
                        CombatSystem.Instance.DealSpellOrSkillDamage(slot.slotIndex, slot.currentMonster, val, null, target, caster);
                    }
                    break;

                case SpellType.DrawAndEnergy:
                case SpellType.DrawTwoAndStrike:
                    caster.DrawCard();
                    caster.DrawCard();
                    target.TakeLeaderDamage(Mathf.Max(6, val / 2));
                    break;

                case SpellType.LifeDrain:
                    target.TakeLeaderDamage(val);
                    caster.HealLeader(val, target);
                    break;

                case SpellType.ManaTheft:
                case SpellType.ManaStealAndAOE:
                    if (target.manaCapacity > 1)
                    {
                        target.manaCapacity = Mathf.Max(1, target.manaCapacity - 1);
                    }
                    caster.manaCapacity++;
                    caster.currentPower++;
                    if (stype == SpellType.ManaStealAndAOE)
                    {
                        int aoeDmg = Mathf.Max(6, val / 3);
                        foreach (var es in enemies)
                        {
                            CombatSystem.Instance.DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, aoeDmg, null, target, caster);
                        }
                    }
                    break;

                case SpellType.ExecuteStrongest:
                    ExecuteStrongestEnemy(target);
                    break;

                case SpellType.ArcaneBlast:
                    ExecuteArcaneBlastSpell(target, val);
                    break;

                // --- Category 1: Link & Synergy Spell ---
                case SpellType.LinkWingsStrike:
                    int wingAtk = 0;
                    foreach (var al in allies)
                    {
                        if (al.slotIndex == 0 || al.slotIndex == 1 || al.slotIndex == 4 || al.slotIndex == 5)
                            wingAtk += al.currentMonster.baseAtk + al.currentMonster.bonusAtk;
                    }
                    ExecuteDirectDamageSpell(caster, target, val + Mathf.RoundToInt(wingAtk * 0.45f), false);
                    break;

                case SpellType.CopySupportPassive:
                    if (allies.Count > 0)
                    {
                        var donor = allies[0].currentMonster;
                        var rec = allies[allies.Count - 1].currentMonster;
                        foreach (var p in donor.passives)
                        {
                            if (!rec.passives.Contains(p)) rec.passives.Add(p);
                        }
                        rec.AddBonusDef(val);
                    }
                    else caster.HealLeader(val, target);
                    break;

                case SpellType.LinkCenterTwin:
                case SpellType.ElementalSynergyShield:
                    if (allies.Count > 0)
                    {
                        foreach (var al in allies)
                        {
                            al.currentMonster.bonusAtk += Mathf.Max(3, val / 4);
                            al.currentMonster.AddBonusDef(Mathf.Max(6, val / 2));
                        }
                    }
                    else caster.shield += val;
                    break;

                // --- Category 2: Position & Swap Spell ---
                case SpellType.SwapWingWithCenter:
                    if (allies.Count >= 2)
                    {
                        var s1 = allies[0];
                        var s2 = allies[allies.Count - 1];
                        var m1 = s1.currentMonster;
                        var m2 = s2.currentMonster;
                        s1.ClearSlot();
                        s2.ClearSlot();
                        s1.PlaceMonster(m2);
                        s2.PlaceMonster(m1);
                        m1.AddBonusDef(val);
                        m2.AddBonusDef(val / 2);
                    }
                    else if (allies.Count == 1) allies[0].currentMonster.AddBonusDef(val);
                    else caster.HealLeader(val, target);
                    break;

                case SpellType.PullEnemyCenterToWing:
                case SpellType.PushEnemyCenterOpenLane:
                    if (enemies.Count > 0)
                    {
                        var es = enemies[0];
                        var em = es.currentMonster;
                        foreach (int ws in new[] { 0, 1, 4, 5 })
                        {
                            if (!target.slots[ws].IsOccupied && em.HasPassiveSubstr("Earth Steadfastness") == null)
                            {
                                es.ClearSlot();
                                target.slots[ws].PlaceMonster(em);
                                es = target.slots[ws];
                                break;
                            }
                        }
                        CombatSystem.Instance.DealSpellOrSkillDamage(es.slotIndex, em, val, null, target, caster);
                    }
                    else target.TakeLeaderDamage(val);
                    break;

                case SpellType.TeleportAttackerToWeakest:
                    if (allies.Count > 0)
                    {
                        var am = allies[0].currentMonster;
                        am.bonusAtk += Mathf.Max(4, val / 2);
                        am.canAttack = true;
                        if (enemies.Count > 0)
                            CombatSystem.Instance.DealSpellOrSkillDamage(enemies[0].slotIndex, enemies[0].currentMonster, val / 2, am, target, caster);
                    }
                    else target.TakeLeaderDamage(val);
                    break;

                // --- Category 3: Convert & Trade Spell ---
                case SpellType.ConvertDefToAtk:
                    if (allies.Count > 0)
                    {
                        var am = allies[0].currentMonster;
                        int conv = Mathf.Min(Mathf.Max(0, am.currentDef - 6), val / 2);
                        am.currentDef -= conv;
                        am.bonusAtk += val;
                        am.canAttack = true;
                    }
                    else target.TakeLeaderDamage(val);
                    break;

                case SpellType.InvertEnemyStats:
                case SpellType.SiphonDefToAtk:
                    if (enemies.Count > 0)
                    {
                        var es = enemies[0];
                        CombatSystem.Instance.DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, val, null, target, caster);
                        if (allies.Count > 0)
                        {
                            allies[0].currentMonster.bonusAtk += val / 2;
                            allies[0].currentMonster.AddBonusDef(val / 3);
                        }
                    }
                    else target.TakeLeaderDamage(val);
                    break;

                // --- Category 4: Activate & Support Spell ---
                case SpellType.ZeroSkillCostBoost:
                    caster.currentPower += 1;
                    caster.freeSkillBonus += val / 2;
                    if (allies.Count > 0)
                    {
                        allies[0].currentMonster.bonusAtk += val / 2;
                        allies[0].currentMonster.AddBonusDef(val / 2);
                    }
                    break;

                case SpellType.DoubleStrikeCenter:
                    if (allies.Count > 0)
                    {
                        var am = allies[0].currentMonster;
                        am.canAttack = true;
                        am.doubleStrike = true;
                        am.bonusAtk += val / 2;
                    }
                    else target.TakeLeaderDamage(val);
                    break;

                // --- Category 5: Pierce & Destroy Spell ---
                case SpellType.ReviveFromGraveyard:
                    CardData fallenMonster = null;
                    foreach (var g in caster.graveyard)
                    {
                        if (g != null && g.cardType == CardType.Monster)
                        {
                            if (fallenMonster == null || g.cost > fallenMonster.cost)
                                fallenMonster = g;
                        }
                    }
                    if (fallenMonster != null && caster.hand.Count < 10)
                    {
                        caster.graveyard.Remove(fallenMonster);
                        caster.hand.Add(fallenMonster);
                    }
                    else
                    {
                        caster.DrawCard();
                    }
                    caster.HealLeader(val, target);
                    break;
            }
        }

        private void ExecuteDirectDamageSpell(PlayerState caster, PlayerState target, int damage, bool ignoreImmunity = false)
        {
            BoardSlot bestTargetSlot = null;
            int maxAtk = -1;

            foreach (var slot in target.slots)
            {
                if (slot != null && slot.IsOccupied)
                {
                    if (slot.currentMonster.baseAtk > maxAtk)
                    {
                        maxAtk = slot.currentMonster.baseAtk;
                        bestTargetSlot = slot;
                    }
                }
            }

            if (bestTargetSlot != null)
            {
                CombatSystem.Instance.DealSpellOrSkillDamage(bestTargetSlot.slotIndex, bestTargetSlot.currentMonster, damage, null, target, caster, ignoreImmunity);
                int sIdx = bestTargetSlot.slotIndex;
                bool isPlayerSlot = (target == humanPlayer);
                var slotView = BattleUIManager.Instance != null ? BattleUIManager.Instance.GetSlotView(sIdx, isPlayerSlot) : null;
                if (slotView != null && BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.TriggerHitShake(slotView.transform, Color.red);
                    BattleUIManager.Instance.SpawnDamageNumber(slotView.transform.position, damage, false);
                }
            }
            else
            {
                target.TakeLeaderDamage(damage);
                if (BattleUIManager.Instance != null)
                {
                    var leaderTransform = BattleUIManager.Instance.GetLeaderTransform(target == humanPlayer);
                    if (leaderTransform != null)
                    {
                        BattleUIManager.Instance.TriggerHitShake(leaderTransform, Color.red);
                        BattleUIManager.Instance.SpawnDamageNumber(leaderTransform.position, damage, true);
                    }
                    BattleUIManager.Instance.ShakeScreen();
                }
            }
        }

        private void ExecuteBoardAOESpell(PlayerState target, int damage)
        {
            bool anyHit = false;
            var caster = (target == humanPlayer) ? aiPlayer : humanPlayer;
            foreach (var slot in target.slots)
            {
                if (slot != null && slot.IsOccupied)
                {
                    anyHit = true;
                    CombatSystem.Instance.DealSpellOrSkillDamage(slot.slotIndex, slot.currentMonster, damage, null, target, caster);
                    int sIdx = slot.slotIndex;
                    bool isPlayerSlot = (target == humanPlayer);
                    var slotView = BattleUIManager.Instance != null ? BattleUIManager.Instance.GetSlotView(sIdx, isPlayerSlot) : null;
                    if (slotView != null && BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.TriggerHitShake(slotView.transform, Color.yellow);
                        BattleUIManager.Instance.SpawnDamageNumber(slotView.transform.position, damage, false);
                    }
                }
            }

            if (!anyHit)
            {
                target.TakeLeaderDamage(damage);
                if (BattleUIManager.Instance != null)
                {
                    var leaderTransform = BattleUIManager.Instance.GetLeaderTransform(target == humanPlayer);
                    if (leaderTransform != null)
                    {
                        BattleUIManager.Instance.SpawnDamageNumber(leaderTransform.position, damage, true);
                    }
                }
            }
        }

        private void ExecuteStrongestEnemy(PlayerState target)
        {
            BoardSlot strongest = null;
            int highestThreat = -1;

            foreach (var slot in target.slots)
            {
                if (slot != null && slot.IsOccupied)
                {
                    int threat = slot.currentMonster.baseAtk * 2 + slot.currentMonster.currentDef;
                    if (threat > highestThreat)
                    {
                        highestThreat = threat;
                        strongest = slot;
                    }
                }
            }

            if (strongest != null)
            {
                string monsterName = strongest.currentMonster.data.name;
                var caster = (target == humanPlayer) ? aiPlayer : humanPlayer;
                strongest.currentMonster.currentDef = 0;
                CombatSystem.Instance.HandleMonsterDeath(target, caster, strongest.slotIndex, strongest.currentMonster, null);
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.LogCombatMessage($"💀 Execution Spell banished Monster [{monsterName}] to the graveyard!");
                }
            }
            else
            {
                target.TakeLeaderDamage(30);
            }
        }

        private void ExecuteArcaneBlastSpell(PlayerState target, int totalDamage)
        {
            int occupiedCount = 0;
            foreach (var slot in target.slots)
            {
                if (slot != null && slot.IsOccupied) occupiedCount++;
            }

            var caster = (target == humanPlayer) ? aiPlayer : humanPlayer;
            if (occupiedCount > 0)
            {
                int dmgPerMonster = Mathf.Max(8, totalDamage / (occupiedCount + 1));
                foreach (var slot in target.slots)
                {
                    if (slot != null && slot.IsOccupied)
                    {
                        CombatSystem.Instance.DealSpellOrSkillDamage(slot.slotIndex, slot.currentMonster, dmgPerMonster, null, target, caster);
                    }
                }
                target.TakeLeaderDamage(dmgPerMonster);
            }
            else
            {
                target.TakeLeaderDamage(totalDamage);
            }
            if (BattleUIManager.Instance != null) BattleUIManager.Instance.ShakeScreen();
        }

        public bool PlayEnvironmentCard(PlayerState player, CardData envCard)
        {
            if (envCard == null || envCard.cardType != CardType.Environment) return false;

            if (envCard.cost > player.currentPower)
            {
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.LogCombatMessage($"❌ Not enough Power to activate [{envCard.name}]! (Req: {envCard.cost}⚡, Avail: {player.currentPower}⚡)");
                }
                return false;
            }

            player.currentPower -= envCard.cost;
            player.hand.Remove(envCard);
            player.graveyard.Add(envCard);

            if (activeEnvironment != null && activeEnvironment.buffedElement == envCard.element)
            {
                activeEnvironment.UpgradeLevel();
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowEnvironmentBanner(activeEnvironment, player.playerName);
                    BattleUIManager.Instance.LogCombatMessage($"⚡ [{activeEnvironment.name}] escalated to Level {activeEnvironment.level} ({activeEnvironment.turnsRemaining} turns remaining)!");
                }
            }
            else
            {
                activeEnvironment = new EnvironmentCardData(envCard.element, 1);
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowEnvironmentBanner(activeEnvironment, player.playerName);
                    BattleUIManager.Instance.LogCombatMessage($"🌍 [{player.playerName}] shifted battlefield weather to [{activeEnvironment.name}] (Level 1)! Buffs {activeEnvironment.buffedElement} by +30%.");
                }
            }

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.UpdateEnvironmentVisuals();
                BattleUIManager.Instance.UpdateUI();
            }

            return true;
        }

        #endregion

        public void OnPlayerExecuteAttackButton()
        {
            if (!isPlayerTurn || currentPhase != GamePhase.ActionPhase) return;

            bool hasReadyAttackers = false;
            foreach (var slot in humanPlayer.slots)
            {
                if (slot != null && slot.IsOccupied && slot.currentMonster != null && slot.currentMonster.canAttack && slot.currentMonster.frozenTurns <= 0)
                {
                    if (slot.currentMonster.GetEffectiveAtk(activeEnvironment) > 0)
                    {
                        hasReadyAttackers = true;
                        break;
                    }
                }
            }

            if (!hasReadyAttackers)
            {
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.LogCombatMessage("⚠️ No monsters on the board are ready to attack right now!");
                    BattleUIManager.Instance.UpdateUI();
                }
                return;
            }

            currentPhase = GamePhase.CombatPhase;

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.ShowBanner("⚔️ COMBAT ASSAULT PHASE!", new Color(1f, 0.85f, 0.2f));
            }

            StartCoroutine(CombatSystem.Instance.ExecuteCombatPhaseAnimated(humanPlayer, aiPlayer, activeEnvironment, () =>
            {
                CheckGameOver();
                if (currentPhase != GamePhase.GameOver)
                {
                    EndTurn();
                }
            }));
        }

        public void OnPlayerEndTurn()
        {
            if (!isPlayerTurn || currentPhase != GamePhase.ActionPhase) return;

            bool hasReadyAttackers = false;
            foreach (var slot in humanPlayer.slots)
            {
                if (slot != null && slot.IsOccupied && slot.currentMonster != null && slot.currentMonster.canAttack && slot.currentMonster.frozenTurns <= 0)
                {
                    if (slot.currentMonster.GetEffectiveAtk(activeEnvironment) > 0)
                    {
                        hasReadyAttackers = true;
                        break;
                    }
                }
            }

            if (hasReadyAttackers)
            {
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.LogCombatMessage("⚔️ Automatically executing ready monster attacks before ending turn...");
                    BattleUIManager.Instance.ShowBanner("⚔️ AUTO-COMBAT PHASE!", new Color(1f, 0.85f, 0.2f));
                }

                currentPhase = GamePhase.CombatPhase;

                StartCoroutine(CombatSystem.Instance.ExecuteCombatPhaseAnimated(humanPlayer, aiPlayer, activeEnvironment, () =>
                {
                    CheckGameOver();
                    if (currentPhase != GamePhase.GameOver)
                    {
                        EndTurn();
                    }
                }));
            }
            else
            {
                EndTurn();
            }
        }

        public void EndTurn()
        {
            currentPhase = GamePhase.TurnEnd;

            if (activeEnvironment != null)
            {
                bool expired = activeEnvironment.TickTurn();
                if (expired)
                {
                    string oldName = activeEnvironment.name;
                    activeEnvironment = null;
                    if (BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.LogCombatMessage($"☀️ [{oldName}] has cleared—battlefield returned to Stable Weather.");
                        BattleUIManager.Instance.ShowBanner("☀️ STABLE WEATHER RESTORED", new Color(0.7f, 0.85f, 1f));
                        BattleUIManager.Instance.UpdateEnvironmentVisuals();
                    }
                }
            }

            CheckGameOver();

            if (currentPhase != GamePhase.GameOver)
            {
                isPlayerTurn = !isPlayerTurn;
                currentTurn++;
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ClearBoardSelection();
                    BattleUIManager.Instance.UpdateUI();
                }
                StartTurn();
            }
        }

        public void CheckGameOver()
        {
            if (currentPhase == GamePhase.GameOver) return;

            if (!aiPlayer.IsAlive)
            {
                currentPhase = GamePhase.GameOver;
                string envName = activeEnvironment != null ? activeEnvironment.name : "Stable Weather ☀️";
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowGameOverScreen(true, currentTurn, humanPlayer.currentHp, aiPlayer.currentHp,
                        humanPlayer.monstersSummonedTotal, playerSpellsCastCount, envName, humanPlayer.manaCapacity);
                }
            }
            else if (!humanPlayer.IsAlive)
            {
                currentPhase = GamePhase.GameOver;
                string envName = activeEnvironment != null ? activeEnvironment.name : "Stable Weather ☀️";
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowGameOverScreen(false, currentTurn, humanPlayer.currentHp, aiPlayer.currentHp,
                        humanPlayer.monstersSummonedTotal, playerSpellsCastCount, envName, humanPlayer.manaCapacity);
                }
            }
        }

        public void RestartGame()
        {
            for (int i = 0; i < humanPlayer.slots.Length; i++)
            {
                if (humanPlayer.slots[i] != null) humanPlayer.slots[i].ClearSlot();
                if (aiPlayer.slots[i] != null) aiPlayer.slots[i].ClearSlot();
            }

            playerSpellsCastCount = 0;
            activeEnvironment = null;

            if (BattleUIManager.Instance != null)
            {
                if (BattleUIManager.Instance.gameOverPanel != null)
                {
                    BattleUIManager.Instance.gameOverPanel.SetActive(false);
                }
                BattleUIManager.Instance.DeselectHandCards();
                BattleUIManager.Instance.ClearBoardSelection();
            }

            StartGame();
        }
    }
}

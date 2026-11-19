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

    [Serializable]
    public class ChampionDecksWrapper
    {
        public string updated_at;
        public int training_matches_analyzed;
        public ChampionDeckEntry gora;
        public ChampionDeckEntry navi;
        public ArchetypesContainer archetypes;
    }

    [Serializable]
    public class ChampionDeckEntry
    {
        public string name;
        public string title;
        public string combat_style;
        public string description;
        public int[] card_ids;
    }

    [Serializable]
    public class ArchetypesContainer
    {
        public ChampionDeckEntry fire_aggro;
        public ChampionDeckEntry water_control;
    }

    [Serializable]
    public class CustomDeckFileWrapper
    {
        public string deck_name;
        public string description;
        public int[] card_ids;
    }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public enum OpponentModel { Gora, Navi }
        public enum PlayerDeckSelection
        {
            BalancedDraft,
            GoraChampion,
            NaviChampion,
            FireAggro,
            WaterControl,
            CustomJson
        }

        [Header("Gora & Navi Arena AI Opponents & Deck Setup")]
        public OpponentModel currentOpponent = OpponentModel.Gora;
        public PlayerDeckSelection selectedPlayerDeck = PlayerDeckSelection.BalancedDraft;
        public int matchCounter = 0;
        public readonly Dictionary<int, CardData> masterCardIndex = new Dictionary<int, CardData>();
        private ChampionDecksWrapper cachedChampionDecks = null;

        [Header("Players")]
        public PlayerState humanPlayer;
        public PlayerState aiPlayer;
        public AIOpponent aiOpponent;

        [Header("State")]
        [NonSerialized] public bool isMatchStarted = false;
        public int currentTurn = 1;
        public bool isPlayerTurn = true;
        public GamePhase currentPhase = GamePhase.StartingCoinToss;
        public EnvironmentCardData activeEnvironment = null;
        public CardData activeEnvironmentCard = null;
        public string activeEnvironmentOwner = "";
        public List<CardData> allLeaders = new List<CardData>();

        [Header("Match Statistics")]
        public int playerSpellsCastCount = 0;

        [Header("Deck & Pity Draw Rules")]
        public int defaultDeckSize = 50;
        public int firstPlayerOpeningHand = 5;
        public int secondPlayerOpeningHand = 6;
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
        private Coroutine autoEndTurnCoroutine = null;

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
            // Match does NOT start automatically on launch; waits for player to click START GAME on the Main Menu.
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
                    allLeaders = new List<CardData>();
                    var allSpells = new List<CardData>();

                    foreach (var c in wrapper.cards)
                    {
                        if (c == null) continue;
                        if (c.cardType == CardType.Monster || c.card_type == "monster")
                        {
                            var m = new MonsterData
                            {
                                id = c.id,
                                name = string.IsNullOrEmpty(c.name) ? c.id.ToString() : c.name,
                                cardType = CardType.Monster,
                                card_type = "monster",
                                element = c.element,
                        tribe = c.tribe,
                                group = c.group,
                                group_en = c.group_en,
                                role = c.role,
                                tier = c.tier_title,
                                tier_title = c.tier_title,
                                tier_num = c.tier_num,
                                cost = c.cost,
                                powerCost = c.powerCost,
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
                            if (string.IsNullOrEmpty(c.name)) c.name = c.id.ToString();
                            SpellDatabase.NormalizeSpellMetadata(c);
                            allSpells.Add(c);
                        }
                        else if (c.cardType == CardType.Leader || string.Equals(c.card_type, "leader", StringComparison.OrdinalIgnoreCase))
                        {
                            c.cardType = CardType.Leader;
                            if (string.IsNullOrEmpty(c.name)) c.name = c.id.ToString();
                            allLeaders.Add(c);
                        }
                    }

                    SpellDatabase.SetSpellsFromDatabase(allSpells);
                    Debug.Log($"✅ Loaded Unified Balanced Database: {allMonsters.Count} Monsters + {allSpells.Count} Tactical Spells + {allLeaders.Count} Commander Spirits!");

                    masterCardIndex.Clear();
                    foreach (var m in allMonsters) if (m != null) masterCardIndex[m.id] = m;
                    foreach (var s in allSpells) if (s != null) masterCardIndex[s.id] = s;
                    foreach (var l in allLeaders) if (l != null) masterCardIndex[l.id] = l;
                    string[] elems = { "Fire", "Water", "Ice", "Earth", "Electric", "Air", "Nature", "Light", "Dark", "Desert" };
                    for (int i = 0; i < elems.Length; i++)
                    {
                        var env = new EnvironmentCardData(elems[i], 1);
                        masterCardIndex[env.id] = env;
                    }
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
                        if (m != null)
                        {
                            m.name = m.id.ToString();
                            masterCardIndex[m.id] = m;
                        }
                    }
                }
                Debug.Log($"✅ Loaded {allMonsters.Count} Monsters from fallback database!");
            }
        }

        #region Champion Decks & Dynamic Deck Construction
        public CardData CloneCardById(int id)
        {
            if (masterCardIndex.TryGetValue(id, out var proto))
            {
                if (proto is MonsterData md) return CloneMonsterCard(md);
                if (proto is EnvironmentCardData ed) return new EnvironmentCardData(ed.element, 1);
                if (proto.cardType == CardType.Spell) return CloneSpellCard(proto);
                if (proto.cardType == CardType.Leader) return CloneLeaderCard(proto);
                if (proto.cardType == CardType.Environment) return new EnvironmentCardData(proto.element, 1);
            }
            if (id >= 441 && id <= 450)
            {
                string[] elems = { "Fire", "Water", "Ice", "Earth", "Electric", "Air", "Nature", "Light", "Dark", "Desert" };
                int idx = id - 441;
                if (idx >= 0 && idx < elems.Length)
                {
                    return new EnvironmentCardData(elems[idx], 1);
                }
            }
            return null;
        }

        public List<CardData> BuildDeckFromIds(IEnumerable<int> ids)
        {
            var deck = new List<CardData>();
            if (ids == null) return deck;
            foreach (int id in ids)
            {
                var card = CloneCardById(id);
                if (card != null)
                {
                    deck.Add(card);
                }
            }
            // Shuffle
            for (int i = 0; i < deck.Count; i++)
            {
                int r = UnityEngine.Random.Range(i, deck.Count);
                var temp = deck[i];
                deck[i] = deck[r];
                deck[r] = temp;
            }
            return deck;
        }

        public ChampionDecksWrapper LoadChampionDecksDatabase()
        {
            if (cachedChampionDecks != null) return cachedChampionDecks;

            string jsonText = null;
            try
            {
                string externalPath = System.IO.Path.Combine(Application.dataPath, "../champion_decks.json");
                if (System.IO.File.Exists(externalPath))
                {
                    jsonText = System.IO.File.ReadAllText(externalPath, System.Text.Encoding.UTF8);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not load external champion_decks.json: {e.Message}");
            }

            if (string.IsNullOrEmpty(jsonText))
            {
                var resAsset = Resources.Load<TextAsset>("champion_decks");
                if (resAsset != null) jsonText = resAsset.text;
            }

            if (!string.IsNullOrEmpty(jsonText))
            {
                try
                {
                    cachedChampionDecks = JsonUtility.FromJson<ChampionDecksWrapper>(jsonText);
                    Debug.Log("✅ Loaded Champion Decks Database successfully!");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error parsing champion_decks.json: {e.Message}");
                }
            }
            return cachedChampionDecks;
        }

        public List<int> GetGoraCardIds()
        {
            var c = LoadChampionDecksDatabase();
            if (c != null && c.gora != null && c.gora.card_ids != null && c.gora.card_ids.Length > 0)
            {
                return new List<int>(c.gora.card_ids);
            }
            return null;
        }

        public List<int> GetNaviCardIds()
        {
            var c = LoadChampionDecksDatabase();
            if (c != null && c.navi != null && c.navi.card_ids != null && c.navi.card_ids.Length > 0)
            {
                return new List<int>(c.navi.card_ids);
            }
            return null;
        }

        public List<int> GetArchetypeCardIds(string key)
        {
            var c = LoadChampionDecksDatabase();
            if (c != null && c.archetypes != null)
            {
                if (key == "fire_aggro" && c.archetypes.fire_aggro != null && c.archetypes.fire_aggro.card_ids != null)
                    return new List<int>(c.archetypes.fire_aggro.card_ids);
                if (key == "water_control" && c.archetypes.water_control != null && c.archetypes.water_control.card_ids != null)
                    return new List<int>(c.archetypes.water_control.card_ids);
            }
            return null;
        }

        public List<int> inMemoryCustomDeckIds = new List<int>();

        public void SaveCustomDeck(List<int> ids, string deckName = "Player Custom Deck")
        {
            if (ids == null) ids = new List<int>();
            inMemoryCustomDeckIds = new List<int>(ids);

            // 1. Save to PlayerPrefs
            string joined = string.Join(",", inMemoryCustomDeckIds);
            PlayerPrefs.SetString("CustomDeck_CardIds", joined);
            PlayerPrefs.Save();

            // 2. Also save to custom_deck.json next to executable
            try
            {
                var wrap = new CustomDeckFileWrapper
                {
                    deck_name = deckName,
                    description = "Custom deck created in-game via Custom Deck Builder.",
                    card_ids = inMemoryCustomDeckIds.ToArray()
                };
                string json = JsonUtility.ToJson(wrap, true);
                string customPath = System.IO.Path.Combine(Application.dataPath, "../custom_deck.json");
                System.IO.File.WriteAllText(customPath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not write custom_deck.json: {ex.Message}");
            }
        }

        public List<int> GetCustomDeckCardIds()
        {
            if (inMemoryCustomDeckIds != null && inMemoryCustomDeckIds.Count > 0)
            {
                return new List<int>(inMemoryCustomDeckIds);
            }

            // Check PlayerPrefs first
            string saved = PlayerPrefs.GetString("CustomDeck_CardIds", "");
            if (!string.IsNullOrEmpty(saved))
            {
                var parts = saved.Split(',');
                var parsedList = new List<int>();
                foreach (var p in parts)
                {
                    if (int.TryParse(p.Trim(), out int cid) && cid > 0)
                        parsedList.Add(cid);
                }
                if (parsedList.Count > 0)
                {
                    inMemoryCustomDeckIds = new List<int>(parsedList);
                    return new List<int>(inMemoryCustomDeckIds);
                }
            }

            try
            {
                string customPath = System.IO.Path.Combine(Application.dataPath, "../custom_deck.json");
                if (System.IO.File.Exists(customPath))
                {
                    string txt = System.IO.File.ReadAllText(customPath, System.Text.Encoding.UTF8);
                    var parsed = JsonUtility.FromJson<CustomDeckFileWrapper>(txt);
                    if (parsed != null && parsed.card_ids != null && parsed.card_ids.Length > 0)
                    {
                        inMemoryCustomDeckIds = new List<int>(parsed.card_ids);
                        return new List<int>(inMemoryCustomDeckIds);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not load custom_deck.json: {e.Message}");
            }
            return GetGoraCardIds();
        }

        public List<CardData> BuildPlayerSelectedDeck()
        {
            List<int> ids = null;
            switch (selectedPlayerDeck)
            {
                case PlayerDeckSelection.GoraChampion: ids = GetGoraCardIds(); break;
                case PlayerDeckSelection.NaviChampion: ids = GetNaviCardIds(); break;
                case PlayerDeckSelection.FireAggro: ids = GetArchetypeCardIds("fire_aggro"); break;
                case PlayerDeckSelection.WaterControl: ids = GetArchetypeCardIds("water_control"); break;
                case PlayerDeckSelection.CustomJson: ids = GetCustomDeckCardIds(); break;
                case PlayerDeckSelection.BalancedDraft:
                default: return GenerateDeck(defaultDeckSize);
            }

            if (ids != null && ids.Count > 0)
            {
                var deck = BuildDeckFromIds(ids);
                if (deck.Count >= defaultDeckSize) return deck;
                var topUp = GenerateDeck(defaultDeckSize - deck.Count);
                deck.AddRange(topUp);
                return deck;
            }
            return GenerateDeck(defaultDeckSize);
        }

        public List<CardData> BuildAIOpponentDeck()
        {
            List<int> ids = (currentOpponent == OpponentModel.Gora) ? GetGoraCardIds() : GetNaviCardIds();
            if (ids != null && ids.Count > 0)
            {
                var deck = BuildDeckFromIds(ids);
                if (deck.Count >= defaultDeckSize) return deck;
                var topUp = GenerateDeck(defaultDeckSize - deck.Count);
                deck.AddRange(topUp);
                return deck;
            }
            return GenerateDeck(defaultDeckSize);
        }

        public void AdvanceToNextOpponent()
        {
            currentOpponent = (currentOpponent == OpponentModel.Gora) ? OpponentModel.Navi : OpponentModel.Gora;
            Debug.Log($"🔄 Next Opponent switched to: {currentOpponent}");
        }

        public void SetOpponent(OpponentModel opp)
        {
            currentOpponent = opp;
            Debug.Log($"🎯 Opponent manually set to: {currentOpponent}");
        }

        public void SetPlayerDeck(PlayerDeckSelection sel)
        {
            selectedPlayerDeck = sel;
            Debug.Log($"🎴 Player deck selection set to: {selectedPlayerDeck}");
        }

        public string GetSelectedDeckDisplayName()
        {
            switch (selectedPlayerDeck)
            {
                case PlayerDeckSelection.GoraChampion: return "🏆 Gora Core Settled Deck (50 Cards)";
                case PlayerDeckSelection.NaviChampion: return "🟣 Navi Tactical Settled Deck (50 Cards)";
                case PlayerDeckSelection.FireAggro: return "🔥 Fire Aggro Assault Deck (50 Cards)";
                case PlayerDeckSelection.WaterControl: return "💧 Water & Ice Control Deck (50 Cards)";
                case PlayerDeckSelection.CustomJson: return $"🛠️ Player Custom Deck ({GetCustomDeckCardIds().Count} Cards)";
                case PlayerDeckSelection.BalancedDraft:
                default: return "🎲 Balanced Draft Deck (50 Cards)";
            }
        }

        public string GetSelectedDeckArabicName()
        {
            return GetSelectedDeckDisplayName();
        }
        #endregion

        public void StartGame()
        {
            isMatchStarted = true;
            matchCounter++;

            LoadChampionDecksDatabase();

            var deckA = BuildPlayerSelectedDeck();
            var deckB = BuildAIOpponentDeck();

            if (currentOpponent == OpponentModel.Gora)
            {
                aiPlayer.playerName = "Gora (Aggressive Combat AI)";
                if (aiOpponent != null) aiOpponent.combatStyle = AICombatStyle.GoraAggressive;
            }
            else
            {
                aiPlayer.playerName = "Navi (Tactical Control AI)";
                if (aiOpponent != null) aiOpponent.combatStyle = AICombatStyle.NaviTactical;
            }

            humanPlayer.InitializeDeck(deckA);
            aiPlayer.InitializeDeck(deckB);
            humanPlayer.hand.Clear();
            aiPlayer.hand.Clear();

            humanPlayer.onDeckExhaustedRefill = RefillPlayerDeck;
            humanPlayer.getCardDrawWeightMultiplier = GetCardPityWeight;
            humanPlayer.onCardDrawnCallback = RecordCardDrawnForPity;
            humanPlayer.onCardDrawnToHand = (p, c) => BattleUIManager.Instance?.NotifyCardDrawn(true, c);
            humanPlayer.onOldestCardBurned = (p, burned, incoming) => BattleUIManager.Instance?.NotifyOldestCardBurned(true, burned, incoming);

            aiPlayer.onDeckExhaustedRefill = RefillPlayerDeck;
            aiPlayer.getCardDrawWeightMultiplier = GetCardPityWeight;
            aiPlayer.onCardDrawnCallback = null;
            aiPlayer.onCardDrawnToHand = (p, c) => BattleUIManager.Instance?.NotifyCardDrawn(false, c);
            aiPlayer.onOldestCardBurned = (p, burned, incoming) => BattleUIManager.Instance?.NotifyOldestCardBurned(false, burned, incoming);

            BattleUIManager.Instance?.ClearFlightAnimations();

            // Player 1 begins with 5 cards (skips Turn 1 draw -> 5);
            // Player 2 begins with 6 cards (skips Turn 2 draw -> 6)
            int p1Draws = Mathf.Max(0, firstPlayerOpeningHand);
            int p2Draws = Mathf.Max(0, secondPlayerOpeningHand);

            for (int i = 0; i < p1Draws; i++)
            {
                humanPlayer.DrawCard();
            }
            for (int i = 0; i < p2Draws; i++)
            {
                aiPlayer.DrawCard();
            }

            isPlayerTurn = true;
            currentTurn = 1;
            activeEnvironment = null;

            // Pre-slot test Commander Spirits for visual verification in Right Wing Altar
            if (allLeaders != null && allLeaders.Count > 0)
            {
                humanPlayer.activeLeader = allLeaders.Find(l => l.id == 451) ?? allLeaders[0];
                aiPlayer.activeLeader = allLeaders.Find(l => l.id == 452) ?? (allLeaders.Count > 1 ? allLeaders[1] : allLeaders[0]);
            }

            // V1 Fair Starting Rule: Both players begin at 0 Power and 0 Mana (Player 2 receives +1 temp Mana once on Turn 2)
            humanPlayer.power = 0;
            humanPlayer.currentMana = 0;
            aiPlayer.power = 0;
            aiPlayer.currentMana = 0;

            Debug.Log($"🎲 Epic Battle Started! Decks: {defaultDeckSize} unique cards each | Opening Hands: P1={p1Draws}, P2={p2Draws} | Start Resources: 0 Power / 0 Mana (P2 +1 Mana on Turn 2).");
            MonsterCardGame.Audio.SoundManager.Instance?.PlayMatchStart();

            StartTurn();

            if (BattleUIManager.Instance != null)
            {
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

            var playableLeaders = (allLeaders != null)
                ? allLeaders.FindAll(l => l != null && MonsterArtLoader.HasCustomCardArt(l))
                : new List<CardData>();

            int leaderTarget = (playableLeaders.Count > 0 && count >= 10)
                ? Mathf.Min(playableLeaders.Count, count >= 40 ? 3 : (count >= 25 ? 2 : 1))
                : 0;
            int monsterTarget = Mathf.Max(0, Mathf.RoundToInt(count * 0.60f) - leaderTarget); // 27 Monsters + 3 Spirits out of 50
            int spellTarget = Mathf.RoundToInt(count * 0.32f);   // 16 Spells out of 50
            int envTarget = Mathf.Max(0, count - monsterTarget - spellTarget - leaderTarget); // 4 Environments out of 50

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

            // 3. Unified Single Environment Cards (multiple copies of the deck's dominant element so each played card raises slot level 1->2->3 or counters enemy level)
            if (envTarget > 0)
            {
                var elemCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in deck)
                {
                    if (c != null && c.cardType == CardType.Monster && !string.IsNullOrEmpty(c.element))
                    {
                        elemCounts[c.element] = (elemCounts.TryGetValue(c.element, out int ec) ? ec : 0) + 1;
                    }
                }

                string dominantElem = "Water";
                int bestCount = -1;
                foreach (var kv in elemCounts)
                {
                    if (kv.Value > bestCount || (kv.Value == bestCount && UnityEngine.Random.value < 0.5f))
                    {
                        bestCount = kv.Value;
                        dominantElem = kv.Key;
                    }
                }

                for (int i = 0; i < envTarget; i++)
                {
                    deck.Add(new EnvironmentCardData(dominantElem, 1));
                }
            }

            // 4. Commander Spirit Medallions without replacement (only Spirits with artwork ready)
            if (leaderTarget > 0 && playableLeaders.Count > 0)
            {
                PickWeightedWithoutReplacement(playableLeaders, leaderTarget, usedIds, l => l.id, CloneLeaderCard, deck);
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
                tribe = src.tribe,
                group = src.group,
                group_en = src.group_en,
                role = src.role,
                tier = src.tier,
                tier_title = src.tier_title,
                tier_num = src.tier_num,
                cost = src.cost,
                powerCost = src.powerCost,
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
            var clone = new CardData
            {
                id = proto.id,
                name = proto.id.ToString(),
                cardType = CardType.Spell,
                card_type = "spell",
                element = proto.element,
                tribe = proto.tribe,
                group = proto.group,
                role = proto.role,
                tier = proto.tier_title,
                tier_title = proto.tier_title,
                tier_num = proto.tier_num,
                cost = proto.cost,
                powerCost = proto.powerCost,
                spellType = proto.spellType,
                spellTargetMode = proto.spellTargetMode,
                spellPower = proto.spellPower,
                active_skill = proto.active_skill,
                description = proto.description
            };
            SpellDatabase.NormalizeSpellMetadata(clone);
            return clone;
        }

        private static CardData CloneLeaderCard(CardData src)
        {
            return new CardData
            {
                id = src.id,
                name = string.IsNullOrEmpty(src.name) ? src.id.ToString() : src.name,
                cardType = CardType.Leader,
                card_type = "leader",
                element = src.element,
                tribe = src.tribe,
                group = src.group,
                group_en = src.group_en,
                role = src.role,
                tier = src.tier_title,
                tier_title = src.tier_title,
                tier_num = src.tier_num,
                cost = src.cost,
                powerCost = src.powerCost,
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
            if (allLeaders != null)
            {
                foreach (var l in allLeaders)
                {
                    if (l != null && MonsterArtLoader.HasCustomCardArt(l) && seen.Add(l.id)) allCatalogIds.Add(l.id);
                }
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

            // Fair Starting Rule: Both players skip their first-turn draw (P1 starts with 5, P2 starts with 6); P2 gets +1 temporary Mana on Turn 2 only
            bool skipTurnDraw = (currentTurn == 1 && isPlayerTurn) || (currentTurn == 2 && !isPlayerTurn);
            activePlayer.StartTurn(currentTurn, skipTurnDraw);
            if (currentTurn == 2 && !isPlayerTurn)
            {
                activePlayer.currentMana += 1;
            }

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
                if (currentTurn > 1)
                {
                    MonsterCardGame.Audio.SoundManager.Instance?.PlayTurnStart(true);
                }
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowBanner($"🟢 YOUR TURN (Round {currentTurn} • Mana: {activePlayer.currentMana} | Power: {activePlayer.power} ∞)", new Color(0.2f, 0.95f, 0.5f));
                }
                EvaluateAutoEndTurn();
            }
            else
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayTurnStart(false);
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowBanner("🔴 ENEMY TURN (Thinking...)", new Color(1f, 0.35f, 0.35f));
                }
                aiOpponent.PlayTurn(currentTurn, activeEnvironment, EndTurn);
            }
        }

        #region Spell & Environment Mechanisms (5 Intermediary Roles + Classic)

        public List<BoardSlot> GetValidSpellEnemies(PlayerState opponent)
        {
            var list = new List<BoardSlot>();
            if (opponent == null || opponent.slots == null) return list;
            foreach (var s in opponent.slots)
            {
                if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.HasPassiveSubstr("Night Stealth") == null)
                {
                    list.Add(s);
                }
            }
            return list;
        }

        private static bool HasEmptySlot(PlayerState p)
        {
            if (p == null || p.slots == null) return false;
            foreach (var s in p.slots)
            {
                if (s != null && !s.IsOccupied) return true;
            }
            return false;
        }

        private static bool HasCatalogMonsterInGraveyard(PlayerState p)
        {
            if (p == null || p.graveyard == null) return false;
            foreach (var g in p.graveyard)
            {
                if (g != null && g.cardType == CardType.Monster && g.id < 9000) return true;
            }
            return false;
        }

        public bool IsSpellPlayable(PlayerState caster, PlayerState target, CardData spellCard, out string reason)
        {
            reason = string.Empty;
            if (caster == null || target == null || spellCard == null || spellCard.cardType != CardType.Spell)
            {
                reason = "Invalid spell card.";
                return false;
            }
            if (spellCard.cost > caster.currentMana)
            {
                reason = $"Not enough Mana for [{spellCard.name}]! (Req: {spellCard.cost}💧, Avail: {caster.currentMana}💧)";
                return false;
            }

            SpellDatabase.NormalizeSpellMetadata(spellCard);
            SpellTargetMode tmode = SpellDatabase.GetTargetMode(spellCard.id);
            if (spellCard.id < 241 || spellCard.id > 440)
                tmode = spellCard.spellTargetMode;

            var allies = GetAliveSlots(caster);
            var validEnemies = GetValidSpellEnemies(target);
            bool hasEmpty = HasEmptySlot(caster);

            if (tmode == SpellTargetMode.Enemy && validEnemies.Count == 0)
            {
                reason = $"No targetable enemy monsters for [{spellCard.name}] (Night Stealth blocks targeting).";
                return false;
            }
            if (tmode == SpellTargetMode.Ally && allies.Count == 0)
            {
                reason = $"No allied monsters on the board to target with [{spellCard.name}].";
                return false;
            }
            if (tmode == SpellTargetMode.Any && allies.Count == 0 && validEnemies.Count == 0)
            {
                reason = $"No targetable monsters on the board for [{spellCard.name}].";
                return false;
            }
            if (tmode == SpellTargetMode.AllyToEmptySlot && (allies.Count == 0 || !hasEmpty))
            {
                reason = $"[{spellCard.name}] requires at least 1 allied monster and 1 empty allied slot.";
                return false;
            }
            if (spellCard.id == 347)
            {
                if (!hasEmpty || allies.Count == 0)
                {
                    reason = $"[{spellCard.name}] requires an allied monster and an empty allied slot.";
                    return false;
                }
            }
            else if (tmode == SpellTargetMode.EmptySlot && (!hasEmpty || !HasCatalogMonsterInGraveyard(caster)))
            {
                reason = $"[{spellCard.name}] requires an empty allied slot and a fallen Monster in your graveyard.";
                return false;
            }
            if (spellCard.id == 295 && (allies.Count == 0 || !hasEmpty))
            {
                reason = $"[{spellCard.name}] requires an allied monster to clone and an empty allied slot.";
                return false;
            }
            if (spellCard.id == 288 && caster.currentMana <= spellCard.cost)
            {
                reason = $"[{spellCard.name}] requires extra Mana beyond its cast cost.";
                return false;
            }

            return true;
        }

        public bool CastSpell(PlayerState caster, PlayerState target, CardData spellCard)
        {
            return CastSpell(caster, target, spellCard, -1, -1, false);
        }

        public bool CastSpell(PlayerState caster, PlayerState target, CardData spellCard, int primaryTargetSlot, int secondaryTargetSlot = -1, bool primaryTargetIsAlly = false)
        {
            if (spellCard == null || spellCard.cardType != CardType.Spell) return false;

            if (!IsSpellPlayable(caster, target, spellCard, out string reason))
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                if (BattleUIManager.Instance != null && !string.IsNullOrEmpty(reason))
                {
                    BattleUIManager.Instance.LogCombatMessage($"❌ {reason}");
                }
                return false;
            }

            caster.currentMana -= spellCard.cost;
            caster.hand.Remove(spellCard);
            if (spellCard.id < 9000)
            {
                caster.graveyard.Add(spellCard);
            }

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
                BattleUIManager.Instance.LogCombatMessage($"🔮 [{caster.playerName}] cast Spell #{spellCard.name} ({spellCard.cost}💧): {spellCard.description}", spellCard);
            }

            int valBonus = 0;
            if (activeEnvironment != null && EnvironmentCardData.AreSameElement(activeEnvironment.buffedElement, "Arcane"))
            {
                int lvlI = Mathf.Clamp(activeEnvironment.level - 1, 0, 2);
                valBonus += (lvlI == 0 ? 5 : (lvlI == 1 ? 8 : 13));
                int arcAtk = (lvlI == 0 ? 3 : (lvlI == 1 ? 4 : 6));
                foreach (var s in caster.slots)
                {
                    if (s != null && s.IsOccupied && s.currentMonster != null &&
                        s.currentMonster.data != null &&
                        EnvironmentCardData.AreSameElement(s.currentMonster.data.element, "Arcane"))
                    {
                        s.currentMonster.bonusAtk += arcAtk;
                    }
                }
            }

            int repeats = 1;
            if (caster.nextSpellRepeats > 0)
            {
                repeats += caster.nextSpellRepeats;
                caster.nextSpellRepeats = 0;
            }
            var enemiesNow = GetAliveSlots(target);
            foreach (var s in caster.slots)
            {
                if (s != null && s.IsOccupied && s.currentMonster != null)
                {
                    var bm = s.currentMonster;
                    if (bm.HasPassiveSubstr("Arcane Echo") != null) bm.bonusAtk += 3;
                    if (bm.HasPassiveSubstr("Rune Claw") != null) bm.bonusAtk += 2;
                    if (bm.HasPassiveSubstr("Runic Carapace") != null)
                    {
                        bm.bonusAtk += 1;
                        bm.AddBonusDef(2);
                    }
                    if (bm.HasPassiveSubstr("Arcane Overflow") != null) valBonus += 8;
                    if (bm.HasPassiveSubstr("Scroll Conduit") != null) valBonus += 3;
                    if (bm.HasPassiveSubstr("Luminous Tome") != null)
                    {
                        caster.HealLeader(4, target);
                        valBonus += 3;
                    }
                    if (bm.HasPassiveSubstr("Stormcaller Halo") != null && enemiesNow.Count > 0)
                    {
                        var pk = enemiesNow[UnityEngine.Random.Range(0, enemiesNow.Count)];
                        if (pk != null && pk.IsOccupied && CombatSystem.Instance != null)
                        {
                            CombatSystem.Instance.DealSpellOrSkillDamage(pk.slotIndex, pk.currentMonster, 3, bm, target, caster, false, true, true, false);
                        }
                    }
                    if (bm.HasPassiveSubstr("Spell Multiplier") != null && repeats == 1 && UnityEngine.Random.value < 0.50f)
                    {
                        repeats = 2;
                    }
                    if (bm.HasPassiveSubstr("Mana Recovery") != null) caster.currentMana += 1;
                }
            }

            for (int rep = 0; rep < repeats; rep++)
            {
                int legacyVal = Mathf.Max(10, spellCard.spellPower + valBonus);
                ExecuteSingleSpellEffect(caster, target, spellCard, valBonus, legacyVal, primaryTargetSlot, secondaryTargetSlot, primaryTargetIsAlly);
            }

            CommanderSpiritEffects.OnSpellCast(caster, target);
            CheckGameOver();
            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.UpdateUI();
            }
            EvaluateAutoEndTurn();
            return true;
        }

        private List<BoardSlot> GetAliveSlots(PlayerState p)
        {
            var list = new List<BoardSlot>();
            if (p == null || p.slots == null) return list;
            foreach (var s in p.slots)
            {
                if (s != null && s.IsOccupied) list.Add(s);
            }
            return list;
        }

        public int PickSpellEnemySlot(PlayerState caster, PlayerState target, int spellId, int valBonus = 0)
        {
            var valid = GetValidSpellEnemies(target);
            if (valid.Count == 0) return -1;
            if (valid.Count == 1) return valid[0].slotIndex;

            if (spellId == 255)
            {
                BoardSlot bestKill = null;
                int bestKillScore = int.MinValue;
                foreach (var e in valid)
                {
                    if (e.currentMonster.currentDef <= 15)
                    {
                        int sc = e.currentMonster.baseAtk + e.currentMonster.bonusAtk + (e.currentMonster.data != null ? e.currentMonster.data.cost : 1) * 3;
                        if (sc > bestKillScore) { bestKillScore = sc; bestKill = e; }
                    }
                }
                if (bestKill != null) return bestKill.slotIndex;
            }
            else if (spellId == 259)
            {
                BoardSlot bestDamaged = null;
                int bestDmgScore = int.MinValue;
                foreach (var e in valid)
                {
                    if (e.currentMonster.currentDef < e.currentMonster.baseDef)
                    {
                        int sc = e.currentMonster.baseAtk + e.currentMonster.bonusAtk + (e.currentMonster.data != null ? e.currentMonster.data.cost : 1) * 2;
                        if (sc > bestDmgScore) { bestDmgScore = sc; bestDamaged = e; }
                    }
                }
                if (bestDamaged != null) return bestDamaged.slotIndex;
            }
            else if (spellId == 257)
            {
                BoardSlot bestChain = null;
                int bestChainScore = int.MinValue;
                foreach (var e in valid)
                {
                    int sc = (e.slotIndex > 0 && e.slotIndex < 5 ? 1 : 0) + e.currentMonster.baseAtk + e.currentMonster.bonusAtk + (e.currentMonster.data != null ? e.currentMonster.data.cost : 1) * 2;
                    if (sc > bestChainScore) { bestChainScore = sc; bestChain = e; }
                }
                if (bestChain != null) return bestChain.slotIndex;
            }
            else if (spellId == 265)
            {
                BoardSlot bestBuffed = null;
                int bestBuffScore = int.MinValue;
                foreach (var e in valid)
                {
                    int sc = Mathf.Max(0, e.currentMonster.bonusAtk) * 3 + Mathf.Max(0, e.currentMonster.tempTurnAtk) * 2 + e.currentMonster.baseAtk;
                    if (sc > bestBuffScore) { bestBuffScore = sc; bestBuffed = e; }
                }
                if (bestBuffed != null) return bestBuffed.slotIndex;
            }
            else if (spellId == 290)
            {
                BoardSlot bestSilence = null;
                int bestSilenceScore = int.MinValue;
                foreach (var e in valid)
                {
                    int pCnt = e.currentMonster.passives != null ? e.currentMonster.passives.Count : 0;
                    int aCnt = e.currentMonster.actives != null ? e.currentMonster.actives.Count : 0;
                    int sc = (e.currentMonster.bubbleShield ? 15 : 0) + pCnt * 8 + aCnt * 10 + e.currentMonster.baseAtk;
                    if (sc > bestSilenceScore) { bestSilenceScore = sc; bestSilence = e; }
                }
                if (bestSilence != null) return bestSilence.slotIndex;
            }

            BoardSlot best = valid[0];
            int bestScore = int.MinValue;
            foreach (var e in valid)
            {
                bool blockedOpp = caster != null && caster.slots != null && e.slotIndex < caster.slots.Length && caster.slots[e.slotIndex] != null && caster.slots[e.slotIndex].IsOccupied;
                int cost = e.currentMonster.data != null ? e.currentMonster.data.cost : 1;
                int sc = e.currentMonster.baseAtk + e.currentMonster.bonusAtk + e.currentMonster.tempTurnAtk + cost * 2 + (blockedOpp ? 6 : 0);
                if (sc > bestScore) { bestScore = sc; best = e; }
            }
            return best.slotIndex;
        }

        public int PickSpellAllySlot(PlayerState caster, PlayerState target, int spellId)
        {
            var allies = GetAliveSlots(caster);
            if (allies.Count == 0) return -1;
            if (allies.Count == 1) return allies[0].slotIndex;

            if (spellId == 245)
            {
                BoardSlot cheapest = allies[0];
                int minVal = int.MaxValue;
                foreach (var a in allies)
                {
                    int c = a.currentMonster.data != null ? a.currentMonster.data.cost : 1;
                    int v = c * 5 + a.currentMonster.baseAtk + a.currentMonster.currentDef;
                    if (v < minVal) { minVal = v; cheapest = a; }
                }
                return cheapest.slotIndex;
            }
            if (spellId == 271)
            {
                BoardSlot bestDeb = null;
                int bestDebScore = int.MinValue;
                foreach (var a in allies)
                {
                    var m = a.currentMonster;
                    if (m.burnDmg > 0 || m.poisonDmg > 0 || m.frozenTurns > 0 || m.weakenedTurns > 0 || m.silencedTurns > 0)
                    {
                        int sc = m.baseAtk + m.bonusAtk + m.currentDef;
                        if (sc > bestDebScore) { bestDebScore = sc; bestDeb = a; }
                    }
                }
                if (bestDeb != null) return bestDeb.slotIndex;
                BoardSlot mostWounded = allies[0];
                int minDiff = int.MaxValue;
                foreach (var a in allies)
                {
                    int diff = a.currentMonster.currentDef - a.currentMonster.baseDef;
                    if (diff < minDiff) { minDiff = diff; mostWounded = a; }
                }
                return mostWounded.slotIndex;
            }
            if (spellId == 272)
            {
                BoardSlot bestFrz = null;
                int bestFrzScore = int.MinValue;
                foreach (var a in allies)
                {
                    var m = a.currentMonster;
                    if (m.frozenTurns > 0 || !m.canAttack)
                    {
                        int sc = m.baseAtk + m.bonusAtk + m.tempTurnAtk;
                        if (sc > bestFrzScore) { bestFrzScore = sc; bestFrz = a; }
                    }
                }
                if (bestFrz != null) return bestFrz.slotIndex;
            }
            if (spellId == 270 || spellId == 279)
            {
                BoardSlot bestUnshielded = null;
                int bestSc = int.MinValue;
                foreach (var a in allies)
                {
                    if (!a.currentMonster.bubbleShield)
                    {
                        int sc = a.currentMonster.baseAtk + a.currentMonster.bonusAtk + (a.currentMonster.data != null ? a.currentMonster.data.cost : 1) * 3;
                        if (sc > bestSc) { bestSc = sc; bestUnshielded = a; }
                    }
                }
                if (bestUnshielded != null) return bestUnshielded.slotIndex;
            }
            if (spellId == 283)
            {
                BoardSlot bestSiphon = allies[0];
                int bestKey = int.MinValue;
                foreach (var a in allies)
                {
                    bool open = target == null || target.slots == null || target.slots[a.slotIndex] == null || !target.slots[a.slotIndex].IsOccupied;
                    int sc = (open ? 10000 : 0) + a.currentMonster.currentDef;
                    if (sc > bestKey) { bestKey = sc; bestSiphon = a; }
                }
                return bestSiphon.slotIndex;
            }
            if (spellId == 284 || spellId == 285)
            {
                BoardSlot bestSkilled = null;
                int bestSkScore = int.MinValue;
                foreach (var a in allies)
                {
                    var m = a.currentMonster;
                    if (m.silencedTurns <= 0 && m.actives != null && m.actives.Count > 0)
                    {
                        int sc = m.actives.Count * 20 + (m.data != null ? m.data.cost : 1) * 3;
                        if (sc > bestSkScore) { bestSkScore = sc; bestSkilled = a; }
                    }
                }
                if (bestSkilled != null) return bestSkilled.slotIndex;
            }
            if (spellId == 287)
            {
                BoardSlot weakest = allies[0];
                int minAtkKey = int.MaxValue;
                foreach (var a in allies)
                {
                    int key = (a.currentMonster.canAttack ? 0 : 10000) + a.currentMonster.baseAtk + a.currentMonster.bonusAtk;
                    if (key < minAtkKey) { minAtkKey = key; weakest = a; }
                }
                return weakest.slotIndex;
            }
            if (spellId == 293 && activeEnvironment != null)
            {
                BoardSlot bestEnv = null;
                int bestEnvAtk = int.MinValue;
                foreach (var a in allies)
                {
                    if (a.currentMonster.data != null && EnvironmentCardData.AreSameElement(a.currentMonster.data.element, activeEnvironment.buffedElement))
                    {
                        int sc = a.currentMonster.baseAtk + a.currentMonster.bonusAtk;
                        if (sc > bestEnvAtk) { bestEnvAtk = sc; bestEnv = a; }
                    }
                }
                if (bestEnv != null) return bestEnv.slotIndex;
            }
            if (spellId == 295)
            {
                BoardSlot bestClone = allies[0];
                int bestCloneSc = int.MinValue;
                foreach (var a in allies)
                {
                    int c = a.currentMonster.data != null ? a.currentMonster.data.cost : 1;
                    int sc = c * 4 + a.currentMonster.baseAtk + a.currentMonster.currentDef;
                    if (sc > bestCloneSc) { bestCloneSc = sc; bestClone = a; }
                }
                return bestClone.slotIndex;
            }
            if (spellId == 298)
            {
                BoardSlot bestTribe = allies[0];
                int bestTribeSc = int.MinValue;
                foreach (var a in allies)
                {
                    int same = 0;
                    string tr = a.currentMonster.data != null ? a.currentMonster.data.tribe : "";
                    foreach (var x in allies)
                    {
                        if (x != a && x.currentMonster.data != null && string.Equals(x.currentMonster.data.tribe, tr, StringComparison.OrdinalIgnoreCase))
                            same++;
                    }
                    int sc = same * 1000 + a.currentMonster.baseAtk + a.currentMonster.currentDef;
                    if (sc > bestTribeSc) { bestTribeSc = sc; bestTribe = a; }
                }
                return bestTribe.slotIndex;
            }
            if (spellId == 269 || spellId == 273 || spellId == 292)
            {
                BoardSlot bestAtk = allies[0];
                int bestKey = int.MinValue;
                foreach (var a in allies)
                {
                    bool open = target == null || target.slots == null || target.slots[a.slotIndex] == null || !target.slots[a.slotIndex].IsOccupied;
                    int sc = (a.currentMonster.canAttack ? 100000 : 0) + (open ? 10000 : 0) + a.currentMonster.baseAtk + a.currentMonster.bonusAtk;
                    if (sc > bestKey) { bestKey = sc; bestAtk = a; }
                }
                return bestAtk.slotIndex;
            }

            BoardSlot fallback = allies[0];
            int bestFbSc = int.MinValue;
            foreach (var a in allies)
            {
                int c = a.currentMonster.data != null ? a.currentMonster.data.cost : 1;
                int sc = a.currentMonster.baseAtk + a.currentMonster.bonusAtk + c * 2;
                if (sc > bestFbSc) { bestFbSc = sc; fallback = a; }
            }
            return fallback.slotIndex;
        }

        private BoardSlot ResolveTargetEnemySlot(PlayerState caster, PlayerState target, int spellId, int valBonus, int explicitSlot)
        {
            if (explicitSlot >= 0 && explicitSlot < 6 && target != null && target.slots[explicitSlot] != null)
            {
                var slot = target.slots[explicitSlot];
                if (slot.IsOccupied && slot.currentMonster != null && slot.currentMonster.HasPassiveSubstr("Night Stealth") == null)
                    return slot;
            }
            int picked = PickSpellEnemySlot(caster, target, spellId, valBonus);
            return (picked >= 0 && picked < 6 && target != null) ? target.slots[picked] : null;
        }

        private BoardSlot ResolveTargetAllySlot(PlayerState caster, PlayerState target, int spellId, int explicitSlot)
        {
            if (explicitSlot >= 0 && explicitSlot < 6 && caster != null && caster.slots[explicitSlot] != null && caster.slots[explicitSlot].IsOccupied)
            {
                return caster.slots[explicitSlot];
            }
            int picked = PickSpellAllySlot(caster, target, spellId);
            return (picked >= 0 && picked < 6 && caster != null) ? caster.slots[picked] : null;
        }

        private void DealSpellToMonster(BoardSlot targetSlot, int dmg, PlayerState targetPlayer, PlayerState caster, bool ignoreImmunity, bool isSingleSpell)
        {
            if (targetSlot == null || !targetSlot.IsOccupied || targetSlot.currentMonster == null) return;
            CombatSystem.Instance?.DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, dmg, null, targetPlayer, caster, ignoreImmunity, true, true, isSingleSpell);
        }

        private void ShowMonsterDamageVfx(BoardSlot targetSlot, int dmg, bool isPlayerSlot)
        {
            if (targetSlot == null || BattleUIManager.Instance == null) return;
            var slotView = BattleUIManager.Instance.GetSlotView(targetSlot.slotIndex, isPlayerSlot);
            if (slotView != null)
            {
                BattleUIManager.Instance.TriggerHitShake(slotView.transform, Color.red);
                BattleUIManager.Instance.SpawnDamageNumber(slotView.transform.position, dmg, false);
            }
        }

        private void DealLeaderSpellDamage(PlayerState target, int dmg)
        {
            if (target == null || dmg <= 0) return;
            target.TakeLeaderDamage(dmg);
            if (BattleUIManager.Instance != null)
            {
                var leaderTransform = BattleUIManager.Instance.GetLeaderTransform(target == humanPlayer);
                if (leaderTransform != null)
                {
                    BattleUIManager.Instance.TriggerHitShake(leaderTransform, Color.red);
                    BattleUIManager.Instance.SpawnDamageNumber(leaderTransform.position, dmg, true);
                }
                BattleUIManager.Instance.ShakeScreen();
            }
        }

        private static int FindFirstEmptySlot(PlayerState p)
        {
            if (p == null || p.slots == null) return -1;
            for (int i = 0; i < 6; i++)
            {
                if (p.slots[i] != null && !p.slots[i].IsOccupied) return i;
            }
            return -1;
        }

        private static int FindPreferredReviveEmptySlot(PlayerState caster, PlayerState opp)
        {
            if (caster == null || caster.slots == null) return -1;
            int[] attackSlots = new[] { 0, 1, 4, 5 };
            foreach (int i in attackSlots)
            {
                if (caster.slots[i] != null && !caster.slots[i].IsOccupied &&
                    (opp == null || opp.slots == null || opp.slots[i] == null || !opp.slots[i].IsOccupied))
                {
                    return i;
                }
            }
            return FindFirstEmptySlot(caster);
        }

        private void ExecuteSingleSpellEffect(PlayerState caster, PlayerState target, CardData spellCard, int valBonus, int legacyVal, int explicitSlot, int explicitSecondarySlot, bool explicitIsAlly)
        {
            var allies = GetAliveSlots(caster);
            var enemiesAll = GetAliveSlots(target);
            var validEnemies = GetValidSpellEnemies(target);
            int cid = spellCard.id;

            if (cid >= 241 && cid <= 440)
            {
                switch (cid)
                {
                    // --- Resource & Ramp Spells (#241 - #252) ---
                    case 241:
                    caster.currentHp = Mathf.Max(1, caster.currentHp - 10);
                    caster.power += 1;
                    break;

                case 242:
                    caster.currentMana += 2;
                    break;

                case 243:
                    caster.DrawCard();
                    caster.shield += 10 + valBonus;
                    break;

                case 244:
                    caster.DrawCard();
                    caster.DrawCard();
                    caster.shield += 5;
                    break;

                case 245:
                {
                    var al = ResolveTargetAllySlot(caster, target, 245, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        CombatSystem.Instance?.HandleMonsterDeath(caster, target, al.slotIndex, al.currentMonster, null);
                        caster.power += 1;
                        caster.currentMana += 1;
                    }
                    break;
                }

                case 246:
                    if (caster.currentHp > 30)
                    {
                        caster.currentHp -= 30;
                        caster.DrawCard();
                        caster.currentMana += 1;
                    }
                    break;

                case 247:
                    if (target.power > 1) target.power = Mathf.Max(1, target.power - 1);
                    DealLeaderSpellDamage(target, 3 + valBonus);
                    break;

                case 248:
                {
                    CardData pick = null;
                    foreach (var g in caster.graveyard)
                    {
                        if (g != null && g.cardType == CardType.Monster && g.id < 9000)
                        {
                            if (pick == null || g.cost < pick.cost) pick = g;
                        }
                    }
                    if (pick != null)
                    {
                        caster.graveyard.Remove(pick);
                        int cur = caster.discountedCardIds.TryGetValue(pick.id, out int ch) ? ch : 0;
                        caster.discountedCardIds[pick.id] = cur + 1;
                        caster.AddCardToHandWithBurnLimit(pick);
                        caster.onCardDrawnToHand?.Invoke(caster, pick);
                    }
                    break;
                }

                case 249:
                {
                    CardData best = null;
                    foreach (var g in caster.graveyard)
                    {
                        if (g != null && g.cardType == CardType.Monster && g.id < 9000)
                        {
                            if (best == null || g.cost > best.cost) best = g;
                        }
                    }
                    if (best != null)
                    {
                        caster.graveyard.Remove(best);
                        caster.AddCardToHandWithBurnLimit(best);
                        caster.onCardDrawnToHand?.Invoke(caster, best);
                    }
                    caster.HealLeader(20 + valBonus, target);
                    break;
                }

                case 250:
                    caster.currentMana += 2;
                    caster.DrawCard();
                    if (caster.hand.Count >= 5)
                    {
                        caster.HealLeader(15 + valBonus, target);
                    }
                    break;

                case 251:
                {
                    int gyM = 0;
                    foreach (var g in caster.graveyard)
                    {
                        if (g != null && g.cardType == CardType.Monster && g.id < 9000) gyM++;
                    }
                    DealLeaderSpellDamage(target, Mathf.Min(35, gyM * 5) + valBonus);
                    break;
                }

                case 252:
                    caster.hasSacrificedThisTurn = false;
                    caster.currentMana += 1;
                    caster.shield += 8;
                    break;

                // --- Damage & Removal Spells (#253 - #266) ---
                case 253:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 253, valBonus, explicitSlot);
                    DealSpellToMonster(t, 10 + valBonus, target, caster, false, true);
                    break;
                }

                case 254:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 254, valBonus, explicitSlot);
                    DealSpellToMonster(t, 12 + valBonus, target, caster, true, true);
                    break;
                }

                case 255:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 255, valBonus, explicitSlot);
                    if (t != null && t.IsOccupied)
                    {
                        if (t.currentMonster.currentDef <= 15)
                        {
                            t.currentMonster.bubbleShield = false;
                            DealSpellToMonster(t, 9999, target, caster, true, true);
                        }
                        else
                        {
                            DealSpellToMonster(t, 14 + valBonus, target, caster, false, true);
                        }
                    }
                    break;
                }

                case 256:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 256, valBonus, explicitSlot);
                    if (t != null && t.IsOccupied)
                    {
                        int dmg = 20 + valBonus;
                        int over = Mathf.Max(0, dmg - t.currentMonster.currentDef);
                        DealSpellToMonster(t, dmg, target, caster, false, true);
                        if (over > 0) DealLeaderSpellDamage(target, over);
                    }
                    break;
                }

                case 257:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 257, valBonus, explicitSlot);
                    if (t != null && t.IsOccupied)
                    {
                        int idx = t.slotIndex;
                        DealSpellToMonster(t, 18 + valBonus, target, caster, false, true);
                        foreach (int adj in new[] { idx - 1, idx + 1 })
                        {
                            if (adj >= 0 && adj < 6 && target.slots[adj] != null && target.slots[adj].IsOccupied)
                            {
                                DealSpellToMonster(target.slots[adj], 9 + valBonus / 2, target, caster, false, false);
                            }
                        }
                    }
                    break;
                }

                case 258:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 258, valBonus, explicitSlot);
                    if (t != null && t.IsOccupied)
                    {
                        var tm = t.currentMonster;
                        tm.bubbleShield = false;
                        int cut = 42 + valBonus;
                        if (tm.currentDef <= cut)
                        {
                            tm.currentDef = 0;
                            ShowMonsterDamageVfx(t, cut, target == humanPlayer);
                            CombatSystem.Instance?.HandleMonsterDeath(target, caster, t.slotIndex, tm, null);
                        }
                        else
                        {
                            DealSpellToMonster(t, cut, target, caster, true, true);
                        }
                    }
                    break;
                }

                case 259:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 259, valBonus, explicitSlot);
                    if (t != null && t.IsOccupied)
                    {
                        int dmg = (t.currentMonster.currentDef < t.currentMonster.baseDef ? 28 : 12) + valBonus;
                        DealSpellToMonster(t, dmg, target, caster, false, true);
                    }
                    break;
                }

                case 260:
                    if (enemiesAll.Count > 0)
                    {
                        foreach (var es in enemiesAll)
                        {
                            if (es.IsOccupied) DealSpellToMonster(es, 14 + valBonus, target, caster, false, false);
                        }
                    }
                    else
                    {
                        DealLeaderSpellDamage(target, 18 + valBonus);
                    }
                    break;

                case 261:
                    foreach (var es in enemiesAll)
                    {
                        if (es.IsOccupied && es.currentMonster.data != null && es.currentMonster.data.cost >= 4)
                        {
                            DealSpellToMonster(es, 24 + valBonus, target, caster, false, false);
                        }
                    }
                    break;

                case 262:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 262, valBonus, explicitSlot);
                    if (t != null && t.IsOccupied)
                    {
                        int dmg = (t.currentMonster.GetEffectiveAtk(activeEnvironment, target) / 2) + 10 + valBonus;
                        DealSpellToMonster(t, dmg, target, caster, false, true);
                    }
                    break;
                }

                case 263:
                    DealLeaderSpellDamage(target, 30 + valBonus);
                    break;

                case 264:
                {
                    int kBefore = enemiesAll.Count;
                    foreach (var es in enemiesAll)
                    {
                        if (es.IsOccupied) DealSpellToMonster(es, 20 + valBonus, target, caster, false, false);
                    }
                    int slain = Mathf.Max(0, kBefore - GetAliveSlots(target).Count);
                    if (slain > 0) caster.HealLeader(slain * 4, target);
                    break;
                }

                case 265:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 265, valBonus, explicitSlot);
                    if (t != null && t.IsOccupied)
                    {
                        t.currentMonster.bonusAtk = Mathf.Min(0, t.currentMonster.bonusAtk);
                        t.currentMonster.tempTurnAtk = Mathf.Min(0, t.currentMonster.tempTurnAtk);
                        DealSpellToMonster(t, 11 + valBonus, target, caster, false, true);
                    }
                    break;
                }

                case 266:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 266, valBonus, explicitSlot);
                    if (t != null && t.IsOccupied)
                    {
                        int atkSum = 0;
                        foreach (var al in allies) atkSum += al.currentMonster.GetEffectiveAtk(activeEnvironment, caster);
                        int dmg = 10 + Mathf.RoundToInt(atkSum * 0.35f) + valBonus;
                        DealSpellToMonster(t, dmg, target, caster, false, true);
                    }
                    break;
                }

                // --- Buff, Shield & Relocation Spells (#267 - #280) ---
                case 267:
                {
                    var empSlots = new List<int>();
                    var unblockedEmp = new List<int>();
                    for (int i = 0; i < 6; i++)
                    {
                        if (caster.slots[i] != null && !caster.slots[i].IsOccupied)
                        {
                            empSlots.Add(i);
                            if (target.slots[i] == null || !target.slots[i].IsOccupied) unblockedEmp.Add(i);
                        }
                    }

                    BoardSlot alSlot = null;
                    if (explicitSlot >= 0 && explicitSlot < 6 && caster.slots[explicitSlot] != null && caster.slots[explicitSlot].IsOccupied)
                    {
                        alSlot = caster.slots[explicitSlot];
                    }
                    else if (explicitSecondarySlot >= 0 && explicitSecondarySlot < 6 && caster.slots[explicitSecondarySlot] != null && caster.slots[explicitSecondarySlot].IsOccupied)
                    {
                        caster.slots[explicitSecondarySlot].currentMonster.AddBonusDef(6 + valBonus / 2);
                        break;
                    }
                    else if (allies.Count > 0 && empSlots.Count > 0)
                    {
                        alSlot = allies[0];
                        int bestKey = int.MinValue;
                        foreach (var a in allies)
                        {
                            bool blocked = target.slots[a.slotIndex] != null && target.slots[a.slotIndex].IsOccupied;
                            int wantMove = (a.currentMonster.canAttack && blocked && unblockedEmp.Count > 0) ? 10000 : 0;
                            int sc = wantMove + a.currentMonster.baseAtk + a.currentMonster.bonusAtk;
                            if (sc > bestKey) { bestKey = sc; alSlot = a; }
                        }
                    }

                    if (alSlot != null && empSlots.Count > 0)
                    {
                        var al = alSlot.currentMonster;
                        int dst = (explicitSecondarySlot >= 0 && explicitSecondarySlot < 6 && caster.slots[explicitSecondarySlot] != null && !caster.slots[explicitSecondarySlot].IsOccupied)
                            ? explicitSecondarySlot
                            : ((unblockedEmp.Count > 0 && (al.canAttack || al.currentDef > 12)) ? unblockedEmp[0] : empSlots[0]);
                        alSlot.ClearSlot();
                        caster.slots[dst].PlaceMonster(al);
                        al.AddBonusDef(6 + valBonus / 2);
                    }
                    break;
                }

                case 268:
                {
                    var al = ResolveTargetAllySlot(caster, target, 268, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        al.currentMonster.bonusAtk += 6 + valBonus / 2;
                        al.currentMonster.AddBonusDef(6 + valBonus / 2);
                    }
                    break;
                }

                case 269:
                {
                    var al = ResolveTargetAllySlot(caster, target, 269, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        al.currentMonster.tempTurnAtk += 12 + valBonus;
                        al.currentMonster.AddBonusDef(3);
                    }
                    break;
                }

                case 270:
                {
                    var al = ResolveTargetAllySlot(caster, target, 270, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        al.currentMonster.bubbleShield = true;
                        al.currentMonster.AddBonusDef(4 + valBonus / 2);
                    }
                    break;
                }

                case 271:
                {
                    var al = ResolveTargetAllySlot(caster, target, 271, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        var m = al.currentMonster;
                        m.burnDmg = 0; m.poisonDmg = 0; m.frozenTurns = 0; m.weakenedTurns = 0; m.silencedTurns = 0;
                        m.HealDefense(14 + valBonus);
                    }
                    break;
                }

                case 272:
                {
                    var al = ResolveTargetAllySlot(caster, target, 272, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        al.currentMonster.frozenTurns = 0;
                        al.currentMonster.canAttack = true;
                        al.currentMonster.tempTurnAtk += 3 + valBonus / 2;
                    }
                    break;
                }

                case 273:
                {
                    var al = ResolveTargetAllySlot(caster, target, 273, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        al.currentMonster.doubleStrike = true;
                        al.currentMonster.canAttack = true;
                        al.currentMonster.tempTurnAtk += 4 + valBonus / 2;
                    }
                    break;
                }

                case 274:
                {
                    var al = ResolveTargetAllySlot(caster, target, 274, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        al.currentMonster.AddBonusDef(18 + valBonus);
                    }
                    break;
                }

                case 275:
                    foreach (var al in allies)
                    {
                        al.currentMonster.bonusAtk += 4 + valBonus / 3;
                        al.currentMonster.AddBonusDef(4 + valBonus / 3);
                    }
                    break;

                case 276:
                    caster.HealLeader(35 + valBonus, target);
                    caster.shield += 15;
                    caster.DrawCard();
                    break;

                case 277:
                    foreach (var al in allies)
                    {
                        var m = al.currentMonster;
                        m.burnDmg = 0; m.poisonDmg = 0; m.frozenTurns = 0; m.weakenedTurns = 0; m.silencedTurns = 0;
                        m.AddBonusDef(6 + valBonus / 2);
                    }
                    break;

                case 278:
                    caster.shield += 35 + valBonus;
                    caster.DrawCard();
                    break;

                case 279:
                {
                    var al = ResolveTargetAllySlot(caster, target, 279, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        var m = al.currentMonster;
                        m.currentDef = Mathf.Max(m.currentDef, m.baseDef);
                        m.bonusAtk += 8 + valBonus;
                        m.bubbleShield = true;
                    }
                    break;
                }

                case 280:
                {
                    var al = ResolveTargetAllySlot(caster, target, 280, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        int gain = Mathf.Clamp(al.currentMonster.currentDef, 1, 60);
                        al.currentMonster.AddBonusDef(gain);
                        caster.HealLeader(Mathf.Min(45, gain), target);
                    }
                    break;
                }

                // --- Utility & Skill Combo Spells (#281 - #292) ---
                case 281:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 281, valBonus, explicitSlot);
                    if (t != null && t.IsOccupied)
                    {
                        t.currentMonster.baseAtk = Mathf.Max(0, t.currentMonster.baseAtk - 10);
                    }
                    caster.HealLeader(22 + valBonus, target);
                    break;
                }

                case 282:
                {
                    MonsterInstance tgt = null;
                    if (explicitSlot >= 0 && explicitSlot < 6)
                    {
                        var pool = explicitIsAlly ? caster.slots[explicitSlot] : target.slots[explicitSlot];
                        if (pool != null && pool.IsOccupied && pool.currentMonster != null &&
                            (explicitIsAlly || pool.currentMonster.HasPassiveSubstr("Night Stealth") == null))
                        {
                            tgt = pool.currentMonster;
                        }
                    }
                    if (tgt == null)
                    {
                        MonsterInstance bestAl = null;
                        int bestAlScore = int.MinValue;
                        foreach (var a in allies)
                        {
                            int sc = a.currentMonster.currentDef - (a.currentMonster.baseAtk + a.currentMonster.bonusAtk);
                            if (sc > bestAlScore) { bestAlScore = sc; bestAl = a.currentMonster; }
                        }
                        MonsterInstance bestEn = null;
                        int bestEnScore = int.MinValue;
                        foreach (var e in validEnemies)
                        {
                            int sc = (e.currentMonster.baseAtk + e.currentMonster.bonusAtk) - e.currentMonster.currentDef;
                            if (sc > bestEnScore) { bestEnScore = sc; bestEn = e.currentMonster; }
                        }
                        if (bestAl != null && (bestEn == null || bestAlScore >= bestEnScore)) tgt = bestAl;
                        else tgt = bestEn;
                    }
                    if (tgt != null)
                    {
                        int totAtk = Mathf.Max(1, tgt.baseAtk + tgt.bonusAtk);
                        int totDef = Mathf.Max(1, tgt.currentDef);
                        tgt.baseAtk = totDef;
                        tgt.bonusAtk = 0;
                        tgt.baseDef = totAtk;
                        tgt.currentDef = totAtk;
                    }
                    break;
                }

                case 283:
                {
                    var al = ResolveTargetAllySlot(caster, target, 283, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        int taken = Mathf.Max(0, al.currentMonster.currentDef - Mathf.Max(6, al.currentMonster.currentDef / 2));
                        if (taken > 0)
                        {
                            al.currentMonster.currentDef -= taken;
                            al.currentMonster.bonusAtk += Mathf.RoundToInt(taken * 1.5f) + valBonus / 2;
                        }
                    }
                    break;
                }

                case 284:
                {
                    var al = ResolveTargetAllySlot(caster, target, 284, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        al.currentMonster.hasUsedActiveThisTurn = false;
                        for (int idxU = 0; idxU < al.currentMonster.activeUsedThisTurn.Count; idxU++)
                        {
                            al.currentMonster.activeUsedThisTurn[idxU] = false;
                        }
                        caster.freeSkillManaZero = true;
                    }
                    break;
                }

                case 285:
                {
                    var al = ResolveTargetAllySlot(caster, target, 285, explicitSlot);
                    caster.freeSkillBonus += 8 + valBonus;
                    if (al != null && al.IsOccupied)
                    {
                        al.currentMonster.hasUsedActiveThisTurn = false;
                        if (al.currentMonster.activeUsedThisTurn.Count > 0) al.currentMonster.activeUsedThisTurn[0] = false;
                    }
                    break;
                }

                case 286:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 286, valBonus, explicitSlot);
                    DealSpellToMonster(t, 14 + valBonus, target, caster, false, true);
                    caster.HealLeader(14 + valBonus / 2, target);
                    break;
                }

                case 287:
                {
                    if (allies.Count > 0)
                    {
                        int maxAtk = 0;
                        foreach (var a in allies) maxAtk = Mathf.Max(maxAtk, a.currentMonster.GetEffectiveAtk(activeEnvironment, caster));
                        foreach (var e in enemiesAll) maxAtk = Mathf.Max(maxAtk, e.currentMonster.GetEffectiveAtk(activeEnvironment, target));
                        var al = ResolveTargetAllySlot(caster, target, 287, explicitSlot);
                        if (al != null && al.IsOccupied)
                        {
                            int curA = al.currentMonster.GetEffectiveAtk(activeEnvironment, caster);
                            if (maxAtk > curA)
                            {
                                al.currentMonster.bonusAtk += Mathf.Min(12, maxAtk - curA);
                            }
                        }
                    }
                    break;
                }

                case 288:
                {
                    int spent = Mathf.Clamp(caster.currentMana, 1, 6);
                    caster.currentMana -= spent;
                    var t = ResolveTargetEnemySlot(caster, target, 288, valBonus, explicitSlot);
                    DealSpellToMonster(t, spent * 6 + valBonus, target, caster, false, true);
                    break;
                }

                case 289:
                {
                    var al = ResolveTargetAllySlot(caster, target, 289, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        al.currentMonster.bonusAtk += 4 + valBonus / 3;
                        al.currentMonster.AddBonusDef(8 + valBonus / 2);
                        foreach (int adj in new[] { al.slotIndex - 1, al.slotIndex + 1 })
                        {
                            if (adj >= 0 && adj < 6 && caster.slots[adj] != null && caster.slots[adj].IsOccupied)
                            {
                                caster.slots[adj].currentMonster.bonusAtk += 4 + valBonus / 3;
                                caster.slots[adj].currentMonster.AddBonusDef(8 + valBonus / 2);
                                break;
                            }
                        }
                    }
                    break;
                }

                case 290:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 290, valBonus, explicitSlot);
                    if (t != null && t.IsOccupied)
                    {
                        t.currentMonster.bubbleShield = false;
                        t.currentMonster.silencedTurns = 99;
                        DealSpellToMonster(t, 10 + valBonus, target, caster, true, true);
                    }
                    break;
                }

                case 291:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 291, valBonus, explicitSlot);
                    if (t != null && t.IsOccupied)
                    {
                        var tm = t.currentMonster;
                        DealSpellToMonster(t, 12 + valBonus, target, caster, false, true);
                        if (!tm.IsAlive)
                        {
                            caster.DrawCard();
                            caster.DrawCard();
                            caster.currentMana += 1;
                        }
                    }
                    break;
                }

                case 292:
                {
                    var al = ResolveTargetAllySlot(caster, target, 292, explicitSlot);
                    if (al != null && al.IsOccupied && al.currentMonster.currentDef > 6)
                    {
                        al.currentMonster.currentDef -= 6;
                        al.currentMonster.tempTurnAtk += 14 + valBonus;
                    }
                    break;
                }

                // --- Legendary Spells (#293 - #300) ---
                case 293:
                {
                    var al = ResolveTargetAllySlot(caster, target, 293, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        var m = al.currentMonster;
                        if (activeEnvironment != null && m.data != null &&
                            EnvironmentCardData.AreSameElement(activeEnvironment.buffedElement, m.data.element))
                        {
                            m.tempTurnAtk += Mathf.Max(1, m.baseAtk + m.bonusAtk) + valBonus;
                            m.bubbleShield = true;
                        }
                        else
                        {
                            m.tempTurnAtk += 10 + valBonus;
                        }
                    }
                    break;
                }

                case 294:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 294, valBonus, explicitSlot);
                    if (t != null && t.IsOccupied)
                    {
                        int bestAtk = 12;
                        if (allies.Count > 0)
                        {
                            bestAtk = 0;
                            foreach (var a in allies) bestAtk = Mathf.Max(bestAtk, a.currentMonster.GetEffectiveAtk(activeEnvironment, caster));
                        }
                        DealSpellToMonster(t, bestAtk + valBonus, target, caster, false, true);
                    }
                    break;
                }

                case 295:
                {
                    int emp = FindFirstEmptySlot(caster);
                    var al = ResolveTargetAllySlot(caster, target, 295, explicitSlot);
                    if (al != null && al.IsOccupied && emp != -1)
                    {
                        var srcM = al.currentMonster;
                        var cl = new MonsterInstance(srcM.data, currentTurn);
                        cl.baseAtk = srcM.baseAtk;
                        cl.bonusAtk = srcM.bonusAtk;
                        cl.baseDef = srcM.baseDef;
                        cl.currentDef = srcM.currentDef;
                        cl.passives = new List<string>(srcM.passives);
                        cl.actives = new List<string>(srcM.actives);
                        cl.bubbleShield = srcM.bubbleShield;
                        cl.canAttack = false;
                        caster.slots[emp].PlaceMonster(cl);
                    }
                    break;
                }

                case 296:
                {
                    var t = ResolveTargetEnemySlot(caster, target, 296, valBonus, explicitSlot);
                    if (t != null && t.IsOccupied)
                    {
                        var tm = t.currentMonster;
                        tm.bubbleShield = false;
                        tm.currentDef = 0;
                        ShowMonsterDamageVfx(t, tm.baseDef, target == humanPlayer);
                        CombatSystem.Instance?.HandleMonsterDeath(target, caster, t.slotIndex, tm, null);
                        foreach (var es in GetAliveSlots(target))
                        {
                            DealSpellToMonster(es, 8 + valBonus, target, caster, false, false);
                        }
                    }
                    break;
                }

                case 297:
                    foreach (var es in enemiesAll)
                    {
                        if (es.IsOccupied)
                        {
                            var em = es.currentMonster;
                            int cut = Mathf.Max(1, em.currentDef / 2);
                            em.currentDef -= cut;
                            ShowMonsterDamageVfx(es, cut, target == humanPlayer);
                            if (!em.IsAlive)
                            {
                                CombatSystem.Instance?.HandleMonsterDeath(target, caster, es.slotIndex, em, null);
                            }
                        }
                    }
                    DealLeaderSpellDamage(target, 12 + valBonus);
                    break;

                case 298:
                {
                    var al = ResolveTargetAllySlot(caster, target, 298, explicitSlot);
                    if (al != null && al.IsOccupied)
                    {
                        int same = 0;
                        string tr = al.currentMonster.data != null ? al.currentMonster.data.tribe : "";
                        foreach (var oa in allies)
                        {
                            if (oa != al && oa.currentMonster.data != null &&
                                string.Equals(oa.currentMonster.data.tribe, tr, StringComparison.OrdinalIgnoreCase))
                            {
                                same++;
                            }
                        }
                        int bGain = 5 + same * 6 + valBonus / 2;
                        al.currentMonster.bonusAtk += bGain;
                        al.currentMonster.AddBonusDef(bGain);
                    }
                    break;
                }

                case 299:
                    foreach (var es in enemiesAll)
                    {
                        if (es.IsOccupied) DealSpellToMonster(es, 22 + valBonus, target, caster, false, false);
                    }
                    DealLeaderSpellDamage(target, 10 + valBonus);
                    break;

                case 300:
                {
                    CardData best = null;
                    foreach (var g in caster.graveyard)
                    {
                        if (g != null && g.cardType == CardType.Monster && g.id < 9000)
                        {
                            if (best == null || g.cost > best.cost) best = g;
                        }
                    }
                    int emp = (explicitSlot >= 0 && explicitSlot < 6 && caster.slots[explicitSlot] != null && !caster.slots[explicitSlot].IsOccupied)
                        ? explicitSlot
                        : FindPreferredReviveEmptySlot(caster, target);
                    if (best != null && emp != -1)
                    {
                        caster.graveyard.Remove(best);
                        var rm = new MonsterInstance(best, currentTurn);
                        rm.currentDef = Mathf.Max(8, Mathf.RoundToInt(rm.baseDef * 0.70f));
                        rm.canAttack = false;
                        caster.slots[emp].PlaceMonster(rm);
                    }
                    break;
                }

                    // =========================================================================
                    // --- 140 NEW STRATEGIC SPELLS (#301 - #440) ---
                    // =========================================================================

                    // --- 1. Resources, Mana Ramp & Power (#301 - #320) ---
                    case 301:
                        caster.currentMana += 2;
                        break;

                    case 302:
                        caster.currentHp = Mathf.Max(1, caster.currentHp - 15);
                        caster.power += 1;
                        break;

                    case 303:
                        if (caster.hand.Count <= 3) caster.currentMana += 3;
                        else caster.currentMana += 1;
                        break;

                    case 304:
                        caster.power += 1;
                        caster.DrawCard();
                        break;

                    case 305:
                        caster.currentMana += 4;
                        break;

                    case 306:
                        caster.currentHp = Mathf.Max(1, caster.currentHp - 8);
                        caster.currentMana += 2;
                        caster.power += 1;
                        break;

                    case 307:
                        if (caster.power <= 3) caster.power += 2;
                        else caster.power += 1;
                        break;

                    case 308:
                        if (allies.Count >= 3)
                        {
                            caster.currentMana += 3;
                            caster.HealLeader(10 + valBonus, target);
                        }
                        else
                        {
                            caster.currentMana += 1;
                        }
                        break;

                    case 309:
                        caster.currentHp = Mathf.Max(1, caster.currentHp - 25);
                        caster.currentMana += 3;
                        caster.DrawCard();
                        caster.DrawCard();
                        break;

                    case 310:
                    {
                        int spCnt = 0;
                        foreach (var g in caster.graveyard)
                        {
                            if (g != null && g.cardType == CardType.Spell) spCnt++;
                        }
                        int gain = Mathf.Min(4, Mathf.Max(1, spCnt / 2));
                        caster.currentMana += gain;
                        break;
                    }

                    case 311:
                        caster.power += 2;
                        break;

                    case 312:
                        caster.currentMana += 2;
                        caster.shield += 12 + valBonus;
                        break;

                    case 313:
                        caster.currentMana += Mathf.Min(6, allies.Count);
                        break;

                    case 314:
                        caster.hasSacrificedThisTurn = false;
                        caster.currentMana += 1;
                        break;

                    case 315:
                        caster.currentMana += 5;
                        caster.DrawCard();
                        break;

                    case 316:
                        caster.maxHp += 30;
                        caster.currentHp += 30;
                        caster.currentMana += 2;
                        break;

                    case 317:
                        target.denyDrawSacTurns = Mathf.Max(target.denyDrawSacTurns, 1);
                        caster.currentMana += 2;
                        break;

                    case 318:
                        if (caster.currentMana == 0)
                        {
                            caster.currentMana += 3;
                            caster.shield += 10 + valBonus;
                        }
                        else
                        {
                            caster.currentMana += 1;
                        }
                        break;

                    case 319:
                        caster.power += 3;
                        caster.HealLeader(25 + valBonus, target);
                        break;

                    case 320:
                        caster.currentMana += 6;
                        foreach (var a in allies)
                        {
                            if (a.IsOccupied) a.currentMonster.bonusAtk += 3;
                        }
                        break;

                    // --- 2. Card Draw & Hand Replenishment (#321 - #340) ---
                    case 321:
                    {
                        bool drawn = caster.DrawCard();
                        if (caster.hand.Count > 0 && caster.hand[caster.hand.Count - 1].cardType == CardType.Monster)
                        {
                            caster.currentMana += 1;
                        }
                        break;
                    }

                    case 322:
                        caster.DrawCard();
                        caster.DrawCard();
                        break;

                    case 323:
                    {
                        CardData cPick = caster.deck.Find(c => c != null && c.cardType == CardType.Monster);
                        if (cPick != null)
                        {
                            caster.deck.Remove(cPick);
                            int curDisc = caster.discountedCardIds.TryGetValue(cPick.id, out int d) ? d : 0;
                            caster.discountedCardIds[cPick.id] = curDisc;
                            caster.AddCardToHandWithBurnLimit(cPick);
                            caster.onCardDrawnToHand?.Invoke(caster, cPick);
                        }
                        else
                        {
                            caster.DrawCard();
                        }
                        break;
                    }

                    case 324:
                    {
                        CardData sPick = caster.deck.Find(c => c != null && c.cardType == CardType.Spell);
                        if (sPick != null)
                        {
                            caster.deck.Remove(sPick);
                            caster.AddCardToHandWithBurnLimit(sPick);
                            caster.onCardDrawnToHand?.Invoke(caster, sPick);
                        }
                        else
                        {
                            caster.DrawCard();
                        }
                        caster.HealLeader(10 + valBonus, target);
                        break;
                    }

                    case 325:
                        caster.currentHp = Mathf.Max(1, caster.currentHp - 15);
                        for (int i = 0; i < 3; i++) caster.DrawCard();
                        break;

                    case 326:
                        caster.DrawCard();
                        caster.DrawCard();
                        caster.shield += 10 + valBonus;
                        break;

                    case 327:
                        if (caster.DrawCard() && caster.hand.Count > 0)
                        {
                            var lastC = caster.hand[caster.hand.Count - 1];
                            int cur = caster.discountedCardIds.TryGetValue(lastC.id, out int cd) ? cd : 0;
                            caster.discountedCardIds[lastC.id] = cur + 1;
                        }
                        break;

                    case 328:
                        if (caster.hand.Count <= 2)
                        {
                            for (int i = 0; i < 3; i++) caster.DrawCard();
                            caster.currentMana += 1;
                        }
                        else
                        {
                            caster.DrawCard();
                        }
                        break;

                    case 329:
                    {
                        int gyLen = caster.graveyard.Count;
                        int nDraw = Mathf.Min(3, Mathf.Max(1, gyLen / 4));
                        for (int i = 0; i < nDraw; i++) caster.DrawCard();
                        break;
                    }

                    case 330:
                        caster.DrawCard();
                        caster.DrawCard();
                        target.DrawCard();
                        target.DrawCard();
                        caster.HealLeader(20 + valBonus, target);
                        break;

                    case 331:
                        caster.DrawCard();
                        if (enemiesAll.Count > allies.Count)
                        {
                            caster.DrawCard();
                        }
                        break;

                    case 332:
                        caster.DrawCard();
                        caster.DrawCard();
                        foreach (var a in allies)
                        {
                            if (a.IsOccupied) a.currentMonster.bonusAtk += 2;
                        }
                        break;

                    case 333:
                    {
                        caster.DrawCard();
                        caster.DrawCard();
                        int mGy = 0;
                        foreach (var g in caster.graveyard)
                        {
                            if (g != null && g.cardType == CardType.Monster) mGy++;
                        }
                        if (mGy >= 4) caster.currentMana += 2;
                        break;
                    }

                    case 334:
                        for (int i = 0; i < 3; i++) caster.DrawCard();
                        caster.shield += 15 + valBonus;
                        break;

                    case 335:
                        if (caster.graveyard.Count > 0)
                        {
                            int rIdx = UnityEngine.Random.Range(0, caster.graveyard.Count);
                            var cPick = caster.graveyard[rIdx];
                            caster.graveyard.RemoveAt(rIdx);
                            caster.AddCardToHandWithBurnLimit(cPick);
                            caster.onCardDrawnToHand?.Invoke(caster, cPick);
                        }
                        break;

                    case 336:
                        caster.DrawCard();
                        caster.HealLeader(6 + valBonus, target);
                        break;

                    case 337:
                    {
                        CardData bestM = null;
                        foreach (var c in caster.deck)
                        {
                            if (c != null && c.cardType == CardType.Monster)
                            {
                                if (bestM == null || c.cost > bestM.cost) bestM = c;
                            }
                        }
                        if (bestM != null)
                        {
                            caster.deck.Remove(bestM);
                            int cur = caster.discountedCardIds.TryGetValue(bestM.id, out int cd) ? cd : 0;
                            caster.discountedCardIds[bestM.id] = cur + 2;
                            caster.AddCardToHandWithBurnLimit(bestM);
                            caster.onCardDrawnToHand?.Invoke(caster, bestM);
                        }
                        else
                        {
                            caster.DrawCard();
                        }
                        break;
                    }

                    case 338:
                        caster.DrawCard();
                        caster.DrawCard();
                        if (validEnemies.Count > 0)
                        {
                            var tgt = validEnemies[UnityEngine.Random.Range(0, validEnemies.Count)];
                            DealSpellToMonster(tgt, 8 + valBonus, target, caster, false, true);
                        }
                        break;

                    case 339:
                    {
                        caster.DrawCard();
                        caster.DrawCard();
                        CardData weakest = null;
                        foreach (var g in caster.graveyard)
                        {
                            if (g != null && g.cardType == CardType.Monster)
                            {
                                if (weakest == null || g.cost < weakest.cost) weakest = g;
                            }
                        }
                        if (weakest != null)
                        {
                            caster.graveyard.Remove(weakest);
                            caster.discountedCardIds[weakest.id] = weakest.cost;
                            caster.AddCardToHandWithBurnLimit(weakest);
                            caster.onCardDrawnToHand?.Invoke(caster, weakest);
                        }
                        break;
                    }

                    case 340:
                        for (int i = 0; i < 4; i++) caster.DrawCard();
                        caster.shield += 20 + valBonus;
                        break;

                    // --- 3. Duplication & Cloning (#341 - #355) ---
                    case 341:
                        if (caster.hand.Count > 0)
                        {
                            var cCopy = caster.hand[UnityEngine.Random.Range(0, caster.hand.Count)];
                            caster.AddCardToHandWithBurnLimit(cCopy);
                            caster.onCardDrawnToHand?.Invoke(caster, cCopy);
                        }
                        break;

                    case 342:
                    {
                        CardData lastSp = null;
                        for (int i = caster.graveyard.Count - 1; i >= 0; i--)
                        {
                            var g = caster.graveyard[i];
                            if (g != null && g.cardType == CardType.Spell && g.id != cid)
                            {
                                lastSp = g;
                                break;
                            }
                        }
                        if (lastSp != null)
                        {
                            caster.AddCardToHandWithBurnLimit(lastSp);
                            caster.onCardDrawnToHand?.Invoke(caster, lastSp);
                        }
                        break;
                    }

                    case 343:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 343, explicitSlot);
                        if (al != null && al.IsOccupied && al.currentMonster.data != null)
                        {
                            var proto = al.currentMonster.data;
                            int cur = caster.discountedCardIds.TryGetValue(proto.id, out int cd) ? cd : 0;
                            caster.discountedCardIds[proto.id] = cur + 1;
                            caster.AddCardToHandWithBurnLimit(proto);
                            caster.onCardDrawnToHand?.Invoke(caster, proto);
                        }
                        break;
                    }

                    case 344:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 344, explicitSlot);
                        int dst = (explicitSecondarySlot >= 0 && explicitSecondarySlot < 6 && caster.slots[explicitSecondarySlot] != null && !caster.slots[explicitSecondarySlot].IsOccupied)
                            ? explicitSecondarySlot
                            : FindFirstEmptySlot(caster);
                        if (al != null && al.IsOccupied && dst != -1)
                        {
                            var src = al.currentMonster;
                            var cl = new MonsterInstance(src.data, currentTurn);
                            cl.baseAtk = src.baseAtk;
                            cl.bonusAtk = src.bonusAtk;
                            cl.baseDef = Mathf.Max(1, src.currentDef / 2);
                            cl.currentDef = cl.baseDef;
                            cl.canAttack = false;
                            caster.slots[dst].PlaceMonster(cl);
                        }
                        break;
                    }

                    case 345:
                        if (target.hand.Count > 0)
                        {
                            var oppC = target.hand[UnityEngine.Random.Range(0, target.hand.Count)];
                            caster.AddCardToHandWithBurnLimit(oppC);
                            caster.onCardDrawnToHand?.Invoke(caster, oppC);
                        }
                        break;

                    case 346:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 346, explicitSlot);
                        int dst = FindFirstEmptySlot(caster);
                        if (al != null && al.IsOccupied && dst != -1)
                        {
                            var src = al.currentMonster;
                            int newDef = Mathf.Max(1, src.currentDef / 2);
                            src.currentDef = newDef;
                            var cl = new MonsterInstance(src.data, currentTurn);
                            cl.baseAtk = src.baseAtk;
                            cl.currentDef = newDef;
                            cl.canAttack = false;
                            caster.slots[dst].PlaceMonster(cl);
                        }
                        break;
                    }

                    case 347:
                    {
                        int dst = (explicitSlot >= 0 && explicitSlot < 6 && caster.slots[explicitSlot] != null && !caster.slots[explicitSlot].IsOccupied)
                            ? explicitSlot
                            : FindFirstEmptySlot(caster);
                        if (allies.Count > 0 && dst != -1)
                        {
                            BoardSlot strongest = null;
                            int maxAtk = -1;
                            foreach (var a in allies)
                            {
                                int at = a.currentMonster.baseAtk + a.currentMonster.bonusAtk;
                                if (strongest == null || at > maxAtk) { strongest = a; maxAtk = at; }
                            }
                            if (strongest != null && strongest.currentMonster.data != null)
                            {
                                var cl = new MonsterInstance(strongest.currentMonster.data, currentTurn);
                                cl.currentDef = 1;
                                cl.canAttack = true;
                                caster.slots[dst].PlaceMonster(cl);
                            }
                        }
                        break;
                    }

                    case 348:
                        caster.nextSpellRepeats++;
                        break;

                    case 349:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 349, explicitSlot);
                        if (al != null && al.IsOccupied && allies.Count >= 2)
                        {
                            BoardSlot otherAl = null;
                            foreach (var a in allies) { if (a != al && a.IsOccupied) { otherAl = a; break; } }
                            if (otherAl != null)
                            {
                                otherAl.currentMonster.bonusAtk += (al.currentMonster.baseAtk + al.currentMonster.bonusAtk) / 2;
                                otherAl.currentMonster.AddBonusDef(al.currentMonster.currentDef / 2);
                            }
                        }
                        break;
                    }

                    case 350:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 350, explicitSlot);
                        if (al != null && al.IsOccupied && enemiesAll.Count > 0)
                        {
                            int topOppAtk = 0;
                            foreach (var e in enemiesAll)
                            {
                                if (e.IsOccupied) topOppAtk = Mathf.Max(topOppAtk, e.currentMonster.baseAtk + e.currentMonster.bonusAtk);
                            }
                            al.currentMonster.bonusAtk = Mathf.Max(al.currentMonster.bonusAtk, topOppAtk - al.currentMonster.baseAtk);
                        }
                        break;
                    }

                    case 351:
                    {
                        CardData strongestM = null;
                        foreach (var g in caster.graveyard)
                        {
                            if (g != null && g.cardType == CardType.Monster && g.id < 9000)
                            {
                                if (strongestM == null || g.cost > strongestM.cost) strongestM = g;
                            }
                        }
                        if (strongestM != null)
                        {
                            int cur = caster.discountedCardIds.TryGetValue(strongestM.id, out int cd) ? cd : 0;
                            caster.discountedCardIds[strongestM.id] = cur + 1;
                            caster.AddCardToHandWithBurnLimit(strongestM);
                            caster.onCardDrawnToHand?.Invoke(caster, strongestM);
                            caster.AddCardToHandWithBurnLimit(strongestM);
                            caster.onCardDrawnToHand?.Invoke(caster, strongestM);
                        }
                        break;
                    }

                    case 352:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 352, explicitSlot);
                        if (al != null && al.IsOccupied)
                        {
                            caster.shield += Mathf.Min(50, al.currentMonster.currentDef);
                        }
                        break;
                    }

                    case 353:
                        caster.currentMana += 2;
                        caster.shield += 10 + valBonus;
                        break;

                    case 354:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 354, explicitSlot);
                        if (al != null && al.IsOccupied)
                        {
                            al.currentMonster.bonusAtk += 10 + valBonus;
                            al.currentMonster.AddBonusDef(15 + valBonus);
                            if (al.currentMonster.data != null)
                            {
                                caster.AddCardToHandWithBurnLimit(al.currentMonster.data);
                                caster.onCardDrawnToHand?.Invoke(caster, al.currentMonster.data);
                            }
                        }
                        break;
                    }

                    case 355:
                    {
                        CardData srcProto = (allies.Count > 0 && allies[0].currentMonster.data != null) ? allies[0].currentMonster.data : spellCard;
                        for (int i = 0; i < 6; i++)
                        {
                            if (caster.slots[i] != null && !caster.slots[i].IsOccupied)
                            {
                                var cl = new MonsterInstance(srcProto, currentTurn);
                                cl.baseAtk = 8;
                                cl.currentDef = 12;
                                cl.canAttack = true;
                                caster.slots[i].PlaceMonster(cl);
                            }
                        }
                        break;
                    }

                    // --- 4. Mind Control & Thefts (#356 - #370) ---
                    case 356:
                    {
                        int dst = FindFirstEmptySlot(caster);
                        if (dst != -1)
                        {
                            BoardSlot tgt = null;
                            if (explicitSlot >= 0 && explicitSlot < 6 && target.slots[explicitSlot].IsOccupied &&
                                target.slots[explicitSlot].currentMonster.data != null && target.slots[explicitSlot].currentMonster.data.cost <= 2 &&
                                target.slots[explicitSlot].currentMonster.HasPassiveSubstr("Night Stealth") == null)
                            {
                                tgt = target.slots[explicitSlot];
                            }
                            else
                            {
                                foreach (var ve in validEnemies)
                                {
                                    if (ve.currentMonster.data != null && ve.currentMonster.data.cost <= 2)
                                    {
                                        tgt = ve;
                                        break;
                                    }
                                }
                            }
                            if (tgt != null && tgt.IsOccupied)
                            {
                                var tm = tgt.currentMonster;
                                tgt.ClearSlot();
                                tm.canAttack = false;
                                caster.slots[dst].PlaceMonster(tm);
                            }
                        }
                        break;
                    }

                    case 357:
                    {
                        int dst = FindFirstEmptySlot(caster);
                        var tgt = ResolveTargetEnemySlot(caster, target, 357, valBonus, explicitSlot);
                        if (tgt != null && tgt.IsOccupied && dst != -1)
                        {
                            var tm = tgt.currentMonster;
                            tgt.ClearSlot();
                            tm.canAttack = true;
                            caster.slots[dst].PlaceMonster(tm);
                        }
                        break;
                    }

                    case 358:
                    {
                        BoardSlot tgt = null;
                        if (explicitSlot >= 0 && explicitSlot < 6 && target.slots[explicitSlot].IsOccupied &&
                            target.slots[explicitSlot].currentMonster.currentDef <= 15 &&
                            target.slots[explicitSlot].currentMonster.HasPassiveSubstr("Night Stealth") == null)
                        {
                            tgt = target.slots[explicitSlot];
                        }
                        else
                        {
                            foreach (var ve in validEnemies)
                            {
                                if (ve.currentMonster.currentDef <= 15) { tgt = ve; break; }
                            }
                        }
                        if (tgt != null && tgt.IsOccupied)
                        {
                            var proto = tgt.currentMonster.data;
                            tgt.ClearSlot();
                            if (proto != null)
                            {
                                caster.AddCardToHandWithBurnLimit(proto);
                                caster.onCardDrawnToHand?.Invoke(caster, proto);
                            }
                        }
                        break;
                    }

                    case 359:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 359, explicitSlot);
                        if (al != null && al.IsOccupied && validEnemies.Count > 0)
                        {
                            BoardSlot en = null;
                            int minCost = 999;
                            foreach (var ve in validEnemies)
                            {
                                int c = (ve.currentMonster.data != null) ? ve.currentMonster.data.cost : 99;
                                if (en == null || c < minCost) { en = ve; minCost = c; }
                            }
                            if (en != null && en.IsOccupied)
                            {
                                var allyM = al.currentMonster;
                                var enemyM = en.currentMonster;
                                al.ClearSlot();
                                en.ClearSlot();
                                al.PlaceMonster(enemyM);
                                en.PlaceMonster(allyM);
                            }
                        }
                        break;
                    }

                    case 360:
                    {
                        int dst = FindFirstEmptySlot(caster);
                        if (dst != -1)
                        {
                            BoardSlot tgt = null;
                            if (explicitSlot >= 0 && explicitSlot < 6 && target.slots[explicitSlot].IsOccupied &&
                                target.slots[explicitSlot].currentMonster.data != null && target.slots[explicitSlot].currentMonster.data.cost <= 4 &&
                                target.slots[explicitSlot].currentMonster.HasPassiveSubstr("Night Stealth") == null)
                            {
                                tgt = target.slots[explicitSlot];
                            }
                            else
                            {
                                foreach (var ve in validEnemies)
                                {
                                    if (ve.currentMonster.data != null && ve.currentMonster.data.cost <= 4)
                                    {
                                        tgt = ve; break;
                                    }
                                }
                            }
                            if (tgt != null && tgt.IsOccupied)
                            {
                                var tm = tgt.currentMonster;
                                tgt.ClearSlot();
                                tm.canAttack = false;
                                caster.slots[dst].PlaceMonster(tm);
                            }
                        }
                        break;
                    }

                    case 361:
                    {
                        var tgt = ResolveTargetEnemySlot(caster, target, 361, valBonus, explicitSlot);
                        if (tgt != null && tgt.IsOccupied)
                        {
                            int sIdx = tgt.slotIndex;
                            BoardSlot adjSlot = null;
                            if (sIdx - 1 >= 0 && target.slots[sIdx - 1].IsOccupied) adjSlot = target.slots[sIdx - 1];
                            else if (sIdx + 1 < 6 && target.slots[sIdx + 1].IsOccupied) adjSlot = target.slots[sIdx + 1];
                            if (adjSlot != null)
                            {
                                int atkDmg = tgt.currentMonster.baseAtk + tgt.currentMonster.bonusAtk;
                                DealSpellToMonster(adjSlot, atkDmg, target, caster, false, false);
                            }
                        }
                        break;
                    }

                    case 362:
                    {
                        int dst = FindFirstEmptySlot(caster);
                        var tgt = ResolveTargetEnemySlot(caster, target, 362, valBonus, explicitSlot);
                        if (tgt != null && tgt.IsOccupied && dst != -1)
                        {
                            var tm = tgt.currentMonster;
                            tgt.ClearSlot();
                            tm.canAttack = false;
                            caster.slots[dst].PlaceMonster(tm);
                        }
                        break;
                    }

                    case 363:
                    {
                        var tgt = ResolveTargetEnemySlot(caster, target, 363, valBonus, explicitSlot);
                        if (tgt != null && tgt.IsOccupied)
                        {
                            tgt.currentMonster.weakenedTurns = Mathf.Max(tgt.currentMonster.weakenedTurns, 2);
                        }
                        DealLeaderSpellDamage(target, 6 + valBonus);
                        break;
                    }

                    case 364:
                    {
                        int dst = FindFirstEmptySlot(caster);
                        if (dst != -1)
                        {
                            BoardSlot tgt = null;
                            if (explicitSlot >= 0 && explicitSlot < 6 && target.slots[explicitSlot].IsOccupied &&
                                target.slots[explicitSlot].currentMonster.currentDef < 12 &&
                                target.slots[explicitSlot].currentMonster.HasPassiveSubstr("Night Stealth") == null)
                            {
                                tgt = target.slots[explicitSlot];
                            }
                            else
                            {
                                foreach (var ve in validEnemies)
                                {
                                    if (ve.currentMonster.currentDef < 12) { tgt = ve; break; }
                                }
                            }
                            if (tgt != null && tgt.IsOccupied)
                            {
                                var tm = tgt.currentMonster;
                                tgt.ClearSlot();
                                tm.canAttack = false;
                                caster.slots[dst].PlaceMonster(tm);
                            }
                        }
                        break;
                    }

                    case 365:
                    {
                        int dst = FindFirstEmptySlot(caster);
                        var tgt = ResolveTargetEnemySlot(caster, target, 365, valBonus, explicitSlot);
                        if (tgt != null && tgt.IsOccupied && dst != -1)
                        {
                            var tm = tgt.currentMonster;
                            tgt.ClearSlot();
                            tm.silencedTurns = 99;
                            tm.baseAtk = 10;
                            tm.currentDef = 10;
                            tm.canAttack = false;
                            caster.slots[dst].PlaceMonster(tm);
                        }
                        break;
                    }

                    case 366:
                    {
                        int dst = FindFirstEmptySlot(caster);
                        var tgt = ResolveTargetEnemySlot(caster, target, 366, valBonus, explicitSlot);
                        if (tgt != null && tgt.IsOccupied && dst != -1)
                        {
                            var proto = tgt.currentMonster.data;
                            CombatSystem.Instance?.HandleMonsterDeath(target, caster, tgt.slotIndex, tgt.currentMonster, null);
                            if (proto != null)
                            {
                                var cl = new MonsterInstance(proto, currentTurn);
                                cl.canAttack = false;
                                caster.slots[dst].PlaceMonster(cl);
                            }
                        }
                        break;
                    }

                    case 367:
                        caster.shield += 15;
                        DealLeaderSpellDamage(target, 10 + valBonus);
                        break;

                    case 368:
                    {
                        var tgt = ResolveTargetEnemySlot(caster, target, 368, valBonus, explicitSlot);
                        if (tgt != null && tgt.IsOccupied)
                        {
                            int atkDmg = tgt.currentMonster.baseAtk + tgt.currentMonster.bonusAtk;
                            DealLeaderSpellDamage(target, atkDmg);
                        }
                        break;
                    }

                    case 369:
                    {
                        int dst = FindFirstEmptySlot(caster);
                        if (validEnemies.Count > 0 && dst != -1)
                        {
                            BoardSlot strongest = null;
                            int maxAtk = -1;
                            foreach (var ve in validEnemies)
                            {
                                int at = ve.currentMonster.baseAtk + ve.currentMonster.bonusAtk;
                                if (strongest == null || at > maxAtk) { strongest = ve; maxAtk = at; }
                            }
                            if (strongest != null && strongest.IsOccupied)
                            {
                                var sm = strongest.currentMonster;
                                strongest.ClearSlot();
                                sm.bonusAtk += 5;
                                sm.AddBonusDef(10);
                                sm.bubbleShield = true;
                                sm.canAttack = true;
                                caster.slots[dst].PlaceMonster(sm);
                            }
                        }
                        break;
                    }

                    case 370:
                        foreach (var ve in validEnemies)
                        {
                            if (ve.IsOccupied && ve.currentMonster.data != null && ve.currentMonster.data.cost <= 3)
                            {
                                int dst = FindFirstEmptySlot(caster);
                                if (dst == -1) break;
                                var em = ve.currentMonster;
                                ve.ClearSlot();
                                em.canAttack = false;
                                caster.slots[dst].PlaceMonster(em);
                            }
                        }
                        break;

                    // --- 5. Graveyard Rebirth (#371 - #385) ---
                    case 371:
                    {
                        CardData pk = caster.graveyard.Find(g => g != null && g.cardType == CardType.Monster && g.cost <= 1 && g.id < 9000);
                        int dst = (explicitSlot >= 0 && explicitSlot < 6 && caster.slots[explicitSlot] != null && !caster.slots[explicitSlot].IsOccupied)
                            ? explicitSlot
                            : FindFirstEmptySlot(caster);
                        if (pk != null && dst != -1)
                        {
                            caster.graveyard.Remove(pk);
                            var cl = new MonsterInstance(pk, currentTurn);
                            cl.currentDef = 5;
                            cl.canAttack = false;
                            caster.slots[dst].PlaceMonster(cl);
                        }
                        break;
                    }

                    case 372:
                    {
                        CardData bestDef = null;
                        foreach (var g in caster.graveyard)
                        {
                            if (g != null && g.cardType == CardType.Monster && g.id < 9000)
                            {
                                if (bestDef == null || g.def > bestDef.def) bestDef = g;
                            }
                        }
                        int dst = (explicitSlot >= 0 && explicitSlot < 6 && caster.slots[explicitSlot] != null && !caster.slots[explicitSlot].IsOccupied)
                            ? explicitSlot
                            : FindFirstEmptySlot(caster);
                        if (bestDef != null && dst != -1)
                        {
                            caster.graveyard.Remove(bestDef);
                            var cl = new MonsterInstance(bestDef, currentTurn);
                            int halfDef = Mathf.Max(1, bestDef.def / 2);
                            cl.currentDef = halfDef;
                            cl.canAttack = false;
                            caster.slots[dst].PlaceMonster(cl);
                        }
                        break;
                    }

                    case 373:
                    {
                        CardData pk = caster.graveyard.Find(g => g != null && g.cardType == CardType.Monster && g.id < 9000);
                        int dst = (explicitSlot >= 0 && explicitSlot < 6 && caster.slots[explicitSlot] != null && !caster.slots[explicitSlot].IsOccupied)
                            ? explicitSlot
                            : FindFirstEmptySlot(caster);
                        if (pk != null && dst != -1)
                        {
                            caster.graveyard.Remove(pk);
                            var cl = new MonsterInstance(pk, currentTurn);
                            cl.canAttack = true;
                            caster.slots[dst].PlaceMonster(cl);
                        }
                        break;
                    }

                    case 374:
                        for (int i = 0; i < 2; i++)
                        {
                            var mGy = caster.graveyard.FindAll(g => g != null && g.cardType == CardType.Monster && g.id < 9000);
                            if (mGy.Count > 0)
                            {
                                var pk = mGy[UnityEngine.Random.Range(0, mGy.Count)];
                                caster.graveyard.Remove(pk);
                                caster.AddCardToHandWithBurnLimit(pk);
                                caster.onCardDrawnToHand?.Invoke(caster, pk);
                            }
                        }
                        break;

                    case 375:
                        for (int i = 0; i < 3; i++)
                        {
                            int dst = FindFirstEmptySlot(caster);
                            if (dst == -1) break;
                            var pk = caster.graveyard.Find(g => g != null && g.cardType == CardType.Monster && g.id < 9000);
                            if (pk == null) break;
                            caster.graveyard.Remove(pk);
                            var cl = new MonsterInstance(pk, currentTurn);
                            cl.currentDef = 8;
                            cl.canAttack = false;
                            caster.slots[dst].PlaceMonster(cl);
                        }
                        break;

                    case 376:
                    {
                        CardData bestM = null;
                        foreach (var g in caster.graveyard)
                        {
                            if (g != null && g.cardType == CardType.Monster && g.id < 9000)
                            {
                                if (bestM == null || g.cost > bestM.cost) bestM = g;
                            }
                        }
                        if (bestM != null)
                        {
                            caster.graveyard.Remove(bestM);
                            int cur = caster.discountedCardIds.TryGetValue(bestM.id, out int cd) ? cd : 0;
                            caster.discountedCardIds[bestM.id] = cur + 2;
                            caster.AddCardToHandWithBurnLimit(bestM);
                            caster.onCardDrawnToHand?.Invoke(caster, bestM);
                        }
                        break;
                    }

                    case 377:
                    {
                        CardData pk = target.graveyard.Find(g => g != null && g.cardType == CardType.Monster && g.id < 9000);
                        int dst = (explicitSlot >= 0 && explicitSlot < 6 && caster.slots[explicitSlot] != null && !caster.slots[explicitSlot].IsOccupied)
                            ? explicitSlot
                            : FindFirstEmptySlot(caster);
                        if (pk != null && dst != -1)
                        {
                            target.graveyard.Remove(pk);
                            var cl = new MonsterInstance(pk, currentTurn);
                            cl.canAttack = false;
                            caster.slots[dst].PlaceMonster(cl);
                        }
                        break;
                    }

                    case 378:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 378, explicitSlot);
                        CardData bestM = null;
                        foreach (var g in caster.graveyard)
                        {
                            if (g != null && g.cardType == CardType.Monster && g.id < 9000)
                            {
                                if (bestM == null || g.cost > bestM.cost) bestM = g;
                            }
                        }
                        if (al != null && al.IsOccupied && bestM != null)
                        {
                            int sIdx = al.slotIndex;
                            CombatSystem.Instance?.HandleMonsterDeath(caster, target, sIdx, al.currentMonster, null);
                            var cl = new MonsterInstance(bestM, currentTurn);
                            cl.canAttack = false;
                            caster.slots[sIdx].PlaceMonster(cl);
                        }
                        break;
                    }

                    case 379:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 379, explicitSlot);
                        if (al != null && al.IsOccupied)
                        {
                            al.currentMonster.salvationUsed = false;
                            al.currentMonster.AddBonusDef(12);
                        }
                        break;
                    }

                    case 380:
                    {
                        CardData bestM = null;
                        foreach (var g in caster.graveyard)
                        {
                            if (g != null && g.cardType == CardType.Monster && g.id < 9000)
                            {
                                if (bestM == null || g.cost > bestM.cost) bestM = g;
                            }
                        }
                        int dst = (explicitSlot >= 0 && explicitSlot < 6 && caster.slots[explicitSlot] != null && !caster.slots[explicitSlot].IsOccupied)
                            ? explicitSlot
                            : FindFirstEmptySlot(caster);
                        if (bestM != null && dst != -1)
                        {
                            caster.graveyard.Remove(bestM);
                            var cl = new MonsterInstance(bestM, currentTurn);
                            cl.bonusAtk += 8;
                            cl.AddBonusDef(12);
                            cl.bubbleShield = true;
                            cl.canAttack = true;
                            caster.slots[dst].PlaceMonster(cl);
                        }
                        break;
                    }

                    case 381:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 381, explicitSlot);
                        if (al != null && al.IsOccupied)
                        {
                            al.currentMonster.currentDef = al.currentMonster.baseDef;
                            al.currentMonster.AddBonusDef(8);
                        }
                        break;
                    }

                    case 382:
                    {
                        CardData strongest = null;
                        foreach (var g in target.graveyard)
                        {
                            if (g != null && g.cardType == CardType.Monster && g.id < 9000)
                            {
                                if (strongest == null || g.cost > strongest.cost) strongest = g;
                            }
                        }
                        if (strongest != null)
                        {
                            target.graveyard.Remove(strongest);
                            caster.AddCardToHandWithBurnLimit(strongest);
                            caster.onCardDrawnToHand?.Invoke(caster, strongest);
                        }
                        break;
                    }

                    case 383:
                    {
                        CardData pk = caster.graveyard.Find(g => g != null && g.cardType == CardType.Monster && g.id < 9000);
                        int dst = (explicitSlot >= 0 && explicitSlot < 6 && caster.slots[explicitSlot] != null && !caster.slots[explicitSlot].IsOccupied)
                            ? explicitSlot
                            : FindFirstEmptySlot(caster);
                        if (pk != null && dst != -1)
                        {
                            caster.graveyard.Remove(pk);
                            var cl = new MonsterInstance(pk, currentTurn);
                            cl.bonusAtk += caster.graveyard.Count;
                            cl.canAttack = false;
                            caster.slots[dst].PlaceMonster(cl);
                        }
                        break;
                    }

                    case 384:
                        for (int i = 0; i < 2; i++)
                        {
                            int dst = FindFirstEmptySlot(caster);
                            if (dst == -1) break;
                            CardData bestM = null;
                            foreach (var g in caster.graveyard)
                            {
                                if (g != null && g.cardType == CardType.Monster && g.id < 9000)
                                {
                                    if (bestM == null || g.cost > bestM.cost) bestM = g;
                                }
                            }
                            if (bestM == null) break;
                            caster.graveyard.Remove(bestM);
                            var cl = new MonsterInstance(bestM, currentTurn);
                            cl.currentDef = Mathf.Max(1, Mathf.RoundToInt(cl.baseDef * 0.80f));
                            cl.canAttack = false;
                            caster.slots[dst].PlaceMonster(cl);
                        }
                        break;

                    case 385:
                        foreach (var es in enemiesAll)
                        {
                            if (es.IsOccupied)
                            {
                                CombatSystem.Instance?.HandleMonsterDeath(target, caster, es.slotIndex, es.currentMonster, null);
                            }
                        }
                        for (int i = 0; i < 6; i++)
                        {
                            int dst = FindFirstEmptySlot(caster);
                            if (dst == -1) break;
                            var pk = caster.graveyard.Find(g => g != null && g.cardType == CardType.Monster && g.id < 9000);
                            if (pk == null) break;
                            caster.graveyard.Remove(pk);
                            var cl = new MonsterInstance(pk, currentTurn);
                            cl.canAttack = false;
                            caster.slots[dst].PlaceMonster(cl);
                        }
                        break;

                    // --- 6. Direct Damage & AOE Burn (#386 - #405) ---
                    case 386:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 386, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied) DealSpellToMonster(t, 10 + valBonus, target, caster, false, true);
                        break;
                    }

                    case 387:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 387, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            DealSpellToMonster(t, 8 + valBonus, target, caster, false, true);
                            if (t.IsOccupied) t.currentMonster.burnDmg = Mathf.Max(t.currentMonster.burnDmg, 6);
                        }
                        break;
                    }

                    case 388:
                        DealLeaderSpellDamage(target, 16 + valBonus);
                        break;

                    case 389:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 389, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            t.currentMonster.poisonDmg = Mathf.Max(t.currentMonster.poisonDmg, 5);
                        }
                        break;
                    }

                    case 390:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 390, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            int dmg = Mathf.Max(10, caster.currentMana * 5) + valBonus;
                            DealSpellToMonster(t, dmg, target, caster, false, true);
                        }
                        break;
                    }

                    case 391:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 391, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            int sIdx = t.slotIndex;
                            DealSpellToMonster(t, 16 + valBonus, target, caster, false, true);
                            if (sIdx - 1 >= 0 && target.slots[sIdx - 1].IsOccupied)
                                DealSpellToMonster(target.slots[sIdx - 1], 8 + valBonus / 2, target, caster, false, false);
                            if (sIdx + 1 < 6 && target.slots[sIdx + 1].IsOccupied)
                                DealSpellToMonster(target.slots[sIdx + 1], 8 + valBonus / 2, target, caster, false, false);
                        }
                        break;
                    }

                    case 392:
                        foreach (var es in enemiesAll)
                        {
                            if (es.IsOccupied) DealSpellToMonster(es, 12 + valBonus, target, caster, false, false);
                        }
                        DealLeaderSpellDamage(target, 6 + valBonus / 2);
                        break;

                    case 393:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 393, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            if (t.currentMonster.currentDef <= 25)
                            {
                                CombatSystem.Instance?.HandleMonsterDeath(target, caster, t.slotIndex, t.currentMonster, null);
                            }
                            else
                            {
                                DealSpellToMonster(t, 15 + valBonus, target, caster, false, true);
                            }
                        }
                        break;
                    }

                    case 394:
                        foreach (var es in enemiesAll)
                        {
                            if (es.IsOccupied)
                            {
                                DealSpellToMonster(es, 10 + valBonus, target, caster, false, false);
                                if (es.IsOccupied) es.currentMonster.burnDmg = Mathf.Max(es.currentMonster.burnDmg, 6);
                            }
                        }
                        break;

                    case 395:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 395, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            DealSpellToMonster(t, 35 + valBonus, target, caster, true, true);
                        }
                        break;
                    }

                    case 396:
                        DealLeaderSpellDamage(target, 28 + valBonus);
                        caster.HealLeader(14 + valBonus / 2, target);
                        break;

                    case 397:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 397, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            string tr = (t.currentMonster.data != null) ? t.currentMonster.data.tribe : "";
                            bool isEvil = string.Equals(tr, "Demons", StringComparison.OrdinalIgnoreCase) || string.Equals(tr, "Ghosts", StringComparison.OrdinalIgnoreCase);
                            int dmg = (isEvil ? 32 : 18) + valBonus;
                            DealSpellToMonster(t, dmg, target, caster, false, true);
                        }
                        break;
                    }

                    case 398:
                        foreach (var es in enemiesAll)
                        {
                            if (es.IsOccupied)
                            {
                                DealSpellToMonster(es, 10 + valBonus, target, caster, false, false);
                                if (es.IsOccupied) es.currentMonster.frozenTurns = Mathf.Max(es.currentMonster.frozenTurns, 1);
                            }
                        }
                        break;

                    case 399:
                        foreach (var a in allies)
                        {
                            if (a.IsOccupied)
                            {
                                a.currentMonster.bubbleShield = false;
                                DealSpellToMonster(a, 12, caster, caster, false, false);
                            }
                        }
                        foreach (var es in enemiesAll)
                        {
                            if (es.IsOccupied)
                            {
                                es.currentMonster.bubbleShield = false;
                                DealSpellToMonster(es, 12, target, caster, false, false);
                            }
                        }
                        break;

                    case 400:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 400, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            t.ClearSlot();
                        }
                        break;
                    }

                    case 401:
                        foreach (var es in enemiesAll)
                        {
                            if (es.IsOccupied) DealSpellToMonster(es, 22 + valBonus, target, caster, false, false);
                        }
                        DealLeaderSpellDamage(target, 15 + valBonus);
                        break;

                    case 402:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 402, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            int dmg = 25 + valBonus;
                            int over = Mathf.Max(0, dmg - t.currentMonster.currentDef);
                            DealSpellToMonster(t, dmg, target, caster, false, true);
                            if (over > 0) DealLeaderSpellDamage(target, over);
                        }
                        break;
                    }

                    case 403:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 403, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            DealSpellToMonster(t, 14 + valBonus, target, caster, false, true);
                            if (!t.IsOccupied)
                            {
                                foreach (var es in GetAliveSlots(target))
                                {
                                    if (es != t && es.IsOccupied)
                                    {
                                        DealSpellToMonster(es, 14 + valBonus, target, caster, false, true);
                                        break;
                                    }
                                }
                            }
                        }
                        break;
                    }

                    case 404:
                        foreach (var es in enemiesAll)
                        {
                            if (es.IsOccupied) DealSpellToMonster(es, 30 + valBonus, target, caster, false, false);
                        }
                        break;

                    case 405:
                        foreach (var es in enemiesAll)
                        {
                            if (es.IsOccupied) DealSpellToMonster(es, 40 + valBonus, target, caster, false, false);
                        }
                        DealLeaderSpellDamage(target, 25 + valBonus);
                        break;

                    // --- 7. Stun, Freeze & Extended Silence (#406 - #420) ---
                    case 406:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 406, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            t.currentMonster.frozenTurns = Mathf.Max(t.currentMonster.frozenTurns, 1);
                            DealSpellToMonster(t, 4 + valBonus / 2, target, caster, false, true);
                        }
                        break;
                    }

                    case 407:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 407, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            t.currentMonster.frozenTurns = Mathf.Max(t.currentMonster.frozenTurns, 2);
                        }
                        break;
                    }

                    case 408:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 408, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            t.currentMonster.silencedTurns = 99;
                            t.currentMonster.bubbleShield = false;
                        }
                        break;
                    }

                    case 409:
                        foreach (var es in enemiesAll)
                        {
                            if (es.IsOccupied)
                            {
                                es.currentMonster.silencedTurns = 99;
                                es.currentMonster.bubbleShield = false;
                            }
                        }
                        break;

                    case 410:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 410, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            t.currentMonster.weakenedTurns = Mathf.Max(t.currentMonster.weakenedTurns, 2);
                            t.currentMonster.tempTurnAtk = -t.currentMonster.baseAtk;
                        }
                        break;
                    }

                    case 411:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 411, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            t.currentMonster.frozenTurns = Mathf.Max(t.currentMonster.frozenTurns, 2);
                            t.currentMonster.silencedTurns = Mathf.Max(t.currentMonster.silencedTurns, 2);
                        }
                        break;
                    }

                    case 412:
                        target.hasSacrificedThisTurn = true;
                        target.currentMana = Mathf.Max(0, target.currentMana - 2);
                        break;

                    case 413:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 413, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            var proto = t.currentMonster.data;
                            t.ClearSlot();
                            if (proto != null)
                            {
                                target.AddCardToHandWithBurnLimit(proto);
                                target.onCardDrawnToHand?.Invoke(target, proto);
                            }
                        }
                        break;
                    }

                    case 414:
                        target.denyDrawSacTurns = Mathf.Max(target.denyDrawSacTurns, 1);
                        break;

                    case 415:
                        foreach (var es in enemiesAll)
                        {
                            if (es.IsOccupied)
                            {
                                es.currentMonster.frozenTurns = Mathf.Max(es.currentMonster.frozenTurns, 1);
                                DealSpellToMonster(es, 8 + valBonus / 2, target, caster, false, false);
                            }
                        }
                        break;

                    case 416:
                        target.denyDrawSacTurns = Mathf.Max(target.denyDrawSacTurns, 1);
                        foreach (var es in enemiesAll)
                        {
                            if (es.IsOccupied) es.currentMonster.canAttack = false;
                        }
                        break;

                    case 417:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 417, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            t.currentMonster.bonusAtk = 0;
                            t.currentMonster.baseAtk = Mathf.Max(0, t.currentMonster.baseAtk - 8);
                        }
                        break;
                    }

                    case 418:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 418, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            t.currentMonster.silencedTurns = 99;
                            t.currentMonster.canAttack = false;
                        }
                        break;
                    }

                    case 419:
                    {
                        var t = ResolveTargetEnemySlot(caster, target, 419, valBonus, explicitSlot);
                        if (t != null && t.IsOccupied)
                        {
                            t.currentMonster.frozenTurns = Mathf.Max(t.currentMonster.frozenTurns, 2);
                            DealSpellToMonster(t, 12 + valBonus, target, caster, false, true);
                        }
                        break;
                    }

                    case 420:
                        foreach (var es in enemiesAll)
                        {
                            if (es.IsOccupied)
                            {
                                es.currentMonster.frozenTurns = Mathf.Max(es.currentMonster.frozenTurns, 2);
                                DealSpellToMonster(es, 10 + valBonus, target, caster, false, false);
                            }
                        }
                        break;

                    // --- 8. Lingering Buffs & Divine Wards (#421 - #440) ---
                    case 421:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 421, explicitSlot);
                        if (al != null && al.IsOccupied)
                        {
                            al.currentMonster.AddBonusDef(8 + valBonus / 2);
                            al.currentMonster.bubbleShield = true;
                        }
                        break;
                    }

                    case 422:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 422, explicitSlot);
                        if (al != null && al.IsOccupied)
                        {
                            al.currentMonster.bonusAtk += 5 + valBonus / 3;
                            al.currentMonster.AddBonusDef(8 + valBonus / 2);
                        }
                        break;
                    }

                    case 423:
                        caster.teamImmuneTurns = Mathf.Max(caster.teamImmuneTurns, 1);
                        break;

                    case 424:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 424, explicitSlot);
                        if (al != null && al.IsOccupied)
                        {
                            al.currentMonster.doubleStrike = true;
                            al.currentMonster.tempTurnAtk += 4 + valBonus / 2;
                        }
                        break;
                    }

                    case 425:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 425, explicitSlot);
                        if (al != null && al.IsOccupied)
                        {
                            al.currentMonster.AddBonusDef(20 + valBonus);
                        }
                        caster.shield += 10 + valBonus / 2;
                        break;
                    }

                    case 426:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 426, explicitSlot);
                        if (al != null && al.IsOccupied)
                        {
                            al.currentMonster.currentDef = al.currentMonster.baseDef;
                            al.currentMonster.bonusAtk += 6 + valBonus / 2;
                        }
                        break;
                    }

                    case 427:
                        foreach (var a in allies)
                        {
                            if (a.IsOccupied)
                            {
                                a.currentMonster.AddBonusDef(6 + valBonus / 3);
                                a.currentMonster.bubbleShield = true;
                            }
                        }
                        break;

                    case 428:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 428, explicitSlot);
                        if (al != null && al.IsOccupied)
                        {
                            al.currentMonster.bonusAtk += (al.currentMonster.baseAtk + al.currentMonster.bonusAtk);
                            al.currentMonster.AddBonusDef(al.currentMonster.currentDef);
                        }
                        break;
                    }

                    case 429:
                        caster.HealLeader(25 + valBonus, target);
                        foreach (var a in allies)
                        {
                            if (a.IsOccupied) a.currentMonster.AddBonusDef(4);
                        }
                        break;

                    case 430:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 430, explicitSlot);
                        if (al != null && al.IsOccupied)
                        {
                            al.currentMonster.AddBonusDef(12 + valBonus);
                        }
                        break;
                    }

                    case 431:
                        foreach (var a in allies)
                        {
                            if (a.IsOccupied)
                            {
                                a.currentMonster.bonusAtk += 4;
                                a.currentMonster.AddBonusDef(6);
                            }
                        }
                        caster.shield += 15 + valBonus;
                        break;

                    case 432:
                        foreach (var a in allies)
                        {
                            if (a.IsOccupied)
                            {
                                a.currentMonster.bonusAtk += 6;
                                a.currentMonster.canAttack = true;
                            }
                        }
                        break;

                    case 433:
                        caster.shield += 40 + valBonus;
                        caster.DrawCard();
                        break;

                    case 434:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 434, explicitSlot);
                        if (al != null && al.IsOccupied)
                        {
                            al.currentMonster.bubbleShield = true;
                            al.currentMonster.AddBonusDef(10);
                        }
                        break;
                    }

                    case 435:
                        foreach (var a in allies)
                        {
                            if (a.IsOccupied)
                            {
                                a.currentMonster.bonusAtk += 8 + valBonus / 2;
                                a.currentMonster.AddBonusDef(10 + valBonus / 2);
                            }
                        }
                        break;

                    case 436:
                        caster.HealLeader(50 + valBonus, target);
                        foreach (var a in allies)
                        {
                            if (a.IsOccupied) a.currentMonster.HealDefense(15);
                        }
                        break;

                    case 437:
                        caster.shield += 60 + valBonus;
                        foreach (var es in enemiesAll)
                        {
                            if (es.IsOccupied) es.currentMonster.baseAtk = Mathf.Max(0, es.currentMonster.baseAtk - 5);
                        }
                        break;

                    case 438:
                    {
                        var al = ResolveTargetAllySlot(caster, target, 438, explicitSlot);
                        if (al != null && al.IsOccupied)
                        {
                            al.currentMonster.bonusAtk += 15 + valBonus;
                            al.currentMonster.AddBonusDef(20 + valBonus);
                            al.currentMonster.doubleStrike = true;
                        }
                        break;
                    }

                    case 439:
                        caster.shield += 80 + valBonus;
                        foreach (var a in allies)
                        {
                            if (a.IsOccupied)
                            {
                                a.currentMonster.AddBonusDef(15);
                                a.currentMonster.bubbleShield = true;
                            }
                        }
                        break;

                    case 440:
                        foreach (var a in allies)
                        {
                            if (a.IsOccupied)
                            {
                                a.currentMonster.bonusAtk += 12 + valBonus;
                                a.currentMonster.AddBonusDef(15 + valBonus);
                                a.currentMonster.doubleStrike = true;
                            }
                        }
                        caster.HealLeader(60 + valBonus, target);
                        break;
                }
                return;
            }

            switch (spellCard.spellType)
            {
                // --- Legacy Fallbacks for any pre-#241 / synthetic test SpellTypes ---
                case SpellType.DirectDamage:
                case SpellType.ArcanePiercingShot:
                    ExecuteDirectDamageSpell(caster, target, legacyVal, spellCard.spellType == SpellType.ArcanePiercingShot);
                    break;

                case SpellType.BoardAOE:
                    ExecuteBoardAOESpell(target, legacyVal);
                    break;

                case SpellType.LeaderHeal:
                case SpellType.SupportBatteryHeal:
                    if (spellCard.spellType == SpellType.SupportBatteryHeal) caster.currentMana += 2;
                    caster.HealLeader(legacyVal, target);
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
                        slot.currentMonster.AddBonusDef(Mathf.Max(6, legacyVal / Mathf.Max(1, allies.Count)));
                    }
                    break;

                case SpellType.FreezeBoard:
                    foreach (var slot in enemiesAll)
                    {
                        slot.currentMonster.frozenTurns = Mathf.Max(slot.currentMonster.frozenTurns, 1);
                        slot.currentMonster.canAttack = false;
                        DealSpellToMonster(slot, legacyVal, target, caster, false, false);
                    }
                    break;

                case SpellType.DrawAndEnergy:
                case SpellType.DrawTwoAndStrike:
                    caster.DrawCard();
                    caster.DrawCard();
                    DealLeaderSpellDamage(target, Mathf.Max(6, legacyVal / 2));
                    break;

                case SpellType.LifeDrain:
                    DealLeaderSpellDamage(target, legacyVal);
                    caster.HealLeader(legacyVal, target);
                    break;

                case SpellType.ManaTheft:
                case SpellType.ManaStealAndAOE:
                    if (target.power > 1) target.power = Mathf.Max(1, target.power - 1);
                    caster.power++;
                    caster.currentMana++;
                    if (spellCard.spellType == SpellType.ManaStealAndAOE)
                    {
                        int aoeDmg = Mathf.Max(6, legacyVal / 3);
                        foreach (var es in enemiesAll) DealSpellToMonster(es, aoeDmg, target, caster, false, false);
                    }
                    break;

                case SpellType.ExecuteStrongest:
                    ExecuteStrongestEnemy(target);
                    break;

                case SpellType.ArcaneBlast:
                    ExecuteArcaneBlastSpell(target, legacyVal);
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
                DealSpellToMonster(bestTargetSlot, damage, target, caster, ignoreImmunity, true);
                ShowMonsterDamageVfx(bestTargetSlot, damage, target == humanPlayer);
            }
            else
            {
                DealLeaderSpellDamage(target, damage);
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
                    DealSpellToMonster(slot, damage, target, caster, false, false);
                    ShowMonsterDamageVfx(slot, damage, target == humanPlayer);
                }
            }

            if (!anyHit)
            {
                DealLeaderSpellDamage(target, damage);
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
                DealLeaderSpellDamage(target, 30);
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
                        DealSpellToMonster(slot, dmgPerMonster, target, caster, false, false);
                    }
                }
                DealLeaderSpellDamage(target, dmgPerMonster);
            }
            else
            {
                DealLeaderSpellDamage(target, totalDamage);
            }
        }

        public bool PlayEnvironmentCard(PlayerState player, CardData envCard)
        {
            if (envCard == null || envCard.cardType != CardType.Environment) return false;

            if (envCard.cost > player.currentMana)
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.LogCombatMessage($"❌ Not enough Mana to activate [{envCard.name}]! (Req: {envCard.cost}💧, Avail: {player.currentMana}💧)");
                }
                return false;
            }

            player.currentMana -= envCard.cost;
            player.hand.Remove(envCard);
            if (envCard.id < 9000) player.graveyard.Add(envCard);

            if (activeEnvironment == null)
            {
                // Neutral Arena -> Activate new Shared Environment at Level 1 (Permanent until countered)
                activeEnvironment = new EnvironmentCardData(envCard.element, 1);
                activeEnvironmentCard = activeEnvironment;
                activeEnvironmentOwner = "Shared Arena";
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowEnvironmentBanner(activeEnvironment, player.playerName, "activated");
                    BattleUIManager.Instance.LogCombatMessage($"🌍 [{player.playerName}] activated shared [{activeEnvironment.name}] (Level 1 • Permanent until countered)! {activeEnvironment.GetPerkSummary()}", activeEnvironment);
                }
            }
            else if (EnvironmentCardData.AreSameElement(activeEnvironment.buffedElement, envCard.element) ||
                     EnvironmentCardData.AreSameElement(activeEnvironment.element, envCard.element))
            {
                // Same Element from EITHER player -> Raise Level in shared slot by +1 (Level 1 -> 2 -> 3 Permanent)
                int prevLvl = activeEnvironment.level;
                activeEnvironment.UpgradeLevel();
                activeEnvironmentCard = activeEnvironment;
                activeEnvironmentOwner = "Shared Arena";
                if (BattleUIManager.Instance != null)
                {
                    string actionVerb = prevLvl >= 3 ? "reinforced" : "escalated";
                    BattleUIManager.Instance.ShowEnvironmentBanner(activeEnvironment, player.playerName, actionVerb);
                    string msg = prevLvl >= 3
                        ? $"⚡ [{player.playerName}] reinforced shared [{activeEnvironment.name}] at Level 3 (Permanent until countered)!"
                        : $"⚡ [{player.playerName}] escalated shared [{activeEnvironment.name}] from Level {prevLvl} to Level {activeEnvironment.level} (Permanent until countered)!";
                    BattleUIManager.Instance.LogCombatMessage(msg, activeEnvironment);
                }
            }
            else
            {
                // Different Element played by EITHER player against active Environment -> Downgrade active Environment by 1 level (3 -> 2 -> 1 -> Neutral)
                int prevLvl = activeEnvironment.level;
                string oldEnvName = activeEnvironment.name;
                bool clearedToNeutral = activeEnvironment.DowngradeLevel();
                if (!clearedToNeutral)
                {
                    activeEnvironmentCard = activeEnvironment;
                    activeEnvironmentOwner = "Shared Arena";
                    if (BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.ShowEnvironmentBanner(activeEnvironment, player.playerName, "weakened");
                        BattleUIManager.Instance.LogCombatMessage($"🛡️ [{player.playerName}] countered shared [{activeEnvironment.name}] with [{envCard.name}]! Dropped from Level {prevLvl} to Level {activeEnvironment.level} (Permanent until countered).", activeEnvironment);
                    }
                }
                else
                {
                    activeEnvironment = null;
                    activeEnvironmentCard = null;
                    activeEnvironmentOwner = "";
                    if (BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.ShowBanner("☀️ WEATHER NEUTRALIZED (NO ENVIRONMENT)", new Color(0.75f, 0.88f, 1f));
                        BattleUIManager.Instance.LogCombatMessage($"☀️ [{player.playerName}] countered with [{envCard.name}] and neutralized [{oldEnvName}] (Level 1 -> No Active Environment)!", envCard);
                    }
                }
            }

            // Asymmetric On-Play Environment Perks (matches Python battle_simulator_gui.py)
            var opponent = (player == humanPlayer) ? aiPlayer : humanPlayer;
            if (activeEnvironment != null && EnvironmentCardData.AreSameElement(activeEnvironment.buffedElement, envCard.element))
            {
                string ae = activeEnvironment.buffedElement;
                if (EnvironmentCardData.AreSameElement(ae, "Ice"))
                {
                    var iceTargets = new List<BoardSlot>();
                    if (opponent != null && opponent.slots != null)
                    {
                        foreach (var es in opponent.slots)
                        {
                            if (es != null && es.IsOccupied && es.currentMonster != null && es.currentMonster.HasPassiveSubstr("Immunity to Ice") == null)
                                iceTargets.Add(es);
                        }
                    }
                    if (iceTargets.Count > 0)
                    {
                        var ft = iceTargets[UnityEngine.Random.Range(0, iceTargets.Count)];
                        ft.currentMonster.frozenTurns = Mathf.Max(ft.currentMonster.frozenTurns, 1);
                        ft.currentMonster.canAttack = false;
                        CombatSystem.Instance?.DealSpellOrSkillDamage(ft.slotIndex, ft.currentMonster, 6, null, opponent, player, false, true, true, false);
                    }
                }
                else if (EnvironmentCardData.AreSameElement(ae, "Water"))
                {
                    player.HealLeader(10, opponent);
                    if (player.slots != null)
                    {
                        foreach (var al in player.slots)
                        {
                            if (al != null && al.IsOccupied && al.currentMonster != null &&
                                al.currentMonster.data != null && EnvironmentCardData.AreSameElement(al.currentMonster.data.element, "Water"))
                            {
                                al.currentMonster.burnDmg = 0;
                                al.currentMonster.weakenedTurns = 0;
                            }
                        }
                    }
                }
                else if (EnvironmentCardData.AreSameElement(ae, "Earth") || EnvironmentCardData.AreSameElement(ae, "Light"))
                {
                    player.shield += 12;
                }
                else if (EnvironmentCardData.AreSameElement(ae, "Nature"))
                {
                    player.HealLeader(8, opponent);
                }
            }

            // Cantrip: Environment cards grant +1 card draw so shifting weather never costs card advantage
            player.DrawCard();
            if (activeEnvironment != null)
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayEnvironmentActivate(activeEnvironment);
            }

            CheckGameOver();
            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.UpdateEnvironmentVisuals();
                BattleUIManager.Instance.UpdateUI();
            }

            EvaluateAutoEndTurn();
            return true;
        }

        public bool PlayLeaderCard(PlayerState player, CardData leaderCard)
        {
            if (player == null || leaderCard == null) return false;
            bool isLeader = leaderCard.cardType == CardType.Leader ||
                            string.Equals(leaderCard.card_type, "leader", StringComparison.OrdinalIgnoreCase);
            if (!isLeader) return false;

            string spiritTitle = MonsterArtLoader.FormatSpiritTitle(leaderCard);

            if (leaderCard.cost > player.currentMana)
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayInvalidAction();
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.LogCombatMessage($"❌ Not enough Mana to summon Commander Spirit [{spiritTitle}]! (Req: {leaderCard.cost}💧, Avail: {player.currentMana}💧)", leaderCard);
                }
                return false;
            }

            player.currentMana -= leaderCard.cost;
            player.hand.Remove(leaderCard);

            if (player.activeLeader != null)
            {
                player.graveyard.Add(player.activeLeader);
            }

            player.activeLeader = leaderCard;
            MonsterCardGame.Audio.SoundManager.Instance?.PlayLeaderDeploy(leaderCard);

            if (BattleUIManager.Instance != null)
            {
                string who = (player == humanPlayer) ? "You" : "Enemy";
                Color spiritColor = MonsterArtLoader.GetSpiritCategoryColor(leaderCard, player == humanPlayer);
                BattleUIManager.Instance.LogCombatMessage($"🔥 [{who}] summoned Commander Spirit [{spiritTitle}] to the Spirit Altar!", leaderCard);
                BattleUIManager.Instance.ShowBanner($"🔥 SPIRIT SUMMONED: {spiritTitle}", spiritColor);
                BattleUIManager.Instance.UpdateLeaderSlotsUI();
                if (player == humanPlayer && BattleUIManager.Instance.playerSpiritController != null)
                    BattleUIManager.Instance.playerSpiritController.TriggerSummonFlare();
                else if (player != humanPlayer && BattleUIManager.Instance.enemySpiritController != null)
                    BattleUIManager.Instance.enemySpiritController.TriggerSummonFlare();
            }

            var opponent = (player == humanPlayer) ? aiPlayer : humanPlayer;
            CommanderSpiritEffects.ApplyOnSummoned(player, opponent, leaderCard);
            CheckGameOver();

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.UpdateUI();
            }

            EvaluateAutoEndTurn();
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
                    if (slot.currentMonster.GetEffectiveAtk(activeEnvironment, humanPlayer) > 0)
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
            MonsterCardGame.Audio.SoundManager.Instance?.PlayCombatStart();

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

        /// <summary>
        /// Returns true if the human player still has at least one legal tactical action this turn:
        /// sacrificing a card, playing an affordable card with valid placement, or triggering a Ready board ability.
        /// </summary>
        public bool HasAnyRemainingPlayerAction()
        {
            if (!isPlayerTurn || currentPhase != GamePhase.ActionPhase || humanPlayer == null)
                return false;

            // 1. Sacrifice available
            if (!humanPlayer.hasSacrificedThisTurn && humanPlayer.hand != null && humanPlayer.hand.Count > 0)
                return true;

            // 2. Affordable playable card in hand
            bool hasEmptySlot = false;
            if (humanPlayer.slots != null)
            {
                foreach (var slot in humanPlayer.slots)
                {
                    if (slot != null && !slot.IsOccupied)
                    {
                        hasEmptySlot = true;
                        break;
                    }
                }
            }

            if (humanPlayer.hand != null)
            {
                foreach (var c in humanPlayer.hand)
                {
                    if (c == null) continue;
                    int effCost = humanPlayer.GetEffectiveCardCost(c);
                    if (effCost > humanPlayer.currentMana) continue;

                    if (c.cardType == CardType.Spell)
                    {
                        if (IsSpellPlayable(humanPlayer, aiPlayer, c, out _))
                            return true;
                    }
                    else if (c.cardType == CardType.Monster)
                    {
                        if (hasEmptySlot)
                            return true;
                    }
                    else
                    {
                        return true;
                    }
                }
            }

            // 3. Ready monster ability on board
            if (CombatSystem.Instance != null && humanPlayer.slots != null)
            {
                for (int i = 0; i < humanPlayer.slots.Length; i++)
                {
                    if (CombatSystem.Instance.GetAbilityState(humanPlayer, aiPlayer, i, out _, out _) == CombatSystem.AbilityReadyState.Ready)
                        return true;
                }
            }

            return false;
        }

        public void CancelAutoEndTurn()
        {
            if (autoEndTurnCoroutine != null)
            {
                StopCoroutine(autoEndTurnCoroutine);
                autoEndTurnCoroutine = null;
            }
        }

        public void EvaluateAutoEndTurn(float delay = 0.65f)
        {
            CancelAutoEndTurn();
            if (!isPlayerTurn || currentPhase != GamePhase.ActionPhase) return;
            if (!HasAnyRemainingPlayerAction())
            {
                autoEndTurnCoroutine = StartCoroutine(AutoEndTurnCoroutine(delay));
            }
        }

        private System.Collections.IEnumerator AutoEndTurnCoroutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            autoEndTurnCoroutine = null;

            if (!isPlayerTurn || currentPhase != GamePhase.ActionPhase) yield break;
            if (HasAnyRemainingPlayerAction()) yield break;

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.LogCombatMessage("⚡ No available actions remaining — ending turn automatically...");
                BattleUIManager.Instance.DeselectHandCards();
                BattleUIManager.Instance.ClearBoardSelection();
            }

            if (MonsterCardGame.Audio.SoundManager.Instance != null)
            {
                MonsterCardGame.Audio.SoundManager.Instance.PlayTurnEnd();
            }

            OnPlayerEndTurn();
        }

        public void OnPlayerEndTurn()
        {
            if (!isPlayerTurn || currentPhase != GamePhase.ActionPhase) return;
            CancelAutoEndTurn();

            bool hasReadyAttackers = false;
            foreach (var slot in humanPlayer.slots)
            {
                if (slot != null && slot.IsOccupied && slot.currentMonster != null && slot.currentMonster.canAttack && slot.currentMonster.frozenTurns <= 0)
                {
                    if (slot.currentMonster.GetEffectiveAtk(activeEnvironment, humanPlayer) > 0)
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
                MonsterCardGame.Audio.SoundManager.Instance?.PlayCombatStart();

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
            if (!isPlayerTurn)
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayTurnEnd();
            }
            CancelAutoEndTurn();
            currentPhase = GamePhase.TurnEnd;

            if (activeEnvironment != null)
            {
                // Environment effects are permanent across rounds/turns.
                // They do NOT expire by turn countdown; they persist until countered by another environment or downgraded.
                activeEnvironment.TickTurn();
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
                MonsterCardGame.Audio.SoundManager.Instance?.PlayVictory();
                string envName = activeEnvironment != null ? activeEnvironment.name : "Stable Weather ☀️";
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowGameOverScreen(true, currentTurn, humanPlayer.currentHp, aiPlayer.currentHp,
                        humanPlayer.monstersSummonedTotal, playerSpellsCastCount, envName, humanPlayer.power);
                }
            }
            else if (!humanPlayer.IsAlive)
            {
                currentPhase = GamePhase.GameOver;
                MonsterCardGame.Audio.SoundManager.Instance?.PlayDefeat();
                string envName = activeEnvironment != null ? activeEnvironment.name : "Stable Weather ☀️";
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowGameOverScreen(false, currentTurn, humanPlayer.currentHp, aiPlayer.currentHp,
                        humanPlayer.monstersSummonedTotal, playerSpellsCastCount, envName, humanPlayer.power);
                }
            }
        }

        public void RestartGame()
        {
            CancelAutoEndTurn();
            for (int i = 0; i < humanPlayer.slots.Length; i++)
            {
                if (humanPlayer.slots[i] != null) humanPlayer.slots[i].ClearSlot();
                if (aiPlayer.slots[i] != null) aiPlayer.slots[i].ClearSlot();
            }

            playerSpellsCastCount = 0;
            activeEnvironment = null;
            activeEnvironmentCard = null;
            activeEnvironmentOwner = "";
            if (humanPlayer != null) humanPlayer.activeLeader = null;
            if (aiPlayer != null) aiPlayer.activeLeader = null;

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.ClearCombatLog();
                BattleUIManager.Instance.ClearFlightAnimations();
                if (BattleUIManager.Instance.gameOverPanel != null)
                {
                    BattleUIManager.Instance.gameOverPanel.SetActive(false);
                }
                BattleUIManager.Instance.DeselectHandCards();
                BattleUIManager.Instance.ClearBoardSelection();
            }

            AdvanceToNextOpponent(); // Automatically alternate between Gora and Navi after each game!
            StartGame();
        }
    }
}

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

        [Header("Database (10,000 Cards: 7,200 Monsters + 2,788 Spells + 12 Environments)")]
        public TextAsset monstersJsonFile;
        public List<MonsterData> allMonsters = new List<MonsterData>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            LoadMonsterDatabase();
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
                            allSpells.Add(c);
                        }
                    }

                    SpellDatabase.SetSpellsFromDatabase(allSpells);
                    Debug.Log($"✅ Loaded 10,000-Card Database: {allMonsters.Count} Monsters + {allSpells.Count} Tactical Spells!");
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

        private List<CardData> GetCustomArtPriorityCards()
        {
            var result = new List<CardData>();
            HashSet<int> customArtIds = MonsterArtLoader.GetAvailableCustomArtCardIds();
            if (customArtIds == null || customArtIds.Count == 0) return result;

            var addedIds = new HashSet<int>();

            // 1. Prioritize all monsters that have custom artwork files
            if (allMonsters != null)
            {
                foreach (var m in allMonsters)
                {
                    if (m != null && customArtIds.Contains(m.id) && addedIds.Add(m.id))
                    {
                        result.Add(m);
                    }
                }
            }

            // 2. Also include any spells that have custom artwork files
            var allSpells = SpellDatabase.GetAllSpells();
            if (allSpells != null)
            {
                foreach (var sp in allSpells)
                {
                    if (sp != null && customArtIds.Contains(sp.id) && addedIds.Add(sp.id))
                    {
                        result.Add(sp);
                    }
                }
            }

            return result;
        }

        public void StartGame()
        {
            var deckA = GenerateDeck(30);
            var deckB = GenerateDeck(30);

            humanPlayer.InitializeDeck(deckA);
            aiPlayer.InitializeDeck(deckB);

            for (int i = 0; i < 5; i++)
            {
                humanPlayer.DrawCard();
                aiPlayer.DrawCard();
            }

            // Automatically detect ALL cards/monsters that have custom artwork and prioritize them in the opening hand & top of deck
            List<CardData> priorityArtCards = GetCustomArtPriorityCards();

            isPlayerTurn = true;
            currentTurn = 1;
            activeEnvironment = null;

            int maxPriorityCost = 0;
            for (int i = 0; i < Mathf.Min(priorityArtCards.Count, 6); i++)
            {
                if (priorityArtCards[i] != null && priorityArtCards[i].cost > maxPriorityCost)
                {
                    maxPriorityCost = priorityArtCards[i].cost;
                }
            }

            if (maxPriorityCost > 0)
            {
                humanPlayer.manaCapacity = Mathf.Max(humanPlayer.manaCapacity, maxPriorityCost);
                humanPlayer.currentPower = humanPlayer.manaCapacity;
            }

            Debug.Log($"🎲 Epic Battle Started (Unlimited Mana & Power ∞)! Custom Art Priority Cards Found: {priorityArtCards.Count}");

            StartTurn();

            if (priorityArtCards.Count > 0)
            {
                // Place up to 5 custom-art cards directly in the opening hand, and any remaining at top of deck
                int handSlotsForArt = Mathf.Min(priorityArtCards.Count, 5);
                for (int i = 0; i < handSlotsForArt; i++)
                {
                    CardData artCard = priorityArtCards[i];
                    humanPlayer.hand.RemoveAll(c => c != null && c.id == artCard.id);
                    if (i < humanPlayer.hand.Count)
                    {
                        humanPlayer.hand[i] = artCard;
                    }
                    else
                    {
                        humanPlayer.hand.Add(artCard);
                    }
                }

                // Put any additional custom-art cards (6th+) at the very top of the player's draw deck
                for (int i = priorityArtCards.Count - 1; i >= handSlotsForArt; i--)
                {
                    CardData artCard = priorityArtCards[i];
                    humanPlayer.deck.RemoveAll(c => c != null && c.id == artCard.id);
                    humanPlayer.deck.Insert(0, artCard);
                }
            }

            // Always place Fire Environment card in hand slot 5 (6th card) so the user can easily test activating & upgrading Fire
            var fireEnvCard = new EnvironmentCardData("Fire", 1);
            if (humanPlayer.hand.Count > 5) humanPlayer.hand[5] = fireEnvCard;
            else humanPlayer.hand.Add(fireEnvCard);

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.UpdateUI();
                if (priorityArtCards.Count > 0)
                {
                    BattleUIManager.Instance.ShowCardDetails(priorityArtCards[0]);
                }
            }
        }

        public List<CardData> GenerateDeck(int count)
        {
            var deck = new List<CardData>();
            if (allMonsters == null || allMonsters.Count == 0) return deck;

            // 1. 18 Random Monsters out of 7,200 (60% of deck)
            int monsterCount = Mathf.Min(18, count);
            for (int i = 0; i < monsterCount; i++)
            {
                var randomMonster = allMonsters[UnityEngine.Random.Range(0, allMonsters.Count)];
                deck.Add(randomMonster);
            }

            // 2. 10 Intermediary Tactical Spells out of 2,788 (33.3% of deck)
            for (int i = 0; i < 10; i++)
            {
                deck.Add(SpellDatabase.GetRandomSpell());
            }

            // 3. 2 Global Environment Cards out of 12 (6.7% of deck)
            for (int i = 0; i < 2; i++)
            {
                deck.Add(EnvironmentCardData.GetRandomEnvironment());
            }

            for (int i = 0; i < deck.Count; i++)
            {
                int r = UnityEngine.Random.Range(i, deck.Count);
                var temp = deck[i];
                deck[i] = deck[r];
                deck[r] = temp;
            }

            return deck;
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

            if (CombatSystem.Instance != null)
            {
                CombatSystem.Instance.ExecuteActiveSkills(humanPlayer, aiPlayer);
            }

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

            if (CombatSystem.Instance != null)
            {
                CombatSystem.Instance.ExecuteActiveSkills(humanPlayer, aiPlayer);
            }

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
                    BattleUIManager.Instance.UpdateUI();
                }
                StartTurn();
            }
        }

        private void CheckGameOver()
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
            }

            StartGame();
        }
    }
}

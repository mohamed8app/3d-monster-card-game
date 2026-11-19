using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Core
{
    public enum AICombatStyle
    {
        Standard,
        GoraAggressive,
        NaviTactical
    }

    public class AIOpponent : MonoBehaviour
    {
        public PlayerState aiPlayer;
        public PlayerState humanPlayer;
        public AICombatStyle combatStyle = AICombatStyle.GoraAggressive;

        public void PlayTurn(int currentTurn, EnvironmentCardData activeEnv, Action onTurnFinished)
        {
            if (!aiPlayer.IsAlive) return;
            StartCoroutine(PlayTurnCoroutine(currentTurn, activeEnv, onTurnFinished));
        }

        private IEnumerator PlayTurnCoroutine(int currentTurn, EnvironmentCardData activeEnv, Action onTurnFinished)
        {
            string brainName = (combatStyle == AICombatStyle.GoraAggressive) ? "Gora (Aggressive Combat AI)" : "Navi (Tactical Control AI)";
            Debug.Log($"🤖 [{brainName}] is calculating tactical moves...");
            yield return new WaitForSeconds(0.55f);

            // 1. Tactical Sacrifice (+1 Power & +1 Mana) with smooth card flight from hand to Enemy Power bar
            yield return DecideSacrificeCoroutine();

            // 2. Activate Environment Weather with visible flip & flight to Shared Environment Slot
            yield return DecideEnvironmentCoroutine();

            // 2.5 Summon Commander Spirit Medallion onto the Enemy Spirit Altar (empowers turn & army)
            yield return DecideCommanderSpiritCoroutine();
            if (!aiPlayer.IsAlive || !humanPlayer.IsAlive) yield break;

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

            // Style adaptation: Gora ramps aggressively, Navi preserves card resources
            if (combatStyle == AICombatStyle.GoraAggressive && aiPlayer.power < 4 && aiPlayer.hand.Count >= 3)
            {
                needsMana = true;
            }
            if (combatStyle == AICombatStyle.NaviTactical && aiPlayer.hand.Count < 5 && aiPlayer.power >= 2)
            {
                needsMana = false;
            }

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

        private float EvaluateEnvironmentScoreForAI(string envElement)
        {
            if (string.IsNullOrEmpty(envElement) || aiPlayer == null) return 0f;

            var probe = new EnvironmentCardData(envElement, 1);
            float score = 0f;

            if (aiPlayer.slots != null)
            {
                foreach (var s in aiPlayer.slots)
                {
                    if (s == null || !s.IsOccupied || s.currentMonster == null || s.currentMonster.data == null)
                        continue;
                    string el = s.currentMonster.data.element;
                    if (EnvironmentCardData.AreSameElement(probe.buffedElement, el)) score += 2.4f;
                    else if (probe.IsDebuffedElement(el)) score -= 2.0f;
                }
            }

            if (aiPlayer.hand != null)
            {
                foreach (var hc in aiPlayer.hand)
                {
                    if (hc == null) continue;
                    if (hc.cardType == CardType.Monster && aiPlayer.GetEffectiveCardCost(hc) <= aiPlayer.currentMana)
                    {
                        if (EnvironmentCardData.AreSameElement(probe.buffedElement, hc.element)) score += 1.5f;
                        else if (probe.IsDebuffedElement(hc.element)) score -= 1.0f;
                    }
                    else if (EnvironmentCardData.AreSameElement(envElement, "Desert") && hc.cardType == CardType.Monster && string.Equals(hc.element, "Desert", StringComparison.OrdinalIgnoreCase))
                    {
                        score += 1.4f;
                    }
                }
            }

            if (humanPlayer != null && humanPlayer.slots != null)
            {
                foreach (var es in humanPlayer.slots)
                {
                    if (es == null || !es.IsOccupied || es.currentMonster == null || es.currentMonster.data == null)
                        continue;
                    string el = es.currentMonster.data.element;
                    if (probe.IsDebuffedElement(el)) score += 1.9f;
                    else if (EnvironmentCardData.AreSameElement(probe.buffedElement, el)) score -= 1.8f;
                }
            }

            if (combatStyle == AICombatStyle.GoraAggressive && (EnvironmentCardData.AreSameElement(envElement, "Fire") || EnvironmentCardData.AreSameElement(envElement, "Dark") || EnvironmentCardData.AreSameElement(envElement, "Electric")))
            {
                score += 1.8f;
            }
            if (combatStyle == AICombatStyle.NaviTactical && (EnvironmentCardData.AreSameElement(envElement, "Water") || EnvironmentCardData.AreSameElement(envElement, "Ice") || EnvironmentCardData.AreSameElement(envElement, "Earth")))
            {
                score += 1.8f;
            }

            return score;
        }

        private IEnumerator DecideEnvironmentCoroutine()
        {
            if (GameManager.Instance == null) yield break;

            int safety = 0;
            while (safety < 4)
            {
                safety++;
                CardData chosenEnv = null;
                var activeEnv = GameManager.Instance.activeEnvironment;

                if (activeEnv == null)
                {
                    float bestScore = float.MinValue;
                    foreach (var card in aiPlayer.hand)
                    {
                        if (card == null || card.cardType != CardType.Environment || card.cost > aiPlayer.currentMana)
                            continue;
                        float s = EvaluateEnvironmentScoreForAI(card.element);
                        if (s >= 0.4f && s > bestScore)
                        {
                            bestScore = s;
                            chosenEnv = card;
                        }
                    }
                }
                else
                {
                    float activeScore = EvaluateEnvironmentScoreForAI(activeEnv.element);

                    // 1. Prefer escalating the active shared environment if it benefits the AI
                    if (activeScore >= 0.4f && activeEnv.level < 3)
                    {
                        foreach (var card in aiPlayer.hand)
                        {
                            if (card == null || card.cardType != CardType.Environment || card.cost > aiPlayer.currentMana)
                                continue;
                            if (EnvironmentCardData.AreSameElement(activeEnv.buffedElement, card.element) ||
                                EnvironmentCardData.AreSameElement(activeEnv.element, card.element))
                            {
                                chosenEnv = card;
                                break;
                            }
                        }
                    }

                    // 2. Otherwise counter/downgrade an unfavorable active environment neutrally (no owner bias)
                    if (chosenEnv == null && activeScore <= -0.8f)
                    {
                        float bestCounterPriority = float.MinValue;
                        foreach (var card in aiPlayer.hand)
                        {
                            if (card == null || card.cardType != CardType.Environment || card.cost > aiPlayer.currentMana)
                                continue;
                            if (EnvironmentCardData.AreSameElement(activeEnv.buffedElement, card.element) ||
                                EnvironmentCardData.AreSameElement(activeEnv.element, card.element))
                                continue;

                            // Prefer spending the lowest-value environment card in hand to downgrade unfavorable weather
                            float keepScore = EvaluateEnvironmentScoreForAI(card.element);
                            float priority = -keepScore;
                            if (priority > bestCounterPriority)
                            {
                                bestCounterPriority = priority;
                                chosenEnv = card;
                            }
                        }
                    }
                }

                if (chosenEnv == null) break;

                var ui = MonsterCardGame.UI.BattleUIManager.Instance;
                if (ui != null)
                {
                    yield return ui.AnimateEnemyCardPlayRoutine(chosenEnv, ui.GetEnvironmentTargetWorldPos(), 0.50f, 0.08f, 0.85f);
                }

                if (!GameManager.Instance.PlayEnvironmentCard(aiPlayer, chosenEnv))
                    break;

                if (ui != null) ui.UpdateUI();
                yield return new WaitForSeconds(0.35f);
            }
        }

        private IEnumerator DecideCommanderSpiritCoroutine()
        {
            if (GameManager.Instance == null || aiPlayer == null || aiPlayer.hand == null) yield break;

            CardData chosenSpirit = null;
            float bestVal = -9999f;
            int enemyCount = 0;
            if (humanPlayer != null && humanPlayer.slots != null)
            {
                foreach (var es in humanPlayer.slots)
                {
                    if (es != null && es.IsOccupied) enemyCount++;
                }
            }

            foreach (var card in new List<CardData>(aiPlayer.hand))
            {
                if (card == null || card.cardType != CardType.Leader || card.cost > aiPlayer.currentMana)
                    continue;
                if (aiPlayer.activeLeader != null && card.id == aiPlayer.activeLeader.id)
                    continue;

                int clanBoard = 0;
                if (aiPlayer.slots != null)
                {
                    foreach (var al in aiPlayer.slots)
                    {
                        if (al != null && al.IsOccupied && al.currentMonster != null && al.currentMonster.data != null &&
                            string.Equals(al.currentMonster.data.tribe, card.tribe, System.StringComparison.OrdinalIgnoreCase))
                        {
                            clanBoard++;
                        }
                    }
                }
                int clanHand = 0;
                foreach (var hc in aiPlayer.hand)
                {
                    if (hc != null && hc.cardType == CardType.Monster &&
                        string.Equals(hc.tribe, card.tribe, System.StringComparison.OrdinalIgnoreCase))
                    {
                        clanHand++;
                    }
                }

                float val = 10f + clanBoard * 9f + clanHand * 3f + enemyCount * 1.8f + card.cost * 1.5f;
                int id = card.id;
                if (id == 451 || id == 453 || id == 455 || id == 456 || id == 457 || id == 458 || id == 464 || id == 467 || id == 468)
                {
                    val += 6f;
                }
                if (combatStyle == AICombatStyle.GoraAggressive && (string.Equals(card.tribe, "Warriors", StringComparison.OrdinalIgnoreCase) || string.Equals(card.tribe, "Demons", StringComparison.OrdinalIgnoreCase) || string.Equals(card.tribe, "Beasts", StringComparison.OrdinalIgnoreCase)))
                {
                    val += 6f;
                }
                if (combatStyle == AICombatStyle.NaviTactical && (string.Equals(card.tribe, "Celestials", StringComparison.OrdinalIgnoreCase) || string.Equals(card.tribe, "Mages", StringComparison.OrdinalIgnoreCase) || string.Equals(card.tribe, "Dwarves", StringComparison.OrdinalIgnoreCase)))
                {
                    val += 6f;
                }
                if (aiPlayer.activeLeader != null)
                {
                    int curClan = 0;
                    if (aiPlayer.slots != null)
                    {
                        foreach (var al in aiPlayer.slots)
                        {
                            if (al != null && al.IsOccupied && al.currentMonster != null && al.currentMonster.data != null &&
                                string.Equals(al.currentMonster.data.tribe, aiPlayer.activeLeader.tribe, System.StringComparison.OrdinalIgnoreCase))
                            {
                                curClan++;
                            }
                        }
                    }
                    val -= (14f + curClan * 8f);
                }

                if (val > 8f && val > bestVal)
                {
                    bestVal = val;
                    chosenSpirit = card;
                }
            }

            if (chosenSpirit != null)
            {
                var ui = MonsterCardGame.UI.BattleUIManager.Instance;
                if (ui != null)
                {
                    yield return ui.AnimateEnemyCardPlayRoutine(chosenSpirit, ui.GetEnemyLeaderTargetWorldPos(), 0.55f, 0.08f, 0.90f);
                }

                GameManager.Instance.PlayLeaderCard(aiPlayer, chosenSpirit);
                if (ui != null) ui.UpdateUI();
                yield return new WaitForSeconds(0.40f);
            }
        }

        private bool ShouldCastSpellForAI(CardData sp)
        {
            if (GameManager.Instance == null || !GameManager.Instance.IsSpellPlayable(aiPlayer, humanPlayer, sp, out _))
                return false;

            int cid = sp.id;
            var allies = new List<BoardSlot>();
            if (aiPlayer.slots != null)
            {
                foreach (var s in aiPlayer.slots)
                {
                    if (s != null && s.IsOccupied && s.currentMonster != null) allies.Add(s);
                }
            }
            var enemiesAll = new List<BoardSlot>();
            var validEnemies = new List<BoardSlot>();
            if (humanPlayer != null && humanPlayer.slots != null)
            {
                foreach (var s in humanPlayer.slots)
                {
                    if (s != null && s.IsOccupied && s.currentMonster != null)
                    {
                        enemiesAll.Add(s);
                        if (s.currentMonster.HasPassiveSubstr("Night Stealth") == null)
                            validEnemies.Add(s);
                    }
                }
            }

            int gyMonsters = 0;
            if (aiPlayer.graveyard != null)
            {
                foreach (var g in aiPlayer.graveyard)
                {
                    if (g != null && g.cardType == CardType.Monster && g.id < 9000) gyMonsters++;
                }
            }

            if (cid == 241) return aiPlayer.currentHp > 20 && aiPlayer.power < 10;
            if (cid == 245) return allies.Count >= 2 && aiPlayer.power < 9;
            if (cid == 246) return aiPlayer.currentHp > 110 && aiPlayer.hand.Count < 8 && aiPlayer.deck.Count > 0;
            if (cid == 248) return gyMonsters > 0;
            if (cid == 249) return gyMonsters > 0 || aiPlayer.currentHp < aiPlayer.maxHp * 0.70f;
            if (cid == 251) return gyMonsters >= 2;
            if (cid == 252) return aiPlayer.hasSacrificedThisTurn && aiPlayer.hand.Count >= 2;
            if (cid == 261)
            {
                foreach (var e in enemiesAll) if (e.currentMonster.data != null && e.currentMonster.data.cost >= 4) return true;
                return false;
            }
            if (cid == 264 || cid == 297 || cid == 299)
                return enemiesAll.Count >= 2 || (cid == 299 && humanPlayer.currentHp <= 15);
            if (cid == 267)
            {
                foreach (var a in allies)
                {
                    bool blocked = humanPlayer.slots[a.slotIndex] != null && humanPlayer.slots[a.slotIndex].IsOccupied;
                    if ((blocked && a.currentMonster.canAttack) || a.currentMonster.currentDef <= 12) return true;
                }
                return false;
            }
            if (cid == 271)
            {
                foreach (var a in allies)
                {
                    var m = a.currentMonster;
                    if (m.currentDef < m.baseDef || m.burnDmg > 0 || m.poisonDmg > 0 || m.frozenTurns > 0 || m.weakenedTurns > 0 || m.silencedTurns > 0)
                        return true;
                }
                return false;
            }
            if (cid == 272)
            {
                foreach (var a in allies) if (a.currentMonster.frozenTurns > 0 || !a.currentMonster.canAttack) return true;
                return false;
            }
            if (cid == 276) return aiPlayer.currentHp <= aiPlayer.maxHp - 25;
            if (cid == 277)
            {
                if (allies.Count >= 2) return true;
                foreach (var a in allies)
                {
                    var m = a.currentMonster;
                    if (m.burnDmg > 0 || m.poisonDmg > 0 || m.frozenTurns > 0 || m.weakenedTurns > 0 || m.silencedTurns > 0)
                        return true;
                }
                return false;
            }
            if (cid == 282)
            {
                foreach (var a in allies) if (a.currentMonster.currentDef - (a.currentMonster.baseAtk + a.currentMonster.bonusAtk) >= 8) return true;
                foreach (var e in validEnemies) if ((e.currentMonster.baseAtk + e.currentMonster.bonusAtk) - e.currentMonster.currentDef >= 8) return true;
                return false;
            }
            if (cid == 283)
            {
                foreach (var a in allies) if (a.currentMonster.currentDef >= 14) return true;
                return false;
            }
            if (cid == 284)
            {
                foreach (var a in allies) if (a.currentMonster.silencedTurns <= 0 && a.currentMonster.actives != null && a.currentMonster.actives.Count > 0) return true;
                return false;
            }
            if (cid == 285)
            {
                foreach (var a in allies) if (a.currentMonster.silencedTurns <= 0 && a.currentMonster.actives != null && a.currentMonster.actives.Count > 0 && !a.currentMonster.hasUsedActiveThisTurn) return true;
                return false;
            }
            if (cid == 287)
            {
                var allBoard = new List<MonsterInstance>();
                foreach (var a in allies) allBoard.Add(a.currentMonster);
                foreach (var e in enemiesAll) allBoard.Add(e.currentMonster);
                if (allBoard.Count == 0) return false;
                int topAtk = 0;
                foreach (var m in allBoard) topAtk = Mathf.Max(topAtk, m.baseAtk + m.bonusAtk + m.tempTurnAtk);
                foreach (var a in allies) if (topAtk - (a.currentMonster.baseAtk + a.currentMonster.bonusAtk + a.currentMonster.tempTurnAtk) >= 6) return true;
                return false;
            }
            if (cid == 292)
            {
                foreach (var a in allies)
                {
                    bool open = humanPlayer.slots[a.slotIndex] == null || !humanPlayer.slots[a.slotIndex].IsOccupied;
                    if (a.currentMonster.currentDef >= 10 && (a.currentMonster.canAttack || open)) return true;
                }
                return false;
            }
            if (sp.spellType == SpellType.LeaderHeal && aiPlayer.currentHp > (aiPlayer.maxHp * 0.8f))
                return false;

            return true;
        }

        private void PickTargetsForSpell(CardData spell, out int primarySlot, out int secondarySlot, out bool primaryIsAlly)
        {
            primarySlot = -1;
            secondarySlot = -1;
            primaryIsAlly = false;
            if (GameManager.Instance == null || spell == null) return;

            SpellDatabase.NormalizeSpellMetadata(spell);
            SpellTargetMode tmode = SpellDatabase.GetTargetMode(spell.id);
            if (tmode == SpellTargetMode.Enemy)
            {
                primarySlot = GameManager.Instance.PickSpellEnemySlot(aiPlayer, humanPlayer, spell.id);
            }
            else if (tmode == SpellTargetMode.Ally)
            {
                primarySlot = GameManager.Instance.PickSpellAllySlot(aiPlayer, humanPlayer, spell.id);
                primaryIsAlly = true;
            }
            else if (tmode == SpellTargetMode.Any)
            {
                int bestAlSlot = -1, bestAlSc = int.MinValue;
                if (aiPlayer.slots != null)
                {
                    foreach (var s in aiPlayer.slots)
                    {
                        if (s != null && s.IsOccupied && s.currentMonster != null)
                        {
                            int sc = s.currentMonster.currentDef - (s.currentMonster.baseAtk + s.currentMonster.bonusAtk);
                            if (sc > bestAlSc) { bestAlSc = sc; bestAlSlot = s.slotIndex; }
                        }
                    }
                }
                int bestEnSlot = -1, bestEnSc = int.MinValue;
                foreach (var e in GameManager.Instance.GetValidSpellEnemies(humanPlayer))
                {
                    int sc = (e.currentMonster.baseAtk + e.currentMonster.bonusAtk) - e.currentMonster.currentDef;
                    if (sc > bestEnSc) { bestEnSc = sc; bestEnSlot = e.slotIndex; }
                }
                if (bestAlSlot >= 0 && (bestEnSlot < 0 || bestAlSc >= bestEnSc))
                {
                    primarySlot = bestAlSlot;
                    primaryIsAlly = true;
                }
                else
                {
                    primarySlot = bestEnSlot;
                    primaryIsAlly = false;
                }
            }
            else if (tmode == SpellTargetMode.AllyToEmptySlot)
            {
                var empSlots = new List<int>();
                var unblockedEmp = new List<int>();
                for (int i = 0; i < 6; i++)
                {
                    if (aiPlayer.slots[i] != null && !aiPlayer.slots[i].IsOccupied)
                    {
                        empSlots.Add(i);
                        if (humanPlayer.slots[i] == null || !humanPlayer.slots[i].IsOccupied) unblockedEmp.Add(i);
                    }
                }
                if (empSlots.Count > 0)
                {
                    int bestKey = int.MinValue;
                    for (int i = 0; i < 6; i++)
                    {
                        var s = aiPlayer.slots[i];
                        if (s == null || !s.IsOccupied || s.currentMonster == null) continue;
                        bool blocked = humanPlayer.slots[i] != null && humanPlayer.slots[i].IsOccupied;
                        int want = (s.currentMonster.canAttack && blocked && unblockedEmp.Count > 0) ? 10000 : 0;
                        int sc = want + s.currentMonster.baseAtk + s.currentMonster.bonusAtk;
                        if (sc > bestKey) { bestKey = sc; primarySlot = i; }
                    }
                    if (primarySlot >= 0)
                    {
                        primaryIsAlly = true;
                        var m = aiPlayer.slots[primarySlot].currentMonster;
                        secondarySlot = (unblockedEmp.Count > 0 && (m.canAttack || m.currentDef > 12)) ? unblockedEmp[0] : empSlots[0];
                    }
                }
            }
        }

        private IEnumerator DecideSpellsCoroutine()
        {
            if (GameManager.Instance == null) yield break;

            var spellCards = new List<CardData>();
            foreach (var c in aiPlayer.hand)
            {
                if (c != null && c.cardType == CardType.Spell && c.cost <= aiPlayer.currentMana)
                {
                    spellCards.Add(c);
                }
            }

            foreach (var spell in spellCards)
            {
                if (!aiPlayer.IsAlive || !humanPlayer.IsAlive) yield break;
                if (!aiPlayer.hand.Contains(spell) || spell.cost > aiPlayer.currentMana) continue;
                if (!ShouldCastSpellForAI(spell)) continue;

                PickTargetsForSpell(spell, out int priSlot, out int secSlot, out bool priIsAlly);

                var ui = MonsterCardGame.UI.BattleUIManager.Instance;
                if (ui != null)
                {
                    yield return ui.AnimateEnemyCardPlayRoutine(spell, ui.GetCenterArenaWorldPos(), 0.48f, 0.18f, 1.12f);
                }

                if (GameManager.Instance.CastSpell(aiPlayer, humanPlayer, spell, priSlot, secSlot, priIsAlly))
                {
                    if (ui != null) ui.UpdateUI();
                    yield return new WaitForSeconds(0.35f);
                }
            }
        }

        private IEnumerator DecideSummonsCoroutine(int currentTurn)
        {
            var monsterCards = new List<CardData>();
            foreach (var c in aiPlayer.hand)
            {
                if (c != null && c.cardType == CardType.Monster) monsterCards.Add(c);
            }

            monsterCards.Sort((a, b) => (b.cost + b.atk + b.def).CompareTo(a.cost + a.atk + a.def));

            foreach (var card in monsterCards)
            {
                if (!aiPlayer.IsAlive || !humanPlayer.IsAlive) yield break;
                int effCost = aiPlayer.GetEffectiveCardCost(card);
                if (effCost <= aiPlayer.currentMana)
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
                            ui.LogCombatMessage($"👹 Enemy summoned [{card.name}] (-{effCost} Mana | Left: {aiPlayer.currentMana}).", card);
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

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;
using MonsterCardGame.UI;

namespace MonsterCardGame.Core
{
    public class CombatSystem : MonoBehaviour
    {
        public static CombatSystem Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        #region Passives & Active Skills Engine (Synced 100% with Simulator)

        public void ApplyOnSummonPassives(PlayerState player, PlayerState opponent, int slot, MonsterInstance m)
        {
            if (m == null || opponent == null) return;

            var oppSlot = (slot >= 0 && slot < 6) ? opponent.slots[slot] : null;
            var oppM = (oppSlot != null && oppSlot.IsOccupied) ? oppSlot.currentMonster : null;

            if (oppM != null && oppM.HasPassiveSubstr("Light of Truth") != null)
            {
                m.silencedTurns = Mathf.Max(m.silencedTurns, 2);
                return;
            }

            foreach (var p in m.passives)
            {
                if (p.Contains("Light of Truth") && oppM != null)
                {
                    oppM.silencedTurns = Mathf.Max(oppM.silencedTurns, 3);
                }
                else if (p.Contains("Granite Armor") && (slot == 2 || slot == 3))
                {
                    m.AddBonusDef(m.baseDef);
                }
                else if (p.Contains("Ice Structure") && (slot == 2 || slot == 3))
                {
                    m.AddBonusDef(5);
                }
                else if (p.Contains("Pure Radiance") && (slot == 2 || slot == 3))
                {
                    m.bonusAtk += 3;
                }
                else if (p.Contains("Flank Strike") && (slot == 0 || slot == 1 || slot == 4 || slot == 5))
                {
                    m.bonusAtk += 4;
                }
                else if (p.Contains("Swift Speed") && (slot == 0 || slot == 1 || slot == 4 || slot == 5))
                {
                    m.bonusAtk += 5;
                }
                else if (p.Contains("Quick Charge"))
                {
                    m.bonusAtk += 4;
                    m.canAttack = true;
                }
                else if (p.Contains("Fire Blaze"))
                {
                    int boosted = 0;
                    foreach (int adj in new[] { slot - 1, slot + 1 })
                    {
                        if (adj >= 0 && adj < 6 && player.slots[adj].IsOccupied)
                        {
                            player.slots[adj].currentMonster.bonusAtk += 3;
                            boosted++;
                        }
                    }
                    if (boosted == 0) m.bonusAtk += 3;
                }
                else if (p.Contains("High Tide"))
                {
                    foreach (var s in player.slots)
                    {
                        if (s != null && s.IsOccupied && s.currentMonster.data.element == "Water")
                        {
                            s.currentMonster.AddBonusDef(4);
                        }
                    }
                }
                else if (p.Contains("Ocean Protection"))
                {
                    MonsterInstance target = null;
                    int minDef = int.MaxValue;
                    for (int i = 0; i < 6; i++)
                    {
                        if (i != slot && player.slots[i].IsOccupied && player.slots[i].currentMonster.currentDef < minDef)
                        {
                            minDef = player.slots[i].currentMonster.currentDef;
                            target = player.slots[i].currentMonster;
                        }
                    }
                    if (target != null) target.AddBonusDef(15);
                    else m.AddBonusDef(10);
                }
                else if (p.Contains("War Fusion"))
                {
                    int mechas = 0;
                    for (int i = 0; i < 6; i++)
                    {
                        if (player.slots[i].IsOccupied && player.slots[i].currentMonster.data.element == "Mecha")
                            mechas++;
                    }
                    m.AddBonusDef(Mathf.Max(1, mechas) * 8);
                }
                else if (p.Contains("Nourishing Roots"))
                {
                    player.manaCapacity++;
                    player.currentPower++;
                }
                else if (p.Contains("Psychic Drain"))
                {
                    int drained = Mathf.Min(2, Mathf.Max(0, opponent.manaCapacity - 1));
                    opponent.manaCapacity -= drained;
                }
                else if (p.Contains("Black Nightmare"))
                {
                    if (opponent.hand.Count > 0)
                    {
                        int lastIdx = opponent.hand.Count - 1;
                        var sac = opponent.hand[lastIdx];
                        opponent.hand.RemoveAt(lastIdx);
                        opponent.graveyard.Add(sac);
                    }
                    else
                    {
                        opponent.TakeLeaderDamage(12);
                    }
                }
                else if (p.Contains("Tidal Bounce") && oppM != null && (oppM.baseAtk + oppM.bonusAtk) < 14)
                {
                    if (oppM.HasPassiveSubstr("Earth Steadfastness") == null)
                    {
                        if (opponent.hand.Count < 10) opponent.hand.Add(oppM.data);
                        oppSlot.ClearSlot();
                    }
                }
            }
        }

        public void TriggerStartOfTurnPassives(PlayerState player, PlayerState opponent)
        {
            for (int slot = 0; slot < 6; slot++)
            {
                var pSlot = player.slots[slot];
                if (pSlot == null || !pSlot.IsOccupied) continue;
                var m = pSlot.currentMonster;

                if (m.burnDmg > 0)
                {
                    if (m.HasPassiveSubstr("Moisture Shield") != null || m.HasPassiveSubstr("Total Immunity to Fire") != null || m.HasPassiveSubstr("Immunity to Poison and Burn") != null)
                        m.burnDmg = 0;
                    else
                        m.currentDef -= m.burnDmg;
                }

                if (m.poisonDmg > 0)
                {
                    if (m.HasPassiveSubstr("Immunity to Poison") != null || m.HasPassiveSubstr("Immunity to Dark and Poison") != null)
                        m.poisonDmg = 0;
                    else
                        m.currentDef -= m.poisonDmg;
                }

                if (!m.IsAlive)
                {
                    if (!TrySalvationSave(player, slot, m))
                    {
                        player.graveyard.Add(m.data);
                        pSlot.ClearSlot();
                        continue;
                    }
                }

                if (m.frozenTurns > 0)
                {
                    if (m.HasPassiveSubstr("Freeze Resistance") != null || m.HasPassiveSubstr("Total Immunity to Ice") != null || m.HasPassiveSubstr("Frost") != null)
                        m.frozenTurns = 0;
                    else
                    {
                        m.frozenTurns--;
                        m.canAttack = false;
                    }
                }

                if (m.silencedTurns > 0) m.silencedTurns--;
                if (m.weakenedTurns > 0) m.weakenedTurns--;

                var oppSlot = opponent.slots[slot];
                var oppM = (oppSlot != null && oppSlot.IsOccupied) ? oppSlot.currentMonster : null;

                if (m.silencedTurns <= 0)
                {
                    foreach (var p in m.passives)
                    {
                        if (p.Contains("Self-Ignition") && oppM != null)
                        {
                            oppM.burnDmg = Mathf.Max(oppM.burnDmg, 3);
                            oppM.currentDef -= 3;
                        }
                        else if (p.Contains("Blazing Fire") && oppM != null)
                        {
                            oppM.burnDmg = Mathf.Max(oppM.burnDmg, 8);
                            oppM.currentDef -= 8;
                        }
                        else if (p.Contains("Slow Poison") && oppM != null)
                        {
                            oppM.poisonDmg = Mathf.Max(oppM.poisonDmg, 6);
                            oppM.currentDef -= 6;
                        }
                        else if (p.Contains("Vital Regeneration"))
                        {
                            m.HealDefense(4);
                        }
                        else if (p.Contains("Soothing Current"))
                        {
                            player.HealLeader(5, opponent);
                        }
                        else if (p.Contains("Holy Aura"))
                        {
                            player.HealLeader(4, opponent);
                        }
                        else if (p.Contains("Gushing Waterfall"))
                        {
                            foreach (var s in player.slots)
                            {
                                if (s != null && s.IsOccupied)
                                {
                                    s.currentMonster.burnDmg = 0;
                                    s.currentMonster.poisonDmg = 0;
                                    s.currentMonster.frozenTurns = 0;
                                    s.currentMonster.weakenedTurns = 0;
                                }
                            }
                        }
                        else if (p.Contains("Supercharge"))
                        {
                            player.currentPower += 1;
                        }
                        else if (p.Contains("Booster Engine"))
                        {
                            m.bonusAtk += 2;
                        }
                        else if (p.Contains("Tailwind Gust"))
                        {
                            foreach (int ws in new[] { 0, 1, 4, 5 })
                            {
                                if (player.slots[ws].IsOccupied)
                                    player.slots[ws].currentMonster.doubleStrike = true;
                            }
                        }
                    }
                }

                if (oppM != null && !oppM.IsAlive)
                {
                    HandleMonsterDeath(opponent, player, slot, oppM, m);
                }
            }
        }

        public bool TrySalvationSave(PlayerState owner, int slot, MonsterInstance m)
        {
            if (m == null) return false;
            if (!m.salvationUsed && m.HasPassiveSubstr("Shield of Salvation") != null)
            {
                m.salvationUsed = true;
                m.currentDef = 10;
                return true;
            }
            foreach (int adj in new[] { slot - 1, slot + 1 })
            {
                if (adj >= 0 && adj < 6 && owner.slots[adj].IsOccupied)
                {
                    var ally = owner.slots[adj].currentMonster;
                    if (!ally.salvationUsed && ally.HasPassiveSubstr("Shield of Salvation") != null && ally.currentDef > 12)
                    {
                        ally.salvationUsed = true;
                        ally.currentDef -= 10;
                        m.currentDef = 10;
                        return true;
                    }
                }
            }
            return false;
        }

        public bool HandleMonsterDeath(PlayerState owner, PlayerState killerOwner, int slot, MonsterInstance deadM, MonsterInstance killerM)
        {
            if (deadM == null) return false;
            if (TrySalvationSave(owner, slot, deadM)) return false;

            if (slot >= 0 && slot < 6 && owner.slots[slot] != null && owner.slots[slot].currentMonster == deadM)
            {
                owner.slots[slot].ClearSlot();
            }
            owner.graveyard.Add(deadM.data);

            if (deadM.silencedTurns <= 0)
            {
                foreach (var p in deadM.passives)
                {
                    if (p.Contains("Thermal Explosion"))
                    {
                        if (slot >= 0 && slot < 6 && killerOwner.slots[slot].IsOccupied)
                        {
                            var t = killerOwner.slots[slot].currentMonster;
                            t.currentDef -= 10;
                            if (!t.IsAlive && !TrySalvationSave(killerOwner, slot, t))
                            {
                                killerOwner.graveyard.Add(t.data);
                                killerOwner.slots[slot].ClearSlot();
                            }
                        }
                        else
                        {
                            killerOwner.TakeLeaderDamage(10);
                        }
                    }
                    else if (p.Contains("Resurrection Ashes"))
                    {
                        owner.HealLeader(20, killerOwner);
                    }
                    else if (p.Contains("Inner Quake"))
                    {
                        killerOwner.TakeLeaderDamage(10);
                    }
                    else if (p.Contains("Scrap Recycling"))
                    {
                        owner.currentPower += 2;
                        owner.manaCapacity += 1;
                    }
                    else if (p.Contains("Curse of Doom") && killerM != null && killerM.IsAlive)
                    {
                        if (killerM.HasPassiveSubstr("Immunity to Dark") == null && killerM.HasPassiveSubstr("Total Immunity to Ghosts and Dark") == null)
                        {
                            killerM.currentDef = 0;
                            for (int ks = 0; ks < 6; ks++)
                            {
                                if (killerOwner.slots[ks].currentMonster == killerM)
                                {
                                    if (!TrySalvationSave(killerOwner, ks, killerM))
                                    {
                                        killerOwner.graveyard.Add(killerM.data);
                                        killerOwner.slots[ks].ClearSlot();
                                    }
                                    break;
                                }
                            }
                        }
                    }
                    else if (p.Contains("Spirit Possession") && slot >= 0 && slot < 6 && killerOwner.slots[slot].IsOccupied)
                    {
                        var t = killerOwner.slots[slot].currentMonster;
                        t.frozenTurns = Mathf.Max(t.frozenTurns, 2);
                        t.silencedTurns = Mathf.Max(t.silencedTurns, 2);
                        t.canAttack = false;
                    }
                }
            }

            return true;
        }

        public void DealSpellOrSkillDamage(int targetSlot, MonsterInstance targetM, int dmg, MonsterInstance sourceM, PlayerState opponent, PlayerState player, bool ignoreImmunity = false)
        {
            if (targetM == null || !targetM.IsAlive) return;

            if (targetM.bubbleShield && !ignoreImmunity)
            {
                targetM.bubbleShield = false;
                return;
            }

            if (!ignoreImmunity)
            {
                if (targetM.HasPassiveSubstr("Electromagnetic Shield") != null || targetM.HasPassiveSubstr("Night Stealth") != null)
                {
                    return;
                }
                if (targetM.HasPassiveSubstr("Mana Shield") != null)
                {
                    dmg = Mathf.Max(0, dmg - 6);
                }
                foreach (var s in opponent.slots)
                {
                    if (s != null && s.IsOccupied && s.currentMonster.HasPassiveSubstr("Thicket Cover") != null)
                    {
                        dmg -= Mathf.RoundToInt(dmg * 0.25f);
                        break;
                    }
                }
            }

            targetM.currentDef -= Mathf.Max(0, dmg);
            if (!targetM.IsAlive)
            {
                HandleMonsterDeath(opponent, player, targetSlot, targetM, sourceM);
            }
        }

        public void ExecuteActiveSkills(PlayerState player, PlayerState opponent)
        {
            if (player.currentPower <= 0) return;

            for (int slot = 0; slot < 6; slot++)
            {
                var pSlot = player.slots[slot];
                if (pSlot == null || !pSlot.IsOccupied) continue;
                var m = pSlot.currentMonster;
                if (!m.IsAlive || m.silencedTurns > 0 || m.frozenTurns > 0 || m.actives == null || m.actives.Count == 0)
                    continue;

                var oppSlot = opponent.slots[slot];
                var oppM = (oppSlot != null && oppSlot.IsOccupied) ? oppSlot.currentMonster : null;
                int extraCost = (oppM != null && oppM.HasPassiveSubstr("Frost Aura") != null) ? 1 : 0;

                foreach (var act in m.actives)
                {
                    int baseSkillCost = act.Contains("(Cost 3 Power)") ? 3 : (act.Contains("(Cost 2 Power)") ? 2 : 1);
                    int totalCost = Mathf.Max(0, baseSkillCost + extraCost - (player.freeSkillBonus > 0 ? 1 : 0));
                    if (player.currentPower < totalCost) continue;

                    var enemies = GetAliveSlots(opponent);
                    if (enemies.Count == 0 && !act.Contains("Restore") && !act.Contains("Heal") && !act.Contains("Nectar") && !act.Contains("Sun Judgment") && !act.Contains("Shield") && !act.Contains("Fortification") && !act.Contains("Blessing") && !act.Contains("Resurrection") && !act.Contains("Wrath of Nature") && !act.Contains("Summoning") && !act.Contains("Reboot") && !act.Contains("Wall") && !act.Contains("Bubble"))
                        continue;

                    player.currentPower -= totalCost;
                    int bonusEff = player.freeSkillBonus;
                    player.freeSkillBonus = 0;

                    if (m.HasPassiveSubstr("Mana Recovery") != null)
                    {
                        player.currentPower += 1;
                    }

                    foreach (var s in player.slots)
                    {
                        if (s != null && s.IsOccupied && s.currentMonster.HasPassiveSubstr("Arcane Echo") != null)
                        {
                            s.currentMonster.bonusAtk += 3;
                        }
                    }

                    ResolveSingleActive(act, slot, m, player, opponent, bonusEff);

                    if (BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.LogCombatMessage($"⚡ Monster [{m.data.name}] activated skill: {act}");
                    }

                    if (player.currentPower <= 0) break;
                }
            }
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

        private void ResolveSingleActive(string act, int slot, MonsterInstance m, PlayerState player, PlayerState opponent, int bonusEff)
        {
            var enemies = GetAliveSlots(opponent);
            var allies = GetAliveSlots(player);

            if (act.Contains("Hell of Annihilation"))
            {
                foreach (var es in enemies) DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, 25 + bonusEff, m, opponent, player);
            }
            else if (act.Contains("Volcanic Eruption") && enemies.Count > 0)
            {
                var targetSlot = opponent.slots[slot].IsOccupied ? opponent.slots[slot] : enemies[0];
                if (targetSlot.currentMonster.currentDef < 30 + bonusEff)
                {
                    targetSlot.currentMonster.currentDef = 0;
                    HandleMonsterDeath(opponent, player, targetSlot.slotIndex, targetSlot.currentMonster, m);
                }
                else
                {
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 20 + bonusEff, m, opponent, player);
                }
            }
            else if (act.Contains("Fireball") || act.Contains("Guided Missile") || act.Contains("Sudden Shock") || act.Contains("Lava Torrent") || act.Contains("Arcane Arrow") || act.Contains("Cutting Gale") || act.Contains("Dawn Ray") || act.Contains("Shadow Strike") || act.Contains("Touch of the Beyond"))
            {
                if (enemies.Count > 0)
                {
                    DealSpellOrSkillDamage(enemies[0].slotIndex, enemies[0].currentMonster, 15 + bonusEff, m, opponent, player);
                }
            }
            else if (act.Contains("Ember Blast") || act.Contains("Earthquake Strike"))
            {
                if (enemies.Count > 0)
                {
                    int idx = enemies[0].slotIndex;
                    DealSpellOrSkillDamage(idx, enemies[0].currentMonster, 14 + bonusEff, m, opponent, player);
                    foreach (int adj in new[] { idx - 1, idx + 1 })
                    {
                        if (adj >= 0 && adj < 6 && opponent.slots[adj].IsOccupied)
                            DealSpellOrSkillDamage(adj, opponent.slots[adj].currentMonster, 8, m, opponent, player);
                    }
                }
            }
            else if (act.Contains("Sweeping Tsunami") || act.Contains("Winds of Annihilation") || act.Contains("Grand Mana Blast"))
            {
                foreach (var es in enemies) DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, 20 + bonusEff, m, opponent, player, act.Contains("Winds of Annihilation"));
            }
            else if (act.Contains("Ice Age") || act.Contains("Abyssal Submersion") || act.Contains("Spectral Terror") || act.Contains("Strangling Roots") || act.Contains("Ice Spear"))
            {
                foreach (var es in enemies)
                {
                    es.currentMonster.frozenTurns = Mathf.Max(es.currentMonster.frozenTurns, 1);
                    es.currentMonster.canAttack = false;
                }
            }
            else if (act.Contains("Total Neural Paralysis") || act.Contains("Soul Lock"))
            {
                foreach (var es in enemies) es.currentMonster.silencedTurns = Mathf.Max(es.currentMonster.silencedTurns, 2);
            }
            else if (act.Contains("Immortal Mountain Shield") || act.Contains("Iron Dome Shield"))
            {
                player.teamImmuneTurns = Mathf.Max(player.teamImmuneTurns, 1);
            }
            else if (act.Contains("Absolute Sun Judgment") || act.Contains("Nectar of Life"))
            {
                player.HealLeader(act.Contains("Sun Judgment") ? 80 : 25, opponent);
                foreach (var al in allies) al.currentMonster.AddBonusDef(15);
            }
            else if (act.Contains("Destructive Ion Cannon"))
            {
                if (enemies.Count > 0)
                {
                    int rem = Mathf.Max(0, 50 + bonusEff - enemies[0].currentMonster.currentDef);
                    DealSpellOrSkillDamage(enemies[0].slotIndex, enemies[0].currentMonster, 50 + bonusEff, m, opponent, player);
                    if (rem > 0) opponent.TakeLeaderDamage(rem);
                }
                else opponent.TakeLeaderDamage(50);
            }
            else if (act.Contains("Arcane Time Warp") || act.Contains("Hastening Breeze"))
            {
                foreach (var al in allies)
                {
                    al.currentMonster.canAttack = true;
                    al.currentMonster.bonusAtk += 5;
                }
            }
            else
            {
                m.AddBonusDef(15 + bonusEff);
                m.bonusAtk += 5;
            }
        }

        public int ComputeAttackPower(int slot, MonsterInstance m, MonsterInstance oppM, EnvironmentCardData activeEnv, PlayerState opponent)
        {
            int atk = m.GetEffectiveAtk(activeEnv);

            for (int eIdx = 0; eIdx < 6; eIdx++)
            {
                var es = opponent.slots[eIdx];
                if (es != null && es.IsOccupied)
                {
                    if (es.currentMonster.HasPassiveSubstr("Dread of the Dead") != null)
                        atk = Mathf.Max(0, atk - 3);
                    if ((slot == 0 || slot == 1 || slot == 4 || slot == 5) && es.currentMonster.HasPassiveSubstr("Polar Winds") != null)
                        atk = Mathf.Max(0, atk - 4);
                }
            }

            if (oppM != null && oppM.IsAlive)
            {
                if (oppM.HasPassiveSubstr("Light Chill") != null) atk = Mathf.Max(0, atk - 3);
                if (oppM.HasPassiveSubstr("Aura of Dread") != null) atk = Mathf.Max(0, atk - 3);
                if (oppM.HasPassiveSubstr("Water Submersion") != null) atk = Mathf.Max(0, atk - 4);
                if (oppM.HasPassiveSubstr("Fierce Gale") != null) atk = Mathf.RoundToInt(atk * 0.75f);
                if (oppM.HasPassiveSubstr("Majestic Brightness") != null && UnityEngine.Random.value < 0.30f) atk = Mathf.RoundToInt(atk * 0.55f);

                if (m.HasPassiveSubstr("Ice Blade") != null && oppM.frozenTurns > 0)
                    atk += 5;
            }

            return Mathf.Max(0, atk);
        }

        #endregion

        public void ExecuteCombatPhase(PlayerState attacker, PlayerState defender, EnvironmentCardData activeEnv)
        {
            StartCoroutine(ExecuteCombatPhaseAnimated(attacker, defender, activeEnv, null));
        }

        public IEnumerator ExecuteCombatPhaseAnimated(PlayerState attacker, PlayerState defender, EnvironmentCardData activeEnv, Action onComplete)
        {
            bool isAttackerPlayer = (attacker == GameManager.Instance.humanPlayer);
            int[] attackSlots = { 0, 1, 2, 3, 4, 5 };

            foreach (int slotIdx in attackSlots)
            {
                var slot = attacker.slots[slotIdx];
                if (slot == null || !slot.IsOccupied || !slot.currentMonster.canAttack || slot.currentMonster.frozenTurns > 0)
                    continue;

                var attackerMonster = slot.currentMonster;
                int strikes = attackerMonster.doubleStrike ? 2 : 1;
                attackerMonster.doubleStrike = false;
                attackerMonster.canAttack = false;

                for (int st = 0; st < strikes; st++)
                {
                    if (!attackerMonster.IsAlive) break;

                    var defenderSlot = defender.slots[slotIdx];
                    var oppM = (defenderSlot != null && defenderSlot.IsOccupied) ? defenderSlot.currentMonster : null;
                    int atkPower = ComputeAttackPower(slotIdx, attackerMonster, oppM, activeEnv, defender);
                    if (atkPower <= 0) continue;

                    var attackerSlotView = BattleUIManager.Instance != null ? BattleUIManager.Instance.GetSlotView(slotIdx, isAttackerPlayer) : null;
                    var defenderSlotView = BattleUIManager.Instance != null ? BattleUIManager.Instance.GetSlotView(slotIdx, !isAttackerPlayer) : null;
                    var defenderLeaderTransform = BattleUIManager.Instance != null ? BattleUIManager.Instance.GetLeaderTransform(!isAttackerPlayer) : null;

                    bool isDirectToLeader = (defenderSlot == null || !defenderSlot.IsOccupied);
                    Vector3 targetPos = (!isDirectToLeader && defenderSlotView != null)
                        ? defenderSlotView.transform.position
                        : (defenderLeaderTransform != null ? defenderLeaderTransform.position : Vector3.zero);

                    if (attackerSlotView != null)
                    {
                        yield return BattleAnimations.AnimateCardAttack(attackerSlotView.transform, targetPos, () =>
                        {
                            ApplyDamageImpact(attacker, attackerMonster, atkPower, slotIdx, defender, defenderSlot, defenderSlotView, defenderLeaderTransform, isDirectToLeader);
                        });
                    }
                    else
                    {
                        ApplyDamageImpact(attacker, attackerMonster, atkPower, slotIdx, defender, defenderSlot, defenderSlotView, defenderLeaderTransform, isDirectToLeader);
                    }

                    yield return new WaitForSeconds(0.28f);
                }
            }

            yield return new WaitForSeconds(0.20f);
            onComplete?.Invoke();
        }

        private void ApplyDamageImpact(PlayerState attacker, MonsterInstance attackerMonster, int atkPower, int slotIdx, PlayerState defender, BoardSlot defenderSlot, SlotView defenderSlotView, Transform defenderLeaderTransform, bool isDirectToLeader)
        {
            if (defender.teamImmuneTurns > 0) return;

            if (!isDirectToLeader && defenderSlot != null && defenderSlot.IsOccupied)
            {
                var enemyMonster = defenderSlot.currentMonster;

                if (enemyMonster.bubbleShield)
                {
                    enemyMonster.bubbleShield = false;
                    return;
                }

                // Ethereal Body for Ghosts: reduces normal monster attacks by 35%
                if (enemyMonster.HasPassiveSubstr("Ethereal Body") != null)
                {
                    if (attackerMonster.data.element != "Light" && attackerMonster.data.element != "Arcane" && attackerMonster.data.element != "Ghosts" && attackerMonster.HasPassiveSubstr("Phase Shift") == null)
                    {
                        atkPower = Mathf.Max(1, Mathf.RoundToInt(atkPower * 0.65f));
                    }
                }

                // Check Evasion & Mystic Intuition
                if (attackerMonster.HasPassiveSubstr("Targeting System") == null)
                {
                    if (enemyMonster.HasPassiveSubstr("Light Evasion") != null && UnityEngine.Random.value < 0.20f) return;
                    if (enemyMonster.HasPassiveSubstr("Mystic Intuition") != null && UnityEngine.Random.value < 0.25f) return;
                    if (enemyMonster.HasPassiveSubstr("Nimble Agility") != null && UnityEngine.Random.value < 0.15f) return;
                }

                // Armor & Damage Reduction
                bool ignoreArmor = attackerMonster.HasPassiveSubstr("Phase Shift") != null || attackerMonster.HasPassiveSubstr("Aerial Pierce") != null;
                if (!ignoreArmor)
                {
                    if (enemyMonster.HasPassiveSubstr("Water Resilience") != null) atkPower = Mathf.Max(1, atkPower - 3);
                    if (enemyMonster.HasPassiveSubstr("Protective Flame") != null) atkPower = Mathf.Max(1, atkPower - 4);
                    if (enemyMonster.HasPassiveSubstr("Rock Hardness") != null) atkPower = Mathf.Max(1, atkPower - 4);
                    if (enemyMonster.HasPassiveSubstr("Steel Armor") != null) atkPower = Mathf.Max(1, atkPower - 5);
                }

                enemyMonster.TakeDamage(atkPower);
                if (MonsterCardGame.Audio.SoundManager.Instance != null)
                    MonsterCardGame.Audio.SoundManager.Instance.PlayCardHit();

                // On-Hit Attacker Passives
                if (attackerMonster.HasPassiveSubstr("Eternal Flame") != null) attackerMonster.bonusAtk += 2;
                if (attackerMonster.HasPassiveSubstr("Static Discharge") != null) enemyMonster.silencedTurns = Mathf.Max(enemyMonster.silencedTurns, 1);
                if (attackerMonster.HasPassiveSubstr("Life Leech") != null) attackerMonster.HealDefense(Mathf.Max(2, Mathf.RoundToInt(atkPower * 0.20f)));
                if (attackerMonster.HasPassiveSubstr("Slow Poison") != null) enemyMonster.poisonDmg = Mathf.Max(enemyMonster.poisonDmg, 6);

                // Chain Lightning
                if (attackerMonster.HasPassiveSubstr("Chain Lightning") != null)
                {
                    int splash = Mathf.Max(3, Mathf.RoundToInt(atkPower * 0.30f));
                    foreach (int adj in new[] { slotIdx - 1, slotIdx + 1 })
                    {
                        if (adj >= 0 && adj < 6 && defender.slots[adj].IsOccupied)
                        {
                            defender.slots[adj].currentMonster.TakeDamage(splash);
                            if (!defender.slots[adj].currentMonster.IsAlive)
                                HandleMonsterDeath(defender, attacker, adj, defender.slots[adj].currentMonster, attackerMonster);
                            break;
                        }
                    }
                }

                // Defensive Counter-Passives (Thorns, Spark, Flint Skin, Burning Embers, Auto-Freeze)
                if (enemyMonster.HasPassiveSubstr("Poison Thorns") != null || enemyMonster.HasPassiveSubstr("Backlash Spark") != null)
                    attackerMonster.TakeDamage(3);
                if (enemyMonster.HasPassiveSubstr("Flint Skin") != null)
                    attackerMonster.TakeDamage(5);
                if (enemyMonster.HasPassiveSubstr("Burning Embers") != null)
                    attackerMonster.TakeDamage(Mathf.Max(2, Mathf.RoundToInt(atkPower * 0.25f)));
                if (enemyMonster.HasPassiveSubstr("Auto-Freeze Attacker") != null && attackerMonster.IsAlive)
                    attackerMonster.frozenTurns = Mathf.Max(attackerMonster.frozenTurns, 1);

                if (defenderSlotView != null && BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.TriggerHitShake(defenderSlotView.transform, Color.red);
                    BattleUIManager.Instance.SpawnDamageNumber(defenderSlotView.transform.position, atkPower, false);
                }

                if (!enemyMonster.IsAlive)
                {
                    if (MonsterCardGame.Audio.SoundManager.Instance != null)
                        MonsterCardGame.Audio.SoundManager.Instance.PlayCardDeath();
                    HandleMonsterDeath(defender, attacker, slotIdx, enemyMonster, attackerMonster);
                }

                if (!attackerMonster.IsAlive)
                {
                    HandleMonsterDeath(attacker, defender, slotIdx, attackerMonster, enemyMonster);
                }
            }
            else
            {
                defender.TakeLeaderDamage(atkPower);
                if (attackerMonster.HasPassiveSubstr("Mana Steal") != null)
                {
                    attacker.currentPower += 1;
                }
                if (MonsterCardGame.Audio.SoundManager.Instance != null)
                    MonsterCardGame.Audio.SoundManager.Instance.PlayHpDamage();

                if (BattleUIManager.Instance != null)
                {
                    if (defenderLeaderTransform != null)
                    {
                        BattleUIManager.Instance.TriggerHitShake(defenderLeaderTransform, Color.red);
                        BattleUIManager.Instance.SpawnDamageNumber(defenderLeaderTransform.position, atkPower, true);
                    }
                    BattleUIManager.Instance.ShakeScreen();
                }
            }

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.UpdateUI();
            }
        }
    }
}

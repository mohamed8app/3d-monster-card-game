using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;
using MonsterCardGame.UI;
using MonsterCardGame.Utils;

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
                        if (player.slots[i].IsOccupied && (player.slots[i].currentMonster.data.tribe == "Dwarves" || player.slots[i].currentMonster.data.element == "Mecha"))
                            mechas++;
                    }
                    m.AddBonusDef(Mathf.Max(1, mechas) * 8);
                }
                else if (p.Contains("Nourishing Roots"))
                {
                    player.power++;
                    player.currentMana++;
                }
                else if (p.Contains("Psychic Drain"))
                {
                    int drained = Mathf.Min(2, Mathf.Max(0, opponent.power - 1));
                    opponent.power -= drained;
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
                            player.currentMana += 1;
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
            owner.RecordMonsterDeath(deadM.data.id, GameManager.Instance != null ? GameManager.Instance.currentTurn : 1);

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
                        owner.currentMana += 2;
                        owner.power += 1;
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

        public enum AbilityReadyState
        {
            Ready,
            NoAbility,
            NotYourTurn,
            Silenced,
            Frozen,
            AlreadyUsed,
            NotEnoughMana,
            NotEnoughPower,
            NoValidTarget
        }

        public void DealSpellOrSkillDamage(int targetSlot, MonsterInstance targetM, int dmg, MonsterInstance sourceM, PlayerState opponent, PlayerState player, bool ignoreImmunity = false, bool showVfx = false)
        {
            if (targetM == null || !targetM.IsAlive) return;

            bool targetIsHuman = GameManager.Instance != null && (opponent == GameManager.Instance.humanPlayer);

            if (targetM.bubbleShield && !ignoreImmunity)
            {
                targetM.bubbleShield = false;
                if (showVfx) SpawnSlotStatusVfx(targetIsHuman, targetSlot, "🫧 BLOCKED!", new Color(0.45f, 0.90f, 1f));
                return;
            }

            if (!ignoreImmunity)
            {
                if (targetM.HasPassiveSubstr("Electromagnetic Shield") != null || targetM.HasPassiveSubstr("Night Stealth") != null)
                {
                    if (showVfx) SpawnSlotStatusVfx(targetIsHuman, targetSlot, "🛡️ IMMUNE!", new Color(0.75f, 0.85f, 1f));
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

            int appliedDmg = Mathf.Max(0, dmg);
            targetM.currentDef -= appliedDmg;

            if (showVfx && appliedDmg > 0 && BattleUIManager.Instance != null)
            {
                var sv = BattleUIManager.Instance.GetSlotView(targetSlot, targetIsHuman);
                if (sv != null)
                {
                    BattleUIManager.Instance.TriggerHitShake(sv.transform, new Color(1f, 0.4f, 0.25f));
                    BattleUIManager.Instance.SpawnDamageNumber(sv.transform.position, appliedDmg, false);
                }
                if (MonsterCardGame.Audio.SoundManager.Instance != null)
                    MonsterCardGame.Audio.SoundManager.Instance.PlayCardHit();
            }

            if (!targetM.IsAlive)
            {
                if (showVfx && MonsterCardGame.Audio.SoundManager.Instance != null)
                    MonsterCardGame.Audio.SoundManager.Instance.PlayCardDeath();
                HandleMonsterDeath(opponent, player, targetSlot, targetM, sourceM);
            }
        }

        public int GetActiveSkillCost(PlayerState player, PlayerState opponent, int slotIndex, string act)
        {
            if (string.IsNullOrEmpty(act)) return 0;
            bool hasPowerConditionOnly = GetParsedPermanentPowerCost(act) > 0 && !act.ToLowerInvariant().Contains("mana");
            int baseSkillCost = hasPowerConditionOnly ? 0 :
                ((act.Contains("(Cost 3 Mana)") || act.Contains("(Cost 3 Power)")) ? 3 :
                ((act.Contains("(Cost 2 Mana)") || act.Contains("(Cost 2 Power)")) ? 2 : 1));
            var oppSlot = (opponent != null && slotIndex >= 0 && slotIndex < 6) ? opponent.slots[slotIndex] : null;
            var oppM = (oppSlot != null && oppSlot.IsOccupied) ? oppSlot.currentMonster : null;
            int extraCost = (oppM != null && oppM.HasPassiveSubstr("Frost Aura") != null) ? 1 : 0;
            int discount = (player != null && player.freeSkillBonus > 0) ? 1 : 0;
            return Mathf.Max(0, baseSkillCost + extraCost - discount);
        }

        private static int GetParsedPermanentPowerCost(string act)
        {
            if (string.IsNullOrEmpty(act)) return 0;
            if (act.Contains("(Requires 3 Power)") || act.Contains("(Cost 3 Permanent Power)") || act.Contains("(Spend 3 Power)") || act.Contains("(Drain 3 Power)") || act.Contains("يتطلب 3 باور") || act.Contains("يستهلك 3 باور")) return 3;
            if (act.Contains("(Requires 2 Power)") || act.Contains("(Cost 2 Permanent Power)") || act.Contains("(Spend 2 Power)") || act.Contains("(Drain 2 Power)") || act.Contains("يتطلب 2 باور") || act.Contains("يستهلك 2 باور")) return 2;
            if (act.Contains("(Requires 1 Power)") || act.Contains("(Cost 1 Permanent Power)") || act.Contains("(Spend 1 Power)") || act.Contains("(Drain 1 Power)") || act.Contains("يتطلب 1 باور") || act.Contains("يستهلك 1 باور")) return 1;
            return 0;
        }

        public int GetActiveSkillPowerCost(MonsterInstance m, string act)
        {
            if (m != null && m.data != null && m.data.powerCost > 0)
            {
                return m.data.powerCost;
            }
            return GetParsedPermanentPowerCost(act);
        }

        public bool SkillRequiresEnemyMonster(string act)
        {
            if (string.IsNullOrEmpty(act)) return false;
            return !(act.Contains("Restore") || act.Contains("Heal") || act.Contains("Nectar") ||
                     act.Contains("Sun Judgment") || act.Contains("Shield") || act.Contains("Fortification") ||
                     act.Contains("Blessing") || act.Contains("Resurrection") || act.Contains("Wrath of Nature") ||
                     act.Contains("Summoning") || act.Contains("Reboot") || act.Contains("Wall") ||
                     act.Contains("Bubble") || act.Contains("Purging Flame") || act.Contains("Energy Surge") ||
                     act.Contains("Hastening Breeze") || act.Contains("Arcane Time Warp") ||
                     act.Contains("Total Eclipse") || act.Contains("Destructive Ion Cannon") ||
                     act.Contains("Grand Mana Blast") || act.Contains("Grand Thunderstorm") ||
                     act.Contains("Hell of Annihilation"));
        }

        private bool HasEmptyFlankSlot(PlayerState player)
        {
            if (player == null || player.slots == null) return false;
            foreach (int ws in new[] { 0, 1, 4, 5 })
            {
                if (player.slots[ws] != null && !player.slots[ws].IsOccupied) return true;
            }
            return false;
        }

        public AbilityReadyState GetAbilityState(PlayerState owner, PlayerState opponent, int slotIndex, out int totalCost, out string reason)
        {
            totalCost = 0;
            reason = string.Empty;

            if (owner == null || slotIndex < 0 || slotIndex >= owner.slots.Length)
            {
                reason = "Invalid board slot.";
                return AbilityReadyState.NoAbility;
            }

            var slot = owner.slots[slotIndex];
            var m = (slot != null && slot.IsOccupied) ? slot.currentMonster : null;
            if (m == null || !m.IsAlive || !m.HasActiveSkill)
            {
                reason = "This monster has no active ability.";
                return AbilityReadyState.NoAbility;
            }

            string act = m.actives[0];
            int manaCost = GetActiveSkillCost(owner, opponent, slotIndex, act);
            int pwrCost = GetActiveSkillPowerCost(m, act);
            totalCost = pwrCost > 0 ? pwrCost : manaCost;

            var gm = GameManager.Instance;
            if (gm != null)
            {
                bool isOwnersTurn = (owner == gm.humanPlayer && gm.isPlayerTurn) || (owner == gm.aiPlayer && !gm.isPlayerTurn);
                if (!isOwnersTurn || gm.currentPhase != GamePhase.ActionPhase)
                {
                    reason = "⏳ Wait until your Action Phase to activate this ability.";
                    return AbilityReadyState.NotYourTurn;
                }
            }

            if (m.silencedTurns > 0)
            {
                reason = $"🔒 [{m.data.name}] is Silenced for {m.silencedTurns} turn(s)!";
                return AbilityReadyState.Silenced;
            }

            if (m.frozenTurns > 0)
            {
                reason = $"❄️ [{m.data.name}] is Frozen for {m.frozenTurns} turn(s)!";
                return AbilityReadyState.Frozen;
            }

            if (m.hasUsedActiveThisTurn)
            {
                reason = $"✓ [{m.data.name}] already used its Special Ability this turn.";
                return AbilityReadyState.AlreadyUsed;
            }

            if (SkillRequiresEnemyMonster(act) && GetAliveSlots(opponent).Count == 0)
            {
                reason = $"🎯 [{m.data.name}] requires at least 1 enemy monster on the battlefield!";
                return AbilityReadyState.NoValidTarget;
            }

            if ((act.Contains("Wrath of Nature") || act.Contains("Graveyard Summoning")) && !HasEmptyFlankSlot(owner))
            {
                reason = $"⚠️ Flank slots are full! Need at least 1 empty flank slot to summon tokens.";
                return AbilityReadyState.NoValidTarget;
            }

            if (owner.currentMana < manaCost)
            {
                reason = $"❌ Not enough Mana for [{m.data.name}]'s ability! Need {manaCost}💧 Mana, have {owner.currentMana}💧.";
                return AbilityReadyState.NotEnoughMana;
            }

            if (pwrCost > 0 && owner.power < pwrCost)
            {
                reason = $"❌ Not enough Power for [{m.data.name}]'s ability! Need {pwrCost}⚡ Power, have {owner.power}⚡.";
                return AbilityReadyState.NotEnoughPower;
            }

            reason = pwrCost > 0
                ? $"⚡ Ready to activate (Requires {pwrCost}⚡ Power" + (manaCost > 0 ? $" + {manaCost}💧 Mana" : "") + ")!"
                : $"💧 Ready to activate ({manaCost}💧 Mana)!";
            return AbilityReadyState.Ready;
        }

        public bool ActivateMonsterAbility(PlayerState player, PlayerState opponent, int slotIndex, out string message)
        {
            var state = GetAbilityState(player, opponent, slotIndex, out int totalCost, out string reason);
            if (state != AbilityReadyState.Ready)
            {
                message = reason;
                return false;
            }

            var m = player.slots[slotIndex].currentMonster;
            m.hasUsedActiveThisTurn = true;
            bool activatedAny = false;
            string primaryAct = m.actives[0];

            foreach (var act in m.actives)
            {
                int actCost = GetActiveSkillCost(player, opponent, slotIndex, act);
                int actPowerCost = GetActiveSkillPowerCost(m, act);
                if (player.currentMana < actCost || player.power < actPowerCost) continue;
                if (SkillRequiresEnemyMonster(act) && GetAliveSlots(opponent).Count == 0) continue;

                player.currentMana -= actCost;
                if (actPowerCost > 0) player.power -= actPowerCost;
                int bonusEff = player.freeSkillBonus;
                player.freeSkillBonus = 0;

                if (m.HasPassiveSubstr("Mana Recovery") != null)
                {
                    player.currentMana += 1;
                }

                foreach (var s in player.slots)
                {
                    if (s != null && s.IsOccupied && s.currentMonster.HasPassiveSubstr("Arcane Echo") != null)
                    {
                        s.currentMonster.bonusAtk += 3;
                    }
                }

                int repeats = (m.HasPassiveSubstr("Spell Multiplier") != null && UnityEngine.Random.value < 0.50f) ? 2 : 1;
                for (int r = 0; r < repeats; r++)
                {
                    ResolveSingleActive(act, slotIndex, m, player, opponent, bonusEff, showVfx: true);
                }

                activatedAny = true;
                primaryAct = act;
                if (player.currentMana <= 0 && player.power <= 0) break;
            }

            if (!activatedAny)
            {
                m.hasUsedActiveThisTurn = false;
                message = reason;
                return false;
            }

            message = $"⚡ Monster #{m.data.name} activated [{primaryAct}]!";
            return true;
        }

        public void ExecuteActiveSkills(PlayerState player, PlayerState opponent)
        {
            if (player.currentMana <= 0 && player.power <= 0) return;

            for (int slot = 0; slot < 6; slot++)
            {
                TryExecuteSlotActiveSkill(player, opponent, slot);
                if (player.currentMana <= 0 && player.power <= 0) break;
            }
        }

        /// <summary>
        /// Steps through the AI/player's monster active skills one by one, refreshing UI so Mana/Power changes are visible live.
        /// </summary>
        public System.Collections.IEnumerator ExecuteActiveSkillsAnimated(PlayerState player, PlayerState opponent)
        {
            if (player == null || (player.currentMana <= 0 && player.power <= 0)) yield break;

            for (int slot = 0; slot < 6; slot++)
            {
                if (TryExecuteSlotActiveSkill(player, opponent, slot))
                {
                    if (BattleUIManager.Instance != null) BattleUIManager.Instance.UpdateUI();
                    yield return new WaitForSeconds(0.45f);
                }

                if (player.currentMana <= 0 && player.power <= 0) break;
            }
        }

        private bool TryExecuteSlotActiveSkill(PlayerState player, PlayerState opponent, int slot)
        {
            var pSlot = player.slots[slot];
            if (pSlot == null || !pSlot.IsOccupied) return false;
            var m = pSlot.currentMonster;
            if (!m.IsAlive || m.silencedTurns > 0 || m.frozenTurns > 0 || m.hasUsedActiveThisTurn || !m.HasActiveSkill)
                return false;

            if (ActivateMonsterAbility(player, opponent, slot, out string msg))
            {
                if (MonsterCardGame.Audio.SoundManager.Instance != null)
                {
                    MonsterCardGame.Audio.SoundManager.Instance.TryPlayMonsterSignatureClip(m.data, "skill");
                }
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.LogCombatMessage(msg);
                }
                return true;
            }
            return false;
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

        private BoardSlot GetEnemyTarget(int preferredSlot, List<BoardSlot> enemies, PlayerState opponent, bool preferMaxAtk = false)
        {
            if (enemies == null || enemies.Count == 0) return null;
            if (preferredSlot >= 0 && preferredSlot < 6 && opponent.slots[preferredSlot] != null && opponent.slots[preferredSlot].IsOccupied)
            {
                return opponent.slots[preferredSlot];
            }

            if (preferMaxAtk)
            {
                BoardSlot best = enemies[0];
                int bestAtk = -1;
                foreach (var e in enemies)
                {
                    int atk = e.currentMonster.baseAtk + e.currentMonster.bonusAtk;
                    if (atk > bestAtk)
                    {
                        bestAtk = atk;
                        best = e;
                    }
                }
                return best;
            }

            return enemies[0];
        }

        private MonsterInstance GetBestAllyForOffensiveBuff(MonsterInstance self, List<BoardSlot> allies)
        {
            MonsterInstance best = self;
            int bestScore = -1;
            foreach (var al in allies)
            {
                var am = al.currentMonster;
                int score = (am.baseAtk + am.bonusAtk) + (am.canAttack ? 15 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = am;
                }
            }
            return best;
        }

        private MonsterInstance GetBestAllyForDefensiveBuff(MonsterInstance self, List<BoardSlot> allies)
        {
            MonsterInstance best = self;
            int bestMissingOrLowest = int.MinValue;
            foreach (var al in allies)
            {
                var am = al.currentMonster;
                int missing = Mathf.Max(0, am.baseDef - am.currentDef);
                int score = missing * 2 + (50 - Mathf.Min(50, am.currentDef));
                if (score > bestMissingOrLowest)
                {
                    bestMissingOrLowest = score;
                    best = am;
                }
            }
            return best;
        }

        private void SpawnSlotStatusVfx(bool targetIsHuman, int slotIdx, string label, Color color)
        {
            if (BattleUIManager.Instance == null) return;
            var sv = BattleUIManager.Instance.GetSlotView(slotIdx, targetIsHuman);
            if (sv == null) return;
            var canvas = BattleUIManager.Instance.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                BattleUIManager.Instance.StartCoroutine(BattleAnimations.AnimateFloatingStatus(canvas.transform, sv.transform.position, label, color));
            }
        }

        private void SpawnLeaderStatusVfx(bool targetIsHuman, string label, Color color, bool isHeal = false, int healAmt = 0)
        {
            if (BattleUIManager.Instance == null) return;
            var leaderTr = BattleUIManager.Instance.GetLeaderTransform(targetIsHuman);
            if (leaderTr == null) return;
            var canvas = BattleUIManager.Instance.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                if (isHeal && healAmt > 0)
                {
                    BattleUIManager.Instance.StartCoroutine(BattleAnimations.AnimateFloatingHeal(canvas.transform, leaderTr.position, healAmt));
                }
                else
                {
                    BattleUIManager.Instance.StartCoroutine(BattleAnimations.AnimateFloatingStatus(canvas.transform, leaderTr.position, label, color));
                }
            }
        }

        private int SpawnTokensOnFlanks(PlayerState owner, CardData tokenProto, bool canAttackImmediately)
        {
            int spawned = 0;
            int turn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 1;
            bool isHuman = GameManager.Instance != null && (owner == GameManager.Instance.humanPlayer);

            foreach (int ws in new[] { 0, 1, 4, 5 })
            {
                if (owner.slots[ws] != null && !owner.slots[ws].IsOccupied && spawned < 2)
                {
                    var token = new MonsterInstance(tokenProto, turn);
                    token.canAttack = canAttackImmediately;
                    owner.slots[ws].PlaceMonster(token);
                    spawned++;
                    SpawnSlotStatusVfx(isHuman, ws, "✨ SUMMONED!", MonsterArtLoader.GetElementColor(tokenProto.element));
                }
            }
            return spawned;
        }

        private void ResolveSingleActive(string act, int slot, MonsterInstance m, PlayerState player, PlayerState opponent, int bonusEff, bool showVfx = true)
        {
            var enemies = GetAliveSlots(opponent);
            var allies = GetAliveSlots(player);
            bool playerIsHuman = GameManager.Instance != null && (player == GameManager.Instance.humanPlayer);
            bool oppIsHuman = !playerIsHuman;

            // --- FIRE ---
            if (act.Contains("Hell of Annihilation"))
            {
                int dmg = 25 + bonusEff;
                if (enemies.Count > 0)
                {
                    foreach (var es in enemies)
                        DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, dmg, m, opponent, player, false, showVfx);
                }
                else
                {
                    opponent.TakeLeaderDamage(dmg);
                    if (showVfx) SpawnLeaderStatusVfx(oppIsHuman, $"-{dmg} 🔥", new Color(1f, 0.45f, 0.15f));
                }
            }
            else if (act.Contains("Volcanic Eruption"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent);
                if (targetSlot != null)
                {
                    if (targetSlot.currentMonster.currentDef < 30 + bonusEff)
                    {
                        if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "🌋 OBLITERATED!", new Color(1f, 0.35f, 0.15f));
                        targetSlot.currentMonster.currentDef = 0;
                        HandleMonsterDeath(opponent, player, targetSlot.slotIndex, targetSlot.currentMonster, m);
                    }
                    else
                    {
                        DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 20 + bonusEff, m, opponent, player, false, showVfx);
                    }
                }
            }
            else if (act.Contains("Fireball"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 12 + bonusEff, m, opponent, player, false, showVfx);
            }
            else if (act.Contains("Ember Blast"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent);
                if (targetSlot != null)
                {
                    int idx = targetSlot.slotIndex;
                    DealSpellOrSkillDamage(idx, targetSlot.currentMonster, 8 + bonusEff, m, opponent, player, false, showVfx);
                    foreach (int adj in new[] { idx - 1, idx + 1 })
                    {
                        if (adj >= 0 && adj < 6 && opponent.slots[adj].IsOccupied)
                            DealSpellOrSkillDamage(adj, opponent.slots[adj].currentMonster, 8, m, opponent, player, false, showVfx);
                    }
                }
            }
            else if (act.Contains("Lava Torrent"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 15 + bonusEff, m, opponent, player, false, showVfx);
            }
            else if (act.Contains("Purging Flame"))
            {
                var ally = m;
                foreach (var al in allies)
                {
                    if (al.currentMonster.burnDmg > 0 || al.currentMonster.poisonDmg > 0 || al.currentMonster.frozenTurns > 0 || al.currentMonster.weakenedTurns > 0)
                    {
                        ally = al.currentMonster;
                        break;
                    }
                }
                ally.burnDmg = 0;
                ally.poisonDmg = 0;
                ally.frozenTurns = 0;
                ally.weakenedTurns = 0;
                ally.silencedTurns = 0;
                ally.bonusAtk += 5 + bonusEff;
                int aSlot = ally.currentSlot != null ? ally.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"🔥 CLEANSED +5 ATK", new Color(1f, 0.65f, 0.25f));
            }
            // --- WATER ---
            else if (act.Contains("Sweeping Tsunami"))
            {
                foreach (var es in enemies)
                {
                    es.currentMonster.bonusAtk = 0;
                    DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, 20 + bonusEff, m, opponent, player, false, showVfx);
                }
            }
            else if (act.Contains("Abyssal Submersion"))
            {
                int frozenCount = 0;
                foreach (int vIdx in new[] { 2, 3 })
                {
                    if (opponent.slots[vIdx] != null && opponent.slots[vIdx].IsOccupied)
                    {
                        var em = opponent.slots[vIdx].currentMonster;
                        em.frozenTurns = Mathf.Max(em.frozenTurns, 1);
                        em.canAttack = false;
                        frozenCount++;
                        if (showVfx) SpawnSlotStatusVfx(oppIsHuman, vIdx, "❄️ FROZEN!", new Color(0.45f, 0.85f, 1f));
                    }
                }
                if (frozenCount == 0)
                {
                    foreach (var es in enemies)
                    {
                        es.currentMonster.frozenTurns = Mathf.Max(es.currentMonster.frozenTurns, 1);
                        es.currentMonster.canAttack = false;
                        if (showVfx) SpawnSlotStatusVfx(oppIsHuman, es.slotIndex, "❄️ FROZEN!", new Color(0.45f, 0.85f, 1f));
                        if (++frozenCount >= 2) break;
                    }
                }
            }
            else if (act.Contains("Water Vortex"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent);
                if (targetSlot != null)
                {
                    var em = targetSlot.currentMonster;
                    em.canAttack = false;
                    int idx = targetSlot.slotIndex;
                    if (em.HasPassiveSubstr("Earth Steadfastness") == null)
                    {
                        foreach (int adj in new[] { idx - 1, idx + 1 })
                        {
                            if (adj >= 0 && adj < 6 && !opponent.slots[adj].IsOccupied)
                            {
                                targetSlot.ClearSlot();
                                opponent.slots[adj].PlaceMonster(em);
                                idx = adj;
                                break;
                            }
                        }
                    }
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, idx, "🌊 SHIFTED!", new Color(0.3f, 0.75f, 1f));
                }
            }
            else if (act.Contains("Healing Stream"))
            {
                var target = GetBestAllyForDefensiveBuff(m, allies);
                target.HealDefense(20 + bonusEff);
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"+{20 + bonusEff} DEF 💚", new Color(0.35f, 1f, 0.55f));
            }
            else if (act.Contains("Safety Bubble"))
            {
                MonsterInstance target = m;
                foreach (var al in allies)
                {
                    if (!al.currentMonster.bubbleShield)
                    {
                        target = al.currentMonster;
                        break;
                    }
                }
                target.bubbleShield = true;
                target.AddBonusDef(10);
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, "🫧 BUBBLE SHIELD!", new Color(0.45f, 0.90f, 1f));
            }
            // --- ICE ---
            else if (act.Contains("Ice Age"))
            {
                foreach (var es in enemies)
                {
                    es.currentMonster.frozenTurns = Mathf.Max(es.currentMonster.frozenTurns, 1);
                    es.currentMonster.canAttack = false;
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, es.slotIndex, "❄️ FROZEN!", new Color(0.55f, 0.92f, 1f));
                }
            }
            else if (act.Contains("Frost Shatter"))
            {
                BoardSlot frozenSlot = null;
                foreach (var es in enemies)
                {
                    if (es.currentMonster.frozenTurns > 0)
                    {
                        frozenSlot = es;
                        break;
                    }
                }
                if (frozenSlot != null)
                {
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, frozenSlot.slotIndex, "💥 SHATTERED!", new Color(0.55f, 0.95f, 1f));
                    frozenSlot.currentMonster.currentDef = 0;
                    HandleMonsterDeath(opponent, player, frozenSlot.slotIndex, frozenSlot.currentMonster, m);
                }
                else
                {
                    var targetSlot = GetEnemyTarget(slot, enemies, opponent);
                    if (targetSlot != null)
                    {
                        targetSlot.currentMonster.frozenTurns = Mathf.Max(targetSlot.currentMonster.frozenTurns, 1);
                        targetSlot.currentMonster.canAttack = false;
                        DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 18 + bonusEff, m, opponent, player, false, showVfx);
                    }
                }
            }
            else if (act.Contains("Ice Spear"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true);
                if (targetSlot != null)
                {
                    targetSlot.currentMonster.frozenTurns = Mathf.Max(targetSlot.currentMonster.frozenTurns, 1);
                    targetSlot.currentMonster.canAttack = false;
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 10 + bonusEff, m, opponent, player, false, showVfx);
                }
            }
            else if (act.Contains("Snow Wall"))
            {
                var target = GetBestAllyForDefensiveBuff(m, allies);
                target.AddBonusDef(20 + bonusEff);
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"❄️ +{20 + bonusEff} DEF", new Color(0.6f, 0.92f, 1f));
            }
            // --- ELECTRIC ---
            else if (act.Contains("Grand Thunderstorm"))
            {
                int perBolt = 10 + bonusEff / 2;
                for (int b = 0; b < 4; b++)
                {
                    var aliveEnemies = GetAliveSlots(opponent);
                    if (aliveEnemies.Count > 0)
                    {
                        var pick = aliveEnemies[UnityEngine.Random.Range(0, aliveEnemies.Count)];
                        DealSpellOrSkillDamage(pick.slotIndex, pick.currentMonster, perBolt, m, opponent, player, false, showVfx);
                    }
                    else
                    {
                        opponent.TakeLeaderDamage(perBolt);
                        if (showVfx) SpawnLeaderStatusVfx(oppIsHuman, $"-{perBolt} ⚡", new Color(1f, 0.9f, 0.25f));
                    }
                }
            }
            else if (act.Contains("Total Neural Paralysis"))
            {
                foreach (var es in enemies)
                {
                    es.currentMonster.silencedTurns = Mathf.Max(es.currentMonster.silencedTurns, 2);
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, es.slotIndex, "🔒 SILENCED!", new Color(1f, 0.88f, 0.25f));
                }
            }
            else if (act.Contains("Sudden Shock"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 14 + bonusEff, m, opponent, player, false, showVfx);
            }
            else if (act.Contains("Energy Surge"))
            {
                var target = GetBestAllyForOffensiveBuff(m, allies);
                target.bonusAtk += 8 + bonusEff;
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"⚡ +{8 + bonusEff} ATK!", new Color(1f, 0.92f, 0.25f));
            }
            // --- AIR ---
            else if (act.Contains("Sweeping Cyclone"))
            {
                if (enemies.Count > 0)
                {
                    BoardSlot best = enemies[0];
                    foreach (var es in enemies)
                    {
                        if (es.currentMonster.data.cost > best.currentMonster.data.cost) best = es;
                    }
                    if (best.currentMonster.HasPassiveSubstr("Earth Steadfastness") == null)
                    {
                        if (showVfx) SpawnSlotStatusVfx(oppIsHuman, best.slotIndex, "🌪️ BOUNCED!", new Color(0.5f, 0.95f, 0.8f));
                        if (opponent.hand.Count < 10) opponent.hand.Add(best.currentMonster.data);
                        best.ClearSlot();
                    }
                    else
                    {
                        DealSpellOrSkillDamage(best.slotIndex, best.currentMonster, 14 + bonusEff, m, opponent, player, false, showVfx);
                    }
                }
            }
            else if (act.Contains("Winds of Annihilation"))
            {
                foreach (var es in enemies)
                    DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, 22 + bonusEff, m, opponent, player, true, showVfx);
            }
            else if (act.Contains("Cutting Gale"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent);
                if (targetSlot != null)
                {
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 12 + bonusEff, m, opponent, player, false, showVfx);
                    var remaining = GetAliveSlots(opponent);
                    if (remaining.Count >= 2)
                    {
                        var s1 = remaining[0];
                        var s2 = remaining[remaining.Count - 1];
                        if (s1.currentMonster.HasPassiveSubstr("Earth Steadfastness") == null &&
                            s2.currentMonster.HasPassiveSubstr("Earth Steadfastness") == null)
                        {
                            var m1 = s1.currentMonster;
                            var m2 = s2.currentMonster;
                            s1.ClearSlot();
                            s2.ClearSlot();
                            s1.PlaceMonster(m2);
                            s2.PlaceMonster(m1);
                        }
                    }
                }
            }
            else if (act.Contains("Hastening Breeze"))
            {
                MonsterInstance target = !m.canAttack ? m : GetBestAllyForOffensiveBuff(m, allies);
                foreach (var al in allies)
                {
                    if (!al.currentMonster.canAttack && al.currentMonster != m &&
                        (al.currentMonster.baseAtk + al.currentMonster.bonusAtk) > (m.baseAtk + m.bonusAtk))
                    {
                        target = al.currentMonster;
                    }
                }
                target.canAttack = true;
                target.bonusAtk += 5 + bonusEff;
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"🌪️ READY! +{5 + bonusEff} ATK", new Color(0.45f, 1f, 0.78f));
            }
            // --- EARTH ---
            else if (act.Contains("Great Earth Fissure"))
            {
                BoardSlot targetSlot = GetEnemyTarget(slot, enemies, opponent);
                if (targetSlot == null || (targetSlot.slotIndex != 2 && targetSlot.slotIndex != 3))
                {
                    foreach (int vIdx in new[] { 2, 3 })
                    {
                        if (opponent.slots[vIdx] != null && opponent.slots[vIdx].IsOccupied)
                        {
                            targetSlot = opponent.slots[vIdx];
                            break;
                        }
                    }
                }
                if (targetSlot != null)
                {
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "🪨 CRUSHED!", new Color(0.85f, 0.65f, 0.35f));
                    targetSlot.currentMonster.currentDef = 0;
                    HandleMonsterDeath(opponent, player, targetSlot.slotIndex, targetSlot.currentMonster, m);
                }
            }
            else if (act.Contains("Immortal Mountain Shield") || act.Contains("Iron Dome Shield"))
            {
                player.teamImmuneTurns = Mathf.Max(player.teamImmuneTurns, 1);
                if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, "🛡️ IMMUNE 1 TURN!", new Color(0.95f, 0.85f, 0.35f));
            }
            else if (act.Contains("Earthquake Strike"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent);
                if (targetSlot != null)
                {
                    int idx = targetSlot.slotIndex;
                    DealSpellOrSkillDamage(idx, targetSlot.currentMonster, 15 + bonusEff, m, opponent, player, false, showVfx);
                    foreach (int adj in new[] { idx - 1, idx + 1 })
                    {
                        if (adj >= 0 && adj < 6 && opponent.slots[adj].IsOccupied)
                            DealSpellOrSkillDamage(adj, opponent.slots[adj].currentMonster, 5, m, opponent, player, false, showVfx);
                    }
                }
            }
            else if (act.Contains("Stone Fortification"))
            {
                var target = GetBestAllyForDefensiveBuff(m, allies);
                target.AddBonusDef(15 + bonusEff);
                if (!target.passives.Contains("Earth Steadfastness: Immune to displacement and bounce"))
                {
                    target.passives.Add("Earth Steadfastness: Immune to displacement and bounce");
                }
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"🪨 +{15 + bonusEff} DEF ANCHORED", new Color(0.85f, 0.65f, 0.38f));
            }
            // --- NATURE ---
            else if (act.Contains("Wrath of Nature"))
            {
                var treant = new MonsterData
                {
                    id = 9998, name = "9998", element = "Nature", tribe = "Giants", role = "Defender",
                    tier_num = 2, cost = 2, atk = 6, def = 30,
                    passives = new List<string> { "Poison Thorns: Deal 3 damage to attacking monster" },
                    description = "Summoned Treant Guardian (30 DEF, Poison Thorns)."
                };
                SpawnTokensOnFlanks(player, treant, false);
            }
            else if (act.Contains("Organic Blight Poison"))
            {
                foreach (var es in enemies)
                {
                    es.currentMonster.poisonDmg = Mathf.Max(es.currentMonster.poisonDmg, 10);
                    DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, 10 + bonusEff, m, opponent, player, false, showVfx);
                }
            }
            else if (act.Contains("Strangling Roots"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true);
                if (targetSlot != null)
                {
                    targetSlot.currentMonster.frozenTurns = Mathf.Max(targetSlot.currentMonster.frozenTurns, 2);
                    targetSlot.currentMonster.canAttack = false;
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "🌿 PARALYZED 2T!", new Color(0.35f, 0.90f, 0.40f));
                }
            }
            else if (act.Contains("Nectar of Life"))
            {
                int healAmt = 25 + bonusEff;
                player.HealLeader(healAmt, opponent);
                m.HealDefense(10);
                if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, "", Color.green, true, healAmt);
            }
            // --- LIGHT ---
            else if (act.Contains("Grand Light Resurrection"))
            {
                CardData bestFallen = null;
                foreach (var g in player.graveyard)
                {
                    if (g != null && g.cardType == CardType.Monster)
                    {
                        if (bestFallen == null || g.cost > bestFallen.cost) bestFallen = g;
                    }
                }
                int emptySlot = -1;
                for (int i = 0; i < 6; i++)
                {
                    if (player.slots[i] != null && !player.slots[i].IsOccupied)
                    {
                        emptySlot = i;
                        break;
                    }
                }
                if (bestFallen != null && emptySlot != -1)
                {
                    player.graveyard.Remove(bestFallen);
                    int turn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 1;
                    var revived = new MonsterInstance(bestFallen, turn);
                    revived.canAttack = true;
                    player.slots[emptySlot].PlaceMonster(revived);
                    if (showVfx) SpawnSlotStatusVfx(playerIsHuman, emptySlot, "✨ REVIVED!", new Color(1f, 0.95f, 0.55f));
                }
                else
                {
                    player.HealLeader(40 + bonusEff, opponent);
                    if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, "", Color.green, true, 40 + bonusEff);
                }
            }
            else if (act.Contains("Absolute Sun Judgment"))
            {
                int healAmt = 80 + bonusEff;
                player.HealLeader(healAmt, opponent);
                foreach (var al in allies) al.currentMonster.AddBonusDef(15);
                if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, "", Color.green, true, healAmt);
            }
            else if (act.Contains("Dawn Ray"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 14 + bonusEff, m, opponent, player, true, showVfx);
            }
            else if (act.Contains("Blessing of Light"))
            {
                var target = GetBestAllyForOffensiveBuff(m, allies);
                int boost = Mathf.Max(8, target.baseAtk) + bonusEff;
                target.bonusAtk += boost;
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"✨ +{boost} ATK!", new Color(1f, 0.95f, 0.50f));
            }
            // --- DARK ---
            else if (act.Contains("Total Eclipse"))
            {
                opponent.denyDrawSacTurns = Mathf.Max(opponent.denyDrawSacTurns, 1);
                if (showVfx) SpawnLeaderStatusVfx(oppIsHuman, "🌑 TOTAL ECLIPSE!", new Color(0.75f, 0.35f, 0.95f));
            }
            else if (act.Contains("Soul Reap"))
            {
                var sorted = new List<BoardSlot>(enemies);
                sorted.Sort((a, b) => a.currentMonster.currentDef.CompareTo(b.currentMonster.currentDef));
                int count = Mathf.Min(2, sorted.Count);
                for (int i = 0; i < count; i++)
                {
                    var es = sorted[i];
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, es.slotIndex, "💀 REAPED!", new Color(0.75f, 0.30f, 0.95f));
                    es.currentMonster.currentDef = 0;
                    HandleMonsterDeath(opponent, player, es.slotIndex, es.currentMonster, m);
                }
            }
            else if (act.Contains("Shadow Strike"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent);
                if (targetSlot != null)
                {
                    player.HealLeader(5, opponent);
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 14 + bonusEff, m, opponent, player, false, showVfx);
                    if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, "", Color.green, true, 5);
                }
            }
            else if (act.Contains("Soul Lock"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true);
                if (targetSlot != null)
                {
                    targetSlot.currentMonster.silencedTurns = Mathf.Max(targetSlot.currentMonster.silencedTurns, 2);
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "🔒 SOUL LOCKED!", new Color(0.75f, 0.35f, 0.95f));
                }
            }
            // --- MECHA ---
            else if (act.Contains("Destructive Ion Cannon"))
            {
                int cannonDmg = 50 + bonusEff;
                var targetSlot = GetEnemyTarget(slot, enemies, opponent);
                if (targetSlot != null)
                {
                    int rem = Mathf.Max(0, cannonDmg - targetSlot.currentMonster.currentDef);
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, cannonDmg, m, opponent, player, true, showVfx);
                    if (rem > 0)
                    {
                        opponent.TakeLeaderDamage(rem);
                        if (showVfx) SpawnLeaderStatusVfx(oppIsHuman, $"-{rem} HP!", new Color(1f, 0.3f, 0.3f));
                    }
                }
                else
                {
                    opponent.TakeLeaderDamage(cannonDmg);
                    if (showVfx) SpawnLeaderStatusVfx(oppIsHuman, $"-{cannonDmg} HP!", new Color(1f, 0.3f, 0.3f));
                }
            }
            else if (act.Contains("Guided Missile"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 15 + bonusEff, m, opponent, player, false, showVfx);
            }
            else if (act.Contains("System Reboot"))
            {
                m.burnDmg = 0;
                m.poisonDmg = 0;
                m.frozenTurns = 0;
                m.silencedTurns = 0;
                m.weakenedTurns = 0;
                m.HealDefense(20 + bonusEff);
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"⚙️ REBOOT +{20 + bonusEff} DEF", new Color(0.75f, 0.85f, 0.95f));
            }
            // --- GHOSTS / DARK ---
            else if (act.Contains("Graveyard Summoning"))
            {
                var ghost = new MonsterData
                {
                    id = 9999, name = "9999", element = "Dark", tribe = "Ghosts", role = "Attacker",
                    tier_num = 2, cost = 2, atk = 12, def = 14,
                    passives = new List<string> { "Ethereal Body: 35% immune to normal monster strikes" },
                    description = "Summoned Spectral Ghost (12 ATK, 14 DEF, Ethereal Body)."
                };
                SpawnTokensOnFlanks(player, ghost, true);
            }
            else if (act.Contains("Soul Drain"))
            {
                foreach (var es in enemies)
                {
                    var em = es.currentMonster;
                    em.baseAtk = Mathf.Max(1, em.baseAtk / 2);
                    em.currentDef = Mathf.Max(1, em.currentDef / 2);
                    em.weakenedTurns = Mathf.Max(em.weakenedTurns, 1);
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, es.slotIndex, "👻 STATS HALVED!", new Color(0.75f, 0.50f, 0.95f));
                }
            }
            else if (act.Contains("Touch of the Beyond"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 10 + bonusEff, m, opponent, player, true, showVfx);
            }
            else if (act.Contains("Spectral Terror"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true);
                if (targetSlot != null)
                {
                    targetSlot.currentMonster.frozenTurns = Mathf.Max(targetSlot.currentMonster.frozenTurns, 1);
                    targetSlot.currentMonster.canAttack = false;
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "👻 TERRIFIED!", new Color(0.75f, 0.50f, 0.95f));
                }
            }
            // --- ARCANE ---
            else if (act.Contains("Grand Mana Blast"))
            {
                foreach (var es in enemies)
                    DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, 20 + bonusEff, m, opponent, player, false, showVfx);
                opponent.TakeLeaderDamage(15);
                if (showVfx) SpawnLeaderStatusVfx(oppIsHuman, "-15 HP!", new Color(0.95f, 0.45f, 0.95f));
            }
            else if (act.Contains("Arcane Time Warp"))
            {
                foreach (var al in allies)
                {
                    al.currentMonster.canAttack = true;
                    al.currentMonster.bonusAtk += 5 + bonusEff;
                    if (showVfx) SpawnSlotStatusVfx(playerIsHuman, al.slotIndex, $"🔮 TIME WARP! +{5 + bonusEff}", new Color(0.95f, 0.50f, 0.95f));
                }
            }
            else if (act.Contains("Arcane Arrow"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 15 + bonusEff, m, opponent, player, false, showVfx);
            }
            else if (act.Contains("Elemental Shift"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true);
                if (targetSlot != null)
                {
                    targetSlot.currentMonster.weakenedTurns = Mathf.Max(targetSlot.currentMonster.weakenedTurns, 2);
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "🔮 WEAKENED 2T!", new Color(0.95f, 0.50f, 0.95f));
                }
            }
            else
            {
                m.AddBonusDef(15 + bonusEff);
                m.bonusAtk += 5;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"+5 ATK / +{15 + bonusEff} DEF", Color.cyan);
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
                        attackerSlotView.SetStandbyPaused(true);
                        yield return BattleAnimations.AnimateCardAttack(attackerSlotView.transform, targetPos, () =>
                        {
                            ApplyDamageImpact(attacker, attackerMonster, atkPower, slotIdx, defender, defenderSlot, defenderSlotView, defenderLeaderTransform, isDirectToLeader);
                        });
                        attackerSlotView.SetStandbyPaused(false);
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
                    if (attackerMonster.data.element != "Light" && attackerMonster.data.element != "Arcane" && attackerMonster.data.tribe != "Ghosts" && attackerMonster.data.element != "Ghosts" && attackerMonster.HasPassiveSubstr("Phase Shift") == null)
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
                    attacker.currentMana += 1;
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

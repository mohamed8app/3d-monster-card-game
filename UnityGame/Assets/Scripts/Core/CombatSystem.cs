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

        private static bool IsEnvElement(EnvironmentCardData env, string elem)
        {
            return env != null && (EnvironmentCardData.AreSameElement(env.buffedElement, elem) || EnvironmentCardData.AreSameElement(env.element, elem));
        }

        private static MonsterData CreateTokenData(int id, string element, string tribe, string role, int tier, int cost, int atk, int def, string passiveLine, string desc)
        {
            return new MonsterData
            {
                id = id,
                name = id.ToString(),
                element = element,
                tribe = tribe,
                role = role,
                tier_num = tier,
                cost = cost,
                atk = atk,
                def = def,
                passives = string.IsNullOrEmpty(passiveLine) ? new List<string>() : new List<string> { passiveLine },
                description = desc
            };
        }

        private static int GetLeaderDmgDealt(PlayerState victim, int dmg)
        {
            if (victim == null || dmg <= 0) return 0;
            int preHp = victim.currentHp;
            victim.TakeLeaderDamage(dmg);
            return Mathf.Max(0, preHp - victim.currentHp);
        }

        public void ApplyOnSummonPassives(PlayerState player, PlayerState opponent, int slot, MonsterInstance m)
        {
            if (m == null || opponent == null) return;

            var oppSlot = (slot >= 0 && slot < 6) ? opponent.slots[slot] : null;
            var oppM = (oppSlot != null && oppSlot.IsOccupied) ? oppSlot.currentMonster : null;
            var activeEnv = GameManager.Instance != null ? GameManager.Instance.activeEnvironment : null;

            if (oppM != null && oppM.HasPassiveSubstr("Light of Truth") != null &&
                m.HasPassiveSubstr("Immunity to Light") == null && m.HasPassiveSubstr("Immunity to Electric") == null)
            {
                m.silencedTurns = Mathf.Max(m.silencedTurns, 2);
                return;
            }

            foreach (var p in m.passives)
            {
                if (p.Contains("Light of Truth") && oppM != null &&
                    oppM.HasPassiveSubstr("Immunity to Light") == null && oppM.HasPassiveSubstr("Immunity to Electric") == null)
                {
                    oppM.silencedTurns = Mathf.Max(oppM.silencedTurns, 3);
                }
                else if (p.Contains("Glacier Bulk"))
                {
                    m.AddBonusDef(7);
                }
                else if (p.Contains("Electromagnetic Shield"))
                {
                    m.bubbleShield = true;
                    m.AddBonusDef(4);
                }
                else if (p.Contains("Tidal Mending"))
                {
                    player.HealLeader(3, opponent);
                }
                else if (p.Contains("Battery Pulse"))
                {
                    player.currentMana += 1;
                }
                else if (p.Contains("Bedrock Anchor"))
                {
                    player.shield += 5;
                }
                else if (p.Contains("Dusk Drain"))
                {
                    int dealtDd = GetLeaderDmgDealt(opponent, 3);
                    if (dealtDd > 0) player.HealLeader(dealtDd, opponent);
                }
                else if (p.Contains("Spark Hex") && oppM != null && oppM.IsAlive)
                {
                    DealSpellOrSkillDamage(slot, oppM, 4, m, opponent, player, false, true);
                }
                else if (p.Contains("Updraft Guard"))
                {
                    foreach (int adj in new[] { slot - 1, slot + 1 })
                    {
                        if (adj >= 0 && adj < 6 && player.slots[adj] != null && player.slots[adj].IsOccupied)
                        {
                            m.AddBonusDef(4);
                            break;
                        }
                    }
                }
                else if (p.Contains("Gale Step"))
                {
                    foreach (int adj in new[] { slot - 1, slot + 1 })
                    {
                        if (adj >= 0 && adj < 6 && player.slots[adj] != null && player.slots[adj].IsOccupied)
                            player.slots[adj].currentMonster.bonusAtk += 2;
                    }
                }
                else if (p.Contains("Leaf Blessing"))
                {
                    foreach (int adj in new[] { slot - 1, slot + 1 })
                    {
                        if (adj >= 0 && adj < 6 && player.slots[adj] != null && player.slots[adj].IsOccupied)
                            player.slots[adj].currentMonster.HealDefense(3);
                    }
                }
                else if (p.Contains("Radiant Banner"))
                {
                    foreach (int adj in new[] { slot - 1, slot + 1 })
                    {
                        if (adj >= 0 && adj < 6 && player.slots[adj] != null && player.slots[adj].IsOccupied)
                        {
                            player.slots[adj].currentMonster.bonusAtk += 1;
                            player.slots[adj].currentMonster.AddBonusDef(2);
                        }
                    }
                }
                else if (p.Contains("Celestial Grace"))
                {
                    foreach (int adj in new[] { slot - 1, slot + 1 })
                    {
                        if (adj >= 0 && adj < 6 && player.slots[adj] != null && player.slots[adj].IsOccupied)
                        {
                            var al = player.slots[adj].currentMonster;
                            al.AddBonusDef(4);
                            if (al.burnDmg > 0) al.burnDmg = 0;
                            else if (al.poisonDmg > 0) al.poisonDmg = 0;
                            else if (al.weakenedTurns > 0) al.weakenedTurns = 0;
                            else if (al.frozenTurns > 0) al.frozenTurns = 0;
                            else if (al.silencedTurns > 0) al.silencedTurns = 0;
                        }
                    }
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
                    m.AddBonusDef(3);
                }
                else if (p.Contains("Flank Strike") && (slot == 0 || slot == 1 || slot == 4 || slot == 5))
                {
                    m.bonusAtk += 5;
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
                else if (p.Contains("Valkyrie Charge"))
                {
                    m.bonusAtk += 4;
                }
                else if (p.Contains("Fire Blaze"))
                {
                    int boosted = 0;
                    foreach (int adj in new[] { slot - 1, slot + 1 })
                    {
                        if (adj >= 0 && adj < 6 && player.slots[adj] != null && player.slots[adj].IsOccupied)
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
                            s.currentMonster.AddBonusDef(4);
                    }
                }
                else if (p.Contains("Ocean Protection"))
                {
                    MonsterInstance target = null;
                    int minDef = int.MaxValue;
                    for (int i = 0; i < 6; i++)
                    {
                        if (i != slot && player.slots[i] != null && player.slots[i].IsOccupied && player.slots[i].currentMonster.currentDef < minDef)
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
                    int adjAllies = 0;
                    foreach (int adj in new[] { slot - 1, slot + 1 })
                    {
                        if (adj >= 0 && adj < 6 && player.slots[adj] != null && player.slots[adj].IsOccupied && player.slots[adj].currentMonster.IsAlive)
                            adjAllies++;
                    }
                    m.bonusAtk += adjAllies * 3;
                }
                else if (p.Contains("Chitin Swarm"))
                {
                    int otherInsects = 0;
                    for (int i = 0; i < 6; i++)
                    {
                        if (i != slot && player.slots[i] != null && player.slots[i].IsOccupied &&
                            player.slots[i].currentMonster.IsAlive && player.slots[i].currentMonster.data.tribe == "Insects")
                        {
                            otherInsects++;
                        }
                    }
                    if (otherInsects > 0) m.AddBonusDef(otherInsects * 3);
                }
                else if (p.Contains("Sylvan Bloom"))
                {
                    int spawnSlot = -1;
                    foreach (int adj in new[] { slot - 1, slot + 1 })
                    {
                        if ((adj == 0 || adj == 1 || adj == 4 || adj == 5) && player.slots[adj] != null && !player.slots[adj].IsOccupied)
                        {
                            spawnSlot = adj;
                            break;
                        }
                    }
                    if (spawnSlot == -1)
                    {
                        foreach (int ws in new[] { 0, 1, 4, 5 })
                        {
                            if (player.slots[ws] != null && !player.slots[ws].IsOccupied)
                            {
                                spawnSlot = ws;
                                break;
                            }
                        }
                    }
                    if (spawnSlot != -1)
                    {
                        int turn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 1;
                        var sapling = CreateTokenData(9997, "Nature", "Beasts", "Defender", 1, 1, 4, 12, "", "Summoned Sapling Sprout.");
                        var tok = new MonsterInstance(sapling, turn);
                        tok.canAttack = false;
                        player.slots[spawnSlot].PlaceMonster(tok);
                        bool isHuman = GameManager.Instance != null && player == GameManager.Instance.humanPlayer;
                        SpawnSlotStatusVfx(isHuman, spawnSlot, "🌱 SAPLING!", MonsterArtLoader.GetElementColor("Nature"));
                    }
                }
                else if (p.Contains("Nourishing Roots"))
                {
                    player.power += 1;
                }
                else if (p.Contains("Black Nightmare"))
                {
                    if (opponent.hand.Count > 0)
                    {
                        int lastIdx = opponent.hand.Count - 1;
                        var sac = opponent.hand[lastIdx];
                        opponent.hand.RemoveAt(lastIdx);
                        if (sac != null && sac.id < 9000) opponent.graveyard.Add(sac);
                    }
                    else
                    {
                        opponent.TakeLeaderDamage(12);
                    }
                }
                else if (p.Contains("Tidal Bounce") && oppM != null && (oppM.baseAtk + oppM.bonusAtk) < 10)
                {
                    if (!oppM.HasEarthSteadfastness(activeEnv) && oppM.HasPassiveSubstr("Nimble Agility") == null && oppM.HasPassiveSubstr("Immunity to Air") == null)
                    {
                        if (oppM.data != null && oppM.data.id < 9000)
                            opponent.AddCardToHandWithBurnLimit(oppM.data);
                        oppSlot.ClearSlot();
                    }
                }
            }
        }

        public void TriggerStartOfTurnPassives(PlayerState player, PlayerState opponent)
        {
            player.freeSkillManaZero = false;
            var activeEnv = GameManager.Instance != null ? GameManager.Instance.activeEnvironment : null;
            int lvlIdx = activeEnv != null ? Mathf.Clamp(activeEnv.level - 1, 0, 2) : 0;
            int currentTurn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 1;

            bool hasNatAlly = false, hasEleAlly = false, hasLgtAlly = false;
            for (int i = 0; i < 6; i++)
            {
                var s = player.slots[i];
                if (s == null || !s.IsOccupied || !s.currentMonster.IsAlive) continue;
                string el = s.currentMonster.data.element;
                if (el == "Nature") hasNatAlly = true;
                else if (el == "Electric") hasEleAlly = true;
                else if (el == "Light") hasLgtAlly = true;
            }

            if (IsEnvElement(activeEnv, "Electric") && hasEleAlly)
            {
                player.currentMana += (lvlIdx == 2 ? 2 : 1);
            }
            else if (IsEnvElement(activeEnv, "Nature") && hasNatAlly)
            {
                player.HealLeader(new[] { 6, 10, 14 }[lvlIdx], opponent);
            }
            else if (IsEnvElement(activeEnv, "Light") && hasLgtAlly)
            {
                // V2 Constitution: ZERO Shields — Light Arena heals Commander +7/+12/+16 HP
                player.HealLeader(new[] { 7, 12, 16 }[lvlIdx], opponent);
            }

            for (int slot = 0; slot < 6; slot++)
            {
                var pSlot = player.slots[slot];
                if (pSlot == null || !pSlot.IsOccupied) continue;
                var m = pSlot.currentMonster;

                if (IsEnvElement(activeEnv, "Water") && m.data.element == "Water")
                {
                    m.burnDmg = 0;
                    m.poisonDmg = 0;
                    m.bleedDmg = 0;
                    m.weakenedTurns = 0;
                    m.HealDefense(new[] { 3, 5, 8 }[lvlIdx]);
                }
                else if (IsEnvElement(activeEnv, "Nature"))
                {
                    if (m.data.element == "Nature")
                    {
                        m.ApplyPermBuff(0, new[] { 1, 2, 3 }[lvlIdx]);
                    }
                    else if (m.currentDef > 1)
                    {
                        int loss = Mathf.Min(new[] { 1, 2, 3 }[lvlIdx], m.currentDef - 1);
                        m.currentDef -= loss;
                        m.baseDef = Mathf.Max(m.currentDef, m.baseDef - loss);
                    }
                }
                else if (IsEnvElement(activeEnv, "Light") && m.data.element == "Light")
                {
                    m.poisonDmg = 0;
                    m.burnDmg = 0;
                    m.bleedDmg = 0;
                    m.weakenedTurns = 0;
                    m.HealDefense(new[] { 3, 5, 8 }[lvlIdx]);
                }
                else if (IsEnvElement(activeEnv, "Fire") && m.data.element != "Fire")
                {
                    // Fire Arena Scorch on Foreign units (1/2/3 dmg)
                    m.currentDef -= new[] { 1, 2, 3 }[lvlIdx];
                }
                else if (IsEnvElement(activeEnv, "Desert") && m.data.element != "Desert")
                {
                    // Desert Arena Sandstorm on Foreign units (2/3/4 dmg + 1/1/2 Bleed)
                    m.currentDef -= new[] { 2, 3, 4 }[lvlIdx];
                    if (m.currentDef > 0) m.bleedDmg += new[] { 1, 1, 2 }[lvlIdx];
                }

                if (m.burnDmg > 0)
                {
                    if (m.HasPassiveSubstr("Moisture Shield") != null || m.HasPassiveSubstr("Immunity to Fire") != null || m.HasPassiveSubstr("Water Resilience") != null)
                        m.burnDmg = 0;
                    else
                        m.currentDef -= m.burnDmg;
                }

                if (m.poisonDmg > 0)
                {
                    if (m.HasPassiveSubstr("Immunity to Poison") != null)
                        m.poisonDmg = 0;
                    else
                    {
                        int pd = m.HasPassiveSubstr("Blight Ward") != null ? Mathf.RoundToInt(m.poisonDmg * 0.65f) : m.poisonDmg;
                        m.currentDef -= Mathf.Max(1, pd);
                    }
                }

                if (!m.IsAlive)
                {
                    if (!TrySalvationSave(player, slot, m))
                    {
                        HandleMonsterDeath(player, opponent, slot, m, null);
                    }
                    continue;
                }

                if (m.frozenTurns > 0)
                {
                    if (m.HasPassiveSubstr("Immunity to Ice") != null)
                        m.frozenTurns = 0;
                    else
                    {
                        int dec = m.HasPassiveSubstr("Freeze Resistance") != null ? 2 : 1;
                        m.frozenTurns = Mathf.Max(0, m.frozenTurns - dec);
                        if (m.frozenTurns > 0)
                            m.canAttack = false;
                        else if (m.summonedTurn < currentTurn)
                            m.canAttack = true;
                    }
                }

                if (m.silencedTurns > 0)
                {
                    if (m.HasPassiveSubstr("Immunity to Electric") != null)
                        m.silencedTurns = 0;
                    else
                        m.silencedTurns--;
                }
                if (m.weakenedTurns > 0) m.weakenedTurns--;

                var oppSlot = opponent.slots[slot];
                var oppM = (oppSlot != null && oppSlot.IsOccupied) ? oppSlot.currentMonster : null;

                if (m.silencedTurns <= 0)
                {
                    foreach (var p in m.passives)
                    {
                        if (p.Contains("Self-Ignition") && oppM != null && oppM.HasPassiveSubstr("Immunity to Fire") == null)
                        {
                            oppM.burnDmg = Mathf.Max(oppM.burnDmg, 3);
                            oppM.currentDef -= 3;
                        }
                        else if (p.Contains("Blazing Fire") && oppM != null && oppM.HasPassiveSubstr("Immunity to Fire") == null)
                        {
                            oppM.burnDmg = Mathf.Max(oppM.burnDmg, 8);
                            oppM.currentDef -= 8;
                        }
                        else if (p.Contains("Light Chill") && oppM != null && oppM.HasPassiveSubstr("Immunity to Ice") == null)
                        {
                            oppM.currentDef -= 1;
                        }
                        else if (p.Contains("Slow Poison") && oppM != null && oppM.HasPassiveSubstr("Immunity to Poison") == null)
                        {
                            oppM.poisonDmg = Mathf.Max(oppM.poisonDmg, 6);
                            oppM.currentDef -= 6;
                        }
                        else if (p.Contains("Spectral Presence") && oppM != null && oppM.HasPassiveSubstr("Sacred Light Ward") == null)
                        {
                            oppM.weakenedTurns = Mathf.Max(oppM.weakenedTurns, 1);
                        }
                        else if (p.Contains("Chitin Plating"))
                        {
                            m.HealDefense(3);
                        }
                        else if (p.Contains("Vital Regeneration"))
                        {
                            m.HealDefense(4);
                        }
                        else if (p.Contains("Water Resilience"))
                        {
                            m.burnDmg = 0;
                            m.HealDefense(5);
                        }
                        else if (p.Contains("Soothing Current"))
                        {
                            player.HealLeader(5, opponent);
                            m.HealDefense(2);
                        }
                        else if (p.Contains("Holy Aura"))
                        {
                            player.HealLeader(4, opponent);
                            player.shield += 2;
                        }
                        else if (p.Contains("Dawn Buckler"))
                        {
                            player.HealLeader(2, opponent);
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
                            m.bonusAtk += 3;
                        }
                        else if (p.Contains("Molten Anvil"))
                        {
                            foreach (var s in player.slots)
                            {
                                if (s != null && s.IsOccupied && s.currentMonster.IsAlive && s.currentMonster.data.tribe == "Dwarves")
                                    s.currentMonster.HealDefense(3);
                            }
                        }
                        else if (p.Contains("Tidal Vortex Aura"))
                        {
                            foreach (int adj in new[] { slot - 1, slot + 1 })
                            {
                                if (adj >= 0 && adj < 6 && player.slots[adj] != null && player.slots[adj].IsOccupied && player.slots[adj].currentMonster.IsAlive)
                                    player.slots[adj].currentMonster.HealDefense(4);
                            }
                        }
                        else if (p.Contains("Verdant Canopy"))
                        {
                            foreach (int adj in new[] { slot - 1, slot + 1 })
                            {
                                if (adj >= 0 && adj < 6 && player.slots[adj] != null && player.slots[adj].IsOccupied && player.slots[adj].currentMonster.IsAlive)
                                    player.slots[adj].currentMonster.HealDefense(3);
                            }
                        }
                        else if (p.Contains("Tailwind Gust"))
                        {
                            foreach (int ws in new[] { 0, 1, 4, 5 })
                            {
                                if (player.slots[ws] != null && player.slots[ws].IsOccupied)
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

            CommanderSpiritEffects.OnStartOfTurn(player, opponent);
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
                if (adj >= 0 && adj < 6 && owner.slots[adj] != null && owner.slots[adj].IsOccupied)
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

            BattleUIManager.Instance?.PlayMonsterDeathEffect(owner, slot, deadM);
            MonsterCardGame.Audio.SoundManager.Instance?.PlayCardDeath();

            if (slot >= 0 && slot < 6 && owner.slots[slot] != null && owner.slots[slot].currentMonster == deadM)
            {
                owner.slots[slot].ClearSlot();
            }
            if (deadM.data != null && deadM.data.id < 9000)
            {
                owner.graveyard.Add(deadM.data);
                owner.RecordMonsterDeath(deadM.data.id, GameManager.Instance != null ? GameManager.Instance.currentTurn : 1);
            }

            if (deadM.silencedTurns <= 0)
            {
                foreach (var p in deadM.passives)
                {
                    if (p.Contains("Shadow Chitin"))
                    {
                        if (slot >= 0 && slot < 6 && killerOwner.slots[slot] != null && killerOwner.slots[slot].IsOccupied)
                        {
                            var oppS = killerOwner.slots[slot].currentMonster;
                            oppS.TakeDamage(3);
                            if (!oppS.IsAlive) HandleMonsterDeath(killerOwner, owner, slot, oppS, deadM);
                        }
                        else
                        {
                            killerOwner.TakeLeaderDamage(3);
                        }
                    }
                    else if (p.Contains("Thermal Explosion"))
                    {
                        if (slot >= 0 && slot < 6 && killerOwner.slots[slot] != null && killerOwner.slots[slot].IsOccupied)
                        {
                            var t = killerOwner.slots[slot].currentMonster;
                            t.TakeDamage(10);
                            if (!t.IsAlive) HandleMonsterDeath(killerOwner, owner, slot, t, deadM);
                        }
                        else
                        {
                            killerOwner.TakeLeaderDamage(10);
                        }
                    }
                    else if (p.Contains("Cinder Vengeance"))
                    {
                        foreach (var es in GetAliveSlots(killerOwner))
                        {
                            if (es.currentMonster.HasPassiveSubstr("Immunity to Fire") == null)
                            {
                                es.currentMonster.TakeDamage(6);
                                if (!es.currentMonster.IsAlive) HandleMonsterDeath(killerOwner, owner, es.slotIndex, es.currentMonster, deadM);
                            }
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
                    else if (p.Contains("Curse of Doom") && killerM != null && killerM.IsAlive)
                    {
                        if (killerM.HasPassiveSubstr("Immunity to Dark") == null && killerM.HasPassiveSubstr("Sacred Light Ward") == null)
                        {
                            killerM.currentDef = 0;
                            for (int ks = 0; ks < 6; ks++)
                            {
                                if (killerOwner.slots[ks] != null && killerOwner.slots[ks].currentMonster == killerM)
                                {
                                    HandleMonsterDeath(killerOwner, owner, ks, killerM, deadM);
                                    break;
                                }
                            }
                        }
                    }
                    else if (p.Contains("Spirit Possession") && slot >= 0 && slot < 6 && killerOwner.slots[slot] != null && killerOwner.slots[slot].IsOccupied)
                    {
                        var t = killerOwner.slots[slot].currentMonster;
                        t.frozenTurns = Mathf.Max(t.frozenTurns, 2);
                        t.silencedTurns = Mathf.Max(t.silencedTurns, 2);
                        t.canAttack = false;
                    }
                    else if (p.Contains("Soul Phylactery") && !deadM.soulPhylacteryUsed && slot >= 0 && slot < 6 &&
                             owner.slots[slot] != null && !owner.slots[slot].IsOccupied)
                    {
                        int pickIdx = -1;
                        for (int gi = owner.graveyard.Count - 1; gi >= 0; gi--)
                        {
                            var g = owner.graveyard[gi];
                            if (g != null && g.cardType == CardType.Monster && (g.tribe == "Ghosts" || g.element == "Dark"))
                            {
                                pickIdx = gi;
                                break;
                            }
                        }
                        if (pickIdx >= 0)
                        {
                            var pick = owner.graveyard[pickIdx];
                            owner.graveyard.RemoveAt(pickIdx);
                            int turn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 1;
                            var rev = new MonsterInstance(pick, turn)
                            {
                                soulPhylacteryUsed = true,
                                canAttack = false
                            };
                            owner.slots[slot].PlaceMonster(rev);
                            bool ownerIsHuman = GameManager.Instance != null && owner == GameManager.Instance.humanPlayer;
                            SpawnSlotStatusVfx(ownerIsHuman, slot, "👻 PHYLACTERY!", new Color(0.75f, 0.50f, 0.95f));
                        }
                    }
                }
            }

            // Global Death Passives (Dread of the Dead, Nightmare Harvest, Dark Environment)
            foreach (var ks in GetAliveSlots(killerOwner))
            {
                if (ks.currentMonster.HasPassiveSubstr("Dread of the Dead") != null)
                {
                    ks.currentMonster.bonusAtk += 3;
                    ks.currentMonster.AddBonusDef(5);
                }
            }

            var activeEnv = GameManager.Instance != null ? GameManager.Instance.activeEnvironment : null;
            int envLvlIdx = activeEnv != null ? Mathf.Clamp(activeEnv.level - 1, 0, 2) : 0;
            int darkBonus = new[] { 2, 3, 4 }[envLvlIdx];
            bool darkActive = IsEnvElement(activeEnv, "Dark");

            foreach (var pair in new[] { (owner, killerOwner), (killerOwner, owner) })
            {
                var poss = pair.Item1;
                var enemy = pair.Item2;
                int darkCount = 0;
                foreach (var s in GetAliveSlots(poss))
                {
                    if (s.currentMonster.HasPassiveSubstr("Nightmare Harvest") != null)
                        enemy.TakeLeaderDamage(4);
                    if (darkActive && s.currentMonster.data.element == "Dark")
                    {
                        s.currentMonster.bonusAtk += darkBonus;
                        darkCount++;
                    }
                }
                if (darkActive && darkCount > 0)
                    enemy.TakeLeaderDamage(darkBonus);
            }

            CommanderSpiritEffects.OnMonsterDied(owner, killerOwner, deadM, killerM);
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

        public enum AbilityTargetMode
        {
            None,
            SingleEnemy,
            SingleAlly
        }

        private static readonly Dictionary<string, string> ElemImmunityMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Ice", "Immunity to Ice" },
            { "Fire", "Immunity to Fire" },
            { "Nature", "Immunity to Poison" },
            { "Dark", "Immunity to Dark" },
            { "Earth", "Immunity to Earth" },
            { "Air", "Immunity to Air" },
            { "Water", "Immunity to Water" },
            { "Light", "Immunity to Light" },
            { "Electric", "Immunity to Electric" }
        };

        private static readonly Dictionary<string, string> ElemResistMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Air", "Wind Resistance" },
            { "Ice", "Freeze Resistance" },
            { "Electric", "Electric Resistance" },
            { "Earth", "Earth Resistance" },
            { "Dark", "Dark Resistance" },
            { "Fire", "Pyroclast Ward" },
            { "Water", "Tidebreaker Ward" },
            { "Nature", "Blight Ward" },
            { "Light", "Sunlit Ward" },
            { "Arcane", "Spellbinder Ward" },
            { "Desert", "Desert Resistance" }
        };

        public AbilityTargetMode GetSkillTargetMode(string act, PlayerState owner = null, PlayerState opponent = null)
        {
            if (string.IsNullOrEmpty(act)) return AbilityTargetMode.None;

            // V2 Structured Active Skill targeting by formatted label
            if (act.Contains("Fortify Ally") || act.Contains("Overclock Spark") ||
                act.Contains("Windfury Blessing") || act.Contains("Tactical Recall (Ally)") ||
                act.Contains("Buff Ally"))
            {
                return AbilityTargetMode.SingleAlly;
            }
            if (act.Contains("Precision Strike") || act.Contains("Sweeping Cleave") ||
                act.Contains("Banish Recall") || act.Contains("Puppeteer Strings") ||
                act.Contains("Abyssal Devour") || act.Contains("Chain Pull Hook") ||
                act.Contains("Shifting Curse") || act.Contains("Dmg Single") ||
                act.Contains("Cleave Enemy") || act.Contains("Recall Enemy"))
            {
                return AbilityTargetMode.SingleEnemy;
            }

            // 1. Single-Ally Targeted Skills (14)
            if (act.Contains("Purging Flame") ||
                act.Contains("Healing Stream") ||
                act.Contains("Safety Bubble") ||
                act.Contains("Snow Wall") ||
                act.Contains("Energy Surge") ||
                act.Contains("Hastening Breeze") ||
                act.Contains("Stone Fortification") ||
                act.Contains("Blessing of Light") ||
                act.Contains("Tempest Tailwind") ||
                act.Contains("Sanctified Aegis") ||
                act.Contains("Pack Alpha Roar") ||
                act.Contains("Dust Mirage") ||
                act.Contains("Mirage Shift") ||
                act.Contains("Arcane Infusion"))
            {
                return AbilityTargetMode.SingleAlly;
            }

            // 2. Single-Enemy Targeted Skills (35 + conditional Destructive Ion Cannon)
            if (act.Contains("Volcanic Eruption") ||
                act.Contains("Fireball") ||
                act.Contains("Ember Blast") ||
                act.Contains("Lava Torrent") ||
                act.Contains("Cinder Nova") ||
                act.Contains("Water Vortex") ||
                act.Contains("Abyssal Geyser") ||
                act.Contains("Frost Shatter") ||
                act.Contains("Ice Spear") ||
                act.Contains("Cryo Lance") ||
                act.Contains("Sudden Shock") ||
                act.Contains("Static Jolt") ||
                act.Contains("Sweeping Cyclone") ||
                act.Contains("Cutting Gale") ||
                act.Contains("Great Earth Fissure") ||
                act.Contains("Earthquake Strike") ||
                act.Contains("Tectonic Slam") ||
                act.Contains("Strangling Roots") ||
                act.Contains("Venom Thorn Volley") ||
                act.Contains("Blighted Fang") ||
                act.Contains("Dawn Ray") ||
                act.Contains("Searing Judgment") ||
                act.Contains("Shadow Strike") ||
                act.Contains("Blood Eclipse Drain") ||
                act.Contains("Soul Lock") ||
                act.Contains("Guided Missile") ||
                act.Contains("Touch of the Beyond") ||
                act.Contains("Spectral Terror") ||
                act.Contains("Arcane Arrow") ||
                act.Contains("Astral Bolt") ||
                act.Contains("Elemental Shift") ||
                act.Contains("Quicksand Snare") ||
                act.Contains("Dune Blast") ||
                act.Contains("Quicksand Sinkhole") ||
                act.Contains("Pharaoh's Doom Curse"))
            {
                return AbilityTargetMode.SingleEnemy;
            }

            if (act.Contains("Destructive Ion Cannon"))
            {
                if (opponent != null && GetAliveSlots(opponent).Count > 0)
                    return AbilityTargetMode.SingleEnemy;
                return AbilityTargetMode.None;
            }

            return AbilityTargetMode.None;
        }

        public bool SkillTargetsCanHitStealth(string act)
        {
            if (string.IsNullOrEmpty(act)) return false;
            return act.Contains("Dawn Ray") ||
                   act.Contains("Destructive Ion Cannon") ||
                   act.Contains("Touch of the Beyond") ||
                   act.Contains("Volcanic Eruption") ||
                   act.Contains("Great Earth Fissure") ||
                   act.Contains("Frost Shatter") ||
                   act.Contains("Sweeping Cyclone") ||
                   act.Contains("Water Vortex") ||
                   act.Contains("Strangling Roots") ||
                   act.Contains("Soul Lock") ||
                   act.Contains("Spectral Terror") ||
                   act.Contains("Elemental Shift") ||
                   act.Contains("Arcane Arrow") ||
                   act.Contains("Guided Missile") ||
                   act.Contains("Abyssal Geyser") ||
                   act.Contains("Astral Bolt") ||
                   act.Contains("Searing Judgment");
        }

        public List<BoardSlot> GetTargetableEnemySlots(PlayerState opponent, string act)
        {
            var result = new List<BoardSlot>();
            if (opponent == null || opponent.slots == null) return result;
            bool bypassStealth = SkillTargetsCanHitStealth(act);
            foreach (var s in opponent.slots)
            {
                if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive)
                {
                    if (!bypassStealth && s.currentMonster.HasPassiveSubstr("Night Stealth") != null)
                        continue;
                    result.Add(s);
                }
            }
            return result;
        }

        public static string GetShortSkillName(string act)
        {
            if (string.IsNullOrEmpty(act)) return "Special Ability";
            int colon = act.IndexOf(':');
            if (colon > 0) return act.Substring(0, colon).Trim();
            int paren = act.IndexOf('(');
            if (paren > 0) return act.Substring(0, paren).Trim();
            return act.Trim();
        }

        private float CalcArmorPen(MonsterInstance srcM, EnvironmentCardData activeEnv)
        {
            float pen = 0f;
            if (srcM != null)
            {
                if (srcM.HasPassiveSubstr("Phase Shift") != null || srcM.HasPassiveSubstr("Targeting System") != null)
                    pen = Mathf.Max(pen, 1f);
                if (srcM.HasPassiveSubstr("Aerial Pierce") != null)
                    pen = Mathf.Max(pen, 0.40f);
                if (IsEnvElement(activeEnv, "Air") && srcM.data != null && srcM.data.element == "Air")
                {
                    int li = Mathf.Clamp(activeEnv.level - 1, 0, 2);
                    pen = Mathf.Max(pen, new[] { 0.25f, 0.35f, 0.50f }[li]);
                }
            }
            return Mathf.Min(1f, pen);
        }

        private int ApplyIncomingDefenseMods(MonsterInstance targetM, PlayerState opponent, MonsterInstance srcM, int dmg,
            bool isCombat, bool isSpell, bool isSingleSpell, bool ignoreImmunity)
        {
            if (dmg <= 0 || targetM == null) return 0;
            var activeEnv = GameManager.Instance != null ? GameManager.Instance.activeEnvironment : null;
            float pen = ignoreImmunity ? 1f : CalcArmorPen(srcM, activeEnv);
            string targetElem = targetM.data != null ? targetM.data.element : "";

            if (!ignoreImmunity)
            {
                if (srcM != null && srcM.data != null)
                {
                    string srcElem = srcM.data.element ?? "";
                    bool hasTargetingSys = srcM.HasPassiveSubstr("Targeting System") != null;
                    if (!hasTargetingSys)
                    {
                        if (ElemImmunityMap.TryGetValue(srcElem, out string immKey) && targetM.HasPassiveSubstr(immKey) != null)
                            return 0;
                        if (targetM.HasPassiveSubstr("Sacred Light Ward") != null && (srcM.data.tribe == "Ghosts" || srcElem == "Dark"))
                            return 0;
                    }
                    if (ElemResistMap.TryGetValue(srcElem, out string resKey) && targetM.HasPassiveSubstr(resKey) != null)
                        dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * (1f - 0.35f * (1f - pen * 0.5f))));
                    if (srcElem == "Fire" && targetM.HasPassiveSubstr("Moisture Shield") != null)
                        dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * 0.75f));
                }

                if (isSpell && targetM.HasPassiveSubstr("Spellbinder Ward") != null)
                    dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * 0.65f));

                if (isSingleSpell && opponent != null && opponent.slots != null)
                {
                    foreach (var s in opponent.slots)
                    {
                        if (s != null && s.IsOccupied && s.currentMonster.IsAlive && s.currentMonster.HasPassiveSubstr("Thicket Cover") != null)
                        {
                            dmg = Mathf.Max(1, dmg - Mathf.RoundToInt(dmg * 0.25f));
                            break;
                        }
                    }
                }
            }

            if (activeEnv != null)
            {
                float defArmor = activeEnv.GetDefenseArmor(targetElem);
                if (defArmor > 0f && !ignoreImmunity)
                {
                    float effArmor = defArmor * (1f - pen);
                    dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * (1f - effArmor)));
                }
                float oppVuln = activeEnv.GetEnemyVulnerability(targetElem);
                if (oppVuln > 0f)
                    dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * (1f + oppVuln)));
            }

            if (!ignoreImmunity && pen < 1f)
            {
                float mult = 1f - pen;
                int flatRed = 0;
                if (targetM.HasPassiveSubstr("Rock Hardness") != null) flatRed += 5;
                if (targetM.HasPassiveSubstr("Granite Armor") != null) flatRed += 6;
                if (targetM.HasPassiveSubstr("Molten Anvil") != null) flatRed += 3;
                flatRed += CommanderSpiritEffects.GetCombatIncomingReduction(opponent, targetM);
                flatRed += V2CardMechanicsEngine.GetLeaderIncomingReduction(opponent, targetM);
                flatRed += V2CardMechanicsEngine.GetCovenantIncomingReduction(opponent);
                if (isCombat)
                {
                    if (targetM.HasPassiveSubstr("Cinder Shield") != null) flatRed += 3;
                    if (targetM.HasPassiveSubstr("Protective Flame") != null) flatRed += 4;
                    if (targetM.HasPassiveSubstr("Steel Armor") != null) flatRed += 7;
                }
                else
                {
                    if (targetM.HasPassiveSubstr("Barkskin Guard") != null) flatRed += 3;
                    if (targetM.HasPassiveSubstr("Spellward Coat") != null) flatRed += 4;
                    if (targetM.HasPassiveSubstr("Mana Shield") != null) flatRed += 4;
                }
                if (flatRed > 0)
                    dmg = Mathf.Max(1, dmg - Mathf.RoundToInt(flatRed * mult));
            }

            return Mathf.Max(0, dmg);
        }

        public void DealSpellOrSkillDamage(int targetSlot, MonsterInstance targetM, int dmg, MonsterInstance sourceM, PlayerState opponent, PlayerState player, bool ignoreImmunity = false, bool showVfx = false, bool isSpell = false, bool isSingleSpell = false)
        {
            if (targetM == null || !targetM.IsAlive) return;

            bool targetIsHuman = GameManager.Instance != null && (opponent == GameManager.Instance.humanPlayer);

            if (targetM.bubbleShield && !ignoreImmunity)
            {
                targetM.bubbleShield = false;
                MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                if (showVfx) SpawnSlotStatusVfx(targetIsHuman, targetSlot, "🫧 BLOCKED!", new Color(0.45f, 0.90f, 1f));
                return;
            }

            int appliedDmg = ApplyIncomingDefenseMods(targetM, opponent, sourceM, dmg, isCombat: false, isSpell: isSpell, isSingleSpell: isSingleSpell, ignoreImmunity: ignoreImmunity);
            if (appliedDmg <= 0)
            {
                MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                if (showVfx) SpawnSlotStatusVfx(targetIsHuman, targetSlot, "🛡️ IMMUNE!", new Color(0.75f, 0.85f, 1f));
                return;
            }

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
                HandleMonsterDeath(opponent, player, targetSlot, targetM, sourceM);
            }
        }

        public static AbilityTargetMode GetV2OpTargetMode(V2CardMechanicsEngine.V2OpItem op)
        {
            if (op == null || string.IsNullOrEmpty(op.kind)) return AbilityTargetMode.None;
            switch (op.kind)
            {
                case "buff_ally":
                case "double_skill_1t":
                case "grant_double_strike":
                case "recall_ally":
                case "sac_ally_draw2":
                case "sac_ally_aether":
                case "reposition_ally":
                case "ally_atk_to_def":
                    return AbilityTargetMode.SingleAlly;
                case "dmg_single":
                case "cleave_enemy":
                case "recall_enemy":
                case "mind_control_temp":
                case "devour_3":
                case "pull_hook":
                case "shifting_curse":
                case "flip_enemy_stats":
                case "mirror_enemy_exact":
                case "betray_commander":
                    return AbilityTargetMode.SingleEnemy;
                default:
                    return AbilityTargetMode.None;
            }
        }

        public static bool V2OpRequiresEnemyMonster(V2CardMechanicsEngine.V2OpItem op)
        {
            if (op == null || string.IsNullOrEmpty(op.kind)) return false;
            switch (op.kind)
            {
                case "cleave_enemy":
                case "aoe_enemy":
                case "recall_enemy":
                case "mind_control_temp":
                case "devour_3":
                case "pull_hook":
                case "shifting_curse":
                case "detonate_dots":
                case "flip_enemy_stats":
                case "mirror_enemy_exact":
                case "betray_commander":
                    return true;
                default:
                    return false;
            }
        }

        public int GetActiveSkillCost(PlayerState player, PlayerState opponent, int slotIndex, string act, int skillIndex = -1)
        {
            if (string.IsNullOrEmpty(act)) return 0;
            var mySlot = (player != null && slotIndex >= 0 && slotIndex < 6) ? player.slots[slotIndex] : null;
            var myM = (mySlot != null && mySlot.IsOccupied) ? mySlot.currentMonster : null;

            // V2 Exact Structured Cost (from v2_ops_json or "[X Mana]" prefix)
            int baseSkillCost = -1;
            if (myM != null)
            {
                int resolvedIdx = skillIndex;
                if (resolvedIdx < 0 && myM.actives != null) resolvedIdx = myM.actives.IndexOf(act);
                var v2op = V2CardMechanicsEngine.GetV2ActiveOp(myM, resolvedIdx);
                if (v2op != null) baseSkillCost = Mathf.Max(0, v2op.cost);
            }
            if (baseSkillCost < 0)
            {
                if (act.StartsWith("[0 Mana]")) baseSkillCost = 0;
                else if (act.StartsWith("[1 Mana]")) baseSkillCost = 1;
                else if (act.StartsWith("[2 Mana]")) baseSkillCost = 2;
                else if (act.StartsWith("[3 Mana]")) baseSkillCost = 3;
                else if (act.StartsWith("[4 Mana]")) baseSkillCost = 4;
                else
                {
                    bool hasPowerConditionOnly = GetParsedPermanentPowerCost(act) > 0 && !act.ToLowerInvariant().Contains("mana");
                    baseSkillCost = hasPowerConditionOnly ? 0 :
                        ((act.Contains("(Cost 3 Mana)") || act.Contains("(Cost 3 Power)")) ? 3 :
                        ((act.Contains("(Cost 2 Mana)") || act.Contains("(Cost 2 Power)")) ? 2 : 1));
                }
            }
            if (player != null && player.freeSkillManaZero) return 0;
            if (baseSkillCost <= 0) return 0;

            var oppSlot = (opponent != null && slotIndex >= 0 && slotIndex < 6) ? opponent.slots[slotIndex] : null;
            var oppM = (oppSlot != null && oppSlot.IsOccupied) ? oppSlot.currentMonster : null;
            var activeEnv = GameManager.Instance != null ? GameManager.Instance.activeEnvironment : null;

            int extraCost = (oppM != null && oppM.HasPassiveSubstr("Frost Aura") != null) ? 1 : 0;
            int covDisc = 0;
            if (V2CardMechanicsEngine.IsCovenantActiveForPlayer(player))
            {
                var cov = V2CardMechanicsEngine.GetCovenantOps(player.activeCovenant);
                if (cov != null) covDisc = cov.rew_skill_disc;
            }
            int envDisc = 0;
            if (activeEnv != null && IsEnvElement(activeEnv, "Electric") && myM != null && myM.data != null)
            {
                int stepE = activeEnv.level >= 3 ? 2 : 1;
                if (EnvironmentCardData.AreSameElement(myM.data.element, "Electric"))
                    envDisc = stepE;
                else
                    extraCost += stepE; // Static Jam: +1/+1/+2 Mana tax on Foreign units' actives!
            }
            int discount = ((player != null && player.freeSkillBonus > 0) ? 1 : 0)
                + CommanderSpiritEffects.GetSkillManaDiscount(player, myM)
                + covDisc
                + envDisc
                + ((myM != null && myM.HasPassiveSubstr("Runic Conduit") != null) ? 1 : 0);
            return Mathf.Max(0, baseSkillCost + extraCost - discount);
        }

        private static int GetParsedPermanentPowerCost(string act)
        {
            if (string.IsNullOrEmpty(act)) return 0;
            if (act.Contains("(Requires 3 Power)") || act.Contains("(Cost 3 Permanent Power)") || act.Contains("(Spend 3 Power)") || act.Contains("(Drain 3 Power)")) return 3;
            if (act.Contains("(Requires 2 Power)") || act.Contains("(Cost 2 Permanent Power)") || act.Contains("(Spend 2 Power)") || act.Contains("(Drain 2 Power)")) return 2;
            if (act.Contains("(Requires 1 Power)") || act.Contains("(Cost 1 Permanent Power)") || act.Contains("(Spend 1 Power)") || act.Contains("(Drain 1 Power)")) return 1;
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
                     act.Contains("Hive Swarm Call") || act.Contains("Summoning") || act.Contains("Reboot") ||
                     act.Contains("Wall") || act.Contains("Bubble") || act.Contains("Purging Flame") ||
                     act.Contains("Energy Surge") || act.Contains("Hastening Breeze") || act.Contains("Tempest Tailwind") ||
                     act.Contains("Sanctified Aegis") || act.Contains("Pack Alpha Roar") || act.Contains("Arcane Infusion") ||
                     act.Contains("Arcane Time Warp") || act.Contains("Total Eclipse") || act.Contains("Destructive Ion Cannon") ||
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

            // If any of the monster's active skills is Ready, report Ready with that skill's cost
            for (int i = 0; i < m.ActiveSkillCount; i++)
            {
                var st = GetAbilityState(owner, opponent, slotIndex, i, out int c, out string r);
                if (st == AbilityReadyState.Ready)
                {
                    totalCost = c;
                    reason = r;
                    return AbilityReadyState.Ready;
                }
            }

            return GetAbilityState(owner, opponent, slotIndex, 0, out totalCost, out reason);
        }

        public AbilityReadyState GetAbilityState(PlayerState owner, PlayerState opponent, int slotIndex, int skillIndex, out int totalCost, out string reason)
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
            if (m == null || !m.IsAlive || !m.HasActiveSkillAt(skillIndex))
            {
                reason = "This monster has no active ability in that slot.";
                return AbilityReadyState.NoAbility;
            }

            string act = m.actives[skillIndex];
            string shortName = GetShortSkillName(act);
            var v2op = V2CardMechanicsEngine.GetV2ActiveOp(m, skillIndex);
            int manaCost = GetActiveSkillCost(owner, opponent, slotIndex, act, skillIndex);
            int pwrCost = GetActiveSkillPowerCost(m, act);
            totalCost = pwrCost > 0 ? pwrCost : manaCost;

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

            if (m.IsActiveSkillUsed(skillIndex, owner))
            {
                reason = $"✓ [{m.data.name}] already used [{shortName}] this turn.";
                return AbilityReadyState.AlreadyUsed;
            }

            var gm = GameManager.Instance;
            if (gm != null)
            {
                bool isOwnersTurn = (owner == gm.humanPlayer && gm.isPlayerTurn) || (owner == gm.aiPlayer && !gm.isPlayerTurn);
                if (!isOwnersTurn || gm.currentPhase != GamePhase.ActionPhase)
                {
                    reason = $"⏳ [{shortName}] (Cost: {totalCost}) — Ready on owner's Action Phase.";
                    return AbilityReadyState.NotYourTurn;
                }
            }

            bool reqEnemy = v2op != null ? V2OpRequiresEnemyMonster(v2op) : SkillRequiresEnemyMonster(act);
            if (reqEnemy && GetAliveSlots(opponent).Count == 0)
            {
                reason = $"🎯 [{shortName}] requires at least 1 enemy monster on the battlefield!";
                return AbilityReadyState.NoValidTarget;
            }

            var targetModeCheck = v2op != null ? GetV2OpTargetMode(v2op) : GetSkillTargetMode(act, owner, opponent);
            if (targetModeCheck == AbilityTargetMode.SingleEnemy && GetAliveSlots(opponent).Count > 0 && GetTargetableEnemySlots(opponent, act).Count == 0)
            {
                reason = $"🌫️ [{shortName}] cannot target enemies protected by Night Stealth!";
                return AbilityReadyState.NoValidTarget;
            }

            // 1. Sacrifice Preconditions: requires at least 1 other alive ally on the battlefield
            bool isSacrificeSkill = (v2op != null && (v2op.sac || v2op.kind.StartsWith("sac_") || v2op.kind == "absorb_ally_atk" || v2op.kind == "ally_atk_to_def" || v2op.kind == "devour_3"))
                                   || act.ToLower().Contains("sacrifice") || act.ToLower().Contains("absorb ally");
            if (isSacrificeSkill && GetAliveSlots(owner).Count <= 1)
            {
                reason = $"⚠️ [{shortName}] requires at least 1 other allied monster on the battlefield to sacrifice!";
                return AbilityReadyState.NoValidTarget;
            }

            // 2. Summon / Spawn / Revive Preconditions: requires at least 1 empty slot on board
            bool isSpawnSkill = (v2op != null && (v2op.kind == "spawn_token" || v2op.kind == "warrior_call" || v2op.kind == "revive_board" || v2op.spawn != null))
                                || act.Contains("Wrath of Nature") || act.Contains("Graveyard Summoning") || act.Contains("Hive Swarm Call") || act.ToLower().Contains("spawn") || act.ToLower().Contains("summon");
            bool hasEmptySlot = false;
            for (int s = 0; s < 6; s++)
            {
                if (owner.slots != null && s < owner.slots.Length && (owner.slots[s] == null || !owner.slots[s].IsOccupied))
                {
                    hasEmptySlot = true;
                    break;
                }
            }
            if (isSpawnSkill && !hasEmptySlot)
            {
                reason = $"⚠️ Board is full! Need at least 1 empty battlefield slot for [{shortName}].";
                return AbilityReadyState.NoValidTarget;
            }

            // 3. Graveyard Preconditions: requires at least 1 card in graveyard
            bool isGraveSkill = (v2op != null && (v2op.kind == "revive_board" || v2op.kind == "grave_to_hand"))
                                || act.ToLower().Contains("graveyard to hand") || act.ToLower().Contains("revive");
            if (isGraveSkill && owner.graveyard.Count == 0)
            {
                reason = $"⚠️ Graveyard is empty! No fallen cards to retrieve or revive with [{shortName}].";
                return AbilityReadyState.NoValidTarget;
            }

            // 4. Hand Discard Preconditions: requires at least 1 card in hand
            bool isDiscardSkill = (v2op != null && v2op.kind.StartsWith("discard_hand")) || act.ToLower().Contains("discard a card from hand");
            if (isDiscardSkill && owner.hand.Count == 0)
            {
                reason = $"⚠️ Hand is empty! No card to discard for [{shortName}].";
                return AbilityReadyState.NoValidTarget;
            }

            // 5. Leader Healing Preconditions: cannot heal if already at max HP
            bool isHealLeaderSkill = (v2op != null && v2op.kind == "heal_leader") || act.ToLower().Contains("heal commander");
            if (isHealLeaderSkill && owner.currentHp >= owner.maxHp)
            {
                reason = $"❤️ Commander is already at maximum health ({owner.maxHp} HP)!";
                return AbilityReadyState.NoValidTarget;
            }

            if (owner.currentMana < manaCost)
            {
                reason = $"❌ Not enough Mana for [{shortName}]! Need {manaCost}💧 Mana, have {owner.currentMana}💧.";
                return AbilityReadyState.NotEnoughMana;
            }

            if (pwrCost > 0 && owner.power < pwrCost)
            {
                reason = $"❌ Not enough Power for [{shortName}]! Need {pwrCost}⚡ Power, have {owner.power}⚡.";
                return AbilityReadyState.NotEnoughPower;
            }

            reason = pwrCost > 0
                ? $"⚡ [{shortName}] Ready (Requires {pwrCost}⚡ Power" + (manaCost > 0 ? $" + {manaCost}💧 Mana" : "") + ")!"
                : $"💧 [{shortName}] Ready ({manaCost}💧 Mana)!";
            return AbilityReadyState.Ready;
        }

        public bool ActivateMonsterAbility(PlayerState player, PlayerState opponent, int slotIndex, out string message)
        {
            var slot = (player != null && slotIndex >= 0 && slotIndex < player.slots.Length) ? player.slots[slotIndex] : null;
            var m = (slot != null && slot.IsOccupied) ? slot.currentMonster : null;
            int chosenSkillIndex = 0;
            if (m != null)
            {
                for (int i = 0; i < m.ActiveSkillCount; i++)
                {
                    if (GetAbilityState(player, opponent, slotIndex, i, out _, out _) == AbilityReadyState.Ready)
                    {
                        chosenSkillIndex = i;
                        break;
                    }
                }
            }
            return ActivateMonsterAbility(player, opponent, slotIndex, chosenSkillIndex, -1, out message);
        }

        public bool ActivateMonsterAbility(PlayerState player, PlayerState opponent, int slotIndex, int skillIndex, int targetSlotIndex, out string message)
        {
            var state = GetAbilityState(player, opponent, slotIndex, skillIndex, out int totalCost, out string reason);
            if (state != AbilityReadyState.Ready)
            {
                message = reason;
                return false;
            }

            var m = player.slots[slotIndex].currentMonster;
            string act = m.actives[skillIndex];
            string shortName = GetShortSkillName(act);
            var v2op = V2CardMechanicsEngine.GetV2ActiveOp(m, skillIndex);

            var targetMode = v2op != null ? GetV2OpTargetMode(v2op) : GetSkillTargetMode(act, player, opponent);
            if (targetSlotIndex >= 0)
            {
                if (targetMode == AbilityTargetMode.SingleEnemy)
                {
                    if (targetSlotIndex >= opponent.slots.Length || opponent.slots[targetSlotIndex] == null || !opponent.slots[targetSlotIndex].IsOccupied)
                    {
                        message = $"🎯 Select an occupied ENEMY monster slot to target with [{shortName}]!";
                        return false;
                    }
                    var tMon = opponent.slots[targetSlotIndex].currentMonster;
                    if (!SkillTargetsCanHitStealth(act) && tMon.HasPassiveSubstr("Night Stealth") != null)
                    {
                        message = $"🌫️ [{tMon.data.name}] has Night Stealth and cannot be targeted directly by [{shortName}]!";
                        return false;
                    }
                }
                else if (targetMode == AbilityTargetMode.SingleAlly)
                {
                    if (targetSlotIndex >= player.slots.Length || player.slots[targetSlotIndex] == null || !player.slots[targetSlotIndex].IsOccupied)
                    {
                        message = $"✨ Select an occupied ALLIED monster slot to buff with [{shortName}]!";
                        return false;
                    }
                }
            }

            int actCost = GetActiveSkillCost(player, opponent, slotIndex, act, skillIndex);
            int actPowerCost = GetActiveSkillPowerCost(m, act);
            if (player.currentMana < actCost || player.power < actPowerCost)
            {
                message = reason;
                return false;
            }

            player.currentMana -= actCost;
            if (actPowerCost > 0) player.power -= actPowerCost;
            m.MarkActiveSkillUsed(skillIndex, player);

            int bonusEff = player.freeSkillBonus;
            player.freeSkillBonus = 0;
            player.freeSkillManaZero = false;

            if (m.HasPassiveSubstr("Mana Recovery") != null)
            {
                player.currentMana += 1;
            }

            int capCores = 0;
            foreach (var s in player.slots)
            {
                if (s != null && s.IsOccupied && s.currentMonster.IsAlive && s.currentMonster.HasPassiveSubstr("Capacitor Core") != null)
                    capCores++;
            }
            if (capCores > 0)
            {
                player.currentMana += Mathf.Min(2, capCores);
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
                ResolveSingleActive(act, slotIndex, m, player, opponent, bonusEff, showVfx: true, explicitTargetSlot: targetSlotIndex, skillIndex: skillIndex);
            }

            string colorBadge = skillIndex == 0 ? "🔵" : "🔴";
            bool isEnemy = (GameManager.Instance != null && player == GameManager.Instance.aiPlayer);
            string sidePrefix = isEnemy ? "💀 Enemy " : "";
            message = $"{colorBadge} {sidePrefix}Monster #{m.data.name} activated [{shortName}]!";
            return true;
        }

        public int ChooseSmartSkillTargetSlot(string act, int casterSlot, MonsterInstance m, PlayerState player, PlayerState opp, int bonus)
        {
            var mode = GetSkillTargetMode(act, player, opp);
            if (mode == AbilityTargetMode.SingleAlly)
            {
                var allies = GetAliveSlots(player);
                if (allies.Count == 0) return casterSlot;
                if (act.Contains("Purging Flame") || act.Contains("Arcane Infusion"))
                {
                    BoardSlot bestDeb = null;
                    int bestVal = int.MinValue;
                    foreach (var a in allies)
                    {
                        var am = a.currentMonster;
                        if (am.burnDmg > 0 || am.poisonDmg > 0 || am.frozenTurns > 0 || am.weakenedTurns > 0 || am.silencedTurns > 0)
                        {
                            int val = am.baseAtk + am.bonusAtk + am.currentDef;
                            if (val > bestVal) { bestVal = val; bestDeb = a; }
                        }
                    }
                    if (bestDeb != null) return bestDeb.slotIndex;
                    var off = GetBestAllyForOffensiveBuff(m, allies);
                    return off?.currentSlot != null ? off.currentSlot.slotIndex : casterSlot;
                }
                if (act.Contains("Safety Bubble") || act.Contains("Sanctified Aegis"))
                {
                    BoardSlot best = null;
                    int bestSc = int.MinValue;
                    bool anyUnbubbled = allies.Exists(a => !a.currentMonster.bubbleShield);
                    foreach (var a in allies)
                    {
                        if (anyUnbubbled && a.currentMonster.bubbleShield) continue;
                        int sc = a.currentMonster.baseAtk + a.currentMonster.bonusAtk + a.currentMonster.data.cost * 3;
                        if (sc > bestSc) { bestSc = sc; best = a; }
                    }
                    return best != null ? best.slotIndex : allies[0].slotIndex;
                }
                if (act.Contains("Hastening Breeze") || act.Contains("Tempest Tailwind"))
                {
                    BoardSlot bestSleep = null;
                    int bestAtk = int.MinValue;
                    foreach (var a in allies)
                    {
                        if (!a.currentMonster.canAttack && a.currentMonster.frozenTurns <= 0)
                        {
                            int atk = a.currentMonster.baseAtk + a.currentMonster.bonusAtk;
                            if (atk > bestAtk) { bestAtk = atk; bestSleep = a; }
                        }
                    }
                    if (bestSleep != null) return bestSleep.slotIndex;
                    var off = GetBestAllyForOffensiveBuff(m, allies);
                    return off?.currentSlot != null ? off.currentSlot.slotIndex : casterSlot;
                }
                if (act.Contains("Energy Surge") || act.Contains("Blessing of Light") || act.Contains("Pack Alpha Roar"))
                {
                    var off = GetBestAllyForOffensiveBuff(m, allies);
                    return off?.currentSlot != null ? off.currentSlot.slotIndex : casterSlot;
                }
                var def = GetBestAllyForDefensiveBuff(m, allies);
                return def?.currentSlot != null ? def.currentSlot.slotIndex : casterSlot;
            }

            if (mode == AbilityTargetMode.SingleEnemy)
            {
                var targetable = GetTargetableEnemySlots(opp, act);
                if (targetable.Count == 0) return -1;
                var activeEnv = GameManager.Instance != null ? GameManager.Instance.activeEnvironment : null;
                int bestSlot = targetable[0].slotIndex;
                float bestScore = float.MinValue;

                foreach (var es in targetable)
                {
                    var em = es.currentMonster;
                    int esAtk = em.baseAtk + em.bonusAtk;
                    float threat = esAtk * 1.6f + em.data.cost * 3.0f + (em.HasActiveSkill ? 8.0f : 0f) + (em.passives != null ? em.passives.Count * 2.5f : 0f);
                    float sc = threat;
                    if (es.slotIndex == casterSlot) sc += 4.0f;

                    if (act.Contains("Volcanic Eruption"))
                    {
                        if (em.currentDef < 30 + bonus) sc += 45.0f + em.currentDef * 1.2f;
                        else if (em.currentDef <= 18 + bonus) sc += 35.0f;
                    }
                    else if (act.Contains("Frost Shatter"))
                    {
                        if (em.frozenTurns > 0) sc += 60.0f + em.currentDef * 1.5f;
                        else if (em.currentDef <= 18 + bonus) sc += 30.0f;
                    }
                    else if (act.Contains("Great Earth Fissure"))
                    {
                        sc += 50.0f + em.currentDef * 1.5f + ((es.slotIndex == 2 || es.slotIndex == 3) ? 8.0f : 0f);
                    }
                    else if (act.Contains("Destructive Ion Cannon"))
                    {
                        int cdmg = 50 + bonus;
                        int overflow = Mathf.Max(0, cdmg - em.currentDef);
                        sc += overflow * 1.4f + (overflow >= opp.currentHp ? 120.0f : 0f);
                    }
                    else if (act.Contains("Ember Blast") || act.Contains("Earthquake Strike") || act.Contains("Tectonic Slam"))
                    {
                        int mainD = (act.Contains("Ember Blast") ? 8 : (act.Contains("Tectonic Slam") ? 11 : 15)) + bonus;
                        int adjD = act.Contains("Ember Blast") ? 8 : 5;
                        if (em.currentDef <= mainD && !em.bubbleShield) sc += 28.0f;
                        foreach (int adj in new[] { es.slotIndex - 1, es.slotIndex + 1 })
                        {
                            if (adj >= 0 && adj < 6 && opp.slots[adj] != null && opp.slots[adj].IsOccupied && opp.slots[adj].currentMonster.IsAlive)
                                sc += 12.0f + (opp.slots[adj].currentMonster.currentDef <= adjD ? 18.0f : 0f);
                        }
                    }
                    else if (act.Contains("Sweeping Cyclone"))
                    {
                        if (!em.HasEarthSteadfastness(activeEnv)) sc += 25.0f + em.data.cost * 5.0f + em.bonusAtk * 2.0f;
                        else if (em.currentDef <= 14 + bonus) sc += 22.0f;
                    }
                    else if (act.Contains("Abyssal Geyser"))
                    {
                        sc += (em.bubbleShield ? 16.0f : 0f) + Mathf.Max(0, em.bonusAtk) * 2.0f + (em.currentDef <= 13 + bonus ? 26.0f : 0f);
                    }
                    else if (act.Contains("Soul Lock") || act.Contains("Static Jolt"))
                    {
                        if (em.silencedTurns == 0) sc += em.ActiveSkillCount * 18.0f + (em.passives != null ? em.passives.Count * 10.0f : 0f);
                        else sc -= 25.0f;
                    }
                    else if (act.Contains("Ice Spear") || act.Contains("Cryo Lance") || act.Contains("Strangling Roots") || act.Contains("Spectral Terror") || act.Contains("Water Vortex"))
                    {
                        if (em.frozenTurns == 0 && em.canAttack) sc += 20.0f + esAtk * 0.8f;
                    }
                    else if (act.Contains("Elemental Shift") || act.Contains("Blighted Fang"))
                    {
                        if (em.weakenedTurns == 0) sc += 18.0f + esAtk * 0.8f;
                    }
                    else
                    {
                        int dmg = 15 + bonus;
                        if (em.currentDef <= dmg && !em.bubbleShield) sc += 32.0f;
                    }

                    if (sc > bestScore)
                    {
                        bestScore = sc;
                        bestSlot = es.slotIndex;
                    }
                }
                return bestSlot;
            }

            return -1;
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
        /// Steps through the AI/player's monster active skills one by one, refreshing UI and playing clear visual cast banners so the player sees every enemy skill activation.
        /// </summary>
        public System.Collections.IEnumerator ExecuteActiveSkillsAnimated(PlayerState player, PlayerState opponent)
        {
            if (player == null || (player.currentMana <= 0 && player.power <= 0)) yield break;

            for (int slot = 0; slot < 6; slot++)
            {
                if (!player.IsAlive || !opponent.IsAlive || (GameManager.Instance != null && GameManager.Instance.currentPhase == GamePhase.GameOver))
                    yield break;

                var pSlot = player.slots[slot];
                if (pSlot == null || !pSlot.IsOccupied) continue;
                var m = pSlot.currentMonster;
                if (m == null || !m.IsAlive || m.silencedTurns > 0 || m.frozenTurns > 0 || !m.HasActiveSkill)
                    continue;

                int skillCount = m.ActiveSkillCount;
                for (int sIdx = 0; sIdx < skillCount; sIdx++)
                {
                    if (!player.IsAlive || !opponent.IsAlive || (GameManager.Instance != null && GameManager.Instance.currentPhase == GamePhase.GameOver))
                        yield break;

                    if (pSlot.currentMonster == null || !m.IsAlive) break;
                    if (m.IsActiveSkillUsed(sIdx, player)) continue;

                    string actText = m.actives[sIdx];
                    int targetSlot = ChooseSmartSkillTargetSlot(actText, slot, m, player, opponent, player.freeSkillBonus);
                    if (ActivateMonsterAbility(player, opponent, slot, sIdx, targetSlot, out string msg))
                    {
                        if (MonsterCardGame.Audio.SoundManager.Instance != null)
                        {
                            MonsterCardGame.Audio.SoundManager.Instance.PlayMonsterSkill(m.data);
                        }
                        if (BattleUIManager.Instance != null)
                        {
                            BattleUIManager.Instance.NotifyAIMonsterSkillActivated(player, slot, m, sIdx, actText, msg);
                            BattleUIManager.Instance.UpdateUI();
                        }
                        if (!player.IsAlive || !opponent.IsAlive)
                        {
                            GameManager.Instance?.CheckGameOver();
                            yield break;
                        }
                        yield return new WaitForSeconds(0.95f);
                    }

                    if (player.currentMana <= 0 && player.power <= 0) break;
                }

                if (player.currentMana <= 0 && player.power <= 0) break;
            }
        }

        private bool TryExecuteSlotActiveSkill(PlayerState player, PlayerState opponent, int slot)
        {
            var pSlot = player.slots[slot];
            if (pSlot == null || !pSlot.IsOccupied) return false;
            var m = pSlot.currentMonster;
            if (!m.IsAlive || m.silencedTurns > 0 || m.frozenTurns > 0 || !m.HasActiveSkill)
                return false;

            bool anyActivated = false;
            for (int sIdx = 0; sIdx < m.ActiveSkillCount; sIdx++)
            {
                if (m.IsActiveSkillUsed(sIdx, player)) continue;
                string actText = m.actives[sIdx];
                int targetSlot = ChooseSmartSkillTargetSlot(actText, slot, m, player, opponent, player.freeSkillBonus);
                if (ActivateMonsterAbility(player, opponent, slot, sIdx, targetSlot, out string msg))
                {
                    anyActivated = true;
                    if (MonsterCardGame.Audio.SoundManager.Instance != null)
                    {
                        MonsterCardGame.Audio.SoundManager.Instance.PlayMonsterSkill(m.data);
                    }
                    if (BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.NotifyAIMonsterSkillActivated(player, slot, m, sIdx, actText, msg);
                    }
                }
            }
            return anyActivated;
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

        private BoardSlot GetEnemyTarget(int preferredSlot, List<BoardSlot> enemies, PlayerState opponent, bool preferMaxAtk = false, int explicitTargetSlot = -1)
        {
            if (enemies == null || enemies.Count == 0) return null;
            if (explicitTargetSlot >= 0 && explicitTargetSlot < 6 && opponent.slots[explicitTargetSlot] != null && opponent.slots[explicitTargetSlot].IsOccupied)
            {
                return opponent.slots[explicitTargetSlot];
            }
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

        private MonsterInstance GetAllyTarget(MonsterInstance self, List<BoardSlot> allies, PlayerState player, bool offensive, int explicitTargetSlot = -1)
        {
            if (explicitTargetSlot >= 0 && player != null && explicitTargetSlot < player.slots.Length &&
                player.slots[explicitTargetSlot] != null && player.slots[explicitTargetSlot].IsOccupied)
            {
                return player.slots[explicitTargetSlot].currentMonster;
            }
            return offensive ? GetBestAllyForOffensiveBuff(self, allies) : GetBestAllyForDefensiveBuff(self, allies);
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

        private void ApplyV2SkillStatusFx(MonsterInstance target, string fx, int val, int dur, bool targetIsHuman, int slotIdx, bool showVfx)
        {
            if (target == null || !target.IsAlive || string.IsNullOrEmpty(fx)) return;
            switch (fx)
            {
                case "burn":
                    target.burnDmg += Mathf.Max(2, val);
                    if (showVfx) SpawnSlotStatusVfx(targetIsHuman, slotIdx, $"🔥 BURN +{Mathf.Max(2, val)}", new Color(1f, 0.45f, 0.15f));
                    break;
                case "poison":
                    target.poisonDmg += Mathf.Max(2, val);
                    if (showVfx) SpawnSlotStatusVfx(targetIsHuman, slotIdx, $"☠️ POISON +{Mathf.Max(2, val)}", new Color(0.45f, 0.92f, 0.30f));
                    break;
                case "bleed":
                    target.bleedDmg += Mathf.Max(2, val);
                    if (showVfx) SpawnSlotStatusVfx(targetIsHuman, slotIdx, $"🩸 BLEED +{Mathf.Max(2, val)}", new Color(0.95f, 0.22f, 0.28f));
                    break;
                case "freeze":
                    target.frozenTurns = Mathf.Max(target.frozenTurns, Mathf.Max(1, dur));
                    if (showVfx) SpawnSlotStatusVfx(targetIsHuman, slotIdx, "❄️ FROZEN!", new Color(0.55f, 0.88f, 1f));
                    break;
                case "silence":
                    target.silencedTurns = Mathf.Max(target.silencedTurns, Mathf.Max(1, dur));
                    if (showVfx) SpawnSlotStatusVfx(targetIsHuman, slotIdx, "🔇 SILENCED!", new Color(0.75f, 0.65f, 0.95f));
                    break;
                case "root":
                    target.rootedTurns = Mathf.Max(target.rootedTurns, Mathf.Max(1, dur));
                    if (showVfx) SpawnSlotStatusVfx(targetIsHuman, slotIdx, "🌿 ROOTED!", new Color(0.35f, 0.85f, 0.45f));
                    break;
                case "weaken":
                    target.weakenedAtkFlat += Mathf.Max(2, val);
                    if (showVfx) SpawnSlotStatusVfx(targetIsHuman, slotIdx, $"⬇️ -{Mathf.Max(2, val)} ATK", new Color(0.88f, 0.65f, 0.35f));
                    break;
            }
        }

        /// <summary>
        /// Executes ALL 25 Arena_Lab_V2 structured monster active skills 1-to-1 with `battle_engine.py::_use_active_skill()`.
        /// Returns true if the monster has a V2 structured active skill so it NEVER falls back to legacy +5/+15!
        /// </summary>
        private bool TryResolveV2ActiveSkill(
            V2CardMechanicsEngine.V2OpItem op,
            int slot,
            MonsterInstance m,
            PlayerState player,
            PlayerState opponent,
            int bonusEff,
            bool showVfx,
            int explicitTargetSlot)
        {
            if (op == null || string.IsNullOrEmpty(op.kind)) return false;

            var enemies = GetAliveSlots(opponent);
            var allies = GetAliveSlots(player);
            bool playerIsHuman = GameManager.Instance != null && (player == GameManager.Instance.humanPlayer);
            bool oppIsHuman = !playerIsHuman;
            int turn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 1;

            int covSkillBonus = 0;
            if (V2CardMechanicsEngine.IsCovenantActiveForPlayer(player))
            {
                var cov = V2CardMechanicsEngine.GetCovenantOps(player.activeCovenant);
                if (cov != null) covSkillBonus = cov.rew_skill_power;
            }
            var activeEnv = GameManager.Instance != null ? GameManager.Instance.activeEnvironment : null;
            int envLvl = activeEnv != null ? Mathf.Clamp(activeEnv.level - 1, 0, 2) : 0;
            int envSkillBonus = 0;
            if (activeEnv != null && m != null && m.data != null)
            {
                if (IsEnvElement(activeEnv, "Desert") && EnvironmentCardData.AreSameElement(m.data.element, "Desert"))
                    envSkillBonus = new[] { 2, 3, 5 }[envLvl];
                else if (IsEnvElement(activeEnv, "Light") && !EnvironmentCardData.AreSameElement(m.data.element, "Light"))
                    envSkillBonus = -new[] { 1, 2, 3 }[envLvl]; // Blinding Glare on Foreign units
            }
            int leaderSkillBonus = V2CardMechanicsEngine.GetLeaderSkillPowerBonus(player, m);
            int bonus = bonusEff + covSkillBonus + envSkillBonus + leaderSkillBonus;
            int v1 = op.v1;
            int v2 = op.v2;

            switch (op.kind)
            {
                case "dmg_single":
                {
                    int dmg = Mathf.Max(1, v1 + bonus);
                    var ts = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                    if (ts != null && ts.currentMonster != null && ts.currentMonster.IsAlive)
                    {
                        var tm = ts.currentMonster;
                        int preDef = tm.currentDef;
                        DealSpellOrSkillDamage(ts.slotIndex, tm, dmg, m, opponent, player, true, showVfx);
                        int dealt = Mathf.Max(0, preDef - (tm.IsAlive ? tm.currentDef : 0));
                        if (op.exec_below > 0 && tm.IsAlive && tm.currentDef <= op.exec_below)
                        {
                            if (showVfx) SpawnSlotStatusVfx(oppIsHuman, ts.slotIndex, "💀 EXECUTED!", new Color(1f, 0.25f, 0.25f));
                            tm.currentDef = 0;
                            HandleMonsterDeath(opponent, player, ts.slotIndex, tm, m);
                        }
                        if (op.leech > 0 && dealt > 0)
                        {
                            int lh = V2CardMechanicsEngine.StrictRoundPct(dealt, op.leech);
                            if (lh > 0)
                            {
                                player.HealLeader(lh, opponent);
                                if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, $"+{lh} HP", Color.green, true, lh);
                            }
                        }
                        if (!string.IsNullOrEmpty(op.fx) && tm.IsAlive)
                            ApplyV2SkillStatusFx(tm, op.fx, v2, op.dur, oppIsHuman, ts.slotIndex, showVfx);
                    }
                    else
                    {
                        opponent.TakeLeaderDamage(dmg);
                        if (showVfx) SpawnLeaderStatusVfx(oppIsHuman, $"-{dmg} HP", new Color(1f, 0.35f, 0.25f));
                    }
                    if (op.heal_hp > 0)
                    {
                        player.HealLeader(op.heal_hp, opponent);
                        if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, $"+{op.heal_hp} HP", Color.green, true, op.heal_hp);
                    }
                    if (op.self_def > 0)
                    {
                        m.ApplyPermBuff(0, op.self_def);
                        if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"+{op.self_def} DEF", Color.cyan);
                    }
                    return true;
                }

                case "aoe_enemy":
                {
                    int dmg = Mathf.Max(1, v1 + bonus);
                    foreach (var es in new List<BoardSlot>(enemies))
                    {
                        if (es == null || !es.IsOccupied || es.currentMonster == null || !es.currentMonster.IsAlive) continue;
                        var em = es.currentMonster;
                        int preDef = em.currentDef;
                        DealSpellOrSkillDamage(es.slotIndex, em, dmg, m, opponent, player, true, showVfx);
                        int dealt = Mathf.Max(0, preDef - (em.IsAlive ? em.currentDef : 0));
                        if (op.leech > 0 && dealt > 0)
                        {
                            int lh = V2CardMechanicsEngine.StrictRoundPct(dealt, op.leech);
                            if (lh > 0) player.HealLeader(lh, opponent);
                        }
                        if (!string.IsNullOrEmpty(op.fx) && em.IsAlive)
                            ApplyV2SkillStatusFx(em, op.fx, v2, op.dur, oppIsHuman, es.slotIndex, showVfx);
                        if (op.weaken > 0 && em.IsAlive)
                            em.weakenedAtkFlat += op.weaken;
                    }
                    if (op.cleanse)
                    {
                        m.burnDmg = 0; m.poisonDmg = 0; m.bleedDmg = 0; m.frozenTurns = 0; m.silencedTurns = 0; m.weakenedTurns = 0;
                    }
                    return true;
                }

                case "cleave_enemy":
                {
                    var ts = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                    if (ts != null)
                    {
                        int dmg = Mathf.Max(1, v1 + bonus);
                        for (int sl = ts.slotIndex - 1; sl <= ts.slotIndex + 1; sl++)
                        {
                            if (sl < 0 || sl >= 6 || opponent.slots[sl] == null || !opponent.slots[sl].IsOccupied) continue;
                            var em = opponent.slots[sl].currentMonster;
                            if (em == null || !em.IsAlive) continue;
                            DealSpellOrSkillDamage(sl, em, dmg, m, opponent, player, true, showVfx);
                            if (!string.IsNullOrEmpty(op.fx) && em.IsAlive)
                                ApplyV2SkillStatusFx(em, op.fx, v2, op.dur, oppIsHuman, sl, showVfx);
                        }
                    }
                    if (op.heal_hp > 0)
                    {
                        player.HealLeader(op.heal_hp, opponent);
                        if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, $"+{op.heal_hp} HP", Color.green, true, op.heal_hp);
                    }
                    if (op.self_def > 0)
                    {
                        m.ApplyPermBuff(0, op.self_def);
                        if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"+{op.self_def} DEF", Color.cyan);
                    }
                    return true;
                }

                case "buff_ally":
                {
                    MonsterInstance targetAlly = m;
                    int tSlot = slot;
                    if (explicitTargetSlot >= 0 && explicitTargetSlot < 6 && player.slots[explicitTargetSlot] != null && player.slots[explicitTargetSlot].IsOccupied)
                    {
                        targetAlly = player.slots[explicitTargetSlot].currentMonster;
                        tSlot = explicitTargetSlot;
                    }
                    int addAtk = op.atk;
                    int addDef = op.deff + (op.deff > 0 ? bonus : 0);
                    if (addAtk > 0 || addDef > 0) targetAlly.ApplyPermBuff(addAtk, addDef);
                    if (op.cleanse)
                    {
                        targetAlly.burnDmg = 0; targetAlly.poisonDmg = 0; targetAlly.bleedDmg = 0;
                        targetAlly.frozenTurns = 0; targetAlly.silencedTurns = 0; targetAlly.weakenedTurns = 0;
                    }
                    if (op.haste && !targetAlly.IsBarrier) targetAlly.canAttack = true;
                    if (op.self_dmg > 0) player.TakeLeaderDamage(op.self_dmg);
                    if (showVfx)
                    {
                        string lbl = (addAtk > 0 && addDef > 0) ? $"+{addAtk} ATK / +{addDef} DEF"
                            : (addAtk > 0 ? $"+{addAtk} ATK" : (addDef > 0 ? $"+{addDef} DEF" : "✨ BUFFED!"));
                        if (op.haste) lbl += " ⚡BLITZ";
                        if (op.cleanse) lbl += " ✨CLEANSE";
                        SpawnSlotStatusVfx(playerIsHuman, tSlot, lbl, new Color(0.45f, 1f, 0.65f));
                    }
                    return true;
                }

                case "aoe_ally":
                {
                    int addAtk = op.atk;
                    int addDef = op.deff + (op.deff > 0 ? bonus : 0);
                    foreach (var aslot in allies)
                    {
                        var au = aslot.currentMonster;
                        if (au == null || !au.IsAlive) continue;
                        if (addAtk > 0 || addDef > 0) au.ApplyPermBuff(addAtk, addDef);
                        if (op.cleanse)
                        {
                            au.burnDmg = 0; au.poisonDmg = 0; au.bleedDmg = 0;
                            au.frozenTurns = 0; au.silencedTurns = 0; au.weakenedTurns = 0;
                        }
                        if (op.haste && !au.IsBarrier) au.canAttack = true;
                        if (showVfx)
                        {
                            string lbl = (addAtk > 0 && addDef > 0) ? $"+{addAtk}/+{addDef}"
                                : (addAtk > 0 ? $"+{addAtk} ATK" : (addDef > 0 ? $"+{addDef} DEF" : "✨ CLEANSE"));
                            SpawnSlotStatusVfx(playerIsHuman, aslot.slotIndex, lbl, new Color(0.45f, 1f, 0.72f));
                        }
                    }
                    if (op.hp > 0)
                    {
                        int hAmt = op.hp + bonus;
                        player.HealLeader(hAmt, opponent);
                        if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, $"+{hAmt} HP", Color.green, true, hAmt);
                    }
                    return true;
                }

                case "heal_leader":
                {
                    int hAmt = Mathf.Max(1, v1 + bonus);
                    player.HealLeader(hAmt, opponent);
                    if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, $"+{hAmt} HP", Color.green, true, hAmt);
                    if (v2 > 0)
                    {
                        m.ApplyPermBuff(0, v2);
                        if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"+{v2} DEF", Color.cyan);
                    }
                    if (op.cleanse)
                    {
                        m.burnDmg = 0; m.poisonDmg = 0; m.bleedDmg = 0; m.frozenTurns = 0; m.silencedTurns = 0;
                    }
                    return true;
                }

                case "dmg_leader":
                {
                    int dmg = Mathf.Max(1, v1 + bonus);
                    opponent.TakeLeaderDamage(dmg);
                    if (showVfx) SpawnLeaderStatusVfx(oppIsHuman, $"-{dmg} HP 🔥", new Color(1f, 0.35f, 0.20f));
                    if (op.heal > 0)
                    {
                        player.HealLeader(op.heal, opponent);
                        if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, $"+{op.heal} HP", Color.green, true, op.heal);
                    }
                    if (op.double_burn)
                    {
                        foreach (var es in enemies)
                            if (es.currentMonster.burnDmg > 0)
                                es.currentMonster.burnDmg = Mathf.Min(30, es.currentMonster.burnDmg * 2);
                    }
                    return true;
                }

                case "spawn_token":
                {
                    int cnt = Mathf.Max(1, op.count > 0 ? op.count : 1);
                    int tAtk = Mathf.Max(2, v1);
                    int tDef = Mathf.Max(2, v2 > 0 ? v2 : v1);
                    int spawned = 0;
                    for (int s = 0; s < 6 && spawned < cnt; s++)
                    {
                        if (player.slots[s] != null && !player.slots[s].IsOccupied)
                        {
                            var tokData = CreateTokenData(9500 + s, m.data.element, m.data.tribe, "Attacker", 1, 1, tAtk, tDef, "", "Summoned Swarm Token");
                            tokData.name = "Swarm Token";
                            var tok = new MonsterInstance(tokData, turn) { canAttack = op.haste };
                            player.slots[s].PlaceMonster(tok);
                            if (showVfx) SpawnSlotStatusVfx(playerIsHuman, s, $"✨ TOKEN {tAtk}/{tDef}", Color.yellow);
                            spawned++;
                        }
                    }
                    return true;
                }

                case "gain_temp_mana":
                {
                    int gain = Mathf.Max(1, v1);
                    player.currentMana += gain;
                    if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"+{gain} 💧 MANA", new Color(0.35f, 0.80f, 1f));
                    if (v2 > 0)
                    {
                        player.HealLeader(v2, opponent);
                        if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, $"+{v2} HP", Color.green, true, v2);
                    }
                    return true;
                }

                case "drain_enemy_mana":
                {
                    int drain = Mathf.Max(1, v1);
                    opponent.currentMana = Mathf.Max(0, opponent.currentMana - drain);
                    if (v2 > 0)
                    {
                        int d = v2 + bonus;
                        opponent.TakeLeaderDamage(d);
                        if (showVfx) SpawnLeaderStatusVfx(oppIsHuman, $"-{drain} MANA / -{d} HP", new Color(0.75f, 0.35f, 1f));
                    }
                    else if (showVfx)
                    {
                        SpawnLeaderStatusVfx(oppIsHuman, $"-{drain} 💧 MANA", new Color(0.75f, 0.35f, 1f));
                    }
                    return true;
                }

                case "detonate_dots":
                {
                    foreach (var es in new List<BoardSlot>(enemies))
                    {
                        var em = es.currentMonster;
                        if (em == null || !em.IsAlive) continue;
                        int dotSum = em.burnDmg + em.poisonDmg + em.bleedDmg;
                        if (dotSum > 0 || v1 > 0)
                        {
                            int burst = dotSum + Mathf.Max(0, v1 + bonus);
                            if (showVfx) SpawnSlotStatusVfx(oppIsHuman, es.slotIndex, $"💥 DETONATE -{burst}", new Color(1f, 0.40f, 0.15f));
                            DealSpellOrSkillDamage(es.slotIndex, em, burst, m, opponent, player, true, showVfx);
                        }
                    }
                    return true;
                }

                case "recall_enemy":
                {
                    var ts = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                    if (ts != null && ts.currentMonster != null)
                    {
                        var em = ts.currentMonster;
                        if (v1 > 0) DealSpellOrSkillDamage(ts.slotIndex, em, v1 + bonus, m, opponent, player, true, showVfx);
                        if (em.IsAlive && !em.IsLocked && (op.cap <= 0 || em.data.cost <= op.cap))
                        {
                            if (opponent.hand.Count < opponent.maxHandSize) opponent.hand.Add(em.data);
                            ts.ClearSlot();
                            if (showVfx) SpawnSlotStatusVfx(oppIsHuman, ts.slotIndex, "↩️ RECALLED!", new Color(0.70f, 0.90f, 1f));
                        }
                    }
                    return true;
                }

                case "recall_ally":
                {
                    int tSlot = (explicitTargetSlot >= 0 && explicitTargetSlot < 6 && player.slots[explicitTargetSlot] != null && player.slots[explicitTargetSlot].IsOccupied)
                        ? explicitTargetSlot : slot;
                    var aSlot = player.slots[tSlot];
                    if (aSlot != null && aSlot.IsOccupied && aSlot.currentMonster != null)
                    {
                        var au = aSlot.currentMonster;
                        if (player.hand.Count < player.maxHandSize) player.hand.Add(au.data);
                        aSlot.ClearSlot();
                        if (v1 > 0) player.HealLeader(v1, opponent);
                        if (op.mana > 0) player.currentMana += op.mana;
                        if (showVfx) SpawnSlotStatusVfx(playerIsHuman, tSlot, "↩️ RETURNED TO HAND", Color.cyan);
                    }
                    return true;
                }

                case "double_skill_1t":
                {
                    int tSlot = (explicitTargetSlot >= 0 && explicitTargetSlot < 6 && player.slots[explicitTargetSlot] != null && player.slots[explicitTargetSlot].IsOccupied)
                        ? explicitTargetSlot : slot;
                    var au = player.slots[tSlot].currentMonster;
                    if (au != null && !au.IsBarrier) au.doubleSkillOneTurn = true;
                    if (v1 > 0) player.freeSkillBonus += v1;
                    if (showVfx) SpawnSlotStatusVfx(playerIsHuman, tSlot, "⚡ 2x SKILL THIS TURN!", Color.yellow);
                    return true;
                }

                case "grant_double_strike":
                {
                    int tSlot = (explicitTargetSlot >= 0 && explicitTargetSlot < 6 && player.slots[explicitTargetSlot] != null && player.slots[explicitTargetSlot].IsOccupied)
                        ? explicitTargetSlot : slot;
                    var au = player.slots[tSlot].currentMonster;
                    if (au != null && !au.IsBarrier)
                    {
                        au.doubleStrike = true;
                        if (v1 > 0 && !au.IsAllAttack) au.tempTurnAtk += v1;
                    }
                    if (showVfx) SpawnSlotStatusVfx(playerIsHuman, tSlot, $"⚔️ DOUBLE STRIKE (+{v1})", Color.yellow);
                    return true;
                }

                case "revive_board":
                {
                    BoardSlot emptySl = null;
                    for (int s = 0; s < 6; s++) if (player.slots[s] != null && !player.slots[s].IsOccupied) { emptySl = player.slots[s]; break; }
                    if (emptySl != null && player.graveyard != null)
                    {
                        CardData bestDead = null;
                        for (int gi = player.graveyard.Count - 1; gi >= 0; gi--)
                        {
                            var g = player.graveyard[gi];
                            if (g != null && g.cardType == CardType.Monster && (op.cap <= 0 || g.cost <= op.cap))
                            {
                                if (bestDead == null || g.cost > bestDead.cost) bestDead = g;
                            }
                        }
                        if (bestDead != null)
                        {
                            player.graveyard.Remove(bestDead);
                            var rev = new MonsterInstance(bestDead, turn);
                            if (v1 > 0) rev.ApplyPermBuff(v1, 0);
                            emptySl.PlaceMonster(rev);
                            if (showVfx) SpawnSlotStatusVfx(playerIsHuman, emptySl.slotIndex, "💀 REVIVED!", new Color(0.80f, 0.45f, 1f));
                        }
                    }
                    return true;
                }

                case "grave_to_hand":
                {
                    if (player.graveyard != null && player.hand.Count < player.maxHandSize)
                    {
                        CardData bestDead = null;
                        for (int gi = player.graveyard.Count - 1; gi >= 0; gi--)
                        {
                            var g = player.graveyard[gi];
                            if (g != null && (op.cap <= 0 || g.cost <= op.cap))
                            {
                                if (bestDead == null || g.cost > bestDead.cost) bestDead = g;
                            }
                        }
                        if (bestDead != null)
                        {
                            player.graveyard.Remove(bestDead);
                            player.hand.Add(bestDead);
                            player.discountedCardIds[bestDead.id] = 1;
                            if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"🪦 RECOVERED {bestDead.name}", new Color(0.80f, 0.55f, 1f));
                        }
                    }
                    return true;
                }

                case "copy_enemy_hand":
                {
                    if (opponent.hand != null && opponent.hand.Count > 0 && player.hand.Count < player.maxHandSize && GameManager.Instance != null)
                    {
                        var pick = opponent.hand[UnityEngine.Random.Range(0, opponent.hand.Count)];
                        var clone = GameManager.Instance.CloneCardById(pick.id);
                        if (clone != null)
                        {
                            player.hand.Add(clone);
                            player.discountedCardIds[clone.id] = 1;
                        }
                    }
                    if (v1 > 0) m.ApplyPermBuff(0, v1);
                    if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, "🪞 COPIED ENEMY CARD!", Color.magenta);
                    return true;
                }

                case "discount_hand_spells":
                {
                    foreach (var hc in player.hand)
                    {
                        if (hc != null && hc.cardType == CardType.Spell)
                        {
                            int cur = player.discountedCardIds.TryGetValue(hc.id, out int cv) ? cv : 0;
                            player.discountedCardIds[hc.id] = Mathf.Min(4, cur + 1);
                        }
                    }
                    if (v2 > 0) player.HealLeader(v2, opponent);
                    if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, "🔮 SPELLS -1 MANA!", new Color(0.65f, 0.75f, 1f));
                    return true;
                }

                case "buff_center":
                {
                    foreach (int cs in new[] { 2, 3 })
                    {
                        if (player.slots[cs] != null && player.slots[cs].IsOccupied && player.slots[cs].currentMonster != null)
                        {
                            player.slots[cs].currentMonster.ApplyPermBuff(0, v1);
                            if (showVfx) SpawnSlotStatusVfx(playerIsHuman, cs, $"+{v1} DEF", Color.cyan);
                        }
                    }
                    if (v2 > 0)
                    {
                        int hAmt = v2 + bonus;
                        player.HealLeader(hAmt, opponent);
                        if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, $"+{hAmt} HP", Color.green, true, hAmt);
                    }
                    return true;
                }

                case "warrior_call":
                {
                    CardData found = null;
                    if (player.deck != null)
                    {
                        for (int di = 0; di < player.deck.Count; di++)
                        {
                            var dc = player.deck[di];
                            if (dc != null && dc.cardType == CardType.Monster && (op.cap <= 0 || dc.cost <= op.cap))
                            {
                                found = dc;
                                player.deck.RemoveAt(di);
                                break;
                            }
                        }
                    }
                    if (found != null)
                    {
                        bool placed = false;
                        if (op.to_board)
                        {
                            for (int s = 0; s < 6; s++)
                            {
                                if (player.slots[s] != null && !player.slots[s].IsOccupied)
                                {
                                    var unit = new MonsterInstance(found, turn);
                                    if (op.buff > 0) unit.ApplyPermBuff(op.buff, op.buff);
                                    player.slots[s].PlaceMonster(unit);
                                    if (showVfx) SpawnSlotStatusVfx(playerIsHuman, s, $"📯 CALLED {found.name}", Color.yellow);
                                    placed = true;
                                    break;
                                }
                            }
                        }
                        if (!placed && player.hand.Count < player.maxHandSize)
                        {
                            player.hand.Add(found);
                            if (op.disc > 0) player.discountedCardIds[found.id] = op.disc;
                            if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"📯 DREW {found.name}", Color.yellow);
                        }
                    }
                    return true;
                }

                case "devour_3":
                {
                    var ts = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                    if (ts != null && ts.currentMonster != null && ts.currentMonster.IsAlive && (op.max_cost <= 0 || ts.currentMonster.data.cost <= op.max_cost))
                    {
                        var victim = ts.currentMonster;
                        int pct = op.pct > 0 ? op.pct : 50;
                        int gAtk = victim.IsBarrier ? 0 : V2CardMechanicsEngine.StrictRoundPct(victim.baseAtk + victim.bonusAtk, pct);
                        int gDef = V2CardMechanicsEngine.StrictRoundPct(victim.currentDef, pct);
                        victim.currentDef = 0;
                        HandleMonsterDeath(opponent, player, ts.slotIndex, victim, m);
                        m.ApplyPermBuff(gAtk, gDef);
                        if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"🩸 DEVOUR +{gAtk}/+{gDef}", new Color(0.95f, 0.25f, 0.45f));
                    }
                    return true;
                }

                case "mind_control_temp":
                {
                    var ts = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                    BoardSlot emptyAlly = null;
                    for (int s = 0; s < 6; s++) if (player.slots[s] != null && !player.slots[s].IsOccupied) { emptyAlly = player.slots[s]; break; }
                    if (ts != null && ts.currentMonster != null && emptyAlly != null && (op.cap <= 0 || ts.currentMonster.data.cost <= op.cap))
                    {
                        var stolen = ts.currentMonster;
                        ts.ClearSlot();
                        emptyAlly.PlaceMonster(stolen);
                        if (!stolen.IsBarrier)
                        {
                            stolen.canAttack = true;
                            if (v1 > 0 && !stolen.IsAllAttack) stolen.tempTurnAtk += v1;
                        }
                        if (showVfx) SpawnSlotStatusVfx(playerIsHuman, emptyAlly.slotIndex, "🎭 MIND CONTROLLED!", Color.magenta);
                    }
                    return true;
                }

                case "pull_hook":
                {
                    var ts = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                    if (ts != null && ts.currentMonster != null && ts.currentMonster.IsAlive)
                    {
                        var targetM = ts.currentMonster;
                        int dmg = Mathf.Max(1, v1 + bonus);
                        DealSpellOrSkillDamage(ts.slotIndex, targetM, dmg, m, opponent, player, true, showVfx);
                        if (targetM.IsAlive && !targetM.IsLocked && ts.slotIndex != slot && opponent.slots[slot] != null && !opponent.slots[slot].IsOccupied)
                        {
                            ts.ClearSlot();
                            opponent.slots[slot].PlaceMonster(targetM);
                            if (showVfx) SpawnSlotStatusVfx(oppIsHuman, slot, "⛓️ PULLED!", Color.yellow);
                        }
                    }
                    return true;
                }

                case "shifting_curse":
                {
                    var ts = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                    if (ts != null && ts.currentMonster != null && ts.currentMonster.IsAlive)
                    {
                        int cDmg = Mathf.Max(3, v1 + bonus);
                        ts.currentMonster.burnDmg += cDmg;
                        ts.currentMonster.weakenedAtkFlat += Mathf.Max(2, cDmg / 2);
                        if (showVfx) SpawnSlotStatusVfx(oppIsHuman, ts.slotIndex, $"☠️ CURSED -{cDmg}/T", new Color(0.75f, 0.25f, 0.85f));
                    }
                    return true;
                }

                case "absorb_ally_atk":
                {
                    if (!m.IsBarrier && !m.IsAllAttack)
                    {
                        int capA = op.cap > 0 ? op.cap : 14;
                        int sumAtk = 0;
                        foreach (var al in allies)
                        {
                            if (al == null || al.currentMonster == null || al.currentMonster == m || al.currentMonster.IsBarrier) continue;
                            sumAtk += Mathf.Max(0, al.currentMonster.GetEffectiveAtk(activeEnv, player));
                        }
                        int gain = Mathf.Min(capA, sumAtk);
                        if (gain > 0)
                        {
                            m.tempTurnAtk += gain;
                            if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"⚔️ +{gain} ATK (1T)", new Color(1f, 0.78f, 0.22f));
                        }
                    }
                    return true;
                }

                case "def_to_atk":
                {
                    if (!m.IsBarrier && !m.IsAllAttack)
                    {
                        int req = v1 > 0 ? v1 : 4;
                        int availD = Mathf.Max(0, m.currentDef - 2);
                        int roomA = Mathf.Max(0, MonsterInstance.MaxPermBonusAtk - m.bonusAtk);
                        int conv = Mathf.Min(req, Mathf.Min(availD, roomA));
                        if (conv > 0)
                        {
                            m.ApplyPermBuff(conv, 0);
                            m.currentDef = Mathf.Max(1, m.currentDef - conv);
                            m.baseDef = Mathf.Max(m.currentDef, m.baseDef - conv);
                            if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"⚔️ +{conv} ATK / -{conv} DEF", new Color(1f, 0.55f, 0.20f));
                        }
                    }
                    return true;
                }

                case "flip_enemy_stats":
                {
                    BoardSlot pick = null;
                    if (explicitTargetSlot >= 0 && explicitTargetSlot < 6 && opponent.slots[explicitTargetSlot] != null && opponent.slots[explicitTargetSlot].IsOccupied)
                    {
                        var em = opponent.slots[explicitTargetSlot].currentMonster;
                        if (em != null && em.IsAlive && !em.IsBarrier && !em.IsAllAttack && !V2CardMechanicsEngine.UnitHasPassiveKind(em, "spell_immune"))
                            pick = opponent.slots[explicitTargetSlot];
                    }
                    if (pick == null)
                    {
                        int bestScore = -1;
                        foreach (var es in enemies)
                        {
                            var em = es.currentMonster;
                            if (em == null || !em.IsAlive || em.IsBarrier || em.IsAllAttack || V2CardMechanicsEngine.UnitHasPassiveKind(em, "spell_immune")) continue;
                            int curA = Mathf.Max(0, em.baseAtk + em.bonusAtk);
                            int sc = Mathf.Abs(curA - em.currentDef) + curA;
                            if (sc > bestScore) { bestScore = sc; pick = es; }
                        }
                    }
                    if (pick != null && pick.currentMonster != null)
                    {
                        var tm = pick.currentMonster;
                        int oldA = Mathf.Max(0, tm.baseAtk + tm.bonusAtk);
                        int oldD = Mathf.Max(1, tm.currentDef);
                        tm.baseAtk = oldD;
                        tm.bonusAtk = 0;
                        tm.currentDef = Mathf.Max(1, oldA);
                        tm.baseDef = Mathf.Max(tm.baseDef, tm.currentDef);
                        if (showVfx) SpawnSlotStatusVfx(oppIsHuman, pick.slotIndex, $"🔄 FLIPPED {tm.baseAtk}/{tm.currentDef}", new Color(0.85f, 0.45f, 1f));
                    }
                    return true;
                }

                case "sac_ally_draw2":
                {
                    BoardSlot victimSlot = null;
                    if (explicitTargetSlot >= 0 && explicitTargetSlot < 6 && explicitTargetSlot != slot && player.slots[explicitTargetSlot] != null && player.slots[explicitTargetSlot].IsOccupied)
                        victimSlot = player.slots[explicitTargetSlot];
                    if (victimSlot == null)
                    {
                        int minPower = int.MaxValue;
                        foreach (var al in allies)
                        {
                            if (al == null || al.slotIndex == slot || al.currentMonster == null) continue;
                            int pw = (al.currentMonster.baseAtk + al.currentMonster.bonusAtk) + al.currentMonster.currentDef;
                            if (pw < minPower) { minPower = pw; victimSlot = al; }
                        }
                    }
                    if (victimSlot != null && victimSlot.currentMonster != null)
                    {
                        var victim = victimSlot.currentMonster;
                        victim.currentDef = 0;
                        HandleMonsterDeath(player, player, victimSlot.slotIndex, victim, m);
                        int drawCnt = v1 > 0 ? v1 : 2;
                        int drawn = 0;
                        if (!V2CardMechanicsEngine.IsExtraDrawBanned(player))
                        {
                            for (int di = 0; di < drawCnt; di++)
                            {
                                if (player.hand.Count < player.maxHandSize)
                                {
                                    player.DrawCard();
                                    drawn++;
                                }
                            }
                        }
                        if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"💀 SACRIFICE +{drawn} CARDS", new Color(0.65f, 0.85f, 1f));
                    }
                    return true;
                }

                case "sac_ally_aether":
                {
                    BoardSlot victimSlot = null;
                    if (explicitTargetSlot >= 0 && explicitTargetSlot < 6 && explicitTargetSlot != slot && player.slots[explicitTargetSlot] != null && player.slots[explicitTargetSlot].IsOccupied)
                        victimSlot = player.slots[explicitTargetSlot];
                    if (victimSlot == null)
                    {
                        int minPower = int.MaxValue;
                        foreach (var al in allies)
                        {
                            if (al == null || al.slotIndex == slot || al.currentMonster == null) continue;
                            int pw = (al.currentMonster.baseAtk + al.currentMonster.bonusAtk) + al.currentMonster.currentDef;
                            if (pw < minPower) { minPower = pw; victimSlot = al; }
                        }
                    }
                    if (victimSlot != null && victimSlot.currentMonster != null)
                    {
                        var victim = victimSlot.currentMonster;
                        victim.currentDef = 0;
                        HandleMonsterDeath(player, player, victimSlot.slotIndex, victim, m);
                        int cap = V2CardMechanicsEngine.GetPlayerManaCap(player);
                        int gainAe = v1 > 0 ? v1 : 1;
                        if (player.power < cap) player.power = Mathf.Min(cap, player.power + gainAe);
                        player.currentMana += 1;
                        if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"⚡ +{gainAe} AETHER & +1 MANA", new Color(0.45f, 0.90f, 1f));
                    }
                    return true;
                }

                case "mirror_enemy_exact":
                {
                    BoardSlot srcSlot = null;
                    if (explicitTargetSlot >= 0 && explicitTargetSlot < 6 && opponent.slots[explicitTargetSlot] != null && opponent.slots[explicitTargetSlot].IsOccupied)
                    {
                        var em = opponent.slots[explicitTargetSlot].currentMonster;
                        if (em != null && em.IsAlive && !em.IsBarrier) srcSlot = opponent.slots[explicitTargetSlot];
                    }
                    if (srcSlot == null)
                    {
                        int bestPw = -1;
                        foreach (var es in enemies)
                        {
                            var em = es.currentMonster;
                            if (em == null || !em.IsAlive || em.IsBarrier) continue;
                            int pw = (em.baseAtk + em.bonusAtk) + em.currentDef;
                            if (pw > bestPw) { bestPw = pw; srcSlot = es; }
                        }
                    }
                    if (srcSlot != null && srcSlot.currentMonster != null)
                    {
                        var src = srcSlot.currentMonster;
                        m.data = src.data;
                        m.baseAtk = src.baseAtk;
                        m.bonusAtk = src.bonusAtk;
                        m.baseDef = src.baseDef;
                        m.currentDef = Mathf.Max(1, src.currentDef);
                        m.permBonusDef = src.permBonusDef;
                        m.tempTurnAtk = 0;
                        m.leaderDefApplied = 0;
                        m.passives = src.passives != null ? new List<string>(src.passives) : new List<string>();
                        m.actives = src.actives != null ? new List<string>(src.actives) : new List<string>();
                        m.doubleStrike = src.doubleStrike;
                        m.canAttack = !m.IsBarrier && (m.canAttack || src.HasPassiveSubstr("Haste") != null);
                        if (V2CardMechanicsEngine.DoesUnitMatchOwnerLeader(m, player))
                        {
                            var lops = V2CardMechanicsEngine.GetLeaderOps(player.activeLeader);
                            if (lops != null && lops.aura_def > 0)
                            {
                                m.baseDef += lops.aura_def;
                                m.currentDef += lops.aura_def;
                                m.leaderDefApplied = lops.aura_def;
                            }
                        }
                        // Lock copied skills for rest of this turn to prevent infinite copy loops
                        m.activeUsedThisTurn = new List<bool>();
                        m.activeUseCountsThisTurn = new List<int>();
                        for (int ai = 0; ai < m.actives.Count; ai++)
                        {
                            m.activeUsedThisTurn.Add(true);
                            m.activeUseCountsThisTurn.Add(2);
                        }
                        m.hasUsedActiveThisTurn = m.actives.Count > 0;
                        if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"🪞 MIRRORED {src.data.name}", new Color(0.85f, 0.55f, 1f));
                    }
                    return true;
                }

                case "discard_hand_mana":
                {
                    if (player.hand != null && player.hand.Count > 0)
                    {
                        int cheapIdx = 0;
                        for (int hi = 1; hi < player.hand.Count; hi++)
                            if (player.hand[hi] != null && player.hand[cheapIdx] != null && player.hand[hi].cost < player.hand[cheapIdx].cost)
                                cheapIdx = hi;
                        var disc = player.hand[cheapIdx];
                        player.hand.RemoveAt(cheapIdx);
                        if (disc != null) player.graveyard.Add(disc);
                        int gainM = v1 > 0 ? v1 : 3;
                        player.currentMana += gainM;
                        if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"💧 DISCARD -> +{gainM} MANA", new Color(0.35f, 0.80f, 1f));
                    }
                    return true;
                }

                case "discard_hand_2aether":
                {
                    if (player.hand != null && player.hand.Count > 0)
                    {
                        int cheapIdx = 0;
                        for (int hi = 1; hi < player.hand.Count; hi++)
                            if (player.hand[hi] != null && player.hand[cheapIdx] != null && player.hand[hi].cost < player.hand[cheapIdx].cost)
                                cheapIdx = hi;
                        var disc = player.hand[cheapIdx];
                        player.hand.RemoveAt(cheapIdx);
                        if (disc != null) player.graveyard.Add(disc);
                        int cap = V2CardMechanicsEngine.GetPlayerManaCap(player);
                        int gainAe = v1 > 0 ? v1 : 2;
                        int gainM = v2 > 0 ? v2 : 1;
                        if (player.power < cap) player.power = Mathf.Min(cap, player.power + gainAe);
                        player.currentMana += gainM;
                        if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"⚡ DISCARD -> +{gainAe} AETHER", new Color(0.55f, 0.92f, 1f));
                    }
                    return true;
                }

                case "dmg_face_aether":
                {
                    int dmgAe = Mathf.Max(1, player.power) + bonus;
                    GetLeaderDmgDealt(opponent, dmgAe);
                    if (showVfx) SpawnLeaderStatusVfx(oppIsHuman, $"⚡ -{dmgAe} AETHER BLAST", new Color(0.45f, 0.85f, 1f), false, dmgAe);
                    return true;
                }

                case "relocate_self":
                {
                    if (!m.IsLocked && player.slots != null)
                    {
                        int openLane = -1;
                        int blockLane = -1;
                        int bestBlkAtk = -1;
                        int anyEmpty = -1;
                        for (int s = 0; s < 6; s++)
                        {
                            if (player.slots[s] == null || player.slots[s].IsOccupied) continue;
                            if (anyEmpty < 0) anyEmpty = s;
                            bool oppEmpty = (opponent.slots[s] == null || !opponent.slots[s].IsOccupied);
                            if (oppEmpty && openLane < 0) openLane = s;
                            if (!oppEmpty && opponent.slots[s].currentMonster != null && !opponent.slots[s].currentMonster.IsBarrier)
                            {
                                int ea = opponent.slots[s].currentMonster.GetEffectiveAtk(activeEnv, opponent);
                                if (ea > bestBlkAtk) { bestBlkAtk = ea; blockLane = s; }
                            }
                        }
                        int myAtk = m.GetEffectiveAtk(activeEnv, player);
                        int dst = (myAtk >= m.currentDef && openLane >= 0) ? openLane : (blockLane >= 0 ? blockLane : anyEmpty);
                        if (dst >= 0 && dst != slot)
                        {
                            player.slots[slot].ClearSlot();
                            player.slots[dst].PlaceMonster(m);
                            if (showVfx) SpawnSlotStatusVfx(playerIsHuman, dst, "💨 RELOCATED!", new Color(0.55f, 0.95f, 1f));
                        }
                    }
                    return true;
                }

                case "reposition_ally":
                {
                    if (player.slots != null)
                    {
                        BoardSlot allySlot = null;
                        if (explicitTargetSlot >= 0 && explicitTargetSlot < 6 && explicitTargetSlot != slot &&
                            player.slots[explicitTargetSlot] != null && player.slots[explicitTargetSlot].IsOccupied &&
                            player.slots[explicitTargetSlot].currentMonster != null && !player.slots[explicitTargetSlot].currentMonster.IsLocked)
                        {
                            allySlot = player.slots[explicitTargetSlot];
                        }
                        if (allySlot == null)
                        {
                            int bestScore = int.MinValue;
                            foreach (var al in allies)
                            {
                                if (al == null || al.slotIndex == slot || al.currentMonster == null || al.currentMonster.IsLocked) continue;
                                bool alreadyFacing = opponent.slots[al.slotIndex] != null && opponent.slots[al.slotIndex].IsOccupied;
                                int sc = (alreadyFacing ? 0 : 100) + al.currentMonster.currentDef + al.currentMonster.baseAtk;
                                if (sc > bestScore) { bestScore = sc; allySlot = al; }
                            }
                        }
                        if (allySlot != null && allySlot.currentMonster != null)
                        {
                            int oppLane = -1;
                            int bestEnemyScore = -1;
                            int anyEmpty = -1;
                            for (int s = 0; s < 6; s++)
                            {
                                if (player.slots[s] == null || player.slots[s].IsOccupied) continue;
                                if (anyEmpty < 0) anyEmpty = s;
                                if (opponent.slots[s] != null && opponent.slots[s].IsOccupied && opponent.slots[s].currentMonster != null)
                                {
                                    int es = opponent.slots[s].currentMonster.GetEffectiveAtk(activeEnv, opponent) * 2 + opponent.slots[s].currentMonster.currentDef;
                                    if (es > bestEnemyScore) { bestEnemyScore = es; oppLane = s; }
                                }
                            }
                            int dst = oppLane >= 0 ? oppLane : anyEmpty;
                            if (dst >= 0 && dst != allySlot.slotIndex)
                            {
                                var moved = allySlot.currentMonster;
                                allySlot.ClearSlot();
                                player.slots[dst].PlaceMonster(moved);
                                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, dst, "🧭 REPOSITIONED!", new Color(0.55f, 0.95f, 0.75f));
                            }
                        }
                    }
                    return true;
                }

                case "ally_atk_to_def":
                {
                    BoardSlot allySlot = null;
                    if (explicitTargetSlot >= 0 && explicitTargetSlot < 6 && explicitTargetSlot != slot &&
                        player.slots[explicitTargetSlot] != null && player.slots[explicitTargetSlot].IsOccupied)
                    {
                        var am = player.slots[explicitTargetSlot].currentMonster;
                        if (am != null && am.IsAlive && !am.IsBarrier && !am.IsAllAttack && (am.baseAtk + am.bonusAtk) > 0)
                            allySlot = player.slots[explicitTargetSlot];
                    }
                    if (allySlot == null)
                    {
                        int bestAtk = 0;
                        foreach (var al in allies)
                        {
                            if (al == null || al.slotIndex == slot || al.currentMonster == null) continue;
                            var am = al.currentMonster;
                            if (am.IsBarrier || am.IsAllAttack) continue;
                            int aVal = am.baseAtk + am.bonusAtk;
                            if (aVal > bestAtk) { bestAtk = aVal; allySlot = al; }
                        }
                    }
                    if (allySlot != null && allySlot.currentMonster != null)
                    {
                        var ally = allySlot.currentMonster;
                        int gainD = Mathf.Max(1, ally.baseAtk + ally.bonusAtk);
                        ally.currentDef += gainD;
                        ally.baseDef += gainD;
                        ally.baseAtk = 0;
                        ally.bonusAtk = 0;
                        ally.tempTurnAtk = 0;
                        if (ally.HasPassiveSubstr("Taunt") == null)
                            ally.passives.Add("Taunt: Must be attacked first");
                        if (showVfx) SpawnSlotStatusVfx(playerIsHuman, allySlot.slotIndex, $"🏰 +{gainD} DEF & TAUNT", new Color(0.45f, 0.85f, 1f));
                    }
                    return true;
                }

                case "betray_commander":
                {
                    BoardSlot pick = null;
                    if (explicitTargetSlot >= 0 && explicitTargetSlot < 6 && opponent.slots[explicitTargetSlot] != null && opponent.slots[explicitTargetSlot].IsOccupied)
                    {
                        var em = opponent.slots[explicitTargetSlot].currentMonster;
                        if (em != null && em.IsAlive && !em.IsBarrier && !V2CardMechanicsEngine.UnitHasPassiveKind(em, "spell_immune") && em.GetEffectiveAtk(activeEnv, opponent) > 0)
                            pick = opponent.slots[explicitTargetSlot];
                    }
                    if (pick == null)
                    {
                        int bestAtk = 0;
                        foreach (var es in enemies)
                        {
                            var em = es.currentMonster;
                            if (em == null || !em.IsAlive || em.IsBarrier || V2CardMechanicsEngine.UnitHasPassiveKind(em, "spell_immune")) continue;
                            int ea = em.GetEffectiveAtk(activeEnv, opponent);
                            if (ea > bestAtk) { bestAtk = ea; pick = es; }
                        }
                    }
                    if (pick != null && pick.currentMonster != null)
                    {
                        int betrayDmg = Mathf.Max(0, pick.currentMonster.GetEffectiveAtk(activeEnv, opponent));
                        if (betrayDmg > 0)
                        {
                            GetLeaderDmgDealt(opponent, betrayDmg);
                            if (showVfx)
                            {
                                SpawnSlotStatusVfx(oppIsHuman, pick.slotIndex, "🗡️ BETRAYED!", new Color(1f, 0.25f, 0.35f));
                                SpawnLeaderStatusVfx(oppIsHuman, $"🗡️ TREASON -{betrayDmg}", new Color(1f, 0.25f, 0.35f), false, betrayDmg);
                            }
                        }
                    }
                    return true;
                }
            }
            return false;
        }

        private void ResolveSingleActive(string act, int slot, MonsterInstance m, PlayerState player, PlayerState opponent, int bonusEff, bool showVfx = true, int explicitTargetSlot = -1, int skillIndex = -1)
        {
            // PRIORITY 1: Execute exact Arena_Lab_V2 structured active skill (all 25 V2 mechanics!)
            int resolvedIdx = skillIndex;
            if (resolvedIdx < 0 && m != null && m.actives != null) resolvedIdx = m.actives.IndexOf(act);
            var v2op = V2CardMechanicsEngine.GetV2ActiveOp(m, resolvedIdx);
            if (v2op != null && TryResolveV2ActiveSkill(v2op, slot, m, player, opponent, bonusEff, showVfx, explicitTargetSlot))
            {
                return;
            }

            var enemies = GetAliveSlots(opponent);
            var allies = GetAliveSlots(player);
            bool playerIsHuman = GameManager.Instance != null && (player == GameManager.Instance.humanPlayer);
            bool oppIsHuman = !playerIsHuman;
            var activeEnv = GameManager.Instance != null ? GameManager.Instance.activeEnvironment : null;

            // --- FIRE ---
            if (act.Contains("Hell of Annihilation"))
            {
                int dmg = 18 + bonusEff;
                if (enemies.Count > 0)
                {
                    foreach (var es in enemies)
                        DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, dmg, m, opponent, player, true, showVfx);
                }
                else
                {
                    opponent.TakeLeaderDamage(dmg);
                    if (showVfx) SpawnLeaderStatusVfx(oppIsHuman, $"-{dmg} 🔥", new Color(1f, 0.45f, 0.15f));
                }
            }
            else if (act.Contains("Volcanic Eruption"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
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
                        DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 18 + bonusEff, m, opponent, player, true, showVfx);
                    }
                }
            }
            else if (act.Contains("Fireball"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 16 + bonusEff, m, opponent, player, false, showVfx);
            }
            else if (act.Contains("Ember Blast"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (targetSlot != null)
                {
                    int idx = targetSlot.slotIndex;
                    DealSpellOrSkillDamage(idx, targetSlot.currentMonster, 8 + bonusEff, m, opponent, player, false, showVfx);
                    foreach (int adj in new[] { idx - 1, idx + 1 })
                    {
                        if (adj >= 0 && adj < 6 && opponent.slots[adj] != null && opponent.slots[adj].IsOccupied)
                            DealSpellOrSkillDamage(adj, opponent.slots[adj].currentMonster, 8, m, opponent, player, false, showVfx);
                    }
                }
            }
            else if (act.Contains("Lava Torrent"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 15 + bonusEff, m, opponent, player, false, showVfx);
            }
            else if (act.Contains("Cinder Nova"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (targetSlot != null)
                {
                    var em = targetSlot.currentMonster;
                    DealSpellOrSkillDamage(targetSlot.slotIndex, em, 12 + bonusEff, m, opponent, player, false, showVfx);
                    if (em.IsAlive && em.HasPassiveSubstr("Immunity to Fire") == null &&
                        em.HasPassiveSubstr("Moisture Shield") == null && em.HasPassiveSubstr("Water Resilience") == null)
                    {
                        em.burnDmg = Mathf.Max(em.burnDmg, 4);
                        if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "🔥 BURN 4/T!", new Color(1f, 0.45f, 0.15f));
                    }
                }
            }
            else if (act.Contains("Purging Flame"))
            {
                MonsterInstance ally;
                if (explicitTargetSlot >= 0 && explicitTargetSlot < player.slots.Length && player.slots[explicitTargetSlot] != null && player.slots[explicitTargetSlot].IsOccupied)
                {
                    ally = player.slots[explicitTargetSlot].currentMonster;
                }
                else
                {
                    ally = m;
                    foreach (var al in allies)
                    {
                        if (al.currentMonster.burnDmg > 0 || al.currentMonster.poisonDmg > 0 || al.currentMonster.frozenTurns > 0 || al.currentMonster.weakenedTurns > 0)
                        {
                            ally = al.currentMonster;
                            break;
                        }
                    }
                }
                ally.burnDmg = 0;
                ally.poisonDmg = 0;
                ally.frozenTurns = 0;
                ally.weakenedTurns = 0;
                ally.silencedTurns = 0;
                ally.bonusAtk += 6 + bonusEff;
                ally.AddBonusDef(6);
                int aSlot = ally.currentSlot != null ? ally.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"🔥 CLEANSED +{6 + bonusEff}/+6", new Color(1f, 0.65f, 0.25f));
            }
            // --- WATER ---
            else if (act.Contains("Sweeping Tsunami"))
            {
                foreach (var es in enemies)
                {
                    es.currentMonster.bonusAtk = 0;
                    DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, 14 + bonusEff, m, opponent, player, true, showVfx);
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
                        if (em.HasPassiveSubstr("Immunity to Water") == null)
                        {
                            em.frozenTurns = Mathf.Max(em.frozenTurns, 1);
                            em.canAttack = false;
                            frozenCount++;
                            if (showVfx) SpawnSlotStatusVfx(oppIsHuman, vIdx, "❄️ FROZEN!", new Color(0.45f, 0.85f, 1f));
                        }
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
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (targetSlot != null)
                {
                    var em = targetSlot.currentMonster;
                    em.canAttack = false;
                    int idx = targetSlot.slotIndex;
                    if (!em.HasEarthSteadfastness(activeEnv))
                    {
                        foreach (int adj in new[] { idx - 1, idx + 1 })
                        {
                            if (adj >= 0 && adj < 6 && opponent.slots[adj] != null && !opponent.slots[adj].IsOccupied)
                            {
                                targetSlot.ClearSlot();
                                opponent.slots[adj].PlaceMonster(em);
                                idx = adj;
                                break;
                            }
                        }
                    }
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, idx, "🌊 VORTEX!", new Color(0.3f, 0.75f, 1f));
                    DealSpellOrSkillDamage(idx, em, 14 + bonusEff, m, opponent, player, true, showVfx);
                }
            }
            else if (act.Contains("Abyssal Geyser"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (targetSlot != null)
                {
                    var em = targetSlot.currentMonster;
                    em.bubbleShield = false;
                    em.bonusAtk = Mathf.Min(0, em.bonusAtk);
                    em.tempTurnAtk = Mathf.Min(0, em.tempTurnAtk);
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "🌊 STRIPPED!", new Color(0.35f, 0.85f, 1f));
                    DealSpellOrSkillDamage(targetSlot.slotIndex, em, 13 + bonusEff, m, opponent, player, true, showVfx);
                }
            }
            else if (act.Contains("Healing Stream"))
            {
                var target = GetAllyTarget(m, allies, player, false, explicitTargetSlot);
                target.HealDefense(20 + bonusEff);
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"+{20 + bonusEff} DEF 💚", new Color(0.35f, 1f, 0.55f));
            }
            else if (act.Contains("Safety Bubble"))
            {
                MonsterInstance target;
                if (explicitTargetSlot >= 0 && explicitTargetSlot < player.slots.Length && player.slots[explicitTargetSlot] != null && player.slots[explicitTargetSlot].IsOccupied)
                {
                    target = player.slots[explicitTargetSlot].currentMonster;
                }
                else
                {
                    target = m;
                    foreach (var al in allies)
                    {
                        if (!al.currentMonster.bubbleShield)
                        {
                            target = al.currentMonster;
                            break;
                        }
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
                    if (es.currentMonster.HasPassiveSubstr("Immunity to Ice") == null)
                    {
                        es.currentMonster.frozenTurns = Mathf.Max(es.currentMonster.frozenTurns, 1);
                        es.currentMonster.canAttack = false;
                        if (showVfx) SpawnSlotStatusVfx(oppIsHuman, es.slotIndex, "❄️ FROZEN!", new Color(0.55f, 0.92f, 1f));
                    }
                    DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, 10 + bonusEff, m, opponent, player, false, showVfx);
                }
            }
            else if (act.Contains("Frost Shatter"))
            {
                BoardSlot chosenSlot = (explicitTargetSlot >= 0 && explicitTargetSlot < 6 && opponent.slots[explicitTargetSlot] != null && opponent.slots[explicitTargetSlot].IsOccupied)
                    ? opponent.slots[explicitTargetSlot]
                    : null;

                if (chosenSlot == null)
                {
                    foreach (var es in enemies)
                    {
                        if (es.currentMonster.frozenTurns > 0)
                        {
                            chosenSlot = es;
                            break;
                        }
                    }
                }
                if (chosenSlot == null)
                {
                    chosenSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                }

                if (chosenSlot != null)
                {
                    if (chosenSlot.currentMonster.frozenTurns > 0)
                    {
                        if (showVfx) SpawnSlotStatusVfx(oppIsHuman, chosenSlot.slotIndex, "💥 SHATTERED!", new Color(0.55f, 0.95f, 1f));
                        chosenSlot.currentMonster.currentDef = 0;
                        HandleMonsterDeath(opponent, player, chosenSlot.slotIndex, chosenSlot.currentMonster, m);
                    }
                    else
                    {
                        if (chosenSlot.currentMonster.HasPassiveSubstr("Immunity to Ice") == null)
                        {
                            chosenSlot.currentMonster.frozenTurns = Mathf.Max(chosenSlot.currentMonster.frozenTurns, 1);
                            chosenSlot.currentMonster.canAttack = false;
                        }
                        DealSpellOrSkillDamage(chosenSlot.slotIndex, chosenSlot.currentMonster, 18 + bonusEff, m, opponent, player, true, showVfx);
                    }
                }
            }
            else if (act.Contains("Ice Spear"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                {
                    if (targetSlot.currentMonster.HasPassiveSubstr("Immunity to Ice") == null)
                    {
                        targetSlot.currentMonster.frozenTurns = Mathf.Max(targetSlot.currentMonster.frozenTurns, 1);
                        targetSlot.currentMonster.canAttack = false;
                    }
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 10 + bonusEff, m, opponent, player, false, showVfx);
                }
            }
            else if (act.Contains("Cryo Lance"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                {
                    if (targetSlot.currentMonster.HasPassiveSubstr("Immunity to Ice") == null)
                    {
                        targetSlot.currentMonster.frozenTurns = Mathf.Max(targetSlot.currentMonster.frozenTurns, 1);
                        targetSlot.currentMonster.canAttack = false;
                    }
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 12 + bonusEff, m, opponent, player, false, showVfx);
                }
            }
            else if (act.Contains("Snow Wall"))
            {
                var target = GetAllyTarget(m, allies, player, false, explicitTargetSlot);
                target.AddBonusDef(24 + bonusEff);
                target.bonusAtk += 3;
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"❄️ +3 ATK / +{24 + bonusEff} DEF", new Color(0.6f, 0.92f, 1f));
            }
            // --- ELECTRIC ---
            else if (act.Contains("Grand Thunderstorm"))
            {
                int perBolt = 8 + bonusEff / 2;
                for (int b = 0; b < 4; b++)
                {
                    var aliveEnemies = GetAliveSlots(opponent);
                    if (aliveEnemies.Count > 0)
                    {
                        var pick = aliveEnemies[UnityEngine.Random.Range(0, aliveEnemies.Count)];
                        DealSpellOrSkillDamage(pick.slotIndex, pick.currentMonster, perBolt, m, opponent, player, true, showVfx);
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
                    if (es.currentMonster.HasPassiveSubstr("Immunity to Electric") == null)
                    {
                        es.currentMonster.silencedTurns = Mathf.Max(es.currentMonster.silencedTurns, 2);
                        if (showVfx) SpawnSlotStatusVfx(oppIsHuman, es.slotIndex, "🔒 SILENCED!", new Color(1f, 0.88f, 0.25f));
                    }
                }
            }
            else if (act.Contains("Sudden Shock"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 14 + bonusEff, m, opponent, player, false, showVfx);
            }
            else if (act.Contains("Static Jolt"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                {
                    if (targetSlot.currentMonster.HasPassiveSubstr("Immunity to Electric") == null)
                        targetSlot.currentMonster.silencedTurns = Mathf.Max(targetSlot.currentMonster.silencedTurns, 1);
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 9 + bonusEff, m, opponent, player, false, showVfx);
                }
            }
            else if (act.Contains("Energy Surge"))
            {
                var target = GetAllyTarget(m, allies, player, true, explicitTargetSlot);
                target.bonusAtk += 8 + bonusEff;
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"⚡ +{8 + bonusEff} ATK!", new Color(1f, 0.92f, 0.25f));
            }
            // --- AIR ---
            else if (act.Contains("Sweeping Cyclone"))
            {
                if (enemies.Count > 0)
                {
                    BoardSlot best = (explicitTargetSlot >= 0 && explicitTargetSlot < 6 && opponent.slots[explicitTargetSlot] != null && opponent.slots[explicitTargetSlot].IsOccupied)
                        ? opponent.slots[explicitTargetSlot]
                        : null;
                    if (best == null)
                    {
                        best = enemies[0];
                        foreach (var es in enemies)
                        {
                            if (es.currentMonster.data.cost > best.currentMonster.data.cost) best = es;
                        }
                    }
                    var bm = best.currentMonster;
                    if (!bm.HasEarthSteadfastness(activeEnv) && bm.HasPassiveSubstr("Nimble Agility") == null && bm.HasPassiveSubstr("Immunity to Air") == null)
                    {
                        if (showVfx) SpawnSlotStatusVfx(oppIsHuman, best.slotIndex, "🌪️ BOUNCED!", new Color(0.5f, 0.95f, 0.8f));
                        if (bm.data != null && bm.data.id < 9000) opponent.AddCardToHandWithBurnLimit(bm.data);
                        best.ClearSlot();
                    }
                    else
                    {
                        DealSpellOrSkillDamage(best.slotIndex, bm, 14 + bonusEff, m, opponent, player, true, showVfx);
                    }
                }
            }
            else if (act.Contains("Winds of Annihilation"))
            {
                foreach (var es in enemies)
                    DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, 15 + bonusEff, m, opponent, player, true, showVfx);
            }
            else if (act.Contains("Cutting Gale"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (targetSlot != null)
                {
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 12 + bonusEff, m, opponent, player, false, showVfx);
                    var remaining = GetAliveSlots(opponent);
                    if (remaining.Count >= 2)
                    {
                        var s1 = remaining[0];
                        var s2 = remaining[remaining.Count - 1];
                        if (!s1.currentMonster.HasEarthSteadfastness(activeEnv) && !s2.currentMonster.HasEarthSteadfastness(activeEnv))
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
                MonsterInstance target;
                if (explicitTargetSlot >= 0 && explicitTargetSlot < player.slots.Length && player.slots[explicitTargetSlot] != null && player.slots[explicitTargetSlot].IsOccupied)
                {
                    target = player.slots[explicitTargetSlot].currentMonster;
                }
                else
                {
                    target = !m.canAttack ? m : GetBestAllyForOffensiveBuff(m, allies);
                    foreach (var al in allies)
                    {
                        if (!al.currentMonster.canAttack && al.currentMonster != m &&
                            (al.currentMonster.baseAtk + al.currentMonster.bonusAtk) > (m.baseAtk + m.bonusAtk))
                        {
                            target = al.currentMonster;
                        }
                    }
                }
                target.canAttack = true;
                target.bonusAtk += 5 + bonusEff;
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"🌪️ READY! +{5 + bonusEff} ATK", new Color(0.45f, 1f, 0.78f));
            }
            else if (act.Contains("Tempest Tailwind"))
            {
                MonsterInstance target = null;
                if (explicitTargetSlot >= 0 && explicitTargetSlot < player.slots.Length && player.slots[explicitTargetSlot] != null && player.slots[explicitTargetSlot].IsOccupied)
                {
                    target = player.slots[explicitTargetSlot].currentMonster;
                }
                else
                {
                    foreach (var al in allies)
                    {
                        if (!al.currentMonster.canAttack) { target = al.currentMonster; break; }
                    }
                    if (target == null) target = GetBestAllyForOffensiveBuff(m, allies);
                }
                target.canAttack = true;
                target.AddBonusDef(6 + bonusEff / 2);
                if (target.HasPassiveSubstr("Mist Veil") == null)
                    target.passives.Add("Mist Veil: 15% Evasion");
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, "🌪️ TAILWIND VEIL!", new Color(0.45f, 1f, 0.85f));
            }
            // --- EARTH ---
            else if (act.Contains("Great Earth Fissure"))
            {
                BoardSlot targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (explicitTargetSlot < 0 && (targetSlot == null || (targetSlot.slotIndex != 2 && targetSlot.slotIndex != 3)))
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
                    if (targetSlot.currentMonster.currentDef <= 32 + bonusEff)
                    {
                        if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "🪨 CRUSHED!", new Color(0.85f, 0.65f, 0.35f));
                        targetSlot.currentMonster.currentDef = 0;
                        HandleMonsterDeath(opponent, player, targetSlot.slotIndex, targetSlot.currentMonster, m);
                    }
                    else
                    {
                        DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 25 + bonusEff, m, opponent, player, true, showVfx);
                    }
                }
            }
            else if (act.Contains("Immortal Mountain Shield"))
            {
                player.teamImmuneTurns = Mathf.Max(player.teamImmuneTurns, 1);
                foreach (var al in allies)
                {
                    al.currentMonster.AddBonusDef(6 + bonusEff / 2);
                    al.currentMonster.bonusAtk += 4;
                }
                if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, "🛡️ MOUNTAIN AEGIS!", new Color(0.95f, 0.85f, 0.35f));
            }
            else if (act.Contains("Iron Dome Shield"))
            {
                player.teamImmuneTurns = Mathf.Max(player.teamImmuneTurns, 1);
                foreach (var al in allies)
                {
                    al.currentMonster.AddBonusDef(8 + bonusEff / 2);
                    al.currentMonster.bonusAtk += 4;
                }
                if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, "🛡️ IRON DOME!", new Color(0.85f, 0.85f, 0.95f));
            }
            else if (act.Contains("Earthquake Strike"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (targetSlot != null)
                {
                    int idx = targetSlot.slotIndex;
                    DealSpellOrSkillDamage(idx, targetSlot.currentMonster, 15 + bonusEff, m, opponent, player, false, showVfx);
                    foreach (int adj in new[] { idx - 1, idx + 1 })
                    {
                        if (adj >= 0 && adj < 6 && opponent.slots[adj] != null && opponent.slots[adj].IsOccupied)
                            DealSpellOrSkillDamage(adj, opponent.slots[adj].currentMonster, 5, m, opponent, player, false, showVfx);
                    }
                }
            }
            else if (act.Contains("Tectonic Slam"))
            {
                player.shield += 5;
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (targetSlot != null)
                {
                    int idx = targetSlot.slotIndex;
                    DealSpellOrSkillDamage(idx, targetSlot.currentMonster, 11 + bonusEff, m, opponent, player, false, showVfx);
                    foreach (int adj in new[] { idx - 1, idx + 1 })
                    {
                        if (adj >= 0 && adj < 6 && opponent.slots[adj] != null && opponent.slots[adj].IsOccupied)
                            DealSpellOrSkillDamage(adj, opponent.slots[adj].currentMonster, 5, m, opponent, player, false, showVfx);
                    }
                }
            }
            else if (act.Contains("Stone Fortification"))
            {
                var target = GetAllyTarget(m, allies, player, false, explicitTargetSlot);
                target.AddBonusDef(15 + bonusEff);
                if (target.HasPassiveSubstr("Earth Steadfastness") == null)
                {
                    target.passives.Add("Earth Steadfastness: Immune to displacement and bounce");
                }
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"🪨 +{15 + bonusEff} DEF ANCHORED", new Color(0.85f, 0.65f, 0.38f));
            }
            // --- NATURE ---
            else if (act.Contains("Wrath of Nature"))
            {
                var treant = CreateTokenData(9998, "Nature", "Giants", "Defender", 2, 2, 6, 30,
                    "Poison Thorns: Deals 3 damage and applies 2/T Venom to attackers",
                    "Summoned Treant Guardian (30 DEF, Poison Thorns).");
                SpawnTokensOnFlanks(player, treant, false);
            }
            else if (act.Contains("Hive Swarm Call"))
            {
                var beetle = CreateTokenData(9996, "Nature", "Insects", "Attacker", 2, 2, 6, 12,
                    "Briar Dart: Applies 2/turn Poison on combat strike",
                    "Summoned Venom Beetle (6 ATK, 12 DEF, Briar Dart).");
                SpawnTokensOnFlanks(player, beetle, true);
            }
            else if (act.Contains("Organic Blight Poison"))
            {
                foreach (var es in enemies)
                {
                    if (es.currentMonster.HasPassiveSubstr("Immunity to Poison") == null)
                        es.currentMonster.poisonDmg = Mathf.Max(es.currentMonster.poisonDmg, 6);
                    DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, 7 + bonusEff, m, opponent, player, true, showVfx);
                }
            }
            else if (act.Contains("Strangling Roots"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                {
                    targetSlot.currentMonster.frozenTurns = Mathf.Max(targetSlot.currentMonster.frozenTurns, 2);
                    targetSlot.currentMonster.canAttack = false;
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "🌿 PARALYZED 2T!", new Color(0.35f, 0.90f, 0.40f));
                }
            }
            else if (act.Contains("Venom Thorn Volley"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                {
                    if (targetSlot.currentMonster.HasPassiveSubstr("Immunity to Poison") == null)
                        targetSlot.currentMonster.poisonDmg = Mathf.Max(targetSlot.currentMonster.poisonDmg, 6);
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 10 + bonusEff, m, opponent, player, false, showVfx);
                }
            }
            else if (act.Contains("Blighted Fang"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                {
                    targetSlot.currentMonster.weakenedTurns = Mathf.Max(targetSlot.currentMonster.weakenedTurns, 2);
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 12 + bonusEff, m, opponent, player, false, showVfx);
                }
            }
            else if (act.Contains("Nectar of Life"))
            {
                int healAmt = 28 + bonusEff;
                player.HealLeader(healAmt, opponent);
                player.shield += 10;
                m.HealDefense(10);
                if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, "", Color.green, true, healAmt);
            }
            // --- LIGHT ---
            else if (act.Contains("Grand Light Resurrection"))
            {
                CardData bestFallen = null;
                foreach (var g in player.graveyard)
                {
                    if (g != null && g.cardType == CardType.Monster && g.id < 9000)
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
                    revived.canAttack = false;
                    player.slots[emptySlot].PlaceMonster(revived);
                    if (showVfx) SpawnSlotStatusVfx(playerIsHuman, emptySlot, "✨ REVIVED!", new Color(1f, 0.95f, 0.55f));
                }
                else
                {
                    int fHeal = 24 + bonusEff;
                    player.HealLeader(fHeal, opponent);
                    if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, "", Color.green, true, fHeal);
                }
            }
            else if (act.Contains("Absolute Sun Judgment"))
            {
                int healAmt = 45 + bonusEff;
                player.HealLeader(healAmt, opponent);
                foreach (var al in allies) al.currentMonster.AddBonusDef(8);
                if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, "", Color.green, true, healAmt);
            }
            else if (act.Contains("Dawn Ray"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 14 + bonusEff, m, opponent, player, true, showVfx);
            }
            else if (act.Contains("Searing Judgment"))
            {
                player.HealLeader(8, opponent);
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 14 + bonusEff, m, opponent, player, true, showVfx);
            }
            else if (act.Contains("Blessing of Light"))
            {
                var target = GetAllyTarget(m, allies, player, true, explicitTargetSlot);
                int boost = Mathf.Max(8, target.baseAtk) + bonusEff;
                target.bonusAtk += boost;
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"✨ +{boost} ATK!", new Color(1f, 0.95f, 0.50f));
            }
            else if (act.Contains("Sanctified Aegis"))
            {
                var target = GetAllyTarget(m, allies, player, false, explicitTargetSlot);
                target.AddBonusDef(10 + bonusEff);
                target.bubbleShield = true;
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"🫧 +{10 + bonusEff} DEF AEGIS!", new Color(1f, 0.95f, 0.65f));
            }
            else if (act.Contains("Pack Alpha Roar"))
            {
                var target = GetAllyTarget(m, allies, player, true, explicitTargetSlot);
                int boost = 6 + bonusEff / 2;
                target.bonusAtk += boost;
                target.AddBonusDef(6);
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"🐺 +{boost} / +6!", new Color(0.95f, 0.80f, 0.35f));
            }
            else if (act.Contains("Arcane Infusion"))
            {
                var target = GetAllyTarget(m, allies, player, true, explicitTargetSlot);
                target.burnDmg = 0;
                target.poisonDmg = 0;
                target.frozenTurns = 0;
                target.weakenedTurns = 0;
                target.silencedTurns = 0;
                target.bonusAtk += 8 + bonusEff;
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, $"🔮 INFUSED +{8 + bonusEff}!", new Color(0.88f, 0.55f, 1f));
            }
            else if (act.Contains("Dwarven Forge Blessing"))
            {
                player.shield += 12 + bonusEff;
                foreach (var al in allies) al.currentMonster.AddBonusDef(6);
                if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, $"⚒️ +{12 + bonusEff} SHIELD!", new Color(0.90f, 0.72f, 0.40f));
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
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (targetSlot != null)
                {
                    player.HealLeader(5, opponent);
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 14 + bonusEff, m, opponent, player, false, showVfx);
                    if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, "", Color.green, true, 5);
                }
            }
            else if (act.Contains("Blood Eclipse Drain"))
            {
                player.HealLeader(7, opponent);
                m.HealDefense(3);
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 10 + bonusEff, m, opponent, player, false, showVfx);
                if (showVfx) SpawnLeaderStatusVfx(playerIsHuman, "", Color.green, true, 7);
            }
            else if (act.Contains("Soul Lock"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null && targetSlot.currentMonster.HasPassiveSubstr("Immunity to Electric") == null)
                {
                    targetSlot.currentMonster.silencedTurns = Mathf.Max(targetSlot.currentMonster.silencedTurns, 2);
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "🔒 SOUL LOCKED!", new Color(0.75f, 0.35f, 0.95f));
                }
            }
            // --- MECHA ---
            else if (act.Contains("Destructive Ion Cannon"))
            {
                int cannonDmg = 50 + bonusEff;
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
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
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 15 + bonusEff, m, opponent, player, true, showVfx);
            }
            else if (act.Contains("System Reboot"))
            {
                m.burnDmg = 0;
                m.poisonDmg = 0;
                m.frozenTurns = 0;
                m.silencedTurns = 0;
                m.weakenedTurns = 0;
                m.HealDefense(18 + bonusEff);
                m.bonusAtk += 5;
                m.bubbleShield = true;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"⚙️ REBOOT +5/+{18 + bonusEff}", new Color(0.75f, 0.85f, 0.95f));
            }
            // --- GHOSTS / DARK ---
            else if (act.Contains("Graveyard Summoning"))
            {
                var ghost = CreateTokenData(9999, "Dark", "Ghosts", "Attacker", 2, 2, 12, 14,
                    "Ethereal Body: 35% chance to evade normal monster strikes",
                    "Summoned Spectral Ghost (12 ATK, 14 DEF, Ethereal Body).");
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
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, false, explicitTargetSlot);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 10 + bonusEff, m, opponent, player, true, showVfx);
            }
            else if (act.Contains("Spectral Terror"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                {
                    targetSlot.currentMonster.frozenTurns = Mathf.Max(targetSlot.currentMonster.frozenTurns, 1);
                    targetSlot.currentMonster.canAttack = false;
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "👻 TERRIFIED!", new Color(0.75f, 0.50f, 0.95f));
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 8 + bonusEff, m, opponent, player, false, showVfx);
                }
            }
            // --- ARCANE ---
            else if (act.Contains("Grand Mana Blast"))
            {
                foreach (var es in enemies)
                    DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, 14 + bonusEff, m, opponent, player, true, showVfx);
                opponent.TakeLeaderDamage(10);
                if (showVfx) SpawnLeaderStatusVfx(oppIsHuman, "-10 HP!", new Color(0.95f, 0.45f, 0.95f));
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
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 15 + bonusEff, m, opponent, player, true, showVfx);
            }
            else if (act.Contains("Astral Bolt"))
            {
                player.shield += 8;
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 14 + bonusEff, m, opponent, player, true, showVfx);
            }
            else if (act.Contains("Elemental Shift"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                {
                    targetSlot.currentMonster.weakenedTurns = Mathf.Max(targetSlot.currentMonster.weakenedTurns, 2);
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "🔮 WEAKENED 2T!", new Color(0.95f, 0.50f, 0.95f));
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 10 + bonusEff, m, opponent, player, true, showVfx);
                }
            }
            else if (act.Contains("Quicksand Snare"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                {
                    targetSlot.currentMonster.silencedTurns = Mathf.Max(targetSlot.currentMonster.silencedTurns, 1);
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "🏜️ SLOWED!", new Color(0.92f, 0.70f, 0.15f));
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 8 + bonusEff, m, opponent, player, true, showVfx);
                }
            }
            else if (act.Contains("Dust Mirage"))
            {
                var target = GetAllyTarget(m, allies, player, false, explicitTargetSlot);
                int aSlot = target.currentSlot != null ? target.currentSlot.slotIndex : slot;
                target.AddBonusDef(10 + bonusEff);
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, aSlot, "🏜️ MIRAGE +30% EVADE", new Color(0.92f, 0.70f, 0.15f));
            }
            else if (act.Contains("Dune Blast"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 10 + bonusEff, m, opponent, player, true, showVfx);
            }
            else if (act.Contains("Desert Sandstorm"))
            {
                foreach (var eSlot in enemies)
                {
                    if (eSlot != null && eSlot.IsOccupied && eSlot.currentMonster != null && eSlot.currentMonster.IsAlive)
                        DealSpellOrSkillDamage(eSlot.slotIndex, eSlot.currentMonster, 12 + bonusEff, m, opponent, player, true, showVfx);
                }
            }
            else if (act.Contains("Quicksand Sinkhole"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                {
                    targetSlot.currentMonster.frozenTurns = Mathf.Max(targetSlot.currentMonster.frozenTurns, 1);
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "🏜️ TRAPPED!", new Color(0.92f, 0.70f, 0.15f));
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 14 + bonusEff, m, opponent, player, true, showVfx);
                }
            }
            else if (act.Contains("Mirage Shift"))
            {
                m.AddBonusDef(20 + bonusEff);
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, "🏜️ SHIFT +20 DEF", new Color(0.92f, 0.70f, 0.15f));
            }
            else if (act.Contains("Cataclysmic Sandstorm"))
            {
                foreach (var eSlot in enemies)
                {
                    if (eSlot != null && eSlot.IsOccupied && eSlot.currentMonster != null && eSlot.currentMonster.IsAlive)
                    {
                        eSlot.currentMonster.bonusAtk = Mathf.Max(0, eSlot.currentMonster.bonusAtk - 5);
                        DealSpellOrSkillDamage(eSlot.slotIndex, eSlot.currentMonster, 16 + bonusEff, m, opponent, player, true, showVfx);
                    }
                }
            }
            else if (act.Contains("Pharaoh's Doom Curse"))
            {
                var targetSlot = GetEnemyTarget(slot, enemies, opponent, true, explicitTargetSlot);
                if (targetSlot != null)
                {
                    targetSlot.currentMonster.silencedTurns = Mathf.Max(targetSlot.currentMonster.silencedTurns, 3);
                    if (showVfx) SpawnSlotStatusVfx(oppIsHuman, targetSlot.slotIndex, "🏜️ PHARAOH'S CURSE!", new Color(0.92f, 0.70f, 0.15f));
                    DealSpellOrSkillDamage(targetSlot.slotIndex, targetSlot.currentMonster, 22 + bonusEff, m, opponent, player, true, showVfx);
                }
            }
            else
            {
                m.AddBonusDef(15 + bonusEff);
                m.bonusAtk += 5;
                if (showVfx) SpawnSlotStatusVfx(playerIsHuman, slot, $"+5 ATK / +{15 + bonusEff} DEF", Color.cyan);
            }
        }

        public int ComputeAttackPower(int slot, MonsterInstance m, MonsterInstance oppM, EnvironmentCardData activeEnv, PlayerState opponent, PlayerState owner = null)
        {
            if (owner == null) CommanderSpiritEffects.TryResolveOwner(m, out owner, out _, out _);
            int atk = m.GetEffectiveAtk(activeEnv, owner);
            int lvlIdx = activeEnv != null ? Mathf.Clamp(activeEnv.level - 1, 0, 2) : 0;

            if ((slot == 0 || slot == 1 || slot == 4 || slot == 5) && opponent != null && opponent.slots != null)
            {
                for (int eIdx = 0; eIdx < 6; eIdx++)
                {
                    var es = opponent.slots[eIdx];
                    if (es != null && es.IsOccupied && es.currentMonster.IsAlive && es.currentMonster.HasPassiveSubstr("Polar Winds") != null)
                        atk = Mathf.Max(0, atk - 4);
                }
            }

            atk = Mathf.Max(0, atk - CommanderSpiritEffects.GetEnemyDreadAtkPenalty(opponent, m));

            bool hasGlacialCmd = false;
            if (owner != null && owner.slots != null)
            {
                foreach (var aslot in owner.slots)
                {
                    if (aslot != null && aslot.IsOccupied && aslot.currentMonster.IsAlive && aslot.currentMonster.HasPassiveSubstr("Glacial Command") != null)
                    {
                        hasGlacialCmd = true;
                        break;
                    }
                }
            }

            if (oppM != null && oppM.IsAlive)
            {
                if (oppM.HasPassiveSubstr("Chilling Gaze") != null) atk = Mathf.Max(0, atk - 1);
                if (oppM.HasPassiveSubstr("Light Chill") != null) atk = Mathf.Max(0, atk - 3);
                if (oppM.HasPassiveSubstr("Aura of Dread") != null && m.HasPassiveSubstr("Immunity to Dark") == null && m.HasPassiveSubstr("Sacred Light Ward") == null)
                    atk = Mathf.Max(0, atk - 4);
                if (oppM.HasPassiveSubstr("Water Submersion") != null && m.HasPassiveSubstr("Immunity to Water") == null)
                    atk = Mathf.Max(0, atk - 4);
                if (oppM.HasPassiveSubstr("Fierce Gale") != null) atk = Mathf.RoundToInt(atk * 0.75f);
                if (oppM.HasPassiveSubstr("Majestic Brightness") != null && UnityEngine.Random.value < 0.30f) atk = Mathf.RoundToInt(atk * 0.55f);

                if (m.HasPassiveSubstr("Vanguard Duelist") != null) atk += 5;
                if (m.HasPassiveSubstr("Ice Blade") != null && oppM.frozenTurns > 0) atk += 5;
                if (m.HasPassiveSubstr("Frostbite Claw") != null && (oppM.frozenTurns > 0 || oppM.weakenedTurns > 0)) atk += 3;
                if (hasGlacialCmd && m.data.element == "Ice" && oppM.frozenTurns > 0) atk += 4;
                if (IsEnvElement(activeEnv, "Ice") && m.data.element == "Ice" && (oppM.frozenTurns > 0 || oppM.weakenedTurns > 0))
                    atk += new[] { 3, 5, 7 }[lvlIdx];
                if (IsEnvElement(activeEnv, "Air") && oppM.data.element == "Air" && m.data.element != "Air" && atk > 0)
                    atk = Mathf.Max(1, atk - new[] { 1, 2, 3 }[lvlIdx]); // Crosswind penalty vs Air defenders
                if (m.HasPassiveSubstr("Boulder Bash") != null && (oppM.data.role == "Defender" || oppM.data.role == "Pure Shield")) atk += 3;
                if (m.HasPassiveSubstr("Sunlit Fang") != null && (oppM.data.element == "Dark" || oppM.data.tribe == "Ghosts")) atk += 3;
                if (m.HasPassiveSubstr("Valkyrie Charge") != null && oppM.data.cost > m.data.cost) atk += 3;
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
                // Instant match termination if either commander is dead
                if (!attacker.IsAlive || !defender.IsAlive || (GameManager.Instance != null && GameManager.Instance.currentPhase == GamePhase.GameOver))
                {
                    break;
                }

                var slot = attacker.slots[slotIdx];
                if (slot == null || !slot.IsOccupied || !slot.currentMonster.canAttack || slot.currentMonster.frozenTurns > 0)
                    continue;

                var attackerMonster = slot.currentMonster;
                int strikes = attackerMonster.doubleStrike ? 2 : 1;
                attackerMonster.doubleStrike = false;
                attackerMonster.canAttack = false;

                for (int st = 0; st < strikes; st++)
                {
                    if (!attacker.IsAlive || !defender.IsAlive || (GameManager.Instance != null && GameManager.Instance.currentPhase == GamePhase.GameOver))
                    {
                        break;
                    }
                    if (!attackerMonster.IsAlive) break;

                    var defenderSlot = defender.slots[slotIdx];
                    var oppM = (defenderSlot != null && defenderSlot.IsOccupied) ? defenderSlot.currentMonster : null;
                    int atkPower = ComputeAttackPower(slotIdx, attackerMonster, oppM, activeEnv, defender, attacker);
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
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayCardAttack();
                        yield return BattleAnimations.AnimateCardAttack(attackerSlotView.transform, targetPos, () =>
                        {
                            ApplyDamageImpact(attacker, attackerMonster, atkPower, slotIdx, defender, defenderSlot, defenderSlotView, defenderLeaderTransform, isDirectToLeader);
                        });
                        attackerSlotView.SetStandbyPaused(false);
                    }
                    else
                    {
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayCardAttack();
                        ApplyDamageImpact(attacker, attackerMonster, atkPower, slotIdx, defender, defenderSlot, defenderSlotView, defenderLeaderTransform, isDirectToLeader);
                    }

                    // Check instantly if the blow killed the leader
                    if (!attacker.IsAlive || !defender.IsAlive)
                    {
                        GameManager.Instance?.CheckGameOver();
                        break;
                    }

                    yield return new WaitForSeconds(0.28f);
                }

                if (!attacker.IsAlive || !defender.IsAlive || (GameManager.Instance != null && GameManager.Instance.currentPhase == GamePhase.GameOver))
                {
                    break;
                }
            }

            yield return new WaitForSeconds(0.20f);
            onComplete?.Invoke();
        }

        private void ApplyDamageImpact(PlayerState attacker, MonsterInstance attackerMonster, int atkPower, int slotIdx, PlayerState defender, BoardSlot defenderSlot, SlotView defenderSlotView, Transform defenderLeaderTransform, bool isDirectToLeader)
        {
            if (defender.teamImmuneTurns > 0) return;

            var activeEnv = GameManager.Instance != null ? GameManager.Instance.activeEnvironment : null;
            int lvlIdx = activeEnv != null ? Mathf.Clamp(activeEnv.level - 1, 0, 2) : 0;

            MonsterInstance defenderTargetM = (!isDirectToLeader && defenderSlot != null && defenderSlot.IsOccupied) ? defenderSlot.currentMonster : null;
            atkPower = CommanderSpiritEffects.ModifyOutgoingCombatAttack(attacker, attackerMonster, defenderTargetM, atkPower);

            bool cyclonePierce = false;
            if (attackerMonster.HasPassiveSubstr("Cyclone Dive") != null && !attackerMonster.cycloneDiveUsed)
            {
                attackerMonster.cycloneDiveUsed = true;
                cyclonePierce = true;
                atkPower += 6;
            }

            if (!isDirectToLeader && defenderSlot != null && defenderSlot.IsOccupied && defenderSlot.currentMonster.IsAlive)
            {
                var enemyMonster = defenderSlot.currentMonster;
                bool defIsHuman = GameManager.Instance != null && defender == GameManager.Instance.humanPlayer;

                if (enemyMonster.bubbleShield && !cyclonePierce)
                {
                    enemyMonster.bubbleShield = false;
                    MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                    SpawnSlotStatusVfx(defIsHuman, slotIdx, "🫧 BLOCKED!", new Color(0.45f, 0.90f, 1f));
                    return;
                }

                // Check Evasion & Air Env Evasion & Ethereal Body (35% evasion)
                if (attackerMonster.HasPassiveSubstr("Targeting System") == null && !cyclonePierce)
                {
                    if (enemyMonster.HasPassiveSubstr("Mist Veil") != null && UnityEngine.Random.value < 0.15f)
                    {
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                        SpawnSlotStatusVfx(defIsHuman, slotIdx, "💨 EVADED!", new Color(0.75f, 0.92f, 1f));
                        return;
                    }
                    if (enemyMonster.HasPassiveSubstr("Nimble Agility") != null && UnityEngine.Random.value < 0.15f)
                    {
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                        SpawnSlotStatusVfx(defIsHuman, slotIdx, "💨 EVADED!", new Color(0.75f, 0.92f, 1f));
                        return;
                    }
                    if (enemyMonster.HasPassiveSubstr("Light Evasion") != null && UnityEngine.Random.value < 0.20f)
                    {
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                        SpawnSlotStatusVfx(defIsHuman, slotIdx, "💨 EVADED!", new Color(0.75f, 0.92f, 1f));
                        return;
                    }
                    if (enemyMonster.HasPassiveSubstr("Skyborne Agility") != null &&
                        (slotIdx == 0 || slotIdx == 1 || slotIdx == 4 || slotIdx == 5) && UnityEngine.Random.value < 0.25f)
                    {
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                        SpawnSlotStatusVfx(defIsHuman, slotIdx, "💨 EVADED!", new Color(0.75f, 0.92f, 1f));
                        return;
                    }
                    if (enemyMonster.HasPassiveSubstr("Mystic Intuition") != null && UnityEngine.Random.value < 0.25f)
                    {
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                        SpawnSlotStatusVfx(defIsHuman, slotIdx, "💨 EVADED!", new Color(0.75f, 0.92f, 1f));
                        return;
                    }
                    if (enemyMonster.HasPassiveSubstr("Mystic Reflex") != null && UnityEngine.Random.value < 0.25f)
                    {
                        enemyMonster.bonusAtk += 2;
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                        SpawnSlotStatusVfx(defIsHuman, slotIdx, "💨 REFLEX +2 ATK!", new Color(0.75f, 0.92f, 1f));
                        return;
                    }
                    if (enemyMonster.HasPassiveSubstr("Phase Shift") != null && UnityEngine.Random.value < 0.30f)
                    {
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                        SpawnSlotStatusVfx(defIsHuman, slotIdx, "💨 PHASED!", new Color(0.75f, 0.92f, 1f));
                        return;
                    }
                    if (enemyMonster.HasPassiveSubstr("Ethereal Body") != null &&
                        attackerMonster.data.element != "Light" && attackerMonster.data.element != "Arcane" &&
                        attackerMonster.data.tribe != "Ghosts" && attackerMonster.HasPassiveSubstr("Phase Shift") == null &&
                        UnityEngine.Random.value < 0.35f)
                    {
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                        SpawnSlotStatusVfx(defIsHuman, slotIdx, "👻 ETHEREAL!", new Color(0.80f, 0.70f, 1f));
                        return;
                    }
                    if (IsEnvElement(activeEnv, "Air") && enemyMonster.data.element == "Air")
                    {
                        float airEv = new[] { 0.12f, 0.18f, 0.25f }[lvlIdx];
                        if (UnityEngine.Random.value < airEv)
                        {
                            MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                            SpawnSlotStatusVfx(defIsHuman, slotIdx, "🌪️ WIND EVADE!", new Color(0.65f, 1f, 0.85f));
                            return;
                        }
                    }
                    else if (IsEnvElement(activeEnv, "Dark") && enemyMonster.data.element == "Dark")
                    {
                        float darkEv = new[] { 0.08f, 0.12f, 0.18f }[lvlIdx];
                        if (UnityEngine.Random.value < darkEv)
                        {
                            MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                            SpawnSlotStatusVfx(defIsHuman, slotIdx, "🌑 SHADOW EVADE!", new Color(0.65f, 0.45f, 0.95f));
                            return;
                        }
                    }
                    float spiritEvade = CommanderSpiritEffects.GetCombatEvasionChance(defender, slotIdx, enemyMonster);
                    if (spiritEvade > 0f && UnityEngine.Random.value < spiritEvade)
                    {
                        MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                        SpawnSlotStatusVfx(defIsHuman, slotIdx, "💨 DODGE", new Color(0.75f, 0.92f, 1f));
                        return;
                    }
                }

                // Immunity / Resistance / Env Armor & Vuln / Flat Reductions
                atkPower = ApplyIncomingDefenseMods(enemyMonster, defender, attackerMonster, atkPower,
                    isCombat: true, isSpell: false, isSingleSpell: false, ignoreImmunity: cyclonePierce);
                if (activeEnv != null)
                {
                    if (IsEnvElement(activeEnv, "Ice") && enemyMonster.data.element == "Ice" && atkPower > 0)
                        atkPower = Mathf.Max(1, atkPower - new[] { 3, 5, 7 }[lvlIdx]);
                    else if (IsEnvElement(activeEnv, "Earth") && enemyMonster.data.element != "Earth" && atkPower > 0)
                        atkPower += new[] { 1, 2, 3 }[lvlIdx];
                }
                if (atkPower <= 0)
                {
                    MonsterCardGame.Audio.SoundManager.Instance?.PlayCardShieldBlock();
                    SpawnSlotStatusVfx(defIsHuman, slotIdx, "🛡️ IMMUNE!", new Color(0.75f, 0.85f, 1f));
                    return;
                }

                // Stone Roots flank bodyguard
                if ((slotIdx == 0 || slotIdx == 1 || slotIdx == 4 || slotIdx == 5) && atkPower > 0)
                {
                    foreach (int adj in new[] { slotIdx - 1, slotIdx + 1 })
                    {
                        if (adj >= 0 && adj < 6 && defender.slots[adj] != null && defender.slots[adj].IsOccupied && defender.slots[adj].currentMonster.IsAlive)
                        {
                            var bg = defender.slots[adj].currentMonster;
                            if (bg.HasPassiveSubstr("Stone Roots") != null && bg.currentDef > 8)
                            {
                                int absorbed = Mathf.Min(6, atkPower);
                                bg.TakeDamage(absorbed);
                                atkPower -= absorbed;
                                if (!bg.IsAlive)
                                    HandleMonsterDeath(defender, attacker, adj, bg, attackerMonster);
                                break;
                            }
                        }
                    }
                }

                int preDef = enemyMonster.currentDef;
                bool hadBubbleBeforeTake = enemyMonster.bubbleShield;
                if (cyclonePierce && hadBubbleBeforeTake)
                {
                    enemyMonster.bubbleShield = false;
                }
                int dealtM = (hadBubbleBeforeTake && !cyclonePierce) ? 0 : atkPower;
                if (hadBubbleBeforeTake && !cyclonePierce)
                {
                    enemyMonster.TakeDamage(atkPower);
                    return;
                }
                enemyMonster.TakeDamage(atkPower);
                if (MonsterCardGame.Audio.SoundManager.Instance != null)
                    MonsterCardGame.Audio.SoundManager.Instance.PlayCardHit();

                // V2.1 Armor Pierce / Bypass Passive + V2.3 Leader Continuous Pierce Aura
                int piercePct = V2CardMechanicsEngine.GetUnitPiercePct(attackerMonster, attacker);
                if (piercePct > 0 && dealtM > 0 && defender != null)
                {
                    int pierceDmg = Mathf.Max(1, V2CardMechanicsEngine.StrictRoundPct(dealtM, piercePct));
                    defender.TakeLeaderDamage(pierceDmg);
                    BattleUIManager.Instance?.LogCombatMessage($"🗡️ [{attackerMonster.data.name}] pierced {piercePct}% armor for {pierceDmg} Commander damage!");
                }

                // On-Hit Attacker Passives
                if (attackerMonster.HasPassiveSubstr("Ember Fangs") != null || attackerMonster.HasPassiveSubstr("Eternal Flame") != null)
                    attackerMonster.bonusAtk += 2;
                if (attackerMonster.HasPassiveSubstr("Life Leech") != null)
                    attackerMonster.HealDefense(Mathf.Max(2, Mathf.RoundToInt(dealtM * 0.20f)));
                if (attackerMonster.HasPassiveSubstr("Bloodscratch") != null)
                    attackerMonster.HealDefense(Mathf.Max(1, Mathf.RoundToInt(dealtM * 0.15f)));
                if (attackerMonster.HasPassiveSubstr("Briar Dart") != null && enemyMonster.HasPassiveSubstr("Immunity to Poison") == null)
                    enemyMonster.poisonDmg = Mathf.Max(enemyMonster.poisonDmg, 2);
                if (attackerMonster.HasPassiveSubstr("Slow Poison") != null && enemyMonster.HasPassiveSubstr("Immunity to Poison") == null)
                    enemyMonster.poisonDmg = Mathf.Max(enemyMonster.poisonDmg, 6);
                if (attackerMonster.HasPassiveSubstr("Venomous Fang") != null && enemyMonster.HasPassiveSubstr("Immunity to Poison") == null)
                    enemyMonster.poisonDmg += 4;
                if (IsEnvElement(activeEnv, "Fire") && attackerMonster.data.element == "Fire" &&
                    enemyMonster.HasPassiveSubstr("Immunity to Fire") == null &&
                    enemyMonster.HasPassiveSubstr("Moisture Shield") == null &&
                    enemyMonster.HasPassiveSubstr("Water Resilience") == null)
                {
                    enemyMonster.burnDmg = Mathf.Max(enemyMonster.burnDmg, new[] { 2, 3, 5 }[lvlIdx]);
                }
                else if (IsEnvElement(activeEnv, "Ice") && attackerMonster.data.element == "Ice" && enemyMonster.IsAlive)
                {
                    float frzCh = new[] { 0.20f, 0.30f, 0.45f }[lvlIdx];
                    if (UnityEngine.Random.value < frzCh)
                        enemyMonster.frozenTurns = Mathf.Max(enemyMonster.frozenTurns, 1);
                }
                else if (IsEnvElement(activeEnv, "Desert") && attackerMonster.data.element == "Desert" && enemyMonster.IsAlive)
                {
                    enemyMonster.bleedDmg += new[] { 2, 4, 6 }[lvlIdx];
                }
                else if (IsEnvElement(activeEnv, "Dark") && dealtM > 0)
                {
                    if (attackerMonster.data.element == "Dark")
                    {
                        int lhPct = new[] { 15, 25, 35 }[lvlIdx];
                        attacker.HealLeader(V2CardMechanicsEngine.StrictRoundPct(dealtM, lhPct), defender);
                    }
                    else if (attackerMonster.currentDef > 1)
                    {
                        int recoil = Mathf.Min(new[] { 1, 2, 3 }[lvlIdx], attackerMonster.currentDef - 1);
                        attackerMonster.currentDef -= recoil;
                    }
                }
                if (IsEnvElement(activeEnv, "Earth") && enemyMonster.data.element == "Earth" && attackerMonster.IsAlive)
                {
                    attackerMonster.TakeDamage(new[] { 3, 5, 8 }[lvlIdx]);
                }
                if (attackerMonster.HasPassiveSubstr("Psychic Drain") != null)
                {
                    int dealtPd = GetLeaderDmgDealt(defender, 4);
                    if (dealtPd > 0) attacker.HealLeader(dealtPd, defender);
                }

                CommanderSpiritEffects.OnCombatHitDealt(attacker, attackerMonster, defender, enemyMonster, atkPower);

                // Chain Lightning & Riptide Surge
                if (attackerMonster.HasPassiveSubstr("Chain Lightning") != null)
                {
                    int splash = Mathf.Max(3, Mathf.RoundToInt(atkPower * 0.30f));
                    foreach (int adj in new[] { slotIdx - 1, slotIdx + 1 })
                    {
                        if (adj >= 0 && adj < 6 && defender.slots[adj] != null && defender.slots[adj].IsOccupied && defender.slots[adj].currentMonster.IsAlive)
                        {
                            defender.slots[adj].currentMonster.TakeDamage(splash);
                            if (!defender.slots[adj].currentMonster.IsAlive)
                                HandleMonsterDeath(defender, attacker, adj, defender.slots[adj].currentMonster, attackerMonster);
                            break;
                        }
                    }
                }
                if (attackerMonster.HasPassiveSubstr("Riptide Surge") != null)
                {
                    foreach (int adj in new[] { slotIdx - 1, slotIdx + 1 })
                    {
                        if (adj >= 0 && adj < 6 && defender.slots[adj] != null && defender.slots[adj].IsOccupied && defender.slots[adj].currentMonster.IsAlive)
                        {
                            var adjM = defender.slots[adj].currentMonster;
                            if (adjM.HasPassiveSubstr("Immunity to Water") == null)
                            {
                                adjM.TakeDamage(4);
                                if (!adjM.IsAlive)
                                    HandleMonsterDeath(defender, attacker, adj, adjM, attackerMonster);
                            }
                        }
                    }
                }

                // Defensive Counter-Passives
                if (enemyMonster.HasPassiveSubstr("Static Discharge") != null && attackerMonster.IsAlive && attackerMonster.HasPassiveSubstr("Immunity to Electric") == null)
                    attackerMonster.silencedTurns = Mathf.Max(attackerMonster.silencedTurns, 1);
                if (enemyMonster.HasPassiveSubstr("Static Stinger") != null)
                    attackerMonster.TakeDamage(2);
                if (enemyMonster.HasPassiveSubstr("Poison Thorns") != null)
                {
                    attackerMonster.TakeDamage(3);
                    if (attackerMonster.IsAlive && attackerMonster.HasPassiveSubstr("Immunity to Poison") == null)
                        attackerMonster.poisonDmg = Mathf.Max(attackerMonster.poisonDmg, 2);
                }
                if (enemyMonster.HasPassiveSubstr("Backlash Spark") != null)
                {
                    attackerMonster.TakeDamage(3);
                    if (enemyMonster.IsAlive) enemyMonster.bonusAtk += 1;
                }
                if (enemyMonster.HasPassiveSubstr("Flint Skin") != null)
                    attackerMonster.TakeDamage(5);
                if (enemyMonster.HasPassiveSubstr("Permafrost Spike") != null)
                {
                    attackerMonster.TakeDamage(4);
                    if (attackerMonster.IsAlive && attackerMonster.HasPassiveSubstr("Immunity to Ice") == null)
                        attackerMonster.bonusAtk = Mathf.Max(-attackerMonster.baseAtk + 1, attackerMonster.bonusAtk - 2);
                }
                if (enemyMonster.HasPassiveSubstr("Magma Core") != null)
                {
                    attackerMonster.TakeDamage(4);
                    if (enemyMonster.IsAlive) enemyMonster.AddBonusDef(2);
                }
                if (enemyMonster.HasPassiveSubstr("Protective Flame") != null)
                    attackerMonster.TakeDamage(2);
                if (enemyMonster.HasPassiveSubstr("Burning Embers") != null)
                    attackerMonster.TakeDamage(Mathf.Max(2, Mathf.RoundToInt(atkPower * 0.25f)));
                if (enemyMonster.HasPassiveSubstr("Auto-Freeze Attacker") != null && attackerMonster.IsAlive && attackerMonster.HasPassiveSubstr("Immunity to Ice") == null)
                    attackerMonster.frozenTurns = Mathf.Max(attackerMonster.frozenTurns, 1);

                if (defenderSlotView != null && BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.TriggerHitShake(defenderSlotView.transform, Color.red);
                    BattleUIManager.Instance.SpawnDamageNumber(defenderSlotView.transform.position, atkPower, false);
                }

                if (!enemyMonster.IsAlive)
                {
                    if (attackerMonster.HasPassiveSubstr("Colossus Trample") != null && dealtM > preDef)
                    {
                        defender.TakeLeaderDamage(dealtM - preDef);
                    }
                    HandleMonsterDeath(defender, attacker, slotIdx, enemyMonster, attackerMonster);
                }

                if (!attackerMonster.IsAlive)
                {
                    HandleMonsterDeath(attacker, defender, slotIdx, attackerMonster, enemyMonster);
                }
            }
            else
            {
                if (attackerMonster.HasPassiveSubstr("Volt Dash") != null) atkPower += 2;
                if (attackerMonster.HasPassiveSubstr("Ember Fangs") != null || attackerMonster.HasPassiveSubstr("Eternal Flame") != null)
                    attackerMonster.bonusAtk += 2;

                int dealtDirect = GetLeaderDmgDealt(defender, atkPower);
                if (dealtDirect > 0)
                    V2CardMechanicsEngine.OnLeaderCombatHitDealt(attacker, attackerMonster, defender, null, dealtDirect);
                if (attackerMonster.HasPassiveSubstr("Psychic Drain") != null)
                {
                    int dPd = GetLeaderDmgDealt(defender, 4);
                    if (dPd > 0) attacker.HealLeader(dPd, defender);
                }
                if (attackerMonster.HasPassiveSubstr("Mana Steal") != null && dealtDirect > 0)
                {
                    attacker.currentMana += 1;
                }
                if (dealtDirect > 0 && attackerMonster.silencedTurns <= 0 &&
                    V2CardMechanicsEngine.UnitHasPassiveKind(attackerMonster, "steal_aether_face") &&
                    defender.power > 0)
                {
                    defender.power = Mathf.Max(0, defender.power - 1);
                    int capAether = V2CardMechanicsEngine.GetPlayerManaCap(attacker);
                    if (attacker.power < capAether) attacker.power += 1;
                    bool atkIsHuman = GameManager.Instance != null && (attacker == GameManager.Instance.humanPlayer);
                    SpawnLeaderStatusVfx(atkIsHuman, "⚡ +1 AETHER STOLEN!", new Color(0.45f, 0.90f, 1f));
                    SpawnLeaderStatusVfx(!atkIsHuman, "⚡ -1 AETHER!", new Color(1f, 0.35f, 0.35f));
                }

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

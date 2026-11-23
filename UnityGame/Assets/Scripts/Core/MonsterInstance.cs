using System;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Core
{
    public class MonsterInstance
    {
        public const int MaxPermBonusAtk = 10;
        public const int MaxPermBonusDef = 15;

        public CardData data;
        public int currentDef;
        public int baseAtk;
        public int baseDef;
        public int bonusAtk;
        public int permBonusDef;
        public int leaderDefApplied = 0;
        public int covDefApplied = 0;
        public bool covTauntApplied = false;
        public int summonedTurn;
        public bool canAttack;
        public BoardSlot currentSlot;

        // Tactical status effects (Synced 100% with the V2 1,470-Card Constitution)
        public int prevTurnDef = 0;
        public List<string> passives = new List<string>();
        public List<string> actives = new List<string>();
        public List<bool> activeUsedThisTurn = new List<bool>();
        public List<int> activeUseCountsThisTurn = new List<int>();
        public bool hasUsedActiveThisTurn = false;
        public int skillsUsedThisTurn = 0;
        public bool doubleSkillOneTurn = false;
        public int frozenTurns = 0;
        public int silencedTurns = 0;
        public int rootedTurns = 0;
        public int burnDmg = 0;
        public int poisonDmg = 0;
        public int bleedDmg = 0;
        public int weakenedTurns = 0;
        public int weakenedAtkFlat = 0;
        public int ascendTurns = 0;
        public bool ascendTriggered = false;
        public bool salvationUsed = false;
        public bool bubbleShield = false; // Deprecated by V2 Constitution (all defense is explicit +DEF)
        public bool doubleStrike = false;
        public int tempTurnAtk = 0;
        public bool soulPhylacteryUsed = false;
        public bool cycloneDiveUsed = false;

        public bool IsAlive => currentDef > 0;
        public bool IsBarrier => data != null && (data.is_barrier ||
            string.Equals(data.role, "Barrier", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(data.role, "Pure Shield", StringComparison.OrdinalIgnoreCase));
        public bool IsAllAttack => data != null && (data.is_all_attack || HasPassiveSubstr("All-Attack") != null);
        public bool IsLocked => rootedTurns > 0 || HasPassiveSubstr("Tectonic Anchor") != null || HasPassiveSubstr("Locked") != null ||
            (data != null && GameManager.Instance != null && GameManager.Instance.activeEnvironment != null &&
             EnvironmentCardData.AreSameElement(GameManager.Instance.activeEnvironment.element, "Earth") &&
             !EnvironmentCardData.AreSameElement(data.element, "Earth"));
        public bool HasActiveSkill => !IsBarrier && actives != null && actives.Count > 0;
        public int ActiveSkillCount => (!IsBarrier && actives != null) ? actives.Count : 0;

        public bool HasActiveSkillAt(int skillIndex)
        {
            return actives != null && skillIndex >= 0 && skillIndex < actives.Count && !string.IsNullOrEmpty(actives[skillIndex]);
        }

        public int GetMaxUsesPerSkill(PlayerState owner = null)
        {
            if (doubleSkillOneTurn) return 2;
            if (owner != null && V2CardMechanicsEngine.IsCovenantActiveForPlayer(owner))
            {
                var cov = V2CardMechanicsEngine.GetCovenantOps(owner.activeCovenant);
                if (cov != null && cov.rew_double_skill) return 2;
            }
            return 1;
        }

        public int GetActiveSkillUses(int skillIndex)
        {
            if (activeUseCountsThisTurn != null && skillIndex >= 0 && skillIndex < activeUseCountsThisTurn.Count)
                return activeUseCountsThisTurn[skillIndex];
            if (activeUsedThisTurn != null && skillIndex >= 0 && skillIndex < activeUsedThisTurn.Count)
                return activeUsedThisTurn[skillIndex] ? 1 : 0;
            return 0;
        }

        public bool IsActiveSkillUsed(int skillIndex, PlayerState owner = null)
        {
            if (IsBarrier || !HasActiveSkillAt(skillIndex)) return true;
            int maxPerSkill = GetMaxUsesPerSkill(owner);
            return GetActiveSkillUses(skillIndex) >= maxPerSkill;
        }

        public bool AreAllActiveSkillsUsed(PlayerState owner = null)
        {
            if (!HasActiveSkill) return true;
            for (int i = 0; i < ActiveSkillCount; i++)
            {
                if (!IsActiveSkillUsed(i, owner)) return false;
            }
            return true;
        }

        public void MarkActiveSkillUsed(int skillIndex, PlayerState owner = null)
        {
            if (skillIndex < 0) return;
            skillsUsedThisTurn++;
            int maxPerSkill = GetMaxUsesPerSkill(owner);
            if (activeUsedThisTurn == null) activeUsedThisTurn = new List<bool>();
            if (activeUseCountsThisTurn == null) activeUseCountsThisTurn = new List<int>();
            int count = actives != null ? actives.Count : 0;
            int targetLen = Mathf.Max(count, skillIndex + 1);
            while (activeUsedThisTurn.Count < targetLen)
                activeUsedThisTurn.Add(false);
            while (activeUseCountsThisTurn.Count < targetLen)
                activeUseCountsThisTurn.Add(0);

            activeUseCountsThisTurn[skillIndex]++;
            activeUsedThisTurn[skillIndex] = (activeUseCountsThisTurn[skillIndex] >= maxPerSkill);
            hasUsedActiveThisTurn = (skillsUsedThisTurn > 0);
        }

        public void ResetActiveSkillsUsage()
        {
            hasUsedActiveThisTurn = false;
            skillsUsedThisTurn = 0;
            if (activeUsedThisTurn != null)
            {
                for (int i = 0; i < activeUsedThisTurn.Count; i++)
                    activeUsedThisTurn[i] = false;
            }
            if (activeUseCountsThisTurn != null)
            {
                for (int i = 0; i < activeUseCountsThisTurn.Count; i++)
                    activeUseCountsThisTurn[i] = 0;
            }
        }

        public MonsterInstance(CardData monsterData, int currentTurn)
        {
            data = monsterData;
            baseAtk = (monsterData != null && monsterData.is_barrier) ? 0 : (monsterData != null ? monsterData.atk : 0);
            baseDef = monsterData != null ? Mathf.Max(1, monsterData.def) : 1;
            currentDef = baseDef;
            prevTurnDef = baseDef;
            bonusAtk = 0;
            permBonusDef = 0;
            summonedTurn = currentTurn;
            canAttack = false;

            if (monsterData.passives != null && monsterData.passives.Count > 0)
            {
                passives = new List<string>(monsterData.passives);
            }
            else if (!string.IsNullOrEmpty(monsterData.passives_text) && !monsterData.passives_text.StartsWith("None"))
            {
                foreach (var p in monsterData.passives_text.Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string clean = p.Trim();
                    if (clean.StartsWith("1. ") || clean.StartsWith("2. ")) clean = clean.Substring(3).Trim();
                    if (!string.IsNullOrEmpty(clean) && !clean.StartsWith("None")) passives.Add(clean);
                }
            }

            if (!IsBarrier)
            {
                if (monsterData.active_skills != null && monsterData.active_skills.Count > 0)
                {
                    actives = new List<string>(monsterData.active_skills);
                }
                else if (!string.IsNullOrEmpty(monsterData.active_skill) && !monsterData.active_skill.StartsWith("None"))
                {
                    foreach (var a in monsterData.active_skill.Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string clean = a.Trim();
                        if (clean.StartsWith("1. ") || clean.StartsWith("2. ")) clean = clean.Substring(3).Trim();
                        if (!string.IsNullOrEmpty(clean) && !clean.StartsWith("None")) actives.Add(clean);
                    }
                }
            }

            activeUsedThisTurn = new List<bool>();
            activeUseCountsThisTurn = new List<int>();
            for (int i = 0; i < actives.Count; i++)
            {
                activeUsedThisTurn.Add(false);
                activeUseCountsThisTurn.Add(0);
            }
        }

        public string HasPassiveSubstr(string substr)
        {
            if (silencedTurns > 0 || passives == null) return null;
            foreach (var p in passives)
            {
                if (!string.IsNullOrEmpty(p) && p.Contains(substr))
                    return p;
            }
            return null;
        }

        public bool HasEarthSteadfastness(EnvironmentCardData activeEnv)
        {
            if (HasPassiveSubstr("Earth Steadfastness") != null) return true;
            return activeEnv != null &&
                   EnvironmentCardData.AreSameElement(activeEnv.buffedElement, "Earth") &&
                   EnvironmentCardData.AreSameElement(data.element, "Earth");
        }

        public int GetEffectiveAtk(EnvironmentCardData activeEnv) => GetEffectiveAtk(activeEnv, null);

        public int GetEffectiveAtk(EnvironmentCardData activeEnv, PlayerState owner)
        {
            // V2 Constitution Rule #31: Barrier cards are strictly locked at 0 ATK forever!
            if (IsBarrier) return 0;

            // V2 Constitution Rule #35: All-Attack monsters cannot gain ATK from any buff or aura
            if (IsAllAttack)
            {
                int lockedAtk = Mathf.Max(0, baseAtk - weakenedAtkFlat);
                if (weakenedTurns > 0) lockedAtk = Mathf.RoundToInt(lockedAtk * 0.70f);
                return Mathf.Clamp(lockedAtk, 0, baseAtk);
            }

            // V2 Constitution Rule #27: Permanent cumulative ATK bonus is capped at +10
            int clampedBonusAtk = Mathf.Clamp(bonusAtk, -99, MaxPermBonusAtk);
            int totalAtk = baseAtk + clampedBonusAtk + tempTurnAtk - weakenedAtkFlat;

            int slotIdx = currentSlot != null ? currentSlot.slotIndex : -1;

            if (activeEnv != null)
            {
                int lvlIdx = Mathf.Clamp(activeEnv.level - 1, 0, 2);
                bool matchesEnvElem = EnvironmentCardData.AreSameElement(activeEnv.element, data.element);
                if (matchesEnvElem && EnvironmentCardData.AreSameElement(activeEnv.element, "Fire"))
                {
                    // Fire Arena: +3/+5/+8 ATK for Fire monsters ONLY
                    totalAtk += new[] { 3, 5, 8 }[lvlIdx];
                }
                else if (!matchesEnvElem && totalAtk > 0 &&
                         (EnvironmentCardData.AreSameElement(activeEnv.element, "Water") ||
                          EnvironmentCardData.AreSameElement(activeEnv.element, "Ice")))
                {
                    // Water / Ice Arena: mild -1/-2/-3 ATK drag/chill on Foreign monsters (min 1)
                    totalAtk = Mathf.Max(1, totalAtk - new[] { 1, 2, 3 }[lvlIdx]);
                }
            }
            if (owner == null && CommanderSpiritEffects.TryResolveOwner(this, out PlayerState resolvedOwner, out _, out _))
            {
                owner = resolvedOwner;
            }
            if (HasPassiveSubstr("Skyborne Agility") != null && (slotIdx == 0 || slotIdx == 1 || slotIdx == 4 || slotIdx == 5))
            {
                totalAtk += 3;
            }
            if (HasPassiveSubstr("Riptide Pounce") != null && (slotIdx == 2 || slotIdx == 3))
            {
                totalAtk += 2;
            }
            if (HasPassiveSubstr("Zephyr Talon") != null && (slotIdx == 0 || slotIdx == 1 || slotIdx == 4 || slotIdx == 5))
            {
                totalAtk += 2;
            }
            if (owner != null && owner.slots != null)
            {
                if (HasPassiveSubstr("Infernal Pack") != null)
                {
                    int packCount = 0;
                    foreach (var s in owner.slots)
                    {
                        if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster != this && s.currentMonster.IsAlive &&
                            (string.Equals(s.currentMonster.data.tribe, "Beasts", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(s.currentMonster.data.tribe, "Demons", StringComparison.OrdinalIgnoreCase)))
                        {
                            packCount++;
                        }
                    }
                    totalAtk += 3 * packCount;
                }
                if (HasPassiveSubstr("Chitin Swarm") != null || HasPassiveSubstr("Swarm Synergy") != null)
                {
                    int swarmCount = 0;
                    foreach (var s in owner.slots)
                    {
                        if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster != this && s.currentMonster.IsAlive)
                        {
                            swarmCount++;
                        }
                    }
                    totalAtk += 2 * swarmCount;
                }
                if (string.Equals(data.tribe, "Mages", StringComparison.OrdinalIgnoreCase))
                {
                    bool hasAstralTwin = false;
                    foreach (var s in owner.slots)
                    {
                        if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive &&
                            s.currentMonster.HasPassiveSubstr("Astral Twin") != null)
                        {
                            hasAstralTwin = true;
                            break;
                        }
                    }
                    if (hasAstralTwin) totalAtk += 4;
                }
            }
            if (weakenedTurns > 0)
            {
                totalAtk = Mathf.RoundToInt(totalAtk * 0.70f);
            }
            if (HasPassiveSubstr("Targeting System") != null)
            {
                totalAtk += Mathf.Max(2, Mathf.RoundToInt(totalAtk * 0.15f));
            }
            totalAtk += CommanderSpiritEffects.GetContinuousAtkBonusForInstance(this);
            if (owner != null)
            {
                totalAtk += V2CardMechanicsEngine.GetUnitAuraAndCovenantAtkDelta(this, owner);
            }
            return Mathf.Max(0, totalAtk);
        }

        public float GetEnvironmentModifier(EnvironmentCardData activeEnv)
        {
            if (activeEnv == null) return 1.0f;
            return activeEnv.GetModifier(data.element);
        }

        public void TakeDamage(int damage)
        {
            if (damage <= 0) return;
            // V2 Constitution Rule #20: Bubble Shield & Digital Armor are eliminated; all protection is explicit +DEF
            bubbleShield = false;
            currentDef = Mathf.Max(0, currentDef - damage);
            Debug.Log($"[{data.name}] took {damage} damage. Remaining DEF: {currentDef}");
        }

        public void HealDefense(int amount)
        {
            if (amount <= 0) return;
            int maxCap = baseDef + MaxPermBonusDef;
            currentDef = Mathf.Min(maxCap, currentDef + amount);
        }

        public void AddBonusDef(int amount)
        {
            if (amount <= 0) return;
            int allowed = Mathf.Min(amount, Mathf.Max(0, MaxPermBonusDef - permBonusDef));
            if (allowed > 0)
            {
                permBonusDef += allowed;
                baseDef += allowed;
                currentDef += allowed;
            }
            else
            {
                HealDefense(amount);
            }
        }

        public void ApplyPermBuff(int atkDelta, int defDelta)
        {
            if (!IsBarrier && !IsAllAttack && atkDelta > 0)
            {
                bonusAtk = Mathf.Min(MaxPermBonusAtk, bonusAtk + atkDelta);
            }
            if (defDelta > 0)
            {
                AddBonusDef(defDelta);
            }
        }

        public void ResetTurnReadiness(int currentTurn)
        {
            if (IsAlive)
            {
                prevTurnDef = currentDef;
                ResetActiveSkillsUsage();
                doubleSkillOneTurn = false; // V2 Constitution Rule #36: Double Skill lasts 1 turn only!
                tempTurnAtk = 0;
                cycloneDiveUsed = false;
                doubleStrike = HasPassiveSubstr("Twin Strike") != null;
                if (rootedTurns > 0) rootedTurns--;
                if (!IsBarrier && currentTurn > summonedTurn && frozenTurns <= 0)
                {
                    canAttack = true;
                }
                else if (IsBarrier)
                {
                    canAttack = false;
                }
            }
        }
    }
}

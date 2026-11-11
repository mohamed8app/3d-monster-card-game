using System;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Core
{
    public class MonsterInstance
    {
        public CardData data;
        public int currentDef;
        public int baseAtk;
        public int baseDef;
        public int bonusAtk;
        public int summonedTurn;
        public bool canAttack;
        public BoardSlot currentSlot;

        // Tactical status effects (Synced 100% with the 100,000-match simulator)
        public List<string> passives = new List<string>();
        public List<string> actives = new List<string>();
        public bool hasUsedActiveThisTurn = false;
        public int frozenTurns = 0;
        public int silencedTurns = 0;
        public int burnDmg = 0;
        public int poisonDmg = 0;
        public int weakenedTurns = 0;
        public bool salvationUsed = false;
        public bool bubbleShield = false;
        public bool doubleStrike = false;

        public bool IsAlive => currentDef > 0;
        public bool HasActiveSkill => actives != null && actives.Count > 0;

        public MonsterInstance(CardData monsterData, int currentTurn)
        {
            data = monsterData;
            baseAtk = monsterData.atk;
            baseDef = monsterData.def;
            currentDef = monsterData.def;
            bonusAtk = 0;
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

        public int GetEffectiveAtk(EnvironmentCardData activeEnv)
        {
            int totalAtk = baseAtk + bonusAtk;
            if (activeEnv != null)
            {
                float mod = activeEnv.GetModifier(data.element);
                totalAtk = Mathf.Max(0, Mathf.RoundToInt(totalAtk * mod));
            }
            if (weakenedTurns > 0)
            {
                totalAtk = Mathf.RoundToInt(totalAtk * 0.70f);
            }
            if (HasPassiveSubstr("Targeting System") != null)
            {
                totalAtk += Mathf.Max(2, Mathf.RoundToInt(totalAtk * 0.15f));
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
            if (bubbleShield)
            {
                bubbleShield = false;
                Debug.Log($"🫧 [{data.name}] completely absorbed the strike via Safety Bubble!");
                return;
            }
            currentDef = Mathf.Max(0, currentDef - damage);
            Debug.Log($"[{data.name}] took {damage} damage. Remaining DEF: {currentDef}");
        }

        public void HealDefense(int amount)
        {
            currentDef = Mathf.Min(baseDef + 20, currentDef + amount);
        }

        public void AddBonusDef(int amount)
        {
            baseDef += amount;
            currentDef += amount;
        }

        public void ResetTurnReadiness(int currentTurn)
        {
            if (IsAlive)
            {
                hasUsedActiveThisTurn = false;
                if (currentTurn > summonedTurn && frozenTurns <= 0)
                {
                    canAttack = true;
                }
            }
        }
    }
}

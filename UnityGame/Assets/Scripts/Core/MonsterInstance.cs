using System;
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
        public int summonedTurn;
        public bool canAttack;
        public BoardSlot currentSlot;

        public bool IsAlive => currentDef > 0;

        public MonsterInstance(CardData monsterData, int currentTurn)
        {
            data = monsterData;
            baseAtk = monsterData.atk;
            baseDef = monsterData.def;
            currentDef = monsterData.def;
            summonedTurn = currentTurn;
            canAttack = false; // الوحوش في أول دور يتم استدعاؤها لا تهاجم حتى يمر عليها دور كامل
        }

        public int GetEffectiveAtk(EnvironmentCardData activeEnv)
        {
            if (activeEnv == null)
                return baseAtk;

            float mod = activeEnv.GetModifier(data.element);
            return Mathf.Max(0, Mathf.RoundToInt(baseAtk * mod));
        }

        public float GetEnvironmentModifier(EnvironmentCardData activeEnv)
        {
            if (activeEnv == null) return 1.0f;
            return activeEnv.GetModifier(data.element);
        }

        public void TakeDamage(int damage)
        {
            currentDef = Mathf.Max(0, currentDef - damage);
            Debug.Log($"[{data.name}] تلقى {damage} ضرر. الدفاع المتبقي: {currentDef}");
        }

        public void HealDefense(int amount)
        {
            currentDef = Mathf.Min(baseDef, currentDef + amount);
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
                // لا يهاجم إلا بعد ما يمر عليه دور كامل على الأقل من استدعائه
                if (currentTurn > summonedTurn)
                {
                    canAttack = true;
                }
            }
        }
    }
}

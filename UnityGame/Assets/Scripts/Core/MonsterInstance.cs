using System;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Core
{
    public class MonsterInstance
    {
        public MonsterData data;
        public int currentDef;
        public int baseAtk;
        public int baseDef;
        public int summonedTurn;
        public bool canAttack;
        public BoardSlot currentSlot;

        public bool IsAlive => currentDef > 0;

        public MonsterInstance(MonsterData monsterData, int currentTurn)
        {
            data = monsterData;
            baseAtk = monsterData.atk;
            baseDef = monsterData.def;
            currentDef = monsterData.def;
            summonedTurn = currentTurn;
            canAttack = true; // جاهز للهجوم فور نزوله للساحة
        }

        public int GetEffectiveAtk(EnvironmentCardData activeEnv)
        {
            if (activeEnv == null)
                return baseAtk;

            float mod = activeEnv.GetModifier(data.element);
            return Mathf.Max(0, Mathf.RoundToInt(baseAtk * mod));
        }

        public void TakeDamage(int damage)
        {
            currentDef = Mathf.Max(0, currentDef - damage);
            Debug.Log($"[{data.name}] تلقى {damage} ضرر. الدفاع المتبقي: {currentDef}");
        }

        public void ResetTurnReadiness(int currentTurn)
        {
            if (IsAlive)
            {
                canAttack = true;
            }
        }
    }
}

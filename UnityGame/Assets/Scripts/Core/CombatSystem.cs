using System;
using UnityEngine;
using MonsterCardGame.Data;

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

        /// <summary>
        /// تنفيذ مرحلة الهجوم في اتجاه واحد (One-Way Combat Phase)
        /// وحوش اللاعب النشط فقط تهاجم، والخصم يمتص الضرر بدفاعه
        /// </summary>
        public void ExecuteCombatPhase(PlayerState attacker, PlayerState defender, EnvironmentCardData activeEnv)
        {
            Debug.Log($"⚔️ بدأ هجوم جيش [{attacker.playerName}] نحو خطوط دفاع [{defender.playerName}]...");

            // 1. هجوم الأجنحة (0، 1، 4، 5)
            int[] wingSlots = { 0, 1, 4, 5 };
            foreach (int slotIdx in wingSlots)
            {
                var slot = attacker.slots[slotIdx];
                if (slot != null && slot.IsOccupied && slot.currentMonster.canAttack)
                {
                    ExecuteWingAttack(slot.currentMonster, slotIdx, defender, activeEnv);
                }
            }

            // 2. هجوم الوسط (2، 3)
            int[] centerSlots = { 2, 3 };
            foreach (int slotIdx in centerSlots)
            {
                var slot = attacker.slots[slotIdx];
                if (slot != null && slot.IsOccupied && slot.currentMonster.canAttack)
                {
                    ExecuteCenterAttack(slot.currentMonster, slotIdx, defender, activeEnv);
                }
            }
        }

        private void ExecuteWingAttack(MonsterInstance attackerMonster, int slotIdx, PlayerState defender, EnvironmentCardData activeEnv)
        {
            int atkPower = attackerMonster.GetEffectiveAtk(activeEnv);
            if (atkPower <= 0) return;

            var targetSlot = defender.slots[slotIdx];

            if (targetSlot != null && targetSlot.IsOccupied)
            {
                // الخانة المقابلة فيها وحش: ضرب الوحش المقابل
                var enemyMonster = targetSlot.currentMonster;
                enemyMonster.TakeDamage(atkPower);

                if (!enemyMonster.IsAlive)
                {
                    Debug.Log($"💀 تم تدمير وحش الخصم بالجناح [{enemyMonster.data.name}]!");
                    defender.graveyard.Add(enemyMonster.data);
                    targetSlot.ClearSlot();
                }
            }
            else
            {
                // الجناح المقابل فارغ: ضربة مباشرة تخترق نحو القائد (1,000 HP)!
                Debug.Log($"🦅 الجناح {slotIdx} فارغ! وحش [{attackerMonster.data.name}] يوجه ضربة مباشرة للقائد بـ {atkPower} ضرر!");
                defender.TakeLeaderDamage(atkPower);
            }
        }

        private void ExecuteCenterAttack(MonsterInstance attackerMonster, int slotIdx, PlayerState defender, EnvironmentCardData activeEnv)
        {
            int atkPower = attackerMonster.GetEffectiveAtk(activeEnv);
            if (atkPower <= 0) return;

            var targetSlot = defender.slots[slotIdx];

            if (targetSlot != null && targetSlot.IsOccupied)
            {
                // وسط الخصم مشغول: ضرب الوحش المقابل
                var enemyMonster = targetSlot.currentMonster;
                enemyMonster.TakeDamage(atkPower);

                if (!enemyMonster.IsAlive)
                {
                    Debug.Log($"💀 تم تدمير وحش وسط الخصم [{enemyMonster.data.name}]!");
                    defender.graveyard.Add(enemyMonster.data);
                    targetSlot.ClearSlot();
                }
            }
            else
            {
                // وسط الخصم فارغ (تم إلغاء المدافع الخلفي): ضرب القائد مباشرة!
                Debug.Log($"🎯 خط الوسط {slotIdx} فارغ! وحش [{attackerMonster.data.name}] يضرب القائد مباشرة بـ {atkPower} ضرر!");
                defender.TakeLeaderDamage(atkPower);
            }
        }
    }
}

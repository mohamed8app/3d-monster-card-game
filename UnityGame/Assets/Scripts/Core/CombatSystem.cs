using System;
using System.Collections;
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

        /// <summary>
        /// تنفيذ مرحلة الهجوم مع حركات الاندفاع والاهتزاز وتطاير أرقام الضرر (Animated Combat Phase)
        /// </summary>
        public void ExecuteCombatPhase(PlayerState attacker, PlayerState defender, EnvironmentCardData activeEnv)
        {
            StartCoroutine(ExecuteCombatPhaseAnimated(attacker, defender, activeEnv, null));
        }

        public IEnumerator ExecuteCombatPhaseAnimated(PlayerState attacker, PlayerState defender, EnvironmentCardData activeEnv, Action onComplete)
        {
            bool isAttackerPlayer = (attacker == GameManager.Instance.humanPlayer);
            string sideName = isAttackerPlayer ? "جيشك" : "جيش الخصم";
            Debug.Log($"⚔️ بدأ هجوم [{sideName}] الميداني بالرسوم المتحركة!");

            int[] attackSlots = { 0, 1, 2, 3, 4, 5 };

            foreach (int slotIdx in attackSlots)
            {
                var slot = attacker.slots[slotIdx];
                if (slot == null || !slot.IsOccupied || !slot.currentMonster.canAttack)
                    continue;

                var attackerMonster = slot.currentMonster;
                int atkPower = attackerMonster.GetEffectiveAtk(activeEnv);
                if (atkPower <= 0) continue;

                var attackerSlotView = BattleUIManager.Instance != null ? BattleUIManager.Instance.GetSlotView(slotIdx, isAttackerPlayer) : null;
                var defenderSlot = defender.slots[slotIdx];
                var defenderSlotView = BattleUIManager.Instance != null ? BattleUIManager.Instance.GetSlotView(slotIdx, !isAttackerPlayer) : null;
                var defenderLeaderTransform = BattleUIManager.Instance != null ? BattleUIManager.Instance.GetLeaderTransform(!isAttackerPlayer) : null;

                bool isDirectToLeader = (defenderSlot == null || !defenderSlot.IsOccupied);
                Vector3 targetPos = (!isDirectToLeader && defenderSlotView != null)
                    ? defenderSlotView.transform.position
                    : (defenderLeaderTransform != null ? defenderLeaderTransform.position : Vector3.zero);

                // حركة اندفاع الكارت الهجومية نحو الهدف مع لحظة الاصطدام
                if (attackerSlotView != null)
                {
                    yield return BattleAnimations.AnimateCardAttack(attackerSlotView.transform, targetPos, () =>
                    {
                        ApplyDamageImpact(attackerMonster, atkPower, slotIdx, defender, defenderSlot, defenderSlotView, defenderLeaderTransform, isDirectToLeader);
                    });
                }
                else
                {
                    ApplyDamageImpact(attackerMonster, atkPower, slotIdx, defender, defenderSlot, defenderSlotView, defenderLeaderTransform, isDirectToLeader);
                }

                // انتظار بين كل ضربة ليعيش اللاعب تجربة تبادل الضربات
                yield return new WaitForSeconds(0.35f);
            }

            yield return new WaitForSeconds(0.25f);
            onComplete?.Invoke();
        }

        private void ApplyDamageImpact(MonsterInstance attackerMonster, int atkPower, int slotIdx, PlayerState defender, BoardSlot defenderSlot, SlotView defenderSlotView, Transform defenderLeaderTransform, bool isDirectToLeader)
        {
            if (!isDirectToLeader)
            {
                var enemyMonster = defenderSlot.currentMonster;
                enemyMonster.TakeDamage(atkPower);

                if (defenderSlotView != null && BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.TriggerHitShake(defenderSlotView.transform, Color.red);
                    BattleUIManager.Instance.SpawnDamageNumber(defenderSlotView.transform.position, atkPower, false);
                }

                if (!enemyMonster.IsAlive)
                {
                    defender.graveyard.Add(enemyMonster.data);
                    defenderSlot.ClearSlot();
                }
            }
            else
            {
                defender.TakeLeaderDamage(atkPower);

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

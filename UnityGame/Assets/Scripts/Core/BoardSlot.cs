using System;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Core
{
    public enum SlotType
    {
        LeftWing,           // Slots 0, 1: Left Flank
        CenterFront,        // Slots 2, 3: Vanguard Center
        RightWing,          // Slots 4, 5: Right Flank
        BacklineGuardian    // Slot 6: Backline Guardian
    }

    public class BoardSlot : MonoBehaviour
    {
        [Header("Slot Configuration")]
        public int slotIndex; // 0 to 6
        public SlotType slotType;
        public bool isPlayerSide = true;

        [Header("State")]
        public MonsterInstance currentMonster;
        public bool IsOccupied => currentMonster != null && currentMonster.IsAlive;

        public void Initialize(int index, bool playerSide)
        {
            slotIndex = index;
            isPlayerSide = playerSide;

            if (index == 0 || index == 1)
                slotType = SlotType.LeftWing;
            else if (index == 2 || index == 3)
                slotType = SlotType.CenterFront;
            else if (index == 4 || index == 5)
                slotType = SlotType.RightWing;
            else if (index == 6)
                slotType = SlotType.BacklineGuardian;
        }

        public bool PlaceMonster(MonsterInstance monster)
        {
            if (IsOccupied)
                return false;

            currentMonster = monster;
            currentMonster.currentSlot = this;
            return true;
        }

        public void ClearSlot()
        {
            if (currentMonster != null)
            {
                currentMonster.currentSlot = null;
                currentMonster = null;
            }
        }
    }
}

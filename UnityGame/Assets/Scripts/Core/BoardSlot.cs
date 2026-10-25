using System;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Core
{
    public enum SlotType
    {
        LeftWing,           // الخانات 0، 1: جناح أيسر
        CenterFront,        // الخانات 2، 3: وسط أمامي
        RightWing,          // الخانات 4، 5: جناح أيمن
        BacklineGuardian    // الخانة 6: مدافع خلفي بالوسط
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

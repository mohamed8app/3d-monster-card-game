using System;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Core
{
    public class PlayerState : MonoBehaviour
    {
        [Header("Leader Stats")]
        public string playerName = "اللاعب";
        public int currentHp = 500;
        public int maxHp = 500;

        [Header("Resources")]
        public int manaCapacity = 1;
        public int currentPower = 1;
        public const int MaxManaCap = 8;
        public bool hasSacrificedThisTurn = false;
        public int fatigueCount = 0;
        public int monstersSummonedTotal = 0;

        [Header("Card Collections")]
        public List<CardData> deck = new List<CardData>();
        public List<CardData> hand = new List<CardData>();
        public List<CardData> graveyard = new List<CardData>();

        [Header("6 Board Slots")]
        public BoardSlot[] slots = new BoardSlot[6];

        public bool IsAlive => currentHp > 0;

        public void InitializeDeck(List<CardData> initialDeck)
        {
            deck = new List<CardData>(initialDeck);
            ShuffleDeck();
            hand.Clear();
            graveyard.Clear();
            currentHp = maxHp;
            manaCapacity = 1;
            currentPower = 1;
            fatigueCount = 0;
            monstersSummonedTotal = 0;
            hasSacrificedThisTurn = false;
        }

        public void ShuffleDeck()
        {
            for (int i = 0; i < deck.Count; i++)
            {
                int rnd = UnityEngine.Random.Range(i, deck.Count);
                var temp = deck[i];
                deck[i] = deck[rnd];
                deck[rnd] = temp;
            }
        }

        public void StartTurn(int turnNumber)
        {
            // 1. إعادة شحن الباور ليساوي سعة المانا الحالية
            currentPower = manaCapacity;
            hasSacrificedThisTurn = false;

            // 2. سحب كارت من المجموعة
            DrawCard();

            // 3. تحديث جاهزية الوحوش بالساحة
            foreach (var slot in slots)
            {
                if (slot != null && slot.IsOccupied)
                {
                    slot.currentMonster.ResetTurnReadiness(turnNumber);
                }
            }
        }

        public bool DrawCard()
        {
            if (deck.Count > 0)
            {
                var card = deck[0];
                deck.RemoveAt(0);

                if (hand.Count < 10)
                {
                    hand.Add(card);
                    if (MonsterCardGame.Audio.SoundManager.Instance != null)
                    {
                        MonsterCardGame.Audio.SoundManager.Instance.PlayCardDraw();
                    }
                    Debug.Log($"[{playerName}] سحب البطاقة: {card.name} (نوع: {card.cardType})");
                    return true;
                }
                else
                {
                    graveyard.Add(card);
                    Debug.Log($"[{playerName}] اليد ممتلئة! احترقت البطاقة: {card.name}");
                    return false;
                }
            }
            else
            {
                // ضرر الإرهاق (Fatigue Damage)
                fatigueCount++;
                int fatigueDmg = fatigueCount * 30;
                TakeLeaderDamage(fatigueDmg);
                Debug.LogWarning($"[{playerName}] نفدت الكروت! تلقى ضرر إرهاق: {fatigueDmg} HP");
                return false;
            }
        }

        public bool SacrificeCard(CardData cardToSacrifice)
        {
            if (hasSacrificedThisTurn)
            {
                Debug.LogWarning("لا يمكنك التضحية بأكثر من كارت واحد في الدور!");
                return false;
            }

            if (manaCapacity >= MaxManaCap)
            {
                Debug.LogWarning("سقف المانا وصل للحد الأقصى (8)!");
                return false;
            }

            if (!hand.Contains(cardToSacrifice))
                return false;

            hand.Remove(cardToSacrifice);
            graveyard.Add(cardToSacrifice);

            manaCapacity++;
            currentPower++;
            hasSacrificedThisTurn = true;

            Debug.Log($"[{playerName}] ضحى بـ [{cardToSacrifice.name}]. سقف المانا: {manaCapacity}، الباور: {currentPower}");
            return true;
        }

        public bool SummonMonster(CardData card, int targetSlotIndex, int currentTurn)
        {
            if (targetSlotIndex < 0 || targetSlotIndex >= slots.Length)
                return false;

            if (slots[targetSlotIndex].IsOccupied)
            {
                Debug.LogWarning($"الخانة {targetSlotIndex} مشغولة بالفعل!");
                return false;
            }

            if (card.cost > currentPower)
            {
                Debug.LogWarning($"نقاط الباور غير كافية! المطلوب: {card.cost}، المتاح: {currentPower}");
                return false;
            }

            currentPower -= card.cost;
            hand.Remove(card);
            monstersSummonedTotal++;

            var instance = new MonsterInstance(card, currentTurn);
            slots[targetSlotIndex].PlaceMonster(instance);

            Debug.Log($"[{playerName}] استدعى [{card.name}] في الخانة {targetSlotIndex} (نوع: {slots[targetSlotIndex].slotType})");
            return true;
        }

        public void TakeLeaderDamage(int damage)
        {
            currentHp = Mathf.Max(0, currentHp - damage);
            Debug.Log($"🚨 [{playerName}] تلقى ضرراً مباشراً للقائد: {damage} HP. المتبقي: {currentHp}/{maxHp}");
        }

        public void HealLeader(int healAmount)
        {
            currentHp = Mathf.Min(maxHp, currentHp + healAmount);
            Debug.Log($"💚 [{playerName}] تم شفاء القائد بـ {healAmount} HP. الصحة الحالية: {currentHp}/{maxHp}");
        }
    }
}

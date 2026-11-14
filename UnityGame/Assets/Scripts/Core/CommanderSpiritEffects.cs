using System;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;
using MonsterCardGame.UI;
using MonsterCardGame.Utils;

namespace MonsterCardGame.Core
{
    /// <summary>
    /// Centralized engine for all 20 Commander Spirit cards (IDs 451–470) across the 10 clans:
    /// handles immediate altar-summon impacts, continuous battlefield auras, turn-start renewals,
    /// sacrifice/spell/summon/death triggers, and direct combat modifiers.
    /// </summary>
    public static class CommanderSpiritEffects
    {
        private static int ResolveSpiritEffectId(CardData spirit)
        {
            if (spirit == null) return 0;
            if (spirit.id >= 451 && spirit.id <= 470) return spirit.id;

            // Graceful fallback mapping for altar test presets (IDs 8001..8010)
            string tribe = (spirit.tribe ?? "").Trim().ToLowerInvariant();
            return tribe switch
            {
                "ghosts" => 451,
                "demons" => 453,
                "celestials" => 455,
                "mages" => 457,
                "warriors" => 459,
                "beasts" => 461,
                "elves" => 463,
                "dwarves" => 465,
                "giants" => 467,
                "insects" => 469,
                _ => 0
            };
        }

        private static bool IsFlankSlot(int slotIdx) => slotIdx == 0 || slotIdx == 1 || slotIdx == 4 || slotIdx == 5;
        private static bool IsVanguardSlot(int slotIdx) => slotIdx == 2 || slotIdx == 3;

        private static bool Matches(MonsterInstance m, string tribeA = null, string elemA = null, string elemB = null)
        {
            if (m == null || m.data == null) return false;
            if (!string.IsNullOrEmpty(tribeA) && string.Equals(m.data.tribe, tribeA, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(elemA) && string.Equals(m.data.element, elemA, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(elemB) && string.Equals(m.data.element, elemB, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsOwnerHuman(PlayerState player)
        {
            return GameManager.Instance != null && player == GameManager.Instance.humanPlayer;
        }

        private static void SpawnSlotFloating(bool targetIsHuman, int slotIdx, string text, Color col)
        {
            var ui = BattleUIManager.Instance;
            if (ui == null) return;
            var sv = ui.GetSlotView(slotIdx, targetIsHuman);
            var canvas = ui.GetComponentInParent<Canvas>();
            if (sv != null && canvas != null)
            {
                ui.StartCoroutine(BattleAnimations.AnimateFloatingStatus(canvas.transform, sv.transform.position, text, col));
            }
        }

        private static void SpawnLeaderFloating(bool targetIsHuman, string text, Color col, bool isHeal = false, int healAmt = 0)
        {
            var ui = BattleUIManager.Instance;
            if (ui == null) return;
            var leaderTr = ui.GetLeaderTransform(targetIsHuman);
            var canvas = ui.GetComponentInParent<Canvas>();
            if (leaderTr != null && canvas != null)
            {
                if (isHeal && healAmt > 0)
                    ui.StartCoroutine(BattleAnimations.AnimateFloatingHeal(canvas.transform, leaderTr.position, healAmt));
                else
                    ui.StartCoroutine(BattleAnimations.AnimateFloatingStatus(canvas.transform, leaderTr.position, text, col));
            }
        }

        private static void HitEnemyLeader(PlayerState target, int damage, string tag, Color col)
        {
            if (target == null || damage <= 0) return;
            target.TakeLeaderDamage(damage);
            bool oppHuman = IsOwnerHuman(target);
            var ui = BattleUIManager.Instance;
            if (ui != null)
            {
                var leaderTr = ui.GetLeaderTransform(oppHuman);
                if (leaderTr != null) ui.TriggerHitShake(leaderTr, col);
                SpawnLeaderFloating(oppHuman, $"-{damage} {tag}", col);
            }
        }

        private static void HealFriendlyLeader(PlayerState owner, PlayerState opponent, int heal)
        {
            if (owner == null || heal <= 0) return;
            int before = owner.currentHp;
            owner.HealLeader(heal, opponent);
            int gained = owner.currentHp - before;
            if (gained > 0)
            {
                SpawnLeaderFloating(IsOwnerHuman(owner), "", Color.green, true, gained);
            }
        }

        private static void GrantLeaderShield(PlayerState owner, int amount)
        {
            if (owner == null || amount <= 0) return;
            owner.shield += amount;
            SpawnLeaderFloating(IsOwnerHuman(owner), $"🛡️ +{amount} SHIELD", new Color(0.95f, 0.85f, 0.35f));
        }

        private static List<BoardSlot> GetOccupiedSlots(PlayerState player)
        {
            var result = new List<BoardSlot>();
            if (player == null || player.slots == null) return result;
            foreach (var s in player.slots)
            {
                if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive)
                    result.Add(s);
            }
            return result;
        }

        /// <summary>
        /// Resolves the board owner & opponent of an active MonsterInstance for live continuous aura queries.
        /// </summary>
        public static bool TryResolveOwner(MonsterInstance m, out PlayerState owner, out PlayerState opponent, out int slotIdx)
        {
            owner = null;
            opponent = null;
            slotIdx = m != null && m.currentSlot != null ? m.currentSlot.slotIndex : -1;

            var gm = GameManager.Instance;
            if (m == null || gm == null) return false;

            if (gm.humanPlayer != null && gm.humanPlayer.slots != null)
            {
                for (int i = 0; i < gm.humanPlayer.slots.Length; i++)
                {
                    if (gm.humanPlayer.slots[i] != null && gm.humanPlayer.slots[i].currentMonster == m)
                    {
                        owner = gm.humanPlayer;
                        opponent = gm.aiPlayer;
                        slotIdx = i;
                        return true;
                    }
                }
            }

            if (gm.aiPlayer != null && gm.aiPlayer.slots != null)
            {
                for (int i = 0; i < gm.aiPlayer.slots.Length; i++)
                {
                    if (gm.aiPlayer.slots[i] != null && gm.aiPlayer.slots[i].currentMonster == m)
                    {
                        owner = gm.aiPlayer;
                        opponent = gm.humanPlayer;
                        slotIdx = i;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Triggers the one-time battlefield entry power when a Commander Spirit is summoned onto the Spirit Altar.
        /// </summary>
        public static void ApplyOnSummoned(PlayerState owner, PlayerState opponent, CardData spirit)
        {
            int sid = ResolveSpiritEffectId(spirit);
            if (sid == 0 || owner == null) return;

            bool ownerHuman = IsOwnerHuman(owner);
            bool oppHuman = IsOwnerHuman(opponent);
            var allies = GetOccupiedSlots(owner);
            var enemies = GetOccupiedSlots(opponent);
            Color spiritCol = MonsterArtLoader.GetSpiritCategoryColor(spirit, ownerHuman);

            switch (sid)
            {
                case 451: // Archlich Valkor: Drain 12 HP from Enemy Leader
                    HitEnemyLeader(opponent, 12, "💀 DRAIN", spiritCol);
                    HealFriendlyLeader(owner, opponent, 12);
                    break;

                case 452: // Lady Morghana: Weaken up to 2 enemy monsters (-30% ATK, 2T)
                    ApplyToRandomEnemies(enemies, 2, (es) =>
                    {
                        es.currentMonster.weakenedTurns = Mathf.Max(es.currentMonster.weakenedTurns, 2);
                        SpawnSlotFloating(oppHuman, es.slotIndex, "👻 WEAKENED 2T", spiritCol);
                    });
                    break;

                case 453: // Emberlord Malakor: Deal 8 fire dmg to enemy frontline & burn 2 foes (4/T)
                    if (enemies.Count > 0 && CombatSystem.Instance != null)
                    {
                        foreach (var es in enemies)
                        {
                            CombatSystem.Instance.DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, 8, null, opponent, owner, false, true);
                        }
                        enemies = GetOccupiedSlots(opponent);
                        ApplyToRandomEnemies(enemies, 2, (es) =>
                        {
                            es.currentMonster.burnDmg = Mathf.Max(es.currentMonster.burnDmg, 4);
                            SpawnSlotFloating(oppHuman, es.slotIndex, "🔥 BURN 4/T", spiritCol);
                        });
                    }
                    else
                    {
                        HitEnemyLeader(opponent, 8, "🔥 HELLFIRE", spiritCol);
                    }
                    break;

                case 454: // Blood Baroness Lilitha: +8 Leader Shield & strongest ally +4 ATK / +6 DEF
                    GrantLeaderShield(owner, 8);
                    var strongest = FindStrongestAlly(allies);
                    if (strongest != null)
                    {
                        strongest.currentMonster.bonusAtk += 4;
                        strongest.currentMonster.AddBonusDef(6);
                        SpawnSlotFloating(ownerHuman, strongest.slotIndex, "🩸 +4/+6", spiritCol);
                    }
                    break;

                case 455: // Archon Seraph Orion: +18 Leader Shield & Vanguard allies +8 DEF
                    GrantLeaderShield(owner, 18);
                    foreach (var al in allies)
                    {
                        if (IsVanguardSlot(al.slotIndex))
                        {
                            al.currentMonster.AddBonusDef(8);
                            SpawnSlotFloating(ownerHuman, al.slotIndex, "☀️ +8 DEF", spiritCol);
                        }
                    }
                    break;

                case 456: // Dawnkeeper Celestia: Restore +22 HP & cleanse all debuffs on allies
                    HealFriendlyLeader(owner, opponent, 22);
                    foreach (var al in allies)
                    {
                        var am = al.currentMonster;
                        am.burnDmg = 0;
                        am.poisonDmg = 0;
                        am.frozenTurns = 0;
                        am.weakenedTurns = 0;
                        am.silencedTurns = 0;
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "✨ CLEANSED", spiritCol);
                    }
                    break;

                case 457: // Archmagus Zarathos: +1 Mana now & +4 Spell Surge
                    owner.currentMana += 1;
                    owner.freeSkillBonus += 4;
                    SpawnLeaderFloating(ownerHuman, "🔮 +1 MANA • +4 SURGE", spiritCol);
                    break;

                case 458: // Astrea the Voidseer: Silence strongest enemy 2T
                    var topEnemy = FindStrongestAlly(enemies);
                    if (topEnemy != null)
                    {
                        topEnemy.currentMonster.silencedTurns = Mathf.Max(topEnemy.currentMonster.silencedTurns, 2);
                        SpawnSlotFloating(oppHuman, topEnemy.slotIndex, "🔮 SILENCED 2T", spiritCol);
                    }
                    break;

                case 459: // Iron Marshal Garik: Rally all living allies +4 ATK / +4 DEF
                    foreach (var al in allies)
                    {
                        al.currentMonster.bonusAtk += 4;
                        al.currentMonster.AddBonusDef(4);
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "⚔️ +4/+4", spiritCol);
                    }
                    break;

                case 460: // Valeria the Shieldmaiden: +15 Shield & Bubble Shield on weakest ally
                    GrantLeaderShield(owner, 15);
                    var weakest = FindWeakestAlly(allies);
                    if (weakest != null)
                    {
                        weakest.currentMonster.bubbleShield = true;
                        SpawnSlotFloating(ownerHuman, weakest.slotIndex, "🫧 BUBBLE SHIELD", spiritCol);
                    }
                    break;

                case 461: // Ashmane Fenrir: Flank allies gain +5 ATK
                    foreach (var al in allies)
                    {
                        if (IsFlankSlot(al.slotIndex))
                        {
                            al.currentMonster.bonusAtk += 5;
                            SpawnSlotFloating(ownerHuman, al.slotIndex, "🐺 +5 ATK", spiritCol);
                        }
                    }
                    break;

                case 462: // Clawmother Ravika: All living allies gain +4 ATK
                    foreach (var al in allies)
                    {
                        al.currentMonster.bonusAtk += 4;
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "🐾 +4 ATK", spiritCol);
                    }
                    break;

                case 463: // Rootwarden Sylvandor: +1 Mana & all allies +6 DEF
                    owner.currentMana += 1;
                    SpawnLeaderFloating(ownerHuman, "🌿 +1 MANA", spiritCol);
                    foreach (var al in allies)
                    {
                        al.currentMonster.AddBonusDef(6);
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "🌿 +6 DEF", spiritCol);
                    }
                    break;

                case 464: // Moonbow Elara: Deal 8 lunar dmg to 2 random enemy monsters
                    if (enemies.Count > 0 && CombatSystem.Instance != null)
                    {
                        ApplyToRandomEnemies(enemies, 2, (es) =>
                        {
                            CombatSystem.Instance.DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, 8, null, opponent, owner, false, true);
                        });
                    }
                    else
                    {
                        HitEnemyLeader(opponent, 8, "🌙 LUNAR", spiritCol);
                    }
                    break;

                case 465: // Forgeking Thorin: +10 Leader Shield & all allies +6 DEF
                    GrantLeaderShield(owner, 10);
                    foreach (var al in allies)
                    {
                        al.currentMonster.AddBonusDef(6);
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "⚒️ +6 DEF", spiritCol);
                    }
                    break;

                case 466: // Runesmith Brunda: All allies +6 DEF & cleanse Freeze/Weaken
                    foreach (var al in allies)
                    {
                        var am = al.currentMonster;
                        am.frozenTurns = 0;
                        am.weakenedTurns = 0;
                        am.AddBonusDef(6);
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "⚒️ +6 DEF", spiritCol);
                    }
                    break;

                case 467: // Crag Titan Kronos: 10 seismic damage to all enemy monsters
                    if (enemies.Count > 0 && CombatSystem.Instance != null)
                    {
                        foreach (var es in enemies)
                        {
                            CombatSystem.Instance.DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, 10, null, opponent, owner, false, true);
                        }
                    }
                    else
                    {
                        HitEnemyLeader(opponent, 10, "🗿 QUAKE", spiritCol);
                    }
                    break;

                case 468: // Basalt Matriarch Skaya: Freeze strongest enemy 1T & all allies +6 DEF
                    var freezeTarget = FindStrongestAlly(enemies);
                    if (freezeTarget != null)
                    {
                        freezeTarget.currentMonster.frozenTurns = Mathf.Max(freezeTarget.currentMonster.frozenTurns, 1);
                        SpawnSlotFloating(oppHuman, freezeTarget.slotIndex, "❄️ FROZEN 1T", spiritCol);
                    }
                    foreach (var al in allies)
                    {
                        al.currentMonster.AddBonusDef(6);
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "❄️ +6 DEF", spiritCol);
                    }
                    break;

                case 469: // Hive Emperor Chitinus: Poison 2 enemies (4/T) & harden allies +5 DEF
                    ApplyToRandomEnemies(enemies, 2, (es) =>
                    {
                        es.currentMonster.poisonDmg = Mathf.Max(es.currentMonster.poisonDmg, 4);
                        SpawnSlotFloating(oppHuman, es.slotIndex, "☠️ POISON 4/T", spiritCol);
                    });
                    foreach (var al in allies)
                    {
                        al.currentMonster.AddBonusDef(5);
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "🪲 +5 DEF", spiritCol);
                    }
                    break;

                case 470: // Mantis Queen Zerilla: Insects & Flank allies +5 ATK
                    foreach (var al in allies)
                    {
                        if (Matches(al.currentMonster, "Insects") || IsFlankSlot(al.slotIndex))
                        {
                            al.currentMonster.bonusAtk += 5;
                            SpawnSlotFloating(ownerHuman, al.slotIndex, "🪲 +5 ATK", spiritCol);
                        }
                    }
                    break;
            }
        }

        public static void OnMonsterSummoned(PlayerState owner, PlayerState opponent, int slotIdx, MonsterInstance m)
        {
            if (owner == null || owner.activeLeader == null || m == null) return;
            int sid = ResolveSpiritEffectId(owner.activeLeader);
            bool ownerHuman = IsOwnerHuman(owner);
            Color spiritCol = MonsterArtLoader.GetSpiritCategoryColor(owner.activeLeader, ownerHuman);

            switch (sid)
            {
                case 456: // Celestia: newly summoned allies gain +6 DEF
                    m.AddBonusDef(6);
                    SpawnSlotFloating(ownerHuman, slotIdx, "☀️ +6 DEF", spiritCol);
                    break;
                case 460: // Valeria: Vanguard summons gain +8 DEF
                    if (IsVanguardSlot(slotIdx))
                    {
                        m.AddBonusDef(8);
                        SpawnSlotFloating(ownerHuman, slotIdx, "🛡️ +8 DEF", spiritCol);
                    }
                    break;
                case 462: // Ravika: Beasts & Nature summons gain +4/+4
                    if (Matches(m, "Beasts", "Nature"))
                    {
                        m.bonusAtk += 4;
                        m.AddBonusDef(4);
                        SpawnSlotFloating(ownerHuman, slotIdx, "🐾 +4/+4", spiritCol);
                    }
                    break;
                case 465: // Thorin: +2 Leader Shield per summon
                    GrantLeaderShield(owner, 2);
                    break;
            }
        }

        public static void OnCardSacrificed(PlayerState owner, PlayerState opponent)
        {
            if (owner == null || owner.activeLeader == null) return;
            int sid = ResolveSpiritEffectId(owner.activeLeader);
            if (sid != 454) return; // Blood Baroness Lilitha

            bool ownerHuman = IsOwnerHuman(owner);
            Color spiritCol = MonsterArtLoader.GetSpiritCategoryColor(owner.activeLeader, ownerHuman);
            foreach (var al in GetOccupiedSlots(owner))
            {
                al.currentMonster.bonusAtk += 3;
                SpawnSlotFloating(ownerHuman, al.slotIndex, "🩸 +3 ATK", spiritCol);
            }
            HitEnemyLeader(opponent, 8, "🩸 BLOOD PACT", spiritCol);
        }

        public static void OnSpellCast(PlayerState owner, PlayerState opponent)
        {
            if (owner == null || owner.activeLeader == null) return;
            int sid = ResolveSpiritEffectId(owner.activeLeader);
            if (sid != 457) return; // Archmagus Zarathos

            bool ownerHuman = IsOwnerHuman(owner);
            Color spiritCol = MonsterArtLoader.GetSpiritCategoryColor(owner.activeLeader, ownerHuman);
            foreach (var al in GetOccupiedSlots(owner))
            {
                al.currentMonster.bonusAtk += 2;
                SpawnSlotFloating(ownerHuman, al.slotIndex, "🔮 +2 ATK", spiritCol);
            }
            HitEnemyLeader(opponent, 6, "🔮 ARCANE SURGE", spiritCol);
        }

        public static void OnStartOfTurn(PlayerState activePlayer, PlayerState otherPlayer)
        {
            if (activePlayer == null || activePlayer.activeLeader == null) return;
            int sid = ResolveSpiritEffectId(activePlayer.activeLeader);
            bool activeHuman = IsOwnerHuman(activePlayer);
            Color spiritCol = MonsterArtLoader.GetSpiritCategoryColor(activePlayer.activeLeader, activeHuman);

            switch (sid)
            {
                case 452: // Morghana: +6 HP each turn
                    HealFriendlyLeader(activePlayer, otherPlayer, 6);
                    break;
                case 455: // Orion: +8 HP each turn
                    HealFriendlyLeader(activePlayer, otherPlayer, 8);
                    break;
                case 456: // Celestia: +3 DEF to Celestials, Light & Water
                    foreach (var al in GetOccupiedSlots(activePlayer))
                    {
                        if (Matches(al.currentMonster, "Celestials", "Light", "Water"))
                        {
                            al.currentMonster.HealDefense(3);
                        }
                    }
                    break;
                case 463: // Sylvandor: +4 DEF to Elves & Nature & +5 HP to Leader
                    HealFriendlyLeader(activePlayer, otherPlayer, 5);
                    foreach (var al in GetOccupiedSlots(activePlayer))
                    {
                        if (Matches(al.currentMonster, "Elves", "Nature"))
                        {
                            al.currentMonster.HealDefense(4);
                            SpawnSlotFloating(activeHuman, al.slotIndex, "🌿 +4 DEF", spiritCol);
                        }
                    }
                    break;
            }
        }

        public static int GetContinuousAtkBonusForInstance(MonsterInstance m)
        {
            if (!TryResolveOwner(m, out PlayerState owner, out _, out int slotIdx)) return 0;
            return GetContinuousAtkBonus(owner, slotIdx, m);
        }

        public static int GetContinuousAtkBonus(PlayerState owner, int slotIdx, MonsterInstance m)
        {
            if (owner == null || owner.activeLeader == null || m == null) return 0;
            int sid = ResolveSpiritEffectId(owner.activeLeader);

            switch (sid)
            {
                case 451: return Matches(m, "Ghosts", "Dark") ? 4 : 0;
                case 453: return Matches(m, "Demons", "Fire") ? 5 : 0;
                case 455: return Matches(m, "Celestials", "Light") ? 3 : 0;
                case 457: return Matches(m, "Mages", "Arcane") ? 4 : 0;
                case 458: return Matches(m, "Mages", "Arcane", "Ice") ? 4 : 0;
                case 459: return (Matches(m, "Warriors") || IsVanguardSlot(slotIdx)) ? 4 : 0;
                case 461: return (Matches(m, "Beasts") || IsFlankSlot(slotIdx)) ? 4 : 0;
                case 464: return (Matches(m, "Elves", "Air") || IsFlankSlot(slotIdx)) ? 4 : 0;
                case 465: return Matches(m, "Dwarves", "Earth", "Fire") ? 3 : 0;
                case 466: return Matches(m, "Dwarves", "Earth") ? 3 : 0;
                case 467: return Matches(m, "Giants", "Earth") ? 5 : 0;
                case 468: return Matches(m, "Giants", "Earth", "Ice") ? 4 : 0;
                case 469: return Matches(m, "Insects", "Dark") ? 3 : 0;
                case 470: return Matches(m, "Insects", "Nature") ? 4 : 0;
                default: return 0;
            }
        }

        public static int GetEnemyDreadAtkPenalty(PlayerState opponent, MonsterInstance attackerM)
        {
            if (opponent == null || opponent.activeLeader == null || attackerM == null) return 0;
            int sid = ResolveSpiritEffectId(opponent.activeLeader);
            if (sid == 452 || sid == 468) return 2;
            return 0;
        }

        public static int GetSkillManaDiscount(PlayerState owner)
        {
            if (owner == null || owner.activeLeader == null) return 0;
            return ResolveSpiritEffectId(owner.activeLeader) == 458 ? 1 : 0;
        }

        public static float GetCombatEvasionChance(PlayerState defenderOwner, int slotIdx, MonsterInstance defenderM)
        {
            if (defenderOwner == null || defenderOwner.activeLeader == null || defenderM == null) return 0f;
            int sid = ResolveSpiritEffectId(defenderOwner.activeLeader);
            return sid switch
            {
                452 => (Matches(defenderM, "Ghosts", "Ice") || IsFlankSlot(slotIdx)) ? 0.20f : 0f,
                458 => Matches(defenderM, "Mages", "Arcane", "Ice") ? 0.18f : 0f,
                464 => (Matches(defenderM, "Elves", "Air") || IsFlankSlot(slotIdx)) ? 0.15f : 0f,
                _ => 0f
            };
        }

        public static int GetCombatIncomingReduction(PlayerState defenderOwner, MonsterInstance defenderM)
        {
            if (defenderOwner == null || defenderOwner.activeLeader == null || defenderM == null) return 0;
            int sid = ResolveSpiritEffectId(defenderOwner.activeLeader);
            return sid switch
            {
                455 => Matches(defenderM, "Celestials", "Light") ? 3 : 0,
                460 => (Matches(defenderM, "Warriors") || (defenderM.data != null && defenderM.data.GetRoleEnum() == TacticalRole.PureShield)) ? 3 : 0,
                465 => Matches(defenderM, "Dwarves", "Earth", "Fire") ? 3 : 0,
                _ => 0
            };
        }

        public static int ModifyOutgoingCombatAttack(PlayerState attackerOwner, MonsterInstance attackerM, MonsterInstance defenderM, int atkPower)
        {
            if (attackerOwner == null || attackerOwner.activeLeader == null || attackerM == null) return atkPower;
            int sid = ResolveSpiritEffectId(attackerOwner.activeLeader);

            if (sid == 459 && defenderM != null)
            {
                atkPower += 3;
            }
            else if (sid == 462 && Matches(attackerM, "Beasts") && UnityEngine.Random.value < 0.18f)
            {
                atkPower = Mathf.RoundToInt(atkPower * 1.35f);
            }
            else if (sid == 470 && defenderM != null && defenderM.poisonDmg > 0)
            {
                atkPower += 3;
            }
            return Mathf.Max(0, atkPower);
        }

        public static void OnCombatHitDealt(PlayerState attackerOwner, MonsterInstance attackerM, PlayerState defenderOwner, MonsterInstance defenderM, int dmgDealt)
        {
            if (attackerM == null || defenderM == null) return;

            if (attackerOwner != null && attackerOwner.activeLeader != null)
            {
                int attSid = ResolveSpiritEffectId(attackerOwner.activeLeader);
                if (attSid == 453)
                {
                    defenderM.burnDmg = Mathf.Max(defenderM.burnDmg, 4);
                }
                else if (attSid == 454 && Matches(attackerM, "Demons", "Dark"))
                {
                    attackerM.HealDefense(Mathf.Max(2, Mathf.RoundToInt(dmgDealt * 0.15f)));
                }
                else if (attSid == 469)
                {
                    defenderM.poisonDmg = Mathf.Max(defenderM.poisonDmg, 4);
                }
                else if (attSid == 470)
                {
                    defenderM.poisonDmg += 2;
                }
            }

            if (defenderOwner != null && defenderOwner.activeLeader != null && attackerM.IsAlive)
            {
                int defSid = ResolveSpiritEffectId(defenderOwner.activeLeader);
                if ((defSid == 466 && Matches(defenderM, "Dwarves", "Earth")) ||
                    (defSid == 468 && Matches(defenderM, "Giants", "Earth", "Ice")))
                {
                    attackerM.TakeDamage(3);
                }
            }
        }

        public static void OnMonsterDied(PlayerState deadOwner, PlayerState killerOwner, MonsterInstance deadM, MonsterInstance killerM)
        {
            if (deadM == null) return;

            // Check Archlich Valkor (451) on EITHER side
            var gm = GameManager.Instance;
            if (gm != null)
            {
                ApplyValkorDeathHarvest(gm.humanPlayer, gm.aiPlayer);
                ApplyValkorDeathHarvest(gm.aiPlayer, gm.humanPlayer);
            }

            // Crag Titan Kronos (467): allied Giant/Earth death triggers 10 seismic damage
            if (deadOwner != null && deadOwner.activeLeader != null && ResolveSpiritEffectId(deadOwner.activeLeader) == 467 && Matches(deadM, "Giants", "Earth"))
            {
                var opp = (deadOwner == gm?.humanPlayer) ? gm?.aiPlayer : gm?.humanPlayer;
                HitEnemyLeader(opp, 10, "🗿 TITANFALL", MonsterArtLoader.GetSpiritCategoryColor(deadOwner.activeLeader, IsOwnerHuman(deadOwner)));
            }

            // Ashmane Fenrir (461): killer monster gains +3 ATK & +5 DEF
            if (killerOwner != null && killerOwner.activeLeader != null && ResolveSpiritEffectId(killerOwner.activeLeader) == 461 && killerM != null && killerM.IsAlive)
            {
                killerM.bonusAtk += 3;
                killerM.HealDefense(5);
                if (killerM.currentSlot != null)
                {
                    SpawnSlotFloating(IsOwnerHuman(killerOwner), killerM.currentSlot.slotIndex, "🐺 +3/+5 KILL FEAST", MonsterArtLoader.GetSpiritCategoryColor(killerOwner.activeLeader, IsOwnerHuman(killerOwner)));
                }
            }
        }

        private static void ApplyValkorDeathHarvest(PlayerState possessor, PlayerState enemy)
        {
            if (possessor == null || possessor.activeLeader == null) return;
            if (ResolveSpiritEffectId(possessor.activeLeader) != 451) return;

            Color col = MonsterArtLoader.GetSpiritCategoryColor(possessor.activeLeader, IsOwnerHuman(possessor));
            HitEnemyLeader(enemy, 6, "💀 SOUL HARVEST", col);
            var weakest = FindWeakestAlly(GetOccupiedSlots(possessor));
            if (weakest != null)
            {
                weakest.currentMonster.HealDefense(4);
                SpawnSlotFloating(IsOwnerHuman(possessor), weakest.slotIndex, "💀 +4 DEF", col);
            }
        }

        private static void ApplyToRandomEnemies(List<BoardSlot> enemies, int count, Action<BoardSlot> action)
        {
            if (enemies == null || enemies.Count == 0 || action == null) return;
            var pool = new List<BoardSlot>(enemies);
            int n = Mathf.Min(count, pool.Count);
            for (int i = 0; i < n; i++)
            {
                int idx = UnityEngine.Random.Range(0, pool.Count);
                var chosen = pool[idx];
                pool.RemoveAt(idx);
                action(chosen);
            }
        }

        private static BoardSlot FindStrongestAlly(List<BoardSlot> list)
        {
            BoardSlot best = null;
            int bestVal = -1;
            foreach (var s in list)
            {
                if (s == null || !s.IsOccupied) continue;
                int v = s.currentMonster.baseAtk + s.currentMonster.bonusAtk + s.currentMonster.currentDef;
                if (v > bestVal)
                {
                    bestVal = v;
                    best = s;
                }
            }
            return best;
        }

        private static BoardSlot FindWeakestAlly(List<BoardSlot> list)
        {
            BoardSlot best = null;
            int lowest = int.MaxValue;
            foreach (var s in list)
            {
                if (s == null || !s.IsOccupied) continue;
                if (s.currentMonster.currentDef < lowest)
                {
                    lowest = s.currentMonster.currentDef;
                    best = s;
                }
            }
            return best;
        }
    }
}

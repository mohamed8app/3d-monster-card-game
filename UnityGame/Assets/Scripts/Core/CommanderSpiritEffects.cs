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
    /// Positive monster perks apply strictly to allied members of the Spirit's own clan (tribe),
    /// while negative curses and combat penalties punish enemy / rival clans.
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

        private static bool IsTribe(MonsterInstance m, string expectedTribe)
        {
            return m != null && m.data != null && !string.IsNullOrEmpty(expectedTribe)
                && string.Equals(m.data.tribe, expectedTribe, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRivalTribe(MonsterInstance m, string commanderTribe)
        {
            return m != null && m.data != null && !string.IsNullOrEmpty(commanderTribe)
                && !string.Equals(m.data.tribe, commanderTribe, StringComparison.OrdinalIgnoreCase);
        }

        private static List<BoardSlot> FilterTribeSlots(List<BoardSlot> slots, string tribe)
        {
            var res = new List<BoardSlot>();
            if (slots == null) return res;
            foreach (var s in slots)
            {
                if (s != null && s.IsOccupied && IsTribe(s.currentMonster, tribe)) res.Add(s);
            }
            return res;
        }

        private static List<BoardSlot> FilterRivalTribeSlots(List<BoardSlot> slots, string commanderTribe)
        {
            var res = new List<BoardSlot>();
            if (slots == null) return res;
            foreach (var s in slots)
            {
                if (s != null && s.IsOccupied && IsRivalTribe(s.currentMonster, commanderTribe)) res.Add(s);
            }
            return res;
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
        /// Positive unit buffs apply strictly to matching clan allies; negative debuffs hit enemy rival clans.
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
                case 451: // Archlich Valkor (Ghosts): Drain 12 HP & weaken 1 non-Ghost enemy
                {
                    HitEnemyLeader(opponent, 12, "💀 DRAIN", spiritCol);
                    HealFriendlyLeader(owner, opponent, 12);
                    var rival = FilterRivalTribeSlots(enemies, "Ghosts");
                    ApplyToRandomEnemies(rival, 1, (es) =>
                    {
                        es.currentMonster.weakenedTurns = Mathf.Max(es.currentMonster.weakenedTurns, 2);
                        SpawnSlotFloating(oppHuman, es.slotIndex, "💀 CURSED 2T", spiritCol);
                    });
                    break;
                }

                case 452: // Lady Morghana (Ghosts): Weaken up to 2 enemy non-Ghosts
                {
                    var rival = FilterRivalTribeSlots(enemies, "Ghosts");
                    ApplyToRandomEnemies(rival, 2, (es) =>
                    {
                        es.currentMonster.weakenedTurns = Mathf.Max(es.currentMonster.weakenedTurns, 2);
                        SpawnSlotFloating(oppHuman, es.slotIndex, "👻 WEAKENED 2T", spiritCol);
                    });
                    break;
                }

                case 453: // Emberlord Malakor (Demons): 10 fire dmg to enemy non-Demons (6 to Demons) & burn 2 rival foes
                {
                    if (enemies.Count > 0 && CombatSystem.Instance != null)
                    {
                        foreach (var es in enemies)
                        {
                            int dmg = IsRivalTribe(es.currentMonster, "Demons") ? 10 : 6;
                            CombatSystem.Instance.DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, dmg, null, opponent, owner, false, true);
                        }
                        var rival = FilterRivalTribeSlots(GetOccupiedSlots(opponent), "Demons");
                        ApplyToRandomEnemies(rival, 2, (es) =>
                        {
                            es.currentMonster.burnDmg = Mathf.Max(es.currentMonster.burnDmg, 5);
                            SpawnSlotFloating(oppHuman, es.slotIndex, "🔥 BURN 5/T", spiritCol);
                        });
                    }
                    else
                    {
                        HitEnemyLeader(opponent, 8, "🔥 HELLFIRE", spiritCol);
                    }
                    break;
                }

                case 454: // Blood Baroness Lilitha (Demons): +10 Leader Shield & strongest allied Demon +5 ATK / +6 DEF
                {
                    GrantLeaderShield(owner, 10);
                    var strongest = FindStrongestAlly(FilterTribeSlots(allies, "Demons"));
                    if (strongest != null)
                    {
                        strongest.currentMonster.bonusAtk += 5;
                        strongest.currentMonster.AddBonusDef(6);
                        SpawnSlotFloating(ownerHuman, strongest.slotIndex, "🩸 +5/+6", spiritCol);
                    }
                    break;
                }

                case 455: // Archon Seraph Orion (Celestials): +18 Leader Shield & allied Celestials +8 DEF
                {
                    GrantLeaderShield(owner, 18);
                    foreach (var al in FilterTribeSlots(allies, "Celestials"))
                    {
                        al.currentMonster.AddBonusDef(8);
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "☀️ +8 DEF", spiritCol);
                    }
                    break;
                }

                case 456: // Dawnkeeper Celestia (Celestials): Restore +22 HP & cleanse debuffs on allied Celestials
                {
                    HealFriendlyLeader(owner, opponent, 22);
                    foreach (var al in FilterTribeSlots(allies, "Celestials"))
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
                }

                case 457: // Archmagus Zarathos (Mages): +1 Mana now & +4 Spell Surge
                {
                    owner.currentMana += 1;
                    owner.freeSkillBonus += 4;
                    SpawnLeaderFloating(ownerHuman, "🔮 +1 MANA • +4 SURGE", spiritCol);
                    break;
                }

                case 458: // Astrea the Voidseer (Mages): Silence strongest enemy 2T & weaken rival clans
                {
                    var topEnemy = FindStrongestAlly(enemies);
                    if (topEnemy != null)
                    {
                        topEnemy.currentMonster.silencedTurns = Mathf.Max(topEnemy.currentMonster.silencedTurns, 2);
                        if (IsRivalTribe(topEnemy.currentMonster, "Mages"))
                            topEnemy.currentMonster.weakenedTurns = Mathf.Max(topEnemy.currentMonster.weakenedTurns, 1);
                        SpawnSlotFloating(oppHuman, topEnemy.slotIndex, "🔮 SILENCED 2T", spiritCol);
                    }
                    break;
                }

                case 459: // Iron Marshal Garik (Warriors): +6 Shield & rally allied Warriors +5 ATK / +5 DEF
                {
                    GrantLeaderShield(owner, 6);
                    foreach (var al in FilterTribeSlots(allies, "Warriors"))
                    {
                        al.currentMonster.bonusAtk += 5;
                        al.currentMonster.AddBonusDef(5);
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "⚔️ +5/+5", spiritCol);
                    }
                    break;
                }

                case 460: // Valeria the Shieldmaiden (Warriors): +15 Shield & Bubble Shield on weakest allied Warrior
                {
                    GrantLeaderShield(owner, 15);
                    var weakest = FindWeakestAlly(FilterTribeSlots(allies, "Warriors"));
                    if (weakest != null)
                    {
                        weakest.currentMonster.bubbleShield = true;
                        SpawnSlotFloating(ownerHuman, weakest.slotIndex, "🫧 BUBBLE SHIELD", spiritCol);
                    }
                    break;
                }

                case 461: // Ashmane Fenrir (Beasts): Allied Beasts gain +6 ATK
                {
                    foreach (var al in FilterTribeSlots(allies, "Beasts"))
                    {
                        al.currentMonster.bonusAtk += 6;
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "🐺 +6 ATK", spiritCol);
                    }
                    break;
                }

                case 462: // Clawmother Ravika (Beasts): Allied Beasts +4/+4 & weaken 1 non-Beast enemy 2T
                {
                    foreach (var al in FilterTribeSlots(allies, "Beasts"))
                    {
                        al.currentMonster.bonusAtk += 4;
                        al.currentMonster.AddBonusDef(4);
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "🐾 +4/+4", spiritCol);
                    }
                    ApplyToRandomEnemies(FilterRivalTribeSlots(enemies, "Beasts"), 1, (es) =>
                    {
                        es.currentMonster.weakenedTurns = Mathf.Max(es.currentMonster.weakenedTurns, 2);
                        SpawnSlotFloating(oppHuman, es.slotIndex, "🐾 MAULED 2T", spiritCol);
                    });
                    break;
                }

                case 463: // Rootwarden Sylvandor (Elves): +1 Mana & allied Elves +7 DEF
                {
                    owner.currentMana += 1;
                    SpawnLeaderFloating(ownerHuman, "🌿 +1 MANA", spiritCol);
                    foreach (var al in FilterTribeSlots(allies, "Elves"))
                    {
                        al.currentMonster.AddBonusDef(7);
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "🌿 +7 DEF", spiritCol);
                    }
                    break;
                }

                case 464: // Moonbow Elara (Elves): 9 lunar dmg to 2 enemy non-Elves (6 to Elves)
                {
                    var targets = FilterRivalTribeSlots(enemies, "Elves");
                    if (targets.Count == 0) targets = enemies;
                    if (targets.Count > 0 && CombatSystem.Instance != null)
                    {
                        ApplyToRandomEnemies(targets, 2, (es) =>
                        {
                            int dmg = IsRivalTribe(es.currentMonster, "Elves") ? 9 : 6;
                            CombatSystem.Instance.DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, dmg, null, opponent, owner, false, true);
                        });
                    }
                    else
                    {
                        HitEnemyLeader(opponent, 8, "🌙 LUNAR", spiritCol);
                    }
                    break;
                }

                case 465: // Forgeking Thorin (Dwarves): +12 Leader Shield & allied Dwarves +7 DEF
                {
                    GrantLeaderShield(owner, 12);
                    foreach (var al in FilterTribeSlots(allies, "Dwarves"))
                    {
                        al.currentMonster.AddBonusDef(7);
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "⚒️ +7 DEF", spiritCol);
                    }
                    break;
                }

                case 466: // Runesmith Brunda (Dwarves): Allied Dwarves +7 DEF & cleanse Freeze/Weaken
                {
                    foreach (var al in FilterTribeSlots(allies, "Dwarves"))
                    {
                        var am = al.currentMonster;
                        am.frozenTurns = 0;
                        am.weakenedTurns = 0;
                        am.AddBonusDef(7);
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "⚒️ +7 DEF", spiritCol);
                    }
                    break;
                }

                case 467: // Crag Titan Kronos (Giants): 12 quake dmg to non-Giant enemies (8 to Giants)
                {
                    if (enemies.Count > 0 && CombatSystem.Instance != null)
                    {
                        foreach (var es in enemies)
                        {
                            int dmg = IsRivalTribe(es.currentMonster, "Giants") ? 12 : 8;
                            CombatSystem.Instance.DealSpellOrSkillDamage(es.slotIndex, es.currentMonster, dmg, null, opponent, owner, false, true);
                        }
                    }
                    else
                    {
                        HitEnemyLeader(opponent, 10, "🗿 QUAKE", spiritCol);
                    }
                    break;
                }

                case 468: // Basalt Matriarch Skaya (Giants): Freeze strongest enemy (2T vs non-Giant) & allied Giants +7 DEF
                {
                    var freezeTarget = FindStrongestAlly(enemies);
                    if (freezeTarget != null)
                    {
                        int turns = IsRivalTribe(freezeTarget.currentMonster, "Giants") ? 2 : 1;
                        freezeTarget.currentMonster.frozenTurns = Mathf.Max(freezeTarget.currentMonster.frozenTurns, turns);
                        SpawnSlotFloating(oppHuman, freezeTarget.slotIndex, $"❄️ FROZEN {turns}T", spiritCol);
                    }
                    foreach (var al in FilterTribeSlots(allies, "Giants"))
                    {
                        al.currentMonster.AddBonusDef(7);
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "❄️ +7 DEF", spiritCol);
                    }
                    break;
                }

                case 469: // Hive Emperor Chitinus (Insects): Poison 2 enemy non-Insects (5/T) & allied Insects +6 DEF
                {
                    ApplyToRandomEnemies(FilterRivalTribeSlots(enemies, "Insects"), 2, (es) =>
                    {
                        es.currentMonster.poisonDmg = Mathf.Max(es.currentMonster.poisonDmg, 5);
                        SpawnSlotFloating(oppHuman, es.slotIndex, "☠️ POISON 5/T", spiritCol);
                    });
                    foreach (var al in FilterTribeSlots(allies, "Insects"))
                    {
                        al.currentMonster.AddBonusDef(6);
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "🪲 +6 DEF", spiritCol);
                    }
                    break;
                }

                case 470: // Mantis Queen Zerilla (Insects): Allied Insects +6 ATK & poison 1 non-Insect foe
                {
                    foreach (var al in FilterTribeSlots(allies, "Insects"))
                    {
                        al.currentMonster.bonusAtk += 6;
                        SpawnSlotFloating(ownerHuman, al.slotIndex, "🪲 +6 ATK", spiritCol);
                    }
                    ApplyToRandomEnemies(FilterRivalTribeSlots(enemies, "Insects"), 1, (es) =>
                    {
                        es.currentMonster.poisonDmg = Mathf.Max(es.currentMonster.poisonDmg, 4);
                        SpawnSlotFloating(oppHuman, es.slotIndex, "☠️ POISON 4/T", spiritCol);
                    });
                    break;
                }
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
                case 456: // Celestia: strictly allied Celestials gain +8 DEF
                    if (IsTribe(m, "Celestials"))
                    {
                        m.AddBonusDef(8);
                        SpawnSlotFloating(ownerHuman, slotIdx, "☀️ +8 DEF", spiritCol);
                    }
                    break;
                case 460: // Valeria: strictly allied Warriors gain +8 DEF
                    if (IsTribe(m, "Warriors"))
                    {
                        m.AddBonusDef(8);
                        SpawnSlotFloating(ownerHuman, slotIdx, "🛡️ +8 DEF", spiritCol);
                    }
                    break;
                case 462: // Ravika: strictly allied Beasts gain +4/+4
                    if (IsTribe(m, "Beasts"))
                    {
                        m.bonusAtk += 4;
                        m.AddBonusDef(4);
                        SpawnSlotFloating(ownerHuman, slotIdx, "🐾 +4/+4", spiritCol);
                    }
                    break;
                case 465: // Thorin: strictly summoning a Dwarf grants +4 Leader Shield
                    if (IsTribe(m, "Dwarves"))
                    {
                        GrantLeaderShield(owner, 4);
                    }
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
            foreach (var al in FilterTribeSlots(GetOccupiedSlots(owner), "Demons"))
            {
                al.currentMonster.bonusAtk += 4;
                SpawnSlotFloating(ownerHuman, al.slotIndex, "🩸 +4 ATK", spiritCol);
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
            foreach (var al in FilterTribeSlots(GetOccupiedSlots(owner), "Mages"))
            {
                al.currentMonster.bonusAtk += 3;
                SpawnSlotFloating(ownerHuman, al.slotIndex, "🔮 +3 ATK", spiritCol);
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
                case 452: // Morghana: +6 HP each turn & chill 1 rival enemy
                    HealFriendlyLeader(activePlayer, otherPlayer, 6);
                    ApplyToRandomEnemies(FilterRivalTribeSlots(GetOccupiedSlots(otherPlayer), "Ghosts"), 1, (es) =>
                    {
                        es.currentMonster.weakenedTurns = Mathf.Max(es.currentMonster.weakenedTurns, 1);
                        SpawnSlotFloating(!activeHuman, es.slotIndex, "👻 DREAD 1T", spiritCol);
                    });
                    break;
                case 455: // Orion: +8 HP each turn
                    HealFriendlyLeader(activePlayer, otherPlayer, 8);
                    break;
                case 456: // Celestia: strictly allied Celestials heal +4 DEF/turn
                    foreach (var al in FilterTribeSlots(GetOccupiedSlots(activePlayer), "Celestials"))
                    {
                        al.currentMonster.HealDefense(4);
                    }
                    break;
                case 463: // Sylvandor: strictly allied Elves heal +4 DEF & Leader restores +5 HP
                    HealFriendlyLeader(activePlayer, otherPlayer, 5);
                    foreach (var al in FilterTribeSlots(GetOccupiedSlots(activePlayer), "Elves"))
                    {
                        al.currentMonster.HealDefense(4);
                        SpawnSlotFloating(activeHuman, al.slotIndex, "🌿 +4 DEF", spiritCol);
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
                case 451: return IsTribe(m, "Ghosts") ? 5 : 0;
                case 453: return IsTribe(m, "Demons") ? 5 : 0;
                case 455: return IsTribe(m, "Celestials") ? 4 : 0;
                case 457: return IsTribe(m, "Mages") ? 5 : 0;
                case 458: return IsTribe(m, "Mages") ? 4 : 0;
                case 459: return IsTribe(m, "Warriors") ? 4 : 0;
                case 461: return IsTribe(m, "Beasts") ? 4 : 0;
                case 464: return IsTribe(m, "Elves") ? 4 : 0;
                case 465: return IsTribe(m, "Dwarves") ? 4 : 0;
                case 466: return IsTribe(m, "Dwarves") ? 4 : 0;
                case 467: return IsTribe(m, "Giants") ? 6 : 0;
                case 468: return IsTribe(m, "Giants") ? 5 : 0;
                case 469: return IsTribe(m, "Insects") ? 4 : 0;
                case 470: return IsTribe(m, "Insects") ? 4 : 0;
                default: return 0;
            }
        }

        /// <summary>
        /// Negative ATK aura suffered by opposing monsters from OTHER (rival) clans.
        /// </summary>
        public static int GetEnemyDreadAtkPenalty(PlayerState opponent, MonsterInstance attackerM)
        {
            if (opponent == null || opponent.activeLeader == null || attackerM == null) return 0;
            int sid = ResolveSpiritEffectId(opponent.activeLeader);
            return sid switch
            {
                452 => IsRivalTribe(attackerM, "Ghosts") ? 3 : 0,
                456 => IsRivalTribe(attackerM, "Celestials") ? 2 : 0,
                463 => IsRivalTribe(attackerM, "Elves") ? 2 : 0,
                468 => IsRivalTribe(attackerM, "Giants") ? 3 : 0,
                _ => 0
            };
        }

        public static int GetSkillManaDiscount(PlayerState owner, MonsterInstance m)
        {
            if (owner == null || owner.activeLeader == null || m == null) return 0;
            return ResolveSpiritEffectId(owner.activeLeader) == 458 && IsTribe(m, "Mages") ? 1 : 0;
        }

        public static float GetCombatEvasionChance(PlayerState defenderOwner, int slotIdx, MonsterInstance defenderM)
        {
            if (defenderOwner == null || defenderOwner.activeLeader == null || defenderM == null) return 0f;
            int sid = ResolveSpiritEffectId(defenderOwner.activeLeader);
            return sid switch
            {
                452 => IsTribe(defenderM, "Ghosts") ? 0.22f : 0f,
                458 => IsTribe(defenderM, "Mages") ? 0.20f : 0f,
                464 => IsTribe(defenderM, "Elves") ? 0.20f : 0f,
                _ => 0f
            };
        }

        public static int GetCombatIncomingReduction(PlayerState defenderOwner, MonsterInstance defenderM)
        {
            if (defenderOwner == null || defenderOwner.activeLeader == null || defenderM == null) return 0;
            int sid = ResolveSpiritEffectId(defenderOwner.activeLeader);
            return sid switch
            {
                455 => IsTribe(defenderM, "Celestials") ? 4 : 0,
                460 => IsTribe(defenderM, "Warriors") ? 4 : 0,
                465 => IsTribe(defenderM, "Dwarves") ? 4 : 0,
                _ => 0
            };
        }

        public static int ModifyOutgoingCombatAttack(PlayerState attackerOwner, MonsterInstance attackerM, MonsterInstance defenderM, int atkPower)
        {
            if (attackerOwner == null || attackerOwner.activeLeader == null || attackerM == null) return atkPower;
            int sid = ResolveSpiritEffectId(attackerOwner.activeLeader);

            if (sid == 459 && IsTribe(attackerM, "Warriors") && defenderM != null)
            {
                atkPower += 4;
            }
            else if (sid == 462 && IsTribe(attackerM, "Beasts") && UnityEngine.Random.value < 0.20f)
            {
                atkPower = Mathf.RoundToInt(atkPower * 1.40f);
            }
            else if (sid == 470 && IsTribe(attackerM, "Insects") && defenderM != null && defenderM.poisonDmg > 0)
            {
                atkPower += 4;
            }
            return Mathf.Max(0, atkPower);
        }

        public static void OnCombatHitDealt(PlayerState attackerOwner, MonsterInstance attackerM, PlayerState defenderOwner, MonsterInstance defenderM, int dmgDealt)
        {
            if (attackerM == null || defenderM == null) return;

            if (attackerOwner != null && attackerOwner.activeLeader != null)
            {
                int attSid = ResolveSpiritEffectId(attackerOwner.activeLeader);
                if (attSid == 453 && IsTribe(attackerM, "Demons"))
                {
                    defenderM.burnDmg = Mathf.Max(defenderM.burnDmg, 4);
                }
                else if (attSid == 454 && IsTribe(attackerM, "Demons"))
                {
                    attackerM.HealDefense(Mathf.Max(2, Mathf.RoundToInt(dmgDealt * 0.20f)));
                }
                else if (attSid == 469 && IsTribe(attackerM, "Insects"))
                {
                    defenderM.poisonDmg = Mathf.Max(defenderM.poisonDmg, 4);
                }
                else if (attSid == 470 && IsTribe(attackerM, "Insects"))
                {
                    defenderM.poisonDmg += 2;
                }
            }

            if (defenderOwner != null && defenderOwner.activeLeader != null && attackerM.IsAlive)
            {
                int defSid = ResolveSpiritEffectId(defenderOwner.activeLeader);
                if (defSid == 460 && IsTribe(defenderM, "Warriors") && IsRivalTribe(attackerM, "Warriors"))
                {
                    attackerM.TakeDamage(3);
                }
                else if ((defSid == 466 && IsTribe(defenderM, "Dwarves")) ||
                         (defSid == 468 && IsTribe(defenderM, "Giants")))
                {
                    attackerM.TakeDamage(4);
                }
            }
        }

        public static void OnMonsterDied(PlayerState deadOwner, PlayerState killerOwner, MonsterInstance deadM, MonsterInstance killerM)
        {
            if (deadM == null) return;

            // Archlich Valkor (451): any death hits Enemy Leader & heals weakest allied Ghost
            var gm = GameManager.Instance;
            if (gm != null)
            {
                ApplyValkorDeathHarvest(gm.humanPlayer, gm.aiPlayer);
                ApplyValkorDeathHarvest(gm.aiPlayer, gm.humanPlayer);
            }

            // Crag Titan Kronos (467): allied Giant death triggers 12 seismic damage
            if (deadOwner != null && deadOwner.activeLeader != null && ResolveSpiritEffectId(deadOwner.activeLeader) == 467 && IsTribe(deadM, "Giants"))
            {
                var opp = (deadOwner == gm?.humanPlayer) ? gm?.aiPlayer : gm?.humanPlayer;
                HitEnemyLeader(opp, 12, "🗿 TITANFALL", MonsterArtLoader.GetSpiritCategoryColor(deadOwner.activeLeader, IsOwnerHuman(deadOwner)));
            }

            // Ashmane Fenrir (461): allied Beast killer gains +4 ATK & +6 DEF
            if (killerOwner != null && killerOwner.activeLeader != null && ResolveSpiritEffectId(killerOwner.activeLeader) == 461
                && killerM != null && killerM.IsAlive && IsTribe(killerM, "Beasts"))
            {
                killerM.bonusAtk += 4;
                killerM.HealDefense(6);
                if (killerM.currentSlot != null)
                {
                    SpawnSlotFloating(IsOwnerHuman(killerOwner), killerM.currentSlot.slotIndex, "🐺 +4/+6 KILL FEAST", MonsterArtLoader.GetSpiritCategoryColor(killerOwner.activeLeader, IsOwnerHuman(killerOwner)));
                }
            }
        }

        private static void ApplyValkorDeathHarvest(PlayerState possessor, PlayerState enemy)
        {
            if (possessor == null || possessor.activeLeader == null) return;
            if (ResolveSpiritEffectId(possessor.activeLeader) != 451) return;

            Color col = MonsterArtLoader.GetSpiritCategoryColor(possessor.activeLeader, IsOwnerHuman(possessor));
            HitEnemyLeader(enemy, 6, "💀 SOUL HARVEST", col);
            var weakestGhost = FindWeakestAlly(FilterTribeSlots(GetOccupiedSlots(possessor), "Ghosts"));
            if (weakestGhost != null)
            {
                weakestGhost.currentMonster.HealDefense(5);
                SpawnSlotFloating(IsOwnerHuman(possessor), weakestGhost.slotIndex, "💀 +5 DEF", col);
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

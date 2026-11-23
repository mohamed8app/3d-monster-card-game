using System;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;
using MonsterCardGame.UI;

namespace MonsterCardGame.Core
{
    /// <summary>
    /// V2 Master Mechanics Engine implementing the 1,470-Card Database (IDs 1..1470)
    /// and the 37 Constitution Rules (Strict Integer Rounding, 60 Owner-Only Leaders,
    /// 100 Right-Wing Altar Covenants, 300 Spells incl. 4 Counter-Tech Spells,
    /// 1,800 Monster Passives/Actives, Barrier 0-ATK Lock, All-Attack Lock, and Turn 40 Sudden Death).
    /// </summary>
    public static class V2CardMechanicsEngine
    {
        [Serializable]
        public class V2OpItem
        {
            public string kind = "";
            public int cost = 1;
            public int v1 = 0;
            public int v2 = 0;
            public int pct = 0;
            public int cnt = 1;
            public int count = 1;
            public int cap = 99;
            public int max_cost = 99;
            public int draw = 0;
            public int dmg = 0;
            public int burn = 0;
            public int weaken = 0;
            public int atk = 0;
            public int deff = 0;
            public int hp = 0;
            public int heal = 0;
            public int heal_hp = 0;
            public int self_def = 0;
            public int self_dmg = 0;
            public int leech = 0;
            public int reflect = 0;
            public int ev = 0;
            public int mana = 0;
            public int dur = 1;
            public int exec_below = 0;
            public int disc = 0;
            public int buff = 0;
            public int poison = 0;
            public int bleed = 0;
            public int reflect_pct = 0;
            public int grave_mult = 0;
            public int board_atk_cap = 0;
            public string fx = "";
            public bool perm = false;
            public bool cleanse = false;
            public bool root = false;
            public bool silence = false;
            public bool silence_all = false;
            public bool root_all = false;
            public bool cleave = false;
            public bool center_aoe = false;
            public bool face_match = false;
            public bool req_skill = false;
            public bool disc_spells_to_1 = false;
            public bool dbl = false;
            public bool haste = false;
            public bool taunt = false;
            public bool stealth = false;
            public bool imm = false;
            public bool double_burn = false;
            public bool enemy_only = false;
            public bool to_board = false;
            public bool refresh = false;
            public bool from_enemy = false;
            public bool enemy = false;
            public bool sac = false;
            public int fallback_dmg = 0;
            public int[] spawn = null;
        }

        [Serializable]
        public class V2MonsterOps
        {
            public bool is_barrier = false;
            public bool is_all_attack = false;
            public List<V2OpItem> passives = new List<V2OpItem>();
            public List<V2OpItem> actives = new List<V2OpItem>();
        }

        [Serializable]
        public class V2LeaderOps
        {
            public int tribe = -1;
            public int style = 0;
            public int aura_atk = 0;
            public int aura_def = 0;
            public int aura_heal_def = 0;
            public int aura_heal_hp = 0;
            public int aura_cleanse_turn = 0;
            public int aura_banner_token = 0;
            public int aura_evasion = 0;
            public int aura_reflect = 0;
            public int aura_dmg_reduce = 0;
            public int aura_leech = 0;
            public int aura_burn = 0;
            public int aura_poison = 0;
            public int aura_bleed = 0;
            public int aura_cleave_pct = 0;
            public int aura_pierce_pct = 0;
            public int aura_anti_barrier = 0;
            public int aura_center_atk = 0;
            public int aura_flank_atk = 0;
            public int aura_open_lane_atk = 0;
            public int aura_pack_atk = 0;
            public int aura_grave_atk = 0;
            public int aura_low_hp_atk = 0;
            public int aura_high_hp_atk = 0;
            public int aura_skill_disc = 0;
            public int aura_skill_power = 0;
            public int aura_spell_power = 0;
            public int aura_spell_ping = 0;
            public int aura_death_ping = 0;
            public int aura_weaken_opp = 0;
            public int aura_survive_atk = 0;
            public int aura_kill_heal = 0;
        }

        [Serializable]
        public class V2CovenantOps
        {
            public int style = 0;
            public int tier = 1;
            public int self_hp_turn = 0;
            public int self_hp_cap = 0;
            public int self_hp_init_loss = 0;
            public int self_hp_on_attack = 0;
            public int self_atk_pen = 0;
            public int self_def_pen = 0;
            public int self_atk_pct_pen = 0;
            public int self_def_pct_pen = 0;
            public int ally_dmg_eot = 0;
            public bool ban_self_spells = false;
            public bool ban_face_attack = false;
            public bool ban_face_spell = false;
            public bool ban_normal_attack = false;
            public bool ban_self_heal = false;
            public bool ban_self_extra_draw = false;
            public bool ban_skill_gt1 = false;
            public int monster_tax_high = 0;
            public int monster_tax_all = 0;
            public int spell_tax_all = 0;
            public bool discard_hand_eot = false;
            public bool discard_cheapest_2t = false;
            public bool single_attacker_turn = false;
            public int max_plays_turn = 99;
            public int max_summons_turn = 99;
            public int max_board_self = 6;
            public int max_hand_self = 7;
            public int max_mana_self = 0;
            public int turn_limit_lose = 0;
            public int self_def_cap = 0;
            public int rew_atk = 0;
            public int rew_def = 0;
            public int rew_hp_turn = 0;
            public int rew_burn = 0;
            public int rew_poison = 0;
            public int rew_bleed = 0;
            public int rew_reflect = 0;
            public int rew_reflect_pct = 0;
            public int rew_leech_pct = 0;
            public bool rew_haste = false;
            public bool rew_taunt = false;
            public bool rew_cleanse = false;
            public int rew_extra_draw = 0;
            public int rew_token_turn = 0;
            public int rew_skill_disc = 0;
            public int rew_spell_disc = 0;
            public int rew_spell_power = 0;
            public int rew_skill_power = 0;
            public int rew_temp_mana = 0;
            public bool rew_double_skill = false;
            public int rew_stat_pct = 0;
            public int rew_enemy_face = 0;
            public int ban_hand_monster_ge = 99;
            public bool ban_self_dots = false;
            public bool ban_self_revive = false;
            public bool delay_leader_turn5 = false;
            public int rew_disrupt_eot = 0;
            public int rew_dmg_reduce = 0;
            public int rew_enemy_mana_loss = 0;
            public int rew_revive_disc = 0;
            public int rew_steal_copy = 0;
            public bool sac_every_2nd = false;
            public int self_hp_extra_spell_dmg = 0;
            public int self_mana_tax_if_lt2 = 0;
            public bool self_minor_restriction = false;
            public bool skip_every_3rd = false;
        }

        private static readonly Dictionary<int, V2MonsterOps> monsterOpsCache = new Dictionary<int, V2MonsterOps>();
        private static readonly Dictionary<int, V2LeaderOps> leaderOpsCache = new Dictionary<int, V2LeaderOps>();
        private static readonly Dictionary<int, V2CovenantOps> covenantOpsCache = new Dictionary<int, V2CovenantOps>();
        private static readonly Dictionary<int, V2OpItem> spellOpsCache = new Dictionary<int, V2OpItem>();

        /// <summary>
        /// V2 Constitution Rule #16: Strict Integer Rounding for all percentage calculations.
        /// </summary>
        public static int StrictRoundPct(int baseValue, int pct)
        {
            return Mathf.RoundToInt(baseValue * pct / 100f);
        }

        public static V2MonsterOps GetMonsterOps(CardData card)
        {
            if (card == null || string.IsNullOrEmpty(card.v2_ops_json)) return null;
            if (monsterOpsCache.TryGetValue(card.id, out var cached)) return cached;
            try
            {
                var parsed = JsonUtility.FromJson<V2MonsterOps>(card.v2_ops_json);
                monsterOpsCache[card.id] = parsed;
                return parsed;
            }
            catch
            {
                return null;
            }
        }

        public static V2OpItem GetV2ActiveOp(MonsterInstance unit, int skillIndex)
        {
            if (unit == null || unit.data == null || skillIndex < 0) return null;
            var mops = GetMonsterOps(unit.data);
            if (mops == null || mops.actives == null || skillIndex >= mops.actives.Count) return null;
            var op = mops.actives[skillIndex];
            return (op != null && !string.IsNullOrEmpty(op.kind)) ? op : null;
        }

        public static bool UnitHasPassiveKind(MonsterInstance unit, string kind)
        {
            if (unit == null || unit.data == null || string.IsNullOrEmpty(kind)) return false;
            var mops = GetMonsterOps(unit.data);
            if (mops == null || mops.passives == null) return false;
            for (int i = 0; i < mops.passives.Count; i++)
            {
                var p = mops.passives[i];
                if (p != null && string.Equals(p.kind, kind, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static V2LeaderOps GetLeaderOps(CardData card)
        {
            if (card == null || string.IsNullOrEmpty(card.v2_ops_json)) return null;
            if (leaderOpsCache.TryGetValue(card.id, out var cached)) return cached;
            try
            {
                var parsed = JsonUtility.FromJson<V2LeaderOps>(card.v2_ops_json);
                leaderOpsCache[card.id] = parsed;
                return parsed;
            }
            catch
            {
                return null;
            }
        }

        public static V2CovenantOps GetCovenantOps(CardData card)
        {
            if (card == null || string.IsNullOrEmpty(card.v2_ops_json)) return null;
            if (covenantOpsCache.TryGetValue(card.id, out var cached)) return cached;
            try
            {
                var parsed = JsonUtility.FromJson<V2CovenantOps>(card.v2_ops_json);
                covenantOpsCache[card.id] = parsed;
                return parsed;
            }
            catch
            {
                return null;
            }
        }

        public static V2OpItem GetSpellOps(CardData card)
        {
            if (card == null || string.IsNullOrEmpty(card.v2_ops_json)) return null;
            if (spellOpsCache.TryGetValue(card.id, out var cached)) return cached;
            try
            {
                var parsed = JsonUtility.FromJson<V2OpItem>(card.v2_ops_json);
                spellOpsCache[card.id] = parsed;
                return parsed;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsLeaderActiveForPlayer(PlayerState p)
        {
            if (p == null || p.activeLeader == null || p.leaderExorcised || p.leaderMutedTurns > 0) return false;
            if (IsCovenantActiveForPlayer(p))
            {
                var cov = GetCovenantOps(p.activeCovenant);
                if (cov != null && cov.delay_leader_turn5 && GameManager.Instance != null && GameManager.Instance.currentTurn < 5)
                    return false;
            }
            return true;
        }

        public static bool IsCovenantActiveForPlayer(PlayerState p)
        {
            return p != null && p.activeCovenant != null && !p.covenantDestroyed && p.covenantMutedTurns <= 0;
        }

        public static bool DoesUnitMatchOwnerLeader(MonsterInstance unit, PlayerState owner)
        {
            if (unit == null || unit.data == null || !IsLeaderActiveForPlayer(owner)) return false;
            if (unit.IsBarrier) return false;
            if (unit.HasPassiveSubstr("Chameleon") != null) return true;
            return !string.IsNullOrEmpty(owner.activeLeader.tribe) &&
                   string.Equals(unit.data.tribe, owner.activeLeader.tribe, StringComparison.OrdinalIgnoreCase);
        }

        public static int GetPlayerManaCap(PlayerState p)
        {
            if (IsCovenantActiveForPlayer(p))
            {
                var cov = GetCovenantOps(p.activeCovenant);
                if (cov != null && cov.max_mana_self > 0 && cov.max_mana_self < 10)
                    return cov.max_mana_self;
            }
            return 999; // Unbounded Aether (no global cap!)
        }

        public static bool IsSelfHealBanned(PlayerState p)
        {
            if (IsCovenantActiveForPlayer(p))
            {
                var cov = GetCovenantOps(p.activeCovenant);
                if (cov != null && cov.ban_self_heal) return true;
            }
            return false;
        }

        public static bool IsExtraDrawBanned(PlayerState p)
        {
            if (IsCovenantActiveForPlayer(p))
            {
                var cov = GetCovenantOps(p.activeCovenant);
                if (cov != null && cov.ban_self_extra_draw) return true;
            }
            return false;
        }

        public static bool IsSelfDotsBanned(PlayerState p)
        {
            if (IsCovenantActiveForPlayer(p))
            {
                var cov = GetCovenantOps(p.activeCovenant);
                if (cov != null && cov.ban_self_dots) return true;
            }
            return false;
        }

        public static bool IsSelfReviveBanned(PlayerState p)
        {
            if (IsCovenantActiveForPlayer(p))
            {
                var cov = GetCovenantOps(p.activeCovenant);
                if (cov != null && cov.ban_self_revive) return true;
            }
            return false;
        }

        public static int GetCovenantIncomingReduction(PlayerState p)
        {
            if (IsCovenantActiveForPlayer(p))
            {
                var cov = GetCovenantOps(p.activeCovenant);
                if (cov != null && cov.rew_dmg_reduce > 0) return cov.rew_dmg_reduce;
            }
            return 0;
        }

        public static int GetCovenantReviveDiscount(PlayerState p)
        {
            if (IsCovenantActiveForPlayer(p))
            {
                var cov = GetCovenantOps(p.activeCovenant);
                if (cov != null && cov.rew_revive_disc > 0) return cov.rew_revive_disc;
            }
            return 0;
        }

        public static bool HasMatchingTribeAlly(PlayerState owner)
        {
            if (!IsLeaderActiveForPlayer(owner) || owner.slots == null) return false;
            foreach (var s in owner.slots)
            {
                if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive &&
                    DoesUnitMatchOwnerLeader(s.currentMonster, owner))
                {
                    return true;
                }
            }
            return false;
        }

        public static void SyncLeaderDefAura(PlayerState player)
        {
            if (player == null || player.slots == null) return;
            int targetDef = 0;
            if (IsLeaderActiveForPlayer(player))
            {
                var lops = GetLeaderOps(player.activeLeader);
                if (lops != null && lops.aura_def > 0) targetDef = lops.aura_def;
            }
            foreach (var s in player.slots)
            {
                if (s == null || !s.IsOccupied || s.currentMonster == null || !s.currentMonster.IsAlive) continue;
                var m = s.currentMonster;
                int want = (targetDef > 0 && DoesUnitMatchOwnerLeader(m, player)) ? targetDef : 0;
                int diff = want - m.leaderDefApplied;
                if (diff > 0)
                {
                    m.baseDef += diff;
                    m.currentDef += diff;
                    m.leaderDefApplied = want;
                }
                else if (diff < 0)
                {
                    m.baseDef = Mathf.Max(1, m.baseDef + diff);
                    m.currentDef = Mathf.Max(1, Mathf.Min(m.currentDef, m.baseDef + m.permBonusDef));
                    m.leaderDefApplied = want;
                }
            }
        }

        public static int GetUnitPiercePct(MonsterInstance unit, PlayerState owner = null)
        {
            if (unit == null || unit.data == null) return 0;
            int best = 0;
            if (unit.silencedTurns <= 0)
            {
                var mops = GetMonsterOps(unit.data);
                if (mops != null && mops.passives != null)
                {
                    for (int i = 0; i < mops.passives.Count; i++)
                    {
                        var p = mops.passives[i];
                        if (p == null) continue;
                        if (string.Equals(p.kind, "pierce", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.kind, "bypass", StringComparison.OrdinalIgnoreCase))
                        {
                            int pct = p.pct > 0 ? p.pct : (p.v1 > 0 ? p.v1 : 25);
                            if (pct > best) best = pct;
                        }
                    }
                }
            }
            if (owner != null && DoesUnitMatchOwnerLeader(unit, owner))
            {
                var lops = GetLeaderOps(owner.activeLeader);
                if (lops != null && lops.aura_pierce_pct > 0)
                    best += lops.aura_pierce_pct;
            }
            return best;
        }

        public static int GetCardCostDelta(PlayerState p, CardData card)
        {
            if (p == null || card == null || !IsCovenantActiveForPlayer(p)) return 0;
            var cov = GetCovenantOps(p.activeCovenant);
            if (cov == null) return 0;
            int delta = 0;
            if (card.cardType == CardType.Monster)
            {
                delta += cov.monster_tax_all;
                if (card.cost >= 5) delta += cov.monster_tax_high;
            }
            else if (card.cardType == CardType.Spell)
            {
                delta += cov.spell_tax_all;
                delta -= cov.rew_spell_disc;
            }
            return delta;
        }

        public static bool CanPlayerSummonMonster(PlayerState p, CardData card, out string reason)
        {
            reason = string.Empty;
            if (p == null || card == null) return false;
            if (IsCovenantActiveForPlayer(p))
            {
                var cov = GetCovenantOps(p.activeCovenant);
                if (cov != null)
                {
                    int curTurn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 1;
                    if (cov.skip_every_3rd && curTurn % 3 == 0)
                    {
                        reason = $"Covenant [{p.activeCovenant.name}] forbids plays on every 3rd turn!";
                        return false;
                    }
                    if (cov.ban_hand_monster_ge > 0 && cov.ban_hand_monster_ge < 99 && card.cost >= cov.ban_hand_monster_ge)
                    {
                        reason = $"Covenant [{p.activeCovenant.name}] forbids summoning monsters costing {cov.ban_hand_monster_ge}+ Mana!";
                        return false;
                    }
                    if (p.cardsPlayedThisTurn >= cov.max_plays_turn)
                    {
                        reason = $"Covenant [{p.activeCovenant.name}] limits you to {cov.max_plays_turn} card play(s) per turn!";
                        return false;
                    }
                    if (p.monstersSummonedThisTurn >= cov.max_summons_turn)
                    {
                        reason = $"Covenant [{p.activeCovenant.name}] limits you to {cov.max_summons_turn} summon(s) per turn!";
                        return false;
                    }
                    int occupied = 0;
                    if (p.slots != null)
                    {
                        foreach (var s in p.slots) if (s != null && s.IsOccupied) occupied++;
                    }
                    if (occupied >= cov.max_board_self)
                    {
                        reason = $"Covenant [{p.activeCovenant.name}] caps your board at {cov.max_board_self} occupied slots!";
                        return false;
                    }
                }
            }
            return true;
        }

        public static int GetUnitAuraAndCovenantAtkDelta(MonsterInstance unit, PlayerState owner)
        {
            if (unit == null || owner == null || unit.IsBarrier || unit.IsAllAttack) return 0;
            int delta = 0;

            // 1. Owner-Only Pure Continuous Tribal Leader ATK Auras (V2.3)
            if (DoesUnitMatchOwnerLeader(unit, owner))
            {
                var lops = GetLeaderOps(owner.activeLeader);
                if (lops != null)
                {
                    int sign = owner.leaderBetrayedTurns > 0 ? -1 : 1;
                    int slotIdx = unit.currentSlot != null ? unit.currentSlot.slotIndex : -1;
                    int leaderAtk = lops.aura_atk;
                    if (lops.aura_center_atk > 0 && (slotIdx == 2 || slotIdx == 3))
                        leaderAtk += lops.aura_center_atk;
                    if (lops.aura_flank_atk > 0 && (slotIdx == 0 || slotIdx == 1 || slotIdx == 4 || slotIdx == 5))
                        leaderAtk += lops.aura_flank_atk;
                    if (lops.aura_open_lane_atk > 0 && slotIdx >= 0 && slotIdx < 6 && GameManager.Instance != null)
                    {
                        var opp = (owner == GameManager.Instance.humanPlayer) ? GameManager.Instance.aiPlayer : GameManager.Instance.humanPlayer;
                        if (opp != null && opp.slots != null && slotIdx < opp.slots.Length &&
                            (opp.slots[slotIdx] == null || !opp.slots[slotIdx].IsOccupied))
                        {
                            leaderAtk += lops.aura_open_lane_atk;
                        }
                    }
                    if (lops.aura_pack_atk > 0 && owner.slots != null)
                    {
                        int tCnt = 0;
                        foreach (var s in owner.slots)
                            if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive &&
                                DoesUnitMatchOwnerLeader(s.currentMonster, owner)) tCnt++;
                        if (tCnt >= 3) leaderAtk += lops.aura_pack_atk;
                    }
                    if (lops.aura_grave_atk > 0 && owner.graveyard != null && !string.IsNullOrEmpty(owner.activeLeader.tribe))
                    {
                        int gCnt = 0;
                        foreach (var gc in owner.graveyard)
                            if (gc != null && string.Equals(gc.tribe, owner.activeLeader.tribe, StringComparison.OrdinalIgnoreCase)) gCnt++;
                        leaderAtk += Mathf.Min(lops.aura_grave_atk, gCnt / 2);
                    }
                    if (lops.aura_low_hp_atk > 0 && owner.currentHp <= 220)
                        leaderAtk += lops.aura_low_hp_atk;
                    if (lops.aura_high_hp_atk > 0 && owner.currentHp >= 280)
                        leaderAtk += lops.aura_high_hp_atk;
                    delta += sign * leaderAtk;
                }
            }

            // 2. Right-Wing Altar Covenant ATK Reward & Self-Penalty
            if (IsCovenantActiveForPlayer(owner))
            {
                var cov = GetCovenantOps(owner.activeCovenant);
                if (cov != null)
                {
                    delta += cov.rew_atk;
                    delta -= cov.self_atk_pen;
                    if (cov.rew_stat_pct > 0)
                        delta += StrictRoundPct(unit.baseAtk, cov.rew_stat_pct);
                    if (cov.self_atk_pct_pen > 0)
                        delta -= StrictRoundPct(unit.baseAtk, cov.self_atk_pct_pen);
                }
            }

            return delta;
        }

        public static void ApplyV2OnMonsterSummoned(PlayerState player, PlayerState opponent, int slotIdx, MonsterInstance instance)
        {
            if (player == null || instance == null || instance.data == null) return;

            // 1. Leader Owner-Only Pure Continuous DEF Aura on summon
            if (DoesUnitMatchOwnerLeader(instance, player))
            {
                var lops = GetLeaderOps(player.activeLeader);
                if (lops != null && lops.aura_def > 0)
                {
                    instance.baseDef += lops.aura_def;
                    instance.currentDef += lops.aura_def;
                    instance.leaderDefApplied = lops.aura_def;
                }
            }

            // 1.5 Shared Environment Bespoke On-Summon Effects (Home Element Bonus + Ice Lv2+ Foreign Haste Suppress)
            var activeEnv = GameManager.Instance != null ? GameManager.Instance.activeEnvironment : null;
            if (activeEnv != null && instance.data != null)
            {
                int eIdx = Mathf.Clamp(activeEnv.level - 1, 0, 2);
                bool sameEl = EnvironmentCardData.AreSameElement(activeEnv.element, instance.data.element);
                if (sameEl)
                {
                    if (EnvironmentCardData.AreSameElement(activeEnv.element, "Water"))
                    {
                        int ed = new[] { 3, 5, 7 }[eIdx];
                        instance.baseDef += ed; instance.currentDef += ed;
                    }
                    else if (EnvironmentCardData.AreSameElement(activeEnv.element, "Nature"))
                    {
                        int ed = new[] { 2, 4, 6 }[eIdx];
                        instance.baseDef += ed; instance.currentDef += ed;
                    }
                    else if (EnvironmentCardData.AreSameElement(activeEnv.element, "Earth"))
                    {
                        int ed = new[] { 3, 6, 9 }[eIdx];
                        instance.baseDef += ed; instance.currentDef += ed;
                    }
                    else if (EnvironmentCardData.AreSameElement(activeEnv.element, "Air") && !instance.IsBarrier)
                    {
                        instance.canAttack = true;
                    }
                }
                else if (EnvironmentCardData.AreSameElement(activeEnv.element, "Ice") && activeEnv.level >= 2)
                {
                    instance.canAttack = false;
                }
            }

            // 2. Covenant DEF Reward / Penalty / Keywords
            if (IsCovenantActiveForPlayer(player))
            {
                var cov = GetCovenantOps(player.activeCovenant);
                if (cov != null)
                {
                    if (cov.rew_def > 0) instance.AddBonusDef(cov.rew_def);
                    if (cov.rew_stat_pct > 0) instance.AddBonusDef(StrictRoundPct(instance.baseDef, cov.rew_stat_pct));
                    if (cov.self_def_pen > 0)
                        instance.currentDef = Mathf.Max(1, instance.currentDef - cov.self_def_pen);
                    if (cov.self_def_pct_pen > 0)
                        instance.currentDef = Mathf.Max(1, instance.currentDef - StrictRoundPct(instance.currentDef, cov.self_def_pct_pen));
                    if (cov.self_def_cap > 0)
                    {
                        instance.baseDef = Mathf.Min(instance.baseDef, cov.self_def_cap);
                        instance.currentDef = Mathf.Min(instance.currentDef, cov.self_def_cap);
                    }
                    if (cov.rew_haste && !instance.IsBarrier)
                        instance.canAttack = true;
                    if (cov.rew_double_skill && !instance.IsBarrier)
                        instance.doubleSkillOneTurn = true;
                }
            }

            // 3. Monster V2 Structured Summon Passives
            var mops = GetMonsterOps(instance.data);
            if (mops != null && mops.passives != null && instance.silencedTurns <= 0)
            {
                foreach (var p in mops.passives)
                {
                    if (p == null) continue;
                    switch (p.kind)
                    {
                        case "haste":
                            if (!instance.IsBarrier)
                            {
                                instance.canAttack = true;
                                if (p.v1 > 0 && !instance.IsAllAttack) instance.tempTurnAtk += p.v1;
                            }
                            break;
                        case "on_summon_heal":
                            player.HealLeader(Mathf.Max(4, p.v1), opponent);
                            break;
                        case "on_summon_draw":
                        case "summon_draw":
                            if (!IsExtraDrawBanned(player))
                            {
                                int dCnt = Mathf.Max(1, p.v1);
                                for (int i = 0; i < dCnt; i++) player.DrawCard();
                            }
                            break;
                        case "on_summon_dmg":
                            if (opponent != null && slotIdx >= 0 && slotIdx < opponent.slots.Length)
                            {
                                var es = opponent.slots[slotIdx];
                                if (es != null && es.IsOccupied && es.currentMonster != null && es.currentMonster.IsAlive)
                                {
                                    es.currentMonster.TakeDamage(Mathf.Max(4, p.v1));
                                    if (!es.currentMonster.IsAlive)
                                    {
                                        opponent.graveyard.Add(es.currentMonster.data);
                                        es.ClearSlot();
                                    }
                                }
                            }
                            break;
                        case "flank_bonus":
                            if (slotIdx == 0 || slotIdx == 1 || slotIdx == 4 || slotIdx == 5)
                                instance.ApplyPermBuff(p.v1, p.v2);
                            break;
                        case "center_bonus":
                            if (slotIdx == 2 || slotIdx == 3)
                                instance.ApplyPermBuff(p.v1, p.v2);
                            break;
                    }
                }
            }
        }

        public static void ApplyV2LeaderOnSummon(PlayerState player, PlayerState opponent, CardData leaderCard)
        {
            if (player == null || leaderCard == null) return;
            player.leaderExorcised = false;
            player.leaderBetrayedTurns = 0;
            player.leaderMutedTurns = 0;

            // V2.3 Pure Continuous Tribal Ancestor Blessing: ZERO On-Summon burst!
            // Immediately synchronize continuous +DEF aura to all matching-Tribe allies on board.
            SyncLeaderDefAura(player);
        }

        public static void ApplyLeaderStartOfTurn(PlayerState activePlayer, PlayerState otherPlayer)
        {
            if (!IsLeaderActiveForPlayer(activePlayer)) return;
            var lops = GetLeaderOps(activePlayer.activeLeader);
            if (lops == null) return;

            if (lops.aura_heal_hp > 0 && HasMatchingTribeAlly(activePlayer))
                activePlayer.HealLeader(lops.aura_heal_hp, otherPlayer);

            if (lops.aura_heal_def > 0 && activePlayer.slots != null)
            {
                foreach (var s in activePlayer.slots)
                {
                    if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive &&
                        DoesUnitMatchOwnerLeader(s.currentMonster, activePlayer))
                    {
                        s.currentMonster.HealDefense(lops.aura_heal_def);
                    }
                }
            }

            if (lops.aura_cleanse_turn > 0 && activePlayer.slots != null)
            {
                MonsterInstance chosen = null;
                foreach (var s in activePlayer.slots)
                {
                    if (s == null || !s.IsOccupied || s.currentMonster == null || !s.currentMonster.IsAlive) continue;
                    var m = s.currentMonster;
                    if (!DoesUnitMatchOwnerLeader(m, activePlayer)) continue;
                    if (m.burnDmg > 0 || m.poisonDmg > 0 || m.bleedDmg > 0 || m.frozenTurns > 0 || m.silencedTurns > 0 || m.rootedTurns > 0 || m.weakenedTurns > 0 || m.weakenedAtkFlat > 0)
                    {
                        chosen = m;
                        break;
                    }
                }
                if (chosen == null)
                {
                    foreach (var s in activePlayer.slots)
                    {
                        if (s == null || !s.IsOccupied || s.currentMonster == null || !s.currentMonster.IsAlive) continue;
                        var m = s.currentMonster;
                        if (DoesUnitMatchOwnerLeader(m, activePlayer) && m.currentDef < m.baseDef + m.permBonusDef)
                        {
                            chosen = m;
                            break;
                        }
                    }
                }
                if (chosen != null)
                {
                    chosen.burnDmg = 0;
                    chosen.poisonDmg = 0;
                    chosen.bleedDmg = 0;
                    chosen.frozenTurns = 0;
                    chosen.silencedTurns = 0;
                    chosen.rootedTurns = 0;
                    chosen.weakenedTurns = 0;
                    chosen.weakenedAtkFlat = 0;
                    chosen.HealDefense(lops.aura_cleanse_turn);
                }
            }

            if (lops.aura_banner_token > 0 && activePlayer.slots != null)
            {
                bool boardEmpty = true;
                foreach (var s in activePlayer.slots)
                {
                    if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive)
                    {
                        boardEmpty = false;
                        break;
                    }
                }
                if (boardEmpty)
                {
                    var emptySlot = FindFirstEmptySlot(activePlayer);
                    if (emptySlot != null)
                    {
                        var tokData = new CardData
                        {
                            id = 0,
                            name = $"{activePlayer.activeLeader.tribe} Banner",
                            cardType = CardType.Monster,
                            card_type = "monster",
                            tribe = activePlayer.activeLeader.tribe ?? "",
                            element = "None",
                            cost = 1,
                            atk = lops.aura_banner_token,
                            def = lops.aura_banner_token + 2
                        };
                        var tokInst = new MonsterInstance(tokData, GameManager.Instance != null ? GameManager.Instance.currentTurn : 1);
                        emptySlot.PlaceMonster(tokInst);
                        SyncLeaderDefAura(activePlayer);
                    }
                }
            }
        }

        public static float GetLeaderEvasionChance(PlayerState defenderOwner, MonsterInstance defender)
        {
            if (!DoesUnitMatchOwnerLeader(defender, defenderOwner)) return 0f;
            var lops = GetLeaderOps(defenderOwner.activeLeader);
            return (lops != null && lops.aura_evasion > 0) ? (lops.aura_evasion / 100f) : 0f;
        }

        public static int GetLeaderIncomingReduction(PlayerState defenderOwner, MonsterInstance defender)
        {
            if (!DoesUnitMatchOwnerLeader(defender, defenderOwner)) return 0;
            var lops = GetLeaderOps(defenderOwner.activeLeader);
            return (lops != null && lops.aura_dmg_reduce > 0) ? lops.aura_dmg_reduce : 0;
        }

        public static int GetLeaderEnemyDreadPenalty(PlayerState opponent)
        {
            if (!HasMatchingTribeAlly(opponent)) return 0;
            var lops = GetLeaderOps(opponent.activeLeader);
            return (lops != null && lops.aura_weaken_opp > 0) ? lops.aura_weaken_opp : 0;
        }

        public static int GetLeaderSkillManaDiscount(PlayerState owner, MonsterInstance unit)
        {
            if (!DoesUnitMatchOwnerLeader(unit, owner)) return 0;
            var lops = GetLeaderOps(owner.activeLeader);
            return (lops != null && lops.aura_skill_disc > 0) ? lops.aura_skill_disc : 0;
        }

        public static int GetLeaderSkillPowerBonus(PlayerState owner, MonsterInstance unit)
        {
            if (!DoesUnitMatchOwnerLeader(unit, owner)) return 0;
            var lops = GetLeaderOps(owner.activeLeader);
            return (lops != null && lops.aura_skill_power > 0) ? lops.aura_skill_power : 0;
        }

        public static int GetLeaderSpellPowerBonus(PlayerState owner)
        {
            if (!HasMatchingTribeAlly(owner)) return 0;
            var lops = GetLeaderOps(owner.activeLeader);
            return (lops != null && lops.aura_spell_power > 0) ? lops.aura_spell_power : 0;
        }

        public static int ModifyLeaderOutgoingAttack(PlayerState attackerOwner, MonsterInstance attacker, MonsterInstance defender, int atkPower)
        {
            if (defender != null && DoesUnitMatchOwnerLeader(attacker, attackerOwner))
            {
                var lops = GetLeaderOps(attackerOwner.activeLeader);
                if (lops != null && lops.aura_anti_barrier > 0 &&
                    (defender.IsBarrier || defender.HasPassiveSubstr("Taunt") != null))
                {
                    atkPower += lops.aura_anti_barrier;
                }
            }
            return Mathf.Max(0, atkPower);
        }

        public static void OnLeaderCombatHitDealt(PlayerState attackerOwner, MonsterInstance attacker, PlayerState defenderOwner, MonsterInstance defender, int dmgDealt)
        {
            if (dmgDealt > 0 && attacker != null && DoesUnitMatchOwnerLeader(attacker, attackerOwner))
            {
                var lops = GetLeaderOps(attackerOwner.activeLeader);
                if (lops != null)
                {
                    if (lops.aura_leech > 0)
                        attackerOwner.HealLeader(Mathf.Max(1, StrictRoundPct(dmgDealt, lops.aura_leech)), defenderOwner);
                    if (defender != null && defender.IsAlive)
                    {
                        if (lops.aura_burn > 0) defender.burnDmg += lops.aura_burn;
                        if (lops.aura_poison > 0) defender.poisonDmg += lops.aura_poison;
                        if (lops.aura_bleed > 0) defender.bleedDmg += lops.aura_bleed;
                    }
                    if (lops.aura_cleave_pct > 0 && defender != null && defender.currentSlot != null && defenderOwner != null && defenderOwner.slots != null)
                    {
                        int cDmg = Mathf.Max(1, StrictRoundPct(dmgDealt, lops.aura_cleave_pct));
                        int dSlot = defender.currentSlot.slotIndex;
                        foreach (int adj in new[] { dSlot - 1, dSlot + 1 })
                        {
                            if (adj >= 0 && adj < defenderOwner.slots.Length &&
                                defenderOwner.slots[adj] != null && defenderOwner.slots[adj].IsOccupied &&
                                defenderOwner.slots[adj].currentMonster != null && defenderOwner.slots[adj].currentMonster.IsAlive)
                            {
                                var adjM = defenderOwner.slots[adj].currentMonster;
                                adjM.TakeDamage(cDmg);
                                if (!adjM.IsAlive)
                                    CombatSystem.Instance?.HandleMonsterDeath(defenderOwner, attackerOwner, adj, adjM, attacker);
                            }
                        }
                    }
                }
            }

            if (dmgDealt > 0 && defender != null && DoesUnitMatchOwnerLeader(defender, defenderOwner))
            {
                var dlops = GetLeaderOps(defenderOwner.activeLeader);
                if (dlops != null)
                {
                    if (dlops.aura_reflect > 0 && attacker != null && attacker.IsAlive)
                    {
                        attacker.TakeDamage(dlops.aura_reflect);
                        if (!attacker.IsAlive && attacker.currentSlot != null)
                            CombatSystem.Instance?.HandleMonsterDeath(attackerOwner, defenderOwner, attacker.currentSlot.slotIndex, attacker, defender);
                    }
                    if (dlops.aura_survive_atk > 0 && defender.IsAlive && !defender.IsBarrier && !defender.IsAllAttack)
                    {
                        defender.ApplyPermBuff(dlops.aura_survive_atk, 0);
                    }
                }
            }
        }

        public static void OnLeaderMonsterDied(PlayerState deadOwner, PlayerState killerOwner, MonsterInstance deadM, MonsterInstance killerM)
        {
            if (deadM != null && DoesUnitMatchOwnerLeader(deadM, deadOwner))
            {
                var lops = GetLeaderOps(deadOwner.activeLeader);
                if (lops != null && lops.aura_death_ping > 0)
                {
                    var gm = GameManager.Instance;
                    var opp = killerOwner ?? ((gm != null && deadOwner == gm.humanPlayer) ? gm.aiPlayer : gm?.humanPlayer);
                    opp?.TakeLeaderDamage(lops.aura_death_ping);
                }
            }
            if (killerM != null && killerM.IsAlive && killerOwner != null && killerOwner != deadOwner &&
                DoesUnitMatchOwnerLeader(killerM, killerOwner))
            {
                var klops = GetLeaderOps(killerOwner.activeLeader);
                if (klops != null && klops.aura_kill_heal > 0)
                {
                    killerM.HealDefense(klops.aura_kill_heal);
                }
            }
        }

        public static bool PlayCovenantCard(PlayerState player, CardData covCard)
        {
            if (player == null || covCard == null || covCard.cardType != CardType.Covenant) return false;
            var ops = GetCovenantOps(covCard);
            if (ops != null && ops.self_hp_init_loss > 0 && player.currentHp <= ops.self_hp_init_loss)
            {
                BattleUIManager.Instance?.LogCombatMessage($"❌ Insufficient Commander HP to activate Covenant [{covCard.name}]!");
                return false;
            }
            int effCost = player.GetEffectiveCardCost(covCard);
            if (effCost > player.currentMana)
            {
                BattleUIManager.Instance?.LogCombatMessage($"❌ Not enough Mana for Covenant [{covCard.name}]!");
                return false;
            }

            player.currentMana -= effCost;
            player.hand.Remove(covCard);
            player.cardsPlayedThisTurn++;
            if (player.activeCovenant != null)
                player.graveyard.Add(player.activeCovenant);

            player.activeCovenant = covCard;
            player.covenantDestroyed = false;
            player.covenantMutedTurns = 0;

            if (ops != null)
            {
                if (ops.self_hp_init_loss > 0)
                {
                    player.maxHp = Mathf.Max(50, player.maxHp - ops.self_hp_init_loss);
                    player.currentHp = Mathf.Min(player.currentHp, player.maxHp);
                }
                if (ops.self_hp_cap > 0)
                {
                    player.maxHp = Mathf.Min(player.maxHp, ops.self_hp_cap);
                    player.currentHp = Mathf.Min(player.currentHp, player.maxHp);
                }
                if (ops.max_hand_self > 0 && ops.max_hand_self < 7)
                {
                    player.maxHandSize = ops.max_hand_self;
                    while (player.hand.Count > player.maxHandSize)
                    {
                        var burned = player.hand[0];
                        player.hand.RemoveAt(0);
                        player.graveyard.Add(burned);
                    }
                }
                if (ops.self_def_cap > 0 && player.slots != null)
                {
                    foreach (var s in player.slots)
                    {
                        if (s != null && s.IsOccupied && s.currentMonster != null)
                        {
                            s.currentMonster.baseDef = Mathf.Min(s.currentMonster.baseDef, ops.self_def_cap);
                            s.currentMonster.currentDef = Mathf.Min(s.currentMonster.currentDef, ops.self_def_cap);
                        }
                    }
                }
            }

            BattleUIManager.Instance?.LogCombatMessage($"📜 [{player.playerName}] activated Right-Wing Altar Covenant [{covCard.name}]: {covCard.description}", covCard);
            BattleUIManager.Instance?.ShowBanner($"📜 ALTAR COVENANT: {covCard.name}", new Color(0.92f, 0.72f, 0.25f));
            BattleUIManager.Instance?.UpdateUI();
            return true;
        }

        public static bool TryExecuteV2Spell(PlayerState caster, PlayerState target, CardData spellCard, int targetSlotIdx)
        {
            if (caster == null || target == null || spellCard == null) return false;
            var op = GetSpellOps(spellCard);
            if (op == null || string.IsNullOrEmpty(op.kind)) return false;

            int spBonus = GetLeaderSpellPowerBonus(caster);
            if (IsCovenantActiveForPlayer(caster))
            {
                var cov = GetCovenantOps(caster.activeCovenant);
                if (cov != null)
                {
                    spBonus += cov.rew_spell_power;
                    if (cov.self_hp_extra_spell_dmg > 0)
                        caster.TakeLeaderDamage(cov.self_hp_extra_spell_dmg);
                }
            }
            int v1 = Mathf.Max(0, op.v1 + spBonus);
            int v2 = Mathf.Max(0, op.v2 + spBonus);

            switch (op.kind)
            {
                case "exorcism":
                case "counter_exorcism":
                    if (target.activeLeader != null)
                    {
                        target.graveyard.Add(target.activeLeader);
                        target.activeLeader = null;
                    }
                    target.leaderExorcised = true;
                    SyncLeaderDefAura(target);
                    BattleUIManager.Instance?.LogCombatMessage($"⚡ [Exorcism] Banished [{target.playerName}]'s Commander Spirit Talisman!", spellCard);
                    BattleUIManager.Instance?.UpdateLeaderSlotsUI();
                    break;

                case "betrayal":
                case "counter_betrayal":
                    target.leaderBetrayedTurns = 2;
                    BattleUIManager.Instance?.LogCombatMessage($"⚡ [Betrayal] Inverted [{target.playerName}]'s Commander Spirit Aura for 2 turns!", spellCard);
                    break;

                case "oblivion":
                case "counter_oblivion":
                    if (target.activeCovenant != null)
                    {
                        target.graveyard.Add(target.activeCovenant);
                        target.activeCovenant = null;
                    }
                    target.covenantDestroyed = true;
                    BattleUIManager.Instance?.LogCombatMessage($"⚡ [Oblivion] Shattered [{target.playerName}]'s Right-Wing Altar Covenant!", spellCard);
                    break;

                case "stand_alone":
                case "counter_stand_alone":
                    target.leaderMutedTurns = 2;
                    target.covenantMutedTurns = 2;
                    SyncLeaderDefAura(target);
                    BattleUIManager.Instance?.LogCombatMessage($"⚡ [Stand Alone] Muted [{target.playerName}]'s Spirit & Covenant for 2 turns!", spellCard);
                    break;

                case "dmg_single":
                case "cleave_enemy":
                case "melt_enemy_def":
                    var es = PickEnemySlot(target, targetSlotIdx);
                    if (es != null && es.IsOccupied && es.currentMonster != null)
                    {
                        if (op.kind == "melt_enemy_def")
                        {
                            int cut = Mathf.Max(1, StrictRoundPct(es.currentMonster.currentDef, op.pct > 0 ? op.pct : 50));
                            es.currentMonster.currentDef = Mathf.Max(1, es.currentMonster.currentDef - cut);
                            es.currentMonster.baseDef = Mathf.Max(1, es.currentMonster.baseDef - cut);
                            if (op.poison > 0) es.currentMonster.poisonDmg += op.poison;
                            if (op.bleed > 0) es.currentMonster.bleedDmg += op.bleed;
                            if (op.silence) es.currentMonster.silencedTurns = Mathf.Max(1, es.currentMonster.silencedTurns + Mathf.Max(1, op.dur));
                        }
                        else if (op.kind == "cleave_enemy" && target.slots != null)
                        {
                            int baseSl = es.slotIndex;
                            for (int sl = baseSl - 1; sl <= baseSl + 1; sl++)
                            {
                                if (sl < 0 || sl >= target.slots.Length || target.slots[sl] == null || !target.slots[sl].IsOccupied) continue;
                                var em = target.slots[sl].currentMonster;
                                if (em == null || !em.IsAlive) continue;
                                if (op.v1 > 0) em.TakeDamage(v1);
                                if (em.IsAlive) ApplySpellFxOnUnit(em, op);
                                if (!em.IsAlive) { target.graveyard.Add(em.data); target.slots[sl].ClearSlot(); }
                            }
                        }
                        else
                        {
                            if (op.v1 > 0) es.currentMonster.TakeDamage(v1);
                            if (es.currentMonster.IsAlive) ApplySpellFxOnUnit(es.currentMonster, op);
                            if (!es.currentMonster.IsAlive) { target.graveyard.Add(es.currentMonster.data); es.ClearSlot(); }
                        }
                    }
                    break;

                case "dmg_aoe":
                case "aoe_enemy":
                    if (target.slots != null)
                    {
                        int aoeDmg = op.v1 > 0 ? v1 : 0;
                        if (op.grave_mult > 0 && caster.graveyard != null)
                        {
                            int mCnt = 0;
                            foreach (var g in caster.graveyard) if (g != null && g.cardType == CardType.Monster) mCnt++;
                            aoeDmg = Mathf.Min(op.cap > 0 ? op.cap : 42, op.grave_mult * mCnt) + spBonus;
                        }
                        int hitCount = 0;
                        foreach (var s in target.slots)
                        {
                            if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive)
                            {
                                if (op.cnt > 1 && hitCount >= op.cnt) break;
                                hitCount++;
                                if (aoeDmg > 0) s.currentMonster.TakeDamage(aoeDmg);
                                if (s.currentMonster.IsAlive) ApplySpellFxOnUnit(s.currentMonster, op);
                                if (!s.currentMonster.IsAlive) { target.graveyard.Add(s.currentMonster.data); s.ClearSlot(); }
                            }
                        }
                        if (op.face_match && aoeDmg > 0) target.TakeLeaderDamage(aoeDmg);
                    }
                    break;

                case "dmg_leader":
                    if (op.board_atk_cap > 0 && caster.slots != null)
                    {
                        int bSum = 0;
                        foreach (var s in caster.slots)
                            if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive)
                                bSum += s.currentMonster.GetEffectiveAtk(GameManager.Instance != null ? GameManager.Instance.activeEnvironment : null, caster);
                        target.TakeLeaderDamage(Mathf.Min(op.board_atk_cap, bSum) + spBonus);
                    }
                    else
                    {
                        target.TakeLeaderDamage(Mathf.Max(8, v1));
                    }
                    break;

                case "reduce_enemy_max_mana":
                    target.power = Mathf.Max(0, target.power - Mathf.Max(1, op.v1));
                    if (target.slots != null)
                    {
                        foreach (var s in target.slots)
                        {
                            if (s == null || !s.IsOccupied || s.currentMonster == null || !s.currentMonster.IsAlive) continue;
                            if (op.silence_all) s.currentMonster.silencedTurns = Mathf.Max(s.currentMonster.silencedTurns, Mathf.Max(1, op.dur));
                            if (op.root_all) s.currentMonster.rootedTurns = Mathf.Max(s.currentMonster.rootedTurns, Mathf.Max(2, op.dur));
                        }
                    }
                    break;

                case "def_slam":
                    if (caster.slots != null)
                    {
                        int capV = op.cap > 0 ? op.cap : 99;
                        if (op.center_aoe)
                        {
                            int cSum = 0;
                            foreach (int ci in new[] { 2, 3 })
                                if (ci < caster.slots.Length && caster.slots[ci] != null && caster.slots[ci].IsOccupied && caster.slots[ci].currentMonster != null)
                                    cSum += caster.slots[ci].currentMonster.currentDef;
                            int slam = Mathf.Min(capV, StrictRoundPct(cSum, op.pct > 0 ? op.pct : 50)) + (cSum > 0 ? spBonus : 0);
                            if (slam > 0 && target.slots != null)
                            {
                                foreach (var s in target.slots)
                                    if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive)
                                    {
                                        s.currentMonster.TakeDamage(slam);
                                        if (!s.currentMonster.IsAlive) { target.graveyard.Add(s.currentMonster.data); s.ClearSlot(); }
                                    }
                                target.TakeLeaderDamage(slam);
                            }
                        }
                        else
                        {
                            int bestDef = 0;
                            foreach (var s in caster.slots)
                                if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.currentDef > bestDef)
                                    bestDef = s.currentMonster.currentDef;
                            int slam = Mathf.Min(capV, StrictRoundPct(bestDef, op.pct > 0 ? op.pct : 50)) + (bestDef > 0 ? spBonus : 0);
                            var tSl = PickEnemySlot(target, targetSlotIdx);
                            if (slam > 0 && tSl != null && target.slots != null)
                            {
                                if (op.cleave)
                                {
                                    for (int sl = tSl.slotIndex - 1; sl <= tSl.slotIndex + 1; sl++)
                                    {
                                        if (sl < 0 || sl >= target.slots.Length || target.slots[sl] == null || !target.slots[sl].IsOccupied) continue;
                                        target.slots[sl].currentMonster.TakeDamage(slam);
                                        if (!target.slots[sl].currentMonster.IsAlive) { target.graveyard.Add(target.slots[sl].currentMonster.data); target.slots[sl].ClearSlot(); }
                                    }
                                }
                                else if (tSl.currentMonster != null)
                                {
                                    tSl.currentMonster.TakeDamage(slam);
                                    if (!tSl.currentMonster.IsAlive) { target.graveyard.Add(tSl.currentMonster.data); tSl.ClearSlot(); }
                                }
                            }
                        }
                    }
                    break;

                case "random_bolts":
                {
                    int bolts = Mathf.Max(1, op.cnt > 0 ? op.cnt : 4);
                    int bdmg = Mathf.Max(1, (op.dmg > 0 ? op.dmg : 8) + spBonus);
                    for (int b = 0; b < bolts; b++)
                    {
                        var alive = new List<BoardSlot>();
                        if (target.slots != null)
                            foreach (var s in target.slots)
                                if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive) alive.Add(s);
                        if (alive.Count > 0 && UnityEngine.Random.value < 0.7f)
                        {
                            var pick = alive[UnityEngine.Random.Range(0, alive.Count)];
                            pick.currentMonster.TakeDamage(bdmg);
                            if (!pick.currentMonster.IsAlive) { target.graveyard.Add(pick.currentMonster.data); pick.ClearSlot(); }
                        }
                        else
                        {
                            target.TakeLeaderDamage(bdmg);
                        }
                    }
                    break;
                }

                case "trigger_lb":
                {
                    var lbSlot = PickAllySlot(caster, targetSlotIdx);
                    if (lbSlot != null && lbSlot.currentMonster != null)
                    {
                        caster.HealLeader(10, target);
                        target.TakeLeaderDamage(8 + spBonus);
                    }
                    break;
                }

                case "heal_leader":
                    caster.HealLeader(Mathf.Max(8, v1), target);
                    if (op.cleanse && caster.slots != null)
                    {
                        foreach (var s in caster.slots)
                        {
                            if (s != null && s.IsOccupied && s.currentMonster != null)
                            {
                                s.currentMonster.burnDmg = 0;
                                s.currentMonster.poisonDmg = 0;
                                s.currentMonster.bleedDmg = 0;
                                s.currentMonster.frozenTurns = 0;
                                s.currentMonster.silencedTurns = 0;
                            }
                        }
                    }
                    break;

                case "buff_ally":
                    var aslot = PickAllySlot(caster, targetSlotIdx);
                    if (aslot != null && aslot.IsOccupied && aslot.currentMonster != null)
                    {
                        aslot.currentMonster.ApplyPermBuff(Mathf.Max(2, v1), Mathf.Max(2, v2 > 0 ? v2 : v1));
                    }
                    break;

                case "buff_all_allies":
                case "aoe_ally":
                    if (caster.slots != null)
                    {
                        foreach (var s in caster.slots)
                        {
                            if (s != null && s.IsOccupied && s.currentMonster != null)
                                s.currentMonster.ApplyPermBuff(Mathf.Max(2, v1), Mathf.Max(2, v2 > 0 ? v2 : v1));
                        }
                    }
                    break;

                case "freeze_enemy":
                    var fs = PickEnemySlot(target, targetSlotIdx);
                    if (fs != null && fs.IsOccupied && fs.currentMonster != null)
                    {
                        fs.currentMonster.frozenTurns = Mathf.Max(1, fs.currentMonster.frozenTurns + 1);
                        if (v1 > 0) fs.currentMonster.TakeDamage(v1);
                        if (!fs.currentMonster.IsAlive)
                        {
                            target.graveyard.Add(fs.currentMonster.data);
                            fs.ClearSlot();
                        }
                    }
                    break;

                case "silence_enemy":
                    var ss = PickEnemySlot(target, targetSlotIdx);
                    if (ss != null && ss.IsOccupied && ss.currentMonster != null)
                    {
                        ss.currentMonster.silencedTurns = Mathf.Max(1, ss.currentMonster.silencedTurns + 1);
                        if (v1 > 0) ss.currentMonster.weakenedAtkFlat += v1;
                    }
                    break;

                case "burn_enemy":
                    var bs = PickEnemySlot(target, targetSlotIdx);
                    if (bs != null && bs.IsOccupied && bs.currentMonster != null)
                        bs.currentMonster.burnDmg += Mathf.Max(3, v1);
                    break;

                case "poison_enemy":
                    var ps = PickEnemySlot(target, targetSlotIdx);
                    if (ps != null && ps.IsOccupied && ps.currentMonster != null)
                        ps.currentMonster.poisonDmg += Mathf.Max(3, v1);
                    break;

                case "bleed_enemy":
                    var bls = PickEnemySlot(target, targetSlotIdx);
                    if (bls != null && bls.IsOccupied && bls.currentMonster != null)
                        bls.currentMonster.bleedDmg += Mathf.Max(3, v1);
                    break;

                case "execute_enemy":
                    var exs = PickEnemySlot(target, targetSlotIdx);
                    if (exs != null && exs.IsOccupied && exs.currentMonster != null)
                    {
                        if (exs.currentMonster.currentDef <= Mathf.Max(15, v1))
                            exs.currentMonster.TakeDamage(exs.currentMonster.currentDef);
                        else
                            exs.currentMonster.TakeDamage(Mathf.Max(10, v1));
                        if (!exs.currentMonster.IsAlive)
                        {
                            target.graveyard.Add(exs.currentMonster.data);
                            exs.ClearSlot();
                        }
                    }
                    break;

                case "stat_flip":
                    var sfs = PickAllySlot(caster, targetSlotIdx) ?? PickEnemySlot(target, targetSlotIdx);
                    if (sfs != null && sfs.IsOccupied && sfs.currentMonster != null &&
                        !sfs.currentMonster.IsBarrier && !sfs.currentMonster.IsAllAttack)
                    {
                        int oldAtk = Mathf.Max(1, sfs.currentMonster.baseAtk + sfs.currentMonster.bonusAtk);
                        int oldDef = Mathf.Max(1, sfs.currentMonster.currentDef);
                        sfs.currentMonster.baseAtk = oldDef;
                        sfs.currentMonster.bonusAtk = 0;
                        sfs.currentMonster.baseDef = oldAtk;
                        sfs.currentMonster.currentDef = oldAtk;
                    }
                    break;

                case "mind_control":
                    var mcs = PickEnemySlot(target, targetSlotIdx);
                    var emptyAlly = FindFirstEmptySlot(caster);
                    if (mcs != null && mcs.IsOccupied && mcs.currentMonster != null && emptyAlly != null)
                    {
                        var stolen = mcs.currentMonster;
                        mcs.ClearSlot();
                        emptyAlly.PlaceMonster(stolen);
                        stolen.canAttack = !stolen.IsBarrier;
                    }
                    break;

                case "grant_double_strike":
                    var dss = PickAllySlot(caster, targetSlotIdx);
                    if (dss != null && dss.IsOccupied && dss.currentMonster != null && !dss.currentMonster.IsBarrier)
                        dss.currentMonster.doubleStrike = true;
                    break;

                case "double_skill_1t":
                    var dsk = PickAllySlot(caster, targetSlotIdx);
                    if (dsk != null && dsk.IsOccupied && dsk.currentMonster != null && !dsk.currentMonster.IsBarrier)
                        dsk.currentMonster.doubleSkillOneTurn = true;
                    break;

                case "draw_cards":
                case "pure_draw":
                    if (!IsExtraDrawBanned(caster))
                    {
                        int dc = Mathf.Max(1, op.draw > 0 ? op.draw : (op.v1 > 0 ? op.v1 : 2));
                        for (int i = 0; i < dc; i++) caster.DrawCard();
                    }
                    if (op.kind == "draw_cards" && v1 > 0) caster.currentMana += v1;
                    break;

                case "temp_mana":
                    caster.currentMana += Mathf.Max(1, op.v1 > 0 ? op.v1 : 2);
                    break;

                case "perm_mana":
                    caster.power = Mathf.Min(GetPlayerManaCap(caster), caster.power + Mathf.Max(1, op.v1 > 0 ? op.v1 : 1));
                    caster.currentMana += 1;
                    break;

                case "revive_board":
                case "revive_grave":
                    if (IsSelfReviveBanned(caster)) break;
                    var revSlot = FindFirstEmptySlot(caster);
                    if (revSlot != null && caster.graveyard != null)
                    {
                        CardData bestDead = null;
                        for (int gi = caster.graveyard.Count - 1; gi >= 0; gi--)
                        {
                            var g = caster.graveyard[gi];
                            if (g != null && g.cardType == CardType.Monster && (bestDead == null || g.cost > bestDead.cost))
                                bestDead = g;
                        }
                        if (bestDead != null)
                        {
                            caster.graveyard.Remove(bestDead);
                            var inst = new MonsterInstance(bestDead, GameManager.Instance != null ? GameManager.Instance.currentTurn : 1);
                            int revPct = op.pct > 0 ? op.pct : 70;
                            inst.baseDef = Mathf.Max(1, StrictRoundPct(inst.baseDef, revPct));
                            inst.currentDef = inst.baseDef;
                            revSlot.PlaceMonster(inst);
                            int revDisc = GetCovenantReviveDiscount(caster);
                            if (revDisc > 0) caster.currentMana += revDisc;
                        }
                    }
                    break;

                case "boost_skills":
                    caster.freeSkillBonus += Mathf.Max(1, op.v1 > 0 ? op.v1 : 6);
                    if (op.disc >= 2) caster.freeSkillManaZero = true;
                    else if (op.disc == 1) caster.currentMana += 1;
                    if (op.mana > 0) caster.currentMana += op.mana;
                    if (op.refresh && caster.slots != null)
                    {
                        foreach (var s in caster.slots)
                            if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive)
                                s.currentMonster.ResetActiveSkillsUsage();
                    }
                    break;

                case "clone_board":
                {
                    int cCnt = Mathf.Max(1, op.cnt);
                    int cPct = op.pct > 0 ? op.pct : 100;
                    int cCap = op.cap > 0 ? op.cap : 99;
                    BoardSlot srcSlot = op.from_enemy ? PickBestEnemySlotByCost(target, cCap) : PickAllySlot(caster, targetSlotIdx);
                    if (srcSlot != null && srcSlot.currentMonster != null && srcSlot.currentMonster.data != null)
                    {
                        var srcData = srcSlot.currentMonster.data;
                        for (int ci = 0; ci < cCnt; ci++)
                        {
                            var clEmpty = FindFirstEmptySlot(caster);
                            if (clEmpty == null) break;
                            var clInst = new MonsterInstance(srcData, GameManager.Instance != null ? GameManager.Instance.currentTurn : 1);
                            if (cPct != 100)
                            {
                                if (!clInst.IsBarrier) clInst.baseAtk = Mathf.Max(0, StrictRoundPct(clInst.baseAtk, cPct));
                                clInst.baseDef = Mathf.Max(1, StrictRoundPct(clInst.baseDef, cPct));
                                clInst.currentDef = clInst.baseDef;
                            }
                            if (op.haste && !clInst.IsBarrier) clInst.canAttack = true;
                            clEmpty.PlaceMonster(clInst);
                        }
                    }
                    break;
                }

                case "deck_burn_spell":
                {
                    int burnCnt = Mathf.Max(1, op.v1 > 0 ? op.v1 : 4);
                    for (int bi = 0; bi < burnCnt && target.deck != null && target.deck.Count > 0; bi++)
                    {
                        int last = target.deck.Count - 1;
                        var milled = target.deck[last];
                        target.deck.RemoveAt(last);
                        if (milled != null) target.graveyard.Add(milled);
                    }
                    if (target.slots != null)
                    {
                        foreach (var s in target.slots)
                        {
                            if (s == null || !s.IsOccupied || s.currentMonster == null || !s.currentMonster.IsAlive) continue;
                            if (op.burn > 0 && !IsSelfDotsBanned(caster)) s.currentMonster.burnDmg += op.burn;
                            if (op.weaken > 0) s.currentMonster.weakenedAtkFlat += op.weaken;
                        }
                    }
                    break;
                }

                case "doppelganger_spell":
                {
                    var dSrc = PickBestEnemySlotByCost(target, op.cap > 0 ? op.cap : 99);
                    var dEmpty = FindFirstEmptySlot(caster);
                    if (dSrc != null && dSrc.currentMonster != null && dSrc.currentMonster.data != null && dEmpty != null)
                    {
                        var dInst = new MonsterInstance(dSrc.currentMonster.data, GameManager.Instance != null ? GameManager.Instance.currentTurn : 1);
                        if (op.haste && !dInst.IsBarrier) dInst.canAttack = true;
                        dEmpty.PlaceMonster(dInst);
                    }
                    break;
                }

                case "echo_next_spell":
                    caster.nextSpellRepeats = Mathf.Max(1, caster.nextSpellRepeats + 1);
                    break;

                case "hand_discard_spell":
                    if (target.hand != null && target.hand.Count > 0)
                    {
                        int rIdx = UnityEngine.Random.Range(0, target.hand.Count);
                        var disc = target.hand[rIdx];
                        target.hand.RemoveAt(rIdx);
                        if (disc != null) target.graveyard.Add(disc);
                    }
                    if (op.dmg > 0) target.TakeLeaderDamage(op.dmg + spBonus);
                    break;

                case "polymorph":
                {
                    var pSlot = PickBestEnemySlotByCost(target, op.cap > 0 ? op.cap : 99, targetSlotIdx);
                    if (pSlot != null && pSlot.IsOccupied && pSlot.currentMonster != null)
                    {
                        var sheepData = new CardData
                        {
                            id = 0,
                            name = "Sheep Token",
                            cardType = CardType.Monster,
                            card_type = "monster",
                            element = "None",
                            tribe = "None",
                            cost = 1,
                            atk = 1,
                            def = 1
                        };
                        pSlot.ClearSlot();
                        pSlot.PlaceMonster(new MonsterInstance(sheepData, GameManager.Instance != null ? GameManager.Instance.currentTurn : 1));
                    }
                    break;
                }

                case "pull_hook_spell":
                {
                    if (target.slots != null)
                    {
                        BoardSlot flankPick = null;
                        int bestScore = -1;
                        foreach (int fi in new[] { 0, 1, 4, 5 })
                        {
                            if (fi < target.slots.Length && target.slots[fi] != null && target.slots[fi].IsOccupied && target.slots[fi].currentMonster != null && target.slots[fi].currentMonster.IsAlive)
                            {
                                int sc = target.slots[fi].currentMonster.baseAtk + target.slots[fi].currentMonster.currentDef;
                                if (sc > bestScore) { bestScore = sc; flankPick = target.slots[fi]; }
                            }
                        }
                        if (flankPick != null)
                        {
                            int destIdx = (targetSlotIdx >= 0 && targetSlotIdx < target.slots.Length) ? targetSlotIdx : 2;
                            var victim = flankPick.currentMonster;
                            if (flankPick.slotIndex != destIdx && target.slots[destIdx] != null)
                            {
                                var displaced = target.slots[destIdx].currentMonster;
                                flankPick.ClearSlot();
                                target.slots[destIdx].ClearSlot();
                                target.slots[destIdx].PlaceMonster(victim);
                                if (displaced != null) flankPick.PlaceMonster(displaced);
                            }
                            if (op.burn > 0 && !IsSelfDotsBanned(caster)) victim.burnDmg += op.burn;
                            if (op.root) victim.rootedTurns = Mathf.Max(victim.rootedTurns, 1);
                        }
                    }
                    break;
                }

                case "recall_spell":
                {
                    int rCnt = Mathf.Max(1, op.cnt);
                    for (int ri = 0; ri < rCnt; ri++)
                    {
                        if (op.enemy)
                        {
                            var rEs = PickBestEnemySlotByCost(target, op.cap > 0 ? op.cap : 99, targetSlotIdx);
                            if (rEs != null && rEs.currentMonster != null && !rEs.currentMonster.IsLocked)
                            {
                                if (rEs.currentMonster.data != null && rEs.currentMonster.data.id > 0)
                                    target.AddCardToHandWithBurnLimit(rEs.currentMonster.data);
                                rEs.ClearSlot();
                            }
                        }
                        else
                        {
                            var rAs = PickAllySlot(caster, targetSlotIdx);
                            if (rAs != null && rAs.currentMonster != null && !rAs.currentMonster.IsLocked)
                            {
                                if (rAs.currentMonster.data != null && rAs.currentMonster.data.id > 0)
                                    caster.AddCardToHandWithBurnLimit(rAs.currentMonster.data);
                                rAs.ClearSlot();
                            }
                        }
                    }
                    if (op.hp > 0) caster.HealLeader(op.hp, target);
                    if (op.mana > 0) caster.currentMana += op.mana;
                    if (op.spawn != null && op.spawn.Length >= 2)
                    {
                        var spEmpty = FindFirstEmptySlot(caster);
                        if (spEmpty != null)
                        {
                            var tok = new CardData { id = 0, name = "Recall Token", cardType = CardType.Monster, card_type = "monster", element = "None", tribe = "None", cost = 1, atk = op.spawn[0], def = op.spawn[1] };
                            spEmpty.PlaceMonster(new MonsterInstance(tok, GameManager.Instance != null ? GameManager.Instance.currentTurn : 1));
                        }
                    }
                    break;
                }

                case "solar_sacrifice":
                {
                    BoardSlot bestNonBar = null;
                    int bestAtk = -1;
                    if (caster.slots != null)
                    {
                        foreach (var s in caster.slots)
                        {
                            if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive && !s.currentMonster.IsBarrier)
                            {
                                int ea = s.currentMonster.GetEffectiveAtk(GameManager.Instance != null ? GameManager.Instance.activeEnvironment : null, caster);
                                if (ea > bestAtk) { bestAtk = ea; bestNonBar = s; }
                            }
                        }
                    }
                    if (bestNonBar != null)
                    {
                        int solarDmg = Mathf.Max(1, StrictRoundPct(bestAtk, op.pct > 0 ? op.pct : 150)) + spBonus;
                        caster.graveyard.Add(bestNonBar.currentMonster.data);
                        bestNonBar.ClearSlot();
                        var eSl = PickEnemySlot(target, targetSlotIdx);
                        if (eSl != null && eSl.currentMonster != null)
                        {
                            eSl.currentMonster.TakeDamage(solarDmg);
                            if (!eSl.currentMonster.IsAlive) { target.graveyard.Add(eSl.currentMonster.data); eSl.ClearSlot(); }
                        }
                        else
                        {
                            target.TakeLeaderDamage(solarDmg);
                        }
                    }
                    break;
                }

                case "spawn_tokens":
                {
                    if (op.sac)
                    {
                        var sacSl = PickAllySlot(caster, targetSlotIdx);
                        if (sacSl != null && sacSl.currentMonster != null)
                        {
                            caster.graveyard.Add(sacSl.currentMonster.data);
                            sacSl.ClearSlot();
                        }
                    }
                    int tCnt = Mathf.Max(1, op.cnt);
                    int tAtk = op.v1 > 0 ? op.v1 : 5;
                    int tDef = op.v2 > 0 ? op.v2 : 5;
                    for (int ti = 0; ti < tCnt; ti++)
                    {
                        var te = FindFirstEmptySlot(caster);
                        if (te == null) break;
                        var tCard = new CardData { id = 0, name = "Swarm Token", cardType = CardType.Monster, card_type = "monster", element = "None", tribe = "Insects", cost = 1, atk = tAtk, def = tDef };
                        var tInst = new MonsterInstance(tCard, GameManager.Instance != null ? GameManager.Instance.currentTurn : 1);
                        if (op.haste) tInst.canAttack = true;
                        te.PlaceMonster(tInst);
                    }
                    if (op.atk > 0 && caster.slots != null)
                    {
                        foreach (var s in caster.slots)
                            if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive && !s.currentMonster.IsBarrier && !s.currentMonster.IsAllAttack)
                                s.currentMonster.tempTurnAtk += op.atk;
                    }
                    break;
                }

                case "spy_plant_spell":
                {
                    BoardSlot spyEmpty = (target.slots != null && targetSlotIdx >= 0 && targetSlotIdx < target.slots.Length && target.slots[targetSlotIdx] != null && !target.slots[targetSlotIdx].IsOccupied)
                        ? target.slots[targetSlotIdx]
                        : FindFirstEmptySlot(target);
                    if (spyEmpty != null)
                    {
                        bool spyPoison = op.poison > 0 || (!string.IsNullOrEmpty(spellCard.v2_ops_json) && spellCard.v2_ops_json.Contains("\"poison\": true"));
                        var spyCard = new CardData
                        {
                            id = 0,
                            name = "Spy Parasite",
                            cardType = CardType.Monster,
                            card_type = "monster",
                            element = "None",
                            tribe = "None",
                            role = "Disruptor",
                            cost = 1,
                            atk = 0,
                            def = Mathf.Max(5, op.v1 > 0 ? op.v1 : 15),
                            is_barrier = true,
                            description = spyPoison ? $"Spy Parasite: Deals {Mathf.Max(2, op.v2)} damage and Poison to adjacent allies." : $"Spy Parasite: Deals {Mathf.Max(2, op.v2)} damage to adjacent allies."
                        };
                        spyEmpty.PlaceMonster(new MonsterInstance(spyCard, GameManager.Instance != null ? GameManager.Instance.currentTurn : 1));
                    }
                    else
                    {
                        var fbEs = PickEnemySlot(target, targetSlotIdx);
                        int fbDmg = (op.fallback_dmg > 0 ? op.fallback_dmg : 14) + spBonus;
                        if (fbEs != null && fbEs.currentMonster != null)
                        {
                            fbEs.currentMonster.TakeDamage(fbDmg);
                            if (!fbEs.currentMonster.IsAlive) { target.graveyard.Add(fbEs.currentMonster.data); fbEs.ClearSlot(); }
                        }
                    }
                    break;
                }

                case "swap_atk_enemy":
                {
                    var aSwap = PickAllySlot(caster, targetSlotIdx);
                    var eSwap = PickEnemySlot(target, -1);
                    if (aSwap != null && eSwap != null && aSwap.currentMonster != null && eSwap.currentMonster != null &&
                        !aSwap.currentMonster.IsBarrier && !aSwap.currentMonster.IsAllAttack &&
                        !eSwap.currentMonster.IsBarrier && !eSwap.currentMonster.IsAllAttack)
                    {
                        int tmpA = aSwap.currentMonster.baseAtk;
                        aSwap.currentMonster.baseAtk = eSwap.currentMonster.baseAtk;
                        eSwap.currentMonster.baseAtk = tmpA;
                    }
                    break;
                }

                case "swap_lanes":
                {
                    var slSlot = PickAllySlot(caster, targetSlotIdx);
                    if (slSlot != null && slSlot.currentMonster != null && caster.slots != null && caster.slots.Length >= 6)
                    {
                        int otherIdx = (slSlot.slotIndex + 3) % 6;
                        var m1 = slSlot.currentMonster;
                        var m2 = caster.slots[otherIdx].currentMonster;
                        slSlot.ClearSlot();
                        caster.slots[otherIdx].ClearSlot();
                        caster.slots[otherIdx].PlaceMonster(m1);
                        if (m2 != null) slSlot.PlaceMonster(m2);
                        m1.ApplyPermBuff(op.atk > 0 ? op.atk : 4, op.deff);
                    }
                    break;
                }

                case "swarm_bite":
                {
                    int biteDmg = Mathf.Max(1, (op.v1 > 0 ? op.v1 : 4) + (spBonus > 0 ? 1 : 0));
                    if (caster.slots != null && target.slots != null)
                    {
                        for (int si = 0; si < caster.slots.Length; si++)
                        {
                            if (caster.slots[si] == null || !caster.slots[si].IsOccupied || caster.slots[si].currentMonster == null || !caster.slots[si].currentMonster.IsAlive) continue;
                            if (si < target.slots.Length && target.slots[si] != null && target.slots[si].IsOccupied && target.slots[si].currentMonster != null && target.slots[si].currentMonster.IsAlive)
                            {
                                target.slots[si].currentMonster.TakeDamage(biteDmg);
                                if (!target.slots[si].currentMonster.IsAlive) { target.graveyard.Add(target.slots[si].currentMonster.data); target.slots[si].ClearSlot(); }
                            }
                            else
                            {
                                target.TakeLeaderDamage(biteDmg);
                            }
                        }
                    }
                    break;
                }

                case "time_rewind":
                    if (caster.slots != null)
                    {
                        foreach (var s in caster.slots)
                        {
                            if (s == null || !s.IsOccupied || s.currentMonster == null || !s.currentMonster.IsAlive) continue;
                            var m = s.currentMonster;
                            m.burnDmg = 0;
                            m.poisonDmg = 0;
                            m.bleedDmg = 0;
                            m.frozenTurns = 0;
                            m.silencedTurns = 0;
                            m.rootedTurns = 0;
                            m.weakenedTurns = 0;
                            m.weakenedAtkFlat = 0;
                            if (m.prevTurnDef > m.currentDef)
                                m.HealDefense(m.prevTurnDef - m.currentDef);
                        }
                    }
                    caster.HealLeader(op.hp > 0 ? op.hp : 35, target);
                    break;

                default:
                    // Fallback general spell effect
                    if (v1 > 0)
                    {
                        var defEs = PickEnemySlot(target, targetSlotIdx);
                        if (defEs != null && defEs.IsOccupied && defEs.currentMonster != null)
                        {
                            defEs.currentMonster.TakeDamage(v1);
                            if (!defEs.currentMonster.IsAlive)
                            {
                                target.graveyard.Add(defEs.currentMonster.data);
                                defEs.ClearSlot();
                            }
                        }
                        else
                        {
                            target.TakeLeaderDamage(v1);
                        }
                    }
                    break;
            }

            // Draw riders attached to spells (e.g., Spartan Hook draw: 1)
            if (op.draw > 0 && op.kind != "draw_cards" && op.kind != "pure_draw" && !IsExtraDrawBanned(caster))
            {
                for (int di = 0; di < op.draw; di++) caster.DrawCard();
            }

            // Trigger Leader Spell-Ping Aura if active and controlling a matching-Tribe ally
            if (HasMatchingTribeAlly(caster))
            {
                var lops = GetLeaderOps(caster.activeLeader);
                if (lops != null && lops.aura_spell_ping > 0)
                {
                    target.TakeLeaderDamage(lops.aura_spell_ping);
                }
            }

            return true;
        }

        /// <summary>
        /// V2 Covenant Start-of-Turn triggers (matching Python BattleEngine._begin_turn 1-to-1).
        /// </summary>
        public static void ApplyCovenantStartOfTurn(PlayerState activePlayer, PlayerState otherPlayer, int currentTurn)
        {
            if (!IsCovenantActiveForPlayer(activePlayer)) return;
            var cov = GetCovenantOps(activePlayer.activeCovenant);
            if (cov == null) return;

            if (cov.turn_limit_lose > 0 && currentTurn > cov.turn_limit_lose)
            {
                BattleUIManager.Instance?.LogCombatMessage($"💀 Covenant [{activePlayer.activeCovenant.name}] turn limit ({cov.turn_limit_lose}) expired!");
                activePlayer.TakeLeaderDamage(activePlayer.currentHp + 999);
                return;
            }
            if (cov.sac_every_2nd && currentTurn % 2 == 0 && activePlayer.slots != null)
            {
                BoardSlot weakest = null;
                int minScore = int.MaxValue;
                foreach (var s in activePlayer.slots)
                {
                    if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive)
                    {
                        int sc = s.currentMonster.baseAtk + s.currentMonster.currentDef;
                        if (sc < minScore) { minScore = sc; weakest = s; }
                    }
                }
                if (weakest != null)
                {
                    activePlayer.graveyard.Add(weakest.currentMonster.data);
                    weakest.ClearSlot();
                }
            }
            if (cov.rew_enemy_mana_loss > 0 && otherPlayer != null)
            {
                otherPlayer.lockedManaNext = Mathf.Min(3, otherPlayer.lockedManaNext + cov.rew_enemy_mana_loss);
            }
            if (cov.rew_temp_mana > 0)
            {
                activePlayer.currentMana += cov.rew_temp_mana;
            }
            if (cov.rew_disrupt_eot > 0 && currentTurn % 2 == 0 && otherPlayer != null && otherPlayer.hand != null && otherPlayer.hand.Count > 0)
            {
                int rIdx = UnityEngine.Random.Range(0, otherPlayer.hand.Count);
                var disc = otherPlayer.hand[rIdx];
                otherPlayer.hand.RemoveAt(rIdx);
                if (disc != null) otherPlayer.graveyard.Add(disc);
            }
            if (cov.rew_steal_copy > 0 && currentTurn % 2 == 0 && otherPlayer != null && otherPlayer.hand != null && otherPlayer.hand.Count > 0)
            {
                var stolen = otherPlayer.hand[UnityEngine.Random.Range(0, otherPlayer.hand.Count)];
                if (stolen != null)
                {
                    activePlayer.AddCardToHandWithBurnLimit(stolen);
                    activePlayer.discountedCardIds[stolen.id] = (activePlayer.discountedCardIds.TryGetValue(stolen.id, out int cc) ? cc : 0) + 1;
                }
            }
            if (cov.self_mana_tax_if_lt2 > 0 && activePlayer.slots != null)
            {
                int occ = 0;
                foreach (var s in activePlayer.slots) if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive) occ++;
                if (occ < 2) activePlayer.currentMana = Mathf.Max(0, activePlayer.currentMana - cov.self_mana_tax_if_lt2);
            }
            if (cov.rew_token_turn > 0)
            {
                for (int ti = 0; ti < cov.rew_token_turn; ti++)
                {
                    var emp = FindFirstEmptySlot(activePlayer);
                    if (emp == null) break;
                    var tCard = new CardData { id = 0, name = "Covenant Token", cardType = CardType.Monster, card_type = "monster", element = "None", tribe = "None", cost = 1, atk = 4, def = 5 };
                    var tInst = new MonsterInstance(tCard, currentTurn);
                    if (cov.rew_haste) tInst.canAttack = true;
                    emp.PlaceMonster(tInst);
                }
            }
        }

        /// <summary>
        /// V2 Constitution Rule #8: After Round 40, both Commanders automatically lose -100 HP per round (Sudden Death).
        /// </summary>
        public static void ApplyTurnEndV2Rules(PlayerState activePlayer, PlayerState otherPlayer, int currentTurn)
        {
            if (activePlayer != null)
            {
                // Covenant end-of-turn self-restrictions & rewards
                if (IsCovenantActiveForPlayer(activePlayer))
                {
                    var cov = GetCovenantOps(activePlayer.activeCovenant);
                    if (cov != null)
                    {
                        if (cov.rew_hp_turn > 0) activePlayer.HealLeader(cov.rew_hp_turn, otherPlayer);
                        if (cov.self_hp_turn > 0) activePlayer.TakeLeaderDamage(cov.self_hp_turn);
                        if (cov.rew_enemy_face > 0 && otherPlayer != null) otherPlayer.TakeLeaderDamage(cov.rew_enemy_face);
                        if (cov.discard_hand_eot && activePlayer.hand != null && activePlayer.hand.Count > 0)
                        {
                            foreach (var c in activePlayer.hand)
                                if (c != null) activePlayer.graveyard.Add(c);
                            activePlayer.hand.Clear();
                            BattleUIManager.Instance?.LogCombatMessage($"📜 Covenant [{activePlayer.activeCovenant.name}] discarded remaining hand at end of turn!");
                        }
                    }
                }
            }

            // Turn 41+ Sudden Death Penalty (-100 HP to BOTH Commanders!)
            if (currentTurn > 40 && activePlayer != null && otherPlayer != null)
            {
                BattleUIManager.Instance?.LogCombatMessage($"⏳ [Sudden Death • Round {currentTurn} > 40] Both Commanders suffer -100 HP Overtime Penalty!");
                activePlayer.TakeLeaderDamage(100);
                otherPlayer.TakeLeaderDamage(100);
            }
        }

        private static void ApplySpellFxOnUnit(MonsterInstance m, V2OpItem op)
        {
            if (m == null || op == null) return;
            int dur = Mathf.Max(1, op.dur);
            if (!string.IsNullOrEmpty(op.fx))
            {
                switch (op.fx)
                {
                    case "burn": m.burnDmg += Mathf.Max(1, op.v2); break;
                    case "poison": m.poisonDmg += Mathf.Max(1, op.v2); break;
                    case "bleed": m.bleedDmg += Mathf.Max(1, op.v2); break;
                    case "weaken": m.weakenedAtkFlat += Mathf.Max(1, op.v2); break;
                    case "freeze": m.frozenTurns = Mathf.Max(m.frozenTurns, dur); break;
                    case "silence": m.silencedTurns = Mathf.Max(m.silencedTurns, dur); break;
                    case "root": m.rootedTurns = Mathf.Max(m.rootedTurns, dur); break;
                }
            }
            if (op.weaken > 0) m.weakenedAtkFlat += op.weaken;
            if (op.bleed > 0) m.bleedDmg += op.bleed;
            if (op.root) m.rootedTurns = Mathf.Max(m.rootedTurns, dur);
            if (op.silence) m.silencedTurns = Mathf.Max(m.silencedTurns, dur);
        }

        private static BoardSlot PickEnemySlot(PlayerState opp, int preferredIdx)
        {
            if (opp == null || opp.slots == null) return null;
            if (preferredIdx >= 0 && preferredIdx < opp.slots.Length &&
                opp.slots[preferredIdx] != null && opp.slots[preferredIdx].IsOccupied)
            {
                return opp.slots[preferredIdx];
            }
            BoardSlot best = null;
            int bestAtk = -1;
            foreach (var s in opp.slots)
            {
                if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive)
                {
                    if (s.currentMonster.baseAtk > bestAtk)
                    {
                        bestAtk = s.currentMonster.baseAtk;
                        best = s;
                    }
                }
            }
            return best;
        }

        private static BoardSlot PickAllySlot(PlayerState ally, int preferredIdx)
        {
            if (ally == null || ally.slots == null) return null;
            if (preferredIdx >= 0 && preferredIdx < ally.slots.Length &&
                ally.slots[preferredIdx] != null && ally.slots[preferredIdx].IsOccupied)
            {
                return ally.slots[preferredIdx];
            }
            foreach (var s in ally.slots)
            {
                if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive)
                    return s;
            }
            return null;
        }

        private static BoardSlot PickBestEnemySlotByCost(PlayerState opp, int maxCost, int preferredIdx = -1)
        {
            if (opp == null || opp.slots == null) return null;
            if (preferredIdx >= 0 && preferredIdx < opp.slots.Length &&
                opp.slots[preferredIdx] != null && opp.slots[preferredIdx].IsOccupied &&
                opp.slots[preferredIdx].currentMonster != null && opp.slots[preferredIdx].currentMonster.IsAlive &&
                (opp.slots[preferredIdx].currentMonster.data == null || opp.slots[preferredIdx].currentMonster.data.cost <= maxCost))
            {
                return opp.slots[preferredIdx];
            }
            BoardSlot best = null;
            int bestScore = -1;
            foreach (var s in opp.slots)
            {
                if (s != null && s.IsOccupied && s.currentMonster != null && s.currentMonster.IsAlive)
                {
                    if (s.currentMonster.data != null && s.currentMonster.data.cost > maxCost) continue;
                    int sc = s.currentMonster.baseAtk + s.currentMonster.currentDef;
                    if (sc > bestScore)
                    {
                        bestScore = sc;
                        best = s;
                    }
                }
            }
            return best;
        }

        private static BoardSlot FindFirstEmptySlot(PlayerState p)
        {
            if (p == null || p.slots == null) return null;
            foreach (var s in p.slots)
            {
                if (s != null && !s.IsOccupied) return s;
            }
            return null;
        }
    }
}

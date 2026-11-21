# -*- coding: utf-8 -*-
"""
Pure-Python Battle Engine implementing all 35 Constitution Rules,
25 Locked Effects, 10 Shared Environments, 60 Leaders, 100 Covenants,
300 Spells, and 1,800 Monsters (including all 17 new signature mechanics).

Zero floats ever enter game state: every percentage calculation uses strict
integer rounding via `pct_of()` / `ri()`.
"""

from __future__ import annotations

import random
from typing import Any, Dict, List, Optional, Tuple

import numpy as np

from .card_model import (
    BOARD_SLOTS,
    CENTER_SLOTS,
    FLANK_SLOTS,
    LEADER_HP,
    MAX_HAND,
    MAX_MANA_CAP,
    MAX_TURNS,
    CardDB,
    CardDef,
    HandCard,
    MatchResult,
    MonsterUnit,
    PlayerState,
    get_card_db,
    pct_of,
    ri,
)

# === Fixed RL Interface Dimensions ===
ACTION_DIM = 62   # 0: EndTurn, 1..7: Sacrifice h, 8..49: Play h at slot s, 50..61: Active slot s skill k
OBS_DIM = 220     # 160 core state features + 60 raw passive/active/status indicator lights (18 ally + 42 enemy)


class BattleEngine:
    """Fast deterministic-seedable Pure-Python simulator for Arena_Lab_V2."""

    def __init__(self, db: Optional[CardDB] = None, seed: Optional[int] = None) -> None:
        self.db: CardDB = db if db is not None else get_card_db()
        self.rng: random.Random = random.Random(seed)

        self.players: List[PlayerState] = []
        self.turn: int = 1
        self.active: int = 0
        self.done: bool = False
        self.winner: int = -1
        self.win_reason: str = ""
        self.actions_this_turn: int = 0

        # Shared Environment Arena state (-1 = none)
        self.env_id: int = -1
        self.env_level: int = 0
        self.env_turns: int = 0

        self._initial_decks: Tuple[List[int], List[int]] = ([], [])
        self._turn_dmg: int = 0
        self._turn_kills: int = 0
        self._turn_highlights: int = 0
        self._lead_side: int = -1
        self._max_deficit: List[int] = [0, 0]
        self._turn_events: List[Tuple[int, ...]] = []
        self._dmg_cause: str = "passive"

    def _add_highlight(self, side: int, tag: str) -> None:
        if side in (0, 1):
            t = self.players[side].telemetry
            t.highlight_triggers += 1
            t.highlight_tags[tag] = t.highlight_tags.get(tag, 0) + 1
            self._turn_highlights += 1

    # ------------------------------------------------------------------
    # Reset & Setup
    # ------------------------------------------------------------------
    def reset(
        self,
        deck0: List[int],
        deck1: List[int],
        seed: Optional[int] = None,
    ) -> None:
        if seed is not None:
            self.rng.seed(seed)

        d0 = list(deck0)
        d1 = list(deck1)
        self._initial_decks = (list(d0), list(d1))

        self.players = [PlayerState(0, d0), PlayerState(1, d1)]
        self.players[0].deck = self._shuffle_cycle_deck(d0)
        self.players[1].deck = self._shuffle_cycle_deck(d1)
        self.turn = 1
        self.active = self.rng.randint(0, 1)
        self.done = False
        self.winner = -1
        self.win_reason = ""
        self.actions_this_turn = 0

        self.env_id = -1
        self.env_level = 0
        self.env_turns = 0
        self._turn_dmg = 0
        self._turn_kills = 0
        self._turn_highlights = 0
        self._lead_side = -1
        self._max_deficit = [0, 0]
        self._turn_events = []
        self._dmg_cause = "passive"
        self.players[self.active].telemetry.went_first = True

        # Opening hands: first player draws 4, second player draws 5
        for side in (0, 1):
            n_open = 4 if side == self.active else 5
            for _ in range(n_open):
                self._draw_card(side, check_deckout=False)

        self._begin_turn(self.active)

    # ------------------------------------------------------------------
    # Turn Lifecycle
    # ------------------------------------------------------------------
    def _begin_turn(self, side: int) -> None:
        p = self.players[side]
        opp = self.players[1 - side]
        self.actions_this_turn = 0
        self._turn_dmg = 0
        self._turn_kills = 0
        self._turn_highlights = 0
        p.sacrificed_this_turn = False
        p.cards_played_turn = 0
        p.summons_turn = 0
        p.echo_next_spell = False
        p.spell_power_temp = 0
        p.skill_bonus_temp = 0
        p.skill_discount_temp = 0

        # Tick shared environment duration
        if self.env_id >= 0:
            self.env_turns -= 1
            if self.env_turns <= 0:
                self.env_id = -1
                self.env_level = 0
                self.env_turns = 0

        # Leader mute countdown
        if p.leader_muted_turns > 0:
            p.leader_muted_turns -= 1

        # Refresh mana (Constitution: +1 automatic mana per turn up to cap; sacrifice can ramp faster)
        if self.turn > 1 or side != self.active:
            if p.max_mana < p.mana_cap():
                p.max_mana += 1
        locked = p.locked_mana_next
        p.locked_mana_next = 0
        p.mana = max(0, p.max_mana - locked)
        p.temp_mana = 0

        # Covenant start-of-turn checks & triggers
        if p.covenant_active and p.covenant_ops:
            cov = p.covenant_ops
            if cov.get("turn_limit_lose", 0) > 0 and self.turn > cov["turn_limit_lose"]:
                self._finish(1 - side, "CovenantTurnLimit")
                return
            self_hp_loss = int(cov.get("self_hp_turn", 0))
            if self_hp_loss > 0:
                self._damage_commander(side, self_hp_loss, source_side=-1)
                if self.done:
                    return
            if cov.get("sac_every_2nd") and self.turn % 2 == 0:
                weak_ally = min(p.board_units(), key=lambda u: u.atk + u.deff, default=None)
                if weak_ally is not None:
                    self._destroy_unit(side, weak_ally, killer_side=-1)
            rew_hp = int(cov.get("rew_hp_turn", 0))
            if rew_hp > 0:
                p.heal_hp(rew_hp)
            rew_face = int(cov.get("rew_enemy_face", 0))
            if rew_face > 0:
                self._damage_commander(1 - side, rew_face, source_side=side)
                if self.done:
                    return
            if cov.get("rew_enemy_mana_loss", 0) > 0:
                opp.locked_mana_next = min(3, opp.locked_mana_next + 1)
            if cov.get("rew_temp_mana", 0) > 0:
                p.temp_mana += int(cov["rew_temp_mana"])
            if cov.get("rew_disrupt_eot") and self.turn % 2 == 0:
                if opp.hand:
                    opp.graveyard.append(opp.hand.pop(self.rng.randrange(len(opp.hand))).card_id)
                    p.telemetry.disruptions_applied += 1
            if cov.get("rew_steal_copy") and self.turn % 2 == 0:
                if opp.hand:
                    p.add_to_hand(self.rng.choice(opp.hand).card_id, cost_mod=-1)
                    p.telemetry.clones_made += 1
            if cov.get("self_mana_tax_if_lt2") and p.board_count() < 2:
                p.mana = max(0, p.mana - 1)
            tok_n = int(cov.get("rew_token_turn", 0))
            if tok_n > 0:
                for _ in range(tok_n):
                    self._spawn_token(side, 4, 5, name="Covenant Token", haste=bool(cov.get("rew_haste", False)))

        # Shared Environment start-of-turn effects (Global base + Strong Elemental Resonance!)
        lvl_idx = min(2, max(0, self.env_level - 1))
        if self.env_id == 1:  # Water Arena
            base_h = (2, 3, 4)[lvl_idx]
            res_h = (6, 10, 15)[lvl_idx]
            for u in p.board_units():
                h = res_h if u.element == 1 else base_h
                u.heal_def(h)
                p.telemetry.def_healed += h
                if u.element == 1:
                    u.cleanse()
        elif self.env_id == 2:  # Nature Arena
            p.heal_hp((5, 8, 12)[lvl_idx])
            if any(u.element == 2 for u in p.board_units()):
                p.heal_hp((12, 18, 26)[lvl_idx])
        elif self.env_id == 3:  # Electric Arena
            if any(u.element == 3 for u in p.board_units()):
                p.temp_mana += (1, 2, 2)[lvl_idx]
        elif self.env_id == 7:  # Light Arena
            base_d = (2, 3, 5)[lvl_idx]
            res_d = (6, 10, 14)[lvl_idx]
            cleansed_any = False
            has_light = False
            for u in p.board_units():
                if u.element == 7:
                    has_light = True
                    u.cleanse()
                    u.heal_def(res_d)
                    p.telemetry.def_healed += res_d
                elif not cleansed_any and u.cleanse():
                    u.heal_def(base_d)
                    p.telemetry.def_healed += base_d
                    cleansed_any = True
            if has_light:
                p.heal_hp((12, 18, 26)[lvl_idx])
        elif self.env_id == 9:  # Desert Arena sandstorm erodes non-Desert enemies if you control a Desert unit
            if any(u.element == 9 for u in p.board_units()):
                erode = (4, 6, 9)[lvl_idx]
                for eu in list(opp.board_units()):
                    if eu.element != 9:
                        self._damage_unit(1 - side, eu, erode, source_side=side)

        # Active Leader Spirit continuous start-of-turn heal aura (Owner-Only!)
        if self._leader_aura_on(side) and p.leader_ops:
            l_hp = int(p.leader_ops.get("aura_heal_hp", 0))
            if l_hp > 0:
                p.heal_hp(l_hp)

        # Draw 1 card (plus Covenant extra draw unless ban_self_extra_draw is active)
        if p.no_draw_turns > 0:
            p.no_draw_turns -= 1
        else:
            ban_xd = bool(p.covenant_active and p.covenant_ops and p.covenant_ops.get("ban_self_extra_draw"))
            extra_d = 0 if ban_xd else (int(p.covenant_ops.get("rew_extra_draw", 0)) if (p.covenant_active and p.covenant_ops) else 0)
            draws = 1 + extra_d
            for _ in range(draws):
                self._draw_card(side, check_deckout=True)
                if self.done:
                    return

        # Record start-of-turn hand size (after draw) for Hand Economy analytics
        p.telemetry.hand_sum_start += len(p.hand)
        p.telemetry.hand_turns += 1
        self._turn_start_hand = len(p.hand)

        # Refresh own board units
        self._dmg_cause = "passive"
        for u in p.board_units():
            if self.turn > u.last_own_turn:
                u.own_turns_survived += 1
                u.last_own_turn = self.turn
            u.prev_turn_def = u.deff
            u.temp_atk_buff = 0
            if u.temp_def_buff > 0:
                u.max_def = max(1, u.max_def - u.temp_def_buff)
                u.deff = min(u.deff, u.max_def)
                u.temp_def_buff = 0
            u.attacked_this_turn = False
            u.skills_used_this_turn = 0
            u.skill_uses_this_turn = [0, 0]
            u.double_skill_1t = False
            u.double_strike_temp = False
            u.can_attack = u.can_ever_attack() and (u.frozen <= 0)

            # Ascend turn progress
            if not u.ascended and not u.is_silenced():
                for pa in u.passives:
                    if pa.get("kind") == "ascend":
                        u.ascend_turns += 1
                        if u.ascend_turns >= 2:
                            self._trigger_ascend(side, u, pa)

            # Turn-start unit heal/cleanse auras
            if not u.is_silenced():
                if u.turn_heal_def > 0:
                    for ally in p.board_units():
                        p.telemetry.def_healed += ally.heal_def(u.turn_heal_def)
                if u.turn_heal_hp > 0:
                    p.heal_hp(u.turn_heal_hp)
                if u.cleanse_aura_def > 0:
                    for ally in p.board_units():
                        if ally.cleanse():
                            p.telemetry.def_healed += ally.heal_def(u.cleanse_aura_def)
                            break

        b_cnt = p.board_count()
        if b_cnt > p.telemetry.max_board_units:
            p.telemetry.max_board_units = b_cnt
        if b_cnt >= 5:
            p.telemetry.turns_with_full_board += 1

    # ------------------------------------------------------------------
    # Dynamic Stat & Aura Calculations (Strict Integer!)
    # ------------------------------------------------------------------
    def _leader_aura_on(self, side: int) -> bool:
        p = self.players[side]
        return p.leader_active and (not p.leader_exorcised) and (p.leader_muted_turns <= 0)

    def _unit_matches_leader(self, side: int, u: MonsterUnit) -> bool:
        p = self.players[side]
        if not self._leader_aura_on(side) or not p.leader_ops:
            return False
        if u.is_barrier or u.is_spy:
            return False
        if u.chameleon and not u.is_silenced():
            return True
        return u.tribe == int(p.leader_ops.get("tribe", -2))

    def _effective_atk(self, side: int, u: MonsterUnit) -> int:
        """Computes strict integer effective ATK respecting Barrier/All-Attack ATK lock."""
        if u.is_barrier or u.is_spy:
            return 0
        if u.atk_locked:
            # All-Attack units can NEVER gain ATK from ANY source; only weakened reduces them
            return max(0, int(u.base_atk - u.weakened))

        p = self.players[side]
        opp = self.players[1 - side]
        atk = int(u.atk + u.temp_atk_buff)

        # Shared Environment Elemental Resonance ATK Bonus (shared by both sides, massively amplified for matching Element!)
        if self.env_id >= 0:
            el_idx = min(2, max(0, self.env_level - 1))
            if u.element == self.env_id:
                if self.env_id in (0, 3, 6, 8, 9):
                    atk += (6, 9, 13)[el_idx]
                else:
                    atk += (5, 8, 11)[el_idx]
            else:
                if self.env_id == 0:
                    atk += (1, 2, 3)[el_idx]
                elif self.env_id == 8 and u.slot in FLANK_SLOTS:
                    atk += (1, 2, 3)[el_idx]

        # Leader Owner-Only Tribal Aura
        if self._unit_matches_leader(side, u) and p.leader_ops:
            atk += int(p.leader_ops.get("aura_atk", 0))

        # Covenant bonus / penalty
        if p.covenant_active and p.covenant_ops:
            cov = p.covenant_ops
            atk += int(cov.get("rew_atk", 0))
            atk -= int(cov.get("self_atk_pen", 0))
            spct = int(cov.get("rew_stat_pct", 0))
            if spct > 0:
                atk += pct_of(u.base_atk, spct)
            pen_pct = int(cov.get("self_atk_pct_pen", 0))
            if pen_pct > 0:
                atk -= pct_of(atk, pen_pct)

        # Dynamic monster passives (only when not silenced)
        if not u.is_silenced():
            if u.enrage_active and u.enrage_atk > 0:
                atk += u.enrage_atk
            if u.lone_wolf_atk > 0 and p.board_count() == 1:
                atk += u.lone_wolf_atk
            if u.open_lane_atk > 0 and opp.board[u.slot] is None:
                atk += u.open_lane_atk
            if u.flank_atk > 0 and u.slot in FLANK_SLOTS:
                atk += u.flank_atk
            if u.pack_atk > 0:
                atk += u.pack_atk * max(0, p.board_count() - 1)
            if u.grave_scale_atk > 0:
                atk += min(12, u.grave_scale_atk * (len(p.graveyard) // 3))

        # Opposing weakening auras
        opp_u = opp.board[u.slot]
        if opp_u is not None and not opp_u.is_silenced() and opp_u.aura_weaken_opp > 0:
            atk -= opp_u.aura_weaken_opp
        if self._leader_aura_on(1 - side) and opp.leader_ops:
            atk -= int(opp.leader_ops.get("aura_weaken_opp", 0))

        # Weakened debuff
        atk -= int(u.weakened)
        return max(0, int(atk))

    def _effective_evasion(self, side: int, u: MonsterUnit) -> int:
        ev = 0 if u.is_silenced() else int(u.evasion_pct)
        el_idx = min(2, max(0, self.env_level - 1))
        if self.env_id == 6:  # Dark Arena Evasion (base + Dark resonance)
            ev += (12, 18, 24)[el_idx] if u.element == 6 else (6, 10, 14)[el_idx]
        elif self.env_id == 8 and u.element == 8:  # Air Arena Evasion for Air units
            ev += (18, 26, 35)[el_idx]
        if self._unit_matches_leader(side, u) and self.players[side].leader_ops:
            ev += int(self.players[side].leader_ops.get("aura_evasion", 0))
        return min(65, max(0, int(ev)))

    def _effective_reflect(self, side: int, u: MonsterUnit) -> Tuple[int, int]:
        flat = 0 if u.is_silenced() else int(u.reflect_flat)
        rpct = 0 if u.is_silenced() else int(u.reflect_pct)
        if not u.is_silenced() and u.enrage_active:
            flat += u.enrage_reflect
        if self.env_id == 5:  # Earth Arena Reflect (base + strong Earth resonance)
            el_idx = min(2, max(0, self.env_level - 1))
            flat += (6, 10, 15)[el_idx] if u.element == 5 else (1, 2, 3)[el_idx]
        p = self.players[side]
        if self._unit_matches_leader(side, u) and p.leader_ops:
            flat += int(p.leader_ops.get("aura_reflect", 0))
        if p.covenant_active and p.covenant_ops:
            flat += int(p.covenant_ops.get("rew_reflect", 0))
            rpct += int(p.covenant_ops.get("rew_reflect_pct", 0))
        return max(0, flat), min(80, max(0, rpct))

    def _effective_card_cost(self, side: int, hc: HandCard) -> int:
        c = self.db.get(hc.card_id)
        cost = int(c.mana) + int(hc.cost_mod)
        p = self.players[side]
        # High-Tier Cost Discount passives on board
        for u in p.board_units():
            if u.is_silenced():
                continue
            if u.cost_discount_mode == 0 and c.type == "spell":
                cost -= 1
            elif u.cost_discount_mode == 1 and c.type == "monster":
                cost -= 1
            elif u.cost_discount_mode == 2:
                cost -= 1
        # Covenant spell discounts & cost taxes
        if p.covenant_active and p.covenant_ops:
            cov = p.covenant_ops
            if c.type == "spell":
                cost -= int(cov.get("rew_spell_disc", 0))
                cost += int(cov.get("spell_tax_all", 0))
            elif c.type == "monster":
                cost += int(cov.get("monster_tax_all", 0))
                if c.tier >= 5:
                    cost += int(cov.get("monster_tax_high", 0))
        return max(0, int(cost))

    def _effective_skill_cost(self, side: int, u: MonsterUnit, op: Dict[str, Any]) -> int:
        cost = int(op.get("cost", 1))
        p = self.players[side]
        # Electric Arena (-1/-1/-2 active skill cost, min 1)
        if self.env_id == 3:
            disc = 2 if self.env_level >= 3 else 1
            cost -= disc
        # Skill Conduit / Leader / Covenant / Spell buff discounts
        for ally in p.board_units():
            if not ally.is_silenced() and ally.skill_conduit > 0:
                cost -= 1
                break
        if self._unit_matches_leader(side, u) and p.leader_ops:
            cost -= int(p.leader_ops.get("aura_skill_disc", 0))
        if p.covenant_active and p.covenant_ops:
            cost -= int(p.covenant_ops.get("rew_skill_disc", 0))
        cost -= p.skill_discount_temp
        return max(1, int(cost))

    def _effective_skill_power_bonus(self, side: int) -> int:
        p = self.players[side]
        bonus = p.skill_bonus_temp
        if self.env_id == 9:  # Desert Arena: +2/+4/+6 active skill power
            bonus += (2, 4, 6)[min(2, max(0, self.env_level - 1))]
        if self.env_id == 3 and self.env_level >= 2:
            bonus += 3
        if p.covenant_active and p.covenant_ops:
            bonus += int(p.covenant_ops.get("rew_skill_power", 0))
        for u in p.board_units():
            if not u.is_silenced() and u.skill_conduit > 0:
                bonus += u.skill_conduit
        return int(bonus)

    def _effective_spell_power(self, side: int) -> int:
        p = self.players[side]
        sp = p.spell_power_temp
        if self.env_id in (0, 3) and any(u.element == self.env_id for u in p.board_units()):
            sp += (3, 5, 8)[min(2, max(0, self.env_level - 1))]
        if p.covenant_active and p.covenant_ops:
            sp += int(p.covenant_ops.get("rew_spell_power", 0))
        for u in p.board_units():
            if not u.is_silenced() and u.spell_power > 0:
                sp += u.spell_power
        return int(sp)

    # ------------------------------------------------------------------
    # Legal Action Mask & Stepping
    # ------------------------------------------------------------------
    def get_legal_mask(self, side: Optional[int] = None) -> np.ndarray:
        mask = np.zeros(ACTION_DIM, dtype=np.bool_)
        if self.done:
            mask[0] = True
            return mask

        s = self.active if side is None else side
        p = self.players[s]
        opp = self.players[1 - s]
        mask[0] = True  # End Main Phase is always legal

        if self.actions_this_turn >= 18:
            # Safety loop guard: finish main phase
            return mask

        # Covenant skip-every-3rd-turn restriction
        cov = p.covenant_ops if (p.covenant_active and p.covenant_ops) else None
        if cov and cov.get("skip_every_3rd") and self.turn % 3 == 0:
            return mask

        avail_mana = p.mana + p.temp_mana

        # 1..7: Sacrifice hand card h for +1 permanent max_mana (once/turn)
        if not p.sacrificed_this_turn and p.max_mana < p.mana_cap():
            for h in range(len(p.hand)):
                mask[1 + h] = True

        # 8..49: Play hand card h at slot slot_idx
        b_cnt = p.board_count()
        b_lim = p.board_limit()
        ban_spells = bool(cov and cov.get("ban_self_spells", False))
        max_plays = int(cov.get("max_plays_turn", 99)) if cov else 99
        max_summons = int(cov.get("max_summons_turn", 99)) if cov else 99
        ban_mon_ge = int(cov.get("ban_hand_monster_ge", 99)) if cov else 99

        if p.cards_played_turn < max_plays:
            for h, hc in enumerate(p.hand):
                c = self.db.get(hc.card_id)
                cost = self._effective_card_cost(s, hc)
                if cost > avail_mana:
                    continue
                base = 8 + h * BOARD_SLOTS
                if c.type == "monster":
                    if p.summons_turn >= max_summons or c.mana >= ban_mon_ge:
                        continue
                    if b_cnt < b_lim:
                        for slot in range(BOARD_SLOTS):
                            if p.board[slot] is None:
                                mask[base + slot] = True
                elif c.type == "environment":
                    mask[base] = True
                elif c.type == "leader":
                    if not p.leader_active and not p.leader_exorcised:
                        mask[base] = True
                elif c.type == "covenant":
                    if not p.covenant_active and not p.covenant_destroyed:
                        mask[base] = True
                elif c.type == "spell":
                    if ban_spells:
                        continue
                    sk = c.spell_ops["kind"] if c.spell_ops else "dmg_single"
                    if cov and cov.get("ban_face_spell") and sk == "dmg_leader":
                        continue
                    # Targeted enemy/ally single-slot spells allow picking occupied slots
                    if sk in ("dmg_single", "execute_enemy", "polymorph", "pull_hook_spell", "mind_control",
                              "shifting_curse", "cleave_enemy", "melt_enemy_def"):
                        any_t = False
                        for slot in range(BOARD_SLOTS):
                            u = opp.board[slot]
                            if u is not None and not u.stealth and not u.spell_immune:
                                mask[base + slot] = True
                                any_t = True
                        if not any_t and sk in ("dmg_single", "cleave_enemy"):
                            if opp.board_count() > 0:
                                mask[base] = True
                    elif sk in ("buff_ally", "stat_flip", "grant_double_strike", "double_skill_1t", "trigger_lb", "swap_lanes"):
                        for slot in range(BOARD_SLOTS):
                            u = p.board[slot]
                            if u is not None:
                                if sk == "stat_flip" and (u.is_barrier or u.atk_locked):
                                    continue
                                mask[base + slot] = True
                    elif sk == "spy_plant_spell":
                        empties = opp.empty_slots()
                        if empties:
                            for slot in empties:
                                mask[base + slot] = True
                        elif opp.board_count() > 0:
                            mask[base] = True
                    else:
                        mask[base] = True

        # 50..61: Active skills (slot 0..5, skill 0..1)
        ban_skill_gt1 = bool(cov and cov.get("ban_skill_gt1"))
        for slot in range(BOARD_SLOTS):
            u = p.board[slot]
            if u is None or u.is_barrier or u.is_spy or u.is_silenced() or u.frozen > 0:
                continue
            max_per_skill = 2 if (u.double_skill_1t or (cov and cov.get("rew_double_skill"))) else 1
            for k, op in enumerate(u.actives[:2]):
                if u.skill_uses_this_turn[k] >= max_per_skill:
                    continue
                sc = self._effective_skill_cost(s, u, op)
                if ban_skill_gt1 and sc > 1:
                    continue
                if sc <= avail_mana:
                    if op.get("kind") == "devour_3":
                        if not self._find_devour_target(s, u):
                            continue
                    mask[50 + slot * 2 + k] = True

        return mask

    def step(self, action: int) -> bool:
        """Executes one action for `self.active`. Returns True if match ended."""
        if self.done:
            return True

        s = self.active
        p = self.players[s]
        action = int(action)
        self.actions_this_turn += 1

        if action == 0:
            self._run_combat_and_end_turn(s)
            return self.done

        if 1 <= action <= 7:
            h = action - 1
            if h < len(p.hand) and not p.sacrificed_this_turn and p.max_mana < p.mana_cap():
                hc = p.hand.pop(h)
                p.graveyard.append(hc.card_id)
                p.max_mana += 1
                p.mana += 1
                p.sacrificed_this_turn = True
                p.telemetry.sacrifices_made += 1
            return self.done

        if 8 <= action <= 49:
            rel = action - 8
            h = rel // BOARD_SLOTS
            slot = rel % BOARD_SLOTS
            if h < len(p.hand):
                self._play_hand_card(s, h, slot)
            return self.done

        if 50 <= action <= 61:
            rel = action - 50
            slot = rel // 2
            k = rel % 2
            self._use_active_skill(s, slot, k)
            return self.done

        return self.done

    # ------------------------------------------------------------------
    # Playing Cards
    # ------------------------------------------------------------------
    def _spend_mana(self, side: int, cost: int) -> None:
        p = self.players[side]
        c = max(0, int(cost))
        use_temp = min(p.temp_mana, c)
        p.temp_mana -= use_temp
        c -= use_temp
        p.mana = max(0, p.mana - c)

    def _play_hand_card(self, side: int, hand_idx: int, slot: int) -> None:
        p = self.players[side]
        hc = p.hand[hand_idx]
        card = self.db.get(hc.card_id)
        cost = self._effective_card_cost(side, hc)
        if cost > p.mana + p.temp_mana:
            return

        p.hand.pop(hand_idx)
        self._spend_mana(side, cost)
        p.cards_played_turn += 1
        p.telemetry.cards_played_ids.append(card.id)

        if card.type == "environment":
            self._dmg_cause = "passive"
            self._play_environment(side, card.env_id)
            p.graveyard.append(card.id)
        elif card.type == "leader":
            self._dmg_cause = "passive"
            self._play_leader(side, card)
        elif card.type == "covenant":
            self._dmg_cause = "passive"
            self._play_covenant(side, card)
        elif card.type == "spell":
            self._dmg_cause = "spell"
            p.telemetry.spells_cast += 1
            echo = p.echo_next_spell
            p.echo_next_spell = False
            self._cast_spell(side, card, slot)
            if echo and not self.done:
                self._cast_spell(side, card, slot)
            p.graveyard.append(card.id)
            # Trigger ally on-spell passives & Leader spell-ping aura
            if not self.done:
                for u in p.board_units():
                    if not u.is_silenced():
                        if u.on_spell_atk > 0 or u.on_spell_def > 0:
                            u.apply_perm_buff(u.on_spell_atk, u.on_spell_def)
                        if u.spell_ping_dmg > 0:
                            self._damage_commander(1 - side, u.spell_ping_dmg, source_side=side)
                if self._leader_aura_on(side) and p.leader_ops and p.leader_ops.get("aura_spell_ping", 0) > 0:
                    self._damage_commander(1 - side, int(p.leader_ops["aura_spell_ping"]), source_side=side)
        elif card.type == "monster":
            self._dmg_cause = "passive"
            if p.board[slot] is None and p.board_count() < p.board_limit():
                p.summons_turn += 1
                self._summon_monster(side, card, slot)

    def _play_environment(self, side: int, env_id: int) -> None:
        if self.env_id == env_id:
            self.env_level = min(3, self.env_level + 1)
        else:
            self.env_id = env_id
            self.env_level = 1
        # Level 1 -> 10T, Level 2 -> 6T, Level 3 -> 3T
        self.env_turns = (10, 6, 3)[self.env_level - 1]
        el_idx = min(2, max(0, self.env_level - 1))

        # On-Cast Elemental Resonance Surge (scales strongly with matching Element units & upgrades existing board!)
        p = self.players[side]
        opp = self.players[1 - side]
        match_units = [u for u in p.board_units() if u.element == env_id]
        mc = len(match_units)

        if env_id == 0:  # Fire Surge: scorch enemy board & ignite Fire allies
            dmg = 8 + 5 * mc
            for eu in list(opp.board_units()):
                self._damage_unit(1 - side, eu, dmg, source_side=side)
                if eu.deff > 0:
                    self._apply_status(1 - side, eu, "burn", 4, source_side=side)
            if not opp.board_units():
                self._damage_commander(1 - side, 12 + 5 * mc, source_side=side)
            for au in match_units:
                au.apply_perm_buff(3, 3)
                au.on_hit_burn += (4, 6, 9)[el_idx]
        elif env_id == 1:  # Water Surge: heal Commander, cleanse & fortify Water allies (+max DEF)
            p.heal_hp(16 + 8 * mc)
            ed = (6, 10, 14)[el_idx]
            for au in match_units:
                au.cleanse()
                au.max_def += ed
                au.deff += ed
                p.telemetry.def_healed += ed
        elif env_id == 2:  # Nature Surge: heal Commander, grow Nature allies & spawn Treant
            p.heal_hp(18 + 8 * mc)
            for au in match_units:
                au.apply_perm_buff(4, 6)
            self._spawn_token(side, 6, 6, name="Nature Treant", haste=False)
        elif env_id == 3:  # Electric Surge: +2 temp Mana + lightning strike + empower Electric allies
            p.temp_mana += 2
            t = self._best_enemy_unit(side)
            if t:
                self._damage_unit(1 - side, t, 10 + 5 * mc, source_side=side)
            else:
                self._damage_commander(1 - side, 10 + 5 * mc, source_side=side)
            for au in match_units:
                au.apply_perm_buff(3, 4)
        elif env_id == 4:  # Ice Surge: freeze up to 2 strongest enemies & armor Ice allies
            for _ in range(2):
                t = self._best_enemy_unit(side)
                if t and t.frozen <= 0:
                    self._apply_status(1 - side, t, "freeze", 1, source_side=side)
                    self._damage_unit(1 - side, t, 8 + 4 * mc, source_side=side)
            for au in match_units:
                au.apply_perm_buff(3, 7)
        elif env_id == 5:  # Earth Surge: fortify Earth allies + reflect + Commander heal
            p.heal_hp(12 + 5 * mc)
            for au in match_units:
                au.apply_perm_buff(3, 9)
                au.reflect_flat += 5
        elif env_id == 6:  # Dark Surge: soul drain enemy Commander & grant Dark Leech
            dr = 14 + 6 * mc
            self._damage_commander(1 - side, dr, source_side=side)
            p.heal_hp(dr)
            for au in match_units:
                au.apply_perm_buff(4, 4)
                au.life_leech_pct += (30, 40, 50)[el_idx]
        elif env_id == 7:  # Light Surge: cleanse & fortify Light allies + heal Commander
            p.heal_hp(18 + 8 * mc)
            for au in match_units:
                au.cleanse()
                au.apply_perm_buff(4, 7)
        elif env_id == 8:  # Air Surge: gale strike + buff & wake Air units
            gale = 6 + 3 * mc
            for eu in list(opp.board_units()):
                self._damage_unit(1 - side, eu, gale, source_side=side)
            for au in match_units:
                au.apply_perm_buff(5, 4)
                if au.can_ever_attack():
                    au.can_attack = True
        elif env_id == 9:  # Desert Surge: sandstorm erodes non-Desert foes + bleed + empower Desert
            er = 10 + 5 * mc
            for eu in list(opp.board_units()):
                if eu.element != 9:
                    self._damage_unit(1 - side, eu, er, source_side=side)
                    if eu.deff > 0:
                        self._apply_status(1 - side, eu, "bleed", 5, source_side=side)
            for au in match_units:
                au.apply_perm_buff(4, 5)
                au.on_hit_bleed += (5, 7, 10)[el_idx]

    def _play_leader(self, side: int, card: CardDef) -> None:
        p = self.players[side]
        opp = self.players[1 - side]
        ops = card.leader_ops or {}
        p.leader_card_id = card.id
        p.leader_active = True
        p.leader_ops = ops

        # V2.1 Balance Rule: Leader Spirit's immediate On-Summon Trigger requires at least 1 matching-Tribe
        # monster on your board (preventing off-tribe decks from exploiting 0-Mana Leaders as free spells!)
        leader_tribe = int(ops.get("tribe", -2))
        has_tribe_ally = any(
            (not u.is_barrier and not u.is_spy and (u.tribe == leader_tribe or (u.chameleon and not u.is_silenced())))
            for u in p.board_units()
        )
        if not has_tribe_ally:
            return

        # Summon trigger
        if ops.get("summon_heal_hp", 0) > 0:
            p.heal_hp(int(ops["summon_heal_hp"]))
        if ops.get("summon_dmg_face", 0) > 0:
            self._damage_commander(1 - side, int(ops["summon_dmg_face"]), source_side=side)
        if ops.get("summon_dmg_enemy", 0) > 0:
            dmg = int(ops["summon_dmg_enemy"])
            for eu in list(opp.board_units()):
                self._damage_unit(1 - side, eu, dmg, source_side=side)
        if ops.get("summon_buff_atk", 0) > 0 or ops.get("summon_buff_def", 0) > 0:
            for au in p.board_units():
                au.apply_perm_buff(int(ops.get("summon_buff_atk", 0)), int(ops.get("summon_buff_def", 0)))
        for _ in range(int(ops.get("summon_draw", 0))):
            self._draw_card(side, check_deckout=True)
        if ops.get("summon_mana", 0) > 0:
            p.temp_mana += int(ops["summon_mana"])
        if ops.get("summon_cleanse"):
            for au in p.board_units():
                au.cleanse()
        if ops.get("summon_freeze"):
            t = self._best_enemy_unit(side)
            if t:
                self._apply_status(1 - side, t, "freeze", 1, source_side=side)
        if ops.get("summon_silence"):
            t = self._best_enemy_unit(side)
            if t:
                self._apply_status(1 - side, t, "silence", 1, source_side=side)
        if ops.get("summon_root"):
            t = self._best_enemy_unit(side)
            if t:
                self._apply_status(1 - side, t, "root", 2, source_side=side)
        if ops.get("summon_weaken", 0) > 0:
            for eu in opp.board_units():
                eu.weakened += int(ops["summon_weaken"])
        if ops.get("summon_clone"):
            src = self._best_enemy_unit(side, max_mana=4) or self._best_ally_unit(side)
            empties = p.empty_slots()
            if src and src.card_id > 0 and empties and p.board_count() < p.board_limit():
                self._summon_monster(side, self.db.get(src.card_id), empties[0], is_clone=True, stat_pct=70)
                p.telemetry.clones_made += 1
        if ops.get("summon_copy_hand") and opp.hand:
            p.add_to_hand(self.rng.choice(opp.hand).card_id, cost_mod=-1)
            p.telemetry.clones_made += 1
        if ops.get("summon_mind_control"):
            t = self._best_enemy_unit(side, max_mana=3)
            empties = p.empty_slots()
            if t and empties and p.board_count() < p.board_limit():
                opp.board[t.slot] = None
                ns = empties[0]
                t.owner = side; t.slot = ns; t.temp_mc_orig_owner = 1 - side
                t.can_attack = t.can_ever_attack()
                p.board[ns] = t
                p.telemetry.clones_made += 1
        if ops.get("summon_recall"):
            t = self._best_enemy_unit(side, max_mana=3)
            if t:
                self._recall_unit(1 - side, t, source_side=side)
        if ops.get("summon_burn", 0) > 0:
            for eu in opp.board_units():
                self._apply_status(1 - side, eu, "burn", int(ops["summon_burn"]), source_side=side)
        if ops.get("summon_poison", 0) > 0:
            for eu in opp.board_units():
                self._apply_status(1 - side, eu, "poison", int(ops["summon_poison"]), source_side=side)
        if ops.get("summon_bleed", 0) > 0:
            for eu in opp.board_units():
                self._apply_status(1 - side, eu, "bleed", int(ops["summon_bleed"]), source_side=side)
        for _ in range(int(ops.get("summon_tokens", 0))):
            self._spawn_token(side, 5, 5, name="Leader Token", haste=True)
        if ops.get("summon_revive"):
            self._revive_from_grave(side, cap=99, count=1, haste=True)
        if ops.get("summon_double_skill"):
            for au in p.board_units():
                if not au.is_barrier:
                    au.double_skill_1t = True
                    break

    def _play_covenant(self, side: int, card: CardDef) -> None:
        p = self.players[side]
        ops = card.covenant_ops or {}
        p.covenant_card_id = card.id
        p.covenant_active = True
        p.covenant_ops = ops

        # Apply immediate self-restriction caps
        hp_init_loss = int(ops.get("self_hp_init_loss", 0))
        if hp_init_loss > 0:
            p.max_hp = max(50, p.max_hp - hp_init_loss)
            p.hp = min(p.hp, p.max_hp)
        hp_cap = int(ops.get("self_hp_cap", 0))
        if hp_cap > 0 and p.hp > hp_cap:
            p.max_hp = min(p.max_hp, hp_cap)
            p.hp = min(p.hp, hp_cap)
        def_cap = int(ops.get("self_def_cap", 0))
        if def_cap > 0:
            for u in p.board_units():
                u.max_def = min(u.max_def, def_cap)
                u.deff = min(u.deff, def_cap)
        if p.max_mana > p.mana_cap():
            p.max_mana = p.mana_cap()
            p.mana = min(p.mana, p.max_mana)
        # Enforce hand cap (burns oldest)
        cap_h = p.hand_limit()
        while len(p.hand) > cap_h:
            p.graveyard.append(p.hand.pop(0).card_id)

    # ------------------------------------------------------------------
    # Monster Summoning & Passive Initialization
    # ------------------------------------------------------------------
    def _summon_monster(
        self,
        side: int,
        card: CardDef,
        slot: int,
        is_clone: bool = False,
        is_resurrected: bool = False,
        stat_pct: int = 100,
    ) -> Optional[MonsterUnit]:
        p = self.players[side]
        if p.board[slot] is not None or p.board_count() >= p.board_limit():
            return None

        atk = 0 if card.is_barrier else max(0, pct_of(card.atk, stat_pct))
        deff = max(1, pct_of(card.deff, stat_pct))

        # Covenant DEF cap / penalty
        if p.covenant_active and p.covenant_ops:
            deff = max(1, deff - int(p.covenant_ops.get("self_def_pen", 0)))
            dpct = int(p.covenant_ops.get("self_def_pct_pen", 0))
            if dpct > 0:
                deff = max(1, deff - pct_of(deff, dpct))
            dcap = int(p.covenant_ops.get("self_def_cap", 0))
            if dcap > 0:
                deff = min(deff, dcap)
            deff += int(p.covenant_ops.get("rew_def", 0))

        u = MonsterUnit(
            card_id=card.id,
            name=card.name,
            owner=side,
            slot=slot,
            element=card.element,
            tribe=card.tribe,
            playstyle=card.playstyle,
            role=card.role,
            tier=card.tier,
            mana=card.mana,
            atk=atk,
            deff=deff,
            is_barrier=card.is_barrier,
            is_all_attack=card.is_all_attack,
            is_clone=is_clone,
            is_resurrected=is_resurrected,
            passives=card.passives,
            actives=card.actives,
            summoned_turn=self.turn,
        )
        p.board[slot] = u

        # Leader Owner-Only DEF aura on summon
        if self._unit_matches_leader(side, u) and p.leader_ops:
            ldef = int(p.leader_ops.get("aura_def", 0))
            if ldef > 0:
                u.deff += ldef
                u.max_def += ldef
            u.life_leech_pct += int(p.leader_ops.get("aura_leech", 0))
            u.on_hit_burn += int(p.leader_ops.get("aura_burn", 0))
            u.on_hit_poison += int(p.leader_ops.get("aura_poison", 0))
            u.on_hit_bleed += int(p.leader_ops.get("aura_bleed", 0))

        # Covenant summon keywords
        if p.covenant_active and p.covenant_ops:
            cov = p.covenant_ops
            if cov.get("rew_haste") and u.can_ever_attack():
                u.haste = True
            if cov.get("rew_taunt"):
                u.taunt = True
            u.life_leech_pct += int(cov.get("rew_leech_pct", 0))
            u.on_hit_burn += int(cov.get("rew_burn", 0))
            u.on_hit_poison += int(cov.get("rew_poison", 0))
            u.on_hit_bleed += int(cov.get("rew_bleed", 0))

        # Shared Environment Elemental Resonance on summon (Water/Nature/Earth/Light DEF, Fire Burn, Dark Leech, Air Haste, Desert Bleed)
        if self.env_id >= 0 and u.element == self.env_id:
            el_idx = min(2, max(0, self.env_level - 1))
            if self.env_id in (1, 2, 5, 7):  # Water / Nature / Earth / Light DEF bonus on summon
                ed = (6, 10, 14)[el_idx]
                u.deff += ed
                u.max_def += ed
            elif self.env_id == 0:  # Fire on-hit Burn
                u.on_hit_burn += (3, 5, 7)[el_idx]
            elif self.env_id == 6:  # Dark Life Leech
                u.life_leech_pct += (25, 35, 45)[el_idx]
            elif self.env_id == 8 and u.can_ever_attack():  # Air Haste on summon
                u.haste = True
            elif self.env_id == 9:  # Desert on-hit Bleed
                u.on_hit_bleed += (4, 6, 9)[el_idx]
        elif self.env_id == 8 and self.env_level >= 2 and u.can_ever_attack():
            u.haste = True

        # Register & trigger monster passives
        self._init_unit_passives(side, u)

        if u.haste and u.can_ever_attack():
            u.can_attack = True
        return u

    def _init_unit_passives(self, side: int, u: MonsterUnit) -> None:
        p = self.players[side]
        opp = self.players[1 - side]
        for pa in u.passives:
            k = pa.get("kind", "")
            v1 = int(pa.get("v1", 0))
            v2 = int(pa.get("v2", 0))
            pct = int(pa.get("pct", 0))

            if k == "all_attack":
                u.is_all_attack = True
                u.atk_locked = True
                if v1 > 0:
                    u.apply_perm_buff(0, v1)
                if pa.get("spell_imm"):
                    u.spell_immune = True
            elif k == "spy_plant":
                self._plant_spy(side, deff=max(8, v1), dmg=max(3, v2), poison=bool(pa.get("poison")))
            elif k == "deck_burn_summon":
                burned = 0
                for _ in range(max(1, v1)):
                    if opp.deck:
                        opp.graveyard.append(opp.deck.pop())
                        burned += 1
                p.telemetry.disruptions_applied += burned
            elif k == "hand_discard_summon":
                if opp.hand:
                    idx = self.rng.randrange(len(opp.hand))
                    opp.graveyard.append(opp.hand.pop(idx).card_id)
                    p.telemetry.disruptions_applied += 1
                if v2 > 0:
                    self._damage_commander(1 - side, v2, source_side=side)
            elif k == "perm_mana_summon":
                if p.max_mana < p.mana_cap():
                    p.max_mana += 1
                if pa.get("temp"):
                    p.temp_mana += 1
                if v2 > 0:
                    p.heal_hp(v2)
                if pa.get("bdef", 0) > 0:
                    u.apply_perm_buff(0, int(pa["bdef"]))
            elif k == "temp_mana_summon":
                p.temp_mana += max(1, v1)
                if v2 > 0:
                    u.apply_perm_buff(0, v2)
            elif k == "summon_token":
                for _ in range(int(pa.get("count", 1))):
                    self._spawn_token(side, v1, v2, name="Hive Token")
            elif k == "lone_wolf":
                u.lone_wolf_atk += v1
                u.lone_wolf_def += v2
                if p.board_count() == 1:
                    u.apply_perm_buff(0, v2)
            elif k == "open_lane":
                u.open_lane_atk += v1
            elif k == "chameleon":
                u.chameleon = True
                if v1 > 0:
                    u.apply_perm_buff(0, v1)
            elif k == "doppelganger":
                target = self._best_enemy_unit(side, max_mana=v1)
                if target is not None and not u.is_barrier:
                    u.atk = 0 if target.is_barrier else target.atk
                    u.deff = max(1, target.deff + v2)
                    u.max_def = u.deff
                    u.taunt = target.taunt
                    u.haste = target.haste
                    u.evasion_pct = target.evasion_pct
                    p.telemetry.clones_made += 1
            elif k == "cost_discount":
                u.cost_discount_mode = v1
            elif k == "mana_lock_summon":
                opp.locked_mana_next = min(4, opp.locked_mana_next + max(1, v1))
                p.telemetry.disruptions_applied += max(1, v1)
            elif k == "undying_1def":
                u.undying_ready = True
                if v1 > 0:
                    u.apply_perm_buff(0, v1)
            elif k == "arena_upgrade_once":
                if self.env_id >= 0:
                    self.env_level = min(3, self.env_level + 1)
                    self.env_turns += max(1, v1)
                elif 0 <= u.element < 10:
                    self._play_environment(side, u.element)
            elif k == "grave_scaling":
                u.grave_scale_atk += v1
                u.grave_scale_def += v2
            elif k == "summon_draw":
                self._draw_card(side, check_deckout=True)
                if v2 > 0:
                    u.apply_perm_buff(0, v2)
            elif k == "summon_clone_self":
                if not u.is_clone:
                    empties = p.empty_slots()
                    if empties and p.board_count() < p.board_limit():
                        self._summon_monster(side, self.db.get(u.card_id), empties[0], is_clone=True, stat_pct=max(30, pct))
                        p.telemetry.clones_made += 1
            elif k == "taunt_reflect_pct":
                u.taunt = True
                u.reflect_pct += pct
                if v1 > 0:
                    u.apply_perm_buff(0, v1)
            elif k == "taunt_evasion":
                u.taunt = True
                u.evasion_pct += pct
            elif k == "taunt":
                u.taunt = True
                if v1 > 0:
                    u.apply_perm_buff(0, v1)
            elif k == "double_strike":
                u.double_strike = True
                u.apply_perm_buff(v1, v2)
            elif k == "haste":
                u.haste = True
                if v1 > 0:
                    u.apply_perm_buff(v1, 0)
                if v2 > 0:
                    u.on_hit_burn += v2
                if pa.get("tok_atk", 0) > 0:
                    for ally in p.board_units():
                        if ally.is_token:
                            ally.apply_perm_buff(int(pa["tok_atk"]), 0)
            elif k == "cleave":
                u.cleave_pct += pct
                if v1 > 0:
                    u.on_hit_burn += v1
            elif k == "spell_ping":
                u.spell_ping_dmg += max(1, v1)
            elif k in ("bypass", "pierce"):
                u.bypass_pct = max(u.bypass_pct, pct)
            elif k == "stealth":
                u.stealth = True
                if v1 > 0:
                    u.apply_perm_buff(v1, 0)
            elif k == "evasion":
                u.evasion_pct += pct
                if v1 > 0:
                    u.apply_perm_buff(v1, 0)
            elif k == "enrage":
                u.enrage_atk += v1
                u.enrage_reflect += v2
            elif k == "spell_immune":
                u.spell_immune = True
                if v1 > 0:
                    u.apply_perm_buff(0, v1)
            elif k == "reflect":
                u.reflect_flat += v1
            elif k == "on_hit_burn":
                u.on_hit_burn += v1
            elif k == "on_hit_poison":
                u.on_hit_poison += v1
            elif k == "on_hit_bleed":
                u.on_hit_bleed += v1
            elif k == "on_death_regen":
                u.on_death_regen_def += v1
                u.on_death_regen_hp += v2
            elif k == "life_leech":
                u.life_leech_pct += pct
                if v1 > 0:
                    u.apply_perm_buff(v1, 0)
            elif k == "summon_freeze":
                t = self._best_enemy_unit(side, max_mana=v1)
                if t:
                    self._apply_status(1 - side, t, "freeze", 1, source_side=side)
                    if v2 > 0:
                        self._damage_unit(1 - side, t, v2, source_side=side)
            elif k == "summon_silence":
                t = self._best_enemy_unit(side)
                if t:
                    self._apply_status(1 - side, t, "silence", int(pa.get("dur", 1)), source_side=side)
                    if v1 > 0:
                        self._damage_unit(1 - side, t, v1, source_side=side)
            elif k == "rooted_immune_regen":
                u.status_immune = True
                if v1 > 0:
                    u.apply_perm_buff(0, v1)
                if v2 > 0:
                    u.dmg_reduce_flat += v2
            elif k == "root_lock":
                t = self._best_enemy_unit(side)
                if t:
                    self._apply_status(1 - side, t, "root", int(pa.get("dur", 1)), source_side=side)
                    if pa.get("silence"):
                        self._apply_status(1 - side, t, "silence", 1, source_side=side)
                    if v1 > 0:
                        self._damage_unit(1 - side, t, v1, source_side=side)
            elif k == "summon_recall_enemy":
                t = self._best_enemy_unit(side, max_mana=v1)
                if t:
                    self._recall_unit(1 - side, t, source_side=side)
                if v2 > 0:
                    u.apply_perm_buff(0, v2)
            elif k == "summon_steal_atk":
                t = self._best_enemy_unit(side)
                if t:
                    t.weakened += v1
                    u.apply_perm_buff(v1, 0)
                    p.telemetry.disruptions_applied += 1
            elif k == "strip_buffs_hit":
                u.strip_buffs_bonus += v1
            elif k == "aura_weaken_opp":
                u.aura_weaken_opp += v1
            elif k == "summon_tax_mana":
                opp.locked_mana_next = min(4, opp.locked_mana_next + 1)
                p.telemetry.disruptions_applied += 1
                if v2 > 0:
                    self._damage_commander(1 - side, v2, source_side=side)
            elif k == "cleanse_heal_aura":
                if pa.get("summon"):
                    for ally in p.board_units():
                        ally.cleanse()
                        p.telemetry.def_healed += ally.heal_def(v1)
                else:
                    u.cleanse_aura_def += v1
            elif k == "turn_heal_aura":
                u.turn_heal_def += v1
                u.turn_heal_hp += v2
            elif k == "status_immune":
                u.status_immune = True
                if v1 > 0:
                    u.apply_perm_buff(0, v1)
            elif k == "skill_conduit":
                u.skill_conduit += max(1, v2)
            elif k == "on_spell_grow":
                u.on_spell_atk += v1
                u.on_spell_def += v2
            elif k == "on_skill_grow":
                u.on_skill_atk += v1
                u.on_skill_def += v2
            elif k == "on_death_grow":
                u.on_death_atk += v1
                u.on_death_def += v2
                u.on_death_dmg += int(pa.get("dmg", 0))
            elif k == "flank_bonus":
                u.flank_atk += v1
            elif k == "pack_aura":
                u.pack_atk += v1
                u.pack_def += v2
            elif k == "adj_buff_summon":
                for adj in (u.slot - 1, u.slot + 1):
                    if 0 <= adj < BOARD_SLOTS and p.board[adj] is not None:
                        p.board[adj].apply_perm_buff(v1, v2)
            elif k == "spell_power_aura":
                u.spell_power += max(1, v1)
            elif k == "bonus_stats_summon":
                u.apply_perm_buff(v1, v2)

            # Optional elemental/tribal rider
            rider = pa.get("rider")
            if rider:
                rk = rider.get("r", "")
                rv = int(rider.get("v", 0))
                if rk == "burn":
                    u.on_hit_burn += rv
                elif rk == "poison":
                    u.on_hit_poison += rv
                elif rk == "cleanse_def":
                    u.cleanse()
                    u.apply_perm_buff(0, rv)
                elif rk == "heal_hp":
                    p.heal_hp(rv)
                elif rk == "ping_opp":
                    t = self._best_enemy_unit(side)
                    if t:
                        self._damage_unit(1 - side, t, rv, source_side=side)
                elif rk == "weaken_opp":
                    t = self._best_enemy_unit(side)
                    if t:
                        t.weakened += rv
                elif rk == "def_reflect":
                    u.apply_perm_buff(0, rv)
                    u.reflect_flat += int(rider.get("v2", 2))
                elif rk == "leech_hp":
                    p.heal_hp(rv)
                elif rk == "evasion":
                    u.evasion_pct += rv
                elif rk == "root_opp":
                    t = self._best_enemy_unit(side)
                    if t:
                        self._apply_status(1 - side, t, "root", 1, source_side=side)
                elif rk == "def_bonus":
                    u.apply_perm_buff(0, rv)

    # ------------------------------------------------------------------
    # Active Skills (Monster Slots 0..5, Skill 0..1)
    # ------------------------------------------------------------------
    def _use_active_skill(self, side: int, slot: int, skill_idx: int) -> None:
        p = self.players[side]
        opp = self.players[1 - side]
        u = p.board[slot]
        if u is None or u.is_barrier or u.is_spy or u.is_silenced() or u.frozen > 0:
            return
        if skill_idx < 0 or skill_idx >= len(u.actives) or skill_idx >= 2:
            return
        cov = p.covenant_ops if p.covenant_active else None
        max_per_skill = 2 if (u.double_skill_1t or (cov and cov.get("rew_double_skill"))) else 1
        if u.skill_uses_this_turn[skill_idx] >= max_per_skill:
            return
        op = u.actives[skill_idx]
        cost = self._effective_skill_cost(side, u, op)
        if cost > p.mana + p.temp_mana:
            return

        self._spend_mana(side, cost)
        u.skill_uses_this_turn[skill_idx] += 1
        u.skills_used_this_turn += 1
        u.stealth = False
        p.telemetry.actives_used += 1
        self._dmg_cause = "active"

        # Trigger on-skill passives
        for ally in p.board_units():
            if not ally.is_silenced() and (ally.on_skill_atk > 0 or ally.on_skill_def > 0):
                ally.apply_perm_buff(ally.on_skill_atk, ally.on_skill_def)

        bonus = self._effective_skill_power_bonus(side)
        k = op.get("kind", "dmg_single")
        v1 = int(op.get("v1", 0))
        v2 = int(op.get("v2", 0))

        if k == "warrior_call":
            self._exec_warrior_call(side, op)
        elif k == "devour_3":
            target_info = self._find_devour_target(side, u)
            if target_info is not None:
                t_side, t_unit = target_info
                pct = int(op.get("pct", 50))
                g_atk = 0 if t_unit.is_barrier else pct_of(t_unit.atk, pct)
                g_def = pct_of(t_unit.deff, pct)
                self._destroy_unit(t_side, t_unit, killer_side=side)
                u.apply_perm_buff(g_atk, g_def)
        elif k == "pull_hook":
            self._exec_pull_hook(side, u.slot, dmg=v1 + bonus)
        elif k == "shifting_curse":
            t = self._best_enemy_unit(side)
            if t:
                t.shifting_curse_dmg = max(t.shifting_curse_dmg, v1 + bonus)
                t.shifting_curse_enemy_only = bool(op.get("enemy_only", True))
                p.telemetry.disruptions_applied += 1
        elif k == "mind_control_temp":
            t = self._best_enemy_unit(side, max_mana=int(op.get("cap", 99)))
            empties = p.empty_slots()
            if t and empties and p.board_count() < p.board_limit():
                opp.board[t.slot] = None
                ns = empties[0]
                t.owner = side
                t.slot = ns
                t.temp_mc_orig_owner = 1 - side
                t.can_attack = t.can_ever_attack()
                t.apply_temp_buff(v1, 0)
                p.board[ns] = t
                p.telemetry.disruptions_applied += 1
                p.telemetry.clones_made += 1
        elif k == "double_skill_1t":
            ally = self._best_ally_unit(side, exclude=u, non_barrier=True) or u
            ally.double_skill_1t = True
            p.skill_bonus_temp += max(0, v1)
        elif k == "grant_double_strike":
            ally = self._best_ally_unit(side, non_barrier=True) or u
            ally.double_strike_temp = True
            ally.apply_temp_buff(v1, 0)
        elif k == "recall_ally":
            ally = self._best_ally_unit(side, exclude=u)
            if ally:
                if self._recall_unit(side, ally, source_side=side):
                    if v1 > 0:
                        p.heal_hp(v1)
                    if op.get("mana", 0) > 0:
                        p.temp_mana += int(op["mana"])
        elif k == "recall_enemy":
            t = self._best_enemy_unit(side, max_mana=int(op.get("cap", 99)))
            if t:
                if v1 > 0:
                    self._damage_unit(1 - side, t, v1 + bonus, source_side=side)
                if t.deff > 0:
                    self._recall_unit(1 - side, t, source_side=side)
        elif k == "aoe_enemy":
            dmg = v1 + bonus
            fx = op.get("fx", "")
            for eu in list(opp.board_units()):
                dealt = self._damage_unit(1 - side, eu, dmg, source_side=side)
                if op.get("leech", 0) > 0 and dealt > 0:
                    p.heal_hp(pct_of(dealt, int(op["leech"])))
                if fx and eu.deff > 0:
                    self._apply_status(1 - side, eu, fx, v2, source_side=side)
            if op.get("cleanse"):
                u.cleanse()
        elif k == "cleave_enemy":
            t = self._best_enemy_unit(side)
            if t:
                dmg = v1 + bonus
                fx = op.get("fx", "")
                for sl in (t.slot - 1, t.slot, t.slot + 1):
                    if 0 <= sl < BOARD_SLOTS and opp.board[sl] is not None:
                        eu = opp.board[sl]
                        self._damage_unit(1 - side, eu, dmg, source_side=side)
                        if fx and eu.deff > 0:
                            self._apply_status(1 - side, eu, fx, v2, source_side=side)
                if op.get("heal_hp", 0) > 0:
                    p.heal_hp(int(op["heal_hp"]))
                if op.get("self_def", 0) > 0:
                    u.apply_perm_buff(0, int(op["self_def"]))
        elif k == "aoe_ally":
            for au in p.board_units():
                au.apply_perm_buff(int(op.get("atk", 0)), int(op.get("deff", 0)) + (bonus if op.get("deff", 0) > 0 else 0))
                if op.get("cleanse"):
                    au.cleanse()
                if op.get("haste") and au.can_ever_attack():
                    au.haste = True
                    au.can_attack = True
                if op.get("reflect", 0) > 0:
                    au.reflect_flat += int(op["reflect"])
                if op.get("ev", 0) > 0:
                    au.evasion_pct += int(op["ev"])
                if op.get("leech", 0) > 0:
                    au.life_leech_pct += int(op["leech"])
                if op.get("imm"):
                    au.status_immune = True
            if op.get("hp", 0) > 0:
                p.heal_hp(int(op["hp"]) + bonus)
        elif k == "buff_ally":
            au = self._best_ally_unit(side) or u
            au.apply_perm_buff(int(op.get("atk", 0)), int(op.get("deff", 0)) + (bonus if op.get("deff", 0) > 0 else 0))
            if op.get("cleanse"):
                au.cleanse()
            if op.get("haste") and au.can_ever_attack():
                au.haste = True
                au.can_attack = True
            if op.get("taunt"):
                au.taunt = True
            if op.get("stealth"):
                au.stealth = True
            if op.get("imm"):
                au.status_immune = True
            if op.get("reflect", 0) > 0:
                au.reflect_flat += int(op["reflect"])
            if op.get("ev", 0) > 0:
                au.evasion_pct += int(op["ev"])
            if op.get("leech", 0) > 0:
                au.life_leech_pct += int(op["leech"])
            if op.get("self_dmg", 0) > 0:
                self._damage_commander(side, int(op["self_dmg"]), source_side=-1)
        elif k == "revive_board":
            self._revive_from_grave(side, cap=int(op.get("cap", 99)), count=1, bonus_atk=v1)
        elif k == "grave_to_hand":
            self._grave_to_hand(side, count=1, disc=1, cap=int(op.get("cap", 99)))
        elif k == "spawn_token":
            for _ in range(int(op.get("count", 1))):
                self._spawn_token(side, v1, v2, name="Summoned Token", haste=bool(op.get("haste")))
        elif k == "gain_temp_mana":
            p.temp_mana += max(1, v1)
            if v2 > 0:
                p.heal_hp(v2)
        elif k == "drain_enemy_mana":
            opp.locked_mana_next = min(4, opp.locked_mana_next + max(1, v1))
            p.telemetry.disruptions_applied += max(1, v1)
            if v2 > 0:
                self._damage_commander(1 - side, v2 + bonus, source_side=side)
        elif k == "detonate_dots":
            self._detonate_dots(side, mult=1, extra_dmg=v1 + bonus)
        elif k == "copy_enemy_hand":
            if opp.hand:
                chosen = self.rng.choice(opp.hand)
                p.add_to_hand(chosen.card_id, cost_mod=-1)
                p.telemetry.clones_made += 1
            if v1 > 0:
                u.apply_perm_buff(0, v1)
        elif k == "heal_leader":
            p.heal_hp(v1 + bonus)
            if v2 > 0:
                u.apply_perm_buff(0, v2)
            if op.get("cleanse"):
                u.cleanse()
        elif k == "dmg_leader":
            self._damage_commander(1 - side, v1 + bonus, source_side=side)
            if op.get("heal", 0) > 0:
                p.heal_hp(int(op["heal"]))
            if op.get("double_burn"):
                for eu in opp.board_units():
                    if eu.burn > 0:
                        eu.burn = min(30, eu.burn * 2)
        elif k == "discount_hand_spells":
            for hc in p.hand:
                if self.db.get(hc.card_id).type == "spell":
                    hc.cost_mod = max(-4, hc.cost_mod - 1)
            if v2 > 0:
                p.heal_hp(v2)
        elif k == "buff_center":
            for cs in CENTER_SLOTS:
                if p.board[cs] is not None:
                    p.board[cs].apply_perm_buff(0, v1)
            if v2 > 0:
                p.heal_hp(v2 + bonus)
        else:
            # Single-target damage / status active
            t = self._best_enemy_unit(side)
            dmg = v1 + bonus
            if t is not None:
                dealt = self._damage_unit(1 - side, t, dmg, source_side=side)
                if op.get("exec_below", 0) > 0 and t.deff > 0 and t.deff <= int(op["exec_below"]):
                    self._destroy_unit(1 - side, t, killer_side=side)
                if op.get("leech", 0) > 0 and dealt > 0:
                    p.heal_hp(pct_of(dealt, int(op["leech"])))
                fx = op.get("fx", "")
                if fx and t.deff > 0:
                    self._apply_status(1 - side, t, fx, v2 if fx in ("burn", "poison", "bleed", "weaken") else int(op.get("dur", 1)), source_side=side)
            else:
                self._damage_commander(1 - side, dmg, source_side=side)
            if op.get("heal_hp", 0) > 0:
                p.heal_hp(int(op["heal_hp"]))
            if op.get("self_def", 0) > 0:
                u.apply_perm_buff(0, int(op["self_def"]))
            st = op.get("spawn_tok")
            if st:
                self._spawn_token(side, int(st[0]), int(st[1]), name="Skill Token")

    # ------------------------------------------------------------------
    # Spell Casting (All 300 Spells & 41 Opcodes)
    # ------------------------------------------------------------------
    def _cast_spell(self, side: int, card: CardDef, slot: int) -> None:
        p = self.players[side]
        opp = self.players[1 - side]
        op = card.spell_ops or {"kind": "dmg_single", "v1": 10}
        k = op.get("kind", "dmg_single")
        sp = self._effective_spell_power(side)
        draw_n = int(op.get("draw", 0))

        # --- 4 Counter Tech Spells ---
        if k == "counter_betrayal":
            self._damage_commander(side, int(op.get("self_dmg", 40)), source_side=-1)
            if opp.covenant_active:
                opp.covenant_active = False
                opp.covenant_destroyed = True
                opp.covenant_ops = None
                p.telemetry.disruptions_applied += 3
                self._add_highlight(side, "Counter_Tech_Break")
        elif k == "counter_stand_alone":
            if opp.leader_active:
                opp.leader_muted_turns = max(opp.leader_muted_turns, int(op.get("dur", 3)))
                p.telemetry.disruptions_applied += 2
                self._add_highlight(side, "Counter_Tech_Break")
        elif k == "counter_oblivion":
            if opp.covenant_active:
                opp.covenant_active = False
                opp.covenant_destroyed = True
                opp.covenant_ops = None
                p.telemetry.disruptions_applied += 3
                self._add_highlight(side, "Counter_Tech_Break")
        elif k == "counter_exorcism":
            if opp.leader_active:
                opp.leader_active = False
                opp.leader_exorcised = True
                opp.leader_ops = None
                p.telemetry.disruptions_applied += 3
                self._add_highlight(side, "Counter_Tech_Break")

        # --- New Signature Spells ---
        elif k == "time_rewind":
            for au in p.board_units():
                au.cleanse()
                if au.prev_turn_def > au.deff:
                    p.telemetry.def_healed += au.heal_def(au.prev_turn_def - au.deff)
            p.heal_hp(int(op.get("hp", 35)))
            self._add_highlight(side, "Time_Rewind_Miracle")
        elif k == "polymorph":
            t = self._pick_enemy_target(side, slot, max_mana=int(op.get("cap", 99)))
            if t is not None and not t.spell_immune:
                ts = t.slot
                sheep = MonsterUnit(
                    card_id=0, name="Sheep Token", owner=1 - side, slot=ts,
                    element=-1, tribe=-1, playstyle=-1, role=0, tier=1, mana=1,
                    atk=1, deff=1, is_token=True, summoned_turn=self.turn,
                )
                opp.board[ts] = sheep
                p.telemetry.disruptions_applied += 2
                self._add_highlight(side, "Polymorph_Sheep")
        elif k == "stat_flip":
            self._add_highlight(side, "Stat_Flip_Surprise")
            targets: List[MonsterUnit] = []
            au = p.board[slot] if (0 <= slot < BOARD_SLOTS and p.board[slot] is not None) else None
            if au is not None and not au.is_barrier and not au.atk_locked:
                targets.append(au)
            else:
                for u in p.board_units():
                    if not u.is_barrier and not u.atk_locked:
                        targets.append(u)
                        if not op.get("twin") or len(targets) >= 2:
                            break
            if op.get("twin") and len(targets) == 1:
                for u in p.board_units():
                    if u is not targets[0] and not u.is_barrier and not u.atk_locked:
                        targets.append(u)
                        break
            for u in targets:
                old_a, old_d = u.atk, u.deff
                u.atk = max(0, old_d)
                u.deff = max(1, old_a + int(op.get("bonus_def", 0)))
                u.max_def = max(u.max_def, u.deff)
            if op.get("hp", 0) > 0:
                p.heal_hp(int(op["hp"]))
        elif k == "doppelganger_spell":
            t = self._best_enemy_unit(side, max_mana=int(op.get("cap", 99)))
            empties = p.empty_slots()
            if t and empties and p.board_count() < p.board_limit() and t.card_id > 0:
                clone = self._summon_monster(side, self.db.get(t.card_id), empties[0], is_clone=True)
                if clone and op.get("haste") and clone.can_ever_attack():
                    clone.haste = True
                    clone.can_attack = True
                p.telemetry.clones_made += 1
        elif k == "shifting_curse":
            t = self._pick_enemy_target(side, slot)
            if t and not t.spell_immune:
                t.shifting_curse_dmg = max(t.shifting_curse_dmg, int(op.get("v1", 5)) + sp)
                t.shifting_curse_enemy_only = bool(op.get("enemy_only", False))
                p.telemetry.disruptions_applied += 1
        elif k == "spy_plant_spell":
            if opp.board[slot] is None:
                self._plant_spy(side, deff=int(op.get("v1", 15)), dmg=int(op.get("v2", 5)),
                                poison=bool(op.get("poison")), preferred_slot=slot)
            else:
                if not self._plant_spy(side, deff=int(op.get("v1", 15)), dmg=int(op.get("v2", 5)),
                                       poison=bool(op.get("poison"))):
                    t = self._best_enemy_unit(side)
                    if t:
                        self._damage_unit(1 - side, t, int(op.get("fallback_dmg", 14)) + sp, source_side=side)
        elif k == "pull_hook_spell":
            self._exec_pull_hook(side, slot, burn=int(op.get("burn", 0)), root=bool(op.get("root")))
        elif k == "deck_burn_spell":
            n = int(op.get("v1", 4))
            for _ in range(n):
                if opp.deck:
                    opp.graveyard.append(opp.deck.pop())
                    p.telemetry.disruptions_applied += 1
            if op.get("burn", 0) > 0:
                for eu in opp.board_units():
                    self._apply_status(1 - side, eu, "burn", int(op["burn"]), source_side=side)
            if op.get("weaken", 0) > 0:
                for eu in opp.board_units():
                    eu.weakened += int(op["weaken"])
        elif k == "hand_discard_spell":
            if opp.hand:
                opp.graveyard.append(opp.hand.pop(self.rng.randrange(len(opp.hand))).card_id)
                p.telemetry.disruptions_applied += 1
            if op.get("dmg", 0) > 0:
                self._damage_commander(1 - side, int(op["dmg"]) + sp, source_side=side)

        # --- Core Spells ---
        elif k == "mind_control":
            cnt = int(op.get("cnt", 1))
            perm = bool(op.get("perm", False))
            cap = int(op.get("cap", 99))
            for _ in range(cnt):
                t = self._pick_enemy_target(side, slot, max_mana=cap)
                empties = p.empty_slots()
                if t and not t.spell_immune and empties and p.board_count() < p.board_limit():
                    opp.board[t.slot] = None
                    ns = empties[0]
                    t.owner = side
                    t.slot = ns
                    t.temp_mc_orig_owner = -1 if perm else (1 - side)
                    t.can_attack = t.can_ever_attack()
                    if op.get("dbl"):
                        t.double_strike_temp = True
                    p.board[ns] = t
                    p.telemetry.disruptions_applied += 2
                    p.telemetry.clones_made += 2 if perm else 1
        elif k == "clone_board":
            cnt = int(op.get("cnt", 1))
            pct = int(op.get("pct", 100))
            cap = int(op.get("cap", 99))
            src = self._best_enemy_unit(side, max_mana=cap) if op.get("from_enemy") else self._best_ally_unit(side)
            if src and src.card_id > 0:
                for _ in range(cnt):
                    empties = p.empty_slots()
                    if not empties or p.board_count() >= p.board_limit():
                        break
                    cl = self._summon_monster(side, self.db.get(src.card_id), empties[0], is_clone=True, stat_pct=pct)
                    if cl:
                        if op.get("haste") and cl.can_ever_attack():
                            cl.haste = True
                            cl.can_attack = True
                        if op.get("stealth"):
                            cl.stealth = True
                        p.telemetry.clones_made += 1
        elif k == "revive_board":
            self._revive_from_grave(
                side, cap=int(op.get("cap", 99)), count=int(op.get("cnt", 1)),
                haste=bool(op.get("haste")), bonus_atk=int(op.get("atk", 0)),
                from_enemy=bool(op.get("from_enemy")),
            )
            if op.get("hp", 0) > 0:
                p.heal_hp(int(op["hp"]))
        elif k == "double_skill_1t":
            cnt = int(op.get("cnt", 1))
            units = [p.board[slot]] if (cnt == 1 and p.board[slot] is not None) else p.board_units()
            applied = 0
            for au in units:
                if au is not None and not au.is_barrier:
                    au.double_skill_1t = True
                    if op.get("dbl_atk"):
                        au.double_strike_temp = True
                    applied += 1
                    if applied >= cnt:
                        break
            if op.get("mana", 0) > 0:
                p.temp_mana += int(op["mana"])
            if op.get("free"):
                p.skill_discount_temp += 5
        elif k == "grant_double_strike":
            cnt = int(op.get("cnt", 1))
            units = [p.board[slot]] if (cnt == 1 and p.board[slot] is not None) else p.board_units()
            applied = 0
            for au in units:
                if au is not None and not au.is_barrier:
                    au.double_strike_temp = True
                    au.apply_temp_buff(int(op.get("atk", 0)), 0)
                    applied += 1
                    if applied >= cnt:
                        break
            if op.get("mana", 0) > 0:
                p.temp_mana += int(op["mana"])
        elif k == "recall_spell":
            cnt = int(op.get("cnt", 1))
            if op.get("enemy"):
                for _ in range(cnt):
                    t = self._best_enemy_unit(side, max_mana=int(op.get("cap", 99)))
                    if t and not t.spell_immune:
                        self._recall_unit(1 - side, t, source_side=side)
            else:
                for _ in range(cnt):
                    au = self._best_ally_unit(side)
                    if au:
                        self._recall_unit(side, au, source_side=side)
            if op.get("hp", 0) > 0:
                p.heal_hp(int(op["hp"]))
            if op.get("mana", 0) > 0:
                p.temp_mana += int(op["mana"])
            if op.get("spawn"):
                ta, td = op["spawn"]
                self._spawn_token(side, int(ta), int(td), name="Recall Token")
        elif k == "perm_mana":
            if op.get("hp_cost", 0) > 0:
                self._damage_commander(side, int(op["hp_cost"]), source_side=-1)
            if op.get("sac"):
                au = self._best_ally_unit(side)
                if au:
                    self._destroy_unit(side, au, killer_side=side)
            p.max_mana = min(p.mana_cap(), p.max_mana + max(1, int(op.get("v1", 1))))
            p.mana = min(p.max_mana, p.mana + 1)
            if op.get("hp_heal", 0) > 0:
                p.heal_hp(int(op["hp_heal"]))
        elif k == "reduce_enemy_max_mana":
            opp.max_mana = max(1, opp.max_mana - max(1, int(op.get("v1", 1))))
            p.telemetry.disruptions_applied += 2
            if op.get("dmg", 0) > 0:
                self._damage_commander(1 - side, int(op["dmg"]) + sp, source_side=side)
            if op.get("silence_all"):
                for eu in opp.board_units():
                    self._apply_status(1 - side, eu, "silence", 1, source_side=side)
        elif k == "drain_enemy_mana":
            opp.locked_mana_next = min(4, opp.locked_mana_next + max(1, int(op.get("v1", 2))))
            p.telemetry.disruptions_applied += 2
            if op.get("gain", 0) > 0:
                p.temp_mana += int(op["gain"])
            if op.get("freeze"):
                t = self._best_enemy_unit(side)
                if t:
                    self._apply_status(1 - side, t, "freeze", 1, source_side=side)
        elif k == "temp_mana":
            if op.get("hp_cost", 0) > 0:
                self._damage_commander(side, int(op["hp_cost"]), source_side=-1)
            p.temp_mana += max(1, int(op.get("v1", 2)))
            if op.get("sp", 0) > 0:
                p.spell_power_temp += int(op["sp"])
        elif k == "spawn_tokens":
            if op.get("sac"):
                au = self._best_ally_unit(side)
                if au:
                    self._destroy_unit(side, au, killer_side=side)
            for _ in range(int(op.get("cnt", 1))):
                self._spawn_token(side, int(op.get("v1", 5)), int(op.get("v2", 5)),
                                  name="Swarm Token", haste=bool(op.get("haste")))
            if op.get("atk", 0) > 0:
                for au in p.board_units():
                    au.apply_temp_buff(int(op["atk"]), 0)
        elif k == "detonate_dots":
            self._detonate_dots(side, mult=int(op.get("mult", 1)),
                                extra_dmg=int(op.get("dmg", 0)) + sp, leech=bool(op.get("leech")))
        elif k == "melt_enemy_def":
            pct = int(op.get("pct", 50))
            targets = list(opp.board_units()) if op.get("aoe") else ([self._pick_enemy_target(side, slot)] if self._pick_enemy_target(side, slot) else [])
            for eu in targets:
                if eu is None or eu.spell_immune:
                    continue
                cut = max(1, pct_of(eu.deff, pct))
                eu.deff = max(1, eu.deff - cut)
                eu.max_def = max(1, eu.max_def - cut)
                if op.get("poison", 0) > 0:
                    self._apply_status(1 - side, eu, "poison", int(op["poison"]), source_side=side)
                if op.get("bleed", 0) > 0:
                    self._apply_status(1 - side, eu, "bleed", int(op["bleed"]), source_side=side)
                if op.get("dmg", 0) > 0:
                    self._damage_unit(1 - side, eu, int(op["dmg"]) + sp, source_side=side)
        elif k == "execute_enemy":
            if op.get("sac"):
                au = self._best_ally_unit(side)
                if au:
                    self._destroy_unit(side, au, killer_side=side)
            t = self._pick_enemy_target(side, slot)
            if t and not t.spell_immune:
                if t.deff <= int(op.get("cap", 999)):
                    self._destroy_unit(1 - side, t, killer_side=side)
                else:
                    self._damage_unit(1 - side, t, int(op.get("fallback", 20)) + sp, source_side=side)
                if op.get("splash", 0) > 0:
                    for eu in list(opp.board_units()):
                        self._damage_unit(1 - side, eu, int(op["splash"]) + sp, source_side=side)
                if op.get("poison_all", 0) > 0:
                    for eu in opp.board_units():
                        self._apply_status(1 - side, eu, "poison", int(op["poison_all"]), source_side=side)
        elif k == "aoe_enemy":
            dmg = int(op.get("v1", 12)) + sp
            fx = op.get("fx", "")
            v2 = int(op.get("v2", 3))
            dur = int(op.get("dur", 1))
            total_dealt = 0
            for eu in list(opp.board_units()):
                if eu.spell_immune:
                    continue
                total_dealt += self._damage_unit(1 - side, eu, dmg, source_side=side)
                if fx and eu.deff > 0:
                    self._apply_status(1 - side, eu, fx, v2 if fx in ("burn", "poison", "bleed", "weaken") else dur, source_side=side)
            if op.get("face", 0) > 0:
                self._damage_commander(1 - side, int(op["face"]) + sp, source_side=side)
                p.telemetry.direct_face_spells += 1
            if op.get("leech") and total_dealt > 0:
                p.heal_hp(pct_of(total_dealt, 50))
            if op.get("heal", 0) > 0:
                p.heal_hp(int(op["heal"]))
        elif k == "cleave_enemy":
            t = self._pick_enemy_target(side, slot)
            if t:
                dmg = int(op.get("v1", 12)) + sp
                fx = op.get("fx", "")
                v2 = int(op.get("v2", 3))
                for sl in (t.slot - 1, t.slot, t.slot + 1):
                    if 0 <= sl < BOARD_SLOTS and opp.board[sl] is not None and not opp.board[sl].spell_immune:
                        eu = opp.board[sl]
                        self._damage_unit(1 - side, eu, dmg, source_side=side)
                        if fx and eu.deff > 0:
                            self._apply_status(1 - side, eu, fx, v2, source_side=side)
        elif k == "aoe_ally":
            for au in p.board_units():
                au.apply_perm_buff(int(op.get("atk", 0)), int(op.get("deff", 0)))
                if op.get("full_heal"):
                    p.telemetry.def_healed += au.heal_def(au.max_def)
                if op.get("cleanse"):
                    au.cleanse()
                if op.get("haste") and au.can_ever_attack():
                    au.haste = True
                    au.can_attack = True
                if op.get("reflect", 0) > 0:
                    au.reflect_flat += int(op["reflect"])
                if op.get("reflect_pct", 0) > 0:
                    au.reflect_pct += int(op["reflect_pct"])
                if op.get("imm"):
                    au.status_immune = True
            if op.get("hp", 0) > 0:
                p.heal_hp(int(op["hp"]))
        elif k == "dmg_leader":
            dmg = int(op.get("v1", 18)) + sp
            gm = int(op.get("grave_mult", 0))
            if gm > 0:
                dmg += min(60, gm * len(p.graveyard))
            self._damage_commander(1 - side, dmg, source_side=side)
            p.telemetry.direct_face_spells += 1
            if op.get("self_dmg", 0) > 0:
                self._damage_commander(side, int(op["self_dmg"]), source_side=-1)
        elif k == "heal_leader":
            mhp = int(op.get("max_hp", 0))
            if mhp > 0:
                p.max_hp = min(700, p.max_hp + mhp)
            p.heal_hp(int(op.get("v1", 25)) + sp)
            if op.get("cleanse"):
                for au in p.board_units():
                    au.cleanse()
        elif k == "pure_draw":
            if op.get("hp", 0) > 0:
                p.heal_hp(int(op["hp"]))
            if op.get("mana", 0) > 0:
                p.temp_mana += int(op["mana"])
            if op.get("disc", 0) > 0 and p.hand:
                p.hand[-1].cost_mod = max(-4, p.hand[-1].cost_mod - 1)
        elif k == "buff_ally":
            cnt = int(op.get("cnt", 1))
            units = [p.board[slot]] if (cnt == 1 and p.board[slot] is not None) else p.board_units()
            applied = 0
            for au in units:
                if au is None:
                    continue
                au.apply_perm_buff(int(op.get("atk", 0)), int(op.get("deff", 0)))
                if op.get("taunt"):
                    au.taunt = True
                if op.get("haste") and au.can_ever_attack():
                    au.haste = True
                    au.can_attack = True
                if op.get("stealth"):
                    au.stealth = True
                if op.get("cleanse"):
                    au.cleanse()
                if op.get("imm"):
                    au.status_immune = True
                if op.get("bypass"):
                    au.bypass_pct = max(au.bypass_pct, 75)
                if op.get("reflect", 0) > 0:
                    au.reflect_flat += int(op["reflect"])
                if op.get("reflect_pct", 0) > 0:
                    au.reflect_pct += int(op["reflect_pct"])
                if op.get("ev", 0) > 0:
                    au.evasion_pct += int(op["ev"])
                if op.get("leech", 0) > 0:
                    au.life_leech_pct += int(op["leech"])
                applied += 1
                if applied >= cnt:
                    break
        elif k == "grave_to_hand":
            if op.get("hp_cost", 0) > 0:
                self._damage_commander(side, int(op["hp_cost"]), source_side=-1)
            self._grave_to_hand(side, count=int(op.get("cnt", 1)), disc=int(op.get("disc", 1)))
            if op.get("hp", 0) > 0:
                p.heal_hp(int(op["hp"]))
        elif k == "copy_enemy_hand":
            for _ in range(int(op.get("cnt", 1))):
                if opp.hand:
                    c_pick = self.rng.choice(opp.hand)
                    p.add_to_hand(c_pick.card_id, cost_mod=-int(op.get("disc", 1)))
                    p.telemetry.clones_made += 1
        elif k == "trigger_lb":
            au = p.board[slot] if (0 <= slot < BOARD_SLOTS and p.board[slot] is not None) else self._best_ally_unit(side)
            if au:
                self._trigger_last_breath(side, au)
            if op.get("dmg", 0) > 0:
                self._damage_commander(1 - side, int(op["dmg"]) + sp, source_side=side)
        elif k == "swap_lanes":
            au = p.board[slot] if (0 <= slot < BOARD_SLOTS and p.board[slot] is not None) else self._best_ally_unit(side)
            if au:
                other = (slot + 3) % BOARD_SLOTS
                p.board[au.slot], p.board[other] = p.board[other], p.board[au.slot]
                if p.board[au.slot] is not None:
                    p.board[au.slot].slot = au.slot
                au.slot = other
                au.apply_perm_buff(int(op.get("atk", 4)), int(op.get("deff", 0)))
        elif k == "boost_skills":
            p.skill_bonus_temp += int(op.get("v1", 6))
            p.skill_discount_temp += int(op.get("disc", 0))
            if op.get("mana", 0) > 0:
                p.temp_mana += int(op["mana"])
            if op.get("refresh"):
                for au in p.board_units():
                    au.skills_used_this_turn = 0
                    au.skill_uses_this_turn = [0, 0]
        elif k == "echo_next_spell":
            p.echo_next_spell = True
            p.spell_power_temp += int(op.get("v1", 0))
        elif k == "random_bolts":
            bolts = int(op.get("cnt", 4))
            bdmg = int(op.get("dmg", 8)) + sp
            for _ in range(bolts):
                if self.done:
                    break
                eus = [u for u in opp.board_units() if not u.spell_immune]
                if eus and self.rng.random() < 0.7:
                    self._damage_unit(1 - side, self.rng.choice(eus), bdmg, source_side=side)
                else:
                    self._damage_commander(1 - side, bdmg, source_side=side)
        elif k == "solar_sacrifice":
            au = self._best_ally_unit(side, non_barrier=True)
            if au:
                dmg = max(1, pct_of(self._effective_atk(side, au), int(op.get("pct", 150)))) + sp
                self._destroy_unit(side, au, killer_side=side)
                t = self._best_enemy_unit(side)
                if t:
                    self._damage_unit(1 - side, t, dmg, source_side=side)
                else:
                    self._damage_commander(1 - side, dmg, source_side=side)
        elif k == "def_slam":
            au = max(p.board_units(), key=lambda u: u.deff, default=None)
            if au:
                dmg = max(1, pct_of(au.deff, int(op.get("pct", 50)))) + sp
                t = self._best_enemy_unit(side)
                if t and not t.spell_immune:
                    self._damage_unit(1 - side, t, dmg, source_side=side)
                else:
                    self._damage_commander(1 - side, dmg, source_side=side)
        elif k == "swap_atk_enemy":
            au = self._best_ally_unit(side, non_barrier=True)
            eu = self._best_enemy_unit(side)
            if au and eu and not eu.is_barrier and not eu.atk_locked and not au.atk_locked and not eu.spell_immune:
                au.atk, eu.atk = eu.atk, au.atk
        elif k == "swarm_bite":
            dmg_each = int(op.get("v1", 4)) + (1 if sp > 0 else 0)
            for au in p.board_units():
                if self.done:
                    break
                opp_u = opp.board[au.slot]
                if opp_u is not None and not opp_u.spell_immune:
                    self._damage_unit(1 - side, opp_u, dmg_each, source_side=side)
                else:
                    self._damage_commander(1 - side, dmg_each, source_side=side)
        else:
            # Default single-target damage / status spell
            t = self._pick_enemy_target(side, slot)
            dmg = int(op.get("v1", 10)) + sp
            if t is not None and not t.spell_immune:
                if op.get("bonus_if_dmg", 0) > 0 and t.deff < t.max_def:
                    dmg = max(dmg, int(op["bonus_if_dmg"]) + sp)
                pre_def = t.deff
                dealt = self._damage_unit(1 - side, t, dmg, source_side=side)
                if op.get("leech", 0) > 0 and dealt > 0:
                    p.heal_hp(int(op["leech"]))
                if op.get("overkill") and dmg > pre_def:
                    self._damage_commander(1 - side, dmg - pre_def, source_side=side)
                if op.get("mana_on_kill") and t.deff <= 0:
                    p.temp_mana += 1
                fx = op.get("fx", "")
                if fx and t.deff > 0:
                    v2 = int(op.get("v2", 3))
                    dur = int(op.get("dur", 1))
                    self._apply_status(1 - side, t, fx, v2 if fx in ("burn", "poison", "bleed", "weaken") else dur, source_side=side)
            else:
                self._damage_commander(1 - side, dmg, source_side=side)
                p.telemetry.direct_face_spells += 1

        # Draw riders attached to spells
        for _ in range(draw_n):
            if self.done:
                break
            self._draw_card(side, check_deckout=True)

    # ------------------------------------------------------------------
    # Combat Phase & End of Turn
    # ------------------------------------------------------------------
    def _run_combat_and_end_turn(self, side: int) -> None:
        p = self.players[side]
        opp = self.players[1 - side]

        cov = p.covenant_ops if (p.covenant_active and p.covenant_ops) else None
        ban_normal = bool(cov and cov.get("ban_normal_attack")) or bool(cov and cov.get("skip_every_3rd") and self.turn % 3 == 0)
        ban_face = bool(cov and cov.get("ban_face_attack"))
        hp_on_atk = int(cov.get("self_hp_on_attack", 0)) if cov else 0
        single_atk = bool(cov and cov.get("single_attacker_turn"))
        attackers_done = 0

        if not ban_normal:
            for slot in range(BOARD_SLOTS):
                if self.done:
                    break
                if single_atk and attackers_done >= 1:
                    break
                u = p.board[slot]
                if u is None or not u.can_attack or u.attacked_this_turn or u.frozen > 0 or not u.can_ever_attack():
                    continue
                atk = self._effective_atk(side, u)
                if atk <= 0:
                    continue

                attackers_done += 1
                if hp_on_atk > 0:
                    self._damage_commander(side, hp_on_atk, source_side=-1)
                    if self.done:
                        break

                strikes = 2 if ((u.double_strike and not u.is_silenced()) or u.double_strike_temp) else 1
                u.attacked_this_turn = True
                u.attacks_made += 1
                u.stealth = False

                for _ in range(strikes):
                    if self.done or p.board[slot] is not u or u.deff <= 0:
                        break
                    # Bleed triggers on attack (splashes if lethal)
                    if u.bleed > 0:
                        bleed_d = u.bleed
                        self._damage_unit(side, u, bleed_d, source_side=1 - side, is_dot=True)
                        if u.deff <= 0:
                            self._damage_commander(side, bleed_d, source_side=1 - side)
                            break

                    atk = self._effective_atk(side, u)
                    if atk <= 0:
                        break

                    if u.is_all_attack:
                        # All-Attack strikes ALL enemy monsters simultaneously (or face if empty)
                        enemies = list(opp.board_units())
                        if enemies:
                            for eu in enemies:
                                if not self.done and u.deff > 0:
                                    self._resolve_monster_strike(side, u, eu, atk)
                        elif not ban_face:
                            self._strike_commander(side, u, atk)
                    else:
                        # Bypass hits Commander directly for pct_of(atk, bypass_pct)
                        if u.bypass_pct > 0 and not u.is_silenced() and not ban_face:
                            b_dmg = max(1, pct_of(atk, u.bypass_pct))
                            self._strike_commander(side, u, b_dmg)
                            continue

                        target = self._pick_combat_target(side, u.slot)
                        if target is not None:
                            self._resolve_monster_strike(side, u, target, atk)
                        elif not ban_face:
                            self._strike_commander(side, u, atk)

        if self.done:
            return

        # --- End of Turn Phase ---
        # 1. Spy tokens planted on enemy board damage adjacent enemy monsters
        for s_slot in range(BOARD_SLOTS):
            eu = opp.board[s_slot]
            if eu is not None and eu.is_spy:
                for adj in (s_slot - 1, s_slot + 1):
                    if 0 <= adj < BOARD_SLOTS and opp.board[adj] is not None and not opp.board[adj].is_spy:
                        adj_u = opp.board[adj]
                        self._damage_unit(1 - side, adj_u, eu.spy_dmg, source_side=side, is_dot=True)
                        if eu.spy_poison and adj_u.deff > 0:
                            self._apply_status(1 - side, adj_u, "poison", 2, source_side=side)

        # 2. DoTs & Shifting Curse tick on active player's units (and return temp mind control)
        for slot in range(BOARD_SLOTS):
            if self.done:
                break
            u = p.board[slot]
            if u is None:
                continue

            # Burn ticks & splashes on kill
            if u.burn > 0:
                bd = u.burn
                pre = u.deff
                self._damage_unit(side, u, bd, source_side=1 - side, is_dot=True)
                if u.deff <= 0 and bd > pre:
                    self._damage_commander(side, bd - pre, source_side=1 - side)
                elif u.deff > 0:
                    u.burn = max(0, u.burn - 1)
            if p.board[slot] is not u or u.deff <= 0:
                continue

            # Poison erodes DEF & max_def
            if u.poison > 0:
                pd = u.poison
                u.max_def = max(1, u.max_def - 1)
                self._damage_unit(side, u, pd, source_side=1 - side, is_dot=True)
                if u.deff > 0:
                    u.poison = max(0, u.poison - 1)
            if p.board[slot] is not u or u.deff <= 0:
                continue

            # Shifting Curse tick
            if u.shifting_curse_dmg > 0:
                self._damage_unit(side, u, u.shifting_curse_dmg, source_side=1 - side, is_dot=True)
            if p.board[slot] is not u or u.deff <= 0:
                continue

            # Decrement timed debuffs
            if u.frozen > 0:
                u.frozen -= 1
            if u.silenced > 0:
                u.silenced -= 1
            if u.rooted > 0:
                u.rooted -= 1
            if u.weakened > 0:
                u.weakened = max(0, u.weakened - 2)

            # Return temporary mind-controlled unit at end of turn
            if u.temp_mc_orig_owner >= 0 and u.temp_mc_orig_owner != side:
                orig = u.temp_mc_orig_owner
                orig_p = self.players[orig]
                empties = orig_p.empty_slots()
                p.board[slot] = None
                if empties:
                    ns = empties[0]
                    u.owner = orig
                    u.slot = ns
                    u.temp_mc_orig_owner = -1
                    orig_p.board[ns] = u
                else:
                    if u.card_id > 0:
                        orig_p.graveyard.append(u.card_id)

        if self.done:
            return

        # 3. Covenant End-of-Turn self-restrictions (e.g., ID 146 Discard Whole Hand, Ally Self-Dmg, Discard Cheapest)
        if p.covenant_active and p.covenant_ops:
            cov = p.covenant_ops
            if cov.get("discard_hand_eot") and p.hand:
                while p.hand:
                    p.graveyard.append(p.hand.pop().card_id)
            elif cov.get("discard_cheapest_2t") and self.turn % 2 == 0 and p.hand:
                cheap_i = min(range(len(p.hand)), key=lambda idx: self.db.get(p.hand[idx].card_id).mana)
                p.graveyard.append(p.hand.pop(cheap_i).card_id)
            ally_dmg = int(cov.get("ally_dmg_eot", 0))
            if ally_dmg > 0:
                for au in list(p.board_units()):
                    self._damage_unit(side, au, ally_dmg, source_side=-1, is_dot=True)

        # 4. Roll up turn-level Excitement & Anomaly telemetry (zero overhead!)
        self._record_turn_excitement(side)

        if self.done:
            return

        # Advance turn
        self.active = 1 - side
        if self.active == 0:
            self.turn += 1
            # Constitution Sudden Death Penalty: after Turn 40, BOTH players lose 100 HP every round!
            if self.turn > 40:
                p0, p1 = self.players[0], self.players[1]
                p0.hp -= 100
                p1.hp -= 100
                if p0.hp <= 0 or p1.hp <= 0:
                    if p0.hp > p1.hp:
                        p0.hp = max(0, p0.hp)
                        p1.hp = max(0, p1.hp)
                        self._finish(0, "SuddenDeathHP")
                    elif p1.hp > p0.hp:
                        p0.hp = max(0, p0.hp)
                        p1.hp = max(0, p1.hp)
                        self._finish(1, "SuddenDeathHP")
                    else:
                        p0.hp = max(0, p0.hp)
                        p1.hp = max(0, p1.hp)
                        self._finish(-1, "SuddenDeathDraw")
                    return
        if self.turn > MAX_TURNS:
            if self.players[0].hp > self.players[1].hp:
                self._finish(0, "TurnLimitHP")
            elif self.players[1].hp > self.players[0].hp:
                self._finish(1, "TurnLimitHP")
            else:
                self._finish(-1, "TurnLimitDraw")
            return

        self._begin_turn(self.active)

    def _pick_combat_target(self, side: int, slot: int) -> Optional[MonsterUnit]:
        opp = self.players[1 - side]
        # Taunt takes priority (nearest Taunt)
        taunts = [u for u in opp.board_units() if u.taunt and not u.stealth]
        if taunts:
            taunts.sort(key=lambda u: abs(u.slot - slot))
            return taunts[0]
        # Direct opposite lane
        opp_u = opp.board[slot]
        if opp_u is not None and not opp_u.stealth:
            return opp_u
        return None

    def _resolve_monster_strike(self, side: int, attacker: MonsterUnit, defender: MonsterUnit, atk: int) -> None:
        p = self.players[side]
        opp_side = 1 - side
        opp = self.players[opp_side]

        # Evasion check
        ev = self._effective_evasion(opp_side, defender)
        if ev > 0 and self.rng.random() * 100.0 < ev:
            opp.telemetry.evasions_triggered += 1
            return

        # Strip buffs bonus passive
        dmg = int(atk)
        if not attacker.is_silenced() and attacker.strip_buffs_bonus > 0:
            if defender.perm_atk_buff > 0 or defender.perm_def_buff > 0:
                defender.atk = max(0, defender.atk - defender.perm_atk_buff)
                defender.perm_atk_buff = 0
                dmg += attacker.strip_buffs_bonus

        # Ice Arena Fortress Armor (strong damage reduction for Ice units + mild global protection)
        if self.env_id == 4:
            el_idx = min(2, max(0, self.env_level - 1))
            red = (5, 8, 12)[el_idx] if defender.element == 4 else (1, 2, 3)[el_idx]
            dmg = max(1, dmg - red)

        self._dmg_cause = "attack"
        dealt = self._damage_unit(opp_side, defender, dmg, source_side=side, killer_unit=attacker)

        # Ice Arena elemental resonance: Ice attackers have 35%/50%/70% Freeze on hit
        if dealt > 0 and defender.deff > 0 and self.env_id == 4 and attacker.element == 4:
            el_idx = min(2, max(0, self.env_level - 1))
            if self.rng.random() * 100.0 < (35, 50, 70)[el_idx]:
                self._apply_status(opp_side, defender, "freeze", 1, source_side=side)

        # On-hit DoTs & Life Leech
        if dealt > 0 and not attacker.is_silenced():
            if attacker.life_leech_pct > 0:
                p.heal_hp(pct_of(dealt, attacker.life_leech_pct))
            if defender.deff > 0:
                if attacker.on_hit_burn > 0:
                    self._apply_status(opp_side, defender, "burn", attacker.on_hit_burn, source_side=side)
                if attacker.on_hit_poison > 0:
                    self._apply_status(opp_side, defender, "poison", attacker.on_hit_poison, source_side=side)
                if attacker.on_hit_bleed > 0:
                    self._apply_status(opp_side, defender, "bleed", attacker.on_hit_bleed, source_side=side)

            # Cleave splash to adjacent enemy slots
            if attacker.cleave_pct > 0:
                cdmg = max(1, pct_of(dmg, attacker.cleave_pct))
                for adj in (defender.slot - 1, defender.slot + 1):
                    if 0 <= adj < BOARD_SLOTS and opp.board[adj] is not None:
                        self._damage_unit(opp_side, opp.board[adj], cdmg, source_side=side)

        # Reflect / Thorns retaliation (automatic, works even on Barriers!)
        self._dmg_cause = "counter"
        r_flat, r_pct = self._effective_reflect(opp_side, defender)
        ref_dmg = r_flat + (pct_of(dmg, r_pct) if r_pct > 0 else 0)
        if ref_dmg > 0 and attacker.deff > 0:
            opp.telemetry.reflect_dmg_dealt += ref_dmg
            self._damage_unit(side, attacker, ref_dmg, source_side=opp_side)

        # Defender counter-attack (ONLY if defender survived, is NOT Barrier/Spy, NOT Frozen, and ATK > 0)
        if defender.deff > 0 and attacker.deff > 0 and defender.can_ever_attack() and defender.frozen <= 0:
            c_atk = self._effective_atk(opp_side, defender)
            if c_atk > 0:
                self._damage_unit(side, attacker, c_atk, source_side=opp_side)

    def _strike_commander(self, side: int, attacker: MonsterUnit, dmg: int) -> None:
        self._damage_commander(1 - side, dmg, source_side=side)
        if not attacker.is_silenced() and attacker.life_leech_pct > 0:
            self.players[side].heal_hp(pct_of(dmg, attacker.life_leech_pct))

    # ------------------------------------------------------------------
    # Damage, Death, Undying, Hydra Split, Last Breath & Special Helpers
    # ------------------------------------------------------------------
    def _damage_commander(self, target_side: int, amount: int, source_side: int) -> None:
        amt = max(0, int(amount))
        if amt <= 0 or self.done:
            return
        tp = self.players[target_side]
        tp.hp -= amt
        if source_side in (0, 1) and source_side != target_side:
            self.players[source_side].telemetry.dmg_to_face += amt
            self._turn_dmg += amt
        # Update live HP deficits for comeback detection
        d0 = self.players[1].hp - self.players[0].hp
        if d0 > self._max_deficit[0]:
            self._max_deficit[0] = d0
        if -d0 > self._max_deficit[1]:
            self._max_deficit[1] = -d0
        if tp.hp <= 0:
            tp.hp = 0
            self._finish(1 - target_side, "CommanderHP")

    def _damage_unit(
        self,
        owner_side: int,
        u: MonsterUnit,
        amount: int,
        source_side: int,
        is_dot: bool = False,
        killer_unit: Optional[MonsterUnit] = None,
    ) -> int:
        if u.deff <= 0 or self.done:
            return 0
        amt = max(0, int(amount) - (0 if u.is_silenced() else u.dmg_reduce_flat))
        if amt <= 0:
            return 0

        # Undying 1 DEF once-per-game save
        if amt >= u.deff and u.undying_ready and not u.undying_used and not u.is_silenced():
            dealt = max(0, u.deff - 1)
            u.deff = 1
            u.undying_used = True
            u.undying_ready = False
            self._add_highlight(owner_side, "Undying_1DEF_Save")
        else:
            dealt = min(u.deff, amt)
            u.deff -= dealt

        cause = "dot" if is_dot else self._dmg_cause
        if source_side in (0, 1) and source_side != owner_side:
            st = self.players[source_side].telemetry
            st.dmg_to_monsters += dealt
            self._turn_dmg += dealt
            if is_dot:
                st.dot_dmg_dealt += dealt
            if cause == "active":
                st.dmg_m_by_active += dealt
            elif cause == "attack":
                st.dmg_m_by_attack += dealt
            elif cause == "spell":
                st.dmg_m_by_spell += dealt
            elif cause == "counter":
                st.dmg_m_by_counter += dealt
            elif cause == "dot":
                st.dmg_m_by_dot += dealt
            else:
                st.dmg_m_by_passive += dealt

        if u.deff <= 0:
            self._destroy_unit(owner_side, u, killer_side=source_side, killer_unit=killer_unit, cause=cause)
            return dealt

        # Survived hit -> Enrage activation & Hydra Split check
        if not u.is_silenced():
            if not u.enrage_active and (u.enrage_atk > 0 or u.enrage_reflect > 0) and u.deff < u.max_def:
                u.enrage_active = True
            if u.hydra_split_turn != self.turn:
                for pa in u.passives:
                    if pa.get("kind") == "hydra_split":
                        p = self.players[owner_side]
                        empties = p.empty_slots()
                        if empties and p.board_count() < p.board_limit():
                            u.hydra_split_turn = self.turn
                            ca = 0 if u.is_barrier else max(1, pct_of(u.atk, int(pa.get("pct", 50))))
                            cd = max(1, pct_of(u.deff, int(pa.get("pct", 50))))
                            self._spawn_token(owner_side, ca, cd, name="Hydra Head", slot=empties[0])
                            p.telemetry.clones_made += 1
                            self._add_highlight(owner_side, "Hydra_Split")
                        break
        return dealt

    def _destroy_unit(
        self,
        owner_side: int,
        u: MonsterUnit,
        killer_side: int = -1,
        killer_unit: Optional[MonsterUnit] = None,
        cause: Optional[str] = None,
    ) -> None:
        p = self.players[owner_side]
        if 0 <= u.slot < BOARD_SLOTS and p.board[u.slot] is u:
            p.board[u.slot] = None
        u.deff = 0
        if not u.is_token and not u.is_spy and u.card_id > 0:
            p.graveyard.append(u.card_id)

        c_tag = cause or self._dmg_cause

        # Track monster board lifespan & survival buckets (non-token, non-spy units)
        if not u.is_token and not u.is_spy:
            ot = p.telemetry
            ot.units_died_count += 1
            rnds = u.own_turns_survived
            ot.units_died_rounds_sum += rnds
            if rnds <= 0:
                ot.units_died_0rnd += 1
            elif rnds == 1:
                ot.units_died_1rnd += 1
            elif rnds == 2:
                ot.units_died_2rnd += 1
            else:
                ot.units_died_3p_rnd += 1
            if not u.is_barrier:
                ot.combat_died_count += 1
                if u.attacks_made == 0:
                    ot.combat_died_0atk += 1

        if killer_side in (0, 1) and killer_side != owner_side:
            kt = self.players[killer_side].telemetry
            kt.monsters_killed += 1
            self._turn_kills += 1
            if not u.is_token and not u.is_spy:
                if c_tag == "active":
                    kt.kills_by_active += 1
                elif c_tag == "attack":
                    kt.kills_by_attack += 1
                elif c_tag == "spell":
                    kt.kills_by_spell += 1
                elif c_tag == "counter":
                    kt.kills_by_counter += 1
                elif c_tag == "dot":
                    kt.kills_by_dot += 1
                else:
                    kt.kills_by_passive += 1

        # Killer unit Ascend kill check
        if killer_unit is not None and killer_unit.deff > 0 and not killer_unit.ascended and not killer_unit.is_silenced():
            for pa in killer_unit.passives:
                if pa.get("kind") == "ascend":
                    killer_unit.ascend_kills += 1
                    self._trigger_ascend(killer_unit.owner, killer_unit, pa)
                    break

        # Shifting Curse jump on death!
        if u.shifting_curse_dmg > 0:
            pool = (
                list(p.board_units())
                if u.shifting_curse_enemy_only
                else list(self.players[0].board_units()) + list(self.players[1].board_units())
            )
            if pool:
                nxt = self.rng.choice(pool)
                nxt.shifting_curse_dmg = max(nxt.shifting_curse_dmg, u.shifting_curse_dmg)
                nxt.shifting_curse_enemy_only = u.shifting_curse_enemy_only

        # Last Breath triggers (isolate cause tag so LB deaths credit 'passive'!)
        if not u.is_silenced():
            _prev_cause = self._dmg_cause
            self._dmg_cause = "passive"
            self._trigger_last_breath(owner_side, u)
            self._dmg_cause = _prev_cause

        # Leader Owner-Only death-ping aura & board on-death passives
        if self._leader_aura_on(owner_side) and p.leader_ops:
            dp = int(p.leader_ops.get("aura_death_ping", 0))
            if dp > 0:
                self._damage_commander(1 - owner_side, dp, source_side=owner_side)

        for s_idx in (0, 1):
            for au in list(self.players[s_idx].board_units()):
                if au.is_silenced():
                    continue
                if au.on_death_atk > 0 or au.on_death_def > 0:
                    au.apply_perm_buff(au.on_death_atk, au.on_death_def)
                if au.on_death_dmg > 0:
                    self._damage_commander(1 - s_idx, au.on_death_dmg, source_side=s_idx)
                if au.on_death_regen_def > 0:
                    self.players[s_idx].telemetry.def_healed += au.heal_def(au.on_death_regen_def)
                if au.on_death_regen_hp > 0:
                    self.players[s_idx].heal_hp(au.on_death_regen_hp)

    def _trigger_last_breath(self, side: int, u: MonsterUnit) -> None:
        p = self.players[side]
        opp = self.players[1 - side]
        for pa in u.passives:
            k = pa.get("kind", "")
            v1 = int(pa.get("v1", 0))
            v2 = int(pa.get("v2", 0))
            if k == "lb_token":
                for _ in range(int(pa.get("count", 1))):
                    self._spawn_token(side, v1, v2, name="Brood Token")
            elif k == "lb_draw":
                self._draw_card(side, check_deckout=True)
                if v2 > 0:
                    p.heal_hp(v2)
            elif k == "lb_clone_hand":
                if u.card_id > 0:
                    p.add_to_hand(u.card_id, cost_mod=-max(1, v1))
                    p.telemetry.clones_made += 1
            elif k == "lb_freeze":
                t = self._best_enemy_unit(side)
                if t:
                    self._apply_status(1 - side, t, "freeze", int(pa.get("dur", 1)), source_side=side)
                    if v1 > 0:
                        self._damage_unit(1 - side, t, v1, source_side=side)
            elif k == "lb_grave_return":
                if v1 > 0:
                    self._damage_commander(1 - side, v1, source_side=side)
                self._grave_to_hand(side, count=1, disc=max(1, v2))
            elif k == "lb_burn_boom":
                for eu in list(opp.board_units()):
                    self._damage_unit(1 - side, eu, v1, source_side=side)
                    if eu.deff > 0:
                        self._apply_status(1 - side, eu, "burn", 2, source_side=side)
            elif k == "lb_poison":
                for eu in list(opp.board_units()):
                    self._apply_status(1 - side, eu, "poison", max(1, v1), source_side=side)
                    if v2 > 0:
                        self._damage_unit(1 - side, eu, v2, source_side=side)
            elif k == "lb_heal_leader":
                p.heal_hp(max(1, v1))
                if pa.get("cleanse"):
                    for au in p.board_units():
                        au.cleanse()
            elif k == "lb_mana":
                p.temp_mana += max(1, v1)
            elif k == "lb_copy_enemy":
                if opp.hand:
                    p.add_to_hand(self.rng.choice(opp.hand).card_id, cost_mod=-1)
                if v1 > 0:
                    p.heal_hp(v1)
            elif k == "lb_dmg_leader":
                self._damage_commander(1 - side, max(1, v1), source_side=side)

    def _trigger_ascend(self, side: int, u: MonsterUnit, pa: Dict[str, Any]) -> None:
        u.ascended = True
        u.apply_perm_buff(int(pa.get("v1", 7)), int(pa.get("v2", 9)))
        u.life_leech_pct += int(pa.get("pct", 35))
        self._add_highlight(side, "Ascend_Evolution")

    def _exec_warrior_call(self, side: int, op: Dict[str, Any]) -> None:
        p = self.players[side]
        cap = int(op.get("cap", 7))
        best_idx = -1
        best_mana = -1
        for i, cid in enumerate(p.deck):
            c = self.db.get(cid)
            if c.type == "monster" and c.tribe == 4 and not c.is_barrier and c.mana <= cap:
                if c.mana > best_mana:
                    best_mana = c.mana
                    best_idx = i
        if best_idx < 0:
            return
        cid = p.deck.pop(best_idx)
        c = self.db.get(cid)
        if op.get("to_board"):
            empties = p.empty_slots()
            if empties and p.board_count() < p.board_limit():
                nu = self._summon_monster(side, c, empties[0])
                b = int(op.get("buff", 0))
                if nu and b > 0:
                    nu.apply_perm_buff(b, b)
            else:
                p.add_to_hand(cid, cost_mod=-int(op.get("disc", 1)))
        else:
            p.add_to_hand(cid, cost_mod=-int(op.get("disc", 0)))

    def _exec_pull_hook(self, side: int, target_slot: int, dmg: int = 0, burn: int = 0, root: bool = False) -> None:
        opp = self.players[1 - side]
        flanks = [opp.board[s] for s in FLANK_SLOTS if opp.board[s] is not None and not opp.board[s].spell_immune]
        if not flanks:
            return
        victim = max(flanks, key=lambda u: u.atk + u.deff)
        old_s = victim.slot
        dest = target_slot if (0 <= target_slot < BOARD_SLOTS) else 2
        if old_s != dest:
            opp.board[old_s], opp.board[dest] = opp.board[dest], opp.board[old_s]
            if opp.board[old_s] is not None:
                opp.board[old_s].slot = old_s
            victim.slot = dest
        if dmg > 0:
            self._damage_unit(1 - side, victim, dmg, source_side=side)
        if burn > 0 and victim.deff > 0:
            self._apply_status(1 - side, victim, "burn", burn, source_side=side)
        if root and victim.deff > 0:
            self._apply_status(1 - side, victim, "root", 1, source_side=side)
        self.players[side].telemetry.disruptions_applied += 1

    def _plant_spy(self, side: int, deff: int, dmg: int, poison: bool = False, preferred_slot: int = -1) -> bool:
        opp = self.players[1 - side]
        empties = opp.empty_slots()
        if not empties:
            return False
        s = preferred_slot if preferred_slot in empties else empties[0]
        spy = MonsterUnit(
            card_id=0, name="Spy Parasite", owner=1 - side, slot=s,
            element=-1, tribe=-1, playstyle=-1, role=5, tier=1, mana=1,
            atk=0, deff=max(5, int(deff)), is_spy=True, is_token=True, summoned_turn=self.turn,
        )
        spy.spy_dmg = max(2, int(dmg))
        spy.spy_poison = poison
        opp.board[s] = spy
        self.players[side].telemetry.disruptions_applied += 2
        return True

    def _spawn_token(
        self, side: int, atk: int, deff: int, name: str = "Token", haste: bool = False, slot: int = -1
    ) -> Optional[MonsterUnit]:
        p = self.players[side]
        empties = p.empty_slots()
        if not empties or p.board_count() >= p.board_limit():
            return None
        s = slot if slot in empties else empties[0]
        u = MonsterUnit(
            card_id=0, name=name, owner=side, slot=s,
            element=-1, tribe=9, playstyle=6, role=0, tier=1, mana=1,
            atk=max(0, int(atk)), deff=max(1, int(deff)), is_token=True, summoned_turn=self.turn,
        )
        if haste or (self.env_id == 8 and self.env_level >= 2):
            u.haste = True
            u.can_attack = u.can_ever_attack()
        p.board[s] = u
        p.telemetry.tokens_spawned += 1
        return u

    def _apply_status(self, owner_side: int, u: MonsterUnit, fx: str, val: int, source_side: int) -> None:
        if u.deff <= 0 or (u.status_immune and not u.is_silenced()):
            return
        v = max(1, int(val))
        if fx == "burn":
            u.burn = min(30, u.burn + v)
        elif fx == "poison":
            u.poison = min(30, u.poison + v)
        elif fx == "bleed":
            u.bleed = min(30, u.bleed + v)
        elif fx == "freeze":
            u.frozen = max(u.frozen, v)
            u.can_attack = False
            if source_side in (0, 1):
                self.players[source_side].telemetry.disruptions_applied += 1
        elif fx == "silence":
            u.silenced = max(u.silenced, v)
            if source_side in (0, 1):
                self.players[source_side].telemetry.disruptions_applied += 1
        elif fx == "root":
            u.rooted = max(u.rooted, v)
            if source_side in (0, 1):
                self.players[source_side].telemetry.disruptions_applied += 1
        elif fx == "weaken":
            u.weakened = min(30, u.weakened + v)
            if source_side in (0, 1):
                self.players[source_side].telemetry.disruptions_applied += 1

    def _recall_unit(self, owner_side: int, u: MonsterUnit, source_side: int) -> bool:
        if u.rooted > 0 or u.is_spy:
            return False
        p = self.players[owner_side]
        p.board[u.slot] = None
        if not u.is_token and u.card_id > 0:
            p.add_to_hand(u.card_id, cost_mod=0)
        if source_side != owner_side and source_side in (0, 1):
            self.players[source_side].telemetry.disruptions_applied += 1
        return True

    def _revive_from_grave(
        self, side: int, cap: int = 99, count: int = 1, haste: bool = False,
        bonus_atk: int = 0, from_enemy: bool = False
    ) -> int:
        p = self.players[side]
        src_p = self.players[1 - side] if from_enemy else p
        revived = 0
        for _ in range(count):
            empties = p.empty_slots()
            if not empties or p.board_count() >= p.board_limit():
                break
            best_i = -1
            best_m = -1
            for i, cid in enumerate(src_p.graveyard):
                c = self.db.get(cid)
                if c.type == "monster" and c.mana <= cap and c.mana > best_m:
                    best_m = c.mana
                    best_i = i
            if best_i < 0:
                break
            cid = src_p.graveyard.pop(best_i)
            nu = self._summon_monster(side, self.db.get(cid), empties[0], is_resurrected=True)
            if nu:
                if haste and nu.can_ever_attack():
                    nu.haste = True
                    nu.can_attack = True
                if bonus_atk > 0:
                    nu.apply_perm_buff(bonus_atk, 0)
                p.telemetry.revives_done += 1
                revived += 1
        return revived

    def _grave_to_hand(self, side: int, count: int = 1, disc: int = 1, cap: int = 99) -> int:
        p = self.players[side]
        got = 0
        cov_disc = int(p.covenant_ops.get("rew_revive_disc", 0)) if (p.covenant_active and p.covenant_ops) else 0
        for _ in range(count):
            best_i = -1
            best_m = -1
            for i, cid in enumerate(p.graveyard):
                c = self.db.get(cid)
                if c.type == "monster" and c.mana <= cap and c.mana > best_m:
                    best_m = c.mana
                    best_i = i
            if best_i < 0:
                break
            cid = p.graveyard.pop(best_i)
            p.add_to_hand(cid, cost_mod=-(disc + cov_disc))
            p.telemetry.grave_returns += 1
            got += 1
        return got

    def _detonate_dots(self, side: int, mult: int = 1, extra_dmg: int = 0, leech: bool = False) -> None:
        p = self.players[side]
        opp = self.players[1 - side]
        total = 0
        for eu in list(opp.board_units()):
            dot = (eu.burn + eu.poison + eu.bleed) * max(1, mult) + extra_dmg
            if dot > 0:
                total += self._damage_unit(1 - side, eu, dot, source_side=side, is_dot=True)
        if leech and total > 0:
            p.heal_hp(pct_of(total, 50))

    def _shuffle_cycle_deck(
        self,
        deck_ids: List[int],
        skip_leader: bool = False,
        skip_covenant: bool = False,
    ) -> List[int]:
        """
        Infinite Shuffle-Bag with Anti-Clump Golden-Ratio spacing:
        - Every card in the 50-card deck appears once per cycle before reshuffling (zero skipped monsters!)
        - Duplicate copies (x2 / x3) naturally appear 2x / 3x more often, spaced cleanly so copies don't clump
        - On Cycle 2+, already-active/destroyed 1-time Leaders & Covenants are omitted so they never clog hand
        """
        bag: List[int] = []
        for cid in deck_ids:
            c = self.db.get(cid)
            if skip_leader and c.type == "leader":
                continue
            if skip_covenant and c.type == "covenant":
                continue
            bag.append(cid)
        if not bag:
            bag = list(deck_ids)
        self.rng.shuffle(bag)
        # Anti-clump spacing pass: avoid adjacent identical copies inside a cycle
        n = len(bag)
        for i in range(1, n):
            if bag[i] == bag[i - 1]:
                # Pick a clean swap target that doesn't create a new adjacent pair
                for _ in range(6):
                    j = self.rng.randrange(n)
                    if bag[j] != bag[i] and (j == 0 or bag[j - 1] != bag[i]) and (j == n - 1 or bag[j + 1] != bag[i]):
                        bag[i], bag[j] = bag[j], bag[i]
                        break
        return bag

    def _draw_card(self, side: int, check_deckout: bool = True) -> None:
        p = self.players[side]
        if not p.deck:
            # Infinite Shuffle-Bag: NO Deck-Out loss! Refill a fresh shuffled cycle while keeping Graveyard intact!
            p.deck = self._shuffle_cycle_deck(
                p.base_deck,
                skip_leader=(p.leader_active or p.leader_exorcised),
                skip_covenant=(p.covenant_active or p.covenant_destroyed),
            )
            p.deck_cycles += 1
        if not p.deck:
            return
        cid = p.deck.pop()
        p.add_to_hand(cid)
        p.telemetry.cards_drawn += 1

    # ------------------------------------------------------------------
    # Smart Targeting Helpers
    # ------------------------------------------------------------------
    def _best_enemy_unit(self, side: int, max_mana: int = 99) -> Optional[MonsterUnit]:
        cands = [
            u for u in self.players[1 - side].board_units()
            if not u.stealth and not u.is_spy and u.mana <= max_mana
        ]
        if not cands:
            return None
        return max(cands, key=lambda u: u.atk * 2 + u.deff + (10 if u.taunt else 0))

    def _pick_enemy_target(self, side: int, slot: int, max_mana: int = 99) -> Optional[MonsterUnit]:
        opp = self.players[1 - side]
        if 0 <= slot < BOARD_SLOTS:
            u = opp.board[slot]
            if u is not None and not u.stealth and not u.is_spy and u.mana <= max_mana:
                return u
        return self._best_enemy_unit(side, max_mana=max_mana)

    def _best_ally_unit(
        self, side: int, exclude: Optional[MonsterUnit] = None, non_barrier: bool = False
    ) -> Optional[MonsterUnit]:
        cands = [
            u for u in self.players[side].board_units()
            if u is not exclude and not u.is_spy and (not non_barrier or not u.is_barrier)
        ]
        if not cands:
            return None
        return max(cands, key=lambda u: u.atk * 2 + u.deff)

    def _find_devour_target(self, side: int, caster: MonsterUnit) -> Optional[Tuple[int, MonsterUnit]]:
        # Prefer consuming strongest enemy monster with mana <= 3; else weakest ally <= 3 mana
        opp_cands = [
            u for u in self.players[1 - side].board_units()
            if u.mana <= 3 and not u.stealth and not u.spell_immune and not u.is_spy
        ]
        if opp_cands:
            best_e = max(opp_cands, key=lambda u: u.atk + u.deff)
            return (1 - side, best_e)
        ally_cands = [
            u for u in self.players[side].board_units()
            if u is not caster and u.mana <= 3 and not u.is_spy
        ]
        if ally_cands:
            weak_a = min(ally_cands, key=lambda u: u.atk + u.deff)
            return (side, weak_a)
        return None

    def _record_turn_excitement(self, side: int) -> None:
        p = self.players[side]
        t = p.telemetry
        if self._turn_dmg > t.max_turn_dmg:
            t.max_turn_dmg = self._turn_dmg
            t.peak_turn_num = self.turn
        if self._turn_dmg >= 120:
            self._add_highlight(side, "Mega_Burst_Turn")
        if self._turn_kills >= 3:
            t.multi_kill_turns += 1
            self._add_highlight(side, "Multi_Kill_Sweep")

        for u in p.board_units():
            ea = self._effective_atk(side, u)
            if ea > t.max_unit_atk:
                t.max_unit_atk = ea
            if u.deff > t.max_unit_def:
                t.max_unit_def = u.deff

        # Check HP lead swing (when lead of >= 25 HP flips from one player to the other)
        hp_diff = self.players[0].hp - self.players[1].hp
        new_leader = 0 if hp_diff >= 25 else (1 if hp_diff <= -25 else self._lead_side)
        lead_swing = 0
        if new_leader in (0, 1) and self._lead_side in (0, 1) and new_leader != self._lead_side:
            lead_swing = 1
            self.players[0].telemetry.lead_swings += 1
            self.players[1].telemetry.lead_swings += 1
        if new_leader in (0, 1):
            self._lead_side = new_leader

        end_hand = len(p.hand)
        board_cnt = p.board_count()
        t.hand_sum_end += end_hand
        t.board_units_turn_sum += board_cnt
        if end_hand == 0:
            t.empty_hand_turns += 1
        if self.turn <= 30:
            start_h = getattr(self, "_turn_start_hand", end_hand)
            # Per-turn Excitement Index (0..100) for real Turn 1 -> Endgame excitement curve!
            min_hp = min(self.players[0].hp, self.players[1].hp)
            hp_gap = abs(self.players[0].hp - self.players[1].hp)
            dmg_pts = min(38, int(round(self._turn_dmg * 0.45)))
            kill_pts = min(24, self._turn_kills * 9)
            swing_pts = 22 if lead_swing else 0
            hi_pts = min(16, self._turn_highlights * 7)
            if hp_gap <= 110:
                tension_pts = min(20, max(0, int((LEADER_HP - min_hp) // 24)))
            else:
                tension_pts = min(10, max(0, int((LEADER_HP - min_hp) // 55)))
            board_pts = min(8, (self.players[0].board_count() + self.players[1].board_count()))
            turn_exc = max(6, min(100, 6 + dmg_pts + kill_pts + swing_pts + hi_pts + tension_pts + board_pts))
            self._turn_events.append((self.turn, self._turn_dmg, self._turn_kills, lead_swing, self._turn_highlights, start_h, end_hand, turn_exc, board_cnt))

    def _finish(self, winner: int, reason: str) -> None:
        if not self.done:
            self._record_turn_excitement(self.active)
        self.done = True
        self.winner = int(winner)
        self.win_reason = reason
        for s in (0, 1):
            p = self.players[s]
            t = p.telemetry
            t.final_hp = p.hp
            t.max_hp_end = p.max_hp
            for su in p.board_units():
                if not su.is_token and not su.is_spy:
                    t.units_survived_end += 1
                    t.units_survived_rounds_sum += su.own_turns_survived
            if winner == s:
                t.comeback_deficit = self._max_deficit[s]
                t.clutch_win = (0 < p.hp <= 75)
                if t.comeback_deficit >= 120:
                    t.highlight_tags["Comeback_120HP"] = t.highlight_tags.get("Comeback_120HP", 0) + 1
                if t.clutch_win:
                    t.highlight_tags["Clutch_Finish"] = t.highlight_tags.get("Clutch_Finish", 0) + 1
            # Compute Match Excitement Score (0..100 integer!)
            opp_t = self.players[1 - s].telemetry
            swings = t.lead_swings
            cb = max(self._max_deficit[0] if winner == 0 else 0, self._max_deficit[1] if winner == 1 else 0)
            clutch = 1 if (winner in (0, 1) and 0 < self.players[winner].hp <= 75) else 0
            peak_b = max(t.max_turn_dmg, opp_t.max_turn_dmg)
            hi_tot = t.highlight_triggers + opp_t.highlight_triggers
            mk_tot = t.multi_kill_turns + opp_t.multi_kill_turns
            pace_b = 12 if (8 <= self.turn <= 22) else 4
            score = (
                pace_b
                + min(28, swings * 14)
                + min(22, int(cb // 8))
                + (16 if clutch else 0)
                + min(14, int(max(0, peak_b - 60) // 8))
                + min(12, mk_tot * 6)
                + min(16, hi_tot * 3)
            )
            t.excitement_score = min(100, max(5, int(score)))

    def get_result(self) -> MatchResult:
        return MatchResult(
            winner=self.winner,
            turns=self.turn,
            win_reason=self.win_reason or "Incomplete",
            telemetry=(self.players[0].telemetry, self.players[1].telemetry),
            final_hp=(self.players[0].hp, self.players[1].hp),
            decks=self._initial_decks,
            turn_events=self._turn_events,
        )

    # ------------------------------------------------------------------
    # Compact Normalized Observation Vector (OBS_DIM = 220)
    #   0..159  : Legacy core state (100% preserved for zero-forgetting upgrade)
    #   160..177: Ally board passive/trait lights (6 slots x 3 raw 0/1 flags)
    #   178..219: Enemy board passive/active/status lights (6 slots x 7 raw flags)
    # ------------------------------------------------------------------
    def get_obs(self, side: Optional[int] = None) -> np.ndarray:
        s = self.active if side is None else side
        p = self.players[s]
        opp = self.players[1 - s]
        obs = np.zeros(OBS_DIM, dtype=np.float32)

        # Global scalars (0..19)
        obs[0] = p.hp / 500.0
        obs[1] = opp.hp / 500.0
        obs[2] = (p.mana + p.temp_mana) / 10.0
        obs[3] = p.max_mana / 10.0
        obs[4] = opp.max_mana / 10.0
        obs[5] = len(p.hand) / 7.0
        obs[6] = len(opp.hand) / 7.0
        obs[7] = len(p.deck) / 50.0
        obs[8] = len(opp.deck) / 50.0
        obs[9] = len(p.graveyard) / 30.0
        obs[10] = len(opp.graveyard) / 30.0
        obs[11] = min(1.0, self.turn / 40.0)
        obs[12] = 1.0 if p.sacrificed_this_turn else 0.0
        obs[13] = 1.0 if self._leader_aura_on(s) else 0.0
        obs[14] = 1.0 if p.covenant_active else 0.0
        obs[15] = 1.0 if self._leader_aura_on(1 - s) else 0.0
        obs[16] = 1.0 if opp.covenant_active else 0.0
        obs[17] = (self.env_id + 1) / 10.0 if self.env_id >= 0 else 0.0
        obs[18] = self.env_level / 3.0
        obs[19] = min(1.0, self.env_turns / 10.0)

        # Ally board: 6 slots x 9 features (20..73)
        idx = 20
        for sl in range(BOARD_SLOTS):
            u = p.board[sl]
            if u is not None:
                obs[idx] = 1.0
                obs[idx + 1] = self._effective_atk(s, u) / 35.0
                obs[idx + 2] = u.deff / 60.0
                obs[idx + 3] = 1.0 if u.can_attack and not u.attacked_this_turn else 0.0
                obs[idx + 4] = 1.0 if u.taunt else 0.0
                obs[idx + 5] = 1.0 if u.is_barrier else 0.0
                obs[idx + 6] = 1.0 if (u.frozen > 0 or u.silenced > 0) else 0.0
                obs[idx + 7] = (u.burn + u.poison + u.bleed) / 15.0
                obs[idx + 8] = len(u.actives) / 2.0
            idx += 9

        # Enemy board: 6 slots x 9 features (74..127)
        for sl in range(BOARD_SLOTS):
            u = opp.board[sl]
            if u is not None:
                obs[idx] = 1.0
                obs[idx + 1] = self._effective_atk(1 - s, u) / 35.0
                obs[idx + 2] = u.deff / 60.0
                obs[idx + 3] = 1.0 if u.taunt else 0.0
                obs[idx + 4] = 1.0 if u.stealth else 0.0
                obs[idx + 5] = 1.0 if u.is_barrier else 0.0
                obs[idx + 6] = 1.0 if u.is_spy else 0.0
                obs[idx + 7] = (u.burn + u.poison + u.bleed) / 15.0
                obs[idx + 8] = u.mana / 7.0
            idx += 9

        # Hand cards: up to 7 cards x 4 features (128..155) + 4 summary features (156..159)
        type_code = {"monster": 0.2, "spell": 0.4, "environment": 0.6, "leader": 0.8, "covenant": 1.0}
        avail = p.mana + p.temp_mana
        for h in range(MAX_HAND):
            if h < len(p.hand):
                hc = p.hand[h]
                c = self.db.get(hc.card_id)
                cost = self._effective_card_cost(s, hc)
                obs[idx] = type_code.get(c.type, 0.0)
                obs[idx + 1] = cost / 7.0
                obs[idx + 2] = 1.0 if cost <= avail else 0.0
                obs[idx + 3] = (c.atk + c.deff) / 70.0 if c.type == "monster" else (c.tier / 7.0)
            idx += 4

        obs[156] = p.board_count() / 6.0
        obs[157] = opp.board_count() / 6.0
        obs[158] = p.spell_power_temp / 10.0
        obs[159] = p.skill_bonus_temp / 10.0

        # Extended Pure-RL Passive/Active Indicator Lights (160..219, ZERO hardcoded biases)
        # Ally board passive lights: 6 slots x 3 features (160..177)
        idx = 160
        for sl in range(BOARD_SLOTS):
            u = p.board[sl]
            if u is not None:
                obs[idx] = 1.0 if (
                    u.double_strike or u.double_strike_temp or u.is_all_attack
                    or u.cleave_pct > 0 or u.bypass_pct > 0
                ) else 0.0
                obs[idx + 1] = 1.0 if (
                    u.turn_heal_def > 0 or u.turn_heal_hp > 0 or u.cleanse_aura_def > 0
                    or u.spell_power > 0 or u.spell_ping_dmg > 0 or u.skill_conduit > 0
                    or u.cost_discount_mode > 0 or u.on_spell_atk > 0 or u.on_spell_def > 0
                    or u.on_skill_atk > 0 or u.on_skill_def > 0 or u.on_death_atk > 0
                    or u.on_death_def > 0 or u.on_death_dmg > 0 or u.aura_weaken_opp > 0
                    or u.hydra_split_turn > 0 or u.ascend_turns > 0 or u.ascend_kills > 0
                    or u.grave_scale_atk > 0 or u.pack_atk > 0 or u.pack_def > 0
                ) else 0.0
                obs[idx + 2] = 1.0 if (
                    u.reflect_flat > 0 or u.reflect_pct > 0 or u.evasion_pct > 0
                    or u.spell_immune or u.status_immune or u.undying_ready
                    or u.life_leech_pct > 0 or u.dmg_reduce_flat > 0
                ) else 0.0
            idx += 3

        # Enemy board passive/active/status lights: 6 slots x 7 features (178..219)
        for sl in range(BOARD_SLOTS):
            u = opp.board[sl]
            if u is not None:
                obs[idx] = 1.0 if (u.double_strike or u.double_strike_temp or u.is_all_attack) else 0.0
                obs[idx + 1] = 1.0 if (
                    u.reflect_flat > 0 or u.reflect_pct > 0 or u.evasion_pct > 0
                    or u.spell_immune or u.status_immune or u.undying_ready or u.dmg_reduce_flat > 0
                ) else 0.0
                obs[idx + 2] = 1.0 if (
                    u.life_leech_pct > 0 or u.on_hit_burn > 0 or u.on_hit_poison > 0
                    or u.on_hit_bleed > 0 or u.enrage_atk > 0 or u.strip_buffs_bonus > 0
                ) else 0.0
                obs[idx + 3] = 1.0 if (
                    u.turn_heal_def > 0 or u.turn_heal_hp > 0 or u.cleanse_aura_def > 0
                    or u.spell_power > 0 or u.spell_ping_dmg > 0 or u.skill_conduit > 0
                    or u.cost_discount_mode > 0 or u.on_spell_atk > 0 or u.on_spell_def > 0
                    or u.on_skill_atk > 0 or u.on_skill_def > 0 or u.on_death_atk > 0
                    or u.on_death_def > 0 or u.on_death_dmg > 0 or u.aura_weaken_opp > 0
                    or u.hydra_split_turn > 0 or u.ascend_turns > 0 or u.ascend_kills > 0
                    or u.grave_scale_atk > 0 or u.pack_atk > 0 or u.pack_def > 0
                ) else 0.0
                obs[idx + 4] = 1.0 if (u.cleave_pct > 0 or u.bypass_pct > 0) else 0.0
                obs[idx + 5] = len(u.actives) / 2.0
                obs[idx + 6] = 1.0 if (u.frozen > 0 or u.silenced > 0 or u.rooted > 0) else 0.0
            idx += 7

        return obs

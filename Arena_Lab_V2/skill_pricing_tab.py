# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Tab #17: Comprehensive 1,470-Card Surgical Pricing Advisor (`skill_pricing_tab.py`).

Evaluates EVERY single card across all 1,470 game cards (Monsters, Spells, Covenants, Leaders, Barriers)
AND every Active Skill tier using live telemetry from `BattleEngine`:
  - Surgical Diagnostic Differentiation:
      * Monsters: Diagnoses whether to adjust [Card Mana Cost] (body stats/passives over/under-performing)
        vs [Active Skill Mana Cost] (combat stats balanced/weak, but active skill over-delivering/cheap)
        vs [Passive Magnitude / Base Stats].
      * Spells: Diagnoses whether to adjust [Spell Mana Cost] vs [Spell Effect Magnitude] (e.g. keeping 2 Mana
        for curve consistency while reducing damage from 18 to 13).
      * Covenants & Leaders: Diagnoses [Activation Mana (1..3 / 1..2)] vs [Continuous Aura / Sacrifice Condition].
  - Live Drafter Duplicate vs Singleton Ratio Metric:
      * Measures across all 40 neural brains: % 1-of (Singles / Tech) vs % 2-of (Pairs / Staples).
      * Highlights top cards favored as 1-of singles vs top cards favored as 2-of pairs.
  - Interactive Filter & Mode Toolbar:
      * 🌐 كل البطاقات (1,470)
      * ⚔️ الوحوش والمصدات (1,000)
      * 📜 بطاقات السحر (300)
      * 📜 العهود والقادة (160)
      * ⚡ مهارات الوحوش × التكلفة
"""

from __future__ import annotations

import glob
import json
import math
import os
import statistics
import tkinter as tk
from collections import Counter, defaultdict
from tkinter import ttk
from typing import Any, Callable, Dict, List, Optional, Tuple

from engine.card_model import CardDB, CardDef
from trainer.stats_store import StatsStore
from pace_tab import rtl
from arabic_glossary import (
    ACTIVE_AR,
    ELEM_AR,
    ROLE_AR,
    SPELL_AR,
    TRIBE_AR,
    enable_treeview_sorting,
    explain_card_arabic,
)

BG_DARK = "#0F131A"
BG_PANEL = "#171C26"
BG_CARD = "#1D2432"
BG_BORDER = "#2A3346"
BG_INPUT = "#121721"
FG_TEXT = "#EEF2F9"
FG_MUTED = "#94A6C0"

COL_BLUE = "#3B82F6"
COL_CYAN = "#06B6D4"
COL_GREEN = "#10B981"
COL_GOLD = "#F59E0B"
COL_RED = "#EF4444"
COL_ORANGE = "#F97316"
COL_PURPLE = "#A855F7"
COL_PINK = "#EC4899"

FONT_UI = "Segoe UI"


def compute_skill_impact_value(
    dmg_mon: float,
    dmg_face: float,
    heal_buff: float,
    kills: float,
    disrupt: float,
    util: float,
) -> float:
    """Weighted composite gameplay value produced per single activation of an Active Skill."""
    return (
        1.00 * dmg_mon
        + 1.35 * dmg_face
        + 0.75 * heal_buff
        + 18.0 * kills
        + 10.0 * disrupt
        + 14.0 * util
    )


def recommend_mana_pricing(
    *,
    is_spell: bool,
    cur_cost: int,
    wr: Optional[float],
    uses: int,
    tot_matches: int,
    uses_per_match: float,
    uses_per_card_pm: float,
    median_upcm: float,
    vpm: Optional[float],
    median_vpm: Optional[float],
    kills_pu: float,
    dmg_face_pu: float,
    heal_buff_pu: float,
    card_count: int,
    ar_name: str,
) -> Tuple[str, str, int, str]:
    """Active-skill aggregated pricing recommendation."""
    max_cap = 7 if is_spell else 6
    min_uses = 30 if not is_spell else 25

    if tot_matches < 50:
        return (
            "⚪ بانتظار بيانات",
            "nodata",
            cur_cost,
            f"عدد المباريات المسجلة حالياً ({tot_matches}) أقل من 50 — ابدأ التدريب لتظهر توصيات التسعير الدقيقة.",
        )

    if uses < min_uses or wr is None:
        if uses == 0 and card_count >= 3 and tot_matches >= 200:
            sug = max(1, cur_cost - 1) if cur_cost >= 2 else cur_cost
            if cur_cost >= 2:
                return (
                    "🔵 خفّض -1 مانا",
                    "drop",
                    sug,
                    f"مهارة مهجورة تماماً (0 تفعيل رغم وجودها على {card_count} بطاقات) — خفّض تكلفتها من {cur_cost} إلى {sug} مانا لتنشيطها.",
                )
            return (
                "🟣 قوّي الأثر",
                "buff",
                cur_cost,
                f"مهارة مهجورة (0 تفعيل) وسعرها بالفعل {cur_cost} مانا — قوِّ تأثيرها بـ +25% لجذب اللاعبين.",
            )
        return (
            "⚪ عيّنة صغيرة",
            "nodata",
            cur_cost,
            f"عدد التفعيلات قليل ({uses}) — بانتظار مزيد من المباريات لإصدار توصية تسعير دقيقة.",
        )

    score = 0
    reasons: List[str] = []

    if wr >= 57.0:
        score += 2
        reasons.append(f"فوز مرتفع جداً ({wr:.1f}%)")
    elif wr >= 53.5:
        score += 1
        reasons.append(f"فوز فوق المتوسط ({wr:.1f}%)")
    elif wr <= 43.5:
        score -= 2
        reasons.append(f"فوز منخفض جداً ({wr:.1f}%)")
    elif wr <= 46.5:
        score -= 1
        reasons.append(f"فوز أضعف من المتوسط ({wr:.1f}%)")

    if vpm is not None and median_vpm is not None and median_vpm > 0.1:
        vpm_ratio = vpm / median_vpm
        if vpm_ratio >= 1.90:
            score += 2
            reasons.append(f"عائد هائل لكل مانا ({vpm:.1f} = {vpm_ratio:.1f}× المتوسط)")
        elif vpm_ratio >= 1.35:
            score += 1
            reasons.append(f"عائد مرتفع لكل مانا ({vpm:.1f} = {vpm_ratio:.1f}× المتوسط)")
        elif vpm_ratio <= 0.50:
            score -= 2
            reasons.append(f"عائد ضعيف جداً لكل مانا ({vpm:.1f} = {vpm_ratio:.1f}× المتوسط)")
        elif vpm_ratio <= 0.72:
            score -= 1
            reasons.append(f"عائد أقل من المتوسط ({vpm:.1f} = {vpm_ratio:.1f}× المتوسط)")

    if median_upcm > 1e-6:
        spam_ratio = uses_per_card_pm / median_upcm
        if spam_ratio >= 2.2 and uses_per_match >= (1.2 if not is_spell else 0.12):
            score += 1
            reasons.append(f"تكرار مفرط ({uses_per_match:.2f}/مباراة = {spam_ratio:.1f}× المتوسط)")
        elif spam_ratio <= 0.25 and card_count >= 4 and uses_per_match < 0.25:
            score -= 1
            reasons.append(f"استخدام نادر ({uses_per_match:.2f}/مباراة)")

    if not is_spell:
        if kills_pu >= 0.40 and cur_cost <= 3:
            score += 1
            reasons.append(f"معدل قتل مباشر مرتفع ({kills_pu:.2f} قتلة/تفعيل)")
        if dmg_face_pu >= 18.0 and cur_cost <= 2:
            score += 1
            reasons.append(f"حرق مباشر مرتفع للقائد ({dmg_face_pu:.1f} ضرر/تفعيل)")
        if heal_buff_pu >= 36.0 and cur_cost <= 2:
            score += 1
            reasons.append(f"شفاء/تعزيز ضخم مقابل {cur_cost} مانا ({heal_buff_pu:.1f}/تفعيل)")

    if score >= 3 and cur_cost < max_cap:
        delta = min(2, max_cap - cur_cost)
        sug = cur_cost + delta
        badge = f"🔴 ارفع +{delta} مانا"
        tag = "raise2"
        why = " + ".join(reasons) if reasons else "أداء وتكرار مفرطان"
        return (badge, tag, sug, f"ارفع السعر من {cur_cost} ← {sug} مانا: {why}.")

    if (score >= 1 or (wr >= 55.0 and score >= 0)) and cur_cost < max_cap:
        sug = cur_cost + 1
        badge = "🟠 ارفع +1 مانا"
        tag = "raise1"
        why = " + ".join(reasons) if reasons else f"أداء فوق المتوسط ({wr:.1f}%)"
        return (badge, tag, sug, f"ارفع السعر من {cur_cost} ← {sug} مانا: {why}.")

    if score >= 1 and cur_cost >= max_cap:
        why = " + ".join(reasons) if reasons else f"فوز مرتفع ({wr:.1f}%)"
        return (
            "🔴 خفّض الضرر (سقف مانا)",
            "raise2",
            cur_cost,
            f"وصلت لسقف المانا ({cur_cost}) — اخفض قيمة التأثير بـ 15–20% بدل رفع المانا: {why}.",
        )

    if score <= -2 or (wr <= 44.5 and score <= -1):
        why = " + ".join(reasons) if reasons else f"أداء ضعيف ({wr:.1f}%)"
        if cur_cost >= 2:
            sug = cur_cost - 1
            return ("🔵 خفّض -1 مانا", "drop", sug, f"خفّض السعر من {cur_cost} ← {sug} مانا: {why}.")
        return (
            "🟣 قوّي الأثر (+20%)",
            "buff",
            cur_cost,
            f"سعرها بالفعل {cur_cost} مانا (الحد الأدنى) — قوِّ تأثير [{ar_name}] بـ +20–25%: {why}.",
        )

    why = " + ".join(reasons) if reasons else f"توازن صحي ({wr:.1f}%)"
    return ("🟢 تكلفة مثالية", "ideal", cur_cost, f"تسعير عادل عند {cur_cost} مانا — أبقِها كما هي ({why}).")


def recommend_card_surgical_pricing(
    card: CardDef,
    st: Dict[str, Any],
    tot_matches: int,
    sp_map: Dict[str, List[int]],
) -> Tuple[str, str, str, int, str]:
    """
    Surgical Diagnostic Evaluator for ANY card across all 1,470 cards.
    Returns: (verdict_badge, tag, target_element, suggested_cost, surgical_reason)
      - target_element: 'مانا المهارة' | 'مانا الوحش' | 'أثر السحر' | 'مانا السحر' | 'هالة العهد' | 'مانا العهد' | 'أثر الباسف' | 'متوازن'
    """
    cur_cost = int(card.mana)
    pl = int(st.get("played", 0))
    pw = int(st.get("played_wins", 0))
    wr = (100.0 * pw / pl) if pl >= 10 else None

    if tot_matches < 40:
        return (
            "⚪ بانتظار بيانات",
            "nodata",
            "قيد الملاحظة",
            cur_cost,
            f"عدد المباريات المسجلة ({tot_matches}) أقل من 40 — ابدأ جولة تدريب لإصدار التشخيص الجراحي.",
        )

    if pl < 12 or wr is None:
        if pl == 0 and tot_matches >= 150:
            if cur_cost >= 2:
                return (
                    "🔵 خفّض -1 مانا",
                    "drop",
                    "مانا الاستدعاء",
                    cur_cost - 1,
                    f"بطاقة مهجورة تماماً (0 لعب رغم وجود {tot_matches} مباراة) — خفّض تكلفتها من {cur_cost} ← {cur_cost - 1} مانا لتشجيع تجربتها.",
                )
            return (
                "🟣 قوّي الأثر",
                "buff",
                "قوة الأثر",
                cur_cost,
                f"بطاقة مهجورة وسعرها بالفعل {cur_cost} مانا — قوِّ هجومها أو تأثيرها بـ +25% لجذب الذكاء الاصطناعي لاختيارها.",
            )
        return (
            "⚪ عيّنة صغيرة",
            "nodata",
            "قيد الملاحظة",
            cur_cost,
            f"لُعبت {pl} مرات فقط — بانتظار مزيد من المباريات لإصدار نصيحة جراحية مؤكدة.",
        )

    # 1. MONSTERS & BARRIERS
    if card.type == "monster":
        body_stat = card.atk + card.deff
        expected_stat = card.mana * 7 + 8
        has_active = bool(card.actives)
        ar_act_name = ""
        act_cost = 1
        if has_active:
            act = card.actives[0]
            act_kind = str(act.get("kind", ""))
            act_cost = int(act.get("cost", 1))
            ar_act_name = ACTIVE_AR.get(act_kind, (act_kind, ""))[0]

        # Case A: Overperforming Monster (WR >= 54.0%)
        if wr >= 54.0:
            # Did active skill cause the dominance while body is modest?
            if has_active and card.atk <= (card.mana * 4 + 2) and act_cost <= 2:
                sug = act_cost + 1
                return (
                    f"🟠 ارفع مانا المهارة ({act_cost}←{sug})",
                    "raise1",
                    "مانا المهارة",
                    sug,
                    f"جسد الوحش متوازن ({card.atk} هـ / {card.deff} د مقابل {cur_cost} مانا)، لكن مهارته [{ar_act_name}] رخيصة جداً ({act_cost} مانا) وتسبب تفوقه (فوز {wr:.1f}%) — ارفع تكلفة مانا المهارة لـ {sug} مانا بدل رفع مانا الوحش.",
                )
            # Did oversized base stats cause the dominance?
            elif body_stat >= (expected_stat + 5) or card.atk >= (card.mana * 5 + 3) or not has_active:
                sug = min(7, cur_cost + 1)
                tag = "raise2" if wr >= 57.5 else "raise1"
                return (
                    f"🔴 ارفع مانا الوحش ({cur_cost}←{sug})",
                    tag,
                    "مانا الوحش",
                    sug,
                    f"قوة الوحش الأساسية ({card.atk} هجوم / {card.deff} دفاع) مرتفعة للغاية مقارنة بتكلفة {cur_cost} مانا (فوز {wr:.1f}%) — ارفع تكلفة استدعائه لـ {sug} مانا لحماية توازن الكيرف.",
                )
            # Passive aura driver
            elif card.passives:
                return (
                    "🟣 قلّص أثر الباسف (-20%)",
                    "raise1",
                    "أثر الباسف",
                    cur_cost,
                    f"هالة الباسف تمنح أفضلية تراكمية مفرطة (فوز {wr:.1f}%) — قلّص قيمة الباسف بـ 20% وأبقِ مانا الاستدعاء عند {cur_cost} مانا.",
                )
            else:
                sug = min(7, cur_cost + 1)
                return (
                    f"🟠 ارفع مانا الوحش ({cur_cost}←{sug})",
                    "raise1",
                    "مانا الوحش",
                    sug,
                    f"وحش متفوق إجمالاً (فوز {wr:.1f}%) — ارفع تكلفة استدعائه لـ {sug} مانا.",
                )

        # Case B: Underperforming Monster (WR <= 46.0%)
        elif wr <= 46.0:
            # Is active skill overpriced and blocking the card?
            if has_active and act_cost >= 2 and act_cost >= card.mana:
                sug = max(1, act_cost - 1)
                return (
                    f"🔵 خفّض مانا المهارة ({act_cost}←{sug})",
                    "drop",
                    "مانا المهارة",
                    sug,
                    f"مهارة الوحش [{ar_act_name}] مكلفة جداً ({act_cost} مانا) وتعيق تفعيله (فوز {wr:.1f}%) — خفّض مانا المهارة لـ {sug} مانا لتنشيط دوره التكتيكي.",
                )
            # Is summon mana too heavy for body stats?
            elif cur_cost >= 2 and body_stat <= (expected_stat - 3):
                sug = cur_cost - 1
                return (
                    f"🔵 خفّض مانا الوحش ({cur_cost}←{sug})",
                    "drop",
                    "مانا الوحش",
                    sug,
                    f"تكلفة استدعاء الوحش ({cur_cost} مانا) ثقيلة مقارنة بإحصائياته ({card.atk} هـ / {card.deff} د) — خفّض مانا الاستدعاء لـ {sug} مانا ليصبح جذاباً.",
                )
            elif cur_cost == 1:
                return (
                    "🟣 قوّ الهجوم (+3) أو الدفاع",
                    "buff",
                    "إحصائيات الوحش",
                    cur_cost,
                    f"الوحش بالفعل عند الحد الأدنى (1 مانا) وفوزه باهت ({wr:.1f}%) — زده +3 هجوم أو +4 دفاع لجعله قادراً على المنافسة.",
                )
            else:
                sug = max(1, cur_cost - 1)
                return (
                    f"🔵 خفّض مانا الوحش ({cur_cost}←{sug})",
                    "drop",
                    "مانا الوحش",
                    sug,
                    f"أداء ضعيف عموماً (فوز {wr:.1f}%) — خفّض تكلفة استدعائه لـ {sug} مانا.",
                )

        # Case C: Balanced
        else:
            return (
                "🟢 تكلفة مثالية",
                "ideal",
                "متوازن",
                cur_cost,
                f"الوحش وإحصائياته ({card.atk} هـ / {card.deff} د) ومهاراته في توازن ممتاز (فوز {wr:.1f}% عبر {pl:,} مباراة) — أبقِه عند {cur_cost} مانا.",
            )

    # 2. SPELL CARDS
    elif card.type == "spell":
        sk = str((card.spell_ops or {}).get("kind", ""))
        ar_k = SPELL_AR.get(sk, (sk, ""))[0]

        if wr >= 54.0:
            if cur_cost >= 4:
                return (
                    "🔴 خفّض قوة الأثر (-15%)",
                    "raise2" if wr >= 58.0 else "raise1",
                    "أثر السحر",
                    cur_cost,
                    f"السحر مرتفع التكلفة بالفعل ({cur_cost} مانا) وفوزه كاسح ({wr:.1f}%) — رفع المانا أكثر قد يعطله تماماً، الأفضل تقليص الضرر/الأثر بـ 15–20% مع إبقاء تكلفته.",
                )
            else:
                sug = cur_cost + 1
                return (
                    f"🟠 ارفع مانا السحر ({cur_cost}←{sug}) أو قلّص الأثر",
                    "raise1",
                    "مانا السحر / الأثر",
                    sug,
                    f"سحر قوي وتكراره مرتفع (فوز {wr:.1f}%) — ارفع تكلفته من {cur_cost} إلى {sug} مانا، أو قلّص ضرره بـ 20% إن رغبت في الحفاظ على سرعة الكيرف.",
                )
        elif wr <= 46.0:
            if cur_cost >= 2:
                sug = cur_cost - 1
                return (
                    f"🔵 خفّض مانا السحر ({cur_cost}←{sug})",
                    "drop",
                    "مانا السحر",
                    sug,
                    f"السحر ضعيف الجدوى مقارنة بتكلفته (فوز {wr:.1f}%) — خفّض تكلفته من {cur_cost} إلى {sug} مانا لتشجيع اللعب به.",
                )
            else:
                return (
                    "🟣 قوّ أثر السحر (+25%)",
                    "buff",
                    "أثر السحر",
                    cur_cost,
                    f"السحر بالفعل عند الحد الأدنى (1 مانا) لكن تأثيره باهت (فوز {wr:.1f}%) — قوِّ قيمة الضرر/الشفاء بـ +25% ليصبح مؤثراً.",
                )
        else:
            return (
                "🟢 تكلفة مثالية",
                "ideal",
                "متوازن",
                cur_cost,
                f"تسعير السحر عادل ومناسب للكيرف ({cur_cost} مانا، فوز {wr:.1f}% عبر {pl:,} مباراة) — أبقِه كما هو.",
            )

    # 3. COVENANTS & LEADERS
    elif card.type in ("covenant", "leader"):
        is_cov = (card.type == "covenant")
        max_cost = 3 if is_cov else 2
        name_t = "العهد" if is_cov else "القائد"

        if wr >= 54.0:
            if cur_cost < max_cost:
                sug = cur_cost + 1
                return (
                    f"🟠 ارفع مانا {name_t} ({cur_cost}←{sug})",
                    "raise1",
                    f"مانا {name_t}",
                    sug,
                    f"هالة {name_t} تحقق فوزاً مرتفعاً ({wr:.1f}%) مقابل تكلفة {cur_cost} مانا فقط — ارفع تكلفتها لـ {sug} مانا.",
                )
            else:
                return (
                    f"🔴 قلّص أثر هالة {name_t} (-20%)",
                    "raise2",
                    f"هالة {name_t}",
                    cur_cost,
                    f"وصلت للحد الأقصى للتكلفة ({cur_cost} مانا) وفوزها مرتفع ({wr:.1f}%) — قلّص قوة الهالة بـ 20% أو زِد ضرر التضحية والشروط.",
                )
        elif wr <= 46.0:
            if cur_cost >= 2:
                sug = cur_cost - 1
                return (
                    f"🔵 خفّض مانا {name_t} ({cur_cost}←{sug})",
                    "drop",
                    f"مانا {name_t}",
                    sug,
                    f"هالة {name_t} ضعيفة الفائدة مقابل تكلفتها (فوز {wr:.1f}%) — خفّض تكلفتها من {cur_cost} إلى {sug} مانا.",
                )
            else:
                return (
                    f"🟣 قوّ هالة {name_t}",
                    "buff",
                    f"هالة {name_t}",
                    cur_cost,
                    f"التكلفة بالفعل 1 مانا (الحد الأدنى) — قوِّ الميزة التكتيكية أو خفّض الجزاء المفروض على اللاعب.",
                )
        else:
            return (
                "🟢 تكلفة مثالية",
                "ideal",
                "متوازن",
                cur_cost,
                f"تسعير هالة {name_t} متوازن ومقبول ({cur_cost} مانا، فوز {wr:.1f}%) — أبقِها كما هي.",
            )

    # 4. ENVIRONMENTS
    else:
        return (
            "🟢 تكلفة مثالية",
            "ideal",
            "متوازن",
            cur_cost,
            f"تسعير بطاقة البيئة ({cur_cost} مانا) متوازن ويخدم ساحة القتال.",
        )


class SkillPricingAdvisorTab:
    """Tab #17: Comprehensive 1,470-Card Surgical Pricing Advisor (Arabic RTL)."""

    def __init__(
        self,
        parent: tk.Frame,
        db: CardDB,
        store: StatsStore,
        on_probe_complete: Optional[Callable[[], None]] = None,
    ) -> None:
        self.parent = parent
        self.db = db
        self.store = store
        self.on_probe_complete = on_probe_complete
        # all_cards | monsters | spells | covenants | skill_tier
        self._mode_var = tk.StringVar(value="all_cards")
        self._filter_var = tk.StringVar(value="all")  # all | raise | drop | ideal
        self._search_var = tk.StringVar(value="")
        self._all_rows: List[Dict[str, Any]] = []
        self._filtered_rows: List[Dict[str, Any]] = []
        self._last_metrics: Dict[str, Any] = {}
        self._meta_stats: Dict[str, Any] = {}
        self._build_layout()

    def _build_layout(self) -> None:
        outer = tk.Frame(self.parent, bg=BG_DARK)
        outer.pack(fill="both", expand=True, padx=8, pady=(2, 6))

        self.scroll_canvas = tk.Canvas(outer, bg=BG_DARK, highlightthickness=0)
        vsb = ttk.Scrollbar(outer, orient="vertical", command=self.scroll_canvas.yview)
        self.scroll_canvas.configure(yscrollcommand=vsb.set)
        vsb.pack(side="left", fill="y")
        self.scroll_canvas.pack(side="right", fill="both", expand=True)

        self.inner = tk.Frame(self.scroll_canvas, bg=BG_DARK)
        self._inner_win = self.scroll_canvas.create_window((0, 0), window=self.inner, anchor="nw")
        self.inner.bind("<Configure>", lambda _e: self.scroll_canvas.configure(scrollregion=self.scroll_canvas.bbox("all")))
        self.scroll_canvas.bind("<Configure>", lambda e: self.scroll_canvas.itemconfigure(self._inner_win, width=max(400, e.width)))

        # 1. Hero Banner (Pricing Health Score + 5 Verdict KPIs + Live Telemetry Status)
        hero = tk.Frame(self.inner, bg=BG_CARD, highlightbackground=COL_GOLD, highlightthickness=1, padx=14, pady=8)
        hero.pack(fill="x", pady=(2, 6))
        self.cv_hero = tk.Canvas(hero, bg=BG_CARD, height=112, highlightthickness=0)
        self.cv_hero.pack(fill="x")
        self.cv_hero.bind("<Configure>", lambda _e: self._draw_hero_banner())

        # 1.5. Live Drafter Duplicate vs Singleton Meta Ratio Strip
        meta_panel = tk.Frame(self.inner, bg=BG_PANEL, highlightbackground=BG_BORDER, highlightthickness=1, padx=10, pady=6)
        meta_panel.pack(fill="x", pady=(0, 6))
        self.cv_meta = tk.Canvas(meta_panel, bg=BG_PANEL, height=44, highlightthickness=0)
        self.cv_meta.pack(fill="x")
        self.cv_meta.bind("<Configure>", lambda _e: self._draw_meta_strip())

        # 2. Mode & Filter Toolbar (2 Clean Non-Cramped Rows)
        ctrl = tk.Frame(self.inner, bg=BG_PANEL, highlightbackground=BG_BORDER, highlightthickness=1, padx=10, pady=6)
        ctrl.pack(fill="x", pady=(0, 8))

        # Row 1: Mode radios
        m_box = tk.Frame(ctrl, bg=BG_PANEL)
        m_box.pack(fill="x", pady=(0, 4))
        tk.Label(m_box, text=rtl("نطاق التقييم الجراحي:"), bg=BG_PANEL, fg=FG_MUTED, font=(FONT_UI, 8, "bold")).pack(side="right", padx=(6, 0))
        for val, lbl in (
            ("all_cards", "🌐 كل البطاقات (1,470)"),
            ("monsters", "⚔️ الوحوش والمصدات (1,000)"),
            ("spells", "📜 بطاقات السحر (300)"),
            ("covenants", "📜 العهود والقادة (160)"),
            ("skill_tier", "⚡ مهارات الوحوش × التكلفة"),
        ):
            tk.Radiobutton(
                m_box, text=rtl(lbl), variable=self._mode_var, value=val,
                command=self.refresh,
                bg=BG_PANEL, fg=FG_TEXT, selectcolor=BG_CARD,
                activebackground=BG_PANEL, activeforeground=COL_GOLD,
                font=(FONT_UI, 8, "bold"), cursor="hand2",
            ).pack(side="right", padx=4)

        # Row 2: Verdict Filter radios + Search box
        row2 = tk.Frame(ctrl, bg=BG_PANEL)
        row2.pack(fill="x")

        f_box = tk.Frame(row2, bg=BG_PANEL)
        f_box.pack(side="right")
        tk.Label(f_box, text=rtl("تصفية القرار:"), bg=BG_PANEL, fg=FG_MUTED, font=(FONT_UI, 8, "bold")).pack(side="right", padx=(6, 0))
        for val, lbl, fg in (
            ("all", "الكل", FG_TEXT),
            ("raise", "🔴 ارفع السعر", "#FCA5A5"),
            ("drop", "🔵 خفّض / قوّي", "#93C5FD"),
            ("ideal", "🟢 مثالي", "#86EFAC"),
        ):
            tk.Radiobutton(
                f_box, text=rtl(lbl), variable=self._filter_var, value=val,
                command=self._apply_filter_and_fill,
                bg=BG_PANEL, fg=fg, selectcolor=BG_CARD,
                activebackground=BG_PANEL, activeforeground=fg,
                font=(FONT_UI, 8, "bold"), cursor="hand2",
            ).pack(side="right", padx=4)

        s_box = tk.Frame(row2, bg=BG_PANEL)
        s_box.pack(side="left")
        tk.Label(s_box, text=rtl("🔍 بحث سريع:"), bg=BG_PANEL, fg=FG_MUTED, font=(FONT_UI, 8, "bold")).pack(side="right", padx=(4, 0))
        ent = tk.Entry(
            s_box, textvariable=self._search_var, width=26,
            bg=BG_INPUT, fg=FG_TEXT, insertbackground=FG_TEXT, relief="flat",
            font=(FONT_UI, 9), justify="right",
        )
        ent.pack(side="right", ipady=2)
        self._search_var.trace_add("write", lambda *_: self._apply_filter_and_fill())

        # 3. Main Sortable Pricing Table (12 Surgical Columns — Full Width + VSB & HSB!)
        tbl_panel = self._make_panel(
            self.inner,
            "جدول التوصية الجراحية المباشرة لتسعير كل بطاقة ومهارة (يحدد هل التعديل لمانا البطاقة أم مانا المهارة أم قوة الأثر)",
            COL_GOLD,
        )
        tbl_panel.pack(fill="x", pady=(0, 8))

        sp_fr = tk.Frame(tbl_panel, bg=BG_PANEL)
        sp_fr.pack(fill="both", expand=True, padx=8, pady=(2, 8))

        headers = [
            ("reason", "التشخيص الجراحي والنصيحة والسبب الرياضي", 360),
            ("verdict", "قرار التسعير", 135),
            ("target", "عنصر التعديل", 100),
            ("vpm", "القيمة/مانا", 88),
            ("impact", "الأثر/المواصفات", 120),
            ("wr", "نسبة الفوز", 80),
            ("upm", "لعب/مباراة", 80),
            ("sug", "المقترح", 88),
            ("cur", "الحالي", 68),
            ("cards", "النوع", 72),
            ("skill", "البطاقة / المهارة", 210),
            ("num", "#", 34),
        ]
        self.sp_tree = ttk.Treeview(sp_fr, columns=tuple(c for c, _, _ in headers), show="headings", height=18)
        for c, txt, w in headers:
            self.sp_tree.heading(c, text=rtl(txt))
            self.sp_tree.column(c, width=w, anchor="e" if c in ("skill", "reason") else "center")
        self.sp_tree.tag_configure("raise2", foreground="#FCA5A5", background="#2E1619")
        self.sp_tree.tag_configure("raise1", foreground="#FDBA74", background="#2B1F15")
        self.sp_tree.tag_configure("ideal", foreground="#4ADE80", background="#142620")
        self.sp_tree.tag_configure("drop", foreground="#93C5FD", background="#152033")
        self.sp_tree.tag_configure("buff", foreground="#D8B4FE", background="#221730")
        self.sp_tree.tag_configure("nodata", foreground="#94A3B8", background=BG_PANEL)

        sp_vsb = ttk.Scrollbar(sp_fr, orient="vertical", command=self.sp_tree.yview)
        sp_hsb = ttk.Scrollbar(sp_fr, orient="horizontal", command=self.sp_tree.xview)
        self.sp_tree.configure(yscrollcommand=sp_vsb.set, xscrollcommand=sp_hsb.set)
        self.sp_tree._has_hsb = True
        self.sp_tree._hsb_widget = sp_hsb
        sp_hsb.pack(side="bottom", fill="x")
        sp_vsb.pack(side="left", fill="y")
        self.sp_tree.pack(side="right", fill="both", expand=True)
        enable_treeview_sorting(self.sp_tree)
        self.sp_tree.bind("<<TreeviewSelect>>", self._on_row_selected)

        # 4. Drill-Down Inspector (Full Width + VSB & HSB!)
        drill_panel = self._make_panel(
            self.inner,
            "تفاصيل البطاقة أو البطاقات الحاملة للمهارة (اضغط على أي صف بالأعلى لمعرفة بطاقاته بالكامل)",
            COL_PURPLE,
        )
        drill_panel.pack(fill="x", pady=(0, 8))

        self.drill_lbl = tk.Label(
            drill_panel,
            text=rtl("اختر أي صف من الجدول بالأعلى لعرض تفاصيل البطاقة وقدراتها ونسبة فوزها بدقة."),
            bg=BG_PANEL, fg=FG_MUTED, font=(FONT_UI, 8), anchor="e", justify="right",
        )
        self.drill_lbl.pack(fill="x", padx=10, pady=(0, 4))

        dr_fr = tk.Frame(drill_panel, bg=BG_PANEL)
        dr_fr.pack(fill="both", expand=True, padx=8, pady=(0, 8))

        d_headers = [
            ("desc", "تفاصيل القدرة والمهارات", 400),
            ("wr", "فوز البطاقة", 88),
            ("plays", "مرات اللعب", 84),
            ("stats", "الإحصائيات", 110),
            ("scost", "تكلفة المهارة", 90),
            ("mcost", "تكلفة الكارت", 86),
            ("name", "اسم البطاقة", 225),
            ("cid", "ID", 54),
        ]
        self.drill_tree = ttk.Treeview(dr_fr, columns=tuple(c for c, _, _ in d_headers), show="headings", height=9)
        for c, txt, w in d_headers:
            self.drill_tree.heading(c, text=rtl(txt))
            self.drill_tree.column(c, width=w, anchor="e" if c in ("name", "desc") else "center")

        dr_vsb = ttk.Scrollbar(dr_fr, orient="vertical", command=self.drill_tree.yview)
        dr_hsb = ttk.Scrollbar(dr_fr, orient="horizontal", command=self.drill_tree.xview)
        self.drill_tree.configure(yscrollcommand=dr_vsb.set, xscrollcommand=dr_hsb.set)
        self.drill_tree._has_hsb = True
        self.drill_tree._hsb_widget = dr_hsb
        dr_hsb.pack(side="bottom", fill="x")
        dr_vsb.pack(side="left", fill="y")
        self.drill_tree.pack(side="right", fill="both", expand=True)
        enable_treeview_sorting(self.drill_tree)

        # 5. Visual Charts Stacked Vertically at Full Screen Width
        p_right = self._make_panel(self.inner, "ترتيب البطاقات والمهارات حسب القيمة/الفوز (المرشحة لرفع السعر مقابل الخفض)", COL_CYAN)
        p_right.pack(fill="x", pady=(0, 8))
        self.cv_vpm = tk.Canvas(p_right, bg=BG_PANEL, height=235, highlightthickness=0)
        self.cv_vpm.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_vpm.bind("<Configure>", lambda _e: self._draw_vpm_chart())

        p_left = self._make_panel(self.inner, "أعلى البطاقات/المهارات تكراراً في المباراة مقابل نسبة الفوز (كاشف الإسبام)", COL_ORANGE)
        p_left.pack(fill="x", pady=(0, 8))
        self.cv_spam = tk.Canvas(p_left, bg=BG_PANEL, height=235, highlightthickness=0)
        self.cv_spam.pack(fill="both", expand=True, padx=8, pady=(2, 8))
        self.cv_spam.bind("<Configure>", lambda _e: self._draw_spam_chart())

    def _make_panel(self, parent: tk.Widget, title: str, accent: str) -> tk.Frame:
        f = tk.Frame(parent, bg=BG_PANEL, highlightbackground=BG_BORDER, highlightthickness=1)
        hdr = tk.Frame(f, bg=BG_PANEL, padx=10, pady=7)
        hdr.pack(fill="x")
        tk.Frame(hdr, bg=accent, width=4, height=15).pack(side="right", padx=(7, 0))
        tk.Label(hdr, text=rtl(title), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold"), anchor="e", justify="right").pack(side="right")
        return f

    def _compute_meta_duplicate_stats(self) -> Dict[str, Any]:
        """Scans saved_models/*/settled_deck.json to track live 1-of vs 2-of drafting ratio."""
        models_dir = os.path.join(os.path.dirname(__file__), "saved_models")
        files = glob.glob(os.path.join(models_dir, "*", "settled_deck.json"))
        total_singles = 0
        total_pairs = 0
        card_single_counts: Dict[int, int] = defaultdict(int)
        card_pair_counts: Dict[int, int] = defaultdict(int)

        for f in files:
            try:
                with open(f, "r", encoding="utf-8") as fp:
                    data = json.load(fp)
                for item in data.get("cards", []):
                    cid = int(item.get("id", 0))
                    copies = int(item.get("copies", 0))
                    if copies == 1:
                        total_singles += 1
                        card_single_counts[cid] += 1
                    elif copies >= 2:
                        total_pairs += 1
                        card_pair_counts[cid] += 1
            except Exception:
                pass

        tot = total_singles + total_pairs
        s_pct = (100.0 * total_singles / tot) if tot > 0 else 0.0
        p_pct = (100.0 * total_pairs / tot) if tot > 0 else 0.0

        top_singles = sorted(card_single_counts.items(), key=lambda kv: kv[1], reverse=True)[:4]
        top_pairs = sorted(card_pair_counts.items(), key=lambda kv: kv[1], reverse=True)[:4]

        return {
            "models_count": len(files),
            "total_singles": total_singles,
            "total_pairs": total_pairs,
            "single_pct": s_pct,
            "pair_pct": p_pct,
            "top_singles": [(self.db.get(cid).name, cnt) for cid, cnt in top_singles if self.db.get(cid)],
            "top_pairs": [(self.db.get(cid).name, cnt) for cid, cnt in top_pairs if self.db.get(cid)],
        }

    def refresh(self) -> None:
        self._meta_stats = self._compute_meta_duplicate_stats()
        self._compute_rows()
        self._apply_filter_and_fill()
        self._draw_hero_banner()
        self._draw_meta_strip()
        self._draw_spam_chart()
        self._draw_vpm_chart()

    def _compute_rows(self) -> None:
        mode = self._mode_var.get()
        ex = self.store.excitement or {}
        sp_map: Dict[str, List[int]] = ex.get("skill_pricing") or {}
        raw_tot_matches = int(ex.get("total_matches", 0))
        tot_matches = max(1, raw_tot_matches)
        has_live_sp = any(int(v[0]) > 0 for v in sp_map.values() if isinstance(v, list) and len(v) >= 8)

        rows: List[Dict[str, Any]] = []

        type_map_ar = {
            "monster": "وحش",
            "spell": "سحر",
            "covenant": "عهد",
            "leader": "قائد",
            "environment": "بيئة",
        }

        # MODES 1..4: Individual Card Evaluation (Surgical Diagnostics)
        if mode in ("all_cards", "monsters", "spells", "covenants"):
            target_cards: List[CardDef] = []
            for c in self.db.cards.values():
                if mode == "all_cards":
                    target_cards.append(c)
                elif mode == "monsters" and c.type == "monster":
                    target_cards.append(c)
                elif mode == "spells" and c.type == "spell":
                    target_cards.append(c)
                elif mode == "covenants" and c.type in ("covenant", "leader"):
                    target_cards.append(c)

            for c in target_cards:
                st = self.store.card_stats.get(str(c.id), {})
                pl = int(st.get("played", 0))
                pw = int(st.get("played_wins", 0))
                wr = (100.0 * pw / pl) if pl >= 10 else None
                upm = pl / tot_matches if raw_tot_matches > 0 else 0.0

                verdict, tag, target_elem, sug_cost, reason = recommend_card_surgical_pricing(
                    card=c,
                    st=st,
                    tot_matches=raw_tot_matches,
                    sp_map=sp_map,
                )

                if c.type == "monster":
                    t_badge = "مصدّة" if c.is_barrier else "وحش"
                    imp_str = f"{c.atk} هـ / {c.deff} د"
                elif c.type == "spell":
                    t_badge = "سحر"
                    sk = str((c.spell_ops or {}).get("kind", ""))
                    imp_str = SPELL_AR.get(sk, (sk, ""))[0][:22]
                elif c.type == "covenant":
                    t_badge = "عهد"
                    imp_str = "هالة مستمرة"
                elif c.type == "leader":
                    t_badge = "قائد"
                    imp_str = "هالة قائد"
                else:
                    t_badge = "بيئة"
                    imp_str = "تأثير ساحة"

                rows.append({
                    "key": f"{c.type}|{c.id}",
                    "skill_label": f"#{c.id} {c.name}",
                    "short_label": f"#{c.id} {c.name[:16]}",
                    "type_badge": t_badge,
                    "target": target_elem,
                    "cards": [c],
                    "card_count": 1,
                    "cur_cost": int(c.mana),
                    "sug_cost": sug_cost,
                    "uses": pl,
                    "upm": upm,
                    "wr": wr,
                    "impact_str": imp_str,
                    "vpm": None,
                    "vpm_str": "--",
                    "verdict": verdict,
                    "tag": tag,
                    "reason": reason,
                    "live_telemetry": True,
                })

        # MODE 5: Active Skills by Tier (Tactical Deep Dive)
        else:
            groups: Dict[str, List[Tuple[CardDef, Dict[str, Any]]]] = defaultdict(list)
            for c in self.db.cards.values():
                if c.type != "monster":
                    continue
                for a in (c.actives or []):
                    kind = str(a.get("kind", "dmg_single"))
                    bcost = int(a.get("cost", 1))
                    gkey = f"{kind}|{bcost}"
                    groups[gkey].append((c, a))

            pre_rows: List[Dict[str, Any]] = []
            vpm_pool: List[float] = []
            upcm_pool: List[float] = []

            for gkey, pairs in groups.items():
                kind, bcost_s = gkey.split("|", 1)
                cur_cost = int(bcost_s)
                matched_keys = [gkey]
                ar_nm, ar_desc = ACTIVE_AR.get(kind, (kind, ""))
                cards_unique = list({c.id: c for c, _ in pairs}.values())
                n_cards = len(cards_unique)

                if has_live_sp:
                    acc = [0] * 10
                    for mk in matched_keys:
                        v = sp_map.get(mk)
                        if isinstance(v, list):
                            for i in range(min(10, len(v))):
                                acc[i] += int(v[i])
                    uses, mana_sum, d_mon, d_face, d_hb, kills, disr, util, sides, wins = acc
                    avg_eff_cost = (mana_sum / uses) if uses > 0 else float(cur_cost)
                    wr = (100.0 * wins / sides) if sides >= 10 else None
                    upm = uses / tot_matches
                    upcm = upm / max(1, n_cards)
                    if uses > 0:
                        dm_pu = d_mon / uses
                        df_pu = d_face / uses
                        hb_pu = d_hb / uses
                        kl_pu = kills / uses
                        ds_pu = disr / uses
                        ut_pu = util / uses
                        imp = compute_skill_impact_value(dm_pu, df_pu, hb_pu, kl_pu, ds_pu, ut_pu)
                        vpm = imp / max(0.5, avg_eff_cost if avg_eff_cost > 0 else float(cur_cost))
                        parts = []
                        if dm_pu >= 0.5:
                            parts.append(f"وحش:{dm_pu:.1f}")
                        if df_pu >= 0.5:
                            parts.append(f"قائد:{df_pu:.1f}")
                        if hb_pu >= 0.5:
                            parts.append(f"شفاء:{hb_pu:.1f}")
                        if kl_pu >= 0.05:
                            parts.append(f"قتل:{kl_pu:.2f}")
                        imp_str = " | ".join(parts[:2]) if parts else f"قيمة {imp:.1f}"
                    else:
                        dm_pu = df_pu = hb_pu = kl_pu = ds_pu = ut_pu = 0.0
                        imp = 0.0
                        vpm = None
                        imp_str = "0 تفعيل"
                else:
                    tot_pl = tot_w = 0
                    for c in cards_unique:
                        st = self.store.card_stats.get(str(c.id), {})
                        tot_pl += int(st.get("played", 0))
                        tot_w += int(st.get("played_wins", 0))
                    uses = tot_pl
                    wr = (100.0 * tot_w / tot_pl) if tot_pl >= 15 else None
                    upm = tot_pl / tot_matches if int(ex.get("total_matches", 0)) > 0 else 0.0
                    upcm = upm / max(1, n_cards)
                    vpm = 12.0 / max(1, cur_cost)
                    imp_str = f"تقديري ({tot_pl:,} لعب)"
                    dm_pu = df_pu = hb_pu = kl_pu = ds_pu = ut_pu = 0.0

                if uses >= 30:
                    upcm_pool.append(upcm)
                    if vpm is not None and vpm > 0:
                        vpm_pool.append(vpm)

                pre_rows.append({
                    "key": gkey,
                    "kind": kind,
                    "cur_cost": cur_cost,
                    "skill_label": f"{ar_nm} [{cur_cost} مانا]",
                    "short_label": f"{ar_nm[:14]} [{cur_cost}م]",
                    "type_badge": "مهارة",
                    "target": "مانا المهارة",
                    "cards": cards_unique,
                    "card_count": n_cards,
                    "uses": uses,
                    "upm": upm,
                    "upcm": upcm,
                    "wr": wr,
                    "vpm": vpm,
                    "impact_str": imp_str,
                    "kills_pu": kl_pu,
                    "dmg_face_pu": df_pu,
                    "heal_buff_pu": hb_pu,
                    "ar_name": ar_nm,
                })

            med_vpm = statistics.median(vpm_pool) if vpm_pool else 10.0
            med_upcm = statistics.median(upcm_pool) if upcm_pool else 0.10

            for pr in pre_rows:
                verdict, tag, sug, reason = recommend_mana_pricing(
                    is_spell=False,
                    cur_cost=pr["cur_cost"],
                    wr=pr["wr"],
                    uses=pr["uses"],
                    tot_matches=raw_tot_matches,
                    uses_per_match=pr["upm"],
                    uses_per_card_pm=pr["upcm"],
                    median_upcm=med_upcm,
                    vpm=pr["vpm"],
                    median_vpm=med_vpm,
                    kills_pu=pr["kills_pu"],
                    dmg_face_pu=pr["dmg_face_pu"],
                    heal_buff_pu=pr["heal_buff_pu"],
                    card_count=pr["card_count"],
                    ar_name=pr["ar_name"],
                )
                vpm_v = pr["vpm"]
                if vpm_v is not None and med_vpm and med_vpm > 0.1:
                    vpm_str = f"{vpm_v:.1f} ({vpm_v / med_vpm:.1f}×)"
                elif vpm_v is not None:
                    vpm_str = f"{vpm_v:.1f}"
                else:
                    vpm_str = "--"
                pr["sug_cost"] = sug
                pr["verdict"] = verdict
                pr["tag"] = tag
                pr["reason"] = reason
                pr["vpm_str"] = vpm_str
                pr["live_telemetry"] = has_live_sp
                rows.append(pr)

        # Priority Sort: raise2 -> raise1 -> drop -> buff -> ideal -> nodata, then by WR desc
        prio = {"raise2": 0, "raise1": 1, "drop": 2, "buff": 3, "ideal": 4, "nodata": 5}
        rows.sort(key=lambda r: (prio.get(r["tag"], 9), -(r["wr"] or 0.0), -r["uses"]))
        self._all_rows = rows

        c_r2 = sum(1 for r in rows if r["tag"] == "raise2")
        c_r1 = sum(1 for r in rows if r["tag"] == "raise1")
        c_ok = sum(1 for r in rows if r["tag"] == "ideal")
        c_dr = sum(1 for r in rows if r["tag"] == "drop")
        c_bf = sum(1 for r in rows if r["tag"] == "buff")
        c_nd = sum(1 for r in rows if r["tag"] == "nodata")
        evaluated = max(0, len(rows) - c_nd)
        fair_score = int(round(100.0 * (c_ok * 1.0 + (c_r1 + c_dr + c_bf) * 0.45) / evaluated)) if evaluated > 0 else 0

        self._last_metrics = {
            "has_data": evaluated > 0,
            "score": fair_score,
            "ideal_cnt": c_ok,
            "good_cnt": c_r1 + c_dr + c_bf,
            "bad_cnt": c_r2,
            "raise2": c_r2,
            "raise1": c_r1,
            "drop": c_dr,
            "buff": c_bf,
            "nodata": c_nd,
            "total": len(rows),
            "evaluated": evaluated,
            "has_live_sp": has_live_sp,
            "mode": mode,
            "tot_matches": raw_tot_matches,
        }

    def _apply_filter_and_fill(self) -> None:
        flt = self._filter_var.get()
        q = self._search_var.get().strip().casefold()
        out = []
        for r in self._all_rows:
            if flt == "raise" and r["tag"] not in ("raise2", "raise1"):
                continue
            if flt == "drop" and r["tag"] not in ("drop", "buff"):
                continue
            if flt == "ideal" and r["tag"] != "ideal":
                continue
            if q:
                hay = f"{r['skill_label']} {r['reason']} {r['key']} {r.get('target', '')}".casefold()
                if q not in hay:
                    continue
            out.append(r)
        self._filtered_rows = out

        self.sp_tree.delete(*self.sp_tree.get_children())
        for idx, r in enumerate(out, 1):
            cur = r["cur_cost"]
            sug = r["sug_cost"]
            diff = sug - cur
            sug_str = f"{sug} مانا ({diff:+d})" if diff != 0 else f"{sug} مانا (=)"
            wr_s = f"{r['wr']:.1f}%" if r["wr"] is not None else "--"
            upm_s = f"{r['upm']:.2f}" if r["uses"] > 0 else "0.00"
            self.sp_tree.insert(
                "",
                "end",
                iid=str(idx - 1),
                values=(
                    rtl(r["reason"]),
                    rtl(r["verdict"]),
                    rtl(r.get("target", "متوازن")),
                    rtl(r["vpm_str"]),
                    rtl(r["impact_str"]),
                    wr_s,
                    upm_s,
                    rtl(sug_str),
                    rtl(f"{cur} مانا"),
                    rtl(r.get("type_badge", "بطاقة")),
                    rtl(r["skill_label"]),
                    str(idx),
                ),
                tags=(r["tag"],),
            )

    def _on_row_selected(self, _evt: Any = None) -> None:
        sel = self.sp_tree.selection()
        if not sel:
            return
        try:
            idx = int(sel[0])
            row = self._filtered_rows[idx]
        except Exception:
            return
        self.drill_tree.delete(*self.drill_tree.get_children())
        cards: List[CardDef] = row.get("cards") or []
        self.drill_lbl.config(
            text=rtl(
                f"العنصر المحدد: {row['skill_label']}  |  القرار: {row['verdict']}  |  عنصر التعديل المستهدف: [{row.get('target', 'متوازن')}]"
            ),
            fg=COL_GOLD,
        )
        items = []
        for c in cards:
            st = self.store.card_stats.get(str(c.id), {})
            pl = int(st.get("played", 0))
            wr = (100.0 * int(st.get("played_wins", 0)) / pl) if pl >= 10 else None
            scost_list = [int(a.get("cost", 0)) for a in (c.actives or [])] if c.type == "monster" else [int(c.mana)]
            scost_s = "/".join(str(x) for x in scost_list) + " م" if scost_list else "--"
            stats_s = f"{c.atk} هـ / {c.deff} د" if c.type == "monster" else (
                "سحر" if c.type == "spell" else ("عهد" if c.type == "covenant" else "قائد")
            )
            items.append((wr or -1.0, pl, c, scost_s, stats_s))
        items.sort(key=lambda x: (x[0], x[1]), reverse=True)
        for wr, pl, c, scost_s, stats_s in items:
            wr_s = f"{wr:.1f}%" if wr >= 0 else "--"
            self.drill_tree.insert(
                "",
                "end",
                values=(
                    rtl(explain_card_arabic(c)[:135]),
                    wr_s,
                    f"{pl:,}",
                    rtl(stats_s),
                    rtl(scost_s),
                    rtl(f"{c.mana} مانا"),
                    c.name,
                    str(c.id),
                ),
            )

    def _draw_hero_banner(self) -> None:
        cv = self.cv_hero
        cv.delete("all")
        w = max(780, cv.winfo_width())
        h = max(108, cv.winfo_height())
        m = self._last_metrics
        if not m:
            return

        sc = int(m.get("score", 0))
        has_data = bool(m.get("has_data"))
        sc_col = COL_GREEN if sc >= 78 else (COL_GOLD if sc >= 58 else COL_RED)
        if not has_data:
            sc_col = FG_MUTED

        bx2 = w - 8
        bx1 = w - 210
        cv.create_rectangle(bx1, 8, bx2, h - 8, fill="#141A24", outline=sc_col, width=2)
        cv.create_text((bx1 + bx2) / 2, 26, text=rtl("عدالة تسعير المانا"), fill=FG_MUTED, font=(FONT_UI, 9, "bold"))
        cv.create_text((bx1 + bx2) / 2, 62, text=f"{sc} / 100" if has_data else "--", fill=sc_col, font=(FONT_UI, 20, "bold"))
        mode_lbl = {
            "all_cards": "1,470 بطاقة شاملة",
            "monsters": "1,000 وحش ومصدّة",
            "spells": "300 بطاقة سحر",
            "covenants": "160 عهد وقائد",
            "skill_tier": "مهارات الوحوش × التكلفة",
        }.get(m.get("mode", "all_cards"), "")
        cv.create_text((bx1 + bx2) / 2, h - 18, text=rtl(mode_lbl), fill=FG_TEXT, font=(FONT_UI, 8))

        if m.get("has_live_sp"):
            src_txt = f"🟢 قياس مباشر جراحي لكل بطاقة ومهارة (من {m.get('tot_matches', 0):,} مباراة — التقييم دقيق 100%)"
            src_col = "#4ADE80"
        else:
            src_txt = f"🟡 وضع التقدير الأولي من {m.get('tot_matches', 0):,} مباراة مسجلة — يبدأ التحديث الجراحي فور بدء التدريب"
            src_col = COL_GOLD
        cv.create_text(bx1 - 14, 18, text=rtl(src_txt), fill=src_col, font=(FONT_UI, 8, "bold"), anchor="e")

        kpis = [
            ("🔴 ارفع (+2 مانا)", m.get("raise2", 0), COL_RED),
            ("🟠 ارفع (+1 مانا)", m.get("raise1", 0), COL_ORANGE),
            ("🟢 تسعير مثالي", m.get("ideal_cnt", 0), COL_GREEN),
            ("🔵 خفّض (-1 مانا)", m.get("drop", 0), COL_BLUE),
            ("🟣 قوّي الأثر", m.get("buff", 0), COL_PURPLE),
        ]
        left_w = max(460, bx1 - 24)
        gap = 8
        cw = (left_w - gap * (len(kpis) - 1)) / len(kpis)
        for idx, (lbl, cnt, col) in enumerate(kpis):
            x2 = bx1 - 14 - idx * (cw + gap)
            x1 = x2 - cw
            cv.create_rectangle(x1, 34, x2, h - 10, fill=BG_PANEL, outline=col, width=1)
            cv.create_text((x1 + x2) / 2, 52, text=rtl(lbl), fill=FG_TEXT, font=(FONT_UI, 8, "bold"))
            cv.create_text((x1 + x2) / 2, 80, text=f"{cnt}", fill=col, font=(FONT_UI, 16, "bold"))

    def _draw_meta_strip(self) -> None:
        """Visual live banner showing the Duplicate vs Singleton Meta Ratio."""
        cv = self.cv_meta
        cv.delete("all")
        w = max(780, cv.winfo_width())
        h = max(40, cv.winfo_height())
        ms = self._meta_stats
        if not ms:
            return

        s_pct = ms.get("single_pct", 0.0)
        p_pct = ms.get("pair_pct", 100.0)
        n_mod = ms.get("models_count", 0)

        # Right label
        cv.create_text(
            w - 12, h / 2,
            text=rtl(f"📊 سلوك الدرافتر عبر {n_mod} نموذجاً:"),
            fill=FG_TEXT, font=(FONT_UI, 8, "bold"), anchor="e",
        )

        # Bar region
        bar_x1 = 280
        bar_x2 = w - 190
        bw = max(100, bar_x2 - bar_x1)
        by1 = 12
        by2 = h - 12

        # Split width
        sw = bw * (s_pct / 100.0)
        pw = bw - sw

        # Draw Single (Cyan) and Pair (Green/Gold) segments
        if sw > 0:
            cv.create_rectangle(bar_x1, by1, bar_x1 + sw, by2, fill=COL_CYAN, outline="")
        if pw > 0:
            cv.create_rectangle(bar_x1 + sw, by1, bar_x2, by2, fill=COL_GOLD, outline="")
        cv.create_rectangle(bar_x1, by1, bar_x2, by2, outline=BG_BORDER, width=1)

        # Text labels on bar
        cv.create_text(
            bar_x1 - 10, h / 2,
            text=f"1-of فريد: {s_pct:.1f}%",
            fill=COL_CYAN, font=(FONT_UI, 8, "bold"), anchor="e",
        )
        cv.create_text(
            bar_x2 + 10, h / 2,
            text=f"2-of مكرر: {p_pct:.1f}%",
            fill=COL_GOLD, font=(FONT_UI, 8, "bold"), anchor="w",
        )

        # Left status hint
        if s_pct > 1.0:
            hint = f"🟢 النماذج تختار بمرونة: {int(ms.get('total_singles',0))} تكتيكي فريد مقابل {int(ms.get('total_pairs',0))} مكرر"
            h_col = COL_GREEN
        else:
            hint = "🟡 بانتظار تشغيل جولة تدريب بالرأس المزدوج لتحديث التشكيلات ذات النسخة الفردية والمكررة"
            h_col = FG_MUTED
        cv.create_text(12, h / 2, text=rtl(hint), fill=h_col, font=(FONT_UI, 8), anchor="w")

    def _draw_vpm_chart(self) -> None:
        cv = self.cv_vpm
        cv.delete("all")
        w = max(420, cv.winfo_width())
        h = max(210, cv.winfo_height())
        valid_wr = [r for r in self._all_rows if r.get("wr") is not None and r["uses"] >= 15]
        if not valid_wr:
            cv.create_text(w / 2, h / 2, text=rtl("بانتظار بيانات تدريب كافية للرسم البياني..."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        valid_wr.sort(key=lambda r: r["wr"], reverse=True)
        pick = valid_wr[:5] + list(reversed(valid_wr[-4:]))
        row_h = (h - 28) / max(1, len(pick))
        mid_x = 125
        bar_w = max(60, w - 245)
        cv.create_line(mid_x + bar_w * 0.5, 10, mid_x + bar_w * 0.5, h - 16, fill="#475569", dash=(3, 3))

        for i, r in enumerate(pick):
            y = 12 + i * row_h
            wr = r["wr"]
            col = COL_RED if wr >= 55.0 else (COL_BLUE if wr <= 45.0 else COL_GREEN)
            x_end = mid_x + bar_w * max(0.05, min(1.0, (wr - 25.0) / 50.0))
            cv.create_rectangle(mid_x, y + 3, x_end, y + row_h - 4, fill=col, outline="")
            cv.create_text(w - 6, y + row_h / 2, text=rtl(r["short_label"]), fill=FG_TEXT, font=(FONT_UI, 8), anchor="e")
            tgt = r.get("target", "")[:8]
            cv.create_text(mid_x - 6, y + row_h / 2, text=f"{wr:.1f}% [{tgt}]", fill=col, font=(FONT_UI, 8, "bold"), anchor="e")

    def _draw_spam_chart(self) -> None:
        cv = self.cv_spam
        cv.delete("all")
        w = max(420, cv.winfo_width())
        h = max(210, cv.winfo_height())
        valid = [r for r in self._all_rows if r["uses"] > 0]
        if not valid:
            cv.create_text(w / 2, h / 2, text=rtl("بانتظار مباريات تدريب لعرض التكرار..."), fill=FG_MUTED, font=(FONT_UI, 9))
            return

        valid.sort(key=lambda r: r["upm"], reverse=True)
        top = valid[:9]
        max_u = max(r["upm"] for r in top) or 1.0
        row_h = (h - 24) / max(1, len(top))
        x_left = 110
        x_right = w - 160
        bw = max(80, x_right - x_left)
        tag_col = {
            "raise2": COL_RED, "raise1": COL_ORANGE, "ideal": COL_GREEN,
            "drop": COL_BLUE, "buff": COL_PURPLE, "nodata": FG_MUTED
        }

        for i, r in enumerate(top):
            y = 12 + i * row_h
            col = tag_col.get(r["tag"], COL_CYAN)
            bx = x_left + bw * min(1.0, r["upm"] / max_u)
            cv.create_rectangle(x_left, y + 3, max(x_left + 4, bx), y + row_h - 4, fill=col, outline="")
            cv.create_text(w - 6, y + row_h / 2, text=rtl(r["short_label"]), fill=FG_TEXT, font=(FONT_UI, 8), anchor="e")
            wr_s = f"{r['wr']:.0f}%" if r["wr"] is not None else "--"
            cv.create_text(x_left - 6, y + row_h / 2, text=f"{r['upm']:.2f}/م | {wr_s}", fill=col, font=(FONT_UI, 8, "bold"), anchor="e")

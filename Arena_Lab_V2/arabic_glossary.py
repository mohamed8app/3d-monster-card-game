# -*- coding: utf-8 -*-
"""
Arena_Lab_V2 — Shared Arabic Localization, Smart Treeview Sorting,
Full Arabic Skills/Passives/Spells/Covenants/Leaders Reference Guide,
and ChatGPT-Ready (Markdown + JSON) Report Exporter.
(Strictly for the offline developer analytics tool; Unity game UI remains English per AGENTS.md.)
"""

from __future__ import annotations

import json
import os
import re
import time
import tkinter as tk
from collections import Counter
from tkinter import ttk
from typing import Any, Dict, List, Optional, Tuple

from engine.card_model import CardDB, CardDef, ELEMENTS, ROLES, TRIBES
from engine.roster_40 import ROSTER_40, ROSTER_MAP

BG_DARK = "#0F131A"
BG_PANEL = "#171C26"
BG_CARD = "#1D2432"
BG_BORDER = "#2A3346"
FG_TEXT = "#EEF2F9"
FG_MUTED = "#94A6C0"

COL_BLUE = "#3B82F6"
COL_CYAN = "#06B6D4"
COL_GREEN = "#10B981"
COL_GOLD = "#F59E0B"
COL_RED = "#EF4444"
COL_PURPLE = "#A855F7"
FONT_UI = "Segoe UI"


def rtl(text: str) -> str:
    return f"\u202B{text}\u202C"


# ======================================================================
# 1. Arabic Names for All 41 AI Models, Tribes, Elements, Roles & Types
# ======================================================================
TRIBE_AR: Dict[str, str] = {
    "Celestials": "السماويون",
    "Demons": "الشياطين",
    "Ghosts": "الأشباح",
    "Mages": "السحرة",
    "Warriors": "المحاربون",
    "Dwarves": "الأقزام",
    "Giants": "العمالقة",
    "Elves": "الجان",
    "Beasts": "الوحوش",
    "Insects": "الحشرات",
    "None": "محايد",
    "Neutral": "محايد",
}

ELEM_AR: Dict[str, str] = {
    "Fire": "النار",
    "Water": "الماء",
    "Nature": "الطبيعة",
    "Electric": "الكهرباء",
    "Ice": "الجليد",
    "Earth": "الأرض",
    "Dark": "الظلام",
    "Light": "النور",
    "Air": "الهواء",
    "Desert": "الصحراء",
    "None": "محايد",
    "Neutral": "محايد",
}

ROLE_AR: Dict[str, str] = {
    "Attacker": "مهاجم",
    "Assassin": "قاتل خاطف",
    "Defender": "مدافع صلب",
    "Barrier": "حاجز دفاعي",
    "Support": "داعم ومعالج",
    "Disruptor": "معطّل ومقيّد",
    "None": "عام",
}

TYPE_AR: Dict[str, str] = {
    "monster": "وحش قتالي",
    "barrier": "حاجز دفاعي (0 هجوم)",
    "spell": "بطاقة سحر",
    "leader": "تميمة قائد",
    "covenant": "عهد وقيد",
    "environment": "ساحة بيئة",
}

STYLE_AR: Dict[str, Tuple[str, str]] = {
    "style_alpha": ("أسلوب الشفاء والصمود", "يركّز على علاج القائد والوحوش والانتصار بدم مرتفع."),
    "style_sparta": ("أسلوب الحصار والتعطيل", "يخنق الخصم بتدمير المانا والتجميد والصمت وكسر العهود."),
    "style_pyro": ("أسلوب الحرق والنيران", "يُراكم الحرق المستمر ويُفجّر أضرار النار القوية."),
    "style_viper": ("أسلوب السم والنزيف", "يذيب دفاع الوحوش ودم القائد بالسم والنزيف الحركي."),
    "style_bastion": ("أسلوب الحصن والدرع", "يحطّم المهاجمين بجدران الدفاع العالي وعكس الضرر والحواجز."),
    "style_mirage": ("أسلوب النسخ والسرقة", "ينسخ بطاقات الخصم ويسرقها ويقلب قوته ضده."),
    "style_hive": ("أسلوب السرب والحشد", "يملأ الـ 6 خانات بالتوكنز ويضرب بقوة التآزر الجماعي."),
    "style_reaper": ("أسلوب المقبرة والإحياء", "يُحيي الوحوش الساقطة ويسترجعها من المقبرة ويزداد قوة بالموت."),
    "style_tactician": ("أسلوب المهارات التكتيكية", "يسيطر على الملعب بتشغيل سلاسل المهارات الفعّالة للوحوش."),
    "style_arcane": ("أسلوب مطر السحر", "يُمطر الخصم بسلسلة متواصلة من التعاويذ والسحر المباشر."),
}

CATEGORY_AR: Dict[str, str] = {
    "Playstyle": "أساليب اللعب",
    "Environment": "العناصر والبيئات",
    "Tribe": "العشائر",
    "Free": "النماذج الحرة",
    "Anomaly": "صائد الثغرات",
}


def arabic_model_name(model_id: str, compact: bool = False) -> str:
    """Returns a pure, natural Arabic name for any of the 41 AI models."""
    if model_id in STYLE_AR:
        nm = STYLE_AR[model_id][0]
        return nm.replace("أسلوب ", "أسلوب ") if not compact else nm
    spec = ROSTER_MAP.get(model_id)
    if not spec:
        return model_id
    if spec.category == "Tribe":
        en = TRIBES[spec.sub_index]
        return f"عشيرة {TRIBE_AR.get(en, en)}"
    if spec.category == "Environment":
        en = ELEMENTS[spec.sub_index]
        return f"عنصر {ELEM_AR.get(en, en)}"
    if spec.category == "Free":
        return f"نموذج حر {spec.sub_index + 1}"
    if spec.category == "Anomaly":
        return "صائد الثغرات 41"
    return model_id


# ======================================================================
# 2. Universal Smart Treeview Column Sorting (Click ANY Column Header!)
# ======================================================================
_NUM_RE = re.compile(r"[-+]?\d+(?:,\d{3})*(?:\.\d+)?")


def _clean_cell_text(val: Any) -> str:
    return str(val).replace("\u202B", "").replace("\u202C", "").strip()


def _smart_sort_key(raw: Any) -> Tuple[int, float, str]:
    txt = _clean_cell_text(raw)
    if not txt or txt in ("--", "!", "بانتظار بيانات", "بانتظار فحص"):
        return (0, -1e18, "")
    # Status badges order cleanly: Ideal(3) > Good(2) > Bad(1)
    if "🟢" in txt or "مثالي" in txt or "PASS" in txt:
        return (2, 3.0, txt)
    if "🟡" in txt or "جيد" in txt or "مقبول" in txt:
        return (2, 2.0, txt)
    if "🔴" in txt or "تعديل" in txt or "عطل" in txt or "بطيء" in txt or "FAIL" in txt:
        return (2, 1.0, txt)
    # Extract primary numeric value if present at start or after short label
    m = _NUM_RE.search(txt)
    if m and (m.start() <= 14 or "%" in txt or "مانا" in txt or "جولة" in txt or "نقطة" in txt or "r =" in txt):
        try:
            num = float(m.group(0).replace(",", ""))
            return (1, num, txt.casefold())
        except Exception:
            pass
    return (3, 0.0, txt.casefold())


def enable_treeview_sorting(tree: ttk.Treeview) -> None:
    """Enables click-to-sort on EVERY column header of a ttk.Treeview with ▲/▼ arrows."""
    base_headings: Dict[str, str] = {}
    cols = list(tree["columns"])
    for c in cols:
        base_headings[c] = _clean_cell_text(tree.heading(c, "text"))

    sort_state: Dict[str, Any] = {"col": None, "desc": False}

    def _sort_by(col: str) -> None:
        items = list(tree.get_children(""))
        if not items:
            return
        if sort_state["col"] == col:
            desc = not sort_state["desc"]
        else:
            # Default numeric/status columns descending, row # ascending
            desc = False if col == "num" else True
        sort_state["col"] = col
        sort_state["desc"] = desc

        keyed = [(_smart_sort_key(tree.set(iid, col)), iid) for iid in items]
        keyed.sort(key=lambda pair: pair[0], reverse=desc)
        for idx, (_k, iid) in enumerate(keyed):
            tree.move(iid, "", idx)

        for c in cols:
            lbl = base_headings[c]
            if c == col:
                arrow = " ▼" if desc else " ▲"
                tree.heading(c, text=rtl(f"{lbl}{arrow}"))
            else:
                tree.heading(c, text=rtl(lbl))

    for c in cols:
        tree.heading(c, command=lambda col=c: _sort_by(col))


# ======================================================================
# 3. Complete Arabic Explanations for ALL Passives, Actives, Spells,
#    Covenants, Leaders, and Environments (100% Coverage of 2,270 Cards!)
# ======================================================================
PASSIVE_AR: Dict[str, Tuple[str, str]] = {
    "adj_buff_summon": ("تعزيز المجاورين عند النزول", "عند الاستدعاء يعزّز هجوم أو دفاع الحلفاء المجاورين له مباشرة."),
    "all_attack": ("هجوم شامل (قفل الهجوم)", "يضرب جميع وحوش الخصم دفعة واحدة، لكن هجومه الأساسي مقفول لا يقبل الزيادة."),
    "arena_upgrade_once": ("ترقية ساحة البيئة", "يرقّي ساحة البيئة المشتركة مستوى واحداً فور نزوله (حتى المستوى 3)."),
    "ascend": ("الارتقاء والتطور", "بعد بقائه جولتين أو تحقيقه قتلة يتطور ويحصل على زيادة ضخمة دائمة وسرقة حياة."),
    "aura_weaken_opp": ("هالة إضعاف المقابل", "يُضعف هجوم الوحش المعادي الواقف في الخانة المقابلة له باستمرار."),
    "bonus_stats_summon": ("تعزيز فوري عند النزول", "يكتسب نقاط هجوم أو دفاع إضافية فور دخوله أرض المعركة."),
    "bypass": ("اختراق مباشر للقائد", "يتجاوز وحوش الخصم ويضرب القائد مباشرة بنسبة مئوية من هجومه."),
    "chameleon": ("الحرباء (كل العشائر)", "يُحتسب كأنه ينتمي لأي عشيرة عند تفعيل هالات وشروط القادة."),
    "cleanse_heal_aura": ("هالة التطهير والشفاء", "يزيل الحالات السلبية (حرق/سم/تجميد) ويشفي دفاع الحلفاء كل دور."),
    "cleave": ("ضربة كاسحة جانبية", "يمتد جزء من ضرر هجومه ليصيب الوحشين المجاورين للهدف أيضاً."),
    "cost_discount": ("تخفيض تكلفة اليد", "يقلّل تكلفة المانا لبطاقات معينة في يدك طالما هو حي على الملعب."),
    "deck_burn_summon": ("حرق ديك الخصم", "يحرق بطاقات من أعلى ديك الخصم إلى المقبرة فور نزوله."),
    "doppelganger": ("المتقمّص الشبيه", "ينسخ إحصائيات أقوى وحش في الملعب ليصبح مثله."),
    "double_strike": ("الضربة المزدوجة", "يهاجم مرتين متتاليتين في كل مرحلة قتال."),
    "enrage": ("الغضب عند الإصابة", "يزداد هجومه دائماً عندما يتلقى ضرراً وينجو منه."),
    "evasion": ("التفادي والمراوغة", "يمتلك فرصة مئوية لتفادي هجوم العدو بالكامل دون تلقي أي ضرر."),
    "flank_bonus": ("قوة الأطراف", "يحصل على زيادة هجوم إضافية عند وضعه في الخانات الطرفية (0، 1، 4، 5)."),
    "grave_scaling": ("نمو من المقبرة", "يزداد هجومه تلقائياً كلما زاد عدد البطاقات في مقبرتك."),
    "hand_discard_summon": ("إتلاف ورقة من يد الخصم", "يجبر الخصم على رمي بطاقة من يده للمقبرة عند النزول."),
    "haste": ("الاندفاع الفوري (Haste)", "يستطيع الهجوم في نفس الدور الذي يُستدعى فيه بلا انتظار."),
    "hydra_split": ("انقسام الهيدرا", "عند موته ينقسم ويستدعي وحشين أصغر لملء الخانات الفارغة."),
    "lb_burn_boom": ("النفس الأخير: انفجار حارق", "عند موته يلحق ضرراً بجميع وحوش الخصم ويشعلها بالحرق."),
    "lb_clone_hand": ("النفس الأخير: عودة للبديل باليد", "عند موته يضيف نسخة أرخص منه إلى يدك."),
    "lb_dmg_leader": ("النفس الأخير: ضربة للقائد", "عند موته يوجّه ضرراً مباشراً إلى قائد الخصم."),
    "lb_draw": ("النفس الأخير: سحب بطاقة", "عند موته تسحب بطاقة إضافية (وقد تشفي القائد)."),
    "lb_freeze": ("النفس الأخير: تجميد عدو", "عند موته يجمّد أقوى وحش معادٍ لدور كامل."),
    "lb_grave_return": ("النفس الأخير: استرجاع من المقبرة", "عند موته يعيد وحشاً من مقبرتك إلى يدك بتكلفة مخفّضة."),
    "lb_heal_leader": ("النفس الأخير: شفاء القائد", "عند موته يعالج دم قائدك (وقد يطهّر الحلفاء)."),
    "lb_mana": ("النفس الأخير: مانا مؤقتة", "عند موته يمنحك نقاط مانا مؤقتة إضافية."),
    "lb_poison": ("النفس الأخير: سحابة سم", "عند موته يصيب جميع وحوش الخصم بالسم المستمر."),
    "lb_token": ("النفس الأخير: استدعاء توكنز", "عند موته يستدعي وحوشاً تابعة (توكنز) في مكانك."),
    "life_leech": ("سرقة الحياة", "يعالج دم قائدك بنسبة مئوية من الضرر الذي يلحقه بالهجوم."),
    "lone_wolf": ("الذئب المنفرد", "يحصل على زيادة هجوم كبيرة إذا كان الوحش الوحيد في ملعبك."),
    "mana_lock_summon": ("قفل مانا الخصم", "يقفل نقاطاً من مانا الخصم في دوره القادم عند النزول."),
    "on_death_grow": ("نمو عند موت الوحوش", "يكتسب هجوماً ودفاعاً دائمين كلما مات أي وحش في الملعب."),
    "on_death_regen": ("تجديد عند الموت", "يعالج دفاعه أو قائدك كلما سقط وحش في المعركة."),
    "on_hit_bleed": ("نزيف عند الضرب", "يصيب هدفه بالنزيف الذي يؤذيه كلما حاول الهجوم."),
    "on_hit_burn": ("حرق عند الضرب", "يشعل هدفه بالحرق المستمر الذي يمتد للقائد عند القتل."),
    "on_hit_poison": ("تسميم عند الضرب", "يحقن هدفه بالسم الذي يآكل دفاعه وحد دفاعه الأقصى كل دور."),
    "on_skill_grow": ("نمو مع المهارات", "يزداد هجومه ودفاعه كلما استخدمت مهارة فعّالة."),
    "on_spell_grow": ("نمو مع السحر", "يزداد هجومه ودفاعه كلما ألقيت بطاقة سحر."),
    "open_lane": ("قناص الخانة المفتوحة", "يحصل على زيادة هجوم قوية إذا كانت الخانة المقابلة له خالية."),
    "pack_aura": ("روح القطيع", "يزداد هجومه عن كل حليف آخر يقف بجانبه على الملعب."),
    "perm_mana_summon": ("زيادة مانا دائمة", "يرفع سقف المانا الدائم لك بمقدار +1 فور نزوله."),
    "pierce": ("اختراق الضرر الفائض", "أي ضرر فائض بعد قتل الوحش المعادي يعبر مباشرة إلى قائد الخصم."),
    "reflect": ("عكس الضرر والأشواك", "يردّ ضرراً تلقائياً لكل وحش يهاجمه (يعمل حتى على الحواجز!)."),
    "root_lock": ("تثبيت بالجذور", "يمنع وحوش العدو من التبديل أو الرجوع لليد عند النزول."),
    "rooted_immune_regen": ("ثبات وتجدّد ومناعة", "لا يُزاح من مكانه ومحصّن ضد الحالات ويتجدّد دفاعه باستمرار."),
    "skill_conduit": ("موصل المهارات", "يرفع قوة المهارات الفعّالة أو يخفض تكلفتها لطالما هو بالملعب."),
    "spell_immune": ("مناعة ضد السحر", "لا يمكن استهدافه بتعاويذ السحر المعادية المباشرة."),
    "spell_ping": ("شرارة السحر", "يلحق ضرراً مباشراً بقائد الخصم كلما ألقيت أي بطاقة سحر."),
    "spell_power_aura": ("هالة قوة السحر", "يزيد قوة وضرر جميع بطاقات السحر التي تلقيها."),
    "spy_plant": ("زرع جاسوس طفيلي", "يزرع وحدة جاسوس في ملعب الخصم تؤذي الوحوش المجاورة لها كل دور."),
    "status_immune": ("مناعة ضد الحالات", "لا يتأثر بالحرق أو السم أو النزيف أو التجميد أو الصمت."),
    "stealth": ("التخفّي والاختفاء", "لا يمكن استهدافه بالهجوم أو السحر حتى يهاجم أو يستخدم مهارته."),
    "strip_buffs_hit": ("نزع التعزيزات", "يمسح كل زيادات الهجوم عن الخصم ويلحق ضرراً إضافياً عند ضربه."),
    "summon_clone_self": ("استنساخ ذاتي", "يستدعي نسخة إضافية من نفسه في خانة فارغة عند النزول."),
    "summon_draw": ("سحب عند النزول", "يسحب لك بطاقة إضافية من الديك فور استدعائه."),
    "summon_freeze": ("تجميد عند النزول", "يجمّد أقوى وحش للخصم فور استدعائه فيمنعه من الهجوم والمهارات."),
    "summon_recall_enemy": ("إرجاع عدو لليد", "يُعيد وحشاً معادياً من الملعب إلى يد الخصم فور النزول."),
    "summon_silence": ("صمت عند النزول", "يُخرس وحشاً معادياً فيعطل كل قدراته السلبية والفعّالة مؤقتاً."),
    "summon_steal_atk": ("سرقة الهجوم", "يسرق نقاط هجوم من وحش معادٍ ويضيفها لنفسه عند النزول."),
    "summon_tax_mana": ("ضريبة مانا على الخصم", "يزيد تكلفة بطاقات الخصم أو يقتطع من ماناه عند النزول."),
    "summon_token": ("استدعاء تابع", "يستدعي وحشاً تابعاً (توكن) بجانبه فور النزول."),
    "taunt": ("الاستفزاز والحماية", "يجبر هجمات الخصم على ضربه أولاً لحماية بقية الوحوش والقائد."),
    "taunt_evasion": ("حماية مع مراوغة", "يحمي الفريق بالاستفزاز مع امتلاك فرصة تفادي الضربات."),
    "taunt_reflect_pct": ("درع شائك مستفز", "يحمي الفريق بالاستفزاز ويعكس نسبة مئوية من الضرر على المهاجم."),
    "temp_mana_summon": ("مانا مؤقتة عند النزول", "يمنحك نقاط مانا إضافية مؤقتة تستخدمها في نفس الدور."),
    "turn_heal_aura": ("هالة الشفاء الدورية", "يعالج الحلفاء أو القائد تلقائياً في بداية كل دور لك."),
    "undying_1def": ("صمود أخير (1 دفاع)", "مرة واحدة بالمباراة: إذا تلقى ضربة قاتلة ينجو منها بـ 1 دفاع."),
}

ACTIVE_AR: Dict[str, Tuple[str, str]] = {
    "aoe_ally": ("تعزيز/شفاء جماعي للحلفاء", "يعزز هجوم ودفاع أو يشفي جميع وحوشك على الملعب دفعة واحدة."),
    "aoe_enemy": ("ضربة جماعية للأعداء", "يلحق ضرراً جماعياً بجميع وحوش الخصم على الملعب."),
    "buff_ally": ("تعزيز حليف", "يمنح وحشاً حليفاً زيادة دائمة في الهجوم أو الدفاع أو الاستفزاز."),
    "buff_center": ("تحصين خانات الوسط", "يقوّي دفاع الوحوش المتمركزة في خانتي الوسط (2 و3) ويشفي القائد."),
    "cleave_enemy": ("ضربة كاسحة لثلاث خانات", "يضرب هدفاً معادياً والوحشين المجاورين له معاً."),
    "copy_enemy_hand": ("نسخ ورقة من يد الخصم", "ينسخ بطاقة عشوائية من يد الخصم إلى يدك بتكلفة أقل بـ 1 مانا."),
    "detonate_dots": ("تفجير الحالات المستمرة", "يفجّر كل تراكمات الحرق والسم والنزيف على الأعداء كضرر فوري."),
    "devour_3": ("الالتهام والامتصاص", "يلتهم وحشاً ضعيفاً ويضيف نسبة من هجومه ودفاعه لنفسه دائماً."),
    "discount_hand_spells": ("تخفيض تكلفة السحر باليد", "يقلل تكلفة جميع بطاقات السحر الموجودة في يدك بـ 1 مانا."),
    "dmg_leader": ("ضربة مباشرة للقائد", "يوجه ضرراً مباشراً لدم قائد الخصم (وقد يضاعف الحرق أو يشفيك)."),
    "dmg_single": ("ضربة مركّزة على هدف", "يلحق ضرراً بوحش معادٍ (أو القائد إذا خلا الملعب) وقد يطبق حالة سلبية."),
    "double_skill_1t": ("مضاعفة المهارات لدور واحد", "يسمح للوحش بتشغيل كل مهارة فعّالة مرتين في هذا الدور."),
    "drain_enemy_mana": ("استنزاف مانا الخصم", "يقفل نقاطاً من مانا الخصم في دوره القادم ويكبّده ضرراً."),
    "gain_temp_mana": ("توليد مانا فورية", "يمنحك مانا إضافية مؤقتة في هذا الدور."),
    "grant_double_strike": ("منح ضربة مزدوجة", "يجعل وحشاً حليفاً يهاجم مرتين في هذا الدور."),
    "grave_to_hand": ("استدعاء من المقبرة لليد", "يعيد أقوى وحش مناسب من مقبرتك إلى يدك بتخفيض مانا."),
    "heal_leader": ("علاج القائد", "يعيد نقاط دم لقائدك وقد يطهر الوحش من الحالات السلبية."),
    "mind_control_temp": ("سيطرة ذهنية مؤقتة", "يستولي على وحش معادٍ ليقاتل بصفك لدور واحد ثم يعود."),
    "pull_hook": ("خطّاف السحب من الأطراف", "يسحب وحشاً من أطراف الخصم إلى المنتصف ويكبده ضرراً."),
    "recall_ally": ("إعادة حليف لليد", "يرجع وحشاً حليفاً مصاباً إلى يدك لإعادة استخدامه سليماً."),
    "recall_enemy": ("طرد عدو لليد", "يعيد وحشاً معادياً إلى يد الخصم ليفتح الخانة أمامك."),
    "revive_board": ("إحياء مباشر من المقبرة", "يُعيد وحشاً ساقطاً من المقبرة إلى الملعب مباشرة."),
    "shifting_curse": ("اللعنة المتنقلة", "يلقي لعنة ضرر مستمر تقفز لوحش آخر كلما مات حاملها!"),
    "spawn_token": ("توليد توكنز قتالية", "يستدعي وحوشاً تابعة (توكنز) لملء الخانات الفارغة."),
    "warrior_call": ("نداء المحارب من الديك", "يسحب أقوى وحش متاح من ديكك مباشرة إلى يدك أو الملعب."),
}

SPELL_AR: Dict[str, Tuple[str, str]] = {
    "aoe_ally": ("تعزيز/علاج شامل للحلفاء", "يقوي أو يعالج جميع وحوشك بالملعب."),
    "aoe_enemy": ("عاصفة ضرر شاملة", "تضرب كل وحوش الخصم دفعة واحدة."),
    "boost_skills": ("شحن قوة المهارات", "تزيد ضرر المهارات الفعّالة وتخفض تكلفتها لهذا الدور."),
    "buff_ally": ("تعزيز وحش حليف", "تمنح وحشاً مختاراً زيادة قوية في الهجوم أو الدفاع."),
    "cleave_enemy": ("سحر الضربة الكاسحة", "تضرب هدفاً معادياً والوحوش المجاورة له."),
    "clone_board": ("استنساخ وحش بالملعب", "تصنع نسخة من وحش موجود وتنزلها بجانبه."),
    "copy_enemy_hand": ("استراق ورقة الخصم", "تنسخ بطاقة من يد الخصم إلى يدك بتكلفة أقل."),
    "counter_betrayal": ("كسر العهد بالخيانة", "تُدمّر عهد الخصم النشط نهائياً مقابل تضحية بجزء من دمك."),
    "counter_exorcism": ("طرد تميمة القائد", "تُدمّر وتُنهي تميمة قائد الخصم نهائياً لبقية المباراة."),
    "counter_oblivion": ("النسيان القاتل للعهد", "تُبطل وتُدمّر عهد الخصم النشط نهائياً."),
    "counter_stand_alone": ("عزل القائد مؤقتاً", "تُخرس هالة قائد الخصم لعدة أدوار متتالية."),
    "deck_burn_spell": ("حرق ديك الخصم", "تتلف بطاقات من ديك الخصم مباشرة."),
    "def_slam": ("صدمة الدفاع الساحقة", "تضرب الخصم بضرر يساوي دفاع أقوى وحش دفاعي لديك!"),
    "detonate_dots": ("تفجير الحرق والسم والنزيف", "تفجر جميع الحالات المستمرة على الأعداء فوراً."),
    "dmg_leader": ("سحر حرق مباشر للقائد", "يوجه ضرراً مباشراً لدم قائد الخصم."),
    "dmg_single": ("صاعقة فردية", "تضرب وحشاً معادياً واحداً بضرر مركز."),
    "doppelganger_spell": ("سحر التقمّص", "تحوّل نسخة لتمتلك قوة أقوى وحش بالملعب."),
    "double_skill_1t": ("تفعيل مزدوج للمهارات", "تتيح لوحش حليف استخدام مهاراته مرتين هذا الدور."),
    "drain_enemy_mana": ("سرقة وتعطيل المانا", "تقفل جزءاً من مانا الخصم بدوره القادم."),
    "echo_next_spell": ("صدى التعويذة القادمة", "تجعل بطاقة السحر التالية التي تلقيها تُنفّذ مرتين!"),
    "execute_enemy": ("إعدام فوري للهدف", "تقضي على وحش معادٍ مصاب أو ضمن شرط دفاع معين."),
    "grant_double_strike": ("بركة الضربة المزدوجة", "تمنح وحشاً حليفاً هجومين متتاليين هذا الدور."),
    "grave_to_hand": ("استرجاع من المقبرة", "تعيد بطاقة وحش من المقبرة ليدك بتكلفة مخفضة."),
    "hand_discard_spell": ("إرغام على الرمي", "تجبر الخصم على التخلص من ورقة في يده."),
    "heal_leader": ("بلسم شفاء القائد", "تعالج نقاطاً كبيرة من دم القائد."),
    "melt_enemy_def": ("إذابة دروع العدو", "تخفض دفاع وحش معادٍ أو كل وحوش الخصم."),
    "mind_control": ("السيطرة الذهنية", "تخطف وحشاً معادياً للقتال بصفك."),
    "perm_mana": ("تصعيد مانا دائم", "ترفع سقف المانا الدائم فوراً."),
    "polymorph": ("التحويل إلى خروف (1/1)", "تحوّل أقوى وحش معادٍ إلى خروف ضعيف 1/1 بلا قدرات!"),
    "pull_hook_spell": ("سحب من الأطراف", "تسحب وحشاً طرفياً للمنتصف وتصيبه بضرر."),
    "pure_draw": ("سحب أوراق صافي", "تسحب بطاقات إضافية فوراً لتغذية يدك."),
    "random_bolts": ("صواعق عشوائية متتابعة", "تطلق وابلاً من الضربات الموزّعة على الأعداء."),
    "recall_spell": ("إعادة إلى اليد", "ترجع وحشاً من الملعب ليد صاحبه."),
    "reduce_enemy_max_mana": ("تحطيم سقف مانا الخصم", "تخفض الحد الأقصى لمانا الخصم."),
    "revive_board": ("بعث من المقبرة للملعب", "تعيد وحشاً من المقبرة مباشرة لأرض المعركة."),
    "shifting_curse": ("تعويذة اللعنة المتنقلة", "تصيب عدواً بلعنة تقفز لغيره بعد موته."),
    "solar_sacrifice": ("التضحية الشمسية", "تضحي بوحش أو دم مقابل انفجار ضرر أو تعزيز ضخم."),
    "spawn_tokens": ("استدعاء جيش التوكنز", "تملأ خاناتك الفارغة بتوكنز قتالية."),
    "spy_plant_spell": ("زرع الجاسوس", "تزرع طفيلياً بملعب الخصم يؤذي جيرانه."),
    "stat_flip": ("قلب الهجوم والدفاع", "تبدّل قيمتي الهجوم والدفاع لوحش بالملعب!"),
    "swap_atk_enemy": ("تبادل الهجوم", "تبدّل هجوم وحشك بهجوم وحش معادٍ."),
    "swap_lanes": ("تبديل الخانات", "تبدّل مواقع وحشين تكتيكياً بين الخانات."),
    "swarm_bite": ("لدغة السرب", "تضرب بضرر يتضاعف بعدد الوحوش في ملعبك."),
    "temp_mana": ("دفعة مانا مؤقتة", "تمنحك مانا إضافية للاستخدام الفوري بهذا الدور."),
    "time_rewind": ("إرجاع الزمن (معجزة)", "تطهّر وحوشك وتعيد دفاعها لما كان عليه بالدور السابق وتشفي القائد!"),
}

ENV_AR_DESC: Dict[int, Tuple[str, str]] = {
    0: ("ساحة النار", "تعزز هجوم وحوش النار وتشعل الأعداء بالحرق عند النزول والهجوم."),
    1: ("ساحة الماء", "تشفي القائد وتطهر وحوش الماء وتزيد دفاعها الأقصى."),
    2: ("ساحة الطبيعة", "تعالج القائد وتنمّي وحوش الطبيعة وتستدعي توكن شجري."),
    3: ("ساحة الكهرباء", "تمنح مانا مؤقتة إضافية وصواعق برق وتعزز وحوش الكهرباء."),
    4: ("ساحة الجليد", "تجمّد أخطر الأعداء وتمنح درعاً يقلل الضرر لوحوش الجليد."),
    5: ("ساحة الأرض", "تحصّن دفاع وحوش الأرض وتمنحها عكس ضرر وتشفي القائد."),
    6: ("ساحة الظلام", "تمتص دم قائد الخصم وتمنح وحوش الظلام سرقة حياة."),
    7: ("ساحة النور", "تطهر وتحصّن وحوش النور وتشفي القائد بسخاء كل دور."),
    8: ("ساحة الهواء", "تطلق عاصفة على الأعداء وتمنح وحوش الهواء سرعة وهجوماً."),
    9: ("ساحة الصحراء", "عاصفة رملية تآكل دفاع الأعداء وتضيف نزيفاً لوحوش الصحراء."),
}

COVENANT_KEY_AR: Dict[str, str] = {
    "rew_atk": "مكافأة: زيادة هجوم كل وحوشك",
    "rew_def": "مكافأة: زيادة دفاع كل وحوشك",
    "rew_stat_pct": "مكافأة: نسبة مئوية إضافية للإحصائيات",
    "rew_hp_turn": "مكافأة: شفاء للقائد كل دور",
    "rew_enemy_face": "مكافأة: ضرر تلقائي لقائد الخصم كل دور",
    "rew_extra_draw": "مكافأة: سحب بطاقة إضافية كل دور",
    "rew_temp_mana": "مكافأة: مانا مؤقتة إضافية كل دور",
    "rew_haste": "مكافأة: كل وحوشك تنزل باندفاع فوري",
    "rew_taunt": "مكافأة: كل وحوشك تكتسب استفزازاً",
    "rew_leech_pct": "مكافأة: سرقة حياة لوحوشك",
    "rew_double_skill": "مكافأة: مضاعفة استخدام المهارات الفعّالة (مرتان/دور)",
    "rew_spell_power": "مكافأة: زيادة قوة السحر",
    "rew_skill_power": "مكافأة: زيادة قوة المهارات الفعّالة",
    "rew_spell_disc": "مكافأة: تخفيض تكلفة السحر",
    "rew_skill_disc": "مكافأة: تخفيض تكلفة المهارات",
    "rew_revive_disc": "مكافأة: تخفيض تكلفة الإحياء من المقبرة",
    "rew_token_turn": "مكافأة: استدعاء توكن مجاني كل دور",
    "rew_reflect": "مكافأة: عكس ضرر ثابت لوحوشك",
    "rew_reflect_pct": "مكافأة: عكس ضرر نسبي لوحوشك",
    "rew_dmg_reduce": "مكافأة: تقليل الضرر الوارد لوحوشك",
    "rew_burn": "مكافأة: حرق إضافي عند الضرب",
    "rew_poison": "مكافأة: سم إضافي عند الضرب",
    "rew_bleed": "مكافأة: نزيف إضافي عند الضرب",
    "rew_cleanse": "مكافأة: تطهير تلقائي للحلفاء",
    "rew_disrupt_eot": "مكافأة: إتلاف ورقة من يد الخصم كل دورين",
    "rew_steal_copy": "مكافأة: نسخ ورقة من يد الخصم كل دورين",
    "rew_enemy_mana_loss": "مكافأة: قفل مانا الخصم كل دور",
    "self_hp_init_loss": "قيد: خسارة فورية من دم قائدك عند التفعيل",
    "self_hp_cap": "قيد: خفض سقف دم قائدك الأقصى",
    "self_hp_turn": "قيد: نزف دم من قائدك في كل دور",
    "self_hp_on_attack": "قيد: خسارة دم من قائدك عند كل هجوم",
    "self_atk_pen": "قيد: خصم من هجوم وحوشك",
    "self_def_pen": "قيد: خصم من دفاع وحوشك",
    "self_def_cap": "قيد: سقف أقصى لدفاع وحوشك",
    "self_atk_pct_pen": "قيد: خصم نسبي من هجوم وحوشك",
    "self_def_pct_pen": "قيد: خصم نسبي من دفاع وحوشك",
    "max_hand_self": "قيد: تقليص سقف أوراق اليد",
    "max_board_self": "قيد: تقليص عدد الخانات المتاحة بملعبك",
    "max_mana_self": "قيد: خفض سقف المانا الأقصى لديك",
    "max_plays_turn": "قيد: حد أقصى لعدد البطاقات الملعوبة بالدور",
    "max_summons_turn": "قيد: حد أقصى لعدد الاستدعاءات بالدور",
    "ban_self_spells": "قيد: منع استخدام بطاقات السحر",
    "ban_self_heal": "قيد: منع شفاء قائدك",
    "ban_self_revive": "قيد: منع الإحياء من المقبرة",
    "ban_self_extra_draw": "قيد: منع السحب الإضافي",
    "ban_self_dots": "قيد: منع استخدام الحرق والسم والنزيف",
    "ban_face_attack": "قيد: منع مهاجمة قائد الخصم مباشرة",
    "ban_face_spell": "قيد: منع سحر الحرق المباشر للقائد",
    "ban_normal_attack": "قيد: منع الهجوم العادي للوحوش",
    "ban_skill_gt1": "قيد: منع المهارات التي تكلّف أكثر من 1 مانا",
    "ban_hand_monster_ge": "قيد: منع إنزال الوحوش الثقيلة فوق تكلفة معينة",
    "single_attacker_turn": "قيد: السماح لوحش واحد فقط بالهجوم كل دور",
    "discard_hand_eot": "قيد: رمي كامل اليد نهاية كل دور",
    "discard_cheapest_2t": "قيد: رمي أرخص ورقة بيدك كل دورين",
    "ally_dmg_eot": "قيد: ضرر ذاتي لوحوشك نهاية كل دور",
    "sac_every_2nd": "قيد: التضحية بأضعف وحش لديك كل دورين",
    "turn_limit_lose": "قيد قاتل: تخسر المباراة إن لم تفز قبل الجولة المحددة!",
    "delay_leader_turn5": "قيد: تأخير تفعيل القائد حتى الجولة 5",
    "monster_tax_all": "قيد: زيادة تكلفة كل الوحوش",
    "monster_tax_high": "قيد: زيادة تكلفة الوحوش الثقيلة",
    "spell_tax_all": "قيد: زيادة تكلفة كل بطاقات السحر",
    "self_mana_tax_if_lt2": "قيد: خصم 1 مانا إذا كان ملعبك به أقل من وحشين",
    "self_hp_extra_spell_dmg": "قيد: ضرر إضافي على قائدك عند إلقاء السحر",
    "skip_every_3rd": "قيد: تخطّي قتال/تأثير كل ثالث دور",
    "self_minor_restriction": "قيد خفيف على أسلوب اللعب",
}

LEADER_KEY_AR: Dict[str, str] = {
    "aura_atk": "بركة سلف مستمرة: زيادة هجوم وحوش العشيرة",
    "aura_def": "بركة سلف مستمرة: زيادة دفاع وحوش العشيرة",
    "aura_heal_def": "بركة سلف مستمرة: شفاء دفاع كل وحوش العشيرة بداية الدور",
    "aura_heal_hp": "بركة سلف مستمرة: شفاء دم القائد بداية الدور (بوجود حليف من العشيرة)",
    "aura_cleanse_turn": "بركة سلف مستمرة: تطهير حليف من العشيرة وشفاء دفاعه بداية الدور",
    "aura_banner_token": "بركة سلف مستمرة: استدعاء راية توكن للعشيرة إذا خلا الملعب بداية الدور",
    "aura_evasion": "بركة سلف مستمرة: نسبة تفادي (%) لوحوش العشيرة",
    "aura_reflect": "بركة سلف مستمرة: عكس ضرر ثابت على المهاجم لوحوش العشيرة",
    "aura_dmg_reduce": "بركة سلف مستمرة: تقليل كل ضرر وارد على وحوش العشيرة",
    "aura_leech": "بركة سلف مستمرة: سرقة حياة (%) عند ضرب وحوش العشيرة",
    "aura_burn": "بركة سلف مستمرة: حرق عند الضرب لوحوش العشيرة",
    "aura_poison": "بركة سلف مستمرة: سم عند الضرب لوحوش العشيرة",
    "aura_bleed": "بركة سلف مستمرة: نزيف عند الضرب لوحوش العشيرة",
    "aura_cleave_pct": "بركة سلف مستمرة: رذاذ ضربة كاسحة (%) للخانات المجاورة لوحوش العشيرة",
    "aura_pierce_pct": "بركة سلف مستمرة: اختراق دروع (%) نحو قائد الخصم لوحوش العشيرة",
    "aura_anti_barrier": "بركة سلف مستمرة: هجوم إضافي ضد الحواجز والمستفزين لوحوش العشيرة",
    "aura_center_atk": "بركة سلف مستمرة: زيادة هجوم وحوش العشيرة بخانات الوسط (2-3)",
    "aura_flank_atk": "بركة سلف مستمرة: زيادة هجوم وحوش العشيرة بخانات الأطراف (0-1-4-5)",
    "aura_open_lane_atk": "بركة سلف مستمرة: زيادة هجوم وحوش العشيرة أمام خانة فارغة",
    "aura_pack_atk": "بركة سلف مستمرة: زيادة هجوم وحوش العشيرة عند وجود 3+ منها بالملعب",
    "aura_grave_atk": "بركة سلف مستمرة: +1 هجوم لكل 2 من وحوش العشيرة بالمقبرة (بحد أقصى)",
    "aura_low_hp_atk": "بركة سلف مستمرة: زيادة هجوم وحوش العشيرة عندما يكون دم قائدك <= 220",
    "aura_high_hp_atk": "بركة سلف مستمرة: زيادة هجوم وحوش العشيرة عندما يكون دم قائدك >= 280",
    "aura_skill_disc": "بركة سلف مستمرة: تخفيض تكلفة المهارات الفعّالة لوحوش العشيرة",
    "aura_skill_power": "بركة سلف مستمرة: زيادة قوة المهارات الفعّالة لوحوش العشيرة",
    "aura_spell_power": "بركة سلف مستمرة: زيادة قوة السحر (بوجود حليف من العشيرة)",
    "aura_spell_ping": "بركة سلف مستمرة: ضرر لقائد الخصم عند إلقاء سحر (بوجود حليف من العشيرة)",
    "aura_death_ping": "بركة سلف مستمرة: ضرر لقائد الخصم عند سقوط وحش من العشيرة",
    "aura_weaken_opp": "بركة سلف مستمرة: إضعاف هجوم وحوش الخصم (بوجود حليف من العشيرة)",
    "aura_survive_atk": "بركة سلف مستمرة: زيادة هجوم دائمة لوحش العشيرة كلما نجا من ضربة",
    "aura_kill_heal": "بركة سلف مستمرة: شفاء دفاع وحش العشيرة كلما قتل عدواً",
}


def explain_card_arabic(c: CardDef) -> str:
    """Produces a clear, human-friendly Arabic explanation of ANY of the 2,270 cards."""
    parts: List[str] = []
    if c.type == "environment":
        nm, desc = ENV_AR_DESC.get(c.env_id, ("ساحة بيئة", "تغيّر خصائص ساحة القتال المشتركة."))
        return f"ساحة بيئة ({nm} • 1 مانا): {desc}"
    if c.type == "leader":
        ops = c.leader_ops or {}
        tr_i = int(ops.get("tribe", -1))
        tr_nm = TRIBE_AR.get(TRIBES[tr_i], "عام") if 0 <= tr_i < 10 else "عام"
        parts.append(f"تميمة قائد لعشيرة ({tr_nm}) • 0 مانا")
        for k, v in ops.items():
            if k in ("tribe", "style") or not v:
                continue
            lbl = LEADER_KEY_AR.get(k, k)
            parts.append(f"{lbl} ({v})" if isinstance(v, (int, float)) and not isinstance(v, bool) else lbl)
        return " | ".join(parts)
    if c.type == "covenant":
        ops = c.covenant_ops or {}
        parts.append("عهد وقيد (مذبح الجناح الأيمن • 0 مانا)")
        for k, v in ops.items():
            if k in ("style", "tier") or not v:
                continue
            lbl = COVENANT_KEY_AR.get(k, k)
            parts.append(f"{lbl} ({v})" if isinstance(v, (int, float)) and not isinstance(v, bool) else lbl)
        return " | ".join(parts)
    if c.type == "spell":
        op = c.spell_ops or {}
        k = str(op.get("kind", "dmg_single"))
        nm, desc = SPELL_AR.get(k, (k, "تأثير سحري خاص."))
        v1 = op.get("v1", 0)
        extra = f" (قيمة: {v1})" if v1 else ""
        return f"سحر ({c.mana} مانا) — {nm}{extra}: {desc}"

    # Monster or Barrier
    el = ELEM_AR.get(c.element_name, c.element_name)
    tr = TRIBE_AR.get(c.tribe_name, c.tribe_name)
    rl = ROLE_AR.get(c.role_name, c.role_name)
    head = f"حاجز دفاعي ({c.mana} مانا | 0 هجوم / {c.deff} دفاع)" if c.is_barrier else f"وحش ({el} • {tr} • {rl} | {c.mana} مانا | {c.atk} هجوم / {c.deff} دفاع)"
    parts.append(head)
    for pa in c.passives:
        pk = str(pa.get("kind", ""))
        pnm, pdesc = PASSIVE_AR.get(pk, (pk, "قدرة سلبية خاصة."))
        v1 = pa.get("v1", pa.get("pct", 0))
        parts.append(f"[سلبية: {pnm}" + (f" ({v1})" if v1 else "") + f" — {pdesc}]")
    for idx_a, ac in enumerate(c.actives):
        ak = str(ac.get("kind", ""))
        anm, adesc = ACTIVE_AR.get(ak, (ak, "مهارة فعّالة خاصة."))
        acost = ac.get("cost", 1)
        v1 = ac.get("v1", 0)
        parts.append(f"[مهارة {idx_a+1} ({acost} مانا): {anm}" + (f" ({v1})" if v1 else "") + f" — {adesc}]")
    return " • ".join(parts)


# ======================================================================
# 4. Interactive Arabic Skills, Passives, Spells & 2,270-Card Guide Window
# ======================================================================
def open_arabic_skills_guide(parent: tk.Tk, db: CardDB, store: Any) -> None:
    """Opens a clean Arabic RTL searchable & sortable guide explaining ALL skills, passives & cards."""
    win = tk.Toplevel(parent)
    win.title(rtl("دليل القدرات والمهارات والبطاقات — شرح عربي كامل (للمحلل فقط)"))
    win.geometry("1160x740")
    win.configure(bg=BG_DARK)

    hdr = tk.Frame(win, bg=BG_PANEL, padx=14, pady=10)
    hdr.pack(fill="x", padx=10, pady=(10, 4))
    tk.Label(
        hdr,
        text=rtl("الدليل العربي الشامل لكل القدرات السلبية (70) والمهارات الفعّالة (25) والتعاويذ (45) والـ 2,270 بطاقة"),
        bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 11, "bold"), anchor="e",
    ).pack(side="right")
    tk.Label(
        hdr, text=rtl("(اضغط على أي عنوان عمود للترتيب فوراً)"),
        bg=BG_PANEL, fg=COL_GOLD, font=(FONT_UI, 8, "bold"),
    ).pack(side="left")

    nb = ttk.Notebook(win)
    nb.pack(fill="both", expand=True, padx=10, pady=(4, 10))

    # Count how many cards carry each passive / active / spell
    p_cnts: Counter = Counter()
    a_cnts: Counter = Counter()
    s_cnts: Counter = Counter()
    for c in db.cards.values():
        for pa in c.passives:
            p_cnts[str(pa.get("kind", ""))] += 1
        for ac in c.actives:
            a_cnts[str(ac.get("kind", ""))] += 1
        if c.spell_ops:
            s_cnts[str(c.spell_ops.get("kind", ""))] += 1

    def _build_dict_tab(title: str, mapping: Dict[str, Tuple[str, str]], cnts: Counter) -> None:
        fr = tk.Frame(nb, bg=BG_DARK)
        nb.add(fr, text=rtl(title))
        cols = (("desc", "الشرح الواضح بالعربي", 540), ("cnt", "عدد البطاقات", 100), ("code", "الرمز البرمجي", 160), ("name", "الاسم العربي", 220), ("num", "#", 40))
        tree = ttk.Treeview(fr, columns=tuple(c[0] for c in cols), show="headings")
        for cid, txt, w in cols:
            tree.heading(cid, text=rtl(txt))
            tree.column(cid, width=w, anchor="e" if cid in ("name", "desc") else "center")
        vsb = ttk.Scrollbar(fr, orient="vertical", command=tree.yview)
        tree.configure(yscrollcommand=vsb.set)
        vsb.pack(side="left", fill="y")
        tree.pack(side="right", fill="both", expand=True, padx=4, pady=4)
        for idx, (k, (nm, desc)) in enumerate(sorted(mapping.items(), key=lambda kv: cnts.get(kv[0], 0), reverse=True), 1):
            tree.insert("", "end", values=(rtl(desc), f"{cnts.get(k, 0)} بطاقة", k, rtl(nm), idx))
        enable_treeview_sorting(tree)

    _build_dict_tab("القدرات السلبية (70 Passive)", PASSIVE_AR, p_cnts)
    _build_dict_tab("المهارات الفعّالة (25 Active)", ACTIVE_AR, a_cnts)
    _build_dict_tab("تأثيرات السحر (45 Spell)", SPELL_AR, s_cnts)

    # Tab 4: Covenants, Leaders & Environments Reference
    fr_sp = tk.Frame(nb, bg=BG_DARK)
    nb.add(fr_sp, text=rtl("العهود والقادة والبيئات"))
    cols_sp = (("desc", "الشرح العربي", 580), ("cat", "الفئة", 120), ("code", "المعرّف", 180), ("num", "#", 40))
    t_sp = ttk.Treeview(fr_sp, columns=tuple(c[0] for c in cols_sp), show="headings")
    for cid, txt, w in cols_sp:
        t_sp.heading(cid, text=rtl(txt))
        t_sp.column(cid, width=w, anchor="e" if cid == "desc" else "center")
    vsb_sp = ttk.Scrollbar(fr_sp, orient="vertical", command=t_sp.yview)
    t_sp.configure(yscrollcommand=vsb_sp.set)
    vsb_sp.pack(side="left", fill="y")
    t_sp.pack(side="right", fill="both", expand=True, padx=4, pady=4)
    rn = 1
    for eid, (enm, edesc) in ENV_AR_DESC.items():
        t_sp.insert("", "end", values=(rtl(f"{enm}: {edesc}"), rtl("ساحة بيئة"), f"env_{eid}", rn)); rn += 1
    for k, desc in COVENANT_KEY_AR.items():
        t_sp.insert("", "end", values=(rtl(desc), rtl("عهد وقيد"), k, rn)); rn += 1
    for k, desc in LEADER_KEY_AR.items():
        t_sp.insert("", "end", values=(rtl(desc), rtl("تميمة قائد"), k, rn)); rn += 1
    enable_treeview_sorting(t_sp)

    # Tab 5: Live Searchable 2,270-Card Arabic Explainer
    fr_c = tk.Frame(nb, bg=BG_DARK)
    nb.add(fr_c, text=rtl("باحث البطاقات الـ 2,270 (شرح عربي)"))

    sbar = tk.Frame(fr_c, bg=BG_PANEL, padx=10, pady=6)
    sbar.pack(fill="x", padx=4, pady=(4, 2))
    tk.Label(sbar, text=rtl("ابحث برقم البطاقة أو اسمها أو عشيرتها:"), bg=BG_PANEL, fg=FG_TEXT, font=(FONT_UI, 9, "bold")).pack(side="right", padx=(4, 0))
    q_var = tk.StringVar(value="")
    ent = tk.Entry(sbar, textvariable=q_var, bg=BG_CARD, fg=FG_TEXT, insertbackground="#FFFFFF", justify="right", width=34, font=(FONT_UI, 9))
    ent.pack(side="right", padx=8)
    cnt_lbl = tk.Label(sbar, text="", bg=BG_PANEL, fg=COL_CYAN, font=(FONT_UI, 8, "bold"))
    cnt_lbl.pack(side="left")

    cols_c = (
        ("explain", "الشرح العربي الكامل للقدرات والمهارات", 480),
        ("wr", "فوز التدريب", 82),
        ("stats", "مانا/هجوم/دفاع", 104),
        ("type", "الفئة", 105),
        ("name", "اسم البطاقة", 190),
        ("num", "ID", 52),
    )
    t_c = ttk.Treeview(fr_c, columns=tuple(c[0] for c in cols_c), show="headings")
    for cid, txt, w in cols_c:
        t_c.heading(cid, text=rtl(txt))
        t_c.column(cid, width=w, anchor="e" if cid in ("explain", "name") else "center")
    vsb_c = ttk.Scrollbar(fr_c, orient="vertical", command=t_c.yview)
    t_c.configure(yscrollcommand=vsb_c.set)
    vsb_c.pack(side="left", fill="y")
    t_c.pack(side="right", fill="both", expand=True, padx=4, pady=4)
    enable_treeview_sorting(t_c)

    detail_var = tk.StringVar(value=rtl("اضغط على أي بطاقة لعرض شرحها العربي الكامل هنا..."))
    tk.Label(
        fr_c, textvariable=detail_var, bg=BG_CARD, fg=COL_GOLD,
        font=(FONT_UI, 9, "bold"), anchor="e", justify="right", wraplength=1080, padx=10, pady=8,
    ).pack(fill="x", padx=4, pady=(0, 4))

    def _on_sel(_e: Any) -> None:
        sel = t_c.selection()
        if not sel:
            return
        vals = t_c.item(sel[0], "values")
        cid = int(vals[5])
        card = db.cards.get(cid)
        if card:
            detail_var.set(rtl(f"بطاقة #{cid} — {card.name}:  {explain_card_arabic(card)}"))

    t_c.bind("<<TreeviewSelect>>", _on_sel)

    def _populate_cards(*_a: Any) -> None:
        q = q_var.get().strip().casefold()
        t_c.delete(*t_c.get_children())
        shown = 0
        for cid in range(1, db.num_cards + 1):
            c = db.cards[cid]
            t_lbl = TYPE_AR.get("barrier" if c.is_barrier else c.type, c.type)
            el_ar = ELEM_AR.get(c.element_name, "")
            tr_ar = TRIBE_AR.get(c.tribe_name, "")
            exp = explain_card_arabic(c)
            if q:
                hay = f"{cid} {c.name} {t_lbl} {el_ar} {tr_ar} {c.element_name} {c.tribe_name} {exp}".casefold()
                if q not in hay:
                    continue
            st = store.card_stats.get(str(cid), {})
            pl = int(st.get("played", 0))
            wr_s = f"{100.0 * int(st.get('played_wins', 0)) / pl:.1f}%" if pl >= 10 else "--"
            st_s = f"{c.mana}م | {c.atk}/{c.deff}" if c.type == "monster" else f"{c.mana} مانا"
            t_c.insert("", "end", values=(rtl(exp[:140]), wr_s, rtl(st_s), rtl(t_lbl), c.name, cid))
            shown += 1
            if shown >= 400 and not q:
                break
        cnt_lbl.config(text=rtl(f"معروض: {shown:,} من 2,270 (ابحث بالرقم أو الاسم أو العشيرة أو وصف القدرة بالعربي)"))

    q_var.trace_add("write", _populate_cards)
    _populate_cards()


# ======================================================================
# 5. Full Report Exporter (ChatGPT-Ready Markdown + Structured JSON)
# ======================================================================
def export_full_analytics_report(app: Any, out_dir: str) -> Tuple[str, str, str, str]:
    """
    Exports ALL 13 Pillars, ALL 188 Indicators, ALL 41 Models (Arabic names),
    and Top Cards/Staples/Gems to THREE formats at once:
      - Markdown (.md) formatted cleanly for direct paste/upload to ChatGPT
      - Structured JSON (.json) for programmatic analysis / GPT Code Interpreter
      - Excel-ready CSV (.csv, UTF-8-BOM) with all 188 indicators + 41 models
    Returns (md_path, json_path, csv_path, md_content).
    """
    import csv

    os.makedirs(out_dir, exist_ok=True)
    ts_tag = time.strftime("%Y%m%d_%H%M%S")
    ts_hr = time.strftime("%Y-%m-%d %H:%M:%S")
    md_path = os.path.join(out_dir, f"DeckGame_Full_Report_{ts_tag}.md")
    json_path = os.path.join(out_dir, f"DeckGame_Full_Report_{ts_tag}.json")
    csv_path = os.path.join(out_dir, f"DeckGame_Indicators_{ts_tag}.csv")

    pillar_specs = [
        (1, "معيار إيقاع المباراة", app.pace_tab, app.pace_tab.pace_tree),
        (2, "معيار بقاء الوحوش", app.survival_tab, app.survival_tab.surv_tree),
        (3, "معيار جودة القتال", app.combat_tab, app.combat_tab.cq_tree),
        (4, "معيار استخدام البطاقات", app.utilization_tab, app.utilization_tab.cu_tree),
        (5, "معيار الموارد والتكلفة", app.resource_tab, app.resource_tab.re_tree),
        (6, "معيار توازن العشائر والعناصر والنماذج", app.balance_tab, app.balance_tab.bal_tree),
        (7, "معيار أداء الميكانيكيات", app.mechanics_tab, app.mechanics_tab.mp_tree),
        (8, "معيار طرق الفوز", app.victory_tab, app.victory_tab.vc_tree),
        (9, "معيار الذكاء الاستراتيجي", app.strategy_tab, app.strategy_tab.si_tree),
        (10, "معيار المتعة والإثارة", app.enjoyment_tab, app.enjoyment_tab.enj_tree),
        (11, "معيار العدالة التنافسية", app.fairness_tab, app.fairness_tab.cf_tree),
        (12, "معيار سلامة المحاكي والقواعد", app.integrity_tab, app.integrity_tab.int_tree),
        (13, "معيار اكتشاف الميتا والتنوع الاستراتيجي", app.meta_tab, app.meta_tab.meta_tree),
    ]

    scores = []
    pillars_json = []
    md_lines: List[str] = [
        "# تقرير التحليل الشامل للعبة Deck Game (منظومة الـ 13 معياراً)",
        "",
        f"> **تاريخ التصدير:** `{ts_hr}`  ",
        f"> **إصدار البطاقات:** `{app.db.version}` (`{app.db.num_cards:,}` بطاقة)  ",
        f"> **مصدر البيانات:** `{app.active_source_label}`  ",
        "> **ملاحظة لـ ChatGPT:** هذا التقرير يحتوي على نتائج محاكاة حقيقية من تدريب الذكاء الاصطناعي عبر 41 نموذجاً مستقلاً و13 معيار جودة. المطلوب: تحليل نقاط الضعف الحمراء/الصفراء واقتراح خطة توازن دقيقة دون كسر نقاط القوة الخضراء.",
        "",
    ]

    for num, pname, tab_obj, tree in pillar_specs:
        lm = getattr(tab_obj, "_last_metrics", {}) or {}
        sc = int(lm.get("score", 0)) if lm.get("has_data") else None
        if sc is not None:
            scores.append(sc)
        rows_out = []
        for iid in tree.get_children(""):
            vals = [_clean_cell_text(v) for v in tree.item(iid, "values")]
            # Standard column layout across all 13 tabs: (diag, status, good, ideal, actual, metric, num)
            if len(vals) >= 7:
                rows_out.append({
                    "num": vals[6], "metric": vals[5], "actual": vals[4],
                    "ideal": vals[3], "good": vals[2], "status": vals[1], "diagnosis": vals[0],
                })
        pillars_json.append({
            "pillar_num": num, "pillar_name": pname, "score": sc,
            "ideal_cnt": lm.get("ideal_cnt", 0), "good_cnt": lm.get("good_cnt", 0), "bad_cnt": lm.get("bad_cnt", 0),
            "indicators": rows_out,
        })

    overall = round(sum(scores) / len(scores), 1) if scores else 0.0
    md_lines.append(f"## 1. الملخص التنفيذي — صحة اللعبة الإجمالية: `{overall} / 100%`")
    md_lines.append("")
    md_lines.append("| # | المعيار | التقييم | مثالي 🟢 | جيد 🟡 | تعديل 🔴 |")
    md_lines.append("|---:|---|:---:|:---:|:---:|:---:|")
    for p in pillars_json:
        sc_s = f"**{p['score']}%**" if p["score"] is not None else "بانتظار تدريب"
        md_lines.append(f"| {p['pillar_num']} | {p['pillar_name']} | {sc_s} | {p['ideal_cnt']} | {p['good_cnt']} | {p['bad_cnt']} |")
    md_lines.append("")

    # Detailed 13-Pillar Tables
    md_lines.append("## 2. التفاصيل الكاملة لجميع المعايير (188 مؤشراً)")
    for p in pillars_json:
        sc_s = f"{p['score']}%" if p["score"] is not None else "لا توجد بيانات بعد"
        md_lines.append(f"\n### {p['pillar_num']}. {p['pillar_name']} (`{sc_s}`)")
        md_lines.append("| # | المؤشر | النتيجة الحالية | المثالي 🟢 | الجيد 🟡 | التقييم | التشخيص التلقائي |")
        md_lines.append("|---:|---|---|:---:|:---:|:---:|---|")
        for r in p["indicators"]:
            md_lines.append(f"| {r['num']} | {r['metric']} | **{r['actual']}** | {r['ideal']} | {r['good']} | {r['status']} | {r['diagnosis']} |")

    # Full 41-Model Arabic Leaderboard
    md_lines.append("\n## 3. ترتيب جميع النماذج الـ 41 (بالأسماء العربية)")
    md_lines.append("| الترتيب | النموذج | الفئة | المباريات | الانتصارات | نسبة الفوز | ELO |")
    md_lines.append("|---:|---|---|---:|---:|:---:|---:|")
    roster_rows = []
    for spec in ROSTER_40:
        rec = app.store.roster.get(spec.model_id, {})
        m = int(rec.get("matches", 0))
        w = int(rec.get("wins", 0))
        wr = (100.0 * w / m) if m > 0 else 0.0
        elo = float(rec.get("elo", 1000.0))
        roster_rows.append({
            "model_id": spec.model_id,
            "arabic_name": arabic_model_name(spec.model_id),
            "category": CATEGORY_AR.get(spec.category, spec.category),
            "matches": m, "wins": w, "win_rate": round(wr, 2), "elo": round(elo, 1),
        })
    roster_rows.sort(key=lambda x: (x["win_rate"], x["elo"]), reverse=True)
    for idx_r, rr in enumerate(roster_rows, 1):
        md_lines.append(f"| {idx_r} | {rr['arabic_name']} | {rr['category']} | {rr['matches']:,} | {rr['wins']:,} | **{rr['win_rate']:.1f}%** | {rr['elo']:.0f} |")

    # Top 15 OP & Top 15 Weakest Cards with Arabic skill explanations
    card_rows = []
    for cid_s, st in app.store.card_stats.items():
        pl = int(st.get("played", 0))
        if pl >= 30:
            cid = int(cid_s)
            c = app.db.cards.get(cid)
            if c:
                wr = 100.0 * int(st.get("played_wins", 0)) / pl
                card_rows.append((wr, pl, cid, c.name, explain_card_arabic(c)))
    card_rows.sort(key=lambda x: x[0], reverse=True)

    md_lines.append("\n## 4. أقوى 15 بطاقة (مرشحة للمراجعة/الإضعاف) مع الشرح العربي للقدرات")
    md_lines.append("| ID | البطاقة | الفوز % | مرات اللعب | الشرح العربي للقدرات والمهارات |")
    md_lines.append("|---:|---|:---:|---:|---|")
    for wr, pl, cid, nm, exp in card_rows[:15]:
        md_lines.append(f"| {cid} | {nm} | **{wr:.1f}%** | {pl:,} | {exp} |")

    md_lines.append("\n## 5. أضعف 15 بطاقة (مرشحة للتقوية) مع الشرح العربي للقدرات")
    md_lines.append("| ID | البطاقة | الفوز % | مرات اللعب | الشرح العربي للقدرات والمهارات |")
    md_lines.append("|---:|---|:---:|---:|---|")
    for wr, pl, cid, nm, exp in list(reversed(card_rows[-15:])):
        md_lines.append(f"| {cid} | {nm} | **{wr:.1f}%** | {pl:,} | {exp} |")

    md_text = "\n".join(md_lines)
    with open(md_path, "w", encoding="utf-8") as f:
        f.write(md_text)

    json_payload = {
        "exported_at": ts_hr,
        "db_version": app.db.version,
        "source": app.active_source_label,
        "overall_health_score": overall,
        "pillars": pillars_json,
        "roster_41": roster_rows,
        "top_15_cards": [{"id": cid, "name": nm, "wr": round(wr, 2), "played": pl, "arabic_desc": exp} for wr, pl, cid, nm, exp in card_rows[:15]],
        "bottom_15_cards": [{"id": cid, "name": nm, "wr": round(wr, 2), "played": pl, "arabic_desc": exp} for wr, pl, cid, nm, exp in list(reversed(card_rows[-15:]))],
    }
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(json_payload, f, indent=2, ensure_ascii=False)

    # Excel-friendly UTF-8-BOM CSV of all 13 Pillars / 188 Indicators + 41 Models
    with open(csv_path, "w", encoding="utf-8-sig", newline="") as cf:
        w = csv.writer(cf)
        w.writerow(["المعيار #", "اسم المعيار", "تقييم المعيار %", "المؤشر #", "اسم المؤشر", "النتيجة الحالية", "المثالي", "الجيد", "التقييم", "التشخيص التلقائي"])
        for p in pillars_json:
            sc_v = p["score"] if p["score"] is not None else ""
            for r in p["indicators"]:
                w.writerow([p["pillar_num"], p["pillar_name"], sc_v, r["num"], r["metric"], r["actual"], r["ideal"], r["good"], r["status"], r["diagnosis"]])
        w.writerow([])
        w.writerow(["الترتيب", "الاسم العربي للنموذج", "المعرّف", "الفئة", "المباريات", "الانتصارات", "نسبة الفوز %", "ELO"])
        for idx_r, rr in enumerate(roster_rows, 1):
            w.writerow([idx_r, rr["arabic_name"], rr["model_id"], rr["category"], rr["matches"], rr["wins"], rr["win_rate"], rr["elo"]])

    return md_path, json_path, csv_path, md_text

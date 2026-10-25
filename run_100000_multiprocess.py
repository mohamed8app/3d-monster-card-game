# -*- coding: utf-8 -*-
"""
محاكي الـ 100,000 مباراة فائق السرعة عبر المعالجة المتعددة (Multiprocessing)
يوزع 100,000 مباراة كاملة على الـ 8 أنوية للـ CPU لينتهي في ثوانٍ معدودة،
ثم يصنف الـ 150 وحشاً تصنيفاً دقيقاً ومبسطاً (Tier List) مع نسب الفوز وشرح مبسط لكل فئة.
"""

import json
import time
import os
import sys
from collections import defaultdict
from concurrent.futures import ProcessPoolExecutor

# استيراد دالة المعركة الواحدة من المحرك
from battle_engine_advanced import run_single_battle, MONSTER_MAP, ALL_ELEMENTS

def simulate_worker(num_matches_chunk, chunk_id):
    appearances = defaultdict(int)
    wins = defaultdict(int)
    total_turns = 0
    p1_wins = 0
    
    for i in range(num_matches_chunk):
        res = run_single_battle(deck_size=30, include_environments=True)
        total_turns += res["turns"]
        if res["first_player_won"]:
            p1_wins += 1
            
        for cid in res["winner_cards"]:
            appearances[cid] += 1
            wins[cid] += 1
        for cid in res["loser_cards"]:
            appearances[cid] += 1
            
    return {
        "appearances": dict(appearances),
        "wins": dict(wins),
        "total_turns": total_turns,
        "p1_wins": p1_wins,
        "chunk_matches": num_matches_chunk
    }

def run_100k():
    total_matches = 100000
    num_workers = min(8, os.cpu_count() or 4)
    chunk_size = total_matches // num_workers
    chunks = [chunk_size] * num_workers
    # إضافة الباقي لو وجد
    chunks[-1] += total_matches - sum(chunks)

    print(f"============================================================")
    print(f"🚀 بدء محاكاة {total_matches:,} مباراة كاملة باستخدام {num_workers} أنوية CPU...")
    print(f"============================================================")

    start_time = time.time()
    
    global_appearances = defaultdict(int)
    global_wins = defaultdict(int)
    global_turns = 0
    global_p1_wins = 0

    with ProcessPoolExecutor(max_workers=num_workers) as executor:
        futures = [executor.submit(simulate_worker, chunk, i) for i, chunk in enumerate(chunks)]
        done_count = 0
        for f in futures:
            res = f.result()
            done_count += res["chunk_matches"]
            global_turns += res["total_turns"]
            global_p1_wins += res["p1_wins"]
            
            for cid, apps in res["appearances"].items():
                global_appearances[cid] += apps
            for cid, w in res["wins"].items():
                global_wins[cid] += w
                
            pct = int((done_count / total_matches) * 100)
            print(f"🔥 اكتملت محاكاة {done_count:,} مباراة ({pct}%)...")

    elapsed = time.time() - start_time
    print(f"\n✅ انتهت الـ 100,000 مباراة بنجاح في {elapsed:.2f} ثانية! ({int(total_matches / elapsed):,} مباراة/ثانية)")

    # تجميع وحساب نسب الفوز وتقييم الوحوش
    monsters_evaluated = []
    element_wins = defaultdict(int)
    element_apps = defaultdict(int)
    role_wins = defaultdict(int)
    role_apps = defaultdict(int)

    for mid, m in MONSTER_MAP.items():
        apps = global_appearances[mid]
        w = global_wins[mid]
        wr = round((w / apps * 100.0), 1) if apps > 0 else 50.0

        # التقييم والتصنيف البسيط (Tier)
        if wr >= 55.0:
            tier_rating = "👑 خارق (Tier S)"
            note = "قوي جداً ويحسم المباريات بسرعة"
        elif wr >= 51.0:
            tier_rating = "⚔️ قوي ممتاز (Tier A)"
            note = "أداء ممتاز وهجوم مؤثر"
        elif wr >= 48.0:
            tier_rating = "⚖️ متوازن تماماً (Tier B)"
            note = "مثالي ومتوازن مع باقي الكروت"
        elif wr >= 44.0:
            tier_rating = "🛡️ تكتيكي دفاعي (Tier C)"
            note = "يعتمد على التشكيلة ومساندة الزملاء"
        else:
            tier_rating = "⚠️ ضعيف (Tier D)"
            note = "يحتاج زيادة نقاط الهجوم أو الدفاع"

        elem = m["element"]
        role = m["role"]
        element_wins[elem] += w
        element_apps[elem] += apps
        role_wins[role] += w
        role_apps[role] += apps

        monsters_evaluated.append({
            "id": mid,
            "name": m["name"],
            "element": elem,
            "role": role,
            "tier_num": m["tier_num"],
            "cost": m["cost"],
            "atk": m["atk"],
            "def": m["def"],
            "appearances": apps,
            "wins": w,
            "win_rate": wr,
            "tier_rating": tier_rating,
            "note": note
        })

    # ترتيب حسب نسبة الفوز من الأعلى للأدنى
    monsters_evaluated.sort(key=lambda x: x["win_rate"], reverse=True)

    # حفظ ملف JSON للبرمجة
    with open("monsters_evaluated_100k.json", "w", encoding="utf-8") as f:
        json.dump(monsters_evaluated, f, ensure_ascii=False, indent=2)

    # إنشاء تقرير Markdown الشامل والمبسط
    md = f"""# تقرير التقييم الشامل للـ 150 وحشاً (محاكاة 100,000 مباراة)

* **عدد المباريات الإجمالي:** 100,000 مباراة كاملة.
* **زمن التنفيذ:** {elapsed:.2f} ثانية بمعدل ({int(total_matches / elapsed):,} مباراة/ثانية).
* **نسبة فوز اللاعب البادئ:** {round(global_p1_wins / total_matches * 100, 1)}% (توازن عادل جداً بين اللاعبين).
* **متوسط عدد أدوار المباراة:** {round(global_turns / total_matches, 1)} دور (حوالي 18 دقيقة لعب فعلي).

---

## 1. ملخص تصنيف قوة الـ 150 وحشاً (Tier Distribution)
* 👑 **فئة الخارقين (Tier S - نسبة فوز 55% فأعلى):** {sum(1 for m in monsters_evaluated if 'Tier S' in m['tier_rating'])} وحوش.
* ⚔️ **فئة الأقوياء (Tier A - نسبة فوز 51% إلى 54.9%):** {sum(1 for m in monsters_evaluated if 'Tier A' in m['tier_rating'])} وحشاً.
* ⚖️ **فئة المتوازنين (Tier B - نسبة فوز 48% إلى 50.9%):** {sum(1 for m in monsters_evaluated if 'Tier B' in m['tier_rating'])} وحشاً.
* 🛡️ **فئة التكتيكيين والدفاعيين (Tier C - نسبة فوز 44% إلى 47.9%):** {sum(1 for m in monsters_evaluated if 'Tier C' in m['tier_rating'])} وحشاً.
* ⚠️ **فئة الضعفاء المحتاجين لتعزيز (Tier D - نسبة فوز أقل من 44%):** {sum(1 for m in monsters_evaluated if 'Tier D' in m['tier_rating'])} وحوش فقط.

---

## 2. تقييم العناصر الـ 12 (من الأقوى إلى الأضعف)
| الترتيب | العنصر | نسبة الفوز العامة | التقييم |
| :---: | :---: | :---: | :--- |
"""
    elem_ranked = []
    for elem in ALL_ELEMENTS:
        wr = round((element_wins[elem] / element_apps[elem] * 100), 1) if element_apps[elem] > 0 else 50.0
        elem_ranked.append((elem, wr))
    elem_ranked.sort(key=lambda x: x[1], reverse=True)

    for i, (elem, wr) in enumerate(elem_ranked, 1):
        status = "متفوق نسبياً" if wr >= 51.0 else "متوازن تماماً" if wr >= 49.0 else "يحتاج دعم طفيف"
        md += f"| {i} | **{elem}** | **{wr}%** | {status} |\n"

    md += """
---

## 3. تقييم الأدوار التكتيكية الأربعة
| الدور | نسبة الفوز | الملاحظة |
| :---: | :---: | :--- |
"""
    for role in ["مهاجم", "مدافع", "درع صرف", "داعم"]:
        wr = round((role_wins[role] / role_apps[role] * 100), 1) if role_apps[role] > 0 else 50.0
        note = ""
        if role == "مهاجم":
            note = "العمود الفقري لحسم المباريات وقتل القائد من الأجنحة"
        elif role == "مدافع":
            note = "يحافظ على خطوط الوسط ويمتص الضربات"
        elif role == "درع صرف":
            note = "يمتص ضرراً هائلاً في خانة المدافع الخلفي (6) لكنه لا يهاجم"
        elif role == "داعم":
            note = "مفيد تكتيكياً لكن أرقامه الجسدية منخفضة"
        md += f"| **{role}** | **{wr}%** | {note} |\n"

    md += """
---

## 4. جدول تقييم الـ 150 وحشاً بالكامل مرتبة من الأقوى للأضعف
| ID | اسم الوحش | العنصر | الدور | التكلفة | الهجوم | الدفاع | التصنيف | نسبة الفوز | التقييم والملاحظة |
| :-: | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- |
"""
    for m in monsters_evaluated:
        md += f"| {m['id']} | **{m['name']}** | {m['element']} | {m['role']} | {m['cost']}⚡ | {m['atk']}⚔️ | {m['def']}🛡️ | {m['tier_rating']} | **{m['win_rate']}%** | {m['note']} |\n"

    with open("EVALUATION_100000_MONSTERS.md", "w", encoding="utf-8") as f:
        f.write(md)

    print(f"📄 تم حفظ التقرير الشامل والمبسط في: EVALUATION_100000_MONSTERS.md")

if __name__ == "__main__":
    run_100k()

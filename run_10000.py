# -*- coding: utf-8 -*-
import json
import time
from battle_engine_advanced import run_large_scale_simulation

print("🚀 جاري محاكاة 10,000 مباراة كاملة مع كروت البيئة ورصد الحماس ونواقص الميكانزم...")
start = time.time()
res = run_large_scale_simulation(
    num_matches=10000,
    deck_size=30,
    include_environments=True,
    progress_callback=lambda cur, tot: print(f"تمت محاكاة {cur:,} / {tot:,} مباراة...") if cur % 2500 == 0 or cur == tot else None
)

print(f"\n✅ اكتملت المحاكاة في {res['elapsed_seconds']} ثانية!")
print(f"🎮 المباريات: {res['num_matches']:,}")
print(f"⚖️ نسبة فوز اللاعب البادئ: {res['first_player_win_rate']}%")
print(f"⏱️ متوسط عدد الأدوار: {res['avg_turns']} دور ({res['avg_match_minutes']} دقيقة مدة واقعية)")
print(f"⏱️ زمن الجولة المتوسط: {res['avg_turn_seconds']} ثانية")
print(f"🔥 أكثر دور اشتعلت فيه الساحة (Climax Turn): الدور {res['most_frequent_climax_turn']}")
print(f"☠️ نسبة الحسم بالإرهاق (Fatigue): {res['fatigue_death_rate']}%")
print(f"⚡ نسبة العودة من الخلف (Comeback): {res['comeback_rate']}%")
print(f"🧊 نسبة الجمود التكتيكي: {res['stagnation_rate']}%")
print(f"🌍 إجمالي كروت البيئة الملعوبة: {res['total_env_plays']:,}")

# حفظ التقرير الشامل في Markdown
file_path = "SIMULATION_10000_FULL_REPORT.md"
md_content = f"""# تقرير المحاكاة الكبرى الشامل ({res['num_matches']:,} مباراة)
* **تاريخ المحاكاة:** مكتملة في {res['elapsed_seconds']} ثانية بمعدل ({int(res['num_matches'] / res['elapsed_seconds']):,} مباراة/ثانية).
* **عدد المباريات المحاكاة:** {res['num_matches']:,} مباراة.
* **كروت البيئة:** مفعلة (12 بيئة بـ 3 مستويات ومصفوفة متناظرة).
* **نسبة فوز اللاعب البادئ:** {res['first_player_win_rate']}% (توازن مثالي).
* **متوسط عدد الأدوار:** {res['avg_turns']} دور (المدة التقديرية للعب البشري: {res['avg_match_minutes']} دقيقة).
* **زمن الجولة المتوسط:** {res['avg_turn_seconds']} ثانية.
* **دور الذروة والاشتعال (Peak Climax):** الدور {res['most_frequent_climax_turn']}.
* **نسبة الحسم بالإرهاق:** {res['fatigue_death_rate']}%.
* **نسبة العودة من الخلف (Comebacks):** {res['comeback_rate']}%.
* **نسبة الجمود التكتيكي (Stagnation):** {res['stagnation_rate']}%.

---

## 1. تشخيص ميكانزم اللعبة ونقاط التحسين والتوصيات
{chr(10).join(['* ' + r for r in res['recommendations']])}

---

## 2. توزيع الضرر التكتيكي
* **🛡️ ضرر امتصه المدافع الخلفي (Guardian):** {res['damage_distribution']['guardian_absorbed_pct']}%
* **🦅 ضرر الأجنحة المباشر للقائد (Wings Direct):** {res['damage_distribution']['wings_direct_pct']}%
* **🎯 ضرر الوسط المباشر للقائد (Center Direct):** {res['damage_distribution']['center_direct_pct']}%
* **🌍 إجمالي كروت البيئة الملعوبة:** {res['total_env_plays']:,}

---

## 3. منحنى الاشتعال لكل دور (Battle Heat by Turn)
| الدور | متوسط الضرر في الدور | متوسط وفيات الوحوش |
| :-: | :---: | :---: |
"""
for c in res["intensity_curve"][:35]:
    md_content += f"| الدور {c['turn']} | {c['avg_damage']}⚔️ | {c['avg_kills']}💀 |\n"

md_content += """
---

## 4. قائمة الـ 150 وحشاً ونسب الفوز الدقيقة في 10,000 مباراة
| ID | الاسم | العنصر | الدور | التكلفة | الهجوم | الدفاع | عدد الظهور | مرات الفوز | نسبة الفوز |
| :-: | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
"""
for m in res["monsters_stats"]:
    status = ""
    if m["win_rate"] >= 56.0:
        status = " 🔥 (قوي)"
    elif m["win_rate"] <= 44.0:
        status = " ❄️ (يحتاج تعزيز)"
    md_content += f"| {m['id']} | {m['name']} | {m['element']} | {m['role']} | {m['cost']}⚡ | {m['atk']}⚔️ | {m['def']}🛡️ | {m['appearances']:,} | {m['wins']:,} | **{m['win_rate']}%**{status} |\n"

with open(file_path, "w", encoding="utf-8") as f:
    f.write(md_content)

print(f"\n📄 تم حفظ التقرير الشامل بنجاح في: {file_path}")

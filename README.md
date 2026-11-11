# ⚔️ Tactical Monster Card Battler (لعبة كروت الوحوش التكتيكية)

مشروع لعبة كروت وحوش استراتيجية متكاملة مبنية بمحرك **Unity (C#)**، تعتمد على فن الكروت ثنائي الأبعاد (2D Art) الفاخر مع ساحة معركة حية ومؤثرات قتالية غامرة (Dynamic 3D Battlefield & VFX)، بنظام تشكيل الـ 7 خانات، 1,000 نقطة حياة (HP)، 12 عنصراً، 150 وحشاً متوازناً رياضياً، ومحاكي معارك ذكي عبر 100,000 جولة.

---

## 🌟 الميزات الجوهرية وميكانيكا اللعبة (Core Game Mechanics)

1. **صحة القائد (Leader HP):**
   * يبدأ كل لاعب بـ **1,000 HP**، مع زمن مباراة مستهدف بين **12 و 18 دقيقة**.
2. **التشكيل الميداني للـ 7 خانات (7-Slot Formation):**
   * **الخط الأمامي (Frontline - 6 خانات):** جناحان يسار (0، 1) • وسط مهاجم (2، 3) • جناحان يمين (4، 5).
   * **الخط الخلفي (Backline - خانة واحدة):** **المدافع الخلفي بالوسط (خانة 6)** لحماية القائد من هجمات الوسط.
3. **نظام الموارد المزدوج (Mana vs Power):**
   * **سقف المانا (Mana Capacity):** يبدأ بـ 1 ويتصاعد حتى 8.
   * **نقاط الباور (Spendable Power):** تتجدد في بداية كل دور لتساوي سقف المانا وتُستهلك لاستدعاء الوحوش.
4. **ميكانيكا التضحية (Sacrifice Mechanic):**
   * التضحية بكارت واحد من اليد في كل دور (اختياري) تمنح اللاعب: **(+1 سقف مانا دائم و +1 باور فوري)**.
5. **قتال في اتجاه واحد (One-Way Tactical Combat):**
   * وحوش اللاعب صاحب الدور فقط هي التي تهاجم (لا يوجد رد فوري إلا بقدرات خاصة مثل الثورنز / عكس الضرر).
   * **هجوم الأجنحة:** تهاجم الجناح المقابل؛ وإذا كان فارغاً تضرب القائد (1,000 HP) مباشرة!
   * **هجوم الوسط:** يهاجم الوسط المقابل؛ وإذا كان فارغاً يصطدم أولاً بـ **المدافع الخلفي**؛ وعند تدميره فقط يصل للقائد.
6. **مصفوفة كروت البيئة الـ 12 المتناظرة:**
   * 12 كارت بيئة يغير ساحة المعركة بنظام 3 مستويات.
   * كل بيئة تفيد عنصرها فقط (+30% إلى +70%)، وتضعف 3 عناصر بالظبط (-20% إلى -50%)، ومحايدة مع 8.

---

## 🗂️ هيكل المشروع المنظّم (Project Architecture)

* **`UnityGame/`**: مشروع محرك Unity 6 الرئيسي (**C#**):
  * `Assets/Scripts/Core/`: منطق اللعبة (`GameManager.cs`, `PlayerState.cs`, `BoardSlot.cs`, `CombatSystem.cs`, `AIOpponent.cs`, `MonsterInstance.cs`).
  * `Assets/Scripts/Data/`: نماذج وقواعد البيانات (`CardData.cs`, `MonsterData.cs`, `EnvironmentData.cs`, `SpellDatabase.cs`).
  * `Assets/Scripts/UI/`: واجهات وساحة المعركة (`GameBootstrapper.cs`, `BattleUIManager.cs`, `BattlefieldAtmosphereEngine.cs`, `CardView.cs`, `SlotView.cs`, `DraggableCard.cs`, `BattleAnimations.cs`).
  * `Assets/Scripts/Utils/`: الأدوات وتحميل صور الكروت (`MonsterArtLoader.cs`, `ArabicFixer.cs`).
  * `Assets/Editor/`: أداة البناء التلقائي للمشهد والـ EXE (`BattleSceneBuilder.cs`).
  * `Assets/Resources/`: المصدر الوحيد المعتمد للأصول المدمجة (`cards_10000.json`, `monsters_150.json`, `CardsArt/Monsters/`, `Environments/` صور البيئات, إطارات الكروت, الصوتيات `Audio/`, الخطوط `Fonts/`).
  * `Assets/StreamingAssets/Environments/`: فيديوهات البيئات والمؤثرات الدورية فقط (`00.mp4`, `01.mp4`, `02.mp4`, `03.mp4`).
  * `Assets/Scenes/`: المشهد الرئيسي (`MainBattleScene.unity`).
* **`Game_Windows_Exe/`**: مجلد نسخة الويندوز الجاهزة للتشغيل (`MonsterCardGame.exe`).
* **`Data/`**: قاعدة بيانات الإكسل الرئيسية (`قاعدة_بيانات_الوحوش_والكروت.xlsx`).
* **`Tools/`**: أدوات البايثون للتوليد والمحاكاة والتقييم:
  * `simulator_100k_evaluator.py`: محاكي تقييم الـ 100,000 جولة وتحديث الإكسل وملفات JSON.
  * `generate_10k_monsters.py`, `generate_game_audio.py`, `generate_monsters.py`, `download_unity_hub.py`, `تثبيت_محرك_يونيتي.bat`.
* **`Docs/`**: وثائق التصميم والأرشيف (`GAME_MECHANICS_PLAN.md`, `MONSTERS_DATABASE_150.md`, `fire_dragon_art_prompt.txt`, `Legacy_Art/`, `Legacy_Web_Prototype/`, `VFX_Drafts/`).

---

## 🚀 الاختصارات السريعة من المجلد الرئيسي

* **تشغيل اللعبة:** اضغط مرتين على `العب_الان.bat`
* **بناء نسخة EXE جديدة:** اضغط مرتين على `run_build.bat`
* **فتح المشروع في Unity:** اضغط مرتين على `فتح_مشروع_اللعبه_في_يونيتي.bat`
* **تشغيل محاكي الـ 100 ألف جولة:** اضغط مرتين على `تقييم_القدرات_100_ألف_جولة.bat` (أو `python Tools/simulator_100k_evaluator.py`)

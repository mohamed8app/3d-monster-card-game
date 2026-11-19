# 🔊 دليل المؤثرات الصوتية وبرومبتات ElevenLabs (SFX Style Guide)

هذا الدليل هو المرجع الإلزامي الثابت لـ **Gemini** وأي نموذج ذكاء اصطناعي عند كتابة أو توليد برومبتات المؤثرات الصوتية (SFX) للعبة (`Deck Game`) باستخدام أداة **[ElevenLabs Sound Effects](https://elevenlabs.io/app/sound-effects/history)**.

---

## ⚡ 1. الشرط الإجباري الثابت في كل برومبت (Mandatory Core Suffix)

أي برومبت صوتي يتم إنشاؤه **يجب** أن ينتهي أو يتضمن هذا السطر الإلزامي نصاً وحرفاً دون أي تعديل أو إغفال:

```text
Crisp studio foley recording, clean binaural 3D stereo, zero background noise
```

> [!IMPORTANT]
> هذا الشرط إلزامي 100% في جميع طلبات المؤثرات الصوتية لضمان خلو الصوت تماماً من أي ضجيج خلفي (Hiss/Noise)، ولضمان نقاء استوديو احترافي وتجسيم ستيريو ثلاثي الأبعاد (Binaural 3D Stereo) ملائم لمحرك Unity.

---

## 🎯 2. هيكل كتابة البرومبت الصوتي (Prompt Structure)

يتكون البرومبت المعتمد لـ ElevenLabs من 3 أجزاء متناسقة:

```text
[وصف الحدث الصوتي والفعل الأساسي] + [الطبقات الفيزيائية والملمس وخامات الصوت] + [الشرط الإجباري]
```

### الصيغة الإنجليزية المعتمدة (Master English Formula):
```text
[Primary Action / Event], [Physical Layers, Impact & Materials, e.g., heavy sub-bass impact, resonant whoosh, sizzling embers], Crisp studio foley recording, clean binaural 3D stereo, zero background noise
```

---

## 📂 3. تصنيفات المؤثرات الصوتية في المشروع والأمثلة

تتوزع المؤثرات في المشروع داخل مجلد `UnityGame/Assets/Resources/Audio/SFX/` وفق التصنيفات التالية:

### 1. أصوات الوحوش (Monsters - Attack, Roar, Impact, Death)
* **مثال (زئير تنين ناري):**
  > `Colossal fire dragon mighty deep guttural roar, intense sub-bass rumble with erupting fiery breath and crackling embers, Crisp studio foley recording, clean binaural 3D stereo, zero background noise`
* **مثال (ضربة وحش حجري/أرضي):**
  > `Massive stone golem heavy seismic punch impacting solid earth, shattering rock debris and deep bass thud, Crisp studio foley recording, clean binaural 3D stereo, zero background noise`

### 2. أصوات التعاويذ والسحر (Spells - Cast, Blast, Elemental Magic)
* **مثال (صاعقة كهربائية):**
  > `Crackling high-voltage lightning strike explosion, sharp electrical arc discharge followed by resonant thunder clap, Crisp studio foley recording, clean binaural 3D stereo, zero background noise`
* **مثال (سحر الجليد وتجميد):**
  > `Deep freezing crystalline ice frost explosion, sharp glass-like ice cracking and shivering glacial resonance, Crisp studio foley recording, clean binaural 3D stereo, zero background noise`

### 3. أصوات تفاعل الكروت والواجهة (Cards & UI Interactions)
* **مثال (استدعاء كارت - Summon):**
  > `Mystical ethereal energy burst, swift whoosh and heavy impactful card slam onto wooden tabletop with shimmering magic resonance, Crisp studio foley recording, clean binaural 3D stereo, zero background noise`
* **مثال (تضحية بكارت - Sacrifice):**
  > `Dark shadow soul absorption whoosh, subtle ghostly dissipation and deep vanishing low-frequency hum, Crisp studio foley recording, clean binaural 3D stereo, zero background noise`

### 4. أصوات البيئات والقادة (Environments & Leaders)
* **مثال (انفجار بركاني بيئي):**
  > `Distant volcanic eruption blast wave, heavy low rumbling magma bubble pop and fiery seismic shockwave, Crisp studio foley recording, clean binaural 3D stereo, zero background noise`

---

## 🚫 4. الممنوعات الصوتية (Audio Exclusions)
يُراعى دائماً في توليد الأصوات تجنب:
- ❌ أي موسيقى تصويرية أو نغمات لحنية في الخلفية (NO background music).
- ❌ أي أصوات بشرية أو كلام غير مقصود (NO speech, NO background talking).
- ❌ الصدى الزائد أو صدى الغرف غير الواقعي (NO muddy room reverb unless specifically requested).
- ❌ التشويش والضوضاء أو ضغط الصوت المنخفض (NO lo-fi, NO clipping, NO background static).

---

## ⏱️ 5. إرشادات المدة الزمنية على ElevenLabs (Duration Guide)
* **أصوات تفاعل الكروت (Draw / Hover / Select):** من `0.3` إلى `0.8` ثانية.
* **ضربات الكروت والهجمات (Attack / Hit):** من `0.8` إلى `1.5` ثانية.
* **التعاويذ واستدعاء الوحوش (Spells / Summons):** من `1.5` إلى `3.0` ثوانٍ.
* **زئير الوحوش والأحداث الكبرى (Roars / Ultimate Abilities):** من `2.5` إلى `4.0` ثوانٍ.

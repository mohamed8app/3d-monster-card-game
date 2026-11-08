"""
مولد المؤثرات الصوتية وأصوات البيئات للعبة كروت الوحوش
يقوم بتوليد ملفات صوتية WAV نقية مباشرة في مجلد المشروع في Unity
بدون الحاجة لأي مكتبات خارجية
"""

import math
import os
import random
import struct
import wave

SAMPLE_RATE = 44100

def create_wav(filename, samples):
    os.makedirs(os.path.dirname(filename), exist_ok=True)
    with wave.open(filename, 'w') as wav_file:
        n_channels = 1
        sampwidth = 2  # 16-bit
        n_frames = len(samples)
        comptype = "NONE"
        compname = "not compressed"
        wav_file.setparams((n_channels, sampwidth, SAMPLE_RATE, n_frames, comptype, compname))
        
        packed_data = bytearray()
        for s in samples:
            # Clamp to 16-bit signed int
            s = max(-1.0, min(1.0, s))
            val = int(s * 32767.0)
            packed_data.extend(struct.pack('<h', val))
        wav_file.writeframes(packed_data)

# --- مولدات مؤثرات تفاعل الكروت والقتال (SFX) ---

def gen_card_draw():
    # صوت انزلاق ورقي سريع وهادئ (Whoosh/Slide)
    duration = 0.22
    total_samples = int(SAMPLE_RATE * duration)
    samples = []
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        env = math.sin((t / duration) * math.pi)
        noise = random.uniform(-1, 1)
        freq = 300 + 1200 * (t / duration)
        tone = math.sin(2 * math.pi * freq * t)
        val = (noise * 0.4 + tone * 0.6) * env * 0.35
        samples.append(val)
    return samples

def gen_card_play():
    # طرقة قوية خشبية/صخرية لاستقرار الكارت بالخانة (Thud / Snap)
    duration = 0.28
    total_samples = int(SAMPLE_RATE * duration)
    samples = []
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        env = math.exp(-18 * t)
        # ضربة بيز مع كليك
        sub = math.sin(2 * math.pi * 95 * t)
        click = random.uniform(-1, 1) * math.exp(-60 * t)
        val = (sub * 0.75 + click * 0.4) * env * 0.8
        samples.append(val)
    return samples

def gen_card_sacrifice():
    # طاقة صاعدة سحرية مع وميض خافت (Ascending Mystic Chime)
    duration = 0.6
    total_samples = int(SAMPLE_RATE * duration)
    samples = []
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        env = (1.0 - math.exp(-8 * t)) * math.exp(-3.5 * t)
        f1 = 260 + 600 * (t / duration)
        f2 = 520 + 1200 * (t / duration)
        tone = math.sin(2 * math.pi * f1 * t) * 0.6 + math.sin(2 * math.pi * f2 * t) * 0.4
        val = tone * env * 0.5
        samples.append(val)
    return samples

def gen_card_attack():
    # اندفاع قتالي هوائي حاد (Swoosh / Swing)
    duration = 0.25
    total_samples = int(SAMPLE_RATE * duration)
    samples = []
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        env = math.sin((t / duration) * math.pi) ** 2
        f = 700 - 450 * (t / duration)
        noise = random.uniform(-1, 1) * 0.5
        tone = math.sin(2 * math.pi * f * t) * 0.5
        val = (noise + tone) * env * 0.7
        samples.append(val)
    return samples

def gen_card_hit():
    # صدمة وارتطام قتالي قوي (Impact Punch)
    duration = 0.35
    total_samples = int(SAMPLE_RATE * duration)
    samples = []
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        env = math.exp(-12 * t)
        f = 140 * math.exp(-10 * t) + 45
        sub = math.sin(2 * math.pi * f * t)
        crunch = random.uniform(-1, 1) * math.exp(-25 * t)
        val = (sub * 0.7 + crunch * 0.6) * env * 0.9
        samples.append(val)
    return samples

def gen_card_death():
    # تلاشي أو تفتت الكارت وانتقاله للمقبرة (Crumble / Vanish)
    duration = 0.55
    total_samples = int(SAMPLE_RATE * duration)
    samples = []
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        env = math.exp(-5 * t)
        f = 320 - 240 * (t / duration)
        sub = math.sin(2 * math.pi * max(20, f) * t) * 0.5
        dust = random.uniform(-1, 1) * 0.5 * math.sin(t * 40)
        val = (sub + dust) * env * 0.5
        samples.append(val)
    return samples

def gen_hp_damage():
    # نبضة هبوط صحة القائد (Heavy Bass Thud)
    duration = 0.45
    total_samples = int(SAMPLE_RATE * duration)
    samples = []
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        env = math.exp(-8 * t)
        f = 85 * math.exp(-6 * t) + 35
        sub = math.sin(2 * math.pi * f * t)
        val = sub * env * 0.85
        samples.append(val)
    return samples

def gen_turn_end():
    # جرس تكتيكي ينبه بنهاية الدور (Tactical Chime)
    duration = 0.7
    total_samples = int(SAMPLE_RATE * duration)
    samples = []
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        env = math.exp(-4.5 * t)
        chime = math.sin(2 * math.pi * 587.33 * t) * 0.5 + math.sin(2 * math.pi * 880.0 * t) * 0.35 + math.sin(2 * math.pi * 1174.66 * t) * 0.15
        val = chime * env * 0.6
        samples.append(val)
    return samples

# --- مولدات أصوات البيئات الـ 12 المستمرة (Seamless Ambience Loops) ---

def gen_ambience_loop(duration, synth_func):
    # حلقة صوتية ناعمة قابلة للتكرار (Seamless Loop)
    total_samples = int(SAMPLE_RATE * duration)
    raw = [synth_func(i / SAMPLE_RATE, duration) for i in range(total_samples)]
    # Crossfade أطراف العينة لتجنب أي طقطقة عند التكرار
    fade_len = int(SAMPLE_RATE * 0.3)
    for j in range(fade_len):
        fade_in = j / fade_len
        fade_out = 1.0 - fade_in
        end_idx = total_samples - fade_len + j
        blend = raw[j] * fade_in + raw[end_idx] * fade_out
        raw[j] = blend
        raw[end_idx] = blend
    return raw

def fire_synth(t, d):
    # خرير وهدير نار بركانية
    rumble = math.sin(2 * math.pi * 48 * t) * 0.4 + math.sin(2 * math.pi * 72 * t + 0.5) * 0.25
    crackle = random.uniform(-1, 1) if random.random() < 0.04 else 0.0
    return (rumble + crackle * 0.35) * 0.45

def water_synth(t, d):
    # خرير وتدفق مياه وطوفان
    flow1 = math.sin(2 * math.pi * (160 + 30 * math.sin(t * 1.5)) * t)
    flow2 = math.sin(2 * math.pi * (280 + 40 * math.cos(t * 1.1)) * t)
    surf = random.uniform(-0.15, 0.15) * math.sin(t * 2.0)
    return (flow1 * 0.3 + flow2 * 0.2 + surf) * 0.4

def ice_synth(t, d):
    # صفير رياح قطبية باردة
    wind = math.sin(2 * math.pi * (440 + 80 * math.sin(t * 0.8)) * t)
    hiss = random.uniform(-0.12, 0.12)
    return (wind * 0.35 + hiss) * 0.35

def earth_synth(t, d):
    # زلازل ودوي باطني عميق
    tremor = math.sin(2 * math.pi * 38 * t) * 0.5 + math.sin(2 * math.pi * 55 * t + 0.3) * 0.35
    grind = math.sin(2 * math.pi * 85 * t) * math.sin(t * 3.5) * 0.15
    return (tremor + grind) * 0.5

def electric_synth(t, d):
    # شحنات وطنين كهرومغناطيسي
    hum = math.sin(2 * math.pi * 60 * t) * 0.35 + math.sin(2 * math.pi * 120 * t) * 0.25
    buzz = math.sin(2 * math.pi * 240 * t) * 0.15 * (1 + math.sin(t * 15))
    spark = random.uniform(-1, 1) if random.random() < 0.015 else 0.0
    return (hum + buzz + spark * 0.3) * 0.38

def wind_synth(t, d):
    # أعاصير وهدير رياح متصاعد
    gust = math.sin(2 * math.pi * (220 + 90 * math.sin(t * 1.2)) * t)
    air = random.uniform(-0.25, 0.25) * (0.6 + 0.4 * math.sin(t * 1.8))
    return (gust * 0.25 + air) * 0.4

def forest_synth(t, d):
    # طبيعة غابية وهمسات حيوية هادئة
    drone = math.sin(2 * math.pi * 196 * t) * 0.25 + math.sin(2 * math.pi * 293.66 * t) * 0.15
    rustle = random.uniform(-0.08, 0.08) * (1.0 + 0.5 * math.sin(t * 2.5))
    return (drone + rustle) * 0.35

def light_synth(t, d):
    # رنين كورال أثيري وترانيم نورانية
    chord = (math.sin(2 * math.pi * 261.63 * t) * 0.35 +  # C4
             math.sin(2 * math.pi * 329.63 * t) * 0.30 +  # E4
             math.sin(2 * math.pi * 392.00 * t) * 0.25 +  # G4
             math.sin(2 * math.pi * 523.25 * t) * 0.15)   # C5
    shimmer = math.sin(t * 3.0) * 0.15
    return chord * (0.85 + shimmer) * 0.35

def dark_synth(t, d):
    # طنين فراغي عميق ومخيف
    sub = math.sin(2 * math.pi * 42 * t) * 0.55 + math.sin(2 * math.pi * 63 * t) * 0.3
    pulse = math.sin(t * 0.9) * 0.2
    return (sub * (0.8 + pulse)) * 0.45

def mecha_synth(t, d):
    # نبض صناعي وتروس تدور
    hum = math.sin(2 * math.pi * 110 * t) * 0.3 + math.sin(2 * math.pi * 220 * t) * 0.15
    rhythm = math.sin(2 * math.pi * 1.5 * t) ** 4 * 0.25
    return (hum + rhythm) * 0.4

def ghost_synth(t, d):
    # أطياف وهمسات شبحية أثيرية
    spectral = math.sin(2 * math.pi * (310 + 25 * math.sin(t * 0.5)) * t) * 0.35
    eerie = math.sin(2 * math.pi * (465 + 35 * math.cos(t * 0.7)) * t) * 0.25
    drift = random.uniform(-0.07, 0.07)
    return (spectral + eerie + drift) * 0.38

def arcane_synth(t, d):
    # ذبذبات بلورية وأوتار سحرية متلألئة
    crystal = (math.sin(2 * math.pi * 440 * t) * 0.3 +
               math.sin(2 * math.pi * 659.25 * t) * 0.25 +
               math.sin(2 * math.pi * 880 * t) * 0.2)
    mod = 0.8 + 0.2 * math.sin(t * 4.5)
    return crystal * mod * 0.35


def main():
    base_dir = r"d:\deck game\UnityGame\Assets\Resources\Audio"
    sfx_dir = os.path.join(base_dir, "SFX")
    amb_dir = os.path.join(base_dir, "Ambience")
    
    import sys
    sys.stdout.reconfigure(encoding='utf-8')
    print("Generating SFX...")
    create_wav(os.path.join(sfx_dir, "card_draw.wav"), gen_card_draw())
    create_wav(os.path.join(sfx_dir, "card_play.wav"), gen_card_play())
    create_wav(os.path.join(sfx_dir, "card_sacrifice.wav"), gen_card_sacrifice())
    create_wav(os.path.join(sfx_dir, "card_attack.wav"), gen_card_attack())
    create_wav(os.path.join(sfx_dir, "card_hit.wav"), gen_card_hit())
    create_wav(os.path.join(sfx_dir, "card_death.wav"), gen_card_death())
    create_wav(os.path.join(sfx_dir, "hp_damage.wav"), gen_hp_damage())
    create_wav(os.path.join(sfx_dir, "turn_end.wav"), gen_turn_end())
    
    print("Generating 12 environment ambience loops...")
    amb_duration = 5.0  # 5 ثواني لكل بيئة مع Seamless Loop
    
    amb_map = {
        "ambience_fire.wav": fire_synth,
        "ambience_water.wav": water_synth,
        "ambience_ice.wav": ice_synth,
        "ambience_earth.synth.wav": earth_synth,
        "ambience_electric.wav": electric_synth,
        "ambience_wind.wav": wind_synth,
        "ambience_forest.wav": forest_synth,
        "ambience_light.wav": light_synth,
        "ambience_dark.wav": dark_synth,
        "ambience_mecha.wav": mecha_synth,
        "ambience_ghost.wav": ghost_synth,
        "ambience_arcane.wav": arcane_synth,
    }
    
    for fname, func in amb_map.items():
        # fix naming
        clean_name = fname.replace(".synth", "")
        create_wav(os.path.join(amb_dir, clean_name), gen_ambience_loop(amb_duration, func))
        
    print(f"Generated 8 SFX and 12 Ambience audio files successfully in: {base_dir}")

if __name__ == "__main__":
    main()

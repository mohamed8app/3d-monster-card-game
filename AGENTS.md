# Guidelines & Rules for "Deck Game" Project

## 1. Official Language Rule (MANDATORY & ABSOLUTE) ⚠️
* **Primary Language:** ENGLISH ONLY. All user interfaces (UI), button labels, HUD elements, alerts, error messages, card names, descriptions, stats, modals, and notifications across the entire project (Unity Game, Python Tools, Scripts, and Windows Executable) must be in **English**.
* **No Arabic in Game UI:** Completely eliminate Arabic text from all game screens, buttons, logs, menus, and game assets. The game is an international AAA card game designed in English.
* **Deck & Opponent Naming:** Use clear English names (e.g., "Gora (Aggressive Combat AI)", "Navi (Tactical Control AI)", "Gora Core Settled Deck", "Fire Aggro Deck", "Water Control Deck", "Balanced Draft Deck", "Custom Deck").
* **Code & Commits:** All code, logs, comments, and identifiers must be written in English.

## 2. Architecture & Systems
* **Game Engine:** Unity (C#) targeting Standalone Windows x64.
* **AI Training & Simulation Lab:** Python (Arena_Lab / Tools) with PyTorch and Stable-Baselines3 (MaskablePPO).
* **Champion Models:** Gora (Aggressive) & Navi (Tactical) trained via pure self-play.
* **Deck Rules:** 50 cards per deck, max 2 copies per card.

## 3. Art & Visual Identity (Art Style Guide)
* For any card artwork, monster concept, spell illustration, or image generation prompt, strictly adhere to the guidelines and templates in [ART_STYLE_GUIDE.md](./ART_STYLE_GUIDE.md).
* **Monster Cards:** Living world-building context (no stiff studio poses); defensive cards must NOT hold swords/offensive weapons; avoid monochrome elemental cliches by using rich, varied artistic palettes.
* **Spell Cards:** Specifically crafted for the antique brown scroll frame (`card_scroll_spell.png`); strictly monochromatic (aged sepia, black ink, charcoal) depicting the spell mechanics directly or utilizing a rare esoteric sigil.

## 4. Sound Effects & Audio Generation (ElevenLabs SFX)
* For any sound effect (SFX) prompt generation targeting ElevenLabs or game audio, strictly follow [SFX_STYLE_GUIDE.md](./SFX_STYLE_GUIDE.md).
* **Mandatory Suffix:** Every sound prompt generated must include the exact phrase:  
  `Crisp studio foley recording, clean binaural 3D stereo, zero background noise`



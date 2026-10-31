# Credits

## Characters

This is an unofficial fan game. The original illustrations and character designs of 오망이 (Omangi), 쿤뭉치 (Kunmungchi),
모쿠 (Moku) and 명칠 (Myeong7) are the copyright of 명칠 (Myeong7): https://x.com/_7M_3

The 3D models in this project are fan-made interpretations of those designs, made for this game only.

## Candy images (Assets/Art/Hand/Candy)

Cut out from the source photos below. Backgrounds were removed and the candies were cropped and resized.

| Files | Source | License |
|---|---|---|
| positano.png, positano_scene.png | Olive Young product page: https://www.oliveyoung.co.kr/store/goods/getGoodsDetail.do?goodsNo=A000000192691 | Not specified by the source (prototype use) |
| taffy.png | "Colourful wrapped salt water taffy" by Lara604 from Seattle, WA, via Wikimedia Commons: https://commons.wikimedia.org/wiki/File:Colourful_wrapped_salt_water_taffy_(3747358050).jpg | CC BY 2.0 |
| sherbet_1.png - sherbet_4.png | "Sherbet lemons 1.jpg" by Mx. Granger, via Wikimedia Commons: https://commons.wikimedia.org/wiki/File:Sherbet_lemons_1.jpg | CC0 |

## Sound effects (Assets/Audio/Sfx)

Event sounds (*.wav: candy_*, combo_up, answer_wrong, visitor_shoo, blast_charge, sheet_blowoff, visitor_bounce, prop_pop, game_over) are synthesized in-house with a Python script (numpy). No outside source.

From the Interface Sounds pack by Kenney (https://kenney.nl/assets/interface-sounds), released under CC0 (Creative Commons Zero, public domain): answer_correct.ogg (confirmation_001), heart_lost.ogg (glass_001).

Footsteps (visitor_step_0 - visitor_step_9) are from the RPG Audio pack by Kenney (https://kenney.nl/assets/rpg-audio), released under CC0 (Creative Commons Zero, public domain).

## Fonts (Assets/Resources/Fonts)

Both fonts are under the SIL Open Font License 1.1; the license texts are in Assets/ThirdParty/FontLicenses. The files are subsets made by Tools/Fonts/subset.py (ASCII plus KS X 1001 Hangul for Galmuri11; ASCII only for the title font) to keep the WebGL build small.

| File | Source | License |
|---|---|---|
| Galmuri11.ttf | "Galmuri11" by Lee Minseo (quiple), https://github.com/quiple/galmuri | SIL OFL 1.1 |
| SpookyTitle.ttf | Subset of "Creepster" by Font Diner, Inc, https://fonts.google.com/specimen/Creepster. Renamed to SpookyTitle because the Reserved Font Name "Creepster" may not be used for a modified version. | SIL OFL 1.1 |

## Background music (Assets/Audio/Bgm)

halloween_ambient.wav: synthesized in-house with Tools/Audio/bgm_synth.py (numpy). No outside source.

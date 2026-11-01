# Patch notes

Detailed notes per build, newest first. The game itself only shows one-line highlights
(`Assets/Scripts/PatchNotes.cs`); keep both in step when a build is released, and keep the
version in Player Settings the same as the newest entry.

## 0.3.0 (2026-11-01)

Combo
- A counter under the score shows the current streak ("COMBO x5") from the second right answer in a row, in the same color as the score. Every step pops the text: 1.3x in 0.35 s, every 10th step 1.6x in 0.5 s, every 100th step 2.2x in 0.7 s (`ComboPop*` in `GameManager`).
- Time limit: after a right answer, the next visitor has to be answered within a limit of arriving at the door, or the combo ends (no heart is lost). The limit starts at 5 seconds and shrinks with the streak, slowly at first (about 4.6 s at 50, 3.9 s at 100, 2.7 s at 150), to a soft cap of 1.5 s at 200, then logarithmically (about 1.26 s at 300) down to a floor of 0.8 s (`ComboSeconds*`, `ComboLog*`). A solid gauge along the bottom edge shows the time left. The clock stands still while the next visitor walks in, while a menu is open and during the first-game hints.
- The hand and candy animations speed up with the streak: a quick lift over the first combos (about 2.3x at 15, 2.9x at 30), then a slow, accelerating climb (3.6x at 60, 4.8x at 100, 6.9x at 150) to 10x at 200 (`HandEarly*`, `HandMaxSpeed*`).
- Atmosphere (`ComboAtmosphere`): the yard gets more Halloween as the combo grows. Jack-o'-lanterns start at half brightness and glow up to 130% with a stronger flicker (half speed) at a 50 combo; the house windows go from nearly dark to lit, steeply at first, also by 50; the moon grows to 1.45x and turns red by 100; cobwebs show up at 8 / 20 / 40 / 70 and bats at 12 / 25 / 45 / 65 / 90 (the existing background props, hidden until then); glowing motes in orange, yellow, purple and green drift up (up to 14 a second); the music speeds up to 1.1x. It follows the combo smoothly and falls faster when it breaks. Works from the objects under `Environment` by name, so no scene rebuild is needed.

Difficulty
- The disguise ramp is stretched: everything is fully open only at a score of 200. Below 40 a disguise is one fake prop (from 20 now and then two) with no paint, paint bucket or cone nose. From 40: more elements and light paint (15%, 30%); paint bucket from 60; cone nose from 80; heavy paint (60%, 90%) from 100; ghost sheets from 60 (`VisitorQueue`, `ItemDef.StartScore`).
- Halloween costumes (never the answer) come earlier and freer: from a score of 4, up to four at once, chance 30% to 72%, full at 30.
- 6% of the visitors are fully plain (no disguise, costume or sheet) whatever the score is (`plainChance`).
- Variety: a visitor whose look shares 60% or more of its items with one of the last 3 is dressed again (up to 4 tries), so the line does not repeat itself. Plain visitors are not counted.

Palm blast
- It blows off the disguise props first; loose costumes only come off once no prop is left.
- Harder props come off more often: cone nose (3x), X glasses (2x), horn band (1.5x) against the others. The help fades with the combo and is gone at 100 (`ItemDef.DropWeight`, `BlastAssistStreak`).
- A knocked-down visitor lies and gets up 1.6x faster (about 1.0 s down in total instead of 1.3 s; `knockRecoverSpeed`).
- The cooldown after a ghost sheet is blown off: 0.3 s to 0.2 s.

Costumes and looks
- New costumes: pirate hat, pirate eye patch, hook, broom, lollipop, rib vest, medical eye patch, gold bikini, bunny suit and maid dress. Costumes that belong together (sets) are worn together more often: from the second costume on, 40% of the picks come from the ones that make a set with what is already worn.
- Metal reflects light: the gold bikini, the hook's metal and the sunglasses lenses are reflective. The scene had nothing for metal to reflect (no skybox), so `EnvironmentReflection` paints a small night-yard cubemap at load (purple sky, warm horizon, moon, lantern glow) and uses it as the scene reflection.

Screens and settings
- Title screen: an info button next to the version stamp opens the update history (one-line highlights per build, paged by version; works on touch).
- Dress-up: the props and costumes are listed per body part (head, eyes, face, neck, hand, back, body) and paged with arrows instead of one long list; the back button moved to the lower left so it does not overlap the list on low screens.
- Score screen: removed the per-game "Share to world ranking" checkbox. Whether a finished game is sent to the online board is decided only by the "Share to world ranking by default" setting (its hint text was corrected too).
- Records tab: the model row is fitted with a wider margin beside the left panel, so the rightmost model is no longer cut off. The porch frame is hidden while the model stage is shown.

Server
- Two new item bits (26 pirate hat, 27 hook) in `server/src/items.js` and `ItemTable.cs`: the server has to be deployed together with this client, or the look codes of games with these costumes are rejected.

## 0.2.0 (2026-11-01)

Gameplay
- Special guests Moku and Myeong7 sometimes join the line from score 20 and 40 on. Either answer (candy or away) is right for them.
- First game: the first two visitors are always a plain Kunmungchi then a plain Omangi, with control hints (touch: swipe hints). Disguises start from score 6, the first disguised Omangi at 8.
- Give up button in the settings during a game: ends it and counts the score reached so far.
- Dye levels changed to 15 / 30 / 60 / 90 percent.

Screens
- Title: the buttons are a carousel (A / D, arrows, wheel, drag or swipe to choose, click to confirm) with a PrimeTween slide; the option descriptions were removed. Version stamp at the bottom right.
- Intro before a game: Kunmungchi turns once with its name shown (can be skipped in the settings).
- Gallery: SPECIAL tab, "unlock all" button, zoom and pan around the model, view reset.
- Records: LOCAL / ONLINE ranking tabs, per-game looks, clear-ranking button, personal and world statistics.
- Score screen: the three looks that fooled you are shown as turnable models; online ranking entries show their mistaken looks too.
- Settings: "reset all progress" (the ranking is kept), skip intro, share to world ranking by default.

Online
- Leaderboard server (Node.js, Redis, fly.io) with result text version 2 (192 bits, 29-bit score, CRC-16), `/api/top` returns each entry's result, `/api/stats` returns world statistics.
- Client caches the board for 15 s; only games with a score of at least 1, three looks and initials are accepted.

Other
- README with screenshots, licenses (Apache 2.0, characters under CC BY-NC-SA 4.0), credits including PrimeTween.

## 0.1.0 (2026-10-31)

First release.

# Patch notes

Detailed notes per build, newest first. The game itself only shows one-line highlights
(`Assets/Scripts/PatchNotes.cs`); keep both in step when a build is released, and keep the
version in Player Settings the same as the newest entry.

## 0.3 (2026-11-01)

- Combo: the current streak of right answers is shown under the score ("COMBO x5") from the second one in a row, in the same color as the score.
- Combo speed: the hand and candy animations speed up evenly with the streak, from 0.7x without a combo to 10x at a 100 combo (`HandBaseSpeed`, `HandMaxSpeed`, `HandMaxSpeedStreak` in `GameManager`); the speed drops back when the combo ends.
- Combo time limit: after a right answer, the next visitor has to be answered within 4 seconds of arriving at the door, or the combo ends (no heart is lost). A solid gauge along the bottom edge shows the time left. The clock stands still while the next visitor walks in, while a menu is open and during the first-game hints.
- Title screen: an info button next to the version stamp opens the update history (one-line highlights per build, paged by version; works on touch).
- Score screen: removed the per-game "Share to world ranking" checkbox. Whether a finished game is sent to the online board is decided only by the "Share to world ranking by default" setting.
- Records tab: the model row is fitted with a wider margin beside the left panel, so the rightmost model is no longer cut off. The porch frame is hidden while the model stage is shown.

## 0.2 (2026-11-01)

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

## 0.1 (2026-10-31)

First release.

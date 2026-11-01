# Halloween 2026: Trick or Treat

**[Play it in your browser](https://nerd-omang.github.io/halloween2026/)** (desktop and mobile, no install, hosted on GitHub Pages)

A first-person Halloween fan game. Trick-or-treaters line up at your door. The gray ones are 쿤뭉치 (Kunmungchi):
give them candy. The red ones are 오망이 (Omangi), and they like to sneak in disguised: send them away. Three hearts,
endless, one point per right answer.

This is an unofficial fan game. The original illustrations and character designs of 오망이, 쿤뭉치, 모쿠 and 명칠
belong to 명칠 ([@_7M_3](https://x.com/_7M_3)). See [CREDITS.md](CREDITS.md).

Built with Unity 6000.3.2f1 (URP 17.3) and published as a WebGL build on GitHub Pages (desktop and mobile browsers).
The look is low poly plus a dither post-process rendered at a third of the screen resolution.

## How to play

| Action | Mouse / keyboard | Touch |
|---|---|---|
| Give candy | Left click | Swipe right |
| Send away | Right click | Swipe left |
| Palm blast | Middle click, wheel or Space | Tap or vertical swipe |

The palm blast knocks the visitor down, blows a ghost sheet off and shakes one loose prop or costume loose.
Use it to look underneath before you decide. Gray with fake horns is still gray: look at the fur, the eyes and the
markings. Esc (or the gear button) opens the settings.

When the third heart is gone, the game shows the three looks that fooled you, as models you can turn, zoom and move,
and what you did against who they really were. Scores can be saved to a local top 20 and, if you allow it in the
settings, sent to the online top 10.

Other screens: a gallery of every look you have met (including the special guests), a dress-up screen to try any
combination, a records screen with your personal statistics, and a settings screen (volumes, dither options,
online sharing).

## Special guests

Moku and Myeong7 sometimes join the line later in a run. Either answer is right for them.

## Project layout

| Path | Content |
|---|---|
| `Assets/Scripts` | Game code. `GameManager` (state flow), `VisitorQueue` (spawning and difficulty ramp), `ItemTable` / `Outfit` / `LookCode` (looks as item bits and the short result code), `LocalRanking`, `LocalStats`, `WorldBoard` (online client), screens and `UiKit`. |
| `Assets/Editor` | Scene, model, environment and WebGL build tooling. |
| `Assets/Models`, `Assets/Prefabs`, `Assets/Shaders` | Characters, environment props, fur dye and dither shaders. |
| `Tools/Blender` | Blender scripts that generate the visitor, Moku and Myeong7 models. |
| `server` | Leaderboard server (Node.js, Redis, fly.io). See `server/README.md` and `server/API.md`. |
| `Docs/MobileWeb.md` | WebGL and mobile build notes. |
| `CREDITS.md` | Sources and licenses of third-party assets. |

## Build and run

1. Open the project in Unity 6000.3.2f1.
2. `Tools > Visitors > Rebuild Prefabs`, `Tools > Moku > Rebuild Prefab` and the Myeong7 builder, whenever the Blender models changed.
3. `Fangame > Build Prototype Scene` generates `Assets/Scenes/Main.unity` (props, UI, audio wiring and queue).
4. Press Play, or `Fangame > Build WebGL` for the web build (output in `Build/WebGL`, settings in `Assets/Editor/WebBuildSettings.cs`).

The online ranking talks to the leaderboard server at `WorldBoard.BaseUrl`; if that is empty it stays off.
Without it the online tab shows that no server is set and nothing is ever sent.

## Branches

git-flow style: work happens on `feature/*` branches, `main` is for releases.

## License

Free to reuse, with one protected part: the characters.

- Code, shaders, environment, UI, audio and everything else made for the game: [Apache 2.0](LICENSE).
- Character models, textures and their generator scripts: [CC BY-NC-SA 4.0](LICENSE-CHARACTERS.md), non-commercial only.
- The character designs belong to 명칠; commercial use of the characters needs their permission.
- Third-party assets keep their own licenses ([CREDITS.md](CREDITS.md)).

## Credits

See [CREDITS.md](CREDITS.md).

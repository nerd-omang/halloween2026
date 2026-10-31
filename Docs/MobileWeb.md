# Mobile web build (WebGL)

Target: iOS Safari and Android Chrome (landscape), served from a plain static host (GitHub Pages later).
Nothing here has been run on a phone or in a full build yet; items marked **unverified** need a first build to confirm.

## Build

1. Open the project in Unity 6000.3.2f1.
2. `Fangame > Build Prototype Scene` (only when the scene needs regenerating).
3. `Fangame > Build WebGL` writes `Build/WebGL`. It first runs `WebBuildSettings.Apply`, so the settings below are
   always in place. `Fangame > Apply WebGL Build Settings` applies them without building (they are also applied once
   when the editor loads; the console logs when something changed).

The page is `Assets/WebGLTemplates/Halloween/index.html` (selected as `PROJECT:Halloween`). It has no external
requests: all CSS, JS and the favicon are inline.

Build settings live in `Assets/Editor/WebBuildSettings.cs`.

| Setting | Value | Why |
|---|---|---|
| Compression | Gzip + decompression fallback | GitHub Pages cannot add `Content-Encoding: gzip/br` to Unity's `.gz`/`.br` files, so the loader unpacks them in JS. Gzip over Brotli: Brotli is ~15-20% smaller but its JS decoder is bigger and slower on a phone's cold start. Works unchanged on any static host, and on hosts that do send the header. |
| Name files as hashes | on | Redeploys cannot be served from a stale cache. |
| Data caching | on | Repeat visits skip the data download (IndexedDB; silently skipped in private tabs). |
| Exception support | None | Smaller and faster wasm. An uncaught C# exception aborts instead of being catchable, so keep `try/catch` out of the game flow. Turn on `ExplicitlyThrownExceptionsOnly` temporarily to debug. |
| Debug symbols | Off | Release size. |
| Linker target | Wasm | asm.js is gone from iOS/Chrome. |
| Threads | off | Needs COOP/COEP headers, which Pages cannot send. |
| Graphics API | WebGL 2 only (OpenGLES3), no WebGPU | WebGPU is experimental, and the game needs nothing from it. |
| Initial / maximum memory | 96 MB / 256 MB | See below. |
| Memory growth | Geometric, step 0.2, cap 32 MB | Rarely used because of the initial size. |
| Managed stripping | High, strip engine code on | No reflection or `SendMessage` in `Assets/Scripts`. Drop to Medium if a runtime `MissingMethod` appears (**unverified**). |
| IL2CPP code generation | Optimize size | Smaller wasm, faster compile on the phone. |
| Texture compression | ASTC, DXT | See below. |

Not set by script (no stable API): in the Build Profile for Web set **Code Optimization = Disk Size** if you want the
smallest wasm. Optional.

### Memory numbers

iOS Safari kills a tab somewhere around 1.2-1.5 GB on recent phones and a few hundred MB on old ones; the wasm heap,
decoded textures, GPU resources and the JS side share that. The game's content is tiny (see the audit), so the heap is
mostly Unity engine + URP. Reallocating a growing heap is the real iOS risk (old and new buffer exist at the same time),
so the heap starts big enough (96 MB) to avoid growing in normal play and is capped at 256 MB. Check in the first real
run (Unity's `Profiler`, or Safari's Timelines > Memory): if the heap never exceeds ~70 MB, lower initial to 64; if the
game aborts with "Cannot enlarge memory", raise maximum to 384. **Unverified.**

### Texture compression

The only compressible textures are ~103 hand-animation sprites (mostly 256x256, ~25 MB as RGBA32). ASTC 6x6 is ~4
bits per pixel (about 3 MB for all); DXT5 ~6 MB. ASTC covers every iOS device since A8 and nearly all Android GPUs; DXT
covers desktop browsers. ETC2 is omitted because a third copy of every texture is not worth the few old Android phones
it would cover. If a GPU supports neither, the textures end up uncompressed (~25 MB), which still fits. Whether the
build ships both formats or picks one, and the exact size effect, depend on the Unity version: compare
`Build/WebGL/Build/*.data.gz` before and after in the first build (**unverified**). If the data file is larger than
expected, drop DXT (desktop then falls back to uncompressed).

## Asset audit (size and memory)

Project is small: Assets total ~7 MB on disk, of which:

| Item | Size on disk | Build / memory notes |
|---|---|---|
| `Audio/Bgm/halloween_ambient.wav` | 3.1 MB | Import is already mono, 22.05 kHz, Vorbis q0.4, Compressed In Memory, no preload (`ConfigureMusicImport`). Ships as roughly 150-300 KB Vorbis (estimate). The biggest line item in the repo, a small one in the build. |
| `Audio/Sfx/*.wav` | ~0.4 MB | Mono, 22.05 kHz, Vorbis, Decompress On Load (`ConfigureSfxImport`). Short clips, tens of KB in memory each. |
| `Art/Hand/**` (103 PNG) | 2.3 MB | Sprites, no mipmaps, max size 256, compressed. Largest texture memory consumer, ~3-6 MB compressed on GPU. |
| `Resources/Fonts/Galmuri11.ttf` | 343 KB | Whole TTF ships (legacy `Font`, `includeFontData`). Gzips to roughly 200 KB. |
| `Resources/Fonts/SpookyTitle.ttf` | 31 KB | Fine. |
| FBX (`Visitors`, `Env_*`) | ~0.4 MB | No animation, no blend shapes, not readable. Mesh compression is off; gains would be a few tens of KB, so left alone. |
| `Textures/BlueNoise128.png` | 45 KB | Default platform is uncompressed, which is right for a noise lookup (compression would corrupt the dither). |
| Shaders | 8 custom + URP | URP stripping is already on (unused variants, debug variants, post-processing variants). Variants are not the problem at this scale. |

No import settings were changed: the existing importers already do the sensible reductions.

Suggestions that would need your call:

- **Subset Galmuri11** to the glyphs the game actually shows (ASCII + a few Hangul if Korean returns). Could save
  ~200 KB gzipped, but the dynamic legacy Font needs the full glyph set if text can come from translators or players.
- **Canvas resolution.** The page caps `devicePixelRatio` at 2 (`MAX_DPR` in `index.html`). The game renders at 1/3 of
  the canvas size, so the pixel size of the dithered look changes with the device. A phone at DPR 3 versus a desktop
  window give different "pixel" sizes. If the look should be identical everywhere, derive render scale from screen height
  in game code (not part of this task).
- **Shader warm-up.** First use of a shader compiles on the phone and can hitch. Add a `ShaderVariantCollection` to
  Graphics Settings > Preloaded Shaders if hitches show up (**unverified**).
- **Brotli** instead of Gzip if you later deploy to a host that sends `Content-Encoding: br` (Netlify, Cloudflare Pages):
  then switch the format to Brotli and disable the fallback.

## Page behaviour (template)

- Canvas fills the screen at `100dvw x 100dvh`, `position: fixed`, no margins, no scrollbars.
- Viewport: `viewport-fit=cover`, no user scaling. Pinch (iOS `gesture*` events), double-tap zoom, pull-to-refresh,
  text selection and the long-press menu are blocked (`touch-action: none`, `overscroll-behavior: none`,
  `user-select: none`, `touchmove`/`contextmenu` prevented). `touchstart`/`touchend` are left alone so taps and the
  audio unlock still work.
- Loading bar driven by the loader's progress callback.
- Fullscreen button (top right, ~40 px) only where `requestFullscreen` or `webkitRequestFullscreen` exists. Hidden on
  iPhone Safari. If the game's touch UI uses the top right corner, move `#fs` in the CSS.
- Screen Wake Lock is requested on the first touch where supported (needs HTTPS or localhost).
- WebGL 2 missing: the page shows a message and does not start the loader. Load failures and out-of-memory aborts show a
  message on the page instead of an `alert`.
- The game must handle `Screen.safeArea` for the notch because the canvas covers it (`viewport-fit=cover`).

## Test on a phone

Serve the build folder with any static server. No special headers are needed (that is the point of the fallback).

```bash
python -m http.server 8080 --directory Build/WebGL --bind 0.0.0.0
```

- **Same Wi-Fi:** open `http://<pc-lan-ip>:8080` on the phone. Fullscreen works; Wake Lock does not (not a secure
  context). Allow the port in Windows Firewall.
- **HTTPS on the LAN** (for Wake Lock and a realistic secure context): install mkcert, then
  `mkcert -install && mkcert <pc-lan-ip> localhost` and
  `npx http-server Build/WebGL -S -C <cert>.pem -K <cert>-key.pem -p 8443`. The phone must trust mkcert's root CA
  (iOS: install the profile, then enable it under Settings > General > About > Certificate Trust Settings).
- **Android over USB (simplest, gives `localhost` = secure):** `adb reverse tcp:8080 tcp:8080`, open
  `http://localhost:8080` in Chrome on the phone.
- **Desktop Chrome device mode:** DevTools > Toggle device toolbar (Ctrl+Shift+M), pick a phone, landscape. Throttle CPU
  4x and Network to Fast 3G to feel the load. Touch is emulated; memory and GPU behaviour are not representative.
- **Android remote debugging:** phone Developer options > USB debugging, `chrome://inspect/#devices` on the PC, click
  Inspect on the tab. Console, Performance and Memory work as usual.
- **iOS Safari Web Inspector:** needs a Mac. iPhone Settings > Safari > Advanced > Web Inspector on; connect by cable;
  Mac Safari > Develop > (device) > the page. Timelines tab shows memory and frame times.

## Real device checklist

- [ ] Loads from a cold cache; time to first frame on mobile data and on Wi-Fi; total transferred size.
- [ ] Second load is faster (data cache) and a redeploy actually updates (hashed names).
- [ ] No "out of memory" on the oldest phone you have (iPhone with 3-4 GB, low-end Android); tab does not reload itself
      after a few minutes of play. Note the Safari memory figure.
- [ ] Frame rate over a full round, in the porch scene and in the dress-up/gallery screens; phone does not get hot fast.
- [ ] Audio plays after the first touch (iOS needs a gesture), BGM loops without gaps, SFX are not delayed, mute switch
      behaviour is acceptable, audio resumes after switching apps.
- [ ] Landscape only: rotate overlay appears in portrait and disappears on rotation; no layout jump when Safari's
      toolbars collapse (`dvh`).
- [ ] No page scroll, no pull-to-refresh, no pinch or double-tap zoom, no text selection or callout on long-press.
- [ ] Fullscreen: button shown on Android Chrome and iPad, hidden on iPhone; toggling works; game resizes.
- [ ] Notch / rounded corners: nothing important under the cutout (`Screen.safeArea`).
- [ ] Screen stays awake during play (HTTPS only).
- [ ] Switch to another tab/app and back: game resumes, no stuck input.
- [ ] Dither/pixel look is acceptable on a DPR 3 phone (see canvas resolution note).
- [ ] Browser with WebGL 2 disabled shows the message page (desktop Chrome flag `--disable-webgl2`).

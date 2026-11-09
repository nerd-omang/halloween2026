---
name: release
description: Build and deploy the WebGL game of this repo to GitHub Pages. Use when the user asks to prepare a release, build, deploy or publish a version (e.g. "0.3.3 빌드해줘", "배포해줘"), or asks what is left before a release.
---

# Release: build and deploy (halloween2026)

The site is a Unity WebGL build served from the `gh-pages` branch of `nerd-omang/halloween2026`
(https://nerd-omang.github.io/halloween2026/). Source work lives on `feature/*` branches; `main` is for releases.
Run shell steps in Git Bash. Reply in Korean.

## Who does what

- The user builds in the Unity editor (`Fangame > Build WebGL` applies the web settings first) into
  `Build/WebGL/<version>/` (one folder per version, with `index.html` and `Build/`). The editor stays open, so
  Claude cannot start a second Unity on the project (it exits at once). Never say a build is done from the
  user's word alone: check that `Build/WebGL/<version>/index.html` exists (the folder is created empty when the
  build starts; linking the wasm takes minutes). If it is not there, wait for it with a background
  `until [ -f ... ]` loop, do not poll.
- Claude prepares the release files, commits them and runs the deploy script.
- If the user closes the editor and asks Claude to build: `Unity.exe -batchmode -quit -projectPath <repo> -executeMethod PrototypeSceneBuilder.BuildWebGL`
  (editor 6000.3.2f1 under `C:\Program Files\Unity\Hub\Editor\`). It builds into `Build/WebGL` (not a version
  folder): move the result into `Build/WebGL/<version>/` afterwards.

## 1. Before the build (check, fix what is missing, commit)

For version X.Y.Z:
1. `Assets/Scripts/PatchNotes.cs`: a new entry at the top, `version = "X.Y.Z"`, release time (24-hour clock) in
   `date`, one short player-facing line per change, English and Korean. Shown on the title screen (i button).
2. `Docs/PatchNotes.md`: a `## X.Y.Z (date)` section on top with the technical details.
3. `bundleVersion` in Project Settings equals X.Y.Z. The user changes it in the editor
   (`Edit > Project Settings > Player > Version`); Unity writes `ProjectSettings/ProjectSettings.asset` by itself.
   There is no "Save Project" menu.
4. Generated assets are current: after changing glyph sets or the ramp, run `Tools > Generate ASCII Glyphs` in the
   editor (the editor must do it; the batch run needs the project closed).
5. README or docs that describe changed features still match.
6. Commit with the `nerd-omang` identity (see below). Stage files by name. Never stage `Assets/Scenes/Main.unity`
   (Unity re-saves it with thousands of reordered lines) or a material the editor touched by itself
   (`CostumeGoldMetal.mat`) unless the user says it is part of the release. The user once committed the scene
   with the version bump on purpose ("as Unity saved them"); only do that when asked.

## 2. Deploy

```bash
Tools/deploy-pages.sh X.Y.Z --dry-run     # every check, shows what would change, pushes nothing
DEPLOY_COMMIT_TRAILER='Co-Authored-By: ...' Tools/deploy-pages.sh X.Y.Z
```

`DEPLOY_COMMIT_TRAILER` carries the attribution line the session's instructions require for commits.
The script checks version agreement (bundleVersion, PatchNotes.cs, PatchNotes.md, the build's `productVersion`),
that the build is complete and newer than the last source commit, and the `nerd-omang` login; then replaces
`index.html` and `Build/` on `gh-pages` in a temporary worktree, commits `Deploy WebGL build X.Y.Z`, pushes
(one retry; a 14 MB upload sometimes ends with HTTP 408 while the remote stays unchanged), waits for the Pages
build and checks that the live page serves X.Y.Z. A user request to deploy is the approval to push `gh-pages`.
Report the result and the commit hash. If a check fails, tell the user what is missing; do not work around it.

## 3. After the deploy (ask, do not assume)

- `origin/feature/<branch>` is usually behind the local commits: ask whether to push it.
- Whether to merge into `main` or tag the release: ask (this repo's rule for it is not recorded).
- The leaderboard server (`server/`, fly.io, `release-server` branch) is deployed separately with `fly deploy`;
  a release that changes the result format needs it first (see Docs/PatchNotes.md of 0.3.0 and 0.3.1).

## Credentials and commits

- Repo-local git config is `nerd-omang <nerd-omang@users.noreply.github.com>`; keep it for every commit.
- `gh` has two accounts (`nerd-omang`, `haje-hwang`) and the active one is global:
  `gh auth switch --hostname github.com --user nerd-omang`. Say so when switching, since it affects other repos.
- Commit messages: short English subject, optional body, then the attribution lines from the session's instructions.
- Push only when asked (a deploy request covers `gh-pages` only).

#!/usr/bin/env bash
# Deploys a finished WebGL build to GitHub Pages (the gh-pages branch of nerd-omang/halloween2026).
#
# Usage:  Tools/deploy-pages.sh <version> [--dry-run] [--src <build dir>]
#   <version>   the release, e.g. 0.3.2; must equal bundleVersion and the build's productVersion
#   --dry-run   run every check and show what would change, push nothing
#   --src       the build folder; default Build/WebGL/<version> (the folder with index.html and Build/)
# Env:  DEPLOY_COMMIT_TRAILER  extra last lines for the commit message (e.g. a Co-Authored-By line)
#
# What it does: checks the build is complete and newer than the last source change, checks the nerd-omang
# account, replaces index.html and Build/ on gh-pages in a throw-away worktree, commits
# "Deploy WebGL build <version>", pushes (one retry: a big upload sometimes times out with HTTP 408),
# waits for the Pages build and checks the live productVersion. Run from Git Bash. Docs: .claude/skills/release.
set -euo pipefail

account=nerd-omang
repo_slug=nerd-omang/halloween2026
site=https://nerd-omang.github.io/halloween2026/

die() { echo "error: $*" >&2; exit 1; }

version=""; dry=0; src=""
while [ $# -gt 0 ]; do
  case "$1" in
    --dry-run) dry=1 ;;
    --src) shift; src="${1:?--src needs a folder}" ;;
    -*) die "unknown option $1" ;;
    *) [ -z "$version" ] || die "one version only"; version="$1" ;;
  esac
  shift
done
[ -n "$version" ] || die "usage: Tools/deploy-pages.sh <version> [--dry-run] [--src <build dir>]"

root=$(git rev-parse --show-toplevel)
cd "$root"
[ -n "$src" ] || src="$root/Build/WebGL/$version"
src=$(cd "$src" 2>/dev/null && pwd) || die "build folder not found: ${src:-Build/WebGL/$version}"

echo "== Checks"

# 1. Versions agree.
bundle=$(sed -n 's/^  bundleVersion: *//p' ProjectSettings/ProjectSettings.asset | tr -d '\r')
[ "$bundle" = "$version" ] || die "bundleVersion is '$bundle', not $version (Edit > Project Settings > Player > Version)"
grep -q "version = \"$version\"" Assets/Scripts/PatchNotes.cs || die "no $version entry in Assets/Scripts/PatchNotes.cs"
grep -q "^## $version " Docs/PatchNotes.md || die "no $version section in Docs/PatchNotes.md"

# 2. The build is complete: index.html, and exactly the files it loads.
[ -f "$src/index.html" ] || die "$src/index.html is missing; the build is not finished (Unity may still be linking)"
grep -q "productVersion: \"$version\"" "$src/index.html" || die "index.html is not a $version build (productVersion differs)"
refs=$(grep -o "Build/[0-9a-f]\{32\}\.[A-Za-z.]*" "$src/index.html" | sort -u)
[ "$(echo "$refs" | wc -l)" -eq 4 ] || die "index.html should load 4 files in Build/ (loader, framework, data, wasm); found: $(echo $refs)"
for f in $refs; do [ -s "$src/$f" ] || die "missing or empty: $src/$f"; done
[ "$(ls "$src/Build" | wc -l)" -eq 4 ] || die "Build/ holds files index.html does not load; rebuild into a clean folder"

# 3. The build is newer than the last change to the game itself.
last_src=$(git log -1 --format=%ct -- Assets ProjectSettings Packages)
built=$(stat -c %Y "$src/index.html")
[ "$built" -ge "$last_src" ] || die "the build ($(date -d @"$built" '+%m-%d %H:%M')) is older than the last source commit ($(date -d @"$last_src" '+%m-%d %H:%M')); rebuild"
if [ -n "$(git status --porcelain -- Assets ProjectSettings Packages | grep -v '^??')" ]; then
  echo "note: uncommitted changes under Assets/ProjectSettings; the build may contain them:"
  git status --short -- Assets ProjectSettings Packages | grep -v '^??' | sed 's/^/  /'
fi

# 4. Credentials.
login=$(gh api user --jq .login 2>/dev/null || true)
[ "$login" = "$account" ] || die "gh is logged in as '${login:-nobody}'; run: gh auth switch --hostname github.com --user $account"
[ "$(git config user.name)" = "$account" ] || die "git user.name is '$(git config user.name)', not $account"
echo "ok: version $version, build complete, account $account"

echo "== Worktree"
git fetch -q origin gh-pages
base=$(git rev-parse origin/gh-pages)
wt=$(mktemp -d)
cleanup() { cd "$root"; git worktree remove --force "$wt" >/dev/null 2>&1 || true; git worktree prune; }
trap cleanup EXIT
git worktree add -q --detach "$wt" "$base"
cd "$wt"
rm -rf Build index.html
mkdir Build
cp "$src/index.html" .
cp "$src"/Build/* Build/
git -c core.autocrlf=false add -A
if git diff --cached --quiet; then
  echo "nothing to deploy: gh-pages already has this build"
  exit 0
fi
git diff --cached --stat | cat

if [ "$dry" -eq 1 ]; then
  echo "dry run: nothing committed or pushed"
  exit 0
fi

msg="Deploy WebGL build $version"
[ -z "${DEPLOY_COMMIT_TRAILER:-}" ] || msg="$msg"$'\n\n'"$DEPLOY_COMMIT_TRAILER"
git -c core.autocrlf=false commit -q -m "$msg"
sha=$(git rev-parse HEAD)

echo "== Push"
for attempt in 1 2; do
  if git push origin HEAD:gh-pages; then break; fi
  [ "$attempt" -lt 2 ] || die "push failed twice; gh-pages is unchanged on the remote? check: git ls-remote origin gh-pages"
  [ "$(git ls-remote origin gh-pages | cut -f1)" = "$base" ] || die "remote gh-pages changed after the failed push; stop and look"
  echo "push failed (HTTP 408 on big uploads is common); retrying once"
done
cd "$root"
git fetch -q origin gh-pages
git branch -f gh-pages origin/gh-pages 2>/dev/null || true

echo "== Pages"
status=""
for _ in $(seq 1 30); do
  status=$(gh api "repos/$repo_slug/pages/builds/latest" --jq '.status + " " + .commit' 2>/dev/null || true)
  echo "  $status"
  case "$status" in "built $sha") break ;; "errored"*) die "Pages build failed: $status" ;; esac
  sleep 10
done
live=""
for _ in $(seq 1 12); do
  live=$(curl -fsS "$site?v=$sha" | grep -o 'productVersion: "[0-9.]*"' || true)
  [ "$live" = "productVersion: \"$version\"" ] && break
  sleep 10
done
[ "$live" = "productVersion: \"$version\"" ] || die "the live site does not serve $version yet (got: ${live:-nothing}); check $site"
echo "deployed: $site serves $version (gh-pages ${sha:0:7})"

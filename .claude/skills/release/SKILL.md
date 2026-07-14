---
name: release
description: Cut and publish a mod release end to end - set the version, promote the changelog's Unreleased section, test, build the zip and installer, commit, tag, push, publish the GitHub release, verify it against the installer and update-check contract, delete the staged artifacts. Use when asked to release version X.Y.Z. Not for rebuilding artifacts without publishing - that is build_release.ps1 / build-installer.ps1 directly.
argument-hint: <version>
---

Publish the release whose version is given as the argument. The argument is a bare three-part version like `1.2.0`; strip a leading `v` if one was typed. Work through the phases in order; each is a gate for the next, and everything before Phase 4 leaves nothing public, so a failure there just stops.

## Phase 0 - preconditions

- The version must match `\d+\.\d+\.\d+` exactly. This is a hard contract, not a style rule: the installer identifies the mod zip asset by the name pattern `WhirlingInWords-v<maj>.<min>.<patch>.zip` and parses that version with the semver crate (`installer/src/core/github.rs`, `paths.rs`), and the mod's launch update check parses the release tag with `System.Version` (`src/WhirlingInWords.Core/Updates/UpdateCheck.cs`). A two-part or suffixed version publishes a release the installer cannot consume, and because the installer always reads `releases/latest`, that breaks every new install immediately, not just the new version.
- The version must be strictly greater than the newest existing `v*` tag (semver order; no tags at all is fine). An equal or lower version makes the update check silently stop announcing to players on newer builds.
- On `main`, working tree clean, `main` not behind `origin/main` (`git fetch origin` and compare), and `gh auth status` succeeds.
- The game may stay running: everything here builds Release, which never deploys, so no game DLL is touched.

## Phase 1 - version and changelog

- Set `<Version>` in `Directory.Build.props` to the target (skip the edit if already set). This is the single version source: `build_release.ps1` names the zip from it, and the same value is compiled into `BuildVersion.Value`, which is what the running mod compares against the release tag.
- `CHANGELOG.md`: if a `## V<version>` section already exists with content, use it as is. Otherwise retitle the `## Unreleased` section to `## V<version>`. If there is no Unreleased content to promote, draft the section from `git log v<previous>..HEAD` in the existing changelog style - player-facing changes only, no internal refactor or tooling notes - and get the user's approval of the draft before continuing: this text becomes the published GitHub release notes verbatim.
- Leave a fresh empty `## Unreleased` heading at the top so future work has its standing slot.
- The heading format is `## V<version>` with a capital V. `create-release.ps1` extracts the notes by finding exactly that heading and fails on a missing or empty section.

## Phase 2 - test and build

- `dotnet test WhirlingInWords.slnx` must be green.
- `.\build_release.ps1` then `.\build-installer.ps1`, producing `releases\WhirlingInWords-v<version>.zip` and `releases\WhirlingInWordsInstaller.exe`.
- Build before committing or tagging, so a failed build pushes nothing. At this point the tree holds exactly the release edits, so the artifacts match the commit about to be made.

## Phase 3 - commit, tag, push

- One commit with the version and changelog edits (plus anything else this release session prepared), message `Release <version>`. If nothing changed (a re-run), skip the commit.
- `git tag v<version>`, then `git push origin main v<version>`.

## Phase 4 - publish

- `.\create-release.ps1 v<version>`. It re-verifies the tag exists locally and on origin, finds both artifacts, extracts the changelog section, and runs `gh release create` with the zip and installer attached.

## Phase 5 - verify and clean up

- Read back `gh api repos/rashadnaqeeb/WhirlingInWords/releases/latest` and confirm both consumer contracts on the real thing: `tag_name` is `v<version>` (what the update check announces from), and the assets are `WhirlingInWords-v<version>.zip` (what the installer's name pattern must match) and `WhirlingInWordsInstaller.exe`.
- Delete `releases\WhirlingInWords-v<version>.zip` and `releases\WhirlingInWordsInstaller.exe`. `create-release.ps1` only checks that the files exist, so an artifact left behind can be republished stale by a future run that skipped a build.

## Failure and re-run notes

- Every phase is re-runnable. If publish fails after the tag was pushed, fix the cause and re-run `create-release.ps1` - the tag and artifacts are still valid.
- If the release was created wrong, `gh release delete v<version>` removes the release and its assets but keeps the tag; re-run Phase 4. Delete and re-push the tag only if the tagged commit itself is wrong.
- Do not edit a published release's zip by re-uploading under the same name unless the version is bumped: installs in the wild key on the asset digest and version name, and a silently different artifact under an already-announced version is exactly the stale-data failure this mod exists to avoid.

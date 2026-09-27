# Contributing to ImmersiveX

Thanks for helping. This guide covers setting up the project, the rules every change follows, and how to get a change merged.

## Simplicity rules
ImmersiveX has to stay easy to start, work with, fork and understand. Every pull request is reviewed against these rules:

1. **One component to learn.** Users should only need `ImmersiveContent`. Everything else is optional.
2. **Safe defaults.** Features work with no setup: transparent, floating, grabbable and remembered where placed.
3. **Presets before parameters.** Add a `ContentProfile` preset before adding another inspector field.
4. **No platform code in core.** No vendor SDK types or calls, and no `#if UNITY_ANDROID`-style branches, in `Packages/com.immersivex.core`. Device specifics live in `Platforms/<Name>`.
5. **Readable over clever.**
   - Small classes with XML doc comments on public APIs.
   - No dependency-injection framework.
   - No reflection into vendor internals.
6. **Degrade, never crash.** If a device lacks a capability, pick a documented fallback and log it.

## Set up

### 1. Install the tools
- **Unity 6000.3.23f1** through Unity Hub.
  - For Meta Quest work, add **Android Build Support** with **Android SDK & NDK Tools** and **OpenJDK**.
  - For Apple platforms (from M7), add the **iOS** and **visionOS** modules and install Xcode.
- **Git.**
- **Python 3.** It's used by the repository checks.

### 2. Clone and open
1. Clone the repository.
2. In Unity Hub, choose **Add ▸ Add project from disk** and select the repository root.
3. Wait for the first import to finish.

### 3. Enable Unity Smart Merge (recommended)
Scenes, prefabs and assets are YAML. `.gitattributes` routes them to UnityYAMLMerge, but you have to register the tool once per clone.

On macOS:

```sh
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "'/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/Helpers/UnityYAMLMerge' merge -p %O %B %A %A"
git config merge.unityyamlmerge.recursive binary
```

On Windows, use `C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\Tools\UnityYAMLMerge.exe` as the tool path.
Without this, Git falls back to a plain text merge.

### 4. Large files and Git LFS
The repository doesn't use Git LFS, so forks don't pay LFS bandwidth. Keep sample assets small.

If your fork carries large source art, enable LFS there **before** adding the files:

```sh
git lfs install
git lfs track "*.fbx" "*.glb" "*.psd" "*.wav" "*.mp4"
git add .gitattributes
```

## Making a change
1. Create a branch from `main`. Name it after the milestone or area, e.g. `m1/session-flow` or `docs/quest-guide`.
2. Keep pull requests small and focused on one task.
3. Write commit messages in the [Conventional Commits](https://www.conventionalcommits.org/) style: `feat:`, `fix:`, `docs:`, `test:`, `chore:`, `refactor:`.
4. Add an entry under **Unreleased** in [CHANGELOG.md](CHANGELOG.md).
5. Open a pull request and fill in the template.

### Definition of Done
A feature is done when it has all of these:
- core implementation with capability fallbacks
- EditMode tests for its logic
- a PlayMode test in XR Simulation
- a device check recorded in `docs/validation/`
- inspector tooltips
- a docs page and a sample

### Code style
- `.editorconfig` defines formatting and naming. Rider, Visual Studio and VS Code apply it automatically.
- Namespaces: `ImmersiveX.*` for core, `ImmersiveX.Platforms.<Name>` for adapters.
- Put logic that can be tested without Unity objects in plain C# classes, and keep MonoBehaviours thin.
- Always commit `.meta` files together with their assets.

### Architecture decisions
If your change would surprise a new contributor or be expensive to reverse, add an ADR:
1. Copy `docs/adr/template.md`.
2. Take the next number.
3. Link it from `docs/adr/README.md`.

## Tests and checks
- **Repository checks** (no Unity needed): `python3 tools/ci/check_repo.py`. They check `.meta` pairing, JSON validity and that no generated folders are committed.
- **Unity tests:** open **Window ▸ General ▸ Test Runner**, send the `test editmode` automation command, or run from the command line (with the editor closed):

  ```sh
  "/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity" -batchmode -projectPath . \
    -runTests -testPlatform EditMode -testResults test-results/editmode.xml
  ```

## CI
GitHub Actions ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) runs on every pull request and on pushes to `main`:
- **Repository checks** always run.
- **Unity EditMode tests** run through [GameCI](https://game.ci) once the repository has these secrets: `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD`. GameCI's [activation guide](https://game.ci/docs/github/activation) explains how to get them. Until they exist, the job is skipped with a notice.

## Adding a platform
Copy `Platforms/_Template` and follow [docs/adding-a-platform.md](docs/adding-a-platform.md). Open a **Platform support request** issue first if you'd like to discuss the device.

## Driving the open editor (automation)
Scripts, CI helpers and coding agents can drive an open editor through `EditorAutomation`:
1. Write one command to `Library/ImmersiveX/Automation/command.tmp`, then rename it to `command.txt`, so the editor never reads a half-written command.
2. Read `result.txt` in the same folder. Play runs also write `play-log.txt` and `play.png`.

Commands:
- `refresh` · `resolve` (re-resolve packages) · `test editmode`
- `open <scene>` · `playmode xr-simulation|metaquest-simulator`
- `play <seconds>` (`0` keeps playing until `stop`) · `stop` · `capture`
- `invoke <panel action>`
- `media list` · `media [@<n>|@<name>] status|play|pause|toggle|seek <s>|mute|unmute|speaker|volume <0-1>|turn <deg>|move <x> <z>|height <m>|open <source> [format]` (ImmersiveX Media)
- `configure <platform>` · `build <platform>` · `buildrun <platform>`
- `scene` · `demo` (3.5D Xperience scene) · `baseline` · `menu <path>`

Every command runs the same code as the menus, so you can watch it happen.

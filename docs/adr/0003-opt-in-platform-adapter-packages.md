# 0003. Ship each platform as an opt-in package in `Platforms/`

- **Status:** Accepted
- **Date:** 2026-09-23

## Context
Installing every vendor SDK in one project is slow, bloats builds and causes conflicts.
Users also need to know the exact SDK versions each platform was tested with.

## Decision
Each platform adapter is a UPM package in `Platforms/<Name>/`, outside `Packages/`.
Its `package.json` pins the vendor packages it needs, and its `platform.json` lists everything else (editor modules, build target, accounts, device requirements).
The Platform Setup window adds the chosen adapter to `Packages/manifest.json` with a `file:` reference.

## Consequences
- Adding an adapter installs exactly its tested dependencies; platforms you don't pick are never downloaded or compiled.
- Other projects can use one adapter by Git URL: `https://github.com/OlafenwaMoses/ImmersiveX.git?path=/Platforms/MetaQuest`.
- `Platforms/_Template` is the starting point for new platforms.
- Every adapter's runtime assembly must declare `[assembly: AlwaysLinkAssembly]` and mark its adapter `[Preserve]`. Nothing references an adapter directly (it registers itself at start-up), so IL2CPP code stripping otherwise removes it from device builds. It still works in the editor, where there's no stripping. The first Quest device run failed this way (2026-09-26).

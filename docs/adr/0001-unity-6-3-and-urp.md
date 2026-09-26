# 0001. Use Unity 6000.3.23f1 and the Universal Render Pipeline

- **Status:** Accepted
- **Date:** 2026-09-23

## Context
ImmersiveX targets standalone headsets (mobile GPUs), phones and PC VR from one project.
The editor version decides which XR packages and platform modules are available.
Unity 6000.3.23f1 has already been proven on Meta Quest 3 in the VizionEnterprise-MixedReality project.

## Decision
Pin the project to **Unity 6000.3.23f1** and the **Universal Render Pipeline (URP) 17.3.0**.
`ImmersiveX ▸ Maintenance ▸ Apply Project Baseline` creates the URP assets with XR-friendly defaults:
HDR off, 4× MSAA, no depth/opaque copy textures, SRP Batcher on. It also sets linear colour and Input System-only input.

## Consequences
- One render pipeline covers every target; visionOS (PolySpatial) also requires URP.
- Upgrading Unity is a deliberate change: update `ProjectSettings/ProjectVersion.txt`, the package pins and every `platform.json`, then run CI.
- Content must use URP-compatible shaders.

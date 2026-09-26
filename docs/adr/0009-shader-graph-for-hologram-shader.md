# 0009. Author the hologram shader in Shader Graph

- **Status:** Accepted
- **Date:** 2026-09-23

## Context
Content is transparent by default (opacity 0.5, overridable per object). The shader has to run on URP everywhere, including visionOS, where PolySpatial converts Shader Graph to MaterialX but can't convert hand-written HLSL.

## Decision
Author `ImmersiveX/Hologram` in Shader Graph. It keeps the base map and colour, adds alpha and an optional rim, and uses a depth pre-pass so complex meshes don't sort against themselves.

## Consequences
- One shader works on every platform.
- Hand-written shader tricks are off the table for this shader; performance tuning is done through Shader Graph and URP settings.

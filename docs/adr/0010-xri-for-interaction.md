# 0010. Use the XR Interaction Toolkit for interaction in core

- **Status:** Accepted
- **Date:** 2026-09-23

## Context
Grab, poke, ray and UI interaction must work with hands, controllers, touch and gaze + pinch on every platform.
The Meta Interaction SDK used in VizionEnterprise only runs on Quest.

## Decision
Core uses **XR Interaction Toolkit 3.3.2** with **XR Hands 1.7.3**.
`ImmersiveContent` sets up XRI interactables automatically, and motion restrictions (F5) are a custom XRI grab transformer.

## Consequences
- One interaction model on every device; XRI's Interaction Simulator sample supports editor testing.
- Meta Interaction SDK features (for example hand-grab poses) aren't available in core; they could be a Quest-only extra later.

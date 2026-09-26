# 0002. Build the core on Unity's vendor-neutral XR stack

- **Status:** Accepted
- **Date:** 2026-09-23

## Context
Every feature (see-through, room mapping, anchors, interaction) exists on many devices under different vendor APIs.
Writing each feature once per vendor would multiply the code and the bugs.

## Decision
`com.immersivex.core` depends only on Unity's cross-platform XR packages:
AR Foundation 6.3.5 (session, planes, bounding boxes, meshes, anchors, camera), XR Interaction Toolkit 3.3.2,
XR Hands 1.7.3, XR Core Utilities 2.6.0 and XR Plug-in Management 4.6.1. Vendor SDKs are referenced only by platform adapters.

## Consequences
- Features are written once; AR Foundation providers exist for Quest, visionOS, ARKit, ARCore and Android XR.
- Where AR Foundation has no API, the gap is filled through the platform contract, never by calling a vendor SDK from core.
- AR Foundation's editor XR Simulation lets most flows run without a headset (see spike S4).

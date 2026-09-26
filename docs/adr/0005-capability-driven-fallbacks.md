# 0005. Choose behaviour from platform capabilities, with fallbacks

- **Status:** Accepted
- **Date:** 2026-09-23

## Context
Devices differ: some have video passthrough, some optical see-through, some none; some persist anchors, some don't.
An app built with ImmersiveX must still run everywhere.

## Decision
Each adapter declares a `PlatformCapabilities` value (see-through type, room data, anchor persistence, input types, boundary control, and so on).
Features read it and pick a strategy. If a capability is missing, the feature uses a documented fallback and logs which one it chose.

## Consequences
- No feature crashes because a device lacks something; the fallback is visible in logs and in the Platform Setup report.
- Fallbacks must be tested. The editor's XR Simulation doesn't persist anchors, so Play mode exercises the room-relative fallback (spike S4).
- New capabilities must come with a default for platforms that don't declare them.

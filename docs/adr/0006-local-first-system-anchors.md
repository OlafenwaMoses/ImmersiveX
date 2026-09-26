# 0006. Keep room data local; use system/native anchors by default

- **Status:** Accepted
- **Date:** 2026-09-23

## Context
Room scans reveal the layout of someone's home or workplace. A backend would add cost, accounts and privacy obligations before anyone can start.

## Decision
- Room data (`MappedSpace`) and the content-anchor index stay on the device under `Application.persistentDataPath/ImmersiveX/`.
- Persistence uses the platform's **system/native anchor store** by default (AR Foundation persistent anchors, ARKit world map, visionOS/HoloLens system anchors), and a room-relative pose where none exists.
- **Cloud-provider anchors** (and shared multi-user rooms) come after v1, as an opt-in add-on behind the same `IAnchorStore` contract.

## Consequences
- Works offline, with no accounts and no server.
- Placements don't roam between devices until the cloud add-on exists.
- Users get **Forget this room** to delete local data.

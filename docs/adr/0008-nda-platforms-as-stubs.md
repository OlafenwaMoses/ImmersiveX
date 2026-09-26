# 0008. Ship NDA platforms as stubs in the public repository

- **Status:** Accepted
- **Date:** 2026-09-23

## Context
Native PlayStation VR2 development needs Sony's PS5 SDK and Unity's PS5 module. Both are under NDA and only available to licensed developers.
Their code can't be published in an MIT-licensed repository.

## Decision
The public repo contains `Platforms/PSVR2` as a stub: the adapter skeleton, capability declaration and a guide.
Licensed developers implement it in a private fork. PS VR2 on PC is covered by the SteamVR/OpenXR adapter.

## Consequences
- The public repo stays legally clean.
- The PS VR2 native path can't be tested in public CI.

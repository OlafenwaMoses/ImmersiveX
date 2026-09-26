# 0004. Don't use a dependency-injection framework

- **Status:** Accepted
- **Date:** 2026-09-23

## Context
ImmersiveX must be easy for beginners to read and extend. DI containers add concepts, reflection and startup magic.

## Decision
Use a small `Services` registry (register and resolve by interface) plus ScriptableObject settings assets.
The active platform adapter registers its providers at boot; features resolve them by interface.

## Consequences
- The whole wiring fits in a few short files that newcomers can step through in a debugger.
- Interfaces still make features testable with fakes.
- There is no automatic lifetime management, so services must be registered during boot, not lazily from random scripts.

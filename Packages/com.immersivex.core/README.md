# ImmersiveX Core

The platform-agnostic part of ImmersiveX. It builds on AR Foundation, the XR Interaction Toolkit and XR Hands,
and talks to devices only through the platform contract. Vendor SDKs live in the platform adapter packages under
`Platforms/` at the repository root.

| Folder | Contents |
|---|---|
| `Editor/` | Editor tooling. Today: `ProjectBaseline` (URP, linear colour, Input System). |
| `Runtime/` | Session flow, contracts and features (arrives in milestone M1). |
| `Tests/` | EditMode and PlayMode tests (arrives in milestone M1). |

Use it from another project by Git URL:

```
https://github.com/OlafenwaMoses/ImmersiveX.git?path=/Packages/com.immersivex.core
```

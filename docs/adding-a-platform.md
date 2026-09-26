# Adding a platform

A platform adapter is a small UPM package in `Platforms/<Name>/`. It tells ImmersiveX what the device can do and fills the gaps AR Foundation leaves. Core code never changes when you add a platform.

## 1. Copy the template
1. Copy `Platforms/_Template` to `Platforms/<Name>` (no leading underscore).
2. Rename these to your platform:
   - `package.json` → `name` (`com.immersivex.platform.<name>`) and `displayName`
   - both `.asmdef` files and the namespaces (`ImmersiveX.Platforms.<Name>`)
   - `TemplateAdapter` and `TemplateBuildConfigurator`

## 2. Declare dependencies and requirements
- **`package.json`:** pin the exact vendor packages the platform needs. Installing the adapter installs these versions.
- **`platform.json`:** everything else a build needs: Unity modules, build target, OS/SDK levels, permissions, device and account requirements, and `status` (`preview` until it passes the device checklist).

## 3. Implement the adapter (`Runtime/`)
| Member | What to do |
|---|---|
| `Id` | Same as `id` in `platform.json` |
| `Capabilities` | Declare see-through, room data, anchor persistence, input and boundary honestly; features choose fallbacks from these |
| `IsActive()` | True only on this platform. Check `Application.platform` plus the running XR loader or session subsystem |
| `RegisterProviders()` | Register `IPermissionProvider`, `ISpaceProvider`, `IBoundaryProvider` as the platform needs |
| `Priority` | Above `-100` (the editor simulation) |

**Keep `[assembly: AlwaysLinkAssembly]` in `AssemblyInfo.cs` and `[Preserve]` on the adapter.** Nothing references an adapter directly, so without them IL2CPP code stripping removes it from device builds. It still works in the editor, which makes this easy to miss ([ADR-0003](adr/0003-opt-in-platform-adapter-packages.md)).

## 4. Implement the build configurator (`Editor/`)
`Configure()` applies player settings, the XR loader (`XrLoaderSetup.UseOnlyLoader`) and vendor features. It must be safe to run repeatedly. `Validate()` returns remaining problems. The Meta Quest configurator is a full example.

If the vendor offers an OpenXR **feature set**, enable the set (as the Project Settings checkbox does), not individual features. Feature sets carry *required* features, and skipping one broke passthrough on Quest ([ADR-0007](adr/0007-quest-on-unity-openxr-meta.md)).

## 5. Install, test, build
1. **ImmersiveX ▸ Platform Setup ▸ Install**, then **Configure**. Fix everything **Configure** reports.
2. Run the EditMode tests (Test Runner, or the `test` automation command).
3. Try the flow in the editor simulator where one exists, then **Build And Run** to the device.
4. Run the device checklist (see `docs/platforms/meta-quest.md` for the Quest version), and record the results in `docs/validation/`.
5. Write `docs/platforms/<name>.md`: requirements, pinned versions, setup, build and run, simulator coverage, troubleshooting.

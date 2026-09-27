# Third-party notices

ImmersiveX Media bundles these libraries, unchanged, in `Runtime/Plugins/`:

| Library | Version | Licence | Used for |
|---|---|---|---|
| [ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp) | 0.8.8 (netstandard2.1) | MIT, © Oleg Stepanischev | Decompressing SPZ v4 files (ZSTD) |
| [System.Runtime.CompilerServices.Unsafe](https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe/6.1.0) | 6.1.0 (netstandard2.0) | MIT, © Microsoft | Required by ZstdSharp |

It depends on these Unity packages, which are installed from the Unity registry rather than bundled:
- glTFast (`com.unity.cloud.gltfast` 6.20.0), for glTF/GLB models.
- Burst, Collections and Mathematics, which glTFast pulls in.

The format specifications implemented here come from the formats' reference implementations: 3D Gaussian Splatting, PlayCanvas, antimatter15/splat, Niantic SPZ and GaussianSplats3D. No code was copied from them.

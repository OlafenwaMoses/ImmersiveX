using System.Collections.Generic;
using UnityEditor;

namespace ImmersiveX.Editor
{
    /// <summary>
    /// Implemented by each platform adapter's editor assembly. It applies the platform's project settings
    /// (build target, XR loader, OpenXR features, player settings) and reports anything still wrong.
    /// ImmersiveX finds implementations automatically; see <see cref="BuildCli"/> and the Platform Setup window.
    /// </summary>
    public interface IBuildConfigurator
    {
        /// <summary>Matches the <c>id</c> in the platform's <c>platform.json</c>, e.g. "metaquest".</summary>
        string PlatformId { get; }

        string DisplayName { get; }
        BuildTarget BuildTarget { get; }
        BuildTargetGroup BuildTargetGroup { get; }

        /// <summary>File name of the build output inside <c>Builds/&lt;platform id&gt;/</c>, e.g. "ImmersiveX.apk".</summary>
        string OutputFileName { get; }

        /// <summary>Apply every setting the platform needs. Must be safe to run repeatedly.</summary>
        void Configure();

        /// <summary>Human-readable problems that remain after <see cref="Configure"/>. Empty when all is well.</summary>
        IEnumerable<string> Validate();
    }
}

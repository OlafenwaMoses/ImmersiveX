using System.Collections.Generic;
using ImmersiveX.Editor;
using UnityEditor;

namespace ImmersiveX.Platforms.Template.Editor
{
    /// <summary>
    /// Applies the project settings this platform needs. Runs from Platform Setup ▸ Configure and before every build.
    /// Everything here must be safe to run repeatedly.
    /// </summary>
    public sealed class TemplateBuildConfigurator : IBuildConfigurator
    {
        public string PlatformId => "template";
        public string DisplayName => "Template platform";

        // TEMPLATE: the platform's build target, e.g. BuildTarget.Android / BuildTargetGroup.Android.
        public BuildTarget BuildTarget => BuildTarget.StandaloneOSX;
        public BuildTargetGroup BuildTargetGroup => BuildTargetGroup.Standalone;

        public string OutputFileName => "ImmersiveX.app";

        public void Configure()
        {
            // TEMPLATE: player settings (scripting backend, architectures, graphics APIs, OS levels),
            // then the XR loader, e.g. XrLoaderSetup.UseOnlyLoader(BuildTargetGroup, "<Loader type name>"),
            // then vendor features. See Platforms/MetaQuest/Editor/MetaQuestBuildConfigurator.cs for a full example.
        }

        public IEnumerable<string> Validate()
        {
            // TEMPLATE: return human-readable problems ("Error: …" / "Warning: …"), or nothing when all is well.
            yield break;
        }
    }
}

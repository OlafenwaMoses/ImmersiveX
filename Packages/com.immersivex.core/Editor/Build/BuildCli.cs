using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImmersiveX.Editor
{
    /// <summary>
    /// Configure and build any installed platform, from the Platform Setup window, the command line or editor automation.
    /// <see cref="BuildPlatform"/> with <c>run: false</c> never touches a device; <c>run: true</c> is Unity's Build And Run.
    /// </summary>
    /// <example>
    /// <code>
    /// Unity -batchmode -quit -projectPath . -buildTarget Android \
    ///   -executeMethod ImmersiveX.Editor.BuildCli.Build -platform metaquest
    /// </code>
    /// </example>
    public static class BuildCli
    {
        /// <summary>Command-line entry point: <c>-executeMethod ImmersiveX.Editor.BuildCli.Configure -platform &lt;id&gt;</c>.</summary>
        public static void Configure() => ConfigurePlatform(RequiredArgument("-platform"));

        /// <summary>Command-line entry point: <c>-executeMethod ImmersiveX.Editor.BuildCli.Build -platform &lt;id&gt;</c>.</summary>
        public static void Build() => BuildPlatform(RequiredArgument("-platform"));

        /// <summary>Command-line entry point for Unity's Build And Run: builds, installs on the connected device and launches.</summary>
        public static void BuildAndRun() => BuildPlatform(RequiredArgument("-platform"), run: true);

        public static IBuildConfigurator[] AllConfigurators() =>
            TypeCache.GetTypesDerivedFrom<IBuildConfigurator>()
                .Where(type => !type.IsAbstract && !type.IsInterface && type.GetConstructor(Type.EmptyTypes) != null)
                .Select(type => (IBuildConfigurator)Activator.CreateInstance(type))
                .ToArray();

        public static IBuildConfigurator Find(string platformId) =>
            AllConfigurators().FirstOrDefault(configurator => configurator.PlatformId == platformId);

        public static void ConfigurePlatform(string platformId)
        {
            var configurator = Require(platformId);
            configurator.Configure();
            AssetDatabase.SaveAssets();
            ReportIssues(configurator);
            ImmersiveXLog.Info($"{configurator.DisplayName}: project configured.");
        }

        public static BuildReport BuildPlatform(string platformId, bool run = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new BuildFailedException("Exit Play mode before building.");

            var configurator = Require(platformId);
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new BuildFailedException("Build cancelled: the open scenes have unsaved changes.");

            configurator.Configure();
            AssetDatabase.SaveAssets();
            if (EditorUserBuildSettings.activeBuildTarget != configurator.BuildTarget &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(configurator.BuildTargetGroup, configurator.BuildTarget))
                throw new BuildFailedException($"Couldn't switch to {configurator.BuildTarget}. Is its build module installed in Unity Hub?");
            ReportIssues(configurator);

            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0)
                throw new BuildFailedException("No scenes are enabled in Build Profiles. Run ImmersiveX ▸ Maintenance ▸ Recreate Starter Scene, or add your scene.");

            var output = Path.Combine("Builds", platformId, configurator.OutputFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = configurator.BuildTarget,
                targetGroup = configurator.BuildTargetGroup,
                options = (EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None) |
                          (run ? BuildOptions.AutoRunPlayer : BuildOptions.None),
            });

            var summary = report.summary;
            var fileSize = File.Exists(output) ? new FileInfo(output).Length / (1024f * 1024f) : 0f;
            ImmersiveXLog.Info($"{configurator.DisplayName} build {summary.result}: {Path.GetFullPath(output)} " +
                               $"({fileSize:0.0} MB, {summary.totalTime.TotalSeconds:0}s, {summary.totalErrors} errors, {summary.totalWarnings} warnings). " +
                               (run ? "Installed and launched on the connected device." : "Nothing was installed on a device."));
            if (summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"{configurator.DisplayName} build failed. See the Console for details.");
            return report;
        }

        static void ReportIssues(IBuildConfigurator configurator)
        {
            foreach (var issue in configurator.Validate())
                ImmersiveXLog.Warn($"{configurator.DisplayName}: {issue}");
        }

        static IBuildConfigurator Require(string platformId)
        {
            var configurator = Find(platformId);
            if (configurator != null)
                return configurator;

            var installed = string.Join(", ", AllConfigurators().Select(c => c.PlatformId));
            throw new BuildFailedException($"Platform '{platformId}' isn't installed. Installed: {(installed.Length > 0 ? installed : "none")}. Use ImmersiveX ▸ Platform Setup.");
        }

        static string RequiredArgument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException($"Missing command-line argument {name} <value>.");
            return args[index + 1];
        }
    }
}

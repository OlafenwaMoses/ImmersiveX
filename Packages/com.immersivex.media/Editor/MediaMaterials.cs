using System.IO;
using UnityEditor;
using UnityEngine;

namespace ImmersiveX.Media.Editor
{
    /// <summary>
    /// Makes sure ImmersiveX Media's materials exist in its Resources folder (one per shader), so the shaders ship in
    /// player builds. glTFast finds its shaders by name at runtime, which doesn't keep them in builds, so there's a
    /// reference material for each glTF shader and its common feature keywords too. Runs after scripts compile; also on the
    /// menu.
    /// </summary>
    static class MediaMaterials
    {
        const string Folder = "Packages/com.immersivex.media/Runtime/Resources/ImmersiveXMedia";

        static readonly (string file, string shader, string[] keywords)[] Materials =
        {
            ("HologramSplats", "ImmersiveX/Hologram Splats", null),
            ("Panorama", "ImmersiveX/Panorama", null),
            ("VolumetricUnlit", "ImmersiveX/Volumetric Unlit", null),
            ("glTF/Metallic", "Shader Graphs/glTF-pbrMetallicRoughness", null),
            ("glTF/MetallicEmissive", "Shader Graphs/glTF-pbrMetallicRoughness", new[] { "_EMISSIVE" }),
            ("glTF/MetallicOcclusion", "Shader Graphs/glTF-pbrMetallicRoughness", new[] { "_OCCLUSION" }),
            ("glTF/MetallicEmissiveOcclusion", "Shader Graphs/glTF-pbrMetallicRoughness", new[] { "_EMISSIVE", "_OCCLUSION" }),
            ("glTF/MetallicTransparent", "Shader Graphs/glTF-pbrMetallicRoughness", new[] { "_SURFACE_TYPE_TRANSPARENT" }),
            ("glTF/MetallicAlphaClip", "Shader Graphs/glTF-pbrMetallicRoughness", new[] { "_ALPHATEST_ON" }),
            ("glTF/Specular", "Shader Graphs/glTF-pbrSpecularGlossiness", null),
            ("glTF/Unlit", "Shader Graphs/glTF-unlit", null),
            ("glTF/UnlitTransparent", "Shader Graphs/glTF-unlit", new[] { "_SURFACE_TYPE_TRANSPARENT" }),
        };

        [InitializeOnLoadMethod]
        static void EnsureAfterCompile() => EditorApplication.delayCall += () => Ensure(log: false);

        [MenuItem("ImmersiveX/Maintenance/Create Media Materials", false, 120)]
        static void EnsureMenu() => Ensure(log: true);

        public static void Ensure(bool log)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            foreach (var (file, shaderName, keywords) in Materials)
            {
                var path = $"{Folder}/{file}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                    continue;
                var shader = Shader.Find(shaderName);
                if (shader == null)
                    continue; // not compiled (or installed) yet; the next compile tries again
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var material = new Material(shader) { name = Path.GetFileName(file) };
                if (keywords != null)
                    foreach (var keyword in keywords)
                        material.EnableKeyword(keyword);
                AssetDatabase.CreateAsset(material, path);
                if (log)
                    ImmersiveXLog.Info($"Created {path}.");
            }

            AssetDatabase.SaveAssets();
        }
    }
}

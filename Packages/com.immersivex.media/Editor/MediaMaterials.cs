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
            ("MeshLit", "Universal Render Pipeline/Simple Lit", null),
            ("PhotoTransparent", "Universal Render Pipeline/Unlit", new[] { "_SURFACE_TYPE_TRANSPARENT" }),
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
                Configure(file, material);
                AssetDatabase.CreateAsset(material, path);
                if (log)
                    ImmersiveXLog.Info($"Created {path}.");
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>Render states the URP shaders take from material properties.</summary>
        static void Configure(string file, Material material)
        {
            switch (file)
            {
                case "MeshLit":
                    material.SetFloat("_Cull", 0f); // both sides: downloaded meshes don't always wind consistently
                    break;
                case "PhotoTransparent":
                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_Blend", 0f);
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                    material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", 0f);
                    material.SetFloat("_Cull", 0f);
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    material.SetOverrideTag("RenderType", "Transparent");
                    break;
            }
        }
    }
}

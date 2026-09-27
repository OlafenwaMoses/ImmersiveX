using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Materials ImmersiveX Media loads from its Resources folder. Referencing each shader from a material there keeps it
    /// in player builds (shaders only reached through Shader.Find are stripped).
    /// </summary>
    static class MediaAssets
    {
        public const string Folder = "ImmersiveXMedia";

        static Material _splats;
        static Material _panorama;
        static Material _mesh;

        /// <summary>Gaussian splats (holograms, splat files, point clouds).</summary>
        public static Material SplatMaterial => _splats != null ? _splats : _splats = Load("HologramSplats");

        /// <summary>360°/180° video on a sphere.</summary>
        public static Shader PanoramaShader => (_panorama != null ? _panorama : _panorama = Load("Panorama")).shader;

        /// <summary>Unlit meshes with a texture and/or vertex colours (volumetric capture, scans).</summary>
        public static Material MeshMaterial => _mesh != null ? _mesh : _mesh = Load("VolumetricUnlit");

        static Material Load(string name)
        {
            var material = Resources.Load<Material>($"{Folder}/{name}");
            if (material == null)
                ImmersiveXLog.Error($"ImmersiveX Media is missing Resources/{Folder}/{name}.mat. Run ImmersiveX ▸ Maintenance ▸ Create Media Materials.");
            return material;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _splats = null;
            _panorama = null;
            _mesh = null;
        }
    }
}

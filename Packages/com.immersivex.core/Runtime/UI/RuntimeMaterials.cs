using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// Materials for shapes ImmersiveX creates while the app runs (anchor markers, grab bars, floor outlines).
    /// Runtime primitives otherwise get Unity's built-in material, which URP can't draw on devices (it shows pink).
    /// The template comes from <see cref="ImmersiveXSettings.RuntimeMaterial"/> (URP Unlit), so it's always in the build.
    /// </summary>
    public static class RuntimeMaterials
    {
        static Material _template;

        /// <summary>A new material of <paramref name="color"/>. Destroy it with the object that uses it.</summary>
        public static Material Create(Color color)
        {
            if (_template == null)
                _template = ImmersiveXSettings.Load().RuntimeMaterial;
            var material = _template != null ? new Material(_template) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.color = color;
            return material;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _template = null;
    }
}

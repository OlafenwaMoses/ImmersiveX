using UnityEngine;

namespace ImmersiveX
{
    /// <summary>Adds a box collider that wraps an object's visible meshes, so it can be grabbed.</summary>
    public static class ColliderFitting
    {
        public static BoxCollider FitBox(GameObject target)
        {
            var box = target.GetComponent<BoxCollider>();
            if (box == null)
                box = target.AddComponent<BoxCollider>();

            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return box;

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            var transform = target.transform;
            var scale = transform.lossyScale;
            box.center = transform.InverseTransformPoint(bounds.center);
            box.size = new Vector3(
                bounds.size.x / Mathf.Max(Mathf.Abs(scale.x), 1e-5f),
                bounds.size.y / Mathf.Max(Mathf.Abs(scale.y), 1e-5f),
                bounds.size.z / Mathf.Max(Mathf.Abs(scale.z), 1e-5f));
            return box;
        }
    }
}

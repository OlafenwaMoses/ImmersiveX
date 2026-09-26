using UnityEngine;

namespace ImmersiveX
{
    /// <summary>Draws the walkable area's outline on the real floor for a few seconds, so the user can see what was mapped.</summary>
    public static class WalkableAreaView
    {
        static readonly Color LineColor = new Color(0.3f, 0.95f, 0.6f);
        const float LineWidth = 0.025f;
        const float LineHeight = 0.004f;

        /// <summary>
        /// Show the outline; it removes itself after <paramref name="seconds"/> (0 keeps it). Pass the XR Origin's
        /// trackables as <paramref name="parent"/> so the outline moves with the room's planes if the rig moves.
        /// </summary>
        public static GameObject Show(WalkableArea area, Pose floorPose, float seconds, Transform parent = null)
        {
            var root = new GameObject("Walkable Area");
            root.transform.SetPositionAndRotation(floorPose.position + Vector3.up * 0.005f, floorPose.rotation);
            if (parent != null)
                root.transform.SetParent(parent, worldPositionStays: true);
            var material = RuntimeMaterials.Create(LineColor);

            foreach (var (from, to) in area.Outline())
            {
                var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(segment.GetComponent<Collider>()); // visual only: never blocks grabbing or physics
                segment.name = "Edge";
                segment.transform.SetParent(root.transform, false);

                var a = new Vector3(from.x, 0f, from.y);
                var b = new Vector3(to.x, 0f, to.y);
                segment.transform.localPosition = (a + b) * 0.5f;
                segment.transform.localRotation = Quaternion.LookRotation(b - a, Vector3.up);
                segment.transform.localScale = new Vector3(LineWidth, LineHeight, Vector3.Distance(a, b) + LineWidth);

                var renderer = segment.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            if (seconds > 0f)
            {
                Object.Destroy(root, seconds);
                Object.Destroy(material, seconds);
            }

            return root;
        }
    }
}

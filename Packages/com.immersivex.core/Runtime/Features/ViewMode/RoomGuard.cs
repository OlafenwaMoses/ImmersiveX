using System.Collections.Generic;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// The real room, shown in VR mode so the user doesn't walk into it while the real world is hidden (see
    /// <see cref="ViewModes"/>): its walls (from the mapped floor outline, up to the ceiling) and its furniture (the sides of
    /// Space Setup's boxes). Each surface shows a soft grid only near the user, brighter as they come closer, and a faint
    /// line where it meets the floor is always there. Drawn by the <c>ImmersiveX/Room Guard</c> shader.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class RoomGuard : MonoBehaviour
    {
        const string ShaderName = "ImmersiveX/RoomGuard";
        const float DefaultWallHeight = 2.6f;

        Material _material;
        Mesh _mesh;

        public int Walls { get; private set; }
        public int Furniture { get; private set; }

        /// <summary>Where each piece of furniture stands (its foot, world space), for logs.</summary>
        public List<Vector3> FurnitureAt { get; } = new List<Vector3>();

        public static RoomGuard Create(Transform parent)
        {
            var guard = new GameObject("Room Guard").AddComponent<RoomGuard>();
            guard.transform.SetParent(parent, false);
            var shader = Resources.Load<Shader>(ShaderName);
            if (shader != null)
                guard._material = new Material(shader) { name = "Room Guard" };
            else
                ImmersiveXLog.Error($"The room guard's shader ({ShaderName}) is missing from Resources; VR mode can't show the room's walls.");
            guard.gameObject.AddComponent<MeshFilter>();
            var renderer = guard.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = guard._material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            guard.gameObject.SetActive(false);
            return guard;
        }

        /// <summary>
        /// Show the room: walls along <paramref name="floorPolygon"/> (floor-plane space, at <paramref name="floorPose"/>) up
        /// to <paramref name="ceilingHeight"/> (0: a usual 2.6 m), and the sides of each piece of <paramref name="furniture"/>
        /// (a box's pose and size, as Space Setup gives them).
        /// </summary>
        public void Show(Vector2[] floorPolygon, Pose floorPose, float ceilingHeight, IEnumerable<(Pose pose, Vector3 size)> furniture)
        {
            var surfaces = new List<(Vector3 a, Vector3 b, float height)>();
            var height = ceilingHeight > 0.5f ? ceilingHeight : DefaultWallHeight;
            Walls = 0;
            if (floorPolygon != null && floorPolygon.Length >= 3)
                for (var i = 0; i < floorPolygon.Length; i++)
                {
                    var a = floorPose.position + floorPose.rotation * new Vector3(floorPolygon[i].x, 0f, floorPolygon[i].y);
                    var p = floorPolygon[(i + 1) % floorPolygon.Length];
                    var b = floorPose.position + floorPose.rotation * new Vector3(p.x, 0f, p.y);
                    if ((b - a).sqrMagnitude < 0.01f)
                        continue;
                    surfaces.Add((a, b, height));
                    Walls++;
                }

            Furniture = 0;
            FurnitureAt.Clear();
            if (furniture != null)
                foreach (var (pose, size) in furniture)
                {
                    if (size.x < 0.05f || size.z < 0.05f || size.y < 0.05f)
                        continue;
                    // a box's pose is at its centre or at the middle of its top, depending on the platform: whichever
                    // its height above the floor fits
                    var above = pose.position.y - floorPose.position.y;
                    var topY = Mathf.Abs(above - size.y) < Mathf.Abs(above - size.y * 0.5f) ? pose.position.y : pose.position.y + size.y * 0.5f;
                    var bottomY = Mathf.Max(floorPose.position.y, topY - size.y);
                    var bottom = new Vector3(pose.position.x, bottomY, pose.position.z);
                    var top = topY - bottomY;
                    var right = Vector3.ProjectOnPlane(pose.rotation * Vector3.right, Vector3.up).normalized;
                    var forward = Vector3.Cross(right, Vector3.up);
                    var x = right * (size.x * 0.5f);
                    var z = forward * (size.z * 0.5f);
                    Vector3 c0 = bottom - x - z, c1 = bottom + x - z, c2 = bottom + x + z, c3 = bottom - x + z;
                    surfaces.Add((c0, c1, top));
                    surfaces.Add((c1, c2, top));
                    surfaces.Add((c2, c3, top));
                    surfaces.Add((c3, c0, top));
                    FurnitureAt.Add(bottom);
                    Furniture++;
                }

            if (_mesh != null)
                Destroy(_mesh);
            _mesh = Surfaces(surfaces);
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // the mesh is in world space
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);

        void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
            if (_material != null)
                Destroy(_material);
        }

        /// <summary>
        /// One mesh of upright quads from the floor at a to the floor at b, <c>height</c> tall: uv in metres (along, up),
        /// uv2 the quad's size, which is what the shader's grid, edges and floor line are drawn from.
        /// </summary>
        public static Mesh Surfaces(IReadOnlyList<(Vector3 a, Vector3 b, float height)> surfaces)
        {
            var vertices = new List<Vector3>(surfaces.Count * 4);
            var metres = new List<Vector2>(surfaces.Count * 4);
            var sizes = new List<Vector2>(surfaces.Count * 4);
            var triangles = new List<int>(surfaces.Count * 6);
            foreach (var (a, b, height) in surfaces)
            {
                var length = Vector3.Distance(a, b);
                var start = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(b + Vector3.up * height);
                vertices.Add(a + Vector3.up * height);
                metres.Add(new Vector2(0f, 0f));
                metres.Add(new Vector2(length, 0f));
                metres.Add(new Vector2(length, height));
                metres.Add(new Vector2(0f, height));
                for (var k = 0; k < 4; k++)
                    sizes.Add(new Vector2(length, height));
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }

            var mesh = new Mesh { name = "Room Guard", indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, metres);
            mesh.SetUVs(1, sizes);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

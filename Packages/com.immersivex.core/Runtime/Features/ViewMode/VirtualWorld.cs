using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// The place VR mode puts the user in (see <see cref="ViewModes"/>): a quiet, low-lit world that suits any content. A night
    /// sky, deep blue overhead fading to a dusky haze at the horizon with sparse faint stars, over a dark floor at the real
    /// floor's height, with a faint metre grid and a soft pool of light where the content stands, melting into the haze far
    /// away. Nothing in it is bright, so holograms, models, video and 3D scenes all stand out. Drawn by the
    /// <c>ImmersiveX/Virtual World</c> shader (in Resources, so it's always in the build); no lights, no textures.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class VirtualWorld : MonoBehaviour
    {
        const string ShaderName = "ImmersiveX/VirtualWorld";
        const float SkyRadius = 150f;
        const float FloorHalfSize = 90f;
        const float BelowTheFloor = 0.004f; // just under the real floor, so content standing on it is never cut

        /// <summary>The colour behind everything (the camera's clear colour in VR): the horizon haze.</summary>
        public static readonly Color Horizon = new Color(0.086f, 0.098f, 0.165f, 1f);

        Transform _sky, _floor;
        Material _skyMaterial, _floorMaterial;
        Transform _eyes;

        public bool IsShowing => gameObject.activeSelf;

        /// <summary>Build the world (hidden); <paramref name="eyes"/> is the camera the sky stays centred on.</summary>
        public static VirtualWorld Create(Transform parent, Transform eyes)
        {
            var world = new GameObject("Virtual World").AddComponent<VirtualWorld>();
            world.transform.SetParent(parent, false);
            world._eyes = eyes;
            world.Build();
            world.gameObject.SetActive(false);
            return world;
        }

        /// <summary>Show it with the floor at <paramref name="floorHeight"/> and the pool of light at <paramref name="centre"/> (world).</summary>
        public void Show(float floorHeight, Vector3 centre)
        {
            _floor.position = new Vector3(centre.x, floorHeight - BelowTheFloor, centre.z);
            _floorMaterial.SetVector("_Centre", centre);
            gameObject.SetActive(true);
            LateUpdate();
        }

        public void Hide() => gameObject.SetActive(false);

        void Build()
        {
            var shader = Resources.Load<Shader>(ShaderName);
            if (shader == null)
            {
                ImmersiveXLog.Error($"The virtual world's shader ({ShaderName}) is missing from Resources; VR mode shows a plain dark background.");
                return;
            }

            _skyMaterial = new Material(shader) { name = "Virtual World Sky", renderQueue = 1000 }; // drawn first, behind everything
            _skyMaterial.SetFloat("_Part", 0f);
            _skyMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off); // seen from inside
            _skyMaterial.SetFloat("_ZWrite", 0f);
            _sky = Part("Sky", SphereMesh(), _skyMaterial);
            var camera = _eyes != null ? _eyes.GetComponent<Camera>() : null;
            _sky.localScale = Vector3.one * (camera != null ? Mathf.Min(SkyRadius, camera.farClipPlane * 0.8f) : SkyRadius); // inside the far plane

            _floorMaterial = new Material(shader) { name = "Virtual World Floor", renderQueue = 1990 }; // before the content
            _floorMaterial.SetFloat("_Part", 1f);
            _floorMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            _floorMaterial.SetFloat("_ZWrite", 1f);
            _floor = Part("Floor", FloorMesh(), _floorMaterial);
        }

        Transform Part(string name, Mesh mesh, Material material)
        {
            var part = new GameObject(name);
            part.transform.SetParent(transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return part.transform;
        }

        // the sky has no position: it stays centred on the eyes, so it never comes nearer as the user walks
        void LateUpdate()
        {
            if (_sky != null && _eyes != null)
                _sky.position = _eyes.position;
        }

        void OnDestroy()
        {
            if (_skyMaterial != null)
                Destroy(_skyMaterial);
            if (_floorMaterial != null)
                Destroy(_floorMaterial);
        }

        /// <summary>A unit sphere (radius 1) of latitude and longitude rings.</summary>
        static Mesh SphereMesh(int rings = 32, int segments = 64)
        {
            var vertices = new Vector3[(rings + 1) * (segments + 1)];
            for (var r = 0; r <= rings; r++)
            for (var s = 0; s <= segments; s++)
            {
                var theta = Mathf.PI * r / rings;
                var phi = 2f * Mathf.PI * s / segments;
                vertices[r * (segments + 1) + s] = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
            }

            var triangles = new int[rings * segments * 6];
            var t = 0;
            for (var r = 0; r < rings; r++)
            for (var s = 0; s < segments; s++)
            {
                int a = r * (segments + 1) + s, b = a + segments + 1;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = a + 1;
                triangles[t++] = a + 1; triangles[t++] = b; triangles[t++] = b + 1;
            }

            var mesh = new Mesh { name = "Virtual World Sky", vertices = vertices, triangles = triangles };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }

        /// <summary>A square floor, facing up, split into a grid so the far floor isn't one huge triangle.</summary>
        static Mesh FloorMesh(int cells = 24)
        {
            var vertices = new Vector3[(cells + 1) * (cells + 1)];
            for (var z = 0; z <= cells; z++)
            for (var x = 0; x <= cells; x++)
                vertices[z * (cells + 1) + x] = new Vector3((x / (float)cells * 2f - 1f) * FloorHalfSize, 0f, (z / (float)cells * 2f - 1f) * FloorHalfSize);
            var triangles = new int[cells * cells * 6];
            var t = 0;
            for (var z = 0; z < cells; z++)
            for (var x = 0; x < cells; x++)
            {
                int a = z * (cells + 1) + x, b = a + cells + 1;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = a + 1;
                triangles[t++] = a + 1; triangles[t++] = b; triangles[t++] = b + 1;
            }

            var mesh = new Mesh { name = "Virtual World Floor", vertices = vertices, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

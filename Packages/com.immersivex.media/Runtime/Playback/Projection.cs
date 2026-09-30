using System.Text.RegularExpressions;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>Stereo arrangement of a frame, as the panorama shader reads it.</summary>
    public enum VideoLayout
    {
        Mono,
        TopBottom,
        SideBySide,
    }

    /// <summary>How a 360° or 180° video or photo is split between the eyes. Auto reads the file name, then the shape.</summary>
    public enum StereoLayout
    {
        Auto,
        Mono,
        [InspectorName("Top-bottom (left eye on top)")] TopBottom,
        [InspectorName("Side-by-side (left eye on the left)")] SideBySide,
    }

    /// <summary>
    /// Pictures on a screen or all around the viewer, shared by video and photos: whether a frame is flat, 180° or 360°
    /// and mono or stereo, and the screen or sphere it's drawn on.
    /// </summary>
    static class Projection
    {
        /// <summary>Height of a screen's bottom edge above the floor, in metres.</summary>
        public const float ScreenBottom = 0.8f;

        public const float SphereRadius = 20f;

        // Name hints, as whole words so that "IMG_1805" isn't 180° and "outdoor" isn't over-under.
        static readonly Regex TopBottomHint = new Regex(@"(^|[^a-z0-9])(tb|ou|3dv)([^a-z0-9]|$)|top[-_ ]?bottom|over[-_ ]?under", RegexOptions.Compiled);
        static readonly Regex SideBySideHint = new Regex(@"(^|[^a-z0-9])(sbs|lr|3dh)([^a-z0-9]|$)|side[-_ ]?by[-_ ]?side", RegexOptions.Compiled);
        static readonly Regex Hint180 = new Regex(@"(^|[^0-9])180([^0-9]|$)", RegexOptions.Compiled);
        static readonly Regex Hint360 = new Regex(@"(^|[^0-9])360([^0-9]|$)|equirect", RegexOptions.Compiled);

        /// <summary>
        /// Coverage (0 = flat, 180 or 360 degrees) and stereo layout. The coverage comes from the requested format, then
        /// hints in the file name (360, 180, equirect), then the shape: a 2:1 frame is a 360° panorama. Once the coverage is
        /// known, the layout comes from the stereo setting, then the name (_tb, _sbs…), then the shape: a square 360° frame
        /// is top-bottom stereo, a 2:1 180° frame is side-by-side.
        /// </summary>
        public static (float coverage, VideoLayout layout) Classify(MediaFormat requested, StereoLayout stereo, string fileName, int width, int height)
        {
            var name = (fileName ?? string.Empty).ToLowerInvariant();
            VideoLayout? chosen = stereo == StereoLayout.Mono ? VideoLayout.Mono
                : stereo == StereoLayout.TopBottom ? VideoLayout.TopBottom
                : stereo == StereoLayout.SideBySide ? VideoLayout.SideBySide
                : TopBottomHint.IsMatch(name) ? VideoLayout.TopBottom
                : SideBySideHint.IsMatch(name) ? VideoLayout.SideBySide
                : (VideoLayout?)null;
            var aspect = width / (float)Mathf.Max(1, height);

            float coverage;
            if (requested == MediaFormat.Video360)
                coverage = 360f;
            else if (requested == MediaFormat.Video180)
                coverage = 180f;
            else if (requested == MediaFormat.Video)
                coverage = 0f;
            else if (Hint180.IsMatch(name))
                coverage = 180f;
            else if (Hint360.IsMatch(name))
                coverage = 360f;
            else
            {
                // A mono 360° panorama is 2:1; each eye of top-bottom or side-by-side 360° is 2:1, of side-by-side 180° 1:1.
                var eyeAspect = chosen == VideoLayout.TopBottom ? aspect * 2f : chosen == VideoLayout.SideBySide ? aspect / 2f : aspect;
                coverage = Near(eyeAspect, 2f) ? 360f : chosen == VideoLayout.SideBySide && Near(eyeAspect, 1f) ? 180f : 0f;
            }

            if (coverage <= 0f)
                return (0f, VideoLayout.Mono); // flat stereo 3D isn't supported: the whole frame is shown
            if (chosen.HasValue)
                return (coverage, chosen.Value);
            if (coverage >= 270f)
                return (coverage, Near(aspect, 1f) ? VideoLayout.TopBottom : Near(aspect, 4f) ? VideoLayout.SideBySide : VideoLayout.Mono);
            return (coverage, Near(aspect, 2f) ? VideoLayout.SideBySide : Near(aspect, 0.5f) ? VideoLayout.TopBottom : VideoLayout.Mono);
        }

        static bool Near(float value, float target) => Mathf.Abs(value - target) < target * 0.02f;

        /// <summary>An upright screen standing on the floor under <paramref name="root"/>, its front towards the user.</summary>
        public static GameObject BuildScreen(Transform root, Material material, int width, int height, float screenHeight, out Mesh mesh, out Bounds bounds)
        {
            var screenWidth = screenHeight * width / Mathf.Max(1, height);
            float left = -screenWidth * 0.5f, right = screenWidth * 0.5f, bottom = ScreenBottom, top = ScreenBottom + screenHeight;
            mesh = new Mesh { name = "Screen" };
            mesh.vertices = new[] { new Vector3(left, bottom, 0f), new Vector3(right, bottom, 0f), new Vector3(left, top, 0f), new Vector3(right, top, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 }; // front faces the user (on the −Z side)
            mesh.RecalculateBounds();

            var surface = new GameObject("Screen", typeof(MeshFilter), typeof(MeshRenderer));
            surface.transform.SetParent(root, false);
            surface.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bounds = new Bounds(new Vector3(0f, bottom + screenHeight * 0.5f, 0f), new Vector3(screenWidth, screenHeight, 0.05f));
            return surface;
        }

        /// <summary>A sphere around the viewer showing <paramref name="texture"/> (see <see cref="FollowViewer"/>).</summary>
        public static GameObject BuildSphere(Texture texture, float coverage, VideoLayout layout, out Mesh mesh, out Material material, out Bounds bounds)
        {
            mesh = Sphere(64, 32);
            material = new Material(MediaAssets.PanoramaShader) { name = "Panorama (instance)" };
            material.mainTexture = texture;
            material.SetFloat("_Layout", (int)layout);
            material.SetFloat("_Coverage", coverage);
            var surface = new GameObject("Panorama Sphere", typeof(MeshFilter), typeof(MeshRenderer));
            surface.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            surface.transform.localScale = Vector3.one * SphereRadius;
            bounds = new Bounds(Vector3.zero, Vector3.one * SphereRadius * 2f);
            return surface;
        }

        /// <summary>
        /// Keep a panorama sphere centred on the viewer's head; its front is where they faced when it first showed.
        /// Returns true once the facing is set.
        /// </summary>
        public static bool FollowViewer(GameObject sphere, Transform viewer, bool facingSet)
        {
            if (sphere == null || viewer == null)
                return facingSet;
            sphere.transform.position = viewer.position;
            if (facingSet)
                return true;
            var forward = Vector3.ProjectOnPlane(viewer.forward, Vector3.up);
            sphere.transform.rotation = forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
            return true;
        }

        /// <summary>A unit UV sphere (positions only; the shader works out the picture lookup).</summary>
        static Mesh Sphere(int longitudes, int latitudes)
        {
            var vertices = new Vector3[(longitudes + 1) * (latitudes + 1)];
            for (int lat = 0, v = 0; lat <= latitudes; lat++)
            {
                var theta = Mathf.PI * lat / latitudes;
                for (var lon = 0; lon <= longitudes; lon++, v++)
                {
                    var phi = 2f * Mathf.PI * lon / longitudes;
                    vertices[v] = new Vector3(Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Cos(phi));
                }
            }

            var triangles = new int[longitudes * latitudes * 6];
            for (int lat = 0, t = 0; lat < latitudes; lat++)
            for (var lon = 0; lon < longitudes; lon++, t += 6)
            {
                var a = lat * (longitudes + 1) + lon;
                var b = a + longitudes + 1;
                triangles[t] = a;
                triangles[t + 1] = b;
                triangles[t + 2] = a + 1;
                triangles[t + 3] = a + 1;
                triangles[t + 4] = b;
                triangles[t + 5] = b + 1;
            }

            var mesh = new Mesh { name = "Panorama Sphere", vertices = vertices, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

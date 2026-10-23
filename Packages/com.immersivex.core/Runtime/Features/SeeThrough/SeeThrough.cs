using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ImmersiveX
{
    /// <summary>Configures the camera so the real world shows behind content (F1).</summary>
    public static class SeeThrough
    {
        public static void Apply(Camera camera, SeeThroughMode mode)
        {
            switch (mode)
            {
                case SeeThroughMode.VideoPassthrough:
                    // Passthrough is composited behind the camera image, so the camera must clear to transparent.
                    ClearToTransparent(camera);
                    Enable<ARCameraManager>(camera.gameObject);
                    break;

                case SeeThroughMode.CameraFeed:
                    Enable<ARCameraManager>(camera.gameObject);
                    Enable<ARCameraBackground>(camera.gameObject);
                    break;

                case SeeThroughMode.Optical:
                    // Black is transparent on optical see-through displays.
                    ClearToTransparent(camera);
                    break;

                default:
                    camera.clearFlags = CameraClearFlags.Skybox;
                    break;
            }
        }

        /// <summary>
        /// Hide the real world (VR mode, see <see cref="ViewModes"/>): the passthrough and any camera feed off, the camera
        /// clearing to <paramref name="background"/>. <see cref="Apply"/> brings the real world back.
        /// </summary>
        public static void Remove(Camera camera, Color background)
        {
            Disable<ARCameraBackground>(camera.gameObject);
            Disable<ARCameraManager>(camera.gameObject);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
        }

        static void Disable<T>(GameObject target) where T : Behaviour
        {
            var component = target.GetComponent<T>();
            if (component != null)
                component.enabled = false;
        }

        static void ClearToTransparent(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
        }

        static void Enable<T>(GameObject target) where T : Behaviour
        {
            var component = target.GetComponent<T>();
            if (component == null)
                component = target.AddComponent<T>();
            component.enabled = true;
        }
    }
}

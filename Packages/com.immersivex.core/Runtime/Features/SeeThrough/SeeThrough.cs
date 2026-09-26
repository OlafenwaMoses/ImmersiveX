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

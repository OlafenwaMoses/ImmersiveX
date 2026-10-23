using System;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// The editor's stand-in for the headset's camera (XR Simulation and the Meta XR Simulator): it renders the app's own
    /// scene from the user's head. The real room isn't in it, because the simulators draw their passthrough themselves, so
    /// test with virtual objects in front of the user. <see cref="ImmersiveHost.PassthroughCamera"/> uses it in the editor when
    /// the platform has no camera of its own.
    /// </summary>
    sealed class SimulatedPassthroughCamera : IPassthroughCameraProvider
    {
        /// <summary>About the Quest 3 passthrough cameras' view.</summary>
        const float VerticalFieldOfView = 70f;

        static readonly Color Background = new Color(0.16f, 0.16f, 0.17f);

        Camera _camera;
        RenderTexture _texture;
        CameraIntrinsics _intrinsics;

        /// <summary>The layers it draws (leave the app's own panels and labels out: a real camera never sees them).</summary>
        public int CullingMask { get; set; } = ~0;

        public string Name => "Simulated camera (the app's scene, seen from the head)";

        public bool IsSupported => Application.isEditor;

        public void RequestAccess(Action<bool> onResult) => onResult(true);

        public bool Start(CameraEye eye, Vector2Int resolution)
        {
            var head = ImmersiveHost.Head;
            if (head == null)
                return false;
            Stop();
            var size = new Vector2Int(Mathf.Clamp(resolution.x, 64, 4096), Mathf.Clamp(resolution.y, 64, 4096));
            _texture = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32) { name = "Simulated Passthrough Camera" };
            // in the scene (hidden), so it goes when Play mode ends; DontSave objects would outlive it
            var holder = new GameObject("Simulated Passthrough Camera") { hideFlags = HideFlags.HideInHierarchy };
            _camera = holder.AddComponent<Camera>();
            var source = head.GetComponent<Camera>();
            if (source != null)
                _camera.CopyFrom(source); // the same renderer as the headset's view
            _camera.stereoTargetEye = StereoTargetEyeMask.None;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Background;
            _camera.fieldOfView = VerticalFieldOfView;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 50f;
            _camera.cullingMask = CullingMask;
            _camera.targetTexture = _texture;
            holder.AddComponent<FollowHead>().Head = head;
            _intrinsics = CameraIntrinsics.FromFieldOfView(size, VerticalFieldOfView);
            return true;
        }

        public void Stop()
        {
            if (_camera != null)
            {
                _camera.targetTexture = null;
                UnityEngine.Object.Destroy(_camera.gameObject);
            }

            if (_texture != null)
                UnityEngine.Object.Destroy(_texture);
            _camera = null;
            _texture = null;
        }

        public bool IsPlaying => _camera != null && _texture != null;

        public Texture Texture => _texture;

        public CameraIntrinsics Intrinsics => _intrinsics;

        public Pose WorldPose => _camera != null ? new Pose(_camera.transform.position, _camera.transform.rotation) : Pose.identity;

        sealed class FollowHead : MonoBehaviour
        {
            public Transform Head;

            void LateUpdate()
            {
                if (Head != null)
                    transform.SetPositionAndRotation(Head.position, Head.rotation);
            }

            void OnDestroy()
            {
                // Play mode ending (or the scene unloading) destroys the camera without Stop: release its picture too
                var camera = GetComponent<Camera>();
                if (camera == null || camera.targetTexture == null)
                    return;
                var texture = camera.targetTexture;
                camera.targetTexture = null;
                Destroy(texture);
            }
        }
    }
}

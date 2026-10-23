using System;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>Which of the headset's passthrough cameras.</summary>
    public enum CameraEye
    {
        Left,
        Right,
    }

    /// <summary>
    /// A camera's intrinsics, in pixels of <see cref="Resolution"/>: where a pixel looks in the camera's own frame, with +z
    /// forward, +x right and +y up. Viewport points run from (0, 0) at the bottom left to (1, 1) at the top right, as Unity
    /// textures do.
    /// </summary>
    [Serializable]
    public struct CameraIntrinsics
    {
        public Vector2 FocalLength;
        public Vector2 PrincipalPoint;

        /// <summary>The resolution the focal length and principal point are given for.</summary>
        public Vector2Int Resolution;

        public bool IsValid => FocalLength.x > 0f && FocalLength.y > 0f && Resolution.x > 0 && Resolution.y > 0;

        /// <summary>The direction through a viewport point, in the camera's frame (not normalised; z is 1).</summary>
        public Vector3 Direction(Vector2 viewport) =>
            new Vector3((viewport.x * Resolution.x - PrincipalPoint.x) / FocalLength.x,
                (viewport.y * Resolution.y - PrincipalPoint.y) / FocalLength.y, 1f);

        /// <summary>The horizontal and vertical field of view, in degrees.</summary>
        public Vector2 FieldOfView =>
            new Vector2(2f * Mathf.Atan(Resolution.x * 0.5f / FocalLength.x), 2f * Mathf.Atan(Resolution.y * 0.5f / FocalLength.y)) * Mathf.Rad2Deg;

        /// <summary>An ideal pinhole camera of <paramref name="resolution"/> with a vertical field of view in degrees.</summary>
        public static CameraIntrinsics FromFieldOfView(Vector2Int resolution, float verticalDegrees)
        {
            var focal = resolution.y * 0.5f / Mathf.Tan(verticalDegrees * 0.5f * Mathf.Deg2Rad);
            return new CameraIntrinsics
            {
                FocalLength = new Vector2(focal, focal),
                PrincipalPoint = new Vector2(resolution.x * 0.5f, resolution.y * 0.5f),
                Resolution = resolution,
            };
        }

        public override string ToString() =>
            $"{Resolution.x}×{Resolution.y}, focal {FocalLength.x:0.0}/{FocalLength.y:0.0} px, centre {PrincipalPoint.x:0.0}, {PrincipalPoint.y:0.0}, " +
            $"field of view {FieldOfView.x:0}°×{FieldOfView.y:0}°";
    }

    /// <summary>
    /// The headset's own view of the room, as images the app can read (Quest 3 and 3S: the Passthrough Camera API). Bare metal
    /// content reads it through <see cref="ImmersiveHost.PassthroughCamera"/>.
    /// </summary>
    public interface IPassthroughCameraProvider
    {
        /// <summary>What the frames come from, for logs ("Quest passthrough camera", "Simulated camera").</summary>
        string Name { get; }

        /// <summary>True when this device has a camera the app may read.</summary>
        bool IsSupported { get; }

        /// <summary>Ask for access (Quest: the camera permissions); <paramref name="onResult"/> receives true if granted.</summary>
        void RequestAccess(Action<bool> onResult);

        /// <summary>Start the camera at the supported resolution closest to <paramref name="resolution"/>. False if it can't.</summary>
        bool Start(CameraEye eye, Vector2Int resolution);

        void Stop();

        /// <summary>True once frames are arriving.</summary>
        bool IsPlaying { get; }

        /// <summary>The latest frame (a GPU texture, upright).</summary>
        Texture Texture { get; }

        CameraIntrinsics Intrinsics { get; }

        /// <summary>
        /// The camera's pose in the world now. Its rotation turns <see cref="CameraIntrinsics.Direction"/> into world space.
        /// </summary>
        Pose WorldPose { get; }
    }
}

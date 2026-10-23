using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// Where an Android camera sits on the headset, from its Camera2 lens pose (<c>LENS_POSE_TRANSLATION</c> and
    /// <c>LENS_POSE_ROTATION</c>), and its pose in the world. The conversion is the one Meta's Passthrough Camera API samples
    /// use; the world pose's rotation turns <see cref="CameraIntrinsics.Direction"/> into world space.
    /// </summary>
    public static class LensPose
    {
        /// <summary>The camera's pose relative to the head, in Unity's axes.</summary>
        public static Pose HeadFromCamera(float[] translation, float[] rotation)
        {
            var position = new Vector3(translation[0], translation[1], -translation[2]);
            var cameraFromHead = new Quaternion(-rotation[0], -rotation[1], rotation[2], rotation[3]);
            return new Pose(position, Quaternion.Inverse(cameraFromHead));
        }

        /// <summary>The camera's pose in the world, for the head at <paramref name="head"/>.</summary>
        public static Pose World(Pose head, Pose headFromCamera) =>
            new Pose(head.position + head.rotation * headFromCamera.position,
                head.rotation * headFromCamera.rotation * Quaternion.Euler(180f, 0f, 0f));
    }
}

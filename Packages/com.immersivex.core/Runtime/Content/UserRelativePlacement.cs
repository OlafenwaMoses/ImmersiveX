using UnityEngine;

namespace ImmersiveX
{
    /// <summary>Helpers for placing things relative to where the user stands and faces.</summary>
    public static class UserRelativePlacement
    {
        /// <summary>The head position with a level rotation: where the user stands and which way they face.</summary>
        public static Pose UserPose(Transform head)
        {
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.ProjectOnPlane(head.up, Vector3.up); // looking straight up or down
            return new Pose(head.position, Quaternion.LookRotation(forward.normalized, Vector3.up));
        }

        /// <summary>Offset a user pose. X is to the user's right, Y is up from eye height, Z is forward. Metres.</summary>
        public static Pose Apply(Pose user, Vector3 offset) =>
            new Pose(user.position + user.rotation * offset, user.rotation);
    }
}

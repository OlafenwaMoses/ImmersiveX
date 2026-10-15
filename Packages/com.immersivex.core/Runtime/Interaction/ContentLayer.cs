using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace ImmersiveX
{
    /// <summary>
    /// Where the bodies of content live for physics: Unity's built-in <c>Ignore Raycast</c> layer. The ray from a hand or
    /// controller passes through them, so a body never blocks a panel's buttons and the ray never grabs or drags a body;
    /// hands still find them within reach. Handles and panels stay on their own layers, so the ray can use them.
    /// Clicks on parts still reach them through <see cref="ContentClickRaycaster"/>.
    /// </summary>
    public static class ContentLayer
    {
        /// <summary>The layer content bodies and parts are grabbed by (2, Ignore Raycast).</summary>
        public const int Body = 2;

        const int BodyBit = 1 << Body;

        /// <summary>
        /// Set up the rig's hands and controllers: their near reach finds content bodies, their rays pass through them.
        /// Returns how many interactors were set up.
        /// </summary>
        public static int ConfigureRig(XROrigin origin)
        {
            if (origin == null)
                return 0;
            var count = 0;
            foreach (var interactor in origin.GetComponentsInChildren<NearFarInteractor>(true))
            {
                if (interactor.nearInteractionCaster is SphereInteractionCaster near)
                    near.physicsLayerMask |= BodyBit;
                if (interactor.farInteractionCaster is CurveInteractionCaster far)
                    far.raycastMask &= ~BodyBit;
                count++;
            }

            foreach (var ray in origin.GetComponentsInChildren<XRRayInteractor>(true))
            {
                ray.raycastMask &= ~BodyBit;
                count++;
            }

            return count;
        }

        /// <summary>A child of <paramref name="owner"/> on the body layer, at its origin, to hold body colliders.</summary>
        public static GameObject CreateBody(Transform owner, string name = "Body")
        {
            var body = new GameObject(name) { layer = Body };
            body.transform.SetParent(owner, false);
            return body;
        }
    }
}

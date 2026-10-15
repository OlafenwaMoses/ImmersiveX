using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ImmersiveX
{
    /// <summary>
    /// Where a hand or controller really is, and where its ray points. Don't use an interactor's own transform for this: in the
    /// hand rigs the Near-Far interactor sits on a hand container that doesn't move; only its casters' origins (the pinch point
    /// and the aim pose) are tracked. With controllers these are the controller's own poses.
    /// </summary>
    public static class HandPose
    {
        /// <summary>The hand's grab point: a Near-Far interactor's near caster (the pinch point), else the attach point.</summary>
        public static Vector3 Position(IXRInteractor interactor)
        {
            if (interactor is IXRSelectInteractor selector)
                return DirectGrabOnly.ReachOrigin(selector, out _);
            return interactor.transform.position;
        }

        /// <summary>The hand's ray: a Near-Far interactor's far caster (the aim pose), a ray interactor's ray origin, else its transform.</summary>
        public static Ray Pointer(IXRInteractor interactor)
        {
            if (interactor is NearFarInteractor nearFar && nearFar.farInteractionCaster != null)
            {
                var origin = nearFar.farInteractionCaster.effectiveCastOrigin;
                if (origin != null)
                    return new Ray(origin.position, origin.forward);
            }

            if (interactor is XRRayInteractor ray && ray.rayOriginTransform != null)
                return new Ray(ray.rayOriginTransform.position, ray.rayOriginTransform.forward);
            var transform = interactor.transform;
            return new Ray(transform.position, transform.forward);
        }
    }
}

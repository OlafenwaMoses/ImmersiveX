using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace ImmersiveX
{
    /// <summary>
    /// ImmersiveX's interaction rule: the ray from a hand or controller only clicks. Picking something up, moving, turning or
    /// resizing it takes a direct grab, with the hand or controller at the object.
    /// <list type="bullet">
    /// <item>Near-Far interactors (the rig's hands and controllers) select only what's within their near reach.</item>
    /// <item>Direct, poke and socket interactors touch what they select, so they're unchanged.</item>
    /// <item>Every other interactor (rays, gaze, teleport and climb) never selects.</item>
    /// </list>
    /// Add it to an interactable with <see cref="Apply"/>. Clicks reach content through <see cref="ContentClickRaycaster"/>.
    /// </summary>
    public sealed class DirectGrabOnly : IXRSelectFilter
    {
        /// <summary>Extra reach around the near caster's sphere, in metres, so a grab at the surface isn't missed.</summary>
        const float Margin = 0.02f;

        /// <summary>Reach for near casters that aren't spheres, in metres.</summary>
        const float DefaultReach = 0.1f;

        static readonly DirectGrabOnly Shared = new DirectGrabOnly();
        static readonly Collider[] Overlaps = new Collider[64];
        static readonly List<Collider> TouchingList = new List<Collider>();

        public bool canProcess => true;

        /// <summary>
        /// Make <paramref name="interactable"/> selectable only by a direct grab, and never by a hand that's at a panel (see
        /// <see cref="PanelZones"/>). Safe to call more than once.
        /// </summary>
        public static void Apply(XRBaseInteractable interactable)
        {
            interactable.selectFilters.Remove(Shared);
            interactable.selectFilters.Add(Shared);
            PanelZoneFilter.Apply(interactable);
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            switch (interactor)
            {
                case NearFarInteractor nearFar:
                    return IsWithinReach(nearFar, interactable);
                default:
                    return Touches(interactor);
            }
        }

        /// <summary>True for interactors that only select what they touch: direct, poke and socket.</summary>
        public static bool Touches(IXRSelectInteractor interactor) =>
            interactor is XRDirectInteractor || interactor is XRPokeInteractor || interactor is XRSocketInteractor;

        /// <summary>True when one of <paramref name="interactable"/>'s colliders is within the interactor's near reach.</summary>
        public static bool IsWithinReach(NearFarInteractor interactor, IXRInteractable interactable)
        {
            if (!interactor.enableNearCasting || interactor.nearInteractionCaster == null)
                return false;

            var origin = ReachOrigin(interactor, out var reach);
            var colliders = interactable.colliders;
            foreach (var collider in Touching(origin, reach))
                if (colliders.Contains(collider))
                    return true;
            return false;
        }

        /// <summary>
        /// Where a hand or controller grabs from, and how far it reaches, in metres: a Near-Far interactor's near caster (its
        /// sphere), or the interactor's attach point for the others.
        /// </summary>
        public static Vector3 ReachOrigin(IXRSelectInteractor interactor, out float reach)
        {
            reach = DefaultReach;
            if (interactor is NearFarInteractor nearFar && nearFar.nearInteractionCaster != null)
            {
                var caster = nearFar.nearInteractionCaster;
                if (caster is SphereInteractionCaster sphere)
                    reach = sphere.castRadius * Mathf.Abs(sphere.transform.lossyScale.x);
                return (caster.effectiveCastOrigin != null ? caster.effectiveCastOrigin : nearFar.transform).position;
            }

            var attach = interactor.GetAttachTransform(null);
            return attach != null ? attach.position : interactor.transform.position;
        }

        /// <summary>
        /// The colliders within <paramref name="reach"/> of <paramref name="origin"/> (plus a small margin). Our own overlap:
        /// asking a caster would advance its frame-to-frame sweep. The list is reused by the next call.
        /// </summary>
        public static List<Collider> Touching(Vector3 origin, float reach)
        {
            TouchingList.Clear();
            var count = Physics.OverlapSphereNonAlloc(origin, reach + Margin, Overlaps, Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (var i = 0; i < count; i++)
                TouchingList.Add(Overlaps[i]);
            return TouchingList;
        }
    }
}

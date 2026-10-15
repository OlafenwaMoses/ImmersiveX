using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ImmersiveX
{
    /// <summary>
    /// Panels claim the space in front of and around them. While a hand or controller is there, ImmersiveX's grabbable things
    /// and handles ignore it (see <see cref="PanelZoneFilter"/>), so XR Interaction Toolkit lets that hand use the panel: its
    /// index finger presses the buttons (poke) and its ray clicks them.
    /// <para>Why: each hand's interactors share an interaction group in which only one acts at a time. Without zones, a hand
    /// reaching for a panel first comes within reach of something grabbable nearby (the move bar, the content itself), its
    /// near-far interactor starts hovering that, and the group then blocks the finger's poke and skips the ray's UI.</para>
    /// Every panel made with <see cref="WorldUi.CreateCanvas"/> is a zone.
    /// </summary>
    public static class PanelZones
    {
        /// <summary>How far around a panel's edges its zone reaches, in metres.</summary>
        public const float Margin = 0.02f;

        /// <summary>How far in front of and behind a panel its zone reaches, in metres.</summary>
        public const float Depth = 0.12f;

        static readonly List<RectTransform> Zones = new List<RectTransform>();

        public static int Count => Zones.Count;

        public static void Add(RectTransform panel)
        {
            if (panel != null && !Zones.Contains(panel))
                Zones.Add(panel);
        }

        public static void Remove(RectTransform panel) => Zones.Remove(panel);

        /// <summary>True when <paramref name="point"/> is in front of, behind or around a visible panel.</summary>
        public static bool Contains(Vector3 point)
        {
            for (var i = Zones.Count - 1; i >= 0; i--)
            {
                var panel = Zones[i];
                if (panel == null)
                {
                    Zones.RemoveAt(i);
                    continue;
                }

                if (!panel.gameObject.activeInHierarchy)
                    continue;
                var local = panel.InverseTransformPoint(point);
                var unit = Mathf.Max(Mathf.Abs(panel.lossyScale.x), 1e-6f); // metres per panel unit
                var margin = Margin / unit;
                var rect = panel.rect;
                if (local.x >= rect.xMin - margin && local.x <= rect.xMax + margin &&
                    local.y >= rect.yMin - margin && local.y <= rect.yMax + margin &&
                    Mathf.Abs(local.z) <= Depth / unit)
                    return true;
            }

            return false;
        }

        /// <summary>True when the hand or controller behind <paramref name="interactor"/> is at a panel.</summary>
        public static bool IsAtPanel(IXRInteractor interactor)
        {
            if (Zones.Count == 0 || interactor == null)
                return false;
            var origin = interactor is IXRSelectInteractor selector
                ? DirectGrabOnly.ReachOrigin(selector, out _)
                : interactor.transform.position;
            return Contains(origin);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Zones.Clear();
    }

    /// <summary>
    /// Keeps a hand that's at a panel (see <see cref="PanelZones"/>) from hovering or picking up the interactable it's on, so the
    /// panel gets that hand. Something already held stays held.
    /// </summary>
    public sealed class PanelZoneFilter : IXRHoverFilter, IXRSelectFilter
    {
        static readonly PanelZoneFilter Shared = new PanelZoneFilter();

        public bool canProcess => PanelZones.Count > 0;

        /// <summary>Add the filter to <paramref name="interactable"/> (safe to call more than once).</summary>
        public static void Apply(XRBaseInteractable interactable)
        {
            interactable.hoverFilters.Remove(Shared);
            interactable.hoverFilters.Add(Shared);
            interactable.selectFilters.Remove(Shared);
            interactable.selectFilters.Add(Shared);
        }

        public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable) => !PanelZones.IsAtPanel(interactor);

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) =>
            interactor.IsSelecting(interactable) || !PanelZones.IsAtPanel(interactor);
    }

    /// <summary>Makes its canvas a <see cref="PanelZones"/> zone while it's enabled.</summary>
    [AddComponentMenu("")]
    sealed class PanelZone : MonoBehaviour
    {
        void OnEnable() => PanelZones.Add((RectTransform)transform);

        void OnDisable() => PanelZones.Remove((RectTransform)transform);
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ImmersiveX
{
    /// <summary>
    /// Lets the ray from a hand or controller click content. Ray pointers (XR UI) send pointer events to objects with a
    /// <see cref="ContentClickTarget"/>; every other collider is ignored, so the box a piece of media is grabbed by never
    /// blocks the buttons of a panel in front of it. The ray clicks only: grabbing takes a direct grab (see
    /// <see cref="DirectGrabOnly"/>), and a finger's press is for panels. <see cref="ImmersiveXSession"/> adds one to the
    /// scene's Event System.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ImmersiveX/Interaction/Content Click Raycaster")]
    public sealed class ContentClickRaycaster : BaseRaycaster
    {
        const int MaxHits = 32;

        readonly RaycastHit[] _hits = new RaycastHit[MaxHits];
        Camera _camera;

        public override Camera eventCamera
        {
            get
            {
                if (_camera == null)
                {
                    var session = ImmersiveXSession.Instance;
                    _camera = session != null && session.Origin != null ? session.Origin.Camera : Camera.main;
                }

                return _camera;
            }
        }

        /// <summary>Add a raycaster to the scene's Event System, unless it has one. False when there's no Event System.</summary>
        public static bool Ensure()
        {
            var events = FindAnyObjectByType<EventSystem>();
            if (events == null)
                return false;
            if (events.GetComponent<ContentClickRaycaster>() == null)
                events.gameObject.AddComponent<ContentClickRaycaster>();
            return true;
        }

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            if (!(eventData is TrackedDeviceEventData tracked) || ContentClickTarget.Count == 0)
                return;
            if (tracked.interactor != null && tracked.interactor.TryGetUIModel(out var model) && model.interactionType == UIInteractionType.Poke)
                return; // fingers press panels; content is clicked by rays only (a press on a part's label never reaches the part behind it)

            var points = tracked.rayPoints;
            var travelled = 0f;
            for (var i = 1; i < points.Count; i++)
            {
                var from = points[i - 1];
                var to = points[i];
                var length = Vector3.Distance(from, to);
                if (length < 1e-5f)
                    continue;

                // Every layer: parts are on the body layer (ContentLayer), which the interactor's own ray skips.
                var count = Physics.RaycastNonAlloc(from, (to - from) / length, _hits, length, Physics.AllLayers, QueryTriggerInteraction.Collide);
                ContentClickTarget target = null;
                var nearest = default(RaycastHit);
                for (var h = 0; h < count; h++)
                {
                    var owner = ContentClickTarget.For(_hits[h].collider);
                    if (owner != null && (target == null || _hits[h].distance < nearest.distance))
                    {
                        target = owner;
                        nearest = _hits[h];
                    }
                }

                if (target != null)
                {
                    var camera = eventCamera;
                    resultAppendList.Add(new RaycastResult
                    {
                        gameObject = target.gameObject,
                        module = this,
                        distance = travelled + nearest.distance,
                        index = resultAppendList.Count,
                        worldPosition = nearest.point,
                        worldNormal = nearest.normal,
                        screenPosition = camera != null ? (Vector2)camera.WorldToScreenPoint(nearest.point) : Vector2.zero,
                    });
                    tracked.rayHitIndex = i;
                    return;
                }

                travelled += length;
            }
        }
    }
}

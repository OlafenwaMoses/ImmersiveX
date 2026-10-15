using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ImmersiveX
{
    /// <summary>
    /// The grab for every part of a <see cref="ContentParts"/>: one interactable owning all the part colliders, so nested
    /// parts never compete. A hand or controller at a part picks up that part (or the selected assembly holding it) and the
    /// part follows it rigidly until it's let go. Two hands can hold two parts. Rays never grab (<see cref="DirectGrabOnly"/>).
    /// </summary>
    [AddComponentMenu("")]
    sealed class PartsInteractable : XRBaseInteractable
    {
        struct Grip
        {
            public ContentPart Part;
            public Vector3 Position; // the part's pose in the hand's attach space when it was picked up
            public Quaternion Rotation;
        }

        internal ContentParts Parts;
        readonly Dictionary<IXRSelectInteractor, Grip> _grips = new Dictionary<IXRSelectInteractor, Grip>();

        protected override void Awake()
        {
            base.Awake();
            selectMode = InteractableSelectMode.Multiple;
            DirectGrabOnly.Apply(this);
        }

        /// <summary>Parts move only while they're movable (<see cref="ContentParts.Movable"/>: the content is paused).</summary>
        public override bool IsSelectableBy(IXRSelectInteractor interactor) => base.IsSelectableBy(interactor) && (Parts == null || Parts.Movable);

        /// <summary>Let go of every part being held.</summary>
        internal void ReleaseAll()
        {
            if (isSelected && interactionManager != null)
                interactionManager.CancelInteractableSelection((IXRSelectInteractable)this);
        }

        /// <summary>Use exactly <paramref name="partColliders"/> (registers again with the interaction manager when needed).</summary>
        internal void SetColliders(List<Collider> partColliders)
        {
            var manager = interactionManager;
            var registered = isActiveAndEnabled && manager != null && manager.IsRegistered((IXRInteractable)this);
            if (registered)
                manager.UnregisterInteractable((IXRInteractable)this);
            colliders.Clear();
            colliders.AddRange(partColliders);
            if (registered)
                manager.RegisterInteractable((IXRInteractable)this);
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            var interactor = args.interactorObject;
            var part = Parts != null ? Parts.GrabTargetFor(interactor) : null;
            if (part == null)
                return;

            // Another hand holding the same part lets go of it: the latest grab moves it.
            foreach (var other in new List<IXRSelectInteractor>(_grips.Keys))
                if (_grips[other].Part == part)
                    _grips.Remove(other);

            var attach = interactor.GetAttachTransform(this);
            var inverse = Quaternion.Inverse(attach.rotation);
            _grips[interactor] = new Grip
            {
                Part = part,
                Position = inverse * (part.transform.position - attach.position),
                Rotation = inverse * part.transform.rotation,
            };
            Parts.BeginHold(part);
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (!_grips.TryGetValue(args.interactorObject, out var grip))
                return;
            _grips.Remove(args.interactorObject);
            Parts.EndHold(grip.Part);
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (_grips.Count == 0 || Parts == null)
                return;
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic && updatePhase != XRInteractionUpdateOrder.UpdatePhase.OnBeforeRender)
                return;

            foreach (var pair in _grips)
            {
                var attach = pair.Key.GetAttachTransform(this);
                if (attach == null || pair.Value.Part == null)
                    continue;
                var grip = pair.Value;
                Parts.MoveTo(grip.Part, new Pose(attach.position + attach.rotation * grip.Position, attach.rotation * grip.Rotation));
            }
        }
    }
}

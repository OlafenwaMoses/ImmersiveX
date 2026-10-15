using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ImmersiveX
{
    /// <summary>
    /// Pointers (rays) by input: a tracked hand has none, unless Settings ▸ Hand Pointers is on, so hands press buttons with
    /// a finger and pick things up; controllers in hand have theirs. It holds every frame, before interaction runs and
    /// before the pointer line is drawn, so nothing in the rig turns a hand's pointer back on (the XRI hands rig re-enables
    /// far casting whenever a poke gesture ends). It covers both rig objects: the hands', and the controllers' when a
    /// tracked hand drives them (Meta's hand interaction profile).
    /// </summary>
    [DefaultExecutionOrder(-200)] // before the XR Interaction Manager (-105) and the pointer lines' LateUpdate
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class HandPointers : MonoBehaviour
    {
        readonly List<NearFarInteractor> _nearFar = new List<NearFarInteractor>();
        readonly List<XRRayInteractor> _rays = new List<XRRayInteractor>();
        readonly HashSet<Object> _handObjects = new HashSet<Object>(); // interactors on the rig's hand objects
        readonly HashSet<Object> _turnedOff = new HashSet<Object>(); // controller interactors this turned off, to turn back on
        readonly List<XRHandSubsystem> _subsystems = new List<XRHandSubsystem>();
        XRHandSubsystem _hands;
        float _nextLookup;

        /// <summary>Hands get no pointer unless <paramref name="handPointers"/>; controllers keep theirs. Returns the interactors watched.</summary>
        public static int Configure(XROrigin origin, bool handPointers)
        {
            if (origin == null)
                return 0;
            var existing = origin.GetComponent<HandPointers>();
            if (handPointers)
            {
                if (existing != null)
                    Destroy(existing);
                return 0;
            }

            var pointers = existing != null ? existing : origin.gameObject.AddComponent<HandPointers>();
            pointers.Collect(origin);
            return pointers._nearFar.Count + pointers._rays.Count;
        }

        void Collect(XROrigin origin)
        {
            _nearFar.Clear();
            _rays.Clear();
            _handObjects.Clear();
            origin.GetComponentsInChildren(true, _nearFar);
            origin.GetComponentsInChildren(true, _rays);

            var modality = origin.GetComponentInChildren<XRInputModalityManager>(true);
            if (modality == null)
                modality = FindAnyObjectByType<XRInputModalityManager>(FindObjectsInactive.Include);
            if (modality == null)
                return;
            foreach (var hand in new[] { modality.leftHand, modality.rightHand })
            {
                if (hand == null)
                    continue;
                foreach (var interactor in hand.GetComponentsInChildren<NearFarInteractor>(true))
                    _handObjects.Add(interactor);
                foreach (var ray in hand.GetComponentsInChildren<XRRayInteractor>(true))
                    _handObjects.Add(ray);
            }
        }

        void Update() => Apply();

        void LateUpdate() => Apply(); // again before the pointer lines are drawn

        void Apply()
        {
            var leftTracked = HandTracked(true);
            var rightTracked = HandTracked(false);
            foreach (var interactor in _nearFar)
            {
                if (interactor == null)
                    continue;
                if (IsHand(interactor, interactor.handedness, leftTracked, rightTracked))
                {
                    if (interactor.enableFarCasting)
                    {
                        interactor.enableFarCasting = false;
                        if (!_handObjects.Contains(interactor))
                            _turnedOff.Add(interactor);
                    }
                }
                else if (_turnedOff.Remove(interactor))
                {
                    interactor.enableFarCasting = true; // a controller in hand again: its pointer is back
                }
            }

            foreach (var ray in _rays)
            {
                if (ray == null)
                    continue;
                if (IsHand(ray, ray.handedness, leftTracked, rightTracked))
                {
                    if (ray.enabled)
                    {
                        ray.enabled = false;
                        if (!_handObjects.Contains(ray))
                            _turnedOff.Add(ray);
                    }
                }
                else if (_turnedOff.Remove(ray))
                {
                    ray.enabled = true;
                }
            }
        }

        /// <summary>Driven by a hand: on the rig's hand objects, or on its side while that hand is tracked.</summary>
        bool IsHand(Object interactor, InteractorHandedness handedness, bool leftTracked, bool rightTracked) =>
            _handObjects.Contains(interactor) ||
            handedness == InteractorHandedness.Left && leftTracked ||
            handedness == InteractorHandedness.Right && rightTracked;

        bool HandTracked(bool left)
        {
            if ((_hands == null || !_hands.running) && Time.unscaledTime >= _nextLookup)
            {
                _nextLookup = Time.unscaledTime + 1f;
                _subsystems.Clear();
                SubsystemManager.GetSubsystems(_subsystems);
                _hands = _subsystems.Find(s => s.running);
            }

            if (_hands == null || !_hands.running)
                return false;
            return left ? _hands.leftHand.isTracked : _hands.rightHand.isTracked;
        }

        void OnDestroy()
        {
            // Hand pointers turned on again (Settings): give back what was taken.
            foreach (var interactor in _nearFar)
                if (interactor != null && (_handObjects.Contains(interactor) || _turnedOff.Contains(interactor)))
                    interactor.enableFarCasting = true;
            foreach (var ray in _rays)
                if (ray != null && (_handObjects.Contains(ray) || _turnedOff.Contains(ray)))
                    ray.enabled = true;
        }
    }
}

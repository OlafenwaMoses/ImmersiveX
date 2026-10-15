using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace ImmersiveX.Tests
{
    public class DirectGrabTests
    {
        GameObject _hand;
        GameObject _thing;
        NearFarInteractor _nearFar;
        XRGrabInteractable _grab;

        [SetUp]
        public void SetUp()
        {
            _hand = new GameObject("Hand");
            _nearFar = _hand.AddComponent<NearFarInteractor>();
            var caster = _hand.AddComponent<SphereInteractionCaster>();
            caster.castOrigin = _hand.transform;
            caster.castRadius = 0.1f;
            _nearFar.nearInteractionCaster = caster;

            _thing = new GameObject("Thing", typeof(BoxCollider));
            _thing.GetComponent<BoxCollider>().size = Vector3.one * 0.1f;
            _grab = _thing.AddComponent<XRGrabInteractable>();
            _grab.colliders.Add(_thing.GetComponent<BoxCollider>()); // Awake doesn't run in Edit mode
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_hand);
            Object.DestroyImmediate(_thing);
        }

        bool CanGrabAt(Vector3 thingPosition)
        {
            _thing.transform.position = thingPosition;
            Physics.SyncTransforms();
            return new DirectGrabOnly().Process(_nearFar, _grab);
        }

        [Test]
        public void HandAtTheObject_Grabs()
        {
            Assert.IsTrue(CanGrabAt(new Vector3(0f, 0f, 0.12f)), "the box's near face is 7 cm away, inside the 10 cm reach");
            Assert.IsTrue(CanGrabAt(Vector3.zero), "the hand is inside it");
        }

        [Test]
        public void RayFromAfar_DoesNotGrab()
        {
            Assert.IsFalse(CanGrabAt(new Vector3(0f, 0f, 1.5f)), "a far select is the ray: it clicks, it doesn't grab");
        }

        [Test]
        public void WithoutNearCasting_DoesNotGrab()
        {
            _nearFar.enableNearCasting = false;
            Assert.IsFalse(CanGrabAt(Vector3.zero));
        }

        [Test]
        public void RayInteractor_NeverGrabs_DirectInteractor_Does()
        {
            var ray = new GameObject("Ray").AddComponent<XRRayInteractor>();
            var direct = new GameObject("Direct").AddComponent<XRDirectInteractor>();
            try
            {
                var rule = new DirectGrabOnly();
                Assert.IsFalse(rule.Process(ray, _grab));
                Assert.IsTrue(rule.Process(direct, _grab));
            }
            finally
            {
                Object.DestroyImmediate(ray.gameObject);
                Object.DestroyImmediate(direct.gameObject);
            }
        }

        [Test]
        public void Apply_AddsTheRulesOnce()
        {
            DirectGrabOnly.Apply(_grab);
            DirectGrabOnly.Apply(_grab);
            Assert.AreEqual(2, _grab.selectFilters.count, "the near-only rule and the panel-zone rule");
            Assert.AreEqual(1, _grab.hoverFilters.count, "the panel-zone rule");
        }

        [Test]
        public void HandAtAPanel_DoesNotGrabWhatsNearby()
        {
            // A 600 × 150 mm panel (1 unit = 1 mm) facing the hand, 5 cm in front of the thing.
            var panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            try
            {
                panel.sizeDelta = new Vector2(600f, 150f);
                panel.localScale = Vector3.one * 0.001f;
                panel.position = new Vector3(0f, 0f, 0.03f);
                PanelZones.Add(panel);
                _thing.transform.position = new Vector3(0f, 0f, 0.08f);
                Physics.SyncTransforms();
                Assert.IsTrue(PanelZones.Contains(_hand.transform.position), "the hand is 3 cm in front of the panel");
                Assert.IsFalse(new PanelZoneFilter().Process((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)_nearFar, _grab),
                    "within reach of the thing, but at the panel: the panel gets the hand");
                Assert.IsTrue(new DirectGrabOnly().Process(_nearFar, _grab), "without the panel it would have grabbed it");

                _hand.transform.position = new Vector3(0f, 0.3f, 0f);
                Assert.IsFalse(PanelZones.Contains(_hand.transform.position), "15 cm above the panel's top edge is outside its zone");
                _hand.transform.position = new Vector3(0f, 0f, 0.2f);
                Assert.IsFalse(PanelZones.Contains(_hand.transform.position), "17 cm behind the panel is outside its zone");
            }
            finally
            {
                PanelZones.Remove(panel);
                Object.DestroyImmediate(panel.gameObject);
                _hand.transform.position = Vector3.zero;
            }
        }
    }
}

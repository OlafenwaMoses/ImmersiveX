using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Media.Tests
{
    /// <summary>
    /// How a web page's mirrored scene meets the room (WebScene, fed the same messages the bridge sends): the page's ground
    /// is the floor and is hidden (a terrain, a studio floor), flat parts of the scene (a platform, an apron) stay, the ground
    /// stays the ground when something flies off, a scene with no ground stands where most of it rests, and what sinks
    /// under the floor is hidden.
    /// </summary>
    public class WebSceneTests
    {
        GameObject _parent;
        WebScene _scene;
        int _nextId = 100;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("Web scene test");
            _scene = new WebScene(_parent.transform);
            _scene.Apply("{\"t\":\"mat\",\"id\":1,\"type\":\"MeshStandardMaterial\",\"color\":[0.5,0.5,0.5],\"opacity\":1,\"transparent\":false,\"visible\":true}");
        }

        [TearDown]
        public void TearDown()
        {
            _scene.Dispose();
            UnityEngine.Object.DestroyImmediate(_parent);
        }

        /// <summary>An outdoor site: a 1 km terrain, a mast on a 20 m platform by a 150 m tower, an apron, small things about.</summary>
        void OutdoorSite()
        {
            Flat("terrain", 1000f, 0f);
            Flat("apron", 60f, 0.5f);
            Flat("platform", 20f, 20f);
            Box("tower", new Vector3(30f, 0f, 0f), new Vector3(10f, 150f, 10f));
            Box("mast", new Vector3(0f, 20f, 0f), new Vector3(9f, 120f, 9f));
            for (var i = 0; i < 8; i++)
                Box("crate " + i, new Vector3(-40f + i * 5f, 0f, 30f), new Vector3(2f, 2f, 2f));
        }

        [Test]
        public void Ground_IsTheWideFlatOne_NotThePlatformOrTheApron()
        {
            OutdoorSite();
            _scene.HideGround();

            Assert.IsFalse(Shown("terrain"), "The terrain is the ground: the room's floor shows instead.");
            Assert.IsTrue(Shown("platform"), "The platform the mast stands on is part of the scene.");
            Assert.IsTrue(Shown("apron"), "The apron is part of the scene.");
            Assert.IsTrue(_scene.TryGroundHeight(_parent.transform, -1f, 80f, out var ground));
            Assert.AreEqual(0f, ground, 1e-3f, "The ground's level is the floor's.");
        }

        [Test]
        public void StudioFloor_IsGroundToo()
        {
            OutdoorSite();
            Flat("studio floor", 600f, 0f);
            _scene.HideGround();

            Assert.IsFalse(Shown("studio floor"), "A floor far wider than the scene is tall is ground.");
            Assert.IsTrue(Shown("platform"));
        }

        [Test]
        public void Ground_StaysTheGround_WhenSomethingFliesOff()
        {
            OutdoorSite();
            _scene.HideGround();
            _scene.KeepRestHeight(); // placed

            Move("mast", new Vector3(0f, 60000f, 0f)); // 60 km up
            _scene.HideGround();

            Assert.IsFalse(Shown("terrain"), "The terrain stays hidden as the ground however high the mast goes.");
            Assert.IsTrue(_scene.TryGroundHeight(_parent.transform, -1f, 80f, out var ground));
            Assert.AreEqual(0f, ground, 1e-3f);
        }

        [Test]
        public void WithoutAGround_TheSceneStandsWhereMostOfItRests()
        {
            Box("trench", new Vector3(0f, -10f, 0f), new Vector3(8f, 10f, 8f)); // dug below the ground
            Box("hall", new Vector3(-50f, 0f, 0f), new Vector3(40f, 20f, 30f));
            Box("hangar", new Vector3(60f, 0f, 20f), new Vector3(50f, 25f, 40f));
            Box("tower", new Vector3(30f, 0f, 0f), new Vector3(10f, 150f, 10f));
            Box("mast", new Vector3(0f, 20f, 0f), new Vector3(9f, 120f, 9f));
            _scene.HideGround();

            Assert.IsFalse(_scene.TryGroundHeight(_parent.transform, -20f, 80f, out _), "There's no ground plane.");
            Assert.IsTrue(_scene.TryBounds(_parent.transform, out var bounds));
            Assert.IsTrue(_scene.TryContactLevel(_parent.transform, bounds, out var level));
            Assert.AreEqual(0f, level, 0.5f, "The level the hall, the hangar and the tower stand on, not the trench's bottom.");
        }

        [Test]
        public void UnderTheFloor_IsHidden_AndComesBack()
        {
            OutdoorSite();
            Box("pit", new Vector3(-20f, -10f, 0f), new Vector3(6f, 8f, 6f));
            _scene.HideBelow(0f);

            Assert.IsFalse(Shown("pit"), "Under the floor: hidden.");
            Assert.IsTrue(Shown("tower"));

            Move("pit", new Vector3(-20f, 5f, 0f));
            _scene.HideBelow(0f);
            Assert.IsTrue(Shown("pit"), "Back above the floor: shown again.");

            Move("pit", new Vector3(-20f, -10f, 0f));
            _scene.HideBelow(0f);
            _scene.ShowBelow();
            Assert.IsTrue(Shown("pit"), "Shown again to measure the whole scene.");
        }

        // ---------------------------------------------------------------- the bridge's messages

        readonly Dictionary<string, int> _ids = new Dictionary<string, int>();

        /// <summary>A box standing at <paramref name="foot"/> (the middle of its bottom).</summary>
        void Box(string name, Vector3 foot, Vector3 size)
        {
            var h = size * 0.5f;
            var corners = new Vector3[8];
            for (var i = 0; i < 8; i++)
                corners[i] = new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? 0f : size.y, (i & 4) == 0 ? -h.z : h.z);
            int[] faces =
            {
                0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3,
            };
            var vertices = new List<Vector3>();
            foreach (var index in faces)
                vertices.Add(corners[index]);
            Add(name, vertices, foot);
        }

        /// <summary>A flat square <paramref name="across"/> wide at height <paramref name="y"/>.</summary>
        void Flat(string name, float across, float y)
        {
            var h = across * 0.5f;
            var vertices = new List<Vector3>
            {
                new Vector3(-h, 0f, -h), new Vector3(h, 0f, -h), new Vector3(h, 0f, h),
                new Vector3(-h, 0f, -h), new Vector3(h, 0f, h), new Vector3(-h, 0f, h),
            };
            Add(name, vertices, new Vector3(0f, y, 0f));
        }

        void Add(string name, List<Vector3> vertices, Vector3 position)
        {
            var geometry = _nextId++;
            var id = _nextId++;
            _ids[name] = id;
            var floats = new float[vertices.Count * 3];
            for (var i = 0; i < vertices.Count; i++)
            {
                floats[i * 3] = vertices[i].x;
                floats[i * 3 + 1] = vertices[i].y;
                floats[i * 3 + 2] = vertices[i].z;
            }

            var bytes = new byte[floats.Length * 4];
            Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
            _scene.Apply($"{{\"t\":\"geo\",\"id\":{geometry},\"attrs\":{{\"position\":{{\"size\":3,\"count\":{vertices.Count},\"f32\":\"{Convert.ToBase64String(bytes)}\"}}}},\"groups\":[],\"range\":[0,-1]}}");
            _scene.Apply($"{{\"t\":\"frame\",\"n\":1,\"add\":[{{\"id\":{id},\"parent\":0,\"kind\":\"mesh\",\"name\":\"{name}\",\"geo\":{geometry},\"mats\":[1]}}],\"m\":[{Matrix(id, position)}]}}");
        }

        void Move(string name, Vector3 position) =>
            _scene.Apply($"{{\"t\":\"frame\",\"n\":2,\"m\":[{Matrix(_ids[name], position)}]}}");

        /// <summary>An id and a three.js matrix (column-major) that moves to <paramref name="p"/> (three.js coordinates).</summary>
        static string Matrix(int id, Vector3 p)
        {
            var e = new[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, p.x, p.y, p.z, 1f };
            var text = new StringBuilder(id.ToString(CultureInfo.InvariantCulture));
            foreach (var value in e)
                text.Append(',').Append(value.ToString("R", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        bool Shown(string name)
        {
            var thing = _scene.ObjectOf(_ids[name]);
            Assert.IsNotNull(thing, name);
            return thing.activeInHierarchy && thing.GetComponent<Renderer>().enabled;
        }
    }
}

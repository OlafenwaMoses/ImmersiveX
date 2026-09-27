using System.Collections;
using GLTFast;
using GLTFast.Logging;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A glTF/GLB model (glTFast), fitted to the media's height, standing on the floor and facing the user. Its first
    /// animation is the timeline: Play, Pause and the seek bar drive it. A model without animation is a still.
    /// </summary>
    sealed class ModelPlayable : IMediaPlayable
    {
        MediaContext _context;
        GltfImport _gltf;
        GameObject _model;
        Animation _animation;
        AnimationState _clip;
        Bounds _bounds;
        int _meshes;
        int _clips;
        bool _wantPlaying;

        public string Description => _model == null
            ? "Model"
            : $"Model · {_meshes} mesh{(_meshes == 1 ? string.Empty : "es")} · " +
              (_clip != null ? $"{_clips} animation{(_clips == 1 ? string.Empty : "s")}, playing '{_clip.name}'" : "no animation");

        public MediaPresentation Presentation => MediaPresentation.Standing;
        public bool IsLoaded => _model != null;
        public string Error { get; private set; }
        public double Duration => _clip != null ? _clip.length : 0d;

        public double Position
        {
            get
            {
                if (_clip == null || _clip.length <= 0f)
                    return 0d;
                return _context.Loop ? _clip.time % _clip.length : Mathf.Min(_clip.time, _clip.length);
            }
        }

        public bool IsBuffering => false;
        public float BufferProgress => 1f;
        public bool HasAudio => false;
        public Bounds Bounds => _bounds;

        public IEnumerator Load(MediaContext context)
        {
            _context = context;
            var logger = new ConsoleLogger();
            _gltf = new GltfImport(logger: logger);
            var settings = new ImportSettings
            {
                AnimationMethod = AnimationMethod.Legacy,
                GenerateMipMaps = true,
                AnisotropicFilterLevel = 4,
            };

            var load = _gltf.Load(context.Url, settings);
            while (!load.IsCompleted)
                yield return null;
            if (load.IsFaulted || !load.Result)
            {
                Error = $"Couldn't load the model ({load.Exception?.GetBaseException().Message ?? "see the Console"}).";
                yield break;
            }

            _model = new GameObject("Model");
            _model.transform.SetParent(context.Root, false);
            var instantiator = new GameObjectInstantiator(_gltf, _model.transform, logger, new InstantiationSettings
            {
                SceneObjectCreation = SceneObjectCreation.Always, // the same hierarchy whether the file has one root node or several
                SkinUpdateWhenOffscreen = true,
                Layer = context.Root.gameObject.layer,
            });
            var instantiate = _gltf.InstantiateMainSceneAsync(instantiator);
            while (!instantiate.IsCompleted)
                yield return null;
            if (instantiate.IsFaulted || !instantiate.Result)
            {
                Error = $"Couldn't build the model ({instantiate.Exception?.GetBaseException().Message ?? "see the Console"}).";
                yield break;
            }

            _animation = instantiator.SceneInstance?.LegacyAnimation;
            if (_animation != null && _animation.clip != null)
            {
                _animation.playAutomatically = false;
                foreach (AnimationState _ in _animation)
                    _clips++;
                _clip = _animation[_animation.clip.name];
                _clip.wrapMode = context.Loop ? WrapMode.Loop : WrapMode.ClampForever;
                _animation.Play(_clip.name);
                _clip.speed = 0f; // paused on the first frame until Play
                _clip.time = 0f;
                _animation.Sample();
            }

            _meshes = _model.GetComponentsInChildren<Renderer>().Length;
            Fit();
        }

        /// <summary>
        /// Height to the media's height, feet on the floor, centred, and turned to face the user (glTF models face +Z;
        /// the media's +Z points away from the user).
        /// </summary>
        void Fit()
        {
            var renderers = _model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                _bounds = new Bounds(new Vector3(0f, _context.Height * 0.5f, 0f), Vector3.one * 0.3f);
                return;
            }

            var root = _context.Root;
            _model.transform.localRotation = Quaternion.AngleAxis(180f, Vector3.up);
            var bounds = LocalBounds(renderers, root);
            var scale = _context.Height / Mathf.Max(bounds.size.y, 1e-4f);
            _model.transform.localScale *= scale;
            bounds = LocalBounds(renderers, root);
            _model.transform.localPosition += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            _bounds = LocalBounds(renderers, root);
        }

        /// <summary>The renderers' combined bounds in <paramref name="space"/>'s local coordinates.</summary>
        static Bounds LocalBounds(Renderer[] renderers, Transform space)
        {
            var bounds = new Bounds();
            var first = true;
            foreach (var renderer in renderers)
            {
                var world = renderer.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = world.center + Vector3.Scale(world.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var local = space.InverseTransformPoint(corner);
                    if (first)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(local);
                    }
                }
            }

            return bounds;
        }

        public void Play()
        {
            _wantPlaying = true;
            if (_clip == null)
                return;
            if (!_animation.IsPlaying(_clip.name))
                _animation.Play(_clip.name);
            _clip.speed = 1f;
        }

        public void Pause()
        {
            _wantPlaying = false;
            if (_clip != null)
                _clip.speed = 0f;
        }

        public void Seek(double seconds)
        {
            if (_clip == null)
                return;
            _clip.time = (float)seconds;
            _animation.Sample(); // show the pose now, even while paused
        }

        public void SetVolume(float volume) { }

        public void Tick(float deltaTime)
        {
            if (_clip != null && _wantPlaying && !_context.Loop && _clip.time >= _clip.length)
                _clip.speed = 0f; // stay on the last pose
        }

        public void LateTick(Transform viewer) { }

        public void Dispose()
        {
            if (_model != null)
                Object.Destroy(_model); // before the import: disposing it destroys the meshes and materials the model uses
            _gltf?.Dispose();
        }
    }
}

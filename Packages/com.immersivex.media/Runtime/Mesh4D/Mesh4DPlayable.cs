using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A 4D mesh (<see cref="Mesh4DContainer"/>, <c>.sp4m</c> or <c>.sp4m.gz</c>): a scene baked over time and played back
    /// as moving meshes coloured from the scene's own lighting (or, for a hologram, as light added over the room), with its
    /// soft effects as Gaussians, its soundtrack as the clock, and its captions and callouts shown in the room.
    /// <list type="bullet">
    /// <item>Placement follows the content's stage: its ground is the floor, its subject's centre stands at the media's
    /// place, the side its film camera watched from faces the user, and its height at the start is fitted to Height.</item>
    /// <item>It has the standard controls (play, pause, seek, volume) and handles; its parts can be selected and moved
    /// while it's paused, like a model's.</item>
    /// <item>When its subject drives or flies away (a car leaving, a rocket climbing), the stage follows it: the subject
    /// stays at the media's place at the same size, and what it leaves behind slides away and dissolves at the edge of
    /// the room, or sinks below the floor.</item>
    /// </list>
    /// </summary>
    sealed class Mesh4DPlayable : IMediaPlayable, IMediaAnnotations, IMediaParts
    {
        MediaContext _context;
        Mesh4DContainer _c;
        Mesh4DRenderer _renderer;
        IStreamAudio _audio;
        SequenceClock _clock;
        Bounds _bounds;
        Mesh4DLate _late;
        Mesh4DParts _parts;
        float _loadProgress;
        Vector3 _home; // the 4D scene's place in the media before any following
        Quaternion _turn;
        float _scale;
        Vector3 _subject0;
        float _followRadius, _followDeadZone;
        readonly List<MediaCallout> _callouts = new List<MediaCallout>();

        public string Description => _c == null
            ? "4D mesh"
            : $"4D mesh · {_c.ObjectCount} objects · {_renderer?.VisibleTriangles ?? _c.Triangles:N0} triangles · {(_c.IsHologram ? "hologram" : $"{_c.Probes:N0} colour probes")} · " +
              $"{_renderer?.EffectCount ?? 0:N0} effect Gaussians · audio {AudioState}";

        string AudioState => _audio == null ? "none" : _audio.Failed ? "unavailable" : !_audio.IsReady ? "loading" : _audio.IsPlaying ? "playing" : "ready";

        public MediaPresentation Presentation => MediaPresentation.Standing;
        public bool IsLoaded => _renderer != null;
        public string Error { get; private set; }
        public double Duration => _c?.Duration ?? 0d;
        public double Position => _clock?.Time ?? 0d;
        public bool IsBuffering => _renderer == null && _clock != null && _clock.WantPlaying;
        public float BufferProgress => _loadProgress;
        public bool HasAudio => _audio != null && !_audio.Failed;
        public Bounds Bounds => _bounds;
        public ContentParts Parts => _parts?.Parts;

        /// <summary>
        /// Where a container from <paramref name="url"/> is cached: one file per URL and app build, so a new build's content
        /// replaces the old copy (older copies are removed).
        /// </summary>
        static string CachePath(string url)
        {
            var folder = Path.Combine(Application.temporaryCachePath, "ImmersiveX", "Mesh4D");
            var name = $"{Hash(url)}-{Hash(Application.buildGUID + Application.version)}{(MediaSource.FileName(url).EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? ".sp4m.gz" : ".sp4m")}";
            if (Directory.Exists(folder))
                foreach (var old in Directory.GetFiles(folder, Hash(url) + "-*"))
                    if (Path.GetFileName(old) != name)
                        File.Delete(old);
            return Path.Combine(folder, name);
        }

        static string Hash(string text)
        {
            unchecked
            {
                var h = 2166136261u; // FNV-1a
                foreach (var c in text)
                    h = (h ^ c) * 16777619u;
                return h.ToString("x8");
            }
        }

        public IEnumerator Load(MediaContext context)
        {
            _context = context;
            byte[] bytes = context.Prefetched;
            string path = null;
            if (bytes == null && !MediaSource.TryLocalFile(context.Url, out path))
            {
                // Inside an APK (or on the web): copy the file to the cache once, then read it from there, so a large
                // container is never held in memory twice (downloaded, then unpacked).
                path = CachePath(context.Url);
                if (!File.Exists(path))
                {
                    var copying = Time.realtimeSinceStartup;
                    yield return MediaSource.FetchToFile(context.Url, path, e => Error = e, p => _loadProgress = p * 0.5f);
                    if (Error != null)
                        yield break;
                    ImmersiveXLog.Info($"4D mesh: copied {MediaSource.FileName(context.Url)} ({new FileInfo(path).Length / 1e6:0} MB) to the cache in {Time.realtimeSinceStartup - copying:0.0} s.");
                }
            }

            var started = Time.realtimeSinceStartup;
            var read = Task.Run(() => path != null ? Mesh4DContainer.ReadFile(path) : Mesh4DContainer.Read(bytes));
            while (!read.IsCompleted)
                yield return null;
            if (read.IsFaulted)
            {
                Error = $"Couldn't read the 4D mesh ({read.Exception?.GetBaseException().Message}).";
                yield break;
            }

            _c = read.Result;
            _loadProgress = 0.8f;
            ImmersiveXLog.Info($"4D mesh: '{_c.Title}' read in {Time.realtimeSinceStartup - started:0.0} s: {_c.ObjectCount} objects, {_c.Triangles:N0} triangles, " +
                               $"{(_c.IsHologram ? "the hologram look" : $"{_c.Probes:N0} probes × {_c.ColorKeys} colour keys")}, {_c.Frames} frames at {_c.Fps:0} fps ({MediaControls.FormatTime(_c.Duration)}), " +
                               $"{(_c.HasEffects ? "effects" : "no effects")}.");

            var atlasBoxes = context.SelectableParts && !_c.IsHologram ? Mesh4DParts.MeasureAtlasBoxes(_c) : null;
            try
            {
                _renderer = new Mesh4DRenderer(_c, context.Root, MediaAssets.Mesh4DMaterial, MediaAssets.Mesh4DPaintMaterial, MediaAssets.SplatMaterial,
                    _c.IsHologram ? MediaAssets.Mesh4DHologramMaterial : null);
            }
            catch (Exception exception)
            {
                Error = $"Couldn't set up the 4D mesh ({exception.Message}).";
                ImmersiveXLog.Error(exception.ToString());
                yield break;
            }

            Fit();
            if (!string.IsNullOrEmpty(_c.AudioFile))
                _audio = StreamAudio.Open(MediaSource.Relative(context.Url, _c.AudioFile), context.Loop, context.Host);
            _clock = new SequenceClock(_c.Duration, context.Loop, _audio);
            if (context.SelectableParts)
                _parts = Mesh4DParts.Create(_c, _renderer, atlasBoxes);
            _late = context.Host.AddComponent<Mesh4DLate>();
            _late.Playable = this;
            Show(0d, null);
            _loadProgress = 1f;
        }

        /// <summary>
        /// The content's stage to the room: its ground on the floor, its subject's centre at the media's place, its front
        /// (the side the viewer belongs on) towards the user, and its starting height fitted to Height. Its coordinates are
        /// right-handed, so z is mirrored into Unity's left-handed space.
        /// </summary>
        void Fit()
        {
            var scale = _context.Height / Mathf.Max(_c.StageHeight, 0.01f);
            var root = _renderer.Root;
            // three.js +z is towards its front; mirrored, that's Unity -z, which is towards the user here
            var front = new Vector3(_c.Front.x, 0f, -_c.Front.z);
            var turn = front.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(front.normalized, Vector3.back) : Quaternion.identity;
            root.localRotation = turn;
            root.localScale = new Vector3(scale, scale, -scale);
            root.localPosition = turn * new Vector3(-scale * _c.Centre.x, -scale * _c.Ground, scale * _c.Centre.y);
            _home = root.localPosition;
            _turn = turn;
            _scale = scale;
            _bounds = StageBounds();
            SetUpFollow();
        }

        /// <summary>
        /// The subject (the per-frame subject floats) is followed once it leaves. The content can say how (its header's
        /// <c>follow</c>); otherwise a subject that ends up more than one and a half stage heights from where it started is
        /// held within a quarter of a stage height of it, and the set dissolves two stage heights from it.
        /// </summary>
        void SetUpFollow()
        {
            const int subject = Mesh4DContainer.FloatsPerFrame - 3;
            var f = _c.FrameFloats;
            _subject0 = new Vector3(f[subject], f[subject + 1], f[subject + 2]);
            if (float.IsNaN(_subject0.x))
                return;
            if (_c.FollowRadius > 0f)
            {
                _followRadius = _c.FollowRadius;
                _followDeadZone = _c.FollowDeadZone;
                return;
            }

            var farthest = 0f;
            for (var i = 0; i < _c.Frames; i++)
            {
                var b = i * Mesh4DContainer.FloatsPerFrame + subject;
                var d = new Vector3(f[b], f[b + 1], f[b + 2]) - _subject0;
                if (!float.IsNaN(d.x))
                    farthest = Mathf.Max(farthest, d.magnitude);
            }

            if (farthest > 1.5f * _c.StageHeight)
            {
                _followRadius = 2f * _c.StageHeight;
                _followDeadZone = 0.25f * _c.StageHeight;
            }
        }

        /// <summary>Keep the subject at the media's place once it has left its dead zone, and let the set fall away.</summary>
        void Follow()
        {
            if (_followRadius <= 0f)
                return;
            const int subject = Mesh4DContainer.FloatsPerFrame - 3;
            var s = new Vector3(_renderer.Floats[subject], _renderer.Floats[subject + 1], _renderer.Floats[subject + 2]);
            var away = s - _subject0;
            var distance = away.magnitude;
            var offset = distance > _followDeadZone && !float.IsNaN(distance) ? away * (1f - _followDeadZone / distance) : Vector3.zero;
            // content z is mirrored into the room (scale -z), so its offset goes back with the opposite sign
            _renderer.Root.localPosition = _home + _turn * new Vector3(-_scale * offset.x, -_scale * offset.y, _scale * offset.z);
            if (offset.sqrMagnitude > 1e-8f)
                _renderer.Focus(new Vector2(s.x, s.z), _followRadius, _c.Ground + offset.y);
            else
                _renderer.Focus(Vector2.zero, 0f, _c.Ground);
        }

        /// <summary>The objects visible at the start, in the media's space (for grabbing and layout).</summary>
        Bounds StageBounds()
        {
            _renderer.Pose(0d);
            var root = _renderer.Root;
            var toRoot = root.parent.worldToLocalMatrix * root.localToWorldMatrix;
            var first = true;
            var bounds = new Bounds();
            for (var o = 0; o < _c.ObjectCount; o++)
            {
                if (!_renderer.Visible[o])
                    continue;
                var p = toRoot.MultiplyPoint3x4(_renderer.ObjectMatrix(o).MultiplyPoint3x4(Vector3.zero));
                if (first)
                {
                    bounds = new Bounds(p, Vector3.zero);
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(p);
                }
            }

            var height = _context.Height;
            bounds.Encapsulate(new Vector3(bounds.center.x, 0f, bounds.center.z));
            bounds.Encapsulate(new Vector3(bounds.center.x, height, bounds.center.z));
            if (_c.Footprint.x > 0f && _c.Footprint.y > 0f)
            {
                // the content says how wide it gets over the whole piece: the handles and the controls go around that
                var half = new Vector3(_c.Footprint.x, 0f, _c.Footprint.y) * (0.5f * root.localScale.x);
                for (var corner = 0; corner < 4; corner++)
                {
                    var p = root.localRotation * new Vector3((corner & 1) == 0 ? -half.x : half.x, 0f, (corner & 2) == 0 ? -half.z : half.z);
                    bounds.Encapsulate(root.localPosition + new Vector3(p.x, 0f, p.z) + root.localRotation * new Vector3(root.localScale.x * _c.Centre.x, 0f, -root.localScale.x * _c.Centre.y));
                }
            }

            return bounds;
        }

        public void Play() => _clock?.Play();

        public void Pause() => _clock?.Pause();

        public void Seek(double seconds)
        {
            _clock?.Seek(seconds);
            if (_renderer != null)
                Show(_clock.Time, null);
        }

        public void SetVolume(float volume) => _audio?.SetVolume(volume);

        public void Tick(float deltaTime)
        {
            if (_clock == null || _renderer == null)
                return;
            if (_clock.WantPlaying)
                _clock.Resume(); // everything is in memory: nothing to wait for
            _clock.Advance(deltaTime);
        }

        public void LateTick(Transform viewer)
        {
            if (_renderer == null)
                return;
            Show(_clock.Time, viewer);
        }

        void Show(double t, Transform viewer)
        {
            _renderer.Pose(t);
            Follow();
            _renderer.PoseEffects(t, viewer);
            _parts?.Pose();
            UpdateAnnotations(t);
        }

        /// <summary>After the content's parts have taken the user's moves (ContentParts runs at 900): upload the poses.</summary>
        internal void Commit()
        {
            if (_renderer == null)
                return;
            if (_parts != null)
            {
                _parts.Refresh();
                _renderer.Commit(_parts.OffsetOf);
            }
            else
                _renderer.Commit();
        }

        // ---------------------------------------------------------------- captions and callouts

        public string Caption { get; private set; }

        public IReadOnlyList<MediaCallout> Callouts => _callouts;

        void UpdateAnnotations(double t)
        {
            Caption = null;
            foreach (var line in _c.Lines)
            {
                if (t >= line.Start - 0.05 && t <= line.End + 0.35)
                {
                    Caption = line.Caption;
                    break;
                }
            }

            _callouts.Clear();
            foreach (var c in _c.Callouts)
            {
                var visibility = Window(t, c.In, c.In + 0.35, c.Out - 0.35, c.Out);
                if (visibility <= 0f)
                    continue;
                _callouts.Add(new MediaCallout
                {
                    Id = c.Id,
                    Title = c.Title,
                    Body = c.Body,
                    Stat = c.Stat,
                    Visibility = visibility,
                    Anchor = AnchorOf(c.Part),
                });
            }
        }

        Vector3 AnchorOf(string partName)
        {
            foreach (var p in _c.Parts)
            {
                if (p.Name != partName || p.AnchorObject < 0)
                    continue;
                var m = _parts != null ? _parts.CurrentMatrix(p.AnchorObject) : _renderer.ObjectMatrix(p.AnchorObject);
                return _renderer.Root.TransformPoint(m.MultiplyPoint3x4(p.AnchorLocal));
            }

            return _renderer.Root.position;
        }

        static float Window(double t, double a, double b, double c, double d)
        {
            if (t <= a || t >= d)
                return 0f;
            if (t < b)
                return Mathf.SmoothStep(0f, 1f, (float)((t - a) / (b - a)));
            if (t <= c)
                return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, (float)((t - c) / (d - c)));
        }

        /// <summary>The content's own clock (such as seconds to or from an event), for a readout; null when it has none now.</summary>
        public double? ClockTime => _c?.ClockAt(Position);

        public void Dispose()
        {
            if (_late != null)
                UnityEngine.Object.Destroy(_late);
            _parts?.Dispose();
            _renderer?.Dispose();
            _audio?.Dispose();
        }
    }

    /// <summary>Commits a 4D mesh's poses after ContentParts has applied the user's moves (order 900).</summary>
    [DefaultExecutionOrder(950)]
    sealed class Mesh4DLate : MonoBehaviour
    {
        internal Mesh4DPlayable Playable;

        void LateUpdate() => Playable?.Commit();
    }
}

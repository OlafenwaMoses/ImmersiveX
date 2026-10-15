using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A 4D mesh (<see cref="Mesh4DContainer"/>, <c>.sp4m</c> or <c>.sp4m.gz</c>): a scene baked over time and played back
    /// as moving meshes coloured from the scene's own lighting, with its soft effects as Gaussians, its soundtrack as the
    /// clock, and its captions and callouts shown in the room.
    /// <list type="bullet">
    /// <item>Placement follows the content's stage: its ground is the floor, its subject's centre stands at the media's
    /// place, the side its film camera watched from faces the user, and its height at the start is fitted to Height.</item>
    /// <item>It has the standard controls (play, pause, seek, volume) and handles; its parts can be selected and moved
    /// while it's paused, like a model's.</item>
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
        readonly List<MediaCallout> _callouts = new List<MediaCallout>();

        public string Description => _c == null
            ? "4D mesh"
            : $"4D mesh · {_c.ObjectCount} objects · {_renderer?.VisibleTriangles ?? _c.Triangles:N0} triangles · {_c.Probes:N0} colour probes · " +
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

        public IEnumerator Load(MediaContext context)
        {
            _context = context;
            byte[] bytes = context.Prefetched;
            if (bytes == null)
            {
                yield return MediaSource.Fetch(context.Url, (b, e) =>
                {
                    bytes = b;
                    Error = e;
                }, p => _loadProgress = p * 0.5f);
                if (Error != null)
                    yield break;
            }

            var started = Time.realtimeSinceStartup;
            var read = Task.Run(() => Mesh4DContainer.Read(bytes));
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
                               $"{_c.Probes:N0} probes × {_c.ColorKeys} colour keys, {_c.Frames} frames at {_c.Fps:0} fps ({MediaControls.FormatTime(_c.Duration)}), " +
                               $"{(_c.HasEffects ? "effects" : "no effects")}.");

            var atlasBoxes = context.SelectableParts ? Mesh4DParts.MeasureAtlasBoxes(_c) : null;
            try
            {
                _renderer = new Mesh4DRenderer(_c, context.Root, MediaAssets.Mesh4DMaterial, MediaAssets.Mesh4DPaintMaterial, MediaAssets.SplatMaterial);
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
            _bounds = StageBounds();
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

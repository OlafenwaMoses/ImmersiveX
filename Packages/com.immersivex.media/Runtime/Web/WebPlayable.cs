using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A web page with a three.js scene (GenXR experiences and other three.js pieces):
    /// <list type="bullet">
    /// <item>The page runs out of sight in a web view, as it is: its own timeline, narration and captions stay in charge.</item>
    /// <item>Its 3D scene is drawn natively in the room (<see cref="WebScene"/>), placed once by one rule for every page,
    /// when the page has finished loading and its scene stands still (pages race through moments of their show as they
    /// load; none of that is shown or measured): its ground on the room's floor (the page's own ground, kept even when the
    /// page hides it; else the level most of the scene stands on), what the page shows (what its camera looks at) at the
    /// placed content's centre, and as tall as the media's height. Then it plays as the page has it (something that flies
    /// away leaves the room). What's under the floor is hidden.</item>
    /// <item>Its own controls show on a panel beside it (<see cref="WebPanel"/>), so Immersive Media shows none.</item>
    /// <item>Hidden (the content menu shows something else), the page loads in the background, then waits where it is.</item>
    /// <item>Its actors are selectable parts: a ray click selects one, a hand picks it up.</item>
    /// </list>
    /// A page without a three.js scene shows on its panel only.
    /// </summary>
    sealed class WebPlayable : IMediaPlayable, IMediaParts, IMediaOwnControls
    {
        const int PageWidth = 1280;
        const int PageHeight = 720;
        const float SceneTimeoutSeconds = 45f;
        const float SettleSeconds = 2f; // a page that doesn't say when it's ready is placed once loaded and still this long…
        const float SettleAtMostSeconds = 20f; // …or this long after its scene appeared (pages that never stand still)
        const float HoldAfterSeconds = 15f; // a hidden page that never settles waits anyway after this long
        const float SubjectMaxAngle = 40f; // further from where the page's camera looks than this isn't what it shows

        MediaContext _context;
        IWebView _view;
        WebScene _scene;
        WebPanel _panel;
        Transform _fitted;
        ContentParts _parts;
        readonly Dictionary<string, ContentPart> _partsById = new Dictionary<string, ContentPart>();
        readonly HashSet<Renderer> _groundWithoutColliders = new HashSet<Renderer>();
        Bounds _bounds;
        bool _started;
        bool _hasScene;
        bool _isPlaced; // the scene is placed in the room (once per page load)
        float _sceneSince = -1f; // when the scene first appeared
        int _meshesSeen; // the scene's mesh count, and when it last changed
        float _meshesChangedAt;
        float _loadedAt; // when the page started loading
        float _nextGroundCheck;
        bool _shown = true; // the media is showing (not hidden by the content menu, a lost anchor, or before it's placed)
        bool _holding; // the page is waiting where it is

        // How the scene is placed (in the scene's space: page units).
        float _groundLevel; // on the room's floor
        bool _groundFromPage; // the page's own ground was seen: its level stays the ground, even when the page hides it
        string _centredOn = "the whole scene";

        public string Description => _scene == null || !_hasScene
            ? "Web page"
            : $"Web page · {_scene.MeshCount} meshes, {_scene.VertexCount:N0} vertices" + (_parts != null ? $" · {_parts.Parts.Count} parts" : string.Empty);

        public MediaPresentation Presentation => MediaPresentation.Standing;
        public bool IsLoaded => _fitted != null && (_hasScene || _view?.Frame != null);

        /// <summary>The page is playing: its sound or video plays, or its scene moved in the last moment.</summary>
        public bool IsPlaying => _scene != null && (_scene.MediaPlaying || _hasScene && Time.unscaledTime - _scene.LastChange < 1f);
        public string Error { get; private set; }
        public double Duration => 0d; // the page keeps its own time
        public double Position => 0d;
        public bool IsBuffering => false;
        public float BufferProgress => 1f;
        public bool HasAudio => false; // the page plays its own sound
        public Bounds Bounds => _bounds;
        public ContentParts Parts => _parts;

        public IEnumerator Load(MediaContext context)
        {
            _context = context;
            var url = WebViews.PageUrl(context.Url);
            _view = WebViews.Create(new WebViewOptions { Width = PageWidth, Height = PageHeight, BridgeScript = WebViews.BridgeScript }, out var error);
            if (_view == null)
            {
                Error = error;
                yield break;
            }

            _fitted = new GameObject("Web Content").transform;
            _fitted.SetParent(context.Root, false);
            _fitted.gameObject.SetActive(false); // shown once the scene is placed
            _bounds = new Bounds(new Vector3(0f, context.Height * 0.5f, 0f), new Vector3(0.6f, context.Height, 0.6f));
            NewScene();
            _view.Load(url);
            _loadedAt = Time.unscaledTime;
            ImmersiveXLog.Info($"Web page: loading {url}");

            _panel = WebPanel.Create(_view, new Vector2Int(PageWidth, PageHeight), context.Host.transform);
            _panel.Viewer = context.Viewer; // it stands beside the content, turned to the user, once the content is placed
            _panel.gameObject.SetActive(_shown);

            var waited = 0f;
            while (!_hasScene && _view.Error == null && waited < SceneTimeoutSeconds)
            {
                Pump();
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (_view.Error != null)
            {
                Error = _view.Error;
                yield break;
            }

            if (!_hasScene)
                ImmersiveXLog.Warn($"Web page: no three.js scene after {SceneTimeoutSeconds:0} s; it shows on its panel only.");
            else
                ImmersiveXLog.Info($"Web page: '{_scene.Title}' · three.js scene with {_scene.MeshCount} meshes, {_scene.VertexCount:N0} vertices.");
        }

        void NewScene()
        {
            _scene?.Dispose();
            _partsById.Clear();
            _groundWithoutColliders.Clear();
            _parts = null;
            _scene = new WebScene(_fitted);
            _scene.SelectablesChanged += UpdateParts;
            _hasScene = false;
            _isPlaced = false;
            _sceneSince = -1f;
            _meshesSeen = 0;
            _fitted.gameObject.SetActive(false);
            _groundFromPage = false;
            _centredOn = "the whole scene";
        }

        /// <summary>Take in the page's frames and the bridge's messages.</summary>
        void Pump()
        {
            _view.Update();
            while (_view.TryReceive(out var message))
            {
                if (message.Contains("\"t\":\"loaded\""))
                {
                    // The page (re)loaded: start the bridge; ImmersiveX draws the scene, the page shows only its controls.
                    if (_started)
                        NewScene();
                    _started = true;
                    _holding = false; // a new page starts running; hidden, it waits again once it has loaded
                    _loadedAt = Time.unscaledTime;
                    _view.Evaluate("window.__ixBridge && window.__ixBridge.command({t:'start', mirror:true, uiOnly:true})");
                    continue;
                }

                _scene.Apply(message);
            }

            if (!_hasScene && _scene.MeshCount > 0)
            {
                _hasScene = true;
                _sceneSince = Time.unscaledTime;
            }

            if (_scene.MeshCount != _meshesSeen)
            {
                _meshesSeen = _scene.MeshCount;
                _meshesChangedAt = Time.unscaledTime;
            }
        }

        /// <summary>
        /// The page has finished loading and its scene stands still: it said so (<c>window.__ready</c>), else its load event
        /// has fired and nothing has been added or moved for a moment. Pages pause for seconds while they build, so stillness
        /// alone isn't enough.
        /// </summary>
        bool Settled
        {
            get
            {
                if (!_hasScene)
                    return false;
                var still = Time.unscaledTime - _scene.LastChange;
                if (_scene.PageReady)
                    return still >= 0.3f; // its last frame is in
                return _scene.PageLoaded && still >= SettleSeconds && Time.unscaledTime - _meshesChangedAt >= SettleSeconds;
            }
        }

        public void Tick(float deltaTime)
        {
            if (_view == null || _scene == null)
                return;
            Pump();

            // Hidden: the page loads in the background (so it shows at once when picked), then waits where it is.
            if (!_shown && !_holding && (Settled || Time.unscaledTime - _loadedAt >= HoldAfterSeconds))
                Hold(true);
        }

        public void LateTick(Transform viewer)
        {
            if (_scene == null)
                return;
            // Pages race through moments of their show while they load (jumping between scenes, far away and back): place the
            // scene once, when it stands still, and show nothing before.
            // …and only once the media itself is showing in the room (before that its content is switched off, so nothing
            // in it can be measured).
            if (!_isPlaced && _hasScene && _context.Root.gameObject.activeInHierarchy && (Settled || Time.unscaledTime - _sceneSince >= SettleAtMostSeconds))
                PlaceOnce();
            if (!_isPlaced)
                return;

            if (Time.unscaledTime >= _nextGroundCheck)
            {
                _nextGroundCheck = Time.unscaledTime + 1f;
                _scene.HideGround(); // pages show things as they play: the ground can appear later
                RemoveGroundColliders();
            }

            _scene.HideBelow(_context.Root.position.y - 0.001f); // the room's floor is there: what's under it doesn't show through
            _scene.DrawInstances();
        }

        /// <summary>Place the scene once, as it stands now, and show it.</summary>
        void PlaceOnce()
        {
            _fitted.gameObject.SetActive(true);
            if (!Fit())
                return; // nothing drawn yet: try again next frame
            _isPlaced = true;
            _scene.KeepRestHeight(); // what flies off later doesn't change what's ground
            RemoveGroundColliders();
            var why = (_scene.PageReady ? "the page said it's ready" : _scene.PageLoaded && Settled ? "loaded and still" : "waited long enough") +
                      $"; {_scene.MeshCount} meshes, {_scene.Selectables.Count} selectables, camera {(_scene.HasCamera ? $"at {_scene.CameraPosition:F0} looking {_scene.CameraForward:F2}" : "not yet")}";
            foreach (var (id, root, title, _) in _scene.Selectables)
            {
                var node = _scene.ObjectOf(root);
                why += $"; {title} {(node == null ? "missing" : node.activeInHierarchy ? $"at {_scene.CentreOf(node.transform):F0}" : "hidden")}";
            }
            ImmersiveXLog.Info($"Web page: placed after {Time.unscaledTime - _sceneSince:0.0} s ({why}) · ×{_fitted.localScale.x:0.####} · centred on {_centredOn} · " +
                               $"ground at {_groundLevel:0.##} ({(_groundFromPage ? "the page's ground" : "where the scene stands")}).");
        }

        /// <summary>
        /// Place the scene (what's shown now, the ground plane left out): its ground on the room's floor, what the page shows at
        /// the placed content's centre, as tall as the media's height. False when nothing is drawn yet.
        /// </summary>
        bool Fit()
        {
            _fitted.localScale = Vector3.one;
            _fitted.localPosition = Vector3.zero;
            _scene.ShowBelow(); // measure everything; what's under the floor is hidden again right after
            if (!_scene.TryBounds(_context.Root, out var bounds) || bounds.size.y < 1e-4f)
                return false;

            // The ground: the page's own, kept once seen even if the page hides it later; else the level the scene stands on.
            if (_scene.TryGroundHeight(_context.Root, bounds.min.y, bounds.center.y, out var ground))
            {
                _groundLevel = ground;
                _groundFromPage = true;
            }
            else if (!_groundFromPage)
            {
                _groundLevel = _scene.TryContactLevel(_context.Root, bounds, out var standsOn) ? standsOn : bounds.min.y;
            }

            var subject = Subject();
            var middle = subject != null ? _scene.CentreOf(subject) : bounds.center;
            var footprint = subject != null && _scene.TryBoundsOf(subject, out var own) ? own.size : bounds.size;
            var scale = _context.Height / Mathf.Max(bounds.max.y - _groundLevel, 1e-4f);
            var position = new Vector3(-middle.x * scale, -_groundLevel * scale, -middle.z * scale);
            var size = new Vector3(Mathf.Clamp(footprint.x * scale, 0.6f, 4f), _context.Height, Mathf.Clamp(footprint.z * scale, 0.6f, 4f));
            _fitted.localScale = Vector3.one * scale;
            _fitted.localPosition = position;
            _bounds = new Bounds(new Vector3(0f, _context.Height * 0.5f, 0f), size);
            _centredOn = subject != null ? $"'{subject.name}'" : "the whole scene";
            return true;
        }

        /// <summary>What the page shows: of its selectable things, the one nearest to where its camera looks (null when none is near).</summary>
        Transform Subject()
        {
            if (!_scene.HasCamera)
                return null;
            Transform best = null;
            var bestAngle = SubjectMaxAngle;
            foreach (var (_, root, _, _) in _scene.Selectables)
            {
                var node = _scene.ObjectOf(root);
                if (node == null || !node.activeInHierarchy)
                    continue;
                var angle = Vector3.Angle(_scene.CameraForward, _scene.CentreOf(node.transform) - _scene.CameraPosition);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = node.transform;
                }
            }

            return best;
        }

        /// <summary>The page's actors (or top-level things) as selectable parts.</summary>
        void UpdateParts()
        {
            if (!_context.SelectableParts)
                return;
            if (_parts == null)
                _parts = _scene.Root.gameObject.AddComponent<ContentParts>();

            var wanted = new HashSet<string>();
            foreach (var (id, root, title, info) in _scene.Selectables)
            {
                var node = _scene.ObjectOf(root);
                if (node == null)
                    continue;
                wanted.Add(id);
                if (_partsById.TryGetValue(id, out var existing) && existing != null)
                {
                    existing.Title = title;
                    existing.Info = info;
                    continue;
                }

                var part = _parts.Add(node.transform, id, title, info, null);
                var renderers = new List<Renderer>();
                foreach (var renderer in node.GetComponentsInChildren<Renderer>(true))
                    if (!_scene.IsGround(renderer))
                        renderers.Add(renderer);
                _parts.AddColliders(part, renderers);
                _partsById[id] = part;
            }

            foreach (var id in new List<string>(_partsById.Keys))
            {
                var part = _partsById[id];
                if (wanted.Contains(id) || part != null && !part.gameObject.activeInHierarchy)
                    continue; // hidden by the page for now: it stays a part, so showing it again doesn't rebuild its colliders
                _parts.Remove(part);
                _partsById.Remove(id);
            }

            _parts.RefreshGrab();
        }

        /// <summary>
        /// The page's ground is the room's floor, not something to pick up: a part holding it (a platform standing on the
        /// ground) gets no collider for it, or a hand anywhere in the room would pick up the whole base.
        /// </summary>
        void RemoveGroundColliders()
        {
            if (_parts == null)
                return;
            foreach (var renderer in _scene.Ground)
                if (renderer != null && _groundWithoutColliders.Add(renderer))
                    _parts.RemoveCollidersOf(renderer);
        }

        /// <summary>
        /// Hidden (the content menu shows something else, its anchor is lost, or it isn't placed yet): its panel goes, and the
        /// page finishes loading, then waits where it is, sound and all (<see cref="Tick"/>). Shown again, it carries on.
        /// </summary>
        public void SetShown(bool shown)
        {
            if (_shown == shown)
                return;
            _shown = shown;
            if (_panel != null)
                _panel.gameObject.SetActive(shown);
            if (shown)
                Hold(false);
        }

        void Hold(bool hold)
        {
            if (_holding == hold || _view == null)
                return;
            _holding = hold;
            _view.Evaluate($"window.__ixBridge && window.__ixBridge.command({{t:'{(hold ? "hold" : "release")}'}})");
        }

        /// <summary>Run JavaScript in the page; what it returns is logged (automation: <c>media @n js &lt;code&gt;</c>).</summary>
        public void Script(string code) =>
            _view?.Evaluate($"window.__ixBridge && window.__ixBridge.command({{t:'eval', code:{MediaFolders.Quote(code)}}})");

        /// <summary>Press and release at <paramref name="pixel"/> on the page (automation: <c>media @n click x y</c>).</summary>
        public void Click(Vector2 pixel)
        {
            _view?.Pointer(WebPointerPhase.Down, pixel);
            _view?.Pointer(WebPointerPhase.Up, pixel);
        }

        /// <summary>Place the scene again as it stands now (automation: <c>media @n refit</c>, for diagnostics).</summary>
        public string Refit()
        {
            Fit();
            return Inspect();
        }

        /// <summary>
        /// Each part's colliders and how near they come to the page's panel and to the reach from the user's chest to it
        /// (automation: <c>media @n parts</c>, for diagnostics).
        /// </summary>
        public string DescribeParts(Transform viewer)
        {
            if (_parts == null || _panel == null)
                return "No parts or panel.";
            var text = new System.Text.StringBuilder();
            var panel = _panel.transform.position;
            var chest = viewer != null ? viewer.position - Vector3.up * 0.45f : panel;
            text.Append($"panel at {panel:F2}, chest at {chest:F2}; parts {(_parts.Movable ? "movable" : "can't move (playing)")}; ");
            foreach (var part in _parts.Parts)
            {
                if (part == null)
                    continue;
                var nearest = float.MaxValue;
                var colliders = part.GetComponentsInChildren<Collider>(true);
                var biggest = Vector3.zero;
                foreach (var collider in colliders)
                {
                    if (!collider.enabled || !collider.gameObject.activeInHierarchy)
                        continue;
                    var b = collider.bounds;
                    if (b.size.sqrMagnitude > biggest.sqrMagnitude)
                        biggest = b.size;
                    for (var t = 0f; t <= 1f; t += 0.05f)
                    {
                        var point = Vector3.Lerp(chest, panel, t);
                        nearest = Mathf.Min(nearest, Vector3.Distance(b.ClosestPoint(point), point));
                    }
                }

                text.Append($"{part.Title} ({(part.gameObject.activeInHierarchy ? "shown" : "hidden")}, {colliders.Length} colliders, biggest {biggest:F2}, " +
                            $"nearest to the reach {(nearest < float.MaxValue ? $"{nearest:0.00} m" : "-")}); ");
            }

            return text.ToString();
        }

        /// <summary>Where the mirrored objects called <paramref name="name"/> are (automation: <c>media @n node &lt;name&gt;</c>).</summary>
        public string InspectNode(string name) => _scene == null ? "No scene." : _scene.DescribeNode(name);

        /// <summary>The mirrored scene in detail, and how it's fitted (automation: <c>media @n inspect</c>).</summary>
        public string Inspect() =>
            _scene == null ? "No scene." : $"{_scene.Describe()} · fitted ×{(_fitted != null ? _fitted.localScale.x : 0f):0.####} · bounds {_bounds.size:F2}" +
                                           $" · centred on {_centredOn} · ground at {_groundLevel:0.##} ({(_groundFromPage ? "the page's ground" : "where the scene stands")})" +
                                           $" · {(_isPlaced ? "placed" : "not placed yet")} · panel {(_panel != null && _panel.gameObject.activeSelf ? "shown" : "hidden")}, page {(_holding ? "held" : "running")}";

        public void Play() { } // the page's own controls play it

        public void Pause() { }

        public void Seek(double seconds) { }

        public void SetVolume(float volume) { }

        public void Dispose()
        {
            _view?.Dispose();
            _scene?.Dispose();
            if (_panel != null)
                Object.Destroy(_panel.gameObject);
            if (_fitted != null)
                Object.Destroy(_fitted.gameObject);
        }
    }
}

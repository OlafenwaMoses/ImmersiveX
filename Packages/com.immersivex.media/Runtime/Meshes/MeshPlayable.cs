using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A mesh (<c>.obj</c>, <c>.ply</c> or <c>.stl</c>) or a mesh sequence (volumetric video as files), fitted to the media's
    /// height and standing on the floor.
    /// <list type="bullet">
    /// <item>Textures and vertex colours are drawn unlit: scans and captures already have their lighting.</item>
    /// <item>Parts with only a material colour, or no colour at all (a plain STL), are lit, so their shape shows.</item>
    /// <item>An OBJ with several materials gets one per usemtl group, from its MTL (colour and texture).</item>
    /// <item>Sequences keep the first frame's scale and position, so a performer can move within the capture.</item>
    /// </list>
    /// </summary>
    sealed class MeshPlayable : IMediaPlayable
    {
        const int MaxTexturesCached = 8;
        static readonly Color PlainColour = new Color(0.8f, 0.8f, 0.8f, 1f);

        readonly MediaSequence _sequence;
        readonly byte[] _firstBytes;
        readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, string> _materialTextures = new Dictionary<string, string>();
        MediaContext _context;
        GameObject _object;
        Mesh _mesh;
        Material _material;
        FrameWindow<MeshFrame> _window;
        SequenceClock _clock;
        IStreamAudio _audio;
        UpAxis _up;
        int _shown = -1;
        bool _fitted;
        Bounds _bounds;
        int _vertices;
        int _triangles;
        string _wantedTexture;
        Material[] _stillMaterials;

        public MeshPlayable(MediaSequence sequence, byte[] firstBytes)
        {
            _sequence = sequence;
            _firstBytes = firstBytes;
        }

        bool IsSequence => _sequence.Frames.Length > 1;

        public string Description => IsSequence
            ? $"Mesh sequence · {_sequence.Frames.Length} frames at {_sequence.Fps:0.#} fps · {_vertices:N0} vertices"
            : $"Mesh · {_vertices:N0} vertices, {_triangles:N0} triangles ({_sequence.Extension})";

        public MediaPresentation Presentation => MediaPresentation.Standing;
        public bool IsLoaded => _shown >= 0;
        public string Error { get; private set; }
        public double Duration => IsSequence ? _sequence.Duration : 0d;
        public double Position => _clock?.Time ?? 0d;
        public bool IsBuffering => _clock != null && _clock.WantPlaying && !_clock.Running;
        public float BufferProgress => _window == null ? 1f : Mathf.Clamp01(_window.BufferedAhead(FrameAt(Position), _context.Loop) / Mathf.Max(1f, Preroll));
        public bool HasAudio => _audio != null && !_audio.Failed;
        public Bounds Bounds => _bounds;

        float Preroll => Mathf.Min(_context.PrerollSeconds * _sequence.Fps, _sequence.Frames.Length - FrameAt(Position));

        int FrameAt(double seconds) => Mathf.Clamp((int)(seconds * _sequence.Fps), 0, _sequence.Frames.Length - 1);

        public IEnumerator Load(MediaContext context)
        {
            _context = context;
            _up = context.Up != UpAxis.Auto ? context.Up
                : _sequence.Up != UpAxis.Auto ? _sequence.Up
                : _sequence.Extension == ".stl" ? UpAxis.PositiveZ // CAD and 3D printing are z-up
                : UpAxis.PositiveY;
            _object = new GameObject(IsSequence ? "Mesh Sequence" : "Mesh", typeof(MeshFilter), typeof(MeshRenderer));
            _object.transform.SetParent(context.Root, false);
            _mesh = new Mesh { name = "Media Mesh", indexFormat = IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            _object.GetComponent<MeshFilter>().sharedMesh = _mesh;
            _material = new Material(MediaAssets.MeshMaterial) { name = "Volumetric Unlit (instance)" };
            var renderer = _object.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            var up = _up;
            if (!IsSequence)
            {
                var bytes = _firstBytes;
                if (bytes == null)
                    yield return MediaSource.Fetch(_sequence.Frames[0], (b, e) =>
                    {
                        bytes = b;
                        Error = e;
                    });
                if (Error != null)
                    yield break;

                var extension = _sequence.Extension;
                var task = Task.Run(() => MeshDecoders.Decode(bytes, extension, up));
                while (!task.IsCompleted)
                    yield return null;
                if (task.IsFaulted)
                {
                    Error = $"Couldn't read '{MediaSource.FileName(_sequence.Frames[0])}': {task.Exception?.GetBaseException().Message}";
                    yield break;
                }

                Show(0, task.Result);
                yield break;
            }

            var ahead = Mathf.CeilToInt(Mathf.Min(context.BufferSeconds, 2f) * _sequence.Fps);
            _window = new FrameWindow<MeshFrame>(_sequence.Frames,
                (bytes, index) => MeshDecoders.Decode(bytes, MediaSource.Extension(_sequence.Frames[index]), up),
                ahead, Mathf.Min(context.ParallelDownloads, 8));
            if (!string.IsNullOrEmpty(_sequence.AudioUrl))
                _audio = StreamAudio.Open(_sequence.AudioUrl, context.Loop, context.Host);
            _clock = new SequenceClock(_sequence.Duration, context.Loop, _audio);

            // Wait for the first frame so there's something to show.
            var waited = 0f;
            while (_shown < 0 && waited < 60f)
            {
                _window.Update(0, context.Loop);
                if (_window.TryGet(0, out var first))
                    Show(0, first);
                else if (_window.Failures > 0 && waited > 10f)
                    break;
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (_shown < 0)
                Error = $"The sequence's first frame didn't load ({_window.LastError ?? "timed out"}).";
        }

        public void Play() => _clock?.Play();
        public void Pause() => _clock?.Pause();

        public void Seek(double seconds)
        {
            if (_clock == null)
                return;
            _clock.Seek(seconds);
            _window.Retarget(FrameAt(_clock.Time), _context.Loop);
        }

        public void SetVolume(float volume) => _audio?.SetVolume(volume);

        public void Tick(float deltaTime)
        {
            if (_clock == null)
                return;
            var frame = FrameAt(_clock.Time);
            _window.Update(frame, _context.Loop);
            if (_clock.WantPlaying && !_clock.Running && _window.BufferedAhead(frame, _context.Loop) >= Mathf.Max(1f, Preroll))
                _clock.Resume();
            _clock.Advance(deltaTime);
            frame = FrameAt(_clock.Time);
            if (frame == _shown)
                return;
            if (_window.TryGet(frame, out var mesh))
                Show(frame, mesh);
            else if (_clock.Running)
                _clock.Halt(); // wait for the frame
        }

        public void LateTick(Transform viewer) { }

        void Show(int index, MeshFrame frame)
        {
            _shown = index;
            _mesh.Clear();
            _mesh.SetVertices(frame.Positions);
            if (frame.Uvs != null)
                _mesh.SetUVs(0, frame.Uvs);
            if (frame.Colors != null)
                _mesh.SetColors(frame.Colors);
            _vertices = frame.Positions.Length;
            _triangles = frame.Triangles.Length / 3;

            if (!_fitted)
            {
                Fit(frame.Bounds);
                _fitted = true;
            }

            var frameUrl = _sequence.Frames[index];
            if (!IsSequence)
            {
                ShowStill(frame, frameUrl);
                _mesh.bounds = frame.Bounds;
                return;
            }

            _mesh.SetTriangles(frame.Triangles, 0, calculateBounds: false);
            _mesh.bounds = frame.Bounds;
            _material.SetFloat("_UseVertexColor", frame.Colors != null ? 1f : 0f);
            if (frame.Texture != null)
                UseTexture(MediaSource.Relative(frameUrl, frame.Texture));
            else if (frame.MaterialLibrary != null)
                _context.StartRoutine(ResolveObjTexture(frameUrl, frame.MaterialLibrary, frame.Material));
            else
                _material.mainTexture = null;
        }

        /// <summary>
        /// A single mesh: one submesh and material per OBJ material. Until the MTL is read, parts with a texture or
        /// vertex colours draw unlit and the rest lit in a neutral grey.
        /// </summary>
        void ShowStill(MeshFrame frame, string frameUrl)
        {
            var parts = frame.Parts ?? new[] { new MeshPart { Material = frame.Material, Triangles = frame.Triangles } };
            _mesh.subMeshCount = parts.Length;
            for (var i = 0; i < parts.Length; i++)
                _mesh.SetTriangles(parts[i].Triangles, i, calculateBounds: false);

            var unlit = frame.Colors != null || frame.Texture != null;
            var materials = new Material[parts.Length];
            for (var i = 0; i < parts.Length; i++)
                materials[i] = unlit ? Unlit(frame.Colors != null, Color.white) : Lit(PlainColour);
            SetStillMaterials(materials, anyLit: !unlit);

            if (frame.Texture != null)
                UseTexture(MediaSource.Relative(frameUrl, frame.Texture));
            else if (frame.MaterialLibrary != null)
                _context.StartRoutine(ResolveObjMaterials(frameUrl, frame.MaterialLibrary, parts, frame.Colors != null));
        }

        /// <summary>Each OBJ part's material from the MTL: its texture unlit, else its colour lit (or times the vertex colours).</summary>
        IEnumerator ResolveObjMaterials(string frameUrl, string library, MeshPart[] parts, bool vertexColours)
        {
            var mtlUrl = MediaSource.Relative(frameUrl, library);
            string text = null;
            yield return MediaSource.FetchText(mtlUrl, (t, error) =>
            {
                text = t;
                if (error != null)
                    ImmersiveXLog.Warn($"The OBJ's material library didn't load: {error}");
            });
            if (text == null || _object == null)
                yield break;

            var materialsByName = new Dictionary<string, MtlMaterial>();
            MtlMaterial first = null;
            foreach (var pair in MeshDecoders.Mtl(text))
            {
                materialsByName[pair.Key] = pair.Value;
                first ??= pair.Value;
            }

            var materials = new Material[parts.Length];
            var anyLit = false;
            for (var i = 0; i < parts.Length; i++)
            {
                // An OBJ that never says usemtl uses its library's (first) material.
                if (!materialsByName.TryGetValue(parts[i].Material ?? string.Empty, out var mtl) && string.IsNullOrEmpty(parts[i].Material))
                    mtl = first;
                if (mtl?.Texture != null)
                {
                    materials[i] = Unlit(vertexColours, Color.white);
                    _context.StartRoutine(LoadTextureInto(MediaSource.Relative(mtlUrl, mtl.Texture), materials[i]));
                }
                else if (vertexColours)
                {
                    materials[i] = Unlit(true, mtl?.Diffuse ?? Color.white);
                }
                else
                {
                    materials[i] = Lit(mtl != null ? mtl.Diffuse : PlainColour);
                    anyLit = true;
                }
            }

            SetStillMaterials(materials, anyLit);
        }

        void SetStillMaterials(Material[] materials, bool anyLit)
        {
            if (_stillMaterials != null)
                foreach (var old in _stillMaterials)
                    if (old != null && old != _material)
                        Object.Destroy(old);
            _stillMaterials = materials;
            if (anyLit)
                _mesh.RecalculateNormals();
            _object.GetComponent<MeshRenderer>().sharedMaterials = materials;
            if (materials.Length > 0 && materials[0] != _material)
            {
                Object.Destroy(_material);
                _material = materials[0]; // UseTexture sets the first material's texture
            }
        }

        static Material Unlit(bool vertexColours, Color colour)
        {
            var material = new Material(MediaAssets.MeshMaterial) { name = "Volumetric Unlit (instance)" };
            material.SetFloat("_UseVertexColor", vertexColours ? 1f : 0f);
            material.SetColor("_BaseColor", colour);
            return material;
        }

        static Material Lit(Color colour)
        {
            var template = MediaAssets.MeshLitMaterial;
            var material = template != null ? new Material(template) { name = "Mesh Lit (instance)" } : RuntimeMaterials.Create(colour);
            material.SetColor("_BaseColor", colour);
            return material;
        }

        IEnumerator LoadTextureInto(string url, Material material)
        {
            if (!_textures.TryGetValue(url, out var texture) || texture == null)
            {
                yield return MediaSource.FetchTexture(url, (t, error) =>
                {
                    texture = t;
                    if (error != null)
                        ImmersiveXLog.Warn($"Mesh texture didn't load: {error}");
                }, mipmaps: true);
                if (texture != null)
                {
                    texture.wrapMode = TextureWrapMode.Repeat; // OBJ models tile their textures
                    _textures[url] = texture;
                }
            }

            if (material != null && texture != null)
                material.mainTexture = texture;
        }

        /// <summary>Scale to the media's height, feet on the floor, centred.</summary>
        void Fit(Bounds bounds)
        {
            var scale = _context.Height / Mathf.Max(bounds.size.y, 1e-4f);
            _object.transform.localScale = Vector3.one * scale;
            _object.transform.localPosition = new Vector3(-bounds.center.x * scale, -bounds.min.y * scale, -bounds.center.z * scale);
            _bounds = new Bounds(new Vector3(0f, _context.Height * 0.5f, 0f), bounds.size * scale);
        }

        IEnumerator ResolveObjTexture(string frameUrl, string library, string material)
        {
            var mtlUrl = MediaSource.Relative(frameUrl, library);
            var key = mtlUrl + "#" + material;
            if (!_materialTextures.TryGetValue(key, out var textureUrl))
            {
                string mtl = null;
                yield return MediaSource.FetchText(mtlUrl, (text, error) => mtl = text);
                var file = mtl != null ? MeshDecoders.MtlTexture(mtl, material) : null;
                textureUrl = file != null ? MediaSource.Relative(mtlUrl, file) : string.Empty;
                _materialTextures[key] = textureUrl;
            }

            if (textureUrl.Length > 0)
                UseTexture(textureUrl);
        }

        void UseTexture(string url)
        {
            _wantedTexture = url;
            if (_textures.TryGetValue(url, out var texture))
            {
                _material.mainTexture = texture;
                return;
            }

            _context.StartRoutine(LoadTexture(url));
        }

        IEnumerator LoadTexture(string url)
        {
            _textures[url] = null; // loading
            Texture2D texture = null;
            yield return MediaSource.FetchTexture(url, (t, error) =>
            {
                texture = t;
                if (error != null)
                    ImmersiveXLog.Warn($"Mesh texture didn't load: {error}");
            });
            if (_material == null)
            {
                if (texture != null)
                    Object.Destroy(texture);
                yield break;
            }

            if (_textures.Count > MaxTexturesCached)
            {
                foreach (var pair in _textures)
                    if (pair.Value != null && pair.Key != url && pair.Key != _wantedTexture)
                        Object.Destroy(pair.Value);
                _textures.Clear();
            }

            _textures[url] = texture;
            if (texture != null)
                texture.wrapMode = TextureWrapMode.Clamp;
            if (_wantedTexture == url)
                _material.mainTexture = texture;
        }

        public void Dispose()
        {
            _window?.Dispose();
            _audio?.Dispose();
            foreach (var texture in _textures.Values)
                if (texture != null)
                    Object.Destroy(texture);
            if (_object != null)
                Object.Destroy(_object);
            if (_mesh != null)
                Object.Destroy(_mesh);
            if (_stillMaterials != null)
                foreach (var material in _stillMaterials)
                    if (material != null && material != _material)
                        Object.Destroy(material);
            if (_material != null)
                Object.Destroy(_material);
        }
    }
}

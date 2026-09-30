using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImmersiveX.Media.Editor
{
    /// <summary>
    /// Adds content to the app without code. It takes a file, a folder or a URL and adds it to the open scene as Immersive
    /// Media:
    /// <list type="bullet">
    /// <item>A URL is played from the internet.</item>
    /// <item>A file or folder already in StreamingAssets is used where it is.</item>
    /// <item>Anything else is copied into <c>StreamingAssets/ImmersiveXContent/&lt;name&gt;/</c> with the files it needs
    /// (an OBJ's MTL and textures, a glTF's buffers and images, a PLY's texture), so it ships inside the app.</item>
    /// <item>A folder of splat frames (a 4D capture) is packed into a hologram stream for the headset
    /// (<see cref="SplatSequencePacker"/>).</item>
    /// </list>
    /// </summary>
    public static class MediaImport
    {
        public const string ContentFolder = "ImmersiveXContent";

        public sealed class Options
        {
            public string Title;
            public MediaFormat Format = MediaFormat.Auto;
            public StereoLayout Stereo = StereoLayout.Auto;
            public UpAxis Up = UpAxis.Auto;

            /// <summary>Frames per second for a folder of frames.</summary>
            public float FrameRate = 30f;

            /// <summary>Pack a folder of splat frames into a stream (off: copy the frames as they are).</summary>
            public bool PackSplats = true;

            /// <summary>Most Gaussians kept per packed frame; 0 keeps them all.</summary>
            public int MaxGaussians;
        }

        /// <summary>One line saying what adding <paramref name="source"/> will do.</summary>
        public static string Describe(string source, out bool isFolder, out bool isSplatFrames)
        {
            isFolder = false;
            isSplatFrames = false;
            source = Clean(source);
            if (source.Length == 0)
                return "Pick a file or a folder, or paste a URL.";
            if (source.Contains("://"))
                return "A URL: it plays from the internet (the headset needs a connection).";
            var path = FullPath(source);
            if (Directory.Exists(path))
            {
                isFolder = true;
                var frames = SplatSequencePacker.Frames(path);
                isSplatFrames = frames.Length > 0;
                if (File.Exists(Path.Combine(path, "stream.json")))
                    return "A hologram stream folder: " + (InStreamingAssets(path) ? "used where it is." : "copied into StreamingAssets.");
                if (isSplatFrames)
                    return $"{frames.Length} splat frames ({Path.GetExtension(frames[0])}): packed into a stream for the headset.";
                var mediaFrames = Directory.GetFiles(path).Count(f => MediaSequence.FrameExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                return mediaFrames > 0 ? $"A folder of {mediaFrames} frames: played as a sequence." : "This folder has no frames ImmersiveX can play.";
            }

            if (!File.Exists(path))
                return "Not found.";
            var extras = Sidecars(path);
            return (InStreamingAssets(path) ? "Used where it is in StreamingAssets." : "Copied into StreamingAssets") +
                   (extras.Count > 0 && !InStreamingAssets(path) ? $" with {extras.Count} file(s) it needs ({string.Join(", ", extras.Take(3))}{(extras.Count > 3 ? "…" : string.Empty)})." : ".");
        }

        /// <summary>
        /// Make <paramref name="source"/> playable from inside the app and add it to the open scene. Returns the new Immersive
        /// Media. <paramref name="progress"/> gets 0–1 and returns false to cancel (packing only).
        /// </summary>
        public static ImmersiveMedia Add(string source, Options options, Func<float, bool> progress = null)
        {
            source = Clean(source);
            if (source.Length == 0)
                throw new ArgumentException("There's no source.");
            options ??= new Options();
            var title = string.IsNullOrWhiteSpace(options.Title) ? TitleFor(source) : options.Title.Trim();
            string mediaSource;
            float height = 0f;
            var frameRate = 0f;

            if (source.Contains("://"))
            {
                mediaSource = source;
            }
            else
            {
                var path = FullPath(source);
                if (Directory.Exists(path))
                {
                    var frames = SplatSequencePacker.Frames(path);
                    if (frames.Length > 0 && options.PackSplats && !File.Exists(Path.Combine(path, "stream.json")))
                    {
                        var output = Path.Combine(Application.streamingAssetsPath, ContentFolder, SafeName(title));
                        var result = SplatSequencePacker.Pack(frames, output, options.FrameRate, options.Up, options.MaxGaussians, title, progress);
                        mediaSource = Relative(result.Manifest);
                        var fitHeight = result.Fit.Height;
                        if (fitHeight >= 0.2f && fitHeight <= 4f)
                            height = fitHeight; // captures are in metres: keep the real size
                        ImmersiveXLog.Info($"Packed {result.Frames} frames ({result.MaxGaussians:N0} Gaussians at most) into {mediaSource}: " +
                                           $"{result.Bytes / 1e6:0} MB, {result.MegabytesPerSecond:0.#} MB/s to play.");
                    }
                    else
                    {
                        mediaSource = Relative(InStreamingAssets(path) ? path : CopyFolder(path, SafeName(title)));
                        frameRate = options.FrameRate;
                    }
                }
                else if (File.Exists(path))
                {
                    mediaSource = Relative(InStreamingAssets(path) ? path : CopyFile(path, SafeName(title)));
                }
                else
                {
                    throw new FileNotFoundException($"'{source}' wasn't found.");
                }

                AssetDatabase.Refresh();
            }

            var media = new GameObject(title).AddComponent<ImmersiveMedia>();
            Undo.RegisterCreatedObjectUndo(media.gameObject, "Add Media");
            var serialized = new SerializedObject(media);
            serialized.FindProperty("_source").stringValue = mediaSource;
            serialized.FindProperty("_title").stringValue = title;
            serialized.FindProperty("_format").intValue = (int)options.Format;
            serialized.FindProperty("_stereo").intValue = (int)options.Stereo;
            serialized.FindProperty("_up").intValue = (int)options.Up;
            if (frameRate > 0f)
                serialized.FindProperty("_frameRate").floatValue = frameRate;
            if (height > 0f)
                serialized.FindProperty("_height").floatValue = (float)Math.Round(height, 2);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(media.gameObject.scene);
            Selection.activeGameObject = media.gameObject;
            ImmersiveXLog.Info($"Added '{title}' to {media.gameObject.scene.name} (Source: {mediaSource}). Save the scene to keep it.");
            return media;
        }

        /// <summary>
        /// The files <paramref name="file"/> needs next to it, relative to its folder: an OBJ's MTL and textures, a glTF's
        /// buffers and images, a PLY's texture. Only files that exist are listed.
        /// </summary>
        public static List<string> Sidecars(string file)
        {
            var folder = Path.GetDirectoryName(file) ?? ".";
            var names = new List<string>();
            try
            {
                switch (Path.GetExtension(file).ToLowerInvariant())
                {
                    case ".obj":
                        var library = MeshDecoders.MtlLibrary(File.ReadAllText(file));
                        if (library != null)
                        {
                            names.Add(library);
                            var mtl = Path.Combine(folder, library);
                            if (File.Exists(mtl))
                            {
                                var mtlFolder = Path.GetDirectoryName(library) ?? string.Empty;
                                names.AddRange(MeshDecoders.MtlTextures(File.ReadAllText(mtl)).Select(t => Path.Combine(mtlFolder, t)));
                            }
                        }

                        break;
                    case ".gltf":
                        if (Json.Parse(File.ReadAllText(file)) is Dictionary<string, object> gltf)
                            foreach (var key in new[] { "buffers", "images" })
                                if (gltf.TryGetValue(key, out var list) && list is List<object> items)
                                    foreach (var item in items.OfType<Dictionary<string, object>>())
                                        if (item.TryGetValue("uri", out var uri) && uri is string u && !u.StartsWith("data:", StringComparison.Ordinal))
                                            names.Add(Uri.UnescapeDataString(u));
                        break;
                    case ".ply":
                        using (var stream = File.OpenRead(file))
                        {
                            var header = new byte[(int)Math.Min(stream.Length, 64 * 1024)];
                            stream.Read(header, 0, header.Length);
                            var texture = PlyFile.Parse(header).TextureFile;
                            if (texture != null)
                                names.Add(texture);
                        }

                        break;
                }
            }
            catch (Exception exception) when (exception is FormatException || exception is IOException)
            {
                ImmersiveXLog.Warn($"Couldn't read which files '{Path.GetFileName(file)}' needs ({exception.Message}).");
            }

            return names.Select(n => n.Replace('\\', '/')).Distinct()
                .Where(n => !Path.IsPathRooted(n) && File.Exists(Path.Combine(folder, n))).ToList();
        }

        /// <summary>A folder name that's safe on every platform, from a title.</summary>
        public static string SafeName(string title)
        {
            var name = Regex.Replace(title ?? string.Empty, @"[^A-Za-z0-9_\-]+", "_").Trim('_');
            return name.Length > 0 ? name : "Media";
        }

        static string CopyFile(string file, string name)
        {
            var folder = Path.Combine(Application.streamingAssetsPath, ContentFolder, name);
            Directory.CreateDirectory(folder);
            var source = Path.GetDirectoryName(file) ?? ".";
            foreach (var extra in Sidecars(file))
            {
                var target = Path.Combine(folder, extra);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? folder);
                File.Copy(Path.Combine(source, extra), target, overwrite: true);
            }

            var copy = Path.Combine(folder, Path.GetFileName(file));
            File.Copy(file, copy, overwrite: true);
            return copy;
        }

        static string CopyFolder(string folder, string name)
        {
            var target = Path.Combine(Application.streamingAssetsPath, ContentFolder, name);
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(folder.Length).TrimStart('/', '\\');
                if (relative.EndsWith(".meta", StringComparison.Ordinal) || Path.GetFileName(relative).StartsWith(".", StringComparison.Ordinal))
                    continue;
                var copy = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(copy) ?? target);
                File.Copy(file, copy, overwrite: true);
            }

            return target;
        }

        static string TitleFor(string source)
        {
            var name = source.Contains("://") ? MediaSource.FileName(source) : Path.GetFileName(source.TrimEnd('/', '\\'));
            if (name.Equals("stream.json", StringComparison.OrdinalIgnoreCase) || name.Equals("sequence.json", StringComparison.OrdinalIgnoreCase))
                name = Path.GetFileName(Path.GetDirectoryName(source.TrimEnd('/', '\\')) ?? name);
            var title = Path.GetFileNameWithoutExtension(name).Replace('_', ' ').Replace('-', ' ').Trim();
            return title.Length > 0 ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(title) : "Media";
        }

        static string Clean(string source) => (source ?? string.Empty).Trim().Trim('"', '\'');

        /// <summary>A path typed relative to StreamingAssets, or to the project, or absolute.</summary>
        static string FullPath(string source)
        {
            if (Path.IsPathRooted(source))
                return Path.GetFullPath(source);
            var inStreamingAssets = Path.Combine(Application.streamingAssetsPath, source);
            return File.Exists(inStreamingAssets) || Directory.Exists(inStreamingAssets) ? Path.GetFullPath(inStreamingAssets) : Path.GetFullPath(source);
        }

        static bool InStreamingAssets(string path) =>
            Path.GetFullPath(path).StartsWith(Path.GetFullPath(Application.streamingAssetsPath) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

        /// <summary>The path inside StreamingAssets, with forward slashes: what Immersive Media's Source takes.</summary>
        static string Relative(string path) =>
            Path.GetFullPath(path).Substring(Path.GetFullPath(Application.streamingAssetsPath).Length).Replace('\\', '/').Trim('/');
    }
}

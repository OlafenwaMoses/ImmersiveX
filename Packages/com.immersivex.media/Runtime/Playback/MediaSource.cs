using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ImmersiveX.Media
{
    /// <summary>Where media comes from: turns what the user typed into a URL, and fetches it.</summary>
    public static class MediaSource
    {
        /// <summary>
        /// A URL UnityWebRequest can fetch. http(s), file and jar URLs pass through; an absolute path becomes a file URL;
        /// anything else is relative to StreamingAssets (which works inside an Android APK too).
        /// </summary>
        public static string Resolve(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return null;
            source = source.Trim();
            if (source.Contains("://"))
                return source;
            if (Path.IsPathRooted(source))
                return new Uri(source).AbsoluteUri;
            var path = Path.Combine(Application.streamingAssetsPath, source);
            return path.Contains("://") ? path : new Uri(path).AbsoluteUri;
        }

        /// <summary>The file extension of a URL in lower case, without query or fragment: ".ply".</summary>
        public static string Extension(string url)
        {
            var name = FileName(url);
            var dot = name.LastIndexOf('.');
            return dot >= 0 ? name.Substring(dot).ToLowerInvariant() : string.Empty;
        }

        /// <summary>The last path segment of a URL, without query or fragment.</summary>
        public static string FileName(string url)
        {
            var path = (url ?? string.Empty).Split('?', '#')[0].TrimEnd('/');
            var slash = path.LastIndexOf('/');
            return Uri.UnescapeDataString(slash >= 0 ? path.Substring(slash + 1) : path);
        }

        /// <summary>
        /// <paramref name="path"/> relative to the folder of <paramref name="baseUrl"/> (sequence frames, textures).
        /// Works for http, file and jar URLs; absolute URLs pass through.
        /// </summary>
        public static string Relative(string baseUrl, string path)
        {
            if (string.IsNullOrEmpty(path) || path.Contains("://"))
                return path;
            path = path.Replace('\\', '/');
            var folder = baseUrl.Split('?', '#')[0];
            folder = folder.Substring(0, folder.LastIndexOf('/') + 1);
            while (path.StartsWith("./"))
                path = path.Substring(2);
            while (path.StartsWith("../"))
            {
                path = path.Substring(3);
                var up = folder.TrimEnd('/').LastIndexOf('/');
                if (up > folder.IndexOf("://", StringComparison.Ordinal) + 2)
                    folder = folder.Substring(0, up + 1);
            }

            return folder + string.Join("/", Array.ConvertAll(path.Split('/'), Uri.EscapeDataString));
        }

        /// <summary>The local folder a file URL points to, when it is one (sequences given as a folder on desktop).</summary>
        public static bool TryLocalFolder(string url, out string folder)
        {
            folder = null;
            if (!url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                return false;
            var path = new Uri(url).LocalPath;
            if (!Directory.Exists(path))
                return false;
            folder = path;
            return true;
        }

        /// <summary>Download <paramref name="url"/>; <paramref name="done"/> gets the bytes, or null and the error.</summary>
        public static IEnumerator Fetch(string url, Action<byte[], string> done, Action<float> progress = null)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 120;
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    progress?.Invoke(request.downloadProgress);
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                    done(null, $"{request.error} ({FileName(url)})");
                else
                    done(request.downloadHandler.data, null);
            }
        }

        /// <summary>Download an image (PNG or JPG) as a texture, decoded off the main thread.</summary>
        public static IEnumerator FetchTexture(string url, Action<Texture2D, string> done, bool mipmaps = false)
        {
            var parameters = DownloadedTextureParams.Default;
            parameters.readable = false;
            parameters.mipmapChain = mipmaps;
            using (var request = UnityWebRequestTexture.GetTexture(url, parameters))
            {
                request.timeout = 60;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                    done(null, $"{request.error} ({FileName(url)})");
                else
                    done(DownloadHandlerTexture.GetContent(request), null);
            }
        }

        /// <summary>Download <paramref name="url"/> as UTF-8 text.</summary>
        public static IEnumerator FetchText(string url, Action<string, string> done)
        {
            byte[] bytes = null;
            string error = null;
            yield return Fetch(url, (b, e) =>
            {
                bytes = b;
                error = e;
            });
            done(bytes != null ? Encoding.UTF8.GetString(bytes) : null, error);
        }
    }
}

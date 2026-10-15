using System;
using System.IO;
using UnityEngine;

namespace ImmersiveX.Media
{
    public enum WebPointerPhase
    {
        Down,
        Move,
        Up,
    }

    /// <summary>What a web view is asked for when it's created.</summary>
    public sealed class WebViewOptions
    {
        /// <summary>The page's viewport, in pixels.</summary>
        public int Width = 1280;

        public int Height = 720;

        /// <summary>JavaScript that runs in every page before the page's own scripts (the ImmersiveX bridge).</summary>
        public string BridgeScript;

        /// <summary>Stream the page's pixels to <see cref="IWebView.Frame"/> (for the page's own controls on a panel).</summary>
        public bool Frames = true;

        /// <summary>Show what's behind the page where it has no background.</summary>
        public bool Transparent = true;
    }

    /// <summary>
    /// A web page running out of sight: ImmersiveX loads it, talks to it through the bridge, shows its pixels on a panel and
    /// sends it the user's pointer. The editor uses headless Chrome (DevTools protocol); Quest uses Android's WebView.
    /// Local content (StreamingAssets) is served under <see cref="WebViews.LocalOrigin"/>, so pages with ES modules and
    /// <c>fetch</c> work as on a web server.
    /// </summary>
    public interface IWebView : IDisposable
    {
        /// <summary>Why the page can't run, or null.</summary>
        string Error { get; }

        /// <summary>The page has loaded (its load event fired).</summary>
        bool IsLoaded { get; }

        /// <summary>The page's latest pixels, or null before the first frame.</summary>
        Texture2D Frame { get; }

        /// <summary>Counts the frames received, so a panel knows when to refresh.</summary>
        int FrameCount { get; }

        void Load(string url);

        /// <summary>Run JavaScript in the page.</summary>
        void Evaluate(string javascript);

        /// <summary>The next message the bridge posted (in order), if there is one.</summary>
        bool TryReceive(out string message);

        /// <summary>Press, drag or release at <paramref name="pixel"/> (page pixels, from the top left).</summary>
        void Pointer(WebPointerPhase phase, Vector2 pixel);

        /// <summary>Main thread, every frame: takes in frames and messages.</summary>
        void Update();
    }

    /// <summary>A platform's web view (Quest: Android WebView), registered in <see cref="Services"/> by its adapter.</summary>
    public interface IWebViewProvider
    {
        IWebView Create(WebViewOptions options);
    }

    public static class WebViews
    {
        /// <summary>The host local content is served under (never looked up on the network).</summary>
        public const string LocalHost = "immersivex.local";

        public static string LocalOrigin => "https://" + LocalHost + "/";

        /// <summary>The bridge that streams a page's three.js scene to ImmersiveX (Resources/ImmersiveXMedia/WebBridge.js).</summary>
        public static string BridgeScript
        {
            get
            {
                var asset = Resources.Load<TextAsset>("ImmersiveXMedia/WebBridge.js");
                return asset != null ? asset.text : null;
            }
        }

        /// <summary>
        /// A web view from the platform (a provider in <see cref="Services"/> first), else headless Chrome in the editor and
        /// Android's WebView on Android headsets; null (and why) when there's none.
        /// </summary>
        public static IWebView Create(WebViewOptions options, out string error)
        {
            error = null;
            if (Services.TryGet<IWebViewProvider>(out var provider))
                return provider.Create(options);
#if UNITY_EDITOR
            return ChromeWebView.Create(options, out error);
#elif UNITY_ANDROID
            return AndroidWebView.Create(options, out error);
#else
            error = "Web pages need a web view, which this platform doesn't have yet.";
            return null;
#endif
        }

        /// <summary>
        /// Where a page loads from: web addresses as they are; content in StreamingAssets under <see cref="LocalOrigin"/>. A
        /// folder (a path without an extension) means its <c>index.html</c>.
        /// </summary>
        public static string PageUrl(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return null;
            source = source.Trim().Replace('\\', '/');
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return source;

            var root = Application.streamingAssetsPath.Replace('\\', '/').TrimEnd('/') + "/";
            const string apkAssets = "!/assets/"; // Android: StreamingAssets inside the APK (jar:file://…/base.apk!/assets/…)
            var inApk = source.IndexOf(apkAssets, StringComparison.Ordinal);
            if (inApk >= 0)
                source = Uri.UnescapeDataString(source.Substring(inApk + apkAssets.Length));
            else if (source.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                source = new Uri(source).LocalPath.Replace('\\', '/');
            if (source.StartsWith(root, StringComparison.Ordinal))
                source = source.Substring(root.Length);
            else if (Path.IsPathRooted(source))
                return new Uri(source).AbsoluteUri; // outside StreamingAssets: only the editor can open it, and ES modules won't load

            source = source.Trim('/');
            if (Path.GetExtension(source).Length == 0)
                source += "/index.html";
            return LocalOrigin + string.Join("/", Array.ConvertAll(source.Split('/'), Uri.EscapeDataString));
        }

        /// <summary>The StreamingAssets-relative path a local-origin URL serves, or null for other URLs.</summary>
        public static string LocalPath(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith(LocalOrigin, StringComparison.OrdinalIgnoreCase))
                return null;
            var path = url.Substring(LocalOrigin.Length).Split('?', '#')[0];
            return Uri.UnescapeDataString(path);
        }

        /// <summary>The Content-Type for a file served to a page.</summary>
        public static string MimeType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".html":
                case ".htm": return "text/html; charset=utf-8";
                case ".js":
                case ".mjs": return "text/javascript; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".webp": return "image/webp";
                case ".gif": return "image/gif";
                case ".ktx2": return "image/ktx2";
                case ".glb": return "model/gltf-binary";
                case ".gltf": return "model/gltf+json";
                case ".wasm": return "application/wasm";
                case ".mp3": return "audio/mpeg";
                case ".m4a": return "audio/mp4";
                case ".ogg": return "audio/ogg";
                case ".wav": return "audio/wav";
                case ".mp4": return "video/mp4";
                case ".webm": return "video/webm";
                case ".woff2": return "font/woff2";
                case ".woff": return "font/woff";
                case ".ttf": return "font/ttf";
                case ".txt": return "text/plain; charset=utf-8";
                default: return "application/octet-stream";
            }
        }
    }
}

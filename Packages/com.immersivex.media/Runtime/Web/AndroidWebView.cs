#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Android's WebView, running the page out of sight on a private virtual display (Quest and other Android headsets;
    /// Plugins/Android/OffscreenWebView.java): its pixels come back for the page's panel, content in the app is served from
    /// the APK under <see cref="WebViews.LocalOrigin"/>, and the bridge is put first into every page.
    /// </summary>
    sealed class AndroidWebView : IWebView
    {
        const float FrameSeconds = 1f / 30f; // the panel needs no more

        // OffscreenWebView.state() flags
        const int Loaded = 1;
        const int Failed = 2;
        const int NewFrame = 4;
        const int Messages = 8;

        readonly AndroidJavaObject _java;
        readonly int _width;
        readonly int _height;
        readonly Queue<string> _messages = new Queue<string>();
        NativeArray<byte> _pixels;
        Texture2D _frame;
        float _nextFrame;
        int _state;
        bool _disposed;

        public string Error { get; private set; }
        public bool IsLoaded => (_state & Loaded) != 0;
        public Texture2D Frame => _frame;
        public int FrameCount { get; private set; }

        public static IWebView Create(WebViewOptions options, out string error)
        {
            error = null;
            try
            {
                return new AndroidWebView(options);
            }
            catch (Exception exception)
            {
                error = "Android's web view couldn't start: " + exception.Message;
                return null;
            }
        }

        AndroidWebView(WebViewOptions options)
        {
            _width = options.Width;
            _height = options.Height;
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                _java = new AndroidJavaObject("genxr.immersivex.web.OffscreenWebView", activity, _width, _height, options.BridgeScript ?? string.Empty, options.Transparent);

            // Frames are copied straight into this memory (wrapped as a Java direct buffer), then into the texture.
            _pixels = new NativeArray<byte>(_width * _height * 4, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var local = AndroidJNI.NewDirectByteBuffer(_pixels);
            using (var buffer = new AndroidJavaObject(local))
                _java.Call("setFrameBuffer", buffer);
            AndroidJNI.DeleteLocalRef(local);
        }

        public void Load(string url) => _java?.Call("load", url);

        public void Evaluate(string javascript) => _java?.Call("evaluate", javascript);

        public void Pointer(WebPointerPhase phase, Vector2 pixel) => _java?.Call("pointer", (int)phase, pixel.x, pixel.y);

        public bool TryReceive(out string message)
        {
            if (_messages.Count > 0)
            {
                message = _messages.Dequeue();
                return true;
            }

            message = null;
            return false;
        }

        public void Update()
        {
            if (_disposed || _java == null)
                return;
            _state = _java.Call<int>("state");
            if ((_state & Failed) != 0 && Error == null)
                Error = _java.Call<string>("error");

            while ((_state & Messages) != 0)
            {
                var batch = _java.Call<string[]>("drain");
                if (batch == null || batch.Length == 0)
                    break;
                foreach (var message in batch)
                    _messages.Enqueue(message);
                if (batch.Length < 256)
                    break; // drained; the rest (if any) next frame
            }

            if ((_state & NewFrame) != 0 && Time.unscaledTime >= _nextFrame && _java.Call<bool>("takeFrame"))
            {
                _nextFrame = Time.unscaledTime + FrameSeconds;
                if (_frame == null)
                    _frame = new Texture2D(_width, _height, TextureFormat.RGBA32, false) { name = "Web page", wrapMode = TextureWrapMode.Clamp };
                _frame.LoadRawTextureData(_pixels);
                _frame.Apply(false);
                FrameCount++;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                _java?.Call("destroy");
            }
            catch (Exception exception)
            {
                ImmersiveXLog.Warn("Web page: the web view didn't close cleanly: " + exception.Message);
            }

            _java?.Dispose();
            if (_pixels.IsCreated)
                _pixels.Dispose(); // the Java side drops it with the view; no frame is copied after destroy
            if (_frame != null)
                UnityEngine.Object.Destroy(_frame);
        }
    }
}
#endif

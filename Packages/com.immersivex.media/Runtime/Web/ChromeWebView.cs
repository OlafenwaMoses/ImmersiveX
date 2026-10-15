#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A web view for the editor (Play mode, the Meta XR Simulator): headless Chrome, driven over the DevTools protocol.
    /// <list type="bullet">
    /// <item>The bridge runs in every page before its own scripts (<c>Page.addScriptToEvaluateOnNewDocument</c>) and posts
    /// through a binding (<c>Runtime.addBinding</c>).</item>
    /// <item>Local content is served from StreamingAssets under <see cref="WebViews.LocalOrigin"/> (<c>Fetch</c> interception),
    /// the same address the headset uses.</item>
    /// <item>The page's pixels arrive as PNG screencast frames (with transparency); the pointer is sent as mouse events.</item>
    /// </list>
    /// </summary>
    sealed class ChromeWebView : IWebView
    {
        const string Binding = "__ixPost";
        const int ScreencastEveryNthFrame = 3; // ~20 frames a second is plenty for a page's controls

        readonly WebViewOptions _options;
        readonly ConcurrentQueue<string> _incoming = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<string> _outgoing = new ConcurrentQueue<string>();
        readonly Queue<string> _messages = new Queue<string>();
        readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        readonly SemaphoreSlim _sendSignal = new SemaphoreSlim(0);
        Process _chrome;
        string _profile;
        ClientWebSocket _socket;
        int _nextId = 1;
        string _pendingUrl;
        bool _connected;
        bool _pointerDown;
        Texture2D _frame;
        int _logged;

        public string Error { get; private set; }
        public bool IsLoaded { get; private set; }
        public Texture2D Frame => _frame;
        public int FrameCount { get; private set; }

        ChromeWebView(WebViewOptions options) => _options = options;

        public static IWebView Create(WebViewOptions options, out string error)
        {
            error = null;
            var chrome = FindChrome();
            if (chrome == null)
            {
                error = "Web pages play in the editor through Google Chrome, which wasn't found (set IMMERSIVEX_CHROME to its path).";
                return null;
            }

            var view = new ChromeWebView(options);
            view.Start(chrome);
            return view;
        }

        static string FindChrome()
        {
            var overridden = Environment.GetEnvironmentVariable("IMMERSIVEX_CHROME");
            foreach (var path in new[]
                     {
                         overridden,
                         "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                         "/Applications/Chromium.app/Contents/MacOS/Chromium",
                         @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                         @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                         "/usr/bin/google-chrome",
                         "/usr/bin/chromium",
                     })
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            return null;
        }

        void Start(string chrome)
        {
            _profile = Path.Combine(Path.GetTempPath(), "ImmersiveX-Chrome-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_profile);
            var arguments = string.Join(" ", "--headless=new", "--remote-debugging-port=0", $"--user-data-dir=\"{_profile}\"",
                "--no-first-run", "--no-default-browser-check", "--autoplay-policy=no-user-gesture-required", "--mute-audio",
                "--hide-scrollbars", $"--window-size={_options.Width},{_options.Height}", "about:blank");
            _chrome = Process.Start(new ProcessStartInfo(chrome, arguments) { UseShellExecute = false, CreateNoWindow = true });
            Task.Run(Connect);
        }

        async Task Connect()
        {
            try
            {
                var portFile = Path.Combine(_profile, "DevToolsActivePort");
                for (var i = 0; i < 150 && !File.Exists(portFile); i++)
                    await Task.Delay(100, _cancel.Token);
                if (!File.Exists(portFile))
                    throw new TimeoutException("Chrome didn't start its DevTools server.");
                var port = File.ReadAllLines(portFile)[0].Trim();

                string socketUrl;
                using (var http = new HttpClient())
                {
                    var created = await http.PutAsync($"http://127.0.0.1:{port}/json/new?about:blank", new StringContent(string.Empty));
                    var target = Json.Parse(await created.Content.ReadAsStringAsync()) as Dictionary<string, object>;
                    socketUrl = target?["webSocketDebuggerUrl"] as string ?? throw new InvalidOperationException("Chrome didn't open a page.");
                }

                _socket = new ClientWebSocket();
                _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                await _socket.ConnectAsync(new Uri(socketUrl), _cancel.Token);
                _ = Task.Run(SendLoop);

                Send("Page.enable", "{}");
                Send("Runtime.enable", "{}");
                Send("Runtime.addBinding", $"{{\"name\":\"{Binding}\"}}");
                if (!string.IsNullOrEmpty(_options.BridgeScript))
                    Send("Page.addScriptToEvaluateOnNewDocument", $"{{\"source\":{Quote(_options.BridgeScript)}}}");
                Send("Fetch.enable", $"{{\"patterns\":[{{\"urlPattern\":\"{WebViews.LocalOrigin}*\",\"requestStage\":\"Request\"}}]}}");
                Send("Emulation.setDeviceMetricsOverride", $"{{\"width\":{_options.Width},\"height\":{_options.Height},\"deviceScaleFactor\":1,\"mobile\":false}}");
                if (_options.Transparent)
                    Send("Emulation.setDefaultBackgroundColorOverride", "{\"color\":{\"r\":0,\"g\":0,\"b\":0,\"a\":0}}");
                if (_options.Frames)
                    Send("Page.startScreencast", $"{{\"format\":\"png\",\"everyNthFrame\":{ScreencastEveryNthFrame},\"maxWidth\":{_options.Width},\"maxHeight\":{_options.Height}}}");
                _connected = true;
                if (_pendingUrl != null)
                    Load(_pendingUrl);

                await ReceiveLoop();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Error = "The editor's web view (Chrome) failed: " + exception.GetBaseException().Message;
            }
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[1 << 16];
            var message = new MemoryStream();
            while (_socket.State == WebSocketState.Open && !_cancel.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancel.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                    continue;
                _incoming.Enqueue(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                message.SetLength(0);
            }
        }

        async Task SendLoop()
        {
            while (!_cancel.IsCancellationRequested)
            {
                await _sendSignal.WaitAsync(_cancel.Token);
                while (_outgoing.TryDequeue(out var text))
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancel.Token);
                }
            }
        }

        void Send(string method, string parameters)
        {
            var id = Interlocked.Increment(ref _nextId);
            _outgoing.Enqueue($"{{\"id\":{id},\"method\":\"{method}\",\"params\":{parameters}}}");
            _sendSignal.Release();
        }

        // ---------------------------------------------------------------- IWebView

        public void Load(string url)
        {
            IsLoaded = false;
            if (!_connected)
            {
                _pendingUrl = url;
                return;
            }

            _pendingUrl = null;
            Send("Page.navigate", $"{{\"url\":{Quote(url)}}}");
        }

        public void Evaluate(string javascript) => Send("Runtime.evaluate", $"{{\"expression\":{Quote(javascript)}}}");

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

        public void Pointer(WebPointerPhase phase, Vector2 pixel)
        {
            string type;
            switch (phase)
            {
                case WebPointerPhase.Down:
                    type = "mousePressed";
                    _pointerDown = true;
                    break;
                case WebPointerPhase.Up:
                    type = "mouseReleased";
                    _pointerDown = false;
                    break;
                default:
                    type = "mouseMoved";
                    break;
            }

            var button = phase == WebPointerPhase.Move && !_pointerDown ? "none" : "left";
            var buttons = _pointerDown ? 1 : 0;
            Send("Input.dispatchMouseEvent", FormattableString.Invariant(
                $"{{\"type\":\"{type}\",\"x\":{pixel.x:0.##},\"y\":{pixel.y:0.##},\"button\":\"{button}\",\"buttons\":{buttons},\"clickCount\":{(phase == WebPointerPhase.Move ? 0 : 1)}}}"));
        }

        public void Update()
        {
            for (var processed = 0; processed < 400 && _incoming.TryDequeue(out var text); processed++)
            {
                Dictionary<string, object> message;
                try
                {
                    message = Json.Parse(text) as Dictionary<string, object>;
                }
                catch (FormatException)
                {
                    continue;
                }

                if (message == null || !message.TryGetValue("method", out var methodValue))
                {
                    ReportProtocolError(message);
                    continue;
                }

                var parameters = message.TryGetValue("params", out var p) ? p as Dictionary<string, object> : null;
                switch (methodValue as string)
                {
                    case "Runtime.bindingCalled":
                        if (parameters != null && parameters.TryGetValue("payload", out var payload) && payload is string posted)
                            _messages.Enqueue(posted);
                        break;
                    case "Page.loadEventFired":
                        IsLoaded = true;
                        break;
                    case "Page.screencastFrame":
                        TakeFrame(parameters);
                        break;
                    case "Fetch.requestPaused":
                        Serve(parameters);
                        break;
                    case "Runtime.exceptionThrown":
                        Log("Web page error: " + Describe(parameters));
                        break;
                }
            }
        }

        void TakeFrame(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return;
            if (parameters.TryGetValue("sessionId", out var session))
                Send("Page.screencastFrameAck", FormattableString.Invariant($"{{\"sessionId\":{Convert.ToInt64(session)}}}"));
            if (!(parameters.TryGetValue("data", out var data) && data is string base64))
                return;
            if (_frame == null)
                _frame = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Web page", wrapMode = TextureWrapMode.Clamp };
            if (_frame.LoadImage(Convert.FromBase64String(base64)))
                FrameCount++;
        }

        /// <summary>Answer a request for local content with the file from StreamingAssets (read and encoded off the main thread).</summary>
        void Serve(Dictionary<string, object> parameters)
        {
            var requestId = parameters?["requestId"] as string;
            var request = parameters?["request"] as Dictionary<string, object>;
            var url = request?["url"] as string;
            if (requestId == null || url == null)
                return;
            var relative = WebViews.LocalPath(url);
            var file = relative != null ? Path.Combine(Application.streamingAssetsPath, relative) : null;
            Task.Run(() =>
            {
                if (file == null || !File.Exists(file))
                {
                    _outgoing.Enqueue($"{{\"id\":{Interlocked.Increment(ref _nextId)},\"method\":\"Fetch.fulfillRequest\",\"params\":{{\"requestId\":{Quote(requestId)},\"responseCode\":404,\"body\":\"\"}}}}");
                    _sendSignal.Release();
                    return;
                }

                var body = Convert.ToBase64String(File.ReadAllBytes(file));
                var headers = $"[{{\"name\":\"Content-Type\",\"value\":{Quote(WebViews.MimeType(file))}}},{{\"name\":\"Access-Control-Allow-Origin\",\"value\":\"*\"}},{{\"name\":\"Cache-Control\",\"value\":\"no-cache\"}}]";
                _outgoing.Enqueue($"{{\"id\":{Interlocked.Increment(ref _nextId)},\"method\":\"Fetch.fulfillRequest\",\"params\":{{\"requestId\":{Quote(requestId)},\"responseCode\":200,\"responseHeaders\":{headers},\"body\":\"{body}\"}}}}");
                _sendSignal.Release();
            });
        }

        void ReportProtocolError(Dictionary<string, object> message)
        {
            if (message != null && message.TryGetValue("error", out var error) && error is Dictionary<string, object> details)
                Log("Chrome: " + (details.TryGetValue("message", out var text) ? text : "error"));
        }

        static string Describe(Dictionary<string, object> parameters)
        {
            if (parameters?["exceptionDetails"] is Dictionary<string, object> details)
            {
                if (details.TryGetValue("exception", out var exception) && exception is Dictionary<string, object> thrown && thrown.TryGetValue("description", out var description))
                    return (description as string ?? string.Empty).Split('\n')[0];
                if (details.TryGetValue("text", out var text))
                    return text as string;
            }

            return "unknown";
        }

        void Log(string text)
        {
            if (_logged++ < 20)
                ImmersiveXLog.Warn(text);
        }

        static string Quote(string text) => MediaFolders.Quote(text ?? string.Empty);

        public void Dispose()
        {
            _cancel.Cancel();
            try
            {
                _socket?.Abort();
                _socket?.Dispose();
            }
            catch (Exception)
            {
                // closing anyway
            }

            try
            {
                if (_chrome != null && !_chrome.HasExited)
                    _chrome.Kill();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("ImmersiveX: couldn't stop the editor's Chrome: " + exception.Message);
            }

            _chrome?.Dispose();
            if (_frame != null)
                UnityEngine.Object.Destroy(_frame);
            var profile = _profile;
            Task.Run(async () =>
            {
                await Task.Delay(1500); // Chrome lets go of its profile once it has exited
                try
                {
                    Directory.Delete(profile, true);
                }
                catch (Exception)
                {
                    // a temp folder: the OS cleans it up
                }
            });
        }
    }
}
#endif

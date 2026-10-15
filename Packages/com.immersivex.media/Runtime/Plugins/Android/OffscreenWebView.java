package genxr.immersivex.web;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.Presentation;
import android.content.Context;
import android.content.res.AssetFileDescriptor;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.drawable.ColorDrawable;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.Image;
import android.media.ImageReader;
import android.net.Uri;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Log;
import android.view.InputDevice;
import android.view.MotionEvent;
import android.view.ViewGroup;
import android.webkit.ConsoleMessage;
import android.webkit.JavascriptInterface;
import android.webkit.MimeTypeMap;
import android.webkit.RenderProcessGoneDetail;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.FilterInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ConcurrentLinkedQueue;

/**
 * A web page running out of sight for ImmersiveX (Quest and other Android headsets): an Android WebView on a private
 * virtual display, its pixels read back through an ImageReader for the page's panel. Content in the app (StreamingAssets,
 * in the APK's assets) is served under https://immersivex.local/, and ImmersiveX's bridge script is put first into every
 * page it opens, so it runs before the page's own scripts. Messages the bridge posts (window.ImmersiveXHost.post) wait
 * here until Unity drains them.
 * <p>
 * Every public method may be called from any thread; the WebView lives on the main (UI) thread.
 */
public final class OffscreenWebView {
    static final String TAG = "ImmersiveX";
    static final String LOCAL_HOST = "immersivex.local";
    static final int MAX_MESSAGES_PER_DRAIN = 256;

    // state() flags
    static final int LOADED = 1;
    static final int FAILED = 2;
    static final int NEW_FRAME = 4;
    static final int MESSAGES = 8;

    final Activity activity;
    final int width;
    final int height;
    final String bridge;
    final boolean transparent;
    final Handler main = new Handler(Looper.getMainLooper());
    final ConcurrentLinkedQueue<String> messages = new ConcurrentLinkedQueue<>();

    final Object frameLock = new Object();
    final ByteBuffer back; // the newest frame, bottom row first (as Unity's textures are), written by the reader thread
    ByteBuffer front; // Unity's buffer: takeFrame copies the newest frame into it
    volatile boolean newFrame;
    volatile boolean loaded;
    volatile String error;

    HandlerThread readerThread;
    ImageReader reader;
    VirtualDisplay display;
    Presentation presentation;
    WebView web;
    boolean pressed;
    long downTime;
    volatile boolean destroyed;

    public OffscreenWebView(Activity activity, int width, int height, String bridge, boolean transparent) {
        this.activity = activity;
        this.width = width;
        this.height = height;
        this.bridge = bridge == null ? "" : bridge;
        this.transparent = transparent;
        back = ByteBuffer.allocateDirect(width * height * 4);
        main.post(this::create);
    }

    // ---------------------------------------------------------------- Unity's side

    /** LOADED, FAILED, NEW_FRAME and MESSAGES, so Unity asks for more only when there is more. */
    public int state() {
        int state = 0;
        if (loaded) state |= LOADED;
        if (error != null) state |= FAILED;
        if (newFrame) state |= NEW_FRAME;
        if (!messages.isEmpty()) state |= MESSAGES;
        return state;
    }

    public String error() {
        return error;
    }

    /** The buffer frames are copied into for Unity (a direct buffer over Unity's memory, width * height * 4 bytes). */
    public void setFrameBuffer(ByteBuffer buffer) {
        synchronized (frameLock) {
            front = buffer;
        }
    }

    /** Copy the newest frame into Unity's buffer; false when there's none Unity hasn't had. */
    public boolean takeFrame() {
        synchronized (frameLock) {
            if (!newFrame || front == null || front.capacity() < back.capacity())
                return false;
            back.position(0);
            front.position(0);
            front.put(back);
            back.position(0);
            front.position(0);
            newFrame = false;
            return true;
        }
    }

    /** The messages the bridge posted since the last call (in order), or null when there are none. */
    public String[] drain() {
        if (messages.isEmpty())
            return null;
        List<String> list = new ArrayList<>();
        String message;
        while (list.size() < MAX_MESSAGES_PER_DRAIN && (message = messages.poll()) != null)
            list.add(message);
        return list.toArray(new String[0]);
    }

    public void load(final String url) {
        main.post(() -> {
            if (web != null) {
                loaded = false;
                web.loadUrl(url);
            }
        });
    }

    public void evaluate(final String script) {
        main.post(() -> {
            if (web != null)
                web.evaluateJavascript(script, null);
        });
    }

    /** A pointer on the page, in page pixels from the top left: 0 down, 1 move (a drag while down, else a hover), 2 up. */
    public void pointer(final int phase, final float x, final float y) {
        main.post(() -> {
            if (web == null)
                return;
            long now = SystemClock.uptimeMillis();
            if (phase == 0) {
                pressed = true;
                downTime = now;
                touch(MotionEvent.ACTION_DOWN, now, x, y);
            } else if (phase == 2) {
                if (!pressed)
                    return;
                pressed = false;
                touch(MotionEvent.ACTION_UP, now, x, y);
            } else if (pressed) {
                touch(MotionEvent.ACTION_MOVE, now, x, y);
            } else {
                MotionEvent hover = MotionEvent.obtain(now, now, MotionEvent.ACTION_HOVER_MOVE, x, y, 0);
                hover.setSource(InputDevice.SOURCE_MOUSE);
                web.dispatchGenericMotionEvent(hover);
                hover.recycle();
            }
        });
    }

    public void destroy() {
        destroyed = true;
        synchronized (frameLock) {
            front = null; // Unity frees its buffer now
        }
        main.post(() -> {
            try {
                if (presentation != null)
                    presentation.dismiss();
            } catch (Throwable ignored) {
            }
            try {
                if (web != null) {
                    web.stopLoading();
                    web.destroy();
                }
            } catch (Throwable ignored) {
            }
            try {
                if (display != null)
                    display.release();
            } catch (Throwable ignored) {
            }
            try {
                if (reader != null)
                    reader.close();
            } catch (Throwable ignored) {
            }
            if (readerThread != null)
                readerThread.quitSafely();
            web = null;
            presentation = null;
            display = null;
            reader = null;
        });
    }

    // ---------------------------------------------------------------- the web view

    @SuppressLint({"SetJavaScriptEnabled", "JavascriptInterface"})
    void create() {
        if (destroyed)
            return;
        try {
            readerThread = new HandlerThread("ImmersiveX web view frames");
            readerThread.start();
            reader = ImageReader.newInstance(width, height, PixelFormat.RGBA_8888, 2);
            reader.setOnImageAvailableListener(this::onImage, new Handler(readerThread.getLooper()));
            DisplayManager displays = (DisplayManager) activity.getSystemService(Context.DISPLAY_SERVICE);
            // A private display only this app draws on; 160 dpi, so one CSS pixel is one pixel.
            display = displays.createVirtualDisplay("ImmersiveX web view", width, height, 160, reader.getSurface(),
                    DisplayManager.VIRTUAL_DISPLAY_FLAG_OWN_CONTENT_ONLY);
            presentation = new Presentation(activity, display.getDisplay());
            if (presentation.getWindow() != null)
                presentation.getWindow().setBackgroundDrawable(new ColorDrawable(transparent ? Color.TRANSPARENT : Color.WHITE));

            web = new WebView(presentation.getContext());
            web.setBackgroundColor(transparent ? Color.TRANSPARENT : Color.WHITE);
            WebSettings settings = web.getSettings();
            settings.setJavaScriptEnabled(true);
            settings.setDomStorageEnabled(true);
            settings.setMediaPlaybackRequiresUserGesture(false);
            settings.setAllowFileAccess(false);
            settings.setAllowContentAccess(false);
            settings.setUseWideViewPort(false); // the page is laid out at the view's size, like a desktop window
            settings.setLoadWithOverviewMode(false);
            settings.setSupportZoom(false);
            settings.setBuiltInZoomControls(false);
            web.addJavascriptInterface(new Host(this), "ImmersiveXHost");
            web.setWebViewClient(new Client());
            web.setWebChromeClient(new Chrome());
            presentation.setContentView(web, new ViewGroup.LayoutParams(width, height));
            presentation.show();
            Log.i(TAG, "Web view: ready (" + width + " x " + height + ")");
        } catch (Throwable e) {
            fail("The web view couldn't start: " + e);
        }
    }

    void touch(int action, long now, float x, float y) {
        MotionEvent event = MotionEvent.obtain(downTime, now, action, x, y, 0);
        event.setSource(InputDevice.SOURCE_TOUCHSCREEN);
        web.dispatchTouchEvent(event);
        event.recycle();
    }

    /** The reader thread: the newest image into the back buffer, bottom row first. */
    void onImage(ImageReader source) {
        Image image = null;
        try {
            image = source.acquireLatestImage();
            if (image == null)
                return;
            Image.Plane plane = image.getPlanes()[0];
            ByteBuffer pixels = plane.getBuffer();
            int rowStride = plane.getRowStride();
            int row = width * 4;
            synchronized (frameLock) {
                for (int y = 0; y < height; y++) {
                    pixels.limit(y * rowStride + row);
                    pixels.position(y * rowStride);
                    back.position((height - 1 - y) * row);
                    back.put(pixels);
                }
                back.position(0);
                newFrame = true;
            }
        } catch (Throwable e) {
            Log.w(TAG, "Web view: a frame couldn't be read: " + e);
        } finally {
            if (image != null)
                image.close();
        }
    }

    void fail(String text) {
        Log.e(TAG, "Web view: " + text);
        if (error == null)
            error = text;
    }

    void log(String level, String text) {
        messages.add("{\"t\":\"log\",\"level\":\"" + level + "\",\"text\":" + quote(text) + "}");
    }

    static String quote(String text) {
        StringBuilder out = new StringBuilder(text.length() + 2).append('"');
        for (int i = 0; i < text.length(); i++) {
            char c = text.charAt(i);
            switch (c) {
                case '"': out.append("\\\""); break;
                case '\\': out.append("\\\\"); break;
                case '\n': out.append("\\n"); break;
                case '\r': out.append("\\r"); break;
                case '\t': out.append("\\t"); break;
                default:
                    if (c < 0x20)
                        out.append(String.format(Locale.ROOT, "\\u%04x", (int) c));
                    else
                        out.append(c);
            }
        }
        return out.append('"').toString();
    }

    /** What the bridge calls: window.ImmersiveXHost.post(json). Public, so the web view's bridge can call it. */
    public static final class Host {
        final OffscreenWebView owner;

        Host(OffscreenWebView owner) {
            this.owner = owner;
        }

        @JavascriptInterface
        public void post(String message) {
            if (message != null && !owner.destroyed)
                owner.messages.add(message);
        }
    }

    final class Chrome extends WebChromeClient {
        @Override
        public boolean onConsoleMessage(ConsoleMessage message) {
            if (message.messageLevel() == ConsoleMessage.MessageLevel.ERROR)
                log("warn", "console: " + message.message() + " (" + message.sourceId() + ":" + message.lineNumber() + ")");
            return true;
        }
    }

    final class Client extends WebViewClient {
        @Override
        public void onPageFinished(WebView view, String url) {
            loaded = true;
        }

        @Override
        public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError failure) {
            if (request.isForMainFrame())
                fail("The page couldn't load: " + failure.getDescription() + " (" + request.getUrl() + ")");
        }

        @Override
        public void onReceivedHttpError(WebView view, WebResourceRequest request, WebResourceResponse response) {
            if (request.isForMainFrame())
                fail("The page couldn't load: HTTP " + response.getStatusCode() + " (" + request.getUrl() + ")");
        }

        @Override
        public boolean onRenderProcessGone(WebView view, RenderProcessGoneDetail detail) {
            // Handled, so the app keeps running: this page is over.
            fail(detail.didCrash() ? "The page stopped: its renderer crashed." : "The page stopped: it ran out of memory.");
            try {
                if (presentation != null)
                    presentation.dismiss();
                view.destroy();
            } catch (Throwable ignored) {
            }
            web = null;
            return true;
        }

        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            return false; // links open in the same page
        }

        @Override
        public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
            Uri url = request.getUrl();
            try {
                if (LOCAL_HOST.equalsIgnoreCase(url.getHost()))
                    return local(request, url);
                if (request.isForMainFrame() && "GET".equalsIgnoreCase(request.getMethod())
                        && ("http".equalsIgnoreCase(url.getScheme()) || "https".equalsIgnoreCase(url.getScheme())))
                    return remotePage(request, url);
            } catch (Throwable e) {
                Log.w(TAG, "Web view: " + url + " couldn't be served: " + e);
            }
            return null; // the web view loads it itself
        }
    }

    // ---------------------------------------------------------------- serving

    /** Content in the app: https://immersivex.local/<path> is assets/<path> in the APK (StreamingAssets). */
    WebResourceResponse local(WebResourceRequest request, Uri url) throws IOException {
        String path = url.getPath() == null ? "" : url.getPath();
        while (path.startsWith("/"))
            path = path.substring(1);
        if (path.isEmpty() || path.endsWith("/"))
            path += "index.html";
        String mime = mimeType(path);
        Map<String, String> headers = new HashMap<>();
        headers.put("Access-Control-Allow-Origin", "*");
        headers.put("Cache-Control", "no-cache");

        if (mime.startsWith("text/html")) {
            byte[] page;
            try (InputStream in = activity.getAssets().open(path)) {
                page = readAll(in);
            } catch (IOException missing) {
                return notFound(path);
            }
            return new WebResourceResponse("text/html", "utf-8", 200, "OK", headers, new ByteArrayInputStream(withBridge(page)));
        }

        // Media seeks ask for ranges: answer them when the file's length is known (stored uncompressed in the APK).
        String range = header(request, "Range");
        long length = -1;
        try (AssetFileDescriptor fd = activity.getAssets().openFd(path)) {
            length = fd.getLength();
        } catch (IOException compressedOrMissing) {
            // compressed in the APK (no descriptor): served whole
        }

        InputStream in;
        try {
            in = activity.getAssets().open(path, android.content.res.AssetManager.ACCESS_STREAMING);
        } catch (IOException missing) {
            return notFound(path);
        }

        if (range != null && length > 0 && range.startsWith("bytes=")) {
            String[] bounds = range.substring(6).split("-", 2);
            long start = bounds[0].isEmpty() ? 0 : Long.parseLong(bounds[0].trim());
            long end = bounds.length > 1 && !bounds[1].trim().isEmpty() ? Long.parseLong(bounds[1].trim()) : length - 1;
            end = Math.min(end, length - 1);
            if (start <= end) {
                skipFully(in, start);
                headers.put("Content-Range", "bytes " + start + "-" + end + "/" + length);
                headers.put("Content-Length", String.valueOf(end - start + 1));
                headers.put("Accept-Ranges", "bytes");
                return new WebResourceResponse(mime, null, 206, "Partial Content", headers, limited(in, end - start + 1));
            }
        }

        if (length > 0)
            headers.put("Content-Length", String.valueOf(length));
        headers.put("Accept-Ranges", length > 0 ? "bytes" : "none");
        return new WebResourceResponse(mime, null, 200, "OK", headers, in);
    }

    /** A page from the internet, fetched here so the bridge can be put first into it. */
    WebResourceResponse remotePage(WebResourceRequest request, Uri url) throws IOException {
        HttpURLConnection connection = (HttpURLConnection) new URL(url.toString()).openConnection();
        connection.setInstanceFollowRedirects(true);
        connection.setConnectTimeout(15000);
        connection.setReadTimeout(30000);
        for (Map.Entry<String, String> header : request.getRequestHeaders().entrySet())
            connection.setRequestProperty(header.getKey(), header.getValue());
        int status = connection.getResponseCode();
        String type = connection.getContentType();
        if (type == null || !type.toLowerCase(Locale.ROOT).contains("text/html")) {
            connection.disconnect();
            return null; // not a page: the web view loads it itself
        }

        byte[] page;
        try (InputStream in = status >= 400 ? connection.getErrorStream() : connection.getInputStream()) {
            page = in == null ? new byte[0] : readAll(in);
        } finally {
            connection.disconnect();
        }

        Map<String, String> headers = new HashMap<>();
        String reason = connection.getResponseMessage();
        return new WebResourceResponse("text/html", "utf-8", status, reason == null || reason.isEmpty() ? "OK" : reason, headers,
                new ByteArrayInputStream(withBridge(page)));
    }

    /** The page with the bridge as its first script (just inside <head>, else <html>, else at the very start). */
    byte[] withBridge(byte[] page) {
        if (bridge.isEmpty())
            return page;
        String html = new String(page, StandardCharsets.UTF_8);
        String script = "<script>" + bridge.replace("</script", "<\\/script") + "</script>";
        String lower = html.length() > 4096 ? html.substring(0, 4096).toLowerCase(Locale.ROOT) : html.toLowerCase(Locale.ROOT);
        int at = 0;
        int head = lower.indexOf("<head");
        int tag = head >= 0 ? head : lower.indexOf("<html");
        if (tag >= 0) {
            int close = lower.indexOf('>', tag);
            if (close >= 0)
                at = close + 1;
        }
        return (html.substring(0, at) + script + html.substring(at)).getBytes(StandardCharsets.UTF_8);
    }

    static String header(WebResourceRequest request, String name) {
        for (Map.Entry<String, String> header : request.getRequestHeaders().entrySet())
            if (header.getKey().equalsIgnoreCase(name))
                return header.getValue();
        return null;
    }

    static WebResourceResponse notFound(String path) {
        Log.w(TAG, "Web view: not in the app: " + path);
        return new WebResourceResponse("text/plain", "utf-8", 404, "Not Found", new HashMap<>(),
                new ByteArrayInputStream(("Not found: " + path).getBytes(StandardCharsets.UTF_8)));
    }

    static String mimeType(String path) {
        String name = path.substring(path.lastIndexOf('/') + 1);
        String extension = name.lastIndexOf('.') >= 0 ? name.substring(name.lastIndexOf('.') + 1).toLowerCase(Locale.ROOT) : "";
        switch (extension) {
            case "html":
            case "htm": return "text/html";
            case "js":
            case "mjs": return "text/javascript";
            case "json": return "application/json";
            case "css": return "text/css";
            case "wasm": return "application/wasm";
            case "glb": return "model/gltf-binary";
            case "gltf": return "model/gltf+json";
            case "ktx2": return "image/ktx2";
            case "svg": return "image/svg+xml";
            case "m4a": return "audio/mp4";
            case "woff2": return "font/woff2";
            default:
                String known = MimeTypeMap.getSingleton().getMimeTypeFromExtension(extension);
                return known != null ? known : "application/octet-stream";
        }
    }

    static byte[] readAll(InputStream in) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = in.read(buffer)) > 0)
            out.write(buffer, 0, read);
        return out.toByteArray();
    }

    static void skipFully(InputStream in, long count) throws IOException {
        while (count > 0) {
            long skipped = in.skip(count);
            if (skipped <= 0) {
                if (in.read() < 0)
                    return;
                skipped = 1;
            }
            count -= skipped;
        }
    }

    static InputStream limited(InputStream in, final long count) {
        return new FilterInputStream(in) {
            long left = count;

            @Override
            public int read() throws IOException {
                if (left <= 0)
                    return -1;
                int value = super.read();
                if (value >= 0)
                    left--;
                return value;
            }

            @Override
            public int read(byte[] buffer, int offset, int length) throws IOException {
                if (left <= 0)
                    return -1;
                int read = super.read(buffer, offset, (int) Math.min(length, left));
                if (read > 0)
                    left -= read;
                return read;
            }
        };
    }
}

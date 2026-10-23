using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ImmersiveX
{
    /// <summary>
    /// One picture from the headset's camera, with where the camera was and how it sees, so a point in the picture can be
    /// turned into a ray in the room. Dispose it when done (it holds a texture).
    /// </summary>
    public sealed class CameraFrame : IDisposable
    {
        public CameraFrame(Texture2D image, Pose pose, CameraIntrinsics intrinsics, float time)
        {
            Image = image;
            Pose = pose;
            Intrinsics = intrinsics;
            Time = time;
        }

        /// <summary>The picture, readable on the CPU (RGBA32, upright).</summary>
        public Texture2D Image { get; private set; }

        /// <summary>The camera's pose in the world when the picture was taken.</summary>
        public Pose Pose { get; }

        public CameraIntrinsics Intrinsics { get; }

        /// <summary><see cref="UnityEngine.Time.realtimeSinceStartup"/> when it was taken.</summary>
        public float Time { get; }

        public int Width => Image != null ? Image.width : 0;
        public int Height => Image != null ? Image.height : 0;

        /// <summary>The ray in the world through a viewport point (0 to 1, from the bottom left).</summary>
        public Ray ViewportToRay(Vector2 viewport) => new Ray(Pose.position, (Pose.rotation * Intrinsics.Direction(viewport)).normalized);

        /// <summary>The ray through a point measured from the top left (0 to 1), as vision models give their boxes.</summary>
        public Ray ImagePointToRay(Vector2 fromTopLeft) => ViewportToRay(new Vector2(fromTopLeft.x, 1f - fromTopLeft.y));

        /// <summary>How wide something <paramref name="fraction"/> of the picture's width is, <paramref name="distance"/> metres away.</summary>
        public float WidthAt(float fraction, float distance) =>
            Intrinsics.IsValid ? fraction * Intrinsics.Resolution.x / Intrinsics.FocalLength.x * distance : 0f;

        public byte[] EncodeToJpg(int quality = 85) => Image != null ? Image.EncodeToJPG(quality) : Array.Empty<byte>();

        public void Dispose()
        {
            if (Image != null)
                UnityEngine.Object.Destroy(Image);
            Image = null;
        }
    }

    /// <summary>
    /// The headset's camera for bare metal content (<see cref="ImmersiveHost.PassthroughCamera"/>): start it, take pictures,
    /// turn picture points into rays in the room. On Quest 3 and 3S it's the Passthrough Camera API (the app's
    /// <see cref="BareMetalContent"/> needs <c>Passthrough Camera</c> on, for the permissions); in the editor it's a simulated
    /// camera that sees the app's own scene.
    /// </summary>
    public sealed class HostCamera
    {
        const float StartTimeoutSeconds = 6f;
        static readonly Vector2Int DefaultResolution = new Vector2Int(1280, 960);

        IPassthroughCameraProvider _provider;
        bool _resolved;

        /// <summary>Where the frames come from, or null on a device without a camera the app may read.</summary>
        public IPassthroughCameraProvider Provider
        {
            get
            {
                if (!_resolved)
                {
                    _resolved = true;
                    _provider = Services.TryGet<IPassthroughCameraProvider>(out var provider) ? provider
                        : Application.isEditor ? new SimulatedPassthroughCamera() : null;
                }

                return _provider;
            }
        }

        public bool IsSupported => Provider != null && Provider.IsSupported;
        public bool IsRunning => Provider != null && Provider.IsPlaying;
        public bool HasAccess { get; private set; }
        public Texture Texture => Provider?.Texture;
        public CameraIntrinsics Intrinsics => Provider != null ? Provider.Intrinsics : default;
        public Pose WorldPose => Provider != null ? Provider.WorldPose : Pose.identity;

        /// <summary>
        /// The layers the editor's simulated camera draws. Leave out the app's own panels and labels, which a real camera
        /// never sees.
        /// </summary>
        public int SimulatedCullingMask
        {
            get => Provider is SimulatedPassthroughCamera simulated ? simulated.CullingMask : ~0;
            set
            {
                if (Provider is SimulatedPassthroughCamera simulated)
                    simulated.CullingMask = value;
            }
        }

        /// <summary>One line for logs and automation.</summary>
        public string Status => Provider == null ? "no camera the app may read on this device"
            : !Provider.IsSupported ? $"{Provider.Name}: not supported here"
            : IsRunning ? $"{Provider.Name}: running · {Intrinsics}"
            : $"{Provider.Name}: stopped{(HasAccess ? string.Empty : " · access not asked yet")}";

        /// <summary>Ask for access (Quest: the camera permissions).</summary>
        public void RequestAccess(Action<bool> onResult)
        {
            if (!IsSupported)
            {
                onResult?.Invoke(false);
                return;
            }

            Provider.RequestAccess(granted =>
            {
                HasAccess = granted;
                if (!granted)
                    ImmersiveXLog.Warn("Camera: access was denied, so the app can't see the room.");
                onResult?.Invoke(granted);
            });
        }

        /// <summary>Ask for access if needed, start the camera, and wait for its first frame. True when it's running.</summary>
        public async Awaitable<bool> StartAsync(CameraEye eye = CameraEye.Left, Vector2Int? resolution = null)
        {
            if (IsRunning)
                return true;
            if (!IsSupported)
            {
                ImmersiveXLog.Warn($"Camera: {Status}.");
                return false;
            }

            if (!HasAccess)
            {
                bool? granted = null;
                RequestAccess(result => granted = result);
                while (granted == null)
                    await Awaitable.NextFrameAsync();
                if (granted == false)
                    return false;
            }

            if (!Provider.Start(eye, resolution ?? DefaultResolution))
            {
                ImmersiveXLog.Warn($"Camera: {Provider.Name} didn't start.");
                return false;
            }

            for (var waited = 0f; !Provider.IsPlaying && waited < StartTimeoutSeconds; waited += UnityEngine.Time.unscaledDeltaTime)
                await Awaitable.NextFrameAsync();
            if (!Provider.IsPlaying)
            {
                ImmersiveXLog.Warn($"Camera: {Provider.Name} gave no frames within {StartTimeoutSeconds:0} s.");
                return false;
            }

            ImmersiveXLog.Info($"Camera: {Status}.");
            return true;
        }

        public void Stop() => Provider?.Stop();

        /// <summary>
        /// Take a picture, at most <paramref name="maxSize"/> pixels on its longer side, without stalling a frame. Null when
        /// the camera isn't running.
        /// </summary>
        public async Awaitable<CameraFrame> CaptureAsync(int maxSize = 1024)
        {
            if (!IsRunning || Texture == null)
                return null;
            var source = Texture;
            var pose = WorldPose;
            var intrinsics = Intrinsics;
            var time = UnityEngine.Time.realtimeSinceStartup;
            var scale = Mathf.Min(1f, maxSize / (float)Mathf.Max(source.width, source.height));
            var width = Mathf.Max(8, Mathf.RoundToInt(source.width * scale));
            var height = Mathf.Max(8, Mathf.RoundToInt(source.height * scale));

            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, target);
            var request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
            while (!request.done)
                await Awaitable.NextFrameAsync();
            RenderTexture.ReleaseTemporary(target);
            if (request.hasError)
            {
                ImmersiveXLog.Warn("Camera: couldn't read the picture back from the GPU.");
                return null;
            }

            var image = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Camera Frame" };
            image.LoadRawTextureData(request.GetData<byte>());
            image.Apply(false);
            return new CameraFrame(image, pose, intrinsics, time);
        }

        /// <summary>The ray in the world through a viewport point of the camera's current view.</summary>
        public Ray ViewportToRay(Vector2 viewport)
        {
            var pose = WorldPose;
            return new Ray(pose.position, (pose.rotation * Intrinsics.Direction(viewport)).normalized);
        }
    }
}

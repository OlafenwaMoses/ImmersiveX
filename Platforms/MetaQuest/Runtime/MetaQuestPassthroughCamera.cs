using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace ImmersiveX.Platforms.MetaQuest
{
    /// <summary>
    /// The Quest 3 and 3S passthrough cameras (Passthrough Camera API, Horizon OS v74 or later), read through Android's
    /// camera service as Meta's samples do: the two passthrough cameras are found by Meta's camera metadata, the frames
    /// arrive in a <see cref="WebCamTexture"/>, and the lens's intrinsics and pose on the headset come from Camera2.
    /// Needs <c>android.permission.CAMERA</c> and <c>horizonos.permission.HEADSET_CAMERA</c>, which the build adds when bare
    /// metal content asks for the cameras.
    /// </summary>
    [Preserve]
    sealed class MetaQuestPassthroughCamera : IPassthroughCameraProvider
    {
        const string CameraPermission = "android.permission.CAMERA";
        const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";
        const int MinimumHorizonOs = 74;
        const int Yuv420 = 0x23; // ImageFormat.YUV_420_888, the cameras' only format

        sealed class Lens
        {
            public string Id;
            public int Index; // in the camera id list, which is WebCamTexture.devices' order
            public AndroidJavaObject Characteristics;
            public CameraIntrinsics Intrinsics;
            public Pose HeadFromCamera;
            public List<Vector2Int> Sizes = new List<Vector2Int>();
        }

        readonly Dictionary<CameraEye, Lens> _lenses = new Dictionary<CameraEye, Lens>();
        bool? _supported;
        WebCamTexture _texture;
        Lens _lens;

        public string Name => "Quest passthrough camera";

        public bool IsSupported => _supported ??= Detect();

        public void RequestAccess(Action<bool> onResult)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(CameraPermission) && Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
            {
                onResult(true);
                return;
            }

            var callbacks = new PermissionCallbacks();
            var answered = 0;
            void Answer()
            {
                if (++answered < 2)
                    return;
                onResult(Permission.HasUserAuthorizedPermission(CameraPermission) && Permission.HasUserAuthorizedPermission(HeadsetCameraPermission));
            }
            callbacks.PermissionGranted += _ => Answer();
            callbacks.PermissionDenied += _ => Answer();
            Permission.RequestUserPermissions(new[] { CameraPermission, HeadsetCameraPermission }, callbacks);
#else
            onResult(false);
#endif
        }

        public bool Start(CameraEye eye, Vector2Int resolution)
        {
            if (!IsSupported || !_lenses.TryGetValue(eye, out var lens))
                return false;
            Stop();
            var devices = WebCamTexture.devices;
            if (lens.Index >= devices.Length)
            {
                ImmersiveXLog.Warn($"Camera: the {eye} passthrough camera (id {lens.Id}) isn't among the {devices.Length} cameras Unity sees.");
                return false;
            }

            var size = Closest(lens.Sizes, resolution);
            _texture = new WebCamTexture(devices[lens.Index].name, size.x, size.y);
            _texture.Play();
            _lens = lens;
            ImmersiveXLog.Info($"Camera: started the {eye} passthrough camera (id {lens.Id}) at {size.x}×{size.y}; {lens.Intrinsics}; " +
                               $"on the headset at {lens.HeadFromCamera.position:F3}.");
            return true;
        }

        public void Stop()
        {
            if (_texture != null)
            {
                _texture.Stop();
                UnityEngine.Object.Destroy(_texture);
            }

            _texture = null;
            _lens = null;
        }

        // A WebCamTexture is 16×16 until its first frame arrives.
        public bool IsPlaying => _texture != null && _texture.isPlaying && _texture.width > 16;

        public Texture Texture => _texture;

        public CameraIntrinsics Intrinsics => _lens != null ? _lens.Intrinsics : default;

        public Pose WorldPose => _lens != null ? LensPose.World(ImmersiveHost.HeadPose, _lens.HeadFromCamera) : Pose.identity;

        bool Detect()
        {
            if (Application.platform != RuntimePlatform.Android)
                return false;
            try
            {
                var os = HorizonOsVersion();
                if (os > 0 && os < MinimumHorizonOs)
                {
                    ImmersiveXLog.Info($"Camera: Horizon OS v{os} has no Passthrough Camera API (v{MinimumHorizonOs} or later needed).");
                    return false;
                }

                FindLenses();
                if (_lenses.Count == 0)
                    ImmersiveXLog.Info("Camera: this headset has no passthrough cameras the app may read (Quest 3 and 3S have).");
                return _lenses.Count > 0;
            }
            catch (Exception exception)
            {
                ImmersiveXLog.Warn($"Camera: couldn't look for the passthrough cameras ({exception.GetBaseException().Message}).");
                return false;
            }
        }

        static int HorizonOsVersion()
        {
            try
            {
                using (var build = new AndroidJavaClass("vros.os.VrosBuild"))
                {
                    var version = build.CallStatic<int>("getSdkVersion");
                    return version == 10000 ? 0 : version; // 10000: a development build without a release number
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }

        void FindLenses()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                var manager = activity.Call<AndroidJavaObject>("getSystemService", "camera");
                var ids = manager.Call<string[]>("getCameraIdList");
                for (var index = 0; index < ids.Length; index++)
                {
                    var characteristics = manager.Call<AndroidJavaObject>("getCameraCharacteristics", ids[index]);
                    sbyte? source = null, position = null;
                    using (var keys = characteristics.Call<AndroidJavaObject>("getKeys"))
                    {
                        var count = keys.Call<int>("size");
                        for (var i = 0; i < count; i++)
                            using (var key = keys.Call<AndroidJavaObject>("get", i))
                            {
                                var name = key.Call<string>("getName");
                                if (string.Equals(name, "com.meta.extra_metadata.camera_source", StringComparison.OrdinalIgnoreCase))
                                    source = First(characteristics.Call<sbyte[]>("get", key));
                                else if (string.Equals(name, "com.meta.extra_metadata.position", StringComparison.OrdinalIgnoreCase))
                                    position = First(characteristics.Call<sbyte[]>("get", key));
                            }
                    }

                    if (source != 0 || position == null || position.Value > 1) // 0: a passthrough camera; position 0 left, 1 right
                    {
                        characteristics.Dispose();
                        continue;
                    }

                    var lens = new Lens { Id = ids[index], Index = index, Characteristics = characteristics };
                    var intrinsics = Value<float[]>(characteristics, "LENS_INTRINSIC_CALIBRATION"); // fx, fy, cx, cy, skew
                    using (var sensor = Value<AndroidJavaObject>(characteristics, "SENSOR_INFO_PRE_CORRECTION_ACTIVE_ARRAY_SIZE"))
                        lens.Intrinsics = new CameraIntrinsics
                        {
                            FocalLength = new Vector2(intrinsics[0], intrinsics[1]),
                            PrincipalPoint = new Vector2(intrinsics[2], intrinsics[3]),
                            Resolution = new Vector2Int(sensor.Get<int>("right"), sensor.Get<int>("bottom")),
                        };
                    lens.HeadFromCamera = LensPose.HeadFromCamera(Value<float[]>(characteristics, "LENS_POSE_TRANSLATION"),
                        Value<float[]>(characteristics, "LENS_POSE_ROTATION"));
                    using (var map = Value<AndroidJavaObject>(characteristics, "SCALER_STREAM_CONFIGURATION_MAP"))
                    {
                        var sizes = map.Call<AndroidJavaObject[]>("getOutputSizes", Yuv420);
                        foreach (var size in sizes)
                        {
                            lens.Sizes.Add(new Vector2Int(size.Call<int>("getWidth"), size.Call<int>("getHeight")));
                            size.Dispose();
                        }
                    }

                    _lenses[position == 0 ? CameraEye.Left : CameraEye.Right] = lens;
                }

                manager.Dispose();
            }
        }

        static sbyte? First(sbyte[] values) => values != null && values.Length == 1 ? values[0] : (sbyte?)null;

        static T Value<T>(AndroidJavaObject characteristics, string field)
        {
            using (var key = characteristics.GetStatic<AndroidJavaObject>(field))
                return characteristics.Call<T>("get", key);
        }

        /// <summary>The supported size closest in area to <paramref name="wanted"/>, or <paramref name="wanted"/> when none are listed.</summary>
        static Vector2Int Closest(List<Vector2Int> sizes, Vector2Int wanted)
        {
            if (sizes.Count == 0)
                return wanted;
            var best = sizes[0];
            var area = (float)wanted.x * wanted.y;
            foreach (var size in sizes)
                if (Mathf.Abs((float)size.x * size.y - area) < Mathf.Abs((float)best.x * best.y - area))
                    best = size;
            return best;
        }
    }
}

using System.Text;
using ImmersiveX;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// The smallest bare metal app: ImmersiveX only hosts it, and it shows what the host gives it. Its own panel (no content
/// menu) says what the host is doing and has a button for each thing it can do: take a picture with the headset's camera,
/// point at what's in the middle of the picture (depth, the room or a virtual object), map the room, switch MR and VR, show
/// the system menu, and close the app. The content menu's swipe toggles the panel, as a bare metal app's own menu would.
/// </summary>
[AddComponentMenu("ImmersiveX/Demos/Host Check")]
public sealed class HostCheck : MonoBehaviour, IBareMetalApp
{
    const float Width = 620f;
    const float Height = 720f;
    const int UiLayer = 5; // the camera never sees the app's own panel (the simulated one in the editor neither)

    GameObject _panel;
    Text _status;
    Text _result;
    RawImage _picture;
    Texture2D _shown;
    Transform _marker;
    float _nextStatus;
    bool _busy;
    bool _mapping;

    public void OnHostReady()
    {
        Build();
        ImmersiveHost.PassthroughCamera.SimulatedCullingMask = ~(1 << UiLayer);
        var swipes = ImmersiveHost.CreateSwipes();
        if (swipes != null)
        {
            swipes.transform.SetParent(transform, false);
            swipes.Swiped += (hand, direction) => SetPanel(direction == SwipeDirection.Inward);
        }

        SetPanel(true);
        Say("Ready. Swipe across to bring this panel to you, and back to put it away.");
    }

    void Update()
    {
        if (_mapping && ImmersiveHost.Session != null && !ImmersiveHost.Session.IsMappingRoom && PromptPanel.Active == null)
        {
            _mapping = false; // the host's mapping prompts and its closing note are gone: the panel comes back
            SetPanel(true);
            Say(ImmersiveHost.Room != null ? $"Room mapped: {ImmersiveHost.Room.Name}." : "The room wasn't mapped.");
        }

        if (_status == null || Time.unscaledTime < _nextStatus)
            return;
        _nextStatus = Time.unscaledTime + 1f;
        var text = new StringBuilder();
        text.AppendLine($"Platform: {(ImmersiveHost.Platform != null ? ImmersiveHost.Platform.DisplayName : "none")} · view {ImmersiveHost.View}");
        text.AppendLine($"Room access: {(ImmersiveHost.HasRoomAccess ? "yes" : "no")} · room: {(ImmersiveHost.Room != null ? ImmersiveHost.Room.Name : "not mapped")}");
        text.AppendLine($"Floor at {ImmersiveHost.FloorHeight:0.00} m · head at {ImmersiveHost.HeadPose.position.y:0.00} m");
        text.AppendLine($"Camera: {ImmersiveHost.PassthroughCamera.Status}");
        text.Append($"Anchors: {(ImmersiveHost.Anchors.IsAvailable ? ImmersiveHost.Anchors.CanPersist ? "saved between runs" : "this run only" : "none")}");
        _status.text = text.ToString();
    }

    async void TakePicture()
    {
        if (_busy)
            return;
        _busy = true;
        Say("Taking a picture…");
        try
        {
            var camera = ImmersiveHost.PassthroughCamera;
            if (!await camera.StartAsync())
            {
                Say($"No camera: {camera.Status}.");
                return;
            }

            var frame = await camera.CaptureAsync(512);
            if (frame == null)
            {
                Say("The camera gave no picture.");
                return;
            }

            if (_shown != null)
                Destroy(_shown);
            _shown = frame.Image; // kept for the panel: not disposed
            _picture.texture = _shown;
            _picture.color = Color.white;
            Say($"Picture {frame.Width}×{frame.Height} from {frame.Pose.position:F2}. {frame.Intrinsics}.");
            Point(frame.ViewportToRay(new Vector2(0.5f, 0.5f)), "the middle of the picture");
        }
        finally
        {
            _busy = false;
        }
    }

    void PointAhead()
    {
        var camera = ImmersiveHost.PassthroughCamera;
        if (camera.IsRunning)
            Point(camera.ViewportToRay(new Vector2(0.5f, 0.5f)), "the middle of the camera's view");
        else
            Point(new Ray(ImmersiveHost.HeadPose.position, ImmersiveHost.HeadPose.forward), "straight ahead");
    }

    void Point(Ray ray, string what)
    {
        if (!ImmersiveHost.Raycast(ray, out var hit, 8f, WorldSurfaces.All, ~(1 << UiLayer)))
        {
            Say($"Nothing within 8 m at {what}.");
            return;
        }

        if (_marker == null)
        {
            _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            _marker.name = "Host Check Marker";
            Destroy(_marker.GetComponent<Collider>());
            _marker.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.Create(Brand.Gold);
            _marker.localScale = Vector3.one * 0.04f;
        }

        _marker.position = hit.Point;
        Say($"At {what}: {hit}{(hit.Collider != null ? $" ({hit.Collider.name})" : string.Empty)}.");
    }

    void MapRoom()
    {
        if (!ImmersiveHost.HasRoomAccess)
        {
            Say("Mapping the room needs room access, which wasn't granted.");
            return;
        }

        // the host's mapping prompts appear in front of the user, where this panel is: put it away until they're done
        SetPanel(false);
        _mapping = ImmersiveHost.MapRoom();
        if (!_mapping)
        {
            SetPanel(true);
            Say("The room is already being mapped.");
        }
    }

    void OpenSystemMenu()
    {
        if (!ImmersiveHost.OpenSystemMenu())
            Say("The system menu isn't available here (it is on a headset).");
    }

    void SetPanel(bool open)
    {
        if (_panel == null)
            return;
        if (open)
        {
            var user = ImmersiveHost.UserPose;
            var pose = UserRelativePlacement.Apply(user, new Vector3(0f, -0.15f, 0.7f));
            _panel.transform.SetPositionAndRotation(pose.position, user.rotation);
        }

        _panel.SetActive(open);
    }

    void Say(string message)
    {
        if (_result != null)
            _result.text = message;
        ImmersiveXLog.Info($"Host Check: {message}");
    }

    void Build()
    {
        _panel = new GameObject("Host Check Panel");
        _panel.transform.SetParent(transform, false);
        var canvas = WorldUi.CreateCanvas(_panel.transform, new Vector2(Width, Height));
        var background = canvas.Find("Background").GetComponent<Image>();
        background.color = Brand.Panel;
        Brand.Round(background, 28f);
        Brand.Hairline(canvas, Vector2.zero, new Vector2(Width, Height), 28f, Brand.LineGold);

        var title = WorldUi.Label(canvas, "Title", "Host Check", 30, new Vector2(0f, Height / 2f - 44f), new Vector2(Width - 60f, 44f), FontStyle.Bold);
        title.color = Brand.Gold;
        var note = WorldUi.Label(canvas, "Note", "Bare metal content: ImmersiveX only hosts this app.", 18, new Vector2(0f, Height / 2f - 80f), new Vector2(Width - 60f, 28f));
        note.color = Brand.Ink3;
        _status = WorldUi.Label(canvas, "Status", string.Empty, 18, new Vector2(0f, Height / 2f - 170f), new Vector2(Width - 60f, 140f));
        _status.color = Brand.Ink2;

        _picture = WorldUi.Box(canvas, "Picture", new Vector2(0f, -10f), new Vector2(256f, 192f)).gameObject.AddComponent<RawImage>();
        _picture.color = Brand.Card;
        _picture.raycastTarget = false;
        _result = WorldUi.Label(canvas, "Result", string.Empty, 18, new Vector2(0f, -150f), new Vector2(Width - 60f, 72f));

        var row1 = -228f;
        var row2 = -292f;
        Brand.Pill(canvas, "Take picture", new Vector2(-196f, row1), new Vector2(180f, 52f), true, TakePicture);
        Brand.Pill(canvas, "Point", new Vector2(0f, row1), new Vector2(180f, 52f), false, PointAhead);
        Brand.Pill(canvas, "Map room", new Vector2(196f, row1), new Vector2(180f, 52f), false, MapRoom);
        Brand.Pill(canvas, "MR / VR", new Vector2(-196f, row2), new Vector2(180f, 52f), false,
            () => ImmersiveHost.SetView(ImmersiveHost.View == ViewMode.Mixed ? ViewMode.Virtual : ViewMode.Mixed));
        Brand.Pill(canvas, "Quest menu", new Vector2(0f, row2), new Vector2(180f, 52f), false, OpenSystemMenu);
        Brand.Pill(canvas, "Close app", new Vector2(196f, row2), new Vector2(180f, 52f), false, ImmersiveHost.Quit);

        foreach (var child in _panel.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = UiLayer;
        PanelMover.Attach(_panel, new Vector2(Width, Height) * 0.001f);
    }

    void OnDestroy()
    {
        if (_shown != null)
            Destroy(_shown);
    }
}

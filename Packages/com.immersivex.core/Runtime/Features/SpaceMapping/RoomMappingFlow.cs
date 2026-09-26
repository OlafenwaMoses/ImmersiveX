using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ImmersiveX
{
    /// <summary>
    /// Maps the room and remembers it (F2). Run by <see cref="ImmersiveXSession"/> during its Space step.
    /// <list type="bullet">
    /// <item>A room ImmersiveX has saved is recognised and loaded silently.</item>
    /// <item>A room the platform already knows (Quest Space Setup) is saved automatically, with a short note.</item>
    /// <item>An unmapped room gets a short briefing, then the platform's room scan (Quest) or a guided look-around
    /// (platforms where the app detects the room itself). The result is saved automatically, with no extra prompt.</item>
    /// </list>
    /// Room tracking is polled 4 times a second while mapping and paused afterwards, so it costs nothing at run time.
    /// </summary>
    public sealed class RoomMappingFlow
    {
        const float PollSeconds = 0.25f;
        const float StableSeconds = 0.75f;          // room data counts as complete once unchanged this long
        const float RoomDataAfterScanSeconds = 8f;
        const float ScanOpenTimeoutSeconds = 5f;    // the system scan normally pauses the app within a second
        const float NoticeSeconds = 5f;
        const float GuidedPollSeconds = 0.5f;

        readonly ImmersiveXSession _session;
        readonly ARPlaneManager _planes;
        readonly ARBoundingBoxManager _boxes;
        readonly SpaceLibrary _library;
        PromptPanel _prompt;
        int _promptToken;
        int _pauseCount;
        bool _paused;
        bool _finishGuided;
        bool _skipGuided;
        Pose _floorInSession = Pose.identity; // relative to the XR Origin's trackables, like the planes it came from

        public RoomMappingFlow(ImmersiveXSession session, ARPlaneManager planes, ARBoundingBoxManager boxes, SpaceLibrary library)
        {
            _session = session;
            _planes = planes;
            _boxes = boxes;
            _library = library;
        }

        /// <summary>The current room, or null when none is mapped.</summary>
        public MappedSpace Space { get; private set; }

        public WalkableArea Walkable { get; private set; }

        /// <summary>
        /// The floor's pose in world space this session; the walkable area is in this pose's local space.
        /// Kept relative to the XR Origin, so it stays on the real floor even if the rig moves.
        /// </summary>
        public Pose FloorPose => Trackables != null ? _floorInSession.GetTransformedBy(Trackables) : _floorInSession;

        public RoomSnapshot LastRoom { get; private set; }

        /// <summary>A one-line description for status displays.</summary>
        public string Status { get; private set; } = "Not mapped yet.";

        public bool IsBusy { get; private set; }

        ImmersiveXSettings Settings => _session.Settings;
        PlatformCapabilities Capabilities => _session.Platform.Capabilities;
        Transform Head => _session.Origin.Camera.transform;

        public void OnApplicationPause(bool paused)
        {
            _paused = paused;
            if (paused)
                _pauseCount++;
        }

        /// <summary>Finish the guided look-around now (once a floor has been found).</summary>
        public void FinishGuidedScan() => _finishGuided = true;

        /// <summary>Leave the guided look-around without mapping.</summary>
        public void SkipGuidedScan() => _skipGuided = true;

        /// <summary>Map the room. <paramref name="rescan"/> goes straight to the scan and updates the current room.</summary>
        public IEnumerator Map(bool rescan)
        {
            if (IsBusy)
                yield break;
            IsBusy = true;
            SetTracking(true);

            RoomSnapshot room = null;
            if (Capabilities.RoomData == RoomDataSource.SystemScan)
                yield return MapWithSystemScan(rescan, result => room = result);
            else if (Capabilities.RoomData == RoomDataSource.LiveDetection)
                yield return GuidedScan(result => room = result);

            if (room != null && room.HasFloor)
                yield return Remember(room, rescan);
            else if (Space == null)
                Status = "No room mapped. Content still works; use Rescan room to map it.";

            if (Settings.PauseRoomTrackingWhenMapped)
                SetTracking(false);
            IsBusy = false;
        }

        /// <summary>Delete ImmersiveX's saved copy of the current room (the platform's own room data is untouched).</summary>
        public void Forget()
        {
            if (Space == null)
                return;
            _library.Delete(Space.Id);
            ImmersiveXLog.Info($"Forgot {Space.Name}.");
            Space = null;
            Walkable = null;
            Status = "Room forgotten. Use Rescan room to map it again.";
        }

        /// <summary>Draw the walkable area on the floor for a few seconds.</summary>
        public void ShowWalkableArea()
        {
            if (Walkable != null)
                WalkableAreaView.Show(Walkable, FloorPose, Settings.ShowWalkableAreaSeconds, Trackables);
        }

        // ---------------------------------------------------------------- Platform room scan (Quest Space Setup)

        IEnumerator MapWithSystemScan(bool rescan, Action<RoomSnapshot> done)
        {
            RoomSnapshot room = null;
            if (!rescan)
            {
                Status = "Loading your room…";
                yield return WaitForRoom(Settings.RoomDataWaitSeconds, result => room = result);
                if (room.HasFloor)
                {
                    done(room);
                    yield break;
                }

                var start = false;
                yield return Briefing(result => start = result);
                if (!start)
                {
                    Status = "Room mapping skipped. Use Rescan room to map it.";
                    done(null);
                    yield break;
                }
            }

            while (true)
            {
                if (!Services.TryGet<ISpaceProvider>(out var provider) || !provider.CanRequestSystemScan)
                {
                    ShowNotice("Room scanning isn't available", "This device can't start a room scan from the app. Content still works.");
                    done(null);
                    yield break;
                }

                Status = "Waiting for the room scan…";
                var pausesBefore = _pauseCount;
                if (!provider.RequestSystemScan())
                {
                    ShowNotice("Room scanning isn't available", "The device didn't start a room scan (the Meta XR Simulator can't run Space Setup). Content still works.");
                    done(null);
                    yield break;
                }

                yield return WaitForSystemScan(pausesBefore);
                yield return WaitForRoom(RoomDataAfterScanSeconds, result => room = result);
                if (room.HasFloor)
                {
                    done(room);
                    yield break;
                }

                var retry = false;
                yield return Choice("The room scan didn't finish", "No floor was found. Try the scan again, or continue without room mapping.",
                    "Try again", "Continue", result => retry = result);
                if (!retry)
                {
                    done(null);
                    yield break;
                }
            }
        }

        IEnumerator Briefing(Action<bool> result)
        {
            const string body = "So your content stays where you put it, Quest will guide you to look around slowly " +
                                "and walk around your space. It takes about a minute.";
            var decided = false;
            var start = false;
            var prompt = ShowPrompt("Let's map your room", body, "Start now", () => { decided = true; start = true; },
                "Skip", () => { decided = true; start = false; });

            var countdown = Settings.AutoStartRoomScanSeconds;
            while (!decided)
            {
                if (countdown > 0f)
                {
                    countdown -= Time.unscaledDeltaTime;
                    prompt.SetBody($"{body}\n\nStarting in {Mathf.Max(1, Mathf.CeilToInt(countdown))}…");
                    if (countdown <= 0f)
                    {
                        decided = true;
                        start = true;
                    }
                }

                yield return null;
            }

            HidePrompt();
            result(start);
        }

        IEnumerator WaitForSystemScan(int pausesBefore)
        {
            var waited = 0f;
            while (_pauseCount == pausesBefore && waited < ScanOpenTimeoutSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (_pauseCount == pausesBefore)
            {
                ImmersiveXLog.Warn("The room scan didn't open.");
                yield break;
            }

            while (_paused)
                yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
        }

        // ---------------------------------------------------------------- Guided look-around (app detects the room)

        IEnumerator GuidedScan(Action<RoomSnapshot> done)
        {
            var tracker = new CoverageTracker(Settings.ScanMaxTurnSpeed, Settings.ScanIdleSeconds);
            _finishGuided = false;
            _skipGuided = false;
            Status = "Looking around…";
            var prompt = ShowPrompt("Look around slowly", string.Empty, "Done", FinishGuidedScan, "Skip", SkipGuidedScan);

            RoomSnapshot room = null;
            var nextPoll = 0f;
            while (true)
            {
                var deltaTime = Time.unscaledDeltaTime;
                tracker.Update(Head.eulerAngles.y, deltaTime);
                nextPoll -= deltaTime;
                if (nextPoll <= 0f)
                {
                    room = RoomSnapshot.Capture(_planes, _boxes);
                    nextPoll = GuidedPollSeconds;
                }

                var hasFloor = room != null && room.HasFloor;
                prompt.SetProgress(tracker);
                prompt.SetPrimaryInteractable(hasFloor);
                prompt.SetBody(Coaching(tracker, room));
                KeepInView(prompt);

                if (_skipGuided)
                {
                    room = null;
                    break;
                }

                if (hasFloor && (_finishGuided || tracker.Coverage >= 1f || (tracker.Coverage >= 0.75f && room.WallLengths.Count >= 2)))
                    break;
                yield return null;
            }

            HidePrompt();
            done(room);
        }

        static string Coaching(CoverageTracker tracker, RoomSnapshot room)
        {
            var pace = tracker.Coaching == ScanCoaching.SlowDown ? "A little slower, so the room can be mapped."
                : tracker.Coaching == ScanCoaching.KeepTurning ? "Turn slowly all the way around."
                : "Good. Keep turning slowly.";
            var floor = room != null && room.HasFloor ? "Floor found." : "Look down at the floor around you too.";
            return $"{pace}\n{floor}\nDirections covered: {tracker.CoveredCount} of {CoverageTracker.Sectors}";
        }

        void KeepInView(PromptPanel prompt)
        {
            // Lazy follow: stay put while the user looks at it, drift back in front when they turn away.
            var toPanel = prompt.transform.position - Head.position;
            toPanel.y = 0f;
            var forward = Vector3.ProjectOnPlane(Head.forward, Vector3.up);
            if (toPanel.sqrMagnitude < 1e-4f || forward.sqrMagnitude < 1e-4f || Vector3.Angle(toPanel, forward) < 30f)
                return;

            var user = UserRelativePlacement.UserPose(Head);
            var target = UserRelativePlacement.Apply(user, new Vector3(0f, -0.12f, 0.9f));
            var t = Mathf.Clamp01(Time.unscaledDeltaTime * 3f);
            prompt.transform.SetPositionAndRotation(
                Vector3.Lerp(prompt.transform.position, target.position, t),
                Quaternion.Slerp(prompt.transform.rotation, user.rotation, t));
        }

        // ---------------------------------------------------------------- Remember the room

        IEnumerator Remember(RoomSnapshot room, bool rescan)
        {
            var nativeKey = Capabilities.RoomData == RoomDataSource.SystemScan ? room.FloorId : string.Empty;
            var signature = SpaceSignature.From(room);
            MappedSpace space = null;
            var announce = true;

            if (rescan && Space != null)
            {
                space = Space; // the user asked to rescan this room: update it
            }
            else
            {
                var match = SpaceMatcher.Match(_library.LoadAll(), signature, nativeKey);
                if (match.Confidence == MatchConfidence.High)
                {
                    space = match.Space;
                    announce = false; // known room: load silently
                }
                else if (match.Confidence == MatchConfidence.Medium)
                {
                    var same = false;
                    yield return Choice($"Is this {match.Space.Name}?", "This room looks like one you've mapped before.",
                        "Yes, same room", "No, a new room", result => same = result);
                    if (same)
                    {
                        space = match.Space;
                        announce = false;
                    }
                }
            }

            if (space == null)
                space = MappedSpace.Create(_library.NextName(), _session.Platform.Id);

            var walkable = WalkableArea.Build(room.FloorPolygon, Settings.WallMargin);
            space.Update(room, nativeKey, Settings.WallMargin, walkable.Area);
            try
            {
                _library.Save(space); // saved automatically: no confirmation needed
            }
            catch (Exception exception)
            {
                ImmersiveXLog.Error($"Couldn't save the room: {exception.Message}");
            }

            Space = space;
            Walkable = walkable;
            _floorInSession = Trackables != null ? ToLocal(Trackables, room.FloorPose) : room.FloorPose;
            LastRoom = room;
            Status = $"{space.Name} · {walkable.Area:0.0} m² walkable";
            ImmersiveXLog.Info($"Room {(announce ? "saved" : "recognised")}: {space.Name} · floor {Polygon.Area(room.FloorPolygon):0.0} m² · " +
                               $"walkable {walkable.Area:0.0} m² · {room.WallLengths.Count} walls · {room.FurnitureCount} furniture · " +
                               $"ceiling {room.CeilingHeight:0.00} m");

            var bounds = Polygon.Bounds(room.FloorPolygon);
            ImmersiveXLog.Info($"Floor pose: position {room.FloorPose.position:F2}, rotation {room.FloorPose.rotation.eulerAngles:F0}, " +
                               $"up {room.FloorPose.rotation * Vector3.up:F2} · polygon {room.FloorPolygon.Length} points, " +
                               $"{bounds.width:0.00} × {bounds.height:0.00} m around {bounds.center:F2} · head {Head.position:F2}");

            if (announce)
            {
                ShowNotice($"Room saved: {space.Name}",
                    $"{walkable.Area:0.0} m² to walk in · {room.WallLengths.Count} walls · {room.FurnitureCount} pieces of furniture.");
                ShowWalkableArea();
            }
        }

        // ---------------------------------------------------------------- Room data and UI helpers

        /// <summary>Where AR Foundation puts planes and anchors: session space, under the XR Origin.</summary>
        Transform Trackables => _session.Origin != null ? _session.Origin.TrackablesParent : null;

        static Pose ToLocal(Transform parent, Pose world) =>
            new Pose(parent.InverseTransformPoint(world.position), Quaternion.Inverse(parent.rotation) * world.rotation);

        IEnumerator WaitForRoom(float timeoutSeconds, Action<RoomSnapshot> done)
        {
            var elapsed = 0f;
            var stable = 0f;
            var lastPlanes = -1;
            var lastBoxes = -1;
            RoomSnapshot room;
            while (true)
            {
                room = RoomSnapshot.Capture(_planes, _boxes);
                if (room.HasFloor)
                {
                    stable = room.PlaneCount == lastPlanes && room.FurnitureCount == lastBoxes ? stable + PollSeconds : 0f;
                    if (stable >= StableSeconds)
                        break;
                }

                lastPlanes = room.PlaneCount;
                lastBoxes = room.FurnitureCount;
                if (elapsed >= timeoutSeconds)
                    break;
                yield return new WaitForSecondsRealtime(PollSeconds);
                elapsed += PollSeconds;
            }

            done(room);
        }

        IEnumerator Choice(string title, string body, string yes, string no, Action<bool> result)
        {
            bool? choice = null;
            ShowPrompt(title, body, yes, () => choice = true, no, () => choice = false);
            while (choice == null)
                yield return null;
            HidePrompt();
            result(choice.Value);
        }

        PromptPanel ShowPrompt(string title, string body, string primary = null, Action onPrimary = null, string secondary = null, Action onSecondary = null)
        {
            if (_prompt == null)
                _prompt = PromptPanel.Create();
            _promptToken++;
            ImmersiveXLog.Info($"Prompt: {title}");
            _prompt.Show(title, body, primary, onPrimary, secondary, onSecondary);
            _prompt.PlaceInFrontOf(Head);
            return _prompt;
        }

        void HidePrompt()
        {
            _promptToken++;
            if (_prompt != null)
                _prompt.Hide();
        }

        void ShowNotice(string title, string body)
        {
            ShowPrompt(title, body);
            _session.StartCoroutine(HideAfter(_promptToken, NoticeSeconds));
        }

        IEnumerator HideAfter(int token, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (token == _promptToken)
                HidePrompt();
        }

        void SetTracking(bool on)
        {
            if (_planes != null)
                _planes.enabled = on;
            if (_boxes != null)
                _boxes.enabled = on;
        }
    }
}

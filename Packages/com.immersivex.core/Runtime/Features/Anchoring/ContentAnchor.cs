using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ImmersiveX
{
    /// <summary>Where <see cref="ContentAnchor.Restore"/> found a piece of content.</summary>
    public sealed class RestoredPlacement
    {
        public Pose Pose;

        /// <summary>The saved size, or 0 when none was saved.</summary>
        public float Size;

        /// <summary>True when the saved spatial anchor was found; false when the pose saved with the room was used.</summary>
        public bool FromAnchor;
    }

    /// <summary>
    /// Keeps a piece of content in the same real-world spot (F3) with the platform's spatial anchors: through tracking
    /// corrections, the headset coming off and on, and app restarts. The pose saved relative to the room is the fallback.
    /// Content components (Immersive Content, Immersive Media) add it and drive it:
    /// <list type="bullet">
    /// <item><see cref="Restore"/> at start-up finds the saved anchor, else the saved pose, else nothing. With nothing
    /// saved, the owner applies its default placement and commits it.</item>
    /// <item><see cref="Commit"/> is called whenever the user lets go of the content. The pose is saved at once; half a
    /// second later a new anchor is created there and saved, and the old one is erased.</item>
    /// <item>While the app runs, the content follows its anchor. When the device corrects its tracking (after the headset
    /// was off, or a recentre), the anchor moves in the app's space and the content moves with it, so it stays put in the
    /// room. While the anchor's tracking is lost, <see cref="IsLost"/> is true and the owner hides the content.</item>
    /// </list>
    /// Content moves only when the user moves it.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added by content components
    public sealed class ContentAnchor : MonoBehaviour
    {
        const float CommitDelaySeconds = 0.5f;
        const float LocaliseTimeoutSeconds = 5f;
        const float FollowDistance = 0.001f;
        const float FollowDegrees = 0.05f;
        const float SaveRetrySeconds = 1f;

        /// <summary>
        /// Saves, loads and erases run one at a time across all content: runtimes can fail overlapping requests (the Meta
        /// XR Simulator does when two pieces of media anchor at once).
        /// </summary>
        static bool s_storeBusy;

        ARAnchor _anchor;
        Pose _alignedTo; // the anchor's world pose when the content was last lined up with it
        bool _tracked;
        int _commits;
        readonly List<string> _stale = new List<string>(); // saved anchors to erase once a newer one is saved

        /// <summary>What the placement is saved under. Set it before <see cref="Restore"/> or <see cref="Commit"/>.</summary>
        public string Key { get; set; }

        /// <summary>Keep the content level: it only ever turns about the up axis (standing media).</summary>
        public bool KeepUpright { get; set; }

        /// <summary>The id of the saved anchor for the current placement, or "" while there's none.</summary>
        public string SavedAnchorId { get; private set; } = string.Empty;

        /// <summary>True while the content's anchor has lost tracking: hide the content until it's found again.</summary>
        public bool IsLost => _anchor != null && _tracked && _anchor.trackingState == TrackingState.None;

        /// <summary>Raised after the content moved with its anchor.</summary>
        public event Action Moved;

        /// <summary>One line for status displays.</summary>
        public string Status => _anchor == null ? "no anchor"
            : string.IsNullOrEmpty(SavedAnchorId) ? $"anchored (not saved) · {_anchor.trackingState}"
            : $"anchor {Short(SavedAnchorId)} saved · {_anchor.trackingState}";

        /// <summary>
        /// Find where the content was left and call <paramref name="done"/>. It gets the anchor's pose when the saved anchor
        /// is found, the pose saved with the room when it isn't, or null when nothing was saved under <see cref="Key"/>.
        /// </summary>
        public async void Restore(Action<RestoredPlacement> done)
        {
            RestoredPlacement restored = null;
            try
            {
                restored = await RestoreAsync();
            }
            catch (Exception exception)
            {
                ImmersiveXLog.Warn($"Anchor: couldn't restore '{Key}' ({exception.Message}).");
            }

            done(restored);
        }

        async Awaitable<RestoredPlacement> RestoreAsync()
        {
            if (string.IsNullOrEmpty(Key) || !RoomPlacements.TryLoad(Key, out var saved))
                return null;

            var restored = new RestoredPlacement { Pose = saved.Pose, Size = saved.Size };
            var manager = Manager();
            if (string.IsNullOrEmpty(saved.AnchorId) || !Guid.TryParse(saved.AnchorId, out var guid) ||
                manager == null || !manager.descriptor.supportsLoadAnchor)
                return Upright(restored);

            await EnterStore();
            Result<ARAnchor> loaded;
            try
            {
                loaded = await manager.TryLoadAnchorAsync(new SerializableGuid(guid));
            }
            finally
            {
                LeaveStore();
            }

            var anchor = loaded.status.IsSuccess() ? loaded.value : null;
            if (anchor == null)
            {
                ImmersiveXLog.Info($"Anchor: couldn't load {Short(saved.AnchorId)} for '{Key}' ({Describe(loaded.status)}); " +
                                   "using the pose saved with the room.");
                return Upright(restored);
            }

            // Loaded; wait for the device to find it in the room.
            for (var waited = 0f; this != null && anchor != null && anchor.trackingState != TrackingState.Tracking && waited < LocaliseTimeoutSeconds;
                 waited += Time.unscaledDeltaTime)
                await Awaitable.NextFrameAsync();
            if (this == null || anchor == null || anchor.trackingState != TrackingState.Tracking)
            {
                if (anchor != null && manager != null && manager.enabled)
                    manager.TryRemoveAnchor(anchor);
                ImmersiveXLog.Info($"Anchor: {Short(saved.AnchorId)} wasn't found in the room within {LocaliseTimeoutSeconds:0} s; " +
                                   "using the pose saved with the room.");
                return this == null ? null : Upright(restored);
            }

            Adopt(anchor);
            SavedAnchorId = saved.AnchorId;
            restored.Pose = new Pose(anchor.transform.position, anchor.transform.rotation);
            restored.FromAnchor = true;
            return Upright(restored);
        }

        /// <summary>
        /// The content now stands at <paramref name="pose"/> with <paramref name="size"/> (0 = none). The pose is saved at
        /// once; half a second later, unless it moves again, it's anchored there and the anchor saved.
        /// </summary>
        public void Commit(Pose pose, float size)
        {
            if (string.IsNullOrEmpty(Key))
                return;
            var previous = RoomPlacements.Save(Key, pose, size);
            if (!string.IsNullOrEmpty(previous) && !_stale.Contains(previous))
                _stale.Add(previous);
            SavedAnchorId = string.Empty;
            AnchorAfterDelay(++_commits, pose);
        }

        async void AnchorAfterDelay(int commit, Pose pose)
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(CommitDelaySeconds);
                var manager = Manager();
                if (this == null || commit != _commits || manager == null)
                    return; // moved again (the newest commit anchors it), or this platform has no anchors

                var added = await manager.TryAddAnchorAsync(pose);
                if (!added.status.IsSuccess() || added.value == null)
                {
                    ImmersiveXLog.Warn($"Anchor: couldn't create one for '{Key}' ({Describe(added.status)}); it'll come back from the pose saved with the room.");
                    return;
                }

                if (this == null || commit != _commits)
                {
                    if (manager != null && manager.enabled)
                        manager.TryRemoveAnchor(added.value);
                    return;
                }

                var previous = _anchor;
                Adopt(added.value);
                if (previous != null && manager.enabled)
                    manager.TryRemoveAnchor(previous);
                if (!manager.descriptor.supportsSaveAnchor)
                    return; // this platform can't keep anchors between runs: the pose saved with the room brings it back

                var saved = await Save(manager, added.value);
                if (!saved.status.IsSuccess() && this != null && commit == _commits)
                {
                    await Awaitable.WaitForSecondsAsync(SaveRetrySeconds); // once more: runtimes can refuse a save while busy
                    if (this != null && commit == _commits && _anchor == added.value)
                        saved = await Save(manager, added.value);
                }

                if (!saved.status.IsSuccess())
                {
                    if (this == null || commit != _commits)
                        return;
                    ImmersiveXLog.Warn($"Anchor: couldn't save the anchor for '{Key}' ({Describe(saved.status)}); it'll come back from the pose saved with the room.");
                    await EraseStale(manager); // the old anchors are out of date either way
                    return;
                }

                var id = saved.value.guid.ToString();
                if (this == null || commit != _commits)
                {
                    await Erase(manager, id); // moved again meanwhile: this one is already out of date
                    return;
                }

                RoomPlacements.SetAnchor(Key, id);
                SavedAnchorId = id;
                ImmersiveXLog.Info($"Anchor: '{Key}' is anchored as {Short(id)}.");
                await EraseStale(manager);
            }
            catch (Exception exception)
            {
                ImmersiveXLog.Warn($"Anchor: couldn't anchor '{Key}' ({exception.Message}).");
            }
        }

        async Awaitable EraseStale(ARAnchorManager manager)
        {
            while (_stale.Count > 0 && this != null)
            {
                var id = _stale[0];
                _stale.RemoveAt(0);
                await Erase(manager, id);
            }
        }

        static async Awaitable<Result<SerializableGuid>> Save(ARAnchorManager manager, ARAnchor anchor)
        {
            await EnterStore();
            try
            {
                return await manager.TrySaveAnchorAsync(anchor);
            }
            finally
            {
                LeaveStore();
            }
        }

        static async Awaitable Erase(ARAnchorManager manager, string id)
        {
            if (manager == null || !manager.descriptor.supportsEraseAnchor || !Guid.TryParse(id, out var guid))
                return;
            await EnterStore();
            try
            {
                var status = await manager.TryEraseAnchorAsync(new SerializableGuid(guid));
                if (!status.IsSuccess())
                    ImmersiveXLog.Info($"Anchor: couldn't erase {Short(id)} ({Describe(status)}).");
            }
            finally
            {
                LeaveStore();
            }
        }

        static async Awaitable EnterStore()
        {
            while (s_storeBusy)
                await Awaitable.NextFrameAsync();
            s_storeBusy = true;
        }

        static void LeaveStore() => s_storeBusy = false;

        /// <summary>Erase saved anchors from the device (for example when their room is forgotten).</summary>
        public static async void EraseSaved(IReadOnlyCollection<string> anchorIds)
        {
            var manager = Manager();
            if (anchorIds == null || anchorIds.Count == 0 || manager == null || !manager.descriptor.supportsEraseAnchor)
                return;
            try
            {
                foreach (var id in anchorIds)
                    await Erase(manager, id);
                ImmersiveXLog.Info($"Anchor: erased {anchorIds.Count} saved anchor(s).");
            }
            catch (Exception exception)
            {
                ImmersiveXLog.Warn($"Anchor: couldn't erase saved anchors ({exception.Message}).");
            }
        }

        void Adopt(ARAnchor anchor)
        {
            _anchor = anchor;
            _alignedTo = new Pose(anchor.transform.position, anchor.transform.rotation);
            _tracked = anchor.trackingState == TrackingState.Tracking;
            anchor.gameObject.name = $"Anchor ({name})";
        }

        void LateUpdate()
        {
            if (_anchor == null || _anchor.trackingState != TrackingState.Tracking)
                return;
            _tracked = true;

            var now = new Pose(_anchor.transform.position, _anchor.transform.rotation);
            if ((now.position - _alignedTo.position).sqrMagnitude < FollowDistance * FollowDistance &&
                Quaternion.Angle(now.rotation, _alignedTo.rotation) < FollowDegrees)
                return;

            // The device corrected its tracking: the anchor moved in the app's space, so the content moves with it.
            var turn = now.rotation * Quaternion.Inverse(_alignedTo.rotation);
            var rotation = turn * transform.rotation;
            transform.SetPositionAndRotation(now.position + turn * (transform.position - _alignedTo.position),
                KeepUpright ? FloorGrabTransformer.Level(rotation) : rotation);
            _alignedTo = now;
            Moved?.Invoke();
        }

        void OnDestroy()
        {
            var manager = Manager();
            if (_anchor != null && manager != null && manager.enabled)
                manager.TryRemoveAnchor(_anchor); // from this session only; the saved copy stays
        }

        RestoredPlacement Upright(RestoredPlacement restored)
        {
            if (KeepUpright)
                restored.Pose.rotation = FloorGrabTransformer.Level(restored.Pose.rotation);
            return restored;
        }

        /// <summary>The session's anchor manager, when it's running.</summary>
        static ARAnchorManager Manager()
        {
            var session = ImmersiveXSession.Instance;
            var manager = session != null && session.Origin != null ? session.Origin.GetComponent<ARAnchorManager>() : null;
            return manager != null && manager.enabled && manager.subsystem != null && manager.subsystem.running ? manager : null;
        }

        /// <summary>The first 8 characters of an anchor id, for logs.</summary>
        public static string Short(string id)
        {
            var compact = (id ?? string.Empty).Replace("-", string.Empty);
            return compact.Length == 0 ? "-" : compact.Substring(0, Math.Min(8, compact.Length));
        }

        static string Describe(XRResultStatus status) => $"{status.statusCode}, native code {status.nativeStatusCode}";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_storeBusy = false;
    }
}

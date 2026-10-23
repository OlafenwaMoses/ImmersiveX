using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ImmersiveX
{
    /// <summary>
    /// The platform's spatial anchors for bare metal content (<see cref="ImmersiveHost.Anchors"/>): pin a pose to the real
    /// world, save it so it's found again after the app restarts, load and erase saved ones. The app keeps the ids.
    /// Saves, loads and erases take turns with Immersive Content's own anchors, since runtimes fail overlapping requests.
    /// For an object that should simply stay where it's put, <see cref="ImmersiveHost.KeepInPlace"/> does all of this.
    /// </summary>
    public sealed class HostAnchors
    {
        const float LocaliseTimeoutSeconds = 5f;
        const int AddedWithinFrames = 10;

        /// <summary>The session's anchor manager, while it's running.</summary>
        public ARAnchorManager Manager => ContentAnchor.Manager();

        public bool IsAvailable => Manager != null;

        /// <summary>True when anchors can be saved and loaded again in a later run.</summary>
        public bool CanPersist => Manager != null && Manager.descriptor.supportsSaveAnchor && Manager.descriptor.supportsLoadAnchor;

        /// <summary>Pin <paramref name="pose"/> to the real world for this run. Null when the platform has no anchors.</summary>
        public async Awaitable<ARAnchor> AddAsync(Pose pose)
        {
            var manager = Manager;
            if (manager == null)
                return null;
            var added = await manager.TryAddAnchorAsync(pose);
            if (!added.status.IsSuccess() || added.value == null)
            {
                ImmersiveXLog.Warn($"Anchors: couldn't add one ({Describe(added.status)}).");
                return null;
            }

            // Hand it over once the subsystem has reported it: removing a pending anchor throws (XR Simulation does).
            var anchor = added.value;
            for (var frames = 0; anchor != null && anchor.pending && frames < AddedWithinFrames; frames++)
                await Awaitable.NextFrameAsync();
            return anchor;
        }

        /// <summary>Save <paramref name="anchor"/> for later runs. Returns its id, or null when it couldn't be saved.</summary>
        public async Awaitable<string> SaveAsync(ARAnchor anchor)
        {
            var manager = Manager;
            if (manager == null || anchor == null || !manager.descriptor.supportsSaveAnchor)
                return null;
            await ContentAnchor.EnterStore();
            try
            {
                var saved = await manager.TrySaveAnchorAsync(anchor);
                if (saved.status.IsSuccess())
                    return saved.value.guid.ToString();
                ImmersiveXLog.Warn($"Anchors: couldn't save one ({Describe(saved.status)}).");
                return null;
            }
            finally
            {
                ContentAnchor.LeaveStore();
            }
        }

        /// <summary>
        /// Load a saved anchor and wait (up to 5 s) for the device to find it in the room. Null when it isn't found: the
        /// room may be another one.
        /// </summary>
        public async Awaitable<ARAnchor> LoadAsync(string id)
        {
            var manager = Manager;
            if (manager == null || !manager.descriptor.supportsLoadAnchor || !Guid.TryParse(id, out var guid))
                return null;
            Result<ARAnchor> loaded;
            await ContentAnchor.EnterStore();
            try
            {
                loaded = await manager.TryLoadAnchorAsync(new SerializableGuid(guid));
            }
            finally
            {
                ContentAnchor.LeaveStore();
            }

            var anchor = loaded.status.IsSuccess() ? loaded.value : null;
            for (var waited = 0f; anchor != null && anchor.trackingState != TrackingState.Tracking && waited < LocaliseTimeoutSeconds;
                 waited += Time.unscaledDeltaTime)
                await Awaitable.NextFrameAsync();
            if (anchor != null && anchor.trackingState == TrackingState.Tracking)
                return anchor;
            if (anchor != null && manager != null && manager.enabled)
                manager.TryRemoveAnchor(anchor);
            return null;
        }

        /// <summary>Erase a saved anchor from the device. True when it's gone.</summary>
        public async Awaitable<bool> EraseAsync(string id)
        {
            var manager = Manager;
            if (manager == null || !manager.descriptor.supportsEraseAnchor || !Guid.TryParse(id, out var guid))
                return false;
            await ContentAnchor.EnterStore();
            try
            {
                var erased = await manager.TryEraseAnchorAsync(new SerializableGuid(guid));
                return erased.IsSuccess();
            }
            finally
            {
                ContentAnchor.LeaveStore();
            }
        }

        /// <summary>Stop tracking <paramref name="anchor"/> in this run (a saved copy stays saved).</summary>
        public void Remove(ARAnchor anchor)
        {
            var manager = Manager;
            if (anchor != null && manager != null)
                manager.TryRemoveAnchor(anchor);
        }

        static string Describe(XRResultStatus status) => $"{status.statusCode}, native code {status.nativeStatusCode}";
    }
}

using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace ImmersiveX.Platforms.MetaQuest
{
    /// <summary>Starts Quest's Space Setup ("scene capture") from inside the app. The app pauses until the user finishes.</summary>
    sealed class MetaQuestSpace : ISpaceProvider
    {
        public bool CanRequestSystemScan => true;

        public bool RequestSystemScan()
        {
            var session = Object.FindAnyObjectByType<ARSession>();
            if (session != null && session.subsystem is MetaOpenXRSessionSubsystem meta)
                return meta.TryRequestSceneCapture();

            ImmersiveXLog.Warn("Space Setup needs a running AR Session on Unity OpenXR: Meta.");
            return false;
        }
    }
}

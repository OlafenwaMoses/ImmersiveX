using System;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace ImmersiveX.Platforms.MetaQuest
{
    /// <summary>Requests <c>USE_SCENE</c>, which Quest requires before an app can read Space Setup data (walls, floor, furniture).</summary>
    sealed class MetaQuestPermissions : IPermissionProvider
    {
        const string UseScene = "com.oculus.permission.USE_SCENE";

        public void RequestRoomAccess(Action<bool> onResult)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(UseScene))
            {
                onResult(true);
                return;
            }

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => onResult(true);
            callbacks.PermissionDenied += _ => onResult(false);
            Permission.RequestUserPermission(UseScene, callbacks);
#else
            onResult(true);
#endif
        }
    }
}

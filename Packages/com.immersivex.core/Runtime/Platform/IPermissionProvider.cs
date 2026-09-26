using System;

namespace ImmersiveX
{
    /// <summary>Asks the operating system for access to room data (for example Quest's <c>USE_SCENE</c> permission).</summary>
    public interface IPermissionProvider
    {
        /// <summary>Request access; <paramref name="onResult"/> receives true if it was granted.</summary>
        void RequestRoomAccess(Action<bool> onResult);
    }
}

using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace ImmersiveX.Platforms.MetaQuest
{
    /// <summary>
    /// Shows Quest's own menu from the app. There's no API for the universal menu, so this asks the system shell for its dock
    /// (the bar the Meta button shows); when that's refused, it opens Home, where the dock is always shown.
    /// </summary>
    [Preserve]
    sealed class MetaQuestDeviceMenu : IDeviceMenuProvider
    {
        const int NewTask = 0x10000000; // Intent.FLAG_ACTIVITY_NEW_TASK

        public string Open()
        {
            if (Application.platform != RuntimePlatform.Android)
                return null;
            if (Start(intent =>
                {
                    intent.Call<AndroidJavaObject>("setAction", "com.oculus.vrshell.SHELL_MAIN").Dispose();
                    intent.Call<AndroidJavaObject>("setClassName", "com.oculus.systemux", "com.oculus.panelapp.activetaskbar.ActiveTaskBarActivity").Dispose();
                }, "the dock"))
                return "the dock";
            if (Start(intent =>
                {
                    intent.Call<AndroidJavaObject>("setAction", "android.intent.action.MAIN").Dispose();
                    intent.Call<AndroidJavaObject>("addCategory", "android.intent.category.HOME").Dispose();
                }, "Home"))
                return "Home";
            return null;
        }

        static bool Start(Action<AndroidJavaObject> configure, string what)
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = new AndroidJavaObject("android.content.Intent"))
                {
                    configure(intent);
                    intent.Call<AndroidJavaObject>("addFlags", NewTask).Dispose();
                    activity.Call("startActivity", intent);
                }

                ImmersiveXLog.Info($"Device menu: opened {what}.");
                return true;
            }
            catch (Exception exception)
            {
                ImmersiveXLog.Warn($"Device menu: couldn't open {what} ({exception.GetBaseException().Message}).");
                return false;
            }
        }
    }
}

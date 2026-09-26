using UnityEngine;

namespace ImmersiveX
{
    /// <summary>Console logging with a common "[ImmersiveX]" prefix, so ImmersiveX messages are easy to filter.</summary>
    public static class ImmersiveXLog
    {
        const string Prefix = "[ImmersiveX] ";

        public static void Info(string message) => Debug.Log(Prefix + message);
        public static void Warn(string message) => Debug.LogWarning(Prefix + message);
        public static void Error(string message) => Debug.LogError(Prefix + message);
    }
}

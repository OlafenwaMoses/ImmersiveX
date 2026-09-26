using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// Project-wide ImmersiveX defaults. Keep one asset named <c>ImmersiveXSettings</c> in a <c>Resources</c> folder
    /// (the project ships with <c>Assets/Settings/Resources/ImmersiveXSettings.asset</c>). Without one, these defaults apply.
    /// </summary>
    [CreateAssetMenu(menuName = "ImmersiveX/Settings", fileName = "ImmersiveXSettings")]
    public sealed class ImmersiveXSettings : ScriptableObject
    {
        const string ResourceName = "ImmersiveXSettings";

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of transparent content (transparent is the default look). Used from milestone M4.")]
        float _defaultOpacity = 0.5f;

        [SerializeField, Tooltip("Turn off joystick movement and turning on see-through devices, so content stays aligned with the real room.")]
        bool _disableLocomotionInSeeThrough = true;

        public float DefaultOpacity => _defaultOpacity;
        public bool DisableLocomotionInSeeThrough => _disableLocomotionInSeeThrough;

        public static ImmersiveXSettings Load()
        {
            var settings = Resources.Load<ImmersiveXSettings>(ResourceName);
            return settings != null ? settings : CreateInstance<ImmersiveXSettings>();
        }
    }
}

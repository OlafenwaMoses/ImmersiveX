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

        [SerializeField, Tooltip("Material for shapes ImmersiveX creates at runtime (markers, grab bars, outlines). Must use a URP shader.")]
        Material _runtimeMaterial;

        [Header("Room mapping")]
        [SerializeField, Range(0f, 1f), Tooltip("Keep this far from the walls, in metres, when working out where the user can walk.")]
        float _wallMargin = 0.3f;

        [SerializeField, Tooltip("How long to wait for room data the platform already has (Quest Space Setup), in seconds.")]
        float _roomDataWaitSeconds = 4f;

        [SerializeField, Tooltip("Start the room scan automatically after this many seconds of the briefing. 0 waits for the user.")]
        float _autoStartRoomScanSeconds = 5f;

        [SerializeField, Tooltip("Stop polling room data once the room is mapped. Saves battery and CPU; Rescan turns it back on.")]
        bool _pauseRoomTrackingWhenMapped = true;

        [SerializeField, Tooltip("Guided look-around: faster turning than this (degrees per second) is too fast to map.")]
        float _scanMaxTurnSpeed = 60f;

        [SerializeField, Tooltip("Guided look-around: remind the user to keep turning after this many seconds without turning.")]
        float _scanIdleSeconds = 3f;

        [SerializeField, Tooltip("How long the walkable area is drawn on the floor after mapping, in seconds.")]
        float _showWalkableAreaSeconds = 6f;

        public float DefaultOpacity => _defaultOpacity;
        public Material RuntimeMaterial => _runtimeMaterial;
        public bool DisableLocomotionInSeeThrough => _disableLocomotionInSeeThrough;
        public float WallMargin => _wallMargin;
        public float RoomDataWaitSeconds => _roomDataWaitSeconds;
        public float AutoStartRoomScanSeconds => _autoStartRoomScanSeconds;
        public bool PauseRoomTrackingWhenMapped => _pauseRoomTrackingWhenMapped;
        public float ScanMaxTurnSpeed => _scanMaxTurnSpeed;
        public float ScanIdleSeconds => _scanIdleSeconds;
        public float ShowWalkableAreaSeconds => _showWalkableAreaSeconds;

        public static ImmersiveXSettings Load()
        {
            var settings = Resources.Load<ImmersiveXSettings>(ResourceName);
            return settings != null ? settings : CreateInstance<ImmersiveXSettings>();
        }
    }
}

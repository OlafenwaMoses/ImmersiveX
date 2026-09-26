using UnityEngine;

namespace ImmersiveX
{
    public enum ScanCoaching
    {
        /// <summary>Turning at a good pace.</summary>
        Good,
        /// <summary>Turning too fast for the device to map the room.</summary>
        SlowDown,
        /// <summary>Not turning; keep looking around.</summary>
        KeepTurning,
    }

    /// <summary>
    /// Tracks which directions the user has looked in during a guided look-around (12 sectors of 30°),
    /// and coaches the pace: not too fast, not stalled. Pure logic, updated with the head's yaw each frame.
    /// </summary>
    public sealed class CoverageTracker
    {
        public const int Sectors = 12;
        const float SectorDegrees = 360f / Sectors;
        const float DwellSeconds = 0.25f; // a direction counts once looked at this long

        readonly bool[] _covered = new bool[Sectors];
        readonly float _maxDegreesPerSecond;
        readonly float _idleSeconds;
        float _dwell;
        int _dwellSector = -1;
        float? _lastYaw;
        float _speed;
        float _stillFor;

        public CoverageTracker(float maxDegreesPerSecond = 60f, float idleSeconds = 3f)
        {
            _maxDegreesPerSecond = maxDegreesPerSecond;
            _idleSeconds = idleSeconds;
        }

        public int CoveredCount { get; private set; }
        public float Coverage => CoveredCount / (float)Sectors;
        public bool IsCovered(int sector) => _covered[sector];
        public ScanCoaching Coaching { get; private set; } = ScanCoaching.KeepTurning;

        /// <summary>Feed the head's yaw (degrees, any range) and the frame time.</summary>
        public void Update(float yawDegrees, float deltaTime)
        {
            var yaw = Mathf.Repeat(yawDegrees, 360f);
            if (_lastYaw.HasValue && deltaTime > 0f)
            {
                var instant = Mathf.Abs(Mathf.DeltaAngle(_lastYaw.Value, yaw)) / deltaTime;
                _speed = Mathf.Lerp(_speed, instant, Mathf.Clamp01(deltaTime * 4f)); // smooth over ~¼ s
            }

            _lastYaw = yaw;
            _stillFor = _speed < 5f ? _stillFor + deltaTime : 0f;
            Coaching = _speed > _maxDegreesPerSecond ? ScanCoaching.SlowDown
                : _stillFor >= _idleSeconds || (CoveredCount == 0 && _speed < 5f) ? ScanCoaching.KeepTurning
                : ScanCoaching.Good;

            // Only count a direction when the user isn't whipping past it.
            var sector = Mathf.Min(Sectors - 1, Mathf.FloorToInt(yaw / SectorDegrees));
            if (sector != _dwellSector)
            {
                _dwellSector = sector;
                _dwell = 0f;
            }

            if (_speed > _maxDegreesPerSecond)
                return;
            _dwell += deltaTime;
            if (_dwell >= DwellSeconds && !_covered[sector])
            {
                _covered[sector] = true;
                CoveredCount++;
            }
        }
    }
}

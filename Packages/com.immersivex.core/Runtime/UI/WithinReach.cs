using System;
using System.Collections.Generic;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// Keeps a panel within reach of the user: low on their right, halfway up the arm between elbow and shoulder (about
    /// 1.2 m above the floor standing, lower sitting) and a forearm's length in front, tilted to face their eyes. It keeps still while they look around, look down at it or lean
    /// in to press it, and glides after them when they walk away, sit down or stand up, or turn to face somewhere else (see
    /// <see cref="Follower"/>). It also keeps still while <see cref="Hold"/> says so (a slider or handle in use). Several
    /// at once stack upwards in the order they appeared, unless one doesn't <see cref="Stacks"/>.
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-50)] // before anything placed relative to the panel (the move bar under it)
    public sealed class WithinReach : MonoBehaviour
    {
        /// <summary>Where the panel sits from the user's eyes, in metres: X to their right, Y up (below the eyes is negative), Z forward.</summary>
        public static readonly Vector3 DefaultOffset = new Vector3(0.24f, -0.4f, 0.38f);

        /// <summary>How far apart stacked panels are, in metres.</summary>
        public const float StackSpacing = 0.15f;

        /// <summary>How near the middle of the view the panel has to be to count as looked at, in degrees.</summary>
        public const float LookAngle = 24f;

        static readonly List<WithinReach> Active = new List<WithinReach>();

        readonly Follower _follower = new Follower();

        /// <summary>Where the panel sits from the user's eyes (see <see cref="DefaultOffset"/>).</summary>
        public Vector3 Offset = DefaultOffset;

        /// <summary>False for a panel with a place of its own (<see cref="MenuButton"/>): it doesn't stack, and the others stack as if it weren't there.</summary>
        public bool Stacks = true;

        /// <summary>While this returns true the panel keeps still.</summary>
        public Func<bool> Hold;

        /// <summary>Who it stays with: the headset camera unless set.</summary>
        public Func<Transform> Viewer;

        /// <summary>True while it's gliding after the user.</summary>
        public bool IsGliding => _follower.IsGliding;

        void OnEnable()
        {
            Active.Add(this);
            _follower.Reset(); // appearing: straight at the user's side, not gliding in from where it was
            LateUpdate();
        }

        void OnDisable() => Active.Remove(this);

        void LateUpdate()
        {
            var head = Viewer != null ? Viewer() : DefaultViewer();
            if (head == null)
                return;

            var toPanel = transform.position - head.position;
            var lookedAt = _follower.IsPlaced && toPanel.sqrMagnitude > 1e-4f && Vector3.Angle(head.forward, toPanel) < LookAngle;
            _follower.Step(head.position, head.forward, lookedAt, Hold != null && Hold(), Time.unscaledDeltaTime);
            var pose = _follower.Place(Offset + Vector3.up * (StackIndex() * StackSpacing));
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        /// <summary>How many stacking panels appeared before this one (0 when it doesn't stack).</summary>
        int StackIndex()
        {
            if (!Stacks)
                return 0;
            var index = 0;
            foreach (var other in Active)
            {
                if (other == this)
                    break;
                if (other.Stacks)
                    index++;
            }

            return index;
        }

        static Transform DefaultViewer()
        {
            var session = ImmersiveXSession.Instance;
            if (session != null && session.Origin != null && session.Origin.Camera != null)
                return session.Origin.Camera.transform;
            return Camera.main != null ? Camera.main.transform : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active.Clear();

        /// <summary>
        /// Where the user is for the panel: their eyes and the way they face, kept still until they walk further than
        /// <see cref="WalkDistance"/>, rise or sink more than <see cref="HeightChange"/>, or turn further than
        /// <see cref="TurnAngle"/> while not looking at the panel. Then it glides after them (about
        /// <see cref="GlideSeconds"/>) until it has caught up.
        /// </summary>
        public sealed class Follower
        {
            public const float WalkDistance = 0.35f;
            public const float HeightChange = 0.3f;
            public const float TurnAngle = 40f;
            public const float GlideSeconds = 0.3f;

            Vector3 _eyes;
            float _yaw;
            Vector3 _velocity;
            float _yawVelocity;

            public bool IsPlaced { get; private set; }

            public bool IsGliding { get; private set; }

            /// <summary>Where it has the user's eyes.</summary>
            public Vector3 Eyes => _eyes;

            /// <summary>Which way it has the user facing, in degrees about the up axis.</summary>
            public float Yaw => _yaw;

            /// <summary>Start again at the user's next pose, without a glide.</summary>
            public void Reset() => IsPlaced = false;

            public void Step(Vector3 eyes, Vector3 forward, bool lookedAt, bool hold, float deltaTime)
            {
                var yaw = YawOf(forward, IsPlaced ? _yaw : 0f);
                if (!IsPlaced)
                {
                    _eyes = eyes;
                    _yaw = yaw;
                    IsPlaced = true;
                    Stop();
                    return;
                }

                if (hold)
                {
                    Stop();
                    return;
                }

                if (!IsGliding)
                {
                    var moved = eyes - _eyes;
                    var walked = new Vector2(moved.x, moved.z).magnitude;
                    var turned = Mathf.Abs(Mathf.DeltaAngle(_yaw, yaw));
                    IsGliding = walked > WalkDistance || Mathf.Abs(moved.y) > HeightChange || (turned > TurnAngle && !lookedAt);
                    if (!IsGliding)
                        return;
                }

                var step = Mathf.Max(deltaTime, 1e-4f);
                _eyes = Vector3.SmoothDamp(_eyes, eyes, ref _velocity, GlideSeconds, Mathf.Infinity, step);
                _yaw = Mathf.SmoothDampAngle(_yaw, yaw, ref _yawVelocity, GlideSeconds, Mathf.Infinity, step);
                if ((eyes - _eyes).sqrMagnitude < 0.02f * 0.02f && Mathf.Abs(Mathf.DeltaAngle(_yaw, yaw)) < 2f)
                    Stop(); // caught up
            }

            /// <summary>The panel's pose at <paramref name="offset"/> from the eyes (see <see cref="DefaultOffset"/>), facing them.</summary>
            public Pose Place(Vector3 offset)
            {
                var position = _eyes + Quaternion.Euler(0f, _yaw, 0f) * offset;
                var facing = position - _eyes;
                return new Pose(position, facing.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(facing, Vector3.up) : Quaternion.identity);
            }

            void Stop()
            {
                IsGliding = false;
                _velocity = Vector3.zero;
                _yawVelocity = 0f;
            }

            /// <summary>The heading of <paramref name="forward"/>; <paramref name="fallback"/> when it points straight up or down.</summary>
            static float YawOf(Vector3 forward, float fallback)
            {
                var flat = new Vector2(forward.x, forward.z);
                return flat.sqrMagnitude < 1e-4f ? fallback : Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg;
            }
        }
    }
}

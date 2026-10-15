using System.Collections.Generic;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// Makes an object clickable with the ray from a hand or controller. Its colliders, and its children's (unless a child
    /// has its own target), take ray clicks, which arrive on this object as the usual pointer events
    /// (<c>IPointerClickHandler</c>, <c>IPointerEnterHandler</c>, …). See <see cref="ContentClickRaycaster"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ImmersiveX/Interaction/Content Click Target")]
    public sealed class ContentClickTarget : MonoBehaviour
    {
        static readonly Dictionary<Collider, ContentClickTarget> ByCollider = new Dictionary<Collider, ContentClickTarget>();
        static readonly List<Collider> Found = new List<Collider>();

        readonly List<Collider> _colliders = new List<Collider>();

        /// <summary>How many colliders take clicks now (the raycaster skips its work when there are none).</summary>
        public static int Count => ByCollider.Count;

        /// <summary>The target that owns <paramref name="collider"/>, or null when it doesn't take clicks.</summary>
        public static ContentClickTarget For(Collider collider) =>
            collider != null && ByCollider.TryGetValue(collider, out var target) && target != null && target.isActiveAndEnabled ? target : null;

        /// <summary>Register the object's colliders again. Call it after adding or removing colliders.</summary>
        public void Refresh()
        {
            Unregister();
            if (!isActiveAndEnabled)
                return;
            GetComponentsInChildren(true, Found);
            foreach (var collider in Found)
            {
                if (collider.GetComponentInParent<ContentClickTarget>(true) != this)
                    continue; // a nested target owns it
                ByCollider[collider] = this;
                _colliders.Add(collider);
            }

            Found.Clear();
        }

        void OnEnable() => Refresh();

        void OnDisable() => Unregister();

        void Unregister()
        {
            foreach (var collider in _colliders)
                if (collider != null && ByCollider.TryGetValue(collider, out var owner) && owner == this)
                    ByCollider.Remove(collider);
            _colliders.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ByCollider.Clear();
    }
}

using UnityEngine;
using UnityEngine.EventSystems;

namespace ImmersiveX
{
    /// <summary>
    /// One selectable, movable piece of a piece of content (see <see cref="ContentParts"/>): a ray click selects it, a direct
    /// grab picks it up. Where the user puts it is kept as an offset on top of where the content puts it (its animation, or
    /// the web page driving it), so it keeps animating in its new place.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ContentPart : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>The name shown when it's selected.</summary>
        public string Title;

        /// <summary>One line about it, shown under the name (optional).</summary>
        public string Info;

        /// <summary>The id the content knows it by (a web page's actor id), or empty.</summary>
        public string Id;

        /// <summary>The assembly it belongs to, or null for a top-level part.</summary>
        public ContentPart Parent { get; internal set; }

        public ContentParts Owner { get; internal set; }

        /// <summary>The content's root object itself: moving it moves the whole thing (no offset, no putting back).</summary>
        public bool IsRoot { get; internal set; }

        /// <summary>True while a hand holds it.</summary>
        public bool IsHeld { get; internal set; }

        // Where the content puts it (local), captured every frame after the content has moved it.
        internal Vector3 HomePosition;
        internal Quaternion HomeRotation = Quaternion.identity;
        internal bool HasHome;

        // The user's move, in the parent's space: local = offset * home.
        internal Vector3 OffsetPosition;
        internal Quaternion OffsetRotation = Quaternion.identity;
        internal bool Gliding;

        /// <summary>True when the user has moved it away from where the content puts it.</summary>
        public bool IsMoved => !IsRoot && (OffsetPosition.sqrMagnitude > 1e-8f || Quaternion.Angle(OffsetRotation, Quaternion.identity) > 0.05f);

        /// <summary>Its name with its assemblies before it: "Body › Wheels › Front wheel" (the root object isn't named).</summary>
        public string Trail => Parent != null && !Parent.IsRoot ? Parent.Trail + " › " + Title : Title;

        /// <summary>True when <paramref name="other"/> is this part or one of its sub-parts.</summary>
        public bool Contains(ContentPart other)
        {
            for (var part = other; part != null; part = part.Parent)
                if (part == this)
                    return true;
            return false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Owner != null)
                Owner.Click(this);
        }
    }
}

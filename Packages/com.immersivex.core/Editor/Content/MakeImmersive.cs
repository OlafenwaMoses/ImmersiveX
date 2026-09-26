using UnityEditor;
using UnityEngine;

namespace ImmersiveX.Editor
{
    /// <summary>Right-click an object ▸ ImmersiveX ▸ Make Immersive. Adds <see cref="ImmersiveContent"/> and a fitted collider.</summary>
    public static class MakeImmersive
    {
        const string MenuPath = "GameObject/ImmersiveX/Make Immersive";

        [MenuItem(MenuPath, false, 10)]
        static void MakeSelectionImmersive(MenuCommand command)
        {
            var target = command.context as GameObject ?? Selection.activeGameObject;
            if (target != null)
                Apply(target);
        }

        [MenuItem(MenuPath, true)]
        static bool CanMakeSelectionImmersive() => Selection.activeGameObject != null;

        /// <summary>Make <paramref name="target"/> immersive: grabbable by hands and controllers, floating, placed for the user.</summary>
        public static ImmersiveContent Apply(GameObject target)
        {
            Undo.SetCurrentGroupName("Make Immersive");
            if (target.GetComponentInChildren<Collider>() == null)
            {
                Undo.AddComponent<BoxCollider>(target);
                ColliderFitting.FitBox(target);
            }

            var content = target.GetComponent<ImmersiveContent>();
            if (content == null)
                content = Undo.AddComponent<ImmersiveContent>(target); // also adds XRGrabInteractable and Rigidbody

            var body = target.GetComponent<Rigidbody>();
            Undo.RecordObject(body, "Make Immersive");
            body.useGravity = false;
            body.isKinematic = true;
            return content;
        }
    }
}

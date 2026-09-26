using System;
using UnityEngine;
using UnityEngine.UI;

namespace ImmersiveX
{
    /// <summary>
    /// A small world-space message panel with up to two buttons and an optional 12-step progress row.
    /// ImmersiveX uses it for the room-scan briefing, look-around coaching and "room saved" notes.
    /// It appears in front of the user and can be moved with its grab bar.
    /// </summary>
    public sealed class PromptPanel : MonoBehaviour
    {
        const float Width = 560f;
        const float Height = 330f;

        Text _title;
        Text _body;
        Button _primary;
        Button _secondary;
        Image[] _progress;
        Action _onPrimary;
        Action _onSecondary;

        /// <summary>The panel currently on screen, if any (used by editor automation to press its buttons).</summary>
        public static PromptPanel Active { get; private set; }

        public static PromptPanel Create()
        {
            var panel = new GameObject("ImmersiveX Prompt").AddComponent<PromptPanel>();
            panel.Build();
            panel.gameObject.SetActive(false);
            return panel;
        }

        /// <summary>Show a message. Buttons are hidden when their label is null.</summary>
        public void Show(string title, string body, string primary = null, Action onPrimary = null, string secondary = null, Action onSecondary = null)
        {
            _title.text = title;
            _body.text = body;
            _onPrimary = onPrimary;
            _onSecondary = onSecondary;
            Configure(_primary, primary);
            Configure(_secondary, secondary);
            ShowProgress(false);
            gameObject.SetActive(true);
            Active = this;
        }

        public void SetBody(string body) => _body.text = body;

        public void SetPrimaryInteractable(bool interactable) => _primary.interactable = interactable;

        /// <summary>Show the 12-step progress row with the covered directions lit.</summary>
        public void SetProgress(CoverageTracker tracker)
        {
            ShowProgress(true);
            for (var i = 0; i < _progress.Length; i++)
                _progress[i].color = tracker.IsCovered(i) ? WorldUi.OnColor : WorldUi.OffColor;
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            if (Active == this)
                Active = null;
        }

        /// <summary>Place the panel in front of <paramref name="head"/>, slightly below eye height, facing the user.</summary>
        public void PlaceInFrontOf(Transform head, float distance = 0.9f)
        {
            var user = UserRelativePlacement.UserPose(head);
            var pose = UserRelativePlacement.Apply(user, new Vector3(0f, -0.12f, distance));
            transform.SetPositionAndRotation(pose.position, user.rotation);
        }

        public void PressPrimary()
        {
            if (_primary.gameObject.activeSelf && _primary.interactable)
                _onPrimary?.Invoke();
        }

        public void PressSecondary()
        {
            if (_secondary.gameObject.activeSelf)
                _onSecondary?.Invoke();
        }

        void Build()
        {
            var root = WorldUi.CreateCanvas(transform, new Vector2(Width, Height));
            _title = WorldUi.Label(root, "Title", string.Empty, 30, new Vector2(0f, 120f), new Vector2(Width - 50f, 44f), FontStyle.Bold);
            _body = WorldUi.Label(root, "Body", string.Empty, 22, new Vector2(0f, 20f), new Vector2(Width - 50f, 150f));

            _progress = new Image[CoverageTracker.Sectors];
            var cell = (Width - 50f) / _progress.Length;
            for (var i = 0; i < _progress.Length; i++)
            {
                var x = -(Width - 50f) / 2f + cell * (i + 0.5f);
                _progress[i] = WorldUi.Rect(root, "Step " + i, new Vector2(x, -70f), new Vector2(cell - 6f, 14f), WorldUi.OffColor);
            }

            _primary = WorldUi.Button(root, "OK", new Vector2(-130f, -125f), new Vector2(240f, 56f), PressPrimary);
            _secondary = WorldUi.Button(root, "Cancel", new Vector2(130f, -125f), new Vector2(240f, 56f), PressSecondary);
            PanelGrabHandle.Attach(gameObject, new Vector2(Width, Height) * 0.001f);
        }

        void ShowProgress(bool visible)
        {
            foreach (var step in _progress)
                step.gameObject.SetActive(visible);
        }

        static void Configure(Button button, string label)
        {
            button.gameObject.SetActive(label != null);
            button.interactable = true;
            if (label != null)
                WorldUi.SetLabel(button, label);
        }

        void OnDestroy()
        {
            if (Active == this)
                Active = null;
        }
    }
}

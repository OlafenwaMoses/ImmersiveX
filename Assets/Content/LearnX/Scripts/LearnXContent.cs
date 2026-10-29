using System.Linq;
using ImmersiveX;
using UnityEngine;

namespace LearnX
{
    /// <summary>
    /// LearnX, a place to learn, as an app in ImmersiveX's content menu (<see cref="ContentApp"/>). Picked in the menu,
    /// the menu turns to LearnX's page (<see cref="LearnPages"/>): the classes, Nursery and Years 1 to 12, each with its
    /// own lessons (<see cref="Catalogue"/>), written to GOV.UK's style and the national curriculum in England
    /// (<see cref="ContentStyle"/>). <b>Content</b> on the classes goes back to the menu's list.
    /// <para>A lesson's immersive media plays in the room as ImmersiveX Immersive Media under this object
    /// (<see cref="MediaStage"/>), so it's placed, anchored, kept in the mapped room and given its controls like any other
    /// media, and it hides and pauses with LearnX when the menu shows other content.</para>
    /// </summary>
    [AddComponentMenu("ImmersiveX/Content/LearnX")]
    public sealed class LearnXContent : ContentApp
    {
        public const string DefaultTitle = "LearnX";
        public const string DefaultKind = "Lessons · Nursery to Year 12";

        public LearnPages Pages { get; private set; }
        public MediaStage Stage { get; private set; }

        void Reset()
        {
            Title = DefaultTitle;
            Kind = DefaultKind;
        }

        void Awake()
        {
            var classes = Catalogue.Classes;
            var media = new GameObject("LearnX lesson media");
            media.transform.SetParent(transform, false);
            Stage = media.AddComponent<MediaStage>();
            Pages = new LearnPages(classes, Stage);
            Log($"Ready: {classes.Count} classes and {classes.Sum(c => c.lessons.Length)} lessons.");
        }

        protected override void OnPageOpened(ContentAppPage page) => Pages.Attach(page);

        protected override void OnShown(bool shown)
        {
            if (!shown)
                Pages.StopWatching(); // its media is hidden and paused with it: the menu shouldn't wait for its end
        }

        void Update() => Pages?.CheckMediaFinished();

        public static void Log(string message) => Debug.Log("[LearnX] " + message);
    }
}

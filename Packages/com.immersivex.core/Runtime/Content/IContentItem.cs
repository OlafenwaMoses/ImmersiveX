namespace ImmersiveX
{
    /// <summary>
    /// A piece of content the content menu lists and switches between (see <see cref="ContentMenu"/>): Immersive Media and
    /// Immersive Content are both one.
    /// </summary>
    public interface IContentItem
    {
        /// <summary>A stable id, so the app remembers which content was showing.</summary>
        string Key { get; }

        /// <summary>The name shown in the menu.</summary>
        string Title { get; }

        /// <summary>What kind of content it is, in a few words ("3.5D hologram", "Model", "360° video").</summary>
        string Kind { get; }

        /// <summary>False while the menu hides it.</summary>
        bool IsShown { get; }

        /// <summary>
        /// Show or hide it, with its controls and handles. Hidden content can't be grabbed or clicked, and hidden media
        /// pauses. Showing it again leaves it paused until Play.
        /// </summary>
        void SetShown(bool shown);
    }
}

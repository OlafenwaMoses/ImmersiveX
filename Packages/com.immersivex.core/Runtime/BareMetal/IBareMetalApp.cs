namespace ImmersiveX
{
    /// <summary>
    /// A bare metal app's entry point: implement it on a component under the <see cref="BareMetalContent"/> object.
    /// </summary>
    public interface IBareMetalApp
    {
        /// <summary>
        /// The host is ready: the see-through view is on, permissions were asked, hands and controllers are set up, and
        /// <see cref="ImmersiveHost"/> can be used. Called once.
        /// </summary>
        void OnHostReady();
    }
}

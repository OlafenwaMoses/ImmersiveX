namespace ImmersiveX
{
    /// <summary>The steps of the start-up flow, in order.</summary>
    public enum SessionState
    {
        Boot,
        ResolvePlatform,
        Permissions,
        SeeThrough,
        Space,
        Content,
        Ready,
        Failed,
    }
}

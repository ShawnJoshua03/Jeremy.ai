namespace Jeremy.Crew
{
    public enum Provider
    {
        Claude,
        Cartesia,
        Apify,
        Convex
    }

    public enum ProviderState
    {
        Unknown,
        Used,
        Fallback,
        Off,
        Live,
        Snapshot,
        Mirrored,
        Unavailable
    }
}

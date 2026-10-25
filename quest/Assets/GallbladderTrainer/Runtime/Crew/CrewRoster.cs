namespace GallbladderTrainer.Crew
{
    public enum Provider
    {
        Claude,
        Cartesia,
        Apify,
        X402,
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
        Paid,
        Unpaid,
        Mirrored,
        Unavailable
    }
}

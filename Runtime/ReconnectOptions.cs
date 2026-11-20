namespace MagicWS
{
    /// <summary>
    /// Backoff configuration for automatic reconnection.
    /// </summary>
    public class ReconnectOptions
    {
        public int MaxAttempts = 5;
        public float InitialDelaySeconds = 0.5f;
        public float MaxDelaySeconds = 10f;
        public float BackoffMultiplier = 2f;
        public bool Forever = false;
    }
}
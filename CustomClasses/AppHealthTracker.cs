namespace SignalRMVC.CustomClasses
{
    public static class AppHealthTracker
    {
        private static int _activeConnections = 0;

        public static DateTime LastActivityTime { get; private set; } = DateTime.UtcNow;
        public static int ActiveConnections => _activeConnections;

        public static void UpdateActivity() => LastActivityTime = DateTime.UtcNow;

        // Called from BasicChatHub.OnConnectedAsync
        public static void TrackConnect() => Interlocked.Increment(ref _activeConnections);

        // Called from BasicChatHub.OnDisconnectedAsync
        public static void TrackDisconnect() => Interlocked.Decrement(ref _activeConnections);
    }
}

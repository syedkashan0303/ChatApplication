namespace SignalRMVC.CustomClasses
{
    // AppHealthTracker.cs
    public static class AppHealthTracker
    {
        public static DateTime LastActivityTime { get; private set; } = DateTime.UtcNow;

        public static void UpdateActivity()
        {
            LastActivityTime = DateTime.UtcNow;
        }
    }

}

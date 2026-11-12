namespace tcpTrigger
{
    // An enabled notification from the configuration. Runs for each detection.
    internal abstract class Notification
    {
        public string Name { get; set; }
    }
}
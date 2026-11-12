namespace tcpTrigger
{
    // Runs a command (program or script) for each detection.
    internal class NotificationCommand : Notification
    {
        public NotificationCommand()
        {
            Name = "Run command";
        }

        public string Path { get; set; }

        // Variables in the arguments are replaced with the detection details.
        public string Arguments { get; set; }
    }
}
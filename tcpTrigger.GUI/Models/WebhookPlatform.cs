namespace tcpTrigger.GUI.Models
{
    // The platform for a webhook notification. Custom is for any other service.
    public enum WebhookPlatform
    {
        MicrosoftTeams,
        Slack,
        Discord,
        GoogleChat,
        Ntfy,
        Custom
    }
}

namespace tcpTrigger
{
    // The platform for a webhook notification. Custom is for any other service.
    internal enum WebhookPlatform
    {
        MicrosoftTeams,
        Slack,
        Discord,
        GoogleChat,
        Ntfy,
        Custom
    }
}

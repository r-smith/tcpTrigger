namespace tcpTrigger.GUI.Configuration
{
    // XML node paths for the tcpTrigger configuration file.
    // The legacy format section uses the same names as ConfigNames in the service.
    internal static class ConfigNames
    {
        // ----- Current format -----

        // Values of the `type` attribute on <notification> elements.
        public const string NotificationTypeEmail = "email";
        public const string NotificationTypeLogFile = "logFile";
        public const string NotificationTypeCommand = "command";

        public const string DetectionIcmp = "/tcpTrigger/detection/icmp";
        public const string DetectionTcp = "/tcpTrigger/detection/tcp";
        public const string DetectionUdp = "/tcpTrigger/detection/udp";
        public const string Notifications = "/tcpTrigger/notifications/notification";
        public const string IgnoreRules = "/tcpTrigger/ignoreRules/rule";

        // ----- Legacy format -----
        // NetworkInterfaceExcludeListDeviceGuid is also used by the current format.

        public const string EnabledComponentsTcp = "/tcpTrigger/enabledComponents/tcp";
        public const string EnabledComponentsUdp = "/tcpTrigger/enabledComponents/udp";
        public const string EnabledComponentsIcmp = "/tcpTrigger/enabledComponents/icmp";
        public const string MonitoredPortsTcpInclude = "/tcpTrigger/monitoredPorts/tcp/include";
        public const string MonitoredPortsTcpExclude = "/tcpTrigger/monitoredPorts/tcp/exclude";
        public const string MonitoredPortsUdpInclude = "/tcpTrigger/monitoredPorts/udp/include";
        public const string MonitoredPortsUdpExclude = "/tcpTrigger/monitoredPorts/udp/exclude";
        public const string EndpointIgnoreListIpAddress = "/tcpTrigger/endpointIgnoreList/ipAddress";
        public const string NetworkInterfaceExcludeListDeviceGuid = "/tcpTrigger/networkInterfaceExcludeList/deviceGuid";
        public const string EnabledActionsLog = "/tcpTrigger/enabledActions/log";
        public const string EnabledActionsEmailNotification = "/tcpTrigger/enabledActions/emailNotification";
        public const string EnabledActionsExecuteCommand = "/tcpTrigger/enabledActions/executeCommand";
        public const string ActionSettingsLogPath = "/tcpTrigger/actionSettings/logPath";
        public const string ActionSettingsCommandPath = "/tcpTrigger/actionSettings/command/path";
        public const string ActionSettingsCommandArguments = "/tcpTrigger/actionSettings/command/arguments";
        public const string EmailServer = "/tcpTrigger/email/server";
        public const string EmailPort = "/tcpTrigger/email/port";
        public const string EmailIsTlsEnabled = "/tcpTrigger/email/isTlsEnabled";
        public const string EmailUsername = "/tcpTrigger/email/username";
        public const string EmailPassword = "/tcpTrigger/email/password";
        public const string EmailRecipientListAddress = "/tcpTrigger/email/recipientList/address";
        public const string EmailSenderAddress = "/tcpTrigger/email/sender/address";
        public const string EmailSenderDisplayName = "/tcpTrigger/email/sender/displayName";
        public const string EmailMessageSubject = "/tcpTrigger/email/message/subject";
        public const string EmailMessageBody = "/tcpTrigger/email/message/body";
        public const string EmailOptionsRateLimitSeconds = "/tcpTrigger/email/options/rateLimitSeconds";
        public const string EmailOptionsBufferSeconds = "/tcpTrigger/email/options/bufferSeconds";
    }
}
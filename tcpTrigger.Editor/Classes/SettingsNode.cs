namespace tcpTrigger.Editor
{
    class SettingsNode
    {
        // XML node paths for the tcpTrigger configuration file.
        public const string enabledComponents_tcp = "/tcpTrigger/enabledComponents/tcp";
        public const string enabledComponents_udp = "/tcpTrigger/enabledComponents/udp";
        public const string enabledComponents_icmp = "/tcpTrigger/enabledComponents/icmp";
        public const string enabledComponents_rogueDhcp = "/tcpTrigger/enabledComponents/rogueDhcp";
        public const string monitoredPorts_tcp_include = "/tcpTrigger/monitoredPorts/tcp/include";
        public const string monitoredPorts_tcp_exclude = "/tcpTrigger/monitoredPorts/tcp/exclude";
        public const string monitoredPorts_udp_include = "/tcpTrigger/monitoredPorts/udp/include";
        public const string monitoredPorts_udp_exclude = "/tcpTrigger/monitoredPorts/udp/exclude";
        public const string dhcpServerIgnoreList_ipAddress = "/tcpTrigger/dhcpServerIgnoreList/ipAddress";
        public const string endpointIgnoreList_ipAddress = "/tcpTrigger/endpointIgnoreList/ipAddress";
        public const string networkInterfaceExcludeList_deviceGuid = "/tcpTrigger/networkInterfaceExcludeList/deviceGuid";
        public const string enabledActions_log = "/tcpTrigger/enabledActions/log";
        public const string enabledActions_windowsEventLog = "/tcpTrigger/enabledActions/windowsEventLog";
        public const string enabledActions_emailNotification = "/tcpTrigger/enabledActions/emailNotification";
        public const string enabledActions_popupNotification = "/tcpTrigger/enabledActions/popupNotification";
        public const string enabledActions_executeCommand = "/tcpTrigger/enabledActions/executeCommand";
        public const string actionsSettings_logPath = "/tcpTrigger/actionSettings/logPath";
        public const string actionsSettings_command_path = "/tcpTrigger/actionSettings/command/path";
        public const string actionsSettings_command_arguments = "/tcpTrigger/actionSettings/command/arguments";
        public const string email_server = "/tcpTrigger/email/server";
        public const string email_port = "/tcpTrigger/email/port";
        public const string email_isAuthRequired = "/tcpTrigger/email/isAuthRequired";
        public const string email_username = "/tcpTrigger/email/username";
        public const string email_password = "/tcpTrigger/email/password";
        public const string email_recipientList_address = "/tcpTrigger/email/recipientList/address";
        public const string email_sender_address = "/tcpTrigger/email/sender/address";
        public const string email_sender_displayName = "/tcpTrigger/email/sender/displayName";
        public const string email_message_subject = "/tcpTrigger/email/message/subject";
        public const string email_message_body = "/tcpTrigger/email/message/body";
        public const string email_options_rateLimitSeconds = "/tcpTrigger/email/options/rateLimitSeconds";
    }
}

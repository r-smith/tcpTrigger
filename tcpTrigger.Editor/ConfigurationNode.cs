namespace tcpTrigger.Editor
{
    class ConfigurationNode
    {
        // XML node paths for the tcpTrigger configuration file.
        public const string enabledComponents_tcp = "/tcpTrigger/enabledComponents/tcp";
        public const string enabledComponents_icmp = "/tcpTrigger/enabledComponents/icmp";
        public const string enabledComponents_namePoison = "/tcpTrigger/enabledComponents/namePoison";
        public const string enabledComponents_rogueDhcp = "/tcpTrigger/enabledComponents/rogueDhcp";
        public const string monitoredPorts_tcp_include = "/tcpTrigger/monitoredPorts/tcp/include";
        public const string monitoredPorts_tcp_exclude = "/tcpTrigger/monitoredPorts/tcp/exclude";
        public const string dhcpServerIgnoreList_ipAddress = "/tcpTrigger/dhcpServerIgnoreList/ipAddress";
        public const string endpointIgnoreList_ipAddress = "/tcpTrigger/endpointIgnoreList/ipAddress";
        public const string networkInterfaceExcludeList_deviceGuid = "/tcpTrigger/networkInterfaceExcludeList/deviceGuid";
        public const string enabledActions_windowsEventLog = "/tcpTrigger/enabledActions/windowsEventLog";
        public const string enabledActions_emailNotification = "/tcpTrigger/enabledActions/emailNotification";
        public const string enabledActions_popupNotification = "/tcpTrigger/enabledActions/popupNotification";
        public const string enabledActions_executeCommand = "/tcpTrigger/enabledActions/executeCommand";
        public const string actionsSettings_rateLimitMinutes = "/tcpTrigger/actionSettings/rateLimitMinutes";
        public const string actionsSettings_command_path = "/tcpTrigger/actionSettings/command/path";
        public const string actionsSettings_command_arguments = "/tcpTrigger/actionSettings/command/arguments";
        public const string emailConfiguration_server = "/tcpTrigger/emailConfiguration/server";
        public const string emailConfiguration_port = "/tcpTrigger/emailConfiguration/port";
        public const string emailConfiguration_isAuthRequired = "/tcpTrigger/emailConfiguration/isAuthRequired";
        public const string emailConfiguration_username = "/tcpTrigger/emailConfiguration/username";
        public const string emailConfiguration_password = "/tcpTrigger/emailConfiguration/password";
        public const string emailConfiguration_recipientList_address = "/tcpTrigger/emailConfiguration/recipientList/address";
        public const string emailConfiguration_sender_address = "/tcpTrigger/emailConfiguration/sender/address";
        public const string emailConfiguration_sender_displayName = "/tcpTrigger/emailConfiguration/sender/displayName";
        public const string customMessage = "/tcpTrigger/customMessage";
    }
}

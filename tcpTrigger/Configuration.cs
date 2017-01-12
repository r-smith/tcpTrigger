using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net;

namespace tcpTrigger
{
    class Configuration
    {
        private bool _enableMonitorTcpPort;
        public bool EnableMonitorTcpPort { get { return _enableMonitorTcpPort; } }

        private bool _enableMonitorIcmpPing;
        public bool EnableMonitorIcmpPing { get { return _enableMonitorIcmpPing; } }

        private bool _enableNamePoisonDetection;
        public bool EnableNamePoisonDetection { get { return _enableNamePoisonDetection; } }

        private bool _enableRogueDhcpDetection;
        public bool EnableRogueDhcpDetection { get { return _enableRogueDhcpDetection; } }

        private bool _doNotMonitorVMwareVirtualHostAdapters;
        public bool DoNotMonitorVMwareVirtualHostAdapters { get { return _doNotMonitorVMwareVirtualHostAdapters; } }

        private int[] _tcpPortsToListenOn;
        public int[] TcpPortsToListenOn { get { return _tcpPortsToListenOn; } }

        private string _tcpPortsToListenOnAsString;
        public string TcpPortsToListenOnAsString { get { return _tcpPortsToListenOnAsString; } }

        private bool _enableEventLogAction;
        public bool EnableEventLogAction { get { return _enableEventLogAction; } }

        private bool _enableEmailNotificationAction;
        public bool EnableEmailNotificationAction { get { return _enableEmailNotificationAction; } }

        private bool _enableRunApplicationAction;
        public bool EnableRunApplicationAction { get { return _enableRunApplicationAction; } }

        private bool _enablePopupMessageAction;
        public bool EnablePopupMessageAction { get { return _enablePopupMessageAction; } }

        private int _actionRateLimitMinutes;
        public int ActionRateLimitMinutes { get { return _actionRateLimitMinutes; } }

        private string _triggeredApplicationPath;
        public string TriggeredApplicationPath { get { return _triggeredApplicationPath; } }

        private string _triggeredApplicationArguments;
        public string TriggeredApplicationArguments { get { return _triggeredApplicationArguments; } }

        private List<IPAddress> _dhcpSafeServerList;
        public List<IPAddress> DhcpSafeServerList { get { return _dhcpSafeServerList; } }

        private string _emailServer;
        public string EmailServer { get { return _emailServer; } }

        private int _emailServerPort;
        public int EmailServerPort { get { return _emailServerPort; } }

        private string _emailRecipientAddress;
        public string EmailRecipientAddress { get { return _emailRecipientAddress; } }

        private string _emailSenderAddress;
        public string EmailSenderAddress { get { return _emailSenderAddress; } }

        private string _emailSenderDisplayName;
        public string EmailSenderDisplayName { get { return _emailSenderDisplayName; } }

        private string _emailSubject;
        public string EmailSubject { get { return _emailSubject; } }

        private string _messageBodyPing;
        public string MessageBodyPing { get { return _messageBodyPing; } }

        private string _messageBodyTcpConnect;
        public string MessageBodyTcpConnect { get { return _messageBodyTcpConnect; } }

        private string _messageBodyNamePoison;
        public string MessageBodyNamePoison { get { return _messageBodyNamePoison; } }

        private string _messageBodyRogueDhcp;
        public string MessageBodyRogueDhcp { get { return _messageBodyRogueDhcp; } }

        public void Load()
        {
            try
            {
                _dhcpSafeServerList = new List<IPAddress>();

                bool.TryParse(ConfigurationManager.AppSettings["Trigger.EnableMonitorTcpPort"], out _enableMonitorTcpPort);
                if (_enableMonitorTcpPort)
                {
                    _tcpPortsToListenOnAsString = ConfigurationManager.AppSettings["Trigger.TcpPortsToListenOn"];
                    
                    _tcpPortsToListenOn = (from part in _tcpPortsToListenOnAsString.Split(',')
                                           let range = part.Split('-')
                                           let start = int.Parse(range[0])
                                           let end = int.Parse(range[range.Length - 1])
                                           from i in Enumerable.Range(start, end - start + 1)
                                           orderby i
                                           select i).Distinct().ToArray();
                }
                bool.TryParse(ConfigurationManager.AppSettings["Trigger.EnableMonitorIcmpPing"], out _enableMonitorIcmpPing);
                bool.TryParse(ConfigurationManager.AppSettings["Trigger.EnableNamePoisonDetection"], out _enableNamePoisonDetection);
                bool.TryParse(ConfigurationManager.AppSettings["Trigger.EnableRogueDhcpDetection"], out _enableRogueDhcpDetection);
                if (_enableRogueDhcpDetection)
                {
                    var dhcpSafeServerListAsString = ConfigurationManager.AppSettings["Dhcp.SafeServerList"];
                    if (dhcpSafeServerListAsString.Length > 0)
                    {
                        var dhcpSafeServerListAsArray = dhcpSafeServerListAsString.Split(',').
                            Select(x => IPAddress.Parse(x)).ToArray();
                        for (int i = 0; i < dhcpSafeServerListAsArray.Length; ++i)
                        {
                            _dhcpSafeServerList.Add(dhcpSafeServerListAsArray[i]);
                        }
                    }
                }
                bool.TryParse(ConfigurationManager.AppSettings["DoNotMonitorVMwareVirtualHostAdapters"], out _doNotMonitorVMwareVirtualHostAdapters);
                bool.TryParse(ConfigurationManager.AppSettings["Action.EnableEventLog"], out _enableEventLogAction);
                bool.TryParse(ConfigurationManager.AppSettings["Action.EnableEmailNotification"], out _enableEmailNotificationAction);
                bool.TryParse(ConfigurationManager.AppSettings["Action.EnableRunApplication"], out _enableRunApplicationAction);
                bool.TryParse(ConfigurationManager.AppSettings["Action.EnablePopupMessage"], out _enablePopupMessageAction);
                if (!(int.TryParse(ConfigurationManager.AppSettings["Action.RateLimitMinutes"], out _actionRateLimitMinutes)))
                {
                    _actionRateLimitMinutes = 0;
                }
                _triggeredApplicationPath = ConfigurationManager.AppSettings["Action.ApplicationPath"];
                _triggeredApplicationArguments = ConfigurationManager.AppSettings["Action.ApplicationArguments"];
                _emailServer = ConfigurationManager.AppSettings["Email.Server"];
                if (!(int.TryParse(ConfigurationManager.AppSettings["Email.ServerPort"], out _emailServerPort)))
                {
                    _emailServerPort = 25;
                }
                _emailRecipientAddress = ConfigurationManager.AppSettings["Email.RecipientAddress"];
                _emailSenderAddress = ConfigurationManager.AppSettings["Email.SenderAddress"];
                _emailSenderDisplayName = ConfigurationManager.AppSettings["Email.SenderDisplayName"];
                _emailSubject = ConfigurationManager.AppSettings["Email.Subject"];

                _messageBodyPing = ConfigurationManager.AppSettings["MessageBody.Ping"];
                _messageBodyTcpConnect = ConfigurationManager.AppSettings["MessageBody.TcpConnect"];
                _messageBodyNamePoison = ConfigurationManager.AppSettings["MessageBody.NamePoison"];
                _messageBodyRogueDhcp = ConfigurationManager.AppSettings["MessageBody.RogueDhcp"];
            }

            catch (Exception ex)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"Error reading configuration file: {ex.Message}",
                    EventLogEntryType.Error,
                    400);
            }
        }
    }
}

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Configuration
{
    // In-memory copy of the tcpTrigger service configuration (tcpTrigger.xml).
    // Use ConfigReader to load it and ConfigWriter to save it.
    public class ServiceConfig
    {
        // Detect
        public bool IsDetectTcpEnabled { get; set; }
        public bool IsDetectUdpEnabled { get; set; }
        public bool IsDetectIcmpEnabled { get; set; }

        // Notify
        public ObservableCollection<Notification> Notifications { get; } = new ObservableCollection<Notification>();

        // Ignore
        public ObservableCollection<IgnoreRule> IgnoreRules { get; } = new ObservableCollection<IgnoreRule>();

        // Devices
        public HashSet<string> ExcludedNetworkInterfaces { get; } = new HashSet<string>();

        // Set by ConfigReader when the file had legacy port lists converted to ignore rules.
        public bool HasConvertedPortLists { get; set; }

        // Settings for a new installation, used when there's no configuration file:
        // detect TCP (all ports) and ICMP, and log detections to a file in ProgramData.
        public static ServiceConfig CreateDefault()
        {
            ServiceConfig config = new ServiceConfig
            {
                IsDetectTcpEnabled = true,
                IsDetectIcmpEnabled = true
            };
            config.Notifications.Add(new NotificationLogFile
            {
                IsEnabled = true,
                Path = Path.Combine(Path.GetDirectoryName(ConfigPaths.ProgramDataPath), "tcpTrigger.log")
            });
            return config;
        }
    }
}
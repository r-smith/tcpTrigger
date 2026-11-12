using System;
using System.Windows;
using System.Windows.Controls;
using tcpTrigger.GUI.Configuration;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Views
{
    public partial class SettingsDetect : UserControl
    {
        private readonly ServiceConfig _config;

        // Raised when the user changes any setting on this page.
        public event EventHandler Changed;

        public SettingsDetect(ServiceConfig config)
        {
            InitializeComponent();

            _config = config;
            ApplySettingsToGUI();

            UdpRuleAdded.Content = $"Added the ignore rule \"{CommonUdpTrafficRule.Name}\" for UDP ports "
                + $"{string.Join(", ", CommonUdpTrafficRule.Ports)}. You can change it on the Ignore page.";
        }

        // Shared by the ICMP and TCP checkboxes. UDP has its own handler, DetectUdp_Click.
        private void Input_Changed(object sender, RoutedEventArgs e)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        // Turning UDP on adds the common UDP traffic rule, unless it's already there. Turning
        // it off removes the rule again, as long as the user hasn't changed it.
        private void DetectUdp_Click(object sender, RoutedEventArgs e)
        {
            IgnoreRule rule = CommonUdpTrafficRule.Find(_config.IgnoreRules);
            if (DetectUdp.IsChecked == true)
            {
                if (rule == null)
                {
                    _config.IgnoreRules.Add(CommonUdpTrafficRule.Create());
                    UdpRuleAdded.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (rule != null && CommonUdpTrafficRule.IsUnchanged(rule))
                {
                    _config.IgnoreRules.Remove(rule);
                }
                UdpRuleAdded.Visibility = Visibility.Collapsed;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void ApplySettingsToGUI()
        {
            DetectIcmp.IsChecked = _config.IsDetectIcmpEnabled;
            DetectTcp.IsChecked = _config.IsDetectTcpEnabled;
            DetectUdp.IsChecked = _config.IsDetectUdpEnabled;

            if (_config.HasConvertedPortLists)
            {
                PortListsConverted.Visibility = Visibility.Visible;
            }
        }

        public void UpdateSettings()
        {
            _config.IsDetectIcmpEnabled = DetectIcmp.IsChecked == true;
            _config.IsDetectTcpEnabled = DetectTcp.IsChecked == true;
            _config.IsDetectUdpEnabled = DetectUdp.IsChecked == true;
        }
    }
}
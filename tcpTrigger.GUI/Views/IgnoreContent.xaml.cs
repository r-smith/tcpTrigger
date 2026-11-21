using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using tcpTrigger.GUI.Models;
using tcpTrigger.GUI.Services;
using tcpTrigger.GUI.Validation;

namespace tcpTrigger.GUI.Views
{
    public partial class IgnoreContent : UserControl
    {
        private readonly IgnoreRule _selectedIgnoreRule;

        // For creating a new rule.
        public IgnoreContent()
        {
            InitializeComponent();

            _selectedIgnoreRule = new IgnoreRule { IsEnabled = true };
            DataContext = _selectedIgnoreRule;
            ShowFormatHelp();
            ShowLocalAddresses();
        }

        // For editing an existing rule, or for a new rule that starts filled in.
        public IgnoreContent(IgnoreRule rule)
        {
            InitializeComponent();

            if (rule.Sources.Count > 0 && rule.Sources[0] == "any")
            {
                MatchAnySource.IsSelected = true;
            }
            else
            {
                SourceList.Text = string.Join(Environment.NewLine, rule.Sources);
            }

            if (rule.Destinations.Count > 0 && rule.Destinations[0] != "any")
            {
                LimitDestinations.IsChecked = true;
                DestinationList.Text = string.Join(Environment.NewLine, rule.Destinations);
            }

            if (rule.Services.Count > 0 && rule.Services[0] == "any")
            {
                MatchAnyService.IsSelected = true;
            }
            else
            {
                ServiceList.Text = string.Join(Environment.NewLine, rule.Services);
            }

            _selectedIgnoreRule = rule;
            DataContext = _selectedIgnoreRule;
            ShowFormatHelp();
            ShowLocalAddresses();
        }

        // The info icons show the same format help as the error messages. Screen readers get it from the boxes.
        private void ShowFormatHelp()
        {
            Style toolTipStyle = (Style)FindResource("HelpToolTip");
            SourceFormatHelp.ToolTip = new ToolTip { Content = s_addressFormatHelp, Style = toolTipStyle };
            ServiceFormatHelp.ToolTip = new ToolTip { Content = s_serviceFormatHelp, Style = toolTipStyle };
            AutomationProperties.SetHelpText(SourceList, s_addressFormatHelp);
            AutomationProperties.SetHelpText(ServiceList, s_serviceFormatHelp);
        }

        // Shows this computer's addresses under the destination box. Hidden if none are found.
        private void ShowLocalAddresses()
        {
            List<string> addresses = LocalAddresses.GetIPv4();
            foreach (string address in addresses)
            {
                Button button = new Button
                {
                    Content = address,
                    Style = (Style)FindResource("VariableButton")
                };
                button.Click += (sender, e) => AddDestination(address);
                LocalAddressButtons.Children.Add(button);
            }

            if (addresses.Count == 0)
            {
                LocalAddressButtons.Visibility = Visibility.Collapsed;
            }
        }

        // Adds an address to the end of the destination box on its own line, unless it's
        // already listed.
        private void AddDestination(string address)
        {
            DestinationList.Focus();
            if (IgnoreRuleInput.ParseLines(DestinationList.Text).Contains(address))
            {
                return;
            }

            string text = DestinationList.Text;
            bool needsNewLine = text.Length > 0 && text[text.Length - 1] != '\n';
            DestinationList.CaretIndex = text.Length;
            DestinationList.SelectedText = (needsNewLine ? Environment.NewLine : "") + address;
            DestinationList.CaretIndex = DestinationList.Text.Length;
        }

        // Start with the name textbox selected.
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            FriendlyName.Focus();
            FriendlyName.CaretIndex = FriendlyName.Text.Length;
        }

        public IgnoreRule GetNewRule()
        {
            UpdateRuleData();
            _selectedIgnoreRule.Created = DateTime.Now;
            return _selectedIgnoreRule;
        }

        public void UpdateRuleData()
        {
            FriendlyName.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            EnabledState.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateSource();

            _selectedIgnoreRule.Modified = DateTime.Now;

            if (MatchAnySource.IsSelected)
            {
                _selectedIgnoreRule.Sources = new List<string> { "any" };
            }
            else
            {
                _selectedIgnoreRule.Sources = IgnoreRuleInput.SortAddresses(IgnoreRuleInput.ParseLines(SourceList.Text));
            }

            if (LimitDestinations.IsChecked == true)
            {
                _selectedIgnoreRule.Destinations = IgnoreRuleInput.SortAddresses(IgnoreRuleInput.ParseLines(DestinationList.Text));
            }
            else
            {
                _selectedIgnoreRule.Destinations = new List<string> { "any" };
            }

            if (MatchAnyService.IsSelected)
            {
                _selectedIgnoreRule.Services = new List<string> { "any" };
            }
            else
            {
                _selectedIgnoreRule.Services = IgnoreRuleInput.SortServices(IgnoreRuleInput.ParseLines(ServiceList.Text));
            }
        }

        public ValidationOutcome ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(FriendlyName.Text))
            {
                return ValidationOutcome.Invalid("Please enter a rule name.", FriendlyName);
            }

            if (MatchSpecificSources.IsSelected)
            {
                if (string.IsNullOrWhiteSpace(SourceList.Text))
                {
                    return ValidationOutcome.Invalid("Please enter at least one source IP address." + s_paragraph + s_addressFormatHelp, SourceList);
                }

                string invalidSource = IgnoreRuleInput.GetFirstInvalidAddress(SourceList.Text);
                if (invalidSource != null)
                {
                    return ValidationOutcome.Invalid($"'{invalidSource}' is not a valid source IP address." + s_paragraph + s_addressFormatHelp, SourceList);
                }
            }

            if (LimitDestinations.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(DestinationList.Text))
                {
                    return ValidationOutcome.Invalid("Please enter at least one destination IP address." + s_paragraph + s_addressFormatHelp, DestinationList);
                }

                string invalidDestination = IgnoreRuleInput.GetFirstInvalidAddress(DestinationList.Text);
                if (invalidDestination != null)
                {
                    return ValidationOutcome.Invalid($"'{invalidDestination}' is not a valid destination IP address." + s_paragraph + s_addressFormatHelp, DestinationList);
                }
            }

            if (MatchSpecificServices.IsSelected)
            {
                if (string.IsNullOrWhiteSpace(ServiceList.Text))
                {
                    return ValidationOutcome.Invalid("Please enter at least one port or protocol." + s_paragraph + s_serviceFormatHelp, ServiceList);
                }

                string invalidService = IgnoreRuleInput.GetFirstInvalidService(ServiceList.Text);
                if (invalidService != null)
                {
                    return ValidationOutcome.Invalid($"'{invalidService}' is not a valid port or protocol." + s_paragraph + s_serviceFormatHelp, ServiceList);
                }
            }

            return ValidationOutcome.ValidResult;
        }

        private static readonly string s_paragraph = Environment.NewLine + Environment.NewLine;

        private static readonly string s_addressFormatHelp =
            "Enter one per line, as:" + Environment.NewLine
            + "- A single IPv4 address, such as 192.168.0.1" + Environment.NewLine
            + "- An IP range, such as 192.168.0.1-100" + Environment.NewLine
            + "- CIDR notation, such as 192.168.0.0/24";

        private static readonly string s_serviceFormatHelp =
            "Enter one per line, as:" + Environment.NewLine
            + "- A protocol: icmp, tcp, or udp" + Environment.NewLine
            + "- A TCP port, such as 443" + Environment.NewLine
            + "- A TCP port range, such as 8000-9000" + Environment.NewLine
            + "- A UDP port or port range, such as udp:53 or udp:5000-6000";

        private void SourceMatch_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MatchAnySource?.IsSelected == true && MatchAnyService?.IsSelected == true && MatchSpecificServices != null)
            {
                MatchSpecificServices.IsSelected = true;
                ServiceList?.Focus();
            }
            else if (MatchAnySource?.IsSelected == true)
            {
                ServiceList?.Focus();
            }
            else if (MatchSpecificSources?.IsSelected == true)
            {
                SourceList?.Focus();
            }
        }

        // Puts the cursor in the destination box when the link opens it. The box isn't laid out
        // yet when this runs, so wait for that. Skip it while the page loads, so the name box
        // keeps the focus when editing a rule that has destinations.
        private void LimitDestinations_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => DestinationList.Focus()), DispatcherPriority.Loaded);
        }

        private void ServiceMatch_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MatchAnyService?.IsSelected == true && MatchAnySource?.IsSelected == true && MatchSpecificSources != null)
            {
                MatchSpecificSources.IsSelected = true;
                SourceList?.Focus();
            }
            else if (MatchAnyService?.IsSelected == true)
            {
                SourceList?.Focus();
            }
            else if (MatchSpecificServices?.IsSelected == true)
            {
                ServiceList?.Focus();
            }
        }
    }
}

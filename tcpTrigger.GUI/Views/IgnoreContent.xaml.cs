using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
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
                MatchSpecificDestinations.IsSelected = true;
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
            ShowLocalAddresses();
        }

        // Lists this computer's addresses under the destination box, so they don't have to be looked up.
        private void ShowLocalAddresses()
        {
            List<string> addresses = LocalAddresses.GetIPv4();
            DestinationHint.Text = addresses.Count > 0
                ? "One per line. This computer: " + string.Join(", ", addresses)
                : "One per line, such as 192.168.0.1, 192.168.0.1-100, or 192.168.0.0/24";
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

            if (MatchAnyDestination.IsSelected)
            {
                _selectedIgnoreRule.Destinations = new List<string> { "any" };
            }
            else
            {
                _selectedIgnoreRule.Destinations = IgnoreRuleInput.SortAddresses(IgnoreRuleInput.ParseLines(DestinationList.Text));
            }

            if (MatchAnyService.IsSelected)
            {
                _selectedIgnoreRule.Services = new List<string> { "any" };
            }
            else
            {
                _selectedIgnoreRule.Services = IgnoreRuleInput.ParseLines(ServiceList.Text);
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

            if (MatchSpecificDestinations.IsSelected)
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
            + "- A port, such as 443" + Environment.NewLine
            + "- A port range, such as 8000-9000" + Environment.NewLine
            + "- A protocol and port, such as udp:53" + Environment.NewLine
            + "- A protocol and port range, such as tcp:20-80";

        // Each choice set to "specific" shows its list box. The boxes sit side by side, one column
        // each, but there are always at least two columns, so a single box stays the same width.
        // The choices change while the page loads, before ListsGrid exists, so check for that.
        private void UpdateListColumns()
        {
            if (ListsGrid == null)
            {
                return;
            }

            int shown = (MatchSpecificSources.IsSelected ? 1 : 0)
                + (MatchSpecificDestinations.IsSelected ? 1 : 0)
                + (MatchSpecificServices.IsSelected ? 1 : 0);
            ListsGrid.Columns = Math.Max(2, shown);
        }

        private void SourceMatch_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateListColumns();
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

        private void DestinationMatch_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateListColumns();
            if (MatchSpecificDestinations?.IsSelected == true)
            {
                DestinationList?.Focus();
            }
        }

        private void ServiceMatch_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateListColumns();
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
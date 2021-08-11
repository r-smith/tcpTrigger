using System;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Windows;

namespace tcpTrigger.Editor
{
    public partial class MainWindow : Window
    {
        private bool ValidateSettings()
        {
            if (MonitorTcpOption.IsChecked == true)
            {
                if (
                    (TcpAllPortsOption.IsChecked == false && TcpIncludePorts.Text.Length > 0 && !IsTcpPortsValid(TcpIncludePorts.Text))
                    || (TcpExcludePorts.Text.Length > 0 && !IsTcpPortsValid(TcpExcludePorts.Text)))
                {
                    ShowMessageBox(
                        message: "A port must be a number between 1-65535." + Environment.NewLine
                                 + "Use a comma to specify multiple ports." + Environment.NewLine
                                 + "Use a hyphen to specify ranges." + Environment.NewLine + Environment.NewLine
                                 + "Example: 21,23,400-450,3389",
                        title: "Invalid TCP port number(s)",
                        type: DialogWindow.Type.Error,
                        tab: MainTab);
                    return false;
                }
            }

            if (MonitorUdpOption.IsChecked == true)
            {
                if (
                    (UdpAllPortsOption.IsChecked == false && UdpIncludePorts.Text.Length > 0 && !IsTcpPortsValid(UdpIncludePorts.Text))
                    || (UdpExcludePorts.Text.Length > 0 && !IsTcpPortsValid(UdpExcludePorts.Text)))
                {
                    ShowMessageBox(
                        message: "A port must be a number between 1-65535." + Environment.NewLine
                                 + "Use a comma to specify multiple ports." + Environment.NewLine
                                 + "Use a hyphen to specify ranges." + Environment.NewLine + Environment.NewLine
                                 + "Example: 21,23,400-450,3389",
                        title: "Invalid UDP port number(s)",
                        type: DialogWindow.Type.Error,
                        tab: MainTab);
                    return false;
                }
            }

            if (MonitorDhcpOption.IsChecked == true && DhcpServers.Text.Length > 0)
            {
                if (!IsIpAddressListValid(DhcpServers.Text))
                {
                    ShowMessageBox(
                        message: "DHCP servers must be entered by IP address. If you have more than one server, use a comma to separate them.",
                        title: "Invalid DHCP server(s)",
                        type: DialogWindow.Type.Error,
                        tab: MainTab,
                        control: DhcpServers);
                    return false;
                }
            }

            if (LogOption.IsChecked == true)
            {
                if (LogPath.Text.Length == 0)
                {
                    ShowMessageBox(
                        message: "You have the option to write to a log file enabled, but no path was entered.",
                        title: "No log path specified",
                        type: DialogWindow.Type.Error,
                        tab: ActionsTab,
                        control: LogPath);
                    return false;
                }
            }

            if (LaunchAppOption.IsChecked == true)
            {
                if (ApplicationPath.Text.Length == 0)
                {
                    ShowMessageBox(
                        message: "You have the option to launch an external application enabled, but no path was entered.",
                        title: "No application path specified",
                        type: DialogWindow.Type.Error,
                        tab: ActionsTab,
                        control: ApplicationPath);
                    return false;
                }
            }

            if (SendEmailOption.IsChecked == true)
            {
                if (EmailServer.Text.Length == 0)
                {
                    ShowMessageBox(
                        message: "You have email notifications enabled, but no email server was entered.",
                        title: "Missing email server",
                        tab: EmailTab,
                        control: EmailServer);
                    return false;
                }

                if (EmailPort.Text.Length == 0
                    || !int.TryParse(EmailPort.Text, out int n)
                    || n <= 0
                    || n > 65535)
                {
                    ShowMessageBox(
                        message: "Enter the port number used by your email server."
                                 + Environment.NewLine + "Typical port numbers include: 25, 465, and 587.",
                        title: "Missing email server port",
                        type: DialogWindow.Type.Error,
                        tab: EmailTab,
                        control: EmailPort);
                    return false;
                }

                if (EmailRecipient.Text.Length == 0 || !IsEmailAddressListValid(EmailRecipient.Text))
                {
                    ShowMessageBox(
                        message: "Check to ensure the recipient you entered is formatted like a standard email address. "
                                 + Environment.NewLine + "If you have more than one recipient, use a comma to separate them.",
                        title: "Invalid email recipient(s)",
                        type: DialogWindow.Type.Error,
                        tab: EmailTab,
                        control: EmailRecipient);
                    return false;
                }

                if (EmailSender.Text.Length == 0 || !IsEmailAddressListValid(EmailSender.Text) || EmailSender.Text.Contains(','))
                {
                    ShowMessageBox(
                        message: "Check to ensure the sender address you entered is formatted like a standard email address.",
                        title: "Invalid email sender",
                        type: DialogWindow.Type.Error,
                        tab: EmailTab,
                        control: EmailSender);
                    return false;
                }

                if (EmailSubject.Text.Length == 0)
                {
                    ShowMessageBox(
                        message: "You have email notifications enabled, but no message subject for your notifications was entered.",
                        title: "Missing message subject",
                        tab: MessagesTab,
                        control: EmailSubject);
                    return false;
                }

                if (EmailBody.Text.Length == 0)
                {
                    ShowMessageBox(
                        message: "You have email notifications enabled, but no message body for your notifications was entered.",
                        title: "Missing message body",
                        tab: MessagesTab,
                        control: EmailBody);
                    return false;
                }
            }

            if (Whitelist.Text.Length > 0 && !IsIpAddressListValid(Whitelist.Text))
            {
                ShowMessageBox(
                        message: "Check to ensure the IP addresses you entered are formatted properly."
                                 + Environment.NewLine + "IP addresses can be entered one per line, or comma-separated.",
                        title: "Invalid whitelist",
                        type: DialogWindow.Type.Error,
                        tab: WhitelistTab,
                        control: Whitelist);
                return false;
            }

            if (RateLimitSeconds.Text.Length > 0)
            {
                if (!int.TryParse(RateLimitSeconds.Text, out int n) || n <= 0 || n > 3600)
                {
                    ShowMessageBox(
                        message: "The number of seconds used for rate limiting must be between 1 and 3600.",
                        title: "Invalid rate limit",
                        type: DialogWindow.Type.Error,
                        tab: AdvancedTab,
                        control: RateLimitSeconds);
                    return false;
                }
            }

            return true;
        }

        private bool IsTcpPortsValid(string ports)
        {
            try
            {
                int[] portNumbers = (from part in ports.Split(',')
                                     let range = part.Split('-')
                                     let start = int.Parse(range[0])
                                     let end = int.Parse(range[range.Length - 1])
                                     from i in Enumerable.Range(start, end - start + 1)
                                     orderby i
                                     select i).Distinct().ToArray();
                string result = string.Join(",", portNumbers.Select(x => x.ToString()).ToArray());

                for (int i = 0; i < portNumbers.Length; ++i)
                    if (portNumbers[i] < 1 || portNumbers[i] > 65535)
                        return false;
            }
            catch
            {
                return false;
            }

            return true;
        }

        private bool IsIpAddressListValid(string ipAddresses)
        {
            // Validate list of IP addresses in a string. IP addresses can be separated by commas or newlines.
            // Note: The IPAddress.TryParse() method used here returns true for any string that can be parsed as an Int64.

            // Split string to array, split by both commas and newlines, then trim each item.
            string[] ipArray =
                ipAddresses
                .Trim()
                .Split(new string[] { ",", "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ip => ip.Trim()).ToArray();
            for (int i = 0; i < ipArray.Length; i++)
            {
                if (!IPAddress.TryParse(ipArray[i], out _))
                    return false;
            }
            return true;
        }

        private bool IsEmailAddressListValid(string emails)
        {
            // Validate a comma-separated list of email addresses.
            try
            {
                string[] emailArray = emails.Trim().Split(',');
                for (int i = 0; i < emailArray.Length; i++)
                {
                    _ = new MailAddress(emailArray[i].Trim());
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
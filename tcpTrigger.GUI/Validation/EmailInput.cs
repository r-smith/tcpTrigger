using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;

namespace tcpTrigger.GUI.Validation
{
    // Parsing and validation for email notification fields.
    internal static class EmailInput
    {
        // Rate limiting and waiting can't be turned off, so the minimum is 1 second.
        public const int MinRateLimitSeconds = 1;
        public const int MaxRateLimitSeconds = 86400; // 24 hours
        public const int MinWaitSeconds = 1;
        public const int MaxWaitSeconds = 600; // 10 minutes

        // Recipients are separated by commas or semicolons.
        public static List<string> ParseRecipients(string recipients)
        {
            List<string> recipientList = new List<string>();

            string[] parts = recipients.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    recipientList.Add(part.Trim());
                }
            }

            return recipientList;
        }

        // True if there's at least one recipient and every recipient is a valid address.
        public static bool IsValidRecipients(string recipients)
        {
            List<string> recipientList = ParseRecipients(recipients);
            return recipientList.Count > 0 && recipientList.All(IsValidAddress);
        }

        // True for a plain email address such as "alerts@example.com".
        // A display name form like "Alerts <alerts@example.com>" is rejected.
        public static bool IsValidAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            try
            {
                MailAddress parsed = new MailAddress(address);
                return parsed.Address == address.Trim();
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static bool IsValidRateLimit(string rateLimit)
        {
            return int.TryParse(rateLimit, out int seconds) && seconds >= MinRateLimitSeconds && seconds <= MaxRateLimitSeconds;
        }

        public static bool IsValidWaitSeconds(string value)
        {
            return int.TryParse(value, out int seconds) && seconds >= MinWaitSeconds && seconds <= MaxWaitSeconds;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace tcpTrigger
{
    // Reminds about Microsoft 365 client secrets that are about to expire. Checks once when started, then every
    // day at 9:00 local time. A reminder is a warning in the event log and an email through the notification itself.
    internal sealed class ClientSecretReminder
    {
        // How many days before a client secret expires to remind about it.
        private const int ReminderDays = 7;

        // The local time of the daily check.
        private static readonly TimeSpan s_checkTime = TimeSpan.FromHours(9);

        // The Microsoft 365 email notifications that have a client secret expiry date.
        private readonly List<NotificationEmail> _notifications = new List<NotificationEmail>();
        private readonly object _lock = new object();
        private Timer _timer;
        private bool _isFirstCheck = true;
        private bool _isStopped;

        public ClientSecretReminder(List<Notification> notifications)
        {
            foreach (Notification notification in notifications)
            {
                if (notification is NotificationEmail email
                    && email.SendMethod == EmailSendMethod.Microsoft365
                    && email.ClientSecretExpires.HasValue)
                {
                    _notifications.Add(email);
                }
            }
        }

        // Runs the first check right away on a background thread, so a slow email doesn't hold up the service.
        // With no client secret expiry dates, there's nothing to check, so no timer is started.
        public void Start()
        {
            if (_notifications.Count == 0)
            {
                return;
            }

            // The timer is created stopped, then started, so _timer is set before the first check can use it.
            lock (_lock)
            {
                _timer = new Timer(Timer_Tick, null, Timeout.Infinite, Timeout.Infinite);
                _timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _isStopped = true;
                _timer?.Dispose();
            }
        }

        private void Timer_Tick(object state)
        {
            bool isFirstCheck;
            lock (_lock)
            {
                if (_isStopped)
                {
                    return;
                }
                isFirstCheck = _isFirstCheck;
                _isFirstCheck = false;
            }

            // An exception on a timer thread would end the service, so it's logged instead.
            try
            {
                Check(isFirstCheck);
            }
            catch (Exception ex)
            {
                Logger.WriteError(
                    "tcpTrigger failed to check for expiring Microsoft 365 client secrets."
                    + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    Logger.EventCode.NotificationFailed);
            }

            lock (_lock)
            {
                if (!_isStopped)
                {
                    _timer.Change(GetTimeUntilNextCheck(), Timeout.InfiniteTimeSpan);
                }
            }
        }

        // The first check reminds about every client secret that expires within ReminderDays, or has expired.
        // The daily check reminds about a client secret only on the day it's ReminderDays away.
        private void Check(bool isFirstCheck)
        {
            foreach (NotificationEmail email in _notifications)
            {
                DateTime expires = email.ClientSecretExpires.Value.Date;
                int daysLeft = (int)(expires - DateTime.Today).TotalDays;
                bool isReminderDue = isFirstCheck ? daysLeft <= ReminderDays : daysLeft == ReminderDays;
                if (!isReminderDue)
                {
                    continue;
                }

                string status = daysLeft >= 0
                    ? $"is expiring {DescribeTimeLeft(daysLeft)}."
                    : "has expired, so the notification can't send email until it has a new secret.";
                Logger.WriteWarning(
                    $"The Microsoft 365 client secret for the notification '{email.Name}' {status}"
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + "Create a new client secret for the app registration in Microsoft Entra. Then update the notification "
                    + "in tcpTrigger with the new secret and its expiry date."
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + $"Notification: {email.Name}{Environment.NewLine}"
                    + $"Expiration date: {expires.ToString(ConfigNames.DateFormat, CultureInfo.InvariantCulture)}{Environment.NewLine}"
                    + $"Tenant ID: {email.TenantId}{Environment.NewLine}"
                    + $"Client ID: {email.ClientId}",
                    Logger.EventCode.ClientSecretExpiring);

                // An expired secret can't send the reminder, so only the warning is written.
                if (daysLeft >= 0)
                {
                    EmailNotifier.SendSecretReminder(email, DescribeTimeLeft(daysLeft), expires);
                }
            }
        }

        // Describes how soon a client secret that hasn't expired yet expires: "in 7 days", "tomorrow", or "today".
        private static string DescribeTimeLeft(int daysLeft)
        {
            if (daysLeft > 1)
            {
                return $"in {daysLeft} days";
            }
            return daysLeft == 1 ? "tomorrow" : "today";
        }

        // Returns how long until the next daily check. It's worked out in UTC, so a daylight saving time change
        // doesn't move the check off 9:00. A check that runs a little early or late still waits for the next day.
        private static TimeSpan GetTimeUntilNextCheck()
        {
            DateTime now = DateTime.Now;
            DateTime next = now.Date + s_checkTime;
            if (next <= now.AddMinutes(1))
            {
                next = next.AddDays(1);
            }
            return next.ToUniversalTime() - DateTime.UtcNow;
        }
    }
}

using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Views
{
    public interface INotificationHandler
    {
        Notification GetNotificationData();
        ValidationOutcome ValidateInputs();
        void UpdateNotificationData();
    }
}

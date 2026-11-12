using System.Windows.Controls;

namespace tcpTrigger.GUI.Views
{
    public class ValidationOutcome
    {
        public bool IsValid { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public Control AssociatedControl { get; set; }

        public static ValidationOutcome ValidResult => new ValidationOutcome { IsValid = true, Message = string.Empty };

        public static ValidationOutcome Invalid(string message, Control control)
        {
            return new ValidationOutcome { IsValid = false, Message = message, AssociatedControl = control };
        }
    }
}
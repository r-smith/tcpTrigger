using System.Windows;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Views
{
    public partial class IgnoreEditWindow : EditorWindow
    {
        private readonly IgnoreRule _selectedIgnoreRule;
        private readonly IgnoreContent _ignoreContent;

        public IgnoreEditWindow(IgnoreRule rule)
        {
            InitializeComponent();
            _selectedIgnoreRule = rule;
            _ignoreContent = new IgnoreContent(rule);
            IgnoreRuleContent.Content = _ignoreContent;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            ValidationOutcome validation = _ignoreContent.ValidateInputs();

            if (validation.IsValid)
            {
                _ignoreContent.UpdateRuleData();
                DialogResult = true;
            }
            else
            {
                ShowErrorDialog(validation);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            ShowDeleteDialog(_selectedIgnoreRule);
        }
    }
}
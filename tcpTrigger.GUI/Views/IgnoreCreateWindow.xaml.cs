using System.Windows;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Views
{
    public partial class IgnoreCreateWindow : EditorWindow
    {
        private readonly IgnoreContent _ignoreContent;

        // The new rule. Set when ShowDialog() returns true.
        public IgnoreRule CreatedRule { get; private set; }

        // `draft` fills in the editor, such as with a source/destination. Without one, it starts empty.
        public IgnoreCreateWindow(IgnoreRule draft = null)
        {
            InitializeComponent();
            _ignoreContent = draft == null ? new IgnoreContent() : new IgnoreContent(draft);
            IgnoreRuleContent.Content = _ignoreContent;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            ValidationOutcome validation = _ignoreContent.ValidateInputs();

            if (validation.IsValid)
            {
                CreatedRule = _ignoreContent.GetNewRule();
                DialogResult = true;
            }
            else
            {
                ShowErrorDialog(validation);
            }
        }
    }
}
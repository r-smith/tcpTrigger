using System;
using System.Windows;
using System.Windows.Controls;

namespace tcpTrigger.GUI.Views.Controls
{
    public partial class VariablePicker : UserControl
    {
        private string[] _variables = new string[0];

        public VariablePicker()
        {
            InitializeComponent();
            UpdateToggleText();
        }

        // Raised with the variable that was clicked, such as "{SOURCE_IP}".
        public event EventHandler<string> VariableClicked;

        // Short help shown above the variables.
        public string Hint
        {
            get { return HintText.Text; }
            set { HintText.Text = value; }
        }

        // The variables to show, each written the way it's typed, such as "{SOURCE_IP}".
        public string[] Variables
        {
            get { return _variables; }
            set
            {
                _variables = value ?? new string[0];
                VariableButtons.Children.Clear();
                foreach (string variable in _variables)
                {
                    Button button = new Button
                    {
                        Content = variable,
                        Style = (Style)FindResource("VariableButton")
                    };
                    button.Click += (sender, e) => VariableClicked?.Invoke(this, variable);
                    VariableButtons.Children.Add(button);
                }
                UpdateToggleText();
            }
        }

        // Inserts a variable at the cursor, replacing any selected text, and leaves the
        // cursor after it.
        public static void InsertInto(TextBox textBox, string variable)
        {
            textBox.Focus();
            textBox.SelectedText = variable;
            textBox.CaretIndex = textBox.SelectionStart + textBox.SelectionLength;
        }

        private void ShowToggle_Changed(object sender, RoutedEventArgs e)
        {
            UpdateToggleText();
        }

        private void UpdateToggleText()
        {
            ShowToggle.Content = ShowToggle.IsChecked == true
                ? "Hide variables"
                : "Show variables (" + _variables.Length + ")";
        }
    }
}

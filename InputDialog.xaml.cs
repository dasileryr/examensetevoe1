using System.Windows;
using System.Windows.Input;

namespace FileDownloader
{
    public partial class InputDialog : Window
    {
        public string Answer { get; set; } = string.Empty;
        public new string Title { get; set; } = "Ввод";
        public string Message { get; set; } = "Введите значение:";

        public InputDialog(string title, string message, string defaultValue = "")
        {
            InitializeComponent();
            base.Title = title;
            Title = title;
            Message = message;
            Answer = defaultValue;
            MessageTextBlock.Text = message;
            AnswerTextBox.Text = defaultValue;
            AnswerTextBox.Focus();
            AnswerTextBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Answer = AnswerTextBox.Text;
            DialogResult = true;
        }

        private void AnswerTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DialogResult = true;
            }
        }
    }
}


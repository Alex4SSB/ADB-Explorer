using ADB_Explorer.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static ADB_Explorer.Helpers.TextHelper;

namespace ADB_Explorer.Controls
{
    /// <summary>
    /// Interaction logic for MaskedTextBox.xaml
    /// </summary>
    public partial class MaskedTextBox : UserControl
    {
        public MaskedTextBox()
        {
            InitializeComponent();
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string),
              typeof(MaskedTextBox), new PropertyMetadata(""));

        public ValidationType ValidationType
        {
            get => (ValidationType)GetValue(ValidationTypeProperty);
            set => SetValue(ValidationTypeProperty, value);
        }

        public static readonly DependencyProperty ValidationTypeProperty =
            DependencyProperty.Register("ValidationType", typeof(ValidationType),
              typeof(MaskedTextBox), new PropertyMetadata(default(ValidationType)));

        public char Separator
        {
            get => (char)GetValue(SeparatorProperty);
            set => SetValue(SeparatorProperty, value);
        }

        public static readonly DependencyProperty SeparatorProperty =
            DependencyProperty.Register("Separator", typeof(char),
              typeof(MaskedTextBox), new PropertyMetadata(default(char)));

        public int MaxChars
        {
            get => (int)GetValue(MaxCharsProperty);
            set => SetValue(MaxCharsProperty, value);
        }

        public static readonly DependencyProperty MaxCharsProperty =
            DependencyProperty.Register("MaxChars", typeof(int),
              typeof(MaskedTextBox), new PropertyMetadata(default(int)));

        public bool IsNumeric
        {
            get => (bool)GetValue(IsNumericProperty);
            set => SetValue(IsNumericProperty, value);
        }

        public static readonly DependencyProperty IsNumericProperty =
            DependencyProperty.Register("IsNumeric", typeof(bool),
              typeof(MaskedTextBox), new PropertyMetadata(default(bool)));

        public ulong MaxNumber
        {
            get => (ulong)GetValue(MaxNumberProperty);
            set => SetValue(MaxNumberProperty, value);
        }

        public static readonly DependencyProperty MaxNumberProperty =
            DependencyProperty.Register("MaxNumber", typeof(ulong),
              typeof(MaskedTextBox), new PropertyMetadata(default(ulong)));

        public int MaxSeparators
        {
            get => (int)GetValue(MaxSeparatorsProperty);
            set => SetValue(MaxSeparatorsProperty, value);
        }

        public static readonly DependencyProperty MaxSeparatorsProperty =
            DependencyProperty.Register("MaxSeparators", typeof(int),
              typeof(MaskedTextBox), new PropertyMetadata(default(int)));

        public char[] SpecialChars
        {
            get => (char[])GetValue(SpecialCharsProperty);
            set => SetValue(SpecialCharsProperty, value);
        }

        public static readonly DependencyProperty SpecialCharsProperty =
            DependencyProperty.Register("SpecialChars", typeof(char[]),
              typeof(MaskedTextBox), new PropertyMetadata(default(char[])));

        public Style ControlStyle
        {
            get => (Style)GetValue(ControlStyleProperty);
            set => SetValue(ControlStyleProperty, value);
        }

        public static readonly DependencyProperty ControlStyleProperty =
            DependencyProperty.Register("ControlStyle", typeof(Style),
              typeof(MaskedTextBox), new PropertyMetadata(default(Style)));

        public ICommand EnterCommand
        {
            get => (ICommand)GetValue(EnterCommandProperty);
            set => SetValue(EnterCommandProperty, value);
        }

        public static readonly DependencyProperty EnterCommandProperty =
            DependencyProperty.Register("EnterCommand", typeof(ICommand),
              typeof(MaskedTextBox), new PropertyMetadata(default(ICommand)));

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox textbox = sender as TextBox;
            
            switch (ValidationType)
            {
                case ValidationType.SeparateDigits:
                    TextHelper.SeparateDigits(textbox, Separator);
                    break;
                case ValidationType.SeparateAndLimitDigits:
                    TextHelper.SeparateAndLimitDigits(textbox, Separator, MaxChars);
                    break;
                case ValidationType.LimitDigits:
                    TextHelper.LimitDigits(textbox, MaxChars);
                    break;
                case ValidationType.LimitNumber:
                    TextHelper.LimitNumber(textbox, MaxNumber);
                    break;
                case ValidationType.LimitDigitsAndNumber:
                    TextHelper.LimitDigitsAndNumber(textbox, MaxChars, MaxNumber);
                    break;
                case ValidationType.FilterString:
                    TextHelper.FilterString(textbox, SpecialChars);
                    break;
                case ValidationType.SeparateFormat:
                    TextHelper.SeparateFormat(textbox, Separator, MaxNumber, MaxSeparators);
                    break;
                default: // including None
                    break;
            }

            Text = textbox.Text;
        }
    }
}

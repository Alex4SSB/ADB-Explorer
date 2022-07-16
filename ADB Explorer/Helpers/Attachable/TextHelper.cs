using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ADB_Explorer.Helpers
{
    public static class TextHelper
    {
        public static string GetAltText(UIElement control) =>
            (string)control.GetValue(AltTextProperty);

        public static void SetAltText(UIElement control, string value) =>
            control.SetValue(AltTextProperty, value);

        public static readonly DependencyProperty AltTextProperty =
            DependencyProperty.RegisterAttached(
                "AltText",
                typeof(string),
                typeof(TextHelper),
                null);

        public static bool GetIsValidating(UIElement control) =>
            (bool)control.GetValue(IsValidatingProperty);

        public static void SetIsValidating(UIElement control, bool value) =>
            control.SetValue(IsValidatingProperty, value);

        public static readonly DependencyProperty IsValidatingProperty =
            DependencyProperty.RegisterAttached(
                "IsValidating",
                typeof(bool),
                typeof(TextHelper),
                null);

        public static object GetAltObject(UIElement control) =>
            control.GetValue(AltObjectProperty);

        public static void SetAltObject(UIElement control, object value) =>
            control.SetValue(AltObjectProperty, value);

        public static readonly DependencyProperty AltObjectProperty =
            DependencyProperty.RegisterAttached(
                "AltObject",
                typeof(object),
                typeof(TextHelper),
                null);

        public static void SeparateDigits(this TextBox textbox, char separator) => TextBoxValidation(textbox, separator);

        public static void SeparateAndLimitDigits(this TextBox textbox, char separator, int maxChars) => TextBoxValidation(textbox, separator, maxChars);

        public static void LimitDigits(this TextBox textbox, int maxLength) => TextBoxValidation(textbox, maxChars: maxLength);

        public static void LimitNumber(this TextBox textbox, ulong maxNumber) => TextBoxValidation(textbox, maxChars: $"{maxNumber}".Length, maxNumber: maxNumber);

        public static void LimitDigitsAndNumber(this TextBox textbox, int maxLength, ulong maxNumber) => TextBoxValidation(textbox, maxChars: maxLength, maxNumber: maxNumber);

        public static void FilterString(this TextBox textbox, params char[] invalidChars) => TextBoxValidation(textbox, specialChars: invalidChars, numeric: false);

        public static void SeparateFormat(this TextBox textbox, char separator, ulong maxNumber, int maxSeparators) => TextBoxValidation(textbox, specialChars: separator, maxNumber: maxNumber, maxSeparators: maxSeparators);

        public static void TextBoxValidation(TextBox textBox,
                                              char? separator = null,
                                              int maxChars = -1,
                                              bool numeric = true,
                                              ulong maxNumber = 9,
                                              int maxSeparators = -1,
                                              params char[] specialChars)
        {
            if (GetIsValidating(textBox))
                return;
            else
                SetIsValidating(textBox, true);

            var caretIndex = textBox.CaretIndex;
            var text = textBox.Text;
            var altText = GetAltText(textBox);
            var maxLength = textBox.MaxLength;

            TextBoxValidation(ref caretIndex, ref text, ref altText, ref maxLength, separator, maxChars, numeric, maxNumber, maxSeparators, specialChars);

            textBox.Text = text;
            textBox.CaretIndex = caretIndex;
            SetAltText(textBox, altText);
            textBox.MaxLength = maxLength;

            SetIsValidating(textBox, false);
        }

        /// <summary>
        /// Provides validation and separation of text in a <see cref="TextBox"/>.
        /// </summary>
        /// <param name="textBox">The textbox to be validated</param>
        /// <param name="separator">Text separator. Default is null - no separator</param>
        /// <param name="maxChars">Maximum allowed characters in the text. Default is -1 - no length validation</param>
        /// <param name="numeric">Enable numeric validation. Default is <see langword="true"/></param>
        /// <param name="specialChars">When numeric is enabled - allowed non-numeric chars. Otherwise - forbidden chars</param>
        public static void TextBoxValidation(ref int caretIndex,
                                             ref string text,
                                             ref string altText,
                                             ref int maxLength,
                                             char? separator = null,
                                             int maxChars = -1,
                                             bool numeric = true,
                                             ulong maxNumber = 9,
                                             int maxSeparators = -1,
                                             params char[] specialChars)
        {
            var output = "";
            var numbers = "";
            var deletedChars = 0;
            if (altText is null)
                altText = "";

            if (text.Length < 1)
                return;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool validChar = true;

                if (numeric)
                {
                    if (char.IsDigit(c))
                    {
                        validChar = ulong.Parse($"{c}") <= maxNumber;
                        if (maxNumber > 9)
                        {
                            var index = specialChars.Length == 0 || !text.Contains(specialChars[0]) ? 0 : text[..i].LastIndexOf(specialChars[0]) + 1;
                            if (index < i)
                            {
                                // Valid if parse fails
                                if (ulong.TryParse(text[index..(i + 1)], out ulong res))
                                    validChar = res <= maxNumber;
                            }
                        }
                    }
                    else
                    {
                        validChar = specialChars.Contains(c)
                            && i > 0
                            && c != text[i - 1]
                            && !(maxSeparators > 0 && text.Count(c => c == specialChars[0]) > maxSeparators
                            && i == text.LastIndexOf(specialChars[0]));
                    }
                }
                else
                {
                    validChar = !specialChars.Contains(c);
                }

                if (validChar)
                    numbers += c;
                else if (c != separator)
                    deletedChars++;
            }

            if (separator is null)
            {
                output = numbers;
            }
            else
            {
                for (int i = 0; i < numbers.Length; i++)
                {
                    output += $"{(i > 0 ? separator : "")}{numbers[i]}";
                }
            }

            if (maxNumber > 9 && specialChars.Any() && output.Any())
            {
                if (output[^1] != specialChars[0])
                {
                    var index = !text.Contains(specialChars[0]) ? 0 : output.LastIndexOf(specialChars[0]) + 1;
                    if (output.Length - index == $"{maxNumber}".Length)
                    {
                        if (maxSeparators < 0 || output.Count(c => c == specialChars[0]) < maxSeparators)
                        {
                            output += specialChars[0];
                            caretIndex++;
                        }
                    }
                }

                var items = output.Split(specialChars[0]);
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i].Length > $"{maxNumber}".Length)
                    {
                        var newItem = items[i][^$"{maxNumber}".Length..];
                        deletedChars += (items[i].Length - newItem.Length);
                        items[i] = newItem;
                    }
                }
                output = string.Join(specialChars[0], items);
            }

            if (deletedChars > 0 && (altText.Length > output.Length || deletedChars == text.Length - altText.Length))
            {
                text = altText;
                caretIndex -= deletedChars;

                return;
            }

            if (maxChars > -1)
                maxLength = separator is null ? maxChars : (maxChars * 2) - 1;

            caretIndex -= deletedChars;
            if (separator is not null)
                caretIndex += output.Count(c => c == separator) - text.Count(c => c == separator);

            text = output;

            if (caretIndex < 0)
                caretIndex = 0;

            altText = output;
        }
    }
}

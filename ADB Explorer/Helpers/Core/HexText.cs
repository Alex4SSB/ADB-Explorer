namespace ADB_Explorer.Helpers;

public static class HexText
{
    public readonly record struct CaretState(string Text, int Caret);

    public readonly record struct NibbleSelection(int Anchor, int Caret);

    public static string Format(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return "";

        return FormatDigits(Convert.ToHexString(bytes));
    }

    public static string FilterInput(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var result = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsAsciiHexDigit(c))
                result.Append(char.ToUpperInvariant(c));
            else if (char.IsWhiteSpace(c))
                result.Append(c);
        }

        return result.ToString();
    }

    public static string ExtractDigits(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var result = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsAsciiHexDigit(c))
                result.Append(char.ToUpperInvariant(c));
        }

        return result.ToString();
    }

    public static string Normalize(string? text) => FormatDigits(ExtractDigits(text));

    public static byte[] Parse(string? text)
    {
        var hex = ExtractDigits(text);
        if (hex.Length == 0)
            return [];

        if (hex.Length % 2 != 0)
            hex += "0";

        return Convert.FromHexString(hex);
    }

    public static int NibbleIndex(string text, int offset)
    {
        if (string.IsNullOrEmpty(text) || offset <= 0)
            return 0;

        if (offset > text.Length)
            offset = text.Length;

        var n = 0;
        for (var i = 0; i < offset; i++)
        {
            if (char.IsAsciiHexDigit(text[i]))
                n++;
        }

        return n;
    }

    public static int OffsetAfterNibbles(string text, int nibbleCount)
    {
        if (string.IsNullOrEmpty(text) || nibbleCount <= 0)
            return 0;

        var seen = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsAsciiHexDigit(text[i]))
                continue;

            seen++;
            if (seen == nibbleCount)
                return i + 1;
        }

        return text.Length;
    }

    public static int CaretOffsetFromNibble(string text, int nibbleIndex)
    {
        var after = OffsetAfterNibbles(text, nibbleIndex);
        if (after < text.Length && text[after] == ' ')
            return after + 1;

        return after;
    }

    /// <summary>
    /// Selection from <paramref name="anchorNibble"/> to <paramref name="caretNibble"/>.
    /// A space is included only when the digits on both sides are in the range.
    /// </summary>
    public static NibbleSelection SelectionForNibbles(string text, int anchorNibble, int caretNibble)
    {
        if (caretNibble == anchorNibble)
        {
            var offset = CaretOffsetFromNibble(text, caretNibble);
            return new NibbleSelection(offset, offset);
        }

        if (caretNibble > anchorNibble)
            return new NibbleSelection(CaretOffsetFromNibble(text, anchorNibble), OffsetAfterNibbles(text, caretNibble));

        return new NibbleSelection(OffsetAfterNibbles(text, anchorNibble), CaretOffsetFromNibble(text, caretNibble));
    }

    public static CaretState Insert(string text, int selStart, int selLength, string? incoming, bool overwrite = false)
    {
        var digits = ExtractDigits(text);
        var from = NibbleIndex(text, selStart);
        var to = NibbleIndex(text, selStart + selLength);
        var insert = ExtractDigits(incoming);

        if (selLength == 0 && overwrite && insert.Length > 0)
        {
            to = from + insert.Length;
            if (to > digits.Length)
                to = digits.Length;
        }

        var next = new StringBuilder(digits.Length + insert.Length);
        next.Append(digits, 0, from);
        next.Append(insert);
        next.Append(digits, to, digits.Length - to);

        var formatted = FormatDigits(next.ToString());
        return new CaretState(formatted, CaretOffsetFromNibble(formatted, from + insert.Length));
    }

    public static CaretState Backspace(string text, int selStart, int selLength)
    {
        if (selLength > 0)
            return Insert(text, selStart, selLength, "");

        var from = NibbleIndex(text, selStart);
        if (from == 0)
        {
            var formatted = Normalize(text);
            return new CaretState(formatted, CaretOffsetFromNibble(formatted, 0));
        }

        return DeleteRange(text, from - 1, from);
    }

    public static CaretState Delete(string text, int selStart, int selLength)
    {
        if (selLength > 0)
            return Insert(text, selStart, selLength, "");

        var from = NibbleIndex(text, selStart);
        var digits = ExtractDigits(text);
        if (from >= digits.Length)
        {
            var formatted = Normalize(text);
            return new CaretState(formatted, CaretOffsetFromNibble(formatted, from));
        }

        return DeleteRange(text, from, from + 1);
    }

    private static CaretState DeleteRange(string text, int nibbleFrom, int nibbleTo)
    {
        var digits = ExtractDigits(text);
        var next = new StringBuilder(digits.Length);
        next.Append(digits, 0, nibbleFrom);
        next.Append(digits, nibbleTo, digits.Length - nibbleTo);

        var formatted = FormatDigits(next.ToString());
        return new CaretState(formatted, CaretOffsetFromNibble(formatted, nibbleFrom));
    }

    private static string FormatDigits(string hex)
    {
        if (hex.Length == 0)
            return "";

        var result = new char[hex.Length + (hex.Length - 1) / 2];
        var o = 0;
        for (var i = 0; i < hex.Length; i++)
        {
            if (i > 0 && i % 2 == 0)
                result[o++] = ' ';
            result[o++] = hex[i];
        }

        return new string(result);
    }
}

using System;
using System.Globalization;

namespace CommandPalette;

public static class CalculatorService
{
    public static PaletteItem? TryCreateResult(
        string input)
    {
        if (string.IsNullOrWhiteSpace(input) ||
            input.Length > 200 ||
            !input.Any(char.IsDigit) ||
            !input.Any(character =>
                "+-*/%^xX×÷−".Contains(character)))
        {
            return null;
        }

        try
        {
            var parser = new ExpressionParser(input);
            var value = parser.Parse();

            if (double.IsNaN(value) || double.IsInfinity(value))
                return null;

            var result = value.ToString(
                "G15",
                CultureInfo.InvariantCulture
            );

            return new PaletteItem(
                result,
                result,
                PaletteItemType.Calculator,
                "Calculator"
            );
        }
        catch
        {
            return null;
        }
    }

    private sealed class ExpressionParser
    {
        private readonly string _text;
        private int _position;

        public ExpressionParser(string text)
        {
            _text = text
                .Replace('×', '*')
                .Replace('÷', '/')
                .Replace('−', '-')
                .Replace(',', '.');
        }

        public double Parse()
        {
            var value = ParseExpression();
            SkipWhitespace();

            if (_position != _text.Length)
                throw new FormatException();

            return value;
        }

        private double ParseExpression()
        {
            var value = ParseTerm();

            while (true)
            {
                if (Match('+'))
                    value += ParseTerm();
                else if (Match('-'))
                    value -= ParseTerm();
                else
                    return value;
            }
        }

        private double ParseTerm()
        {
            var value = ParsePower();

            while (true)
            {
                if (Match('*'))
                    value *= ParsePower();
                else if (Match('x') || Match('X'))
                    value *= ParsePower();
                else if (Match('/'))
                    value /= ParsePower();
                else if (Match('%'))
                    value %= ParsePower();
                else if (NextCharacterIs('('))
                    value *= ParsePower();
                else
                    return value;
            }
        }

        private double ParsePower()
        {
            var value = ParseUnary();

            if (Match('^'))
            {
                value = Math.Pow(value, ParsePower());
            }

            return value;
        }

        private double ParseUnary()
        {
            if (Match('+'))
                return ParseUnary();

            if (Match('-'))
                return -ParseUnary();

            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            if (Match('('))
            {
                var parenthesesValue = ParseExpression();

                if (!Match(')') && !IsAtEnd())
                    throw new FormatException();

                return parenthesesValue;
            }

            SkipWhitespace();
            var start = _position;

            while (_position < _text.Length &&
                   (char.IsDigit(_text[_position]) ||
                    _text[_position] == '.'))
            {
                _position++;
            }

            if (start == _position ||
                !double.TryParse(
                    _text[start.._position],
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var parsedValue))
            {
                throw new FormatException();
            }

            return parsedValue;
        }

        private bool Match(char expected)
        {
            SkipWhitespace();

            if (_position >= _text.Length ||
                _text[_position] != expected)
            {
                return false;
            }

            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length &&
                   char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }

        private bool NextCharacterIs(char expected)
        {
            return _position < _text.Length &&
                   _text[_position] == expected;
        }

        private bool IsAtEnd()
        {
            SkipWhitespace();
            return _position == _text.Length;
        }
    }
}

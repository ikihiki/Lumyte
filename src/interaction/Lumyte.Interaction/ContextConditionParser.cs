using System.Globalization;

namespace Lumyte.Interaction;

public sealed class ContextConditionParser(ContextKeyRegistry registry)
{
    public ContextCondition Parse(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var parser = new Parser(expression, registry);
        ContextCondition condition = parser.ParseExpression();
        parser.ExpectEnd();
        return condition;
    }

    private sealed class Parser(string text, ContextKeyRegistry registry)
    {
        private int position;

        public ContextCondition ParseExpression() => ParseOr();

        public void ExpectEnd()
        {
            SkipWhiteSpace();
            if (position != text.Length)
            {
                throw Error("Unexpected trailing input.");
            }
        }

        private ContextCondition ParseOr()
        {
            ContextCondition condition = ParseAnd();
            while (TryRead("||"))
            {
                condition |= ParseAnd();
            }

            return condition;
        }

        private ContextCondition ParseAnd()
        {
            ContextCondition condition = ParseUnary();
            while (TryRead("&&"))
            {
                condition &= ParseUnary();
            }

            return condition;
        }

        private ContextCondition ParseUnary()
        {
            if (TryRead("!"))
            {
                return !ParseUnary();
            }

            if (TryRead("("))
            {
                ContextCondition nested = ParseExpression();
                Expect(")");
                return nested;
            }

            return ParseComparison();
        }

        private ContextCondition ParseComparison()
        {
            string name = ReadIdentifier();
            if (!registry.TryGet(name, out ContextKey? key) || key is null)
            {
                throw Error($"Unknown context key '{name}'.");
            }

            bool equals = TryRead("==");
            if (!equals && !TryRead("!="))
            {
                if (key is ContextKey<bool> booleanKey)
                {
                    return booleanKey.Is(true);
                }

                throw Error("A non-boolean context key requires == or !=.");
            }

            object? value = ConvertValue(ReadValue(), key.ValueType);
            ContextCondition condition = key.EqualObject(value);
            return equals ? condition : !condition;
        }

        private object? ReadValue()
        {
            SkipWhiteSpace();
            if (position < text.Length && text[position] is '\'' or '"')
            {
                char quote = text[position++];
                int start = position;
                while (position < text.Length && text[position] != quote)
                {
                    position++;
                }

                if (position == text.Length)
                {
                    throw Error("Unterminated string literal.");
                }

                string value = text[start..position];
                position++;
                return value;
            }

            string token = ReadIdentifier();
            return token switch
            {
                "true" => true,
                "false" => false,
                "null" => null,
                _ when double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) => number,
                _ => token,
            };
        }

        private static object? ConvertValue(object? value, Type targetType)
        {
            Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (value is null)
            {
                return null;
            }

            if (effectiveType.IsInstanceOfType(value))
            {
                return value;
            }

            return Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
        }

        private string ReadIdentifier()
        {
            SkipWhiteSpace();
            int start = position;
            while (position < text.Length
                && (char.IsLetterOrDigit(text[position]) || text[position] is '.' or '_' or '-'))
            {
                position++;
            }

            if (start == position)
            {
                throw Error("Expected an identifier or value.");
            }

            return text[start..position];
        }

        private bool TryRead(string token)
        {
            SkipWhiteSpace();
            if (!text.AsSpan(position).StartsWith(token, StringComparison.Ordinal))
            {
                return false;
            }

            position += token.Length;
            return true;
        }

        private void Expect(string token)
        {
            if (!TryRead(token))
            {
                throw Error($"Expected '{token}'.");
            }
        }

        private void SkipWhiteSpace()
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }
        }

        private FormatException Error(string message) =>
            new($"{message} Position: {position}.");
    }
}

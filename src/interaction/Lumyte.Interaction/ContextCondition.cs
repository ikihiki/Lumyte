using System.Globalization;

namespace Lumyte.Interaction;

public abstract class ContextCondition
{
    public static ContextCondition Always { get; } = new ConstantCondition(true);

    public static ContextCondition Never { get; } = new ConstantCondition(false);

    public abstract bool Evaluate(InteractionContext context);

    public abstract string ToExpression();

    public static ContextCondition operator &(ContextCondition left, ContextCondition right) =>
        new BinaryCondition(left, "&&", right, static (leftValue, rightValue) => leftValue && rightValue);

    public static ContextCondition operator |(ContextCondition left, ContextCondition right) =>
        new BinaryCondition(left, "||", right, static (leftValue, rightValue) => leftValue || rightValue);

    public static ContextCondition operator !(ContextCondition condition) => new NotCondition(condition);

    internal static ContextCondition Equal<T>(ContextKey<T> key, T value) => new EqualCondition<T>(key, value);

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        bool boolean => boolean ? "true" : "false",
        string text => $"'{text.Replace("'", "\\'", StringComparison.Ordinal)}'",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => $"'{value}'",
    };

    private sealed class ConstantCondition(bool value) : ContextCondition
    {
        public override bool Evaluate(InteractionContext context) => value;

        public override string ToExpression() => value ? "true" : "false";
    }

    private sealed class EqualCondition<T>(ContextKey<T> key, T value) : ContextCondition
    {
        public override bool Evaluate(InteractionContext context) =>
            EqualityComparer<T>.Default.Equals(context.GetValueOrDefault(key), value);

        public override string ToExpression() => $"{key.Name} == {FormatValue(value)}";
    }

    private sealed class NotCondition(ContextCondition operand) : ContextCondition
    {
        public override bool Evaluate(InteractionContext context) => !operand.Evaluate(context);

        public override string ToExpression() => $"!({operand.ToExpression()})";
    }

    private sealed class BinaryCondition(
        ContextCondition left,
        string operation,
        ContextCondition right,
        Func<bool, bool, bool> evaluate) : ContextCondition
    {
        public override bool Evaluate(InteractionContext context) =>
            evaluate(left.Evaluate(context), right.Evaluate(context));

        public override string ToExpression() =>
            $"({left.ToExpression()}) {operation} ({right.ToExpression()})";
    }
}

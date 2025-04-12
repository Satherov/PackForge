using PackForge.Core.Terminal.Arguments;

namespace PackForge.Core.Terminal;

public static class CommandNode
{
    public static LiteralArgumentBuilder Literal(string literal)
    {
        return new LiteralArgumentBuilder(literal);
    }

    public static RequiredArgumentBuilder<T> Argument<T>(string name, ArgumentType<T> type)
    {
        return new RequiredArgumentBuilder<T>(name, type);
    }

    public static OptionalArgumentBuilder<T> OptionalArgument<T>(string name, ArgumentType<T> type)
    {
        return new OptionalArgumentBuilder<T>(name, type);
    }
}
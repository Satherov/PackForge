using PackForge.Core.Terminal.Exceptions;

namespace PackForge.Core.Terminal.Arguments;

public class StringArgument : ArgumentType<string>
{

    public override string Parse(string token)
    {
        return token;
    }

    public static StringArgument StringType()
    {
        return new StringArgument();
    }

    public static string GetString(CommandContext context, string name)
    {
        if (context.Arguments.TryGetValue(name, out object? value) && value is string s)
            return s;

        throw new InvalidArgumentException($"Argument {name} not found or not a string.");
    }
    
    public static string? GetOptionalString(CommandContext context, string name)
    {
        if (context.Arguments.TryGetValue(name, out object? value) && value is string s)
            return s;
        return null;
    }
}
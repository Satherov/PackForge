using System;

namespace PackForge.Core.Terminal.Arguments;

public class StringArgument : ArgumentType<string>
{
    private StringArgument()
    {
    }

    private static StringArgument Instance { get; } = new();

    public override string Parse(string token)
    {
        return token;
    }

    public static StringArgument StringType()
    {
        return Instance;
    }

    public static string GetString(CommandContext context, string name)
    {
        if (context.Arguments.TryGetValue(name, out object? value) && value is string s)
            return s;

        throw new Exception($"Argument {name} not found or not a string.");
    }
}
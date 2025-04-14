using System;

namespace PackForge.Core.Terminal.Arguments;

public class IntArgument(int min = int.MinValue, int max = int.MaxValue) : ArgumentType<int>
{
    private int Min { get; } = min;
    private int Max { get; } = max;

    public override int Parse(string token)
    {
        if (!int.TryParse(token, out int result)) throw new Exception("Invalid integer: " + token);

        if (result < Min || result > Max)
            throw new Exception($"Integer {result} is out of bounds [{Min}, {Max}]");
        return result;
    }

    public static IntArgument IntType()
    {
        return new IntArgument();
    }

    public static IntArgument IntType(int min, int max)
    {
        return new IntArgument(min, max);
    }

    public static int GetInt(CommandContext context, string name)
    {
        if (context.Arguments.TryGetValue(name, out object? value) && value is int i)
            return i;
        throw new Exception($"Argument {name} not found or not an int.");
    }
}
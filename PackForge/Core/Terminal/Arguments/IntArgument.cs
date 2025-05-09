﻿using PackForge.Core.Terminal.Exceptions;

namespace PackForge.Core.Terminal.Arguments;

public class IntArgument(int min = int.MinValue, int max = int.MaxValue) : ArgumentType<int>
{
    private static IntArgument Instance { get; } = new();

    private int Min { get; } = min;
    private int Max { get; } = max;

    public override int Parse(string token)
    {
        if (!int.TryParse(token, out int result)) throw new InvalidArgumentException("Invalid integer: " + token);

        if (result < Min || result > Max)
            throw new InvalidArgumentException($"Integer {result} is out of bounds [{Min}, {Max}]");
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
        throw new InvalidArgumentException($"Argument {name} not found or not an int.");
    }

    public static int? GetOptionalInt(CommandContext context, string name)
    {
        if (context.Arguments.TryGetValue(name, out object? value) && value is int i)
            return i;
        return null;
    }
}
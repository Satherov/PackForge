using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PackForge.Core.Terminal.Arguments;
using Serilog;

namespace PackForge.Core.Terminal;

public delegate Task CommandAction(CommandContext context);

public abstract class CommandNodeBase(string name, CommandAction action)
{
    public string Name { get; } = name;
    public CommandAction Action { get; } = action;
    private readonly List<CommandNodeBase> _children = [];
    public IReadOnlyList<CommandNodeBase> Children => _children;

    public void AddChild(CommandNodeBase child)
    {
        _children.Add(child);
    }

    public abstract bool Match(string token, out object? parsedValue);
}

public class LiteralCommandNode(string literal, CommandAction action) : CommandNodeBase(literal, action)
{
    public override bool Match(string token, out object? parsedValue)
    {
        parsedValue = null;
        return token.Equals(Name, StringComparison.OrdinalIgnoreCase);
    }
}

public class RequiredArgumentCommandNode<T>(string name, ArgumentType<T?> type, CommandAction action) : CommandNodeBase(name, action)
{
    private ArgumentType<T?> Type { get; } = type;

    public override bool Match(string token, out object? parsedValue)
    {
        try
        {
            parsedValue = Type.Parse(token);
            return true;
        }
        catch (Exception ex)
        {
            parsedValue = null;
            Log.Error($"Error parsing required argument '{Name}': {token} is not a valid {Type.GetType().Name}. Exception: {ex.Message}");
            return false;
        }
    }
}

public class OptionalArgumentCommandNode<T>(string name, ArgumentType<T?> type, CommandAction action) : CommandNodeBase(name, action)
{
    private ArgumentType<T?> Type { get; } = type;

    public override bool Match(string token, out object? parsedValue)
    {
        try
        {
            parsedValue = Type.Parse(token);
            return true;
        }
        catch
        {
            parsedValue = null;
            Log.Error($"Error parsing optional argument '{Name}': {token} is not a valid {Type.GetType().Name}");
            return false;
        }
    }
}
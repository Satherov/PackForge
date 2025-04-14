using System.Collections.Generic;
using PackForge.Core.Terminal.Arguments;

namespace PackForge.Core.Terminal;

public abstract class CommandBuilder
{
    private readonly List<CommandBuilder> _children = [];
    protected CommandAction? Action;

    public CommandBuilder Then(CommandBuilder child)
    {
        _children.Add(child);
        return this;
    }

    public CommandBuilder Executes(CommandAction action)
    {
        Action = action;
        return this;
    }

    public abstract CommandNodeBase Build();

    protected void AddChildren(CommandNodeBase node)
    {
        foreach (CommandBuilder? childBuilder in _children) node.AddChild(childBuilder.Build());
    }
}

public class LiteralArgumentBuilder(string literal) : CommandBuilder
{
    private string Literal { get; } = literal;

    public override CommandNodeBase Build()
    {
        LiteralCommandNode node = new(Literal, Action!);
        AddChildren(node);
        return node;
    }
}

public class RequiredArgumentBuilder<T>(string name, ArgumentType<T> type) : CommandBuilder
{
    private string ArgumentName { get; } = name;
    private ArgumentType<T> Type { get; } = type;

    public override CommandNodeBase Build()
    {
        RequiredArgumentCommandNode<T> node = new(ArgumentName, Type!, Action!);
        AddChildren(node);
        return node;
    }
}

public class OptionalArgumentBuilder<T>(string name, ArgumentType<T> type) : CommandBuilder
{
    private string ArgumentName { get; } = name;
    private ArgumentType<T> Type { get; } = type;

    public override CommandNodeBase Build()
    {
        OptionalArgumentCommandNode<T> node = new(ArgumentName, Type!, Action!);
        AddChildren(node);
        return node;
    }
}
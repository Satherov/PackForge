using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace PackForge.Core.Terminal;

public class CommandDispatcher
{
    private readonly List<CommandNodeBase> _rootNodes = [];

    public void Register(CommandBuilder builder)
    {
        CommandNodeBase? node = builder.Build();
        _rootNodes.Add(node);
    }

    public async Task Execute(string input)
    {
        string[] tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            Log.Error("Error: empty input.");
            return;
        }

        CommandContext context = new() { InputTokens = tokens };

        bool matchedRoot = false;
        foreach (CommandNodeBase root in _rootNodes)
        {
            if (!root.Match(tokens[0], out object? parsedValue))
                continue;

            matchedRoot = true;

            List<string> matchedTokens = [tokens[0]];

            if (parsedValue != null) context.Arguments[root.Name] = parsedValue;

            bool executed = await Traverse(root, tokens, 1, context, matchedTokens);
            if (executed) return;
        }

        if (!matchedRoot) Log.Error("Error: no matching root command for token: " + tokens[0]);
    }

    private static async Task<bool> Traverse(CommandNodeBase node, string[] tokens, int index, CommandContext context,
        List<string> matchedTokens)
    {
        if (index == tokens.Length)
        {
            if (node.Action != null)
            {
                await node.Action(context);
                return true;
            }

            Log.Error($"Error: Incomplete command: {string.Join(" ", matchedTokens)} <---");
            return false;
        }

        bool childMatched = false;
        foreach (CommandNodeBase? child in node.Children)
        {
            if (!child.Match(tokens[index], out object? parsedValue))
                continue;

            childMatched = true;
            List<string> newMatched = [..matchedTokens, tokens[index]];

            if (parsedValue != null)
                context.Arguments[child.Name] = parsedValue;

            bool executed = await Traverse(child, tokens, index + 1, context, newMatched);

            if (executed) return true;
        }

        if (!childMatched) Log.Error($"Error: Invalid argument: {string.Join(" ", matchedTokens)} <---");

        return false;
    }

    public List<string> GetSuggestions(string input)
    {
        string[]? tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<CommandNodeBase>? currentNodes = _rootNodes;

        for (int i = 0; i < tokens.Length; i++)
        {
            string? token = tokens[i];
            bool isPartial = i == tokens.Length - 1 && !input.EndsWith(' ');
            if (isPartial)
                return currentNodes
                    .Where(node => node.Name.StartsWith(token, StringComparison.InvariantCultureIgnoreCase))
                    .Select(FormatSuggestion)
                    .ToList();

            CommandNodeBase? match = currentNodes.FirstOrDefault(n => n.Match(token, out _));
            if (match == null) return [];
            currentNodes = match.Children.ToList();
        }

        return currentNodes.Select(FormatSuggestion).ToList();
    }

    private static string FormatSuggestion(CommandNodeBase node)
    {
        return node is LiteralCommandNode ? node.Name : $"<{node.Name}>";
    }
}
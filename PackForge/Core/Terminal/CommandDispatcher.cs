using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PackForge.Core.Terminal.Exceptions;
using Serilog;

namespace PackForge.Core.Terminal;

public class CommandDispatcher
{
    private readonly List<CommandNodeBase> _rootNodes = [];

    public void Register(CommandBuilder builder)
    {
        CommandNodeBase node = builder.Build();
        _rootNodes.Add(node);
    }

    public async Task Execute(string input)
    { 
        string[] tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            throw new InvalidCommandException("No command provided");
        }

        List<List<CommandNodeBase>> paths = [];
        foreach (CommandNodeBase root in _rootNodes)
            GatherPaths(root, [root], paths);

        List<List<CommandNodeBase>> exact = [];
        Dictionary<List<CommandNodeBase>, int> partial = new();

        foreach (List<CommandNodeBase> path in paths)
        {
            int matched = 0;
            foreach (CommandNodeBase node in path.Take(tokens.Length))
            {
                if (!node.Match(tokens[matched], out _))
                    break;
                matched++;
            }

            if (matched == path.Count && matched == tokens.Length)
                exact.Add(path);
            else if (matched > 0)
                partial[path] = matched;
        }

        switch (exact.Count)
        {
            case > 1:
                throw new InvalidCommandException($"Ambiguous command registration for \"{input}\"");
            case 1:
            {
                List<CommandNodeBase> bestPath = exact[0];
                CommandContext context = new() { InputTokens = tokens };
                for (int i = 0; i < bestPath.Count; i++)
                {
                    bestPath[i].Match(tokens[i], out object? parsed);
                    if (parsed != null)
                        context.Arguments[bestPath[i].Name] = parsed;
                }
                IEnumerable<string> displayParts = bestPath.Select(node =>
                    context.Arguments.TryGetValue(node.Name, out object? val)
                        ? $"<{node.Name}:{val}>"
                        : node.Name
                );
                Log.Debug($"Executing: {string.Join(" ", displayParts)}");

                await bestPath.Last().Action(context);
                return;
            }
        }

        if (partial.Count != 0)
        {
            int max = partial.Values.Max();
            List<List<CommandNodeBase>> runners = partial
                .Where(kv => kv.Value == max)
                .Select(kv => kv.Key)
                .ToList();
            string prefix = string.Join(" ", tokens.Take(max));

            List<List<CommandNodeBase>> endingHere = runners
                .Where(path => path.Count == max)
                .ToList();

            throw new InvalidCommandException(endingHere.Count > 1 ? $"Ambiguous command registration at \"{prefix}\"" : $"Invalid Argument: {prefix} <---");
        }


        throw new InvalidCommandException($"No matching root command for token: {tokens[0]}");
    }

    private static void GatherPaths(CommandNodeBase node, List<CommandNodeBase> soFar, List<List<CommandNodeBase>> outPaths)
    {
        outPaths.Add([..soFar]); 

        foreach (CommandNodeBase child in node.Children)
        {
            soFar.Add(child);
            GatherPaths(child, soFar, outPaths);
            soFar.RemoveAt(soFar.Count - 1);
        }
    }
}
using System.Collections.Generic;

namespace PackForge.Core.Terminal;

public class CommandContext
{
    public Dictionary<string, object?> Arguments { get; } = new();
    public required string[] InputTokens { get; set; }
}
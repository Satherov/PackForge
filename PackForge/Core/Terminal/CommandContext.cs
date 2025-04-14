using System.Collections.Generic;
using System.Threading;
using PackForge.ViewModels;

namespace PackForge.Core.Terminal;

public class CommandContext
{
    public Dictionary<string, object?> Arguments { get; } = new();
    public required string[] InputTokens { get; set; }
    public CancellationToken Token { get; set; } = MainWindowViewModel.Cts.Token;
}
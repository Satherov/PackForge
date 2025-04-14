using System.Threading.Tasks;
using PackForge.Core.Terminal.Commands;
using PackForge.Core.Util;
using Serilog;

namespace PackForge.Core.Terminal;

public static class Terminal
{
    private static readonly CommandDispatcher Dispatcher = new();

    static Terminal()
    {
        //TestCommand.Register(Dispatcher);
        //GitCommand.Register(Dispatcher);
        ChangelogCommand.Register(Dispatcher);
    }

    public static async Task RunCommand(string input)
    {
        if (Validator.IsNullOrWhiteSpace(input))
        {
            Log.Error("Input is null or empty.");
            return;
        }

        Log.Debug($"Running command: {input}");
        await Dispatcher.Execute(input);
    }
}
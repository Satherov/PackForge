using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PackForge.Core.Terminal.Commands;
using PackForge.Core.Terminal.Exceptions;
using PackForge.Core.Util;
using Serilog;
using Serilog.Events;

namespace PackForge.Core.Terminal;

public static class Terminal
{
    public static List<string> CommandHistory = [];
    private static readonly CommandDispatcher Dispatcher = new();

    static Terminal()
    {
        GitCommand.Register(Dispatcher);
        ChangelogCommand.Register(Dispatcher);
    }

    public static async Task RunCommand(string input)
    {
        Log.Information("Dispatching command: {Input}", input);
        try
        {
            CommandHistory.Add(input);
            await Dispatcher.Execute(input);
        }
        catch (InvalidCommandException ex)
        {
            Log.Error("Invalid command: {ExMessage}", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error("Unexpected error while trying to execute command: {ExMessage}", ex.Message);
        }
    }
}
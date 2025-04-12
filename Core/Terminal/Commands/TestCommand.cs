using System.Threading.Tasks;
using PackForge.Core.Terminal.Arguments;
using Serilog;

namespace PackForge.Core.Terminal.Commands;

public static class TestCommand
{
    public static void Register(CommandDispatcher dispatcher)
    {
        dispatcher.Register(
            CommandNode.Literal("example")
                .Then(
                    CommandNode.Argument("word", StringArgument.StringType())
                        .Then(CommandNode.Argument("number", IntArgument.IntType())
                            .Executes(context =>
                            {
                                string? word = StringArgument.GetString(context, "word");
                                int number = IntArgument.GetInt(context, "number");
                                Log.Information($"TestCommand executed: word = {word}, number = {number}");
                                return Task.CompletedTask;
                            })
                        )
                )
        );
    }
}
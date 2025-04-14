using PackForge.Core.Builders;
using PackForge.Core.Terminal.Arguments;

namespace PackForge.Core.Terminal.Commands;

public class ChangelogCommand
{
    public static void Register(CommandDispatcher dispatcher)
    {
        dispatcher.Register(CommandNode.Literal("changelog").Then(CommandNode.Literal("generate").Then(CommandNode.Argument("dest", StringArgument.StringType())
            .Then(CommandNode.Argument("source", StringArgument.StringType()).Then(CommandNode.Argument("version", StringArgument.StringType()).Executes(async context =>
                await ChangelogGenerator.GenerateFullChangelogAsync(StringArgument.GetString(context, "dest"), StringArgument.GetString(context, "source"),
                    StringArgument.GetString(context, "version"), null, context.Token)))).Then(CommandNode.Argument("oldVersion", StringArgument.StringType())
                .Then(CommandNode.Argument("version", StringArgument.StringType()).Executes(async context =>
                    await ChangelogGenerator.GenerateFullChangelogAsync(StringArgument.GetString(context, "dest"), null, StringArgument.GetString(context, "version"),
                        StringArgument.GetString(context, "oldVersion"), context.Token)))))));
    }
}
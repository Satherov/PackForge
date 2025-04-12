using PackForge.Core.Terminal.Arguments;

namespace PackForge.Core.Terminal.Commands;

public class GitCommand
{
    public static void Register(CommandDispatcher dispatcher)
    {
        dispatcher.Register(CommandNode.Literal("git")
            .Then(CommandNode.Literal("clone").Then(CommandNode.Argument("repository", StringArgument.StringType()).Executes(context => { return null; })))
            .Then(CommandNode.Literal("add").Then(CommandNode.Argument("path", StringArgument.StringType()).Executes(context => { return null; })))
            .Then(CommandNode.Literal("commit").Then(CommandNode.Argument("path", StringArgument.StringType()).Executes(context => { return null; }))
                .Then(CommandNode.Argument("message", StringArgument.StringType()).Executes(context => { return null; }))).Then(CommandNode.Literal("push")).Then(CommandNode
                .Literal("credentials").Then(CommandNode.Argument("path", StringArgument.StringType()).Executes(context => { return null; }))
                .Then(CommandNode.Argument("message", StringArgument.StringType()).Executes(context => { return null; }))));
    }
}
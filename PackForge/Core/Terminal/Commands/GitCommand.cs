using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using PackForge.Core.Data;
using PackForge.Core.Service;
using PackForge.Core.Terminal.Arguments;

namespace PackForge.Core.Terminal.Commands;

public class GitCommand
{
    public static void Register(CommandDispatcher dispatcher)
    {
        dispatcher.Register(CommandNode.Literal("git")
            .Then(CommandNode.Literal("clone")
                .Then(CommandNode.Argument("url", StringArgument.StringType())
                    .Executes(async context => await GitService.CloneOrUpdateRepoAsync(
                        StringArgument.GetString(context, "url"), 
                        context.Token))
                )
            )
            .Then(CommandNode.Literal("pull")
                .Then(CommandNode.OptionalArgument("url", StringArgument.StringType())
                    .Executes(async context => await GitService.UpdateRepoAsync(
                        StringArgument.GetOptionalString(context, "url") ?? string.Empty,
                        context.Token))
                )
            )
            .Then(CommandNode.Literal("status")
                .Executes(async context => await GitService.GetRepoStatusAsync(context.Token))
            )
            .Then(CommandNode.Literal("stage")
                .Then(CommandNode.Argument("path", StringArgument.StringType())
                    .Executes(async context => await GitService.StageAsync(
                        StringArgument.GetString(context, "path"), 
                        context.Token))
                )
            )
            .Then(CommandNode.Literal("commit")
                .Then(CommandNode.Argument("message", StringArgument.StringType())
                    .Executes(async context => await GitService.CommitAsync(
                        await TokenManager.RetrieveTokenValueByTypeAsync(TokenType.GitHub),
                        StringArgument.GetString(context, "message"), 
                        context.Token))
                )
            )
            .Then(CommandNode.Literal("push")
                .Then(CommandNode.Argument("url", StringArgument.StringType())
                    .Executes(async context => await GitService.PushAsync(
                        await TokenManager.RetrieveTokenValueByTypeAsync(TokenType.GitHub), 
                        StringArgument.GetString(context, "url"),
                        context.Token)
                    )
                )
            )
            .Then(CommandNode.Literal("open")
                .Executes(async context =>
                {
                    await Task.Run(() =>
                    {
                        if (!Directory.Exists(GitService.GitRepoPath))
                            Directory.CreateDirectory(GitService.GitRepoPath);

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = GitService.GitRepoPath,
                            UseShellExecute = true
                        });
                    });
                })
            )
        );
    }
}
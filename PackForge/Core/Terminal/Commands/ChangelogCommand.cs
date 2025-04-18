using System.IO;
using PackForge.Core.Builders;
using PackForge.Core.Terminal.Arguments;
using PackForge.Core.Util;
using Serilog.Events;

namespace PackForge.Core.Terminal.Commands;

public class ChangelogCommand
{
    public static void Register(CommandDispatcher dispatcher)
    {
        dispatcher.Register(
            CommandNode.Literal("changelog")
                .Then(CommandNode.Literal("generate")
                    .Then(CommandNode.Literal("normal")
                        .Then(CommandNode.Argument("dest", StringArgument.StringType())
                            .Then(CommandNode.Argument("source", StringArgument.StringType())
                                .Then(CommandNode.Argument("version", StringArgument.StringType())
                                    .Executes(async context => await ChangelogGenerator.GenerateFullChangelogAsync(
                                        StringArgument.GetString(context, "dest"), 
                                        StringArgument.GetString(context, "source"), 
                                        StringArgument.GetString(context, "version"), null, context.Token)
                                    )
                                )
                            )
                        )
                    )
                    .Then(CommandNode.Literal("versioned")
                        .Then(CommandNode.Argument("dest", StringArgument.StringType())
                            .Then(CommandNode.Argument("oldVersion", StringArgument.StringType())
                                .Then(CommandNode.Argument("version", StringArgument.StringType())
                                    .Executes(async context => await ChangelogGenerator.GenerateFullChangelogAsync(
                                        StringArgument.GetString(context, "dest"), 
                                        string.Empty, 
                                        StringArgument.GetString(context, "version"), 
                                        StringArgument.GetString(context, "oldVersion"), 
                                        context.Token)
                                    )
                                )
                            )
                        )
                    )
                )
                .Then(CommandNode.Literal("init")
                    .Then(CommandNode.Argument("source", StringArgument.StringType())
                        .Executes(async context =>
                            {
                                if(Validator.DirectoryExists(ChangelogGenerator.OldExportDir, LogEventLevel.Debug)) 
                                    Directory.Delete(ChangelogGenerator.OldExportDir, true);
                                await FileCopyHelper.CopyFilesAsync(StringArgument.GetString(context, "source"), ChangelogGenerator.OldExportDir, null, context.Token);
                            }
                        )
                    )
                )
        );
    }
}
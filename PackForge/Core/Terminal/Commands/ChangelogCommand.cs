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
                                .Then(CommandNode.Argument("name", StringArgument.StringType())
                                    .Then(CommandNode.Argument("loaderType", StringArgument.StringType())
                                        .Then(CommandNode.Argument("loaderVersion", StringArgument.StringType())
                                            .Then(CommandNode.Argument("version", StringArgument.StringType())
                                                .Executes(async context => await ChangelogGenerator.GenerateFullChangelogAsync(
                                                    StringArgument.GetString(context, "dest"), 
                                                    string.Empty, 
                                                    StringArgument.GetString(context, "name"), 
                                                    StringArgument.GetString(context, "loaderType"),
                                                    StringArgument.GetString(context, "loaderVersion"), 
                                                    StringArgument.GetString(context, "version"), 
                                                    null, 
                                                    context.Token)
                                                )
                                            )
                                        )
                                    )
                                )
                            )
                        )
                    )
                    .Then(CommandNode.Literal("versioned")
                        .Then(CommandNode.Argument("dest", StringArgument.StringType())
                            .Then(CommandNode.Argument("name", StringArgument.StringType())
                                .Then(CommandNode.Argument("loaderType", StringArgument.StringType())
                                    .Then(CommandNode.Argument("loaderVersion", StringArgument.StringType())
                                        .Then(CommandNode.Argument("oldVersion", StringArgument.StringType())
                                            .Then(CommandNode.Argument("newVersion", StringArgument.StringType())
                                                .Executes(async context => await ChangelogGenerator.GenerateFullChangelogAsync(
                                                    StringArgument.GetString(context, "dest"), 
                                                    string.Empty, 
                                                    StringArgument.GetString(context, "name"), 
                                                    StringArgument.GetString(context, "loaderType"),
                                                    StringArgument.GetString(context, "loaderVersion"), 
                                                    StringArgument.GetString(context, "newVersion"), 
                                                    StringArgument.GetString(context, "oldVersion"), 
                                                    context.Token)
                                                )
                                            )
                                        )
                                    )
                                )
                            )
                        )
                    )
                )
                .Then(CommandNode.Literal("init")
                    .Then(CommandNode.Argument("source", StringArgument.StringType())
                        .Then(CommandNode.Argument("name", StringArgument.StringType())
                            .Executes(async context =>
                                {
                                    string name = StringArgument.GetString(context, "name");
                                    string oldExport = Path.Combine(ChangelogGenerator.ChangelogPath, name, "export-old");
                                    
                                    if(Validator.DirectoryExists(oldExport, LogEventLevel.Debug)) 
                                        Directory.Delete(oldExport, true);
                                    await FileCopyHelper.CopyFilesAsync(StringArgument.GetString(context, "source"), oldExport, null, context.Token);
                                }
                            )
                        )
                    )
                )
        );
    }
}
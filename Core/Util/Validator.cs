using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Events;

// ReSharper disable ExplicitCallerInfoArgument
namespace PackForge.Core.Util;

public static partial class Validator
{
    public static bool IsTypeOf<T>([NotNullWhen(false)] object? variable, LogEventLevel? logLevel = LogEventLevel.Warning,
        [CallerArgumentExpression("variable")] string variableName = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0, [CallerMemberName] string member = "")
    {
        if (variable is T) return true;
        LogLevelMessage($"{FormatVariableName(variableName, file, line, member)} is not of type {typeof(T).Name}", logLevel);
        return false;
    }

    public static bool DirectoryEmpty([NotNullWhen(false)] string? value, LogEventLevel? logLevel = LogEventLevel.Warning,
        [CallerArgumentExpression("value")] string variableName = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0, [CallerMemberName] string member = "")
    {
        if (DirectoryExists(value, logLevel, variableName, file, line, member) && Directory.EnumerateFileSystemEntries(value).Any()) return false;

        LogLevelMessage($"{FormatVariableName(variableName, file, line, member)} is empty", logLevel);
        return true;
    }

    public static bool DirectoryExists([NotNullWhen(true)] string? value, LogEventLevel? logLevel = LogEventLevel.Warning,
        [CallerArgumentExpression("value")] string variableName = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0, [CallerMemberName] string member = "")
    {
        if (IsNullOrWhiteSpace(value, logLevel, variableName, file, line, member)) return false;
        if (Directory.Exists(value)) return true;
        LogLevelMessage($"{FormatVariableName(variableName, file, line, member)} not found at {Path.GetFullPath(value)}", logLevel);
        return false;
    }

    public static bool FileExists([NotNullWhen(true)] string? value, LogEventLevel? logLevel = LogEventLevel.Warning, [CallerArgumentExpression("value")] string variableName = "",
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0, [CallerMemberName] string member = "")
    {
        if (IsNullOrWhiteSpace(value, logLevel, variableName, file, line, member)) return false;
        if (File.Exists(value)) return true;
        LogLevelMessage($"{FormatVariableName(variableName, file, line, member)} not found at {Path.GetFullPath(value)}", logLevel);
        return false;
    }

    public static bool IsNullOrWhiteSpace([NotNullWhen(false)] string? value, LogEventLevel? logLevel = LogEventLevel.Warning,
        [CallerArgumentExpression("value")] string variableName = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0, [CallerMemberName] string member = "")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            LogLevelMessage($"{FormatVariableName(variableName, file, line, member)} is null or whitespace", logLevel);
            return true;
        }

        return false;
    }

    public static bool IsNullOrEmpty<T>([NotNullWhen(false)] T? value, LogEventLevel? logLevel = LogEventLevel.Warning,
        [CallerArgumentExpression("value")] string variableName = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0, [CallerMemberName] string member = "")
    {
        if (IsNull(value, logLevel, variableName, file, line, member))
            return true;

        switch (value)
        {
            case string str when string.IsNullOrEmpty(str):
                LogLevelMessage($"{FormatVariableName(variableName, file, line, member)} is empty", logLevel);
                return true;
            case Array arr when arr.Length == 0:
                LogLevelMessage($"{FormatVariableName(variableName, file, line, member)} is empty", logLevel);
                return true;
            case ICollection coll when coll.Count == 0:
                LogLevelMessage($"{FormatVariableName(variableName, file, line, member)} is empty", logLevel);
                return true;
        }

        return false;
    }

    public static bool IsNull<T>([NotNullWhen(false)] T? value, LogEventLevel? logLevel = LogEventLevel.Warning, [CallerArgumentExpression("value")] string variableName = "",
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0, [CallerMemberName] string member = "")
    {
        if (value is null)
        {
            LogLevelMessage($"{FormatVariableName(variableName, file, line, member)} is null", logLevel);
            return true;
        }

        return false;
    }

    private static void LogLevelMessage(string message, LogEventLevel? logLevel)
    {
        switch (logLevel)
        {
            case LogEventLevel.Error: Log.Error(message); break;
            case LogEventLevel.Warning: Log.Warning(message); break;
            case LogEventLevel.Information: Log.Information(message); break;
            case LogEventLevel.Debug: Log.Debug(message); break;
            case LogEventLevel.Verbose: Log.Verbose(message); break;
        }
    }

    private static string FormatVariableName(string? variable, string file, int line, string member)
    {
        if (string.IsNullOrWhiteSpace(variable))
            return $"'Unknown Variable' at {Path.GetFileName(file)}, {member}, line {line}";

        string? formattedName = variable.Split(['.'], StringSplitOptions.RemoveEmptyEntries).Last();
        formattedName = UpperCaseRegex().Replace(formattedName, " $1");
        return $"{char.ToUpper(formattedName[0]) + formattedName[1..]}";
    }

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex UpperCaseRegex();
}
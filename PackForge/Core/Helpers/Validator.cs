using System.IO;
using System.Runtime.CompilerServices;
using Serilog;

namespace PackForge.Core.Helpers;

public static class Validator
{
    /// <param name="value">Value to check</param>
    /// <param name="path">Optional: Additional path to display in the log</param>
    /// <param name="logLevel">Optional: The level at which to display the log entry. Default: Warning</param>
    /// <param name="variableName">Optional: Variable name to display in the log. Default: Literal name of 'value'</param>
    /// <returns>Does the directory exist?</returns>
    public static bool DirectoryEmpty(string? value, string? path = null, string logLevel = "warning", [CallerArgumentExpression("value")] string variableName = "")
    {
        if (IsNullOrWhiteSpace(value, logLevel: "none")) return true;
        if (Directory.GetFileSystemEntries(value!).Length > 0) return false;
        HandleLogLevel($"{Path.GetFileName(value) ?? GetVariableName(variableName)} not found" + (string.IsNullOrWhiteSpace(path) ? "" : $" at {path}"), logLevel);
        return true;
    }
    
    /// <param name="value">Value to check</param>
    /// <param name="path">Optional: Additional path to display in the log</param>
    /// <param name="logLevel">Optional: The level at which to display the log entry. Default: Warning</param>
    /// <param name="variableName">Optional: Variable name to display in the log. Default: Literal name of 'value'</param>
    /// <returns>Does the directory exist?</returns>
    public static bool DirectoryExists(string? value, string? path = null, string logLevel = "warning", [CallerArgumentExpression("value")] string variableName = "")
    {
        if (Directory.Exists(value)) return true;
        HandleLogLevel($"{Path.GetFileName(value) ?? GetVariableName(variableName)} not found" + (string.IsNullOrWhiteSpace(path) ? "" : $" at {path}"), logLevel);
        return false;
    }

    /// <param name="value">Value to check</param>
    /// <param name="path">Optional: Additional path to display in the log</param>
    /// <param name="logLevel">Optional: The level at which to display the log entry. Default: Warning</param>
    /// <param name="variableName">Optional: Variable name to display in the log. Default: Literal name of 'value'</param>
    /// <returns>Does the file exist?</returns>
    public static bool FileExists(string? value, string? path = null, string logLevel = "warning", [CallerArgumentExpression("value")] string variableName = "")
    {
        if (File.Exists(value)) return true;
        HandleLogLevel($"{Path.GetFileName(value) ?? GetVariableName(variableName)} not found" + (string.IsNullOrWhiteSpace(path) ? "" : $" at {path}"), logLevel);
        return false;
    }
    

    /// <param name="value">Value to check</param>
    /// <param name="message">Optional: Additional message to display in the log</param>
    /// <param name="logLevel">Optional: The level at which to display the log entry. Default: Warning</param>
    /// <param name="variableName">Optional: Variable name to display in the log. Default: Literal name of 'value'</param>
    /// <typeparam name="T">Type of the value</typeparam>
    /// <returns>Is the value null or a white space?</returns>
    public static bool IsNullOrWhiteSpace<T>(T? value, string? message = null, string logLevel = "warning", [CallerArgumentExpression("value")] string variableName = "")
    {
        if ((value is string && !string.IsNullOrWhiteSpace(value.ToString())) || (value is not string && value is not null)) return false;
        HandleLogLevel(message ?? $"{GetVariableName(variableName)} is empty" , logLevel);
        return true;
    }
    
    private static void HandleLogLevel(string message, string logLevel)
    {
        switch(logLevel.ToLowerInvariant())
        {
            case "information": Log.Information(message); break;
            case "debug": Log.Debug(message); break;
            case "warning": Log.Warning(message); break;
            case "error": Log.Error(message); break;
            case "none": break;
        };
    }
    
    private static string GetVariableName(string? variableName = null)
    {
        variableName ??= string.Empty;
        var splitIndex = -1;
        for (var i = 0; i < variableName.Length; i++)
        {
            if (!char.IsUpper(variableName[i]) && variableName[i] != '_') continue;
            splitIndex = i;
            break;
        }

        if (splitIndex <= 0) return variableName;
        
        var firstPart = variableName[..splitIndex];
        firstPart = char.ToUpper(firstPart[0]) + firstPart[1..];
        var secondPart = variableName[splitIndex..];
        return $"{firstPart} {secondPart}";

    }
}

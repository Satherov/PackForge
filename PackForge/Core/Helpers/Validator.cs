using System.IO;
using System.Runtime.CompilerServices;
using Serilog;

namespace PackForge.Core.Helpers;

public static class Validator
{
    public static bool CheckDirectoryExists(string? value, string? path = null, string logLevel = "warning", [CallerArgumentExpression("value")] string variableName = "")
    {
        if (Directory.Exists(value)) return false;
        HandleLogLevel($"{GetVariableName(variableName)} not found" + (string.IsNullOrWhiteSpace(path) ? "" : $" at {path}"), logLevel);
        return true;
    }
    
    public static bool CheckFileExists(string? value, string? path = null, string logLevel = "warning", [CallerArgumentExpression("value")] string variableName = "")
    {
        if (File.Exists(value)) return false;
        HandleLogLevel($"{GetVariableName(variableName)} not found" + (string.IsNullOrWhiteSpace(path) ? "" : $" at {path}"), logLevel);
        return true;
    }
    
    public static bool CheckNullOrWhiteSpace<T>(T? value, string? message = null, string logLevel = "warning", [CallerArgumentExpression("value")] string variableName = "")
    {
        if(value is not null) return false;
        if (value is string && !string.IsNullOrWhiteSpace(value.ToString())) return false;
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
        };
    }
    
    private static string GetVariableName(string variableName)
    {
        var splitIndex = -1;
        for (var i = 0; i < variableName.Length; i++)
        {
            if (!char.IsUpper(variableName[i]) && variableName[i] != '_') continue;
            splitIndex = i;
            break;
        }

        var firstPart = variableName[..splitIndex];
        var secondPart = variableName[splitIndex..];
        
        return splitIndex == -1 ? variableName : $"{firstPart} {secondPart}";
    }
}

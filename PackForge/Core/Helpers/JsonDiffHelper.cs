using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PackForge.Core.Builders;
using Serilog;

namespace PackForge.Core.Helpers;
public static class JsonDiffHelper
{
    /// <summary>
    /// Asynchronously generates a list of diff lines between two Json files.
    /// </summary>
    public static async Task<List<string>> GenerateDiffAsync(string oldJsonPath, string newJsonPath)
    {
        var texts = await Task.WhenAll(
            File.Exists(oldJsonPath) ? File.ReadAllTextAsync(oldJsonPath) : Task.FromResult(string.Empty),
            File.Exists(newJsonPath) ? File.ReadAllTextAsync(newJsonPath) : Task.FromResult(string.Empty)
        );
        var oldText = texts[0];
        var newText = texts[1];

        // If both files are empty, return no diff.
        if (string.IsNullOrWhiteSpace(oldText) && string.IsNullOrWhiteSpace(newText))
            return [];

        var oldNode = !string.IsNullOrWhiteSpace(oldText) ? JsonNode.Parse(oldText) : null;
        var newNode = !string.IsNullOrWhiteSpace(newText) ? JsonNode.Parse(newText) : null;

        // Quickly compare normalized hashes for entire Json.
        var hashOld = ComputeNormalizedJsonHash(oldNode);
        var hashNew = ComputeNormalizedJsonHash(newNode);
        if (hashOld == hashNew) return [];

        var lines = new List<string>();
        DiffNodes(oldNode, newNode, lines, 0, null);
        return lines;
    }

    private static string ComputeNormalizedJsonHash(JsonNode? node)
    {
        switch (node)
        {
            case JsonValue val: return val.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            case JsonObject obj:
            {
                var sb = new StringBuilder();
                foreach (var kvp in obj.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    sb.Append(kvp.Key);
                    sb.Append(ComputeNormalizedJsonHash(kvp.Value));
                }
                return sb.ToString();
            }
            case JsonArray arr:
            {
                var hashes = arr.Select(ComputeNormalizedJsonHash).ToList();
                hashes.Sort(StringComparer.Ordinal);
                return string.Join("", hashes);
            }
            default: return string.Empty;
        }
    }

    private static void DiffNodes(
        JsonNode? oldNode,
        JsonNode? newNode,
        List<string> lines,
        int indentLevel,
        string? propertyName)
    {
        if (oldNode == null && newNode == null) return;

        if (oldNode == null && newNode != null)
        {
            PrintNode(newNode, lines, indentLevel, '+', propertyName);
            return;
        }
        if (oldNode != null && newNode == null)
        {
            PrintNode(oldNode, lines, indentLevel, '-', propertyName);
            return;
        }

        var oldKind = GetNodeKind(oldNode);
        var newKind = GetNodeKind(newNode);
        if (oldKind != newKind)
        {
            PrintNode(oldNode, lines, indentLevel, '-', propertyName);
            PrintNode(newNode, lines, indentLevel, '+', propertyName);
            return;
        }

        switch (oldNode)
        {
            case JsonObject oldObj when newNode is JsonObject newObj:
                lines.Add(propertyName != null
                    ? FormatLine(null, indentLevel, $"\"{propertyName}\": {{")
                    : FormatLine(null, indentLevel, "{"));

                var allProps = oldObj.Select(kvp => kvp.Key)
                    .Union(newObj.Select(kvp => kvp.Key))
                    .ToList();

                foreach (var prop in allProps)
                {
                    oldObj.TryGetPropertyValue(prop, out var oldPropVal);
                    newObj.TryGetPropertyValue(prop, out var newPropVal);
                    DiffNodes(oldPropVal, newPropVal, lines, indentLevel + 2, prop);
                }

                lines.Add(FormatLine(null, indentLevel, "}"));
                return;

            case JsonArray oldArr when newNode is JsonArray newArr:
                lines.Add(propertyName != null
                    ? FormatLine(null, indentLevel, $"\"{propertyName}\": [")
                    : FormatLine(null, indentLevel, "["));
                DiffArraysByPrefixSuffix(oldArr, newArr, lines, indentLevel + 2);
                lines.Add(FormatLine(null, indentLevel, "]"));
                return;

            case JsonValue oldVal when newNode is JsonValue newVal:
                // Run StripLambdaSuffix before comparing the Json values.
                var oldStr = StripLambdaSuffix(JsonValueToString(oldVal));
                var newStr = StripLambdaSuffix(JsonValueToString(newVal));
                if (oldStr == newStr)
                {
                    lines.Add(propertyName != null
                        ? FormatLine(null, indentLevel, $"\"{propertyName}\": {oldStr}")
                        : FormatLine(null, indentLevel, oldStr));
                }
                else
                {
                    if (propertyName != null)
                    {
                        lines.Add(FormatLine('-', indentLevel, $"\"{propertyName}\": {oldStr}"));
                        lines.Add(FormatLine('+', indentLevel, $"\"{propertyName}\": {newStr}"));
                    }
                    else
                    {
                        lines.Add(FormatLine('-', indentLevel, oldStr));
                        lines.Add(FormatLine('+', indentLevel, newStr));
                    }
                }
                return;

            default:
                PrintNode(oldNode, lines, indentLevel, '-', propertyName);
                PrintNode(newNode, lines, indentLevel, '+', propertyName);
                break;
        }
    }

    /// <summary>
    /// Matches common prefixes/suffixes of the arrays to avoid marking unchanged items as removed+added.
    /// Anything in the middle is considered removed from the old array and then added in the new array.
    /// </summary>
    private static void DiffArraysByPrefixSuffix(JsonArray oldArr, JsonArray newArr, List<string> lines, int indentLevel)
    {
        var prefixLen = 0;
        var minLen = Math.Min(oldArr.Count, newArr.Count);
        var oldHashes = oldArr.Select(ComputeNormalizedJsonHash).ToArray();
        var newHashes = newArr.Select(ComputeNormalizedJsonHash).ToArray();
        while (prefixLen < minLen && oldHashes[prefixLen] == newHashes[prefixLen])
        {
            DiffNodes(oldArr[prefixLen], newArr[prefixLen], lines, indentLevel, null);
            prefixLen++;
        }

        var suffixLen = 0;
        while (suffixLen < (minLen - prefixLen) &&
               ComputeNormalizedJsonHash(oldArr[^(suffixLen + 1)]) == ComputeNormalizedJsonHash(newArr[^(suffixLen + 1)]))
        {
            suffixLen++;
        }

        var oldMidStart = prefixLen;
        var oldMidEnd = oldArr.Count - suffixLen - 1;
        var newMidStart = prefixLen;
        var newMidEnd = newArr.Count - suffixLen - 1;

        for (var i = oldMidStart; i <= oldMidEnd; i++)
        {
            DiffNodes(oldArr[i], null, lines, indentLevel, null);
        }
        for (var i = newMidStart; i <= newMidEnd; i++)
        {
            DiffNodes(null, newArr[i], lines, indentLevel, null);
        }
        for (var i = suffixLen - 1; i >= 0; i--)
        {
            var oldIndex = oldArr.Count - 1 - i;
            var newIndex = newArr.Count - 1 - i;
            DiffNodes(oldArr[oldIndex], newArr[newIndex], lines, indentLevel, null);
        }
    }

    private static void PrintNode(
        JsonNode node,
        List<string> lines,
        int indentLevel,
        char prefix,
        string? propertyName)
    {
        var multiline = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var splitted = multiline.Split('\n');

        if (propertyName != null && node is JsonObject or JsonArray)
        {
            var bracketIndex = splitted[0].IndexOfAny(['{', '[']);
            if (bracketIndex >= 0)
            {
                var openBracket = splitted[0].Substring(bracketIndex, 1);
                lines.Add(FormatLine(prefix, indentLevel, $"\"{propertyName}\": {openBracket}"));
                for (var i = 1; i < splitted.Length; i++)
                {
                    lines.Add(FormatLine(prefix, indentLevel, splitted[i]));
                }
                return;
            }
        }
        foreach (var rawLine in splitted)
        {
            var line = rawLine.TrimEnd('\r');
            if (propertyName != null && node is not (JsonObject or JsonArray))
            {
                if (line.StartsWith('{') || line.StartsWith('['))
                    lines.Add(FormatLine(prefix, indentLevel, $"\"{propertyName}\": {line}"));
                else if (line.StartsWith('}') || line.StartsWith(']'))
                    lines.Add(FormatLine(prefix, indentLevel, line));
                else
                    lines.Add(FormatLine(prefix, indentLevel, $"\"{propertyName}\": {line}"));
            }
            else
            {
                lines.Add(FormatLine(prefix, indentLevel, line));
            }
        }
    }
    
    /// <summary>
    /// Preserves original ordering while merging lines that start with '-' and '+'
    /// if they have identical content. The merged line has no prefix (just a leading space).
    /// If multiple matches exist, lines are paired in first-come-first-served order.
    /// </summary>
    public static List<string> CombinePairs(List<string> lines)
    {
        if (Validator.IsNullOrWhiteSpace(lines, logLevel: "debug")) return [];
        
        string?[] finalLines = lines.ToArray();
        
        var leftoverMinus = new Dictionary<string, Queue<int>>();
        var leftoverPlus  = new Dictionary<string, Queue<int>>();

        for (var i = 0; i < finalLines.Length; i++)
        {
            var line = finalLines[i];

            if (line!.StartsWith('-'))
            {
                var content = line[1..];
                if (leftoverPlus.TryGetValue(content, out var plusQueue) && plusQueue.Count > 0)
                {
                    var j = plusQueue.Dequeue();
                    // Unify them at whichever index is earlier
                    if (j < i)
                    {
                        // The plus line came first, so unify there
                        finalLines[j] = " " + content.TrimStart();
                        finalLines[i] = null; // remove the minus line
                    }
                    else
                    {
                        // The minus line came first, unify here
                        finalLines[i] = " " + content.TrimStart();
                        finalLines[j] = null; // remove the plus line
                    }
                }
                else
                {
                    if (!leftoverMinus.TryGetValue(content, out var queue))
                    {
                        queue = new Queue<int>();
                        leftoverMinus[content] = queue;
                    }
                    queue.Enqueue(i);
                }
            }
            else if (line.StartsWith('+'))
            {
                var content = line[1..];
                if (leftoverMinus.TryGetValue(content, out var minusQueue) && minusQueue.Count > 0)
                {
                    var j = minusQueue.Dequeue();
                    if (j < i)
                    {
                        finalLines[j] = " " + content.TrimStart();
                        finalLines[i] = null;
                    }
                    else
                    {
                        finalLines[i] = " " + content.TrimStart();
                        finalLines[j] = null;
                    }
                }
                else
                {
                    if (!leftoverPlus.TryGetValue(content, out var queue))
                    {
                        queue = new Queue<int>();
                        leftoverPlus[content] = queue;
                    }
                    queue.Enqueue(i);
                }
            }
        }
        
        return finalLines.ToList()!;
    }
    
    public static DiffCategory DetectCategory(string relativePath)
    {
        var directory = Path.GetDirectoryName(relativePath) ?? string.Empty;
        var path = directory.ToLowerInvariant();

        if (path.Contains("registry") || path.Contains("registries")) return DiffCategory.Registries;
        if (path.Contains("recipe") || path.Contains("recipes")) return DiffCategory.Recipes;
        if (path.Contains("tag") || path.Contains("tags")) return DiffCategory.Tags;
        if (path.Contains("loot_table") || path.Contains("loot_tables")) return DiffCategory.LootTable;
        
        return DiffCategory.Unknown;
    }
    
    /// <summary>
    /// Compares the contents of two jsons against each other
    /// </summary>
    /// <param name="oldPath">old Path to compare against</param>
    /// <param name="newPath">new Path use as comparison</param>
    /// <param name="ct">CancellationToken to use if the User kills the Task</param>
    /// <returns>True if they are the same</returns>
    public static bool CompareJsons(string oldPath, string newPath)
    {
        if (!Validator.FileExists(oldPath, logLevel: "debug") || 
            !Validator.FileExists(newPath, logLevel: "debug")) return false;

        try
        {
            var readOld = File.ReadAllText(oldPath);
            var readNew = File.ReadAllText(newPath);

            var json1 = JsonNode.Parse(readOld);
            var json2 = JsonNode.Parse(readNew);

            if (json1 is null || json2 is null) return false;

            var options = new JsonSerializerOptions { WriteIndented = false };
            return json1.ToJsonString(options) == json2.ToJsonString(options);
        }
        catch // If anything goes wrong assume they are not the same
        {
            return false;
        }
    }

    /// <summary>
    /// Recursively sorts the keys of objects and the elements of arrays in a Json node
    /// </summary>
    /// <param name="node">The Json node to normalize</param>
    private static void NormalizeJsonNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var list = obj.ToList().OrderBy(x => x.Key, StringComparer.Ordinal);
                
                foreach (var (key, value) in list)
                {
                    NormalizeJsonNode(value);
                    obj[key] = value;
                }

                break;
            }
            case JsonArray arr:
            {
                foreach (var t in arr)
                {
                    NormalizeJsonNode(t);
                }
                
                var sorted = arr
                    .OrderBy(e => e?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? string.Empty, StringComparer.Ordinal)
                    .ToList();

                for (var i = 0; i < arr.Count; i++)
                {
                    arr[i] = sorted[i];
                }

                break;
            }

        }
    }

    /// <summary>
    /// Returns the type, of a given Json Node
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    private static string GetNodeKind(JsonNode? node) => node switch 
    {
        JsonObject => "object",
        JsonArray => "array",
        _ => "value"
    };
    
    /// <summary>
    /// Formats a line with an optional prefix, indentation level, and content to ensure consistent spacing.
    /// </summary>
    /// <param name="prefix">Either a "+" or "-". If null reduce the indent by 1 to ensure correct spacing</param>
    /// <param name="indentLevel">Number of indents for the line</param>
    /// <param name="content">Contents of the line to append</param>
    /// <returns>Formatted line with prefix applied</returns>
    private static string FormatLine(char? prefix, int indentLevel, string content)
    {
        if (prefix != null)
        {
            indentLevel--;
        }
        return $"{prefix}{new string(' ', indentLevel)}{content}";
    }

    /// <summary>
    /// Turns a Json value into a string, removing any indentation.
    /// </summary>
    /// <param name="value"></param>
    /// <returns>Converted string</returns>
    private static string JsonValueToString(JsonValue value)
    {
        if (Validator.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.ToJsonString(new JsonSerializerOptions { WriteIndented = false }).Trim();
        return text;
    }
    
    /// <summary>
    /// Removes the Lambda suffix from a given entry
    /// </summary>
    /// <param name="line">Line to remove suffix from</param>
    /// <returns>Line without suffix</returns>
    private static string StripLambdaSuffix(string line)
    {
        const string lambdaMarker = "$$Lambda";
        var idx = line.IndexOf(lambdaMarker, StringComparison.Ordinal);
        return idx >= 0 ? line.AsSpan(0, idx).ToString() : line;
    }

    
    /// <summary>
    /// Possible Categories for each Json, used for display in the Changelog
    /// </summary>
    public enum DiffCategory
    {
        Unknown,
        Recipes,
        Tags,
        Registries,
        LootTable
    }
}


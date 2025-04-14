using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using PackForge.Core.Util;
using Serilog;
using Serilog.Events;
using static System.Security.Cryptography.ProtectedData;

namespace PackForge.Core.Data;

public enum TokenType
{
    GitHub,
    Curseforge,
    None
}

public record Token(TokenType Type, string? Value)
{
    public TokenType Type { get; set; } = Type;
    public string Value { get; set; } = Value ?? string.Empty;
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);
    public static Token Empty => new(TokenType.None, string.Empty);

    public override string ToString()
    {
        return $"{Type}";
    }
}

public class TokenStore
{
    private readonly List<Token> _tokens = [];

    public bool IsEmpty => _tokens.Count <= 0;

    public IReadOnlyList<Token> GetAllTokens()
    {
        return _tokens.AsReadOnly();
    }

    public bool TryGetTokenByType(TokenType type, out Token token)
    {
        token = _tokens.FirstOrDefault(t => t.Type == type) ?? Token.Empty;
        if (!token.IsEmpty)
            return true;

        Log.Debug($"{type} token not found in memory");
        return false;
    }


    public void UpsertToken(Token token)
    {
        int index = _tokens.FindIndex(t => t.Type == token.Type);
        if (index >= 0)
            _tokens[index] = token;
        else
            _tokens.Add(token);
    }

    public void RemoveToken(TokenType type)
    {
        Token? token = _tokens.FirstOrDefault(t => t.Type == type);
        if (token != null)
            _tokens.Remove(token);
        else
            Log.Warning($"No token of type {type} found to remove");
    }
}

internal static class TokenManager
{
    private static readonly TokenStore TokenStore = new();
    private static readonly IDataProtector? DataProtector = DataProtectionProvider.Create("TokenManager").CreateProtector("TokenProtector");
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static readonly string TokensDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PackForge", "data", "tokens");

    static TokenManager()
    {
        if (!Directory.Exists(TokensDirectory))
            Directory.CreateDirectory(TokensDirectory);
    }

    private static string GetTokenFilePath(TokenType tokenType)
    {
        return Path.Combine(TokensDirectory, $"{tokenType}.token");
    }

    public static bool IsTokenStored(TokenType type)
    {
        bool inMemory = TokenStore.TryGetTokenByType(type, out _);
        bool onDisk = Validator.FileExists(GetTokenFilePath(type), LogEventLevel.Debug) && new FileInfo(GetTokenFilePath(type)).Length > 0;
        return inMemory || onDisk;
    }

    public static async Task StoreTokenAsync(TokenType type, string? value)
    {
        string filePath = GetTokenFilePath(type);
        if (Validator.FileExists(filePath, LogEventLevel.Debug))
            File.Delete(filePath);

        Log.Debug($"Storing '{type}' token");

        byte[] encryptedTokenBytes = EncryptToken(new Token(type, value));
        TokenStore.UpsertToken(new Token(type, Convert.ToBase64String(encryptedTokenBytes)));

        try
        {
            await File.WriteAllBytesAsync(filePath, encryptedTokenBytes);
        }
        catch (Exception e)
        {
            Log.Error($"Error writing token file: {e.Message}");
        }
    }

    public static async Task<string> RetrieveTokenValueByTypeAsync(TokenType tokenType)
    {
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Log.Debug($"Retrieving token of type '{tokenType}'");

            string memoryToken = string.Empty;
            if (TokenStore.TryGetTokenByType(tokenType, out Token tokenFromMemory) && !tokenFromMemory.IsEmpty)
                memoryToken = tokenFromMemory.Value;

            string filePath = GetTokenFilePath(tokenType);
            if (!Validator.FileExists(filePath, LogEventLevel.Debug))
                return Token.Empty.Value;

            byte[] diskTokenBytes = await File.ReadAllBytesAsync(filePath);
            byte[] encryptedTokenBytes = !string.IsNullOrWhiteSpace(memoryToken) ? Convert.FromBase64String(memoryToken) : diskTokenBytes;
            string decrypted = DecryptToken(encryptedTokenBytes, tokenType);

            Log.Debug($"Token retrieval completed in {stopwatch.ElapsedMilliseconds}ms");
            return decrypted;
        }
        catch (Exception e)
        {
            Log.Error($"Error retrieving token: {e.Message}");
            return Token.Empty.Value;
        }
    }

    private static byte[] EncryptToken(Token token)
    {
        Log.Information($"Encrypting token '{token.Type}'");
        try
        {
            if (!IsWindows)
            {
                string protectedText = DataProtector!.Protect(token.Value);
                return Encoding.UTF8.GetBytes(protectedText);
            }

            Log.Debug("Using DPAPI to encrypt token");
            byte[] tokenBytes = Encoding.UTF8.GetBytes(token.Value);
#pragma warning disable CA1416
            byte[] encryptedBytes = Protect(tokenBytes, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
            Log.Information("Token encrypted successfully");
            return encryptedBytes;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to encrypt token: {e.Message}");
            return [];
        }
    }

    private static string DecryptToken(byte[] encryptedToken, TokenType tokenType)
    {
        Log.Debug($"Decrypting token '{tokenType}'");
        try
        {
            if (!IsWindows)
            {
                string encryptedText = Encoding.UTF8.GetString(encryptedToken);
                return DataProtector!.Unprotect(encryptedText);
            }

            Log.Debug("Using DPAPI to decrypt token");
#pragma warning disable CA1416
            byte[] decryptedBytes = Unprotect(encryptedToken, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
            Log.Debug("Token decrypted successfully");
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to decrypt token: {e.Message}");
            return string.Empty;
        }
    }
}
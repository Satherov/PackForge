using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using PackForge.Core.Helpers;
using Serilog;
using static System.Security.Cryptography.ProtectedData;

namespace PackForge.Core.Data;

internal static class TokenManager
{
    private static readonly ConcurrentDictionary<string, string> TokenStore = new();
    private static readonly IDataProtector? DataProtector;
    private static readonly bool IsWindows;

    private static readonly string TokensDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "PackForge", "data", "tokens");

    static TokenManager()
    {
        IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        
        // If we're not on Windows, we need to create a data
        // protector since Windows DPAPI isn't available there
        if (!IsWindows)
        {
            var provider = DataProtectionProvider.Create("TokenManager");
            DataProtector = provider.CreateProtector("TokenProtector");
        }

        if (!Directory.Exists(TokensDirectory)) Directory.CreateDirectory(TokensDirectory);
    }

    /// <summary>
    /// Retrieves a token from memory or disk.
    /// Attempts to decrypt the token from memory first, if not available tries reading it from disk.
    /// </summary>
    /// <param name="tokenName">Name of the token to retrieve</param>
    /// <returns></returns>
    public static async Task<string?> RetrieveTokenAsync(string tokenName)
    {
        try
        {
            Log.Debug($"Retrieving {tokenName} token");
            
            // Check if the token is stored in memory
            if (TokenStore.TryGetValue(tokenName, out var encryptedToken))
                return DecryptToken(encryptedToken);

            // Otherwise, try reading it from disk
            var filePath = GetTokenFilePath(tokenName);
            if(!Validator.FileExists(filePath, logLevel: "debug")) return string.Empty;
            
            encryptedToken = await File.ReadAllTextAsync(filePath);
            TokenStore[tokenName] = encryptedToken;
            return DecryptToken(encryptedToken);
        }
        catch (Exception e)
        {
            Log.Error($"Error reading token file: {e.Message}");
            return string.Empty;
        }
        
    }

    /// <summary>
    /// Attempts to store a token in memory and on disk.
    /// Encrypts the token before storing it using either Windows DPAPI or a data protector.
    /// </summary>
    /// <param name="tokenName">Name of the Token to store</param>
    /// <param name="token">Value of the Token</param>
    public static async Task StoreTokenAsync(string tokenName, string? token)
    {
        if (Validator.FileExists(GetTokenFilePath(tokenName))) File.Delete(GetTokenFilePath(tokenName));
        
        Log.Debug($"Storing {tokenName} token");

        // Encrypt the token and store it in memory
        var encryptedToken = EncryptToken(token!);
        TokenStore[tokenName] = encryptedToken;

        // Write the encrypted token to disk
        var filePath = GetTokenFilePath(tokenName);
        if(Validator.FileExists(filePath)) File.Delete(filePath);
        try
        {
            await File.WriteAllTextAsync(filePath, encryptedToken);
        }
        catch (Exception e)
        {
            Log.Error($"Error writing token file: {e.Message}");
        }
    }

    /// <summary>
    /// Encrypts the token using either Windows DPAPI or data protector.
    /// </summary>
    /// <param name="token">Token value to encrypt</param>
    /// <returns></returns>
    private static string EncryptToken(string token)
    {
        Log.Information($"Encrypting token");

        try
        {
            // If we're not on Windows, use the data protector
            if (!IsWindows) return DataProtector!.Protect(token);
            
            // Otherwise, use Windows DPAPI
            Log.Debug("Using DPAPI to encrypt token");
            var tokenBytes = Encoding.UTF8.GetBytes(token);
#pragma warning disable CA1416
            var encryptedBytes = Protect(tokenBytes, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
            Log.Information($"Token encrypted successfully");
            return Convert.ToBase64String(encryptedBytes);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to encrypt token: {e.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Decrypts the token using either Windows DPAPI or data protector.
    /// </summary>
    /// <param name="encryptedToken">The encrypted token value to decrypt</param>
    /// <returns></returns>
    private static string DecryptToken(string encryptedToken)
    {
        Log.Information($"Decrypting token");

        try
        {
            // If we're not on Windows, use the data protector
            if (!IsWindows) return DataProtector!.Unprotect(encryptedToken);
            
            // Otherwise, use Windows DPAPI
            Log.Debug("Using DPAPI to decrypt token");
            var encryptedBytes = Convert.FromBase64String(encryptedToken);
#pragma warning disable CA1416
            var decryptedBytes = Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
            Log.Information($"Token decrypted successfully");
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to decrypt token: {e.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Returns the file path for the given token name.
    /// </summary>
    /// <param name="tokenName">Token name to return return path for</param>
    /// <returns></returns>
    private static string GetTokenFilePath(string tokenName)
    {
        return Path.Combine(TokensDirectory, $"{tokenName}.token");
    }
}
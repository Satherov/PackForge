using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using PackForge.Core.Data;
using Serilog;
using SevenZip;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace PackForge.Core.Util;

public record Rule(string FilePath, string Type, bool Whitelist)
{
    public override string ToString()
    {
        return Type == "directory" ? $"Folder: {FilePath} Whitelist: {Whitelist}" : $"File: {FilePath}.{Type} Whitelist: {Whitelist}";
    }

    public static Rule None => new("*", "*", false);
    public static Rule All => new("*", "*", true);
}

public static class FileUtil
{
    public static async Task<string> PrepareRootFolderAsync(string? destinationFolder, string? folderName, string? version)
    {
        if (Validator.IsNullOrWhiteSpace(destinationFolder) || Validator.IsNullOrWhiteSpace(folderName) || Validator.IsNullOrWhiteSpace(version)) return string.Empty;

        try
        {
            string rootFolder = Path.Combine(destinationFolder, folderName, version);
            Directory.CreateDirectory(rootFolder);
            await CleanPermissionsAsync(rootFolder);
            return rootFolder;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static async Task CleanUpEmptyFoldersAsync(string rootPath)
    {
        if (!Validator.DirectoryExists(rootPath)) return;

        string overwritesFolderPath = Path.Combine(rootPath, "overrides");
        if (Validator.DirectoryExists(overwritesFolderPath, null))
            Directory.Delete(overwritesFolderPath, true);

        Directory.CreateDirectory(overwritesFolderPath);
        await CleanPermissionsAsync(overwritesFolderPath);

        foreach (string directory in Directory.GetDirectories(rootPath))
        {
            string dirName = Path.GetFileName(directory);
            if (dirName == "overrides") continue;

            if (Validator.DirectoryEmpty(directory, null))
                Directory.Delete(directory, true);
            else
                Directory.Move(directory, Path.Combine(overwritesFolderPath, dirName));
        }
    }

    public static async Task<string?> OpenFolderAsync(string? startLocation = null, Window? focusWindow = null)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktopLifetime)
            return string.Empty;

        try
        {
            Window? window = desktopLifetime.MainWindow;
            if (window == null) throw new SystemException();
            IReadOnlyList<IStorageFolder> folderResult = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select a folder",
                SuggestedStartLocation = startLocation != null ? await window.StorageProvider.TryGetFolderFromPathAsync(startLocation) : null
            });
            if (focusWindow != null) await WindowHelper.FocusWindow(() => focusWindow);
            return !folderResult.Any() ? string.Empty : folderResult.Single().Path.LocalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static async Task ZipFolderAsync(string targetPath, string zipPath, CancellationToken ct = default)
    {
        try
        {
            ProcessStartInfo processStartInfo = new()
            {
                FileName = "7z",
                Arguments = $"a -r -mx1 \"{zipPath}\" \"{targetPath}\\*\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            await Task.Run(() =>
            {
                using Process? process = Process.Start(processStartInfo);
                process?.WaitForExit();
            }, ct);
            return;
        }
        catch
        {
            // ignored
        }

        try
        {
            if (File.Exists(zipPath))
            {
                await CleanPermissionsAsync(zipPath);
                File.Delete(zipPath);
            }

            await Task.Run(() =>
            {
                SevenZipBase.SetLibraryPath("Libs/7z.dll");
                SevenZipCompressor compressor = new()
                {
                    CompressionLevel = SevenZip.CompressionLevel.Fast,
                    CompressionMethod = CompressionMethod.Lzma,
                    CompressionMode = SevenZip.CompressionMode.Create,
                    IncludeEmptyDirectories = false
                };
                compressor.CompressDirectory(targetPath, zipPath);
            }, ct);
            return;
        }
        catch
        {
            // ignored
        }

        try
        {
            if (File.Exists(zipPath))
            {
                await CleanPermissionsAsync(zipPath);
                File.Delete(zipPath);
            }

            await Task.Run(() => ZipFile.CreateFromDirectory(targetPath, zipPath, CompressionLevel.Fastest, false), ct);
        }
        catch
        {
            // ignored
        }
    }

    private static async Task CleanPermissionsAsync(string rootFolder, bool recursive = false)
    {
        DirectoryInfo directoryInfo = new(rootFolder);
        directoryInfo.Attributes &= ~FileAttributes.ReadOnly;

        await Task.Run(() =>
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                DirectorySecurity security = directoryInfo.GetAccessControl();
                security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().Name, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                directoryInfo.SetAccessControl(security);
            }
            else
            {
                Process.Start("chmod", $"777 \"{rootFolder}\"");
            }
        });

        if (!recursive) return;

        foreach (FileInfo file in directoryInfo.GetFiles())
        {
            file.Attributes &= ~FileAttributes.ReadOnly;
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    FileSecurity fileSecurity = file.GetAccessControl();
                    fileSecurity.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().Name, FileSystemRights.FullControl, AccessControlType.Allow));
                    file.SetAccessControl(fileSecurity);
                }
                else
                {
                    Process.Start("chmod", $"777 \"{file.FullName}\"");
                }
            }
            catch
            {
                // ignored
            }
        }

        foreach (DirectoryInfo subDir in directoryInfo.GetDirectories())
            await CleanPermissionsAsync(subDir.FullName, true);
    }

    #region Filters

    public static async Task ApplyFilters(string? sourceDir, bool client, CancellationToken ct = default)
    {
        if (!Validator.DirectoryExists(sourceDir)) return;

        Stopwatch stopwatch = Stopwatch.StartNew();
        Log.Information("Applying filters to {SourceDir}", sourceDir);

        List<ModInfo> allData = await JarUtil.GetJarInfoInDirectoryAsync(sourceDir, ct);
        foreach (ModInfo mod in allData)
        {
            if (Validator.IsNullOrEmpty(mod.Metadata, null) && mod.NestedJars.Count > 0)
            {
                Log.Debug("Mod {GetFileName} only contains JarInJars", Path.GetFileName(mod.FilePath));
                foreach (ModInfo jarMod in mod.NestedJars) LogFilterResults(jarMod, mod, client, true);
                continue;
            }

            if (string.IsNullOrWhiteSpace(mod.Metadata.ModId)) continue;

            Log.Debug("{ModInfo}", mod);
            LogFilterResults(mod, mod, client);
        }

        Log.Information("Finished applying filters after {StopwatchElapsedMilliseconds}ms", stopwatch.ElapsedMilliseconds);
    }

    private static void LogFilterResults(ModInfo mod, ModInfo parent, bool client, bool jarInJar = false)
    {
        string fileName = Path.GetFileName(parent.FilePath);
        string? prefix = jarInJar ? $"JarInJar '{mod.Metadata.DisplayName}' found within " : null;
        if (string.IsNullOrEmpty(prefix)) fileName = prefix + fileName;
            

        if (DataManager.ExcludedCommon.Contains(mod.Metadata.ModId))
        {
            Log.Warning("{GetFileName} matching {MetadataModId} is excluded. Deleting...", fileName, mod.Metadata.ModId);
            File.Delete(parent.FilePath);
        }

        foreach (string dependency in mod.Metadata.Dependencies.Where(dependency => DataManager.ExcludedCommon.Contains(dependency)))
            Log.Warning("{GetFileName} depends on an excluded mod {Dependency}", fileName, dependency);

        switch (client)
        {
            case true when DataManager.ExcludedClient.Contains(mod.Metadata.ModId):
                Log.Warning("{GetFileName} containing {MetadataModId} is excluded on the client. Deleting...", fileName, mod.Metadata.ModId);
                File.Delete(parent.FilePath);

                foreach (string dependency in mod.Metadata.Dependencies.Where(dependency => DataManager.ExcludedClient.Contains(dependency)))
                    Log.Warning("'{GetFileName}' depends on an excluded mod {Dependency}", fileName, dependency);
                break;

            case false when DataManager.ExcludedServer.Contains(mod.Metadata.ModId):
                Log.Warning("{GetFileName} containing {MetadataModId} is excluded on the server. Deleting...", fileName, mod.Metadata.ModId);
                File.Delete(parent.FilePath);

                foreach (string dependency in mod.Metadata.Dependencies.Where(dependency => DataManager.ExcludedServer.Contains(dependency)))
                    Log.Warning("{GetFileName} depends on an excluded mod {Dependency}", fileName, dependency);
                break;
        }

        foreach (string author in mod.Metadata.Authors.Where(author => DataManager.ExcludedAuthors.Contains(author, StringComparer.InvariantCultureIgnoreCase)))
        {
            Log.Warning("{GetFileName} contains Author {Author} who is excluded. Deleting...", fileName, author);
            File.Delete(parent.FilePath);
        }

        if (!mod.OnlyJarInJars && mod.ClassFileCount < DataManager.FlagDataOnly)
            Log.Warning("{GetFileName} contains less classes than {FlagDataOnly} classes: {ModClassFileCount}", fileName, DataManager.FlagDataOnly, mod.ClassFileCount);

        if (mod.IsMcreator && DataManager.FlagMcreator)
            Log.Warning("{GetFileName} contains MCreator fragments", fileName);

        if (mod.NestedJars.Count == 0) return;
        foreach (ModInfo jarMod in mod.NestedJars) LogFilterResults(jarMod, mod, client);
    }

    #endregion
}
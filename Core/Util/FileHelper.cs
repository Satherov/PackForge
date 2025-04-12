using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
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

public record RuleSet(List<Rule> Rules, bool Whitelist);

public record Rule(string FilePath, FileAttributes Attributes);

public static class FileHelper
{
    public static async Task<string> PrepareRootFolderAsync(string? destinationFolder, string? folderName, string? version)
    {
        if (Validator.IsNullOrWhiteSpace(destinationFolder) || Validator.IsNullOrWhiteSpace(folderName) || Validator.IsNullOrWhiteSpace(version)) return string.Empty;

        try
        {
            string rootFolder = Path.Combine(destinationFolder!, folderName ?? "Unknown", version ?? "0.0.0");
            if (Directory.Exists(rootFolder)) return rootFolder;
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

        string overwritesFolderPath = Path.Combine(rootPath, "overwrites");
        if (Validator.DirectoryExists(overwritesFolderPath, null))
            Directory.Delete(overwritesFolderPath, true);

        Directory.CreateDirectory(overwritesFolderPath);
        await CleanPermissionsAsync(overwritesFolderPath);

        foreach (string directory in Directory.GetDirectories(rootPath))
        {
            string directoryName = Path.GetFileName(directory);
            if (string.Equals(directoryName, "overwrites", StringComparison.OrdinalIgnoreCase))
                continue;

            bool isEmpty = !Validator.DirectoryEmpty(directory, null);
            bool containsExcludedName = directoryName.Contains("shaderpacks", StringComparison.OrdinalIgnoreCase) ||
                                        directoryName.Contains("resourcepacks", StringComparison.OrdinalIgnoreCase);

            if (isEmpty || containsExcludedName)
            {
                Directory.Delete(directory, true);
            }
            else
            {
                string destination = Path.Combine(overwritesFolderPath, directoryName);

                if (Validator.DirectoryExists(destination, null))
                    destination = Path.Combine(overwritesFolderPath, $"{directoryName}_{Guid.NewGuid():N}");

                Directory.Move(directory, destination);
            }
        }

        foreach (string file in Directory.GetFiles(rootPath))
        {
            string fileName = Path.GetFileName(file);
            if (!string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase))
                File.Delete(file);
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
            return !folderResult.Any() ? string.Empty : folderResult.Single().Path.LocalPath;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static async Task ApplyFilters(string? sourceDir, bool client, CancellationToken ct = default)
    {
        if (!Validator.DirectoryExists(sourceDir)) return;

        Stopwatch stopwatch = Stopwatch.StartNew();
        Log.Information($"Applying filters to {sourceDir}");

        List<ModInfo> allData = await JarHelper.GetAllModData(sourceDir, ct);
        foreach (ModInfo mod in allData)
        {
            if (string.IsNullOrWhiteSpace(mod.ModId) && mod.JarInJars.Count > 0)
            {
                Log.Debug($"Mod {Path.GetFileName(mod.FilePath)} only contains JarInJars");
                foreach (ModInfo jarMod in mod.JarInJars) ApplyResults(jarMod, client, true, mod);
                continue;
            }

            if (string.IsNullOrWhiteSpace(mod.ModId)) continue;

            Log.Debug($"{mod}");
            ApplyResults(mod, client);
        }

        Log.Information($"Finished applying filters after {stopwatch.ElapsedMilliseconds}ms");
    }

    private static void ApplyResults(ModInfo mod, bool client, bool jarInJar = false, ModInfo? parent = null)
    {
        string prefix = jarInJar ? $"JarInJar '{mod.Name}' found within " : string.Empty;
        parent ??= mod;

        if (DataManager.ExcludedCommon.Contains(mod.ModId))
        {
            Log.Warning($"{prefix}'{Path.GetFileName(parent.FilePath)}' matching {mod.ModId} is excluded");
            File.Delete(parent.FilePath);
        }

        switch (client)
        {
            case true when DataManager.ExcludedClient.Contains(mod.ModId):
                Log.Warning($"{prefix}'{Path.GetFileName(parent.FilePath)}' containing {mod.ModId} is excluded on the client");
                File.Delete(parent.FilePath);
                break;
            case false when DataManager.ExcludedServer.Contains(mod.ModId):
                Log.Warning($"{prefix}'{Path.GetFileName(parent.FilePath)}' containing {mod.ModId} is excluded on the server");
                File.Delete(parent.FilePath);
                break;
        }

        foreach (string author in mod.Authors.Where(author => DataManager.ExcludedAuthors.Contains(author, StringComparer.InvariantCultureIgnoreCase)))
        {
            Log.Warning($"{prefix}'{Path.GetFileName(parent.FilePath)}' contains Author '{author}' who is excluded");
            File.Delete(parent.FilePath);
        }

        if (mod.DataOnly && DataManager.FlagDataOnly)
            Log.Warning($"{prefix}'{Path.GetFileName(parent.FilePath)}' contains only data");

        if (mod.McreatorFragments && DataManager.FlagMcreator)
            Log.Warning($"{prefix}'{Path.GetFileName(parent.FilePath)}' contains MCreator fragments");

        if (mod.JarInJars.Count == 0) return;
        foreach (ModInfo jarMod in mod.JarInJars) ApplyResults(jarMod, client, true, mod);
    }

    public static async Task CopyFilesAsync(string sourceDir, string targetDir, RuleSet? ruleSet, CancellationToken ct)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (!Validator.DirectoryExists(sourceDir)) return;
        Directory.CreateDirectory(targetDir);
        Log.Debug($"Copying files from {sourceDir} to {targetDir}");

        List<string> fileFilter = ruleSet?.Rules.Select(r => r.FilePath).ToList() ?? [];
        bool isWhitelist = ruleSet?.Whitelist == true;

        SemaphoreSlim semaphore = new(4);

        if (isWhitelist)
        {
            foreach (Rule rule in ruleSet!.Rules)
            {
                string sourcePath = Path.Combine(sourceDir, rule.FilePath);
                string destPath = Path.Combine(targetDir, rule.FilePath);

                if ((rule.Attributes & FileAttributes.Directory) != 0 && Directory.Exists(sourcePath))
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        await CopyFolderRobocopyAsync(sourcePath, destPath, fileFilter, ct);
                    else
                        await CopyFolderRecursiveAsync(sourcePath, destPath, fileFilter, semaphore, ct);
                }
                else if (File.Exists(sourcePath))
                {
                    await CopySingleFileAsync(sourcePath, targetDir, [], semaphore, ct);
                }
            }
        }
        else
        {
            string[] entries = Directory.GetFileSystemEntries(sourceDir);
            IEnumerable<Task> tasks = entries.Select(async entry =>
            {
                if (fileFilter.Contains(Path.GetFileName(entry))) return;

                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        await CopyFolderRobocopyAsync(entry, Path.Combine(targetDir, Path.GetFileName(entry)), fileFilter, ct);
                    else
                        await CopyFolderRecursiveAsync(entry, Path.Combine(targetDir, Path.GetFileName(entry)), fileFilter, semaphore, ct);
                }
                else
                {
                    await CopySingleFileAsync(entry, targetDir, [], semaphore, ct);
                }
            });

            await Task.WhenAll(tasks);
        }

        stopwatch.Stop();
        Log.Debug($"Finished copying files in {stopwatch.ElapsedMilliseconds}ms");
    }

    private static async Task CopyFolderRobocopyAsync(string sourcePath, string destPath, List<string> fileFilter, CancellationToken ct)
    {
        if (!Directory.Exists(sourcePath)) return;
        Directory.CreateDirectory(destPath);
        string argsBase = $"\"{sourcePath}\" \"{destPath}\" /E /NFL /NDL /NJH /NJS /NC /NS";
        string excludes = string.Join(" ", fileFilter.Select(f => $"\"{f}\""));
        string args = $"{argsBase} /R:3 /W:5{(fileFilter.Count != 0 ? $" /XD {excludes} /XF {excludes}" : null)}";
        try
        {
            using Process process = new();
            process.StartInfo.FileName = "robocopy";
            process.StartInfo.Arguments = args;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            await Task.Run(() => process.WaitForExit(), ct);
        }
        catch
        {
            // ignored
        }
    }

    private static async Task CopyFolderRecursiveAsync(string sourcePath, string destPath, List<string> fileFilter, SemaphoreSlim semaphore, CancellationToken ct)
    {
        if (!Directory.Exists(sourcePath)) return;
        Directory.CreateDirectory(destPath);
        string[] files = Directory.GetFiles(sourcePath);
        List<Task> fileTasks = files.Select(filePath => CopySingleFileAsync(filePath, destPath, fileFilter, semaphore, ct)).ToList();
        await Task.WhenAll(fileTasks);
        string[] subDirs = Directory.GetDirectories(sourcePath);
        List<Task> subDirTasks = subDirs.Select(subDir =>
        {
            string subDirName = Path.GetFileName(subDir);
            string newDestPath = Path.Combine(destPath, subDirName);
            return CopyFolderRecursiveAsync(subDir, newDestPath, fileFilter, semaphore, ct);
        }).ToList();
        await Task.WhenAll(subDirTasks);
    }

    private static async Task CopySingleFileAsync(string sourceFile, string destFolder, List<string> fileFilter, SemaphoreSlim semaphore, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string fileName = Path.GetFileName(sourceFile);
        if (fileFilter.Contains(fileName)) return;
        string destFile = Path.Combine(destFolder, fileName);
        if (File.Exists(destFile))
            try
            {
                FileInfo destInfo = new(destFile);
                destInfo.Attributes &= ~FileAttributes.ReadOnly;
            }
            catch
            {
                // ignored
            }

        await semaphore.WaitAsync(ct);
        try
        {
            const int bufferSize = 1048576;
            await using FileStream sourceStream = new(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
            await using FileStream destStream = new(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);
            await sourceStream.CopyToAsync(destStream, bufferSize, ct);
        }
        finally
        {
            semaphore.Release();
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
}
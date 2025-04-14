using System;
using System.Collections.Generic;
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

public record RuleSet(List<Rule> Rules, bool Whitelist);

public record Rule(string FilePath, FileAttributes Attributes);

public static class FileHelper
{
    public static async Task<string> PrepareRootFolderAsync(string? destinationFolder, string? folderName, string? version)
    {
        if (Validator.IsNullOrWhiteSpace(destinationFolder) || Validator.IsNullOrWhiteSpace(folderName) || Validator.IsNullOrWhiteSpace(version)) return string.Empty;

        try
        {
            string rootFolder = Path.Combine(destinationFolder, folderName, version);
            if (Directory.Exists(rootFolder)) Directory.Delete(rootFolder, true);
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
            if(dirName == "overrides") continue;
            
            if(Validator.DirectoryEmpty(directory, null))
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

        if (!mod.OnlyJarInJars && !(mod.Classes <= -1) && mod.Classes < DataManager.FlagDataOnly)
            Log.Warning($"{prefix}'{Path.GetFileName(parent.FilePath)}' contains less classes than {DataManager.FlagDataOnly} classes: {mod.Classes}");

        if (mod.McreatorFragments && DataManager.FlagMcreator)
            Log.Warning($"{prefix}'{Path.GetFileName(parent.FilePath)}' contains MCreator fragments");

        if (mod.JarInJars.Count == 0) return;
        foreach (ModInfo jarMod in mod.JarInJars) ApplyResults(jarMod, client, true, mod);
    }

    public static async Task CopyFilesAsync(string sourceDir, string targetDir, RuleSet? ruleSet, CancellationToken ct = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (!Validator.DirectoryExists(sourceDir)) return;
        Directory.CreateDirectory(targetDir);
        Log.Debug($"Copying files from {sourceDir} to {targetDir}");


        SemaphoreSlim semaphore = new(4);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            await CopyFolderRobocopyAsync(sourceDir,targetDir, ruleSet, ct);
        else
            await CopyFolderRecursiveAsync(sourceDir, targetDir, ruleSet, sourceDir, semaphore, ct);

        stopwatch.Stop();
        Log.Debug($"Finished copying files in {stopwatch.ElapsedMilliseconds}ms");
    }

    private static async Task CopyFolderRobocopyAsync(string sourceDir, string targetDir, RuleSet? ruleSet, CancellationToken ct = default)
    {
        if (!Directory.Exists(sourceDir))
            return;

        sourceDir = sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        targetDir = targetDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        
        Directory.CreateDirectory(targetDir);
        Log.Debug($"Using Robocopy to copy files from {sourceDir} to {targetDir}");
        
        
        if (ruleSet is { Whitelist: true })
        {
            List<Task> tasks = [];
            List<string> files = [];
            
            foreach (Rule rule in ruleSet.Rules)
            {
                if(rule.Attributes == FileAttributes.Directory)
                {
                    string sourceFolder = Path.Combine(sourceDir, rule.FilePath);
                    string destFolder = Path.Combine(targetDir, rule.FilePath);
                    Directory.CreateDirectory(destFolder);
                    string args = $"\"{sourceFolder}\" \"{destFolder}\" /E /NFL /NDL /NJH /NJS /NC /NS";
                    Log.Debug($"Starting Robocopy for folder: {args}");
                    tasks.Add(RunRobocopy(args, ct));
                }
                else
                {
                    files.Add(Path.GetFileName(rule.FilePath));
                }
            }
            
            if(tasks.Count > 0)
                await Task.WhenAll(tasks);
            if (files.Count <= 0) return;
            {
                Directory.CreateDirectory(targetDir);
                string args = $"\"{sourceDir}\" \"{targetDir}\" \"{string.Join(" ", files)}\" /NFL /NDL /NJH /NJS /NC /NS";
                Log.Debug($"Starting Robocopy for file: {args}");
                await RunRobocopy(args, ct);
            }
        }
        else
        {
            StringBuilder argBuilder = new();
            argBuilder.Append($"\"{sourceDir}\" \"{targetDir}\" /S /NFL /NDL /NJH /NJS /NC /NS");

            if (ruleSet != null)
            {
                List<string> excludedDirs = [];
                List<string> excludedFiles = [];

                foreach (Rule rule in ruleSet.Rules)
                {
                    if (rule.Attributes.HasFlag(FileAttributes.Directory))
                        excludedDirs.Add($"\"{Path.Combine(sourceDir, rule.FilePath)}\"");
                    else
                        excludedFiles.Add($"\"{Path.Combine(sourceDir, rule.FilePath)}\"");
                }
                if (excludedDirs.Count > 0)
                    argBuilder.Append(" /XD " + string.Join(" ", excludedDirs));
                if (excludedFiles.Count > 0)
                    argBuilder.Append(" /XF " + string.Join(" ", excludedFiles));
            }

            string args = argBuilder.ToString();
            Log.Debug($"Robocopy arguments: {args}");
            await RunRobocopy(args, ct);
        }
        
        return;

        async Task RunRobocopy(string arguments, CancellationToken token)
        {
            try
            {
                using Process process = new();
                process.StartInfo.FileName = "robocopy";
                process.StartInfo.Arguments = arguments;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = false;
                process.Start();
                await process.WaitForExitAsync(token);
            }
            catch (Exception ex)
            {
                Log.Error($"Robocopy process failed: {ex.Message}");
            }
        }
    }

    private static async Task CopyFolderRecursiveAsync(string sourceDir, string targetDir, RuleSet? ruleSet, string baseSource, SemaphoreSlim semaphore, CancellationToken ct = default)
    {
        if (!Directory.Exists(sourceDir))
            return;

        Directory.CreateDirectory(targetDir);

        string[] files = Directory.GetFiles(sourceDir);
        List<Task> fileTasks = [];
        foreach (string file in files)
        {
            string relativeFile = Path.GetRelativePath(baseSource, file);
            bool skip = false;
            if (ruleSet != null)
            {
                if (ruleSet.Whitelist)
                {
                    if (!ruleSet.Rules.Any(r => !r.Attributes.HasFlag(FileAttributes.Directory) &&
                         string.Equals(r.FilePath, relativeFile, StringComparison.OrdinalIgnoreCase)))
                    {
                        skip = true;
                    }
                }
                else
                {
                    if (ruleSet.Rules.Any(r => !r.Attributes.HasFlag(FileAttributes.Directory) &&
                         string.Equals(r.FilePath, relativeFile, StringComparison.OrdinalIgnoreCase)))
                    {
                        skip = true;
                    }
                }
            }
            if (!skip)
                fileTasks.Add(CopySingleFileAsync(file, targetDir, semaphore, ct));
        }
        await Task.WhenAll(fileTasks);

        string[] subDirs = Directory.GetDirectories(sourceDir);
        List<Task> subDirTasks = [];
        foreach (string subDir in subDirs)
        {
            string relativeDir = Path.GetRelativePath(baseSource, subDir);
            bool skip = false;
            if (ruleSet != null)
            {
                if (ruleSet.Whitelist)
                {
                    if (!ruleSet.Rules.Any(r => r.Attributes.HasFlag(FileAttributes.Directory) &&
                         (string.Equals(r.FilePath, relativeDir, StringComparison.OrdinalIgnoreCase) ||
                          relativeDir.StartsWith(r.FilePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))))
                    {
                        skip = true;
                    }
                }
                else
                {
                    if (ruleSet.Rules.Any(r => r.Attributes.HasFlag(FileAttributes.Directory) &&
                         (string.Equals(r.FilePath, relativeDir, StringComparison.OrdinalIgnoreCase) ||
                          relativeDir.StartsWith(r.FilePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))))
                    {
                        skip = true;
                    }
                }
            }

            if (skip) continue;
            
            string subFolderName = Path.GetFileName(subDir);
            string newDestPath = Path.Combine(targetDir, subFolderName);
            subDirTasks.Add(CopyFolderRecursiveAsync(subDir, newDestPath, ruleSet, baseSource, semaphore, ct));
        }
        await Task.WhenAll(subDirTasks);
    }

    private static async Task CopySingleFileAsync(string sourceFile, string destFolder, SemaphoreSlim semaphore, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string fileName = Path.GetFileName(sourceFile);
        string destFile = Path.Combine(destFolder, fileName);

        if (File.Exists(destFile))
        {
            try
            {
                FileInfo destInfo = new(destFile);
                destInfo.Attributes &= ~FileAttributes.ReadOnly;
            }
            catch { }
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Serilog;

namespace PackForge.Core.Util;

public static class FileHelper
{
    /// <summary>
    /// Prepares the root folder for the modpack
    /// </summary>
    /// <param name="destinationFolder">Location to create the folder in</param>
    /// <param name="folderName">Name of the created root</param>
    /// <returns>Location of the folder created</returns>
    public static async Task<string> PrepareRootFolderAsync(string? destinationFolder, string? folderName)
    {
        Log.Information("Generating root folder");
        
        if (string.IsNullOrEmpty(destinationFolder))
        {
            Log.Error("Destination folder does not exist");
            return string.Empty;
        }

        if (string.IsNullOrEmpty(folderName))
        {
            Log.Error("Folder name is not set");
            return string.Empty;
        }

        try
        {
            var rootFolder = Path.Combine(destinationFolder, folderName);

            // If the folder already exists, return the path and stop
            if (Directory.Exists(rootFolder))
            {
                Log.Debug("Root already exists");
                Directory.Delete(rootFolder, true);
                return rootFolder;
            };
            
            Log.Debug("Creating new root folder");
            Directory.CreateDirectory(rootFolder);
            var directoryInfo = new DirectoryInfo(rootFolder);
            // Remove possible read-only flag
            Log.Debug("Removing read-only flag");
            directoryInfo.Attributes &= ~FileAttributes.ReadOnly;

            await Task.Run(async () =>
            {
                Log.Debug("Setting full control for current user");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Set full control for the current user on Windows
                    Log.Debug("Running on Windows");
                    var security = directoryInfo.GetAccessControl();
                    security.AddAccessRule(new FileSystemAccessRule(
                        WindowsIdentity.GetCurrent().Name,
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow
                    ));
                    directoryInfo.SetAccessControl(security);
                }
                else
                {
                    // Set full control for all users on non-Windows platforms
                    Log.Debug("Running on non-Windows");
                    await Task.Run(() => System.Diagnostics.Process.Start("chmod", $"777 \"{rootFolder}\""));
                }
            });

            return rootFolder;
        }
        catch (Exception ex)
        {
            Log.Error($"Error preparing root folder: {ex.Message}");
            return string.Empty;
        }
    }

    public static async Task CleanUpClientFolder(string rootPath)
    {
        Log.Information("Cleaning up client folder");
        
        if (!Directory.Exists(rootPath))
        {
            Log.Error($"The folder '{rootPath}' does not exist.");
            return;
        }

        var overwritesFolder = Path.Combine(rootPath, "overwrites");
        if (Directory.Exists(overwritesFolder))
        {
            Log.Debug("Deleting existing overwrites folder");
            Directory.Delete(overwritesFolder, true);
        }
        
        Directory.CreateDirectory(overwritesFolder);

        foreach (var subDir in Directory.GetDirectories(rootPath))
        {
            var folderName = Path.GetFileName(subDir);
            
            Log.Debug($"Processing {folderName}");
            
            if (string.Equals(folderName, "overwrites", StringComparison.OrdinalIgnoreCase))
                continue;

            var isEmpty = !Directory.EnumerateFileSystemEntries(subDir).Any();
            var destinationPath = Path.Combine(overwritesFolder, folderName);
            if (isEmpty)
            {
                Log.Debug($"{folderName} is empty, deleting");
                await Task.Run(() => Directory.Delete(subDir, true));
            }
            else
            {
                if (Directory.Exists(destinationPath))
                {
                    destinationPath = Path.Combine(overwritesFolder, folderName + "_" + Guid.NewGuid().ToString("N"));
                }

                Log.Debug($"Moving {folderName}");
                await Task.Run(() => Directory.Move(subDir, destinationPath));
            }
        }

        // Process files in the root folder.
        // Delete any file that is not "manifest.json".
        foreach (var file in Directory.GetFiles(rootPath))
        {
            var fileName = Path.GetFileName(file);
            if (!string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>
    /// Opens a folder picker dialog and returns the selected folder path
    /// </summary>
    /// <param name="startLocation">Initial location to open if the path has already been defined before</param>
    /// <returns>The folder location selected</returns>
    /// <exception cref="SystemException">If you manage to call this function while the main windows doesn't exist yet. Should never happen!</exception>
    public static async Task<string?> OpenFolderAsync(string? startLocation = null)
    {
        Log.Debug("Opening folder picker dialog");
        
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            Log.Error("Application lifetime is not desktop style or null.");
            return string.Empty;
        }

        try
        {
            var window = desktopLifetime.MainWindow;
            if (window == null)
            {
                Log.Error("Main window is null.");
                throw new SystemException();
            }
            
            var folderResult = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select a folder",
                SuggestedStartLocation = startLocation != null ? await window.StorageProvider.TryGetFolderFromPathAsync(startLocation) : null
            });

            if (!folderResult.Any())
            {
                Log.Warning("No folder selected");
                return string.Empty;
            }

            var selectedFolder = folderResult.Single().Path.LocalPath;
            Log.Information($"Selected folder: {selectedFolder}");
            return selectedFolder;
        }
        catch (Exception ex)
        {
            Log.Error($"Error during folder selection: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Copies folder recursively from source to destination using robocopy
    /// </summary>
    /// <param name="sourceDir">The source to copy files from</param>
    /// <param name="targetDir">The destination to copy files to</param>
    /// <param name="folderRules">Filter dictionary to use (folderName, ([files to filter], whitelist?))</param>
    /// <param name="ct">CancellationToken to stop operation if requested by the user</param>
    public static async Task CopyMatchingFilesAsync(
        string sourceDir,
        string targetDir,
        Dictionary<string, (List<string> files, bool isWhitelist)> folderRules,
        CancellationToken ct)
    {
        if (!Directory.Exists(sourceDir))
        {
            Log.Warning($"Source directory does not exist: {sourceDir}");
            return;
        }
        
        Log.Debug($"Source directory: {sourceDir}");
        Log.Debug($"Target directory: {targetDir}");
        
        var sourceSeg = sourceDir.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        Log.Information($"Copying folders from {Path.Combine(sourceSeg[^2], sourceSeg[^1])} to {Path.GetFileName(targetDir)}");
        

        Directory.CreateDirectory(targetDir);

        foreach (var (name, (fileFilter, isWhitelist)) in folderRules)
        {
            ct.ThrowIfCancellationRequested();

            var sourcePath = Path.Combine(sourceDir, name);
            var destPath = Path.Combine(targetDir, name);
            
            if (File.Exists(sourcePath))
            {
                Log.Debug($"Copying {name} to {Path.GetFileName(destPath)}");
                await CopySingleFileAsync(sourcePath, targetDir, [], false, new SemaphoreSlim(4), ct);
                Log.Debug($"Copied file {name}");
                return;
            }

            if (!Directory.Exists(sourcePath))
            {
                Log.Warning($"{name} does not exist in {sourceDir}");
                continue;
            }
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use robocopy on Windows
                Log.Debug($"Copying folder {name} using robocopy");
                await CopyFolderRobocopyAsync(sourcePath, destPath, fileFilter, isWhitelist, ct);
            }
            else
            {
                // Use recursive copy on non-Windows platforms
                Log.Debug($"Copying folder {name} using recursive copy");
                await CopyFolderRecursiveAsync(sourcePath, destPath, fileFilter, isWhitelist, new SemaphoreSlim(4), ct);
            }
            
            Log.Debug($"Copied folder {name}");
        }
    }

    /// <summary>
    /// Copies folder recursively from source to destination using robocopy
    /// </summary>
    /// <param name="sourcePath">The source to copy files from</param>
    /// <param name="destPath">The destination to copy files to</param>
    /// <param name="fileFilter">List of files to white/blacklist</param>
    /// <param name="isWhitelist">Is the filter a white or blacklist?</param>
    /// <param name="ct">CancellationToken to stop operation if requested by the user</param>
    private static async Task CopyFolderRobocopyAsync(
        string sourcePath,
        string destPath,
        List<string> fileFilter,
        bool isWhitelist,
        CancellationToken ct)
    {
        if (!Directory.Exists(sourcePath))
            return;

        // Ensure destination exists
        Directory.CreateDirectory(destPath);

        // Build the base robocopy arguments for recursive copy (/E)
        var argsBase = $"\"{sourcePath}\" \"{destPath}\" /E /NFL /NDL /NJH /NJS /NC /NS";

        if (isWhitelist)
        {
            // For each file in the whitelist, call robocopy to copy only that file.
            foreach (var file in fileFilter)
            {
                ct.ThrowIfCancellationRequested();
                // /IF is not available, so specify the file directly as a file pattern.
                string args = $"\"{sourcePath}\" \"{destPath}\" \"{file}\" /COPYALL /R:3 /W:5";
                await RunRobocopyAsync(args, ct);
            }
        }
        else
        {
            var excludes = string.Join(" ", fileFilter.Select(f => $"\"{f}\""));
            var args = $"{argsBase} /R:3 /W:5 {(fileFilter.Any() ? $"/XF {excludes}" : string.Empty)}";
            await RunRobocopyAsync(args, ct);
        }
    }

    /// <summary>
    /// Runs the robocopy command with the given arguments
    /// </summary>
    /// <param name="arguments">Extra parameters used in the robocopy command</param>
    /// <param name="ct">CancellationToken to stop operation if requested by the user</param>
    private static async Task RunRobocopyAsync(string arguments, CancellationToken ct)
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "robocopy";
            process.StartInfo.Arguments = arguments;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();

            // Robocopy returns non-zero exit codes even for success.
            await Task.Run(() => process.WaitForExit(), ct);
        }
        catch (Exception ex)
        {
            Log.Error($"Robocopy error: {ex.Message}");
        }
    }


    /// <summary>
    /// Copies folder recursively from source to destination, used for non-Windows platforms
    /// </summary>
    /// <param name="sourcePath">The source to copy files from</param>
    /// <param name="destPath">The destination to copy files to</param>
    /// <param name="fileFilter">List of files to white/blacklist</param>
    /// <param name="isWhitelist">Is the filter a white or blacklist?</param>
    /// <param name="semaphore">Limits the number of concurrent file copies</param>
    /// <param name="ct">CancellationToken to stop operation if requested by the user</param>
    private static async Task CopyFolderRecursiveAsync(
        string sourcePath,
        string destPath,
        List<string> fileFilter,
        bool isWhitelist,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        if (!Directory.Exists(sourcePath))
            return;

        Directory.CreateDirectory(destPath);

        var files = Directory.GetFiles(sourcePath);
        var fileTasks = files.Select(filePath =>
            CopySingleFileAsync(filePath, destPath, fileFilter, isWhitelist, semaphore, ct)).ToList();

        await Task.WhenAll(fileTasks);

        var subDirs = Directory.GetDirectories(sourcePath);
        var subDirTasks = subDirs.Select(subDir =>
        {
            var subDirName = Path.GetFileName(subDir);
            var newDestPath = Path.Combine(destPath, subDirName);
            return CopyFolderRecursiveAsync(subDir, newDestPath, fileFilter, isWhitelist, semaphore, ct);
        }).ToList();

        await Task.WhenAll(subDirTasks);
    }

    /// <summary>
    /// Copies a single file from source to destination
    /// </summary>
    /// <param name="sourceFile">The sourceFile to copy</param>
    /// <param name="destFolder">The destination to copy the file to</param>
    /// <param name="fileFilter">List of files to white/blacklist</param>
    /// <param name="isWhitelist">Is the filter a white or blacklist?</param>
    /// <param name="semaphore">Limits the number of concurrent file copies</param>
    /// <param name="ct">CancellationToken to stop operation if requested by the user</param>
    private static async Task CopySingleFileAsync(
        string sourceFile,
        string destFolder,
        List<string> fileFilter,
        bool isWhitelist,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var fileName = Path.GetFileName(sourceFile);
        var shouldCopy = isWhitelist ? fileFilter.Contains(fileName) : !fileFilter.Contains(fileName);
        if (!shouldCopy) return;

        var destFile = Path.Combine(destFolder, fileName);

        await semaphore.WaitAsync(ct);
        try
        {
            const int bufferSize = 1048576;
            await using var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
            await using var destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);

            await sourceStream.CopyToAsync(destStream, bufferSize, ct);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
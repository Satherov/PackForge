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
using Serilog;
using SevenZip;
using CompressionLevel = System.IO.Compression.CompressionLevel;


namespace PackForge.Core.Helpers;

public static class FileHelper
{
    /// <summary>
    /// Prepares the root folder for the modpack
    /// </summary>
    /// <param name="destinationFolder">Location to create the folder in</param>
    /// <param name="folderName">Name of the created root</param>
    /// <param name="version">Used in the folder structure</param>
    /// <returns>Location of the folder created</returns>
    public static string PrepareRootFolderAsync(string? destinationFolder, string? folderName, string? version)
    {
        Log.Information("Generating root folder");
        if(Validator.IsNullOrWhiteSpace(destinationFolder) ||
           Validator.IsNullOrWhiteSpace(folderName) ||
           Validator.IsNullOrWhiteSpace(version)) return string.Empty;

        try
        {
            var rootFolder = Path.Combine(destinationFolder!, folderName ?? "Unknown", version ?? "0.0.0");
            
            if (Directory.Exists(rootFolder))
            {
                Log.Debug("Root already exists");
                return rootFolder;
            }
            
            Log.Debug("Creating new root folder");
            Directory.CreateDirectory(rootFolder);
            CleanPermissions(rootFolder);

            return rootFolder;
        }
        catch (Exception ex)
        {
            Log.Error($"Error preparing root folder: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Prepares a folder
    /// </summary>
    /// <param name="destinationFolder">Path the folder is supposed to end up at</param>
    /// <param name="folderName">Name of the folder</param>
    /// <returns>Location of the new folder</returns>
    public static string PrepareFolder(string? destinationFolder, string? folderName)
    {
        folderName ??= "Unknown";
        
        Log.Information($"Generating {folderName} folder");
        
        if(Validator.IsNullOrWhiteSpace(destinationFolder) ||
           Validator.IsNullOrWhiteSpace(folderName)) return string.Empty;

        try
        {
            var targetPath = Path.Join(destinationFolder!, folderName);
            
            if (Directory.Exists(targetPath))
            {
                Log.Debug($"{folderName} already exists");
                Directory.Delete(targetPath, true);
            }

            Log.Debug($"Creating new {folderName} folder");
            Directory.CreateDirectory(targetPath);
            CleanPermissions(targetPath);
            
            return targetPath;
        }
        catch (Exception ex)
        {
            Log.Error($"Error preparing {folderName} folder: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Removes any empty folders in the given root
    /// </summary>
    /// <param name="rootPath">Root to clean</param>
    public static void CleanUpEmptyFolders(string rootPath)
    {
        Log.Information("Cleaning up empty folders");

        if (!Validator.DirectoryExists(rootPath)) return;

        var overwritesFolder = Path.Combine(rootPath, "overwrites");
        if (Validator.DirectoryExists(overwritesFolder, logLevel:"debug"))
        {
            Log.Debug("Deleting existing overwrites folder");
            Directory.Delete(overwritesFolder, true);
        }
        
        Directory.CreateDirectory(overwritesFolder);
        CleanPermissions(overwritesFolder);

        foreach (var subDir in Directory.GetDirectories(rootPath))
        {
            var folderName = Path.GetFileName(subDir);
            
            Log.Debug($"Processing {folderName}");
            
            if (string.Equals(folderName, "overwrites", StringComparison.OrdinalIgnoreCase))
                continue;

            var isEmpty = !Directory.EnumerateFileSystemEntries(subDir).Any();
            var destinationPath = Path.Combine(overwritesFolder, folderName);
            if (isEmpty || 
                folderName.Contains("shaderpacks", StringComparison.OrdinalIgnoreCase) || 
                folderName.Contains("resourcepacks", StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug($"{folderName} is empty, deleting");
                Directory.Delete(subDir, true);
            }
            else
            {
                if (Directory.Exists(destinationPath))
                {
                    destinationPath = Path.Combine(overwritesFolder, folderName + "_" + Guid.NewGuid().ToString("N"));
                }

                Log.Debug($"Moving {folderName}");
                Directory.Move(subDir, destinationPath);
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
    /// <param name="focusWindow">Focuses a window after the folder picker closes</param>
    /// <returns>The folder location selected</returns>
    /// <exception cref="SystemException">If you manage to call this function while the main windows doesn't exist yet. Should never happen!</exception>
    public static async Task<string?> OpenFolderAsync(string? startLocation = null,  Window? focusWindow = null)
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

            if (!Validator.IsNullOrWhiteSpace(focusWindow, logLevel: "debug")) await WindowHelper.FocusWindow(() => focusWindow!);
  
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
    /// <exception cref="ArgumentOutOfRangeException">If you somehow manage to enter a file type that doesnt exist</exception>
    public static async Task CopyMatchingFilesAsync(
        string sourceDir,
        string targetDir,
        Dictionary<string, (FileAttributes, List<string> files , bool isWhitelist)>? folderRules ,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        Log.Debug($"Starting file copy from {sourceDir} to {targetDir}");
        
        if (!Validator.DirectoryExists(sourceDir)) return;

        Log.Debug($"Source directory: {sourceDir}");
        Log.Debug($"Target directory: {targetDir}");

        // Ensure target exists.
        Directory.CreateDirectory(targetDir);

        folderRules ??= new Dictionary<string, (FileAttributes, List<string> files , bool isWhitelist)>
        {
            { string.Empty, (FileAttributes.Normal, [], false) },
        };

        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim(4);

        await Parallel.ForEachAsync(folderRules, ct, async (rule, token) =>
        {
            var (name, (fileType, fileFilter, isWhitelist)) = rule;
            token.ThrowIfCancellationRequested();

            var sourcePath = Path.Combine(sourceDir, name);
            var destPath = Path.Combine(targetDir, name);

            // Use a bitwise check to see if fileType represents a directory.
            var isDirectoryRule = (fileType & FileAttributes.Directory) != 0;

            if (isWhitelist)
            {
                if (isDirectoryRule && Directory.Exists(sourcePath))
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        Log.Debug($"Copying folder {name} using robocopy");
                        await CopyFolderRobocopyAsync(sourcePath, destPath, fileFilter, token);
                    }
                    else
                    {
                        Log.Debug($"Copying folder {name} using recursive copy");
                        await CopyFolderRecursiveAsync(sourcePath, destPath, fileFilter, semaphore, token);
                    }
                }
                else if (File.Exists(sourcePath))
                {
                    Log.Debug($"Copying {name} to {Path.GetFileName(destPath)}");
                    await CopySingleFileAsync(sourcePath, targetDir, new List<string>(), semaphore, token);
                }
                else
                {
                    Log.Warning($"{name} does not exist in {sourceDir}");
                }
                return;
            }

            // Note: using sourcePath instead of sourceDir here might be more appropriate.
            var entries = Directory.GetFileSystemEntries(sourceDir);
            var entryTasks = entries.Select(async entry =>
            {
                // Remove any entries that are in the file filter.
                if (fileFilter.Contains(Path.GetFileName(entry))) return;

                // Use bitwise check for directories.
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        Log.Debug($"Copying folder {entry} using robocopy");
                        await CopyFolderRobocopyAsync(entry, Path.Combine(targetDir, Path.GetFileName(entry)), fileFilter, token);
                    }
                    else
                    {
                        Log.Debug($"Copying folder {entry} using recursive copy");
                        await CopyFolderRecursiveAsync(entry, Path.Combine(targetDir, Path.GetFileName(entry)), fileFilter, semaphore, token);
                    }
                }
                else
                {
                    await CopySingleFileAsync(entry, targetDir, new List<string>(), semaphore, token);
                }
            });

            tasks.AddRange(entryTasks);
        });

        await Task.WhenAll(tasks);
        
        stopwatch.Stop();
        Log.Debug($"File copying completed in {stopwatch.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Copies folder recursively from source to destination using robocopy
    /// </summary>
    /// <param name="sourcePath">The source to copy files from</param>
    /// <param name="destPath">The destination to copy files to</param>
    /// <param name="fileFilter">List of files to white/blacklist</param>
    /// <param name="ct">CancellationToken to stop operation if requested by the user</param>
    private static async Task CopyFolderRobocopyAsync(
        string sourcePath,
        string destPath,
        List<string> fileFilter,
        CancellationToken ct)
    {
        if (!Directory.Exists(sourcePath))
            return;

        // Ensure destination exists
        Directory.CreateDirectory(destPath);

        var argsBase = $"\"{sourcePath}\" \"{destPath}\" /E /NFL /NDL /NJH /NJS /NC /NS";
        var excludes = string.Join(" ", fileFilter.Select(f => $"\"{f}\""));
        var args = $"{argsBase} /R:3 /W:5{(fileFilter.Count != 0 ? $" /XD {excludes} /XF {excludes}" : null)}";
        
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "robocopy";
            process.StartInfo.Arguments = args;
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
    /// <param name="semaphore">Limits the number of concurrent file copies</param>
    /// <param name="ct">CancellationToken to stop operation if requested by the user</param>
    private static async Task CopyFolderRecursiveAsync(
        string sourcePath,
        string destPath,
        List<string> fileFilter,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        if (!Directory.Exists(sourcePath))
            return;

        Directory.CreateDirectory(destPath);

        var files = Directory.GetFiles(sourcePath);
        var fileTasks = files.Select(filePath =>
            CopySingleFileAsync(filePath, destPath, fileFilter, semaphore, ct)).ToList();

        await Task.WhenAll(fileTasks);

        var subDirs = Directory.GetDirectories(sourcePath);
        var subDirTasks = subDirs.Select(subDir =>
        {
            var subDirName = Path.GetFileName(subDir);
            var newDestPath = Path.Combine(destPath, subDirName);
            return CopyFolderRecursiveAsync(subDir, newDestPath, fileFilter, semaphore, ct);
        }).ToList();

        await Task.WhenAll(subDirTasks);
    }

    /// <summary>
    /// Copies a single file from source to destination
    /// </summary>
    /// <param name="sourceFile">The sourceFile to copy</param>
    /// <param name="destFolder">The destination to copy the file to</param>
    /// <param name="fileFilter">List of files to white/blacklist</param>
    /// <param name="semaphore">Limits the number of concurrent file copies</param>
    /// <param name="ct">CancellationToken to stop operation if requested by the user</param>
    private static async Task CopySingleFileAsync(
        string sourceFile,
        string destFolder,
        List<string> fileFilter,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var fileName = Path.GetFileName(sourceFile);
        if (fileFilter.Contains(fileName)) return;

        var destFile = Path.Combine(destFolder, fileName);

        if (File.Exists(destFile))
        {
            try
            {
                var destInfo = new FileInfo(destFile);
                destInfo.Attributes &= ~FileAttributes.ReadOnly;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to clean permissions for destination file {destFile}: {ex.Message}");
            }
        }

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
    
    /// <summary>
    /// Turns the given file path into a zip file
    /// </summary>
    /// <param name="targetPath">Target file path</param>
    /// <param name="zipPath">Path the zip is supposed to end up at</param>
    /// <param name="ct"></param>
    public static async Task ZipDir(string targetPath, string zipPath, CancellationToken ct)
    { 
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "7z",
                Arguments = $"a -mx1 \"{zipPath}\" \"{targetPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            await Task.Run(() =>
            {
                using var process = Process.Start(processStartInfo);
                process?.WaitForExit();
            }, ct);
            return;
        }
        catch (Exception ex)
        {
            Log.Warning($"7z process failed, falling back to internal dll: {ex.Message}");
        }
        
        try
        {
            if (File.Exists(zipPath))
            {
                CleanPermissions(zipPath);
                File.Delete(zipPath);
            }
            
            await Task.Run(() =>
            {
                SevenZipBase.SetLibraryPath("Libs/7z.dll");
                var compressor = new SevenZipCompressor
                {
                    CompressionLevel = SevenZip.CompressionLevel.Fast,
                    CompressionMethod = CompressionMethod.Lzma,
                    CompressionMode   = SevenZip.CompressionMode.Create,
                    IncludeEmptyDirectories = false
                };
                compressor.CompressDirectory(targetPath, zipPath);
                
            }, ct);
            return;
        }
        catch (Exception ex)
        {
            Log.Warning($"7z dll failed, falling back to .NET ZipFile: {ex.Message}");
        }

        try
        {
            if (File.Exists(zipPath))
            {
                CleanPermissions(zipPath);
                File.Delete(zipPath);
            }
        
            await Task.Run(() => ZipFile.CreateFromDirectory(targetPath, zipPath, CompressionLevel.Fastest, false), ct);

        }
        catch (Exception ex)
        {
            Log.Error($".NET ZipFile failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleans the permissions of a folder and its contents, removing read-only flags and setting full control for the current user
    /// </summary>
    /// <param name="rootFolder">Folder to clean</param>
    /// <param name="recursive">Recursive clean?</param>
    private static void CleanPermissions(string rootFolder, bool recursive = false)
    {
        var directoryInfo = new DirectoryInfo(rootFolder);
        Log.Debug("Removing read-only flag");
        directoryInfo.Attributes &= ~FileAttributes.ReadOnly;

        Task.Run(() =>
        {
            Log.Debug("Setting full control for current user on directory");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
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
                Log.Debug("Running on non-Windows");
                Process.Start("chmod", $"777 \"{rootFolder}\"");
            }
        });

        if (!recursive) return;
        
        foreach (var file in directoryInfo.GetFiles())
        {
            file.Attributes &= ~FileAttributes.ReadOnly;
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var fileSecurity = file.GetAccessControl();
                    fileSecurity.AddAccessRule(new FileSystemAccessRule(
                        WindowsIdentity.GetCurrent().Name,
                        FileSystemRights.FullControl,
                        AccessControlType.Allow));
                    file.SetAccessControl(fileSecurity);
                }
                else
                {
                    Process.Start("chmod", $"777 \"{file.FullName}\"");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating permissions for file {file.FullName}: {ex.Message}");
            }
        }
        
        foreach (var subDir in directoryInfo.GetDirectories())
        {
            CleanPermissions(subDir.FullName, true);
        }
    }
}
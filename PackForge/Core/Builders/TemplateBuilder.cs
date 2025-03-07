using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PackForge.Core.Helpers;
using Serilog;

namespace PackForge.Core.Builders;

public static class TemplateBuilder
{
    public static readonly string TemplateFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PackForge", "templates"
    );

    const string AssetsPath = "https://github.com/Satherov/PackForge/tree/master/PackForge/Assets/templates"
    
    static TemplateBuilder()
    {
        ExtractAndCopyTemplates();
    }

    private static void ExtractAndCopyTemplates()
    {
        Log.Debug("Extracting and copying templates...");

        try
        {
            if (!Validator.DirectoryExists(TemplateFolderPath, logLevel: "none"))
                Directory.CreateDirectory(TemplateFolderPath);
            var assembly = Assembly.GetExecutingAssembly();
            var templates = assembly.GetManifestResourceNames().Where(name => name.StartsWith(AssetsFolder));
            var enumerable = templates as string[] ?? templates.ToArray();
            Log.Debug($"Found {enumerable.Length} templates");

            foreach (var template in enumerable)
            {
                var fileName = template[$"{AssetsFolder}.".Length..];
                var destinationPath = Path.Combine(TemplateFolderPath, fileName);
                Log.Debug($"Extracting {template} from {fileName}");
                using var resourceStream = assembly.GetManifestResourceStream(template);
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
                resourceStream?.CopyTo(fileStream);
            }

            Log.Debug("Templates extracted and copied successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to extract templates: {ex.Message}");
        }
    }
}
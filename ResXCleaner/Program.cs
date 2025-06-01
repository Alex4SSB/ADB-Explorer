using System.Globalization;
using System.Xml.Linq;

namespace ResxCleaner;

class Program
{
    static void Main(string[] args)
    {
        // Use emoji in the console output. What could possibly go wrong?
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var solutionDir = Environment.CurrentDirectory[..^"ResX Cleaner\\bin\\Debug\\net9.0".Length];
        string baseDirectory = args.Length > 0 ? args[0] : Path.Combine(solutionDir, "ADB Explorer", "Strings");

        if (!Directory.Exists(baseDirectory))
        {
            Console.WriteLine($"❌ Directory not found: {baseDirectory}");
            return;
        }

        Console.WriteLine($"🔍 Scanning directory: {baseDirectory}");

        var resxFiles = Directory.GetFiles(baseDirectory, "*.resx", SearchOption.AllDirectories)
            .Where(IsLocalizedResx)
            .ToList();

        if (resxFiles.Count == 0)
        {
            Console.WriteLine("✅ No localized .resx files found.");
            return;
        }

        foreach (var file in resxFiles)
        {
            CleanResxFile(file);
        }

        Console.WriteLine("✅ Cleanup complete.");
    }

    static bool IsLocalizedResx(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path); // e.g., Resources.fr
        var parts = fileName.Split('.');

        if (parts.Length < 2) return false;

        try
        {
            _ = new CultureInfo(parts.Last());
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    static void CleanResxFile(string path)
    {
        try
        {
            var doc = XDocument.Load(path);

            var dataElements = doc
                .Root
                ?.Elements("data")
                ?.ToList();

            if (dataElements is null)
                return;

            int before = dataElements.Count;

            foreach (var element in dataElements.ToList())
            {
                if (string.IsNullOrWhiteSpace(element.Element("value")?.Value))
                {
                    element.Remove();
                }
            }

            doc.Save(path);

            int after = doc.Root?.Elements("data").Count() ?? 0;
            Console.WriteLine($"🧹 Cleaned {Path.GetFileName(path)}: removed {before - after} empty entries");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to process {path}: {ex.Message}");
        }
    }
}

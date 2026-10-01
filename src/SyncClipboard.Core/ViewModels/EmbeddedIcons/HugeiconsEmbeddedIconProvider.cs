using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;

namespace SyncClipboard.Core.ViewModels.EmbeddedIcons;

public static class HugeiconsEmbeddedIconProvider
{
    private const string ResourcePrefix = "SyncClipboard.Core.Assets.EmbeddedIcons.";
    private static readonly ConcurrentDictionary<EmbeddedIconCategory, string> PathDataCache = new();

    public static string ResolvePathData(ProfileType type, string[]? filePaths) =>
        GetPathData(ResolveCategory(type, filePaths));

    public static EmbeddedIconCategory ResolveCategory(ProfileType type, string[]? filePaths)
    {
        return type switch
        {
            ProfileType.Text => EmbeddedIconCategory.Text,
            ProfileType.Image => EmbeddedIconCategory.Image,
            ProfileType.Group => EmbeddedIconCategory.MultipleFiles,
            ProfileType.File => ResolveFileCategory(filePaths),
            _ => EmbeddedIconCategory.File,
        };
    }

    public static string GetPathData(EmbeddedIconCategory category) =>
        PathDataCache.GetOrAdd(category, static value => ReadPathData(value));

    private static EmbeddedIconCategory ResolveFileCategory(string[]? filePaths)
    {
        if (filePaths is not { Length: > 0 })
            return EmbeddedIconCategory.File;

        var extension = Path.GetExtension(filePaths[0]);
        return string.IsNullOrEmpty(extension)
            ? EmbeddedIconCategory.Binary
            : EmbeddedIconExtensionMap.TryResolve(extension, out var category)
                ? category
                : EmbeddedIconCategory.File;
    }

    private static string ReadPathData(EmbeddedIconCategory category)
    {
        var resourceName = ResourcePrefix + GetFileName(category);
        var assembly = typeof(HugeiconsEmbeddedIconProvider).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded Hugeicons resource: {resourceName}");

        var document = XDocument.Load(stream);
        var pathData = document.Descendants()
            .Where(element => element.Name.LocalName == "path")
            .Select(element => (string?)element.Attribute("d"))
            .Where(data => !string.IsNullOrWhiteSpace(data));
        var circleData = document.Descendants()
            .Where(element => element.Name.LocalName == "circle")
            .Select(CreateCirclePathData);
        var result = string.Join(' ', pathData.Concat(circleData));

        return !string.IsNullOrWhiteSpace(result)
            ? result
            : throw new InvalidOperationException($"Hugeicons resource has no supported geometry: {resourceName}");
    }

    private static string CreateCirclePathData(XElement element)
    {
        var cx = ParseAttribute(element, "cx");
        var cy = ParseAttribute(element, "cy");
        var radius = ParseAttribute(element, "r");
        return FormattableString.Invariant($"M {cx - radius} {cy} A {radius} {radius} 0 1 0 {cx + radius} {cy} A {radius} {radius} 0 1 0 {cx - radius} {cy}");
    }

    private static decimal ParseAttribute(XElement element, string attributeName)
    {
        var value = (string?)element.Attribute(attributeName)
            ?? throw new InvalidOperationException($"Missing {attributeName} attribute in Hugeicons resource.");
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string GetFileName(EmbeddedIconCategory category)
    {
        return category switch
        {
            EmbeddedIconCategory.File => "file-empty-02.svg",
            EmbeddedIconCategory.Binary => "file-digit.svg",
            EmbeddedIconCategory.AiModel => "ai-file.svg",
            EmbeddedIconCategory.Font => "file-type.svg",
            EmbeddedIconCategory.KeyFile => "file-locked.svg",
            EmbeddedIconCategory.Subtitle => "file-code.svg",
            EmbeddedIconCategory.DiskImage => "file-database.svg",
            EmbeddedIconCategory.Text => "text-align-left-01.svg",
            EmbeddedIconCategory.TextFile => "file-02.svg",
            EmbeddedIconCategory.Image => "image-03.svg",
            EmbeddedIconCategory.ImageFile => "file-image.svg",
            EmbeddedIconCategory.MultipleFiles => "folder-02.svg",
            EmbeddedIconCategory.Spreadsheet => "file-spreadsheet.svg",
            EmbeddedIconCategory.Presentation => "file-chart-pie.svg",
            EmbeddedIconCategory.Archive => "file-archive.svg",
            EmbeddedIconCategory.Code => "file-code.svg",
            EmbeddedIconCategory.Audio => "file-audio.svg",
            EmbeddedIconCategory.Video => "file-play.svg",
            EmbeddedIconCategory.Database => "file-database.svg",
            EmbeddedIconCategory.ThreeDimensional => "file-axis-three-d.svg",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };
    }
}

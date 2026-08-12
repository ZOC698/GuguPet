using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GuguPet;

public sealed record LanguageOption(string Code, string DisplayName);

public static class LocalizationService
{
    private const string AutomaticCode = "auto";
    private const string FallbackCulture = "en-US";
    private static readonly Dictionary<string, LanguagePack> Packs =
        new(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyDictionary<string, string> _strings =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private static CultureInfo _formatCulture = CultureInfo.GetCultureInfo(FallbackCulture);

    public static string EffectiveCulture { get; private set; } = FallbackCulture;
    public static string RequestedCulture { get; private set; } = AutomaticCode;

    public static IReadOnlyList<LanguageOption> InstalledLanguages => Packs.Values
        .OrderBy(pack => pack.Culture, StringComparer.OrdinalIgnoreCase)
        .Select(pack => new LanguageOption(pack.Culture, pack.DisplayName))
        .ToArray();

    public static void Initialize(string? requestedCulture, string? localesDirectory = null)
    {
        Packs.Clear();
        var directory = string.IsNullOrWhiteSpace(localesDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "Locales")
            : Path.GetFullPath(localesDirectory);
        if (Directory.Exists(directory))
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                TryLoadPack(path);
            }
        }

        RequestedCulture = string.IsNullOrWhiteSpace(requestedCulture)
            ? AutomaticCode
            : requestedCulture.Trim();
        var desired = RequestedCulture.Equals(AutomaticCode, StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.CurrentUICulture.Name
            : RequestedCulture;
        var selected = ResolvePack(desired) ?? ResolvePack(FallbackCulture);
        if (selected is null)
        {
            EffectiveCulture = CultureInfo.CurrentUICulture.Name;
            _formatCulture = CultureInfo.CurrentCulture;
            _strings = new Dictionary<string, string>(StringComparer.Ordinal);
            return;
        }

        EffectiveCulture = selected.Culture;
        if (IsSourceLanguage(selected.Culture) || ResolvePack(FallbackCulture) is not { } fallback)
        {
            _strings = selected.Strings;
        }
        else
        {
            var merged = new Dictionary<string, string>(fallback.Strings, StringComparer.Ordinal);
            foreach (var pair in selected.Strings) merged[pair.Key] = pair.Value;
            _strings = merged;
        }
        try { _formatCulture = CultureInfo.GetCultureInfo(selected.Culture); }
        catch (CultureNotFoundException) { _formatCulture = CultureInfo.CurrentCulture; }
    }

    public static string T(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;
        return _strings.TryGetValue(source, out var translated) && !string.IsNullOrWhiteSpace(translated)
            ? translated
            : source;
    }

    public static string F(string source, params object?[] args) =>
        string.Format(_formatCulture, T(source), args);

    public static void Apply(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        Visit(root, visited);
    }

    private static void Visit(DependencyObject node, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(node)) return;

        switch (node)
        {
            case Window window:
                window.Title = T(window.Title);
                break;
            case TextBlock textBlock:
                textBlock.Text = T(textBlock.Text);
                break;
            case Run run:
                run.Text = T(run.Text);
                break;
            case System.Windows.Controls.TextBox textBox:
                textBox.Text = T(textBox.Text);
                break;
        }

        if (node is ContentControl contentControl && contentControl.Content is string content)
            contentControl.Content = T(content);
        if (node is HeaderedContentControl headeredContent && headeredContent.Header is string contentHeader)
            headeredContent.Header = T(contentHeader);
        if (node is HeaderedItemsControl headeredItems && headeredItems.Header is string itemsHeader)
            headeredItems.Header = T(itemsHeader);
        if (node is FrameworkElement element)
        {
            if (element.ToolTip is string toolTip) element.ToolTip = T(toolTip);
            if (element.ContextMenu is not null) Visit(element.ContextMenu, visited);
        }

        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            Visit(child, visited);

        if (node is not Visual visual) return;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            Visit(VisualTreeHelper.GetChild(visual, index), visited);
    }

    private static void TryLoadPack(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) return;
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize<LanguagePackDocument>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (document is null || string.IsNullOrWhiteSpace(document.Culture) ||
                document.Strings is null) return;

            var culture = document.Culture.Trim();
            Packs[culture] = new LanguagePack(
                culture,
                string.IsNullOrWhiteSpace(document.DisplayName) ? culture : document.DisplayName.Trim(),
                new Dictionary<string, string>(document.Strings, StringComparer.Ordinal));
        }
        catch (IOException) { }
        catch (JsonException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static LanguagePack? ResolvePack(string cultureName)
    {
        if (Packs.TryGetValue(cultureName, out var exact)) return exact;

        string language;
        try { language = CultureInfo.GetCultureInfo(cultureName).TwoLetterISOLanguageName; }
        catch (CultureNotFoundException)
        {
            language = cultureName.Split('-', '_')[0];
        }

        return Packs.Values
            .OrderBy(pack => pack.Culture, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(pack =>
            {
                try
                {
                    return CultureInfo.GetCultureInfo(pack.Culture).TwoLetterISOLanguageName
                        .Equals(language, StringComparison.OrdinalIgnoreCase);
                }
                catch (CultureNotFoundException)
                {
                    return pack.Culture.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase) ||
                           pack.Culture.Equals(language, StringComparison.OrdinalIgnoreCase);
                }
            });
    }

    private static bool IsSourceLanguage(string cultureName)
    {
        try
        {
            return CultureInfo.GetCultureInfo(cultureName).TwoLetterISOLanguageName
                .Equals("zh", StringComparison.OrdinalIgnoreCase);
        }
        catch (CultureNotFoundException)
        {
            return cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record LanguagePack(
        string Culture,
        string DisplayName,
        IReadOnlyDictionary<string, string> Strings);

    private sealed class LanguagePackDocument
    {
        public string Culture { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public Dictionary<string, string>? Strings { get; set; }
    }
}

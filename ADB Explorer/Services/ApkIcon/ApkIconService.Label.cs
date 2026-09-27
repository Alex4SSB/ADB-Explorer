using AlphaOmega.Debug;

namespace ADB_Explorer.Services;

public static partial class ApkIconService
{
    private static string? TryReadPackageLabel(byte[] manifestBytes, byte[] resourcesBytes)
    {
        try
        {
            // Prefer AlphaOmega's named android:label when present — the binary resource-map
            // walker can match a wrong early element (e.g. a settings activity label).
            // Fall back to binary AXML when AO drops the attribute.
            var labelRef = FindNamedApplicationAttribute(manifestBytes, "label")
                           ?? AxmlManifestReader.TryGetApplicationAttribute(
                               manifestBytes, AxmlManifestReader.AttrLabel)
                           ?? FindLauncherActivityLabelFromBytes(manifestBytes);

            if (string.IsNullOrWhiteSpace(labelRef))
                return null;

            labelRef = labelRef.Trim();
            if (!labelRef.StartsWith('@'))
                return IsPlausibleAppLabel(labelRef) ? labelRef : null;

            if (!TryParseResourceId(labelRef, out var resourceId))
                return null;

            // Sparse-aware resolver — AlphaOmega ResourceMap is unreliable for many APKs.
            var resolved = ArscResourceResolver.ResolveString(
                resourcesBytes, resourceId, Data.Settings.ActualUICulture);
            if (!string.IsNullOrWhiteSpace(resolved) && IsPlausibleAppLabel(resolved))
                return resolved;

            // Last resort: Latin-only majority from ResourceMap (ignore non-Latin pollution).
            var arsc = new ArscFile(resourcesBytes);
            if (!arsc.ResourceMap.TryGetValue(resourceId, out var rows) || rows is null || rows.Count == 0)
                return null;

            var candidates = rows
                .Select(r => r.Value?.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v) && !v.StartsWith("res/", StringComparison.OrdinalIgnoreCase))
                .Cast<string>()
                .ToList();

            return PickBestAppLabel(candidates, requireLatin: true);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindNamedApplicationAttribute(byte[] manifestBytes, string attributeName)
    {
        try
        {
            using var manifestStream = new MemoryStream(manifestBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(manifestStream));
            return FindApplicationAttribute(axml.RootNode, attributeName);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindApplicationAttributeFromAxml(byte[] manifestBytes, string attributeName)
    {
        try
        {
            using var manifestStream = new MemoryStream(manifestBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(manifestStream));
            return FindApplicationAttribute(axml.RootNode, attributeName)
                   ?? (attributeName.Equals("label", StringComparison.OrdinalIgnoreCase)
                       ? FindLauncherActivityLabel(axml.RootNode)
                       : null);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindLauncherActivityLabelFromBytes(byte[] manifestBytes)
    {
        try
        {
            using var manifestStream = new MemoryStream(manifestBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(manifestStream));
            return FindLauncherActivityLabel(axml.RootNode);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Some apps omit <c>android:label</c> on <c>&lt;application&gt;</c>
    /// and only label the MAIN/LAUNCHER activity. Also accept nameless <c>@7F12…</c> string refs
    /// when AlphaOmega drops the attribute name.
    /// </summary>
    private static string? FindLauncherActivityLabel(XmlNode? root)
    {
        if (root is null)
            return null;

        foreach (var activity in EnumerateNodesNamed(root, "activity", "activity-alias"))
        {
            if (!ActivityHasLauncherIntent(activity))
                continue;

            var label = GetAttributeValue(activity, "label");
            if (!string.IsNullOrWhiteSpace(label))
                return label;

            // AlphaOmega sometimes drops the attribute name — accept string refs or literals.
            foreach (var value in EnumerateAllAttributeValues(activity))
            {
                if (value.StartsWith('@') && TryParseResourceId(value, out var id))
                {
                    // Prefer string resources (type 0x12 / 0x13 typical) over drawables.
                    var type = (id >> 16) & 0xFF;
                    if (type is >= 0x0B and <= 0x14)
                        return value;
                }

                if (IsPlausibleAppLabel(value)
                    && !value.Contains('.', StringComparison.Ordinal)
                    && !value.StartsWith('@'))
                    return value;
            }
        }

        return null;
    }

    private static bool ActivityHasLauncherIntent(XmlNode activity)
    {
        if (activity.ChildNodes is null)
            return false;

        foreach (var children in activity.ChildNodes.Values)
        {
            foreach (var child in children)
            {
                if (!child.NodeName.Equals("intent-filter", StringComparison.OrdinalIgnoreCase))
                    continue;

                var hasMain = false;
                var hasLauncher = false;
                foreach (var intentChild in EnumerateChildNodes(child))
                {
                    var name = GetAttributeValue(intentChild, "name") ?? "";
                    if (intentChild.NodeName.Equals("action", StringComparison.OrdinalIgnoreCase)
                        && name.Equals("android.intent.action.MAIN", StringComparison.OrdinalIgnoreCase))
                        hasMain = true;
                    if (intentChild.NodeName.Equals("category", StringComparison.OrdinalIgnoreCase)
                        && name.Equals("android.intent.category.LAUNCHER", StringComparison.OrdinalIgnoreCase))
                        hasLauncher = true;
                }

                if (hasMain && hasLauncher)
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<XmlNode> EnumerateNodesNamed(XmlNode root, params string[] names)
    {
        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<XmlNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (set.Contains(node.NodeName ?? ""))
                yield return node;

            if (node.ChildNodes is null)
                continue;

            foreach (var children in node.ChildNodes.Values)
            {
                foreach (var child in children)
                    stack.Push(child);
            }
        }
    }

    private static IEnumerable<XmlNode> EnumerateChildNodes(XmlNode node)
    {
        if (node.ChildNodes is null)
            yield break;

        foreach (var children in node.ChildNodes.Values)
        {
            foreach (var child in children)
                yield return child;
        }
    }

    private static IEnumerable<string> EnumerateAllAttributeValues(XmlNode node)
    {
        if (node.Attributes is null)
            yield break;

        foreach (var (_, values) in node.Attributes)
        {
            if (values is null)
                continue;
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }
    }

    /// <summary>
    /// AlphaOmega's ResourceMap often lists every locale (and sometimes polluted values) for one id.
    /// Prefer a stable Latin/default display name and reject format strings / class names.
    /// </summary>
    private static string? PickBestAppLabel(IReadOnlyList<string> candidates, bool requireLatin = false)
    {
        var valid = candidates.Where(IsPlausibleAppLabel).ToList();
        if (valid.Count == 0)
            return null;

        var latin = valid.Where(IsMostlyLatin).ToList();
        if (requireLatin && latin.Count == 0)
            return null;

        var pool = latin.Count > 0 ? latin : valid;

        return pool
            .GroupBy(s => s, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Length)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    private static bool IsPlausibleAppLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();
        if (value.Length is < 1 or > 80)
            return false;

        // Format strings / placeholders (Play services prompts, etc.)
        if (value.Contains('%', StringComparison.Ordinal))
            return false;

        if (value.StartsWith("res/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (value.Length <= 2)
            return false;

        // Resource typed-value mistakes (e.g. "65536").
        if (value.Length <= 8 && value.All(char.IsAsciiDigit))
            return false;

        // Boolean attrs misread as labels ("true").
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no", StringComparison.OrdinalIgnoreCase))
            return false;

        // Fully-qualified Java/Kotlin type names mistakenly mapped under the label id.
        var dotParts = value.Split('.');
        if (dotParts.Length >= 3
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '$')
            && dotParts[0] is "com" or "org" or "net" or "io" or "android" or "java" or "kotlin")
            return false;

        if (value.Contains("android.", StringComparison.OrdinalIgnoreCase)
            && value.Contains('.', StringComparison.Ordinal)
            && !value.Contains(' ', StringComparison.Ordinal))
            return false;

        // Do not reject PascalCase tokens — system overlays often ship the resource name
        // as the English label (SetupWizardOverlay). Filtering them sent ResolveString
        // into ResourceMap fishing, which preferred spaced Latin (e.g. Estonian).

        return true;
    }

    private static bool IsMostlyLatin(string value)
    {
        var letters = 0;
        var latin = 0;
        foreach (var c in value)
        {
            if (!char.IsLetter(c))
                continue;

            letters++;
            if (c <= 0x024F) // Basic Latin + Latin Extended
                latin++;
        }

        return letters == 0 || latin * 2 >= letters;
    }

    private static string? FindApplicationAttribute(XmlNode? root, string attributeName)
    {
        if (root is null)
            return null;

        if (root.NodeName.Equals("application", StringComparison.OrdinalIgnoreCase))
            return GetAttributeValue(root, attributeName);

        if (root.ChildNodes is null)
            return null;

        foreach (var children in root.ChildNodes.Values)
        {
            foreach (var child in children)
            {
                var found = FindApplicationAttribute(child, attributeName);
                if (found is not null)
                    return found;
            }
        }

        return null;
    }

    private static string? GetAttributeValue(XmlNode node, string attributeName)
    {
        foreach (var value in EnumerateAttributeValues(node, attributeName))
            return value;

        return null;
    }

    private static IEnumerable<string> EnumerateAttributeValues(XmlNode node, string attributeName)
    {
        if (node.Attributes is null)
            yield break;

        foreach (var (key, values) in node.Attributes)
        {
            if (!AttributeNameMatches(key, attributeName) || values is null)
                continue;

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }
    }

    private static bool AttributeNameMatches(string key, string attributeName)
    {
        if (key.Equals(attributeName, StringComparison.OrdinalIgnoreCase))
            return true;

        // android:icon / {http://schemas.android.com/apk/res/android}icon
        var suffix = ":" + attributeName;
        return key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
               || key.EndsWith("}" + attributeName, StringComparison.OrdinalIgnoreCase);
    }
}

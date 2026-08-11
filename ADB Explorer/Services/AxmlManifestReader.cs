using System.Globalization;

namespace ADB_Explorer.Services;

/// <summary>
/// Reads selected <c>&lt;application&gt;</c> attributes from binary AndroidManifest AXML.
/// AlphaOmega often drops attribute names when the manifest uses the XML resource-id map
/// (nameless theme + missing label).
/// </summary>
internal static class AxmlManifestReader
{
    private const uint AxmlMagic = 0x00080003;
    private const ushort ResStringPoolType = 0x0001;
    private const ushort ResXmlResourceMapType = 0x0180;
    private const ushort ResXmlStartElementType = 0x0102;
    private const ushort ResXmlEndElementType = 0x0103;

    // android.R.attr.*
    public const int AttrLabel = 0x01010001;
    public const int AttrIcon = 0x01010002;

    private const byte DataTypeReference = 0x01;
    private const byte DataTypeString = 0x03;
    private const byte DataTypeIntBoolean = 0x12;

    public static string? TryGetApplicationAttribute(byte[] axmlBytes, int androidAttrId)
    {
        if (axmlBytes.Length < 16 || BitConverter.ToUInt32(axmlBytes, 0) != AxmlMagic)
            return null;

        try
        {
            if (!TryReadPools(axmlBytes, out var strings, out var attrIds, out var nodesPos))
                return null;

            var depth = 0;
            var inApplication = false;
            var pos = nodesPos;

            while (pos + 8 <= axmlBytes.Length)
            {
                var type = BitConverter.ToUInt16(axmlBytes, pos);
                var headerSize = BitConverter.ToUInt16(axmlBytes, pos + 2);
                var size = BitConverter.ToInt32(axmlBytes, pos + 4);
                if (size < 8 || pos + size > axmlBytes.Length)
                    break;

                if (type == ResXmlStartElementType && headerSize >= 16)
                {
                    var nameIdx = BitConverter.ToInt32(axmlBytes, pos + 20);
                    var name = GetString(strings, nameIdx);
                    var isApplication = name.Equals("application", StringComparison.OrdinalIgnoreCase);

                    var attrStart = BitConverter.ToUInt16(axmlBytes, pos + 24);
                    var attrSize = BitConverter.ToUInt16(axmlBytes, pos + 26);
                    var attrCount = BitConverter.ToUInt16(axmlBytes, pos + 28);
                    // attributeStart is relative to ResXMLTree_attrExt (ns at pos+16).
                    var attrsBase = pos + 16 + attrStart;

                    string? matchedValue = null;
                    var hasIcon = false;
                    var hasLabel = false;

                    if (attrSize >= 20 && attrCount > 0)
                    {
                        for (var i = 0; i < attrCount; i++)
                        {
                            var ap = attrsBase + i * attrSize;
                            if (ap + 20 > pos + size)
                                break;

                            var attrNameIdx = BitConverter.ToInt32(axmlBytes, ap + 4);
                            var rawValue = BitConverter.ToInt32(axmlBytes, ap + 8);
                            var dataType = axmlBytes[ap + 15];
                            var data = BitConverter.ToUInt32(axmlBytes, ap + 16);

                            var attrId = attrNameIdx >= 0 && attrNameIdx < attrIds.Length
                                ? attrIds[attrNameIdx]
                                : 0;
                            var attrName = GetString(strings, attrNameIdx);

                            if (attrId == AttrIcon
                                || attrName.Equals("icon", StringComparison.OrdinalIgnoreCase))
                                hasIcon = true;
                            if (attrId == AttrLabel
                                || attrName.Equals("label", StringComparison.OrdinalIgnoreCase))
                                hasLabel = true;

                            var matches = attrId == androidAttrId
                                          || attrName.Equals(
                                              androidAttrId == AttrLabel ? "label" : "icon",
                                              StringComparison.OrdinalIgnoreCase);
                            if (!matches)
                                continue;

                            matchedValue = dataType switch
                            {
                                DataTypeReference => "@" + data.ToString("X8", CultureInfo.InvariantCulture),
                                DataTypeString => rawValue >= 0
                                    ? GetString(strings, rawValue)
                                    : GetString(strings, (int)data),
                                // Never treat booleans as label/icon.
                                DataTypeIntBoolean => null,
                                _ => null,
                            };
                        }
                    }

                    // AlphaOmega-style manifests may mis-index element names; treat a node that
                    // carries both icon and label as <application> — but only at shallow depth
                    // and only when no better named match was found yet. Prefer the deepest
                    // application-like node by continuing until we see a real "application" name
                    // when string indices are reliable.
                    if (!isApplication && hasIcon && hasLabel && depth <= 1)
                        isApplication = true;

                    if (isApplication && !string.IsNullOrWhiteSpace(matchedValue))
                        return matchedValue;

                    if (isApplication)
                        inApplication = true;

                    // Once inside <application>, also accept a late-resolved attr (rare).
                    if (inApplication && !isApplication && !string.IsNullOrWhiteSpace(matchedValue))
                        return matchedValue;

                    depth++;
                }
                else if (type == ResXmlEndElementType)
                {
                    depth--;
                    if (inApplication && depth <= 1)
                    {
                        inApplication = false;
                        // Do not return null — a wrong early match used to bail here.
                        // Keep scanning for a real <application> if we haven't returned yet.
                    }
                }

                pos += size;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool TryReadPools(
        byte[] data,
        out List<string> strings,
        out int[] attrIds,
        out int nodesPos)
    {
        strings = [];
        attrIds = [];
        nodesPos = data.Length;

        var pos = 8;
        while (pos + 8 <= data.Length)
        {
            var type = BitConverter.ToUInt16(data, pos);
            var headerSize = BitConverter.ToUInt16(data, pos + 2);
            var size = BitConverter.ToInt32(data, pos + 4);
            if (size < 8 || pos + size > data.Length)
                return false;

            if (type == ResStringPoolType)
            {
                strings = AxmlStringPool.ReadAll(data);
            }
            else if (type == ResXmlResourceMapType)
            {
                var count = (size - 8) / 4;
                if (count > 0)
                {
                    attrIds = new int[count];
                    for (var i = 0; i < count; i++)
                        attrIds[i] = BitConverter.ToInt32(data, pos + 8 + i * 4);
                }
            }
            else if (type is >= 0x0100 and <= 0x017F)
            {
                nodesPos = pos;
                return strings.Count > 0;
            }

            pos += size;
        }

        return false;
    }

    private static string GetString(List<string> strings, int index)
    {
        if (index < 0 || index >= strings.Count)
            return "";
        return strings[index] ?? "";
    }
}

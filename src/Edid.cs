using Microsoft.Win32;

namespace AleVista;

/// <summary>
/// Erkennt denselben physischen Bildschirm auf verschiedenen PCs: Hersteller/Modell und Seriennummer aus
/// den EDID-Daten des Monitors (HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\…\Device Parameters\EDID).
/// </summary>
internal static class Edid
{
    static readonly Dictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>z. B. „GSM5B09:0001E240:204NTXR1A123“ oder null, wenn unbekannt.</summary>
    public static string? PhysicalKey(string monitorId)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(monitorId, out var k)) return k;
            k = Read(monitorId);
            Cache[monitorId] = k;
            return k;
        }
    }

    static string? Read(string monitorId)
    {
        try
        {
            // \\?\DISPLAY#GSM5B09#5&2b0d1234&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}
            var parts = monitorId.TrimStart('\\', '?').Split('#');
            if (parts.Length < 3 || !parts[0].Equals("DISPLAY", StringComparison.OrdinalIgnoreCase)) return null;
            string model = parts[1].ToUpperInvariant();
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters");
            if (key?.GetValue("EDID") is not byte[] edid || edid.Length < 128) return model;

            uint serial = BitConverter.ToUInt32(edid, 12);
            string serialText = "";
            for (int off = 54; off <= 108; off += 18)
            {
                if (edid[off] == 0 && edid[off + 1] == 0 && edid[off + 3] == 0xFF)
                {
                    serialText = new string(edid.Skip(off + 5).Take(13).Select(b => (char)b).ToArray())
                        .Split('\n')[0].Trim();
                    break;
                }
            }
            return $"{model}:{serial:X8}:{serialText}";
        }
        catch
        {
            return null;
        }
    }
}

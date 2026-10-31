using System.Text;
using System.Text.Json;

namespace ResourceManager.Core;

public sealed record ExportTable(string Scope, string[] Columns, string[][] Rows);

public static class SnapshotExport
{
    public static string Json(ExportTable table) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1, capturedAt = DateTimeOffset.UtcNow,
        scope = table.Scope, columns = table.Columns, rows = table.Rows
    }, new JsonSerializerOptions { WriteIndented = true });

    public static string Csv(ExportTable table)
    {
        var result = new StringBuilder();
        result.AppendLine(string.Join(",", table.Columns.Select(Escape)));
        foreach (var row in table.Rows) result.AppendLine(string.Join(",", row.Select(Escape)));
        return result.ToString();
    }

    private static string Escape(string value)
    {
        // Process names and paths can be untrusted. Keep spreadsheet imports as text.
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

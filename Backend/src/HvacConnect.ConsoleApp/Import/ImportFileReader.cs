using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace HvacConnect.ConsoleApp;

internal static class ImportFileReader
{
    public static async Task<List<Dictionary<string, string>>> ReadAsync(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csv" => ParseCsv(await File.ReadAllTextAsync(path, Encoding.UTF8)),
            ".xlsx" => ReadXlsx(path),
            _ => throw new InvalidDataException("Supported formats are CSV and Excel .xlsx.")
        };
    }

    private static List<Dictionary<string, string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0) quoted = true;
            else if (c == ',') { row.Add(field.ToString().Trim()); field.Clear(); }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString().Trim()); field.Clear();
                if (row.Any(x => x.Length > 0)) rows.Add(row);
                row = new List<string>();
            }
            else field.Append(c);
        }
        if (quoted) throw new InvalidDataException("CSV contains an unclosed quoted field.");
        row.Add(field.ToString().Trim());
        if (row.Any(x => x.Length > 0)) rows.Add(row);
        return ToRecords(rows);
    }

    private static List<Dictionary<string, string>> ReadXlsx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pr = "http://schemas.openxmlformats.org/package/2006/relationships";
        var workbook = XDocument.Load(zip.GetEntry("xl/workbook.xml")!.Open());
        string? id = (string?)workbook.Root?.Element(x + "sheets")?.Elements(x + "sheet").FirstOrDefault()?.Attribute(r + "id");
        var rels = XDocument.Load(zip.GetEntry("xl/_rels/workbook.xml.rels")!.Open());
        string? target = (string?)rels.Root?.Elements(pr + "Relationship").FirstOrDefault(e => (string?)e.Attribute("Id") == id)?.Attribute("Target");
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidDataException("The workbook has no readable worksheet.");
        string sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target.Replace("../", "");
        var sheet = XDocument.Load(zip.GetEntry(sheetPath)!.Open());
        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is { } sharedEntry)
            shared = XDocument.Load(sharedEntry.Open()).Root?.Elements(x + "si").Select(si => string.Concat(si.Descendants(x + "t").Select(t => t.Value))).ToList() ?? new();

        var rows = new List<List<string>>();
        foreach (var xmlRow in sheet.Descendants(x + "sheetData").Elements(x + "row"))
        {
            var cells = new List<string>();
            foreach (var cell in xmlRow.Elements(x + "c"))
            {
                string reference = (string?)cell.Attribute("r") ?? "A1";
                int col = 0;
                foreach (char c in reference.TakeWhile(char.IsLetter)) col = col * 26 + char.ToUpperInvariant(c) - 'A' + 1;
                col = Math.Max(0, col - 1);
                while (cells.Count <= col) cells.Add(string.Empty);
                string raw = (string?)cell.Element(x + "v") ?? string.Empty;
                string value = (string?)cell.Attribute("t") switch
                {
                    "s" when int.TryParse(raw, out int index) && index >= 0 && index < shared.Count => shared[index],
                    "inlineStr" => string.Concat(cell.Descendants(x + "t").Select(t => t.Value)),
                    "b" => raw == "1" ? "true" : "false",
                    _ => raw
                };
                cells[col] = value.Trim();
            }
            rows.Add(cells);
        }
        return ToRecords(rows);
    }

    private static List<Dictionary<string, string>> ToRecords(List<List<string>> rows)
    {
        if (rows.Count < 2) throw new InvalidDataException("File needs a header row and at least one data row.");
        var headers = rows[0].Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"Column{i + 1}" : h.Trim()).ToList();
        if (headers.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Count)
            throw new InvalidDataException("Duplicate column headings found; rename them before importing.");
        return rows.Skip(1).Select(row => headers.Select((header, i) => (header, value: i < row.Count ? row[i].Trim() : ""))
            .Where(x => x.value.Length > 0).ToDictionary(x => x.header, x => x.value, StringComparer.OrdinalIgnoreCase))
            .Where(row => row.Count > 0).ToList();
    }

    private static string Normalize(string text) => new(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}

using System.Text;

namespace HvacConnect.ConsoleApp;

public static class TableFormatter
{
    public static void PrintTable(string title, string[] headers, List<string[]> rows, int[]? customWidths = null)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n─── {title.ToUpper()} ───");
        Console.ResetColor();

        if (rows == null || rows.Count == 0)
        {
            Console.WriteLine("  (No records found in current scope)");
            return;
        }

        int colCount = headers.Length;
        int[] widths = new int[colCount];

        for (int i = 0; i < colCount; i++)
        {
            int maxLen = headers[i].Length;
            foreach (var row in rows)
            {
                if (i < row.Length && row[i] != null)
                {
                    maxLen = Math.Max(maxLen, row[i].Length);
                }
            }
            widths[i] = customWidths != null && i < customWidths.Length ? customWidths[i] : Math.Min(maxLen + 2, 40);
        }

        // Top Border
        Console.WriteLine("┌" + string.Join("┬", widths.Select(w => new string('─', w + 2))) + "┐");

        // Headers
        var headerCells = headers.Select((h, i) => $" {h.PadRight(widths[i])} ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("│" + string.Join("│", headerCells) + "│");
        Console.ResetColor();

        // Divider
        Console.WriteLine("├" + string.Join("┼", widths.Select(w => new string('─', w + 2))) + "┤");

        // Data Rows
        foreach (var row in rows)
        {
            var cells = new string[colCount];
            for (int i = 0; i < colCount; i++)
            {
                string val = i < row.Length && row[i] != null ? row[i] : "";
                if (val.Length > widths[i])
                {
                    val = val.Substring(0, widths[i] - 3) + "...";
                }
                cells[i] = $" {val.PadRight(widths[i])} ";
            }
            Console.WriteLine("│" + string.Join("│", cells) + "│");
        }

        // Bottom Border
        Console.WriteLine("└" + string.Join("┴", widths.Select(w => new string('─', w + 2))) + "┘");
    }
}

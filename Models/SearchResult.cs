using System;
using System.IO;

namespace CoderCommander.Models;

/// <summary>
/// Результат поиска по содержимому (grep, ph2.1 / exp.yml).
/// A single content-search result (grep, ph2.1).
/// Содержит путь, имя, размер и (для поиска по содержимому) совпавшую строку.
/// Holds the path, name, size and (for content search) the matched line.
/// </summary>
public sealed class SearchResult
{
    /// <summary>Полный путь к файлу. / Full path to the file.</summary>
    public string FullPath { get; }

    /// <summary>Имя файла без пути. / File name without path.</summary>
    public string Name { get; }

    /// <summary>Размер файла в байтах. / File size in bytes.</summary>
    public long Size { get; }

    /// <summary>
    /// Совпавшая строка (для поиска по содержимому). <c>null</c> — при поиске только по имени.
    /// Matched line (content search). null for name-only search.
    /// </summary>
    public string? MatchLine { get; }

    /// <summary>
    /// Номер строки (1-based); 0 — при поиске только по имени.
    /// 1-based line number; 0 for name-only search.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>Кодировка, в которой прочитан файл (WebName). / Encoding the file was read with (WebName).</summary>
    public string? EncodingName { get; }

    public SearchResult(string fullPath, long size, string? matchLine = null, int lineNumber = 0, string? encodingName = null)
    {
        FullPath = fullPath;
        Name = Path.GetFileName(fullPath);
        Size = size;
        MatchLine = matchLine;
        LineNumber = lineNumber;
        EncodingName = encodingName;
    }

    /// <summary>Размер в человекочитаемом виде. / Human-readable size.</summary>
    public string SizeDisplay
    {
        get
        {
            string[] u = ["B", "KB", "MB", "GB", "TB"];
            double s = Size;
            int i = 0;
            while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
            return $"{s:0.##} {u[i]}";
        }
    }
}

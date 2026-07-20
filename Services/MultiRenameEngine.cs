using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CoderCommander.Services;

/// <summary>
/// Стиль регистра имени файла. / Filename case style.
/// 0 — без изменений, 1 — ВЕРХНИЙ, 2 — нижний, 3 — Заглавные Буквы.
/// 0 — as-is, 1 — UPPERCASE, 2 — lowercase, 3 — Title Case.
/// </summary>
public enum NameStyle
{
    AsIs = 0,
    Upper = 1,
    Lower = 2,
    TitleCase = 3,
}

/// <summary>
/// Движок масок мульти-переименования (ph2.3, exp.yml).
/// Multi-rename mask engine (ph2.3).
/// Реализует собственный простой сканер токенов (НЕ формальная грамматика) и
/// разрешение коллизий с двухпроходным переименованием через временные имена.
/// Implements a custom simple token scanner (not a formal grammar) plus collision
/// resolution with a two-pass rename via temporary names.
/// BCL-only: System.IO, System.Text.RegularExpressions.
/// </summary>
public sealed class MultiRenameEngine
{
    /// <summary>Описание исходного элемента (без привязки к UI). / Source entry description (UI-agnostic).</summary>
    public sealed record SourceFile(
        string FullPath,
        string Name,                // имя с расширением / name with extension
        string NameWithoutExtension,// имя без расширения / name without extension
        string Extension,           // расширение БЕЗ точки / extension without dot
        DateTime Modified,          // локальное время изменения / local modified time
        bool IsDirectory);

    /// <summary>Элемент плана переименования. / A rename plan item.</summary>
    public sealed record RenamePlanItem(SourceFile Source, string OriginalName, string NewName, bool Changed);

    /// <summary>Результат применения плана. / Result of applying the plan.</summary>
    public sealed record RenameResult(int Renamed, int Failed, IReadOnlyList<string> Errors)
    {
        public bool Success => Failed == 0;
    }

    /// <summary>Результат одного элемента плана. / Per-item plan result.</summary>
    public sealed record RenameDetail(RenamePlanItem Item, bool Success, string? Error);

    /// <summary>Параметры переименования. / Rename options.</summary>
    public sealed class RenameOptions
    {
        public string Mask { get; set; } = "[N] ([C]).[E]";
        public bool UseRegex { get; set; }
        public string Find { get; set; } = "";
        public string Replace { get; set; } = "";
        public bool CaseSensitive { get; set; }
        public int CounterStart { get; set; } = 1;
        public int CounterStep { get; set; } = 1;
        public int CounterWidth { get; set; } = 0;
        public NameStyle Style { get; set; }
        public string BadCharReplacement { get; set; } = "_";
        public bool EnableLogging { get; set; }
        public string LogPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoderCommander", "rename_log.txt");
    }

    /// <summary>Карта токенов даты/времени → формат .NET. / Date/time token → .NET format map.</summary>
    private static readonly Dictionary<string, string> DateFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Y"] = "yyyy", ["YY"] = "yy",
        ["M"] = "M",     ["MM"] = "MM",
        ["D"] = "d",     ["DD"] = "dd",
        ["h"] = "H",     ["hh"] = "HH",
        ["n"] = "m",     ["nn"] = "mm",
        ["s"] = "s",     ["ss"] = "ss",
    };

    /// <summary>
    /// Извлекает имена переменных [V:name] из маски (для отображения полей ввода).
    /// Extracts [V:name] variable names from the mask (to render input fields).
    /// </summary>
    public static IReadOnlyList<string> ExtractVariables(string mask)
    {
        var list = new List<string>();
        int i = 0;
        while (i < mask.Length)
        {
            if (mask[i] == '[')
            {
                int close = mask.IndexOf(']', i);
                if (close < 0) break;
                var token = mask.Substring(i + 1, close - i - 1);
                if (token.StartsWith("V:", StringComparison.OrdinalIgnoreCase))
                {
                    var name = token.Substring(2);
                    if (!string.IsNullOrWhiteSpace(name) &&
                        !list.Contains(name, StringComparer.OrdinalIgnoreCase))
                        list.Add(name);
                }
                i = close + 1;
            }
            else i++;
        }
        return list;
    }

    /// <summary>
    /// Строит план переименования с разрешением коллизий (HashSet целевых имён).
    /// Builds the rename plan with collision resolution (HashSet of target names).
    /// </summary>
    /// <param name="files">Выделенные элементы (все в одной папке). / Selected items (all in one folder).</param>
    /// <param name="opts">Параметры масок/режима. / Mask/mode options.</param>
    /// <param name="variables">Значения переменных [V:name]. / Variable values [V:name].</param>
    public static List<RenamePlanItem> BuildPlan(
        IReadOnlyList<SourceFile> files,
        RenameOptions opts,
        IReadOnlyDictionary<string, string> variables)
    {
        var result = new List<RenamePlanItem>();
        if (files.Count == 0) return result;

        var dir = Path.GetDirectoryName(files[0].FullPath) ?? "";

        // Имена, уже существующие в папке (внешние — не из выборки).
        // Names already existing in the folder (external — not from the selection).
        var existingAll = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in Directory.EnumerateFileSystemEntries(dir))
                existingAll.Add(Path.GetFileName(f));
        }
        catch { /* папка недоступна — пропускаем внешнюю проверку */ }

        // Исходные имена выборки освободятся после переименования → не блокируют.
        // Originals from the selection will be freed → do not block.
        var selectedOriginals = new HashSet<string>(files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        var blocked = new HashSet<string>(existingAll, StringComparer.OrdinalIgnoreCase);
        foreach (var o in selectedOriginals) blocked.Remove(o);

        var usedFinal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int counter = opts.CounterStart;

        foreach (var file in files)
        {
            string desired = opts.UseRegex
                ? ApplyRegex(file.Name, opts)
                : Expand(opts.Mask, file, ref counter, opts, variables, Guid.NewGuid());

            // Стиль регистра имени (только для масочного режима).
            // Name style transform (mask mode only).
            if (!opts.UseRegex && opts.Style != NameStyle.AsIs && !file.IsDirectory)
                desired = ApplyNameStyle(desired, opts.Style);

            // Замена запрещённых символов. / Replace forbidden characters.
            if (!file.IsDirectory)
                desired = SanitizeFileName(desired, opts.BadCharReplacement);

            string finalName = ResolveName(desired, file.Name, usedFinal, blocked);
            usedFinal.Add(finalName);
            bool changed = !string.Equals(finalName, file.Name, StringComparison.Ordinal);
            result.Add(new RenamePlanItem(file, file.Name, finalName, changed));

            if (!opts.UseRegex) counter += opts.CounterStep;
        }

        return result;
    }

    /// <summary>
    /// Применяет план: двухпроходное переименование (temp-имена → финальные).
    /// Applies the plan: two-pass rename (temp names → final names).
    /// </summary>
    public static (RenameResult Result, IReadOnlyList<RenameDetail> Details) Apply(IReadOnlyList<RenamePlanItem> plan)
    {
        var errors = new List<string>();
        var details = new List<RenameDetail>();
        int done = 0;

        // Проход 1: переименование в уникальные временные имена.
        // Pass 1: rename to unique temporary names.
        var temps = new List<(RenamePlanItem item, string? tempPath)>();
        var failSet = new HashSet<RenamePlanItem>();
        foreach (var it in plan)
        {
            if (!it.Changed) { temps.Add((it, it.Source.FullPath)); continue; }

            var dir = Path.GetDirectoryName(it.Source.FullPath) ?? "";
            string? temp = null;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                var candidate = Path.Combine(dir, $"{it.OriginalName}.cctmp-{Guid.NewGuid():N}");
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) { temp = candidate; break; }
            }
            if (temp is null)
            {
                var msg = $"{it.OriginalName}: не удалось создать временное имя";
                errors.Add(msg); failSet.Add(it); temps.Add((it, null)); continue;
            }

            try
            {
                if (it.Source.IsDirectory) Directory.Move(it.Source.FullPath, temp);
                else File.Move(it.Source.FullPath, temp);
                temps.Add((it, temp));
            }
            catch (Exception ex)
            {
                errors.Add($"{it.OriginalName}: {ex.Message}");
                failSet.Add(it); temps.Add((it, null));
            }
        }

        // Проход 2: переименование temp → финальное имя.
        // Pass 2: rename temp → final name.
        foreach (var (item, tempPath) in temps)
        {
            if (tempPath is null) continue;
            if (!item.Changed) { done++; continue; }

            var dir = Path.GetDirectoryName(item.Source.FullPath) ?? "";
            var finalPath = Path.Combine(dir, item.NewName);
            try
            {
                if (item.Source.IsDirectory) Directory.Move(tempPath, finalPath);
                else File.Move(tempPath, finalPath);
                done++;
            }
            catch (Exception ex)
            {
                // Пытаемся восстановить исходное имя из temp.
                // Try to restore the original name from temp.
                try
                {
                    var restore = Path.Combine(dir, item.OriginalName);
                    if (!File.Exists(restore) && !Directory.Exists(restore))
                    {
                        if (item.Source.IsDirectory) Directory.Move(tempPath, restore);
                        else File.Move(tempPath, restore);
                    }
                }
                catch { /* оставляем как temp */ }
                var msg = $"{item.OriginalName} → {item.NewName}: {ex.Message}";
                errors.Add(msg); failSet.Add(item);
            }
        }

        // Формируем детализацию. / Build per-item details.
        foreach (var it in plan)
        {
            bool ok = !failSet.Contains(it);
            string? err = ok ? null : errors.FirstOrDefault(e => e.StartsWith(it.OriginalName, StringComparison.Ordinal));
            details.Add(new RenameDetail(it, ok, err));
        }

        return (new RenameResult(done, plan.Count - done, errors), details);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Сканер маски / Mask scanner
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Раскрывает маску в имя для одного файла. / Expands the mask into a name for one file.
    /// </summary>
    private static string Expand(
        string mask, SourceFile file, ref int counter, RenameOptions opts,
        IReadOnlyDictionary<string, string> variables, Guid guid)
    {
        var sb = new StringBuilder(mask.Length * 2);
        int i = 0;
        while (i < mask.Length)
        {
            char c = mask[i];
            if (c == '[')
            {
                int close = mask.IndexOf(']', i);
                if (close < 0) { sb.Append(c); i++; continue; }
                string token = mask.Substring(i + 1, close - i - 1);
                sb.Append(ExpandToken(token, file, ref counter, opts, variables, guid));
                i = close + 1;
            }
            else { sb.Append(c); i++; }
        }
        return sb.ToString();
    }

    /// <summary>Раскрывает один токен. / Expands a single token.</summary>
    private static string ExpandToken(
        string token, SourceFile file, ref int counter, RenameOptions opts,
        IReadOnlyDictionary<string, string> variables, Guid guid)
    {
        if (token.Length == 0) return "";

        // Переменная: [V:name]
        if (token.StartsWith("V:", StringComparison.OrdinalIgnoreCase))
        {
            var name = token.Substring(2);
            return variables.TryGetValue(name, out var v) ? v : "";
        }

        switch (token[0])
        {
            case 'N': return Substring(file.NameWithoutExtension, token.Substring(1));
            case 'E': return Substring(file.Extension, token.Substring(1));
            case 'C': return FormatCounter(counter, opts.CounterWidth);
            case 'G': return guid.ToString("N");
            case 'Y':
            case 'M':
            case 'D':
            case 'h':
            case 'n':
            case 's': return DatePart(token, file.Modified);
            default: return ""; // неизвестный токен → пусто / unknown token → empty
        }
    }

    /// <summary>
    /// Подстрока по индексам: [Nx] (до конца), [Nx:y] (включительно).
    /// Отрицательные индексы считают от конца: -1 = последний символ.
    /// Substring by indices: [Nx] (to end), [Nx:y] (inclusive).
    /// Negative indices count from end: -1 = last char.
    /// </summary>
    private static string Substring(string value, string range)
    {
        if (string.IsNullOrEmpty(range)) return value;

        int start = 1, end = value.Length;
        try
        {
            int colon = range.IndexOf(':');
            if (colon < 0)
            {
                start = ParseIndex(range, value.Length);
                end = value.Length;
            }
            else
            {
                start = ParseIndex(range.Substring(0, colon), value.Length);
                end = ParseIndex(range.Substring(colon + 1), value.Length);
            }
        }
        catch { return value; }

        if (start < 1) start = 1;
        if (end > value.Length) end = value.Length;
        if (start > end) return "";
        return value.Substring(start - 1, end - start + 1);
    }

    /// <summary>
    /// Парсит 1-базисный индекс с поддержкой отрицательных значений.
    /// Parses a 1-based index with support for negative values.
    /// Отрицательные: -1 = последний, -2 = предпоследний и т.д.
    /// Negative: -1 = last, -2 = second-to-last, etc.
    /// </summary>
    private static int ParseIndex(string text, int length)
    {
        int raw = int.Parse(text.Trim(), CultureInfo.InvariantCulture);
        if (raw < 0) return length + raw + 1; // -1 → last, -2 → second-to-last
        return raw;
    }

    /// <summary>Форматирует счётчик с дополнением нулями. / Formats the counter with zero padding.</summary>
    private static string FormatCounter(int value, int width)
    {
        if (width > 0) return value.ToString(new string('0', width), CultureInfo.InvariantCulture);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Преобразует токен даты/времени в строку. / Converts a date/time token to a string.</summary>
    private static string DatePart(string token, DateTime dt)
        => DateFormats.TryGetValue(token, out var fmt) ? dt.ToString(fmt) : "";

    /// <summary>Применяет regex «найти/заменить» к исходному имени. / Applies find/replace regex to the original name.</summary>
    private static string ApplyRegex(string originalName, RenameOptions opts)
    {
        if (string.IsNullOrEmpty(opts.Find)) return originalName;
        try
        {
            var rx = new Regex(opts.Find, opts.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
            // Regex.Replace поддерживает $1, $2… — группы захвата / supports $1, $2… capture groups
            return rx.Replace(originalName, opts.Replace ?? "");
        }
        catch (Exception) { return originalName; }
    }

    /// <summary>
    /// Применяет стиль регистра к имени: часть перед точкой + расширение.
    /// Applies case style to name: part before dot + extension.
    /// </summary>
    private static string ApplyNameStyle(string fullName, NameStyle style)
    {
        if (style == NameStyle.AsIs) return fullName;
        var dotIdx = fullName.LastIndexOf('.');
        string namePart, extPart;
        if (dotIdx > 0) { namePart = fullName[..dotIdx]; extPart = fullName[dotIdx..]; }
        else { namePart = fullName; extPart = ""; }
        return ApplyCase(namePart, style) + extPart;
    }

    /// <summary>Преобразует регистр строки. / Transforms string case.</summary>
    private static string ApplyCase(string text, NameStyle style) => style switch
    {
        NameStyle.Upper => text.ToUpperInvariant(),
        NameStyle.Lower => text.ToLowerInvariant(),
        NameStyle.TitleCase => string.Concat(
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant())),
        _ => text,
    };

    /// <summary>
    /// Заменяет запрещённые в Windows символы на replacement.
    /// Replaces Windows-forbidden filename characters with replacement.
    /// </summary>
    private static string SanitizeFileName(string name, string replacement)
    {
        const string bad = "<>:\"/\\|?*";
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(bad.IndexOf(c) >= 0 ? replacement : c);
        return sb.ToString();
    }

    /// <summary>
    /// Записывает лог переименования. / Writes the rename log.
    /// </summary>
    internal static void WriteLog(string logPath, IReadOnlyList<RenameDetail> details, RenameResult result)
    {
        try
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var sw = new StreamWriter(logPath, append: true, encoding: Encoding.UTF8);
            sw.WriteLine($"# {DateTime.Now:yyyy-MM-dd HH:mm:ss} — renamed {result.Renamed}/{details.Count}");
            foreach (var d in details)
            {
                if (d.Success)
                    sw.WriteLine($"OK    {d.Item.OriginalName} -> {d.Item.NewName}");
                else
                    sw.WriteLine($"FAIL  {d.Item.OriginalName} -> {d.Item.NewName} ({d.Error ?? "unknown"})");
            }
        }
        catch { /* не критично / non-critical */ }
    }

    /// <summary>
    /// Разрешает коллизию: если имя занято — добавляет суффикс « (1)», « (2)»…
    /// Resolves a collision: if the name is taken, appends a " (1)", " (2)"… suffix.
    /// </summary>
    private static string ResolveName(string desired, string originalName, HashSet<string> usedFinal, HashSet<string> blocked)
    {
        if (!usedFinal.Contains(desired) && !blocked.Contains(desired))
            return desired;

        string baseName = Path.GetFileNameWithoutExtension(desired);
        string ext = Path.GetExtension(desired); // включает точку / includes the dot
        for (int i = 1; i < 100000; i++)
        {
            var candidate = $"{baseName} ({i}){ext}";
            if (!usedFinal.Contains(candidate) && !blocked.Contains(candidate))
                return candidate;
        }
        return desired;
    }
}

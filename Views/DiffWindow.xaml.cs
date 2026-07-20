using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using CoderCommander.Operations;
using CoderCommander.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoderCommander.Views;

/// <summary>
/// Окно сравнения двух текстовых файлов (ph3.1). Два режима:
/// «Рядом» (side-by-side, два AvalonEdit) и «Встроку» (inline, один AvalonEdit).
/// Подсветка различий построчно и внутристрочно (character-level) через DiffPlex.
/// Text file diff window (ph3.1). Two modes: side-by-side (two AvalonEdit) and
/// inline (single AvalonEdit). Line-level and character-level (inline) highlighting via DiffPlex.
/// </summary>
public partial class DiffWindow : Window
{
    private readonly DiffWindowViewModel _vm;

    /// <summary>Конструктор окна сравнения. Читает оба файла (BOM/UTF-8), вычисляет diff и настраивает рендереры.</summary>
    /// <param name="leftPath">Путь к левому (исходному) файлу. / Path to the left (old) file.</param>
    /// <param name="rightPath">Путь к правому (новому) файлу. / Path to the right (new) file.</param>
    public DiffWindow(string leftPath, string rightPath)
    {
        InitializeComponent();

        _vm = new DiffWindowViewModel(leftPath, rightPath);
        DataContext = _vm;

        // Текстовый diff строим только для небинарных файлов; иначе — hex-режим (ph3.2).
        // Text diff is built only for non-binary files; otherwise the hex mode (ph3.2).
        if (!_vm.AreBothBinary)
        {
            // Тексты для трёх редакторов
            LeftEditor.Text = _vm.LeftDisplay;
            RightEditor.Text = _vm.RightDisplay;
            InlineEditor.Text = _vm.InlineDisplay;

            // Подсветка синтаксиса по расширению (улучшает читаемость)
            SyntaxHighlighter.Apply(LeftEditor, leftPath);
            SyntaxHighlighter.Apply(RightEditor, rightPath);
            // Для inline-представления используем синтаксис правого (нового) файла
            SyntaxHighlighter.Apply(InlineEditor, rightPath);

            // Фоновые рендереры строк (post-line) и внутристрочная подсветка
            AttachRenderers(LeftEditor, _vm.LeftLineTypes, _vm.LeftInlineRanges);
            AttachRenderers(RightEditor, _vm.RightLineTypes, _vm.RightInlineRanges);
            AttachRenderers(InlineEditor, null, _vm.InlineRanges);
        }

        // Подключаем ленивую загрузку бинарного (hex) режима (ph3.2).
        // Wire the lazy binary (hex) mode loading (ph3.2).
        WireBinaryEvents();
    }

    /// <summary>
    /// Подключает к редактору рендерер фона строк и (опционально) внутристрочный цветизатор.
    /// Attaches a line-background renderer and (optionally) an inline colorizer to the editor.
    /// </summary>
    private static void AttachRenderers(TextEditor editor,
        Dictionary<int, ChangeType>? lineTypes,
        Dictionary<int, List<(int start, int length, ChangeType type)>> inlineRanges)
    {
        if (lineTypes is not null)
        {
            var bg = new DiffLineBackgroundRenderer(lineTypes);
            editor.TextArea.TextView.BackgroundRenderers.Add(bg);
        }
        if (inlineRanges.Count > 0)
        {
            var col = new DiffInlineColorizer(inlineRanges);
            editor.TextArea.TextView.LineTransformers.Add(col);
        }
    }

    #region Заголовок окна / Window chrome handlers
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else DragMove();
        }
    }
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    #endregion

    #region Рендереры подсветки различий / Diff highlight renderers
    /// <summary>
    /// Рисует полупрозрачный фон на строках с изменениями (добавлено/удалено/изменено).
    /// Draws a semi-transparent background on changed lines (added/removed/modified).
    /// Цвет берётся из темы по типу изменения (ресурсы перечитываются при каждой отрисовке,
    /// поэтому подсветка корректно меняется при смене темы без подписки на события).
    /// Brush is resolved from the theme by change type on every draw, so it adapts to theme switches.
    /// </summary>
    private class DiffLineBackgroundRenderer : IBackgroundRenderer
    {
        private readonly Dictionary<int, ChangeType> _lineTypes;
        public DiffLineBackgroundRenderer(Dictionary<int, ChangeType> lineTypes) => _lineTypes = lineTypes;
        public KnownLayer Layer => KnownLayer.Background;
        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            foreach (var vl in textView.VisualLines)
            {
                var line = vl.FirstDocumentLine;
                if (_lineTypes.TryGetValue(line.LineNumber, out var type))
                {
                    var brush = (SolidColorBrush?)Application.Current.TryFindResource(
                        type == ChangeType.Inserted ? "DiffAddBgBrush" :
                        type == ChangeType.Deleted ? "DiffDelBgBrush" : "DiffModBgBrush");
                    if (brush is null) continue;
                    foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
                        drawingContext.DrawRectangle(brush, null, new Rect(0, r.Top, textView.ActualWidth, r.Height));
                }
            }
        }
    }

    /// <summary>
    /// Внутристрочный (character-level) цветизатор: подсвечивает удалённые/добавленные фрагменты
    /// внутри изменённых строк, а также целиком добавленные/удалённые строки.
    /// Character-level colorizer: highlights removed/added fragments inside changed lines,
    /// and whole inserted/deleted lines.
    /// </summary>
    private class DiffInlineColorizer : DocumentColorizingTransformer
    {
        private readonly Dictionary<int, List<(int start, int length, ChangeType type)>> _ranges;
        public DiffInlineColorizer(Dictionary<int, List<(int start, int length, ChangeType type)>> ranges) => _ranges = ranges;

        protected override void ColorizeLine(DocumentLine line)
        {
            if (!_ranges.TryGetValue(line.LineNumber, out var list)) return;
            var baseOffset = line.Offset;
            foreach (var (start, length, type) in list)
            {
                var s = baseOffset + start;
                var e = baseOffset + start + length;
                if (e > line.EndOffset) e = line.EndOffset;
                if (s >= e) continue;
                ChangeLinePart(s, e, ve =>
                {
                    if (type == ChangeType.Inserted)
                        ve.BackgroundBrush = (SolidColorBrush?)Application.Current.TryFindResource("DiffAddBgBrush");
                    else
                    {
                        ve.BackgroundBrush = (SolidColorBrush?)Application.Current.TryFindResource("DiffDelBgBrush");
                        ve.TextRunProperties.SetTextDecorations(System.Windows.TextDecorations.Strikethrough);
                    }
                });
            }
        }
    }
    #endregion
}

/// <summary>
/// ViewModel окна сравнения: хранит пути/тексты, вычисляет diff (DiffPlex) и готовит данные для рендереров.
/// Diff window ViewModel: holds paths/texts, computes the diff (DiffPlex) and prepares data for the renderers.
/// </summary>
public partial class DiffWindowViewModel : ObservableObject
{
    private readonly Differ _differ = new();

    /// <summary>Левый (исходный) путь. / Left (old) path.</summary>
    [ObservableProperty] private string _leftPath = "";
    /// <summary>Правый (новый) путь. / Right (new) path.</summary>
    [ObservableProperty] private string _rightPath = "";
    /// <summary>Отображаемое имя левого файла. / Display name of the left file.</summary>
    [ObservableProperty] private string _leftPathDisplay = "";
    /// <summary>Отображаемое имя правого файла. / Display name of the right file.</summary>
    [ObservableProperty] private string _rightPathDisplay = "";
    /// <summary>Сводка: +добавлено / -удалено / ~изменено. / Summary: +added / -removed / ~modified.</summary>
    [ObservableProperty] private string _diffSummary = "";

    /// <summary>Текст левого редактора (исходный). / Left editor text (old).</summary>
    public string LeftDisplay { get; private set; } = "";
    /// <summary>Текст правого редактора (новый). / Right editor text (new).</summary>
    public string RightDisplay { get; private set; } = "";
    /// <summary>Текст inline-редактора (объединённый). / Inline editor text (merged).</summary>
    public string InlineDisplay { get; private set; } = "";

    /// <summary>Тип изменения по номеру строки левого редактора. / Per-line change type for the left editor.</summary>
    public Dictionary<int, ChangeType> LeftLineTypes { get; } = new();
    /// <summary>Тип изменения по номеру строки правого редактора. / Per-line change type for the right editor.</summary>
    public Dictionary<int, ChangeType> RightLineTypes { get; } = new();
    /// <summary>Внутристрочные диапазоны левого редактора (удалённые фрагменты). / Inline ranges for the left editor (removed fragments).</summary>
    public Dictionary<int, List<(int start, int length, ChangeType type)>> LeftInlineRanges { get; } = new();
    /// <summary>Внутристрочные диапазоны правого редактора (добавленные фрагменты). / Inline ranges for the right editor (inserted fragments).</summary>
    public Dictionary<int, List<(int start, int length, ChangeType type)>> RightInlineRanges { get; } = new();
    /// <summary>Внутристрочные диапазоны inline-редактора. / Inline ranges for the inline editor.</summary>
    public Dictionary<int, List<(int start, int length, ChangeType type)>> InlineRanges { get; } = new();

    // ─────────────────────────────────────────────────────────────────────────
    // Бинарный diff / hex-вьювер (ph3.2). Свойства выставляются однократно
    // (в конструкторе / после сравнения) и читаются из code-behind, поэтому
    // уведомление об изменениях не требуется. / Binary diff / hex viewer (ph3.2).
    // Properties are set once and read from code-behind, so no notification needed.
    // ─────────────────────────────────────────────────────────────────────────
    private bool _leftBinary;
    private bool _rightBinary;
    private string _binarySummary = "";

    /// <summary>Левый файл определён как бинарный. / Left file detected as binary.</summary>
    public bool LeftBinary
    {
        get => _leftBinary;
        set => _leftBinary = value;
    }

    /// <summary>Правый файл определён как бинарный. / Right file detected as binary.</summary>
    public bool RightBinary
    {
        get => _rightBinary;
        set => _rightBinary = value;
    }

    /// <summary>Сводка бинарного сравнения (размеры, число отличий). / Binary comparison summary.</summary>
    public string BinarySummary
    {
        get => _binarySummary;
        set => _binarySummary = value;
    }

    /// <summary>Оба файла бинарные — по умолчанию показываем hex-сравнение. / Both binary → show hex by default.</summary>
    public bool AreBothBinary => LeftBinary && RightBinary;

    /// <summary>Хотя бы один файл бинарный. / At least one file is binary.</summary>
    public bool HasBinary => LeftBinary || RightBinary;

    /// <summary>
    /// Конструктор: читает файлы (BOM + UTF-8), вычисляет SideBySide- и Inline-модели DiffPlex и готовит данные рендереров.
    /// Constructor: reads files (BOM + UTF-8), computes DiffPlex SideBySide and Inline models and prepares renderer data.
    /// </summary>
    public DiffWindowViewModel(string leftPath, string rightPath)
    {
        LeftPath = leftPath;
        RightPath = rightPath;
        LeftPathDisplay = Path.GetFileName(leftPath);
        RightPathDisplay = Path.GetFileName(rightPath);

        // Детекция бинарности: если хотя бы один файл бинарный — текстовый diff
        // не строим (экономим память/время на больших файлах; hex-режим в ph3.2).
        // Binary detection: if either file is binary we skip the text diff to
        // save memory/time on large files (hex mode handles them, ph3.2).
        LeftBinary = BinaryDiffHelper.IsBinaryFile(leftPath);
        RightBinary = BinaryDiffHelper.IsBinaryFile(rightPath);
        if (LeftBinary || RightBinary) return;

        var (leftText, _) = ReadTextWithEncoding(leftPath);
        var (rightText, _) = ReadTextWithEncoding(rightPath);

        var side = new SideBySideDiffBuilder(_differ).BuildDiffModel(leftText, rightText);
        var inline = new InlineDiffBuilder(_differ).BuildDiffModel(leftText, rightText);

        BuildSideBySide(side);
        BuildInline(inline);

        int added = 0, removed = 0, modified = 0;
        foreach (var p in side.NewText.Lines) if (p.Type == ChangeType.Inserted) added++;
        foreach (var p in side.OldText.Lines) if (p.Type == ChangeType.Deleted) removed++;
        foreach (var p in side.OldText.Lines) if (p.Type == ChangeType.Modified) modified++;
        DiffSummary = $"+{added}  -{removed}  ~{modified}";
    }

    /// <summary>
    /// Читает текстовый файл с детектом кодировки: BOM (UTF-8/UTF-16/UTF-32), иначе UTF-8.
    /// Универсальные окончания строк нормализуются к '\n'.
    /// Reads a text file with encoding detection: BOM (UTF-8/UTF-16/UTF-32), otherwise UTF-8.
    /// Line endings are normalized to '\n'.
    /// </summary>
    private static (string text, string encoding) ReadTextWithEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Encoding enc = new UTF8Encoding(false);
        string encName = "UTF-8";

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        { enc = new UTF8Encoding(false); encName = "UTF-8 (BOM)"; }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        { enc = Encoding.Unicode; encName = "UTF-16 LE"; }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        { enc = Encoding.BigEndianUnicode; encName = "UTF-16 BE"; }
        else if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        { enc = Encoding.UTF32; encName = "UTF-32"; }
        else if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        { enc = new UTF32Encoding(false, true); encName = "UTF-32 LE"; }

        string text;
        try { text = enc.GetString(bytes); }
        catch { text = Encoding.UTF8.GetString(bytes); encName = "UTF-8"; }

        // Убираем BOM-символ, если он попал в текст
        if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
        // Нормализация окончаний строк
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return (text, encName);
    }

    /// <summary>
    /// Строит данные для side-by-side представления: тексты редакторов, типы строк и внутристрочные диапазоны.
    /// Builds side-by-side data: editor texts, per-line types and inline ranges.
    /// </summary>
    private void BuildSideBySide(SideBySideDiffModel side)
    {
        var left = new StringBuilder();
        var right = new StringBuilder();
        var oldLines = side.OldText.Lines;
        var newLines = side.NewText.Lines;
        int n = oldLines.Count;

        for (int i = 0; i < n; i++)
        {
            var oldP = oldLines[i];
            var newP = newLines[i];
            int docLine = i + 1;

            left.Append(oldP.Text ?? "");
            right.Append(newP.Text ?? "");
            if (i < n - 1) { left.Append('\n'); right.Append('\n'); }

            // Левый редактор (исходный): удалённые и изменённые строки
            if (oldP.Type == ChangeType.Deleted)
                LeftLineTypes[docLine] = ChangeType.Deleted;
            else if (oldP.Type == ChangeType.Modified)
            {
                LeftLineTypes[docLine] = ChangeType.Modified;
                AddSubPieceRanges(LeftInlineRanges, docLine, oldP.SubPieces, ChangeType.Deleted);
            }

            // Правый редактор (новый): добавленные и изменённые строки
            if (newP.Type == ChangeType.Inserted)
                RightLineTypes[docLine] = ChangeType.Inserted;
            else if (newP.Type == ChangeType.Modified)
            {
                RightLineTypes[docLine] = ChangeType.Modified;
                AddSubPieceRanges(RightInlineRanges, docLine, newP.SubPieces, ChangeType.Inserted);
            }
        }

        LeftDisplay = left.ToString();
        RightDisplay = right.ToString();
    }

    /// <summary>
    /// Добавляет внутристрочные диапазоны из SubPieces (character-level diff) изменённой строки.
    /// Adds inline ranges from SubPieces (character-level diff) of a modified line.
    /// </summary>
    private static void AddSubPieceRanges(Dictionary<int, List<(int start, int length, ChangeType type)>> target,
        int docLine, IEnumerable<DiffPiece>? subPieces, ChangeType wanted)
    {
        if (subPieces is null) return;
        var list = new List<(int start, int length, ChangeType type)>();
        foreach (var sub in subPieces)
        {
            if (sub.Type != wanted) continue;
            var t = sub.Text ?? "";
            var pos = sub.Position ?? 0;
            if (t.Length > 0) list.Add((pos, t.Length, wanted));
        }
        if (list.Count > 0) target[docLine] = list;
    }

    /// <summary>
    /// Строит данные для inline представления на базе InlineDiffModel: объединённый текст и диапазоны подсветки.
    /// Builds inline data from InlineDiffModel: merged text and highlight ranges.
    /// </summary>
    private void BuildInline(DiffPaneModel inline)
    {
        var sb = new StringBuilder();
        int lineNo = 1;
        foreach (var lp in inline.Lines)
        {
            string lineText;
            if (lp.SubPieces != null && lp.SubPieces.Any())
            {
                // Изменённая строка: собираем из под-фрагментов (Unchanged/Inserted/Deleted)
                var lineSb = new StringBuilder();
                var ranges = new List<(int start, int length, ChangeType type)>();
                int pos = 0;
                foreach (var sub in lp.SubPieces)
                {
                    var t = sub.Text ?? "";
                    lineSb.Append(t);
                    if (sub.Type != ChangeType.Unchanged && t.Length > 0)
                        ranges.Add((pos, t.Length, sub.Type));
                    pos += t.Length;
                }
                lineText = lineSb.ToString();
                if (ranges.Count > 0) InlineRanges[lineNo] = ranges;
            }
            else
            {
                lineText = lp.Text ?? "";
                if (lp.Type == ChangeType.Inserted && lineText.Length > 0)
                    InlineRanges[lineNo] = new() { (0, lineText.Length, ChangeType.Inserted) };
                else if (lp.Type == ChangeType.Deleted && lineText.Length > 0)
                    InlineRanges[lineNo] = new() { (0, lineText.Length, ChangeType.Deleted) };
            }

            sb.Append(lineText);
            if (lineNo < inline.Lines.Count) sb.Append('\n');
            lineNo++;
        }
        InlineDisplay = sb.ToString();
    }
}

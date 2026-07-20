using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CoderCommander.Services;

namespace CoderCommander.Views;

/// <summary>
/// Кастомный hex-вьювер с виртуализацией (ph3.2, exp.yml).
/// Custom hex viewer with virtualization (ph3.2, exp.yml).
/// Отображает байты в hex + ASCII с подсветкой отличающихся байтов (цвет из темы).
/// Shows bytes as hex + ASCII with highlighting of differing bytes (theme brush).
/// Строки генерируются лениво через IList-коллекцию, поэтому большие файлы
/// не загружаются в память целиком. / Rows are generated lazily via an IList
/// collection, so large files are never fully loaded into memory.
/// </summary>
public partial class HexViewer : UserControl
{
    private HexRowCollection? _rows;
    private IReadOnlyList<(long start, long end)> _ranges = Array.Empty<(long, long)>();
    private const int BytesPerRow = 16;

    public HexViewer()
    {
        InitializeComponent();
        // Освобождаем открытые потоки файлов при закрытии окна.
        // Release the open file streams when the window is closed.
        Unloaded += (_, _) => _rows?.Dispose();
    }

    /// <summary>Краткая сводка (размеры, число отличий) — задаётся извне. / Summary text set by host.</summary>
    public string Summary
    {
        set => SummaryText.Text = value ?? "";
    }

    /// <summary>
    /// Инициализирует вьювер: открывает потоки файлов и подключает виртуализированную коллекцию строк.
    /// Initializes the viewer: opens file streams and attaches the virtualized row collection.
    /// </summary>
    public void Initialize(string leftPath, string rightPath, IReadOnlyList<(long start, long end)> ranges)
    {
        _ranges = ranges ?? Array.Empty<(long, long)>();

        _rows?.Dispose();
        _rows = new HexRowCollection(leftPath, rightPath);
        Rows.ItemsSource = _rows;

        if (_ranges.Count == 0)
            SummaryText.Text = LocalizationService.Current.GetString("Hex.Identical");
    }

    /// <summary>
    /// Переходит к следующему/предыдущему диапазону различий, прокручивая виртуализированный список.
    /// Jumps to the next/previous difference range by scrolling the virtualized list.
    /// </summary>
    private void ScrollToDiff(bool next)
    {
        if (_rows is null || _ranges.Count == 0) return;
        var sv = FindVisualChild<ScrollViewer>(this);
        if (sv is null) return;

        int curRow = (int)sv.VerticalOffset;
        int target = -1;

        if (next)
        {
            foreach (var r in _ranges)
            {
                int rs = (int)(r.start / BytesPerRow);
                if (rs > curRow) { target = rs; break; }
            }
            if (target < 0) target = (int)(_ranges[0].start / BytesPerRow); // обёртка к первому
        }
        else
        {
            for (int i = _ranges.Count - 1; i >= 0; i--)
            {
                int rs = (int)(_ranges[i].start / BytesPerRow);
                if (rs < curRow) { target = rs; break; }
            }
            if (target < 0) target = (int)(_ranges[_ranges.Count - 1].start / BytesPerRow); // обёртка к последнему
        }

        sv.ScrollToVerticalOffset(target);
    }

    private void NextDiffButton_Click(object sender, RoutedEventArgs e) => ScrollToDiff(true);
    private void PrevDiffButton_Click(object sender, RoutedEventArgs e) => ScrollToDiff(false);

    /// <summary>Ищет визуального потомка заданного типа. / Finds a visual child of the given type.</summary>
    private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T t) return t;
            var inner = FindVisualChild<T>(child);
            if (inner is not null) return inner;
        }
        return null;
    }
}

/// <summary>Одна ячейка hex-строки (байт в hex и ASCII-видах). / A single hex row cell (byte in hex and ASCII).</summary>
public sealed class HexCell
{
    public string Hex { get; set; } = "";
    public string Ascii { get; set; } = "";
    public bool IsDiff { get; set; }
    public bool IsPadding { get; set; }
}

/// <summary>Строка hex-дампа: смещение + 16 ячеек для левого и правого файлов. / A hex dump row: offset + 16 cells for each file.</summary>
public sealed class HexRow
{
    public long Offset { get; }
    public string OffsetText => Offset.ToString("X8");
    public List<HexCell> LeftCells { get; } = new();
    public List<HexCell> RightCells { get; } = new();

    public HexRow(long offset) => Offset = offset;
}

/// <summary>
/// Виртуализированная коллекция строк hex-дампа. Реализует IList, поэтому
/// VirtualizingStackPanel запрашивает только видимые строки через индексатор,
/// не материализуя файл целиком. / Virtualized collection of hex dump rows.
/// Implements IList so VirtualizingStackPanel requests only visible rows via
/// the indexer, never materializing the whole file.
/// </summary>
public sealed class HexRowCollection : IList, IReadOnlyList<HexRow>, IDisposable
{
    private readonly FileStream _left;
    private readonly FileStream _right;
    private readonly long _leftLen;
    private readonly long _rightLen;
    private readonly long _rowCount;
    private const int BytesPerRow = 16;
    private bool _disposed;

    public HexRowCollection(string leftPath, string rightPath)
    {
        _left = new FileStream(leftPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        _right = new FileStream(rightPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        _leftLen = _left.Length;
        _rightLen = _right.Length;
        long max = Math.Max(_leftLen, _rightLen);
        _rowCount = max <= 0 ? 1 : (max + BytesPerRow - 1) / BytesPerRow;
        if (_rowCount > int.MaxValue) _rowCount = int.MaxValue;
    }

    public int Count => (int)_rowCount;
    public bool IsFixedSize => true;
    public bool IsReadOnly => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public HexRow this[int index] => BuildRow(index);
    object? IList.this[int index] { get => BuildRow(index); set => throw new NotSupportedException(); }

    /// <summary>Строит строку дампа для заданного индекса, читая ровно 16 байт из каждого файла. / Builds a dump row for the given index, reading exactly 16 bytes from each file.</summary>
    private HexRow BuildRow(int index)
    {
        long offset = (long)index * BytesPerRow;
        var left = ReadBytes(_left, offset, Math.Min(BytesPerRow, _leftLen - offset));
        var right = ReadBytes(_right, offset, Math.Min(BytesPerRow, _rightLen - offset));

        var row = new HexRow(offset);
        for (int c = 0; c < BytesPerRow; c++)
        {
            bool lp = c >= left.Length;
            bool rp = c >= right.Length;
            byte lb = lp ? (byte)0 : left[c];
            byte rb = rp ? (byte)0 : right[c];
            bool diff = !lp && !rp && lb != rb;

            row.LeftCells.Add(new HexCell
            {
                Hex = lp ? "  " : lb.ToString("X2"),
                Ascii = lp ? " " : ToPrintable(lb),
                IsDiff = diff,
                IsPadding = lp
            });
            row.RightCells.Add(new HexCell
            {
                Hex = rp ? "  " : rb.ToString("X2"),
                Ascii = rp ? "  " : ToPrintable(rb),
                IsDiff = diff,
                IsPadding = rp
            });
        }
        return row;
    }

    private byte[] ReadBytes(FileStream s, long offset, long count)
    {
        if (count <= 0) return Array.Empty<byte>();
        var buf = new byte[count];
        s.Seek(offset, SeekOrigin.Begin);
        int total = 0;
        while (total < count)
        {
            int n = s.Read(buf, total, (int)count - total);
            if (n == 0) break;
            total += n;
        }
        if (total == buf.Length) return buf;
        var trimmed = new byte[total];
        Array.Copy(buf, trimmed, total);
        return trimmed;
    }

    private static string ToPrintable(byte b) => b >= 32 && b < 127 ? ((char)b).ToString() : ".";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _left.Dispose();
        _right.Dispose();
    }

    #region IList / ICollection реализация (только для чтения) — IList / ICollection (read-only) implementation
int IList.Add(object? value) => throw new NotSupportedException();
        void IList.Clear() => throw new NotSupportedException();
        bool IList.Contains(object? value) => false;
        int IList.IndexOf(object? value) => -1;
        void IList.Insert(int index, object? value) => throw new NotSupportedException();
        void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();

    void ICollection.CopyTo(Array array, int index)
    {
        for (int i = 0; i < Count; i++) array.SetValue(this[i], index + i);
    }

    public IEnumerator<HexRow> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) yield return this[i];
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    #endregion
}

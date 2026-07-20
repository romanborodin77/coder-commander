using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoderCommander.Services;
using ICSharpCode.AvalonEdit.Highlighting;

namespace CoderCommander.Views;

/// <summary>
/// UserControl для быстрого просмотра содержимого файла в панели (ph5.5).
/// Text files: AvalonEdit readonly with syntax highlighting.
/// Images: Image control with AutoSize.
/// Binary files: hex-preview of first 4096 bytes.
/// Auto-updates on selection change (debounce 200ms).
/// </summary>
public partial class QuickViewHost : UserControl
{
    private CancellationTokenSource? _previewCts;
    private const int HexPreviewBytes = 4096;

    public QuickViewHost()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Обновляет предпросмотр для указанного файла. / Updates the preview for the specified file.
    /// </summary>
    /// <param name="filePath">Путь к файлу или null для очистки. / File path or null to clear.</param>
    public void UpdatePreview(string? filePath)
    {
        // Отменяем предыдущую задачу
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        // Скрываем все панели
        TextBorder.Visibility = Visibility.Collapsed;
        ImageBorder.Visibility = Visibility.Collapsed;
        HexBorder.Visibility = Visibility.Collapsed;
        NoPreviewText.Visibility = Visibility.Collapsed;

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            HeaderText.Text = LocalizationService.Current.GetString("QuickView.Title");
            StatusText.Text = string.Empty;
            return;
        }

        var fileName = System.IO.Path.GetFileName(filePath);
        HeaderText.Text = fileName;

        try
        {
            // Проверяем, является ли файл изображением
            if (IsImageFile(filePath))
            {
                LoadImagePreview(filePath, ct);
                return;
            }

            // Проверяем, является ли файл текстовым
            if (FileService.IsTextFile(filePath))
            {
                LoadTextPreview(filePath, ct);
                return;
            }

            // Бинарный файл — hex preview
            LoadHexPreview(filePath, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            NoPreviewText.Visibility = Visibility.Visible;
            StatusText.Text = ex.Message;
        }
    }

    /// <summary>
    /// Загружает текстовый предпросмотр через AvalonEdit. / Loads text preview via AvalonEdit.
    /// </summary>
    private void LoadTextPreview(string filePath, CancellationToken ct)
    {
        var fi = new FileInfo(filePath);
        StatusText.Text = $"{fi.Length:N0} B";

        // Читаем первые 256 КБ для предпросмотра
        const int maxPreview = 256 * 1024;
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sr = new StreamReader(fs);

        var buffer = new char[maxPreview];
        int read = sr.Read(buffer, 0, maxPreview);
        var content = new string(buffer, 0, read);

        if (ct.IsCancellationRequested) return;

        Dispatcher.BeginInvoke(() =>
        {
            if (ct.IsCancellationRequested) return;

            TextPreview.Text = content;
            SyntaxHighlighter.Apply(TextPreview, filePath);
            TextBorder.Visibility = Visibility.Visible;
        });
    }

    /// <summary>
    /// Загружает предпросмотр изображения. / Loads image preview.
    /// </summary>
    private void LoadImagePreview(string filePath, CancellationToken ct)
    {
        var fi = new FileInfo(filePath);
        StatusText.Text = $"{fi.Length:N0} B";

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 400; // Ограничиваем для производительности
            bitmap.EndInit();
            bitmap.Freeze();

            if (ct.IsCancellationRequested) return;

            Dispatcher.BeginInvoke(() =>
            {
                if (ct.IsCancellationRequested) return;

                ImagePreview.Source = bitmap;
                ImageBorder.Visibility = Visibility.Visible;
                StatusText.Text = $"{fi.Length:N0} B — {bitmap.PixelWidth}×{bitmap.PixelHeight}";
            });
        }
        catch
        {
            Dispatcher.BeginInvoke(() =>
            {
                NoPreviewText.Visibility = Visibility.Visible;
                StatusText.Text = LocalizationService.Current.GetString("QuickView.CannotLoad");
            });
        }
    }

    /// <summary>
    /// Загружает hex-предпросмотр первых 4096 байт. / Loads hex preview of first 4096 bytes.
    /// </summary>
    private void LoadHexPreview(string filePath, CancellationToken ct)
    {
        var fi = new FileInfo(filePath);

        byte[] data;
        int readCount;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            data = new byte[Math.Min(HexPreviewBytes, fi.Length)];
            readCount = fs.Read(data, 0, data.Length);
        }

        if (ct.IsCancellationRequested) return;

        var hex = FormatHexDump(data, readCount);

        Dispatcher.BeginInvoke(() =>
        {
            if (ct.IsCancellationRequested) return;

            HexPreview.Text = hex;
            HexBorder.Visibility = Visibility.Visible;

            var totalSize = FormatSize(fi.Length);
            StatusText.Text = readCount < fi.Length
                ? $"Hex: {readCount} / {fi.Length} ({totalSize})"
                : $"Hex: {fi.Length} ({totalSize})";
        });
    }

    /// <summary>
    /// Форматирует байты в hex-дамп (адрес + hex + ASCII). / Formats bytes as hex dump.
    /// </summary>
    private static string FormatHexDump(byte[] data, int length)
    {
        var sb = new System.Text.StringBuilder(length * 3);

        for (int offset = 0; offset < length; offset += 16)
        {
            // Адрес
            sb.Append($"{offset:X8}  ");

            // Hex-байты
            for (int i = 0; i < 16; i++)
            {
                if (offset + i < length)
                    sb.Append($"{data[offset + i]:X2} ");
                else
                    sb.Append("   ");

                if (i == 7) sb.Append(' ');
            }

            sb.Append(' ');
            sb.Append('|');

            // ASCII
            for (int i = 0; i < 16 && offset + i < length; i++)
            {
                byte b = data[offset + i];
                sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
            }

            sb.Append('|');
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Проверяет, является ли файл изображением по расширению. / Checks if file is an image by extension.
    /// </summary>
    private static bool IsImageFile(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".webp" or ".tiff" or ".tif";
    }

    /// <summary>
    /// Форматирует размер файла в человекочитаемый формат. / Formats file size as human-readable.
    /// </summary>
    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double s = bytes;
        int i = 0;
        while (s >= 1024 && i < units.Length - 1) { s /= 1024; i++; }
        return $"{s:0.##} {units[i]}";
    }
}

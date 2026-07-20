using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using CoderCommander.Models;
using CoderCommander.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CoderCommander.Views;

/// <summary>
/// Модальное окно редактора/просмотра файлов с вкладками и подсветкой синтаксиса (F3 — просмотр, F4 — редактирование).
/// Modal editor/viewer window with tabs and syntax highlighting (F3 — view, F4 — edit).
/// Поддержка изображений: зум, поворот, слайдшоу. / Supports image files: zoom, rotate, slideshow.
/// </summary>
public partial class EditorWindow : Window
{
    private EditorWindowViewModel _vm = null!;

    // ═══════════════════════════════════════════
    // TAB STATE / СОСТОЯНИЕ ВКЛАДОК
    // ═══════════════════════════════════════════

    /// <summary>Словарь: вкладка → редактор AvalonEdit. / Dictionary: tab → AvalonEdit editor.</summary>
    private readonly Dictionary<EditorTabViewModel, TextEditor> _editors = new();

    /// <summary>Подсветчики текущей строки для каждого редактора. / Current line highlighters for each editor.</summary>
    private readonly Dictionary<TextEditor, CurrentLineHighlighter> _highlighters = new();

    /// <summary>Флаг: режим просмотра изображений (нет вкладок). / Flag: image viewer mode (no tabs).</summary>
    private bool _isImageMode;

    // ═══════════════════════════════════════════
    // IMAGE VIEWER STATE / СОСТОЯНИЕ ПРОСМОТРЩИКА
    // ═══════════════════════════════════════════

    private BitmapImage? _image;
    private double _imageScale = 1.0;
    private double _imageRotation;
    private string? _currentImagePath;
    private readonly bool _isReadOnly;
    private DispatcherTimer? _slideshowTimer;
    private const int SlideshowIntervalSeconds = 3;

    /// <summary>
    /// Конструктор окна редактора. Для текстовых файлов — вкладки, для изображений — просмотрщик.
    /// Editor window constructor. For text files — tabs, for images — viewer.
    /// </summary>
    public EditorWindow(string filePath, string content, bool isReadOnly = false)
    {
        InitializeComponent();
        _isReadOnly = isReadOnly;

        _vm = new EditorWindowViewModel(this);

        if (IsImageFile(filePath))
        {
            _isImageMode = true;
            SetupImageViewer(filePath, isReadOnly);
        }
        else
        {
            SetupTabbedEditor(filePath, content, isReadOnly);
        }

        DataContext = _vm;
        Closing += OnClosing;
        ((App)Application.Current).ThemeChanged += OnAppThemeChanged;
        Closing += (s, e) =>
        {
            if (!e.Cancel)
                ((App)Application.Current).ThemeChanged -= OnAppThemeChanged;
        };
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path)?.ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp"
            or ".ico" or ".tiff" or ".tif" or ".webp" or ".svg";
    }

    // ═══════════════════════════════════════════
    // TEXT EDITOR TABS / ВКЛАДКИ РЕДАКТОРА
    // ═══════════════════════════════════════════

    /// <summary>
    /// Настраивает редактор с вкладками: создаёт первую вкладку из параметров.
    /// Sets up the tabbed editor: creates the first tab from parameters.
    /// </summary>
    private void SetupTabbedEditor(string filePath, string content, bool isReadOnly)
    {
        TextEditorDockPanel.Visibility = Visibility.Visible;
        ImageBorder.Visibility = Visibility.Collapsed;

        var tab = CreateTabViewModel(filePath, content ?? "", isReadOnly);
        AddEditorTab(tab);

        _vm.Title = Path.GetFileName(filePath);
        _vm.ModeIcon = isReadOnly ? "\uE714" : "\uE104";

        var settings = SettingsService.Load();
        ApplySettingsToEditor(_editors[tab], settings);

        Loaded += (s, e) => _editors[tab].Focus();
        StateChanged += OnStateChanged;
    }

    /// <summary>
    /// Создаёт модель вкладки редактора.
    /// Creates an editor tab view model.
    /// </summary>
    private static EditorTabViewModel CreateTabViewModel(string filePath, string content, bool isReadOnly)
    {
        var tab = new EditorTabViewModel
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Content = content,
            OriginalContent = content,
            IsReadOnly = isReadOnly,
            Language = EditorTabViewModel.DetectLanguage(filePath),
            LineCount = string.IsNullOrEmpty(content) ? 0 : content.Split('\n').Length
        };
        return tab;
    }

    /// <summary>
    /// Добавляет вкладку редактора: создаёт TextEditor, подписывается на события, добавляет в контейнер.
    /// Adds an editor tab: creates TextEditor, subscribes to events, adds to container.
    /// </summary>
    private void AddEditorTab(EditorTabViewModel tab)
    {
        var editor = new TextEditor
        {
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 14,
            ShowLineNumbers = true,
            Tag = tab
        };

        editor.Foreground = FindResource("VsEditorFg") as Brush ?? Brushes.LightGray;
        editor.Background = FindResource("VsEditorBg") as Brush ?? Brushes.Black;
        editor.LineNumbersForeground = FindResource("VsLineNumber") as Brush ?? Brushes.Gray;
        editor.BorderThickness = new Thickness(0);
        editor.Padding = new Thickness(8, 4, 0, 0);

        editor.Text = tab.Content;
        editor.IsReadOnly = tab.IsReadOnly;

        SyntaxHighlighter.Apply(editor, tab.FilePath);
        ApplySelectionStyleToEditor(editor);

        var capturedTab = tab;
        var capturedEditor = editor;
        editor.TextChanged += (s, e) =>
        {
            capturedTab.UpdateContent(capturedEditor.Text);
        };

        _editors[tab] = editor;
        EditorContainer.Children.Add(editor);

        var tabItem = new TabItem
        {
            DataContext = tab,
            Header = tab,
            ContentTemplate = null
        };
        EditorTabControl.Items.Add(tabItem);

        _vm.Tabs.Add(tab);
        _vm.ActiveTab = tab;

        ShowEditor(tab);
    }

    /// <summary>
    /// Показывает редактор указанной вкладки, скрывая остальные.
    /// Shows the editor for the specified tab, hiding others.
    /// </summary>
    private void ShowEditor(EditorTabViewModel? tab)
    {
        foreach (var kvp in _editors)
            kvp.Value.Visibility = kvp.Key == tab ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Открывает файл в новой вкладке (или переключается на существующую).
    /// Opens a file in a new tab (or switches to existing one).
    /// </summary>
    public void OpenFile(string path)
    {
        if (_isImageMode) return;

        var existing = _vm.Tabs.FirstOrDefault(t =>
            string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            _vm.ActiveTab = existing;
            SyncTabControl();
            Title = _vm.ActiveTab.FileName;
            return;
        }

        try
        {
            var content = File.ReadAllText(path);
            var tab = CreateTabViewModel(path, content, false);
            AddEditorTab(tab);

            var settings = SettingsService.Load();
            ApplySettingsToEditor(_editors[tab], settings);
            _editors[tab].Focus();
        }
        catch (Exception ex)
        {
            StyledMessageBoxWindow.Show(
                string.Format(LocalizationService.Current.GetString("Editor.SaveError"), ex.Message),
                LocalizationService.Current.GetString("Error.Title"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Закрывает вкладку с проверкой несохранённых изменений.
    /// Closes a tab with unsaved changes check.
    /// </summary>
    public void CloseTab(EditorTabViewModel tab)
    {
        SyncEditorContent(tab);

        if (tab.HasUnsavedChanges())
        {
            _vm.ActiveTab = tab;
            var result = StyledMessageBoxWindow.Show(
                string.Format(LocalizationService.Current.GetString("Editor.Tabs.UnsavedPrompt"), tab.FileName),
                LocalizationService.Current.GetString("Editor.Tabs.UnsavedTitle"),
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    SyncEditorContent(tab);
                    tab.Save();
                    break;
                case MessageBoxResult.Cancel:
                    return;
            }
        }

        RemoveEditorTab(tab);
    }

    /// <summary>
    /// Удаляет вкладку и её редактор из UI.
    /// Removes a tab and its editor from UI.
    /// </summary>
    private void RemoveEditorTab(EditorTabViewModel tab)
    {
        if (_editors.TryGetValue(tab, out var editor))
        {
            EditorContainer.Children.Remove(editor);
            _highlighters.Remove(editor);
            _editors.Remove(tab);
        }

        for (int i = EditorTabControl.Items.Count - 1; i >= 0; i--)
        {
            if (EditorTabControl.Items[i] is TabItem ti && ti.DataContext == tab)
            {
                EditorTabControl.Items.RemoveAt(i);
                break;
            }
        }

        _vm.Tabs.Remove(tab);

        if (_vm.Tabs.Count == 0)
        {
            Close();
        }
        else if (_vm.ActiveTab == tab)
        {
            var idx = Math.Min(EditorTabControl.Items.Count - 1, _vm.Tabs.Count - 1);
            if (idx >= 0)
                _vm.ActiveTab = _vm.Tabs[Math.Max(0, idx)];
        }
        if (_vm.ActiveTab != null)
        {
            SyncTabControl();
            Title = _vm.ActiveTab.FileName;
        }
    }

    /// <summary>
    /// Синхронизирует содержимое вкладки из редактора.
    /// Syncs tab content from the editor.
    /// </summary>
    private void SyncEditorContent(EditorTabViewModel tab)
    {
        if (_editors.TryGetValue(tab, out var editor))
            tab.UpdateContent(editor.Text);
    }

    /// <summary>
    /// Возвращает текст из активного редактора.
    /// Returns text from the active editor.
    /// </summary>
    public string GetActiveEditorText()
    {
        if (_vm.ActiveTab != null && _editors.TryGetValue(_vm.ActiveTab, out var editor))
            return editor.Text;
        return "";
    }

    /// <summary>
    /// Возвращает текст из указанного редактора вкладки.
    /// Returns text from the specified tab's editor.
    /// </summary>
    public string GetEditorText(EditorTabViewModel tab)
    {
        if (_editors.TryGetValue(tab, out var editor))
            return editor.Text;
        return tab.Content;
    }

    /// <summary>
    /// Закрывает все вкладки с проверкой несохранённых изменений.
    /// Closes all tabs with unsaved changes check.
    /// </summary>
    public void CloseAllTabs()
    {
        var tabsCopy = _vm.Tabs.ToList();
        foreach (var tab in tabsCopy)
        {
            SyncEditorContent(tab);
            if (tab.HasUnsavedChanges())
            {
                _vm.ActiveTab = tab;
                ShowEditor(tab);
                var result = StyledMessageBoxWindow.Show(
                    string.Format(LocalizationService.Current.GetString("Editor.Tabs.UnsavedPrompt"), tab.FileName),
                    LocalizationService.Current.GetString("Editor.Tabs.UnsavedTitle"),
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                switch (result)
                {
                    case MessageBoxResult.Yes:
                        SyncEditorContent(tab);
                        tab.Save();
                        break;
                    case MessageBoxResult.Cancel:
                        return;
                }
            }
            RemoveEditorTabInternal(tab);
        }
        Close();
    }

    /// <summary>
    /// Удаляет вкладку без проверки (для массового закрытия).
    /// Removes a tab without checking (for bulk close).
    /// </summary>
    private void RemoveEditorTabInternal(EditorTabViewModel tab)
    {
        if (_editors.TryGetValue(tab, out var editor))
        {
            EditorContainer.Children.Remove(editor);
            _highlighters.Remove(editor);
            _editors.Remove(tab);
        }

        for (int i = EditorTabControl.Items.Count - 1; i >= 0; i--)
        {
            if (EditorTabControl.Items[i] is TabItem ti && ti.DataContext == tab)
            {
                EditorTabControl.Items.RemoveAt(i);
                break;
            }
        }

        _vm.Tabs.Remove(tab);
    }

    /// <summary>
    /// Применяет настройки редактора (шрифт, перенос строк) к указанному редактору.
    /// Applies editor settings (font, word wrap) to the specified editor.
    /// </summary>
    private static void ApplySettingsToEditor(TextEditor editor, AppSettings settings)
    {
        editor.FontFamily = new FontFamily(settings.EditorFontFamily);
        editor.FontSize = settings.EditorFontSize;
        editor.ShowLineNumbers = settings.EditorShowLineNumbers;
        editor.WordWrap = settings.EditorWordWrap;
    }

    // ═══════════════════════════════════════════
    // TAB SYNC / СИНХРОНИЗАЦИЯ ВКЛАДОК
    // ═══════════════════════════════════════════

    internal void SyncTabControl()
    {
        for (int i = 0; i < EditorTabControl.Items.Count; i++)
        {
            if (EditorTabControl.Items[i] is TabItem ti && ti.DataContext == _vm.ActiveTab)
            {
                EditorTabControl.SelectedItem = ti;
                break;
            }
        }
    }

    // ═══════════════════════════════════════════
    // EVENT HANDLERS / ОБРАБОТЧИКИ СОБЫТИЙ
    // ═══════════════════════════════════════════

    private void AddTab_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = LocalizationService.Current.GetString("Editor.Tab.New"),
            Filter = "All files (*.*)|*.*|Text files (*.txt)|*.txt|C# files (*.cs)|*.cs"
        };
        if (dlg.ShowDialog() == true)
            OpenFile(dlg.FileName);
    }

    private void EditorTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EditorTabControl.SelectedItem is TabItem selectedTab && selectedTab.DataContext is EditorTabViewModel selectedVm)
            _vm.ActiveTab = selectedVm;
        if (_vm.ActiveTab != null)
        {
            ShowEditor(_vm.ActiveTab);
            Title = _vm.ActiveTab.FileName;
        }
    }

    private void EditorTabControl_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && e.LeftButton != MouseButtonState.Pressed)
        {
            var tabItem = FindTabItemFromMouse(e);
            if (tabItem?.DataContext is EditorTabViewModel tab)
                CloseTab(tab);
        }
    }

    /// <summary>
    /// Находит TabItem под курсором мыши.
    /// Finds the TabItem under the mouse cursor.
    /// </summary>
    private TabItem? FindTabItemFromMouse(MouseButtonEventArgs e)
    {
        var hit = VisualTreeHelper.HitTest(EditorTabControl, e.GetPosition(EditorTabControl));
        var depObj = hit.VisualHit;
        while (depObj != null)
        {
            if (depObj is TabItem tabItem)
                return tabItem;
            depObj = VisualTreeHelper.GetParent(depObj);
        }
        return null;
    }

    // ═══════════════════════════════════════════
    // IMAGE VIEWER / ПРОСМОТРЩИК ИЗОБРАЖЕНИЙ
    // ═══════════════════════════════════════════

    private void SetupImageViewer(string filePath, bool isReadOnly)
    {
        _currentImagePath = filePath;

        _vm.Title = isReadOnly
            ? $"{Path.GetFileName(filePath)} {LocalizationService.Current.GetString("Editor.ViewMode")}"
            : Path.GetFileName(filePath);
        _vm.ModeIcon = "\uE714";

        TextEditorDockPanel.Visibility = Visibility.Collapsed;
        ImageBorder.Visibility = Visibility.Visible;

        Loaded += (s, e) =>
        {
            LoadImage(filePath);
            UpdateZoomText();
        };

        KeyDown += ImageWindow_KeyDown;
        StateChanged += OnStateChanged;
    }

    private void LoadImage(string path)
    {
        try
        {
            StopSlideshow();
            _image = new BitmapImage();
            _image.BeginInit();
            _image.UriSource = new Uri(path, UriKind.Absolute);
            _image.CacheOption = BitmapCacheOption.OnLoad;
            _image.EndInit();
            _image.Freeze();

            ImageViewer.Source = _image;
            _currentImagePath = path;
            _imageScale = 1.0;
            _imageRotation = 0;

            ApplyImageTransform();

            var fileName = Path.GetFileName(path);
            _vm.Title = _isReadOnly ? $"{fileName} {LocalizationService.Current.GetString("Editor.ViewMode")}" : fileName;

            ImageInfoText.Text = string.Format(
                LocalizationService.Current.GetString("Editor.Image.Size"),
                _image.PixelWidth, _image.PixelHeight);
        }
        catch (Exception ex)
        {
            ImageInfoText.Text = $"Error: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════
    // IMAGE VIEWER HANDLERS / ОБРАБОТЧИКИ ПРОСМОТРЩИКА
    // ═══════════════════════════════════════════

    private void ZoomIn_Click(object sender, RoutedEventArgs e) { _imageScale *= 1.25; ApplyImageTransform(); UpdateZoomText(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { _imageScale *= 0.8; ApplyImageTransform(); UpdateZoomText(); }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        if (_image == null) return;
        _imageScale = 1.0;
        ApplyImageTransform();
        UpdateZoomText();
    }

    private void ZoomFit_Click(object sender, RoutedEventArgs e)
    {
        if (_image == null) return;
        var container = ImageViewer.Parent as FrameworkElement;
        if (container == null) return;
        var scaleX = container.ActualWidth / _image.PixelWidth;
        var scaleY = container.ActualHeight / _image.PixelHeight;
        _imageScale = Math.Min(scaleX, scaleY);
        ApplyImageTransform();
        UpdateZoomText();
    }

    private void RotateLeft_Click(object sender, RoutedEventArgs e) { _imageRotation -= 90; ApplyImageTransform(); }
    private void RotateRight_Click(object sender, RoutedEventArgs e) { _imageRotation += 90; ApplyImageTransform(); }

    private void Slideshow_Click(object sender, RoutedEventArgs e)
    {
        if (_slideshowTimer != null) StopSlideshow();
        else StartSlideshow();
    }

    private void ApplyImageTransform()
    {
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(_imageScale, _imageScale));
        group.Children.Add(new RotateTransform(_imageRotation));
        ImageViewer.RenderTransform = group;
    }

    private void UpdateZoomText() => ZoomText.Text = $"{(int)(_imageScale * 100)}%";

    private void StartSlideshow()
    {
        if (_currentImagePath == null) return;
        _slideshowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SlideshowIntervalSeconds) };
        _slideshowTimer.Tick += (_, _) => LoadNextImage();
        _slideshowTimer.Start();
        BtnSlideshow.Content = "\uE71A";
        BtnSlideshow.ToolTip = LocalizationService.Current.GetString("Editor.Image.SlideshowStop");
    }

    private void StopSlideshow()
    {
        _slideshowTimer?.Stop();
        _slideshowTimer = null;
        BtnSlideshow.Content = "\uE768";
        BtnSlideshow.ToolTip = LocalizationService.Current.GetString("Editor.Image.Slideshow");
    }

    private void LoadNextImage()
    {
        if (_currentImagePath == null) return;
        var next = GetSiblingImage(_currentImagePath, forward: true);
        if (next != null) LoadImage(next);
        else StopSlideshow();
    }

    private void LoadPrevImage()
    {
        if (_currentImagePath == null) return;
        var prev = GetSiblingImage(_currentImagePath, forward: false);
        if (prev != null) LoadImage(prev);
    }

    private static string? GetSiblingImage(string current, bool forward)
    {
        var dir = Path.GetDirectoryName(current);
        if (string.IsNullOrEmpty(dir)) return null;
        string[] rawFiles;
        try { rawFiles = Directory.GetFiles(dir); }
        catch { return null; }
        var images = rawFiles
            .Where(f =>
            {
                var ext = Path.GetExtension(f)?.ToLowerInvariant();
                return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp"
                    or ".ico" or ".tiff" or ".tif" or ".webp";
            })
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (images.Count == 0) return null;
        var idx = images.FindIndex(f => string.Equals(f, current, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return null;
        return forward ? images[(idx + 1) % images.Count] : images[(idx - 1 + images.Count) % images.Count];
    }

    private void ImageWindow_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: LoadPrevImage(); e.Handled = true; break;
            case Key.Right: LoadNextImage(); e.Handled = true; break;
            case Key.OemPlus or Key.Add: _imageScale *= 1.25; ApplyImageTransform(); UpdateZoomText(); e.Handled = true; break;
            case Key.OemMinus or Key.Subtract: _imageScale *= 0.8; ApplyImageTransform(); UpdateZoomText(); e.Handled = true; break;
            case Key.D0 or Key.NumPad0: _imageScale = 1.0; ApplyImageTransform(); UpdateZoomText(); e.Handled = true; break;
            case Key.F: ZoomFit_Click(sender, e); e.Handled = true; break;
            case Key.Space: if (_slideshowTimer != null) StopSlideshow(); else StartSlideshow(); e.Handled = true; break;
        }
    }

    // ═══════════════════════════════════════════
    // THEME / ТЕМА
    // ═══════════════════════════════════════════

    private void OnAppThemeChanged(object? sender, EventArgs e)
    {
        ApplySelectionStyle();
        foreach (var child in EditorContainer.Children)
        {
            if (child is TextEditor ed)
            {
                ed.Foreground = FindResource("VsEditorFg") as SolidColorBrush;
                ed.Background = FindResource("VsEditorBg") as SolidColorBrush;
                ed.LineNumbersForeground = FindResource("VsLineNumber") as SolidColorBrush;
            }
        }
    }

    /// <summary>
    /// Применяет кисти темы выделения и подсветки текущей строки ко всем редакторам.
    /// Applies theme selection and current line highlight brushes to all editors.
    /// </summary>
    private void ApplySelectionStyle()
    {
        foreach (var editor in _editors.Values)
            ApplySelectionStyleToEditor(editor);
    }

    private void ApplySelectionStyleToEditor(TextEditor editor)
    {
        try
        {
            var selectionBrush = TryFindResource("VsSelection") as SolidColorBrush;
            var selectionBorder = TryFindResource("EditorSelectionBorder") as SolidColorBrush;
            var currentLineBrush = TryFindResource("EditorCurrentLine") as SolidColorBrush;

            if (selectionBrush != null)
            {
                editor.TextArea.SelectionBrush = selectionBrush;
                editor.TextArea.SelectionForeground = null;
            }
            if (selectionBorder != null)
                editor.TextArea.SelectionBorder = new Pen(selectionBorder, 0);

            if (currentLineBrush != null)
            {
                if (_highlighters.TryGetValue(editor, out var old))
                    editor.TextArea.TextView.BackgroundRenderers.Remove(old);
                var hl = new CurrentLineHighlighter(editor, currentLineBrush);
                _highlighters[editor] = hl;
                editor.TextArea.TextView.BackgroundRenderers.Add(hl);
            }
        }
        catch { }
    }

    // ═══════════════════════════════════════════
    // WINDOW STATE / СОСТОЯНИЕ ОКНА
    // ═══════════════════════════════════════════

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            MaximizeButton.Content = "\uE923";
            MaximizeButton.ToolTip = LocalizationService.Current.GetString("Editor.Restore");
        }
        else
        {
            MaximizeButton.Content = "\uE922";
            MaximizeButton.ToolTip = LocalizationService.Current.GetString("Editor.Maximize");
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        StopSlideshow();
        if (_isImageMode) return;
        if (!_vm.CheckAllUnsavedChanges())
            e.Cancel = true;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else
                DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ═══════════════════════════════════════════
    // CURRENT LINE HIGHLIGHTER / ПОДСВЕТКА ТЕКУЩЕЙ СТРОКИ
    // ═══════════════════════════════════════════

    private class CurrentLineHighlighter : IBackgroundRenderer
    {
        private readonly TextEditor _editor;
        private readonly SolidColorBrush _brush;

        public CurrentLineHighlighter(TextEditor editor, SolidColorBrush brush)
        {
            _editor = editor;
            _brush = brush;
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_editor.Document == null) return;
            var currentLine = _editor.Document.GetLineByOffset(_editor.CaretOffset);
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, currentLine))
            {
                drawingContext.DrawRectangle(_brush, null,
                    new Rect(0, rect.Top, textView.ActualWidth, rect.Height));
            }
        }
    }
}

// ═══════════════════════════════════════════
// VIEWMODEL / МОДЕЛЬ ПРЕДСТАВЛЕНИЯ
// ═══════════════════════════════════════════

/// <summary>
/// ViewModel для многовкладочного окна редактора. Управляет вкладками, командами сохранения/закрытия.
/// ViewModel for the multi-tab editor window. Manages tabs, save/close commands.
/// </summary>
public partial class EditorWindowViewModel : ObservableObject
{
    private readonly EditorWindow _window;

    /// <summary>Заголовок окна. / Window title.</summary>
    [ObservableProperty] private string _title = "";
    /// <summary>Иконка режима (карандаш/глаз). / Mode icon (pencil/eye).</summary>
    [ObservableProperty] private string _modeIcon = "\uE104";
    /// <summary>Активная вкладка. / Active tab.</summary>
    [ObservableProperty] private EditorTabViewModel? _activeTab;

    /// <summary>Коллекция открытых вкладок. / Collection of open tabs.</summary>
    public ObservableCollection<EditorTabViewModel> Tabs { get; } = new();

    public EditorWindowViewModel(EditorWindow window)
    {
        _window = window;
        _title = LocalizationService.Current.GetString("Editor.Title");
    }

    /// <summary>Закрыть окно. / Close window.</summary>
    [RelayCommand]
    private void Close() => _window.Close();

    /// <summary>Сохранить активную вкладку. / Save active tab.</summary>
    [RelayCommand]
    private void SaveActiveTab()
    {
        if (ActiveTab == null || ActiveTab.IsReadOnly) return;
        ActiveTab.Content = _window.GetActiveEditorText();
        ActiveTab.Save();
    }

    /// <summary>Закрыть активную вкладку. / Close active tab.</summary>
    [RelayCommand]
    private void CloseActiveTab()
    {
        if (ActiveTab != null)
            _window.CloseTab(ActiveTab);
    }

    /// <summary>Закрыть указанную вкладку. / Close specified tab.</summary>
    [RelayCommand]
    private void CloseTab(EditorTabViewModel? tab)
    {
        if (tab != null)
            _window.CloseTab(tab);
    }

    /// <summary>Закрыть другие вкладки (кроме указанной). / Close other tabs (except specified).</summary>
    [RelayCommand]
    private void CloseOtherTabs(EditorTabViewModel? keepTab)
    {
        var toClose = Tabs.Where(t => t != keepTab).ToList();
        foreach (var tab in toClose)
            _window.CloseTab(tab);
    }

    /// <summary>Следующая вкладка (Ctrl+Tab). / Next tab (Ctrl+Tab).</summary>
    [RelayCommand]
    private void NextTab()
    {
        if (Tabs.Count <= 1 || ActiveTab == null) return;
        var idx = Tabs.IndexOf(ActiveTab);
        ActiveTab = Tabs[(idx + 1) % Tabs.Count];
        _window.SyncTabControl();
        _window.Title = ActiveTab.FileName;
    }

    /// <summary>Предыдущая вкладка (Ctrl+Shift+Tab). / Previous tab (Ctrl+Shift+Tab).</summary>
    [RelayCommand]
    private void PreviousTab()
    {
        if (Tabs.Count <= 1 || ActiveTab == null) return;
        var idx = Tabs.IndexOf(ActiveTab);
        ActiveTab = Tabs[(idx - 1 + Tabs.Count) % Tabs.Count];
        _window.SyncTabControl();
        _window.Title = ActiveTab.FileName;
    }

    /// <summary>Закрыть все вкладки. / Close all tabs.</summary>
    [RelayCommand]
    private void CloseAllTabs() => _window.CloseAllTabs();

    /// <summary>
    /// Проверяет все вкладки на несохранённые изменения перед закрытием окна.
    /// Checks all tabs for unsaved changes before closing the window.
    /// </summary>
    public bool CheckAllUnsavedChanges()
    {
        foreach (var tab in Tabs.ToList())
        {
            _window.GetEditorText(tab);
            tab.Content = _window.GetEditorText(tab);
            if (tab.HasUnsavedChanges())
            {
                ActiveTab = tab;
                var result = StyledMessageBoxWindow.Show(
                    string.Format(LocalizationService.Current.GetString("Editor.Tabs.UnsavedPrompt"), tab.FileName),
                    LocalizationService.Current.GetString("Editor.Tabs.UnsavedTitle"),
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                switch (result)
                {
                    case MessageBoxResult.Yes:
                        tab.Content = _window.GetEditorText(tab);
                        tab.Save();
                        break;
                    case MessageBoxResult.No:
                        break;
                    default:
                        return false;
                }
            }
        }
        return true;
    }
}

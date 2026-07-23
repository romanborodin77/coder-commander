using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Search;
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

    /// <summary>Подсветчики текущего слова для каждого редактора. / Current word highlighters for each editor.</summary>
    private readonly Dictionary<TextEditor, CurrentWordHighlighter> _wordHighlighters = new();

    /// <summary>Менеджеры фолдинга для каждого редактора. / Folding managers for each editor.</summary>
    private readonly Dictionary<TextEditor, FoldingManager> _foldingManagers = new();

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
        // FIXED: Consolidated into a single Closing handler to avoid non-deterministic ordering.
        // Previously had two Closing subscriptions: one for OnClosing and one lambda for ThemeChanged unsubscribe.
        // The lambda could run before OnClosing, unsubscribing ThemeChanged prematurely.
        Closing += OnClosing;
        ((App)Application.Current).ThemeChanged += OnAppThemeChanged;
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
        var settings = SettingsService.Load();

        var editor = new TextEditor
        {
            FontFamily = new FontFamily(settings.EditorFontFamily),
            FontSize = settings.EditorFontSize,
            ShowLineNumbers = settings.EditorShowLineNumbers,
            WordWrap = settings.EditorWordWrap,
            Tag = tab
        };

        editor.Foreground = FindResource("VsEditorFg") as Brush ?? Brushes.LightGray;
        editor.Background = FindResource("VsEditorBg") as Brush ?? Brushes.Black;
        editor.LineNumbersForeground = FindResource("VsLineNumber") as Brush ?? Brushes.Gray;
        editor.BorderThickness = new Thickness(0);
        editor.Padding = new Thickness(8, 4, 0, 0);

        editor.Options.IndentationSize = settings.EditorTabWidth;
        editor.Options.ConvertTabsToSpaces = settings.EditorUseSpaces;
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.Options.ShowColumnRuler = settings.EditorShowColumnRuler;
        editor.Options.ColumnRulerPosition = settings.EditorColumnRulerPosition;
        editor.Options.ShowSpaces = settings.EditorShowSpaces;
        editor.Options.ShowTabs = settings.EditorShowTabs;
        editor.Options.ShowEndOfLine = settings.EditorShowEndOfLine;
        editor.Options.CutCopyWholeLine = true;

        editor.Text = tab.Content;
        editor.IsReadOnly = tab.IsReadOnly;

        SyntaxHighlighter.Apply(editor, tab.FilePath);
        ApplySelectionStyleToEditor(editor);

        var capturedTab = tab;
        var capturedEditor = editor;

        SearchPanel.Install(editor);

        var foldingManager = FoldingManager.Install(editor.TextArea);
        _foldingManagers[editor] = foldingManager;
        var foldingStrategy = new XmlFoldingStrategy();
        foldingStrategy.UpdateFoldings(foldingManager, editor.Document);

        editor.TextChanged += (s, e) =>
        {
            capturedTab.UpdateContent(capturedEditor.Text);
            if (_foldingManagers.TryGetValue(capturedEditor, out var fm))
                foldingStrategy.UpdateFoldings(fm, capturedEditor.Document);
        };

        var wordHighlighter = new CurrentWordHighlighter(editor);
        _wordHighlighters[editor] = wordHighlighter;
        editor.TextArea.TextView.BackgroundRenderers.Add(wordHighlighter);

        editor.TextArea.TextView.BackgroundRenderers.Add(new BracketHighlightRenderer(editor));

        editor.TextArea.MouseWheel += (s, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                var delta = e.Delta > 0 ? 1 : -1;
                var newSize = Math.Max(6, Math.Min(72, editor.FontSize + delta));
                editor.FontSize = newSize;
                e.Handled = true;
            }
        };

        editor.TextArea.Caret.PositionChanged += (s, e) => UpdateCursorPosition(editor);

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
        UpdateCursorPosition(editor);
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
            if (_wordHighlighters.TryGetValue(editor, out var wh))
            {
                editor.TextArea.TextView.BackgroundRenderers.Remove(wh);
                _wordHighlighters.Remove(editor);
            }
            if (_foldingManagers.TryGetValue(editor, out var fm))
            {
                _foldingManagers.Remove(editor);
            }
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
            if (_wordHighlighters.TryGetValue(editor, out var wh))
            {
                editor.TextArea.TextView.BackgroundRenderers.Remove(wh);
                _wordHighlighters.Remove(editor);
            }
            if (_foldingManagers.TryGetValue(editor, out var fm))
            {
                _foldingManagers.Remove(editor);
            }
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
        editor.Options.IndentationSize = settings.EditorTabWidth;
        editor.Options.ConvertTabsToSpaces = settings.EditorUseSpaces;
        editor.Options.ShowColumnRuler = settings.EditorShowColumnRuler;
        editor.Options.ColumnRulerPosition = settings.EditorColumnRulerPosition;
        editor.Options.ShowSpaces = settings.EditorShowSpaces;
        editor.Options.ShowTabs = settings.EditorShowTabs;
        editor.Options.ShowEndOfLine = settings.EditorShowEndOfLine;
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
    // CURSOR POSITION / ПОЗИЦИЯ КУРСОРА
    // ═══════════════════════════════════════════

    private void UpdateCursorPosition(TextEditor editor)
    {
        if (editor.Document == null) return;
        var offset = editor.CaretOffset;
        var line = editor.Document.GetLineByOffset(offset);
        var lineNum = line.LineNumber;
        var col = offset - line.Offset + 1;
        CursorPositionText.Text = $"Ln {lineNum}, Col {col}";

        var text = editor.Document.Text;
        var wordCount = string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        WordCountText.Text = $"{wordCount} {LocalizationService.Current.GetString("Editor.Words")}";
    }

    // ═══════════════════════════════════════════
    // GO TO LINE / ПЕРЕХОД К СТРОКЕ
    // ═══════════════════════════════════════════

    internal void GoToLine()
    {
        if (_vm.ActiveTab == null || !_editors.TryGetValue(_vm.ActiveTab, out var editor)) return;

        var inputDialog = new InputDialog(
            LocalizationService.Current.GetString("Editor.GoToLine"),
            LocalizationService.Current.GetString("Editor.GoToLine.Title"),
            "1");
        if (inputDialog.ShowDialog() != true) return;

        if (int.TryParse(inputDialog.InputValue, out var lineNumber) && lineNumber >= 1)
        {
            var maxLine = editor.Document.LineCount;
            lineNumber = Math.Min(lineNumber, maxLine);
            var line = editor.Document.GetLineByNumber(lineNumber);
            editor.CaretOffset = line.Offset;
            editor.ScrollToLine(lineNumber);
            editor.Focus();
        }
    }

    // ═══════════════════════════════════════════
    // SAVE AS / СОХРАНИТЬ КАК
    // ═══════════════════════════════════════════

    internal void SaveAsActiveTab()
    {
        if (_vm.ActiveTab == null) return;
        var tab = _vm.ActiveTab;
        if (!_editors.TryGetValue(tab, out var editor)) return;

        var dlg = new SaveFileDialog
        {
            Title = LocalizationService.Current.GetString("Editor.SaveAs"),
            FileName = tab.FileName,
            Filter = "All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var text = editor.Text;
            File.WriteAllText(dlg.FileName, text, System.Text.Encoding.UTF8);
            tab.FilePath = dlg.FileName;
            tab.FileName = Path.GetFileName(dlg.FileName);
            tab.Content = text;
            tab.OriginalContent = text;
            tab.IsModified = false;
            SyncTabControl();
            Title = tab.FileName;
            CursorPositionText.Text = LocalizationService.Current.GetString("Editor.Saved");
        }
        catch (Exception ex)
        {
            StyledMessageBoxWindow.Show(
                string.Format(LocalizationService.Current.GetString("Editor.SaveError"), ex.Message),
                LocalizationService.Current.GetString("Error.Title"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══════════════════════════════════════════
    // TOGGLE READ-ONLY / ПЕРЕКЛЮЧЕНИЕ РЕЖИМА ТОЛЬКО ЧТЕНИЕ
    // ═══════════════════════════════════════════

    internal void ToggleReadOnly()
    {
        if (_vm.ActiveTab == null || !_editors.TryGetValue(_vm.ActiveTab, out var editor)) return;
        editor.IsReadOnly = !editor.IsReadOnly;
        _vm.ActiveTab.IsReadOnly = editor.IsReadOnly;
        _vm.ModeIcon = editor.IsReadOnly ? "\uE714" : "\uE104";
    }

    // ═══════════════════════════════════════════
    // COMMENT TOGGLE / ПЕРЕКЛЮЧЕНИЕ КОММЕНТАРИЯ
    // ═══════════════════════════════════════════

    internal void ToggleComment()
    {
        if (_vm.ActiveTab == null || !_editors.TryGetValue(_vm.ActiveTab, out var editor)) return;
        if (editor.IsReadOnly) return;

        var (commentPrefix, commentSuffix) = GetCommentDelimiters(_vm.ActiveTab.FilePath);
        if (string.IsNullOrEmpty(commentPrefix)) return;

        var doc = editor.Document;
        var selectionStart = editor.SelectionStart;
        var selectionLength = editor.SelectionLength;

        int startLine, endLine;
        if (selectionLength > 0)
        {
            startLine = doc.GetLineByOffset(selectionStart).LineNumber;
            endLine = doc.GetLineByOffset(selectionStart + selectionLength).LineNumber;
        }
        else
        {
            var caretLine = doc.GetLineByOffset(editor.CaretOffset);
            startLine = endLine = caretLine.LineNumber;
        }

        bool allCommented = true;
        for (int i = startLine; i <= endLine; i++)
        {
            var line = doc.GetLineByNumber(i);
            var text = doc.GetText(line.Offset, line.Length).TrimStart();
            if (!text.StartsWith(commentPrefix, StringComparison.Ordinal))
            {
                allCommented = false;
                break;
            }
        }

        using (doc.RunUpdate())
        {
            for (int i = startLine; i <= endLine; i++)
            {
                var line = doc.GetLineByNumber(i);
                var lineText = doc.GetText(line.Offset, line.Length);

                if (allCommented)
                {
                    var trimmed = lineText.TrimStart();
                    if (trimmed.StartsWith(commentPrefix, StringComparison.Ordinal))
                    {
                        var indent = lineText.Length - lineText.TrimStart().Length;
                        var afterPrefix = trimmed[commentPrefix.Length..];
                        if (!string.IsNullOrEmpty(commentSuffix) && afterPrefix.EndsWith(commentSuffix, StringComparison.Ordinal))
                            afterPrefix = afterPrefix[..^commentSuffix.Length];
                        if (afterPrefix.Length > 0 && afterPrefix[0] == ' ')
                            afterPrefix = afterPrefix[1..];
                        var newText = lineText[..indent] + afterPrefix;
                        doc.Replace(line.Offset, line.Length, newText);
                    }
                }
                else
                {
                    var indent = lineText.Length - lineText.TrimStart().Length;
                    var newText = lineText[..indent] + commentPrefix + lineText[indent..] + commentSuffix;
                    doc.Replace(line.Offset, line.Length, newText);
                }
            }
        }
    }

    private static (string prefix, string suffix) GetCommentDelimiters(string filePath)
    {
        var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" or ".xml" or ".xaml" => ("<!-- ", " -->"),
            _ => (GetCommentPrefix(filePath), "")
        };
    }

    private static string GetCommentPrefix(string filePath)
    {
        var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
        return ext switch
        {
            ".cs" or ".js" or ".ts" or ".java" or ".cpp" or ".cc" or ".cxx" or ".h" or ".hpp"
                or ".css" or ".go" or ".rs" or ".php" => "//",
            ".py" or ".sh" or ".bash" or ".ps1" or ".yaml" or ".yml" => "#",
            ".html" or ".htm" or ".xml" or ".xaml" => "<!--",
            ".sql" => "--",
            _ => "//"
        };
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
        // FIXED: Unsubscribe ThemeChanged when window is actually closing (not cancelled).
        if (!e.Cancel)
            ((App)Application.Current).ThemeChanged -= OnAppThemeChanged;
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

    // ═══════════════════════════════════════════
    // BRACKET HIGHLIGHTER / ПОДСВЕТКА СКОБОК
    // ═══════════════════════════════════════════

    private class BracketHighlightRenderer : IBackgroundRenderer
    {
        private readonly TextEditor _editor;
        private static readonly Dictionary<char, char> _pairs = new()
        {
            ['('] = ')', [')'] = '(',
            ['{'] = '}', ['}'] = '{',
            ['['] = ']', [']'] = '[',
            ['<'] = '>', ['>'] = '<'
        };

        public BracketHighlightRenderer(TextEditor editor)
        {
            _editor = editor;
        }

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_editor.Document == null) return;
            var offset = _editor.CaretOffset;
            if (offset >= _editor.Document.TextLength) return;

            var c = _editor.Document.GetCharAt(offset);
            int? matchOffset = null;

            if (_pairs.ContainsKey(c))
            {
                matchOffset = FindMatch(offset, c);
            }
            else if (offset > 0)
            {
                var prev = _editor.Document.GetCharAt(offset - 1);
                if (_pairs.ContainsKey(prev))
                {
                    matchOffset = FindMatch(offset - 1, prev);
                }
            }

            if (matchOffset == null) return;

            var brush = new SolidColorBrush(Color.FromArgb(100, 100, 200, 255));
            brush.Freeze();

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new SimpleSegment(offset, 1)))
            {
                drawingContext.DrawRectangle(null, new Pen(brush, 1.5), rect);
            }
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new SimpleSegment(matchOffset.Value, 1)))
            {
                drawingContext.DrawRectangle(null, new Pen(brush, 1.5), rect);
            }
        }

        private int FindMatch(int offset, char open)
        {
            var close = _pairs[open];
            var isForward = open is '(' or '{' or '[' or '<';
            var doc = _editor.Document;
            var depth = 0;

            if (isForward)
            {
                for (int i = offset; i < doc.TextLength; i++)
                {
                    var ch = doc.GetCharAt(i);
                    if (ch == open) depth++;
                    else if (ch == close) depth--;
                    if (depth == 0) return i;
                }
            }
            else
            {
                for (int i = offset; i >= 0; i--)
                {
                    var ch = doc.GetCharAt(i);
                    if (ch == open) depth++;
                    else if (ch == close) depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }

    // ═══════════════════════════════════════════
    // CURRENT WORD HIGHLIGHTER / ПОДСВЕТКА ТЕКУЩЕГО СЛОВА
    // ═══════════════════════════════════════════

    private class CurrentWordHighlighter : IBackgroundRenderer
    {
        private readonly TextEditor _editor;
        private readonly SolidColorBrush _highlightBrush;
        private SimpleSegment? _currentWord;
        private List<ISegment> _segments = new();
        private string _lastWord = "";
        private Regex? _cachedRegex;

        public CurrentWordHighlighter(TextEditor editor)
        {
            _editor = editor;
            _highlightBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 100));
            _highlightBrush.Freeze();
            _editor.TextArea.Caret.PositionChanged += OnCaretChanged;
        }

        public KnownLayer Layer => KnownLayer.Selection;

        private void OnCaretChanged(object? sender, EventArgs e)
        {
            UpdateHighlight();
            _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        }

        private void UpdateHighlight()
        {
            if (_editor.Document == null) { _currentWord = null; _segments = new(); return; }

            var offset = _editor.CaretOffset;
            if (offset >= _editor.Document.TextLength) { _currentWord = null; _segments = new(); return; }

            var c = _editor.Document.GetCharAt(offset);
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || c == '(' || c == ')' || c == '{' || c == '}' || c == '[' || c == ']' || c == ';' || c == ',')
            { _currentWord = null; _segments = new(); return; }

            var wordStart = offset;
            var wordEnd = offset;

            while (wordStart > 0)
            {
                var prev = _editor.Document.GetCharAt(wordStart - 1);
                if (char.IsWhiteSpace(prev) || char.IsPunctuation(prev) || prev == '(' || prev == ')' || prev == '{' || prev == '}' || prev == '[' || prev == ']' || prev == ';' || prev == ',')
                    break;
                wordStart--;
            }

            while (wordEnd < _editor.Document.TextLength)
            {
                var next = _editor.Document.GetCharAt(wordEnd);
                if (char.IsWhiteSpace(next) || char.IsPunctuation(next) || next == '(' || next == ')' || next == '{' || next == '}' || next == '[' || next == ']' || next == ';' || next == ',')
                    break;
                wordEnd++;
            }

            var wordLength = wordEnd - wordStart;
            if (wordLength < 1) { _currentWord = null; _segments = new(); return; }

            var word = _editor.Document.GetText(wordStart, wordLength);
            if (word == _lastWord) return;
            _lastWord = word;

            _currentWord = new SimpleSegment(wordStart, wordLength);

            if (_cachedRegex == null || _cachedRegex.ToString() != $@"\b{Regex.Escape(word)}\b")
                _cachedRegex = new Regex($@"\b{Regex.Escape(word)}\b", RegexOptions.Compiled);

            var found = new List<ISegment>();
            var text = _editor.Document.Text;
            foreach (Match m in _cachedRegex.Matches(text))
            {
                found.Add(new SimpleSegment(m.Index, m.Length));
            }
            _segments = found;
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_currentWord == null) return;
            foreach (var seg in _segments)
            {
                if (seg.Offset == _currentWord.Offset && seg.Length == _currentWord.Length) continue;
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, seg))
                {
                    drawingContext.DrawRectangle(_highlightBrush, null, rect);
                }
            }
        }
    }

    private class SimpleSegment : ISegment
    {
        public int Offset { get; }
        public int Length { get; }
        public int EndOffset => Offset + Length;

        public SimpleSegment(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }
    }

    // ═══════════════════════════════════════════
    // INPUT DIALOG / ДИАЛОГ ВВОДА
    // ═══════════════════════════════════════════

    private class InputDialog : Window
    {
        public string InputValue { get; private set; } = "";

        public InputDialog(string prompt, string title, string defaultValue = "")
        {
            Title = title;
            Width = 350;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.ToolWindow;
            Background = (SolidColorBrush)Application.Current.TryFindResource("BgDarkBrush") ?? Brushes.Black;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var contentPanel = new StackPanel { Margin = new Thickness(16, 12, 16, 0) };
            var promptText = new TextBlock
            {
                Text = prompt,
                Foreground = (SolidColorBrush)Application.Current.TryFindResource("FgLightBrush") ?? Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8)
            };
            contentPanel.Children.Add(promptText);

            var inputBox = new TextBox
            {
                Text = defaultValue,
                FontSize = 14,
                Padding = new Thickness(6, 4, 6, 4),
                Background = (SolidColorBrush)Application.Current.TryFindResource("VsEditorBg") ?? Brushes.Black,
                Foreground = (SolidColorBrush)Application.Current.TryFindResource("VsEditorFg") ?? Brushes.White,
                BorderBrush = (SolidColorBrush)Application.Current.TryFindResource("AccentBrush") ?? Brushes.CornflowerBlue,
                BorderThickness = new Thickness(1)
            };
            inputBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { InputValue = inputBox.Text; DialogResult = true; }
                else if (e.Key == Key.Escape) { DialogResult = false; }
            };
            contentPanel.Children.Add(inputBox);
            Grid.SetRow(contentPanel, 0);
            grid.Children.Add(contentPanel);

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16, 8, 16, 12)
            };
            var okBtn = new Button
            {
                Content = "OK",
                Padding = new Thickness(16, 6, 16, 6),
                FontSize = 12,
                Margin = new Thickness(0, 0, 8, 0),
                Background = (SolidColorBrush)Application.Current.TryFindResource("AccentBrush") ?? Brushes.CornflowerBlue,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                IsDefault = true
            };
            okBtn.Click += (s, e) => { InputValue = inputBox.Text; DialogResult = true; };
            buttonsPanel.Children.Add(okBtn);

            var cancelBtn = new Button
            {
                Content = LocalizationService.Current.GetString("Dialog.Cancel"),
                Padding = new Thickness(16, 6, 16, 6),
                FontSize = 12,
                Background = (SolidColorBrush)Application.Current.TryFindResource("BgHeaderBrush") ?? Brushes.DarkGray,
                Foreground = (SolidColorBrush)Application.Current.TryFindResource("FgLightBrush") ?? Brushes.White,
                BorderBrush = (SolidColorBrush)Application.Current.TryFindResource("BorderBrush") ?? Brushes.Gray,
                BorderThickness = new Thickness(1),
                IsCancel = true
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; };
            buttonsPanel.Children.Add(cancelBtn);
            Grid.SetRow(buttonsPanel, 1);
            grid.Children.Add(buttonsPanel);

            Content = grid;

            Loaded += (s, e) => { inputBox.Focus(); inputBox.SelectAll(); };
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

    /// <summary>Перейти к строке (Ctrl+G). / Go to line (Ctrl+G).</summary>
    [RelayCommand]
    private void GoToLine() => _window.GoToLine();

    /// <summary>Переключить комментарий (Ctrl+.). / Toggle comment (Ctrl+.).</summary>
    [RelayCommand]
    private void CommentLine() => _window.ToggleComment();

    /// <summary>Сохранить как (Ctrl+Shift+S). / Save As (Ctrl+Shift+S).</summary>
    [RelayCommand]
    private void SaveAs()
    {
        if (ActiveTab == null) return;
        _window.SaveAsActiveTab();
    }

    /// <summary>Переключить режим «только чтение». / Toggle read-only mode.</summary>
    [RelayCommand]
    private void ToggleReadOnly()
    {
        if (ActiveTab == null) return;
        _window.ToggleReadOnly();
    }

    /// <summary>
    /// Проверяет все вкладки на несохранённые изменения перед закрытием окна.
    /// Checks all tabs for unsaved changes before closing the window.
    /// </summary>
    public bool CheckAllUnsavedChanges()
    {
        foreach (var tab in Tabs.ToList())
        {
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

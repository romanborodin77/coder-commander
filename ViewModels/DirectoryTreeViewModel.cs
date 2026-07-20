using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// ViewModel модального окна дерева каталогов (ph5.6).
/// ViewModel for the modal directory tree window (ph5.6).
/// Управляет деревом, фильтрацией, навигацией и подтверждением выбора.
/// Manages the tree, filtering, navigation, and selection confirmation.
/// </summary>
public partial class DirectoryTreeViewModel : ObservableObject
{
    /// <summary>Текущий путь панели (начальное положение дерева). / Current panel path (initial tree position).</summary>
    private readonly string _initialPath;

    /// <summary>Целевая панель (для навигации). / Target panel (for navigation).</summary>
    private readonly PanelViewModel _targetPanel;

    /// <summary>Корневые узлы (диски). / Root nodes (drives).</summary>
    public IReadOnlyList<DirectoryTreeNode> Roots { get; }

    /// <summary>Развёрнутый корневой узел. / Expanded root node.</summary>
    [ObservableProperty] private DirectoryTreeNode? _expandedRoot;

    /// <summary>Текст фильтра (быстрый поиск по именам). / Filter text (quick search by name).</summary>
    [ObservableProperty] private string _filterText = "";

    /// <summary>Выбранный узел в дереве. / Selected node in the tree.</summary>
    [ObservableProperty] private DirectoryTreeNode? _selectedNode;

    /// <summary>
    /// Создаёт ViewModel с указанием стартового пути и целевой панели.
    /// Creates ViewModel with the specified start path and target panel.
    /// </summary>
    /// <param name="initialPath">Начальный путь (текущая папка панели). / Initial path (panel's current folder).</param>
    /// <param name="targetPanel">Целевая панель для навигации. / Target panel for navigation.</param>
    public DirectoryTreeViewModel(string initialPath, PanelViewModel targetPanel)
    {
        _initialPath = initialPath;
        _targetPanel = targetPanel;
        Roots = DirectoryTreeService.GetRootNodes();
    }

    /// <summary>
    /// Выбранный узел изменился: разворачиваем до выбранного и подгружаем детей.
    /// Selected node changed: expand to the selected node and load children.
    /// </summary>
    partial void OnSelectedNodeChanged(DirectoryTreeNode? value)
    {
        if (value is null) return;
        _ = ExpandToNodeAsync(value);
    }

    /// <summary>
    /// Разворачивает дерево от корня до указанного узла (рекурсивно).
    /// Expands the tree from root to the specified node (recursively).
    /// </summary>
    private async Task ExpandToNodeAsync(DirectoryTreeNode node)
    {
        // Находим корень для этого узла
        var root = FindRootForNode(node);
        if (root is null) return;

        ExpandedRoot = root;
        await root.LoadChildrenAsync();

        // Рекурсивно раскрываем путь к узлу
        var segments = node.FullPath
            .Replace(root.FullPath, "", StringComparison.OrdinalIgnoreCase)
            .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        foreach (var seg in segments)
        {
            await current.LoadChildrenAsync();
            var child = current.Children.FirstOrDefault(c =>
                string.Equals(c.DisplayName, seg, StringComparison.OrdinalIgnoreCase));
            if (child is null) break;
            child.IsExpanded = true;
            await child.LoadChildrenAsync();
            current = child;
        }
    }

    /// <summary>
    /// Находит корневой узел для указанного узла.
    /// Finds the root node for the specified node.
    /// </summary>
    private DirectoryTreeNode? FindRootForNode(DirectoryTreeNode node)
    {
        var root = DirectoryTreeService.FindRootForPath(node.FullPath, Roots);
        return root ?? Roots.FirstOrDefault();
    }

    /// <summary>
    /// Фильтр текста изменился: автоматически раскрываем дерево до первого совпадения.
    /// Filter text changed: automatically expand tree to the first match.
    /// </summary>
    partial void OnFilterTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        _ = FindByFilterAsync(value);
    }

    /// <summary>
    /// Ищет первый узел, содержащий текст фильтра, и выбирает его.
    /// Finds the first node containing the filter text and selects it.
    /// </summary>
    private async Task FindByFilterAsync(string filter)
    {
        foreach (var root in Roots)
        {
            await root.LoadChildrenAsync();
            var match = await FindNodeRecursive(root, filter);
            if (match is not null)
            {
                SelectedNode = match;
                ExpandedRoot = FindRootForNode(match);
                return;
            }
        }
    }

    /// <summary>
    /// Рекурсивный поиск узла по подстроке имени.
    /// Recursively searches for a node by name substring.
    /// </summary>
    private async Task<DirectoryTreeNode?> FindNodeRecursive(
        DirectoryTreeNode node, string filter)
    {
        if (node.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            return node;

        await node.LoadChildrenAsync();
        foreach (var child in node.Children)
        {
            var result = await FindNodeRecursive(child, filter);
            if (result is not null) return result;
        }
        return null;
    }

    /// <summary>
    /// Команда выбора узла: навигация панели + закрытие окна.
    /// Command to select a node: navigate panel + close window.
    /// </summary>
    [RelayCommand]
    private async Task SelectNodeAsync(DirectoryTreeNode? node)
    {
        if (node is null) return;
        await _targetPanel.NavigateToAsync(node.FullPath);
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Команда отмены (ESC). / Cancel command (ESC).
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Запрос закрытия окна (делегируется View). / Request to close the window (delegated to the View).
    /// </summary>
    public Action? RequestClose { get; set; }
}

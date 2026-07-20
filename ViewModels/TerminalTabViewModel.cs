using CommunityToolkit.Mvvm.ComponentModel;

namespace CoderCommander.ViewModels;

/// <summary>
/// Модель представления для одной вкладки терминала. Содержит идентификатор, тип shell и отображаемый заголовок.
/// ViewModel for a single terminal tab. Holds the tab ID, shell type, and display title.
/// </summary>
public partial class TerminalTabViewModel : ObservableObject
{
    private static int _counter;

    [ObservableProperty] private string _title = "";
    /// <summary>Тип shell для этой вкладки: "cmd" или "powershell".</summary>
    public string Shell { get; }

    /// <summary>Истина, когда shell для этой вкладки запускается (защита от повторного старта).</summary>
    public bool IsShellStarting { get; set; }

    /// <summary>Уникальный идентификатор вкладки, назначаемый через Interlocked.Increment.</summary>
    public int Id { get; }
    
    /// <summary>Порядковый номер для отображения (обновляется при пересортировке).</summary>
    [ObservableProperty] private int _displayNumber;

    /// <summary>
    /// Создаёт новую вкладку терминала с указанным типом shell.
    /// Creates a new terminal tab with the specified shell type.
    /// </summary>
    /// <param name="shell">Тип оболочки: "cmd" или "powershell".</param>
    public TerminalTabViewModel(string shell)
    {
        Shell = shell;
        Id = Interlocked.Increment(ref _counter);
        DisplayNumber = Id;
        UpdateTitle();
    }
    
    /// <summary>
    /// Обновляет заголовок вкладки на основе типа shell и отображаемого номера.
    /// Updates the tab title based on the shell type and display number.
    /// </summary>
    public void UpdateTitle()
    {
        var shellName = Shell switch
        {
            "powershell" => "PowerShell",
            "pwsh" => "pwsh",
            _ => "CMD"
        };
        Title = $"{shellName} #{DisplayNumber}";
    }
}

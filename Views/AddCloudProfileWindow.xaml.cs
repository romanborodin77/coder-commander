using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CoderCommander.Models;

namespace CoderCommander.Views;

/// <summary>
/// Диалог добавления/редактирования облачного профиля с выбором провайдера.
/// Dialog for adding/editing a cloud profile with provider selection.
/// </summary>
public partial class AddCloudProfileWindow : Window
{
    /// <summary>Созданный профиль (результат диалога). / Created profile (dialog result).</summary>
    public CloudProfile? ResultProfile { get; private set; }

    /// <summary>Режим редактирования. / Edit mode.</summary>
    public bool EditMode { get; set; }

    /// <summary>Редактируемый профиль (при EditMode=true). / Profile to edit (when EditMode=true).</summary>
    public CloudProfile? EditProfile { get; set; }

    /// <summary>Словарь полей ввода для каждого провайдера. / Input fields for each provider.</summary>
    private readonly Dictionary<string, List<FrameworkElement>> _providerFields = new();

    public AddCloudProfileWindow()
    {
        InitializeComponent();
        BuildFields();
        ProviderCombo.SelectedIndex = 0;
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Инициализация после установки свойств (EditMode/EditProfile).
    /// Runs after EditMode/EditProfile are set via object initializer.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var L = Services.LocalizationService.Current;
        TitleText.Text = EditMode ? L.GetString("Cloud.EditProfileTitle") : L.GetString("Cloud.AddProfileTitle");
        AddButtonText.Text = EditMode ? L.GetString("Cloud.EditBtn") : L.GetString("Cloud.AddBtnConfirm");

        // Если режим редактирования — заполняем поля из профиля.
        if (EditMode && EditProfile is not null)
        {
            ProfileName.Text = EditProfile.Name;
            foreach (ComboBoxItem item in ProviderCombo.Items)
            {
                if (item.Tag?.ToString() == EditProfile.Provider.ToString())
                {
                    ProviderCombo.SelectedItem = item;
                    break;
                }
            }
            FillFieldsFromProfile(EditProfile);
        }
    }

    /// <summary>
    /// Создаёт поля ввода для каждого провайдера.
    /// Creates input fields for each provider.
    /// </summary>
    private void BuildFields()
    {
        // S3
        _providerFields["S3"] = CreateFields("Endpoint (https://s3.amazonaws.com)", "Access Key", "Secret Key", "Region (us-east-1)", "Bucket");
        // AzureBlob
        _providerFields["AzureBlob"] = CreateFields("Connection String", "Container");
        // GoogleDrive
        _providerFields["GoogleDrive"] = CreateFields("Client ID", "Client Secret", "Refresh Token");
        // YandexDisk
        _providerFields["YandexDisk"] = CreateFields("OAuth Token", "Root path (/)");
        // NextCloud
        _providerFields["NextCloud"] = CreateFields("Server URL (https://cloud.example.com)", "Username", "Password", "Root path (/)");
        // WebDAV
        _providerFields["WebDAV"] = CreateFields("WebDAV URL (https://example.com/remote.php/dav/files/user/)", "Username", "Password", "Root path (/)");
    }

    /// <summary>
    /// Создаёт список TextBox-полей с метками.
    /// Creates a list of labeled TextBox fields.
    /// </summary>
    private static List<FrameworkElement> CreateFields(params string[] labels)
    {
        var list = new List<FrameworkElement>();
        var fgBrush = (System.Windows.Media.Brush)Application.Current.FindResource("FgDimBrush");
        foreach (var label in labels)
        {
            var tb = new TextBox
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 4, 0, 4),
                Tag = label
            };
            var lbl = new TextBlock
            {
                Text = label,
                Foreground = fgBrush,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Width = 220
            };
            list.Add(lbl);
            list.Add(tb);
        }
        return list;
    }

    /// <summary>
    /// Обработчик смены провайдера: показывает соответствующие поля.
    /// Handles provider change: shows relevant fields.
    /// </summary>
    private void Provider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderCombo.SelectedItem is not ComboBoxItem selected) return;
        var tag = selected.Tag.ToString()!;
        ShowFields(tag);
    }

    /// <summary>
    /// Поля ввода для текущего провайдера.
    /// Shows input fields for the selected provider.
    /// </summary>
    private void ShowFields(string provider)
    {
        FieldsPanel.Children.Clear();
        if (_providerFields.TryGetValue(provider, out var fields))
        {
            foreach (var field in fields)
                FieldsPanel.Children.Add(field);
        }

        // SSL cert ignore — только для WebDAV и NextCloud.
        IgnoreCertCheck.Visibility = (provider == "WebDAV" || provider == "NextCloud")
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// Заполняет поля значениями из профиля (при редактировании).
    /// Fills fields from profile values (when editing).
    /// </summary>
    private void FillFieldsFromProfile(CloudProfile profile)
    {
        var provider = profile.Provider.ToString();
        if (!_providerFields.TryGetValue(provider, out var fields)) return;

        IgnoreCertCheck.IsChecked = profile.IgnoreCertificateErrors;

        // Заполняем поля в зависимости от провайдера.
        switch (profile.Provider)
        {
            case CloudProvider.S3:
                SetFieldValue(fields, 0, profile.Endpoint ?? "");
                SetFieldValue(fields, 1, profile.Credentials.TryGetValue("AccessKey", out var ak) ? ak : "");
                SetFieldValue(fields, 2, profile.Credentials.TryGetValue("SecretKey", out var sk) ? sk : "");
                SetFieldValue(fields, 3, profile.Region ?? "");
                SetFieldValue(fields, 4, profile.BucketOrContainer ?? "");
                break;
            case CloudProvider.AzureBlob:
                SetFieldValue(fields, 0, profile.Credentials.TryGetValue("ConnectionString", out var cs) ? cs : "");
                SetFieldValue(fields, 1, profile.BucketOrContainer ?? "");
                break;
            case CloudProvider.GoogleDrive:
                SetFieldValue(fields, 0, profile.Credentials.TryGetValue("ClientId", out var cid) ? cid : "");
                SetFieldValue(fields, 1, profile.Credentials.TryGetValue("ClientSecret", out var csec) ? csec : "");
                SetFieldValue(fields, 2, profile.Credentials.TryGetValue("RefreshToken", out var rt) ? rt : "");
                break;
            case CloudProvider.YandexDisk:
                SetFieldValue(fields, 0, profile.Credentials.TryGetValue("OAuthToken", out var token) ? token : "");
                SetFieldValue(fields, 1, profile.RootPath ?? "");
                break;
            case CloudProvider.NextCloud:
            case CloudProvider.WebDAV:
                SetFieldValue(fields, 0, profile.Endpoint ?? "");
                SetFieldValue(fields, 1, profile.Credentials.TryGetValue("Username", out var user) ? user : "");
                SetFieldValue(fields, 2, profile.Credentials.TryGetValue("Password", out var pass) ? pass : "");
                SetFieldValue(fields, 3, profile.RootPath ?? "");
                break;
        }
    }

    /// <summary>Устанавливает значение TextBox по индексу. / Sets TextBox value by index.</summary>
    private static void SetFieldValue(List<FrameworkElement> fields, int index, string value)
    {
        var textBoxIndex = index * 2 + 1;
        if (textBoxIndex < fields.Count && fields[textBoxIndex] is TextBox tb)
            tb.Text = value;
    }

    /// <summary>
    /// Получает значение поля по индексу для текущего провайдера.
    /// Gets the value of a field by index for the current provider.
    /// </summary>
    private string GetFieldValue(int index)
    {
        if (ProviderCombo.SelectedItem is not ComboBoxItem selected) return "";
        var tag = selected.Tag.ToString()!;
        if (!_providerFields.TryGetValue(tag, out var fields)) return "";

        // Поля чередуются: Label, TextBox, Label, TextBox, ...
        var textBoxIndex = index * 2 + 1;
        if (textBoxIndex < fields.Count && fields[textBoxIndex] is TextBox tb)
            return tb.Text.Trim();
        return "";
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProfileName.Text))
        {
            MessageBox.Show("Введите имя профиля", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (ProviderCombo.SelectedItem is not ComboBoxItem selected)
        {
            MessageBox.Show("Выберите провайдер", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var tag = selected.Tag.ToString()!;
        var provider = tag switch
        {
            "S3" => CloudProvider.S3,
            "AzureBlob" => CloudProvider.AzureBlob,
            "GoogleDrive" => CloudProvider.GoogleDrive,
            "YandexDisk" => CloudProvider.YandexDisk,
            "NextCloud" => CloudProvider.NextCloud,
            "WebDAV" => CloudProvider.WebDAV,
            _ => CloudProvider.S3
        };

        var profile = new CloudProfile
        {
            Name = ProfileName.Text.Trim(),
            Provider = provider,
            IgnoreCertificateErrors = IgnoreCertCheck.IsChecked == true
        };

        // Заполняем Credentials в зависимости от провайдера.
        switch (provider)
        {
            case CloudProvider.S3:
                var ep = GetFieldValue(0); if (!string.IsNullOrEmpty(ep)) profile.Endpoint = ep;
                var ak = GetFieldValue(1); if (!string.IsNullOrEmpty(ak)) profile.Credentials["AccessKey"] = ak;
                var sk = GetFieldValue(2); if (!string.IsNullOrEmpty(sk)) profile.Credentials["SecretKey"] = sk;
                profile.Region = GetFieldValue(3);
                var bucket = GetFieldValue(4); if (!string.IsNullOrEmpty(bucket)) profile.BucketOrContainer = bucket;
                break;
            case CloudProvider.AzureBlob:
                var cs = GetFieldValue(0); if (!string.IsNullOrEmpty(cs)) profile.Credentials["ConnectionString"] = cs;
                var container = GetFieldValue(1); if (!string.IsNullOrEmpty(container)) profile.BucketOrContainer = container;
                break;
            case CloudProvider.GoogleDrive:
                var cid = GetFieldValue(0); if (!string.IsNullOrEmpty(cid)) profile.Credentials["ClientId"] = cid;
                var csecret = GetFieldValue(1); if (!string.IsNullOrEmpty(csecret)) profile.Credentials["ClientSecret"] = csecret;
                var rt = GetFieldValue(2); if (!string.IsNullOrEmpty(rt)) profile.Credentials["RefreshToken"] = rt;
                break;
            case CloudProvider.YandexDisk:
                var token = GetFieldValue(0); if (!string.IsNullOrEmpty(token)) profile.Credentials["OAuthToken"] = token;
                var root = GetFieldValue(1); if (!string.IsNullOrEmpty(root)) profile.RootPath = root;
                break;
            case CloudProvider.NextCloud:
            case CloudProvider.WebDAV:
                var url = GetFieldValue(0); if (!string.IsNullOrEmpty(url)) profile.Endpoint = url;
                var user = GetFieldValue(1); if (!string.IsNullOrEmpty(user)) profile.Credentials["Username"] = user;
                var pass = GetFieldValue(2); if (!string.IsNullOrEmpty(pass)) profile.Credentials["Password"] = pass;
                var rp = GetFieldValue(3); if (!string.IsNullOrEmpty(rp)) profile.RootPath = rp;
                break;
        }

        ResultProfile = profile;
        DialogResult = true;
        Close();
    }
}

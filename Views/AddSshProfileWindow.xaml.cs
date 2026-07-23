using System;
using System.Windows;
using System.Windows.Input;
using CoderCommander.Models;
using CoderCommander.Services;
using Microsoft.Win32;

namespace CoderCommander.Views;

public partial class AddSshProfileWindow : Window
{
    private SshProfile? _editingProfile;
    public SshProfile? ResultProfile { get; private set; }

    public AddSshProfileWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => ProfileName.Focus();
        TitleText.Text = LocalizationService.Current.GetString("Ssh.NewProfile");
        AddButtonText.Text = LocalizationService.Current.GetString("Ssh.Add");
        CancelButtonText.Text = LocalizationService.Current.GetString("Dialog.Cancel");
    }

    public void LoadForEdit(SshProfile profile)
    {
        _editingProfile = profile;
        ProfileName.Text = profile.Name;
        Host.Text = profile.Host;
        User.Text = profile.User;
        Port.Text = profile.Port.ToString();
        RemotePath.Text = profile.RemotePath;
        IdentityFile.Text = profile.IdentityFile ?? "";
        Password.Password = profile.Password ?? "";
        TitleText.Text = LocalizationService.Current.GetString("Ssh.Editor");
        AddButtonText.Text = LocalizationService.Current.GetString("Ssh.Save");
        Title = LocalizationService.Current.GetString("Ssh.Editor");
        CancelButtonText.Text = LocalizationService.Current.GetString("Dialog.Cancel");
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void BrowseIdentityFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Private key files (*.pem;*.ppk;*.key;*.p12;*.pfx)|*.pem;*.ppk;*.key;*.p12;*.pfx|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            IdentityFile.Text = dlg.FileName;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var name = ProfileName.Text.Trim();
        var host = Host.Text.Trim();
        var user = User.Text.Trim();
        var remotePath = RemotePath.Text.Trim();
        var identityFile = IdentityFile.Text.Trim();
        var password = Password.Password;

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(host))
        {
            StyledMessageBoxWindow.Show(LocalizationService.Current.GetString("Ssh.NameHostRequired"), "", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!int.TryParse(Port.Text, out var port) || port < 1 || port > 65535)
        {
            StyledMessageBoxWindow.Show(LocalizationService.Current.GetString("Ssh.Port") + " (1-65535)", "", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrEmpty(user))
            user = Environment.UserName;
        if (string.IsNullOrEmpty(remotePath))
            remotePath = "/";

        ResultProfile = new SshProfile(name, host, user, port, remotePath, string.IsNullOrEmpty(identityFile) ? null : identityFile, password);
        DialogResult = true;
        Close();
    }
}

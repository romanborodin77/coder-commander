using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.FileSystem;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>
/// Сервис управления подключениями к облачным хранилищам: CRUD профилей, подключение/отключение.
/// Service for managing cloud storage connections: profile CRUD, connect/disconnect.
/// Профили хранятся в settings.json (раздел CloudProfiles).
/// Profiles are stored in settings.json (CloudProfiles section).
/// </summary>
public sealed class CloudStorageService
{
    /// <summary>
    /// Загружает профили облачных хранилищ из настроек.
    /// Loads cloud storage profiles from settings.
    /// </summary>
    public IReadOnlyList<CloudProfile> GetProfiles()
    {
        var settings = SettingsService.Load();
        return settings.CloudProfiles;
    }

    /// <summary>
    /// Получает профиль по идентификатору.
    /// Gets a profile by identifier.
    /// </summary>
    public CloudProfile? GetProfile(string id)
    {
        return GetProfiles().FirstOrDefault(p => p.Id == id);
    }

    /// <summary>
    /// Добавляет новый профиль и сохраняет настройки.
    /// Adds a new profile and saves settings.
    /// </summary>
    public void AddProfile(CloudProfile profile)
    {
        var settings = SettingsService.Load();
        settings.CloudProfiles.Add(profile);
        SettingsService.Save(settings);
    }

    /// <summary>
    /// Обновляет существующий профиль.
    /// Updates an existing profile.
    /// </summary>
    public void UpdateProfile(CloudProfile profile)
    {
        var settings = SettingsService.Load();
        var idx = settings.CloudProfiles.FindIndex(p => p.Id == profile.Id);
        if (idx >= 0)
        {
            settings.CloudProfiles[idx] = profile;
            SettingsService.Save(settings);
        }
    }

    /// <summary>
    /// Удаляет профиль по идентификатору.
    /// Deletes a profile by identifier.
    /// </summary>
    public void DeleteProfile(string id)
    {
        var settings = SettingsService.Load();
        settings.CloudProfiles.RemoveAll(p => p.Id == id);
        SettingsService.Save(settings);
    }

    /// <summary>
    /// Создаёт и подключает облачную файловую систему по профилю.
    /// Creates and connects a cloud file system from a profile.
    /// </summary>
    public async Task<CloudFileSystem> ConnectAsync(CloudProfile profile, CancellationToken ct = default)
    {
        var fs = CreateFileSystem(profile);
        await fs.ConnectAsync(ct);
        return fs;
    }

    /// <summary>
    /// Создаёт облачную файловую систему по профилю (без подключения).
    /// Creates a cloud file system from a profile (without connecting).
    /// </summary>
    public static CloudFileSystem CreateFileSystem(CloudProfile profile)
    {
        return profile.Provider switch
        {
            CloudProvider.S3 => new S3FileSystem(profile),
            CloudProvider.AzureBlob => new AzureBlobFileSystem(profile),
            CloudProvider.GoogleDrive => new GDriveFileSystem(profile),
            CloudProvider.YandexDisk => new YandexDiskFileSystem(profile),
            CloudProvider.NextCloud => new NextCloudFileSystem(profile),
            _ => throw new ArgumentException($"Unknown cloud provider: {profile.Provider}")
        };
    }
}

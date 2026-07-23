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
/// Пароли/токены шифруются через DPAPI при сохранении.
/// Profiles are stored in settings.json (CloudProfiles section).
/// Passwords/tokens are encrypted via DPAPI on save.
/// </summary>
public sealed class CloudStorageService
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "OAuthToken", "AccessKey", "SecretKey",
        "ClientSecret", "RefreshToken", "AccessToken"
    };

    /// <summary>
    /// Загружает профили облачных хранилищ из настроек.
    /// Loads cloud storage profiles from settings.
    /// </summary>
    public IReadOnlyList<CloudProfile> GetProfiles()
    {
        var settings = SettingsService.Load();
        // Работаем с копиями, чтобы не портить кэш settings (шифрованные значения на диске).
        // Work with copies to avoid corrupting the settings cache (encrypted values on disk).
        return settings.CloudProfiles.Select(CloneAndDecrypt).ToList();
    }

    /// <summary>
    /// Получает профиль по идентификатору.
    /// Gets a profile by identifier.
    /// </summary>
    public CloudProfile? GetProfile(string id)
    {
        var settings = SettingsService.Load();
        var source = settings.CloudProfiles.FirstOrDefault(p => p.Id == id);
        return source is null ? null : CloneAndDecrypt(source);
    }

    /// <summary>
    /// Добавляет новый профиль и сохраняет настройки.
    /// Adds a new profile and saves settings.
    /// </summary>
    public void AddProfile(CloudProfile profile)
    {
        var clone = CloneForStorage(profile);
        EncryptCredentials(clone);
        var settings = SettingsService.Load();
        settings.CloudProfiles.Add(clone);
        SettingsService.Save(settings);
    }

    /// <summary>
    /// Обновляет существующий профиль.
    /// Updates an existing profile.
    /// </summary>
    public void UpdateProfile(CloudProfile profile)
    {
        var clone = CloneForStorage(profile);
        EncryptCredentials(clone);
        var settings = SettingsService.Load();
        var idx = settings.CloudProfiles.FindIndex(p => p.Id == profile.Id);
        if (idx >= 0)
        {
            settings.CloudProfiles[idx] = clone;
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
        DecryptCredentials(profile);
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
            CloudProvider.WebDAV => new WebDavFileSystem(profile),
            _ => throw new ArgumentException($"Unknown cloud provider: {profile.Provider}")
        };
    }

    /// <summary>
    /// Шифрует чувствительные значения Credentials через DPAPI.
    /// Encrypts sensitive Credential values via DPAPI.
    /// </summary>
    private static void EncryptCredentials(CloudProfile profile)
    {
        foreach (var key in profile.Credentials.Keys.ToList())
        {
            if (SensitiveKeys.Contains(key) && !string.IsNullOrEmpty(profile.Credentials[key]))
            {
                var val = profile.Credentials[key]!;
                if (!CredentialProtector.IsProtected(val))
                    profile.Credentials[key] = CredentialProtector.Protect(val);
            }
        }
    }

    /// <summary>
    /// Дешифрует чувствительные значения Credentials.
    /// Decrypts sensitive Credential values.
    /// </summary>
    private static void DecryptCredentials(CloudProfile profile)
    {
        foreach (var key in profile.Credentials.Keys.ToList())
        {
            if (SensitiveKeys.Contains(key) && !string.IsNullOrEmpty(profile.Credentials[key]))
                profile.Credentials[key] = CredentialProtector.Unprotect(profile.Credentials[key]!);
        }
    }

    /// <summary>
    /// Создаёт копию профиля для безопасного сохранения (без мутации оригинала).
    /// Creates a profile copy for safe saving (without mutating the original).
    /// </summary>
    private static CloudProfile CloneForStorage(CloudProfile source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Provider = source.Provider,
        Credentials = new Dictionary<string, string>(source.Credentials),
        Endpoint = source.Endpoint,
        Region = source.Region,
        BucketOrContainer = source.BucketOrContainer,
        RootPath = source.RootPath,
        IgnoreCertificateErrors = source.IgnoreCertificateErrors
    };

    /// <summary>
    /// Создаёт копию профиля и дешифрует её Credentials (без мутации оригинала).
    /// Creates a profile copy and decrypts its Credentials (without mutating the original).
    /// </summary>
    private static CloudProfile CloneAndDecrypt(CloudProfile source)
    {
        var clone = CloneForStorage(source);
        DecryptCredentials(clone);
        return clone;
    }
}

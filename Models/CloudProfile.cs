using System;
using System.Collections.Generic;

namespace CoderCommander.Models;

/// <summary>
/// Тип облачного провайдера. / Cloud provider type.
/// </summary>
public enum CloudProvider
{
    /// <summary>Amazon S3 или S3-совместимое хранилище. / Amazon S3 or S3-compatible storage.</summary>
    S3,
    /// <summary>Azure Blob Storage. / Azure Blob Storage.</summary>
    AzureBlob,
    /// <summary>Google Drive. / Google Drive.</summary>
    GoogleDrive,
    /// <summary>Yandex Disk. / Yandex Disk.</summary>
    YandexDisk,
    /// <summary>NextCloud (WebDAV). / NextCloud (WebDAV).</summary>
    NextCloud
}

/// <summary>
/// Профиль подключения к облачному хранилищу.
/// Connection profile for cloud storage.
/// </summary>
public class CloudProfile
{
    /// <summary>Уникальный идентификатор профиля. / Unique profile identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Отображаемое имя профиля. / Display name of the profile.</summary>
    public string Name { get; set; } = "";

    /// <summary>Тип облачного провайдера. / Cloud provider type.</summary>
    public CloudProvider Provider { get; set; } = CloudProvider.S3;

    /// <summary>
    /// Учётные данные (ключи, токены). Хранятся в plain text (TODO: шифрование).
    /// Credentials (keys, tokens). Stored in plain text (TODO: encryption).
    /// </summary>
    public Dictionary<string, string> Credentials { get; set; } = new();

    /// <summary>Конечная точка (для S3-совместимых хранилищ). / Endpoint (for S3-compatible storages).</summary>
    public string? Endpoint { get; set; }

    /// <summary>Регион. / Region.</summary>
    public string? Region { get; set; }

    /// <summary>Имя бакета или контейнера. / Bucket or container name.</summary>
    public string? BucketOrContainer { get; set; }

    /// <summary>Корневая папка в облаке (по умолчанию "/"). / Root folder in the cloud (default "/").</summary>
    public string? RootPath { get; set; }
}

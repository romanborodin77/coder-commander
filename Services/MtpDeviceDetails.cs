using CoderCommander.FileSystem;
using MediaDevices;

namespace CoderCommander.Services;

/// <summary>One storage volume on a device, as the Devices dialog shows it.</summary>
/// <param name="Name">The storage's own name, e.g. "Internal shared storage".</param>
/// <param name="TotalBytes">Capacity, or 0 when the device does not report one.</param>
/// <param name="FreeBytes">Free space, or 0 when the device does not report it.</param>
public sealed record MtpStorageInfo(string Name, long TotalBytes, long FreeBytes);

/// <summary>
/// Everything the Devices dialog shows about one connected MTP device. Read once, on demand -
/// none of it changes while the device is plugged in except the power level, and nothing here is
/// worth a background poll of its own.
/// </summary>
public sealed record MtpDeviceDetails(
    string DeviceId,
    string Name,
    string Manufacturer,
    string Model,
    string DeviceType,
    string SerialNumber,
    string FirmwareVersion,
    string Protocol,
    string Transport,
    string PowerSource,
    int PowerLevel,
    bool InUse,
    IReadOnlyList<MtpStorageInfo> Storages)
{
    /// <summary>
    /// Reads the device's properties. Runs the WPD work on a background thread - opening a session
    /// against a phone that is asleep or locked blocks for seconds.
    /// </summary>
    /// <remarks>
    /// A device that a file panel is already browsing is reused through the connection registry and
    /// is never disposed here: MediaDevices hands back an instance representing the same underlying
    /// device, and disposing it disconnects the session the panel is holding - the same trap the
    /// device poll in <see cref="MtpDeviceCatalog"/> fell into.
    /// </remarks>
    public static Task<MtpDeviceDetails?> LoadAsync(string deviceId, CancellationToken ct = default) =>
        Task.Run<MtpDeviceDetails?>(() =>
        {
            var inUse = MtpConnectionRegistry.Get(deviceId) is not null;

            var devices = MediaDevice.GetDevices().ToList();
            MediaDevice? device = null;
            foreach (var d in devices)
            {
                if (device is null && string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    device = d;
                    continue;
                }
                if (!string.IsNullOrEmpty(d.DeviceId) && MtpConnectionRegistry.Get(d.DeviceId) is not null) continue;
                d.Dispose();
            }

            if (device is null) return null;

            try
            {
                if (!device.IsConnected) device.Connect();
                ct.ThrowIfCancellationRequested();

                var storages = new List<MtpStorageInfo>();
                try
                {
                    foreach (var drive in device.GetDrives())
                    {
                        var name = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                            ? (drive.Name ?? "").TrimStart('\\')
                            : drive.VolumeLabel;
                        storages.Add(new MtpStorageInfo(name, (long)drive.TotalSize, (long)drive.TotalFreeSpace));
                    }
                }
                catch (Exception ex)
                {
                    // A device can refuse to enumerate its storages while something else is busy on
                    // it. The rest of the properties are still worth showing.
                    LogService.Warning($"MTP: could not read storages for {deviceId}: {ex.Message}");
                }

                return new MtpDeviceDetails(
                    deviceId,
                    FirstNonEmpty(device.FriendlyName, device.Description, device.Model, deviceId),
                    device.Manufacturer ?? "",
                    device.Model ?? "",
                    device.DeviceType.ToString(),
                    device.SerialNumber ?? "",
                    device.FirmwareVersion ?? "",
                    device.Protocol ?? "",
                    device.Transport.ToString(),
                    device.PowerSource.ToString(),
                    device.PowerLevel,
                    inUse,
                    storages);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogService.Warning($"MTP: could not read details for {deviceId}: {ex.Message}");
                return null;
            }
            finally
            {
                // Only when nobody is browsing it - see the remark above.
                if (!inUse) device.Dispose();
            }
        }, ct);

    /// <summary>The path a file panel navigates to in order to open this device.</summary>
    public string RootPath => RemotePath.Make("mtp", DeviceId);

    private static string FirstNonEmpty(params string?[] candidates) =>
        Array.Find(candidates, c => !string.IsNullOrWhiteSpace(c)) ?? "";
}

using CoderCommander.Services;
using CoderCommander.Utils;

namespace CoderCommander.WinForms;

/// <summary>
/// Configuration ▸ Connections ▸ Devices: the MTP devices currently attached, and what each one
/// reports about itself.
/// </summary>
/// <remarks>
/// The places bar already offers a button per device, but a button can only say the device's name
/// and open it. Everything else the device knows - model, serial, firmware, transport, how full
/// each of its storages is, whether it is running on battery - had nowhere to be shown.
/// </remarks>
public sealed partial class MtpDevicesForm : ThemedForm
{
    /// <summary>Raised when the user asks for a device to be opened. The argument is the
    /// <c>mtp://</c> root path; the caller decides which panel it lands in.</summary>
    public event EventHandler<string>? OpenDeviceRequested;

    private CancellationTokenSource? _detailsCts;

    public MtpDevicesForm()
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        var L = LocalizationService.Current;
        // A ColumnHeader is not a Control and cannot carry a LocalizationKey.
        _colDevice.Text = L.GetString("Mtp.Devices.Column");
        _colProperty.Text = L.GetString("Mtp.Devices.Property");
        _colValue.Text = L.GetString("Mtp.Devices.Value");

        // Set here rather than in the designer: ThemedForm.Resizable is this app's own property,
        // applied in OnLoad rather than a real FormBorderStyle the designer could round-trip.
        Resizable = true;

        _devices.SelectedIndexChanged += (_, _) => _ = LoadSelectedDetailsAsync();
        _devices.DoubleClick += (_, _) => OpenSelected();
        _openBtn.Click += (_, _) => OpenSelected();
        _refreshBtn.Click += (_, _) =>
        {
            MtpDeviceCatalog.Instance.Refresh();
            RefreshDeviceList();
        };
        _closeBtn.Click += (_, _) => Close();

        MtpDeviceCatalog.Instance.Changed += OnCatalogChanged;
        RefreshDeviceList();

        // And ask for a fresh look right away, off the UI thread - the catalog polls on its own
        // schedule and backs that schedule off when nothing changes, so a device plugged in a
        // moment ago may not be in the snapshot yet.
        //
        // The list is re-read here rather than left to the Changed event, which only fires when the
        // device set actually differs from the last poll: a catalog that already knew about the
        // device before this dialog subscribed raises nothing at all, and the dialog would sit
        // showing "no devices attached" next to a phone that is plainly plugged in.
        _ = Task.Run(() =>
        {
            MtpDeviceCatalog.Instance.Refresh();
            try
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)RefreshDeviceList);
            }
            catch (ObjectDisposedException)
            {
                // The dialog was closed while the poll was in flight.
            }
        });
    }

    /// <summary>
    /// Places the divider once, when the container first has a real width - the device list wants
    /// about a third, the properties the rest. In OnLayout rather than OnLoad, and clamped into the
    /// range SplitContainer will actually accept: assigning a distance it considers out of bounds
    /// throws rather than clamping, and before layout has run the container is still 150px wide.
    /// One-shot, so dragging the divider afterwards sticks.
    /// </summary>
    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_splitterPlaced || !_split.IsHandleCreated) return;

        const int deviceListMin = 160;
        const int detailsMin = 240;

        var highest = _split.Width - detailsMin - _split.SplitterWidth;
        if (highest <= deviceListMin) return;

        _split.Panel1MinSize = deviceListMin;
        _split.Panel2MinSize = detailsMin;
        _split.SplitterDistance = Math.Clamp(_split.Width / 3, deviceListMin, highest);
        _splitterPlaced = true;
    }

    private bool _splitterPlaced;

    /// <summary>The catalog raises this on a thread-pool thread.</summary>
    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke((Action)RefreshDeviceList); return; }
        RefreshDeviceList();
    }

    private void RefreshDeviceList()
    {
        if (IsDisposed) return;

        var previous = SelectedDeviceId();
        var devices = MtpDeviceCatalog.Instance.Current;

        _devices.BeginUpdate();
        _devices.Items.Clear();
        foreach (var device in devices)
            _devices.Items.Add(new ListViewItem(device.DisplayName) { Tag = device.DeviceId });
        _devices.EndUpdate();

        if (_devices.Items.Count == 0)
        {
            ShowMessageRow(LocalizationService.Current.GetString("Mtp.Devices.None"));
            UpdateButtonState();
            return;
        }

        // Keep the user on whatever they had selected across a refresh; otherwise select the first.
        var restored = _devices.Items.Cast<ListViewItem>()
            .FirstOrDefault(i => (string?)i.Tag == previous) ?? _devices.Items[0];
        restored.Selected = true;
        UpdateButtonState();
    }

    private string? SelectedDeviceId() =>
        _devices.SelectedItems.Count > 0 ? _devices.SelectedItems[0].Tag as string : null;

    private void UpdateButtonState() => _openBtn.Enabled = SelectedDeviceId() is not null;

    private async Task LoadSelectedDetailsAsync()
    {
        UpdateButtonState();

        var deviceId = SelectedDeviceId();
        if (deviceId is null) { _details.Items.Clear(); return; }

        // One in-flight read at a time: clicking down the device list must not leave several
        // background WPD sessions racing to fill the same grid.
        _detailsCts?.Cancel();
        _detailsCts?.Dispose();
        var cts = new CancellationTokenSource();
        _detailsCts = cts;

        ShowMessageRow(LocalizationService.Current.GetString("Mtp.Devices.Reading"));

        MtpDeviceDetails? details;
        try
        {
            details = await MtpDeviceDetails.LoadAsync(deviceId, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (IsDisposed || cts.IsCancellationRequested || SelectedDeviceId() != deviceId) return;

        if (details is null)
        {
            ShowMessageRow(LocalizationService.Current.GetString("Mtp.Devices.Unavailable"));
            return;
        }

        ShowDetails(details);
    }

    private void ShowDetails(MtpDeviceDetails d)
    {
        var L = LocalizationService.Current;

        _details.BeginUpdate();
        _details.Items.Clear();

        Add("Mtp.Prop.Name", d.Name);
        Add("Mtp.Prop.Manufacturer", d.Manufacturer);
        Add("Mtp.Prop.Model", d.Model);
        Add("Mtp.Prop.Type", d.DeviceType);
        Add("Mtp.Prop.Serial", d.SerialNumber);
        Add("Mtp.Prop.Firmware", d.FirmwareVersion);
        Add("Mtp.Prop.Protocol", d.Protocol);
        Add("Mtp.Prop.Transport", d.Transport);
        Add("Mtp.Prop.Power", d.PowerLevel > 0
            ? L.GetString("Mtp.Prop.PowerValue", d.PowerSource, d.PowerLevel)
            : d.PowerSource);
        Add("Mtp.Prop.InUse", L.GetString(d.InUse ? "Common.Yes" : "Common.No"));

        foreach (var storage in d.Storages)
        {
            var value = storage.TotalBytes > 0
                ? L.GetString("Mtp.Prop.StorageValue",
                    FormatUtils.FormatSize(storage.FreeBytes),
                    FormatUtils.FormatSize(storage.TotalBytes))
                : L.GetString("Mtp.Devices.Unavailable");
            _details.Items.Add(new ListViewItem(new[] { storage.Name, value }));
        }

        // Last, and deliberately: it is the least readable line here and the least often wanted.
        Add("Mtp.Prop.DeviceId", d.DeviceId);

        _details.EndUpdate();

        void Add(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            _details.Items.Add(new ListViewItem(new[] { L.GetString(key), value }));
        }
    }

    /// <summary>A single greyed row standing in for the property list - "reading", "no devices",
    /// "unavailable". A row rather than a label over the grid: a native ListView paints over any
    /// managed sibling laid on top of it whatever the z-order says. The text goes in the wider
    /// second column, since a sentence does not fit a column sized for the word "Firmware".</summary>
    private void ShowMessageRow(string text)
    {
        _details.BeginUpdate();
        _details.Items.Clear();
        _details.Items.Add(new ListViewItem(new[] { "", text })
        {
            ForeColor = ThemeService.Current.DimForeground,
        });
        _details.EndUpdate();
    }

    private void OpenSelected()
    {
        if (SelectedDeviceId() is not { } deviceId) return;
        OpenDeviceRequested?.Invoke(this, FileSystem.RemotePath.Make("mtp", deviceId));
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;

        MtpDeviceCatalog.Instance.Changed -= OnCatalogChanged;
        _detailsCts?.Cancel();
        _detailsCts?.Dispose();
        _detailsCts = null;
    }
}

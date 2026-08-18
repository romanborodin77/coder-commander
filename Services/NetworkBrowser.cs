using System.Runtime.InteropServices;

namespace CoderCommander.Services;

/// <summary>
/// Enumerates network resources (servers and shares) via the Windows Networking API
/// (<c>WNetOpenEnum</c>/<c>WNetEnumResource</c>/<c>WNetCloseEnum</c>), producing a tree of
/// servers and their disk shares — the same data source Windows Explorer's "Network" folder uses.
/// </summary>
public static class NetworkBrowser
{
    /// <summary>One network resource: a server or a share.</summary>
    public sealed class NetResource
    {
        /// <summary>Display name, e.g. <c>NAS1</c> or <c>Public</c>.</summary>
        public string Name { get; init; } = "";
        /// <summary>UNC path, e.g. <c>\\NAS1</c> or <c>\\NAS1\Public</c>.</summary>
        public string UncPath { get; init; } = "";
        /// <summary><c>true</c> for a server (has shares beneath it), <c>false</c> for a share.</summary>
        public bool IsServer { get; init; }
    }

    /// <summary>Scope: the network neighbourhood around this machine — the same scope Explorer uses.
    /// Falls back to the entire network if the context scope returns nothing (common on networks
    /// where the master browser protocol is disabled).</summary>
    public static IReadOnlyList<NetResource> EnumerateServers()
    {
        var servers = Enumerate(RESOURCE_CONTEXT, RESOURCETYPE_DISK);
        if (servers.Count == 0)
            servers = Enumerate(RESOURCE_GLOBALNET, RESOURCETYPE_DISK);
        return servers;
    }

    /// <summary>Enumerates disk shares on the specified server UNC path (e.g. <c>\\NAS1</c>).</summary>
    public static IReadOnlyList<NetResource> EnumerateShares(string serverUnc)
    {
        var handle = IntPtr.Zero;
        var result = WNetOpenEnum(RESOURCE_GLOBALNET, RESOURCETYPE_DISK, 0,
            new NETRESOURCE { lpRemoteName = serverUnc }, out handle);
        if (result != 0) return Array.Empty<NetResource>();

        try
        {
            return EnumerateChildren(handle);
        }
        finally
        {
            WNetCloseEnum(handle);
        }
    }

    // ── Internal ──

    private const int RESOURCE_CONTEXT = 0x00000005;
    private const int RESOURCE_GLOBALNET = 0x00000002;
    private const int RESOURCETYPE_DISK = 0x00000001;

    private static List<NetResource> Enumerate(int scope, int type)
    {
        var handle = IntPtr.Zero;
        var result = WNetOpenEnum(scope, type, 0, IntPtr.Zero, out handle);
        if (result != 0) return new List<NetResource>();

        try
        {
            return EnumerateChildren(handle);
        }
        finally
        {
            WNetCloseEnum(handle);
        }
    }

    private static List<NetResource> EnumerateChildren(IntPtr handle)
    {
        var results = new List<NetResource>();
        var bufSize = 16 * 1024;
        var buffer = IntPtr.Zero;

        try
        {
            buffer = Marshal.AllocHGlobal(bufSize);
            var count = 0xffffffffu;

            while (true)
            {
                var size = (uint)bufSize;
                var result = WNetEnumResource(handle, ref count, buffer, ref size);
                if (result == ErrorMoreData)
                {
                    Marshal.FreeHGlobal(buffer);
                    bufSize = (int)size;
                    buffer = Marshal.AllocHGlobal(bufSize);
                    continue;
                }
                if (result != 0) break; // ERROR_NO_MORE_ITEMS or other error

                // Read count NETRESOURCE structs from the buffer.
                var structSize = Marshal.SizeOf<NETRESOURCE>();
                for (var i = 0; i < count; i++)
                {
                    var ptr = buffer + i * structSize;
                    var nr = Marshal.PtrToStructure<NETRESOURCE>(ptr);
                    if (string.IsNullOrEmpty(nr.lpRemoteName)) continue;

                    // Top-level entries can be containers (servers) — their children are shares.
                    // A server's remote name looks like \\SERVER. A share's looks like \\SERVER\SHARE.
                    var name = nr.lpRemoteName.TrimStart('\\');
                    var isServer = nr.dwDisplayType == RESOURCEDISPLAYTYPE_SERVER;
                    results.Add(new NetResource
                    {
                        Name = name,
                        UncPath = nr.lpRemoteName,
                        IsServer = isServer
                    });
                }
                count = 0xffffffffu; // reset for next batch
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        }

        return results;
    }

    private const int ErrorMoreData = 234;
    private const int RESOURCEDISPLAYTYPE_SERVER = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string lpLocalName;
        public string lpRemoteName;
        public string lpComment;
        public string lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetOpenEnum(int dwScope, int dwType, int dwUsage,
        IntPtr lpNetResource, out IntPtr lphEnum);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetOpenEnum(int dwScope, int dwType, int dwUsage,
        NETRESOURCE lpNetResource, out IntPtr lphEnum);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetEnumResource(IntPtr hEnum, ref uint lpcCount,
        IntPtr lpBuffer, ref uint lpBufferSize);

    [DllImport("mpr.dll", SetLastError = true)]
    private static extern int WNetCloseEnum(IntPtr hEnum);
}

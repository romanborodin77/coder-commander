using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CoderCommander.Services;

/// <summary>
/// Passwords for saved connections, encrypted with Windows DPAPI and kept out of
/// <c>settings.json</c> entirely.
///
/// <para><b>Why a separate file.</b> <c>settings.json</c> is plain text that gets copied between
/// machines, backed up, and pasted into bug reports. Nothing secret may live there, so profiles
/// carry only an <see cref="Models.ConnectionProfile.Id"/> and the secret is looked up here.</para>
///
/// <para><b>What DPAPI buys.</b> <see cref="DataProtectionScope.CurrentUser"/> ties the ciphertext
/// to the logged-on Windows account: copied to another machine, or opened by another user on this
/// one, it simply won't decrypt. It does <i>not</i> protect against code already running as this
/// user - nothing on this side of the trust boundary can. The honest claim is "not readable off the
/// disk", not "unreadable".</para>
///
/// <para><b>Why fixed entropy.</b> The extra entropy passed to Protect/Unprotect must match on both
/// sides, so it cannot be secret from anyone holding this file. Its real effect is that a different
/// application running as the same user cannot decrypt this blob by simply calling Unprotect on it
/// - it would have to know this constant too. That is a modest, real benefit, and it is the only
/// one claimed here.</para>
///
/// <para>Windows-only by construction: <see cref="ProtectedData"/> throws
/// <see cref="PlatformNotSupportedException"/> elsewhere. The app is <c>net8.0-windows</c>, so this
/// is a statement of fact rather than a limitation to work around.</para>
/// </summary>
public sealed class CredentialStore
{
    /// <summary>Application-specific entropy. Not a secret (see the class remarks) - it exists so
    /// another process running as the same user cannot decrypt this file's entries by calling
    /// Unprotect on them directly.</summary>
    private static readonly byte[] Entropy = "CoderCommander.Connections.v1"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _lock = new();

    /// <summary>Process-wide store over the real AppData location.</summary>
    public static CredentialStore Instance { get; } = new(DefaultPath);

    private static string DefaultPath => Path.Combine(DataDirectory.Root, "credentials.dat");

    /// <summary>Internal so tests can point the store at a temp file instead of the real one -
    /// the same reason <c>SettingsService.Validate</c> is internal. A test that wrote to the
    /// operator's actual credential file would be unacceptable.</summary>
    internal CredentialStore(string path) => _path = path;

    /// <summary>Stores (or replaces) the password for <paramref name="profileId"/>. An empty or
    /// null password removes the entry rather than storing an empty secret, so "clear the saved
    /// password" needs no separate code path in the UI.</summary>
    public bool TrySet(Guid profileId, string? password)
    {
        if (string.IsNullOrEmpty(password))
            return Remove(profileId);

        try
        {
            lock (_lock)
            {
                var entries = Load();
                var plain = Encoding.UTF8.GetBytes(password);
                try
                {
                    var cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                    entries[profileId.ToString("N")] = Convert.ToBase64String(cipher);
                }
                finally
                {
                    // Wipe the plaintext copy this method made. The caller's string can't be
                    // wiped - .NET strings are immutable and SecureString is documented as not
                    // providing the protection people expect - so this is a partial measure, not
                    // a guarantee, and is not presented as one.
                    Array.Clear(plain);
                }
                Save(entries);
            }
            return true;
        }
        catch (Exception ex)
        {
            // Never log the password, the profile's URL, or the exception's data - only that it
            // failed and why in general terms.
            LogService.Error($"Failed to store credentials for {profileId:N}: {ex.GetType().Name}", ex);
            return false;
        }
    }

    /// <summary>Reads the password back, or <c>null</c> when there is none or it cannot be
    /// decrypted. A blob written by a different Windows account (a copied AppData folder) fails
    /// here with a <see cref="CryptographicException"/>; that is treated as "no saved password",
    /// which lands the user on the ordinary prompt instead of an error they cannot act on.</summary>
    public string? TryGet(Guid profileId)
    {
        try
        {
            lock (_lock)
            {
                var entries = Load();
                if (!entries.TryGetValue(profileId.ToString("N"), out var base64))
                    return null;

                var cipher = Convert.FromBase64String(base64);
                var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
                try
                {
                    return Encoding.UTF8.GetString(plain);
                }
                finally
                {
                    Array.Clear(plain);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Warning($"Cannot read credentials for {profileId:N}: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary><c>true</c> when a password is stored, without decrypting it - lets the UI show
    /// "password saved" without touching the secret.</summary>
    public bool Has(Guid profileId)
    {
        lock (_lock)
        {
            return Load().ContainsKey(profileId.ToString("N"));
        }
    }

    /// <summary>Deletes the entry. Must be called whenever a profile is deleted, or the store
    /// accumulates secrets that nothing can reach and nothing will ever clean up.</summary>
    public bool Remove(Guid profileId)
    {
        try
        {
            lock (_lock)
            {
                var entries = Load();
                if (!entries.Remove(profileId.ToString("N")))
                    return true;   // nothing stored is a successful outcome, not a failure
                Save(entries);
            }
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to remove credentials for {profileId:N}", ex);
            return false;
        }
    }

    /// <summary>
    /// Drops entries whose profile no longer exists.
    ///
    /// Deleting a profile removes its entry directly; this is the backstop for the cases that
    /// bypass that path - a hand-edited <c>settings.json</c>, a settings file restored from an
    /// older backup, a crash between the two writes. Without it the file only ever grows, and
    /// every orphan is a live secret for a connection the user believes they removed.
    /// </summary>
    public void RemoveOrphans(IEnumerable<Guid> liveProfileIds)
    {
        try
        {
            var live = liveProfileIds.Select(id => id.ToString("N")).ToHashSet(StringComparer.Ordinal);
            lock (_lock)
            {
                var entries = Load();
                var orphans = entries.Keys.Where(k => !live.Contains(k)).ToList();
                if (orphans.Count == 0) return;

                foreach (var key in orphans)
                    entries.Remove(key);
                Save(entries);
                LogService.Info($"Credential store: removed {orphans.Count} orphaned entry(ies)");
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to prune orphaned credentials", ex);
        }
    }

    // ── Persistence ─────────────────────────────────────────────────────────────────────────

    /// <summary>Entries as <c>{ profileId: base64(ciphertext) }</c>. Per-entry encryption rather
    /// than one blob over the whole file: a single unreadable entry then costs one password, not
    /// all of them.</summary>
    private Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new Dictionary<string, string>(StringComparer.Ordinal);

            var json = File.ReadAllText(_path, Encoding.UTF8);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            // A corrupt store must not take the app down or block the connections UI; the user
            // re-enters the passwords, which is recoverable, unlike a crash at startup.
            LogService.Warning($"Credential store unreadable, treating as empty: {ex.GetType().Name}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void Save(Dictionary<string, string> entries)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Temp file then atomic replace, matching SettingsService.Save: a crash mid-write must not
        // leave a truncated store, which would read back as "no saved passwords at all".
        var tmp = _path + ".tmp";
        // Clean up any stale .tmp from a previous crash — DPAPI-encrypted, but still clutter.
        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
        File.WriteAllText(tmp, JsonSerializer.Serialize(entries, JsonOpts), Encoding.UTF8);
        File.Move(tmp, _path, overwrite: true);
    }
}

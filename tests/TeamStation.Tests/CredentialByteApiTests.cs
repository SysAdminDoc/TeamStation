using System.Security.Cryptography;
using System.Text;
using TeamStation.Core.Models;
using TeamStation.Data.Security;
using TeamStation.Data.Storage;
using TeamStation.Launcher;

namespace TeamStation.Tests;

/// <summary>
/// v0.3.4: byte[] credential-read API on the launch hot path. Adds
/// EncryptBytes / DecryptToBytes on CryptoService, LoadEntryPasswordBytes /
/// LoadEntryProxyPasswordBytes on EntryRepository, and a byte[]-aware
/// CliArgvBuilder.Build overload + TeamViewerLauncher overload that zeros
/// the input buffers immediately after argv has been composed. The
/// existing string-based APIs remain (they're now compatibility shims that
/// route through the byte[] path internally).
/// </summary>
public class CredentialByteApiTests : IDisposable
{
    private readonly string _dbPath;
    private readonly Database _db;
    private readonly CryptoService _crypto;
    private readonly EntryRepository _entries;

    public CredentialByteApiTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ts-byte-{Guid.NewGuid():N}.db");
        _db = new Database(_dbPath);
        _crypto = CryptoService.CreateOrLoad(_db);
        _entries = new EntryRepository(_db, _crypto);
    }

    public void Dispose()
    {
        _crypto.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            try { if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void EncryptBytes_DecryptToBytes_round_trip()
    {
        var plain = Encoding.UTF8.GetBytes("byte-roundtrip-secret");
        var ct = _crypto.EncryptBytes(plain);
        Assert.NotNull(ct);

        var roundtripped = _crypto.DecryptToBytes(ct);
        Assert.NotNull(roundtripped);
        Assert.Equal(plain, roundtripped);
    }

    [Fact]
    public void EncryptBytes_and_EncryptString_produce_cross_decryptable_wraps()
    {
        // Both APIs target the same wire format. A wrap produced by EncryptBytes
        // must decrypt as a string, and a wrap produced by EncryptString must
        // decrypt as bytes that round-trip through UTF-8.
        var asBytes = _crypto.EncryptBytes(Encoding.UTF8.GetBytes("cross-format"));
        Assert.NotNull(asBytes);
        Assert.Equal("cross-format", _crypto.DecryptString(asBytes));

        var asString = _crypto.EncryptString("cross-format");
        Assert.NotNull(asString);
        var decryptedBytes = _crypto.DecryptToBytes(asString);
        Assert.NotNull(decryptedBytes);
        Assert.Equal("cross-format", Encoding.UTF8.GetString(decryptedBytes!));
    }

    [Fact]
    public void DecryptToBytes_returns_a_fresh_buffer_caller_can_zero_safely()
    {
        var ct = _crypto.EncryptString("zero-me-please");
        Assert.NotNull(ct);

        var first = _crypto.DecryptToBytes(ct)!;
        var second = _crypto.DecryptToBytes(ct)!;

        // Same content but distinct allocations — the caller can zero one
        // without affecting the other or the wrap itself.
        Assert.Equal(first, second);
        Assert.NotSame(first, second);

        CryptographicOperations.ZeroMemory(first);
        Assert.All(first, b => Assert.Equal(0, b));
        // Second is untouched.
        Assert.Equal(Encoding.UTF8.GetBytes("zero-me-please"), second);
        // Wrap is reusable.
        Assert.Equal("zero-me-please", _crypto.DecryptString(ct));
    }

    [Fact]
    public void EncryptBytes_and_DecryptToBytes_handle_null()
    {
        Assert.Null(_crypto.EncryptBytes(null));
        Assert.Null(_crypto.DecryptToBytes(null));
    }

    [Fact]
    public void LoadEntryPasswordBytes_round_trips_against_Upsert()
    {
        var entry = new ConnectionEntry
        {
            Name = "byte-entry",
            TeamViewerId = "999000111",
            Password = "stored-via-string-loaded-as-bytes",
        };
        _entries.Upsert(entry);

        var bytes = _entries.LoadEntryPasswordBytes(entry.Id);
        Assert.NotNull(bytes);
        Assert.Equal(Encoding.UTF8.GetBytes("stored-via-string-loaded-as-bytes"), bytes);
    }

    [Fact]
    public void LoadEntryPasswordBytes_returns_null_when_password_is_absent()
    {
        var entry = new ConnectionEntry
        {
            Name = "no-pw",
            TeamViewerId = "888777666",
            Password = null,
        };
        _entries.Upsert(entry);

        Assert.Null(_entries.LoadEntryPasswordBytes(entry.Id));
        Assert.Null(_entries.LoadEntryPasswordBytes(Guid.NewGuid())); // unknown id
    }

    [Fact]
    public void LoadEntryProxyPasswordBytes_round_trips_against_Upsert()
    {
        var entry = new ConnectionEntry
        {
            Name = "proxy-entry",
            TeamViewerId = "100200300",
            Password = "main",
            Proxy = new ProxySettings(Host: "10.0.0.1", Port: 8080, Username: "u", Password: "proxy-secret"),
        };
        _entries.Upsert(entry);

        var bytes = _entries.LoadEntryProxyPasswordBytes(entry.Id);
        Assert.NotNull(bytes);
        Assert.Equal(Encoding.UTF8.GetBytes("proxy-secret"), bytes);
    }

    [Fact]
    public void CliArgvBuilder_with_byte_overrides_emits_PasswordB64_from_bytes_and_ignores_entry_Password()
    {
        var entry = new ConnectionEntry
        {
            Name = "argv",
            TeamViewerId = "123456789",
            Password = "WRONG-do-not-use", // must be ignored
        };
        var bytes = Encoding.UTF8.GetBytes("byte-override-secret");
        var argv = CliArgvBuilder.Build(entry, passwordBytes: bytes, proxyPasswordBytes: null, base64Password: true);

        Assert.Contains("--PasswordB64", argv);
        var idx = argv.ToList().IndexOf("--PasswordB64");
        Assert.True(idx >= 0 && idx + 1 < argv.Count);
        Assert.Equal(Convert.ToBase64String(bytes), argv[idx + 1]);
        // The string from entry.Password must not have leaked through.
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("WRONG-do-not-use")), argv);
    }

    [Fact]
    public void CliArgvBuilder_with_byte_overrides_validates_password()
    {
        // Passwords containing forbidden characters / leading dash must throw
        // through the byte path the same way they do through the string path.
        var entry = new ConnectionEntry
        {
            Name = "argv-bad",
            TeamViewerId = "123456789",
        };
        var leadingDash = Encoding.UTF8.GetBytes("-bad");
        Assert.ThrowsAny<Exception>(() =>
            CliArgvBuilder.Build(entry, passwordBytes: leadingDash, proxyPasswordBytes: null));
    }

    [Fact]
    public void CliArgvBuilder_with_byte_overrides_emits_proxy_password_from_bytes()
    {
        var entry = new ConnectionEntry
        {
            Name = "proxy-argv",
            TeamViewerId = "123456789",
            Proxy = new ProxySettings(Host: "10.0.0.1", Port: 8080, Username: "u", Password: "WRONG-PROXY"),
        };
        var pwBytes = Encoding.UTF8.GetBytes("good-pw");
        var proxyBytes = Encoding.UTF8.GetBytes("good-proxy");
        var argv = CliArgvBuilder.Build(entry, passwordBytes: pwBytes, proxyPasswordBytes: proxyBytes);

        var idx = argv.ToList().IndexOf("--ProxyPassword");
        Assert.True(idx >= 0);
        Assert.Equal(Convert.ToBase64String(proxyBytes), argv[idx + 1]);
        // The string from proxy.Password must not have leaked through.
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("WRONG-PROXY")), argv);
    }
}

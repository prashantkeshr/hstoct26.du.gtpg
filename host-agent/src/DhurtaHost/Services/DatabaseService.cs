using System.Security.Cryptography;
using DhurtaHost.Models;
using Microsoft.Data.Sqlite;

namespace DhurtaHost.Services;

public sealed class DatabaseService : IDisposable
{
    private readonly SqliteConnection _conn;

    public DatabaseService(IConfiguration cfg)
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DhurtaHost");
        Directory.CreateDirectory(dataDir);

        var dbPath = Path.Combine(dataDir, "dhurta.db");
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();

        // WAL mode for concurrency
        Exec("PRAGMA journal_mode=WAL");
        Exec("PRAGMA foreign_keys=ON");
    }

    public void Initialize()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS devices (
              id TEXT PRIMARY KEY,
              name TEXT NOT NULL,
              os_version TEXT NOT NULL,
              agent_version TEXT NOT NULL,
              created_at TEXT NOT NULL,
              last_seen_at TEXT NOT NULL,
              unattended_access INTEGER NOT NULL DEFAULT 0
            )
            """);

        Exec("""
            CREATE TABLE IF NOT EXISTS users (
              id TEXT PRIMARY KEY,
              username TEXT NOT NULL UNIQUE,
              password_hash TEXT NOT NULL,
              created_at TEXT NOT NULL,
              is_owner INTEGER NOT NULL DEFAULT 0
            )
            """);

        Exec("""
            CREATE TABLE IF NOT EXISTS sessions (
              id TEXT PRIMARY KEY,
              user_id TEXT NOT NULL,
              device_id TEXT NOT NULL,
              refresh_token_hash TEXT NOT NULL,
              created_at TEXT NOT NULL,
              expires_at TEXT NOT NULL,
              is_revoked INTEGER NOT NULL DEFAULT 0,
              client_ip TEXT NOT NULL,
              FOREIGN KEY(user_id) REFERENCES users(id),
              FOREIGN KEY(device_id) REFERENCES devices(id)
            )
            """);

        Exec("""
            CREATE TABLE IF NOT EXISTS access_keys (
              id TEXT PRIMARY KEY,
              device_id TEXT NOT NULL,
              key_hash TEXT NOT NULL,
              label TEXT NOT NULL,
              created_at TEXT NOT NULL,
              expires_at TEXT,
              last_used_at TEXT,
              is_revoked INTEGER NOT NULL DEFAULT 0,
              FOREIGN KEY(device_id) REFERENCES devices(id)
            )
            """);

        Exec("""
            CREATE TABLE IF NOT EXISTS config (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            )
            """);

        // Generate JWT signing secret on first run
        if (GetConfig("jwt_secret") is null)
        {
            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            SetConfig("jwt_secret", secret);
        }
    }

    // ── Devices ──────────────────────────────────────────────────────────

    public Device? GetDevice(string id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM devices WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadDevice(r) : null;
    }

    public Device? GetFirstDevice()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM devices ORDER BY created_at LIMIT 1";
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadDevice(r) : null;
    }

    public void UpsertDevice(Device d)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO devices(id,name,os_version,agent_version,created_at,last_seen_at,unattended_access)
            VALUES(@id,@name,@os,@ver,@ca,@ls,@ua)
            ON CONFLICT(id) DO UPDATE SET
              name=excluded.name, os_version=excluded.os_version,
              agent_version=excluded.agent_version, last_seen_at=excluded.last_seen_at,
              unattended_access=excluded.unattended_access
            """;
        cmd.Parameters.AddWithValue("@id", d.Id);
        cmd.Parameters.AddWithValue("@name", d.Name);
        cmd.Parameters.AddWithValue("@os", d.OsVersion);
        cmd.Parameters.AddWithValue("@ver", d.AgentVersion);
        cmd.Parameters.AddWithValue("@ca", d.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@ls", d.LastSeenAt.ToString("O"));
        cmd.Parameters.AddWithValue("@ua", d.UnattendedAccessEnabled ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    // ── Users ─────────────────────────────────────────────────────────────

    public User? GetUserByUsername(string username)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM users WHERE username = @u";
        cmd.Parameters.AddWithValue("@u", username);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadUser(r) : null;
    }

    public User? GetUser(string id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM users WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadUser(r) : null;
    }

    public void InsertUser(User u)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO users(id,username,password_hash,created_at,is_owner)
            VALUES(@id,@u,@ph,@ca,@io)
            """;
        cmd.Parameters.AddWithValue("@id", u.Id);
        cmd.Parameters.AddWithValue("@u", u.Username);
        cmd.Parameters.AddWithValue("@ph", u.PasswordHash);
        cmd.Parameters.AddWithValue("@ca", u.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@io", u.IsOwner ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public bool HasOwner()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users WHERE is_owner = 1";
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    // ── Sessions ──────────────────────────────────────────────────────────

    public void InsertSession(Session s)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sessions(id,user_id,device_id,refresh_token_hash,created_at,expires_at,is_revoked,client_ip)
            VALUES(@id,@uid,@did,@rth,@ca,@ea,0,@ip)
            """;
        cmd.Parameters.AddWithValue("@id", s.Id);
        cmd.Parameters.AddWithValue("@uid", s.UserId);
        cmd.Parameters.AddWithValue("@did", s.DeviceId);
        cmd.Parameters.AddWithValue("@rth", s.RefreshTokenHash);
        cmd.Parameters.AddWithValue("@ca", s.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@ea", s.ExpiresAt.ToString("O"));
        cmd.Parameters.AddWithValue("@ip", s.ClientIp);
        cmd.ExecuteNonQuery();
    }

    public Session? GetSessionById(string id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM sessions WHERE id = @id AND is_revoked = 0";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadSession(r) : null;
    }

    public void RevokeSession(string id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET is_revoked = 1 WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void RevokeAllUserSessions(string userId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET is_revoked = 1 WHERE user_id = @uid";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.ExecuteNonQuery();
    }

    // ── Access Keys ───────────────────────────────────────────────────────

    public void InsertAccessKey(AccessKey k)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO access_keys(id,device_id,key_hash,label,created_at,expires_at,is_revoked)
            VALUES(@id,@did,@kh,@lbl,@ca,@ea,0)
            """;
        cmd.Parameters.AddWithValue("@id", k.Id);
        cmd.Parameters.AddWithValue("@did", k.DeviceId);
        cmd.Parameters.AddWithValue("@kh", k.KeyHash);
        cmd.Parameters.AddWithValue("@lbl", k.Label);
        cmd.Parameters.AddWithValue("@ca", k.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@ea", k.ExpiresAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<AccessKey> GetActiveAccessKeys(string deviceId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM access_keys WHERE device_id = @did AND is_revoked = 0";
        cmd.Parameters.AddWithValue("@did", deviceId);
        using var r = cmd.ExecuteReader();
        var keys = new List<AccessKey>();
        while (r.Read()) keys.Add(ReadAccessKey(r));
        return keys;
    }

    public void RevokeAccessKey(string id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE access_keys SET is_revoked = 1 WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void TouchAccessKey(string id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE access_keys SET last_used_at = @now WHERE id = @id";
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    // ── Config ────────────────────────────────────────────────────────────

    public string? GetConfig(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM config WHERE key = @k";
        cmd.Parameters.AddWithValue("@k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetConfig(string key, string value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO config(key,value) VALUES(@k,@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    // ── Readers ───────────────────────────────────────────────────────────

    private static Device ReadDevice(SqliteDataReader r) => new()
    {
        Id = r.GetString(0), Name = r.GetString(1), OsVersion = r.GetString(2),
        AgentVersion = r.GetString(3), CreatedAt = DateTime.Parse(r.GetString(4)),
        LastSeenAt = DateTime.Parse(r.GetString(5)), UnattendedAccessEnabled = r.GetInt32(6) != 0
    };

    private static User ReadUser(SqliteDataReader r) => new()
    {
        Id = r.GetString(0), Username = r.GetString(1), PasswordHash = r.GetString(2),
        CreatedAt = DateTime.Parse(r.GetString(3)), IsOwner = r.GetInt32(4) != 0
    };

    private static Session ReadSession(SqliteDataReader r) => new()
    {
        Id = r.GetString(0), UserId = r.GetString(1), DeviceId = r.GetString(2),
        RefreshTokenHash = r.GetString(3), CreatedAt = DateTime.Parse(r.GetString(4)),
        ExpiresAt = DateTime.Parse(r.GetString(5)), IsRevoked = r.GetInt32(6) != 0,
        ClientIp = r.GetString(7)
    };

    private static AccessKey ReadAccessKey(SqliteDataReader r) => new()
    {
        Id = r.GetString(0), DeviceId = r.GetString(1), KeyHash = r.GetString(2),
        Label = r.GetString(3), CreatedAt = DateTime.Parse(r.GetString(4)),
        ExpiresAt = r.IsDBNull(5) ? null : DateTime.Parse(r.GetString(5)),
        LastUsedAt = r.IsDBNull(6) ? null : DateTime.Parse(r.GetString(6)),
        IsRevoked = r.GetInt32(7) != 0
    };

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _conn.Dispose();
}

using System.Security.Cryptography;
using DhurtaHost.Models;

namespace DhurtaHost.Services;

public sealed class DeviceService
{
    private readonly DatabaseService _db;
    private Device? _self;

    public DeviceService(DatabaseService db)
    {
        _db = db;
    }

    public void Initialize()
    {
        _self = _db.GetFirstDevice();
        if (_self is null)
        {
            _self = new Device
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = Environment.MachineName,
                OsVersion = GetOsVersion(),
                AgentVersion = GetAgentVersion(),
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                UnattendedAccessEnabled = false,
            };
            _db.UpsertDevice(_self);
        }
        else
        {
            _self.LastSeenAt = DateTime.UtcNow;
            _self.AgentVersion = GetAgentVersion();
            _db.UpsertDevice(_self);
        }
    }

    public Device Self => _self ?? throw new InvalidOperationException("DeviceService not initialized");

    public void UpdateLastSeen()
    {
        if (_self is null) return;
        _self.LastSeenAt = DateTime.UtcNow;
        _db.UpsertDevice(_self);
    }

    public void SetUnattendedAccess(bool enabled)
    {
        if (_self is null) return;
        _self.UnattendedAccessEnabled = enabled;
        _db.UpsertDevice(_self);
    }

    private static string GetOsVersion()
    {
        try { return System.Runtime.InteropServices.RuntimeInformation.OSDescription; }
        catch { return "Windows"; }
    }

    private static string GetAgentVersion() =>
        typeof(DeviceService).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
}

using System.Collections.Concurrent;

namespace DhurtaHost.Services;

public sealed class RateLimiterService : IDisposable
{
    private sealed record BucketKey(string Ip, string Action);

    private sealed class Bucket
    {
        public int Failures;
        public DateTime LockedUntil = DateTime.MinValue;
        public DateTime WindowStart = DateTime.UtcNow;
        public int RequestCount;
    }

    private readonly ConcurrentDictionary<BucketKey, Bucket> _buckets = new();
    private readonly Timer _cleanupTimer;

    public RateLimiterService()
    {
        _cleanupTimer = new Timer(_ => Cleanup(), null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    // Returns true if the request is allowed.
    public bool Allow(string ip, string action)
    {
        var key = new BucketKey(ip, action);
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket());

        lock (bucket)
        {
            if (DateTime.UtcNow < bucket.LockedUntil)
                return false;

            // Slide the window every minute for general rate limiting
            if ((DateTime.UtcNow - bucket.WindowStart).TotalMinutes >= 1)
            {
                bucket.WindowStart = DateTime.UtcNow;
                bucket.RequestCount = 0;
            }

            bucket.RequestCount++;

            // Hard cap: 60 requests/minute per IP per action (adjustable)
            return bucket.RequestCount <= 60;
        }
    }

    // Call after a failed auth attempt; returns false (deny) after 5 failures
    public bool RecordAuthFailure(string ip)
    {
        var key = new BucketKey(ip, "auth");
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket());
        lock (bucket)
        {
            if (DateTime.UtcNow < bucket.LockedUntil)
                return false;

            bucket.Failures++;
            if (bucket.Failures >= 5)
            {
                bucket.LockedUntil = DateTime.UtcNow.AddMinutes(15);
                bucket.Failures = 0;
            }
            return true;
        }
    }

    public bool IsAuthLocked(string ip)
    {
        var key = new BucketKey(ip, "auth");
        if (!_buckets.TryGetValue(key, out var bucket)) return false;
        lock (bucket) return DateTime.UtcNow < bucket.LockedUntil;
    }

    private void Cleanup()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        foreach (var kv in _buckets)
        {
            lock (kv.Value)
            {
                if (kv.Value.WindowStart < cutoff && kv.Value.LockedUntil < DateTime.UtcNow)
                    _buckets.TryRemove(kv.Key, out _);
            }
        }
    }

    public void Dispose() => _cleanupTimer.Dispose();
}

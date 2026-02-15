using System.Collections.Concurrent;

namespace cochca.Services;

public class SessionRegistry
{
    private readonly ConcurrentDictionary<string, int> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public bool IsActive(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var count) && count > 0;
    }

    public void Register(string sessionId)
    {
        _sessions.AddOrUpdate(sessionId, 1, (_, count) => count + 1);
    }

    public void Unregister(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var count))
        {
            if (count <= 1)
            {
                _sessions.TryRemove(sessionId, out _);
            }
            else
            {
                _sessions[sessionId] = count - 1;
            }
        }
    }
}

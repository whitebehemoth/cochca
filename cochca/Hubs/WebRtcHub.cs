using cochca.Services;
using Microsoft.AspNetCore.SignalR;

namespace cochca.Hubs;

public class WebRtcHub : Hub
{
    private readonly SessionRegistry _sessions;

    public WebRtcHub(SessionRegistry sessions)
    {
        _sessions = sessions;
    }

    public async Task JoinSession(string sessionId)
    {
        _sessions.Register(sessionId);
        Context.Items["sessionId"] = sessionId;
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        await Clients.GroupExcept(sessionId, Context.ConnectionId).SendAsync("PeerJoined");
    }

    public Task SendOffer(string sessionId, string offerJson)
    {
        return Clients.GroupExcept(sessionId, Context.ConnectionId).SendAsync("ReceiveOffer", offerJson);
    }

    public Task SendAnswer(string sessionId, string answerJson)
    {
        return Clients.GroupExcept(sessionId, Context.ConnectionId).SendAsync("ReceiveAnswer", answerJson);
    }

    public Task SendIceCandidate(string sessionId, string candidateJson)
    {
        return Clients.GroupExcept(sessionId, Context.ConnectionId).SendAsync("ReceiveIceCandidate", candidateJson);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("sessionId", out var session) && session is string sessionId)
        {
            _sessions.Unregister(sessionId);
        }

        return base.OnDisconnectedAsync(exception);
    }
}

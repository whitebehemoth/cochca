using cochca.Services;
using Microsoft.AspNetCore.SignalR;

namespace cochca.Hubs;

public class ChatHub : Hub
{
    private readonly SessionRegistry _sessions;

    public ChatHub(SessionRegistry sessions)
    {
        _sessions = sessions;
    }

    public async Task JoinSession(string sessionId)
    {
        _sessions.Register(sessionId);
        Context.Items["sessionId"] = sessionId;
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
    }

    public Task SendMessage(string sessionId, string senderId, string senderName, string text)
    {
        return Clients.Group(sessionId).SendAsync("ReceiveMessage", senderId, senderName, text);
    }

    public Task SendFile(string sessionId, string senderId, string senderName, string fileName, string contentType, string base64)
    {
        return Clients.Group(sessionId).SendAsync("ReceiveFile", senderId, senderName, fileName, contentType, base64);
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

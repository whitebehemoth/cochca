using cochca.Components;
using cochca.Hubs;
using cochca.Services;
using Microsoft.AspNetCore.DataProtection;

namespace cochca;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();

        // Add Data Protection with ephemeral key (for single replica or sticky sessions)
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo("/tmp/keys"));

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<SessionRegistry>();
        builder.Services.AddSingleton<LocalizationService>();
        builder.Services.AddSingleton<TurnCredentialsService>();

        var app = builder.Build();

        app.MapDefaultEndpoints();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapHub<WebRtcHub>("/hubs/webrtc");
        app.MapHub<ChatHub>("/hubs/chat");
        app.MapGet("/api/sessions/{sessionId}/active", (string sessionId, SessionRegistry sessions)
            => Results.Ok(sessions.IsActive(sessionId)));
        
        // PROTECTED: TURN credentials endpoint - requires active session
        app.MapGet("/api/turn-credentials", (
            HttpContext context,
            SessionRegistry sessions,
            TurnCredentialsService turnService,
            ILogger<Program> logger) =>
        {
            // Require sessionId in query or header
            var sessionId = context.Request.Query["sessionId"].FirstOrDefault()
                ?? context.Request.Headers["X-Session-Id"].FirstOrDefault();

            if (string.IsNullOrEmpty(sessionId))
            {
                logger.LogWarning("[TURN] Request without sessionId from {IP}", context.Connection.RemoteIpAddress);
                return Results.BadRequest(new { error = "sessionId is required" });
            }

            // Verify session is active (user must be connected via SignalR)
            if (!sessions.IsActive(sessionId))
            {
                logger.LogWarning("[TURN] Invalid session {SessionId} from {IP}", sessionId, context.Connection.RemoteIpAddress);
                return Results.Unauthorized();
            }

            // Log successful request for monitoring
            logger.LogInformation("[TURN] Credentials issued for session {SessionId} from {IP}", sessionId, context.Connection.RemoteIpAddress);

            return Results.Ok(turnService.GenerateCredentials());
        });
        
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}

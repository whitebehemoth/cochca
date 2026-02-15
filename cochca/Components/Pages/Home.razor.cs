using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using cochca.Models;

namespace cochca.Components.Pages;

public partial class Home : IAsyncDisposable, IDisposable
{
    private const long MaxFileSize = 4 * 1024 * 1024;
    
    private readonly List<ChatMessage> Messages = new();
    private string? StatusKey;
    private bool StatusIsError;
    private string? MessageText;
    private string DisplayName = string.Empty;
    private string ClientId = Guid.NewGuid().ToString("N");
    private DotNetObjectReference<Home>? DotNetRef;
    private bool ChatStarted;
    private bool CallStarted;
    private bool IsConnected;
    private bool IsVideoEnabled = true;
    private bool ManuallyDisabledCamera = false; // Track manual camera toggle
    private bool ShowLinkCopied;
    private bool PendingChatStart;
    private bool PendingCallStart;
    private bool PendingVideoToggle;
    private bool PendingVideoEnabled;

    private string? _sessionId;

    [Parameter]
    public string? SessionId
    {
        get => _sessionId;
        set
        {
            if (_sessionId == value)
            {
                return;
            }

            _sessionId = value;
        }
    }

    private string BaseUrl => Navigation.ToAbsoluteUri("/").ToString().TrimEnd('/');

    private Tab SelectedTab = Tab.Call;

    protected override void OnInitialized()
    {
        DisplayName = L["DefaultName"];
        L.OnChange += HandleLanguageChanged;

        if (string.IsNullOrWhiteSpace(SessionId))
        {
            GenerateSession();
        }
    }

    protected override void OnParametersSet()
    {
        if (string.IsNullOrWhiteSpace(SessionId))
        {
            GenerateSession();
        }
    }

    private void HandleLanguageChanged()
    {
        DisplayName = L["DefaultName"];
        InvokeAsync(StateHasChanged);
    }

    private void ToggleLanguage()
    {
        L.Toggle();
        StateHasChanged();
    }

    private void GenerateSession()
    {
        SessionId = Guid.NewGuid().ToString("N")[..10];
        StatusKey = null;
        StatusIsError = false;
    }

    private async Task CopyLinkAsync()
    {
        if (string.IsNullOrWhiteSpace(SessionId))
        {
            return;
        }

        var fullLink = $"{BaseUrl}/{SessionId}";
        
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", fullLink);
            
            ShowLinkCopied = true;
            StateHasChanged();
            
            // Hide message after 2 seconds
            await Task.Delay(2000);
            ShowLinkCopied = false;
            StateHasChanged();
        }
        catch (InvalidOperationException)
        {
            // JS not available yet (prerendering)
        }
    }

    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(SessionId))
        {
            GenerateSession();
        }

        StatusKey = null;
        StatusIsError = false;
        IsConnected = true;
        IsVideoEnabled = true;
        await JS.InvokeVoidAsync("app.setPath", $"/{SessionId}");
        PendingChatStart = true;
        PendingCallStart = SelectedTab == Tab.Call;
        await InvokeAsync(StateHasChanged);
    }

    private async Task DisconnectAsync()
    {
        IsConnected = false;
        CallStarted = false;
        ManuallyDisabledCamera = false; // Reset manual flag

        await JS.InvokeVoidAsync("webrtc.hangup");
        await JS.InvokeVoidAsync("chat.stop");
        ChatStarted = false;
        await JS.InvokeVoidAsync("app.setPath", "/");
    }

    private async Task SelectTab(Tab tab)
    {
        SelectedTab = tab;

        if (IsConnected)
        {
            if (tab == Tab.Call)
            {
                // Switch to Call: enable camera only if not manually disabled
                if (!ManuallyDisabledCamera && !IsVideoEnabled)
                {
                    IsVideoEnabled = true;
                    PendingVideoToggle = true;
                    PendingVideoEnabled = true;
                }
                
                PendingCallStart = true;
            }
            else
            {
                // Switch to Chat: disable camera temporarily (but remember manual state)
                if (IsVideoEnabled)
                {
                    IsVideoEnabled = false;
                    PendingVideoToggle = true;
                    PendingVideoEnabled = false;
                }
                
                PendingChatStart = true;
            }

            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task StartChatAsync()
    {
        if (ChatStarted || string.IsNullOrWhiteSpace(SessionId))
        {
            return;
        }

        DotNetRef ??= DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("chat.start", SessionId, ClientId, DotNetRef);
        ChatStarted = true;
    }

    private async Task StartCallAsync()
    {
        if (CallStarted || string.IsNullOrWhiteSpace(SessionId))
        {
            return;
        }

        await JS.InvokeVoidAsync("webrtc.start", SessionId);
        CallStarted = true;
        IsVideoEnabled = true;
    }

    private async Task SendMessageAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        if (!ChatStarted)
        {
            await StartChatAsync();
        }

        if (string.IsNullOrWhiteSpace(MessageText))
        {
            StatusKey = "StatusEmptyMessage";
            StatusIsError = true;
            return;
        }

        await JS.InvokeVoidAsync("chat.sendMessage", SessionId, ClientId, DisplayName, MessageText);
        MessageText = string.Empty;
        StatusKey = null;
        StatusIsError = false;
    }

    private async Task OnFileSelected(InputFileChangeEventArgs args)
    {
        if (!IsConnected)
        {
            return;
        }

        if (!ChatStarted)
        {
            await StartChatAsync();
        }

        var file = args.File;
        if (file.Size > MaxFileSize)
        {
            StatusKey = "StatusFileTooLarge";
            StatusIsError = true;
            return;
        }

        using var stream = file.OpenReadStream(MaxFileSize);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        var base64 = Convert.ToBase64String(memory.ToArray());

        await JS.InvokeVoidAsync("chat.sendFile", SessionId, ClientId, DisplayName, file.Name, file.ContentType, base64);
    }

    private async Task ToggleVideoAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        // Manual toggle - update the flag
        ManuallyDisabledCamera = IsVideoEnabled; // If currently enabled, will be manually disabled
        IsVideoEnabled = !IsVideoEnabled;

        if (!CallStarted)
        {
            PendingCallStart = true;
            PendingVideoToggle = true;
            PendingVideoEnabled = IsVideoEnabled;
            await InvokeAsync(StateHasChanged);
            return;
        }

        PendingVideoToggle = true;
        PendingVideoEnabled = IsVideoEnabled;
        await InvokeAsync(StateHasChanged);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!IsConnected)
        {
            return;
        }

        if (PendingChatStart)
        {
            PendingChatStart = false;
            await StartChatAsync();
        }

        if (PendingCallStart)
        {
            PendingCallStart = false;
            await StartCallAsync();
        }

        if (PendingVideoToggle)
        {
            PendingVideoToggle = false;
            await JS.InvokeVoidAsync("webrtc.toggleVideo", PendingVideoEnabled);
        }
    }

    [JSInvokable]
    public Task ReceiveMessage(ChatMessageDto dto)
    {
        Messages.Add(new ChatMessage
        {
            SenderName = dto.SenderName,
            Text = dto.Text,
            FileName = dto.FileName,
            ContentType = dto.ContentType,
            Base64 = dto.Base64,
            IsLocal = dto.IsLocal
        });

        return InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        L.OnChange -= HandleLanguageChanged;

        if (DotNetRef is not null)
        {
            DotNetRef.Dispose();
        }

        // Only call JS if not prerendering
        try
        {
            await JS.InvokeVoidAsync("chat.stop");
            await JS.InvokeVoidAsync("webrtc.hangup");
        }
        catch (InvalidOperationException)
        {
            // Ignore - component is being disposed during prerendering
        }
    }

    public void Dispose()
    {
        L.OnChange -= HandleLanguageChanged;
    }

    private enum Tab
    {
        Chat,
        Call
    }

    public class ChatMessageDto
    {
        public string SenderName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string Base64 { get; set; } = string.Empty;
        public bool IsLocal { get; set; }
    }
}

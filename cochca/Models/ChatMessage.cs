namespace cochca.Models;

public class ChatMessage
{
    public required string SenderName { get; init; }
    public string? Text { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
    public string? Base64 { get; init; }
    public bool IsLocal { get; init; }

    public bool HasFile => !string.IsNullOrWhiteSpace(FileName) && !string.IsNullOrWhiteSpace(Base64);
    public bool IsImage => HasFile && !string.IsNullOrWhiteSpace(ContentType) && ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

public class ChatMessageDto
{
    public required string SenderId { get; init; }
    public required string SenderName { get; init; }
    public string? Text { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
    public string? Base64 { get; init; }
    public bool IsLocal { get; init; }
}

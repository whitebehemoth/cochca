using System.Collections.Concurrent;

namespace cochca.Services;

public class LocalizationService
{
    private readonly ConcurrentDictionary<string, (string Ru, string En)> _strings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AppTitle"] = ("Кочка. Простая браузерная звонилка. Сделана Chat GPT 5.2 с подачи Хабра", "Kochka. Simple browser calling app. Made by ChatGPT 5.2 inspired by Habr"),
        ["HomeTitle"] = ("Локальный звонок", "Local Call"),
        ["HomeIntro"] = ("Создай ссылку и отправь её второму устройству в той же сети.", "Create a link and send it to the other device on the same network."),
        ["CreateSession"] = ("Создать сессию", "Create session"),
        ["PasswordLabel"] = ("Начни с новым паролем или подключись с паролем открытой сессии", "Start with a new password or join an existing session"),
        ["ShareLabel"] = ("Ссылка для подключения", "Share URL"),
        ["ShareHint"] = ("Отправь пароль или этот URL второму адресату.", "Send the password or this URL to the other person."),
        ["Refresh"] = ("Обновить", "Refresh"),
        ["CopyLink"] = ("Скопировать ссылку", "Copy Link"),
        ["LinkCopied"] = ("Ссылка скопирована!", "Link copied!"),
        ["Connect"] = ("Подключиться", "Connect"),
        ["Disconnect"] = ("Отключиться", "Disconnect"),
        ["ToggleCamera"] = ("Камера", "Camera"),
        ["Call"] = ("Звонок", "Call"),
        ["Chat"] = ("Чат", "Chat"),
        ["SessionLink"] = ("Ссылка для подключения", "Join link"),
        ["OpenHere"] = ("Открыть у меня", "Open here"),
        ["JoinExisting"] = ("Подключиться к существующей сессии", "Join existing session"),
        ["EnterCode"] = ("Введите код", "Enter code"),
        ["Join"] = ("Подключиться", "Join"),
        ["CallTitle"] = ("Сессия", "Session"),
        ["SessionId"] = ("Идентификатор сессии", "Session ID"),
        ["NewId"] = ("Новый ID", "New ID"),
        ["Start"] = ("Начать", "Start"),
        ["End"] = ("Завершить", "End"),
        ["VideoToggle"] = ("Видео", "Video"),
        ["Name"] = ("Имя", "Name"),
        ["DefaultName"] = ("Вы", "You"),
        ["Message"] = ("Сообщение", "Message"),
        ["Send"] = ("Отправить", "Send"),
        ["AttachFile"] = ("Файл", "File"),
        ["ChatTitle"] = ("Чат", "Chat"),
        ["StatusEnterId"] = ("Введите ID сессии.", "Enter a session ID."),
        ["StatusIdInUse"] = ("Сессия с таким ID уже открыта. Измените ID.", "This session ID is already in use. Change it."),
        ["StatusCreated"] = ("Сессия создана. Жмите старт на обоих устройствах.", "Session created. Press start on both devices."),
        ["StatusNotFound"] = ("Сессия не найдена. Проверьте ID.", "Session not found. Check the ID."),
        ["StatusConnecting"] = ("Подключение к сессии...", "Connecting to session..."),
        ["StatusFileTooLarge"] = ("Файл слишком большой.", "File is too large."),
        ["StatusEmptyMessage"] = ("Введите сообщение.", "Enter a message."),
        ["ChatHint"] = ("Отправляйте сообщения и файлы.", "Send messages and files.")
    };

    public event Action? OnChange;

    public string CurrentLanguage { get; private set; } = "ru";

    public string ToggleLabel => CurrentLanguage == "ru" ? "EN" : "RU";

    public string this[string key] => _strings.TryGetValue(key, out var value)
        ? (CurrentLanguage == "ru" ? value.Ru : value.En)
        : key;

    public void Toggle()
    {
        CurrentLanguage = CurrentLanguage == "ru" ? "en" : "ru";
        OnChange?.Invoke();
    }
}

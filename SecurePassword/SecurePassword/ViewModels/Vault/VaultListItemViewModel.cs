using SecurePassword.ViewModels.Base;

namespace SecurePassword.ViewModels.Vault;

public enum VaultItemType
{
    Password,
    Card,
    Note
}

/// <summary>
/// Lightweight, sanitized UI presentation model for vault list items.
/// Security principle: holds ONLY the minimum data needed for list display.
/// Plaintext passwords, full card numbers, CVVs and entire note bodies are NEVER kept in this model.
/// </summary>
public sealed class VaultListItemViewModel : BaseViewModel
{
    public int Id { get; init; }
    public VaultItemType Type { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string IconSource { get; init; } = string.Empty;
    public string SearchText { get; init; } = string.Empty;
    public string TypeDisplayName { get; init; } = string.Empty;
    public string TypeBadgeColor { get; init; } = "#19A38C";
    public string TypeGlyph { get; init; } = "🔑";
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    public string AccessibleDescription => $"{Title}, {TypeDisplayName}";

    public static VaultListItemViewModel FromPassword(PasswordEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string title = entry.Title;
        string login = entry.Login;
        string serviceName = entry.ServiceName;

        string subtitle = string.IsNullOrWhiteSpace(login)
            ? (string.IsNullOrWhiteSpace(serviceName) ? "Пароль" : serviceName)
            : login;

        string searchText = string.Join(" ", title, login, serviceName).ToLowerInvariant();
        string iconSource = ServiceImageGenerator.GetServiceIconSource(serviceName, title);

        return new VaultListItemViewModel
        {
            Id = entry.Id,
            Type = VaultItemType.Password,
            Title = title,
            Subtitle = subtitle,
            IconSource = iconSource,
            SearchText = searchText,
            TypeDisplayName = "Пароль",
            TypeBadgeColor = "#19A38C",
            TypeGlyph = "🔑",
            UpdatedAt = entry.UpdatedAt
        };
    }

    public static VaultListItemViewModel FromCard(CardEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string title = entry.Title;
        string maskedNumber = MaskCardNumber(entry.CardNumber);
        string holder = entry.CardHolder;
        string bank = entry.BankName;

        string searchText = string.Join(" ", title, holder, bank, maskedNumber).ToLowerInvariant();


        return new VaultListItemViewModel
        {
            Id = entry.Id,
            Type = VaultItemType.Card,
            Title = title,
            Subtitle = maskedNumber,
            IconSource = "icon_card.svg",
            SearchText = searchText,
            TypeDisplayName = "Карта",
            TypeBadgeColor = "#FB8C00",
            TypeGlyph = "💳",
            UpdatedAt = entry.UpdatedAt
        };
    }

    public static VaultListItemViewModel FromNote(NoteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string title = entry.Title;
        string preview = GetNotePreview(entry.Content);
        string searchText = string.Join(" ", title, preview).ToLowerInvariant();

        return new VaultListItemViewModel
        {
            Id = entry.Id,
            Type = VaultItemType.Note,
            Title = title,
            Subtitle = preview,
            IconSource = "icon_note.svg",
            SearchText = searchText,
            TypeDisplayName = "Заметка",
            TypeBadgeColor = "#60707A",
            TypeGlyph = "📝",
            UpdatedAt = entry.UpdatedAt
        };
    }

    public static string MaskCardNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Номер карты не указан";

        string digits = new(value.Where(char.IsDigit).ToArray());
        if (digits.Length < 4)
            return "••••";

        return $"•••• •••• •••• {digits[^4..]}";
    }

    public static string GetNotePreview(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "Пустая заметка";

        var text = content.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length > 50 ? $"{text[..50]}..." : text;
    }
}

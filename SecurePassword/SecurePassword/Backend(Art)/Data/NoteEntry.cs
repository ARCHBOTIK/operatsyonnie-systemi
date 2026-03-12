using System.Text;

namespace SecurePassword;

public class NoteEntry : IHasID
{
    public int Id { get; set; }
    public byte[] TitleBytes { get; set; }
    public byte[] ContentBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string Title
    {
        get => TitleBytes != null ? Encoding.UTF8.GetString(TitleBytes) : string.Empty;
        set => TitleBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
    }

    public string Content
    {
        get => ContentBytes != null ? Encoding.UTF8.GetString(ContentBytes) : string.Empty;
        set => ContentBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
    }

    public NoteEntry()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
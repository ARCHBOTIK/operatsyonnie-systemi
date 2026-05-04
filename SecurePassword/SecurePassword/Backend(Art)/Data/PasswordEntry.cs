using System.Text;

namespace SecurePassword;

public class PasswordEntry : IHasID
{
    public int Id { get; set; }
    public byte[] TitleBytes { get; set; }
    public byte[] LoginBytes { get; set; }
    public byte[] PasswordBytes { get; set; }
    public byte[] ServiceNameBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string Title
    {
        get => TitleBytes != null ? Encoding.UTF8.GetString(TitleBytes) : string.Empty;
        set => TitleBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
    }

    public string Login
    {
        get => LoginBytes != null ? Encoding.UTF8.GetString(LoginBytes) : string.Empty;
        set => LoginBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
    }

    public string Password
    {
        get => PasswordBytes != null ? Encoding.UTF8.GetString(PasswordBytes) : string.Empty;
        set => PasswordBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
    }

    public string ServiceName
    {
        get => ServiceNameBytes != null ? Encoding.UTF8.GetString(ServiceNameBytes) : string.Empty;
        set => ServiceNameBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
    }

    public PasswordEntry()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
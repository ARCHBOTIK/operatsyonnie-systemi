namespace SecurePassword;

public interface IClipboardBackend
{
    Task SetTextAsync(string text, bool isSensitive);
    Task<string?> GetTextAsync();
    Task ClearAsync();
}

public interface ISecureClipboardService : IDisposable
{
    Task CopyToClipboardAsync(string text, bool isSensitive = true);
    Task ClearClipboardAsync();
    bool HasActiveSecret { get; }
}

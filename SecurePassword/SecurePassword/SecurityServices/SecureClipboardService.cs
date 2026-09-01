using System.Security.Cryptography;
using System.Text;

#if ANDROID
using Android.Content;
using Android.OS;
#endif

namespace SecurePassword;

public class MauiClipboardBackend : IClipboardBackend
{
    public async Task SetTextAsync(string text, bool isSensitive)
    {
        try
        {
            await Clipboard.Default.SetTextAsync(text);
        }
        catch
        {
        }
    }

    public async Task<string?> GetTextAsync()
    {
        try
        {
            if (Clipboard.Default.HasText)
            {
                return await Clipboard.Default.GetTextAsync();
            }
        }
        catch
        {
        }
        return null;
    }

    public async Task ClearAsync()
    {
        try
        {
            await Clipboard.Default.SetTextAsync(string.Empty);
        }
        catch
        {
        }
    }
}

#if ANDROID
public class AndroidClipboardBackend : IClipboardBackend
{
    public Task SetTextAsync(string text, bool isSensitive)
    {
        var context = Android.App.Application.Context;
        var clipboard = (ClipboardManager?)context.GetSystemService(Context.ClipboardService);
        if (clipboard == null)
            return Task.CompletedTask;

        var clipData = ClipData.NewPlainText(isSensitive ? "password" : "text", text);
        if (isSensitive && clipData != null)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                var extras = clipData.Description?.Extras ?? new PersistableBundle();
                extras.PutBoolean("android.content.extra.IS_SENSITIVE", true);
                if (clipData.Description != null)
                {
                    clipData.Description.Extras = extras;
                }
            }
        }

        clipboard.PrimaryClip = clipData;
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync()
    {
        var context = Android.App.Application.Context;
        var clipboard = (ClipboardManager?)context.GetSystemService(Context.ClipboardService);
        if (clipboard == null || !clipboard.HasPrimaryClip)
            return Task.FromResult<string?>(null);

        var clip = clipboard.PrimaryClip;
        if (clip == null || clip.ItemCount == 0)
            return Task.FromResult<string?>(null);

        var item = clip.GetItemAt(0);
        string? text = item?.CoerceToText(context)?.ToString();
        return Task.FromResult(text);
    }

    public Task ClearAsync()
    {
        var context = Android.App.Application.Context;
        var clipboard = (ClipboardManager?)context.GetSystemService(Context.ClipboardService);
        if (clipboard == null)
            return Task.CompletedTask;

        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            clipboard.ClearPrimaryClip();
        }
        else
        {
            clipboard.PrimaryClip = ClipData.NewPlainText(string.Empty, string.Empty);
        }

        return Task.CompletedTask;
    }
}
#endif

public class SecureClipboardService : ISecureClipboardService
{
    private readonly IClipboardBackend _backend;
    private readonly TimeSpan _timeout;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayFunc;
    private readonly Lock _lock = new();

    private CancellationTokenSource? _cts;
    private long _currentOperationId;
    private byte[]? _activeSecretHash;
    private bool _disposed;

    public bool HasActiveSecret
    {
        get
        {
            lock (_lock)
            {
                return _activeSecretHash != null;
            }
        }
    }

    public SecureClipboardService(
        IClipboardBackend backend,
        TimeSpan? timeout = null,
        Func<TimeSpan, CancellationToken, Task>? delayFunc = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _delayFunc = delayFunc ?? Task.Delay;
    }

    public async Task CopyToClipboardAsync(string text, bool isSensitive = true)
    {
        if (_disposed)
            return;

        ArgumentNullException.ThrowIfNull(text);

        long opId;
        CancellationToken token;
        byte[] secretHash = ComputeHash(text);

        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();

            _currentOperationId++;
            opId = _currentOperationId;

            if (_activeSecretHash != null)
            {
                CryptographicOperations.ZeroMemory(_activeSecretHash);
            }

            _activeSecretHash = isSensitive ? (byte[])secretHash.Clone() : null;

            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }

        await _backend.SetTextAsync(text, isSensitive);

        if (isSensitive)
        {
            _ = ScheduleAutoClearAsync(opId, secretHash, token);
        }
        else
        {
            CryptographicOperations.ZeroMemory(secretHash);
        }
    }

    private async Task ScheduleAutoClearAsync(long operationId, byte[] expectedHash, CancellationToken token)
    {
        try
        {
            await _delayFunc(_timeout, token);

            if (token.IsCancellationRequested)
                return;

            string? currentClipboardText = await _backend.GetTextAsync();
            if (currentClipboardText == null)
            {
                ClearActiveHash(operationId);
                return;
            }

            byte[] currentHash = ComputeHash(currentClipboardText);
            bool isMatch = CryptographicOperations.FixedTimeEquals(expectedHash, currentHash);
            CryptographicOperations.ZeroMemory(currentHash);

            if (isMatch)
            {
                lock (_lock)
                {
                    if (_currentOperationId == operationId)
                    {
                        ClearActiveHash(operationId);
                    }
                    else
                    {
                        return;
                    }
                }

                await _backend.ClearAsync();
            }
            else
            {
                ClearActiveHash(operationId);
            }
        }
        catch (System.OperationCanceledException)
        {
            // Expected on cancellation by a newer copy or dispose
        }
        catch
        {
            // Fail-safe against clipboard exceptions
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedHash);
        }
    }

    private void ClearActiveHash(long operationId)
    {
        lock (_lock)
        {
            if (_currentOperationId == operationId && _activeSecretHash != null)
            {
                CryptographicOperations.ZeroMemory(_activeSecretHash);
                _activeSecretHash = null;
            }
        }
    }

    public async Task ClearClipboardAsync()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _currentOperationId++;

            if (_activeSecretHash != null)
            {
                CryptographicOperations.ZeroMemory(_activeSecretHash);
                _activeSecretHash = null;
            }
        }

        await _backend.ClearAsync();
    }

    private static byte[] ComputeHash(string text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        try
        {
            return SHA256.HashData(utf8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            if (_activeSecretHash != null)
            {
                CryptographicOperations.ZeroMemory(_activeSecretHash);
                _activeSecretHash = null;
            }
        }
    }
}

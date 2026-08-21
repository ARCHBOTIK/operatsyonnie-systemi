using SecurePassword;
using Xunit;

namespace SecurePassword.Tests;

public class MockClipboardBackend : IClipboardBackend
{
    public string? Content { get; set; }
    public bool LastIsSensitive { get; set; }

    public Task SetTextAsync(string text, bool isSensitive)
    {
        Content = text;
        LastIsSensitive = isSensitive;
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync()
    {
        return Task.FromResult(Content);
    }

    public Task ClearAsync()
    {
        Content = null;
        return Task.CompletedTask;
    }
}

public class SecureClipboardTests
{
    [Fact]
    public async Task Test01_Clipboard_Copy_StartsExpirationAndClearsAfterTimeout()
    {
        var mockBackend = new MockClipboardBackend();
        using var service = new SecureClipboardService(mockBackend, TimeSpan.FromMilliseconds(50));

        await service.CopyToClipboardAsync("SuperSecretPassword123!", isSensitive: true);

        Assert.Equal("SuperSecretPassword123!", mockBackend.Content);
        Assert.True(mockBackend.LastIsSensitive);
        Assert.True(service.HasActiveSecret);

        // Wait for auto-clear timer to trigger
        await Task.Delay(150);

        Assert.Null(mockBackend.Content);
        Assert.False(service.HasActiveSecret);
    }

    [Fact]
    public async Task Test02_Clipboard_NonSensitiveCopy_DoesNotAutoClear()
    {
        var mockBackend = new MockClipboardBackend();
        using var service = new SecureClipboardService(mockBackend, TimeSpan.FromMilliseconds(50));

        await service.CopyToClipboardAsync("user@example.com", isSensitive: false);

        Assert.Equal("user@example.com", mockBackend.Content);
        Assert.False(mockBackend.LastIsSensitive);
        Assert.False(service.HasActiveSecret);

        // Wait past timer
        await Task.Delay(150);

        // Non-sensitive data should NOT be cleared
        Assert.Equal("user@example.com", mockBackend.Content);
    }

    [Fact]
    public async Task Test03_Clipboard_ModifiedByThirdParty_DoesNotClearNewContent()
    {
        var mockBackend = new MockClipboardBackend();
        using var service = new SecureClipboardService(mockBackend, TimeSpan.FromMilliseconds(80));

        await service.CopyToClipboardAsync("PasswordA", isSensitive: true);
        Assert.Equal("PasswordA", mockBackend.Content);

        // User copies something else from an external app
        mockBackend.Content = "ExternalNotesFromBrowser";

        // Wait for service's timer to expire
        await Task.Delay(160);

        // Must NOT overwrite or clear external user content
        Assert.Equal("ExternalNotesFromBrowser", mockBackend.Content);
    }

    [Fact]
    public async Task Test04_Clipboard_SecondCopy_CancelsAndSupercedesFirstTimer()
    {
        var mockBackend = new MockClipboardBackend();
        using var service = new SecureClipboardService(mockBackend, TimeSpan.FromMilliseconds(100));

        // Copy first password
        await service.CopyToClipboardAsync("Password_One", isSensitive: true);
        Assert.Equal("Password_One", mockBackend.Content);

        // Wait 40ms, then copy second password
        await Task.Delay(40);
        await service.CopyToClipboardAsync("Password_Two", isSensitive: true);
        Assert.Equal("Password_Two", mockBackend.Content);

        // Wait another 70ms (total 110ms from first copy, 70ms from second copy)
        // First timer would have fired around 100ms, but must NOT clear Password_Two!
        await Task.Delay(70);
        Assert.Equal("Password_Two", mockBackend.Content);

        // Wait another 70ms (total 140ms from second copy)
        // Second timer should now have fired and cleared Password_Two
        await Task.Delay(70);
        Assert.Null(mockBackend.Content);
    }

    [Fact]
    public async Task Test05_Clipboard_ClearClipboardAsync_ImmediatelyClears()
    {
        var mockBackend = new MockClipboardBackend();
        using var service = new SecureClipboardService(mockBackend, TimeSpan.FromSeconds(30));

        await service.CopyToClipboardAsync("TemporarySecret", isSensitive: true);
        Assert.Equal("TemporarySecret", mockBackend.Content);
        Assert.True(service.HasActiveSecret);

        await service.ClearClipboardAsync();

        Assert.Null(mockBackend.Content);
        Assert.False(service.HasActiveSecret);
    }

    [Fact]
    public async Task Test06_Clipboard_NullOrEmptyContent_DoesNotThrow()
    {
        var mockBackend = new MockClipboardBackend();
        using var service = new SecureClipboardService(mockBackend, TimeSpan.FromMilliseconds(50));

        await service.CopyToClipboardAsync("Secret", isSensitive: true);

        // Simulate clipboard cleared by system before timer
        mockBackend.Content = null;

        await Task.Delay(100);

        Assert.Null(mockBackend.Content);
        Assert.False(service.HasActiveSecret);
    }

    [Fact]
    public async Task Test07_Clipboard_RapidConcurrentCopies_ThreadSafeAndConsistent()
    {
        var mockBackend = new MockClipboardBackend();
        using var service = new SecureClipboardService(mockBackend, TimeSpan.FromMilliseconds(150));

        const int count = 20;
        var tasks = new List<Task>();

        for (int i = 0; i < count; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                await service.CopyToClipboardAsync($"Secret_{index}", isSensitive: true);
            }));
        }

        await Task.WhenAll(tasks);

        Assert.NotNull(mockBackend.Content);
        Assert.StartsWith("Secret_", mockBackend.Content);

        // Wait for final auto-clear
        await Task.Delay(250);
        Assert.Null(mockBackend.Content);
    }

    [Fact]
    public async Task Test08_Clipboard_Dispose_CancelsPendingTimersAndCleansMemory()
    {
        var mockBackend = new MockClipboardBackend();
        var service = new SecureClipboardService(mockBackend, TimeSpan.FromMilliseconds(100));

        await service.CopyToClipboardAsync("SecretToDispose", isSensitive: true);
        Assert.True(service.HasActiveSecret);

        service.Dispose();

        Assert.False(service.HasActiveSecret);

        // Wait past timer duration to confirm no exceptions from dead timer
        await Task.Delay(150);
    }
}

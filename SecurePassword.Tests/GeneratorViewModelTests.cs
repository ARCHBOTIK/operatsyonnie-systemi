using SecurePassword.ViewModels.Generator;
using Xunit;

namespace SecurePassword.Tests;

public class GeneratorViewModelTests
{
    private class MockSecureClipboardService : ISecureClipboardService
    {
        public string? LastCopiedText { get; private set; }
        public bool? LastIsSensitive { get; private set; }
        public int CopyCallCount { get; private set; }

        public bool HasActiveSecret => LastCopiedText != null;

        public Task CopyToClipboardAsync(string text, bool isSensitive = true)
        {
            LastCopiedText = text;
            LastIsSensitive = isSensitive;
            CopyCallCount++;
            return Task.CompletedTask;
        }

        public Task ClearClipboardAsync()
        {
            LastCopiedText = null;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Defaults_ShouldBeValidAndConsistent()
    {
        var clipboard = new MockSecureClipboardService();
        using var vm = new GeneratorViewModel(clipboard);

        Assert.Equal(12, vm.PasswordLength);
        Assert.Equal(12.0, vm.PasswordLengthDouble);
        Assert.True(vm.IncludeDigits);
        Assert.True(vm.IncludeLowercase);
        Assert.True(vm.IncludeUppercase);
        Assert.False(vm.IncludeSpecial);
        Assert.Equal(GeneratorViewModel.InitialMessage, vm.GeneratedPassword);
        Assert.False(vm.HasEvaluatedPassword);
        Assert.Equal(0, vm.EntropyBits);
        Assert.Equal(0, vm.StrengthPercentage);
        Assert.Equal(string.Empty, vm.StrengthText);
        Assert.False(vm.Copied);
        Assert.Equal(3, vm.ActiveOptionsCount);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(128)]
    public void Generate_ShouldCreatePasswordOfRequestedLength(int length)
    {
        var clipboard = new MockSecureClipboardService();
        using var vm = new GeneratorViewModel(clipboard)
        {
            PasswordLength = length
        };

        vm.GenerateCommand.Execute(null);

        Assert.True(vm.HasEvaluatedPassword);
        Assert.Equal(length, vm.GeneratedPassword.Length);
        Assert.True(vm.EntropyBits > 0);
        Assert.True(vm.StrengthPercentage > 0);
        Assert.False(string.IsNullOrEmpty(vm.StrengthText));
    }

    [Fact]
    public void Generate_DisabledCharacterSets_ShouldBeRespected()
    {
        var clipboard = new MockSecureClipboardService();
        using var vm = new GeneratorViewModel(clipboard)
        {
            PasswordLength = 30,
            IncludeDigits = false,
            IncludeUppercase = false,
            IncludeSpecial = false,
            IncludeLowercase = true
        };

        vm.GenerateCommand.Execute(null);

        Assert.True(vm.HasEvaluatedPassword);
        Assert.All(vm.GeneratedPassword, ch => Assert.True(char.IsLower(ch)));
    }

    [Fact]
    public void Generate_AllDisabled_ShouldShowValidationMessage()
    {
        var clipboard = new MockSecureClipboardService();
        using var vm = new GeneratorViewModel(clipboard)
        {
            IncludeDigits = false,
            IncludeUppercase = false,
            IncludeSpecial = false,
            IncludeLowercase = false
        };

        vm.GenerateCommand.Execute(null);

        Assert.False(vm.HasEvaluatedPassword);
        Assert.Equal(GeneratorViewModel.ValidationMessage, vm.GeneratedPassword);
        Assert.Equal(0, vm.EntropyBits);
        Assert.Equal(0, vm.StrengthPercentage);
        Assert.Equal(0, vm.ActiveOptionsCount);
    }

    [Fact]
    public void Generate_ShouldUpdateEntropyAndStrength()
    {
        var clipboard = new MockSecureClipboardService();
        using var vm = new GeneratorViewModel(clipboard)
        {
            PasswordLength = 24,
            IncludeDigits = true,
            IncludeLowercase = true,
            IncludeUppercase = true,
            IncludeSpecial = true
        };

        vm.GenerateCommand.Execute(null);

        Assert.True(vm.EntropyBits >= 50);
        Assert.True(vm.StrengthPercentage >= 50);
        Assert.True(vm.StrengthProgress >= 0.5);
        Assert.Contains(vm.StrengthText, new[] { "Хороший", "Отличный" });
        Assert.Contains(vm.StrengthColorHex, new[] { "#43A047", "#19A38C" });
        Assert.Contains("Энтропия:", vm.EntropyText);
    }

    [Fact]
    public void OptionChanged_ShouldResetPasswordPreview()
    {
        var clipboard = new MockSecureClipboardService();
        using var vm = new GeneratorViewModel(clipboard);

        vm.GenerateCommand.Execute(null);
        Assert.True(vm.HasEvaluatedPassword);

        vm.IncludeSpecial = true;
        Assert.Equal(GeneratorViewModel.InitialMessage, vm.GeneratedPassword);
        Assert.False(vm.HasEvaluatedPassword);
    }

    [Fact]
    public async Task Copy_WhenEvaluated_ShouldCallSecureClipboardWithSensitiveFlag()
    {
        var clipboard = new MockSecureClipboardService();
        using var vm = new GeneratorViewModel(clipboard);

        vm.GenerateCommand.Execute(null);
        string generated = vm.GeneratedPassword;

        vm.CopyCommand.Execute(null);

        // Allow task completion
        await Task.Delay(50);

        Assert.Equal(1, clipboard.CopyCallCount);
        Assert.Equal(generated, clipboard.LastCopiedText);
        Assert.True(clipboard.LastIsSensitive);
        Assert.True(vm.Copied);
    }

    [Fact]
    public async Task Copy_WhenNotEvaluated_ShouldNotCallClipboard()
    {
        var clipboard = new MockSecureClipboardService();
        using var vm = new GeneratorViewModel(clipboard);

        vm.CopyCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(0, clipboard.CopyCallCount);
        Assert.False(vm.Copied);
    }

    [Fact]
    public void LockSession_ShouldClearSensitiveData()
    {
        var clipboard = new MockSecureClipboardService();
        var session = new VaultSessionService();
        session.MarkAuthenticated();

        using var vm = new GeneratorViewModel(clipboard, session);

        vm.GenerateCommand.Execute(null);
        Assert.True(vm.HasEvaluatedPassword);

        // Lock session
        session.Lock();

        Assert.Equal(GeneratorViewModel.InitialMessage, vm.GeneratedPassword);
        Assert.False(vm.HasEvaluatedPassword);
        Assert.False(vm.Copied);
    }

    [Fact]
    public void Dispose_ShouldUnsubscribeFromSession()
    {
        var clipboard = new MockSecureClipboardService();
        var session = new VaultSessionService();
        session.MarkAuthenticated();

        var vm = new GeneratorViewModel(clipboard, session);
        vm.GenerateCommand.Execute(null);
        string generated = vm.GeneratedPassword;

        vm.Dispose();

        // Lock session after dispose should not affect vm state or throw
        session.Lock();
        Assert.Equal(generated, vm.GeneratedPassword);
    }

    [Fact]
    public void Concurrent_GenerateCalls_ShouldNotThrow()
    {
        var clipboard = new MockSecureClipboardService();
        using var vm = new GeneratorViewModel(clipboard);

        Parallel.For(0, 50, _ =>
        {
            vm.GeneratePassword();
        });

        Assert.True(vm.HasEvaluatedPassword);
        Assert.Equal(12, vm.GeneratedPassword.Length);
    }

    [Theory]
    [InlineData(-10, 4)]
    [InlineData(0, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(64, 64)]
    [InlineData(65, 65)]
    [InlineData(255, 255)]
    [InlineData(300, 255)]
    [InlineData(1000, 255)]
    public void PasswordLength_ShouldClampBetween4And255(int input, int expected)
    {
        var clipboard = new MockSecureClipboardService();
        using var vm = new GeneratorViewModel(clipboard)
        {
            PasswordLength = input
        };

        Assert.Equal(expected, vm.PasswordLength);
        Assert.Equal((double)expected, vm.PasswordLengthDouble);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(255)]
    public void Generate_BoundaryLengths_ShouldProduceExactStringLength(int length)
    {
        var clipboard = new MockSecureClipboardService();
        using var vm = new GeneratorViewModel(clipboard)
        {
            PasswordLength = length
        };

        vm.GenerateCommand.Execute(null);

        Assert.True(vm.HasEvaluatedPassword);
        Assert.Equal(length, vm.GeneratedPassword.Length);
    }

    [Fact]
    public void MultipleInstances_Dispose_ShouldCleanlyUnsubscribeWithoutInterference()
    {
        var clipboard = new MockSecureClipboardService();
        var session = new VaultSessionService();
        session.MarkAuthenticated();

        var vms = Enumerable.Range(0, 10)
            .Select(_ => new GeneratorViewModel(clipboard, session))
            .ToList();

        foreach (var vm in vms)
        {
            vm.GenerateCommand.Execute(null);
            Assert.True(vm.HasEvaluatedPassword);
        }

        // Dispose first 5
        for (int i = 0; i < 5; i++)
        {
            vms[i].Dispose();
        }

        // Lock session
        session.Lock();

        // Disposed VMs retain old password (unsubscribed)
        for (int i = 0; i < 5; i++)
        {
            Assert.True(vms[i].HasEvaluatedPassword);
        }

        // Active VMs are cleared
        for (int i = 5; i < 10; i++)
        {
            Assert.False(vms[i].HasEvaluatedPassword);
            Assert.Equal(GeneratorViewModel.InitialMessage, vms[i].GeneratedPassword);
            vms[i].Dispose();
        }
    }
}

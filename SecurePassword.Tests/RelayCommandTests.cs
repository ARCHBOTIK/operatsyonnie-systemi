using SecurePassword.ViewModels.Base;
using Xunit;

namespace SecurePassword.Tests;

public class RelayCommandTests
{
    [Fact]
    public async Task AsyncCommand_ContainsDelegateExceptionAndReportsIt()
    {
        Exception? reported = null;
        var command = new AsyncRelayCommand(
            () => Task.FromException(new InvalidOperationException("test failure")),
            onException: exception => reported = exception);

        await command.ExecuteAsync(null);

        Assert.IsType<InvalidOperationException>(reported);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task AsyncCommand_ContainsExceptionThrownByErrorReporter()
    {
        var command = new AsyncRelayCommand(
            () => Task.FromException(new InvalidOperationException("test failure")),
            onException: _ => throw new ApplicationException("reporting failure"));

        Exception? escaped = await Record.ExceptionAsync(() => command.ExecuteAsync(null));

        Assert.Null(escaped);
        Assert.True(command.CanExecute(null));
    }
}

using System.IO;
using Merkautomat.Services;
using Xunit;

namespace Merkautomat.Tests;

public class DeferredWriterTests
{
    [Fact]
    public async Task Later_WritesABurstOfChangesOnceWithTheNewestState()
    {
        var state = 0;
        var writes = new List<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new DeferredWriter(() => state.ToString(), text => { lock (writes) writes.Add(text); done.TrySetResult(); },
            TimeSpan.FromMilliseconds(200), ex => done.TrySetException(ex));

        for (var i = 1; i <= 20; i++) { state = i; writer.Later(); }
        Assert.True(writer.Pending);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // The write callback finishes just before the writer marks the change as written.
        Assert.True(SpinWait.SpinUntil(() => !writer.Pending, TimeSpan.FromSeconds(5)));
        Assert.Equal(["20"], writes);
    }

    [Fact]
    public void Flush_WritesOnlyWhenSomethingChanged()
    {
        var writes = 0;
        var writer = new DeferredWriter(() => "x", _ => writes++, TimeSpan.FromHours(1), _ => { });

        writer.Flush();
        Assert.Equal(0, writes);
        writer.Later();
        writer.Flush();
        writer.Flush();
        Assert.Equal(1, writes);
        writer.Now();
        Assert.Equal(2, writes);
    }

    [Fact]
    public void Now_ReportsWriteErrorsAndKeepsTheChangePending()
    {
        var fail = true;
        var writes = 0;
        var writer = new DeferredWriter(() => "x", _ =>
        {
            if (fail) throw new IOException("gesperrt");
            writes++;
        }, TimeSpan.FromHours(1), _ => { });

        Assert.Throws<IOException>(writer.Now);
        Assert.True(writer.Pending);
        fail = false;
        writer.Flush();
        Assert.Equal(1, writes);
        Assert.False(writer.Pending);
    }

    [Fact]
    public async Task Later_ReportsBackgroundWriteErrors()
    {
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new DeferredWriter(() => "x", _ => throw new UnauthorizedAccessException(), TimeSpan.FromMilliseconds(10),
            ex => failed.TrySetResult(ex));

        writer.Later();
        var error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.IsType<UnauthorizedAccessException>(error);
        Assert.True(writer.Pending);
    }
}

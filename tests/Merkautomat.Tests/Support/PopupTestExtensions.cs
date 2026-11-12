using Merkautomat.Models;
using Merkautomat.ViewModels;
using Xunit;

namespace Merkautomat.Tests;

internal static class PopupTestExtensions
{
    /// <summary>
    /// With <see cref="PopupViewModel.InlineDatabase"/> the addition finishes before it returns; the assertion keeps a
    /// test from ever blocking on it if that changes.
    /// </summary>
    public static void AddNow(this PopupViewModel vm, ClipboardEntry entry)
    {
        var task = vm.AddEntryAsync(entry);
        Assert.True(task.IsCompleted, "AddEntryAsync ran asynchronously; construct the view model with InlineDatabase.");
        task.GetAwaiter().GetResult();
    }
}

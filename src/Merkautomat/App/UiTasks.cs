using System.Windows;
using Merkautomat.Services;
using Merkautomat.Views;

namespace Merkautomat;

/// <summary>
/// The one place where UI work is started without being awaited. An exception escaping an <c>async void</c> handler
/// would reach DispatcherUnhandledException and end the process; here it is logged and reported instead.
/// </summary>
internal static class UiTasks
{
    /// <param name="attempted">What the user tried, shown in the message and the log (never content).</param>
    public static async void Forget(this Task task, string attempted)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Superseded searches and closed dialogs cancel on purpose.
        }
        catch (Exception ex)
        {
            App.LogError(attempted, ex);
            AppMessageBox.Show(
                $"{attempted} ist fehlgeschlagen: {ex.Message}\n\nDetails stehen in {System.IO.Path.Combine(AppPaths.DataDir, "merkautomat.log")}.",
                AppNames.App,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}

using System.Windows;

namespace Merkautomat.Tests;

internal static class TestApplication
{
    /// <summary>
    /// The one Application of the test process. With the default ShutdownMode, closing the last window of one test
    /// shuts it down, and WPF then silently ignores Show() for every window of later tests.
    /// </summary>
    public static Application Ensure()
        => Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
}

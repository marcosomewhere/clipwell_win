namespace Merkautomat.Tests;

internal static class StaTestRunner
{
    public static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally
            {
                // The test may already have shut the dispatcher down itself.
                try { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
                catch (InvalidOperationException) { }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

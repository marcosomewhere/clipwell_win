using System.IO;

namespace Merkautomat.Services;

/// <summary>
/// Writes a whole file for many small changes at once. <see cref="Later"/> collects changes and writes once after a
/// short pause on a background thread; <see cref="Now"/> writes right away. Writes never overlap, and each one takes a
/// fresh snapshot, so a slow earlier write can never put an older state on disk after a newer one.
/// </summary>
public sealed class DeferredWriter(Func<string> snapshot, Action<string> write, TimeSpan delay, Action<Exception> failed)
{
    private readonly object _writeLock = new();
    private long _changed;
    private long _written;
    private int _scheduled;

    /// <summary>True while a change has not reached the file.</summary>
    public bool Pending => Interlocked.Read(ref _changed) != Interlocked.Read(ref _written);

    /// <summary>
    /// Records a change and schedules a write. Call it while the data is still locked, right after changing it,
    /// so the next snapshot is sure to contain the change.
    /// </summary>
    public void Later()
    {
        Interlocked.Increment(ref _changed);
        if (Interlocked.Exchange(ref _scheduled, 1) == 1) return;
        _ = Task.Delay(delay).ContinueWith(_ =>
        {
            Volatile.Write(ref _scheduled, 0);
            try { Flush(); }
            catch (Exception ex) when (IsWriteFailure(ex)) { failed(ex); }
        }, TaskScheduler.Default);
    }

    /// <summary>Records a change and writes it now; write errors reach the caller. Call it without holding the data lock.</summary>
    public void Now()
    {
        Interlocked.Increment(ref _changed);
        Flush();
    }

    /// <summary>Writes pending changes; does nothing when the file is up to date.</summary>
    public void Flush()
    {
        lock (_writeLock)
        {
            // Read before the snapshot: a change made in between is in the snapshot and only causes one more write.
            var changed = Interlocked.Read(ref _changed);
            if (changed == Interlocked.Read(ref _written)) return;
            write(snapshot());
            Interlocked.Exchange(ref _written, changed);
        }
    }

    public static bool IsWriteFailure(Exception ex)
        => ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException;
}

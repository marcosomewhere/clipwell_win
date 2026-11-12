using System.Runtime.InteropServices;

namespace Merkautomat.Services;

public enum UserPresence
{
    Normal,
    /// <summary>A full-screen window that is not a game, e.g. a video in the browser.</summary>
    FullScreen,
    /// <summary>Full-screen Direct3D application (game).</summary>
    Game,
    Presentation,
}

public readonly record struct SystemActivity(TimeSpan IdleFor, bool OnBattery, UserPresence Presence, TimeSpan SinceDirectAiUse);

public enum BackgroundState { Paused, Eco, Fast }

/// <param name="Hard">Stop a running model call at once (game, battery); otherwise the running step may finish.</param>
public sealed record BackgroundDecision(BackgroundState State, string Reason, bool Hard = false)
{
    public bool Allowed => State != BackgroundState.Paused;
}

/// <summary>When background work may run and how much of the machine it may take.</summary>
public static class BackgroundWork
{
    public const string Off = "off";
    public const string Idle = "idle";
    public const string Always = "always";
    public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DirectUsePause = TimeSpan.FromMinutes(3);

    public static string Normalize(string? mode) => mode is Off or Always ? mode : Idle;

    public static BackgroundDecision Decide(string mode, SystemActivity a)
    {
        mode = Normalize(mode);
        if (mode == Off) return new(BackgroundState.Paused, "ausgeschaltet", Hard: true);
        if (a.OnBattery) return new(BackgroundState.Paused, "pausiert im Akkubetrieb", Hard: true);
        if (a.Presence == UserPresence.Game) return new(BackgroundState.Paused, "pausiert, solange ein Vollbildspiel läuft", Hard: true);
        if (a.Presence == UserPresence.Presentation) return new(BackgroundState.Paused, "pausiert im Präsentationsmodus", Hard: true);
        if (a.SinceDirectAiUse < DirectUsePause) return new(BackgroundState.Paused, "pausiert, die lokale KI wird gerade direkt genutzt");
        var idle = a.IdleFor >= IdleAfter;
        if (idle && a.Presence == UserPresence.Normal) return new(BackgroundState.Fast, "läuft im Leerlauf mit voller Geschwindigkeit");
        // A full-screen video often means no input for a long time; it still gets the machine first.
        if (a.Presence == UserPresence.FullScreen) return new(BackgroundState.Eco, "läuft sparsam neben dem Vollbild");
        if (mode == Always) return new(BackgroundState.Eco, "läuft sparsam neben deiner Arbeit");
        return new(BackgroundState.Paused, $"wartet auf Leerlauf ({(int)IdleAfter.TotalMinutes} Minuten ohne Eingabe)");
    }

    /// <summary>Current state of the machine from Windows; unknown values count as "no reason to pause".</summary>
    public static SystemActivity Probe(DateTime lastDirectAiUse)
    {
        var idle = TimeSpan.Zero;
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref info)) idle = TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
        var battery = GetSystemPowerStatus(out var power) && power.ACLineStatus == 0;
        var presence = UserPresence.Normal;
        if (SHQueryUserNotificationState(out var state) == 0)
            presence = state switch
            {
                QunsBusy or QunsApp => UserPresence.FullScreen,
                QunsRunningD3DFullScreen => UserPresence.Game,
                QunsPresentationMode => UserPresence.Presentation,
                _ => UserPresence.Normal,
            };
        var since = DateTime.Now - lastDirectAiUse;
        return new(idle, battery, presence, since < TimeSpan.Zero ? TimeSpan.Zero : since);
    }

    private const int QunsBusy = 2;
    private const int QunsRunningD3DFullScreen = 3;
    private const int QunsPresentationMode = 4;
    private const int QunsApp = 7;

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    /// <summary>Keeps the PC from sleeping while background work runs; per thread, so always call from the UI thread.</summary>
    public static void KeepAwake(bool awake) => SetThreadExecutionState(awake ? EsContinuous | EsSystemRequired : EsContinuous);
}

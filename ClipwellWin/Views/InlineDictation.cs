using System.Windows;
using System.Windows.Threading;
using ClipwellWin.Services;
using TextBox = System.Windows.Controls.TextBox;

namespace ClipwellWin.Views;

/// <summary>
/// Dictates straight into a text box (quick-note slot in popup and KB window) without opening the notes window.
/// The instance itself is the dictation owner, so two slots never record at the same time.
/// </summary>
internal sealed class InlineDictation : IDisposable
{
    private readonly App _app;
    private readonly Window _owner;
    private readonly TextBox _target;
    private readonly DispatcherTimer _levelTimer;
    private string _transientStatus = "";

    public InlineDictation(App app, Window owner, TextBox target)
    {
        _app = app;
        _owner = owner;
        _target = target;
        _levelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _levelTimer.Tick += (_, _) => Changed?.Invoke();
        _app.Dictation.StateChanged += Dictation_StateChanged;
    }

    /// <summary>Raised on the UI thread whenever button or status should be redrawn.</summary>
    public event Action? Changed;

    private DictationService D => _app.Dictation;
    private bool IsMine => ReferenceEquals(D.Owner, this);

    public bool IsRecording => IsMine && D.State == DictationState.Recording;
    public bool IsWorking => IsMine && D.State is DictationState.Downloading or DictationState.Transcribing;
    public bool IsBlocked => D.IsBusyFor(this);
    public bool IsActive => IsMine && D.State != DictationState.Idle;

    public string StatusText
    {
        get
        {
            if (IsRecording)
            {
                var t = D.Microphone.Elapsed;
                return $"Aufnahme läuft {(int)t.TotalMinutes}:{t.Seconds:00} - erneut klicken zum Beenden";
            }
            if (IsActive) return D.StatusText;
            return _transientStatus;
        }
    }

    public async Task ToggleAsync()
    {
        _transientStatus = "";
        var wasRecording = IsRecording;
        var text = await _app.ToggleDictationAsync(this, _owner);
        if (!string.IsNullOrWhiteSpace(text)) Insert(text.Trim());
        else if (wasRecording && text != null) _transientStatus = "Keine Sprache erkannt.";
        Changed?.Invoke();
    }

    /// <summary>Ends a running recording and inserts its text, so saving never drops spoken words.</summary>
    public async Task FinishAsync()
    {
        if (IsRecording) await ToggleAsync();
    }

    public void Cancel()
    {
        if (IsMine) D.Cancel();
        _transientStatus = "";
        Changed?.Invoke();
    }

    public void ClearStatus()
    {
        _transientStatus = "";
        Changed?.Invoke();
    }

    private void Insert(string text)
    {
        var current = _target.Text;
        var caret = Math.Clamp(_target.CaretIndex, 0, current.Length);
        var before = caret > 0 ? current[caret - 1] : ' ';
        var after = caret < current.Length ? current[caret] : ' ';
        var insert = (char.IsWhiteSpace(before) ? "" : " ") + text + (char.IsWhiteSpace(after) ? "" : " ");
        _target.Text = current.Insert(caret, insert);
        _target.CaretIndex = caret + insert.Length;
        _target.Focus();
    }

    private void Dictation_StateChanged(object? sender, EventArgs e)
        => _owner.Dispatcher.BeginInvoke(() =>
        {
            if (IsRecording) _levelTimer.Start(); else _levelTimer.Stop();
            Changed?.Invoke();
        });

    public void Dispose()
    {
        _app.Dictation.StateChanged -= Dictation_StateChanged;
        _levelTimer.Stop();
        if (IsMine) D.Cancel();
    }
}

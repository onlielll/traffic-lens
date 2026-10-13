using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProxyTrafficFloat;

public sealed partial class QuotaPanel
{
    public void VerifyCommitForTest()
    {
        void Enter(string value)
        {
            input.Text = value;
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        }
        Enter("24");
        if (app.Config.QuotaGB != 24 || app.Config.QuotaBaselineBytes != app.Store.ObservedBytes()) throw new InvalidOperationException("Quota input did not commit its snapshot.");
        var saved = app.Config.QuotaUpdatedUtc;
        Enter("-1");
        if (app.Config.QuotaUpdatedUtc != saved || app.Config.QuotaGB != 24) throw new InvalidOperationException("Invalid quota input changed the snapshot.");
        app.Store.Add(new[] { new Increment(DateTime.Today, "downloads.example", 1_500_000_000, 500_000_000) });
        Refresh(true);
        if (duration.Text != Strings.Format("约 {0:0} 天", 22)) throw new InvalidOperationException("Observed usage did not reduce the displayed allowance.");
        Enter("48");
    }
    public string DurationForTest => duration.Text;
}

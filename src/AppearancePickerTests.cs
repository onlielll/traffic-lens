using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shell;

namespace ProxyTrafficFloat;

public sealed partial class AppearancePicker
{
    public async System.Threading.Tasks.Task VerifyForTest(string output)
    {
        popup.IsOpen = true;
        MoveSelection(0, false);
        MoveSelection(2, true);
        choose("dark");
        await System.Threading.Tasks.Task.Delay(70);
        double middle = selectionPosition.X;
        await System.Threading.Tasks.Task.Delay(230);
        if ((SystemParameters.ClientAreaAnimation && (middle <= 0 || middle >= 172)) || selectionPosition.HasAnimatedProperties || selectionPosition.X != 172)
            throw new InvalidOperationException("Appearance selection did not slide or release its clock.");
        popup.IsOpen = false;
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, "appearance-animation.txt"), $"PASS: middle={middle:0.#}, end=172, theme refresh preserved the 220 ms slide; clock released\n");
    }
    public async System.Threading.Tasks.Task VerifyPopupLifecycleForTest()
    {
        var shell = (Border)popup.Child;
        bool respectsPreference = true, released, closedReleased;
        async System.Threading.Tasks.Task ClosePopupAsync()
        {
            var closed = new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler closedHandler = (_, _) => closed.TrySetResult(true);
            popup.Closed += closedHandler;
            try
            {
                if (popup.IsOpen)
                {
                    popup.IsOpen = false;
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(2));
                }
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            }
            finally { popup.Closed -= closedHandler; }
        }
        try
        {
            popup.IsOpen = true;
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            if (!SystemParameters.ClientAreaAnimation)
                respectsPreference = shell.Opacity == 1 && !shell.HasAnimatedProperties;
            await System.Threading.Tasks.Task.Delay(220);
            released = shell.Opacity == 1 && !shell.HasAnimatedProperties;
            await ClosePopupAsync();
            popup.IsOpen = true;
            await System.Threading.Tasks.Task.Delay(35);
            await ClosePopupAsync();
            closedReleased = !shell.HasAnimatedProperties && !selectionPosition.HasAnimatedProperties;
        }
        finally { popup.IsOpen = false; }
        if (!respectsPreference || !released || !closedReleased)
            throw new InvalidOperationException($"Appearance fade lifecycle failed: preference={respectsPreference}, completed={released}, closed={closedReleased}, systemAnimation={SystemParameters.ClientAreaAnimation}.");
    }
    public void OpenForTest()
    {
        Refresh();
        popup.IsOpen = true;
    }
    public void CloseForTest() => popup.IsOpen = false;
    public UIElement PreviewForTest => popup.Child;
}

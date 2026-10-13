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
    public void OpenForTest()
    {
        Refresh();
        popup.IsOpen = true;
    }
    public void CloseForTest() => popup.IsOpen = false;
    public UIElement PreviewForTest => popup.Child;
}

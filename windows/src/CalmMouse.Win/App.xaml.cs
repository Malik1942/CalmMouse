using System.Windows;

namespace CalmMouse.Win;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Temporary developer switch (plan task 16): the engine with every feature off, logging to %TEMP%.
        if (e.Args.Contains(EngineTest.Switch, StringComparer.OrdinalIgnoreCase))
        {
            EngineTest.Run(this, e.Args);
            return;
        }
        new MainWindow().Show();
    }
}

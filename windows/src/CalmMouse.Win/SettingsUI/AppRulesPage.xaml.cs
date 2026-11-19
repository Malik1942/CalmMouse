using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using static CalmMouse.Win.Localization.Strings;

namespace CalmMouse.Win.SettingsUI;

/// <summary>App rules: the list, and "Add an app…" with the apps open now and a program picker.</summary>
public partial class AppRulesPage : UserControl
{
    public AppRulesPage() => InitializeComponent();

    private SettingsViewModel? Model => DataContext as SettingsViewModel;

    /// <summary>A menu under the button: every app open now (icon and name), then "Choose a program…".</summary>
    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model) { return; }
        var menu = new ContextMenu { PlacementTarget = AddButton, Placement = PlacementMode.Bottom };
        IReadOnlyList<AppCandidate> apps = model.AppsToAdd();
        foreach (AppCandidate app in apps)
        {
            var item = new MenuItem { Header = app.DisplayName, ToolTip = app.ExeName };
            if (model.IconFor(app) is { } icon) { item.Icon = new Image { Source = icon, Width = 16, Height = 16 }; }
            AutomationProperties.SetName(item, app.DisplayName);
            item.Click += (_, _) => Added(model.AddRule(app));
            menu.Items.Add(item);
        }
        if (apps.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = L("No other apps are open."), IsEnabled = false });
        }
        menu.Items.Add(new Separator());
        var choose = new MenuItem { Header = L("Choose a program…") };
        choose.Click += (_, _) => ChooseProgram(model);
        menu.Items.Add(choose);
        menu.IsOpen = true;
    }

    private void ChooseProgram(SettingsViewModel model)
    {
        var dialog = new OpenFileDialog
        {
            Filter = $"{L("Programs")} (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            Added(model.AddRule(model.DescribeProgram(dialog.FileName)));
        }
    }

    /// <summary>Shows the rule just added (or the one the app already had) and puts the focus on it.</summary>
    private void Added(AppRuleViewModel rule)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (RuleList.ItemContainerGenerator.ContainerFromItem(rule) is FrameworkElement card)
            {
                card.BringIntoView();
                card.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}

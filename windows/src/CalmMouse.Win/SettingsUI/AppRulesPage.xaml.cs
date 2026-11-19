using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
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
        // A menu is a window of its own: it takes the window's language, so Chinese is drawn with the right forms.
        var menu = new ContextMenu { PlacementTarget = AddButton, Placement = PlacementMode.Bottom, Language = Language };
        IReadOnlyList<AppCandidate> apps = model.AppsToAdd();
        foreach (AppCandidate app in apps)
        {
            var item = new MenuItem { Header = app.DisplayName, ToolTip = app.ExeName };
            if (model.IconFor(app) is { } icon) { item.Icon = new Image { Source = icon, Width = 16, Height = 16 }; }
            AutomationProperties.SetName(item, app.DisplayName);
            item.Click += (_, _) => FocusRule(model.AddRule(app));
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
            FocusRule(model.AddRule(model.DescribeProgram(dialog.FileName)));
        }
    }

    /// <summary>Removes the rule, then puts the focus on the next one's switch, or on "Add an app…" if it was the last.</summary>
    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model || (sender as FrameworkElement)?.DataContext is not AppRuleViewModel rule) { return; }
        int index = model.Rules.IndexOf(rule);
        model.RemoveRule(rule);
        if (index >= 0 && index < model.Rules.Count) { FocusRule(model.Rules[index]); }
        else if (model.Rules.Count > 0) { FocusRule(model.Rules[^1]); }
        else { AddButton.Focus(); }
    }

    /// <summary>Shows a rule's card and puts the keyboard focus on its "Rule enabled" switch.</summary>
    internal void FocusRule(AppRuleViewModel rule)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (RuleList.ItemContainerGenerator.ContainerFromItem(rule) is not FrameworkElement card) { return; }
            card.BringIntoView();
            if (FindSwitch(card) is { } toggle) { Keyboard.Focus(toggle); }
        }, DispatcherPriority.Loaded);
    }

    private static ToggleSwitch? FindSwitch(DependencyObject root)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ToggleSwitch toggle) { return toggle; }
            if (FindSwitch(child) is { } inner) { return inner; }
        }
        return null;
    }
}

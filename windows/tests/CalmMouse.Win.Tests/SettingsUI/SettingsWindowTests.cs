using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using CalmMouse.Core.Routing;
using CalmMouse.Win.Input;
using CalmMouse.Win.Localization;
using CalmMouse.Win.SettingsUI;

namespace CalmMouse.Win.Tests.SettingsUI;

/// <summary>The window and its XAML: localized text that follows the language, every key translated, and that it loads.</summary>
public sealed partial class SettingsWindowTests
{
    private static Localizer Sample() => new(new Dictionary<UiLanguage, IReadOnlyDictionary<string, string>>
    {
        [UiLanguage.English] = new Dictionary<string, string> { ["Don't scroll while pressed"] = "Don't scroll while pressed" },
        [UiLanguage.SimplifiedChinese] = new Dictionary<string, string> { ["Don't scroll while pressed"] = "按下时不滚动" },
        [UiLanguage.TraditionalChinese] = new Dictionary<string, string> { ["Don't scroll while pressed"] = "按下時不捲動" },
    });

    // MARK: {loc:L}

    [Fact]
    public void LocalizedTextFollowsTheLanguage() => WpfThread.Run(() =>
    {
        Localizer localizer = Sample();
        var source = new LocalizedText(localizer);
        var text = new TextBlock();
        var binding = (Binding)new LExtension("Don't scroll while pressed") { Source = source }.ProvideValue(null!);
        text.SetBinding(TextBlock.TextProperty, binding);
        Assert.Equal("Don't scroll while pressed", text.Text);

        localizer.SetLanguage(UiLanguage.SimplifiedChinese);
        Assert.Equal("按下时不滚动", text.Text);
        localizer.SetLanguage(UiLanguage.TraditionalChinese);
        Assert.Equal("按下時不捲動", text.Text);

        // An attached property (the explanation) follows too.
        var row = new Border();
        BindingOperations.SetBinding(row, CalmMouse.Win.SettingsUI.Explain.TextProperty, source.Bind("Missing key"));
        Assert.Equal("Missing key", CalmMouse.Win.SettingsUI.Explain.GetText(row));
    });

    [Fact]
    public void TheMarkupExtensionReadsQuotesAndLineBreaksInKeys() => WpfThread.Run(() =>
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                        xmlns:loc="clr-namespace:CalmMouse.Win.Localization;assembly=CalmMouse.Win">
                <TextBlock Text="{loc:L 'Don\'t scroll while pressed'}" />
                <TextBlock Text="{loc:L 'Pressing a wheel often turns it too.&#10;That won\'t scroll the page.'}" />
                <TextBlock Text="{loc:L Language}" />
            </StackPanel>
            """;
        var panel = (StackPanel)XamlReader.Parse(xaml);
        string[] texts = panel.Children.OfType<TextBlock>().Select(t => t.Text).ToArray();
        Assert.Equal(Strings.L("Don't scroll while pressed"), texts[0]);
        Assert.Equal(Strings.L("Pressing a wheel often turns it too.\nThat won't scroll the page."), texts[1]);
        Assert.Equal(Strings.L("Language"), texts[2]);
    });

    // MARK: Every key in the XAML is translated

    [Fact]
    public void EveryStringInTheXamlIsInAllThreeLanguages()
    {
        Dictionary<UiLanguage, IReadOnlyDictionary<string, string>> tables = Localizer.LoadEmbeddedTables();
        List<string> keys = XamlKeys();
        Assert.Contains("Smooth scrolling", keys);
        Assert.Contains("Pressing a wheel often turns it too.\nThat won't scroll the page.", keys);
        foreach (string key in keys)
        {
            foreach (UiLanguage language in Enum.GetValues<UiLanguage>())
            {
                Assert.True(tables[language].ContainsKey(key), $"{{loc:L '{key}'}} is missing from the {Localizer.LprojName(language)} tables");
            }
        }
    }

    [GeneratedRegex("""\{loc:L\s+(?:'((?:[^'\\]|\\.)*)'|([^'}\s][^}]*))\}""")]
    private static partial Regex LocMarkup();

    /// <summary>Every <c>{loc:L …}</c> key in windows/src/CalmMouse.Win's XAML, as the parser reads it.</summary>
    private static List<string> XamlKeys()
    {
        string src = Path.Combine(RepoRoot(), "windows", "src", "CalmMouse.Win");
        return Directory.EnumerateFiles(src, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => LocMarkup().Matches(File.ReadAllText(f)))
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value.Trim())
            .Select(raw => Regex.Replace(WebUtility.HtmlDecode(raw), @"\\(.)", "$1"))
            .Distinct()
            .ToList();
    }

    private static string RepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "windows", "src"))) { return dir.FullName; }
        }
        throw new DirectoryNotFoundException("repository root not found");
    }

    // MARK: The window itself

    [Fact]
    public void TheWindowOpensOnEveryPageAndClosesCleanly() => WpfThread.Run(() =>
    {
        if (Application.Current == null)
        {
#pragma warning disable WPF0001   // ThemeMode is marked experimental
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.Light };
#pragma warning restore WPF0001
        }
        using var temp = new TempStore();
        var host = new FakeHost
        {
            Devices = [new MouseDeviceInfo(1, DeviceKind.WheelMouse, "A", "Alpha"), new MouseDeviceInfo(2, DeviceKind.Touchpad, "T", "Pad")],
            Health = new EngineHealth("It stopped.", true),
            ConflictMessage = "Another tool.",
        };
        var model = new SettingsViewModel(temp.Store, host, new FakeApps(), action => Application.Current!.Dispatcher.BeginInvoke(action));
        var window = new SettingsWindow(model) { Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show();
        foreach (SettingsPage page in Enum.GetValues<SettingsPage>())
        {
            model.Page = page;
            WpfThread.DoEvents();
        }

        // The Devices page's switches are the store's settings, and pointing at a row explains it.
        model.Page = SettingsPage.Devices;
        WpfThread.DoEvents();
        CheckBox smooth = Descendants<CheckBox>(window).First(c => Equals(c.Content, Strings.L("Smooth scrolling")) && c.IsVisible);
        Assert.Equal(Strings.L("Smooth scrolling"), AutomationName(smooth));
        var row = (Border)smooth.Parent;
        row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        Assert.Equal(Strings.L("The page glides instead of jumping.\nZooming stays one step per notch."), model.Explanation.Text);
        smooth.IsChecked = true;
        Assert.True(temp.Store.SettingsFor("A").Smooth);
        WpfThread.DoEvents();
        Assert.Contains(Descendants<Slider>(window), s => s.IsVisible);   // the speed shows once smoothing is on

        // A screen reader gets the explanation as the switch's help text, and calls it a toggle switch.
        string explained = OneLine.Join(Strings.L("The page glides instead of jumping.\nZooming stays one step per notch."), Localizer.Shared.Language);
        Assert.Equal(explained, System.Windows.Automation.AutomationProperties.GetHelpText(smooth));
        Assert.Equal(Strings.L("toggle switch"), System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(smooth).GetLocalizedControlType());
        Assert.Contains(Descendants<TextBlock>(window), t => t.IsVisible && t.Text == Strings.L("Devices")
            && System.Windows.Automation.AutomationProperties.GetHeadingLevel(t) == System.Windows.Automation.AutomationHeadingLevel.Level1);

        // The explanation is a row's tooltip only while the panel is hidden (a narrow window).
        Assert.False(ToolTipService.GetIsEnabled(row));
        window.Width = 820;
        WpfThread.DoEvents();
        Assert.True(model.Explanation.ShowToolTips);
        Assert.True(ToolTipService.GetIsEnabled(row));
        Assert.Equal(explained, row.ToolTip);
        window.Width = 940;
        WpfThread.DoEvents();
        Assert.False(ToolTipService.GetIsEnabled(row));

        // Every control a person can reach has a name a screen reader can read: the pages, three switches and the speed.
        List<Control> reachable = Descendants<Control>(window)
            .Where(c => c.Focusable && c.IsVisible && c is System.Windows.Controls.Primitives.ButtonBase or Slider or ComboBox or ListBoxItem)
            .ToList();
        Assert.True(reachable.Count >= 7, $"only {reachable.Count} controls found");
        Assert.All(reachable, control => Assert.False(string.IsNullOrWhiteSpace(AutomationName(control)), $"{control.GetType().Name} has no name"));

        // App rules: Remove is named after its app; after removing the last rule the focus goes to "Add an app…".
        model.Page = SettingsPage.AppRules;
        AppRuleViewModel rule = model.AddRule(new AppCandidate("figma.exe", "Figma", null));
        WpfThread.DoEvents();
        Button remove = Descendants<Button>(window).First(b => b.DataContext == rule);
        Assert.Equal(Strings.L("Remove %@", "Figma"), AutomationName(remove));
        window.Activate();

        // A new rule gets the focus on its switch; Tab goes through the two choices, and Remove comes last.
        AppRulesPage rulesPage = Descendants<AppRulesPage>(window).First();
        rulesPage.FocusRule(rule);
        WpfThread.DoEvents();
        ToggleSwitch enabled = Descendants<ToggleSwitch>(window).First(t => t.DataContext == rule);
        Assert.True(enabled.IsKeyboardFocused);
        var next = new TraversalRequest(FocusNavigationDirection.Next);
        ((UIElement)Keyboard.FocusedElement).MoveFocus(next);
        Assert.True(Keyboard.FocusedElement is RadioButton { IsChecked: true });
        ((UIElement)Keyboard.FocusedElement).MoveFocus(next);
        Assert.True(Keyboard.FocusedElement is RadioButton { IsChecked: false });
        ((UIElement)Keyboard.FocusedElement).MoveFocus(next);
        Assert.Same(remove, Keyboard.FocusedElement);

        remove.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        WpfThread.DoEvents();
        Assert.Empty(model.Rules);
        Button add = Descendants<Button>(window).First(b => AutomationName(b) == Strings.L("Add an app…"));
        Assert.True(add.IsKeyboardFocused);

        window.Close();
        Assert.Equal(0, host.DevicesChangedHandlers);
        Assert.Equal(0, host.EngineHandlers);
    });

    private static string AutomationName(DependencyObject element) => System.Windows.Automation.AutomationProperties.GetName(element);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) { yield return match; }
            foreach (T inner in Descendants<T>(child)) { yield return inner; }
        }
    }
}

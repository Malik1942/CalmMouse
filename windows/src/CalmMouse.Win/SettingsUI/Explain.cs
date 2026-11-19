// The settings window's side panel, as the Mac's caption under the device: point at a setting
// (or move the keyboard focus to it) and the panel says, in plain words, what it does.
//
//   <Border ui:Explain.Text="{loc:L 'Pressing a wheel often turns it too.&#10;That won't scroll the page.'}"> … </Border>
//
// Any element can carry an explanation. The window sets Explain.Panel (an ExplainState) once on
// its root; it is inherited down the tree, so every row finds it. The panel shows the row under
// the pointer, else the row holding the keyboard focus, else the page's intro.

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace CalmMouse.Win.SettingsUI;

/// <summary>
/// What the explanation panel shows. Rows are tracked by identity, innermost (most recent) first,
/// so a row inside a card wins over the card, and leaving it falls back to the card.
/// </summary>
public sealed class ExplainState : INotifyPropertyChanged
{
    private readonly List<(object Source, string? Text)> _hovered = [];
    private readonly List<(object Source, string? Text)> _focused = [];
    private string _intro = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>What the panel says when no row is pointed at or focused.</summary>
    public string Intro
    {
        get => _intro;
        set
        {
            if (_intro == value) { return; }
            _intro = value;
            Changed();
        }
    }

    /// <summary>The hovered row's text, else the focused row's, else <see cref="Intro"/>.</summary>
    public string Text => Pick(_hovered) ?? Pick(_focused) ?? Intro;

    /// <summary>True while the panel shows the intro (drawn quieter).</summary>
    public bool ShowsIntro => Pick(_hovered) == null && Pick(_focused) == null;

    public void PointerEntered(object source, string? text) => Add(_hovered, source, text);

    public void PointerLeft(object source) => Remove(_hovered, source);

    public void FocusEntered(object source, string? text) => Add(_focused, source, text);

    public void FocusLeft(object source) => Remove(_focused, source);

    /// <summary>A row's text changed (the language did): shown at once if it's the one on show.</summary>
    public void TextChanged(object source, string? text)
    {
        bool changed = Replace(_hovered, source, text) | Replace(_focused, source, text);
        if (changed) { Changed(); }
    }

    /// <summary>A row went away (hidden, unloaded): it no longer counts.</summary>
    public void Forget(object source)
    {
        bool changed = _hovered.RemoveAll(e => ReferenceEquals(e.Source, source)) > 0;
        changed |= _focused.RemoveAll(e => ReferenceEquals(e.Source, source)) > 0;
        if (changed) { Changed(); }
    }

    /// <summary>Another page: nothing from the last one counts.</summary>
    public void Reset(string intro)
    {
        _hovered.Clear();
        _focused.Clear();
        _intro = intro;
        Changed();
    }

    private static string? Pick(List<(object Source, string? Text)> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (!string.IsNullOrEmpty(list[i].Text)) { return list[i].Text; }
        }
        return null;
    }

    private void Add(List<(object Source, string? Text)> list, object source, string? text)
    {
        list.RemoveAll(e => ReferenceEquals(e.Source, source));
        list.Add((source, text));
        Changed();
    }

    private void Remove(List<(object Source, string? Text)> list, object source)
    {
        if (list.RemoveAll(e => ReferenceEquals(e.Source, source)) > 0) { Changed(); }
    }

    private static bool Replace(List<(object Source, string? Text)> list, object source, string? text)
    {
        int i = list.FindIndex(e => ReferenceEquals(e.Source, source));
        if (i < 0) { return false; }
        list[i] = (source, text);
        return true;
    }

    private void Changed()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowsIntro)));
    }
}

/// <summary>The attached properties: <c>Explain.Text</c> on a row, <c>Explain.Panel</c> on the window.</summary>
public static class Explain
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Explain), new PropertyMetadata(null, OnTextChanged));

    public static readonly DependencyProperty PanelProperty = DependencyProperty.RegisterAttached(
        "Panel", typeof(ExplainState), typeof(Explain), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    public static ExplainState? GetPanel(DependencyObject element) => (ExplainState?)element.GetValue(PanelProperty);

    public static void SetPanel(DependencyObject element, ExplainState? value) => element.SetValue(PanelProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) { return; }
        if (e.OldValue == null)
        {
            element.MouseEnter += OnMouseEnter;
            element.MouseLeave += OnMouseLeave;
            element.IsKeyboardFocusWithinChanged += OnFocusWithinChanged;
            element.IsVisibleChanged += OnVisibleChanged;
            element.Unloaded += OnUnloaded;
        }
        (GetPanel(element) ?? (ExplainState?)element.GetValue(ReportedToProperty))?.TextChanged(element, (string?)e.NewValue);
    }

    /// <summary>The panel a row last reported to, so it can still be told once the row has left the tree.</summary>
    private static readonly DependencyProperty ReportedToProperty = DependencyProperty.RegisterAttached(
        "ReportedTo", typeof(ExplainState), typeof(Explain), new PropertyMetadata(null));

    private static ExplainState? PanelFor(FrameworkElement element)
    {
        ExplainState? panel = GetPanel(element);
        if (panel != null) { element.SetValue(ReportedToProperty, panel); }
        return panel;
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element) { PanelFor(element)?.PointerEntered(element, GetText(element)); }
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element) { PanelFor(element)?.PointerLeft(element); }
    }

    private static void OnFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || PanelFor(element) is not { } panel) { return; }
        if ((bool)e.NewValue) { panel.FocusEntered(element, GetText(element)); }
        else { panel.FocusLeft(element); }
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element && !(bool)e.NewValue) { Forget(element); }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element) { Forget(element); }
    }

    private static void Forget(FrameworkElement element) =>
        (GetPanel(element) ?? (ExplainState?)element.GetValue(ReportedToProperty))?.Forget(element);
}

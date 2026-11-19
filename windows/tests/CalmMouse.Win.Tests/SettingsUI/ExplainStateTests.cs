using CalmMouse.Win.SettingsUI;

namespace CalmMouse.Win.Tests.SettingsUI;

public sealed class ExplainStateTests
{
    private static ExplainState State() => new() { Intro = "Point at a setting." };

    [Fact]
    public void ShowsTheIntroUntilSomethingIsPointedAtOrFocused()
    {
        ExplainState state = State();
        Assert.Equal("Point at a setting.", state.Text);
        Assert.True(state.ShowsIntro);
    }

    [Fact]
    public void TheRowUnderThePointerWinsOverTheFocusedOne()
    {
        ExplainState state = State();
        object smooth = new(), reverse = new();
        state.FocusEntered(smooth, "Glides.");
        Assert.Equal("Glides.", state.Text);
        Assert.False(state.ShowsIntro);
        state.PointerEntered(reverse, "Flips.");
        Assert.Equal("Flips.", state.Text);
        state.PointerLeft(reverse);
        Assert.Equal("Glides.", state.Text);   // back to the focused row
        state.FocusLeft(smooth);
        Assert.Equal("Point at a setting.", state.Text);
    }

    [Fact]
    public void AnInnerRowWinsOverItsCardAndLeavingItFallsBackToTheCard()
    {
        ExplainState state = State();
        object card = new(), row = new();
        state.PointerEntered(card, "The card.");
        state.PointerEntered(row, "The row.");
        Assert.Equal("The row.", state.Text);
        state.PointerLeft(row);
        Assert.Equal("The card.", state.Text);
    }

    [Fact]
    public void ARowWithoutTextIsSkipped()
    {
        ExplainState state = State();
        object card = new(), plain = new();
        state.PointerEntered(card, "The card.");
        state.PointerEntered(plain, null);
        Assert.Equal("The card.", state.Text);
    }

    [Fact]
    public void ANewLanguageShowsAtOnce()
    {
        ExplainState state = State();
        object row = new();
        var changes = new List<string?>();
        state.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        state.PointerEntered(row, "Flips.");
        state.TextChanged(row, "反转。");
        Assert.Equal("反转。", state.Text);
        state.TextChanged(new object(), "Not on show.");   // a row nobody points at: nothing to say
        Assert.Equal([nameof(ExplainState.Text), nameof(ExplainState.ShowsIntro), nameof(ExplainState.Text), nameof(ExplainState.ShowsIntro)], changes);
    }

    [Fact]
    public void AHiddenOrRemovedRowNoLongerCounts()
    {
        ExplainState state = State();
        object speed = new();
        state.PointerEntered(speed, "How far.");
        state.FocusEntered(speed, "How far.");
        state.Forget(speed);
        Assert.True(state.ShowsIntro);
    }

    [Fact]
    public void AnotherPageClearsEverything()
    {
        ExplainState state = State();
        state.PointerEntered(new object(), "Old page.");
        state.FocusEntered(new object(), "Old page.");
        state.Reset("New intro.");
        Assert.Equal("New intro.", state.Text);
        Assert.True(state.ShowsIntro);
    }
}

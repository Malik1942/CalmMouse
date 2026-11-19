using System.Windows.Automation.Peers;
using System.Windows.Controls;
using static CalmMouse.Win.Localization.Strings;

namespace CalmMouse.Win.SettingsUI;

/// <summary>
/// An on/off switch: a CheckBox drawn as a Windows 11 toggle switch (the "Switch" style), which
/// screen readers call a "toggle switch", as they do Windows' own.
/// </summary>
public sealed class ToggleSwitch : CheckBox
{
    protected override AutomationPeer OnCreateAutomationPeer() => new ToggleSwitchAutomationPeer(this);

    private sealed class ToggleSwitchAutomationPeer(ToggleSwitch owner) : CheckBoxAutomationPeer(owner)
    {
        protected override string GetLocalizedControlTypeCore() => L("toggle switch");
    }
}

using Windows.Win32;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;

namespace CalmMouse.Win.Input;

/// <summary>How often a glide pays out a frame: once per refresh of the main display.</summary>
public static class FrameTiming
{
    /// <summary>Used when the display's rate can't be read.</summary>
    public const double DefaultHz = 60;

    /// <summary>Slower displays still glide at this rate (a glide frame is cheap, a stutter isn't).</summary>
    public const double MinimumHz = 60;

    /// <summary>Faster displays glide at this rate: more frames than this only add SendInput calls nobody sees.</summary>
    public const double MaximumHz = 144;

    /// <summary>The frame rate to glide at for a display rate in Hz (0, 1, NaN = unknown).</summary>
    public static double GlideHz(double displayHz)
    {
        // DEVMODE gives 0 or 1 for "the hardware's default".
        if (!double.IsFinite(displayHz) || displayHz <= 1) { return DefaultHz; }
        return Math.Clamp(displayHz, MinimumHz, MaximumHz);
    }

    /// <summary>Seconds between glide frames for a display rate in Hz.</summary>
    public static double IntervalSeconds(double displayHz) => 1.0 / GlideHz(displayHz);

    /// <summary>A refresh rate given as a ratio (DWM's rateRefresh); NaN if it isn't one.</summary>
    public static double FromRatio(uint numerator, uint denominator) =>
        denominator == 0 || numerator == 0 ? double.NaN : (double)numerator / denominator;

    /// <summary>
    /// The main display's refresh rate in Hz: the display mode's frequency (a whole number, steady),
    /// else DWM's composition rate (measured, so it wanders a little on some laptops), else NaN.
    /// </summary>
    public static unsafe double ReadDisplayHz()
    {
        try
        {
            var mode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
            if (PInvoke.EnumDisplaySettings(null, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref mode)
                && mode.dmDisplayFrequency > 1)
            {
                return mode.dmDisplayFrequency;
            }
        }
        catch
        {
            // Try DWM.
        }
        try
        {
            var timing = new DWM_TIMING_INFO { cbSize = (uint)sizeof(DWM_TIMING_INFO) };
            if (PInvoke.DwmGetCompositionTimingInfo(default, &timing).Succeeded)
            {
                return FromRatio(timing.rateRefresh.uiNumerator, timing.rateRefresh.uiDenominator);
            }
        }
        catch
        {
            // Fall through to "unknown".
        }
        return double.NaN;
    }
}

using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Utilities.UnifiedSettings;
using ShowPathOnTabs.Core.Captioning;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ShowPathOnTabs.Services;

/// <summary>
/// While switched on, sets Visual Studio's maximum tab width to what the widest caption needs, never
/// below <see cref="RestoredTabWidth"/>. <see cref="TabWidening.ToFit"/> goes as wide as Visual Studio
/// allows; <see cref="TabWidening.ToLimit"/> stops at the user's limit. When switched off, sets it back
/// to <see cref="RestoredTabWidth"/> once. The user's own width is not remembered: while this is
/// on, the extension owns the setting.
/// </summary>
/// <param name="settingsManager">Used to read and write Visual Studio's tab width. May be null.</param>
/// <param name="createMeasurer">Returns a function that measures caption text, or null when it cannot.</param>
internal sealed class TabWidener(
    ISettingsManager? settingsManager,
    Func<Func<string, double>?> createMeasurer) : IDisposable
{
    /// <summary>The width tabs go back to: Visual Studio's default.</summary>
    internal const int RestoredTabWidth = TabCaptionBudgetProvider.DefaultMaximumTabWidth;

    /// <summary>The widest Visual Studio accepts for its maximum tab width setting.</summary>
    internal const int VsMaximumTabWidth = 999;

    private const string WriterName = "ShowPathOnTabs";

    private readonly TabCaptionBudgetProvider _tabSettings = new(settingsManager);

    // Whether widening was on at the last refresh, so switching it off restores the width only once
    // and a width the user sets while it is off is left alone.
    private bool _wasOn;

    /// <summary>Called after the tab captions have been set.</summary>
    /// <param name="limit">The widest a tab may be made under <see cref="TabWidening.ToLimit"/>.</param>
    public void Apply(TabWidening mode, int limit, IReadOnlyList<string> captions)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            if (mode != TabWidening.Off)
            {
                _wasOn = true;
                int widest = mode == TabWidening.ToLimit
                    ? Math.Max(RestoredTabWidth, Math.Min(limit, VsMaximumTabWidth))
                    : VsMaximumTabWidth;
                SetWidth(WidthFor(captions, widest));
            }
            else if (_wasOn)
            {
                _wasOn = false;
                SetWidth(RestoredTabWidth);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShowPathOnTabs: could not set the tab width: {ex.Message}");
        }
    }

    private int WidthFor(IReadOnlyList<string> captions, int widest)
    {
        var measure = createMeasurer();
        if (measure == null || captions.Count == 0) return RestoredTabWidth;

        int needed = TabCaptionBudgetProvider.TabWidthFor(captions.Max(measure));
        return Math.Max(RestoredTabWidth, Math.Min(needed, widest));
    }

    private void SetWidth(int width)
    {
        if (settingsManager == null || _tabSettings.ReadMaximumTabWidth() == width) return;

        var writer = settingsManager.GetWriter(WriterName);
        writer.EnqueueChange(TabCaptionBudgetProvider.MaximumTabWidthMoniker, width);
        var commit = writer.RequestCommit("Show Path On Tabs: fit long tab captions");
        Debug.WriteLine($"ShowPathOnTabs: maximum tab width set to {width} ({commit.Outcome})");
    }

    /// <summary>Puts the width back when the extension unloads with widening on.</summary>
    public void Dispose()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Apply(TabWidening.Off, 0, []);
    }
}

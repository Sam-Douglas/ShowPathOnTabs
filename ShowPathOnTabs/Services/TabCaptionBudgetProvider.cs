using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Utilities.UnifiedSettings;
using ShowPathOnTabs.Core.Captioning;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ShowPathOnTabs.Services;

/// <summary>
/// Works out how much text fits on a document tab, so folder names are abbreviated only as far as
/// needed. The figure is an estimate: when it is off, a caption is abbreviated slightly more or less
/// than ideal, and Visual Studio still trims anything that is too long.
/// </summary>
/// <param name="settingsManager">Used to read Visual Studio's own tab settings. May be null.</param>
internal sealed class TabCaptionBudgetProvider(ISettingsManager? settingsManager)
{
    /// <summary>Visual Studio's "maximum tab width" setting.</summary>
    public const string MaximumTabWidthMoniker = "environment.tabs.documentTabs.maximumTabWidth";

    /// <summary>Visual Studio's "show selected tab in bold" setting.</summary>
    public const string BoldSelectedTabsMoniker = "environment.tabs.documentTabs.boldSelectedTabs";

    /// <summary>Visual Studio's default when the setting cannot be read.</summary>
    internal const int DefaultMaximumTabWidth = 215;

    /// <summary>
    /// The part of a tab's width that is not caption text: padding, the colour bar, the pin and close
    /// buttons and the unsaved-changes marker. Visual Studio does not publish this figure; it was
    /// measured from Visual Studio 2026 with default tab settings (about 73) and padded.
    /// </summary>
    internal const double TabChromeWidth = 96;

    /// <param name="maximumTabWidth">
    /// The tab width to fit captions into, or null to use Visual Studio's current setting.
    /// </param>
    public CaptionBudget? Create(int? maximumTabWidth = null)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        double textWidth = TextWidthFor(maximumTabWidth ?? ReadMaximumTabWidth());
        if (textWidth <= 0) return null;

        var measure = CreateMeasurer();
        return measure == null ? null : new CaptionBudget(textWidth, measure);
    }

    /// <summary>Visual Studio's current "maximum tab width" setting.</summary>
    public int ReadMaximumTabWidth() => ReadSetting(MaximumTabWidthMoniker, DefaultMaximumTabWidth);

    /// <summary>
    /// Returns a function that measures caption text the way a tab draws it, or null when it cannot.
    /// </summary>
    public Func<string, double>? CreateMeasurer()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            // The selected tab is drawn in bold, which is wider, and any tab can become the selected one.
            var weight = ReadSetting(BoldSelectedTabsMoniker, true) ? FontWeights.Bold : FontWeights.Normal;

            var fontFamily = Application.Current?.TryFindResource(VsFonts.EnvironmentFontFamilyKey) as FontFamily
                ?? SystemFonts.MessageFontFamily;
            double fontSize = Application.Current?.TryFindResource(VsFonts.EnvironmentFontSizeKey) as double?
                ?? SystemFonts.MessageFontSize;
            var typeface = new Typeface(fontFamily, FontStyles.Normal, weight, FontStretches.Normal);

            var mainWindow = Application.Current?.MainWindow;
            double pixelsPerDip = mainWindow != null ? VisualTreeHelper.GetDpi(mainWindow).PixelsPerDip : 1.0;

            return caption => new FormattedText(
                caption,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                null,
                TextFormattingMode.Display,
                pixelsPerDip).WidthIncludingTrailingWhitespace;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShowPathOnTabs: could not measure tab captions: {ex.Message}");
            return null;
        }
    }

    /// <summary>The width left for caption text on a tab of the given maximum width.</summary>
    internal static double TextWidthFor(int maximumTabWidth) => maximumTabWidth - TabChromeWidth;

    /// <summary>The maximum tab width needed to show caption text of the given width.</summary>
    internal static int TabWidthFor(double textWidth) => (int)Math.Ceiling(textWidth + TabChromeWidth);

    private T ReadSetting<T>(string moniker, T fallback) where T : notnull
    {
        try
        {
            if (settingsManager == null) return fallback;

            var retrieval = settingsManager.GetReader().GetValue<T>(moniker);
            return retrieval.Outcome == SettingRetrievalOutcome.Success ? retrieval.Value ?? fallback : fallback;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShowPathOnTabs: could not read {moniker}: {ex.Message}");
            return fallback;
        }
    }
}

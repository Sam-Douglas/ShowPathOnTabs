using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities.UnifiedSettings;
using ShowPathOnTabs.Core.Captioning;
using System;
using System.Diagnostics;

namespace ShowPathOnTabs.Options;

/// <summary>
/// The extension's caption settings, wherever Visual Studio keeps them.
/// </summary>
internal interface ICaptionSettings
{
    CaptionOptions Current { get; }

    /// <summary>Raised on the UI thread after a setting changes, from any source.</summary>
    event EventHandler? Changed;

    void Update(CaptionOptions options);
}

/// <summary>
/// Where the settings live when Unified Settings cannot be used: the classic options page.
/// </summary>
internal interface ILegacyCaptionStore
{
    bool CollapseMiddle { get; set; }

    bool AbbreviateFolders { get; set; }

    CaptionLayout Layout { get; set; }

    /// <summary>Raised after the user confirms the classic Options dialog.</summary>
    event EventHandler? Applied;

    void Save();
}

/// <summary>
/// Reads and writes the settings declared in <c>Options\registration.json</c> through Visual Studio's
/// Unified Settings. Where Unified Settings is unavailable or switched off (Visual Studio 2022 in
/// "classic" mode), it uses the legacy options page's storage instead.
/// </summary>
internal sealed class CaptionSettings : ICaptionSettings, IDisposable
{
    public const string CollapseMiddleMoniker = "showPathOnTabs.collapseMiddle";
    public const string AbbreviateFoldersMoniker = "showPathOnTabs.abbreviateFolders";
    public const string LayoutMoniker = "showPathOnTabs.layout";

    private const string WriterName = "ShowPathOnTabs";

    private readonly ISettingsManager? _settingsManager;
    private readonly ILegacyCaptionStore _legacyPage;
    private readonly JoinableTaskFactory _joinableTaskFactory;
    private readonly IDisposable? _subscription;
    private CaptionOptions? _cached;

    /// <param name="settingsManager">The Unified Settings manager, or null when Visual Studio does not provide one.</param>
    /// <param name="legacyPage">The classic options page, used as the fallback store.</param>
    /// <param name="joinableTaskFactory">Used to return to the UI thread when a setting changes (normally the package's).</param>
    /// <param name="relatedMonikers">Other settings whose changes should also raise <see cref="Changed"/>.</param>
    public CaptionSettings(
        ISettingsManager? settingsManager,
        ILegacyCaptionStore legacyPage,
        JoinableTaskFactory joinableTaskFactory,
        params string[] relatedMonikers)
    {
        _settingsManager = settingsManager;
        _joinableTaskFactory = joinableTaskFactory ?? throw new ArgumentNullException(nameof(joinableTaskFactory));
        _legacyPage = legacyPage ?? throw new ArgumentNullException(nameof(legacyPage));
        _legacyPage.Applied += OnLegacyPageApplied;

        try
        {
            string[] monikers = [CollapseMiddleMoniker, AbbreviateFoldersMoniker, LayoutMoniker, .. relatedMonikers];
            _subscription = _settingsManager?.GetReader().SubscribeToChanges(OnUnifiedSettingsChanged, monikers);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShowPathOnTabs: could not subscribe to settings changes: {ex.Message}");
        }
    }

    public event EventHandler? Changed;

    public CaptionOptions Current => _cached ??= Read();

    public void Update(CaptionOptions options)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!TryWriteUnified(options))
        {
            _legacyPage.CollapseMiddle = options.CollapseMiddle;
            _legacyPage.AbbreviateFolders = options.AbbreviateFolders;
            _legacyPage.Layout = options.Layout;
            _legacyPage.Save();
        }

        RaiseChanged();
    }

    private CaptionOptions Read()
    {
        ISettingsReader? reader = null;
        try
        {
            reader = _settingsManager?.GetReader();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShowPathOnTabs: could not open the settings reader: {ex.Message}");
        }

        return new CaptionOptions(
            ReadBool(reader, CollapseMiddleMoniker, _legacyPage.CollapseMiddle),
            ReadBool(reader, AbbreviateFoldersMoniker, _legacyPage.AbbreviateFolders),
            ReadEnum(reader, LayoutMoniker, _legacyPage.Layout));
    }

    private static bool ReadBool(ISettingsReader? reader, string moniker, bool fallback)
    {
        if (reader == null) return fallback;

        try
        {
            var retrieval = reader.GetValue<bool>(moniker);
            return retrieval.Outcome == SettingRetrievalOutcome.Success ? retrieval.Value : fallback;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShowPathOnTabs: could not read {moniker}: {ex.Message}");
            return fallback;
        }
    }

    private static TEnum ReadEnum<TEnum>(ISettingsReader? reader, string moniker, TEnum fallback) where TEnum : struct
    {
        if (reader == null) return fallback;

        try
        {
            var retrieval = reader.GetValue<string>(moniker);

            // Setting values are camelCase ("collapseMiddle"); the enum members are PascalCase.
            if (retrieval.Outcome == SettingRetrievalOutcome.Success
                && Enum.TryParse(retrieval.Value, ignoreCase: true, out TEnum value)
                && Enum.IsDefined(typeof(TEnum), value))
            {
                return value;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShowPathOnTabs: could not read {moniker}: {ex.Message}");
        }

        return fallback;
    }

    private bool TryWriteUnified(CaptionOptions options)
    {
        if (_settingsManager == null) return false;

        try
        {
            var writer = _settingsManager.GetWriter(WriterName);

            bool queued =
                IsQueued(writer.EnqueueChange(CollapseMiddleMoniker, options.CollapseMiddle))
                && IsQueued(writer.EnqueueChange(AbbreviateFoldersMoniker, options.AbbreviateFolders))
                && IsQueued(writer.EnqueueChange(LayoutMoniker, ToSettingValue(options.Layout)));
            if (!queued) return false;

            var commit = writer.RequestCommit("Show Path On Tabs caption settings");
            return commit.Outcome != SettingCommitOutcome.InternalError;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShowPathOnTabs: could not write settings: {ex.Message}");
            return false;
        }
    }

    private static bool IsQueued(SettingChangeResult result) =>
        result.Outcome is SettingChangeOutcome.PendingCommit or SettingChangeOutcome.PendingCommitWithoutValidation;

    /// <summary>Converts an enum member to the camelCase value used in registration.json.</summary>
    internal static string ToSettingValue<TEnum>(TEnum value) where TEnum : struct
    {
        string name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private void OnLegacyPageApplied(object sender, EventArgs e) => RaiseChanged();

    private void OnUnifiedSettingsChanged(SettingsUpdate update)
    {
        // Visual Studio may raise this from a background thread.
        _ = _joinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await _joinableTaskFactory.SwitchToMainThreadAsync();
                RaiseChanged();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShowPathOnTabs: settings change was not applied: {ex.Message}");
            }
        });
    }

    private void RaiseChanged()
    {
        _cached = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _legacyPage.Applied -= OnLegacyPageApplied;
        _subscription?.Dispose();
    }
}

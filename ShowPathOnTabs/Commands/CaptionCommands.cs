using Microsoft.VisualStudio.Shell;
using ShowPathOnTabs.Core.Captioning;
using ShowPathOnTabs.Options;
using System;
using System.ComponentModel.Design;

namespace ShowPathOnTabs.Commands;

/// <summary>
/// The Extensions > Show Path On Tabs menu: an Enabled tick, two tick-able settings, an Abbreviate
/// Folders and a Widen Tabs submenu with one ticked choice each, Reset to Defaults and a link to the
/// settings page. While the extension is switched off, the caption settings are greyed out. The ids
/// match <c>ShowPathOnTabs.vsct</c>.
/// </summary>
internal sealed class CaptionCommands(ICaptionSettings settings, Action openSettings)
{
    public static readonly Guid CommandSet = new("3b1c7d0e-9f5a-4c58-8e2b-6a4d2f7c9e10");

    public const int CollapseMiddleCommandId = 0x0100;
    public const int FileNameFirstCommandId = 0x0101;
    public const int OpenSettingsCommandId = 0x0102;
    public const int EnabledCommandId = 0x0103;
    public const int ResetToDefaultsCommandId = 0x0104;

    public const int AbbreviateOffCommandId = 0x0110;
    public const int AbbreviateToFitCommandId = 0x0111;
    public const int AbbreviateAlwaysCommandId = 0x0112;

    public const int WidenOffCommandId = 0x0120;
    public const int WidenToFitCommandId = 0x0121;
    public const int WidenToLimitCommandId = 0x0122;

    private readonly ICaptionSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly Action _openSettings = openSettings ?? throw new ArgumentNullException(nameof(openSettings));

    public bool IsEnabled => _settings.Current.Enabled;

    public bool IsCollapseMiddleOn => _settings.Current.CollapseMiddle;

    public bool IsFileNameFirstOn => _settings.Current.Layout == CaptionLayout.FileNameThenPath;

    public void Register(IMenuCommandService commandService)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        AddCommand(commandService, EnabledCommandId, ToggleEnabled, () => IsEnabled, alwaysAvailable: true);
        AddCommand(commandService, CollapseMiddleCommandId, ToggleCollapseMiddle, () => IsCollapseMiddleOn);
        AddCommand(commandService, FileNameFirstCommandId, ToggleFileNameFirst, () => IsFileNameFirstOn);

        AddAbbreviationChoice(commandService, AbbreviateOffCommandId, FolderAbbreviation.Off);
        AddAbbreviationChoice(commandService, AbbreviateToFitCommandId, FolderAbbreviation.ToFit);
        AddAbbreviationChoice(commandService, AbbreviateAlwaysCommandId, FolderAbbreviation.Always);

        AddWideningChoice(commandService, WidenOffCommandId, TabWidening.Off);
        AddWideningChoice(commandService, WidenToFitCommandId, TabWidening.ToFit);
        AddWideningChoice(commandService, WidenToLimitCommandId, TabWidening.ToLimit);

        commandService.AddCommand(new MenuCommand(
            (_, _) => ResetToDefaults(),
            new CommandID(CommandSet, ResetToDefaultsCommandId)));

        commandService.AddCommand(new MenuCommand(
            (_, _) => _openSettings(),
            new CommandID(CommandSet, OpenSettingsCommandId)));
    }

    public void ToggleEnabled()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _settings.Update(_settings.Current.With(enabled: !IsEnabled));
    }

    /// <summary>Puts every setting back to its default, which also switches the extension on.</summary>
    public void ResetToDefaults()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _settings.Update(CaptionOptions.Default);
    }

    public void ToggleCollapseMiddle()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _settings.Update(_settings.Current.With(collapseMiddle: !IsCollapseMiddleOn));
    }

    public void ToggleFileNameFirst()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var layout = IsFileNameFirstOn ? CaptionLayout.PathThenFileName : CaptionLayout.FileNameThenPath;
        _settings.Update(_settings.Current.With(layout: layout));
    }

    public void SetFolderAbbreviation(FolderAbbreviation value)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _settings.Update(_settings.Current.With(abbreviateFolders: value));
    }

    public void SetTabWidening(TabWidening value)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _settings.Update(_settings.Current.With(widenTabs: value));
    }

    private void AddAbbreviationChoice(IMenuCommandService commandService, int commandId, FolderAbbreviation value)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        AddCommand(commandService, commandId, () =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetFolderAbbreviation(value);
        }, () => _settings.Current.AbbreviateFolders == value);
    }

    private void AddWideningChoice(IMenuCommandService commandService, int commandId, TabWidening value)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        AddCommand(commandService, commandId, () =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetTabWidening(value);
        }, () => _settings.Current.WidenTabs == value);
    }

    private void AddCommand(IMenuCommandService commandService, int commandId, Action invoke, Func<bool> isChecked, bool alwaysAvailable = false)
    {
        var command = new OleMenuCommand((_, _) => invoke(), new CommandID(CommandSet, commandId));

        // Runs each time the menu opens, so the tick follows changes made on the settings page.
        command.BeforeQueryStatus += (_, _) =>
        {
            command.Checked = isChecked();
            command.Enabled = alwaysAvailable || IsEnabled;
        };

        commandService.AddCommand(command);
    }
}

using Microsoft.VisualStudio.Shell;
using ShowPathOnTabs.Core.Captioning;
using ShowPathOnTabs.Options;
using System;
using System.ComponentModel.Design;

namespace ShowPathOnTabs.Commands;

/// <summary>
/// The Extensions > Show Path On Tabs menu: three independent tick-able settings and a link to the
/// settings page. The ids match <c>ShowPathOnTabs.vsct</c>.
/// </summary>
internal sealed class CaptionCommands(ICaptionSettings settings, Action openSettings)
{
    public static readonly Guid CommandSet = new("3b1c7d0e-9f5a-4c58-8e2b-6a4d2f7c9e10");

    public const int CollapseMiddleCommandId = 0x0100;
    public const int FileNameFirstCommandId = 0x0101;
    public const int OpenSettingsCommandId = 0x0102;
    public const int AbbreviateFoldersCommandId = 0x0104;

    private readonly ICaptionSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly Action _openSettings = openSettings ?? throw new ArgumentNullException(nameof(openSettings));

    public bool IsCollapseMiddleOn => _settings.Current.CollapseMiddle;

    public bool IsAbbreviateFoldersOn => _settings.Current.AbbreviateFolders;

    public bool IsFileNameFirstOn => _settings.Current.Layout == CaptionLayout.FileNameThenPath;

    public void Register(IMenuCommandService commandService)
    {
        AddToggle(commandService, CollapseMiddleCommandId, ToggleCollapseMiddle, () => IsCollapseMiddleOn);
        AddToggle(commandService, AbbreviateFoldersCommandId, ToggleAbbreviateFolders, () => IsAbbreviateFoldersOn);
        AddToggle(commandService, FileNameFirstCommandId, ToggleFileNameFirst, () => IsFileNameFirstOn);

        commandService.AddCommand(new MenuCommand(
            (_, _) => _openSettings(),
            new CommandID(CommandSet, OpenSettingsCommandId)));
    }

    public void ToggleCollapseMiddle()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _settings.Update(_settings.Current.With(collapseMiddle: !IsCollapseMiddleOn));
    }

    public void ToggleAbbreviateFolders()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _settings.Update(_settings.Current.With(abbreviateFolders: !IsAbbreviateFoldersOn));
    }

    public void ToggleFileNameFirst()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var layout = IsFileNameFirstOn ? CaptionLayout.PathThenFileName : CaptionLayout.FileNameThenPath;
        _settings.Update(_settings.Current.With(layout: layout));
    }

    private static void AddToggle(IMenuCommandService commandService, int commandId, Action toggle, Func<bool> isOn)
    {
        var command = new OleMenuCommand((_, _) => toggle(), new CommandID(CommandSet, commandId));

        // Runs each time the menu opens, so the tick follows changes made on the settings page.
        command.BeforeQueryStatus += (_, _) => command.Checked = isOn();

        commandService.AddCommand(command);
    }
}

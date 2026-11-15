using Microsoft.VisualStudio.Sdk.TestFramework;
using Microsoft.VisualStudio.Shell;
using ShowPathOnTabs.Commands;
using ShowPathOnTabs.Core.Captioning;
using ShowPathOnTabs.Options;
using System.ComponentModel.Design;

namespace ShowPathOnTabs.VsTests.Commands;

[Collection(MockedVsCollection.Name)]
public sealed class CaptionCommandsTests
{
    private sealed class FakeCaptionSettings : ICaptionSettings
    {
        public CaptionOptions Current { get; private set; } = CaptionOptions.Default;

        public event EventHandler? Changed;

        public void Update(CaptionOptions options)
        {
            Current = options;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private readonly FakeCaptionSettings _settings = new();
    private int _settingsOpened;

    public CaptionCommandsTests(GlobalServiceProvider globalServiceProvider)
    {
        globalServiceProvider.Reset();
    }

    private CaptionCommands CreateCommands() => new(_settings, () => _settingsOpened++);

    [Fact]
    public async Task ToggleCollapseMiddle_SwitchesOnAndOff()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commands = CreateCommands();

        commands.ToggleCollapseMiddle();
        Assert.True(commands.IsCollapseMiddleOn);

        commands.ToggleCollapseMiddle();
        Assert.False(commands.IsCollapseMiddleOn);
    }

    [Fact]
    public async Task ToggleAbbreviateFolders_SwitchesOnAndOff()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commands = CreateCommands();

        commands.ToggleAbbreviateFolders();
        Assert.True(commands.IsAbbreviateFoldersOn);

        commands.ToggleAbbreviateFolders();
        Assert.False(commands.IsAbbreviateFoldersOn);
    }

    [Fact]
    public async Task Toggles_AreIndependentAndKeepTheOtherSettings()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commands = CreateCommands();

        commands.ToggleCollapseMiddle();
        commands.ToggleAbbreviateFolders();
        commands.ToggleFileNameFirst();

        Assert.True(commands.IsCollapseMiddleOn);
        Assert.True(commands.IsAbbreviateFoldersOn);
        Assert.True(commands.IsFileNameFirstOn);

        commands.ToggleFileNameFirst();

        Assert.True(commands.IsCollapseMiddleOn);
        Assert.True(commands.IsAbbreviateFoldersOn);
        Assert.False(commands.IsFileNameFirstOn);
    }

    [Fact]
    public async Task Register_AddsCommandsThatToggleAndShowTicks()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = new MenuCommandService(new ServiceContainer());
        CreateCommands().Register(commandService);
        OleMenuCommand Find(int id) => (OleMenuCommand)commandService.FindCommand(new CommandID(CaptionCommands.CommandSet, id))!;
        var collapse = Find(CaptionCommands.CollapseMiddleCommandId);
        var fit = Find(CaptionCommands.AbbreviateFoldersCommandId);
        var fileNameFirst = Find(CaptionCommands.FileNameFirstCommandId);
        var settings = commandService.FindCommand(new CommandID(CaptionCommands.CommandSet, CaptionCommands.OpenSettingsCommandId))!;

        collapse.Invoke();
        fit.Invoke();
        settings.Invoke();

        // Visual Studio asks for the status each time the menu opens.
        _ = collapse.OleStatus;
        _ = fit.OleStatus;
        _ = fileNameFirst.OleStatus;

        Assert.True(collapse.Checked);
        Assert.True(fit.Checked);
        Assert.False(fileNameFirst.Checked);
        Assert.Equal(1, _settingsOpened);
    }
}

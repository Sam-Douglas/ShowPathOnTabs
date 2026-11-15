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
        // Start with every toggle off so each test sees a clear off-to-on change.
        public CaptionOptions Current { get; private set; } = new(collapseMiddle: false, FolderAbbreviation.Off, widenTabs: TabWidening.Off);

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
    public async Task SetFolderAbbreviation_ChangesOnlyThatSetting()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commands = CreateCommands();

        commands.SetFolderAbbreviation(FolderAbbreviation.Always);

        Assert.Equal(FolderAbbreviation.Always, _settings.Current.AbbreviateFolders);
        Assert.Equal(TabWidening.Off, _settings.Current.WidenTabs);
        Assert.False(commands.IsCollapseMiddleOn);
    }

    [Fact]
    public async Task SetTabWidening_ChangesOnlyThatSetting()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commands = CreateCommands();

        commands.SetTabWidening(TabWidening.ToLimit);

        Assert.Equal(TabWidening.ToLimit, _settings.Current.WidenTabs);
        Assert.Equal(FolderAbbreviation.Off, _settings.Current.AbbreviateFolders);
    }

    [Fact]
    public async Task Toggles_AreIndependentAndKeepTheOtherSettings()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commands = CreateCommands();

        commands.ToggleCollapseMiddle();
        commands.SetFolderAbbreviation(FolderAbbreviation.ToFit);
        commands.ToggleFileNameFirst();

        Assert.True(commands.IsCollapseMiddleOn);
        Assert.Equal(FolderAbbreviation.ToFit, _settings.Current.AbbreviateFolders);
        Assert.True(commands.IsFileNameFirstOn);

        commands.ToggleFileNameFirst();

        Assert.True(commands.IsCollapseMiddleOn);
        Assert.Equal(FolderAbbreviation.ToFit, _settings.Current.AbbreviateFolders);
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
        var fileNameFirst = Find(CaptionCommands.FileNameFirstCommandId);
        var settings = commandService.FindCommand(new CommandID(CaptionCommands.CommandSet, CaptionCommands.OpenSettingsCommandId))!;

        collapse.Invoke();
        settings.Invoke();

        // Visual Studio asks for the status each time the menu opens.
        _ = collapse.OleStatus;
        _ = fileNameFirst.OleStatus;

        Assert.True(collapse.Checked);
        Assert.False(fileNameFirst.Checked);
        Assert.Equal(1, _settingsOpened);
    }

    [Theory]
    [InlineData(CaptionCommands.AbbreviateOffCommandId, FolderAbbreviation.Off)]
    [InlineData(CaptionCommands.AbbreviateToFitCommandId, FolderAbbreviation.ToFit)]
    [InlineData(CaptionCommands.AbbreviateAlwaysCommandId, FolderAbbreviation.Always)]
    public async Task AbbreviateSubmenu_SelectsAndTicksOneChoice(int commandId, FolderAbbreviation expected)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _settings.Update(_settings.Current.With(abbreviateFolders: FolderAbbreviation.ToFit));
        var commandService = new MenuCommandService(new ServiceContainer());
        CreateCommands().Register(commandService);
        int[] choices = [CaptionCommands.AbbreviateOffCommandId, CaptionCommands.AbbreviateToFitCommandId, CaptionCommands.AbbreviateAlwaysCommandId];

        ((OleMenuCommand)commandService.FindCommand(new CommandID(CaptionCommands.CommandSet, commandId))!).Invoke();

        Assert.Equal(expected, _settings.Current.AbbreviateFolders);
        Assert.Equal([commandId], TickedCommands(commandService, choices));
    }

    [Theory]
    [InlineData(CaptionCommands.WidenOffCommandId, TabWidening.Off)]
    [InlineData(CaptionCommands.WidenToFitCommandId, TabWidening.ToFit)]
    [InlineData(CaptionCommands.WidenToLimitCommandId, TabWidening.ToLimit)]
    public async Task WidenSubmenu_SelectsAndTicksOneChoice(int commandId, TabWidening expected)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _settings.Update(_settings.Current.With(widenTabs: TabWidening.ToFit));
        var commandService = new MenuCommandService(new ServiceContainer());
        CreateCommands().Register(commandService);
        int[] choices = [CaptionCommands.WidenOffCommandId, CaptionCommands.WidenToFitCommandId, CaptionCommands.WidenToLimitCommandId];

        ((OleMenuCommand)commandService.FindCommand(new CommandID(CaptionCommands.CommandSet, commandId))!).Invoke();

        Assert.Equal(expected, _settings.Current.WidenTabs);
        Assert.Equal([commandId], TickedCommands(commandService, choices));
    }

    private static int[] TickedCommands(MenuCommandService commandService, int[] commandIds) =>
        commandIds
            .Where(id =>
            {
                var command = (OleMenuCommand)commandService.FindCommand(new CommandID(CaptionCommands.CommandSet, id))!;
                _ = command.OleStatus; // Visual Studio asks for the status each time the menu opens.
                return command.Checked;
            })
            .ToArray();
}

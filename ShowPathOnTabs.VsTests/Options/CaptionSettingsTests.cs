using Microsoft.VisualStudio.Sdk.TestFramework;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Utilities.UnifiedSettings;
using Moq;
using ShowPathOnTabs.Core.Captioning;
using ShowPathOnTabs.Options;

namespace ShowPathOnTabs.VsTests.Options;

[Collection(MockedVsCollection.Name)]
public sealed class CaptionSettingsTests
{
    private sealed class FakeLegacyStore : ILegacyCaptionStore
    {
        public bool CollapseMiddle { get; set; }

        public FolderAbbreviation FolderAbbreviation { get; set; }

        public CaptionLayout Layout { get; set; }

        public TabWidening TabWidening { get; set; }

        public int TabWidthLimit { get; set; }

        public int SaveCount { get; private set; }

        public event EventHandler? Applied;

        public void Save() => SaveCount++;

        public void RaiseApplied() => Applied?.Invoke(this, EventArgs.Empty);
    }

    private readonly Mock<ISettingsManager> _manager = new();
    private readonly Mock<ISettingsWriter> _writer = new();
    private readonly Mock<ISettingsReader> _reader = new();
    private readonly FakeLegacyStore _legacyPage = new();
    private Action<SettingsUpdate>? _changeHandler;

    public CaptionSettingsTests(GlobalServiceProvider globalServiceProvider)
    {
        globalServiceProvider.Reset();

        _manager.Setup(m => m.GetReader()).Returns(_reader.Object);
        _manager.Setup(m => m.GetWriter(It.IsAny<string>())).Returns(_writer.Object);
        _reader
            .Setup(r => r.SubscribeToChanges(It.IsAny<Action<SettingsUpdate>>(), It.IsAny<string[]>()))
            .Callback((Action<SettingsUpdate> handler, string[] _) => _changeHandler = handler)
            .Returns(Mock.Of<IDisposable>());

        StoreValue(CaptionSettings.CollapseMiddleMoniker, SettingRetrievalOutcome.Success, false);
        StoreValue(CaptionSettings.FolderAbbreviationMoniker, SettingRetrievalOutcome.Success, "off");
        StoreValue(CaptionSettings.LayoutMoniker, SettingRetrievalOutcome.Success, "pathThenFileName");
        StoreValue(CaptionSettings.TabWideningMoniker, SettingRetrievalOutcome.Success, "toFit");
        StoreValue(CaptionSettings.TabWidthLimitMoniker, SettingRetrievalOutcome.Success, 300);

        var pending = new SettingChangeResult(SettingChangeOutcome.PendingCommit, true, "");
        _writer.Setup(w => w.EnqueueChange(It.IsAny<string>(), It.IsAny<string>())).Returns(pending);
        _writer.Setup(w => w.EnqueueChange(It.IsAny<string>(), It.IsAny<bool>())).Returns(pending);
        _writer.Setup(w => w.EnqueueChange(It.IsAny<string>(), It.IsAny<int>())).Returns(pending);
        _writer
            .Setup(w => w.RequestCommit(It.IsAny<string>()))
            .Returns(new SettingCommitResult(SettingCommitOutcome.Success, ""));
    }

    private void StoreValue<T>(string moniker, SettingRetrievalOutcome outcome, T value) where T : notnull =>
        _reader
            .Setup(r => r.GetValue<T>(moniker, It.IsAny<SettingReadOptions>()))
            .Returns(new SettingRetrieval<T>(outcome, "", value));

    private CaptionSettings CreateSettings(bool withUnifiedSettings = true) =>
        new(withUnifiedSettings ? _manager.Object : null, _legacyPage, ThreadHelper.JoinableTaskFactory);

    [Fact]
    public void Current_ReadsUnifiedSettings()
    {
        StoreValue(CaptionSettings.CollapseMiddleMoniker, SettingRetrievalOutcome.Success, true);
        StoreValue(CaptionSettings.FolderAbbreviationMoniker, SettingRetrievalOutcome.Success, "always");
        StoreValue(CaptionSettings.LayoutMoniker, SettingRetrievalOutcome.Success, "fileNameThenPath");
        StoreValue(CaptionSettings.TabWideningMoniker, SettingRetrievalOutcome.Success, "toLimit");
        StoreValue(CaptionSettings.TabWidthLimitMoniker, SettingRetrievalOutcome.Success, 640);

        using var settings = CreateSettings();

        Assert.True(settings.Current.CollapseMiddle);
        Assert.Equal(FolderAbbreviation.Always, settings.Current.AbbreviateFolders);
        Assert.Equal(CaptionLayout.FileNameThenPath, settings.Current.Layout);
        Assert.Equal(TabWidening.ToLimit, settings.Current.WidenTabs);
        Assert.Equal(640, settings.Current.TabWidthLimit);
    }

    [Fact]
    public void Current_WithoutUnifiedSettings_UsesLegacyPage()
    {
        _legacyPage.CollapseMiddle = true;
        _legacyPage.FolderAbbreviation = FolderAbbreviation.ToFit;
        _legacyPage.Layout = CaptionLayout.FileNameThenPath;
        _legacyPage.TabWidening = TabWidening.ToLimit;
        _legacyPage.TabWidthLimit = 320;

        using var settings = CreateSettings(withUnifiedSettings: false);

        Assert.True(settings.Current.CollapseMiddle);
        Assert.Equal(FolderAbbreviation.ToFit, settings.Current.AbbreviateFolders);
        Assert.Equal(CaptionLayout.FileNameThenPath, settings.Current.Layout);
        Assert.Equal(TabWidening.ToLimit, settings.Current.WidenTabs);
        Assert.Equal(320, settings.Current.TabWidthLimit);
    }

    [Fact]
    public void Current_InClassicMode_UsesLegacyPage()
    {
        StoreValue(CaptionSettings.CollapseMiddleMoniker, SettingRetrievalOutcome.NotSupportedInClassicMode, false);
        _legacyPage.CollapseMiddle = true;

        using var settings = CreateSettings();

        Assert.True(settings.Current.CollapseMiddle);
    }

    [Fact]
    public void Current_UnknownStoredValue_UsesLegacyPage()
    {
        StoreValue(CaptionSettings.LayoutMoniker, SettingRetrievalOutcome.Success, "somethingElse");

        using var settings = CreateSettings();

        Assert.Equal(CaptionLayout.PathThenFileName, settings.Current.Layout);
    }

    [Fact]
    public async Task Update_WritesUnifiedSettingsAndRaisesChanged()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        using var settings = CreateSettings();
        int changes = 0;
        settings.Changed += (_, _) => changes++;

        settings.Update(new CaptionOptions(collapseMiddle: false, FolderAbbreviation.ToFit, CaptionLayout.FileNameThenPath, TabWidening.ToLimit, tabWidthLimit: 640));

        _writer.Verify(w => w.EnqueueChange(CaptionSettings.CollapseMiddleMoniker, false), Times.Once);
        _writer.Verify(w => w.EnqueueChange(CaptionSettings.FolderAbbreviationMoniker, "toFit"), Times.Once);
        _writer.Verify(w => w.EnqueueChange(CaptionSettings.LayoutMoniker, "fileNameThenPath"), Times.Once);
        _writer.Verify(w => w.EnqueueChange(CaptionSettings.TabWideningMoniker, "toLimit"), Times.Once);
        _writer.Verify(w => w.EnqueueChange(CaptionSettings.TabWidthLimitMoniker, 640), Times.Once);
        _writer.Verify(w => w.RequestCommit(It.IsAny<string>()), Times.Once);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Update_WhenUnifiedSettingsRejectsTheWrite_SavesToLegacyPage()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _writer
            .Setup(w => w.EnqueueChange(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(new SettingChangeResult(SettingChangeOutcome.NotSupportedInClassicMode, false, ""));
        using var settings = CreateSettings();

        settings.Update(new CaptionOptions(collapseMiddle: true, FolderAbbreviation.Always, CaptionLayout.FileNameThenPath, TabWidening.ToLimit, tabWidthLimit: 320));

        Assert.Equal(TabWidening.ToLimit, _legacyPage.TabWidening);
        Assert.Equal(320, _legacyPage.TabWidthLimit);
        Assert.True(_legacyPage.CollapseMiddle);
        Assert.Equal(FolderAbbreviation.Always, _legacyPage.FolderAbbreviation);
        Assert.Equal(CaptionLayout.FileNameThenPath, _legacyPage.Layout);
        Assert.Equal(1, _legacyPage.SaveCount);
        _writer.Verify(w => w.RequestCommit(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UnifiedSettingsChange_RaisesChangedAndRereads()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        using var settings = CreateSettings();
        Assert.Equal(FolderAbbreviation.Off, settings.Current.AbbreviateFolders);
        var changed = new TaskCompletionSource<bool>();
        settings.Changed += (_, _) => changed.TrySetResult(true);

        StoreValue(CaptionSettings.FolderAbbreviationMoniker, SettingRetrievalOutcome.Success, "toFit");
        _changeHandler!(new SettingsUpdate(Guid.Empty, [CaptionSettings.FolderAbbreviationMoniker]));
        await changed.Task;

        Assert.Equal(FolderAbbreviation.ToFit, settings.Current.AbbreviateFolders);
    }

    [Fact]
    public void LegacyPageApplied_RaisesChangedAndRereads()
    {
        using var settings = CreateSettings(withUnifiedSettings: false);
        Assert.False(settings.Current.CollapseMiddle);
        int changes = 0;
        settings.Changed += (_, _) => changes++;

        _legacyPage.CollapseMiddle = true;
        _legacyPage.RaiseApplied();

        Assert.Equal(1, changes);
        Assert.True(settings.Current.CollapseMiddle);
    }

    [Fact]
    public void SettingNames_MatchRegistrationJson()
    {
        string registration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Options", "registration.json"));

        var values = Enum.GetValues(typeof(CaptionLayout)).Cast<CaptionLayout>().Select(CaptionSettings.ToSettingValue)
            .Concat(Enum.GetValues(typeof(FolderAbbreviation)).Cast<FolderAbbreviation>().Select(CaptionSettings.ToSettingValue))
            .Concat(Enum.GetValues(typeof(TabWidening)).Cast<TabWidening>().Select(CaptionSettings.ToSettingValue));
        foreach (var value in values)
        {
            Assert.Contains($"\"{value}\"", registration);
        }

        Assert.Contains($"\"{CaptionSettings.CollapseMiddleMoniker}\"", registration);
        Assert.Contains($"\"{CaptionSettings.FolderAbbreviationMoniker}\"", registration);
        Assert.Contains($"\"{CaptionSettings.LayoutMoniker}\"", registration);
        Assert.Contains($"\"{CaptionSettings.TabWideningMoniker}\"", registration);
        Assert.Contains($"\"{CaptionSettings.TabWidthLimitMoniker}\"", registration);
    }
}

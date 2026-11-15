using Microsoft.VisualStudio.Sdk.TestFramework;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Utilities.UnifiedSettings;
using Moq;
using ShowPathOnTabs.Core.Captioning;
using ShowPathOnTabs.Services;

namespace ShowPathOnTabs.VsTests.Services;

[Collection(MockedVsCollection.Name)]
public sealed class TabWidenerTests
{
    private const string Moniker = TabCaptionBudgetProvider.MaximumTabWidthMoniker;

    private readonly Mock<ISettingsManager> _manager = new();
    private readonly Mock<ISettingsWriter> _writer = new();
    private readonly Mock<ISettingsReader> _reader = new();
    private readonly List<int> _writes = [];
    private int _currentWidth = TabWidener.RestoredTabWidth;

    public TabWidenerTests(GlobalServiceProvider globalServiceProvider)
    {
        globalServiceProvider.Reset();

        _manager.Setup(m => m.GetReader()).Returns(_reader.Object);
        _manager.Setup(m => m.GetWriter(It.IsAny<string>())).Returns(_writer.Object);
        _reader
            .Setup(r => r.GetValue<int>(Moniker, It.IsAny<SettingReadOptions>()))
            .Returns(() => new SettingRetrieval<int>(SettingRetrievalOutcome.Success, "", _currentWidth));
        _writer
            .Setup(w => w.EnqueueChange(Moniker, It.IsAny<int>()))
            .Callback((string _, int width) => _writes.Add(width))
            .Returns(new SettingChangeResult(SettingChangeOutcome.PendingCommit, true, ""));
        _writer
            .Setup(w => w.RequestCommit(It.IsAny<string>()))
            .Callback(() => _currentWidth = _writes[_writes.Count - 1])
            .Returns(new SettingCommitResult(SettingCommitOutcome.Success, ""));
    }

    // One unit per character keeps the expected widths easy to work out.
    private TabWidener CreateWidener() => new(_manager.Object, () => caption => caption.Length);

    private const int Limit = 400;

    private static string CaptionNeeding(int tabWidth) =>
        new('x', tabWidth - (int)TabCaptionBudgetProvider.TabChromeWidth);

    [Fact]
    public async Task ToFit_SetsWidthToFitTheWidestCaption()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        using var widener = CreateWidener();

        widener.Apply(TabWidening.ToFit, Limit, ["Short.cs", CaptionNeeding(300), CaptionNeeding(260)]);

        Assert.Equal([300], _writes);
    }

    [Fact]
    public async Task ToFit_IgnoresTheLimit()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        using var widener = CreateWidener();

        widener.Apply(TabWidening.ToFit, Limit, [CaptionNeeding(700)]);

        Assert.Equal([700], _writes);
    }

    [Fact]
    public async Task ToFit_StopsAtVisualStudiosMaximum()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        using var widener = CreateWidener();

        widener.Apply(TabWidening.ToFit, Limit, [CaptionNeeding(1500)]);

        Assert.Equal([TabWidener.VsMaximumTabWidth], _writes);
    }

    [Fact]
    public async Task ToLimit_FitsTheWidestCaptionWhenItIsUnderTheLimit()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        using var widener = CreateWidener();

        widener.Apply(TabWidening.ToLimit, Limit, [CaptionNeeding(300)]);

        Assert.Equal([300], _writes);
    }

    [Fact]
    public async Task ToLimit_StopsAtTheLimit()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        using var widener = CreateWidener();

        widener.Apply(TabWidening.ToLimit, Limit, [CaptionNeeding(700)]);

        Assert.Equal([Limit], _writes);
    }

    [Theory]
    [InlineData(100, TabWidener.RestoredTabWidth)]
    [InlineData(5000, TabWidener.VsMaximumTabWidth)]
    public async Task ToLimit_KeepsAnOutOfRangeLimitWithinVisualStudiosRange(int limit, int expected)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _currentWidth = 300;
        using var widener = CreateWidener();

        widener.Apply(TabWidening.ToLimit, limit, [CaptionNeeding(1500)]);

        Assert.Equal([expected], _writes);
    }

    [Fact]
    public async Task ChangingTheLimit_ResizesStraightAway()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        using var widener = CreateWidener();
        widener.Apply(TabWidening.ToLimit, Limit, [CaptionNeeding(700)]);

        widener.Apply(TabWidening.ToLimit, 600, [CaptionNeeding(700)]);

        Assert.Equal([Limit, 600], _writes);
    }

    [Theory]
    [InlineData(TabWidening.ToFit)]
    [InlineData(TabWidening.ToLimit)]
    public async Task ShortCaptions_UseTheDefaultWidth(TabWidening mode)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _currentWidth = 350;
        using var widener = CreateWidener();

        widener.Apply(mode, Limit, ["Short.cs"]);

        Assert.Equal([TabWidener.RestoredTabWidth], _writes);
    }

    [Fact]
    public async Task WhenTheWidthAlreadyFits_WritesNothing()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _currentWidth = 300;
        using var widener = CreateWidener();

        widener.Apply(TabWidening.ToFit, Limit, [CaptionNeeding(300)]);

        Assert.Empty(_writes);
    }

    [Fact]
    public async Task WhenCaptionsCannotBeMeasured_UsesTheDefaultWidth()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _currentWidth = 300;
        using var widener = new TabWidener(_manager.Object, () => null);

        widener.Apply(TabWidening.ToFit, Limit, [CaptionNeeding(400)]);

        Assert.Equal([TabWidener.RestoredTabWidth], _writes);
    }

    [Fact]
    public async Task TurningOff_RestoresTheDefaultWidthOnce()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        using var widener = CreateWidener();
        widener.Apply(TabWidening.ToFit, Limit, [CaptionNeeding(300)]);

        widener.Apply(TabWidening.Off, Limit, [CaptionNeeding(300)]);
        Assert.Equal([300, TabWidener.RestoredTabWidth], _writes);

        // A width the user sets afterwards is left alone.
        _currentWidth = 350;
        widener.Apply(TabWidening.Off, Limit, [CaptionNeeding(300)]);
        Assert.Equal([300, TabWidener.RestoredTabWidth], _writes);
    }

    [Fact]
    public async Task OffFromTheStart_LeavesTheWidthAlone()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _currentWidth = 350;
        using var widener = CreateWidener();

        widener.Apply(TabWidening.Off, Limit, [CaptionNeeding(400)]);

        Assert.Empty(_writes);
    }

    [Fact]
    public async Task Dispose_WhileOn_RestoresTheDefaultWidth()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var widener = CreateWidener();
        widener.Apply(TabWidening.ToLimit, Limit, [CaptionNeeding(300)]);

        widener.Dispose();

        Assert.Equal([300, TabWidener.RestoredTabWidth], _writes);
    }

    [Fact]
    public async Task WithoutSettingsManager_DoesNothing()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        using var widener = new TabWidener(null, () => caption => caption.Length);

        widener.Apply(TabWidening.ToFit, Limit, [CaptionNeeding(300)]);
        widener.Apply(TabWidening.Off, Limit, []);

        Assert.Empty(_writes);
    }
}

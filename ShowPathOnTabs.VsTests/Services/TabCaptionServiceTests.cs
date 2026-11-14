using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Sdk.TestFramework;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Moq;
using ShowPathOnTabs.Services;
using ShowPathOnTabs.VsTests.Fakes;

namespace ShowPathOnTabs.VsTests.Services;

[Collection(MockedVsCollection.Name)]
public sealed class TabCaptionServiceTests : IDisposable
{
    private readonly FakeVsEnvironment _vs = new();

    public TabCaptionServiceTests(GlobalServiceProvider globalServiceProvider)
    {
        globalServiceProvider.Reset();
    }

    public void Dispose() => _vs.Dispose();

    private async Task<TabCaptionService> CreateInitializedServiceAsync()
    {
        var service = new TabCaptionService(_vs.ServiceProvider.Object);
        await service.InitializeAsync();
        return service;
    }

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TabCaptionService(null!));
    }

    [Fact]
    public async Task Initialize_SubscribesToRunningDocumentTableEvents()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        using var service = await CreateInitializedServiceAsync();

        Assert.Same(service, _vs.AdvisedSink);
    }

    [Fact]
    public async Task Initialize_SetsCaptionsForTabsAlreadyOpen()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var first = _vs.OpenFile(@"src\A\Program.cs");
        var second = _vs.OpenFile(@"src\B\Program.cs");
        var unique = _vs.OpenFile(@"src\A\Startup.cs");

        using var service = await CreateInitializedServiceAsync();

        Assert.Equal(@"A\Program.cs", first.Caption);
        Assert.Equal(@"B\Program.cs", second.Caption);
        Assert.Equal("Startup.cs", unique.Caption);
    }

    [Fact]
    public async Task Initialize_WithoutRunningDocumentTable_DoesNothing()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _vs.ServiceProvider
            .Setup(p => p.GetServiceAsync(typeof(SVsRunningDocumentTable)))
            .ReturnsAsync((object)null!);
        var frame = _vs.OpenFile(@"src\Program.cs");

        using var service = await CreateInitializedServiceAsync();

        Assert.Null(_vs.AdvisedSink);
        Assert.Null(frame.Caption);
    }

    [Fact]
    public async Task NonFileTabs_AreNotModified()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var missingFile = _vs.OpenMoniker(@"C:\does\not\exist\Program.cs");
        var noMoniker = _vs.OpenMoniker(null);
        var nonStringMoniker = _vs.OpenMoniker(123);
        var file = _vs.OpenFile(@"src\Program.cs");

        using var service = await CreateInitializedServiceAsync();

        Assert.Null(missingFile.Caption);
        Assert.Null(noMoniker.Caption);
        Assert.Null(nonStringMoniker.Caption);
        Assert.Equal("Program.cs", file.Caption);
    }

    [Fact]
    public async Task OpeningDuplicateFile_UpdatesCaptionsOfBothTabs()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var first = _vs.OpenFile(@"src\A\Program.cs");
        using var service = await CreateInitializedServiceAsync();
        Assert.Equal("Program.cs", first.Caption);

        var second = _vs.OpenFile(@"src\B\Program.cs");
        service.OnBeforeDocumentWindowShow(docCookie: 1, fFirstShow: 1, second.Object);

        Assert.Equal(@"A\Program.cs", first.Caption);
        Assert.Equal(@"B\Program.cs", second.Caption);
    }

    [Fact]
    public async Task SwitchingToAlreadyOpenTab_DoesNotRefreshCaptions()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var frame = _vs.OpenFile(@"src\Program.cs");
        using var service = await CreateInitializedServiceAsync();
        int setCountAfterInit = frame.CaptionSetCount;

        service.OnBeforeDocumentWindowShow(docCookie: 1, fFirstShow: 0, frame.Object);

        Assert.Equal(setCountAfterInit, frame.CaptionSetCount);
    }

    [Fact]
    public async Task ClosingDuplicate_RestoresPlainCaption()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var first = _vs.OpenFile(@"src\A\Program.cs");
        var second = _vs.OpenFile(@"src\B\Program.cs");
        using var service = await CreateInitializedServiceAsync();

        _vs.Close(second);
        service.OnAfterDocumentWindowHide(docCookie: 2, second.Object);

        Assert.Equal("Program.cs", first.Caption);
    }

    [Fact]
    public async Task LastDocumentUnlock_RefreshesCaptions()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var first = _vs.OpenFile(@"src\A\Program.cs");
        var second = _vs.OpenFile(@"src\B\Program.cs");
        using var service = await CreateInitializedServiceAsync();

        _vs.Close(second);
        service.OnBeforeLastDocumentUnlock(docCookie: 2, dwRDTLockType: 0, dwReadLocksRemaining: 0, dwEditLocksRemaining: 0);

        Assert.Equal("Program.cs", first.Caption);
    }

    [Fact]
    public async Task DeeplyNestedDuplicates_UseMinimumUniquePrefix()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var first = _vs.OpenFile(@"src\Web\Pages\Index.razor");
        var second = _vs.OpenFile(@"src\Admin\Pages\Index.razor");

        using var service = await CreateInitializedServiceAsync();

        Assert.Equal(@"Web\Pages\Index.razor", first.Caption);
        Assert.Equal(@"Admin\Pages\Index.razor", second.Caption);
    }

    [Fact]
    public async Task FrameThatThrows_DoesNotEscapeEventHandler()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var frame = _vs.OpenFile(@"src\Program.cs");
        frame.Mock
            .Setup(f => f.SetProperty((int)__VSFPROPID.VSFPROPID_OwnerCaption, It.IsAny<object>()))
            .Throws(new InvalidOperationException("frame is closing"));
        using var service = await CreateInitializedServiceAsync();

        int hr = service.OnBeforeDocumentWindowShow(docCookie: 1, fFirstShow: 1, frame.Object);

        Assert.Equal(VSConstants.S_OK, hr);
    }

    [Fact]
    public async Task UnusedEvents_ReturnOkWithoutRefreshing()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var frame = _vs.OpenFile(@"src\Program.cs");
        using var service = await CreateInitializedServiceAsync();
        int setCountAfterInit = frame.CaptionSetCount;

        Assert.Equal(VSConstants.S_OK, service.OnAfterFirstDocumentLock(1, 0, 0, 0));
        Assert.Equal(VSConstants.S_OK, service.OnAfterSave(1));
        Assert.Equal(VSConstants.S_OK, service.OnAfterAttributeChange(1, 0));
        Assert.Equal(setCountAfterInit, frame.CaptionSetCount);
    }

    [Fact]
    public async Task Dispose_UnsubscribesOnce()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var service = await CreateInitializedServiceAsync();

        service.Dispose();
        service.Dispose();

        _vs.RunningDocTable.Verify(r => r.UnadviseRunningDocTableEvents(FakeVsEnvironment.RdtCookie), Times.Once);
    }

    [Fact]
    public async Task EventHandlers_RequireUIThread()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        using var service = await CreateInitializedServiceAsync();

        await TaskScheduler.Default;

        Assert.ThrowsAny<Exception>(() => service.OnAfterDocumentWindowHide(1, Mock.Of<IVsWindowFrame>()));
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
    }
}

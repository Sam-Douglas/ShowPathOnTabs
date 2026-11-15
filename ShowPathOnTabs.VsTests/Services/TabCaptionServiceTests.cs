using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Sdk.TestFramework;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Moq;
using ShowPathOnTabs.Core.Captioning;
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

    private const uint RdtaMkDocument = (uint)__VSRDTATTRIB.RDTA_MkDocument;

    private static int RaiseRenameEx(TabCaptionService service, (string OldPath, string NewPath) rename) =>
        service.OnAfterAttributeChangeEx(docCookie: 2, RdtaMkDocument, null!, 0, rename.OldPath, null!, 0, rename.NewPath);

    [Fact]
    public async Task RenamingDuplicate_UpdatesCaptionsOfBothTabs()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var first = _vs.OpenFile(@"src\A\Program.cs");
        var second = _vs.OpenFile(@"src\B\Program.cs");
        using var service = await CreateInitializedServiceAsync();

        var rename = _vs.Rename(second, @"src\B\Worker.cs");
        RaiseRenameEx(service, rename);
        await service.PendingRefresh;

        Assert.Equal("Program.cs", first.Caption);
        Assert.Equal("Worker.cs", second.Caption);
    }

    [Fact]
    public async Task RenamingIntoDuplicate_ShowsParentFolders()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var first = _vs.OpenFile(@"src\A\Program.cs");
        var second = _vs.OpenFile(@"src\B\Worker.cs");
        using var service = await CreateInitializedServiceAsync();

        var rename = _vs.Rename(second, @"src\B\Program.cs");
        RaiseRenameEx(service, rename);
        await service.PendingRefresh;

        Assert.Equal(@"A\Program.cs", first.Caption);
        Assert.Equal(@"B\Program.cs", second.Caption);
    }

    [Fact]
    public async Task RenameEvent_RaisedBeforeFrameHasNewPath_StillUsesNewPath()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var frame = _vs.OpenFile(@"src\Program.cs");
        using var service = await CreateInitializedServiceAsync();
        string oldPath = (string)frame.Moniker!;
        string newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, "Worker.cs");

        // Visual Studio updates the document table (and raises the event) before the frame's own path.
        service.OnAfterAttributeChangeEx(2, RdtaMkDocument, null!, 0, oldPath, null!, 0, newPath);
        File.Move(oldPath, newPath);
        frame.SetMoniker(newPath);
        await service.PendingRefresh;

        Assert.Equal("Worker.cs", frame.Caption);
    }

    [Fact]
    public async Task BasicAttributeChange_WithRename_RefreshesCaptions()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var frame = _vs.OpenFile(@"src\Program.cs");
        using var service = await CreateInitializedServiceAsync();

        _vs.Rename(frame, @"src\Worker.cs");
        service.OnAfterAttributeChange(docCookie: 2, RdtaMkDocument);
        await service.PendingRefresh;

        Assert.Equal("Worker.cs", frame.Caption);
    }

    [Fact]
    public async Task BothRenameEvents_AreCombinedIntoOneRefresh()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var frame = _vs.OpenFile(@"src\Program.cs");
        using var service = await CreateInitializedServiceAsync();
        int setCountAfterInit = frame.CaptionSetCount;

        var rename = _vs.Rename(frame, @"src\Worker.cs");
        service.OnAfterAttributeChange(docCookie: 2, RdtaMkDocument);
        RaiseRenameEx(service, rename);
        await service.PendingRefresh;

        Assert.Equal(setCountAfterInit + 1, frame.CaptionSetCount);
        Assert.Equal("Worker.cs", frame.Caption);
    }

    [Fact]
    public async Task UnrelatedAttributeChange_DoesNotRefresh()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var frame = _vs.OpenFile(@"src\Program.cs");
        using var service = await CreateInitializedServiceAsync();
        int setCountAfterInit = frame.CaptionSetCount;
        string path = (string)frame.Moniker!;

        service.OnAfterAttributeChange(docCookie: 1, (uint)__VSRDTATTRIB.RDTA_DocDataIsDirty);
        service.OnAfterAttributeChangeEx(1, (uint)__VSRDTATTRIB.RDTA_DocDataIsDirty, null!, 0, path, null!, 0, path);
        await service.PendingRefresh;

        Assert.Equal(setCountAfterInit, frame.CaptionSetCount);
    }

    [Fact]
    public async Task RenameAfterDispose_DoesNotRefresh()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var frame = _vs.OpenFile(@"src\Program.cs");
        var service = await CreateInitializedServiceAsync();
        int setCountAfterInit = frame.CaptionSetCount;

        var rename = _vs.Rename(frame, @"src\Worker.cs");
        RaiseRenameEx(service, rename);
        service.Dispose();
        await service.PendingRefresh;

        Assert.Equal(setCountAfterInit, frame.CaptionSetCount);
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
    public async Task DeeplyNestedDuplicates_ShowOnlyTheDifferingFolderByDefault()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var first = _vs.OpenFile(@"src\Web\Pages\Index.razor");
        var second = _vs.OpenFile(@"src\Admin\Pages\Index.razor");

        using var service = await CreateInitializedServiceAsync();

        Assert.Equal(@"Web\Index.razor", first.Caption);
        Assert.Equal(@"Admin\Index.razor", second.Caption);
    }

    [Fact]
    public async Task CaptionOptions_AreUsedWhenBuildingCaptions()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var first = _vs.OpenFile(@"src\Web\Pages\Index.razor");
        var second = _vs.OpenFile(@"src\Admin\Pages\Index.razor");
        var options = new CaptionOptions(collapseMiddle: true, layout: CaptionLayout.FileNameThenPath);

        using var service = new TabCaptionService(_vs.ServiceProvider.Object, () => options);
        await service.InitializeAsync();

        Assert.Equal(@"Index.razor (Web)", first.Caption);
        Assert.Equal(@"Index.razor (Admin)", second.Caption);
    }

    [Fact]
    public async Task RefreshCaptions_PicksUpChangedOptions()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var first = _vs.OpenFile(@"src\Web\Pages\Index.razor");
        _vs.OpenFile(@"src\Admin\Pages\Index.razor");
        var options = new CaptionOptions(collapseMiddle: false);
        using var service = new TabCaptionService(_vs.ServiceProvider.Object, () => options);
        await service.InitializeAsync();
        Assert.Equal(@"Web\Pages\Index.razor", first.Caption);

        options = new CaptionOptions(collapseMiddle: true);
        service.RefreshCaptions();

        Assert.Equal(@"Web\Index.razor", first.Caption);
    }

    [Fact]
    public async Task AbbreviateFolders_ClipsFolderWordsToTheBudget()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var first = _vs.OpenFile(@"src\Web\Pages\Index.razor");
        var second = _vs.OpenFile(@"src\Administration\Pages\Index.razor");
        var options = new CaptionOptions(collapseMiddle: true, FolderAbbreviation.ToFit);
        var budget = new CaptionBudget(maxWidth: 17, measure: caption => caption.Length);

        using var service = new TabCaptionService(_vs.ServiceProvider.Object, () => options, () => budget);
        await service.InitializeAsync();

        Assert.Equal(@"Web\Index.razor", first.Caption);
        Assert.Equal(@"Admin\Index.razor", second.Caption);
    }

    [Fact]
    public async Task AbbreviateFolders_WithoutBudget_LeavesFoldersWhole()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _vs.OpenFile(@"src\Web\Pages\Index.razor");
        var second = _vs.OpenFile(@"src\Administration\Pages\Index.razor");
        var options = new CaptionOptions(collapseMiddle: false, FolderAbbreviation.ToFit);

        using var service = new TabCaptionService(_vs.ServiceProvider.Object, () => options, () => null);
        await service.InitializeAsync();

        Assert.Equal(@"Administration\Pages\Index.razor", second.Caption);
    }

    [Fact]
    public async Task WithoutAbbreviateFolders_DoesNotAskForABudget()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _vs.OpenFile(@"src\Web\Pages\Index.razor");
        _vs.OpenFile(@"src\Admin\Pages\Index.razor");
        int budgetRequests = 0;

        using var service = new TabCaptionService(_vs.ServiceProvider.Object, () => CaptionOptions.Default, () => { budgetRequests++; return null; });
        await service.InitializeAsync();

        Assert.Equal(0, budgetRequests);
    }

    [Fact]
    public async Task AbbreviateAlways_ShortensFoldersWithoutAskingForABudget()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var first = _vs.OpenFile(@"src\Web\Pages\Index.razor");
        var second = _vs.OpenFile(@"src\Administration\Pages\Index.razor");
        var options = new CaptionOptions(collapseMiddle: true, FolderAbbreviation.Always);
        int budgetRequests = 0;

        using var service = new TabCaptionService(_vs.ServiceProvider.Object, () => options, () => { budgetRequests++; return null; });
        await service.InitializeAsync();

        Assert.Equal(@"Web\Index.razor", first.Caption);
        Assert.Equal(@"Adm\Index.razor", second.Caption);
        Assert.Equal(0, budgetRequests);
    }

    [Fact]
    public async Task Disabled_ShowsPlainFileNamesWithoutAskingForABudget()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var first = _vs.OpenFile(@"src\Web\Pages\Index.razor");
        var second = _vs.OpenFile(@"src\Admin\Pages\Index.razor");
        var options = new CaptionOptions(abbreviateFolders: FolderAbbreviation.ToFit, enabled: false);
        int budgetRequests = 0;

        using var service = new TabCaptionService(_vs.ServiceProvider.Object, () => options, () => { budgetRequests++; return null; });
        await service.InitializeAsync();

        Assert.Equal("Index.razor", first.Caption);
        Assert.Equal("Index.razor", second.Caption);
        Assert.Equal(0, budgetRequests);
    }

    [Fact]
    public async Task SwitchingOffAndOn_RemovesAndRestoresThePaths()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var first = _vs.OpenFile(@"src\Web\Pages\Index.razor");
        _vs.OpenFile(@"src\Admin\Pages\Index.razor");
        var options = CaptionOptions.Default;
        using var service = new TabCaptionService(_vs.ServiceProvider.Object, () => options);
        await service.InitializeAsync();
        Assert.Equal(@"Web\Index.razor", first.Caption);

        options = options.With(enabled: false);
        service.RefreshCaptions();
        Assert.Equal("Index.razor", first.Caption);

        options = options.With(enabled: true);
        service.RefreshCaptions();
        Assert.Equal(@"Web\Index.razor", first.Caption);
    }

    [Fact]
    public async Task CaptionsApplied_ReceivesTheOptionsAndEveryCaption()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _vs.OpenFile(@"src\A\Program.cs");
        _vs.OpenFile(@"src\B\Program.cs");
        _vs.OpenFile(@"src\A\Startup.cs");
        var options = new CaptionOptions(widenTabs: TabWidening.Off);
        CaptionOptions? appliedOptions = null;
        IReadOnlyList<string>? appliedCaptions = null;

        using var service = new TabCaptionService(_vs.ServiceProvider.Object, () => options, null,
            (o, captions) => { appliedOptions = o; appliedCaptions = captions; });
        await service.InitializeAsync();

        Assert.Same(options, appliedOptions);
        Assert.Equal([@"A\Program.cs", @"B\Program.cs", "Startup.cs"], appliedCaptions!.OrderBy(c => c));
    }

    [Fact]
    public async Task CaptionsApplied_WithNoFileTabs_ReceivesNoCaptions()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        IReadOnlyList<string>? appliedCaptions = null;

        using var service = new TabCaptionService(_vs.ServiceProvider.Object, null, null,
            (_, captions) => appliedCaptions = captions);
        await service.InitializeAsync();

        Assert.NotNull(appliedCaptions);
        Assert.Empty(appliedCaptions);
    }

    [Fact]
    public async Task RefreshCaptions_AfterDispose_DoesNothing()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var frame = _vs.OpenFile(@"src\Program.cs");
        var service = await CreateInitializedServiceAsync();
        int setCountAfterInit = frame.CaptionSetCount;

        service.Dispose();
        service.RefreshCaptions();

        Assert.Equal(setCountAfterInit, frame.CaptionSetCount);
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

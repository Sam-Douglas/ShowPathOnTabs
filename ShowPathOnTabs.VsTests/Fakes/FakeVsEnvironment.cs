using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Moq;
using IAsyncServiceProvider = Microsoft.VisualStudio.Shell.IAsyncServiceProvider;

namespace ShowPathOnTabs.VsTests.Fakes;

/// <summary>
/// A fake set of Visual Studio document services: a running document table, a UI shell that
/// enumerates the open document windows, and the window frames themselves.
/// </summary>
internal sealed class FakeVsEnvironment : IDisposable
{
    public const uint RdtCookie = 42;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ShowPathOnTabs.VsTests", Guid.NewGuid().ToString("N"));

    public List<FakeWindowFrame> OpenFrames { get; } = [];

    public Mock<IVsRunningDocumentTable> RunningDocTable { get; } = new();

    public Mock<IVsUIShell> UIShell { get; } = new();

    public Mock<IAsyncServiceProvider> ServiceProvider { get; } = new();

    /// <summary>The event sink the service registered with the running document table.</summary>
    public IVsRunningDocTableEvents? AdvisedSink { get; private set; }

    private delegate void AdviseCallback(IVsRunningDocTableEvents sink, out uint cookie);

    private delegate void GetEnumCallback(out IEnumWindowFrames enumerator);

    public FakeVsEnvironment()
    {
        Directory.CreateDirectory(_root);

        uint cookie = RdtCookie;
        RunningDocTable
            .Setup(r => r.AdviseRunningDocTableEvents(It.IsAny<IVsRunningDocTableEvents>(), out cookie))
            .Callback(new AdviseCallback((IVsRunningDocTableEvents sink, out uint c) => { AdvisedSink = sink; c = RdtCookie; }))
            .Returns(VSConstants.S_OK);

        UIShell
            .Setup(s => s.GetDocumentWindowEnum(out It.Ref<IEnumWindowFrames>.IsAny))
            .Callback(new GetEnumCallback((out IEnumWindowFrames e) => e = new FakeWindowFrameEnumerator(OpenFrames.Select(f => f.Object).ToList())))
            .Returns(VSConstants.S_OK);

        ServiceProvider
            .Setup(p => p.GetServiceAsync(typeof(SVsRunningDocumentTable)))
            .ReturnsAsync(() => RunningDocTable.Object);
        ServiceProvider
            .Setup(p => p.GetServiceAsync(typeof(SVsUIShell)))
            .ReturnsAsync(() => UIShell.Object);
    }

    /// <summary>Creates a real file under the temp root and opens a tab for it.</summary>
    public FakeWindowFrame OpenFile(string relativePath)
    {
        string fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, string.Empty);
        return OpenMoniker(fullPath);
    }

    /// <summary>Opens a tab with an arbitrary moniker (which may not be a file on disk).</summary>
    public FakeWindowFrame OpenMoniker(object? moniker)
    {
        var frame = new FakeWindowFrame(moniker);
        OpenFrames.Add(frame);
        return frame;
    }

    public void Close(FakeWindowFrame frame) => OpenFrames.Remove(frame);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>A document window whose caption changes are recorded.</summary>
internal sealed class FakeWindowFrame
{
    private readonly Mock<IVsWindowFrame> _mock = new();

    public FakeWindowFrame(object? moniker)
    {
        object? value = moniker;
        _mock
            .Setup(f => f.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out value))
            .Returns(VSConstants.S_OK);
        _mock
            .Setup(f => f.SetProperty((int)__VSFPROPID.VSFPROPID_OwnerCaption, It.IsAny<object>()))
            .Callback((int _, object caption) => { Caption = (string)caption; CaptionSetCount++; })
            .Returns(VSConstants.S_OK);
    }

    public IVsWindowFrame Object => _mock.Object;

    public Mock<IVsWindowFrame> Mock => _mock;

    /// <summary>The last caption the extension set, or null if it never set one.</summary>
    public string? Caption { get; private set; }

    public int CaptionSetCount { get; private set; }
}

internal sealed class FakeWindowFrameEnumerator(IReadOnlyList<IVsWindowFrame> frames) : IEnumWindowFrames
{
    private int _index;

    public int Next(uint celt, IVsWindowFrame[] rgelt, out uint pceltFetched)
    {
        pceltFetched = 0;
        while (pceltFetched < celt && _index < frames.Count)
            rgelt[pceltFetched++] = frames[_index++];
        return pceltFetched == celt ? VSConstants.S_OK : VSConstants.S_FALSE;
    }

    public int Skip(uint celt)
    {
        _index += (int)celt;
        return _index <= frames.Count ? VSConstants.S_OK : VSConstants.S_FALSE;
    }

    public int Reset()
    {
        _index = 0;
        return VSConstants.S_OK;
    }

    public int Clone(out IEnumWindowFrames ppenum)
    {
        ppenum = new FakeWindowFrameEnumerator(frames) { _index = _index };
        return VSConstants.S_OK;
    }
}

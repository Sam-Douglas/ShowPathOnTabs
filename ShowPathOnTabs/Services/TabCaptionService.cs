using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using ShowPathOnTabs.Core.Captioning;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using IAsyncServiceProvider = Microsoft.VisualStudio.Shell.IAsyncServiceProvider;

namespace ShowPathOnTabs.Services;

/// <summary>
/// Updates tab captions to show the path where there is ambiguity
/// </summary>
/// <param name="serviceProvider">Provides the running document table and UI shell (normally the package).</param>
/// <param name="captionOptions">Returns the current caption settings. Read on every refresh so changes apply straight away.</param>
/// <param name="captionBudget">Returns the room available on a tab, for folder names that are abbreviated to fit. Null when it cannot be measured.</param>
/// <param name="captionsApplied">Called with the options and every caption after the captions are set.</param>
internal sealed class TabCaptionService(
    IAsyncServiceProvider serviceProvider,
    Func<CaptionOptions>? captionOptions = null,
    Func<CaptionBudget?>? captionBudget = null,
    Action<CaptionOptions, IReadOnlyList<string>>? captionsApplied = null) : IVsRunningDocTableEvents2, IDisposable
{
    // Attributes whose change means a tab may now show a different file: path changed, file reloaded.
    private const uint RenameAttributes = (uint)(__VSRDTATTRIB.RDTA_MkDocument | __VSRDTATTRIB.RDTA_DocDataReloaded);

    private readonly IAsyncServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly Func<CaptionOptions> _captionOptions = captionOptions ?? (() => CaptionOptions.Default);
    private readonly Func<CaptionBudget?> _captionBudget = captionBudget ?? (() => null);
    private IVsRunningDocumentTable? _runningDocTable;
    private IVsUIShell? _uiShell;
    private uint _rdtCookie;
    private bool _disposed;

    // Refreshes deferred until Visual Studio has finished handling a rename.
    private readonly JoinableTaskCollection _deferredRefreshes = ThreadHelper.JoinableTaskContext.CreateCollection();
    private bool _refreshQueued;

    /// <summary>
    /// Completes once any refresh deferred by a rename has run. Exposed for tests.
    /// </summary>
    internal Task PendingRefresh => _deferredRefreshes.JoinTillEmptyAsync();

    public async Task InitializeAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        _runningDocTable = await _serviceProvider.GetServiceAsync(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
        _uiShell = await _serviceProvider.GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;

        if (_runningDocTable == null) return;

        _runningDocTable.AdviseRunningDocTableEvents(this, out _rdtCookie);

        UpdateTabCaptions();
    }

    public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;

    public int OnAfterSave(uint docCookie) => VSConstants.S_OK;

    /// <summary>
    /// Fires when a document's attributes change, such as when a file is renamed or saved to a new path.
    /// </summary>
    public int OnAfterAttributeChange(uint docCookie, uint grfAttribs)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if ((grfAttribs & RenameAttributes) != 0)
            ScheduleRefresh();

        return VSConstants.S_OK;
    }

    /// <summary>
    /// Extended form of <see cref="OnAfterAttributeChange"/> that carries the old and new paths.
    /// Visual Studio reports renames through this event, so both are handled.
    /// </summary>
    public int OnAfterAttributeChangeEx(uint docCookie, uint grfAttribs,
        IVsHierarchy pHierOld, uint itemidOld, string pszMkDocumentOld,
        IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        bool pathChanged = !string.Equals(pszMkDocumentOld, pszMkDocumentNew, StringComparison.OrdinalIgnoreCase);
        if (pathChanged || (grfAttribs & RenameAttributes) != 0)
            ScheduleRefresh();

        return VSConstants.S_OK;
    }

    /// <summary>
    /// Refreshes captions once Visual Studio has finished handling the current event.
    /// During a rename, the document table changes before the window frames get their new path,
    /// and Visual Studio resets the tab caption itself afterwards, so refreshing straight away
    /// would read the old path and then be overwritten.
    /// Several events in the same rename are combined into one refresh.
    /// </summary>
    private void ScheduleRefresh()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (_disposed || _refreshQueued) return;
        _refreshQueued = true;

        var factory = ThreadHelper.JoinableTaskContext.CreateFactory(_deferredRefreshes);
        factory.RunAsync(async () =>
        {
            await factory.SwitchToMainThreadAsync(alwaysYield: true);
            _refreshQueued = false;
            if (!_disposed)
                UpdateTabCaptions();
        }).FileAndForget("ShowPathOnTabs/RefreshCaptionsAfterRename");
    }

    /// <summary>
    /// Tab is about to be fully closed
    /// </summary>
    public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        UpdateTabCaptions();
        return VSConstants.S_OK;
    }

    /// <summary>
    /// A tab is about to be in focus.
    /// FirstShow is non-zero the first time a tab is opened.
    /// </summary>
    public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (fFirstShow != 0)
            UpdateTabCaptions();
        return VSConstants.S_OK;
    }

    /// <summary>
    /// Tab is closed or hidden
    /// </summary>
    public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        UpdateTabCaptions();
        return VSConstants.S_OK;
    }

    /// <summary>
    /// Rebuilds the caption of every open tab, for example after the settings change.
    /// </summary>
    public void RefreshCaptions()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!_disposed)
            UpdateTabCaptions();
    }

    private void UpdateTabCaptions()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (_runningDocTable == null) return;

        try
        {
            var openDocs = GetOpenDocuments();
            if (openDocs.Count == 0)
            {
                // Still reported, so a tab width raised for captions that are now closed is put back.
                captionsApplied?.Invoke(_captionOptions(), []);
                return;
            }

            var allOpenPaths = openDocs.Select(d => d.FilePath).ToList();
            var options = _captionOptions();
            var budget = options.Enabled && options.AbbreviateFolders == FolderAbbreviation.ToFit ? _captionBudget() : null;

            var captions = new List<string>(openDocs.Count);
            foreach (var (filePath, frame) in openDocs)
            {
                // Switched off, tabs get the plain file name: Visual Studio's own caption.
                string caption = options.Enabled
                    ? TabCaptionBuilder.Build(filePath, allOpenPaths, options, budget)
                    : Path.GetFileName(filePath);
                frame.SetProperty((int)__VSFPROPID.VSFPROPID_OwnerCaption, caption);
                captions.Add(caption);
            }

            captionsApplied?.Invoke(options, captions);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in RefreshAllCaptions: {ex.Message}");
        }
    }

    private List<(string FilePath, IVsWindowFrame Frame)> GetOpenDocuments()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var result = new List<(string FilePath, IVsWindowFrame Frame)>();

        if (_runningDocTable == null || _uiShell == null) return result;

        try
        {
            // Enumerator for every tab
            _uiShell.GetDocumentWindowEnum(out IEnumWindowFrames frameEnumerator);
            if (frameEnumerator == null) return result;

            IVsWindowFrame[] frameBuffer = new IVsWindowFrame[1];
            while (frameEnumerator.Next(1, frameBuffer, out uint fetched) == VSConstants.S_OK && fetched == 1)
            {
                var frame = frameBuffer[0];
                if (frame == null) continue;

                // Get tab path
                frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out object monikerValue);

                // Only keep file based tabs
                if (monikerValue is string filePath && !string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    result.Add((filePath, frame));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetOpenDocuments: {ex.Message}");
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ThreadHelper.ThrowIfNotOnUIThread();
        if (_runningDocTable != null && _rdtCookie != 0)
        {
            _runningDocTable.UnadviseRunningDocTableEvents(_rdtCookie);
            _rdtCookie = 0;
        }
    }
}
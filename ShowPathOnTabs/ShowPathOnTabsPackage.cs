using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Utilities.UnifiedSettings;
using ShowPathOnTabs.Commands;
using ShowPathOnTabs.Options;
using ShowPathOnTabs.Services;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ShowPathOnTabs
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    // The settings shown in Visual Studio's Settings UI, with the classic options page kept for
    // versions where Unified Settings is unavailable or switched off.
    [ProvideSettingsManifest(PackageRelativeManifestFile = @"Options\registration.json")]
    [ProvideOptionPage(typeof(GeneralOptionsPage), GeneralOptionsPage.CategoryName, GeneralOptionsPage.PageName, 0, 0, true, IsInUnifiedSettings = true)]
    [Guid(PackageGuidString)]
    public sealed class ShowPathOnTabsPackage : AsyncPackage
    {
        private const string PackageGuidString = "0075df24-a377-4274-a304-a8501ad061e8";

        private CaptionSettings? _captionSettings;
        private TabCaptionService? _tabCaptionService;
        private TabWidener? _tabWidener;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);

            var settingsManager = await GetUnifiedSettingsManagerAsync();

            // Dialog pages and menu commands are set up on the UI thread.
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var legacyPage = (GeneralOptionsPage)GetDialogPage(typeof(GeneralOptionsPage));

            // Abbreviating folders to fit depends on Visual Studio's tab width settings as well as our own.
            var captionSettings = new CaptionSettings(
                settingsManager,
                legacyPage,
                JoinableTaskFactory,
                TabCaptionBudgetProvider.MaximumTabWidthMoniker,
                TabCaptionBudgetProvider.BoldSelectedTabsMoniker);
            _captionSettings = captionSettings;

            var budgetProvider = new TabCaptionBudgetProvider(settingsManager);
            var tabWidener = new TabWidener(settingsManager, budgetProvider.CreateMeasurer);
            _tabWidener = tabWidener;

            _tabCaptionService = new TabCaptionService(
                this,
                () => captionSettings.Current,
                budgetProvider.Create,
                (options, captions) => tabWidener.Apply(options.WidenTabs, options.TabWidthLimit, captions));
            await _tabCaptionService.InitializeAsync();

            captionSettings.Changed += OnCaptionSettingsChanged;

            if (await GetServiceAsync(typeof(IMenuCommandService)) is IMenuCommandService commandService)
                new CaptionCommands(captionSettings, () => ShowOptionPage(typeof(GeneralOptionsPage))).Register(commandService);
        }

        private async Task<ISettingsManager?> GetUnifiedSettingsManagerAsync()
        {
            try
            {
                return await GetServiceAsync(typeof(SVsUnifiedSettingsManager)) as ISettingsManager;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShowPathOnTabs: Unified Settings is unavailable: {ex.Message}");
                return null;
            }
        }

        private void OnCaptionSettingsChanged(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _tabCaptionService?.RefreshCaptions();
        }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (disposing)
            {
                _captionSettings?.Dispose();
                _tabCaptionService?.Dispose();
                _tabWidener?.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Identifies Visual Studio's Unified Settings manager service. The SDK publishes the
        /// <see cref="ISettingsManager"/> interface but not this service id, so it is declared here by its GUID.
        /// </summary>
        [Guid("e3684f31-344e-42ea-9047-b620fdc7ac25")]
        private interface SVsUnifiedSettingsManager
        {
        }
    }
}

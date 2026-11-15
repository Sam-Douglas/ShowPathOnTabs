using Microsoft.VisualStudio.Shell;
using ShowPathOnTabs.Core.Captioning;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ShowPathOnTabs.Options;

/// <summary>
/// The classic options page, used where Visual Studio's Unified Settings is unavailable or switched off.
/// Visual Studio stores the public properties in the user's settings.
/// </summary>
[ComVisible(true)]
[Guid("5f0b9a3e-6c1d-4b57-9a40-2f6f6a0f5d11")]
public class GeneralOptionsPage : DialogPage, ILegacyCaptionStore
{
    public const string CategoryName = "Show Path On Tabs";
    public const string PageName = "General";

    /// <summary>Raised after the user confirms the Options dialog.</summary>
    public event EventHandler? Applied;

    [Category("Tab caption")]
    [DisplayName("Collapse middle folders")]
    [Description("Show only the folder that tells the tabs apart, e.g. Admin\\Index.razor instead of Admin\\Areas\\Pages\\Index.razor.")]
    [DefaultValue(false)]
    public bool CollapseMiddle { get; set; }

    [Category("Tab caption")]
    [DisplayName("Abbreviate folders to fit")]
    [Description("When a caption is too long for the tab, clip the words in its folder names (EntityFrameworkCore to EntFraCor) only as far as needed to make it fit. The file name is never changed.")]
    [DefaultValue(false)]
    public bool AbbreviateFolders { get; set; }

    [Category("Tab caption")]
    [DisplayName("Layout")]
    [Description(
        "Where the path sits relative to the file name.\n" +
        "PathThenFileName: Admin\\Pages\\Index.razor.\n" +
        "FileNameThenPath: Index.razor (Admin\\Pages).")]
    [DefaultValue(CaptionLayout.PathThenFileName)]
    public CaptionLayout Layout { get; set; } = CaptionLayout.PathThenFileName;

    protected override void OnApply(PageApplyEventArgs e)
    {
        base.OnApply(e);

        if (e.ApplyBehavior == ApplyKind.Apply)
            Applied?.Invoke(this, EventArgs.Empty);
    }

    void ILegacyCaptionStore.Save() => SaveSettingsToStorage();
}

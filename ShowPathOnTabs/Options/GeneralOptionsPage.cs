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
    [DisplayName("Enabled")]
    [Description("When off, tabs show Visual Studio's own captions and the tab width is set back to Visual Studio's default (215).")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    [Category("Tab caption")]
    [DisplayName("Collapse middle folders")]
    [Description("Show only the folder that tells the tabs apart, e.g. Admin\\Index.razor instead of Admin\\Areas\\Pages\\Index.razor.")]
    [DefaultValue(true)]
    public bool CollapseMiddle { get; set; } = true;

    [Category("Tab caption")]
    [DisplayName("Abbreviate folders")]
    [Description(
        "Clip the words in folder names (EntityFrameworkCore to EntFraCor). The file name is never changed.\n" +
        "Off: folder names are shown in full.\n" +
        "ToFit: only when the caption is too long for the tab, and only as far as needed.\n" +
        "Always: always, as short as they can go.")]
    [DefaultValue(FolderAbbreviation.Off)]
    public FolderAbbreviation FolderAbbreviation { get; set; } = FolderAbbreviation.Off;

    [Category("Tab caption")]
    [DisplayName("Layout")]
    [Description(
        "Where the path sits relative to the file name.\n" +
        "PathThenFileName: Admin\\Pages\\Index.razor.\n" +
        "FileNameThenPath: Index.razor (Admin\\Pages).")]
    [DefaultValue(CaptionLayout.PathThenFileName)]
    public CaptionLayout Layout { get; set; } = CaptionLayout.PathThenFileName;

    [Category("Tab caption")]
    [DisplayName("Widen tabs")]
    [Description(
        "Raise Visual Studio's maximum tab width so the widest caption fits.\n" +
        "Off: the width is left alone. Switching to Off sets it back to Visual Studio's default (215).\n" +
        "ToFit: as wide as the widest caption needs.\n" +
        "ToLimit: as wide as the widest caption needs, but no wider than the tab width limit.")]
    [DefaultValue(TabWidening.ToFit)]
    public TabWidening TabWidening { get; set; } = TabWidening.ToFit;

    [Category("Tab caption")]
    [DisplayName("Tab width limit")]
    [Description("The widest tabs are made when Widen tabs is ToLimit (215 to 999).")]
    [DefaultValue(CaptionOptions.DefaultTabWidthLimit)]
    public int TabWidthLimit { get; set; } = CaptionOptions.DefaultTabWidthLimit;

    protected override void OnApply(PageApplyEventArgs e)
    {
        base.OnApply(e);

        if (e.ApplyBehavior == ApplyKind.Apply)
            Applied?.Invoke(this, EventArgs.Empty);
    }

    void ILegacyCaptionStore.Save() => SaveSettingsToStorage();
}

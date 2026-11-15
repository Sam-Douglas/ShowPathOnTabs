namespace ShowPathOnTabs.Core.Captioning
{
    /// <summary>
    /// Where the path sits relative to the file name.
    /// </summary>
    public enum CaptionLayout
    {
        /// <summary>Path then file name, e.g. <c>Admin\Pages\Index.razor</c>.</summary>
        PathThenFileName,

        /// <summary>File name then path, e.g. <c>Index.razor (Admin\Pages)</c>.</summary>
        FileNameThenPath
    }

    /// <summary>
    /// When folder names are shortened by clipping their words (<c>EntityFrameworkCore</c> to <c>EntFraCor</c>).
    /// The file name is never changed, and a folder is never shortened until it reads like a sibling tab's.
    /// </summary>
    public enum FolderAbbreviation
    {
        /// <summary>Folder names are shown in full.</summary>
        Off,

        /// <summary>Only when the caption is too long for the tab, and only as far as needed to fit.</summary>
        ToFit,

        /// <summary>Always, as short as they can go.</summary>
        Always
    }

    /// <summary>
    /// How Visual Studio's maximum tab width is raised so the widest caption fits.
    /// </summary>
    public enum TabWidening
    {
        /// <summary>The width is left alone.</summary>
        Off,

        /// <summary>As wide as the widest caption needs.</summary>
        ToFit,

        /// <summary>As wide as the widest caption needs, but no wider than <see cref="CaptionOptions.TabWidthLimit"/>.</summary>
        ToLimit
    }

    /// <summary>
    /// Settings that control how a tab caption is built.
    /// </summary>
    public sealed class CaptionOptions
    {
        public static readonly CaptionOptions Default = new CaptionOptions();

        /// <summary>The default for <see cref="TabWidthLimit"/>.</summary>
        public const int DefaultTabWidthLimit = 300;

        public CaptionOptions(
            bool collapseMiddle = true,
            FolderAbbreviation abbreviateFolders = FolderAbbreviation.Off,
            CaptionLayout layout = CaptionLayout.PathThenFileName,
            TabWidening widenTabs = TabWidening.ToFit,
            int tabWidthLimit = DefaultTabWidthLimit,
            bool enabled = true)
        {
            CollapseMiddle = collapseMiddle;
            AbbreviateFolders = abbreviateFolders;
            Layout = layout;
            WidenTabs = widenTabs;
            TabWidthLimit = tabWidthLimit;
            Enabled = enabled;
        }

        /// <summary>
        /// Whether the extension changes anything. When off, tabs show Visual Studio's own caption
        /// and the tab width is left to Visual Studio.
        /// </summary>
        public bool Enabled { get; }

        /// <summary>
        /// Show only the folder that tells the tabs apart, e.g. <c>Admin\Index.razor</c>
        /// instead of <c>Admin\Areas\Pages\Index.razor</c>.
        /// </summary>
        public bool CollapseMiddle { get; }

        public FolderAbbreviation AbbreviateFolders { get; }

        public CaptionLayout Layout { get; }

        /// <summary>
        /// How Visual Studio's maximum tab width is raised for long captions. Turning it off sets the
        /// width back to Visual Studio's default.
        /// </summary>
        public TabWidening WidenTabs { get; }

        /// <summary>The widest a tab is made under <see cref="TabWidening.ToLimit"/>.</summary>
        public int TabWidthLimit { get; }

        public CaptionOptions With(
            bool? collapseMiddle = null,
            FolderAbbreviation? abbreviateFolders = null,
            CaptionLayout? layout = null,
            TabWidening? widenTabs = null,
            int? tabWidthLimit = null,
            bool? enabled = null)
        {
            return new CaptionOptions(
                collapseMiddle ?? CollapseMiddle,
                abbreviateFolders ?? AbbreviateFolders,
                layout ?? Layout,
                widenTabs ?? WidenTabs,
                tabWidthLimit ?? TabWidthLimit,
                enabled ?? Enabled);
        }
    }
}

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
    /// Settings that control how a tab caption is built.
    /// </summary>
    public sealed class CaptionOptions
    {
        public static readonly CaptionOptions Default = new CaptionOptions();

        public CaptionOptions(
            bool collapseMiddle = false,
            bool abbreviateFolders = false,
            CaptionLayout layout = CaptionLayout.PathThenFileName)
        {
            CollapseMiddle = collapseMiddle;
            AbbreviateFolders = abbreviateFolders;
            Layout = layout;
        }

        /// <summary>
        /// Show only the folder that tells the tabs apart, e.g. <c>Admin\Index.razor</c>
        /// instead of <c>Admin\Areas\Pages\Index.razor</c>.
        /// </summary>
        public bool CollapseMiddle { get; }

        /// <summary>
        /// When a caption is too long for the tab, clip the words in its folder names
        /// (<c>EntityFrameworkCore</c> to <c>EntFraCor</c>) only as far as needed to make it fit.
        /// The file name is never changed.
        /// </summary>
        public bool AbbreviateFolders { get; }

        public CaptionLayout Layout { get; }

        public CaptionOptions With(
            bool? collapseMiddle = null,
            bool? abbreviateFolders = null,
            CaptionLayout? layout = null)
        {
            return new CaptionOptions(
                collapseMiddle ?? CollapseMiddle,
                abbreviateFolders ?? AbbreviateFolders,
                layout ?? Layout);
        }
    }
}

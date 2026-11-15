using ShowPathOnTabs.Core.Captioning;
using static ShowPathOnTabs.Core.Captioning.TabCaptionBuilder;

namespace ShowPathOnTabs.UnitTests.Captioning;

public class TabCaptionBuilderTests
{
    [Fact]
    public void UniqueFilename_ReturnsUnmodified_Caption()
    {
        List<string> tabPaths = [
            @"C:\Test\Pages\Page.razor",
            @"C:\Test\Pages\DifferentPage.razor"];

        Assert.Equal("Page.razor", Build(tabPaths.First(), tabPaths));
    }

    [Fact]
    public void TwoDuplicateFilenames_ReturnsCaptionWithParentFolder()
    {
        List<string> tabPaths = [
            @"C:\Test\Pages\1\Page.razor",
            @"C:\Test\Pages\2\Page.razor"];

        Assert.Equal(@"1\Page.razor", Build(tabPaths.First(), tabPaths));
    }

    [Fact]
    public void ThreeDuplicateFilenames_ReturnsCaptionsWithUniqueNames()
    {
        List<string> tabPaths = [
            @"C:\Test\Pages\1\Page.razor",
            @"C:\Test\Pages\2\Page.razor",
            @"C:\Test\Pages\3\Page.razor"];

        var resultCaptions = tabPaths.Select(path => Build(path, tabPaths)).ToList();

        Assert.Equal(resultCaptions.Count, resultCaptions.Distinct().Count());
    }

    [Fact]
    public void DuplicateFilenames_WithMatchingParentFolderNames_ReturnsCorrectCaption()
    {
        List<string> tabPaths = [
            @"C:\Test\Pages\1\Content\Page.razor",
            @"C:\Test\Pages\2\Content\Page.razor"];

        Assert.Equal(@"1\Content\Page.razor", Build(tabPaths.First(), tabPaths));
    }

    private static readonly CaptionOptions Collapsed = new(collapseMiddle: true);
    private static readonly CaptionOptions FileNameFirst = new(layout: CaptionLayout.FileNameThenPath);
    private static readonly CaptionOptions Abbreviated = new(abbreviateFolders: true);
    private static readonly CaptionOptions CollapsedAbbreviated = new(collapseMiddle: true, abbreviateFolders: true);

    // One unit per character keeps the expected captions easy to count.
    private static CaptionBudget Budget(int characters) => new(characters, caption => caption.Length);

    private static readonly List<string> ServiceTabs = [
        @"C:\Test\Messaging\AzureServiceBus\Configuration\ServiceCollectionExtensions.cs",
        @"C:\Test\Caching\Redis\Configuration\ServiceCollectionExtensions.cs"];

    [Fact]
    public void CollapseMiddle_ShowsOnlyTheFolderThatDiffers()
    {
        List<string> tabPaths = [
            @"C:\Test\Admin\Areas\Pages\Index.razor",
            @"C:\Test\Web\Areas\Pages\Index.razor"];

        Assert.Equal(@"Admin\Index.razor", Build(tabPaths[0], tabPaths, Collapsed));
        Assert.Equal(@"Web\Index.razor", Build(tabPaths[1], tabPaths, Collapsed));
    }

    [Fact]
    public void CollapseMiddle_SingleFolder_IsLeftAlone()
    {
        List<string> tabPaths = [
            @"C:\Test\Pages\1\Page.razor",
            @"C:\Test\Pages\2\Page.razor"];

        Assert.Equal(@"1\Page.razor", Build(tabPaths[0], tabPaths, Collapsed));
    }

    [Fact]
    public void CollapseMiddle_UniqueFilename_ReturnsFilename()
    {
        List<string> tabPaths = [
            @"C:\Test\Pages\Page.razor",
            @"C:\Test\Pages\DifferentPage.razor"];

        Assert.Equal("Page.razor", Build(tabPaths[0], tabPaths, Collapsed));
    }

    [Fact]
    public void CollapseMiddle_WhenCollapsingWouldCollide_KeepsFullPath()
    {
        // Both A tabs need two folders to differ from B, and would both collapse to A\Page.razor.
        List<string> tabPaths = [
            @"C:\Test\A\X\Page.razor",
            @"C:\Test\A\Y\Page.razor",
            @"C:\Test\B\X\Page.razor",
            @"C:\Test\B\Y\Page.razor"];

        var captions = tabPaths.Select(path => Build(path, tabPaths, Collapsed)).ToList();

        Assert.Equal([@"A\X\Page.razor", @"A\Y\Page.razor", @"B\X\Page.razor", @"B\Y\Page.razor"], captions);
    }

    [Fact]
    public void CollapseMiddle_WhenCollapsedFormIsAnotherTabsRealPath_KeepsFullPath()
    {
        // The third tab really is Admin\Index.razor, so the first cannot collapse to that.
        List<string> tabPaths = [
            @"C:\Test\Admin\Pages\Index.razor",
            @"C:\Test\Web\Pages\Index.razor",
            @"C:\Other\Admin\Index.razor"];

        var captions = tabPaths.Select(path => Build(path, tabPaths, Collapsed)).ToList();

        Assert.Equal([@"Admin\Pages\Index.razor", @"Web\Index.razor", @"Admin\Index.razor"], captions);
    }

    [Fact]
    public void CollapseMiddle_CaptionsStayUnique()
    {
        List<string> tabPaths = [
            @"C:\Test\A\X\Page.razor",
            @"C:\Test\B\X\Page.razor",
            @"C:\Test\B\Y\Page.razor",
            @"C:\Test\C\Deep\Er\X\Page.razor",
            @"C:\Other\A\Deep\Er\X\Page.razor"];

        var captions = tabPaths.Select(path => Build(path, tabPaths, Collapsed)).ToList();

        Assert.Equal(captions.Count, captions.Distinct().Count());
    }

    [Fact]
    public void FileNameThenPath_PutsPathInBrackets()
    {
        List<string> tabPaths = [
            @"C:\Test\Pages\1\Content\Page.razor",
            @"C:\Test\Pages\2\Content\Page.razor"];

        Assert.Equal(@"Page.razor (1\Content)", Build(tabPaths[0], tabPaths, FileNameFirst));
    }

    [Fact]
    public void FileNameThenPath_UniqueFilename_ReturnsFilename()
    {
        List<string> tabPaths = [
            @"C:\Test\Pages\Page.razor",
            @"C:\Test\Pages\DifferentPage.razor"];

        Assert.Equal("Page.razor", Build(tabPaths[0], tabPaths, FileNameFirst));
    }

    [Fact]
    public void AbbreviateFolders_WhenCaptionFits_ChangesNothing()
    {
        Assert.Equal(
            @"AzureServiceBus\Configuration\ServiceCollectionExtensions.cs",
            Build(ServiceTabs[0], ServiceTabs, Abbreviated, Budget(60)));
    }

    [Fact]
    public void AbbreviateFolders_ClipsLongestWordsFirstUntilCaptionFits()
    {
        string caption = Build(ServiceTabs[0], ServiceTabs, Abbreviated, Budget(50));

        // "Bus" and "Azure" are short enough to stay whole.
        Assert.Equal(@"AzureServiBus\Confi\ServiceCollectionExtensions.cs", caption);
        Assert.Equal(50, caption.Length);
    }

    [Fact]
    public void AbbreviateFolders_WithCollapseMiddle_AbbreviatesTheRemainingFolder()
    {
        Assert.Equal(
            @"AzuSerBus\ServiceCollectionExtensions.cs",
            Build(ServiceTabs[0], ServiceTabs, CollapsedAbbreviated, Budget(40)));
    }

    [Fact]
    public void AbbreviateFolders_WhenNothingFits_ReturnsShortestFoldersAndWholeFileName()
    {
        string caption = Build(ServiceTabs[0], ServiceTabs, CollapsedAbbreviated, Budget(10));

        Assert.Equal(@"AzuSerBus\ServiceCollectionExtensions.cs", caption);
    }

    [Fact]
    public void AbbreviateFolders_FileNameFirst_AbbreviatesFolderInBrackets()
    {
        var options = new CaptionOptions(collapseMiddle: true, abbreviateFolders: true, CaptionLayout.FileNameThenPath);

        Assert.Equal(
            "ServiceCollectionExtensions.cs (AzuSerBus)",
            Build(ServiceTabs[0], ServiceTabs, options, Budget(42)));
    }

    [Fact]
    public void AbbreviateFolders_StopsBeforeFolderMatchesASiblingsFolder()
    {
        // At four letters a word, both folders would read "AzurServ".
        List<string> tabPaths = [
            @"C:\Test\AzureService\Configuration\Setup.cs",
            @"C:\Test\AzureServer\Configuration\Setup.cs"];

        var captions = tabPaths.Select(path => Build(path, tabPaths, CollapsedAbbreviated, Budget(5))).ToList();

        Assert.Equal([@"AzureServi\Setup.cs", @"AzureServe\Setup.cs"], captions);
    }

    [Fact]
    public void AbbreviateFolders_WithoutBudget_ChangesNothing()
    {
        Assert.Equal(
            @"Redis\Configuration\ServiceCollectionExtensions.cs",
            Build(ServiceTabs[1], ServiceTabs, Abbreviated));
    }

    [Fact]
    public void AbbreviateFolders_UniqueFilename_ReturnsFilename()
    {
        List<string> tabPaths = [@"C:\Test\Pages\AVeryLongUniquePageName.razor"];

        Assert.Equal("AVeryLongUniquePageName.razor", Build(tabPaths[0], tabPaths, Abbreviated, Budget(10)));
    }

    [Fact]
    public void Captions_NeverContainAnEllipsis()
    {
        foreach (var options in new[] { Collapsed, Abbreviated, CollapsedAbbreviated })
        {
            Assert.DoesNotContain("…", Build(ServiceTabs[0], ServiceTabs, options, Budget(10)));
            Assert.DoesNotContain("...", Build(ServiceTabs[0], ServiceTabs, options, Budget(10)));
        }
    }
}

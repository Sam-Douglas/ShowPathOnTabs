using Microsoft.VisualStudio.Sdk.TestFramework;

namespace ShowPathOnTabs.VsTests;

/// <summary>
/// Sets up a mocked Visual Studio environment (main thread, ThreadHelper, global service provider)
/// shared by every test in the collection.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MockedVsCollection : ICollectionFixture<GlobalServiceProvider>, ICollectionFixture<MefHostingFixture>
{
    public const string Name = "MockedVS";
}

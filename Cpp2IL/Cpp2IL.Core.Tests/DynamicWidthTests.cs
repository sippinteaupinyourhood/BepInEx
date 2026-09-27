namespace Cpp2IL.Core.Tests;

public class DynamicWidthTests
{
    [SetUp]
    public void Setup()
    {
        Cpp2IlApi.ResetInternalState();
        TestGameLoader.LoadSimpleV106Game();
    }
    
    [Test]
    public void AssertTheGameLoadedProperly()
    {
        var context = Cpp2IlApi.CurrentAppContext!;
        
        
        
        Assert.That(context, Is.Not.Null);
    }
}

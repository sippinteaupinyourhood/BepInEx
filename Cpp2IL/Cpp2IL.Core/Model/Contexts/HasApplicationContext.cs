namespace Cpp2IL.Core.Model.Contexts;




public abstract class HasApplicationContext(ApplicationAnalysisContext appContext) : ContextWithDataStorage
{
    
    
    
    public ApplicationAnalysisContext AppContext = appContext;
}

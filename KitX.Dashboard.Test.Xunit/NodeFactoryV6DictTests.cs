using KitX.Core.Contract.Workflow;
using KitX.Dashboard.Services;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Unit tests for NodeFactoryV6 (2026-08-03): the palette's dict-definition factory
/// must honour the requested DeclKind ("const" for const dict declarations — legal KS,
/// rendered by the backend — default "var" otherwise).
/// </summary>
public class NodeFactoryV6DictTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void CreateDictNewDefinitionNode_DefaultDeclKindIsVar()
    {
        var node = NodeFactoryV6.CreateDictNewDefinitionNode();
        Assert.Equal("var", node.Properties["DeclKind"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CreateDictNewDefinitionNode_ConstDeclKind_IsStored()
    {
        var node = NodeFactoryV6.CreateDictNewDefinitionNode("const");
        Assert.Equal("const", node.Properties["DeclKind"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CreateDictNewDefinitionNode_AlwaysSeedsOneKeyValuePair()
    {
        var node = NodeFactoryV6.CreateDictNewDefinitionNode();
        Assert.Equal(2, node.InputPins.Count);
        Assert.Equal("Key0", node.InputPins[0].Name);
        Assert.Equal("Value0", node.InputPins[1].Name);
    }
}

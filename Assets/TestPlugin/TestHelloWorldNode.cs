using Warudo.Core.Attributes;
using Warudo.Core.Graphs;

[NodeType(
    Id = "cb2601b1-5158-4b8f-9ade-43b7e4d05ede",
    Title = "Test Hello World",
    Category = "CATEGORY_DEBUG"
)]
public class TestHelloWorldNode : Node
{
    [DataInput]
    public string Name = "World";

    [DataOutput]
    public string Message()
    {
        return "Hello " + Name + "!";
    }
}

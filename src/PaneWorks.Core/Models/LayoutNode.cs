using System.Text.Json.Serialization;

namespace PaneWorks.Core.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LeafNode), "leaf")]
[JsonDerivedType(typeof(SplitNode), "split")]
public abstract record LayoutNode(string Id);


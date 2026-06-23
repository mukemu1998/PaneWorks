using PaneWorks.Core.Models;

namespace PaneWorks.Core.Services;

public sealed class LayoutTreeQueryService
{
    public bool IsLeaf(LayoutDocument document, string? nodeId)
    {
        return nodeId is not null && FindNode(document.Root, nodeId) is LeafNode;
    }

    public bool IsSplit(LayoutDocument document, string? nodeId)
    {
        return nodeId is not null && FindNode(document.Root, nodeId) is SplitNode;
    }

    public LayoutNode? FindNode(LayoutNode root, string nodeId)
    {
        if (root.Id == nodeId)
        {
            return root;
        }

        if (root is not SplitNode split)
        {
            return null;
        }

        return FindNode(split.First, nodeId) ?? FindNode(split.Second, nodeId);
    }

    public string? FindParentSplitId(LayoutDocument document, string nodeId)
    {
        return FindParentSplitId(document.Root, nodeId);
    }

    private string? FindParentSplitId(LayoutNode current, string nodeId)
    {
        if (current is not SplitNode split)
        {
            return null;
        }

        if (split.First.Id == nodeId || split.Second.Id == nodeId)
        {
            return split.Id;
        }

        return FindParentSplitId(split.First, nodeId) ?? FindParentSplitId(split.Second, nodeId);
    }
}

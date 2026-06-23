using PaneWorks.Core.Models;

namespace PaneWorks.Core.Services;

public sealed class LayoutEditorService
{
    public LayoutDocument CreateBlank(string name = "未命名布局")
    {
        return new LayoutDocument(1, name, new LeafNode("root"));
    }

    public EditorMutationResult<LayoutDocument> SplitLeaf(
        LayoutDocument document,
        string leafNodeId,
        SplitDirection direction)
    {
        var changed = false;
        string? nextSelectedNodeId = null;

        var root = ReplaceNode(document.Root, node =>
        {
            if (node is not LeafNode leaf || leaf.Id != leafNodeId)
            {
                return node;
            }

            changed = true;

            var firstLeaf = new LeafNode(CreateNodeId());
            var secondLeaf = new LeafNode(CreateNodeId());
            nextSelectedNodeId = firstLeaf.Id;

            return new SplitNode(
                leaf.Id,
                direction,
                0.5,
                firstLeaf,
                secondLeaf);
        });

        return new EditorMutationResult<LayoutDocument>(
            changed ? document with { Root = root } : document,
            nextSelectedNodeId ?? leafNodeId,
            changed);
    }

    public EditorMutationResult<LayoutDocument> DeleteSplit(
        LayoutDocument document,
        string splitNodeId)
    {
        var changed = false;

        var root = ReplaceNode(document.Root, node =>
        {
            if (node is not SplitNode split || split.Id != splitNodeId)
            {
                return node;
            }

            changed = true;
            return new LeafNode(split.Id);
        });

        return new EditorMutationResult<LayoutDocument>(
            changed ? document with { Root = root } : document,
            splitNodeId,
            changed);
    }

    public EditorMutationResult<LayoutDocument> SplitLeafIntoThree(
        LayoutDocument document,
        string leafNodeId,
        SplitDirection direction)
    {
        var changed = false;
        string? nextSelectedNodeId = null;

        var root = ReplaceNode(document.Root, node =>
        {
            if (node is not LeafNode leaf || leaf.Id != leafNodeId)
            {
                return node;
            }

            changed = true;

            var firstLeaf = new LeafNode(CreateNodeId());
            var middleLeaf = new LeafNode(CreateNodeId());
            var lastLeaf = new LeafNode(CreateNodeId());
            nextSelectedNodeId = firstLeaf.Id;

            return new SplitNode(
                leaf.Id,
                direction,
                1d / 3d,
                firstLeaf,
                new SplitNode(
                    CreateNodeId(),
                    direction,
                    0.5,
                    middleLeaf,
                    lastLeaf));
        });

        return new EditorMutationResult<LayoutDocument>(
            changed ? document with { Root = root } : document,
            nextSelectedNodeId ?? leafNodeId,
            changed);
    }

    public EditorMutationResult<LayoutDocument> UpdateSplitRatio(
        LayoutDocument document,
        string splitNodeId,
        double ratio)
    {
        var normalizedRatio = Math.Clamp(ratio, 0.05, 0.95);
        var changed = false;

        var root = ReplaceNode(document.Root, node =>
        {
            if (node is not SplitNode split || split.Id != splitNodeId)
            {
                return node;
            }

            if (Math.Abs(split.Ratio - normalizedRatio) < 0.0001)
            {
                return node;
            }

            changed = true;
            return split with
            {
                Ratio = normalizedRatio
            };
        });

        return new EditorMutationResult<LayoutDocument>(
            changed ? document with { Root = root } : document,
            splitNodeId,
            changed);
    }

    private static LayoutNode ReplaceNode(LayoutNode current, Func<LayoutNode, LayoutNode> replace)
    {
        var replaced = replace(current);
        if (!ReferenceEquals(replaced, current))
        {
            return replaced;
        }

        if (current is not SplitNode split)
        {
            return current;
        }

        var first = ReplaceNode(split.First, replace);
        var second = ReplaceNode(split.Second, replace);

        if (ReferenceEquals(first, split.First) && ReferenceEquals(second, split.Second))
        {
            return current;
        }

        return split with
        {
            First = first,
            Second = second
        };
    }

    private static string CreateNodeId()
    {
        return Guid.NewGuid().ToString("N");
    }
}

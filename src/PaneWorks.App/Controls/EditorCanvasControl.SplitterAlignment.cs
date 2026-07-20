using System;
using System.Collections.Generic;
using System.Linq;
using PaneWorks.Core.Models;

namespace PaneWorks.App.Controls;

public sealed partial class EditorCanvasControl
{
    private double ApplyAlignmentSnap(ComputedSplitter activeSplitter, double axisPosition)
    {
        if (_lastGeometry is null)
        {
            ClearSnapGuide();
            return axisPosition;
        }

        var candidatePositions = GetAlignmentCandidates(activeSplitter);
        if (candidatePositions.Count == 0)
        {
            ClearSnapGuide();
            return axisPosition;
        }

        var nearest = candidatePositions
            .Select(position => new { Position = position, Distance = Math.Abs(position - axisPosition) })
            .OrderBy(item => item.Distance)
            .First();

        if (nearest.Distance > SplitterSnapThreshold)
        {
            ClearSnapGuide();
            return axisPosition;
        }

        SetSnapGuide(activeSplitter.Direction, nearest.Position);
        return nearest.Position;
    }

    private List<double> GetAlignmentCandidates(ComputedSplitter activeSplitter)
    {
        if (_lastGeometry is null)
        {
            return [];
        }

        var candidates = new List<double>();

        AddWorkAreaEdgeCandidates(activeSplitter, candidates);

        foreach (var splitter in _lastGeometry.Splitters)
        {
            if (splitter.SplitNodeId == activeSplitter.SplitNodeId)
            {
                continue;
            }

            if (activeSplitter.Direction == SplitDirection.Vertical)
            {
                if (splitter.Direction == SplitDirection.Vertical)
                {
                    candidates.Add(splitter.Bounds.X + (splitter.Bounds.Width / 2));
                }
                else
                {
                    candidates.Add(splitter.HostBounds.X);
                    candidates.Add(splitter.HostBounds.X + splitter.HostBounds.Width);
                }

                continue;
            }

            if (splitter.Direction == SplitDirection.Horizontal)
            {
                candidates.Add(splitter.Bounds.Y + (splitter.Bounds.Height / 2));
            }
            else
            {
                candidates.Add(splitter.HostBounds.Y);
                candidates.Add(splitter.HostBounds.Y + splitter.HostBounds.Height);
            }
        }

        return candidates
            .OrderBy(value => value)
            .Aggregate(
                new List<double>(),
                (distinct, value) =>
                {
                    if (distinct.Count == 0 || Math.Abs(distinct[^1] - value) > 0.5)
                    {
                        distinct.Add(value);
                    }

                    return distinct;
                });
    }

    private void AddWorkAreaEdgeCandidates(ComputedSplitter activeSplitter, ICollection<double> candidates)
    {
        var stageBounds = GetDesktopStageBounds();
        if (WorkAreaBounds.Width <= 0 || WorkAreaBounds.Height <= 0)
        {
            return;
        }

        if (activeSplitter.Direction == SplitDirection.Horizontal)
        {
            if (WorkAreaBounds.Y > stageBounds.Y && WorkAreaBounds.Y < stageBounds.Y + stageBounds.Height)
            {
                candidates.Add(WorkAreaBounds.Y);
            }

            var bottom = WorkAreaBounds.Y + WorkAreaBounds.Height;
            if (bottom > stageBounds.Y && bottom < stageBounds.Y + stageBounds.Height)
            {
                candidates.Add(bottom);
            }

            return;
        }

        if (WorkAreaBounds.X > stageBounds.X && WorkAreaBounds.X < stageBounds.X + stageBounds.Width)
        {
            candidates.Add(WorkAreaBounds.X);
        }

        var right = WorkAreaBounds.X + WorkAreaBounds.Width;
        if (right > stageBounds.X && right < stageBounds.X + stageBounds.Width)
        {
            candidates.Add(right);
        }
    }

    private void SetSnapGuide(SplitDirection direction, double axisPosition)
    {
        var changed = _snapGuideDirection != direction
            || !_snapGuideAxisPosition.HasValue
            || Math.Abs(_snapGuideAxisPosition.Value - axisPosition) > 0.1;

        _snapGuideDirection = direction;
        _snapGuideAxisPosition = axisPosition;

        if (changed)
        {
            InvalidateVisual();
        }
    }

    private void ClearSnapGuide()
    {
        if (!_snapGuideAxisPosition.HasValue && _snapGuideDirection is null)
        {
            return;
        }

        _snapGuideAxisPosition = null;
        _snapGuideDirection = null;
        InvalidateVisual();
    }
}

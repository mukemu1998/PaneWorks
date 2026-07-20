using System.Windows;
using PaneWorks.App.ViewModels;
using PaneWorks.Core.Models;

namespace PaneWorks.App.Controls;

public sealed partial class EditorCanvasControl
{
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(
            nameof(Document),
            typeof(LayoutDocument),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedNodeIdProperty =
        DependencyProperty.Register(
            nameof(SelectedNodeId),
            typeof(string),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PreviewNodeIdProperty =
        DependencyProperty.Register(
            nameof(PreviewNodeId),
            typeof(string),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AvailableLayoutsProperty =
        DependencyProperty.Register(
            nameof(AvailableLayouts),
            typeof(IEnumerable<LayoutListItemViewModel>),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ActiveSnapLayoutIdProperty =
        DependencyProperty.Register(
            nameof(ActiveSnapLayoutId),
            typeof(string),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty AvailableWorkspaceProfilesProperty =
        DependencyProperty.Register(
            nameof(AvailableWorkspaceProfiles),
            typeof(IEnumerable<LayoutListItemViewModel>),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ActiveWorkspaceProfileIdProperty =
        DependencyProperty.Register(
            nameof(ActiveWorkspaceProfileId),
            typeof(string),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty StageBoundsProperty =
        DependencyProperty.Register(
            nameof(StageBounds),
            typeof(PaneRect),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(default(PaneRect), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WorkAreaBoundsProperty =
        DependencyProperty.Register(
            nameof(WorkAreaBounds),
            typeof(PaneRect),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(default(PaneRect), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EditingDisplayNameProperty =
        DependencyProperty.Register(
            nameof(EditingDisplayName),
            typeof(string),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ReferenceLayoutsProperty =
        DependencyProperty.Register(
            nameof(ReferenceLayouts),
            typeof(IEnumerable<EditorReferenceLayout>),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsLayoutEditingEnabledProperty =
        DependencyProperty.Register(
            nameof(IsLayoutEditingEnabled),
            typeof(bool),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowWorkspaceBindingMarkersProperty =
        DependencyProperty.Register(
            nameof(ShowWorkspaceBindingMarkers),
            typeof(bool),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BindingDisplayIdProperty =
        DependencyProperty.Register(
            nameof(BindingDisplayId),
            typeof(string),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WorkspaceWindowBindingsProperty =
        DependencyProperty.Register(
            nameof(WorkspaceWindowBindings),
            typeof(IEnumerable<WorkspaceWindowBinding>),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public LayoutDocument? Document
    {
        get => (LayoutDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public string? SelectedNodeId
    {
        get => (string?)GetValue(SelectedNodeIdProperty);
        set => SetValue(SelectedNodeIdProperty, value);
    }

    public string? PreviewNodeId
    {
        get => (string?)GetValue(PreviewNodeIdProperty);
        set => SetValue(PreviewNodeIdProperty, value);
    }

    public IEnumerable<LayoutListItemViewModel>? AvailableLayouts
    {
        get => (IEnumerable<LayoutListItemViewModel>?)GetValue(AvailableLayoutsProperty);
        set => SetValue(AvailableLayoutsProperty, value);
    }

    public string ActiveSnapLayoutId
    {
        get => (string)GetValue(ActiveSnapLayoutIdProperty);
        set => SetValue(ActiveSnapLayoutIdProperty, value);
    }

    public IEnumerable<LayoutListItemViewModel>? AvailableWorkspaceProfiles
    {
        get => (IEnumerable<LayoutListItemViewModel>?)GetValue(AvailableWorkspaceProfilesProperty);
        set => SetValue(AvailableWorkspaceProfilesProperty, value);
    }

    public string ActiveWorkspaceProfileId
    {
        get => (string)GetValue(ActiveWorkspaceProfileIdProperty);
        set => SetValue(ActiveWorkspaceProfileIdProperty, value);
    }

    public PaneRect StageBounds
    {
        get => (PaneRect)GetValue(StageBoundsProperty);
        set => SetValue(StageBoundsProperty, value);
    }

    public PaneRect WorkAreaBounds
    {
        get => (PaneRect)GetValue(WorkAreaBoundsProperty);
        set => SetValue(WorkAreaBoundsProperty, value);
    }

    public string EditingDisplayName
    {
        get => (string)GetValue(EditingDisplayNameProperty);
        set => SetValue(EditingDisplayNameProperty, value);
    }

    public IEnumerable<EditorReferenceLayout>? ReferenceLayouts
    {
        get => (IEnumerable<EditorReferenceLayout>?)GetValue(ReferenceLayoutsProperty);
        set => SetValue(ReferenceLayoutsProperty, value);
    }

    public bool IsLayoutEditingEnabled
    {
        get => (bool)GetValue(IsLayoutEditingEnabledProperty);
        set => SetValue(IsLayoutEditingEnabledProperty, value);
    }

    public bool ShowWorkspaceBindingMarkers
    {
        get => (bool)GetValue(ShowWorkspaceBindingMarkersProperty);
        set => SetValue(ShowWorkspaceBindingMarkersProperty, value);
    }

    public string BindingDisplayId
    {
        get => (string)GetValue(BindingDisplayIdProperty);
        set => SetValue(BindingDisplayIdProperty, value);
    }

    public IEnumerable<WorkspaceWindowBinding>? WorkspaceWindowBindings
    {
        get => (IEnumerable<WorkspaceWindowBinding>?)GetValue(WorkspaceWindowBindingsProperty);
        set => SetValue(WorkspaceWindowBindingsProperty, value);
    }
}

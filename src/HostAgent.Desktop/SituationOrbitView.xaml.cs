using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace HostAgent.Desktop;

public partial class SituationOrbitView : UserControl
{
    private static readonly SituationSubsystem[] SubsystemOrder =
    [
        SituationSubsystem.Cpu,
        SituationSubsystem.Memory,
        SituationSubsystem.Disks,
        SituationSubsystem.Network,
        SituationSubsystem.Services
    ];

    private readonly Dictionary<SituationSubsystem, Button> _buttons = [];
    private readonly Dictionary<SituationSubsystem, Button> _textButtons = [];
    private readonly Dictionary<SituationSubsystem, Ellipse> _statusDots = [];
    private readonly Dictionary<SituationSubsystem, Ellipse> _textStatusDots = [];
    private readonly Line _selectionLink = new() { StrokeThickness = 2, Visibility = Visibility.Collapsed };
    private SituationOrbitSnapshot? _snapshot;
    private SituationSubsystem? _selection = SituationSubsystem.Cpu;
    private Button? _selectionOrigin;
    private bool _detailExpanded = true;
    private bool _isCompact = true;

    public SituationOrbitView()
    {
        InitializeComponent();
        AddNode(SituationSubsystem.Cpu, 0, 1);
        AddNode(SituationSubsystem.Memory, 1, 2);
        AddNode(SituationSubsystem.Disks, 2, 2);
        AddNode(SituationSubsystem.Network, 2, 0);
        AddNode(SituationSubsystem.Services, 1, 0);
        _selectionLink.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        Links.Children.Add(_selectionLink);
        Map.LayoutUpdated += (_, _) => PositionLink();
        Body.SizeChanged += (_, _) => ArrangeDetail();
        Loaded += (_, _) => ArrangeDetail();
    }

    internal void Render(SituationOrbitSnapshot snapshot)
    {
        _snapshot = snapshot;
        Heading.Text = snapshot.Title;
        Description.Text = snapshot.Description;
        CenterStatus.Text = snapshot.Status;
        Source.Text = snapshot.Source;
        foreach (var node in snapshot.Nodes)
        {
            UpdateLabel(_buttons[node.Id], node);
            UpdateLabel(_textButtons[node.Id], node);
            SetDotState(_statusDots[node.Id], node.DataState);
            SetDotState(_textStatusDots[node.Id], node.DataState);
        }
        RenderSelection();
    }

    private void AddNode(SituationSubsystem id, int row, int column)
    {
        Button CreateButton(bool isOrbitNode)
        {
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Margin = new Thickness(0, 4, 8, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            if (isOrbitNode) _statusDots.Add(id, dot);
            else _textStatusDots.Add(id, dot);

            var label = new StackPanel();
            var title = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            var status = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            };
            status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            label.Children.Add(title);
            label.Children.Add(status);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(dot);
            content.Children.Add(label);

            var nodeButton = new Button
            {
                Content = content,
                Margin = new Thickness(4),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = id
            };
            nodeButton.SetResourceReference(FrameworkElement.FocusVisualStyleProperty, "FocusRing");
            nodeButton.Click += (_, _) => Select(id, nodeButton, focus: false);
            if (isOrbitNode)
            {
                nodeButton.PreviewKeyDown += (_, e) => OrbitNodeKeyDown(id, e);
                Grid.SetRow(nodeButton, row);
                Grid.SetColumn(nodeButton, column);
                Map.Children.Add(nodeButton);
                _buttons.Add(id, nodeButton);
            }
            else
            {
                TextNodes.Children.Add(nodeButton);
                _textButtons.Add(id, nodeButton);
            }
            return nodeButton;
        }

        _ = CreateButton(isOrbitNode: true);
        _ = CreateButton(isOrbitNode: false);
    }

    private static void UpdateLabel(Button button, SituationOrbitNode node)
    {
        if (button.Content is not StackPanel { Children.Count: 2 } content ||
            content.Children[1] is not StackPanel label || label.Children.Count < 2 ||
            label.Children[0] is not TextBlock title || label.Children[1] is not TextBlock status)
            return;
        title.Text = node.Title;
        status.Text = node.Status;
        AutomationProperties.SetName(button, node.Title + ", " + node.Status);
        AutomationProperties.SetItemStatus(button, node.Status);
    }

    private void SetDotState(Ellipse dot, SituationDataState state)
    {
        var brushKey = state switch
        {
            SituationDataState.Available => "GreenBrush",
            SituationDataState.Partial or SituationDataState.Limited => "WarningBrush",
            SituationDataState.Unknown or SituationDataState.Missing or SituationDataState.Stale or SituationDataState.Offline => "ErrorBrush",
            _ => "MutedBrush"
        };
        dot.SetResourceReference(Shape.FillProperty, brushKey);
    }

    private void RenderSelection()
    {
        var selected = _snapshot?.Nodes.FirstOrDefault(node => node.Id == _selection);
        foreach (var (id, button) in _buttons)
        {
            var isSelected = id == _selection;
            button.IsTabStop = isSelected;
            button.SetResourceReference(Control.BackgroundProperty, "CardBrush");
            button.SetResourceReference(Control.ForegroundProperty, "InkBrush");
            button.SetResourceReference(Control.BorderBrushProperty, isSelected ? "AccentBrush" : "InputBorderBrush");
            button.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
            AutomationProperties.SetItemStatus(button, isSelected ? _snapshot?.SelectedLabel ?? "" : "");
        }
        foreach (var (id, button) in _textButtons)
        {
            var isSelected = id == _selection;
            button.SetResourceReference(Control.BackgroundProperty, "CardBrush");
            button.SetResourceReference(Control.ForegroundProperty, "InkBrush");
            button.SetResourceReference(Control.BorderBrushProperty, isSelected ? "AccentBrush" : "InputBorderBrush");
            button.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
            AutomationProperties.SetItemStatus(button, isSelected ? _snapshot?.SelectedLabel ?? "" : "");
        }

        var visible = selected is not null && (_isCompact || _detailExpanded);
        Detail.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        DetailContent.Visibility = selected is not null && _detailExpanded ? Visibility.Visible : Visibility.Collapsed;
        _selectionLink.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
        if (selected is null)
        {
            DetailTitle.Text = "";
            DetailStatus.Text = "";
            DetailSummary.Text = "";
            DetailRows.ItemsSource = null;
            ApplyDetailToggleState();
            return;
        }

        DetailTitle.Text = selected.Title;
        DetailStatus.Text = selected.Status;
        DetailSummary.Text = selected.Summary;
        DetailRows.ItemsSource = selected.Details;
        ApplyDetailToggleState();
        PositionLink();
    }

    private void ApplyDetailToggleState()
    {
        var labelKey = _isCompact
            ? _detailExpanded ? "OrbitCollapseDetail" : "OrbitExpandDetail"
            : "OrbitCloseDetail";
        var label = _snapshot is null ? labelKey : labelKey == "OrbitExpandDetail"
            ? _snapshot.ExpandDetailLabel
            : labelKey == "OrbitCollapseDetail"
                ? _snapshot.CollapseDetailLabel
                : _snapshot.CloseDetailLabel;
        CloseDetail.Content = _isCompact ? (_detailExpanded ? "⌃" : "⌄") : "×";
        CloseDetail.ToolTip = label;
        AutomationProperties.SetName(CloseDetail, label);
        AutomationProperties.SetItemStatus(CloseDetail, _detailExpanded ? label : _snapshot?.ExpandDetailLabel ?? label);
        AutomationProperties.SetHelpText(CloseDetail, label);
    }

    private void ArrangeDetail()
    {
        var compact = Body.ActualWidth < 940;
        var modeChanged = compact != _isCompact;
        var focusWasInDetail = Detail.IsKeyboardFocusWithin;
        _isCompact = compact;
        DetailColumn.Width = !compact && _detailExpanded ? new GridLength(300) : new GridLength(0);
        Grid.SetColumn(Detail, compact ? 0 : 1);
        Grid.SetRow(Detail, compact ? 1 : 0);
        Detail.Margin = compact ? new Thickness(0, 10, 0, 0) : new Thickness(12, 0, 0, 0);
        RenderSelection();

        if (modeChanged && focusWasInDetail && (!compact && !_detailExpanded))
        {
            FocusOrbitSelection();
        }
        else if (modeChanged && focusWasInDetail && compact && _detailExpanded)
        {
            CloseDetail.Focus();
        }
    }

    private void Select(SituationSubsystem id, Button origin, bool focus)
    {
        if (_snapshot is null || !_snapshot.Nodes.Any(node => node.Id == id)) return;
        _selection = id;
        _selectionOrigin = origin;
        _detailExpanded = true;
        ArrangeDetail();
        var node = _snapshot!.Nodes.First(item => item.Id == id);
        SelectionAnnouncement.Text = string.Format(
            _snapshot.SelectionAnnouncementTemplate, node.Title, node.Status);
        if (focus) origin.Focus();
        if (_isCompact) Detail.BringIntoView();
    }

    private void OrbitNodeKeyDown(SituationSubsystem current, KeyEventArgs e)
    {
        var direction = e.Key switch
        {
            Key.Right or Key.Down => 1,
            Key.Left or Key.Up => -1,
            _ => 0
        };
        if (direction == 0) return;
        e.Handled = true;
        var currentIndex = Array.IndexOf(SubsystemOrder, _selection ?? current);
        var next = SubsystemOrder[(currentIndex + direction + SubsystemOrder.Length) % SubsystemOrder.Length];
        Select(next, _buttons[next], focus: true);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key != Key.Escape || !_detailExpanded) return;
        SetDetailExpanded(false);
        e.Handled = true;
    }

    private void ToggleDetailClick(object sender, RoutedEventArgs e) => SetDetailExpanded(!_detailExpanded);

    private void SetDetailExpanded(bool expanded)
    {
        if (_selection is null || _detailExpanded == expanded) return;
        _detailExpanded = expanded;
        ArrangeDetail();
        if (expanded)
        {
            CloseDetail.Focus();
            return;
        }

        if (_isCompact)
        {
            CloseDetail.Focus();
            return;
        }
        FocusSelectionOriginOrOrbit();
    }

    private void FocusSelectionOriginOrOrbit()
    {
        if (_selection is not { } id) return;
        var origin = _selectionOrigin;
        if (origin is { IsVisible: true, IsEnabled: true } && origin.IsLoaded)
        {
            origin.Focus();
            return;
        }
        FocusOrbitSelection();
    }

    private void FocusOrbitSelection()
    {
        if (_selection is { } id && _buttons.TryGetValue(id, out var button) && button.IsVisible)
            button.Focus();
    }

    private void PositionLink()
    {
        if (_selection is not { } id || !Center.IsArrangeValid || !_buttons[id].IsArrangeValid) return;
        var button = _buttons[id];
        var from = Center.TranslatePoint(new Point(Center.ActualWidth / 2, Center.ActualHeight / 2), Links);
        var to = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), Links);
        _selectionLink.X1 = from.X;
        _selectionLink.Y1 = from.Y;
        _selectionLink.X2 = to.X;
        _selectionLink.Y2 = to.Y;
    }
}

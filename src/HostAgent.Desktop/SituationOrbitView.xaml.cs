using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HostAgent.Desktop;

public partial class SituationOrbitView : UserControl
{
    private readonly Dictionary<SituationSubsystem, Button> _buttons = [];
    private readonly Dictionary<SituationSubsystem, Button> _textButtons = [];
    private readonly Dictionary<SituationSubsystem, Line> _spokes = [];
    private readonly Line _selectionLink = new() { StrokeThickness = 2, Visibility = Visibility.Collapsed };
    private SituationOrbitSnapshot? _snapshot;
    private SituationSubsystem? _selection;
    private Button? _selectionOrigin;
    internal event Action<SituationOrbitNode?>? SelectedNodeChanged;
    internal SituationOrbitNode? SelectedNode => _snapshot?.Nodes.FirstOrDefault(node => node.Id == _selection);

    public SituationOrbitView()
    {
        InitializeComponent();
        AddNode(SituationSubsystem.Cpu);
        AddNode(SituationSubsystem.Memory);
        AddNode(SituationSubsystem.Disks);
        AddNode(SituationSubsystem.Network);
        AddNode(SituationSubsystem.Services);
        _selection = SituationSubsystem.Cpu;
        foreach(var id in _buttons.Keys)
        {
            var spoke=new Line{StrokeThickness=1,Opacity=0.55,IsHitTestVisible=false,Visibility=Visibility.Collapsed};
            spoke.SetResourceReference(Shape.StrokeProperty,"LineBrush");
            _spokes.Add(id,spoke);
            Links.Children.Add(spoke);
        }
        _selectionLink.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        Links.Children.Add(_selectionLink);
        Map.SizeChanged += (_, _) => PositionOrbit();
        Map.Loaded += (_, _) => PositionOrbit();
        RenderSelection();
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
        }
        RenderSelection();
    }

    private void AddNode(SituationSubsystem id)
    {
        Button CreateButton()
        {
            var nodeButton = new Button
            {
                Content = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                Tag = id,
                Margin = new Thickness(4),
                Padding = new Thickness(5),
                FontSize = 13,
                Width = 104,
                MinHeight = 58,
                VerticalAlignment = VerticalAlignment.Center
            };
            nodeButton.Click += (_, _) =>
            {
                _selection = id;
                _selectionOrigin = nodeButton;
                RenderSelection();
            };
            return nodeButton;
        }
        var button = CreateButton();
        button.SizeChanged += (_, _) => PositionOrbit();
        OrbitNodes.Children.Add(button);
        _buttons.Add(id, button);
        var textButton = CreateButton();
        TextNodes.Children.Add(textButton);
        _textButtons.Add(id, textButton);
    }

    private static void UpdateLabel(Button button, SituationOrbitNode node)
    {
        var value = string.IsNullOrWhiteSpace(node.Summary) ? node.Status : node.Summary + Environment.NewLine + node.Status;
        ((TextBlock)button.Content).Text = node.Title + Environment.NewLine + value;
        AutomationProperties.SetName(button, string.Join(", ", new[] { node.Title, node.Summary, node.Status }.Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private void RenderSelection()
    {
        var selected = _snapshot?.Nodes.FirstOrDefault(node => node.Id == _selection);
        foreach (var (id, button) in _buttons.Concat(_textButtons))
        {
            button.SetResourceReference(Control.BackgroundProperty, id == _selection ? "AccentBrush" : "CardBrush");
            button.SetResourceReference(Control.ForegroundProperty, id == _selection ? "AccentInkBrush" : "InkBrush");
            AutomationProperties.SetItemStatus(button, id == _selection ? _snapshot?.SelectedLabel ?? "" : "");
        }
        _selectionLink.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
        if (selected is not null) PositionOrbit();
        SelectedNodeChanged?.Invoke(selected);
    }

    private void PositionOrbit()
    {
        if (!Center.IsArrangeValid || Map.ActualWidth <= 0 || Map.ActualHeight <= 0) return;
        var centerX = Map.ActualWidth / 2;
        var centerY = Map.ActualHeight / 2;
        var radiusX = Math.Max(0, Map.ActualWidth / 2 - 62);
        var radiusY = Math.Max(0, Map.ActualHeight / 2 - 41);
        var angles = new Dictionary<SituationSubsystem, double>
        {
            [SituationSubsystem.Cpu] = -90,
            [SituationSubsystem.Memory] = -18,
            [SituationSubsystem.Disks] = 54,
            [SituationSubsystem.Network] = 126,
            [SituationSubsystem.Services] = 198
        };
        foreach (var (id, button) in _buttons)
        {
            if (button.ActualWidth <= 0 || button.ActualHeight <= 0) return;
            var angle = angles[id] * Math.PI / 180;
            var x = centerX + Math.Cos(angle) * radiusX - button.ActualWidth / 2;
            var y = centerY + Math.Sin(angle) * radiusY - button.ActualHeight / 2;
            var currentX = Canvas.GetLeft(button);
            var currentY = Canvas.GetTop(button);
            if (double.IsNaN(currentX) || Math.Abs(currentX - x) > 0.5) Canvas.SetLeft(button, x);
            if (double.IsNaN(currentY) || Math.Abs(currentY - y) > 0.5) Canvas.SetTop(button, y);
        }
        var from = Center.TranslatePoint(new Point(Center.ActualWidth / 2, Center.ActualHeight / 2), Links);
        foreach(var (id,spoke) in _spokes)
        {
            var angle = angles[id] * Math.PI / 180;
            var x2 = centerX + Math.Cos(angle) * radiusX;
            var y2 = centerY + Math.Sin(angle) * radiusY;
            spoke.X1=from.X;spoke.Y1=from.Y;spoke.X2=x2;spoke.Y2=y2;
            spoke.Visibility=Visibility.Visible;
        }
        if(_selection is not { } selectedSubsystem)return;
        var selectedAngle = angles[selectedSubsystem] * Math.PI / 180;
        _selectionLink.X1 = from.X;
        _selectionLink.Y1 = from.Y;
        _selectionLink.X2 = centerX + Math.Cos(selectedAngle) * radiusX;
        _selectionLink.Y2 = centerY + Math.Sin(selectedAngle) * radiusY;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key != Key.Escape || _selection is null) return;
        CloseSelection();
        e.Handled = true;
    }

    private void CloseSelection()
    {
        if (_selection is not { } id) return;
        _selection = null;
        RenderSelection();
        (_selectionOrigin is { IsVisible: true } origin ? origin : _buttons[id]).Focus();
        _selectionOrigin = null;
    }
}

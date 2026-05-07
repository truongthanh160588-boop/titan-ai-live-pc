using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TitanAILivePC;

public partial class RegionSelectorWindow : Window
{
    private Point _start;
    private bool _isDragging;
    public Rect? SelectedRegion { get; private set; }

    public RegionSelectorWindow()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        WindowState = WindowState.Normal;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
    }

    private void RootCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(RootCanvas);
        _isDragging = true;
        SelectionRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRect, _start.X);
        Canvas.SetTop(SelectionRect, _start.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        Mouse.Capture(RootCanvas);
    }

    private void RootCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var current = e.GetPosition(RootCanvas);
        var x = Math.Min(_start.X, current.X);
        var y = Math.Min(_start.Y, current.Y);
        var width = Math.Abs(current.X - _start.X);
        var height = Math.Abs(current.Y - _start.Y);
        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = width;
        SelectionRect.Height = height;
    }

    private void RootCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        Mouse.Capture(null);

        var left = Canvas.GetLeft(SelectionRect);
        var top = Canvas.GetTop(SelectionRect);
        var width = SelectionRect.Width;
        var height = SelectionRect.Height;

        if (width < 30 || height < 20)
        {
            DialogResult = false;
            Close();
            return;
        }

        var source = PresentationSource.FromVisual(this);
        var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        var absoluteLeft = (Left + left) * dpiX;
        var absoluteTop = (Top + top) * dpiY;
        SelectedRegion = new Rect(absoluteLeft, absoluteTop, width * dpiX, height * dpiY);
        DialogResult = true;
        Close();
    }
}

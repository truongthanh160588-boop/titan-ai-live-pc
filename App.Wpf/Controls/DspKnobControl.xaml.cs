using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace TitanAILivePC.Controls;

public partial class DspKnobControl : UserControl
{
    private const double StartAngle = -135;
    private const double SweepAngle = 270;
    private const double KnobCenter = 48;
    private const double ArcRadius = 34.5;
    private const double PointerRadius = 19.8;
    private const double RenderAngleOffset = -90; // logical 0° at 12 o'clock

    private readonly List<Line> _tickMarks = new();
    private Point _dragStart;
    private double _valueStart;
    private bool _isDragging;

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(DspKnobControl),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(DspKnobControl), new PropertyMetadata(0d));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(DspKnobControl), new PropertyMetadata(100d));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(DspKnobControl), new PropertyMetadata("KNOB"));

    public static readonly DependencyProperty AccentColorProperty =
        DependencyProperty.Register(nameof(AccentColor), typeof(Brush), typeof(DspKnobControl), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(79, 163, 255))));

    public static readonly DependencyProperty AnimatedValueProperty =
        DependencyProperty.Register(nameof(AnimatedValue), typeof(double), typeof(DspKnobControl), new PropertyMetadata(0d, OnAnimatedValueChanged));

    public static readonly DependencyProperty PercentageTextProperty =
        DependencyProperty.Register(nameof(PercentageText), typeof(string), typeof(DspKnobControl), new PropertyMetadata("0%"));

    public DspKnobControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            BuildTicks();
            UpdateComputed(Value);
            AnimatedValue = Value;
        };

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public Brush AccentColor
    {
        get => (Brush)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public double AnimatedValue
    {
        get => (double)GetValue(AnimatedValueProperty);
        set => SetValue(AnimatedValueProperty, value);
    }

    public string PercentageText
    {
        get => (string)GetValue(PercentageTextProperty);
        set => SetValue(PercentageTextProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DspKnobControl knob)
        {
            return;
        }

        var target = Math.Clamp((double)e.NewValue, knob.Minimum, knob.Maximum);
        if (!target.Equals((double)e.NewValue))
        {
            knob.Value = target;
            return;
        }

        var animation = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        knob.BeginAnimation(AnimatedValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnAnimatedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DspKnobControl knob)
        {
            knob.UpdateComputed((double)e.NewValue);
        }
    }

    private void UpdateComputed(double value)
    {
        var range = Math.Max(Maximum - Minimum, 0.0001);
        var normalized = Math.Clamp((value - Minimum) / range, 0, 1);
        var currentAngle = StartAngle + normalized * SweepAngle;

        PercentageText = $"{normalized * 100:0}%";
        UpdatePointer(currentAngle);
        UpdateArcs(normalized);
        UpdateTickState(normalized);
    }

    private void UpdatePointer(double angle)
    {
        var radians = ToRenderRadians(angle);
        PointerLine.X2 = KnobCenter + PointerRadius * Math.Cos(radians);
        PointerLine.Y2 = KnobCenter + PointerRadius * Math.Sin(radians);
    }

    private void UpdateArcs(double normalized)
    {
        BackgroundArc.Data = BuildArcGeometry(StartAngle, SweepAngle, ArcRadius);

        var (tailColor, headColor) = GetAccentPalette();
        ActiveTailArc.Stroke = new SolidColorBrush(tailColor);
        ActiveMidArc.Stroke = new SolidColorBrush(headColor);
        ActiveHeadArc.Stroke = new SolidColorBrush(headColor);
        ActiveHeadArcGlow.Stroke = new SolidColorBrush(headColor);

        if (normalized <= 0)
        {
            ActiveTailArc.Data = null;
            ActiveMidArc.Data = null;
            ActiveHeadArc.Data = null;
            ActiveHeadArcGlow.Data = null;
            HotArc.Data = null;
            HotArcGlow.Data = null;
            return;
        }

        var fullActiveSweep = normalized * SweepAngle;
        var hotThreshold = 0.90;
        var hotStartAngle = StartAngle + hotThreshold * SweepAngle;
        var midStartAngle = StartAngle + fullActiveSweep * 0.42;
        var midSweep = Math.Max(0, fullActiveSweep * 0.58);
        var headSweep = Math.Min(26, Math.Max(8, fullActiveSweep * 0.2));
        var headStart = StartAngle + Math.Max(0, fullActiveSweep - headSweep);

        ActiveTailArc.Data = BuildArcGeometry(StartAngle, fullActiveSweep, ArcRadius);
        ActiveMidArc.Data = BuildArcGeometry(midStartAngle, midSweep, ArcRadius);
        ActiveHeadArc.Data = BuildArcGeometry(headStart, headSweep, ArcRadius);
        ActiveHeadArcGlow.Data = BuildArcGeometry(headStart, headSweep, ArcRadius);

        var brightBoost = normalized is > 0.70 and <= 0.90 ? 0.08 : 0.0;
        ActiveTailArc.Opacity = 0.42 + brightBoost;
        ActiveMidArc.Opacity = 0.72 + brightBoost;
        ActiveHeadArc.Opacity = 0.95;
        ActiveHeadArcGlow.Opacity = 0.34 + brightBoost;

        if (normalized <= hotThreshold)
        {
            HotArc.Data = null;
            HotArcGlow.Data = null;
            return;
        }

        var hotSweep = (normalized - hotThreshold) * SweepAngle;
        HotArc.Data = BuildArcGeometry(hotStartAngle, hotSweep, ArcRadius);
        HotArcGlow.Data = BuildArcGeometry(hotStartAngle, hotSweep, ArcRadius);
    }

    private void BuildTicks()
    {
        TickCanvas.Children.Clear();
        _tickMarks.Clear();

        const int totalTicks = 28;
        const double majorOuter = 44.5;
        const double majorInner = 41;
        const double minorOuter = 43.5;
        const double minorInner = 41.5;

        for (var i = 0; i < totalTicks; i++)
        {
            var isMajor = i % 7 == 0;
            var angle = StartAngle + i * (SweepAngle / (totalTicks - 1));
            var radians = ToRenderRadians(angle);

            var outer = isMajor ? majorOuter : minorOuter;
            var inner = isMajor ? majorInner : minorInner;

            var tick = new Line
            {
                X1 = KnobCenter + inner * Math.Cos(radians),
                Y1 = KnobCenter + inner * Math.Sin(radians),
                X2 = KnobCenter + outer * Math.Cos(radians),
                Y2 = KnobCenter + outer * Math.Sin(radians),
                StrokeThickness = isMajor ? 1.2 : 0.9,
                Stroke = new SolidColorBrush(Color.FromRgb(50, 63, 82)),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = 0.35
            };

            _tickMarks.Add(tick);
            TickCanvas.Children.Add(tick);
        }
    }

    private void UpdateTickState(double normalized)
    {
        if (_tickMarks.Count == 0)
        {
            return;
        }

        var activeCount = (int)Math.Round(normalized * (_tickMarks.Count - 1));
        var (_, accent) = GetAccentPalette();
        var inactive = Color.FromRgb(50, 63, 82);

        for (var i = 0; i < _tickMarks.Count; i++)
        {
            var tick = _tickMarks[i];
            var brush = tick.Stroke as SolidColorBrush ?? new SolidColorBrush(inactive);
            tick.Stroke = brush;

            var targetColor = i <= activeCount ? accent : inactive;
            var relative = activeCount > 0 ? (double)i / activeCount : 0;
            var headBias = i <= activeCount ? 0.35 + (relative * 0.4) : 0.0;
            var targetOpacity = i <= activeCount ? Math.Min(0.88, 0.38 + headBias) : 0.35;

            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
            {
                To = targetColor,
                Duration = TimeSpan.FromMilliseconds(120),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });

            tick.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(120),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }
    }

    private static Geometry BuildArcGeometry(double startAngle, double sweepAngle, double radius)
    {
        if (Math.Abs(sweepAngle) < 0.001)
        {
            return Geometry.Empty;
        }

        var start = ToPoint(startAngle, radius);
        var end = ToPoint(startAngle + sweepAngle, radius);
        var isLarge = Math.Abs(sweepAngle) > 180;

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = isLarge
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Point ToPoint(double angleDegrees, double radius)
    {
        var radians = ToRenderRadians(angleDegrees);
        return new Point(
            KnobCenter + radius * Math.Cos(radians),
            KnobCenter + radius * Math.Sin(radians));
    }

    private static double ToRenderRadians(double logicalAngleDegrees)
    {
        return (logicalAngleDegrees + RenderAngleOffset) * Math.PI / 180.0;
    }

    private (Color Tail, Color Head) GetAccentPalette()
    {
        if (AccentColor is SolidColorBrush sb)
        {
            var color = sb.Color;
            var isGold = color.R > 220 && color.G > 150 && color.B < 120;
            if (isGold)
            {
                return (Color.FromRgb(255, 177, 0), Color.FromRgb(255, 200, 74));
            }

            return (Color.FromRgb(60, 123, 255), Color.FromRgb(78, 163, 255));
        }

        return (Color.FromRgb(60, 123, 255), Color.FromRgb(78, 163, 255));
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        CaptureMouse();
        _isDragging = true;
        _dragStart = e.GetPosition(this);
        _valueStart = Value;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var current = e.GetPosition(this);
        var dy = _dragStart.Y - current.Y;
        var dx = current.X - _dragStart.X;
        var movement = dy + (dx * 0.35);
        var stepPerPixel = (Maximum - Minimum) / 220.0;
        ApplyDelta(movement * stepPerPixel);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ReleaseMouseCapture();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var range = Maximum - Minimum;
        var stepPercent = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 5.0 :
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 1.0;
        var step = range * (stepPercent / 100.0);
        ApplyDelta((e.Delta > 0 ? 1 : -1) * step);
        e.Handled = true;
    }

    private void ApplyDelta(double delta)
    {
        var next = Math.Clamp((_isDragging ? _valueStart : Value) + delta, Minimum, Maximum);
        Value = next;
        if (_isDragging)
        {
            _valueStart = next;
            _dragStart = Mouse.GetPosition(this);
        }
    }
}

using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using TitanAILivePC.Models;
using TitanAILivePC.ViewModels;

namespace TitanAILivePC;

public partial class MainWindow : Window
{
#pragma warning disable IDE0044 // Assigned in ctor try block; compiler cannot prove definite assignment before DataContext bind.
    private MainViewModel _viewModel = null!;
#pragma warning restore IDE0044

    public MainWindow()
    {
        try
        {
            StartupDiagnostics.Write("MainWindow: InitializeComponent begin");
            InitializeComponent();
            StartupDiagnostics.Write("MainWindow: InitializeComponent OK");
            _viewModel = new MainViewModel();
            StartupDiagnostics.Write("MainWindow: MainViewModel ctor OK");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Write($"MainWindow ctor FAILED: {ex}");
            try
            {
                MessageBox.Show(
                    $"Không khởi tạo được cửa sổ chính:\n{ex.Message}\n\nXem log:\n{StartupDiagnostics.LogFilePath}",
                    "TITAN AI LIVE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // ignored
            }

            throw;
        }

        DataContext = _viewModel;
        _viewModel.SetChatRegionSelector(SelectChatRegionAsync);
        _viewModel.SetEngineerModePasswordGate(() =>
        {
            var dlg = new EngineerPasswordDialog(_viewModel.VerifyEngineerPassword) { Owner = this };
            return Task.FromResult(dlg.ShowDialog() == true);
        });

        _viewModel.Comments.CollectionChanged += LiveComments_CollectionChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += OnMainWindowLoaded;
        ContentRendered += OnMainWindowContentRendered;
        Closing += OnMainWindowClosing;
    }

    private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        SyncObsPasswordBoxes();
    }

    private void OnMainWindowContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnMainWindowContentRendered;
        SyncObsPasswordBoxes();
        _viewModel.SetStartupPhaseUiReady();
        Dispatcher.BeginInvoke(
            () => _ = _viewModel.SafeInitializeAsync(),
            DispatcherPriority.Background);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentUiMode))
        {
            Dispatcher.BeginInvoke(SyncObsPasswordBoxes, DispatcherPriority.Background);
        }
    }

    private void SyncObsPasswordBoxes()
    {
        var pwd = _viewModel.ObsPassword;
        if (!string.Equals(ObsPasswordBox.Password, pwd, StringComparison.Ordinal))
        {
            ObsPasswordBox.Password = pwd;
        }

        if (!string.Equals(LiveObsPasswordBox.Password, pwd, StringComparison.Ordinal))
        {
            LiveObsPasswordBox.Password = pwd;
        }
    }

    private void LiveComments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_viewModel.IsLiveMode ||
            e.Action != NotifyCollectionChangedAction.Add ||
            e.NewItems is null ||
            e.NewItems.Count == 0 ||
            e.NewItems[0] is not LiveComment comment)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                LiveModeCommentsList.ScrollIntoView(comment);
                LiveModeCommentsList.UpdateLayout();
            },
            DispatcherPriority.Background);
    }

    private void LiveObsPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.ObsPassword = LiveObsPasswordBox.Password;
    }

    private void ApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenAiApiKey = ApiKeyPasswordBox.Password;
    }

    private void ObsPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.ObsPassword = ObsPasswordBox.Password;
    }

    private void DspSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Slider slider)
        {
            return;
        }

        var baseStep = (slider.Maximum - slider.Minimum) / 120.0;
        if (baseStep <= 0)
        {
            baseStep = 0.5;
        }

        var modifiers = Keyboard.Modifiers;
        var multiplier = modifiers.HasFlag(ModifierKeys.Control) ? 4.0 :
            modifiers.HasFlag(ModifierKeys.Shift) ? 0.25 : 1.0;

        var deltaStep = baseStep * multiplier * (e.Delta > 0 ? 1 : -1);
        var target = Math.Clamp(slider.Value + deltaStep, slider.Minimum, slider.Maximum);

        var animation = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) => slider.Value = target;
        slider.BeginAnimation(Slider.ValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
        e.Handled = true;
    }

    private void DspSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is not Slider { Tag: Border knobBorder })
        {
            return;
        }

        var pulseColor = (Color)ColorConverter.ConvertFromString("#F5C542");
        var idleColor = (Color)ColorConverter.ConvertFromString("#4A5E7D");
        // Style brushes can be shared/frozen. Always use a local mutable brush for animation.
        var brush = new SolidColorBrush(pulseColor);
        knobBorder.BorderBrush = brush;

        var borderPulse = new ColorAnimation
        {
            From = pulseColor,
            To = idleColor,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, borderPulse, HandoffBehavior.SnapshotAndReplace);

        var glow = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Color = pulseColor, Opacity = 0.7 };
        knobBorder.Effect = glow;

        var glowPulse = new DoubleAnimation
        {
            From = 0.7,
            To = 0.35,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, glowPulse, HandoffBehavior.SnapshotAndReplace);
    }

    private Task<Rect?> SelectChatRegionAsync()
    {
        var selector = new RegionSelectorWindow();
        var result = selector.ShowDialog();
        return Task.FromResult(result == true ? selector.SelectedRegion : null);
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        try
        {
            _viewModel.SaveAppSettings();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Write($"SaveAppSettings on exit failed: {ex}");
        }
    }

    private void MenuSave_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveAppSettings();
        MessageBox.Show("Đã lưu cài đặt.", "Titan AI Live PC", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MenuPriceCatalog_Click(object sender, RoutedEventArgs e)
    {
        var priceWindow = new PriceCatalogWindow { Owner = this };
        priceWindow.ShowDialog();
    }

    private void MenuReset_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Reset toàn bộ cài đặt về mặc định ban đầu?",
            "Titan AI Live PC",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _viewModel.ResetAppToDefaults();
        SyncObsPasswordBoxes();
        MessageBox.Show("Đã reset mặc định thành công.", "Titan AI Live PC", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MenuHelpInfo_Click(object sender, RoutedEventArgs e)
    {
        var help = new HelpInfoWindow { Owner = this };
        help.ShowDialog();
    }

}

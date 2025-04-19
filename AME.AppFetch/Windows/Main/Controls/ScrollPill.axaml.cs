using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

namespace AME.AppFetch.Windows.Main.Controls;

public class ScrollPill : TemplatedControl
{
    public static readonly StyledProperty<double> MinPillHeightProperty = AvaloniaProperty.Register<ScrollPill, double>(nameof(MinPillHeight), 18);
    public double MinPillHeight { get => GetValue(MinPillHeightProperty); set => SetValue(MinPillHeightProperty, value); }
    public static readonly StyledProperty<double> MaxPillHeightProperty = AvaloniaProperty.Register<ScrollPill, double>(nameof(MaxPillHeight), 250);
    public double MaxPillHeight { get => GetValue(MaxPillHeightProperty); set => SetValue(MaxPillHeightProperty, value); }
    
    public static readonly StyledProperty<ScrollViewer> ScrollViewerProperty = AvaloniaProperty.Register<ScrollPill, ScrollViewer>(nameof(ScrollViewer));
    public ScrollViewer ScrollViewer { get => GetValue(ScrollViewerProperty); set => SetValue(ScrollViewerProperty, value); }

    private Border PART_Container = null!;
    private Border PART_Rect = null!;
    private Button PART_UpButton = null!;
    private Button PART_DownButton = null!;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        PART_Container = e.NameScope.Get<Border>(nameof(PART_Container));
        PART_Rect = e.NameScope.Get<Border>(nameof(PART_Rect));
        PART_UpButton = e.NameScope.Get<Button>(nameof(PART_UpButton));
        PART_DownButton = e.NameScope.Get<Button>(nameof(PART_DownButton));

        this.LayoutUpdated -= OnLayoutUpdated;
        this.LayoutUpdated += OnLayoutUpdated;
        
        PART_Rect.PointerPressed += PART_RectOnPointerPressed;
        PART_Rect.PointerReleased += PART_RectOnPointerReleased;
        PART_Rect.PointerMoved += PART_RectOnPointerMoved;
        
        ScrollViewer.ScrollChanged += ScrollViewerOnScrollChanged;

        PART_UpButton.Click += (sender, args) =>
        {
            if (_holdAnimation != null)
            {
                _holdAnimation?.Cancel();
                _holdAnimation?.Dispose();
                _holdAnimation = null;
                return;
            }
            
            ScrollViewer.Offset = new Vector(ScrollViewer.Offset.X, Math.Max(0, ScrollViewer.Offset.Y - (ScrollViewer.Viewport.Height / 3)));
        };

        PART_UpButton.AddHandler(PointerPressedEvent, async (sender, args) =>
        {
            await Task.Delay(150);
            if (PART_UpButton.IsPressed && ScrollViewer.Offset.Y > 0)
            {
                var animation = new Animation()
                {
                    Duration = TimeSpan.FromMilliseconds(ScrollViewer.Offset.Y * 5),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame()
                        {
                            Cue = new Cue(1),
                            Setters =
                            {
                                new Setter()
                                {
                                    Property = ScrollViewer.OffsetProperty,
                                    Value = new Vector(ScrollViewer.Offset.X, 0)
                                }
                            }
                        }
                    }
                };
                _holdAnimation?.Cancel();
                _holdAnimation?.Dispose();
                _holdAnimation = new CancellationTokenSource();
                _ = animation.RunAsync(ScrollViewer, _holdAnimation.Token);
            }
        }, RoutingStrategies.Tunnel);
        PART_UpButton.PointerReleased += (sender, args) =>
        {
            _holdAnimation?.Cancel();
            _holdAnimation?.Dispose();
            _holdAnimation = null;
        };
        
        PART_DownButton.Click += (sender, args) =>
        {
            if (_holdAnimation != null)
            {
                _holdAnimation?.Cancel();
                _holdAnimation?.Dispose();
                _holdAnimation = null;
                return;
            }
            
            ScrollViewer.Offset = new Vector(ScrollViewer.Offset.X, Math.Min(ScrollViewer.Extent.Height - ScrollViewer.Viewport.Height, ScrollViewer.Offset.Y + (ScrollViewer.Viewport.Height / 3)));
        };
        PART_DownButton.PointerReleased += (sender, args) =>
        {
            _holdAnimation?.Cancel();
            _holdAnimation?.Dispose();
            _holdAnimation = null;
        };
        PART_DownButton.AddHandler(PointerPressedEvent, async (sender, args) => 
        {
            await Task.Delay(150);
            if (PART_DownButton.IsPressed && ScrollViewer.Offset.Y < ScrollViewer.Extent.Height - ScrollViewer.Viewport.Height)
            {
                var animation = new Animation()
                {
                    Duration = TimeSpan.FromMilliseconds(((ScrollViewer.Extent.Height - ScrollViewer.Viewport.Height) - ScrollViewer.Offset.Y) * 5),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame()
                        {
                            Cue = new Cue(1),
                            Setters =
                            {
                                new Setter()
                                {
                                    Property = ScrollViewer.OffsetProperty,
                                    Value = new Vector(ScrollViewer.Offset.X, ScrollViewer.Extent.Height - ScrollViewer.Viewport.Height)
                                }
                            }
                        }
                    }
                };
                _holdAnimation?.Cancel();
                _holdAnimation?.Dispose();
                _holdAnimation = new CancellationTokenSource();
                _ = animation.RunAsync(ScrollViewer, _holdAnimation.Token);
            }
        }, RoutingStrategies.Tunnel);

    }

    private CancellationTokenSource? _holdAnimation;
    private PointerPoint? _lastPressPoint;
    private void PART_RectOnPointerReleased(object? sender, PointerReleasedEventArgs e) => _lastPressPoint = null;
    private void PART_RectOnPointerPressed(object? sender, PointerPressedEventArgs e) => _lastPressPoint = e.GetCurrentPoint(PART_Container);
    private void PART_RectOnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(PART_Container);;
        if (point.Properties.IsLeftButtonPressed)
        {
            _holdAnimation?.Cancel();
            _holdAnimation?.Dispose();
            _holdAnimation = null;
            
            _lastPressPoint ??= point;
            
            var oldRectPositionY = Math.Max(_lastPressPoint.Value.Position.Y - ((TranslateTransform)PART_Rect.RenderTransform!).Y, 0);
            var newRectPositionY = Math.Max(point.Position.Y - ((TranslateTransform)PART_Rect.RenderTransform!).Y, 0);
            
            var newTranslateY = ((TranslateTransform)PART_Rect.RenderTransform!).Y + (newRectPositionY - oldRectPositionY);
                
            var offsetY = newTranslateY / (PART_Container.Bounds.Size.Height - PART_Rect.Height) * (ScrollViewer.Extent.Height - ScrollViewer.Viewport.Height);
            
            ScrollViewer.Offset = new Point(ScrollViewer.Offset.X, offsetY);
      
            _lastPressPoint = point;
        }
    }
    
    private void ScrollViewerOnScrollChanged(object? sender, ScrollChangedEventArgs e) => OnLayoutUpdated(this, EventArgs.Empty);
    
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (PART_Container?.Bounds.Size.Height > 0 && ScrollViewer != null!)
        {
            PART_Rect.IsVisible = ScrollViewer.Extent.Height > ScrollViewer.Viewport.Height;
            if (!PART_Rect.IsVisible)
                return;
            
            PART_Rect.Height = Math.Min(MaxPillHeight, Math.Max(PART_Container.Bounds.Size.Height * (ScrollViewer.Viewport.Height / ScrollViewer.Extent.Height), Math.Min(MinPillHeight, PART_Container.Bounds.Size.Height)));
            ((TranslateTransform)PART_Rect.RenderTransform!).Y = (PART_Container.Bounds.Size.Height - PART_Rect.Height) * (ScrollViewer.Offset.Y / (ScrollViewer.Extent.Height - ScrollViewer.Viewport.Height));
        }
    }
}
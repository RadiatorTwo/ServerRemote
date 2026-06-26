using ServerRemote.App.Services;

namespace ServerRemote.App.Components;

/// <summary>
/// Transparent input surface over the live image. Translates pointer/touch gestures into NanoKVM
/// mouse input. Two modes:
/// <list type="bullet">
///   <item><b>Absolute</b> (<see cref="RelativeMode"/> = false): position on the image = mouse position
///   (letterbox-correct via <see cref="AspectRatio"/>). Works well with a real mouse (Windows) — driven
///   by the <see cref="PointerGestureRecognizer"/>.</item>
///   <item><b>Touchpad</b> (<see cref="RelativeMode"/> = true): swiping moves the cursor relatively,
///   tapping left-clicks, long-pressing = right-click. Driven by the <see cref="PanGestureRecognizer"/>,
///   because finger dragging on Android does not produce reliable move events through the pointer recognizer.</item>
/// </list>
/// Touchpad mode sends true RELATIVE HID reports (device HID1) — both movement and clicks. This is
/// more reliable than absolute reports on sleeping displays and in the BIOS, and matches the behavior
/// of a physical touchpad. Absolute mode still uses absolute reports (HID2).
/// </summary>
public sealed class KvmInputSurface : ContentView
{
    // Movement threshold (device-independent units) above which a gesture counts as a swipe rather than a tap.
    private const double MoveSlop = 12;
    // Duration after which a hold counts as a right-click.
    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(500);

    public static readonly BindableProperty HidProperty = BindableProperty.Create(
        nameof(Hid), typeof(NanoKvmHidService), typeof(KvmInputSurface));

    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive), typeof(bool), typeof(KvmInputSurface), false);

    public static readonly BindableProperty AspectRatioProperty = BindableProperty.Create(
        nameof(AspectRatio), typeof(double), typeof(KvmInputSurface), 0.0);

    public static readonly BindableProperty RelativeModeProperty = BindableProperty.Create(
        nameof(RelativeMode), typeof(bool), typeof(KvmInputSurface), false);

    public static readonly BindableProperty SensitivityProperty = BindableProperty.Create(
        nameof(Sensitivity), typeof(double), typeof(KvmInputSurface), 2.0);

    public NanoKvmHidService? Hid
    {
        get => (NanoKvmHidService?)GetValue(HidProperty);
        set => SetValue(HidProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Aspect ratio (width/height) of the displayed image. 0 = unknown → the surface
    /// is mapped 1:1. Otherwise the letterbox border produced by <c>AspectFit</c> is factored
    /// out so that clicks land exactly on the image. (Absolute mode only.)</summary>
    public double AspectRatio
    {
        get => (double)GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    /// <summary>Touchpad mode instead of absolute tapping.</summary>
    public bool RelativeMode
    {
        get => (bool)GetValue(RelativeModeProperty);
        set => SetValue(RelativeModeProperty, value);
    }

    /// <summary>Pointer speed in touchpad mode (1.0 = 1:1 with the swipe distance).</summary>
    public double Sensitivity
    {
        get => (double)GetValue(SensitivityProperty);
        set => SetValue(SensitivityProperty, value);
    }

    // --- Touchpad state (pan gesture) ---
    private double _lastTotalX, _lastTotalY;  // last cumulative pan offset since the gesture started
    private double _accumDx, _accumDy;        // sub-pixel remainders of the relative movement
    private bool _moved;
    private double _accumDist;
    private IDispatcherTimer? _longPress;

    public KvmInputSurface()
    {
        BackgroundColor = Colors.Transparent;

        // Absolute mode (real mouse, mainly Windows): pointer events.
        var pointer = new PointerGestureRecognizer();
        pointer.PointerMoved += OnPointerMoved;
        pointer.PointerPressed += OnPointerPressed;
        pointer.PointerReleased += OnPointerReleased;
        GestureRecognizers.Add(pointer);

        // Touchpad mode (finger dragging, reliable on Android): pan gesture.
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        GestureRecognizers.Add(pan);

        // Tapping = left-click (both modes).
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);
    }

    // ----- Absolute mode: image position → mouse position (letterbox-correct) -----

    private bool TryFraction(Point? p, out double fx, out double fy)
    {
        fx = fy = 0;
        if (!IsActive || Hid is null || p is null || Width <= 0 || Height <= 0)
            return false;

        double x = p.Value.X, y = p.Value.Y;
        double ar = AspectRatio;

        if (ar <= 0)
        {
            fx = Math.Clamp(x / Width, 0, 1);
            fy = Math.Clamp(y / Height, 0, 1);
            return true;
        }

        double containerAr = Width / Height;
        double dispW, dispH, offX, offY;
        if (containerAr > ar)
        {
            dispH = Height;
            dispW = Height * ar;
            offX = (Width - dispW) / 2;
            offY = 0;
        }
        else
        {
            dispW = Width;
            dispH = Width / ar;
            offX = 0;
            offY = (Height - dispH) / 2;
        }

        if (x < offX || x > offX + dispW || y < offY || y > offY + dispH)
            return false;

        fx = Math.Clamp((x - offX) / dispW, 0, 1);
        fy = Math.Clamp((y - offY) / dispH, 0, 1);
        return true;
    }

    // ----- Absolute mode: pointer events (real mouse) -----

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        if (RelativeMode || !IsActive || Hid is null)
            return;

        if (TryFraction(e.GetPosition(this), out var fx, out var fy))
        {
            _ = Hid.MouseMoveAsync(fx, fy);
            _ = Hid.MouseButtonAsync(0, true);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (RelativeMode)
            return;

        if (TryFraction(e.GetPosition(this), out var fx, out var fy))
            _ = Hid!.MouseMoveAsync(fx, fy);
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        if (RelativeMode || !IsActive || Hid is null)
            return;

        _ = Hid.MouseButtonAsync(0, false);
    }

    // ----- Touchpad mode: pan gesture → relative movement (HID1) -----

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (!RelativeMode || !IsActive || Hid is null)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _moved = false;
                _accumDist = 0;
                _accumDx = _accumDy = 0;
                _lastTotalX = _lastTotalY = 0;
                StartLongPress();
                break;

            case GestureStatus.Running:
                // TotalX/Y are cumulative from the gesture start → delta = current total minus last.
                double dx = e.TotalX - _lastTotalX;
                double dy = e.TotalY - _lastTotalY;
                _lastTotalX = e.TotalX;
                _lastTotalY = e.TotalY;

                _accumDist += Math.Abs(dx) + Math.Abs(dy);
                if (_accumDist > MoveSlop)
                {
                    _moved = true;
                    CancelLongPress();
                }

                // Scaled by sensitivity; accumulate sub-pixel remainders so that slow
                // swiping is not swallowed entirely.
                _accumDx += dx * Sensitivity;
                _accumDy += dy * Sensitivity;
                int rdx = (int)_accumDx;
                int rdy = (int)_accumDy;
                if (rdx != 0 || rdy != 0)
                {
                    _accumDx -= rdx;
                    _accumDy -= rdy;
                    _ = Hid.MouseMoveRelativeAsync(rdx, rdy);
                }
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                CancelLongPress();
                break;
        }
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsActive || Hid is null)
            return;

        // Touchpad: left-click on the relative device (same source as the movement).
        if (RelativeMode)
        {
            _ = Hid.MouseClickRelativeAsync(0);
            return;
        }

        // Absolute: move to the tapped image position and click there.
        if (TryFraction(e.GetPosition(this), out var fx, out var fy))
        {
            _ = Hid.MouseMoveAsync(fx, fy);
            _ = Hid.MouseClickAsync(0);
        }
    }

    // ----- Long press → right-click (touchpad mode only) -----

    private void StartLongPress()
    {
        CancelLongPress();
        _longPress = Application.Current?.Dispatcher.CreateTimer();
        if (_longPress is null)
            return;

        _longPress.Interval = LongPressDelay;
        _longPress.IsRepeating = false;
        _longPress.Tick += (_, _) =>
        {
            CancelLongPress();
            // Held without significant movement → right-click (relative, HID1).
            if (!_moved && Hid is not null)
                _ = Hid.MouseClickRelativeAsync(2);
        };
        _longPress.Start();
    }

    private void CancelLongPress()
    {
        _longPress?.Stop();
        _longPress = null;
    }
}

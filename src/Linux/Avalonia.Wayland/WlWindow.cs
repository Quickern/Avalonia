using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Utilities;
using NWayland.Protocols.Wayland;
using NWayland.Protocols.XdgDecorationUnstableV1;
using NWayland.Protocols.XdgShell;

namespace Avalonia.Wayland
{
    internal class WlWindow : IWindowImpl, WlSurface.IEvents, XdgWmBase.IEvents, XdgSurface.IEvents, XdgToplevel.IEvents, ZxdgToplevelDecorationV1.IEvents
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly WlSurface _wlSurface;
        private readonly XdgSurface _xdgSurface;
        private readonly XdgToplevel _xdgToplevel;
        private readonly ZxdgToplevelDecorationV1 _toplevelDecoration;

        private bool _active;
        private WlOutput _wlOutput;

        public WlWindow(AvaloniaWaylandPlatform platform, IWindowImpl? popupParent)
        {
            _platform = platform;
            platform.XdgWmBase.Events = this;
            WlInputDevice = new WlInputDevice(platform, this);
            _wlSurface = platform.WlCompositor.CreateSurface();
            _wlSurface.Events = this;
            _xdgSurface = platform.XdgWmBase.GetXdgSurface(_wlSurface);
            _xdgSurface.Events = this;
            _xdgToplevel = _xdgSurface.GetToplevel();
            _xdgToplevel.Events = this;
            _toplevelDecoration = platform.ZxdgDecorationManager.GetToplevelDecoration(_xdgToplevel);

            if (popupParent is not null)
                SetParent(popupParent);

            platform.WlDisplay.Roundtrip();
            var screens = _platform.WlScreens.AllScreens;
            ClientSize = screens.Count > 0
                ? new Size(screens[0].WorkingArea.Width * 0.75, screens[0].WorkingArea.Height * 0.7)
                : new Size(400, 600);

            _surfaces = new List<object>
            {
                new WlFramebufferSurface()
            };

            var glFeature = AvaloniaLocator.Current.GetService<IPlatformOpenGlInterface>();
            if (glFeature is EglPlatformOpenGlInterface egl)
            {
                var eglWindow = LibWaylandEgl.wl_egl_window_create(_wlSurface.Handle, (int)ClientSize.Width, (int)ClientSize.Height);
                Handle = new PlatformHandle(eglWindow, "EGL_WINDOW");
                _surfaces.Add(new EglGlPlatformSurface(egl, new SurfaceInfo(this)));
            }
        }

        private sealed class SurfaceInfo : EglGlPlatformSurfaceBase.IEglWindowGlPlatformSurfaceInfo
        {
            private readonly WlWindow _wlWindow;

            public SurfaceInfo(WlWindow wlWindow)
            {
                _wlWindow = wlWindow;
            }

            public IntPtr Handle => _wlWindow.Handle.Handle;
            public PixelSize Size => new((int)_wlWindow.ClientSize.Width, (int)_wlWindow.ClientSize.Height);
            public double Scaling => _wlWindow.RenderScaling;
        }

        internal WlInputDevice WlInputDevice { get; }

        public Size ClientSize { get; private set; }
        public Size? FrameSize { get; }
        public double RenderScaling { get; private set; } = 1;

        private readonly List<object> _surfaces;
        public IEnumerable<object> Surfaces => _surfaces;

        public Action<RawInputEventArgs>? Input { get; set; }
        public Action<Rect> Paint { get; set; }
        public Action<Size, PlatformResizeReason> Resized { get; set; }
        public Action<double>? ScalingChanged { get; set; }
        public Action<WindowTransparencyLevel>? TransparencyLevelChanged { get; set; }

        public IRenderer CreateRenderer(IRenderRoot root)
        {
            var loop = AvaloniaLocator.Current.GetService<IRenderLoop>();
            var customRendererFactory = AvaloniaLocator.Current.GetService<IRendererFactory>();

            if (customRendererFactory is not null)
                return customRendererFactory.Create(root, loop);

            return _platform.Options.UseDeferredRendering
                ? new DeferredRenderer(root, loop) { RenderOnlyOnRenderThread = true }
                : new ImmediateRenderer(root);
        }

        public void Invalidate(Rect rect) => _wlSurface.Damage((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);

        public void SetInputRoot(IInputRoot inputRoot) => WlInputDevice.InputRoot = inputRoot;

        public Point PointToClient(PixelPoint point) => new(point.X, point.Y);

        public PixelPoint PointToScreen(Point point) => new((int)point.X, (int)point.Y);

        public void SetCursor(ICursorImpl? cursor)
        {
            if (cursor is not WlCursorFactory.WlCursor wlCursor) return;
            WlInputDevice.SetCursor(wlCursor);
        }

        public Action? Closed { get; set; }
        public Action? LostFocus { get; set; }
        public IMouseDevice MouseDevice => WlInputDevice.MouseDevice;

        public IPopupImpl? CreatePopup()
        {
            return null;
        }

        public void SetTransparencyLevelHint(WindowTransparencyLevel transparencyLevel)
        {

        }

        public WindowTransparencyLevel TransparencyLevel { get; }
        public AcrylicPlatformCompensationLevels AcrylicCompensationLevels { get; }

        public void Show(bool activate, bool isDialog)
        {
            _wlSurface.Commit();
            Redraw();
        }

        public void Hide()
        {
            _wlSurface.Attach(null, 0, 0);
            _wlSurface.Commit();
        }

        public double DesktopScaling => RenderScaling;
        public PixelPoint Position => PixelPoint.Origin;
        public Action<PixelPoint>? PositionChanged { get; set; }

        public void Activate()
        {

        }

        public Action? Deactivated { get; set; }
        public Action? Activated { get; set; }
        public IPlatformHandle Handle { get; }
        public Size MaxAutoSizeHint { get; }

        public void SetTopmost(bool value)
        {

        }

        public IScreenImpl Screen => _platform.WlScreens;

        private WindowState _windowState;
        public WindowState WindowState
        {
            get => _windowState;
            set
            {
                if (_windowState == value) return;
                switch (value)
                {
                    case WindowState.Minimized:
                        _xdgToplevel.SetMinimized();
                        break;
                    case WindowState.Maximized:
                        if (_windowState == WindowState.FullScreen)
                            _xdgToplevel.UnsetFullscreen();
                        _xdgToplevel.SetMaximized();
                        break;
                    case WindowState.FullScreen:
                        _xdgToplevel.SetFullscreen(_wlOutput);
                        break;
                    case WindowState.Normal:
                        if (_windowState == WindowState.Maximized)
                            _xdgToplevel.UnsetMaximized();
                        else if (_windowState == WindowState.FullScreen)
                            _xdgToplevel.UnsetFullscreen();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(value), value, null);
                }
            }
        }

        public Action<WindowState> WindowStateChanged { get; set; }

        public void SetTitle(string? title) => _xdgToplevel.SetTitle(title);

        public void SetParent(IWindowImpl parent)
        {
            if (parent is WlWindow wlWindow)
                _xdgToplevel.SetParent(wlWindow._xdgToplevel);
        }

        public void SetEnabled(bool enable)
        {

        }

        public Action GotInputWhenDisabled { get; set; }

        public void SetSystemDecorations(SystemDecorations enabled)
        {
            switch (enabled)
            {
                case SystemDecorations.Full:
                    _toplevelDecoration.SetMode(ZxdgToplevelDecorationV1.ModeEnum.ServerSide);
                    break;
                case SystemDecorations.None:
                case SystemDecorations.BorderOnly:
                    _toplevelDecoration.SetMode(ZxdgToplevelDecorationV1.ModeEnum.ClientSide);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(enabled), enabled, null);
            }
        }

        public void SetIcon(IWindowIconImpl? icon) { } // impossible on wayland

        public void ShowTaskbarIcon(bool value)
        {

        }

        public void CanResize(bool value)
        {

        }

        public Func<bool>? Closing { get; set; }
        public bool IsClientAreaExtendedToDecorations { get; private set; }
        public Action<bool> ExtendClientAreaToDecorationsChanged { get; set; }
        public bool NeedsManagedDecorations { get; }
        public Thickness ExtendedMargins { get; }
        public Thickness OffScreenMargin { get; }

        public void BeginMoveDrag(PointerPressedEventArgs e)
        {
            _xdgToplevel.Move(_platform.WlSeat, WlInputDevice.Serial);
        }

        public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e)
        {
            _xdgToplevel.Resize(_platform.WlSeat, WlInputDevice.Serial, _windowEdges[edge]);
        }

        public void Resize(Size clientSize, PlatformResizeReason reason = PlatformResizeReason.Application)
        {
            if (clientSize == ClientSize || clientSize == Size.Empty) return;
            ClientSize = clientSize;
            LibWaylandEgl.wl_egl_window_resize(Handle.Handle, (int)ClientSize.Width, (int)ClientSize.Height, 0, 0);
            Resized.Invoke(ClientSize, reason);
            Redraw();
        }

        public void Move(PixelPoint point) { }

        public void SetMinMaxSize(Size minSize, Size maxSize)
        {
            var minX = double.IsInfinity(minSize.Width) ? 0 : (int)minSize.Width;
            var minY = double.IsInfinity(minSize.Height) ? 0 : (int)minSize.Height;
            var maxX = double.IsInfinity(maxSize.Width) ? 0 : (int)maxSize.Width;
            var maxY = double.IsInfinity(maxSize.Height) ? 0 : (int)maxSize.Height;
            _xdgToplevel.SetMinSize(minX, minY);
            _xdgToplevel.SetMaxSize(maxX, maxY);
        }

        public void SetExtendClientAreaToDecorationsHint(bool extendIntoClientAreaHint) =>
            _toplevelDecoration.SetMode(extendIntoClientAreaHint ? ZxdgToplevelDecorationV1.ModeEnum.ClientSide : ZxdgToplevelDecorationV1.ModeEnum.ServerSide);

        public void SetExtendClientAreaChromeHints(ExtendClientAreaChromeHints hints) { }

        public void SetExtendClientAreaTitleBarHeightHint(double titleBarHeight) { }

        public IPopupPositioner PopupPositioner { get; }

        public void SetWindowManagerAddShadowHint(bool enabled)
        {

        }

        public void OnConfigure(XdgToplevel eventSender, int width, int height, ReadOnlySpan<XdgToplevel.StateEnum> states)
        {
            if (states.Length == 0 && _active)
            {
                Deactivated?.Invoke();
                _windowState = WindowState.Minimized;
                _active = false;
                WindowStateChanged.Invoke(_windowState);
                return;
            }

            _windowState = WindowState.Normal;
            foreach (var state in states)
            {
                switch (state)
                {
                    case XdgToplevel.StateEnum.Maximized:
                        _windowState = WindowState.Maximized;
                        WindowStateChanged?.Invoke(_windowState);
                        break;
                    case XdgToplevel.StateEnum.Fullscreen:
                        _windowState = WindowState.FullScreen;
                        WindowStateChanged?.Invoke(_windowState);
                        break;
                    case XdgToplevel.StateEnum.Activated when !_active:
                        Activated?.Invoke();
                        _active = true;
                        break;
                }
            }

            var size = new Size(width, height);
            Resize(size, PlatformResizeReason.User);
        }

        public void OnConfigure(ZxdgToplevelDecorationV1 eventSender, ZxdgToplevelDecorationV1.ModeEnum mode)
        {
            var isExtended = mode == ZxdgToplevelDecorationV1.ModeEnum.ClientSide;
            if (isExtended == IsClientAreaExtendedToDecorations) return;
            IsClientAreaExtendedToDecorations = isExtended;
            ExtendClientAreaToDecorationsChanged.Invoke(IsClientAreaExtendedToDecorations);
        }

        public void OnClose(XdgToplevel eventSender)
        {
            if (Closing?.Invoke() is true) return;
            Closed?.Invoke();
        }

        public void OnConfigureBounds(XdgToplevel eventSender, int width, int height) { }

        public void OnConfigure(XdgSurface eventSender, uint serial) => _xdgSurface.AckConfigure(serial);

        public void OnPing(XdgWmBase eventSender, uint serial) => _platform.XdgWmBase.Pong(serial);

        public void OnEnter(WlSurface eventSender, WlOutput output)
        {
            _wlOutput = output;
            _platform.WlScreens.ActiveWindow = this;
            var screen = _platform.WlScreens.ScreenFromOutput(output);
            if (MathUtilities.AreClose(screen.PixelDensity, RenderScaling)) return;
            RenderScaling = screen.PixelDensity;
            ScalingChanged?.Invoke(RenderScaling);
        }

        public void OnLeave(WlSurface eventSender, WlOutput output) { }

        public void Dispose()
        {
            _toplevelDecoration.Dispose();
            _xdgToplevel.Dispose();
            _xdgSurface.Dispose();
            _wlSurface.Dispose();
            LibWaylandEgl.wl_egl_window_destroy(Handle.Handle);
        }

        private void Redraw()
        {
            Paint.Invoke(Rect.Empty);
        }

        private static readonly Dictionary<WindowEdge, XdgToplevel.ResizeEdgeEnum> _windowEdges = new()
        {
            { WindowEdge.North, XdgToplevel.ResizeEdgeEnum.Top },
            { WindowEdge.East, XdgToplevel.ResizeEdgeEnum.Right },
            { WindowEdge.South, XdgToplevel.ResizeEdgeEnum.Bottom },
            { WindowEdge.West, XdgToplevel.ResizeEdgeEnum.Left },
            { WindowEdge.NorthEast, XdgToplevel.ResizeEdgeEnum.TopLeft },
            { WindowEdge.SouthEast, XdgToplevel.ResizeEdgeEnum.BottomLeft },
            { WindowEdge.NorthWest, XdgToplevel.ResizeEdgeEnum.TopRight },
            { WindowEdge.SouthWest, XdgToplevel.ResizeEdgeEnum.BottomRight }
        };
    }
}

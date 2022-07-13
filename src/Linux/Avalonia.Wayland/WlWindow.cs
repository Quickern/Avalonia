using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.FreeDesktop;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Logging;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Rendering;
using Avalonia.Utilities;
using Avalonia.Wayland.Egl;
using Avalonia.Wayland.Framebuffer;
using NWayland.Protocols.Wayland;
using NWayland.Protocols.XdgDecorationUnstableV1;
using NWayland.Protocols.XdgForeignUnstableV2;
using NWayland.Protocols.XdgShell;

namespace Avalonia.Wayland
{
    internal class WlWindow : IWindowImpl, ITopLevelImplWithStorageProvider, IRenderTimer, WlSurface.IEvents, WlCallback.IEvents, XdgWmBase.IEvents, XdgSurface.IEvents, XdgToplevel.IEvents, ZxdgToplevelDecorationV1.IEvents, ZxdgExportedV2.IEvents
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly XdgSurface _xdgSurface;
        private readonly XdgToplevel _xdgToplevel;
        private readonly ZxdgToplevelDecorationV1 _toplevelDecoration;
        private readonly ZxdgExportedV2 _exported;
        private readonly WlFramebufferSurface _wlFramebufferSurface;
        private readonly IntPtr _eglWindow;

        private uint _lastTick;
        private bool _isDirty;
        private bool _isDamaged;
        private bool _canResize;
        private bool _active;
        private WindowState _prevWindowState;
        private WlOutput? _wlOutput;
        private WlCallback? _wlCallback;

        public event Action<TimeSpan>? Tick;

        public WlWindow(AvaloniaWaylandPlatform platform, IWindowImpl? popupParent)
        {
            _platform = platform;
            WlSurface = platform.WlCompositor.CreateSurface();
            WlSurface.Events = this;
            _xdgSurface = platform.XdgWmBase.GetXdgSurface(WlSurface);
            _xdgSurface.Events = this;
            _xdgToplevel = _xdgSurface.GetToplevel();
            _xdgToplevel.Events = this;
            _toplevelDecoration = platform.ZxdgDecorationManager.GetToplevelDecoration(_xdgToplevel);

            platform.WlScreens.AddWindow(this);

            if (popupParent is null)
                platform.XdgWmBase.Events = this;
            else
                SetParent(popupParent);

            _exported = _platform.ZxdgExporter.ExportToplevel(WlSurface);
            _exported.Events = this;

            var screens = _platform.WlScreens.AllScreens;
            ClientSize = screens.Count > 0
                ? new Size(screens[0].WorkingArea.Width * 0.75, screens[0].WorkingArea.Height * 0.7)
                : new Size(400, 600);

            _wlFramebufferSurface = new WlFramebufferSurface(platform, this, WlSurface);
            var surfaces = new List<object> { _wlFramebufferSurface };

            var glFeature = AvaloniaLocator.Current.GetService<IPlatformOpenGlInterface>();
            if (glFeature is EglPlatformOpenGlInterface egl)
            {
                _eglWindow = LibWaylandEgl.wl_egl_window_create(WlSurface.Handle, (int)ClientSize.Width, (int)ClientSize.Height);
                var surfaceInfo = new WlEglSurfaceInfo(this, _eglWindow);
                var platformSurface = new WlEglPlatformSurface(egl, surfaceInfo);
                surfaces.Add(platformSurface);
            }

            Surfaces = surfaces.ToArray();
        }

        public IPlatformHandle Handle { get; }

        public Size MaxAutoSizeHint => _wlOutput is null ? Size.Empty : _platform.WlScreens.ScreenFromOutput(_wlOutput).Bounds.Size.ToSize(RenderScaling);

        public Size ClientSize { get; private set; }

        public Size? FrameSize => default;

        public PixelPoint Position => PixelPoint.Origin;

        public double RenderScaling { get; private set; } = 1;

        public double DesktopScaling => RenderScaling;

        public WindowTransparencyLevel TransparencyLevel { get; private set; }

        public AcrylicPlatformCompensationLevels AcrylicCompensationLevels => default;

        public bool IsClientAreaExtendedToDecorations { get; private set; }

        public bool NeedsManagedDecorations { get; private set; }

        public Thickness ExtendedMargins => default;

        public Thickness OffScreenMargin => default;

        public IScreenImpl Screen => _platform.WlScreens;

        public IEnumerable<object> Surfaces { get; }

        public IMouseDevice? MouseDevice => _platform.WlInputDevice.MouseDevice;

        public IStorageProvider StorageProvider { get; private set; }

        public Action<RawInputEventArgs>? Input { get; set; }

        public Action<Rect>? Paint { get; set; }

        public Action<Size, PlatformResizeReason>? Resized { get; set; }

        public Action<double>? ScalingChanged { get; set; }

        public Action<WindowTransparencyLevel>? TransparencyLevelChanged { get; set; }

        public Action? Activated { get; set; }

        public Action? Deactivated { get; set; }

        public Action? LostFocus { get; set; }

        public Func<bool> Closing { get; set; }

        public Action? Closed { get; set; }

        public Action<PixelPoint>? PositionChanged { get; set; }

        public Action<WindowState> WindowStateChanged { get; set; }

        public Action GotInputWhenDisabled { get; set; }

        public Action<bool> ExtendClientAreaToDecorationsChanged { get; set; }

        private WindowState _windowState;
        public WindowState WindowState
        {
            get => _windowState;
            set
            {
                if (_windowState == value)
                    return;
                switch (value)
                {
                    case WindowState.Minimized:
                        _xdgToplevel.SetMinimized();
                        break;
                    case WindowState.Maximized:
                        _xdgToplevel.UnsetFullscreen();
                        _xdgToplevel.SetMaximized();
                        break;
                    case WindowState.FullScreen:
                        _xdgToplevel.SetFullscreen(_wlOutput);
                        break;
                    case WindowState.Normal:
                        _xdgToplevel.UnsetFullscreen();
                        _xdgToplevel.UnsetMaximized();
                        break;
                }
            }
        }

        internal WlSurface WlSurface { get; }

        internal IInputRoot? InputRoot { get; private set; }

        public IRenderer CreateRenderer(IRenderRoot root)
        {
            var loop = new RenderLoop(this, null!);
            var customRendererFactory = AvaloniaLocator.Current.GetService<IRendererFactory>();

            if (customRendererFactory is not null)
                return customRendererFactory.Create(root, loop);

            return _platform.Options.UseDeferredRendering ? new DeferredRenderer(root, loop) : new ImmediateRenderer(root);
        }

        public void Invalidate(Rect rect)
        {
            WlSurface.DamageBuffer((int)rect.X, (int)rect.Y, (int)(rect.Width * RenderScaling), (int)(rect.Height * RenderScaling));
            _isDamaged = true;
        }

        public void SetInputRoot(IInputRoot inputRoot) => InputRoot = inputRoot;

        public Point PointToClient(PixelPoint point) => new(point.X, point.Y);

        public PixelPoint PointToScreen(Point point) => new((int)point.X, (int)point.Y);

        public void SetCursor(ICursorImpl? cursor) => _platform.WlInputDevice.SetCursor(cursor as WlCursor);

        public IPopupImpl? CreatePopup() => null; // TODO

        public void SetTransparencyLevelHint(WindowTransparencyLevel transparencyLevel)
        {
            if (transparencyLevel == TransparencyLevel)
                return;
            TransparencyLevel = transparencyLevel;
            TransparencyLevelChanged?.Invoke(transparencyLevel);
        }

        public void Show(bool activate, bool isDialog)
        {
            _isDirty = true;
            RequestFrame();
            Paint?.Invoke(Rect.Empty);
        }

        public void Hide()
        {
            WlSurface.Attach(null, 0, 0);
            WlSurface.Commit();
        }

        public void Activate() { }

        public void SetTopmost(bool value) { }

        public void SetTitle(string? title) => _xdgToplevel.SetTitle(title ?? string.Empty);

        public void SetParent(IWindowImpl parent)
        {
            if (parent is WlWindow wlWindow)
                _xdgToplevel.SetParent(wlWindow._xdgToplevel);
        }

        public void SetEnabled(bool enable) { }

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

        public void SetIcon(IWindowIconImpl? icon) =>
            Logger.TryGet(LogEventLevel.Debug, LogArea.WaylandPlatform)?.Log(this, "SetIcon() is not supported on Wayland.");

        public void ShowTaskbarIcon(bool value) =>
            Logger.TryGet(LogEventLevel.Debug, LogArea.WaylandPlatform)?.Log(this, "ShowTaskbarIcon() is not supported on Wayland.");

        public void CanResize(bool value) { } // TODO

        public void BeginMoveDrag(PointerPressedEventArgs e)
        {
            _xdgToplevel.Move(_platform.WlSeat, _platform.WlInputDevice.Serial);
            e.Pointer.Capture(null);
        }

        public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e)
        {
            var wlEdge = _windowEdges[edge];
            Console.WriteLine(wlEdge);
            _xdgToplevel.Resize(_platform.WlSeat, _platform.WlInputDevice.Serial, wlEdge);
            e.Pointer.Capture(null);
        }

        public void Resize(Size clientSize, PlatformResizeReason reason = PlatformResizeReason.Application)
        {
            if (clientSize == ClientSize)
                return;
            _isDirty = true;
            ClientSize = clientSize;
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

        public void OnEnter(WlSurface eventSender, WlOutput output)
        {
            _wlOutput = output;
            var screen = _platform.WlScreens.ScreenFromOutput(output);
            if (MathUtilities.AreClose(screen.PixelDensity, RenderScaling))
                return;
            RenderScaling = screen.PixelDensity;
            ScalingChanged?.Invoke(RenderScaling);
            if (WlSurface.Version >= 3)
                WlSurface.SetBufferScale((int)RenderScaling);
        }

        public void OnLeave(WlSurface eventSender, WlOutput output) { }

        public void OnDone(WlCallback eventSender, uint callbackData)
        {
            Tick?.Invoke(TimeSpan.FromMilliseconds(callbackData - _lastTick));
            _wlCallback!.Dispose();
            _wlCallback = null;
            RequestFrame();
            Paint?.Invoke(Rect.Empty);
            WlSurface.Commit();
            _lastTick = callbackData;
        }

        public void OnPing(XdgWmBase eventSender, uint serial) => _platform.XdgWmBase.Pong(serial);

        public void OnConfigure(XdgSurface eventSender, uint serial)
        {
            if (_isDirty)
            {
                _isDirty = false;
                if (_eglWindow != IntPtr.Zero)
                    LibWaylandEgl.wl_egl_window_resize(_eglWindow, (int)ClientSize.Width, (int)ClientSize.Height, 0, 0);
                Resized?.Invoke(ClientSize, PlatformResizeReason.User);
            }

            _xdgSurface.AckConfigure(serial);
        }

        public void OnConfigure(XdgToplevel eventSender, int width, int height, ReadOnlySpan<XdgToplevel.StateEnum> states)
        {
            if (states.Length == 0 && _active)
            {
                _prevWindowState = _windowState;
                _windowState = WindowState.Minimized;
                _active = false;
                Deactivated?.Invoke();
                WindowStateChanged.Invoke(_windowState);
                return;
            }

            var windowState = WindowState.Normal;
            foreach (var state in states)
            {
                switch (state)
                {
                    case XdgToplevel.StateEnum.Maximized:
                        windowState = WindowState.Maximized;
                        break;
                    case XdgToplevel.StateEnum.Fullscreen:
                        windowState = WindowState.FullScreen;
                        break;
                    case XdgToplevel.StateEnum.Activated when !_active:
                        windowState = _prevWindowState;
                        _active = true;
                        Activated?.Invoke();
                        break;
                }
            }

            if (_windowState != windowState)
            {
                _windowState = windowState;
                WindowStateChanged.Invoke(windowState);
            }

            var size = new Size(width, height);
            if (size == Size.Empty)
                return;
            _isDirty = true;
            ClientSize = size;
        }

        public void OnClose(XdgToplevel eventSender) => Closing.Invoke();

        public void OnConfigureBounds(XdgToplevel eventSender, int width, int height) { }

        public void OnWmCapabilities(XdgToplevel eventSender, ReadOnlySpan<XdgToplevel.WmCapabilitiesEnum> capabilities) { }

        public void OnConfigure(ZxdgToplevelDecorationV1 eventSender, ZxdgToplevelDecorationV1.ModeEnum mode)
        {
            var isExtended = mode == ZxdgToplevelDecorationV1.ModeEnum.ClientSide;
            if (isExtended == IsClientAreaExtendedToDecorations)
                return;
            IsClientAreaExtendedToDecorations = isExtended;
            NeedsManagedDecorations = !isExtended;
            ExtendClientAreaToDecorationsChanged.Invoke(IsClientAreaExtendedToDecorations);
        }

        public void OnHandle(ZxdgExportedV2 eventSender, string handle) => StorageProvider = DBusSystemDialog.TryCreate($"wayland:{handle}")!;

        public void Dispose()
        {
            _platform.WlScreens.RemoveWindow(this);
            Closed?.Invoke();
            _exported.Dispose();
            _toplevelDecoration.Dispose();
            _xdgToplevel.Dispose();
            _xdgSurface.Dispose();
            _wlFramebufferSurface.Dispose();
            _platform.WlDisplay.Roundtrip();
            if (_eglWindow != IntPtr.Zero)
                LibWaylandEgl.wl_egl_window_destroy(_eglWindow);
            WlSurface.Dispose();
        }

        internal void RequestFrame()
        {
            if (_wlCallback is not null)
                return;
            _wlCallback = WlSurface.Frame();
            _wlCallback.Events = this;
        }

        private static readonly Dictionary<WindowEdge, XdgToplevel.ResizeEdgeEnum> _windowEdges = new()
        {
            { WindowEdge.North, XdgToplevel.ResizeEdgeEnum.Top },
            { WindowEdge.NorthEast, XdgToplevel.ResizeEdgeEnum.TopRight },
            { WindowEdge.East, XdgToplevel.ResizeEdgeEnum.Right },
            { WindowEdge.SouthEast, XdgToplevel.ResizeEdgeEnum.BottomRight },
            { WindowEdge.South, XdgToplevel.ResizeEdgeEnum.Bottom },
            { WindowEdge.SouthWest, XdgToplevel.ResizeEdgeEnum.BottomLeft },
            { WindowEdge.West, XdgToplevel.ResizeEdgeEnum.Left },
            { WindowEdge.NorthWest, XdgToplevel.ResizeEdgeEnum.TopLeft }
        };
    }
}

using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.FreeDesktop;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using NWayland.Protocols.XdgDecorationUnstableV1;
using NWayland.Protocols.XdgForeignUnstableV2;
using NWayland.Protocols.XdgShell;

namespace Avalonia.Wayland
{
    internal class WlToplevel : WlWindow, IWindowImpl, ITopLevelImplWithStorageProvider, XdgToplevel.IEvents, ZxdgToplevelDecorationV1.IEvents, ZxdgExportedV2.IEvents
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly XdgToplevel _xdgToplevel;
        private readonly ZxdgToplevelDecorationV1 _toplevelDecoration;
        private readonly ZxdgExportedV2? _exported;

        private PixelSize _minSize;
        private PixelSize _maxSize;

        public WlToplevel(AvaloniaWaylandPlatform platform) : base(platform)
        {
            _platform = platform;
            _xdgToplevel = XdgSurface.GetToplevel();
            _xdgToplevel.Events = this;
            if (platform.Options.AppId is not null)
                _xdgToplevel.SetAppId(platform.Options.AppId);
            _toplevelDecoration = platform.ZxdgDecorationManager.GetToplevelDecoration(_xdgToplevel);
            _toplevelDecoration.Events = this;
            _exported = platform.ZxdgExporter?.ExportToplevel(WlSurface);
            if (_exported is not null)
                _exported.Events = this;
        }

        public Func<bool> Closing { get; set; }

        public Action GotInputWhenDisabled { get; set; }

        public Action<WindowState> WindowStateChanged { get; set; }

        public Action<bool> ExtendClientAreaToDecorationsChanged { get; set; }

        public IStorageProvider StorageProvider { get; private set; }

        public bool IsClientAreaExtendedToDecorations => false;

        public bool NeedsManagedDecorations => false;

        public Thickness ExtendedMargins => default;

        public Thickness OffScreenMargin => default;

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
                        _xdgToplevel.SetFullscreen(WlOutput);
                        break;
                    case WindowState.Normal:
                        _xdgToplevel.UnsetFullscreen();
                        _xdgToplevel.UnsetMaximized();
                        break;
                }
            }
        }

        public void SetTitle(string? title) => _xdgToplevel.SetTitle(title ?? string.Empty);

        public void SetParent(IWindowImpl parent)
        {
            if (parent is not WlToplevel wlToplevel)
                return;
            _xdgToplevel.SetParent(wlToplevel._xdgToplevel);
            Parent = wlToplevel;
        }

        public void SetEnabled(bool enable) { }

        public void SetSystemDecorations(SystemDecorations enabled) => _toplevelDecoration.SetMode(enabled == SystemDecorations.Full ? ZxdgToplevelDecorationV1.ModeEnum.ServerSide : ZxdgToplevelDecorationV1.ModeEnum.ClientSide);

        public void SetIcon(IWindowIconImpl? icon) { }

        public void ShowTaskbarIcon(bool value) { }

        public void CanResize(bool value)
        {
            if (value)
            {
                _xdgToplevel.SetMinSize(_minSize.Width, _minSize.Height);
                _xdgToplevel.SetMaxSize(_maxSize.Width, _maxSize.Height);
            }
            else
            {
                _xdgToplevel.SetMinSize((int)ClientSize.Width, (int)ClientSize.Height);
                _xdgToplevel.SetMaxSize((int)ClientSize.Width, (int)ClientSize.Height);
            }
        }

        public void BeginMoveDrag(PointerPressedEventArgs e)
        {
            _xdgToplevel.Move(_platform.WlSeat, _platform.WlInputDevice.Serial);
            e.Pointer.Capture(null);
        }

        public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e)
        {
            _xdgToplevel.Resize(_platform.WlSeat, _platform.WlInputDevice.Serial, ParseWindowEdges(edge));
            e.Pointer.Capture(null);
        }

        public void Move(PixelPoint point) { }

        public void SetMinMaxSize(Size minSize, Size maxSize)
        {
            var minX = double.IsInfinity(minSize.Width) ? 0 : (int)minSize.Width;
            var minY = double.IsInfinity(minSize.Height) ? 0 : (int)minSize.Height;
            var maxX = double.IsInfinity(maxSize.Width) ? 0 : (int)maxSize.Width;
            var maxY = double.IsInfinity(maxSize.Height) ? 0 : (int)maxSize.Height;
            _minSize = new PixelSize(minX, minY);
            _maxSize = new PixelSize(maxX, maxY);
            _xdgToplevel.SetMinSize(minX, minY);
            _xdgToplevel.SetMaxSize(maxX, maxY);
        }

        public void SetExtendClientAreaToDecorationsHint(bool extendIntoClientAreaHint) { }

        public void SetExtendClientAreaChromeHints(ExtendClientAreaChromeHints hints) { }

        public void SetExtendClientAreaTitleBarHeightHint(double titleBarHeight) { }

        public void OnConfigure(XdgToplevel eventSender, int width, int height, ReadOnlySpan<XdgToplevel.StateEnum> states)
        {
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
                    case XdgToplevel.StateEnum.Activated:
                        Activated?.Invoke();
                        break;
                }
            }

            if (_windowState != windowState)
            {
                _windowState = windowState;
                WindowStateChanged.Invoke(windowState);
            }

            PendingSize = new PixelSize(width, height);
        }

        public void OnClose(XdgToplevel eventSender) => Closing.Invoke();

        public void OnConfigureBounds(XdgToplevel eventSender, int width, int height) { }

        public void OnWmCapabilities(XdgToplevel eventSender, ReadOnlySpan<XdgToplevel.WmCapabilitiesEnum> capabilities) { }

        public void OnConfigure(ZxdgToplevelDecorationV1 eventSender, ZxdgToplevelDecorationV1.ModeEnum mode) { }

        public void OnHandle(ZxdgExportedV2 eventSender, string handle)
        {
            StorageProvider = new CompositeStorageProvider(new Func<Task<IStorageProvider?>>[]
            {
                () => DBusSystemDialog.TryCreate($"wayland:{handle}")
            });
        }

        public override void Dispose()
        {
            Closed?.Invoke();
            _exported?.Dispose();
            _toplevelDecoration.Dispose();
            _xdgToplevel.Dispose();
            base.Dispose();
        }

        private static XdgToplevel.ResizeEdgeEnum ParseWindowEdges(WindowEdge windowEdge) => windowEdge switch
        {
            WindowEdge.North => XdgToplevel.ResizeEdgeEnum.Top,
            WindowEdge.NorthEast => XdgToplevel.ResizeEdgeEnum.TopRight,
            WindowEdge.East => XdgToplevel.ResizeEdgeEnum.Right,
            WindowEdge.SouthEast => XdgToplevel.ResizeEdgeEnum.BottomRight,
            WindowEdge.South => XdgToplevel.ResizeEdgeEnum.Bottom,
            WindowEdge.SouthWest => XdgToplevel.ResizeEdgeEnum.BottomLeft,
            WindowEdge.West => XdgToplevel.ResizeEdgeEnum.Left,
            WindowEdge.NorthWest => XdgToplevel.ResizeEdgeEnum.TopLeft,
            _ => throw new ArgumentOutOfRangeException(nameof(windowEdge))
        };
    }
}
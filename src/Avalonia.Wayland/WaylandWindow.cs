using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Rendering;
using NWayland.Interop;
using NWayland.Protocols.Wayland;
using NWayland.Protocols.XdgShell;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Surfaces;
using Avalonia.OpenGL.Egl;
using Avalonia.Platform.Interop;

namespace Avalonia.Wayland
{
    unsafe partial class WaylandWindow : IWindowImpl
    {
        private readonly WaylandPlatform _platform;
        private readonly bool _popup;
        private IInputRoot _inputRoot;
        private PixelSize _realSize;

        private Size _dummy_size = new Size(640, 480);

        private double _scaling = 1.0;

        private WlSurface _surface;
        private WlRegion _region;
        private XdgSurface _xdgSurface;

        private XdgToplevel _xdgTopLevel;

        private EglPlatformOpenGlInterface _platformGl;
        private EglSurface _eglSurface;

        private WlBuffer _wlBuffer;

        [DllImport("libEGL.so.1")]
        static extern IntPtr eglGetProcAddress(Utf8Buffer proc);

        public WaylandWindow(WaylandPlatform platform, IWindowImpl popupParent)
        {
            _platform = platform;
            _popup = popupParent != null;

            _surface = platform.Compositor.CreateSurface();
            _xdgSurface = _platform.Shell.GetXdgSurface(_surface);
            _xdgTopLevel = _xdgSurface.GetToplevel();

            _xdgSurface.Events = new XdgSurfaceHandler(_xdgSurface);
            _xdgTopLevel.Events = new XdgTopLevelHandler(_xdgTopLevel);

            _region = platform.Compositor.CreateRegion();
            _region.Add(0, 0, 640, 480);
            _surface.SetOpaqueRegion(_region);

            _xdgSurface.SetWindowGeometry(0, 0, 640, 480);

            _surface.Commit();
            _platform.Display.Roundtrip();

            var wl_egl_window = LibWayland.wl_egl_window_create(_surface.Handle, 640, 480);

            // Console.WriteLine($"Made a wl_egl_window() :: {wl_egl_window}");

            _platformGl = AvaloniaLocator.Current.GetService<IPlatformOpenGlInterface>() as EglPlatformOpenGlInterface;
            var surface = new EglGlPlatformSurface(_platformGl, new SurfaceInfo(this, _platform.Display.Handle, _surface.Handle, wl_egl_window));

            var surfaces = new List<object>
            {
               surface
            };

            Surfaces = surfaces.ToArray();
            // Console.WriteLine($"_eglSurface {_eglSurface}");
        }

        class SurfaceInfo  : EglGlPlatformSurface.IEglWindowGlPlatformSurfaceInfo
        {
            private readonly WaylandWindow _window;
            private readonly IntPtr _display;
            private readonly IntPtr _parent;

            public SurfaceInfo(WaylandWindow window, IntPtr display, IntPtr parent, IntPtr surface)
            {
                _window = window;
                _display = display;
                _parent = parent;
                Handle = surface;
            }
            public IntPtr Handle { get; }

            public PixelSize Size
            {
                get
                {
                    return new PixelSize(1, 1);
                }
            }

            public double Scaling => _window.RenderScaling;
        }

        public WindowState WindowState { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public Action<WindowState> WindowStateChanged { get; set; }
        public Action GotInputWhenDisabled { get; set; }
        public Func<bool> Closing { get; set; }

        public bool IsClientAreaExtendedToDecorations => throw new NotImplementedException();

        public Action<bool> ExtendClientAreaToDecorationsChanged { get; set; }

        public bool NeedsManagedDecorations => false;

        public Thickness ExtendedMargins => throw new NotImplementedException();

        public Thickness OffScreenMargin => throw new NotImplementedException();

        public double DesktopScaling => RenderScaling;

        public PixelPoint Position => throw new NotImplementedException();

        public Action<PixelPoint>? PositionChanged { get; set; }
        public Action? Deactivated { get; set; }
        public Action? Activated { get; set; }

        public IPlatformHandle Handle => throw new NotImplementedException();

        public Size MaxAutoSizeHint => _dummy_size;

        public IScreenImpl Screen => _platform.Screens;

        public Size ClientSize => new Size(_realSize.Width / RenderScaling, _realSize.Height / RenderScaling);

        public Size? FrameSize
        {
            get
            {
                return new Size(640, 480);
            }
        }

        public double RenderScaling
        {
            get => Interlocked.CompareExchange(ref _scaling, 0.0, 0.0);
            private set => Interlocked.Exchange(ref _scaling, value);
        }

        public IEnumerable<object> Surfaces { get; }

        public Action<RawInputEventArgs>? Input { get; set; }
        public Action<Rect>? Paint { get; set; }
        public Action<Size, PlatformResizeReason>? Resized { get; set; }
        public Action<double>? ScalingChanged { get; set; }
        public Action<WindowTransparencyLevel>? TransparencyLevelChanged { get; set; }
        public Action? Closed { get; set; }
        public Action? LostFocus { get; set; }

        public IMouseDevice MouseDevice => null;

        public WindowTransparencyLevel TransparencyLevel => WindowTransparencyLevel.None;

        public AcrylicPlatformCompensationLevels AcrylicCompensationLevels => throw new NotImplementedException();

        public void Activate()
        {
            throw new NotImplementedException();
        }

        public void BeginMoveDrag(PointerPressedEventArgs e)
        {
            throw new NotImplementedException();
        }

        public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e)
        {
            throw new NotImplementedException();
        }

        public void CanResize(bool value)
        {
            throw new NotImplementedException();
        }

        public IPopupImpl? CreatePopup()
        {
            throw new NotImplementedException();
        }

        public IRenderer CreateRenderer(IRenderRoot root)
        {
            var loop = AvaloniaLocator.Current.GetService<IRenderLoop>();
            return (IRenderer)new WaylandImmediateRendererProxy(root, loop);
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public void Hide()
        {
            throw new NotImplementedException();
        }

        public void Invalidate(Rect rect)
        {
            throw new NotImplementedException();
        }

        public void Move(PixelPoint point)
        {
            throw new NotImplementedException();
        }

        public Point PointToClient(PixelPoint point)
        {
            throw new NotImplementedException();
        }

        public PixelPoint PointToScreen(Point point)
        {
            throw new NotImplementedException();
        }

        public void Resize(Size clientSize, PlatformResizeReason reason = PlatformResizeReason.Application)
        {
        }

        public void SetCursor(ICursorImpl? cursor)
        {
            throw new NotImplementedException();
        }

        public void SetEnabled(bool enable)
        {
            throw new NotImplementedException();
        }

        public void SetExtendClientAreaChromeHints(ExtendClientAreaChromeHints hints)
        {
        }

        public void SetExtendClientAreaTitleBarHeightHint(double titleBarHeight)
        {
        }

        public void SetExtendClientAreaToDecorationsHint(bool extendIntoClientAreaHint)
        {
        }

        public void SetIcon(IWindowIconImpl? icon)
        {
            throw new NotImplementedException();
        }

        public void SetInputRoot(IInputRoot inputRoot)
        {
            _inputRoot = inputRoot;
        }

        public void SetMinMaxSize(Size minSize, Size maxSize)
        {
            throw new NotImplementedException();
        }

        public void SetParent(IWindowImpl parent)
        {
            throw new NotImplementedException();
        }

        public void SetSystemDecorations(SystemDecorations enabled)
        {
            throw new NotImplementedException();
        }

        public void SetTitle(string? title)
        {
            _xdgTopLevel.SetTitle(title);
        }

        public void SetTopmost(bool value)
        {
            throw new NotImplementedException();
        }

        public void SetTransparencyLevelHint(WindowTransparencyLevel transparencyLevel)
        {
            throw new NotImplementedException();
        }

        public void Show(bool activate, bool isDialog)
        {
        }

        public void ShowTaskbarIcon(bool value)
        {
        }
    }

    public class XdgSurfaceHandler : XdgSurface.IEvents
    {
        private XdgSurface _xdg_surface;

        public XdgSurfaceHandler(XdgSurface xdg_surface)
        {
            _xdg_surface = xdg_surface;
        }

        public void OnConfigure(NWayland.Protocols.XdgShell.XdgSurface eventSender, uint @serial)
        {
            _xdg_surface.AckConfigure(serial);
        }
    }

    public class XdgTopLevelHandler : XdgToplevel.IEvents
    {
        private XdgToplevel _top_level;

        public XdgTopLevelHandler(XdgToplevel top_level)
        {
            _top_level = top_level;
        }

        public void OnClose(XdgToplevel eventSender)
        {
            throw new NotImplementedException();
        }

        public void OnConfigure(XdgToplevel eventSender, int width, int height, ReadOnlySpan<byte> states)
        {
            throw new NotImplementedException();
        }
    }


}

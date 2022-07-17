using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.Utilities;
using Avalonia.Wayland.Egl;
using Avalonia.Wayland.Framebuffer;
using NWayland.Protocols.Wayland;
using NWayland.Protocols.XdgShell;

namespace Avalonia.Wayland
{
    internal abstract class WlWindow : IWindowBaseImpl, IRenderTimer, WlSurface.IEvents, WlCallback.IEvents, XdgSurface.IEvents
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly WlFramebufferSurface _wlFramebufferSurface;
        private readonly IntPtr _eglWindow;

        private uint _lastTick;
        private PixelSize _pendingSize;
        private WlCallback? _wlCallback;

        public event Action<TimeSpan>? Tick;

        protected WlWindow(AvaloniaWaylandPlatform platform)
        {
            _platform = platform;
            WlSurface = platform.WlCompositor.CreateSurface();
            WlSurface.Events = this;
            XdgSurface = platform.XdgWmBase.GetXdgSurface(WlSurface);
            XdgSurface.Events = this;

            platform.WlScreens.AddWindow(this);

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
                var platformSurface = new EglGlPlatformSurface(egl, surfaceInfo);
                surfaces.Add(platformSurface);
            }

            Surfaces = surfaces.ToArray();
        }

        public IPlatformHandle Handle { get; }

        public Size MaxAutoSizeHint => WlOutput is null ? Size.Empty : _platform.WlScreens.ScreenFromOutput(WlOutput).Bounds.Size.ToSize(RenderScaling);//_platform.WlScreens.AllScreens.Select(static s => s.Bounds.Size.ToSize(s.PixelDensity)).OrderByDescending(static x => x.Width + x.Height).FirstOrDefault();

        public Size ClientSize { get; private set; }

        public Size? FrameSize => default;

        public PixelPoint Position { get; protected set; }

        public double RenderScaling { get; private set; } = 1;

        public double DesktopScaling => RenderScaling;

        public WindowTransparencyLevel TransparencyLevel { get; private set; }

        public AcrylicPlatformCompensationLevels AcrylicCompensationLevels => default;

        public IScreenImpl Screen => _platform.WlScreens;

        public IEnumerable<object> Surfaces { get; }

        public IMouseDevice? MouseDevice => _platform.WlInputDevice.MouseDevice;

        public Action<RawInputEventArgs>? Input { get; set; }

        public Action<Rect>? Paint { get; set; }

        public Action<Size, PlatformResizeReason>? Resized { get; set; }

        public Action<double>? ScalingChanged { get; set; }

        public Action<WindowTransparencyLevel>? TransparencyLevelChanged { get; set; }

        public Action? Activated { get; set; }

        public Action? Deactivated { get; set; }

        public Action? LostFocus { get; set; }

        public Action? Closed { get; set; }

        public Action<PixelPoint>? PositionChanged { get; set; }

        internal IInputRoot? InputRoot { get; private set; }

        internal WlSurface WlSurface { get; }

        internal XdgSurface XdgSurface { get; }

        internal uint XdgSurfaceConfigureSerial { get; private set; }

        protected WlOutput? WlOutput { get; private set; }

        protected ref PixelSize PendingSize => ref _pendingSize;

        public IRenderer CreateRenderer(IRenderRoot root)
        {
            var loop = new RenderLoop(this, Dispatcher.UIThread);
            var customRendererFactory = AvaloniaLocator.Current.GetService<IRendererFactory>();

            if (customRendererFactory is not null)
                return customRendererFactory.Create(root, loop);

            return _platform.Options.UseDeferredRendering ? new DeferredRenderer(root, loop) : new ImmediateRenderer(root);
        }

        public void Invalidate(Rect rect) => WlSurface.DamageBuffer((int)rect.X, (int)rect.Y, (int)(rect.Width * RenderScaling), (int)(rect.Height * RenderScaling));

        public void SetInputRoot(IInputRoot inputRoot) => InputRoot = inputRoot;

        public Point PointToClient(PixelPoint point) => new(point.X, point.Y);

        public PixelPoint PointToScreen(Point point) => new((int)point.X, (int)point.Y);

        public void SetCursor(ICursorImpl? cursor) => _platform.WlInputDevice.SetCursor(cursor as WlCursor);

        public IPopupImpl CreatePopup() => new WlPopup(_platform, this);

        public void SetTransparencyLevelHint(WindowTransparencyLevel transparencyLevel)
        {
            if (transparencyLevel == TransparencyLevel)
                return;
            TransparencyLevel = transparencyLevel;
            TransparencyLevelChanged?.Invoke(transparencyLevel);
        }

        public virtual void Show(bool activate, bool isDialog)
        {
            RequestFrame();
            Paint?.Invoke(Rect.Empty);
        }

        public void Hide()
        {
            //WlSurface.Attach(null, 0, 0);
            //WlSurface.Commit();
        }

        public void Activate() { }

        public void SetTopmost(bool value) { }

        public void Resize(Size clientSize, PlatformResizeReason reason = PlatformResizeReason.Application)
        {
            if (clientSize != ClientSize)
                _pendingSize = new PixelSize((int)clientSize.Width, (int)clientSize.Height);
        }

        public void OnEnter(WlSurface eventSender, WlOutput output)
        {
            WlOutput = output;
            var screen = _platform.WlScreens.ScreenFromOutput(output);
            if (MathUtilities.AreClose(screen.PixelDensity, RenderScaling))
                return;
            RenderScaling = screen.PixelDensity;
            ScalingChanged?.Invoke(RenderScaling);
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

        public void OnConfigure(XdgSurface eventSender, uint serial)
        {
            XdgSurfaceConfigureSerial = serial;
            var pendingSize = new Size(_pendingSize.Width, _pendingSize.Height);
            if (_pendingSize != PixelSize.Empty && pendingSize != ClientSize)
            {
                ClientSize = pendingSize;
                if (_eglWindow != IntPtr.Zero)
                    LibWaylandEgl.wl_egl_window_resize(_eglWindow, _pendingSize.Width, _pendingSize.Height, 0, 0);
                Resized?.Invoke(ClientSize, PlatformResizeReason.User);
            }

            XdgSurface.AckConfigure(serial);
        }

        public virtual void Dispose()
        {
            Console.WriteLine($"Disposing base window of type {GetType().Name}");
            _platform.WlScreens.RemoveWindow(this);
            _wlCallback?.Dispose();
            _wlCallback = null;
            XdgSurface.Dispose();
            _wlFramebufferSurface.Dispose();
            if (_eglWindow != IntPtr.Zero)
                LibWaylandEgl.wl_egl_window_destroy(_eglWindow);
            WlSurface.Dispose();
        }

        internal void RequestFrame()
        {
            if (_wlCallback is not null)
                return;
            _wlCallback = WlSurface.Frame();
            if (_wlCallback is not null)
                _wlCallback.Events = this;
        }
    }
}

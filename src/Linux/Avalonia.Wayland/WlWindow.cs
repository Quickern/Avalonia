using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Input.TextInput;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.Wayland.Egl;
using Avalonia.Wayland.Framebuffer;
using NWayland.Protocols.FractionalScaleV1;
using NWayland.Protocols.Wayland;
using NWayland.Protocols.XdgShell;

namespace Avalonia.Wayland
{
    internal abstract class WlWindow : IWindowBaseImpl, ITopLevelImplWithTextInputMethod, WlSurface.IEvents, WlCallback.IEvents, XdgSurface.IEvents, WpFractionalScaleV1.IEvents
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly WlFramebufferSurface _wlFramebufferSurface;
        private readonly WpFractionalScaleV1? _wpFractionalScale;
        private readonly IntPtr _eglWindow;

        private WlCallback? _frameCallback;

        protected WlWindow(AvaloniaWaylandPlatform platform)
        {
            _platform = platform;
            WlSurface = platform.WlCompositor.CreateSurface();
            XdgSurface = platform.XdgWmBase.GetXdgSurface(WlSurface);
            XdgSurface.Events = this;

            if (platform.WpFractionalScaleManager is not null)
            {
                _wpFractionalScale = platform.WpFractionalScaleManager.GetFractionalScale(WlSurface);
                _wpFractionalScale.Events = this;
            }

            TextInputMethod = platform.WlTextInputMethod;

            var screens = _platform.WlScreens.AllScreens;
            ClientSize = screens.Count > 0
                ? new Size(screens[0].Bounds.Width * 0.75, screens[0].Bounds.Height * 0.7)
                : new Size(400, 600);

            _wlFramebufferSurface = new WlFramebufferSurface(platform, this);
            var surfaces = new List<object> { _wlFramebufferSurface };

            var glFeature = AvaloniaLocator.Current.GetService<IPlatformOpenGlInterface>();
            if (glFeature is EglPlatformOpenGlInterface egl)
            {
                _eglWindow = LibWaylandEgl.wl_egl_window_create(WlSurface.Handle, (int)ClientSize.Width, (int)ClientSize.Height);
                var surfaceInfo = new WlEglSurfaceInfo(this, _eglWindow);
                var platformSurface = new WlEglGlPlatformSurface(egl, surfaceInfo);
                surfaces.Insert(0, platformSurface);
            }

            Surfaces = surfaces.ToArray();

            platform.WlScreens.AddWindow(this);
        }

        public IPlatformHandle Handle { get; }

        public ITextInputMethodImpl? TextInputMethod { get; }

        public Size MaxAutoSizeHint => _platform.WlScreens.AllScreens.Select(static s => s.Bounds.Size.ToSize(s.Scaling)).OrderByDescending(static x => x.Width + x.Height).FirstOrDefault();

        public Size ClientSize { get; private set; }

        public Size? FrameSize => null;

        public PixelPoint Position { get; protected set; }

        public double RenderScaling { get; private set; } = 1;

        public double DesktopScaling => RenderScaling;

        public WindowTransparencyLevel TransparencyLevel { get; private set; }

        public AcrylicPlatformCompensationLevels AcrylicCompensationLevels => default;

        public IScreenImpl Screen => _platform.WlScreens;

        public IEnumerable<object> Surfaces { get; }

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

        internal WlWindow? Parent { get; set; }

        internal WlSurface WlSurface { get; }

        internal XdgSurface XdgSurface { get; }

        internal uint XdgSurfaceConfigureSerial { get; private set; }

        protected WlOutput? WlOutput { get; private set; }

        protected Size PendingSize { get; set; }

        public IRenderer CreateRenderer(IRenderRoot root)
        {
            var loop = AvaloniaLocator.Current.GetRequiredService<IRenderLoop>();
            var customRendererFactory = AvaloniaLocator.Current.GetService<IRendererFactory>();

            if (customRendererFactory is not null)
                return customRendererFactory.Create(root, loop);
            if (_platform.Options.UseCompositor)
                return new CompositingRenderer(root, _platform.Compositor!);
            if (_platform.Options.UseDeferredRendering)
                return new DeferredRenderer(root, loop);
            return new ImmediateRenderer((Visual)root);
        }

        public void Invalidate(Rect rect) => WlSurface.DamageBuffer((int)rect.X, (int)rect.Y, (int)(rect.Width * RenderScaling), (int)(rect.Height * RenderScaling));

        public void SetInputRoot(IInputRoot inputRoot) => InputRoot = inputRoot;

        public Point PointToClient(PixelPoint point) => point.ToPoint(RenderScaling);

        public PixelPoint PointToScreen(Point point) => new((int)(point.X * RenderScaling), (int)(point.Y * RenderScaling));

        public void SetCursor(ICursorImpl? cursor) => _platform.WlInputDevice.PointerHandler?.SetCursor(cursor as WlCursor);

        public IPopupImpl CreatePopup() => new WlPopup(_platform, this);

        public void SetTransparencyLevelHint(WindowTransparencyLevel transparencyLevel)
        {
            if (transparencyLevel == TransparencyLevel)
                return;
            if (transparencyLevel == WindowTransparencyLevel.None)
                SetOpaqueRegion(ClientSize);
            else
                WlSurface.SetOpaqueRegion(null);
            TransparencyLevel = transparencyLevel;
            TransparencyLevelChanged?.Invoke(transparencyLevel);
        }

        public virtual void Show(bool activate, bool isDialog) => DoPaint();

        public void Hide()
        {
            WlSurface.Attach(null, 0, 0);
            WlSurface.Commit();
        }

        public void Activate() { }

        public void SetTopmost(bool value) { } // impossible on Wayland

        public void Resize(Size clientSize, PlatformResizeReason reason = PlatformResizeReason.Application)
        {
            if (XdgSurfaceConfigureSerial == 0 && clientSize != Size.Empty && clientSize != ClientSize)
                DoResize(clientSize);
        }

        public void OnDone(WlCallback eventSender, uint callbackData)
        {
            _frameCallback!.Dispose();
            _frameCallback = null;
            DoPaint();
        }

        public void OnEnter(WlSurface eventSender, WlOutput output) => WlOutput = output;

        public void OnLeave(WlSurface eventSender, WlOutput output) => WlOutput = null;

        public void OnConfigure(XdgSurface eventSender, uint serial)
        {
            if (XdgSurfaceConfigureSerial == serial)
                return;
            XdgSurfaceConfigureSerial = serial;
            XdgSurface.AckConfigure(serial);
            if (_frameCallback is null)
                DoPaint();
        }

        public void OnPreferredScale(WpFractionalScaleV1 eventSender, uint scale)
        {
            RenderScaling = scale / 120d;
            ScalingChanged?.Invoke(RenderScaling);
        }

        public virtual void Dispose()
        {
            _platform.WlScreens.RemoveWindow(this);
            if (_eglWindow != IntPtr.Zero)
                LibWaylandEgl.wl_egl_window_destroy(_eglWindow);
            _wpFractionalScale?.Dispose();
            _wlFramebufferSurface.Dispose();
            XdgSurface.Dispose();
            WlSurface.Dispose();
            Closed?.Invoke();
        }

        internal void RequestFrame()
        {
            if (_frameCallback is not null)
                return;
            _frameCallback = WlSurface.Frame();
            _frameCallback.Events = this;
        }

        private void DoResize(Size size)
        {
            ClientSize = size;
            if (_eglWindow != IntPtr.Zero)
                LibWaylandEgl.wl_egl_window_resize(_eglWindow, (int)size.Width, (int)size.Height, 0, 0);
            if (TransparencyLevel == WindowTransparencyLevel.None)
                SetOpaqueRegion(size);
            Resized?.Invoke(size, PlatformResizeReason.User);
        }

        private void DoPaint()
        {
            if (PendingSize != Size.Empty && PendingSize != ClientSize)
                DoResize(PendingSize);
            Paint?.Invoke(new Rect(ClientSize));
        }

        private void SetOpaqueRegion(Size size)
        {
            using var region = _platform.WlCompositor.CreateRegion();
            region.Add(0, 0, (int)size.Width, (int)size.Height);
            WlSurface.SetOpaqueRegion(region);
        }
    }
}

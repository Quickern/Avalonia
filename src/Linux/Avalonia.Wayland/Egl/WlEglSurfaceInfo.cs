using System;
using Avalonia.OpenGL.Egl;

namespace Avalonia.Wayland.Egl
{
    internal class WlEglSurfaceInfo : EglGlPlatformSurfaceBase.IEglWindowGlPlatformSurfaceInfo
    {
        private readonly WlWindow _wlWindow;

        public WlEglSurfaceInfo(WlWindow wlWindow, IntPtr eglWindow)
        {
            _wlWindow = wlWindow;
            Handle = eglWindow;
        }

        public IntPtr Handle { get; }

        public PixelSize Size => new((int)_wlWindow.ClientSize.Width, (int)_wlWindow.ClientSize.Height);

        public double Scaling => _wlWindow.RenderScaling;
    }
}

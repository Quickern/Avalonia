using System;
using Avalonia.OpenGL.Egl;

namespace Avalonia.Wayland.Egl
{
    internal class WlEglSurfaceInfo : EglGlPlatformSurfaceBase.IEglWindowGlPlatformSurfaceInfo
    {
        public WlEglSurfaceInfo(WlWindow wlWindow, IntPtr eglWindow)
        {
            WlWindow = wlWindow;
            Handle = eglWindow;
        }

        public WlWindow WlWindow { get; }

        public IntPtr Handle { get; }

        public PixelSize Size => new((int)WlWindow.ClientSize.Width, (int)WlWindow.ClientSize.Height);

        public double Scaling => WlWindow.RenderScaling;
    }
}

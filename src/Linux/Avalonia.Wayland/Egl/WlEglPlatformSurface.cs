using Avalonia.OpenGL.Egl;
using Avalonia.OpenGL.Surfaces;

namespace Avalonia.Wayland.Egl
{
    internal class WlEglPlatformSurface : IGlPlatformSurface
    {
        private readonly WlWindow _wlWindow;
        private readonly EglPlatformOpenGlInterface _egl;
        private readonly EglGlPlatformSurfaceBase.IEglWindowGlPlatformSurfaceInfo _info;

        public WlEglPlatformSurface(WlWindow wlWindow, EglPlatformOpenGlInterface egl, EglGlPlatformSurfaceBase.IEglWindowGlPlatformSurfaceInfo info)
        {
            _wlWindow = wlWindow;
            _egl = egl;
            _info = info;
        }

        public IGlPlatformSurfaceRenderTarget CreateGlRenderTarget() => new WlEglPlatformSurfaceRenderTarget(_wlWindow, _egl, _info);
    }
}

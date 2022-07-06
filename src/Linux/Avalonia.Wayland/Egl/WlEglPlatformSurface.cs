using Avalonia.OpenGL.Egl;
using Avalonia.OpenGL.Surfaces;

namespace Avalonia.Wayland.Egl
{
    internal class WlEglPlatformSurface : IGlPlatformSurface
    {
        private readonly EglPlatformOpenGlInterface _egl;
        private readonly EglGlPlatformSurfaceBase.IEglWindowGlPlatformSurfaceInfo _info;

        public WlEglPlatformSurface(EglPlatformOpenGlInterface egl, EglGlPlatformSurfaceBase.IEglWindowGlPlatformSurfaceInfo info)
        {
            _egl = egl;
            _info = info;
        }

        public IGlPlatformSurfaceRenderTarget CreateGlRenderTarget() => new WlEglPlatformSurfaceRenderTarget(_egl, _info);
    }
}

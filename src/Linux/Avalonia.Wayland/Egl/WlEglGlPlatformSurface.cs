using Avalonia.OpenGL.Egl;
using Avalonia.OpenGL.Surfaces;

namespace Avalonia.Wayland.Egl
{
    internal class WlEglGlPlatformSurface : EglGlPlatformSurfaceBase
    {
        private readonly EglPlatformOpenGlInterface _egl;
        private readonly WlEglSurfaceInfo _info;

        public WlEglGlPlatformSurface(EglPlatformOpenGlInterface egl, WlEglSurfaceInfo info)
        {
            _egl = egl;
            _info = info;
        }

        public override IGlPlatformSurfaceRenderTarget CreateGlRenderTarget()
        {
            var glSurface = _egl.CreateWindowSurface(_info.Handle);
            return new RenderTarget(_egl, glSurface, _info);
        }

        private sealed class RenderTarget : EglPlatformSurfaceRenderTargetBase
        {
            private readonly EglPlatformOpenGlInterface _egl;
            private readonly WlEglSurfaceInfo _info;

            private EglSurface _glSurface;
            private PixelSize _currentSize;

            public RenderTarget(EglPlatformOpenGlInterface egl, EglSurface glSurface, WlEglSurfaceInfo info) : base(egl)
            {
                _egl = egl;
                _glSurface = glSurface;
                _info = info;
                _currentSize = info.Size;
            }

            public override void Dispose()
            {
                _glSurface.Dispose();
                base.Dispose();
            }

            public override IGlPlatformSurfaceRenderingSession BeginDraw()
            {
                if (_info.Size != _currentSize)
                {
                    _glSurface.Dispose();
                    _glSurface = _egl.CreateWindowSurface(_info.Handle);
                    _currentSize = _info.Size;
                }

                _info.WlWindow.RequestFrame();
                return base.BeginDraw(_glSurface, _info);
            }
        }
    }
}

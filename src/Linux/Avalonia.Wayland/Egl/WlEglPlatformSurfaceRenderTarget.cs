using Avalonia.OpenGL.Egl;
using Avalonia.OpenGL.Surfaces;

namespace Avalonia.Wayland.Egl
{
    internal class WlEglPlatformSurfaceRenderTarget : EglPlatformSurfaceRenderTargetBase
    {
        private readonly EglPlatformOpenGlInterface _egl;
        private readonly EglGlPlatformSurfaceBase.IEglWindowGlPlatformSurfaceInfo _surfaceInfo;

        private PixelSize _currentSize;
        private EglSurface? _eglSurface;

        internal WlEglPlatformSurfaceRenderTarget(EglPlatformOpenGlInterface egl, EglGlPlatformSurfaceBase.IEglWindowGlPlatformSurfaceInfo surfaceInfo) : base(egl)
        {
            _egl = egl;
            _surfaceInfo = surfaceInfo;
            _currentSize = surfaceInfo.Size;
        }

        public override IGlPlatformSurfaceRenderingSession BeginDraw()
        {
            if (_surfaceInfo.Size != _currentSize || _eglSurface is null)
            {
                _eglSurface?.Dispose();
                _eglSurface = _egl.CreateWindowSurface(_surfaceInfo.Handle);
                _currentSize = _surfaceInfo.Size;
            }

            _egl.Display.EglInterface.SwapInterval(_egl.Display.Handle, 0);
            return base.BeginDraw(_eglSurface, _surfaceInfo);
        }

        public override void Dispose() => _eglSurface?.Dispose();
    }
}

using System;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using Avalonia.OpenGL.Surfaces;

namespace Avalonia.Wayland.Egl
{
    internal class WlEglPlatformSurfaceRenderTarget : IGlPlatformSurfaceRenderTarget
    {
        private readonly WlWindow _wlWindow;
        private readonly EglPlatformOpenGlInterface _egl;
        private readonly EglGlPlatformSurfaceBase.IEglWindowGlPlatformSurfaceInfo _surfaceInfo;

        private PixelSize _currentSize;
        private EglSurface? _eglSurface;

        internal WlEglPlatformSurfaceRenderTarget(WlWindow wlWindow, EglPlatformOpenGlInterface egl, EglGlPlatformSurfaceBase.IEglWindowGlPlatformSurfaceInfo surfaceInfo)
        {
            _wlWindow = wlWindow;
            _egl = egl;
            _surfaceInfo = surfaceInfo;
            _currentSize = surfaceInfo.Size;
        }

        public IGlPlatformSurfaceRenderingSession BeginDraw()
        {
            if (_surfaceInfo.Size != _currentSize || _eglSurface is null)
            {
                _eglSurface?.Dispose();
                _eglSurface = _egl.CreateWindowSurface(_surfaceInfo.Handle);
                _currentSize = _surfaceInfo.Size;
            }

            var restoreContext = _egl.PrimaryEglContext.MakeCurrent(_eglSurface);
            var success = false;
            try
            {
                var eglInterface = _egl.Display.EglInterface;
                eglInterface.SwapInterval(_egl.Display.Handle, 0);
                success = true;
                _wlWindow.RequestFrame();
                return new Session(_egl.PrimaryEglContext, _eglSurface, _surfaceInfo, restoreContext, false);
            }
            finally
            {
                if (!success)
                    restoreContext.Dispose();
            }
        }

        public void Dispose() => _eglSurface?.Dispose();

        private sealed class Session : IGlPlatformSurfaceRenderingSession
        {
            private readonly EglContext _context;
            private readonly EglSurface _glSurface;
            private readonly EglGlPlatformSurfaceBase.IEglWindowGlPlatformSurfaceInfo _info;
            private readonly IDisposable _restoreContext;

            public Session(EglContext context, EglSurface glSurface, EglGlPlatformSurfaceBase.IEglWindowGlPlatformSurfaceInfo info, IDisposable restoreContext, bool isYFlipped)
            {
                _context = context;
                _glSurface = glSurface;
                _info = info;
                _restoreContext = restoreContext;
                IsYFlipped = isYFlipped;
            }

            public IGlContext Context => _context;

            public PixelSize Size => _info.Size;

            public double Scaling => _info.Scaling;

            public bool IsYFlipped { get; }

            public void Dispose()
            {
                _context.GlInterface.Flush();
                _glSurface.SwapBuffers();
                _restoreContext.Dispose();
            }
        }
    }
}

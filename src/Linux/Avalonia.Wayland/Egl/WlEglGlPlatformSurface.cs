using System;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using Avalonia.OpenGL.Surfaces;

namespace Avalonia.Wayland.Egl
{
    internal class WlEglGlPlatformSurface : EglGlPlatformSurfaceBase
    {
        private readonly WlEglSurfaceInfo _info;

        public WlEglGlPlatformSurface(WlEglSurfaceInfo info)
        {
            _info = info;
        }

        public override IGlPlatformSurfaceRenderTarget CreateGlRenderTarget(IGlContext context)
        {
            var eglContext = (EglContext)context;
            var glSurface = eglContext.Display.CreateWindowSurface(_info.Handle);
            return new RenderTarget(glSurface, eglContext, _info);
        }

        private sealed class RenderTarget : EglPlatformSurfaceRenderTargetBase
        {
            private readonly WlEglSurfaceInfo _info;
            private readonly IntPtr _handle;

            private EglSurface? _glSurface;
            private PixelSize _currentSize;

            public RenderTarget(EglSurface glSurface, EglContext context, WlEglSurfaceInfo info) : base(context)
            {
                _glSurface = glSurface;
                _info = info;
                _currentSize = info.Size;
                _handle = _info.Handle;
            }

            public override void Dispose() => _glSurface?.Dispose();

            public override IGlPlatformSurfaceRenderingSession BeginDrawCore()
            {
                if (_info.Size != _currentSize || _handle != _info.Handle || _glSurface is null)
                {
                    _glSurface?.Dispose();
                    _glSurface = null;
                    _glSurface = Context.Display.CreateWindowSurface(_info.Handle);
                    _currentSize = _info.Size;
                }

                _info.WlWindow.RequestFrame();
                return BeginDraw(_glSurface, _info.Size, _info.Scaling);
            }
        }
    }
}

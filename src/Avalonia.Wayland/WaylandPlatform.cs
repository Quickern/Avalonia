using System.Runtime.InteropServices;

using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Wayland;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using NWayland.Protocols.Wayland;
using NWayland.Protocols.XdgShell;
using NWayland.Interop;
using Avalonia.Platform.Interop;

namespace Avalonia.Wayland
{
    class WaylandPlatform : IWindowingPlatform
    {
        public IScreenImpl Screens { get; private set; }

        public WlDisplay Display { get; set; }
        private WlRegistry Registry { get; set; }
        private RegistryHandler _registryHandler;
        public WlCompositor Compositor { get; set; }
        public XdgWmBase Shell { get; set; }

        public EglDisplay _eglDisplay;

        [DllImport("libEGL.so.1")]
        static extern IntPtr eglGetProcAddress(Utf8Buffer proc);

        public void Initialize()
        {
            Display = WlDisplay.Connect(null);
            Registry = Display.GetRegistry();
            _registryHandler = new RegistryHandler(Registry);

            Registry.Events = _registryHandler;

            Display.Dispatch();

            Compositor = _registryHandler.Bind(WlCompositor.BindFactory, WlCompositor.InterfaceName, 1);
            Shell = _registryHandler.Bind(XdgWmBase.BindFactory, XdgWmBase.InterfaceName, 1);

            Display.Roundtrip();

            Console.WriteLine("Starting EGL init");

            var _egl = new EglInterface(eglGetProcAddress);
            var egl_display = _egl.GetDisplay(Display.Handle);

            EglDisplay _eglDisplay = new EglDisplay(_egl, true, egl_display);
            var _platformGl = new EglPlatformOpenGlInterface(_eglDisplay);

            AvaloniaLocator.CurrentMutable.BindToSelf(this)
                .Bind<IWindowingPlatform>().ToConstant(this)
                .Bind<IPlatformThreadingInterface>().ToConstant(new WaylandPlatformThreading(this))
                .Bind<IRenderLoop>().ToConstant(new RenderLoop())
                .Bind<IRenderTimer>().ToConstant(new SleepLoopRenderTimer(60))
                .Bind<ICursorFactory>().ToConstant(new WaylandCursorFactory())
                .Bind<IPlatformOpenGlInterface>().ToConstant(_platformGl);
        }

        public IWindowImpl CreateEmbeddableWindow()
        {
            throw new NotSupportedException();
        }

        public ITrayIconImpl? CreateTrayIcon()
        {
            throw new NotImplementedException();
        }

        public IWindowImpl CreateWindow()
        {
            return new WaylandWindow(this, null);
        }
    }

    public class GlobalInfo
    {
        public uint Name { get; }
        public string Interface { get; }
        public uint Version { get; }

        public GlobalInfo(uint name, string @interface, uint version)
        {
            Name = name;
            Interface = @interface;
            Version = version;
        }

        public override string ToString() => $"{Interface} version {Version} at {Name}";
    }


    internal class RegistryHandler : WlRegistry.IEvents
    {
        private readonly WlRegistry _registry;

        public RegistryHandler(WlRegistry registry)
        {
            _registry = registry;
        }
        private Dictionary<uint, GlobalInfo> _globals { get; } = new Dictionary<uint, GlobalInfo>();
        public List<GlobalInfo> GetGlobals() => _globals.Values.ToList();
        public void OnGlobal(WlRegistry eventSender, uint name, string @interface, uint version)
        {
            _globals[name] = new GlobalInfo(name, @interface, version);
        }

        public void OnGlobalRemove(WlRegistry eventSender, uint name)
        {
            _globals.Remove(name);
        }

        public unsafe T Bind<T>(IBindFactory<T> factory, string iface, int? version) where T : WlProxy
        {
            var glob = GetGlobals().FirstOrDefault(g => g.Interface == iface);
            if (glob == null)
                throw new NotSupportedException($"Unable to find {iface} in the registry");

            version ??= factory.GetInterface()->Version;
            if (version > factory.GetInterface()->Version)
                throw new ArgumentException($"Version {version} is not supported");

            if (glob.Version < version)
                throw new NotSupportedException(
        $"Compositor doesn't support {version} of {iface}, only {glob.Version} is supported");

            return _registry.Bind(glob.Name, factory, version.Value);
        }
    }

}

namespace Avalonia
{
    public static class AvaloniaWaylandPlatformExtensions
    {
        public static T UseWayland<T>(this T builder) where T : AppBuilderBase<T>, new()
        {
            builder.UseWindowingSubsystem(() =>
                    new WaylandPlatform().Initialize());

            return builder;
        }

        public static void InitializeWaylandPlatform() =>
            new WaylandPlatform().Initialize();
    }

}


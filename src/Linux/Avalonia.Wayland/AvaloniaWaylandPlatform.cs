using System;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Dialogs;
using Avalonia.FreeDesktop;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Wayland;
using NWayland.Protocols.Wayland;
using NWayland.Protocols.XdgActivationV1;
using NWayland.Protocols.XdgDecorationUnstableV1;
using NWayland.Protocols.XdgOutputUnstableV1;
using NWayland.Protocols.XdgShell;

namespace Avalonia.Wayland
{
    internal class AvaloniaWaylandPlatform : IWindowingPlatform
    {
        internal WaylandPlatformOptions Options { get; private set; }

        internal WlDisplay WlDisplay { get; private set; }

        internal WlRegistryHandler WlRegistryHandler { get; private set; }

        internal WlCompositor WlCompositor { get; private set; }

        internal WlSeat WlSeat { get; private set; }

        internal WlShm WlShm { get; private set; }

        internal WlDataDeviceManager WlDataDeviceManager { get; private set; }

        internal XdgWmBase XdgWmBase { get; private set; }

        internal XdgActivationV1 XdgActivation { get; private set; }

        internal ZxdgDecorationManagerV1 ZxdgDecorationManager { get; private set; }

        internal ZxdgOutputManagerV1 ZxdgOutputManager { get; private set; }

        internal WlScreens WlScreens { get; private set; }

        public void Initialize(WaylandPlatformOptions options)
        {
            Options = options;
            WlDisplay = WlDisplay.Connect();
            var registry = WlDisplay.GetRegistry();
            WlRegistryHandler = new WlRegistryHandler(registry);
            WlDisplay.Roundtrip();
            WlCompositor = WlRegistryHandler.Bind(WlCompositor.BindFactory, WlCompositor.InterfaceName, WlCompositor.InterfaceVersion);
            WlSeat = WlRegistryHandler.Bind(WlSeat.BindFactory, WlSeat.InterfaceName, WlSeat.InterfaceVersion);
            WlShm = WlRegistryHandler.Bind(WlShm.BindFactory, WlShm.InterfaceName, WlShm.InterfaceVersion);
            WlDataDeviceManager = WlRegistryHandler.Bind(WlDataDeviceManager.BindFactory, WlDataDeviceManager.InterfaceName, WlDataDeviceManager.InterfaceVersion);
            XdgWmBase = WlRegistryHandler.Bind(XdgWmBase.BindFactory, XdgWmBase.InterfaceName, XdgWmBase.InterfaceVersion);
            XdgActivation = WlRegistryHandler.Bind(XdgActivationV1.BindFactory, XdgActivationV1.InterfaceName, XdgActivationV1.InterfaceVersion);
            ZxdgDecorationManager = WlRegistryHandler.Bind(ZxdgDecorationManagerV1.BindFactory, ZxdgDecorationManagerV1.InterfaceName, ZxdgDecorationManagerV1.InterfaceVersion);
            ZxdgOutputManager = WlRegistryHandler.Bind(ZxdgOutputManagerV1.BindFactory, ZxdgOutputManagerV1.InterfaceName, ZxdgOutputManagerV1.InterfaceVersion);
            WlScreens = new WlScreens(this);

            AvaloniaLocator.CurrentMutable.BindToSelf(this)
                .Bind<IWindowingPlatform>().ToConstant(this)
                .Bind<IPlatformThreadingInterface>().ToConstant(new WlPlatformThreading(this))
                .Bind<IRenderTimer>().ToConstant(new DefaultRenderTimer(60))
                .Bind<IRenderLoop>().ToConstant(new RenderLoop())
                .Bind<PlatformHotkeyConfiguration>().ToConstant(new PlatformHotkeyConfiguration(KeyModifiers.Control))
                .Bind<IKeyboardDevice>().ToConstant(new KeyboardDevice())
                .Bind<ICursorFactory>().ToConstant(new WlCursorFactory(this))
                .Bind<IClipboard>().ToLazy<IClipboard>(() => new WlDataHandler(this))
                //.Bind<IPlatformSettings>().ToConstant(new PlatformSettingsStub())
                .Bind<IPlatformIconLoader>().ToConstant(new IconLoaderStub())
                .Bind<ISystemDialogImpl>().ToConstant(DBusSystemDialog.TryCreate() as ISystemDialogImpl ?? new ManagedFileDialogExtensions.ManagedSystemDialogImpl<Window>())
                .Bind<IMountedVolumeInfoProvider>().ToConstant(new LinuxMountedVolumeInfoProvider());
                //.Bind<IPlatformLifetimeEventsImpl>().ToConstant();

                var egl = EglPlatformOpenGlInterface.TryCreate(() => new EglDisplay(new EglInterface(), false, 0x31D8, WlDisplay.Handle, null));
                if (egl is not null)
                    AvaloniaLocator.CurrentMutable.Bind<IPlatformOpenGlInterface>().ToConstant(egl);
        }

        public IWindowImpl CreateWindow() => new WlWindow(this, null);

        public IWindowImpl CreateEmbeddableWindow()
        {
            throw new NotSupportedException();
        }

        public ITrayIconImpl? CreateTrayIcon()
        {
            // TODO
            return null;
        }

        public void Dispose()
        {
            WlSeat.Dispose();
            WlShm.Dispose();
            XdgWmBase.Dispose();
            XdgActivation.Dispose();
            ZxdgDecorationManager.Dispose();
            ZxdgOutputManager.Dispose();
            WlCompositor.Dispose();
            WlRegistryHandler.Dispose();
            WlDisplay.Dispose();
        }
    }
}

namespace Avalonia
{
    public static class AvaloniaWaylandPlatformExtensions
    {
        public static T UseWayland<T>(this T builder) where T : AppBuilderBase<T>, new() =>
            builder.UseWindowingSubsystem(static () => new AvaloniaWaylandPlatform().Initialize(
                AvaloniaLocator.Current.GetService<WaylandPlatformOptions>() ?? new WaylandPlatformOptions()));
    }

    public class WaylandPlatformOptions
    {
        /// <summary>
        /// Determines whether to use GPU for rendering in your project. The default value is true.
        /// </summary>
        public bool UseGpu { get; set; } = true;

        /// <summary>
        /// Deferred renderer would be used when set to true. Immediate renderer when set to false. The default value is true.
        /// </summary>
        /// <remarks>
        /// Avalonia has two rendering modes: Immediate and Deferred rendering.
        /// Immediate re-renders the whole scene when some element is changed on the scene. Deferred re-renders only changed elements.
        /// </remarks>
        public bool UseDeferredRendering { get; set; } = true;

        /// <summary>
        /// Enables global menu support on Linux desktop environments where it's supported (e. g. XFCE and MATE with plugin, KDE, etc).
        /// The default value is true.
        /// </summary>
        public bool UseDBusMenu { get; set; } = true;
    }
}

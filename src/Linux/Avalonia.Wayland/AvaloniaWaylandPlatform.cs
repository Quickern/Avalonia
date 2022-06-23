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
using NWayland.Protocols.XdgForeignUnstableV2;
using NWayland.Protocols.XdgShell;

namespace Avalonia.Wayland
{
    internal class AvaloniaWaylandPlatform : IWindowingPlatform
    {
        public AvaloniaWaylandPlatform(WaylandPlatformOptions options)
        {
            Options = options;
            WlDisplay = WlDisplay.Connect();
            var registry = WlDisplay.GetRegistry();
            WlRegistryHandler = new WlRegistryHandler(registry);
            WlDisplay.Roundtrip();
            WlCompositor = WlRegistryHandler.BindRequiredInterface(WlCompositor.BindFactory, WlCompositor.InterfaceName, WlCompositor.InterfaceVersion);
            WlSeat = WlRegistryHandler.BindRequiredInterface(WlSeat.BindFactory, WlSeat.InterfaceName, WlSeat.InterfaceVersion);
            WlShm = WlRegistryHandler.BindRequiredInterface(WlShm.BindFactory, WlShm.InterfaceName, WlShm.InterfaceVersion);
            WlDataDeviceManager = WlRegistryHandler.BindRequiredInterface(WlDataDeviceManager.BindFactory, WlDataDeviceManager.InterfaceName, WlDataDeviceManager.InterfaceVersion);
            XdgWmBase = WlRegistryHandler.BindRequiredInterface(XdgWmBase.BindFactory, XdgWmBase.InterfaceName, XdgWmBase.InterfaceVersion);
            XdgActivation = WlRegistryHandler.BindRequiredInterface(XdgActivationV1.BindFactory, XdgActivationV1.InterfaceName, XdgActivationV1.InterfaceVersion);
            ZxdgDecorationManager = WlRegistryHandler.BindRequiredInterface(ZxdgDecorationManagerV1.BindFactory, ZxdgDecorationManagerV1.InterfaceName, ZxdgDecorationManagerV1.InterfaceVersion);
            ZxdgExporter = WlRegistryHandler.BindRequiredInterface(ZxdgExporterV2.BindFactory, ZxdgExporterV2.InterfaceName, ZxdgExporterV2.InterfaceVersion);

            var wlDataHandler = new WlDataHandler(this);
            AvaloniaLocator.CurrentMutable.Bind<IWindowingPlatform>().ToConstant(this)
                .Bind<IPlatformThreadingInterface>().ToConstant(new WlPlatformThreading(this))
                .Bind<IRenderTimer>().ToConstant(new DefaultRenderTimer(60))
                .Bind<IRenderLoop>().ToConstant(new RenderLoop())
                .Bind<PlatformHotkeyConfiguration>().ToConstant(new PlatformHotkeyConfiguration(KeyModifiers.Control))
                .Bind<IKeyboardDevice>().ToConstant(new KeyboardDevice())
                .Bind<ICursorFactory>().ToConstant(new WlCursorFactory(this))
                .Bind<IClipboard>().ToConstant(wlDataHandler)
                .Bind<IPlatformDragSource>().ToConstant(wlDataHandler)
                .Bind<IPlatformIconLoader>().ToConstant(new IconLoaderStub())
                .Bind<ISystemDialogImpl>().ToConstant(DBusSystemDialog.TryCreate() as ISystemDialogImpl ?? new ManagedFileDialogExtensions.ManagedSystemDialogImpl<Window>())
                .Bind<IMountedVolumeInfoProvider>().ToConstant(new LinuxMountedVolumeInfoProvider());

            WlScreens = new WlScreens(this);
            WlInputDevice = new WlInputDevice(this);

            if (options.UseGpu)
            {
                var egl = EglPlatformOpenGlInterface.TryCreate(() => new EglDisplay(new EglInterface(), false, 0x31D8, WlDisplay.Handle, null));
                if (egl is not null)
                    AvaloniaLocator.CurrentMutable.Bind<IPlatformOpenGlInterface>().ToConstant(egl);
            }
        }

        internal WaylandPlatformOptions Options { get; }

        internal WlDisplay WlDisplay { get; }

        internal WlRegistryHandler WlRegistryHandler { get; }

        internal WlCompositor WlCompositor { get; }

        internal WlSeat WlSeat { get; }

        internal WlShm WlShm { get; }

        internal WlDataDeviceManager WlDataDeviceManager { get; }

        internal XdgWmBase XdgWmBase { get; }

        internal XdgActivationV1 XdgActivation { get; }

        internal ZxdgDecorationManagerV1 ZxdgDecorationManager { get; }

        internal ZxdgExporterV2 ZxdgExporter { get; }

        internal WlScreens WlScreens { get; }

        internal WlInputDevice WlInputDevice { get; }

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
            builder.UseWindowingSubsystem(static () =>
            {
                var options = AvaloniaLocator.Current.GetService<WaylandPlatformOptions>() ?? new WaylandPlatformOptions();
                var platform = new AvaloniaWaylandPlatform(options);
                AvaloniaLocator.CurrentMutable.BindToSelf(platform);
            });
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

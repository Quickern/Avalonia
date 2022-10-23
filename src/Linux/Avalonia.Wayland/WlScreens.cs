using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Platform;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland
{
    internal class WlScreens : IScreenImpl, IDisposable
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly Dictionary<uint, WlScreen> _wlScreens = new();
        private readonly Dictionary<WlOutput, WlScreen> _wlOutputs = new();
        private readonly Dictionary<WlSurface, WlWindow> _wlWindows = new();
        private readonly List<WlScreen> _allScreens = new();

        public WlScreens(AvaloniaWaylandPlatform platform)
        {
            _platform = platform;
            platform.WlRegistryHandler.GlobalAdded += OnGlobalAdded;
            _platform.WlRegistryHandler.GlobalRemoved += OnGlobalRemoved;
        }

        public int ScreenCount => _allScreens.Count;

        public IReadOnlyList<Screen> AllScreens => _allScreens.Select(static x => x.ToScreen()).ToList();

        public WlWindow? KeyboardFocus { get; private set; }

        public WlWindow? PointerFocus { get; private set; }

        public WlWindow? TouchFocus { get; private set; }

        public Screen? ScreenFromWindow(IWindowBaseImpl window) => ScreenHelper.ScreenFromWindow(window, AllScreens);

        public Screen? ScreenFromPoint(PixelPoint point) => ScreenHelper.ScreenFromPoint(point, AllScreens);

        public Screen? ScreenFromRect(PixelRect rect) => ScreenHelper.ScreenFromRect(rect, AllScreens);

        public void Dispose()
        {
            _platform.WlRegistryHandler.GlobalAdded -= OnGlobalAdded;
            _platform.WlRegistryHandler.GlobalRemoved -= OnGlobalRemoved;
            foreach (var wlScreen in _wlScreens.Values)
                wlScreen.Dispose();
        }

        internal Screen ScreenFromOutput(WlOutput wlOutput) => _wlOutputs[wlOutput].ToScreen();

        internal void AddWindow(WlWindow window) => _wlWindows.Add(window.WlSurface, window);

        internal void RemoveWindow(WlWindow window)
        {
            _wlWindows.Remove(window.WlSurface);
            if (KeyboardFocus == window)
                KeyboardFocus = window.Parent;
            if (PointerFocus == window)
                PointerFocus = window.Parent;
            if (TouchFocus == window)
                TouchFocus = window.Parent;
        }

        internal void SetKeyboardFocus(WlSurface? surface)
        {
            if (surface is null)
                KeyboardFocus = null;
            else
                KeyboardFocus = _wlWindows.TryGetValue(surface, out var window) ? window : null;
        }

        internal void SetPointerFocus(WlSurface? surface)
        {
            if (surface is null)
                PointerFocus = null;
            else
                PointerFocus = _wlWindows.TryGetValue(surface, out var window) ? window : null;
        }

        internal void SetTouchFocus(WlSurface? surface)
        {
            if (surface is null)
                TouchFocus = null;
            else
                TouchFocus = _wlWindows.TryGetValue(surface, out var window) ? window : null;
        }

        private void OnGlobalAdded(WlRegistryHandler.GlobalInfo globalInfo)
        {
            if (globalInfo.Interface != WlOutput.InterfaceName)
                return;
            var wlOutput = _platform.WlRegistryHandler.BindRequiredInterface(WlOutput.BindFactory, WlOutput.InterfaceVersion, globalInfo);
            var wlScreen = new WlScreen(wlOutput);
            _wlScreens.Add(globalInfo.Name, wlScreen);
            _wlOutputs.Add(wlOutput, wlScreen);
            _allScreens.Add(wlScreen);
        }

        private void OnGlobalRemoved(WlRegistryHandler.GlobalInfo globalInfo)
        {
            if (globalInfo.Interface is not WlOutput.InterfaceName || !_wlScreens.TryGetValue(globalInfo.Name, out var wlScreen))
                return;
            _wlScreens.Remove(globalInfo.Name);
            _wlOutputs.Remove(wlScreen.WlOutput);
            _allScreens.Remove(wlScreen);
            wlScreen.Dispose();
        }

        private sealed class WlScreen : WlOutput.IEvents, IDisposable
        {
            private PixelRect _bounds;
            private double _scaling;
            private bool _isPreferred;
            private Screen? _screen;

            public WlScreen(WlOutput wlOutput)
            {
                WlOutput = wlOutput;
                wlOutput.Events = this;
            }

            public WlOutput WlOutput { get; }

            public Screen ToScreen() => _screen!;

            public void OnGeometry(WlOutput eventSender, int x, int y, int physicalWidth, int physicalHeight, WlOutput.SubpixelEnum subpixel, string make, string model, WlOutput.TransformEnum transform)
                => _bounds = new PixelRect(x, y, physicalWidth, physicalHeight);

            public void OnMode(WlOutput eventSender, WlOutput.ModeEnum flags, int width, int height, int refresh)
            {
                _isPreferred = flags == WlOutput.ModeEnum.Preferred;
                _bounds = new PixelRect(_bounds.X, _bounds.Y, width, height);
            }

            public void OnScale(WlOutput eventSender, int factor) => _scaling = factor;

            public void OnName(WlOutput eventSender, string name) { }

            public void OnDescription(WlOutput eventSender, string description) { }

            public void OnDone(WlOutput eventSender) => _screen = new Screen(_scaling, _bounds, _bounds, _isPreferred);

            public void Dispose() => WlOutput.Dispose();
        }
    }
}

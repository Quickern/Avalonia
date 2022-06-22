using System;
using System.Collections.Generic;
using Avalonia.FreeDesktop.DBusIme.Fcitx;
using Avalonia.FreeDesktop.DBusIme.IBus;
using Tmds.DBus;

namespace Avalonia.FreeDesktop.DBusIme
{
    internal static class X11DBusImeHelper
    {
        private static readonly Dictionary<string, Func<Connection, IX11InputMethodFactory>> _knownMethods = new()
            {
                ["fcitx"] = conn =>
                    new DBusInputMethodFactory<FcitxX11TextInputMethod>(_ => new FcitxX11TextInputMethod(conn)),
                ["ibus"] = conn =>
                    new DBusInputMethodFactory<IBusX11TextInputMethod>(_ => new IBusX11TextInputMethod(conn))
            };

        private static Func<Connection, IX11InputMethodFactory>? DetectInputMethod()
        {
            foreach (var name in new[] { "AVALONIA_IM_MODULE", "GTK_IM_MODULE", "QT_IM_MODULE" })
            {
                var value = Environment.GetEnvironmentVariable(name);

                if (value == "none")
                    return null;

                if (value != null && _knownMethods.TryGetValue(value, out var factory))
                    return factory;
            }

            return null;
        }

        public static bool DetectAndRegister()
        {
            var factory = DetectInputMethod();
            if (factory is null)
                return false;
            var conn = DBusHelper.TryInitialize();
            if (conn is null)
                return false;
            AvaloniaLocator.CurrentMutable.Bind<IX11InputMethodFactory>().ToConstant(factory(conn));
            return true;
        }
    }
}

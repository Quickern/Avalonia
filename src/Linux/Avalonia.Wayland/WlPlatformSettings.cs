using System;
using Avalonia.Platform;

namespace Avalonia.Wayland
{
    internal class WlPlatformSettings : IPlatformSettings
    {
        public Size DoubleClickSize { get; } = new(2, 2);

        public TimeSpan DoubleClickTime { get; } = TimeSpan.FromMilliseconds(500);

        /// <inheritdoc cref="IPlatformSettings.TouchDoubleClickSize"/>
        public Size TouchDoubleClickSize { get; } = new(16, 16);

        /// <inheritdoc cref="IPlatformSettings.TouchDoubleClickTime"/>
        public TimeSpan TouchDoubleClickTime => DoubleClickTime;
    }
}

namespace Avalonia.Logging
{
    /// <summary>
    /// Specifies the area in which a log event occurred.
    /// </summary>
    public static class LogArea
    {
        /// <summary>
        /// The log event comes from the property system.
        /// </summary>
        public const string Property = nameof(Property);

        /// <summary>
        /// The log event comes from the binding system.
        /// </summary>
        public const string Binding = nameof(Binding);

        /// <summary>
        /// The log event comes from the animations system.
        /// </summary>
        public const string Animations = nameof(Animations);

        /// <summary>
        /// The log event comes from the visual system.
        /// </summary>
        public const string Visual = nameof(Visual);

        /// <summary>
        /// The log event comes from the layout system.
        /// </summary>
        public const string Layout = nameof(Layout);

        /// <summary>
        /// The log event comes from the control system.
        /// </summary>
        public const string Control = nameof(Control);

        /// <summary>
        /// The log event comes from Win32Platform.
        /// </summary>
        public const string Win32Platform = nameof(Win32Platform);

        /// <summary>
        /// The log event comes from X11Platform.
        /// </summary>
        public const string X11Platform = nameof(X11Platform);

        /// <summary>
        /// The log event comes from X11Platform.
        /// </summary>
        public const string WaylandPlatform = nameof(WaylandPlatform);
    }
}

using System;

namespace Avalonia.Platform
{
    /// <summary>
    /// Represents a single display screen.
    /// </summary>
    public class Screen
    {
        /// <summary>
        /// Gets the scaling factor applied to the screen by the operating system.
        /// </summary>
        /// <remarks>
        /// Multiply this value by 100 to get a percentage.
        /// Both X and Y scaling factors are assumed uniform.
        /// </remarks>
        public double Scaling { get; protected set; }

        /// <inheritdoc cref="Scaling"/>
        [Obsolete("Use the Scaling property instead.")]
        public double PixelDensity => Scaling;

        /// <summary>
        /// Gets the overall pixel-size of the screen.
        /// </summary>
        /// <remarks>
        /// This generally is the raw pixel counts in both the X and Y direction.
        /// </remarks>
        public PixelRect Bounds { get; protected set; }

        /// <summary>
        /// Gets the actual working-area pixel-size of the screen.
        /// </summary>
        /// <remarks>
        /// This area may be smaller than <see href="Bounds"/> to account for notches and
        /// other block-out areas such as taskbars etc.
        /// </remarks>
        public PixelRect WorkingArea { get; protected set; }

        /// <summary>
        /// Gets a value indicating whether the screen is the primary one.
        /// </summary>
        public bool IsPrimary { get; }

        /// <inheritdoc cref="IsPrimary"/>
        [Obsolete("Use the IsPrimary property instead.")]
        public bool Primary => IsPrimary;

        /// <summary>
        /// Initializes a new instance of the <see cref="Screen"/> class.
        /// </summary>
        protected Screen() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="Screen"/> class.
        /// </summary>
        /// <param name="scaling">The scaling factor applied to the screen by the operating system.</param>
        /// <param name="bounds">The overall pixel-size of the screen.</param>
        /// <param name="workingArea">The actual working-area pixel-size of the screen.</param>
        /// <param name="isPrimary">Whether the screen is the primary one.</param>
        public Screen(double scaling, PixelRect bounds, PixelRect workingArea, bool isPrimary)
        {
            Scaling = scaling;
            Bounds = bounds;
            WorkingArea = workingArea;
            IsPrimary = isPrimary;
        }
    }
}

namespace Avalonia.Platform
{
    public class Screen
    {
        public double PixelDensity { get; protected set; }

        public PixelRect Bounds { get; protected set; }

        public PixelRect WorkingArea { get; protected set; }

        public bool Primary { get; protected set; }

        protected Screen() { }

        public Screen(double pixelDensity, PixelRect bounds, PixelRect workingArea, bool primary)
        {
            PixelDensity = pixelDensity;
            Bounds = bounds;
            WorkingArea = workingArea;
            Primary = primary;
        }
    }
}

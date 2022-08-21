using System;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Avalonia.Controls.Chrome
{
    public class ClientSideDecorations : TemplatedControl
    {
        private IDisposable? _toggleVisibilityDisposable;

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);

            SetupResizeBorder(e, "PART_Top", StandardCursorType.TopSide, WindowEdge.North);
            SetupResizeBorder(e, "PART_Right", StandardCursorType.RightSide, WindowEdge.East);
            SetupResizeBorder(e, "PART_Bottom", StandardCursorType.BottomSide, WindowEdge.South);
            SetupResizeBorder(e, "PART_Left", StandardCursorType.LeftSide, WindowEdge.West);
            SetupResizeBorder(e, "PART_TopLeft", StandardCursorType.TopLeftCorner, WindowEdge.NorthWest);
            SetupResizeBorder(e, "PART_TopRight", StandardCursorType.TopRightCorner, WindowEdge.NorthEast);
            SetupResizeBorder(e, "PART_BottomLeft", StandardCursorType.BottomLeftCorner, WindowEdge.SouthWest);
            SetupResizeBorder(e, "PART_BottomRight", StandardCursorType.BottomRightCorner, WindowEdge.SouthEast);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (VisualRoot is Window window)
                _toggleVisibilityDisposable = window.GetObservable(Window.ExtendClientAreaChromeHintsProperty)
                    .Subscribe(_ => IsVisible = window.PlatformImpl?.NeedsManagedDecorations ?? false);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            _toggleVisibilityDisposable?.Dispose();
        }

        private void SetupResizeBorder(TemplateAppliedEventArgs e, string name, StandardCursorType cursor, WindowEdge edge)
        {
            var control = e.NameScope.Get<ResizeBorder>(name);
            control.Cursor = new Cursor(cursor);
            control.PointerPressed += (_, args) => (VisualRoot as Window)?.PlatformImpl?.BeginResizeDrag(edge, args);
        }
    }
}

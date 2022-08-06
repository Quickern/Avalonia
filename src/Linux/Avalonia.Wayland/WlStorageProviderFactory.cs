using System;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform.Storage;

namespace Avalonia.Wayland
{
    internal class WlStorageProviderFactory : IStorageProviderFactory
    {
        public IStorageProvider CreateProvider(TopLevel topLevel) => topLevel is Window window
            ? new WlCompositeStorageProvider(window)
            : throw new InvalidOperationException("Cannot create storage provider from non-toplevel windows.");
    }
}

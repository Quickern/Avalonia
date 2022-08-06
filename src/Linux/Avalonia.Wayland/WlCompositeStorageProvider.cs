using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Dialogs;
using Avalonia.FreeDesktop;
using Avalonia.Platform.Storage;

namespace Avalonia.Wayland
{
    internal class WlCompositeStorageProvider : IStorageProvider
    {
        private readonly Window _window;

        private IStorageProvider? _storageProvider;

        public WlCompositeStorageProvider(Window window)
        {
            _window = window;
        }

        public bool CanOpen => true;

        public bool CanSave => true;

        public bool CanPickFolder => true;

        private async ValueTask<IStorageProvider> EnsureStorageProvider()
        {
            if (_storageProvider is not null)
                return _storageProvider;

            var windowHandle = (_window.PlatformImpl as WlToplevel)?.ExportedToplevelHandle;
            _storageProvider = windowHandle is not null
                ? await DBusSystemDialog.TryCreate(windowHandle)
                : new ManagedStorageProvider<Window>(_window, AvaloniaLocator.Current.GetService<ManagedFileDialogOptions>());

            if (_storageProvider is not null)
                return _storageProvider;

            throw new InvalidOperationException("No storage provider found");
        }

        public async Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options)
        {
            var provider = await EnsureStorageProvider().ConfigureAwait(false);
            return await provider.OpenFilePickerAsync(options).ConfigureAwait(false);
        }

        public async Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions options)
        {
            var provider = await EnsureStorageProvider().ConfigureAwait(false);
            return await provider.SaveFilePickerAsync(options).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<IStorageFolder>> OpenFolderPickerAsync(FolderPickerOpenOptions options)
        {
            var provider = await EnsureStorageProvider().ConfigureAwait(false);
            return await provider.OpenFolderPickerAsync(options).ConfigureAwait(false);
        }

        public async Task<IStorageBookmarkFile?> OpenFileBookmarkAsync(string bookmark)
        {
            var provider = await EnsureStorageProvider().ConfigureAwait(false);
            return await provider.OpenFileBookmarkAsync(bookmark).ConfigureAwait(false);
        }

        public async Task<IStorageBookmarkFolder?> OpenFolderBookmarkAsync(string bookmark)
        {
            var provider = await EnsureStorageProvider().ConfigureAwait(false);
            return await provider.OpenFolderBookmarkAsync(bookmark).ConfigureAwait(false);
        }
    }
}

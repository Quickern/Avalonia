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
            if (windowHandle is not null)
                _storageProvider = await DBusSystemDialog.TryCreate(windowHandle);
            _storageProvider ??= new ManagedStorageProvider<Window>(_window, AvaloniaLocator.Current.GetService<ManagedFileDialogOptions>());
            return _storageProvider;
        }

        public async Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options)
        {
            var provider = await EnsureStorageProvider();
            return await provider.OpenFilePickerAsync(options);
        }

        public async Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions options)
        {
            var provider = await EnsureStorageProvider();
            return await provider.SaveFilePickerAsync(options);
        }

        public async Task<IReadOnlyList<IStorageFolder>> OpenFolderPickerAsync(FolderPickerOpenOptions options)
        {
            var provider = await EnsureStorageProvider();
            return await provider.OpenFolderPickerAsync(options);
        }

        public async Task<IStorageBookmarkFile?> OpenFileBookmarkAsync(string bookmark)
        {
            var provider = await EnsureStorageProvider();
            return await provider.OpenFileBookmarkAsync(bookmark);
        }

        public async Task<IStorageBookmarkFolder?> OpenFolderBookmarkAsync(string bookmark)
        {
            var provider = await EnsureStorageProvider();
            return await provider.OpenFolderBookmarkAsync(bookmark);
        }
    }
}

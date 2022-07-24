using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Avalonia.Wayland
{
    internal class CompositeStorageProvider : IStorageProvider
    {
        private readonly IEnumerable<Func<Task<IStorageProvider?>>> _factories;

        private IStorageProvider? _storageProvider;

        public CompositeStorageProvider(IEnumerable<Func<Task<IStorageProvider?>>> factories)
        {
            _factories = factories;
        }

        public bool CanOpen => true;

        public bool CanSave => true;

        public bool CanPickFolder => true;

        private async ValueTask<IStorageProvider> EnsureStorageProvider()
        {
            if (_storageProvider is not null)
                return _storageProvider;

            foreach (var factory in _factories)
            {
                _storageProvider = await factory();
                if (_storageProvider is not null)
                    return _storageProvider;
            }

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

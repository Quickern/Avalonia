using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.FreeDesktop;
using Avalonia.Input;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland
{
    internal sealed class WlDataObject : IDataObject, IDisposable, WlDataOffer.IEvents
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly DataObject _cache;

        public WlDataObject(AvaloniaWaylandPlatform platform, WlDataOffer wlDataOffer)
        {
            _platform = platform;
            WlDataOffer = wlDataOffer;
            WlDataOffer.Events = this;
            MimeTypes = new List<string>();
            _cache = new DataObject();
        }

        internal WlDataOffer WlDataOffer { get; }

        internal List<string> MimeTypes { get; }

        internal DragDropEffects OfferedDragDropEffects { get; private set; }

        internal DragDropEffects MatchedDragDropEffects { get; private set; }

        public IEnumerable<string> GetDataFormats()
        {
            foreach (var mimeType in MimeTypes)
            {
                switch (mimeType)
                {
                    case Wayland.MimeTypes.Text:
                    case Wayland.MimeTypes.TextUtf8:
                        yield return DataFormats.Text;
                        break;
                    case Wayland.MimeTypes.UriList:
                        yield return DataFormats.FileNames;
                        break;
                    default:
                        yield return mimeType;
                        break;
                }
            }
        }

        public bool Contains(string dataFormat) => dataFormat switch
            {
                DataFormats.Text => MimeTypes.Contains(Wayland.MimeTypes.Text) || MimeTypes.Contains(Wayland.MimeTypes.TextUtf8),
                DataFormats.FileNames => MimeTypes.Contains(Wayland.MimeTypes.UriList),
                _ => MimeTypes.Contains(dataFormat)
            };

        public string? GetText()
        {
            if (_cache.GetText() is { } text)
                return text;
            var mimeType = MimeTypes.FirstOrDefault(static x => x is Wayland.MimeTypes.Text) ?? MimeTypes.FirstOrDefault(static x => x is Wayland.MimeTypes.TextUtf8);
            if (mimeType is null)
                return null;
            var fd = Receive(mimeType);
            var result = fd < 0 ? null : ReceiveText(fd);
            _cache.Set(DataFormats.Text, result);
            return result;
        }

        public IEnumerable<string>? GetFileNames()
        {
            if (_cache.GetFileNames() is { } fileNames)
                return fileNames;
            if (!MimeTypes.Contains(Wayland.MimeTypes.UriList))
                return null;
            var fd = Receive(Wayland.MimeTypes.UriList);
            var result = fd < 0 ? null : ReceiveText(fd).Split('\n');
            _cache.Set(DataFormats.FileNames, result);
            return result;
        }

        public unsafe object? Get(string dataFormat)
        {
            switch (dataFormat)
            {
                case DataFormats.Text:
                    return GetText();
                case DataFormats.FileNames:
                    return GetFileNames();
            }

            if (_cache.Get(dataFormat) is { } obj)
                return obj;

            if (!MimeTypes.Contains(dataFormat))
                return null;

            var fd = Receive(dataFormat);
            if (fd < 0)
                return null;

            var buffer = new byte[1024];
            var ms = new MemoryStream();
            fixed (byte* ptr = buffer)
            {
                while (true)
                {
                    var read = LibC.read(fd, (IntPtr)ptr, 1024);
                    if (read <= 0)
                        break;
                    ms.Write(buffer, 0, read);
                }
            }

            LibC.close(fd);
            var result = ms.ToArray();
            _cache.Set(dataFormat, result);
            return result;
        }

        public void OnOffer(WlDataOffer eventSender, string mimeType) => MimeTypes.Add(mimeType);

        public void OnSourceActions(WlDataOffer eventSender, WlDataDeviceManager.DndActionEnum sourceActions) => OfferedDragDropEffects = (DragDropEffects)sourceActions;

        public void OnAction(WlDataOffer eventSender, WlDataDeviceManager.DndActionEnum dndAction) => MatchedDragDropEffects = (DragDropEffects)dndAction;

        public void Dispose() => WlDataOffer.Dispose();

        private unsafe int Receive(string mimeType)
        {
            var fds = stackalloc int[2];
            if (LibC.pipe2(fds, FileDescriptorFlags.O_RDONLY) < 0)
            {
                WlDataOffer.Dispose();
                return -1;
            }

            WlDataOffer.Receive(mimeType, fds[1]);
            _platform.WlDisplay.Roundtrip();
            LibC.close(fds[1]);
            return fds[0];
        }

        private static unsafe string ReceiveText(int fd)
        {
            Span<byte> buffer = stackalloc byte[1024];
            var sb = new StringBuilder();
            fixed (byte* ptr = buffer)
            {
                while (true)
                {
                    var read = LibC.read(fd, (IntPtr)ptr, 1024);
                    if (read <= 0)
                        break;
                    sb.Append(Encoding.UTF8.GetString(ptr, read));
                }
            }

            LibC.close(fd);
            return sb.ToString();
        }
    }
}

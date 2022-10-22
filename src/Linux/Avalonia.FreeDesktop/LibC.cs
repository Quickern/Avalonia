using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Avalonia.FreeDesktop
{
    internal static class LibC
    {
        private const string C = "libc";

        [DllImport(C, SetLastError = true)]
        private static extern long readlink([MarshalAs(UnmanagedType.LPArray)] byte[] filename,
                                            [MarshalAs(UnmanagedType.LPArray)] byte[] buffer,
                                            long len);

        [DllImport(C, SetLastError = true)]
        public static extern int open(string pathname, int flags, int mode);

        [DllImport(C, SetLastError = true)]
        public static extern int close(int fd);

        [DllImport(C, SetLastError = true)]
        public static extern int read(int fd, IntPtr buffer, int count);

        [DllImport(C, SetLastError = true)]
        public static extern int write(int fd, IntPtr buffer, int count);

        [DllImport(C, SetLastError = true)]
        public static extern unsafe int pipe2(int* fds, FileDescriptorFlags flags);

        [DllImport(C, SetLastError = true)]
        public static extern unsafe int ioctl(int fd, FbIoCtl code, void* arg);

        [DllImport(C, SetLastError = true)]
        public static extern IntPtr mmap(IntPtr addr, IntPtr length, MemoryProtection prot, SharingType flags, int fd, IntPtr offset);

        [DllImport(C, SetLastError = true)]
        public static extern int munmap(IntPtr addr, IntPtr length);

        [DllImport(C, SetLastError = true)]
        public static extern int memcpy(IntPtr dest, IntPtr src, IntPtr length);

        [DllImport(C, SetLastError = true)]
        public static extern int memfd_create(string name, MemoryFileCreation flags);

        [DllImport(C, SetLastError = true)]
        public static extern int ftruncate(int fd, int size);

        [DllImport(C, SetLastError = true)]
        public static extern int fcntl(int fd, FileSealCommand cmd, FileSeals flags);

        [DllImport(C, SetLastError = true)]
        public static extern unsafe int poll(pollfd* fds, int nfds, int timeout);

        [DllImport(C, SetLastError = true)]
        public static extern int epoll_create1(int size);

        [DllImport(C, SetLastError = true)]
        public static extern unsafe int epoll_ctl(int epfd, EpollCommands op, int fd, epoll_event* __event);

        [DllImport(C, SetLastError = true)]
        public static extern unsafe int epoll_wait(int epfd, epoll_event* events, int maxevents, int timeout);

        public static string ReadLink(string path)
        {
            var symlinkSize = Encoding.UTF8.GetByteCount(path);
            const int bufferSize = 4097; // PATH_MAX is (usually?) 4096, but we need to know if the result was truncated

            var symlink = ArrayPool<byte>.Shared.Rent(symlinkSize + 1);
            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

            try
            {
                Encoding.UTF8.GetBytes(path, 0, path.Length, symlink, 0);
                symlink[symlinkSize] = 0;

                var size = readlink(symlink, buffer, bufferSize);
                Debug.Assert(size < bufferSize); // if this fails, we need to increase the buffer size (dynamically?)

                return Encoding.UTF8.GetString(buffer, 0, (int)size);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(symlink);
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public enum Errno
    {
        EINTR = 4,
        EAGAIN = 11,
        EPIPE = 32
    }

    [Flags]
    public enum MemoryProtection
    {
        PROT_NONE = 0,
        PROT_READ = 1,
        PROT_WRITE = 2,
        PROT_EXEC = 4
    }

    public enum SharingType
    {
        MAP_SHARED = 1,
        MAP_PRIVATE = 2
    }

    [Flags]
    public enum MemoryFileCreation : uint
    {
        MFD_CLOEXEC = 1,
        MFD_ALLOW_SEALING = 2,
        MFD_HUGETLB = 4
    }

    public enum FileSealCommand
    {
        F_ADD_SEALS = 1024 + 9,
        F_GET_SEALS = 1024 + 10
    }

    [Flags]
    public enum FileSeals
    {
        F_SEAL_SEAL = 1,
        F_SEAL_SHRINK = 2,
        F_SEAL_GROW = 4,
        F_SEAL_WRITE = 8,
        F_SEAL_FUTURE_WRITE = 16
    }

    [Flags]
    public enum FileDescriptorFlags
    {
        O_RDONLY = 0,
        O_NONBLOCK = 2048,
        O_DIRECT = 40000,
        O_CLOEXEC = 2000000
    }

    [Flags]
    public enum EpollEvents : uint
    {
        EPOLLIN = 1,
        EPOLLPRI = 2,
        EPOLLOUT = 4,
        EPOLLRDNORM = 64,
        EPOLLRDBAND = 128,
        EPOLLWRNORM = 256,
        EPOLLWRBAND = 512,
        EPOLLMSG = 1024,
        EPOLLERR = 8,
        EPOLLHUP = 16,
        EPOLLRDHUP = 8192
    }

    public enum EpollCommands
    {
        EPOLL_CTL_ADD = 1,
        EPOLL_CTL_DEL = 2,
        EPOLL_CTL_MOD = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct pollfd
    {
        public int   fd;         /* file descriptor */
        public short events;     /* requested events */
        public short revents;    /* returned events */
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct epoll_data
    {
        [FieldOffset(0)]
        public IntPtr ptr;
        [FieldOffset(0)]
        public int fd;
        [FieldOffset(0)]
        public uint u32;
        [FieldOffset(0)]
        public ulong u64;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct epoll_event
    {
        public EpollEvents events;
        public epoll_data data;
    }
}

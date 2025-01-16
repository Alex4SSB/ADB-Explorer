// Copied and modified from https://github.com/asklar/clipview under MIT License

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace AdbDataObject
{
    public class FileContentsStream : IDisposable
    {
        private readonly STATSTG stat;

        private readonly IStream stream;

        public FileContentsStream(IStream stream)
        {
            this.stream = stream;
            stream.Stat(out stat, 0);
        }

        public string FileName => stat.pwcsName;
        public long Length => stat.cbSize;

        enum STREAM_SEEK
        {
            STREAM_SEEK_SET,
            STREAM_SEEK_CUR,
            STREAM_SEEK_END
        };

        public void SaveToStream(Stream outputStream)
        {
            stream.Seek(0, (int)STREAM_SEEK.STREAM_SEEK_SET, IntPtr.Zero);
            byte[] buffer = new byte[stat.cbSize];
            int cbRead = 0;
            unsafe
            {
                IntPtr pcbRead = new IntPtr((void*)&cbRead);
                try
                {
                    do
                    {
                        stream.Read(buffer, buffer.Length, pcbRead);
                        outputStream.Write(buffer, 0, cbRead);
                    } while (cbRead >= buffer.Length);
                }
                catch (EndOfStreamException)
                { }
            }
        }

        public void Save(string filepath)
        {
            using var file = File.Create(filepath);

            SaveToStream(file);
        }

        public void Dispose()
        {
            Marshal.ReleaseComObject(stream);
        }
    }
}

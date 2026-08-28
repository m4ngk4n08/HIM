using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.ServiceModel
{

    #region Infrastructure: SSH-to-Spectre Bridge


    /// <summary>
    /// A custom TextWriter that wraps an SSH network stream, ensuring immediate flushing
    /// and correct character encoding for terminal rendering.
    /// </summary>
    public class SshTextWriter : TextWriter
    {
        private readonly Stream _stream;
        private Encoding _encoding = Encoding.UTF8;
        private bool _isFaulted = false;

        public override Encoding Encoding => _encoding;

        public SshTextWriter(Stream stream) => _stream = stream;
        public SshTextWriter(Stream stream, Encoding encoding)
        {
            _stream = stream;
            _encoding = encoding ?? Encoding.UTF8;
        }

        public void ApplyEncoding(Encoding encoding) => _encoding = encoding ?? Encoding.UTF8;
        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value) || _isFaulted) return;

            try
            {
                var normalizedValue = value.Replace("\r\n", "\n").Replace("\n", "\r\n");
                var bytes = _encoding.GetBytes(normalizedValue);
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();
            }
            catch (Exception ex) when (IsTransportException(ex))  // <-- broadened
            {
                _isFaulted = true;
                // Swallow: session is dead, cleanup is driven by CancellationToken.
            }
        }

        private static bool IsTransportException(Exception ex)
        {
            if (ex is AggregateException ae)
            {
                ex = ae.Flatten().InnerException ?? ex;
            }
            return ex is IOException
                || ex is ObjectDisposedException
                || ex is InvalidOperationException   // "Cannot send more data after EOF"
                || ex is OperationCanceledException;
        }

        public override void Write(char value) => Write(value.ToString());
    }

    #endregion
}

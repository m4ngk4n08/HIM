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
            if (string.IsNullOrEmpty(value)) return;

            // Senior Fix: SSH Terminals in raw mode require \r\n for a new line.
            // A simple \n only moves the cursor down, not to the left margin.
            // This replaces lone \n with \r\n, but avoids creating \r\r\n.
            var normalizedValue = value.Replace("\r\n", "\n").Replace("\n", "\r\n");

            var bytes = _encoding.GetBytes(normalizedValue);
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
        }

        public override void Write(char value) => Write(value.ToString());
    }

    #endregion
}

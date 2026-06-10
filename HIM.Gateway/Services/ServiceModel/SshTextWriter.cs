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

        public void ApplyEncoding(Encoding encoding) => _encoding = encoding ?? Encoding.UTF8;

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            var bytes = _encoding.GetBytes(value);
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
        }

        public override void Write(char value) => Write(value.ToString());
    }

    #endregion
}

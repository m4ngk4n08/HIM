using HIM.Gateway.Services.SSH;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.ServiceModel
{
    #region Infrastructure: SSH-to-Spectre Bridge

    /// <summary>
    /// Bridges Spectre's IAnsiConsoleOutput interface to our SSH-specific stream writer.
    /// </summary>
    public class SshConsoleOutput : IAnsiConsoleOutput
    {
        private readonly SshTextWriter _sshWriter;
        public TextWriter Writer => _sshWriter;
        public bool IsTerminal => true;
        public int Width { get; }
        public int Height { get; }

        public SshConsoleOutput(SshTextWriter writer, int width, int height)
        {
            _sshWriter = writer;
            Width = width > 0 ? width : 80;
            Height = height > 0 ? height : 24;
        }

        /// <summary>
        /// Complies with the IAnsiConsoleOutput contract to sync character encoding.
        /// </summary>
        public void SetEncoding(Encoding encoding) => _sshWriter.ApplyEncoding(encoding);

        public void SetRawMode(bool enable) { }
    }
    #endregion
}

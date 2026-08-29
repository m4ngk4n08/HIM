using Microsoft.DevTunnels.Ssh.IO;
using Microsoft.DevTunnels.Ssh.Messages;

namespace HIM.Gateway.Services.SSH.Messages
{
    /// <summary>
    /// SSH_MSG_CHANNEL_REQUEST "window-change" (RFC 4254 §6.7):
    /// recipient-channel, "window-change", want-reply(false), cols, rows, pixel-width, pixel-height.
    /// </summary>
    /// <remarks>
    /// Microsoft.DevTunnels.Ssh 3.12.29 does not ship a dedicated message type for this request -
    /// verified by decompiling the installed package (no "WindowChange"-named message exists, and
    /// no string constant for "window-change" appears anywhere in the assembly). The only terminal
    /// sizing message the library provides is <see cref="TerminalRequestMessage"/>, built for
    /// "pty-req", whose <c>OnRead</c> unconditionally reads a TERM string before the size fields.
    /// A "window-change" payload has no TERM string, so calling
    /// <c>e.Request.ConvertTo&lt;TerminalRequestMessage&gt;()</c> on it would read the columns
    /// field as a string-length prefix and misparse everything after it. This mirrors exactly the
    /// fields "window-change" carries, in wire order, so it can be used the same way pty-req uses
    /// <see cref="TerminalRequestMessage"/>: <c>e.Request.ConvertTo&lt;WindowChangeMessage&gt;()</c>.
    /// </remarks>
    internal sealed class WindowChangeMessage : ChannelRequestMessage
    {
        public uint Columns { get; set; }

        public uint Rows { get; set; }

        public uint PixelWidth { get; set; }

        public uint PixelHeight { get; set; }

        public WindowChangeMessage()
        {
            RequestType = "window-change";
        }

        protected override void OnRead(ref SshDataReader reader)
        {
            base.OnRead(ref reader);
            Columns = reader.ReadUInt32();
            Rows = reader.ReadUInt32();
            PixelWidth = reader.ReadUInt32();
            PixelHeight = reader.ReadUInt32();
        }

        protected override void OnWrite(ref SshDataWriter writer)
        {
            base.OnWrite(ref writer);
            writer.Write(Columns);
            writer.Write(Rows);
            writer.Write(PixelWidth);
            writer.Write(PixelHeight);
        }
    }
}

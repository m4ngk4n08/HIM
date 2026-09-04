namespace HIM.Gateway.Services.SSH.Interfaces
{
    /// <summary>
    /// The single owner of a session's SSH Stream reads. ConsoleEngineService's outer loop and
    /// CommandDispatcherHelper's nested prompt reader both read through this instead of calling
    /// stream.ReadAsync directly, so bytes one of them pulls off the wire but doesn't consume can
    /// be handed back instead of sitting stranded in a local buffer.
    /// </summary>
    public interface ISessionByteReader
    {
        Task<int> ReadAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct);

        /// <summary>
        /// Returns bytes to the front of the queue so the next ReadAsync call - by either
        /// reader - sees them before anything new comes off the stream.
        /// </summary>
        void PushBack(ReadOnlySpan<byte> bytes);
    }
}

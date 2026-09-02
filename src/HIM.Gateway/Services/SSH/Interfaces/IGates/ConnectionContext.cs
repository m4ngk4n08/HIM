namespace HIM.Gateway.Services.SSH.Interfaces.IGates
{
    /// <summary>
    /// What a connection gate needs to decide. Deliberately minimal — no TcpClient, so no gate
    /// can reach into the socket. A readonly record struct so the accept loop allocates nothing
    /// per connection.
    /// </summary>
    public readonly record struct ConnectionContext(string IpAddress);
}

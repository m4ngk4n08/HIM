namespace HIM.Gateway.Services.SSH.Interfaces.IGates
{
    /// <summary>
    /// A gate that acquires a resource during Evaluate and must release it later, once the
    /// connection it admitted has ended. Deliberately not on IConnectionGate itself — three of
    /// the four gates have nothing to release, and an interface with three no-op implementations
    /// is how the next person learns to ignore it.
    /// </summary>
    public interface IConnectionSlotGate : IConnectionGate
    {
        void Release(ConnectionContext ctx);
    }
}

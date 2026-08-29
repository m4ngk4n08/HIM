using HIM.Gateway.Models.Knowledge;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    public interface IPortfolioDataProvider
    {
        PortfolioData? Data { get; }
    }
}

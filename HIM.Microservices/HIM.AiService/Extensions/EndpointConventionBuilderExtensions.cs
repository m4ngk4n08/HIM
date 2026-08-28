using Microsoft.Extensions.DependencyInjection;

namespace HIM.AiService.Extensions
{
    /// <summary>
    /// Lets an <see cref="IEndpointFilter"/> be attached to controller-routed endpoints -
    /// AddEndpointFilter only works out of the box on minimal-API RouteHandlerBuilders.
    /// </summary>
    public static class EndpointConventionBuilderExtensions
    {
        public static IEndpointConventionBuilder AddControllerEndpointFilter<TFilter>(this IEndpointConventionBuilder builder)
            where TFilter : IEndpointFilter
        {
            builder.Add(endpointBuilder =>
            {
                endpointBuilder.FilterFactories.Add((factoryContext, next) =>
                {
                    var filter = ActivatorUtilities.CreateInstance<TFilter>(factoryContext.ApplicationServices);
                    return invocationContext => filter.InvokeAsync(invocationContext, next);
                });
            });

            return builder;
        }
    }
}

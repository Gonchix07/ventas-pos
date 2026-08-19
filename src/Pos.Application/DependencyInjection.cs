using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Common.Behaviors;

namespace Pos.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);

        // Orden del pipeline: Validación → Auditoría → Transacción → Handler
        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(AuditBehavior<,>));
        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}

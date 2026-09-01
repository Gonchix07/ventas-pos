using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Abstractions;
using Pos.Application.Abstractions.Fiscal;
using Pos.Application.Abstractions.Payments;
using Pos.Infrastructure.Adapters;
using Pos.Infrastructure.Adapters.Hasar;
using Pos.Infrastructure.Audit;
using Pos.Infrastructure.Persistence;
using Pos.Infrastructure.Security;
using Pos.Infrastructure.Storage;

namespace Pos.Infrastructure;

public static class DependencyInjection
{
    // contentRootPath resuelve la ruta relativa de Storage:CertificadosPath (App_Data/certificados
    // por defecto) contra la raíz del proyecto host, en vez de contra el bin de ejecución (que se
    // pisa en cada build/deploy y no es un lugar donde persistir nada).
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, string contentRootPath)
    {
        services.AddDbContext<PosDbContext>(opt =>
            opt.UseSqlServer(config.GetConnectionString("Pos"),
                sql => sql
                    .MigrationsAssembly(typeof(PosDbContext).Assembly.FullName)
                    // Declara explícito el comportamiento que EF ya usaba por defecto (una sola
                    // consulta con JOINs) para silenciar el warning "MultipleCollectionInclude" sin
                    // cambiar el comportamiento real. Si una consulta puntual necesita evitar el
                    // producto cartesiano de traer 2+ colecciones relacionadas a la vez, se puede
                    // pedir .AsSplitQuery() en esa consulta específica.
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery)));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(ICrudService<>), typeof(CrudService<>));
        services.AddScoped<Pos.Application.Catalogo.IArticuloService, Services.ArticuloService>();
        services.AddScoped<Pos.Application.Catalogo.IFamiliaService, Services.FamiliaService>();
        services.AddScoped<Pos.Application.Clientes.IClienteService, Services.ClienteService>();
        services.AddScoped<Pos.Application.Precios.IListaPrecioService, Services.ListaPrecioService>();
        services.AddScoped<Pos.Application.Abm.IPagoAdminService, Services.PagoAdminService>();
        services.AddScoped<Pos.Application.Abm.IEstructuraService, Services.EstructuraService>();
        services.AddScoped<Pos.Application.Facturacion.ICaeaCargadoService, Services.CaeaCargadoService>();
        services.AddScoped<Pos.Application.Abm.IConfiguracionAdminService, Services.ConfiguracionAdminService>();
        services.AddScoped<Pos.Application.Abm.IConexionExternaAdminService, Services.ConexionExternaAdminService>();
        services.AddHttpClient<Pos.Application.Abm.IConexionPuntosAppAdminService, Services.ConexionPuntosAppAdminService>();
        services.AddHttpClient<Pos.Application.Abstractions.Fidelizacion.IPuntosFidelizacionService, Services.PuntosFidelizacionService>();
        services.AddHttpClient<Pos.Application.Abm.IConexionGiftcardsAppAdminService, Services.ConexionGiftcardsAppAdminService>();
        services.AddHttpClient<Pos.Application.Abstractions.Giftcards.IGiftcardsAppService, Services.GiftcardsAppService>();
        services.AddScoped<Pos.Application.Abm.ICajaEstructuraService, Services.CajaEstructuraService>();
        services.AddScoped<Pos.Application.Abm.IUsuarioAdminService, Services.UsuarioAdminService>();
        services.AddScoped<Pos.Application.Abm.IPermisoAdminService, Services.PermisoAdminService>();
        services.AddScoped<Pos.Application.Abm.IConvenioService, Services.ConvenioService>();
        services.AddScoped<Pos.Application.Abm.IClusterService, Services.ClusterService>();
        services.AddScoped<Pos.Application.Abm.ITarjetaAdminService, Services.TarjetaAdminService>();
        services.AddScoped<Pos.Application.Abm.IPadronService, Services.PadronService>();
        services.AddScoped<Pos.Application.Abm.IClienteEnCuentaService, Services.ClienteEnCuentaService>();
        services.AddScoped<Pos.Application.Pricing.IPricingService, Services.PricingService>();
        services.AddScoped<Pos.Application.Abm.IOfertaAdminService, Services.OfertaAdminService>();
        services.AddScoped<Pos.Application.Abm.IOfertaMedioPagoAdminService, Services.OfertaMedioPagoAdminService>();
        services.AddScoped<Pos.Application.Caja.ICajaService, Services.CajaService>();
        services.AddScoped<Pos.Application.VerificarPrecios.IVerificarPreciosService, Services.VerificarPreciosService>();
        services.AddScoped<Pos.Application.Percepciones.IPercepcionesCalculoService, Services.PercepcionesCalculoService>();
        services.AddScoped<Pos.Application.Facturacion.IFacturacionService, Services.FacturacionService>();
        services.AddScoped<Pos.Application.Facturacion.INotaCreditoService, Services.NotaCreditoService>();
        services.AddScoped<Pos.Application.Facturacion.IReimpresionService, Services.ReimpresionService>();
        services.AddScoped<Pos.Application.Facturacion.ICaeaLoteService, Services.CaeaLoteService>();
        services.AddScoped<Services.CierreLoteEjecutor>();
        services.AddScoped<Pos.Application.Cierres.ICierreCajaService, Services.CierreCajaService>();
        services.AddScoped<Pos.Application.Cierres.ICierreZFiscalService, Services.CierreZFiscalService>();
        services.AddScoped<Pos.Application.Caja.IRetiroCajaService, Services.RetiroCajaService>();
        services.AddScoped<Pos.Application.Tesoreria.ITesoreriaService, Services.TesoreriaService>();
        services.AddScoped<Pos.Application.Cupones.ICuponesService, Services.CuponesService>();
        services.AddScoped<Pos.Application.Etiquetas.IEtiquetaService, Services.EtiquetaService>();
        services.AddScoped<Pos.Application.Estadisticas.IEstadisticasService, Services.EstadisticasService>();
        services.AddScoped<Pos.Application.Abstractions.Interfase.IInterfaseContableService, Services.InterfaseContableService>();
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();
        services.AddScoped<IPuestoRepository, PuestoRepository>();
        services.AddScoped<IPermisoRepository, PermisoRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<ISupervisorAuthService, Services.SupervisorAuthService>();

        // Seguridad
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        var jwt = new JwtOptions
        {
            Key = config["Jwt:Key"] ?? "CAMBIAR-ESTA-CLAVE-DE-DESARROLLO-MINIMO-32-CHARS",
            Issuer = config["Jwt:Issuer"] ?? "Pos",
            Audience = config["Jwt:Audience"] ?? "PosClients",
            Minutos = int.TryParse(config["Jwt:Minutos"], out var min) ? min : 15
        };
        services.AddSingleton(jwt);
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddSingleton(new RefreshTokenOptions
        {
            Dias = int.TryParse(config["Jwt:RefreshDias"], out var dias) ? dias : 7
        });

        // Piezas del adaptador AFIP/ARCA: se registran SIEMPRE (no tienen efecto ninguno hasta que
        // alguien las llama — no abren conexión al construirse) para que se puedan usar desde un
        // endpoint de "probar conexión" aunque el servicio fiscal activo siga siendo el Mock.
        var ambiente = string.Equals(config["Fiscal:Afip:Ambiente"], "Produccion", StringComparison.OrdinalIgnoreCase)
            ? Adapters.Afip.AfipAmbiente.Produccion
            : Adapters.Afip.AfipAmbiente.Homologacion;
        services.AddSingleton(new Adapters.Afip.AfipOptions { Ambiente = ambiente });
        services.AddSingleton<Adapters.Afip.AfipCertificadoStore>();
        services.AddSingleton<Adapters.Afip.AfipWsaaClient>();
        services.AddSingleton<Adapters.Afip.AfipWsfeClient>();

        // Servicio fiscal CAE/CAEA que consume la saga de venta: Mock por defecto (nada cambia
        // salvo que se active a propósito), Afip habla de verdad con ARCA para las cajas de tipo
        // Electrónica — Fiscal sigue yendo siempre por el controlador Hasar, nunca por acá.
        if (string.Equals(config["Fiscal:Service"], "Afip", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IFiscalService, Adapters.Afip.AfipFiscalService>();
        else
            services.AddSingleton<IFiscalService, MockFiscalService>();

        // Impresora fiscal: Mock por defecto, Hasar contra los controladores fiscales 2G reales.
        if (string.Equals(config["Fiscal:Printer"], "Hasar", StringComparison.OrdinalIgnoreCase))
        {
            var sec = config.GetSection("Fiscal:Hasar");
            var hasar = new HasarOptions
            {
                TimeoutMs = int.TryParse(sec["TimeoutMs"], out var t) ? t : 15000,
                EsperaOcupadoMs = int.TryParse(sec["EsperaOcupadoMs"], out var e) ? e : 400,
                MaxReintentosOcupado = int.TryParse(sec["MaxReintentosOcupado"], out var m) ? m : 60,
                EstadoDir = sec["EstadoDir"] ?? "",
                Impresoras = sec.GetSection("Impresoras").GetChildren().Select(i => new HasarImpresoraOptions
                {
                    IdSucursal = int.TryParse(i["IdSucursal"], out var s) ? s : 0,
                    IdCaja = int.TryParse(i["IdCaja"], out var c) ? c : 0,
                    Host = i["Host"] ?? "",
                    Puerto = int.TryParse(i["Puerto"], out var p) ? p : 80
                }).ToList()
            };
            if (hasar.Impresoras.Count == 0)
                throw new InvalidOperationException(
                    "Fiscal:Printer=Hasar pero no hay ninguna impresora en Fiscal:Hasar:Impresoras. " +
                    "Cada entrada necesita IdSucursal, IdCaja y Host.");
            services.AddSingleton(hasar);
            services.AddSingleton<IFiscalPrinter, HasarFiscalPrinter>();
        }
        else
        {
            services.AddSingleton<IFiscalPrinter, MockFiscalPrinter>();
        }

        services.AddSingleton<IPaymentProviderFactory, PaymentProviderFactory>();
        services.AddSingleton<IMailSender, MockMailSender>();
        services.AddSingleton<IErpGateway, DisabledErpGateway>();

        var imgBase = config["ImageBank:BaseUrl"] ?? "https://portal.hergo.com.ar:8099/Imagenes";
        services.AddSingleton<IImageBank>(new BancoImagenesAdapter(imgBase));

        // Certificados CAE: guardados en disco del servidor, fuera de wwwroot (nunca servibles por HTTP).
        var certificadosPath = config["Storage:CertificadosPath"] ?? "App_Data/certificados";
        if (!Path.IsPathRooted(certificadosPath))
            certificadosPath = Path.Combine(contentRootPath, certificadosPath);
        services.AddSingleton(new StorageOptions { CertificadosPath = certificadosPath });

        return services;
    }
}

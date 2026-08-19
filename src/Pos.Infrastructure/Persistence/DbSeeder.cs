using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Domain.Entities;
using Pos.Domain.Enums;

namespace Pos.Infrastructure.Persistence;

/// <summary>Semilla inicial: roles, módulos, permisos, usuario admin y configuraciones base.</summary>
public static class DbSeeder
{
    public static async Task SeedAsync(PosDbContext db, IPasswordHasher hasher, string adminPassword, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (!await db.Roles.AnyAsync(ct))
        {
            db.Roles.AddRange(
                new Rol { Descripcion = "Administrador" },
                new Rol { Descripcion = "Cajero" },
                new Rol { Descripcion = "Supervisor" },
                new Rol { Descripcion = "Tesorero" },
                new Rol { Descripcion = "Repositor" });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Modulos.AnyAsync(ct))
        {
            db.Modulos.AddRange(
                new Modulo { Descripcion = "Caja" },
                new Modulo { Descripcion = "Facturacion" },
                new Modulo { Descripcion = "Tesoreria" },
                new Modulo { Descripcion = "Etiquetas" },
                new Modulo { Descripcion = "Administracion" });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Permisos.AnyAsync(ct))
        {
            // Administrador: todos los módulos con edición.
            for (int m = 1; m <= 5; m++)
                db.Permisos.Add(new Permiso { IdRol = 1, IdModulo = m, PuedeVer = true, PuedeEditar = true, EsEspecial = true });
            // Cajero: Caja + Facturación + Etiquetas (necesita reimprimir/reetiquetar en el mostrador).
            db.Permisos.Add(new Permiso { IdRol = 2, IdModulo = 1, PuedeVer = true, PuedeEditar = true });
            db.Permisos.Add(new Permiso { IdRol = 2, IdModulo = 2, PuedeVer = true, PuedeEditar = true });
            db.Permisos.Add(new Permiso { IdRol = 2, IdModulo = 4, PuedeVer = true, PuedeEditar = true });
            // Supervisor: Caja + Facturación con permisos especiales.
            db.Permisos.Add(new Permiso { IdRol = 3, IdModulo = 1, PuedeVer = true, PuedeEditar = true, EsEspecial = true });
            db.Permisos.Add(new Permiso { IdRol = 3, IdModulo = 2, PuedeVer = true, PuedeEditar = true, EsEspecial = true });
            // Tesorero: Tesorería + Etiquetas.
            db.Permisos.Add(new Permiso { IdRol = 4, IdModulo = 3, PuedeVer = true, PuedeEditar = true, EsEspecial = true });
            db.Permisos.Add(new Permiso { IdRol = 4, IdModulo = 4, PuedeVer = true, PuedeEditar = true });
            // Repositor: Etiquetas.
            db.Permisos.Add(new Permiso { IdRol = 5, IdModulo = 4, PuedeVer = true, PuedeEditar = true });
            await db.SaveChangesAsync(ct);
        }

        // Cajero también puede imprimir etiquetas: alta pedida después del seed inicial, por eso no
        // se gatea con "tabla Permisos vacía" (en instalaciones ya en uso nunca lo está) sino que se
        // busca por Descripcion, igual que el ajuste de TiposOferta más abajo.
        var rolCajero = await db.Roles.FirstOrDefaultAsync(r => r.Descripcion == "Cajero", ct);
        var moduloEtiquetas = await db.Modulos.FirstOrDefaultAsync(m => m.Descripcion == "Etiquetas", ct);
        if (rolCajero is not null && moduloEtiquetas is not null &&
            !await db.Permisos.AnyAsync(p => p.IdRol == rolCajero.IdRol && p.IdModulo == moduloEtiquetas.IdModulo, ct))
        {
            db.Permisos.Add(new Permiso { IdRol = rolCajero.IdRol, IdModulo = moduloEtiquetas.IdModulo, PuedeVer = true, PuedeEditar = true });
            await db.SaveChangesAsync(ct);
        }

        // Cada medio Tarjeta necesita al menos un plan de cuotas (obligatorio elegir uno al cobrar,
        // ver FacturacionService): alta pedida después de que ya existían medios Tarjeta sin
        // ninguno cargado, por eso el backfill acá — PagoAdminService.AsegurarPlanPorDefectoAsync
        // ya cubre los medios que se den de alta/editen de acá en más.
        var mediosTarjetaSinPlan = await db.MediosPago
            .Where(m => db.TiposPago.Any(t => t.IdTipoPago == m.IdTipoPago && t.Fuente == FuentePago.Tarjeta))
            .Where(m => !db.PlanesCuota.Any(p => p.IdMedioPago == m.IdMedioPago))
            .ToListAsync(ct);
        if (mediosTarjetaSinPlan.Count > 0)
        {
            foreach (var m in mediosTarjetaSinPlan)
                db.PlanesCuota.Add(new PlanCuota { IdMedioPago = m.IdMedioPago, Denominacion = "1 cuota", CantidadCuotas = 1 });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Usuarios.AnyAsync(ct))
        {
            db.Usuarios.Add(new Usuario
            {
                NombreUsuario = "admin",
                ClaveHash = hasher.Hash(adminPassword),
                Activo = true,
                IdRol = 1
            });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.CondicionesIva.AnyAsync(ct))
        {
            db.CondicionesIva.AddRange(
                new CondicionIva { Descripcion = "Responsable Inscripto", Letra = "A", CodigoInterno = "RI" },
                new CondicionIva { Descripcion = "Monotributista", Letra = "A", CodigoInterno = "MT" },
                new CondicionIva { Descripcion = "Exento", Letra = "B", CodigoInterno = "EX" },
                new CondicionIva { Descripcion = "Consumidor Final", Letra = "B", CodigoInterno = "CF" });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.ModosIva.AnyAsync(ct))
        {
            db.ModosIva.AddRange(
                new ModoIva { Descripcion = "IVA 21%", Alicuota = 0.21m, PorcentajePercepcion = 0m },
                new ModoIva { Descripcion = "IVA 10,5%", Alicuota = 0.105m, PorcentajePercepcion = 0m },
                new ModoIva { Descripcion = "IVA 27%", Alicuota = 0.27m, PorcentajePercepcion = 0m },
                new ModoIva { Descripcion = "Exento", Alicuota = 0m, PorcentajePercepcion = 0m });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Empresas.AnyAsync(ct))
        {
            var empresa = new Empresa { CodigoInterno = "E01", Descripcion = "Empresa Principal", Cuit = "30000000007" };
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync(ct);
            db.Sucursales.Add(new Sucursal { IdEmpresa = empresa.IdEmpresa, Descripcion = "Casa Central" });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Sectores.AnyAsync(ct))
        {
            db.Sectores.AddRange(new Sector { Descripcion = "Almacén" }, new Sector { Descripcion = "Bebidas" });
            db.Lineas.AddRange(new Linea { Descripcion = "General" }, new Linea { Descripcion = "Gaseosas" });
            db.Familias.AddRange(new Familia { Descripcion = "Sin clasificar" });
            await db.SaveChangesAsync(ct);
        }

        // Los tipos de oferta son fijos: cada uno tiene un comportamiento programado en el motor
        // (TipoOfertaEnum = columna Codigo). Se garantizan por Codigo y no con el guard "si la tabla
        // está vacía", porque en instalaciones ya en uso la tabla nunca está vacía.
        var tiposFijos = new (TipoOfertaEnum Codigo, string Descripcion, bool Seleccionable)[]
        {
            (TipoOfertaEnum.Descuento, "Descuento", true),
            (TipoOfertaEnum.DosPorUno, "2x1", true),
            (TipoOfertaEnum.MixCanasta, "Mix Canasta", true),
            (TipoOfertaEnum.SegundaUnidad, "Segunda unidad al 70%", true),
            (TipoOfertaEnum.Bonificacion, "Bonificacion", false), // legacy: sigue vivo para ofertas viejas
        };
        var tiposExistentes = await db.TiposOferta.ToListAsync(ct);
        foreach (var (codigo, descripcion, seleccionable) in tiposFijos)
        {
            var fila = tiposExistentes.FirstOrDefault(t => t.Codigo == (int)codigo);
            if (fila is null)
                db.TiposOferta.Add(new TipoOferta { Descripcion = descripcion, Codigo = (int)codigo, Seleccionable = seleccionable });
            else
                fila.Seleccionable = seleccionable;
        }
        await db.SaveChangesAsync(ct);

        if (!await db.TiposComprobante.AnyAsync(ct))
        {
            db.TiposComprobante.AddRange(
                new TipoComprobante { Descripcion = "Factura A", Letra = "A", CodigoArca = "001", Signo = 1 },
                new TipoComprobante { Descripcion = "Factura B", Letra = "B", CodigoArca = "006", Signo = 1 },
                new TipoComprobante { Descripcion = "Nota de Crédito A", Letra = "A", CodigoArca = "003", Signo = -1 },
                new TipoComprobante { Descripcion = "Nota de Crédito B", Letra = "B", CodigoArca = "008", Signo = -1 });
            await db.SaveChangesAsync(ct);
        }

        // Comprobante X (Presupuesto): sin valor fiscal, no discrimina IVA, siempre efectivo. Chequeo
        // separado (no el "AnyAsync" de arriba) para que se agregue también en una BD que ya tenía
        // los 4 tipos de antes.
        if (!await db.TiposComprobante.AnyAsync(t => t.Letra == "X", ct))
        {
            db.TiposComprobante.Add(new TipoComprobante { Descripcion = "Presupuesto", Letra = "X", CodigoArca = null, Signo = 1 });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.TiposPago.AnyAsync(ct))
        {
            // Los tipos son los GENÉRICOS; cada uno define por dónde se cobra (Manual o iCARD) y
            // agrupa a los medios concretos que se den de alta después (Visa, MODO, etc.).
            var efectivo = new TipoPago { Descripcion = "Efectivo", Fuente = FuentePago.Efectivo, Canal = CanalCobro.Manual };
            var transferencia = new TipoPago { Descripcion = "Transferencia", Fuente = FuentePago.Transferencia, Canal = CanalCobro.Manual };
            var billetera = new TipoPago { Descripcion = "Billetera virtual", Fuente = FuentePago.BilleteraVirtual, Canal = CanalCobro.ICard };
            var tarjetas = new TipoPago { Descripcion = "Tarjetas", Fuente = FuentePago.Tarjeta, Canal = CanalCobro.ICard };
            // Cuenta corriente no pasa por ningún canal de cobro: la resuelve el control de crédito
            // interno (ver FacturacionService.AprobarCuentaCorrienteAsync).
            var cuentaCorriente = new TipoPago { Descripcion = "Cuenta corriente", Fuente = FuentePago.CuentaCorriente, Canal = CanalCobro.Manual };
            db.TiposPago.AddRange(efectivo, transferencia, billetera, tarjetas, cuentaCorriente);
            await db.SaveChangesAsync(ct);

            // Un medio inicial por tipo, para poder cobrar desde el arranque. El resto se dan de
            // alta por el ABM (varios medios por tipo).
            db.MediosPago.AddRange(
                new MedioPago { Descripcion = "Efectivo", IdTipoPago = efectivo.IdTipoPago, Activo = true },
                new MedioPago { Descripcion = "Transferencia bancaria", IdTipoPago = transferencia.IdTipoPago, Activo = true },
                new MedioPago { Descripcion = "Cuenta corriente", IdTipoPago = cuentaCorriente.IdTipoPago, Activo = true });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Configuraciones.AnyAsync(ct))
        {
            db.Configuraciones.AddRange(
                new Configuracion { Clave = "LimiteConsumidorFinal", Descripcion = "Límite de facturación a Consumidor Final", Valor = "417400" },
                new Configuracion { Clave = "LimiteEfectivoCaja", Descripcion = "Límite de efectivo en caja", Valor = "500000" },
                new Configuracion { Clave = "ReintentosCae", Descripcion = "Reintentos por CAE inaccesible", Valor = "3" },
                new Configuracion { Clave = "RangoRedondeo", Descripcion = "Rango de redondeo por efectivo", Valor = "1" },
                new Configuracion { Clave = "TimeoutInactividadSeg", Descripcion = "Bloqueo de caja por inactividad (seg)", Valor = "300" });
            await db.SaveChangesAsync(ct);
        }
    }
}

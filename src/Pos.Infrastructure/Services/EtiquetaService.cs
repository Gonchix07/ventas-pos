using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Etiquetas;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Búsqueda de artículos para armar la lista de etiquetas y cálculo de los datos de cada
/// etiqueta (precio base + precios por tipo de tarjeta + precio por unidad de medida + sin
/// impuestos + compra mínima). Ver docs de Fase 6 y plantillas reales de referencia.
/// </summary>
public class EtiquetaService : IEtiquetaService
{
    private readonly PosDbContext _db;
    private readonly ICurrentUser _currentUser;
    public EtiquetaService(PosDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<ArticuloParaEtiquetaDto>> BuscarAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0) return Array.Empty<ArticuloParaEtiquetaDto>();

        // Solo presentaciones individuales (UnidadXBulto = 1, no bultos/cajas): la etiqueta de
        // góndola es para la unidad suelta, no para el bulto — pedido explícito del usuario.
        var porBarra = await (
            from b in _db.Barras.AsNoTracking().Where(x => x.CodigoBarra == query)
            join pr in _db.Presentaciones.AsNoTracking().Where(p => p.UnidadXBulto == 1m) on b.IdPresentacion equals pr.IdPresentacion
            join a in _db.Articulos.AsNoTracking().Where(x => x.Activo) on pr.IdArticulo equals a.IdArticulo
            select new ArticuloParaEtiquetaDto(a.IdArticulo, pr.IdPresentacion, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket)
        ).ToListAsync(ct);
        if (porBarra.Count > 0) return porBarra;

        return await (
            from a in _db.Articulos.AsNoTracking().Where(x => x.Activo && (x.CodigoInterno.Contains(query) || x.Descripcion.Contains(query)))
            join pr in _db.Presentaciones.AsNoTracking().Where(p => p.UnidadXBulto == 1m) on a.IdArticulo equals pr.IdArticulo
            orderby a.Descripcion
            select new ArticuloParaEtiquetaDto(a.IdArticulo, pr.IdPresentacion, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket)
        ).Take(30).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ArticuloParaEtiquetaDto>> PorClasificacionAsync(
        int? idSector, int? idLinea, int? idFamilia, CancellationToken ct = default)
    {
        var q = _db.Articulos.AsNoTracking().Where(a => a.Activo);
        if (idSector.HasValue) q = q.Where(a => a.IdSector == idSector.Value);
        if (idLinea.HasValue) q = q.Where(a => a.IdLinea == idLinea.Value);
        if (idFamilia.HasValue) q = q.Where(a => a.IdFamilia == idFamilia.Value);

        return await (
            from a in q
            join pr in _db.Presentaciones.AsNoTracking() on a.IdArticulo equals pr.IdArticulo
            orderby a.Descripcion
            select new ArticuloParaEtiquetaDto(a.IdArticulo, pr.IdPresentacion, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket)
        ).Take(500).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EtiquetaDto>> GenerarAsync(int idSucursal, List<int> idsPresentacion, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);

        var resultado = new List<EtiquetaDto>();
        var fecha = DateTime.Now;

        var tiposTarjeta = await _db.TiposTarjeta.AsNoTracking().Where(t => t.IdListaPrecio != null).ToListAsync(ct);
        var ofertasVigentes = await _db.CabecerasOfertas.AsNoTracking()
            .Where(o => o.IdSucursal == idSucursal && o.FechaInicio <= fecha && o.FechaFin >= fecha)
            .Include(o => o.Alcances).Include(o => o.Acciones)
            .ToListAsync(ct);

        foreach (var idPresentacion in idsPresentacion.Distinct())
        {
            var info = await (
                from pr in _db.Presentaciones.AsNoTracking().Where(p => p.IdPresentacion == idPresentacion)
                join a in _db.Articulos.AsNoTracking() on pr.IdArticulo equals a.IdArticulo
                join m in _db.ModosIva.AsNoTracking() on a.IdModoIva equals m.IdModoIva
                select new
                {
                    pr.IdPresentacion, a.IdArticulo, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket,
                    a.IdSector, a.IdLinea, a.IdFamilia, a.UnidadMedida, a.ContenidoNetoUnitario, m.Alicuota
                }
            ).FirstOrDefaultAsync(ct);
            if (info is null) continue;

            var codigoBarra = await _db.Barras.AsNoTracking().Where(b => b.IdPresentacion == idPresentacion)
                .Select(b => b.CodigoBarra).FirstOrDefaultAsync(ct);

            var candidatos = await (
                from p in _db.Precios.AsNoTracking().Where(x => x.IdPresentacion == idPresentacion)
                join l in _db.ListasPrecios.AsNoTracking().Where(x => x.IdSucursal == idSucursal) on p.IdListaPrecio equals l.IdListaPrecio
                // El IdListaPrecio va explícito: dentro de un árbol de expresión no se pueden omitir
                // los argumentos opcionales del record.
                select new CandidatoPrecio(l.Tipo, l.Prioridad, l.FechaInicio, l.FechaFin, p.PrecioFinal,
                    p.ImpuestoInterno, l.IdListaPrecio)
            ).ToListAsync(ct);
            var resuelto = CalculadoraPrecios.Resolver(candidatos, fecha);
            if (!resuelto.Encontrado) continue; // sin precio vigente: no se genera etiqueta para esta presentación

            var precioPorUnidad = EtiquetaCalculos.PrecioPorUnidadMedida(resuelto.PrecioVigente, info.ContenidoNetoUnitario);
            var sinImpuestos = EtiquetaCalculos.PrecioSinImpuestosNacionales(resuelto.PrecioVigente, resuelto.ImpuestoInterno, info.Alicuota);

            var preciosTarjeta = new List<TipoTarjetaPrecioDto>();
            foreach (var t in tiposTarjeta)
            {
                var precioLista = await _db.Precios.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.IdListaPrecio == t.IdListaPrecio!.Value && p.IdPresentacion == idPresentacion, ct);
                if (precioLista is null) continue;
                var pxu = EtiquetaCalculos.PrecioPorUnidadMedida(precioLista.PrecioFinal, info.ContenidoNetoUnitario);
                var si = EtiquetaCalculos.PrecioSinImpuestosNacionales(precioLista.PrecioFinal, precioLista.ImpuestoInterno, info.Alicuota);
                preciosTarjeta.Add(new TipoTarjetaPrecioDto(t.Descripcion.ToUpperInvariant(), precioLista.PrecioFinal, pxu, si));
            }

            var compraMinima = ResolverCompraMinima(ofertasVigentes, info.IdArticulo, info.IdSector, info.IdLinea, info.IdFamilia);

            // Precio Único: se busca primero el folder (gana por prioridad — ver CalculadoraPrecios —
            // así que si hay uno vigente ES el precio ya resuelto) y, si no hay, se ve si las tarjetas
            // configuradas (Rojo/Azul) terminaron coincidiendo en el mismo precio. En cualquiera de los
            // dos casos se muestra una sola línea con la aclaración; si no, la etiqueta sigue mostrando
            // un precio por tarjeta como hasta ahora.
            var precioFinal = resuelto.PrecioVigente;
            var pxuFinal = precioPorUnidad;
            var siFinal = sinImpuestos;
            string? aclaracion = null;
            var esFolder = candidatos.Any(c => c.Tipo == TipoListaPrecio.Folder);

            if (esFolder)
            {
                aclaracion = "Precio Único";
                preciosTarjeta = new List<TipoTarjetaPrecioDto>();
            }
            else if (preciosTarjeta.Count >= 2 && preciosTarjeta.Select(t => t.Precio).Distinct().Count() == 1)
            {
                aclaracion = "Precio Único";
                var compartido = preciosTarjeta[0];
                precioFinal = compartido.Precio;
                pxuFinal = compartido.PrecioPorUnidadMedida;
                siFinal = compartido.PrecioSinImpuestos;
                preciosTarjeta = new List<TipoTarjetaPrecioDto>();
            }

            resultado.Add(new EtiquetaDto(idPresentacion, info.CodigoInterno, info.Descripcion, info.DescripcionTicket,
                codigoBarra, precioFinal, pxuFinal, siFinal, preciosTarjeta, compraMinima,
                TextoUnidadMedida(info.UnidadMedida), aclaracion));
        }

        return resultado;
    }

    /// <summary>
    /// Cantidad mínima de compra para el precio mostrado, según ofertas de bonificación
    /// vigentes con alcance sobre el artículo (mismo criterio de alcance que el motor de
    /// ofertas de Fase 2, sin la dimensión de cluster porque en etiquetas no hay cliente).
    /// </summary>
    private static decimal ResolverCompraMinima(
        List<Pos.Domain.Entities.CabeceraOferta> ofertas, int idArticulo, int idSector, int idLinea, int idFamilia)
    {
        foreach (var cab in ofertas)
        {
            var accionBonif = cab.Acciones.FirstOrDefault(a => a.CantidadMin is > 0);
            if (accionBonif is null) continue;

            bool Coincide(Pos.Domain.Entities.AlcanceOferta al) =>
                (al.IdArticulo is null || al.IdArticulo == idArticulo) &&
                (al.IdSector is null || al.IdSector == idSector) &&
                (al.IdLinea is null || al.IdLinea == idLinea) &&
                (al.IdFamilia is null || al.IdFamilia == idFamilia);

            var inclusiones = cab.Alcances.Where(a => !a.EsExcepcion).ToList();
            var exclusiones = cab.Alcances.Where(a => a.EsExcepcion).ToList();
            var incluida = inclusiones.Count == 0 || inclusiones.Any(Coincide);
            var excluida = exclusiones.Any(Coincide);
            if (incluida && !excluida) return accionBonif.CantidadMin!.Value;
        }
        return 1m;
    }

    public async Task<ClasificacionesDto> GetClasificacionesAsync(CancellationToken ct = default)
    {
        var sectores = await _db.Sectores.AsNoTracking().OrderBy(s => s.Descripcion)
            .Select(s => new LookupSimpleDto(s.IdSector, s.Descripcion)).ToListAsync(ct);
        var lineas = await _db.Lineas.AsNoTracking().OrderBy(l => l.Descripcion)
            .Select(l => new LookupSimpleDto(l.IdLinea, l.Descripcion)).ToListAsync(ct);
        var familias = await _db.Familias.AsNoTracking().OrderBy(f => f.Descripcion)
            .Select(f => new FamiliaLookupDto(f.IdFamilia, f.Descripcion, f.IdSector)).ToListAsync(ct);
        return new ClasificacionesDto(sectores, lineas, familias);
    }

    public async Task<IReadOnlyList<LookupSimpleDto>> GetSucursalesAsync(CancellationToken ct = default) =>
        await _db.Sucursales.AsNoTracking().OrderBy(s => s.Descripcion)
            .Select(s => new LookupSimpleDto(s.IdSucursal, s.Descripcion)).ToListAsync(ct);

    private static string TextoUnidadMedida(UnidadMedida u) => u switch
    {
        UnidadMedida.Kilogramo => "Kg",
        UnidadMedida.Litro => "Lt",
        _ => ""
    };
}

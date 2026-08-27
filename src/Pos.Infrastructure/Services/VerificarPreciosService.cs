using Microsoft.EntityFrameworkCore;
using Pos.Application.Caja;
using Pos.Application.Pricing;
using Pos.Application.VerificarPrecios;
using Pos.Domain.Enums;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

public class VerificarPreciosService : IVerificarPreciosService
{
    // Las dos listas "de mostrador" que se muestran siempre, en este orden — mismo criterio de
    // nombres que usan los badges de Caja (ver claseLista en CajaPage.tsx / lista-azul, lista-roja
    // en App.css). Si el negocio agrega más listas de este tipo en el futuro, esto es lo único que
    // hay que tocar acá.
    private static readonly string[] ListasMostradas = { "AZUL", "ROJA" };

    private readonly PosDbContext _db;
    private readonly ICajaService _caja;
    private readonly IPricingService _pricing;

    public VerificarPreciosService(PosDbContext db, ICajaService caja, IPricingService pricing)
    {
        _db = db;
        _caja = caja;
        _pricing = pricing;
    }

    public async Task<ConsultaPrecioResult?> ConsultarAsync(int idSucursal, string codigo, CancellationToken ct = default)
    {
        // Reusa la búsqueda de Caja (barra exacta → etiqueta de balanza → código interno) para no
        // duplicar esa lógica acá. El precio que trae (resuelto para "cliente genérico", null) se
        // descarta: lo único que interesa de acá es identificar el artículo — precios de lista
        // puntuales se resuelven aparte, más abajo.
        var articulo = await _caja.BuscarArticuloAsync(idSucursal, codigo, null, ct);
        if (articulo is null) return null;

        var candidatos = await (
            from p in _db.Precios.AsNoTracking().Where(x => x.IdPresentacion == articulo.IdPresentacion)
            join l in _db.ListasPrecios.AsNoTracking().Where(x => x.IdSucursal == idSucursal)
                on p.IdListaPrecio equals l.IdListaPrecio
            select new { l.CodigoInterno, l.Tipo, p.PrecioFinal }
        ).ToListAsync(ct);

        var precios = ListasMostradas
            .Select(codLista => new PrecioListaResumen(codLista,
                candidatos.FirstOrDefault(c => c.CodigoInterno.Equals(codLista, StringComparison.OrdinalIgnoreCase))?.PrecioFinal))
            .ToList();

        // Lista Folder vigente con precio cargado para este artículo: la señal para el sticker
        // "LISTA FOLDER", independiente de si Azul/Roja también tienen precio.
        var esListaFolder = candidatos.Any(c => c.Tipo == TipoListaPrecio.Folder);

        // Ofertas vigentes aplicables: se prueba con 1 unidad al primer precio de lista que haya
        // (Azul si existe, si no el que sea) — alcanza para saber SI hay oferta, el motor necesita
        // un precio unitario de referencia pero el descuento en sí no se usa acá.
        var precioRef = precios.FirstOrDefault(p => p.Precio is not null)?.Precio ?? 0m;
        var ofertasResp = await _pricing.AplicarOfertasAsync(
            new AplicarOfertasRequest(idSucursal, null,
                new List<LineaOfertaRequest> { new(articulo.IdPresentacion, 1m, precioRef) }), ct);
        var ofertas = (ofertasResp.Lineas.FirstOrDefault()?.Ofertas ?? new List<OfertaAplicadaDto>())
            .Select(o => new OfertaResumen(o.IdOferta, o.Descripcion))
            .ToList();

        return new ConsultaPrecioResult(articulo.CodigoInterno, articulo.Descripcion, articulo.ImagenUrl,
            precios, esListaFolder, ofertas);
    }
}

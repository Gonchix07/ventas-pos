import { Document, Page, View, Text, Font, StyleSheet, pdf } from "@react-pdf/renderer";
import type { Etiqueta } from "../../shared/api/etiquetas";

/**
 * Genera un PDF real de las etiquetas (fleje 90x40mm o A4/A5, una etiqueta por hoja) y lo abre en
 * una pestaña nueva para que el usuario lo imprima desde el visor de PDF del navegador.
 *
 * Reemplaza al viejo mecanismo de "vista de impresión" en HTML + window.print(): ese dependía de
 * @page/@media print, que en algunas instalaciones de Chrome imprimía la hoja en blanco (bug de
 * capas compuestas al combinar CSS transform con paginación de impresión — ver etiquetas-print.css,
 * ya no se usa). Un PDF real, visto en el visor nativo, no tiene ese problema: el visor de PDF
 * imprime directamente el documento, no una página HTML que intenta imitar el papel.
 */

Font.register({
  family: "Plus Jakarta Sans",
  fonts: [
    { src: "/fonts/plus-jakarta-sans/PlusJakartaSans-Regular.ttf", fontWeight: 400 },
    { src: "/fonts/plus-jakarta-sans/PlusJakartaSans-Bold.ttf", fontWeight: 700 },
    { src: "/fonts/plus-jakarta-sans/PlusJakartaSans-ExtraBold.ttf", fontWeight: 800 },
  ],
});

// react-pdf mide todo en puntos (pt); las medidas del diseño original venían en mm.
const mm = (n: number) => n * 2.834645669291339;

const fmt = (n: number) => n.toLocaleString("es-AR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
// Fleje: la etiqueta es fija (90x40mm) — un precio de 7+ dígitos con centavos no entra en el ancho
// disponible. Se prefiere truncar los centavos antes que desbordar el diseño.
const fmtFleje = (n: number) => (n >= 1_000_000 ? Math.round(n).toLocaleString("es-AR") : fmt(n));

/** Una fila de precio (por tarjeta, o el precio base si el artículo no tiene precios por tarjeta). */
function filasDe(e: Etiqueta, prefijoTarjeta: string) {
  return e.preciosTarjeta.length > 0
    ? e.preciosTarjeta.map((t) => ({
        nombre: `${prefijoTarjeta}${t.nombreTarjeta}`, precio: t.precio,
        pxu: t.precioPorUnidadMedida, si: t.precioSinImpuestos,
      }))
    : [{
        nombre: e.aclaracionPrecio ?? "", precio: e.precioBase,
        pxu: e.precioBasePorUnidadMedida, si: e.precioBaseSinImpuestos,
      }];
}

// ---------- Fleje (90x40mm, comandera Zebra) ----------

const fs = StyleSheet.create({
  page: { fontFamily: "Plus Jakarta Sans", color: "#16211f" },
  container: {
    flexDirection: "column", justifyContent: "space-between", height: "100%",
    paddingVertical: mm(2), paddingHorizontal: mm(3),
  },
  titulo: { textAlign: "center", fontWeight: 800, fontSize: 8.5, lineHeight: 1.1 },
  codigos: { flexDirection: "row", justifyContent: "space-between", fontSize: 6.5, marginTop: mm(1) },
  precios: { flexDirection: "column", marginTop: mm(1) },
  fila: { flexDirection: "row", justifyContent: "space-between", alignItems: "flex-end", marginBottom: mm(1) },
  tarjeta: { fontWeight: 700, fontSize: 8 },
  precioInline: { fontWeight: 800, fontSize: 12 },
  detalle: { textAlign: "right", fontSize: 6, lineHeight: 1.25, maxWidth: mm(35) },
  footer: {
    textAlign: "center", fontSize: 6, borderTopWidth: 1, borderTopColor: "#dde3e0",
    paddingTop: mm(0.5),
  },
});

function FlejeDocument({ items }: { items: Etiqueta[] }) {
  return (
    <Document>
      {items.map((e) => (
        <Page key={e.idPresentacion} size={{ width: mm(90), height: mm(40) }} style={fs.page}>
          <View style={fs.container}>
            <Text style={fs.titulo}>{e.descripcion}</Text>
            <View style={fs.codigos}>
              <Text>Cod. {e.codigoInterno}</Text>
              <Text>Cod.Bar {e.codigoBarra}</Text>
            </View>
            <View style={fs.precios}>
              {filasDe(e, "Tarj. ").map((row, i) => (
                <View key={i} style={fs.fila}>
                  <Text style={fs.tarjeta}>
                    {row.nombre} <Text style={fs.precioInline}>$ {fmtFleje(row.precio)}</Text>
                  </Text>
                  <Text style={fs.detalle}>
                    {row.pxu != null && `Precio por ${e.unidadMedidaTexto} $${fmtFleje(row.pxu)}\n`}
                    Sin imp. nac.: ${fmtFleje(row.si)}
                  </Text>
                </View>
              ))}
            </View>
            <Text style={fs.footer}>
              Compra minima: {e.compraMinima} Unidad(es) - Precio unitario final con IVA
            </Text>
          </View>
        </Page>
      ))}
    </Document>
  );
}

// ---------- A4 / A5 (una etiqueta grande por hoja) ----------

const hs = StyleSheet.create({
  page: { fontFamily: "Plus Jakarta Sans", color: "#16211f" },
  container: {
    flexDirection: "column", justifyContent: "space-between", height: "100%",
    paddingVertical: mm(14), paddingHorizontal: mm(12),
  },
  arriba: { flexDirection: "column" },
  titulo: { fontWeight: 800, fontSize: 26, textAlign: "center", marginBottom: mm(8) },
  bloque: { marginBottom: mm(6), alignItems: "center" },
  nombreTarjeta: { fontWeight: 800, fontSize: 15, marginBottom: mm(2) },
  precio: { fontWeight: 800, fontSize: 44, marginBottom: mm(2) },
  detalle: { fontSize: 10.5, color: "#333333", textAlign: "center" },
  piePrecio: { fontSize: 10.5, textAlign: "center" },
  footer: { flexDirection: "row", justifyContent: "center", gap: mm(12), fontSize: 10.5, marginTop: mm(6) },
});

function HojaDocument({ items, formato }: { items: Etiqueta[]; formato: "A4" | "A5" }) {
  const size = formato === "A4" ? { width: mm(210), height: mm(297) } : { width: mm(148), height: mm(210) };
  return (
    <Document>
      {items.map((e) => (
        <Page key={e.idPresentacion} size={size} style={hs.page}>
          <View style={hs.container}>
            <View style={hs.arriba}>
              <Text style={hs.titulo}>{e.descripcion.toUpperCase()}</Text>
              {filasDe(e, "").map((row, i) => (
                <View key={i} style={hs.bloque}>
                  {row.nombre && <Text style={hs.nombreTarjeta}>{row.nombre.toUpperCase()}</Text>}
                  <Text style={hs.precio}>$ {fmt(row.precio)}</Text>
                  {row.pxu != null && (
                    <Text style={hs.detalle}>Precio por {e.unidadMedidaTexto} $ {fmt(row.pxu)}</Text>
                  )}
                  <Text style={hs.detalle}>Precio sin impuestos nacionales: $ {fmt(row.si)}</Text>
                </View>
              ))}
            </View>
            <View>
              <Text style={hs.piePrecio}>
                Compra mínima: {e.compraMinima} Unidad(es){"\n"}Precio final, IVA incluido
              </Text>
              <View style={hs.footer}>
                <Text>Cod. {e.codigoInterno}</Text>
                <Text>Cod. Barras: {e.codigoBarra}</Text>
              </View>
            </View>
          </View>
        </Page>
      ))}
    </Document>
  );
}

export type FormatoEtiqueta = "Fleje" | "A4" | "A5";

/**
 * Abre una pestaña en blanco. Llamar a esto ANTES de cualquier `await` en el handler del click (ej.
 * antes de pedirle los datos al backend) — el navegador solo asocia `window.open` al gesto del
 * click si se llama sincrónicamente; si se abre después de un await, lo bloquea como popup.
 */
export function abrirPestañaParaPdf(): Window | null {
  return window.open("", "_blank");
}

/**
 * Arma el PDF y lo carga en la ventana ya abierta (ver `abrirPestañaParaPdf`). Si por lo que sea no
 * se pudo abrir antes (bloqueada por el navegador), intenta abrir una nueva acá — puede que el
 * navegador la bloquee igual al no venir de un gesto sincrónico, pero es mejor que no ofrecer nada.
 */
export async function generarYAbrirPdf(formato: FormatoEtiqueta, items: Etiqueta[], ventana: Window | null): Promise<void> {
  const doc = formato === "Fleje" ? <FlejeDocument items={items} /> : <HojaDocument items={items} formato={formato} />;
  const blob = await pdf(doc).toBlob();
  const url = URL.createObjectURL(blob);
  if (ventana && !ventana.closed) ventana.location.href = url;
  else window.open(url, "_blank");
}

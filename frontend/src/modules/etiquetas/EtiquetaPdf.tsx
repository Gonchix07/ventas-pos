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
// A4/A5: mismo criterio, pero recién por encima de $9.999.999 (letra mucho más grande, hay más
// margen antes de que los centavos empiecen a molestar) — pedido explícito del usuario.
const fmtHoja = (n: number) => (n > 9_999_999 ? Math.round(n).toLocaleString("es-AR") : fmt(n));

const esAzul = (nombre: string) => nombre.toUpperCase().includes("AZUL");

/** Una fila de precio (por tarjeta, o el precio base si el artículo no tiene precios por tarjeta).
 *  AZUL siempre primero (arriba) cuando conviven varias tarjetas — el resto mantiene el orden en
 *  que vino de la API. */
function filasDe(e: Etiqueta, prefijoTarjeta: string) {
  const filas = e.preciosTarjeta.length > 0
    ? e.preciosTarjeta.map((t) => ({
        nombre: `${prefijoTarjeta}${t.nombreTarjeta}`, precio: t.precio,
        pxu: t.precioPorUnidadMedida, si: t.precioSinImpuestos,
      }))
    : [{
        nombre: e.aclaracionPrecio ?? "", precio: e.precioBase,
        pxu: e.precioBasePorUnidadMedida, si: e.precioBaseSinImpuestos,
      }];
  return filas.length > 1
    ? [...filas].sort((a, b) => Number(esAzul(b.nombre)) - Number(esAzul(a.nombre)))
    : filas;
}

// ---------- Fleje (90x40mm, comandera Zebra) ----------

const fs = StyleSheet.create({
  page: { fontFamily: "Plus Jakarta Sans", color: "#16211f" },
  // Margen izq./der. ampliado (3mm -> 4.5mm) — pedido explícito.
  container: {
    flexDirection: "column", justifyContent: "space-between", height: "100%",
    paddingVertical: mm(2), paddingHorizontal: mm(4.5),
  },
  // Todas las fuentes agrandadas salvo `footer` (la leyenda "Compra minima... Precio unitario
  // final con IVA") — pedido explícito de dejar esa sola sin cambios.
  titulo: { textAlign: "center", fontWeight: 800, fontSize: 10, lineHeight: 1.1 },
  codigos: { flexDirection: "row", justifyContent: "space-between", fontSize: 7.5, marginTop: mm(1) },
  precios: { flexDirection: "column", marginTop: mm(1) },
  fila: { flexDirection: "row", justifyContent: "space-between", alignItems: "flex-end", marginBottom: mm(1) },
  tarjeta: { fontWeight: 700, fontSize: 9.5 },
  precioInline: { fontWeight: 800, fontSize: 14 },
  detalle: { textAlign: "right", fontSize: 7, lineHeight: 1.25, maxWidth: mm(36) },
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
              {filasDe(e, "").map((row, i) => (
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

// Tamaño del número de precio: si hay un solo precio queda grande; si hay dos (AZUL/ROJA), el de
// AZUL va arriba y todavía más grande que el de ROJA — pedido explícito del usuario para que la
// tarjeta "principal" se distinga a simple vista en la góndola.
const PRECIO_UNICO = 60;
const PRECIO_AZUL = 66;
const PRECIO_OTRA = 50;

const hs = StyleSheet.create({
  page: { fontFamily: "Plus Jakarta Sans", color: "#16211f" },
  // Márgenes superior/inferior al doble de los originales (14mm → 28mm) — pedido explícito.
  container: {
    flexDirection: "column", height: "100%",
    paddingVertical: mm(28), paddingHorizontal: mm(12),
  },
  titulo: { fontWeight: 800, fontSize: 26, textAlign: "center" },
  // El bloque de precios ocupa todo el espacio entre el título y el pie, y se centra ahí adentro
  // (tanto si hay un precio único como si hay AZUL + ROJA) — pedido explícito.
  preciosArea: { flex: 1, flexDirection: "column", justifyContent: "center", alignItems: "center" },
  bloque: { marginBottom: mm(6), alignItems: "center" },
  nombreTarjeta: { fontWeight: 800, fontSize: 15, marginBottom: mm(2) },
  precio: { fontWeight: 800, marginBottom: mm(2) },
  detalle: { fontSize: 10.5, color: "#333333", textAlign: "center" },
  piePrecio: { fontSize: 10.5, textAlign: "center" },
  footer: { flexDirection: "row", justifyContent: "center", gap: mm(12), fontSize: 10.5, marginTop: mm(6) },
});

// Horizontal (A4H/A5H): mucho menos alto disponible que en vertical — A5H tiene solo 148mm de
// alto total contra los 210/297mm de A5/A4 verticales. Con el padding y las fuentes de `hs` tal
// cual, el contenido no entraba: `preciosArea` (flex:1) terminaba con una altura calculada
// insuficiente y los bloques de AZUL/ROJA se superponían en vez de apilarse (bug real, visto en
// captura). Achicando padding vertical y fuentes de este set alcanza de sobra incluso en A5H.
const hsH = StyleSheet.create({
  page: hs.page,
  container: { flexDirection: "column", height: "100%", paddingVertical: mm(8), paddingHorizontal: mm(16) },
  titulo: { fontWeight: 800, fontSize: 20, textAlign: "center" },
  preciosArea: { flex: 1, flexDirection: "column", justifyContent: "center", alignItems: "center" },
  bloque: { marginBottom: mm(3), alignItems: "center" },
  nombreTarjeta: { fontWeight: 800, fontSize: 11, marginBottom: mm(1) },
  precio: { fontWeight: 800, marginBottom: mm(1) },
  detalle: { fontSize: 8, color: "#333333", textAlign: "center" },
  piePrecio: { fontSize: 8, textAlign: "center" },
  footer: { flexDirection: "row", justifyContent: "center", gap: mm(10), fontSize: 8, marginTop: mm(3) },
});
const PRECIO_UNICO_H = 42;
const PRECIO_AZUL_H = 46;
const PRECIO_OTRA_H = 34;

function HojaDocument({ items, formato }: { items: Etiqueta[]; formato: "A4" | "A5" | "A4H" | "A5H" }) {
  // H = horizontal (apaisada): mismas medidas de A4/A5 con ancho y alto invertidos.
  const size = formato === "A4" ? { width: mm(210), height: mm(297) }
    : formato === "A4H" ? { width: mm(297), height: mm(210) }
    : formato === "A5" ? { width: mm(148), height: mm(210) }
    : { width: mm(210), height: mm(148) };
  const esHorizontal = formato === "A4H" || formato === "A5H";
  const s = esHorizontal ? hsH : hs;
  const [precioUnico, precioAzul, precioOtra] = esHorizontal
    ? [PRECIO_UNICO_H, PRECIO_AZUL_H, PRECIO_OTRA_H]
    : [PRECIO_UNICO, PRECIO_AZUL, PRECIO_OTRA];
  return (
    <Document>
      {items.map((e) => {
        // filasDe ya deja AZUL primero cuando conviven varias tarjetas.
        const ordenadas = filasDe(e, "");
        return (
          <Page key={e.idPresentacion} size={size} style={s.page}>
            <View style={s.container}>
              <Text style={s.titulo}>{e.descripcion.toUpperCase()}</Text>
              <View style={s.preciosArea}>
                {ordenadas.map((row, i) => {
                  const fontSize = ordenadas.length > 1 ? (esAzul(row.nombre) ? precioAzul : precioOtra) : precioUnico;
                  return (
                    <View key={i} style={s.bloque}>
                      {row.nombre && <Text style={s.nombreTarjeta}>{row.nombre.toUpperCase()}</Text>}
                      <Text style={[s.precio, { fontSize }]}>$ {fmtHoja(row.precio)}</Text>
                      {row.pxu != null && (
                        <Text style={s.detalle}>Precio por {e.unidadMedidaTexto} $ {fmt(row.pxu)}</Text>
                      )}
                      <Text style={s.detalle}>Precio sin impuestos nacionales: $ {fmt(row.si)}</Text>
                    </View>
                  );
                })}
              </View>
              <View>
                <Text style={s.piePrecio}>
                  Compra mínima: {e.compraMinima} Unidad(es){"\n"}Precio final, IVA incluido
                </Text>
                <View style={s.footer}>
                  <Text>Cod. {e.codigoInterno}</Text>
                  <Text>Cod. Barras: {e.codigoBarra}</Text>
                </View>
              </View>
            </View>
          </Page>
        );
      })}
    </Document>
  );
}

export type FormatoEtiqueta = "Fleje" | "A4" | "A5" | "A4H" | "A5H";

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

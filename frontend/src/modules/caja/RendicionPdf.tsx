import { Document, Page, View, Text, Font, StyleSheet, pdf } from "@react-pdf/renderer";
import type { ArqueoX, CierreTurnoResultado } from "../../shared/api/caja";

/**
 * Rendición de caja (cierre de turno) como PDF real, en vez de la vieja vista HTML +
 * `window.print()`. Motivo (2026-08-25): en el puesto de Caja el navegador corre en modo
 * `--kiosk-printing` (ver docs/08-puesto-caja.md) para que los tickets salgan solos por la
 * comandera — pero eso es un flag de TODO el proceso del navegador, no de una pantalla puntual, así
 * que `window.print()` en esta pantalla también salía derecho a la comandera (a la impresora
 * predeterminada, en papel de ticket) en vez de dejar elegir la impresora de oficina/Tesorería.
 *
 * Con un PDF real, el botón no llama a `window.print()` en absoluto: abre el documento en una
 * pestaña nueva (visor de PDF del navegador) y el cajero/Tesorería lo imprime cuando quiera, desde
 * donde quiera, eligiendo la impresora a mano — igual que ya se resolvió para las Etiquetas (ver
 * EtiquetaPdf.tsx, mismo mecanismo con @react-pdf/renderer).
 */

Font.register({
  family: "Plus Jakarta Sans",
  fonts: [
    { src: "/fonts/plus-jakarta-sans/PlusJakartaSans-Regular.ttf", fontWeight: 400 },
    { src: "/fonts/plus-jakarta-sans/PlusJakartaSans-Bold.ttf", fontWeight: 700 },
    { src: "/fonts/plus-jakarta-sans/PlusJakartaSans-ExtraBold.ttf", fontWeight: 800 },
  ],
});

const money = (n: number) =>
  n.toLocaleString("es-AR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const fechaHora = (iso?: string | null) => {
  if (!iso) return "—";
  return new Date(iso).toLocaleString("es-AR", {
    day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
};

const s = StyleSheet.create({
  page: { fontFamily: "Plus Jakarta Sans", color: "#16211f", fontSize: 9, padding: 28 },
  header: { flexDirection: "row", justifyContent: "space-between", alignItems: "flex-start", marginBottom: 14 },
  h1: { fontSize: 16, fontWeight: 800 },
  subtitulo: { fontSize: 9, color: "#57635f", marginTop: 2 },
  folio: { alignItems: "flex-end" },
  folioLabel: { fontSize: 8, color: "#57635f" },
  folioValor: { fontSize: 12, fontWeight: 700 },

  meta: { flexDirection: "row", flexWrap: "wrap", borderTopWidth: 1, borderBottomWidth: 1,
    borderColor: "#c6cfcb", paddingVertical: 6, marginBottom: 10 },
  metaItem: { width: "25%", marginBottom: 2 },
  metaItemFull: { width: "50%", marginBottom: 2 },
  metaLabel: { fontSize: 7.5, color: "#57635f" },
  metaValor: { fontSize: 9.5, fontWeight: 700 },

  bloque: { marginBottom: 10 },
  h2: { fontSize: 10.5, fontWeight: 700, marginBottom: 4 },

  fila: { flexDirection: "row", borderBottomWidth: 0.5, borderColor: "#dde3e0", paddingVertical: 3 },
  filaHead: { flexDirection: "row", borderBottomWidth: 1, borderColor: "#16211f", paddingBottom: 3, fontWeight: 700 },
  filaFoot: { flexDirection: "row", borderTopWidth: 1, borderColor: "#16211f", paddingTop: 3, fontWeight: 700 },
  celda: { flex: 2 },
  celdaNum: { flex: 1, textAlign: "right" },
  // Variante para la tabla de Retiros: "Autorizó / retiró" es casi siempre un nombre de usuario
  // corto, así que se le achica el ancho para darle más lugar al importe (un retiro grande, ej.
  // "−$8.000.000,00", se cortaba en dos líneas con el ancho parejo de celdaNum).
  celdaCorta: { flex: 1 },
  celdaNumAncha: { flex: 1.6, textAlign: "right" },
  diferencia: { color: "#b3261e" },

  resumenGrid: { flexDirection: "column", marginTop: 4 },
  resumenItem: { width: "100%", flexDirection: "row", justifyContent: "space-between", paddingVertical: 2 },
  resumenItemBold: { width: "100%", flexDirection: "row", justifyContent: "space-between", paddingVertical: 2, fontWeight: 700 },
  resumenTotal: { width: "100%", flexDirection: "row", justifyContent: "space-between",
    borderTopWidth: 1, borderColor: "#16211f", paddingTop: 4, marginTop: 4, fontWeight: 800, fontSize: 10.5 },

  firmas: { flexDirection: "row", justifyContent: "space-between", marginTop: 26 },
  firma: { width: "45%", alignItems: "center" },
  firmaLinea: { borderTopWidth: 1, borderColor: "#16211f", width: "100%", marginBottom: 3 },
  firmaLabel: { fontSize: 8.5 },
  firmaSmall: { fontSize: 7.5, color: "#57635f" },

  pie: { marginTop: 18, fontSize: 7, color: "#8a948f" },
});

interface Props {
  arqueo: ArqueoX;
  cierre: CierreTurnoResultado;
  usuario: string;
  motivoDescripcion?: string | null;
  observaciones?: string | null;
}

function RendicionDocument({ arqueo, cierre, usuario, motivoDescripcion, observaciones }: Props) {
  const totalEsperado = cierre.detalle.reduce((acc, d) => acc + d.esperado, 0);
  const totalDeclarado = cierre.detalle.reduce((acc, d) => acc + d.declarado, 0);
  const hayDiferencia = Math.abs(cierre.diferenciaTotal) >= 0.01;

  // "Efectivo Real (contado)" y "Otros Medios (contado)" — el detalle por medio de pago
  // (cierre.detalle) ya viene con el saldo inicial sumado y retiros/vueltos/notas de crédito
  // restados del medio Efectivo (ver ArmarDetalleAsync/AcumularAsync en el backend). La línea
  // "Efectivo" de acá arriba deshace esos descuentos para mostrar la venta BRUTA en efectivo del
  // turno, antes de que salieran los retiros/vueltos/créditos — el cajero la usa para ver de un
  // vistazo cuánto vendió en efectivo, no solo cuánto quedó al final.
  const efectivo = cierre.detalle.find((d) => d.descripcion === "Efectivo");
  const efectivoReal = efectivo?.declarado ?? 0;
  const saldoInicial = arqueo.ingresoInicial?.monto ?? 0;
  // efectivoReal ya trae el saldo inicial sumado (no solo ventas) — se resta acá para no
  // contarlo dos veces al deshacer el resto de los descuentos.
  const efectivoBruto = efectivoReal - saldoInicial + arqueo.totalRetiros + arqueo.totalVueltos + cierre.totalAnulaciones;
  const otrosMedios = cierre.detalle.filter((d) => d.descripcion !== "Efectivo")
    .reduce((acc, d) => acc + d.declarado, 0);

  return (
    <Document>
      <Page size="A4" style={s.page}>
        <View style={s.header}>
          <View>
            <Text style={s.h1}>Rendición de caja</Text>
            <Text style={s.subtitulo}>Cierre de turno del cajero — comprobante para Tesorería</Text>
          </View>
          <View style={s.folio}>
            <Text style={s.folioLabel}>N.º de cierre</Text>
            <Text style={s.folioValor}>T-{String(cierre.numeroCierre).padStart(6, "0")}</Text>
          </View>
        </View>

        <View style={s.meta}>
          <View style={s.metaItem}><Text style={s.metaLabel}>Cajero</Text><Text style={s.metaValor}>{usuario}</Text></View>
          <View style={s.metaItem}><Text style={s.metaLabel}>Caja</Text><Text style={s.metaValor}>{arqueo.descripcionCaja}</Text></View>
          <View style={s.metaItem}><Text style={s.metaLabel}>Lote</Text><Text style={s.metaValor}>#{arqueo.idLote}</Text></View>
          <View style={s.metaItemFull}><Text style={s.metaLabel}>Apertura del turno</Text><Text style={s.metaValor}>{fechaHora(arqueo.fechaApertura)}</Text></View>
          <View style={s.metaItemFull}><Text style={s.metaLabel}>Cierre del turno</Text><Text style={s.metaValor}>{fechaHora(cierre.fechaCierre)}</Text></View>
        </View>

        {arqueo.ingresoInicial && (
          <View style={s.bloque}>
            <Text style={s.h2}>Saldo inicial</Text>
            <View style={s.filaHead}>
              <Text style={s.celda}>Hora</Text><Text style={s.celda}>Concepto</Text><Text style={s.celdaNum}>Importe</Text>
            </View>
            <View style={s.fila}>
              <Text style={s.celda}>{fechaHora(arqueo.ingresoInicial.fecha)}</Text>
              <Text style={s.celda}>{arqueo.ingresoInicial.concepto ?? "Fondo de apertura"}</Text>
              <Text style={s.celdaNum}>${money(arqueo.ingresoInicial.monto)}</Text>
            </View>
          </View>
        )}

        <View style={s.bloque}>
          <Text style={s.h2}>Operaciones por medio de pago</Text>
          <View style={s.filaHead}>
            <Text style={s.celda}>Medio de pago</Text>
            <Text style={s.celdaNum}>Esperado</Text>
            <Text style={s.celdaNum}>Declarado</Text>
            <Text style={s.celdaNum}>Diferencia</Text>
          </View>
          {cierre.detalle.map((d) => (
            <View style={s.fila} key={d.idMedioPago}>
              <Text style={s.celda}>{d.descripcion}</Text>
              <Text style={s.celdaNum}>${money(d.esperado)}</Text>
              <Text style={s.celdaNum}>${money(d.declarado)}</Text>
              <Text style={[s.celdaNum, d.requiereMotivo ? s.diferencia : undefined]}>${money(d.diferencia)}</Text>
            </View>
          ))}
          {cierre.detalle.length === 0 && <Text>Sin movimientos en este lote.</Text>}
          <View style={s.filaFoot}>
            <Text style={s.celda}>Total</Text>
            <Text style={s.celdaNum}>${money(totalEsperado)}</Text>
            <Text style={s.celdaNum}>${money(totalDeclarado)}</Text>
            <Text style={[s.celdaNum, hayDiferencia ? s.diferencia : undefined]}>${money(cierre.diferenciaTotal)}</Text>
          </View>
        </View>

        {cierre.anulaciones.length > 0 && (
          <View style={s.bloque}>
            <Text style={s.h2}>Notas de crédito emitidas</Text>
            <View style={s.filaHead}>
              <Text style={s.celda}>Nota de crédito</Text><Text style={s.celda}>Anula comprobante</Text>
              <Text style={s.celda}>Motivo</Text><Text style={s.celdaNum}>Importe</Text>
            </View>
            {cierre.anulaciones.map((a) => (
              <View style={s.fila} key={a.idComprobante}>
                <Text style={s.celda}>{a.numeroCompleto} {a.letra}</Text>
                <Text style={s.celda}>{a.comprobanteOrigen ?? "—"}</Text>
                <Text style={s.celda}>{a.motivo ?? "—"}</Text>
                <Text style={s.celdaNum}>−${money(a.total)}</Text>
              </View>
            ))}
            <View style={s.filaFoot}>
              <Text style={{ flex: 3 }}>Total notas de crédito</Text>
              <Text style={s.celdaNum}>−${money(cierre.totalAnulaciones)}</Text>
            </View>
          </View>
        )}

        {arqueo.retiros.length > 0 && (
          <View style={s.bloque}>
            <Text style={s.h2}>Retiros de efectivo</Text>
            <View style={s.filaHead}>
              <Text style={s.celda}>Hora</Text><Text style={s.celda}>Concepto</Text>
              <Text style={s.celdaCorta}>Autorizó / retiró</Text><Text style={s.celdaNumAncha}>Importe</Text>
            </View>
            {arqueo.retiros.map((r) => (
              <View style={s.fila} key={r.idMovCaja}>
                <Text style={s.celda}>{fechaHora(r.fecha)}</Text>
                <Text style={s.celda}>{r.concepto ?? "—"}</Text>
                <Text style={s.celdaCorta}>{r.usuario ?? "—"}</Text>
                <Text style={s.celdaNumAncha}>−${money(r.monto)}</Text>
              </View>
            ))}
            <View style={s.filaFoot}>
              <Text style={{ flex: 5 }}>Total retiros</Text>
              <Text style={s.celdaNumAncha}>−${money(arqueo.totalRetiros)}</Text>
            </View>
          </View>
        )}

        {arqueo.vueltos.length > 0 && (
          <View style={s.bloque}>
            <Text style={s.h2}>Vueltos entregados</Text>
            {/* Un solo ítem con el total del turno — no uno por comprobante (a diferencia de
                Retiros/Notas de crédito, que sí se justifican caso por caso, el vuelto es un
                goteo constante de casi todas las ventas en efectivo y detallarlo uno por uno
                no aporta nada a la rendición). */}
            <View style={s.fila}>
              <Text style={{ flex: 3 }}>Total de vueltos entregados en el turno</Text>
              <Text style={s.celdaNum}>−${money(arqueo.totalVueltos)}</Text>
            </View>
          </View>
        )}

        <View style={s.bloque}>
          <Text style={s.h2}>Resumen del turno</Text>
          <View style={s.resumenGrid}>
            <View style={s.resumenItem}><Text>Saldo Inicial</Text><Text>${money(saldoInicial)}</Text></View>
            <View style={s.resumenItem}><Text>Efectivo</Text><Text>${money(efectivoBruto)}</Text></View>
            <View style={s.resumenItem}><Text>Retiros</Text><Text>−${money(arqueo.totalRetiros)}</Text></View>
            <View style={s.resumenItem}><Text>Créditos</Text><Text>−${money(cierre.totalAnulaciones)}</Text></View>
            <View style={s.resumenItem}><Text>Vueltos</Text><Text>−${money(arqueo.totalVueltos)}</Text></View>
            <View style={s.resumenItemBold}><Text>Efectivo Real (contado)</Text><Text>${money(efectivoReal)}</Text></View>
            <View style={s.resumenItemBold}><Text>Otros Medios (contado)</Text><Text>${money(otrosMedios)}</Text></View>
            <View style={s.resumenItem}><Text>Total esperado (sistema)</Text><Text>${money(totalEsperado)}</Text></View>
            <View style={[s.resumenTotal, hayDiferencia ? s.diferencia : undefined]}>
              <Text>Diferencia Total</Text><Text>${money(cierre.diferenciaTotal)}</Text>
            </View>
          </View>
        </View>

        {(motivoDescripcion || observaciones) && (
          <View style={s.bloque}>
            <Text style={s.h2}>Justificación de diferencias</Text>
            {motivoDescripcion && <Text>Motivo: {motivoDescripcion}</Text>}
            {observaciones && <Text>Observaciones del cajero: {observaciones}</Text>}
          </View>
        )}

        <View style={s.firmas}>
          <View style={s.firma}>
            <View style={s.firmaLinea} />
            <Text style={s.firmaLabel}>Firma del cajero</Text>
            <Text style={s.firmaSmall}>{usuario}</Text>
          </View>
          <View style={s.firma}>
            <View style={s.firmaLinea} />
            <Text style={s.firmaLabel}>Recibido por Tesorería</Text>
            <Text style={s.firmaSmall}>Aclaración y fecha</Text>
          </View>
        </View>

        <Text style={s.pie}>
          Este comprobante se generó automáticamente al confirmar el cierre de turno — el cierre es
          irreversible. Generado el {fechaHora(new Date().toISOString())}.
        </Text>
      </Page>
    </Document>
  );
}

/**
 * Abre una pestaña en blanco. Llamar a esto SINCRÓNICAMENTE en el handler del click (antes de armar
 * el PDF, que es async) — el navegador solo asocia `window.open` al gesto del click si se llama sin
 * ningún `await` de por medio; si se abre después, lo bloquea como popup. Mismo criterio que
 * `abrirPestañaParaPdf` en EtiquetaPdf.tsx.
 */
export function abrirPestañaParaRendicion(): Window | null {
  return window.open("", "_blank");
}

/** Arma el PDF de la rendición y lo carga en la ventana ya abierta (ver la función de arriba). */
export async function generarYAbrirRendicionPdf(props: Props, ventana: Window | null): Promise<void> {
  const blob = await pdf(<RendicionDocument {...props} />).toBlob();
  const url = URL.createObjectURL(blob);
  if (ventana && !ventana.closed) ventana.location.href = url;
  else window.open(url, "_blank");
}

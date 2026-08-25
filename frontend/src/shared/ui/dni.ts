/**
 * Parser del QR frontal del DNI argentino (formato "nuevo ejemplar", vigente desde 2012). El lector
 * escribe el contenido crudo del QR como si fuera un teclado — mismo mecanismo que el lector de
 * códigos de barra de Caja (ver CajaPage). El QR trae los datos en claro separados por comillas
 * dobles ("), confirmado contra un DNI real (2026-08-25):
 *
 *   Nº Trámite"Apellido"Nombre"Sexo"Nº Documento"Ejemplar"Fecha Nacimiento"Fecha Emisión"CUIL
 *
 * ej.: 00696492766"ELIA GIMENEZ"TOMAS"M"43255207"B"05-02-2001"29-11-2022"202...
 *
 * Se acepta también "@" como separador de respaldo (formato citado en documentación pública de
 * terceros, nunca confirmado contra un lector real) por si algún modelo de lector da ese formato.
 */
export interface DniQr {
  tramite: string;
  apellido: string;
  nombre: string;
  sexo: string;
  documento: string;
  ejemplar: string;
  fechaNacimiento: string;
  fechaEmision: string;
  cuil: string;
}

const SEPARADORES = ['"', "@"];

/** Devuelve los datos parseados si el string tiene pinta de QR de DNI (9 campos). */
export function parseDniQr(raw: string): DniQr | null {
  const texto = raw.trim();
  for (const sep of SEPARADORES) {
    const partes = texto.split(sep);
    if (partes.length !== 9) continue;
    const [tramite, apellido, nombre, sexo, documento, ejemplar, fechaNacimiento, fechaEmision, cuil] = partes;
    if (!documento || !/^\d+$/.test(documento)) continue;
    return { tramite, apellido, nombre, sexo, documento, ejemplar, fechaNacimiento, fechaEmision, cuil };
  }
  return null;
}

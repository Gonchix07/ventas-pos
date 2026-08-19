/*
  Normalización de los clientes que quedaron con MÁS DE UNA tarjeta activa.

  Contexto: la regla "un cliente = una tarjeta vigente" se aplica desde el ABM
  (TarjetaAdminService.AddTarjetaAsync anula la anterior al dar de alta una nueva), pero el padrón
  importado del ERP ya traía 2.310 clientes con 2 tarjetas, todas con la MISMA fecha de alta —
  no hay dato para deducir cuál es la vigente. Por eso el import no las tocó y tampoco se creó el
  índice único en la base.

  Este script NO se ejecuta solo. Correr primero el diagnóstico, decidir el criterio, y recién
  después el UPDATE. Hoy el sistema funciona igual sin correrlo: al darle una tarjeta nueva a uno
  de esos clientes, el ABM le anula TODAS las anteriores y queda normalizado solo.
*/

-- 1) Diagnóstico: cuántos y cuáles.
SELECT COUNT(*) AS ClientesConVariasActivas
FROM (SELECT IdCliente FROM TarjetasClientes WHERE Activa = 1
      GROUP BY IdCliente HAVING COUNT(*) > 1) x;

SELECT tc.IdCliente, c.CodigoInt, c.Descripcion, tc.IdTipoTarjeta, tc.NroTarjeta, tc.CreatedAtUtc
FROM TarjetasClientes tc
JOIN Clientes c ON c.IdCliente = tc.IdCliente
WHERE tc.Activa = 1
  AND tc.IdCliente IN (SELECT IdCliente FROM TarjetasClientes WHERE Activa = 1
                       GROUP BY IdCliente HAVING COUNT(*) > 1)
ORDER BY tc.IdCliente, tc.NroTarjeta;

-- 2) Criterio propuesto: queda vigente el número MÁS ALTO (suele ser el emitido último) y el resto
--    se anula. Cambiar el ORDER BY si el criterio es otro.
/*
BEGIN TRAN;

WITH Ranking AS (
    SELECT tc.*, ROW_NUMBER() OVER (PARTITION BY IdCliente ORDER BY NroTarjeta DESC) AS Orden
    FROM TarjetasClientes tc
    WHERE Activa = 1
)
UPDATE Ranking
SET Activa = 0, FechaBajaUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = 'normalizacion-tarjetas'
WHERE Orden > 1;

-- Verificar que quedó 0 antes de confirmar.
SELECT COUNT(*) AS ClientesConVariasActivas
FROM (SELECT IdCliente FROM TarjetasClientes WHERE Activa = 1
      GROUP BY IdCliente HAVING COUNT(*) > 1) x;

-- COMMIT;  -- o ROLLBACK;
*/

-- 3) Recién con el paso 2 en 0 se puede blindar la regla en la base. Si se crea este índice, hay
--    que agregarlo también al modelo (PosDbContext) con una migración, para que EF no lo borre.
/*
CREATE UNIQUE INDEX IX_TarjetasClientes_UnaActivaPorCliente
    ON TarjetasClientes (IdCliente) WHERE Activa = 1;
*/

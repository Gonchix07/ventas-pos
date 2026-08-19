using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CabecerasOfertas",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdOferta = table.Column<int>(type: "int", nullable: false),
                    IdAccion = table.Column<int>(type: "int", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaFin = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Acumula = table.Column<bool>(type: "bit", nullable: false),
                    PermiteConvenio = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CabecerasOfertas", x => new { x.IdSucursal, x.IdOferta });
                });

            migrationBuilder.CreateTable(
                name: "Cajas",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdCaja = table.Column<int>(type: "int", nullable: false),
                    IdPuntoVenta = table.Column<int>(type: "int", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IdPuestoAsignado = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cajas", x => new { x.IdSucursal, x.IdCaja });
                });

            migrationBuilder.CreateTable(
                name: "CierresLotesCaja",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdLote = table.Column<int>(type: "int", nullable: false),
                    IdMedioPago = table.Column<int>(type: "int", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    NumeroCierre = table.Column<int>(type: "int", nullable: false),
                    RedondeoAcumulado = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DiferenciaTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IdMotivoDiferencia = table.Column<int>(type: "int", nullable: true),
                    ObservacionesCajero = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VerificaTesoreria = table.Column<bool>(type: "bit", nullable: false),
                    IdMotivoCierre = table.Column<int>(type: "int", nullable: true),
                    ObservacionTesoreria = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CierresLotesCaja", x => new { x.IdSucursal, x.IdLote, x.IdMedioPago });
                });

            migrationBuilder.CreateTable(
                name: "ComprobantesAsociados",
                columns: table => new
                {
                    IdComprobanteOrigen = table.Column<int>(type: "int", nullable: false),
                    IdComprobanteAsociado = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprobantesAsociados", x => new { x.IdComprobanteOrigen, x.IdComprobanteAsociado });
                });

            migrationBuilder.CreateTable(
                name: "CondicionesIva",
                columns: table => new
                {
                    IdCondIva = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Letra = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CodigoInterno = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CondicionesIva", x => x.IdCondIva);
                });

            migrationBuilder.CreateTable(
                name: "Configuraciones",
                columns: table => new
                {
                    IdConfiguracion = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Clave = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Valor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configuraciones", x => x.IdConfiguracion);
                });

            migrationBuilder.CreateTable(
                name: "CuentasCorrientes",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdCliente = table.Column<int>(type: "int", nullable: false),
                    IdComprobante = table.Column<int>(type: "int", nullable: false),
                    Debe = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Haber = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CuentasCorrientes", x => new { x.IdSucursal, x.IdCliente, x.IdComprobante });
                });

            migrationBuilder.CreateTable(
                name: "Cupones",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdCupon = table.Column<int>(type: "int", nullable: false),
                    IdMedioPago = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NroCupon = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NroLote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IdComprobante = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cupones", x => new { x.IdSucursal, x.IdCupon });
                });

            migrationBuilder.CreateTable(
                name: "Empresas",
                columns: table => new
                {
                    IdEmpresa = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CodigoInterno = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Cuit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CertificadoAlias = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Empresas", x => x.IdEmpresa);
                });

            migrationBuilder.CreateTable(
                name: "Familias",
                columns: table => new
                {
                    IdFamilia = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Familias", x => x.IdFamilia);
                });

            migrationBuilder.CreateTable(
                name: "Lineas",
                columns: table => new
                {
                    IdLinea = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lineas", x => x.IdLinea);
                });

            migrationBuilder.CreateTable(
                name: "ListasPrecios",
                columns: table => new
                {
                    IdListaPrecio = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    CodigoInterno = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Prioridad = table.Column<int>(type: "int", nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaFin = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListasPrecios", x => x.IdListaPrecio);
                });

            migrationBuilder.CreateTable(
                name: "LotesCaja",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdLote = table.Column<int>(type: "int", nullable: false),
                    IdCaja = table.Column<int>(type: "int", nullable: false),
                    IdUsuarioApertura = table.Column<int>(type: "int", nullable: false),
                    FechaApertura = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaCierre = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotesCaja", x => new { x.IdSucursal, x.IdLote });
                });

            migrationBuilder.CreateTable(
                name: "ModosIva",
                columns: table => new
                {
                    IdModoIva = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Alicuota = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PorcentajePercepcion = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModosIva", x => x.IdModoIva);
                });

            migrationBuilder.CreateTable(
                name: "Modulos",
                columns: table => new
                {
                    IdModulo = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modulos", x => x.IdModulo);
                });

            migrationBuilder.CreateTable(
                name: "MotivosCierre",
                columns: table => new
                {
                    IdMotivoCierre = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MotivosCierre", x => x.IdMotivoCierre);
                });

            migrationBuilder.CreateTable(
                name: "MotivosDiferencia",
                columns: table => new
                {
                    IdMotivoDiferencia = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MotivosDiferencia", x => x.IdMotivoDiferencia);
                });

            migrationBuilder.CreateTable(
                name: "MovimientosAuditoria",
                columns: table => new
                {
                    IdMovimiento = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FechaUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IdUsuario = table.Column<int>(type: "int", nullable: true),
                    Usuario = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Modulo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Accion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Entidad = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EntidadId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DatosAntes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DatosDespues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Puesto = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimientosAuditoria", x => x.IdMovimiento);
                });

            migrationBuilder.CreateTable(
                name: "MovimientosCaja",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdMovCaja = table.Column<int>(type: "int", nullable: false),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    IdCaja = table.Column<int>(type: "int", nullable: false),
                    IdComprobante = table.Column<int>(type: "int", nullable: true),
                    IdLote = table.Column<int>(type: "int", nullable: false),
                    IdMovPagos = table.Column<long>(type: "bigint", nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimientosCaja", x => new { x.IdSucursal, x.IdMovCaja });
                });

            migrationBuilder.CreateTable(
                name: "MovimientosPagos",
                columns: table => new
                {
                    IdMovPagos = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdMedioPago = table.Column<int>(type: "int", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Redondeo = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimientosPagos", x => x.IdMovPagos);
                });

            migrationBuilder.CreateTable(
                name: "Numeros",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdNumero = table.Column<int>(type: "int", nullable: false),
                    IdPuntoVenta = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Numeros", x => new { x.IdSucursal, x.IdNumero });
                });

            migrationBuilder.CreateTable(
                name: "PadronExcepcionPercepcionesIva",
                columns: table => new
                {
                    Cuit = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PadronExcepcionPercepcionesIva", x => x.Cuit);
                });

            migrationBuilder.CreateTable(
                name: "PadronIngresosBrutos",
                columns: table => new
                {
                    Cuit = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    Percepcion = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PadronIngresosBrutos", x => x.Cuit);
                });

            migrationBuilder.CreateTable(
                name: "PuestosCaja",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdPuestoAsignado = table.Column<int>(type: "int", nullable: false),
                    NombrePc = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PuestosCaja", x => new { x.IdSucursal, x.IdPuestoAsignado });
                });

            migrationBuilder.CreateTable(
                name: "PuntosVenta",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdPuntoVenta = table.Column<int>(type: "int", nullable: false),
                    IdTipoPuntoVenta = table.Column<int>(type: "int", nullable: false),
                    NumeroPuntoVenta = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PuntosVenta", x => new { x.IdSucursal, x.IdPuntoVenta });
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    IdRol = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.IdRol);
                });

            migrationBuilder.CreateTable(
                name: "Sectores",
                columns: table => new
                {
                    IdSector = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sectores", x => x.IdSector);
                });

            migrationBuilder.CreateTable(
                name: "TiposComprobante",
                columns: table => new
                {
                    IdTipoComprobante = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Letra = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CodigoArca = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Signo = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TiposComprobante", x => x.IdTipoComprobante);
                });

            migrationBuilder.CreateTable(
                name: "TiposOferta",
                columns: table => new
                {
                    IdTipoOferta = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TiposOferta", x => x.IdTipoOferta);
                });

            migrationBuilder.CreateTable(
                name: "TiposPago",
                columns: table => new
                {
                    IdTipoPago = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Fuente = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TiposPago", x => x.IdTipoPago);
                });

            migrationBuilder.CreateTable(
                name: "TiposPuntoVenta",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdTipoPuntoVenta = table.Column<int>(type: "int", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TipoArca = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TiposPuntoVenta", x => new { x.IdSucursal, x.IdTipoPuntoVenta });
                });

            migrationBuilder.CreateTable(
                name: "TiposTarjeta",
                columns: table => new
                {
                    IdTipoTarjeta = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IdListaPrecio = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TiposTarjeta", x => x.IdTipoTarjeta);
                });

            migrationBuilder.CreateTable(
                name: "AlcancesOfertas",
                columns: table => new
                {
                    IdAlcance = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdOferta = table.Column<int>(type: "int", nullable: false),
                    IdCluster = table.Column<int>(type: "int", nullable: true),
                    IdLinea = table.Column<int>(type: "int", nullable: true),
                    IdSector = table.Column<int>(type: "int", nullable: true),
                    IdFamilia = table.Column<int>(type: "int", nullable: true),
                    IdArticulo = table.Column<int>(type: "int", nullable: true),
                    EsExcepcion = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlcancesOfertas", x => x.IdAlcance);
                    table.ForeignKey(
                        name: "FK_AlcancesOfertas_CabecerasOfertas_IdSucursal_IdOferta",
                        columns: x => new { x.IdSucursal, x.IdOferta },
                        principalTable: "CabecerasOfertas",
                        principalColumns: new[] { "IdSucursal", "IdOferta" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Clientes",
                columns: table => new
                {
                    IdCliente = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CodigoInt = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Cuit = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    Documento = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IdCondIva = table.Column<int>(type: "int", nullable: false),
                    PermitePresupuesto = table.Column<bool>(type: "bit", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clientes", x => x.IdCliente);
                    table.ForeignKey(
                        name: "FK_Clientes_CondicionesIva_IdCondIva",
                        column: x => x.IdCondIva,
                        principalTable: "CondicionesIva",
                        principalColumn: "IdCondIva",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sucursales",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdEmpresa = table.Column<int>(type: "int", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sucursales", x => x.IdSucursal);
                    table.ForeignKey(
                        name: "FK_Sucursales_Empresas_IdEmpresa",
                        column: x => x.IdEmpresa,
                        principalTable: "Empresas",
                        principalColumn: "IdEmpresa",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Permisos",
                columns: table => new
                {
                    IdPermiso = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdRol = table.Column<int>(type: "int", nullable: false),
                    IdModulo = table.Column<int>(type: "int", nullable: false),
                    PuedeVer = table.Column<bool>(type: "bit", nullable: false),
                    PuedeEditar = table.Column<bool>(type: "bit", nullable: false),
                    EsEspecial = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permisos", x => x.IdPermiso);
                    table.ForeignKey(
                        name: "FK_Permisos_Modulos_IdModulo",
                        column: x => x.IdModulo,
                        principalTable: "Modulos",
                        principalColumn: "IdModulo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permisos_Roles_IdRol",
                        column: x => x.IdRol,
                        principalTable: "Roles",
                        principalColumn: "IdRol",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    IdUsuario = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NombreUsuario = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClaveHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    IdRol = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.IdUsuario);
                    table.ForeignKey(
                        name: "FK_Usuarios_Roles_IdRol",
                        column: x => x.IdRol,
                        principalTable: "Roles",
                        principalColumn: "IdRol",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Articulos",
                columns: table => new
                {
                    IdArticulo = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CodigoInterno = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IdSector = table.Column<int>(type: "int", nullable: false),
                    IdLinea = table.Column<int>(type: "int", nullable: false),
                    IdFamilia = table.Column<int>(type: "int", nullable: false),
                    IdModoIva = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Articulos", x => x.IdArticulo);
                    table.ForeignKey(
                        name: "FK_Articulos_Familias_IdFamilia",
                        column: x => x.IdFamilia,
                        principalTable: "Familias",
                        principalColumn: "IdFamilia",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Articulos_Lineas_IdLinea",
                        column: x => x.IdLinea,
                        principalTable: "Lineas",
                        principalColumn: "IdLinea",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Articulos_ModosIva_IdModoIva",
                        column: x => x.IdModoIva,
                        principalTable: "ModosIva",
                        principalColumn: "IdModoIva",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Articulos_Sectores_IdSector",
                        column: x => x.IdSector,
                        principalTable: "Sectores",
                        principalColumn: "IdSector",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CabecerasComprobantes",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdComprobante = table.Column<int>(type: "int", nullable: false),
                    IdTipoComprobante = table.Column<int>(type: "int", nullable: false),
                    IdCliente = table.Column<int>(type: "int", nullable: true),
                    IdPuntoVenta = table.Column<int>(type: "int", nullable: true),
                    IdOperacion = table.Column<int>(type: "int", nullable: true),
                    Letra = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NumeroCompleto = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Neto = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Iva = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Percepciones = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Cae = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CaeVencimiento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EsCaea = table.Column<bool>(type: "bit", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CabecerasComprobantes", x => new { x.IdSucursal, x.IdComprobante });
                    table.ForeignKey(
                        name: "FK_CabecerasComprobantes_TiposComprobante_IdTipoComprobante",
                        column: x => x.IdTipoComprobante,
                        principalTable: "TiposComprobante",
                        principalColumn: "IdTipoComprobante",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccionesOfertas",
                columns: table => new
                {
                    IdAccion = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdOferta = table.Column<int>(type: "int", nullable: false),
                    IdTipoOferta = table.Column<int>(type: "int", nullable: false),
                    IdPresentacion = table.Column<int>(type: "int", nullable: true),
                    Porcentaje = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    MontoFijo = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CantidadMin = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CantidadBonif = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccionesOfertas", x => x.IdAccion);
                    table.ForeignKey(
                        name: "FK_AccionesOfertas_CabecerasOfertas_IdSucursal_IdOferta",
                        columns: x => new { x.IdSucursal, x.IdOferta },
                        principalTable: "CabecerasOfertas",
                        principalColumns: new[] { "IdSucursal", "IdOferta" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccionesOfertas_TiposOferta_IdTipoOferta",
                        column: x => x.IdTipoOferta,
                        principalTable: "TiposOferta",
                        principalColumn: "IdTipoOferta",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MediosPago",
                columns: table => new
                {
                    IdMedioPago = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IdTipoPago = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediosPago", x => x.IdMedioPago);
                    table.ForeignKey(
                        name: "FK_MediosPago_TiposPago_IdTipoPago",
                        column: x => x.IdTipoPago,
                        principalTable: "TiposPago",
                        principalColumn: "IdTipoPago",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Autorizados",
                columns: table => new
                {
                    IdAutorizado = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdCliente = table.Column<int>(type: "int", nullable: false),
                    Dni = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Autorizados", x => x.IdAutorizado);
                    table.ForeignKey(
                        name: "FK_Autorizados_Clientes_IdCliente",
                        column: x => x.IdCliente,
                        principalTable: "Clientes",
                        principalColumn: "IdCliente",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClientesEnCuenta",
                columns: table => new
                {
                    IdCliente = table.Column<int>(type: "int", nullable: false),
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    LimiteCredito = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientesEnCuenta", x => new { x.IdCliente, x.IdSucursal });
                    table.ForeignKey(
                        name: "FK_ClientesEnCuenta_Clientes_IdCliente",
                        column: x => x.IdCliente,
                        principalTable: "Clientes",
                        principalColumn: "IdCliente",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClusterClientes",
                columns: table => new
                {
                    IdCluster = table.Column<int>(type: "int", nullable: false),
                    IdCliente = table.Column<int>(type: "int", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClusterClientes", x => new { x.IdCluster, x.IdCliente });
                    table.ForeignKey(
                        name: "FK_ClusterClientes_Clientes_IdCliente",
                        column: x => x.IdCliente,
                        principalTable: "Clientes",
                        principalColumn: "IdCliente",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Convenios",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdConvenio = table.Column<int>(type: "int", nullable: false),
                    IdCliente = table.Column<int>(type: "int", nullable: false),
                    Descuento = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IdListaPrecio = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Convenios", x => new { x.IdSucursal, x.IdConvenio });
                    table.ForeignKey(
                        name: "FK_Convenios_Clientes_IdCliente",
                        column: x => x.IdCliente,
                        principalTable: "Clientes",
                        principalColumn: "IdCliente",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Operaciones",
                columns: table => new
                {
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdOperacion = table.Column<int>(type: "int", nullable: false),
                    IdCliente = table.Column<int>(type: "int", nullable: true),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DescuentoTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operaciones", x => new { x.IdSucursal, x.IdOperacion });
                    table.ForeignKey(
                        name: "FK_Operaciones_Clientes_IdCliente",
                        column: x => x.IdCliente,
                        principalTable: "Clientes",
                        principalColumn: "IdCliente",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TarjetasClientes",
                columns: table => new
                {
                    IdCliente = table.Column<int>(type: "int", nullable: false),
                    IdTipoTarjeta = table.Column<int>(type: "int", nullable: false),
                    NroTarjeta = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TarjetasClientes", x => new { x.IdCliente, x.IdTipoTarjeta, x.NroTarjeta });
                    table.ForeignKey(
                        name: "FK_TarjetasClientes_Clientes_IdCliente",
                        column: x => x.IdCliente,
                        principalTable: "Clientes",
                        principalColumn: "IdCliente",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TarjetasClientes_TiposTarjeta_IdTipoTarjeta",
                        column: x => x.IdTipoTarjeta,
                        principalTable: "TiposTarjeta",
                        principalColumn: "IdTipoTarjeta",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Presentaciones",
                columns: table => new
                {
                    IdPresentacion = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdArticulo = table.Column<int>(type: "int", nullable: false),
                    UnidadXBulto = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DescripcionTicket = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Presentaciones", x => x.IdPresentacion);
                    table.ForeignKey(
                        name: "FK_Presentaciones_Articulos_IdArticulo",
                        column: x => x.IdArticulo,
                        principalTable: "Articulos",
                        principalColumn: "IdArticulo",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DetallesComprobantes",
                columns: table => new
                {
                    IdDetalleComprobante = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdComprobante = table.Column<int>(type: "int", nullable: false),
                    IdPresentacion = table.Column<int>(type: "int", nullable: false),
                    DescripcionTicket = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PrecioUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Descuento = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AlicuotaIva = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Importe = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DetallesComprobantes", x => x.IdDetalleComprobante);
                    table.ForeignKey(
                        name: "FK_DetallesComprobantes_CabecerasComprobantes_IdSucursal_IdComprobante",
                        columns: x => new { x.IdSucursal, x.IdComprobante },
                        principalTable: "CabecerasComprobantes",
                        principalColumns: new[] { "IdSucursal", "IdComprobante" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DetallesOperaciones",
                columns: table => new
                {
                    IdDetalleOperacion = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdSucursal = table.Column<int>(type: "int", nullable: false),
                    IdOperacion = table.Column<int>(type: "int", nullable: false),
                    IdPresentacion = table.Column<int>(type: "int", nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Precio = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Descuento = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OfertasAplicadas = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DetallesOperaciones", x => x.IdDetalleOperacion);
                    table.ForeignKey(
                        name: "FK_DetallesOperaciones_Operaciones_IdSucursal_IdOperacion",
                        columns: x => new { x.IdSucursal, x.IdOperacion },
                        principalTable: "Operaciones",
                        principalColumns: new[] { "IdSucursal", "IdOperacion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Barras",
                columns: table => new
                {
                    IdBarra = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdPresentacion = table.Column<int>(type: "int", nullable: false),
                    CodigoBarra = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Barras", x => x.IdBarra);
                    table.ForeignKey(
                        name: "FK_Barras_Presentaciones_IdPresentacion",
                        column: x => x.IdPresentacion,
                        principalTable: "Presentaciones",
                        principalColumn: "IdPresentacion",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Precios",
                columns: table => new
                {
                    IdListaPrecio = table.Column<int>(type: "int", nullable: false),
                    IdPresentacion = table.Column<int>(type: "int", nullable: false),
                    IdArticulo = table.Column<int>(type: "int", nullable: false),
                    PrecioFinal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ImpuestoInterno = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Precios", x => new { x.IdListaPrecio, x.IdPresentacion });
                    table.ForeignKey(
                        name: "FK_Precios_ListasPrecios_IdListaPrecio",
                        column: x => x.IdListaPrecio,
                        principalTable: "ListasPrecios",
                        principalColumn: "IdListaPrecio",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Precios_Presentaciones_IdPresentacion",
                        column: x => x.IdPresentacion,
                        principalTable: "Presentaciones",
                        principalColumn: "IdPresentacion",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccionesOfertas_IdSucursal_IdOferta",
                table: "AccionesOfertas",
                columns: new[] { "IdSucursal", "IdOferta" });

            migrationBuilder.CreateIndex(
                name: "IX_AccionesOfertas_IdTipoOferta",
                table: "AccionesOfertas",
                column: "IdTipoOferta");

            migrationBuilder.CreateIndex(
                name: "IX_AlcancesOfertas_IdSucursal_IdOferta",
                table: "AlcancesOfertas",
                columns: new[] { "IdSucursal", "IdOferta" });

            migrationBuilder.CreateIndex(
                name: "IX_Articulos_CodigoInterno",
                table: "Articulos",
                column: "CodigoInterno",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Articulos_IdFamilia",
                table: "Articulos",
                column: "IdFamilia");

            migrationBuilder.CreateIndex(
                name: "IX_Articulos_IdLinea",
                table: "Articulos",
                column: "IdLinea");

            migrationBuilder.CreateIndex(
                name: "IX_Articulos_IdModoIva",
                table: "Articulos",
                column: "IdModoIva");

            migrationBuilder.CreateIndex(
                name: "IX_Articulos_IdSector",
                table: "Articulos",
                column: "IdSector");

            migrationBuilder.CreateIndex(
                name: "IX_Autorizados_IdCliente",
                table: "Autorizados",
                column: "IdCliente");

            migrationBuilder.CreateIndex(
                name: "IX_Barras_CodigoBarra",
                table: "Barras",
                column: "CodigoBarra",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Barras_IdPresentacion",
                table: "Barras",
                column: "IdPresentacion");

            migrationBuilder.CreateIndex(
                name: "IX_CabecerasComprobantes_Cae",
                table: "CabecerasComprobantes",
                column: "Cae");

            migrationBuilder.CreateIndex(
                name: "IX_CabecerasComprobantes_IdTipoComprobante",
                table: "CabecerasComprobantes",
                column: "IdTipoComprobante");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_CodigoInt",
                table: "Clientes",
                column: "CodigoInt",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Cuit",
                table: "Clientes",
                column: "Cuit");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Documento",
                table: "Clientes",
                column: "Documento");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_IdCondIva",
                table: "Clientes",
                column: "IdCondIva");

            migrationBuilder.CreateIndex(
                name: "IX_ClusterClientes_IdCliente",
                table: "ClusterClientes",
                column: "IdCliente");

            migrationBuilder.CreateIndex(
                name: "IX_Configuraciones_Clave",
                table: "Configuraciones",
                column: "Clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Convenios_IdCliente",
                table: "Convenios",
                column: "IdCliente");

            migrationBuilder.CreateIndex(
                name: "IX_CuentasCorrientes_IdSucursal_IdCliente",
                table: "CuentasCorrientes",
                columns: new[] { "IdSucursal", "IdCliente" });

            migrationBuilder.CreateIndex(
                name: "IX_DetallesComprobantes_IdSucursal_IdComprobante",
                table: "DetallesComprobantes",
                columns: new[] { "IdSucursal", "IdComprobante" });

            migrationBuilder.CreateIndex(
                name: "IX_DetallesOperaciones_IdSucursal_IdOperacion",
                table: "DetallesOperaciones",
                columns: new[] { "IdSucursal", "IdOperacion" });

            migrationBuilder.CreateIndex(
                name: "IX_Empresas_CodigoInterno",
                table: "Empresas",
                column: "CodigoInterno",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediosPago_IdTipoPago",
                table: "MediosPago",
                column: "IdTipoPago");

            migrationBuilder.CreateIndex(
                name: "IX_Operaciones_IdCliente",
                table: "Operaciones",
                column: "IdCliente");

            migrationBuilder.CreateIndex(
                name: "IX_Permisos_IdModulo",
                table: "Permisos",
                column: "IdModulo");

            migrationBuilder.CreateIndex(
                name: "IX_Permisos_IdRol",
                table: "Permisos",
                column: "IdRol");

            migrationBuilder.CreateIndex(
                name: "IX_Precios_IdPresentacion",
                table: "Precios",
                column: "IdPresentacion");

            migrationBuilder.CreateIndex(
                name: "IX_Presentaciones_IdArticulo",
                table: "Presentaciones",
                column: "IdArticulo");

            migrationBuilder.CreateIndex(
                name: "IX_Sucursales_IdEmpresa",
                table: "Sucursales",
                column: "IdEmpresa");

            migrationBuilder.CreateIndex(
                name: "IX_TarjetasClientes_IdTipoTarjeta",
                table: "TarjetasClientes",
                column: "IdTipoTarjeta");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_IdRol",
                table: "Usuarios",
                column: "IdRol");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_NombreUsuario",
                table: "Usuarios",
                column: "NombreUsuario",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccionesOfertas");

            migrationBuilder.DropTable(
                name: "AlcancesOfertas");

            migrationBuilder.DropTable(
                name: "Autorizados");

            migrationBuilder.DropTable(
                name: "Barras");

            migrationBuilder.DropTable(
                name: "Cajas");

            migrationBuilder.DropTable(
                name: "CierresLotesCaja");

            migrationBuilder.DropTable(
                name: "ClientesEnCuenta");

            migrationBuilder.DropTable(
                name: "ClusterClientes");

            migrationBuilder.DropTable(
                name: "ComprobantesAsociados");

            migrationBuilder.DropTable(
                name: "Configuraciones");

            migrationBuilder.DropTable(
                name: "Convenios");

            migrationBuilder.DropTable(
                name: "CuentasCorrientes");

            migrationBuilder.DropTable(
                name: "Cupones");

            migrationBuilder.DropTable(
                name: "DetallesComprobantes");

            migrationBuilder.DropTable(
                name: "DetallesOperaciones");

            migrationBuilder.DropTable(
                name: "LotesCaja");

            migrationBuilder.DropTable(
                name: "MediosPago");

            migrationBuilder.DropTable(
                name: "MotivosCierre");

            migrationBuilder.DropTable(
                name: "MotivosDiferencia");

            migrationBuilder.DropTable(
                name: "MovimientosAuditoria");

            migrationBuilder.DropTable(
                name: "MovimientosCaja");

            migrationBuilder.DropTable(
                name: "MovimientosPagos");

            migrationBuilder.DropTable(
                name: "Numeros");

            migrationBuilder.DropTable(
                name: "PadronExcepcionPercepcionesIva");

            migrationBuilder.DropTable(
                name: "PadronIngresosBrutos");

            migrationBuilder.DropTable(
                name: "Permisos");

            migrationBuilder.DropTable(
                name: "Precios");

            migrationBuilder.DropTable(
                name: "PuestosCaja");

            migrationBuilder.DropTable(
                name: "PuntosVenta");

            migrationBuilder.DropTable(
                name: "Sucursales");

            migrationBuilder.DropTable(
                name: "TarjetasClientes");

            migrationBuilder.DropTable(
                name: "TiposPuntoVenta");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropTable(
                name: "TiposOferta");

            migrationBuilder.DropTable(
                name: "CabecerasOfertas");

            migrationBuilder.DropTable(
                name: "CabecerasComprobantes");

            migrationBuilder.DropTable(
                name: "Operaciones");

            migrationBuilder.DropTable(
                name: "TiposPago");

            migrationBuilder.DropTable(
                name: "Modulos");

            migrationBuilder.DropTable(
                name: "ListasPrecios");

            migrationBuilder.DropTable(
                name: "Presentaciones");

            migrationBuilder.DropTable(
                name: "Empresas");

            migrationBuilder.DropTable(
                name: "TiposTarjeta");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "TiposComprobante");

            migrationBuilder.DropTable(
                name: "Clientes");

            migrationBuilder.DropTable(
                name: "Articulos");

            migrationBuilder.DropTable(
                name: "CondicionesIva");

            migrationBuilder.DropTable(
                name: "Familias");

            migrationBuilder.DropTable(
                name: "Lineas");

            migrationBuilder.DropTable(
                name: "ModosIva");

            migrationBuilder.DropTable(
                name: "Sectores");
        }
    }
}

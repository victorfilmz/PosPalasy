using System.Collections.Generic;
using POS.Application.DTOs;
using POS.Domain.Entities;

namespace POS.Application.Interfaces;

public interface IReporteFiscalService
{
    List<Registro607Dto> GenerarRegistros607(IEnumerable<ElectronicInvoice> facturas);
    ResumenItbisMensualDto GenerarResumenItbis(int anio, int mes, IEnumerable<ElectronicInvoice> facturas);
    string GenerarArchivo607Txt(string rncEmisor, int anio, int mes, List<Registro607Dto> registros);

    /// <summary>Libro de compras 606 desde las entradas de compra del kardex (sub-fase 6.0).</summary>
    List<Registro606Dto> GenerarRegistros606(IEnumerable<MovimientoInventario> entradasCompra);
    string GenerarArchivo606Txt(string rncEmisor, int anio, int mes, List<Registro606Dto> registros);
}

using System.Collections.Generic;
using POS.Application.DTOs;
using POS.Domain.Entities;

namespace POS.Application.Interfaces;

public interface IReporteFiscalService
{
    List<Registro607Dto> GenerarRegistros607(IEnumerable<ElectronicInvoice> facturas);
    ResumenItbisMensualDto GenerarResumenItbis(int anio, int mes, IEnumerable<ElectronicInvoice> facturas);
    string GenerarArchivo607Txt(string rncEmisor, int anio, int mes, List<Registro607Dto> registros);
}

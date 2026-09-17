using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Types;

namespace POS.Domain.Repositories;

/// <summary>
/// Contrato de repositorio para persistencia y consultas de Facturas Electrónicas (e-CF).
/// </summary>
public interface IInvoiceRepository
{
    Task<ElectronicInvoice?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ElectronicInvoice?> GetByENCFAsync(string encf, CancellationToken ct = default);
    Task<ElectronicInvoice?> GetByTrackIdAsync(string trackId, CancellationToken ct = default);
    Task<IEnumerable<ElectronicInvoice>> GetByRNCAsync(string rnc, TipoeCFType? tipo = null, CancellationToken ct = default);
    Task<IEnumerable<ElectronicInvoice>> GetByEstadoAsync(EstadoFacturaElectronica estado, CancellationToken ct = default);
    Task<IEnumerable<ElectronicInvoice>> GetPendingAsync(CancellationToken ct = default);
    Task<bool> ExistsENCFAsync(string encf, CancellationToken ct = default);
    Task<string?> GetLastENCFAsync(string serie, CancellationToken ct = default);

    Task<ElectronicInvoice> AddAsync(ElectronicInvoice invoice, CancellationToken ct = default);
    Task UpdateAsync(ElectronicInvoice invoice, CancellationToken ct = default);
}

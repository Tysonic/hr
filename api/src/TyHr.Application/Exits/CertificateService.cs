using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.Exits;

namespace TyHr.Application.Exits;

/// <summary>
/// What a certificate of service states: who employed whom, as what and where, from when to when. It never gives the
/// reason for leaving or a view of the employee's work.
/// </summary>
public record CertificateDocument(string CompanyName, string? CompanyAddress, string? CompanyPhone, string CompanyEmail, string EmployeeName,
    string EmployeeNumber, string? PositionTitle, string? DepartmentName, string? Workplace, DateOnly StartDate, DateOnly EndDate, DateOnly Issued, string IssuedBy);

public interface ICertificateRenderer
{
    byte[] Render(CertificateDocument document);
}

public record CertificateFile(string FileName, byte[] Content);

public class CertificateService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, ICertificateRenderer renderer, TimeProvider clock)
{
    public async Task<CertificateFile> RenderAsync(Guid exitId, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var exit = await db.EmployeeExits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == exitId, ct) ?? throw new NotFoundException("There is no such exit.");
        if (exit.Status != ExitStatus.Completed) throw new DomainException("The certificate of service is issued once the exit is complete.");
        var e = await db.Employees.AsNoTracking().SingleAsync(x => x.Id == exit.EmployeeId, ct);
        var company = await db.CompanyProfiles.AsNoTracking().SingleAsync(ct);
        var position = e.PositionId is { } p ? await db.Positions.AsNoTracking().Where(x => x.Id == p).Select(x => x.Title).SingleOrDefaultAsync(ct) : null;
        var dept = e.DepartmentId is { } d ? await db.Departments.AsNoTracking().Where(x => x.Id == d).Select(x => x.Name).SingleOrDefaultAsync(ct) : null;
        var branch = e.BranchId is { } b ? await db.Branches.AsNoTracking().Where(x => x.Id == b).Select(x => x.Address != null ? x.Name + ", " + x.Address : x.Name).SingleOrDefaultAsync(ct) : null;
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var today = DateOnly.FromDateTime(country.ToLocal(clock.GetUtcNow()).DateTime);
        var signer = user.Email == null ? null
            : await db.Employees.AsNoTracking().Where(x => x.WorkEmail == user.Email).Select(x => x.FirstName + " " + x.LastName).FirstOrDefaultAsync(ct);
        var doc = new CertificateDocument(company.TradingName ?? company.LegalName, company.PostalAddress ?? company.PhysicalAddress, company.Phone, company.Email,
            e.FullName, e.EmployeeNumber, position, dept, branch, e.HireDate, exit.LastWorkingDay, today, signer ?? "Human Resources");
        var safe = string.Concat(e.FullName.Where(char.IsLetterOrDigit));
        return new CertificateFile($"Certificate of service - {safe}.pdf", renderer.Render(doc));
    }
}

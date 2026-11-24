using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.Payroll;

namespace TyHr.Application.Payroll;

/// <summary>Pay components, employees' recurring pay items and tax profiles, and the country's payroll notes.</summary>
public class PayrollSetupService(IAppDbContext db, ITenantContext tenantContext, TimeProvider clock)
{
    public async Task<PayrollSettingsDto> SettingsAsync(CancellationToken ct)
    {
        var c = await db.CompanyCountryAsync(tenantContext, ct);
        return new PayrollSettingsDto(c.Code, c.Name, c.CurrencyCode, c.CurrencyMinorUnits, c.Payroll.SupportedFrom, c.Payroll.Notes);
    }

    public async Task<IReadOnlyList<PayComponentDto>> ComponentsAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        return (await db.PayComponents.AsNoTracking().OrderBy(c => c.Kind).ThenBy(c => c.Name).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<PayComponentDto> CreateComponentAsync(SavePayComponentRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var component = new PayComponent(r.Code, r.Name, r.Kind, r.Taxable, r.Pensionable, r.Description);
        await CheckCodeAsync(component, ct);
        db.PayComponents.Add(component);
        await db.SaveChangesAsync(ct);
        return ToDto(component);
    }

    public async Task<PayComponentDto> UpdateComponentAsync(Guid id, SavePayComponentRequest r, CancellationToken ct)
    {
        var component = await FindComponentAsync(id, ct);
        if (r.Kind != component.Kind) throw new DomainException("An earning can't become a deduction (or the reverse); add a new component.");
        component.Update(r.Code, r.Name, r.Taxable, r.Pensionable, r.Description);
        await CheckCodeAsync(component, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(component);
    }

    public async Task<PayComponentDto> SetComponentActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var component = await FindComponentAsync(id, ct);
        component.SetActive(active);
        await db.SaveChangesAsync(ct);
        return ToDto(component);
    }

    public async Task<EmployeePayDto> EmployeePayAsync(Guid employeeId, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == employeeId, ct) ?? throw new NotFoundException("There is no such employee.");
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var today = DateOnly.FromDateTime(country.ToLocal(clock.GetUtcNow()).DateTime);
        var salary = await db.SalaryChanges.AsNoTracking().Where(s => s.EmployeeId == employeeId && s.EffectiveDate <= today)
            .OrderByDescending(s => s.EffectiveDate).FirstOrDefaultAsync(ct);
        var profile = await db.EmployeeTaxProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.EmployeeId == employeeId, ct);
        var components = await db.PayComponents.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var items = await db.EmployeePayItems.AsNoTracking().Where(i => i.EmployeeId == employeeId).OrderByDescending(i => i.StartDate).ToListAsync(ct);
        return new EmployeePayDto(employee.Id, employee.FullName, country.CurrencyCode, salary?.MonthlyBasic, salary?.EffectiveDate,
            profile?.Resident ?? true, profile?.SecondaryEmployment ?? false,
            items.Select(i => ToDto(i, components[i.ComponentId], today)).OrderBy(i => !i.Current).ThenBy(i => i.Kind).ToList());
    }

    public async Task<EmployeePayDto> AddItemAsync(Guid employeeId, SavePayItemRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, ct)) throw new NotFoundException("There is no such employee.");
        var component = await FindComponentAsync(r.ComponentId, ct);
        if (!component.IsActive) throw new DomainException($"{component.Name} isn't in use.");
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        if (await db.EmployeePayItems.AnyAsync(i => i.EmployeeId == employeeId && i.ComponentId == r.ComponentId
                && i.StartDate <= (r.EndDate ?? DateOnly.MaxValue) && (i.EndDate == null || i.EndDate >= r.StartDate), ct))
            throw new DomainException($"The employee already has {component.Name} for some of that time; end the earlier one first.");
        db.EmployeePayItems.Add(new EmployeePayItem(employeeId, r.ComponentId, r.Amount, r.StartDate, r.EndDate, r.Note, country));
        await db.SaveChangesAsync(ct);
        return await EmployeePayAsync(employeeId, ct);
    }

    public async Task<EmployeePayDto> UpdateItemAsync(Guid employeeId, Guid itemId, SavePayItemRequest r, CancellationToken ct)
    {
        var item = await FindItemAsync(employeeId, itemId, ct);
        if (r.ComponentId != item.ComponentId) throw new DomainException("End this item and add a new one to change what it pays.");
        if (await db.EmployeePayItems.AnyAsync(i => i.Id != itemId && i.EmployeeId == employeeId && i.ComponentId == r.ComponentId
                && i.StartDate <= (r.EndDate ?? DateOnly.MaxValue) && (i.EndDate == null || i.EndDate >= r.StartDate), ct))
            throw new DomainException("Another item for the same thing overlaps those dates.");
        item.Update(r.Amount, r.StartDate, r.EndDate, r.Note, await db.CompanyCountryAsync(tenantContext, ct));
        await db.SaveChangesAsync(ct);
        return await EmployeePayAsync(employeeId, ct);
    }

    /// <summary>Removes an item entered by mistake. Payslips already calculated keep their own copy.</summary>
    public async Task<EmployeePayDto> DeleteItemAsync(Guid employeeId, Guid itemId, CancellationToken ct)
    {
        db.EmployeePayItems.Remove(await FindItemAsync(employeeId, itemId, ct));
        await db.SaveChangesAsync(ct);
        return await EmployeePayAsync(employeeId, ct);
    }

    public async Task<EmployeePayDto> SaveTaxProfileAsync(Guid employeeId, SaveTaxProfileRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, ct)) throw new NotFoundException("There is no such employee.");
        var profile = await db.EmployeeTaxProfiles.SingleOrDefaultAsync(p => p.EmployeeId == employeeId, ct);
        if (profile == null) db.EmployeeTaxProfiles.Add(new EmployeeTaxProfile(employeeId, r.Resident, r.SecondaryEmployment));
        else profile.Update(r.Resident, r.SecondaryEmployment);
        await db.SaveChangesAsync(ct);
        return await EmployeePayAsync(employeeId, ct);
    }

    private async Task<EmployeePayItem> FindItemAsync(Guid employeeId, Guid itemId, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        return await db.EmployeePayItems.SingleOrDefaultAsync(i => i.Id == itemId && i.EmployeeId == employeeId, ct)
               ?? throw new NotFoundException("There is no such pay item.");
    }

    private async Task<PayComponent> FindComponentAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        return await db.PayComponents.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("There is no such pay component.");
    }

    private async Task CheckCodeAsync(PayComponent c, CancellationToken ct)
    {
        if (await db.PayComponents.AnyAsync(x => x.Id != c.Id && x.Code == c.Code, ct)) throw new DomainException($"There is already a component with code {c.Code}.");
    }

    public static PayComponentDto ToDto(PayComponent c) => new(c.Id, c.Code, c.Name, c.Kind, c.Taxable, c.Pensionable, c.Description, c.IsActive);

    private static PayItemDto ToDto(EmployeePayItem i, PayComponent c, DateOnly today) => new(i.Id, i.ComponentId, c.Code, c.Name, c.Kind, i.Amount,
        i.StartDate, i.EndDate, i.Note, i.StartDate <= today && (i.EndDate == null || i.EndDate >= today));
}

/// <summary>A new company's starting components; companies add their own.</summary>
public static class PayrollDefaults
{
    public static async Task SeedAsync(IAppDbContext db, CancellationToken ct)
    {
        db.PayComponents.AddRange(
            new PayComponent("HOUSING", "Housing allowance", PayComponentKind.Earning, true, true),
            new PayComponent("TRANSPORT", "Transport allowance", PayComponentKind.Earning, true, true),
            new PayComponent("OVERTIME", "Overtime", PayComponentKind.Earning, true, true),
            new PayComponent("BONUS", "Bonus", PayComponentKind.Earning, true, true),
            new PayComponent("ARREARS", "Salary arrears", PayComponentKind.Earning, true, true),
            new PayComponent("LOAN", "Loan repayment", PayComponentKind.Deduction, false, false),
            new PayComponent("ADVANCE", "Salary advance recovery", PayComponentKind.Deduction, false, false),
            new PayComponent("SACCO", "SACCO contribution", PayComponentKind.Deduction, false, false),
            new PayComponent("UNION", "Union dues", PayComponentKind.Deduction, false, false));
        await db.SaveChangesAsync(ct);
    }
}

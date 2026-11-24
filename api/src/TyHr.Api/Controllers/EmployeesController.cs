using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Domain.Employees;

namespace TyHr.Api.Controllers;

/// <summary>
/// Employee records. Reading is for HR, payroll and line managers (their own team only, enforced by the service, which
/// also leaves out personal and pay details the user may not see). HR keeps the records; payroll and HR admins keep pay.
/// </summary>
[ApiController]
[Route("api/employees")]
[RequireTenant]
[Authorize(Policy = Permissions.EmployeesView)]
public class EmployeesController(EmployeeService employees) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<EmployeeListItem>> List([FromQuery] EmployeeQuery query, CancellationToken ct) => employees.ListAsync(query, ct);

    /// <summary>The employees the user may see, as Excel, with only the columns they may see.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] EmployeeQuery query, [FromServices] EmployeeSpreadsheets sheets, CancellationToken ct)
    {
        var file = await sheets.ExportAsync(query, ct);
        return File(file.Content, Spreadsheet.ContentType, file.FileName);
    }

    [HttpGet("import/template"), Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> ImportTemplate([FromServices] EmployeeSpreadsheets sheets, CancellationToken ct)
    {
        var file = await sheets.TemplateAsync(ct);
        return File(file.Content, Spreadsheet.ContentType, file.FileName);
    }

    /// <summary>Checks an import file (commit=false) or imports it (commit=true): all rows or, if any has a problem, none.</summary>
    [HttpPost("import"), Authorize(Policy = Permissions.EmployeesManage)]
    [RequestSizeLimit(EmployeeSpreadsheets.MaxBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = EmployeeSpreadsheets.MaxBytes + 64 * 1024)]
    public async Task<ImportResult> Import(IFormFile? file, [FromQuery] bool commit, [FromServices] EmployeeSpreadsheets sheets, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new Domain.DomainException("Choose the filled-in import template to upload.");
        await using var content = file.OpenReadStream();
        var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, ct);
        buffered.Position = 0;
        return await sheets.ImportAsync(buffered, commit, ct);
    }

    [HttpGet("{id:guid}")]
    public Task<EmployeeDto> Get(Guid id, CancellationToken ct) => employees.GetAsync(id, ct);

    [HttpPost, Authorize(Policy = Permissions.EmployeesManage)]
    public Task<EmployeeDto> Create(CreateEmployeeRequest r, CancellationToken ct) => employees.CreateAsync(r, ct);

    [HttpPut("{id:guid}/personal"), Authorize(Policy = Permissions.EmployeesManage), Authorize(Policy = Permissions.EmployeesPersonal)]
    public Task<EmployeeDto> UpdatePersonal(Guid id, PersonalDetails r, CancellationToken ct) => employees.UpdatePersonalAsync(id, r, ct);

    [HttpPut("{id:guid}/employment"), Authorize(Policy = Permissions.EmployeesManage)]
    public Task<EmployeeDto> UpdateEmployment(Guid id, EmploymentDetails r, CancellationToken ct) => employees.UpdateEmploymentAsync(id, r, ct);

    [HttpPut("{id:guid}/payment"), Authorize(Policy = Permissions.EmployeesPay)]
    public Task<EmployeeDto> UpdatePayment(Guid id, PaymentDetails r, CancellationToken ct) => employees.UpdatePaymentAsync(id, r, ct);

    [HttpPost("{id:guid}/status"), Authorize(Policy = Permissions.EmployeesManage)]
    public Task<EmployeeDto> SetStatus(Guid id, SetStatusRequest r, CancellationToken ct) => employees.SetStatusAsync(id, r.Status, ct);

    [HttpPost("{id:guid}/contracts"), Authorize(Policy = Permissions.EmployeesManage)]
    public Task<EmployeeDto> AddContract(Guid id, SaveContractRequest r, CancellationToken ct) => employees.AddContractAsync(id, r, ct);

    [HttpPut("{id:guid}/contracts/{contractId:guid}"), Authorize(Policy = Permissions.EmployeesManage)]
    public Task<EmployeeDto> UpdateContract(Guid id, Guid contractId, SaveContractRequest r, CancellationToken ct) =>
        employees.UpdateContractAsync(id, contractId, r, ct);

    [HttpPost("{id:guid}/contracts/{contractId:guid}/confirm"), Authorize(Policy = Permissions.EmployeesManage)]
    public Task<EmployeeDto> ConfirmContract(Guid id, Guid contractId, ConfirmContractRequest r, CancellationToken ct) =>
        employees.ConfirmContractAsync(id, contractId, r.ConfirmedOn, ct);

    [HttpPost("{id:guid}/salaries"), Authorize(Policy = Permissions.EmployeesPay)]
    public Task<EmployeeDto> AddSalary(Guid id, AddSalaryRequest r, CancellationToken ct) => employees.AddSalaryAsync(id, r, ct);

    [HttpDelete("{id:guid}/salaries/{salaryId:guid}"), Authorize(Policy = Permissions.EmployeesPay)]
    public Task<EmployeeDto> DeleteSalary(Guid id, Guid salaryId, CancellationToken ct) => employees.DeleteSalaryAsync(id, salaryId, ct);

    [HttpPost("{id:guid}/next-of-kin"), Authorize(Policy = Permissions.EmployeesManage), Authorize(Policy = Permissions.EmployeesPersonal)]
    public Task<EmployeeDto> AddNextOfKin(Guid id, SaveNextOfKinRequest r, CancellationToken ct) => employees.AddNextOfKinAsync(id, r, ct);

    [HttpPut("{id:guid}/next-of-kin/{kinId:guid}"), Authorize(Policy = Permissions.EmployeesManage), Authorize(Policy = Permissions.EmployeesPersonal)]
    public Task<EmployeeDto> UpdateNextOfKin(Guid id, Guid kinId, SaveNextOfKinRequest r, CancellationToken ct) =>
        employees.UpdateNextOfKinAsync(id, kinId, r, ct);

    [HttpDelete("{id:guid}/next-of-kin/{kinId:guid}"), Authorize(Policy = Permissions.EmployeesManage), Authorize(Policy = Permissions.EmployeesPersonal)]
    public Task<EmployeeDto> DeleteNextOfKin(Guid id, Guid kinId, CancellationToken ct) => employees.DeleteNextOfKinAsync(id, kinId, ct);
}

/// <summary>The staff directory: any colleague's name, job and work contacts.</summary>
[ApiController]
[Route("api/directory")]
[RequireTenant]
[Authorize(Policy = Permissions.DirectoryView)]
public class DirectoryController(EmployeeService employees) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<DirectoryEntry>> Search(string? search = null, Guid? departmentId = null, int page = 1, int pageSize = 50,
        CancellationToken ct = default) => employees.DirectoryAsync(search, departmentId, page, pageSize, ct);
}

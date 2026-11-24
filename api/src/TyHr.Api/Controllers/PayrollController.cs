using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Payroll;
using TyHr.Domain.Employees;

namespace TyHr.Api.Controllers;

/// <summary>Pay set-up: components, employees' pay items and tax profiles.</summary>
[ApiController]
[Route("api/payroll")]
[RequireTenant]
[Authorize(Policy = Permissions.PayrollSetup)]
public class PayrollSetupController(PayrollSetupService setup) : ControllerBase
{
    [HttpGet("settings"), Authorize(Policy = Permissions.PayrollView)]
    public Task<PayrollSettingsDto> Settings(CancellationToken ct) => setup.SettingsAsync(ct);

    [HttpGet("components"), Authorize(Policy = Permissions.PayrollView)]
    public Task<IReadOnlyList<PayComponentDto>> Components(CancellationToken ct) => setup.ComponentsAsync(ct);

    [HttpPost("components")]
    public Task<PayComponentDto> CreateComponent(SavePayComponentRequest r, CancellationToken ct) => setup.CreateComponentAsync(r, ct);

    [HttpPut("components/{id:guid}")]
    public Task<PayComponentDto> UpdateComponent(Guid id, SavePayComponentRequest r, CancellationToken ct) => setup.UpdateComponentAsync(id, r, ct);

    [HttpPost("components/{id:guid}/activate")]
    public Task<PayComponentDto> Activate(Guid id, CancellationToken ct) => setup.SetComponentActiveAsync(id, true, ct);

    [HttpPost("components/{id:guid}/deactivate")]
    public Task<PayComponentDto> Deactivate(Guid id, CancellationToken ct) => setup.SetComponentActiveAsync(id, false, ct);

    [HttpGet("employees/{employeeId:guid}")]
    public Task<EmployeePayDto> EmployeePay(Guid employeeId, CancellationToken ct) => setup.EmployeePayAsync(employeeId, ct);

    [HttpPost("employees/{employeeId:guid}/items")]
    public Task<EmployeePayDto> AddItem(Guid employeeId, SavePayItemRequest r, CancellationToken ct) => setup.AddItemAsync(employeeId, r, ct);

    [HttpPut("employees/{employeeId:guid}/items/{itemId:guid}")]
    public Task<EmployeePayDto> UpdateItem(Guid employeeId, Guid itemId, SavePayItemRequest r, CancellationToken ct) => setup.UpdateItemAsync(employeeId, itemId, r, ct);

    [HttpDelete("employees/{employeeId:guid}/items/{itemId:guid}")]
    public Task<EmployeePayDto> DeleteItem(Guid employeeId, Guid itemId, CancellationToken ct) => setup.DeleteItemAsync(employeeId, itemId, ct);

    [HttpPut("employees/{employeeId:guid}/tax-profile")]
    public Task<EmployeePayDto> SaveTaxProfile(Guid employeeId, SaveTaxProfileRequest r, CancellationToken ct) => setup.SaveTaxProfileAsync(employeeId, r, ct);
}

/// <summary>Payroll runs and payslips. Preparing and approving are separate permissions, and separate people.</summary>
[ApiController]
[Route("api/payroll/runs")]
[RequireTenant]
[Authorize(Policy = Permissions.PayrollView)]
public class PayrollRunsController(PayrollService payroll) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PayrollRunDto>> List([FromQuery] int? year, CancellationToken ct) => payroll.ListRunsAsync(year, ct);

    [HttpGet("{id:guid}")]
    public Task<PayrollRunDto> Get(Guid id, CancellationToken ct) => payroll.GetRunAsync(id, ct);

    [HttpPost, Authorize(Policy = Permissions.PayrollPrepare)]
    public Task<PayrollRunDto> Create(CreatePayrollRunRequest r, CancellationToken ct) => payroll.CreateRunAsync(r, ct);

    [HttpPut("{id:guid}"), Authorize(Policy = Permissions.PayrollPrepare)]
    public Task<PayrollRunDto> Update(Guid id, UpdatePayrollRunRequest r, CancellationToken ct) => payroll.UpdateRunAsync(id, r, ct);

    [HttpDelete("{id:guid}"), Authorize(Policy = Permissions.PayrollPrepare)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await payroll.DeleteRunAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/inputs")]
    public Task<IReadOnlyList<PayrollInputDto>> Inputs(Guid id, CancellationToken ct) => payroll.InputsAsync(id, ct);

    [HttpPost("{id:guid}/inputs"), Authorize(Policy = Permissions.PayrollPrepare)]
    public Task<IReadOnlyList<PayrollInputDto>> AddInput(Guid id, AddPayrollInputRequest r, CancellationToken ct) => payroll.AddInputAsync(id, r, ct);

    [HttpDelete("{id:guid}/inputs/{inputId:guid}"), Authorize(Policy = Permissions.PayrollPrepare)]
    public Task<IReadOnlyList<PayrollInputDto>> RemoveInput(Guid id, Guid inputId, CancellationToken ct) => payroll.RemoveInputAsync(id, inputId, ct);

    [HttpPost("{id:guid}/calculate"), Authorize(Policy = Permissions.PayrollPrepare)]
    public Task<PayrollRunDto> Calculate(Guid id, CancellationToken ct) => payroll.CalculateAsync(id, ct);

    [HttpPost("{id:guid}/submit"), Authorize(Policy = Permissions.PayrollPrepare)]
    public Task<PayrollRunDto> Submit(Guid id, CancellationToken ct) => payroll.SubmitAsync(id, ct);

    [HttpPost("{id:guid}/approve"), Authorize(Policy = Permissions.PayrollApprove)]
    public Task<PayrollRunDto> Approve(Guid id, CancellationToken ct) => payroll.ApproveAsync(id, ct);

    [HttpPost("{id:guid}/send-back"), Authorize(Policy = Permissions.PayrollApprove)]
    public Task<PayrollRunDto> SendBack(Guid id, SendBackRequest r, CancellationToken ct) => payroll.SendBackAsync(id, r.Note, ct);

    [HttpPost("{id:guid}/lock"), Authorize(Policy = Permissions.PayrollPrepare)]
    public Task<PayrollRunDto> Lock(Guid id, CancellationToken ct) => payroll.LockAsync(id, ct);

    [HttpPost("{id:guid}/reverse"), Authorize(Policy = Permissions.PayrollPrepare)]
    public Task<PayrollRunDto> Reverse(Guid id, CancellationToken ct) => payroll.ReverseAsync(id, ct);

    [HttpGet("{id:guid}/payslips")]
    public Task<IReadOnlyList<PayslipSummaryDto>> Payslips(Guid id, CancellationToken ct) => payroll.PayslipsAsync(id, ct);

    [HttpGet("payslips/{payslipId:guid}")]
    public Task<PayslipDto> Payslip(Guid payslipId, CancellationToken ct) => payroll.PayslipAsync(payslipId, ct);

    [HttpGet("{id:guid}/variance")]
    public Task<PayrollVarianceDto> Variance(Guid id, CancellationToken ct) => payroll.VarianceAsync(id, ct);
}

/// <summary>What a payroll produces: payslip PDFs and emails, payment files, statutory returns and the journal.</summary>
[ApiController]
[Route("api/payroll")]
[RequireTenant]
[Authorize(Policy = Permissions.PayrollView)]
public class PayrollOutputsController(PayrollOutputService outputs) : ControllerBase
{
    [HttpGet("runs/payslips/{payslipId:guid}/pdf")]
    public async Task<IActionResult> PayslipPdf(Guid payslipId, CancellationToken ct) => Send(await outputs.PayslipPdfAsync(payslipId, ct));

    [HttpPost("runs/{id:guid}/email-payslips"), Authorize(Policy = Permissions.PayrollPrepare)]
    public Task<EmailPayslipsResult> EmailPayslips(Guid id, CancellationToken ct) => outputs.EmailPayslipsAsync(id, ct);

    /// <summary>Bank or mobile-money bulk payment CSV. Account numbers in clear, so only for those who prepare payroll.</summary>
    [HttpGet("runs/{id:guid}/payment-file"), Authorize(Policy = Permissions.PayrollPrepare)]
    public async Task<IActionResult> PaymentFile(Guid id, [FromQuery] PaymentMethod method, CancellationToken ct) => Send(await outputs.PaymentFileAsync(id, method, ct));

    [HttpGet("runs/{id:guid}/journal")]
    public async Task<IActionResult> Journal(Guid id, CancellationToken ct) => Send(await outputs.JournalAsync(id, ct));

    [HttpGet("returns")]
    public async Task<IActionResult> Returns([FromQuery, BindRequired] int year, [FromQuery, BindRequired] int month, CancellationToken ct) => Send(await outputs.ReturnsAsync(year, month, ct));

    private FileContentResult Send(OutputFile f) => File(f.Content, f.ContentType, f.FileName);
}

/// <summary>Employees' own payslips, once their payroll is locked.</summary>
[ApiController]
[Route("api/payroll/my-payslips")]
[RequireTenant]
[Authorize(Policy = Permissions.PayslipsOwn)]
public class MyPayslipsController(PayrollOutputService outputs) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<MyPayslipDto>> List(CancellationToken ct) => outputs.MyPayslipsAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<PayslipDto> Get(Guid id, CancellationToken ct) => outputs.MyPayslipAsync(id, ct);

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var f = await outputs.MyPayslipPdfAsync(id, ct);
        return File(f.Content, f.ContentType, f.FileName);
    }
}

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Employees;
using TyHr.Domain.SelfService;

namespace TyHr.Application.SelfService;

/// <summary>
/// Employees ask to change their own personal details, payment details or next of kin; HR decides (payroll or HR admins
/// for payment details, since a changed account number is how payroll fraud usually starts). Nobody decides on their
/// own record. Approving applies only the fields the employee changed.
/// </summary>
public class ProfileChangeService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, EmployeeAccess access, IEmailSender email,
    TimeProvider clock, ILogger<ProfileChangeService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // Asking

    public async Task<IReadOnlyList<ProfileChangeDto>> MineAsync(CancellationToken ct)
    {
        var me = await MeAsync(ct);
        var requests = await db.ProfileChangeRequests.AsNoTracking().Where(r => r.EmployeeId == me).OrderByDescending(r => r.RequestedAt).Take(50).ToListAsync(ct);
        return await ToDtosAsync(requests, ct);
    }

    public async Task<ProfileChangeDto> RequestPersonalAsync(RequestPersonalChange r, CancellationToken ct)
    {
        var (me, country) = (await MeAsync(ct), await db.CompanyCountryAsync(tenantContext, ct));
        var before = Personal(await EmployeeAsync(me, ct));
        var proposed = await EmployeeAsync(me, ct); // not tracked: changed only to check and tidy the values
        proposed.UpdatePersonal(r.Details, country);
        return await SaveAsync(me, ProfileChangeKind.Personal, null, before, Personal(proposed), r.Reason, ct);
    }

    public async Task<ProfileChangeDto> RequestPaymentAsync(RequestPaymentChange r, CancellationToken ct)
    {
        var (me, country) = (await MeAsync(ct), await db.CompanyCountryAsync(tenantContext, ct));
        var before = Payment(await EmployeeAsync(me, ct));
        var proposed = await EmployeeAsync(me, ct);
        proposed.UpdatePayment(r.Details, country);
        return await SaveAsync(me, ProfileChangeKind.Payment, null, before, Payment(proposed), r.Reason, ct);
    }

    public async Task<ProfileChangeDto> RequestNextOfKinAsync(RequestNextOfKinChange r, CancellationToken ct)
    {
        var (me, country) = (await MeAsync(ct), await db.CompanyCountryAsync(tenantContext, ct));
        SaveNextOfKinRequest before = new("", "", "", null, null);
        if (r.NextOfKinId is { } kinId)
        {
            var kin = await db.NextOfKin.AsNoTracking().SingleOrDefaultAsync(k => k.Id == kinId && k.EmployeeId == me, ct)
                      ?? throw new NotFoundException("There is no such next of kin.");
            before = Kin(kin);
        }
        var d = r.Details;
        var proposed = new NextOfKin(me, d.Name, d.Relationship, d.Phone, d.Email, d.Address, country);
        return await SaveAsync(me, ProfileChangeKind.NextOfKin, r.NextOfKinId, before, Kin(proposed), r.Reason, ct);
    }

    public async Task<ProfileChangeDto> WithdrawAsync(Guid id, CancellationToken ct)
    {
        var me = await MeAsync(ct);
        var request = await db.ProfileChangeRequests.SingleOrDefaultAsync(r => r.Id == id && r.EmployeeId == me, ct)
                      ?? throw new NotFoundException("There is no such request.");
        request.Withdraw(user.UserName, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([request], ct))[0];
    }

    private async Task<ProfileChangeDto> SaveAsync<T>(Guid me, ProfileChangeKind kind, Guid? kinId, T before, T proposed, string? reason, CancellationToken ct)
    {
        var beforeJson = JsonSerializer.Serialize(before, Json);
        var proposedJson = JsonSerializer.Serialize(proposed, Json);
        if (beforeJson == proposedJson) throw new DomainException("Nothing has changed.");
        if (await db.ProfileChangeRequests.AnyAsync(r => r.EmployeeId == me && r.Kind == kind && r.NextOfKinId == kinId
                                                         && r.Status == ProfileChangeStatus.Pending, ct))
            throw new DomainException("You already have a change to these details waiting for HR. Withdraw it first to ask for a different one.");
        var request = new ProfileChangeRequest(me, kind, kinId, beforeJson, proposedJson, reason, user.UserName, clock.GetUtcNow());
        db.ProfileChangeRequests.Add(request);
        await db.SaveChangesAsync(ct);
        var dto = (await ToDtosAsync([request], ct))[0];
        var hr = await db.CompanyProfiles.AsNoTracking().Select(p => p.Email).SingleAsync(ct);
        await NotifyAsync([hr], $"{dto.EmployeeName} asks to change their {Topic(dto)}",
            $"{dto.EmployeeName} ({dto.EmployeeNumber}) asks to change their {Topic(dto)}." + (dto.Reason != null ? $"\nReason: {dto.Reason}" : "")
            + "\n\nReview it in TyHR under Approvals.", ct);
        return dto;
    }

    // Deciding

    /// <summary>Requests the user may decide (pending by default), oldest first.</summary>
    public async Task<IReadOnlyList<ProfileChangeDto>> ListAsync(ProfileChangeStatus? status, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var kinds = DecidableKinds();
        var me = await access.MeAsync(ct);
        var s = status ?? ProfileChangeStatus.Pending;
        var query = db.ProfileChangeRequests.AsNoTracking().Where(r => kinds.Contains(r.Kind) && r.Status == s && r.EmployeeId != me);
        var requests = s == ProfileChangeStatus.Pending
            ? await query.OrderBy(r => r.RequestedAt).ToListAsync(ct)
            : await query.OrderByDescending(r => r.DecidedAt).Take(100).ToListAsync(ct);
        return await ToDtosAsync(requests, ct);
    }

    public async Task<int> PendingCountAsync(CancellationToken ct)
    {
        var kinds = DecidableKinds();
        if (kinds.Count == 0) return 0;
        var me = await access.MeAsync(ct);
        return await db.ProfileChangeRequests.CountAsync(r => kinds.Contains(r.Kind) && r.Status == ProfileChangeStatus.Pending && r.EmployeeId != me, ct);
    }

    public async Task<ProfileChangeDto> ApproveAsync(Guid id, string? note, CancellationToken ct)
    {
        var request = await DecidableAsync(id, ct);
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var employee = await db.Employees.SingleAsync(e => e.Id == request.EmployeeId, ct);
        var oldAddresses = new[] { employee.WorkEmail, employee.PersonalEmail };
        switch (request.Kind)
        {
            case ProfileChangeKind.Personal:
                employee.UpdatePersonal(Merge(request, Personal(employee)), country);
                break;
            case ProfileChangeKind.Payment:
                employee.UpdatePayment(Merge(request, Payment(employee)), country);
                break;
            case ProfileChangeKind.NextOfKin:
                if (request.NextOfKinId is { } kinId)
                {
                    var kin = await db.NextOfKin.SingleOrDefaultAsync(k => k.Id == kinId, ct)
                              ?? throw new DomainException("That next of kin has since been removed from the record. Decline this request and ask the employee to add them again.");
                    var k = Merge(request, Kin(kin));
                    kin.Update(k.Name, k.Relationship, k.Phone, k.Email, k.Address, country);
                }
                else
                {
                    var k = JsonSerializer.Deserialize<SaveNextOfKinRequest>(request.Proposed, Json)!;
                    db.NextOfKin.Add(new NextOfKin(employee.Id, k.Name, k.Relationship, k.Phone, k.Email, k.Address, country));
                }
                break;
        }
        request.Approve(user.UserName, clock.GetUtcNow(), note);
        await db.SaveChangesAsync(ct);
        var dto = (await ToDtosAsync([request], ct))[0];

        // A payment change also goes to the addresses on file before it, so someone who took over an account can't hide it.
        var to = request.Kind == ProfileChangeKind.Payment
            ? oldAddresses.Concat([employee.WorkEmail, employee.PersonalEmail])
            : [employee.WorkEmail ?? employee.PersonalEmail];
        await NotifyAsync(to, $"Your {Topic(dto)} change is approved",
            $"Your requested change to your {Topic(dto)} was approved by {user.UserName} and is now on your record."
            + (dto.DecisionNote != null ? $"\nNote: {dto.DecisionNote}" : "")
            + (request.Kind == ProfileChangeKind.Payment ? "\n\nIf you didn't ask for this, tell HR straight away." : ""), ct);
        return dto;
    }

    public async Task<ProfileChangeDto> RejectAsync(Guid id, string? note, CancellationToken ct)
    {
        var request = await DecidableAsync(id, ct);
        request.Reject(user.UserName, clock.GetUtcNow(), note);
        await db.SaveChangesAsync(ct);
        var dto = (await ToDtosAsync([request], ct))[0];
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == request.EmployeeId, ct);
        await NotifyAsync([employee.WorkEmail ?? employee.PersonalEmail], $"Your {Topic(dto)} change was declined",
            $"Your requested change to your {Topic(dto)} was declined by {user.UserName}.\nReason: {dto.DecisionNote}", ct);
        return dto;
    }

    public async Task<ProfileChangeDto> GetAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var request = await db.ProfileChangeRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
        var me = await access.MeAsync(ct);
        if (request == null || (request.EmployeeId != me && !DecidableKinds().Contains(request.Kind)))
            throw new NotFoundException("There is no such request.");
        return (await ToDtosAsync([request], ct))[0];
    }

    private async Task<ProfileChangeRequest> DecidableAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var request = await db.ProfileChangeRequests.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (request == null || !DecidableKinds().Contains(request.Kind)) throw new NotFoundException("There is no such request.");
        if (request.EmployeeId == await access.MeAsync(ct)) throw new DomainException("You can't decide a change to your own record; another HR user has to.");
        return request;
    }

    private List<ProfileChangeKind> DecidableKinds()
    {
        var kinds = new List<ProfileChangeKind>();
        if (user.Can(Permissions.EmployeesManage)) kinds.AddRange([ProfileChangeKind.Personal, ProfileChangeKind.NextOfKin]);
        if (user.Can(Permissions.EmployeesPay)) kinds.Add(ProfileChangeKind.Payment);
        return kinds;
    }

    // Snapshots and merging

    private static PersonalDetails Personal(Employee e) => new(e.FirstName, e.MiddleName, e.LastName, e.Gender, e.DateOfBirth, e.MaritalStatus,
        e.Nationality, e.IdType, e.IdNumber, e.TaxNumber, e.SocialSecurityNumber, e.PersonalEmail, e.PersonalPhone, e.HomeAddress);

    private static PaymentDetails Payment(Employee e) => new(e.PaymentMethod, e.BankName, e.BankBranch, e.BankAccountNumber, e.BankAccountName,
        e.MobileMoneyProvider, e.MobileMoneyNumber);

    private static SaveNextOfKinRequest Kin(NextOfKin k) => new(k.Name, k.Relationship, k.Phone, k.Email, k.Address);

    /// <summary>The current values with the fields the employee changed replaced by what they asked for.</summary>
    private static T Merge<T>(ProfileChangeRequest r, T current)
    {
        var before = JsonNode.Parse(r.Before)!.AsObject();
        var proposed = JsonNode.Parse(r.Proposed)!.AsObject();
        var result = JsonSerializer.SerializeToNode(current, Json)!.AsObject();
        foreach (var (name, value) in proposed)
            if (!JsonNode.DeepEquals(value, before[name])) result[name] = value?.DeepClone();
        return result.Deserialize<T>(Json)!;
    }

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["firstName"] = "First name", ["middleName"] = "Middle name", ["lastName"] = "Last name", ["gender"] = "Gender",
        ["dateOfBirth"] = "Date of birth", ["maritalStatus"] = "Marital status", ["nationality"] = "Nationality", ["idType"] = "ID type",
        ["idNumber"] = "ID number", ["personalEmail"] = "Personal email", ["personalPhone"] = "Personal phone", ["homeAddress"] = "Home address",
        ["method"] = "Paid by", ["bankName"] = "Bank", ["bankBranch"] = "Branch", ["bankAccountNumber"] = "Account number",
        ["bankAccountName"] = "Account name", ["mobileMoneyProvider"] = "Mobile money provider", ["mobileMoneyNumber"] = "Mobile money number",
        ["name"] = "Name", ["relationship"] = "Relationship", ["phone"] = "Phone", ["email"] = "Email", ["address"] = "Address",
    };

    private async Task<IReadOnlyList<ProfileChangeDto>> ToDtosAsync(IReadOnlyList<ProfileChangeRequest> requests, CancellationToken ct)
    {
        if (requests.Count == 0) return [];
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var ids = requests.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var kinIds = requests.Where(r => r.NextOfKinId != null).Select(r => r.NextOfKinId!.Value).ToList();
        var kin = await db.NextOfKin.AsNoTracking().Where(k => kinIds.Contains(k.Id)).ToDictionaryAsync(k => k.Id, ct);
        var me = await access.MeAsync(ct);
        var kinds = DecidableKinds();
        return requests.Select(r =>
        {
            var e = employees[r.EmployeeId];
            JsonObject? current = r.Kind switch
            {
                ProfileChangeKind.Personal => JsonSerializer.SerializeToNode(Personal(e), Json)!.AsObject(),
                ProfileChangeKind.Payment => JsonSerializer.SerializeToNode(Payment(e), Json)!.AsObject(),
                _ => r.NextOfKinId is { } k && kin.TryGetValue(k, out var nk) ? JsonSerializer.SerializeToNode(Kin(nk), Json)!.AsObject() : null,
            };
            var before = JsonNode.Parse(r.Before)!.AsObject();
            var proposed = JsonNode.Parse(r.Proposed)!.AsObject();
            var changes = proposed.Where(p => !JsonNode.DeepEquals(p.Value, before[p.Key]))
                .Select(p => new ChangeField(Label(p.Key, country), Text(before[p.Key]), Text(p.Value), Text(current?[p.Key])))
                .ToList();
            var what = r.Kind switch
            {
                ProfileChangeKind.Personal => "Personal details",
                ProfileChangeKind.Payment => "Payment details",
                _ => r.NextOfKinId == null ? "Next of kin (new)" : "Next of kin",
            };
            var pending = r.Status == ProfileChangeStatus.Pending;
            return new ProfileChangeDto(r.Id, e.Id, e.EmployeeNumber, e.FullName, r.Kind, what, r.Status, r.Reason, r.RequestedBy, r.RequestedAt,
                r.DecidedBy, r.DecidedAt, r.DecisionNote, changes, pending && kinds.Contains(r.Kind) && r.EmployeeId != me, pending && r.EmployeeId == me);
        }).ToList();

        static string? Text(JsonNode? n) => n switch
        {
            null => null,
            JsonValue v when v.TryGetValue<string>(out var s) => string.IsNullOrEmpty(s) ? null : s,
            _ => n.ToJsonString(),
        };
    }

    /// <summary>"personal details", "payment details" or "next of kin", for email wording.</summary>
    private static string Topic(ProfileChangeDto dto) => dto.Kind == ProfileChangeKind.NextOfKin ? "next of kin" : dto.What.ToLowerInvariant();

    private static string Label(string field, CountryPack country) => field switch
    {
        "taxNumber" => country.TaxNumberLabel,
        "socialSecurityNumber" => country.SocialSecurityLabel,
        _ => Labels.GetValueOrDefault(field, field),
    };

    // Helpers

    private async Task<Guid> MeAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        return await access.MeAsync(ct) ?? throw new DomainException(EmployeeService.NotLinked);
    }

    private async Task<Employee> EmployeeAsync(Guid id, CancellationToken ct) => await db.Employees.AsNoTracking().SingleAsync(e => e.Id == id, ct);

    /// <summary>Best effort: the change is already saved, so a mail failure is logged, not shown.</summary>
    private async Task NotifyAsync(IEnumerable<string?> to, string subject, string text, CancellationToken ct)
    {
        var addresses = to.OfType<string>().Distinct().ToList();
        if (addresses.Count == 0) return;
        try
        {
            var html = string.Join("", text.Split('\n').Select(line => $"<p>{WebUtility.HtmlEncode(line)}</p>"));
            await email.SendAsync(new EmailMessage(addresses, subject, html, text), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Could not send the profile change email \"{Subject}\".", subject);
        }
    }
}

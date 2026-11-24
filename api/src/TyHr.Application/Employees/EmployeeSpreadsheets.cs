using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Employees;

namespace TyHr.Application.Employees;

public record ImportRowError(int Row, string Message);

/// <summary>
/// <see cref="Committed"/> is true only when every row was valid and the employees were created; otherwise nothing
/// was saved and <see cref="Errors"/> says what to fix, by spreadsheet row.
/// </summary>
public record ImportResult(int Rows, int Created, bool Committed, IReadOnlyList<ImportRowError> Errors, IReadOnlyList<string> Numbers);

public record Spreadsheet(byte[] Content, string FileName)
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

/// <summary>
/// Employees to and from Excel. The import template and the export share column headings, so an export's layout is
/// familiar, but importing only ever adds new employees. An import is checked in full first; it saves all of its rows
/// in one transaction or none, so a half-imported file never has to be untangled.
/// </summary>
public class EmployeeSpreadsheets(IAppDbContext db, ITenantContext tenantContext, EmployeeService employees, EmployeeAccess access,
    TimeProvider clock)
{
    public const int MaxRows = 2000;
    public const long MaxBytes = 5 * 1024 * 1024;
    private const string Sheet = "Employees";

    // Headings (a trailing * marks required columns in the template; it is ignored when reading).
    private const string FirstName = "First name", MiddleName = "Middle name", LastName = "Last name", GenderCol = "Gender",
        DateOfBirth = "Date of birth", Marital = "Marital status", Nationality = "Nationality", IdTypeCol = "ID type", IdNumber = "ID number",
        TaxNumber = "Tax number", SocialSecurity = "Social security number", PersonalEmail = "Personal email", PersonalPhone = "Personal phone",
        HomeAddress = "Home address", HireDate = "Hire date", WorkEmail = "Work email", WorkPhone = "Work phone", Branch = "Branch",
        Department = "Department", Position = "Position", Manager = "Manager", ContractTypeCol = "Contract type", ContractStart = "Contract start",
        ContractEnd = "Contract end", ProbationEnd = "Probation end", Salary = "Monthly basic salary", SalaryFrom = "Salary effective from";

    private static readonly string[] Required = [FirstName, LastName, GenderCol, HireDate];
    private static readonly string[] PersonalColumns = [DateOfBirth, Marital, Nationality, IdTypeCol, IdNumber, TaxNumber, SocialSecurity,
        PersonalEmail, PersonalPhone, HomeAddress];
    private static readonly string[] ImportColumns = [FirstName, MiddleName, LastName, GenderCol, .. PersonalColumns, HireDate, WorkEmail,
        WorkPhone, Branch, Department, Position, Manager, ContractTypeCol, ContractStart, ContractEnd, ProbationEnd, Salary, SalaryFrom];

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "d/M/yyyy", "d-M-yyyy", "d.M.yyyy", "d MMM yyyy", "d-MMM-yyyy", "d MMMM yyyy", "d-MMM-yy"];

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    // Template

    public async Task<Spreadsheet> TemplateAsync(CancellationToken ct)
    {
        var tenant = tenantContext.RequireTenant();
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var lookups = await Lookups.LoadAsync(db, ct);
        var columns = access.CanSeePay ? ImportColumns : ImportColumns.Where(c => c is not (Salary or SalaryFrom)).ToArray();

        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet(Sheet);
        WriteHeader(sheet, columns, country, template: true);
        var lists = book.AddWorksheet("Lists");
        var listColumn = 0;
        void Dropdown(string column, IEnumerable<string> values)
        {
            var index = Array.IndexOf(columns, column);
            var items = values.ToList();
            if (index < 0 || items.Count == 0) return;
            listColumn++;
            lists.Cell(1, listColumn).Value = column;
            for (var i = 0; i < items.Count; i++) lists.Cell(i + 2, listColumn).Value = items[i];
            var source = lists.Range(2, listColumn, items.Count + 1, listColumn);
            var validation = sheet.Range(2, index + 1, MaxRows + 1, index + 1).CreateDataValidation();
            validation.List(source, true);
            validation.IgnoreBlanks = true;
        }
        Dropdown(GenderCol, Enum.GetNames<Gender>());
        Dropdown(Marital, Enum.GetNames<MaritalStatus>());
        Dropdown(IdTypeCol, Enum.GetNames<IdType>());
        Dropdown(ContractTypeCol, Enum.GetNames<ContractType>());
        Dropdown(Branch, lookups.Branches.Select(b => b.Name).Order());
        Dropdown(Department, lookups.Departments.Select(d => d.Name).Order());
        Dropdown(Position, lookups.Positions.Select(p => p.Title).Distinct().Order());
        lists.Columns().AdjustToContents();
        lists.Hide();

        var help = book.AddWorksheet("Instructions");
        string[] lines =
        [
            "Importing employees into TyHR",
            "",
            "One employee per row on the Employees sheet, starting on row 2. Columns marked * are required.",
            "Dates: type them as dates, or as 2025-01-31 or 31/01/2025 (day first).",
            "Branch, Department and Position: pick from the dropdowns (they list what is set up under Settings > Organisation); a code works too.",
            "Manager: the manager's employee number (EMP-0001) or work email. A manager in this same file must be on a row above their team.",
            $"ID number: with ID type NationalId it must be a valid {country.Name} {country.Formats.NationalIdLabel}. Tax number is the {country.TaxNumberLabel}, social security the {country.SocialSecurityLabel}.",
            "Contract: fill Contract type and Contract start to add the first contract; Fixed term and Internship contracts need an end date.",
            access.CanSeePay ? $"Salary: the monthly basic in {country.CurrencyCode}; Salary effective from defaults to the hire date." : "Salaries are set by payroll on each record after the import.",
            "",
            "TyHR checks the whole file first. If any row has a problem nothing is imported, and you get a list of what to fix by row number.",
            $"At most {MaxRows} employees per file.",
        ];
        for (var i = 0; i < lines.Length; i++) help.Cell(i + 1, 1).Value = lines[i];
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Cell(1, 1).Style.Font.FontSize = 14;
        help.Column(1).Width = 120;
        help.SetTabActive(false);
        sheet.SetTabActive();

        return new Spreadsheet(Save(book), $"tyhr-employee-import-{tenant}.xlsx");
    }

    // Export

    public async Task<Spreadsheet> ExportAsync(EmployeeQuery q, CancellationToken ct)
    {
        var tenant = tenantContext.RequireTenant();
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var query = (await access.VisibleAsync(ct)).AsNoTracking();
        if (q.DepartmentId is { } departmentId) query = query.Where(e => e.DepartmentId == departmentId);
        if (q.BranchId is { } branchId) query = query.Where(e => e.BranchId == branchId);
        if (q.Status is { } status) query = query.Where(e => e.Status == status);
        var list = (await query.ToListAsync(ct)).OrderBy(e => e.EmployeeNumber, StringComparer.Ordinal).ToList();
        if (Guard.Optional(q.Search) is { } search)
        {
            var term = search.ToLowerInvariant();
            list = list.Where(e => e.FullName.ToLowerInvariant().Contains(term) || e.EmployeeNumber.ToLowerInvariant().Contains(term)
                                   || (e.WorkEmail?.Contains(term) ?? false)).ToList();
        }

        var lookups = await Lookups.LoadAsync(db, ct, includeInactive: true);
        var numbers = await db.Employees.AsNoTracking().Select(e => new { e.Id, e.EmployeeNumber }).ToDictionaryAsync(e => e.Id, e => e.EmployeeNumber, ct);
        var ids = list.Select(e => e.Id).ToList();
        var contracts = (await db.EmploymentContracts.AsNoTracking().Where(c => ids.Contains(c.EmployeeId)).ToListAsync(ct))
            .Where(c => c.StartDate <= Today).GroupBy(c => c.EmployeeId).ToDictionary(g => g.Key, g => g.MaxBy(c => c.StartDate)!);
        var salaries = access.CanSeePay
            ? (await db.SalaryChanges.AsNoTracking().Where(s => ids.Contains(s.EmployeeId) && s.EffectiveDate <= Today).ToListAsync(ct))
                .GroupBy(s => s.EmployeeId).ToDictionary(g => g.Key, g => g.MaxBy(s => s.EffectiveDate)!)
            : [];

        string[] columns =
        [
            "Employee number", "Status", FirstName, MiddleName, LastName, GenderCol, .. access.CanSeePersonal ? PersonalColumns : [],
            HireDate, WorkEmail, WorkPhone, Branch, Department, Position, "Job grade", Manager, ContractTypeCol, ContractStart, ContractEnd, ProbationEnd,
            .. access.CanSeePay ? [Salary, SalaryFrom, "Paid by", "Bank", "Bank branch", "Account number", "Account name", "Mobile money provider", "Mobile money number"] : Array.Empty<string>(),
        ];
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet(Sheet);
        WriteHeader(sheet, columns, country, template: false);
        var row = 2;
        foreach (var e in list)
        {
            var position = lookups.Positions.FirstOrDefault(p => p.Id == e.PositionId);
            contracts.TryGetValue(e.Id, out var contract);
            salaries.TryGetValue(e.Id, out var salary);
            var values = new Dictionary<string, object?>
            {
                ["Employee number"] = e.EmployeeNumber, ["Status"] = e.Status.ToString(), [FirstName] = e.FirstName, [MiddleName] = e.MiddleName,
                [LastName] = e.LastName, [GenderCol] = e.Gender.ToString(), [DateOfBirth] = e.DateOfBirth, [Marital] = e.MaritalStatus?.ToString(),
                [Nationality] = e.Nationality, [IdTypeCol] = e.IdType?.ToString(), [IdNumber] = e.IdNumber, [TaxNumber] = e.TaxNumber,
                [SocialSecurity] = e.SocialSecurityNumber, [PersonalEmail] = e.PersonalEmail, [PersonalPhone] = e.PersonalPhone, [HomeAddress] = e.HomeAddress,
                [HireDate] = e.HireDate, [WorkEmail] = e.WorkEmail, [WorkPhone] = e.WorkPhone,
                [Branch] = lookups.Branches.FirstOrDefault(b => b.Id == e.BranchId)?.Name,
                [Department] = lookups.Departments.FirstOrDefault(d => d.Id == e.DepartmentId)?.Name,
                [Position] = position?.Title, ["Job grade"] = position?.GradeCode,
                [Manager] = e.ManagerId is { } m ? numbers.GetValueOrDefault(m) : null,
                [ContractTypeCol] = contract?.Type.ToString(), [ContractStart] = contract?.StartDate, [ContractEnd] = contract?.EndDate,
                [ProbationEnd] = contract?.ProbationEndDate,
                [Salary] = salary?.MonthlyBasic, [SalaryFrom] = salary?.EffectiveDate, ["Paid by"] = e.PaymentMethod.ToString(),
                ["Bank"] = e.BankName, ["Bank branch"] = e.BankBranch, ["Account number"] = e.BankAccountNumber, ["Account name"] = e.BankAccountName,
                ["Mobile money provider"] = e.MobileMoneyProvider, ["Mobile money number"] = e.MobileMoneyNumber,
            };
            for (var c = 0; c < columns.Length; c++)
            {
                var cell = sheet.Cell(row, c + 1);
                switch (values.GetValueOrDefault(columns[c]))
                {
                    case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "d mmm yyyy"; break;
                    case decimal amount: cell.Value = amount; cell.Style.NumberFormat.Format = country.CurrencyMinorUnits == 0 ? "#,##0" : "#,##0.00"; break;
                    // Always text: IDs and account numbers keep their leading zeros, and nothing is read as a formula.
                    case string s: cell.SetValue(s); break;
                }
            }
            row++;
        }
        sheet.Columns().AdjustToContents(1, Math.Min(row, 200), 8, 40);
        return new Spreadsheet(Save(book), $"tyhr-employees-{tenant}-{Today:yyyy-MM-dd}.xlsx");
    }

    // Import

    public async Task<ImportResult> ImportAsync(Stream content, bool commit, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (!access.CanEdit) throw new DomainException("You can't add employees.");
        var country = await db.CompanyCountryAsync(tenantContext, ct);

        XLWorkbook book;
        try
        {
            book = new XLWorkbook(content);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new DomainException("That isn't an Excel workbook (.xlsx) TyHR can read. Start from the import template.");
        }
        using var workbook = book;
        var sheet = book.Worksheets.FirstOrDefault(w => w.Name.Equals(Sheet, StringComparison.OrdinalIgnoreCase)) ?? book.Worksheet(1);

        // Headings to columns, by name, so columns may be reordered or left out.
        var headings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in sheet.Row(1).CellsUsed())
        {
            var name = HeadingName(cell.GetString(), country);
            if (name != null) headings.TryAdd(name, cell.Address.ColumnNumber);
        }
        var missing = Required.Where(r => !headings.ContainsKey(r)).ToList();
        if (missing.Count > 0) throw new DomainException($"The sheet has no {string.Join(", ", missing)} column. Start from the import template.");

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        var rows = Enumerable.Range(2, Math.Max(0, lastRow - 1)).Where(r => !sheet.Row(r).IsEmpty()).ToList();
        if (rows.Count == 0) throw new DomainException("The sheet has no employees in it (they start on row 2).");
        if (rows.Count > MaxRows) throw new DomainException($"A file can have at most {MaxRows} employees; this one has {rows.Count}. Split it.");

        var lookups = await Lookups.LoadAsync(db, ct);
        var existing = await db.Employees.AsNoTracking().Where(e => e.Status != EmployeeStatus.Exited)
            .Select(e => new { e.Id, e.EmployeeNumber, e.WorkEmail }).ToListAsync(ct);
        var existingByNumber = existing.ToDictionary(e => e.EmployeeNumber, e => e.Id, StringComparer.OrdinalIgnoreCase);
        var existingByEmail = existing.Where(e => e.WorkEmail != null).ToDictionary(e => e.WorkEmail!, e => e.Id, StringComparer.OrdinalIgnoreCase);
        var takenEmails = (await db.Employees.AsNoTracking().Where(e => e.WorkEmail != null).Select(e => e.WorkEmail!).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var errors = new List<ImportRowError>();
        var parsed = new List<(int Row, CreateEmployeeRequest Request, string? ManagerEmailInFile)>();
        var fileEmails = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            var rowErrors = new List<string>();
            string? Text(string column) => headings.TryGetValue(column, out var c) ? Guard.Optional(sheet.Cell(r, c).GetFormattedString()) : null;
            DateOnly? Date(string column)
            {
                if (!headings.TryGetValue(column, out var c)) return null;
                var cell = sheet.Cell(r, c);
                if (cell.IsEmpty()) return null;
                if (cell.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(cell.GetDateTime());
                if (cell.DataType == XLDataType.Number && cell.GetDouble() is > 1 and < 80000) return DateOnly.FromDateTime(DateTime.FromOADate(cell.GetDouble()));
                var text = cell.GetString().Trim();
                if (DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
                rowErrors.Add($"{column} '{text}' isn't a date (use 31/01/2025 or 2025-01-31).");
                return null;
            }
            TEnum? Choice<TEnum>(string column) where TEnum : struct, Enum
            {
                var text = Text(column);
                if (text == null) return null;
                if (Enum.TryParse<TEnum>(text.Replace(" ", "").Replace("-", ""), true, out var value) && Enum.IsDefined(value)) return value;
                rowErrors.Add($"{column} '{text}' should be one of {string.Join(", ", Enum.GetNames<TEnum>())}.");
                return null;
            }
            Guid? Find<T>(string column, IReadOnlyList<T> items, Func<T, bool> match, Func<T, Guid> id, string what)
            {
                var text = Text(column);
                if (text == null) return null;
                var found = items.Where(match).ToList();
                if (found.Count == 1) return id(found[0]);
                rowErrors.Add(found.Count == 0 ? $"There is no active {what} '{text}'." : $"'{text}' matches more than one {what}; use its code.");
                return null;
            }

            var gender = Choice<Gender>(GenderCol);
            var hire = Date(HireDate);
            var branchText = Text(Branch);
            var branchId = Find(Branch, lookups.Branches, b => Same(b.Name, branchText) || Same(b.Code, branchText), b => b.Id, "branch");
            var departmentText = Text(Department);
            var departmentId = Find(Department, lookups.Departments, d => Same(d.Name, departmentText) || Same(d.Code, departmentText), d => d.Id, "department");
            var positionText = Text(Position);
            var positionId = Find(Position, lookups.Positions,
                p => Same(p.Title, positionText) && (departmentId == null || p.DepartmentId == departmentId), p => p.Id, "position" + (departmentId != null ? " in that department" : ""));
            if (departmentId == null && positionId is { } pid) departmentId = lookups.Positions.Single(p => p.Id == pid).DepartmentId;

            // Manager: an existing employee (number or work email), or someone on an earlier row of this file (work email).
            Guid? managerId = null;
            string? managerInFile = null;
            if (Text(Manager) is { } managerText)
            {
                if (existingByNumber.TryGetValue(managerText, out var byNumber) || existingByEmail.TryGetValue(managerText, out byNumber)) managerId = byNumber;
                else if (fileEmails.ContainsKey(managerText)) managerInFile = managerText;
                else if (rows.Any(other => other > r && headings.TryGetValue(WorkEmail, out var wc) && Same(sheet.Cell(other, wc).GetString().Trim(), managerText)))
                    rowErrors.Add($"The manager {managerText} is further down the file; put managers above the people who report to them.");
                else rowErrors.Add($"Manager '{managerText}' isn't a current employee's number or work email, or anyone above in this file.");
            }

            var contractType = Choice<ContractType>(ContractTypeCol);
            var contractStart = Date(ContractStart);
            var contractEnd = Date(ContractEnd);
            var probationEnd = Date(ProbationEnd);
            var salaryText = Text(Salary);
            decimal? salary = null;
            if (salaryText != null)
            {
                if (!access.CanSeePay) rowErrors.Add("You can't set salaries; leave Monthly basic salary empty for payroll to fill in.");
                else if (decimal.TryParse(salaryText.Replace(",", "").Replace(country.CurrencyCode, "", StringComparison.OrdinalIgnoreCase).Trim(),
                             NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) salary = amount;
                else rowErrors.Add($"Monthly basic salary '{salaryText}' isn't an amount.");
            }
            var salaryFrom = Date(SalaryFrom);

            if (Text(FirstName) == null) rowErrors.Add("First name is required.");
            if (Text(LastName) == null) rowErrors.Add("Last name is required.");
            if (Text(GenderCol) == null) rowErrors.Add("Gender is required (Female or Male).");
            if (hire == null && !rowErrors.Any(e => e.StartsWith(HireDate))) rowErrors.Add("Hire date is required.");
            if (contractType == null && contractStart != null) rowErrors.Add("Contract start is filled in but Contract type isn't.");

            // Work emails are checked on every row, so one pass lists all the duplicates.
            if (Text(WorkEmail)?.ToLowerInvariant() is { } workEmail)
            {
                if (takenEmails.Contains(workEmail)) rowErrors.Add($"Another employee already has the work email {workEmail}.");
                else if (fileEmails.TryGetValue(workEmail, out var firstRow)) rowErrors.Add($"Row {firstRow} has the same work email {workEmail}.");
                else fileEmails[workEmail] = r;
            }

            if (rowErrors.Count == 0)
            {
                var idNumber = Text(IdNumber);
                var personal = new PersonalDetails(Text(FirstName)!, Text(MiddleName), Text(LastName)!, gender!.Value, Date(DateOfBirth),
                    Choice<MaritalStatus>(Marital), Text(Nationality)?.ToUpperInvariant() ?? country.Code,
                    idNumber == null ? null : Choice<IdType>(IdTypeCol) ?? IdType.NationalId, idNumber, Text(TaxNumber), Text(SocialSecurity),
                    Text(PersonalEmail), Text(PersonalPhone), Text(HomeAddress));
                var employment = new EmploymentDetails(hire!.Value, Text(WorkEmail), Text(WorkPhone), branchId, departmentId, positionId, managerId);
                var contract = contractType is { } type
                    ? new SaveContractRequest(type, contractStart ?? hire.Value, contractEnd, probationEnd, null) : null;
                var salaryRequest = salary is { } basic ? new AddSalaryRequest(salaryFrom ?? hire.Value, basic, "Imported") : null;

                // The same rules the record itself enforces, checked without saving.
                try
                {
                    var probe = new Employee("IMPORT", personal, country, employment);
                    if (contract != null) _ = new EmploymentContract(probe.Id, contract.Type, contract.StartDate, contract.EndDate, contract.ProbationEndDate, null);
                    if (salaryRequest != null) _ = new SalaryChange(probe.Id, salaryRequest.EffectiveDate, salaryRequest.MonthlyBasic, country, "Imported", "import", clock.GetUtcNow());
                }
                catch (DomainException e)
                {
                    rowErrors.Add(e.Message);
                }
                if (rowErrors.Count == 0) parsed.Add((r, new CreateEmployeeRequest(personal, employment, contract, salaryRequest), managerInFile));
            }
            errors.AddRange(rowErrors.Select(m => new ImportRowError(r, m)));
        }

        if (errors.Count > 0 || !commit) return new ImportResult(rows.Count, 0, false, errors, []);

        // All valid: create them in one transaction, in file order so managers exist before their teams.
        var created = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var numbers = await db.InTransactionAsync(async () =>
        {
            var result = new List<string>();
            foreach (var (row, request, managerEmail) in parsed)
            {
                var r = managerEmail == null ? request : request with { Employment = request.Employment with { ManagerId = created[managerEmail] } };
                EmployeeDto employee;
                try
                {
                    employee = await employees.CreateAsync(r, ct);
                }
                catch (DomainException e)
                {
                    throw new DomainException($"Row {row}: {e.Message} Nothing was imported.");
                }
                if (employee.Employment.WorkEmail is { } email) created[email] = employee.Id;
                result.Add(employee.EmployeeNumber);
            }
            return result;
        }, ct);
        return new ImportResult(rows.Count, numbers.Count, true, [], numbers);
    }

    private static bool Same(string? a, string? b) => a != null && b != null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The column a heading stands for, accepting the country's own labels (TIN, NIN, NSSF number, KRA PIN...).</summary>
    private static string? HeadingName(string heading, CountryPack country)
    {
        // "Tax number (TIN) *" -> "Tax number": the template adds the country's label and a required marker.
        var h = heading.Replace("*", "").Trim();
        if (h.EndsWith(')') && h.LastIndexOf(" (", StringComparison.Ordinal) is > 0 and var open)
        {
            var inner = h[(open + 2)..^1];
            if (Same(inner, country.TaxNumberLabel) || Same(inner, country.SocialSecurityLabel) || Same(inner, country.CurrencyCode)) h = h[..open];
        }
        if (h.Length == 0) return null;
        if (Same(h, country.TaxNumberLabel)) return TaxNumber;
        if (Same(h, country.SocialSecurityLabel)) return SocialSecurity;
        if (Same(h, country.Formats.NationalIdLabel)) return IdNumber;
        return ImportColumns.FirstOrDefault(c => Same(c, h)) ?? h;
    }

    private void WriteHeader(IXLWorksheet sheet, string[] columns, CountryPack country, bool template)
    {
        for (var c = 0; c < columns.Length; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            var heading = columns[c] switch
            {
                TaxNumber => $"{TaxNumber} ({country.TaxNumberLabel})",
                SocialSecurity => $"{SocialSecurity} ({country.SocialSecurityLabel})",
                Salary => $"{Salary} ({country.CurrencyCode})",
                var name => name,
            };
            cell.Value = template && Required.Contains(columns[c]) ? heading + " *" : heading;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F0FE");
            if (template)
            {
                // Text columns stay text so leading zeros (phone, account and ID numbers) survive.
                if (columns[c] is IdNumber or TaxNumber or SocialSecurity or PersonalPhone or WorkPhone)
                    sheet.Range(2, c + 1, MaxRows + 1, c + 1).Style.NumberFormat.Format = "@";
                if (columns[c] is DateOfBirth or HireDate or ContractStart or ContractEnd or ProbationEnd or SalaryFrom)
                    sheet.Range(2, c + 1, MaxRows + 1, c + 1).Style.DateFormat.Format = "d mmm yyyy";
                sheet.Column(c + 1).Width = Math.Max(14, heading.Length + 4);
            }
        }
        sheet.SheetView.FreezeRows(1);
    }

    private static byte[] Save(XLWorkbook book)
    {
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Branches, departments and positions: active ones for matching names in a file and the template's dropdowns; all for exports.</summary>
    private sealed record Lookups(List<Lookups.Item> Branches, List<Lookups.Item> Departments, List<Lookups.PositionItem> Positions)
    {
        public sealed record Item(Guid Id, string Name, string? Code);
        public sealed record PositionItem(Guid Id, string Title, Guid DepartmentId, string? GradeCode);

        public static async Task<Lookups> LoadAsync(IAppDbContext db, CancellationToken ct, bool includeInactive = false) => new(
            await db.Branches.AsNoTracking().Where(b => includeInactive || b.IsActive).Select(b => new Item(b.Id, b.Name, b.Code)).ToListAsync(ct),
            await db.Departments.AsNoTracking().Where(d => includeInactive || d.IsActive).Select(d => new Item(d.Id, d.Name, d.Code)).ToListAsync(ct),
            await db.Positions.AsNoTracking().Where(p => includeInactive || p.IsActive).Select(p => new PositionItem(p.Id, p.Title, p.DepartmentId,
                db.JobGrades.Where(g => g.Id == p.JobGradeId).Select(g => g.Code).FirstOrDefault())).ToListAsync(ct));
    }
}

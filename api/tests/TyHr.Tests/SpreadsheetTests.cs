using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Application.Organization;
using TyHr.Domain;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

public class SpreadsheetTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private sealed record Services(Scope Scope, OrgService Org, EmployeeService Employees, EmployeeSpreadsheets Sheets) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }

    private Services As(string tenant, string[]? roles = null, string? email = null)
    {
        var scope = fx.NewScope(tenant, roles: roles ?? [Roles.HrAdmin], email: email);
        var access = new EmployeeAccess(scope.Db, scope.User);
        var employees = new EmployeeService(scope.Db, scope.TenantContext, new PostgresDocumentNumbers(scope.Db), access, scope.User, fx.Clock);
        return new Services(scope, new OrgService(scope.Db, scope.TenantContext), employees,
            new EmployeeSpreadsheets(scope.Db, scope.TenantContext, employees, access, fx.Clock));
    }

    private static readonly string[] Headings =
    [
        "First name *", "Last name *", "Gender *", "Hire date *", "Work email", "Department", "Position", "Manager",
        "ID type", "NIN", "Contract type", "Contract start", "Contract end", "Monthly basic salary (UGX)",
    ];

    /// <summary>An import file: the headings above, then one row per array (null = empty cell).</summary>
    private static MemoryStream Workbook(params object?[][] rows)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Employees");
        for (var c = 0; c < Headings.Length; c++) sheet.Cell(1, c + 1).Value = Headings[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                switch (rows[r][c])
                {
                    case DateTime d: sheet.Cell(r + 2, c + 1).Value = d; break;
                    case decimal m: sheet.Cell(r + 2, c + 1).Value = m; break;
                    case string s: sheet.Cell(r + 2, c + 1).SetValue(s); break;
                }
        var stream = new MemoryStream();
        book.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private async Task<string> CompanyWithOrgAsync()
    {
        var co = await fx.CreateTenantAsync();
        await using var s = As(co);
        var finance = await s.Org.CreateDepartmentAsync(new("Finance", "FIN", null, null), default);
        await s.Org.CreateDepartmentAsync(new("Sales", "SAL", null, null), default);
        await s.Org.CreatePositionAsync(new("Accountant", finance.Id, null), default);
        await s.Employees.CreateAsync(EmployeeTests.NewEmployee("Grace", "Boss", email: "grace@co.test"), default); // EMP-0001
        return co;
    }

    [Fact]
    public async Task A_valid_file_is_checked_without_saving_then_imported_whole_with_managers_contracts_and_salaries()
    {
        var co = await CompanyWithOrgAsync();
        var file = Workbook(
            ["Peter", "Okello", "Male", new DateTime(2025, 2, 3), "peter@co.test", "fin", "Accountant", "EMP-0001", "NationalId", "CM90012345ABCD", "Permanent", null, null, 2_500_000m],
            ["Joan", "Akello", "female", "17/03/2025", "joan@co.test", null, "Accountant", "peter@co.test", null, null, "Fixed term", null, "2026-03-16", "1,200,000"],
            ["Sam", "Waiswa", "Male", "2025-04-01", null, "Sales", null, "grace@co.test", null, null, null, null, null, null]);

        await using (var s = As(co))
        {
            var check = await s.Sheets.ImportAsync(file, commit: false, default);
            Assert.Empty(check.Errors);
            Assert.Equal((3, 0, false), (check.Rows, check.Created, check.Committed));
            Assert.Equal(1, await s.Scope.Db.Employees.CountAsync());
        }

        file.Position = 0;
        await using (var s = As(co))
        {
            var result = await s.Sheets.ImportAsync(file, commit: true, default);
            Assert.True(result.Committed);
            Assert.Equal(["EMP-0002", "EMP-0003", "EMP-0004"], result.Numbers);
        }

        await using (var s = As(co))
        {
            var all = await s.Scope.Db.Employees.AsNoTracking().ToDictionaryAsync(e => e.EmployeeNumber);
            Assert.Equal(all["EMP-0001"].Id, all["EMP-0002"].ManagerId);
            Assert.Equal(all["EMP-0002"].Id, all["EMP-0003"].ManagerId); // manager on an earlier row of the same file
            Assert.Equal("CM90012345ABCD", all["EMP-0002"].IdNumber);
            Assert.Equal(new DateOnly(2025, 3, 17), all["EMP-0003"].HireDate); // day-first text date
            Assert.NotNull(all["EMP-0003"].DepartmentId); // taken from the position
            var joan = await s.Employees.GetAsync(all["EMP-0003"].Id, default);
            Assert.Equal((Domain.Employees.ContractType.FixedTerm, new DateOnly(2025, 3, 17), new DateOnly(2026, 3, 16)),
                (joan.Contracts.Single().Type, joan.Contracts.Single().StartDate, joan.Contracts.Single().EndDate));
            Assert.Equal(1_200_000m, joan.Salaries!.Single().MonthlyBasic);
        }
    }

    [Fact]
    public async Task Any_bad_row_means_nothing_is_imported_and_every_problem_is_reported_by_row()
    {
        var co = await CompanyWithOrgAsync();
        var file = Workbook(
            ["Ann", "Good", "Female", "2025-01-06", "ann@co.test"],
            ["Bad", "Gender", "Other", "2025-01-06"],
            ["No", "Dept", "Male", "2025-01-06", null, "Marketing"],
            ["Bad", "Nin", "Male", "2025-01-06", null, null, null, null, "NationalId", "12345"],
            ["Dup", "Email", "Male", "2025-01-06", "ANN@co.test"],
            ["Taken", "Email", "Male", "2025-01-06", "grace@co.test"],
            ["Early", "Manager", "Male", "2025-01-06", null, null, null, "late@co.test"],
            ["Late", "Manager", "Male", "2025-01-06", "late@co.test"],
            ["No", "Hire", "Male", null],
            ["Bad", "Date", "Male", "31st Jan"],
            ["Fixed", "NoEnd", "Male", "2025-01-06", null, null, null, null, null, null, "FixedTerm"]);

        await using var s = As(co);
        var result = await s.Sheets.ImportAsync(file, commit: true, default);
        Assert.False(result.Committed);
        Assert.Equal(1, await s.Scope.Db.Employees.CountAsync());
        var byRow = result.Errors.ToLookup(e => e.Row, e => e.Message);
        Assert.Empty(byRow[2]);
        Assert.Contains(byRow[3], m => m.Contains("Gender 'Other'"));
        Assert.Contains(byRow[4], m => m.Contains("no active department 'Marketing'"));
        Assert.Contains(byRow[5], m => m.Contains("not a valid NIN"));
        Assert.Contains(byRow[6], m => m.Contains("Row 2 has the same work email"));
        Assert.Contains(byRow[7], m => m.Contains("already has the work email grace@co.test"));
        Assert.Contains(byRow[8], m => m.Contains("further down the file"));
        Assert.Empty(byRow[9]);
        Assert.Contains(byRow[10], m => m.Contains("Hire date is required"));
        Assert.Contains(byRow[11], m => m.Contains("isn't a date"));
        Assert.Contains(byRow[12], m => m.Contains("end date", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Users_without_pay_access_cannot_import_salaries_and_files_must_be_workbooks_with_the_required_columns()
    {
        var co = await CompanyWithOrgAsync();
        await using var s = As(co, [Roles.HrOfficer]);
        var result = await s.Sheets.ImportAsync(Workbook(["Ann", "Good", "Female", "2025-01-06", null, null, null, null, null, null, null, null, null, 900_000m]), false, default);
        Assert.Contains("can't set salaries", Assert.Single(result.Errors).Message);

        await Assert.ThrowsAsync<DomainException>(() => s.Sheets.ImportAsync(new MemoryStream("not,a,workbook"u8.ToArray()), false, default));
        using var book = new XLWorkbook();
        book.AddWorksheet("Employees").Cell(1, 1).Value = "Name";
        var noColumns = new MemoryStream();
        book.SaveAs(noColumns);
        noColumns.Position = 0;
        var ex = await Assert.ThrowsAsync<DomainException>(() => s.Sheets.ImportAsync(noColumns, false, default));
        Assert.Contains("no First name, Last name, Gender, Hire date column", ex.Message);
    }

    [Fact]
    public async Task The_template_has_dropdowns_from_the_companys_setup_and_pay_columns_only_for_pay_users()
    {
        var co = await CompanyWithOrgAsync();
        await using (var s = As(co))
        {
            using var book = new XLWorkbook(new MemoryStream((await s.Sheets.TemplateAsync(default)).Content));
            var headings = book.Worksheet("Employees").Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            Assert.Contains("First name *", headings);
            Assert.Contains("Tax number (TIN)", headings);
            Assert.Contains("Monthly basic salary (UGX)", headings);
            var lists = book.Worksheet("Lists");
            Assert.Equal(XLWorksheetVisibility.Hidden, lists.Visibility);
            Assert.Contains("Finance", lists.ColumnsUsed().SelectMany(c => c.CellsUsed()).Select(c => c.GetString()));
            Assert.NotEmpty(book.Worksheet("Employees").DataValidations);

            // The template itself, filled in, imports.
            var sheet = book.Worksheet("Employees");
            string[] values = ["Ann", "", "Good", "Female"];
            for (var i = 0; i < values.Length; i++) sheet.Cell(2, i + 1).SetValue(values[i]);
            sheet.Cell(2, headings.IndexOf("Hire date *") + 1).Value = new DateTime(2025, 1, 6);
            var filled = new MemoryStream();
            book.SaveAs(filled);
            filled.Position = 0;
            Assert.Empty((await s.Sheets.ImportAsync(filled, false, default)).Errors);
        }
        await using (var s = As(co, [Roles.HrOfficer]))
        {
            using var book = new XLWorkbook(new MemoryStream((await s.Sheets.TemplateAsync(default)).Content));
            Assert.DoesNotContain(book.Worksheet("Employees").Row(1).CellsUsed(), c => c.GetString().StartsWith("Monthly basic"));
        }
    }

    [Fact]
    public async Task Exports_hold_only_the_people_and_columns_the_user_may_see()
    {
        var co = await CompanyWithOrgAsync();
        await using (var s = As(co))
        {
            var grace = (await s.Employees.ListAsync(new EmployeeQuery(), default)).Items.Single();
            await s.Employees.CreateAsync(EmployeeTests.NewEmployee("Tom", "Report", managerId: grace.Id, nin: "CM90012345ABCD",
                salary: new AddSalaryRequest(new DateOnly(2025, 1, 6), 1_500_000m, "Start")), default);
            await s.Employees.CreateAsync(EmployeeTests.NewEmployee("Other", "Person"), default);
        }

        static List<Dictionary<string, string>> Read(Spreadsheet file)
        {
            using var book = new XLWorkbook(new MemoryStream(file.Content));
            var sheet = book.Worksheet("Employees");
            var headings = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            return sheet.RowsUsed().Skip(1).Select(r => headings.Select((h, i) => (h, v: r.Cell(i + 1).GetFormattedString())).ToDictionary(x => x.h, x => x.v)).ToList();
        }

        await using (var s = As(co))
        {
            var rows = Read(await s.Sheets.ExportAsync(new EmployeeQuery(), default));
            Assert.Equal(["EMP-0001", "EMP-0002", "EMP-0003"], rows.Select(r => r["Employee number"]));
            var tom = rows[1];
            Assert.Equal(("CM90012345ABCD", "EMP-0001", "1,500,000"), (tom["ID number"], tom["Manager"], tom["Monthly basic salary (UGX)"]));
        }
        await using (var s = As(co, [Roles.HrOfficer]))
        {
            var tom = Read(await s.Sheets.ExportAsync(new EmployeeQuery(Search: "tom"), default)).Single();
            Assert.True(tom.ContainsKey("ID number"));
            Assert.DoesNotContain(tom.Keys, k => k.StartsWith("Monthly basic") || k == "Account number");
        }
        await using (var s = As(co, [Roles.LineManager], email: "grace@co.test"))
        {
            var rows = Read(await s.Sheets.ExportAsync(new EmployeeQuery(), default));
            Assert.Equal("Tom", Assert.Single(rows)["First name"]);
            Assert.DoesNotContain(rows[0].Keys, k => k is "ID number" or "Date of birth" || k.StartsWith("Monthly basic"));
        }
    }
}

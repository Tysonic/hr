using System.Globalization;
using TyHr.Domain;
using TyHr.Domain.Attendance;

namespace TyHr.Application.Attendance;

/// <summary>Which columns hold what; null = detect from the headings.</summary>
public record PunchColumns(string? Id = null, string? DateTime = null, string? Date = null, string? Time = null, string? Direction = null);

public record ParsedPunch(int Line, string DeviceUserId, DateTime LocalTime, ClockDirection Direction);

public record PunchCsvResult(IReadOnlyList<string> Headings, PunchColumns Columns, IReadOnlyList<ParsedPunch> Punches, IReadOnlyList<string> Errors, int Lines);

/// <summary>
/// Reads punch exports from biometric devices (ZKTeco, Hikvision, Suprema and the like): CSV or tab-separated, with a
/// header row, a user-number column and either one date-time column or separate date and time columns. Dates are read
/// day-first (31/01/2026) or ISO (2026-01-31). The direction column is optional; ZKTeco's 0/1 (check-in/out) and
/// words such as "C/In" or "Check Out" are understood.
/// </summary>
public static class PunchCsv
{
    private static readonly string[] IdNames = ["ac-no.", "ac-no", "ac no", "user id", "userid", "enroll no", "enrollno", "badge", "person id", "employee id", "emp no", "id", "no.", "no"];
    private static readonly string[] DateTimeNames = ["date/time", "datetime", "date time", "time stamp", "timestamp", "check time", "checktime", "punch time", "record time"];
    private static readonly string[] DateNames = ["date"];
    private static readonly string[] TimeNames = ["time"];
    private static readonly string[] DirectionNames = ["state", "status", "attendance status", "in/out", "inout", "direction", "check type", "checktype", "punch state", "event"];

    private static readonly string[] Formats =
    [
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd HH:mm", "yyyy-MM-ddTHH:mm:ss",
        "d/M/yyyy HH:mm:ss", "d/M/yyyy H:mm:ss", "d/M/yyyy HH:mm", "d/M/yyyy H:mm", "d/M/yyyy h:mm tt", "d/M/yyyy h:mm:ss tt",
        "d-M-yyyy HH:mm:ss", "d-M-yyyy HH:mm", "d.M.yyyy HH:mm:ss", "d.M.yyyy HH:mm", "d MMM yyyy HH:mm", "d-MMM-yyyy HH:mm", "d-MMM-yy HH:mm",
    ];

    public static PunchCsvResult Parse(string text, PunchColumns? columns = null)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count < 2) throw new DomainException("The file has no punches in it (a heading row, then one punch per line).");
        var delimiter = new[] { '\t', ';', ',' }.MaxBy(d => lines[0].Count(c => c == d));
        var headings = Split(lines[0], delimiter).Select(h => h.Trim().Trim('"')).ToList();

        int Find(string? chosen, string[] names, params int[] skip)
        {
            if (chosen != null)
                return headings.FindIndex(h => h.Equals(chosen, StringComparison.OrdinalIgnoreCase)) is >= 0 and var i
                    ? i : throw new DomainException($"There is no column called '{chosen}'.");
            foreach (var name in names)
                if (headings.FindIndex(h => h.Equals(name, StringComparison.OrdinalIgnoreCase)) is >= 0 and var i && !skip.Contains(i)) return i;
            return -1;
        }
        var id = Find(columns?.Id, IdNames);
        var dateTime = Find(columns?.DateTime, DateTimeNames);
        var date = dateTime < 0 ? Find(columns?.Date, DateNames) : -1;
        var time = dateTime < 0 ? Find(columns?.Time, TimeNames, date) : -1;
        if (dateTime < 0 && date >= 0 && time < 0) (dateTime, date) = (date, -1); // one "Date" column holding date and time
        if (dateTime < 0 && date < 0 && time >= 0) (dateTime, time) = (time, -1); // one "Time" column holding date and time
        var direction = Find(columns?.Direction, DirectionNames);
        if (id < 0) throw new DomainException($"Couldn't tell which column has the user number. Headings: {string.Join(", ", headings)}.");
        if (dateTime < 0 && (date < 0 || time < 0)) throw new DomainException($"Couldn't tell which column has the punch time. Headings: {string.Join(", ", headings)}.");

        var punches = new List<ParsedPunch>();
        var errors = new List<string>();
        for (var n = 1; n < lines.Count; n++)
        {
            var cells = Split(lines[n], delimiter).Select(c => c.Trim().Trim('"').Trim()).ToList();
            string Cell(int i) => i >= 0 && i < cells.Count ? cells[i] : "";
            var user = Cell(id).TrimStart('0') is { Length: > 0 } u ? u : Cell(id).Length > 0 ? "0" : "";
            var when = dateTime >= 0 ? Cell(dateTime) : $"{Cell(date)} {Cell(time)}";
            if (user.Length == 0) { errors.Add($"Line {n + 1}: no user number."); continue; }
            if (!System.DateTime.TryParseExact(when.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
            {
                errors.Add($"Line {n + 1}: '{when}' isn't a date and time TyHR can read.");
                continue;
            }
            punches.Add(new ParsedPunch(n + 1, user, local, ParseDirection(Cell(direction))));
        }
        string? Name(int i) => i >= 0 ? headings[i] : null;
        return new PunchCsvResult(headings, new PunchColumns(Name(id), Name(dateTime), Name(date), Name(time), Name(direction)), punches, errors, lines.Count - 1);
    }

    public static ClockDirection ParseDirection(string value) => value.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "") switch
    {
        "0" or "in" or "i" or "c/in" or "checkin" or "clockin" or "dutyon" or "4" or "otin" => ClockDirection.In,
        "1" or "out" or "o" or "c/out" or "checkout" or "clockout" or "dutyoff" or "5" or "otout" => ClockDirection.Out,
        _ => ClockDirection.Unknown,
    };

    /// <summary>Splits a line on the delimiter, keeping quoted fields (with doubled quotes) together.</summary>
    private static List<string> Split(string line, char delimiter)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
            else if (c == '"') quoted = !quoted;
            else if (c == delimiter && !quoted) { cells.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        cells.Add(current.ToString());
        return cells;
    }
}

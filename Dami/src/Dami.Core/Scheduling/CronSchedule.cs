namespace Dami.Core.Scheduling;

/// <summary>A validated five-field cron schedule (minute through day-of-week).</summary>
public sealed class CronSchedule
{
    private readonly Field minutes;
    private readonly Field hours;
    private readonly Field days;
    private readonly Field months;
    private readonly Field weekdays;

    private CronSchedule(Field minutes, Field hours, Field days, Field months, Field weekdays)
    {
        this.minutes = minutes;
        this.hours = hours;
        this.days = days;
        this.months = months;
        this.weekdays = weekdays;
    }

    /// <summary>Parses standard five-field cron syntax.</summary>
    public static CronSchedule Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new FormatException("A cron expression is required.");
        }

        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            throw new FormatException("A cron expression must contain five fields.");
        }

        return new CronSchedule(
            Field.Parse(fields[0], 0, 59),
            Field.Parse(fields[1], 0, 23),
            Field.Parse(fields[2], 1, 31),
            Field.Parse(fields[3], 1, 12),
            Field.Parse(fields[4], 0, 7, normalizeSunday: true));
    }

    /// <summary>Whether the supplied local wall-clock minute satisfies this schedule.</summary>
    public bool IsMatch(DateTimeOffset localTime)
    {
        var dayMatches = this.days.Contains(localTime.Day);
        var weekdayMatches = this.weekdays.Contains((int)localTime.DayOfWeek);
        var calendarDayMatches = this.days.IsWildcard || this.weekdays.IsWildcard
            ? dayMatches && weekdayMatches
            : dayMatches || weekdayMatches;
        return this.minutes.Contains(localTime.Minute)
            && this.hours.Contains(localTime.Hour)
            && this.months.Contains(localTime.Month)
            && calendarDayMatches;
    }

    /// <summary>Finds the first matching minute after an instant in a named time zone.</summary>
    public DateTimeOffset Next(DateTimeOffset after, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var candidate = new DateTimeOffset(
            after.UtcDateTime.AddMinutes(1).Ticks - after.UtcDateTime.Ticks % TimeSpan.TicksPerMinute,
            TimeSpan.Zero);
        var limit = candidate.AddYears(5);
        while (candidate < limit)
        {
            if (this.IsMatch(TimeZoneInfo.ConvertTime(candidate, timeZone)))
            {
                return candidate;
            }

            candidate = candidate.AddMinutes(1);
        }

        throw new InvalidOperationException("The cron schedule has no occurrence within five years.");
    }

    private sealed class Field
    {
        private readonly HashSet<int> values;

        private Field(HashSet<int> values, bool isWildcard)
        {
            this.values = values;
            this.IsWildcard = isWildcard;
        }

        public bool IsWildcard { get; }

        public bool Contains(int value) => this.values.Contains(value);

        public static Field Parse(string text, int minimum, int maximum, bool normalizeSunday = false)
        {
            var values = new HashSet<int>();
            foreach (var part in text.Split(','))
            {
                AddPart(part, minimum, maximum, normalizeSunday, values);
            }

            if (values.Count == 0)
            {
                throw new FormatException($"Cron field '{text}' selects no values.");
            }

            return new Field(values, text == "*");
        }

        private static void AddPart(
            string part,
            int minimum,
            int maximum,
            bool normalizeSunday,
            HashSet<int> values)
        {
            var stepParts = part.Split('/');
            if (stepParts.Length > 2 || !int.TryParse(stepParts.ElementAtOrDefault(1) ?? "1", out var step)
                || step < 1)
            {
                throw new FormatException($"Invalid cron step '{part}'.");
            }

            int start;
            int end;
            if (stepParts[0] == "*")
            {
                (start, end) = (minimum, maximum);
            }
            else
            {
                var range = stepParts[0].Split('-');
                if (range.Length > 2 || !int.TryParse(range[0], out start)
                    || !int.TryParse(range.ElementAtOrDefault(1) ?? range[0], out end))
                {
                    throw new FormatException($"Invalid cron value '{part}'.");
                }
            }

            ValidateRange(part, start, end, minimum, maximum);

            for (var value = start; value <= end; value += step)
            {
                values.Add(normalizeSunday && value == 7 ? 0 : value);
            }
        }

        private static void ValidateRange(string part, int start, int end, int minimum, int maximum)
        {
            if (start < minimum || end > maximum || start > end)
            {
                throw new FormatException($"Cron value '{part}' is outside {minimum}-{maximum}.");
            }
        }
    }
}

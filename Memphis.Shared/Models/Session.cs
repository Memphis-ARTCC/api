﻿using Microsoft.EntityFrameworkCore;

namespace Memphis.Shared.Models;

[Index(nameof(Name))]
[Index(nameof(Callsign))]
[Index(nameof(Frequency))]
[Index(nameof(Start))]
[Index(nameof(End))]
public class Session
{
    public int Id { get; set; }
    public required User User { get; set; }
    public required string Name { get; set; }
    public required string Callsign { get; set; }
    public required string Frequency { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;
}
using dotenv.net;
using FluentValidation;
using Memphis.API.Data;
using Memphis.API.Services;
using Memphis.API.Validators;
using Memphis.Shared.Dtos;
using Memphis.Shared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Prometheus;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;
using System.Text;

DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
var logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
builder.Logging.AddSerilog(logger, dispose: true);

builder.WebHost.UseSentry(options =>
{
    options.Dsn = Environment.GetEnvironmentVariable("SENTRY_DSN") ??
                  throw new ArgumentNullException("SENTRY_DSN env variable not found");
    options.TracesSampleRate = 1.0;
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Format is the word Bearer, then a space, followed by the token",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddDbContext<DatabaseContext>(options =>
{
    options.UseNpgsql(Environment.GetEnvironmentVariable("CONNECTION_STRING") ??
                      throw new ArgumentException("CONNECTION_STRING env variable not found"));
});

var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ??
                throw new ArgumentNullException("REDIS_HOST env variable not found");
builder.Services.AddSingleton(ConnectionMultiplexer.Connect(redisHost).GetDatabase());

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = true;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ??
                      throw new ArgumentNullException("JWT_ISSUER env variable not found"),
        ValidateIssuerSigningKey = true,
        IssuerSigningKey =
            new SymmetricSecurityKey(Encoding.ASCII.GetBytes(Environment.GetEnvironmentVariable("JWT_SECRET") ??
                                                             throw new ArgumentNullException(
                                                                 "JWT_SECRET env variable not found"))),
        ValidAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ??
                        throw new ArgumentNullException("JWT_AUDIENCE env variable not found"),
        ValidateAudience = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

builder.Services.AddScoped<IValidator<AirportDto>, AirportValidator>();
builder.Services.AddScoped<IValidator<CommentDto>, CommentValidator>();
builder.Services.AddScoped<IValidator<EventPositionDto>, EventPositionValidator>();
builder.Services.AddScoped<IValidator<EventRegistrationDto>, EventRegistrationValidator>();
builder.Services.AddScoped<IValidator<EventDto>, EventValidator>();
builder.Services.AddScoped<IValidator<FaqDto>, FaqValidator>();
builder.Services.AddScoped<IValidator<FeedbackDto>, FeedbackValidator>();
builder.Services.AddScoped<IValidator<FileDto>, FileValidator>();
builder.Services.AddScoped<IValidator<OtsDto>, OtsValidator>();
builder.Services.AddScoped<IValidator<TrainingMilestone>, TrainingMilestoneValidator>();
builder.Services.AddScoped<IValidator<TrainingScheduleDto>, TrainingScheduleValidator>();

builder.Services.AddScoped<LoggingService>();
builder.Services.AddScoped<RedisService>();
builder.Services.AddScoped<S3Service>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        policy =>
        {
            policy.AllowAnyOrigin();
            policy.AllowAnyHeader();
            policy.AllowAnyMethod();
        });
});

var app = builder.Build();

using (var serviceScope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
{
    using var context = serviceScope.ServiceProvider.GetService<DatabaseContext>();
    if (context != null && context.Database.GetMigrations().Any())
        context.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors();
}
else
{
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });
}

app.UseMetricServer();
app.UseHttpMetrics();

app.UseRouting();
app.UseSentryTracing();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
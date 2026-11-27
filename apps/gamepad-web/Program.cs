using GamepadWeb.Dsu;
using GamepadWeb.Input;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DsuOptions>(builder.Configuration.GetSection("Dsu"));
builder.Services.AddSingleton<ControllerSlots>();
builder.Services.AddHostedService<DsuServer>();

if (builder.Configuration.GetValue<bool>("Dsu:Demo"))
{
    builder.Services.AddHostedService<DemoInputService>();
}

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Stato delle 4 porte (utile per il debug)
app.MapGet("/api/slots", (ControllerSlots slots) =>
    slots.Snapshot().Select((state, index) => new
    {
        port = index + 1,
        connected = state is not null,
        buttons = state?.Buttons.ToString(),
        leftStick = state is { } s ? new { x = (int)s.LeftX, y = (int)s.LeftY } : null,
            }));

app.Run();

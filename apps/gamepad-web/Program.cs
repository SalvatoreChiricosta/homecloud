using System.Net.NetworkInformation;
using System.Net.Sockets;
using GamepadWeb.Dsu;
using GamepadWeb.Input;
using GamepadWeb.Session;
using GamepadWeb.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DsuOptions>(builder.Configuration.GetSection("Dsu"));
builder.Services.Configure<GameSessionOptions>(builder.Configuration.GetSection("Session"));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ControllerSlots>();
builder.Services.AddSingleton<GameSession>();
builder.Services.AddSingleton<PadSocketHandler>();
builder.Services.AddHostedService<DsuServer>();
builder.Services.AddHostedService<SessionMaintenanceService>();

if (builder.Configuration.GetValue<bool>("Dsu:Demo"))
{
    builder.Services.AddHostedService<DemoInputService>();
}

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(10) });
// no-cache: il browser ricontrolla i file a ogni apertura, così vede subito le nuove versioni
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Pagina gamepad: /pad?c=<codice partita>
app.MapGet("/pad", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "pad.html"), "text/html; charset=utf-8"));

app.Map("/ws", (HttpContext context, PadSocketHandler handler) => handler.HandleAsync(context));

// Stato delle 4 porte (debug)
app.MapGet("/api/slots", (ControllerSlots slots) =>
    slots.Snapshot().Select((state, index) => new
    {
        port = index + 1,
        connected = state is not null,
        buttons = state?.Buttons.ToString(),
        leftStick = state is { } s ? new { x = (int)s.LeftX, y = (int)s.LeftY } : null,
    }));

// All'avvio scrive nel log gli indirizzi da aprire sul telefono
app.Lifetime.ApplicationStarted.Register(() =>
{
    var session = app.Services.GetRequiredService<GameSession>();
    var port = app.Urls.Select(u => new Uri(u.Replace("0.0.0.0", "localhost").Replace("[::]", "localhost")).Port)
                       .DefaultIfEmpty(8080).First();

    var addresses = NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
        .Select(a => a.Address.ToString());

    app.Logger.LogInformation("Codice partita: {Code}", session.JoinCode);
    foreach (var address in addresses)
    {
        app.Logger.LogInformation("Gamepad: http://{Address}:{Port}/pad?c={Code}", address, port, session.JoinCode);
    }
});

app.Run();

// The same kind of app as Shop.Api, with no PulseSharp and no database: what ASP.NET Core costs by itself.
var app = WebApplication.CreateBuilder(args).Build();
app.MapGet("/health", () => Results.Ok());
app.Run();

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using RecouvrementAPI.Data;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// --- CONFIGURATION QUESTPDF ---
// Définit la licence en mode communautaire pour autoriser la génération de PDF
QuestPDF.Settings.License = LicenseType.Community;

// --- SERVICES CONTAINER (Injection de Dépendances) ---

// Récupération de la "Connection String" depuis appsettings.json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Définition explicite de la version du serveur MySQL
var serverVersion = new MySqlServerVersion(new Version(8, 0, 31));

// Injection du DbContext dans le conteneur de services
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, serverVersion));

// ✅ AJOUT 1 : JWT Authentication
// Pour sécuriser le back-office agent avec un token JWT
var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey!))
        };
    });

// Active le système d'autorisation [Authorize]
builder.Services.AddAuthorization();

// AJOUT 2 : CORS
// Permet à Angular (localhost:4200) de communiquer avec l'API
// Sans ça le navigateur bloque toutes les requêtes Angular
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Enregistrement des services pour les contrôleurs API
builder.Services.AddControllers();

var app = builder.Build();

// --- HTTP REQUEST PIPELINE ---
//  L'ordre des middlewares est très important !

//  AJOUT 3 : CORS → doit être en premier
app.UseCors("AllowAngular");

// AJOUT 4 : Authentication → doit être AVANT Authorization
app.UseAuthentication();

// Authorization → active [Authorize] sur les contrôleurs agent
app.UseAuthorization();

// Point d'entrée racine pour le diagnostic
app.MapGet("/", () => "L'API est en ligne sur .NET 10 !");

// Analyse les attributs [Route] (ex: api/client, api/agent)
app.MapControllers();

// Lance l'écoute des requêtes HTTP entrantes
app.Run();
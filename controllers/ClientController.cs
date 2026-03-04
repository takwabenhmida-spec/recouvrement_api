using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecouvrementAPI.Data;
using RecouvrementAPI.DTOs;
using RecouvrementAPI.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace RecouvrementAPI.Controllers
{
    // Contrôleur qui gère tout ce que le CLIENT peut faire
    // Route de base : http://localhost:5203/api/client
    [ApiController]
    [Route("api/client")]
    public class ClientController : ControllerBase
    {
        // _context : accès à la base de données MySQL
        private readonly ApplicationDbContext _context;

        // _env : accès au système de fichiers du serveur (upload)
        private readonly IWebHostEnvironment _env;

        // Constructeur : .NET injecte automatiquement les dépendances
        public ClientController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // ==============================
        // MÉTHODE PRIVÉE : Vérifier token
        // Utilisée par tous les endpoints pour éviter la répétition
        // Retourne null si token introuvable
        // ==============================
        private async Task<Client> VerifierToken(string token)
        {
            // Cherche le client par son token UUID dans la BDD
            var client = await _context.Clients
                .Include(c => c.Agence)
                .Include(c => c.Dossiers)
                .FirstOrDefaultAsync(c => c.TokenAcces == token);

            // Token introuvable → retourne null
            if (client == null) return null;

            return client;
        }

        // ==============================
        // GET api/client/historique/{token}
        // Appelé par Angular quand le client entre son token UUID
        // Retourne toutes les infos du dossier en JSON
        // ==============================
        [HttpGet("historique/{token}")]
        public async Task<IActionResult> GetHistorique(string token)
        {
            // Vérification : token non vide
            if (string.IsNullOrEmpty(token))
                return BadRequest("Token requis");

            // Récupère le client avec toutes ses relations (jointures)
            var client = await _context.Clients
                .Include(c => c.Agence)
                .Include(c => c.Dossiers)
                    .ThenInclude(d => d.Echeances)
                .Include(c => c.Dossiers)
                    .ThenInclude(d => d.HistoriquePaiements)
                .Include(c => c.Dossiers)
                    .ThenInclude(d => d.Relances)
                .Include(c => c.Dossiers)
                    .ThenInclude(d => d.Communications)
                .FirstOrDefaultAsync(c => c.TokenAcces == token);

            // Token UUID introuvable → 401 Unauthorized
            if (client == null)
                return Unauthorized("Token invalide");

            // Journalisation de l'accès dans l'historique
            // Trace l'IP du client pour la sécurité
            var dossierPrincipal = client.Dossiers
                .OrderByDescending(d => d.DateCreation)
                .FirstOrDefault();

            if (dossierPrincipal != null)
            {
                _context.HistoriqueActions.Add(new HistoriqueAction
                {
                    IdDossier = dossierPrincipal.IdDossier,
                    ActionDetail = $"Accès client via token UUID — IP : {HttpContext.Connection.RemoteIpAddress}",
                    Acteur = "client",
                    DateAction = DateTime.Now
                });
                await _context.SaveChangesAsync();
            }

            // Prend le dossier le plus récent du client
            var dossier = client.Dossiers
                .OrderByDescending(d => d.DateCreation)
                .FirstOrDefault();

            if (dossier == null)
                return NotFound("Aucun dossier trouvé pour ce client");

            // Construction du DTO envoyé à Angular
            // DTO = objet simplifié pour ne pas exposer tout le modèle
            var dto = new ClientHistoriqueDto
            {
                // Informations du client
                NomComplet = client.Nom + " " + client.Prenom,
                IdAgence = client.Agence != null ? client.Agence.IdAgence : 0,
                VilleAgence = client.Agence?.Ville,

                //  Type d'emprunt
                TypeEmprunt = dossier.TypeEmprunt,

                // Informations financières du dossier
                MontantImpaye = dossier.MontantImpaye,
                FraisDossier = dossier.FraisDossier,
                StatutDossier = dossier.StatutDossier,

                //  Montant initial
                MontantInitial = dossier.MontantInitial,

                // Montant payé = montant initial - montant impayé
                MontantPaye = dossier.MontantInitial - dossier.MontantImpaye,

                //  Nombre de jours de retard
                // Calculé depuis la première échéance impayée dépassée
                NombreJoursRetard = dossier.Echeances
                    .Where(e => e.Statut == "impaye" && e.DateEcheance < DateTime.Now)
                    .Any()
                    ? (int)(DateTime.Now - dossier.Echeances
                        .Where(e => e.Statut == "impaye" && e.DateEcheance < DateTime.Now)
                        .Min(e => e.DateEcheance)).TotalDays
                    : 0,

                // Prochaine échéance la plus proche
                DateEcheance = dossier.Echeances
                    .OrderBy(e => e.DateEcheance)
                    .Select(e => e.DateEcheance)
                    .FirstOrDefault(),

                // Liste complète des échéances
                Echeances = dossier.Echeances.Select(e => new EcheanceDto
                {
                    Montant = e.Montant,
                    DateEcheance = e.DateEcheance,
                    Statut = e.Statut // impaye / paye / partiel
                }).ToList(),

                // Historique des paiements effectués
                Paiements = dossier.HistoriquePaiements.Select(p => new HistoriquePaiementDto
                {
                    MontantPaye = p.MontantPaye,
                    TypePaiement = p.TypePaiement,
                    DatePaiement = p.DatePaiement
                }).ToList(),

                // Relances envoyées au client
                Relances = dossier.Relances.Select(r => new RelanceDto
                {
                    DateRelance = r.DateRelance,
                    Moyen = r.Moyen,
                    Statut = r.Statut
                }).ToList(),

                // Historique des communications
                Communications = dossier.Communications.Select(c => new CommunicationDto
                {
                    Message = c.Message,
                    Origine = c.Origine, // client / agent / systeme
                    DateEnvoi = c.DateEnvoi
                }).ToList()
            };

            
            return Ok(dto);
        }

        // ==============================
        // GET api/client/recu/{token}
        // Génère et télécharge un PDF du reçu de situation
        // ==============================
        [HttpGet("recu/{token}")]
        public async Task<IActionResult> GenerateRecu(string token)
        {
            // Vérification token UUID via méthode privée
            var client = await VerifierToken(token);
            if (client == null)
                return Unauthorized("Token invalide");

            // Prend le dossier le plus récent
            var dossier = client.Dossiers
                .OrderByDescending(d => d.DateCreation)
                .FirstOrDefault();

            if (dossier == null)
                return NotFound("Aucun dossier trouvé");

            // Couleur dynamique selon statut du dossier
            //  Régularisé → Vert
            //  Contentieux → Rouge
            //  Aimable     → Bleu
            string colorHex = dossier.StatutDossier == "regularise" ? Colors.Green.Medium :
                             (dossier.StatutDossier == "contentieux" ? Colors.Red.Medium : Colors.Blue.Medium);

            // Construction du PDF avec QuestPDF
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(50);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    // En-tête : titre + date + ville agence
                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("REÇU DE SITUATION")
                                .FontSize(22).SemiBold().FontColor(Colors.Blue.Medium);
                            col.Item().Text($"Date d'édition : {DateTime.Now:dd/MM/yyyy}");
                        });
                        row.RelativeItem().AlignRight()
                            .Text($"{client.Agence?.Ville}").FontSize(16).Bold();
                    });

                    // Contenu principal du PDF
                    page.Content().PaddingVertical(25).Column(col =>
                    {
                        col.Spacing(10);
                        col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        // Nom du client
                        col.Item().Text(text => {
                            text.Span("Client : ").Bold();
                            text.Span($"{client.Nom} {client.Prenom}");
                        });

                        //  Type d'emprunt dans le PDF
                        col.Item().Text(text => {
                            text.Span("Type d'emprunt : ").Bold();
                            text.Span($"{dossier.TypeEmprunt}");
                        });

                        // Badge coloré du statut
                        col.Item().Row(r => {
                            r.AutoItem().Text("Statut du dossier : ").Bold();
                            r.AutoItem().Background(colorHex).PaddingHorizontal(5)
                                .Text(dossier.StatutDossier.ToUpper())
                                .FontColor(Colors.White).Bold();
                        });

                        //  Montant initial dans le PDF
                        col.Item().Text(text => {
                            text.Span("Montant initial : ").Bold();
                            text.Span($"{dossier.MontantInitial} TND");
                        });

                        //  Montant payé dans le PDF
                        col.Item().Text(text => {
                            text.Span("Montant payé : ").Bold();
                            text.Span($"{dossier.MontantInitial - dossier.MontantImpaye} TND");
                        });

                        // Montant impayé en grand
                        col.Item().PaddingTop(15)
                            .Background(Colors.Grey.Lighten4).Padding(15).Column(inner =>
                        {
                            inner.Item().Text("RESTE À PAYER").FontSize(10).Bold();
                            inner.Item().Text($"{dossier.MontantImpaye} TND")
                                .FontSize(26).Bold().FontColor(colorHex);
                            inner.Item().Text($"Frais dossier : {dossier.FraisDossier} TND")
                                .FontSize(10);
                        });
                    });

                    // Pied de page avec numéro de page automatique
                    page.Footer().AlignCenter().Text(x => {
                        x.Span("Document généré automatiquement - Page ");
                        x.CurrentPageNumber();
                    });
                });
            });

            // Génère et retourne le PDF au navigateur
            byte[] pdfBytes = document.GeneratePdf();
            return File(pdfBytes, "application/pdf", $"Recu_{client.Nom}.pdf");
        }

        // ==============================
        // POST api/client/upload/{token}
        // Upload justificatif de paiement (PDF/JPG/PNG max 5MB)
        // ==============================
        [HttpPost("upload/{token}")]
        public async Task<IActionResult> UploadJustificatif(string token, IFormFile fichier)
        {
            // Vérification token UUID via méthode privée
            var client = await VerifierToken(token);
            if (client == null)
                return Unauthorized("Token invalide");

            // Vérifier qu'un fichier est envoyé
            if (fichier == null || fichier.Length == 0)
                return BadRequest("Aucun fichier envoyé");

            // Vérifier le format autorisé
            
            var extensionsAutorisees = new[] { ".pdf", ".jpg", ".jpeg", ".png" };
            var extension = Path.GetExtension(fichier.FileName).ToLower();
            if (!extensionsAutorisees.Contains(extension))
                return BadRequest("Format non autorisé. Utilisez PDF, JPG ou PNG.");

            // Vérifier taille max 5MB
           
            if (fichier.Length > 5 * 1024 * 1024)
                return BadRequest("Fichier trop volumineux. Maximum 5 MB.");

            // Récupère le dossier le plus récent
            var dossier = client.Dossiers
                .OrderByDescending(d => d.DateCreation)
                .FirstOrDefault();

            if (dossier == null)
                return NotFound("Aucun dossier trouvé");

            // Crée le dossier de stockage automatiquement
           
            var uploadsPath = Path.Combine(
                _env.ContentRootPath, "uploads", dossier.IdDossier.ToString());
            Directory.CreateDirectory(uploadsPath);

            // Nom unique pour éviter les conflits
            
            var nomFichier = $"{DateTime.Now:yyyyMMddHHmmss}_{client.Nom}{extension}";
            var cheminComplet = Path.Combine(uploadsPath, nomFichier);

            // Sauvegarde le fichier sur le disque du serveur
            using (var stream = new FileStream(cheminComplet, FileMode.Create))
            {
                await fichier.CopyToAsync(stream);
            }

            // Trace l'upload dans l'historique
            _context.HistoriqueActions.Add(new HistoriqueAction
            {
                IdDossier = dossier.IdDossier,
                ActionDetail = $"Client a uploadé un justificatif : {nomFichier}",
                Acteur = "client",
                DateAction = DateTime.Now
            });

            // Communication automatique vers l'agent
            _context.Communications.Add(new Communication
            {
                IdDossier = dossier.IdDossier,
                Message = $"Le client a envoyé un justificatif : {nomFichier}",
                Origine = "client",
                DateEnvoi = DateTime.Now
            });

            // Sauvegarde tout en BDD
            await _context.SaveChangesAsync();

            // Retourne 200 OK avec le nom du fichier
            return Ok(new
            {
                message = "Fichier uploadé avec succès",
                nomFichier = nomFichier
            });
        }
    }
}
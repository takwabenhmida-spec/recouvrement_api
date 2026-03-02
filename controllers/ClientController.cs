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

        // _env : accès au système de fichiers du serveur
        // utilisé pour sauvegarder les fichiers uploadés
        private readonly IWebHostEnvironment _env;

        // Constructeur 
        public ClientController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // ==============================
        // GET api/client/historique/{token}
        
        // ==============================
        [HttpGet("historique/{token}")]
        public async Task<IActionResult> GetHistorique(string token)
        {
            // Vérification : token non vide
            if (string.IsNullOrEmpty(token))
                return BadRequest("Token requis");

            // Récupère le client depuis la BDD avec toutes ses relations
            // Include = charge les tables liées en même temps (jointures)
            var client = await _context.Clients
                .Include(c => c.Agence)              // infos de l'agence bancaire
                .Include(c => c.Dossiers)
                    .ThenInclude(d => d.Echeances)   // liste des échéances du dossier
                .Include(c => c.Dossiers)
                    .ThenInclude(d => d.HistoriquePaiements) // paiements effectués
                .Include(c => c.Dossiers)
                    .ThenInclude(d => d.Relances)    // relances envoyées au client
                .Include(c => c.Dossiers)
                    .ThenInclude(d => d.Communications) // échanges banque-client
                .FirstOrDefaultAsync(c => c.TokenAcces == token); // cherche par token

           
            if (client == null)
                return Unauthorized("Token invalide");

            // Prend le dossier le plus récent du client
            // Un client peut avoir plusieurs dossiers historiques
            var dossier = client.Dossiers
                .OrderByDescending(d => d.DateCreation)
                .FirstOrDefault();

            if (dossier == null)
                return NotFound("Aucun dossier trouvé pour ce client");

            // Construction du DTO 
            var dto = new ClientHistoriqueDto
            {
                // Informations du client
                NomComplet = client.Nom + " " + client.Prenom,
                IdAgence = client.Agence != null ? client.Agence.IdAgence : 0,
                VilleAgence = client.Agence?.Ville,

                // Informations financières du dossier
                MontantImpaye = dossier.MontantImpaye,
                FraisDossier = dossier.FraisDossier,
                StatutDossier = dossier.StatutDossier, // aimable/contentieux/regularise

                // Prochaine échéance (la plus proche dans le temps)
                DateEcheance = dossier.Echeances
                    .OrderBy(e => e.DateEcheance)
                    .Select(e => e.DateEcheance)
                    .FirstOrDefault(),

                // Liste complète des échéances
                Echeances = dossier.Echeances.Select(e => new EcheanceDto
                {
                    Montant = e.Montant,
                    DateEcheance = e.DateEcheance,
                    Statut = e.Statut // impaye/paye/partiel
                }).ToList(),

                // Historique des paiements déjà effectués
                Paiements = dossier.HistoriquePaiements.Select(p => new HistoriquePaiementDto
                {
                    MontantPaye = p.MontantPaye,
                    TypePaiement = p.TypePaiement, // virement/cheque/cash/autre
                    DatePaiement = p.DatePaiement
                }).ToList(),

                // Historique des relances envoyées au client
                Relances = dossier.Relances.Select(r => new RelanceDto
                {
                    DateRelance = r.DateRelance,
                    Moyen = r.Moyen,   // email/sms/appel
                    Statut = r.Statut  // envoye/repondu/sans_reponse
                }).ToList(),

                // Historique des communications banque-client
                Communications = dossier.Communications.Select(c => new CommunicationDto
                {
                    Message = c.Message,
                    Origine = c.Origine, // client/agent/systeme
                    DateEnvoi = c.DateEnvoi
                }).ToList()
            };

            
            return Ok(dto);
        }

        // ==============================
        // GET api/client/recu/{token}
        // Génère et télécharge un PDF du reçu de situation
        // Utilisé par le client pour avoir une preuve de sa situation
        // ==============================
        [HttpGet("recu/{token}")]
        public async Task<IActionResult> GenerateRecu(string token)
        {
            // Récupère le client avec son agence et ses dossiers
            var client = await _context.Clients
                .Include(c => c.Agence)
                .Include(c => c.Dossiers)
                .FirstOrDefaultAsync(c => c.TokenAcces == token);

            // Token invalide → 401
            if (client == null)
                return Unauthorized("Token invalide");

            // Prend le dossier le plus récent
            var dossier = client.Dossiers
                .OrderByDescending(d => d.DateCreation)
                .FirstOrDefault();

            // Aucun dossier → 404
            if (dossier == null)
                return NotFound("Aucun dossier trouvé");

            // Couleur dynamique selon le statut du dossier
            //  Régularisé → Vert
            //  Contentieux → Rouge
            //  Aimable     → Bleu
            string colorHex = dossier.StatutDossier == "regularise" ? Colors.Green.Medium :
                             (dossier.StatutDossier == "contentieux" ? Colors.Red.Medium : Colors.Blue.Medium);

            // Création du document PDF avec QuestPDF
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(50);            // marges de 50px
                    page.Size(PageSizes.A4);    // format A4 standard
                    page.DefaultTextStyle(x => x.FontSize(12)); // taille texte par défaut

                    // EN-TÊTE : titre à gauche + ville à droite
                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            // Titre du document
                            col.Item().Text("REÇU DE SITUATION")
                                .FontSize(22).SemiBold().FontColor(Colors.Blue.Medium);
                            // Date de génération automatique
                            col.Item().Text($"Date d'édition : {DateTime.Now:dd/MM/yyyy}");
                        });
                        // Ville de l'agence à droite
                        row.RelativeItem().AlignRight()
                            .Text($"{client.Agence?.Ville}").FontSize(16).Bold();
                    });

                    // CONTENU PRINCIPAL du PDF
                    page.Content().PaddingVertical(25).Column(col =>
                    {
                        col.Spacing(10); // espace entre les éléments

                        // Ligne de séparation horizontale
                        col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        // Nom complet du client
                        col.Item().Text(text => {
                            text.Span("Client : ").Bold();
                            text.Span($"{client.Nom} {client.Prenom}");
                        });

                        // Badge coloré du statut du dossier
                        col.Item().Row(r => {
                            r.AutoItem().Text("Statut du dossier : ").Bold();
                            r.AutoItem().Background(colorHex).PaddingHorizontal(5)
                                .Text(dossier.StatutDossier.ToUpper())
                                .FontColor(Colors.White).Bold();
                        });

                        // Bloc principal : montant impayé en grand
                        col.Item().PaddingTop(15)
                            .Background(Colors.Grey.Lighten4).Padding(15).Column(inner =>
                        {
                            inner.Item().Text("RESTE À PAYER").FontSize(10).Bold();
                            // Montant en grand avec la couleur du statut
                            inner.Item().Text($"{dossier.MontantImpaye} TND")
                                .FontSize(26).Bold().FontColor(colorHex);
                        });
                    });

                    // PIED DE PAGE : numéro de page automatique
                    page.Footer().AlignCenter().Text(x => {
                        x.Span("Document généré automatiquement - Page ");
                        x.CurrentPageNumber(); // QuestPDF gère ça automatiquement
                    });
                });
            });

            // Convertit le document en tableau de bytes (fichier binaire)
            byte[] pdfBytes = document.GeneratePdf();

            // Retourne le PDF au navigateur → téléchargement automatique
            // "application/pdf" = type MIME du fichier
            return File(pdfBytes, "application/pdf", $"Recu_{client.Nom}.pdf");
        }

        // ==============================
        // POST api/client/upload/{token}
        // Permet au client d'uploader un justificatif de paiement
        // Formats acceptés : PDF, JPG, PNG (max 5MB)
        // ==============================
        [HttpPost("upload/{token}")]
        public async Task<IActionResult> UploadJustificatif(string token, IFormFile fichier)
        {
            // Vérifier le token et récupérer le client
            var client = await _context.Clients
                .Include(c => c.Dossiers)
                .FirstOrDefaultAsync(c => c.TokenAcces == token);

            
            if (client == null)
                return Unauthorized("Token invalide");

            // Vérifier qu'un fichier a bien été envoyé
            if (fichier == null || fichier.Length == 0)
                return BadRequest("Aucun fichier envoyé");

            // Vérifier que le format est autorisé
            // Sécurité : on n'accepte pas les fichiers .exe, .bat, etc.
            var extensionsAutorisees = new[] { ".pdf", ".jpg", ".jpeg", ".png" };
            var extension = Path.GetExtension(fichier.FileName).ToLower();
            if (!extensionsAutorisees.Contains(extension))
                return BadRequest("Format non autorisé. Utilisez PDF, JPG ou PNG.");

            // Vérifier que le fichier ne dépasse pas 5MB
           
            if (fichier.Length > 5 * 1024 * 1024)
                return BadRequest("Fichier trop volumineux. Maximum 5 MB.");

            // Récupère le dossier le plus récent du client
            var dossier = client.Dossiers
                .OrderByDescending(d => d.DateCreation)
                .FirstOrDefault();

            if (dossier == null)
                return NotFound("Aucun dossier trouvé");

            // Crée le dossier de stockage sur le serveur
           
            var uploadsPath = Path.Combine(
                _env.ContentRootPath, "uploads", dossier.IdDossier.ToString());
            Directory.CreateDirectory(uploadsPath); // crée si n'existe pas

            // Nom unique pour éviter les conflits de fichiers
          
            var nomFichier = $"{DateTime.Now:yyyyMMddHHmmss}_{client.Nom}{extension}";
            var cheminComplet = Path.Combine(uploadsPath, nomFichier);

            // Sauvegarde le fichier sur le disque du serveur
            // using → ferme automatiquement le stream après l'écriture
            using (var stream = new FileStream(cheminComplet, FileMode.Create))
            {
                await fichier.CopyToAsync(stream); // copie le fichier reçu
            }

            // Trace l'upload dans l'historique des actions du dossier
            _context.HistoriqueActions.Add(new HistoriqueAction
            {
                IdDossier = dossier.IdDossier,
                ActionDetail = $"Client a uploadé un justificatif : {nomFichier}",
                Acteur = "client",
                DateAction = DateTime.Now
            });

            // Crée une communication automatique vers l'agent
            // L'agent verra ce message dans son back-office
            _context.Communications.Add(new Communication
            {
                IdDossier = dossier.IdDossier,
                Message = $"Le client a envoyé un justificatif : {nomFichier}",
                Origine = "client",  // vient du client
                DateEnvoi = DateTime.Now
            });

            // Sauvegarde tout en BDD
            await _context.SaveChangesAsync();

            
            return Ok(new
            {
                message = "Fichier uploadé avec succès",
                nomFichier = nomFichier
            });

        } 
    }     
}         
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecouvrementAPI.Data;
using RecouvrementAPI.Models;

namespace RecouvrementAPI.Controllers
{
    // Contrôleur qui gère les réponses du client face à son dossier impayé
    // Route de base : http://localhost:5203/api/intention
    [ApiController]
    [Route("api/intention")]
    public class IntentionController : ControllerBase
    {
        // _context : accès à la base de données MySQL
        private readonly ApplicationDbContext _context;

        // Constructeur : .NET injecte automatiquement le contexte BDD
        public IntentionController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ==============================
        // POST api/intention
        // Appelé quand le client valide son choix sur Angular
        // Reçoit un objet JSON : { idDossier, typeIntention, commentaire }
        // ==============================
        [HttpPost]
        public async Task<IActionResult> AjouterIntention([FromBody] IntentionClient intention)
        {
            // Vérification 1 : le body JSON n'est pas null
            if (intention == null)
                return BadRequest("Données manquantes");

            // Vérification 2 : le type d'intention est obligatoire
            // Valeurs : paiement_immediat / promesse_paiement / reclamation / demande_echeance
            if (string.IsNullOrEmpty(intention.TypeIntention))
                return BadRequest("Type intention requis");

            // Vérification 3 : le dossier existe dans la BDD
            // FindAsync cherche par clé primaire id_dossier
            var dossier = await _context.Dossiers.FindAsync(intention.IdDossier);
            if (dossier == null)
                return NotFound("Dossier introuvable");

            // SÉCURITÉ : Blocage multi-soumission
            // Un client ne peut soumettre qu'UNE SEULE intention par jour
            // AnyAsync retourne true si au moins 1 enregistrement correspond
            bool dejaSoumis = await _context.Intentions.AnyAsync(i =>
                i.IdDossier == intention.IdDossier &&    // même dossier
                i.DateIntention.Date == DateTime.Today); // même jour

            if (dejaSoumis)
            {
              
                return BadRequest(new
                {
                    message = "Vous avez déjà soumis une réponse aujourd'hui. Veuillez contacter votre agence."
                });
            }

            // Date remplie automatiquement côté serveur
            intention.DateIntention = DateTime.Now;

            // Ajout dans le contexte (pas encore sauvegardé en BDD)
            _context.Intentions.Add(intention);

            // Commentaire optionnel : si vide → chaîne vide, sinon ajouté au message
            string commentairePart = string.IsNullOrEmpty(intention.Commentaire)
                ? ""
                : $" Commentaire : {intention.Commentaire}";

            // ==============================
            // CAS 1 : Paiement immédiat
            // ==============================
            if (intention.TypeIntention == "paiement_immediat")
            {
                // Message automatique visible par l'agent dans le back-office
                _context.Communications.Add(new Communication
                {
                    IdDossier = intention.IdDossier,
                    Message = $"Le client a indiqué vouloir effectuer un paiement immédiat.{commentairePart}",
                    Origine = "systeme", // généré automatiquement par l'API
                    DateEnvoi = DateTime.Now
                });

                // Trace l'action dans l'historique du dossier
                _context.HistoriqueActions.Add(new HistoriqueAction
                {
                    IdDossier = intention.IdDossier,
                    ActionDetail = "Client : intention de paiement immédiat déclarée",
                    Acteur = "client",
                    DateAction = DateTime.Now
                });
            }

            // ==============================
            // CAS 2 : Promesse de paiement
            // ==============================
            else if (intention.TypeIntention == "promesse_paiement"
                     && intention.DatePaiementPrevue.HasValue) // date obligatoire ici
            {
                // Crée une nouvelle échéance avec le montant impayé et la date promise
                _context.Echeances.Add(new Echeance
                {
                    IdDossier = dossier.IdDossier,
                    Montant = dossier.MontantImpaye,               // montant total restant
                    DateEcheance = intention.DatePaiementPrevue.Value, // date choisie par le client
                    Statut = "impaye"                              // en attente de paiement
                });

                // Alerte l'agent avec la date promise par le client
                _context.Communications.Add(new Communication
                {
                    IdDossier = intention.IdDossier,
                    Message = $"Le client a promis un paiement pour le {intention.DatePaiementPrevue.Value:dd/MM/yyyy}.{commentairePart}",
                    Origine = "systeme",
                    DateEnvoi = DateTime.Now
                });

                // Trace dans l'historique
                _context.HistoriqueActions.Add(new HistoriqueAction
                {
                    IdDossier = intention.IdDossier,
                    ActionDetail = $"Promesse de paiement prévue le {intention.DatePaiementPrevue.Value:dd/MM/yyyy}",
                    Acteur = "client",
                    DateAction = DateTime.Now
                });
            }

            // ==============================
            // CAS 3 : Réclamation
            // Le client conteste sa dette
            // → Dossier passe automatiquement en "contentieux"
            // ==============================
            else if (intention.TypeIntention == "reclamation")
            {
                // Changement de statut : aimable → contentieux
                // Sauvegardé en BDD avec SaveChangesAsync plus bas
                dossier.StatutDossier = "contentieux";

                // Communication urgente vers l'agent
                _context.Communications.Add(new Communication
                {
                    IdDossier = intention.IdDossier,
                    Message = $"Le client a soumis une réclamation. Dossier passé en contentieux.{commentairePart}",
                    Origine = "systeme",
                    DateEnvoi = DateTime.Now
                });

                // Trace dans l'historique
                _context.HistoriqueActions.Add(new HistoriqueAction
                {
                    IdDossier = intention.IdDossier,
                    ActionDetail = "Réclamation soumise — dossier passé en contentieux",
                    Acteur = "client",
                    DateAction = DateTime.Now
                });
            }

            // ==============================
            // CAS 4 : Demande d'échéancier
            // ==============================
            else if (intention.TypeIntention == "demande_echeance")
            {
                // Communication vers l'agent pour traitement
                _context.Communications.Add(new Communication
                {
                    IdDossier = intention.IdDossier,
                    Message = $"Le client demande un échéancier de paiement.{commentairePart}",
                    Origine = "systeme",
                    DateEnvoi = DateTime.Now
                });

                // Trace dans l'historique
                _context.HistoriqueActions.Add(new HistoriqueAction
                {
                    IdDossier = intention.IdDossier,
                    ActionDetail = "Demande d'échéancier soumise",
                    Acteur = "client",
                    DateAction = DateTime.Now
                });
            }

            // Sauvegarde tout en BDD en une seule fois :
            // intention + communication + historique + statut dossier
            await _context.SaveChangesAsync();

            return Ok(new {
                message = "Intention enregistrée avec succès",
                type = intention.TypeIntention
            });
        }

        // ==============================
        // GET api/intention/{idDossier}
        // Récupère l'historique des intentions d'un dossier
        // Utilisé par l'agent dans le back-office
        // ==============================
        [HttpGet("{idDossier}")]
        public async Task<IActionResult> GetIntentions(int idDossier)
        {
            // Récupère toutes les intentions triées par date (plus récente en premier)
            var intentions = await _context.Intentions
                .Where(i => i.IdDossier == idDossier)        // filtre par dossier
                .OrderByDescending(i => i.DateIntention)     // tri décroissant
                .ToListAsync();                               // convertit en liste

           
            if (!intentions.Any())
                return NotFound("Aucune intention trouvée pour ce dossier");

        
            return Ok(intentions);
        }
    }
}
using System;
using System.Collections.Generic;

namespace RecouvrementAPI.DTOs
{
    // DTO envoyé à Angular après validation du token
    // Contient toutes les infos nécessaires pour l'espace client
    public class ClientHistoriqueDto
    {
        // Informations client
        public string NomComplet { get; set; }
        public int IdAgence { get; set; }
        public string VilleAgence { get; set; }

        //  Type du crédit
        public string TypeEmprunt { get; set; }

        // Informations financières
        public decimal MontantImpaye { get; set; }

        //  Montant total du crédit au départ
        public decimal MontantInitial { get; set; }

        //  Montant déjà payé (MontantInitial - MontantImpaye)
        public decimal MontantPaye { get; set; }

        public decimal FraisDossier { get; set; }
        public string StatutDossier { get; set; }

        //  Jours de retard calculés automatiquement
        public int NombreJoursRetard { get; set; }

        public DateTime? DateEcheance { get; set; }

        // Listes détaillées
        public List<EcheanceDto> Echeances { get; set; }
        public List<HistoriquePaiementDto> Paiements { get; set; }
        public List<RelanceDto> Relances { get; set; }
        public List<CommunicationDto> Communications { get; set; }
    }
}
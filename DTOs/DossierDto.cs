using System;
using System.Collections.Generic;

namespace RecouvrementAPI.DTOs
{
    public class DossierDto
    {
        public int IdDossier { get; set; }
        public string TypeEmprunt { get; set; }
        public decimal MontantImpaye { get; set; }
        public decimal MontantInitial { get; set; }
        // Calculé : MontantInitial - MontantImpaye
        public decimal MontantPaye { get; set; }
        public decimal FraisDossier { get; set; }
        public string StatutDossier { get; set; }
        public int NombreJoursRetard { get; set; }
        public DateTime? DateEcheance { get; set; }

        // Taux d'intérêt depuis la BDD
        public decimal TauxInteret { get; set; }

        // Intérêts calculés si retard > 3 mois
        public decimal MontantInterets { get; set; }

        // Garanties depuis table garantie
        public List<GarantieDto> Garanties { get; set; }

        public List<EcheanceDto> Echeances { get; set; }
        public List<HistoriquePaiementDto> Paiements { get; set; }
        public List<RelanceDto> Relances { get; set; }
        public List<CommunicationDto> Communications { get; set; }
    }
}
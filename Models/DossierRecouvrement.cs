using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace RecouvrementAPI.Models
{
    [Table("dossier_recouvrement")]
    public class DossierRecouvrement
    {
        [Key]
        [Column("id_dossier")]
        public int IdDossier { get; set; } // Clé primaire

        [Column("id_client")]
        public int IdClient { get; set; } // Clé étrangère vers le client

        [Column("montant_initial")]
        public decimal MontantInitial { get; set; } // Montant total du dossier

        [Column("montant_impaye")]
        public decimal MontantImpaye { get; set; } // Montant restant à payer

        [Column("frais_dossier")]
        public decimal FraisDossier { get; set; } // Frais éventuels

        [Column("statut_dossier")]
        public string StatutDossier { get; set; } 
        // Statut : aimable / contentieux / regularise

        

        [Column("date_creation")]
        public DateTime DateCreation { get; set; } 
        // AJOUT : date à laquelle le dossier a été créé
        [Column("type_emprunt")]
        public string TypeEmprunt { get; set; } 


        public Client Client { get; set; } // Navigation vers client

        public ICollection<IntentionClient> Intentions { get; set; } 
        // Navigation vers intentions associées

        public ICollection<Echeance> Echeances { get; set; } 
        // Navigation vers échéances associées

        public ICollection<HistoriquePaiement> HistoriquePaiements { get; set; } 
        // Navigation vers les paiements enregistrés

        public ICollection<RelanceClient> Relances { get; set; } 
        // Navigation vers les relances effectuées

        public ICollection<Communication> Communications { get; set; } 
        // Messages et notes liés au dossier

        public ICollection<Garantie> Garanties { get; set; } 
        // Garanties associées au dossier

        public ICollection<ScoreRisque> ScoresRisque { get; set; } 
        // Évaluations de risque du dossier

        public ICollection<HistoriqueAction> HistoriqueActions { get; set; } 
        // Actions réalisées sur le dossier
    }
}
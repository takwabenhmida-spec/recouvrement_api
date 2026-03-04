using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace RecouvrementAPI.Models
{
    // Table "client" : stocke les informations personnelles des clients
    [Table("client")]
    public class Client
    {
        [Key]
        [Column("id_client")]
        public int IdClient { get; set; }

        [Column("id_agence")]
        public int IdAgence { get; set; }

        [Required]
        [Column("nom")]
        public string Nom { get; set; }

        [Required]
        [Column("prenom")]
        public string Prenom { get; set; }

        [Column("telephone")]
        public string Telephone { get; set; }
        // Numéro de téléphone pour envoi SMS du token

        [Column("email")]
        public string Email { get; set; }
        // Email pour envoi du lien token

        [Column("token_acces")]
        public string TokenAcces { get; set; }
        // Token UUID unique → généré avec UUID() dans MySQL
        

        // Navigation vers l'agence (ville, nom agence)
        public Agence Agence { get; set; }

        // Navigation vers les dossiers du client
        public ICollection<DossierRecouvrement> Dossiers { get; set; }
    }
}
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiForTravel.Models
{
    public class Photo
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public string FilePath { get; set; } // Путь в папке uploads

        // Внешний ключ
        public int TravelPointId { get; set; }
        [JsonIgnore]
        public TravelPoint TravelPoint { get; set; }
    }
}

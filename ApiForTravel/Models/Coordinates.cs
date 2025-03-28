using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiForTravel.Models
{
    public class Coordinates
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }

        // Связь 1:1 с TravelPoint
        [JsonIgnore]
        public TravelPoint TravelPoint { get; set; }
    }
}

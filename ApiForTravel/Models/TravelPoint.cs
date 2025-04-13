using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace ApiForTravel.Models
{
    public class TravelPoint
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public Coordinates Coordinates { get; set; }
        public TimeSpan? DepartureTime { get; set; }
        public TimeSpan? ArrivalTime { get; set; }
        public string Type { get; set; } // "attraction", "restaurant", "shopping"
        public double? Duration { get; set; } // в секундах
        public List<Photo> Photos { get; set; } = new();
        public string note { get; set; } = string.Empty;

        // Внешний ключ
        public int TravelId { get; set; }
        [JsonIgnore]
        public TravelModel Travel { get; set; }
    }
}

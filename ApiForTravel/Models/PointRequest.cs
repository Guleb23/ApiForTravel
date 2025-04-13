using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiForTravel.Models
{
    public class PointRequest
    {
        [Required]
        public string Name { get; set; }

        [Required]
        public string Address { get; set; }

        [Required]
        public CoordinatesDTO Coordinates { get; set; }

        public string DepartureTime { get; set; } // Изменили на string
        public string? ArrivalTime { get; set; } // Изменили на string
        public string? note { get; set; } // Изменили на string
        public double? Duration { get; set; }
        public string? Type { get; set; } = "attraction";
        public List<PhotoRequest> Photos { get; set; } = new();
    }

}

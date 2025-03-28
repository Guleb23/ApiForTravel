using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiForTravel.Models
{
    public class TravelCreateRequest
    {
        [Required]
        public string Title { get; set; }

        [Required]
        public string Date { get; set; } // Или DateTime, если нужно
        public List<PointRequest> Points { get; set; } = new();
    }
}

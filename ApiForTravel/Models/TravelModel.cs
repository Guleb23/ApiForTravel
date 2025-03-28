using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiForTravel.Models
{
    public class TravelModel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public string Title { get; set; }
        public DateTime Date { get; set; }
        public List<TravelPoint> Points { get; set; } = new List<TravelPoint>();
        public int UserId { get; set; } // Внешний ключ на пользователя
        [JsonIgnore]
        public UserModel User { get; set; } // Навигационное свойство
        [JsonIgnore]
        public List<string>? Tags { get; set; } = new List<string>();
        public int LikesCount { get; set; } = 0;

    }
}

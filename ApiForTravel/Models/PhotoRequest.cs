namespace ApiForTravel.Models
{
    public class PhotoRequest
    {
        public int Id { get; set; } // 0 для новых фото
        public string FileName { get; set; }
        public string Base64Content { get; set; } // Данные фото в base64
    }
}

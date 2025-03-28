namespace ApiForTravel.Models
{
    public class PointUpdateRequest
    {
        public int Id { get; set; } // 0 для новых точек
        public string? Name { get; set; }
        public string? Address { get; set; }
        public string? Type { get; set; }
        public string? DepartureTime { get; set; }
        public CoordinatesRequest? Coordinates { get; set; }
        public List<PhotoRequest>? Photos { get; set; }
    }
}

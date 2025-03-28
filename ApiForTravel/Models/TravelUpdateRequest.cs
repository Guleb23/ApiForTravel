namespace ApiForTravel.Models
{
    public class TravelUpdateRequest
    {
        public string? Title { get; set; }
        public string? Date { get; set; }
        public List<string>? Tags { get; set; }
        public List<PointUpdateRequest>? Points { get; set; }
    }
}

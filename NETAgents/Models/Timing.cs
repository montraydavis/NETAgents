namespace NETAgents.Models
{
    public record Timing
    {
        public double StartTime { get; init; }
        public double? EndTime { get; init; }
        public double? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;

        public Timing(double startTime, double? endTime = null)
        {
            StartTime = startTime;
            EndTime = endTime;
        }
    }
}
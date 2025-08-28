using NETAgents.Tools.Processing.Models;

namespace NETAgents.Tools.Processing.Services
{
    public interface IMultiLevelFileProcessorService : IDisposable
    {
        Task<JobProcessingResult> ProcessLevelAsync(MultiLevelProcessingJob job, JobProcessingLevel level, CancellationToken cancellationToken = default);
    }
}

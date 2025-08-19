using NETAgents.Tools.Processing.Models;

namespace NETAgents.Tools.Processing.Services;

public interface IMultiLevelFileProcessorService
{
    Task<ProcessingResult> ProcessLevelAsync(MultiLevelProcessingJob job, ProcessingLevel level, CancellationToken cancellationToken = default);
}

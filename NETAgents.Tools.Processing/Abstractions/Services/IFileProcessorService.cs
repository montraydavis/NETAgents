using NETAgents.Tools.Processing.Models;

namespace NETAgents.Tools.Processing.Services
{

    public interface IFileProcessorService
    {
        Task<string> ProcessFileAsync(FileProcessingJob job, CancellationToken cancellationToken = default);
    }
}

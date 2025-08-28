# NETAgents Processing Worker Service

A robust, efficient, and optimized processing queue system for handling code documentation generation.

## Features

### 🚀 **Robust Queue Architecture**

- **Bounded Channel Queue**: Uses `System.Threading.Channels` for high-performance, thread-safe queuing
- **Concurrent Processing**: Configurable number of background workers (default: 3)
- **Backpressure Handling**: Queue automatically handles overflow with wait semantics
- **Job Tracking**: Complete job lifecycle tracking with status, timing, and error handling

### 🔄 **Background Processing**

- **Multiple Workers**: Parallel processing with configurable worker count
- **Non-blocking Operations**: Async/await throughout the entire pipeline
- **Graceful Shutdown**: Proper cleanup and cancellation handling
- **Monitoring Loop**: Real-time queue statistics and health monitoring

### 📁 **File Discovery & Monitoring**

- **Initial Discovery**: Scans input directory for existing files on startup
- **File Watcher**: Real-time monitoring for new/modified files (optional)
- **Pattern Matching**: Configurable file patterns (default: `*.md`)
- **Duplicate Prevention**: Tracks processed files to avoid reprocessing

### 🛡️ **Error Handling & Resilience**

- **Retry Logic**: Configurable retry attempts with exponential backoff
- **Timeout Protection**: Per-job timeout handling to prevent hanging
- **Error Logging**: Comprehensive error tracking and logging
- **Graceful Degradation**: Continues processing even if individual jobs fail

### ⚙️ **Configuration Management**

- **AppSettings Integration**: All settings configurable via `appsettings.json`
- **Environment Variables**: Support for environment-based configuration
- **Hot Reload**: Configuration changes can be applied without restart

## Architecture

```markdown
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   File Discovery│    │  Processing Queue│    │ Background      │
│   Service       │───▶│  Service         │───▶│ Workers         │
└─────────────────┘    └──────────────────┘    └─────────────────┘
         │                       │                       │
         ▼                       ▼                       ▼
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   File Watcher  │    │  Job Tracking    │    │ AI Model        │
│   (Optional)    │    │  & Status        │    │ Processing      │
└─────────────────┘    └──────────────────┘    └─────────────────┘
```

## Configuration

### appsettings.json

```json
{
  "Processing": {
    "InputDirectory": "/path/to/input/files",
    "FilePattern": "*.md",
    "MaxConcurrentProcessing": 3,
    "MaxRetryAttempts": 3,
    "RetryDelay": "00:00:05",
    "ProcessingTimeout": "00:10:00",
    "EnableFileWatcher": true,
    "PollingInterval": "00:00:30"
  }
}
```

### Configuration Options

| Setting | Default | Description |
|---------|---------|-------------|
| `InputDirectory` | - | Directory to monitor for files |
| `FilePattern` | `*.md` | File pattern to process |
| `MaxConcurrentProcessing` | 3 | Number of parallel workers |
| `MaxRetryAttempts` | 3 | Maximum retry attempts per job |
| `RetryDelay` | 5s | Delay between retry attempts |
| `ProcessingTimeout` | 10m | Maximum processing time per job |
| `EnableFileWatcher` | true | Enable real-time file monitoring |
| `PollingInterval` | 30s | Monitoring loop interval |

## Job Processing Flow

1. **File Discovery**: Service scans input directory for matching files
2. **Job Creation**: Each file creates a `FileProcessingJob` with unique ID
3. **Queue Enqueue**: Job is added to the processing queue
4. **Worker Pickup**: Available worker dequeues and processes job
5. **AI Processing**: File content is processed using Azure OpenAI model
6. **Result Handling**: Success/failure is logged and job is completed
7. **Retry Logic**: Failed jobs are retried based on configuration

## Job Status Tracking

- **Pending**: Job created, waiting in queue
- **Processing**: Job picked up by worker
- **Completed**: Job processed successfully
- **Failed**: Job failed after all retry attempts
- **Retrying**: Job failed, scheduled for retry

## Monitoring & Logging

The service provides comprehensive logging:

- **Queue Statistics**: Pending jobs, active workers, processing rates
- **Job Lifecycle**: Creation, processing, completion, failures
- **Performance Metrics**: Processing times, throughput
- **Error Tracking**: Detailed error information with stack traces

## Usage

### Running the Service

```bash
cd NETAgents.Tools.Processing
dotnet run
```

### Adding Files for Processing

Simply place `.md` files in the configured input directory. The service will:

1. Automatically discover existing files on startup
2. Monitor for new files if file watcher is enabled
3. Process files in parallel using the queue system

### Stopping the Service

Press `Ctrl+C` for graceful shutdown. The service will:

1. Stop accepting new jobs
2. Complete in-progress jobs
3. Clean up resources
4. Shut down cleanly

## Performance Characteristics

- **High Throughput**: Parallel processing with configurable worker count
- **Low Latency**: Non-blocking queue operations
- **Memory Efficient**: Bounded queue prevents memory overflow
- **Scalable**: Easy to adjust worker count for different workloads

## Error Handling

The system handles various error scenarios:

- **Network Issues**: Retries with exponential backoff
- **API Timeouts**: Configurable timeout per job
- **File System Errors**: Graceful handling of file access issues
- **Memory Pressure**: Bounded queue prevents memory exhaustion
- **Worker Failures**: Automatic worker restart and job redistribution

## Dependencies

- **.NET 9.0**: Modern .NET runtime
- **System.Threading.Channels**: High-performance queuing
- **Microsoft.Extensions.Hosting**: Background service framework
- **NETAgents**: AI model integration
- **Azure OpenAI**: AI processing capabilities

## Development

### Building

```bash
dotnet build
```

### Testing

```bash
dotnet test
```

### Configuration

Modify `appsettings.json` or use environment variables for configuration.

## Production Deployment

For production deployment:

1. **Configuration**: Set appropriate values for your environment
2. **Logging**: Configure logging levels and outputs
3. **Monitoring**: Set up health checks and metrics collection
4. **Scaling**: Adjust worker count based on workload
5. **Security**: Ensure proper API key management

## Troubleshooting

### Common Issues

1. **Queue Not Processing**: Check worker count and API credentials
2. **High Memory Usage**: Reduce worker count or increase queue bounds
3. **Slow Processing**: Increase worker count or check API rate limits
4. **File Not Picked Up**: Verify file pattern and directory permissions

### Log Analysis

Monitor these log patterns:

- `Job {JobId} enqueued` - Job successfully added to queue
- `Worker {WorkerId} processing job {JobId}` - Job processing started
- `Job {JobId} completed successfully` - Job completed
- `Job {JobId} failed after {RetryCount} retries` - Job failed permanently

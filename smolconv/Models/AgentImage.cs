namespace SmolConv.Models
{
    public class AgentImage : AgentType
    {
        private object? _raw;
        private string? _path;
        private object? _tensor;

        public AgentImage(object value) : base(value)
        {
            if (value is AgentImage agentImage)
            {
                _raw = agentImage._raw;
                _path = agentImage._path;
                _tensor = agentImage._tensor;
            }
            else if (value is byte[] bytes)
            {
                _raw = bytes; // Store as raw bytes for cross-platform compatibility
            }
            else if (value is string path || value is FileInfo)
            {
                _path = value.ToString();
            }
            else
            {
                // Handle tensor types (would need ML.NET or similar for full tensor support)
                _tensor = value;
            }

            // Validate that we have some form of data
            if (_path == null && _raw == null && _tensor == null)
            {
                throw new ArgumentException($"Unsupported type for {nameof(AgentImage)}: {value?.GetType()}");
            }
        }

        public override object ToRaw()
        {
            if (_raw != null)
                return _raw;

            if (_path != null)
            {
                // For cross-platform compatibility, return the file path
                // In a full implementation, you'd load the image using a cross-platform library
                return _path;
            }

            if (_tensor != null)
            {
                // Convert tensor to image (simplified - would need proper tensor handling)
                return _tensor;
            }

            throw new InvalidOperationException("No valid image data available");
        }

        public override string ToString()
        {
            if (_path != null)
                return _path;

            if (_raw != null)
            {
                var tempDir = Path.GetTempPath();
                var fileName = $"{Guid.NewGuid()}.png";
                _path = Path.Combine(tempDir, fileName);
                
                // For cross-platform compatibility, just create a placeholder file
                // In a full implementation, you'd save the actual image data
                File.WriteAllBytes(_path, _raw as byte[] ?? new byte[0]);
                return _path;
            }

            if (_tensor != null)
            {
                // Convert tensor to image and save (simplified)
                var tempDir = Path.GetTempPath();
                var fileName = $"{Guid.NewGuid()}.png";
                _path = Path.Combine(tempDir, fileName);
                
                // This would need proper tensor to image conversion
                File.WriteAllBytes(_path, new byte[0]); // Placeholder
                return _path;
            }

            throw new InvalidOperationException("No valid image data available");
        }

        public void Save(Stream outputStream, string? format = null)
        {
            var raw = ToRaw();
            if (raw is byte[] bytes)
            {
                outputStream.Write(bytes, 0, bytes.Length);
            }
            else if (raw is string path && File.Exists(path))
            {
                var fileBytes = File.ReadAllBytes(path);
                outputStream.Write(fileBytes, 0, fileBytes.Length);
            }
        }
    }
}
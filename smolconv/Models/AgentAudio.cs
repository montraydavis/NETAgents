namespace SmolConv.Models
{
    public class AgentAudio : AgentType
    {
        private string? _path;
        private object? _tensor;
        public int SampleRate { get; set; }

        public AgentAudio(object value, int sampleRate = 16000) : base(value)
        {
            SampleRate = sampleRate;

            if (value is string path || value is FileInfo)
            {
                _path = value.ToString();
            }
            else if (value is ValueTuple<int, object> tuple)
            {
                SampleRate = tuple.Item1;
                _tensor = tuple.Item2;
            }
            else
            {
                _tensor = value;
            }
        }

        public override object ToRaw()
        {
            if (_tensor != null)
                return _tensor;

            if (_path != null)
            {
                // Load audio from path (simplified - would need proper audio library)
                if (_path.Contains("://"))
                {
                    // Handle URL
                    using HttpClient client = new HttpClient();
                    HttpResponseMessage response = client.GetAsync(_path).Result;
                    response.EnsureSuccessStatusCode();
                    byte[] audioData = response.Content.ReadAsByteArrayAsync().Result;
                    _tensor = LoadAudioFromBytes(audioData);
                }
                else
                {
                    // Handle local file
                    byte[] audioData = File.ReadAllBytes(_path);
                    _tensor = LoadAudioFromBytes(audioData);
                }
                return _tensor;
            }

            throw new InvalidOperationException("No valid audio data available");
        }

        public override string ToString()
        {
            if (_path != null)
                return _path;

            if (_tensor != null)
            {
                string tempDir = Path.GetTempPath();
                string fileName = $"{Guid.NewGuid()}.wav";
                _path = Path.Combine(tempDir, fileName);
                
                // Save audio to file (simplified - would need proper audio library)
                SaveAudioToFile(_tensor, _path);
                return _path;
            }

            throw new InvalidOperationException("No valid audio data available");
        }

        private object LoadAudioFromBytes(byte[] audioData)
        {
            // This would need a proper audio library like NAudio or similar
            // For now, return the raw bytes as a placeholder
            return audioData;
        }

        private void SaveAudioToFile(object tensor, string path)
        {
            // This would need a proper audio library like NAudio or similar
            // For now, just create an empty file as a placeholder
            File.WriteAllBytes(path, new byte[0]);
        }
    }
}
using SmolConv.Inference;
using SmolConv.Models;
using Xunit;

namespace SmolConv.Tests
{
    public class AzureOpenAIModelCacheTests
    {
        private readonly string _testEndpoint = "https://test-endpoint.openai.azure.com/";
        private readonly string _testApiKey = "test-api-key";
        private readonly string _testModelId = "gpt-4.1";

        [Fact]
        public void TestCacheDirectoryCreation()
        {
            // Arrange
            AzureOpenAIModel model = new AzureOpenAIModel(_testModelId, _testEndpoint, _testApiKey);
            
            // Act
            string cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "smolconv",
                ".cache"
            );
            
            // Assert
            Assert.True(Directory.Exists(cacheDir), "Cache directory should be created");
        }

        [Fact]
        public void TestCacheKeyGeneration()
        {
            // Arrange
            AzureOpenAIModel model = new AzureOpenAIModel(_testModelId, _testEndpoint, _testApiKey);
            List<ChatMessage> messages = new List<ChatMessage>
            {
                new ChatMessage(MessageRole.User, "Hello, how are you?")
            };
            
            // Act
            string cacheKey1 = model.GenerateCacheKey(messages, null);
            string cacheKey2 = model.GenerateCacheKey(messages, null);
            
            // Assert
            Assert.NotNull(cacheKey1);
            Assert.Equal(64, cacheKey1.Length); // SHA256 hash is 64 characters
            Assert.Equal(cacheKey1, cacheKey2); // Same input should produce same key
        }

        [Fact]
        public void TestCacheKeyUniqueness()
        {
            // Arrange
            AzureOpenAIModel model = new AzureOpenAIModel(_testModelId, _testEndpoint, _testApiKey);
            List<ChatMessage> messages1 = new List<ChatMessage>
            {
                new ChatMessage(MessageRole.User, "Hello")
            };
            List<ChatMessage> messages2 = new List<ChatMessage>
            {
                new ChatMessage(MessageRole.User, "Hello there")
            };
            
            // Act
            string cacheKey1 = model.GenerateCacheKey(messages1, null);
            string cacheKey2 = model.GenerateCacheKey(messages2, null);
            
            // Assert
            Assert.NotEqual(cacheKey1, cacheKey2); // Different inputs should produce different keys
        }

        [Fact]
        public void TestCacheStats()
        {
            // Arrange
            AzureOpenAIModel model = new AzureOpenAIModel(_testModelId, _testEndpoint, _testApiKey);
            
            // Act
            Dictionary<string, object> stats = model.GetCacheStats();
            
            // Assert
            Assert.NotNull(stats);
            Assert.True(stats.ContainsKey("file_count"));
            Assert.True(stats.ContainsKey("total_size_bytes"));
            Assert.True(stats.ContainsKey("total_size_mb"));
            Assert.True(stats.ContainsKey("cache_directory"));
        }

        [Fact]
        public void TestCacheClear()
        {
            // Arrange
            AzureOpenAIModel model = new AzureOpenAIModel(_testModelId, _testEndpoint, _testApiKey);
            
            // Act
            model.ClearCache();
            Dictionary<string, object> stats = model.GetCacheStats();
            
            // Assert
            Assert.Equal(0, Convert.ToInt32(stats["file_count"]));
        }

        [Fact]
        public void TestCacheCleanup()
        {
            // Arrange
            AzureOpenAIModel model = new AzureOpenAIModel(_testModelId, _testEndpoint, _testApiKey);
            
            // Act
            model.CleanupExpiredCache();
            
            // Assert
            // Should not throw any exceptions
            Assert.True(true);
        }

        [Fact]
        public void TestToDictIncludesCacheInfo()
        {
            // Arrange
            AzureOpenAIModel model = new AzureOpenAIModel(_testModelId, _testEndpoint, _testApiKey);
            
            // Act
            Dictionary<string, object> dict = model.ToDict();
            
            // Assert
            Assert.True(dict.ContainsKey("cache_directory"));
            Assert.True(dict.ContainsKey("cache_enabled"));
            Assert.True(Convert.ToBoolean(dict["cache_enabled"]));
        }
    }
}

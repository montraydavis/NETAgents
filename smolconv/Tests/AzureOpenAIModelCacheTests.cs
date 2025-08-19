using SmolConv.Inference;
using SmolConv.Models;
using SmolConv.Tools;
using System.Text.Json;
using Xunit;

namespace SmolConv.Tests
{
    public class AzureOpenAIModelCacheTests
    {
        private readonly string _testEndpoint = "https://test-endpoint.openai.azure.com/";
        private readonly string _testApiKey = "test-api-key";
        private readonly string _testModelId = "gpt-4.1-nano";

        [Fact]
        public void TestCacheDirectoryCreation()
        {
            // Arrange
            var model = new AzureOpenAIModel(_testModelId, _testEndpoint, _testApiKey);
            
            // Act
            var cacheDir = Path.Combine(
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
            var model = new AzureOpenAIModel(_testModelId, _testEndpoint, _testApiKey);
            var messages = new List<ChatMessage>
            {
                new ChatMessage(MessageRole.User, "Hello, how are you?")
            };
            
            // Act
            var cacheKey1 = model.GenerateCacheKey(messages, null);
            var cacheKey2 = model.GenerateCacheKey(messages, null);
            
            // Assert
            Assert.NotNull(cacheKey1);
            Assert.Equal(64, cacheKey1.Length); // SHA256 hash is 64 characters
            Assert.Equal(cacheKey1, cacheKey2); // Same input should produce same key
        }

        [Fact]
        public void TestCacheKeyUniqueness()
        {
            // Arrange
            var model = new AzureOpenAIModel(_testModelId, _testEndpoint, _testApiKey);
            var messages1 = new List<ChatMessage>
            {
                new ChatMessage(MessageRole.User, "Hello")
            };
            var messages2 = new List<ChatMessage>
            {
                new ChatMessage(MessageRole.User, "Hello there")
            };
            
            // Act
            var cacheKey1 = model.GenerateCacheKey(messages1, null);
            var cacheKey2 = model.GenerateCacheKey(messages2, null);
            
            // Assert
            Assert.NotEqual(cacheKey1, cacheKey2); // Different inputs should produce different keys
        }

        [Fact]
        public void TestCacheStats()
        {
            // Arrange
            var model = new AzureOpenAIModel(_testModelId, _testEndpoint, _testApiKey);
            
            // Act
            var stats = model.GetCacheStats();
            
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
            var model = new AzureOpenAIModel(_testModelId, _testEndpoint, _testApiKey);
            
            // Act
            model.ClearCache();
            var stats = model.GetCacheStats();
            
            // Assert
            Assert.Equal(0, Convert.ToInt32(stats["file_count"]));
        }

        [Fact]
        public void TestCacheCleanup()
        {
            // Arrange
            var model = new AzureOpenAIModel(_testModelId, _testEndpoint, _testApiKey);
            
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
            var model = new AzureOpenAIModel(_testModelId, _testEndpoint, _testApiKey);
            
            // Act
            var dict = model.ToDict();
            
            // Assert
            Assert.True(dict.ContainsKey("cache_directory"));
            Assert.True(dict.ContainsKey("cache_enabled"));
            Assert.True(Convert.ToBoolean(dict["cache_enabled"]));
        }
    }
}

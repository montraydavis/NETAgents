using Xunit;
using SmolConv.Models;

namespace SmolConv.Tests
{
    // Test implementation of AgentType for testing abstract behavior
    public class TestAgentType : AgentType
    {
        public TestAgentType(object value) : base(value) { }
        
        public override object ToRaw() => ToRawDefault();
        public override string ToString() => ToStringDefault();
    }

    public class AgentTypeTests
    {
        [Fact]
        public void AgentText_ShouldBehaveLikeString()
        {
            // Test basic string behavior
            var agentText = new AgentText("Hello World");
            Assert.Equal("Hello World", agentText.ToString());
            Assert.Equal("Hello World", agentText.ToRaw());
            
            // Test implicit conversion
            string str = agentText;
            Assert.Equal("Hello World", str);
        }

        [Fact]
        public void AgentImage_ShouldHandleDifferentInputTypes()
        {
            // Test with file path
            var tempPath = Path.GetTempFileName() + ".png";
            File.WriteAllBytes(tempPath, new byte[100]); // Create dummy file
            
            var agentImage = new AgentImage(tempPath);
            Assert.Equal(tempPath, agentImage.ToString());
            
            // Test with byte array
            var imageBytes = new byte[100];
            var agentImage2 = new AgentImage(imageBytes);
            Assert.NotNull(agentImage2.ToRaw());
            
            // Cleanup
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        [Fact]
        public void AgentAudio_ShouldHandleSampleRate()
        {
            var agentAudio = new AgentAudio("test.wav", 44100);
            Assert.Equal(44100, agentAudio.SampleRate);
            
            var agentAudio2 = new AgentAudio(new ValueTuple<int, object>(22050, new byte[100]));
            Assert.Equal(22050, agentAudio2.SampleRate);
        }

        [Fact]
        public void AgentTypeMapping_ShouldHandleOutputTypes()
        {
            // Test string mapping
            var result = AgentTypeMapping.HandleAgentOutputTypes("test", "string");
            Assert.IsType<AgentText>(result);
            
            // Test image mapping with byte array
            var imageBytes = new byte[100];
            var result2 = AgentTypeMapping.HandleAgentOutputTypes(imageBytes, "image");
            Assert.IsType<AgentImage>(result2);
            
            // Test automatic type detection
            var result3 = AgentTypeMapping.HandleAgentOutputTypes("auto detect");
            Assert.IsType<AgentText>(result3);
        }

        [Fact]
        public void AgentTypeMapping_ShouldHandleInputTypes()
        {
            var agentText = new AgentText("test");
            var agentImage = new AgentImage("test.png");
            
            var (args, kwargs) = AgentTypeMapping.HandleAgentInputTypes(agentText, agentImage, "regular string");
            
            Assert.Equal(3, args.Length);
            Assert.Equal("test", args[0]);
            Assert.Equal("test.png", args[1]);
            Assert.Equal("regular string", args[2]);
        }

        [Fact]
        public void ActionOutput_ShouldIntegrateWithAgentTypes()
        {
            // Test with typed output
            var agentText = new AgentText("Hello");
            var actionOutput = new ActionOutput(agentText, false);
            
            Assert.Equal("Hello", actionOutput.GetRawOutput());
            Assert.Equal("Hello", actionOutput.GetStringOutput());
            Assert.Equal("text", actionOutput.OutputType);
            
            // Test with automatic conversion
            var actionOutput2 = new ActionOutput("World", false, "string");
            Assert.IsType<AgentText>(actionOutput2.TypedOutput);
            Assert.Equal("World", actionOutput2.GetRawOutput());
        }

        [Fact]
        public void AgentType_ShouldHandleUnknownTypes()
        {
            // Test that unknown types are handled gracefully
            var unknownType = new TestAgentType(new { test = "value" });
            
            // Should not throw, but should log warning
            var raw = unknownType.ToRaw();
            var str = unknownType.ToString();
            
            Assert.NotNull(raw);
            Assert.NotNull(str);
        }
    }
}

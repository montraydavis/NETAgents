using NETAgents.Core;
using NETAgents.Core.Validation;
using NETAgents.Models;
using NETAgents.Tools;
using Xunit;

namespace NETAgents.Tests
{
    /// <summary>
    /// Tests for Phase 2: State Management implementation
    /// </summary>
    public class Phase2StateManagementTests
    {
        private readonly ToolCallingAgent _agent;
        private readonly Dictionary<string, object> _testState;

        public Phase2StateManagementTests()
        {
            // Setup test agent with mock model
            MockModel mockModel = new MockModel();
            List<Tool> tools = new List<Tool>();
            _agent = new ToolCallingAgent(tools, mockModel);
            
            // Setup test state
            _testState = new Dictionary<string, object>
            {
                ["user_name"] = "John Doe",
                ["user_age"] = 30,
                ["user_city"] = "New York",
                ["user_preferences"] = new Dictionary<string, object>
                {
                    ["theme"] = "dark",
                    ["language"] = "en"
                },
                ["user_hobbies"] = new List<object> { "reading", "swimming", "coding" }
            };

            // Set state in agent
            foreach (KeyValuePair<string, object> kvp in _testState)
            {
                _agent.State[kvp.Key] = kvp.Value;
            }
        }

        [Fact]
        public void SubstituteStateVariables_WithSimpleString_ShouldSubstitute()
        {
            // Arrange
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                ["name"] = "user_name",
                ["age"] = "user_age"
            };

            // Act
            Dictionary<string, object>? result = _agent.SubstituteStateVariables(arguments) as Dictionary<string, object>;

            // Assert
            Assert.NotNull(result);
            Assert.Equal("John Doe", result["name"]);
            Assert.Equal(30, result["age"]);
        }

        [Fact]
        public void SubstituteStateVariables_WithNestedDictionary_ShouldSubstituteRecursively()
        {
            // Arrange
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                ["user"] = new Dictionary<string, object>
                {
                    ["name"] = "user_name",
                    ["preferences"] = "user_preferences"
                }
            };

            // Act
            Dictionary<string, object>? result = _agent.SubstituteStateVariables(arguments) as Dictionary<string, object>;

            // Assert
            Assert.NotNull(result);
            Dictionary<string, object>? user = result["user"] as Dictionary<string, object>;
            Assert.NotNull(user);
            Assert.Equal("John Doe", user["name"]);
            
            Dictionary<string, object>? preferences = user["preferences"] as Dictionary<string, object>;
            Assert.NotNull(preferences);
            Assert.Equal("dark", preferences["theme"]);
            Assert.Equal("en", preferences["language"]);
        }

        [Fact]
        public void SubstituteStateVariables_WithArray_ShouldSubstituteElements()
        {
            // Arrange
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                ["hobbies"] = "user_hobbies",
                ["mixed_array"] = new List<object> { "user_name", "user_age", "non_state_value" }
            };

            // Act
            Dictionary<string, object>? result = _agent.SubstituteStateVariables(arguments) as Dictionary<string, object>;

            // Assert
            Assert.NotNull(result);
            
            List<object>? hobbies = result["hobbies"] as List<object>;
            Assert.NotNull(hobbies);
            Assert.Contains("reading", hobbies);
            Assert.Contains("swimming", hobbies);
            Assert.Contains("coding", hobbies);

            List<object>? mixedArray = result["mixed_array"] as List<object>;
            Assert.NotNull(mixedArray);
            Assert.Equal("John Doe", mixedArray[0]);
            Assert.Equal(30, mixedArray[1]);
            Assert.Equal("non_state_value", mixedArray[2]);
        }

        [Fact]
        public void ValidateStateVariables_WithValidReferences_ShouldNotThrow()
        {
            // Arrange
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                ["name"] = "user_name",
                ["age"] = "user_age"
            };

            // Act & Assert
            Exception? exception = Record.Exception(() => _agent.ValidateStateVariables(arguments));
            Assert.Null(exception);
        }

        [Fact]
        public void ValidateStateVariables_WithMissingReferences_ShouldThrowArgumentException()
        {
            // Arrange
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                ["name"] = "user_name",
                ["missing_var"] = "non_existent_state_var"
            };

            // Act & Assert
            ArgumentException exception = Assert.Throws<ArgumentException>(() => 
                _agent.ValidateStateVariables(arguments));
            
            Assert.Contains("non_existent_state_var", exception.Message);
        }

        [Fact]
        public void ValidateStateVariables_WithNestedMissingReferences_ShouldThrowArgumentException()
        {
            // Arrange
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                ["user"] = new Dictionary<string, object>
                {
                    ["name"] = "user_name",
                    ["missing"] = "non_existent_state_var"
                }
            };

            // Act & Assert
            ArgumentException exception = Assert.Throws<ArgumentException>(() => 
                _agent.ValidateStateVariables(arguments));
            
            Assert.Contains("non_existent_state_var", exception.Message);
        }

        [Fact]
        public void NullableParameterHandler_WithNullableParameter_ShouldAllowNull()
        {
            // Arrange
            Dictionary<string, object?> arguments = new Dictionary<string, object?>
            {
                ["required_param"] = "value"
                // nullable_param is missing (null)
            };

            Dictionary<string, Dictionary<string, object>> inputs = new Dictionary<string, Dictionary<string, object>>
            {
                ["required_param"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["nullable"] = false
                },
                ["nullable_param"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["nullable"] = true
                }
            };

            // Act & Assert
            Exception? exception = Record.Exception(() => 
                NullableParameterHandler.ValidateNullableParameters(arguments, inputs));
            Assert.Null(exception);
        }

        [Fact]
        public void NullableParameterHandler_WithNonNullableParameter_ShouldThrowOnNull()
        {
            // Arrange
            Dictionary<string, object?> arguments = new Dictionary<string, object?>
            {
                ["required_param"] = null
            };

            Dictionary<string, Dictionary<string, object>> inputs = new Dictionary<string, Dictionary<string, object>>
            {
                ["required_param"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["nullable"] = false
                }
            };

            // Act & Assert
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => 
                NullableParameterHandler.ValidateNullableParameters(arguments, inputs));
            
            Assert.Equal("required_param", exception.ParamName);
        }

        [Fact]
        public void NullableParameterHandler_WithOptionalParameter_ShouldAllowMissing()
        {
            // Arrange
            Dictionary<string, object?> arguments = new Dictionary<string, object?>
            {
                ["required_param"] = "value"
                // optional_param is missing
            };

            Dictionary<string, Dictionary<string, object>> inputs = new Dictionary<string, Dictionary<string, object>>
            {
                ["required_param"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["nullable"] = false
                },
                ["optional_param"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["optional"] = true
                }
            };

            // Act & Assert
            Exception? exception = Record.Exception(() => 
                NullableParameterHandler.ValidateNullableParameters(arguments, inputs));
            Assert.Null(exception);
        }

        [Fact]
        public void NullableParameterHandler_WithRequiredParameter_ShouldThrowOnMissing()
        {
            // Arrange
            Dictionary<string, object?> arguments = new Dictionary<string, object?>
            {
                ["provided_param"] = "value"
                // required_param is missing
            };

            Dictionary<string, Dictionary<string, object>> inputs = new Dictionary<string, Dictionary<string, object>>
            {
                ["provided_param"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["nullable"] = false
                },
                ["required_param"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["nullable"] = false
                }
            };

            // Act & Assert
            ArgumentException exception = Assert.Throws<ArgumentException>(() => 
                NullableParameterHandler.ValidateNullableParameters(arguments, inputs));
            
            Assert.Contains("required_param", exception.Message);
        }

        [Fact]
        public void IsNullable_WithNullableSchema_ShouldReturnTrue()
        {
            // Arrange
            Dictionary<string, object> schema = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["nullable"] = true
            };

            // Act
            bool result = NullableParameterHandler.IsNullable(schema);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsNullable_WithNonNullableSchema_ShouldReturnFalse()
        {
            // Arrange
            Dictionary<string, object> schema = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["nullable"] = false
            };

            // Act
            bool result = NullableParameterHandler.IsNullable(schema);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsOptional_WithOptionalSchema_ShouldReturnTrue()
        {
            // Arrange
            Dictionary<string, object> schema = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["optional"] = true
            };

            // Act
            bool result = NullableParameterHandler.IsOptional(schema);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsOptional_WithRequiredSchema_ShouldReturnFalse()
        {
            // Arrange
            Dictionary<string, object> schema = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["optional"] = false
            };

            // Act
            bool result = NullableParameterHandler.IsOptional(schema);

            // Assert
            Assert.False(result);
        }
    }

    /// <summary>
    /// Mock model for testing
    /// </summary>
    public class MockModel : Model
    {
        public MockModel() : base(modelId: "mock-model")
        {
        }

        public override Task<LLMMessage> Generate(List<LLMMessage> messages, ModelCompletionOptions? options = null)
        {
            LLMMessage? lastMessage = messages.LastOrDefault();
            string content = lastMessage?.Content?.ToString() ?? "";
            
            // Return appropriate mock response based on the prompt content
            string response;
            if (content.Contains("domain keywords") || content.Contains("DomainKeywords"))
            {
                response = @"{
  ""domains"": [
    {
      ""name"": ""software-development"",
      ""reasoning"": ""This file contains code analysis and processing logic typical of software development tools.""
    },
    {
      ""name"": ""code-analysis"",
      ""reasoning"": ""The content involves parsing, processing, and analyzing source code files.""
    }
  ]
}";
            }
            else if (content.Contains("AST") || content.Contains("Abstract Syntax Tree"))
            {
                response = @"{
  ""namespace"": ""MockNamespace"",
  ""usings"": [""System"", ""System.Collections.Generic""],
  ""classes"": [
    {
      ""name"": ""MockClass"",
      ""modifiers"": [""public""],
      ""baseTypes"": [],
      ""attributes"": []
    }
  ],
  ""interfaces"": [],
  ""enums"": []
}";
            }
            else
            {
                response = "Mock response";
            }
            
            return Task.FromResult(new LLMMessage(MessageRole.Assistant, response, response));
        }

        public override LLMMessage ParseToolCalls(LLMMessage message)
        {
            return message;
        }
    }
}

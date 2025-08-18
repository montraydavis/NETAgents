namespace SmolConv.Core
{
    /// <summary>
    /// Base class for pipeline-based tools (transformers, ML models, etc.)
    /// </summary>
    public abstract class PipelineTool : Tool
    {
        /// <summary>
        /// Gets or sets the model checkpoint or instance
        /// </summary>
        protected object? Model { get; set; }

        /// <summary>
        /// Gets or sets the preprocessor
        /// </summary>
        protected object? PreProcessor { get; set; }

        /// <summary>
        /// Gets or sets the postprocessor
        /// </summary>
        protected object? PostProcessor { get; set; }

        /// <summary>
        /// Gets or sets the device for execution
        /// </summary>
        protected string? Device { get; set; }

        /// <summary>
        /// Gets or sets additional model arguments
        /// </summary>
        protected Dictionary<string, object> ModelKwargs { get; set; } = new();

        /// <summary>
        /// Gets the default checkpoint for this pipeline tool
        /// </summary>
        protected abstract string? DefaultCheckpoint { get; }

        /// <summary>
        /// Initializes a new instance of the PipelineTool class
        /// </summary>
        /// <param name="model">Model checkpoint or instance</param>
        /// <param name="preProcessor">Preprocessor instance</param>
        /// <param name="postProcessor">Postprocessor instance</param>
        /// <param name="device">Execution device</param>
        /// <param name="modelKwargs">Additional model arguments</param>
        protected PipelineTool(object? model = null, object? preProcessor = null, object? postProcessor = null,
                              string? device = null, Dictionary<string, object>? modelKwargs = null)
        {
            Model = model ?? DefaultCheckpoint ?? throw new InvalidOperationException("No model provided and no default checkpoint available");
            PreProcessor = preProcessor ?? Model;
            PostProcessor = postProcessor;
            Device = device;
            ModelKwargs = modelKwargs ?? new Dictionary<string, object>();
        }

        /// <summary>
        /// Setup method for loading models and preprocessors
        /// </summary>
        protected override void Setup()
        {
            LoadModel();
            LoadPreProcessor();
            LoadPostProcessor();
            ConfigureDevice();
            base.Setup();
        }

        /// <summary>
        /// Loads the model if it's a string path/identifier
        /// </summary>
        protected virtual void LoadModel()
        {
            // Subclasses should implement model loading logic
            // For now, assume model is already loaded or is a string identifier
        }

        /// <summary>
        /// Loads the preprocessor
        /// </summary>
        protected virtual void LoadPreProcessor()
        {
            // Subclasses should implement preprocessor loading logic
        }

        /// <summary>
        /// Loads the postprocessor
        /// </summary>
        protected virtual void LoadPostProcessor()
        {
            PostProcessor ??= PreProcessor;
        }

        /// <summary>
        /// Configures the execution device
        /// </summary>
        protected virtual void ConfigureDevice()
        {
            // Default device configuration logic
            Device ??= "cpu"; // Default to CPU if no device specified
        }

        /// <summary>
        /// Encodes inputs using the preprocessor
        /// </summary>
        /// <param name="rawInputs">Raw input data</param>
        /// <returns>Encoded inputs</returns>
        protected virtual object Encode(object rawInputs)
        {
            // Subclasses should implement encoding logic
            return rawInputs;
        }

        /// <summary>
        /// Runs the model on encoded inputs
        /// </summary>
        /// <param name="inputs">Encoded inputs</param>
        /// <returns>Model outputs</returns>
        protected virtual object RunModel(object inputs)
        {
            // Subclasses should implement model execution logic
            throw new NotImplementedException("RunModel must be implemented by subclasses");
        }

        /// <summary>
        /// Decodes model outputs using the postprocessor
        /// </summary>
        /// <param name="outputs">Model outputs</param>
        /// <returns>Decoded outputs</returns>
        protected virtual object Decode(object outputs)
        {
            // Subclasses should implement decoding logic
            return outputs;
        }

        /// <summary>
        /// Main execution pipeline
        /// </summary>
        /// <param name="args">Positional arguments</param>
        /// <param name="kwargs">Named arguments</param>
        /// <returns>Pipeline output</returns>
        protected override object? Forward(object?[]? args, Dictionary<string, object>? kwargs)
        {
            // Extract input data
            var inputData = ExtractInputData(args, kwargs);

            // Encode inputs
            var encodedInputs = Encode(inputData);

            // Run model
            var modelOutputs = RunModel(encodedInputs);

            // Decode outputs
            var decodedOutputs = Decode(modelOutputs);

            return decodedOutputs;
        }

        /// <summary>
        /// Extracts input data from arguments
        /// </summary>
        /// <param name="args">Positional arguments</param>
        /// <param name="kwargs">Named arguments</param>
        /// <returns>Extracted input data</returns>
        protected virtual object ExtractInputData(object?[]? args, Dictionary<string, object>? kwargs)
        {
            // Default implementation: return first arg or first kwarg value
            if (args?.Length > 0)
                return args[0] ?? throw new ArgumentException("Input data cannot be null");

            if (kwargs?.Count > 0)
                return kwargs.Values.First();

            throw new ArgumentException("No input data provided");
        }
    }
}
using System.Diagnostics;
using Microsoft.Build.Locator;

namespace SmolConv
{
    public static class InitMSBuild
    {

        private static bool _msbuildLocated;
        public static void EnsureMSBuildLocated()
        {
            if (_msbuildLocated)
            {
                return;
            }

            try
            {
                // Check if MSBuildLocator has already been registered
                if (MSBuildLocator.IsRegistered)
                {
                    Console.WriteLine("MSBuild is already registered");
                    _msbuildLocated = true;
                    return;
                }

                try
                {
                    // Check if MSBuildLocator has already been registered
                    if (MSBuildLocator.IsRegistered)
                    {
                        Console.WriteLine("MSBuild is already registered");
                        _msbuildLocated = true;
                        return;
                    }

                    // Try to register MSBuild using MSBuildLocator
                    VisualStudioInstance[] instances = [.. MSBuildLocator.QueryVisualStudioInstances()];

                    if (instances.Length > 0)
                    {
                        // Use the latest version found
                        VisualStudioInstance latestInstance = instances.OrderByDescending(i => i.Version).First();
                        Console.WriteLine($"Registering MSBuild from: {latestInstance.MSBuildPath} (Version: {latestInstance.Version})");
                        MSBuildLocator.RegisterInstance(latestInstance);
                        _msbuildLocated = true;
                        return;
                    }

                    // If no Visual Studio instances found, try to register defaults
                    Console.WriteLine("No Visual Studio instances found, trying to register defaults");
                    MSBuildLocator.RegisterDefaults();
                    _msbuildLocated = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not register MSBuild with MSBuildLocator: {ex.Message}");

                    // Fall back to manual MSBuild location
                    try
                    {
                        EnsureMSBuildLocatedManually();
                    }
                    catch (Exception fallbackEx)
                    {
                        Console.WriteLine($"Warning: Manual MSBuild location also failed: {fallbackEx.Message}");
                        _msbuildLocated = true; // Don't keep trying
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not register MSBuild with MSBuildLocator: {ex.Message}");
            }
        }

        private static void EnsureMSBuildLocatedManually()
        {
            // Try to find and set MSBuild path manually
            List<string> possibleMSBuildPaths =
                [
                    @"C:\Program Files\dotnet\sdk",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin"
                ];

            foreach (string path in possibleMSBuildPaths)
            {
                if (Directory.Exists(path))
                {
                    Console.WriteLine($"Found MSBuild at: {path}");
                    Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", Path.Combine(path, "MSBuild.exe"));
                    break;
                }
            }

            // Try to find .NET SDK
            try
            {
                ProcessStartInfo processInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "--list-sdks",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process? process = Process.Start(processInfo);
                if (process == null)
                {
                    Console.WriteLine("Warning: Could not start dotnet process");
                    return;
                }
                process.WaitForExit();
                string output = process.StandardOutput.ReadToEnd();

                Console.WriteLine($"Found .NET SDKs: {output.Trim().Replace('\n', ' ')}");

                // Extract the latest SDK version and set MSBUILD_EXE_PATH
                string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0)
                {
                    string? lastLine = lines[lines.Length - 1];
                    if (lastLine != null)
                    {
                        int bracketIndex = lastLine.IndexOf('[');
                        if (bracketIndex > 0)
                        {
                            string sdkPath = lastLine.Substring(bracketIndex + 1).Replace("]", "").Trim();
                            string version = lastLine.Substring(0, bracketIndex).Trim();
                            string msbuildPath = Path.Combine(sdkPath, version, "MSBuild.dll");

                            if (File.Exists(msbuildPath))
                            {
                                Console.WriteLine($"Set MSBUILD_EXE_PATH to: {msbuildPath}");
                                Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", msbuildPath);
                            }
                        }
                    }
                }

                _msbuildLocated = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not locate MSBuild: {ex.Message}");
                _msbuildLocated = true; // Don't keep trying
            }
        }

    }
}

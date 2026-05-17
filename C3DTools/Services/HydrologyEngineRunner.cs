using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace C3DTools.Services
{
    public class HydrologyEngineRunner
    {
        public HydrologyRunResult Run(
            string modelPath,
            string resultsPath,
            string? excelPath = null,
            string? validationPath = null,
            string? atlas14DepthsPath = null,
            string atlas14Duration = "24-hr",
            string stormDistribution = "ATLAS14_ALTERNATING_BLOCK",
            int hydrographTimeStepMinutes = 2)
        {
            string engineRoot = FindEngineRoot();
            string pythonExecutable = ResolvePythonExecutable(engineRoot);
            validationPath ??= Path.Combine(Path.GetDirectoryName(resultsPath) ?? Environment.CurrentDirectory, "validation_report.json");
            string arguments = BuildArguments(modelPath, resultsPath, excelPath, validationPath, atlas14DepthsPath, atlas14Duration, stormDistribution, hydrographTimeStepMinutes);

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                Arguments = arguments,
                WorkingDirectory = engineRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Python hydrology engine.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new HydrologyRunResult(process.ExitCode, output, error, resultsPath, validationPath, excelPath);
        }

        private static string FindEngineRoot()
        {
            string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
            foreach (string startDirectory in new[] { assemblyDirectory, AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                string? current = startDirectory;
                for (int i = 0; i < 10 && !string.IsNullOrWhiteSpace(current); i++)
                {
                    string candidate = Path.Combine(current, "python-engine");
                    if (Directory.Exists(candidate))
                        return candidate;

                    current = Directory.GetParent(current)?.FullName;
                }
            }

            string workspaceCandidate = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", "python-engine"));
            if (Directory.Exists(workspaceCandidate))
                return workspaceCandidate;

            throw new DirectoryNotFoundException("Could not locate the python-engine directory.");
        }

        private static string ResolvePythonExecutable(string engineRoot)
        {
            string? configuredPython = Environment.GetEnvironmentVariable("C3DTOOLS_PYTHON");
            if (!string.IsNullOrWhiteSpace(configuredPython))
                return configuredPython;

            string? workspaceRoot = Directory.GetParent(engineRoot)?.FullName;
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(workspaceRoot))
                candidates.Add(Path.Combine(workspaceRoot, ".venv", "Scripts", "python.exe"));

            candidates.Add(Path.Combine(engineRoot, ".venv", "Scripts", "python.exe"));

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return "python";
        }

        private static string BuildArguments(
            string modelPath,
            string resultsPath,
            string? excelPath,
            string validationPath,
            string? atlas14DepthsPath,
            string atlas14Duration,
            string stormDistribution,
            int hydrographTimeStepMinutes)
        {
            var args = new List<string>
            {
                "-m",
                "hydro_app.cli",
                "run",
                Quote(modelPath),
                "--out",
                Quote(resultsPath),
                "--validation",
                Quote(validationPath),
                "--time-step-minutes",
                Math.Max(hydrographTimeStepMinutes, 1).ToString()
            };

            if (!string.IsNullOrWhiteSpace(excelPath))
            {
                args.Add("--excel");
                args.Add(Quote(excelPath));
            }

            if (!string.IsNullOrWhiteSpace(atlas14DepthsPath))
            {
                args.Add("--atlas14-depths");
                args.Add(Quote(atlas14DepthsPath));
                args.Add("--atlas14-duration");
                args.Add(Quote(string.IsNullOrWhiteSpace(atlas14Duration) ? "24-hr" : atlas14Duration));
                args.Add("--distribution");
                args.Add(Quote(string.IsNullOrWhiteSpace(stormDistribution) ? "ATLAS14_ALTERNATING_BLOCK" : stormDistribution));
            }

            return string.Join(" ", args);
        }

        private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
    }

    public sealed record HydrologyRunResult(int ExitCode, string Output, string Error, string ResultsPath, string ValidationPath, string? ExcelPath);
}
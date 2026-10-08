using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;

namespace Realm.Shared.Animation;

public class GlbAutoRiggerOptions
{
    public bool NoFingers { get; set; } = true;
    public bool UseNormals { get; set; } = true;
    public bool WeightPostprocess { get; set; } = true;
    public Action<string>? LogCallback { get; set; }
}

public class GlbAutoRiggerResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OutputPath { get; set; }
}

public static class GlbAutoRigger
{
    private static readonly Mutex RiggingMutex = CreateGlobalMutex();

    private static Mutex CreateGlobalMutex()
    {
        try
        {
            return new Mutex(false, @"Global\Realm_GlbAutoRigger_Mutex");
        }
        catch
        {
            return new Mutex(false, @"Local\Realm_GlbAutoRigger_Mutex");
        }
    }

    public static GlbAutoRiggerResult RigHumanoid(
        string inputPath,
        string outputPath,
        GlbAutoRiggerOptions? options = null)
    {
        options ??= new GlbAutoRiggerOptions();

        void Log(string msg)
        {
            options.LogCallback?.Invoke(msg);
            Console.WriteLine(msg);
        }

        if (!File.Exists(inputPath))
        {
            return new GlbAutoRiggerResult
            {
                Success = false,
                ErrorMessage = $"Input file does not exist: {inputPath}"
            };
        }

        string fullInputPath = Path.GetFullPath(inputPath);
        string fullOutputPath = Path.GetFullPath(outputPath);

        bool hasLock = AcquireRiggingLock();
        if (!hasLock)
        {
            return new GlbAutoRiggerResult
            {
                Success = false,
                ErrorMessage = "Timed out waiting for Make-It-Animatable GPU lock."
            };
        }

        try
        {
            return ExecuteRiggingPipeline(fullInputPath, fullOutputPath, options, Log);
        }
        catch (Exception ex)
        {
            return new GlbAutoRiggerResult
            {
                Success = false,
                ErrorMessage = $"Auto-rigging exception: {ex.Message}"
            };
        }
        finally
        {
            ReleaseRiggingLock();
        }
    }

    private static bool AcquireRiggingLock()
    {
        try
        {
            return RiggingMutex.WaitOne(TimeSpan.FromMinutes(15));
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static void ReleaseRiggingLock()
    {
        try { RiggingMutex.ReleaseMutex(); } catch { }
    }

    private static GlbAutoRiggerResult ExecuteRiggingPipeline(
        string fullInputPath,
        string fullOutputPath,
        GlbAutoRiggerOptions options,
        Action<string> log)
    {
        try
        {
            MakeItAnimatableSetup.EnsureSetup(options.LogCallback);
        }
        catch (Exception ex)
        {
            return new GlbAutoRiggerResult
            {
                Success = false,
                ErrorMessage = $"Make-It-Animatable setup failed: {ex.Message}"
            };
        }

        EnsureDirectoryExists(fullOutputPath);

        var optimizer = new GlbOptimizer();
        byte[] sourceBytes = File.ReadAllBytes(fullInputPath);
        bool wasOptimized = optimizer.IsOptimized(sourceBytes);

        string tempJobDir = Path.Combine(Path.GetTempPath(), $"realm_rig_job_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempJobDir);
        string rigSourcePath = Path.Combine(tempJobDir, "input_for_rigging.glb");

        try
        {
            var prepResult = PrepareRigSource(fullInputPath, rigSourcePath, sourceBytes, wasOptimized, optimizer, log);
            if (!prepResult.Success)
            {
                return prepResult;
            }

            var rigResult = RunRiggingProcess(fullInputPath, fullOutputPath, rigSourcePath, options, log);
            if (!rigResult.Success)
            {
                return rigResult;
            }
        }
        finally
        {
            CleanupTempDir(tempJobDir);
        }

        return ReoptimizeOutput(fullOutputPath, optimizer, log);
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static GlbAutoRiggerResult PrepareRigSource(
        string fullInputPath,
        string rigSourcePath,
        byte[] sourceBytes,
        bool wasOptimized,
        GlbOptimizer optimizer,
        Action<string> log)
    {
        if (!wasOptimized)
        {
            File.Copy(fullInputPath, rigSourcePath, true);
            return new GlbAutoRiggerResult { Success = true };
        }

        log("  Detected pre-optimized GLB — unoptimizing first to restore mesh topology...");
        var unoptResult = optimizer.Unoptimize(sourceBytes);
        if (!unoptResult.Success || unoptResult.OutputGlbBytes == null)
        {
            return new GlbAutoRiggerResult
            {
                Success = false,
                ErrorMessage = $"Failed to unoptimize {fullInputPath}: {unoptResult.ErrorMessage}"
            };
        }

        File.WriteAllBytes(rigSourcePath, unoptResult.OutputGlbBytes);
        return new GlbAutoRiggerResult { Success = true };
    }

    private static GlbAutoRiggerResult RunRiggingProcess(
        string fullInputPath,
        string fullOutputPath,
        string rigSourcePath,
        GlbAutoRiggerOptions options,
        Action<string> log)
    {
        var kwargs = new JsonObject
        {
            ["is_gs"] = false,
            ["no_fingers"] = options.NoFingers,
            ["input_normal"] = options.UseNormals,
            ["bw_fix"] = options.WeightPostprocess,
            ["reset_to_rest"] = true,
            ["inplace"] = true,
            ["animation_file"] = JsonValue.Create<string?>(null)
        };
        string kwargsJson = kwargs.ToJsonString();

        log($"Rigging: {fullInputPath}");
        log($"  Output:             {fullOutputPath}");
        log($"  no_fingers:         {options.NoFingers}");
        log($"  use_normals:        {options.UseNormals}");
        log($"  weight_postprocess: {options.WeightPostprocess}");

        var psi = new ProcessStartInfo
        {
            FileName = MakeItAnimatableSetup.PythonExePath,
            WorkingDirectory = MakeItAnimatableSetup.NodeDir,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add(MakeItAnimatableSetup.ServerScriptPath);
        psi.ArgumentList.Add("--input");
        psi.ArgumentList.Add(rigSourcePath);
        psi.ArgumentList.Add("--output");
        psi.ArgumentList.Add(fullOutputPath);
        psi.ArgumentList.Add("--kwargs");
        psi.ArgumentList.Add(kwargsJson);

        int exitCode = RunPipelineProcess(psi, log);

        bool outputCreated = File.Exists(fullOutputPath) && new FileInfo(fullOutputPath).Length > 0;

        if (exitCode != 0 && exitCode != -1073741819 && exitCode != unchecked((int)0xC0000005))
        {
            return new GlbAutoRiggerResult
            {
                Success = false,
                ErrorMessage = $"Make-It-Animatable pipeline exited with code {exitCode}."
            };
        }

        if (!outputCreated)
        {
            return new GlbAutoRiggerResult
            {
                Success = false,
                ErrorMessage = $"Output rigged file was not created: {fullOutputPath}"
            };
        }

        return new GlbAutoRiggerResult { Success = true };
    }

    private static GlbAutoRiggerResult ReoptimizeOutput(
        string fullOutputPath,
        GlbOptimizer optimizer,
        Action<string> log)
    {
        log("  Re-optimizing output (LODs regenerated from rigged bone structure)...");
        var optimizeResult = optimizer.OptimizeFile(
            fullOutputPath,
            fullOutputPath,
            new OptimizationOptions { ForceReDecimate = true });

        if (!optimizeResult.Success)
        {
            log($"  Warning: Re-optimization failed: {optimizeResult.ErrorMessage}");
            return new GlbAutoRiggerResult
            {
                Success = true,
                OutputPath = fullOutputPath,
                ErrorMessage = $"Rigged successfully, but re-optimization warning: {optimizeResult.ErrorMessage}"
            };
        }

        log($"  Successfully rigged and optimized: {fullOutputPath} ({optimizeResult.OriginalSize} -> {optimizeResult.OptimizedSize} bytes)");

        return new GlbAutoRiggerResult
        {
            Success = true,
            OutputPath = fullOutputPath
        };
    }

    private static void CleanupTempDir(string tempJobDir)
    {
        try
        {
            if (Directory.Exists(tempJobDir))
            {
                Directory.Delete(tempJobDir, true);
            }
        }
        catch { }
    }

    private static int RunPipelineProcess(ProcessStartInfo psi, Action<string> log)
    {
        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };

        if (!proc.Start())
        {
            log("Error: Failed to start Python process.");
            return -1;
        }

        if (psi.RedirectStandardInput)
        {
            proc.StandardInput.Close();
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        return proc.ExitCode;
    }
}

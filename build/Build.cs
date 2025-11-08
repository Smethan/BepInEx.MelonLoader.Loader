using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;

class Build : NukeBuild
{
	public static int Main () => Execute<Build>(x => x.Compile);

    public const string MLVersionName = "2.1.0";
    private const string ProjectName = "BepInEx.MelonLoader.Loader";

    private AbsolutePath OutputDir => RootDirectory / "Output";
    private AbsolutePath MelonloaderFilesPath => OutputDir / "MelonLoader";

    private static void Bash(string command)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new Exception($"Bash command failed: {command}\n{error}");
        }
    }

    private static string ConvertToWindowsPath(string wslPath)
    {
        if (wslPath.StartsWith("/mnt/c/"))
        {
            // Windows path mounted in WSL, convert to C:\...
            return "C:\\" + wslPath.Substring("/mnt/c/".Length).Replace("/", "\\");
        }
        else if (wslPath.StartsWith("/"))
        {
            // WSL filesystem path, use \\wsl.localhost\Ubuntu-22.04\...
            return "\\\\wsl.localhost\\Ubuntu-22.04" + wslPath.Replace("/", "\\");
        }
        return wslPath.Replace("/", "\\");
    }

    private static void CreateWindowsLink(AbsolutePath linkPath, AbsolutePath targetPath, bool isDirectory = false)
    {
        var windowsLinkPath = ConvertToWindowsPath(linkPath);
        var windowsTargetPath = ConvertToWindowsPath(targetPath);

        System.Diagnostics.Process process;

        if (isDirectory)
        {
            // Use junction for directories (doesn't require admin privileges)
            // cmd.exe /c mklink /J "link" "target"
            process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /J \"{windowsLinkPath}\" \"{windowsTargetPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
        }
        else
        {
            // Use hard link for files (doesn't require admin privileges)
            // Escape single quotes in paths by doubling them for PowerShell
            var escapedLinkPath = windowsLinkPath.Replace("'", "''");
            var escapedTargetPath = windowsTargetPath.Replace("'", "''");

            var psCommand = $"New-Item -ItemType HardLink -Path '{escapedLinkPath}' -Target '{escapedTargetPath}' -Force";

            process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{psCommand.Replace("\"", "`\"\"")}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
        }

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var linkType = isDirectory ? "junction" : "hard link";
            throw new Exception($"Failed to create {linkType}: {windowsLinkPath} -> {windowsTargetPath}\n{error}\n{output}");
        }
    }

    Target DownloadDependencies => _ => _
	    .After(Clean)
	    .Executes(async () =>
	    {
		    using var httpClient = new HttpClient();

		    var zipPath = OutputDir / "MelonLoader.x64.zip";

		    await using var fileStream = new FileStream(zipPath, FileMode.Create);

		    await using var downloadStream =
                    await httpClient.GetStreamAsync(
                        "https://github.com/LavaGang/MelonLoader/releases/download/v0.7.1/MelonLoader.x64.zip");

		    await downloadStream.CopyToAsync(fileStream);
			fileStream.Close();

			MelonloaderFilesPath.CreateOrCleanDirectory();
		    ZipFile.ExtractToDirectory(zipPath, MelonloaderFilesPath);
		    zipPath.DeleteFile();
	    });

    Target Clean => _ => _
	    .Executes(() =>
	    {
		    DotNetTasks.DotNetClean(x =>
			    x.SetProject(RootDirectory / $"{ProjectName}.UnityMono" / $"{ProjectName}.UnityMono.csproj"));

		    DotNetTasks.DotNetClean(x =>
			    x.SetProject(RootDirectory / $"{ProjectName}.IL2CPP" / $"{ProjectName}.IL2CPP.csproj"));

		    OutputDir.CreateOrCleanDirectory();
	    });

    private void HandleBuild(string projectSubname, string framework, string configuration, bool il2cpp)
    {
	    var stagingDirectory = OutputDir / "staging";
	    // Create BepInEx/plugins structure (r2modman will extract BepInEx folder to profile root)
	    var stagingBepInExPlugins = stagingDirectory / "BepInEx" / "plugins";
	    var stagingPluginPath = stagingBepInExPlugins / "BepInEx.MelonLoader.Loader";
	    var stagingMLPath = stagingBepInExPlugins / "MLLoader";

	    stagingPluginPath.CreateOrCleanDirectory();
	    stagingMLPath.CreateOrCleanDirectory();

	    (stagingMLPath / "MelonLoader").CreateDirectory();
	    (stagingMLPath / "Mods").CreateDirectory();
	    (stagingMLPath / "Plugins").CreateDirectory();
	    (stagingMLPath / "UserData").CreateDirectory();
	    (stagingMLPath / "UserLibs").CreateDirectory();

	    DotNetTasks.DotNetBuild(x =>
		    x.SetProjectFile(RootDirectory / $"{ProjectName}.{projectSubname}" / $"{ProjectName}.{projectSubname}.csproj")
			    .SetFramework(framework)
			    .SetConfiguration(configuration));

	    // Copy plugin DLLs to plugin subfolder
	    CopyDirectoryRecursively(
		    RootDirectory / $"{ProjectName}.{projectSubname}" / "Output" / configuration / projectSubname,
		    stagingPluginPath,
		    DirectoryExistsPolicy.Merge);

	    // Determine MelonLoader directory based on framework (net6 for IL2CPP, net35 for Mono)
	    var melonLoaderDllPath = MelonloaderFilesPath / "MelonLoader" / (il2cpp ? "net6" : "net35");

	    // Copy MelonLoader core DLLs to plugin folder
	    CopyFileToDirectory(melonLoaderDllPath / "MelonLoader.dll", stagingPluginPath);
	    CopyFileToDirectory(melonLoaderDllPath / "0Harmony.dll", stagingPluginPath);

	    // Copy support DLLs if they exist
	    var supportDlls = new[] {
		    "AssetRipper.Primitives.dll", "AssetsTools.NET.dll", "Tomlet.dll",
		    "WebSocketDotNet.dll", "bHapticsLib.dll"
	    };
	    foreach (var dll in supportDlls)
	    {
		    var dllPath = melonLoaderDllPath / dll;
		    if (File.Exists(dllPath))
			    CopyFileToDirectory(dllPath, stagingPluginPath);
	    }

	    var stagingMLDependencies = stagingMLPath / "MelonLoader" / "Dependencies";

		CopyDirectoryRecursively(MelonloaderFilesPath / "MelonLoader" / "Dependencies",
			stagingMLDependencies,
			DirectoryExistsPolicy.Merge);

		// Remove variant-specific files
		if (!il2cpp)
		{
			(stagingMLDependencies / "Il2CppAssemblyGenerator").DeleteDirectory();
			(stagingMLDependencies / "SupportModules" / "Il2Cpp.dll").DeleteFile();
			(stagingMLDependencies / "CompatibilityLayers" / "Il2CppUnityTls.dll").DeleteFile();
			(stagingMLDependencies / "CompatibilityLayers" / "Stress_Level_Zero_Il2Cpp.dll").DeleteFile();
		}
		else
		{
			(stagingMLDependencies / "SupportModules" / "Mono.dll").DeleteFile();
			(stagingMLDependencies / "CompatibilityLayers" / "IPA.dll").DeleteFile();
			(stagingMLDependencies / "CompatibilityLayers" / "Muse_Dash_Mono.dll").DeleteFile();
		}

		(stagingMLDependencies / "MonoBleedingEdge.x64").DeleteDirectory();
        (stagingMLDependencies / "Bootstrap.dll").DeleteFile();

		// Remove all debug symbols (.pdb, .mdb files) from entire staging directory
		stagingDirectory.GlobFiles("**/*.pdb").DeleteFiles();
		stagingDirectory.GlobFiles("**/*.mdb").DeleteFiles();
		stagingDirectory.GlobFiles("**/*.dll.mdb").DeleteFiles();

		// Remove .NET dependency files from plugins folder (not needed for Unity/Mono runtime)
		// BUT preserve deps.json in MLLoader/MelonLoader/Dependencies (required for Generator)
		stagingPluginPath.GlobFiles("**/*.deps.json").DeleteFiles();

		stagingDirectory.ZipTo(OutputDir / $"MLLoader-{projectSubname}-{configuration}-{MLVersionName}.zip");
		stagingDirectory.DeleteDirectory();
    }

    Target Compile => _ => _
	    .DependsOn(DownloadDependencies, Clean)
        .Executes(() =>
	    {
			HandleBuild("UnityMono", "net35", "BepInEx5", false);
			HandleBuild("UnityMono", "net35", "BepInEx6", false);
			HandleBuild("IL2CPP", "net6.0", "BepInEx6", true);

			MelonloaderFilesPath.DeleteDirectory();
	    });

    Target DevDeploy => _ => _
        .DependsOn(DownloadDependencies)
        .Executes(() =>
        {
            // NOTE: Symlinks/hardlinks don't work when repo is in WSL and target is on Windows C: drive
            // To enable linking: move repo to /mnt/c/... (Windows filesystem) and update CreateWindowsLink calls
            // For now, we copy files which still provides fast rebuild-and-deploy workflow

            // Build only IL2CPP for development
            var projectSubname = "IL2CPP";
            var framework = "net6.0";
            var configuration = "BepInEx6";

            var projectPath = RootDirectory / $"{ProjectName}.{projectSubname}" / $"{ProjectName}.{projectSubname}.csproj";
            var outputPath = RootDirectory / $"{ProjectName}.{projectSubname}" / "Output" / configuration / projectSubname;

            DotNetTasks.DotNetBuild(x =>
                x.SetProjectFile(projectPath)
                .SetConfiguration(configuration)
                .SetFramework(framework));

            // r2modman profile path
            var r2modmanPath = AbsolutePath.Create("/mnt/c/Users/Ethan/AppData/Roaming/r2modmanPlus-local/Megabonk/profiles/Default/BepInEx/plugins");
            var deployPath = r2modmanPath / "ElectricEspeon-MelonLoader_Loader";

            // Clean and create deploy directories
            if (deployPath.DirectoryExists())
                deployPath.DeleteDirectory();
            deployPath.CreateDirectory();

            var pluginDeployPath = deployPath / "BepInEx.MelonLoader.Loader";
            var mlDeployPath = deployPath / "MLLoader";

            pluginDeployPath.CreateDirectory();
            (mlDeployPath / "MelonLoader").CreateDirectory();
            (mlDeployPath / "Mods").CreateDirectory();
            (mlDeployPath / "Plugins").CreateDirectory();
            (mlDeployPath / "UserData").CreateDirectory();
            (mlDeployPath / "UserLibs").CreateDirectory();

            // Copy plugin DLLs
            Serilog.Log.Information("Copying plugin DLLs...");
            foreach (var file in outputPath.GlobFiles("*.dll"))
            {
                Serilog.Log.Information($"  Copying {file.Name}");
                CopyFileToDirectory(file, pluginDeployPath, FileExistsPolicy.Overwrite);
            }

            // Copy MelonLoader dependencies
            var sourceMLPath = MelonloaderFilesPath / "MelonLoader";
            var melonLoaderDll = sourceMLPath / (framework == "net6.0" ? "net6" : "net35") / "MelonLoader.dll";
            Serilog.Log.Information("Copying MelonLoader.dll...");
            CopyFileToDirectory(melonLoaderDll, pluginDeployPath, FileExistsPolicy.Overwrite);

            // Copy other required DLLs
            var supportDlls = new[] { "0Harmony.dll", "AssetRipper.Primitives.dll", "AssetsTools.NET.dll", "Tomlet.dll", "WebSocketDotNet.dll", "bHapticsLib.dll" };
            foreach (var dll in supportDlls)
            {
                var sourceDll = sourceMLPath / (framework == "net6.0" ? "net6" : "net35") / dll;
                if (sourceDll.FileExists())
                {
                    Serilog.Log.Information($"  Copying {dll}");
                    CopyFileToDirectory(sourceDll, pluginDeployPath, FileExistsPolicy.Overwrite);
                }
            }

            // Copy MelonLoader Dependencies folder
            Serilog.Log.Information("Copying MelonLoader Dependencies...");
            var sourceDepsPath = sourceMLPath / "Dependencies";
            var targetDepsPath = mlDeployPath / "MelonLoader" / "Dependencies";
            CopyDirectoryRecursively(sourceDepsPath, targetDepsPath, DirectoryExistsPolicy.Merge, FileExistsPolicy.Overwrite);

            Serilog.Log.Information($"✓ Development deployment complete!");
            Serilog.Log.Information($"  Plugin DLLs copied to: {pluginDeployPath}");
            Serilog.Log.Information($"  MelonLoader files copied to: {mlDeployPath}");
            Serilog.Log.Information($"  Run './build.sh DevDeploy' after code changes to quickly redeploy");
        });
}

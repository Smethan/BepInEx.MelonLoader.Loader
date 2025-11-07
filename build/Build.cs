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
	    var stagingBepInExPath = stagingDirectory / "BepInEx" / "plugins";
	    var stagingPluginPath = stagingBepInExPath / "BepInEx.MelonLoader.Loader";
	    var stagingMLPath = stagingDirectory / "MLLoader";

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

		stagingDirectory.ZipTo(OutputDir / $"MLLoader-{projectSubname}-{configuration}-{MLVersionName}.zip");
		stagingDirectory.DeleteDirectory();
    }

    Target Compile => _ => _
	    .DependsOn(DownloadDependencies, Clean)
        .Executes(() =>
	    {
			HandleBuild("UnityMono", "net35", "BepInEx5", false);
			HandleBuild("UnityMono", "net35", "BepInEx6", false);
			HandleBuild("IL2CPP", "netstandard2.1", "BepInEx6", true);

			MelonloaderFilesPath.DeleteDirectory();
	    });
}

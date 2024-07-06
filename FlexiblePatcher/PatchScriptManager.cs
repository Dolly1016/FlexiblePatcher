using HarmonyLib;
using Iced.Intel;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Loader;
using BepInEx.Logging;

namespace FlexiblePatcher;

public class PatchScriptManager
{
    private MD5 Hasher = MD5.Create();
    public class PatchScript
    {
        public string FilePath { get; private init; }
        public bool DirtyFlag { get; set; }
        public string Hash { get; set; }
        private Harmony Harmony { get; init; }
        private Assembly MyAssembly { get; set; }
        private static int AvailableId = 0;
        private static int AvailableAssemblyId = 0;
        public PatchScript(string filePath)
        {
            FilePath = filePath;
            Harmony = new Harmony("jp.dolly.flexiblePatcher." + (AvailableId++).ToString());
        }

        public void Unpatch()
        {
            Harmony.UnpatchSelf();
        }

        public bool RecompileAndPatch(BepInEx.Logging.ManualLogSource logger, CSharpParseOptions parseOptions, CSharpCompilationOptions compilationOptions, MetadataReference[] references)
        {
            Harmony.UnpatchSelf();
            
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(FilePath), parseOptions, FilePath, Encoding.UTF8);

            var name = "PatchScript." + (AvailableAssemblyId++);
            var compilation = CSharpCompilation.Create(name, [tree], references, compilationOptions.WithModuleName(name));

            Assembly? assembly = null;
            using (var stream = new MemoryStream())
            {
                var emitResult = compilation.Emit(stream);

                if (emitResult.Diagnostics.Length > 0)
                {
                    foreach (var diagnostic in emitResult.Diagnostics)
                    {
                        var pos = diagnostic.Location.GetLineSpan();
                        var location = "(" + pos.Path + " at line " + (pos.StartLinePosition.Line + 1) + ", character" + (pos.StartLinePosition.Character + 1) + ")";

                        var message = $"[{location}] {diagnostic.GetMessage()}";
                        switch (diagnostic.Severity)
                        {
                            case DiagnosticSeverity.Hidden:
                            case DiagnosticSeverity.Info:
                                logger.LogInfo(message);
                                break;
                            case DiagnosticSeverity.Warning:
                                logger.LogWarning(message);
                                break;
                            case DiagnosticSeverity.Error:
                                logger.LogError(message);
                                break;
                        }
                    }
                }

                if (emitResult.Success)
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
                }
                else
                {
                    logger.LogError("Compile Error! Patch is ignored (" + FilePath + ")");
                }
            }

            if (assembly != null)
            {
                MyAssembly = assembly;
                try
                {
                    Harmony.PatchAll(MyAssembly);
                }catch (Exception ex)
                {
                    logger.LogError(ex.ToString());
                    return false;
                }
                return true;
            }

            return false;
        }
    }
    private Dictionary<string, PatchScript> allScripts = new();

    public void UnpatchAndRemoveAll() {
        allScripts.Do(s => s.Value.Unpatch());
        allScripts.Clear();
    }

    private void DoForAllFiles(string directoryPath, Action<string> action)
    {
        foreach (var file in Directory.GetFiles(directoryPath)) action.Invoke(file);
        foreach (var dir in Directory.GetDirectories(directoryPath)) DoForAllFiles(dir, action);
    }

    public void Reload(ManualLogSource logger, string patchesFolderPath)
    {
        FlexiblePatcher.Plugin.Log.LogInfo("Start fetching and reloading...");

        //フォルダが存在しなければ何もしない。
        if (!Directory.Exists(patchesFolderPath))
        {
            logger.LogMessage("No path specified.");
            return;
        }

        allScripts.Do(s => s.Value.DirtyFlag = true);

        int success = 0;
        int error = 0;
        int noChange = 0;
        int removed = 0;

        DoForAllFiles(patchesFolderPath, path =>
        {
            if (!path.EndsWith(".cs")) return;//csファイルのみを対象とする。

            string hash = System.BitConverter.ToString(Hasher.ComputeHash(File.ReadAllBytes(path)));
            if(allScripts.TryGetValue(path, out var script))
            {
                if (script.Hash != hash)
                {
                    script.Hash = hash;
                    script.DirtyFlag = false;
                    if (script.RecompileAndPatch(logger, ParseOptions, CompilationOptions, ReferenceAssemblies))
                        success++;
                    else
                        error++;
                }
                else
                {
                    noChange++;
                }
            }
            else
            {
                PatchScript newScript = new(path);
                allScripts[path] = newScript;
                if(newScript.RecompileAndPatch(logger, ParseOptions, CompilationOptions, ReferenceAssemblies))
                    success++;
                else
                    error++;
                newScript.DirtyFlag = false;
            }
        });

        allScripts.Values.ToArray().Do(s =>
        {
            if (s.DirtyFlag)
            {
                s.Unpatch();
                allScripts.Remove(s.FilePath);
                removed++;
            }
        });

        logger.LogInfo($"Finish Patching! (success: {success}, failed: {error}, not-changed: {noChange}, removed: {removed})");
    }

    MetadataReference[] ReferenceAssemblies;
    CSharpParseOptions ParseOptions;
    CSharpCompilationOptions CompilationOptions;
    private void UpdateReference()
    {
        ReferenceAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => { try { return ((a.Location?.Length ?? 0) > 0); } catch { return false; } }).Select(a => MetadataReference.CreateFromFile(a.Location)).ToArray();
    }

    public PatchScriptManager()
    {
        UpdateReference();
        ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        CompilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithUsings("System", "System.Linq", "System.Collections.Generic")
            .WithNullableContextOptions(NullableContextOptions.Enable)
            .WithOptimizationLevel(OptimizationLevel.Release);
    }
}
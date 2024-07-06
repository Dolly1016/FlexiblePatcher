using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlexiblePatcher;

public static class StreamHelper
{
    public static byte[] ReadBytes(this Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}

[BepInPlugin("jp.dolly.flexiblePatcher", "FlexiblePatcher", "1.0.0")]
[BepInProcess("Among Us.exe")]
public class FlexiblePatcher : BasePlugin
{
    public static string PatchesFolderPath = "";
    private PatchScriptManager Patcher = null!;
    public static FlexiblePatcher Plugin;
    
    public void FetchAndReloadAll()
    {
        Patcher.Reload(Log, PatchesFolderPath);
    }

    public override void Load()
    {
        Plugin = this;

        //必要なアセンブリをロード
        Assembly.Load(Assembly.GetExecutingAssembly().GetManifestResourceStream("FlexiblePatcher.Resources.System.Collections.Immutable.dll")!.ReadBytes());
        Assembly.Load(Assembly.GetExecutingAssembly().GetManifestResourceStream("FlexiblePatcher.Resources.System.Reflection.Metadata.dll")!.ReadBytes());
        Assembly.Load(Assembly.GetExecutingAssembly().GetManifestResourceStream("FlexiblePatcher.Resources.Microsoft.CodeAnalysis.dll")!.ReadBytes());
        Assembly.Load(Assembly.GetExecutingAssembly().GetManifestResourceStream("FlexiblePatcher.Resources.Microsoft.CodeAnalysis.CSharp.dll")!.ReadBytes());

        PatchesFolderPath = Config.Bind<string>("Patching", "PatchesFolderPath", "", "Please specify the file containing the patch.").Value;

        SceneManager.sceneLoaded += (UnityEngine.Events.UnityAction<Scene, LoadSceneMode>)((scene, loadMode) =>
        {
            //シーン読み込みの瞬間までパッチングを遅延
            if(Patcher == null)
            {
                Patcher = new PatchScriptManager();
                FetchAndReloadAll();
            }

            new UnityEngine.GameObject("PatcherListener").AddComponent<PatcherListener>();

        });
    }
}

public class PatcherListener : MonoBehaviour
{
    static PatcherListener()=> ClassInjector.RegisterTypeInIl2Cpp<PatcherListener>();

    public void Update()
    {
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.F12))
        {
            FlexiblePatcher.Plugin.FetchAndReloadAll();
        }
    }

}

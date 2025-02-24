using System.IO;
using System.Reflection;
using UnityEngine;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HarmonyLib.Tools;
using GameNetcodeStuff;

[BepInPlugin("com.github.SimonTendo.LCBeatBoxerMod", "LCBeatBoxerMod", "0.2.1")]
public class Plugin : BaseUnityPlugin
{
    internal new static ManualLogSource Logger;
    public static AssetBundle assetBundle;
    public static AllAssets allAssets;
    public static Configs myConfig { get; internal set; }

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo("Plugin LCBeatBoxerMod is loaded!");

        assetBundle = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "beatboxerassetbundle"));
        if (assetBundle == null)
        {
            Logger.LogError("Failed to load LCBeatBoxerMod AssetBundle");
            return;
        }
        Logger.LogInfo("Loaded LCBeatBoxerMod AssetBundle");
        allAssets = assetBundle.LoadAsset<AllAssets>("Assets/LCBeatBoxerMod/ScriptableObjects/AllAssets.asset");

        myConfig = new(base.Config);
        Configs.DisplayConfigs();

        Harmony harmony = new Harmony("LCBeatBoxerMod");
        harmony.PatchAll();
        HarmonyFileLog.Enabled = false;

        UnityNetcodePatcher();
    }

    public static bool ConvertToBool(int value)
    {
        return value == 1;
    }

    public static bool ConvertToBool(string value)
    {
        return value.ToLower() == "true";
    }

    public static int DebugLogLevel()
    {
        if (Configs.printDebugEnemyAI.Value)
        {
            Logger.LogDebug($"[Print debugEnemyAI]: {Configs.printDebugEnemyAI.Value} || DebugEnemy: TRUE || debugEnemyAI: TRUE");
            return 2;
        }
        if (Application.isEditor)
        {
            Logger.LogDebug($"isEditor: {Application.isEditor} || DebugEnemy: TRUE || debugEnemyAI: FALSE");
            return 1;
        }
        else if (Configs.printDebugEnemy.Value)
        {
            Logger.LogDebug($"[Print DebugEnemy]: {Configs.printDebugEnemy.Value} || DebugEnemy: TRUE || debugEnemyAI: FALSE");
            return 1;
        }
        else
        {
            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player != null && player.playerUsername != null && player.playerUsername == "simtendo")
                {
                    Logger.LogDebug($"playing with {player.playerUsername} || DebugEnemy: TRUE || debugEnemyAI: FALSE");
                    return 1;
                }
            }
        }
        return 0;
    }

    private static void UnityNetcodePatcher()
    {
        //Method courtesy of Evaisa and the Lethal Company Modding Wiki
        var types = Assembly.GetExecutingAssembly().GetTypes();
        foreach (var type in types)
        {
            var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var method in methods)
            {
                var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                if (attributes.Length > 0)
                {
                    method.Invoke(null, null);
                }
            }
        }
    }
}

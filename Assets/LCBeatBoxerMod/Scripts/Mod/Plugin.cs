using System.IO;
using System.Reflection;
using UnityEngine;
using GameNetcodeStuff;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HarmonyLib.Tools;

[BepInPlugin("com.github.SimonTendo.LCBeatBoxerMod", "LCBeatBoxerMod", "0.3.1")]
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

        myConfig = new(Config);
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

    public static int GetEnemyIndexInRoundManager(EnemyAI enemy)
    {
        int toReturn = 0;
        if (enemy != null)
        {
            for (int i = 0; i < RoundManager.Instance.SpawnedEnemies.Count; i++)
            {
                if (RoundManager.Instance.SpawnedEnemies[i] == enemy)
                {
                    toReturn = i;
                    break;
                }
            }
        }
        return toReturn;
    }

    public static int DebugLogLevel()
    {
        int toReturn = 0;
        if (Configs.printDebugEnemyAI.Value)
        {
            toReturn = 2;
            Logger.LogDebug($"[Print debugEnemyAI]: {Configs.printDebugEnemyAI.Value} || toReturn = {toReturn}");
            return toReturn;
        }
        if (Application.isEditor)
        {
            toReturn = 1;
            Logger.LogDebug($"isEditor: {Application.isEditor} || toReturn = {toReturn}");
            return 1;
        }
        else if (Configs.printDebugEnemy.Value)
        {
            toReturn = 1;
            Logger.LogDebug($"[Print DebugEnemy]: {Configs.printDebugEnemy.Value} || toReturn = {toReturn}");
            return 1;
        }
        else
        {
            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player != null && player.playerUsername != null && player.playerUsername == "simtendo")
                {
                    toReturn = 1;
                    Logger.LogDebug($"playing with {player.playerUsername} || toReturn = {toReturn}");
                    return toReturn;
                }
            }
        }
        Logger.LogDebug($"toReturn = {toReturn}");
        return toReturn;
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

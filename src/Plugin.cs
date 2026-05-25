using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AddressableAssets;
using System.IO;
using System.Collections.Generic;
using System;

namespace SubnauticaRegenMod
{
    [BepInPlugin("com.harold.subnauticaregen", "Subnautica Infinite Resources", "1.0.0")]
    public class MainPlugin : BaseUnityPlugin
    {
        public static string SaveFilePath => Path.Combine(Paths.PluginPath, "SubnauticaRegenMod", "resources_queue.json");
        public static BepInEx.Logging.ManualLogSource? ModLogger;

        private void Awake()
        {
            ModLogger = Logger;
            var harmony = new Harmony("com.harold.subnauticaregen");
            harmony.PatchAll();
            ModLogger.LogInfo("¡Módulo 1 de Regeneración Completo (Estable) inicializado!");
            
            string? directory = Path.GetDirectoryName(SaveFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var regenController = new GameObject("RegenMod_Controller");
            regenController.AddComponent<ResourceRegenComponent>();
            DontDestroyOnLoad(regenController);
        }
    }

    [System.Serializable]
    public class SavedResource
    {
        public string techType = string.Empty;
        public float posX;
        public float posY;
        public float posZ;
        public float rotX;
        public float rotY;
        public float rotZ;
        public float rotW;
        public long respawnTimestamp;
    }

    public class ResourceRegenComponent : MonoBehaviour
    {
        private static readonly object FileLock = new object();

        private void Start()
        {
            InvokeRepeating(nameof(CheckQueue), 10f, 15f);
            InvokeRepeating(nameof(EjecutarEscaneoDelPasado), 12f, 4f); // Arranca a los 12s, repite cada 4s
        }

        private void CheckQueue()
        {
            if (!File.Exists(MainPlugin.SaveFilePath)) return;
            
            // Validación nativa segura de Unity: si el player está activo en la jerarquía, el mapa ya cargó
            if (Player.main == null || !Player.main.isActiveAndEnabled) return;

            long currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            List<SavedResource> queue = new List<SavedResource>();
            List<SavedResource> remaining = new List<SavedResource>();
            List<SavedResource> toRespawn = new List<SavedResource>();

            lock (FileLock)
            {
                try
                {
                    string existingJson = File.ReadAllText(MainPlugin.SaveFilePath);
                    string wrapper = $"{{\"items\":{existingJson}}}";
                    var loadedData = JsonUtility.FromJson<JsonListWrapper<SavedResource>>(wrapper);
                    if (loadedData != null && loadedData.items != null)
                    {
                        queue = loadedData.items;
                    }
                }
                catch (Exception ex)
                {
                    MainPlugin.ModLogger?.LogError($"Error leyendo JSON en ciclo de chequeo: {ex.Message}");
                    return;
                }

                foreach (var res in queue)
                {
                    if (currentUnixTime >= res.respawnTimestamp)
                    {
                        toRespawn.Add(res);
                    }
                    else
                    {
                        remaining.Add(res);
                    }
                }

                if (toRespawn.Count > 0)
                {
                    try
                    {
                        string rawJson = JsonUtility.ToJson(new JsonListWrapper<SavedResource> { items = remaining }, true);
                        int firstBracket = rawJson.IndexOf('[');
                        int lastBracket = rawJson.LastIndexOf(']');
                        if (firstBracket != -1 && lastBracket != -1)
                        {
                            string cleanJson = rawJson.Substring(firstBracket, lastBracket - firstBracket + 1);
                            File.WriteAllText(MainPlugin.SaveFilePath, cleanJson);
                        }
                    }
                    catch (Exception ex)
                    {
                        MainPlugin.ModLogger?.LogError($"Error actualizando JSON en ciclo de chequeo: {ex.Message}");
                        return;
                    }
                }
            }

            foreach (var res in toRespawn)
            {
                if (Enum.TryParse(res.techType, out TechType type))
                {
                    Vector3 position = new Vector3(res.posX, res.posY, res.posZ);
                    Quaternion rotation = new Quaternion(res.rotX, res.rotY, res.rotZ, res.rotW);

                    if (Vector3.Distance(Player.main.transform.position, position) > 150f)
                    {
                        Reenqueue(res);
                        continue;
                    }

                    SpawnResource(type, position, rotation);
                }
            }
        }

        private void EjecutarEscaneoDelPasado()
        {
            // Si el jugador no está listo o activo en el mapa, evitamos evaluar celdas
            if (Player.main == null || !Player.main.isActiveAndEnabled) return;

            if (LargeWorldStreamer.main != null && LargeWorldStreamer.main.cellManager != null)
            {
                try
                {
                    PastReconstructor.EvaluarCeldasActivas(LargeWorldStreamer.main.cellManager);
                }
                catch (Exception ex)
                {
                    MainPlugin.ModLogger?.LogError($"[RegenMod] Error en ciclo de Módulo 2: {ex.Message}");
                }
            }
        }

        private void SpawnResource(TechType type, Vector3 position, Quaternion rotation)
        {
            string prefabClassId = CraftData.GetClassIdForTechType(type);
            if (string.IsNullOrEmpty(prefabClassId)) return;

            Addressables.InstantiateAsync(prefabClassId, position, rotation).Completed += (handle) =>
            {
                GameObject spawned = handle.Result;
                if (spawned != null)
                {
                    MainPlugin.ModLogger?.LogInfo($"[RegenMod] Reaparecido exitosamente: {type} en {position}");
                }
                else
                {
                    MainPlugin.ModLogger?.LogError($"[RegenMod] Falló instanciar el asset con ID {prefabClassId}");
                }
            };
        }

        private void Reenqueue(SavedResource res)
        {
            lock (FileLock)
            {
                List<SavedResource> queue = new List<SavedResource>();
                if (File.Exists(MainPlugin.SaveFilePath))
                {
                    string existingJson = File.ReadAllText(MainPlugin.SaveFilePath);
                    string wrapper = $"{{\"items\":{existingJson}}}";
                    var loadedData = JsonUtility.FromJson<JsonListWrapper<SavedResource>>(wrapper);
                    if (loadedData != null && loadedData.items != null) queue = loadedData.items;
                }
                res.respawnTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;
                queue.Add(res);

                string rawJson = JsonUtility.ToJson(new JsonListWrapper<SavedResource> { items = queue }, true);
                int firstBracket = rawJson.IndexOf('[');
                int lastBracket = rawJson.LastIndexOf(']');
                if (firstBracket != -1 && lastBracket != -1)
                {
                    string cleanJson = rawJson.Substring(firstBracket, lastBracket - firstBracket + 1);
                    File.WriteAllText(MainPlugin.SaveFilePath, cleanJson);
                }
            }
        }
    }

    [HarmonyPatch(typeof(BreakableResource))]
    [HarmonyPatch(nameof(BreakableResource.BreakIntoResources))]
    public static class BreakableResource_Patch
    {
        private static readonly object FileLock = new object();

        [HarmonyPrefix]
        public static void Prefix(BreakableResource __instance)
        {
            bool isBroken = Traverse.Create(__instance).Field("broken").GetValue<bool>();
            if (isBroken) return;

            Vector3 position = __instance.transform.position;
            Quaternion rotation = __instance.transform.rotation;
            string resourceType = __instance.defaultPrefabTechType.ToString();

            long currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long respawnTime = currentUnixTime + 300;

            SavedResource newResource = new SavedResource
            {
                techType = resourceType,
                posX = position.x,
                posY = position.y,
                posZ = position.z,
                rotX = rotation.x,
                rotY = rotation.y,
                rotZ = rotation.z,
                rotW = rotation.w,
                respawnTimestamp = respawnTime
            };

            lock (FileLock)
            {
                List<SavedResource> queue = new List<SavedResource>();

                if (File.Exists(MainPlugin.SaveFilePath))
                {
                    try
                    {
                        string existingJson = File.ReadAllText(MainPlugin.SaveFilePath);
                        string wrapper = $"{{\"items\":{existingJson}}}";
                        var loadedData = JsonUtility.FromJson<JsonListWrapper<SavedResource>>(wrapper);
                        if (loadedData != null && loadedData.items != null) queue = loadedData.items;
                    }
                    catch (Exception ex)
                    {
                        MainPlugin.ModLogger?.LogError($"Error leyendo el JSON de persistencia: {ex.Message}");
                    }
                }

                queue.Add(newResource);

                try
                {
                    string rawJson = JsonUtility.ToJson(new JsonListWrapper<SavedResource> { items = queue }, true);
                    int firstBracket = rawJson.IndexOf('[');
                    int lastBracket = rawJson.LastIndexOf(']');
                    if (firstBracket != -1 && lastBracket != -1)
                    {
                        string cleanJson = rawJson.Substring(firstBracket, lastBracket - firstBracket + 1);
                        File.WriteAllText(MainPlugin.SaveFilePath, cleanJson);
                        MainPlugin.ModLogger?.LogInfo($"[RegenMod] Sincronizado {resourceType} en cola JSON.");
                    }
                }
                catch (Exception ex)
                {
                    MainPlugin.ModLogger?.LogError($"Error writing to serialization file: {ex.Message}");
                }
            }
        }
    }

    [System.Serializable]
    public class JsonListWrapper<T>
    {
        public List<T> items = new List<T>();
    }
}

using HarmonyLib;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace SubnauticaRegenMod
{
    // Estructura ligera para memorizar la firma completa del recurso en la plantilla
    public struct ResourceTemplate
    {
        public TechType tipo;
        public Vector3 posicion;
        public Quaternion rotacion;
    }

    public static class PastReconstructor
    {
        private static readonly Dictionary<string, List<ResourceTemplate>> CeldasRegistradas = new Dictionary<string, List<ResourceTemplate>>();
        private const float ToleranciaDistancia = 0.5f;

        public static void EvaluarCeldasActivas(CellManager cellManager)
        {
            if (cellManager == null) return;

            var batch2cells = Traverse.Create(cellManager).Field("batch2cells").GetValue();
            if (batch2cells == null) return;

            foreach (var kvp in (IEnumerable)batch2cells)
            {
                var batchCellsInstance = Traverse.Create(kvp).Property("Value").GetValue();
                if (batchCellsInstance == null) return;

                var allCells = Traverse.Create(batchCellsInstance).Method("All").GetValue();
                if (allCells == null) return;

                foreach (var cell in (IEnumerable)allCells)
                {
                    if (cell == null) continue;

                    var stateValue = Traverse.Create(cell).Property("CurrentState").GetValue();
                    if (stateValue == null || stateValue.ToString() != "IsAwake") continue;

                    GameObject liveRoot = Traverse.Create(cell).Field("liveRoot").GetValue<GameObject>();
                    if (liveRoot == null || liveRoot.transform == null) continue;

                    ProcesarPresenciaDeRecursos(liveRoot.transform);
                }
            }
        }

        private static void ProcesarPresenciaDeRecursos(Transform cellRoot)
        {
            Vector3 posCelda = cellRoot.position;
            string idCelda = $"cell_{(int)posCelda.x}_{(int)posCelda.y}_{(int)posCelda.z}";

            BreakableResource[] recursosVivos = cellRoot.GetComponentsInChildren<BreakableResource>(true);

            // CASO A: Registro inicial de la plantilla de la celda
            if (!CeldasRegistradas.ContainsKey(idCelda))
            {
                List<ResourceTemplate> plantillaOriginal = new List<ResourceTemplate>();
                
                foreach (var piedra in recursosVivos)
                {
                    plantillaOriginal.Add(new ResourceTemplate 
                    { 
                        tipo = piedra.defaultPrefabTechType, 
                        posicion = piedra.transform.position,
                        rotacion = piedra.transform.rotation
                    });
                }

                CeldasRegistradas.Add(idCelda, plantillaOriginal);
                MainPlugin.ModLogger?.LogInfo($"[RegenMod] Celda {idCelda} registrada. Plantilla creada con {plantillaOriginal.Count} afloramientos.");
                return;
            }

            // CASO B: Comparación molecular de presencia
            List<ResourceTemplate> posicionesOriginales = CeldasRegistradas[idCelda];
            List<ResourceTemplate> recursosABorrarDePlantilla = new List<ResourceTemplate>();

            foreach (ResourceTemplate nodoEsperado in posicionesOriginales)
            {
                bool existeEnLaRealidad = false;

                foreach (var piedraReal in recursosVivos)
                {
                    if (Vector3.Distance(nodoEsperado.posicion, piedraReal.transform.position) <= ToleranciaDistancia)
                    {
                        existeEnLaRealidad = true;
                        break;
                    }
                }

                // ¡Detectamos un vacío del pasado!
                if (!existeEnLaRealidad)
                {
                    MainPlugin.ModLogger?.LogWarning($"[RegenMod] Recurso faltante detectado en {idCelda}. Programando reaparición en 5 min para: {nodoEsperado.tipo}");
                    
                    // Aquí llamamos al método local
                    InyectarRecursoFaltanteDelPasado(nodoEsperado.tipo, nodoEsperado.posicion, nodoEsperado.rotacion);
                    
                    // Lo marcamos para sacarlo de la plantilla y que no repita el log cada 4 segundos
                    recursosABorrarDePlantilla.Add(nodoEsperado);
                }
            }

            // Limpieza de la plantilla en memoria
            if (recursosABorrarDePlantilla.Count > 0)
            {
                foreach (var plantillaBorrar in recursosABorrarDePlantilla)
                {
                    posicionesOriginales.Remove(plantillaBorrar);
                }
            }
        }

        // EL MÉTODO QUE FALTABA: Escribe directo en tu cola JSON usando el candado unificado de tu componente
        private static void InyectarRecursoFaltanteDelPasado(TechType tipo, Vector3 pos, Quaternion rot)
        {
            SavedResource newResource = new SavedResource
            {
                techType = tipo.ToString(),
                posX = pos.x, posY = pos.y, posZ = pos.z,
                rotX = rot.x, rotY = rot.y, rotZ = rot.z, rotW = rot.w,
                minutosRestantes = 5 // Tus 5 minutos reglamentarios
            };

            lock (ResourceRegenComponent.FileLock) // Candado estático de tu original Plugin.cs
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
                    catch { }
                }

                // Evitar registrar la misma coordenada por duplicado
                foreach (var item in queue)
                {
                    if (Vector3.Distance(new Vector3(item.posX, item.posY, item.posZ), pos) < 0.5f) return;
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
                    }
                }
                catch (Exception ex)
                {
                    MainPlugin.ModLogger?.LogError($"[RegenMod] Error al escribir recurso del pasado en JSON: {ex.Message}");
                }
            }
        }
    }
}
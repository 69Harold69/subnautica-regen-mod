using HarmonyLib;
using UnityEngine;
using System;
using System.Collections;

namespace SubnauticaRegenMod
{
    public static class PastReconstructor
    {
        // Este método será invocado de forma segura desde nuestro componente central
        public static void EvaluarCeldasActivas(CellManager cellManager)
        {
            if (cellManager == null) return;

            // Extraemos el diccionario privado batch2cells de forma segura con Traverse
            var batch2cells = Traverse.Create(cellManager).Field("batch2cells").GetValue();
            if (batch2cells == null) return;

            // Recorremos el diccionario usando un bucle agnóstico de tipos
            foreach (var kvp in (IEnumerable)batch2cells)
            {
                // Obtenemos la propiedad .Value (BatchCells) de forma dinámica
                var batchCellsInstance = Traverse.Create(kvp).Property("Value").GetValue();
                if (batchCellsInstance == null) return;

                // CORRECCIÓN: En Harmony, se usa Method().GetValue() para ejecutar y obtener el retorno del método
                var allCells = Traverse.Create(batchCellsInstance).Method("All").GetValue();
                if (allCells == null) return;

                foreach (var cell in (IEnumerable)allCells)
                {
                    if (cell == null) continue;

                    // Interrogamos la propiedad interna que acabamos de validar con sed
                    var stateValue = Traverse.Create(cell).Property("CurrentState").GetValue();
                    if (stateValue == null || stateValue.ToString() != "IsAwake") continue;

                    // Si está despierta, extraemos de forma segura el liveRoot
                    GameObject liveRoot = Traverse.Create(cell).Field("liveRoot").GetValue<GameObject>();
                    if (liveRoot == null || liveRoot.transform == null) continue;

                    // Ejecutamos el análisis de presencia sin colisiones
                    ProcesarPresenciaDeRecursos(liveRoot.transform);
                }
            }
        }

        private static void ProcesarPresenciaDeRecursos(Transform cellRoot)
        {
            // Escaneo molecular de control: Contamos cuántos elementos visuales heredados existen
            int entidadesVivas = cellRoot.childCount;
            
            // Por ahora, registramos en el Log que el Módulo 2 está leyendo el interior de la celda de forma exitosa
            MainPlugin.ModLogger?.LogInfo($"[RegenMod] Módulo 2 analizó liveRoot awake de forma segura. Entidades presentes: {entidadesVivas}");
        }
    }
}

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
            // Buscamos de golpe todos los componentes BreakableResource en los hijos de la celda
            BreakableResource[] recursosVivos = cellRoot.GetComponentsInChildren<BreakableResource>(true);

            if (recursosVivos.Length > 0)
            {
                MainPlugin.ModLogger?.LogInfo($"[RegenMod] Escaneando celda. Encontrados {recursosVivos.Length} afloramientos vivos:");
                
                foreach (BreakableResource piedra in recursosVivos)
                {
                    // Extraemos su tipo por defecto y su posición global fija
                    TechType tipo = piedra.defaultPrefabTechType;
                    Vector3 pos = piedra.transform.position;

                    MainPlugin.ModLogger?.LogInfo($" -> Recurso: {tipo} en posición: X:{pos.x:F1}, Y:{pos.y:F1}, Z:{pos.z:F1}");
                }
            }
        }
    }
}
